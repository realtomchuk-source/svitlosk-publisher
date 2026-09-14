# deploy/set-facebook-secrets.ps1
# Interactive secure setup for Facebook Graph API secrets for SvitloSk Publisher.

$ErrorActionPreference = "Stop"

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  SvitloSk Publisher - Facebook Configuration Tool" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""

# 1. Facebook Page ID
Write-Host "Enter Facebook Page ID (e.g. 105829184729104): " -NoNewline
$pageId = Read-Host
if ([string]::IsNullOrWhiteSpace($pageId)) {
    Write-Error "Facebook Page ID cannot be empty. Aborted."
}

# 2. Facebook Page Access Token (Masked)
Write-Host "Enter Facebook Page Access Token (Input is masked):" -NoNewline
$secureToken = Read-Host -AsSecureString
$plainToken = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto(
    [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureToken)
)
if ([string]::IsNullOrWhiteSpace($plainToken)) {
    Write-Error "Facebook Page Access Token cannot be empty. Aborted."
}

# 3. Facebook Registry Path
Write-Host "Enter Facebook Registry Path [Default: local/registry/facebook_registry.json]: " -NoNewline
$registryPath = Read-Host
if ([string]::IsNullOrWhiteSpace($registryPath)) {
    $registryPath = "local/registry/facebook_registry.json"
}

# Set environment variables in User target context (persists across processes/reboots)
[Environment]::SetEnvironmentVariable("FACEBOOK_PAGE_ID", $pageId, "User")
[Environment]::SetEnvironmentVariable("FACEBOOK_PAGE_ACCESS_TOKEN", $plainToken, "User")
[Environment]::SetEnvironmentVariable("FACEBOOK_REGISTRY_PATH", $registryPath, "User")

Write-Host ""
Write-Host "[SUCCESS] Facebook environment variables configured successfully!" -ForegroundColor Green
Write-Host "[INFO] Note: Reopen PowerShell console to apply variables to current session." -ForegroundColor Yellow
Write-Host "--------------------------------------------------"
Write-Host "Configuration summary:"
Write-Host "  FACEBOOK_PAGE_ID:            $pageId"
Write-Host "  FACEBOOK_PAGE_ACCESS_TOKEN:  [SET - Masked]"
Write-Host "  FACEBOOK_REGISTRY_PATH:      $registryPath"
Write-Host "=================================================="
Write-Host ""
Write-Host "To verify your Facebook credentials, run:" -ForegroundColor Cyan
Write-Host "  dotnet run --project SvitloSk.Publisher.Infrastructure -- --facebook-check" -ForegroundColor White
