# Split Frontend/Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the combined ASP.NET static UI with an independent Next.js 16 frontend and a cleanly separated ASP.NET Core .NET 10 backend while preserving the existing backup pipeline.

**Architecture:** Backend projects expose `/api/v1` REST and `/hubs/runs` SignalR contracts; frontend consumes only generated typed contracts. Docker Compose and native instructions start both applications, while privileged SQL Server, filesystem, SCP, secret, and Telegram operations remain exclusively in the backend.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core SQLite, SignalR, xUnit; Next.js 16, React 19, TypeScript, Tailwind CSS, Vitest, Testing Library, Playwright.

**Spec:** `docs/superpowers/specs/2026-09-29-backup-manager-split-architecture.md`

## Global Constraints

- Frontend and backend must start independently.
- Browser code must never execute SQL/OS commands or receive plaintext secrets.
- Backend API prefix is `/api/v1`; realtime hub is `/hubs/runs`.
- Existing SQL Server backup behavior and job fields must be preserved during migration.
- `wwwroot` is removed only after the equivalent Next.js flow exists.
- Backend source targets .NET 10 even though the current generation workspace cannot compile it.
- Frontend uses Next.js 16, React 19, TypeScript, App Router, and Tailwind CSS.

## Review Focus

- Frontend reload during an active run must recover current state through REST after SignalR reconnect (Task 4).
- API responses and generated types must expose secret metadata only, never values (Task 2).
- Duplicate run requests for one job must return conflict and produce only one execution (Task 3).
- A backend outage must render a clear reconnect/error state instead of a blank frontend (Task 4).
- Docker paths must distinguish Agent-visible paths from SQL Server-visible backup paths (Task 5).

---

### Task 1: Split backend solution boundaries

**Files:**
- Create: `backend/BackupManager.slnx`
- Create: `backend/src/BackupManager.Domain/**`
- Create: `backend/src/BackupManager.Application/**`
- Create: `backend/src/BackupManager.Infrastructure/**`
- Create: `backend/src/BackupManager.Plugins.SqlServer/**`
- Create: `backend/src/BackupManager.API/**`
- Create: `backend/tests/**`
- Migrate from: `src/BackupManager.Agent/**`

**Interfaces:**
- Produces: domain entities; `IBackupStore`, `ISecretVault`, `IProcessRunner`, `IDatabaseBackupPlugin`; `IBackupPipeline.ExecuteAsync(Guid, CancellationToken)`.

- [ ] Write failing domain/application tests for state transitions, immutable job snapshots, required stages, cancellation, and plugin contracts.
- [ ] Run backend tests and confirm failures identify missing split projects/types.
- [ ] Create project references and migrate existing model, vault, process, SQL Server, and pipeline code behind application interfaces.
- [ ] Run backend tests in a .NET 10 environment and require all pass; in this workspace, run XML/source/static checks and record the SDK limitation.
- [ ] Commit `refactor: split backup manager backend projects`.

### Task 2: Versioned REST API, persistence, and secret-safe contracts

**Files:**
- Create: `backend/src/BackupManager.API/Endpoints/**`
- Create: `backend/src/BackupManager.Application/Contracts/**`
- Create: `backend/src/BackupManager.Infrastructure/Persistence/**`
- Test: `backend/tests/BackupManager.API.Tests/**`

**Interfaces:**
- Consumes: Task 1 application interfaces.
- Produces: `/api/v1/jobs`, `/connections/sql-server/*`, `/secrets`, `/runs`; OpenAPI document; secret metadata DTO.

- [ ] Write failing API contract tests for CRUD, discovery, RFC 7807 validation, pagination, run actions, and absence of plaintext secret fields.
- [ ] Implement EF Core SQLite persistence, DTO mapping, validation, versioned endpoints, CORS configuration, and OpenAPI.
- [ ] Verify API contract tests and inspect OpenAPI for forbidden secret-value properties.
- [ ] Commit `feat: add versioned backup manager api`.

### Task 3: SignalR execution progress and concurrency

**Files:**
- Create: `backend/src/BackupManager.API/Hubs/RunHub.cs`
- Create: `backend/src/BackupManager.Application/Execution/RunEvent.cs`
- Modify: pipeline and run coordinator files
- Test: execution and hub integration tests

**Interfaces:**
- Produces: `RunStarted`, `StageStarted`, `StageProgress`, `StageCompleted`, `RunCompleted`, `LogAppended`, each with run/job IDs, timestamp, and sequence.

- [ ] Write failing tests for monotonic sequences, redacted log events, duplicate-run conflict, cancellation, and terminal completion events.
- [ ] Implement event publisher, SignalR adapter, per-job lock, and startup recovery semantics.
- [ ] Run execution/hub tests and require all pass.
- [ ] Commit `feat: stream backup execution progress`.

### Task 4: Next.js 16 operational frontend

**Files:**
- Create: `frontend/package.json`, `next.config.ts`, `tsconfig.json`, Tailwind/PostCSS configuration
- Create: `frontend/src/app/**`
- Create: `frontend/src/features/{dashboard,jobs,connections,runs,history,settings}/**`
- Create: `frontend/src/lib/api/**`, `frontend/src/lib/signalr/**`
- Test: `frontend/src/**/*.test.tsx`, `frontend/e2e/**`

**Interfaces:**
- Consumes: Task 2 OpenAPI and Task 3 SignalR events.
- Produces: responsive Vietnamese-first dashboard, setup/job wizard, live run, history, settings, typed API client, and reconnect recovery.

- [ ] Scaffold Next.js 16/React 19/Tailwind and write failing tests for navigation, job validation, configured-secret display, API outage, live progress, and reconnect REST recovery.
- [ ] Run frontend tests and confirm failures before components exist.
- [ ] Implement shared UI primitives, application shell, feature pages, typed API client, SignalR client, loading/error/empty states, and no-outside-dismiss confirmations.
- [ ] Run typecheck, lint, component tests, and Playwright mocked workflow.
- [ ] Remove backend `wwwroot` after equivalent flows pass.
- [ ] Commit `feat: add nextjs backup manager frontend`.

### Task 5: Dual-application packaging and downloadable source

**Files:**
- Modify: `deploy/docker-compose.yml`
- Create: `backend/src/BackupManager.API/Dockerfile`
- Create: `frontend/Dockerfile`
- Create/modify: `deploy/windows/**`, root `README.md`, `.env.example`

**Interfaces:**
- Consumes: Task 1–4 deployables.
- Produces: internal backend/frontend Docker networking, Next.js proxy/rewrites, Windows service/process instructions, and final ZIP.

- [ ] Add smoke checks for frontend health, backend health, proxy routing, persistent state, secret environment requirement, and explicit SQL Server-visible backup path documentation.
- [ ] Implement Dockerfiles/Compose, frontend standalone output, Windows scripts, and complete local/native/Docker instructions.
- [ ] Run available frontend/static/ZIP validation; document backend build commands that must run on a .NET 10 host.
- [ ] Scan the archive for the previously exposed Telegram credential and other embedded secrets.
- [ ] Create `BackupManager-split-dotnet10-next16-source.zip`, validate archive integrity, calculate SHA-256, and commit `chore: package split backup manager source`.

## Completion gate

The migration is complete when the source archive contains independent frontend/backend applications, frontend tests and static checks pass in this workspace, backend source passes static validation here, all backend build/test commands are documented for a .NET 10 host, and no credential is embedded in source or artifacts.
