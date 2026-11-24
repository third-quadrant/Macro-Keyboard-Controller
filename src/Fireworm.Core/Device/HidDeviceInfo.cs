namespace Fireworm.Core.Device;

public sealed class HidDeviceInfo
{
    public string Path { get; init; } = "";
    public string Tag { get; init; } = "";
    public ushort Vid { get; init; }
    public ushort Pid { get; init; }
    public ushort UsagePage { get; init; }
    public ushort Usage { get; init; }
    public int InputReportLength { get; init; }
    public int OutputReportLength { get; init; }
    public int FeatureReportLength { get; init; }

    public bool IsVendor => Tag.Equals("mi_01", StringComparison.OrdinalIgnoreCase);
    public bool IsKeyboard => UsagePage == 0x01 && Usage == 0x06;
    public bool IsConsumer => UsagePage == 0x0C && Usage == 0x01;
}
