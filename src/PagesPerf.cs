// RUSCUU Repara - monitor en vivo, test de rendimiento y test de red
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;
using Timer = System.Windows.Forms.Timer;

namespace Ruscuu
{
    // Tarjeta de resultado (benchmark / test de red)
    class StatCard : Control
    {
        public string Title, Value = "–", Sub = "";
        public Color ValueColor = Theme.Text;
        public StatCard(string title) { Title = title; Height = Theme.S(120); Margin = new Padding(Theme.S(8)); SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); }
        public void Set(string value, string sub, Color? color = null) { Value = value; Sub = sub; ValueColor = color ?? Theme.Text; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Prep(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            using (var path = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), Theme.S(10)))
            {
                using (var b = new SolidBrush(Theme.Card)) g.FillPath(b, path);
                using (var p = new Pen(Theme.Line)) g.DrawPath(p, path);
            }
            using (var f = Theme.F(9.5f, FontStyle.Bold)) using (var b = new SolidBrush(Theme.Muted)) g.DrawString(Title, f, b, Theme.S(14), Theme.S(12));
            using (var f = Theme.F(22f, FontStyle.Bold)) using (var b = new SolidBrush(ValueColor)) g.DrawString(Value, f, b, Theme.S(12), Theme.S(34));
            using (var f = Theme.F(8.75f, FontStyle.Regular)) using (var b = new SolidBrush(Theme.Muted))
                g.DrawString(Sub, f, b, new RectangleF(Theme.S(14), Theme.S(80), Width - Theme.S(28), Height - Theme.S(84)));
        }
    }

    partial class MainForm
    {
        [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

        // ---------------------------------------------------------------- Monitor en vivo
        Spark spCpu, spRam, spDisk, spNet;
        Label monTemps, monProcs;
        Timer monTimer;
        long lastIdle, lastKernel, lastUser, lastNet;
        PerformanceCounter diskCounter;
        readonly Dictionary<int, TimeSpan> procTimes = new Dictionary<int, TimeSpan>();
        DateTime procStamp;
        int monTick;
        volatile bool tempBusy;

        void BuildMonitor(FlowLayoutPanel page)
        {
            spCpu = new Spark("PROCESADOR") { Height = Theme.S(190), Tag = "quarter" };
            spRam = new Spark("MEMORIA RAM") { Height = Theme.S(190), Tag = "quarter", LineColor = Theme.Blue };
            spDisk = new Spark("DISCO") { Height = Theme.S(190), Tag = "quarter", LineColor = Theme.Ok };
            spNet = new Spark("RED") { Height = Theme.S(190), Tag = "quarter", LineColor = Theme.Warn, Max = 0 };
            page.Controls.AddRange(new Control[] { spCpu, spRam, spDisk, spNet });
            monTemps = new Label { AutoSize = false, Height = Theme.S(62), Margin = new Padding(Theme.S(8)), BackColor = Theme.Card, ForeColor = Theme.Text, Font = Theme.F(10f, FontStyle.Regular), Padding = new Padding(Theme.S(14), Theme.S(10), Theme.S(10), Theme.S(8)), Tag = "full", Text = "🌡 Temperaturas: midiendo…" };
            monProcs = new Label { AutoSize = false, Height = Theme.S(250), Margin = new Padding(Theme.S(8)), BackColor = Theme.Card, ForeColor = Theme.Text, Font = new Font("Consolas", 9.5f), Padding = new Padding(Theme.S(14), Theme.S(10), Theme.S(10), Theme.S(8)), Tag = "full" };
            page.Controls.Add(monTemps); page.Controls.Add(monProcs);

            monTimer = new Timer { Interval = 1000 };
            monTimer.Tick += (s, e) => MonitorTick();
            onShow[Monitor] = () =>
            {
                try { if (diskCounter == null) diskCounter = new PerformanceCounter("PhysicalDisk", "% Idle Time", "_Total"); } catch { }
                GetSystemTimes(out lastIdle, out lastKernel, out lastUser);
                lastNet = NetBytes();
                monTimer.Start(); MonitorTick();
            };
            onHide[Monitor] = () => monTimer.Stop();
        }

        static long NetBytes()
        {
            long t = 0;
            try
            {
                foreach (var n in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
                { var st = n.GetIPv4Statistics(); t += st.BytesReceived + st.BytesSent; }
            }
            catch { }
            return t;
        }

        static string Rate(double bytesPerSec)
        {
            double bits = bytesPerSec * 8;
            return bits > 1e6 ? (bits / 1e6).ToString("0.0") + " Mbps" : (bits / 1e3).ToString("0") + " kbps";
        }

        void MonitorTick()
        {
            monTick++;
            long idle, kernel, user;
            GetSystemTimes(out idle, out kernel, out user);
            long sys = (kernel - lastKernel) + (user - lastUser);
            double cpu = sys > 0 ? Math.Max(0, Math.Min(100, 100.0 * (sys - (idle - lastIdle)) / sys)) : 0;
            lastIdle = idle; lastKernel = kernel; lastUser = user;
            spCpu.Push((float)cpu, cpu.ToString("0") + " %", Environment.ProcessorCount + " hilos");

            var m = new Native.MEMSTATUS(); Native.GlobalMemoryStatusEx(m);
            spRam.Push(m.dwMemoryLoad, m.dwMemoryLoad + " %", Catalog.Size((long)(m.ullTotalPhys - m.ullAvailPhys)) + " de " + Catalog.Size((long)m.ullTotalPhys));

            float disk = 0;
            try { if (diskCounter != null) disk = Math.Max(0, Math.Min(100, 100 - diskCounter.NextValue())); } catch { }
            spDisk.Push(disk, disk.ToString("0") + " %", "actividad del disco");

            long net = NetBytes();
            double rate = Math.Max(0, net - lastNet); lastNet = net;
            spNet.Push((float)(rate * 8 / 1e6), Rate(rate), "subida + bajada");

            if (monTick % 2 == 1) UpdateProcesses();
            if (monTick % 4 == 1 && !tempBusy) UpdateTemps();
        }

        void UpdateProcesses()
        {
            var now = DateTime.Now;
            double secs = Math.Max(0.5, (now - procStamp).TotalSeconds);
            var rows = new List<Tuple<string, double, long>>();
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    var t = p.TotalProcessorTime;
                    TimeSpan prev;
                    double c = procTimes.TryGetValue(p.Id, out prev) ? (t - prev).TotalSeconds / secs / Environment.ProcessorCount * 100 : 0;
                    procTimes[p.Id] = t;
                    rows.Add(Tuple.Create(p.ProcessName, c, p.WorkingSet64));
                }
                catch { }
                finally { p.Dispose(); }
            }
            procStamp = now;
            var byCpu = rows.OrderByDescending(r => r.Item2).Take(8).ToList();
            var byRam = rows.GroupBy(r => r.Item1).Select(gr => Tuple.Create(gr.Key, gr.Count(), gr.Sum(x => x.Item3))).OrderByDescending(r => r.Item3).Take(8).ToList();
            var sb = new StringBuilder();
            sb.AppendLine(string.Format("{0,-34}{1,8}      {2,-30}{3,12}", "MÁS PROCESADOR", "CPU", "MÁS MEMORIA", "RAM"));
            sb.AppendLine();
            for (int i = 0; i < 8; i++)
            {
                string a = i < byCpu.Count ? string.Format("{0,-34}{1,7:0.0}%", Trim(byCpu[i].Item1, 33), byCpu[i].Item2) : new string(' ', 42);
                string b = i < byRam.Count ? string.Format("{0,-30}{1,12}", Trim(byRam[i].Item1 + (byRam[i].Item2 > 1 ? " (" + byRam[i].Item2 + ")" : ""), 29), Catalog.Size(byRam[i].Item3)) : "";
                sb.AppendLine(a + "      " + b);
            }
            monProcs.Text = sb.ToString();
        }

        static string Trim(string s, int n) { return s.Length > n ? s.Substring(0, n - 1) + "…" : s; }

        void UpdateTemps()
        {
            tempBusy = true;
            Task.Run(() =>
            {
                var parts = new List<string>();
                var zones = Wmi.Query(@"root\wmi", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
                var temps = zones.Select(z => { double v; return double.TryParse(Wmi.Prop(z, "CurrentTemperature"), out v) ? v / 10 - 273.15 : -1; }).Where(v => v > 5 && v < 120).ToList();
                if (temps.Count > 0) parts.Add("Placa/CPU: " + temps.Max().ToString("0") + " °C");
                string smi = Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
                if (File.Exists(smi))
                    try
                    {
                        using (var p = Process.Start(new ProcessStartInfo(smi, "--query-gpu=name,temperature.gpu,utilization.gpu,fan.speed --format=csv,noheader,nounits") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }))
                        {
                            var o = p.StandardOutput.ReadToEnd().Trim().Split(',').Select(x => x.Trim()).ToArray();
                            p.WaitForExit(3000);
                            if (o.Length >= 3) parts.Add(o[0] + ": " + o[1] + " °C  •  uso " + o[2] + " %" + (o.Length > 3 && o[3] != "[N/A]" ? "  •  ventilador " + o[3] + " %" : ""));
                        }
                    }
                    catch { }
                return parts;
            }).ContinueWith(t =>
            {
                tempBusy = false;
                try
                {
                    BeginInvoke((Action)(() => monTemps.Text = "🌡 Temperaturas:  " + (t.Result.Count > 0 ? string.Join("     •     ", t.Result.ToArray()) :
                        "este equipo no informa temperaturas a Windows. Para verlas usa HWiNFO (Instalar programas › Técnico).")));
                }
                catch { }
            });
        }

        // ---------------------------------------------------------------- Test de rendimiento
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteFile(SafeFileHandle h, IntPtr buf, uint n, out uint done, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadFile(SafeFileHandle h, IntPtr buf, uint n, out uint done, IntPtr ov);
        [DllImport("kernel32.dll")] static extern IntPtr VirtualAlloc(IntPtr a, UIntPtr size, uint type, uint protect);
        [DllImport("kernel32.dll")] static extern bool VirtualFree(IntPtr a, UIntPtr size, uint type);

        static ulong benchSink;

        void BuildBench(FlowLayoutPanel page)
        {
            var top = new Panel { Height = Theme.S(70), Margin = new Padding(Theme.S(8)), BackColor = Theme.Card, Tag = "full" };
            var go = Theme.Btn("🏁  Empezar test (≈40 s)", Theme.Accent, Theme.S(230)); go.Location = new Point(Theme.S(14), Theme.S(17));
            var st = Theme.Lbl("Cierra los programas pesados para que el resultado sea fiable. El test de disco escribe 1 GB temporal.", 9f, FontStyle.Regular, Theme.Muted); st.Location = new Point(Theme.S(260), Theme.S(26));
            top.Controls.Add(go); top.Controls.Add(st);
            page.Controls.Add(top);
            var names = new[] { "PROCESADOR · 1 NÚCLEO", "PROCESADOR · TODOS LOS NÚCLEOS", "MEMORIA RAM", "DISCO · ESCRITURA", "DISCO · LECTURA" };
            var cardsB = names.Select(n => new StatCard(n) { Tag = "third" }).ToArray();
            page.Controls.AddRange(cardsB);
            var hist = FullLabel("", Theme.Muted, FontStyle.Regular, 60);
            page.Controls.Add(hist);
            string file = Path.Combine(Settings.DataDir, "rendimiento.csv");
            Func<string[]> lastLine = () => { try { var l = File.ReadAllLines(file).LastOrDefault(x => x.Contains(";")); return l == null ? null : l.Split(';'); } catch { return null; } };
            var pl = lastLine();
            if (pl != null) hist.Text = "Último test: " + pl[0] + ". Pulsa «Empezar» para comparar.";

            go.Click += async (s, e) =>
            {
                if (running) return;
                go.Enabled = false; running = true; SetBusy(true);
                var prevLine = lastLine();
                double[] prev = null;
                try { if (prevLine != null) prev = prevLine.Skip(1).Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray(); } catch { }
                string pd = prevLine != null ? prevLine[0] : "";
                var res = new double[5];
                string[] units = { " pts", " pts", " GB/s", " MB/s", " MB/s" };
                Action<int, double> show = (i, v) =>
                {
                    string cmp = "";
                    Color col = Theme.Text;
                    if (prev != null && prev.Length == 5 && prev[i] > 0)
                    {
                        double d = (v - prev[i]) * 100 / prev[i];
                        cmp = (d >= 0 ? "▲ +" : "▼ ") + d.ToString("0") + "% vs " + pd + " (" + prev[i].ToString(i == 2 ? "0.0" : "N0") + units[i] + ")";
                        col = d > 3 ? Theme.Ok : d < -3 ? Theme.Warn : Theme.Text;
                    }
                    cardsB[i].Set(v.ToString(i == 2 ? "0.0" : "N0") + units[i], cmp == "" ? "Primer test en este equipo" : cmp, col);
                };
                try
                {
                    st.Text = "Midiendo el procesador (1 núcleo)…"; cardsB[0].Set("…", "");
                    res[0] = await Task.Run(() => CpuScore(1, 4000)); show(0, res[0]);
                    st.Text = "Midiendo el procesador (todos los núcleos)…"; cardsB[1].Set("…", "");
                    res[1] = await Task.Run(() => CpuScore(Environment.ProcessorCount, 5000)); show(1, res[1]);
                    st.Text = "Midiendo la memoria RAM…"; cardsB[2].Set("…", "");
                    res[2] = await Task.Run(() => MemSpeed(4000)); show(2, res[2]);
                    st.Text = "Midiendo el disco (escribiendo 1 GB)…"; cardsB[3].Set("…", ""); cardsB[4].Set("…", "");
                    var disk = await Task.Run(() => DiskSpeed());
                    res[3] = disk[0]; res[4] = disk[1]; show(3, res[3]); show(4, res[4]);
                    Directory.CreateDirectory(Settings.DataDir);
                    File.AppendAllText(file, DateTime.Now.ToString("dd/MM/yyyy HH:mm") + ";" + string.Join(";", res.Select(r => r.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).ToArray()) + "\r\n");
                    st.Text = "Test terminado. El resultado se guarda para comparar después de reparar.";
                    hist.Text = "Referencia de discos: disco duro ≈ 100-200 MB/s  •  SSD SATA ≈ 500 MB/s  •  SSD NVMe ≈ 2.000-7.000 MB/s.";
                    Log("🏁 Test de rendimiento: CPU 1 núcleo " + res[0].ToString("N0") + " pts • multinúcleo " + res[1].ToString("N0") + " pts • RAM " + res[2].ToString("0.0") + " GB/s • disco " + res[3].ToString("0") + " / " + res[4].ToString("0") + " MB/s", Theme.Ok);
                    Sfx.Success();
                }
                catch (Exception ex) { st.Text = "Error en el test: " + ex.Message; }
                running = false; SetBusy(false); go.Enabled = true;
            };
        }

        static double CpuScore(int threads, int ms)
        {
            long total = 0;
            var ts = new Thread[threads];
            for (int t = 0; t < threads; t++)
            {
                ts[t] = new Thread(() =>
                {
                    var sw = Stopwatch.StartNew(); long n = 0; ulong x = 88172645463325252UL ^ (ulong)Thread.CurrentThread.ManagedThreadId;
                    while (sw.ElapsedMilliseconds < ms) { for (int i = 0; i < 50000; i++) { x ^= x << 13; x ^= x >> 7; x ^= x << 17; } n++; }
                    Interlocked.Add(ref total, n);
                    benchSink ^= x;
                }) { IsBackground = true, Priority = ThreadPriority.AboveNormal };
                ts[t].Start();
            }
            foreach (var t in ts) t.Join();
            return total * 1000.0 / ms / 10;
        }

        static double MemSpeed(int ms)
        {
            const int size = 128 << 20;
            var a = new byte[size]; var b = new byte[size];
            new Random().NextBytes(a);
            var sw = Stopwatch.StartNew(); long bytes = 0;
            while (sw.ElapsedMilliseconds < ms) { Buffer.BlockCopy(a, 0, b, 0, size); Buffer.BlockCopy(b, 0, a, 0, size); bytes += 4L * size; }
            return bytes / sw.Elapsed.TotalSeconds / 1e9;
        }

        static double[] DiskSpeed()
        {
            string path = Path.Combine(Path.GetTempPath(), "ruscuu_bench.tmp");
            if (new DriveInfo(Path.GetPathRoot(path)).AvailableFreeSpace < 3L << 30) throw new Exception("se necesitan 3 GB libres en el disco del sistema");
            const int block = 8 << 20, blocks = 128;   // 1 GB
            IntPtr buf = VirtualAlloc(IntPtr.Zero, (UIntPtr)block, 0x3000, 0x04);
            try
            {
                var rnd = new byte[block]; new Random().NextBytes(rnd); Marshal.Copy(rnd, 0, buf, block);
                uint done; double w, r;
                using (var h = CreateFile(path, 0x40000000, 0, IntPtr.Zero, 2, 0x20000000 | 0x80000000, IntPtr.Zero))
                {
                    if (h.IsInvalid) throw new Exception("no se pudo crear el archivo de prueba");
                    var sw = Stopwatch.StartNew();
                    for (int i = 0; i < blocks; i++) if (!WriteFile(h, buf, block, out done, IntPtr.Zero)) throw new Exception("error de escritura");
                    w = (double)block * blocks / sw.Elapsed.TotalSeconds / 1e6;
                }
                using (var h = CreateFile(path, 0x80000000, 0, IntPtr.Zero, 3, 0x20000000 | 0x08000000, IntPtr.Zero))
                {
                    if (h.IsInvalid) throw new Exception("no se pudo leer el archivo de prueba");
                    var sw = Stopwatch.StartNew();
                    for (int i = 0; i < blocks; i++) if (!ReadFile(h, buf, block, out done, IntPtr.Zero)) throw new Exception("error de lectura");
                    r = (double)block * blocks / sw.Elapsed.TotalSeconds / 1e6;
                }
                return new[] { w, r };
            }
            finally
            {
                VirtualFree(buf, UIntPtr.Zero, 0x8000);
                try { File.Delete(path); } catch { }
            }
        }

        // ---------------------------------------------------------------- Test de red
        static readonly string[][] DnsServers = {
            new[] { "Cloudflare", "1.1.1.1", "1.0.0.1" }, new[] { "Google", "8.8.8.8", "8.8.4.4" }, new[] { "Quad9 (bloquea malware)", "9.9.9.9", "149.112.112.112" },
            new[] { "AdGuard (bloquea anuncios)", "94.140.14.14", "94.140.15.15" }, new[] { "OpenDNS", "208.67.222.222", "208.67.220.220" } };
        static readonly string[] TestDomains = { "google.com", "youtube.com", "facebook.com", "whatsapp.com", "wikipedia.org", "amazon.com", "microsoft.com", "netflix.com", "tiktok.com", "instagram.com" };

        void BuildNetTest(FlowLayoutPanel page)
        {
            var top = new Panel { Height = Theme.S(70), Margin = new Padding(Theme.S(8)), BackColor = Theme.Card, Tag = "full" };
            var go = Theme.Btn("📶  Probar mi conexión", Theme.Accent, Theme.S(220)); go.Location = new Point(Theme.S(14), Theme.S(17));
            var st = Theme.Lbl("Mide ping, velocidad de bajada y subida (servidores de Cloudflare) y compara 6 servidores DNS.", 9f, FontStyle.Regular, Theme.Muted); st.Location = new Point(Theme.S(250), Theme.S(26));
            top.Controls.Add(go); top.Controls.Add(st);
            page.Controls.Add(top);
            var cPing = new StatCard("PING (LATENCIA)") { Tag = "third" };
            var cDown = new StatCard("BAJADA") { Tag = "third" };
            var cUp = new StatCard("SUBIDA") { Tag = "third" };
            page.Controls.AddRange(new Control[] { cPing, cDown, cUp });
            page.Controls.Add(new CheckRow("Velocidad de los servidores DNS", "", "tiempo medio de respuesta") { Header = true, Checkable = false, Height = Theme.S(36) });
            var dnsRows = new List<CheckRow>();
            var actions = new Panel { Height = Theme.S(56), Margin = new Padding(Theme.S(8)), BackColor = Theme.Bg, Tag = "full" };
            var useBest = Theme.Btn("Usar el DNS más rápido", Theme.Accent, Theme.S(240)); useBest.Location = new Point(0, Theme.S(8)); useBest.Enabled = false;
            var reset = Theme.Btn("Volver a DNS automático", Theme.Card, Theme.S(220)); reset.Location = new Point(Theme.S(252), Theme.S(8));
            actions.Controls.Add(useBest); actions.Controls.Add(reset);
            page.Controls.Add(actions);
            string[] best = null;

            go.Click += async (s, e) =>
            {
                if (running) return;
                go.Enabled = false; useBest.Enabled = false;
                try
                {
                    st.Text = "Midiendo ping…"; cPing.Set("…", ""); cDown.Set("–", ""); cUp.Set("–", "");
                    var ping = await Task.Run(() => PingStats("1.1.1.1", 20));
                    cPing.Set(ping[0] < 0 ? "Sin respuesta" : ping[0].ToString("0") + " ms", ping[0] < 0 ? "No hay conexión a Internet" : "variación " + ping[1].ToString("0") + " ms  •  perdidos " + ping[2].ToString("0") + "%",
                        ping[0] < 0 ? Theme.Err : ping[0] < 40 ? Theme.Ok : ping[0] < 100 ? Theme.Warn : Theme.Err);
                    if (ping[0] >= 0)
                    {
                        st.Text = "Midiendo velocidad de bajada…"; cDown.Set("…", "");
                        double down = await Task.Run(() => Download(8000));
                        cDown.Set(down.ToString("0.0") + " Mbps", "≈ " + (down / 8).ToString("0.0") + " MB/s", down > 50 ? Theme.Ok : down > 10 ? Theme.Warn : Theme.Err);
                        st.Text = "Midiendo velocidad de subida…"; cUp.Set("…", "");
                        double up = await Task.Run(() => Upload(8000));
                        cUp.Set(up.ToString("0.0") + " Mbps", "≈ " + (up / 8).ToString("0.0") + " MB/s", up > 20 ? Theme.Ok : up > 5 ? Theme.Warn : Theme.Err);
                        Log("📶 Internet: ping " + ping[0].ToString("0") + " ms • bajada " + down.ToString("0.0") + " Mbps • subida " + up.ToString("0.0") + " Mbps", Theme.Ok);
                    }
                    st.Text = "Comparando servidores DNS…";
                    foreach (var r in dnsRows) page.Controls.Remove(r);
                    dnsRows.Clear();
                    var current = CurrentDns();
                    var servers = new List<string[]>();
                    if (current.Count > 0) servers.Add(new[] { "Tu DNS actual", current[0], current.Count > 1 ? current[1] : "" });
                    servers.AddRange(DnsServers.Where(d => !current.Contains(d[1])));
                    var times = await Task.Run(() => servers.Select(sv => DnsAvg(sv[1])).ToList());
                    double min = times.Where(t => t < 2000).DefaultIfEmpty(9999).Min();
                    int bi = times.IndexOf(min);
                    for (int i = 0; i < servers.Count; i++)
                    {
                        bool isBest = i == bi;
                        var row = new CheckRow(servers[i][0] + (isBest ? "   ⭐ el más rápido" : ""), servers[i][1] + (servers[i][2] != "" ? "  •  " + servers[i][2] : ""), times[i] >= 2000 ? "sin respuesta" : times[i].ToString("0") + " ms")
                        { Checkable = false, Dot = isBest ? Theme.Ok : times[i] >= 2000 ? Theme.Err : Theme.Muted };
                        dnsRows.Add(row);
                        page.Controls.Add(row); page.Controls.SetChildIndex(row, page.Controls.GetChildIndex(actions));
                    }
                    best = bi >= 0 && servers[bi][0] != "Tu DNS actual" ? servers[bi] : null;
                    useBest.Enabled = best != null;
                    useBest.Text = best != null ? "Usar " + best[0].Split(' ')[0] : "Tu DNS ya es el más rápido";
                    st.Text = "Prueba terminada.";
                    FitPage(page);
                }
                catch (Exception ex) { st.Text = "Error: " + ex.Message; }
                go.Enabled = true;
            };
            useBest.Click += (s, e) =>
            {
                if (best == null) return;
                StartRun(new List<RepairTask> { Catalog.T("dnsset", Catalog.Net, "Cambiar DNS a " + best[0], "", "", false,
                    Catalog.PS("$a = Get-NetAdapter -Physical | Where-Object Status -eq 'Up'; $a | Set-DnsClientServerAddress -ServerAddresses ('" + best[1] + "','" + best[2] + "'); Clear-DnsClientCache; 'DNS cambiado a " + best[1] + " en: ' + (($a.Name) -join ', ')")) }, false);
            };
            reset.Click += (s, e) => StartRun(new List<RepairTask> { Catalog.T("dnsreset", Catalog.Net, "Volver a DNS automático", "", "", false,
                Catalog.PS("$a = Get-NetAdapter -Physical | Where-Object Status -eq 'Up'; $a | Set-DnsClientServerAddress -ResetServerAddresses; Clear-DnsClientCache; 'DNS automático (del router) restaurado.'")) }, false);
        }

        static double[] PingStats(string host, int count)
        {
            var times = new List<long>(); int lost = 0;
            using (var p = new Ping())
                for (int i = 0; i < count; i++)
                {
                    try { var r = p.Send(host, 1500); if (r.Status == IPStatus.Success) times.Add(r.RoundtripTime); else lost++; } catch { lost++; }
                    Thread.Sleep(80);
                }
            if (times.Count == 0) return new double[] { -1, 0, 100 };
            double jitter = times.Count > 1 ? times.Zip(times.Skip(1), (a, b) => (double)Math.Abs(a - b)).Average() : 0;
            return new[] { times.Average(), jitter, lost * 100.0 / count };
        }

        static void PrepNet()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2
            ServicePointManager.DefaultConnectionLimit = 16;
            ServicePointManager.Expect100Continue = false;
        }

        static double Download(int ms)
        {
            PrepNet();
            long total = 0; var sw = Stopwatch.StartNew();
            var threads = Enumerable.Range(0, 4).Select(i => new Thread(() =>
            {
                var buf = new byte[1 << 16];
                while (sw.ElapsedMilliseconds < ms)
                    try
                    {
                        var req = (HttpWebRequest)WebRequest.Create("https://speed.cloudflare.com/__down?bytes=50000000");
                        req.UserAgent = "RUSCUU-Repara"; req.Timeout = 10000;
                        using (var resp = req.GetResponse()) using (var st = resp.GetResponseStream())
                        {
                            int n;
                            while ((n = st.Read(buf, 0, buf.Length)) > 0) { Interlocked.Add(ref total, n); if (sw.ElapsedMilliseconds >= ms) break; }
                        }
                    }
                    catch { Thread.Sleep(200); }
            }) { IsBackground = true }).ToList();
            threads.ForEach(t => t.Start());
            threads.ForEach(t => t.Join(ms + 3000));
            return total * 8 / sw.Elapsed.TotalSeconds / 1e6;
        }

        static double Upload(int ms)
        {
            PrepNet();
            long total = 0; var sw = Stopwatch.StartNew();
            var data = new byte[1 << 16]; new Random().NextBytes(data);
            var threads = Enumerable.Range(0, 3).Select(i => new Thread(() =>
            {
                while (sw.ElapsedMilliseconds < ms)
                    try
                    {
                        var req = (HttpWebRequest)WebRequest.Create("https://speed.cloudflare.com/__up");
                        req.Method = "POST"; req.UserAgent = "RUSCUU-Repara"; req.Timeout = 15000; req.ContentLength = 16 << 20; req.ContentType = "application/octet-stream";
                        req.AllowWriteStreamBuffering = false;
                        using (var st = req.GetRequestStream())
                            for (long sent = 0; sent < req.ContentLength; sent += data.Length)
                            {
                                st.Write(data, 0, data.Length);
                                Interlocked.Add(ref total, data.Length);
                                if (sw.ElapsedMilliseconds >= ms) break;
                            }
                        if (sw.ElapsedMilliseconds < ms) using (req.GetResponse()) { }
                    }
                    catch { if (sw.ElapsedMilliseconds < ms) Thread.Sleep(200); }
            }) { IsBackground = true }).ToList();
            threads.ForEach(t => t.Start());
            threads.ForEach(t => t.Join(ms + 3000));
            return total * 8 / sw.Elapsed.TotalSeconds / 1e6;
        }

        static List<string> CurrentDns()
        {
            var r = new List<string>();
            try
            {
                foreach (var n in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback && n.GetIPProperties().GatewayAddresses.Count > 0))
                    foreach (var d in n.GetIPProperties().DnsAddresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork))
                        if (!r.Contains(d.ToString())) r.Add(d.ToString());
            }
            catch { }
            return r;
        }

        static double DnsAvg(string server)
        {
            var times = new List<double>();
            foreach (var dom in TestDomains) times.Add(DnsQuery(server, dom));
            return times.Average();
        }

        static readonly Random dnsRnd = new Random();

        // consulta DNS tipo A por UDP; devuelve milisegundos (2000 si falla)
        static double DnsQuery(string server, string domain)
        {
            try
            {
                ushort id;
                lock (dnsRnd) id = (ushort)dnsRnd.Next(1, 65535);
                var q = new List<byte> { (byte)(id >> 8), (byte)id, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0 };
                foreach (var label in domain.Split('.')) { q.Add((byte)label.Length); q.AddRange(Encoding.ASCII.GetBytes(label)); }
                q.AddRange(new byte[] { 0, 0, 1, 0, 1 });
                using (var udp = new UdpClient())
                {
                    udp.Client.ReceiveTimeout = 2000;
                    var ep = new IPEndPoint(IPAddress.Parse(server), 53);
                    var sw = Stopwatch.StartNew();
                    udp.Send(q.ToArray(), q.Count, ep);
                    while (sw.ElapsedMilliseconds < 2000)
                    {
                        var from = new IPEndPoint(IPAddress.Any, 0);
                        var resp = udp.Receive(ref from);
                        if (resp.Length > 3 && resp[0] == (byte)(id >> 8) && resp[1] == (byte)id) return (resp[3] & 0x0F) == 0 ? sw.Elapsed.TotalMilliseconds : 2000;
                    }
                }
            }
            catch { }
            return 2000;
        }
    }
}
