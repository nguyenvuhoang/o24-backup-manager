namespace BackupManager.API.Services;

public sealed class RemoteBackupStorageOptions
{
    public const string SectionName = "BackupManager:RemoteStorage";

    public bool Enabled { get; set; } = false;

    public string SshHost { get; set; } = string.Empty;

    public int SshPort { get; set; } = 22;

    public string SshUser { get; set; } = "root";

    public string SshPrivateKeyPath { get; set; } = string.Empty;

    public string RcloneRemote { get; set; } = string.Empty;

    public string RootPath { get; set; } = "DATABASE";

    public bool VerifyAfterUpload { get; set; } = true;

    public int ConnectTimeoutSeconds { get; set; } = 30;

    public int UploadTimeoutMinutes { get; set; } = 240;
}
