# Tool Registry

## Capability Index

| Capability | Tool / Procedure | Path / Command | Notes |
| --- | --- | --- | --- |
| Bite detection regression | Position replay | `tests/Run-BiteWatcherRegression.ps1` | No game or restored packages required |
| Release build | Restore packages and MSBuild | `tools/Build-Release.ps1` | Downloads project-local .NET Framework 4.7.2 reference assemblies |

## Tools and Procedures

### Bite detection regression

**Purpose:** Verify that a seven-pixel bite triggers reel-in, including immediately after reset and after drift. Replay the two premature-strike position sequences recorded on 2026-11-08 at 21:19:42 and 21:19:54, and verify steady two-, three-, and four-pixel drift does not trigger reel-in.

**Path / command:** `tests/Run-BiteWatcherRegression.ps1`

**Use when:** Changing position-based bite detection.

**Usage:** `powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-BiteWatcherRegression.ps1`

**Inputs / arguments:** None; compiles the production watcher and event interfaces with a test-only logging stub.

**Outputs:** PASS/FAIL lines and nonzero exit on failure. Generated executable: `temp/tests/BiteWatcherRegression.exe`.

**Requirements:** Windows .NET Framework and Visual Studio 2022 C# compiler.

**Notes / constraints:** Tests detection logic only; mouse delivery and live game behavior require in-game verification.

### Release build

**Purpose:** Compile the UI, console, and bot projects.

**Path / command:** `tools/Build-Release.ps1`, using Visual Studio MSBuild.

**Use when:** Producing updated application binaries.

**Usage:** Run `powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-Release.ps1` from the repository root.

**Inputs / arguments:** No arguments. Restores the versions in each project's `packages.config` under `Source/packages`, plus `Microsoft.NETFramework.ReferenceAssemblies.net472` version `1.0.3` under `temp/build-packages`. Passes its `build` directory as `TargetFrameworkRootPath` to MSBuild.

**Outputs:** Application binaries under `Source/bin/Release`; normal MSBuild intermediate output under project `obj` directories; downloaded archives and reference assemblies under `temp/build-packages`.

**Requirements:** Visual Studio 2022 MSBuild, Windows .NET Framework, and HTTPS access to `api.nuget.org` for packages not already restored. No system-wide targeting-pack installation is required.

**Notes / constraints:** The script stops on download or build failure. If NuGet DNS resolution fails, restore network access before retrying. With a system-wide targeting pack and packages already installed, the original developer-shell command also works: `MSBuild Source/FishingFun.sln /t:Build /p:Configuration=Release /verbosity:minimal /nologo`. Position regression tests can run independently using the installed Framework runtime.
