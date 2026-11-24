Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class WinWizard {
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
"@

$found = [IntPtr]::Zero
$cb = [WinWizard+EnumProc]{
    param($hWnd, $lParam)
    $t = New-Object System.Text.StringBuilder 512
    $null = [WinWizard]::GetWindowText($hWnd, $t, 512)
    if ($t.ToString() -eq '장치 트리거 설정') { $script:found = $hWnd; return $false }
    return $true
}
$null = [WinWizard]::EnumWindows($cb, [IntPtr]::Zero)
if ($found -eq [IntPtr]::Zero) { Write-Output 'WIZARD NOT FOUND'; exit 2 }
Write-Output ('WIZARD HWND=' + $found)

$wiz = [System.Windows.Automation.AutomationElement]::FromHandle($found)
Write-Output ('NAME=' + $wiz.Current.Name + ' VISIBLE=' + (-not $wiz.Current.IsOffscreen))
foreach ($name in @('KEY1만 시험 적용','나머지 5개 적용','전체 복구','기본 백업 저장 (A/C)','현재 동작 녹음','닫기')) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $el = $wiz.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if ($null -eq $el) { Write-Output ('MISSING: ' + $name) }
    else { Write-Output ('FOUND: ' + $name + ' enabled=' + $el.Current.IsEnabled) }
}
$null = [WinWizard]::PostMessage($found, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
Write-Output 'CLOSE POSTED'
