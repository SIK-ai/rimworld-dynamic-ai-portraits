@echo off
echo Building RimWorld AI Portraits Mod (RimWorld 1.6)...
if not exist 1.6\Assemblies mkdir 1.6\Assemblies

:: Check if a local configuration script exists to load machine-specific paths
if exist build_local.bat (
    call build_local.bat
)

:: Fallback default paths if not set by local script
if "%RIMWORLD_MANAGED%"=="" (
    set "RIMWORLD_MANAGED=C:\Program Files (x86)\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed"
)
if "%HARMONY_PATH%"=="" (
    set "HARMONY_PATH=C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\Harmony\Current\Assemblies\0Harmony.dll"
)

echo Using RIMWORLD_MANAGED: "%RIMWORLD_MANAGED%"
echo Using HARMONY_PATH:     "%HARMONY_PATH%"

if not exist "%RIMWORLD_MANAGED%" (
    echo.
    echo Error: Managed assemblies directory not found at "%RIMWORLD_MANAGED%"
    echo Please create a 'build_local.bat' file to override these paths with your local game directory.
    echo Example:
    echo   set "RIMWORLD_MANAGED=C:\Path\To\RimWorld\RimWorldWin64_Data\Managed"
    echo   set "HARMONY_PATH=C:\Path\To\RimWorld\Mods\Harmony\Current\Assemblies\0Harmony.dll"
    echo   set "RIMWORLD_MODS_DIR=C:\Path\To\RimWorld\Mods"
    echo.
    exit /b 1
)

C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:library /nostdlib /noconfig /out:1.6\Assemblies\AIPortraits.dll /r:"%RIMWORLD_MANAGED%\mscorlib.dll","%RIMWORLD_MANAGED%\System.dll","%RIMWORLD_MANAGED%\System.Core.dll","%RIMWORLD_MANAGED%\Assembly-CSharp.dll","%RIMWORLD_MANAGED%\UnityEngine.dll","%RIMWORLD_MANAGED%\UnityEngine.CoreModule.dll","%RIMWORLD_MANAGED%\UnityEngine.IMGUIModule.dll","%RIMWORLD_MANAGED%\UnityEngine.ImageConversionModule.dll","%RIMWORLD_MANAGED%\UnityEngine.UnityWebRequestModule.dll","%RIMWORLD_MANAGED%\UnityEngine.TextRenderingModule.dll","%RIMWORLD_MANAGED%\UnityEngine.VideoModule.dll","%RIMWORLD_MANAGED%\netstandard.dll","%HARMONY_PATH%" /recurse:Source\*.cs

if %errorlevel% neq 0 (
    echo Build FAILED!
    exit /b %errorlevel%
)

echo Copying assembly to root Assemblies folder...
copy /y 1.6\Assemblies\AIPortraits.dll Assemblies\AIPortraits.dll

if not "%RIMWORLD_MODS_DIR%"=="" (
    if exist "%RIMWORLD_MODS_DIR%" (
        echo Copying assembly to RimWorld game Mods folder...
        if not exist "%RIMWORLD_MODS_DIR%\AIPortraits\1.6\Assemblies" mkdir "%RIMWORLD_MODS_DIR%\AIPortraits\1.6\Assemblies"
        copy /y 1.6\Assemblies\AIPortraits.dll "%RIMWORLD_MODS_DIR%\AIPortraits\1.6\Assemblies\AIPortraits.dll"
    )
)

echo Build succeeded!
