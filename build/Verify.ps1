param([switch]$IncludeFps)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
Push-Location $root
try {
    foreach ($project in @('Telemetry.Core.Tests','Telemetry.App.Tests')) {
        & $dotnet restore "tests/$project/$project.csproj" --locked-mode
        if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }
        & $dotnet test "tests/$project/$project.csproj" -c Release --no-restore
        if ($LASTEXITCODE -ne 0) { throw "$project failed." }
    }
    $exe = Join-Path $root 'src\Telemetry.App\bin\Release\net10.0-windows\Telemetry.exe'
    $running = Get-CimInstance Win32_Process -Filter "Name = 'Telemetry.exe'" | Where-Object { $_.CommandLine -notmatch '--collector|--frame-workload|--verify-fps' }
    if ($running) { throw 'Quit the running Telemetry UI before running native smoke verification.' }
    $report = Join-Path $root ('work\validation-' + [Guid]::NewGuid().ToString('N'))
    $process = Start-Process -FilePath $exe -ArgumentList '--smoke-test', ('"' + $report + '"') -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw 'Native smoke process failed.' }
    $smoke = Get-Content -Raw -LiteralPath (Join-Path $report 'smoke.json') | ConvertFrom-Json
    foreach ($name in @('trayVisible','overlayVisible','controlsVisible','hideWorks','showWorks','editReceivesInput','resizeWorks','positionRemembered','toggleHotkeyRegistered','editHotkeyRegistered','captureHotkeyRegistered','settingsHotkeyRegistered','clickThrough','noActivate')) {
        if ($smoke.$name -ne $true) { throw "Native verification failed: $name" }
    }
    if ($smoke.displayChecks.Count -ne $smoke.monitorCount) { throw 'Not every attached display was checked.' }
    $picker = Get-Content -Raw -LiteralPath (Join-Path $report 'picker.json') | ConvertFrom-Json
    if (!$picker.captionValid) { throw 'The app picker did not render its label with bounded ellipsis.' }
    foreach ($display in $smoke.displayChecks) {
        foreach ($name in @('placedOnDisplay','resizePersisted','positionRestored','visibleAfterToggle','displayRemembered')) {
            if ($display.$name -ne $true) { throw "Display verification failed: $name" }
        }
    }
    if ($IncludeFps) {
        Write-Host 'Approve the Windows prompt for the Telemetry collector. The DirectX test window closes automatically.'
        $process = Start-Process -FilePath $exe -ArgumentList '--verify-fps', ('"' + $report + '"') -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw "FPS verification did not pass; inspect $report" }
    }
    Write-Host "Verification passed. Reports and renders: $report"
} finally { Pop-Location }
