$ErrorActionPreference = 'Continue'
$root = 'C:\Users\user\orca\workspaces\new macro keyboard controller\fireworm'
$log = Join-Path $env:LOCALAPPDATA 'Fireworm\fireworm.log'
$out = Join-Path $env:TEMP 'fireworm-knob-left-result.txt'

$app = Get-Process Fireworm.App -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $app) {
    $app = Start-Process (Join-Path $root 'dist\Fireworm\Fireworm.App.exe') -PassThru
}
Remove-Item $log -ErrorAction SilentlyContinue
Start-Sleep -Seconds 10
[Console]::Beep(880, 160)
Start-Sleep -Milliseconds 150
[Console]::Beep(880, 160)
Start-Sleep -Seconds 20

$content = if (Test-Path $log) { Get-Content $log -Raw } else { '' }
"KNOB LEFT TEST $(Get-Date -Format o)" | Set-Content $out
if ($content -match '노브 왼쪽 →') {
    "RESULT=SUCCESS knob left action" | Add-Content $out
} else {
    "RESULT=FAIL no knob left action" | Add-Content $out
}
Get-Content $log -ErrorAction SilentlyContinue | Select-Object -Last 20 | Add-Content $out
Get-Content $out
