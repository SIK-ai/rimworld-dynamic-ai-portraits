@echo off
echo ===================================================
echo Building RimWorld AI Portraits Mod (Multi-Version)
echo ===================================================

:: Load local paths configuration if it exists
if exist build_local.bat (
    call build_local.bat
)

:: Set default fallbacks for RimWorld 1.5 paths if not defined
if "%RIMWORLD_15_MANAGED%"=="" (
    set "RIMWORLD_15_MANAGED=C:\GOG Games\RimWorld\RimWorldWin64_Data\Managed"
)
if "%HARMONY_15_PATH%"=="" (
    set "HARMONY_15_PATH=C:\GOG Games\RimWorld\Mods\HarmonyRimWorld-master\Current\Assemblies\0Harmony.dll"
)

:: Set default fallbacks for RimWorld 1.6 paths if not defined
if "%RIMWORLD_16_MANAGED%"=="" (
    if not "%RIMWORLD_MANAGED%"=="" (
        set "RIMWORLD_16_MANAGED=%RIMWORLD_MANAGED%"
    ) else (
        set "RIMWORLD_16_MANAGED=C:\Program Files (x86)\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed"
    )
)
if "%HARMONY_16_PATH%"=="" (
    if not "%HARMONY_PATH%"=="" (
        set "HARMONY_16_PATH=%HARMONY_PATH%"
    ) else (
        set "HARMONY_16_PATH=C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\Harmony\Current\Assemblies\0Harmony.dll"
    )
)

echo.
echo Version 1.5 paths:
echo   Managed: "%RIMWORLD_15_MANAGED%"
echo   Harmony: "%HARMONY_15_PATH%"
echo.
echo Version 1.6 paths:
echo   Managed: "%RIMWORLD_16_MANAGED%"
echo   Harmony: "%HARMONY_16_PATH%"
echo.

:: Ensure build output folders exist
if not exist 1.5\Assemblies mkdir 1.5\Assemblies
if not exist 1.6\Assemblies mkdir 1.6\Assemblies

:: --- BUILD FOR RIMWORLD 1.5 ---
echo.
echo [1/2] Compiling for RimWorld 1.5...
if not exist "%RIMWORLD_15_MANAGED%" (
    echo WARNING: Managed assemblies directory not found for RimWorld 1.5. Skipping 1.5 compilation.
) else if not exist "%HARMONY_15_PATH%" (
    echo WARNING: Harmony.dll not found for RimWorld 1.5. Skipping 1.5 compilation.
) else (
    C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:library /nostdlib /noconfig /out:1.5\Assemblies\AIPortraits.dll /r:"%RIMWORLD_15_MANAGED%\mscorlib.dll","%RIMWORLD_15_MANAGED%\System.dll","%RIMWORLD_15_MANAGED%\System.Core.dll","%RIMWORLD_15_MANAGED%\Assembly-CSharp.dll","%RIMWORLD_15_MANAGED%\UnityEngine.dll","%RIMWORLD_15_MANAGED%\UnityEngine.CoreModule.dll","%RIMWORLD_15_MANAGED%\UnityEngine.IMGUIModule.dll","%RIMWORLD_15_MANAGED%\UnityEngine.ImageConversionModule.dll","%RIMWORLD_15_MANAGED%\UnityEngine.UnityWebRequestModule.dll","%RIMWORLD_15_MANAGED%\UnityEngine.TextRenderingModule.dll","%RIMWORLD_15_MANAGED%\UnityEngine.VideoModule.dll","%RIMWORLD_16_MANAGED%\netstandard.dll","Assemblies\Microsoft.ML.OnnxRuntime.dll","Assemblies\System.Memory.dll","Assemblies\System.Buffers.dll","Assemblies\System.Runtime.CompilerServices.Unsafe.dll","Assemblies\System.Numerics.Vectors.dll","%HARMONY_15_PATH%" /recurse:Source\*.cs
    
    if %errorlevel% neq 0 (
        echo.
        echo Error: RimWorld 1.5 compilation FAILED!
        exit /b %errorlevel%
    ) else (
        echo Compilation succeeded for RimWorld 1.5.
        
        echo Packaging dependencies into 1.5\Assemblies...
        copy /y Assemblies\Microsoft.ML.OnnxRuntime.dll 1.5\Assemblies\ >nul
        copy /y Assemblies\onnxruntime.dll 1.5\Assemblies\ >nul
        copy /y Assemblies\System.Memory.dll 1.5\Assemblies\ >nul
        copy /y Assemblies\System.Buffers.dll 1.5\Assemblies\ >nul
        copy /y Assemblies\System.Runtime.CompilerServices.Unsafe.dll 1.5\Assemblies\ >nul
        copy /y Assemblies\System.Numerics.Vectors.dll 1.5\Assemblies\ >nul
        :: Copy netstandard.dll from the 1.6 Managed folder as it is not natively in RimWorld 1.5 but required by ONNX Runtime
        copy /y "%RIMWORLD_16_MANAGED%\netstandard.dll" 1.5\Assemblies\ >nul
    )
)

:: --- BUILD FOR RIMWORLD 1.6 ---
echo.
echo [2/2] Compiling for RimWorld 1.6...
if not exist "%RIMWORLD_16_MANAGED%" (
    echo WARNING: Managed assemblies directory not found for RimWorld 1.6. Skipping 1.6 compilation.
) else if not exist "%HARMONY_16_PATH%" (
    echo WARNING: Harmony.dll not found for RimWorld 1.6. Skipping 1.6 compilation.
) else (
    C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:library /nostdlib /noconfig /out:1.6\Assemblies\AIPortraits.dll /r:"%RIMWORLD_16_MANAGED%\mscorlib.dll","%RIMWORLD_16_MANAGED%\System.dll","%RIMWORLD_16_MANAGED%\System.Core.dll","%RIMWORLD_16_MANAGED%\Assembly-CSharp.dll","%RIMWORLD_16_MANAGED%\UnityEngine.dll","%RIMWORLD_16_MANAGED%\UnityEngine.CoreModule.dll","%RIMWORLD_16_MANAGED%\UnityEngine.IMGUIModule.dll","%RIMWORLD_16_MANAGED%\UnityEngine.ImageConversionModule.dll","%RIMWORLD_16_MANAGED%\UnityEngine.UnityWebRequestModule.dll","%RIMWORLD_16_MANAGED%\UnityEngine.TextRenderingModule.dll","%RIMWORLD_16_MANAGED%\UnityEngine.VideoModule.dll","%RIMWORLD_16_MANAGED%\netstandard.dll","Assemblies\Microsoft.ML.OnnxRuntime.dll","Assemblies\System.Memory.dll","Assemblies\System.Buffers.dll","Assemblies\System.Runtime.CompilerServices.Unsafe.dll","Assemblies\System.Numerics.Vectors.dll","%HARMONY_16_PATH%" /recurse:Source\*.cs
    
    if %errorlevel% neq 0 (
        echo.
        echo Error: RimWorld 1.6 compilation FAILED!
        exit /b %errorlevel%
    ) else (
        echo Compilation succeeded for RimWorld 1.6.
        
        echo Copying 1.6 assembly to legacy root Assemblies folder...
        copy /y 1.6\Assemblies\AIPortraits.dll Assemblies\AIPortraits.dll >nul
        
        echo Packaging dependencies into 1.6\Assemblies...
        copy /y Assemblies\Microsoft.ML.OnnxRuntime.dll 1.6\Assemblies\ >nul
        copy /y Assemblies\onnxruntime.dll 1.6\Assemblies\ >nul
        copy /y Assemblies\System.Memory.dll 1.6\Assemblies\ >nul
        copy /y Assemblies\System.Buffers.dll 1.6\Assemblies\ >nul
        copy /y Assemblies\System.Runtime.CompilerServices.Unsafe.dll 1.6\Assemblies\ >nul
        copy /y Assemblies\System.Numerics.Vectors.dll 1.6\Assemblies\ >nul
    )
)

:: --- DEPLOYMENT ---
:: Deploy 1.5 Mod files
if not "%RIMWORLD_15_MODS_DIR%"=="" (
    if exist "%RIMWORLD_15_MODS_DIR%" (
        if exist 1.5\Assemblies\AIPortraits.dll (
            echo.
            echo Deploying RimWorld 1.5 mod files to "%RIMWORLD_15_MODS_DIR%\AIPortraits"...
            if not exist "%RIMWORLD_15_MODS_DIR%\AIPortraits\1.5\Assemblies" mkdir "%RIMWORLD_15_MODS_DIR%\AIPortraits\1.5\Assemblies"
            copy /y 1.5\Assemblies\*.dll "%RIMWORLD_15_MODS_DIR%\AIPortraits\1.5\Assemblies\" >nul
            if not exist "%RIMWORLD_15_MODS_DIR%\AIPortraits\Models" mkdir "%RIMWORLD_15_MODS_DIR%\AIPortraits\Models"
            copy /y Models\u2netp.onnx "%RIMWORLD_15_MODS_DIR%\AIPortraits\Models\" >nul
            
            :: Also copy core metadata files to GOG mods folder so it runs standalone
            if not exist "%RIMWORLD_15_MODS_DIR%\AIPortraits\About" mkdir "%RIMWORLD_15_MODS_DIR%\AIPortraits\About"
            copy /y About\* "%RIMWORLD_15_MODS_DIR%\AIPortraits\About\" >nul
            copy /y LoadFolders.xml "%RIMWORLD_15_MODS_DIR%\AIPortraits\" >nul
        )
    )
)

:: Deploy 1.6 Mod files
if not "%RIMWORLD_16_MODS_DIR%"=="" (
    if exist "%RIMWORLD_16_MODS_DIR%" (
        if exist 1.6\Assemblies\AIPortraits.dll (
            echo.
            echo Deploying RimWorld 1.6 mod files to "%RIMWORLD_16_MODS_DIR%\AIPortraits"...
            if not exist "%RIMWORLD_16_MODS_DIR%\AIPortraits\1.6\Assemblies" mkdir "%RIMWORLD_16_MODS_DIR%\AIPortraits\1.6\Assemblies"
            copy /y 1.6\Assemblies\*.dll "%RIMWORLD_16_MODS_DIR%\AIPortraits\1.6\Assemblies\" >nul
            if not exist "%RIMWORLD_16_MODS_DIR%\AIPortraits\Models" mkdir "%RIMWORLD_16_MODS_DIR%\AIPortraits\Models"
            copy /y Models\u2netp.onnx "%RIMWORLD_16_MODS_DIR%\AIPortraits\Models\" >nul
        )
    )
)

echo.
echo Build process completed!
