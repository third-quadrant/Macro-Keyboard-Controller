using Fireworm.Core.Device;
using Fireworm.Core.Models;
using Fireworm.Core.Settings;

var cmdArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
if (cmdArgs.Length == 0)
{
    PrintUsage();
    return 1;
}

var confirm = cmdArgs.Contains("--confirm", StringComparer.OrdinalIgnoreCase);
var store = new SettingsStore();
var backup = store.LoadOrCreateBackup();
var device = new DeviceService();
device.Refresh();
if (!device.IsConnected)
{
    Console.WriteLine("ERROR: target device (MI_01) not connected.");
    return 2;
}

Console.WriteLine($"device={device.VendorPath}");
Console.WriteLine($"backup={store.BackupPath}");

switch (cmdArgs[0].ToLowerInvariant())
{
    case "writekey":
    {
        if (cmdArgs.Length < 3)
        {
            PrintUsage();
            return 1;
        }
        var slot = int.Parse(cmdArgs[1]);
        var usage = ParseByte(cmdArgs[2]);
        var b = backup.ForSlot(slot);
        Console.WriteLine($"plan: slot={slot} usage=0x{usage:X2} layer=1 type=1 mod=0 commit=AA AA");
        Console.WriteLine($"backup: slot={slot} usage=0x{b?.Usage:X2} mod=0x{b?.Modifier:X2} ({b?.Description})");
        Console.WriteLine("packets: [slot 11 01 00 00 00] [slot 11 01 01 00 usage] [AA AA]");
        if (!confirm)
        {
            Console.WriteLine("NOT WRITTEN: pass --confirm after user approval.");
            return 3;
        }
        using var writer = device.OpenWriter();
        writer.WriteKeySlot(slot, usage, 0, 1);
        Console.WriteLine($"WROTE slot={slot} usage=0x{usage:X2}");
        return 0;
    }
    case "restorekey":
    {
        if (cmdArgs.Length < 2)
        {
            PrintUsage();
            return 1;
        }
        var slot = int.Parse(cmdArgs[1]);
        var b = backup.ForSlot(slot);
        if (b == null)
        {
            Console.WriteLine($"ERROR: no backup for slot {slot}");
            return 4;
        }
        Console.WriteLine($"plan: restore slot={slot} usage=0x{b.Usage:X2} mod=0x{b.Modifier:X2} ({b.Description})");
        if (!confirm)
        {
            Console.WriteLine("NOT WRITTEN: pass --confirm after user approval.");
            return 3;
        }
        using var writer = device.OpenWriter();
        writer.WriteKeySlot(slot, b.Usage, b.Modifier, 1);
        Console.WriteLine($"RESTORED slot={slot} usage=0x{b.Usage:X2}");
        return 0;
    }
    case "led":
    {
        if (cmdArgs.Length < 2)
        {
            PrintUsage();
            return 1;
        }
        var mode = byte.Parse(cmdArgs[1]);
        if (mode > 2)
        {
            Console.WriteLine("ERROR: LED mode must be 0, 1, or 2.");
            return 1;
        }
        Console.WriteLine($"plan: LED mode={mode} slot=B0 layer=1 type=8 commit=AA A1");
        Console.WriteLine("packets: [B0 18 mode] [AA A1]");
        if (!confirm)
        {
            Console.WriteLine("NOT WRITTEN: pass --confirm after user approval.");
            return 3;
        }
        using var writer = device.OpenWriter();
        writer.WriteLedMode(mode, 1);
        Console.WriteLine($"WROTE LED mode={mode}");
        return 0;
    }
    case "ledkeys":
    {
        if (cmdArgs.Length < 2)
        {
            PrintUsage();
            return 1;
        }
        var mode = byte.Parse(cmdArgs[1]);
        if (mode > 2)
        {
            Console.WriteLine("ERROR: LED mode must be 0, 1, or 2.");
            return 1;
        }
        Console.WriteLine($"plan: slot LED type8 slots=1..6 mode={mode} commit=AA A1");
        Console.WriteLine("packets per slot: [slot 18 mode 00 00 00] [AA A1]");
        if (!confirm)
        {
            Console.WriteLine("NOT WRITTEN: pass --confirm after user approval.");
            return 3;
        }
        using var writer = device.OpenWriter();
        for (var slot = 1; slot <= 6; slot++)
        {
            writer.WriteSlotLed(slot, mode);
            Console.WriteLine($"WROTE slot-led slot={slot} mode={mode}");
        }
        return 0;
    }
    case "ledkey":
    {
        if (cmdArgs.Length < 3)
        {
            PrintUsage();
            return 1;
        }
        var slot = int.Parse(cmdArgs[1]);
        var mode = byte.Parse(cmdArgs[2]);
        Console.WriteLine($"plan: slot LED slot={slot} mode={mode} commit=AA A1");
        if (!confirm)
        {
            Console.WriteLine("NOT WRITTEN: pass --confirm after user approval.");
            return 3;
        }
        using var writer = device.OpenWriter();
        writer.WriteSlotLed(slot, mode);
        Console.WriteLine($"WROTE slot-led slot={slot} mode={mode}");
        return 0;
    }
    case "ledon":
    {
        if (cmdArgs.Length < 2)
        {
            PrintUsage();
            return 1;
        }
        var slot = int.Parse(cmdArgs[1]);
        var doCommit = cmdArgs.Contains("--commit", StringComparer.OrdinalIgnoreCase);
        Console.WriteLine($"plan: slot={slot} LED mode=1 commit={doCommit}");
        if (!confirm)
        {
            Console.WriteLine("NOT WRITTEN: pass --confirm after user approval.");
            return 3;
        }
        using var writer = device.OpenWriter();
        writer.WriteSlotLed(slot, 1, commit: doCommit);
        Console.WriteLine($"WROTE slot={slot} LED mode=1 commit={doCommit}");
        return 0;
    }
    case "ledoff":
    {
        if (cmdArgs.Length < 2)
        {
            PrintUsage();
            return 1;
        }
        var slot = int.Parse(cmdArgs[1]);
        var doCommit = cmdArgs.Contains("--commit", StringComparer.OrdinalIgnoreCase);
        Console.WriteLine($"plan: slot={slot} LED mode=0 commit={doCommit}");
        if (!confirm)
        {
            Console.WriteLine("NOT WRITTEN: pass --confirm after user approval.");
            return 3;
        }
        using var writer = device.OpenWriter();
        writer.WriteSlotLed(slot, 0, commit: doCommit);
        Console.WriteLine($"WROTE slot={slot} LED mode=0 commit={doCommit}");
        return 0;
    }
    case "restore":
    {
        Console.WriteLine("plan: restore KEY1~KEY6 from behaviour backup");
        foreach (var k in backup.Keys.OrderBy(k => k.Slot))
            Console.WriteLine($"  {k.Key}: 0x{k.Usage:X2} mod 0x{k.Modifier:X2} ({k.Description})");
        if (!confirm)
        {
            Console.WriteLine("NOT WRITTEN: pass --confirm after user approval.");
            return 3;
        }
        using var writer = device.OpenWriter();
        writer.WriteRestore(backup, 1);
        Console.WriteLine("RESTORED ALL");
        return 0;
    }
    default:
        PrintUsage();
        return 1;
}

static byte ParseByte(string value)
{
    value = value.Trim();
    if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        return Convert.ToByte(value[2..], 16);
    if (byte.TryParse(value, out var b)) return b;
    return Convert.ToByte(value, 16);
}

static void PrintUsage()
{
    Console.WriteLine("ApplyConfig writekey <slot> <usage> [--confirm]");
    Console.WriteLine("ApplyConfig led <0|1|2> [--confirm]");
    Console.WriteLine("ApplyConfig ledkey <slot> <mode> [--confirm]");
    Console.WriteLine("ApplyConfig ledkeys <mode> [--confirm]");
    Console.WriteLine("ApplyConfig ledoff <slot> [--commit] [--confirm]");
    Console.WriteLine("ApplyConfig ledon <slot> [--commit] [--confirm]");
    Console.WriteLine("ApplyConfig restorekey <slot> [--confirm]");
    Console.WriteLine("ApplyConfig restore [--confirm]");
    Console.WriteLine("Examples: writekey 1 0x68 --confirm | restorekey 1 --confirm");
}

