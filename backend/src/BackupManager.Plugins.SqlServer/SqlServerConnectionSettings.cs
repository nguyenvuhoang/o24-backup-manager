using BackupManager.Application.Connections;
using BackupManager.Domain.Models;
using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;

namespace BackupManager.Plugins.SqlServer;

public static class SqlServerConnectionSettings
{
    public static List<string> BuildSqlcmdArguments(SqlServerOptions options, string? help)
    {
        // Generated SQL must never expand $(SQLCMDPASSWORD) from SQLCMD's environment.
        var args = new List<string> { "-S", options.Server, "-x" };
        if (options.Encrypt is bool encrypt)
        {
            if (help is null || !Regex.IsMatch(help, @"-N\s*\[s\|m\|o\]", RegexOptions.IgnoreCase))
                throw new ConfigurationException("SQLCMD_UNSUPPORTED", "Cần sqlcmd (ODBC) có tùy chọn -N[s|m|o]. Kiểm tra sqlcmd -? và nâng cấp SQL Server command-line tools trước khi chạy job mới.");
            args.Add(encrypt ? "-Nm" : "-No");
        }
        if (options.TrustServerCertificate == true) args.Add("-C");
        if (options.ConnectionTimeout is int timeout) args.AddRange(["-l", timeout.ToString()]);
        if (options.CommandTimeout is int commandTimeout) args.AddRange(["-t", commandTimeout.ToString()]);
        if (!string.IsNullOrWhiteSpace(options.Database)) args.AddRange(["-d", options.Database]);
        return args;
    }
    public static string ResolveDataSource(DatabaseConnectionSettings options)
    {
        var host = options.Host.Trim();
        // An explicit TCP port addresses the listener directly, bypassing SQL Browser.
        if (options.Port is int port) return $"tcp:{host},{port}";
        return string.IsNullOrWhiteSpace(options.InstanceName) ? host : $"{host}\\{options.InstanceName.Trim()}";
    }
    public static string BuildConnectionString(DatabaseConnectionOptions options)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = ResolveDataSource(options), InitialCatalog = string.IsNullOrWhiteSpace(options.Database) ? "master" : options.Database.Trim(),
            IntegratedSecurity = options.AuthenticationType == "windows", Encrypt = options.Encrypt,
            TrustServerCertificate = options.TrustServerCertificate, ConnectTimeout = options.ConnectionTimeout,
            ApplicationName = options.ApplicationName, PersistSecurityInfo = false, Pooling = false
        };
        if (!builder.IntegratedSecurity) { builder.UserID = options.Username; builder.Password = options.Password; }
        return builder.ConnectionString;
    }
    public static SqlServerOptions ForBackup(DatabaseConnection connection) => new()
    {
        Server = ResolveDataSource(connection.Settings), IntegratedSecurity = connection.Settings.AuthenticationType == "windows",
        Username = connection.Settings.Username, PasswordSecret = connection.PasswordSecret,
        Encrypt = connection.Settings.Encrypt, TrustServerCertificate = connection.Settings.TrustServerCertificate,
        ConnectionTimeout = connection.Settings.ConnectionTimeout, CommandTimeout = connection.Settings.CommandTimeout,
        Database = connection.Settings.Database
    };
}
