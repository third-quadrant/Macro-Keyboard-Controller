using System.Runtime.InteropServices;
using System.Text;
using Fireworm.Core.Models;

namespace Fireworm.Core.Input;

/// <summary>
/// Raw Input (WM_INPUT) source. It observes the target device only and never
/// suppresses the original input. The original F13-F18 keys still reach the
/// foreground application; see the report for the implications.
/// </summary>
public sealed class RawInputService : IDisposable
{
    private const int WM_INPUT = 0x00FF;
    private const uint RID_INPUT = 0x10000003;
    private const uint RIDI_DEVICENAME = 0x20000007;
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const ushort UsagePageGeneric = 0x01;
    private const ushort UsageKeyboard = 0x06;
    private const ushort UsageMouse = 0x02;
    private const ushort UsagePageConsumer = 0x0C;
    private const ushort UsageConsumerControl = 0x01;
    private const int RI_KEY_BREAK = 0x01;

    private readonly Dictionary<IntPtr, string> _deviceNames = new();
    private readonly HashSet<int> _pressedKeys = new();
    private readonly object _sync = new();
    private int _lastConsumerUsage;
    private DateTime _lastConsumerAt;
    private IntPtr _hwnd;
    private bool _registered;

    public ushort Vid { get; }
    public ushort Pid { get; }
    public string? LastError { get; private set; }
    public event Action<TriggerEvent>? TriggerReceived;
    public event Action<RawObservation>? ObservationReceived;

    public RawInputService(ushort vid = 0x1189, ushort pid = 0x8890)
    {
        Vid = vid;
        Pid = pid;
    }

    public bool Register(IntPtr hwnd)
    {
        _hwnd = hwnd;
        var devices = new[]
        {
            new RAWINPUTDEVICE { usUsagePage = UsagePageGeneric, usUsage = UsageKeyboard, dwFlags = RIDEV_INPUTSINK, hwndTarget = hwnd },
            new RAWINPUTDEVICE { usUsagePage = UsagePageConsumer, usUsage = UsageConsumerControl, dwFlags = RIDEV_INPUTSINK, hwndTarget = hwnd },
            new RAWINPUTDEVICE { usUsagePage = UsagePageGeneric, usUsage = UsageMouse, dwFlags = RIDEV_INPUTSINK, hwndTarget = hwnd },
        };

        _registered = NativeRegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
        if (!_registered)
            LastError = $"RegisterRawInputDevices 실패 (Win32 {Marshal.GetLastWin32Error()})";
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered) return;
        var devices = new[]
        {
            new RAWINPUTDEVICE { usUsagePage = UsagePageGeneric, usUsage = UsageKeyboard, dwFlags = 0x00000001, hwndTarget = _hwnd },
            new RAWINPUTDEVICE { usUsagePage = UsagePageConsumer, usUsage = UsageConsumerControl, dwFlags = 0x00000001, hwndTarget = _hwnd },
            new RAWINPUTDEVICE { usUsagePage = UsagePageGeneric, usUsage = UsageMouse, dwFlags = 0x00000001, hwndTarget = _hwnd },
        };
        NativeRegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
        _registered = false;
    }

    public void ProcessMessage(IntPtr lParam)
    {
        uint size = 0;
        var headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        NativeGetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, headerSize);
        if (size == 0) return;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            var got = NativeGetRawInputData(lParam, RID_INPUT, buffer, ref size, headerSize);
            if (got != size) return;

            var header = Marshal.PtrToStructure<RAWINPUTHEADER>(buffer);
            var devicePath = GetDeviceName(header.hDevice);
            if (!IsTargetDevice(devicePath)) return;

            var dataPtr = IntPtr.Add(buffer, Marshal.SizeOf<RAWINPUTHEADER>());
            switch (header.dwType)
            {
                case 1:
                    ProcessKeyboard(devicePath, dataPtr);
                    break;
                case 2:
                    ProcessHid(devicePath, dataPtr);
                    break;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void ProcessKeyboard(string devicePath, IntPtr dataPtr)
    {
        var kb = Marshal.PtrToStructure<RAWKEYBOARD>(dataPtr);
        if (kb.VKey == 0 || kb.VKey == 0xFF) return;

        var isBreak = (kb.Flags & RI_KEY_BREAK) != 0;
        var isDown = !isBreak;

        if (isDown)
        {
            lock (_sync)
            {
                if (!_pressedKeys.Add(kb.VKey))
                    return; // auto-repeat: already held
            }

            ObservationReceived?.Invoke(new RawObservation(
                devicePath, RawObservationKind.Keyboard, kb.VKey, 0,
                KeyName(kb.VKey), DateTime.Now));

            var trigger = TriggerCatalog.FromVk(kb.VKey);
            if (trigger is not null)
                TriggerReceived?.Invoke(new TriggerEvent(trigger.Value, devicePath,
                    $"VKey=0x{kb.VKey:X2} ({KeyName(kb.VKey)})", DateTime.Now));
        }
        else
        {
            lock (_sync) _pressedKeys.Remove(kb.VKey);
        }
    }

    private void ProcessHid(string devicePath, IntPtr dataPtr)
    {
        var sizeHid = (uint)Marshal.ReadInt32(dataPtr, 0);
        var count = (uint)Marshal.ReadInt32(dataPtr, 4);
        if (sizeHid == 0 || count == 0) return;

        var dataStart = IntPtr.Add(dataPtr, 8);
        for (var i = 0; i < count; i++)
        {
            var reportPtr = IntPtr.Add(dataStart, (int)(i * sizeHid));
            var report = new byte[sizeHid];
            Marshal.Copy(reportPtr, report, 0, report.Length);
            if (report.Length < 3) continue;

            var reportId = report[0];
            var usage = report[1] | (report[2] << 8);
            if (reportId != 2) continue; // consumer control collection uses report ID 2

            if (usage == 0)
            {
                lock (_sync)
                {
                    _lastConsumerUsage = 0;
                    _lastConsumerAt = default;
                }
                continue;
            }

            var now = DateTime.UtcNow;
            lock (_sync)
            {
                // The release report normally clears the state. If a release is missed,
                // allow the same usage again after a short gap instead of suppressing it forever.
                if (usage == _lastConsumerUsage &&
                    (now - _lastConsumerAt).TotalMilliseconds < 120)
                    continue;
                _lastConsumerUsage = usage;
                _lastConsumerAt = now;
            }

            ObservationReceived?.Invoke(new RawObservation(
                devicePath, RawObservationKind.Consumer, 0, usage,
                $"Consumer 0x{usage:X4}", DateTime.Now));

            var trigger = TriggerCatalog.FromConsumerUsage(usage);
            if (trigger is not null)
                TriggerReceived?.Invoke(new TriggerEvent(trigger.Value, devicePath,
                    $"Consumer 0x{usage:X4}", DateTime.Now));
        }
    }

    private bool IsTargetDevice(string devicePath)
    {
        var p = devicePath.ToLowerInvariant();
        return p.Contains($"vid_{Vid:x4}") && p.Contains($"pid_{Pid:x4}");
    }

    private string GetDeviceName(IntPtr hDevice)
    {
        lock (_sync)
        {
            if (_deviceNames.TryGetValue(hDevice, out var cached)) return cached;
        }

        uint size = 0;
        NativeGetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, null, ref size);
        var name = "?";
        if (size > 0)
        {
            var sb = new StringBuilder((int)size + 2);
            if (NativeGetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, sb, ref size) > 0)
                name = sb.ToString();
        }

        lock (_sync) _deviceNames[hDevice] = name;
        return name;
    }

    public static string KeyName(int vk) => vk switch
    {
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        >= 0x70 and <= 0x87 => "F" + (vk - 0x6F),
        0x0D => "Enter",
        0x1B => "Esc",
        0x09 => "Tab",
        0x20 => "Space",
        0x08 => "Backspace",
        0x2E => "Delete",
        _ => $"VK 0x{vk:X2}",
    };

    public void Dispose() => Unregister();

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWKEYBOARD
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(IntPtr hRawInput, uint command, IntPtr data, ref uint size, uint headerSize);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetRawInputDeviceInfo(IntPtr hDevice, uint command, StringBuilder? data, ref uint size);

    private static bool NativeRegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size)
        => RegisterRawInputDevices(devices, count, size);

    private static uint NativeGetRawInputData(IntPtr hRawInput, uint command, IntPtr data, ref uint size, uint headerSize)
        => GetRawInputData(hRawInput, command, data, ref size, headerSize);

    private static uint NativeGetRawInputDeviceInfo(IntPtr hDevice, uint command, StringBuilder? data, ref uint size)
        => GetRawInputDeviceInfo(hDevice, command, data, ref size);
}
