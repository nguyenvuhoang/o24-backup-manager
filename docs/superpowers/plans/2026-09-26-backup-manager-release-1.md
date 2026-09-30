# Backup Manager Release 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a runnable local Backup Manager that configures and executes SQL Server backup, verification, ZIP packaging, SFTP transfer, retention, and Telegram notification from a web UI.

**Architecture:** A .NET 10 Agent modular monolith owns execution, scheduling, SQLite persistence, secrets, and plugin contracts. A Next.js 16 UI consumes its local REST/SSE API; database and destination behavior is isolated behind plugin interfaces so later releases add providers without changing orchestration.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core SQLite, xUnit, FluentAssertions, Quartz.NET, SSH.NET, Next.js 16, React, TypeScript, MUI, Vitest, Testing Library, Playwright, Docker Compose.

**Spec:** `docs/superpowers/specs/2026-09-26-backup-manager-design.md`

## Global Constraints

- A run is successful only after all required database backups and destination verifications succeed.
- Local artifacts are never removed before all required remote copies verify successfully.
- Secrets are stored by reference, redacted from logs, and never returned as plaintext.
- Local UI binds to loopback by default; remote binding requires authentication and TLS configuration.
- Release 1 supports Windows native hosting and Docker.
- Every execution persists run, stage, artifact, checksum, duration, and classified error data.
- Use test-driven development and commit each task independently.

## Review Focus

- A batch where one database fails must end `Failed`, skip transfer/retention, and preserve successful `.bak` files for diagnosis (Task 4).
- A checksum mismatch after upload must fail verification and retain the local archive (Task 5).
- Secrets embedded in connection strings, command arguments, Telegram URLs, or exception messages must be redacted (Task 2).
- Two triggers for the same job must result in one active run and one rejected/conflict response (Task 6).
- Restart during a running stage must mark the prior run interrupted/failed and never infer success from leftover files (Task 6).

---

## File structure

```text
BackupManager.sln
src/
  BackupManager.Domain/              # job/run model and state rules
  BackupManager.Application/         # orchestration contracts and use cases
  BackupManager.Infrastructure/      # SQLite, process runner, vault, scheduler
  BackupManager.Agent/               # HTTP API, SSE, service host
  BackupManager.Plugins.SqlServer/   # sqlcmd backup and verification
  BackupManager.Plugins.Sftp/        # SSH.NET transfer and verification
  BackupManager.Plugins.Telegram/    # notification provider
  BackupManager.Web/                 # Next.js local UI
tests/
  BackupManager.Domain.Tests/
  BackupManager.Application.Tests/
  BackupManager.Infrastructure.Tests/
  BackupManager.Agent.Tests/
  BackupManager.Plugins.SqlServer.Tests/
  BackupManager.Plugins.Sftp.Tests/
  BackupManager.Plugins.Telegram.Tests/
  BackupManager.E2E/
deploy/
  docker-compose.yml
  windows/install-service.ps1
  windows/uninstall-service.ps1
```

### Task 1: Domain model and solution foundation

**Files:**
- Create: `BackupManager.sln`, `Directory.Build.props`, `Directory.Packages.props`
- Create: `src/BackupManager.Domain/Jobs/*.cs`, `src/BackupManager.Domain/Runs/*.cs`
- Test: `tests/BackupManager.Domain.Tests/RunStateTests.cs`, `JobDefinitionTests.cs`

**Interfaces:**
- Produces: `BackupJob`, `JobDefinitionSnapshot`, `JobRun`, `StageRun`, `Artifact`, `RunStatus`, `StageStatus`, `ErrorCategory`; `JobRun.Start()`, `StartStage(StageKind)`, `CompleteStage(...)`, `FailStage(...)`, `Complete()`.

- [ ] Write failing domain tests for legal state transitions, immutable job snapshots, required-destination failure, and terminal-state protection.
- [ ] Run `dotnet test tests/BackupManager.Domain.Tests` and confirm failures reference missing domain types.
- [ ] Create solution/projects and implement the minimal domain model and invariants.
- [ ] Run `dotnet test tests/BackupManager.Domain.Tests` and confirm all tests pass.
- [ ] Commit with `feat: add backup job and run domain model`.

### Task 2: Persistence, secret vault, and redacted logging

**Files:**
- Create: `src/BackupManager.Application/Abstractions/IBackupStore.cs`, `ISecretVault.cs`, `IRedactor.cs`
- Create: `src/BackupManager.Infrastructure/Persistence/BackupDbContext.cs`, `SqliteBackupStore.cs`
- Create: `src/BackupManager.Infrastructure/Security/LocalSecretVault.cs`, `StructuredRedactor.cs`
- Test: `tests/BackupManager.Infrastructure.Tests/PersistenceTests.cs`, `SecretVaultTests.cs`, `StructuredRedactorTests.cs`

**Interfaces:**
- Consumes: domain entities from Task 1.
- Produces: async CRUD and run-history methods through `IBackupStore`; `StoreAsync`, `ResolveAsync`, `DeleteAsync` through `ISecretVault`; `Redact(string)` and structured-value redaction through `IRedactor`.

- [ ] Write failing SQLite round-trip/migration tests, encrypted-secret round-trip tests, and redaction tests covering connection strings, CLI passwords, Telegram bot URLs, URI userinfo, and nested exception messages.
- [ ] Run `dotnet test tests/BackupManager.Infrastructure.Tests` and confirm expected failures.
- [ ] Implement EF Core mappings/migrations, OS-aware encrypted vault, and structured redactor without plaintext API serialization.
- [ ] Run infrastructure and domain tests and confirm all pass.
- [ ] Commit with `feat: add persistence vault and log redaction`.

### Task 3: Plugin contracts and hardened process execution

**Files:**
- Create: `src/BackupManager.Application/Plugins/*.cs`
- Create: `src/BackupManager.Application/Execution/IProcessRunner.cs`
- Create: `src/BackupManager.Infrastructure/Execution/ProcessRunner.cs`
- Test: `tests/BackupManager.Application.Tests/PluginContractTests.cs`
- Test: `tests/BackupManager.Infrastructure.Tests/ProcessRunnerTests.cs`

**Interfaces:**
- Produces: `IDatabaseBackupPlugin`, `IStoragePlugin`, `INotificationPlugin`, configuration schema/capability records, `ProcessSpec`, `ProcessResult`, and streaming progress callback.

- [ ] Write failing contract tests for plugin metadata/version compatibility and process tests for timeout, cancellation, bounded output, exit code, and secret argument redaction.
- [ ] Run targeted tests and confirm failures.
- [ ] Implement contracts, plugin registry, compatibility validation, and argument-list-based process runner without shell interpolation.
- [ ] Run targeted tests and confirm all pass.
- [ ] Commit with `feat: add plugin contracts and safe process runner`.

### Task 4: SQL Server backup and archive pipeline

**Files:**
- Create: `src/BackupManager.Plugins.SqlServer/SqlServerBackupPlugin.cs`, `SqlServerDiscovery.cs`, `SqlServerCommandFactory.cs`
- Create: `src/BackupManager.Application/Execution/BackupPipeline.cs`
- Create: `src/BackupManager.Infrastructure/Archives/ZipArchiveService.cs`
- Test: `tests/BackupManager.Plugins.SqlServer.Tests/*.cs`
- Test: `tests/BackupManager.Application.Tests/BackupPipelineTests.cs`

**Interfaces:**
- Consumes: plugin/process/store/redactor contracts from Tasks 2–3.
- Produces: database discovery, tool preflight, backup/`RESTORE VERIFYONLY`, ZIP creation, SHA-256 artifacts, and `BackupPipeline.ExecuteAsync(jobId, trigger, cancellationToken)`.

- [ ] Write failing tests for safe identifier quoting, database discovery filtering, sqlcmd exit/SQL-error detection, missing/empty file, verify failure, ZIP/checksum creation, and one-database-fails batch behavior.
- [ ] Run SQL Server and application tests and confirm failures.
- [ ] Implement the SQL Server plugin and orchestration through verified local archive creation.
- [ ] Run targeted tests and confirm all pass.
- [ ] Commit with `feat: execute verified sql server backups`.

### Task 5: SFTP, retention, and Telegram completion

**Files:**
- Create: `src/BackupManager.Plugins.Sftp/SftpStoragePlugin.cs`
- Create: `src/BackupManager.Plugins.Telegram/TelegramNotificationPlugin.cs`
- Create: `src/BackupManager.Application/Retention/RetentionService.cs`
- Modify: `src/BackupManager.Application/Execution/BackupPipeline.cs`
- Test: corresponding plugin tests and `RetentionServiceTests.cs`

**Interfaces:**
- Consumes: pipeline artifacts from Task 4 and secret references from Task 2.
- Produces: upload/size/checksum verification, safe retention candidate calculation, and terminal notification delivery records.

- [ ] Write failing tests for interrupted upload, checksum mismatch, required versus optional destination, two-day ZIP retention boundary, fourteen-day log boundary, Telegram escaping/error handling, and preservation of local files on verification failure.
- [ ] Run targeted tests and confirm failures.
- [ ] Implement SFTP upload to temporary remote name then atomic rename, verification, retention, and independent notification delivery.
- [ ] Run targeted tests and confirm all pass.
- [ ] Commit with `feat: add verified sftp delivery retention and telegram`.

### Task 6: Scheduler, recovery, and Agent API

**Files:**
- Create: `src/BackupManager.Infrastructure/Scheduling/QuartzJobScheduler.cs`
- Create: `src/BackupManager.Agent/Endpoints/{Jobs,Connections,Runs,Secrets,Plugins}Endpoints.cs`
- Create: `src/BackupManager.Agent/Streaming/RunEventStream.cs`
- Test: `tests/BackupManager.Agent.Tests/*.cs`, scheduler/recovery tests

**Interfaces:**
- Consumes: pipeline from Task 4–5 and persistence from Task 2.
- Produces: versioned `/api/v1` endpoints, RFC 7807 errors, SSE run events, run-now/cancel, persistent schedules, per-job locks, and startup recovery.

- [ ] Write failing API tests for validation, no plaintext secrets, test-connection flow, run lifecycle/cancellation, concurrent trigger conflict, SSE events, and restart interruption recovery.
- [ ] Run Agent tests and confirm failures.
- [ ] Implement Quartz scheduling, locks/recovery, REST endpoints, OpenAPI, health/readiness, and SSE streaming.
- [ ] Run all backend tests and confirm all pass.
- [ ] Commit with `feat: expose backup agent api and scheduling`.

### Task 7: Next.js operational UI

**Files:**
- Create: `src/BackupManager.Web/app/**`, `features/setup/**`, `features/jobs/**`, `features/runs/**`, `lib/api/**`
- Test: `src/BackupManager.Web/**/*.test.tsx`, `tests/BackupManager.E2E/setup-and-run.spec.ts`

**Interfaces:**
- Consumes: `/api/v1` REST/OpenAPI and SSE from Task 6.
- Produces: setup wizard, overview, jobs, live run, history, connections/destinations/notifications/credentials screens.

- [ ] Write failing component tests for wizard validation, database selection, secret-presence display, preflight failures, and live stage progress.
- [ ] Run `npm test` and confirm failures.
- [ ] Implement generated typed API client, Vietnamese/English-ready MUI UI, wizard, operational screens, and accessible status/error feedback.
- [ ] Run component tests and confirm all pass.
- [ ] Write and run Playwright flow for configure -> test -> save -> run -> inspect verified artifact using mocked external processes.
- [ ] Commit with `feat: add backup manager web interface`.

### Task 8: Packaging and release verification

**Files:**
- Create: `src/BackupManager.Agent/Dockerfile`, `deploy/docker-compose.yml`
- Create: `deploy/windows/install-service.ps1`, `uninstall-service.ps1`
- Create: `.github/workflows/ci.yml`, `README.md`, `.env.example`
- Test: packaging smoke scripts and full test suite

**Interfaces:**
- Consumes: complete Agent and Web application.
- Produces: Windows service install/uninstall, Docker image/Compose deployment, health checks, persistent volumes, and operator documentation.

- [ ] Write smoke checks for Docker health, persisted SQLite/history after restart, loopback binding default, missing dependency diagnostics, and clean Windows service command generation.
- [ ] Build production UI and publish self-contained Agent.
- [ ] Build and start Docker Compose, run health/readiness and mocked end-to-end backup, restart, and verify persisted history.
- [ ] Run `dotnet test`, frontend tests, Playwright, and secret scan; require zero failures and no embedded credential from the original script.
- [ ] Document installation, first-run wizard, SQL Server filesystem permission, SSH-key setup, upgrades, logs, and troubleshooting.
- [ ] Commit with `chore: package backup manager release 1`.

## Completion gate

Release 1 is complete when a clean Windows Server or Docker host can configure the EMI CMS database list through the UI, execute the complete pipeline, display an honest per-stage result, retain verified artifacts according to policy, and send a Telegram summary without any credential in source or logs.

