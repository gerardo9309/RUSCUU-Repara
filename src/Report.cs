// RUSCUU Repara - informe del PC en HTML con el logo de RUSCUU
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.Win32;

namespace Ruscuu
{
    static class Report
    {
        static string H(object s) { return WebUtility.HtmlEncode(Convert.ToString(s)); }

        static List<ManagementBaseObject> Q(string ns, string query)
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

        static string P(ManagementBaseObject o, string prop)
        {
            try { var v = o[prop]; return v == null ? "" : v.ToString().Trim(); } catch { return ""; }
        }

        class Row { public string Label, Value, State; public Row(string l, string v, string s) { Label = l; Value = v; State = s; } }

        public static string Generate(string client, Action<string> log)
        {
            int warnings = 0, errors = 0;
            var sections = new List<KeyValuePair<string, List<Row>>>();
            Func<string, List<Row>> Sec = title => { var l = new List<Row>(); sections.Add(new KeyValuePair<string, List<Row>>(title, l)); return l; };
            Action<List<Row>, string, string, string> Add = (l, a, b, st) => { l.Add(new Row(a, b, st)); if (st == "warn") warnings++; if (st == "err") errors++; };

            // ---- Sistema
            log("Leyendo información del sistema…");
            var sys = Sec("🖥 Sistema");
            string build, ver;
            string os = OsName(out build, out ver);
            Add(sys, "Equipo", Environment.MachineName + "  (usuario " + Environment.UserName + ")", "");
            Add(sys, "Windows", os + " " + ver + "  •  compilación " + build, "");
            var lic = Q(@"root\cimv2", "SELECT LicenseStatus FROM SoftwareLicensingProduct WHERE PartialProductKey IS NOT NULL AND ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f'");
            bool activated = lic.Any(o => P(o, "LicenseStatus") == "1");
            Add(sys, "Activación", activated ? "Windows activado" : "Windows NO activado", activated ? "ok" : "warn");
            var up = TimeSpan.FromMilliseconds(Native.GetTickCount64());
            Add(sys, "Encendido", string.Format("{0} días, {1} h, {2} min", up.Days, up.Hours, up.Minutes), up.TotalDays > 14 ? "warn" : "");
            foreach (var b in Q(@"root\cimv2", "SELECT Manufacturer, Product FROM Win32_BaseBoard"))
                Add(sys, "Placa base", P(b, "Manufacturer") + " " + P(b, "Product"), "");
            foreach (var b in Q(@"root\cimv2", "SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS"))
            {
                string d = P(b, "ReleaseDate");
                Add(sys, "BIOS", P(b, "SMBIOSBIOSVersion") + (d.Length >= 8 ? "  (" + d.Substring(6, 2) + "/" + d.Substring(4, 2) + "/" + d.Substring(0, 4) + ")" : ""), "");
            }

            // ---- Hardware
            log("Leyendo hardware…");
            var hw = Sec("⚙ Hardware");
            foreach (var c in Q(@"root\cimv2", "SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor"))
                Add(hw, "Procesador", P(c, "Name") + "  •  " + P(c, "NumberOfCores") + " núcleos / " + P(c, "NumberOfLogicalProcessors") + " hilos", "");
            var m = new Native.MEMSTATUS(); Native.GlobalMemoryStatusEx(m);
            var mods = Q(@"root\cimv2", "SELECT Capacity, Speed FROM Win32_PhysicalMemory");
            string modTxt = mods.Count > 0 ? "  •  " + mods.Count + " módulo(s) " + string.Join(" + ", mods.Select(x => (Convert.ToUInt64(x["Capacity"]) / 1073741824UL) + " GB").ToArray()) + (P(mods[0], "Speed") != "" ? " @ " + P(mods[0], "Speed") + " MHz" : "") : "";
            Add(hw, "Memoria RAM", string.Format("{0:0.0} GB  •  {1}% en uso", m.ullTotalPhys / 1073741824.0, m.dwMemoryLoad) + modTxt, m.dwMemoryLoad > 85 ? "warn" : "");
            foreach (var g in Q(@"root\cimv2", "SELECT Name, DriverVersion FROM Win32_VideoController"))
                Add(hw, "Tarjeta gráfica", P(g, "Name") + "  •  driver " + P(g, "DriverVersion"), "");

            // ---- Almacenamiento
            log("Revisando discos…");
            var stor = Sec("💽 Almacenamiento");
            foreach (var d in DriveInfo.GetDrives().Where(x => x.DriveType == DriveType.Fixed && x.IsReady))
            {
                double free = d.AvailableFreeSpace * 100.0 / d.TotalSize;
                Add(stor, "Unidad " + d.Name.TrimEnd('\\') + (d.VolumeLabel != "" ? " (" + d.VolumeLabel + ")" : ""),
                    Catalog.Size(d.AvailableFreeSpace) + " libres de " + Catalog.Size(d.TotalSize) + string.Format("  ({0:0}% libre)", free),
                    free < 10 ? "err" : free < 20 ? "warn" : "ok");
            }
            foreach (var p in Q(@"root\Microsoft\Windows\Storage", "SELECT FriendlyName, MediaType, Size, HealthStatus FROM MSFT_PhysicalDisk"))
            {
                string media = P(p, "MediaType") == "4" ? "SSD" : P(p, "MediaType") == "3" ? "HDD" : "Disco";
                string hs = P(p, "HealthStatus");
                string health = hs == "0" ? "Saludable" : hs == "1" ? "Advertencia" : hs == "2" ? "En mal estado" : "Desconocido";
                ulong size = 0; ulong.TryParse(P(p, "Size"), out size);
                Add(stor, media + " físico", P(p, "FriendlyName") + "  •  " + Catalog.Size((long)size) + "  •  " + health, hs == "0" ? "ok" : hs == "1" ? "warn" : hs == "2" ? "err" : "");
            }

            // ---- Seguridad
            log("Revisando seguridad…");
            var sec = Sec("🛡 Seguridad");
            var av = Q(@"root\SecurityCenter2", "SELECT displayName FROM AntiVirusProduct").Select(o => P(o, "displayName")).Distinct().ToList();
            Add(sec, "Antivirus instalado", av.Count > 0 ? string.Join(", ", av.ToArray()) : "Ninguno detectado", av.Count > 0 ? "ok" : "err");
            foreach (var d in Q(@"root\Microsoft\Windows\Defender", "SELECT AntivirusEnabled, RealTimeProtectionEnabled, AntivirusSignatureAge, QuickScanAge FROM MSFT_MpComputerStatus"))
            {
                bool rt = P(d, "RealTimeProtectionEnabled") == "True";
                int age; int.TryParse(P(d, "AntivirusSignatureAge"), out age);
                if (P(d, "AntivirusEnabled") == "True")
                {
                    Add(sec, "Protección en tiempo real", rt ? "Activada" : "DESACTIVADA", rt ? "ok" : "err");
                    Add(sec, "Firmas de virus", "Actualizadas hace " + age + " día(s)", age > 7 ? "warn" : "ok");
                    Add(sec, "Último análisis rápido", "Hace " + P(d, "QuickScanAge") + " día(s)", "");
                }
            }
            var fw = Q(@"root\StandardCimv2", "SELECT Name, Enabled FROM MSFT_NetFirewallProfile");
            if (fw.Count > 0)
            {
                bool allOn = fw.All(f => P(f, "Enabled") == "1");
                Add(sec, "Firewall", allOn ? "Activado en todos los perfiles" : "Desactivado en algún perfil", allOn ? "ok" : "warn");
            }

            // ---- Batería
            var bat = Q(@"root\cimv2", "SELECT EstimatedChargeRemaining FROM Win32_Battery");
            if (bat.Count > 0)
            {
                var bs = Sec("🔋 Batería");
                Add(bs, "Carga actual", P(bat[0], "EstimatedChargeRemaining") + "%", "");
                var design = Q(@"root\wmi", "SELECT DesignedCapacity FROM BatteryStaticData");
                var full = Q(@"root\wmi", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity");
                double dc, fc;
                if (design.Count > 0 && full.Count > 0 && double.TryParse(P(design[0], "DesignedCapacity"), out dc) && double.TryParse(P(full[0], "FullChargedCapacity"), out fc) && dc > 0)
                {
                    double health = Math.Min(100, fc * 100 / dc);
                    Add(bs, "Salud de la batería", string.Format("{0:0}% de la capacidad original", health), health < 60 ? "err" : health < 80 ? "warn" : "ok");
                }
            }

            // ---- Dispositivos e inicio
            log("Revisando dispositivos y programas de inicio…");
            var dev = Sec("🔌 Dispositivos e inicio");
            var bad = Q(@"root\cimv2", "SELECT Name, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0 AND ConfigManagerErrorCode <> 22 AND ConfigManagerErrorCode <> 45");
            if (bad.Count == 0) Add(dev, "Dispositivos", "Todos funcionan correctamente", "ok");
            else foreach (var b in bad) Add(dev, "Dispositivo con error", P(b, "Name") + "  (código " + P(b, "ConfigManagerErrorCode") + ")", "warn");
            var su = StartupManager.List();
            int on = su.Count(x => x.Enabled);
            Add(dev, "Programas de inicio", on + " activos de " + su.Count, on > 12 ? "warn" : "ok");

            // ---- Última reparación
            var last = LastRepair();
            if (last.Value.Count > 0)
            {
                var rep = Sec("🛠 Última reparación (" + last.Key + ")");
                foreach (var r in last.Value) rep.Add(new Row(r.Key, r.Value ? "Completado" : "Revisar", r.Value ? "ok" : "warn"));
            }

            // ---- HTML
            log("Creando el informe…");
            string status = errors > 0 ? "Necesita atención" : warnings > 0 ? "Bueno, con avisos" : "Excelente";
            string statusCls = errors > 0 ? "err" : warnings > 0 ? "warn" : "ok";
            string logo = "";
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("logo.png"))
                using (var ms = new MemoryStream()) { s.CopyTo(ms); logo = Convert.ToBase64String(ms.ToArray()); }
            }
            catch { }

            var sb = new StringBuilder();
            sb.Append("<!doctype html><html lang='es'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>");
            sb.Append("<title>Informe RUSCUU - " + H(Environment.MachineName) + "</title><style>");
            sb.Append(":root{--bg:#1a0b2e;--card:#26123e;--line:#3c235a;--acc:#a052ff;--text:#f2e8d5;--muted:#aa9bbe;--ok:#6edc8c;--warn:#ffc45c;--err:#ff6e6e}");
            sb.Append("*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:15px/1.5 'Segoe UI',system-ui,sans-serif}");
            sb.Append(".wrap{max-width:980px;margin:0 auto;padding:32px 16px}header{display:flex;gap:24px;align-items:center;flex-wrap:wrap;margin-bottom:24px}");
            sb.Append("header img{width:130px;height:130px;border-radius:20px}h1{margin:0;font-size:30px}.sub{color:var(--muted)}");
            sb.Append(".status{margin-left:auto;padding:14px 22px;border-radius:14px;background:var(--card);border:2px solid var(--line);text-align:center}");
            sb.Append(".status b{display:block;font-size:20px}.status.ok{border-color:var(--ok)}.status.warn{border-color:var(--warn)}.status.err{border-color:var(--err)}");
            sb.Append("section{background:var(--card);border:1px solid var(--line);border-radius:14px;padding:18px 22px;margin:16px 0}h2{margin:0 0 10px;font-size:18px;color:var(--acc)}");
            sb.Append("table{width:100%;border-collapse:collapse}td{padding:8px 4px;border-top:1px solid var(--line);vertical-align:top}td:first-child{color:var(--muted);width:32%}");
            sb.Append(".dot{display:inline-block;width:10px;height:10px;border-radius:50%;margin-right:8px}.dot.ok{background:var(--ok)}.dot.warn{background:var(--warn)}.dot.err{background:var(--err)}");
            sb.Append("footer{color:var(--muted);text-align:center;margin-top:28px;font-size:13px}@media print{body{background:#fff;color:#000}section,.status{background:#fff}td:first-child{color:#555}}");
            sb.Append("</style></head><body><div class='wrap'><header>");
            if (logo != "") sb.Append("<img alt='RUSCUU' src='data:image/png;base64," + logo + "'>");
            sb.Append("<div><h1>Informe del equipo</h1><div class='sub'>" + (string.IsNullOrWhiteSpace(client) ? "" : "Cliente: <b>" + H(client) + "</b><br>") +
                      H(Environment.MachineName) + " • " + DateTime.Now.ToString("dd/MM/yyyy HH:mm") + "</div></div>");
            sb.Append("<div class='status " + statusCls + "'><span class='sub'>Estado general</span><b>" + status + "</b><span class='sub'>" + errors + " problemas • " + warnings + " avisos</span></div></header>");
            foreach (var s in sections)
            {
                sb.Append("<section><h2>" + H(s.Key) + "</h2><table>");
                foreach (var r in s.Value)
                    sb.Append("<tr><td>" + H(r.Label) + "</td><td>" + (r.State != "" ? "<span class='dot " + r.State + "'></span>" : "") + H(r.Value) + "</td></tr>");
                sb.Append("</table></section>");
            }
            sb.Append("<footer>Generado por <b>RUSCUU Repara</b></footer></div></body></html>");

            string dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string safe = string.IsNullOrWhiteSpace(client) ? Environment.MachineName : string.Join("_", client.Split(Path.GetInvalidFileNameChars()));
            string path = Path.Combine(dir, "Informe_RUSCUU_" + safe + "_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm") + ".html");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            log("Informe guardado en: " + path + "  (" + status + ")");
            return path;
        }

        // Lee el registro más reciente de Documentos\RUSCUU Repara
        static KeyValuePair<string, List<KeyValuePair<string, bool>>> LastRepair()
        {
            var res = new List<KeyValuePair<string, bool>>();
            string date = "";
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RUSCUU Repara");
                var f = Directory.Exists(dir) ? new DirectoryInfo(dir).GetFiles("Registro_*.txt").OrderByDescending(x => x.LastWriteTime).FirstOrDefault() : null;
                if (f != null)
                {
                    date = f.LastWriteTime.ToString("dd/MM/yyyy HH:mm");
                    string cur = null;
                    foreach (var line in File.ReadAllLines(f.FullName))
                    {
                        if (line.StartsWith("» ")) cur = line.Substring(2).Trim();
                        else if (cur != null && line.StartsWith("✔")) { res.Add(new KeyValuePair<string, bool>(cur, true)); cur = null; }
                        else if (cur != null && (line.StartsWith("⚠") || line.StartsWith("✖"))) { res.Add(new KeyValuePair<string, bool>(cur, false)); cur = null; }
                    }
                }
            }
            catch { }
            return new KeyValuePair<string, List<KeyValuePair<string, bool>>>(date, res);
        }

        public static string OsName(out string build, out string ver)
        {
            build = ""; ver = "";
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    string name = (string)k.GetValue("ProductName", "Windows");
                    build = (string)k.GetValue("CurrentBuild", "");
                    ver = (string)k.GetValue("DisplayVersion", "");
                    int b; int.TryParse(build, out b);
                    if (b >= 22000) name = name.Replace("Windows 10", "Windows 11");
                    return name;
                }
            }
            catch { return "Windows"; }
        }
    }
}
