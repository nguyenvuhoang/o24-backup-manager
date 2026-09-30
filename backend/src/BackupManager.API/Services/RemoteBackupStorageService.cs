using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;

namespace BackupManager.API.Services;

public sealed record RemoteBackupUploadResult(
    string Destination,
    string UploadOutput,
    string VerificationOutput
);

public sealed class RemoteBackupStorageService
{
    private readonly RemoteBackupStorageOptions _options;
    private readonly ILogger<RemoteBackupStorageService> _logger;

    public RemoteBackupStorageService(
        IOptions<RemoteBackupStorageOptions> options,
        ILogger<RemoteBackupStorageService> logger
    )
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool Enabled => _options.Enabled;

    public async Task TestAsync(CancellationToken ct)
    {
        if (!Enabled)
            return;

        ValidateConfiguration();

        var remotePath = BuildRclonePath(_options.RootPath);

        var command = $"rclone lsd {ShellQuote(remotePath)}";

        var result = await ExecuteSshAsync(
            command,
            TimeSpan.FromSeconds(Math.Max(10, _options.ConnectTimeoutSeconds)),
            ct
        );

        EnsureSuccess(result, "Không thể kết nối Google Drive qua rclone.");
    }

    public async Task<RemoteBackupUploadResult> UploadAndVerifyAsync(
        string sourceDirectory,
        string jobName,
        DateTimeOffset startedAt,
        string runFolderName,
        CancellationToken ct
    )
    {
        if (!Enabled)
        {
            throw new InvalidOperationException("Remote backup storage chưa được bật.");
        }

        ValidateConfiguration();

        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            throw new ArgumentException(
                "Source backup directory is required.",
                nameof(sourceDirectory)
            );
        }

        var safeJobName = SanitizeRemoteSegment(jobName);

        var destinationRelative = CombineRemotePath(
            _options.RootPath,
            safeJobName,
            startedAt.UtcDateTime.ToString("yyyy", CultureInfo.InvariantCulture),
            startedAt.UtcDateTime.ToString("MM", CultureInfo.InvariantCulture),
            SanitizeRemoteSegment(runFolderName)
        );

        var destination = BuildRclonePath(destinationRelative);

        _logger.LogInformation(
            "Uploading SQL Server backup directory {SourceDirectory} to {Destination}.",
            sourceDirectory,
            destination
        );

        var uploadCommand =
            $"rclone copy {ShellQuote(sourceDirectory)} {ShellQuote(destination)} "
            + "--create-empty-src-dirs "
            + "--stats=10s "
            + "--stats-one-line "
            + "--retries=3 "
            + "--low-level-retries=10";

        var uploadResult = await ExecuteSshAsync(
            uploadCommand,
            TimeSpan.FromMinutes(Math.Max(1, _options.UploadTimeoutMinutes)),
            ct
        );

        EnsureSuccess(uploadResult, $"Google Drive upload thất bại. Destination: {destination}");

        var verificationOutput = string.Empty;

        if (_options.VerifyAfterUpload)
        {
            _logger.LogInformation(
                "Verifying uploaded backup directory {Destination}.",
                destination
            );

            var verifyCommand =
                $"rclone check {ShellQuote(sourceDirectory)} {ShellQuote(destination)} "
                + "--one-way";

            var verifyResult = await ExecuteSshAsync(
                verifyCommand,
                TimeSpan.FromMinutes(Math.Max(1, _options.UploadTimeoutMinutes)),
                ct
            );

            EnsureSuccess(
                verifyResult,
                $"Google Drive verification thất bại. Destination: {destination}"
            );

            verificationOutput = CombineOutput(verifyResult);
        }

        return new RemoteBackupUploadResult(
            destination,
            CombineOutput(uploadResult),
            verificationOutput
        );
    }

    private async Task<SshCommandResult> ExecuteSshAsync(
        string remoteCommand,
        TimeSpan timeout,
        CancellationToken ct
    )
    {
        ValidateConfiguration();

        var sshExecutable = ResolveSshExecutable();

        var startInfo = new ProcessStartInfo
        {
            FileName = sshExecutable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(
            Environment.ExpandEnvironmentVariables(_options.SshPrivateKeyPath)
        );

        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add(_options.SshPort.ToString(CultureInfo.InvariantCulture));

        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("BatchMode=yes");

        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add($"ConnectTimeout={Math.Max(5, _options.ConnectTimeoutSeconds)}");

        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("StrictHostKeyChecking=accept-new");

        startInfo.ArgumentList.Add($"{_options.SshUser}@{_options.SshHost}");

        startInfo.ArgumentList.Add(remoteCommand);

        using var process = new Process { StartInfo = startInfo };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Không thể khởi động ssh.exe.");
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Không thể khởi động SSH client '{sshExecutable}'. "
                    + "Kiểm tra Windows OpenSSH Client.",
                ex
            );
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);

        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        timeoutCts.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            TryKill(process);

            throw new TimeoutException(
                $"SSH command timeout sau {timeout.TotalMinutes:0.##} phút."
            );
        }
        catch
        {
            TryKill(process);
            throw;
        }

        var stdout = await stdoutTask;

        var stderr = await stderrTask;

        return new SshCommandResult(process.ExitCode, stdout, stderr);
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.SshHost))
        {
            throw new InvalidOperationException(
                $"{RemoteBackupStorageOptions.SectionName}:SshHost chưa được cấu hình."
            );
        }

        if (string.IsNullOrWhiteSpace(_options.SshUser))
        {
            throw new InvalidOperationException(
                $"{RemoteBackupStorageOptions.SectionName}:SshUser chưa được cấu hình."
            );
        }

        if (string.IsNullOrWhiteSpace(_options.SshPrivateKeyPath))
        {
            throw new InvalidOperationException(
                $"{RemoteBackupStorageOptions.SectionName}:SshPrivateKeyPath chưa được cấu hình."
            );
        }

        var keyPath = Environment.ExpandEnvironmentVariables(_options.SshPrivateKeyPath);

        if (!File.Exists(keyPath))
        {
            throw new FileNotFoundException($"SSH private key không tồn tại: {keyPath}", keyPath);
        }

        if (string.IsNullOrWhiteSpace(_options.RcloneRemote))
        {
            throw new InvalidOperationException(
                $"{RemoteBackupStorageOptions.SectionName}:RcloneRemote chưa được cấu hình."
            );
        }
    }

    private string BuildRclonePath(string relativePath)
    {
        var remote = _options.RcloneRemote.Trim().TrimEnd(':');

        var path = relativePath.Trim().Trim('/');

        return string.IsNullOrWhiteSpace(path) ? $"{remote}:" : $"{remote}:{path}";
    }

    private static string CombineRemotePath(params string[] segments)
    {
        return string.Join(
            "/",
            segments.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim().Trim('/', '\\'))
        );
    }

    private static string SanitizeRemoteSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unnamed";

        var invalid = new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };

        var builder = new StringBuilder(value.Length);

        foreach (var ch in value.Trim())
        {
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        }

        return builder.ToString();
    }

    private static string ShellQuote(string value)
    {
        return "'" + value.Replace("'", "'\"'\"'") + "'";
    }

    private static void EnsureSuccess(SshCommandResult result, string message)
    {
        if (result.ExitCode == 0)
            return;

        var output = CombineOutput(result);

        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(output)
                ? $"{message} SSH exit code: {result.ExitCode}."
                : $"{message} SSH exit code: {result.ExitCode}. {output}"
        );
    }

    private static string CombineOutput(SshCommandResult result)
    {
        var parts = new[] { result.StandardOutput?.Trim(), result.StandardError?.Trim() }.Where(x =>
            !string.IsNullOrWhiteSpace(x)
        );

        return string.Join(Environment.NewLine, parts);
    }

    private static string ResolveSshExecutable()
    {
        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);

        var windowsSsh = Path.Combine(systemDirectory, "OpenSSH", "ssh.exe");

        return File.Exists(windowsSsh) ? windowsSsh : "ssh.exe";
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort only.
        }
    }

    private sealed record SshCommandResult(
        int ExitCode,
        string StandardOutput,
        string StandardError
    );
}
