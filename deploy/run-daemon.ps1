# deploy/run-daemon.ps1
# SvitloSk Publisher - Continuous Background Runner Daemon (Interval: 30 minutes)

$ErrorActionPreference = "Continue"

$intervalMinutes = 30
$intervalSeconds = $intervalMinutes * 60

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  SvitloSk Publisher - Continuous Daemon Runner" -ForegroundColor Cyan
Write-Host "  Interval: Every $intervalMinutes minutes" -ForegroundColor Yellow
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""

$iteration = 1

while ($true) {
    $nowDt = Get-Date
    $now = $nowDt.ToString("yyyy-MM-dd HH:mm:ss")
    
    # Adaptive Interval: 15 min between 00:00 and 03:00 (night rollover window), 30 min daytime
    $currentHour = $nowDt.Hour
    if ($currentHour -ge 0 -and $currentHour -lt 3) {
        $intervalMinutes = 15
    } else {
        $intervalMinutes = 30
    }
    $intervalSeconds = $intervalMinutes * 60

    Write-Host "[$now] >>> Starting Sync Cycle #$iteration (Interval: ${intervalMinutes}m)..." -ForegroundColor Green
    
    try {
        & "$PSScriptRoot\run-publisher.ps1"
    }
    catch {
        Write-Host "[$now] [ERROR] Sync cycle failed: $_" -ForegroundColor Red
    }
    
    $nextRun = (Get-Date).AddSeconds($intervalSeconds).ToString("HH:mm:ss")
    Write-Host ""
    Write-Host "[$now] Cycle #$iteration finished. Next cycle at: $nextRun" -ForegroundColor Cyan
    Write-Host "Waiting $intervalMinutes minutes (Press Ctrl+C to stop)..." -ForegroundColor DarkGray
    Write-Host "--------------------------------------------------" -ForegroundColor DarkGray
    Write-Host ""
    
    $iteration++
    Start-Sleep -Seconds $intervalSeconds
}
