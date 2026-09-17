@echo off
title SvitloSk Publisher Daemon (30 min)
cd /d "%~dp0"
powershell -NoExit -ExecutionPolicy Bypass -File deploy\run-daemon.ps1
pause
