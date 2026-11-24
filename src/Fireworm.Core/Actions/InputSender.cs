using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Fireworm.Core.Actions;

public static class InputSender
{
    public static int InputSize => Marshal.SizeOf<INPUT>();
    private const uint InputKeyboard = 1;
    private const uint InputMouse = 0;
    private const uint KeyeventfKeyup = 0x0002;
    private const uint KeyeventfExtendedkey = 0x0001;
    private const uint KeyeventfUnicode = 0x0004;
    private const uint MouseeventfLeftdown = 0x0002;
    private const uint MouseeventfLeftup = 0x0004;
    private const uint MouseeventfRightdown = 0x0008;
    private const uint MouseeventfRightup = 0x0010;
    private const uint MouseeventfMiddledown = 0x0020;
    private const uint MouseeventfMiddleup = 0x0040;
    private const uint MouseeventfWheel = 0x0800;
    private static readonly object Sync = new();

    public static void SendChord(KeyChord chord)
    {
        lock (Sync)
        {
            var inputs = new List<INPUT>();
            foreach (var modifier in chord.Modifiers)
                inputs.Add(KeyInput(modifier, false));
            inputs.Add(KeyInput(chord.Key, false));
            inputs.Add(KeyInput(chord.Key, true));
            for (var i = chord.Modifiers.Count - 1; i >= 0; i--)
                inputs.Add(KeyInput(chord.Modifiers[i], true));

            Send(inputs.ToArray());
            Thread.Sleep(10);
        }
    }

    public static void SendText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        lock (Sync)
        {
            foreach (var c in text)
            {
                var inputs = new[]
                {
                    UnicodeInput(c, false),
                    UnicodeInput(c, true),
                };
                Send(inputs);
                Thread.Sleep(2);
            }
        }
    }

    public static void SendMedia(string media)
    {
        var vk = media switch
        {
            "volume_up" => 0xAF,
            "volume_down" => 0xAE,
            "mute" => 0xAD,
            "play_pause" => 0xB3,
            "next" => 0xB0,
            "prev" => 0xB1,
            _ => throw new ArgumentException($"알 수 없는 미디어 동작: {media}"),
        };

        lock (Sync)
        {
            Send(new[]
            {
                KeyInput((ushort)vk, false, KeyeventfExtendedkey),
                KeyInput((ushort)vk, true, KeyeventfExtendedkey),
            });
            Thread.Sleep(20);
        }
    }

    public static void SendMouse(string mouse)
    {
        lock (Sync)
        {
            switch (mouse)
            {
                case "left_click":
                    Send(new[] { MouseInput(MouseeventfLeftdown, 0), MouseInput(MouseeventfLeftup, 0) });
                    break;
                case "right_click":
                    Send(new[] { MouseInput(MouseeventfRightdown, 0), MouseInput(MouseeventfRightup, 0) });
                    break;
                case "middle_click":
                    Send(new[] { MouseInput(MouseeventfMiddledown, 0), MouseInput(MouseeventfMiddleup, 0) });
                    break;
                case "wheel_up":
                    Send(new[] { MouseInput(MouseeventfWheel, 120) });
                    break;
                case "wheel_down":
                    Send(new[] { MouseInput(MouseeventfWheel, unchecked((uint)-120)) });
                    break;
                default:
                    throw new ArgumentException($"알 수 없는 마우스 동작: {mouse}");
            }
            Thread.Sleep(15);
        }
    }

    private static INPUT KeyInput(ushort vk, bool up, uint extraFlags = 0)
    {
        return new INPUT
        {
            type = InputKeyboard,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = (up ? KeyeventfKeyup : 0) | extraFlags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                },
            },
        };
    }

    private static INPUT UnicodeInput(char c, bool up)
    {
        return new INPUT
        {
            type = InputKeyboard,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = c,
                    dwFlags = KeyeventfUnicode | (up ? KeyeventfKeyup : 0),
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                },
            },
        };
    }

    private static INPUT MouseInput(uint flags, uint data)
    {
        return new INPUT
        {
            type = InputMouse,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = data,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                },
            },
        };
    }

    private static void Send(INPUT[] inputs)
    {
        if (inputs.Length == 0) return;
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SendInput 실패");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}

