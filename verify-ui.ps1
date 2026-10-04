$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$exe = Join-Path $PSScriptRoot 'artifacts\app\GameLibrary.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Run .\build.ps1 -Test -Publish first.' }
if (Get-Process -Name GameLibrary -ErrorAction SilentlyContinue) { throw 'Exit Game Library from its tray menu before UI verification.' }
foreach ($mode in @('local', 'stress', 'idle')) {
    $dataPath = Join-Path $PSScriptRoot ('artifacts\verify-' + $mode)
    $launchArgs = @('--data-dir', ('"' + $dataPath + '"'))
    if ($mode -eq 'idle') { $launchArgs += @('--background', '--idle-diagnostics') }
    else { $launchArgs += '--smoke-test' }
    if ($mode -eq 'stress') { $launchArgs += '--stress' }
    $process = Start-Process -FilePath $exe -ArgumentList $launchArgs -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(30000)) {
        Stop-Process -Id $process.Id -Force
        throw "$mode verification timed out. See $dataPath\app.log"
    }
    if ($process.ExitCode -ne 0) { throw "$mode verification failed. See $dataPath\app.log" }
    $resultName = if ($mode -eq 'idle') { 'idle-result.json' } else { 'smoke-result.json' }
    Write-Output "$mode verification:"
    Get-Content -LiteralPath (Join-Path $dataPath $resultName)
}
