$ErrorActionPreference = 'Continue'
$root = 'C:\Users\user\orca\workspaces\new macro keyboard controller\fireworm'
$log = Join-Path $env:LOCALAPPDATA 'Fireworm\fireworm.log'
$out = Join-Path $env:TEMP 'fireworm-physical-key2-result.txt'

Get-Process Fireworm.App -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Remove-Item $log -ErrorAction SilentlyContinue
$app = Start-Process (Join-Path $root 'dist\Fireworm\Fireworm.App.exe') -PassThru
Start-Sleep -Seconds 15
[Console]::Beep(880, 160)
Start-Sleep -Milliseconds 150
[Console]::Beep(880, 160)
Start-Sleep -Seconds 25

$content = if (Test-Path $log) { Get-Content $log -Raw } else { '' }
"PHYSICAL KEY2 TEST $(Get-Date -Format o)" | Set-Content $out
if ($content -match 'KEY 2 → 실행') {
    "RESULT=SUCCESS physical KEY2 triggered launch action" | Add-Content $out
} else {
    "RESULT=FAIL no KEY 2 action in log" | Add-Content $out
}
Get-Content $log -ErrorAction SilentlyContinue | Select-Object -Last 20 | Add-Content $out
Get-Content $out
