# deploy/run-daemon.ps1
# SvitloSk Publisher - Continuous Background Runner Daemon (Interval: 30 minutes)

$ErrorActionPreference = "Continue"

$intervalMinutes = 30
$intervalSeconds = $intervalMinutes * 60

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  SvitloSk Publisher - Continuous Daemon Runner" -ForegroundColor Cyan
Write-Host "  Daytime Cadence:  Every 30m (05:00 - 23:30)" -ForegroundColor Yellow
Write-Host "  Night Window:     Every 15m (23:30 - 02:30)" -ForegroundColor Yellow
Write-Host "  Sleep Mode:       02:30 - 05:00 (Quiet Window)" -ForegroundColor Magenta
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""

$iteration = 1

while ($true) {
    $nowDt = Get-Date
    $now = $nowDt.ToString("yyyy-MM-dd HH:mm:ss")
    $timeOfDay = $nowDt.TimeOfDay

    $sleepStart = [TimeSpan]::Parse("02:30:00")
    $sleepEnd   = [TimeSpan]::Parse("05:00:00")
    $nightStart = [TimeSpan]::Parse("23:30:00")

    # 1. Sleep Mode: 02:30 - 05:00 (Technological pause / Quiet window for WhatsApp API)
    if ($timeOfDay -ge $sleepStart -and $timeOfDay -lt $sleepEnd) {
        $targetWakeTime = $nowDt.Date.AddHours(5)
        $sleepSeconds = [int]([Math]::Max(1, ($targetWakeTime - $nowDt).TotalSeconds))
        $sleepMinutes = [math]::Round($sleepSeconds / 60)
        Write-Host "[$now] [SLEEP] Technological pause / Sleep mode active (02:30 - 05:00)." -ForegroundColor Magenta
        Write-Host "[$now] WhatsApp API quiet window. Sleeping for $sleepMinutes min until 05:00:00..." -ForegroundColor Magenta
        Write-Host "--------------------------------------------------" -ForegroundColor DarkGray
        Write-Host ""
        Start-Sleep -Seconds $sleepSeconds
        continue
    }

    # 2. Adaptive Cadence:
    # 23:30 - 02:30 -> 15 min (night formation and publishing window)
    # 05:00 - 23:30 -> 30 min (daytime operation)
    if ($timeOfDay -ge $nightStart -or $timeOfDay -lt $sleepStart) {
        $intervalMinutes = 15
        $modeDesc = "Night Window (23:30 - 02:30)"
    } else {
        $intervalMinutes = 30
        $modeDesc = "Daytime (05:00 - 23:30)"
    }
    $intervalSeconds = $intervalMinutes * 60

    Write-Host "[$now] >>> Starting Sync Cycle #$iteration ($modeDesc, Interval: ${intervalMinutes}m)..." -ForegroundColor Green
    
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
