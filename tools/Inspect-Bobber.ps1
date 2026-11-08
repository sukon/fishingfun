param(
    [string]$Output = 'temp/bobber-diagnostic',
    [ValidateRange(0.1,60)][double]$Seconds = 2,
    [ValidateRange(50,2000)][int]$IntervalMs = 200,
    [string]$Image
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$compiler = Get-ChildItem 'C:/Program Files/Microsoft Visual Studio/2022/*/MSBuild/Current/Bin/Roslyn/csc.exe' | Select-Object -First 1
if (!$compiler) { throw 'Visual Studio 2022 C# compiler is required.' }
$outputPath = [IO.Path]::GetFullPath((Join-Path $root $Output))
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
$toolOutput = Join-Path $root 'temp/diagnostic-tool'
New-Item -ItemType Directory -Force -Path $toolOutput | Out-Null
$exe = Join-Path $toolOutput 'BobberDiagnostic.exe'
& $compiler.FullName /nologo /r:System.Drawing.dll /r:System.Windows.Forms.dll "/out:$exe" "$PSScriptRoot/BobberDiagnostic.cs"
if ($LASTEXITCODE -ne 0) { throw 'Diagnostic compilation failed.' }
Copy-Item -LiteralPath "$root/Source/bin/Release/FishingFunBot.dll","$root/Source/bin/Release/log4net.dll" -Destination $toolOutput -Force
if ($Image) { & $exe $outputPath $Seconds $IntervalMs (Resolve-Path -LiteralPath $Image).Path }
else { & $exe $outputPath $Seconds $IntervalMs }
exit $LASTEXITCODE
