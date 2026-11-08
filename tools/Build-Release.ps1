$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$msbuild = Get-ChildItem 'C:/Program Files/Microsoft Visual Studio/2022/*/MSBuild/Current/Bin/MSBuild.exe' | Select-Object -First 1
if (!$msbuild) { throw 'Visual Studio 2022 MSBuild is required.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$cache = Join-Path $root 'temp/build-packages'
New-Item -ItemType Directory -Force -Path $cache | Out-Null

function Restore-Package([string]$Id, [string]$Version, [string]$Destination) {
    if (Test-Path (Join-Path $Destination "$Id.nuspec")) { return }
    $archive = Join-Path $cache "$Id.$Version.nupkg"
    $url = 'https://api.nuget.org/v3-flatcontainer/{0}/{1}/{0}.{1}.nupkg' -f $Id.ToLowerInvariant(), $Version.ToLowerInvariant()
    Write-Host "Restoring $Id $Version"
    Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $archive
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    [IO.Compression.ZipFile]::ExtractToDirectory($archive, $Destination)
}

$referenceId = 'Microsoft.NETFramework.ReferenceAssemblies.net472'
$referencePath = Join-Path $cache "$referenceId.1.0.3"
Restore-Package $referenceId '1.0.3' $referencePath
$packages = Get-ChildItem (Join-Path $root 'Source') -Recurse -Filter packages.config |
    ForEach-Object { ([xml](Get-Content $_.FullName)).packages.package } |
    Sort-Object id, version -Unique
foreach ($package in $packages) {
    $destination = Join-Path $root ("Source/packages/{0}.{1}" -f $package.id, $package.version)
    Restore-Package $package.id $package.version $destination
}

$frameworkRoot = Join-Path $referencePath 'build'
& $msbuild.FullName (Join-Path $root 'Source/FishingFun.sln') /t:Build /p:Configuration=Release "/p:TargetFrameworkRootPath=$frameworkRoot" /verbosity:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw "Release build failed (exit $LASTEXITCODE)." }
Write-Host "Release output: $root/Source/bin/Release"
