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

# Verify configuration presence before launching dotnet run
if ([string]::IsNullOrWhiteSpace($env:TELEGRAM_BOT_TOKEN) -or [string]::IsNullOrWhiteSpace($env:TELEGRAM_CHAT_ID) -or [string]::IsNullOrWhiteSpace($env:REGISTRY_PATH)) {
    Write-Error "System Environment configuration variables are missing. Please run set-publisher-secrets.ps1 first."
}

$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
Push-Location $repoRoot
try {
    dotnet run --project "$repoRoot\SvitloSk.Publisher.Infrastructure" -- $args
}
finally {
    Pop-Location
}
