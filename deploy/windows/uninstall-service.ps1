param([string]$ServiceName = "BackupManagerAgent")
$service = Get-Service $ServiceName -ErrorAction SilentlyContinue
if ($service) {
    Stop-Service $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName
}
