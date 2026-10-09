// RUSCUU Repara - programas que se abren al encender el PC
// Usa las mismas claves "StartupApproved" que el Administrador de tareas, así que
// activar/desactivar es reversible y se refleja también allí.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace Ruscuu
{
    class StartupItem
    {
        public string Name, Command, Source;
        public RegistryHive ApprovedHive;
        public string ApprovedKey, ValueName;
        public bool Enabled;
    }

    static class StartupManager
    {
        const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunWow = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
        const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

        public static List<StartupItem> List()
        {
            var list = new List<StartupItem>();
            FromRegistry(list, RegistryHive.CurrentUser, Run, "Run", "Usuario");
            FromRegistry(list, RegistryHive.LocalMachine, Run, "Run", "Todos los usuarios");
            FromRegistry(list, RegistryHive.LocalMachine, RunWow, "Run32", "Todos (32 bits)");
            FromFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), RegistryHive.CurrentUser, "Carpeta Inicio");
            FromFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), RegistryHive.LocalMachine, "Carpeta Inicio común");
            return list.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        static RegistryKey Base(RegistryHive hive) { return RegistryKey.OpenBaseKey(hive, RegistryView.Registry64); }

        static void FromRegistry(List<StartupItem> list, RegistryHive hive, string path, string approvedKey, string source)
        {
            try
            {
                using (var k = Base(hive).OpenSubKey(path))
                {
                    if (k == null) return;
                    foreach (var name in k.GetValueNames())
                    {
                        if (string.IsNullOrEmpty(name)) continue;
                        string cmd = Convert.ToString(k.GetValue(name));
                        if (!LooksLikeProgram(cmd)) continue;
                        var it = new StartupItem { Name = name, Command = cmd, Source = source, ApprovedHive = hive, ApprovedKey = approvedKey, ValueName = name };
                        it.Enabled = IsEnabled(it);
                        list.Add(it);
                    }
                }
            }
            catch { }
        }

        static void FromFolder(List<StartupItem> list, string dir, RegistryHive hive, string source)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                foreach (var f in Directory.GetFiles(dir))
                {
                    string fn = Path.GetFileName(f);
                    if (fn.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    var it = new StartupItem { Name = Path.GetFileNameWithoutExtension(f), Command = f, Source = source, ApprovedHive = hive, ApprovedKey = "StartupFolder", ValueName = fn };
                    it.Enabled = IsEnabled(it);
                    list.Add(it);
                }
            }
            catch { }
        }

        // descarta valores que no son comandos (contadores, identificadores…) que algunos programas guardan en Run
        static bool LooksLikeProgram(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return false;
            string c = cmd.ToLowerInvariant();
            return c.Contains("\\") || c.Contains(".exe") || c.Contains("%") || c.Contains("rundll32");
        }

        static bool IsEnabled(StartupItem it)
        {
            try
            {
                using (var k = Base(it.ApprovedHive).OpenSubKey(Approved + it.ApprovedKey))
                {
                    if (k == null) return true;
                    var v = k.GetValue(it.ValueName) as byte[];
                    // byte 0 par (02/06) = activo, impar (03/07) = desactivado
                    return v == null || v.Length == 0 || (v[0] & 1) == 0;
                }
            }
            catch { return true; }
        }

        public static void SetEnabled(StartupItem it, bool enabled)
        {
            var data = new byte[12];
            data[0] = (byte)(enabled ? 2 : 3);
            if (!enabled) Array.Copy(BitConverter.GetBytes(DateTime.Now.ToFileTime()), 0, data, 4, 8);
            using (var k = Base(it.ApprovedHive).CreateSubKey(Approved + it.ApprovedKey))
                k.SetValue(it.ValueName, data, RegistryValueKind.Binary);
            it.Enabled = enabled;
        }
    }
}
