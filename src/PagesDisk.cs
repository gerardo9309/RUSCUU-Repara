// RUSCUU Repara - espacio en disco (mapa visual), duplicados/archivos grandes y desinstalador
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using SearchOption = System.IO.SearchOption;

namespace Ruscuu
{
    static class Recycle
    {
        public static bool Send(string path)
        {
            try
            {
                if (Directory.Exists(path)) FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                else FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                return true;
            }
            catch { return false; }
        }
        public static void Reveal(string path) { try { Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { } }
    }

    // ================================================================ Mapa de espacio
    class DirNode
    {
        public string Name, Path;
        public long Size;
        public bool IsFiles;
        public DirNode Parent;
        public List<DirNode> Kids = new List<DirNode>();
    }

    class Treemap : Control
    {
        public DirNode Root;
        public event Action<DirNode> Open, Reveal;
        readonly List<KeyValuePair<DirNode, RectangleF>> boxes = new List<KeyValuePair<DirNode, RectangleF>>();
        DirNode hot;
        static readonly Color[] Palette = { Color.FromArgb(160, 82, 255), Color.FromArgb(90, 160, 255), Color.FromArgb(40, 190, 140), Color.FromArgb(255, 170, 60), Color.FromArgb(240, 90, 160), Color.FromArgb(120, 120, 255), Color.FromArgb(230, 110, 80), Color.FromArgb(60, 200, 210) };

        public Treemap() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); Cursor = Cursors.Hand; Margin = new Padding(Theme.S(8)); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var n = boxes.Where(b => b.Value.Contains(e.Location)).Select(b => b.Key).FirstOrDefault();
            if (n != hot) { hot = n; Invalidate(); }
        }
        protected override void OnMouseLeave(EventArgs e) { hot = null; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            var n = boxes.Where(b => b.Value.Contains(e.Location)).Select(b => b.Key).FirstOrDefault();
            if (n == null) return;
            if (e.Button == MouseButtons.Right) { if (Reveal != null) Reveal(n); }
            else if (Open != null) Open(n);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Prep(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            boxes.Clear();
            var area = new RectangleF(0, 0, Width - 1, Height - 1);
            if (Root == null || Root.Size <= 0)
            {
                using (var f = Theme.F(11f, FontStyle.Regular)) using (var b = new SolidBrush(Theme.Muted))
                {
                    string msg = "Elige una unidad y pulsa «Analizar».";
                    var sz = g.MeasureString(msg, f); g.DrawString(msg, f, b, (Width - sz.Width) / 2, (Height - sz.Height) / 2);
                }
                return;
            }
            var kids = Root.Kids.Where(k => k.Size > 0).OrderByDescending(k => k.Size).Take(80).ToList();
            LayoutBoxes(kids, area);
            int i = 0;
            using (var white = new SolidBrush(Color.FromArgb(220, 255, 255, 255)))
                foreach (var kv in boxes)
                {
                    var r = kv.Value; var n = kv.Key;
                    Color c = n.IsFiles ? Theme.Track : Palette[i++ % Palette.Length];
                    if (!Theme.Dark && !n.IsFiles) c = Theme.Mix(c, Color.White, 0.2);
                    if (n == hot) c = ControlPaint.Light(c, 0.3f);
                    var rr = new RectangleF(r.X + 1.5f, r.Y + 1.5f, Math.Max(1, r.Width - 3), Math.Max(1, r.Height - 3));
                    using (var b = new LinearGradientBrush(new RectangleF(rr.X, rr.Y, rr.Width + 1, rr.Height + 1), c, ControlPaint.Dark(c, 0.15f), 45f)) g.FillRectangle(b, rr);
                    if (rr.Width > Theme.S(60) && rr.Height > Theme.S(34))
                        using (var f = Theme.F(rr.Width > Theme.S(160) && rr.Height > Theme.S(60) ? 10f : 8.5f, FontStyle.Bold))
                        using (var f2 = Theme.F(8.5f, FontStyle.Regular))
                        using (var fmt = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                        {
                            g.DrawString((n.Kids.Count > 0 ? "📁 " : "") + n.Name, f, Brushes.White, new RectangleF(rr.X + 6, rr.Y + 5, rr.Width - 12, Theme.S(20)), fmt);
                            g.DrawString(Catalog.Size(n.Size) + "  •  " + (n.Size * 100.0 / Root.Size).ToString("0.#") + "%", f2, white, new RectangleF(rr.X + 6, rr.Y + Theme.S(23), rr.Width - 12, Theme.S(18)), fmt);
                        }
                }
            if (hot != null)
                using (var f = Theme.F(9f, FontStyle.Bold))
                {
                    string tip = hot.Path + "   —   " + Catalog.Size(hot.Size) + (hot.Kids.Count > 0 ? "   (clic para entrar, clic derecho para abrir)" : "   (clic derecho para abrir)");
                    var sz = g.MeasureString(tip, f);
                    var tr = new RectangleF(6, Height - sz.Height - 12, Math.Min(Width - 12, sz.Width + 16), sz.Height + 6);
                    using (var b = new SolidBrush(Color.FromArgb(220, 15, 6, 28))) g.FillRectangle(b, tr);
                    g.DrawString(tip, f, Brushes.White, tr.X + 8, tr.Y + 3);
                }
        }

        // algoritmo "squarified treemap"
        void LayoutBoxes(List<DirNode> nodes, RectangleF rect)
        {
            double total = nodes.Sum(n => (double)n.Size);
            if (total <= 0) return;
            var areas = nodes.Select(n => n.Size * (double)rect.Width * rect.Height / total).ToList();
            int i = 0; var r = rect;
            while (i < nodes.Count && r.Width > 1 && r.Height > 1)
            {
                bool horiz = r.Width >= r.Height;
                double side = horiz ? r.Height : r.Width;
                int j = i; double sum = 0, best = double.MaxValue;
                while (j < nodes.Count)
                {
                    double s2 = sum + areas[j];
                    double worst = 0;
                    for (int k = i; k <= j; k++) worst = Math.Max(worst, Math.Max(side * side * areas[k] / (s2 * s2), s2 * s2 / (side * side * areas[k])));
                    if (worst > best) break;
                    best = worst; sum = s2; j++;
                }
                double thick = sum / side, off = 0;
                for (int k = i; k < j; k++)
                {
                    double len = areas[k] / thick;
                    var box = horiz ? new RectangleF(r.X, (float)(r.Y + off), (float)thick, (float)len) : new RectangleF((float)(r.X + off), r.Y, (float)len, (float)thick);
                    boxes.Add(new KeyValuePair<DirNode, RectangleF>(nodes[k], box));
                    off += len;
                }
                r = horiz ? new RectangleF((float)(r.X + thick), r.Y, (float)(r.Width - thick), r.Height) : new RectangleF(r.X, (float)(r.Y + thick), r.Width, (float)(r.Height - thick));
                i = j;
            }
        }
    }

    partial class MainForm
    {
        // ---------------------------------------------------------------- Espacio en disco
        Treemap treemap;
        Label spacePath;
        readonly List<Control> bigFileRows = new List<Control>();
        volatile bool spaceCancel;
        long scanCount;

        void BuildSpace(FlowLayoutPanel page)
        {
            var top = new Panel { Height = Theme.S(56), Margin = new Padding(Theme.S(8)), BackColor = Theme.Card, Tag = "full" };
            var drives = Theme.Combo(Theme.S(240), DriveInfo.GetDrives().Where(d => d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable))
                .Select(d => d.Name + "  " + Catalog.Size(d.TotalSize - d.TotalFreeSpace) + " usados de " + Catalog.Size(d.TotalSize)).ToArray());
            drives.Location = new Point(Theme.S(14), Theme.S(15));
            var scan = Theme.Btn("Analizar", Theme.Accent, Theme.S(110)); scan.Location = new Point(Theme.S(266), Theme.S(10));
            var up = Theme.Btn("⬆ Subir", Theme.Bg, Theme.S(100)); up.Location = new Point(Theme.S(386), Theme.S(10));
            spacePath = Theme.Lbl("", 9f, FontStyle.Regular, Theme.Muted); spacePath.Location = new Point(Theme.S(500), Theme.S(19));
            top.Controls.AddRange(new Control[] { drives, scan, up, spacePath });
            page.Controls.Add(top);

            treemap = new Treemap { Height = Theme.S(400), Tag = "full" };
            treemap.Open += n => { if (n.Kids.Count > 0) { treemap.Root = n; spacePath.Text = n.Path + "  —  " + Catalog.Size(n.Size); treemap.Invalidate(); } };
            treemap.Reveal += n => Process.Start("explorer.exe", "\"" + (n.IsFiles ? n.Parent.Path : n.Path) + "\"");
            page.Controls.Add(treemap);
            up.Click += (s, e) => { if (treemap.Root != null && treemap.Root.Parent != null) { treemap.Root = treemap.Root.Parent; spacePath.Text = treemap.Root.Path + "  —  " + Catalog.Size(treemap.Root.Size); treemap.Invalidate(); } };
            scan.Click += async (s, e) =>
            {
                if (scan.Text == "Detener") { spaceCancel = true; return; }
                string root = drives.SelectedItem == null ? "C:\\" : drives.SelectedItem.ToString().Substring(0, 3);
                spaceCancel = false; scan.Text = "Detener"; scanCount = 0;
                var big = new List<KeyValuePair<string, long>>();
                var timer = new Timer { Interval = 250 };
                timer.Tick += (s2, e2) => spacePath.Text = "Analizando " + root + "…  " + scanCount.ToString("N0") + " archivos";
                timer.Start();
                var node = await Task.Run(() => ScanDir(new DirectoryInfo(root), null, big));
                timer.Stop(); timer.Dispose();
                scan.Text = "Analizar";
                treemap.Root = node; treemap.Invalidate();
                spacePath.Text = (spaceCancel ? "Análisis detenido (resultado parcial): " : "") + node.Path + "  —  " + Catalog.Size(node.Size) + " en " + scanCount.ToString("N0") + " archivos";
                foreach (var c in bigFileRows) page.Controls.Remove(c);
                bigFileRows.Clear();
                bigFileRows.Add(new CheckRow("Los 25 archivos más grandes", "", "clic: mostrar en el Explorador") { Header = true, Checkable = false, Height = Theme.S(36) });
                foreach (var f in big.OrderByDescending(b => b.Value).Take(25))
                {
                    var row = new CheckRow(Path.GetFileName(f.Key), Path.GetDirectoryName(f.Key), Catalog.Size(f.Value)) { Checkable = false };
                    string path = f.Key;
                    row.Toggled += (s3, e3) => Recycle.Reveal(path);
                    bigFileRows.Add(row);
                }
                foreach (var c in bigFileRows) page.Controls.Add(c);
                FitPage(page);
            };
        }

        DirNode ScanDir(DirectoryInfo dir, DirNode parent, List<KeyValuePair<string, long>> big)
        {
            var node = new DirNode { Name = dir.Parent == null ? dir.FullName : dir.Name, Path = dir.FullName, Parent = parent };
            long own = 0;
            List<FileSystemInfo> entries;
            try { entries = dir.EnumerateFileSystemInfos().ToList(); } catch { return node; }
            foreach (var e in entries)
            {
                if (spaceCancel) break;
                try
                {
                    if ((e.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if ((e.Attributes & FileAttributes.Directory) != 0)
                    {
                        var k = ScanDir((DirectoryInfo)e, node, big);
                        if (k.Size > 0) node.Kids.Add(k);
                    }
                    else
                    {
                        long len = ((FileInfo)e).Length;
                        own += len; scanCount++;
                        if (len > 100L << 20) big.Add(new KeyValuePair<string, long>(e.FullName, len));
                    }
                }
                catch { }
            }
            if (own > 0 && node.Kids.Count > 0) node.Kids.Add(new DirNode { Name = "(archivos sueltos)", Path = node.Path, Size = own, IsFiles = true, Parent = node });
            node.Size = own + node.Kids.Where(k => !k.IsFiles).Sum(k => k.Size);
            return node;
        }

        // ---------------------------------------------------------------- Duplicados y archivos grandes
        Label dupStatus;
        readonly List<CheckRow> dupRows = new List<CheckRow>();
        volatile bool dupCancel;

        void BuildDuplicates(FlowLayoutPanel page)
        {
            var top = new Panel { Height = Theme.S(104), Margin = new Padding(Theme.S(8)), BackColor = Theme.Card, Tag = "full" };
            var l1 = Theme.Lbl("Buscar en", 8.5f, FontStyle.Regular, Theme.Muted); l1.Location = new Point(Theme.S(14), Theme.S(8));
            var where = Theme.Combo(Theme.S(250), "Mis carpetas (Escritorio, Documentos…)", "Disco del sistema completo", "Elegir una carpeta…");
            where.Location = new Point(Theme.S(14), Theme.S(28));
            var l2 = Theme.Lbl("Tamaño mínimo", 8.5f, FontStyle.Regular, Theme.Muted); l2.Location = new Point(Theme.S(278), Theme.S(8));
            var min = Theme.Combo(Theme.S(100), "1 MB", "10 MB", "100 MB"); min.Location = new Point(Theme.S(278), Theme.S(28));
            var dupBtn = Theme.Btn("Buscar duplicados", Theme.Accent, Theme.S(170)); dupBtn.Location = new Point(Theme.S(392), Theme.S(22));
            var bigBtn = Theme.Btn("Archivos > 500 MB", Theme.Bg, Theme.S(160)); bigBtn.Location = new Point(Theme.S(570), Theme.S(22));
            var del = Theme.Btn("🗑  A la papelera", Theme.Err, Theme.S(160)); del.Location = new Point(Theme.S(738), Theme.S(22));
            dupStatus = Theme.Lbl("Los archivos que elimines van a la papelera de reciclaje: puedes recuperarlos.", 9f, FontStyle.Regular, Theme.Muted); dupStatus.Location = new Point(Theme.S(14), Theme.S(72));
            top.Controls.AddRange(new Control[] { l1, where, l2, min, dupBtn, bigBtn, del, dupStatus });
            page.Controls.Add(top);

            Func<List<string>> roots = () =>
            {
                if (where.SelectedIndex == 0) return ClientData.Folders().Select(f => f[1]).Where(Directory.Exists).Distinct().ToList();
                if (where.SelectedIndex == 1) return new List<string> { (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:") + "\\" };
                using (var fb = new FolderBrowserDialog { Description = "Elige la carpeta donde buscar" })
                    return fb.ShowDialog(this) == DialogResult.OK ? new List<string> { fb.SelectedPath } : new List<string>();
            };
            Action clear = () => { foreach (var r in dupRows) page.Controls.Remove(r); dupRows.Clear(); };
            Action updateSel = () =>
            {
                var sel = dupRows.Where(r => r.Checked && r.Data is FileInfo).Select(r => (FileInfo)r.Data).ToList();
                dupStatus.Text = sel.Count == 0 ? "Marca los archivos que quieras eliminar. Clic derecho: mostrar en el Explorador." : sel.Count + " archivos marcados  •  liberarías " + Catalog.Size(sel.Sum(f => f.Length));
            };

            dupBtn.Click += async (s, e) =>
            {
                if (dupBtn.Text == "Detener") { dupCancel = true; return; }
                var r = roots(); if (r.Count == 0) return;
                long minSize = new[] { 1L << 20, 10L << 20, 100L << 20 }[min.SelectedIndex];
                dupCancel = false; dupBtn.Text = "Detener"; clear();
                var progress = new Progress<string>(m => dupStatus.Text = m);
                var groups = await Task.Run(() => FindDuplicates(r, minSize, progress));
                dupBtn.Text = "Buscar duplicados";
                long waste = groups.Sum(g => g.Skip(1).Sum(f => f.Length));
                page.SuspendLayout();
                foreach (var g in groups.OrderByDescending(g => g.Skip(1).Sum(f => f.Length)).Take(250))
                {
                    dupRows.Add(new CheckRow(g.Count + " copias de «" + g[0].Name + "»", "", Catalog.Size(g[0].Length) + " cada una") { Header = true, Checkable = false, Height = Theme.S(36) });
                    bool first = true;
                    foreach (var f in g.OrderBy(x => x.CreationTime))
                    {
                        var file = f;
                        var row = new CheckRow(f.Name, f.DirectoryName, f.LastWriteTime.ToString("dd/MM/yyyy")) { Checked = !first, Data = f };
                        row.Toggled += (s2, e2) => updateSel();
                        row.Secondary += (s2, e2) => Recycle.Reveal(file.FullName);
                        dupRows.Add(row); first = false;
                    }
                }
                foreach (var row in dupRows) page.Controls.Add(row);
                page.ResumeLayout();
                FitPage(page);
                if (groups.Count > 0) updateSel();
                dupStatus.Text = groups.Count == 0 ? "No se encontraron archivos duplicados. ✨" : groups.Count + " grupos de duplicados  •  " + Catalog.Size(waste) + " repetidos. Se marcan todas las copias menos la más antigua.";
            };

            bigBtn.Click += async (s, e) =>
            {
                var r = roots(); if (r.Count == 0) return;
                dupCancel = false; clear(); dupStatus.Text = "Buscando archivos grandes…";
                var list = await Task.Run(() => r.SelectMany(root => EnumFiles(root)).Where(f => { try { return f.Length > 500L << 20; } catch { return false; } }).OrderByDescending(f => f.Length).Take(150).ToList());
                dupStatus.Text = list.Count == 0 ? "No hay archivos de más de 500 MB." : list.Count + " archivos de más de 500 MB  •  " + Catalog.Size(list.Sum(f => f.Length)) + " en total. Clic derecho: mostrar en el Explorador.";
                page.SuspendLayout();
                foreach (var f in list)
                {
                    var file = f;
                    var row = new CheckRow(f.Name, f.DirectoryName + "  •  último uso " + f.LastAccessTime.ToString("dd/MM/yyyy"), Catalog.Size(f.Length)) { Data = f };
                    row.Toggled += (s2, e2) => updateSel();
                    row.Secondary += (s2, e2) => Recycle.Reveal(file.FullName);
                    dupRows.Add(row); page.Controls.Add(row);
                }
                page.ResumeLayout();
                FitPage(page);
            };

            del.Click += (s, e) =>
            {
                // nunca borrar todas las copias de un grupo
                for (int i = 0; i < dupRows.Count; i++)
                {
                    if (!dupRows[i].Header) continue;
                    var grp = dupRows.Skip(i + 1).TakeWhile(r => !r.Header).ToList();
                    if (grp.Count > 1 && grp.All(r => r.Checked)) { grp[0].Checked = false; grp[0].Invalidate(); }
                }
                var sel = dupRows.Where(r => r.Checked && r.Data is FileInfo).ToList();
                if (sel.Count == 0) return;
                long size = sel.Sum(r => ((FileInfo)r.Data).Length);
                if (MessageBox.Show("¿Mover " + sel.Count + " archivos (" + Catalog.Size(size) + ") a la papelera?\nPodrás recuperarlos desde la papelera de reciclaje.", AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                int ok = 0;
                Cursor = Cursors.WaitCursor;
                foreach (var r in sel)
                    if (Recycle.Send(((FileInfo)r.Data).FullName)) { ok++; page.Controls.Remove(r); dupRows.Remove(r); }
                Cursor = Cursors.Default;
                Log("🗑 " + ok + " archivos enviados a la papelera (" + Catalog.Size(size) + ").", Theme.Ok);
                dupStatus.Text = ok + " archivos enviados a la papelera. Vacíala (Limpieza › Vaciar la papelera) para liberar el espacio.";
                Sfx.Success();
            };
        }

        static readonly string[] SkipDirs = { "Windows", "Program Files", "Program Files (x86)", "ProgramData", "$Recycle.Bin", "System Volume Information", "AppData", "Recovery", "$WinREAgent" };

        IEnumerable<FileInfo> EnumFiles(string root)
        {
            var stack = new Stack<DirectoryInfo>(); stack.Push(new DirectoryInfo(root));
            while (stack.Count > 0 && !dupCancel)
            {
                var d = stack.Pop();
                FileSystemInfo[] items;
                try { items = d.GetFileSystemInfos(); } catch { continue; }
                foreach (var i in items)
                {
                    if ((i.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if ((i.Attributes & FileAttributes.Directory) != 0) { if (!SkipDirs.Contains(i.Name, StringComparer.OrdinalIgnoreCase)) stack.Push((DirectoryInfo)i); }
                    else yield return (FileInfo)i;
                }
            }
        }

        List<List<FileInfo>> FindDuplicates(List<string> roots, long minSize, IProgress<string> progress)
        {
            var bySize = new Dictionary<long, List<FileInfo>>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long n = 0;
            foreach (var root in roots)
                foreach (var f in EnumFiles(root))
                {
                    if (++n % 3000 == 0) progress.Report("Revisando… " + n.ToString("N0") + " archivos");
                    if (!seen.Add(f.FullName)) continue;
                    long len; try { len = f.Length; } catch { continue; }
                    if (len < minSize) continue;
                    List<FileInfo> l; if (!bySize.TryGetValue(len, out l)) bySize[len] = l = new List<FileInfo>();
                    l.Add(f);
                }
            var result = new List<List<FileInfo>>();
            var candidates = bySize.Values.Where(l => l.Count > 1).ToList();
            int done = 0;
            foreach (var group in candidates)
            {
                if (dupCancel) break;
                if (++done % 20 == 0) progress.Report("Comparando contenido… " + done + " de " + candidates.Count);
                foreach (var quick in group.GroupBy(f => Hash(f.FullName, true)).Where(g => g.Key != null && g.Count() > 1))
                    foreach (var full in quick.GroupBy(f => Hash(f.FullName, false)).Where(g => g.Key != null && g.Count() > 1))
                        result.Add(full.ToList());
            }
            return result;
        }

        static string Hash(string path, bool partial)
        {
            try
            {
                using (var md5 = MD5.Create())
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16))
                {
                    if (!partial) return BitConverter.ToString(md5.ComputeHash(fs));
                    var buf = new byte[1 << 16];
                    int a = fs.Read(buf, 0, buf.Length);
                    md5.TransformBlock(buf, 0, a, null, 0);
                    if (fs.Length > buf.Length * 2) { fs.Seek(-buf.Length, SeekOrigin.End); a = fs.Read(buf, 0, buf.Length); }
                    else a = 0;
                    md5.TransformFinalBlock(buf, 0, a);
                    return BitConverter.ToString(md5.Hash);
                }
            }
            catch { return null; }
        }

        // ---------------------------------------------------------------- Desinstalador
        class InstalledApp { public string Name, Publisher, Version, Uninstall, Location, Key; public long Size; public DateTime? Date; public RegistryHive Hive; public RegistryView View; }

        readonly List<CheckRow> appRows = new List<CheckRow>();
        Label appStatus;

        void BuildUninstall(FlowLayoutPanel page)
        {
            var top = new Panel { Height = Theme.S(60), Margin = new Padding(Theme.S(8)), BackColor = Theme.Card, Tag = "full" };
            var search = Theme.Input(Theme.S(280)); search.Location = new Point(Theme.S(14), Theme.S(17));
            var hint = Theme.Lbl("🔍 buscar", 9f, FontStyle.Regular, Theme.Muted); hint.Location = new Point(Theme.S(300), Theme.S(20));
            var go = Theme.Btn("Desinstalar seleccionados", Theme.Err, Theme.S(220)); go.Location = new Point(Theme.S(372), Theme.S(12));
            appStatus = Theme.Lbl("", 9f, FontStyle.Regular, Theme.Muted); appStatus.Location = new Point(Theme.S(606), Theme.S(21));
            top.Controls.AddRange(new Control[] { search, hint, go, appStatus });
            page.Controls.Add(top);

            search.TextChanged += (s, e) =>
            {
                string q = search.Text.Trim().ToLowerInvariant();
                page.SuspendLayout();
                foreach (var r in appRows) r.Visible = q == "" || r.Title.ToLowerInvariant().Contains(q) || (r.Sub ?? "").ToLowerInvariant().Contains(q);
                page.ResumeLayout();
            };
            onShow[Uninst] = () => { if (appRows.Count == 0) LoadApps(page); };
            go.Click += (s, e) =>
            {
                if (running) return;
                var sel = appRows.Where(r => r.Checked).Select(r => (InstalledApp)r.Data).ToList();
                if (sel.Count == 0) { appStatus.Text = "Marca los programas que quieras desinstalar."; return; }
                if (MessageBox.Show("Se desinstalarán " + sel.Count + " programas, uno detrás de otro:\n\n" + string.Join("\n", sel.Select(a => "  •  " + a.Name).ToArray()) +
                    "\n\nAlgunos abrirán su propio asistente: síguelo hasta el final.\nDespués sus restos se enviarán a la papelera.\n\n¿Continuar?", AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                foreach (var r in appRows) page.Controls.Remove(r);
                appRows.Clear();   // la lista se recarga al volver a esta página
                StartRun(sel.Select(UninstallTask).ToList(), false);
            };
        }

        void LoadApps(FlowLayoutPanel page)
        {
            foreach (var r in appRows) page.Controls.Remove(r);
            appRows.Clear();
            var apps = ListApps();
            page.SuspendLayout();
            foreach (var a in apps)
            {
                var row = new CheckRow(a.Name, (a.Publisher != "" ? a.Publisher : "Editor desconocido") + (a.Version != "" ? "  •  v" + a.Version : "") + (a.Date.HasValue ? "  •  instalado " + a.Date.Value.ToString("dd/MM/yyyy") : ""), a.Size > 0 ? Catalog.Size(a.Size) : "") { Data = a };
                row.Toggled += (s, e) => { int n = appRows.Count(r => r.Checked); appStatus.Text = n == 0 ? apps.Count + " programas instalados" : n + " seleccionados"; };
                appRows.Add(row); page.Controls.Add(row);
            }
            page.ResumeLayout();
            appStatus.Text = apps.Count + " programas instalados  •  " + Catalog.Size(apps.Sum(a => a.Size));
            FitPage(page);
        }

        static List<InstalledApp> ListApps()
        {
            var list = new List<InstalledApp>();
            var sources = new[] {
                new KeyValuePair<RegistryHive, RegistryView>(RegistryHive.LocalMachine, RegistryView.Registry64),
                new KeyValuePair<RegistryHive, RegistryView>(RegistryHive.LocalMachine, RegistryView.Registry32),
                new KeyValuePair<RegistryHive, RegistryView>(RegistryHive.CurrentUser, RegistryView.Registry64) };
            const string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
            foreach (var src in sources)
                try
                {
                    using (var root = RegistryKey.OpenBaseKey(src.Key, src.Value).OpenSubKey(path))
                    {
                        if (root == null) continue;
                        foreach (var sub in root.GetSubKeyNames())
                            using (var k = root.OpenSubKey(sub))
                            {
                                if (k == null) continue;
                                string name = Convert.ToString(k.GetValue("DisplayName", "")).Trim();
                                string un = Convert.ToString(k.GetValue("UninstallString", "")).Trim();
                                if (name == "" || un == "" || Convert.ToInt32(k.GetValue("SystemComponent", 0)) == 1 || k.GetValue("ParentKeyName") != null) continue;
                                if (name.StartsWith("Update for", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Security Update", StringComparison.OrdinalIgnoreCase)) continue;
                                DateTime d; string ds = Convert.ToString(k.GetValue("InstallDate", ""));
                                long size = 0; try { size = Convert.ToInt64(k.GetValue("EstimatedSize", 0)) * 1024; } catch { }
                                list.Add(new InstalledApp
                                {
                                    Name = name, Publisher = Convert.ToString(k.GetValue("Publisher", "")).Trim(), Version = Convert.ToString(k.GetValue("DisplayVersion", "")).Trim(),
                                    Uninstall = un, Location = Convert.ToString(k.GetValue("InstallLocation", "")).Trim().Trim('"'),
                                    Size = size, Key = path + "\\" + sub, Hive = src.Key, View = src.Value,
                                    Date = DateTime.TryParseExact(ds, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out d) ? (DateTime?)d : null
                                });
                            }
                    }
                }
                catch { }
            return list.GroupBy(a => a.Name).Select(g => g.First()).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        RepairTask UninstallTask(InstalledApp a)
        {
            return Catalog.T("uninst:" + a.Name, Uninst, "Desinstalar " + a.Name, "", "", false, Catalog.Int(log =>
            {
                string cmd = a.Uninstall;
                var msi = System.Text.RegularExpressions.Regex.Match(cmd, @"msiexec(\.exe)?\s+/[IX]\s*(\{[0-9A-Fa-f\-]+\})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (msi.Success) cmd = "MsiExec.exe /X" + msi.Groups[2].Value;
                log("Ejecutando el desinstalador de " + a.Name + "…");
                using (var p = Process.Start(new ProcessStartInfo("cmd.exe", "/s /c \"" + cmd + "\"") { UseShellExecute = false, CreateNoWindow = true }))
                    p.WaitForExit();
                System.Threading.Thread.Sleep(1500);
                bool gone;
                using (var k = RegistryKey.OpenBaseKey(a.Hive, a.View).OpenSubKey(a.Key)) gone = k == null;
                if (!gone) { log("El programa sigue instalado (¿se canceló el asistente o sigue abierto?)."); return 1; }
                log("Desinstalado. Buscando restos…");
                var leftovers = new List<string>();
                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                // solo se aceptan ubicaciones dentro de Program Files para evitar borrar carpetas compartidas
                if (a.Location.Length > 10 && Directory.Exists(a.Location) &&
                    (a.Location.StartsWith(pf + "\\", StringComparison.OrdinalIgnoreCase) || a.Location.StartsWith(pf86 + "\\", StringComparison.OrdinalIgnoreCase)))
                    leftovers.Add(a.Location);
                foreach (var baseDir in new[] { Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.CommonApplicationData })
                {
                    string b = Environment.GetFolderPath(baseDir);
                    try
                    {
                        foreach (var cand in new[] { Path.Combine(b, a.Name), a.Publisher != "" ? Path.Combine(b, a.Publisher, a.Name) : null })
                            if (cand != null && Directory.Exists(cand)) leftovers.Add(cand);
                    }
                    catch { }   // nombres con caracteres no válidos para una ruta
                }
                foreach (var l in leftovers.Distinct())
                    log((Recycle.Send(l) ? "Resto enviado a la papelera: " : "No se pudo quitar: ") + l);
                if (leftovers.Count == 0) log("Sin restos.");
                return 0;
            }));
        }
    }
}
