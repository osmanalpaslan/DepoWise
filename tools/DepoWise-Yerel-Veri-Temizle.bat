@echo off
REM DepoWise - Yerel veriyi tamamen temizle (cift tikla calistir).
REM Yanindaki .ps1 dosyasini yonetici gerektirmeden calistirir; onay ister.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0DepoWise-Yerel-Veri-Temizle.ps1"
