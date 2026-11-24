$ErrorActionPreference = 'Continue'
$root = 'C:\Users\user\orca\workspaces\new macro keyboard controller\fireworm'
$apply = Join-Path $root 'tools\phase2\ApplyConfig\bin\Release\net8.0\ApplyConfig.exe'
$hid = Join-Path $root 'tools\phase0\HidProbe.exe'
$log = Join-Path $env:TEMP 'fireworm-keys2to6-verify.log'
$out = Join-Path $env:TEMP 'fireworm-keys2to6-result.txt'

"APPLY slots 2..6 F14..F18 $(Get-Date -Format o)" | Set-Content $out
for ($slot = 2; $slot -le 6; $slot++) {
    $usage = 0x68 + ($slot - 1)
    $usageText = '0x{0:X2}' -f $usage
    "APPLY slot=$slot usage=$usageText" | Add-Content $out
    & $apply writekey $slot $usageText --confirm *>> $out
    if ($LASTEXITCODE -ne 0) {
        "APPLY_FAILED slot=$slot exit=$LASTEXITCODE" | Add-Content $out
        "RESTORING ALL" | Add-Content $out
        & $apply restore --confirm *>> $out
        Get-Content $out
        exit 1
    }
    Start-Sleep -Milliseconds 800
}

Start-Sleep -Seconds 15
Remove-Item $log -ErrorAction SilentlyContinue
"CAPTURE_START $(Get-Date -Format o)" | Add-Content $out
& $hid raw 45 $log *>> $out
Start-Sleep -Milliseconds 300

$text = if (Test-Path $log) { Get-Content $log -Raw } else { '' }
$expected = @{ 2 = '7D'; 3 = '7E'; 4 = '7F'; 5 = '80'; 6 = '81' }
$missing = @()
foreach ($slot in 2..6) {
    if ($text -match ('vk=' + $expected[$slot])) {
        "KEY$slot = F$($slot + 11) detected" | Add-Content $out
    } else {
        "KEY$slot = F$($slot + 11) MISSING" | Add-Content $out
        $missing += $slot
    }
}

if ($missing.Count -eq 0) {
    "RESULT=SUCCESS KEY1..KEY6 = F13..F18" | Add-Content $out
} else {
    "RESULT=FAIL missing slots: $($missing -join ',')" | Add-Content $out
    "RESTORING ALL to backup A/C" | Add-Content $out
    & $apply restore --confirm *>> $out
    "RESTORED ALL" | Add-Content $out
}
Get-Content $out
