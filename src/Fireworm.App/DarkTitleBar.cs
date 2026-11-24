using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Fireworm.App;

/// <summary>Windows 10/11 only paints the title bar dark when asked explicitly.</summary>
public static class DarkTitleBar
{
    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        var enabled = 1;
        if (DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, 19, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
