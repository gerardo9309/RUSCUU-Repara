// RUSCUU Repara - animación de bienvenida
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Windows.Forms;

namespace Ruscuu
{
    class Splash : Form
    {
        const double Total = 3.2, FadeIn = 0.35, FadeOut = 0.4;

        class Spark { public float X, Y, Vx, Vy, Size; public double Born, Life; public Color Col; }

        readonly Image logo;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Timer timer = new Timer { Interval = 15 };
        readonly List<Spark> sparks = new List<Spark>();
        readonly Random rnd = new Random();
        double skipAt = -1;
        float sc = 1f;

        static readonly string[] Status = { "Revisando el sistema…", "Cargando herramientas de reparación…", "Preparando todo para ti…", "¡Listo!" };

        public Splash()
        {
            using (var g = CreateGraphics()) sc = g.DpiX / 96f;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(S(560), S(440));
            BackColor = Theme.Bg;
            ShowInTaskbar = true;
            Text = "RUSCUU Repara";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Opacity = 0;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            try { logo = Image.FromStream(Assembly.GetExecutingAssembly().GetManifestResourceStream("logo.png")); } catch { }
            Cursor = Cursors.Hand;
            Click += (s, e) => Skip();
            KeyPreview = true;
            KeyDown += (s, e) => Skip();
            timer.Tick += (s, e) => Frame();
        }

        int S(int v) { return (int)Math.Round(v * sc); }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int round = 2; Native.DwmSetWindowAttribute(Handle, 33, ref round, 4); } catch { }   // esquinas redondeadas (Windows 11)
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); timer.Start(); }

        void Skip()
        {
            double t = clock.Elapsed.TotalSeconds;
            if (skipAt < 0 && t < Total - FadeOut) skipAt = t;
        }

        double End { get { return skipAt >= 0 ? skipAt + FadeOut : Total; } }

        void Frame()
        {
            double t = clock.Elapsed.TotalSeconds;
            if (t >= End) { timer.Stop(); Close(); return; }
            double fadeStart = End - FadeOut;
            Opacity = t < FadeIn ? Ease(t / FadeIn) : t > fadeStart ? 1 - (t - fadeStart) / FadeOut : 1;

            // chispas que suben
            if (t < End - 0.2)
                if (rnd.NextDouble() < 0.55)
                    sparks.Add(new Spark
                    {
                        X = (float)(rnd.NextDouble() * ClientSize.Width), Y = ClientSize.Height + S(4),
                        Vx = (float)((rnd.NextDouble() - 0.5) * S(30)), Vy = -(float)(S(50) + rnd.NextDouble() * S(110)),
                        Size = (float)(S(1) + rnd.NextDouble() * S(3)), Born = t, Life = 1.6 + rnd.NextDouble() * 1.6,
                        Col = rnd.Next(5) == 0 ? Color.FromArgb(255, 200, 120) : Color.FromArgb(185, 110, 255)
                    });
            sparks.RemoveAll(p => t - p.Born > p.Life);
            Invalidate();
        }

        // ---------------------------------------------------------------- funciones de suavizado
        static double Clamp(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
        static double Ease(double x) { x = Clamp(x); return 1 - Math.Pow(1 - x, 3); }
        static double Back(double x) { x = Clamp(x); const double c1 = 1.70158, c3 = c1 + 1; return 1 + c3 * Math.Pow(x - 1, 3) + c1 * Math.Pow(x - 1, 2); }
        static double Seg(double t, double a, double b) { return Clamp((t - a) / (b - a)); }

        protected override void OnPaint(PaintEventArgs e)
        {
            double t = clock.Elapsed.TotalSeconds;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            int W = ClientSize.Width, Hh = ClientSize.Height;

            // fondo con degradado
            using (var bg = new LinearGradientBrush(new Rectangle(0, 0, W, Hh), Color.FromArgb(34, 14, 60), Color.FromArgb(12, 4, 24), 90f))
                g.FillRectangle(bg, 0, 0, W, Hh);

            float cx = W / 2f, cy = S(160);
            float R = S(92);

            // halo que late detrás del logo
            double pulse = 0.5 + 0.5 * Math.Sin(t * 3.2);
            float glowR = R * (1.55f + 0.12f * (float)pulse);
            using (var gp = new GraphicsPath())
            {
                gp.AddEllipse(cx - glowR, cy - glowR, glowR * 2, glowR * 2);
                using (var pg = new PathGradientBrush(gp))
                {
                    pg.CenterColor = Color.FromArgb((int)(110 * Ease(t / 0.8) * (0.7 + 0.3 * pulse)), 160, 82, 255);
                    pg.SurroundColors = new[] { Color.FromArgb(0, 160, 82, 255) };
                    g.FillPath(pg, gp);
                }
            }

            // chispas
            foreach (var p in sparks)
            {
                double age = t - p.Born, k = age / p.Life;
                float x = p.X + p.Vx * (float)age + (float)Math.Sin(age * 3 + p.X) * S(6);
                float y = p.Y + p.Vy * (float)age;
                int a = (int)(170 * Math.Sin(Math.PI * Clamp(k)));
                using (var b = new SolidBrush(Color.FromArgb(Math.Max(0, a / 3), p.Col))) g.FillEllipse(b, x - p.Size * 2, y - p.Size * 2, p.Size * 4, p.Size * 4);
                using (var b = new SolidBrush(Color.FromArgb(Math.Max(0, a), p.Col))) g.FillEllipse(b, x - p.Size / 2, y - p.Size / 2, p.Size, p.Size);
            }

            // anillos de neón girando
            double ringIn = Ease(Seg(t, 0.1, 0.9));
            if (ringIn > 0)
            {
                float rr = R + S(16);
                var rect = new RectangleF(cx - rr, cy - rr, rr * 2, rr * 2);
                float a1 = (float)(t * 220), sweep = (float)(250 * ringIn);
                for (int i = 4; i >= 1; i--)
                    using (var pen = new Pen(Color.FromArgb((int)(40 * ringIn), 170, 90, 255), S(3) * i) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawArc(pen, rect, a1, sweep);
                using (var pen = new Pen(Color.FromArgb((int)(255 * ringIn), 200, 140, 255), S(3)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(pen, rect, a1, sweep);

                float rr2 = R + S(28);
                var rect2 = new RectangleF(cx - rr2, cy - rr2, rr2 * 2, rr2 * 2);
                using (var pen = new Pen(Color.FromArgb((int)(170 * ringIn), 255, 196, 120), S(2)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawArc(pen, rect2, (float)(-t * 160), (float)(70 * ringIn));
                    g.DrawArc(pen, rect2, (float)(-t * 160) + 180, (float)(70 * ringIn));
                }
            }

            // logo con rebote dentro de un círculo
            double li = Seg(t, 0.15, 0.95);
            if (li > 0 && logo != null)
            {
                float s = (float)(0.35 + 0.65 * Back(li));
                float r = R * s;
                using (var clip = new GraphicsPath())
                {
                    clip.AddEllipse(cx - r, cy - r, r * 2, r * 2);
                    var state = g.Save();
                    g.SetClip(clip);
                    var cm = new ColorMatrix { Matrix33 = (float)Ease(li * 1.4) };
                    using (var ia = new ImageAttributes())
                    {
                        ia.SetColorMatrix(cm);
                        g.DrawImage(logo, new Rectangle((int)(cx - r * 1.08f), (int)(cy - r * 1.08f), (int)(r * 2.16f), (int)(r * 2.16f)), 0, 0, logo.Width, logo.Height, GraphicsUnit.Pixel, ia);
                    }
                    // destello de luz que cruza el logo
                    double sweepK = Seg(t, 1.05, 1.6);
                    if (sweepK > 0 && sweepK < 1)
                    {
                        float sx = cx - r * 2 + (float)(sweepK * r * 4);
                        var band = new RectangleF(sx - r * 0.5f, cy - r, r, r * 2);
                        using (var lb = new LinearGradientBrush(band, Color.FromArgb(0, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 20f))
                        {
                            var blend = new ColorBlend
                            {
                                Colors = new[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb(150, 255, 255, 255), Color.FromArgb(0, 255, 255, 255) },
                                Positions = new[] { 0f, 0.5f, 1f }
                            };
                            lb.InterpolationColors = blend;
                            g.FillRectangle(lb, band);
                        }
                    }
                    g.Restore(state);
                }
                using (var pen = new Pen(Color.FromArgb((int)(255 * Ease(li)), 160, 82, 255), S(3)))
                    g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
            }

            // título: las letras de RUSCUU caen una a una
            const string word = "RUSCUU";
            using (var f = new Font("Segoe UI Black", 34f, FontStyle.Bold, GraphicsUnit.Point))
            using (var fmt = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                var widths = new float[word.Length];
                float total = 0;
                for (int i = 0; i < word.Length; i++) { widths[i] = g.MeasureString(word[i].ToString(), f, 1000, fmt).Width + S(4); total += widths[i]; }
                float x = cx - total / 2, baseY = S(292);
                for (int i = 0; i < word.Length; i++)
                {
                    double k = Seg(t, 0.85 + i * 0.07, 1.25 + i * 0.07);
                    if (k > 0)
                    {
                        float dy = (float)((1 - Back(k)) * -S(40));
                        int a = (int)(255 * Ease(k));
                        using (var sh = new SolidBrush(Color.FromArgb(a / 2, 120, 50, 220)))
                            g.DrawString(word[i].ToString(), f, sh, x + S(2), baseY + dy + S(3), fmt);
                        using (var b = new LinearGradientBrush(new RectangleF(x, baseY + dy, widths[i], S(50)), Color.FromArgb(a, 255, 245, 225), Color.FromArgb(a, 205, 190, 170), 90f))
                            g.DrawString(word[i].ToString(), f, b, x, baseY + dy, fmt);
                    }
                    x += widths[i];
                }
            }
            double sub = Ease(Seg(t, 1.35, 1.8));
            if (sub > 0)
                using (var f = Theme.F(13f, FontStyle.Bold))
                using (var b = new SolidBrush(Color.FromArgb((int)(255 * sub), 190, 130, 255)))
                {
                    string txt = "R E P A R A";
                    var sz = g.MeasureString(txt, f);
                    g.DrawString(txt, f, b, cx - sz.Width / 2, S(352) + (float)((1 - sub) * S(10)));
                }

            // barra de carga con mensajes
            double load = Ease(Seg(t, 1.4, Total - 0.5));
            double barIn = Ease(Seg(t, 1.3, 1.6));
            if (barIn > 0)
            {
                int bw = S(260), bh = S(4), bx = (int)(cx - bw / 2), by = S(392);
                using (var b = new SolidBrush(Color.FromArgb((int)(120 * barIn), 70, 40, 110)))
                using (var path = Theme.Round(new Rectangle(bx, by, bw, bh), bh / 2)) g.FillPath(b, path);
                int fw = Math.Max(bh, (int)(bw * load));
                using (var b = new LinearGradientBrush(new Rectangle(bx, by, fw, bh), Color.FromArgb((int)(255 * barIn), 120, 50, 220), Color.FromArgb((int)(255 * barIn), 210, 150, 255), 0f))
                using (var path = Theme.Round(new Rectangle(bx, by, fw, bh), bh / 2)) g.FillPath(b, path);
                // brillo en la punta de la barra
                using (var b = new SolidBrush(Color.FromArgb((int)(90 * barIn), 220, 170, 255))) g.FillEllipse(b, bx + fw - S(6), by - S(4), S(12), S(12));

                string msg = load >= 0.999 ? Status[Status.Length - 1] : Status[Math.Min(Status.Length - 2, (int)(load * (Status.Length - 1)))];
                using (var f = Theme.F(8.5f, FontStyle.Regular))
                using (var b = new SolidBrush(Color.FromArgb((int)(200 * barIn), 170, 155, 190)))
                {
                    var sz = g.MeasureString(msg, f);
                    g.DrawString(msg, f, b, cx - sz.Width / 2, by + S(10));
                }
            }

            // borde fino morado
            using (var pen = new Pen(Color.FromArgb(90, 160, 82, 255), 1))
                g.DrawRectangle(pen, 0, 0, W - 1, Hh - 1);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Dispose(); if (logo != null) logo.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
