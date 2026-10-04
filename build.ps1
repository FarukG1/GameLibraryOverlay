param([switch]$Test, [switch]$Publish)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools\dotnet'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.tools\packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$sdk = 'C:\Program Files\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $sdk)) { $sdk = 'dotnet' }
& $sdk build GameLibrary.slnx -c Release --nologo -p:RestoreConfigFile="$PSScriptRoot\NuGet.Config"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
if ($Test) {
    & $sdk run --project tests/GameLibrary.Tests -c Release --no-build -- "$PSScriptRoot\artifacts\test-data" "$PSScriptRoot\tests\TestGame\bin\Release\net10.0-windows\TestGame.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
if ($Publish) {
    & $sdk publish src/GameLibrary.App -c Release --no-restore --self-contained false -o "$PSScriptRoot\artifacts\app" --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
}
