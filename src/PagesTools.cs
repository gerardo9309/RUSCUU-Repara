// RUSCUU Repara - Herramientas, Historial, Ajustes, actualizaciones y desinstalación
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Ruscuu
{
    // ================================================================ Historial de reparaciones
    static class History
    {
        public class Entry { public string File, Date, Client, Pc, Result, Score, Tasks; public bool Ok; }

        public static void Save(string client, List<KeyValuePair<RepairTask, int>> results, string log, int? before, int? after)
        {
            if (results.Count == 0) return;
            try
            {
                Directory.CreateDirectory(Settings.HistoryDir);
                string safeClient = string.IsNullOrWhiteSpace(client) ? "" : "_" + string.Join("_", client.Split(Path.GetInvalidFileNameChars()));
                string f = Path.Combine(Settings.HistoryDir, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_" + Environment.MachineName + safeClient + ".txt");
                var sb = new StringBuilder();
                sb.AppendLine("Fecha: " + DateTime.Now.ToString("dd/MM/yyyy HH:mm"));
                sb.AppendLine("Cliente: " + (string.IsNullOrWhiteSpace(client) ? "-" : client));
                sb.AppendLine("Equipo: " + Environment.MachineName + " (" + Environment.UserName + ")");
                sb.AppendLine("Resultado: " + results.Count(r => r.Value == 0) + " de " + results.Count + " correctas");
                sb.AppendLine("Puntuación: " + (before.HasValue ? before.Value.ToString() : "-") + " -> " + (after.HasValue ? after.Value.ToString() : "-"));
                sb.AppendLine("Tareas: " + string.Join(" | ", results.Select(r => (r.Value == 0 ? "OK " : "!! ") + r.Key.Title).ToArray()));
                sb.AppendLine("----------------------------------------------------------------");
                sb.Append(log);
                File.WriteAllText(f, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        public static List<Entry> List()
        {
            var list = new List<Entry>();
            try
            {
                if (!Directory.Exists(Settings.HistoryDir)) return list;
                foreach (var f in Directory.GetFiles(Settings.HistoryDir, "*.txt").OrderByDescending(x => x))
                {
                    var head = File.ReadLines(f, Encoding.UTF8).Take(6).ToList();
                    Func<string, string> v = k => { var l = head.FirstOrDefault(x => x.StartsWith(k + ": ")); return l == null ? "" : l.Substring(k.Length + 2); };
                    var e = new Entry { File = f, Date = v("Fecha"), Client = v("Cliente"), Pc = v("Equipo"), Result = v("Resultado"), Score = v("Puntuación"), Tasks = v("Tareas") };
                    var parts = e.Result.Split(' ');
                    e.Ok = parts.Length >= 3 && parts[0] == parts[2];
                    list.Add(e);
                }
            }
            catch { }
            return list;
        }
    }

    // ================================================================ Actualizaciones e instalación
    static class Updater
    {
        public class Info { public string Version, Url, Sha256, Notes; }

        // manifiesto de texto: version=…, url=…, sha256=…, notas=…
        public static Info Check(string manifestUrl)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "RUSCUU-Repara";
                wc.Encoding = Encoding.UTF8;
                string txt = wc.DownloadString(manifestUrl + (manifestUrl.Contains("?") ? "&" : "?") + "t=" + DateTime.Now.Ticks);
                var d = new Dictionary<string, string>();
                foreach (var l in txt.TrimStart('﻿').Split('\n').Select(x => x.Trim()).Where(x => x.Contains("=")))
                    d[l.Substring(0, l.IndexOf('=')).Trim().ToLowerInvariant()] = l.Substring(l.IndexOf('=') + 1).Trim();
                string tmp;
                return new Info { Version = d.TryGetValue("version", out tmp) ? tmp : "", Url = d.TryGetValue("url", out tmp) ? tmp : "", Sha256 = d.TryGetValue("sha256", out tmp) ? tmp : "", Notes = d.TryGetValue("notas", out tmp) ? tmp : "" };
            }
        }

        public static bool IsNewer(string v)
        {
            Version a, b;
            return Version.TryParse(v, out a) && Version.TryParse(AppInfo.Version, out b) && a > b;
        }

        public static void Apply(Info info)
        {
            if (!info.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) throw new Exception("la dirección de descarga debe ser https://");
            string tmp = Path.Combine(Path.GetTempPath(), "RUSCUU_Repara_" + info.Version + ".exe");
            using (var wc = new WebClient()) { wc.Headers[HttpRequestHeader.UserAgent] = "RUSCUU-Repara"; wc.DownloadFile(info.Url, tmp); }
            if (info.Sha256 != "")
            {
                string h;
                using (var sha = SHA256.Create()) using (var fs = File.OpenRead(tmp)) h = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
                if (!h.Equals(info.Sha256, StringComparison.OrdinalIgnoreCase)) { File.Delete(tmp); throw new Exception("la descarga está dañada (la huella SHA-256 no coincide)"); }
            }
            string exe = Application.ExecutablePath;
            string cmd = Path.Combine(Path.GetTempPath(), "ruscuu_update.cmd");
            File.WriteAllText(cmd, "@echo off\r\nping 127.0.0.1 -n 3 > nul\r\nmove /y \"" + tmp + "\" \"" + exe + "\" > nul\r\nstart \"\" \"" + exe + "\"\r\ndel \"%~f0\"\r\n", Encoding.Default);
            Process.Start(new ProcessStartInfo("cmd.exe", "/c \"" + cmd + "\"") { CreateNoWindow = true, UseShellExecute = false });
            Application.Exit();
        }

        const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\RUSCUU Repara";

        public static void SelfUninstall()
        {
            if (MessageBox.Show("¿Desinstalar RUSCUU Repara de este equipo?", AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            bool data = MessageBox.Show("¿Borrar también la configuración, el historial y los respaldos guardados en este equipo?", AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            string installed = null;
            try
            {
                using (var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(UninstallKey))
                    if (k != null) installed = Convert.ToString(k.GetValue("InstallLocation", ""));
            }
            catch { }
            try { using (var p = Process.Start(new ProcessStartInfo("schtasks.exe", "/Delete /F /TN \"RUSCUU Repara Mantenimiento\"") { CreateNoWindow = true, UseShellExecute = false })) p.WaitForExit(); } catch { }
            foreach (var lnk in new[] {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "RUSCUU Repara.lnk"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "RUSCUU Repara.lnk") })
                try { if (File.Exists(lnk)) File.Delete(lnk); } catch { }
            try { RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).DeleteSubKeyTree(UninstallKey, false); } catch { }
            string pd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RUSCUU Repara");
            var script = new StringBuilder("@echo off\r\nping 127.0.0.1 -n 3 > nul\r\n");
            // solo se borra la carpeta del programa si es la de instalación registrada
            if (!string.IsNullOrEmpty(installed) && string.Equals(installed.TrimEnd('\\'), dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) && dir.EndsWith("RUSCUU Repara", StringComparison.OrdinalIgnoreCase))
                script.Append("rmdir /s /q \"" + dir + "\"\r\n");
            if (data) script.Append("rmdir /s /q \"" + pd + "\"\r\n");
            else script.Append("del /q \"" + Path.Combine(pd, "RUSCUU Repara.exe") + "\" 2> nul\r\n");
            script.Append("del \"%~f0\"\r\n");
            string cmd = Path.Combine(Path.GetTempPath(), "ruscuu_uninstall.cmd");
            File.WriteAllText(cmd, script.ToString(), Encoding.Default);
            Process.Start(new ProcessStartInfo("cmd.exe", "/c \"" + cmd + "\"") { CreateNoWindow = true, UseShellExecute = false });
            MessageBox.Show("RUSCUU Repara se ha desinstalado. ¡Gracias por usarlo!", AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            Environment.Exit(0);
        }
    }

    partial class MainForm
    {
        const string SchedTask = "RUSCUU Repara Mantenimiento";
        ComboBox schedFreq, schedDay, schedHour, schedPreset;
        Label schedStatus;
        Updater.Info pendingUpdate;

        // ---------------------------------------------------------------- Herramientas
        void BuildTools(FlowLayoutPanel page)
        {
            var client = new Panel { Height = Theme.S(64), Margin = new Padding(Theme.S(8)), BackColor = Theme.Card, Tag = "full" };
            var cl = Theme.Lbl("👤  Nombre del cliente o del equipo (aparece en informes e historial):", 9.5f, FontStyle.Regular, Theme.Muted);
            cl.Location = new Point(Theme.S(16), Theme.S(22));
            var tb = Theme.Input(Theme.S(300));
            tb.TextChanged += (s, e) => ClientName = tb.Text.Trim();
            client.Controls.Add(cl); client.Controls.Add(tb);
            client.Resize += (s, e) => tb.Location = new Point(client.Width - tb.Width - Theme.S(16), (client.Height - tb.Height) / 2);
            page.Controls.Add(client);

            page.Controls.Add(GroupLabel("Clientes"));
            AddTile(page, "📄", "Informe del PC", "Informe HTML con tu logo: hardware, discos, seguridad, batería y última reparación. Para entregar al cliente.", Theme.Accent, () => RunIds("report"));
            AddTile(page, "📋", "Historial de reparaciones", "Consulta todo lo que has hecho en cada equipo y cliente.", Theme.Blue, () => ShowPage(HistoryPage));

            page.Controls.Add(GroupLabel("Antes y después de formatear"));
            AddTile(page, "💼", "Copia de datos del cliente", "Escritorio, Documentos, Imágenes, Vídeos, Música, Descargas, favoritos y Wi-Fi a una USB o disco externo.", Theme.Ok, () =>
            {
                using (var d = new BackupDialog())
                    if (d.ShowDialog(this) == DialogResult.OK) StartRun(new List<RepairTask> { ClientData.BackupTask(d.Dest, d.Items, d.Bookmarks, d.Wifi) }, false);
            });
            AddTile(page, "📥", "Restaurar datos del cliente", "Devuelve una copia «Respaldo_…» a su sitio sin sobrescribir nada, con favoritos y redes Wi-Fi.", Theme.Ok, () =>
            {
                using (var fb = new FolderBrowserDialog { Description = "Elige la carpeta Respaldo_… creada por RUSCUU Repara" })
                {
                    if (fb.ShowDialog(this) != DialogResult.OK) return;
                    if (!File.Exists(Path.Combine(fb.SelectedPath, "RUSCUU-respaldo.txt")))
                    { MessageBox.Show("Esa carpeta no es una copia de RUSCUU Repara (falta RUSCUU-respaldo.txt).", AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    StartRun(new List<RepairTask> { ClientData.RestoreTask(fb.SelectedPath) }, false);
                }
            });
            AddTile(page, "💾", "Respaldar drivers", "Guarda todos los drivers instalados en una carpeta o USB.", Theme.Brown, () =>
            {
                using (var fb = new FolderBrowserDialog { Description = "Elige dónde guardar la copia de drivers (por ejemplo tu USB)" })
                {
                    if (fb.ShowDialog(this) != DialogResult.OK) return;
                    StartRun(new List<RepairTask> { Catalog.DriverBackupTask(Path.Combine(fb.SelectedPath, "Drivers_" + Environment.MachineName + "_" + DateTime.Now.ToString("yyyy-MM-dd"))) }, false);
                }
            });
            AddTile(page, "♻", "Restaurar drivers", "Instala los drivers desde una copia hecha con «Respaldar drivers».", Theme.Brown, () =>
            {
                using (var fb = new FolderBrowserDialog { Description = "Elige la carpeta Drivers_… con la copia de drivers" })
                {
                    if (fb.ShowDialog(this) != DialogResult.OK) return;
                    if (Directory.GetFiles(fb.SelectedPath, "*.inf", SearchOption.AllDirectories).Length == 0)
                    { MessageBox.Show("Esa carpeta no contiene drivers (.inf).", AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    StartRun(new List<RepairTask> { Catalog.DriverRestoreTask(fb.SelectedPath) }, false);
                }
            });

            page.Controls.Add(GroupLabel("Utilidades"));
            AddTile(page, "📶", "Contraseñas Wi-Fi", "Muestra las redes guardadas en este equipo con sus contraseñas. Cópialas o guárdalas en un archivo.", Theme.Blue, ShowWifi);
            AddTile(page, "🔑", "Crear USB técnico", "Copia RUSCUU Repara a una USB con enlaces a herramientas, guía rápida e icono propio. Funciona en modo portátil.", Theme.Accent, () =>
            {
                using (var d = new UsbDialog())
                    if (d.ShowDialog(this) == DialogResult.OK) StartRun(new List<RepairTask> { Catalog.UsbTask(d.Drive, d.ToolsFolder, Application.ExecutablePath) }, false);
            });

            // ---- mantenimiento programado
            page.Controls.Add(GroupLabel("Mantenimiento automático"));
            var sched = new Panel { Height = Theme.S(140), Margin = new Padding(Theme.S(8)), BackColor = Theme.Card, Tag = "full" };
            var sd = Theme.Lbl("RUSCUU Repara se abrirá solo (minimizado), hará la reparación elegida y te avisará al terminar. Nunca reinicia el equipo.", 9f, FontStyle.Regular, Theme.Muted);
            sd.Location = new Point(Theme.S(16), Theme.S(12));
            int y = Theme.S(42), x = Theme.S(16);
            schedFreq = Theme.Combo(Theme.S(120), "Semanal", "Diario", "Mensual");
            schedDay = Theme.Combo(Theme.S(120), "Domingo", "Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado");
            schedHour = Theme.Combo(Theme.S(90), Enumerable.Range(0, 24).Select(h => h.ToString("00") + ":00").ToArray());
            schedHour.SelectedIndex = 12;
            schedPreset = Theme.Combo(Theme.S(210), "Reparación rápida", "Limpieza", "Reparación completa (sin reiniciar)");
            schedFreq.SelectedIndexChanged += (s, e) => schedDay.Enabled = schedFreq.SelectedIndex == 0;
            foreach (var pair in new[] { new KeyValuePair<string, Control>("Frecuencia", schedFreq), new KeyValuePair<string, Control>("Día", schedDay), new KeyValuePair<string, Control>("Hora", schedHour), new KeyValuePair<string, Control>("Qué hacer", schedPreset) })
            {
                var l = Theme.Lbl(pair.Key, 8.5f, FontStyle.Regular, Theme.Muted); l.Location = new Point(x, y);
                pair.Value.Location = new Point(x, y + Theme.S(20));
                sched.Controls.Add(l); sched.Controls.Add(pair.Value);
                x += pair.Value.Width + Theme.S(12);
            }
            var on = Theme.Btn("Activar", Theme.Accent, Theme.S(96)); on.Location = new Point(x + Theme.S(4), y + Theme.S(16));
            var off = Theme.Btn("Desactivar", Theme.Bg, Theme.S(110)); off.Location = new Point(x + Theme.S(108), y + Theme.S(16));
            on.Click += (s, e) => CreateSchedule();
            off.Click += (s, e) => DeleteSchedule();
            schedStatus = Theme.Lbl("", 9f, FontStyle.Bold, Theme.Muted); schedStatus.Location = new Point(Theme.S(16), y + Theme.S(64));
            sched.Controls.AddRange(new Control[] { sd, on, off, schedStatus });
            page.Controls.Add(sched);
            onShow[Tools] = RefreshSchedule;
        }

        async void ShowWifi()
        {
            Cursor = Cursors.WaitCursor;
            var nets = await Task.Run(() => ClientData.WifiPasswords());
            Cursor = Cursors.Default;
            if (nets.Count == 0) { MessageBox.Show("Este equipo no tiene redes Wi-Fi guardadas.", AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            using (var d = new WifiDialog(nets)) d.ShowDialog(this);
        }

        static string SchedExe { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"RUSCUU Repara\RUSCUU Repara.exe"); } }

        void CreateSchedule()
        {
            try
            {
                // copia fija del programa para que la tarea funcione aunque se mueva o borre el original (p. ej. desde una USB)
                string exe = SchedExe;
                Directory.CreateDirectory(Path.GetDirectoryName(exe));
                if (!string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase))
                    File.Copy(Application.ExecutablePath, exe, true);
                string preset = new[] { "auto-rapida", "auto-limpieza", "auto-completa" }[schedPreset.SelectedIndex];
                string days = new[] { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" }[schedDay.SelectedIndex];
                string when = schedFreq.SelectedIndex == 0 ? "/SC WEEKLY /D " + days : schedFreq.SelectedIndex == 1 ? "/SC DAILY" : "/SC MONTHLY /D 1";
                string args = "/Create /F /TN \"" + SchedTask + "\" /TR \"\\\"" + exe + "\\\" --auto " + preset + "\" " + when + " /ST " + schedHour.SelectedItem + " /RL HIGHEST /IT";
                string output;
                if (RunQuiet("schtasks.exe", args, out output) == 0) Log("✔ Mantenimiento automático activado: " + schedPreset.SelectedItem + ", " + schedFreq.SelectedItem.ToString().ToLower() + " a las " + schedHour.SelectedItem, Theme.Ok);
                else Log("No se pudo programar: " + output.Trim(), Theme.Err);
            }
            catch (Exception ex) { Log("No se pudo programar: " + ex.Message, Theme.Err); }
            RefreshSchedule();
        }

        void DeleteSchedule()
        {
            string output;
            if (RunQuiet("schtasks.exe", "/Delete /F /TN \"" + SchedTask + "\"", out output) == 0) Log("Mantenimiento automático desactivado.", Theme.Warn);
            RefreshSchedule();
        }

        async void RefreshSchedule()
        {
            string output = "";
            int code = await Task.Run(() => RunQuiet("powershell.exe", "-NoProfile -Command \"$i = Get-ScheduledTaskInfo -TaskName '" + SchedTask + "' -ErrorAction Stop; $i.NextRunTime.ToString('dddd dd/MM/yyyy HH:mm')\"", out output));
            if (code == 0 && output.Trim() != "") { schedStatus.Text = "● Activo — próxima ejecución: " + output.Trim(); schedStatus.ForeColor = Theme.Ok; }
            else { schedStatus.Text = "○ Desactivado"; schedStatus.ForeColor = Theme.Muted; }
        }

        static int RunQuiet(string file, string args, out string output)
        {
            try
            {
                var psi = new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                try { psi.StandardOutputEncoding = Encoding.GetEncoding(Native.GetOEMCP()); psi.StandardErrorEncoding = psi.StandardOutputEncoding; } catch { }
                using (var p = Process.Start(psi))
                {
                    var err = p.StandardError.ReadToEndAsync();
                    output = p.StandardOutput.ReadToEnd() + err.Result;
                    p.WaitForExit();
                    return p.ExitCode;
                }
            }
            catch (Exception ex) { output = ex.Message; return -1; }
        }

        // ---------------------------------------------------------------- Historial
        void BuildHistory(FlowLayoutPanel page)
        {
            var top = new Panel { Height = Theme.S(60), Margin = new Padding(Theme.S(8)), BackColor = Theme.Card, Tag = "full" };
            var search = Theme.Input(Theme.S(280)); search.Location = new Point(Theme.S(14), Theme.S(17));
            var hint = Theme.Lbl("🔍 cliente, equipo o tarea", 9f, FontStyle.Regular, Theme.Muted); hint.Location = new Point(Theme.S(300), Theme.S(20));
            var open = Theme.Btn("Abrir carpeta", Theme.Bg, Theme.S(140)); open.Location = new Point(Theme.S(500), Theme.S(12));
            var info = Theme.Lbl("", 9f, FontStyle.Regular, Theme.Muted); info.Location = new Point(Theme.S(656), Theme.S(21));
            top.Controls.AddRange(new Control[] { search, hint, open, info });
            page.Controls.Add(top);
            open.Click += (s, e) => { Directory.CreateDirectory(Settings.HistoryDir); Process.Start("explorer.exe", "\"" + Settings.HistoryDir + "\""); };
            var rows = new List<CheckRow>();
            search.TextChanged += (s, e) =>
            {
                string q = search.Text.Trim().ToLowerInvariant();
                page.SuspendLayout();
                foreach (var r in rows) r.Visible = q == "" || (r.Title + " " + r.Sub).ToLowerInvariant().Contains(q);
                page.ResumeLayout();
            };
            onShow[HistoryPage] = () =>
            {
                page.SuspendLayout();
                foreach (var r in rows) page.Controls.Remove(r);
                rows.Clear();
                var list = History.List();
                foreach (var h in list)
                {
                    var row = new CheckRow((h.Client != "-" && h.Client != "" ? h.Client + "  •  " : "") + h.Pc + "  •  " + h.Date,
                        h.Tasks.Replace("OK ", "✔ ").Replace("!! ", "⚠ "), h.Result + (h.Score != "- -> -" && h.Score != "" ? "   •   salud " + h.Score.Replace("->", "→") : ""))
                    { Checkable = false, Dot = h.Ok ? Theme.Ok : Theme.Warn, Data = h };
                    string file = h.File;
                    row.Toggled += (s2, e2) => { try { Process.Start("notepad.exe", "\"" + file + "\""); } catch { } };
                    rows.Add(row); page.Controls.Add(row);
                }
                info.Text = list.Count == 0 ? "Aún no hay reparaciones registradas." : list.Count + " reparaciones  •  " + (Settings.Portable ? "guardadas en la USB" : Settings.HistoryDir);
                page.ResumeLayout();
                FitPage(page);
            };
        }

        // ---------------------------------------------------------------- Ajustes
        void BuildSettings(FlowLayoutPanel page)
        {
            // ---- apariencia
            page.Controls.Add(GroupLabel("Apariencia"));
            var look = new Panel { Height = Theme.S(150), Margin = new Padding(Theme.S(8)), BackColor = Theme.Card, Tag = "full" };
            var lt = Theme.Lbl("Tema", 9f, FontStyle.Bold, Theme.Muted); lt.Location = new Point(Theme.S(16), Theme.S(14));
            var theme = Theme.Combo(Theme.S(210), "Oscuro", "Claro", "Automático (como Windows)");
            theme.SelectedIndex = Settings.ThemeMode == "claro" ? 1 : Settings.ThemeMode == "auto" ? 2 : 0;
            theme.Location = new Point(Theme.S(16), Theme.S(40));
            var la = Theme.Lbl("Color de acento", 9f, FontStyle.Bold, Theme.Muted); la.Location = new Point(Theme.S(250), Theme.S(14));
            look.Controls.AddRange(new Control[] { lt, theme, la });
            int accent = Settings.Accent;
            var swatches = new List<Button>();
            var tips = new ToolTip();
            for (int i = 0; i < Theme.Accents.Length; i++)
            {
                int idx = i;
                var b = new Button { Width = Theme.S(40), Height = Theme.S(40), FlatStyle = FlatStyle.Flat, BackColor = Theme.Accents[i], Location = new Point(Theme.S(250) + i * Theme.S(48), Theme.S(34)), Cursor = Cursors.Hand, Text = i == accent ? "✔" : "", ForeColor = Color.White, Font = Theme.F(12f, FontStyle.Bold) };
                b.FlatAppearance.BorderSize = 0;
                tips.SetToolTip(b, Theme.AccentNames[i]);
                b.Click += (s, e) => { accent = idx; foreach (var w in swatches) w.Text = ""; b.Text = "✔"; };
                swatches.Add(b); look.Controls.Add(b);
            }
            var snd = new CheckBox { Text = "Sonidos al terminar", Checked = Settings.Sounds, AutoSize = true, ForeColor = Theme.Text, Location = new Point(Theme.S(16), Theme.S(100)) };
            var test = Theme.Btn("Probar efecto 🎉", Theme.Bg, Theme.S(160)); test.Location = new Point(Theme.S(250), Theme.S(94));
            var apply = Theme.Btn("Aplicar y reiniciar", Theme.Accent, Theme.S(180)); apply.Location = new Point(Theme.S(420), Theme.S(94));
            test.Click += (s, e) => { Sfx.Success(); Confetti.Celebrate(this, "¡Así se verá!", "Salud del PC: 62 → 95", true); };
            apply.Click += (s, e) =>
            {
                Settings.Set("tema", new[] { "oscuro", "claro", "auto" }[theme.SelectedIndex]);
                Settings.Set("acento", accent.ToString());
                Settings.SetBool("sonidos", snd.Checked);
                if (running) { MessageBox.Show("Se aplicará cuando termine la reparación en curso y vuelvas a abrir el programa.", AppInfo.Name); return; }
                Application.Restart();
            };
            look.Controls.AddRange(new Control[] { snd, test, apply });
            page.Controls.Add(look);

            // ---- modo cliente
            page.Controls.Add(GroupLabel("Modo cliente / modo técnico"));
            var mode = new Panel { Height = Theme.S(120), Margin = new Padding(Theme.S(8)), BackColor = Theme.Card, Tag = "full" };
            var md = new Label { AutoSize = false, Location = new Point(Theme.S(16), Theme.S(12)), Size = new Size(Theme.S(820), Theme.S(42)), ForeColor = Theme.Muted,
                Text = "En modo cliente el programa solo muestra Inicio (salud y reparaciones de un clic), Arreglos rápidos y el Monitor. Ideal para dejarlo instalado en el PC de un cliente. Para volver a ver todo, «Modo técnico» pide tu PIN." };
            var modeBtn = Theme.Btn(Settings.ClientMode ? "Desactivar modo cliente" : "Activar modo cliente", Theme.Accent, Theme.S(220)); modeBtn.Location = new Point(Theme.S(16), Theme.S(64));
            var pinBtn = Theme.Btn(Settings.HasPin ? "Cambiar PIN" : "Crear PIN", Theme.Bg, Theme.S(150)); pinBtn.Location = new Point(Theme.S(250), Theme.S(64));
            var modeSt = Theme.Lbl(Settings.ClientMode ? "● Modo cliente activo" : "○ Modo técnico (todas las funciones)", 9f, FontStyle.Bold, Settings.ClientMode ? Theme.Accent : Theme.Muted); modeSt.Location = new Point(Theme.S(420), Theme.S(73));
            Func<bool> ensurePin = () =>
            {
                if (Settings.HasPin) return true;
                using (var d = new PinDialog(true))
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return false;
                    Settings.SetPin(d.Pin); pinBtn.Text = "Cambiar PIN";
                    Log("🔐 PIN de técnico guardado.", Theme.Ok);
                    return true;
                }
            };
            pinBtn.Click += (s, e) =>
            {
                if (Settings.HasPin)
                    using (var d = new PinDialog(false))
                    {
                        if (d.ShowDialog(this) != DialogResult.OK) return;
                        if (!Settings.CheckPin(d.Pin)) { MessageBox.Show("PIN incorrecto.", AppInfo.Name); return; }
                    }
                using (var d = new PinDialog(true))
                    if (d.ShowDialog(this) == DialogResult.OK) { Settings.SetPin(d.Pin); pinBtn.Text = "Cambiar PIN"; Log("🔐 PIN de técnico actualizado.", Theme.Ok); }
            };
            modeBtn.Click += (s, e) =>
            {
                if (!Settings.ClientMode)
                {
                    if (!ensurePin()) return;
                    Settings.SetBool("modo_cliente", true); techUnlocked = false;
                    Log("Modo cliente activado.", Theme.Accent);
                    BuildNav(); ShowPage(Home);
                }
                else { Settings.SetBool("modo_cliente", false); Log("Modo cliente desactivado.", Theme.Accent); BuildNav(); }
                modeBtn.Text = Settings.ClientMode ? "Desactivar modo cliente" : "Activar modo cliente";
                modeSt.Text = Settings.ClientMode ? "● Modo cliente activo" : "○ Modo técnico (todas las funciones)";
                modeSt.ForeColor = Settings.ClientMode ? Theme.Accent : Theme.Muted;
            };
            mode.Controls.AddRange(new Control[] { md, modeBtn, pinBtn, modeSt });
            page.Controls.Add(mode);

            // ---- actualizaciones
            page.Controls.Add(GroupLabel("Actualizaciones"));
            var upd = new Panel { Height = Theme.S(130), Margin = new Padding(Theme.S(8)), BackColor = Theme.Card, Tag = "full" };
            var ul = Theme.Lbl("Dirección del archivo version.txt (por ejemplo, publicado en GitHub):", 9f, FontStyle.Regular, Theme.Muted); ul.Location = new Point(Theme.S(16), Theme.S(12));
            var url = Theme.Input(Theme.S(560)); url.Text = Settings.UpdateUrl; url.Location = new Point(Theme.S(16), Theme.S(36));
            var save = Theme.Btn("Guardar", Theme.Bg, Theme.S(100)); save.Location = new Point(Theme.S(590), Theme.S(31));
            var check = Theme.Btn("Buscar actualizaciones", Theme.Accent, Theme.S(210)); check.Location = new Point(Theme.S(16), Theme.S(76));
            var ust = Theme.Lbl("Versión instalada: " + AppInfo.Version + (Settings.UpdateUrl == "" ? "  •  sin dirección configurada" : ""), 9f, FontStyle.Regular, Theme.Muted); ust.Location = new Point(Theme.S(240), Theme.S(85));
            save.Click += (s, e) => { Settings.Set("update_url", url.Text.Trim()); ust.Text = "Dirección guardada."; };
            check.Click += (s, e) => { Settings.Set("update_url", url.Text.Trim()); CheckUpdatesAsync(true, ust); };
            upd.Controls.AddRange(new Control[] { ul, url, save, check, ust });
            page.Controls.Add(upd);

            // ---- acerca de
            page.Controls.Add(GroupLabel("Acerca de"));
            page.Controls.Add(FullLabel(AppInfo.Name + " " + AppInfo.Version + "  •  Hecho para RUSCUU  •  Datos en: " + Settings.DataDir + (Settings.Portable ? "  (modo portátil)" : ""), Theme.Muted, FontStyle.Regular));
            var uninstall = Theme.Btn("Desinstalar RUSCUU Repara…", Theme.Bg, Theme.S(240));
            uninstall.Margin = new Padding(Theme.S(8)); uninstall.Tag = "fixed";
            uninstall.Click += (s, e) => { if (!running) Updater.SelfUninstall(); };
            page.Controls.Add(uninstall);
        }

        async void CheckUpdatesAsync(bool manual, Label status = null)
        {
            string url = Settings.UpdateUrl;
            if (url == "") { if (status != null) status.Text = "Escribe primero la dirección del archivo version.txt."; return; }
            if (status != null) status.Text = "Buscando…";
            try
            {
                var info = await Task.Run(() => Updater.Check(url));
                if (Updater.IsNewer(info.Version))
                {
                    pendingUpdate = info;
                    updateBanner.Text = "⬆ Nueva versión " + info.Version + " — clic para actualizar";
                    updateBanner.Visible = true;
                    updateBanner.Click -= UpdateBannerClick; updateBanner.Click += UpdateBannerClick;
                    if (status != null) status.Text = "¡Hay una versión nueva: " + info.Version + "! " + info.Notes;
                    if (manual) UpdateBannerClick(null, EventArgs.Empty);
                }
                else if (status != null) status.Text = "Ya tienes la última versión (" + AppInfo.Version + ").";
            }
            catch (Exception ex) { if (status != null) status.Text = "No se pudo comprobar: " + ex.Message; }
        }

        void UpdateBannerClick(object sender, EventArgs e)
        {
            if (pendingUpdate == null || running) return;
            if (MessageBox.Show("¿Actualizar RUSCUU Repara a la versión " + pendingUpdate.Version + "?\n\n" + pendingUpdate.Notes + "\n\nEl programa se cerrará y se abrirá de nuevo.", AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { Cursor = Cursors.WaitCursor; Updater.Apply(pendingUpdate); }
            catch (Exception ex) { Cursor = Cursors.Default; MessageBox.Show("No se pudo actualizar: " + ex.Message, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }
}
