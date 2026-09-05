# deploy/set-publisher-secrets.ps1
# Interactive secure setup for Windows Environment secrets for SvitloSk Publisher.

$ErrorActionPreference = "Stop"

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  SvitloSk Publisher - Secret Initialization Tool" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""

# 1. Telegram Bot Token Setup
Write-Host "Enter Telegram Bot Token (Input is masked):" -NoNewline
$secureToken = Read-Host -AsSecureString
$plainToken = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto(
    [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureToken)
)
if ([string]::IsNullOrWhiteSpace($plainToken)) {
    Write-Error "Telegram Bot Token cannot be empty. Aborted."
}

# 2. Telegram Chat/Channel ID Setup
Write-Host "Enter Telegram Chat/Channel ID (e.g. -1001234567890): " -NoNewline
$chatId = Read-Host
if ([string]::IsNullOrWhiteSpace($chatId)) {
    Write-Error "Telegram Chat ID cannot be empty. Aborted."
}

# 3. Registry Path Setup
Write-Host "Enter Registry Path [Default: local/registry/production_registry.json]: " -NoNewline
$registryPath = Read-Host
if ([string]::IsNullOrWhiteSpace($registryPath)) {
    $registryPath = "local/registry/production_registry.json"
}

# Set environment variables in User target context (persists across processes/reboots)
[Environment]::SetEnvironmentVariable("TELEGRAM_BOT_TOKEN", $plainToken, "User")
[Environment]::SetEnvironmentVariable("TELEGRAM_CHAT_ID", $chatId, "User")
[Environment]::SetEnvironmentVariable("REGISTRY_PATH", $registryPath, "User")

Write-Host ""
Write-Host "[SUCCESS] Windows Environment variables have been securely configured for the current user!" -ForegroundColor Green
Write-Host "[INFO] Note: You will need to reopen your PowerShell/Command window for the changes to take effect." -ForegroundColor Yellow
Write-Host "--------------------------------------------------"
Write-Host "Verification status:"
Write-Host "  TELEGRAM_BOT_TOKEN: [SET]"
Write-Host "  TELEGRAM_CHAT_ID:   $chatId"
Write-Host "  REGISTRY_PATH:      $registryPath"
Write-Host "=================================================="
