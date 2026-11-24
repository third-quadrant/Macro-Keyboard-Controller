param(
    [string]$OutFile = "$PSScriptRoot\descriptor-output.txt",
    [string]$HidSharp = "$env:TEMP\..\..\AppData\Local\Temp\opencode\hidsharp\pkg\lib\netstandard2.0\HidSharp.dll"
)

# Resolve HidSharp path (allow env override).
if (-not (Test-Path $HidSharp)) {
    $candidates = @(
        "C:\Users\user\AppData\Local\Temp\opencode\hidsharp\pkg\lib\netstandard2.0\HidSharp.dll"
    )
    $HidSharp = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $HidSharp) { throw "HidSharp.dll not found" }
Add-Type -Path $HidSharp

function FmtUsage([uint32]$u) { '{0:X4}:{1:X4}' -f ($u -shr 16), ($u -band 0xFFFF) }

$lines = New-Object System.Collections.Generic.List[string]
$devs = [HidSharp.DeviceList]::Local.GetHidDevices([uint16]0x1189, [uint16]0x8890)
$lines.Add("HidSharp 2.6.4 report-descriptor dump (reconstructed by Windows HID API / HidSharp)")
$lines.Add("generated: $(Get-Date -Format o)")
$lines.Add("collections: $($devs.Count)")
foreach ($d in $devs) {
    $lines.Add("================================================================")
    $lines.Add("PATH      : $($d.DevicePath)")
    $lines.Add(("VID/PID   : {0:X4}/{1:X4}  MaxIn={2} MaxOut={3} MaxFeat={4}" -f $d.VendorID, $d.ProductID, $d.MaxInputReportLength, $d.MaxOutputReportLength, $d.MaxFeatureReportLength))
    $rd = $d.GetReportDescriptor()
    $lines.Add("UseReportIDs=$($rd.ReportsUseID)")
    foreach ($di in $rd.DeviceItems) {
        $u = ($di.Usages.GetAllValues() | ForEach-Object { FmtUsage $_ }) -join ','
        $lines.Add("  DeviceItem collection=$($di.CollectionType) usages=$u")
    }
    foreach ($r in $rd.Reports) {
        $lines.Add(("  REPORT type={0} RID={1} length={2} bytes" -f $r.ReportType, $r.ReportID, $r.Length))
        $bit = 0
        foreach ($item in $r.DataItems) {
            $us = ($item.Usages.GetAllValues() | ForEach-Object { FmtUsage $_ }) -join ','
            $lines.Add(("    off={0,4} bits={1,4} elemBits={2,2} count={3,2} flags={4} usage={5} min={6} max={7}" -f `
                $bit, $item.TotalBits, $item.ElementBits, $item.ElementCount, $item.Flags, $us, $item.LogicalMinimum, $item.LogicalMaximum))
            $bit += $item.TotalBits
        }
    }
}
$lines | Set-Content -Path $OutFile -Encoding UTF8
Get-Content $OutFile
