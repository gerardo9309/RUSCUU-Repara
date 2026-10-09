// RUSCUU Repara - diagnóstico: salud del PC, pantallazos azules, licencias, navegadores y Modo Gamer
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace Ruscuu
{
    static class Wmi
    {
        public static List<ManagementBaseObject> Query(string ns, string query)
        {
            var r = new List<ManagementBaseObject>();
            try
            {
                using (var s = new ManagementObjectSearcher(new ManagementScope(ns), new ObjectQuery(query)))
                    foreach (var o in s.Get()) r.Add(o);
            }
            catch { }
            return r;
        }
        public static string Prop(ManagementBaseObject o, string p)
        {
            try { var v = o[p]; return v == null ? "" : v.ToString().Trim(); } catch { return ""; }
        }
    }

    // ================================================================ Salud del equipo
    class Finding
    {
        public string Title, Detail, Page;
        public int Severity, Penalty;   // 0 bien, 1 aviso, 2 problema
        public string[] Fix;
    }

    class HealthResult
    {
        public int Score = 100;
        public List<Finding> Findings = new List<Finding>();
        public string Label { get { return Score >= 90 ? "Excelente" : Score >= 75 ? "Bien" : Score >= 50 ? "Mejorable" : "Necesita ayuda"; } }
    }

    static class Health
    {
        public static HealthResult Analyze(int bloatCount)
        {
            var r = new HealthResult();
            Action<int, int, string, string, string[], string> add = (sev, pen, title, detail, fix, page) =>
                r.Findings.Add(new Finding { Severity = sev, Penalty = pen, Title = title, Detail = detail, Fix = fix, Page = page });

            // disco del sistema
            try
            {
                var d = new DriveInfo(Environment.GetEnvironmentVariable("SystemDrive") ?? "C:");
                double free = d.AvailableFreeSpace * 100.0 / d.TotalSize;
                if (free < 10) add(2, 20, "Disco casi lleno", string.Format("Solo queda {0:0}% libre ({1}). Windows se vuelve lento y puede fallar.", free, Catalog.Size(d.AvailableFreeSpace)), new[] { "tempuser", "tempwin", "wucache", "recycle", "compclean" }, null);
                else if (free < 20) add(1, 8, "Poco espacio en disco", string.Format("Queda {0:0}% libre ({1}).", free, Catalog.Size(d.AvailableFreeSpace)), new[] { "tempuser", "tempwin", "wucache", "compclean" }, null);
            }
            catch { }

            // archivos temporales
            long temp = DirSize(Path.GetTempPath(), 3000) + DirSize(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"), 2000);
            if (temp > 1L << 30) add(1, 5, "Muchos archivos temporales", Catalog.Size(temp) + " de basura que se puede borrar.", new[] { "tempuser", "tempwin" }, null);

            // memoria
            var m = new Native.MEMSTATUS(); Native.GlobalMemoryStatusEx(m);
            if (m.dwMemoryLoad > 85) add(1, 8, "Memoria RAM saturada", m.dwMemoryLoad + "% en uso. Cierra programas o revisa los que arrancan solos.", null, "Arranque");

            // programas de inicio
            int startup = StartupManager.List().Count(i => i.Enabled);
            if (startup > 10) add(1, 6, "Demasiados programas al encender", startup + " programas se abren solos y retrasan el arranque.", null, "Arranque");

            // antivirus y firewall
            var av = Wmi.Query(@"root\SecurityCenter2", "SELECT displayName, productState FROM AntiVirusProduct");
            bool avOn = av.Any(o => { int st; return int.TryParse(Wmi.Prop(o, "productState"), out st) && ((st >> 12) & 0xF) == 1; });
            if (av.Count == 0 || !avOn) add(2, 25, "Antivirus desactivado", av.Count == 0 ? "No hay ningún antivirus instalado." : "El antivirus está apagado.", new[] { "defupdate", "defquick" }, null);
            foreach (var d in Wmi.Query(@"root\Microsoft\Windows\Defender", "SELECT AntivirusEnabled, AntivirusSignatureAge FROM MSFT_MpComputerStatus"))
            {
                int age;
                if (Wmi.Prop(d, "AntivirusEnabled") == "True" && int.TryParse(Wmi.Prop(d, "AntivirusSignatureAge"), out age) && age > 7)
                    add(1, 6, "Antivirus desactualizado", "Las firmas de virus tienen " + age + " días.", new[] { "defupdate" }, null);
            }
            var fw = Wmi.Query(@"root\StandardCimv2", "SELECT Enabled FROM MSFT_NetFirewallProfile");
            if (fw.Count > 0 && fw.Any(f => Wmi.Prop(f, "Enabled") != "1")) add(2, 15, "Firewall desactivado", "El equipo está expuesto en la red.", new[] { "fwon" }, null);
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"))
                    if (k != null && Convert.ToInt32(k.GetValue("EnableLUA", 1)) == 0) add(2, 10, "Control de cuentas (UAC) apagado", "Cualquier programa puede cambiar el sistema sin avisar.", new[] { "uacon" }, null);
            }
            catch { }

            // discos físicos
            foreach (var p in Wmi.Query(@"root\Microsoft\Windows\Storage", "SELECT FriendlyName, HealthStatus FROM MSFT_PhysicalDisk"))
            {
                string hs = Wmi.Prop(p, "HealthStatus");
                if (hs == "2") add(2, 30, "Disco en mal estado", Wmi.Prop(p, "FriendlyName") + ": haz una copia de tus datos YA y cambia el disco.", null, "Herramientas");
                else if (hs == "1") add(1, 12, "Disco con advertencias", Wmi.Prop(p, "FriendlyName") + " reporta problemas de salud.", new[] { "smart", "chkboot" }, null);
            }

            // dispositivos con error
            int bad = Wmi.Query(@"root\cimv2", "SELECT Name FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0 AND ConfigManagerErrorCode <> 22 AND ConfigManagerErrorCode <> 45").Count;
            if (bad > 0) add(1, 6, "Dispositivos con error", bad + " dispositivo(s) sin driver o con fallos.", new[] { "drivers" }, null);

            // pantallazos azules
            int crashes = Bsod.Recent(30).Count;
            if (crashes > 0) add(crashes > 2 ? 2 : 1, Math.Min(20, crashes * 6), "Pantallazos azules recientes", crashes + " en los últimos 30 días.", new[] { "bsod" }, null);

            // tiempo encendido / reinicio pendiente
            var up = TimeSpan.FromMilliseconds(Native.GetTickCount64());
            bool pending = KeyExists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired") || KeyExists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
            if (pending) add(1, 5, "Reinicio pendiente", "Windows necesita reiniciar para terminar de actualizar.", null, null);
            else if (up.TotalDays > 7) add(1, 4, "Sin reiniciar hace " + up.Days + " días", "Reiniciar de vez en cuando libera memoria y aplica actualizaciones.", null, null);

            // actualizaciones
            var last = Wmi.Query(@"root\cimv2", "SELECT InstalledOn FROM Win32_QuickFixEngineering").Select(o => ParseDate(Wmi.Prop(o, "InstalledOn"))).Where(dt => dt.HasValue).Select(dt => dt.Value).DefaultIfEmpty(DateTime.MinValue).Max();
            if (last > DateTime.MinValue && (DateTime.Now - last).TotalDays > 45)
                add(1, 6, "Windows sin actualizar", "La última actualización se instaló hace " + (int)(DateTime.Now - last).TotalDays + " días.", new[] { "wuopen" }, null);

            // activación
            var lic = Wmi.Query(@"root\cimv2", "SELECT LicenseStatus FROM SoftwareLicensingProduct WHERE PartialProductKey IS NOT NULL AND ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f'");
            if (lic.Count > 0 && !lic.Any(o => Wmi.Prop(o, "LicenseStatus") == "1")) add(1, 5, "Windows sin activar", "Algunas funciones están limitadas.", new[] { "license" }, null);

            // batería
            var design = Wmi.Query(@"root\wmi", "SELECT DesignedCapacity FROM BatteryStaticData");
            var full = Wmi.Query(@"root\wmi", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity");
            double dc, fc;
            if (design.Count > 0 && full.Count > 0 && double.TryParse(Wmi.Prop(design[0], "DesignedCapacity"), out dc) && double.TryParse(Wmi.Prop(full[0], "FullChargedCapacity"), out fc) && dc > 0 && fc * 100 / dc < 70)
                add(1, 5, "Batería desgastada", string.Format("Conserva el {0:0}% de su capacidad original.", fc * 100 / dc), new[] { "battery" }, null);

            // apps basura
            if (bloatCount >= 3) add(1, 3, "Apps basura instaladas", bloatCount + " apps preinstaladas que casi nadie usa.", null, Catalog.Apps);

            r.Score = Math.Max(0, 100 - r.Findings.Sum(f => f.Penalty));
            r.Findings = r.Findings.OrderByDescending(f => f.Severity).ThenByDescending(f => f.Penalty).ToList();
            return r;
        }

        static bool KeyExists(string path)
        {
            try { using (var k = Registry.LocalMachine.OpenSubKey(path)) return k != null; } catch { return false; }
        }

        static DateTime? ParseDate(string s)
        {
            DateTime d;
            if (DateTime.TryParseExact(s, new[] { "M/d/yyyy", "MM/dd/yyyy", "yyyyMMdd", "d/M/yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return d;
            return null;
        }

        public static long DirSize(string dir, int maxMs)
        {
            long total = 0;
            var sw = Stopwatch.StartNew();
            var stack = new Stack<string>(); stack.Push(dir);
            while (stack.Count > 0 && sw.ElapsedMilliseconds < maxMs)
            {
                string d = stack.Pop();
                try
                {
                    foreach (var f in new DirectoryInfo(d).EnumerateFileSystemInfos())
                    {
                        if ((f.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if ((f.Attributes & FileAttributes.Directory) != 0) stack.Push(f.FullName);
                        else total += ((FileInfo)f).Length;
                    }
                }
                catch { }
            }
            return total;
        }
    }

    // ================================================================ Pantallazos azules
    static class Bsod
    {
        public class Crash { public DateTime When; public uint Code; public string Raw; }

        public static List<Crash> Recent(int days)
        {
            var list = new List<Crash>();
            try
            {
                var q = new EventLogQuery("System", PathType.LogName, "*[System[Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting'] and (EventID=1001)]]") { ReverseDirection = true };
                using (var reader = new EventLogReader(q))
                {
                    EventRecord e;
                    while ((e = reader.ReadEvent()) != null)
                        using (e)
                        {
                            var when = e.TimeCreated ?? DateTime.MinValue;
                            if (when < DateTime.Now.AddDays(-days)) break;
                            string raw = e.Properties.Count > 0 ? Convert.ToString(e.Properties[0].Value) : "";
                            var mm = Regex.Match(raw, "0x([0-9a-fA-F]{8})");
                            uint code = mm.Success ? Convert.ToUInt32(mm.Groups[1].Value, 16) : 0;
                            if (code != 0xC000021A) code &= 0x0FFFFFFF;
                            list.Add(new Crash { When = when, Code = code, Raw = raw });
                        }
                }
            }
            catch { }
            return list;
        }

        static int UnexpectedShutdowns(int days)
        {
            int n = 0;
            try
            {
                var q = new EventLogQuery("System", PathType.LogName, "*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and (EventID=41)]]") { ReverseDirection = true };
                using (var reader = new EventLogReader(q))
                {
                    EventRecord e;
                    while ((e = reader.ReadEvent()) != null) using (e) { if ((e.TimeCreated ?? DateTime.MinValue) < DateTime.Now.AddDays(-days)) break; n++; }
                }
            }
            catch { }
            return n;
        }

        // código -> { nombre, explicación, tareas sugeridas }
        static readonly Dictionary<uint, string[]> Info = new Dictionary<uint, string[]> {
            { 0x0A, new[] { "IRQL_NOT_LESS_OR_EQUAL", "Un driver accedió a memoria indebida. Suele ser un driver defectuoso o RAM inestable.", "drivers,memtest" } },
            { 0x19, new[] { "BAD_POOL_HEADER", "Memoria del sistema dañada, casi siempre por un driver (antivirus, VPN, red).", "drivers,memtest" } },
            { 0x1A, new[] { "MEMORY_MANAGEMENT", "Error grave de memoria. Lo más probable: módulo de RAM defectuoso o perfil XMP inestable.", "memtest" } },
            { 0x1E, new[] { "KMODE_EXCEPTION_NOT_HANDLED", "Un driver provocó un error que Windows no pudo manejar.", "drivers,sfc" } },
            { 0x24, new[] { "NTFS_FILE_SYSTEM", "Error en el sistema de archivos del disco.", "chkboot,smart" } },
            { 0x3B, new[] { "SYSTEM_SERVICE_EXCEPTION", "Fallo en un servicio del sistema; suele deberse a drivers de gráfica o antivirus.", "drivers,dism,sfc" } },
            { 0x4E, new[] { "PFN_LIST_CORRUPT", "Corrupción de la lista de memoria: RAM o driver defectuoso.", "memtest" } },
            { 0x50, new[] { "PAGE_FAULT_IN_NONPAGED_AREA", "Se pidió memoria que no existe: RAM, driver o disco con problemas.", "memtest,chkboot,drivers" } },
            { 0x7A, new[] { "KERNEL_DATA_INPAGE_ERROR", "No se pudo leer datos del disco: disco dañado o cable flojo.", "smart,chkboot" } },
            { 0x7E, new[] { "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED", "Un driver falló durante un proceso del sistema.", "drivers,sfc" } },
            { 0x7F, new[] { "UNEXPECTED_KERNEL_MODE_TRAP", "Error del procesador: hardware, sobrecalentamiento u overclock.", "memtest" } },
            { 0x8E, new[] { "KERNEL_MODE_EXCEPTION_NOT_HANDLED", "Excepción en el núcleo: RAM o driver defectuoso.", "memtest,drivers" } },
            { 0x9F, new[] { "DRIVER_POWER_STATE_FAILURE", "Un driver no respondió al suspender o despertar el equipo.", "drivers" } },
            { 0xA0, new[] { "INTERNAL_POWER_ERROR", "Error de gestión de energía (suspensión/hibernación).", "drivers" } },
            { 0xC2, new[] { "BAD_POOL_CALLER", "Un driver usó mal la memoria del sistema.", "drivers" } },
            { 0xC5, new[] { "DRIVER_CORRUPTED_EXPOOL", "Un driver dañó la memoria del sistema.", "drivers" } },
            { 0xD1, new[] { "DRIVER_IRQL_NOT_LESS_OR_EQUAL", "Driver defectuoso, muy a menudo el de red o Wi-Fi.", "drivers" } },
            { 0xEF, new[] { "CRITICAL_PROCESS_DIED", "Un proceso vital de Windows se cerró: archivos del sistema dañados.", "dism,sfc,smart" } },
            { 0xF4, new[] { "CRITICAL_OBJECT_TERMINATION", "Un proceso crítico terminó, normalmente por fallos del disco.", "smart,chkboot" } },
            { 0xFC, new[] { "ATTEMPTED_EXECUTE_OF_NOEXECUTE_MEMORY", "Un driver intentó ejecutar memoria protegida.", "drivers" } },
            { 0x101, new[] { "CLOCK_WATCHDOG_TIMEOUT", "Un núcleo del procesador dejó de responder: overclock, temperatura o BIOS.", "" } },
            { 0x109, new[] { "CRITICAL_STRUCTURE_CORRUPTION", "Estructura del núcleo modificada: driver defectuoso o RAM.", "memtest,drivers" } },
            { 0x10D, new[] { "WDF_VIOLATION", "Un driver USB o de periféricos falló.", "drivers" } },
            { 0x116, new[] { "VIDEO_TDR_FAILURE", "La tarjeta gráfica dejó de responder. Reinstala el driver de vídeo (usa DDU) y revisa temperaturas.", "drivers" } },
            { 0x117, new[] { "VIDEO_TDR_TIMEOUT_DETECTED", "La tarjeta gráfica tardó demasiado en responder.", "drivers" } },
            { 0x119, new[] { "VIDEO_SCHEDULER_INTERNAL_ERROR", "Error del driver de vídeo.", "drivers" } },
            { 0x124, new[] { "WHEA_UNCORRECTABLE_ERROR", "Error de hardware: procesador, RAM, overclock o temperaturas altas. Quita el overclock y revisa la refrigeración.", "memtest" } },
            { 0x133, new[] { "DPC_WATCHDOG_VIOLATION", "Un driver tardó demasiado; suele ser el del SSD o almacenamiento. Actualiza el firmware del SSD.", "drivers,smart" } },
            { 0x139, new[] { "KERNEL_SECURITY_CHECK_FAILURE", "Corrupción detectada en el núcleo: drivers incompatibles o RAM.", "drivers,memtest,sfc" } },
            { 0x13A, new[] { "KERNEL_MODE_HEAP_CORRUPTION", "Memoria del núcleo dañada, normalmente por el driver de gráfica.", "drivers" } },
            { 0x154, new[] { "UNEXPECTED_STORE_EXCEPTION", "Error del almacenamiento: disco o su driver.", "smart,chkboot" } },
            { 0x15F, new[] { "CONNECTED_STANDBY_WATCHDOG_TIMEOUT_LIVEDUMP", "Fallo al entrar en reposo moderno.", "drivers" } },
            { 0x1A8, new[] { "BLUETOOTH_ERROR_RECOVERY_LIVEDUMP", "Error del driver de Bluetooth.", "drivers" } },
            { 0xC000021A, new[] { "STATUS_SYSTEM_PROCESS_TERMINATED", "Un proceso de inicio de sesión falló: archivos del sistema dañados.", "dism,sfc" } },
        };

        public static int Analyze(Action<string> log)
        {
            var crashes = Recent(90);
            int shutdowns = UnexpectedShutdowns(30);
            string dumps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Minidump");
            int dumpCount = 0;
            try { if (Directory.Exists(dumps)) dumpCount = Directory.GetFiles(dumps, "*.dmp").Length; } catch { }
            log(crashes.Count + " pantallazos azules en los últimos 90 días  •  " + shutdowns + " apagados inesperados en 30 días  •  " + dumpCount + " minidumps guardados.");
            if (crashes.Count == 0)
            {
                log(shutdowns > 0 ? "Sin pantallazos, pero hubo apagados bruscos: revisa la fuente de alimentación, la batería o las temperaturas." : "¡Ningún pantallazo azul registrado!");
                return 0;
            }
            var fixes = new HashSet<string>();
            foreach (var grp in crashes.GroupBy(c => c.Code).OrderByDescending(g => g.Count()))
            {
                uint code = grp.Key;
                string[] info;
                if (!Info.TryGetValue(code, out info)) info = new[] { "Código 0x" + code.ToString("X"), "Código poco común. Busca el nombre en Internet o analiza el minidump con BlueScreenView.", "drivers,sfc" };
                log("");
                log("■ " + info[0] + "  (0x" + code.ToString("X") + ")  —  " + grp.Count() + " vez/veces, la última el " + grp.First().When.ToString("dd/MM/yyyy HH:mm"));
                log("  " + info[1]);
                foreach (var f in info[2].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) fixes.Add(f);
            }
            log("");
            var names = new Dictionary<string, string> { { "drivers", "Diagnóstico › Dispositivos con problemas" }, { "memtest", "Diagnóstico › Prueba de memoria RAM" }, { "sfc", "Sistema › SFC" }, { "dism", "Sistema › DISM" }, { "chkboot", "Sistema › Reparar disco al reiniciar" }, { "smart", "Diagnóstico › Salud de los discos" } };
            log("Recomendado: " + string.Join("  •  ", fixes.Where(names.ContainsKey).Select(f => names[f]).ToArray()));
            log("Para ver el driver exacto, abre el minidump con BlueScreenView (enlace en tu USB técnico).");
            return 0;
        }
    }

    // ================================================================ Licencias
    static class License
    {
        public static int Show(Action<string> log)
        {
            foreach (var s in Wmi.Query(@"root\cimv2", "SELECT OA3xOriginalProductKey FROM SoftwareLicensingService"))
            {
                string oem = Wmi.Prop(s, "OA3xOriginalProductKey");
                log(oem != "" ? "Clave OEM grabada en la BIOS: " + oem : "No hay clave OEM grabada en la BIOS.");
            }
            try
            {
                using (var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    var id = k.GetValue("DigitalProductId") as byte[];
                    if (id != null && id.Length > 66) log("Clave instalada en Windows: " + DecodeKey(id) + "   (si el equipo usa licencia digital puede ser una clave genérica)");
                    log("Edición: " + k.GetValue("EditionID") + "  •  Versión " + k.GetValue("DisplayVersion") + "  •  Instalado el " + new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(Convert.ToInt64(k.GetValue("InstallDate", 0))).ToLocalTime().ToString("dd/MM/yyyy"));
                }
            }
            catch { }
            string[] states = { "Sin licencia", "Activado", "Periodo de gracia", "Gracia adicional", "Gracia (no genuino)", "Notificación", "Gracia extendida" };
            log("");
            foreach (var p in Wmi.Query(@"root\cimv2", "SELECT Name, LicenseStatus, PartialProductKey FROM SoftwareLicensingProduct WHERE PartialProductKey IS NOT NULL"))
            {
                int st; int.TryParse(Wmi.Prop(p, "LicenseStatus"), out st);
                log((st == 1 ? "✔ " : "⚠ ") + Wmi.Prop(p, "Name") + "  •  …" + Wmi.Prop(p, "PartialProductKey") + "  •  " + (st >= 0 && st < states.Length ? states[st] : "?"));
            }
            try
            {
                using (var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Microsoft\Office\ClickToRun\Configuration"))
                    if (k != null) log("Office instalado (Hacer clic y ejecutar): " + k.GetValue("ProductReleaseIds") + "  •  versión " + k.GetValue("VersionToReport"));
            }
            catch { }
            return 0;
        }

        static string DecodeKey(byte[] src)
        {
            var id = (byte[])src.Clone();
            const int offset = 52;
            const string chars = "BCDFGHJKMPQRTVWXY2346789";
            int isWin8 = (id[66] / 6) & 1;
            id[66] = (byte)((id[66] & 0xF7) | ((isWin8 & 2) * 4));
            string key = ""; int last = 0;
            for (int i = 24; i >= 0; i--)
            {
                int cur = 0;
                for (int j = 14; j >= 0; j--)
                {
                    cur = cur * 256 + id[j + offset];
                    id[j + offset] = (byte)(cur / 24);
                    cur %= 24;
                }
                key = chars[cur] + key;
                last = cur;
            }
            if (isWin8 == 1)
            {
                string part1 = key.Substring(1, last), part2 = key.Substring(1);
                key = last == 0 ? "N" + part2 : part2.Insert(part2.IndexOf(part1, StringComparison.Ordinal) + part1.Length, "N");
            }
            var sb = new StringBuilder();
            for (int i = 0; i < 25; i++) { if (i > 0 && i % 5 == 0) sb.Append('-'); sb.Append(key[i]); }
            return sb.ToString();
        }
    }

    // ================================================================ Navegadores
    static class Browsers
    {
        static string Local { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }
        static string Roaming { get { return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); } }

        // { nombre, carpeta de datos, proceso }
        static readonly string[][] Chromium = {
            new[] { "Google Chrome", @"Google\Chrome\User Data", "chrome" },
            new[] { "Microsoft Edge", @"Microsoft\Edge\User Data", "msedge" },
            new[] { "Brave", @"BraveSoftware\Brave-Browser\User Data", "brave" },
            new[] { "Opera GX", "*opera", "opera" },
        };

        public static int CleanCache(string browser, Action<string> log)
        {
            long freed = 0; int n = 0;
            if (browser == "firefox")
            {
                if (Process.GetProcessesByName("firefox").Length > 0) { log("Cierra Firefox y vuelve a intentarlo."); return 1; }
                string prof = Path.Combine(Local, @"Mozilla\Firefox\Profiles");
                if (!Directory.Exists(prof)) { log("Firefox no está instalado."); return 0; }
                foreach (var p in Directory.GetDirectories(prof)) freed += Wipe(Path.Combine(p, "cache2"), ref n);
            }
            else
            {
                var b = Chromium.First(x => x[2] == browser);
                if (Process.GetProcessesByName(b[2]).Length > 0) { log("Cierra " + b[0] + " y vuelve a intentarlo."); return 1; }
                string root = b[1] == "*opera" ? Path.Combine(Local, @"Opera Software\Opera GX Stable") : Path.Combine(Local, b[1]);
                if (!Directory.Exists(root)) { log(b[0] + " no está instalado."); return 0; }
                var profiles = Directory.GetDirectories(root).Where(d => { string nm = Path.GetFileName(d); return nm == "Default" || nm.StartsWith("Profile "); }).ToList();
                profiles.Add(root);
                foreach (var p in profiles)
                    foreach (var sub in new[] { "Cache", "Code Cache", "GPUCache", @"Service Worker\CacheStorage", @"Service Worker\ScriptCache" })
                        freed += Wipe(Path.Combine(p, sub), ref n);
                freed += Wipe(Path.Combine(root, "ShaderCache"), ref n) + Wipe(Path.Combine(root, "GrShaderCache"), ref n);
            }
            log(n + " archivos de caché eliminados, " + Catalog.Size(freed) + " liberados. Historial, contraseñas y sesiones intactos.");
            return 0;
        }

        static long Wipe(string dir, ref int count)
        {
            long freed = 0;
            if (!Directory.Exists(dir)) return 0;
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                try { var fi = new FileInfo(f); long l = fi.Length; fi.Delete(); freed += l; count++; } catch { }
            return freed;
        }

        // Navegador secuestrado: proxy, archivo hosts y políticas forzadas (con copia de seguridad)
        public static int FixHijack(Action<string> log)
        {
            string bk = Path.Combine(Settings.DataDir, "Respaldos", "Navegador_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm"));
            Directory.CreateDirectory(bk);
            int changes = 0;

            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true))
                {
                    if (Convert.ToInt32(k.GetValue("ProxyEnable", 0)) != 0) { log("Proxy desactivado: " + k.GetValue("ProxyServer")); k.SetValue("ProxyEnable", 0, RegistryValueKind.DWord); changes++; }
                    if (k.GetValue("AutoConfigURL") != null) { log("Script de proxy eliminado: " + k.GetValue("AutoConfigURL")); k.DeleteValue("AutoConfigURL"); changes++; }
                }
            }
            catch (Exception ex) { log("Proxy: " + ex.Message); }

            try
            {
                string hosts = Path.Combine(Environment.SystemDirectory, @"drivers\etc\hosts");
                if (File.Exists(hosts))
                {
                    var suspicious = File.ReadAllLines(hosts).Select(l => l.Trim())
                        .Where(l => l.Length > 0 && !l.StartsWith("#") && !Regex.IsMatch(l, @"^(127\.0\.0\.1|::1)\s+localhost\s*$", RegexOptions.IgnoreCase)).ToList();
                    if (suspicious.Count > 0)
                    {
                        File.Copy(hosts, Path.Combine(bk, "hosts"), true);
                        foreach (var l in suspicious.Take(15)) log("hosts: quitado «" + l + "»");
                        File.SetAttributes(hosts, FileAttributes.Normal);
                        File.WriteAllText(hosts, "# Archivo hosts restablecido por RUSCUU Repara " + DateTime.Now.ToString("dd/MM/yyyy") + "\r\n# 127.0.0.1       localhost\r\n# ::1             localhost\r\n");
                        changes++;
                    }
                }
            }
            catch (Exception ex) { log("hosts: " + ex.Message); }

            string[] policies = { @"SOFTWARE\Policies\Google\Chrome", @"SOFTWARE\Policies\Microsoft\Edge", @"SOFTWARE\Policies\BraveSoftware\Brave", @"SOFTWARE\Policies\Mozilla\Firefox" };
            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
                foreach (var p in policies)
                {
                    try
                    {
                        using (var k = hive.OpenSubKey(p)) if (k == null) continue;
                        string full = (hive == Registry.LocalMachine ? "HKLM\\" : "HKCU\\") + p;
                        string file = Path.Combine(bk, full.Replace('\\', '_') + ".reg");
                        using (var exp = Process.Start(new ProcessStartInfo("reg.exe", "export \"" + full + "\" \"" + file + "\" /y") { CreateNoWindow = true, UseShellExecute = false }))
                            exp.WaitForExit();
                        hive.DeleteSubKeyTree(p, false);
                        log("Política forzada eliminada: " + full + "  (copia en " + Path.GetFileName(file) + ")");
                        changes++;
                    }
                    catch (Exception ex) { log(p + ": " + ex.Message); }
                }

            log(changes == 0 ? "No se encontró nada sospechoso en proxy, hosts ni políticas." : changes + " cambio(s). Copia de seguridad en: " + bk);
            log("Consejo: en cada navegador abre Configuración › Restablecer configuración, y revisa las extensiones.");
            return 0;
        }
    }

    // ================================================================ Modo Gamer
    static class Gamer
    {
        [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr h);

        static readonly string[] Background = { "OneDrive", "Teams", "ms-teams", "Skype", "PhoneExperienceHost", "YourPhone", "Widgets", "WidgetService", "Copilot", "AdobeCollabSync", "CCXProcess", "Creative Cloud", "AdobeIPCBroker", "Dropbox", "GoogleDriveFS", "Cortana" };
        static readonly Regex Guid = new Regex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");

        public static int On(Action<string> log)
        {
            if (Settings.GetBool("gamer_on", false)) log("El Modo Gamer ya estaba activo; se vuelve a aplicar.");
            else
            {
                var mm = Guid.Match(Run("powercfg.exe", "/getactivescheme"));
                if (mm.Success) Settings.Set("gamer_prev_scheme", mm.Value);
            }
            string list = Run("powercfg.exe", "/list");
            var ult = list.Split('\n').FirstOrDefault(l => l.Contains("Ultimate") || l.Contains("ximo rendimiento"));
            string guid = ult != null ? Guid.Match(ult).Value : Guid.Match(Run("powercfg.exe", "-duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61")).Value;
            if (guid == "") guid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
            Run("powercfg.exe", "/setactive " + guid);
            log("Plan de energía de máximo rendimiento activado.");
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\GameBar")) { k.SetValue("AutoGameModeEnabled", 1, RegistryValueKind.DWord); k.SetValue("AllowAutoGameMode", 1, RegistryValueKind.DWord); }
                log("Modo juego de Windows activado.");
            }
            catch { }
            var closed = new List<string>();
            foreach (var name in Background)
                foreach (var p in Process.GetProcessesByName(name))
                    try { p.Kill(); if (!closed.Contains(name)) closed.Add(name); } catch { }
            Settings.Set("gamer_closed", string.Join(",", closed.ToArray()));
            log(closed.Count > 0 ? "Cerrados en segundo plano: " + string.Join(", ", closed.ToArray()) : "No había programas en segundo plano que cerrar.");
            var before = new Native.MEMSTATUS(); Native.GlobalMemoryStatusEx(before);
            foreach (var p in Process.GetProcesses()) try { EmptyWorkingSet(p.Handle); } catch { }
            Thread.Sleep(800);
            var after = new Native.MEMSTATUS(); Native.GlobalMemoryStatusEx(after);
            long freed = (long)after.ullAvailPhys - (long)before.ullAvailPhys;
            log("Memoria liberada: " + Catalog.Size(Math.Max(0, freed)) + "  (RAM libre: " + Catalog.Size((long)after.ullAvailPhys) + ")");
            Settings.SetBool("gamer_on", true);
            log("🎮 ¡Modo Gamer activado! Cuando termines de jugar pulsa «Salir del Modo Gamer».");
            return 0;
        }

        public static int Off(Action<string> log)
        {
            string prev = Settings.Get("gamer_prev_scheme", "381b4222-f694-41f0-9685-ff5bb260df2e");
            Run("powercfg.exe", "/setactive " + prev);
            log("Plan de energía anterior restaurado.");
            string od = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\OneDrive\OneDrive.exe");
            if (Settings.Get("gamer_closed").Contains("OneDrive") && File.Exists(od))
                try { Process.Start(new ProcessStartInfo("runas.exe", "/trustlevel:0x20000 \"\\\"" + od + "\\\" /background\"") { CreateNoWindow = true, UseShellExecute = false }); log("OneDrive reiniciado."); } catch { }
            Settings.SetBool("gamer_on", false);
            log("Modo Gamer desactivado. Los demás programas se abrirán solos la próxima vez que los uses.");
            return 0;
        }

        static string Run(string file, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                using (var p = Process.Start(psi)) { string o = p.StandardOutput.ReadToEnd(); p.WaitForExit(); return o; }
            }
            catch { return ""; }
        }
    }
}
