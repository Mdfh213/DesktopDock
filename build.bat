@echo off
rem DesktopDock 一键重新编译（只依赖 Windows 自带的 .NET Framework 4.x 编译器）
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo [错误] 找不到 csc.exe，请确认系统带有 .NET Framework 4.x
  pause
  exit /b 1
)
if not exist "%~dp0dist" mkdir "%~dp0dist"
"%CSC%" /nologo /target:winexe /optimize+ /out:"%~dp0dist\DesktopDock.exe" ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll ^
  "%~dp0src\*.cs"
if errorlevel 1 (
  echo.
  echo [失败] 编译出错
  pause
  exit /b 1
)
echo.
echo [成功] 已生成 dist\DesktopDock.exe
dir "%~dp0dist\DesktopDock.exe" | findstr DesktopDock
pause
