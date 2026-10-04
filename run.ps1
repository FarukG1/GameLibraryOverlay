$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'artifacts\app\GameLibrary.exe'
if (-not (Test-Path -LiteralPath $exe)) { & (Join-Path $PSScriptRoot 'build.ps1') -Publish }
Start-Process -FilePath $exe -WorkingDirectory $PSScriptRoot -WindowStyle Hidden
