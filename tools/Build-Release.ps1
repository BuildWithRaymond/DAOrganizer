param([string]$Output='artifacts\release',[switch]$SkipPublish)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    [xml]$project=Get-Content src\DAOrganizer.App\DAOrganizer.App.csproj
    $version=$project.Project.PropertyGroup.Version
    $package='artifacts\release-package-'+$version
    if (-not $SkipPublish) {
        if (Test-Path -LiteralPath $package) { throw "Use a clean package directory: $package already exists." }
        & ./tools/Package.ps1 -Output $package
    }
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'Release tool restore failed.' }
    dotnet tool run vpk -- pack --packId BuildWithRaymond.DAOrganizer --packTitle DAOrganizer --packVersion $version --packDir $package --mainExe DAOrganizer.exe --runtime win-x64 --outputDir $Output --releaseNotes "docs/releases/v$version.md" --shortcuts StartMenuRoot --delta None
    if ($LASTEXITCODE -ne 0) { throw 'Velopack packaging failed.' }
    $releasePath=Join-Path $projectRoot $Output
    foreach ($suffix in @('Setup.exe','Portable.zip')) {
        Move-Item -LiteralPath (Join-Path $releasePath ('BuildWithRaymond.DAOrganizer-win-'+$suffix)) -Destination (Join-Path $releasePath ('DAOrganizer-win-'+$suffix))
    }
    $assetIndexPath=Join-Path $releasePath 'assets.win.json'
    $assetIndex=[IO.File]::ReadAllText($assetIndexPath).Replace('BuildWithRaymond.DAOrganizer-win-Setup.exe','DAOrganizer-win-Setup.exe').Replace('BuildWithRaymond.DAOrganizer-win-Portable.zip','DAOrganizer-win-Portable.zip')
    [IO.File]::WriteAllText($assetIndexPath,$assetIndex,[Text.UTF8Encoding]::new($false))
    $rows=Get-ChildItem -LiteralPath $releasePath -File | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object Name | ForEach-Object {
        (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+$_.Name
    }
    $rows | Set-Content -LiteralPath (Join-Path $releasePath 'SHA256SUMS.txt') -Encoding ASCII
    & ./tools/Verify-Release.ps1 -Output $Output
    Write-Output "Release $version ready: $releasePath"
}
finally { Pop-Location }
