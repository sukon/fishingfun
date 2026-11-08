$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$compiler = Get-ChildItem 'C:/Program Files/Microsoft Visual Studio/2022/*/MSBuild/Current/Bin/Roslyn/csc.exe' | Select-Object -First 1
if (!$compiler) { throw 'Visual Studio 2022 C# compiler is required.' }
$output = Join-Path $root 'temp/tests'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$exe = Join-Path $output 'BiteWatcherRegression.exe'
& $compiler.FullName /nologo /r:System.Drawing.dll "/out:$exe" "$PSScriptRoot/BiteWatcherRegression.cs" "$root/Source/FishingFunBot/Bot/PositionBiteWatcher.cs" "$root/Source/FishingFunBot/Bot/Interfaces/IBiteWatcher.cs" "$root/Source/FishingFunBot/Bot/Interfaces/IImageProvider.cs"
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& $exe
exit $LASTEXITCODE
