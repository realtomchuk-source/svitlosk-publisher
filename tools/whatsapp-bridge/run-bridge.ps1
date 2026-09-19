# tools/whatsapp-bridge/run-bridge.ps1
# Starts the local WhatsApp Web Bridge microservice for SvitloSk Publisher.

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "   SvitloSk Publisher - WhatsApp Local Bridge" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

Set-Location $ScriptDir

if (-not (Test-Path "node_modules")) {
    Write-Host "[INFO] Installing Node.js dependencies..." -ForegroundColor Yellow
    npm install
}

Write-Host "[INFO] Starting WhatsApp Bridge server..." -ForegroundColor Green
node server.js
