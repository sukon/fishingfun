param(
    [Parameter(Mandatory = $true)][string]$Trace,
    [Nullable[long]]$ExpectedBiteAtMs
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$compiler = Get-ChildItem 'C:/Program Files/Microsoft Visual Studio/2022/*/MSBuild/Current/Bin/Roslyn/csc.exe' | Select-Object -First 1
if (!$compiler) { throw 'Visual Studio 2022 C# compiler is required.' }
$bot = Join-Path $root 'Source/bin/Release/FishingFunBot.dll'
$log = Join-Path $root 'Source/bin/Release/log4net.dll'
if (!(Test-Path $bot) -or !(Test-Path $log)) { throw 'Run tools/Build-Release.ps1 first.' }
$tracePath = (Resolve-Path -LiteralPath $Trace).Path
$output = Join-Path $root 'temp/replay'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$exe = Join-Path $output 'BobberReplay.exe'
& $compiler.FullName /nologo /r:System.Drawing.dll "/r:$bot" "/out:$exe" "$PSScriptRoot/BobberReplay.cs"
if ($LASTEXITCODE -ne 0) { throw 'Replay compilation failed.' }
Copy-Item -LiteralPath $bot,$log -Destination $output -Force
if ($null -ne $ExpectedBiteAtMs) { & $exe $tracePath ([string]$ExpectedBiteAtMs) }
else { & $exe $tracePath }
exit $LASTEXITCODE
