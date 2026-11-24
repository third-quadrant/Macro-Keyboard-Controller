$ErrorActionPreference = 'Continue'
$root = 'C:\Users\user\orca\workspaces\new macro keyboard controller\fireworm'
$apply = Join-Path $root 'tools\phase2\ApplyConfig\bin\Release\net8.0\ApplyConfig.exe'
$out = Join-Path $env:TEMP 'fireworm-led-modes-result.txt'

Start-Sleep -Seconds 5
"LED MODE EXPERIMENT $(Get-Date -Format o)" | Set-Content $out
foreach ($mode in 0, 1, 2) {
    "APPLY mode=$mode $(Get-Date -Format o)" | Add-Content $out
    & $apply led $mode --confirm *>> $out
    Start-Sleep -Seconds 4
}
"LEAVE mode=1 (on) $(Get-Date -Format o)" | Add-Content $out
& $apply led 1 --confirm *>> $out
Get-Content $out
