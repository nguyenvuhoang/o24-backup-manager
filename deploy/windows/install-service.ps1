param(
    [string]$PublishPath = "C:\Program Files\BackupManager\backend",
    [string]$ServiceName = "BackupManagerAgent",
    [int]$Port = 5088
)
$ErrorActionPreference = "Stop"
$executable = Join-Path $PublishPath "BackupManager.API.exe"
if (-not (Test-Path $executable)) { throw "Self-contained publish output not found: $executable" }
$binary = "`"$executable`""
if (Get-Service $ServiceName -ErrorAction SilentlyContinue) {
    Stop-Service $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}
New-Service -Name $ServiceName -BinaryPathName $binary -DisplayName "Backup Manager Agent" -Description "Local database backup agent" -StartupType Automatic
[Environment]::SetEnvironmentVariable("ASPNETCORE_URLS", "http://127.0.0.1:$Port", "Machine")
Start-Service $ServiceName
Write-Host "Backup Manager installed: http://127.0.0.1:$Port"
