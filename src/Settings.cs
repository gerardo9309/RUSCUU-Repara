// RUSCUU Repara - configuración persistente (config.ini)
// Si el programa se ejecuta desde una USB, los datos (config, historial) viajan en la propia USB.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Ruscuu
{
    static class AppInfo
    {
        public const string Version = "3.0.0";
        public const string Name = "RUSCUU Repara";
    }

    static class Settings
    {
        static Dictionary<string, string> data;

        public static bool Portable
        {
            get
            {
                try { return new DriveInfo(Path.GetPathRoot(Application.ExecutablePath)).DriveType == DriveType.Removable; }
                catch { return false; }
            }
        }

        public static string DataDir
        {
            get
            {
                return Portable ? Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "RUSCUU Datos")
                                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RUSCUU Repara");
            }
        }

        public static string HistoryDir { get { return Path.Combine(DataDir, "Historial"); } }
        static string FilePath { get { return Path.Combine(DataDir, "config.ini"); } }

        static void Load()
        {
            if (data != null) return;
            data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(FilePath))
                    foreach (var l in File.ReadAllLines(FilePath, Encoding.UTF8))
                    {
                        int i = l.IndexOf('=');
                        if (i > 0) data[l.Substring(0, i).Trim()] = l.Substring(i + 1).Trim();
                    }
            }
            catch { }
        }

        public static string Get(string key, string def = "")
        {
            Load();
            string v;
            return data.TryGetValue(key, out v) ? v : def;
        }

        public static void Set(string key, string value)
        {
            Load();
            data[key] = value ?? "";
            try
            {
                Directory.CreateDirectory(DataDir);
                File.WriteAllLines(FilePath, data.Select(kv => kv.Key + "=" + kv.Value).ToArray(), Encoding.UTF8);
            }
            catch { }
        }

        public static bool GetBool(string key, bool def) { string v = Get(key, def ? "1" : "0"); return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase); }
        public static void SetBool(string key, bool v) { Set(key, v ? "1" : "0"); }

        // ---- apariencia
        public static string ThemeMode { get { return Get("tema", "oscuro"); } }   // oscuro | claro | auto
        public static bool Dark
        {
            get
            {
                string m = ThemeMode;
                if (m == "claro") return false;
                if (m == "auto")
                    try
                    {
                        using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                        {
                            var v = k == null ? null : k.GetValue("AppsUseLightTheme");
                            if (v is int && (int)v == 1) return false;
                        }
                    }
                    catch { }
                return true;
            }
        }
        public static int Accent { get { int a; return int.TryParse(Get("acento", "0"), out a) ? a : 0; } }
        public static bool Sounds { get { return GetBool("sonidos", true); } }

        // ---- modo cliente / técnico
        public static bool ClientMode { get { return GetBool("modo_cliente", false); } }
        public static bool HasPin { get { return Get("pin_hash") != ""; } }
        public static void SetPin(string pin)
        {
            string salt = Guid.NewGuid().ToString("N");
            Set("pin_salt", salt);
            Set("pin_hash", Hash(salt + pin));
        }
        public static bool CheckPin(string pin) { return HasPin && Hash(Get("pin_salt") + pin) == Get("pin_hash"); }
        static string Hash(string s)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-", "");
        }

        // ---- actualizaciones
        public static string UpdateUrl { get { return Get("update_url", "https://raw.githubusercontent.com/gerardo9309/RUSCUU-Repara/main/version.txt"); } }
    }
}
