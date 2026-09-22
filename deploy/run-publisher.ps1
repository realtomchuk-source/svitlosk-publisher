# deploy/run-publisher.ps1
# Production runner helper script ensuring environment variable mappings before invocation.

$ErrorActionPreference = "Stop"

# Load variables from User context if missing in current process env session
if ([string]::IsNullOrEmpty($env:TELEGRAM_BOT_TOKEN)) {
    $env:TELEGRAM_BOT_TOKEN = [Environment]::GetEnvironmentVariable("TELEGRAM_BOT_TOKEN", "User")
}
if ([string]::IsNullOrEmpty($env:TELEGRAM_CHAT_ID)) {
    $env:TELEGRAM_CHAT_ID = [Environment]::GetEnvironmentVariable("TELEGRAM_CHAT_ID", "User")
}
if ([string]::IsNullOrEmpty($env:TELEGRAM_DISCUSSION_GROUP_ID)) {
    $env:TELEGRAM_DISCUSSION_GROUP_ID = [Environment]::GetEnvironmentVariable("TELEGRAM_DISCUSSION_GROUP_ID", "User")
}
if ([string]::IsNullOrEmpty($env:REGISTRY_PATH)) {
    $env:REGISTRY_PATH = [Environment]::GetEnvironmentVariable("REGISTRY_PATH", "User")
}

# Facebook environment variables
if ([string]::IsNullOrEmpty($env:FACEBOOK_PAGE_ACCESS_TOKEN)) {
    $env:FACEBOOK_PAGE_ACCESS_TOKEN = [Environment]::GetEnvironmentVariable("FACEBOOK_PAGE_ACCESS_TOKEN", "User")
}
if ([string]::IsNullOrEmpty($env:FACEBOOK_PAGE_ID)) {
    $env:FACEBOOK_PAGE_ID = [Environment]::GetEnvironmentVariable("FACEBOOK_PAGE_ID", "User")
}
if ([string]::IsNullOrEmpty($env:FACEBOOK_REGISTRY_PATH)) {
    $env:FACEBOOK_REGISTRY_PATH = [Environment]::GetEnvironmentVariable("FACEBOOK_REGISTRY_PATH", "User")
}

# WhatsApp environment variables
if ([string]::IsNullOrEmpty($env:WHATSAPP_CHANNEL_ID)) {
    $env:WHATSAPP_CHANNEL_ID = [Environment]::GetEnvironmentVariable("WHATSAPP_CHANNEL_ID", "User")
}
if ([string]::IsNullOrEmpty($env:WHATSAPP_BRIDGE_URL)) {
    $env:WHATSAPP_BRIDGE_URL = [Environment]::GetEnvironmentVariable("WHATSAPP_BRIDGE_URL", "User")
}
if ([string]::IsNullOrEmpty($env:WHATSAPP_ACCESS_TOKEN)) {
    $env:WHATSAPP_ACCESS_TOKEN = [Environment]::GetEnvironmentVariable("WHATSAPP_ACCESS_TOKEN", "User")
}
if ([string]::IsNullOrEmpty($env:WHATSAPP_REGISTRY_PATH)) {
    $env:WHATSAPP_REGISTRY_PATH = [Environment]::GetEnvironmentVariable("WHATSAPP_REGISTRY_PATH", "User")
}

# Verify configuration presence before launching dotnet run
if ([string]::IsNullOrWhiteSpace($env:TELEGRAM_BOT_TOKEN) -or [string]::IsNullOrWhiteSpace($env:TELEGRAM_CHAT_ID) -or [string]::IsNullOrWhiteSpace($env:REGISTRY_PATH)) {
    Write-Error "System Environment configuration variables are missing. Please run set-publisher-secrets.ps1 first."
}

$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path

# Self-healing: Ensure WhatsApp Bridge is running if WhatsApp channel is configured
if (-not [string]::IsNullOrWhiteSpace($env:WHATSAPP_CHANNEL_ID)) {
    $bridgeUrl = if (-not [string]::IsNullOrWhiteSpace($env:WHATSAPP_BRIDGE_URL)) { $env:WHATSAPP_BRIDGE_URL } else { "http://127.0.0.1:3000" }
    $bridgeHealthy = $false
    try {
        $res = Invoke-RestMethod -Uri "$bridgeUrl/health" -TimeoutSec 2 -ErrorAction Stop
        if ($res.connected -eq $true) {
            $bridgeHealthy = $true
        }
    } catch {
        $bridgeHealthy = $false
    }

    if (-not $bridgeHealthy) {
        Write-Host "[INFO] WhatsApp Bridge is not running on $bridgeUrl. Auto-starting bridge in background..." -ForegroundColor Yellow
        $bridgeScript = Join-Path $repoRoot "tools\whatsapp-bridge\run-bridge.ps1"
        if (Test-Path $bridgeScript) {
            Start-Process -FilePath "powershell.exe" -ArgumentList "-ExecutionPolicy Bypass -File `"$bridgeScript`"" -WindowStyle Minimized
            for ($i = 0; $i -lt 10; $i++) {
                Start-Sleep -Seconds 1
                try {
                    $res = Invoke-RestMethod -Uri "$bridgeUrl/health" -TimeoutSec 2 -ErrorAction Stop
                    if ($res.connected -eq $true) {
                        Write-Host "[INFO] WhatsApp Bridge successfully auto-started and connected!" -ForegroundColor Green
                        $bridgeHealthy = $true
                        break
                    }
                } catch {}
            }
        }
    }
}

Push-Location $repoRoot
try {
    dotnet run --project "$repoRoot\SvitloSk.Publisher.Infrastructure" -- $args
}
finally {
    Pop-Location
}

