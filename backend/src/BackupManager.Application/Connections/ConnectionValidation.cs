using System.Text.RegularExpressions;

namespace BackupManager.Application.Connections;

public static class ConnectionValidation
{
    public static Dictionary<string, string[]> GetErrors(DatabaseConnectionOptions o)
    {
        var errors = new Dictionary<string, string[]>();
        void Error(string field, string message) => errors[field] = [message];
        if (o.Provider != "sqlserver") Error("provider", "Hiện chỉ hỗ trợ Microsoft SQL Server.");
        if (string.IsNullOrWhiteSpace(o.Host) || o.Host.Length > 255 || !Regex.IsMatch(o.Host.Trim(), @"^[a-zA-Z0-9_.:\[\]-]+$"))
            Error("host", "Nhập hostname/IP; nhập instance và port ở ô riêng.");
        if (o.Port is < 1 or > 65535) Error("port", "Port phải từ 1 đến 65535.");
        if (o.UseNamedInstanceDiscovery && o.Port is null && string.IsNullOrWhiteSpace(o.InstanceName))
            Error("instanceName", "Nhập Instance Name để sử dụng Named Instance discovery.");
        if (!string.IsNullOrEmpty(o.InstanceName) && (o.InstanceName.Length > 128 || !Regex.IsMatch(o.InstanceName, @"^[a-zA-Z0-9_$-]+$")))
            Error("instanceName", "Instance name không hợp lệ.");
        if (o.AuthenticationType is not ("windows" or "sqlserver")) Error("authenticationType", "Kiểu xác thực không hợp lệ.");
        if (o.AuthenticationType == "sqlserver")
        {
            if (string.IsNullOrWhiteSpace(o.Username) || o.Username.Length > 128) Error("username", "Username là bắt buộc (tối đa 128 ký tự).");
            if (string.IsNullOrEmpty(o.Password) || o.Password.Length > 4096) Error("password", "Password là bắt buộc (tối đa 4096 ký tự).");
        }
        if (o.Database?.Length > 128) Error("database", "Database tối đa 128 ký tự.");
        if (o.ConnectionTimeout is < 1 or > 120) Error("connectionTimeout", "Connection timeout phải từ 1 đến 120 giây.");
        if (o.CommandTimeout is < 1 or > 600) Error("commandTimeout", "Command timeout phải từ 1 đến 600 giây.");
        if (string.IsNullOrWhiteSpace(o.ApplicationName) || o.ApplicationName.Length > 128) Error("applicationName", "Application name là bắt buộc (tối đa 128 ký tự).");
        if (o.Name is null || o.Name.Length > 200) Error("name", "Tên kết nối tối đa 200 ký tự.");
        return errors;
    }
    public static void Validate(DatabaseConnectionOptions options)
    {
        var errors = GetErrors(options);
        if (errors.Count > 0) throw new ConfigurationException("VALIDATION_FAILED", "Thông tin kết nối không hợp lệ.", errors);
    }
}
