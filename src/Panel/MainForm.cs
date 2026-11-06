// YogaPanel: leichtgewichtige Desktop-App mit Live-Diagrammen und Tray-Symbol.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace YogaTool.Panel
{
    static class App
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            bool created;
            var mutex = new Mutex(true, "Local\\YogaPanel.Single", out created);
            var showEvt = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\YogaPanel.Show");
            if (!created) { showEvt.Set(); return; }
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) Th.S = g.DpiX / 96f;
            var form = new MainForm(args.Contains("--tray"));
            var t = new Thread(() => { while (true) { showEvt.WaitOne(); try { form.BeginInvoke((Action)form.ShowFromTray); } catch { } } });
            t.IsBackground = true; t.Start();
            Application.Run(form);
            GC.KeepAlive(mutex);
        }
    }

    // Momentaufnahme des Geräts (wird im Hintergrund gelesen).
    class HwState
    {
        public bool Drv;
        public PerfMode Mode = PerfMode.Unknown;
        public ChargeMode Charge = ChargeMode.Unknown;
        public bool FnLock, Usb;
        public int KbdType, Kbd = -1;
        public HidDev Touch, Pen, Pad;
        public string Dev; public int Hz; public List<int> Rates = new List<int>();
        public int Bright = -1, BoostDc = -1;
    }

    class Tick
    {
        public DateTime Time = DateTime.UtcNow;
        public BatteryInfo Bat; public float[] V; public List<ProcRow> Rows;
        public double Pkg, Cpu, GpuU, Temp, GHz;
        public double MemTotal, MemUsed, Commit, CommitLimit, PageFile, Pages;
    }

    class MainForm : Form
    {
        readonly History hist = new History();
        readonly ChartCard power = new ChartCard(), util = new ChartCard(), temp = new ChartCard(), mem = new ChartCard();
        readonly BatteryCard bat = new BatteryCard();
        readonly Toolbar toolbar = new Toolbar();
        readonly ProcCard procs = new ProcCard();
        readonly CardBase ctrl = new CardBase();
        readonly Segmented segRange = new Segmented(), segRate = new Segmented(), segMode = new Segmented(), segCharge = new Segmented(), segHz = new Segmented(), segKbd = new Segmented();
        readonly Slider bright = new Slider();
        readonly Toggle tFn = new Toggle(), tUsb = new Toggle(), tTouch = new Toggle(), tPen = new Toggle(), tPad = new Toggle(), tTurbo = new Toggle(), tAuto = new Toggle();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly ToolStripMenuItem[] trayModes = new ToolStripMenuItem[3], trayCharge = new ToolStripMenuItem[3];
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly string yogaExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "yoga.exe");
        HwState hw = new HwState();
        volatile bool forceState = true, quitting;
        bool startHidden;
        IntPtr lastIcon = IntPtr.Zero;
        DateTime suppressUntil = DateTime.MinValue; // nach eigener Änderung kurz keine Statusüberschreibung

        static readonly double[] RangeSec = { 60, 300, 900, 3600, 6 * 3600, 24 * 3600 };
        static readonly int[] RateMs = { 500, 1000, 2000, 5000 };
        volatile int intervalMs = 1000;
        readonly string histFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YogaPanel", "history.bin");
        const string SettingsKey = @"Software\YogaPanel";

        static int ReadSetting(string name, int def)
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(SettingsKey)) return k == null ? def : Convert.ToInt32(k.GetValue(name, def)); }
            catch { return def; }
        }
        static void WriteSetting(string name, int v)
        {
            try { using (var k = Registry.CurrentUser.CreateSubKey(SettingsKey)) k.SetValue(name, v, RegistryValueKind.DWord); } catch { }
        }

        public MainForm(bool hidden)
        {
            startHidden = hidden;
            uiVisible = !hidden;
            string model = Model();
            Text = "Yoga Panel" + (model != "" ? "  ·  " + model : "");
            BackColor = Th.Bg; ForeColor = Th.Text;
            Font = Th.F(9f);
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Th.P(1160), Th.P(760));
            MinimumSize = new Size(Th.P(980), Th.P(680));
            Icon = MakeAppIcon(64);
            int ww = ReadSetting("WinW", 0), wh = ReadSetting("WinH", 0);
            if (ww >= MinimumSize.Width && wh >= MinimumSize.Height) ClientSize = new Size(ww, wh);
            if (ReadSetting("WinMax", 0) == 1) WindowState = FormWindowState.Maximized;
            DoubleBuffered = true;

            // Diagramme
            power.Title = "Leistungsaufnahme"; power.H = hist; power.Unit = "W"; power.MinMax = 5;
            power.PlotTop = 98; power.SubBelow = true;
            power.Series.Add(new Series("Kerne", History.Cores, Th.Violet) { Stack = true });
            power.Series.Add(new Series("GPU", History.GpuW, Th.Cyan) { Stack = true });
            power.Series.Add(new Series("SoC-Rest", History.Rest, Th.Slate) { Stack = true });
            power.Series.Add(new Series("RAM", History.Dram, Th.Orange) { Stack = true });
            power.Series.Add(new Series("Akku", History.BatW, Th.Yellow) { Dashed = true, Fill = false });
            util.Title = "Auslastung"; util.H = hist; util.Unit = "%"; util.FixedMax = 100; util.Decimals = 0;
            util.Series.Add(new Series("CPU", History.Cpu, Th.Violet));
            util.Series.Add(new Series("GPU", History.GpuU, Th.Cyan));
            temp.Title = "Temperatur"; temp.H = hist; temp.Unit = "°C"; temp.MinMax = 60; temp.Decimals = 0;
            temp.Series.Add(new Series("Chip", History.Temp, Th.Pink));
            util.SubBelow = temp.SubBelow = mem.SubBelow = true;
            util.PlotTop = temp.PlotTop = mem.PlotTop = 98;
            // RAM in GB, Skala 0 … ermittelter Gesamt-RAM; Paging als eigener Balkenstreifen
            double mt, mu, cu, cl;
            Mem.Read(out mt, out mu, out cu, out cl);
            mem.Title = "Arbeitsspeicher"; mem.H = hist; mem.Unit = "GB"; mem.Decimals = 1;
            mem.FixedMax = double.IsNaN(mt) ? 16 : (float)mt; mem.TickStep = mt > 24 ? 8 : 4;
            mem.Series.Add(new Series("Belegt", History.MemGB, Th.Green));
            mem.Series.Add(new Series("RAM", History.MemPct, Th.Green) { Hidden = true, Unit = "%", Decimals = 0 });
            mem.Series.Add(new Series("Commit", History.CommitPct, Th.Yellow) { Hidden = true, Unit = "%", Decimals = 0 });
            mem.Series.Add(new Series("Auslagerungsdatei", History.PageFilePct, Th.Pink) { Hidden = true, Unit = "%", Decimals = 0 });
            mem.Series.Add(new Series("Paging", History.PagingMB, Th.Pink) { Strip = true, Unit = "MB/s", Decimals = 1 });

            Func<double> range = () => RangeSec[Math.Max(0, Math.Min(RangeSec.Length - 1, segRange.Selected))];
            power.Range = util.Range = temp.Range = mem.Range = range;

            segRange.Caption = "ZEITRAUM";
            segRange.Items = new[] { "1 min", "5 min", "15 min", "1 h", "6 h", "24 h" };
            segRange.Selected = Math.Max(0, Math.Min(RangeSec.Length - 1, ReadSetting("Range", 1))); segRange.BackColor = Th.Bg;
            segRange.Accents = Enumerable.Repeat(Th.Slate, 6).ToArray();
            segRange.Changed += i => { WriteSetting("Range", i); power.Invalidate(); util.Invalidate(); temp.Invalidate(); mem.Invalidate(); };
            toolbar.Controls.Add(segRange);

            segRate.Caption = "AKTUALISIERUNG";
            segRate.Items = new[] { "0,5 s", "1 s", "2 s", "5 s" };
            segRate.Selected = Math.Max(0, Array.IndexOf(RateMs, ReadSetting("IntervalMs", 1000)));
            intervalMs = RateMs[segRate.Selected]; hist.IntervalSec = intervalMs / 1000.0;
            segRate.BackColor = Th.Bg; segRate.Accents = Enumerable.Repeat(Th.Slate, 4).ToArray();
            segRate.Changed += i => { intervalMs = RateMs[i]; hist.IntervalSec = intervalMs / 1000.0; WriteSetting("IntervalMs", intervalMs); wake.Set(); };
            toolbar.Controls.Add(segRate);
            toolbar.Model = model;

            hist.Load(histFile);
            var saveTimer = new System.Windows.Forms.Timer { Interval = 10 * 60 * 1000 };
            saveTimer.Tick += (s, e) => hist.Save(histFile);
            saveTimer.Start();
            Application.ApplicationExit += (s, e) => hist.Save(histFile);

            // Steuerung
            segMode.Caption = "LEISTUNGSMODUS"; segMode.Note = "auch per Fn+Q";
            segMode.Items = new[] { "Akku sparen", "Intelligent", "Extrem" }; segMode.Accents = new[] { Th.Green, Th.Violet, Th.Orange };
            segMode.Changed += i => Act(() => SetMode(i == 0 ? PerfMode.Quiet : i == 1 ? PerfMode.Balanced : PerfMode.Performance));
            segCharge.Caption = "LADEMODUS"; segCharge.Note = "Schonen hält ~80 %";
            segCharge.Items = new[] { "Normal", "Schonen", "Schnell" }; segCharge.Accents = new[] { Th.Violet, Th.Green, Th.Orange };
            segCharge.Changed += i => Act(() => Lenovo.SetChargeMode(i == 0 ? ChargeMode.Normal : i == 1 ? ChargeMode.Conservation : ChargeMode.Rapid));
            segHz.Caption = "DISPLAY"; segHz.Accents = new[] { Th.Cyan, Th.Cyan, Th.Cyan, Th.Cyan };
            segHz.Changed += i => { var rates = HzChoices(); if (i < rates.Count) { int hz = rates[i]; string dev = hw.Dev; Act(() => Display.SetRate(dev, hz)); } };
            segKbd.Caption = "TASTATURLICHT"; segKbd.Accents = new[] { Th.Yellow, Th.Yellow, Th.Yellow, Th.Yellow };
            segKbd.Changed += i => { int t = hw.KbdType; Act(() => Lenovo.SetKbd(t, i)); };
            bright.Caption = "HELLIGKEIT";
            bright.Committed += v => ThreadPool.QueueUserWorkItem(_ => { try { SetBrightness(v); } catch { } });
            tFn.Label = "Fn-Lock"; tFn.Hint = "F1–F12 ohne Fn"; tFn.Changed += v => Act(() => Lenovo.SetFnLock(v));
            tUsb.Label = "USB-Laden"; tUsb.Hint = "wenn aus / im Standby"; tUsb.Changed += v => Act(() => Lenovo.SetUsbCharge(v));
            tTouch.Label = "Touchscreen"; tTouch.Changed += v => Cli(tTouch, "touch", v);
            tPen.Label = "Stift"; tPen.Changed += v => Cli(tPen, "stift", v);
            tPad.Label = "Touchpad"; tPad.Changed += v => Cli(tPad, "touchpad", v);
            tTurbo.Label = "CPU-Turbo auf Akku"; tTurbo.Hint = "aus = deutlich sparsamer";
            tTurbo.Changed += v => Act(() => WinPower.WriteValue(WinPower.SetBoost, false, v ? 2u : 0u));
            tAuto.Label = "Mit Windows starten"; tAuto.Hint = "im Tray"; tAuto.Accent = Th.Slate;
            tAuto.On = AutostartOn(); tAuto.Changed += SetAutostart;
            foreach (Control c in new Control[] { segMode, segCharge, segHz, segKbd, bright, tFn, tUsb, tTouch, tPen, tPad, tTurbo, tAuto }) ctrl.Controls.Add(c);
            foreach (var t in new[] { tTouch, tPen, tPad }) t.Hint = "Admin-Freigabe";

            procs.TabChanged += i => { if (i == 1 && procs.AppsState == "idle") LoadApps(); };
            procs.LoadApps += LoadApps;

            Controls.AddRange(new Control[] { toolbar, util, mem, power, temp, bat, procs, ctrl });
            BuildTray();
            Resize += (s, e) => LayoutAll();
            LayoutAll();
        }

        bool monitorStarted;
        volatile bool uiVisible = true;

        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); UpdateVis(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateVis(); }
        void UpdateVis()
        {
            bool v = Visible && WindowState != FormWindowState.Minimized;
            if (v && !uiVisible) { uiVisible = true; forceState = true; wake.Set(); }
            uiVisible = v;
        }

        // ------------------------------------------------------------ Layout
        void LayoutAll()
        {
            if (ClientSize.Width == 0) return;
            int m = Th.P(16), gap = Th.P(12), W = ClientSize.Width, H = ClientSize.Height;
            int rightW = Th.P(390), leftW = W - 2 * m - gap - rightW;
            // Leiste oben, dann: CPU | RAM, darunter Verbrauch | Temperatur, unten Programme
            int tbH = Segmented.CaptionH + Th.P(30);
            toolbar.SetBounds(m, Th.P(8), leftW, tbH);
            int top = Th.P(8) + tbH + Th.P(8);
            int rowH = Math.Max(Th.P(220), (int)((H - top) * 0.31));
            int half = (leftW - gap) / 2;
            util.SetBounds(m, top, half, rowH);
            mem.SetBounds(m + half + gap, top, leftW - half - gap, rowH);
            int r2 = top + rowH + gap;
            int pw = (int)((leftW - gap) * 0.62);
            power.SetBounds(m, r2, pw, rowH);
            temp.SetBounds(m + pw + gap, r2, leftW - pw - gap, rowH);
            int py = r2 + rowH + gap;
            procs.SetBounds(m, py, leftW, H - py - m);
            int rx = m + leftW + gap;
            int batH = Th.P(118);
            bat.SetBounds(rx, m, rightW, batH);
            ctrl.SetBounds(rx, m + batH + gap, rightW, H - 2 * m - batH - gap);
            int selH = Segmented.CaptionH + Th.P(28), rangeW = Th.P(330), rateW = Th.P(210);
            segRange.SetBounds(toolbar.Width - rangeW, 0, rangeW, selH);
            segRate.SetBounds(toolbar.Width - rangeW - Th.P(16) - rateW, 0, rateW, selH);

            int p = Th.P(18), w = ctrl.Width - 2 * p, y = Th.P(16), segH = Segmented.CaptionH + Th.P(36), vg = Th.P(12);
            segMode.SetBounds(p, y, w, segH); y += segH + vg;
            segCharge.SetBounds(p, y, w, segH); y += segH + vg;
            int hw2 = (w - Th.P(12)) / 2;
            segHz.SetBounds(p, y, hw2, segH);
            segKbd.SetBounds(p + hw2 + Th.P(12), y, w - hw2 - Th.P(12), segH); y += segH + vg;
            bright.SetBounds(p, y, w, Th.P(46)); y += Th.P(46) + Th.P(8);
            int tg = Math.Max(Th.P(30), Math.Min(Th.P(38), (ctrl.Height - y - Th.P(10)) / 7));
            foreach (var t in new[] { tFn, tUsb, tTouch, tPen, tPad, tTurbo, tAuto }) { t.SetBounds(p, y, w, tg); y += tg; }
            Invalidate(true);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int on = 1; DwmSetWindowAttribute(Handle, 20, ref on, 4);
            int col = Th.Bg.R | (Th.Bg.G << 8) | (Th.Bg.B << 16); DwmSetWindowAttribute(Handle, 35, ref col, 4);
            if (!monitorStarted)
            {
                monitorStarted = true;
                new Thread(MonitorLoop) { IsBackground = true, Name = "monitor" }.Start();
            }
        }
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

        protected override void SetVisibleCore(bool value)
        {
            if (startHidden && !IsHandleCreated) { CreateHandle(); value = false; }
            base.SetVisibleCore(value);
        }

        public void ShowFromTray()
        {
            startHidden = false;
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            forceState = true; wake.Set();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (WindowState == FormWindowState.Normal) { WriteSetting("WinW", ClientSize.Width); WriteSetting("WinH", ClientSize.Height); }
            if (WindowState != FormWindowState.Minimized) WriteSetting("WinMax", WindowState == FormWindowState.Maximized ? 1 : 0);
            if (!quitting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
            tray.Visible = false;
            base.OnFormClosing(e);
        }

        // ------------------------------------------------------------ Messung (Hintergrund-Thread)
        void MonitorLoop()
        {
            Sampler s = null;
            try { s = new Sampler(); } catch { }
            int n = 0;
            while (true)
            {
                // im Tray mindestens alle 2 s (spart Strom, Verlauf bleibt lückenlos)
                wake.WaitOne(uiVisible ? intervalMs : Math.Max(2000, intervalMs));
                bool vis = uiVisible;
                var t = new Tick();
                try
                {
                    t.Bat = BatteryInfo.Read();
                    if (s != null)
                    {
                        s.Full = vis;
                        s.Sample();
                        double pkg, pp0, pp1, dram;
                        s.Rapl.TryGetValue("RAPL_Package0_PKG", out pkg); s.Rapl.TryGetValue("RAPL_Package0_PP0", out pp0);
                        s.Rapl.TryGetValue("RAPL_Package0_PP1", out pp1); s.Rapl.TryGetValue("RAPL_Package0_DRAM", out dram);
                        t.Pkg = (pkg + dram) / 1000; t.Cpu = s.CpuUtil; t.GpuU = s.GpuTotal; t.Temp = s.TempMax; t.GHz = s.GHz;
                        t.V = new float[History.Channels];
                        t.V[History.Cores] = (float)(pp0 / 1000); t.V[History.GpuW] = (float)(pp1 / 1000);
                        t.V[History.Rest] = (float)Math.Max(0, (pkg - pp0 - pp1) / 1000); t.V[History.Dram] = (float)(dram / 1000);
                        t.V[History.BatW] = t.Bat.Discharging && t.Bat.Rate < 0 ? -t.Bat.Rate / 1000f : float.NaN;
                        t.V[History.Cpu] = (float)s.CpuUtil; t.V[History.GpuU] = vis ? (float)s.GpuTotal : float.NaN;
                        t.V[History.Temp] = (float)s.TempMax; t.V[History.GHz] = (float)s.GHz;
                        t.MemTotal = s.MemTotalGB; t.MemUsed = s.MemUsedGB; t.Commit = s.CommitGB; t.CommitLimit = s.CommitLimitGB;
                        t.PageFile = s.PageFilePct; t.Pages = s.PagesPerSec;
                        t.V[History.MemPct] = (float)(s.MemUsedGB / s.MemTotalGB * 100);
                        t.V[History.CommitPct] = (float)(s.CommitGB / s.CommitLimitGB * 100);
                        t.V[History.PageFilePct] = (float)s.PageFilePct;
                        t.V[History.Paging] = (float)s.PagesPerSec;
                        t.V[History.PagingMB] = (float)(s.PagesPerSec * 4096 / 1048576.0);
                        t.V[History.MemGB] = (float)s.MemUsedGB;
                        t.Rows = s.Rows;
                    }
                }
                catch { }
                // Lenovo-Status (schnell) alle 2 s, Geräte/Helligkeit/Display (langsam) alle 10 s; versteckt seltener
                HwState st = null;
                bool force = forceState;
                if (force || n % (vis ? 2 : 6) == 0) { forceState = false; st = ReadState(force || n % (vis ? 10 : 60) == 0); }
                n++;
                try { BeginInvoke((Action)(() => Apply(t, st))); } catch { if (IsDisposed) return; }
            }
        }

        HwState ReadState(bool slow)
        {
            var h = new HwState();
            var prev = hw;
            h.Drv = EnergyDrv.Available();
            if (h.Drv)
            {
                try { h.Mode = Lenovo.GetPerfMode(); } catch { }
                try { h.Charge = Lenovo.GetChargeMode(); } catch { }
                try { uint hals = Lenovo.Hals(); h.FnLock = (hals & (1u << 10)) != 0; h.Usb = (hals & (1u << 7)) != 0; } catch { }
                try { h.KbdType = Lenovo.KbdType(); if (h.KbdType > 0) h.Kbd = Lenovo.GetKbd(h.KbdType); } catch { }
            }
            try { h.BoostDc = (int)WinPower.ReadValue(WinPower.SetBoost, false); } catch { }
            if (!slow && prev != null && prev.Dev != null)
            {
                h.Touch = prev.Touch; h.Pen = prev.Pen; h.Pad = prev.Pad; h.Dev = prev.Dev; h.Hz = prev.Hz; h.Rates = prev.Rates; h.Bright = prev.Bright;
                return h;
            }
            try { h.Touch = Hid.Find("touch"); h.Pen = Hid.Find("pen"); h.Pad = Hid.Find("touchpad"); } catch { }
            try { h.Dev = Display.InternalDevice(); if (h.Dev != null) { h.Hz = Display.Current(h.Dev).dmDisplayFrequency; h.Rates = Display.Rates(h.Dev); } } catch { }
            try { h.Bright = GetBrightness(); } catch { }
            return h;
        }

        void Apply(Tick t, HwState st)
        {
            if (t.V != null) hist.Push(t.Time, t.V);
            if (st != null && DateTime.UtcNow > suppressUntil) { hw = st; ApplyState(); }
            UpdateTray(t);
            if (!Visible || WindowState == FormWindowState.Minimized) return;

            if (t.V != null)
            {
                power.Big = Fmt.N(t.Pkg, 1) + " W";
                power.Sub = t.Bat != null && t.Bat.Discharging ? "Chip · gesamt aus Akku " + Fmt.N(-t.Bat.Rate / 1000.0, 1) + " W" : "Chip-Leistung (SoC + RAM)";
                util.Big = Fmt.N(t.Cpu, 0) + " %"; util.Sub = Fmt.N(t.GHz, 1) + " GHz";
                temp.Big = Fmt.N(t.Temp, 0) + " °C";
                temp.BigColor = t.Temp >= 90 ? Th.Red : t.Temp >= 75 ? Th.Yellow : Th.Text;
                temp.Sub = t.Temp >= 90 ? "heiß" : t.Temp >= 75 ? "warm" : "kühl";
                double memPct = t.MemUsed / t.MemTotal * 100;
                mem.Big = Fmt.N(t.MemUsed, 1) + " GB";
                mem.BigColor = memPct >= 90 ? Th.Red : memPct >= 80 ? Th.Yellow : Th.Text;
                if (!double.IsNaN(t.MemTotal)) mem.FixedMax = (float)t.MemTotal;
                mem.Sub = "von " + Fmt.N(t.MemTotal, 1) + " GB (" + Fmt.N(memPct, 0) + " %) · Commit " + Fmt.N(t.Commit, 0) + "/" + Fmt.N(t.CommitLimit, 0) + " GB · Auslagerung " + Fmt.N(t.PageFile, 0) + " %";
                if (t.Rows != null) procs.Rows = t.Rows;
            }
            bat.B = t.Bat;
            power.Invalidate(); util.Invalidate(); temp.Invalidate(); mem.Invalidate(); bat.Invalidate();
            if (procs.Tab == 0) procs.Invalidate();
        }

        void ApplyState()
        {
            bool d = hw.Drv;
            segMode.Enabled = segCharge.Enabled = d;
            segMode.Selected = hw.Mode == PerfMode.Quiet ? 0 : hw.Mode == PerfMode.Balanced ? 1 : hw.Mode == PerfMode.Performance ? 2 : -1;
            segCharge.Selected = hw.Charge == ChargeMode.Normal ? 0 : hw.Charge == ChargeMode.Conservation ? 1 : hw.Charge == ChargeMode.Rapid ? 2 : -1;
            bat.ChargeMode = hw.Charge == ChargeMode.Conservation ? "Schonmodus" : hw.Charge == ChargeMode.Rapid ? "Schnellladen" : "Normal";
            var rates = HzChoices();
            segHz.Items = rates.Select(r => r + " Hz").ToArray();
            segHz.Selected = rates.IndexOf(hw.Hz);
            segHz.Note = hw.Hz > 0 && segHz.Selected < 0 ? hw.Hz + " Hz" : "";
            if (hw.KbdType == 3) segKbd.Items = new[] { "Aus", "1", "2", "Auto" };
            else if (hw.KbdType == 2) segKbd.Items = new[] { "Aus", "1", "2" };
            else if (hw.KbdType == 1) segKbd.Items = new[] { "Aus", "An" };
            else segKbd.Items = new string[0];
            segKbd.Enabled = hw.KbdType > 0; segKbd.Selected = hw.Kbd;
            if (hw.Bright >= 0 && !bright.Dragging) bright.Value = hw.Bright;
            bright.Enabled = hw.Bright >= 0;
            tFn.Available = tUsb.Available = d;
            if (!tFn.Busy) tFn.On = hw.FnLock;
            if (!tUsb.Busy) tUsb.On = hw.Usb;
            SetDev(tTouch, hw.Touch); SetDev(tPen, hw.Pen); SetDev(tPad, hw.Pad);
            tTurbo.Available = hw.BoostDc >= 0; tTurbo.On = hw.BoostDc > 0;
            for (int i = 0; i < 3; i++) { trayModes[i].Checked = segMode.Selected == i; trayCharge[i].Checked = segCharge.Selected == i; }
            ctrl.Invalidate(true);
        }

        void SetDev(Toggle t, HidDev d)
        {
            if (t.Busy) return;
            t.Available = d != null && d.Found;
            if (t.Available) t.On = d.Enabled;
        }

        List<int> HzChoices()
        {
            var r = hw.Rates ?? new List<int>();
            if (r.Count == 0) return new List<int>();
            var pick = new List<int>();
            foreach (int c in new[] { 60, 120 }) if (r.Contains(c)) pick.Add(c);
            if (pick.Count < 2) { pick.Clear(); pick.Add(r.Min()); if (r.Max() != r.Min()) pick.Add(r.Max()); }
            return pick;
        }

        // ------------------------------------------------------------ Aktionen
        void Act(Action a)
        {
            suppressUntil = DateTime.UtcNow.AddSeconds(1.5);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string err = null;
                try { a(); } catch (Exception e) { err = e.Message; }
                Thread.Sleep(150);
                suppressUntil = DateTime.MinValue; forceState = true; wake.Set();
                if (err != null) try { BeginInvoke((Action)(() => tray.ShowBalloonTip(3000, "Yoga Panel", err, ToolTipIcon.Warning))); } catch { }
            });
        }

        static void SetMode(PerfMode m)
        {
            Lenovo.SetPerfMode(m);
            WinPower.SetOverlay(m == PerfMode.Quiet ? WinPower.Efficiency : m == PerfMode.Performance ? WinPower.BestPerf : WinPower.Balanced);
        }

        // Touch & Co. brauchen Admin: yoga.exe übernimmt die UAC-Abfrage.
        void Cli(Toggle t, string what, bool on)
        {
            if (!File.Exists(yogaExe)) { t.On = !on; tray.ShowBalloonTip(3000, "Yoga Panel", "yoga.exe fehlt neben YogaPanel.exe", ToolTipIcon.Warning); return; }
            t.Busy = true; t.Invalidate();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var psi = new ProcessStartInfo(yogaExe, what + (on ? " an" : " aus")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8 };
                    using (var p = Process.Start(psi)) { p.StandardOutput.ReadToEnd(); p.WaitForExit(); }
                }
                catch { }
                BeginInvoke((Action)(() => { t.Busy = false; forceState = true; wake.Set(); }));
            });
        }

        void LoadApps()
        {
            if (procs.AppsState == "loading") return;
            if (!File.Exists(yogaExe)) { procs.AppsState = "error"; procs.AppsError = "yoga.exe fehlt"; procs.Invalidate(); return; }
            procs.AppsState = "loading"; procs.Invalidate();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var list = new List<AppEnergy>(); double pkg = double.NaN, span = double.NaN; string err = null;
                try
                {
                    var psi = new ProcessStartInfo(yogaExe, "apps 24 --tsv") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8 };
                    string output; int code;
                    using (var p = Process.Start(psi)) { output = p.StandardOutput.ReadToEnd(); p.WaitForExit(); code = p.ExitCode; }
                    var inv = CultureInfo.InvariantCulture;
                    foreach (var line in output.Split('\n'))
                    {
                        var f = line.TrimEnd('\r').Split('\t');
                        if (f[0] == "HW" && f.Length >= 3 && f[1] == "PKG") pkg = double.Parse(f[2], inv);
                        else if (f[0] == "SPAN" && f.Length >= 2) span = double.Parse(f[1], inv);
                        else if (f[0] == "APP" && f.Length >= 8)
                            list.Add(new AppEnergy { Name = f[1], Wh = double.Parse(f[2], inv), Cpu = double.Parse(f[3], inv), Disp = double.Parse(f[4], inv), Net = double.Parse(f[5], inv), Disk = double.Parse(f[6], inv), Rest = double.Parse(f[7], inv) });
                    }
                    if (list.Count == 0) err = code == 2 ? "Admin-Freigabe abgelehnt" : "keine Daten";
                }
                catch (Exception e) { err = e.Message; }
                BeginInvoke((Action)(() =>
                {
                    if (err != null) { procs.AppsState = "error"; procs.AppsError = err; }
                    else { procs.Apps = list; procs.HwPkg = pkg; procs.HwSpan = span; procs.AppsState = "done"; }
                    procs.Invalidate();
                }));
            });
        }

        static int GetBrightness()
        {
            using (var s = new System.Management.ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness"))
                foreach (System.Management.ManagementObject o in s.Get()) return Convert.ToInt32(o["CurrentBrightness"]);
            return -1;
        }
        static void SetBrightness(int v)
        {
            using (var s = new System.Management.ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods"))
                foreach (System.Management.ManagementObject o in s.Get()) o.InvokeMethod("WmiSetBrightness", new object[] { 1u, (byte)v });
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        static bool AutostartOn()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("YogaPanel") != null;
        }
        void SetAutostart(bool on)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue("YogaPanel", "\"" + Application.ExecutablePath + "\" --tray");
                else k.DeleteValue("YogaPanel", false);
            }
        }

        static string Model()
        {
            try { using (var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS")) return k == null ? "" : (string)k.GetValue("SystemFamily", ""); }
            catch { return ""; }
        }

        // ------------------------------------------------------------ Tray
        void BuildTray()
        {
            var menu = new ContextMenuStrip { Renderer = new ToolStripProfessionalRenderer(new DarkColors()), ShowImageMargin = false, ShowCheckMargin = true };
            menu.Font = Th.F(9.5f);
            Func<string, EventHandler, ToolStripMenuItem> item = (s, h) => { var it = new ToolStripMenuItem(s, null, h) { ForeColor = Th.Text }; return it; };
            var open = item("Yoga Panel öffnen", (s, e) => ShowFromTray()); open.Font = Th.Semi(9.5f);
            menu.Items.Add(open);
            menu.Items.Add(new ToolStripSeparator());
            string[] modes = { "Akku sparen", "Intelligent", "Extreme Leistung" };
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                trayModes[i] = item(modes[i], (s, e) => Act(() => SetMode(k == 0 ? PerfMode.Quiet : k == 1 ? PerfMode.Balanced : PerfMode.Performance)));
                menu.Items.Add(trayModes[i]);
            }
            menu.Items.Add(new ToolStripSeparator());
            string[] ch = { "Laden: Normal", "Laden: Schonen (~80 %)", "Laden: Schnell" };
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                trayCharge[i] = item(ch[i], (s, e) => Act(() => Lenovo.SetChargeMode(k == 0 ? ChargeMode.Normal : k == 1 ? ChargeMode.Conservation : ChargeMode.Rapid)));
                menu.Items.Add(trayCharge[i]);
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(item("Beenden", (s, e) => { quitting = true; tray.Visible = false; Application.Exit(); }));
            tray.ContextMenuStrip = menu;
            tray.Text = "Yoga Panel";
            tray.Icon = Icon;
            tray.Visible = true;
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) { if (Visible && WindowState != FormWindowState.Minimized) Hide(); else ShowFromTray(); } };
        }

        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        void UpdateTray(Tick t)
        {
            if (t.V == null) return;
            int size = SystemInformation.SmallIconSize.Width;
            Color c = hw.Mode == PerfMode.Quiet ? Th.Green : hw.Mode == PerfMode.Performance ? Th.Orange : Color.White;
            string s = t.Pkg >= 9.95 ? ((int)Math.Round(t.Pkg)).ToString() : t.Pkg.ToString("0.0", Fmt.DE);
            using (var bmp = new Bitmap(size, size))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    float fs = size * (s.Length >= 3 ? 0.50f : 0.62f);
                    using (var f = new Font("Segoe UI", fs, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    using (var b = new SolidBrush(c))
                        g.DrawString(s, f, b, new RectangleF(-size * 0.1f, 0, size * 1.2f, size * 1.04f), sf);
                    // Unterstreichung in Akkufarbe
                    if (t.Bat != null && t.Bat.Present)
                    {
                        float pct = (float)Math.Max(0, Math.Min(100, t.Bat.Percent)) / 100f;
                        Color bc = t.Bat.Charging ? Th.Cyan : pct <= 0.15f ? Th.Red : pct <= 0.35f ? Th.Yellow : Th.Green;
                        float h = Math.Max(2, size / 10f);
                        using (var b = new SolidBrush(Th.A(Color.White, 50))) g.FillRectangle(b, 1, size - h, size - 2, h);
                        using (var b = new SolidBrush(bc)) g.FillRectangle(b, 1, size - h, (size - 2) * pct, h);
                    }
                }
                IntPtr hi = bmp.GetHicon();
                tray.Icon = Icon.FromHandle(hi);
                if (lastIcon != IntPtr.Zero) DestroyIcon(lastIcon);
                lastIcon = hi;
            }
            string bt = t.Bat != null && t.Bat.Present ? " · Akku " + Fmt.N(t.Bat.Percent, 0) + " %" + (t.Bat.Discharging ? " (" + Fmt.N(-t.Bat.Rate / 1000.0, 1) + " W)" : t.Bat.Charging ? " lädt" : "") : "";
            string tip = "Yoga · Chip " + Fmt.N(t.Pkg, 1) + " W" + bt;
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        static Icon MakeAppIcon(int size)
        {
            using (var bmp = new Bitmap(size, size))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    Th.Hq(g);
                    var r = new RectangleF(2, 2, size - 4, size - 4);
                    using (var p = Th.Round(r, size * 0.22f))
                    using (var b = new LinearGradientBrush(r, Th.Violet, Th.Cyan, 45f)) g.FillPath(b, p);
                    // Blitz
                    float s = size / 64f;
                    var bolt = new[] { new PointF(36 * s, 10 * s), new PointF(18 * s, 36 * s), new PointF(30 * s, 36 * s), new PointF(26 * s, 54 * s), new PointF(46 * s, 26 * s), new PointF(34 * s, 26 * s) };
                    using (var b = new SolidBrush(Color.White)) g.FillPolygon(b, bolt);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }
}
