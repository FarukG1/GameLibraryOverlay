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
    # Remove old multi-file output before switching to the bundled release layout.
    $artifactRoot = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot 'artifacts'))
    $publishDirectory = [IO.Path]::GetFullPath($publishDirectory)
    if (-not $publishDirectory.StartsWith($artifactRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Publish directory must be inside artifacts.'
    }
    $active = Get-Process GameLibrary -ErrorAction SilentlyContinue | Where-Object {
        $_.Path -and $_.Path.StartsWith($publishDirectory + '\', [StringComparison]::OrdinalIgnoreCase)
    }
    if ($active) { throw 'Exit the published app before publishing this version again.' }
    if (Test-Path -LiteralPath $publishDirectory) {
        if ((Get-Item -LiteralPath $publishDirectory).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw 'Publish directory must not be a link.'
        }
        Remove-Item -LiteralPath $publishDirectory -Recurse -Force
    }
    & $sdk publish src/GameLibrary.App -c Release -r win-x64 --self-contained false -o $publishDirectory --nologo -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:DebugSymbols=false -p:RestoreConfigFile="$RepositoryRoot\NuGet.Config"
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    # Archive the directory contents so GameLibrary.exe is at the ZIP root.
    $archivePath = $publishDirectory + '.zip'
    Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $archivePath -Force
    $checksumPath = $archivePath + '.sha256'
    $checksum = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksumLine = $checksum + '  ' + [IO.Path]::GetFileName($archivePath)
    [IO.File]::WriteAllText($checksumPath, $checksumLine + "`n", [Text.Encoding]::ASCII)
    Write-Output "Release archive: $archivePath"
    Write-Output "SHA-256 checksum: $checksumPath"
}
