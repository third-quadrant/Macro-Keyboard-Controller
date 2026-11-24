using Fireworm.Core.Device;

namespace Fireworm.Core.Device;

public sealed class DeviceService
{
    public bool IsConnected { get; private set; }
    public string? VendorPath { get; private set; }
    public IReadOnlyList<HidDeviceInfo> Devices { get; private set; } = Array.Empty<HidDeviceInfo>();
    public string? LastError { get; private set; }
    public string StatusText => IsConnected ? "연결됨" : "연결 안 됨";
    public event EventHandler? StatusChanged;

    public void Refresh()
    {
        try
        {
            var devices = HidEnumerator.Enumerate();
            var vendor = devices.FirstOrDefault(d => d.IsVendor);
            var changed = IsConnected != (vendor != null) ||
                          !string.Equals(VendorPath, vendor?.Path, StringComparison.OrdinalIgnoreCase);

            Devices = devices;
            VendorPath = vendor?.Path;
            IsConnected = vendor != null;
            LastError = null;

            if (changed) StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            if (IsConnected)
            {
                IsConnected = false;
                VendorPath = null;
                StatusChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public VendorConfigWriter OpenWriter()
    {
        if (!IsConnected || string.IsNullOrEmpty(VendorPath))
            throw new InvalidOperationException("장치가 연결되어 있지 않습니다.");
        return new VendorConfigWriter(VendorPath);
    }
}
