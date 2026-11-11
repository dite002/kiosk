@echo off
echo ============================================
echo  SIKAWAN LAGUSTA - Build System
echo ============================================
echo.

:: 1. KioskApp (TCP Server Host)
echo [1/4] Compiling KioskApp.cs ...
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /res:img/logo.png,logo.png /r:System.dll,System.Windows.Forms.dll,System.Drawing.dll,System.Data.dll,System.Data.SQLite.dll KioskApp.cs
if %ERRORLEVEL% NEQ 0 (echo    GAGAL! & goto :eof) else (echo    OK)

:: 2. DisplayMonitor (TCP Socket Client & Video Player)
echo [2/4] Compiling DisplayMonitor.cs ...
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /r:System.dll,System.Windows.Forms.dll,System.Drawing.dll,System.Data.dll,System.Data.SQLite.dll DisplayMonitor.cs
if %ERRORLEVEL% NEQ 0 (echo    GAGAL! & goto :eof) else (echo    OK)

:: 3. CallerApp (TCP Socket Client)
echo [3/4] Compiling CallerApp.cs ...
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /r:System.dll,System.Windows.Forms.dll,System.Drawing.dll,System.Data.dll,System.Data.SQLite.dll CallerApp.cs
if %ERRORLEVEL% NEQ 0 (echo    GAGAL! & goto :eof) else (echo    OK)

:: 4. ReportApp (Laporan Antrian)
echo [4/4] Compiling ReportApp.cs ...
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /r:System.dll,System.Windows.Forms.dll,System.Drawing.dll,System.Data.dll,System.Data.SQLite.dll ReportApp.cs
if %ERRORLEVEL% NEQ 0 (echo    GAGAL! & goto :eof) else (echo    OK)

echo.
echo ============================================
echo  Build selesai!
echo ============================================
pause
