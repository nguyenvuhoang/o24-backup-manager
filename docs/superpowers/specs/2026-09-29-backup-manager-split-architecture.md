# Backup Manager — Split Frontend/Backend Design

## 1. Objective

Restructure Backup Manager into two independently deployable applications:

- **Frontend:** Next.js 16, React 19, TypeScript, Tailwind CSS.
- **Backend:** ASP.NET Core Web API .NET 10 with the backup engine and all privileged integrations.

The browser must never connect directly to SQL Server, execute operating-system commands, access backup files, or receive plaintext credentials. The backend remains the only trusted execution boundary.

## 2. Repository structure

```text
backup-manager/
├── frontend/
│   ├── src/app/
│   ├── src/components/
│   ├── src/features/
│   ├── src/lib/api/
│   └── src/types/
├── backend/
│   ├── src/BackupManager.API/
│   ├── src/BackupManager.Application/
│   ├── src/BackupManager.Domain/
│   ├── src/BackupManager.Infrastructure/
│   └── src/BackupManager.Plugins.SqlServer/
│   └── tests/
└── deploy/
    ├── docker-compose.yml
    └── windows/
```

The existing combined source becomes migration input. Its static `wwwroot` interface is replaced by the Next.js application; reusable backend logic is moved into the appropriate .NET projects.

## 3. Frontend responsibilities

The frontend provides:

- overview dashboard;
- initial setup wizard;
- connection and database discovery screens;
- backup-job create/edit/clone/enable/disable;
- storage, notification, schedule, and retention configuration;
- run-now and cancel actions;
- live stage progress and redacted logs;
- backup history and artifact/checksum details;
- responsive Vietnamese-first UI with English-ready copy.

The frontend does not persist secrets. Secret inputs are sent once to the backend and replaced in UI state by `{ configured: true, updatedAt }` metadata.

## 4. Backend responsibilities

The backend provides:

- REST API under `/api/v1`;
- SignalR hub at `/hubs/runs`;
- job validation and persistence;
- encrypted secret storage;
- scheduling and per-job concurrency control;
- SQL Server discovery, backup, and verification;
- archive creation and SHA-256 hashing;
- SFTP/SCP transfer and verification;
- retention processing;
- Telegram notification;
- durable run/stage/artifact history;
- Windows Service and Docker hosting.

The first database plugin uses `sqlcmd`. Passwords are passed with `SQLCMDPASSWORD`, never command-line arguments. SQL identifiers and file paths are generated and escaped by the backend; the API never accepts raw SQL.

## 5. Backend project boundaries

| Project | Responsibility |
|---|---|
| `BackupManager.Domain` | Job definitions, snapshots, run/stage/artifact state and invariants |
| `BackupManager.Application` | Use cases, plugin interfaces, orchestration, DTO contracts |
| `BackupManager.Infrastructure` | SQLite/EF Core, vault, scheduler, process runner, filesystem |
| `BackupManager.Plugins.SqlServer` | SQL Server discovery, backup, `RESTORE VERIFYONLY` |
| `BackupManager.API` | HTTP endpoints, validation, SignalR, DI and hosting |

Vendor-specific dependencies must not enter Domain or Application.

## 6. API contract

### Jobs

- `GET /api/v1/jobs`
- `GET /api/v1/jobs/{id}`
- `POST /api/v1/jobs`
- `PUT /api/v1/jobs/{id}`
- `POST /api/v1/jobs/{id}/run`
- `POST /api/v1/jobs/{id}/cancel`
- `POST /api/v1/jobs/{id}/enable`
- `POST /api/v1/jobs/{id}/disable`

### Connections and secrets

- `POST /api/v1/connections/sql-server/test`
- `POST /api/v1/connections/sql-server/databases`
- `POST /api/v1/secrets`
- `GET /api/v1/secrets/{name}/metadata`

### Runs

- `GET /api/v1/runs`
- `GET /api/v1/runs/{id}`
- `GET /api/v1/runs/{id}/logs`

All errors use RFC 7807 `ProblemDetails`. Validation errors map fields consistently so the frontend can display them next to the corresponding controls.

## 7. Realtime contract

SignalR hub events:

- `RunStarted`
- `StageStarted`
- `StageProgress`
- `StageCompleted`
- `RunCompleted`
- `LogAppended`

Every event includes `runId`, `jobId`, timestamp, and an increasing sequence number. The frontend reconnects automatically and reloads the current run through REST after reconnection, avoiding dependence on missed transient events.

## 8. Frontend design

Next.js uses App Router and feature-based modules:

```text
src/features/jobs
src/features/connections
src/features/runs
src/features/history
src/features/settings
```

Server Components render page shells and initial data where appropriate. Interactive forms, SignalR subscriptions, and live progress are Client Components. A single generated typed API client is used; components do not call `fetch` directly.

Tailwind provides layout and tokens. Reusable accessible primitives cover inputs, dialogs, tables, badges, toasts, and confirmation flows. Destructive modals do not close by clicking outside.

## 9. Development and deployment

### Development

- Frontend: `http://localhost:3000`
- Backend: `http://localhost:5088`
- Next.js rewrites `/api/*` and `/hubs/*` to the backend.
- Backend CORS allows only the configured frontend origin when direct access is used.

### Docker

Docker Compose contains separate `frontend` and `backend` services. Only the frontend is exposed publicly by default; the backend is reachable on the internal network. Persistent volumes contain backend state and backup staging.

### Windows native

The backend publishes self-contained and runs as a Windows Service. The frontend builds in standalone mode and runs as a separate Windows service/process, or is reverse-proxied by IIS/Nginx.

## 10. Persistence and security

- SQLite with EF Core is the default local control database.
- Job definitions use immutable execution snapshots.
- Secrets use DPAPI on Windows and AES-256-GCM with an external master key on Linux/Docker.
- API payloads and logs never expose plaintext secrets.
- Local development may bind backend to loopback; remote access requires TLS and authentication.
- The previously exposed Telegram token is never included in source, examples, history, or seed data.

## 11. Testing

- Domain and application behavior: xUnit.
- Infrastructure and API integration: xUnit with temporary SQLite and fake process/plugin adapters.
- Frontend components: Vitest and Testing Library.
- Full workflow: Playwright with a fake backend execution adapter.
- Production adapter smoke tests run separately on a machine with .NET 10, `sqlcmd`, SQL Server, and SSH/SFTP access.

The source-generation workspace lacks the .NET SDK. Frontend tests can run here; backend source must be built and tested in a .NET 10 environment before production deployment.

## 12. Migration rules

- Preserve the existing pipeline behavior and job fields while moving code.
- Remove `wwwroot` only after equivalent Next.js screens exist.
- Keep REST DTOs separate from domain entities.
- Do not let frontend-specific naming leak into the domain.
- Do not add PostgreSQL/Oracle/MySQL plugins during this restructuring; they remain later plugin packages.
- The deliverable is a new downloadable source ZIP containing both applications and deployment assets.

## 13. Acceptance criteria

- `frontend` and `backend` start independently.
- The frontend can create a job, discover databases, run/cancel a backup, and show live state using only backend contracts.
- Refreshing the page preserves jobs and history.
- A missing backend produces a clear connection error rather than a blank page.
- No secret appears in browser responses, generated client types, command arguments, or logs.
- Docker Compose and Windows deployment instructions describe both processes explicitly.
