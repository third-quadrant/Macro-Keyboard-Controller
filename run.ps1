$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'dist\Fireworm\Fireworm.App.exe'
if (-not (Test-Path $exe)) {
    Write-Host 'dist\Fireworm\Fireworm.App.exe not found. Run .\build.ps1 first.'
    exit 1
}
Start-Process $exe
