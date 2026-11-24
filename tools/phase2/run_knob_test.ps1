$ErrorActionPreference = 'Continue'
$root = 'C:\Users\user\orca\workspaces\new macro keyboard controller\fireworm'
$log = Join-Path $env:LOCALAPPDATA 'Fireworm\fireworm.log'
$out = Join-Path $env:TEMP 'fireworm-knob-result.txt'

$app = Get-Process Fireworm.App -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $app) {
    $app = Start-Process (Join-Path $root 'dist\Fireworm\Fireworm.App.exe') -PassThru
}
Remove-Item $log -ErrorAction SilentlyContinue
Start-Sleep -Seconds 15
[Console]::Beep(880, 160)
Start-Sleep -Milliseconds 150
[Console]::Beep(880, 160)
Start-Sleep -Seconds 25

$content = if (Test-Path $log) { Get-Content $log -Raw } else { '' }
"KNOB TEST $(Get-Date -Format o)" | Set-Content $out
$checks = @{
    '노브 왼쪽' = $content -match '노브 왼쪽 →'
    '노브 오른쪽' = $content -match '노브 오른쪽 →'
    '노브 클릭' = $content -match '노브 클릭 →'
}
foreach ($k in $checks.Keys) {
    if ($checks[$k]) { "RESULT $k=SUCCESS" | Add-Content $out } else { "RESULT $k=MISSING" | Add-Content $out }
}
Get-Content $log -ErrorAction SilentlyContinue | Select-Object -Last 20 | Add-Content $out
Get-Content $out
