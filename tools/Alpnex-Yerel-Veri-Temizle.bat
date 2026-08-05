@echo off
REM Alpnex - Yerel veriyi tamamen temizle (cift tikla calistir).
REM Yanindaki .ps1 dosyasini yonetici gerektirmeden calistirir; onay ister.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Alpnex-Yerel-Veri-Temizle.ps1"
