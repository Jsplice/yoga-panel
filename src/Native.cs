// P/Invoke-Schicht: Lenovo EnergyDrv, Akku, Energie, PDH, Prozesse, Display, SetupAPI.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace YogaTool
{
    // Lenovo "Energy Management" Treiber (AcpiVpc.sys). Codes und Bits wurden auf diesem Gerät
    // per Disassembly des Treibers und der DSDT verifiziert.
    static class EnergyDrv
    {
        public const uint IOCTL_HALS_SALS = 0x831020E8; // in 2 = HALS lesen, sonst SALS(wert)
        public const uint IOCTL_BATTERY_MODE = 0x831020F8; // in 0xFF = GBMD lesen, sonst SBMC(wert)
        public const uint IOCTL_DYTC = 0x8310213C;     // ACPI DYTC(arg)
        public const uint IOCTL_KBLC = 0x83102144;     // ACPI KBLC(arg) Tastaturbeleuchtung

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sa, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool DeviceIoControl(IntPtr h, uint code, ref uint inBuf, int inLen, out uint outBuf, int outLen, out int ret, IntPtr ov);
        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr h);

        public static bool Available()
        {
            IntPtr h = CreateFile("\\\\.\\EnergyDrv", 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h == new IntPtr(-1)) return false;
            CloseHandle(h);
            return true;
        }

        public static uint Call(uint code, uint input)
        {
            IntPtr h = CreateFile("\\\\.\\EnergyDrv", 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h == new IntPtr(-1)) throw new Exception("Lenovo-Treiber (EnergyDrv) nicht erreichbar");
            try
            {
                uint o; int r;
                if (!DeviceIoControl(h, code, ref input, 4, out o, 4, out r, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                return o;
            }
            finally { CloseHandle(h); }
        }
    }

    enum PerfMode { Unknown, Quiet, Balanced, Performance }
    enum ChargeMode { Unknown, Normal, Conservation, Rapid }

    static class Lenovo
    {
        // ---- Leistungsmodus (DYTC, Funktion MMC=11) ----
        public static PerfMode GetPerfMode()
        {
            uint o = EnergyDrv.Call(EnergyDrv.IOCTL_DYTC, 2);
            if ((o & 1) == 0) return PerfMode.Unknown;
            uint func = (o >> 8) & 0xF, mode = (o >> 12) & 0xF;
            if (func == 11 && mode == 3) return PerfMode.Quiet;
            if (func == 11 && mode == 2) return PerfMode.Performance;
            return PerfMode.Balanced;
        }

        public static void SetPerfMode(PerfMode m)
        {
            uint arg;
            if (m == PerfMode.Quiet) arg = 0x13B001;            // SET | MMC<<12 | 3<<16 | valid
            else if (m == PerfMode.Performance) arg = 0x12B001; // SET | MMC<<12 | 2<<16 | valid
            else arg = 0x1FB001;                                // SET | MMC<<12 | F<<16 | valid -> Standard
            EnergyDrv.Call(EnergyDrv.IOCTL_DYTC, arg);
        }

        // ---- Lademodus (GBMD/SBMC) ----
        public static ChargeMode GetChargeMode()
        {
            uint s = EnergyDrv.Call(EnergyDrv.IOCTL_BATTERY_MODE, 0xFF);
            if ((s & (1u << 5)) != 0) return ChargeMode.Conservation;
            if ((s & (1u << 2)) != 0) return ChargeMode.Rapid;
            return ChargeMode.Normal;
        }

        public static void SetChargeMode(ChargeMode m)
        {
            uint c = EnergyDrv.IOCTL_BATTERY_MODE;
            if (m == ChargeMode.Conservation) { EnergyDrv.Call(c, 8); EnergyDrv.Call(c, 3); }
            else if (m == ChargeMode.Rapid) { EnergyDrv.Call(c, 5); EnergyDrv.Call(c, 7); }
            else { EnergyDrv.Call(c, 5); EnergyDrv.Call(c, 8); }
        }

        // ---- HALS-Bits (aus DSDT): 7=USB-Laden aus/Standby, 10=Fn-Lock an, 15=AOUB ----
        public static uint Hals() { return EnergyDrv.Call(EnergyDrv.IOCTL_HALS_SALS, 2); }
        public static bool GetFnLock() { return (Hals() & (1u << 10)) != 0; }
        public static void SetFnLock(bool on) { EnergyDrv.Call(EnergyDrv.IOCTL_HALS_SALS, on ? 0x0Eu : 0x0Fu); }
        public static bool GetUsbCharge() { return (Hals() & (1u << 7)) != 0; }
        public static void SetUsbCharge(bool on) { EnergyDrv.Call(EnergyDrv.IOCTL_HALS_SALS, on ? 0x0Au : 0x0Bu); }

        // ---- Tastaturbeleuchtung (KBLC): Typ 3 = Stufen 0..3 (3 = automatisch) ----
        public static int KbdType()
        {
            uint o = EnergyDrv.Call(EnergyDrv.IOCTL_KBLC, 1);
            return (o & 1) != 0 ? (int)((o >> 1) & 0x7) : 0;
        }
        public static int KbdMax(int type) { return type == 1 ? 1 : type == 2 ? 2 : type == 3 ? 3 : 0; }
        public static int GetKbd(int type)
        {
            uint o = EnergyDrv.Call(EnergyDrv.IOCTL_KBLC, (uint)(2 | (type << 4)));
            return (o & 1) != 0 ? (int)((o >> 1) & 0x7) : -1;
        }
        public static void SetKbd(int type, int level)
        {
            EnergyDrv.Call(EnergyDrv.IOCTL_KBLC, (uint)(3 | (type << 4) | (level << 16)));
        }
        public static string KbdName(int type, int level)
        {
            if (level < 0) return "?";
            if (level == 0) return "aus";
            if (type == 3 && level == 3) return "auto";
            if (type == 1) return "an";
            return level == 1 ? "niedrig" : "hoch";
        }
    }

    // ---- Windows Energiemodus (Overlay) und Energiepläne ----
    static class WinPower
    {
        public static readonly Guid Efficiency = new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a");
        public static readonly Guid Balanced = Guid.Empty;
        public static readonly Guid BestPerf = new Guid("ded574b5-45a0-4f42-8737-46345c09c238");
        public static readonly Guid SubProcessor = new Guid("54533251-82be-4824-96c1-47b60b740d00");
        public static readonly Guid SetBoost = new Guid("be337238-0d82-4146-a960-4f3749d470c7");
        public static readonly Guid SetMaxState = new Guid("bc5038f7-23e0-4960-96da-33abaf5935ec");

        [DllImport("powrprof.dll")] static extern uint PowerGetEffectiveOverlayScheme(out Guid g);
        [DllImport("powrprof.dll")] static extern uint PowerSetActiveOverlayScheme(Guid g);
        [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr guid);
        [DllImport("powrprof.dll")] static extern uint PowerSetActiveScheme(IntPtr root, ref Guid g);
        [DllImport("powrprof.dll")] static extern uint PowerEnumerate(IntPtr root, IntPtr scheme, IntPtr sub, uint flags, uint index, byte[] buf, ref uint size);
        [DllImport("powrprof.dll")] static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr sub, IntPtr setting, byte[] buf, ref uint size);
        [DllImport("powrprof.dll")] static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
        [DllImport("powrprof.dll")] static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
        [DllImport("powrprof.dll")] static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
        [DllImport("powrprof.dll")] static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
        [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);

        public static Guid GetOverlay() { Guid g; PowerGetEffectiveOverlayScheme(out g); return g; }
        public static void SetOverlay(Guid g)
        {
            uint rc = PowerSetActiveOverlayScheme(g);
            if (rc != 0) throw new Win32Exception((int)rc);
        }
        public static string OverlayName(Guid g)
        {
            if (g == Efficiency) return "Beste Energieeffizienz";
            if (g == BestPerf) return "Beste Leistung";
            if (g == Balanced || g == new Guid("381b4222-f694-41f0-9685-ff5bb260df2e")) return "Ausgeglichen";
            if (g == new Guid("3af9b8d9-7c97-431d-ad78-34a8bfea439f")) return "Bessere Leistung";
            return g.ToString();
        }

        public static Guid ActiveScheme()
        {
            IntPtr p;
            PowerGetActiveScheme(IntPtr.Zero, out p);
            Guid g = (Guid)Marshal.PtrToStructure(p, typeof(Guid));
            LocalFree(p);
            return g;
        }
        public static void SetActiveScheme(Guid g)
        {
            uint rc = PowerSetActiveScheme(IntPtr.Zero, ref g);
            if (rc != 0) throw new Win32Exception((int)rc);
        }
        public static List<KeyValuePair<Guid, string>> Schemes()
        {
            var list = new List<KeyValuePair<Guid, string>>();
            for (uint i = 0; ; i++)
            {
                byte[] b = new byte[16]; uint sz = 16;
                if (PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 16, i, b, ref sz) != 0) break;
                Guid g = new Guid(b);
                list.Add(new KeyValuePair<Guid, string>(g, SchemeName(g)));
            }
            return list;
        }
        public static string SchemeName(Guid g)
        {
            uint sz = 0;
            PowerReadFriendlyName(IntPtr.Zero, ref g, IntPtr.Zero, IntPtr.Zero, null, ref sz);
            if (sz == 0) return g.ToString();
            byte[] b = new byte[sz];
            PowerReadFriendlyName(IntPtr.Zero, ref g, IntPtr.Zero, IntPtr.Zero, b, ref sz);
            return Encoding.Unicode.GetString(b).TrimEnd('\0');
        }

        public static uint ReadValue(Guid setting, bool ac)
        {
            Guid s = ActiveScheme(), sub = SubProcessor; uint v;
            uint rc = ac ? PowerReadACValueIndex(IntPtr.Zero, ref s, ref sub, ref setting, out v)
                         : PowerReadDCValueIndex(IntPtr.Zero, ref s, ref sub, ref setting, out v);
            if (rc != 0) throw new Win32Exception((int)rc);
            return v;
        }
        public static void WriteValue(Guid setting, bool ac, uint value)
        {
            Guid s = ActiveScheme(), sub = SubProcessor;
            uint rc = ac ? PowerWriteACValueIndex(IntPtr.Zero, ref s, ref sub, ref setting, value)
                         : PowerWriteDCValueIndex(IntPtr.Zero, ref s, ref sub, ref setting, value);
            if (rc != 0) throw new Win32Exception((int)rc);
            PowerSetActiveScheme(IntPtr.Zero, ref s); // übernehmen
        }
    }

    // ---- Akku über IOCTL_BATTERY_* (ohne WMI, ohne Admin) ----
    class BatteryInfo
    {
        public bool Present;
        public uint Designed, FullCharged, Remaining, CycleCount, Voltage;
        public int Rate; // mW, negativ = Entladen
        public uint PowerState; // 1=online, 2=discharging, 4=charging, 8=critical
        public string Name = "", Maker = "";
        public double TempC = double.NaN;
        public bool OnAc { get { return (PowerState & 1) != 0; } }
        public bool Charging { get { return (PowerState & 4) != 0; } }
        public bool Discharging { get { return (PowerState & 2) != 0; } }
        public double Percent { get { return FullCharged > 0 ? 100.0 * Remaining / FullCharged : 0; } }
        public double Health { get { return Designed > 0 ? 100.0 * FullCharged / Designed : 0; } }

        static Guid GUID_BATTERY = new Guid("72631e54-78a4-11d0-bcf7-00aa00b7b32a");

        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid g; public int flags; public IntPtr reserved; }
        [DllImport("setupapi.dll", SetLastError = true)]
        static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr enumr, IntPtr hwnd, int flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid g, int idx, ref SP_DEVICE_INTERFACE_DATA did);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA did, IntPtr detail, int size, out int req, IntPtr devInfo);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sa, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool DeviceIoControl(IntPtr h, uint code, byte[] inBuf, int inLen, byte[] outBuf, int outLen, out int ret, IntPtr ov);

        static byte[] Ioctl(IntPtr h, uint code, byte[] input, int outLen)
        {
            byte[] o = new byte[outLen]; int r;
            if (!DeviceIoControl(h, code, input, input == null ? 0 : input.Length, o, outLen, out r, IntPtr.Zero)) return null;
            Array.Resize(ref o, r);
            return o;
        }

        static byte[] QueryInfo(IntPtr h, uint tag, int level, int outLen)
        {
            byte[] q = new byte[12];
            BitConverter.GetBytes(tag).CopyTo(q, 0);
            BitConverter.GetBytes(level).CopyTo(q, 4);
            return Ioctl(h, 0x294044, q, outLen);
        }

        public static BatteryInfo Read()
        {
            var bi = new BatteryInfo();
            IntPtr set = SetupDiGetClassDevs(ref GUID_BATTERY, IntPtr.Zero, IntPtr.Zero, 0x12);
            if (set == new IntPtr(-1)) return bi;
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    var did = new SP_DEVICE_INTERFACE_DATA(); did.cbSize = Marshal.SizeOf(did);
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref GUID_BATTERY, i, ref did)) break;
                    int req;
                    SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, out req, IntPtr.Zero);
                    IntPtr det = Marshal.AllocHGlobal(req);
                    try
                    {
                        Marshal.WriteInt32(det, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref did, det, req, out req, IntPtr.Zero)) continue;
                        string path = Marshal.PtrToStringUni(new IntPtr(det.ToInt64() + 4));
                        IntPtr h = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0x80, IntPtr.Zero);
                        if (h == new IntPtr(-1)) continue;
                        try { ReadOne(h, bi); } finally { EnergyDrv.CloseHandle(h); }
                    }
                    finally { Marshal.FreeHGlobal(det); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return bi;
        }

        static void ReadOne(IntPtr h, BatteryInfo bi)
        {
            byte[] t = Ioctl(h, 0x294040, BitConverter.GetBytes(0u), 4);
            if (t == null || t.Length < 4) return;
            uint tag = BitConverter.ToUInt32(t, 0);
            if (tag == 0) return;
            bi.Present = true;
            byte[] info = QueryInfo(h, tag, 0, 36); // BATTERY_INFORMATION
            if (info != null && info.Length >= 36)
            {
                bi.Designed += BitConverter.ToUInt32(info, 12);
                bi.FullCharged += BitConverter.ToUInt32(info, 16);
                bi.CycleCount = Math.Max(bi.CycleCount, BitConverter.ToUInt32(info, 32));
            }
            byte[] tmp = QueryInfo(h, tag, 2, 4);
            if (tmp != null && tmp.Length >= 4) { uint k = BitConverter.ToUInt32(tmp, 0); if (k > 2000) bi.TempC = k / 10.0 - 273.15; }
            byte[] nm = QueryInfo(h, tag, 4, 256);
            if (nm != null) bi.Name = Encoding.Unicode.GetString(nm).TrimEnd('\0');
            byte[] mk = QueryInfo(h, tag, 6, 256);
            if (mk != null) bi.Maker = Encoding.Unicode.GetString(mk).TrimEnd('\0');
            byte[] ws = new byte[20];
            BitConverter.GetBytes(tag).CopyTo(ws, 0);
            byte[] st = Ioctl(h, 0x29404C, ws, 16);
            if (st != null && st.Length >= 16)
            {
                bi.PowerState = BitConverter.ToUInt32(st, 0);
                bi.Remaining += BitConverter.ToUInt32(st, 4);
                bi.Voltage = BitConverter.ToUInt32(st, 8);
                int rate = BitConverter.ToInt32(st, 12);
                if (rate != int.MinValue) bi.Rate += rate;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SYSTEM_POWER_STATUS { public byte AC, Flag, Percent, Saver; public int LifeTime, FullLifeTime; }
        [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s);
        public static int SecondsLeft() { SYSTEM_POWER_STATUS s; GetSystemPowerStatus(out s); return s.LifeTime; }
        public static bool SaverOn() { SYSTEM_POWER_STATUS s; GetSystemPowerStatus(out s); return (s.Saver & 1) != 0; }
    }

    // ---- PDH mit englischen Zählernamen (sprachunabhängig) ----
    class Pdh : IDisposable
    {
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhOpenQueryW(string src, IntPtr user, out IntPtr q);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhAddEnglishCounterW(IntPtr q, string path, IntPtr user, out IntPtr c);
        [DllImport("pdh.dll")] static extern uint PdhCollectQueryData(IntPtr q);
        [DllImport("pdh.dll")] static extern uint PdhCloseQuery(IntPtr q);
        [DllImport("pdh.dll")] static extern uint PdhGetFormattedCounterValue(IntPtr c, uint fmt, IntPtr type, out FMT v);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhGetFormattedCounterArrayW(IntPtr c, uint fmt, ref uint size, ref uint count, IntPtr buf);
        [StructLayout(LayoutKind.Explicit)] struct FMT { [FieldOffset(0)] public uint Status; [FieldOffset(8)] public double Value; }
        const uint FMT_DOUBLE_NOCAP = 0x200 | 0x8000;

        IntPtr q;
        Dictionary<string, IntPtr> counters = new Dictionary<string, IntPtr>();

        public Pdh() { PdhOpenQueryW(null, IntPtr.Zero, out q); }
        public void Add(string key, string path)
        {
            IntPtr c;
            if (PdhAddEnglishCounterW(q, path, IntPtr.Zero, out c) == 0) counters[key] = c;
        }
        public void Collect() { PdhCollectQueryData(q); }
        public double Value(string key)
        {
            IntPtr c; FMT v;
            if (!counters.TryGetValue(key, out c)) return double.NaN;
            if (PdhGetFormattedCounterValue(c, FMT_DOUBLE_NOCAP, IntPtr.Zero, out v) != 0 || v.Status > 1) return double.NaN;
            return v.Value;
        }
        public Dictionary<string, double> Array(string key)
        {
            var res = new Dictionary<string, double>();
            IntPtr c;
            if (!counters.TryGetValue(key, out c)) return res;
            uint size = 0, count = 0;
            PdhGetFormattedCounterArrayW(c, FMT_DOUBLE_NOCAP, ref size, ref count, IntPtr.Zero);
            if (size == 0) return res;
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArrayW(c, FMT_DOUBLE_NOCAP, ref size, ref count, buf) != 0) return res;
                int item = IntPtr.Size + 16;
                for (int i = 0; i < count; i++)
                {
                    IntPtr p = new IntPtr(buf.ToInt64() + i * item);
                    string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(p));
                    uint st = (uint)Marshal.ReadInt32(p, IntPtr.Size);
                    double val = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(p, IntPtr.Size + 8));
                    if (st <= 1 && name != null) res[name] = val;
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
            return res;
        }
        public void Dispose() { if (q != IntPtr.Zero) { PdhCloseQuery(q); q = IntPtr.Zero; } }
    }

    // ---- Arbeitsspeicher & Commit ----
    static class Mem
    {
        [StructLayout(LayoutKind.Sequential)]
        class MEMORYSTATUSEX
        {
            public uint dwLength = 64, dwMemoryLoad;
            public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
        }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX m);

        // Werte in GB: physisch gesamt/belegt, Commit belegt/Limit
        public static bool Read(out double totalGB, out double usedGB, out double commitGB, out double commitLimitGB)
        {
            var m = new MEMORYSTATUSEX();
            totalGB = usedGB = commitGB = commitLimitGB = double.NaN;
            if (!GlobalMemoryStatusEx(m)) return false;
            const double G = 1024.0 * 1024 * 1024;
            totalGB = m.TotalPhys / G; usedGB = (m.TotalPhys - m.AvailPhys) / G;
            commitLimitGB = m.TotalPageFile / G; commitGB = (m.TotalPageFile - m.AvailPageFile) / G;
            return true;
        }
    }

    // ---- Prozess-Snapshot via NtQuerySystemInformation (sieht alle Prozesse, ohne Admin) ----
    class ProcSample
    {
        public int Pid; public string Name; public long CpuTime; public long PrivateBytes;
    }

    static class Procs
    {
        [DllImport("ntdll.dll")] static extern int NtQuerySystemInformation(int cls, IntPtr buf, int len, out int ret);

        public static Dictionary<int, ProcSample> Snapshot()
        {
            int len = 1 << 20, ret;
            IntPtr buf = IntPtr.Zero;
            try
            {
                while (true)
                {
                    buf = Marshal.AllocHGlobal(len);
                    int st = NtQuerySystemInformation(5, buf, len, out ret);
                    if (st == unchecked((int)0xC0000004)) { Marshal.FreeHGlobal(buf); buf = IntPtr.Zero; len = Math.Max(len * 2, ret + 65536); continue; }
                    if (st != 0) throw new Win32Exception(st);
                    break;
                }
                var res = new Dictionary<int, ProcSample>();
                long off = 0;
                while (true)
                {
                    IntPtr p = new IntPtr(buf.ToInt64() + off);
                    int next = Marshal.ReadInt32(p, 0);
                    var s = new ProcSample();
                    s.PrivateBytes = Marshal.ReadInt64(p, 0x08);
                    s.CpuTime = Marshal.ReadInt64(p, 0x28) + Marshal.ReadInt64(p, 0x30);
                    int nlen = Marshal.ReadInt16(p, 0x38);
                    IntPtr nbuf = Marshal.ReadIntPtr(p, 0x40);
                    s.Pid = (int)Marshal.ReadIntPtr(p, 0x50).ToInt64();
                    s.Name = nbuf == IntPtr.Zero ? (s.Pid == 0 ? "Idle" : "?") : Marshal.PtrToStringUni(nbuf, nlen / 2);
                    res[s.Pid] = s;
                    if (next == 0) break;
                    off += next;
                }
                return res;
            }
            finally { if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf); }
        }
    }

    // ---- Display: Bildwiederholrate des internen Panels ----
    static class Display
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string dev, int mode, ref DEVMODE dm);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettingsEx(string dev, ref DEVMODE dm, IntPtr hwnd, int flags, IntPtr param);
        [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
        [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint np, byte[] paths, ref uint nm, byte[] modes, IntPtr topo);
        [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(byte[] packet);

        // GDI-Name ("\\.\DISPLAY1") des internen Panels; null wenn keins aktiv.
        public static string InternalDevice()
        {
            uint np, nm;
            if (GetDisplayConfigBufferSizes(2, out np, out nm) != 0) return null;
            byte[] paths = new byte[np * 72], modes = new byte[nm * 64];
            if (QueryDisplayConfig(2, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return null;
            string first = null;
            for (int i = 0; i < np; i++)
            {
                int o = i * 72;
                uint tech = BitConverter.ToUInt32(paths, o + 20 + 16);
                byte[] pk = new byte[84];
                BitConverter.GetBytes(1).CopyTo(pk, 0);       // GET_SOURCE_NAME
                BitConverter.GetBytes(84).CopyTo(pk, 4);
                System.Array.Copy(paths, o, pk, 8, 8);         // adapter LUID
                System.Array.Copy(paths, o + 8, pk, 16, 4);    // source id
                if (DisplayConfigGetDeviceInfo(pk) != 0) continue;
                string name = Encoding.Unicode.GetString(pk, 20, 64).TrimEnd('\0');
                if (first == null) first = name;
                if (tech == 0x80000000 || tech == 11 || tech == 13) return name; // INTERNAL / eDP / UDI embedded
            }
            return first;
        }

        public static DEVMODE Current(string dev)
        {
            var dm = new DEVMODE(); dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            EnumDisplaySettings(dev, -1, ref dm);
            return dm;
        }

        public static List<int> Rates(string dev)
        {
            var cur = Current(dev);
            var set = new SortedSet<int>();
            for (int i = 0; ; i++)
            {
                var dm = new DEVMODE(); dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                if (!EnumDisplaySettings(dev, i, ref dm)) break;
                if (dm.dmPelsWidth == cur.dmPelsWidth && dm.dmPelsHeight == cur.dmPelsHeight && dm.dmDisplayFrequency > 1) set.Add(dm.dmDisplayFrequency);
            }
            return new List<int>(set);
        }

        public static void SetRate(string dev, int hz)
        {
            var dm = Current(dev);
            dm.dmDisplayFrequency = hz;
            dm.dmFields = 0x400000; // DM_DISPLAYFREQUENCY
            int rc = ChangeDisplaySettingsEx(dev, ref dm, IntPtr.Zero, 1, IntPtr.Zero);
            if (rc != 0) throw new Exception("Bildwiederholrate " + hz + " Hz abgelehnt (Code " + rc + ")");
        }

        [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
        public static bool TabletPosture() { return GetSystemMetrics(0x2003) == 0; }
    }

    // ---- Eingabegeräte (Touchscreen/Stift/Touchpad) über SetupAPI ----
    class HidDev
    {
        public string Kind, Name, InstanceId; public bool Enabled; public bool Found;
    }

    static class Hid
    {
        static Guid HIDCLASS = new Guid("745a17a0-74d3-11d0-b6fe-00a0c90f57da");
        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVINFO_DATA { public int cbSize; public Guid g; public int devInst; public IntPtr reserved; }
        [DllImport("setupapi.dll", SetLastError = true)] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr enumr, IntPtr hwnd, int flags);
        [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInfo(IntPtr set, int idx, ref SP_DEVINFO_DATA d);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceRegistryProperty(IntPtr set, ref SP_DEVINFO_DATA d, int prop, out int type, byte[] buf, int size, out int req);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref SP_DEVINFO_DATA d, StringBuilder sb, int size, out int req);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("cfgmgr32.dll")] static extern int CM_Get_DevNode_Status(out int status, out int problem, int devInst, int flags);

        static string Prop(IntPtr set, ref SP_DEVINFO_DATA d, int prop)
        {
            byte[] b = new byte[2048]; int t, r;
            if (!SetupDiGetDeviceRegistryProperty(set, ref d, prop, out t, b, b.Length, out r)) return "";
            return Encoding.Unicode.GetString(b, 0, r).Replace('\0', ';');
        }

        // usage: 0004 Touchscreen, 0002 Stift, 0005 Touchpad. Nur interne (I2C-)Geräte, keine USB-Empfänger.
        public static HidDev Find(string kind)
        {
            string usage = kind == "touch" ? "UP:000D_U:0004" : kind == "pen" ? "UP:000D_U:0002" : "UP:000D_U:0005";
            var res = new HidDev(); res.Kind = kind;
            IntPtr set = SetupDiGetClassDevs(ref HIDCLASS, IntPtr.Zero, IntPtr.Zero, 0x2);
            if (set == new IntPtr(-1)) return res;
            try
            {
                for (int i = 0; ; i++)
                {
                    var d = new SP_DEVINFO_DATA(); d.cbSize = Marshal.SizeOf(d);
                    if (!SetupDiEnumDeviceInfo(set, i, ref d)) break;
                    string hw = Prop(set, ref d, 1);
                    if (hw.IndexOf(usage, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var sb = new StringBuilder(512); int req;
                    SetupDiGetDeviceInstanceId(set, ref d, sb, sb.Capacity, out req);
                    string id = sb.ToString();
                    if (id.IndexOf("VID_", StringComparison.OrdinalIgnoreCase) >= 0 && id.IndexOf("PID_", StringComparison.OrdinalIgnoreCase) >= 0) continue; // USB
                    int st, prob;
                    CM_Get_DevNode_Status(out st, out prob, d.devInst, 0);
                    res.Found = true; res.InstanceId = id;
                    res.Name = Prop(set, ref d, 0).TrimEnd(';');
                    res.Enabled = prob != 22; // CM_PROB_DISABLED
                    return res;
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return res;
        }
    }
}
