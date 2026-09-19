# deploy/set-whatsapp-secrets.ps1
# Interactive secure setup for WhatsApp Channel / Bridge / Meta secrets for SvitloSk Publisher.

$ErrorActionPreference = "Stop"

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  SvitloSk Publisher - WhatsApp Configuration Tool" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""

# 1. WhatsApp Channel Link or ID
Write-Host "Enter WhatsApp Channel link (e.g. https://whatsapp.com/channel/...) or Channel ID: " -NoNewline
$channelId = Read-Host
if ([string]::IsNullOrWhiteSpace($channelId)) {
    Write-Error "WhatsApp Channel identifier cannot be empty. Aborted."
}

# 2. Select Connection Mode
Write-Host ""
Write-Host "Select Connection Mode:" -ForegroundColor Yellow
Write-Host "  1. Free Local WhatsApp Web Bridge (Recommended for Channels)"
Write-Host "  2. Meta Cloud API (Graph API token)"
Write-Host "Enter Choice [Default: 1]: " -NoNewline
$choice = Read-Host

$bridgeUrl = ""
$plainToken = ""

if ($choice -eq "2") {
    Write-Host "Enter WhatsApp Meta Access Token (Input is masked): " -NoNewline
    $secureToken = Read-Host -AsSecureString
    $plainToken = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureToken)
    )
    if ([string]::IsNullOrWhiteSpace($plainToken)) {
        Write-Error "WhatsApp Access Token cannot be empty when using Meta Cloud API. Aborted."
    }
} else {
    Write-Host "Enter WhatsApp Bridge URL [Default: http://127.0.0.1:3000]: " -NoNewline
    $bridgeUrl = Read-Host
    if ([string]::IsNullOrWhiteSpace($bridgeUrl)) {
        $bridgeUrl = "http://127.0.0.1:3000"
    }
}

# 3. WhatsApp Registry Path
Write-Host "Enter WhatsApp Registry Path [Default: local/registry/whatsapp_registry.json]: " -NoNewline
$registryPath = Read-Host
if ([string]::IsNullOrWhiteSpace($registryPath)) {
    $registryPath = "local/registry/whatsapp_registry.json"
}

# Set environment variables in User target context (persists across reboots/sessions)
[Environment]::SetEnvironmentVariable("WHATSAPP_CHANNEL_ID", $channelId, "User")
[Environment]::SetEnvironmentVariable("WHATSAPP_REGISTRY_PATH", $registryPath, "User")

if (-not [string]::IsNullOrWhiteSpace($bridgeUrl)) {
    [Environment]::SetEnvironmentVariable("WHATSAPP_BRIDGE_URL", $bridgeUrl, "User")
}
if (-not [string]::IsNullOrWhiteSpace($plainToken)) {
    [Environment]::SetEnvironmentVariable("WHATSAPP_ACCESS_TOKEN", $plainToken, "User")
}

Write-Host ""
Write-Host "[SUCCESS] WhatsApp configuration saved successfully!" -ForegroundColor Green
Write-Host "--------------------------------------------------"
Write-Host "Configuration summary:"
Write-Host "  WHATSAPP_CHANNEL_ID:         $channelId"
if (-not [string]::IsNullOrWhiteSpace($bridgeUrl)) {
    Write-Host "  WHATSAPP_BRIDGE_URL:         $bridgeUrl"
}
if (-not [string]::IsNullOrWhiteSpace($plainToken)) {
    Write-Host "  WHATSAPP_ACCESS_TOKEN:       [SET - Masked]"
}
Write-Host "  WHATSAPP_REGISTRY_PATH:      $registryPath"
Write-Host "=================================================="
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
if (-not [string]::IsNullOrWhiteSpace($bridgeUrl)) {
    Write-Host "  1. Start the bridge:        .\tools\whatsapp-bridge\run-bridge.ps1" -ForegroundColor White
    Write-Host "  2. Scan the QR code once with your WhatsApp mobile app." -ForegroundColor White
}
Write-Host "  3. Verify connection:       dotnet run --project SvitloSk.Publisher.Infrastructure -- --whatsapp-check" -ForegroundColor White
Write-Host "  4. Publish journal:         dotnet run --project SvitloSk.Publisher.Infrastructure -- --whatsapp-sync" -ForegroundColor White
