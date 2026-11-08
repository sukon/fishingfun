$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$compiler = Get-ChildItem 'C:/Program Files/Microsoft Visual Studio/2022/*/MSBuild/Current/Bin/Roslyn/csc.exe' | Select-Object -First 1
if (!$compiler) { throw 'Visual Studio 2022 C# compiler is required.' }
$output = Join-Path $root 'temp/tests'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$exe = Join-Path $output 'BiteWatcherRegression.exe'
& $compiler.FullName /nologo /unsafe /r:System.Drawing.dll "/out:$exe" "$PSScriptRoot/BiteWatcherRegression.cs" "$root/Source/FishingFunBot/Bot/PositionBiteWatcher.cs" "$root/Source/FishingFunBot/Bot/BobberObservation.cs" "$root/Source/FishingFunBot/Bot/BobberTraceRecorder.cs" "$root/Source/FishingFunBot/Bot/BobberDetector.cs" "$root/Source/FishingFunBot/Bot/BobberTracker.cs" "$root/Source/FishingFunBot/Bot/BotConfig.cs" "$root/Source/FishingFunBot/Bot/PixelClassifier.cs" "$root/Source/FishingFunBot/Bot/Interfaces/IPixelClassifier.cs" "$root/Source/FishingFunBot/Bot/Interfaces/IBiteWatcher.cs" "$root/Source/FishingFunBot/Bot/Interfaces/IImageProvider.cs"
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& $exe "$PSScriptRoot/fixtures" "$output/traces" $exe
exit $LASTEXITCODE
