using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Fireworm.Core.Actions;
using Fireworm.Core.Models;

namespace Fireworm.App;

public partial class App : Application
{
    private Mutex? _instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var args = e.Args;
        if (args.Any(a => a.Equals("--smoke", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var store = new Core.Settings.SettingsStore();
                var settings = store.Load();
                var device = new Core.Device.DeviceService();
                device.Refresh();
                File.WriteAllText(Path.Combine(store.BaseDirectory, "smoke-ok.txt"),
                    $"OK {DateTime.Now:O}\nsettings={store.SettingsPath}\ndevice={device.StatusText}\nbindings={settings.Bindings.Count}\n");
                Shutdown(0);
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "fireworm-smoke-error.txt"), ex.ToString());
                Shutdown(1);
            }
            return;
        }

        var keyboardArg = FindArg(args, "--test-keyboard");
        if (keyboardArg != null)
        {
            RunTestAction(new ActionDefinition { Type = "keyboard", Keys = new List<string> { keyboardArg } },
                "fireworm-test-keyboard.txt");
            return;
        }

        var mediaArg = FindArg(args, "--test-media");
        if (mediaArg != null)
        {
            RunTestAction(new ActionDefinition { Type = "media", Media = mediaArg },
                "fireworm-test-media.txt");
            return;
        }

        // One instance only: two would both receive Raw Input and run every action twice.
        _instanceMutex = new Mutex(true, @"Local\Fireworm.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            AppMessages.PostMessage((IntPtr)AppMessages.HwndBroadcast, AppMessages.ShowWindow, IntPtr.Zero, IntPtr.Zero);
            Shutdown(0);
            return;
        }

        var window = new MainWindow(args.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase)));
        MainWindow = window;
        window.Show();
    }

    private static string? FindArg(string[] args, string name)
    {
        var index = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private void RunTestAction(ActionDefinition action, string resultFile)
    {
        var engine = new ActionEngine();
        Thread.Sleep(3000);
        var fg = GetForegroundWindow();
        var title = new StringBuilder(512);
        GetWindowTextW(fg, title, title.Capacity);
        uint pid;
        GetWindowThreadProcessId(fg, out pid);
        var procName = "?";
        try { procName = Process.GetProcessById((int)pid).ProcessName; } catch { }
        var result = engine.Execute(action);
        var path = Path.Combine(Path.GetTempPath(), resultFile);
        var detail = $"fg={title} fgPid={pid} fgProc={procName} inputSize={InputSender.InputSize}";
        File.WriteAllText(path, (result.Success ? "OK " + result.Message : "FAIL " + result.Message) + Environment.NewLine + detail);
        Shutdown(result.Success ? 0 : 1);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}

