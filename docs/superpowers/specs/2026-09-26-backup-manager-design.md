# Backup Manager — Product and Technical Design

## 1. Purpose

Build a production backup-management system that replaces hand-edited PowerShell/Bash scripts with a portable UI-driven agent. An operator installs the agent on a server, enters connection and destination settings, tests them, schedules a job, and can immediately run a real backup.

The first usable release must reproduce the supplied EMI CMS workflow end to end:

`SQL Server backup -> validate every database -> compress -> SFTP/SCP upload -> verify -> retention cleanup -> Telegram notification`

The product must grow through plugins to support Windows/Linux, native/Docker deployment, SQL Server/PostgreSQL/Oracle/MySQL, local and cloud storage, multiple notifications, restore, and a central control plane without rewriting the agent core.

## 2. Success criteria

- A new server can be configured from the UI without editing a script.
- Operators can test database, storage, and notification connections before saving a job.
- A job never reports success when any required pipeline stage failed.
- Local backups are not deleted until required remote copies are verified.
- Passwords, tokens, and private keys are never written to logs or returned to the browser.
- Scheduled jobs continue running while the central control plane is unavailable.
- The same job model runs under Windows Service, Linux systemd, and Docker.
- Every execution has durable status, per-stage logs, file size, checksum, duration, and error details.

## 3. Delivery decomposition

The system is one product composed of independently deliverable modules.

### Release 1 — Runnable local backup product

- Local Agent API and web UI
- SQL Server backup plugin
- ZIP compression
- Local filesystem and SFTP/SCP destinations
- SHA-256 verification
- Telegram notifications
- Cron/calendar scheduling
- Retention cleanup
- Run history and redacted logs
- Windows native host and Docker image
- Import/export of non-secret configuration

### Release 2 — Complete database and storage plugin set

- PostgreSQL (`pg_dump`/`pg_basebackup`)
- MySQL (`mysqldump`/`mysqlpump` where available)
- Oracle Data Pump and RMAN profiles
- SMB, NFS, S3-compatible/MinIO, Azure Blob, and Google Cloud Storage
- 7-Zip, gzip, tar.gz, AES-256, and GPG
- Email, Slack, and generic webhook notifications
- Linux native package and systemd host

### Release 3 — Hybrid control plane and restore center

- Central dashboard and multi-agent enrollment
- Outbound-only agent connection
- Offline queue and later synchronization
- RBAC, OIDC/LDAP readiness, audit log
- Remote job assignment and run commands
- Restore workflows and scheduled restore verification
- Agent/plugin version management

Each release uses the same contracts and persistence model. Releases are sequencing, not a reduction of the approved full scope.

## 4. Architecture

### 4.1 Deployable applications

1. **BackupManager.Agent** — ASP.NET Core .NET 10 modular monolith. Hosts the local API, scheduler, job engine, plugin runtime, local UI, secret vault, and SQLite persistence.
2. **BackupManager.Web** — Next.js application served locally with the Agent in standalone deployments and by the Control Plane centrally.
3. **BackupManager.ControlPlane** — ASP.NET Core .NET 10 API with PostgreSQL for multi-agent administration.
4. **Plugin packages** — signed .NET assemblies implementing versioned contracts. External database tools are invoked through a hardened process runner.

### 4.2 Core boundaries

| Module | Responsibility |
|---|---|
| Jobs | Job definitions, validation, enable/disable, import/export |
| Scheduler | Persistent schedules, misfire policy, concurrency guard |
| Execution | Pipeline orchestration, cancellation, retry, progress |
| Plugins | Discovery, compatibility, capabilities, health checks |
| Secrets | Encrypted credential storage and secret references |
| History | Runs, stages, artifacts, structured/redacted logs |
| Retention | Local/remote retention evaluation and safe deletion |
| Notifications | Templating and delivery after terminal state |
| Agent Sync | Outbound control-plane registration and offline queue |

Core modules depend on interfaces, not vendor SDKs. Database and storage-specific code lives only in plugins.

## 5. Plugin contracts

Plugins expose metadata, JSON-schema configuration, validation, connection testing, and capability flags.

- `IDatabaseBackupPlugin`: discover databases, validate tools, estimate where possible, backup, cancel, produce artifacts.
- `IDatabaseRestorePlugin`: validate backup, create restore plan, restore to an explicitly selected target.
- `IStoragePlugin`: upload/download/list/delete, checksum or metadata verification, multipart/resume capability.
- `ICompressionPlugin`: pack/unpack/test archive.
- `IEncryptionPlugin`: encrypt/decrypt with secret references.
- `INotificationPlugin`: test and send templated messages.
- `ISecretProviderPlugin`: resolve secrets without exposing plaintext to job configuration.

Plugin execution is versioned. Unknown or incompatible versions disable affected jobs with an actionable error rather than silently changing behavior.

## 6. Job and execution model

A job contains:

- source connection reference and database selection rules;
- staging path and free-space threshold;
- optional compression and encryption stages;
- one or more destinations, each marked required or optional;
- verification policy;
- local and remote retention policies;
- notification routing;
- schedule, timezone, timeout, retry, and concurrency policy.

Run state is `Queued`, `Running`, `Succeeded`, `PartiallySucceeded`, `Failed`, or `Cancelled`. Each stage records its own state, attempts, timestamps, structured progress, redacted command description, and error classification.

Default pipeline:

1. Acquire per-job lock.
2. Validate tools, credentials, paths, connectivity, and disk space.
3. Create a run-specific staging directory.
4. Back up every selected database independently.
5. Fail the run if any required database backup fails.
6. Compress and optionally encrypt artifacts.
7. Upload to every configured destination.
8. Verify size and SHA-256, or provider-native checksum when semantically equivalent.
9. Apply retention only after all required destinations verify successfully.
10. Persist terminal status and send notifications.
11. Remove temporary files according to the cleanup policy.

Default concurrency is one active run per job. Missed schedules run once after restart unless the job disables catch-up.

## 7. SQL Server Release 1 behavior

- Supports Windows authentication and SQL authentication.
- Discovers online, non-tempdb databases visible to the configured login.
- Allows explicit database selection and include/exclude patterns.
- Executes `BACKUP DATABASE ... WITH INIT, COMPRESSION, CHECKSUM, STATS = 5` through `sqlcmd` in Release 1.
- Uses quoted identifiers and generated artifact paths; raw SQL input is not accepted from the UI.
- Treats non-zero process exit, SQL error output, missing file, empty file, or failed `RESTORE VERIFYONLY` as failure.
- Runs `RESTORE VERIFYONLY WITH CHECKSUM` before packaging by default.
- Records each `.bak` size and verification result.
- Requires the SQL Server service account to have write access to the selected backup path and surfaces a diagnostic when it does not.

The supplied CMS configuration can be represented as a seeded example job but must not contain its Telegram token, passwords, or private keys.

## 8. Security

- Local secrets are encrypted with Windows DPAPI on Windows and a master-key-backed AES-256-GCM vault on Linux/Docker.
- Docker deployments require the master key through a mounted secret or supported external secret provider, not an image/environment default.
- API responses expose secret presence and last update time, never plaintext.
- Logs pass through structured redaction for passwords, tokens, connection strings, URI credentials, command arguments, and headers.
- Local UI binds to loopback by default. Remote binding requires TLS and authentication.
- Destructive restore and retention actions require explicit target resolution, authorization, and audit records.
- Plugins are allow-listed and verified by package hash/signature before loading.
- The Telegram token present in the original script is considered compromised and must be replaced before use.

## 9. User interface

### Setup Wizard

1. Runtime and data paths
2. Database type and connection
3. Database discovery and selection
4. Compression/encryption
5. Destinations
6. Schedule and retention
7. Notifications
8. Full preflight test
9. Save and optionally run now

Every integration step has a visible **Test connection** action and field-level remediation messages.

### Operational screens

- Overview: latest status, next runs, storage usage, failing agents.
- Jobs: create, clone, edit, enable, disable, run, cancel.
- Live Run: stage progress and streaming redacted logs.
- History: filters, artifacts, checksums, duration, retry details.
- Restore Center: browse verified backups, preflight, restore, audit.
- Connections, Destinations, Notifications, Credentials, Plugins.
- Agents and Audit Log when connected to the Control Plane.

## 10. Persistence

Local Agent uses SQLite with migrations and WAL mode. Principal entities are `Agent`, `Plugin`, `Connection`, `SecretReference`, `BackupJob`, `Schedule`, `Destination`, `RetentionPolicy`, `JobRun`, `StageRun`, `Artifact`, `Transfer`, `NotificationDelivery`, `AuditEvent`, and `OutboxMessage`.

Job definitions are versioned snapshots. A run always points to the exact snapshot used, so later edits do not alter historical meaning.

## 11. Error handling and recovery

- Errors are classified as validation, dependency, authentication, connectivity, capacity, database, archive, transfer, verification, cancellation, or internal.
- Retry applies only to transient classes and uses bounded exponential backoff with jitter.
- Database backups are restarted rather than resumed unless the database plugin explicitly supports resume.
- Multipart transfers store checkpoints and resume when the provider supports it.
- Agent restart marks interrupted stages and applies the stage recovery policy; it never invents success from file presence alone.
- Cleanup failures generate warnings and remain visible; they do not overwrite the primary failure.
- Notifications are retried independently and cannot turn a successful verified backup into a failed backup. Their delivery failure is prominently recorded.

## 12. Packaging and operation

- Windows: self-contained x64 installer, Windows Service, Start Menu link to local UI, upgrade/uninstall support.
- Linux: deb/rpm packages and systemd unit.
- Docker: multi-stage image, non-root user, health check, mounted data/staging/secret volumes.
- Docker Compose examples include Agent standalone and Control Plane stacks.
- External database client binaries are checked at startup. Native installers can install supported dependencies or provide exact remediation; container images include compatible clients where licensing permits.
- Configuration has schema versioning and upgrade migrations.

## 13. Testing and acceptance

- Unit tests for state transitions, retry, retention, redaction, schedule/timezone, and plugin compatibility.
- Contract tests shared by every plugin implementation.
- Integration tests using containerized SQL Server, PostgreSQL, and MySQL; Oracle tests run against an explicitly licensed environment.
- End-to-end test: configure SQL Server job in UI, run it, verify archive and remote checksum, inspect history, and receive notification.
- Failure tests cover one database failing in a batch, disk full, wrong credentials, missing tool, transfer interruption, checksum mismatch, notification outage, restart during each stage, and retention boundaries.
- Packaging smoke tests run on supported Windows, Linux, and Docker targets.

Release 1 is accepted only when the supplied script workflow can be configured entirely through the UI and produces an honest terminal status with verifiable artifacts.

## 14. Explicit decisions

- The product is a modular monolith with plugins, not a fleet of microservices.
- The Agent is authoritative for local execution; the Control Plane is eventually consistent.
- Agent-to-control-plane communication is outbound initiated.
- SQLite is used locally; PostgreSQL is used centrally.
- Release sequencing is mandatory to make the full platform shippable and testable.
- Credentials are referenced, never embedded in portable job exports.
- A backup is not successful until required verification completes.

