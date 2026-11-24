$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$localSdk = 'C:\Users\user\.dotnet8\dotnet.exe'
$dotnet = if (Test-Path $localSdk) { $localSdk } else { 'dotnet' }

Write-Host "Using: $dotnet"
& $dotnet build (Join-Path $root 'Fireworm.sln') -c Release
& $dotnet publish (Join-Path $root 'src\Fireworm.App\Fireworm.App.csproj') -c Release -r win-x64 --self-contained false -o (Join-Path $root 'dist\Fireworm')
Write-Host ''
Write-Host 'Framework-dependent build: dist\Fireworm\Fireworm.App.exe'
Write-Host 'Requires .NET 8 Desktop Runtime.'
