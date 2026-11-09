@echo off
rem Baut yoga.exe (Kommandozeile) und YogaPanel.exe (App) mit dem in Windows enthaltenen C#-Compiler.
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set S=%~dp0src
set ICO=%~dp0assets\app.ico
"%CSC%" /nologo /optimize+ /platform:x64 /target:exe /codepage:65001 /out:"%~dp0yoga.exe" /win32icon:"%ICO%" /r:System.Management.dll /r:System.Core.dll "%S%\Native.cs" "%S%\Sampler.cs" "%S%\Program.cs" || exit /b 1
"%CSC%" /nologo /optimize+ /platform:x64 /target:winexe /codepage:65001 /out:"%~dp0YogaPanel.exe" /win32icon:"%ICO%" /resource:"%ICO%",app.ico /r:System.Management.dll /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll "%S%\Native.cs" "%S%\Sampler.cs" "%S%\Panel\*.cs" || exit /b 1
