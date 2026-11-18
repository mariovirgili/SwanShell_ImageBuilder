@echo off
setlocal
rem Builds SwanShellImageBuilder.exe with the C# compiler of the .NET Framework 4,
rem which ships with Windows 7/8/10/11. Nothing to install.

cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [ERROR] csc.exe not found.
    echo Enable ".NET Framework 4.8" in "Turn Windows features on or off".
    pause
    exit /b 1
)

echo Compiler: %CSC%
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Security.dll ^
  /out:SwanShellImageBuilder.exe SwanShellImageBuilder.cs
if errorlevel 1 (
    echo.
    echo [ERROR] Build failed.
    pause
    exit /b 1
)

echo.
echo OK: SwanShellImageBuilder.exe created
pause
