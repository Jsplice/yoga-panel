// Diagramme, Akku-Ring und Programmliste für YogaPanel.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace YogaTool.Panel
{
    static class Fmt
    {
        public static readonly CultureInfo DE = CultureInfo.GetCultureInfo("de-DE");
        public static string N(double v, int d) { return double.IsNaN(v) ? "–" : v.ToString("N" + d, DE); }
        public static string Dur(double sec)
        {
            if (sec <= 0 || double.IsNaN(sec) || double.IsInfinity(sec)) return "?";
            var t = TimeSpan.FromSeconds(sec);
            return (int)t.TotalHours + ":" + t.Minutes.ToString("00") + " h";
        }
    }

    class Pt { public DateTime T; public float[] V; }

    // Verlauf: Rohwerte (letzte ~20 min) + Minutenmittel (48 h, wird gespeichert). Zeiten in UTC.
    class History
    {
        public const int Cores = 0, GpuW = 1, Rest = 2, Dram = 3, BatW = 4, Cpu = 5, GpuU = 6, Temp = 7, GHz = 8,
            MemPct = 9, CommitPct = 10, PageFilePct = 11, Paging = 12, MemGB = 13, PagingMB = 14, Channels = 15;
        const int RawCap = 2400, MinCap = 2880;
        readonly Pt[] raw = new Pt[RawCap]; int rawHead, rawCount;
        readonly Pt[] mins = new Pt[MinCap]; int minHead, minCount;
        DateTime curMin = DateTime.MinValue;
        readonly double[] sum = new double[Channels]; readonly int[] cnt = new int[Channels];
        public double IntervalSec = 1;

        public int Count { get { return rawCount; } }
        public Pt Last { get { return rawCount > 0 ? raw[(rawHead - 1 + RawCap) % RawCap] : null; } }

        public void Push(DateTime t, float[] v)
        {
            raw[rawHead] = new Pt { T = t, V = v }; rawHead = (rawHead + 1) % RawCap; if (rawCount < RawCap) rawCount++;
            var m = new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, DateTimeKind.Utc);
            if (m != curMin) { FlushMinute(); curMin = m; }
            for (int c = 0; c < Channels; c++) if (!float.IsNaN(v[c])) { sum[c] += v[c]; cnt[c]++; }
        }

        void FlushMinute()
        {
            if (curMin == DateTime.MinValue) return;
            var v = new float[Channels]; bool any = false;
            for (int c = 0; c < Channels; c++)
            {
                v[c] = cnt[c] > 0 ? (float)(sum[c] / cnt[c]) : float.NaN;
                if (cnt[c] > 0) any = true;
                sum[c] = 0; cnt[c] = 0;
            }
            if (any) AddMin(new Pt { T = curMin.AddSeconds(30), V = v });
        }
        void AddMin(Pt p) { mins[minHead] = p; minHead = (minHead + 1) % MinCap; if (minCount < MinCap) minCount++; }

        IEnumerable<Pt> Ring(Pt[] a, int head, int count, int cap)
        {
            for (int i = 0; i < count; i++) yield return a[((head - count + i) % cap + cap) % cap];
        }

        public static bool UseMinutes(double rangeSec) { return rangeSec > 1800; }

        public List<Pt> Window(DateTime now, double rangeSec)
        {
            var from = now.AddSeconds(-rangeSec);
            var res = new List<Pt>();
            if (!UseMinutes(rangeSec))
            {
                foreach (var p in Ring(raw, rawHead, rawCount, RawCap)) if (p.T >= from) res.Add(p);
                return res;
            }
            foreach (var p in Ring(mins, minHead, minCount, MinCap)) if (p.T >= from) res.Add(p);
            // laufende Minute live anhängen
            foreach (var p in Ring(raw, rawHead, rawCount, RawCap)) if (p.T >= curMin) res.Add(p);
            return res;
        }

        // Abstand, ab dem eine Lücke (Standby, App zu) als Lücke gezeichnet wird
        public double GapLimit(double rangeSec) { return UseMinutes(rangeSec) ? 150 : Math.Max(IntervalSec, 2.0) * 3.2; }

        public void Save(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string tmp = path + ".tmp";
                using (var w = new BinaryWriter(File.Create(tmp)))
                {
                    var list = Ring(mins, minHead, minCount, MinCap).ToList();
                    w.Write(1); w.Write(Channels); w.Write(list.Count);
                    foreach (var p in list) { w.Write(p.T.Ticks); for (int c = 0; c < Channels; c++) w.Write(p.V[c]); }
                }
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch { }
        }

        public void Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                var cutoff = DateTime.UtcNow.AddHours(-48);
                using (var r = new BinaryReader(File.OpenRead(path)))
                {
                    if (r.ReadInt32() != 1) return;
                    int ch = r.ReadInt32(), n = r.ReadInt32();
                    for (int i = 0; i < n; i++)
                    {
                        var t = new DateTime(r.ReadInt64(), DateTimeKind.Utc);
                        var v = new float[Channels];
                        for (int c = 0; c < ch; c++) { float f = r.ReadSingle(); if (c < Channels) v[c] = f; }
                        for (int c = ch; c < Channels; c++) v[c] = float.NaN;
                        if (t >= cutoff && t <= DateTime.UtcNow) AddMin(new Pt { T = t, V = v });
                    }
                }
            }
            catch { }
        }
    }

    class Series
    {
        public string Name; public int Ch; public Color Color; public bool Stack, Dashed, Fill = true; public string Unit = "";
        public bool Hidden;     // nicht zeichnen, nur im Tooltip zeigen (andere Einheit)
        public bool Strip;      // als Balken in eigenem Streifen unter dem Diagramm (eigene Skala)
        public int Decimals = -1;
        public Series(string name, int ch, Color c) { Name = name; Ch = ch; Color = c; }
    }

    class ChartCard : CardBase
    {
        public string Title = "", Big = "", Sub = "";
        public Color BigColor = Th.Text;
        public List<Series> Series = new List<Series>();
        public History H;
        public Func<double> Range = () => 300;
        public float FixedMax; // 0 = automatisch
        public float MinMax = 1;
        public string Unit = "";
        public int Decimals = 1;
        public int PlotTop = 86;   // logische Pixel
        public bool SubBelow;      // Untertitel unter statt neben dem großen Wert
        public float TickStep;     // feste Rasterschritte (z. B. 8 GB), oberste Linie = Maximum
        public float StripMin = 1; // Mindest-Skala des Balkenstreifens
        public float StripFrac = 0.3f;
        int hoverX = -1;

        protected override void OnMouseMove(MouseEventArgs e) { hoverX = e.X; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hoverX = -1; Invalidate(); base.OnMouseLeave(e); }

        Rectangle Plot
        {
            get
            {
                int top = Th.P(PlotTop), left = Th.P(16) + Th.P(30);
                return new Rectangle(left, top, Width - left - Th.P(16), Height - top - Th.P(26));
            }
        }

        static string Span(double sec)
        {
            if (sec >= 3600) return Fmt.N(sec / 3600, sec % 3600 == 0 ? 0 : 1) + " h";
            if (sec >= 60) return Fmt.N(sec / 60, 0) + " min";
            return Fmt.N(sec, 0) + " s";
        }
        static string Ago(double sec)
        {
            if (sec < 1.5) return "jetzt";
            if (sec < 60) return "vor " + (int)sec + " s";
            var t = TimeSpan.FromSeconds(sec);
            return t.TotalHours >= 1 ? "vor " + (int)t.TotalHours + ":" + t.Minutes.ToString("00") + " h" : "vor " + t.Minutes + ":" + t.Seconds.ToString("00") + " min";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; DrawCard(g);
            int x0 = Th.P(16);
            Th.Txt(g, Title.ToUpperInvariant(), Th.Semi(8f), Th.Dim, x0, Th.P(14));
            Th.Txt(g, Big, Th.F(20f, FontStyle.Bold), BigColor, x0 - Th.P(1), Th.P(30));
            if (!string.IsNullOrEmpty(Sub))
            {
                if (SubBelow) Th.Txt(g, Sub, Th.F(8.5f), Th.Dim, x0, Th.P(68));
                else
                {
                    int bw = Th.W(g, Big, Th.F(20f, FontStyle.Bold));
                    Th.Txt(g, Sub, Th.F(9f), Th.Dim, x0 + bw + Th.P(8), Th.P(44));
                }
            }
            var last = H.Last;
            // Legende rechts oben (schmale Karten: nur Namen)
            bool compact = Width < Th.P(420);
            int lx = Width - Th.P(16);
            for (int k = Series.Count - 1; k >= 0; k--)
            {
                var s = Series[k];
                if (s.Hidden) continue;
                float lv = last != null ? last.V[s.Ch] : float.NaN;
                if (s.Dashed && float.IsNaN(lv)) continue; // z. B. Akku im Netzbetrieb
                string cur = compact ? "" : last != null ? FormatV(lv, s) : "–";
                string label = s.Name + "  " + cur;
                int w = Th.W(g, label, Th.F(8.5f));
                lx -= w;
                Th.Txt(g, s.Name + "  ", Th.F(8.5f), Th.Dim, lx, Th.P(16));
                Th.Txt(g, cur, Th.F(8.5f), Th.Text, lx + Th.W(g, s.Name + "  ", Th.F(8.5f)), Th.P(16));
                float dd = Th.Pf(8);
                using (var b = new SolidBrush(s.Color)) g.FillEllipse(b, lx - dd - Th.Pf(6), Th.P(16) + Th.Pf(4), dd, dd);
                lx -= Th.P(26);
            }

            var pr = Plot;
            if (pr.Width < 20 || pr.Height < 20) return;
            double range = Math.Max(10, Range());
            var now = DateTime.UtcNow;
            var pts = H.Window(now, range);
            double gap = H.GapLimit(range);
            var stripS = Series.FirstOrDefault(x => x.Strip);
            Rectangle strip = Rectangle.Empty;
            if (stripS != null)
            {
                int sh = (int)(pr.Height * StripFrac);
                strip = new Rectangle(pr.Left, pr.Bottom - sh, pr.Width, sh);
                pr = new Rectangle(pr.Left, pr.Top, pr.Width, pr.Height - sh - Th.P(12));
            }
            int axisBottom = strip.IsEmpty ? pr.Bottom : strip.Bottom;

            // Skala
            float max = FixedMax;
            if (max <= 0)
            {
                float m = MinMax;
                foreach (var p in pts)
                {
                    float stack = 0;
                    foreach (var s in Series)
                    {
                        float v = p.V[s.Ch]; if (float.IsNaN(v) || s.Hidden || s.Strip) continue;
                        if (s.Stack) stack += Math.Max(0, v); else m = Math.Max(m, v);
                    }
                    m = Math.Max(m, stack);
                }
                max = NiceStep(m * 1.1f / 4) * 4;
            }

            using (var pen = new Pen(Th.A(Th.Faint, 140), 1) { DashStyle = DashStyle.Dot })
            {
                var ticks = new List<float>();
                if (TickStep > 0) { for (float v = 0; v < max - TickStep * 0.35f; v += TickStep) ticks.Add(v); ticks.Add(max); }
                else for (int k = 0; k <= 4; k++) ticks.Add(max * k / 4f);
                foreach (float v in ticks)
                {
                    int y = pr.Bottom - (int)(pr.Height * v / max);
                    if (v > 0) g.DrawLine(pen, pr.Left, y, pr.Right, y);
                    string lab = v.ToString(Math.Abs(v - Math.Round(v)) < 0.05 ? "0" : "0.#", Fmt.DE);
                    Th.TxtR(g, lab, Th.F(7.5f), Th.Dim, pr.Left - Th.P(8), y - Th.P(7));
                }
            }
            using (var pen = new Pen(Th.Faint, 1)) g.DrawLine(pen, pr.Left, pr.Bottom, pr.Right, pr.Bottom);
            // Zeitachse: Anfang, Mitte (Uhrzeit bei langen Zeiträumen), jetzt
            Th.Txt(g, "−" + Span(range), Th.F(7.5f), Th.Dim, pr.Left, axisBottom + Th.P(6));
            if (range >= 3600)
            {
                string mid = now.AddSeconds(-range / 2).ToLocalTime().ToString("HH:mm");
                int mw = Th.W(g, mid, Th.F(7.5f));
                Th.Txt(g, mid, Th.F(7.5f), Th.Dim, pr.Left + pr.Width / 2 - mw / 2, axisBottom + Th.P(6));
                using (var pen = new Pen(Th.Faint, 1)) g.DrawLine(pen, pr.Left + pr.Width / 2, axisBottom, pr.Left + pr.Width / 2, axisBottom + Th.P(3));
            }
            Th.TxtR(g, "jetzt", Th.F(7.5f), Th.Dim, pr.Right, axisBottom + Th.P(6));
            if (stripS != null) DrawStrip(g, stripS, strip, pts, now, range);
            if (pts.Count < 2) { Th.TxtC(g, History.UseMinutes(range) ? "Noch keine Daten in diesem Zeitraum – der Verlauf füllt sich minütlich" : "Sammle Messwerte …", Th.F(9f), Th.Dim, pr); return; }

            Func<DateTime, float> X = t => (float)(pr.Right - (now - t).TotalSeconds / range * pr.Width);
            Func<float, float> Y = v => pr.Bottom - Math.Min(1.05f, v / max) * pr.Height;

            // zusammenhängende Abschnitte (Lücken durch Standby / App geschlossen)
            var segs = new List<List<Pt>>();
            List<Pt> curSeg = null;
            for (int i = 0; i < pts.Count; i++)
            {
                if (curSeg == null || (pts[i].T - pts[i - 1].T).TotalSeconds > gap) { curSeg = new List<Pt>(); segs.Add(curSeg); }
                curSeg.Add(pts[i]);
            }

            var oldClip = g.Clip;
            g.SetClip(new Rectangle(pr.Left, pr.Top - Th.P(4), pr.Width + 1, pr.Height + Th.P(4)));
            var stacked = Series.Where(x => x.Stack).ToList();
            foreach (var seg in segs)
            {
                if (seg.Count < 2) continue;
                int n = seg.Count;
                // gestapelte Flächen
                var lower = new float[n];
                var tops = new List<KeyValuePair<Color, PointF[]>>();
                foreach (var s in stacked)
                {
                    var top = new PointF[n]; var bot = new PointF[n];
                    for (int j = 0; j < n; j++)
                    {
                        float v = seg[j].V[s.Ch]; if (float.IsNaN(v) || v < 0) v = 0;
                        float x = X(seg[j].T);
                        bot[j] = new PointF(x, Y(lower[j]));
                        lower[j] += v;
                        top[j] = new PointF(x, Y(lower[j]));
                    }
                    var poly = top.Concat(bot.Reverse()).ToArray();
                    using (var b = new LinearGradientBrush(new RectangleF(pr.Left, pr.Top, pr.Width, pr.Height + 1), Th.A(s.Color, 190), Th.A(s.Color, 90), LinearGradientMode.Vertical))
                        g.FillPolygon(b, poly);
                    tops.Add(new KeyValuePair<Color, PointF[]>(s.Color, top));
                }
                // Kanten von oben nach unten, damit dünne Schichten (GPU 0 W) die untere nicht verdecken
                for (int k = tops.Count - 1; k >= 0; k--)
                    using (var pen = new Pen(tops[k].Key, Th.Pf(1.6f)) { LineJoin = LineJoin.Round })
                        g.DrawLines(pen, tops[k].Value);
                // Linien
                foreach (var s in Series.Where(x => !x.Stack && !x.Hidden && !x.Strip))
                {
                    var line = new List<PointF>();
                    Action flush = () =>
                    {
                        if (line.Count >= 2)
                        {
                            if (s.Fill && !s.Dashed)
                            {
                                var poly = new List<PointF>(line); poly.Add(new PointF(line[line.Count - 1].X, pr.Bottom)); poly.Add(new PointF(line[0].X, pr.Bottom));
                                using (var b = new LinearGradientBrush(new RectangleF(pr.Left, pr.Top - 1, pr.Width, pr.Height + 2), Th.A(s.Color, 95), Th.A(s.Color, 0), LinearGradientMode.Vertical))
                                    g.FillPolygon(b, poly.ToArray());
                            }
                            using (var pen = new Pen(s.Color, Th.Pf(s.Dashed ? 1.6f : 2.1f)) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
                            {
                                if (s.Dashed) pen.DashPattern = new[] { 3f, 2f };
                                g.DrawLines(pen, line.ToArray());
                            }
                        }
                        line.Clear();
                    };
                    foreach (var p in seg)
                    {
                        float v = p.V[s.Ch];
                        if (float.IsNaN(v)) { flush(); continue; }
                        line.Add(new PointF(X(p.T), Y(v)));
                    }
                    flush();
                }
            }
            // Endpunkt-Leuchten (nur wenn aktuell)
            var lp = pts[pts.Count - 1];
            if ((now - lp.T).TotalSeconds <= gap)
                foreach (var s in Series)
                {
                    float v = lp.V[s.Ch]; if (float.IsNaN(v) || s.Hidden || s.Strip) continue;
                    float yv = s.Stack ? Y(StackTop(s, lp)) : Y(v);
                    float xv = X(lp.T);
                    float r1 = Th.Pf(7), r2 = Th.Pf(3.2f);
                    using (var b = new SolidBrush(Th.A(s.Color, 60))) g.FillEllipse(b, xv - r1, yv - r1, r1 * 2, r1 * 2);
                    using (var b = new SolidBrush(s.Color)) g.FillEllipse(b, xv - r2, yv - r2, r2 * 2, r2 * 2);
                }
            g.Clip = oldClip;

            // Hover: nächster Messpunkt
            if (hoverX >= pr.Left && hoverX <= pr.Right)
            {
                Pt hp = null; float best = float.MaxValue;
                foreach (var p in pts) { float d = Math.Abs(X(p.T) - hoverX); if (d < best) { best = d; hp = p; } }
                if (hp != null && best <= Math.Max(Th.Pf(12), pr.Width / 60f))
                {
                    float hx = X(hp.T);
                    using (var pen = new Pen(Th.A(Color.White, 70), 1)) g.DrawLine(pen, hx, pr.Top, hx, axisBottom);
                    var lines = new List<KeyValuePair<Series, string>>();
                    foreach (var s in Series)
                    {
                        float v = hp.V[s.Ch];
                        if (float.IsNaN(v)) continue;
                        if (!s.Hidden && !s.Strip)
                        {
                            float yv = s.Stack ? Y(StackTop(s, hp)) : Y(v);
                            float r = Th.Pf(3.5f);
                            using (var b = new SolidBrush(s.Color)) g.FillEllipse(b, hx - r, yv - r, r * 2, r * 2);
                        }
                        lines.Add(new KeyValuePair<Series, string>(s, FormatV(v, s)));
                    }
                    double ago = (now - hp.T).TotalSeconds;
                    string head = hp.T.ToLocalTime().ToString(History.UseMinutes(range) ? "HH:mm" : "HH:mm:ss") + " · " + Ago(ago) + (History.UseMinutes(range) ? " · Ø 1 min" : "");
                    int lh = Th.P(18), bw = Math.Max(Th.P(160), Th.W(g, head, Th.F(8f)) + Th.P(20)), bh = Th.P(14) + lh * (lines.Count + 1);
                    int bx = hx + Th.P(12) + bw > pr.Right ? (int)hx - Th.P(12) - bw : (int)hx + Th.P(12);
                    var box = new Rectangle(bx, pr.Top + Th.P(4), bw, bh);
                    using (var p = Th.Round(box, Th.Pf(8)))
                    {
                        using (var b = new SolidBrush(Color.FromArgb(235, 14, 14, 18))) g.FillPath(b, p);
                        using (var pen = new Pen(Th.Border)) g.DrawPath(pen, p);
                    }
                    Th.Txt(g, head, Th.F(8f), Th.Dim, box.X + Th.P(10), box.Y + Th.P(7));
                    for (int k = 0; k < lines.Count; k++)
                    {
                        int y = box.Y + Th.P(7) + lh * (k + 1);
                        using (var b = new SolidBrush(lines[k].Key.Color)) g.FillEllipse(b, box.X + Th.P(10), y + Th.P(4), Th.P(7), Th.P(7));
                        Th.Txt(g, lines[k].Key.Name, Th.F(8.5f), Th.Text, box.X + Th.P(22), y);
                        Th.TxtR(g, lines[k].Value, Th.Semi(8.5f), Th.Text, box.Right - Th.P(10), y);
                    }
                }
            }
        }

        // Balkenstreifen mit eigener Skala (z. B. Paging in MB/s)
        void DrawStrip(Graphics g, Series s, Rectangle r, List<Pt> pts, DateTime now, double range)
        {
            float smax = StripMin;
            foreach (var p in pts) { float v = p.V[s.Ch]; if (!float.IsNaN(v)) smax = Math.Max(smax, v); }
            smax = NiceStep(smax * 1.1f);
            using (var b = new SolidBrush(Th.A(Color.White, 6))) g.FillRectangle(b, r);
            using (var pen = new Pen(Th.Faint, 1)) g.DrawLine(pen, r.Left, r.Bottom, r.Right, r.Bottom);
            Th.Txt(g, s.Name.ToUpperInvariant(), Th.Semi(7f), Th.Dim, r.Left + Th.P(6), r.Top + Th.P(3));
            string ml = smax.ToString(smax >= 10 ? "0" : "0.#", Fmt.DE);
            Th.TxtR(g, ml, Th.F(7.5f), Th.Dim, r.Left - Th.P(8), r.Top - Th.P(2));
            Th.TxtR(g, s.Unit, Th.F(7f), Th.Dim, r.Left - Th.P(8), r.Top + Th.P(12));
            float prevX = float.NaN;
            var oldClip = g.Clip; g.SetClip(r);
            using (var br = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), s.Color, Th.A(s.Color, 110), LinearGradientMode.Vertical))
                foreach (var p in pts)
                {
                    float v = p.V[s.Ch];
                    float x = (float)(r.Right - (now - p.T).TotalSeconds / range * r.Width);
                    float bw = float.IsNaN(prevX) ? Th.Pf(2) : Math.Max(1f, Math.Min(Th.Pf(6), x - prevX - 1));
                    prevX = x;
                    if (float.IsNaN(v) || v <= 0) continue;
                    float h = Math.Max(1f, v / smax * (r.Height - Th.P(2)));
                    g.FillRectangle(br, x - bw, r.Bottom - h, bw, h);
                }
            g.Clip = oldClip;
        }

        float StackTop(Series upto, Pt p)
        {
            float sum = 0;
            foreach (var s in Series.Where(x => x.Stack))
            {
                float v = p.V[s.Ch]; if (!float.IsNaN(v) && v > 0) sum += v;
                if (s == upto) break;
            }
            return sum;
        }

        string FormatV(float v, Series s) { return float.IsNaN(v) ? "–" : Fmt.N(v, s.Decimals >= 0 ? s.Decimals : Decimals) + " " + (s.Unit != "" ? s.Unit : Unit); }

        static float NiceStep(float raw)
        {
            if (raw <= 0) return 1;
            float exp = (float)Math.Pow(10, Math.Floor(Math.Log10(raw)));
            float f = raw / exp;
            float nf = f <= 1 ? 1 : f <= 2 ? 2 : f <= 2.5f ? 2.5f : f <= 5 ? 5 : 10;
            return nf * exp;
        }
    }

    // Akku als Ring mit Detailzeilen.
    class BatteryCard : CardBase
    {
        public BatteryInfo B;
        public string ChargeMode = "";
        float shown = -1;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; DrawCard(g);
            if (B == null || !B.Present) { Th.TxtC(g, "Kein Akku gefunden", Th.F(10f), Th.Dim, ClientRectangle); return; }
            float pct = (float)Math.Max(0, Math.Min(100, B.Percent));
            if (shown < 0) shown = pct;
            int d = Height - Th.P(36);
            var r = new RectangleF(Th.P(20), (Height - d) / 2f, d, d);
            float thick = Th.Pf(9);
            Color c = B.Charging ? Th.Cyan : pct <= 15 ? Th.Red : pct <= 35 ? Th.Yellow : Th.Green;
            using (var pen = new Pen(Th.Track, thick) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(pen, r, -90, 360);
            if (pct > 0.5f)
                using (var pen = new Pen(c, thick) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(pen, r, -90, Math.Max(2, 360 * pct / 100));
            Th.TxtC(g, Fmt.N(pct, 0) + "%", Th.F(15f, FontStyle.Bold), Th.Text, Rectangle.Round(r));

            int x = (int)r.Right + Th.P(22), y = Th.P(18);
            Th.Txt(g, "AKKU", Th.Semi(8f), Th.Dim, x, y);
            string state; Color sc = Th.Text;
            if (B.Charging) { state = "Lädt · " + Fmt.N(B.Rate / 1000.0, 1) + " W"; sc = Th.Cyan; }
            else if (B.Discharging) { state = "Entlädt · " + Fmt.N(-B.Rate / 1000.0, 1) + " W"; sc = Th.Yellow; }
            else state = B.OnAc ? "Netzbetrieb" : "–";
            Th.Txt(g, state, Th.Semi(12f), sc, x, y + Th.P(18));
            string eta = "";
            if (B.Discharging && B.Rate < 0) eta = "noch ca. " + Fmt.Dur(B.Remaining * 3600.0 / -B.Rate);
            else if (B.Charging && B.Rate > 0 && B.FullCharged > B.Remaining) eta = "voll in ca. " + Fmt.Dur((B.FullCharged - B.Remaining) * 3600.0 / B.Rate);
            else if (B.OnAc) eta = "lädt nicht · " + ChargeMode;
            Th.Txt(g, eta, Th.F(9f), Th.Dim, x, y + Th.P(44));
            Th.Txt(g, "Gesundheit " + Fmt.N(B.Health, 1) + " % · " + B.CycleCount + " Zyklen · " + Fmt.N(B.FullCharged / 1000.0, 1) + "/" + Fmt.N(B.Designed / 1000.0, 1) + " Wh", Th.F(8.5f), Th.Dim, x, y + Th.P(64));
        }
    }

    class AppEnergy { public string Name; public double Wh, Cpu, Disp, Net, Disk, Rest; }

    // Programmliste: live oder Windows-Energiedaten der letzten 24 h.
    class ProcCard : CardBase
    {
        public List<ProcRow> Rows = new List<ProcRow>();
        public int Tab; // 0 live, 1 24h
        public string AppsState = "idle"; // idle, loading, done, error
        public string AppsError = "";
        public List<AppEnergy> Apps = new List<AppEnergy>();
        public double HwPkg = double.NaN, HwSpan = double.NaN;
        public event Action<int> TabChanged;
        public event Action LoadApps;
        int hoverRow = -1, hoverTab = -1;
        Rectangle[] tabs = new Rectangle[2];

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int ht = -1;
            for (int i = 0; i < 2; i++) if (tabs[i].Contains(e.Location)) ht = i;
            int hr = e.Y > Th.P(72) ? (e.Y - Th.P(72)) / Th.P(27) : -1;
            if (ht != hoverTab || hr != hoverRow) { hoverTab = ht; hoverRow = hr; Invalidate(); }
            Cursor = ht >= 0 || (Tab == 1 && AppsState != "loading" && AppsState != "done" && e.Y > Th.P(72)) ? Cursors.Hand : Cursors.Default;
        }
        protected override void OnMouseLeave(EventArgs e) { hoverTab = hoverRow = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            for (int i = 0; i < 2; i++)
                if (tabs[i].Contains(e.Location) && Tab != i) { Tab = i; Invalidate(); if (TabChanged != null) TabChanged(i); return; }
            if (Tab == 1 && (AppsState == "idle" || AppsState == "error" || (AppsState == "done" && e.Y < Th.P(72) && e.X < Width / 2)) && LoadApps != null) LoadApps();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; DrawCard(g);
            int x0 = Th.P(16), right = Width - Th.P(16);
            Th.Txt(g, "PROGRAMME", Th.Semi(8f), Th.Dim, x0, Th.P(14));
            Th.Txt(g, Tab == 0 ? "Wer gerade Strom zieht" : "Verbrauch der letzten 24 Stunden", Th.Semi(11.5f), Th.Text, x0, Th.P(32));

            // Tabs
            string[] names = { "Live", "24 h" };
            int tw = Th.P(64), th = Th.P(28), tx = right - tw * 2;
            using (var p = Th.Round(new RectangleF(tx, Th.P(16), tw * 2, th), Th.Pf(8))) using (var b = new SolidBrush(Th.Track)) g.FillPath(b, p);
            for (int i = 0; i < 2; i++)
            {
                tabs[i] = new Rectangle(tx + i * tw, Th.P(16), tw, th);
                if (i == Tab)
                    using (var p = Th.Round(RectangleF.Inflate(tabs[i], -Th.Pf(3), -Th.Pf(3)), Th.Pf(6))) using (var b = new SolidBrush(Th.Violet)) g.FillPath(b, p);
                Th.TxtC(g, names[i], i == Tab ? Th.Semi(8.5f) : Th.F(8.5f), i == Tab ? Color.FromArgb(18, 18, 24) : (i == hoverTab ? Th.Text : Th.Dim), tabs[i]);
            }

            int y = Th.P(72), rh = Th.P(27);
            int nameW = (int)((right - x0) * 0.30), barX = x0 + nameW, valW = Th.P(64);
            int colW = Th.P(54);
            int barW = right - barX - valW - colW * 3 - Th.P(8);
            if (Tab == 0)
            {
                Header(g, x0, y - Th.P(18), barX, barW, valW, colW, right, "≈ Watt", new[] { "CPU", "GPU", "RAM" });
                var list = Rows.OrderByDescending(r => r.Watt).ThenByDescending(r => r.Cpu).Take(Math.Max(1, (Height - y - Th.P(10)) / rh)).ToList();
                double max = Math.Max(0.5, list.Count > 0 ? list.Max(r => r.Watt) : 1);
                for (int i = 0; i < list.Count; i++)
                {
                    var r = list[i]; int ry = y + i * rh;
                    RowBg(g, i, ry, rh);
                    Th.TxtL(g, r.Name + (r.Count > 1 ? "  ×" + r.Count : ""), Th.F(9f), Th.Text, new Rectangle(x0, ry, nameW - Th.P(8), rh));
                    DrawBar(g, barX, ry, barW, rh, (float)(r.Watt / max), Th.Violet, Th.Cyan);
                    Th.TxtR(g, Fmt.N(r.Watt, 2) + " W", Th.Semi(8.5f), Th.Text, barX + barW + valW, ry + (rh - Th.H(Th.Semi(8.5f))) / 2);
                    Cell(g, Fmt.N(r.Cpu, 1) + "%", barX + barW + valW + colW, ry, rh, r.Cpu > 10);
                    Cell(g, Fmt.N(r.Gpu, 1) + "%", barX + barW + valW + colW * 2, ry, rh, r.Gpu > 10);
                    Cell(g, r.MemMB >= 1024 ? Fmt.N(r.MemMB / 1024, 1) + " G" : Fmt.N(r.MemMB, 0) + " M", barX + barW + valW + colW * 3, ry, rh, false);
                }
                Th.Txt(g, "≈ Watt: gemessene Kern-/GPU-Leistung, anteilig nach Auslastung verteilt", Th.F(7.5f), Th.Faint, x0, Height - Th.P(20));
                return;
            }
            // 24h
            if (AppsState != "done")
            {
                string msg = AppsState == "loading" ? "Lese Windows-Energiedaten …" : AppsState == "error" ? "Fehler: " + AppsError + " – klicken für neuen Versuch" : "Klicken, um die Energiedaten der letzten 24 h zu laden";
                string sub = AppsState == "loading" ? "" : "Windows speichert den Verbrauch je App. Zum Auslesen sind einmal Admin-Rechte nötig (UAC).";
                var area = new Rectangle(x0, y, right - x0, Height - y - Th.P(20));
                Th.TxtC(g, msg, Th.Semi(10f), AppsState == "error" ? Th.Red : Th.Text, new Rectangle(area.X, area.Y + area.Height / 2 - Th.P(24), area.Width, Th.P(24)));
                Th.TxtC(g, sub, Th.F(8.5f), Th.Dim, new Rectangle(area.X, area.Y + area.Height / 2 + Th.P(2), area.Width, Th.P(20)));
                return;
            }
            Header(g, x0, y - Th.P(18), barX, barW, valW, colW, right, "Wh", new[] { "CPU", "Display", "Netz" });
            if (!double.IsNaN(HwPkg))
            {
                string s = "Chip gemessen: " + Fmt.N(HwPkg, 1) + " Wh" + (!double.IsNaN(HwSpan) && HwSpan > 0 ? " · Ø " + Fmt.N(HwPkg / HwSpan, 1) + " W" : "") + "   ·   neu laden";
                Th.TxtR(g, s, Th.F(8f), Th.Dim, right, Th.P(52) - Th.P(1));
            }
            double total = Apps.Sum(a => a.Wh), amax = Math.Max(0.001, Apps.Count > 0 ? Apps.Max(a => a.Wh) : 1);
            var shown = Apps.Take(Math.Max(1, (Height - y - Th.P(10)) / rh)).ToList();
            for (int i = 0; i < shown.Count; i++)
            {
                var a = shown[i]; int ry = y + i * rh;
                RowBg(g, i, ry, rh);
                Th.TxtL(g, a.Name, Th.F(9f), Th.Text, new Rectangle(x0, ry, nameW - Th.P(8), rh));
                DrawBar(g, barX, ry, barW, rh, (float)(a.Wh / amax), Th.Orange, Th.Pink);
                Th.TxtR(g, Fmt.N(a.Wh, a.Wh >= 10 ? 0 : 1), Th.Semi(8.5f), Th.Text, barX + barW + valW, ry + (rh - Th.H(Th.Semi(8.5f))) / 2);
                Cell(g, Fmt.N(a.Cpu * 100, 0) + "%", barX + barW + valW + colW, ry, rh, false);
                Cell(g, Fmt.N(a.Disp * 100, 0) + "%", barX + barW + valW + colW * 2, ry, rh, false);
                Cell(g, Fmt.N(a.Net * 100, 0) + "%", barX + barW + valW + colW * 3, ry, rh, false);
            }
        }

        void Header(Graphics g, int x0, int y, int barX, int barW, int valW, int colW, int right, string val, string[] cols)
        {
            var f = Th.F(7.5f);
            Th.Txt(g, "NAME", f, Th.Dim, x0, y);
            Th.TxtR(g, val.ToUpperInvariant(), f, Th.Dim, barX + barW + valW, y);
            for (int i = 0; i < cols.Length; i++) Th.TxtR(g, cols[i].ToUpperInvariant(), f, Th.Dim, barX + barW + valW + colW * (i + 1), y);
        }
        void RowBg(Graphics g, int i, int ry, int rh)
        {
            if (i != hoverRow) return;
            using (var p = Th.Round(new RectangleF(Th.P(8), ry + 1, Width - Th.P(16), rh - 2), Th.Pf(6))) using (var b = new SolidBrush(Th.A(Color.White, 10))) g.FillPath(b, p);
        }
        void DrawBar(Graphics g, int x, int ry, int w, int rh, float t, Color a, Color b)
        {
            int bh = Th.P(6); float by = ry + (rh - bh) / 2f;
            using (var p = Th.Round(new RectangleF(x, by, w, bh), bh / 2f)) using (var br = new SolidBrush(Th.Track)) g.FillPath(br, p);
            float fw = Math.Max(bh, w * Math.Max(0, Math.Min(1, t)));
            if (t <= 0.001f) return;
            using (var p = Th.Round(new RectangleF(x, by, fw, bh), bh / 2f))
            using (var br = new LinearGradientBrush(new RectangleF(x, by, Math.Max(1, w), bh), a, b, LinearGradientMode.Horizontal)) g.FillPath(br, p);
        }
        void Cell(Graphics g, string s, int right, int ry, int rh, bool hi)
        {
            var f = Th.F(8.5f);
            Th.TxtR(g, s, f, hi ? Th.Text : Th.Dim, right, ry + (rh - Th.H(f)) / 2);
        }
    }
}
