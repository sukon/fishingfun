# 設計：游標預先就位（Cursor Pre-positioning）

- 日期：2026-11-22
- 狀態：已核准
- 對應提交基準：`97d58da`（`Let the bot space its actions out like a person`）

## 問題

一個真人玩家在《World of Warcraft》釣魚時，流程是：

1. 按投竿鍵
2. 等浮標落水
3. 把滑鼠游標移到浮標上，然後**維持不動**
4. 魚咬鉤時，只按一次右鍵即可揚竿

目前機器人的行為不是這樣：

- 拋竿後的整段等待期間（最多 25 秒），**游標完全不動**，維持在使用者原本的位置。
  `FishBot.WaitForBite()` 只呼叫 `bobberFinder.Find()`，而 `Find()` 是
  `WowScreen.GetBitmap()` 的截圖（`Source/FishingFunBot/Platform/WowScreen.cs:48`），
  純讀取畫面。
- 游標是在**揚竿的那一刻**才移到浮標上（`Source/FishingFunBot/Platform/WowProcess.cs:316`）。
- 因為游標在最後一刻才移動，程式必須讓遊戲先讀到新的滑鼠位置，
  所以右鍵前要固定等待 `LootDelay` = 1000ms
  （`Source/FishingFunBot/Platform/WowProcess.cs:154`、`WowProcess.cs:317`）。

結果是「魚咬鉤 → 點擊」的時間為 **1700–2120ms**，而真人只要 200–400ms。
那 1000ms 是機器人自己製造出來的延遲，因為它不像真人那樣預先把游標放好。

## 目標

讓機器人的游標行為與真人一致：拋竿後延遲一段隨機時間，再把游標移到浮標上並留在那裡，
直到下一次拋竿。揚竿時只需右鍵，點擊延遲因而回到真人等級。

## 非目標

- 不改變浮標偵測、咬鉤判定、每輪校準的任何邏輯。
- 不引入游標的**連續跟隨**（浮標每幀微動就重置游標）。本次採「只移一次」。
- 不改變滑鼠按鍵釋放、停止機器人、輸入鎖等既有安全機制。

## 設計

### 1. 拋竿後的隨機延遲

`FishBot.Start()` 的迴圈（`Source/FishingFunBot/Bot/FishBot.cs:103-110`）目前是：

```csharp
logger.Info($"Pressing key {castKey} to Cast.");
FishingEventHandler?.Invoke(this, new FishingEvent { Action = FishingAction.Cast });
WowProcess.PressKey(castKey);

Watch(2000);

WaitForBite();
checkForStopTimer();
```

改為：

```csharp
logger.Info($"Pressing key {castKey} to Cast.");
FishingEventHandler?.Invoke(this, new FishingEvent { Action = FishingAction.Cast });
WowProcess.PressKey(castKey);

// A person waits for the float to land before moving the cursor onto it.
int cursorDelay = HumanTiming.Current.CursorMoveDelayMs();
logger.Info($"Waiting {cursorDelay}ms before moving the cursor onto the float.");
WaitWhileEnabled(cursorDelay);

WaitForBite();
checkForStopTimer();
```

- `Watch(2000)` 的方法與呼叫點一併移除。該方法只有這一個呼叫點，其作用等同於
  「拋竿後固定停 2 秒」；`WaitForBite()` 開頭本來就會 `bobberFinder.Reset()`
  並重新等待浮標可見，因此移除不影響追蹤品質。
- 延遲必須用 `WaitWhileEnabled()` 而非 `Thread.Sleep()`，這樣按停止鈕能立即中斷。

### 2. 延遲長度

新增 `HumanTiming.CursorMoveDelayMs()`（`Source/FishingFunBot/Bot/HumanTiming.cs`）：

| 情況 | 值 |
| --- | --- |
| `human_behaviour = true` | `Skewed(700, 2200, 1.4)`，右偏分布 |
| `human_behaviour = false` | 固定 `1200` |

理由：浮標落水約需 1–2 秒，人類是在浮標落水時才把游標移過去，所以下限 700ms
不應早於拋竿動畫完成太多，上限 2200ms 對應浮標剛落水後被移過去的情況。
`human_behaviour = false` 時仍有 1200ms，是因為這是**遊戲機制需求**
（等浮標落水）而非擬人需求，關掉擬人行為不應讓機器人無法運作。

### 3. 游標移到浮標上

游標的放置放在 `WaitForBite()` 的**主觀察迴圈**裡，每一幀都詢問一次「是否該把游標
放上去」：

```csharp
while (isEnabled)
{
    observation = FindBobber();
    bool isBite = this.biteWatcher.IsBite(observation);
    trace?.Record(observation, isBite ? "bite" : "watch");

    if (observation.IsVisible && floatCursor.Place(observation.Position, WowProcess.IsMouseButtonHeldDownByUser()))
    {
        // The float has been found. A person puts the cursor on the float and
        // leaves it there, so that the right click is all that is needed when
        // the fish bites.
        WowProcess.MoveCursorOntoFloat(observation.Position);
    }

    if (isBite)
    {
        Loot(observation.Position);
        PressTenMinKey();
        return;
    }
    ...
}
```

放進主迴圈而不是「等待浮標可見」迴圈結束時，是為了讓放置動作**可以重試**：
若使用者當下正按著滑鼠鍵，這一幀不會標記為已放置，下一幀使用者鬆手後就會補上。
一旦放置成功，`floatCursor.Placed` 為 true，之後的幀不再移動游標。

延遲結束不代表浮標已入水，所以游標是在**浮標真的被看到時**才移動，不是延遲一結束
就盲移。

`WowProcess`  既有的私有方法 `IsMouseButtonHeldDown()`（`WowProcess.cs:173`）改為
**public**，並更名為 `IsMouseButtonHeldDownByUser()`，讓 `FishBot` 讀得到。
更名是為了讓「使用者按著」這件事在呼叫端讀起來不會與機器人自己的狀態混淆。

### 4. 新增 `WowProcess.MoveCursorOntoFloat(Point)`

放在 `Source/FishingFunBot/Platform/WowProcess.cs`：

```csharp
/// <summary>
/// Moves the cursor onto the float, the way a person does before waiting for
/// the fish to bite. The cursor is left there.
/// </summary>
public static void MoveCursorOntoFloat(System.Drawing.Point position)
{
    RunUnlessCancelled(() =>
    {
        var wowProcess = Get();
        if (wowProcess == null) return;

        // The user is holding a mouse button down, so the game is getting real
        // input from them. Moving the cursor then is not safe.
        if (IsMouseButtonHeldDownByUser()) return;

        // Send a button up before moving. If the game still thinks the right
        // button is down it treats the movement as a camera rotation.
        PostMessage(wowProcess.MainWindowHandle, Keys.WM_RBUTTONUP, Keys.VK_RMB, IntPtr.Zero);

        MoveCursorTo(position);
    });
}
```

三個安全條件：

1. 包在既有的 `RunUnlessCancelled` 內 → 停止中的機器人不會移動游標。
2. `IsMouseButtonHeldDown()` 為真時跳過 → 使用者正按著滑鼠時不搶。
3. 先送 `WM_RBUTTONUP` → 清掉遊戲以為右鍵還按著的狀態，否則移動會被當成鏡頭旋轉
   （issue #44 / #13 的成因）。

### 4.1 滑鼠按鍵的兩道檢查

「使用者是否正按著滑鼠鍵」有兩處檢查，兩者目的不同，都必須存在：

| 位置 | 目的 |
| --- | --- |
| `FloatCursor.Place(floatPosition, userHoldsMouseButton)` | **狀態層**。使用者按著鍵時**不**把 `placedOn` 標記為已放置，因此下一幀還能重試 |
| `WowProcess.MoveCursorOntoFloat()` 內部的 `IsMouseButtonHeldDownByUser()` | **動作層**。防止 `Place` 判定與實際移動之間使用者才剛好按下鍵的時間差 |

`FloatCursor` 是純邏輯、不碰 Win32，因此它的判定可在離線測試中驗證；
`MoveCursorOntoFloat` 的檢查則是執行期防線，兩者不互相取代。

### 5. 揚竿時的修正

既有行為已經會在點擊前重新確認浮標位置，本次不變：

- `FishBot.Loot()`（`FishBot.cs:258-306`）先 `bobberFinder.Find()` 重新找浮標，
  用 `LootPosition.Choose()` 決定點擊位置。
- `WowProcess.RightClickMouse()`（`WowProcess.cs:316`）在按鍵前 `MoveCursorTo(position)`，
  把游標修正到浮標**當下**的位置。

這就是「點擊時才修正一次」。等待期間浮標飄移並不影響，因為修正發生在點擊前。

### 6. `LootDelay` 改為隨機

`WowProcess.LootDelay`（`WowProcess.cs:154`）保留，但**語意改變**：

| | 現況 | 改後 |
| --- | --- | --- |
| 意義 | 右鍵前固定等待時間 | 右鍵前等待時間的**上限** |
| 預設值 | 1000 | **300** |
| 滑桿範圍 | 0–3000 | 100–3000 |
| 實際等待 | 固定 1000ms | `random.Next(100, LootDelay + 1)`，即 100 到上限（含）之間的整數 |

`Random.Next(min, max)` 的上界不含，因此用 `Next(100, LootDelay + 1)` 得到的是
**100 到 LootDelay 之間含兩端**的整數。亂數來源沿用 `WowProcess` 既有的
`private static Random random`（`WowProcess.cs:19`），並在既有的輸入鎖
（`InputLock`）內取用，與既有的按鍵按住時長（`WowProcess.cs:335`）同一路徑，
不會有跨執行緒共用 `Random` 的問題。

滑桿（`Source/FishingFunUI/UI/ColourConfiguration.xaml:57`）的最小值改為 100，
標籤文字（`ColourConfiguration.xaml.cs:142`）改為說明「在游標移到浮標之後、
按右鍵之前，隨機等待 100 到上限 ms」。

保留滑桿是為了讓遇到「揚竿時畫面轉向」的人仍能調高（`使用說明.md` 的疑難排解
已有此說明）。游標已停在浮標上數秒，遊戲早已讀到位置，100–300ms 足以讓它
讀到「修正後」的新位置。

### 7. 游標還原

依決策，游標**留在浮標上**，直到下一輪拋竿後才延遲移動到新浮標。

- `WowProcess.RefocusOnOldScreen()`（`WowProcess.cs:359-383`）中的
  `MoveCursorTo(oldPosition)` **移除**，並一併移除 `oldPosition` 參數。
  該位置現在就是浮標，還原等於原地不動；
  浮標飄移時反而會把游標從修正後的新位置拉回舊點。
  該方法其餘行為（右鍵點擊前景視窗取回焦點、送出 Escape）**保留**。
- 機器人**完全停止**時，游標還原到使用者原本的位置：
  - `WowProcess` 以一個靜態欄位記錄第一輪移動游標**之前**的 `Cursor.Position`。
  - `FishingBot.Stop()`（`FishBot.cs:181-193`）呼叫新的 `WowProcess.RestoreCursor()`，
    把游標移回該位置並清除記錄。

`RestoreCursor()` **不會**走 `RunUnlessCancelled`。`Stop()` 會先設定
`stopRequested` 並呼叫 `WowProcess.StopSendingInput()`，此時 `RunUnlessCancelled`
必然拒絕執行，游標就永遠還原不了。這與既有的滑鼠按鍵釋放是同一個道理
（`WowProcess.cs:47-48` 的註解：按鍵的釋放不被阻擋，因為遊戲必須被告知按鍵
已經放開）。`RestoreCursor()` 因此只取輸入鎖、不檢查取消旗標。

### 8. 可離線測試的判斷邏輯

新增 `Source/FishingFunBot/Bot/FloatCursor.cs`，**不碰 Win32**，因此可在離線測試中驗證：

```csharp
public class FloatCursor
{
    private Point? placedOn;

    /// True when the cursor has already been put on the float for this cast.
    public bool Placed => placedOn.HasValue;

    /// The place the cursor was put on.
    public Point? PlacedOn => placedOn;

    /// Puts the cursor on the float the first time the float is seen.
    /// 
    /// Returns false when it has already been done for this cast. It also
    /// returns false when the user is holding a mouse button down, and in that
    /// case the float is not marked as placed, so that the cursor can be put on
    /// it on a later frame.
    /// 
    /// The float drifting does not move the cursor again. A person leaves their
    /// cursor where it is while they wait, and the place it ends up is put
    /// right before the click.
    public bool Place(Point floatPosition, bool userHoldsMouseButton)
    {
        if (placedOn.HasValue || userHoldsMouseButton) return false;
        placedOn = floatPosition;
        return true;
    }
}
```

`FishBot` 每輪拋竿建立一個新的 `FloatCursor` 實例（等於每輪重新計數）。

**不新增「是否需要修正」的判斷。** 點擊前的位置修正是既有行為：
`LootPosition.Choose()`（`Source/FishingFunBot/Bot/LootPosition.cs`）決定該點哪裡，
`RightClickMouse()` 把游標移到那裡，`BotSafetyRegression` 已有涵蓋
（「the place the float is now is used when it is close by」等）。新增一份重複的
判定只會有兩份邏輯可能不一致。

### 9. 點擊延遲的影響

| | 現況 | 改後 |
| --- | --- | --- |
| 魚咬鉤 → 點擊 | 1700–2120ms | **800–1420ms** |
| 舊行為上限 | 2124ms | 仍低於 2124ms |

點擊前重新確認浮標位置所需的時間仍**從反應預算中扣除**（既有行為不變）。

### 10. 測試

| 測試 | 動作 |
| --- | --- |
| `tests/LootLatencyRegression.cs` | `LootDelay` 改為隨機 100–300ms；預期點擊區間改為 800–1420ms；仍須低於 2124ms |
| `tests/HumanTimingRegression.cs` | 新增：游標移動延遲落在 700–2200ms；`human_behaviour=false` 時為 1200ms；LootDelay 隨機值落在 100–300ms |
| `tests/CursorBehaviourRegression.cs`（新增） | 驗證「每輪只移一次」「浮標漂移也不會再移」「使用者按著滑鼠時不移且下一幀可補上」「新的一輪會重新計算」 |
| `tests/Run-CursorBehaviourRegression.ps1`（新增） | 對應執行腳本，形式比照 `tests/Run-BotSafetyRegression.ps1` |

### 11. 文件

- `使用說明.md`：擬人化作為表新增「游標移動延遲」；點擊延遲說明由 1000ms 改為
  隨機 100–300ms、預算上限改為 1420ms；疑難排解的 `LootDelay` 說明改為「上限」；
  測試章節補上新的測試腳本。
- `TOOLS.md`：能力索引新增 Cursor behaviour regression 一列，並新增對應章節。

## 風險

| 風險 | 緩解 |
| --- | --- |
| 揚竿時鏡頭甩動（issue #44 / #13） | 移動游標前先送 `WM_RBUTTONUP`；右鍵前仍保留 `LootDelay` 等待；滑桿可調高 |
| 等待期間滑鼠被佔用，使用者無法自由操作 | 這是真人行為的必然結果，也是本設計的目的；機器人停止時游標會還原 |
| 隨機延遲短於浮標落水時間 | 游標只在**浮標被偵測到**時才移動，延遲短只會讓移動晚一點發生 |
| 浮標落水位置與前一輪不同 | 點擊前的重新確認與修正會帶游標到當下位置 |
| 遊戲是否真的在該位置撿起魚 | 離線測試無法驗證，需實機確認 |

## 完成定義

- `tools/Build-Release.ps1` 成功，產出 `Source/bin/Release`。
- 全部回歸測試（含新增的 Cursor behaviour regression）ALL PASS。
- `使用說明.md` 與 `TOOLS.md` 已同步更新。
- 實機驗證：拋竿後游標自動移至浮標並停留；魚咬鉤後能在 1.5 秒內點擊到浮標；
  揚竿時畫面不甩動。
