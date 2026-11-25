Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class TrayTest {
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
}
"@

$root = 'C:\Users\user\orca\workspaces\new macro keyboard controller\fireworm'
$exe = Join-Path $root 'dist\Fireworm\Fireworm.App.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

Get-Process Fireworm.App -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1
$first = Start-Process $exe -PassThru
Start-Sleep -Seconds 4
$first.Refresh()
$handle = $first.MainWindowHandle
Write-Output ("FIRST_PID=" + $first.Id + " VISIBLE=" + [TrayTest]::IsWindowVisible($handle))

# Close the window: it must hide, not exit.
[TrayTest]::PostMessage($handle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
Start-Sleep -Seconds 2
$alive = Get-Process -Id $first.Id -ErrorAction SilentlyContinue
Write-Output ("AFTER_CLOSE_ALIVE=" + [bool]$alive + " VISIBLE=" + [TrayTest]::IsWindowVisible($handle))

# Launching again must not create a second instance, and must bring the window back.
$second = Start-Process $exe -PassThru
Start-Sleep -Seconds 3
$count = (Get-Process Fireworm.App -ErrorAction SilentlyContinue | Measure-Object).Count
Write-Output ("PROCESS_COUNT_AFTER_RELAUNCH=" + $count + " VISIBLE=" + [TrayTest]::IsWindowVisible($handle))

# Auto-start toggle -> registry.
$AE = [System.Windows.Automation.AutomationElement]
$win = $AE::FromHandle($handle)
$cond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, 'Windows 시작 시 자동 실행')
$check = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
if ($check) {
    $check.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Start-Sleep -Milliseconds 700
    $value = (Get-ItemProperty $runKey -Name Fireworm -ErrorAction SilentlyContinue).Fireworm
    Write-Output ("AUTOSTART_VALUE=" + $value)
    $check.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Start-Sleep -Milliseconds 700
    $after = (Get-ItemProperty $runKey -Name Fireworm -ErrorAction SilentlyContinue).Fireworm
    Write-Output ("AUTOSTART_AFTER_UNCHECK=" + [string]::IsNullOrEmpty($after))
} else {
    Write-Output 'AUTOSTART_CHECKBOX_NOT_FOUND'
}
