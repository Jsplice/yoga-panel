// Theme und selbst gezeichnete Bedienelemente für YogaPanel.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace YogaTool.Panel
{
    static class Th
    {
        public static float S = 1f;
        public static readonly Color Bg = Color.FromArgb(16, 16, 20);
        public static readonly Color Card = Color.FromArgb(26, 26, 32);
        public static readonly Color Border = Color.FromArgb(38, 38, 47);
        public static readonly Color Track = Color.FromArgb(38, 38, 47);
        public static readonly Color Text = Color.FromArgb(236, 236, 242);
        public static readonly Color Dim = Color.FromArgb(142, 142, 158);
        public static readonly Color Faint = Color.FromArgb(58, 58, 70);
        public static readonly Color Violet = Color.FromArgb(142, 124, 255);
        public static readonly Color Cyan = Color.FromArgb(64, 200, 255);
        public static readonly Color Orange = Color.FromArgb(255, 168, 76);
        public static readonly Color Pink = Color.FromArgb(255, 99, 146);
        public static readonly Color Green = Color.FromArgb(72, 220, 150);
        public static readonly Color Yellow = Color.FromArgb(255, 206, 84);
        public static readonly Color Red = Color.FromArgb(255, 92, 92);
        public static readonly Color Slate = Color.FromArgb(120, 130, 160);

        static string family;
        static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();

        public static string Family
        {
            get
            {
                if (family == null)
                {
                    family = "Segoe UI";
                    using (var fc = new InstalledFontCollection())
                        foreach (var f in fc.Families)
                            if (f.Name == "Segoe UI Variable Text") { family = f.Name; break; }
                }
                return family;
            }
        }

        public static Font F(float pt, FontStyle st = FontStyle.Regular)
        {
            string key = pt + "|" + (int)st;
            Font f;
            if (!fonts.TryGetValue(key, out f))
            {
                string fam = Family;
                if (st == FontStyle.Bold && fam.StartsWith("Segoe UI Variable")) fam = "Segoe UI Variable Display";
                fonts[key] = f = new Font(fam, pt, st, GraphicsUnit.Point);
            }
            return f;
        }
        public static Font Semi(float pt)
        {
            string key = pt + "|semi";
            Font f;
            if (!fonts.TryGetValue(key, out f))
            {
                string fam = Family == "Segoe UI" ? "Segoe UI Semibold" : "Segoe UI Variable Text Semibold";
                f = new Font(fam, pt, FontStyle.Regular, GraphicsUnit.Point);
                if (f.Name != fam) f = new Font(Family, pt, FontStyle.Bold, GraphicsUnit.Point);
                fonts[key] = f;
            }
            return f;
        }

        public static int P(float v) { return (int)Math.Round(v * S); }
        public static float Pf(float v) { return v * S; }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Hq(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }

        public static Color A(Color c, int a) { return Color.FromArgb(Math.Max(0, Math.Min(255, a)), c); }
        public static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        const TextFormatFlags Base = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        public static void Txt(Graphics g, string s, Font f, Color c, int x, int y)
        {
            TextRenderer.DrawText(g, s, f, new Point(x, y), c, Base);
        }
        public static void TxtR(Graphics g, string s, Font f, Color c, int right, int y)
        {
            var sz = TextRenderer.MeasureText(g, s, f, Size.Empty, Base);
            TextRenderer.DrawText(g, s, f, new Point(right - sz.Width, y), c, Base);
        }
        public static void TxtC(Graphics g, string s, Font f, Color c, Rectangle r)
        {
            TextRenderer.DrawText(g, s, f, r, c, Base | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
        public static void TxtL(Graphics g, string s, Font f, Color c, Rectangle r)
        {
            TextRenderer.DrawText(g, s, f, r, c, Base | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
        public static int W(Graphics g, string s, Font f) { return TextRenderer.MeasureText(g, s, f, Size.Empty, Base).Width; }
        public static int H(Font f) { return TextRenderer.MeasureText("Ag", f, Size.Empty, Base).Height; }
    }

    // Basis: weich gezeichnete, abgerundete Karte.
    class CardBase : Control
    {
        public CardBase()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Th.Bg;
        }
        protected void DrawCard(Graphics g)
        {
            g.Clear(Th.Bg);
            Th.Hq(g);
            using (var p = Th.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Th.Pf(14)))
            {
                using (var b = new SolidBrush(Th.Card)) g.FillPath(b, p);
                using (var pen = new Pen(Th.Border, 1)) g.DrawPath(pen, p);
            }
        }
        protected override void OnPaint(PaintEventArgs e) { DrawCard(e.Graphics); }
    }

    class Widget : Control
    {
        public Widget()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Th.Card;
            Cursor = Cursors.Hand;
        }
        protected bool Hot;
        protected override void OnMouseEnter(EventArgs e) { Hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Hot = false; Invalidate(); base.OnMouseLeave(e); }
    }

    // Segment-Schalter mit Überschrift.
    class Segmented : Widget
    {
        public string Caption = "";
        public string Note = "";
        public string[] Items = new string[0];
        public Color[] Accents;
        public int Selected = -1;
        public bool Busy;
        public event Action<int> Changed;
        int hover = -1;

        public static int CaptionH { get { return Th.P(22); } }
        Rectangle TrackRect { get { int top = string.IsNullOrEmpty(Caption) ? 0 : CaptionH; return new Rectangle(0, top, Width - 1, Height - top - 1); } }

        int HitItem(Point p)
        {
            var r = TrackRect;
            if (!r.Contains(p) || Items.Length == 0) return -1;
            return Math.Min(Items.Length - 1, (p.X - r.X) * Items.Length / Math.Max(1, r.Width));
        }
        protected override void OnMouseMove(MouseEventArgs e) { int h = HitItem(e.Location); if (h != hover) { hover = h; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            int h = HitItem(e.Location);
            if (h >= 0 && Enabled && h != Selected) { Selected = h; Invalidate(); if (Changed != null) Changed(h); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor); Th.Hq(g);
            if (!string.IsNullOrEmpty(Caption))
            {
                Th.Txt(g, Caption, Th.Semi(8.5f), Th.Dim, 0, Th.P(2));
                string note = Busy ? "…" : Note;
                if (!string.IsNullOrEmpty(note)) Th.TxtR(g, note, Th.F(8f), Th.Dim, Width - 1, Th.P(3));
            }
            var r = TrackRect;
            float rad = Th.Pf(9);
            using (var p = Th.Round(r, rad)) using (var b = new SolidBrush(Th.Track)) g.FillPath(b, p);
            if (Items.Length == 0) return;
            float w = (float)r.Width / Items.Length;
            for (int i = 0; i < Items.Length; i++)
            {
                var seg = new RectangleF(r.X + i * w, r.Y, w, r.Height);
                Color acc = Accents != null && i < Accents.Length ? Accents[i] : Th.Violet;
                bool sel = i == Selected;
                if (sel)
                {
                    var inner = RectangleF.Inflate(seg, -Th.Pf(3), -Th.Pf(3));
                    using (var p = Th.Round(inner, rad - Th.Pf(3)))
                    using (var b = new LinearGradientBrush(inner, Th.Mix(acc, Color.White, 0.12f), acc, LinearGradientMode.Vertical))
                        g.FillPath(b, p);
                }
                else if (i == hover && Enabled)
                {
                    var inner = RectangleF.Inflate(seg, -Th.Pf(3), -Th.Pf(3));
                    using (var p = Th.Round(inner, rad - Th.Pf(3))) using (var b = new SolidBrush(Th.A(Color.White, 14))) g.FillPath(b, p);
                }
                Color tc = !Enabled ? Th.Faint : sel ? Color.FromArgb(18, 18, 24) : (i == hover ? Th.Text : Th.Dim);
                Th.TxtC(g, Items[i], sel ? Th.Semi(9f) : Th.F(9f), tc, Rectangle.Round(seg));
            }
        }
    }

    // Kippschalter mit Beschriftung und kurzer Animation.
    class Toggle : Widget
    {
        public string Label = "", Hint = "";
        public Color Accent = Th.Violet;
        public bool Busy, Available = true;
        bool on; float anim;
        Timer t;
        public event Action<bool> Changed;

        public bool On
        {
            get { return on; }
            set
            {
                if (on == value) return;
                on = value;
                if (t == null) { t = new Timer { Interval = 15 }; t.Tick += (s, e) => { float target = on ? 1 : 0; anim += (target - anim) * 0.35f; if (Math.Abs(target - anim) < 0.02f) { anim = target; t.Stop(); } Invalidate(); }; }
                if (IsHandleCreated && Visible) t.Start(); else { anim = on ? 1 : 0; Invalidate(); }
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (!Available || Busy) return;
            On = !On;
            if (Changed != null) Changed(on);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor); Th.Hq(g);
            int tw = Th.P(40), th = Th.P(22);
            var tr = new RectangleF(Width - tw - 1, (Height - th) / 2f, tw, th);
            int lx = 0;
            Th.TxtL(g, Label, Th.F(9.5f), Available ? (Hot ? Color.White : Th.Text) : Th.Faint, new Rectangle(lx, 0, Width - tw - Th.P(8), Height));
            if (!string.IsNullOrEmpty(Hint) || Busy)
            {
                int lw = Th.W(g, Label, Th.F(9.5f));
                Th.TxtL(g, Busy ? "wird geändert …" : Hint, Th.F(8f), Th.Dim, new Rectangle(lx + lw + Th.P(8), Th.P(1), Width - tw - lw - Th.P(18), Height));
            }
            Color trackC = !Available ? Th.Track : Th.Mix(Th.Track, Accent, anim);
            using (var p = Th.Round(tr, th / 2f)) using (var b = new SolidBrush(trackC)) g.FillPath(b, p);
            float kd = th - Th.Pf(6);
            float kx = tr.X + Th.Pf(3) + (tw - kd - Th.Pf(6)) * anim;
            using (var b = new SolidBrush(Available ? Color.White : Th.Faint)) g.FillEllipse(b, kx, tr.Y + Th.Pf(3), kd, kd);
        }
    }

    // Schieberegler (0–100) mit Überschrift.
    class Slider : Widget
    {
        public string Caption = "";
        public Color Accent = Th.Yellow;
        public int Value = 50;
        public event Action<int> Committed;
        bool drag; DateTime lastSend;

        Rectangle Bar { get { int y = Segmented.CaptionH + Th.P(10); return new Rectangle(Th.P(8), y, Width - Th.P(16), Th.P(6)); } }

        void SetFromX(int x, bool final)
        {
            var b = Bar;
            int v = Math.Max(0, Math.Min(100, (int)Math.Round((x - b.X) * 100.0 / b.Width)));
            if (v != Value) { Value = v; Invalidate(); }
            if (final || (DateTime.UtcNow - lastSend).TotalMilliseconds > 180)
            {
                lastSend = DateTime.UtcNow;
                if (Committed != null) Committed(Value);
            }
        }
        protected override void OnMouseDown(MouseEventArgs e) { drag = true; SetFromX(e.X, false); }
        protected override void OnMouseMove(MouseEventArgs e) { if (drag) SetFromX(e.X, false); }
        protected override void OnMouseUp(MouseEventArgs e) { if (drag) { drag = false; SetFromX(e.X, true); } }
        public bool Dragging { get { return drag; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor); Th.Hq(g);
            Th.Txt(g, Caption, Th.Semi(8.5f), Th.Dim, 0, Th.P(2));
            Th.TxtR(g, Value + " %", Th.F(8.5f), Th.Text, Width - 1, Th.P(2));
            var b = Bar;
            using (var p = Th.Round(b, b.Height / 2f)) using (var br = new SolidBrush(Th.Track)) g.FillPath(br, p);
            var fill = new RectangleF(b.X, b.Y, b.Width * Value / 100f, b.Height);
            if (fill.Width > 1)
                using (var p = Th.Round(fill, b.Height / 2f)) using (var br = new LinearGradientBrush(new RectangleF(b.X, b.Y, b.Width, b.Height), Th.Mix(Accent, Th.Card, 0.5f), Accent, LinearGradientMode.Horizontal)) g.FillPath(br, p);
            float kd = Th.Pf(16);
            float kx = b.X + b.Width * Value / 100f - kd / 2;
            using (var br = new SolidBrush(Color.FromArgb(60, 0, 0, 0))) g.FillEllipse(br, kx, b.Y + b.Height / 2f - kd / 2 + Th.Pf(1), kd, kd);
            using (var br = new SolidBrush(Color.White)) g.FillEllipse(br, kx, b.Y + b.Height / 2f - kd / 2, kd, kd);
        }
    }

    // Leiste über den Diagrammen: Titel links, Auswahlfelder rechts (als Kind-Controls).
    class Toolbar : Control
    {
        public string Model = "";
        public Toolbar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Th.Bg;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Th.Bg); Th.Hq(g);
            float s = Th.Pf(26), y = (Height - s) / 2f + Th.Pf(6);
            var r = new RectangleF(Th.Pf(2), y, s, s);
            using (var p = Th.Round(r, s * 0.26f))
            using (var b = new LinearGradientBrush(r, Th.Violet, Th.Cyan, 45f)) g.FillPath(b, p);
            float k = s / 64f;
            var bolt = new[] { new PointF(r.X + 36 * k, r.Y + 10 * k), new PointF(r.X + 18 * k, r.Y + 36 * k), new PointF(r.X + 30 * k, r.Y + 36 * k), new PointF(r.X + 26 * k, r.Y + 54 * k), new PointF(r.X + 46 * k, r.Y + 26 * k), new PointF(r.X + 34 * k, r.Y + 26 * k) };
            using (var b = new SolidBrush(Color.White)) g.FillPolygon(b, bolt);
            int tx = (int)(r.Right + Th.Pf(12));
            int ty = (int)y + Th.P(1);
            Th.Txt(g, "Yoga Panel", Th.F(13f, FontStyle.Bold), Th.Text, tx, ty - Th.P(4));
            if (!string.IsNullOrEmpty(Model))
                Th.Txt(g, Model, Th.F(9f), Th.Dim, tx + Th.W(g, "Yoga Panel", Th.F(13f, FontStyle.Bold)) + Th.P(10), ty + Th.P(2));
        }
    }

    // Dunkles Kontextmenü für das Tray-Symbol.
    class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Th.Card; } }
        public override Color ImageMarginGradientBegin { get { return Th.Card; } }
        public override Color ImageMarginGradientMiddle { get { return Th.Card; } }
        public override Color ImageMarginGradientEnd { get { return Th.Card; } }
        public override Color MenuBorder { get { return Th.Border; } }
        public override Color MenuItemBorder { get { return Th.A(Th.Violet, 120); } }
        public override Color MenuItemSelected { get { return Th.A(Th.Violet, 60); } }
        public override Color SeparatorDark { get { return Th.Border; } }
        public override Color SeparatorLight { get { return Th.Border; } }
        public override Color CheckBackground { get { return Th.A(Th.Violet, 80); } }
        public override Color CheckSelectedBackground { get { return Th.A(Th.Violet, 120); } }
        public override Color CheckPressedBackground { get { return Th.A(Th.Violet, 120); } }
    }
}
