# Database Connection persistence

## Before

CRUD already existed. DatabaseConnectionService + JsonStore wrote connections.json and jobs.json, with references to AES-GCM passwords in secrets.json. Test/Discover were transient. JobForm created the connection only when saving a job; reloading before Save Job lost the React configuration.

## After

Infrastructure owns MetadataDbContext and SqliteConfigurationStore (EF Core SQLite 10.0.9; SQLitePCLRaw 3.0.5). Domain/Application have no EF/SQLite dependency.

Default file: backend/src/BackupManager.API/data/backupmanager.db, relative to API ContentRoot when deployed. Startup applies migration 20260930070136_InitialMetadata automatically.

Tables:
- DatabaseConnections: flat settings, EncryptedPassword, timestamps; Name/Provider/Host required.
- BackupJobs: ConnectionId and job settings; new jobs have no copied connection credentials.
- BackupJobDatabases: composite key BackupJobId + DatabaseName.
- MetadataStates: one-time legacy import marker. EF also maintains migration tables.

Connection -> jobs uses RESTRICT. Job -> databases uses CASCADE. Delete referenced connection returns 409 CONNECTION_IN_USE. Changing the server target of a referenced connection is rejected; job save checks connection version after discovery.

Legacy JSON connections/jobs import transactionally once, retaining IDs and references. Secret references are resolved and re-encrypted into SQLite. Original files remain untouched for recovery; deleted objects do not return on restart. Legacy inline jobs stay readable until converted; new jobs require ConnectionId. Run history and non-connection legacy secrets retain their existing stores.

## API

Base: /api/v1/database-connections

| Method | Path | Behavior |
|---|---|---|
| GET | / | Safe flat metadata list |
| GET | /{id} | Safe flat metadata |
| POST | / | Require name, test, discover, encrypt and persist |
| PUT | /{id} | Same ID; blank/null password preserves ciphertext |
| DELETE | /{id} | Delete unused connection; 409 when referenced |
| POST | /test | Transient options, no persistence |
| POST | /discover | Transient options, no persistence |
| POST | /{id}/test | Resolve stored credentials internally |
| POST | /{id}/discover | Resolve stored credentials internally |
| POST | /{id}/test-options | Test unsaved edits, reuse stored password when blank; no persistence |

Responses expose metadata, HasPassword and timestamps, never Password, EncryptedPassword or secret references. Errors contain safe messages, not SQL exception objects.

## Password and backup

SecretVault encrypts with AES-GCM, random 12-byte nonce and 16-byte tag. Base64 ciphertext lives directly in DatabaseConnections.EncryptedPassword. BackupManager__MasterKey must contain at least 32 characters and remain stable across restarts; preserve the key separately from the database. The README configuration remains required: an empty key cannot persist SQL credentials.

BackupPipeline reads one connection snapshot and decrypts internally, passing the password to sqlcmd via SQLCMDPASSWORD environment. Jobs/browser never receive it. sqlcmd -x disables variable substitution. New jobs require sqlcmd ODBC supporting -N[s|m|o] to honor Encrypt. Existing backup/verify/ZIP/hash/SCP/retention/Telegram stages remain.

## UI and ports

Cấu hình -> Kết nối Database includes list, Add, Test, Edit, Delete. A shared modal requires connection name and offers Test / Cancel / Save & Connect. Save persists independently of Save Job; edits use PUT with an initially blank password. Create Job loads GET connections, discovers by saved ID and submits ConnectionId + database names. Failed discovery keeps saved metadata visible for retry/configuration. Requests from old selections cannot overwrite current notices.

Central defaults: SQL Server 1433, PostgreSQL 5432, Oracle 1521. Only SQL Server is currently implemented. Empty/legacy ports normalize on both frontend/backend; custom ports remain unchanged. Named-instance discovery is explicitly selected without a TCP port. TCP takes priority when a port exists. Database and InstanceName are independent fields.

## Verification

- Frontend production build and backend Release build succeeded.
- Backend: 72 assertions covering EF migration, ciphertext and blank-password edits, populated legacy import, concurrency/version checks, FK enforcement and actual HTTP POST -> stop/dispose host -> new host -> GET same ID -> saved-ID test/discover -> create job -> delete conflict.
- HTTP persistence tests use a deterministic provider only in the test assembly; they do not prove connectivity to the user's SQL Server.
- Browser: manager, required name, default 1433, custom 1500 restored after toggling discovery, Save & Connect footer.
- Live Windows Authentication to 192.168.1.138:1433 failed. No production credentials or configured MasterKey were available. Full live SQL + frontend-restart verification still requires those inputs; no live success is claimed.

Commands:

```powershell
dotnet build backend/BackupManager.slnx -c Release
dotnet run --project backend/tests/BackupManager.ContractTests -c Release
cd frontend
npm test
npm run build
```

Migration development from backend: dotnet tool restore, then dotnet tool run dotnet-ef migrations add <Name> --project src/BackupManager.Infrastructure --output-dir Persistence/Migrations.

Runtime *.db, *.db-shm, *.db-wal and API data/ remain ignored. Runtime database files are not source artifacts.
