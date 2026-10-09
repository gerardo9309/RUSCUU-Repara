// RUSCUU Repara - instalador
// Lleva dentro "RUSCUU Repara.exe" (recurso app.exe): lo copia a Archivos de programa,
// crea accesos directos y lo registra en "Aplicaciones instaladas" de Windows.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Instalador de RUSCUU Repara")]
[assembly: AssemblyProduct("RUSCUU Repara")]
[assembly: AssemblyCompany("RUSCUU")]
[assembly: AssemblyVersion(Ruscuu.AppInfo.Version + ".0")]
[assembly: AssemblyFileVersion(Ruscuu.AppInfo.Version + ".0")]

namespace Ruscuu.Setup
{
    class SetupForm : Form
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        static readonly Color Bg = Color.FromArgb(26, 11, 46), Card = Color.FromArgb(38, 18, 62), Accent = Color.FromArgb(160, 82, 255),
            Text1 = Color.FromArgb(242, 232, 213), Muted = Color.FromArgb(170, 155, 190), Ok = Color.FromArgb(110, 220, 140);
        const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\RUSCUU Repara";

        readonly TextBox path;
        readonly CheckBox desktop, startMenu;
        readonly Button install, cancel;
        readonly Label status;
        readonly ProgressBar progress;
        readonly float sc;
        string installedExe;

        int S(int v) { return (int)(v * sc); }

        public SetupForm()
        {
            using (var g = CreateGraphics()) sc = g.DpiX / 96f;
            Text = "Instalar RUSCUU Repara " + AppInfo.Version;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen; BackColor = Bg; ForeColor = Text1;
            AutoScaleMode = AutoScaleMode.None; ClientSize = new Size(S(600), S(420));
            Font = new Font("Segoe UI", 9.5f);

            var logo = new PictureBox { Location = new Point(S(28), S(26)), Size = new Size(S(130), S(130)), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Bg };
            try { logo.Image = Image.FromStream(Assembly.GetExecutingAssembly().GetManifestResourceStream("logo.png")); } catch { }
            var title = new Label { Text = "RUSCUU Repara", AutoSize = true, Font = new Font("Segoe UI", 22f, FontStyle.Bold), Location = new Point(S(176), S(36)), ForeColor = Text1 };
            var sub = new Label { Text = "Versión " + AppInfo.Version + "  •  Reparación y mantenimiento de Windows", AutoSize = true, ForeColor = Muted, Location = new Point(S(180), S(84)) };
            var feat = new Label { AutoSize = false, Size = new Size(S(400), S(44)), ForeColor = Muted, Location = new Point(S(180), S(108)),
                Text = "Salud del PC, reparaciones de un clic, limpieza, Modo Gamer, instalador de programas, copias de seguridad y mucho más." };

            var pl = new Label { Text = "Carpeta de instalación", AutoSize = true, ForeColor = Muted, Location = new Point(S(28), S(180)) };
            path = new TextBox { Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "RUSCUU Repara"), Location = new Point(S(28), S(202)), Width = S(430), BackColor = Card, ForeColor = Text1, BorderStyle = BorderStyle.FixedSingle };
            var browse = MakeBtn("Cambiar…", Card, S(110)); browse.Location = new Point(S(466), S(198));
            browse.Click += (s, e) =>
            {
                using (var fb = new FolderBrowserDialog { Description = "Elige dónde instalar RUSCUU Repara" })
                    if (fb.ShowDialog(this) == DialogResult.OK) path.Text = Path.Combine(fb.SelectedPath, "RUSCUU Repara");
            };
            desktop = new CheckBox { Text = "Crear acceso directo en el escritorio", Checked = true, AutoSize = true, Location = new Point(S(28), S(244)), ForeColor = Text1 };
            startMenu = new CheckBox { Text = "Añadir al menú Inicio", Checked = true, AutoSize = true, Location = new Point(S(28), S(272)), ForeColor = Text1 };
            progress = new ProgressBar { Location = new Point(S(28), S(312)), Size = new Size(S(544), S(8)), Visible = false };
            status = new Label { AutoSize = false, Size = new Size(S(544), S(22)), Location = new Point(S(28), S(326)), ForeColor = Muted };
            install = MakeBtn("Instalar", Accent, S(150)); install.Location = new Point(S(422), S(360));
            cancel = MakeBtn("Cancelar", Card, S(120)); cancel.Location = new Point(S(292), S(360));
            cancel.Click += (s, e) => Close();
            install.Click += (s, e) => { if (installedExe != null) { Process.Start(installedExe); Close(); } else Install(); };
            Controls.AddRange(new Control[] { logo, title, sub, feat, pl, path, browse, desktop, startMenu, progress, status, install, cancel });
            AcceptButton = install;

            using (var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(UninstallKey))
                if (k != null)
                {
                    string loc = Convert.ToString(k.GetValue("InstallLocation", ""));
                    if (loc != "") path.Text = loc;
                    status.Text = "Ya está instalada la versión " + k.GetValue("DisplayVersion") + ": se actualizará conservando tu configuración.";
                    install.Text = "Actualizar";
                }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int on = 1; DwmSetWindowAttribute(Handle, 20, ref on, 4); int c = 0x1F0611; DwmSetWindowAttribute(Handle, 35, ref c, 4); } catch { }
        }

        Button MakeBtn(string text, Color bg, int w)
        {
            var b = new Button { Text = text, Width = w, Height = S(38), FlatStyle = FlatStyle.Flat, BackColor = bg, ForeColor = Color.White, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Cursor = Cursors.Hand };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        void Install()
        {
            string dir = path.Text.Trim();
            string exe = Path.Combine(dir, "RUSCUU Repara.exe");
            install.Enabled = false; cancel.Enabled = false; progress.Visible = true;
            try
            {
                var running = Process.GetProcessesByName("RUSCUU Repara");
                if (running.Length > 0)
                {
                    if (MessageBox.Show("RUSCUU Repara está abierto. ¿Cerrarlo para continuar?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    { install.Enabled = true; cancel.Enabled = true; return; }
                    foreach (var p in running) try { p.Kill(); p.WaitForExit(5000); } catch { }
                }
                Step(15, "Copiando archivos…");
                Directory.CreateDirectory(dir);
                using (var src = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.exe"))
                using (var dst = File.Create(exe)) src.CopyTo(dst);

                Step(55, "Creando accesos directos…");
                if (desktop.Checked) Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "RUSCUU Repara.lnk"), exe, dir);
                if (startMenu.Checked) Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "RUSCUU Repara.lnk"), exe, dir);

                Step(80, "Registrando en Windows…");
                using (var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).CreateSubKey(UninstallKey))
                {
                    k.SetValue("DisplayName", "RUSCUU Repara");
                    k.SetValue("DisplayVersion", AppInfo.Version);
                    k.SetValue("Publisher", "RUSCUU");
                    k.SetValue("DisplayIcon", exe + ",0");
                    k.SetValue("InstallLocation", dir);
                    k.SetValue("UninstallString", "\"" + exe + "\" --uninstall");
                    k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                    k.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                }
                Step(100, "✔ RUSCUU Repara " + AppInfo.Version + " se instaló correctamente.");
                status.ForeColor = Ok;
                installedExe = exe;
                install.Text = "Abrir"; install.Enabled = true;
                cancel.Text = "Cerrar"; cancel.Enabled = true;
            }
            catch (Exception ex)
            {
                status.ForeColor = Color.FromArgb(255, 110, 110);
                status.Text = "Error: " + ex.Message;
                install.Enabled = true; cancel.Enabled = true;
            }
        }

        void Step(int pct, string text) { progress.Value = pct; status.Text = text; Application.DoEvents(); }

        // acceso directo con WScript.Shell (enlace tardío, sin referencias COM extra)
        static void Shortcut(string lnk, string target, string workDir)
        {
            var t = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(t);
            try
            {
                object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                var st = sc.GetType();
                st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
                st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { workDir });
                st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
                st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { "Reparación y mantenimiento de Windows" });
                st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
                Marshal.FinalReleaseComObject(sc);
            }
            finally { Marshal.FinalReleaseComObject(shell); }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
        }
    }
}
