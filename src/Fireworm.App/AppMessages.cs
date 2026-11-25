using System.Runtime.InteropServices;

namespace Fireworm.App;

/// <summary>Broadcast message a second instance uses to bring the running one forward.</summary>
public static class AppMessages
{
    public static readonly int ShowWindow = RegisterWindowMessage("Fireworm.ShowWindow");

    public const int HwndBroadcast = 0xFFFF;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);
}
