# Database connections và màn hình tạo Backup Job

Đã triển khai flow frontend → .NET → Microsoft.Data.SqlClient → metadata → lưu connection → lưu job với ConnectionId → resolve vào engine sqlcmd hiện có. Không có mock API hoặc danh sách database hard-code trong implementation.

## Backend

### Files tạo mới

- `backend/src/BackupManager.Domain/Models/DatabaseConnection.cs`: settings không chứa password và entity connection chứa secret reference, timestamps.
- `backend/src/BackupManager.Application/Connections/Contracts.cs`: DTO, provider/resolver, store/vault contracts, clean domain errors.
- `backend/src/BackupManager.Application/Connections/ConnectionValidation.cs`: host, auth, port, timeout và kích thước input.
- `backend/src/BackupManager.Application/Connections/DatabaseConnectionService.cs`: test/discover/CRUD, kiểm tra thật trước persistence, giữ password cũ khi update null/rỗng.
- `backend/src/BackupManager.Application/Connections/BackupJobService.cs`: validation, kiểm tra database ONLINE/access khi lưu, compatibility job cũ.
- `backend/src/BackupManager.Plugins.SqlServer/SqlServerConnectionSettings.cs`: SqlConnectionStringBuilder, resolve DataSource, bridge sqlcmd.
- `backend/src/BackupManager.Plugins.SqlServer/SqlServerDatabaseProvider.cs`: SqlClient test/discover, cancellation, dispose, metadata query cố định.
- `backend/src/BackupManager.API/Endpoints/DatabaseConnectionEndpoints.cs`: Minimal API CRUD/test/discover.
- `backend/src/BackupManager.API/Endpoints/ConfigurationErrorFilter.cs`: HTTP mapping không trả exception/stack trace.
- `backend/tests/BackupManager.ContractTests/BackupManager.ContractTests.csproj` và `Program.cs`: console contract suite, không cần framework test bổ sung.
- `scripts/test-connection-api.ps1`: kiểm tra HTTP validation và failure path với SqlClient thật.

### Files sửa

- `backend/BackupManager.slnx`: thêm test project.
- `backend/src/BackupManager.Domain/Models/BackupModels.cs`: nullable legacy SqlServer, ConnectionId và cấu hình bridge.
- `backend/src/BackupManager.Infrastructure/Services/JsonStore.cs`: connections.json, mutation atomic có khóa, ngăn xóa connection đang được dùng và kiểm tra revision khi lưu job.
- `backend/src/BackupManager.Infrastructure/Services/SecretVault.cs`: triển khai abstraction, serial hóa các mutation AES-GCM, ghi atomic.
- `backend/src/BackupManager.Plugins.SqlServer/BackupManager.Plugins.SqlServer.csproj`: Microsoft.Data.SqlClient 7.0.3.
- `backend/src/BackupManager.Plugins.SqlServer/SqlServerPlugin.cs`: cấu hình từ connection, preflight sqlcmd, tắt SQLCMD variable substitution bằng `-x` để tên database/path không mở rộng `$(SQLCMDPASSWORD)`.
- `backend/src/BackupManager.API/Program.cs`: DI/endpoints/clean malformed-JSON errors.
- `backend/src/BackupManager.API/Services/BackupPipeline.cs`: resolve ConnectionId trước backup; giữ BACKUP/VERIFY/ZIP/hash/SCP/retention/Telegram.
- `.gitignore`: loại runtime data API khỏi source control.

### Endpoints

| Method | Path | Behavior |
|---|---|---|
| POST | `/api/v1/database-connections/test` | Kết nối thật, không lưu |
| POST | `/api/v1/database-connections/discover` | Kết nối và lấy metadata thật, không lưu |
| GET | `/api/v1/database-connections` | Metadata các connection, không trả password/secret reference/ciphertext |
| GET | `/api/v1/database-connections/{id}` | Metadata một connection |
| POST | `/api/v1/database-connections` | Discover lại trước khi lưu, HTTP 201 |
| PUT | `/api/v1/database-connections/{id}` | Cập nhật; password null/rỗng giữ giá trị trước |
| DELETE | `/api/v1/database-connections/{id}` | HTTP 204; HTTP 409 nếu đang được job dùng |
| POST | `/api/v1/jobs` | Endpoint cũ được mở rộng để dùng ConnectionId |

Request connection là object phẳng: `provider`, `host`, `port`, `instanceName`, `authenticationType` (`windows` hoặc `sqlserver`), `username`, `password`, `database`, `encrypt`, `trustServerCertificate`, `connectionTimeout`, `commandTimeout`, `applicationName`, `name` (tùy chọn).

Connection response: `{ id, configuration, hasPassword, createdAtUtc, updatedAtUtc }`. `configuration` không chứa credential. Test response: `{ success, serverName, databaseEngine, version, message }`. Discover response: `{ success, server: { name, provider, version }, databases: [{ name, status, sizeMb, createdAt, isAccessible }] }`.

Errors: `{ success: false, message, errorCode, errors? }`; validation 400, not found 404, connection in use 409, connection/discovery failure 422. JSON sai không trả stack trace. Không thêm authentication/authorization trong task này.

Metadata lấy từ `sys.databases`, `sys.master_files`, `HAS_DBACCESS`. Loại database_id 1–4. Size không thấy được trả null; UI hiển thị “—”. `createdAt` là ngày tạo theo server, không phải ngày cập nhật. Visibility phụ thuộc quyền login.

## Frontend

### Files tạo mới

- `frontend/src/components/modal.tsx`: native dialog, focus trap/Escape và scroll lock.
- `frontend/src/components/notification.tsx`: notification success/error/info dùng chung.
- `frontend/src/features/jobs/form-elements.tsx`: field và numbered card.
- `frontend/src/features/jobs/basic-information-card.tsx`.
- `frontend/src/features/jobs/database-connection-card.tsx`.
- `frontend/src/features/jobs/database-connection-modal.tsx`.
- `frontend/src/features/jobs/database-selector.tsx`.
- `frontend/src/features/jobs/backup-job-summary.tsx` (kèm ProcessingPipeline).
- `frontend/src/lib/database-selection.ts`, `database-selection.test.mjs`, `job-validation.d.mts`.

### Files sửa

- `frontend/src/features/jobs/job-form.tsx`: orchestrate flow, reset danh sách/selection khi connect lại, Cancel edit giữ active connection, retry ID ổn định, xóa password khỏi state sau persist.
- `frontend/src/lib/api/client.ts`: reuse `/api/v1` client, typed errors, AbortSignal và test/discover/save connection.
- `frontend/src/types/api.ts`: contract khớp backend.
- `frontend/src/lib/job-validation.mjs`, `job-validation.test.mjs`: name/path/retention/connection/database.
- `frontend/src/app/page.tsx`: back action, save notification, responsive container.
- `frontend/src/components/sidebar.tsx`: navigation responsive.
- `frontend/src/features/dashboard/dashboard.tsx`: hỗ trợ job chỉ có ConnectionId.
- `frontend/src/app/globals.css`: inputs/buttons/dialog và fullscreen mobile.
- `frontend/src/app/layout.tsx`: `suppressHydrationWarning` chỉ ở body để dung nạp thuộc tính extension ColorZilla `cz-shortcut-listen`. Không tắt kiểm tra hydration ở descendants. Xem [React guidance](https://react.dev/reference/react-dom/client/hydrateRoot#suppressing-unavoidable-hydration-mismatch-errors).

Không lưu password vào localStorage/sessionStorage. Test kết nối không có nghĩa tạo session SQL giữ mở: mỗi request mở/dispose connection; status UI phản ánh lần discover gần nhất và save kiểm tra lại.

## Build và test

```powershell
dotnet build .\backend\BackupManager.slnx -c Release
dotnet run --project .\backend\tests\BackupManager.ContractTests -c Release
cd frontend
node --test src/lib/*.test.mjs
node node_modules/next/dist/bin/next build
```

Trong môi trường kiểm tra, npm launcher trỏ tới npm-cli.js không tồn tại nên đã dùng Node chạy trực tiếp cùng Next CLI và Node test runner. Cần Node 22.18+ hoặc Node 24 để chạy test import TypeScript; phiên kiểm tra dùng Node 24.19.0.

Đã kiểm tra:

- Backend Release build: 0 warning, 0 error.
- Frontend production build và TypeScript pass.
- 39 backend assertions: DataSource, validation, SqlConnectionStringBuilder escaping, vault encryption, update password, test/discover không lưu, save failure không lưu, legacy jobs, online/access recheck, concurrency, referenced-delete protection, retry ID, sqlcmd arguments.
- 6 frontend tests: validation, search, size units và selectable states.
- 8 HTTP checks với API thật ở cổng 5089: validation/not-found/malformed JSON, unreachable SQL test/discover/save, new-inline rejection và không persist khi fail. Lệnh: `./scripts/test-connection-api.ps1` (PowerShell 7).
- Browser production: create page, validation, summary, modal, loading/disable và lỗi kết nối SqlClient thật qua proxy.
- Browser desktop và mobile 390×844: layout/scroll/fullscreen dialog, authentication fields. Không thấy hydration mismatch trong browser kiểm tra; browser này không có ColorZilla để tái hiện chính xác extension của người dùng.

Provider thay thế chỉ có trong test project để kiểm tra use case/persistence thành công. Implementation API luôn đăng ký SqlServerDatabaseProvider thật.

Chưa xác minh successful end-to-end với SQL Server đang chạy hoặc backup thật: chưa có endpoint/credential test do người dùng cung cấp và không có sqlcmd trong PATH. Không chạy BACKUP/retention/SCP/Telegram vào hệ thống người dùng trong phiên này.

## Configuration và migration

1. Restart backend đang chạy tại 5088 để nạp assembly/API mới. Phiên kiểm tra dùng API riêng 5089, không thay process API cũ.
2. SQL Authentication persistence cần `BackupManager__MasterKey` ít nhất 32 ký tự. Giữ nguyên key khi nâng cấp; sao lưu key cùng cơ chế an toàn của deployment. Key thay đổi sẽ không giải mã được secrets cũ.
3. Backend service phải đọc/ghi được `data/`. `connections.json` tự tạo khi lưu lần đầu. AES-GCM ciphertext vẫn ở `secrets.json`; jobs.json chỉ chứa ConnectionId với job mới. Không cần SQL/EF migration.
4. Job cũ có inline SqlServer vẫn chạy; cập nhật không thay server/database vẫn được hỗ trợ. Khi tạo job mới hoặc đổi server/database của job cũ, dùng connection mới. Không tự migrate/xóa dữ liệu cũ.
5. Windows Authentication dùng backend service identity. Linux/Docker bị từ chối rõ ràng và cần SQL Server Authentication; không tự giả định Kerberos đã được cấu hình.
6. Có Port: DataSource `tcp:host,port`, không dùng InstanceName. Không Port: `host\instance` hoặc `host`. Named instance có thể cần SQL Browser/network rules.
7. Backup job mới yêu cầu **sqlcmd ODBC có `-N[s|m|o]` trong `sqlcmd -?`** để truyền đúng Encrypt=true/false. Engine preflight và trả lỗi rõ nếu thiếu/không tương thích. Legacy jobs giữ flag behavior cũ. Xem [Microsoft sqlcmd options](https://learn.microsoft.com/en-us/sql/tools/sqlcmd/sqlcmd-utility).
8. CommandTimeout mặc định 30 giây áp dụng metadata và sqlcmd của job mới; tăng phù hợp trong Advanced (tối đa 600 giây). ApplicationName áp dụng SqlClient test/discovery; sqlcmd dùng tên ứng dụng riêng của utility.
9. BackupDirectory phải cùng được backend và SQL Server truy cập; backend tạo thư mục khi chạy backup. Remote SQL Server cần shared/mounted storage phù hợp.
10. UI mới giữ SCP/Telegram tắt mặc định, như form cũ; pipeline vẫn thực thi hai bước với job có cấu hình enabled. Summary ghi rõ điều này.
11. Connection đã được job dùng không được đổi host/port/instance/provider tại chỗ; tạo connection mới để tránh redirect backup job. Credentials/TLS/timeouts vẫn cập nhật được. Secret phiên bản cũ được giữ để backup đang chạy không mất credential; chưa có secret garbage collection.

## Manual success flow

Mở Tạo Backup Job → điền tên/path/retention → cấu hình server thật → test → connect → thấy user databases → chọn ONLINE/access=true → lưu → kiểm tra job có ConnectionId, không có credential → chạy job khi đường dẫn, quyền BACKUP/VERIFY và sqlcmd đã sẵn sàng. Thử Cancel edit, đổi server thất bại, database không ONLINE, và retry sau save failure.
