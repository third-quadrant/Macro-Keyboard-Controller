using System.ComponentModel;
using System.Runtime.InteropServices;
using Fireworm.Core.Models;

namespace Fireworm.Core.Device;

/// <summary>
/// Explicit, user-initiated writer for MI_01. It never runs automatically.
/// The caller must present the planned changes and a recovery path first.
/// </summary>
public sealed class VendorConfigWriter : IDisposable
{
    private readonly IntPtr _handle;
    private bool _disposed;

    public string Path { get; }

    public VendorConfigWriter(string path)
    {
        Path = path;
        _handle = Native.CreateFileW(path, Native.GENERIC_READ | Native.GENERIC_WRITE,
            Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero,
            Native.OPEN_EXISTING, 0, IntPtr.Zero);
        if (_handle == Native.INVALID)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "MI_01 벤더 인터페이스를 열지 못했습니다.");
    }

    public void WriteKeySlot(int slot, byte usage, byte modifier = 0, int layer = 1)
    {
        WritePacket(VendorPacketBuilder.BuildLayerSwitch(layer));
        foreach (var packet in VendorPacketBuilder.BuildKeyPackets(slot, layer, usage, modifier))
            WritePacket(packet);
        Thread.Sleep(25);
    }

    public void WriteRestore(DeviceBackup backup, int layer = 1)
    {
        foreach (var key in backup.Keys.OrderBy(k => k.Slot))
        {
            if (key.Type.Equals("media", StringComparison.OrdinalIgnoreCase))
            {
                WritePacket(VendorPacketBuilder.BuildLayerSwitch(layer));
                foreach (var packet in VendorPacketBuilder.BuildMediaPackets(key.Slot, layer, key.Usage))
                    WritePacket(packet);
            }
            else
            {
                WriteKeySlot(key.Slot, key.Usage, key.Modifier, layer);
            }
            Thread.Sleep(25);
        }
    }

    public void WriteLedMode(byte mode, int layer = 1)
    {
        foreach (var packet in VendorPacketBuilder.BuildLedPackets(layer, mode))
            WritePacket(packet);
    }

    /// <summary>Experimental per-slot LED write (type 8). Restore key mappings afterwards.</summary>
    public void WriteSlotLed(int slot, byte mode, byte d3 = 0, byte d4 = 0, byte d5 = 0, bool commit = true)
    {
        var payload = new byte[VendorPacketBuilder.ReportLength];
        payload[0] = (byte)slot;
        payload[1] = 0x18; // layer 1 | type 8
        payload[2] = mode;
        payload[3] = d3;
        payload[4] = d4;
        payload[5] = d5;
        WritePacket(payload);

        if (!commit) return;

        var commitPacket = new byte[VendorPacketBuilder.ReportLength];
        commitPacket[0] = 0xAA;
        commitPacket[1] = 0xA1;
        WritePacket(commitPacket);
    }

    private void WritePacket(byte[] payload)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(VendorConfigWriter));
        if (payload.Length != VendorPacketBuilder.ReportLength)
            throw new ArgumentException("payload must be 64 bytes", nameof(payload));

        var buffer = new byte[65];
        buffer[0] = VendorPacketBuilder.ReportId;
        Array.Copy(payload, 0, buffer, 1, payload.Length);

        if (!Native.WriteFile(_handle, buffer, (uint)buffer.Length, out var written, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "HID 출력 리포트 전송 실패");
        if (written != buffer.Length)
            throw new IOException($"HID 출력 리포트가 {written}/{buffer.Length}바이트만 전송됐습니다.");

        Thread.Sleep(12);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Native.CloseHandle(_handle);
    }

    private static class Native
    {
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 1;
        public const uint FILE_SHARE_WRITE = 2;
        public const uint OPEN_EXISTING = 3;
        public static readonly IntPtr INVALID = new(-1);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteFile(IntPtr handle, byte[] buffer, uint toWrite, out uint written, IntPtr overlapped);

        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr handle);
    }
}
