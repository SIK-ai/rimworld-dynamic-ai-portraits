@echo off
echo Building RimWorld AI Portraits Mod (RimWorld 1.6)...
if not exist 1.6\Assemblies mkdir 1.6\Assemblies

set "RIMWORLD_MANAGED=C:\Users\SIK\Downloads\RimWorld.v1.6.4543-20260521T230817Z-3-001\RimWorld.v1.6.4543\game\RimWorldWin64_Data\Managed"
set "HARMONY_PATH=C:\Users\SIK\Downloads\RimWorld.v1.6.4543-20260521T230817Z-3-001\RimWorld.v1.6.4543\game\Mods\Harmony\Current\Assemblies\0Harmony.dll"

C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:library /nostdlib /noconfig /out:1.6\Assemblies\AIPortraits.dll /r:"%RIMWORLD_MANAGED%\mscorlib.dll","%RIMWORLD_MANAGED%\System.dll","%RIMWORLD_MANAGED%\System.Core.dll","%RIMWORLD_MANAGED%\Assembly-CSharp.dll","%RIMWORLD_MANAGED%\UnityEngine.dll","%RIMWORLD_MANAGED%\UnityEngine.CoreModule.dll","%RIMWORLD_MANAGED%\UnityEngine.IMGUIModule.dll","%RIMWORLD_MANAGED%\UnityEngine.ImageConversionModule.dll","%RIMWORLD_MANAGED%\UnityEngine.UnityWebRequestModule.dll","%RIMWORLD_MANAGED%\UnityEngine.TextRenderingModule.dll","%RIMWORLD_MANAGED%\UnityEngine.VideoModule.dll","%RIMWORLD_MANAGED%\netstandard.dll","Assemblies\Microsoft.ML.OnnxRuntime.dll","Assemblies\System.Memory.dll","Assemblies\System.Buffers.dll","Assemblies\System.Runtime.CompilerServices.Unsafe.dll","Assemblies\System.Numerics.Vectors.dll","%HARMONY_PATH%" /recurse:Source\*.cs

if %errorlevel% neq 0 (
    echo Build FAILED!
    exit /b %errorlevel%
)

echo Copying assembly to root Assemblies folder...
copy /y 1.6\Assemblies\AIPortraits.dll Assemblies\AIPortraits.dll

echo Packaging ONNX Runtime + dependencies into 1.6\Assemblies...
copy /y Assemblies\Microsoft.ML.OnnxRuntime.dll 1.6\Assemblies\
copy /y Assemblies\onnxruntime.dll 1.6\Assemblies\
copy /y Assemblies\System.Memory.dll 1.6\Assemblies\
copy /y Assemblies\System.Buffers.dll 1.6\Assemblies\
copy /y Assemblies\System.Runtime.CompilerServices.Unsafe.dll 1.6\Assemblies\
copy /y Assemblies\System.Numerics.Vectors.dll 1.6\Assemblies\

set "GAME_MOD=C:\Users\SIK\Downloads\RimWorld.v1.6.4543-20260521T230817Z-3-001\RimWorld.v1.6.4543\game\Mods\AIPortraits"
echo Deploying full mod (assemblies + model) to game Mods folder...
copy /y 1.6\Assemblies\*.dll "%GAME_MOD%\1.6\Assemblies\"
if not exist "%GAME_MOD%\Models" mkdir "%GAME_MOD%\Models"
copy /y Models\u2netp.onnx "%GAME_MOD%\Models\"

echo Build succeeded!
