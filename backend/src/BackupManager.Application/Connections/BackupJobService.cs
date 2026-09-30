using BackupManager.Domain.Models;

namespace BackupManager.Application.Connections;

public sealed class BackupJobService(IConfigurationStore store, DatabaseConnectionService connections)
{
    public async Task<BackupJob> SaveAsync(BackupJob job, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(job.Name) || job.Name.Length > 200) errors["name"] = ["Tên job là bắt buộc (tối đa 200 ký tự)."];
        if (string.IsNullOrWhiteSpace(job.BackupDirectory) || job.BackupDirectory.IndexOf('\0') >= 0) errors["backupDirectory"] = ["Thư mục backup không hợp lệ."];
        if (job.RetentionDays < 1) errors["retentionDays"] = ["Giữ file ít nhất 1 ngày."];
        if (job.Databases is null || job.Databases.Length == 0 || job.Databases.Any(string.IsNullOrWhiteSpace)) errors["databases"] = ["Chọn ít nhất một database."];
        if (job.ConnectionId is null)
        {
            var existing = (await store.GetJobsAsync()).FirstOrDefault(j => j.Id == job.Id);
            if (existing?.SqlServer is null || existing.ConnectionId is not null || existing.SqlServer != job.SqlServer || !existing.Databases.SequenceEqual(job.Databases ?? []))
                errors["connectionId"] = ["Job mới hoặc thay đổi kết nối/database cần DatabaseConnection đã lưu."];
        }
        if (errors.Count > 0) throw new ConfigurationException("VALIDATION_FAILED", "Cấu hình job không hợp lệ.", errors);
        DateTimeOffset? expectedVersion = null;
        if (job.ConnectionId is Guid id)
        {
            expectedVersion = (await connections.GetAsync(id)).UpdatedAtUtc;
            var discovered = await connections.DiscoverAsync(await connections.ResolveOptionsAsync(id), ct);
            var selectable = discovered.Databases.Where(d => d.Status == "ONLINE" && d.IsAccessible).Select(d => d.Name).ToHashSet(StringComparer.Ordinal);
            if (job.Databases!.Any(d => !selectable.Contains(d)))
                throw new ConfigurationException("DATABASE_UNAVAILABLE", "Database đã chọn không còn ONLINE hoặc không có quyền truy cập. Hãy kết nối lại.");
            job = job with { SqlServer = null };
        }
        return await store.SaveJobAsync(job with { Name = job.Name.Trim(), BackupDirectory = job.BackupDirectory.Trim(), Databases = job.Databases!.Distinct(StringComparer.Ordinal).ToArray() }, expectedVersion);
    }
}
