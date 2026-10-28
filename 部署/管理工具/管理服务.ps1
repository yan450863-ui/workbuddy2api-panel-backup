param([ValidateSet('Start','Stop','Status')][string]$Action = 'Start')
$ErrorActionPreference = 'Stop'
$deployDir = Split-Path -Parent $PSScriptRoot
$exePath = Join-Path $deployDir 'wb2api.exe'
$pidPath = Join-Path $deployDir 'data/service-process.json'
$lifecyclePath = Join-Path $deployDir 'data/lifecycle.log'

function Get-TrackedServiceProcess {
    if (!(Test-Path -LiteralPath $pidPath)) { return $null }
    try {
        $saved = Get-Content -LiteralPath $pidPath -Raw | ConvertFrom-Json
        $candidate = Get-Process -Id ([int]$saved.process_id) -ErrorAction Stop
        $savedTicks = ([DateTimeOffset]$saved.start_time).UtcDateTime.Ticks
        if ([string]::Equals($candidate.Path, $exePath, [StringComparison]::OrdinalIgnoreCase) -and $candidate.StartTime.ToUniversalTime().Ticks -eq $savedTicks) {
            return $candidate
        }
    } catch { }
    return $null
}

function Write-Lifecycle([string]$message) {
    Add-Content -LiteralPath $lifecyclePath -Value ('{0} {1}' -f (Get-Date).ToString('o'), $message)
}

New-Item -ItemType Directory -Path (Join-Path $deployDir 'data') -Force | Out-Null
$activeProcess = Get-TrackedServiceProcess

if ($Action -eq 'Stop') {
    if ($null -ne $activeProcess) {
        Stop-Process -Id $activeProcess.Id -ErrorAction SilentlyContinue
        try { Wait-Process -Id $activeProcess.Id -Timeout 8 -ErrorAction SilentlyContinue } catch { }
        Write-Lifecycle ('stopped PID={0}' -f $activeProcess.Id)
        Remove-Item -LiteralPath $pidPath -ErrorAction SilentlyContinue
        Write-Host 'Service stopped.'
    } else {
        Remove-Item -LiteralPath $pidPath -ErrorAction SilentlyContinue
        Write-Host 'Service is not running.'
    }
    exit 0
}

if ($Action -eq 'Start' -and $null -eq $activeProcess) {
    if (!(Test-Path -LiteralPath $exePath)) { throw 'wb2api.exe is missing.' }
    if (!(Test-Path -LiteralPath (Join-Path $deployDir 'config.json'))) { throw 'config.json is missing.' }

    $stdoutPath = Join-Path $deployDir 'data/stdout.log'
    $stderrPath = Join-Path $deployDir 'data/stderr.log'
    $serviceProcess = Start-Process -FilePath $exePath -ArgumentList @('-config','config.json') -WorkingDirectory $deployDir -WindowStyle Hidden -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath -PassThru
    $startedAt = $serviceProcess.StartTime.ToUniversalTime().ToString('o')
    @{ process_id=$serviceProcess.Id; start_time=$startedAt } | ConvertTo-Json -Compress | Set-Content -LiteralPath $pidPath -Encoding UTF8
    Write-Lifecycle ('started PID={0}' -f $serviceProcess.Id)

    $activeProcess = $null
    for ($attempt=0; $attempt -lt 24; $attempt++) {
        Start-Sleep -Milliseconds 250
        $candidate = Get-Process -Id $serviceProcess.Id -ErrorAction SilentlyContinue
        if ($null -ne $candidate -and [string]::Equals($candidate.Path, $exePath, [StringComparison]::OrdinalIgnoreCase) -and $candidate.StartTime.ToUniversalTime().Ticks -eq ([DateTimeOffset]$startedAt).UtcDateTime.Ticks) {
            $activeProcess = $candidate
            break
        }
    }
    if ($null -eq $activeProcess) {
        Remove-Item -LiteralPath $pidPath -ErrorAction SilentlyContinue
        throw 'Service exited during startup; inspect data/stderr.log.'
    }
}

if ($null -ne $activeProcess) {
    Write-Host ('Service running. PID: ' + $activeProcess.Id)
    Write-Host 'Panel: http://127.0.0.1:7863/panel/'
    Write-Host 'API base URL: http://127.0.0.1:7863/v1'
} else {
    Write-Host 'Service is not running.'
}
