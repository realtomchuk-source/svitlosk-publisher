@echo off
title SvitloSk Services Stopper
cd /d "%~dp0"

echo ==================================================
echo   Stopping All SvitloSk Services...
echo ==================================================

echo Stopping node server on port 3000...
for /f "tokens=5" %%a in ('netstat -aon ^| findstr :3000 ^| findstr LISTENING') do (
    taskkill /F /PID %%a >nul 2>&1
)

echo Stopping daemon and bridge powershell windows...
powershell -NoProfile -Command "Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like '*run-bridge.ps1*' -or $_.CommandLine -like '*run-daemon.ps1*' -or $_.CommandLine -like '*run-publisher.ps1*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }" >nul 2>&1

echo All services stopped cleanly.
ping 127.0.0.1 -n 2 >nul
