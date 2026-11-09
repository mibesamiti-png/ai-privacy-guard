$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
Write-Host 'AI Privacy Guard - Windows build & test'
dotnet restore .\AIPrivacyGuard.slnx
if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }
dotnet test .\tests\PrivacyGuard.Core.Tests\PrivacyGuard.Core.Tests.csproj -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
dotnet publish .\src\PrivacyGuard.App\PrivacyGuard.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o .\dist\win-x64
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
Write-Host 'Done: dist\win-x64\AIPrivacyGuard.exe'
