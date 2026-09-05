# deploy/remove-publisher-task.ps1
# Unregisters the SvitloSk Task from Windows Task Scheduler.

$ErrorActionPreference = "Stop"

$taskName = "SvitloSk_Outage_Publisher"

if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
    Write-Host "[SUCCESS] Task '$taskName' removed successfully." -ForegroundColor Green
} else {
    Write-Host "[INFO] Task '$taskName' not found. Nothing to remove." -ForegroundColor Yellow
}
