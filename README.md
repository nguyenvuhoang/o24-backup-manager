# Backup Manager — Next.js 16 + .NET 10

Backup Manager gồm hai ứng dụng độc lập:

- `frontend/`: Next.js 16, React 19, TypeScript, Tailwind CSS.
- `backend/`: ASP.NET Core Web API .NET 10, SignalR và backup engine.

Backend là thành phần duy nhất được phép kết nối SQL Server, chạy `sqlcmd`/`scp`, đọc ghi file và sử dụng credentials.

## Chạy development

### Backend

Yêu cầu .NET SDK 10, SQL Server command-line tools và OpenSSH Client.

```powershell
$env:BackupManager__MasterKey="replace-with-at-least-32-characters"
dotnet restore .\backend\BackupManager.slnx
dotnet run --project .\backend\src\BackupManager.API\BackupManager.API.csproj
```

Backend chạy tại `http://127.0.0.1:5088`; API dùng `/api/v1`; SignalR dùng `/hubs/runs`.

### Frontend

```powershell
cd .\frontend
npm install
npm run dev
```

Mở `http://localhost:3000`. Next.js proxy `/api/*` và `/hubs/*` tới backend.

## Docker Compose

```bash
cd deploy
cp .env.example .env
docker compose up -d --build
```

Chỉ frontend được publish ra cổng `3000`; backend nằm trong Docker network.

> Đường dẫn trong câu lệnh `BACKUP DATABASE` phải là đường dẫn mà dịch vụ SQL Server nhìn thấy. Nếu SQL Server chạy ngoài container, cần share/mount đúng đường dẫn hoặc chạy backend native trên máy Windows SQL Server.

## Windows production

```powershell
.\scripts\publish-windows.ps1
```

Backend chạy Windows Service bằng `deploy/windows/install-service.ps1`. Frontend standalone chạy bằng Node:

```powershell
cd C:\Program Files\BackupManager\frontend
$env:BACKEND_URL="http://127.0.0.1:5088"
node server.js
```

Có thể dùng NSSM, IIS reverse proxy hoặc Task Scheduler để giữ frontend hoạt động.

## Pipeline hiện tại

`SQL Server BACKUP -> RESTORE VERIFYONLY -> ZIP -> SHA-256 -> SCP -> retention -> Telegram`

Secret được gửi vào `POST /api/v1/secrets`; frontend không đọc lại giá trị. Telegram token từng xuất hiện trong script cũ phải được revoke trước khi sử dụng.

## Kiểm tra

```powershell
cd frontend
npm test
npm run build

cd ..
dotnet build .\backend\BackupManager.slnx -c Release
```

Flow tạo job mới hỗ trợ test/discover SQL Server qua Microsoft.Data.SqlClient, lưu connection riêng và resolve vào engine sqlcmd. Xem [implementation, API, cấu hình và test](docs/database-connections-implementation.md).

Job mới cần sqlcmd ODBC hỗ trợ `-N[s|m|o]` (kiểm tra bằng `sqlcmd -?`) để đồng bộ lựa chọn Encrypt. Job cũ giữ cách gọi sqlcmd trước đây. Không cần migration SQL; `data/connections.json` tự tạo khi lưu kết nối đầu tiên. Restart backend sau khi build để nạp API mới.
