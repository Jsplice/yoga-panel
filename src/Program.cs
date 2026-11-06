// yoga.exe – schlankes Steuer- und Statistik-Tool für Lenovo Yoga (getestet: Yoga 7 2-in-1 16ILL10).
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace YogaTool
{
    static class C
    {
        public static bool On = true;
        static string W(string code, string s) { return On ? "\x1b[" + code + "m" + s + "\x1b[0m" : s; }
        public static string Dim(string s) { return W("90", s); }
        public static string Bold(string s) { return W("1", s); }
        public static string Green(string s) { return W("32", s); }
        public static string Yellow(string s) { return W("33", s); }
        public static string Red(string s) { return W("31", s); }
        public static string Cyan(string s) { return W("36", s); }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int n);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool GetConsoleMode(IntPtr h, out int m);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool SetConsoleMode(IntPtr h, int m);
        public static void Init()
        {
            if (Console.IsOutputRedirected) { On = false; return; }
            IntPtr h = GetStdHandle(-11); int m;
            if (!GetConsoleMode(h, out m) || !SetConsoleMode(h, m | 4)) On = false;
        }
    }

    class Program
    {
        static readonly CultureInfo DE = CultureInfo.GetCultureInfo("de-DE");
        static string F(double v, int d) { return double.IsNaN(v) ? "–" : v.ToString("N" + d, DE); }

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            C.Init();
            string elevOut = null;
            if (args.Length >= 2 && args[0] == "--elev-out")
            {
                elevOut = args[1];
                args = args.Skip(2).ToArray();
                var sw = new StreamWriter(elevOut, false, new UTF8Encoding(false)); sw.AutoFlush = true;
                Console.SetOut(sw); Console.SetError(sw); C.On = false;
            }
            try { return Run(args); }
            catch (NeedAdmin) { return Elevate(args); }
            catch (Exception e) { Console.Error.WriteLine(C.Red("Fehler: ") + e.Message); return 1; }
            finally { if (elevOut != null) Console.Out.Flush(); }
        }

        class NeedAdmin : Exception { }

        static bool IsAdmin() { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
        static void RequireAdmin() { if (!IsAdmin()) throw new NeedAdmin(); }

        // Startet dieselbe Aktion mit Admin-Rechten (UAC) und gibt deren Ausgabe hier aus.
        static int Elevate(string[] args)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "yoga-elev-" + Guid.NewGuid().ToString("N") + ".txt");
            var psi = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,
                "--elev-out " + Quote(tmp) + " " + string.Join(" ", args.Select(Quote)));
            psi.Verb = "runas"; psi.UseShellExecute = true; psi.WindowStyle = ProcessWindowStyle.Hidden;
            Console.WriteLine(C.Dim("Admin-Rechte nötig – bitte UAC bestätigen …"));
            try
            {
                var p = Process.Start(psi); p.WaitForExit();
                if (File.Exists(tmp)) { Console.Write(File.ReadAllText(tmp, Encoding.UTF8)); File.Delete(tmp); }
                return p.ExitCode;
            }
            catch (Win32Exception e) { if (e.NativeErrorCode == 1223) { Console.WriteLine("Abgebrochen."); return 2; } throw; }
        }
        static string Quote(string s) { return s.Length > 0 && s.IndexOfAny(new[] { ' ', '"', '\t' }) < 0 ? s : "\"" + s.Replace("\"", "\\\"") + "\""; }

        static int Run(string[] a)
        {
            string cmd = a.Length > 0 ? a[0].ToLowerInvariant() : "status";
            string[] rest = a.Skip(1).ToArray();
            string arg = rest.Length > 0 ? rest[0].ToLowerInvariant() : null;
            switch (cmd)
            {
                case "status": case "s": Status(); return 0;
                case "live": case "watch": case "w": Live(arg); return 0;
                case "top": Top(arg); return 0;
                case "apps": case "energie": Apps(rest); return 0;
                case "akku": case "battery": case "bat": Battery(rest); return 0;
                case "modus": case "mode": case "m": Mode(rest); return 0;
                case "winmodus": case "winmode": WinMode(arg); return 0;
                case "laden": case "charge": case "c": Charge(arg); return 0;
                case "boost": Boost(rest); return 0;
                case "cpumax": CpuMax(rest); return 0;
                case "plan": Plan(rest); return 0;
                case "touch": case "touchscreen": Device("touch", arg); return 0;
                case "stift": case "pen": Device("pen", arg); return 0;
                case "touchpad": case "tp": Device("touchpad", arg); return 0;
                case "fnlock": case "fn": FnLock(arg); return 0;
                case "usb": Usb(arg); return 0;
                case "tastatur": case "kbd": case "licht": Kbd(arg); return 0;
                case "hz": Hz(arg); return 0;
                case "hell": case "brightness": Bright(arg); return 0;
                case "hilfe": case "help": case "-h": case "--help": case "/?": Help(); return 0;
                default: Console.WriteLine("Unbekannter Befehl: " + cmd); Help(); return 1;
            }
        }

        static void Help()
        {
            Console.WriteLine(C.Bold("yoga") + " – Lenovo Yoga Steuerung & Statistik\n");
            string[][] rows = {
                new[]{"Übersicht", ""},
                new[]{"yoga [status]", "Alles auf einen Blick"},
                new[]{"yoga live [sek]", "Live-Monitor: Watt, CPU/GPU, Top-Programme, Hotkeys"},
                new[]{"yoga top [anzahl]", "Programme nach CPU/GPU/RAM (1 s Messung)"},
                new[]{"yoga apps [stunden] [--akku]", "Energieverbrauch pro App laut Windows (Admin)"},
                new[]{"yoga akku [--report]", "Akku-Details, Gesundheit, Zyklen"},
                new[]{"Energie", ""},
                new[]{"yoga modus spar|auto|max", "Lenovo-Leistungsmodus + Windows-Energiemodus"},
                new[]{"yoga winmodus effizienz|ausgeglichen|leistung", "Nur den Windows-Energiemodus"},
                new[]{"yoga laden normal|schonen|schnell", "Lademodus (schonen = Erhaltungsmodus ~80 %)"},
                new[]{"yoga boost an|aus|effizient [--ac|--dc]", "CPU-Turbo"},
                new[]{"yoga cpumax 5-100 [--ac|--dc]", "Max. Prozessorleistung in %"},
                new[]{"yoga plan [name|nr]", "Windows-Energiepläne anzeigen/wechseln"},
                new[]{"Yoga-Hardware", ""},
                new[]{"yoga touch an|aus", "Touchscreen (Admin)"},
                new[]{"yoga stift an|aus", "Stift-Eingabe (Admin)"},
                new[]{"yoga touchpad an|aus", "Touchpad (Admin)"},
                new[]{"yoga fnlock an|aus", "Fn-Lock"},
                new[]{"yoga tastatur aus|niedrig|hoch|auto", "Tastaturbeleuchtung"},
                new[]{"yoga usb an|aus", "USB-Laden, wenn das Gerät aus ist/schläft"},
                new[]{"yoga hz [60|120|min|max]", "Bildwiederholrate internes Display"},
                new[]{"yoga hell [0-100]", "Bildschirmhelligkeit"},
            };
            foreach (var r in rows)
            {
                if (r[1] == "") Console.WriteLine("\n " + C.Cyan(r[0]));
                else Console.WriteLine("  " + r[0].PadRight(46) + C.Dim(r[1]));
            }
            Console.WriteLine();
        }

        // ------------------------------------------------------------------ Status
        static string ModeName(PerfMode m)
        {
            switch (m)
            {
                case PerfMode.Quiet: return "Akku sparen";
                case PerfMode.Balanced: return "Intelligent (ausgeglichen)";
                case PerfMode.Performance: return "Extreme Leistung";
                default: return "unbekannt";
            }
        }
        static string ChargeName(ChargeMode m)
        {
            switch (m)
            {
                case ChargeMode.Normal: return "Normal";
                case ChargeMode.Conservation: return "Schonen (Erhaltungsmodus ~80 %)";
                case ChargeMode.Rapid: return "Schnellladen";
                default: return "unbekannt";
            }
        }
        static string OnOff(bool b) { return b ? C.Green("an") : C.Yellow("aus"); }
        static T Try<T>(Func<T> f, T def) { try { return f(); } catch { return def; } }

        static void Line(string label, string value) { Console.WriteLine(" " + C.Dim(label.PadRight(13)) + value); }

        static string Model()
        {
            using (var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS"))
            {
                if (k == null) return "?";
                return k.GetValue("SystemFamily", "") + " (" + k.GetValue("SystemProductName", "") + ") · BIOS " + k.GetValue("BIOSVersion", "");
            }
        }

        static string BatteryLine(BatteryInfo b)
        {
            if (!b.Present) return "kein Akku";
            string state = b.Charging ? C.Green("lädt " + F(b.Rate / 1000.0, 1) + " W")
                : b.Discharging ? C.Yellow("entlädt " + F(-b.Rate / 1000.0, 1) + " W")
                : (b.OnAc ? "Netzbetrieb, lädt nicht" : "–");
            string left = "";
            if (b.Discharging && b.Rate < 0) left = " · noch ~" + Dur(b.Remaining * 3600.0 / -b.Rate);
            else if (b.Charging && b.Rate > 0 && b.FullCharged > b.Remaining) left = " · voll in ~" + Dur((b.FullCharged - b.Remaining) * 3600.0 / b.Rate);
            return C.Bold(F(b.Percent, 0) + " %") + " · " + state + left;
        }
        static string Dur(double sec)
        {
            if (sec <= 0 || double.IsInfinity(sec) || double.IsNaN(sec)) return "?";
            var t = TimeSpan.FromSeconds(sec);
            return (int)t.TotalHours + ":" + t.Minutes.ToString("00") + " h";
        }

        static void Status()
        {
            bool drv = EnergyDrv.Available();
            Console.WriteLine(C.Bold(" " + Model()));
            var b = BatteryInfo.Read();
            Line("Akku", BatteryLine(b));
            if (b.Present)
                Line("Gesundheit", F(b.FullCharged / 1000.0, 1) + " / " + F(b.Designed / 1000.0, 1) + " Wh (" + F(b.Health, 0) + " %) · " + b.CycleCount + " Zyklen");
            if (drv)
            {
                Line("Lademodus", Try(() => ChargeName(Lenovo.GetChargeMode()), "?"));
                Line("Lenovo-Modus", C.Bold(Try(() => ModeName(Lenovo.GetPerfMode()), "?")) + C.Dim("   (Fn+Q / yoga modus)"));
            }
            else Line("Lenovo", C.Red("EnergyDrv-Treiber fehlt – Lenovo-Funktionen nicht verfügbar"));
            Line("Windows", WinPower.OverlayName(WinPower.GetOverlay()) + " · Plan " + WinPower.SchemeName(WinPower.ActiveScheme())
                + (BatteryInfo.SaverOn() ? " · " + C.Yellow("Energiesparen aktiv") : ""));
            Line("CPU-Turbo", "Netz " + BoostName(Try(() => (int)WinPower.ReadValue(WinPower.SetBoost, true), -1))
                + " · Akku " + BoostName(Try(() => (int)WinPower.ReadValue(WinPower.SetBoost, false), -1)));

            // 0,5 s Messung für Watt/CPU
            using (var pdh = new Pdh())
            {
                pdh.Add("util", @"\Processor Information(_Total)\% Processor Utility");
                pdh.Add("freq", @"\Processor Information(_Total)\Processor Frequency");
                pdh.Add("perf", @"\Processor Information(_Total)\% Processor Performance");
                pdh.Add("rapl", @"\Energy Meter(*)\Power");
                pdh.Add("temp", @"\Thermal Zone Information(*)\High Precision Temperature");
                pdh.Add("pf", @"\Paging File(_Total)\% Usage");
                pdh.Add("pages", @"\Memory\Pages/sec");
                pdh.Collect(); Thread.Sleep(500); pdh.Collect();
                var r = pdh.Array("rapl");
                if (r.Count > 0) Line("Leistung", RaplText(r));
                Line("CPU", F(Math.Min(100, pdh.Value("util")), 0) + " % · " + F(pdh.Value("freq") * pdh.Value("perf") / 100 / 1000, 1) + " GHz · " + TempText(pdh.Array("temp")));
                double mt, mu, cu, cl;
                if (Mem.Read(out mt, out mu, out cu, out cl)) Line("Speicher", MemText(mu, mt, cu, cl, pdh.Value("pf"), pdh.Value("pages")));
            }

            string dev = Display.InternalDevice();
            if (dev != null)
            {
                var dm = Display.Current(dev);
                Line("Display", dm.dmPelsWidth + "×" + dm.dmPelsHeight + " · " + C.Bold(dm.dmDisplayFrequency + " Hz") + " · Helligkeit " + Try(() => GetBrightness() + " %", "?"));
            }
            if (drv)
            {
                int kt = Try(() => Lenovo.KbdType(), 0);
                string kb = kt > 0 ? Lenovo.KbdName(kt, Try(() => Lenovo.GetKbd(kt), -1)) : "–";
                Line("Tastatur", "Fn-Lock " + Try(() => OnOff(Lenovo.GetFnLock()), "?") + " · Beleuchtung " + kb);
                Line("USB-Laden", "im Aus/Standby " + Try(() => OnOff(Lenovo.GetUsbCharge()), "?"));
            }
            Line("Eingabe", DevText("touch", "Touchscreen") + " · " + DevText("pen", "Stift") + " · " + DevText("touchpad", "Touchpad"));
            Line("Haltung", Display.TabletPosture() ? "Tablet" : "Laptop");
        }

        static string DevText(string kind, string label)
        {
            var d = Hid.Find(kind);
            return label + " " + (d.Found ? OnOff(d.Enabled) : C.Dim("–"));
        }

        static string RaplText(Dictionary<string, double> r)
        {
            Func<string, double> g = k => { double v; return r.TryGetValue("RAPL_Package0_" + k, out v) ? v / 1000 : double.NaN; };
            return "SoC " + C.Bold(F(g("PKG"), 1) + " W") + C.Dim(" (Kerne " + F(g("PP0"), 1) + " · GPU " + F(g("PP1"), 1) + " · RAM " + F(g("DRAM"), 1) + ")");
        }
        static string MemText(double used, double total, double commit, double limit, double pfPct, double pages)
        {
            double pct = used / total * 100;
            string u = F(used, 1) + " / " + F(total, 1) + " GB (" + F(pct, 0) + " %)";
            return (pct >= 90 ? C.Red(u) : pct >= 80 ? C.Yellow(u) : C.Bold(u))
                + C.Dim(" · Commit " + F(commit, 1) + "/" + F(limit, 0) + " GB · Auslagerung " + F(pfPct, 0) + " % · Paging " + F(pages, 0) + " Seiten/s");
        }

        static string TempText(Dictionary<string, double> t)
        {
            if (t.Count == 0) return "–";
            double max = t.Values.Max() / 10 - 273.15;
            string s = F(max, 0) + " °C";
            return max >= 90 ? C.Red(s) : max >= 75 ? C.Yellow(s) : s;
        }

        // ------------------------------------------------------------------ Prozess-Messung

        static string Bar(double pct, int w)
        {
            int n = (int)Math.Round(Math.Max(0, Math.Min(100, pct)) / 100 * w);
            string s = new string('█', n) + new string('░', w - n);
            return pct >= 80 ? C.Red(s) : pct >= 50 ? C.Yellow(s) : C.Green(s);
        }

        static string ProcHeader() { return C.Dim(" " + "Programm".PadRight(28) + "Anz".PadLeft(4) + "CPU %".PadLeft(8) + "GPU %".PadLeft(8) + "RAM MB".PadLeft(9) + "≈ Watt".PadLeft(8)); }
        static string ProcLine(ProcRow r)
        {
            string name = r.Name.Length > 27 ? r.Name.Substring(0, 26) + "…" : r.Name;
            return " " + name.PadRight(28) + r.Count.ToString().PadLeft(4) + F(r.Cpu, 1).PadLeft(8) + F(r.Gpu, 1).PadLeft(8)
                + r.MemMB.ToString("N0", DE).PadLeft(9) + F(r.Watt, 2).PadLeft(8);
        }

        static void Top(string arg)
        {
            int n = 15; if (arg != null) int.TryParse(arg, out n);
            using (var s = new Sampler())
            {
                Thread.Sleep(1000); s.Sample();
                Console.WriteLine(" CPU " + F(s.CpuUtil, 0) + " % · GPU " + F(s.GpuTotal, 0) + " % · " + (s.Rapl.Count > 0 ? RaplText(s.Rapl) : ""));
                Console.WriteLine(ProcHeader());
                foreach (var r in s.Rows.OrderByDescending(x => x.Cpu + x.Gpu).ThenByDescending(x => x.MemMB).Take(n)) Console.WriteLine(ProcLine(r));
            }
        }

        // ------------------------------------------------------------------ Live-Monitor
        static void Live(string arg)
        {
            if (Console.IsOutputRedirected) { Top(null); return; }
            double interval = 1.5;
            if (arg != null) double.TryParse(arg.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out interval);
            interval = Math.Max(0.5, interval);
            bool drv = EnergyDrv.Available();
            int sort = 0; // 0 Watt, 1 CPU, 2 RAM
            string msg = "";
            Console.CursorVisible = false;
            Console.CancelKeyPress += (o, e) => { Console.CursorVisible = true; Console.Write("\x1b[0m"); };
            Console.Clear();
            try
            {
                using (var s = new Sampler())
                {
                    while (true)
                    {
                        // warten, dabei Tasten verarbeiten
                        var until = DateTime.UtcNow.AddSeconds(interval);
                        bool quit = false, redraw = false;
                        while (DateTime.UtcNow < until && !redraw)
                        {
                            while (Console.KeyAvailable)
                            {
                                var k = Console.ReadKey(true);
                                char ch = char.ToLowerInvariant(k.KeyChar);
                                if (ch == 'q' || k.Key == ConsoleKey.Escape) { quit = true; break; }
                                msg = LiveKey(ch, drv, ref sort);
                                redraw = msg != null;
                                if (msg == null) msg = "";
                            }
                            if (quit) break;
                            Thread.Sleep(40);
                        }
                        if (quit) break;
                        s.Sample();
                        DrawLive(s, drv, sort, msg);
                    }
                }
            }
            finally { Console.CursorVisible = true; Console.Clear(); }
        }

        static string LiveKey(char ch, bool drv, ref int sort)
        {
            try
            {
                switch (ch)
                {
                    case '1': SetMode(PerfMode.Quiet, true); return "Modus: Akku sparen";
                    case '2': SetMode(PerfMode.Balanced, true); return "Modus: Intelligent";
                    case '3': SetMode(PerfMode.Performance, true); return "Modus: Extreme Leistung";
                    case 'c':
                        {
                            var m = Lenovo.GetChargeMode();
                            var n = m == ChargeMode.Normal ? ChargeMode.Conservation : m == ChargeMode.Conservation ? ChargeMode.Rapid : ChargeMode.Normal;
                            Lenovo.SetChargeMode(n); return "Lademodus: " + ChargeName(n);
                        }
                    case 'h':
                        {
                            string dev = Display.InternalDevice(); var rates = Display.Rates(dev);
                            int cur = Display.Current(dev).dmDisplayFrequency;
                            int next = cur == rates.Max() ? rates.Min() : rates.Max();
                            Display.SetRate(dev, next); return "Display: " + next + " Hz";
                        }
                    case 'k':
                        {
                            int t = Lenovo.KbdType(); int l = Lenovo.GetKbd(t);
                            int n = (l + 1) % (Lenovo.KbdMax(t) + 1); Lenovo.SetKbd(t, n);
                            return "Tastatur: " + Lenovo.KbdName(t, n);
                        }
                    case 'f': { bool v = !Lenovo.GetFnLock(); Lenovo.SetFnLock(v); return "Fn-Lock: " + (v ? "an" : "aus"); }
                    case 't':
                        {
                            var d = Hid.Find("touch");
                            int rc = Elevate(new[] { "touch", d.Enabled ? "aus" : "an" });
                            return rc == 0 ? "Touchscreen umgeschaltet" : "Touchscreen: abgebrochen";
                        }
                    case 's': sort = (sort + 1) % 3; return "Sortierung: " + new[] { "≈ Watt", "CPU", "RAM" }[sort];
                }
            }
            catch (Exception e) { return "Fehler: " + e.Message; }
            return null;
        }

        static void DrawLive(Sampler s, bool drv, int sort, string msg)
        {
            int w = Math.Max(60, Console.WindowWidth - 1), h = Math.Max(15, Console.WindowHeight);
            var lines = new List<string>();
            var b = BatteryInfo.Read();
            lines.Add(C.Bold(" Yoga Live") + C.Dim("  " + DateTime.Now.ToString("HH:mm:ss") + "   [1] Sparen [2] Auto [3] Max  [c] Laden [h] Hz [k] Licht [f] Fn [t] Touch [s] Sort [q] Ende"));
            lines.Add(C.Dim(" " + new string('─', Math.Min(w - 2, 96))));
            lines.Add(" " + C.Dim("Akku".PadRight(9)) + BatteryLine(b));
            if (s.Rapl.Count > 0) lines.Add(" " + C.Dim("Leistung".PadRight(9)) + RaplText(s.Rapl));
            lines.Add(" " + C.Dim("CPU".PadRight(9)) + Bar(s.CpuUtil, 20) + " " + F(s.CpuUtil, 0).PadLeft(3) + " % · " + F(s.GHz, 1) + " GHz · " + (double.IsNaN(s.TempMax) ? "–" : F(s.TempMax, 0) + " °C"));
            lines.Add(" " + C.Dim("GPU".PadRight(9)) + Bar(s.GpuTotal, 20) + " " + F(s.GpuTotal, 0).PadLeft(3) + " %");
            lines.Add(" " + C.Dim("RAM".PadRight(9)) + Bar(s.MemUsedGB / s.MemTotalGB * 100, 20) + " " + MemText(s.MemUsedGB, s.MemTotalGB, s.CommitGB, s.CommitLimitGB, s.PageFilePct, s.PagesPerSec));
            string mode = drv ? Try(() => ModeName(Lenovo.GetPerfMode()), "?") + " · Laden " + Try(() => ChargeName(Lenovo.GetChargeMode()), "?") : "–";
            string dev = Display.InternalDevice();
            lines.Add(" " + C.Dim("Modus".PadRight(9)) + C.Bold(mode) + " · Win " + WinPower.OverlayName(WinPower.GetOverlay()) + (dev != null ? " · " + Display.Current(dev).dmDisplayFrequency + " Hz" : ""));
            lines.Add(" " + C.Dim(new string('─', Math.Min(w - 2, 96))));
            lines.Add(ProcHeader());
            IEnumerable<ProcRow> rows = sort == 0 ? s.Rows.OrderByDescending(x => x.Watt).ThenByDescending(x => x.Cpu)
                : sort == 1 ? s.Rows.OrderByDescending(x => x.Cpu) : s.Rows.OrderByDescending(x => x.MemMB);
            int room = h - lines.Count - 2;
            foreach (var r in rows.Take(Math.Max(3, room))) lines.Add(ProcLine(r));
            lines.Add(C.Dim(" ≈ Watt = grobe Zuordnung der gemessenen Kern-/GPU-Leistung nach Anteil") + (string.IsNullOrEmpty(msg) ? "" : "   " + C.Cyan(msg)));

            var sb = new StringBuilder("\x1b[H");
            foreach (var l in lines.Take(h - 1)) sb.Append(l).Append("\x1b[K\n");
            sb.Append("\x1b[J");
            Console.Write(sb.ToString());
        }

        // ------------------------------------------------------------------ Apps (SRUM)
        static void Apps(string[] rest)
        {
            double hours = 24; bool batOnly = false, tsv = false;
            foreach (var r in rest)
            {
                if (r == "--akku" || r == "--battery") batOnly = true;
                else if (r == "--tsv") tsv = true;
                else double.TryParse(r.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out hours);
            }
            RequireAdmin();
            string csv = Path.Combine(Path.GetTempPath(), "yoga-srum.csv");
            if (File.Exists(csv)) File.Delete(csv);
            var psi = new ProcessStartInfo("powercfg.exe", "/srumutil /output " + Quote(csv) + " /csv");
            psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true;
            var p = Process.Start(psi); p.StandardOutput.ReadToEnd(); p.WaitForExit();
            if (!File.Exists(csv)) throw new Exception("powercfg /srumutil hat keine Daten geliefert");

            var since = DateTime.UtcNow.AddHours(-hours);
            var agg = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase); // total, cpu, display, net, disk, other
            DateTime first = DateTime.MaxValue, lastTs = DateTime.MinValue;
            using (var rd = new StreamReader(csv, Encoding.UTF8))
            {
                string[] hdr = rd.ReadLine().Split(',').Select(x => x.Trim()).ToArray();
                Func<string, int> col = n => Array.IndexOf(hdr, n);
                int cTs = col("TimeStamp"), cBat = col("OnBattery"), cTot = col("TotalEnergyConsumption"), cCpu = col("CPUEnergyConsumption"),
                    cDisp = col("DisplayEnergyConsumption"), cNet = col("NetworkEnergyConsumption"), cDisk = col("DiskEnergyConsumption");
                if (cTs < 0 || cTot < 0) throw new Exception("Unbekanntes SRUM-Format");
                string line;
                while ((line = rd.ReadLine()) != null)
                {
                    var f = line.Split(',');
                    if (f.Length < hdr.Length) continue;
                    int extra = f.Length - hdr.Length; // Kommas im AppId-Pfad
                    Func<int, string> get = i => (i == 0 ? string.Join(",", f, 0, extra + 1) : f[i + extra]).Trim();
                    DateTime ts;
                    if (!DateTime.TryParse(get(cTs), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out ts)) continue;
                    if (ts < since) continue;
                    if (batOnly && !get(cBat).Equals("TRUE", StringComparison.OrdinalIgnoreCase)) continue;
                    Func<int, double> num = i => { double v; return i >= 0 && double.TryParse(get(i), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0; };
                    double tot = num(cTot); if (tot <= 0) continue;
                    string app = ShortApp(get(0));
                    double[] a;
                    if (!agg.TryGetValue(app, out a)) agg[app] = a = new double[6];
                    double cpu = num(cCpu), disp = num(cDisp), net = num(cNet), disk = num(cDisk);
                    a[0] += tot; a[1] += cpu; a[2] += disp; a[3] += net; a[4] += disk; a[5] += Math.Max(0, tot - cpu - disp - net - disk);
                    if (ts < first) first = ts; if (ts > lastTs) lastTs = ts;
                }
            }
            try { File.Delete(csv); } catch { }
            // EMI_RAPL_* sind Hardware-Messzähler, keine Apps
            var hw = agg.Where(x => x.Key.StartsWith("EMI_", StringComparison.OrdinalIgnoreCase)).ToDictionary(x => x.Key.Replace("EMI_RAPL_Package0_", ""), x => x.Value[0]);
            foreach (var k in agg.Keys.Where(x => x.StartsWith("EMI_", StringComparison.OrdinalIgnoreCase)).ToList()) agg.Remove(k);
            double sum = agg.Values.Sum(x => x[0]);
            if (tsv)
            {
                var inv = CultureInfo.InvariantCulture;
                foreach (var kv in hw) Console.WriteLine("HW\t" + kv.Key + "\t" + (kv.Value / 3.6e6).ToString("0.###", inv));
                if (lastTs > first) Console.WriteLine("SPAN\t" + (lastTs - first).TotalHours.ToString("0.##", inv));
                foreach (var kv in agg.OrderByDescending(x => x.Value[0]).Take(40))
                    Console.WriteLine("APP\t" + kv.Key + "\t" + (kv.Value[0] / 3.6e6).ToString("0.####", inv) + "\t" + string.Join("\t", kv.Value.Skip(1).Select(v => (kv.Value[0] > 0 ? v / kv.Value[0] : 0).ToString("0.###", inv))));
                return;
            }
            if (sum <= 0) { Console.WriteLine("Keine Energiedaten im Zeitraum."); return; }
            Console.WriteLine(C.Bold(" Energie pro App") + C.Dim(" · letzte " + F(hours, 0) + " h" + (batOnly ? " · nur Akkubetrieb" : "") + " · Quelle: Windows-Energieschätzung (SRUM)"));
            if (hw.Count > 0)
            {
                Func<string, string> hv = k => { double v; return hw.TryGetValue(k, out v) ? F(v / 3.6e6, 1) + " Wh" : "–"; };
                double pkg; double avg = hw.TryGetValue("PKG", out pkg) && lastTs > first ? pkg / 1000 / (lastTs - first).TotalSeconds : double.NaN;
                Console.WriteLine(" " + C.Dim("Gemessen") + "  SoC " + C.Bold(hv("PKG")) + C.Dim(" (Kerne " + hv("PP0") + " · RAM " + hv("DRAM") + ")")
                    + (double.IsNaN(avg) ? "" : C.Dim(" · Ø " + F(avg, 1) + " W über " + F((lastTs - first).TotalHours, 0) + " h")));
            }
            Console.WriteLine(C.Dim(" " + "App".PadRight(34) + "Anteil".PadLeft(8) + "≈ Wh".PadLeft(8) + "  CPU  Disp  Netz  Disk  Rest"));
            foreach (var kv in agg.OrderByDescending(x => x.Value[0]).Take(25))
            {
                var a = kv.Value; double pct = a[0] / sum * 100;
                string name = kv.Key.Length > 33 ? kv.Key.Substring(0, 32) + "…" : kv.Key;
                Func<double, string> part = v => (a[0] > 0 ? (v / a[0] * 100).ToString("0", DE) : "0").PadLeft(5);
                Console.WriteLine(" " + name.PadRight(34) + (F(pct, 1) + " %").PadLeft(8) + F(a[0] / 3.6e6, 2).PadLeft(8) + " " + part(a[1]) + part(a[2]) + part(a[3]) + part(a[4]) + part(a[5]));
            }
            Console.WriteLine(C.Dim(" Summe ≈ " + F(sum / 3.6e6, 1) + " Wh · Spalten CPU…Rest = Aufteilung der App-Energie in %"));
        }

        static string ShortApp(string id)
        {
            if (string.IsNullOrEmpty(id)) return "(unbekannt)";
            int bs = id.LastIndexOf('\\');
            if (bs >= 0) id = id.Substring(bs + 1);
            int dbl = id.IndexOf("__");
            if (dbl > 0 && id.IndexOf('_') < dbl) id = id.Substring(0, id.IndexOf('_')); // Paketname
            if (id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) id = id.Substring(0, id.Length - 4);
            if (id == "Unknown") return "(nicht zugeordnet)";
            return id;
        }

        // ------------------------------------------------------------------ Akku
        static void Battery(string[] rest)
        {
            if (rest.Any(x => x == "--report"))
            {
                string f = Path.Combine(Path.GetTempPath(), "yoga-akkubericht.html");
                var psi = new ProcessStartInfo("powercfg.exe", "/batteryreport /output " + Quote(f)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                var p = Process.Start(psi); p.StandardOutput.ReadToEnd(); p.WaitForExit();
                if (File.Exists(f)) { Process.Start(f); Console.WriteLine("Bericht geöffnet: " + f); }
                else Console.WriteLine("Bericht konnte nicht erstellt werden.");
                return;
            }
            var b = BatteryInfo.Read();
            if (!b.Present) { Console.WriteLine("Kein Akku gefunden."); return; }
            Line("Ladung", BatteryLine(b));
            Line("Kapazität", F(b.Remaining / 1000.0, 1) + " von " + F(b.FullCharged / 1000.0, 1) + " Wh");
            Line("Gesundheit", Bar(b.Health, 20) + " " + F(b.Health, 1) + " % der Nennkapazität (" + F(b.Designed / 1000.0, 1) + " Wh)");
            Line("Zyklen", b.CycleCount.ToString());
            Line("Spannung", F(b.Voltage / 1000.0, 2) + " V" + (double.IsNaN(b.TempC) ? "" : " · " + F(b.TempC, 1) + " °C"));
            Line("Modell", (b.Maker + " " + b.Name).Trim());
            if (EnergyDrv.Available()) Line("Lademodus", Try(() => ChargeName(Lenovo.GetChargeMode()), "?"));
            Console.WriteLine(C.Dim(" Tipp: 'yoga laden schonen' hält den Akku bei ~80 % – gut, wenn das Gerät meist am Netz hängt."));
        }

        // ------------------------------------------------------------------ Energie-Einstellungen
        static PerfMode ParseMode(string s)
        {
            switch (s)
            {
                case "spar": case "sparen": case "eco": case "akku": case "quiet": case "leise": case "1": return PerfMode.Quiet;
                case "auto": case "intelligent": case "ausgeglichen": case "balanced": case "normal": case "2": return PerfMode.Balanced;
                case "max": case "leistung": case "extrem": case "performance": case "perf": case "3": return PerfMode.Performance;
            }
            throw new Exception("Modus unbekannt: " + s + " (spar | auto | max)");
        }

        static void SetMode(PerfMode m, bool alsoWindows)
        {
            Lenovo.SetPerfMode(m);
            if (alsoWindows) WinPower.SetOverlay(m == PerfMode.Quiet ? WinPower.Efficiency : m == PerfMode.Performance ? WinPower.BestPerf : WinPower.Balanced);
        }

        static void Mode(string[] rest)
        {
            bool onlyLenovo = rest.Any(x => x == "--nur-lenovo");
            var arg = rest.FirstOrDefault(x => !x.StartsWith("--"));
            if (arg != null)
            {
                SetMode(ParseMode(arg.ToLowerInvariant()), !onlyLenovo);
                Thread.Sleep(150);
            }
            Line("Lenovo-Modus", C.Bold(ModeName(Lenovo.GetPerfMode())));
            Line("Windows", WinPower.OverlayName(WinPower.GetOverlay()));
            if (arg == null) Console.WriteLine(C.Dim(" Ändern: yoga modus spar | auto | max   (--nur-lenovo lässt Windows unverändert)"));
        }

        static void WinMode(string arg)
        {
            if (arg != null)
            {
                Guid g;
                if (arg.StartsWith("eff") || arg == "spar") g = WinPower.Efficiency;
                else if (arg.StartsWith("leist") || arg == "max") g = WinPower.BestPerf;
                else if (arg.StartsWith("aus") || arg == "auto" || arg == "balanced") g = WinPower.Balanced;
                else throw new Exception("effizienz | ausgeglichen | leistung");
                WinPower.SetOverlay(g);
            }
            Line("Windows", WinPower.OverlayName(WinPower.GetOverlay()));
        }

        static void Charge(string arg)
        {
            if (arg != null)
            {
                ChargeMode m;
                if (arg.StartsWith("schon") || arg.StartsWith("cons") || arg == "80" || arg == "erhalt") m = ChargeMode.Conservation;
                else if (arg.StartsWith("schnell") || arg.StartsWith("rapid") || arg == "turbo") m = ChargeMode.Rapid;
                else if (arg == "normal" || arg == "standard") m = ChargeMode.Normal;
                else throw new Exception("normal | schonen | schnell");
                Lenovo.SetChargeMode(m);
                Thread.Sleep(300);
            }
            Line("Lademodus", C.Bold(ChargeName(Lenovo.GetChargeMode())));
            if (arg == null) Console.WriteLine(C.Dim(" Ändern: yoga laden normal | schonen | schnell"));
        }

        static readonly string[] BoostNames = { "aus", "an", "aggressiv", "effizient", "effizient aggressiv", "aggressiv (garantiert)", "effizient aggr. (garantiert)" };
        static string BoostName(int v) { return v >= 0 && v < BoostNames.Length ? BoostNames[v] : "?"; }

        static IEnumerable<bool> Targets(string[] rest)
        {
            bool ac = rest.Contains("--ac"), dc = rest.Contains("--dc");
            if (!ac && !dc) { ac = dc = true; }
            if (ac) yield return true;
            if (dc) yield return false;
        }

        static void Boost(string[] rest)
        {
            var arg = rest.FirstOrDefault(x => !x.StartsWith("--"));
            if (arg != null)
            {
                uint v;
                switch (arg.ToLowerInvariant())
                {
                    case "aus": case "off": case "0": v = 0; break;
                    case "an": case "on": case "1": v = 1; break;
                    case "aggressiv": case "2": v = 2; break;
                    case "effizient": case "eff": case "3": v = 3; break;
                    default: throw new Exception("an | aus | aggressiv | effizient");
                }
                foreach (bool ac in Targets(rest)) WinPower.WriteValue(WinPower.SetBoost, ac, v);
            }
            Line("CPU-Turbo", "Netz " + BoostName((int)WinPower.ReadValue(WinPower.SetBoost, true)) + " · Akku " + BoostName((int)WinPower.ReadValue(WinPower.SetBoost, false)));
            if (arg == null) Console.WriteLine(C.Dim(" Turbo aus spart auf Akku deutlich Strom, kostet Spitzenleistung."));
        }

        static void CpuMax(string[] rest)
        {
            var arg = rest.FirstOrDefault(x => !x.StartsWith("--"));
            if (arg != null)
            {
                uint v;
                if (!uint.TryParse(arg.TrimEnd('%'), out v) || v < 5 || v > 100) throw new Exception("Wert 5–100");
                foreach (bool ac in Targets(rest)) WinPower.WriteValue(WinPower.SetMaxState, ac, v);
            }
            Line("CPU max.", "Netz " + WinPower.ReadValue(WinPower.SetMaxState, true) + " % · Akku " + WinPower.ReadValue(WinPower.SetMaxState, false) + " %");
        }

        static void Plan(string[] rest)
        {
            var list = WinPower.Schemes(); var act = WinPower.ActiveScheme();
            if (rest.Length > 0)
            {
                string q = string.Join(" ", rest); int idx;
                var hit = int.TryParse(q, out idx) && idx >= 1 && idx <= list.Count ? list[idx - 1]
                    : list.FirstOrDefault(x => x.Value.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
                if (hit.Key == Guid.Empty) throw new Exception("Plan nicht gefunden: " + q);
                WinPower.SetActiveScheme(hit.Key); act = hit.Key;
            }
            for (int i = 0; i < list.Count; i++)
                Console.WriteLine(" " + (list[i].Key == act ? C.Green("●") : " ") + " " + (i + 1) + "  " + list[i].Value + C.Dim("  " + list[i].Key));
        }

        // ------------------------------------------------------------------ Yoga-Hardware
        static bool? ParseOnOff(string s)
        {
            if (s == null) return null;
            if (s == "an" || s == "ein" || s == "on" || s == "1" || s == "true") return true;
            if (s == "aus" || s == "off" || s == "0" || s == "false") return false;
            throw new Exception("an | aus erwartet");
        }

        static void Device(string kind, string arg)
        {
            string label = kind == "touch" ? "Touchscreen" : kind == "pen" ? "Stift" : "Touchpad";
            var d = Hid.Find(kind);
            if (!d.Found) { Console.WriteLine(label + ": kein internes Gerät gefunden."); return; }
            bool? want = arg == "umschalten" || arg == "toggle" ? !d.Enabled : ParseOnOff(arg);
            if (want.HasValue && want.Value != d.Enabled)
            {
                RequireAdmin();
                var psi = new ProcessStartInfo("pnputil.exe", (want.Value ? "/enable-device " : "/disable-device ") + Quote(d.InstanceId));
                psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true;
                var p = Process.Start(psi); string o = p.StandardOutput.ReadToEnd(); p.WaitForExit();
                if (p.ExitCode != 0 && p.ExitCode != 3010) throw new Exception("pnputil: " + o.Trim());
                d = Hid.Find(kind);
            }
            Line(label, OnOff(d.Enabled) + C.Dim("  " + d.Name));
        }

        static void FnLock(string arg)
        {
            var v = ParseOnOff(arg);
            if (v.HasValue) Lenovo.SetFnLock(v.Value);
            Line("Fn-Lock", OnOff(Lenovo.GetFnLock()));
        }

        static void Usb(string arg)
        {
            var v = ParseOnOff(arg);
            if (v.HasValue) Lenovo.SetUsbCharge(v.Value);
            Line("USB-Laden", "im Aus/Standby " + OnOff(Lenovo.GetUsbCharge()));
        }

        static void Kbd(string arg)
        {
            int t = Lenovo.KbdType();
            if (t == 0) { Console.WriteLine("Keine steuerbare Tastaturbeleuchtung gefunden."); return; }
            if (arg != null)
            {
                int lvl;
                if (arg == "aus" || arg == "off") lvl = 0;
                else if (arg == "niedrig" || arg == "low" || arg == "an" || arg == "on") lvl = 1;
                else if (arg == "hoch" || arg == "high") lvl = Math.Min(2, Lenovo.KbdMax(t));
                else if (arg == "auto") { if (t != 3) throw new Exception("Auto wird nicht unterstützt"); lvl = 3; }
                else if (!int.TryParse(arg, out lvl) || lvl < 0 || lvl > Lenovo.KbdMax(t)) throw new Exception("aus | niedrig | hoch" + (t == 3 ? " | auto" : ""));
                Lenovo.SetKbd(t, lvl);
            }
            Line("Tastatur", "Beleuchtung " + C.Bold(Lenovo.KbdName(t, Lenovo.GetKbd(t))));
        }

        static void Hz(string arg)
        {
            string dev = Display.InternalDevice();
            if (dev == null) throw new Exception("Kein Display gefunden");
            var rates = Display.Rates(dev);
            if (arg != null)
            {
                int hz;
                if (arg == "max") hz = rates.Max();
                else if (arg == "min") hz = rates.Min();
                else if (!int.TryParse(arg, out hz)) throw new Exception("Zahl, min oder max");
                if (!rates.Contains(hz)) throw new Exception(hz + " Hz nicht verfügbar (" + string.Join("/", rates) + ")");
                Display.SetRate(dev, hz);
            }
            Line("Display", C.Bold(Display.Current(dev).dmDisplayFrequency + " Hz") + C.Dim("  verfügbar: " + string.Join(" / ", rates) + " Hz"));
        }

        static int GetBrightness()
        {
            using (var s = new System.Management.ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness"))
                foreach (System.Management.ManagementObject o in s.Get()) return Convert.ToInt32(o["CurrentBrightness"]);
            return -1;
        }

        static void Bright(string arg)
        {
            if (arg != null)
            {
                int v;
                if (!int.TryParse(arg.TrimEnd('%'), out v) || v < 0 || v > 100) throw new Exception("Wert 0–100");
                using (var s = new System.Management.ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods"))
                    foreach (System.Management.ManagementObject o in s.Get()) o.InvokeMethod("WmiSetBrightness", new object[] { 1u, (byte)v });
                Thread.Sleep(200);
            }
            Line("Helligkeit", GetBrightness() + " %");
        }
    }
}
