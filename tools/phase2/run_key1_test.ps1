$ErrorActionPreference = 'Continue'
$root = 'C:\Users\user\orca\workspaces\new macro keyboard controller\fireworm'
$apply = Join-Path $root 'tools\phase2\ApplyConfig\bin\Release\net8.0\ApplyConfig.exe'
$hid = Join-Path $root 'tools\phase0\HidProbe.exe'
$log = Join-Path $env:TEMP 'fireworm-key1-verify.log'
$out = Join-Path $env:TEMP 'fireworm-key1-result.txt'

Start-Sleep -Seconds 20
"APPLY slot=1 usage=0x68 F13 $(Get-Date -Format o)" | Set-Content $out
& $apply writekey 1 0x68 --confirm *>> $out
if ($LASTEXITCODE -ne 0) {
    "APPLY_FAILED exit=$LASTEXITCODE" | Add-Content $out
    Get-Content $out
    exit 1
}

Start-Sleep -Seconds 2
Remove-Item $log -ErrorAction SilentlyContinue
"CAPTURE_START $(Get-Date -Format o)" | Add-Content $out
& $hid raw 25 $log *>> $out
Start-Sleep -Milliseconds 300

$text = if (Test-Path $log) { Get-Content $log -Raw } else { '' }
if ($text -match 'vk=7C') {
    "RESULT=SUCCESS KEY1 emits F13" | Add-Content $out
} else {
    "RESULT=FAIL no F13; restoring slot=1 to A" | Add-Content $out
    & $apply restorekey 1 --confirm *>> $out
    "RESTORED slot=1 usage=0x04 A" | Add-Content $out
}
Get-Content $out
