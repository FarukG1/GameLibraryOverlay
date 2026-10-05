function Get-PublishDirectory {
    param([Parameter(Mandatory)][string]$RepositoryRoot)

    [xml]$properties = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'Directory.Build.props') -Raw
    $version = [string]$properties.Project.PropertyGroup.Version
    if ($version -notmatch '^\d{4}\.\d{2}\.[1-9]\d*$') {
        throw 'Directory.Build.props must contain a release version in YYYY.MM.X format.'
    }

    Join-Path $RepositoryRoot ("artifacts\GameLibraryOverlay_v" + $version)
}
