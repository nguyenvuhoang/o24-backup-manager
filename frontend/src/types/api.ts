export type RunStatus = 'Queued' | 'Running' | 'Succeeded' | 'Failed' | 'Cancelled';
export interface SqlServerOptions { server: string; integratedSecurity: boolean; username?: string; passwordSecret?: string }
export interface BackupJob { id?: string; name: string; enabled: boolean; connectionId?: string; sqlServer?: SqlServerOptions | null; databases: string[]; backupDirectory: string; retentionDays: number; sftp?: { enabled: boolean; host: string; port: number; username: string; remotePath: string; identityFile?: string }; telegram?: { enabled: boolean; chatId: string; botTokenSecret: string; prefix: string } }
export interface BackupRun { id: string; jobId: string; jobName: string; status: RunStatus; createdAt: string; startedAt?: string; finishedAt?: string; error?: string; artifacts: { path: string; size: number; sha256: string }[]; stages: { name: string; status: RunStatus; message: string }[] }
export interface DiscoveryResult { success: boolean; message: string; items?: string[] }

export interface DatabaseConnectionForm {
  name: string;
  provider: 'sqlserver';
  host: string;
  port: number | null;
  instanceName: string | null;
  authenticationType: 'windows' | 'sqlserver';
  username: string | null;
  password: string | null;
  database: string | null;
  encrypt: boolean;
  trustServerCertificate: boolean;
  connectionTimeout: number;
  commandTimeout: number;
  applicationName: string;
}
export interface DatabaseServerInfo { name: string; provider: string; version: string }
export interface DatabaseInfo { name: string; status: string; sizeMb: number | null; createdAt: string | null; isAccessible: boolean }
export interface DatabaseDiscoveryResult { success: boolean; server: DatabaseServerInfo; databases: DatabaseInfo[] }
export interface DatabaseTestResult { success: boolean; serverName: string; databaseEngine: string; version: string; message: string }
export interface SavedDatabaseConnection { id: string; configuration: Omit<DatabaseConnectionForm, 'password'>; hasPassword: boolean; createdAtUtc: string; updatedAtUtc: string }
export interface CreateBackupJobRequest { id: string; name: string; connectionId: string; databases: string[]; backupDirectory: string; retentionDays: number; enabled: boolean }
