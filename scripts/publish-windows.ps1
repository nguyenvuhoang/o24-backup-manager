$ErrorActionPreference = "Stop"
$output = Join-Path $PSScriptRoot "..\artifacts\windows-x64\backend"
dotnet publish "$PSScriptRoot\..\backend\src\BackupManager.API\BackupManager.API.csproj" -c Release -r win-x64 --self-contained true -o $output
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
Copy-Item "$PSScriptRoot\..\deploy\windows\*.ps1" (Split-Path $output) -Force
Push-Location "$PSScriptRoot\..\frontend"
npm ci
npm run build
Pop-Location
Copy-Item "$PSScriptRoot\..\frontend\.next\standalone" "$PSScriptRoot\..\artifacts\windows-x64\frontend" -Recurse -Force
Copy-Item "$PSScriptRoot\..\frontend\.next\static" "$PSScriptRoot\..\artifacts\windows-x64\frontend\.next\static" -Recurse -Force
Compress-Archive -Path "$output\*" -DestinationPath "$PSScriptRoot\..\artifacts\BackupManager-windows-x64.zip" -Force
Write-Host "Created artifacts\BackupManager-windows-x64.zip"
