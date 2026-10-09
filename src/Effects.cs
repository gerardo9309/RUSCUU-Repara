// RUSCUU Repara - sonidos, confeti y pequeños efectos de interfaz
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Ruscuu
{
    // Sonidos sintetizados en memoria (no necesita archivos .wav)
    static class Sfx
    {
        static SoundPlayer current;   // referencia viva mientras suena

        public static void Success() { Play(new[] { new[] { 523.25, 0, 0.5 }, new[] { 659.25, 0.1, 0.5 }, new[] { 783.99, 0.2, 0.6 }, new[] { 1046.5, 0.3, 0.8 } }, 1.2); }
        public static void Warning() { Play(new[] { new[] { 587.33, 0, 0.35 }, new[] { 440.0, 0.17, 0.55 } }, 0.8); }
        public static void Tick() { Play(new[] { new[] { 880.0, 0, 0.08 } }, 0.1); }

        static void Play(double[][] notes, double total)
        {
            if (!Settings.Sounds) return;
            try
            {
                current = new SoundPlayer(new MemoryStream(Wav(notes, total)));
                current.Play();
            }
            catch { }
        }

        // notas: { frecuencia, inicio (s), duración (s) }
        static byte[] Wav(double[][] notes, double total)
        {
            const int rate = 44100;
            int n = (int)(rate * total);
            var buf = new double[n];
            foreach (var note in notes)
            {
                int start = (int)(note[1] * rate), len = (int)(note[2] * rate);
                for (int i = 0; i < len && start + i < n; i++)
                {
                    double t = i / (double)rate;
                    double env = Math.Min(1, t / 0.006) * Math.Exp(-t * 7);
                    buf[start + i] += env * (0.6 * Math.Sin(2 * Math.PI * note[0] * t) + 0.18 * Math.Sin(4 * Math.PI * note[0] * t) + 0.06 * Math.Sin(6 * Math.PI * note[0] * t));
                }
            }
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + n * 2); w.Write(new[] { 'W', 'A', 'V', 'E' });
                w.Write(new[] { 'f', 'm', 't', ' ' }); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(n * 2);
                foreach (var v in buf) w.Write((short)Math.Max(-32767, Math.Min(32767, v * 0.28 * 32767)));
                return ms.ToArray();
            }
        }
    }

    // Capa transparente encima de la ventana con confeti y la insignia "¡Listo!"
    class Confetti : Form
    {
        class Bit { public float X, Y, Vx, Vy, Rot, VRot, W, H; public Color C; }

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int W, H; public SIZE(int w, int h) { W = w; H = h; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLEND { public byte Op, Flags, Alpha, Format; }
        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref POINT pt, ref SIZE sz, IntPtr src, ref POINT srcPt, int key, ref BLEND bl, int flags);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);

        const double Duration = 3.0;
        readonly List<Bit> bits = new List<Bit>();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Timer timer = new Timer { Interval = 16 };
        readonly string title, sub;
        readonly bool success;

        public static void Celebrate(Form owner, string title, string sub, bool success)
        {
            if (owner.WindowState == FormWindowState.Minimized) return;
            var c = new Confetti(owner, title, sub, success);
            c.Show(owner);
        }

        Confetti(Form owner, string title, string sub, bool success)
        {
            this.title = title; this.sub = sub; this.success = success;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            Bounds = owner.RectangleToScreen(owner.ClientRectangle);
            var rnd = new Random();
            Color[] cols = { Theme.Accent, Color.FromArgb(255, 200, 90), Color.FromArgb(242, 232, 213), Color.FromArgb(235, 90, 170), Color.FromArgb(120, 200, 255), Theme.AccentDark };
            int count = success ? 170 : 0;
            for (int i = 0; i < count; i++)
            {
                bool burst = i % 2 == 0;
                double ang = rnd.NextDouble() * Math.PI * 2;
                double sp = 300 + rnd.NextDouble() * 650;
                bits.Add(new Bit
                {
                    X = burst ? Width / 2f : (float)(rnd.NextDouble() * Width),
                    Y = burst ? Height * 0.42f : -(float)(rnd.NextDouble() * Height * 0.5),
                    Vx = burst ? (float)(Math.Cos(ang) * sp) : (float)((rnd.NextDouble() - 0.5) * 120),
                    Vy = burst ? (float)(Math.Sin(ang) * sp - 350) : (float)(rnd.NextDouble() * 120),
                    Rot = (float)(rnd.NextDouble() * 360), VRot = (float)((rnd.NextDouble() - 0.5) * 900),
                    W = Theme.S(6) + (float)rnd.NextDouble() * Theme.S(6), H = Theme.S(3) + (float)rnd.NextDouble() * Theme.S(5),
                    C = cols[rnd.Next(cols.Length)]
                });
            }
            timer.Tick += (s, e) => Frame();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x8000000;   // LAYERED | TRANSPARENT (clic atraviesa) | TOOLWINDOW | NOACTIVATE
                return cp;
            }
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void OnShown(EventArgs e) { base.OnShown(e); timer.Start(); Frame(); }

        static double Clamp(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
        static double Back(double x) { x = Clamp(x); const double c1 = 1.70158, c3 = c1 + 1; return 1 + c3 * Math.Pow(x - 1, 3) + c1 * Math.Pow(x - 1, 2); }

        void Frame()
        {
            double t = clock.Elapsed.TotalSeconds;
            if (t > Duration) { timer.Stop(); Close(); return; }
            float dt = 0.016f;
            foreach (var b in bits)
            {
                b.Vy += 900 * dt; b.Vx *= 0.985f; b.Vy *= 0.985f;
                b.X += b.Vx * dt; b.Y += b.Vy * dt; b.Rot += b.VRot * dt;
            }
            double fade = t > Duration - 0.5 ? (Duration - t) / 0.5 : 1;

            using (var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.Transparent);
                    foreach (var b in bits)
                    {
                        if (b.Y > Height + 20) continue;
                        var st = g.Save();
                        g.TranslateTransform(b.X, b.Y); g.RotateTransform(b.Rot);
                        float squash = (float)Math.Abs(Math.Cos(b.Rot * Math.PI / 180));
                        using (var br = new SolidBrush(b.C)) g.FillRectangle(br, -b.W / 2, -b.H * squash / 2, b.W, Math.Max(1, b.H * squash));
                        g.Restore(st);
                    }
                    // insignia central
                    double k = Back(t / 0.45);
                    float cx = Width / 2f, cy = Height * 0.42f, r = (float)(Theme.S(56) * k);
                    if (r > 1)
                    {
                        Color main = success ? Theme.Accent : Theme.Warn;
                        using (var gp = new GraphicsPath())
                        {
                            float gr = r * 1.9f;
                            gp.AddEllipse(cx - gr, cy - gr, gr * 2, gr * 2);
                            using (var pg = new PathGradientBrush(gp) { CenterColor = Theme.Alpha(main, 120), SurroundColors = new[] { Color.Transparent } })
                                g.FillPath(pg, gp);
                        }
                        using (var br = new SolidBrush(main)) g.FillEllipse(br, cx - r, cy - r, r * 2, r * 2);
                        using (var p = new Pen(Color.White, Theme.S(7)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                        {
                            if (success) g.DrawLines(p, new[] { new PointF(cx - r * 0.42f, cy + r * 0.02f), new PointF(cx - r * 0.1f, cy + r * 0.32f), new PointF(cx + r * 0.45f, cy - r * 0.3f) });
                            else { g.DrawLine(p, cx, cy - r * 0.45f, cx, cy + r * 0.12f); g.FillEllipse(Brushes.White, cx - Theme.S(5), cy + r * 0.3f, Theme.S(10), Theme.S(10)); }
                        }
                        double tk = Clamp((t - 0.25) / 0.35);
                        if (tk > 0)
                        {
                            using (var f = Theme.F(22f, FontStyle.Bold))
                            {
                                var sz = g.MeasureString(title, f);
                                float ty = cy + Theme.S(70) + (float)((1 - tk) * Theme.S(12));
                                var pillT = new RectangleF(cx - sz.Width / 2 - Theme.S(16), ty - Theme.S(2), sz.Width + Theme.S(32), sz.Height + Theme.S(4));
                                using (var path = Theme.Round(Rectangle.Round(pillT), (int)(pillT.Height / 2)))
                                using (var br = new SolidBrush(Color.FromArgb((int)(210 * tk), 20, 8, 36))) g.FillPath(br, path);
                                using (var sh = new SolidBrush(Color.FromArgb((int)(150 * tk), 0, 0, 0))) g.DrawString(title, f, sh, cx - sz.Width / 2 + 2, ty + 2);
                                using (var br = new SolidBrush(Color.FromArgb((int)(255 * tk), 255, 255, 255))) g.DrawString(title, f, br, cx - sz.Width / 2, ty);
                            }
                            if (!string.IsNullOrEmpty(sub))
                                using (var f = Theme.F(12f, FontStyle.Bold))
                                {
                                    var sz = g.MeasureString(sub, f);
                                    float ty = cy + Theme.S(122);
                                    var pill = new RectangleF(cx - sz.Width / 2 - Theme.S(12), ty - Theme.S(4), sz.Width + Theme.S(24), sz.Height + Theme.S(8));
                                    using (var path = Theme.Round(Rectangle.Round(pill), (int)(pill.Height / 2)))
                                    using (var br = new SolidBrush(Color.FromArgb((int)(200 * tk), 20, 8, 36))) g.FillPath(br, path);
                                    using (var br = new SolidBrush(Color.FromArgb((int)(255 * tk), 255, 220, 140))) g.DrawString(sub, f, br, cx - sz.Width / 2, ty);
                                }
                        }
                    }
                }
                SetBits(bmp, (byte)(255 * Clamp(fade)));
            }
        }

        void SetBits(Bitmap bmp, byte opacity)
        {
            IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), hbmp = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                hbmp = bmp.GetHbitmap(Color.FromArgb(0));
                old = SelectObject(mem, hbmp);
                var size = new SIZE(bmp.Width, bmp.Height); var src = new POINT(0, 0); var top = new POINT(Left, Top);
                var blend = new BLEND { Op = 0, Flags = 0, Alpha = opacity, Format = 1 };
                UpdateLayeredWindow(Handle, screen, ref top, ref size, mem, ref src, 0, ref blend, 2);
            }
            finally
            {
                if (hbmp != IntPtr.Zero) { SelectObject(mem, old); DeleteObject(hbmp); }
                DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen);
            }
        }

        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }

    // La rueda del ratón desplaza el panel que está bajo el cursor (sin tener que hacer clic antes)
    class WheelFilter : IMessageFilter
    {
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point p);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != 0x20A) return false;   // WM_MOUSEWHEEL
            var c = Control.FromHandle(WindowFromPoint(Cursor.Position));
            if (c == null || c is TextBoxBase || c is ComboBox) return false;
            for (var p = c; p != null; p = p.Parent)
            {
                var sc = p as ScrollableControl;
                if (sc != null && sc.AutoScroll && sc.VerticalScroll.Visible)
                {
                    if (p.Handle == m.HWnd) return false;
                    SendMessage(p.Handle, m.Msg, m.WParam, m.LParam);
                    return true;
                }
            }
            return false;
        }
    }
}
