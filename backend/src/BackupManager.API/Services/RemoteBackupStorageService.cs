using BackupManager.Domain.Models;
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

    /// <summary>
    /// Global remote storage switch.
    ///
    /// Kept for backward compatibility only.
    /// New connection-specific logic should use IsEnabled().
    /// </summary>
    public bool Enabled => _options.Enabled;

    /// <summary>
    /// Determines whether remote storage is enabled for a connection.
    ///
    /// If the connection has no BackupTransport configuration,
    /// the legacy/global RemoteStorage configuration is used.
    /// </summary>
    public bool IsEnabled(BackupTransportSettings? transport)
    {
        return transport?.Enabled ?? _options.Enabled;
    }

    /// <summary>
    /// Tests the legacy/global remote storage configuration.
    /// Existing /storage/test endpoint can continue using this method.
    /// </summary>
    public async Task TestAsync(CancellationToken ct)
    {
        if (!_options.Enabled)
            return;

        var effective = ResolveEffectiveSettings(null);

        ValidateConfiguration(effective);

        var remotePath = BuildRclonePath(
            effective.RcloneRemote,
            effective.RootPath
        );

        var command = $"rclone lsd {ShellQuote(remotePath)}";

        var result = await ExecuteSshAsync(
            effective,
            command,
            TimeSpan.FromSeconds(
                Math.Max(10, effective.ConnectTimeoutSeconds)
            ),
            ct
        );

        EnsureSuccess(
            result,
            "Không thể kết nối Google Drive qua rclone."
        );
    }

    /// <summary>
    /// Tests remote storage for one database connection.
    /// </summary>
    public async Task TestAsync(
        BackupTransportSettings? transport,
        CancellationToken ct
    )
    {
        if (!IsEnabled(transport))
            return;

        var effective = ResolveEffectiveSettings(transport);

        ValidateConfiguration(effective);

        var remotePath = BuildRclonePath(
            effective.RcloneRemote,
            effective.RootPath
        );

        var command = $"rclone lsd {ShellQuote(remotePath)}";

        var result = await ExecuteSshAsync(
            effective,
            command,
            TimeSpan.FromSeconds(
                Math.Max(10, effective.ConnectTimeoutSeconds)
            ),
            ct
        );

        EnsureSuccess(
            result,
            "Không thể kết nối Google Drive qua rclone."
        );
    }

    public async Task<RemoteBackupUploadResult> UploadAndVerifyAsync(
        string sourceDirectory,
        string jobName,
        DateTimeOffset startedAt,
        string runFolderName,
        BackupTransportSettings? transport,
        CancellationToken ct
    )
    {
        if (!IsEnabled(transport))
        {
            throw new InvalidOperationException(
                "Remote backup storage chưa được bật."
            );
        }

        var effective = ResolveEffectiveSettings(transport);

        ValidateConfiguration(effective);

        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            throw new ArgumentException(
                "Source backup directory is required.",
                nameof(sourceDirectory)
            );
        }

        var safeJobName = SanitizeRemoteSegment(jobName);

        //
        // The run folder is created using local server/application time:
        //
        //     Job Name_yyyyMMdd_HHmmss
        //
        // Prefer that date for Google Drive yyyy/MM partitioning.
        //
        // This avoids an important timezone issue for scheduled jobs
        // running at 03:00 Asia/Vientiane, where UTC can still belong
        // to the previous date/month.
        //
        var storageDate = ResolveStorageDate(
            runFolderName,
            startedAt
        );

        var destinationRelative = CombineRemotePath(
            effective.RootPath,
            safeJobName,
            storageDate.ToString("yyyy", CultureInfo.InvariantCulture),
            storageDate.ToString("MM", CultureInfo.InvariantCulture),
            SanitizeRemoteSegment(runFolderName)
        );

        var destination = BuildRclonePath(
            effective.RcloneRemote,
            destinationRelative
        );

        _logger.LogInformation(
            "Uploading SQL Server backup directory {SourceDirectory} via SSH host {SshHost} to {Destination}.",
            sourceDirectory,
            effective.SshHost,
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
            effective,
            uploadCommand,
            TimeSpan.FromMinutes(
                Math.Max(1, effective.UploadTimeoutMinutes)
            ),
            ct
        );

        EnsureSuccess(
            uploadResult,
            $"Google Drive upload thất bại. Destination: {destination}"
        );

        var verificationOutput = string.Empty;

        if (effective.VerifyAfterUpload)
        {
            _logger.LogInformation(
                "Verifying uploaded backup directory {Destination} via SSH host {SshHost}.",
                destination,
                effective.SshHost
            );

            var verifyCommand =
                $"rclone check {ShellQuote(sourceDirectory)} {ShellQuote(destination)} "
                + "--one-way";

            var verifyResult = await ExecuteSshAsync(
                effective,
                verifyCommand,
                TimeSpan.FromMinutes(
                    Math.Max(1, effective.UploadTimeoutMinutes)
                ),
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

    private EffectiveRemoteBackupSettings ResolveEffectiveSettings(
        BackupTransportSettings? transport
    )
    {
        //
        // No per-connection transport:
        // use the existing global configuration unchanged.
        //
        if (transport is null)
        {
            return new EffectiveRemoteBackupSettings(
                Enabled: _options.Enabled,
                SshHost: _options.SshHost,
                SshPort: _options.SshPort,
                SshUser: _options.SshUser,
                SshPrivateKeyPath: _options.SshPrivateKeyPath,
                RcloneRemote: _options.RcloneRemote,
                RootPath: string.IsNullOrWhiteSpace(_options.RootPath)
                    ? "DATABASE"
                    : _options.RootPath,
                VerifyAfterUpload: _options.VerifyAfterUpload,
                ConnectTimeoutSeconds: _options.ConnectTimeoutSeconds,
                UploadTimeoutMinutes: _options.UploadTimeoutMinutes
            );
        }

        //
        // Per-connection transport exists.
        //
        // SSH host/user/rclone remote belong to that connection.
        // We intentionally do NOT fall back RcloneRemote to the
        // global remote because that could upload a backup from
        // one server using another server's remote configuration.
        //
        // The private key path may safely fall back to the global
        // Windows key because the same client key can be authorized
        // on multiple Linux servers.
        //
        return new EffectiveRemoteBackupSettings(
            Enabled: transport.Enabled,

            SshHost: transport.SshHost.Trim(),

            SshPort: transport.SshPort > 0
                ? transport.SshPort
                : 22,

            SshUser: string.IsNullOrWhiteSpace(transport.SshUser)
                ? "root"
                : transport.SshUser.Trim(),

            SshPrivateKeyPath:
                string.IsNullOrWhiteSpace(transport.SshPrivateKeyPath)
                    ? _options.SshPrivateKeyPath
                    : transport.SshPrivateKeyPath.Trim(),

            RcloneRemote: transport.RcloneRemote.Trim(),

            RootPath: !string.IsNullOrWhiteSpace(transport.RootPath)
                ? transport.RootPath.Trim()
                : !string.IsNullOrWhiteSpace(_options.RootPath)
                    ? _options.RootPath.Trim()
                    : "DATABASE",

            VerifyAfterUpload: transport.VerifyAfterUpload,

            ConnectTimeoutSeconds:
                transport.ConnectTimeoutSeconds > 0
                    ? transport.ConnectTimeoutSeconds
                    : Math.Max(30, _options.ConnectTimeoutSeconds),

            UploadTimeoutMinutes:
                transport.UploadTimeoutMinutes > 0
                    ? transport.UploadTimeoutMinutes
                    : Math.Max(240, _options.UploadTimeoutMinutes)
        );
    }

    private async Task<SshCommandResult> ExecuteSshAsync(
        EffectiveRemoteBackupSettings settings,
        string remoteCommand,
        TimeSpan timeout,
        CancellationToken ct
    )
    {
        ValidateConfiguration(settings);

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
            Environment.ExpandEnvironmentVariables(
                settings.SshPrivateKeyPath
            )
        );

        startInfo.ArgumentList.Add("-p");

        startInfo.ArgumentList.Add(
            settings.SshPort.ToString(
                CultureInfo.InvariantCulture
            )
        );

        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("BatchMode=yes");

        startInfo.ArgumentList.Add("-o");

        startInfo.ArgumentList.Add(
            $"ConnectTimeout={Math.Max(5, settings.ConnectTimeoutSeconds)}"
        );

        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(
            "StrictHostKeyChecking=accept-new"
        );

        startInfo.ArgumentList.Add(
            $"{settings.SshUser}@{settings.SshHost}"
        );

        startInfo.ArgumentList.Add(remoteCommand);

        using var process = new Process
        {
            StartInfo = startInfo
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException(
                    "Không thể khởi động ssh.exe."
                );
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

        var stdoutTask =
            process.StandardOutput.ReadToEndAsync(ct);

        var stderrTask =
            process.StandardError.ReadToEndAsync(ct);

        using var timeoutCts =
            CancellationTokenSource.CreateLinkedTokenSource(ct);

        timeoutCts.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(
                timeoutCts.Token
            );
        }
        catch (OperationCanceledException)
            when (!ct.IsCancellationRequested)
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

        return new SshCommandResult(
            process.ExitCode,
            stdout,
            stderr
        );
    }

    private static void ValidateConfiguration(
        EffectiveRemoteBackupSettings settings
    )
    {
        if (!settings.Enabled)
        {
            throw new InvalidOperationException(
                "Remote backup storage chưa được bật."
            );
        }

        if (string.IsNullOrWhiteSpace(settings.SshHost))
        {
            throw new InvalidOperationException(
                "Backup transport SshHost chưa được cấu hình."
            );
        }

        if (string.IsNullOrWhiteSpace(settings.SshUser))
        {
            throw new InvalidOperationException(
                "Backup transport SshUser chưa được cấu hình."
            );
        }

        if (string.IsNullOrWhiteSpace(
                settings.SshPrivateKeyPath
            ))
        {
            throw new InvalidOperationException(
                "Backup transport SshPrivateKeyPath chưa được cấu hình."
            );
        }

        var keyPath =
            Environment.ExpandEnvironmentVariables(
                settings.SshPrivateKeyPath
            );

        if (!File.Exists(keyPath))
        {
            throw new FileNotFoundException(
                $"SSH private key không tồn tại: {keyPath}",
                keyPath
            );
        }

        if (string.IsNullOrWhiteSpace(
                settings.RcloneRemote
            ))
        {
            throw new InvalidOperationException(
                "Backup transport RcloneRemote chưa được cấu hình."
            );
        }
    }

    private static string BuildRclonePath(
        string rcloneRemote,
        string relativePath
    )
    {
        var remote =
            rcloneRemote.Trim().TrimEnd(':');

        var path =
            relativePath.Trim().Trim('/');

        return string.IsNullOrWhiteSpace(path)
            ? $"{remote}:"
            : $"{remote}:{path}";
    }

    private static string CombineRemotePath(
        params string[] segments
    )
    {
        return string.Join(
            "/",
            segments
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().Trim('/', '\\'))
        );
    }

    private static string SanitizeRemoteSegment(
        string value
    )
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unnamed";

        var invalid = new[]
        {
            '/',
            '\\',
            ':',
            '*',
            '?',
            '"',
            '<',
            '>',
            '|'
        };

        var builder =
            new StringBuilder(value.Length);

        foreach (var ch in value.Trim())
        {
            builder.Append(
                invalid.Contains(ch) ? '_' : ch
            );
        }

        return builder.ToString();
    }

    private static DateTime ResolveStorageDate(
        string runFolderName,
        DateTimeOffset startedAt
    )
    {
        //
        // Expected suffix:
        //
        //     _yyyyMMdd_HHmmss
        //
        // Example:
        //
        //     JITS Admin_20261001_113536
        //
        // Search from the end so underscores in job names do not matter.
        //
        if (!string.IsNullOrWhiteSpace(runFolderName))
        {
            var lastUnderscore =
                runFolderName.LastIndexOf('_');

            if (lastUnderscore > 0)
            {
                var previousUnderscore =
                    runFolderName.LastIndexOf(
                        '_',
                        lastUnderscore - 1
                    );

                if (previousUnderscore >= 0)
                {
                    var timestamp =
                        runFolderName[
                            (previousUnderscore + 1)..];

                    if (DateTime.TryParseExact(
                            timestamp,
                            "yyyyMMdd_HHmmss",
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.None,
                            out var parsed
                        ))
                    {
                        return parsed;
                    }
                }
            }
        }

        //
        // Fallback only.
        //
        return startedAt.LocalDateTime;
    }

    private static string ShellQuote(string value)
    {
        return "'"
            + value.Replace("'", "'\"'\"'")
            + "'";
    }

    private static void EnsureSuccess(
        SshCommandResult result,
        string message
    )
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

    private static string CombineOutput(
        SshCommandResult result
    )
    {
        var parts = new[]
        {
            result.StandardOutput?.Trim(),
            result.StandardError?.Trim()
        }.Where(x => !string.IsNullOrWhiteSpace(x));

        return string.Join(
            Environment.NewLine,
            parts
        );
    }

    private static string ResolveSshExecutable()
    {
        var systemDirectory =
            Environment.GetFolderPath(
                Environment.SpecialFolder.System
            );

        var windowsSsh =
            Path.Combine(
                systemDirectory,
                "OpenSSH",
                "ssh.exe"
            );

        return File.Exists(windowsSsh)
            ? windowsSsh
            : "ssh.exe";
    }

    private static void TryKill(
        Process process
    )
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(
                    entireProcessTree: true
                );
            }
        }
        catch
        {
            //
            // Best effort only.
            //
        }
    }

    private sealed record EffectiveRemoteBackupSettings(
        bool Enabled,
        string SshHost,
        int SshPort,
        string SshUser,
        string SshPrivateKeyPath,
        string RcloneRemote,
        string RootPath,
        bool VerifyAfterUpload,
        int ConnectTimeoutSeconds,
        int UploadTimeoutMinutes
    );

    private sealed record SshCommandResult(
        int ExitCode,
        string StandardOutput,
        string StandardError
    );
}