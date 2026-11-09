$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$compiler = Get-ChildItem 'C:/Program Files/Microsoft Visual Studio/2022/*/MSBuild/Current/Bin/Roslyn/csc.exe' | Select-Object -First 1
if (!$compiler) { throw 'Visual Studio 2022 C# compiler is required.' }
$output = Join-Path $root 'temp/tests'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$exe = Join-Path $output 'BotSafetyRegression.exe'
& $compiler.FullName /nologo /r:System.Drawing.dll /r:System.Windows.Forms.dll "/out:$exe" "/r:$root/Source/packages/log4net.3.5.0/lib/net462/log4net.dll" "$PSScriptRoot/BotSafetyRegression.cs" "$root/Source/FishingFunBot/Bot/BotWait.cs" "$root/Source/FishingFunBot/Bot/LootPosition.cs" "$root/Source/FishingFunBot/Bot/HumanTiming.cs" "$root/Source/FishingFunBot/Platform/WowProcess.cs" "$root/Source/FishingFunBot/Platform/Keys.cs"
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
Copy-Item -LiteralPath "$root/Source/packages/log4net.3.5.0/lib/net462/log4net.dll" -Destination $output -Force
& $exe
exit $LASTEXITCODE
