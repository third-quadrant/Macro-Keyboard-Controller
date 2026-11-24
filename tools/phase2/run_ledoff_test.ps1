$ErrorActionPreference = 'Continue'
$root = 'C:\Users\user\orca\workspaces\new macro keyboard controller\fireworm'
$apply = Join-Path $root 'tools\phase2\ApplyConfig\bin\Release\net8.0\ApplyConfig.exe'
$out = Join-Path $env:TEMP 'fireworm-ledoff-test.txt'

Start-Sleep -Seconds 4
"LED OFF TEST $(Get-Date -Format o)" | Set-Content $out
[Console]::Beep(880, 150)
Start-Sleep -Milliseconds 120
[Console]::Beep(880, 150)
Start-Sleep -Seconds 10
"OFF slot=1 no-commit $(Get-Date -Format o)" | Add-Content $out
& $apply ledoff 1 --confirm *>> $out
[Console]::Beep(660, 400)
Start-Sleep -Seconds 3
"OFF-COMMAND-SENT" | Add-Content $out
Get-Content $out
