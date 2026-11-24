using System.Runtime.InteropServices;

namespace Fireworm.Core.Device;

public static class HidEnumerator
{
    public static List<HidDeviceInfo> Enumerate(ushort vid = 0x1189, ushort pid = 0x8890)
    {
        var result = new List<HidDeviceInfo>();
        foreach (var path in EnumeratePaths())
        {
            // Cheap pre-filter before opening anything.
            var lower = path.ToLowerInvariant();
            if (!lower.Contains($"vid_{vid:x4}") || !lower.Contains($"pid_{pid:x4}"))
                continue;

            var info = Describe(path);
            if (info != null) result.Add(info);
        }
        return result;
    }

    public static HidDeviceInfo? FindVendor(ushort vid = 0x1189, ushort pid = 0x8890)
        => Enumerate(vid, pid).FirstOrDefault(d => d.IsVendor);

    private static HidDeviceInfo? Describe(string path)
    {
        var h = Native.CreateFileW(path, 0, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
            IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
        if (h == Native.INVALID) return null;

        try
        {
            var attrs = new Native.HIDD_ATTRIBUTES { Size = (uint)Marshal.SizeOf<Native.HIDD_ATTRIBUTES>() };
            Native.HidD_GetAttributes(h, out attrs);

            var caps = new Native.HIDP_CAPS { Reserved = new ushort[17] };
            if (Native.HidD_GetPreparsedData(h, out var prep))
            {
                try { Native.HidP_GetCaps(prep, out caps); }
                finally { Native.HidD_FreePreparsedData(prep); }
            }

            return new HidDeviceInfo
            {
                Path = path,
                Tag = TagFromPath(path),
                Vid = attrs.VendorID,
                Pid = attrs.ProductID,
                UsagePage = caps.UsagePage,
                Usage = caps.Usage,
                InputReportLength = caps.InputReportByteLength,
                OutputReportLength = caps.OutputReportByteLength,
                FeatureReportLength = caps.FeatureReportByteLength,
            };
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    private static string TagFromPath(string path)
    {
        var p = path.ToLowerInvariant();
        var i = p.IndexOf("mi_", StringComparison.Ordinal);
        if (i < 0) return "?";
        var e = p.IndexOf('#', i);
        if (e < 0) e = p.Length;
        return p[i..e];
    }

    private static List<string> EnumeratePaths()
    {
        var paths = new List<string>();
        Native.HidD_GetHidGuid(out var guid);
        var set = Native.SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, 0x12);
        if (set == Native.INVALID) return paths;

        try
        {
            for (uint index = 0; ; index++)
            {
                var data = new Native.SP_DEVICE_INTERFACE_DATA
                {
                    cbSize = (uint)Marshal.SizeOf<Native.SP_DEVICE_INTERFACE_DATA>(),
                };
                if (!Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref data))
                    break;

                Native.SetupDiGetDeviceInterfaceDetailW(set, ref data, IntPtr.Zero, 0, out var required, IntPtr.Zero);
                if (required == 0) continue;

                var detail = Marshal.AllocHGlobal((int)required);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (Native.SetupDiGetDeviceInterfaceDetailW(set, ref data, detail, required, out _, IntPtr.Zero))
                    {
                        var path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                        if (!string.IsNullOrEmpty(path)) paths.Add(path);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            Native.SetupDiDestroyDeviceInfoList(set);
        }

        return paths;
    }

    private static class Native
    {
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 1;
        public const uint FILE_SHARE_WRITE = 2;
        public const uint OPEN_EXISTING = 3;
        public static readonly IntPtr INVALID = new(-1);

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA
        {
            public uint cbSize;
            public Guid InterfaceClassGuid;
            public uint Flags;
            public UIntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDD_ATTRIBUTES
        {
            public uint Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")]
        public static extern void HidD_GetHidGuid(out Guid guid);

        [DllImport("hid.dll")]
        public static extern bool HidD_GetAttributes(IntPtr h, out HIDD_ATTRIBUTES attributes);

        [DllImport("hid.dll")]
        public static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr preparsed);

        [DllImport("hid.dll")]
        public static extern bool HidD_FreePreparsedData(IntPtr preparsed);

        [DllImport("hid.dll")]
        public static extern int HidP_GetCaps(IntPtr preparsed, out HIDP_CAPS caps);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr hwnd, uint flags);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid guid, uint index, ref SP_DEVICE_INTERFACE_DATA data);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, uint size, out uint required, IntPtr devInfo);

        [DllImport("setupapi.dll")]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr handle);
    }
}
