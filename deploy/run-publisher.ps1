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
if ([string]::IsNullOrEmpty($env:REGISTRY_PATH)) {
    $env:REGISTRY_PATH = [Environment]::GetEnvironmentVariable("REGISTRY_PATH", "User")
}

# Verify configuration presence before launching dotnet run
if ([string]::IsNullOrWhiteSpace($env:TELEGRAM_BOT_TOKEN) -or [string]::IsNullOrWhiteSpace($env:TELEGRAM_CHAT_ID) -or [string]::IsNullOrWhiteSpace($env:REGISTRY_PATH)) {
    Write-Error "System Environment configuration variables are missing. Please run set-publisher-secrets.ps1 first."
}

# Run SvitloSk Publisher
dotnet run --project SvitloSk.Publisher.Infrastructure -- $args
