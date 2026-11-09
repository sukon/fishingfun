# Tool Registry

## Capability Index

| Capability | Tool / Procedure | Path / Command | Notes |
| --- | --- | --- | --- |
| Bite detection regression | Position replay | `tests/Run-BiteWatcherRegression.ps1` | No game or restored packages required |
| Disk write reduction regression | Settings and log behaviour | `tests/Run-WriteReductionRegression.ps1` | No game or restored packages required |
| Shared front end settings | Settings file round trip | `tests/Run-WriteReductionRegression.ps1` | The window and console front ends read the same file |
| Observation capture and replay | Per-cast CSV and offline watcher replay | `tools/Replay-BiteTrace.ps1`, `tools/BobberReplay.cs` | Position, size, area, missing frames and monotonic time |
| Raw image capture and inspection | Live/offline detector diagnostic | `tools/Inspect-Bobber.ps1`, `tools/BobberDiagnostic.cs` | Read-only capture; raw pixels, overlays and rejection report |
| Release build | Restore packages and MSBuild | `tools/Build-Release.ps1` | Downloads project-local .NET Framework 4.7.2 reference assemblies |

## Tools and Procedures

### Bite detection regression

**Purpose:** Verify the complete image-to-bite path on eight real splash images, sparse/small acquisition confirmation, tracking through missed frames, and per-cast size/motion calibration. Position cases cover different sampling intervals, drift, missing/stale observations, reset and recording/replay. Initial settling is ignored until at least six visible observations spanning one second are available. Replay the two premature-strike position sequences recorded on 2026-11-08 at 21:19:42 and 21:19:54; these original logs did not include sizes, so those CSV fixtures explicitly use unknown size (zero).

**Path / command:** `tests/Run-BiteWatcherRegression.ps1`

**Use when:** Changing position-based bite detection.

**Usage:** `powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-BiteWatcherRegression.ps1`

**Inputs / arguments:** None; compiles the production tracker, detector, observation, watcher, recorder and interfaces with a test-only logging stub. Fixtures live under `tests/fixtures/`; image provenance and deterministic replay timing are documented in `tests/fixtures/images/README.md`. Size variants remain generated scenarios, separate from the real splash images and logged no-bite positions.

**Outputs:** PASS/FAIL lines and nonzero exit on failure. Generated executable: `temp/tests/BiteWatcherRegression.exe`; recorder round-trip CSVs under `temp/tests/traces/`.

**Requirements:** Windows .NET Framework and Visual Studio 2022 C# compiler.

**Notes / constraints:** Tests image detection and bite decisions; mouse delivery and successful automatic collection require in-game verification.

### Observation capture and replay

**Purpose:** Capture each cast's measured observations automatically, then run the production bite watcher offline against exactly those sizes and time intervals.

**Path / command:** `tools/Replay-BiteTrace.ps1` compiles `tools/BobberReplay.cs` against the current Release bot DLL.

**Use when:** Comparing small/large float behavior or diagnosing a missed/premature strike without repeatedly interacting with the game.

**Usage:**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-Release.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Replay-BiteTrace.ps1 -Trace tests/fixtures/logged-no-bite-211942.csv -ExpectedBiteAtMs -1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Replay-BiteTrace.ps1 -Trace Source/bin/Release/temp/bite-traces/<capture>.csv
```

**Inputs / arguments:** `-Trace` is a single-cast CSV. Optional `-ExpectedBiteAtMs` asserts the first bite time exactly; `-1` asserts no bite. Header: `elapsed_ms,x,y,width,height,area,decision`. First row uses `reset`, others `watch` or `bite`. Times are monotonic milliseconds relative to the first observation; position is screen pixels. Missing observations have zero position/size/area; zero size with a nonzero position means an older/legacy finder cannot measure size. `decision` preserves the original software decision, not human-labeled ground truth.

**Outputs:** Live recordings go under `<application directory>/temp/bite-traces/`, normally `Source/bin/Release/temp/bite-traces/`, one timestamped CSV per cast. Each row flushes so the active file can be inspected. The replay prints first bite time and final calibration. Generated executable/dependencies go under `temp/replay/`. Exit codes: 0 completed/matched expectation, 1 expectation mismatch, 2 invalid input/runtime error.

**Requirements:** A current Release build, Windows .NET Framework, Visual Studio 2022 C# compiler. No game is needed for replay.

**Notes / constraints:** CSV captures contain observations, not raw image frames; use the image diagnostic below for detector failures. Recordings are retained for comparison and can be removed manually. Capture failure logs a message and does not stop fishing. Rebuild Release before replaying code changes. Gaps of one to two seconds preserve existing calibration but reappearance does not cause a strike. Gaps over two seconds or a large horizontal jump restart calibration.

### Raw image capture and inspection

**Purpose:** Inspect which real image shapes are rejected and preserve exact source pixels for regression. This promotes and extends the pre-existing disposable `temp/DiagBobber/Program.cs` diagnostic into the canonical tool location.

**Path / command:** `tools/Inspect-Bobber.ps1`, compiling `tools/BobberDiagnostic.cs` and loading the current Release bot DLL. Uses the same detector and tracker as the application.

**Use when:** The float is visibly present but the bot loses it, especially around water/splash or different cast distances.

**Usage:**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Inspect-Bobber.ps1 -Output temp/bobber-diagnostic/cast1 -Seconds 25 -IntervalMs 200
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Inspect-Bobber.ps1 -Output temp/bobber-diagnostic/offline -Image temp/bobber-diagnostic/cast1/frame1-window.png
```

**Inputs / arguments:** `-Output`: repository-relative output directory, preferably a new directory under `temp/`. `-Seconds`: 0.1–60 capture duration. `-IntervalMs`: 50–2000 pause between analyses; PNG encoding and analysis add to the real sampling interval. Optional `-Image`: full game-window screenshot; the tool applies the same central-half crop as the application. A single offline image cannot establish a three-frame tentative track.

**Outputs:** Raw full-window images (`frameN-window.png`), unmodified detection crops (`frameN-raw.png`), matched-pixel overlays (`frameN-matched.png`), representative overlays, and `diag-report.txt` under `-Output`. Generated tool/dependencies go under `temp/diagnostic-tool/`. It never presses game keys or clicks the mouse.

**Requirements:** A current Release build, Windows desktop/game visible for live capture, .NET Framework and Visual Studio 2022 C# compiler. For live capture, cast manually and leave the game window unobscured.

**Notes / constraints:** The report includes raw candidate rejection details; a sparse candidate may be pending temporal confirmation even if its geometry is acceptable. Raw image captures are larger than CSV traces. This is a detector diagnostic, not an automatic fishing run.

### Shared front end settings

**Purpose:** Keep the window front end and the console front end behaving the same way.

**Path / command:** `Source/FishingFunBot/Bot/FishingFunSettings.cs`; file `fishingfun.settings` next to the program.

**Use when:** Changing which key casts the float, the colour rule, or the keys that run the bait macro.

**Usage:** The window front end writes the file when the cast key or the colour sliders change. The console front end reads it at startup. Both write a `Settings: ...` line to the log at startup so the two can be compared.

**Inputs / arguments:** `cast_key`, `colour_multiplier`, `colour_closeness_multiplier`, `ten_minute_keys`. The older `keybind.txt` is still read for the cast key.

**Outputs:** `fishingfun.settings` next to the program.

**Requirements:** None beyond the program itself.

**Notes / constraints:** The file is written when a slider is released, not on every step, so dragging it does not keep writing to the disk. The colour settings were previously only held in memory, which meant the console front end could never be given the same colour rule as the window front end.

### Release build

**Purpose:** Compile the UI, console, and bot projects.

**Path / command:** `tools/Build-Release.ps1`, using Visual Studio MSBuild.

**Use when:** Producing updated application binaries.

**Usage:** Run `powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-Release.ps1` from the repository root.

**Inputs / arguments:** No arguments. Restores the versions in each project's `packages.config` under `Source/packages`, plus `Microsoft.NETFramework.ReferenceAssemblies.net472` version `1.0.3` under `temp/build-packages`. Passes its `build` directory as `TargetFrameworkRootPath` to MSBuild.

**Outputs:** Application binaries under `Source/bin/Release`; normal MSBuild intermediate output under project `obj` directories; downloaded archives and reference assemblies under `temp/build-packages`.

**Requirements:** Visual Studio 2022 MSBuild, Windows .NET Framework, and HTTPS access to `api.nuget.org` for packages not already restored. No system-wide targeting-pack installation is required.

**Notes / constraints:** The script stops on download or build failure. If NuGet DNS resolution fails, restore network access before retrying. With a system-wide targeting pack and packages already installed, the original developer-shell command also works: `MSBuild Source/FishingFun.sln /t:Build /p:Configuration=Release /verbosity:minimal /nologo`. Position regression tests can run independently using the installed Framework runtime.

### Disk write settings

**Purpose:** Decide how much the bot writes to disk, so a machine that fishes all day does not keep the drive busy.

**Path / command:** `Source/FishingFunBot/Bot/BotConfig.cs` and `LoggingSetup.cs`; sample settings file `fishingfun.config.sample`.

**Use when:** The log file grows quickly, the drive should be left alone, or the float observation recording is not wanted.

**Usage:** Copy `fishingfun.config.sample` next to the program as `fishingfun.config`, or set environment variables, which take priority over the file:

```bat
set FISHINGFUN_LOG_FILE_THRESHOLD=WARN
set FISHINGFUN_RECORD_FLOAT_TRACES=1
```

**Inputs / arguments:** `log_to_file`, `log_file_threshold`, `log_file_immediate_flush`, `log_file_max_size_mb`, `log_file_backups`, `record_float_traces`. A value that cannot be understood is ignored and the default is kept.

**Outputs:** `myapp.log` next to the program, and the float observation recordings under `temp/bite-traces/` when they are switched on. The user interface and the console still show every message, whatever the file is set to.

**Requirements:** None beyond the program itself.

**Notes / constraints:** The log file only keeps the levels named by `log_file_threshold`, so the per frame messages stay on screen but do not reach the disk. `log_file_immediate_flush=false` batches the writes, which is gentler on the drive, but the tail of the log is then only written when the program closes the file, so a program that is killed loses that last part; it is on by default for that reason. **The float observation recording is off by default**, which is a change from earlier versions: it created a file for every cast and wrote a row for every frame. Set `record_float_traces=true` to bring it back; existing recordings are not affected.

### Disk write reduction regression

**Purpose:** Check that the disk write settings behave as described.

**Path / command:** `tests/Run-WriteReductionRegression.ps1`

**Use when:** Changing `BotConfig` or `LoggingSetup`.

**Usage:** `powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-WriteReductionRegression.ps1`

**Inputs / arguments:** None.

**Outputs:** PASS/FAIL lines and nonzero exit on failure.

**Requirements:** Windows .NET Framework and Visual Studio 2022 C# compiler.

**Notes / constraints:** Checks that switching file logging off creates no file at all, that the threshold applies to the file but not to the screen, that a normal exit keeps the last lines, that the log rolls over, and that the environment beats the settings file while a bad value is ignored.
