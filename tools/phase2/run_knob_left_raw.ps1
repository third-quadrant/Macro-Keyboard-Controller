$ErrorActionPreference = 'Continue'
$root = 'C:\Users\user\orca\workspaces\new macro keyboard controller\fireworm'
$capture = Join-Path $root 'tools\phase0\HidProbe.exe'
$dir = 'C:\Users\user\AppData\Local\Temp\opencode\phase0'
New-Item -ItemType Directory -Force $dir | Out-Null
$capLog = Join-Path $dir 'knob_left_cap.log'
$rawLog = Join-Path $dir 'knob_left_raw.log'
$out = Join-Path $env:TEMP 'fireworm-knob-left-raw.txt'

Get-Process Fireworm.App -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Remove-Item $capLog, $rawLog -ErrorAction SilentlyContinue
Start-Sleep -Seconds 10
[Console]::Beep(880, 160)
Start-Sleep -Milliseconds 150
[Console]::Beep(880, 160)
Start-Process -FilePath $capture -ArgumentList @('capture','20',$capLog) -WindowStyle Hidden
Start-Process -FilePath $capture -ArgumentList @('raw','20',$rawLog,'quiet') -WindowStyle Hidden
Start-Sleep -Seconds 23

"KNOB LEFT RAW $(Get-Date -Format o)" | Set-Content $out
"--- direct capture (MI_02 COL02) ---" | Add-Content $out
Get-Content $capLog -ErrorAction SilentlyContinue | Add-Content $out
"--- raw input ---" | Add-Content $out
Get-Content $rawLog -ErrorAction SilentlyContinue | Add-Content $out
Get-Content $out
