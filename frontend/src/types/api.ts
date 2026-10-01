import type { DatabaseProvider } from "@/lib/database-providers";

export type RunStatus =
  | "Queued"
  | "Running"
  | "Succeeded"
  | "Failed"
  | "Cancelled";

export type BackupStage =
  | "Queued"
  | "Connecting"
  | "BackingUp"
  | "Verifying"
  | "Compressing"
  | "Transferring"
  | "VerifyingUpload"
  | "RetentionCleanup"
  | "Notifying"
  | "Completed"
  | "Failed";

export type RunLogLevel = "Information" | "Warning" | "Error";

export type RuntimeJobState = "Running" | "Queued";

export interface SqlServerOptions {
  server: string;
  integratedSecurity: boolean;
  username?: string;
  passwordSecret?: string;
}

export interface BackupSchedule {
  enabled: boolean;
  time: string;
  timeZone: string;
}

export interface BackupJob {
  id?: string;
  name: string;
  enabled: boolean;
  connectionId?: string;
  sqlServer?: SqlServerOptions | null;
  databases: string[];

  /**
   * SQL Server-side staging directory.
   * Example:
   * /var/opt/mssql/backup/backup-manager
   */
  sqlServerBackupDirectory?: string;

  /**
   * Final/archive destination configured for the job.
   */
  backupDirectory: string;

  retentionDays: number;

  schedule?: BackupSchedule;

  sftp?: {
    enabled: boolean;
    host: string;
    port: number;
    username: string;
    remotePath: string;
    identityFile?: string;
  };

  telegram?: {
    enabled: boolean;
    chatId: string;
    botTokenSecret: string;
    prefix: string;
  };
}

export interface BackupArtifact {
  path: string;
  size: number;
  sha256: string;
}

export interface RunStage {
  name: string;
  status: RunStatus;
  startedAt?: string | null;
  finishedAt?: string | null;
  message: string;
}

export interface DatabaseBackupRun {
  database: string;
  status: RunStatus;
  stage: BackupStage;

  startedAt?: string | null;
  finishedAt?: string | null;

  backupPath?: string | null;
  backupSize?: number | null;

  sqlOutput?: string | null;
  error?: string | null;
}

export interface RunLog {
  timestamp: string;
  level: RunLogLevel;
  stage: BackupStage;
  database?: string | null;
  message: string;
}

export interface BackupRun {
  id: string;
  jobId: string;
  jobName: string;

  status: RunStatus;

  createdAt: string;
  startedAt?: string | null;
  finishedAt?: string | null;

  currentStage?: BackupStage;
  currentDatabase?: string | null;

  totalDatabases?: number;
  completedDatabases?: number;
  succeededDatabases?: number;
  failedDatabases?: number;

  sqlServerBackupDirectory?: string | null;
  sqlServerRunDirectory?: string | null;
  backupDirectory?: string | null;
  remoteBackupDirectory?: string | null;

  artifacts: BackupArtifact[];
  stages: RunStage[];

  databases?: DatabaseBackupRun[];
  logs?: RunLog[];

  error?: string | null;
}

export interface RuntimeJobStatus {
  jobId: string;
  state: RuntimeJobState;
  queuePosition?: number | null;
}

export interface RuntimeSchedule {
  jobId: string;
  enabled: boolean;
  time: string;
  timeZone: string;
  nextRunAt?: string | null;
}

export interface BackupRuntime {
  currentJobId?: string | null;
  queuedCount: number;
  jobs: RuntimeJobStatus[];
  schedules?: RuntimeSchedule[];
}

export interface DiscoveryResult {
  success: boolean;
  message: string;
  items?: string[];
}

export interface DatabaseConnectionForm {
  name: string;
  provider: DatabaseProvider;
  host: string;
  port: number | null;
  instanceName: string | null;
  useNamedInstanceDiscovery?: boolean;
  authenticationType: "windows" | "sqlserver";
  username: string | null;
  password: string | null;
  database: string | null;
  encrypt: boolean;
  trustServerCertificate: boolean;
  connectionTimeout: number;
  commandTimeout: number;
  applicationName: string;
}

export interface BackupTransportSettings {
  enabled: boolean;
  sshHost: string;
  sshPort: number;
  sshUser: string;
  sshPrivateKeyPath: string;
  rcloneRemote: string;
  rootPath: string;
  verifyAfterUpload: boolean;
  connectTimeoutSeconds: number;
  uploadTimeoutMinutes: number;
}

export interface BackupTransportTestResult {
  success: boolean;
  message: string;
}

export interface DatabaseServerInfo {
  name: string;
  provider: string;
  version: string;
}

export interface DatabaseInfo {
  name: string;
  status: string;
  sizeMb: number | null;
  createdAt: string | null;
  isAccessible: boolean;
}

export interface DatabaseDiscoveryResult {
  success: boolean;
  server: DatabaseServerInfo;
  databases: DatabaseInfo[];
}

export interface DatabaseTestResult {
  success: boolean;
  serverName: string;
  databaseEngine: string;
  version: string;
  message: string;
}

export interface SavedDatabaseConnection extends Omit<
  DatabaseConnectionForm,
  "password"
> {
  id: string;
  hasPassword: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
  backupTransport?: BackupTransportSettings | null;
}

export interface CreateBackupJobRequest {
  id: string;
  name: string;
  connectionId: string;
  databases: string[];
  backupDirectory: string;
  retentionDays: number;
  enabled: boolean;

  sqlServerBackupDirectory?: string;
  schedule?: BackupSchedule;

  sftp?: BackupJob["sftp"];
  telegram?: BackupJob["telegram"];
}
