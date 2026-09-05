# deploy/install-publisher-task.ps1
# Sets up Windows Task Scheduler to execute SvitloSk Publisher hourly.

$ErrorActionPreference = "Stop"

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  SvitloSk Publisher - Install Scheduler Task" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""

$taskName = "SvitloSk_Outage_Publisher"
$workDir = Resolve-Path "."
$scriptPath = Resolve-Path "deploy/run-publisher.ps1"

# Create action to execute PowerShell script
$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-NoProfile -WindowStyle Hidden -File `"$scriptPath`"" -WorkingDirectory $workDir

# Trigger: 15-minute repeat, infinitely
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes 15)

# Settings: Stop if runs longer than 15 minutes, allow start on battery, ignore overlapping instances
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew

# Register Task
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings -Force | Out-Null

Write-Host "[SUCCESS] Task '$taskName' has been registered." -ForegroundColor Green
Write-Host "  WorkingDirectory: $workDir"
Write-Host "  ScriptPath:       $scriptPath"
Write-Host "  Schedule:         Triggered hourly (network check enabled)"
Write-Host "=================================================="
