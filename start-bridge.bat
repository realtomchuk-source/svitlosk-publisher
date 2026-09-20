@echo off
title SvitloSk WhatsApp Bridge
cd /d "%~dp0"
powershell -NoExit -ExecutionPolicy Bypass -File tools\whatsapp-bridge\run-bridge.ps1
pause
