param([switch]$SkipBuild,[string]$Output='artifacts\DAOrganizer')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$publishPath=Join-Path $projectRoot $Output
if (-not $SkipBuild) {
    $env:AVALONIA_TELEMETRY_OPTOUT='1'
    dotnet publish (Join-Path $projectRoot 'src\DAOrganizer.App') -c Release -r win-x64 --self-contained true -m:1 -p:UsedAvaloniaProducts= -o $publishPath
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
}
$licensePath=Join-Path $publishPath 'licenses'
New-Item -ItemType Directory -Force -Path $licensePath | Out-Null
foreach ($name in @('README.md','LICENSE','CHANGELOG.md','CONTRIBUTING.md','SECURITY.md','CODE_OF_CONDUCT.md','THIRD_PARTY_NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $publishPath
}
$docsPath=Join-Path $publishPath 'docs'
New-Item -ItemType Directory -Force -Path $docsPath | Out-Null
Get-ChildItem -LiteralPath (Join-Path $projectRoot 'docs') | Copy-Item -Destination $docsPath -Recurse -Force
Get-ChildItem -LiteralPath (Join-Path $projectRoot 'docs\licenses') -File | Copy-Item -Destination $licensePath
$assets=Get-Content (Join-Path $projectRoot 'src\DAOrganizer.App\obj\project.assets.json') -Raw | ConvertFrom-Json
$packageRoots=@($assets.packageFolders.PSObject.Properties.Name)
function Resolve-PackagePath([string]$key) {
    foreach ($root in $packageRoots) {
        $candidate=Join-Path $root $key
        if (Test-Path -LiteralPath $candidate -PathType Container) { return $candidate }
    }
    throw "Restored package not found: $key"
}
$rows=@()
foreach ($property in $assets.libraries.PSObject.Properties) {
    if ($property.Value.type -ne 'package') { continue }
    $key=$property.Name.ToLowerInvariant()
    $packagePath=Resolve-PackagePath $key
    $packageId=$key.Split('/')[0]
    $nuspec=Get-ChildItem -LiteralPath $packagePath -Filter '*.nuspec' | Select-Object -First 1
    [xml]$metadata=Get-Content -LiteralPath $nuspec.FullName
    $record=$metadata.package.metadata
    $rows += "$($property.Name) | $($record.license.'#text') | $($record.copyright)"
    foreach ($file in Get-ChildItem -LiteralPath $packagePath -File | Where-Object { $_.Name -match 'LICENSE|NOTICE' }) {
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $licensePath ($packageId+'-'+$file.Name))
    }
}
$rows | Set-Content -LiteralPath (Join-Path $licensePath 'PACKAGES.txt') -Encoding UTF8
$runtime=$assets.project.frameworks.PSObject.Properties.Value.downloadDependencies | Where-Object { $_.name -eq 'Microsoft.NETCore.App.Runtime.win-x64' } | Select-Object -First 1
if (-not $runtime) { throw 'No restored Windows runtime found. Run Package.ps1 without -SkipBuild.' }
$runtimeVersion=$runtime.version.Trim('[',']').Split(',')[0].Trim()
$runtimePath=Resolve-PackagePath ('microsoft.netcore.app.runtime.win-x64/'+$runtimeVersion)
foreach ($file in Get-ChildItem -LiteralPath $runtimePath -File | Where-Object { $_.Name -match 'LICENSE|NOTICE' }) {
    Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $licensePath ('dotnet-'+$file.Name))
}
Write-Output "Package ready: $publishPath"
