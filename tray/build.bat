@echo off
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize+ /out:UgreenSwitch.exe /r:System.Net.Http.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll UgreenSwitch.cs
