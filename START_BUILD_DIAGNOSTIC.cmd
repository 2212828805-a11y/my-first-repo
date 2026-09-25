@echo off
setlocal
title LOOY Windows Controller - Build Diagnostic
color 0F

echo ============================================================
echo LOOY Windows Controller - Build Diagnostic
echo ============================================================
echo.
echo This window is paused BEFORE starting the build.
echo If you can read this message, Windows allowed the CMD file.
echo Project folder: %~dp0
echo.
echo Press any key to continue with the environment checks.
pause ^>nul

echo.
echo [1/4] Checking project files...
if not exist "%~dp0src\LooyWindowsController\LooyWindowsController.csproj" (
    echo ERROR: src\LooyWindowsController\LooyWindowsController.csproj was not found.
    echo Please extract the entire ZIP before running this file.
    goto :failed
)
if not exist "%~dp0scripts\build-windows.ps1" (
    echo ERROR: scripts\build-windows.ps1 was not found.
    echo Please extract the entire ZIP before running this file.
    goto :failed
)
echo Project files: OK

echo.
echo [2/4] Checking Windows tools...
where powershell.exe 2^>nul || echo WARNING: powershell.exe was not found.
where dotnet.exe 2^>nul || echo INFO: dotnet.exe is not installed yet; the build script will try to install it.
where winget.exe 2^>nul || echo INFO: winget.exe was not found.

echo.
echo [3/4] Starting the PowerShell build script...
echo A detailed log will be written to build.log.
echo.
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build-windows.ps1"
set "BUILD_RC=%ERRORLEVEL%"

echo.
echo [4/4] Build script finished with exit code: %BUILD_RC%
if exist "%~dp0build.log" echo Log file: %~dp0build.log
if "%BUILD_RC%"=="0" goto :success
goto :failed_with_code

:success
echo.
echo SUCCESS: The build completed.
echo Look in the dist folder for the Windows application.
echo.
pause
exit /b 0

:failed
set "BUILD_RC=1"

:failed_with_code
echo.
echo BUILD FAILED OR WAS BLOCKED.
echo Please send build.log and a photo of this window for diagnosis.
echo.
pause
exit /b %BUILD_RC%
