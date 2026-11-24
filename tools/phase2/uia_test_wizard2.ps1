Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win32Wizard {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
"@

$hwnd = [Win32Wizard]::FindWindow($null, '장치 트리거 설정')
if ($hwnd -eq [IntPtr]::Zero) { Write-Output 'WIZARD HWND NOT FOUND'; exit 2 }
Write-Output ('WIZARD HWND=' + $hwnd)

$wiz = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
Write-Output ('NAME=' + $wiz.Current.Name + ' ENABLED=' + $wiz.Current.IsEnabled + ' OFFSCREEN=' + $wiz.Current.IsOffscreen)

foreach ($name in @('KEY1만 시험 적용','나머지 5개 적용','전체 복구','닫기')) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $el = $wiz.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if ($null -eq $el) { Write-Output ("BUTTON MISSING: " + $name) }
    else { Write-Output ("BUTTON " + $name + " enabled=" + $el.Current.IsEnabled + " type=" + $el.Current.ControlType.ProgrammaticName) }
}

$null = [Win32Wizard]::PostMessage($hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
Write-Output 'WIZARD CLOSE POSTED'
