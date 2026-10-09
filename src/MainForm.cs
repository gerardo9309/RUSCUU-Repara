// RUSCUU Repara - ventana principal (núcleo: menú, páginas, ejecución de tareas y registro)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle("RUSCUU Repara")]
[assembly: AssemblyProduct("RUSCUU Repara")]
[assembly: AssemblyDescription("Reparación y mantenimiento de Windows")]
[assembly: AssemblyCompany("RUSCUU")]
[assembly: AssemblyCopyright("RUSCUU")]
[assembly: AssemblyVersion(Ruscuu.AppInfo.Version + ".0")]
[assembly: AssemblyFileVersion(Ruscuu.AppInfo.Version + ".0")]

namespace Ruscuu
{
    partial class MainForm : Form
    {
        public static volatile string ClientName = "";
        public string AutoPreset;   // modo mantenimiento automático (--auto <preset>)

        const string Home = "Inicio", Boot = "Arranque", Tools = "Herramientas", Space = "Espacio en disco", Dups = "Duplicados y grandes",
            Uninst = "Desinstalar programas", Monitor = "Monitor en vivo", Bench = "Test de rendimiento", NetTest = "Test de red", HistoryPage = "Historial", SettingsPage = "Ajustes";

        readonly List<RepairTask> tasks = Catalog.Build();
        readonly List<TaskCard> cards = new List<TaskCard>();
        readonly Dictionary<string, FlowLayoutPanel> pages = new Dictionary<string, FlowLayoutPanel>();
        readonly Dictionary<string, Action> onShow = new Dictionary<string, Action>(), onHide = new Dictionary<string, Action>();
        FlowLayoutPanel nav;
        Panel content; Label pageTitle, pageSub, selLabel, statusLabel, updateBanner;
        Button runBtn, cancelBtn, clearBtn, logBtn;
        RichTextBox logBox; FlatBar bar; NotifyIcon tray;
        volatile bool cancel; Process current; bool running, techUnlocked;
        string currentPage;
        int bloatCount;
        HealthResult lastHealth;
        readonly StringBuilder logText = new StringBuilder();
        static readonly Regex Pct = new Regex(@"(\d{1,3}(?:[.,]\d+)?)\s?%");
        readonly string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RUSCUU Repara");

        // { icono, nombre, subtítulo, grupo, visible en modo cliente }
        static readonly string[][] Nav = {
            new[] { "🏠", Home, "Analiza tu PC, mira su puntuación de salud o usa una reparación de un clic.", "", "1" },
            new[] { "🛠", Catalog.Sys, "Repara archivos dañados de Windows y revisa el disco. Marca las tareas y pulsa «Empezar».", "REPARAR", "0" },
            new[] { "🔄", Catalog.WU, "Arregla actualizaciones atascadas y la Microsoft Store.", "REPARAR", "0" },
            new[] { "🌐", Catalog.Net, "Soluciona problemas de conexión.", "REPARAR", "0" },
            new[] { "🔧", Catalog.Fix, "Soluciones de un clic para los problemas más comunes.", "REPARAR", "1" },
            new[] { "🧹", Catalog.Clean, "Libera espacio y elimina archivos basura y cachés.", "LIMPIAR", "0" },
            new[] { "💽", Space, "Mapa visual de qué ocupa espacio en tus discos. Haz clic en un bloque para entrar.", "LIMPIAR", "0" },
            new[] { "🗂", Dups, "Encuentra archivos repetidos y archivos enormes olvidados. Todo va a la papelera (recuperable).", "LIMPIAR", "0" },
            new[] { "🗑", Uninst, "Desinstala varios programas a la vez y limpia los restos que dejan.", "LIMPIAR", "0" },
            new[] { "📦", Catalog.Apps, "Apps preinstaladas que casi nadie usa. Marca las que quieras quitar y pulsa «Empezar».", "LIMPIAR", "0" },
            new[] { "⚡", Catalog.Perf, "Modo Gamer y ajustes para que Windows y los juegos vayan más rápido.", "OPTIMIZAR", "0" },
            new[] { "🚀", Boot, "Programas que se abren solos al encender el PC. Desactiva los que no necesites (no se desinstalan).", "OPTIMIZAR", "0" },
            new[] { "📈", Monitor, "Uso de procesador, memoria, disco y red en tiempo real, temperaturas y programas que más consumen.", "OPTIMIZAR", "1" },
            new[] { "🏁", Bench, "Mide la velocidad del procesador, la memoria y el disco. Compáralo antes y después de reparar.", "OPTIMIZAR", "0" },
            new[] { "📶", NetTest, "Mide tu Internet (ping, bajada, subida) y encuentra el DNS más rápido.", "OPTIMIZAR", "0" },
            new[] { "🛡", Catalog.Sec, "Antivirus, revisión de seguridad y navegador secuestrado.", "PROTEGER", "0" },
            new[] { "🕶", Catalog.Priv, "Menos seguimiento y menos anuncios de Windows. Todo se puede deshacer.", "PROTEGER", "0" },
            new[] { "⬇", Catalog.Inst, "Instala programas de golpe en un PC recién formateado y mantén todo actualizado (winget).", "TÉCNICO", "0" },
            new[] { "🩺", Catalog.Diag, "Pantallazos azules, licencias, salud del hardware e informes.", "TÉCNICO", "0" },
            new[] { "🧰", Tools, "Copias de datos y drivers, USB técnico, Wi-Fi, informe para clientes y mantenimiento automático.", "TÉCNICO", "0" },
            new[] { "📋", HistoryPage, "Todas las reparaciones realizadas, por cliente y equipo.", "TÉCNICO", "0" },
            new[] { "⚙", SettingsPage, "Tema, colores, sonidos, modo cliente y actualizaciones.", "", "0" },
        };

        static readonly string[] TaskPages = { Catalog.Sys, Catalog.WU, Catalog.Net, Catalog.Fix, Catalog.Clean, Catalog.Apps, Catalog.Perf, Catalog.Sec, Catalog.Priv, Catalog.Inst, Catalog.Diag };

        bool Restricted { get { return Settings.ClientMode && !techUnlocked; } }

        public MainForm()
        {
            using (var g = CreateGraphics()) Theme.Scale = g.DpiX / 96f;
            Text = AppInfo.Name;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.F(9.5f, FontStyle.Regular);
            AutoScaleMode = AutoScaleMode.None;
            var wa = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(Theme.S(1240), wa.Width), Math.Min(Theme.S(860), wa.Height));
            MinimumSize = new Size(Math.Min(Theme.S(1040), wa.Width), Math.Min(Theme.S(660), wa.Height));
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;

            BuildLayout();
            ShowPage(Home);
            Log(AppInfo.Name + " " + AppInfo.Version + " listo." + (Settings.Portable ? "  (modo portátil: los datos se guardan en la USB)" : ""), Theme.Muted);
            FormClosing += (s, e) =>
            {
                if (running && AutoPreset == null &&
                    MessageBox.Show("Hay una reparación en curso. ¿Seguro que quieres salir?", AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                { e.Cancel = true; return; }
                if (running) { cancel = true; KillCurrent(); }
                foreach (var h in onHide.Values) h();
                if (tray != null) tray.Dispose();
            };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int on = Theme.Dark ? 1 : 0; Native.DwmSetWindowAttribute(Handle, 20, ref on, 4);
                int caption = Theme.Side.R | (Theme.Side.G << 8) | (Theme.Side.B << 16); Native.DwmSetWindowAttribute(Handle, 35, ref caption, 4);
                int border = Theme.Accent.R | (Theme.Accent.G << 8) | (Theme.Accent.B << 16); Native.DwmSetWindowAttribute(Handle, 34, ref border, 4);
            }
            catch { }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (AutoPreset != null && Catalog.Presets.ContainsKey(AutoPreset))
            {
                WindowState = FormWindowState.Minimized;
                var ids = Catalog.Presets[AutoPreset];
                StartRun(tasks.Where(t => ids.Contains(t.Id)).ToList(), false);
                return;
            }
            LoadBloatAsync();
            CheckUpdatesAsync(false);
        }

        public void OpenPage(string name) { if (pages.ContainsKey(name)) ShowPage(name); }

        // ---------------------------------------------------------------- Construcción de la interfaz
        void BuildLayout()
        {
            var side = new Panel { Dock = DockStyle.Left, Width = Theme.S(236), BackColor = Theme.Side };
            nav = Theme.DarkScroll(new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Side, Padding = new Padding(0, 0, 0, Theme.S(8)) });
            var logo = new PictureBox { Dock = DockStyle.Top, Height = Theme.S(112), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Side, Padding = new Padding(Theme.S(60), Theme.S(12), Theme.S(60), 0) };
            try { logo.Image = Image.FromStream(Assembly.GetExecutingAssembly().GetManifestResourceStream("logo.png")); } catch { }
            var brand = new Label { Dock = DockStyle.Top, Height = Theme.S(34), Text = AppInfo.Name, TextAlign = ContentAlignment.MiddleCenter, Font = Theme.F(13f, FontStyle.Bold), ForeColor = Theme.Text };
            var foot = new Label { Dock = DockStyle.Bottom, Height = Theme.S(26), Text = "v" + AppInfo.Version + "  •  " + WinName(), TextAlign = ContentAlignment.MiddleCenter, Font = Theme.F(8f, FontStyle.Regular), ForeColor = Theme.Muted };
            side.Controls.Add(nav); side.Controls.Add(foot); side.Controls.Add(brand); side.Controls.Add(logo);
            BuildNav();

            var main = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            var header = new Panel { Dock = DockStyle.Top, Height = Theme.S(80), Padding = new Padding(Theme.S(28), Theme.S(14), Theme.S(20), 0) };
            updateBanner = new Label { Dock = DockStyle.Right, AutoSize = false, Width = Theme.S(300), TextAlign = ContentAlignment.TopRight, ForeColor = Theme.Accent, Font = Theme.F(9.5f, FontStyle.Bold), Cursor = Cursors.Hand, Visible = false };
            pageTitle = new Label { Dock = DockStyle.Top, Height = Theme.S(38), Font = Theme.F(18f, FontStyle.Bold), ForeColor = Theme.Text };
            pageSub = new Label { Dock = DockStyle.Top, Height = Theme.S(24), Font = Theme.F(9.5f, FontStyle.Regular), ForeColor = Theme.Muted, AutoEllipsis = true };
            header.Controls.Add(pageSub); header.Controls.Add(pageTitle); header.Controls.Add(updateBanner);
            content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Theme.S(20), 0, Theme.S(12), 0) };

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(220), BackColor = Theme.Side, Padding = new Padding(Theme.S(20), Theme.S(12), Theme.S(20), Theme.S(14)) };
            var actions = new Panel { Dock = DockStyle.Top, Height = Theme.S(40) };
            selLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = Theme.F(10.5f, FontStyle.Bold), ForeColor = Theme.Text };
            runBtn = Theme.Btn("▶  Empezar", Theme.Accent, Theme.S(160));
            cancelBtn = Theme.Btn("■  Cancelar", Theme.Track, Theme.S(120));
            clearBtn = Theme.Btn("Quitar selección", Theme.Card, Theme.S(150));
            logBtn = Theme.Btn("Ver registros", Theme.Card, Theme.S(130));
            cancelBtn.Enabled = false;
            runBtn.Click += (s, e) => StartRun(tasks.Where(t => t.Selected).ToList(), true);
            cancelBtn.Click += (s, e) => { cancel = true; KillCurrent(); Log("Cancelando…", Theme.Warn); };
            clearBtn.Click += (s, e) => { foreach (var t in tasks) t.Selected = false; RefreshCards(); };
            logBtn.Click += (s, e) => { Directory.CreateDirectory(logDir); Process.Start("explorer.exe", "\"" + logDir + "\""); };
            foreach (var b in new[] { runBtn, cancelBtn, clearBtn, logBtn })
            {
                b.Dock = DockStyle.Right;
                actions.Controls.Add(b);
                actions.Controls.Add(new Panel { Dock = DockStyle.Right, Width = Theme.S(8) });
            }
            actions.Controls.Add(selLabel); selLabel.BringToFront();

            statusLabel = new Label { Dock = DockStyle.Top, Height = Theme.S(28), TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Muted, Font = Theme.F(9f, FontStyle.Regular), Text = "Esperando…" };
            bar = new FlatBar { Dock = DockStyle.Top, Height = Theme.S(6) };
            var spacer = new Panel { Dock = DockStyle.Top, Height = Theme.S(8) };
            logBox = Theme.DarkScroll(new RichTextBox { Dock = DockStyle.Fill, BackColor = Theme.LogBg, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, ReadOnly = true, Font = new Font("Consolas", 9.5f), DetectUrls = false });
            bottom.Controls.Add(logBox); bottom.Controls.Add(spacer); bottom.Controls.Add(bar); bottom.Controls.Add(statusLabel); bottom.Controls.Add(actions);

            main.Controls.Add(content); main.Controls.Add(bottom); main.Controls.Add(header);
            Controls.Add(main); Controls.Add(side);

            BuildPages();
            UpdateSelection();
        }

        void BuildNav()
        {
            nav.SuspendLayout();
            nav.Controls.Clear();
            string group = null;
            foreach (var n in Nav)
            {
                if (Restricted && n[4] != "1") continue;
                if (!Restricted && n[3] != group)
                {
                    group = n[3];
                    nav.Controls.Add(new NavHeader(group) { Width = Theme.S(214), Margin = new Padding(0), Height = group == "" ? Theme.S(8) : Theme.S(28) });
                }
                string name = n[1];
                var nb = new NavButton(n[0], name) { Width = Theme.S(214), Margin = new Padding(0) };
                nb.Click += (s, e) => ShowPage(name);
                nav.Controls.Add(nb);
            }
            if (Restricted)
            {
                var lockBtn = new NavButton("🔒", "Modo técnico") { Width = Theme.S(214), Margin = new Padding(0, Theme.S(16), 0, 0) };
                lockBtn.Click += (s, e) => UnlockTech();
                nav.Controls.Add(lockBtn);
            }
            nav.ResumeLayout();
            if (currentPage != null) foreach (var c in nav.Controls.OfType<NavButton>()) c.Active = c.Text == currentPage;
        }

        FlowLayoutPanel NewPage(string name)
        {
            var flow = Theme.DarkScroll(new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg, Visible = false });
            flow.Resize += (s, e) => FitPage(flow);
            pages[name] = flow;
            content.Controls.Add(flow);
            return flow;
        }

        Label FullLabel(string text, Color color, FontStyle st, int h = 40)
        {
            return new Label { AutoSize = false, Height = Theme.S(h), Margin = new Padding(Theme.S(8)), ForeColor = color, Font = Theme.F(9.5f, st), Text = text, Tag = "full" };
        }

        Label GroupLabel(string text)
        {
            return new Label { AutoSize = false, Height = Theme.S(30), Margin = new Padding(Theme.S(8), Theme.S(14), Theme.S(8), 0), ForeColor = Theme.Accent, Font = Theme.F(11f, FontStyle.Bold), Text = text, Tag = "full" };
        }

        void BuildPages()
        {
            BuildHome(NewPage(Home));
            foreach (var cat in TaskPages)
            {
                var flow = NewPage(cat);
                if (cat == Catalog.Perf)
                {
                    AddTile(flow, "🎮", "Activar Modo Gamer", "Máximo rendimiento, modo juego, cierra programas en segundo plano y libera RAM. Un clic.", Theme.Accent, () => RunIds("gameron"));
                    AddTile(flow, "🛑", "Salir del Modo Gamer", "Deja todo como estaba antes de jugar (plan de energía y programas).", Theme.Brown, () => RunIds("gameroff"));
                }
                if (cat == Catalog.Inst)
                    AddTile(flow, "⭐", "Esenciales para PC nuevo", "Marca Chrome, 7-Zip, VLC, Acrobat Reader, librerías de Visual C++ y .NET, y AnyDesk. Revisa y pulsa «Empezar».", Theme.Ok, () =>
                    {
                        foreach (var t in tasks.Where(t => Catalog.Essentials.Contains(t.Id))) t.Selected = true;
                        RefreshCards();
                    });
                string group = null;
                foreach (var t in tasks.Where(x => x.Category == cat))
                {
                    if (t.Group != null && t.Group != group) { group = t.Group; flow.Controls.Add(GroupLabel(group)); }
                    AddCard(flow, t);
                }
                if (cat == Catalog.Apps) flow.Controls.Add(FullLabel("Buscando aplicaciones instaladas…", Theme.Muted, FontStyle.Italic));
            }
            NewPage(Boot); onShow[Boot] = LoadStartup;
            BuildSpace(NewPage(Space));
            BuildDuplicates(NewPage(Dups));
            BuildUninstall(NewPage(Uninst));
            BuildMonitor(NewPage(Monitor));
            BuildBench(NewPage(Bench));
            BuildNetTest(NewPage(NetTest));
            BuildTools(NewPage(Tools));
            BuildHistory(NewPage(HistoryPage));
            BuildSettings(NewPage(SettingsPage));
        }

        void AddCard(FlowLayoutPanel flow, RepairTask t)
        {
            var c = new TaskCard(t);
            c.Toggled += (s, e) => UpdateSelection();
            c.Enabled = !running;
            cards.Add(c); flow.Controls.Add(c);
        }

        PresetTile AddTile(FlowLayoutPanel page, string icon, string title, string desc, Color color, Action action)
        {
            var tile = new PresetTile(icon, title, desc, color);
            tile.Click += (s, e) => { if (!running) action(); };
            page.Controls.Add(tile);
            return tile;
        }

        void RunIds(params string[] ids) { StartRun(tasks.Where(t => ids.Contains(t.Id)).ToList(), false); }

        void FitPage(FlowLayoutPanel flow)
        {
            int w = flow.ClientSize.Width - Theme.S(36);
            if (w < Theme.S(300)) return;
            int cols = w > Theme.S(1150) ? 3 : 2;
            int card = (w - Theme.S(16) * (cols - 1)) / cols;
            int half = (w - Theme.S(16)) / 2;
            int third = (w - Theme.S(32)) / 3;
            int quarter = (w - Theme.S(48)) / 4;
            flow.SuspendLayout();
            foreach (Control c in flow.Controls)
            {
                string tag = c.Tag as string;
                if (tag == "fixed") continue;
                if (tag == "full" || c is StartupRow || c is CheckRow) c.Width = w;
                else if (tag == "third") c.Width = third;
                else if (tag == "quarter") c.Width = quarter;
                else if (c is TaskCard) c.Width = card;
                else c.Width = half;
            }
            flow.ResumeLayout();
        }

        void ShowPage(string name)
        {
            if (currentPage != null && currentPage != name && onHide.ContainsKey(currentPage)) onHide[currentPage]();
            currentPage = name;
            foreach (var kv in pages) kv.Value.Visible = kv.Key == name;
            foreach (var n in nav.Controls.OfType<NavButton>()) { n.Active = n.Text == name; n.Invalidate(); }
            var info = Nav.First(n => n[1] == name);
            pageTitle.Text = name == Home ? "¿Qué quieres reparar hoy?" : name;
            pageSub.Text = info[2];
            if (onShow.ContainsKey(name)) onShow[name]();
            FitPage(pages[name]);
        }

        void RefreshCards() { foreach (var c in cards) c.Invalidate(); UpdateSelection(); }

        void UpdateSelection()
        {
            int n = tasks.Count(t => t.Selected);
            selLabel.Text = running ? "Trabajando… no apagues el equipo" : n == 0 ? "Ninguna tarea seleccionada" : n + (n == 1 ? " tarea seleccionada" : " tareas seleccionadas");
            runBtn.Enabled = n > 0 && !running;
            runBtn.BackColor = runBtn.Enabled ? Theme.Accent : Theme.Track;
        }

        void UnlockTech()
        {
            if (Settings.HasPin)
                using (var d = new PinDialog(false))
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    if (!Settings.CheckPin(d.Pin)) { Sfx.Warning(); MessageBox.Show("PIN incorrecto.", AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                }
            techUnlocked = true;
            BuildNav();
            Log("🔓 Modo técnico desbloqueado en esta sesión.", Theme.Ok);
        }

        // ---------------------------------------------------------------- Apps basura
        async void LoadBloatAsync()
        {
            var names = await Task.Run(() =>
            {
                var r = new List<string>();
                try
                {
                    var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"$p = try { Get-AppxPackage -AllUsers -ErrorAction Stop } catch { Get-AppxPackage }; $p | ForEach-Object { $_.Name }\"")
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                    using (var p = Process.Start(psi))
                    {
                        string line;
                        while ((line = p.StandardOutput.ReadLine()) != null) if (line.Trim() != "") r.Add(line.Trim());
                        p.WaitForExit();
                    }
                }
                catch { }
                return r;
            });
            var page = pages[Catalog.Apps];
            foreach (var old in page.Controls.OfType<Label>().ToList()) page.Controls.Remove(old);
            var found = Catalog.BloatTasks(names);
            bloatCount = found.Count;
            if (found.Count == 0)
                page.Controls.Add(FullLabel("No se encontraron apps basura conocidas. ¡Este equipo está limpio!", Theme.Ok, FontStyle.Bold));
            else
            {
                var lbl = FullLabel(found.Count + " apps preinstaladas encontradas. Las marcadas con ⚠ pueden ser útiles: revísalas antes de quitarlas.", Theme.Muted, FontStyle.Regular);
                page.Controls.Add(lbl); page.Controls.SetChildIndex(lbl, 0);
                foreach (var t in found) { tasks.Add(t); AddCard(page, t); }
            }
            FitPage(page);
            RunHealth(null);
        }

        // ---------------------------------------------------------------- Arranque
        void LoadStartup()
        {
            var page = pages[Boot];
            page.SuspendLayout();
            page.Controls.Clear();
            var items = StartupManager.List();
            int on = items.Count(i => i.Enabled);
            page.Controls.Add(FullLabel(items.Count == 0 ? "No hay programas configurados para iniciarse." :
                on + " de " + items.Count + " programas se abren al encender el PC. Toca un programa para activarlo o desactivarlo.", Theme.Muted, FontStyle.Regular));
            foreach (var it in items)
            {
                var row = new StartupRow(it) { Enabled = !running };
                row.Toggled += (s, e) =>
                {
                    var r = (StartupRow)s;
                    try
                    {
                        StartupManager.SetEnabled(r.Item, !r.Item.Enabled);
                        Log((r.Item.Enabled ? "✔ Activado en el inicio: " : "✖ Desactivado del inicio: ") + r.Item.Name, r.Item.Enabled ? Theme.Ok : Theme.Warn);
                    }
                    catch (Exception ex) { Log("Error al cambiar " + r.Item.Name + ": " + ex.Message, Theme.Err); }
                };
                page.Controls.Add(row);
            }
            page.ResumeLayout();
            FitPage(page);
        }

        // ---------------------------------------------------------------- Ejecución
        static readonly string[] HealthCats = { Catalog.Sys, Catalog.Clean, Catalog.Net, Catalog.WU, Catalog.Sec, Catalog.Priv, Catalog.Perf, Catalog.Fix, Catalog.Apps };

        async void StartRun(List<RepairTask> sel, bool confirm)
        {
            if (running || sel.Count == 0) return;
            bool auto = AutoPreset != null;
            if (confirm)
            {
                var msg = "Se van a ejecutar " + sel.Count + " tareas:\n\n" + string.Join("\n", sel.Take(25).Select(t => "  •  " + t.Title).ToArray()) + (sel.Count > 25 ? "\n  … y " + (sel.Count - 25) + " más" : "");
                if (sel.Any(t => t.RunLast)) msg += "\n\n⚠ El análisis sin conexión reiniciará el equipo al final, sin preguntar.";
                if (sel.Any(t => t.Category == Catalog.Apps)) msg += "\n\nLas apps se quitarán para todos los usuarios (se pueden reinstalar desde Microsoft Store).";
                if (MessageBox.Show(msg + "\n\n¿Continuar?", AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            }
            sel = sel.OrderBy(t => t.RunLast ? 1 : 0).ThenBy(t => { int i = tasks.IndexOf(t); return i < 0 ? int.MaxValue : i; }).ToList();
            int? scoreBefore = lastHealth != null ? (int?)lastHealth.Score : null;
            running = true; cancel = false; SetBusy(true);
            logBox.Clear(); logText.Clear();
            Log("═══ " + AppInfo.Name + " — " + DateTime.Now.ToString("dd/MM/yyyy HH:mm") + (auto ? " (mantenimiento automático)" : "") + (ClientName != "" ? "  •  Cliente: " + ClientName : "") + " ═══", Theme.Accent);
            var results = new List<KeyValuePair<RepairTask, int>>();
            var sw = Stopwatch.StartNew();

            for (int i = 0; i < sel.Count && !cancel; i++)
            {
                var t = sel[i];
                int idx = i, total = sel.Count;
                bar.Value = (double)i / total;
                statusLabel.Text = string.Format("[{0}/{1}] {2}…", i + 1, total, t.Title);
                if (auto) Text = string.Format("{0} — {1}/{2} {3}", AppInfo.Name, i + 1, total, t.Title);
                Log("", Theme.Text);
                Log("» " + t.Title, Theme.Accent);
                int code = await Task.Run(() => RunTask(t, pct => BeginInvoke((Action)(() =>
                {
                    bar.Value = (idx + pct / 100.0) / total;
                    statusLabel.Text = string.Format("[{0}/{1}] {2}…  {3:0}%", idx + 1, total, t.Title, pct);
                }))));
                results.Add(new KeyValuePair<RepairTask, int>(t, code));
                if (cancel) Log("✖ Cancelado", Theme.Warn);
                else if (code == 0) Log("✔ Completado", Theme.Ok);
                else Log("⚠ Terminó con código " + code + " (revisa los mensajes de arriba)", Theme.Warn);
            }

            if (!cancel) bar.Value = 1;
            sw.Stop();
            int okCount = results.Count(r => r.Value == 0);
            Log("", Theme.Text);
            Log(string.Format("═══ Fin: {0} de {1} tareas correctas en {2} ═══", okCount, sel.Count, sw.Elapsed.ToString(@"hh\:mm\:ss")), Theme.Accent);
            string saved = SaveLog();
            if (saved != null) Log("Registro guardado en: " + saved, Theme.Muted);
            statusLabel.Text = cancel ? "Cancelado." : "Terminado.";
            Text = AppInfo.Name;
            running = false; SetBusy(false);

            foreach (var r in results.Where(r => r.Key.Category == Catalog.Apps && r.Value == 0))
            {
                tasks.Remove(r.Key);
                var card = cards.FirstOrDefault(c => c.Task == r.Key);
                if (card != null) { cards.Remove(card); card.Parent.Controls.Remove(card); card.Dispose(); bloatCount--; }
            }
            foreach (var t in sel) t.Selected = false;
            RefreshCards();

            if (cancel) { History.Save(ClientName, results, logText.ToString(), scoreBefore, null); return; }
            if (auto) { History.Save(ClientName, results, logText.ToString(), null, null); AutoFinished(okCount, sel.Count); return; }

            bool allOk = okCount == sel.Count;
            if (allOk) Sfx.Success(); else Sfx.Warning();
            if (sel.Any(t => HealthCats.Contains(t.Category)))
            {
                statusLabel.Text = "Comprobando la salud del equipo…";
                RunHealth(h =>
                {
                    string sub = scoreBefore.HasValue ? "Salud del PC: " + scoreBefore.Value + " → " + h.Score : "Salud del PC: " + h.Score + "/100";
                    if (scoreBefore.HasValue) Log("Salud del equipo: " + scoreBefore.Value + " → " + h.Score + (h.Score > scoreBefore.Value ? "  (+" + (h.Score - scoreBefore.Value) + ") ✨" : ""), Theme.Ok);
                    History.Save(ClientName, results, logText.ToString(), scoreBefore, h.Score);
                    statusLabel.Text = "Terminado.";
                    Finish(results, allOk, sub);
                });
            }
            else { History.Save(ClientName, results, logText.ToString(), null, null); Finish(results, allOk, null); }
        }

        void Finish(List<KeyValuePair<RepairTask, int>> results, bool allOk, string sub)
        {
            Confetti.Celebrate(this, allOk ? "¡Listo!" : "Terminado con avisos", sub, allOk);
            bool reboot = results.Any(r => r.Key.Reboot && r.Value == 0);
            if (!reboot && results.Count < 2 && allOk) return;
            var t = new Timer { Interval = 1300 };
            t.Tick += (s, e) =>
            {
                t.Stop(); t.Dispose();
                var summary = new StringBuilder(allOk ? "Reparación terminada.\n\n" : "Terminado, pero algunas tareas necesitan revisión.\n\n");
                foreach (var r in results.Take(30)) summary.AppendLine((r.Value == 0 ? "✔  " : "⚠  ") + r.Key.Title);
                if (sub != null) summary.AppendLine("\n" + sub);
                if (reboot)
                {
                    summary.AppendLine("\nAlgunos cambios necesitan reiniciar el equipo. ¿Reiniciar ahora?");
                    if (MessageBox.Show(summary.ToString(), AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                        Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 10 /c \"RUSCUU Repara reiniciara el equipo para terminar la reparacion.\"") { CreateNoWindow = true, UseShellExecute = false });
                }
                else MessageBox.Show(summary.ToString(), AppInfo.Name, MessageBoxButtons.OK, allOk ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            };
            t.Start();
        }

        void AutoFinished(int ok, int total)
        {
            tray = new NotifyIcon { Icon = Icon, Text = AppInfo.Name, Visible = true };
            tray.BalloonTipClicked += (s, e) => { WindowState = FormWindowState.Normal; Activate(); };
            tray.Click += (s, e) => { WindowState = FormWindowState.Normal; Activate(); };
            tray.ShowBalloonTip(10000, AppInfo.Name + " — mantenimiento terminado", ok + " de " + total + " tareas correctas. Toca aquí para ver el detalle.", ok == total ? ToolTipIcon.Info : ToolTipIcon.Warning);
            var timer = new Timer { Interval = 120000 };
            timer.Tick += (s, e) => { timer.Stop(); if (WindowState == FormWindowState.Minimized) Close(); };
            timer.Start();
        }

        int RunTask(RepairTask t, Action<double> progress)
        {
            int last = 0;
            Action<string> log = s => LogAsync("  " + s, Theme.Text);
            foreach (var st in t.Steps)
            {
                if (cancel) return -1;
                try
                {
                    if (st.Internal != null) { last = st.Internal(log); if (last != 0) break; continue; }
                    if (st.InternalP != null) { last = st.InternalP(log, progress); if (last != 0) break; continue; }
                    LogAsync("> " + Path.GetFileNameWithoutExtension(st.File) + " " + (st.File == "powershell.exe" ? "(script)" : st.Args), Theme.Muted);
                    var psi = new ProcessStartInfo(st.File, st.Args)
                    {
                        UseShellExecute = false, CreateNoWindow = true,
                        RedirectStandardOutput = true, RedirectStandardError = true,
                        StandardOutputEncoding = st.Enc, StandardErrorEncoding = st.Enc,
                        WorkingDirectory = Environment.SystemDirectory
                    };
                    using (var p = new Process { StartInfo = psi })
                    {
                        string lastLine = null;
                        object gate = new object();
                        DataReceivedEventHandler h = (s, e) =>
                        {
                            if (e.Data == null) return;
                            string line = e.Data.Replace("\0", "").Trim();
                            // barras y animaciones de progreso de winget/DISM
                            if (line.Length <= 2 || line.IndexOf('█') >= 0 || line.IndexOf('▒') >= 0) return;
                            lock (gate)
                            {
                                if (line == lastLine) return;
                                lastLine = line;
                            }
                            var m = Pct.Match(line);
                            if (m.Success && line.Length < 140)
                            {
                                double v;
                                if (double.TryParse(m.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v))
                                    progress(Math.Min(100, v));
                                return;
                            }
                            LogAsync("  " + line, line.StartsWith("[!!]") ? Theme.Warn : line.StartsWith("[OK]") ? Theme.Ok : Theme.Text);
                        };
                        p.OutputDataReceived += h; p.ErrorDataReceived += h;
                        p.Start(); current = p;
                        p.BeginOutputReadLine(); p.BeginErrorReadLine();
                        p.WaitForExit();
                        current = null;
                        last = p.ExitCode;
                        if (st.OkCodes != null && st.OkCodes.Contains(last)) last = 0;
                    }
                }
                catch (Exception ex) { LogAsync("  Error: " + ex.Message, Theme.Err); last = 1; break; }
            }
            return last;
        }

        void KillCurrent() { try { var p = current; if (p != null && !p.HasExited) p.Kill(); } catch { } }

        void SetBusy(bool b)
        {
            cancelBtn.Enabled = b; clearBtn.Enabled = !b;
            cancelBtn.BackColor = b ? Color.FromArgb(190, 50, 75) : Theme.Track;
            foreach (var c in cards) c.Enabled = !b;
            foreach (var p in pages.Values) foreach (Control c in p.Controls) if (c is PresetTile || c is StartupRow) c.Enabled = !b;
            UpdateSelection();
        }

        void LogAsync(string s, Color c) { try { BeginInvoke((Action)(() => Log(s, c))); } catch { } }

        void Log(string s, Color c)
        {
            logText.AppendLine(s);
            logBox.SelectionStart = logBox.TextLength;
            logBox.SelectionLength = 0;
            logBox.SelectionColor = c;
            logBox.AppendText(s + "\n");
            logBox.ScrollToCaret();
        }

        string SaveLog()
        {
            try
            {
                Directory.CreateDirectory(logDir);
                string f = Path.Combine(logDir, "Registro_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".txt");
                File.WriteAllText(f, logText.ToString(), Encoding.UTF8);
                return f;
            }
            catch { return null; }
        }

        // ---------------------------------------------------------------- Info del sistema
        static string WinName() { string b, v; return Report.OsName(out b, out v); }

        static string SystemSummary()
        {
            var sb = new StringBuilder();
            string build, ver;
            string name = Report.OsName(out build, out ver);
            sb.AppendLine("Equipo:  " + Environment.MachineName + "   •   " + name + " " + ver + " (compilación " + build + ")");
            try
            {
                using (var cpu = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                    sb.AppendLine("Procesador:  " + ((string)cpu.GetValue("ProcessorNameString", "")).Trim() + "   •   " + Environment.ProcessorCount + " hilos");
            }
            catch { }
            try
            {
                var m = new Native.MEMSTATUS(); Native.GlobalMemoryStatusEx(m);
                var up = TimeSpan.FromMilliseconds(Native.GetTickCount64());
                string sysDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
                var d = new DriveInfo(sysDrive);
                sb.Append(string.Format("RAM:  {0:0.0} GB ({1}% en uso)   •   Disco {2}  {3} libres de {4}   •   Encendido hace {5}d {6}h {7}m",
                    m.ullTotalPhys / 1073741824.0, m.dwMemoryLoad, sysDrive, Catalog.Size(d.AvailableFreeSpace), Catalog.Size(d.TotalSize), up.Days, up.Hours, up.Minutes));
            }
            catch { }
            return sb.ToString();
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.AddMessageFilter(new WheelFilter());
            Theme.Apply(Settings.Dark, Settings.Accent);
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "--uninstall") > 0) { Updater.SelfUninstall(); return; }
            int a = Array.IndexOf(args, "--auto");
            if (a < 0 && Array.IndexOf(args, "--nosplash") < 0)
                using (var splash = new Splash()) Application.Run(splash);
            var form = new MainForm();
            if (a > 0 && a + 1 < args.Length) form.AutoPreset = args[a + 1];
            int p = Array.IndexOf(args, "--page");
            if (p > 0 && p + 1 < args.Length) form.Shown += (s, e) => form.OpenPage(args[p + 1]);
            Application.Run(form);
        }
    }
}
