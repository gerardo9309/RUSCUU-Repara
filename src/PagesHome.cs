// RUSCUU Repara - página de Inicio: salud del equipo con puntuación y reparaciones de un clic
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Ruscuu
{
    partial class MainForm
    {
        Gauge gauge;
        Label healthTitle, healthSub;
        FlowLayoutPanel findingsPanel;
        Button fixAllBtn, rescanBtn;
        bool healthBusy;
        // ancho de fila que deja sitio a la barra vertical y evita la horizontal
        int RowWidth { get { return findingsPanel.Width - SystemInformation.VerticalScrollBarWidth - Theme.S(6); } }

        void BuildHome(FlowLayoutPanel home)
        {
            // ---- panel de salud
            var hp = new Panel { Height = Theme.S(290), Margin = new Padding(Theme.S(8)), BackColor = Theme.Card, Tag = "full" };
            gauge = new Gauge { Location = new Point(Theme.S(16), Theme.S(30)), Size = new Size(Theme.S(200), Theme.S(230)) };
            healthTitle = Theme.Lbl("Salud del equipo", 14f, FontStyle.Bold, Theme.Text); healthTitle.Location = new Point(Theme.S(236), Theme.S(18));
            healthSub = Theme.Lbl("Analizando tu PC…", 9.5f, FontStyle.Regular, Theme.Muted); healthSub.Location = new Point(Theme.S(238), Theme.S(50));
            findingsPanel = Theme.DarkScroll(new FlowLayoutPanel { Location = new Point(Theme.S(230), Theme.S(78)), Height = Theme.S(164), FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Card });
            fixAllBtn = Theme.Btn("✨  Arreglar lo recomendado", Theme.Accent, Theme.S(240));
            rescanBtn = Theme.Btn("Analizar de nuevo", Theme.Bg, Theme.S(160));
            fixAllBtn.Location = new Point(Theme.S(236), Theme.S(242)); rescanBtn.Location = new Point(Theme.S(486), Theme.S(242));
            fixAllBtn.Enabled = false;
            fixAllBtn.Click += (s, e) =>
            {
                if (lastHealth == null || running) return;
                var ids = lastHealth.Findings.Where(f => f.Fix != null).SelectMany(f => f.Fix).Distinct().ToList();
                if (!ids.Contains("restore")) ids.Insert(0, "restore");
                StartRun(tasks.Where(t => ids.Contains(t.Id)).ToList(), true);
            };
            rescanBtn.Click += (s, e) => RunHealth(null);
            hp.Controls.AddRange(new Control[] { gauge, healthTitle, healthSub, findingsPanel, fixAllBtn, rescanBtn });
            hp.Resize += (s, e) =>
            {
                findingsPanel.Width = hp.Width - findingsPanel.Left - Theme.S(16);
                foreach (Control c in findingsPanel.Controls) c.Width = RowWidth;
            };
            home.Controls.Add(hp);

            var info = new Label { AutoSize = false, Height = Theme.S(84), Margin = new Padding(Theme.S(8)), BackColor = Theme.Card, ForeColor = Theme.Text, Font = Theme.F(9.5f, FontStyle.Regular), Padding = new Padding(Theme.S(16), Theme.S(10), Theme.S(10), Theme.S(10)), Text = SystemSummary(), Tag = "full" };
            home.Controls.Add(info);

            AddPreset(home, "⚡", "Reparación rápida", "Punto de restauración + DISM + SFC + temporales + DNS. Lo recomendado ante fallos comunes (20-35 min).", Theme.Accent, "rapida");
            AddPreset(home, "🛡", "Reparación completa", "Todo lo anterior + disco, componentes, Windows Update, red y análisis antivirus. Para un PC muy lento o inestable (40-80 min).", Theme.Brown, "completa");
            AddPreset(home, "🧹", "Liberar espacio", "Temporales, caché de Windows Update, papelera y componentes antiguos de Windows.", Theme.Ok, "limpieza");
            AddPreset(home, "🌐", "No tengo Internet", "DNS, IP, proxy, Winsock y TCP/IP. Arregla la mayoría de problemas de conexión.", Theme.Blue, "internet");
            home.Controls.Add(FullLabel("Consejo: cierra tus programas antes de empezar. Cada reparación se guarda en el Historial y en Documentos\\RUSCUU Repara.", Theme.Muted, FontStyle.Italic));
        }

        void AddPreset(FlowLayoutPanel host, string icon, string title, string desc, Color color, string preset)
        {
            AddTile(host, icon, title, desc, color, () =>
            {
                var ids = Catalog.Presets[preset];
                var sel = tasks.Where(t => ids.Contains(t.Id)).ToList();
                var msg = new StringBuilder(title + " ejecutará:\n\n");
                foreach (var t in sel) msg.AppendLine("  •  " + t.Title + "   (" + t.Time + ")");
                if (sel.Any(t => t.Reboot)) msg.AppendLine("\nAlgunas tareas necesitan reiniciar el equipo al final.");
                msg.AppendLine("\n¿Empezar ahora?");
                if (MessageBox.Show(msg.ToString(), AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                foreach (var t in tasks) t.Selected = ids.Contains(t.Id);
                RefreshCards();
                StartRun(sel, false);
            });
        }

        async void RunHealth(Action<HealthResult> done)
        {
            if (healthBusy) return;
            healthBusy = true;
            gauge.SetBusy(true);
            healthSub.Text = "Analizando tu PC… (unos segundos)";
            rescanBtn.Enabled = false; fixAllBtn.Enabled = false;
            int bloat = bloatCount;
            HealthResult r;
            try { r = await Task.Run(() => Health.Analyze(bloat)); }
            catch (Exception ex) { r = new HealthResult(); Log("No se pudo completar el análisis: " + ex.Message, Theme.Warn); }
            healthBusy = false;
            lastHealth = r;
            gauge.SetValue(r.Score, r.Label);
            int problems = r.Findings.Count(f => f.Severity == 2), warns = r.Findings.Count(f => f.Severity == 1);
            healthSub.Text = r.Findings.Count == 0 ? "¡Todo en orden! No se encontraron problemas." :
                (problems > 0 ? problems + " problema(s)" : "") + (problems > 0 && warns > 0 ? " y " : "") + (warns > 0 ? warns + " aviso(s)" : "") + " — analizado a las " + DateTime.Now.ToString("HH:mm");
            healthSub.ForeColor = problems > 0 ? Theme.Err : warns > 0 ? Theme.Warn : Theme.Ok;
            rescanBtn.Enabled = true;
            fixAllBtn.Enabled = r.Findings.Any(f => f.Fix != null);

            findingsPanel.SuspendLayout();
            findingsPanel.Controls.Clear();
            if (r.Findings.Count == 0)
                findingsPanel.Controls.Add(new CheckRow("Tu equipo está en excelente estado ✨", "Vuelve a analizarlo de vez en cuando o activa el mantenimiento automático en Herramientas.", "") { Checkable = false, Dot = Theme.Ok, Height = Theme.S(50), Margin = new Padding(0, 0, 0, Theme.S(4)), Width = RowWidth });
            foreach (var f in r.Findings)
            {
                bool reboot = f.Fix == null && f.Page == null;
                string action = f.Fix != null ? "Arreglar ›" : reboot ? "Reiniciar ›" : Restricted ? "" : "Ver ›";
                var row = new CheckRow(f.Title, f.Detail, action) { Checkable = false, Dot = f.Severity == 2 ? Theme.Err : Theme.Warn, Height = Theme.S(50), Margin = new Padding(0, 0, 0, Theme.S(4)), Width = RowWidth };
                var finding = f;
                row.Toggled += (s, e) =>
                {
                    if (running) return;
                    if (finding.Fix != null) StartRun(tasks.Where(t => finding.Fix.Contains(t.Id)).ToList(), true);
                    else if (reboot)
                    {
                        if (MessageBox.Show("¿Reiniciar el equipo ahora? Guarda tu trabajo antes.", AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                            Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 5") { CreateNoWindow = true, UseShellExecute = false });
                    }
                    else if (!Restricted && pages.ContainsKey(finding.Page)) ShowPage(finding.Page);
                };
                findingsPanel.Controls.Add(row);
            }
            findingsPanel.ResumeLayout();
            if (done != null) done(r);
        }
    }
}
