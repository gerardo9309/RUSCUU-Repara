// RUSCUU Repara - tema visual y controles dibujados a mano
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Ruscuu
{
    static class Theme
    {
        public static Color Bg, Side, Card, CardHover, Card2, Line, Accent, AccentDark, Text, Muted, Ok, Warn, Err, Brown, Blue, NavHover, Track, LogBg, BadgeBg;
        public static bool Dark = true;
        public static readonly Color[] Accents = {
            Color.FromArgb(160, 82, 255), Color.FromArgb(64, 140, 255), Color.FromArgb(30, 180, 120),
            Color.FromArgb(255, 140, 50), Color.FromArgb(235, 80, 165), Color.FromArgb(225, 65, 80) };
        public static readonly string[] AccentNames = { "Morado", "Azul", "Verde", "Naranja", "Rosa", "Rojo" };
        public static float Scale = 1f;

        static Theme() { Apply(true, 0); }

        public static void Apply(bool dark, int accent)
        {
            Dark = dark;
            Accent = Accents[Math.Max(0, Math.Min(Accents.Length - 1, accent))];
            AccentDark = Mix(Accent, Color.Black, 0.3);
            Brown = Color.FromArgb(196, 120, 64);
            Blue = Color.FromArgb(70, 150, 240);
            if (dark)
            {
                Bg = Color.FromArgb(26, 11, 46); Side = Color.FromArgb(17, 6, 31); Card = Color.FromArgb(38, 18, 62);
                CardHover = Color.FromArgb(50, 25, 80); Card2 = Color.FromArgb(30, 12, 52); Line = Color.FromArgb(60, 35, 90);
                Text = Color.FromArgb(242, 232, 213); Muted = Color.FromArgb(170, 155, 190);
                Ok = Color.FromArgb(110, 220, 140); Warn = Color.FromArgb(255, 196, 92); Err = Color.FromArgb(255, 110, 110);
                NavHover = Color.FromArgb(28, 12, 48); Track = Color.FromArgb(70, 50, 95); LogBg = Color.FromArgb(10, 3, 20); BadgeBg = Color.FromArgb(70, 40, 105);
            }
            else
            {
                Bg = Color.FromArgb(246, 242, 251); Side = Color.FromArgb(236, 229, 246); Card = Color.White;
                CardHover = Color.FromArgb(243, 236, 252); Card2 = Color.FromArgb(248, 244, 253); Line = Color.FromArgb(221, 208, 238);
                Text = Color.FromArgb(36, 17, 58); Muted = Color.FromArgb(105, 90, 128);
                Ok = Color.FromArgb(25, 145, 75); Warn = Color.FromArgb(185, 115, 0); Err = Color.FromArgb(205, 45, 55);
                NavHover = Color.FromArgb(228, 218, 243); Track = Color.FromArgb(218, 207, 234); LogBg = Color.FromArgb(252, 250, 254); BadgeBg = Color.FromArgb(236, 228, 248);
            }
        }

        public static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }
        public static Color Alpha(Color c, int a) { return Color.FromArgb(Math.Max(0, Math.Min(255, a)), c); }
        static bool IsLight(Color c) { return (c.R * 0.299 + c.G * 0.587 + c.B * 0.114) > 160; }

        public static int S(int v) { return (int)Math.Round(v * Scale); }
        public static Font F(float size, FontStyle st) { return new Font("Segoe UI", size, st); }

        public static GraphicsPath Round(Rectangle r, int rad)
        {
            var p = new GraphicsPath(); int d = Math.Max(1, rad * 2);
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }

        public static void Prep(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        }

        public static Button Btn(string text, Color bg, int w)
        {
            var b = new Button { Text = text, Width = w, Height = S(36), FlatStyle = FlatStyle.Flat, BackColor = bg, ForeColor = IsLight(bg) ? Text : Color.White, Font = F(10f, FontStyle.Bold), Cursor = Cursors.Hand };
            b.FlatAppearance.BorderSize = IsLight(bg) ? 1 : 0;
            b.FlatAppearance.BorderColor = Line;
            b.FlatAppearance.MouseOverBackColor = Dark ? ControlPaint.Light(bg, 0.25f) : Mix(bg, Accent, 0.12);
            b.BackColorChanged += (s, e) => b.ForeColor = IsLight(b.BackColor) ? Text : Color.White;
            return b;
        }

        public static ComboBox Combo(int w, params string[] items)
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Bg, ForeColor = Text, Font = F(9.5f, FontStyle.Regular), Width = w };
            c.Items.AddRange(items);
            if (items.Length > 0) c.SelectedIndex = 0;
            return c;
        }

        public static TextBox Input(int w)
        {
            return new TextBox { BackColor = Bg, ForeColor = Text, BorderStyle = BorderStyle.FixedSingle, Font = F(10.5f, FontStyle.Regular), Width = w };
        }

        public static Label Lbl(string text, float size, FontStyle st, Color color)
        {
            return new Label { Text = text, AutoSize = true, Font = F(size, st), ForeColor = color, BackColor = Color.Transparent };
        }

        // barras de desplazamiento oscuras nativas (Windows 10 1809+ / Windows 11)
        public static T DarkScroll<T>(T c) where T : Control
        {
            if (!Dark) return c;
            EventHandler apply = (s, e) => { try { Native.SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { } };
            if (c.IsHandleCreated) apply(c, EventArgs.Empty); else c.HandleCreated += apply;
            return c;
        }

        public static Color ScoreColor(double v) { return v < 50 ? Err : v < 75 ? Warn : Ok; }
    }

    // Base para controles dibujados con doble búfer y estado hover
    class OwnerControl : Control
    {
        protected bool hover;
        public OwnerControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected Color ParentBg { get { return Parent != null ? Parent.BackColor : Theme.Bg; } }

        protected void CardBg(Graphics g, bool highlight, Color? border = null, int rad = 10)
        {
            g.Clear(ParentBg);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Theme.Round(r, Theme.S(rad)))
            {
                using (var b = new SolidBrush(highlight ? Theme.CardHover : Theme.Card)) g.FillPath(b, path);
                using (var p = new Pen(border ?? Theme.Line, border.HasValue ? 2f : 1f)) g.DrawPath(p, path);
            }
        }

        protected void CheckBox(Graphics g, Rectangle box, bool on)
        {
            using (var path = Theme.Round(box, Theme.S(4)))
            {
                if (on) using (var b = new SolidBrush(Theme.Accent)) g.FillPath(b, path);
                using (var p = new Pen(on ? Theme.Accent : Theme.Muted, 1.5f)) g.DrawPath(p, path);
            }
            if (on)
                using (var p = new Pen(Color.White, Theme.S(2)))
                    g.DrawLines(p, new[] { new Point(box.X + box.Width / 5, box.Y + box.Height / 2), new Point(box.X + box.Width * 2 / 5, box.Y + box.Height * 7 / 10), new Point(box.X + box.Width * 4 / 5, box.Y + box.Height * 3 / 10) });
        }

        protected int Badge(Graphics g, string text, int x, int y, Color fg)
        {
            using (var f = Theme.F(7.75f, FontStyle.Regular))
            {
                var sz = g.MeasureString(text, f);
                var r = new Rectangle(x, y, (int)sz.Width + Theme.S(10), Theme.S(19));
                using (var path = Theme.Round(r, Theme.S(9))) using (var b = new SolidBrush(Theme.Alpha(fg, Theme.Dark ? 45 : 30))) g.FillPath(b, path);
                using (var b = new SolidBrush(fg)) g.DrawString(text, f, b, r.X + Theme.S(5), r.Y + (r.Height - sz.Height) / 2);
                return r.Right + Theme.S(6);
            }
        }
    }

    class NavButton : OwnerControl
    {
        public bool Active;
        public string Glyph;
        public NavButton(string glyph, string text) { Glyph = glyph; Text = text; Height = Theme.S(36); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Prep(g);
            g.Clear(Theme.Side);
            if (Active || hover)
            {
                using (var b = new SolidBrush(Active ? Theme.Card : Theme.NavHover))
                using (var path = Theme.Round(new Rectangle(Theme.S(10), 2, Width - Theme.S(20), Height - 4), Theme.S(8)))
                    g.FillPath(b, path);
            }
            if (Active) using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, Theme.S(10), Theme.S(9), Theme.S(4), Height - Theme.S(18));
            using (var f = new Font("Segoe UI Emoji", 10.5f))
            using (var b = new SolidBrush(Active ? Theme.Accent : Theme.Muted))
                g.DrawString(Glyph, f, b, Theme.S(24), (Height - g.MeasureString(Glyph, f).Height) / 2);
            using (var f = Theme.F(9.75f, Active ? FontStyle.Bold : FontStyle.Regular))
            using (var b = new SolidBrush(Active ? Theme.Text : Theme.Muted))
                g.DrawString(Text, f, b, Theme.S(54), (Height - g.MeasureString(Text, f).Height) / 2);
        }
    }

    class NavHeader : Control
    {
        public NavHeader(string text) { Text = text; Height = Theme.S(28); SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Side);
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (var f = Theme.F(7.5f, FontStyle.Bold)) using (var b = new SolidBrush(Theme.Alpha(Theme.Muted, 170)))
                e.Graphics.DrawString(Text, f, b, Theme.S(24), Theme.S(10));
        }
    }

    class TaskCard : OwnerControl
    {
        public RepairTask Task;
        public event EventHandler Toggled;
        public TaskCard(RepairTask t)
        {
            Task = t;
            Size = new Size(Theme.S(400), Theme.S(110)); Margin = new Padding(Theme.S(8));
        }
        protected override void OnClick(EventArgs e)
        {
            if (!Enabled) return;
            Task.Selected = !Task.Selected; Invalidate();
            if (Toggled != null) Toggled(this, EventArgs.Empty);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Prep(g);
            CardBg(g, hover, Task.Selected ? (Color?)Theme.Accent : null);
            CheckBox(g, new Rectangle(Theme.S(14), Theme.S(16), Theme.S(20), Theme.S(20)), Task.Selected);
            int x = Theme.S(46);
            using (var f = Theme.F(10.5f, FontStyle.Bold)) using (var b = new SolidBrush(Enabled ? Theme.Text : Theme.Muted))
                g.DrawString(Task.Title, f, b, new RectangleF(x, Theme.S(14), Width - x - Theme.S(10), Theme.S(24)));
            using (var f = Theme.F(8.75f, FontStyle.Regular)) using (var b = new SolidBrush(Theme.Muted))
                g.DrawString(Task.Desc, f, b, new RectangleF(x, Theme.S(38), Width - x - Theme.S(12), Theme.S(40)));
            int y = Height - Theme.S(28);
            int bx = Badge(g, "⏱ " + Task.Time, x, y, Theme.Muted);
            if (Task.Reboot) bx = Badge(g, "⟳ Requiere reiniciar", bx, y, Theme.Warn);
            if (Task.Caution != null) Badge(g, "⚠ " + Task.Caution, bx, y, Theme.Err);
        }
    }

    class PresetTile : OwnerControl
    {
        public string Glyph, Desc; public Color Stripe;
        public PresetTile(string glyph, string title, string desc, Color stripe)
        {
            Glyph = glyph; Text = title; Desc = desc; Stripe = stripe;
            Size = new Size(Theme.S(400), Theme.S(112)); Margin = new Padding(Theme.S(8));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Prep(g);
            g.Clear(ParentBg);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Theme.Round(r, Theme.S(12)))
            {
                using (var b = new LinearGradientBrush(r, hover && Enabled ? Theme.CardHover : Theme.Card, Theme.Card2, 0f)) g.FillPath(b, path);
                using (var p = new Pen(hover && Enabled ? Stripe : Theme.Line, hover ? 2f : 1f)) g.DrawPath(p, path);
            }
            var circle = new Rectangle(Theme.S(18), (Height - Theme.S(52)) / 2, Theme.S(52), Theme.S(52));
            using (var b = new SolidBrush(Theme.Alpha(Stripe, 60))) g.FillEllipse(b, circle);
            using (var f = new Font("Segoe UI Emoji", 18f)) using (var b = new SolidBrush(Stripe))
            {
                var sz = g.MeasureString(Glyph, f);
                g.DrawString(Glyph, f, b, circle.X + (circle.Width - sz.Width) / 2, circle.Y + (circle.Height - sz.Height) / 2);
            }
            int x = Theme.S(86);
            using (var f = Theme.F(12.5f, FontStyle.Bold)) using (var b = new SolidBrush(Enabled ? Theme.Text : Theme.Muted))
                g.DrawString(Text, f, b, x, Theme.S(18));
            using (var f = Theme.F(9f, FontStyle.Regular)) using (var b = new SolidBrush(Theme.Muted))
                g.DrawString(Desc, f, b, new RectangleF(x, Theme.S(48), Width - x - Theme.S(14), Height - Theme.S(52)));
        }
    }

    // Fila del gestor de arranque con interruptor
    class StartupRow : OwnerControl
    {
        public StartupItem Item;
        public event EventHandler Toggled;
        public StartupRow(StartupItem it) { Item = it; Height = Theme.S(62); Margin = new Padding(Theme.S(8), Theme.S(4), Theme.S(8), Theme.S(4)); }
        protected override void OnClick(EventArgs e)
        {
            if (!Enabled) return;
            if (Toggled != null) Toggled(this, EventArgs.Empty);
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Prep(g);
            CardBg(g, hover);
            var sw = new Rectangle(Width - Theme.S(66), (Height - Theme.S(24)) / 2, Theme.S(46), Theme.S(24));
            using (var path = Theme.Round(sw, Theme.S(12)))
            using (var b = new SolidBrush(Item.Enabled ? Theme.Accent : Theme.Track)) g.FillPath(b, path);
            int kx = Item.Enabled ? sw.Right - Theme.S(21) : sw.X + Theme.S(3);
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, kx, sw.Y + Theme.S(3), Theme.S(18), Theme.S(18));

            int x = Theme.S(18), maxW = Width - x - Theme.S(230);
            using (var f = Theme.F(10.5f, FontStyle.Bold)) using (var b = new SolidBrush(Item.Enabled ? Theme.Text : Theme.Muted))
                g.DrawString(Item.Name, f, b, new RectangleF(x, Theme.S(10), maxW, Theme.S(22)));
            using (var f = Theme.F(8.5f, FontStyle.Regular)) using (var b = new SolidBrush(Theme.Muted))
            using (var fmt = new StringFormat { Trimming = StringTrimming.EllipsisPath, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(Item.Command, f, b, new RectangleF(x, Theme.S(34), maxW, Theme.S(18)), fmt);
            using (var f = Theme.F(8f, FontStyle.Regular)) using (var b = new SolidBrush(Item.Enabled ? Theme.Ok : Theme.Muted))
            {
                string st = (Item.Enabled ? "Activo" : "Desactivado") + "  •  " + Item.Source;
                var sz = g.MeasureString(st, f);
                g.DrawString(st, f, b, sw.X - sz.Width - Theme.S(14), (Height - sz.Height) / 2);
            }
        }
    }

    // Fila genérica de lista con casilla opcional (desinstalador, duplicados, historial…)
    class CheckRow : OwnerControl
    {
        public string Title, Sub, RightText;
        public bool Checked, Checkable = true, Header;
        public Color? Dot;
        public object Data;
        public event EventHandler Toggled, Secondary;
        public CheckRow(string title, string sub, string right) { Title = title; Sub = sub; RightText = right; Height = Theme.S(54); Margin = new Padding(Theme.S(8), Theme.S(3), Theme.S(8), Theme.S(3)); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!Enabled) return;
            if (e.Button == MouseButtons.Right) { if (Secondary != null) Secondary(this, EventArgs.Empty); return; }
            if (Checkable) { Checked = !Checked; Invalidate(); }
            if (Toggled != null) Toggled(this, EventArgs.Empty);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Prep(g);
            if (Header)
            {
                g.Clear(ParentBg);
                using (var f = Theme.F(10f, FontStyle.Bold)) using (var b = new SolidBrush(Theme.Accent)) g.DrawString(Title, f, b, Theme.S(4), Theme.S(8));
                if (!string.IsNullOrEmpty(RightText))
                    using (var f = Theme.F(9f, FontStyle.Regular)) using (var b = new SolidBrush(Theme.Muted))
                    { var sz = g.MeasureString(RightText, f); g.DrawString(RightText, f, b, Width - sz.Width - Theme.S(8), Theme.S(9)); }
                return;
            }
            CardBg(g, hover, Checked ? (Color?)Theme.Accent : null, 8);
            int x = Theme.S(16);
            if (Checkable) { CheckBox(g, new Rectangle(x, (Height - Theme.S(18)) / 2, Theme.S(18), Theme.S(18)), Checked); x += Theme.S(30); }
            if (Dot.HasValue) { using (var b = new SolidBrush(Dot.Value)) g.FillEllipse(b, x, (Height - Theme.S(10)) / 2, Theme.S(10), Theme.S(10)); x += Theme.S(22); }
            float rightW = 0;
            if (!string.IsNullOrEmpty(RightText))
                using (var f = Theme.F(9f, FontStyle.Regular)) using (var b = new SolidBrush(Theme.Muted))
                {
                    var sz = g.MeasureString(RightText, f); rightW = sz.Width + Theme.S(16);
                    g.DrawString(RightText, f, b, Width - sz.Width - Theme.S(14), (Height - sz.Height) / 2);
                }
            float maxW = Width - x - rightW - Theme.S(10);
            using (var fmt = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            {
                using (var f = Theme.F(10f, FontStyle.Bold)) using (var b = new SolidBrush(Theme.Text))
                    g.DrawString(Title, f, b, new RectangleF(x, string.IsNullOrEmpty(Sub) ? (Height - Theme.S(20)) / 2 : Theme.S(8), maxW, Theme.S(22)), fmt);
                if (!string.IsNullOrEmpty(Sub))
                    using (var f = Theme.F(8.5f, FontStyle.Regular)) using (var b = new SolidBrush(Theme.Muted))
                        g.DrawString(Sub, f, b, new RectangleF(x, Theme.S(30), maxW, Theme.S(18)), fmt);
            }
        }
    }

    // Medidor de salud 0-100 con animación
    class Gauge : Control
    {
        double shown, target = -1, spin;
        public bool Busy;
        public string Caption = "Sin analizar";
        readonly Timer timer = new Timer { Interval = 15 };
        public Gauge()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            timer.Tick += (s, e) =>
            {
                spin += 7;
                if (target >= 0) { shown += (target - shown) * 0.07; if (Math.Abs(target - shown) < 0.4) shown = target; }
                if (!Busy && (target < 0 || shown == target)) timer.Stop();
                Invalidate();
            };
        }
        public void SetBusy(bool b) { Busy = b; if (b) Caption = "Analizando…"; timer.Start(); Invalidate(); }
        public void SetValue(int v, string caption) { Busy = false; target = v; Caption = caption; timer.Start(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Prep(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.Card);
            int th = Theme.S(14);
            int size = Math.Min(Width, Height - Theme.S(26)) - th;
            var r = new Rectangle((Width - size) / 2, th / 2 + Theme.S(2), size, size);
            using (var p = new Pen(Theme.Track, th) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(p, r, 135, 270);
            if (Busy)
                using (var p = new Pen(Theme.Accent, th) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(p, r, (float)(135 + spin % 270), 50);
            else if (target >= 0 && shown > 0.5)
                using (var p = new Pen(Theme.ScoreColor(shown), th) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(p, r, 135, (float)(270 * shown / 100));
            string num = Busy ? "…" : target < 0 ? "?" : ((int)Math.Round(shown)).ToString();
            using (var f = Theme.F(30f, FontStyle.Bold)) using (var b = new SolidBrush(target < 0 || Busy ? Theme.Muted : Theme.ScoreColor(shown)))
            {
                var sz = g.MeasureString(num, f);
                g.DrawString(num, f, b, r.X + (r.Width - sz.Width) / 2, r.Y + (r.Height - sz.Height) / 2 - Theme.S(4));
            }
            using (var f = Theme.F(9f, FontStyle.Bold)) using (var b = new SolidBrush(Theme.Muted))
            {
                var sz = g.MeasureString(Caption, f);
                g.DrawString(Caption, f, b, (Width - sz.Width) / 2, r.Bottom - Theme.S(18));
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }

    // Gráfica de línea en vivo (monitor)
    class Spark : Control
    {
        public string Title, Value = "–", Sub = "";
        public Color LineColor = Theme.Accent;
        public float Max = 100;
        public readonly List<float> Values = new List<float>();
        public Spark(string title) { Title = title; SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); Margin = new Padding(Theme.S(8)); }
        public void Push(float v, string value, string sub) { Values.Add(v); if (Values.Count > 60) Values.RemoveAt(0); Value = value; Sub = sub; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Prep(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Theme.Round(r, Theme.S(10)))
            {
                using (var b = new SolidBrush(Theme.Card)) g.FillPath(b, path);
                using (var p = new Pen(Theme.Line)) g.DrawPath(p, path);
            }
            using (var f = Theme.F(9.5f, FontStyle.Bold)) using (var b = new SolidBrush(Theme.Muted)) g.DrawString(Title, f, b, Theme.S(14), Theme.S(10));
            using (var f = Theme.F(20f, FontStyle.Bold)) using (var b = new SolidBrush(Theme.Text)) g.DrawString(Value, f, b, Theme.S(12), Theme.S(28));
            using (var f = Theme.F(8.5f, FontStyle.Regular)) using (var b = new SolidBrush(Theme.Muted))
            using (var fmt = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(Sub, f, b, new RectangleF(Theme.S(14), Theme.S(66), Width - Theme.S(28), Theme.S(18)), fmt);
            int top = Theme.S(90), bottom = Height - Theme.S(10), left = Theme.S(10), right = Width - Theme.S(10);
            if (Values.Count < 2 || bottom - top < 10) return;
            float max = Max > 0 ? Max : Math.Max(1, Values.Max() * 1.2f);
            var pts = new List<PointF>();
            for (int i = 0; i < Values.Count; i++)
            {
                float x = right - (Values.Count - 1 - i) * (right - left) / 59f;
                float y = bottom - Math.Min(1, Values[i] / max) * (bottom - top);
                pts.Add(new PointF(x, y));
            }
            var fill = new List<PointF>(pts) { new PointF(pts[pts.Count - 1].X, bottom), new PointF(pts[0].X, bottom) };
            using (var b = new LinearGradientBrush(new Rectangle(left, top - 1, right - left, bottom - top + 2), Theme.Alpha(LineColor, 110), Theme.Alpha(LineColor, 5), 90f))
                g.FillPolygon(b, fill.ToArray());
            using (var p = new Pen(LineColor, Theme.S(2))) g.DrawLines(p, pts.ToArray());
        }
    }

    class FlatBar : Control
    {
        double val;
        public double Value { get { return val; } set { val = Math.Max(0, Math.Min(1, value)); Invalidate(); } }
        public FlatBar() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Track);
            int w = (int)(Width * val);
            if (w > 0) using (var b = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(w, 1), Height), Theme.AccentDark, Theme.Accent, 0f))
                e.Graphics.FillRectangle(b, 0, 0, w, Height);
        }
    }

    // Ventana de diálogo base con el estilo de RUSCUU
    class DarkDialog : Form
    {
        public DarkDialog(string title, int w, int h)
        {
            Text = title;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent; BackColor = Theme.Bg; ForeColor = Theme.Text;
            AutoScaleMode = AutoScaleMode.None; ClientSize = new Size(Theme.S(w), Theme.S(h));
            Font = Theme.F(9.5f, FontStyle.Regular);
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (Theme.Dark) try { int on = 1; Native.DwmSetWindowAttribute(Handle, 20, ref on, 4); } catch { }
        }
        protected Button AddButtons(string okText, bool cancel)
        {
            var ok = Theme.Btn(okText, Theme.Accent, Theme.S(140)); ok.Location = new Point(ClientSize.Width - Theme.S(160), ClientSize.Height - Theme.S(54));
            Controls.Add(ok); AcceptButton = ok;
            if (cancel)
            {
                var c = Theme.Btn("Cancelar", Theme.Card, Theme.S(120)); c.Location = new Point(ok.Left - Theme.S(130), ok.Top);
                c.Click += (s, e) => DialogResult = DialogResult.Cancel;
                Controls.Add(c); CancelButton = c;
            }
            return ok;
        }
    }

    // Pedir o crear el PIN del modo técnico
    class PinDialog : DarkDialog
    {
        public string Pin;
        public PinDialog(bool create) : base(create ? "Crear PIN de técnico" : "Modo técnico", 380, create ? 250 : 190)
        {
            var t = Theme.Lbl(create ? "Crea un PIN (4 a 8 números) para proteger el modo técnico:" : "Introduce el PIN de técnico:", 10f, FontStyle.Regular, Theme.Text);
            t.MaximumSize = new Size(Theme.S(340), 0); t.Location = new Point(Theme.S(20), Theme.S(18));
            var a = Theme.Input(Theme.S(160)); a.UseSystemPasswordChar = true; a.MaxLength = 8; a.Location = new Point(Theme.S(20), Theme.S(62));
            Controls.Add(t); Controls.Add(a);
            TextBox b = null;
            if (create)
            {
                var l2 = Theme.Lbl("Repítelo:", 9.5f, FontStyle.Regular, Theme.Muted); l2.Location = new Point(Theme.S(20), Theme.S(104));
                b = Theme.Input(Theme.S(160)); b.UseSystemPasswordChar = true; b.MaxLength = 8; b.Location = new Point(Theme.S(20), Theme.S(126));
                Controls.Add(l2); Controls.Add(b);
            }
            var err = Theme.Lbl("", 9f, FontStyle.Regular, Theme.Err); err.Location = new Point(Theme.S(200), Theme.S(66)); Controls.Add(err);
            var ok = AddButtons(create ? "Guardar PIN" : "Entrar", true);
            ok.Click += (s, e) =>
            {
                string p = a.Text.Trim();
                if (p.Length < 4 || !p.All(char.IsDigit)) { err.Text = "Solo números (4-8)"; return; }
                if (create && b.Text.Trim() != p) { err.Text = "No coinciden"; return; }
                Pin = p; DialogResult = DialogResult.OK;
            };
        }
    }

    // Diálogo para elegir la memoria USB del modo técnico
    class UsbDialog : DarkDialog
    {
        public string Drive, ToolsFolder;
        public UsbDialog() : base("Crear USB técnico", 520, 290)
        {
            var found = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Removable && d.IsReady).ToList();
            var t = Theme.Lbl("Memoria USB de destino", 11f, FontStyle.Bold, Theme.Text); t.Location = new Point(Theme.S(20), Theme.S(18));
            var drives = Theme.Combo(Theme.S(480), found.Select(d => d.Name + "  " + (string.IsNullOrEmpty(d.VolumeLabel) ? "USB" : d.VolumeLabel) + "  (" + Catalog.Size(d.AvailableFreeSpace) + " libres de " + Catalog.Size(d.TotalSize) + ")").ToArray());
            drives.Location = new Point(Theme.S(20), Theme.S(48));
            var info = new Label { AutoSize = false, Location = new Point(Theme.S(20), Theme.S(84)), Size = new Size(Theme.S(480), Theme.S(60)), ForeColor = Theme.Muted,
                Text = found.Count == 0 ? "No se detectó ninguna memoria USB. Conéctala y vuelve a abrir esta ventana." :
                "Se creará la carpeta RUSCUU con este programa, enlaces a las mejores herramientas gratuitas, una guía rápida y el icono de RUSCUU para la unidad. No se borra nada de la memoria." };
            var chk = new CheckBox { Text = "Copiar también mi carpeta de herramientas…", AutoSize = true, Location = new Point(Theme.S(20), Theme.S(150)), ForeColor = Theme.Text };
            var toolsLbl = new Label { AutoSize = false, Location = new Point(Theme.S(40), Theme.S(176)), Size = new Size(Theme.S(460), Theme.S(36)), ForeColor = Theme.Muted, Text = "" };
            chk.CheckedChanged += (s, e) =>
            {
                if (!chk.Checked) { ToolsFolder = null; toolsLbl.Text = ""; return; }
                using (var fb = new FolderBrowserDialog { Description = "Elige la carpeta con tus herramientas portables" })
                {
                    if (fb.ShowDialog(this) == DialogResult.OK) { ToolsFolder = fb.SelectedPath; toolsLbl.Text = ToolsFolder; }
                    else chk.Checked = false;
                }
            };
            Controls.AddRange(new Control[] { t, drives, info, chk, toolsLbl });
            var ok = AddButtons("Crear USB", true);
            ok.Enabled = found.Count > 0;
            ok.Click += (s, e) => { Drive = found[drives.SelectedIndex].RootDirectory.FullName; DialogResult = DialogResult.OK; };
        }
    }
}
