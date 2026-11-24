$ErrorActionPreference = 'Continue'
$root = 'C:\Users\user\orca\workspaces\new macro keyboard controller\fireworm'
$apply = Join-Path $root 'tools\phase2\ApplyConfig\bin\Release\net8.0\ApplyConfig.exe'
$out = Join-Path $env:TEMP 'fireworm-ledvolatile-test.txt'

"LED VOLATILE TEST $(Get-Date -Format o)" | Set-Content $out
Start-Sleep -Seconds 4
foreach ($step in 'on', 'off', 'on', 'off') {
    if ($step -eq 'on') {
        [Console]::Beep(1200, 120)
        Start-Sleep -Milliseconds 100
        [Console]::Beep(1200, 120)
        "ON  no-commit $(Get-Date -Format o)" | Add-Content $out
        & $apply ledon 1 --confirm *>> $out
    }
    else {
        [Console]::Beep(500, 400)
        "OFF no-commit $(Get-Date -Format o)" | Add-Content $out
        & $apply ledoff 1 --confirm *>> $out
    }
    Start-Sleep -Seconds 3
}
Get-Content $out
