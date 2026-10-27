# MemSweep Phase 04：設定與發佈 實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**目標：** 新增 `data\settings.json` 與設定視窗，讓橘色、紅色門檻、尖峰紀錄保留天數與開機自動啟動可以調整並立即生效；右鍵選單加「設定…」；併入 PR #3 延後的三項小修；寫好發佈指令與 README 使用說明，並完成 A-01～A-09 的完整驗收。

**架構：** 與介面無關的邏輯放在 MemSweep.Core，可以直接單元測試：`AppSettings` 是設定值、預設值與驗證；`SettingsStore` 讀寫 `data\settings.json`，和尖峰紀錄共用 `AtomicFile`（先寫暫存檔並寫入磁碟，再取代原檔）；`SettingsForm` 是設定視窗的輸入與檢查。MemSweep.Desktop：`AutoStart` 透過 `IAutoStartRegistry` 寫入 HKCU 的 Run 機碼；`SettingsService` 負責儲存順序、失敗訊息與變更通知；`TrayController` 以目前設定的門檻畫圖示並開啟 `SettingsWindow`（沿用 `TitleBar`）；右鍵選單抽成 `TrayMenu`；`PeakRecorder` 接收新的保留天數。

**技術：** C#、.NET 10、WPF（軟體繪圖）、WinForms `NotifyIcon`、`System.Text.Json` 原始碼產生、`Microsoft.Win32.Registry`、MSTest 4.5.1（含 `[STATestMethod]`）。

**依據：** [SPEC](../spec.md)、[Design](../design.md)（含隨本計畫提交的修訂）。本計畫引用的條文都是原文；計畫自己的解讀集中在「計畫的解讀」一節。

**分支：** `phase-04-settings-release`（開工時從 `origin/main` 建立）。

## 全域限制

以下條文適用於每個 Task：

> - Identifiers are English: code symbols, file and folder names, and branch names.
> - Documents, code comments, and commit messages are written in Traditional Chinese (Taiwan).
> - Commit subjects are Traditional Chinese only, with no type prefix such as `feat:` or `fix:`.
> - Push after each completed Task. Open the pull request only after every Task in the Phase is done.
> - Running anything that changes system memory state requires the user's explicit approval for that run.
> - Do not start elevated (administrator) processes or change system settings such as the page file without the user's explicit approval.
> - Never terminate the user's processes to free memory.
> - Run every `dotnet` command inside `app/`, because `app/global.json` pins the SDK, the MSTest.Sdk version, and the test runner. For example: `(cd app && dotnet build)`.
> - 登錄操作包在 `IAutoStartRegistry` 介面後面。測試使用假的實作；開發期間不在使用者電腦上實際切換（AGENTS.md「System Safety」）。
> - E-3 免安裝：兩支 exe 放在使用者有寫入權限的資料夾就能執行。不可放在 `C:\Program Files` 等需要系統管理員權限才能寫入的位置。
> - E-4 以一般使用者權限即可使用全部功能。
> - X-1 MemSweep 本身成為記憶體大戶。常駐時本身的 commit 目標不超過 100 MB。〔Claude 決定〕

## Review Focus

SPEC 沒有直接寫到、但實際使用最可能出問題的情況。每一項都已在負責的 Task 裡加上測試：

1. 使用者在注音輸入法開著時輸入門檻或天數，或貼上全形數字：輸入框關閉輸入法，直接打出半形數字；貼上的全形數字也接受。Task 2 `FullWidthDigitsAndSurroundingSpacesAreAccepted`，Task 5 `NumberBoxesTurnOffTheInputMethod`。
2. 使用者用記事本改壞 `settings.json`（少一個欄位、數字寫成字串、門檻順序顛倒）：MemSweep 照常啟動並使用預設值，下次在設定視窗儲存時覆寫。Task 1 `InvalidContentGivesTheDefaults`、`InvalidContentIsOverwrittenByTheNextSave`。
3. `data\` 無法寫入或在執行中被刪除：設定視窗說明原因、不關閉，目前的設定與開機項目都不變；尖峰紀錄在下一次寫入時重新建立資料夾。Task 1 `SaveRecreatesADataFolderDeletedWhileRunning`，Task 3 `AFailedWriteKeepsTheCurrentSettingsAndTheRegistry`，Task 5 `ASaveFailureIsShownAndTheWindowStaysOpen`。
4. 使用者把 MemSweep 資料夾搬到別處，或電腦上有兩份 MemSweep：開機自動啟動開著時，啟動就更新登記的路徑；只改門檻或天數時不動登錄，不會刪掉另一份登記的值。Task 3 `StartupSyncUpdatesAMovedExe`、`OtherChangesLeaveTheRegistryAlone`、`AnUnchangedPathIsNotRewritten`。
5. 使用者把保留天數從 90 改成 7：超過天數的紀錄在下一次取樣時刪除（設定視窗事先說明），不必等到隔天或重新啟動。Task 4 `ALowerRetentionIsAppliedAtTheNextSample`。

## 計畫的解讀

以下是本計畫自己的決定，不是 SPEC 或 Design 的原文。Design 3.6、3.8、3.9 與新的 3.12 已隨本計畫修訂，記下會影響使用者的部分：

- `settings.json` 的四個欄位都必須存在且型別正確，任何一項不符合或違反驗證規則，就整份使用預設值（不逐欄補預設值：門檻是一組，只留下一半容易違反「橘色 < 紅色」）；不認得的欄位略過，留給以後的版本。以 `System.Text.Json` 原始碼產生讀寫，`RespectRequiredConstructorParameters` 讓缺欄位成為錯誤。
- 讀取設定不會建立 `data\`；第一次儲存時才建立。寫入沿用尖峰紀錄的做法（先寫暫存檔、寫入磁碟、再取代），抽成 `AtomicFile`，兩者共用；`AtomicFile` 寫入前會建立資料夾，順便修正 PR #3 延後的「執行中刪除 `data\` 後寫入一直失敗」。
- 門檻與天數的輸入框是文字，按「儲存」才檢查，只顯示第一個錯誤：「門檻必須是 1～100 的整數」「橘色門檻必須小於紅色門檻」「保留天數必須是 1～3650 的整數」。去掉前後空白，全形數字以相容分解（FormKC）轉成半形；不接受正負號、小數點與千分位。
- 儲存順序（Design 3.8）：先寫 `settings.json`，成功後才更新目前設定並通知變更，最後才動登錄，而且只在開機自動啟動的開關改變時。開關打開時，登記的內容與 `"<exe 完整路徑>"` 不分大小寫相同就不重寫。啟動時只在設定為開啟時檢查路徑，失敗不顯示，下次啟動再試。
- 新的門檻立即套用：`TrayController` 以最近一次取樣重畫圖示、更新小面板與完整視窗。新的保留天數在下一次取樣時生效：`PeakRecorder.SetRetentionDays` 讓下一次取樣重新清理（原本只在啟動後第一次取樣與跨日時清理）。
- 設定視窗固定大小（`ResizeMode="NoResize"`、`SizeToContent="Height"`），`TitleBar` 依 `ResizeMode` 隱藏按鈕：`NoResize` 只留關閉，`CanMinimize` 隱藏最大化。和完整視窗一樣第一次開啟時才建立、關閉時真正關閉並在 UI 空閒時回收記憶體（兩者共用 `ReleaseMemoryWhenIdle`），因為 A-03 的餘裕只剩約 1.3 MB。
- 小面板的按鈕樣式移到 `Styles.xaml`，小面板與設定視窗共用。設定視窗另有跟著深淺色的輸入框與核取方塊，原型以離線繪製確認淺色、深色與錯誤訊息的外觀。
- 右鍵選單抽成 `TrayMenu.Build`，才能以單元測試確認 F-07 的四個項目與順序（`TrayController` 會建立系統匣圖示，不適合單元測試）。
- `RunKeyRegistry` 直接讀寫使用者的登錄，不寫單元測試（Design 3.8 規定開發期間不實際切換）；只測它的機碼路徑與值名稱。這兩個是 `static readonly` 而不是 `const`：MSTest 的 MSTEST0032 會把常數之間的比較視為永遠成立而建置失敗。實際切換留到「Phase 收尾」，由使用者同意後確認。
- 併入 PR #3 延後的小問題（Task 6）：紀錄檔裡換算成 UTC 會超出範圍的時間與大於 1 PB 的 GB 數值，原本會讓啟動當掉，改為「格式不符」；`PeakCsv` 的前五名欄數改用 `ReportFormatter.TopCount`；第一次取樣前按「完整清單…」改為記下，取樣完成後開啟（同小面板）。加上 Task 1 的資料夾重建，共四項。其餘延後項目（結束時 `Flush` 與最後一次取樣的競爭、啟動時暫時的共用違規、完整視窗回到螢幕中央、小面板沒拿到焦點時的收起、CLI 以相對路徑引用圖示、PR #2 的 B、C、D 組）不影響這次發佈，維持延後。
- 發佈（Design 3.12）：兩個 `dotnet publish` 指令輸出到同一個資料夾，保留 pdb 以便當機時看得到行號。AGENTS.md 與 AGENTS_cht.md 的發佈指令是規則檔，隨本計畫直接提交到 main；README 屬於這個 Phase 的成果，在分支上修改（Task 7）。

## 共同執行規則

- 工作目錄是 repo 根目錄 `D:\AxeenWorld\CodeLab\Projects\MemSweep`，分支 `phase-04-settings-release`。
- `dotnet` 指令一律在 `app/` 底下執行，用子殼層避免改變目前目錄，例如 `(cd app && dotnet test)`。
- 程式碼區塊要逐字照抄，包括全形符號：`：`、`（`、`）`、`「`、`」`、`、`、`…`、`～`、`≤`、`³`。XAML 的屬性順序與縮排也照抄。不要刪除或移動步驟沒有提到的任何一行。
- 測試就是規格：不得修改或刪除測試裡的任何斷言。照抄後測試仍失敗，就回報 BLOCKED。
- 測試會在測試輸出資料夾（`app/artifacts/bin/MemSweep.Core.Tests/`）底下建立暫時的 `PeakStoreTests\`、`SettingsStoreTests\`、`SettingsServiceTests\` 資料夾並自行刪除。開機自動啟動的測試一律用 `FakeAutoStartRegistry`，不碰真正的登錄。`[STATestMethod]` 的測試會在 STA 執行緒建立 WPF 控制項，但不顯示視窗。即時測試全部是唯讀查詢。不得執行任何會改變系統記憶體狀態的動作，也不得結束使用者的行程。
- 需要的暫存檔放在 `.camp_claude/` 的任務資料夾。commit 訊息用 Bash 的 heredoc 傳給 `git commit -F -`，不要用 PowerShell 寫訊息檔。
- commit 訊息與 `Co-Authored-By` 尾行照計畫原樣使用。
- 每個 Task 完成後推送。推送前先 `git fetch origin`，並確認遠端分支仍存在、沒有已合併或已關閉的 PR：

  ```bash
  git fetch origin
  git ls-remote --heads origin phase-04-settings-release
  gh pr list --head phase-04-settings-release --state all
  git push
  ```

  Task 1 是第一次推送，遠端還沒有這個分支，改用 `git push -u origin phase-04-settings-release`。

---

### Task 1：設定檔的讀寫

**檔案：**

- 新增：`app/MemSweep.Core/AtomicFile.cs`、`app/MemSweep.Core/AppSettings.cs`、`app/MemSweep.Core/SettingsStore.cs`、`app/MemSweep.Core.Tests/SettingsStoreTests.cs`
- 修改（整份換成）：`app/MemSweep.Core/PeakStore.cs`、`app/MemSweep.Core.Tests/PeakStoreTests.cs`

**實作的條文：**

> - F-50 可設定的項目：橘色門檻、紅色門檻、開機自動啟動、尖峰紀錄保留天數。
> - F-51 開機自動啟動預設關閉。開啟時以目前使用者的身分登記，不需要系統管理員權限。〔Claude 決定〕
> - F-52 設定與尖峰紀錄都存放在 exe 所在資料夾下的 `data\` 子資料夾。
> - F-03 數字顏色依門檻變化：低於橘色門檻為綠色；達到橘色門檻但低於紅色門檻為橘色；達到紅色門檻以上為紅色。門檻可在設定調整，預設橘色 70%、紅色 85%。〔預設值：Claude 決定〕
> - F-33 每次開機一筆、每天一筆。保留天數可設定，預設 90 天，超過的紀錄自動刪除。〔預設值：Claude 決定〕

> - 設定視窗（F-50）：橘色門檻、紅色門檻、開機自動啟動、保留天數。驗證規則：1 ≤ 橘色 < 紅色 ≤ 100；保留天數 1～3650。
> 檔案不存在或內容不合法時使用預設值，並在下次儲存時覆寫。四個欄位都必須存在且型別正確，任何一項不符合或違反 3.6 的驗證規則，就整份使用預設值；不認得的欄位略過。檔案是不含 BOM 的 UTF-8、CRLF 換行，和紀錄檔一樣先寫暫存檔再取代。
> - 寫入失敗（例如 Excel 開著檔案）時不顯示警告，保留變動，到下一個寫入時機再試。寫入時 `data\` 不存在（例如執行中被刪除）就重新建立。

`settings.json` 的格式（Design 3.9）：

```json
{
  "orangeThreshold": 70,
  "redThreshold": 85,
  "autoStart": false,
  "retentionDays": 90
}
```

**介面：**

- 使用：`UsageThresholds`（Phase 02）；`PeakStore`、`PeakCsv`（Phase 03）。
- 產生：
  - `AppSettings(int OrangeThreshold, int RedThreshold, bool AutoStart, int RetentionDays)`；`AppSettings.Default`；`UsageThresholds Thresholds`；`bool IsValid`；`AppSettings.MinRetentionDays`、`AppSettings.MaxRetentionDays`
  - `SettingsStore(string directory)`；`string FilePath`；`AppSettings Load()`；`void Save(AppSettings settings)`；`SettingsStore.FileName`
  - `AtomicFile.Write(string path, byte[] content)`（`internal`，Core 內部與測試使用）

- [ ] **Step 1：寫失敗的測試**

新增 `app/MemSweep.Core.Tests/SettingsStoreTests.cs`：

```csharp
using System.Text;
using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class SettingsStoreTests
{
    // 測試資料夾放在測試輸出資料夾底下，不碰 repo 以外的位置
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "SettingsStoreTests", Guid.NewGuid().ToString("N"));

    private string DataDirectory => Path.Combine(_root, "data");

    private string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void WriteSettings(string json)
    {
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(SettingsPath, json);
    }

    [TestMethod]
    public void DefaultsFollowTheSpec()
    {
        Assert.AreEqual(new AppSettings(70, 85, false, 90), AppSettings.Default);
        Assert.AreEqual(UsageThresholds.Default, AppSettings.Default.Thresholds);
        Assert.IsTrue(AppSettings.Default.IsValid);
    }

    [TestMethod]
    [DataRow(1, 2, 1, true)]
    [DataRow(99, 100, 3650, true)]
    [DataRow(0, 85, 90, false)]
    [DataRow(85, 85, 90, false)]
    [DataRow(86, 85, 90, false)]
    [DataRow(70, 101, 90, false)]
    [DataRow(70, 85, 0, false)]
    [DataRow(70, 85, 3651, false)]
    public void ValidationFollowsTheDesign(int orange, int red, int retentionDays, bool valid)
    {
        Assert.AreEqual(valid, new AppSettings(orange, red, false, retentionDays).IsValid);
    }

    [TestMethod]
    public void AMissingFileGivesTheDefaultsWithoutCreatingAnything()
    {
        var settings = new SettingsStore(DataDirectory).Load();

        Assert.AreEqual(AppSettings.Default, settings);
        Assert.IsFalse(Directory.Exists(DataDirectory));
    }

    [TestMethod]
    public void SavedSettingsAreReadBack()
    {
        var store = new SettingsStore(DataDirectory);
        var saved = new AppSettings(60, 90, true, 30);

        store.Save(saved);

        Assert.AreEqual(SettingsPath, store.FilePath);
        Assert.AreEqual(saved, new SettingsStore(DataDirectory).Load());
    }

    [TestMethod]
    public void SaveWritesTheDesignFormat()
    {
        new SettingsStore(DataDirectory).Save(AppSettings.Default);

        byte[] written = File.ReadAllBytes(SettingsPath);
        // 沒有 BOM
        Assert.AreEqual((byte)'{', written[0]);
        Assert.AreEqual(
            "{\r\n  \"orangeThreshold\": 70,\r\n  \"redThreshold\": 85,\r\n  \"autoStart\": false,\r\n  \"retentionDays\": 90\r\n}",
            Encoding.UTF8.GetString(written));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("不是 JSON")]
    [DataRow("null")]
    [DataRow("[]")]
    [DataRow("""{"orangeThreshold":70,"redThreshold":85,"autoStart":false}""")]
    [DataRow("""{"orangeThreshold":60,"redThreshold":90,"retentionDays":30}""")]
    [DataRow("""{"orangeThreshold":"70","redThreshold":85,"autoStart":false,"retentionDays":90}""")]
    [DataRow("""{"orangeThreshold":70.5,"redThreshold":85,"autoStart":false,"retentionDays":90}""")]
    [DataRow("""{"orangeThreshold":85,"redThreshold":85,"autoStart":true,"retentionDays":90}""")]
    [DataRow("""{"orangeThreshold":70,"redThreshold":85,"autoStart":true,"retentionDays":0}""")]
    public void InvalidContentGivesTheDefaults(string json)
    {
        WriteSettings(json);

        Assert.AreEqual(AppSettings.Default, new SettingsStore(DataDirectory).Load());
    }

    [TestMethod]
    public void UnknownFieldsAreIgnored()
    {
        WriteSettings("""{"orangeThreshold":60,"redThreshold":90,"autoStart":true,"retentionDays":30,"theme":"dark"}""");

        Assert.AreEqual(new AppSettings(60, 90, true, 30), new SettingsStore(DataDirectory).Load());
    }

    [TestMethod]
    public void InvalidContentIsOverwrittenByTheNextSave()
    {
        WriteSettings("不是 JSON");
        var store = new SettingsStore(DataDirectory);

        store.Save(store.Load() with { RetentionDays = 30 });

        Assert.AreEqual(AppSettings.Default with { RetentionDays = 30 }, store.Load());
    }

    [TestMethod]
    public void ReadsWhileAnotherProgramHasTheFileOpen()
    {
        new SettingsStore(DataDirectory).Save(new AppSettings(60, 90, true, 30));

        // 模擬文字編輯器：以讀寫方式開啟，只允許其他程式讀取
        using var editor = new FileStream(SettingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);

        Assert.AreEqual(new AppSettings(60, 90, true, 30), new SettingsStore(DataDirectory).Load());
    }

    [TestMethod]
    public void SaveFailsWhenTheDataFolderCannotBeCreated()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(DataDirectory, "不是資料夾");

        Assert.Throws<IOException>(() => new SettingsStore(DataDirectory).Save(AppSettings.Default));
    }
}
```

`app/MemSweep.Core.Tests/PeakStoreTests.cs` 整份換成（新增 `SaveRecreatesADataFolderDeletedWhileRunning`）：

```csharp
using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class PeakStoreTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time");
    private static readonly DateTimeOffset PeakTime = new(2026, 10, 26, 14, 35, 2, TimeSpan.FromHours(8));

    // 測試資料夾放在測試輸出資料夾底下，不碰 repo 以外的位置
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "PeakStoreTests", Guid.NewGuid().ToString("N"));

    private string DataDirectory => Path.Combine(_root, "data");

    private static long Gb(double value) => MemoryMath.FromGigabytes(value);

    private static PeakHistory History(int days) =>
        new(
            [new BootPeak(new DateTime(2026, 10, 26, 0, 30, 0, DateTimeKind.Utc), Gb(36.8), Gb(39.4), PeakTime, [new PeakApp("Claude", Gb(3.2))])],
            Enumerable.Range(0, days)
                .Select(i => new DailyPeak(new DateOnly(2026, 10, 26).AddDays(-i), Gb(31.4), Gb(39.4), PeakTime.AddDays(-i), [new PeakApp("Claude", Gb(3.2))]))
                .Reverse()
                .ToList());

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [TestMethod]
    public void OpenCreatesTheDataFolder()
    {
        var opening = PeakStore.Open(DataDirectory, Zone);

        Assert.IsTrue(Directory.Exists(DataDirectory));
        Assert.IsNotNull(opening.Store);
        Assert.IsNull(opening.Warning);
        Assert.IsEmpty(opening.History.Boots);
        Assert.IsEmpty(opening.History.Days);
        Assert.IsEmpty(Directory.GetFileSystemEntries(DataDirectory));
    }

    [TestMethod]
    public void SavedRecordsAreReadBackOnTheNextOpen()
    {
        PeakStore.Open(DataDirectory, Zone).Store!.Save(History(days: 2));

        var opening = PeakStore.Open(DataDirectory, Zone);

        CollectionAssert.AreEquivalent(
            new[] { "peaks-boot.csv", "peaks-daily.csv" },
            Directory.GetFiles(DataDirectory).Select(Path.GetFileName).ToArray());
        Assert.HasCount(1, opening.History.Boots);
        Assert.AreEqual(PeakTime, opening.History.Boots[0].Time);
        CollectionAssert.AreEqual(
            new[] { new DateOnly(2026, 10, 25), new DateOnly(2026, 10, 26) },
            opening.History.Days.Select(d => d.Date).ToArray());
    }

    [TestMethod]
    public void SaveWritesTheCsvFormat()
    {
        PeakStore.Open(DataDirectory, Zone).Store!.Save(History(days: 1));

        byte[] written = File.ReadAllBytes(Path.Combine(DataDirectory, "peaks-daily.csv"));

        CollectionAssert.AreEqual(PeakCsv.WriteDays(History(days: 1).Days, Zone), written);
    }

    [TestMethod]
    public void SaveReplacesTheWholeFile()
    {
        var store = PeakStore.Open(DataDirectory, Zone).Store!;
        store.Save(History(days: 3));

        store.Save(History(days: 1));

        Assert.HasCount(1, PeakStore.Open(DataDirectory, Zone).History.Days);
    }

    [TestMethod]
    public void SaveRecreatesADataFolderDeletedWhileRunning()
    {
        var store = PeakStore.Open(DataDirectory, Zone).Store!;
        Directory.Delete(DataDirectory, recursive: true);

        store.Save(History(days: 2));

        Assert.HasCount(2, PeakStore.Open(DataDirectory, Zone).History.Days);
    }

    [TestMethod]
    public void AFailedSaveLeavesTheOldFileIntact()
    {
        var store = PeakStore.Open(DataDirectory, Zone).Store!;
        store.Save(History(days: 1));
        string path = Path.Combine(DataDirectory, "peaks-daily.csv");
        byte[] before = File.ReadAllBytes(path);
        // 暫存檔的位置被資料夾佔住，寫到一半就失敗
        Directory.CreateDirectory(path + ".tmp");

        Assert.Throws<Exception>(() => store.Save(History(days: 3)));

        CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
    }

    [TestMethod]
    public void AFolderThatCannotBeCreatedDisablesTheStore()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(DataDirectory, "不是資料夾");

        var opening = PeakStore.Open(DataDirectory, Zone);

        Assert.IsNull(opening.Store);
        Assert.AreEqual("data 資料夾無法寫入，尖峰紀錄已停用", opening.Warning);
        Assert.IsEmpty(opening.History.Days);
    }

    [TestMethod]
    public void AFolderThatCannotBeWrittenDisablesTheStore()
    {
        // 試寫的暫存檔位置被資料夾佔住，等同沒有寫入權限
        Directory.CreateDirectory(Path.Combine(DataDirectory, "peaks-boot.csv.tmp"));

        var opening = PeakStore.Open(DataDirectory, Zone);

        Assert.IsNull(opening.Store);
        Assert.AreEqual("data 資料夾無法寫入，尖峰紀錄已停用", opening.Warning);
    }

    [TestMethod]
    [DataRow("peaks-boot.csv")]
    [DataRow("peaks-daily.csv")]
    public void AnUnreadableFileDisablesTheStoreAndIsLeftAlone(string fileName)
    {
        PeakStore.Open(DataDirectory, Zone).Store!.Save(History(days: 1));
        string path = Path.Combine(DataDirectory, fileName);
        byte[] broken = [0xB6, 0x7D, 0xBE, 0xF7];
        File.WriteAllBytes(path, broken);

        var opening = PeakStore.Open(DataDirectory, Zone);

        Assert.IsNull(opening.Store);
        Assert.AreEqual(fileName + " 無法讀取，尖峰紀錄已停用", opening.Warning);
        Assert.IsEmpty(opening.History.Boots);
        CollectionAssert.AreEqual(broken, File.ReadAllBytes(path));
    }

    [TestMethod]
    public void ReadsWhileAnotherProgramHasTheFileOpen()
    {
        PeakStore.Open(DataDirectory, Zone).Store!.Save(History(days: 2));

        // 模擬 Excel：以讀寫方式開啟，只允許其他程式讀取
        using var excel = new FileStream(Path.Combine(DataDirectory, "peaks-daily.csv"), FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        var opening = PeakStore.Open(DataDirectory, Zone);

        Assert.IsNotNull(opening.Store);
        Assert.HasCount(2, opening.History.Days);
    }

    [TestMethod]
    public void SaveFailsWhileAnotherProgramHasTheFileOpenAndSucceedsLater()
    {
        var store = PeakStore.Open(DataDirectory, Zone).Store!;
        store.Save(History(days: 1));

        using (new FileStream(Path.Combine(DataDirectory, "peaks-boot.csv"), FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
        {
            // 被鎖住的檔案無法取代時，Windows 回報「存取被拒」或共用違規，兩種都可能
            var error = Assert.Throws<Exception>(() => store.Save(History(days: 2)));
            Assert.IsTrue(error is IOException or UnauthorizedAccessException, error.GetType().Name);
            Assert.IsFalse(File.Exists(Path.Combine(DataDirectory, "peaks-boot.csv.tmp")));
        }

        store.Save(History(days: 2));
        Assert.HasCount(2, PeakStore.Open(DataDirectory, Zone).History.Days);
    }
}
```

- [ ] **Step 2：執行測試，確認失敗**

執行：`(cd app && dotnet test)`

預期：建置失敗，錯誤是找不到 `SettingsStore` 與 `AppSettings`（CS0246、CS0103）。

- [ ] **Step 3：實作**

新增 `app/MemSweep.Core/AtomicFile.cs`：

```csharp
namespace MemSweep.Core;

/// <summary>data\ 裡的檔案一律先寫暫存檔再取代原檔，寫到一半中斷也不會留下殘缺的檔案（Design 3.9）。</summary>
internal static class AtomicFile
{
    /// <summary>資料夾不存在時（例如執行中被刪除）先建立；失敗時丟出 <see cref="IOException"/> 或 <see cref="UnauthorizedAccessException"/>。</summary>
    public static void Write(string path, byte[] content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            stream.Write(content);
            // 只寫進系統快取就取代原檔的話，斷電或當機後原檔可能整份是 0 而無法讀取
            stream.Flush(flushToDisk: true);
        }
        try
        {
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            File.Delete(temp);
            throw;
        }
    }
}
```

新增 `app/MemSweep.Core/AppSettings.cs`：

```csharp
using System.Text.Json.Serialization;

namespace MemSweep.Core;

/// <summary>設定視窗可以調整的項目（F-50），存成 data\settings.json（Design 3.9）。</summary>
public sealed record AppSettings(int OrangeThreshold, int RedThreshold, bool AutoStart, int RetentionDays)
{
    public const int MinRetentionDays = 1;
    public const int MaxRetentionDays = 3650;

    /// <summary>橘色 70%、紅色 85%（F-03），開機自動啟動關閉（F-51），保留 90 天（F-33）。</summary>
    public static AppSettings Default { get; } =
        new(UsageThresholds.Default.OrangePercent, UsageThresholds.Default.RedPercent, false, 90);

    [JsonIgnore]
    public UsageThresholds Thresholds => new(OrangeThreshold, RedThreshold);

    /// <summary>1 ≤ 橘色 &lt; 紅色 ≤ 100；保留天數 1～3650（Design 3.6）。</summary>
    [JsonIgnore]
    public bool IsValid =>
        OrangeThreshold >= 1 && OrangeThreshold < RedThreshold && RedThreshold <= 100
        && RetentionDays is >= MinRetentionDays and <= MaxRetentionDays;
}
```

新增 `app/MemSweep.Core/SettingsStore.cs`：

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MemSweep.Core;

/// <summary>data\ 資料夾裡的 settings.json（F-50、F-52、Design 3.9）。</summary>
public sealed class SettingsStore
{
    public const string FileName = "settings.json";

    public SettingsStore(string directory)
    {
        FilePath = Path.Combine(directory, FileName);
    }

    public string FilePath { get; }

    /// <summary>檔案不存在、無法讀取或內容不合法時使用預設值，下次儲存時覆寫（Design 3.9）。</summary>
    public AppSettings Load()
    {
        try
        {
            // 使用者用其他程式開著檔案時也要讀得到
            using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var settings = JsonSerializer.Deserialize(stream, SettingsJson.Default.AppSettings);
            return settings is { IsValid: true } ? settings : AppSettings.Default;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return AppSettings.Default;
        }
    }

    /// <summary>寫入全部設定，資料夾不存在時建立；失敗時丟出 <see cref="IOException"/> 或 <see cref="UnauthorizedAccessException"/>。</summary>
    public void Save(AppSettings settings) =>
        AtomicFile.Write(FilePath, JsonSerializer.SerializeToUtf8Bytes(settings, SettingsJson.Default.AppSettings));
}

/// <summary>settings.json 的格式：欄位名稱為小駝峰，四個欄位都必須存在，換行與尖峰紀錄 CSV 一樣用 CRLF。</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    NewLine = "\r\n",
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext;
```

`app/MemSweep.Core/PeakStore.cs` 整份換成（`Replace` 改用 `AtomicFile`）：

```csharp
namespace MemSweep.Core;

/// <summary>寫入尖峰紀錄的地方；測試以假的實作取代。</summary>
public interface IPeakStore
{
    /// <summary>寫入全部紀錄；失敗時丟出 <see cref="IOException"/> 或 <see cref="UnauthorizedAccessException"/>。</summary>
    void Save(PeakHistory history);
}

/// <summary>開啟 data\ 的結果：Store 為 null 表示尖峰紀錄已停用，Warning 是顯示給使用者的原因。</summary>
public sealed record PeakStoreOpening(PeakHistory History, IPeakStore? Store, string? Warning);

/// <summary>data\ 資料夾裡的 peaks-boot.csv 與 peaks-daily.csv（F-34、F-52、Design 3.9）。</summary>
public sealed class PeakStore : IPeakStore
{
    public const int DefaultRetentionDays = 90;
    public const string UnwritableWarning = "data 資料夾無法寫入，尖峰紀錄已停用";

    private readonly string _directory;
    private readonly TimeZoneInfo _zone;

    private PeakStore(string directory, TimeZoneInfo zone)
    {
        _directory = directory;
        _zone = zone;
    }

    /// <summary>
    /// 建立資料夾（不存在時）並載入紀錄。資料夾無法寫入，或既有的紀錄檔無法讀取時停用尖峰紀錄，
    /// 也不會覆寫那些檔案；其他功能照常（Design 3.9）。
    /// </summary>
    public static PeakStoreOpening Open(string directory, TimeZoneInfo zone)
    {
        try
        {
            Directory.CreateDirectory(directory);
            // 資料夾已存在但沒有寫入權限時 CreateDirectory 不會失敗，所以實際建立一個暫存檔試試
            using var probe = new FileStream(
                Path.Combine(directory, PeakCsv.BootFileName + ".tmp"),
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                1,
                FileOptions.DeleteOnClose);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new PeakStoreOpening(PeakHistory.Empty, null, UnwritableWarning);
        }

        var store = new PeakStore(directory, zone);
        string fileName = PeakCsv.BootFileName;
        try
        {
            var boots = store.Read(fileName, PeakCsv.ReadBoots);
            fileName = PeakCsv.DailyFileName;
            var days = store.Read(fileName, PeakCsv.ReadDays);
            return new PeakStoreOpening(new PeakHistory(boots, days), store, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            return new PeakStoreOpening(PeakHistory.Empty, null, fileName + " 無法讀取，尖峰紀錄已停用");
        }
    }

    public void Save(PeakHistory history)
    {
        // 執行中 data\ 被刪除時由 AtomicFile 重新建立，之後的寫入就恢復
        AtomicFile.Write(Path.Combine(_directory, PeakCsv.BootFileName), PeakCsv.WriteBoots(history.Boots, _zone));
        AtomicFile.Write(Path.Combine(_directory, PeakCsv.DailyFileName), PeakCsv.WriteDays(history.Days, _zone));
    }

    private List<T> Read<T>(string fileName, Func<byte[], TimeZoneInfo, List<T>> parse)
    {
        string path = Path.Combine(_directory, fileName);
        if (!File.Exists(path))
        {
            return [];
        }

        // 其他程式（例如 Excel）開著檔案時也要讀得到
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var content = new MemoryStream();
        stream.CopyTo(content);
        return parse(content.ToArray(), _zone);
    }
}
```

- [ ] **Step 4：執行測試，確認通過**

執行：`(cd app && dotnet test)`

預期：總計 244，失敗 0。

- [ ] **Step 5：Commit**

```bash
git add app/MemSweep.Core/AtomicFile.cs app/MemSweep.Core/AppSettings.cs app/MemSweep.Core/SettingsStore.cs app/MemSweep.Core/PeakStore.cs app/MemSweep.Core.Tests/SettingsStoreTests.cs app/MemSweep.Core.Tests/PeakStoreTests.cs
git status --short
git commit -F - <<'EOF'
新增設定檔的讀寫

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

- [ ] **Step 6：推送**（依「共同執行規則」先 fetch 並檢查；第一次推送用 `git push -u origin phase-04-settings-release`）

---

### Task 2：設定視窗的輸入檢查

**檔案：**

- 新增：`app/MemSweep.Core/SettingsForm.cs`、`app/MemSweep.Core.Tests/SettingsFormTests.cs`

**實作的條文：**

> - 設定視窗（F-50）：橘色門檻、紅色門檻、開機自動啟動、保留天數。驗證規則：1 ≤ 橘色 < 紅色 ≤ 100；保留天數 1～3650。
>   - 數字用輸入框，關閉輸入法，注音模式下也直接打出半形數字；貼上的全形數字也接受。按「儲存」（Enter）時才檢查，不合法時在按鈕上方顯示第一個錯誤，視窗不關閉；「取消」（Esc）不儲存就關閉。

**介面：**

- 使用：Task 1 的 `AppSettings`；`ObservableObject`（Phase 03）。
- 產生：
  - `SettingsForm(AppSettings current)`；可繫結的 `string OrangeText`、`string RedText`、`bool AutoStart`、`string RetentionText`、`string? Error`
  - `AppSettings? TryRead()`：合法時清除錯誤並回傳設定，否則設定 `Error` 並回傳 null
  - `void ShowError(string message)`；`SettingsForm.ThresholdError`、`OrderError`、`RetentionError`

- [ ] **Step 1：寫失敗的測試**

新增 `app/MemSweep.Core.Tests/SettingsFormTests.cs`：

```csharp
using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class SettingsFormTests
{
    private static SettingsForm Form(string orange, string red, string retention, bool autoStart = false) =>
        new(AppSettings.Default) { OrangeText = orange, RedText = red, RetentionText = retention, AutoStart = autoStart };

    [TestMethod]
    public void StartsWithTheCurrentSettings()
    {
        var form = new SettingsForm(new AppSettings(60, 90, true, 30));

        Assert.AreEqual("60", form.OrangeText);
        Assert.AreEqual("90", form.RedText);
        Assert.IsTrue(form.AutoStart);
        Assert.AreEqual("30", form.RetentionText);
        Assert.IsNull(form.Error);
    }

    [TestMethod]
    public void ValidInputGivesTheSettings()
    {
        var form = Form("65", "80", "7", autoStart: true);

        Assert.AreEqual(new AppSettings(65, 80, true, 7), form.TryRead());
        Assert.IsNull(form.Error);
    }

    [TestMethod]
    [DataRow("1", "2", "1")]
    [DataRow("99", "100", "3650")]
    public void BoundaryValuesAreAccepted(string orange, string red, string retention)
    {
        var settings = Form(orange, red, retention).TryRead();

        Assert.IsNotNull(settings);
        Assert.IsTrue(settings.IsValid);
    }

    [TestMethod]
    public void FullWidthDigitsAndSurroundingSpacesAreAccepted()
    {
        var form = Form("７０", " 85 ", "９０");

        Assert.AreEqual(new AppSettings(70, 85, false, 90), form.TryRead());
    }

    [TestMethod]
    [DataRow("", "85")]
    [DataRow("abc", "85")]
    [DataRow("0", "85")]
    [DataRow("70", "101")]
    [DataRow("-5", "85")]
    [DataRow("+70", "85")]
    [DataRow("70.5", "85")]
    [DataRow("1,000", "85")]
    public void ThresholdsMustBeWholeNumbersFromOneToHundred(string orange, string red)
    {
        var form = Form(orange, red, "90");

        Assert.IsNull(form.TryRead());
        Assert.AreEqual("門檻必須是 1～100 的整數", form.Error);
    }

    [TestMethod]
    [DataRow("85", "85")]
    [DataRow("90", "85")]
    public void OrangeMustBeBelowRed(string orange, string red)
    {
        var form = Form(orange, red, "90");

        Assert.IsNull(form.TryRead());
        Assert.AreEqual("橘色門檻必須小於紅色門檻", form.Error);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("0")]
    [DataRow("3651")]
    [DataRow("1.5")]
    [DataRow("九十")]
    public void RetentionMustBeWholeDaysFromOneTo3650(string retention)
    {
        var form = Form("70", "85", retention);

        Assert.IsNull(form.TryRead());
        Assert.AreEqual("保留天數必須是 1～3650 的整數", form.Error);
    }

    [TestMethod]
    public void AValidReadClearsAnEarlierError()
    {
        var form = Form("90", "85", "90");
        form.TryRead();

        form.OrangeText = "70";

        Assert.IsNotNull(form.TryRead());
        Assert.IsNull(form.Error);
    }

    [TestMethod]
    public void ChangesAreAnnouncedToTheWindow()
    {
        var form = new SettingsForm(AppSettings.Default);
        var changed = new List<string?>();
        form.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        form.OrangeText = "60";
        form.AutoStart = true;
        form.ShowError("data 資料夾無法寫入，設定沒有儲存");

        CollectionAssert.AreEqual(new[] { "OrangeText", "AutoStart", "Error" }, changed);
        Assert.AreEqual("data 資料夾無法寫入，設定沒有儲存", form.Error);
    }
}
```

- [ ] **Step 2：執行測試，確認失敗**

執行：`(cd app && dotnet test)`

預期：建置失敗，錯誤是找不到 `SettingsForm`（CS0246）。

- [ ] **Step 3：實作**

新增 `app/MemSweep.Core/SettingsForm.cs`：

```csharp
using System.Globalization;
using System.Text;

namespace MemSweep.Core;

/// <summary>
/// 設定視窗的輸入內容（F-50、Design 3.6）：輸入框綁定文字，按「儲存」時才檢查；
/// 注音輸入法打出的全形數字也接受。
/// </summary>
public sealed class SettingsForm : ObservableObject
{
    public const string ThresholdError = "門檻必須是 1～100 的整數";
    public const string OrderError = "橘色門檻必須小於紅色門檻";
    public const string RetentionError = "保留天數必須是 1～3650 的整數";

    private string _orangeText;
    private string _redText;
    private bool _autoStart;
    private string _retentionText;
    private string? _error;

    public SettingsForm(AppSettings current)
    {
        _orangeText = Text(current.OrangeThreshold);
        _redText = Text(current.RedThreshold);
        _autoStart = current.AutoStart;
        _retentionText = Text(current.RetentionDays);
    }

    public string OrangeText { get => _orangeText; set => Set(ref _orangeText, value); }

    public string RedText { get => _redText; set => Set(ref _redText, value); }

    public bool AutoStart { get => _autoStart; set => Set(ref _autoStart, value); }

    public string RetentionText { get => _retentionText; set => Set(ref _retentionText, value); }

    /// <summary>顯示在按鈕上方的錯誤訊息；沒有錯誤時為 null。</summary>
    public string? Error { get => _error; private set => Set(ref _error, value); }

    /// <summary>檢查輸入：合法時清除錯誤訊息並回傳設定；不合法時顯示第一個錯誤並回傳 null。</summary>
    public AppSettings? TryRead()
    {
        if (!TryParse(OrangeText, 1, 100, out int orange) || !TryParse(RedText, 1, 100, out int red))
        {
            Error = ThresholdError;
            return null;
        }
        if (orange >= red)
        {
            Error = OrderError;
            return null;
        }
        if (!TryParse(RetentionText, AppSettings.MinRetentionDays, AppSettings.MaxRetentionDays, out int days))
        {
            Error = RetentionError;
            return null;
        }
        Error = null;
        return new AppSettings(orange, red, AutoStart, days);
    }

    /// <summary>儲存失敗時顯示原因，例如 data 資料夾無法寫入。</summary>
    public void ShowError(string message) => Error = message;

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    // 全形數字以相容分解轉成半形；不接受正負號、小數點與千分位
    private static bool TryParse(string text, int min, int max, out int value) =>
        int.TryParse(text.Normalize(NormalizationForm.FormKC).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value)
        && value >= min
        && value <= max;
}
```

- [ ] **Step 4：執行測試，確認通過**

執行：`(cd app && dotnet test)`

預期：總計 266，失敗 0。

- [ ] **Step 5：Commit**

```bash
git add app/MemSweep.Core/SettingsForm.cs app/MemSweep.Core.Tests/SettingsFormTests.cs
git status --short
git commit -F - <<'EOF'
新增設定視窗的輸入檢查

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

- [ ] **Step 6：推送**（依「共同執行規則」先 fetch 並檢查）

---

### Task 3：開機自動啟動與設定的儲存

**檔案：**

- 新增：`app/MemSweep.Desktop/AutoStart.cs`、`app/MemSweep.Desktop/SettingsService.cs`
- 新增：`app/MemSweep.Core.Tests/FakeAutoStartRegistry.cs`、`app/MemSweep.Core.Tests/AutoStartTests.cs`、`app/MemSweep.Core.Tests/SettingsServiceTests.cs`

**實作的條文：**

> - F-51 開機自動啟動預設關閉。開啟時以目前使用者的身分登記，不需要系統管理員權限。〔Claude 決定〕
> - 開啟時在 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 寫入值 `MemSweep`，內容是加上引號的 `MemSweep.exe` 完整路徑；關閉時刪除該值。
> - 設定為開啟時，每次啟動都檢查路徑；exe 被移動過就更新。
> - 登錄操作包在 `IAutoStartRegistry` 介面後面。測試使用假的實作；開發期間不在使用者電腦上實際切換（AGENTS.md「System Safety」）。
> - 儲存設定時先寫入 `settings.json`，成功後才套用新設定；開機自動啟動的開關有改變時才寫入或刪除登錄值，只改其他設定時不動登錄（例如另一份 MemSweep 登記的值）。
> - 寫入 `settings.json` 失敗時設定維持不變，設定視窗顯示「無法寫入 settings.json，設定沒有儲存」。登錄操作失敗時新設定照樣生效，設定視窗顯示「設定已儲存，但無法變更開機自動啟動」。啟動時的路徑檢查失敗不顯示，下次啟動再試。

**介面：**

- 使用：Task 1 的 `AppSettings`、`SettingsStore`。
- 產生（都是 `internal`，測試經由 `InternalsVisibleTo` 使用）：
  - `IAutoStartRegistry`：`string? Read()`、`void Write(string command)`、`void Delete()`
  - `AutoStart(IAutoStartRegistry registry, string exePath)`；`string Command`；`void Apply(bool enabled)`
  - `RunKeyRegistry : IAutoStartRegistry`；`RunKeyRegistry.KeyPath`、`RunKeyRegistry.ValueName`
  - `SettingsService(SettingsStore store, AutoStart autoStart)`；`AppSettings Current`；`event Action<AppSettings>? Changed`；`void SyncAutoStart()`；`string? Save(AppSettings next)`；`SettingsService.SaveFailedMessage`、`AutoStartFailedMessage`
  - 測試用：`FakeAutoStartRegistry`（`Value`、`Writes`、`Deletes`、`Failure`）

- [ ] **Step 1：寫失敗的測試**

新增 `app/MemSweep.Core.Tests/FakeAutoStartRegistry.cs`：

```csharp
using MemSweep.Desktop;

namespace MemSweep.Core.Tests;

/// <summary>代替 HKCU 的 Run 機碼；測試不得實際寫入使用者的開機項目（Design 3.8）。</summary>
internal sealed class FakeAutoStartRegistry : IAutoStartRegistry
{
    public string? Value { get; set; }

    public int Writes { get; private set; }

    public int Deletes { get; private set; }

    public Exception? Failure { get; set; }

    public string? Read() => Value;

    public void Write(string command)
    {
        ThrowIfFailing();
        Writes++;
        Value = command;
    }

    public void Delete()
    {
        ThrowIfFailing();
        Deletes++;
        Value = null;
    }

    private void ThrowIfFailing()
    {
        if (Failure is not null)
        {
            throw Failure;
        }
    }
}
```

新增 `app/MemSweep.Core.Tests/AutoStartTests.cs`：

```csharp
using MemSweep.Desktop;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class AutoStartTests
{
    private const string ExePath = @"D:\Tools\MemSweep\MemSweep.exe";
    private const string Command = "\"" + ExePath + "\"";

    [TestMethod]
    public void TheRunKeyFollowsTheDesign()
    {
        Assert.AreEqual(@"Software\Microsoft\Windows\CurrentVersion\Run", RunKeyRegistry.KeyPath);
        Assert.AreEqual("MemSweep", RunKeyRegistry.ValueName);
    }

    [TestMethod]
    public void TheCommandIsTheQuotedExePath()
    {
        Assert.AreEqual(Command, new AutoStart(new FakeAutoStartRegistry(), ExePath).Command);
    }

    [TestMethod]
    public void EnablingRegistersTheExe()
    {
        var registry = new FakeAutoStartRegistry();

        new AutoStart(registry, ExePath).Apply(true);

        Assert.AreEqual(Command, registry.Value);
    }

    [TestMethod]
    [DataRow(Command)]
    [DataRow("\"d:\\tools\\memsweep\\MEMSWEEP.EXE\"")]
    public void AnUnchangedPathIsNotRewritten(string registered)
    {
        var registry = new FakeAutoStartRegistry { Value = registered };

        new AutoStart(registry, ExePath).Apply(true);

        Assert.AreEqual(0, registry.Writes);
        Assert.AreEqual(registered, registry.Value);
    }

    [TestMethod]
    public void AMovedExeIsUpdated()
    {
        var registry = new FakeAutoStartRegistry { Value = "\"C:\\Users\\me\\Downloads\\MemSweep\\MemSweep.exe\"" };

        new AutoStart(registry, ExePath).Apply(true);

        Assert.AreEqual(Command, registry.Value);
    }

    [TestMethod]
    public void DisablingRemovesTheValue()
    {
        var registry = new FakeAutoStartRegistry { Value = Command };

        new AutoStart(registry, ExePath).Apply(false);

        Assert.IsNull(registry.Value);
        Assert.AreEqual(1, registry.Deletes);
    }

    [TestMethod]
    public void DisablingWithoutAValueTouchesNothing()
    {
        var registry = new FakeAutoStartRegistry();

        new AutoStart(registry, ExePath).Apply(false);

        Assert.AreEqual(0, registry.Deletes);
        Assert.AreEqual(0, registry.Writes);
    }
}
```

新增 `app/MemSweep.Core.Tests/SettingsServiceTests.cs`：

```csharp
using MemSweep.Core;
using MemSweep.Desktop;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class SettingsServiceTests
{
    private const string ExePath = @"D:\Tools\MemSweep\MemSweep.exe";
    private const string Command = "\"" + ExePath + "\"";

    // 測試資料夾放在測試輸出資料夾底下，不碰 repo 以外的位置
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "SettingsServiceTests", Guid.NewGuid().ToString("N"));
    private readonly FakeAutoStartRegistry _registry = new();

    private string DataDirectory => Path.Combine(_root, "data");

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private SettingsService Service(AppSettings? saved = null)
    {
        if (saved is not null)
        {
            new SettingsStore(DataDirectory).Save(saved);
        }
        return new SettingsService(new SettingsStore(DataDirectory), new AutoStart(_registry, ExePath));
    }

    [TestMethod]
    public void StartsWithTheSavedSettings()
    {
        var service = Service(new AppSettings(60, 90, false, 30));

        Assert.AreEqual(new AppSettings(60, 90, false, 30), service.Current);
    }

    [TestMethod]
    public void SaveWritesTheFileAndAnnouncesTheChange()
    {
        var service = Service();
        var announced = new List<AppSettings>();
        service.Changed += announced.Add;
        var next = new AppSettings(60, 90, false, 30);

        string? error = service.Save(next);

        Assert.IsNull(error);
        Assert.AreEqual(next, service.Current);
        Assert.AreEqual(next, new SettingsStore(DataDirectory).Load());
        CollectionAssert.AreEqual(new[] { next }, announced);
    }

    [TestMethod]
    public void TurningAutoStartOnRegistersTheExe()
    {
        var service = Service();

        Assert.IsNull(service.Save(AppSettings.Default with { AutoStart = true }));

        Assert.AreEqual(Command, _registry.Value);
    }

    [TestMethod]
    public void TurningAutoStartOffRemovesIt()
    {
        _registry.Value = Command;
        var service = Service(AppSettings.Default with { AutoStart = true });

        Assert.IsNull(service.Save(AppSettings.Default));

        Assert.IsNull(_registry.Value);
    }

    [TestMethod]
    public void OtherChangesLeaveTheRegistryAlone()
    {
        // 例如另一份 MemSweep 登記的開機項目
        _registry.Value = "\"C:\\Other\\MemSweep.exe\"";
        var service = Service();

        Assert.IsNull(service.Save(AppSettings.Default with { OrangeThreshold = 60, RetentionDays = 30 }));

        Assert.AreEqual(0, _registry.Writes);
        Assert.AreEqual(0, _registry.Deletes);
        Assert.AreEqual("\"C:\\Other\\MemSweep.exe\"", _registry.Value);
    }

    [TestMethod]
    public void AFailedWriteKeepsTheCurrentSettingsAndTheRegistry()
    {
        var service = Service();
        bool announced = false;
        service.Changed += _ => announced = true;
        // data 的位置被檔案佔住，資料夾建立不了
        Directory.CreateDirectory(_root);
        File.WriteAllText(DataDirectory, "不是資料夾");

        string? error = service.Save(AppSettings.Default with { AutoStart = true, RetentionDays = 30 });

        Assert.AreEqual("無法寫入 settings.json，設定沒有儲存", error);
        Assert.AreEqual(AppSettings.Default, service.Current);
        Assert.IsFalse(announced);
        Assert.IsNull(_registry.Value);
    }

    [TestMethod]
    public void ARegistryFailureIsReportedAfterTheSettingsAreSaved()
    {
        var service = Service();
        _registry.Failure = new UnauthorizedAccessException();
        var next = AppSettings.Default with { AutoStart = true };

        string? error = service.Save(next);

        Assert.AreEqual("設定已儲存，但無法變更開機自動啟動", error);
        Assert.AreEqual(next, service.Current);
        Assert.AreEqual(next, new SettingsStore(DataDirectory).Load());
    }

    [TestMethod]
    public void StartupSyncUpdatesAMovedExe()
    {
        _registry.Value = "\"C:\\Users\\me\\Downloads\\MemSweep\\MemSweep.exe\"";
        var service = Service(AppSettings.Default with { AutoStart = true });

        service.SyncAutoStart();

        Assert.AreEqual(Command, _registry.Value);
    }

    [TestMethod]
    public void StartupSyncLeavesTheRegistryAloneWhenAutoStartIsOff()
    {
        _registry.Value = Command;
        var service = Service();

        service.SyncAutoStart();

        Assert.AreEqual(Command, _registry.Value);
        Assert.AreEqual(0, _registry.Deletes);
    }

    [TestMethod]
    public void AStartupSyncFailureIsRetriedAtTheNextStartup()
    {
        _registry.Failure = new System.Security.SecurityException();
        var service = Service(AppSettings.Default with { AutoStart = true });

        service.SyncAutoStart();

        Assert.IsNull(_registry.Value);
        Assert.IsTrue(service.Current.AutoStart);
    }
}
```

- [ ] **Step 2：執行測試，確認失敗**

執行：`(cd app && dotnet test)`

預期：建置失敗，錯誤是找不到 `IAutoStartRegistry` 與 `SettingsService`（CS0246）。

- [ ] **Step 3：實作**

新增 `app/MemSweep.Desktop/AutoStart.cs`：

```csharp
using Microsoft.Win32;

namespace MemSweep.Desktop;

/// <summary>開機自動啟動用到的登錄值；測試以假的實作取代，開發期間不在使用者電腦上實際切換（Design 3.8）。</summary>
internal interface IAutoStartRegistry
{
    /// <summary>目前登記的命令；沒有登記時為 null。</summary>
    string? Read();

    void Write(string command);

    void Delete();
}

/// <summary>開機自動啟動（F-51、Design 3.8）：以目前使用者的身分登記，不需要系統管理員權限。</summary>
internal sealed class AutoStart
{
    private readonly IAutoStartRegistry _registry;

    public AutoStart(IAutoStartRegistry registry, string exePath)
    {
        _registry = registry;
        Command = "\"" + exePath + "\"";
    }

    /// <summary>登記的內容：加上引號的 MemSweep.exe 完整路徑。</summary>
    public string Command { get; }

    /// <summary>開啟時寫入，內容相同就不寫（exe 被移動過才會不同）；關閉時刪除。失敗時丟出登錄的例外。</summary>
    public void Apply(bool enabled)
    {
        string? registered = _registry.Read();
        if (enabled)
        {
            if (!string.Equals(registered, Command, StringComparison.OrdinalIgnoreCase))
            {
                _registry.Write(Command);
            }
        }
        else if (registered is not null)
        {
            _registry.Delete();
        }
    }
}

/// <summary>HKCU\Software\Microsoft\Windows\CurrentVersion\Run 裡名為 MemSweep 的值（Design 3.8）。</summary>
internal sealed class RunKeyRegistry : IAutoStartRegistry
{
    public static readonly string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static readonly string ValueName = "MemSweep";

    public string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(ValueName) as string;
    }

    public void Write(string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue(ValueName, command, RegistryValueKind.String);
    }

    public void Delete()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
```

新增 `app/MemSweep.Desktop/SettingsService.cs`：

```csharp
using System.IO;
using System.Security;
using MemSweep.Core;

namespace MemSweep.Desktop;

/// <summary>
/// 目前的設定（F-50）：建立時讀取 settings.json；儲存時先寫入檔案，成功後才套用新設定與開機自動啟動
/// （F-51、Design 3.8）。必須在 UI 執行緒上使用。
/// </summary>
internal sealed class SettingsService
{
    public const string SaveFailedMessage = "無法寫入 settings.json，設定沒有儲存";
    public const string AutoStartFailedMessage = "設定已儲存，但無法變更開機自動啟動";

    private readonly SettingsStore _store;
    private readonly AutoStart _autoStart;

    public SettingsService(SettingsStore store, AutoStart autoStart)
    {
        _store = store;
        _autoStart = autoStart;
        Current = store.Load();
    }

    public AppSettings Current { get; private set; }

    /// <summary>儲存成功後觸發，讓系統匣、視窗與尖峰紀錄套用新設定。</summary>
    public event Action<AppSettings>? Changed;

    /// <summary>設定為開啟時，每次啟動都檢查登記的路徑，exe 被移動過就更新（Design 3.8）；失敗時下次啟動再試。</summary>
    public void SyncAutoStart()
    {
        if (Current.AutoStart)
        {
            TryApplyAutoStart(true);
        }
    }

    /// <summary>成功時回傳 null；失敗時回傳要顯示在設定視窗的訊息。</summary>
    public string? Save(AppSettings next)
    {
        var previous = Current;
        try
        {
            _store.Save(next);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return SaveFailedMessage;
        }

        Current = next;
        Changed?.Invoke(next);
        // 只在開關改變時才動登錄，其他設定的變更不會改寫使用者的開機項目
        if (next.AutoStart != previous.AutoStart && !TryApplyAutoStart(next.AutoStart))
        {
            return AutoStartFailedMessage;
        }
        return null;
    }

    private bool TryApplyAutoStart(bool enabled)
    {
        try
        {
            _autoStart.Apply(enabled);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }
}
```

- [ ] **Step 4：執行測試，確認通過**

執行：`(cd app && dotnet test)`

預期：總計 284，失敗 0。

- [ ] **Step 5：Commit**

```bash
git add app/MemSweep.Desktop/AutoStart.cs app/MemSweep.Desktop/SettingsService.cs app/MemSweep.Core.Tests/FakeAutoStartRegistry.cs app/MemSweep.Core.Tests/AutoStartTests.cs app/MemSweep.Core.Tests/SettingsServiceTests.cs
git status --short
git commit -F - <<'EOF'
新增開機自動啟動與設定的儲存

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

- [ ] **Step 6：推送**（依「共同執行規則」先 fetch 並檢查）

---

### Task 4：門檻與保留天數改讀設定

**檔案：**

- 修改（整份換成）：`app/MemSweep.Core/UsageThresholds.cs`、`app/MemSweep.Core/PeakStore.cs`、`app/MemSweep.Core/PeakRecorder.cs`、`app/MemSweep.Core/FullWindowModel.cs`
- 修改（整份換成）：`app/MemSweep.Desktop/TrayController.cs`、`app/MemSweep.Desktop/App.xaml.cs`
- 修改（整份換成）：`app/MemSweep.Core.Tests/PeakRecorderTests.cs`、`app/MemSweep.Core.Tests/FullWindowModelTests.cs`

**實作的條文：**

> - F-03 數字顏色依門檻變化：低於橘色門檻為綠色；達到橘色門檻但低於紅色門檻為橘色；達到紅色門檻以上為紅色。門檻可在設定調整，預設橘色 70%、紅色 85%。〔預設值：Claude 決定〕
> - F-33 每次開機一筆、每天一筆。保留天數可設定，預設 90 天，超過的紀錄自動刪除。〔預設值：Claude 決定〕
>   - 儲存成功後立即套用：系統匣圖示、小面板與完整視窗以最近一次取樣改用新的門檻；保留天數從下一次取樣開始生效（3.9）。
> - 保留天數清理：啟動時與跨日時執行，刪除尖峰時間（或日期）早於保留天數的列（F-33）。在設定視窗改了保留天數時，下一次取樣就依新的天數清理。
> - 設定為開啟時，每次啟動都檢查路徑；exe 被移動過就更新。

**介面：**

- 使用：Task 1 的 `AppSettings`、`SettingsStore`；Task 3 的 `SettingsService`、`AutoStart`、`RunKeyRegistry`。
- 產生：
  - `PeakRecorder.SetRetentionDays(int retentionDays)`
  - `FullWindowModel.Thresholds`（可設定，下一次 `Update` 套用）
  - `TrayController(SettingsService settings)`
  - 移除 `PeakStore.DefaultRetentionDays`（預設值改由 `AppSettings.Default` 提供）

- [ ] **Step 1：寫失敗的測試**

`app/MemSweep.Core.Tests/PeakRecorderTests.cs` 整份換成（新增 `ALowerRetentionIsAppliedAtTheNextSample`）：

```csharp
using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class PeakRecorderTests
{
    private const string Warning = "data 資料夾無法寫入，尖峰紀錄已停用";

    private sealed class FakeStore : IPeakStore
    {
        public List<PeakHistory> Saved { get; } = [];

        public int Attempts { get; private set; }

        public Exception? Failure { get; set; }

        public void Save(PeakHistory history)
        {
            Attempts++;
            if (Failure is not null)
            {
                throw Failure;
            }
            Saved.Add(history);
        }
    }

    private static long Gb(double value) => MemoryMath.FromGigabytes(value);

    private static DateTimeOffset At(int day, int hour, int minute, int second = 0) =>
        new(2026, 10, day, hour, minute, second, TimeSpan.FromHours(8));

    private static GroupedSnapshot Sample(double usedGb, DateTimeOffset timestamp)
    {
        var typical = ReportSamples.Typical();
        var snapshot = typical.Snapshot with
        {
            CommitUsedBytes = Gb(usedGb),
            CommitPeakBytes = Gb(Math.Max(usedGb, 36.8)),
            Timestamp = timestamp,
        };
        return typical with { Snapshot = snapshot };
    }

    private static PeakRecorder Recorder(FakeStore store, PeakHistory? saved = null, int retentionDays = 90) =>
        new(new PeakStoreOpening(saved ?? PeakHistory.Empty, store, null), retentionDays);

    private static DailyPeak Day(int month, int day, double usedGb) =>
        new(new DateOnly(2026, month, day), Gb(usedGb), Gb(39.4), new DateTimeOffset(2026, month, day, 12, 0, 0, TimeSpan.FromHours(8)), [new PeakApp("Steam", Gb(1.0))]);

    [TestMethod]
    public void StateBeforeTheFirstSample()
    {
        var saved = new PeakHistory([], [Day(10, 25, 30.0)]);
        var recorder = new PeakRecorder(new PeakStoreOpening(saved, null, Warning), 90);

        Assert.IsNull(recorder.State.BootPeak);
        Assert.AreSame(saved, recorder.State.History);
        Assert.AreEqual(Warning, recorder.State.StorageWarning);
    }

    [TestMethod]
    public void FirstSampleIsWrittenImmediately()
    {
        var store = new FakeStore();
        var recorder = Recorder(store);

        recorder.Record(Sample(31.4, At(26, 14, 0)));

        Assert.HasCount(1, store.Saved);
        Assert.AreSame(recorder.State.History, store.Saved[0]);
        Assert.IsNotNull(recorder.State.BootPeak);
        Assert.HasCount(1, store.Saved[0].Days);
    }

    [TestMethod]
    public void ChangesAreWrittenAtMostEverySixtySeconds()
    {
        var store = new FakeStore();
        var recorder = Recorder(store);
        recorder.Record(Sample(31.4, At(26, 14, 0, 0)));

        recorder.Record(Sample(32.0, At(26, 14, 0, 2)));
        recorder.Record(Sample(32.5, At(26, 14, 0, 58)));

        Assert.HasCount(1, store.Saved);

        recorder.Record(Sample(31.0, At(26, 14, 1, 0)));

        Assert.HasCount(2, store.Saved);
        Assert.AreEqual(Gb(32.5), store.Saved[1].Days[0].CommitBytes);
        Assert.AreEqual(TimeSpan.FromSeconds(60), PeakRecorder.WriteInterval);
    }

    [TestMethod]
    public void NothingIsWrittenWithoutAChange()
    {
        var store = new FakeStore();
        var recorder = Recorder(store);
        recorder.Record(Sample(31.4, At(26, 14, 0)));

        recorder.Record(Sample(30.0, At(26, 14, 5)));

        Assert.AreEqual(1, store.Attempts);
    }

    [TestMethod]
    public void FlushWritesPendingChangesOnce()
    {
        var store = new FakeStore();
        var recorder = Recorder(store);
        recorder.Record(Sample(31.4, At(26, 14, 0, 0)));
        recorder.Record(Sample(33.0, At(26, 14, 0, 2)));

        recorder.Flush();
        recorder.Flush();

        Assert.HasCount(2, store.Saved);
        Assert.AreEqual(Gb(33.0), store.Saved[1].Days[0].CommitBytes);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void AFailedWriteIsRetriedAtTheNextInterval(bool accessDenied)
    {
        var store = new FakeStore
        {
            Failure = accessDenied ? new UnauthorizedAccessException() : new IOException("檔案被其他程式使用"),
        };
        var recorder = Recorder(store);
        recorder.Record(Sample(31.4, At(26, 14, 0, 0)));
        store.Failure = null;

        recorder.Record(Sample(30.0, At(26, 14, 0, 2)));
        Assert.AreEqual(1, store.Attempts);

        recorder.Record(Sample(30.0, At(26, 14, 1, 0)));
        Assert.HasCount(1, store.Saved);
        Assert.AreEqual(Gb(31.4), store.Saved[0].Days[0].CommitBytes);
    }

    [TestMethod]
    public void OtherWriteErrorsAreNotHidden()
    {
        var store = new FakeStore { Failure = new NullReferenceException() };

        Assert.ThrowsExactly<NullReferenceException>(() => Recorder(store).Record(Sample(31.4, At(26, 14, 0))));
    }

    [TestMethod]
    public void ClockMovingBackDoesNotDelayWrites()
    {
        var store = new FakeStore();
        var recorder = Recorder(store);
        recorder.Record(Sample(31.4, At(26, 14, 0)));

        recorder.Record(Sample(33.0, At(26, 13, 30)));

        Assert.HasCount(2, store.Saved);
    }

    [TestMethod]
    public void ADisabledStoreStillTracksPeaks()
    {
        var recorder = new PeakRecorder(new PeakStoreOpening(PeakHistory.Empty, null, Warning), 90);

        recorder.Record(Sample(31.4, At(26, 14, 0)));
        recorder.Flush();

        Assert.IsNotNull(recorder.State.BootPeak);
        Assert.HasCount(1, recorder.State.History.Days);
        Assert.AreEqual(Warning, recorder.State.StorageWarning);
    }

    [TestMethod]
    public void RestartWithUnchangedPeaksShowsTheSavedBootPeakWithoutWriting()
    {
        var typical = ReportSamples.Typical().Snapshot;
        var savedBoot = new BootPeak(typical.BootTimeUtc, Gb(36.8), Gb(39.4), At(26, 9, 0), [new PeakApp("Steam", Gb(1.0))]);
        var store = new FakeStore();
        var recorder = Recorder(store, new PeakHistory([savedBoot], [Day(10, 26, 35.0)]));

        recorder.Record(Sample(31.4, At(26, 14, 0)));

        Assert.AreSame(savedBoot, recorder.State.BootPeak);
        Assert.AreEqual(0, store.Attempts);
    }

    [TestMethod]
    public void OldRecordsArePrunedOnTheFirstSampleAndAtMidnight()
    {
        var store = new FakeStore();
        var saved = new PeakHistory([], [Day(10, 24, 30.0), Day(10, 25, 30.0)]);
        var recorder = Recorder(store, saved, retentionDays: 2);

        recorder.Record(Sample(31.4, At(26, 23, 59, 58)));

        CollectionAssert.AreEqual(
            new[] { new DateOnly(2026, 10, 25), new DateOnly(2026, 10, 26) },
            recorder.State.History.Days.Select(d => d.Date).ToArray());

        recorder.Record(Sample(30.0, At(27, 0, 0, 0)));

        CollectionAssert.AreEqual(
            new[] { new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 27) },
            recorder.State.History.Days.Select(d => d.Date).ToArray());
    }

    [TestMethod]
    public void ALowerRetentionIsAppliedAtTheNextSample()
    {
        var store = new FakeStore();
        var saved = new PeakHistory([], [Day(10, 20, 30.0), Day(10, 24, 30.0), Day(10, 25, 30.0)]);
        var recorder = Recorder(store, saved, retentionDays: 90);
        recorder.Record(Sample(31.4, At(26, 14, 0, 0)));
        Assert.HasCount(4, recorder.State.History.Days);

        recorder.SetRetentionDays(2);
        recorder.Record(Sample(30.0, At(26, 14, 0, 2)));

        CollectionAssert.AreEqual(
            new[] { new DateOnly(2026, 10, 25), new DateOnly(2026, 10, 26) },
            recorder.State.History.Days.Select(d => d.Date).ToArray());
    }
}
```

`app/MemSweep.Core.Tests/FullWindowModelTests.cs` 整份換成（新增 `NewThresholdsApplyAtTheNextUpdate`）：

```csharp
using System.Collections.Specialized;
using System.ComponentModel;
using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class FullWindowModelTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time");
    private static readonly DateTime Boot = new(2026, 10, 26, 0, 30, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset PeakTime = new(2026, 10, 26, 14, 35, 2, TimeSpan.FromHours(8));
    private static readonly PeakState NoPeaks = new(null, PeakHistory.Empty, null);

    private static long Gb(double value) => MemoryMath.FromGigabytes(value);

    private static FullWindowModel Model() => new(UsageThresholds.Default, Zone);

    private static AppGroup App(string name, double commitGb, params AppComponent[] components) =>
        new(name, false, Math.Max(1, components.Sum(c => c.ProcessCount)), Gb(commitGb), Gb(commitGb / 2), components);

    private static GroupedSnapshot WithApps(params AppGroup[] apps) => ReportSamples.Typical() with { Apps = apps };

    [TestMethod]
    public void OverviewIsThePanelPlusThePageFileSize()
    {
        var model = Model();

        model.Update(ReportSamples.Typical(), NoPeaks with { StorageWarning = "data 資料夾無法寫入，尖峰紀錄已停用" });

        Assert.AreEqual("31.4 / 39.4 GB（80%）", model.Overview!.Commit.ValueText);
        Assert.HasCount(6, model.Overview.Rows);
        Assert.AreEqual("data 資料夾無法寫入，尖峰紀錄已停用", model.Overview.Warning);
        Assert.AreEqual("分頁檔大小：8.0 GB", model.PageFileText);
    }

    [TestMethod]
    public void NewThresholdsApplyAtTheNextUpdate()
    {
        var model = Model();
        model.Update(ReportSamples.Typical(), NoPeaks);
        Assert.AreEqual(UsageLevel.Orange, model.Overview!.Commit.Level);

        model.Thresholds = new UsageThresholds(60, 75);
        model.Update(ReportSamples.Typical(), NoPeaks);

        Assert.AreEqual(UsageLevel.Red, model.Overview!.Commit.Level);
    }

    [TestMethod]
    public void AppsListsEveryAppByCommitThenUnattributed()
    {
        var model = Model();

        model.Update(ReportSamples.Typical(), NoPeaks);

        Assert.HasCount(7, model.Apps);
        var claude = model.Apps[0];
        Assert.AreEqual("Claude", claude.Name);
        Assert.AreEqual("36", claude.CountText);
        Assert.AreEqual("3.2 GB", claude.CommitText);
        Assert.AreEqual("1.6 GB", claude.WorkingSetText);
        Assert.IsFalse(claude.IsUnattributed);
        Assert.AreEqual("Microsoft Edge", model.Apps[5].Name);
        var last = model.Apps[6];
        Assert.IsTrue(last.IsUnattributed);
        Assert.AreEqual(GroupedSnapshot.UnattributedName, last.Name);
        Assert.AreEqual("6.9 GB", last.CommitText);
        Assert.AreEqual(string.Empty, last.CountText);
        Assert.AreEqual(string.Empty, last.WorkingSetText);
        Assert.IsEmpty(last.Components);
    }

    [TestMethod]
    public void ComponentsShowTheProcessCountAfterTheName()
    {
        var model = Model();
        var claude = App(
            "Claude",
            3.2,
            new AppComponent("claude.exe", 14, Gb(2.0), Gb(1.0)),
            new AppComponent("node：@playwright/mcp", 2, Gb(0.4), Gb(0.3)));

        model.Update(WithApps(claude), NoPeaks);

        CollectionAssert.AreEqual(
            new[]
            {
                new ComponentRow("claude.exe ×14", "2.0 GB", "1.0 GB"),
                new ComponentRow("node：@playwright/mcp ×2", "0.4 GB", "0.3 GB"),
            },
            model.Apps[0].Components.ToArray());
        Assert.AreEqual("16", model.Apps[0].CountText);
    }

    [TestMethod]
    public void RowsAreUpdatedInPlaceAndKeepTheirExpansion()
    {
        var model = Model();
        model.Update(WithApps(App("Claude", 3.2), App("ChatGPT", 2.2)), NoPeaks);
        var claude = model.Apps[0];
        claude.IsExpanded = true;
        var changed = new List<string?>();
        claude.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        model.Update(WithApps(App("Claude", 3.5), App("ChatGPT", 2.2)), NoPeaks);

        Assert.AreSame(claude, model.Apps[0]);
        Assert.IsTrue(claude.IsExpanded);
        Assert.AreEqual("3.5 GB", claude.CommitText);
        CollectionAssert.AreEquivalent(new[] { "CommitText", "WorkingSetText" }, changed);
    }

    [TestMethod]
    public void ReorderedAppsAreMovedNotRecreated()
    {
        var model = Model();
        model.Update(WithApps(App("Claude", 3.2), App("ChatGPT", 2.2), App("Steam", 1.0)), NoPeaks);
        var chatGpt = model.Apps[1];
        var actions = new List<NotifyCollectionChangedAction>();
        model.Apps.CollectionChanged += (_, e) => actions.Add(e.Action);

        model.Update(WithApps(App("ChatGPT", 4.0), App("Claude", 3.2), App("Steam", 1.0)), NoPeaks);

        Assert.AreSame(chatGpt, model.Apps[0]);
        CollectionAssert.AreEqual(new[] { "ChatGPT", "Claude", "Steam", GroupedSnapshot.UnattributedName }, model.Apps.Select(a => a.Name).ToArray());
        CollectionAssert.AreEqual(new[] { NotifyCollectionChangedAction.Move }, actions);
    }

    [TestMethod]
    public void AppsThatAppearOrDisappearAreAddedOrRemoved()
    {
        var model = Model();
        model.Update(WithApps(App("Claude", 3.2), App("Steam", 1.0)), NoPeaks);
        var claude = model.Apps[0];

        model.Update(WithApps(App("Claude", 3.2), App("Game", 2.0)), NoPeaks);

        Assert.AreSame(claude, model.Apps[0]);
        CollectionAssert.AreEqual(new[] { "Claude", "Game", GroupedSnapshot.UnattributedName }, model.Apps.Select(a => a.Name).ToArray());
    }

    [TestMethod]
    public void AppsWithTheSameNameAreKeptApart()
    {
        // 不同路徑的最上層程式可能有相同的檔案描述（R-5 只合併相同路徑）
        var model = Model();
        model.Update(WithApps(App("Node.js JavaScript Runtime", 1.4), App("Node.js JavaScript Runtime", 0.5)), NoPeaks);
        var second = model.Apps[1];
        second.IsExpanded = true;

        model.Update(WithApps(App("Node.js JavaScript Runtime", 1.4), App("Node.js JavaScript Runtime", 0.6)), NoPeaks);

        Assert.HasCount(3, model.Apps);
        Assert.AreSame(second, model.Apps[1]);
        Assert.AreEqual("0.6 GB", model.Apps[1].CommitText);
        Assert.IsFalse(model.Apps[0].IsExpanded);
    }

    [TestMethod]
    public void UnchangedComponentsKeepTheSameList()
    {
        var model = Model();
        var component = new AppComponent("claude.exe", 14, Gb(2.0), Gb(1.0));
        model.Update(WithApps(App("Claude", 3.2, component)), NoPeaks);
        var before = model.Apps[0].Components;

        model.Update(WithApps(App("Claude", 3.2, component)), NoPeaks);

        Assert.AreSame(before, model.Apps[0].Components);
    }

    [TestMethod]
    public void PeakRowsAreNewestFirst()
    {
        var history = new PeakHistory(
            [
                new BootPeak(Boot.AddDays(-1), Gb(39.0), Gb(39.4), null, null),
                new BootPeak(Boot, Gb(36.8), Gb(39.4), PeakTime, [new PeakApp("Claude", Gb(3.2)), new PeakApp("ChatGPT", Gb(2.2))]),
            ],
            [
                new DailyPeak(new DateOnly(2026, 10, 25), Gb(30.5), Gb(39.4), PeakTime.AddDays(-1), [new PeakApp("Steam", Gb(1.0))]),
                new DailyPeak(new DateOnly(2026, 10, 26), Gb(31.4), Gb(39.4), PeakTime, [new PeakApp("Claude", Gb(3.2))]),
            ]);
        var model = Model();

        model.Update(ReportSamples.Typical(), new PeakState(history.Boots[1], history, null));

        CollectionAssert.AreEqual(
            new[]
            {
                new PeakRow("2026-10-26 08:30", "2026-10-26 14:35", "36.8 GB", "39.4 GB", "Claude 3.2 GB、ChatGPT 2.2 GB"),
                new PeakRow("2026-10-25 08:30", "未知", "39.0 GB", "39.4 GB", "未知"),
            },
            model.BootPeaks.ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                new PeakRow("2026-10-26", "14:35", "31.4 GB", "39.4 GB", "Claude 3.2 GB"),
                new PeakRow("2026-10-25", "14:35", "30.5 GB", "39.4 GB", "Steam 1.0 GB"),
            },
            model.DailyPeaks.ToArray());
    }

    [TestMethod]
    public void PeakRowsAreRebuiltOnlyWhenTheHistoryChanges()
    {
        var history = new PeakHistory([], [new DailyPeak(new DateOnly(2026, 10, 26), Gb(31.4), Gb(39.4), PeakTime, [])]);
        var model = Model();
        model.Update(ReportSamples.Typical(), new PeakState(null, history, null));
        var before = model.DailyPeaks;
        var changed = new List<string?>();
        ((INotifyPropertyChanged)model).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        model.Update(ReportSamples.Typical(), new PeakState(null, history, null));

        Assert.AreSame(before, model.DailyPeaks);
        CollectionAssert.DoesNotContain(changed, nameof(FullWindowModel.DailyPeaks));
        CollectionAssert.DoesNotContain(changed, nameof(FullWindowModel.BootPeaks));
    }
}
```

- [ ] **Step 2：執行測試，確認失敗**

執行：`(cd app && dotnet test)`

預期：建置失敗，錯誤是 `PeakRecorder` 沒有 `SetRetentionDays`、`FullWindowModel` 沒有 `Thresholds`（CS1061）。

- [ ] **Step 3：實作**

`app/MemSweep.Core/UsageThresholds.cs` 整份換成（只改說明）：

```csharp
namespace MemSweep.Core;

/// <summary>commit 使用率的顏色等級（F-03）。</summary>
public enum UsageLevel
{
    Green,
    Orange,
    Red,
}

/// <summary>橘色與紅色門檻（百分比），預設 70、85；使用者可在設定視窗調整（F-03、F-50）。</summary>
public sealed record UsageThresholds(int OrangePercent, int RedPercent)
{
    public static UsageThresholds Default { get; } = new(70, 85);

    /// <summary>低於橘色門檻為綠色；達到橘色門檻但低於紅色門檻為橘色；達到紅色門檻以上為紅色。</summary>
    public UsageLevel LevelOf(int percent) =>
        percent >= RedPercent ? UsageLevel.Red
        : percent >= OrangePercent ? UsageLevel.Orange
        : UsageLevel.Green;
}
```

`app/MemSweep.Core/PeakStore.cs` 整份換成（移除 `DefaultRetentionDays`）：

```csharp
namespace MemSweep.Core;

/// <summary>寫入尖峰紀錄的地方；測試以假的實作取代。</summary>
public interface IPeakStore
{
    /// <summary>寫入全部紀錄；失敗時丟出 <see cref="IOException"/> 或 <see cref="UnauthorizedAccessException"/>。</summary>
    void Save(PeakHistory history);
}

/// <summary>開啟 data\ 的結果：Store 為 null 表示尖峰紀錄已停用，Warning 是顯示給使用者的原因。</summary>
public sealed record PeakStoreOpening(PeakHistory History, IPeakStore? Store, string? Warning);

/// <summary>data\ 資料夾裡的 peaks-boot.csv 與 peaks-daily.csv（F-34、F-52、Design 3.9）。</summary>
public sealed class PeakStore : IPeakStore
{
    public const string UnwritableWarning = "data 資料夾無法寫入，尖峰紀錄已停用";

    private readonly string _directory;
    private readonly TimeZoneInfo _zone;

    private PeakStore(string directory, TimeZoneInfo zone)
    {
        _directory = directory;
        _zone = zone;
    }

    /// <summary>
    /// 建立資料夾（不存在時）並載入紀錄。資料夾無法寫入，或既有的紀錄檔無法讀取時停用尖峰紀錄，
    /// 也不會覆寫那些檔案；其他功能照常（Design 3.9）。
    /// </summary>
    public static PeakStoreOpening Open(string directory, TimeZoneInfo zone)
    {
        try
        {
            Directory.CreateDirectory(directory);
            // 資料夾已存在但沒有寫入權限時 CreateDirectory 不會失敗，所以實際建立一個暫存檔試試
            using var probe = new FileStream(
                Path.Combine(directory, PeakCsv.BootFileName + ".tmp"),
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                1,
                FileOptions.DeleteOnClose);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new PeakStoreOpening(PeakHistory.Empty, null, UnwritableWarning);
        }

        var store = new PeakStore(directory, zone);
        string fileName = PeakCsv.BootFileName;
        try
        {
            var boots = store.Read(fileName, PeakCsv.ReadBoots);
            fileName = PeakCsv.DailyFileName;
            var days = store.Read(fileName, PeakCsv.ReadDays);
            return new PeakStoreOpening(new PeakHistory(boots, days), store, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            return new PeakStoreOpening(PeakHistory.Empty, null, fileName + " 無法讀取，尖峰紀錄已停用");
        }
    }

    public void Save(PeakHistory history)
    {
        // 執行中 data\ 被刪除時由 AtomicFile 重新建立，之後的寫入就恢復
        AtomicFile.Write(Path.Combine(_directory, PeakCsv.BootFileName), PeakCsv.WriteBoots(history.Boots, _zone));
        AtomicFile.Write(Path.Combine(_directory, PeakCsv.DailyFileName), PeakCsv.WriteDays(history.Days, _zone));
    }

    private List<T> Read<T>(string fileName, Func<byte[], TimeZoneInfo, List<T>> parse)
    {
        string path = Path.Combine(_directory, fileName);
        if (!File.Exists(path))
        {
            return [];
        }

        // 其他程式（例如 Excel）開著檔案時也要讀得到
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var content = new MemoryStream();
        stream.CopyTo(content);
        return parse(content.ToArray(), _zone);
    }
}
```

`app/MemSweep.Core/PeakRecorder.cs` 整份換成：

```csharp
namespace MemSweep.Core;

/// <summary>交給介面顯示的尖峰狀態；內容不會再被修改，可以交給其他執行緒。</summary>
public sealed record PeakState(BootPeak? BootPeak, PeakHistory History, string? StorageWarning);

/// <summary>
/// 以每次取樣更新尖峰紀錄並寫入 CSV（Design 3.9）：尖峰有變動就標記，最多每 60 秒寫一次，
/// 正常結束時再寫一次；保留天數清理在啟動後第一次取樣、跨日與保留天數改變後的下一次取樣時執行（F-33）。
/// 取樣執行緒與結束時的 UI 執行緒都會呼叫，以 lock 保護。
/// </summary>
public sealed class PeakRecorder
{
    public static readonly TimeSpan WriteInterval = TimeSpan.FromSeconds(60);

    private readonly Lock _gate = new();
    private readonly PeakTracker _tracker;
    private readonly IPeakStore? _store;
    private readonly string? _warning;
    private int _retentionDays;
    private DateOnly? _prunedOn;
    private DateTimeOffset? _lastWrite;
    private bool _dirty;

    public PeakRecorder(PeakStoreOpening opening, int retentionDays)
    {
        _tracker = new PeakTracker(opening.History);
        _store = opening.Store;
        _warning = opening.Warning;
        _retentionDays = retentionDays;
        State = new PeakState(null, opening.History, opening.Warning);
    }

    public PeakState State { get; private set; }

    public void Record(GroupedSnapshot grouped)
    {
        lock (_gate)
        {
            var now = grouped.Snapshot.Timestamp;
            bool changed = _tracker.Update(grouped);
            var today = DateOnly.FromDateTime(now.DateTime);
            if (_prunedOn != today)
            {
                changed |= _tracker.Prune(now, _retentionDays);
                _prunedOn = today;
            }

            // 第一次取樣即使沒有變動（同一次開機內重新啟動），也要讓介面拿到開機尖峰
            if (changed || State.BootPeak is null)
            {
                State = new PeakState(_tracker.BootPeak, _tracker.History, _warning);
            }
            _dirty |= changed;

            // 時鐘往回調時也照常寫入，所以比較時間差的絕對值
            if (_dirty && (_lastWrite is not { } last || (now - last).Duration() >= WriteInterval))
            {
                _lastWrite = now;
                Write();
            }
        }
    }

    /// <summary>保留天數改變時由 UI 執行緒呼叫：下一次取樣就依新的天數清理（Design 3.9）。</summary>
    public void SetRetentionDays(int retentionDays)
    {
        lock (_gate)
        {
            _retentionDays = retentionDays;
            _prunedOn = null;
        }
    }

    /// <summary>正常結束時呼叫：還沒寫入的變動立即寫入。</summary>
    public void Flush()
    {
        lock (_gate)
        {
            if (_dirty)
            {
                Write();
            }
        }
    }

    private void Write()
    {
        if (_store is null)
        {
            return;
        }
        try
        {
            _store.Save(_tracker.History);
            _dirty = false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // 例如使用者用 Excel 開著紀錄檔：保留變動，下一個寫入時機再試
        }
    }
}
```

`app/MemSweep.Core/FullWindowModel.cs` 整份換成：

```csharp
using System.Collections.ObjectModel;
using System.Globalization;

namespace MemSweep.Core;

/// <summary>「尖峰紀錄」分頁的一列（F-24）；開機尖峰的 First 是開機時間，每日尖峰的是日期。</summary>
public sealed record PeakRow(string FirstText, string TimeText, string CommitText, string LimitText, string TopAppsText);

/// <summary>完整視窗的顯示資料（F-20～F-24）：總覽、程式、尖峰紀錄三個分頁。</summary>
public sealed class FullWindowModel : ObservableObject
{
    private const string UnattributedKey = "#unattributed";

    private readonly TimeZoneInfo _zone;
    private PanelModel? _overview;
    private string _pageFileText = string.Empty;
    private PeakHistory? _history;
    private IReadOnlyList<PeakRow> _bootPeaks = [];
    private IReadOnlyList<PeakRow> _dailyPeaks = [];

    public FullWindowModel(UsageThresholds thresholds, TimeZoneInfo zone)
    {
        Thresholds = thresholds;
        _zone = zone;
    }

    /// <summary>commit 量表的顏色門檻；設定變更後從下一次 Update 開始套用。</summary>
    public UsageThresholds Thresholds { get; set; }

    /// <summary>總覽：小面板的全部資訊（F-22）。</summary>
    public PanelModel? Overview { get => _overview; private set => Set(ref _overview, value); }

    /// <summary>總覽另外顯示的分頁檔大小（F-22）。</summary>
    public string PageFileText { get => _pageFileText; private set => Set(ref _pageFileText, value); }

    /// <summary>所有應用程式依 commit 由大到小，最後一列是「系統（不屬於任何程式）」（F-23）。</summary>
    public ObservableCollection<AppListRow> Apps { get; } = [];

    /// <summary>開機尖峰，新的在前。</summary>
    public IReadOnlyList<PeakRow> BootPeaks { get => _bootPeaks; private set => Set(ref _bootPeaks, value); }

    /// <summary>每日尖峰，新的在前。</summary>
    public IReadOnlyList<PeakRow> DailyPeaks { get => _dailyPeaks; private set => Set(ref _dailyPeaks, value); }

    public void Update(GroupedSnapshot grouped, PeakState peaks)
    {
        Overview = PanelModel.Build(grouped, peaks.BootPeak, Thresholds, peaks.StorageWarning);
        PageFileText = "分頁檔大小：" + Gigabytes(grouped.Snapshot.PageFileTotalBytes);
        UpdateApps(grouped);

        // 尖峰紀錄只在有變動時（換成新的 PeakHistory）才重建
        if (!ReferenceEquals(peaks.History, _history))
        {
            _history = peaks.History;
            BootPeaks = peaks.History.Boots.OrderByDescending(b => b.BootTimeUtc).Select(BootRow).ToList();
            DailyPeaks = peaks.History.Days.OrderByDescending(d => d.Date).Select(DayRow).ToList();
        }
    }

    internal static string Gigabytes(long bytes) => MemoryMath.FormatGigabytes(bytes) + " GB";

    private void UpdateApps(GroupedSnapshot grouped)
    {
        int index = 0;
        foreach (var app in grouped.Apps)
        {
            Place(index++, app.Name, false).Update(app);
        }
        Place(index++, UnattributedKey, true).UpdateUnattributed(grouped.UnattributedBytes);

        // 這次沒有出現的列都被擠到最後面
        while (Apps.Count > index)
        {
            Apps.RemoveAt(Apps.Count - 1);
        }
    }

    // 把鍵相同的列移到 index；沒有就新增一列。從 index 往後找，所以同名的列依出現順序對應
    private AppListRow Place(int index, string key, bool isUnattributed)
    {
        for (int i = index; i < Apps.Count; i++)
        {
            if (Apps[i].Key == key)
            {
                if (i != index)
                {
                    Apps.Move(i, index);
                }
                return Apps[index];
            }
        }
        var row = new AppListRow(key, isUnattributed);
        Apps.Insert(index, row);
        return row;
    }

    private PeakRow BootRow(BootPeak boot) =>
        new(
            TimeZoneInfo.ConvertTimeFromUtc(boot.BootTimeUtc, _zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            boot.Time is { } time ? Local(time).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : PeakCsv.Unknown,
            Gigabytes(boot.CommitBytes),
            Gigabytes(boot.CommitLimitBytes),
            boot.TopApps is { } apps ? TopAppsText(apps) : PeakCsv.Unknown);

    private PeakRow DayRow(DailyPeak day) =>
        new(
            day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Local(day.Time).ToString("HH:mm", CultureInfo.InvariantCulture),
            Gigabytes(day.CommitBytes),
            Gigabytes(day.CommitLimitBytes),
            TopAppsText(day.TopApps));

    private DateTimeOffset Local(DateTimeOffset time) => TimeZoneInfo.ConvertTime(time, _zone);

    private static string TopAppsText(IReadOnlyList<PeakApp> apps) =>
        string.Join("、", apps.Select(a => a.Name + " " + Gigabytes(a.CommitBytes)));
}
```

`app/MemSweep.Desktop/TrayController.cs` 整份換成：

```csharp
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Threading;
using MemSweep.Core;

namespace MemSweep.Desktop;

/// <summary>系統匣圖示、提示文字、右鍵選單、小面板與完整視窗（F-01～F-24）。必須在 UI 執行緒上使用。</summary>
internal sealed class TrayController : IDisposable
{
    private readonly SettingsService _settings;
    private readonly NotifyIcon _notifyIcon;
    private FlyoutWindow? _flyout;
    private FullWindow? _fullWindow;
    private FullWindowModel? _fullModel;
    private bool _flyoutRequested;
    private Icon? _icon;
    private string? _iconKey;
    private GroupedSnapshot? _latest;
    private PeakState? _latestPeaks;

    public TrayController(SettingsService settings)
    {
        _settings = settings;
        _settings.Changed += OnSettingsChanged;
        // 「設定…」在 Phase 04 加入
        var menu = new ContextMenuStrip();
        menu.Items.Add("開啟小面板", null, (_, _) => ShowFlyout());
        menu.Items.Add("完整清單…", null, (_, _) => ShowFullWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("結束 MemSweep", null, (_, _) => ExitRequested?.Invoke());
        _notifyIcon = new NotifyIcon { Text = "MemSweep", ContextMenuStrip = menu };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ToggleFlyout();
            }
        };
    }

    public event Action? ExitRequested;

    public void ShowFlyout()
    {
        // 還沒有第一次取樣的結果時沒有內容可顯示，先記下來，取樣完成後再顯示
        if (_latest is null)
        {
            _flyoutRequested = true;
            return;
        }
        var flyout = EnsureFlyout();
        flyout.PanelView.DataContext = BuildPanel();
        flyout.ShowPanel();
    }

    public void Update(GroupedSnapshot grouped, PeakState peaks)
    {
        var snapshot = grouped.Snapshot;
        string text = TrayText.IconText(snapshot);
        var level = _settings.Current.Thresholds.LevelOf(TrayText.CommitPercent(snapshot));
        int size = SystemInformation.SmallIconSize.Width;

        // 數字、顏色與尺寸都沒變就不重畫
        string key = $"{text}|{level}|{size}";
        if (key != _iconKey)
        {
            var previous = _icon;
            _icon = TrayIconRenderer.Render(text, level, size);
            _iconKey = key;
            _notifyIcon.Icon = _icon;
            previous?.Dispose();
        }

        _notifyIcon.Text = TrayText.Tooltip(snapshot);
        _notifyIcon.Visible = true;
        _latest = grouped;
        _latestPeaks = peaks;
        if (_flyoutRequested)
        {
            _flyoutRequested = false;
            ShowFlyout();
        }
        else if (_flyout is { IsVisible: true })
        {
            _flyout.PanelView.DataContext = BuildPanel();
        }
        if (_fullWindow is { IsVisible: true })
        {
            _fullModel!.Update(grouped, peaks);
        }
    }

    public void ShowFullWindow()
    {
        if (_latest is null)
        {
            return;
        }
        _flyout?.HidePanel();
        var window = EnsureFullWindow();
        _fullModel!.Update(_latest, _latestPeaks!);
        window.ShowWindow();
    }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        if (_fullWindow is { } window)
        {
            window.Closed -= OnFullWindowClosed;
            window.Close();
        }
        _flyout?.Close();
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _icon?.Dispose();
    }

    private PanelModel BuildPanel() =>
        PanelModel.Build(_latest!, _latestPeaks?.BootPeak, _settings.Current.Thresholds, _latestPeaks?.StorageWarning);

    // 第一次開啟時才建立，之後只顯示／隱藏：只用系統匣時，行程不必載入 WPF 視窗（約省 40 MB）
    private FlyoutWindow EnsureFlyout()
    {
        if (_flyout is null)
        {
            _flyout = new FlyoutWindow();
            _flyout.PanelView.FullListClicked += ShowFullWindow;
            _flyout.PanelView.CopyClicked += CopyReport;
            _flyout.PanelView.ExitClicked += () => ExitRequested?.Invoke();
        }
        return _flyout;
    }

    // 第一次開啟時才建立；展開狀態保存在 FullWindowModel，關閉後再開仍在
    private FullWindow EnsureFullWindow()
    {
        if (_fullWindow is null)
        {
            _fullModel ??= new FullWindowModel(_settings.Current.Thresholds, TimeZoneInfo.Local);
            _fullWindow = new FullWindow { DataContext = _fullModel };
            _fullWindow.Closed += OnFullWindowClosed;
        }
        return _fullWindow;
    }

    // 隱藏的視窗會保留整個畫面樹，實測讓 MemSweep 超過 100 MB（Design 3.6），所以關閉時真正釋放：
    // 等 WPF 處理完關閉的工作後，請 GC 回收並把空出來的記憶體還給系統。只回收 MemSweep 自己的記憶體。
    private void OnFullWindowClosed(object? sender, EventArgs e)
    {
        _fullWindow = null;
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true)));
    }

    // 圖示顏色、小面板與完整視窗立即換成新的門檻，不等下一次取樣
    private void OnSettingsChanged(AppSettings settings)
    {
        if (_fullModel is not null)
        {
            _fullModel.Thresholds = settings.Thresholds;
        }
        if (_latest is not null)
        {
            Update(_latest, _latestPeaks!);
        }
    }

    private void ToggleFlyout()
    {
        if (_flyout is { IsVisible: true })
        {
            _flyout.HidePanel();
        }
        else if (_flyout is null || !_flyout.WasJustHidden)
        {
            ShowFlyout();
        }
    }

    private void CopyReport()
    {
        if (_latest is null || _flyout is null)
        {
            return;
        }

        bool succeeded;
        try
        {
            System.Windows.Clipboard.SetText(ReportFormatter.ToText(_latest));
            succeeded = true;
        }
        catch (ExternalException)
        {
            // 剪貼簿被其他程式佔用
            succeeded = false;
        }
        _flyout.PanelView.ShowCopyResult(succeeded);
    }
}
```

`app/MemSweep.Desktop/App.xaml.cs` 整份換成：

```csharp
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using MemSweep.Core;

namespace MemSweep.Desktop;

/// <summary>常駐系統匣，直到使用者選「結束 MemSweep」（F-01）。</summary>
public partial class App : Application
{
    private SingleInstance? _instance;
    private ThemeWatcher? _theme;
    private TrayController? _tray;
    private CancellationTokenSource? _sampling;
    private PeakRecorder? _peaks;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // 必須在建立任何視窗前設定（Design 3.6）
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        _instance = SingleInstance.TryAcquire(SingleInstance.MutexName, SingleInstance.ShowPanelEventName);
        if (_instance is null)
        {
            Shutdown();
            return;
        }

        // 設定與尖峰紀錄放在 exe 所在資料夾下的 data\（F-52）
        string data = Path.Combine(AppContext.BaseDirectory, "data");
        var settings = new SettingsService(new SettingsStore(data), new AutoStart(new RunKeyRegistry(), Environment.ProcessPath!));
        settings.SyncAutoStart();

        _theme = new ThemeWatcher(Resources);
        _tray = new TrayController(settings);
        _tray.ExitRequested += () => Shutdown();
        // UI 執行緒的工作一律用 BeginInvoke，例外才不會像 InvokeAsync 那樣消失（Design 3.4）
        _instance.ShowRequested += () => Dispatcher.BeginInvoke(new Action(() => _tray?.ShowFlyout()));
        StartSampling(data, settings);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _sampling?.Cancel();
        _sampling?.Dispose();
        // 還沒寫入的尖峰變動在結束前寫入（Design 3.9）
        _peaks?.Flush();
        _tray?.Dispose();
        _theme?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private void StartSampling(string data, SettingsService settings)
    {
        var opening = PeakStore.Open(data, TimeZoneInfo.Local);
        var peaks = new PeakRecorder(opening, settings.Current.RetentionDays);
        _peaks = peaks;
        // 保留天數改變後，下一次取樣就依新的天數清理（Design 3.9）
        settings.Changed += changed => peaks.SetRetentionDays(changed.RetentionDays);

        // MemorySampler 只在取樣迴圈裡使用；PeakRecorder 另外會在結束時由 UI 執行緒呼叫，自己有 lock
        var sampler = MemorySampler.CreateDefault();
        var loop = new SamplingLoop(
            () =>
            {
                var grouped = sampler.Sample();
                peaks.Record(grouped);
                return grouped;
            },
            grouped =>
            {
                var state = peaks.State;
                Dispatcher.BeginInvoke(new Action(() => _tray?.Update(grouped, state)));
            },
            SamplingLoop.DefaultPeriod);

        _sampling = new CancellationTokenSource();
        var token = _sampling.Token;
        Task.Run(() => loop.RunAsync(token)).ContinueWith(
            task => SamplingFailure.Raise(Dispatcher, task.Exception),
            TaskContinuationOptions.OnlyOnFaulted);
    }
}
```

- [ ] **Step 4：執行測試，確認通過**

執行：`(cd app && dotnet test)`

預期：總計 286，失敗 0。

- [ ] **Step 5：Commit**

```bash
git add app/MemSweep.Core/UsageThresholds.cs app/MemSweep.Core/PeakStore.cs app/MemSweep.Core/PeakRecorder.cs app/MemSweep.Core/FullWindowModel.cs app/MemSweep.Desktop/TrayController.cs app/MemSweep.Desktop/App.xaml.cs app/MemSweep.Core.Tests/PeakRecorderTests.cs app/MemSweep.Core.Tests/FullWindowModelTests.cs
git status --short
git commit -F - <<'EOF'
門檻與保留天數改讀設定

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

- [ ] **Step 6：推送**（依「共同執行規則」先 fetch 並檢查）

---

### Task 5：設定視窗與右鍵選單的「設定…」

**檔案：**

- 新增：`app/MemSweep.Desktop/Styles.xaml`、`app/MemSweep.Desktop/SettingsWindow.xaml`、`app/MemSweep.Desktop/SettingsWindow.xaml.cs`、`app/MemSweep.Desktop/TrayMenu.cs`
- 修改（整份換成）：`app/MemSweep.Desktop/FlyoutPanel.xaml`、`app/MemSweep.Desktop/TitleBar.xaml`、`app/MemSweep.Desktop/TrayController.cs`
- 新增：`app/MemSweep.Core.Tests/SettingsWindowTests.cs`、`app/MemSweep.Core.Tests/TrayMenuTests.cs`
- 修改（整份換成）：`app/MemSweep.Core.Tests/TitleBarTests.cs`

**實作的條文：**

> - F-07 右鍵選單有「開啟小面板」、「完整清單…」、「設定…」、「結束 MemSweep」。〔Claude 決定〕
> - 設定視窗（F-50）：橘色門檻、紅色門檻、開機自動啟動、保留天數。驗證規則：1 ≤ 橘色 < 紅色 ≤ 100；保留天數 1～3650。
>   - 數字用輸入框，關閉輸入法，注音模式下也直接打出半形數字；貼上的全形數字也接受。按「儲存」（Enter）時才檢查，不合法時在按鈕上方顯示第一個錯誤，視窗不關閉；「取消」（Esc）不儲存就關閉。
>   - 固定大小，沿用 `TitleBar`，只顯示關閉按鈕：`TitleBar` 依視窗的 `ResizeMode` 隱藏最小化與最大化按鈕。
>   - 和完整視窗一樣，第一次開啟時才建立，關閉時真正關閉並在 UI 空閒時回收記憶體；開著時再選「設定…」只會把它帶到前面。
> - F-62 MemSweep.exe 與 memsweep-cli.exe 使用 MemSweep 專屬的程式圖示，不使用預設圖示。有標題列的視窗使用自訂標題列，不使用系統預設的標題列。
> - F-61 介面使用繁體中文，深淺色跟隨 Windows 設定。〔Claude 決定〕

**介面：**

- 使用：Task 2 的 `SettingsForm`；Task 3 的 `SettingsService`；Task 4 的 `TrayController(SettingsService settings)`；`TitleBar`（Phase 03）。
- 產生：
  - `SettingsWindow`：`Func<AppSettings, string?>? Save`（回傳 null 表示成功）；`void ShowWindow()`；具名元素 `OrangeBox`、`RedBox`、`RetentionBox`、`AutoStartBox`、`ErrorText`、`SaveButton`、`CancelButton`、`Caption`
  - `TrayMenu.Build(Action showFlyout, Action showFullWindow, Action showSettings, Action exit)`：回傳 `ContextMenuStrip`
  - `TrayController.ShowSettings()`
  - `Styles.xaml` 的 `PanelButton` 樣式（小面板與設定視窗共用）

- [ ] **Step 1：寫失敗的測試**

新增 `app/MemSweep.Core.Tests/SettingsWindowTests.cs`：

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Shell;
using MemSweep.Core;
using MemSweep.Desktop;

namespace MemSweep.Core.Tests;

// 多個 STA 執行緒同時初始化 WPF 介面元件時會互相鎖死，介面測試一律依序執行
[TestClass]
[DoNotParallelize]
public sealed class SettingsWindowTests
{
    private static SettingsWindow NewWindow(AppSettings? current = null)
    {
        var window = new SettingsWindow { DataContext = new SettingsForm(current ?? AppSettings.Default) };
        WpfLayout.LaidOut((FrameworkElement)window.Content, 360);
        return window;
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    [STATestMethod]
    public void TheWindowShowsEverySetting()
    {
        var window = NewWindow(new AppSettings(60, 90, true, 30));

        CollectionAssert.IsSubsetOf(
            new[] { "MemSweep 設定", "橘色門檻", "紅色門檻", "保留天數", "開機時自動啟動 MemSweep", "儲存", "取消" },
            WpfLayout.Texts((DependencyObject)window.Content));
        Assert.AreEqual("60", window.OrangeBox.Text);
        Assert.AreEqual("90", window.RedBox.Text);
        Assert.AreEqual("30", window.RetentionBox.Text);
        Assert.IsTrue(window.AutoStartBox.IsChecked);
        Assert.AreEqual(Visibility.Collapsed, window.ErrorText.Visibility);
        window.Close();
    }

    [STATestMethod]
    public void EditsReachTheFormAsTheyAreTyped()
    {
        var window = NewWindow();
        var form = (SettingsForm)window.DataContext;

        window.OrangeBox.Text = "60";
        window.RetentionBox.Text = "30";
        window.AutoStartBox.IsChecked = true;

        Assert.AreEqual("60", form.OrangeText);
        Assert.AreEqual("30", form.RetentionText);
        Assert.IsTrue(form.AutoStart);
        window.Close();
    }

    [STATestMethod]
    public void SaveHandsOverTheSettingsAndCloses()
    {
        var window = NewWindow();
        AppSettings? saved = null;
        bool closed = false;
        window.Save = settings =>
        {
            saved = settings;
            return null;
        };
        window.Closed += (_, _) => closed = true;
        window.OrangeBox.Text = "60";
        window.AutoStartBox.IsChecked = true;

        Click(window.SaveButton);

        Assert.AreEqual(new AppSettings(60, 85, true, 90), saved);
        Assert.IsTrue(closed);
    }

    [STATestMethod]
    public void InvalidInputIsShownAndNothingIsSaved()
    {
        var window = NewWindow();
        bool saved = false;
        bool closed = false;
        window.Save = _ =>
        {
            saved = true;
            return null;
        };
        window.Closed += (_, _) => closed = true;
        window.RedBox.Text = "50";

        Click(window.SaveButton);

        Assert.IsFalse(saved);
        Assert.IsFalse(closed);
        Assert.AreEqual(Visibility.Visible, window.ErrorText.Visibility);
        Assert.AreEqual("橘色門檻必須小於紅色門檻", window.ErrorText.Text);
        window.Close();
    }

    [STATestMethod]
    public void ASaveFailureIsShownAndTheWindowStaysOpen()
    {
        var window = NewWindow();
        bool closed = false;
        window.Save = _ => "無法寫入 settings.json，設定沒有儲存";
        window.Closed += (_, _) => closed = true;

        Click(window.SaveButton);

        Assert.IsFalse(closed);
        Assert.AreEqual(Visibility.Visible, window.ErrorText.Visibility);
        Assert.AreEqual("無法寫入 settings.json，設定沒有儲存", window.ErrorText.Text);
        window.Close();
    }

    [STATestMethod]
    public void CancelClosesWithoutSaving()
    {
        var window = NewWindow();
        bool saved = false;
        bool closed = false;
        window.Save = _ =>
        {
            saved = true;
            return null;
        };
        window.Closed += (_, _) => closed = true;
        window.OrangeBox.Text = "60";

        Click(window.CancelButton);

        Assert.IsFalse(saved);
        Assert.IsTrue(closed);
    }

    [STATestMethod]
    public void EnterSavesAndEscapeCancels()
    {
        var window = NewWindow();

        Assert.IsTrue(window.SaveButton.IsDefault);
        Assert.IsTrue(window.CancelButton.IsCancel);
        window.Close();
    }

    [STATestMethod]
    public void TheWindowUsesTheCustomTitleBarWithOnlyACloseButton()
    {
        var window = NewWindow();

        var chrome = WindowChrome.GetWindowChrome(window);
        Assert.IsNotNull(chrome);
        Assert.AreEqual(32.0, chrome.CaptionHeight);
        Assert.AreEqual(ResizeMode.NoResize, window.ResizeMode);
        Assert.AreEqual("MemSweep 設定", window.Caption.TitleText.Text);
        Assert.AreEqual(Visibility.Collapsed, window.Caption.MinimizeButton.Visibility);
        Assert.AreEqual(Visibility.Collapsed, window.Caption.MaximizeButton.Visibility);
        Assert.AreEqual(Visibility.Visible, window.Caption.CloseButton.Visibility);
        window.Close();
    }

    [STATestMethod]
    public void NumberBoxesTurnOffTheInputMethod()
    {
        var window = NewWindow();

        Assert.IsFalse(InputMethod.GetIsInputMethodEnabled(window.OrangeBox));
        Assert.IsFalse(InputMethod.GetIsInputMethodEnabled(window.RedBox));
        Assert.IsFalse(InputMethod.GetIsInputMethodEnabled(window.RetentionBox));
        window.Close();
    }
}
```

新增 `app/MemSweep.Core.Tests/TrayMenuTests.cs`：

```csharp
using System.Windows.Forms;
using MemSweep.Desktop;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class TrayMenuTests
{
    [TestMethod]
    public void TheMenuListsTheSpecItemsInOrder()
    {
        using var menu = TrayMenu.Build(() => { }, () => { }, () => { }, () => { });

        CollectionAssert.AreEqual(
            new[] { "開啟小面板", "完整清單…", "設定…", string.Empty, "結束 MemSweep" },
            menu.Items.Cast<ToolStripItem>().Select(i => i.Text).ToArray());
        Assert.IsInstanceOfType<ToolStripSeparator>(menu.Items[3]);
    }

    [TestMethod]
    public void EachItemRunsItsAction()
    {
        var clicked = new List<string>();
        using var menu = TrayMenu.Build(
            () => clicked.Add("flyout"),
            () => clicked.Add("full"),
            () => clicked.Add("settings"),
            () => clicked.Add("exit"));

        foreach (var item in menu.Items.OfType<ToolStripMenuItem>())
        {
            item.PerformClick();
        }

        CollectionAssert.AreEqual(new[] { "flyout", "full", "settings", "exit" }, clicked);
    }
}
```

`app/MemSweep.Core.Tests/TitleBarTests.cs` 整份換成（新增 `CaptionButtonsFollowTheResizeMode`）：

```csharp
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Shell;
using MemSweep.Core;
using MemSweep.Desktop;

namespace MemSweep.Core.Tests;

// 多個 STA 執行緒同時初始化 WPF 介面元件時會互相鎖死，介面測試一律依序執行
[TestClass]
[DoNotParallelize]
public sealed class TitleBarTests
{
    private static readonly string MaximizeGlyph = ((char)0xE922).ToString();
    private static readonly string RestoreGlyph = ((char)0xE923).ToString();

    private static FullWindow NewWindow()
    {
        var model = new FullWindowModel(UsageThresholds.Default, TimeZoneInfo.Local);
        model.Update(ReportSamples.Typical(), new PeakState(null, PeakHistory.Empty, null));
        var window = new FullWindow { DataContext = model };
        WpfLayout.LaidOut((FrameworkElement)window.Content, 760);
        return window;
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static Bitmap Pixels(Icon icon) => icon.ToBitmap();

    [STATestMethod]
    public void TheWindowReplacesTheSystemTitleBar()
    {
        var window = NewWindow();

        var chrome = WindowChrome.GetWindowChrome(window);
        Assert.IsNotNull(chrome);
        Assert.AreEqual(32.0, chrome.CaptionHeight);
        Assert.AreEqual(new Thickness(0), chrome.GlassFrameThickness);
        Assert.IsFalse(chrome.UseAeroCaptionButtons);
        Assert.AreEqual(32.0, window.Caption.ActualHeight);
        window.Close();
    }

    [STATestMethod]
    public void TitleBarShowsTheTitleAndThreeCaptionButtons()
    {
        var window = NewWindow();
        var caption = window.Caption;

        Assert.AreEqual("MemSweep", caption.TitleText.Text);
        var buttons = new[] { caption.MinimizeButton, caption.MaximizeButton, caption.CloseButton };
        CollectionAssert.AreEqual(new[] { "最小化", "最大化", "關閉" }, buttons.Select(b => b.ToolTip as string).ToArray());
        CollectionAssert.AreEqual(
            new[] { ((char)0xE921).ToString(), MaximizeGlyph, ((char)0xE8BB).ToString() },
            buttons.Select(b => b.Content as string).ToArray());
        Assert.IsTrue(buttons.All(WindowChrome.GetIsHitTestVisibleInChrome));
        window.Close();
    }

    [STATestMethod]
    [DataRow(ResizeMode.CanResize, true, true)]
    [DataRow(ResizeMode.CanResizeWithGrip, true, true)]
    [DataRow(ResizeMode.CanMinimize, true, false)]
    [DataRow(ResizeMode.NoResize, false, false)]
    public void CaptionButtonsFollowTheResizeMode(ResizeMode mode, bool minimize, bool maximize)
    {
        var window = NewWindow();

        window.ResizeMode = mode;

        Assert.AreEqual(minimize ? Visibility.Visible : Visibility.Collapsed, window.Caption.MinimizeButton.Visibility);
        Assert.AreEqual(maximize ? Visibility.Visible : Visibility.Collapsed, window.Caption.MaximizeButton.Visibility);
        Assert.AreEqual(Visibility.Visible, window.Caption.CloseButton.Visibility);
        window.Close();
    }

    [STATestMethod]
    public void CaptionButtonsMaximizeRestoreAndMinimizeTheWindow()
    {
        var window = NewWindow();
        var caption = window.Caption;

        Click(caption.MaximizeButton);
        Assert.AreEqual(WindowState.Maximized, window.WindowState);
        Assert.AreEqual(RestoreGlyph, caption.MaximizeButton.Content);
        Assert.AreEqual("還原", caption.MaximizeButton.ToolTip);

        Click(caption.MaximizeButton);
        Assert.AreEqual(WindowState.Normal, window.WindowState);
        Assert.AreEqual(MaximizeGlyph, caption.MaximizeButton.Content);

        Click(caption.MinimizeButton);
        Assert.AreEqual(WindowState.Minimized, window.WindowState);
        window.Close();
    }

    [STATestMethod]
    public void TheCloseButtonClosesTheWindow()
    {
        var window = NewWindow();
        bool closed = false;
        window.Closed += (_, _) => closed = true;

        Click(window.Caption.CloseButton);

        Assert.IsTrue(closed);
    }

    [STATestMethod]
    public void AMaximizedWindowInsetsItsContentByTheFrame()
    {
        var window = NewWindow();

        window.WindowState = WindowState.Maximized;
        Assert.AreEqual(TitleBar.MaximizedMargin, window.Frame.Margin);
        Assert.IsGreaterThan(0.0, window.Frame.Margin.Left);
        Assert.IsGreaterThan(0.0, window.Frame.Margin.Top);

        window.WindowState = WindowState.Normal;
        Assert.AreEqual(new Thickness(0), window.Frame.Margin);
        window.Close();
    }

    [TestMethod]
    public void TheMaximizedInsetIsTheResizeFramePlusThePaddedBorder()
    {
        // 原型實測 150%：框線 5 px 加邊框間距 6 px 才剛好；WPF 的 SystemParameters 只有框線的 3.33
        Assert.AreEqual(new Thickness(11 / 1.5), TitleBar.Inset(5, 5, 6, 144));
        Assert.AreEqual(new Thickness(8, 9, 8, 9), TitleBar.Inset(4, 5, 4, 96));
    }

    [STATestMethod]
    public void TheIconHasEveryStandardWindowsSize()
    {
        CollectionAssert.AreEquivalent(
            new[] { 16, 20, 24, 32, 40, 48, 64, 256 },
            AppIcon.Frames().Select(f => f.PixelWidth).ToArray());
        Assert.IsTrue(AppIcon.Frames().All(f => f.PixelWidth == f.PixelHeight));
    }

    [TestMethod]
    [DataRow(16, 16)]
    [DataRow(20, 20)]
    [DataRow(24, 24)]
    [DataRow(30, 32)]
    [DataRow(33, 40)]
    [DataRow(512, 256)]
    public void TheTitleBarPicksTheSmallestFrameThatIsLargeEnough(int pixels, int expected)
    {
        Assert.AreEqual(expected, AppIcon.PickSize([256, 16, 20, 24, 32, 40, 48, 64], pixels));
    }

    [STATestMethod]
    [DataRow("MemSweep.exe")]
    [DataRow("memsweep-cli.exe")]
    public void BothExecutablesCarryTheIcon(string exe)
    {
        // 測試輸出資料夾裡有兩支 exe；取出 exe 內嵌的圖示，與 Assets/MemSweep.ico 同尺寸的一格逐像素比對
        using var embedded = Icon.ExtractAssociatedIcon(Path.Combine(AppContext.BaseDirectory, exe))!;
        using var stream = System.Windows.Application.GetResourceStream(AppIcon.Source).Stream;
        using var expected = new Icon(stream, embedded.Width, embedded.Height);
        using var actualPixels = Pixels(embedded);
        using var expectedPixels = Pixels(expected);

        Assert.AreEqual(expected.Width, embedded.Width);
        for (int y = 0; y < actualPixels.Height; y++)
        {
            for (int x = 0; x < actualPixels.Width; x++)
            {
                Assert.AreEqual(expectedPixels.GetPixel(x, y), actualPixels.GetPixel(x, y), $"({x}, {y})");
            }
        }
    }
}
```

- [ ] **Step 2：執行測試，確認失敗**

執行：`(cd app && dotnet test)`

預期：建置失敗，錯誤是找不到 `SettingsWindow`（CS0246）。

- [ ] **Step 3：實作**

新增 `app/MemSweep.Desktop/Styles.xaml`（原本在 `FlyoutPanel.xaml` 裡的按鈕樣式）：

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <!-- 小面板與設定視窗共用的按鈕，顏色跟著深淺色 -->
  <Style x:Key="PanelButton" TargetType="Button">
    <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />
    <Setter Property="Background" Value="{DynamicResource ButtonBackgroundBrush}" />
    <Setter Property="BorderBrush" Value="{DynamicResource ButtonBorderBrush}" />
    <Setter Property="Padding" Value="10,4" />
    <Setter Property="Margin" Value="0,0,6,0" />
    <Setter Property="Cursor" Value="Hand" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Button">
          <Border x:Name="Chrome"
                  Background="{TemplateBinding Background}"
                  BorderBrush="{TemplateBinding BorderBrush}"
                  BorderThickness="1"
                  CornerRadius="4"
                  Padding="{TemplateBinding Padding}">
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource ButtonHoverBrush}" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>
```

`app/MemSweep.Desktop/FlyoutPanel.xaml` 整份換成（按鈕樣式改從 `Styles.xaml` 取得）：

```xml
<UserControl x:Class="MemSweep.Desktop.FlyoutPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:MemSweep.Desktop"
             Width="330"
             FontFamily="Segoe UI, Microsoft JhengHei UI"
             FontSize="12"
             Foreground="{DynamicResource TextBrush}">
  <UserControl.Resources>
    <ResourceDictionary Source="Styles.xaml" />
  </UserControl.Resources>

  <Border Background="{DynamicResource PanelBackgroundBrush}"
          BorderBrush="{DynamicResource PanelBorderBrush}"
          BorderThickness="1"
          CornerRadius="8"
          Padding="14,12">
    <StackPanel>
      <local:SummaryView x:Name="Summary" />

      <StackPanel Orientation="Horizontal" Margin="0,12,0,0">
        <Button Content="完整清單…" Style="{StaticResource PanelButton}" Click="FullListButton_Click" />
        <Button x:Name="CopyButton" Content="複製報告" Style="{StaticResource PanelButton}" Click="CopyButton_Click" />
        <Button Content="結束 MemSweep" Style="{StaticResource PanelButton}" Click="ExitButton_Click" />
      </StackPanel>
    </StackPanel>
  </Border>
</UserControl>
```

`app/MemSweep.Desktop/TitleBar.xaml` 整份換成：

```xml
<UserControl x:Class="MemSweep.Desktop.TitleBar"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Height="32">
  <!-- 自訂標題列（F-62）：圖示、標題與最小化／最大化／關閉；拖曳、雙擊最大化與右鍵系統選單交給視窗的 WindowChrome -->
  <UserControl.Resources>
    <Style x:Key="CaptionButton" TargetType="Button">
      <Setter Property="Width" Value="46" />
      <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />
      <Setter Property="Background" Value="Transparent" />
      <Setter Property="FontFamily" Value="Segoe Fluent Icons, Segoe MDL2 Assets" />
      <Setter Property="FontSize" Value="10" />
      <Setter Property="IsTabStop" Value="False" />
      <Setter Property="WindowChrome.IsHitTestVisibleInChrome" Value="True" />
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="Button">
            <Border Background="{TemplateBinding Background}">
              <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
            </Border>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
      <Style.Triggers>
        <Trigger Property="IsMouseOver" Value="True">
          <Setter Property="Background" Value="{DynamicResource ButtonHoverBrush}" />
        </Trigger>
        <Trigger Property="IsPressed" Value="True">
          <Setter Property="Background" Value="{DynamicResource TrackBrush}" />
        </Trigger>
      </Style.Triggers>
    </Style>
    <Style x:Key="CloseCaptionButton" TargetType="Button" BasedOn="{StaticResource CaptionButton}">
      <Style.Triggers>
        <Trigger Property="IsMouseOver" Value="True">
          <Setter Property="Background" Value="{DynamicResource CloseHoverBrush}" />
          <Setter Property="Foreground" Value="White" />
        </Trigger>
      </Style.Triggers>
    </Style>
  </UserControl.Resources>

  <DockPanel>
    <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
      <!-- 按鈕跟著視窗的 ResizeMode：不能調整大小的視窗（例如設定視窗）只有關閉按鈕 -->
      <Button x:Name="MinimizeButton"
              Content="&#xE921;"
              ToolTip="最小化"
              Click="MinimizeButton_Click">
        <Button.Style>
          <Style TargetType="Button" BasedOn="{StaticResource CaptionButton}">
            <Style.Triggers>
              <DataTrigger Binding="{Binding ResizeMode, RelativeSource={RelativeSource AncestorType=Window}}" Value="NoResize">
                <Setter Property="Visibility" Value="Collapsed" />
              </DataTrigger>
            </Style.Triggers>
          </Style>
        </Button.Style>
      </Button>
      <Button x:Name="MaximizeButton" Click="MaximizeButton_Click">
        <Button.Style>
          <Style TargetType="Button" BasedOn="{StaticResource CaptionButton}">
            <Setter Property="Content" Value="&#xE922;" />
            <Setter Property="ToolTip" Value="最大化" />
            <Style.Triggers>
              <DataTrigger Binding="{Binding WindowState, RelativeSource={RelativeSource AncestorType=Window}}" Value="Maximized">
                <Setter Property="Content" Value="&#xE923;" />
                <Setter Property="ToolTip" Value="還原" />
              </DataTrigger>
              <DataTrigger Binding="{Binding ResizeMode, RelativeSource={RelativeSource AncestorType=Window}}" Value="NoResize">
                <Setter Property="Visibility" Value="Collapsed" />
              </DataTrigger>
              <DataTrigger Binding="{Binding ResizeMode, RelativeSource={RelativeSource AncestorType=Window}}" Value="CanMinimize">
                <Setter Property="Visibility" Value="Collapsed" />
              </DataTrigger>
            </Style.Triggers>
          </Style>
        </Button.Style>
      </Button>
      <Button x:Name="CloseButton"
              Content="&#xE8BB;"
              ToolTip="關閉"
              Style="{StaticResource CloseCaptionButton}"
              Click="CloseButton_Click" />
    </StackPanel>

    <Image x:Name="IconImage" Width="16" Height="16" Margin="12,0,8,0" VerticalAlignment="Center" />
    <TextBlock x:Name="TitleText"
               Text="{Binding Title, RelativeSource={RelativeSource AncestorType=Window}}"
               VerticalAlignment="Center"
               TextTrimming="CharacterEllipsis">
      <TextBlock.Style>
        <Style TargetType="TextBlock">
          <Style.Triggers>
            <!-- 視窗不在前景時標題變淡，和 Windows 的標題列一樣 -->
            <DataTrigger Binding="{Binding IsActive, RelativeSource={RelativeSource AncestorType=Window}}" Value="False">
              <Setter Property="Foreground" Value="{DynamicResource MutedTextBrush}" />
            </DataTrigger>
          </Style.Triggers>
        </Style>
      </TextBlock.Style>
    </TextBlock>
  </DockPanel>
</UserControl>
```

新增 `app/MemSweep.Desktop/SettingsWindow.xaml`：

```xml
<Window x:Class="MemSweep.Desktop.SettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:MemSweep.Desktop"
        Title="MemSweep 設定"
        Width="360"
        SizeToContent="Height"
        ResizeMode="NoResize"
        WindowStartupLocation="CenterScreen"
        FontFamily="Segoe UI, Microsoft JhengHei UI"
        FontSize="12"
        Background="{DynamicResource PanelBackgroundBrush}"
        Foreground="{DynamicResource TextBrush}">
  <!-- 設定視窗（F-50、Design 3.6）；DataContext 是 SettingsForm -->
  <!-- 自訂標題列（F-62）：不能調整大小，TitleBar 只顯示關閉按鈕 -->
  <WindowChrome.WindowChrome>
    <WindowChrome CaptionHeight="32"
                  ResizeBorderThickness="0"
                  GlassFrameThickness="0"
                  CornerRadius="0"
                  UseAeroCaptionButtons="False" />
  </WindowChrome.WindowChrome>

  <Window.Resources>
    <ResourceDictionary>
      <ResourceDictionary.MergedDictionaries>
        <ResourceDictionary Source="Styles.xaml" />
      </ResourceDictionary.MergedDictionaries>

      <!-- 數字輸入框：關閉輸入法，注音模式下也直接打出半形數字 -->
      <Style TargetType="TextBox">
        <Setter Property="Width" Value="64" />
        <Setter Property="Padding" Value="4,2" />
        <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />
        <Setter Property="Background" Value="{DynamicResource ButtonBackgroundBrush}" />
        <Setter Property="BorderBrush" Value="{DynamicResource ButtonBorderBrush}" />
        <Setter Property="CaretBrush" Value="{DynamicResource TextBrush}" />
        <Setter Property="HorizontalContentAlignment" Value="Right" />
        <Setter Property="VerticalContentAlignment" Value="Center" />
        <Setter Property="InputMethod.IsInputMethodEnabled" Value="False" />
      </Style>

      <!-- 自訂核取方塊，深色模式下才不會出現系統預設的白色方塊 -->
      <Style TargetType="CheckBox">
        <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="Template">
          <Setter.Value>
            <ControlTemplate TargetType="CheckBox">
              <StackPanel Orientation="Horizontal" Background="Transparent">
                <Border x:Name="Box"
                        Width="16"
                        Height="16"
                        CornerRadius="3"
                        BorderThickness="1"
                        BorderBrush="{DynamicResource ButtonBorderBrush}"
                        Background="{DynamicResource ButtonBackgroundBrush}"
                        VerticalAlignment="Center">
                  <TextBlock x:Name="Mark"
                             Text="&#xE73E;"
                             FontFamily="Segoe Fluent Icons, Segoe MDL2 Assets"
                             FontSize="10"
                             Foreground="White"
                             HorizontalAlignment="Center"
                             VerticalAlignment="Center"
                             Visibility="Collapsed" />
                </Border>
                <ContentPresenter Margin="8,0,0,0" VerticalAlignment="Center" />
              </StackPanel>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True">
                  <Setter TargetName="Box" Property="BorderBrush" Value="{DynamicResource AccentBrush}" />
                </Trigger>
                <Trigger Property="IsChecked" Value="True">
                  <Setter TargetName="Box" Property="Background" Value="{DynamicResource AccentBrush}" />
                  <Setter TargetName="Box" Property="BorderBrush" Value="{DynamicResource AccentBrush}" />
                  <Setter TargetName="Mark" Property="Visibility" Value="Visible" />
                </Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
          </Setter.Value>
        </Setter>
      </Style>
    </ResourceDictionary>
  </Window.Resources>

  <Border BorderBrush="{DynamicResource PanelBorderBrush}" BorderThickness="1">
    <DockPanel>
      <local:TitleBar x:Name="Caption" DockPanel.Dock="Top" />
      <StackPanel Margin="20,8,20,20">
        <TextBlock Text="commit 使用率的顏色" FontWeight="SemiBold" />
        <Grid Margin="0,8,0,0">
          <Grid.ColumnDefinitions>
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="*" />
          </Grid.ColumnDefinitions>
          <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
          </Grid.RowDefinitions>
          <TextBlock Text="橘色門檻" VerticalAlignment="Center" Margin="0,0,12,0" />
          <TextBox x:Name="OrangeBox" Grid.Column="1" Text="{Binding OrangeText, UpdateSourceTrigger=PropertyChanged}" />
          <TextBlock Grid.Column="2" Text="%" VerticalAlignment="Center" Margin="6,0,0,0" />
          <TextBlock Grid.Row="1" Text="紅色門檻" VerticalAlignment="Center" Margin="0,6,12,0" />
          <TextBox x:Name="RedBox" Grid.Row="1" Grid.Column="1" Margin="0,6,0,0" Text="{Binding RedText, UpdateSourceTrigger=PropertyChanged}" />
          <TextBlock Grid.Row="1" Grid.Column="2" Text="%" VerticalAlignment="Center" Margin="6,6,0,0" />
        </Grid>
        <TextBlock Text="低於橘色門檻為綠色，達到紅色門檻以上為紅色。"
                   Foreground="{DynamicResource MutedTextBrush}"
                   TextWrapping="Wrap"
                   Margin="0,6,0,0" />

        <TextBlock Text="尖峰紀錄" FontWeight="SemiBold" Margin="0,16,0,0" />
        <StackPanel Orientation="Horizontal" Margin="0,8,0,0">
          <TextBlock Text="保留天數" VerticalAlignment="Center" Margin="0,0,12,0" />
          <TextBox x:Name="RetentionBox" Text="{Binding RetentionText, UpdateSourceTrigger=PropertyChanged}" />
          <TextBlock Text="天" VerticalAlignment="Center" Margin="6,0,0,0" />
        </StackPanel>
        <TextBlock Text="減少天數後，超過天數的紀錄會在下一次取樣時刪除。"
                   Foreground="{DynamicResource MutedTextBrush}"
                   TextWrapping="Wrap"
                   Margin="0,6,0,0" />

        <TextBlock Text="啟動" FontWeight="SemiBold" Margin="0,16,0,0" />
        <CheckBox x:Name="AutoStartBox" Content="開機時自動啟動 MemSweep" IsChecked="{Binding AutoStart}" Margin="0,8,0,0" />

        <!-- 輸入不合法或儲存失敗的原因，沒有問題時不顯示 -->
        <TextBlock x:Name="ErrorText"
                   Text="{Binding Error}"
                   Foreground="{DynamicResource RedBrush}"
                   TextWrapping="Wrap"
                   Margin="0,16,0,0">
          <TextBlock.Style>
            <Style TargetType="TextBlock">
              <Style.Triggers>
                <DataTrigger Binding="{Binding Error}" Value="{x:Null}">
                  <Setter Property="Visibility" Value="Collapsed" />
                </DataTrigger>
              </Style.Triggers>
            </Style>
          </TextBlock.Style>
        </TextBlock>

        <!-- Enter 等同「儲存」，Esc 等同「取消」 -->
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,16,0,0">
          <Button x:Name="SaveButton"
                  Content="儲存"
                  MinWidth="72"
                  IsDefault="True"
                  Style="{StaticResource PanelButton}"
                  Click="SaveButton_Click" />
          <Button x:Name="CancelButton"
                  Content="取消"
                  MinWidth="72"
                  Margin="0"
                  IsCancel="True"
                  Style="{StaticResource PanelButton}"
                  Click="CancelButton_Click" />
        </StackPanel>
      </StackPanel>
    </DockPanel>
  </Border>
</Window>
```

新增 `app/MemSweep.Desktop/SettingsWindow.xaml.cs`：

```csharp
using System.Windows;
using MemSweep.Core;

namespace MemSweep.Desktop;

/// <summary>
/// 設定視窗（F-50、Design 3.6）：DataContext 是 <see cref="SettingsForm"/>。按「儲存」時檢查輸入並交給 <see cref="Save"/>，
/// 成功就關閉。關閉時真正關閉並釋放，下次開啟由 <see cref="TrayController"/> 重新建立。
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    /// <summary>儲存新的設定：回傳 null 表示成功，否則是要顯示在視窗裡的訊息。</summary>
    public Func<AppSettings, string?>? Save { get; set; }

    public void ShowWindow()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Show();
        Activate();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var form = (SettingsForm)DataContext;
        if (form.TryRead() is not { } settings)
        {
            return;
        }
        if (Save?.Invoke(settings) is { } error)
        {
            form.ShowError(error);
            return;
        }
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();
}
```

新增 `app/MemSweep.Desktop/TrayMenu.cs`：

```csharp
using System.Windows.Forms;

namespace MemSweep.Desktop;

/// <summary>系統匣的右鍵選單（F-07）。</summary>
internal static class TrayMenu
{
    public static ContextMenuStrip Build(Action showFlyout, Action showFullWindow, Action showSettings, Action exit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("開啟小面板", null, (_, _) => showFlyout());
        menu.Items.Add("完整清單…", null, (_, _) => showFullWindow());
        menu.Items.Add("設定…", null, (_, _) => showSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("結束 MemSweep", null, (_, _) => exit());
        return menu;
    }
}
```

`app/MemSweep.Desktop/TrayController.cs` 整份換成：

```csharp
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Threading;
using MemSweep.Core;

namespace MemSweep.Desktop;

/// <summary>系統匣圖示、提示文字、右鍵選單、小面板、完整視窗與設定視窗（F-01～F-24、F-50）。必須在 UI 執行緒上使用。</summary>
internal sealed class TrayController : IDisposable
{
    private readonly SettingsService _settings;
    private readonly NotifyIcon _notifyIcon;
    private FlyoutWindow? _flyout;
    private FullWindow? _fullWindow;
    private FullWindowModel? _fullModel;
    private SettingsWindow? _settingsWindow;
    private bool _flyoutRequested;
    private Icon? _icon;
    private string? _iconKey;
    private GroupedSnapshot? _latest;
    private PeakState? _latestPeaks;

    public TrayController(SettingsService settings)
    {
        _settings = settings;
        _settings.Changed += OnSettingsChanged;
        var menu = TrayMenu.Build(ShowFlyout, ShowFullWindow, ShowSettings, () => ExitRequested?.Invoke());
        _notifyIcon = new NotifyIcon { Text = "MemSweep", ContextMenuStrip = menu };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ToggleFlyout();
            }
        };
    }

    public event Action? ExitRequested;

    public void ShowFlyout()
    {
        // 還沒有第一次取樣的結果時沒有內容可顯示，先記下來，取樣完成後再顯示
        if (_latest is null)
        {
            _flyoutRequested = true;
            return;
        }
        var flyout = EnsureFlyout();
        flyout.PanelView.DataContext = BuildPanel();
        flyout.ShowPanel();
    }

    public void Update(GroupedSnapshot grouped, PeakState peaks)
    {
        var snapshot = grouped.Snapshot;
        string text = TrayText.IconText(snapshot);
        var level = _settings.Current.Thresholds.LevelOf(TrayText.CommitPercent(snapshot));
        int size = SystemInformation.SmallIconSize.Width;

        // 數字、顏色與尺寸都沒變就不重畫
        string key = $"{text}|{level}|{size}";
        if (key != _iconKey)
        {
            var previous = _icon;
            _icon = TrayIconRenderer.Render(text, level, size);
            _iconKey = key;
            _notifyIcon.Icon = _icon;
            previous?.Dispose();
        }

        _notifyIcon.Text = TrayText.Tooltip(snapshot);
        _notifyIcon.Visible = true;
        _latest = grouped;
        _latestPeaks = peaks;
        if (_flyoutRequested)
        {
            _flyoutRequested = false;
            ShowFlyout();
        }
        else if (_flyout is { IsVisible: true })
        {
            _flyout.PanelView.DataContext = BuildPanel();
        }
        if (_fullWindow is { IsVisible: true })
        {
            _fullModel!.Update(grouped, peaks);
        }
    }

    public void ShowFullWindow()
    {
        if (_latest is null)
        {
            return;
        }
        _flyout?.HidePanel();
        var window = EnsureFullWindow();
        _fullModel!.Update(_latest, _latestPeaks!);
        window.ShowWindow();
    }

    /// <summary>開啟設定視窗；已經開著時帶到前面。</summary>
    public void ShowSettings()
    {
        _flyout?.HidePanel();
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow { DataContext = new SettingsForm(_settings.Current), Save = _settings.Save };
            _settingsWindow.Closed += OnSettingsWindowClosed;
        }
        _settingsWindow.ShowWindow();
    }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        if (_fullWindow is { } window)
        {
            window.Closed -= OnFullWindowClosed;
            window.Close();
        }
        if (_settingsWindow is { } settingsWindow)
        {
            settingsWindow.Closed -= OnSettingsWindowClosed;
            settingsWindow.Close();
        }
        _flyout?.Close();
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _icon?.Dispose();
    }

    private PanelModel BuildPanel() =>
        PanelModel.Build(_latest!, _latestPeaks?.BootPeak, _settings.Current.Thresholds, _latestPeaks?.StorageWarning);

    // 第一次開啟時才建立，之後只顯示／隱藏：只用系統匣時，行程不必載入 WPF 視窗（約省 40 MB）
    private FlyoutWindow EnsureFlyout()
    {
        if (_flyout is null)
        {
            _flyout = new FlyoutWindow();
            _flyout.PanelView.FullListClicked += ShowFullWindow;
            _flyout.PanelView.CopyClicked += CopyReport;
            _flyout.PanelView.ExitClicked += () => ExitRequested?.Invoke();
        }
        return _flyout;
    }

    // 第一次開啟時才建立；展開狀態保存在 FullWindowModel，關閉後再開仍在
    private FullWindow EnsureFullWindow()
    {
        if (_fullWindow is null)
        {
            _fullModel ??= new FullWindowModel(_settings.Current.Thresholds, TimeZoneInfo.Local);
            _fullWindow = new FullWindow { DataContext = _fullModel };
            _fullWindow.Closed += OnFullWindowClosed;
        }
        return _fullWindow;
    }

    private void OnFullWindowClosed(object? sender, EventArgs e)
    {
        _fullWindow = null;
        ReleaseMemoryWhenIdle();
    }

    private void OnSettingsWindowClosed(object? sender, EventArgs e)
    {
        _settingsWindow = null;
        ReleaseMemoryWhenIdle();
    }

    // 隱藏的視窗會保留整個畫面樹，實測讓 MemSweep 超過 100 MB（Design 3.6），所以視窗關閉時真正釋放：
    // 等 WPF 處理完關閉的工作後，請 GC 回收並把空出來的記憶體還給系統。只回收 MemSweep 自己的記憶體。
    private static void ReleaseMemoryWhenIdle() =>
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true)));

    // 圖示顏色、小面板與完整視窗立即換成新的門檻，不等下一次取樣
    private void OnSettingsChanged(AppSettings settings)
    {
        if (_fullModel is not null)
        {
            _fullModel.Thresholds = settings.Thresholds;
        }
        if (_latest is not null)
        {
            Update(_latest, _latestPeaks!);
        }
    }

    private void ToggleFlyout()
    {
        if (_flyout is { IsVisible: true })
        {
            _flyout.HidePanel();
        }
        else if (_flyout is null || !_flyout.WasJustHidden)
        {
            ShowFlyout();
        }
    }

    private void CopyReport()
    {
        if (_latest is null || _flyout is null)
        {
            return;
        }

        bool succeeded;
        try
        {
            System.Windows.Clipboard.SetText(ReportFormatter.ToText(_latest));
            succeeded = true;
        }
        catch (ExternalException)
        {
            // 剪貼簿被其他程式佔用
            succeeded = false;
        }
        _flyout.PanelView.ShowCopyResult(succeeded);
    }
}
```

- [ ] **Step 4：執行測試，確認通過**

執行：`(cd app && dotnet test)`

預期：總計 301，失敗 0。

- [ ] **Step 5：Commit**

```bash
git add app/MemSweep.Desktop/Styles.xaml app/MemSweep.Desktop/FlyoutPanel.xaml app/MemSweep.Desktop/TitleBar.xaml app/MemSweep.Desktop/SettingsWindow.xaml app/MemSweep.Desktop/SettingsWindow.xaml.cs app/MemSweep.Desktop/TrayMenu.cs app/MemSweep.Desktop/TrayController.cs app/MemSweep.Core.Tests/SettingsWindowTests.cs app/MemSweep.Core.Tests/TrayMenuTests.cs app/MemSweep.Core.Tests/TitleBarTests.cs
git status --short
git commit -F - <<'EOF'
新增設定視窗與右鍵選單的「設定…」

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

- [ ] **Step 6：推送**（依「共同執行規則」先 fetch 並檢查）

---

### Task 6：併入延後的小修

**檔案：**

- 修改（整份換成）：`app/MemSweep.Core/PeakCsv.cs`、`app/MemSweep.Desktop/TrayController.cs`、`app/MemSweep.Core.Tests/PeakCsvTests.cs`

**實作的條文：**

> 時間換算成 UTC 會超出範圍（例如台北時間 0001-01-01 凌晨）或 GB 數值大於 1048576（1 PB）時，也視為格式不符，不讓 MemSweep 啟動時當掉。
> - F-24 尖峰紀錄：列出開機尖峰與每日尖峰紀錄（7.4），每筆顯示時間、commit 已用、commit 上限與當時的前五名應用程式。

PR #3「延後處理的小問題」中的三項：

- 紀錄檔裡 0001-01-01 前 8 小時的時間會讓啟動當掉；GB 欄沒有範圍檢查
- 第一次取樣前按「完整清單…」沒有反應
- `PeakCsv.TopCount` 與 `ReportFormatter.TopCount` 重複

**介面：**

- 使用：`ReportFormatter.TopCount`（Phase 01）。
- 產生：無新的公開介面。

- [ ] **Step 1：寫失敗的測試**

`app/MemSweep.Core.Tests/PeakCsvTests.cs` 整份換成（新增 `RejectsBootValuesOutsideTheSupportedRange`、`RejectsDailyValuesOutsideTheSupportedRange`、`AcceptsTheLimitsOfTheSupportedRange`）：

```csharp
using System.Text;
using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class PeakCsvTests
{
    private const string BootHeader =
        "開機時間,尖峰時間,commit已用GB,commit上限GB,第1名,第2名,第3名,第4名,第5名,第1名GB,第2名GB,第3名GB,第4名GB,第5名GB";

    private const string DailyHeader =
        "日期,尖峰時間,commit已用GB,commit上限GB,第1名,第2名,第3名,第4名,第5名,第1名GB,第2名GB,第3名GB,第4名GB,第5名GB";

    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time");
    private static readonly DateTime Boot = new(2026, 10, 26, 0, 30, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset PeakTime = new(2026, 10, 26, 14, 35, 2, TimeSpan.FromHours(8));

    private static long Gb(double value) => MemoryMath.FromGigabytes(value);

    private static readonly PeakApp[] FiveApps =
    [
        new("Claude", Gb(3.2)),
        new("ChatGPT", Gb(2.2)),
        new("Microsoft Edge WebView2", Gb(1.8)),
        new(AppGrouper.WindowsGroupName, Gb(1.5)),
        new("Node.js JavaScript Runtime", Gb(1.4)),
    ];

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes.AsSpan(3));

    private static byte[] Utf8(string text) => [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(text)];

    [TestMethod]
    public void WritesUtf8WithBomCrlfAndChineseHeaders()
    {
        byte[] bytes = PeakCsv.WriteDays([new DailyPeak(new DateOnly(2026, 10, 26), Gb(31.4), Gb(39.4), PeakTime, FiveApps)], Zone);

        CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        string text = Text(bytes);
        StringAssert.StartsWith(text, DailyHeader + "\r\n");
        StringAssert.EndsWith(text, "\r\n");
        Assert.AreEqual(text.Split('\n').Length - 1, text.Split("\r\n").Length - 1);
    }

    [TestMethod]
    public void KnownBootPeakRow()
    {
        var boot = new BootPeak(Boot, Gb(36.8), Gb(39.4), PeakTime, FiveApps);

        string text = Text(PeakCsv.WriteBoots([boot], Zone));

        Assert.AreEqual(
            BootHeader + "\r\n" +
            "2026-10-26 08:30:00,2026-10-26 14:35:02,36.80,39.40," +
            "Claude,ChatGPT,Microsoft Edge WebView2,Windows（系統與服務）,Node.js JavaScript Runtime," +
            "3.20,2.20,1.80,1.50,1.40\r\n",
            text);
    }

    [TestMethod]
    public void UnknownBootPeakRow()
    {
        var boot = new BootPeak(Boot, Gb(36.8), Gb(39.4), null, null);

        string text = Text(PeakCsv.WriteBoots([boot], Zone));

        Assert.AreEqual(BootHeader + "\r\n" + "2026-10-26 08:30:00,未知,36.80,39.40,未知,,,,,,,,,\r\n", text);
    }

    [TestMethod]
    public void DailyRowWithFewerThanFiveAppsLeavesTheRestEmpty()
    {
        var day = new DailyPeak(new DateOnly(2026, 10, 26), Gb(31.4), Gb(39.4), PeakTime, FiveApps.Take(2).ToList());

        string text = Text(PeakCsv.WriteDays([day], Zone));

        Assert.AreEqual(DailyHeader + "\r\n" + "2026-10-26,2026-10-26 14:35:02,31.40,39.40,Claude,ChatGPT,,,,3.20,2.20,,,\r\n", text);
    }

    [TestMethod]
    public void TimesAreWrittenInTheGivenZone()
    {
        // 同一個時刻以 UTC 表示，寫出時仍換成本機時間
        var day = new DailyPeak(new DateOnly(2026, 10, 26), Gb(31.4), Gb(39.4), PeakTime.ToUniversalTime(), FiveApps);

        StringAssert.Contains(Text(PeakCsv.WriteDays([day], Zone)), ",2026-10-26 14:35:02,");
    }

    [TestMethod]
    public void GigabytesKeepTwoDecimals()
    {
        var day = new DailyPeak(new DateOnly(2026, 10, 26), Gb(36.8372), Gb(39.4), PeakTime, []);

        byte[] bytes = PeakCsv.WriteDays([day], Zone);

        StringAssert.Contains(Text(bytes), ",36.84,39.40,");
        Assert.AreEqual(Gb(36.84), PeakCsv.ReadDays(bytes, Zone)[0].CommitBytes);
    }

    [TestMethod]
    public void BootPeaksRoundTrip()
    {
        var boots = new[]
        {
            new BootPeak(Boot.AddDays(-1), Gb(39.0), Gb(39.4), null, null),
            new BootPeak(Boot, Gb(36.8), Gb(63.4), PeakTime, FiveApps),
        };

        var read = PeakCsv.ReadBoots(PeakCsv.WriteBoots(boots, Zone), Zone);

        Assert.HasCount(2, read);
        Assert.AreEqual(Boot.AddDays(-1), read[0].BootTimeUtc);
        Assert.AreEqual(DateTimeKind.Utc, read[0].BootTimeUtc.Kind);
        Assert.IsFalse(read[0].IsKnown);
        Assert.IsNull(read[0].TopApps);
        Assert.AreEqual(Gb(39.0), read[0].CommitBytes);
        Assert.AreEqual(Boot, read[1].BootTimeUtc);
        Assert.AreEqual(PeakTime, read[1].Time);
        Assert.AreEqual(TimeSpan.FromHours(8), read[1].Time!.Value.Offset);
        Assert.AreEqual(Gb(63.4), read[1].CommitLimitBytes);
        CollectionAssert.AreEqual(FiveApps, read[1].TopApps!.ToArray());
    }

    [TestMethod]
    public void DailyPeaksRoundTrip()
    {
        var days = new[]
        {
            new DailyPeak(new DateOnly(2026, 10, 25), Gb(30.5), Gb(39.4), PeakTime.AddDays(-1), FiveApps.Take(1).ToList()),
            new DailyPeak(new DateOnly(2026, 10, 26), Gb(31.4), Gb(39.4), PeakTime, FiveApps),
        };

        var read = PeakCsv.ReadDays(PeakCsv.WriteDays(days, Zone), Zone);

        Assert.HasCount(2, read);
        Assert.AreEqual(new DateOnly(2026, 10, 25), read[0].Date);
        Assert.AreEqual(Gb(30.5), read[0].CommitBytes);
        Assert.AreEqual(PeakTime.AddDays(-1), read[0].Time);
        CollectionAssert.AreEqual(FiveApps.Take(1).ToArray(), read[0].TopApps.ToArray());
        CollectionAssert.AreEqual(FiveApps, read[1].TopApps.ToArray());
    }

    [TestMethod]
    public void NamesWithCommasQuotesAndLineBreaksRoundTrip()
    {
        var apps = new[] { new PeakApp("Foo, \"Bar\" Helper", Gb(1.0)), new PeakApp("兩行\r\n名稱", Gb(0.5)) };
        var day = new DailyPeak(new DateOnly(2026, 10, 26), Gb(31.4), Gb(39.4), PeakTime, apps);

        byte[] bytes = PeakCsv.WriteDays([day], Zone);

        StringAssert.Contains(Text(bytes), ",\"Foo, \"\"Bar\"\" Helper\",\"兩行\r\n名稱\",");
        CollectionAssert.AreEqual(apps, PeakCsv.ReadDays(bytes, Zone)[0].TopApps.ToArray());
    }

    [TestMethod]
    public void HeaderOnlyIsEmpty()
    {
        Assert.IsEmpty(PeakCsv.ReadBoots(Utf8(BootHeader + "\r\n"), Zone));
        Assert.IsEmpty(PeakCsv.ReadDays(Utf8(DailyHeader), Zone));
    }

    [TestMethod]
    public void ReadsLfLineEndingsBlankLinesAndNoBom()
    {
        string text = DailyHeader + "\n\n2026-10-26,2026-10-26 14:35:02,31.40,39.40,Claude,,,,,3.20,,,,\n\n";

        var read = PeakCsv.ReadDays(Encoding.UTF8.GetBytes(text), Zone);

        Assert.HasCount(1, read);
        Assert.AreEqual("Claude", read[0].TopApps[0].Name);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("Date,PeakTime,CommitGB,LimitGB,1,2,3,4,5,1GB,2GB,3GB,4GB,5GB")]
    [DataRow("開機時間,尖峰時間,commit已用GB,commit上限GB,第1名,第2名,第3名,第4名,第5名,第1名GB,第2名GB,第3名GB,第4名GB,第5名GB")]
    public void RejectsAWrongHeader(string header)
    {
        Assert.ThrowsExactly<FormatException>(() => PeakCsv.ReadDays(Utf8(header + "\r\n"), Zone));
    }

    [TestMethod]
    public void RejectsBytesThatAreNotUtf8()
    {
        // Excel 另存成「CSV（逗號分隔）」時會改用 Big5：「開機」= B6 7D BE F7
        byte[] big5 = [0xB6, 0x7D, 0xBE, 0xF7, (byte)','];

        Assert.ThrowsExactly<FormatException>(() => PeakCsv.ReadBoots(big5, Zone));
    }

    [TestMethod]
    public void RejectsBytesThatAreNotUtf8InsideARow()
    {
        // 欄名正確但名稱裡有不是 UTF-8 的位元組：照樣讀進來會把亂碼寫回檔案
        byte[] row = Encoding.UTF8.GetBytes("2026-10-26,2026-10-26 14:35:02,31.40,39.40,");
        byte[] rest = Encoding.UTF8.GetBytes(",,,,,3.20,,,,\r\n");
        byte[] content = [.. Utf8(DailyHeader + "\r\n"), .. row, 0xB6, 0x7D, .. rest];

        Assert.ThrowsExactly<FormatException>(() => PeakCsv.ReadDays(content, Zone));
    }

    [TestMethod]
    public void RejectsATimeSkippedByDaylightSaving()
    {
        // 美國太平洋時間 2026-03-08 02:00 直接跳到 03:00，02:30 不存在
        var pacific = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
        byte[] content = Utf8(BootHeader + "\r\n" + "2026-03-08 02:30:00,未知,36.80,39.40,未知,,,,,,,,,\r\n");

        Assert.ThrowsExactly<FormatException>(() => PeakCsv.ReadBoots(content, pacific));
    }

    [TestMethod]
    [DataRow("2026/10/26 08:30,2026-10-26 14:35:02,36.80,39.40,Claude,,,,,3.20,,,,")]
    [DataRow("2026-10-26 08:30:00,2026-10-26 14:35:02,36.80,39.40,Claude,,,,,3.20,,,")]
    [DataRow("2026-10-26 08:30:00,2026-10-26 14:35:02,-1.00,39.40,Claude,,,,,3.20,,,,")]
    [DataRow("2026-10-26 08:30:00,2026-10-26 14:35:02,36.80,39.40,Claude,,,,,abc,,,,")]
    [DataRow("2026-10-26 08:30:00,2026-10-26 14:35:02,36.80,39.40,\"Claude,,,,,3.20,,,,")]
    public void RejectsMalformedBootRows(string row)
    {
        Assert.ThrowsExactly<FormatException>(() => PeakCsv.ReadBoots(Utf8(BootHeader + "\r\n" + row + "\r\n"), Zone));
    }

    [TestMethod]
    [DataRow("2026-10-26,未知,31.40,39.40,未知,,,,,,,,,")]
    [DataRow("2026/10/26,2026-10-26 14:35:02,31.40,39.40,Claude,,,,,3.20,,,,")]
    public void RejectsMalformedDailyRows(string row)
    {
        Assert.ThrowsExactly<FormatException>(() => PeakCsv.ReadDays(Utf8(DailyHeader + "\r\n" + row + "\r\n"), Zone));
    }

    // 紀錄檔被改壞時，換算成 UTC 會超出範圍的時間與大到不合理的 GB 數值也是格式不符，MemSweep 啟動時才不會當掉
    [TestMethod]
    [DataRow("0001-01-01 07:59:59,未知,36.80,39.40,未知,,,,,,,,,")]
    [DataRow("2026-10-26 08:30:00,0001-01-01 07:59:59,36.80,39.40,Claude,,,,,3.20,,,,")]
    [DataRow("2026-10-26 08:30:00,2026-10-26 14:35:02,1048577.00,39.40,Claude,,,,,3.20,,,,")]
    [DataRow("2026-10-26 08:30:00,2026-10-26 14:35:02,36.80,39.40,Claude,,,,,99999999999.00,,,,")]
    public void RejectsBootValuesOutsideTheSupportedRange(string row)
    {
        Assert.ThrowsExactly<FormatException>(() => PeakCsv.ReadBoots(Utf8(BootHeader + "\r\n" + row + "\r\n"), Zone));
    }

    [TestMethod]
    [DataRow("2026-10-26,0001-01-01 07:59:59,31.40,39.40,Claude,,,,,3.20,,,,")]
    [DataRow("2026-10-26,2026-10-26 14:35:02,31.40,99999999999.00,Claude,,,,,3.20,,,,")]
    public void RejectsDailyValuesOutsideTheSupportedRange(string row)
    {
        Assert.ThrowsExactly<FormatException>(() => PeakCsv.ReadDays(Utf8(DailyHeader + "\r\n" + row + "\r\n"), Zone));
    }

    [TestMethod]
    public void AcceptsTheLimitsOfTheSupportedRange()
    {
        // 台北時間 0001-01-01 08:00:00 正好是 UTC 的最早時間；GB 最大 1048576（1 PB）
        byte[] content = Utf8(BootHeader + "\r\n" + "0001-01-01 08:00:00,未知,1048576.00,39.40,未知,,,,,,,,,\r\n");

        var boot = PeakCsv.ReadBoots(content, Zone).Single();

        Assert.AreEqual(DateTime.MinValue, boot.BootTimeUtc);
        Assert.AreEqual(Gb(1048576), boot.CommitBytes);
    }
}
```

- [ ] **Step 2：執行測試，確認失敗**

執行：`(cd app && dotnet test)`

預期：總計 308，失敗 6：`RejectsBootValuesOutsideTheSupportedRange` 的 4 筆與 `RejectsDailyValuesOutsideTheSupportedRange` 的 2 筆（目前丟出的不是 `FormatException`，或根本沒有丟出）。

- [ ] **Step 3：實作**

`app/MemSweep.Core/PeakCsv.cs` 整份換成：

```csharp
using System.Globalization;
using System.Text;

namespace MemSweep.Core;

/// <summary>
/// 尖峰紀錄 CSV 的格式（F-34、Design 3.9）：含 BOM 的 UTF-8、CRLF 換行、中文欄名，
/// 記憶體以 GB 保留兩位小數，時間為本機時間 yyyy-MM-dd HH:mm:ss，未知時填「未知」。
/// </summary>
public static class PeakCsv
{
    public const string BootFileName = "peaks-boot.csv";
    public const string DailyFileName = "peaks-daily.csv";
    public const string Unknown = "未知";

    public const string BootHeader =
        "開機時間,尖峰時間,commit已用GB,commit上限GB,第1名,第2名,第3名,第4名,第5名,第1名GB,第2名GB,第3名GB,第4名GB,第5名GB";

    public const string DailyHeader =
        "日期,尖峰時間,commit已用GB,commit上限GB,第1名,第2名,第3名,第4名,第5名,第1名GB,第2名GB,第3名GB,第4名GB,第5名GB";

    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";
    private const string DateFormat = "yyyy-MM-dd";
    // 前五名的欄數與文字摘要的前五名相同
    private const int TopCount = ReportFormatter.TopCount;
    private const int ColumnCount = 4 + TopCount * 2;

    // 1 PB；更大的數值不會由 MemSweep 寫出，換算成 bytes 時也會溢位
    private const double MaxGigabytes = 1024 * 1024;

    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static byte[] WriteBoots(IEnumerable<BootPeak> boots, TimeZoneInfo zone) =>
        Encode(BootHeader, boots.Select(b => BootFields(b, zone)));

    public static byte[] WriteDays(IEnumerable<DailyPeak> days, TimeZoneInfo zone) =>
        Encode(DailyHeader, days.Select(d => DayFields(d, zone)));

    /// <summary>讀取 peaks-boot.csv；內容不符合格式時丟出 <see cref="FormatException"/>。</summary>
    public static List<BootPeak> ReadBoots(byte[] content, TimeZoneInfo zone) =>
        Decode(content, BootHeader).Select(fields => ParseBoot(fields, zone)).ToList();

    /// <summary>讀取 peaks-daily.csv；內容不符合格式時丟出 <see cref="FormatException"/>。</summary>
    public static List<DailyPeak> ReadDays(byte[] content, TimeZoneInfo zone) =>
        Decode(content, DailyHeader).Select(fields => ParseDay(fields, zone)).ToList();

    private static string[] BootFields(BootPeak boot, TimeZoneInfo zone)
    {
        string bootTime = TimeZoneInfo.ConvertTimeFromUtc(boot.BootTimeUtc, zone).ToString(TimeFormat, CultureInfo.InvariantCulture);
        string peakTime = boot.Time is { } time ? FormatTime(time, zone) : Unknown;
        return Row(bootTime, peakTime, boot.CommitBytes, boot.CommitLimitBytes, boot.TopApps);
    }

    private static string[] DayFields(DailyPeak day, TimeZoneInfo zone) =>
        Row(day.Date.ToString(DateFormat, CultureInfo.InvariantCulture), FormatTime(day.Time, zone), day.CommitBytes, day.CommitLimitBytes, day.TopApps);

    private static string[] Row(string first, string time, long commit, long limit, IReadOnlyList<PeakApp>? topApps)
    {
        var fields = new string[ColumnCount];
        Array.Fill(fields, string.Empty);
        fields[0] = first;
        fields[1] = time;
        fields[2] = Gigabytes(commit);
        fields[3] = Gigabytes(limit);
        if (topApps is null)
        {
            // 尖峰發生在 MemSweep 未執行期間，前五名無從得知（F-31）
            fields[4] = Unknown;
            return fields;
        }
        for (int i = 0; i < Math.Min(TopCount, topApps.Count); i++)
        {
            fields[4 + i] = topApps[i].Name;
            fields[4 + TopCount + i] = Gigabytes(topApps[i].CommitBytes);
        }
        return fields;
    }

    private static BootPeak ParseBoot(string[] fields, TimeZoneInfo zone)
    {
        var bootUtc = TimeZoneInfo.ConvertTimeToUtc(ParseLocal(fields[0], zone), zone);
        if (fields[1] == Unknown)
        {
            return new BootPeak(bootUtc, ParseGigabytes(fields[2]), ParseGigabytes(fields[3]), null, null);
        }
        return new BootPeak(bootUtc, ParseGigabytes(fields[2]), ParseGigabytes(fields[3]), ParseTime(fields[1], zone), ParseTopApps(fields));
    }

    private static DailyPeak ParseDay(string[] fields, TimeZoneInfo zone)
    {
        if (!DateOnly.TryParseExact(fields[0], DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new FormatException("日期格式不符：「" + fields[0] + "」");
        }
        return new DailyPeak(date, ParseGigabytes(fields[2]), ParseGigabytes(fields[3]), ParseTime(fields[1], zone), ParseTopApps(fields));
    }

    private static List<PeakApp> ParseTopApps(string[] fields)
    {
        var apps = new List<PeakApp>();
        for (int i = 0; i < TopCount && fields[4 + i].Length > 0; i++)
        {
            apps.Add(new PeakApp(fields[4 + i], ParseGigabytes(fields[4 + TopCount + i])));
        }
        return apps;
    }

    private static string FormatTime(DateTimeOffset time, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(time, zone).ToString(TimeFormat, CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTime(string text, TimeZoneInfo zone)
    {
        var local = ParseLocal(text, zone);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private static DateTime ParseLocal(string text, TimeZoneInfo zone)
    {
        // 日光節約時間跳過的時刻，與換算成 UTC 會超出範圍的時刻（例如台北時間 0001-01-01 凌晨），
        // 都不會由 MemSweep 寫出，出現時視為格式不符
        if (!DateTime.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            || zone.IsInvalidTime(value)
            || !IsWithinUtcRange(value, zone))
        {
            throw new FormatException("時間格式不符：「" + text + "」");
        }
        return value;
    }

    private static bool IsWithinUtcRange(DateTime local, TimeZoneInfo zone)
    {
        long utcTicks = local.Ticks - zone.GetUtcOffset(local).Ticks;
        return utcTicks >= DateTime.MinValue.Ticks && utcTicks <= DateTime.MaxValue.Ticks;
    }

    private static string Gigabytes(long bytes) =>
        MemoryMath.ToGigabytes(bytes).ToString("0.00", CultureInfo.InvariantCulture);

    private static long ParseGigabytes(string text) =>
        double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double value) && value <= MaxGigabytes
            ? MemoryMath.FromGigabytes(value)
            : throw new FormatException("GB 數值格式不符：「" + text + "」");

    private static byte[] Encode(string header, IEnumerable<string[]> rows)
    {
        var text = new StringBuilder(header).Append("\r\n");
        foreach (var fields in rows)
        {
            text.AppendJoin(',', fields.Select(Escape)).Append("\r\n");
        }
        return [.. Utf8WithBom.GetPreamble(), .. Utf8WithBom.GetBytes(text.ToString())];
    }

    private static string Escape(string field) =>
        field.AsSpan().IndexOfAny(",\"\r\n") < 0 ? field : "\"" + field.Replace("\"", "\"\"") + "\"";

    private static IEnumerable<string[]> Decode(byte[] content, string header)
    {
        string text;
        try
        {
            var span = content.AsSpan();
            var preamble = Utf8WithBom.Preamble;
            text = StrictUtf8.GetString(span.StartsWith(preamble) ? span[preamble.Length..] : span);
        }
        catch (DecoderFallbackException e)
        {
            throw new FormatException("不是 UTF-8 編碼", e);
        }

        var records = SplitRecords(text);
        if (records.Count == 0 || string.Join(',', records[0]) != header)
        {
            throw new FormatException("欄位名稱不符");
        }
        foreach (var fields in records.Skip(1))
        {
            if (fields.Length != ColumnCount)
            {
                throw new FormatException("欄位數不符：" + fields.Length);
            }
            yield return fields;
        }
    }

    /// <summary>依 RFC 4180 拆成記錄與欄位：雙引號內可以有逗號、換行與成對的雙引號；空白行略過。</summary>
    private static List<string[]> SplitRecords(string text)
    {
        var records = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        bool recordHasContent = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c != '"')
                {
                    field.Append(c);
                }
                else if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else
                {
                    quoted = false;
                }
                continue;
            }

            switch (c)
            {
                case '"':
                    quoted = true;
                    recordHasContent = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    recordHasContent = true;
                    break;
                case '\r' when i + 1 < text.Length && text[i + 1] == '\n':
                    break;
                case '\n':
                    EndRecord();
                    break;
                default:
                    field.Append(c);
                    recordHasContent = true;
                    break;
            }
        }
        if (quoted)
        {
            throw new FormatException("雙引號沒有成對");
        }
        EndRecord();
        return records;

        void EndRecord()
        {
            if (recordHasContent)
            {
                fields.Add(field.ToString());
                records.Add(fields.ToArray());
            }
            fields.Clear();
            field.Clear();
            recordHasContent = false;
        }
    }
}
```

`app/MemSweep.Desktop/TrayController.cs` 整份換成（記下第一次取樣前的「完整清單…」要求）：

```csharp
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Threading;
using MemSweep.Core;

namespace MemSweep.Desktop;

/// <summary>系統匣圖示、提示文字、右鍵選單、小面板、完整視窗與設定視窗（F-01～F-24、F-50）。必須在 UI 執行緒上使用。</summary>
internal sealed class TrayController : IDisposable
{
    private readonly SettingsService _settings;
    private readonly NotifyIcon _notifyIcon;
    private FlyoutWindow? _flyout;
    private FullWindow? _fullWindow;
    private FullWindowModel? _fullModel;
    private SettingsWindow? _settingsWindow;
    private bool _flyoutRequested;
    private bool _fullWindowRequested;
    private Icon? _icon;
    private string? _iconKey;
    private GroupedSnapshot? _latest;
    private PeakState? _latestPeaks;

    public TrayController(SettingsService settings)
    {
        _settings = settings;
        _settings.Changed += OnSettingsChanged;
        var menu = TrayMenu.Build(ShowFlyout, ShowFullWindow, ShowSettings, () => ExitRequested?.Invoke());
        _notifyIcon = new NotifyIcon { Text = "MemSweep", ContextMenuStrip = menu };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ToggleFlyout();
            }
        };
    }

    public event Action? ExitRequested;

    public void ShowFlyout()
    {
        // 還沒有第一次取樣的結果時沒有內容可顯示，先記下來，取樣完成後再顯示
        if (_latest is null)
        {
            _flyoutRequested = true;
            return;
        }
        var flyout = EnsureFlyout();
        flyout.PanelView.DataContext = BuildPanel();
        flyout.ShowPanel();
    }

    public void Update(GroupedSnapshot grouped, PeakState peaks)
    {
        var snapshot = grouped.Snapshot;
        string text = TrayText.IconText(snapshot);
        var level = _settings.Current.Thresholds.LevelOf(TrayText.CommitPercent(snapshot));
        int size = SystemInformation.SmallIconSize.Width;

        // 數字、顏色與尺寸都沒變就不重畫
        string key = $"{text}|{level}|{size}";
        if (key != _iconKey)
        {
            var previous = _icon;
            _icon = TrayIconRenderer.Render(text, level, size);
            _iconKey = key;
            _notifyIcon.Icon = _icon;
            previous?.Dispose();
        }

        _notifyIcon.Text = TrayText.Tooltip(snapshot);
        _notifyIcon.Visible = true;
        _latest = grouped;
        _latestPeaks = peaks;
        if (_flyoutRequested)
        {
            _flyoutRequested = false;
            ShowFlyout();
        }
        else if (_flyout is { IsVisible: true })
        {
            _flyout.PanelView.DataContext = BuildPanel();
        }
        if (_fullWindowRequested)
        {
            _fullWindowRequested = false;
            ShowFullWindow();
        }
        else if (_fullWindow is { IsVisible: true })
        {
            _fullModel!.Update(grouped, peaks);
        }
    }

    public void ShowFullWindow()
    {
        // 和小面板一樣，還沒有第一次取樣的結果時先記下來，取樣完成後再開啟
        if (_latest is null)
        {
            _fullWindowRequested = true;
            return;
        }
        _flyout?.HidePanel();
        var window = EnsureFullWindow();
        _fullModel!.Update(_latest, _latestPeaks!);
        window.ShowWindow();
    }

    /// <summary>開啟設定視窗；已經開著時帶到前面。</summary>
    public void ShowSettings()
    {
        _flyout?.HidePanel();
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow { DataContext = new SettingsForm(_settings.Current), Save = _settings.Save };
            _settingsWindow.Closed += OnSettingsWindowClosed;
        }
        _settingsWindow.ShowWindow();
    }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        if (_fullWindow is { } window)
        {
            window.Closed -= OnFullWindowClosed;
            window.Close();
        }
        if (_settingsWindow is { } settingsWindow)
        {
            settingsWindow.Closed -= OnSettingsWindowClosed;
            settingsWindow.Close();
        }
        _flyout?.Close();
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _icon?.Dispose();
    }

    private PanelModel BuildPanel() =>
        PanelModel.Build(_latest!, _latestPeaks?.BootPeak, _settings.Current.Thresholds, _latestPeaks?.StorageWarning);

    // 第一次開啟時才建立，之後只顯示／隱藏：只用系統匣時，行程不必載入 WPF 視窗（約省 40 MB）
    private FlyoutWindow EnsureFlyout()
    {
        if (_flyout is null)
        {
            _flyout = new FlyoutWindow();
            _flyout.PanelView.FullListClicked += ShowFullWindow;
            _flyout.PanelView.CopyClicked += CopyReport;
            _flyout.PanelView.ExitClicked += () => ExitRequested?.Invoke();
        }
        return _flyout;
    }

    // 第一次開啟時才建立；展開狀態保存在 FullWindowModel，關閉後再開仍在
    private FullWindow EnsureFullWindow()
    {
        if (_fullWindow is null)
        {
            _fullModel ??= new FullWindowModel(_settings.Current.Thresholds, TimeZoneInfo.Local);
            _fullWindow = new FullWindow { DataContext = _fullModel };
            _fullWindow.Closed += OnFullWindowClosed;
        }
        return _fullWindow;
    }

    private void OnFullWindowClosed(object? sender, EventArgs e)
    {
        _fullWindow = null;
        ReleaseMemoryWhenIdle();
    }

    private void OnSettingsWindowClosed(object? sender, EventArgs e)
    {
        _settingsWindow = null;
        ReleaseMemoryWhenIdle();
    }

    // 隱藏的視窗會保留整個畫面樹，實測讓 MemSweep 超過 100 MB（Design 3.6），所以視窗關閉時真正釋放：
    // 等 WPF 處理完關閉的工作後，請 GC 回收並把空出來的記憶體還給系統。只回收 MemSweep 自己的記憶體。
    private static void ReleaseMemoryWhenIdle() =>
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true)));

    // 圖示顏色、小面板與完整視窗立即換成新的門檻，不等下一次取樣
    private void OnSettingsChanged(AppSettings settings)
    {
        if (_fullModel is not null)
        {
            _fullModel.Thresholds = settings.Thresholds;
        }
        if (_latest is not null)
        {
            Update(_latest, _latestPeaks!);
        }
    }

    private void ToggleFlyout()
    {
        if (_flyout is { IsVisible: true })
        {
            _flyout.HidePanel();
        }
        else if (_flyout is null || !_flyout.WasJustHidden)
        {
            ShowFlyout();
        }
    }

    private void CopyReport()
    {
        if (_latest is null || _flyout is null)
        {
            return;
        }

        bool succeeded;
        try
        {
            System.Windows.Clipboard.SetText(ReportFormatter.ToText(_latest));
            succeeded = true;
        }
        catch (ExternalException)
        {
            // 剪貼簿被其他程式佔用
            succeeded = false;
        }
        _flyout.PanelView.ShowCopyResult(succeeded);
    }
}
```

- [ ] **Step 4：執行測試，確認通過**

執行：`(cd app && dotnet test)`

預期：總計 308，失敗 0。

- [ ] **Step 5：Commit**

```bash
git add app/MemSweep.Core/PeakCsv.cs app/MemSweep.Desktop/TrayController.cs app/MemSweep.Core.Tests/PeakCsvTests.cs
git status --short
git commit -F - <<'EOF'
紀錄檔超出範圍的數值視為格式不符，並記下第一次取樣前的完整清單要求

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

- [ ] **Step 6：推送**（依「共同執行規則」先 fetch 並檢查）

---

### Task 7：發佈指令與使用說明

**檔案：**

- 修改（整份換成）：`README.md`

**實作的條文：**

> - E-2 需先安裝 .NET 10 Desktop Runtime。〔Claude 決定〕
> - E-3 免安裝：兩支 exe 放在使用者有寫入權限的資料夾就能執行。不可放在 `C:\Program Files` 等需要系統管理員權限才能寫入的位置。
> - 兩支 exe、共用的 `MemSweep.Core.dll` 與除錯符號（pdb）都在 `app/artifacts/publish/MemSweep/`，整個資料夾複製到使用者有寫入權限的位置就能使用（E-3）。不做安裝程式（N-6），也不做單一檔案發佈：兩支 exe 共用同一份 Core。
> - README 說明需求、建置與發佈、安裝、使用、命令列、資料檔與移除步驟。移除前要先關閉開機自動啟動，否則 Run 機碼會留下指向已刪除 exe 的值。

**介面：** 無。

- [ ] **Step 1：寫 README**

`README.md` 整份換成：

````markdown
# MemSweep

唯讀的 Windows commit 監看工具：系統匣顯示 commit 使用率、佔用 commit 的應用程式與 commit 尖峰，另附命令列工具 `memsweep-cli`，讓代理在吃記憶體的工作前先檢查剩餘 commit。

MemSweep 只讀取系統資訊：不清理記憶體、不結束任何程式，也不需要系統管理員權限。需求見 [docs/spec.md](docs/spec.md)，設計見 [docs/design.md](docs/design.md)。

## 需求

- Windows 11 x64
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)（x64）

## 建置與發佈

需要 .NET 10 SDK。在 repo 根目錄執行：

```bash
(cd app && dotnet publish MemSweep.Desktop -c Release -r win-x64 --self-contained false -o artifacts/publish/MemSweep)
(cd app && dotnet publish MemSweep.Cli -c Release -r win-x64 --self-contained false -o artifacts/publish/MemSweep)
```

`MemSweep.exe`、`memsweep-cli.exe` 與共用的 DLL 都會放在 `app/artifacts/publish/MemSweep/`。測試：`(cd app && dotnet test)`。

## 安裝

把 `MemSweep` 資料夾整個複製到自己有寫入權限的位置，例如 `D:\Tools\MemSweep`。不要放在 `C:\Program Files` 這類需要系統管理員權限才能寫入的位置：設定與尖峰紀錄會寫在 exe 旁邊的 `data\` 資料夾。

## 使用

執行 `MemSweep.exe` 後，MemSweep 常駐在系統匣，直到選「結束 MemSweep」。關閉任何視窗都不會結束它；再執行一次 `MemSweep.exe` 會叫出小面板。

- **系統匣圖示**：目前的 commit 使用率（%）。低於橘色門檻為綠色，達到橘色門檻為橘色，達到紅色門檻以上為紅色；預設橘色 70%、紅色 85%。滑鼠停在圖示上會顯示 commit 與實體 RAM 的用量。
- **左鍵點圖示**：開關小面板，顯示 commit 與 RAM 量表、開機以來的尖峰、commit 前五名的應用程式與「系統（不屬於任何程式）」。「複製報告」會把與 `memsweep-cli status` 相同的摘要複製到剪貼簿。
- **右鍵選單**：開啟小面板、完整清單…、設定…、結束 MemSweep。
- **完整清單**：「總覽」「程式」「尖峰紀錄」三個分頁。「程式」列出所有應用程式，可展開看組成的行程；「尖峰紀錄」列出開機尖峰與每日尖峰。
- **設定**：橘色與紅色門檻（1～100，橘色必須小於紅色）、尖峰紀錄保留天數（1～3650，預設 90）、開機時自動啟動（預設關閉）。減少保留天數後，超過天數的紀錄會在下一次取樣時刪除。

## 命令列

`memsweep-cli` 不需要 `MemSweep.exe` 在執行，直接向系統查詢，也不讀寫 `data\`。

| 指令 | 說明 |
| --- | --- |
| `memsweep-cli status` | 文字摘要：commit、RAM、開機以來尖峰、前五名與「系統（不屬於任何程式）」 |
| `memsweep-cli status --json` | 相同內容的 JSON，記憶體以 bytes 整數表示 |
| `memsweep-cli check --need <GB>` | 剩餘 commit 是否足夠，`<GB>` 可帶小數，1 GB = 1024³ bytes |
| `memsweep-cli --help` | 用法說明 |

| 結束碼 | 意義 |
| --- | --- |
| 0 | 成功；`check` 時剩餘 commit 足夠 |
| 1 | `check` 時剩餘 commit 不足，並輸出剩餘量與前五名 |
| 2 | 參數錯誤 |
| 3 | 無法取得系統記憶體資訊 |

例如開始吃記憶體的分析前，先確認至少還有 8 GB：

```bash
memsweep-cli check --need 8
```

## 資料檔

`MemSweep.exe` 所在資料夾下的 `data\`，`MemSweep.exe` 啟動時自動建立：

| 檔案 | 內容 |
| --- | --- |
| `settings.json` | 設定；內容不合法時使用預設值，下次儲存時覆寫 |
| `peaks-boot.csv` | 每次開機一筆的 commit 尖峰 |
| `peaks-daily.csv` | 每天一筆的 commit 尖峰 |

CSV 是含 BOM 的 UTF-8，可以直接用 Excel 開啟。MemSweep 執行中用 Excel 開著紀錄檔也沒關係，寫入失敗時會稍後再試。

## 移除

1. 如果開啟過開機自動啟動，先在「設定…」取消勾選並儲存。
2. 從系統匣選「結束 MemSweep」。
3. 刪除整個資料夾。

MemSweep 不會寫入其他位置；開機自動啟動只登記在 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 的 `MemSweep` 值。
````

- [ ] **Step 2：確認發佈指令**

執行：

```bash
(cd app && dotnet publish MemSweep.Desktop -c Release -r win-x64 --self-contained false -o artifacts/publish/MemSweep)
(cd app && dotnet publish MemSweep.Cli -c Release -r win-x64 --self-contained false -o artifacts/publish/MemSweep)
ls app/artifacts/publish/MemSweep
app/artifacts/publish/MemSweep/memsweep-cli.exe status
```

預期：資料夾裡有 `MemSweep.exe`、`memsweep-cli.exe`、`MemSweep.dll`、`memsweep-cli.dll`、`MemSweep.Core.dll`、兩份 `.deps.json`、兩份 `.runtimeconfig.json` 與三個 `.pdb`；`memsweep-cli.exe status` 印出文字摘要，結束碼 0。這一步只讀取系統資訊。

- [ ] **Step 3：Commit**

```bash
git add README.md
git status --short
git commit -F - <<'EOF'
README 補上發佈與使用說明

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

- [ ] **Step 4：推送**（依「共同執行規則」先 fetch 並檢查）

---

## Phase 收尾

- [ ] **完整檢查**：`(cd app && dotnet build -c Release)` 為 0 個警告、0 個錯誤；`(cd app && dotnet test)` 總計 308，失敗 0。
- [ ] **A-03 量測**：以發佈資料夾的 `MemSweep.exe` 量測。執行後再執行一次同一個 exe 叫出小面板，按「完整清單…」，三個分頁都切過、展開兩三列後關閉；從右鍵選單開「設定…」，改一個門檻後儲存，再開一次按「取消」。之後放著不動 60 分鐘，以 `(Get-Process MemSweep).PrivateMemorySize64` 量測，必須不超過 100 MB。開始前提醒使用者不要按「結束 MemSweep」，量完用小面板的「結束 MemSweep」結束，並刪除量測時寫入發佈資料夾的 `data\`。
- [ ] **自動化與實機檢查**：A-04（`check --need` 的三種結束碼，含發佈資料夾的 `memsweep-cli.exe check --need 1`、`--need 99999`、`--need abc`）、A-06（以一般使用者權限執行，行程沒有提升權限）、A-07（`NativeSafetyTests` 通過）、A-09（完整視窗「程式」分頁符合五個範例中當時能觀察到的部分）。
- [ ] **使用者驗收 A-01、A-02**：同一時間對照工作管理員「效能 > 記憶體」的「已認可」與「詳細資料」頁的「認可大小」。
- [ ] **使用者驗收 A-05、A-08**：以 Excel 直接開啟 `data\` 的兩份 CSV；關閉小面板、完整視窗與設定視窗後 MemSweep 仍在系統匣，只有「結束 MemSweep」會結束程式。
- [ ] **使用者確認設定視窗**：淺色與深色都清楚；標題列只有關閉按鈕、可拖曳；Enter 儲存、Esc 取消；輸入不合法時顯示原因；改門檻後系統匣圖示的顏色立即改變。
- [ ] **使用者確認開機自動啟動（需使用者同意才實際切換）**：在設定視窗勾選並儲存後，`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 出現 `MemSweep` 值、內容是加上引號的 exe 路徑；登出再登入時 MemSweep 自動啟動；取消勾選並儲存後該值被刪除。
- [ ] **開 PR**：所有 Task 都推送後，用 `/repo-open-pr` 開 PR（base `main`，head `phase-04-settings-release`）。合併由使用者決定，使用 merge commit。
