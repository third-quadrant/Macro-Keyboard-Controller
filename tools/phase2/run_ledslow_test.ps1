$ErrorActionPreference = 'Continue'
$root = 'C:\Users\user\orca\workspaces\new macro keyboard controller\fireworm'
$apply = Join-Path $root 'tools\phase2\ApplyConfig\bin\Release\net8.0\ApplyConfig.exe'
$out = Join-Path $env:TEMP 'fireworm-ledslow-test.txt'

"LED SLOW TEST $(Get-Date -Format o)" | Set-Content $out
Start-Sleep -Seconds 5
foreach ($round in 1..3) {
    [Console]::Beep(1300, 200)
    Start-Sleep -Milliseconds 150
    [Console]::Beep(1300, 200)
    "ROUND $round ON no-commit $(Get-Date -Format o)" | Add-Content $out
    & $apply ledon 1 --confirm *>> $out
    Start-Sleep -Seconds 6
    [Console]::Beep(440, 700)
    "ROUND $round OFF no-commit $(Get-Date -Format o)" | Add-Content $out
    & $apply ledoff 1 --confirm *>> $out
    Start-Sleep -Seconds 6
}
# leave the slot in the committed "reacts to press" mode
& $apply ledkeys 1 --confirm *>> $out
"DONE $(Get-Date -Format o)" | Add-Content $out
Get-Content $out | Select-Object -Last 4
