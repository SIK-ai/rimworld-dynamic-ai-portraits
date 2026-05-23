@echo off
echo Building RimWorld AI Portraits Mod (RimWorld 1.6)...
if not exist 1.6\Assemblies mkdir 1.6\Assemblies

:: ── CONFIGURE THESE PATHS FOR YOUR MACHINE ────────────────────────────────
:: Point RIMWORLD_MANAGED to: <RimWorld install>\RimWorldWin64_Data\Managed
:: Point HARMONY_PATH    to: <RimWorld install>\Mods\Harmony\Current\Assemblies\0Harmony.dll
set "RIMWORLD_MANAGED=C:\Program Files (x86)\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed"
set "HARMONY_PATH=C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\Harmony\Current\Assemblies\0Harmony.dll"
:: ──────────────────────────────────────────────────────────────────────────

C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:library /nostdlib /noconfig /out:1.6\Assemblies\AIPortraits.dll /r:"%RIMWORLD_MANAGED%\mscorlib.dll","%RIMWORLD_MANAGED%\System.dll","%RIMWORLD_MANAGED%\System.Core.dll","%RIMWORLD_MANAGED%\Assembly-CSharp.dll","%RIMWORLD_MANAGED%\UnityEngine.dll","%RIMWORLD_MANAGED%\UnityEngine.CoreModule.dll","%RIMWORLD_MANAGED%\UnityEngine.IMGUIModule.dll","%RIMWORLD_MANAGED%\UnityEngine.ImageConversionModule.dll","%RIMWORLD_MANAGED%\UnityEngine.UnityWebRequestModule.dll","%RIMWORLD_MANAGED%\UnityEngine.TextRenderingModule.dll","%RIMWORLD_MANAGED%\netstandard.dll","%HARMONY_PATH%" /recurse:Source\*.cs

if %errorlevel% neq 0 (
    echo Build FAILED!
    exit /b %errorlevel%
)

echo Copying assembly to root Assemblies folder...
copy /y 1.6\Assemblies\AIPortraits.dll Assemblies\AIPortraits.dll

echo Build succeeded!
