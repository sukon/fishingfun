# 游標預先就位 實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 讓機器人像真人一樣，拋竿後延遲一段隨機時間把游標移到浮標上並留在那裡，魚咬鉤時只需右鍵，把點擊延遲由 1700–2120ms 降到 800–1420ms。

**Architecture:** 新增純邏輯類別 `FloatCursor`（不碰 Win32）承載「每輪只把游標放一次」的規則；`HumanTiming` 新增兩個計時方法（拋竿後的游標移動延遲、右鍵前的隨機等待）；`WowProcess` 新增 `MoveCursorOntoFloat` 與 `RestoreCursor`；`FishBot` 把原本固定的 `Watch(2000)` 換成隨機延遲，並在觀察迴圈中把游標放到浮標上。既有 `LootPosition.Choose` 的點擊前修正邏輯不動。

**Tech Stack:** C# 8 / .NET Framework 4.7.2、Visual Studio 2022 MSBuild、log4net 3.5.0、PowerShell 測試腳本。

**設計文件:** `docs/superpowers/specs/2026-11-22-cursor-pre-positioning-design.md`

## Global Constraints

- 目標框架：.NET Framework 4.7.2，`LangVersion` 8.0，`#nullable enable` 的新檔案必須宣告。
- 不得引入新的 NuGet 套件或第三方相依。
- 程式碼中的註解一律使用英文，與現有 `Source/` 內的程式碼風格一致；文件與本計畫使用繁體中文。
- 所有輸入必須走既有的 `WowProcess.RunUnlessCancelled`（或 `lock (InputLock)`），不得繞過輸入鎖。
- 不得改動浮標偵測、咬鉤判定、每輪校準的邏輯（`BobberDetector`、`BobberTracker`、`PositionBiteWatcher`）。
- 每一個任務結束時，該任務相關的回歸測試必須 ALL PASS，且工作區乾淨後才 commit。
- 提交訊息使用英文，與既有歷史一致（例：`Park the cursor on the float after casting`）。
- 新增的 `.cs` 檔案必須同時加入 `Source/FishingFunBot/FishingFun.Bot.csproj` 的 `<Compile Include>` 清單（此專案是傳統格式，不會自動 glob）。

## 檔案結構

| 檔案 | 責任 | 本次動作 |
| --- | --- | --- |
| `Source/FishingFunBot/Bot/FloatCursor.cs` | 「每輪只把游標放一次」的規則，純邏輯 | 新增 |
| `Source/FishingFunBot/Bot/HumanTiming.cs` | 所有時間決策：拋竿延遲、反應時間、按鍵時長、右鍵前等待 | 修改 |
| `Source/FishingFunBot/Platform/WowProcess.cs` | 送出按鍵與滑鼠輸入到遊戲 | 修改 |
| `Source/FishingFunBot/Bot/FishBot.cs` | 機器人的主主迴圈：拋竿、等魚、揚竿 | 修改 |
| `Source/FishingFunBot/FishingFun.Bot.csproj` | 編譯檔案清單 | 修改 |
| `Source/FishingFunUI/UI/ColourConfiguration.xaml` | LootDelay 滑桿範圍 | 修改 |
| `Source/FishingFunUI/UI/ColourConfiguration.xaml.cs` | LootDelay 滑桿標籤文字 | 修改 |
| `tests/CursorBehaviourRegression.cs` | 游標放置規則的離線回歸測試 | 新增 |
| `tests/Run-CursorBehaviourRegression.ps1` | 上述測試的執行腳本 | 新增 |
| `tests/HumanTimingRegression.cs` | 計時範圍的回歸測試 | 修改 |
| `tests/LootLatencyRegression.cs` | 點擊延遲預算的回歸測試 | 修改 |
| `使用說明.md` | 使用者手冊 | 修改 |
| `TOOLS.md` | 工具與測試的註冊表 | 修改 |

---

### Task 1: 新增兩個計時方法

**Files:**
- Modify: `Source/FishingFunBot/Bot/HumanTiming.cs`（在 `KeyHoldMs()` 之後加入兩個方法）
- Test: `tests/HumanTimingRegression.cs`

**Interfaces:**
- Consumes: 無（既有類別）
- Produces:
  - `public int HumanTiming.CursorMoveDelayMs()` — `Enabled` 為 true 時回傳 700–2200 的整數；`Enabled` 為 false 時固定回傳 `1200`。
  - `public int HumanTiming.LootDelayMs(int maximum)` — 回傳 `[100, Math.Max(100, maximum)]` 之間的整數（含兩端），**不論 `Enabled` 為何都會隨機**。

- [ ] **Step 1: 寫失敗的測試**

在 `tests/HumanTimingRegression.cs` 的 `Console.WriteLine("=== a bad strength is handled ===");` 區塊之前插入：

```csharp
            Console.WriteLine();
            Console.WriteLine("=== the cursor is moved onto the float after a wait ===");
            {
                var timing = new HumanTiming(new Random(11), 1.0, true);

                var cursorDelay = Sample(timing.CursorMoveDelayMs, 4000);
                Check("the wait before moving the cursor stays in range",
                    cursorDelay.All(v => v >= 700 && v <= 2200),
                    $"[{cursorDelay.Min()}, {cursorDelay.Max()}]");

                var strong = new HumanTiming(new Random(13), 2.0, true);
                Check("a strong setting cannot push the wait past its range",
                    Sample(strong.CursorMoveDelayMs, 4000).All(v => v >= 700 && v <= 2200));

                var off = new HumanTiming(new Random(12), 1.0, false);
                Check("the float is still given time to land when it is off",
                    Sample(off.CursorMoveDelayMs, 500).All(v => v == 1200));
            }

            Console.WriteLine();
            Console.WriteLine("=== the wait before the click is short and random ===");
            {
                var timing = new HumanTiming(new Random(14), 1.0, true);

                var delays = Sample(() => timing.LootDelayMs(300), 4000);
                Check("the wait before the click stays between 100 and 300ms",
                    delays.All(v => v >= 100 && v <= 300),
                    $"[{delays.Min()}, {delays.Max()}]");
                Check("the wait before the click is not always the same",
                    delays.Distinct().Count() > 100,
                    $"{delays.Distinct().Count()} different values");

                Check("a maximum below the minimum still gives the minimum",
                    timing.LootDelayMs(10) == 100);

                var off = new HumanTiming(new Random(15), 1.0, false);
                Check("the wait before the click is random even when the human behaviour is off",
                    Sample(() => off.LootDelayMs(300), 2000).Distinct().Count() > 100);
            }
```

- [ ] **Step 2: 執行測試確認失敗**

```
cd C:/Working/fish
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-HumanTimingRegression.ps1
```

預期：編譯失敗，錯誤訊息包含 `HumanTiming` 不具有 `CursorMoveDelayMs` 與 `LootDelayMs`。

- [ ] **Step 3: 實作最小程式碼**

在 `Source/FishingFunBot/Bot/HumanTiming.cs` 的 `KeyHoldMs()` 方法之後（`Skewed` 方法之前）加入：

```csharp
        /// <summary>
        /// The wait after casting and before the cursor is moved onto the float.
        /// 
        /// A person waits for the float to land before putting the cursor on it.
        /// The float lands about a second after the cast, so the wait is never
        /// shorter than that.
        /// 
        /// The wait is still there when the human behaviour is switched off,
        /// because the game needs the float to land. It is not a human timing
        /// then, so it is a fixed value.
        /// </summary>
        public int CursorMoveDelayMs()
        {
            if (!Enabled) return 1200;
            return Skewed(700, 2200, 1.4);
        }

        /// <summary>
        /// The wait between moving the cursor onto the float and pressing the
        /// right mouse button.
        /// 
        /// The game reads the mouse position once a frame, so it needs a moment
        /// to see where the cursor is now. The cursor has usually been on the
        /// float for a while, so the wait is short. It is picked at random
        /// between the minimum and the maximum.
        /// 
        /// This is not a human timing, so it stays random when the human
        /// behaviour is switched off.
        /// </summary>
        public int LootDelayMs(int maximum)
        {
            int upper = Math.Max(100, maximum);
            return 100 + random.Next(upper - 100 + 1);
        }
```

- [ ] **Step 4: 執行測試確認通過**

```
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-HumanTimingRegression.ps1
```

預期：輸出 `ALL PASS`，退出碼 0。

- [ ] **Step 5: Commit**

```bash
git add Source/FishingFunBot/Bot/HumanTiming.cs tests/HumanTimingRegression.cs
git commit -m "Add the wait before the cursor is moved onto the float"
```

---

### Task 2: 新增 `FloatCursor` 與它的回歸測試

**Files:**
- Create: `Source/FishingFunBot/Bot/FloatCursor.cs`
- Create: `tests/CursorBehaviourRegression.cs`
- Create: `tests/Run-CursorBehaviourRegression.ps1`
- Modify: `Source/FishingFunBot/FishingFun.Bot.csproj`（在 `<Compile Include="Bot\FishBot.cs" />` 之後加入 `Bot\FloatCursor.cs`）

**Interfaces:**
- Consumes: 無
- Produces:
  - `public class FloatCursor`
    - `public bool Placed { get; }` — 本輪是否已放置過游標
    - `public System.Drawing.Point? PlacedOn { get; }` — 游標被放置的位置
    - `public bool Place(System.Drawing.Point floatPosition, bool userHoldsMouseButton)` — 回傳是否應該移動游標；`userHoldsMouseButton` 為 true 時回傳 false 且**不**標記為已放置（下一幀可重試）

- [ ] **Step 1: 寫失敗的測試**

建立 `tests/CursorBehaviourRegression.cs`：

```csharp
using System;
using System.Drawing;

#nullable enable
namespace FishingFun
{
    /// <summary>
    /// Checks that the cursor is put on the float once per cast, the way a
    /// person puts it there and leaves it, and that it is not moved while the
    /// user is holding a mouse button down.
    /// </summary>
    public static class CursorBehaviourRegression
    {
        private static int failures;
        private static int checks;

        private static void Check(string what, bool passed, string detail = "")
        {
            checks++;
            Console.WriteLine((passed ? "PASS " : "FAIL ") + what + (detail.Length > 0 ? "  (" + detail + ")" : ""));
            if (!passed) failures++;
        }

        public static int Main()
        {
            Console.WriteLine("=== the cursor is put on the float once ===");
            {
                var cursor = new FloatCursor();
                var floatPosition = new Point(1000, 500);

                Check("the cursor is put on the float when it is first seen",
                    cursor.Place(floatPosition, false));

                Check("the cursor is not put on the float again",
                    !cursor.Place(floatPosition, false));

                Check("the cursor is on the float",
                    cursor.Placed);

                Check("the place the cursor was put on is remembered",
                    cursor.PlacedOn == floatPosition);
            }

            Console.WriteLine();
            Console.WriteLine("=== the float drifting does not move the cursor ===");
            {
                var cursor = new FloatCursor();
                cursor.Place(new Point(1000, 500), false);

                Check("the cursor is not moved when the float drifts",
                    !cursor.Place(new Point(1000, 507), false));

                Check("the cursor is still where it was",
                    cursor.PlacedOn == new Point(1000, 500));
            }

            Console.WriteLine();
            Console.WriteLine("=== the cursor is not moved while the user holds a button ===");
            {
                var cursor = new FloatCursor();

                Check("the cursor is not put on the float while the user holds a button",
                    !cursor.Place(new Point(1000, 500), true));

                Check("the float is not marked as placed",
                    !cursor.Placed);

                Check("the cursor is put on the float once the user lets go",
                    cursor.Place(new Point(1000, 500), false));
            }

            Console.WriteLine();
            Console.WriteLine("=== every cast gets its own cursor ===");
            {
                var first = new FloatCursor();
                var second = new FloatCursor();

                first.Place(new Point(1000, 500), false);

                Check("a new cast places the cursor again",
                    second.Place(new Point(1100, 600), false));

                Check("the two casts are separate",
                    first.PlacedOn != second.PlacedOn);
            }

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILURE(S)");
            return failures == 0 ? 0 : 1;
        }
    }
}
```

- [ ] **Step 2: 建立測試執行腳本**

建立 `tests/Run-CursorBehaviourRegression.ps1`：

```powershell
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$compiler = Get-ChildItem 'C:/Program Files/Microsoft Visual Studio/2022/*/MSBuild/Current/Bin/Roslyn/csc.exe' | Select-Object -First 1
if (!$compiler) { throw 'Visual Studio 2022 C# compiler is required.' }
$output = Join-Path $root 'temp/tests'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$exe = Join-Path $output 'CursorBehaviourRegression.exe'
& $compiler.FullName /nologo /r:System.Drawing.dll "/out:$exe" "$PSScriptRoot/CursorBehaviourRegression.cs" "$root/Source/FishingFunBot/Bot/FloatCursor.cs"
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& $exe
exit $LASTEXITCODE
```

- [ ] **Step 3: 執行測試確認失敗**

```
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-CursorBehaviourRegression.ps1
```

預期：編譯失敗，錯誤訊息包含 `FloatCursor` 找不到型別。

- [ ] **Step 4: 實作 `FloatCursor`**

建立 `Source/FishingFunBot/Bot/FloatCursor.cs`：

```csharp
using System.Drawing;

#nullable enable
namespace FishingFun
{
    /// <summary>
    /// Decides when the cursor is put onto the float.
    /// 
    /// A person puts the cursor on the float once the float has landed and then
    /// leaves it there. The float drifts while they wait, but they do not follow
    /// it. The place it ends up in is put right before the click.
    /// 
    /// This class holds that rule. It does not touch the mouse itself, so that
    /// the rule can be checked without a game running.
    /// </summary>
    public class FloatCursor
    {
        private Point? placedOn;

        /// <summary>
        /// True when the cursor has already been put on the float for this cast.
        /// </summary>
        public bool Placed => placedOn.HasValue;

        /// <summary>
        /// The place the cursor was put on.
        /// </summary>
        public Point? PlacedOn => placedOn;

        /// <summary>
        /// Puts the cursor on the float the first time the float is seen.
        /// 
        /// Returns false when it has already been done for this cast. It also
        /// returns false when the user is holding a mouse button down, and in
        /// that case the float is not marked as placed, so that the cursor can
        /// be put on it on a later frame.
        /// </summary>
        public bool Place(Point floatPosition, bool userHoldsMouseButton)
        {
            if (placedOn.HasValue || userHoldsMouseButton) return false;
            placedOn = floatPosition;
            return true;
        }
    }
}
```

- [ ] **Step 5: 把新檔案加入專案**

在 `Source/FishingFunBot/FishingFun.Bot.csproj` 中，把這一行

```xml
    <Compile Include="Bot\FishBot.cs" />
```

改為

```xml
    <Compile Include="Bot\FishBot.cs" />
    <Compile Include="Bot\FloatCursor.cs" />
```

- [ ] **Step 6: 執行測試確認通過**

```
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-CursorBehaviourRegression.ps1
```

預期：輸出 `ALL PASS`，退出碼 0。

- [ ] **Step 7: 確認專案仍可建置**

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-Release.ps1
```

預期：`FishingFun.Bot -> ...\FishingFunBot.dll`，退出碼 0。

- [ ] **Step 8: Commit**

```bash
git add Source/FishingFunBot/Bot/FloatCursor.cs Source/FishingFunBot/FishingFun.Bot.csproj tests/CursorBehaviourRegression.cs tests/Run-CursorBehaviourRegression.ps1
git commit -m "Decide when the cursor is put on the float"
```

---

### Task 3: `LootDelay` 變成隨機等待的上限

**Files:**
- Modify: `Source/FishingFunBot/Platform/WowProcess.cs:140-154`（`LootDelay` 欄位與說明）、`:317`（等待處）
- Modify: `Source/FishingFunUI/UI/ColourConfiguration.xaml:57`（滑桿最小值）
- Modify: `Source/FishingFunUI/UI/ColourConfiguration.xaml.cs:140-143`（滑桿標籤）
- Test: `tests/LootLatencyRegression.cs`
- Modify: `tests/Run-LootLatencyRegression.ps1`（改為連同 `WowProcess.cs` 一起編譯）

**Interfaces:**
- Consumes: `HumanTiming.LootDelayMs(int maximum)`（Task 1）
- Produces: `public static int WowProcess.LootDelay` 語意變更為「右鍵前等待的上限」，預設值由 `1000` 改為 `300`。

- [ ] **Step 1: 寫失敗的測試**

在 `tests/LootLatencyRegression.cs` 中，把常數區塊改為：

```csharp
        /// <summary>
        /// The longest the wait before the click may be. The bot picks a random
        /// value between the minimum and the maximum.
        /// </summary>
        private const int LootDelayMaximum = 300;

        /// <summary>
        /// The shortest the wait before the click may be.
        /// </summary>
        private const int LootDelayMinimum = 100;

        /// <summary>
        /// The worst case of the old behaviour: the reaction was 900 to 1124ms and
        /// then the camera wait of 1000ms.
        /// </summary>
        private const int OldWorstCase = 2124;

        /// <summary>
        /// The worst case the bot aims for now: the longest reaction and then the
        /// longest wait before the click.
        /// </summary>
        private const int WorstCase = 1120 + LootDelayMaximum;
```

在 `Main()` 的 `=== the click never comes later than it used to be ===` 區塊**之前**插入新的區塊。這一段直接讀取正式程式碼裡的 `WowProcess.LootDelay`，所以在正式程式還沒改之前它會失敗：

```csharp
            Console.WriteLine("=== the wait before the click is short and random ===");
            {
                // The production default is the one the slider starts at. The wait
                // is picked at random between the minimum and that default.
                var timing = new HumanTiming(new Random(5), 1.0, true);

                bool withinRange = true;
                int min = int.MaxValue;
                int max = 0;
                for (int i = 0; i < 5000; i++)
                {
                    int delay = timing.LootDelayMs(WowProcess.LootDelay);
                    if (delay < LootDelayMinimum || delay > LootDelayMaximum) withinRange = false;
                    if (delay < min) min = delay;
                    if (delay > max) max = delay;
                }

                Check("the wait before the click stays between 100 and 300ms",
                    withinRange, $"[{min}, {max}]");

                Check("the production default is the upper bound of that range",
                    WowProcess.LootDelay == LootDelayMaximum,
                    $"LootDelay is {WowProcess.LootDelay}ms");
            }
```

把該檔案中所有使用舊常數 `LootDelay` 的算式替換為 `LootDelayMaximum`（共 3 處 `lookup + actualReaction + LootDelay`、1 處 `0 + reaction + LootDelay`、1 處範圍比較、1 處訊息字串）：

| 原本 | 改為 |
| --- | --- |
| `int total = lookup + actualReaction + LootDelay;`（3 處） | `int total = lookup + actualReaction + LootDelayMaximum;` |
| `int total = 0 + reaction + LootDelay;` | `int total = 0 + reaction + LootDelayMaximum;` |
| `if (total < 700 + LootDelay || total > 1120 + LootDelay) withinRange = false;` | `if (total < 700 + LootDelayMaximum || total > 1120 + LootDelayMaximum) withinRange = false;` |
| `$"between {700 + LootDelay}ms and {1120 + LootDelay}ms"` | `$"between {700 + LootDelayMaximum}ms and {1120 + LootDelayMaximum}ms"` |

把「a lookup longer than the reaction is noticed」那一段的比較基準由 `OldWorstCase` 改為 `WorstCase`：

```csharp
                Check("a lookup longer than the reaction is noticed",
                    total > WorstCase,
                    $"the click would be {total}ms, which is outside the budget of {WorstCase}ms");
```

**修改測試執行腳本**，讓它連同正式程式的 `WowProcess` 一起編譯（`tests/Run-LootLatencyRegression.ps1`）：

```powershell
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$compiler = Get-ChildItem 'C:/Program Files/Microsoft Visual Studio/2022/*/MSBuild/Current/Bin/Roslyn/csc.exe' | Select-Object -First 1
if (!$compiler) { throw 'Visual Studio 2022 C# compiler is required.' }
$output = Join-Path $root 'temp/tests'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$exe = Join-Path $output 'LootLatencyRegression.exe'
& $compiler.FullName /nologo /r:System.Drawing.dll /r:System.Windows.Forms.dll "/out:$exe" "/r:$root/Source/packages/log4net.3.5.0/lib/net462/log4net.dll" "$PSScriptRoot/LootLatencyRegression.cs" "$root/Source/FishingFunBot/Bot/HumanTiming.cs" "$root/Source/FishingFunBot/Platform/WowProcess.cs" "$root/Source/FishingFunBot/Platform/Keys.cs"
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
Copy-Item -LiteralPath "$root/Source/packages/log4net.3.5.0/lib/net462/log4net.dll" -Destination $output -Force
& $exe
exit $LASTEXITCODE
```

- [ ] **Step 2: 執行測試確認失敗**

```
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-LootLatencyRegression.ps1
```

預期：輸出包含兩行 FAIL：

```
FAIL the wait before the click stays between 100 and 300ms  ([100, 1000])
FAIL the production default is the upper bound of that range  (LootDelay is 1000ms)
```

退出碼為 1。

- [ ] **Step 3: 實作 `WowProcess` 的變更**

在 `Source/FishingFunBot/Platform/WowProcess.cs` 中，把 `LootDelay` 的宣告與說明改為：

```csharp
        /// <summary>
        /// The longest time to wait after the cursor has been moved onto the
        /// bobber and before the right mouse button is pressed.
        /// 
        /// The game rotates the camera when it sees the mouse move while the right
        /// mouse button is down. Moving the cursor generates a mouse move message
        /// that the game reads on one of its own frames, so the cursor has to be
        /// moved and the game given time to read that movement before the button
        /// is pressed. If the game reads the movement while the button is down it
        /// applies it as a camera rotation and the view swings away from the
        /// bobber.
        /// 
        /// The cursor has usually been on the bobber for a while, so the wait is
        /// short. A random value between 100ms and this maximum is used.
        /// 
        /// Raise this value if the camera still moves. See issue #44 "Camera going wild"
        /// and the discussion on issue #13.
        /// </summary>
        public static int LootDelay = 300;
```

在 `RightClickMouse` 中，把

```csharp
                    MoveCursorTo(position);
                    Thread.Sleep(LootDelay);
```

改為

```csharp
                    MoveCursorTo(position);
                    Thread.Sleep(HumanTiming.Current.LootDelayMs(LootDelay));
```

- [ ] **Step 4: 變更 UI 滑桿**

在 `Source/FishingFunUI/UI/ColourConfiguration.xaml` 中，把

```xml
<Slider Margin="20 0 10 10" Foreground="Blue" ValueChanged="LootDelay_ValueChanged" Minimum="0" Maximum="3000" TickFrequency="100" Value="{Binding LootDelay}" Thumb.DragCompleted="Slider_DragCompleted" TickPlacement="BottomRight" />
```

改為

```xml
<Slider Margin="20 0 10 10" Foreground="Blue" ValueChanged="LootDelay_ValueChanged" Minimum="100" Maximum="3000" TickFrequency="100" Value="{Binding LootDelay}" Thumb.DragCompleted="Slider_DragCompleted" TickPlacement="BottomRight" />
```

在 `Source/FishingFunUI/UI/ColourConfiguration.xaml.cs` 中，把

```csharp
            this.LabelLootDelay.Text = $"Wait {this.LootDelay}ms after moving the cursor onto the bobber, before clicking. Raise this if the camera moves when the fish bites.";
```

改為

```csharp
            this.LabelLootDelay.Text = $"Wait a random 100 to {this.LootDelay}ms after moving the cursor onto the bobber, before clicking. Raise this if the camera moves when the fish bites.";
```

- [ ] **Step 5: 執行測試確認通過**

```
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-LootLatencyRegression.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-Release.ps1
```

預期：兩者皆退出碼 0；延遲測試輸出 `worst 1420ms, old worst 2124ms`。

- [ ] **Step 6: Commit**

```bash
git add Source/FishingFunBot/Platform/WowProcess.cs Source/FishingFunUI/UI/ColourConfiguration.xaml Source/FishingFunUI/UI/ColourConfiguration.xaml.cs tests/LootLatencyRegression.cs tests/Run-LootLatencyRegression.ps1
git commit -m "Wait a random short time before the click on the float"
```

---

### Task 4: `WowProcess` 的游標操作

**Files:**
- Modify: `Source/FishingFunBot/Platform/WowProcess.cs`
  - `:169-179`（`IsMouseButtonHeldDown` 改為 public 並更名）
  - `:297`（呼叫點更新）
  - `:303`（移除 `oldPosition`）
  - `:359-383`（`RefocusOnOldScreen` 移除游標還原）
  - 新增 `MoveCursorOntoFloat` 與 `RestoreCursor`

**Interfaces:**
- Consumes: 無
- Produces:
  - `public static bool WowProcess.IsMouseButtonHeldDownByUser()` — 使用者是否正按著滑鼠鍵
  - `public static void WowProcess.MoveCursorOntoFloat(System.Drawing.Point position)` — 把游標移到浮標上並留在那裡；使用者按著滑鼠鍵時不動作；停止中的機器人不動作
  - `public static void WowProcess.RestoreCursor()` — 把游標還原到使用者原本的位置；不檢查取消旗標

- [ ] **Step 1: 寫失敗的測試**

在 `tests/BotSafetyRegression.cs` 的 `=== the right place is clicked when the float cannot be seen ===` 區塊之後、`Console.WriteLine(failures == 0 ...)` 之前插入：

```csharp
            Console.WriteLine();
            Console.WriteLine("=== the cursor is only moved when the user is not holding a button ===");
            {
                Check("the user is not holding a mouse button",
                    !WowProcess.IsMouseButtonHeldDownByUser());
            }
```

> 這段測試只能在沒有按下滑鼠的環境下執行，CI 與本機皆應成立。

- [ ] **Step 2: 執行測試確認失敗**

```
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-BotSafetyRegression.ps1
```

預期：編譯失敗，錯誤訊息包含 `IsMouseButtonHeldDownByUser` 找不到。

- [ ] **Step 3: 實作 `WowProcess` 的變更**

（a）把 `IsMouseButtonHeldDown` 改為 public 並更名：

```csharp
        /// <summary>
        /// True when the user is physically holding a mouse button down.
        /// Injecting a click or a movement at the same time as real input
        /// confuses the game.
        /// </summary>
        public static bool IsMouseButtonHeldDownByUser()
        {
            const short keyDownMask = unchecked((short)0x8000);

            return (GetAsyncKeyState(VK_LBUTTON) & keyDownMask) != 0
                || (GetAsyncKeyState(VK_RBUTTON) & keyDownMask) != 0;
        }
```

（b）在 `RightClickMouse` 中把 `if (IsMouseButtonHeldDown())` 改為 `if (IsMouseButtonHeldDownByUser())`。

（c）移除 `RightClickMouse` 中的 `var oldPosition = System.Windows.Forms.Cursor.Position;` 這一行。

（d）把 `RefocusOnOldScreen` 的簽章與內容改為：

```csharp
        private static void RefocusOnOldScreen(ILog logger, Process activeProcess, Process wowProcess)
        {
            try
            {
                if (activeProcess.MainWindowTitle != wowProcess.MainWindowTitle)
                {
                    // get focus back on this screen
                    PostMessage(activeProcess.MainWindowHandle, Keys.WM_RBUTTONDOWN, Keys.VK_RMB, IntPtr.Zero);
                    Thread.Sleep(30);
                    PostMessage(activeProcess.MainWindowHandle, Keys.WM_RBUTTONUP, Keys.VK_RMB, IntPtr.Zero);

                    KeyDown(ConsoleKey.Escape);
                    Thread.Sleep(30);
                    KeyUp(ConsoleKey.Escape);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex.Message);
            }
        }
```

並把呼叫點 `RefocusOnOldScreen(logger, activeProcess, wowProcess, oldPosition);` 改為 `RefocusOnOldScreen(logger, activeProcess, wowProcess);`。

> 游標不再還原到舊位置：游標現在停在浮標上，還原等於原地不動；浮標飄移時反而會把游標從修正後的新位置拉回舊點。

（e）在 `WowProcess` 類別中新增靜態欄位與兩個方法（放在 `MoveCursorTo` 之後）：

```csharp
        /// <summary>
        /// Where the user left the cursor before the bot moved it. It is put
        /// back when the bot stops.
        /// </summary>
        private static System.Drawing.Point? userCursorPosition;

        /// <summary>
        /// Moves the cursor onto the float, the way a person does before waiting
        /// for the fish to bite. The cursor is left there.
        /// 
        /// The movement must not happen while the right mouse button is down,
        /// because the game treats that as a camera rotation.
        /// </summary>
        public static void MoveCursorOntoFloat(System.Drawing.Point position)
        {
            RunUnlessCancelled(() =>
            {
                var wowProcess = Get();
                if (wowProcess == null) return;

                // The user is holding a mouse button down, so the game is getting
                // real input from them. Moving the cursor now is not safe.
                if (IsMouseButtonHeldDownByUser()) return;

                // Send a button up before moving. If the game still thinks the
                // right button is down it treats the movement as a camera
                // rotation and the view swings away.
                PostMessage(wowProcess.MainWindowHandle, Keys.WM_RBUTTONUP, Keys.VK_RMB, IntPtr.Zero);

                if (!userCursorPosition.HasValue)
                {
                    // Remember where the user left the cursor, so that it can be
                    // given back when the bot stops.
                    userCursorPosition = System.Windows.Forms.Cursor.Position;
                }

                MoveCursorTo(position);
            });
        }

        /// <summary>
        /// Puts the cursor back where the user left it.
        /// 
        /// This does not go through RunUnlessCancelled, because the bot has
        /// already stopped by the time this is called and the cursor still has
        /// to be given back. It is the same reason the mouse button is always
        /// let go.
        /// </summary>
        public static void RestoreCursor()
        {
            lock (InputLock)
            {
                var original = userCursorPosition;
                userCursorPosition = null;
                if (!original.HasValue) return;
                SetCursorPos(original.Value.X, original.Value.Y);
            }
        }
```

- [ ] **Step 4: 執行測試確認通過**

```
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-BotSafetyRegression.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-Release.ps1
```

預期：兩者皆退出碼 0。

- [ ] **Step 5: Commit**

```bash
git add Source/FishingFunBot/Platform/WowProcess.cs tests/BotSafetyRegression.cs
git commit -m "Move the cursor onto the float and give it back when the bot stops"
```

---

### Task 5: `FishBot` 接上新的行為

**Files:**
- Modify: `Source/FishingFunBot/Bot/FishBot.cs`
  - `:88-118`（`Start()` 的主迴圈）
  - `:170-179`（移除 `Watch` 方法）
  - `:195-237`（`WaitForBite()`）
  - `:181-193`（`Stop()`）

**Interfaces:**
- Consumes:
  - `HumanTiming.Current.CursorMoveDelayMs()`（Task 1）
  - `FloatCursor.Place(Point, bool)`、`FloatCursor.Placed`（Task 2）
  - `WowProcess.MoveCursorOntoFloat(Point)`、`WowProcess.IsMouseButtonHeldDownByUser()`、`WowProcess.RestoreCursor()`（Task 4）
- Produces: 無新的公開介面

- [ ] **Step 1: 確認此任務的驗證門檻**

本任務的行為需要真正的遊戲視窗才能端對端驗證，無法離線測試。因此本任務的驗證
門檻是「專案可建置，且既有的六組回歸測試仍全數通過」，實機驗收項目記在 Step 7。
在開始改動前，先確認基準是綠的：

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-Release.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-BiteWatcherRegression.ps1
```

預期：兩者皆退出碼 0。若基準已經是紅的，先解決基準問題再繼續本任務。

- [ ] **Step 2: 在 `Start()` 中以隨機延遲取代固定等待**

在 `Source/FishingFunBot/Bot/FishBot.cs` 中，把

```csharp
                    WowProcess.PressKey(castKey);

                    Watch(2000);

                    WaitForBite();
```

改為

```csharp
                    WowProcess.PressKey(castKey);

                    // A person waits for the float to land before moving the
                    // cursor onto it.
                    int cursorDelay = HumanTiming.Current.CursorMoveDelayMs();
                    logger.Info($"Waiting {cursorDelay}ms before moving the cursor onto the float.");
                    WaitWhileEnabled(cursorDelay);

                    WaitForBite();
```

- [ ] **Step 3: 移除 `Watch` 方法**

刪除 `FishBot.cs` 中整個方法：

```csharp
        private void Watch(int milliseconds)
        {
            bobberFinder.Reset();
            stopwatch.Restart();
            while (stopwatch.ElapsedMilliseconds < milliseconds)
            {
                bobberFinder.Find();
            }
            stopwatch.Stop();
        }
```

> 這個方法只有 `Watch(2000)` 這一個呼叫點。`WaitForBite()` 開頭本來就會 `bobberFinder.Reset()` 並重新等待浮標可見，因此移除不會影響追蹤品質。

同時刪除 `FishBot.cs` 中這一行，因為 `Watch` 是 `stopwatch` 欄位唯一的使用者：

```csharp
        private Stopwatch stopwatch = new Stopwatch();
```

（`totalTimeStopWatch` 與 `lureStopwatch` 是另外兩個欄位，仍然被使用，不動。）

- [ ] **Step 4: 在觀察迴圈中把游標放到浮標上**

在 `WaitForBite()` 中，把

```csharp
        private void WaitForBite()
        {
            bobberFinder.Reset();

            var timedTask = new TimedAction((a) => { logger.Info("Fishing timed out!"); }, 25 * 1000, 25);
```

改為

```csharp
        private void WaitForBite()
        {
            bobberFinder.Reset();

            // The cursor is put on the float once it has been seen, and it is
            // left there, the way a person leaves it. The place it ends up in
            // is put right before the click.
            var floatCursor = new FloatCursor();

            var timedTask = new TimedAction((a) => { logger.Info("Fishing timed out!"); }, 25 * 1000, 25);
```

再把觀察迴圈中的這一段

```csharp
                bool isBite = this.biteWatcher.IsBite(observation);
                trace?.Record(observation, isBite ? "bite" : "watch");

                if (isBite)
```

改為

```csharp
                bool isBite = this.biteWatcher.IsBite(observation);
                trace?.Record(observation, isBite ? "bite" : "watch");

                if (observation.IsVisible && floatCursor.Place(observation.Position, WowProcess.IsMouseButtonHeldDownByUser()))
                {
                    logger.Info($"Moving the cursor onto the float at {observation.Position}.");
                    WowProcess.MoveCursorOntoFloat(observation.Position);
                }

                if (isBite)
```

> 放在主迴圈而不是「等待浮標可見」迴圈結束時，是為了讓放置動作可以重試：使用者當下正按著滑鼠鍵時，這一幀不會標記為已放置，下一幀使用者鬆手後就會補上。

- [ ] **Step 5: 停止時還原游標**

在 `Stop()` 中，把

```csharp
            WowProcess.StopSendingInput();

            totalTimeStopWatch.Reset();
```

改為

```csharp
            WowProcess.StopSendingInput();

            // The cursor is put back where the user left it, so that the bot
            // does not leave the mouse somewhere else.
            WowProcess.RestoreCursor();

            totalTimeStopWatch.Reset();
```

- [ ] **Step 6: 執行建置與全部回歸測試**

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-Release.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-BiteWatcherRegression.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-HumanTimingRegression.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-BotSafetyRegression.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-LootLatencyRegression.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-WriteReductionRegression.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-CursorBehaviourRegression.ps1
```

預期：建置退出碼 0，六個測試腳本皆輸出 `ALL PASS` 且退出碼 0。

- [ ] **Step 7: 記錄實機驗收項目**

在提交訊息或 PR 說明中記錄下列項目需實機確認（離線測試無法驗證）：

- 拋竿後游標自動移至浮標並停留，不再移回。
- 魚咬鉤後點擊浮標的時間明顯短於原本的 1700–2120ms。
- 揚竿時畫面不會甩動。
- 停止機器人後游標回到使用者原本的位置。

- [ ] **Step 8: Commit**

```bash
git add Source/FishingFunBot/Bot/FishBot.cs
git commit -m "Park the cursor on the float after casting"
```

---

### Task 6: 更新文件

**Files:**
- Modify: `使用說明.md`
  - `:116-140`（擬人化作為）
  - `:177-179`（疑難排解「揚竿時畫面突然轉向」）
  - `:213-220`（測試章節）
- Modify: `TOOLS.md`
  - `:5-15`（能力索引）
  - 工具章節（在 `Bot safety` 章節之後新增）

**Interfaces:**
- Consumes: Task 1–5 的最終行為
- Produces: 無程式碼介面

- [ ] **Step 1: 更新 `使用說明.md` 的擬人化作為表**

把

```markdown
| 項目 | 範圍 | 說明 |
| --- | --- | --- |
| 投竿前停頓 | 950–3000ms | 不會在上一條魚收完的瞬间就投竿 |
| 咬鉤反應時間 | 700–1120ms | 發現魚到把游標移過去的時間 |
| 按鍵按住 | 45–180ms | 每個按鍵的長短不同 |
```

改為

```markdown
| 項目 | 範圍 | 說明 |
| --- | --- | --- |
| 投竿前停頓 | 950–3000ms | 不會在上一條魚收完的瞬间就投竿 |
| 拋竿後移游標 | 700–2200ms | 等浮標落水後才把游標移過去，移過去就不再移動 |
| 咬鉤反應時間 | 700–1120ms | 發現魚到把游標移過去的時間 |
| 按鍵按住 | 45–180ms | 每個按鍵的長短不同 |
```

- [ ] **Step 2: 更新點擊延遲的說明**

把 `使用說明.md` 中

```markdown
> **反應時間刻意不比原本慢。** 從魚上鉤到點擊浮標的時間，由「反應時間」加上
> 「相機需要的 1000ms」組成，預算上限 2120ms，不超過原本的 2124ms。因為魚咬鉤
> 後只有很短的時間可以點，點慢了就會漏魚。
```

改為

```markdown
> **反應時間刻意不比原本慢。** 從魚上鉤到點擊浮標的時間，由「反應時間」加上
> 「相機需要的隨機 100–300ms」組成，預算上限 1420ms，遠低於原本的 2124ms。
> 因為魚咬鉤後只有很短的時間可以點，點慢了就會漏魚。
>
> **游標會先停在浮標上。** 真人拋竿後會把游標移到浮標上並維持不動，魚咬鉤時
> 只要按一次右鍵。機器人現在也一樣：拋竿後隨機等 700–2200ms，浮標一出現就把
> 游標移過去並留在那裡，游標**不會**跟著浮標的漂移移動，只在點擊前修正一次。
> 因為游標早已就位，右鍵前就不必再等整整一秒。等待期間滑鼠游標會停在遊戲畫面
> 上，機器人停止時會還原到原本的位置。
```

- [ ] **Step 3: 更新疑難排解**

把

```markdown
已修正（游標不再在右鍵按下期間移動）。若仍發生，把 `LootDelay` 調高。
```

改為

```markdown
已修正（游標不再在右鍵按下期間移動）。若仍發生，把 `LootDelay` 調高。
`LootDelay` 是右鍵前等待的**上限**，實際等待是 100ms 到上限之間的隨機值，
預設上限 300ms。
```

- [ ] **Step 4: 更新測試章節**

把

```markdown
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-BiteWatcherRegression.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-WriteReductionRegression.ps1
```

兩者都不需要遊戲即可執行。
```

改為

```markdown
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-BiteWatcherRegression.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-HumanTimingRegression.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-BotSafetyRegression.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-LootLatencyRegression.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-WriteReductionRegression.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-CursorBehaviourRegression.ps1
```

全部都不需要遊戲即可執行。
```

- [ ] **Step 5: 更新 `TOOLS.md` 的能力索引**

在能力索引表格中，於 `| Bot safety regression | ... |` 那一列之後加入：

```markdown
| Cursor behaviour regression | The cursor is put on the float once per cast | `tests/Run-CursorBehaviourRegression.ps1` | No game or restored packages required |
```

- [ ] **Step 6: 在 `TOOLS.md` 新增章節**

在 `### Bot safety` 章節之後加入：

```markdown
### Cursor behaviour

**Purpose:** Check that the cursor is put on the float once per cast, that the float drifting does not move it again, and that it is not moved while the user is holding a mouse button down.

**Path / command:** `tests/Run-CursorBehaviourRegression.ps1`

**Use when:** Changing when the cursor is moved onto the float, or what stops it from being moved.

**Usage:** `powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-CursorBehaviourRegression.ps1`

**Inputs / arguments:** None.

**Outputs:** PASS/FAIL lines and nonzero exit on failure.

**Requirements:** Windows .NET Framework and the Visual Studio 2022 C# compiler.

**Notes / constraints:** The cursor is left on the float while the bot waits, the way a person leaves it. The place the float ends up in is put right before the click, which is the existing behaviour of `LootPosition.Choose`. A person is holding a mouse button down, the cursor is not moved and the float is not marked as placed, so it is put on the float on a later frame.
```

- [ ] **Step 7: 確認文件沒有遺漏**

用 Grep 工具搜尋 `使用說明.md` 與 `TOOLS.md` 中的過時說法：

- pattern：`1000ms|2120ms`
- path：`C:\Working\fish\使用說明.md` 與 `C:\Working\fish\TOOLS.md`

預期：`1000ms` 與 `2120ms` 都沒有符合結果。`2124ms` 只會出現在描述「舊行為上限」
的句子中，那是刻意保留的對照值。

- [ ] **Step 8: Commit**

```bash
git add 使用說明.md TOOLS.md
git commit -m "Document the cursor being parked on the float"
```

---

### Task 7: 完整驗證

**Files:**
- 無檔案變更（僅驗證）

**Interfaces:**
- Consumes: Task 1–6 的全部成果
- Produces: 無

- [ ] **Step 1: 執行 Release 建置**

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-Release.ps1
```

預期：退出碼 0，產出 `Source/bin/Release/FishingFunBot.dll`、`Chrome.exe`、`Powershell.exe`。

- [ ] **Step 2: 執行全部六組回歸測試**

```
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-BiteWatcherRegression.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-HumanTimingRegression.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-BotSafetyRegression.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-LootLatencyRegression.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-WriteReductionRegression.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-CursorBehaviourRegression.ps1
```

預期：六者皆輸出 `ALL PASS` 且退出碼 0。

- [ ] **Step 3: 確認工作區乾淨並列出本次的提交**

```
git status -sb
git log --oneline -8
```

預期：`git status -sb` 沒有任何未提交的檔案（`## main...origin/main [ahead N]` 是正常的，
代表本計畫的提交尚未推送；推送不在本計畫範圍內）。

- [ ] **Step 4: 實機驗收（需要遊戲）**

依 Task 5 Step 7 記錄的項目逐項確認，並把結果寫入提交訊息或 PR 說明。
