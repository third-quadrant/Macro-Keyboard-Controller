$ErrorActionPreference = 'Continue'
$root = 'C:\Users\user\orca\workspaces\new macro keyboard controller\fireworm'
$apply = Join-Path $root 'tools\phase2\ApplyConfig\bin\Release\net8.0\ApplyConfig.exe'
$hid = Join-Path $root 'tools\phase0\HidProbe.exe'
$log = Join-Path $env:TEMP 'fireworm-slotled-verify.log'
$out = Join-Path $env:TEMP 'fireworm-slotled-result.txt'

Start-Sleep -Seconds 5
"SLOT LED TEST $(Get-Date -Format o)" | Set-Content $out
& $apply ledkeys 1 --confirm *>> $out
Start-Sleep -Seconds 2
[Console]::Beep(880, 160)
Start-Sleep -Milliseconds 150
[Console]::Beep(880, 160)
Remove-Item $log -ErrorAction SilentlyContinue
& $hid raw 15 $log *>> $out
Start-Sleep -Milliseconds 300

$text = if (Test-Path $log) { Get-Content $log -Raw } else { '' }
if ($text -match 'vk=7C') {
    "KEY1_STILL=F13" | Add-Content $out
} else {
    "KEY1_STILL=NO; restoring F13..F18" | Add-Content $out
    for ($slot = 1; $slot -le 6; $slot++) {
        $usage = 0x68 + ($slot - 1)
        & $apply writekey $slot ('0x{0:X2}' -f $usage) --confirm *>> $out
    }
    "RESTORED_KEYS=F13..F18" | Add-Content $out
}
Get-Content $out
