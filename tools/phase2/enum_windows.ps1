param([int]$AppPid)

Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class WinEnum {
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder text, int max);
    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);
}
"@

$results = New-Object System.Collections.Generic.List[string]
$callback = [WinEnum+EnumProc]{
    param($hWnd, $lParam)
    $procId = 0
    $null = [WinEnum]::GetWindowThreadProcessId($hWnd, [ref]$procId)
    if ($procId -eq $AppPid) {
        $t = New-Object System.Text.StringBuilder 512
        $c = New-Object System.Text.StringBuilder 256
        $null = [WinEnum]::GetWindowText($hWnd, $t, 512)
        $null = [WinEnum]::GetClassName($hWnd, $c, 256)
        $v = [WinEnum]::IsWindowVisible($hWnd)
        $results.Add(("HWND=" + $hWnd + " VISIBLE=" + $v + " CLASS=[" + $c.ToString() + "] TITLE=[" + $t.ToString() + "]"))
    }
    return $true
}
$null = [WinEnum]::EnumWindows($callback, [IntPtr]::Zero)
$results | ForEach-Object { Write-Output $_ }
