# Resolve paths from the checkout rather than the current working directory.
$RepositoryRoot = Split-Path -Parent $PSScriptRoot
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Get-PublishDirectory.ps1')
$exe = Join-Path (Get-PublishDirectory -RepositoryRoot $RepositoryRoot) 'GameLibrary.exe'
if (-not (Test-Path -LiteralPath $exe)) { & (Join-Path $RepositoryRoot 'scripts\build.ps1') -Publish }
Start-Process -FilePath $exe -WorkingDirectory $RepositoryRoot -WindowStyle Hidden
