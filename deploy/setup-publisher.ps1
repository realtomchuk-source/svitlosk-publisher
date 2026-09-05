# deploy/setup-publisher.ps1
# Production deployment readiness check and folders setup for Windows PC.

$ErrorActionPreference = "Stop"

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  SvitloSk Publisher - Production Deployment Setup" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""

# 1. Check for .NET Core Runtime / SDK
Write-Host "[CHECK] Checking .NET SDK / Runtime installation..."
$dotnetVersion = & dotnet --version 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Error ".NET SDK is not installed or not in System PATH. Please download and install .NET 8.0 SDK."
} else {
    Write-Host "  .NET SDK Version: $dotnetVersion" -ForegroundColor Green
}

# 2. Check and Setup Production Directory Structure
Write-Host "[CHECK] Setting up directory structures..."
$dirs = @(
    "deploy",
    "local/registry",
    "local/fixtures"
)

foreach ($dir in $dirs) {
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir | Out-Null
        Write-Host "  Created directory: $dir" -ForegroundColor Green
    } else {
        Write-Host "  Directory exists: $dir" -ForegroundColor Gray
    }
}

# 3. Check for Required Environment Variables (reads User context as fallback if not in active session)
Write-Host "[CHECK] Validating Environment Variables..."
$botToken = $env:TELEGRAM_BOT_TOKEN
if ([string]::IsNullOrEmpty($botToken)) {
    $botToken = [Environment]::GetEnvironmentVariable("TELEGRAM_BOT_TOKEN", "User")
}

$chatId = $env:TELEGRAM_CHAT_ID
if ([string]::IsNullOrEmpty($chatId)) {
    $chatId = [Environment]::GetEnvironmentVariable("TELEGRAM_CHAT_ID", "User")
}

$registryPath = $env:REGISTRY_PATH
if ([string]::IsNullOrEmpty($registryPath)) {
    $registryPath = [Environment]::GetEnvironmentVariable("REGISTRY_PATH", "User")
}

$errors = 0

if ([string]::IsNullOrWhiteSpace($botToken)) {
    Write-Host "  [MISSING] TELEGRAM_BOT_TOKEN is not configured." -ForegroundColor Red
    $errors++
} else {
    if ($botToken -match "^\d+:[A-Za-z0-9_-]+$") {
        Write-Host "  [SET] TELEGRAM_BOT_TOKEN (Valid Format)" -ForegroundColor Green
    } else {
        Write-Host "  [INVALID FORMAT] TELEGRAM_BOT_TOKEN format is invalid." -ForegroundColor Yellow
    }
}

if ([string]::IsNullOrWhiteSpace($chatId)) {
    Write-Host "  [MISSING] TELEGRAM_CHAT_ID is not configured." -ForegroundColor Red
    $errors++
} else {
    Write-Host "  [SET] TELEGRAM_CHAT_ID: $chatId" -ForegroundColor Green
}

if ([string]::IsNullOrWhiteSpace($registryPath)) {
    Write-Host "  [MISSING] REGISTRY_PATH is not configured." -ForegroundColor Red
    $errors++
} else {
    Write-Host "  [SET] REGISTRY_PATH: $registryPath" -ForegroundColor Green
}

# 4. Check Git Installation
Write-Host "[CHECK] Checking Git command-line tool..."
$gitVersion = & git --version 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "  [WARNING] Git CLI tool not found in PATH. History commits and auto-pushes will fail." -ForegroundColor Yellow
} else {
    Write-Host "  Git Version: $gitVersion" -ForegroundColor Green
}

# 5. Summary
Write-Host ""
if ($errors -gt 0) {
    Write-Host "[FAIL] Deployment preparation completed with errors. Please resolve missing environment variables." -ForegroundColor Red
    Exit 1
} else {
    Write-Host "[SUCCESS] SvitloSk Publisher environment configuration is valid for launch!" -ForegroundColor Green
    Exit 0
}
