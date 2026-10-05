param([switch]$Test, [switch]$Publish)
# Resolve paths from the checkout rather than the current working directory.
$RepositoryRoot = Split-Path -Parent $PSScriptRoot
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $RepositoryRoot
$env:DOTNET_CLI_HOME = Join-Path $RepositoryRoot '.tools\dotnet'
$env:NUGET_PACKAGES = Join-Path $RepositoryRoot '.tools\packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$sdk = 'C:\Program Files\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $sdk)) { $sdk = 'dotnet' }
& $sdk build GameLibrary.slnx -c Release --nologo -p:RestoreConfigFile="$RepositoryRoot\NuGet.Config"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
if ($Test) {
    & $sdk run --project tests/GameLibrary.Tests -c Release --no-build -- "$RepositoryRoot\artifacts\test-data" "$RepositoryRoot\tests\TestGame\bin\Release\net10.0-windows\TestGame.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
if ($Publish) {
    . (Join-Path $PSScriptRoot 'Get-PublishDirectory.ps1')
    $publishDirectory = Get-PublishDirectory -RepositoryRoot $RepositoryRoot
    & $sdk publish src/GameLibrary.App -c Release --no-restore --self-contained false -o $publishDirectory --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    # Archive the directory contents so GameLibrary.exe is at the ZIP root.
    $archivePath = $publishDirectory + '.zip'
    Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $archivePath -Force
    Write-Output "Release archive: $archivePath"
}
