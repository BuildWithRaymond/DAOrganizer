param([string]$Output='artifacts\release')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$releasePath=Join-Path $projectRoot $Output
[xml]$project=Get-Content (Join-Path $projectRoot 'src\DAOrganizer.App\DAOrganizer.App.csproj')
$version=$project.Project.PropertyGroup.Version
$checksums=Get-Content -LiteralPath (Join-Path $releasePath 'SHA256SUMS.txt')
$names=@()
foreach ($line in $checksums) {
    if ($line -notmatch '^([a-f0-9]{64})  ([^/\\]+)$') { throw 'Malformed release checksum entry.' }
    $expected=$Matches[1];$name=$Matches[2]
    if ($name -in $names -or $name -eq 'SHA256SUMS.txt') { throw 'Duplicate or recursive release checksum entry.' }
    $names+=$name
    $file=Join-Path $releasePath $name
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw "Release checksum failed: $name" }
}
$actual=@(Get-ChildItem -LiteralPath $releasePath -File | Where-Object Name -ne 'SHA256SUMS.txt' | ForEach-Object Name)
if (@(Compare-Object $names $actual).Count -ne 0) { throw 'Release has missing or unchecked assets.' }
foreach ($required in @('DAOrganizer-win-Setup.exe','DAOrganizer-win-Portable.zip','releases.win.json')) {
    if ($required -notin $names) { throw "Missing release asset: $required" }
}
$assetIndex=Get-Content -LiteralPath (Join-Path $releasePath 'assets.win.json') -Raw | ConvertFrom-Json
if ($assetIndex | Where-Object { $_.RelativeFileName -notin $names }) { throw 'Asset index refers to missing release downloads.' }
$feed=Get-Content -LiteralPath (Join-Path $releasePath 'releases.win.json') -Raw | ConvertFrom-Json
$full=@($feed.Assets | Where-Object Type -eq 'Full')
if ($full.Count -ne 1 -or $full[0].Version -ne $version -or $full[0].PackageId -ne 'BuildWithRaymond.DAOrganizer') { throw 'Update feed version or application identity mismatch.' }
$asset=$full[0]
if ($asset.FileName -notin $names) { throw 'Update feed package missing.' }
$package=Get-Item -LiteralPath (Join-Path $releasePath $asset.FileName)
if ($package.Length -ne $asset.Size -or (Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash -ne $asset.SHA256) { throw 'Update feed integrity mismatch.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[IO.Compression.ZipFile]::OpenRead((Join-Path $releasePath 'DAOrganizer-win-Portable.zip'))
try {
    $entries=@($archive.Entries.FullName)
    if ($entries -notcontains 'current/DAOrganizer.exe' -or $entries -notcontains 'current/DAOrganizer.Core.dll' -or $entries -notcontains 'current/docs/GETTING_STARTED.md' -or -not ($entries | Where-Object { $_ -match '^current/licenses/' })) { throw 'Portable archive is incomplete.' }
    $manifestReader=[IO.StreamReader]::new($archive.GetEntry('current/sq.version').Open())
    try { [xml]$manifest=$manifestReader.ReadToEnd() } finally { $manifestReader.Dispose() }
    if ($manifest.package.metadata.version -ne $version -or $manifest.package.metadata.rid -ne 'win-x64' -or $manifest.package.metadata.machineArchitecture -ne 'x64') { throw 'Portable version or architecture mismatch.' }
    if ($entries | Where-Object { $_ -match '(?i)(\.db($|-)|\.sqlite3?($|-)|\.log$|\.pdb$|(^|/)(profiles|handoffs|plans|protocol-captures|test-results)/)' }) { throw 'Portable archive contains private or development files.' }
}
finally { $archive.Dispose() }
Write-Output "Verified $version release checksums, update feed and portable contents."
