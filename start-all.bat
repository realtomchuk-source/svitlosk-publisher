@echo off
title SvitloSk Production Launcher
cd /d "%~dp0"

echo ==================================================
echo   SvitloSk Publisher - Launching All Services
echo ==================================================
echo.

echo [1/2] Starting WhatsApp Bridge in separate window...
start "SvitloSk WhatsApp Bridge" powershell -NoExit -ExecutionPolicy Bypass -File tools\whatsapp-bridge\run-bridge.ps1

echo [2/2] Waiting 5 seconds for WhatsApp Bridge connection...
timeout /t 5 /nobreak >nul

echo Starting Publisher Daemon (30 min loop)...
start "SvitloSk Publisher Daemon (30 min)" powershell -NoExit -ExecutionPolicy Bypass -File deploy\run-daemon.ps1

echo.
echo All services launched! You can minimize these windows.
timeout /t 3 >nul
