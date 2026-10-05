@echo off
title SvitloSk Production Launcher
cd /d "%~dp0"

echo ==================================================
echo   SvitloSk Publisher - Launching All Services
echo ==================================================
echo.

echo Cleaning up any old instances...
call "%~dp0stop-all.bat" >nul 2>&1
ping 127.0.0.1 -n 2 >nul

echo [1/2] Starting WhatsApp Bridge in separate window...
start "SvitloSk WhatsApp Bridge" powershell -NoExit -ExecutionPolicy Bypass -File tools\whatsapp-bridge\run-bridge.ps1

echo [2/2] Waiting 6 seconds for WhatsApp Bridge initialization...
ping 127.0.0.1 -n 7 >nul

echo Starting Publisher Daemon (30 min loop)...
start "SvitloSk Publisher Daemon (30 min)" powershell -NoExit -ExecutionPolicy Bypass -File deploy\run-daemon.ps1

echo.
echo All services launched! You can minimize these windows.
ping 127.0.0.1 -n 3 >nul
