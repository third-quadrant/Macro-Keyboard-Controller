using Fireworm.Core.Models;

namespace Fireworm.Core.Device;

/// <summary>
/// Pure packet builder for the vendor configuration protocol.
/// Report ID 3 is added by <see cref="VendorConfigWriter"/>; payloads here are D0..D63.
/// </summary>
public static class VendorPacketBuilder
{
    public const byte ReportId = 3;
    public const byte CommitKey0 = 0xAA;
    public const byte CommitKey1 = 0xAA;
    public const byte CommitLed1 = 0xA1;
    public const int ReportLength = 64;

    public static byte[] BuildLayerSwitch(int layer)
    {
        var p = new byte[ReportLength];
        p[0] = 0xA1;
        p[1] = (byte)Math.Clamp(layer, 1, 3);
        return p;
    }

    public static byte[] BuildCommitKey()
    {
        var p = new byte[ReportLength];
        p[0] = CommitKey0;
        p[1] = CommitKey1;
        return p;
    }

    public static List<byte[]> BuildKeyPackets(int slot, int layer, byte usage, byte modifier = 0)
    {
        if (slot <= 0 || slot > 0xB0) throw new ArgumentOutOfRangeException(nameof(slot));
        var typeAndLayer = (byte)((Math.Clamp(layer, 1, 3) << 4) | 1);

        var first = new byte[ReportLength];
        first[0] = (byte)slot;
        first[1] = typeAndLayer;
        first[2] = 0x01; // group count
        first[3] = 0x00; // sub-report index 0: modifier-only step
        first[4] = modifier;
        first[5] = 0x00;

        var second = new byte[ReportLength];
        second[0] = (byte)slot;
        second[1] = typeAndLayer;
        second[2] = 0x01;
        second[3] = 0x01; // entry 1
        second[4] = modifier;
        second[5] = usage;

        return new List<byte[]> { first, second, BuildCommitKey() };
    }

    public static List<byte[]> BuildMediaPackets(int slot, int layer, byte usage)
    {
        var p = new byte[ReportLength];
        p[0] = (byte)slot;
        p[1] = (byte)((Math.Clamp(layer, 1, 3) << 4) | 2);
        p[2] = usage;
        p[3] = 0;
        return new List<byte[]> { p, BuildCommitKey() };
    }

    public static List<byte[]> BuildLedPackets(int layer, byte mode)
    {
        var p = new byte[ReportLength];
        p[0] = 0xB0;
        p[1] = (byte)((Math.Clamp(layer, 1, 3) << 4) | 8);
        p[2] = mode;
        var commit = new byte[ReportLength];
        commit[0] = 0xAA;
        commit[1] = CommitLed1;
        return new List<byte[]> { p, commit };
    }

    public static List<byte[]> BuildRestorePackets(DeviceBackup backup, int layer = 1)
    {
        var packets = new List<byte[]>();
        foreach (var key in backup.Keys.OrderBy(k => k.Slot))
        {
            if (key.Type.Equals("media", StringComparison.OrdinalIgnoreCase))
                packets.AddRange(BuildMediaPackets(key.Slot, layer, key.Usage));
            else
                packets.AddRange(BuildKeyPackets(key.Slot, layer, key.Usage, key.Modifier));
        }
        return packets;
    }
}
