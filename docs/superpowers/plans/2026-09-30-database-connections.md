# Database connection and create-job implementation plan

> Execute inline with superpowers:executing-plans. User explicitly approved direct implementation without further design gates.

**Goal:** Real SQL Server discovery, secure connection persistence, responsive create-job console, and connection resolution by the existing backup engine.
**Architecture:** Domain holds configuration and connection references; Application owns validation/use cases/provider contracts; Infrastructure implements JSON and vault storage; SqlServer plugin owns SqlClient and sqlcmd configuration; API maps HTTP; React consumes the existing API client.
**Stack:** Next.js 16, React 19, Tailwind 4, .NET 10, Microsoft.Data.SqlClient.
**Spec:** User's two attached requests in this task.

## Constraints and review focus
- Preserve `/api/v1`, legacy jobs, sqlcmd backup/verify, ZIP/SCP/Telegram, JSON, AES-GCM. No auth scope.
- Never serialize credentials in responses or jobs. Empty update password preserves existing secret.
- Explicit port selects TCP host:port; instance is used only without port, explained in UI.
- Cancel modal retains active connection; submitting a changed connection invalidates old selection, including failures.
- Missing/inaccessible metadata is nullable, never invented. Only ONLINE and accessible databases selectable.
- No Git metadata exists in supplied workspace: direct edits, no worktree or commits.

## Tasks
- [x] Backend contract tests: validation, DataSource resolution, safe serialization, secrets preserve/replace, legacy/new job execution configuration, offline selection and referenced-delete protection.
- [x] Domain/Application: `DatabaseConnection`, configuration, provider/repository/vault abstractions, connection and job services. Tests start red, then run console contract suite.
- [x] Infrastructure/plugin/API: serialized atomic JSON mutations, vault adapter, SqlClient test/discovery, clean HTTP errors, CRUD, job integration. Build backend.
- [x] Frontend: reusable modal/notification; basic, connection, selector and summary components; API types and real async flow. Build frontend and run validation tests.
- [x] Integration: real HTTP validation/failure paths and available SQL Server checks; browser desktop/mobile verification; independent review, fix material findings, document configuration and test evidence.

## Progress
- Source inspected; no reusable modal/toast/auth present. Existing jobs use inline SQL configuration; vault stores encrypted secrets in a separate file.
- Independent review completed: fixed sqlcmd substitution, stable job retry ID, capability preflight, new-inline validation bypass, and connection update/save race. Regression suite: 39 passing assertions.
- Ruling: no successful live SQL/backup claim without supplied test endpoint or sqlcmd; explicit manual verification steps recorded in docs/database-connections-implementation.md.
- Ruling: preserve encrypted previous-secret versions for in-flight jobs; no secret GC in this scope (cost: vault growth on updates).
- User-reported hydration issue traced to extension-added body attribute; narrow body boundary suppression added and type/build/browser checked.

