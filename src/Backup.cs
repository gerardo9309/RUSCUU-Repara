// RUSCUU Repara - copia y restauración de datos del cliente, y contraseñas Wi-Fi
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Xml;

namespace Ruscuu
{
    static class ClientData
    {
        [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

        static string Downloads
        {
            get
            {
                IntPtr p;
                if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out p) == 0)
                { string s = Marshal.PtrToStringUni(p); Marshal.FreeCoTaskMem(p); return s; }
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            }
        }

        // { nombre en la copia, carpeta del usuario }
        public static List<string[]> Folders()
        {
            return new List<string[]> {
                new[] { "Escritorio", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) },
                new[] { "Documentos", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) },
                new[] { "Imágenes", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) },
                new[] { "Vídeos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos) },
                new[] { "Música", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic) },
                new[] { "Descargas", Downloads },
            };
        }

        static readonly string[][] Bookmarks = {
            new[] { "Chrome", @"Google\Chrome\User Data\Default\Bookmarks", "chrome" },
            new[] { "Edge", @"Microsoft\Edge\User Data\Default\Bookmarks", "msedge" },
            new[] { "Brave", @"BraveSoftware\Brave-Browser\User Data\Default\Bookmarks", "brave" },
        };
        static string Local { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }

        // ---------------------------------------------------------------- copia
        public static RepairTask BackupTask(string dest, List<string> items, bool bookmarks, bool wifi)
        {
            string root = Path.Combine(dest, "Respaldo_" + Environment.MachineName + "_" + Environment.UserName + "_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm"));
            return Catalog.T("backup", Catalog.Diag, "Copia de datos del cliente", "", "", false, Catalog.IntP((log, progress) =>
            {
                Directory.CreateDirectory(root);
                var folders = Folders().Where(f => items.Contains(f[0]) && Directory.Exists(f[1])).ToList();
                // evitar copiar la carpeta destino dentro de sí misma
                folders = folders.Where(f => !root.StartsWith(f[1] + "\\", StringComparison.OrdinalIgnoreCase)).ToList();
                log("Calculando tamaño…");
                var files = new List<KeyValuePair<string, string>>();   // origen -> destino
                long total = 0;
                foreach (var f in folders)
                    foreach (var file in SafeFiles(f[1]))
                    {
                        try { total += new FileInfo(file).Length; } catch { continue; }
                        files.Add(new KeyValuePair<string, string>(file, Path.Combine(root, f[0], file.Substring(f[1].Length).TrimStart('\\'))));
                    }
                var drive = new DriveInfo(Path.GetPathRoot(root));
                log(files.Count + " archivos, " + Catalog.Size(total) + "  •  libre en destino: " + Catalog.Size(drive.AvailableFreeSpace));
                if (total > drive.AvailableFreeSpace) { log("No hay espacio suficiente en el destino."); return 1; }

                long done = 0; int ok = 0, fail = 0;
                foreach (var kv in files)
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(kv.Value));
                        File.Copy(kv.Key, kv.Value, true);
                        done += new FileInfo(kv.Key).Length; ok++;
                    }
                    catch { fail++; }
                    if (ok % 25 == 0) progress(total > 0 ? done * 100.0 / total : 100);
                }
                log(ok + " archivos copiados" + (fail > 0 ? ", " + fail + " omitidos (en uso o sin permiso)" : "") + ".");

                if (bookmarks)
                    foreach (var b in Bookmarks)
                    {
                        string src = Path.Combine(Local, b[1]);
                        if (!File.Exists(src)) continue;
                        Directory.CreateDirectory(Path.Combine(root, "Favoritos"));
                        File.Copy(src, Path.Combine(root, "Favoritos", b[0] + ".Bookmarks"), true);
                        log("Favoritos de " + b[0] + " guardados.");
                    }

                if (wifi)
                {
                    string wdir = Path.Combine(root, "WiFi");
                    Directory.CreateDirectory(wdir);
                    Run("netsh.exe", "wlan export profile key=clear folder=\"" + wdir + "\"");
                    log(Directory.GetFiles(wdir, "*.xml").Length + " redes Wi-Fi guardadas (con contraseña).");
                }

                File.WriteAllText(Path.Combine(root, "RUSCUU-respaldo.txt"),
                    "Respaldo creado por RUSCUU Repara\r\nEquipo: " + Environment.MachineName + "\r\nUsuario: " + Environment.UserName +
                    "\r\nFecha: " + DateTime.Now.ToString("dd/MM/yyyy HH:mm") + "\r\nCarpetas: " + string.Join(", ", folders.Select(f => f[0]).ToArray()) + "\r\n", Encoding.UTF8);
                log("Copia terminada en: " + root);
                Process.Start("explorer.exe", "\"" + root + "\"");
                return 0;
            }));
        }

        // ---------------------------------------------------------------- restauración
        public static RepairTask RestoreTask(string root)
        {
            return Catalog.T("restoredata", Catalog.Diag, "Restaurar datos del cliente", "", "", false, Catalog.IntP((log, progress) =>
            {
                var map = Folders().ToDictionary(f => f[0], f => f[1]);
                var files = new List<KeyValuePair<string, string>>();
                foreach (var dir in Directory.GetDirectories(root))
                {
                    string name = Path.GetFileName(dir);
                    if (!map.ContainsKey(name)) continue;
                    foreach (var file in SafeFiles(dir))
                        files.Add(new KeyValuePair<string, string>(file, Path.Combine(map[name], file.Substring(dir.Length).TrimStart('\\'))));
                }
                int copied = 0, same = 0, renamed = 0, fail = 0;
                for (int i = 0; i < files.Count; i++)
                {
                    var kv = files[i];
                    try
                    {
                        var src = new FileInfo(kv.Key);
                        string target = kv.Value;
                        if (File.Exists(target))
                        {
                            if (new FileInfo(target).Length == src.Length) { same++; continue; }
                            target = Path.Combine(Path.GetDirectoryName(target), Path.GetFileNameWithoutExtension(target) + " (respaldo)" + Path.GetExtension(target));
                            renamed++;
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        File.Copy(src.FullName, target, true); copied++;
                    }
                    catch { fail++; }
                    if (i % 25 == 0) progress(i * 100.0 / Math.Max(1, files.Count));
                }
                log(copied + " archivos restaurados, " + same + " ya existían" + (renamed > 0 ? ", " + renamed + " guardados como «(respaldo)» para no sobrescribir" : "") + (fail > 0 ? ", " + fail + " con error" : "") + ".");

                string fav = Path.Combine(root, "Favoritos");
                if (Directory.Exists(fav))
                    foreach (var b in Bookmarks)
                    {
                        string src = Path.Combine(fav, b[0] + ".Bookmarks"), dst = Path.Combine(Local, b[1]);
                        if (!File.Exists(src)) continue;
                        if (!Directory.Exists(Path.GetDirectoryName(dst))) { log(b[0] + " no está instalado: favoritos no restaurados."); continue; }
                        if (Process.GetProcessesByName(b[2]).Length > 0) { log("Cierra " + b[0] + " para restaurar sus favoritos."); continue; }
                        if (File.Exists(dst)) File.Copy(dst, dst + ".ruscuu.bak", true);
                        File.Copy(src, dst, true);
                        log("Favoritos de " + b[0] + " restaurados.");
                    }

                string wifi = Path.Combine(root, "WiFi");
                if (Directory.Exists(wifi))
                {
                    int n = 0;
                    foreach (var x in Directory.GetFiles(wifi, "*.xml")) { Run("netsh.exe", "wlan add profile filename=\"" + x + "\" user=all"); n++; }
                    log(n + " redes Wi-Fi restauradas.");
                }
                return fail > 0 && copied == 0 ? 1 : 0;
            }));
        }

        static IEnumerable<string> SafeFiles(string dir)
        {
            var stack = new Stack<string>(); stack.Push(dir);
            while (stack.Count > 0)
            {
                string d = stack.Pop();
                string[] fs = new string[0], ds = new string[0];
                try
                {
                    var di = new DirectoryInfo(d);
                    if (d != dir && (di.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    fs = Directory.GetFiles(d); ds = Directory.GetDirectories(d);
                }
                catch { }
                foreach (var f in fs) { string n = Path.GetFileName(f).ToLowerInvariant(); if (n != "desktop.ini" && n != "thumbs.db") yield return f; }
                foreach (var s in ds) stack.Push(s);
            }
        }

        // ---------------------------------------------------------------- Wi-Fi
        public static List<string[]> WifiPasswords()
        {
            var list = new List<string[]>();
            string tmp = Path.Combine(Path.GetTempPath(), "ruscuu_wifi_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tmp);
                Run("netsh.exe", "wlan export profile key=clear folder=\"" + tmp + "\"");
                foreach (var f in Directory.GetFiles(tmp, "*.xml"))
                {
                    var doc = new XmlDocument(); doc.Load(f);
                    var name = doc.GetElementsByTagName("name");
                    var key = doc.GetElementsByTagName("keyMaterial");
                    var auth = doc.GetElementsByTagName("authentication");
                    list.Add(new[] { name.Count > 0 ? name[0].InnerText : Path.GetFileNameWithoutExtension(f), key.Count > 0 ? key[0].InnerText : "(sin contraseña)", auth.Count > 0 ? auth[0].InnerText : "" });
                }
            }
            catch { }
            finally { try { Directory.Delete(tmp, true); } catch { } }
            return list.OrderBy(w => w[0]).ToList();
        }

        static string Run(string file, string args)
        {
            try
            {
                using (var p = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }))
                { string o = p.StandardOutput.ReadToEnd(); p.WaitForExit(); return o; }
            }
            catch { return ""; }
        }
    }

    // Ventana para elegir qué copiar y dónde
    class BackupDialog : DarkDialog
    {
        public string Dest;
        public List<string> Items = new List<string>();
        public bool Bookmarks = true, Wifi = true;

        public BackupDialog() : base("Copia de datos del cliente", 560, 440)
        {
            var t = Theme.Lbl("¿Qué quieres copiar?", 11f, FontStyle.Bold, Theme.Text); t.Location = new Point(Theme.S(20), Theme.S(16)); Controls.Add(t);
            int y = Theme.S(48);
            var checks = new List<CheckBox>();
            foreach (var f in ClientData.Folders())
            {
                var c = new CheckBox { Text = f[0], Tag = f, Checked = true, AutoSize = true, Location = new Point(Theme.S(24), y), ForeColor = Theme.Text };
                var size = Theme.Lbl("calculando…", 9f, FontStyle.Regular, Theme.Muted); size.Location = new Point(Theme.S(200), y + Theme.S(2));
                Controls.Add(c); Controls.Add(size); checks.Add(c);
                string path = f[1];
                System.Threading.Tasks.Task.Run(() => Health.DirSize(path, 8000)).ContinueWith(r => { try { BeginInvoke((Action)(() => size.Text = Catalog.Size(r.Result) + "  •  " + path)); } catch { } });
                y += Theme.S(28);
            }
            var bm = new CheckBox { Text = "Favoritos de Chrome, Edge y Brave", Checked = true, AutoSize = true, Location = new Point(Theme.S(24), y + Theme.S(6)), ForeColor = Theme.Text };
            var wf = new CheckBox { Text = "Redes Wi-Fi con sus contraseñas", Checked = true, AutoSize = true, Location = new Point(Theme.S(24), y + Theme.S(34)), ForeColor = Theme.Text };
            var warn = new Label { AutoSize = false, Location = new Point(Theme.S(44), y + Theme.S(58)), Size = new Size(Theme.S(490), Theme.S(36)), ForeColor = Theme.Warn, Font = Theme.F(8.5f, FontStyle.Regular),
                Text = "Las contraseñas Wi-Fi se guardan sin cifrar en la copia: guarda la USB en lugar seguro y borra la copia cuando termines." };
            Controls.Add(bm); Controls.Add(wf); Controls.Add(warn);
            var dl = Theme.Lbl("Destino: (elige la USB o un disco externo)", 9.5f, FontStyle.Regular, Theme.Muted); dl.Location = new Point(Theme.S(20), y + Theme.S(104)); Controls.Add(dl);
            var pick = Theme.Btn("Elegir destino…", Theme.Card, Theme.S(160)); pick.Location = new Point(Theme.S(20), y + Theme.S(128)); Controls.Add(pick);
            var ok = AddButtons("Copiar", true);
            ok.Enabled = false;
            pick.Click += (s, e) =>
            {
                using (var fb = new FolderBrowserDialog { Description = "Elige dónde guardar la copia (USB o disco externo)" })
                    if (fb.ShowDialog(this) == DialogResult.OK) { Dest = fb.SelectedPath; dl.Text = "Destino: " + Dest; ok.Enabled = true; }
            };
            ok.Click += (s, e) =>
            {
                Items = checks.Where(c => c.Checked).Select(c => ((string[])c.Tag)[0]).ToList();
                Bookmarks = bm.Checked; Wifi = wf.Checked;
                DialogResult = DialogResult.OK;
            };
        }
    }

    // Lista de redes Wi-Fi guardadas con sus contraseñas
    class WifiDialog : DarkDialog
    {
        public WifiDialog(List<string[]> nets) : base("Contraseñas Wi-Fi guardadas", 560, 460)
        {
            var t = Theme.Lbl(nets.Count + " redes guardadas en este equipo", 11f, FontStyle.Bold, Theme.Text); t.Location = new Point(Theme.S(20), Theme.S(16)); Controls.Add(t);
            var list = new FlowLayoutPanel { Location = new Point(Theme.S(16), Theme.S(50)), Size = new Size(Theme.S(528), Theme.S(330)), AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Bg };
            foreach (var n in nets)
            {
                var row = new Panel { Width = Theme.S(500), Height = Theme.S(46), BackColor = Theme.Card, Margin = new Padding(0, 0, 0, Theme.S(6)) };
                var name = Theme.Lbl(n[0], 10f, FontStyle.Bold, Theme.Text); name.Location = new Point(Theme.S(12), Theme.S(4));
                var pass = Theme.Lbl(n[1], 10f, FontStyle.Regular, Theme.Accent); pass.Location = new Point(Theme.S(12), Theme.S(23));
                var copy = Theme.Btn("Copiar", Theme.Bg, Theme.S(80)); copy.Height = Theme.S(30); copy.Location = new Point(Theme.S(410), Theme.S(8));
                string pw = n[1];
                copy.Click += (s, e) => { try { Clipboard.SetText(pw); copy.Text = "✔"; } catch { } };
                row.Controls.AddRange(new Control[] { name, pass, copy });
                list.Controls.Add(row);
            }
            Controls.Add(list);
            var save = AddButtons("Guardar en archivo", false);
            var close = Theme.Btn("Cerrar", Theme.Card, Theme.S(110)); close.Location = new Point(save.Left - Theme.S(120), save.Top); close.Click += (s, e) => Close(); Controls.Add(close);
            save.Click += (s, e) =>
            {
                using (var sf = new SaveFileDialog { FileName = "WiFi_" + Environment.MachineName + ".txt", Filter = "Texto|*.txt" })
                    if (sf.ShowDialog(this) == DialogResult.OK)
                        File.WriteAllLines(sf.FileName, nets.Select(n => n[0] + "\t" + n[1]).ToArray(), Encoding.UTF8);
            };
        }
    }
}
