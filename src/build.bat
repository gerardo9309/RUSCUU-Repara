@echo off
rem ============================================================================
rem  RUSCUU Repara - compilación
rem  Genera en la carpeta superior:
rem    RUSCUU Repara.exe               (programa, también funciona portátil)
rem    RUSCUU Repara - Instalador.exe  (instalador con accesos directos)
rem    version.txt                     (para las actualizaciones automáticas)
rem
rem  Firma digital opcional: define estas variables antes de compilar
rem    set RUSCUU_PFX=C:\ruta\certificado.pfx
rem    set RUSCUU_PFX_PASS=contraseña
rem ============================================================================
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
set OUT=%~dp0..
set APP=%OUT%\RUSCUU Repara.exe
set SETUP=%OUT%\RUSCUU Repara - Instalador.exe
set REFS=/r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Management.dll /r:Microsoft.VisualBasic.dll /r:System.Xml.dll

echo [1/4] Compilando el programa...
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /platform:anycpu /out:"%APP%" /win32icon:ruscuu.ico /win32manifest:app.manifest /resource:logo.png,logo.png /resource:ruscuu.ico,ruscuu.ico %REFS% *.cs
if errorlevel 1 goto :error

echo [2/4] Firma digital...
call :sign "%APP%"

echo [3/4] Compilando el instalador...
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /platform:anycpu /out:"%SETUP%" /win32icon:ruscuu.ico /win32manifest:app.manifest /resource:logo.png,logo.png /resource:"%APP%",app.exe /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll Installer\Setup.cs Settings.cs
if errorlevel 1 goto :error
call :sign "%SETUP%"

echo [4/4] Generando version.txt...
powershell -NoProfile -Command "$v = (Get-Item '%APP%').VersionInfo.ProductVersion -replace '\.0$',''; $h = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash([IO.File]::ReadAllBytes('%APP%'))).Replace('-',''); [IO.File]::WriteAllLines('%OUT%\version.txt', [string[]]@(('version=' + $v), 'url=https://github.com/ruscuu/RUSCUU-Repara/releases/latest/download/RUSCUU.Repara.exe', ('sha256=' + $h), 'notas=La cuenta de GitHub ahora es ruscuu (nueva URL de actualizaciones).'))"

echo.
echo Listo:
echo   %APP%
echo   %SETUP%
echo   %OUT%\version.txt
exit /b 0

:sign
if "%RUSCUU_PFX%"=="" (echo    sin certificado: se omite la firma & exit /b 0)
set SIGNTOOL=
for /f "delims=" %%i in ('dir /b /s "%ProgramFiles(x86)%\Windows Kits\10\bin\signtool.exe" 2^>nul ^| findstr /i "\x64\\"') do set "SIGNTOOL=%%i"
if "%SIGNTOOL%"=="" (echo    signtool.exe no encontrado: instala el Windows SDK & exit /b 0)
"%SIGNTOOL%" sign /f "%RUSCUU_PFX%" /p "%RUSCUU_PFX_PASS%" /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 %1
exit /b 0

:error
echo.
echo ERROR al compilar
exit /b 1
