// RUSCUU Repara - tareas de la versión 3: navegadores, seguridad, privacidad, licencias, Modo Gamer y winget
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Ruscuu
{
    static partial class Catalog
    {
        static void AddExtras(List<RepairTask> list)
        {
            // ---- Limpieza: caché de navegadores
            foreach (var b in new[] { new[] { "chrome", "Google Chrome" }, new[] { "msedge", "Microsoft Edge" }, new[] { "brave", "Brave" }, new[] { "opera", "Opera GX" }, new[] { "firefox", "Mozilla Firefox" } })
            {
                string id = b[0];
                list.Add(T("cache-" + id, Clean, "Caché de " + b[1],
                    "Borra la caché de páginas e imágenes. No toca historial, contraseñas ni sesiones. Cierra el navegador antes.",
                    "<1 min", false, Int(log => Browsers.CleanCache(id, log))));
            }

            // ---- Seguridad
            list.Add(T("seccheck", Sec, "Revisión de seguridad",
                "Comprueba UAC, Secure Boot, TPM, BitLocker, SmartScreen, firewall, cuentas sin contraseña, invitado, escritorio remoto y SMBv1.",
                "segundos", false, PS(SecurityScript)));
            list.Add(T("fwon", Sec, "Activar el firewall",
                "Enciende el firewall de Windows en todos los perfiles de red.",
                "segundos", false, PS("Set-NetFirewallProfile -All -Enabled True; 'Firewall activado en todos los perfiles.'")));
            list.Add(T("uacon", Sec, "Activar el control de cuentas (UAC)",
                "Vuelve a pedir permiso antes de que un programa cambie el sistema.",
                "segundos", true, PS(@"Set-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' -Name EnableLUA -Value 1 -Type DWord; Set-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' -Name ConsentPromptBehaviorAdmin -Value 5 -Type DWord; 'UAC activado (se aplica al reiniciar).'")));
            list.Add(T("hijack", Sec, "Arreglar navegador secuestrado",
                "Quita proxys falsos, limpia el archivo hosts y elimina políticas que fuerzan página de inicio o extensiones. Guarda copia de todo.",
                "segundos", false, Int(Browsers.FixHijack)));

            // ---- Privacidad
            list.Add(T("priv1", Priv, "Privacidad básica",
                "Quita el ID de publicidad, los anuncios y sugerencias de Windows y las experiencias personalizadas.",
                "segundos", false, PS(PrivacyScript(1))));
            list.Add(T("priv2", Priv, "Privacidad recomendada",
                "Lo básico + sin Bing en la búsqueda, sin recomendaciones en Inicio, sin historial de actividad ni encuestas.",
                "segundos", false, PS(PrivacyScript(2))));
            list.Add(T("priv3", Priv, "Privacidad máxima",
                "Lo recomendado + telemetría al mínimo, sin Recall ni Copilot y servicio de seguimiento desactivado.",
                "segundos", true, PS(PrivacyScript(3))));
            list.Add(T("privreset", Priv, "Restaurar privacidad de Windows",
                "Deshace todos los cambios de privacidad y vuelve a la configuración de fábrica.",
                "segundos", true, PS(PrivacyScript(0))));

            // ---- Diagnóstico
            list.Add(T("bsod", Diag, "Analizar pantallazos azules",
                "Lee los errores de los últimos 90 días, explica en español qué los causó y qué reparar.",
                "segundos", false, Int(Bsod.Analyze)));
            list.Add(T("license", Diag, "Licencias de Windows y Office",
                "Muestra la clave de Windows grabada en la BIOS, la clave instalada y el estado de activación de Windows y Office.",
                "~10 s", false, Int(License.Show)));

            // ---- Modo Gamer (se lanzan desde los botones grandes de Rendimiento)
            list.Add(T("gameron", GamerCat, "Activar Modo Gamer", "", "segundos", false, Int(Gamer.On)));
            list.Add(T("gameroff", GamerCat, "Salir del Modo Gamer", "", "segundos", false, Int(Gamer.Off)));

            // ---- Instalar y actualizar programas (winget)
            var up = T("wgupgrade", Inst, "Actualizar todos los programas",
                "Busca e instala la última versión de todos los programas compatibles (Chrome, 7-Zip, VLC, Zoom…).",
                "5-20 min", false, WingetCheck(), Wg("upgrade --all --silent --accept-package-agreements --accept-source-agreements --include-unknown"));
            up.Group = "Mantener al día"; list.Add(up);
            var ls = T("wglist", Inst, "Ver actualizaciones disponibles",
                "Solo muestra qué programas tienen versión nueva, sin instalar nada.",
                "<1 min", false, WingetCheck(), Wg("upgrade --accept-source-agreements --include-unknown"));
            ls.Group = "Mantener al día"; list.Add(ls);

            foreach (var app in Kit)
            {
                var t = T("wg:" + app[2], Inst, app[1], app[3], "1-5 min", false, WingetCheck(),
                    Wg("install --id " + app[2] + " -e --silent --accept-package-agreements --accept-source-agreements" + (app.Length > 4 ? " " + app[4] : "")));
                t.Group = app[0];
                list.Add(t);
            }
        }

        public static readonly string[] Essentials = { "wg:Google.Chrome", "wg:7zip.7zip", "wg:VideoLAN.VLC", "wg:Adobe.Acrobat.Reader.64-bit", "wg:Microsoft.VCRedist.2015+.x64", "wg:Microsoft.VCRedist.2015+.x86", "wg:Microsoft.DotNet.DesktopRuntime.8", "wg:AnyDeskSoftwareGmbH.AnyDesk" };

        // { grupo, nombre, id de winget, descripción, [argumentos extra] }
        static readonly string[][] Kit = {
            new[] { "Navegadores", "Google Chrome", "Google.Chrome", "El navegador más usado." },
            new[] { "Navegadores", "Mozilla Firefox", "Mozilla.Firefox", "Navegador libre y respetuoso con la privacidad." },
            new[] { "Navegadores", "Brave", "Brave.Brave", "Navegador con bloqueador de anuncios integrado." },
            new[] { "Navegadores", "Opera GX", "Opera.OperaGX", "Navegador pensado para gamers." },
            new[] { "Utilidades", "7-Zip", "7zip.7zip", "Abre y crea ZIP, RAR, 7z… gratis." },
            new[] { "Utilidades", "WinRAR", "RARLab.WinRAR", "Compresor RAR clásico." },
            new[] { "Utilidades", "Notepad++", "Notepad++.Notepad++", "Editor de texto avanzado." },
            new[] { "Utilidades", "PowerToys", "Microsoft.PowerToys", "Utilidades extra de Microsoft para Windows." },
            new[] { "Utilidades", "Everything", "voidtools.Everything", "Busca cualquier archivo al instante." },
            new[] { "Utilidades", "ShareX", "ShareX.ShareX", "Capturas y grabación de pantalla." },
            new[] { "Multimedia", "VLC", "VideoLAN.VLC", "Reproduce cualquier vídeo o música." },
            new[] { "Multimedia", "K-Lite Codec Pack", "CodecGuide.K-LiteCodecPack.Standard", "Códecs para reproducir todos los formatos." },
            new[] { "Multimedia", "OBS Studio", "OBSProject.OBSStudio", "Graba y transmite en directo." },
            new[] { "Multimedia", "Audacity", "Audacity.Audacity", "Editor de audio." },
            new[] { "Juegos", "Steam", "Valve.Steam", "La tienda de juegos de PC más grande." },
            new[] { "Juegos", "Epic Games Launcher", "EpicGames.EpicGamesLauncher", "Juegos gratis cada semana." },
            new[] { "Juegos", "EA app", "ElectronicArts.EADesktop", "Juegos de EA (FC, Battlefield…)." },
            new[] { "Juegos", "Ubisoft Connect", "Ubisoft.Connect", "Juegos de Ubisoft." },
            new[] { "Comunicación", "Discord", "Discord.Discord", "Chat de voz y texto." },
            new[] { "Comunicación", "WhatsApp", "9NKSQGP7F2NH", "WhatsApp para escritorio (Microsoft Store).", "--source msstore" },
            new[] { "Comunicación", "Telegram", "Telegram.TelegramDesktop", "Mensajería rápida." },
            new[] { "Comunicación", "Zoom", "Zoom.Zoom", "Videollamadas y reuniones." },
            new[] { "Oficina", "LibreOffice", "TheDocumentFoundation.LibreOffice", "Suite de oficina gratuita (Word, Excel…)." },
            new[] { "Oficina", "Adobe Acrobat Reader", "Adobe.Acrobat.Reader.64-bit", "Lector de PDF." },
            new[] { "Oficina", "Google Drive", "Google.GoogleDrive", "Sincroniza archivos con Google Drive." },
            new[] { "Oficina", "Dropbox", "Dropbox.Dropbox", "Almacenamiento en la nube." },
            new[] { "Técnico", "AnyDesk", "AnyDeskSoftwareGmbH.AnyDesk", "Soporte remoto." },
            new[] { "Técnico", "TeamViewer", "TeamViewer.TeamViewer", "Soporte remoto." },
            new[] { "Técnico", "RustDesk", "RustDesk.RustDesk", "Soporte remoto gratuito y de código abierto." },
            new[] { "Técnico", "CrystalDiskInfo", "CrystalDewWorld.CrystalDiskInfo", "Salud SMART de los discos." },
            new[] { "Técnico", "HWiNFO", "REALiX.HWiNFO", "Sensores y temperaturas." },
            new[] { "Técnico", "CPU-Z", "CPUID.CPU-Z", "Detalles del procesador y la RAM." },
            new[] { "Técnico", "Malwarebytes", "Malwarebytes.Malwarebytes", "Elimina malware y adware." },
            new[] { "Librerías (para que funcionen juegos y programas)", "Visual C++ 2015-2022 (64 bits)", "Microsoft.VCRedist.2015+.x64", "Necesario para muchos juegos y programas." },
            new[] { "Librerías (para que funcionen juegos y programas)", "Visual C++ 2015-2022 (32 bits)", "Microsoft.VCRedist.2015+.x86", "Necesario para programas de 32 bits." },
            new[] { "Librerías (para que funcionen juegos y programas)", ".NET Desktop Runtime 8", "Microsoft.DotNet.DesktopRuntime.8", "Necesario para muchos programas modernos." },
            new[] { "Librerías (para que funcionen juegos y programas)", "DirectX (runtime)", "Microsoft.DirectX", "Componentes de DirectX para juegos antiguos." },
            new[] { "Librerías (para que funcionen juegos y programas)", "Java", "Oracle.JavaRuntimeEnvironment", "Para programas y juegos en Java (Minecraft Java…)." },
        };

        static Step Wg(string args)
        {
            // códigos de winget que no son errores: ya instalado / sin actualización aplicable / nada que actualizar
            return new Step { File = "winget.exe", Args = args, Enc = Encoding.UTF8, OkCodes = new[] { -1978335189, -1978335135, -1978335212 } };
        }

        static Step WingetCheck()
        {
            return Int(log =>
            {
                string wg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\winget.exe");
                if (File.Exists(wg)) return 0;
                log("winget no está instalado en este equipo. Se abrirá la Microsoft Store para instalar «Instalador de aplicación»; después vuelve a intentarlo.");
                try { Process.Start("ms-windows-store://pdp/?productid=9NBLGGH4NNS1"); } catch { }
                return 1;
            });
        }

        // ---------------------------------------------------------------- Privacidad
        // { nivel, ruta, valor, dato, valor por defecto (vacío = borrar) }
        static readonly string[][] Tweaks = {
            new[] { "1", @"HKCU:\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", "0", "1" },
            new[] { "1", @"HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338388Enabled", "0", "1" },
            new[] { "1", @"HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338389Enabled", "0", "1" },
            new[] { "1", @"HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-353694Enabled", "0", "1" },
            new[] { "1", @"HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-353696Enabled", "0", "1" },
            new[] { "1", @"HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled", "0", "1" },
            new[] { "1", @"HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SilentInstalledAppsEnabled", "0", "1" },
            new[] { "1", @"HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SoftLandingEnabled", "0", "1" },
            new[] { "1", @"HKCU:\Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", "0", "1" },
            new[] { "2", @"HKCU:\Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", "1", "" },
            new[] { "2", @"HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_IrisRecommendations", "0", "1" },
            new[] { "2", @"HKCU:\Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", "0", "" },
            new[] { "2", @"HKLM:\SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", "0", "" },
            new[] { "2", @"HKLM:\SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", "0", "" },
            new[] { "2", @"HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", "1", "" },
            new[] { "3", @"HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", "0", "" },
            new[] { "3", @"HKLM:\SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo", "DisabledByGroupPolicy", "1", "" },
            new[] { "3", @"HKCU:\Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", "1", "" },
            new[] { "3", @"HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", "1", "" },
            new[] { "3", @"HKCU:\Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", "1", "" },
        };

        static string PrivacyScript(int level)
        {
            var sb = new StringBuilder();
            foreach (var t in Tweaks)
            {
                if (level > 0)
                {
                    if (int.Parse(t[0]) > level) continue;
                    sb.Append("New-Item -Path '" + t[1] + "' -Force -ErrorAction SilentlyContinue | Out-Null; Set-ItemProperty -Path '" + t[1] + "' -Name '" + t[2] + "' -Value " + t[3] + " -Type DWord; ");
                }
                else if (t[4] == "") sb.Append("Remove-ItemProperty -Path '" + t[1] + "' -Name '" + t[2] + "' -ErrorAction SilentlyContinue; ");
                else sb.Append("if (Test-Path '" + t[1] + "') { Set-ItemProperty -Path '" + t[1] + "' -Name '" + t[2] + "' -Value " + t[4] + " -Type DWord }; ");
            }
            if (level == 3) sb.Append("Stop-Service DiagTrack -Force -ErrorAction SilentlyContinue; Set-Service DiagTrack -StartupType Disabled; 'Servicio de seguimiento (DiagTrack) desactivado.'; ");
            if (level == 0) sb.Append("Set-Service DiagTrack -StartupType Automatic -ErrorAction SilentlyContinue; Start-Service DiagTrack -ErrorAction SilentlyContinue; 'Configuración de privacidad de Windows restaurada.'; ");
            else sb.Append("'" + Tweaks.Count(t => int.Parse(t[0]) <= level) + " ajustes de privacidad aplicados. Algunos se ven al cerrar sesión.'");
            return sb.ToString();
        }

        // ---------------------------------------------------------------- Revisión de seguridad
        const string SecurityScript =
            "function R($ok, $t) { if ($ok) { '[OK]  ' + $t } else { '[!!]  ' + $t } }; " +
            @"$uac = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' -ErrorAction SilentlyContinue).EnableLUA; R ($uac -ne 0) 'Control de cuentas de usuario (UAC)'; " +
            "try { R (Confirm-SecureBootUEFI -ErrorAction Stop) 'Arranque seguro (Secure Boot)' } catch { '[--]  Arranque seguro: no compatible o BIOS en modo Legacy' }; " +
            "try { $t = Get-Tpm -ErrorAction Stop; R ($t.TpmPresent -and $t.TpmReady) 'Chip de seguridad TPM' } catch { '[--]  TPM: no se pudo consultar' }; " +
            "try { $b = Get-BitLockerVolume -MountPoint $env:SystemDrive -ErrorAction Stop; R ($b.ProtectionStatus -eq 'On') ('Cifrado BitLocker del disco del sistema: ' + $b.ProtectionStatus) } catch { '[--]  BitLocker no disponible en esta edición' }; " +
            @"$ss = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer' -ErrorAction SilentlyContinue).SmartScreenEnabled; R ($ss -ne 'Off') 'SmartScreen (filtro de descargas peligrosas)'; " +
            "$fw = Get-NetFirewallProfile -ErrorAction SilentlyContinue; R (-not ($fw | Where-Object { -not $_.Enabled })) 'Firewall de Windows en todos los perfiles'; " +
            "try { $s = Get-MpComputerStatus -ErrorAction Stop; R $s.RealTimeProtectionEnabled 'Protección antivirus en tiempo real' } catch { '[--]  Defender no activo (otro antivirus instalado)' }; " +
            "$np = Get-LocalUser | Where-Object { $_.Enabled -and -not $_.PasswordLastSet }; R (-not $np) ('Cuentas sin contraseña: ' + $(if ($np) { ($np.Name -join ', ') } else { 'ninguna' })); " +
            "$g = Get-LocalUser | Where-Object { $_.Enabled -and $_.SID -like '*-501' }; R (-not $g) 'Cuenta de invitado desactivada'; " +
            @"$rdp = (Get-ItemProperty 'HKLM:\System\CurrentControlSet\Control\Terminal Server' -ErrorAction SilentlyContinue).fDenyTSConnections; R ($rdp -ne 0) 'Escritorio remoto cerrado'; " +
            "$smb = (Get-SmbServerConfiguration -ErrorAction SilentlyContinue).EnableSMB1Protocol; R (-not $smb) 'Protocolo inseguro SMBv1 desactivado'; " +
            "$hf = Get-HotFix -ErrorAction SilentlyContinue | Where-Object InstalledOn | Sort-Object InstalledOn -Descending | Select-Object -First 1; if ($hf) { $d = ((Get-Date) - $hf.InstalledOn).Days; R ($d -lt 45) ('Última actualización de Windows hace ' + $d + ' días') }";
    }
}
