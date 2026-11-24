using System.Runtime.InteropServices;

namespace Fireworm.Core.Actions;

public sealed class KeyChord
{
    public ushort Key { get; init; }
    public List<ushort> Modifiers { get; init; } = new();
    public string Display { get; init; } = "";

    public override string ToString() => Display;
}

public static class KeyParser
{
    private const ushort VkShift = 0x10;
    private const ushort VkControl = 0x11;
    private const ushort VkAlt = 0x12;
    private const ushort VkLWin = 0x5B;

    public static bool TryParse(string text, out KeyChord chord, out string error)
    {
        chord = new KeyChord();
        error = "";
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "키를 입력하세요. 예: C, Ctrl+Z, F11";
            return false;
        }

        var tokens = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            error = "키 조합이 비어 있습니다.";
            return false;
        }

        var modifiers = new List<ushort>();
        ushort? key = null;
        foreach (var token in tokens)
        {
            if (token.Equals("CTRL", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("CONTROL", StringComparison.OrdinalIgnoreCase))
            {
                AddModifier(modifiers, VkControl);
                continue;
            }
            if (token.Equals("SHIFT", StringComparison.OrdinalIgnoreCase))
            {
                AddModifier(modifiers, VkShift);
                continue;
            }
            if (token.Equals("ALT", StringComparison.OrdinalIgnoreCase))
            {
                AddModifier(modifiers, VkAlt);
                continue;
            }
            if (token.Equals("WIN", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("WINDOWS", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("LWIN", StringComparison.OrdinalIgnoreCase))
            {
                AddModifier(modifiers, VkLWin);
                continue;
            }

            if (!TryMapKey(token, out var vk, out var needsShift))
            {
                error = $"알 수 없는 키: {token}";
                return false;
            }

            if (key is not null)
            {
                error = "키는 하나만 지정할 수 있습니다. 수정자는 Ctrl/Shift/Alt/Win을 사용하세요.";
                return false;
            }

            key = vk;
            if (needsShift) AddModifier(modifiers, VkShift);
        }

        if (key is null)
        {
            error = "수정자만으로는 동작을 만들 수 없습니다. 키를 하나 포함하세요.";
            return false;
        }

        chord = new KeyChord
        {
            Key = key.Value,
            Modifiers = modifiers,
            Display = string.Join("+", modifiers.Select(ModifierName).Append(KeyName(key.Value))),
        };
        return true;
    }

    private static void AddModifier(List<ushort> modifiers, ushort vk)
    {
        if (!modifiers.Contains(vk)) modifiers.Add(vk);
    }

    private static bool TryMapKey(string token, out ushort vk, out bool needsShift)
    {
        vk = 0;
        needsShift = false;
        var upper = token.ToUpperInvariant();

        if (upper.Length == 1)
        {
            var c = upper[0];
            if (c is >= 'A' and <= 'Z')
            {
                vk = (ushort)c;
                return true;
            }
            if (c is >= '0' and <= '9')
            {
                vk = (ushort)c;
                return true;
            }

            var scan = VkKeyScanW(c);
            if (scan != -1)
            {
                vk = (ushort)(scan & 0xFF);
                var state = (scan >> 8) & 0xFF;
                needsShift = (state & 1) != 0;
                return true;
            }
        }

        if (upper.StartsWith('F') && int.TryParse(upper.AsSpan(1), out var fn) && fn is >= 1 and <= 24)
        {
            vk = (ushort)(0x70 + (fn - 1));
            return true;
        }

        vk = upper switch
        {
            "ENTER" or "RETURN" => 0x0D,
            "ESC" or "ESCAPE" => 0x1B,
            "TAB" => 0x09,
            "SPACE" or "SPACEBAR" => 0x20,
            "BACKSPACE" or "BACK" => 0x08,
            "DELETE" or "DEL" => 0x2E,
            "INSERT" or "INS" => 0x2D,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" or "PGUP" => 0x21,
            "PAGEDOWN" or "PGDN" => 0x22,
            "LEFT" => 0x25,
            "UP" => 0x26,
            "RIGHT" => 0x27,
            "DOWN" => 0x28,
            _ => 0,
        };
        return vk != 0;
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
        0x2D => "Insert",
        0x24 => "Home",
        0x23 => "End",
        0x21 => "PageUp",
        0x22 => "PageDown",
        0x25 => "Left",
        0x26 => "Up",
        0x27 => "Right",
        0x28 => "Down",
        _ => $"VK 0x{vk:X2}",
    };

    private static string ModifierName(ushort vk) => vk switch
    {
        VkControl => "Ctrl",
        VkShift => "Shift",
        VkAlt => "Alt",
        VkLWin => "Win",
        _ => "?",
    };

    /// <summary>Maps a Windows virtual-key to a USB HID keyboard usage for behaviour backup.</summary>
    public static bool TryVKeyToHidUsage(int vk, out byte usage)
    {
        usage = 0;
        if (vk is >= 0x41 and <= 0x5A) { usage = (byte)(0x04 + (vk - 0x41)); return true; }
        if (vk is >= 0x31 and <= 0x39) { usage = (byte)(0x1E + (vk - 0x31)); return true; }
        if (vk == 0x30) { usage = 0x27; return true; }
        if (vk is >= 0x70 and <= 0x7B) { usage = (byte)(0x3A + (vk - 0x70)); return true; }
        if (vk is >= 0x7C and <= 0x81) { usage = (byte)(0x68 + (vk - 0x7C)); return true; }
        usage = vk switch
        {
            0x0D => 0x28,
            0x1B => 0x29,
            0x08 => 0x2A,
            0x09 => 0x2B,
            0x20 => 0x2C,
            0x2D => 0x49,
            0x24 => 0x4A,
            0x21 => 0x4B,
            0x2E => 0x4C,
            0x23 => 0x4D,
            0x22 => 0x4E,
            0x27 => 0x4F,
            0x25 => 0x50,
            0x28 => 0x51,
            0x26 => 0x52,
            _ => (byte)0,
        };
        return usage != 0;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern short VkKeyScanW(char ch);
}
