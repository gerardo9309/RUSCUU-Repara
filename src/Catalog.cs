// RUSCUU Repara - catálogo de tareas de reparación
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Ruscuu
{
    delegate int InternalAction(Action<string> log);
    delegate int ProgressAction(Action<string> log, Action<double> progress);

    class Step
    {
        public string File, Args;
        public Encoding Enc;
        public InternalAction Internal;
        public ProgressAction InternalP;
        public int[] OkCodes;   // códigos de salida que también cuentan como éxito
    }

    class RepairTask
    {
        public string Id, Category, Title, Desc, Time, Caution, Group;
        public bool Reboot, Selected, RunLast;
        public List<Step> Steps = new List<Step>();
    }

    static class Native
    {
        [DllImport("kernel32.dll")] public static extern int GetOEMCP();
        [DllImport("kernel32.dll")] public static extern ulong GetTickCount64();
        [StructLayout(LayoutKind.Sequential)]
        public class MEMSTATUS
        {
            public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMSTATUS));
            public uint dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }
        [DllImport("kernel32.dll")] public static extern bool GlobalMemoryStatusEx([In, Out] MEMSTATUS m);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] public static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);
    }

    static partial class Catalog
    {
        public const string Sys = "Sistema", Clean = "Limpieza", Net = "Red e Internet", WU = "Windows Update", Sec = "Seguridad",
            Perf = "Rendimiento", Fix = "Arreglos rápidos", Diag = "Diagnóstico", Apps = "Apps basura",
            Priv = "Privacidad", Inst = "Instalar programas", GamerCat = "Gamer";

        // Reparaciones de un clic (Inicio) y del mantenimiento programado
        public static readonly Dictionary<string, string[]> Presets = new Dictionary<string, string[]> {
            { "rapida", new[] { "restore", "dism", "sfc", "tempuser", "tempwin", "dns" } },
            { "completa", new[] { "restore", "dism", "sfc", "chkscan", "compclean", "tempuser", "tempwin", "wucache", "thumbs", "dns", "winsock", "wureset", "defupdate", "defquick" } },
            { "limpieza", new[] { "tempuser", "tempwin", "wucache", "recycle", "compclean" } },
            { "internet", new[] { "dns", "renew", "proxy", "winsock", "tcpip" } },
            // versiones sin interacción ni reinicios para el mantenimiento automático
            { "auto-rapida", new[] { "restore", "dism", "sfc", "tempuser", "tempwin", "dns" } },
            { "auto-limpieza", new[] { "tempuser", "tempwin", "wucache", "compclean" } },
            { "auto-completa", new[] { "restore", "dism", "sfc", "chkscan", "compclean", "tempuser", "tempwin", "wucache", "dns", "defupdate", "defquick" } },
        };

        static Encoding Oem { get { try { return Encoding.GetEncoding(Native.GetOEMCP()); } catch { return Encoding.Default; } } }

        public static Step Cmd(string file, string args) { return new Step { File = file, Args = args, Enc = Oem }; }
        public static Step PS(string script)
        {
            return new Step { File = "powershell.exe", Args = "-NoProfile -ExecutionPolicy Bypass -Command \"" + script + "\"", Enc = Oem };
        }
        public static Step Int(InternalAction a) { return new Step { Internal = a }; }
        public static Step IntP(ProgressAction a) { return new Step { InternalP = a }; }

        public static RepairTask T(string id, string cat, string title, string desc, string time, bool reboot, params Step[] steps)
        {
            var t = new RepairTask { Id = id, Category = cat, Title = title, Desc = desc, Time = time, Reboot = reboot };
            t.Steps.AddRange(steps);
            return t;
        }

        static string WinDir { get { return Environment.GetFolderPath(Environment.SpecialFolder.Windows); } }
        static string MpCmd { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Windows Defender\MpCmdRun.exe"); } }

        public static List<RepairTask> Build()
        {
            string sys = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
            var list = new List<RepairTask>();

            // ---- Sistema
            list.Add(T("restore", Sys, "Crear punto de restauración",
                "Guarda una copia del estado actual de Windows para poder volver atrás si algo sale mal. Recomendado siempre.",
                "~1 min", false,
                PS(@"$k='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore'; New-ItemProperty -Path $k -Name SystemRestorePointCreationFrequency -Value 0 -PropertyType DWord -Force | Out-Null; try { Enable-ComputerRestore -Drive ($env:SystemDrive+'\'); Checkpoint-Computer -Description 'RUSCUU Repara' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop; 'Punto de restauracion creado correctamente.' } catch { 'No se pudo crear: ' + $_.Exception.Message; exit 1 } finally { Remove-ItemProperty -Path $k -Name SystemRestorePointCreationFrequency -ErrorAction SilentlyContinue }")));
            list.Add(T("dism", Sys, "Reparar imagen de Windows (DISM)",
                "Descarga y repara los archivos base de Windows desde Windows Update. Ejecutar antes del SFC.",
                "10-20 min", false,
                Cmd("DISM.exe", "/Online /Cleanup-Image /RestoreHealth")));
            list.Add(T("sfc", Sys, "Reparar archivos del sistema (SFC)",
                "Revisa todos los archivos protegidos de Windows y reemplaza los dañados.",
                "5-15 min", false,
                new Step { File = "sfc.exe", Args = "/scannow", Enc = Encoding.Unicode }));
            list.Add(T("chkscan", Sys, "Revisar disco " + sys + " (sin reiniciar)",
                "Busca errores en el sistema de archivos del disco principal en línea.",
                "2-10 min", false,
                Cmd("chkdsk.exe", sys + " /scan")));
            list.Add(T("chkboot", Sys, "Reparar disco " + sys + " al reiniciar",
                "Programa una reparación profunda del disco en el próximo arranque (CHKDSK /F).",
                "al reiniciar", true,
                Cmd("fsutil.exe", "dirty set " + sys)));
            list.Add(T("compclean", Sys, "Limpiar componentes de Windows",
                "Elimina versiones antiguas de actualizaciones guardadas en WinSxS. Libera varios GB.",
                "5-15 min", false,
                Cmd("DISM.exe", "/Online /Cleanup-Image /StartComponentCleanup")));

            // ---- Limpieza
            list.Add(T("tempuser", Clean, "Temporales del usuario",
                "Borra la carpeta %TEMP% del usuario actual. Los archivos en uso se omiten.",
                "<1 min", false,
                Int(log => CleanFolder(Path.GetTempPath(), log))));
            list.Add(T("tempwin", Clean, "Temporales de Windows",
                "Borra la carpeta C:\\Windows\\Temp. Los archivos en uso se omiten.",
                "<1 min", false,
                Int(log => CleanFolder(Path.Combine(WinDir, "Temp"), log))));
            list.Add(T("wucache", Clean, "Caché de descargas de Windows Update",
                "Elimina instaladores de actualizaciones ya descargados. Windows los vuelve a bajar si los necesita.",
                "1-2 min", false,
                PS("Stop-Service wuauserv,bits -Force -ErrorAction SilentlyContinue"),
                Int(log => CleanFolder(Path.Combine(WinDir, @"SoftwareDistribution\Download"), log)),
                PS("Start-Service wuauserv,bits -ErrorAction SilentlyContinue; 'Servicios de Windows Update iniciados.'")));
            list.Add(T("thumbs", Clean, "Caché de iconos y miniaturas",
                "Arregla iconos en blanco o miniaturas incorrectas. El escritorio se reinicia unos segundos.",
                "<1 min", false,
                Int(RebuildIconCache)));
            list.Add(T("recycle", Clean, "Vaciar la papelera de reciclaje",
                "Elimina definitivamente los archivos de la papelera de todas las unidades.",
                "<1 min", false,
                PS("Clear-RecycleBin -Force -ErrorAction SilentlyContinue; 'Papelera vaciada.'")));

            // ---- Red
            list.Add(T("dns", Net, "Vaciar caché DNS",
                "Soluciona páginas que no cargan o cargan una versión vieja.",
                "segundos", false,
                Cmd("ipconfig.exe", "/flushdns")));
            list.Add(T("renew", Net, "Renovar dirección IP",
                "Pide una nueva IP al router. Internet se corta unos segundos.",
                "<1 min", false,
                Cmd("ipconfig.exe", "/release"), Cmd("ipconfig.exe", "/renew")));
            list.Add(T("proxy", Net, "Quitar proxy del sistema",
                "Restablece la configuración de proxy de WinHTTP (útil tras malware o VPNs).",
                "segundos", false,
                Cmd("netsh.exe", "winhttp reset proxy")));
            list.Add(T("winsock", Net, "Restablecer Winsock",
                "Repara la pila de red de Windows cuando hay Internet pero nada conecta.",
                "segundos", true,
                Cmd("netsh.exe", "winsock reset")));
            list.Add(T("tcpip", Net, "Restablecer TCP/IP",
                "Devuelve la configuración TCP/IP a valores de fábrica.",
                "segundos", true,
                Cmd("netsh.exe", "int ip reset")));

            // ---- Windows Update
            list.Add(T("wureset", WU, "Reiniciar componentes de Windows Update",
                "Para cuando las actualizaciones fallan o se quedan atascadas. Renombra SoftwareDistribution y catroot2.",
                "1-3 min", true,
                PS(@"Stop-Service wuauserv,bits,cryptsvc,msiserver -Force -ErrorAction SilentlyContinue; Remove-Item ($env:windir+'\SoftwareDistribution.old'),($env:windir+'\System32\catroot2.old') -Recurse -Force -ErrorAction SilentlyContinue; Rename-Item ($env:windir+'\SoftwareDistribution') 'SoftwareDistribution.old' -ErrorAction SilentlyContinue; Rename-Item ($env:windir+'\System32\catroot2') 'catroot2.old' -ErrorAction SilentlyContinue; Start-Service wuauserv,bits,cryptsvc -ErrorAction SilentlyContinue; 'Componentes de Windows Update reiniciados.'")));
            list.Add(T("store", WU, "Reparar Microsoft Store",
                "Limpia la caché de la tienda (wsreset). Se abrirá la Store al terminar.",
                "<1 min", false,
                Int(log => { Process.Start("wsreset.exe"); log("Microsoft Store reiniciada (se abrirá en unos segundos)."); return 0; })));
            list.Add(T("wuopen", WU, "Abrir Windows Update",
                "Abre la configuración para buscar actualizaciones pendientes.",
                "segundos", false,
                Int(log => { Process.Start("ms-settings:windowsupdate"); log("Windows Update abierto."); return 0; })));

            // ---- Seguridad (Microsoft Defender)
            list.Add(T("defstatus", Sec, "Estado de la protección",
                "Muestra si el antivirus y la protección en tiempo real están activos y la antigüedad de las firmas.",
                "segundos", false,
                PS("try { $s = Get-MpComputerStatus -ErrorAction Stop; 'Antivirus activo: ' + $s.AntivirusEnabled; 'Proteccion en tiempo real: ' + $s.RealTimeProtectionEnabled; 'Firmas actualizadas hace: ' + $s.AntivirusSignatureAge + ' dias (version ' + $s.AntivirusSignatureVersion + ')'; 'Ultimo analisis rapido hace: ' + $s.QuickScanAge + ' dias' } catch { 'Defender no esta disponible (posiblemente hay otro antivirus instalado).' }; Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntiVirusProduct -ErrorAction SilentlyContinue | Select-Object -ExpandProperty displayName -Unique | ForEach-Object { 'Antivirus registrado: ' + $_ }")));
            list.Add(T("defupdate", Sec, "Actualizar firmas de Defender",
                "Descarga las definiciones de virus más recientes antes de analizar.",
                "1-3 min", false,
                Cmd(MpCmd, "-SignatureUpdate")));
            list.Add(T("defquick", Sec, "Análisis rápido de virus",
                "Revisa las zonas donde suele esconderse el malware (memoria, inicio, carpetas del sistema).",
                "3-10 min", false,
                Cmd(MpCmd, "-Scan -ScanType 1")));
            list.Add(T("deffull", Sec, "Análisis completo de virus",
                "Revisa todos los archivos de todos los discos. Puedes seguir usando el equipo.",
                "30-120 min", false,
                Cmd(MpCmd, "-Scan -ScanType 2")));
            list.Add(T("defthreats", Sec, "Ver amenazas detectadas",
                "Lista el historial de amenazas encontradas y qué hizo Defender con ellas.",
                "segundos", false,
                PS("$t = Get-MpThreatDetection -ErrorAction SilentlyContinue; if ($t) { $t | ForEach-Object { $_.InitialDetectionTime.ToString('dd/MM/yyyy HH:mm') + '  ' + (Get-MpThreat -ThreatID $_.ThreatID -ErrorAction SilentlyContinue).ThreatName + '  -> ' + $_.Resources[0] } } else { 'No hay amenazas registradas.' }")));

            // ---- Rendimiento
            list.Add(T("pwrhigh", Perf, "Plan de energía: Alto rendimiento",
                "El procesador trabaja siempre a máxima velocidad. Ideal para PC de escritorio (en portátiles gasta más batería).",
                "segundos", false,
                PS("$g='8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c'; powercfg /setactive $g 2>$null; if ($LASTEXITCODE -ne 0) { $o = powercfg -duplicatescheme $g; $n = [regex]::Match(($o -join ' '),'[0-9a-fA-F]{8}-[0-9a-fA-F-]{27}').Value; if ($n) { powercfg /setactive $n } else { 'No disponible en este equipo'; exit 1 } }; 'Plan Alto rendimiento activado.'")));
            list.Add(T("pwrultimate", Perf, "Plan de energía: Máximo rendimiento",
                "Plan oculto de Windows para equipos potentes: elimina micro-pausas de ahorro de energía.",
                "segundos", false,
                PS("$e = powercfg /list | Select-String 'Ultimate|ximo rendimiento' | Select-Object -First 1; if ($e) { $n = [regex]::Match($e.ToString(),'[0-9a-fA-F]{8}-[0-9a-fA-F-]{27}').Value } else { $o = powercfg -duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61; $n = [regex]::Match(($o -join ' '),'[0-9a-fA-F]{8}-[0-9a-fA-F-]{27}').Value }; if ($n) { powercfg /setactive $n; 'Plan Maximo rendimiento activado.' } else { 'No disponible en este equipo'; exit 1 }")));
            list.Add(T("pwrbalanced", Perf, "Plan de energía: Equilibrado",
                "Vuelve al plan recomendado por Windows (ahorra energía cuando no se necesita potencia).",
                "segundos", false,
                Cmd("powercfg.exe", "/setactive 381b4222-f694-41f0-9685-ff5bb260df2e")));
            list.Add(T("vfxperf", Perf, "Efectos visuales: máximo rendimiento",
                "Desactiva animaciones, sombras y transparencias. Notable en equipos lentos. Aplica al cerrar sesión.",
                "segundos", false,
                PS(@"Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects' -Name VisualFXSetting -Value 2 -Type DWord; Set-ItemProperty 'HKCU:\Control Panel\Desktop' -Name UserPreferencesMask -Value ([byte[]](0x90,0x12,0x03,0x80,0x10,0x00,0x00,0x00)) -Type Binary; Set-ItemProperty 'HKCU:\Control Panel\Desktop\WindowMetrics' -Name MinAnimate -Value '0'; Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' -Name EnableTransparency -Value 0 -Type DWord; 'Efectos visuales en modo rendimiento. Cierra sesion para aplicarlos.'")));
            list.Add(T("vfxdefault", Perf, "Efectos visuales: restaurar",
                "Devuelve las animaciones y transparencias por defecto de Windows. Aplica al cerrar sesión.",
                "segundos", false,
                PS(@"Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects' -Name VisualFXSetting -Value 0 -Type DWord; Set-ItemProperty 'HKCU:\Control Panel\Desktop' -Name UserPreferencesMask -Value ([byte[]](0x9E,0x1E,0x07,0x80,0x12,0x00,0x00,0x00)) -Type Binary; Set-ItemProperty 'HKCU:\Control Panel\Desktop\WindowMetrics' -Name MinAnimate -Value '1'; Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' -Name EnableTransparency -Value 1 -Type DWord; 'Efectos visuales restaurados. Cierra sesion para aplicarlos.'")));
            list.Add(T("gamemode", Perf, "Activar Modo juego",
                "Windows prioriza el juego abierto y pausa tareas en segundo plano.",
                "segundos", false,
                PS(@"New-Item 'HKCU:\Software\Microsoft\GameBar' -Force -ErrorAction SilentlyContinue | Out-Null; Set-ItemProperty 'HKCU:\Software\Microsoft\GameBar' -Name AutoGameModeEnabled -Value 1 -Type DWord; Set-ItemProperty 'HKCU:\Software\Microsoft\GameBar' -Name AllowAutoGameMode -Value 1 -Type DWord; 'Modo juego activado.'")));
            list.Add(T("gamedvr", Perf, "Desactivar grabación en segundo plano",
                "Apaga la grabación automática de Xbox Game Bar, que consume FPS en los juegos.",
                "segundos", false,
                PS(@"New-Item 'HKCU:\System\GameConfigStore' -Force -ErrorAction SilentlyContinue | Out-Null; Set-ItemProperty 'HKCU:\System\GameConfigStore' -Name GameDVR_Enabled -Value 0 -Type DWord; New-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR' -Force -ErrorAction SilentlyContinue | Out-Null; Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR' -Name AppCaptureEnabled -Value 0 -Type DWord; 'Grabacion en segundo plano desactivada.'")));
            list.Add(T("hags", Perf, "Programación de GPU acelerada",
                "Deja que la tarjeta gráfica gestione su memoria: menos latencia en juegos (GPU y drivers modernos).",
                "segundos", true,
                PS(@"Set-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers' -Name HwSchMode -Value 2 -Type DWord; 'Programacion de GPU acelerada activada (requiere reiniciar).'")));

            // ---- Arreglos rápidos
            list.Add(T("fixaudio", Fix, "Arreglar el sonido",
                "Reinicia los servicios de audio de Windows. Soluciona «no se oye nada» o el icono de sonido con X.",
                "segundos", false,
                PS("Restart-Service AudioEndpointBuilder -Force -ErrorAction SilentlyContinue; Start-Service Audiosrv -ErrorAction SilentlyContinue; 'Servicios de audio: ' + (Get-Service Audiosrv).Status")));
            list.Add(T("fixprint", Fix, "Arreglar la impresora",
                "Vacía la cola de impresión atascada y reinicia el servicio de impresión.",
                "segundos", false,
                PS(@"Stop-Service Spooler -Force -ErrorAction SilentlyContinue; Remove-Item ($env:windir+'\System32\spool\PRINTERS\*') -Force -Recurse -ErrorAction SilentlyContinue; Start-Service Spooler; 'Cola de impresion vaciada. Servicio: ' + (Get-Service Spooler).Status")));
            list.Add(T("fixexplorer", Fix, "Reiniciar barra de tareas y escritorio",
                "Reinicia el Explorador de Windows cuando la barra de tareas o las ventanas se congelan.",
                "segundos", false,
                Int(log => { RestartExplorer(log); return 0; })));
            list.Add(T("fixstart", Fix, "Arreglar menú Inicio y búsqueda",
                "Reinicia el menú Inicio y el buscador cuando no abren o no escriben.",
                "segundos", false,
                PS("Stop-Process -Name StartMenuExperienceHost,SearchHost,SearchApp -Force -ErrorAction SilentlyContinue; Restart-Service WSearch -Force -ErrorAction SilentlyContinue; 'Menu Inicio y busqueda reiniciados.'")));
            list.Add(T("fixtime", Fix, "Sincronizar la hora",
                "Corrige la fecha y hora con el servidor de Internet (arregla errores de certificados en webs).",
                "segundos", false,
                PS("Set-Service w32time -StartupType Manual -ErrorAction SilentlyContinue; Start-Service w32time -ErrorAction SilentlyContinue; w32tm /resync /force")));
            list.Add(T("fixbt", Fix, "Arreglar Bluetooth",
                "Reinicia el servicio de Bluetooth cuando los dispositivos no aparecen o no conectan.",
                "segundos", false,
                PS("Restart-Service bthserv -Force -ErrorAction SilentlyContinue; 'Servicio Bluetooth: ' + (Get-Service bthserv -ErrorAction SilentlyContinue).Status")));
            list.Add(T("fixclip", Fix, "Arreglar copiar y pegar",
                "Reinicia el servicio del portapapeles cuando Ctrl+C / Ctrl+V dejan de funcionar.",
                "segundos", false,
                PS("Get-Service cbdhsvc_* -ErrorAction SilentlyContinue | Restart-Service -Force -ErrorAction SilentlyContinue; Stop-Process -Name rdpclip -Force -ErrorAction SilentlyContinue; 'Portapapeles reiniciado.'")));

            // ---- Diagnóstico
            list.Add(T("report", Diag, "Informe del PC (HTML con tu logo)",
                "Genera un informe completo del equipo: hardware, discos, seguridad, batería y última reparación.",
                "~30 s", false,
                Int(log => { string p = Report.Generate(MainForm.ClientName, log); if (p != null) Process.Start(p); return p != null ? 0 : 1; })));
            list.Add(T("battery", Diag, "Informe de batería (portátiles)",
                "Genera un informe HTML con la salud y desgaste de la batería y lo guarda en el escritorio.",
                "segundos", false,
                Int(BatteryReport)));
            list.Add(T("memtest", Diag, "Prueba de memoria RAM",
                "Abre el diagnóstico de memoria de Windows (requiere reiniciar para hacer la prueba).",
                "al reiniciar", false,
                Int(log => { Process.Start("mdsched.exe"); log("Diagnóstico de memoria abierto: elige cuándo reiniciar."); return 0; })));
            list.Add(T("drivers", Diag, "Dispositivos con problemas",
                "Lista los dispositivos y controladores que Windows marca con error.",
                "segundos", false,
                PS("$d = Get-PnpDevice -PresentOnly | Where-Object { $_.Status -ne 'OK' -and $_.Status -ne 'Unknown' }; if ($d) { $d | ForEach-Object { '[' + $_.Status + '] ' + $_.FriendlyName + ' (' + $_.Class + ')' } } else { 'Todos los dispositivos funcionan correctamente.' }")));
            list.Add(T("smart", Diag, "Salud de los discos",
                "Muestra el estado de salud (SMART) reportado por cada disco físico.",
                "segundos", false,
                PS("Get-PhysicalDisk | ForEach-Object { $_.FriendlyName + '  |  ' + $_.MediaType + '  |  ' + [math]::Round($_.Size/1GB) + ' GB  |  Salud: ' + $_.HealthStatus }")));

            AddExtras(list);

            // Análisis sin conexión: reinicia el equipo de inmediato, por eso siempre va al final
            var off = T("defoffline", Sec, "Análisis sin conexión (antes de Windows)",
                "Reinicia y analiza antes de que arranque Windows: elimina virus que se esconden. El equipo se reinicia AL INSTANTE.",
                "~15 min", true,
                PS("Start-MpWDOScan; 'Reiniciando para el analisis sin conexion…'"));
            off.RunLast = true; off.Caution = "Reinicia al momento";
            list.Add(off);
            return list;
        }

        // ---------------------------------------------------------------- Apps basura
        class Bloat { public string Pattern, Name, Desc, Caution; }

        static readonly Bloat[] BloatList = {
            new Bloat { Pattern = "Microsoft.BingNews", Name = "Noticias (Microsoft News)", Desc = "App de noticias con publicidad." },
            new Bloat { Pattern = "Microsoft.BingWeather", Name = "El Tiempo (MSN)", Desc = "App del clima de MSN." },
            new Bloat { Pattern = "Microsoft.BingSearch", Name = "Búsqueda de Bing", Desc = "Integración de Bing en la búsqueda." },
            new Bloat { Pattern = "Microsoft.GetHelp", Name = "Obtener ayuda", Desc = "Asistente de soporte de Microsoft." },
            new Bloat { Pattern = "Microsoft.Getstarted", Name = "Consejos", Desc = "Tutoriales de bienvenida de Windows." },
            new Bloat { Pattern = "Microsoft.MicrosoftSolitaireCollection", Name = "Solitaire Collection", Desc = "Juegos de cartas con anuncios." },
            new Bloat { Pattern = "Microsoft.People", Name = "Contactos", Desc = "Agenda de contactos antigua." },
            new Bloat { Pattern = "Microsoft.ZuneVideo", Name = "Películas y TV", Desc = "Reproductor de vídeo antiguo." },
            new Bloat { Pattern = "Microsoft.ZuneMusic", Name = "Reproductor multimedia", Desc = "Reproductor de música y vídeo de Windows.", Caution = "Abre MP3/MP4" },
            new Bloat { Pattern = "Microsoft.WindowsFeedbackHub", Name = "Centro de opiniones", Desc = "Envío de comentarios a Microsoft." },
            new Bloat { Pattern = "Clipchamp.Clipchamp", Name = "Clipchamp", Desc = "Editor de vídeo en línea." },
            new Bloat { Pattern = "Microsoft.Todos", Name = "Microsoft To Do", Desc = "Lista de tareas en la nube." },
            new Bloat { Pattern = "Microsoft.MicrosoftOfficeHub", Name = "Microsoft 365 (Office)", Desc = "Lanzador/publicidad de Office. No borra Office instalado." },
            new Bloat { Pattern = "Microsoft.SkypeApp", Name = "Skype", Desc = "Skype preinstalado (servicio cerrado)." },
            new Bloat { Pattern = "Microsoft.WindowsMaps", Name = "Mapas", Desc = "Mapas sin conexión de Windows." },
            new Bloat { Pattern = "Microsoft.Microsoft3DViewer", Name = "Visor 3D", Desc = "Visor de modelos 3D." },
            new Bloat { Pattern = "Microsoft.MixedReality.Portal", Name = "Portal de realidad mixta", Desc = "Solo útil con gafas de realidad mixta." },
            new Bloat { Pattern = "MicrosoftTeams", Name = "Teams (personal)", Desc = "Teams de consumo preinstalado." },
            new Bloat { Pattern = "MSTeams", Name = "Teams", Desc = "Microsoft Teams.", Caution = "Trabajo o clases" },
            new Bloat { Pattern = "Microsoft.549981C3F5F10", Name = "Cortana", Desc = "Asistente de voz (ya retirado)." },
            new Bloat { Pattern = "Microsoft.PowerAutomateDesktop", Name = "Power Automate", Desc = "Automatización para empresas." },
            new Bloat { Pattern = "Microsoft.OutlookForWindows", Name = "Outlook (nuevo)", Desc = "Nuevo cliente de correo de Microsoft.", Caution = "Si lo usa como correo" },
            new Bloat { Pattern = "microsoft.windowscommunicationsapps", Name = "Correo y Calendario", Desc = "App de correo antigua (retirada)." },
            new Bloat { Pattern = "Microsoft.Windows.DevHome", Name = "Dev Home", Desc = "Panel para programadores." },
            new Bloat { Pattern = "Microsoft.Copilot", Name = "Copilot", Desc = "Asistente de IA de Microsoft." },
            new Bloat { Pattern = "Microsoft.YourPhone", Name = "Enlace Móvil", Desc = "Conecta el teléfono al PC.", Caution = "Si conecta su móvil" },
            new Bloat { Pattern = "Microsoft.GamingApp", Name = "Xbox", Desc = "App de Xbox y Game Pass.", Caution = "Necesaria para Game Pass" },
            new Bloat { Pattern = "Microsoft.XboxGamingOverlay", Name = "Xbox Game Bar", Desc = "Barra de juego (Win+G).", Caution = "Capturas en juegos" },
            new Bloat { Pattern = "SpotifyAB.SpotifyMusic", Name = "Spotify", Desc = "Spotify preinstalado." },
            new Bloat { Pattern = "Disney.*", Name = "Disney+", Desc = "Disney+ preinstalado." },
            new Bloat { Pattern = "king.com.*", Name = "Juegos de King (Candy Crush…)", Desc = "Juegos preinstalados con compras." },
            new Bloat { Pattern = "*TikTok*", Name = "TikTok", Desc = "TikTok preinstalado." },
            new Bloat { Pattern = "Facebook.*", Name = "Facebook / Instagram", Desc = "Apps de Meta preinstaladas." },
        };

        // Devuelve las tareas para las apps basura que estén instaladas
        public static List<RepairTask> BloatTasks(IEnumerable<string> installed)
        {
            var names = installed.ToList();
            var result = new List<RepairTask>();
            foreach (var b in BloatList)
            {
                var rx = new Regex("^" + Regex.Escape(b.Pattern).Replace(@"\*", ".*") + "$", RegexOptions.IgnoreCase);
                var hits = names.Where(n => rx.IsMatch(n)).Distinct().ToList();
                if (hits.Count == 0) continue;
                string filter = string.Join(",", hits.Select(h => "'" + h.Replace("'", "") + "'").ToArray());
                var t = T("app:" + b.Pattern, Apps, "Quitar " + b.Name, b.Desc + " Se puede reinstalar desde Microsoft Store.", "segundos", false,
                    PS("$fail = 0; foreach ($n in @(" + filter + ")) { Get-AppxPackage -AllUsers -Name $n | Remove-AppxPackage -AllUsers -ErrorAction SilentlyContinue; Get-AppxProvisionedPackage -Online | Where-Object { $_.DisplayName -eq $n } | Remove-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue | Out-Null; if (Get-AppxPackage -AllUsers -Name $n) { 'No se pudo quitar ' + $n; $fail = 1 } else { 'Quitado: ' + $n } }; exit $fail"));
                t.Caution = b.Caution;
                result.Add(t);
            }
            return result;
        }

        // ---------------------------------------------------------------- Drivers y USB
        public static RepairTask DriverBackupTask(string dest)
        {
            return T("drvbackup", Diag, "Respaldar drivers", "", "", false,
                Int(log => { Directory.CreateDirectory(dest); log("Carpeta: " + dest); return 0; }),
                Cmd("DISM.exe", "/Online /Export-Driver /Destination:\"" + dest + "\""),
                Int(log =>
                {
                    int n = Directory.Exists(dest) ? Directory.GetFiles(dest, "*.inf", SearchOption.AllDirectories).Length : 0;
                    log(n + " drivers guardados. Para reinstalarlos usa «Restaurar drivers» en Herramientas.");
                    Process.Start("explorer.exe", "\"" + dest + "\"");
                    return n > 0 ? 0 : 1;
                }));
        }

        public static RepairTask DriverRestoreTask(string folder)
        {
            return T("drvrestore", Diag, "Restaurar drivers", "", "", true,
                Cmd("pnputil.exe", "/add-driver \"" + Path.Combine(folder, "*.inf") + "\" /subdirs /install"));
        }

        static readonly string[][] Links = {
            new[] { "Ventoy - USB multiarranque", "https://www.ventoy.net/" },
            new[] { "Rufus - crear USB de Windows", "https://rufus.ie/" },
            new[] { "Windows 11 - descarga oficial", "https://www.microsoft.com/software-download/windows11" },
            new[] { "CrystalDiskInfo - salud de discos", "https://crystalmark.info/en/software/crystaldiskinfo/" },
            new[] { "HWiNFO - sensores y hardware", "https://www.hwinfo.com/" },
            new[] { "Malwarebytes - antimalware", "https://www.malwarebytes.com/" },
            new[] { "7-Zip - compresor", "https://www.7-zip.org/" },
            new[] { "Snappy Driver Installer Origin", "https://www.glenn.delahoy.com/snappy-driver-installer-origin/" },
            new[] { "Autoruns - programas de inicio (Sysinternals)", "https://learn.microsoft.com/sysinternals/downloads/autoruns" },
            new[] { "BlueScreenView - pantallazos azules", "https://www.nirsoft.net/utils/blue_screen_view.html" },
            new[] { "MemTest86 - prueba de RAM", "https://www.memtest86.com/" },
            new[] { "DDU - desinstalar drivers de video", "https://www.wagnardsoft.com/" },
        };

        public static RepairTask UsbTask(string drive, string toolsFolder, string exePath)
        {
            string root = Path.Combine(drive, "RUSCUU");
            return T("usb", Diag, "Crear USB técnico en " + drive, "", "", false, Int(log =>
            {
                Directory.CreateDirectory(root);
                File.Copy(exePath, Path.Combine(root, "RUSCUU Repara.exe"), true);
                log("Copiado RUSCUU Repara.exe");
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("ruscuu.ico"))
                using (var f = File.Create(Path.Combine(root, "ruscuu.ico"))) s.CopyTo(f);

                string links = Path.Combine(root, "Enlaces de descarga");
                Directory.CreateDirectory(links);
                foreach (var l in Links)
                    File.WriteAllText(Path.Combine(links, l[0] + ".url"), "[InternetShortcut]\r\nURL=" + l[1] + "\r\n");
                log(Links.Length + " enlaces a herramientas creados.");

                File.WriteAllText(Path.Combine(root, "LEEME - Guía rápida.txt"),
                    "RUSCUU Repara - USB técnico\r\n===========================\r\n\r\n" +
                    "1. Abre «RUSCUU Repara.exe» en el equipo a reparar (pide permisos de administrador).\r\n" +
                    "2. Empieza por Inicio > Reparación rápida. Si el equipo sigue mal, usa la Reparación completa.\r\n" +
                    "3. Sin Internet: Inicio > No tengo Internet.\r\n" +
                    "4. Antes de formatear: Herramientas > Respaldar drivers (elige esta USB como destino).\r\n" +
                    "5. Al terminar: Herramientas > Informe del PC, para entregar al cliente.\r\n\r\n" +
                    "La carpeta «Enlaces de descarga» tiene accesos a las páginas oficiales de herramientas gratuitas.\r\n", Encoding.UTF8);

                string autorun = Path.Combine(drive, "autorun.inf");
                if (!File.Exists(autorun) || File.ReadAllText(autorun).Contains("RUSCUU"))
                {
                    if (File.Exists(autorun)) File.SetAttributes(autorun, FileAttributes.Normal);
                    File.WriteAllText(autorun, "[autorun]\r\nicon=RUSCUU\\ruscuu.ico\r\nlabel=RUSCUU\r\n");
                    File.SetAttributes(autorun, FileAttributes.Hidden);
                    log("Icono de RUSCUU asignado a la unidad (se ve al volver a conectarla).");
                }
                else log("La USB ya tenía un autorun.inf propio; no se modificó.");

                if (!string.IsNullOrEmpty(toolsFolder) && Directory.Exists(toolsFolder))
                {
                    string dst = Path.Combine(root, "Herramientas");
                    int n = CopyDir(toolsFolder, dst);
                    log(n + " archivos de herramientas copiados.");
                }
                log("USB técnico listo en " + root);
                Process.Start("explorer.exe", "\"" + root + "\"");
                return 0;
            }));
        }

        static int CopyDir(string src, string dst)
        {
            int n = 0;
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src)) { File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true); n++; }
            foreach (var d in Directory.GetDirectories(src)) n += CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
            return n;
        }

        // ---------------------------------------------------------------- Acciones internas
        static int CleanFolder(string dir, Action<string> log)
        {
            if (!Directory.Exists(dir)) { log("No existe: " + dir); return 0; }
            long freed = 0; int files = 0, skipped = 0;
            CleanRec(new DirectoryInfo(dir), ref freed, ref files, ref skipped);
            log(string.Format("{0}: {1} archivos eliminados, {2} liberados ({3} en uso omitidos).", dir, files, Size(freed), skipped));
            return 0;
        }

        static void CleanRec(DirectoryInfo d, ref long freed, ref int files, ref int skipped)
        {
            FileInfo[] fs; DirectoryInfo[] ds;
            try { fs = d.GetFiles(); ds = d.GetDirectories(); } catch { skipped++; return; }
            foreach (var f in fs)
            {
                try { long l = f.Length; f.Attributes = FileAttributes.Normal; f.Delete(); freed += l; files++; }
                catch { skipped++; }
            }
            foreach (var sd in ds)
            {
                if ((sd.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                CleanRec(sd, ref freed, ref files, ref skipped);
                try { sd.Delete(false); } catch { }
            }
        }

        public static string Size(long b)
        {
            if (b >= 1L << 30) return (b / (double)(1L << 30)).ToString("0.00") + " GB";
            if (b >= 1L << 20) return (b / (double)(1L << 20)).ToString("0.0") + " MB";
            return (b / 1024.0).ToString("0") + " KB";
        }

        static void KillExplorer()
        {
            foreach (var p in Process.GetProcessesByName("explorer")) { try { p.Kill(); p.WaitForExit(3000); } catch { } }
        }

        static void EnsureExplorer(Action<string> log)
        {
            Thread.Sleep(2500);
            if (Process.GetProcessesByName("explorer").Length == 0)
            {
                // Lanzar el explorador sin privilegios de administrador
                try { Process.Start(new ProcessStartInfo("runas.exe", "/trustlevel:0x20000 explorer.exe") { CreateNoWindow = true, UseShellExecute = false }); }
                catch { Process.Start("explorer.exe"); }
            }
            log("Escritorio reiniciado.");
        }

        static void RestartExplorer(Action<string> log) { KillExplorer(); EnsureExplorer(log); }

        static int RebuildIconCache(Action<string> log)
        {
            KillExplorer();
            Thread.Sleep(1000);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            int n = 0;
            var targets = new List<string>();
            string exp = Path.Combine(local, @"Microsoft\Windows\Explorer");
            if (Directory.Exists(exp))
            {
                targets.AddRange(Directory.GetFiles(exp, "iconcache*.db"));
                targets.AddRange(Directory.GetFiles(exp, "thumbcache*.db"));
            }
            targets.Add(Path.Combine(local, "IconCache.db"));
            foreach (var f in targets) { try { if (File.Exists(f)) { File.Delete(f); n++; } } catch { } }
            log(n + " archivos de caché eliminados.");
            EnsureExplorer(log);
            return 0;
        }

        static int BatteryReport(Action<string> log)
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "RUSCUU_Informe_Bateria.html");
            var p = Process.Start(new ProcessStartInfo("powercfg.exe", "/batteryreport /output \"" + path + "\"") { CreateNoWindow = true, UseShellExecute = false });
            p.WaitForExit();
            if (File.Exists(path)) { log("Informe guardado en: " + path); Process.Start(path); return 0; }
            log("No se detectó batería en este equipo.");
            return 0;
        }
    }
}
