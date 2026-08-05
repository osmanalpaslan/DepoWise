@echo off
REM Alpnex - Uygulamayi tamamen kaldir (cift tikla calistir).
REM Yanindaki .ps1 dosyasini yonetici gerektirmeden calistirir; onay ister.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Alpnex-Uygulamayi-Kaldir.ps1"
