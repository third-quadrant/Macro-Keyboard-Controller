using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;

// READ-ONLY HID analysis tool for Phase 0.
// It never calls WriteFile / HidD_SetFeature / HidD_SetOutputReport.
// Modes:
//   list                         enumerate HID collections, caps, raw report descriptor
//   capture <sec> <logfile>      open matching collections for READ and log input reports
//   raw <sec> <logfile>          Raw Input (WM_INPUT) capture, filtered to this VID/PID
//   opencheck                    try opening each matching collection read-only
namespace HidProbe
{
    internal static class Native
    {
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

        public const uint GENERIC_READ = 0x80000000;
        public const uint FILE_SHARE_READ = 1;
        public const uint FILE_SHARE_WRITE = 2;
        public const uint OPEN_EXISTING = 3;
        public const uint IOCTL_HID_GET_REPORT_DESCRIPTOR = 0x000B0007;

        [DllImport("hid.dll")]
        public static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("hid.dll")]
        public static extern bool HidD_GetAttributes(IntPtr h, out HIDD_ATTRIBUTES a);
        [DllImport("hid.dll", CharSet = CharSet.Unicode)]
        public static extern bool HidD_GetManufacturerString(IntPtr h, StringBuilder b, int bytes);
        [DllImport("hid.dll", CharSet = CharSet.Unicode)]
        public static extern bool HidD_GetProductString(IntPtr h, StringBuilder b, int bytes);
        [DllImport("hid.dll", CharSet = CharSet.Unicode)]
        public static extern bool HidD_GetSerialNumberString(IntPtr h, StringBuilder b, int bytes);
        [DllImport("hid.dll")]
        public static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr p);
        [DllImport("hid.dll")]
        public static extern bool HidD_FreePreparsedData(IntPtr p);
        [DllImport("hid.dll")]
        public static extern int HidP_GetCaps(IntPtr p, out HIDP_CAPS caps);
        [DllImport("hid.dll")]
        public static extern bool HidD_GetInputReport(IntPtr h, byte[] buffer, int bufferLength);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr enumerator, IntPtr hwnd, uint flags);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid g, uint index, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref SP_DEVICE_INTERFACE_DATA iface, IntPtr detail, uint size, out uint required, IntPtr devInfo);
        [DllImport("setupapi.dll")]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadFile(IntPtr h, byte[] buf, uint toRead, out uint read, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteFile(IntPtr h, byte[] buf, uint toWrite, out uint written, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DeviceIoControl(IntPtr h, uint code, IntPtr inBuf, uint inSize, byte[] outBuf, uint outSize, out uint returned, IntPtr overlapped);
        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")]
        public static extern bool Beep(uint freq, uint dur);

        public static IntPtr INVALID = new IntPtr(-1);

        public static List<string> EnumerateHidPaths()
        {
            List<string> list = new List<string>();
            Guid guid;
            HidD_GetHidGuid(out guid);
            IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, 0x12);
            if (set == INVALID) return list;
            try
            {
                for (uint i = 0; ; i++)
                {
                    SP_DEVICE_INTERFACE_DATA data = new SP_DEVICE_INTERFACE_DATA();
                    data.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data)) break;
                    uint required = 0;
                    SetupDiGetDeviceInterfaceDetailW(set, ref data, IntPtr.Zero, 0, out required, IntPtr.Zero);
                    if (required == 0) continue;
                    IntPtr detail = Marshal.AllocHGlobal((int)required);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        uint got;
                        if (SetupDiGetDeviceInterfaceDetailW(set, ref data, detail, required, out got, IntPtr.Zero))
                        {
                            string path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                            if (!string.IsNullOrEmpty(path)) list.Add(path);
                        }
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return list;
        }
    }

    internal sealed class DevInfo
    {
        public string Path;
        public string Tag;          // mi_00 / mi_01 / mi_02&col01 ...
        public ushort Vid, Pid, Version;
        public bool Opened;
        public int OpenError;
        public Native.HIDP_CAPS Caps;
        public byte[] Descriptor;
        public int DescriptorError;
        public string Manufacturer = "", Product = "", Serial = "";
        public bool UsesReportIds;
    }

    internal static class Program
    {
        private const ushort TargetVid = 0x1189;
        private const ushort TargetPid = 0x8890;

        private static string TagOf(string path)
        {
            string p = path.ToLowerInvariant();
            int i = p.IndexOf("mi_");
            if (i < 0) return "?";
            int e = p.IndexOf('#', i);
            if (e < 0) e = p.Length;
            return p.Substring(i, e - i);
        }

        private static DevInfo Describe(string path, bool withDescriptor)
        {
            DevInfo d = new DevInfo();
            d.Path = path;
            d.Tag = TagOf(path);
            IntPtr h = Native.CreateFileW(path, 0, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == Native.INVALID) { d.OpenError = Marshal.GetLastWin32Error(); return d; }
            try
            {
                Native.HIDD_ATTRIBUTES a = new Native.HIDD_ATTRIBUTES();
                a.Size = (uint)Marshal.SizeOf(typeof(Native.HIDD_ATTRIBUTES));
                if (Native.HidD_GetAttributes(h, out a))
                {
                    d.Vid = a.VendorID; d.Pid = a.ProductID; d.Version = a.VersionNumber;
                }
                StringBuilder sb = new StringBuilder(512);
                if (Native.HidD_GetManufacturerString(h, sb, 1024)) d.Manufacturer = sb.ToString();
                sb.Clear();
                if (Native.HidD_GetProductString(h, sb, 1024)) d.Product = sb.ToString();
                sb.Clear();
                if (Native.HidD_GetSerialNumberString(h, sb, 1024)) d.Serial = sb.ToString();

                IntPtr prep;
                if (Native.HidD_GetPreparsedData(h, out prep))
                {
                    try { Native.HidP_GetCaps(prep, out d.Caps); }
                    finally { Native.HidD_FreePreparsedData(prep); }
                }
                if (withDescriptor)
                {
                    byte[] buf = new byte[4096];
                    uint ret;
                    if (Native.DeviceIoControl(h, Native.IOCTL_HID_GET_REPORT_DESCRIPTOR, IntPtr.Zero, 0, buf, (uint)buf.Length, out ret, IntPtr.Zero) && ret > 0)
                    {
                        d.Descriptor = new byte[ret];
                        Array.Copy(buf, d.Descriptor, (int)ret);
                    }
                    else
                    {
                        d.DescriptorError = Marshal.GetLastWin32Error();
                    }
                }
            }
            finally { Native.CloseHandle(h); }
            return d;
        }

        private static List<DevInfo> All(bool withDescriptor)
        {
            List<DevInfo> result = new List<DevInfo>();
            foreach (string p in Native.EnumerateHidPaths())
            {
                DevInfo d = Describe(p, withDescriptor);
                if (d.Vid == TargetVid && d.Pid == TargetPid) result.Add(d);
            }
            return result;
        }

        // ---------------- report descriptor parser ----------------

        private sealed class Parser
        {
            private readonly byte[] b;
            private int i;
            private uint usagePage;
            private uint reportSize, reportCount, reportId;
            private int logicalMin = 0, logicalMax = 0;
            private readonly List<string> usageStack = new List<string>();
            private uint usageMin, usageMax;
            private bool hasUsageRange;
            private readonly List<string> coll = new List<string>();
            private readonly Dictionary<string, long> offsets = new Dictionary<string, long>();
            private readonly StringBuilder o = new StringBuilder();

            public bool UsesIds;
            public Parser(byte[] data) { b = data; }

            private static int Signed(uint v, int size)
            {
                if (size == 1) return (sbyte)v;
                if (size == 2) return (short)v;
                return (int)v;
            }

            private static string UsageStr(uint page, uint id)
            {
                return string.Format("{0:X4}:{1:X4}", page, id);
            }

            public string Run()
            {
                while (i < b.Length)
                {
                    int start = i;
                    byte prefix = b[i++];
                    int size = prefix & 3; if (size == 3) size = 4;
                    int type = (prefix >> 2) & 3;
                    int tag = (prefix >> 4) & 0xF;
                    uint data = 0;
                    for (int k = 0; k < size && i < b.Length; k++) data |= (uint)b[i++] << (8 * k);

                    if (type == 1) // global
                    {
                        switch (tag)
                        {
                            case 0: usagePage = data; break;
                            case 1: logicalMin = Signed(data, size); break;
                            case 2: logicalMax = Signed(data, size); break;
                            case 7: reportSize = data; break;
                            case 8: reportId = data; if (data != 0) UsesIds = true; break;
                            case 9: reportCount = data; break;
                        }
                    }
                    else if (type == 2) // local
                    {
                        if (tag == 0)
                        {
                            uint page = size == 4 ? (data >> 16) : usagePage;
                            uint id = size == 4 ? (data & 0xFFFF) : data;
                            usageStack.Add(UsageStr(page, id));
                        }
                        else if (tag == 1) { usageMin = data; hasUsageRange = true; }
                        else if (tag == 2) { usageMax = data; hasUsageRange = true; }
                    }
                    else if (type == 0) // main
                    {
                        string indent = new string(' ', coll.Count * 2);
                        if (tag == 10) // Collection
                        {
                            string us = usageStack.Count > 0 ? usageStack[usageStack.Count - 1] : "?";
                            string[] kinds = { "Physical", "Application", "Logical", "Report", "NamedArray", "UsageSwitch", "UsageModifier" };
                            string kind = data < kinds.Length ? kinds[data] : data.ToString();
                            o.AppendFormat("  {0}Collection {1} ({2})\r\n", indent, us, kind);
                            coll.Add(kind + " " + us);
                            usageStack.Clear(); hasUsageRange = false;
                        }
                        else if (tag == 12) // End Collection
                        {
                            if (coll.Count > 0) coll.RemoveAt(coll.Count - 1);
                            o.AppendFormat("  {0}EndCollection\r\n", indent);
                        }
                        else if (tag == 8 || tag == 9 || tag == 11) // Input/Output/Feature
                        {
                            string kind = tag == 8 ? "Input" : tag == 9 ? "Output" : "Feature";
                            string key = kind + ":" + reportId;
                            long off; offsets.TryGetValue(key, out off);
                            long bits = (long)reportSize * (long)reportCount;
                            offsets[key] = off + bits;
                            string flags = "";
                            if ((data & 1) != 0) flags += "Const,"; else flags += "Data,";
                            if ((data & 2) != 0) flags += "Var,"; else flags += "Arr,";
                            if ((data & 4) != 0) flags += "Rel,"; else flags += "Abs,";
                            if ((data & 8) != 0) flags += "Wrap,";
                            if ((data & 16) != 0) flags += "NonLinear,";
                            if ((data & 32) != 0) flags += "NoPref,";
                            if ((data & 64) != 0) flags += "Null,";
                            if ((data & 128) != 0) flags += "Vol,";
                            if ((data & 256) != 0) flags += "BufferedBytes,";
                            string us = string.Join(",", usageStack.ToArray());
                            if (hasUsageRange) us += string.Format(" range {0:X4}:{1:X4}..{2:X4}", usagePage, usageMin, usageMax);
                            o.AppendFormat("  {0}{1,-7} RID={2} off={3} bits={4} count={5} size={6} [{7}] usages={8}\r\n",
                                indent, kind, reportId, off, bits, reportCount, reportSize, flags.TrimEnd(','), us);
                            usageStack.Clear(); hasUsageRange = false;
                        }
                    }
                }
                return o.ToString();
            }

            public static string Hex(byte[] data)
            {
                StringBuilder s = new StringBuilder();
                for (int k = 0; k < data.Length; k++)
                {
                    if (k > 0 && k % 16 == 0) s.Append("\r\n    ");
                    else if (k > 0) s.Append(' ');
                    s.AppendFormat("{0:X2}", data[k]);
                }
                return s.ToString();
            }
        }

        // ---------------- modes ----------------

        private static void List()
        {
            Console.WriteLine("READ-ONLY probe (no output/feature reports are sent)");
            Console.WriteLine("OS: " + Environment.OSVersion + "  CLR: " + Environment.Version);
            Console.WriteLine();
            List<DevInfo> all = All(true);
            Console.WriteLine("Target collections: " + all.Count);
            foreach (DevInfo d in all)
            {
                Console.WriteLine("================================================================");
                Console.WriteLine("PATH      : " + d.Path);
                Console.WriteLine("TAG       : " + d.Tag);
                Console.WriteLine("VID/PID   : {0:X4}/{1:X4}  bcdDevice={2:X4}", d.Vid, d.Pid, d.Version);
                Console.WriteLine("MANUF     : " + d.Manufacturer);
                Console.WriteLine("PRODUCT   : " + d.Product);
                Console.WriteLine("SERIAL    : " + d.Serial);
                Console.WriteLine("CAPS      : UsagePage={0:X4} Usage={1:X4} In={2} Out={3} Feature={4} LinkColl={5}",
                    d.Caps.UsagePage, d.Caps.Usage, d.Caps.InputReportByteLength, d.Caps.OutputReportByteLength,
                    d.Caps.FeatureReportByteLength, d.Caps.NumberLinkCollectionNodes);
                if (d.Descriptor == null)
                {
                    Console.WriteLine("DESCRIPTOR: <unavailable> err=" + d.DescriptorError);
                    continue;
                }
                Parser p = new Parser(d.Descriptor);
                string parsed = p.Run();
                Console.WriteLine("REPORT IDS: " + (p.UsesIds ? "yes" : "no"));
                Console.WriteLine("DESCRIPTOR (" + d.Descriptor.Length + " bytes):");
                Console.WriteLine("    " + Parser.Hex(d.Descriptor));
                Console.WriteLine("PARSED:");
                Console.Write(parsed);
            }
        }

        private static void OpenCheck()
        {
            foreach (DevInfo d in All(false))
            {
                IntPtr h = Native.CreateFileW(d.Path, Native.GENERIC_READ,
                    Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
                if (h == Native.INVALID)
                    Console.WriteLine("{0}  READ OPEN FAIL err={1}", d.Tag, Marshal.GetLastWin32Error());
                else
                {
                    Console.WriteLine("{0}  READ OPEN OK   (In={1} Out={2} Feat={3})", d.Tag, d.Caps.InputReportByteLength, d.Caps.OutputReportByteLength, d.Caps.FeatureReportByteLength);
                    Native.CloseHandle(h);
                }
            }
        }

        private static void ReadOpen(string file)
        {
            StreamWriter fw = null;
            if (!string.IsNullOrEmpty(file)) fw = OpenLog(file);
            Action<string> P = delegate(string s)
            {
                Console.WriteLine(s);
                if (fw != null) fw.WriteLine(s);
            };
            WindowsIdentity id = WindowsIdentity.GetCurrent();
            P("READ-ONLY open probe (no GetInputReport, no writes)");
            P(string.Format("user={0} elevated-admin={1} auth={2}", id.Name, new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator), id.AuthenticationType));
            P("CreateFile arguments: (path, access, share=READ|WRITE, NULL, OPEN_EXISTING, 0, NULL)");
            uint[] shares = { 0, Native.FILE_SHARE_READ, Native.FILE_SHARE_WRITE, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE };
            uint[] accesses = { 0, 0x80u, 0x1u, Native.GENERIC_READ };
            string[] accessNames = { "0", "FILE_READ_ATTRIBUTES", "FILE_READ_DATA", "GENERIC_READ" };
            foreach (DevInfo d in All(false))
            {
                P(string.Format("===== {0} vid={1:X4} pid={2:X4} usage={3:X4}:{4:X4} In={5}",
                    d.Tag, d.Vid, d.Pid, d.Caps.UsagePage, d.Caps.Usage, d.Caps.InputReportByteLength));
                List<string> variants = new List<string>();
                variants.Add(d.Path);
                if (d.Path.EndsWith("\\kbd", StringComparison.OrdinalIgnoreCase))
                    variants.Add(d.Path.Substring(0, d.Path.Length - 4));
                for (int vi = 0; vi < variants.Count; vi++)
                {
                    P(string.Format("  path[{0}] = {1}", vi, variants[vi]));
                    for (int ai = 0; ai < accesses.Length; ai++)
                    {
                        for (int si = 0; si < shares.Length; si++)
                        {
                            IntPtr h = Native.CreateFileW(variants[vi], accesses[ai], shares[si], IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
                            if (h == Native.INVALID)
                            {
                                P(string.Format("    access={0,-22} share={1} FAIL err={2}", accessNames[ai], shares[si], Marshal.GetLastWin32Error()));
                            }
                            else
                            {
                                Native.HIDD_ATTRIBUTES a = new Native.HIDD_ATTRIBUTES();
                                a.Size = (uint)Marshal.SizeOf(typeof(Native.HIDD_ATTRIBUTES));
                                bool got = Native.HidD_GetAttributes(h, out a);
                                P(string.Format("    access={0,-22} share={1} OK   attr={2} vid={3:X4} pid={4:X4}", accessNames[ai], shares[si], got, a.VendorID, a.ProductID));
                                Native.CloseHandle(h);
                            }
                        }
                    }
                }
            }
            if (fw != null) { fw.Flush(); Console.WriteLine("log: " + file); }
        }

        private static void InputProbe()
        {            Console.WriteLine("READ-ONLY input probe: no WriteFile, no SetFeature, no SetOutputReport");
            uint[] shares = { 0, Native.FILE_SHARE_READ, Native.FILE_SHARE_WRITE, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE };
            uint[] accesses = { 0, Native.GENERIC_READ };
            foreach (DevInfo d in All(false))
            {
                Console.WriteLine("===== {0}  In={1} Out={2} Feat={3}", d.Tag, d.Caps.InputReportByteLength, d.Caps.OutputReportByteLength, d.Caps.FeatureReportByteLength);
                foreach (uint access in accesses)
                {
                    foreach (uint share in shares)
                    {
                        IntPtr h = Native.CreateFileW(d.Path, access, share, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
                        string mode = string.Format("access={0:X8} share={1}", access, share);
                        if (h == Native.INVALID)
                        {
                            Console.WriteLine("  OPEN FAIL {0} err={1}", mode, Marshal.GetLastWin32Error());
                            continue;
                        }
                        Console.WriteLine("  OPEN OK   {0}", mode);
                        int len = d.Caps.InputReportByteLength > 0 ? d.Caps.InputReportByteLength : 65;
                        if (len < 1) len = 1;
                        for (byte rid = 0; rid <= 3; rid++)
                        {
                            if (d.Caps.InputReportByteLength == 0) break;
                            byte[] buf = new byte[len];
                            buf[0] = rid;
                            bool ok = Native.HidD_GetInputReport(h, buf, len);
                            int err = ok ? 0 : Marshal.GetLastWin32Error();
                            Console.Write("    GetInputReport rid={0} ok={1} err={2}", rid, ok, err);
                            if (ok)
                            {
                                Console.Write(" data=");
                                for (int k = 0; k < Math.Min(len, 16); k++) Console.Write("{0:X2} ", buf[k]);
                            }
                            Console.WriteLine();
                        }
                        Native.CloseHandle(h);
                    }
                }
            }
        }

        private static void PollInput(int seconds, string file)
        {
            StreamWriter w = OpenLog(file);
            DateTime start = DateTime.Now;
            w.WriteLine("# READ-ONLY GetInputReport poll started {0:O} duration={1}s", start, seconds);
            List<IntPtr> handles = new List<IntPtr>();
            List<string> tags = new List<string>();
            List<byte> rids = new List<byte>();
            List<int> lens = new List<int>();
            foreach (DevInfo d in All(false))
            {
                if (d.Tag != "mi_01" && d.Tag != "mi_02&col02") continue;
                IntPtr h = Native.CreateFileW(d.Path, Native.GENERIC_READ,
                    Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
                if (h == Native.INVALID)
                {
                    w.WriteLine("# OPEN FAIL {0} err={1}", d.Tag, Marshal.GetLastWin32Error());
                    continue;
                }
                handles.Add(h);
                tags.Add(d.Tag);
                rids.Add(d.Tag == "mi_01" ? (byte)3 : (byte)2);
                lens.Add(d.Caps.InputReportByteLength > 0 ? d.Caps.InputReportByteLength : 65);
                w.WriteLine("# open {0} rid={1} len={2}", d.Tag, rids[rids.Count - 1], lens[lens.Count - 1]);
            }
            string[] last = new string[handles.Count];
            while ((DateTime.Now - start).TotalSeconds < seconds)
            {
                for (int i = 0; i < handles.Count; i++)
                {
                    byte[] buf = new byte[lens[i]];
                    buf[0] = rids[i];
                    if (Native.HidD_GetInputReport(handles[i], buf, lens[i]))
                    {
                        StringBuilder s = new StringBuilder();
                        for (int k = 0; k < lens[i]; k++) s.AppendFormat("{0:X2} ", buf[k]);
                        string hex = s.ToString().TrimEnd();
                        if (hex != last[i])
                        {
                            double ms = (DateTime.Now - start).TotalMilliseconds;
                            lock (logLock) w.WriteLine("{0,10:F1}ms {1,-14} {2}", ms, tags[i], hex);
                            last[i] = hex;
                        }
                    }
                }
                Thread.Sleep(20);
            }
            w.WriteLine("# stopped {0:O}", DateTime.Now);
            w.Flush();
            foreach (IntPtr h in handles) Native.CloseHandle(h);
            Console.WriteLine("log: " + file);
        }

        private static void Probe(int seconds, string file)
        {
            StreamWriter w = OpenLog(file);
            DateTime start = DateTime.Now;
            w.WriteLine("# PROBE: write one zero report (RID 3) then observe. No commit (AA AA) is sent. {0:O}", start);
            DevInfo target = null;
            foreach (DevInfo d in All(false)) if (d.Tag == "mi_01") target = d;
            if (target == null) { w.WriteLine("# mi_01 not found"); w.Flush(); return; }
            IntPtr h = Native.CreateFileW(target.Path, Native.GENERIC_READ | 0x40000000u,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == Native.INVALID)
            {
                w.WriteLine("# OPEN FAIL err=" + Marshal.GetLastWin32Error());
                w.Flush();
                return;
            }
            byte[] report = new byte[65];
            report[0] = 3;
            uint written;
            bool ok = Native.WriteFile(h, report, 65, out written, IntPtr.Zero);
            w.WriteLine("# WriteFile zero-report ok={0} err={1} written={2}", ok, ok ? 0 : Marshal.GetLastWin32Error(), written);
            w.Flush();
            string tag = "mi_01";
            Thread reader = new Thread(delegate()
            {
                byte[] buf = new byte[65];
                while (true)
                {
                    uint got;
                    if (!Native.ReadFile(h, buf, 65, out got, IntPtr.Zero))
                    {
                        int err = Marshal.GetLastWin32Error();
                        lock (logLock) w.WriteLine("# READ END err={0}", err);
                        return;
                    }
                    if (got > 0)
                    {
                        StringBuilder s = new StringBuilder();
                        for (int k = 0; k < got; k++) s.AppendFormat("{0:X2} ", buf[k]);
                        double ms = (DateTime.Now - start).TotalMilliseconds;
                        lock (logLock) w.WriteLine("{0,10:F1}ms {1}-read {2}", ms, tag, s.ToString().TrimEnd());
                    }
                }
            });
            reader.IsBackground = true;
            reader.Start();
            string last = null;
            while ((DateTime.Now - start).TotalSeconds < seconds)
            {
                byte[] buf = new byte[65];
                buf[0] = 3;
                if (Native.HidD_GetInputReport(h, buf, 65))
                {
                    StringBuilder s = new StringBuilder();
                    for (int k = 0; k < 65; k++) s.AppendFormat("{0:X2} ", buf[k]);
                    string hex = s.ToString().TrimEnd();
                    if (hex != last)
                    {
                        double ms = (DateTime.Now - start).TotalMilliseconds;
                        lock (logLock) w.WriteLine("{0,10:F1}ms mi_01-get {1}", ms, hex);
                        last = hex;
                    }
                }
                Thread.Sleep(20);
            }
            w.WriteLine("# stopped {0:O}", DateTime.Now);
            w.Flush();
            Console.WriteLine("log: " + file);
            Environment.Exit(0);
        }

        private static void ReadRaw(int seconds, string file)
        {
            // Intended to run as SYSTEM via a temporary scheduled task.
            // Read-only: opens the exclusive keyboard collections and logs raw input reports.
            Thread.Sleep(30000);
            StreamWriter w = OpenLog(file);
            DateTime start = DateTime.Now;
            WindowsIdentity id = WindowsIdentity.GetCurrent();
            w.WriteLine("# SYSTEM raw read started {0:O} capture={1}s", start, seconds);
            w.WriteLine("# identity user={0} elevated-admin={1}", id.Name, new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator));
            List<IntPtr> handles = new List<IntPtr>();
            List<string> tags = new List<string>();
            List<int> lens = new List<int>();
            foreach (DevInfo d in All(false))
            {
                if (d.Tag != "mi_00" && d.Tag != "mi_02&col01") continue;
                string path = d.Path;
                IntPtr h = Native.CreateFileW(path, Native.GENERIC_READ,
                    Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
                if (h == Native.INVALID)
                {
                    int err = Marshal.GetLastWin32Error();
                    w.WriteLine("# OPEN FAIL {0} path={1} err={2}", d.Tag, path, err);
                    if (path.EndsWith("\\kbd", StringComparison.OrdinalIgnoreCase))
                    {
                        path = path.Substring(0, path.Length - 4);
                        h = Native.CreateFileW(path, Native.GENERIC_READ,
                            Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
                        if (h == Native.INVALID)
                            w.WriteLine("# OPEN FAIL {0} (stripped \\kbd) path={1} err={2}", d.Tag, path, Marshal.GetLastWin32Error());
                        else
                            w.WriteLine("# OPEN OK {0} (stripped \\kbd) path={1}", d.Tag, path);
                    }
                }
                if (h == Native.INVALID) continue;
                handles.Add(h);
                tags.Add(d.Tag);
                lens.Add(d.Caps.InputReportByteLength > 0 ? d.Caps.InputReportByteLength : 16);
                w.WriteLine("# OPEN OK {0} path={1} len={2}", d.Tag, path, lens[lens.Count - 1]);
            }
            w.WriteLine("# CAPTURE START {0:O}", DateTime.Now);
            w.Flush();
            for (int i = 0; i < handles.Count; i++)
            {
                IntPtr handle = handles[i];
                string tag = tags[i];
                int len = lens[i];
                Thread t = new Thread(delegate()
                {
                    byte[] buf = new byte[len];
                    while (true)
                    {
                        uint got;
                        if (!Native.ReadFile(handle, buf, (uint)len, out got, IntPtr.Zero))
                        {
                            int err = Marshal.GetLastWin32Error();
                            lock (logLock) w.WriteLine("# READ END {0} err={1}", tag, err);
                            return;
                        }
                        if (got > 0)
                        {
                            double ms = (DateTime.Now - start).TotalMilliseconds;
                            StringBuilder s = new StringBuilder();
                            for (int k = 0; k < got; k++) s.AppendFormat("{0:X2} ", buf[k]);
                            lock (logLock) w.WriteLine("{0,10:F1}ms {1,-14} len={2} {3}", ms, tag, got, s.ToString().TrimEnd());
                        }
                    }
                });
                t.IsBackground = true;
                t.Start();
            }
            Thread.Sleep(seconds * 1000);
            w.WriteLine("# CAPTURE END {0:O}", DateTime.Now);
            w.Flush();
            Environment.Exit(0);
        }

        private static StreamWriter OpenLog(string file)
        {
            FileStream fs = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            StreamWriter w = new StreamWriter(fs, new UTF8Encoding(false));
            w.AutoFlush = true;
            return w;
        }

        private static object logLock = new object();
        private static DateTime t0;
        private static bool silent;

        private static void Log(StreamWriter w, string tag, int rid, byte[] buf, int n)
        {
            StringBuilder s = new StringBuilder();
            double ms = (DateTime.Now - t0).TotalMilliseconds;
            s.AppendFormat("{0,10:F1}ms {1,-14} len={2,3} rid={3:X2} data=", ms, tag, n, rid);
            for (int k = 0; k < n; k++) s.AppendFormat("{0:X2} ", buf[k]);
            lock (logLock) w.WriteLine(s.ToString());
        }

        private static void Capture(int seconds, string file)
        {
            List<DevInfo> all = All(true);
            StreamWriter w = OpenLog(file);
            t0 = DateTime.Now;
            w.WriteLine("# READ-ONLY capture started {0:O}", DateTime.Now);
            w.WriteLine("# duration={0}s  collections={1}", seconds, all.Count);
            List<IntPtr> handles = new List<IntPtr>();
            foreach (DevInfo d in all)
            {
                w.WriteLine("# {0}  {1}  In={2} Out={3} Feat={4}", d.Tag, d.Path, d.Caps.InputReportByteLength, d.Caps.OutputReportByteLength, d.Caps.FeatureReportByteLength);
                IntPtr h = Native.CreateFileW(d.Path, Native.GENERIC_READ,
                    Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
                if (h == Native.INVALID)
                {
                    int err = Marshal.GetLastWin32Error();
                    w.WriteLine("# OPEN FAIL {0} err={1}", d.Tag, err);
                    continue;
                }
                handles.Add(h);
                int len = d.Caps.InputReportByteLength > 0 ? d.Caps.InputReportByteLength : 64;
                string tag = d.Tag;
                IntPtr handle = h;
                Thread t = new Thread(delegate()
                {
                    byte[] buf = new byte[len];
                    while (true)
                    {
                        uint got;
                        if (!Native.ReadFile(handle, buf, (uint)len, out got, IntPtr.Zero))
                        {
                            int err = Marshal.GetLastWin32Error();
                            lock (logLock) w.WriteLine("# READ END {0} err={1}", tag, err);
                            return;
                        }
                        if (got > 0)
                        {
                            int rid = buf[0];
                            Log(w, tag, rid, buf, (int)got);
                        }
                    }
                });
                t.IsBackground = true;
                t.Start();
            }
            w.WriteLine("# capture running. 2 beeps = start, 1 beep = end.");
            if (!silent) { Native.Beep(880, 120); Thread.Sleep(120); Native.Beep(880, 120); }
            Thread.Sleep(seconds * 1000);
            if (!silent) Native.Beep(440, 500);
            w.WriteLine("# capture stopped {0:O}", DateTime.Now);
            w.Flush();
            Console.WriteLine("log: " + file);
            Environment.Exit(0);
        }

        // ---------------- Raw Input mode ----------------

        private const int WM_INPUT = 0x00FF;
        private const int WM_TIMER = 0x0113;
        private const uint RID_INPUT = 0x10000003;
        private const uint RIDI_DEVICENAME = 0x20000007;
        private const uint RIDEV_INPUTSINK = 0x00000100;
        private static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

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

        private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassW(ref WNDCLASS wc);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASS
        {
            public uint style;
            public WndProc lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowExW(uint exStyle, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProcW(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] d, uint n, uint size);
        [DllImport("user32.dll")]
        private static extern uint GetRawInputData(IntPtr h, uint cmd, IntPtr data, ref uint size, uint headerSize);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint GetRawInputDeviceInfoW(IntPtr h, uint cmd, StringBuilder data, ref uint size);
        [DllImport("user32.dll")]
        private static extern IntPtr SetTimer(IntPtr h, IntPtr id, uint ms, IntPtr proc);
        [DllImport("user32.dll")]
        private static extern bool GetMessageW(out MSG m, IntPtr h, uint min, uint max);
        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG m);
        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessageW(ref MSG m);
        [StructLayout(LayoutKind.Sequential)]
        private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int x; public int y; }

        private static StreamWriter rawLog;
        private static readonly Dictionary<IntPtr, string> devNames = new Dictionary<IntPtr, string>();
        private static WndProc wndProcRef;

        private static string DeviceName(IntPtr h)
        {
            string name;
            if (devNames.TryGetValue(h, out name)) return name;
            uint size = 0;
            GetRawInputDeviceInfoW(h, RIDI_DEVICENAME, null, ref size);
            if (size > 0)
            {
                StringBuilder sb = new StringBuilder((int)size + 2);
                if (GetRawInputDeviceInfoW(h, RIDI_DEVICENAME, sb, ref size) > 0) name = sb.ToString();
            }
            if (name == null) name = "?";
            devNames[h] = name;
            return name;
        }

        private static bool IsTarget(string name)
        {
            string n = name.ToLowerInvariant();
            return n.Contains("vid_1189") && n.Contains("pid_8890");
        }

        private static IntPtr OnWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_INPUT)
            {
                uint size = 0;
                GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER)));
                if (size > 0)
                {
                    IntPtr buf = Marshal.AllocHGlobal((int)size);
                    try
                    {
                        if (GetRawInputData(lParam, RID_INPUT, buf, ref size, (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER))) == size)
                        {
                            RAWINPUTHEADER hdr = (RAWINPUTHEADER)Marshal.PtrToStructure(buf, typeof(RAWINPUTHEADER));
                            string name = DeviceName(hdr.hDevice);
                            if (IsTarget(name))
                            {
                                double ms = (DateTime.Now - t0).TotalMilliseconds;
                                string tag = TagOf(name);
                                if (hdr.dwType == 1) // keyboard
                                {
                                    RAWKEYBOARD kb = (RAWKEYBOARD)Marshal.PtrToStructure(IntPtr.Add(buf, Marshal.SizeOf(typeof(RAWINPUTHEADER))), typeof(RAWKEYBOARD));
                                    lock (logLock) rawLog.WriteLine("{0,10:F1}ms {1,-14} RAWKEY vk={2:X2} make={3:X2} flags={4:X4} msg={5:X4} extra={6:X8}",
                                        ms, tag, kb.VKey, kb.MakeCode, kb.Flags, kb.Message, kb.ExtraInformation);
                                }
                                else if (hdr.dwType == 2) // HID
                                {
                                    int p = Marshal.SizeOf(typeof(RAWINPUTHEADER));
                                    uint sizeHid = (uint)Marshal.ReadInt32(buf, p);
                                    uint count = (uint)Marshal.ReadInt32(buf, p + 4);
                                    StringBuilder s = new StringBuilder();
                                    for (uint c = 0; c < count; c++)
                                    {
                                        s.Append(" | ");
                                        for (uint k = 0; k < sizeHid; k++)
                                            s.AppendFormat("{0:X2} ", Marshal.ReadByte(buf, p + 8 + (int)(c * sizeHid + k)));
                                    }
                                    lock (logLock) rawLog.WriteLine("{0,10:F1}ms {1,-14} RAWHID size={2} count={3} data={4}", ms, tag, sizeHid, count, s.ToString());
                                }
                                else
                                {
                                    lock (logLock) rawLog.WriteLine("{0,10:F1}ms {1,-14} RAW type={2} size={3}", ms, tag, hdr.dwType, hdr.dwSize);
                                }
                            }
                        }
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            return DefWindowProcW(hWnd, msg, wParam, lParam);
        }

        private static void Raw(int seconds, string file)
        {
            rawLog = OpenLog(file);
            t0 = DateTime.Now;
            rawLog.WriteLine("# Raw Input capture started {0:O}  duration={1}s", DateTime.Now, seconds);
            List<DevInfo> all = All(true);
            foreach (DevInfo d in all) rawLog.WriteLine("# {0} {1} usage={2:X4}:{3:X4}", d.Tag, d.Path, d.Caps.UsagePage, d.Caps.Usage);

            wndProcRef = OnWndProc;
            WNDCLASS wc = new WNDCLASS();
            wc.lpfnWndProc = wndProcRef;
            wc.lpszClassName = "HidProbeRawWnd";
            ushort atom = RegisterClassW(ref wc);
            if (atom == 0) { rawLog.WriteLine("# RegisterClass failed err=" + Marshal.GetLastWin32Error()); Environment.Exit(2); }
            IntPtr hwnd = CreateWindowExW(0, "HidProbeRawWnd", "probe", 0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (hwnd == IntPtr.Zero) { rawLog.WriteLine("# CreateWindow failed err=" + Marshal.GetLastWin32Error()); Environment.Exit(2); }

            List<RAWINPUTDEVICE> devs = new List<RAWINPUTDEVICE>();
            devs.Add(new RAWINPUTDEVICE { usUsagePage = 0x01, usUsage = 0x06, dwFlags = RIDEV_INPUTSINK, hwndTarget = hwnd });
            devs.Add(new RAWINPUTDEVICE { usUsagePage = 0x0C, usUsage = 0x01, dwFlags = RIDEV_INPUTSINK, hwndTarget = hwnd });
            devs.Add(new RAWINPUTDEVICE { usUsagePage = 0x01, usUsage = 0x02, dwFlags = RIDEV_INPUTSINK, hwndTarget = hwnd });
            foreach (DevInfo d in all)
            {
                if (d.Caps.UsagePage >= 0xFF00)
                {
                    bool dup = false;
                    foreach (RAWINPUTDEVICE r in devs) if (r.usUsagePage == d.Caps.UsagePage && r.usUsage == d.Caps.Usage) dup = true;
                    if (!dup) devs.Add(new RAWINPUTDEVICE { usUsagePage = d.Caps.UsagePage, usUsage = d.Caps.Usage, dwFlags = RIDEV_INPUTSINK, hwndTarget = hwnd });
                }
            }
            RAWINPUTDEVICE[] arr = devs.ToArray();
            if (!RegisterRawInputDevices(arr, (uint)arr.Length, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE))))
                rawLog.WriteLine("# RegisterRawInputDevices failed err=" + Marshal.GetLastWin32Error());
            foreach (RAWINPUTDEVICE r in arr) rawLog.WriteLine("# registered {0:X4}:{1:X4}", r.usUsagePage, r.usUsage);

            SetTimer(hwnd, new IntPtr(1), (uint)(seconds * 1000), IntPtr.Zero);
            if (!silent) { Native.Beep(880, 120); Thread.Sleep(120); Native.Beep(880, 120); }
            MSG msg;
            while (GetMessageW(out msg, IntPtr.Zero, 0, 0))
            {
                if (msg.message == WM_TIMER) break;
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }
            if (!silent) Native.Beep(440, 500);
            rawLog.WriteLine("# Raw Input stopped {0:O}", DateTime.Now);
            rawLog.Flush();
            Console.WriteLine("log: " + file);
            Environment.Exit(0);
        }

        private static int Main(string[] args)
        {
            try
            {
                if (args.Length == 0) { Console.WriteLine("usage: list | opencheck | capture <sec> <log> [quiet] | raw <sec> <log> [quiet]"); return 1; }
                foreach (string a in args) if (a == "quiet") silent = true;
                if (args[0] == "list") { List(); return 0; }
                if (args[0] == "opencheck") { OpenCheck(); return 0; }
                if (args[0] == "inputprobe") { InputProbe(); return 0; }
                if (args[0] == "readopen") { ReadOpen(args.Length >= 2 ? args[1] : null); return 0; }
                if (args[0] == "readraw" && args.Length >= 3) { ReadRaw(int.Parse(args[1]), args[2]); return 0; }
                if (args[0] == "pollinput" && args.Length >= 3) { PollInput(int.Parse(args[1]), args[2]); return 0; }
                if (args[0] == "probe" && args.Length >= 3) { Probe(int.Parse(args[1]), args[2]); return 0; }
                if (args[0] == "capture" && args.Length >= 3) { Capture(int.Parse(args[1]), args[2]); return 0; }
                if (args[0] == "raw" && args.Length >= 3) { Raw(int.Parse(args[1]), args[2]); return 0; }
                Console.WriteLine("bad args");
                return 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR: " + ex);
                return 2;
            }
        }
    }
}
