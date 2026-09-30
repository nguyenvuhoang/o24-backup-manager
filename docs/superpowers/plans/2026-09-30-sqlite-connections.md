# SQLite connections implementation ledger

User specification: attachment df87305b-e6a3-481e-950d-0f366ab0fbb8.
Direct implementation authorized; no proposal or approval gate. Workspace has no Git repository.

1. Replace connection/job persistence with EF Core SQLite and initial migration; retain JSON run history and import legacy configuration once without deleting originals.
2. Store AES-GCM ciphertext in DatabaseConnections, flatten safe responses, add saved-ID operations, preserve empty-password edits and FK conflicts.
3. Reuse modal for save/edit, add saved selector and settings manager; preserve port rules.
4. Verify migration/restart persistence, password safety, relationships, UI and builds; independent final review.

Ruling: existing legacy inline jobs are imported with their old configuration until explicitly converted; new jobs require ConnectionId. This preserves existing jobs while enforcing the new contract for new writes.
Pre-flight: API/UI response changes must ship together; backup runner must resolve SQLite credentials internally, never through browser or job fields.

## Completion ledger

Tasks 1-3 implemented: SQLite store and generated initial migration, safe flat DTOs, AES-GCM ciphertext, saved-ID API, shared save/edit modal, selector and settings manager.
Verification: 72 backend assertions, 13 frontend tests, 8 live HTTP failure-path checks; frontend production build and backend Release build succeeded (0 backend warnings/errors).
Independent review: one P2 stale test-result race fixed with cancellation on new selection/disconnect/unmount; LatestRequest tests pass. Populated legacy import coverage added and passed.
Ruling: legacy inline jobs remain supported on import; new jobs require ConnectionId. Source JSON retained for recovery and never reimported after the marker is committed.
Live limit: Windows Authentication to 192.168.1.138:1433 failed; no saved credentials or configured MasterKey available. User asked asynchronously to provide runtime credentials via UI (not chat). HTTP host-restart tests use a deterministic provider only in test assembly; full live SQL/frontend restart verification remains pending those inputs.
