using BackupManager.Domain.Models;
using BackupManager.Infrastructure.Services;

namespace BackupManager.Plugins.SqlServer;

public sealed class SqlServerPlugin(ProcessRunner runner, SecretVault vault)
{
    public async Task<TestResult> DiscoverAsync(SqlServerOptions options, CancellationToken ct)
    {
        var (args, environment) = await ConnectionArgs(options, ct);
        args.AddRange(["-h", "-1", "-W", "-Q", "SET NOCOUNT ON; SELECT name FROM sys.databases WHERE state_desc='ONLINE' AND name <> 'tempdb' ORDER BY name"]);
        var result = await runner.RunAsync("sqlcmd", args, ct, environment);
        if (result.ExitCode != 0 || result.Error.Contains("Msg ", StringComparison.OrdinalIgnoreCase))
            return new(false, Clean(result.Error + result.Output));
        var items = result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new(true, $"Found {items.Length} databases.", items);
    }

    public async Task<string> BackupAsync(SqlServerOptions options, string database, string directory, CancellationToken ct, string? resolvedPassword = null)
    {
        Directory.CreateDirectory(directory);
        var safeName = string.Concat(database.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var path = Path.GetFullPath(Path.Combine(directory, safeName + ".bak"));
        var db = database.Replace("]", "]]", StringComparison.Ordinal);
        var disk = path.Replace("'", "''", StringComparison.Ordinal);
        var sql = $"BACKUP DATABASE [{db}] TO DISK=N'{disk}' WITH INIT, COMPRESSION, CHECKSUM, STATS=5; RESTORE VERIFYONLY FROM DISK=N'{disk}' WITH CHECKSUM;";
        var (args, environment) = await ConnectionArgs(options, ct, resolvedPassword);
        args.AddRange(["-b", "-Q", sql]);
        var result = await runner.RunAsync("sqlcmd", args, ct, environment);
        if (result.ExitCode != 0 || result.Error.Contains("Msg ", StringComparison.OrdinalIgnoreCase) || !File.Exists(path) || new FileInfo(path).Length == 0)
            throw new InvalidOperationException($"Backup failed for {database}: {Clean(result.Error + result.Output)}");
        return path;
    }

    private async Task<(List<string> Arguments, Dictionary<string, string?> Environment)> ConnectionArgs(SqlServerOptions options, CancellationToken ct, string? resolvedPassword = null)
    {
        string? help = null;
        if (options.Encrypt.HasValue)
        {
            using var probe = CancellationTokenSource.CreateLinkedTokenSource(ct);
            probe.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                var result = await runner.RunAsync("sqlcmd", ["-?"], probe.Token);
                help = result.Output + result.Error;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            { throw new InvalidOperationException("Không đọc được phiên bản sqlcmd trong 5 giây. Kiểm tra SQL Server command-line tools."); }
            catch (System.ComponentModel.Win32Exception)
            { throw new InvalidOperationException("Không tìm thấy sqlcmd trong PATH của backend. Cài đặt SQL Server command-line tools."); }
        }
        var args = SqlServerConnectionSettings.BuildSqlcmdArguments(options, help);
        if (options.IntegratedSecurity && options.Encrypt.HasValue && !OperatingSystem.IsWindows())
            throw new InvalidOperationException("Windows Authentication requires a Windows backend service identity.");
        var environment = new Dictionary<string, string?>();
        if (options.IntegratedSecurity) args.Add("-E");
        else
        {
            var password = resolvedPassword ?? await vault.ResolveAsync(options.PasswordSecret) ?? throw new InvalidOperationException("SQL password secret is missing.");
            args.AddRange(["-U", options.Username ?? ""]);
            environment["SQLCMDPASSWORD"] = password;
        }
        return (args, environment);
    }

    private static string Clean(string value) => value.ReplaceLineEndings(" ").Trim();
}
