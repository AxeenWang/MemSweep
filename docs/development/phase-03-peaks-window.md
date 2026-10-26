# MemSweep Phase 03：尖峰紀錄與完整視窗 實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**目標：** 把開機尖峰與每日尖峰寫進 `data\` 的 CSV，MemSweep 重新啟動時接續既有紀錄；新增完整視窗（總覽、程式、尖峰紀錄三個分頁）與右鍵選單、小面板的「完整清單…」；兩支 exe 換上專屬的程式圖示，完整視窗改用自訂標題列（F-62）；並併入 PR #2 延後的四項小面板小修。

**架構：** 與介面無關的邏輯都放在 MemSweep.Core，可以直接單元測試：`PeakTracker` 追蹤並保存全部尖峰紀錄、做保留天數清理；`PeakCsv` 是 CSV 格式；`PeakStore` 讀寫 `data\` 的檔案；`PeakRecorder` 負責寫入節流，取樣執行緒與結束時的 UI 執行緒共用；`FullWindowModel` 是完整視窗的顯示資料，程式清單原地更新。MemSweep.Desktop 只放介面：`SummaryView`（小面板與「總覽」共用）、`AppsView`、`PeaksView`、`FullWindow`，由 `TrayController` 管理；`FullWindow` 以 `WindowChrome` 搭配 `TitleBar` 取代系統標題列，程式圖示是 `Assets/MemSweep.ico`。

**技術：** C#、.NET 10、WPF（軟體繪圖）、WinForms `NotifyIcon`、`LibraryImport`、MSTest 4.5.1（含 `[STATestMethod]`）。

**依據：** [SPEC](../spec.md)、[Design](../design.md)（含隨本計畫提交的修訂）。本計畫引用的條文都是原文；計畫自己的解讀集中在「計畫的解讀」一節。

**分支：** `phase-03-peaks-window`（開工時從 `origin/main` 建立）。

## 全域限制

以下條文適用於每個 Task：

> - Identifiers are English: code symbols, file and folder names, and branch names.
> - Documents, code comments, and commit messages are written in Traditional Chinese (Taiwan).
> - Commit subjects are Traditional Chinese only, with no type prefix such as `feat:` or `fix:`.
> - Push after each completed Task. Open the pull request only after every Task in the Phase is done.
> - Running anything that changes system memory state requires the user's explicit approval for that run.
> - Never terminate the user's processes to free memory.
> - Run every `dotnet` command inside `app/`, because `app/global.json` pins the SDK, the MSTest.Sdk version, and the test runner. For example: `(cd app && dotnet build)`.
> - E-4 以一般使用者權限即可使用全部功能。
> - X-1 MemSweep 本身成為記憶體大戶。常駐時本身的 commit 目標不超過 100 MB。〔Claude 決定〕

## Review Focus

SPEC 沒有直接寫到、但實際使用最可能出問題的情況。每一項都已在負責的 Task 裡加上測試：

1. 使用者用 Excel 開著紀錄檔（A-05 的情境）：MemSweep 照樣讀得到；寫入失敗時保留變動、60 秒後再試，不當掉也不停用。Task 3 `ReadsWhileAnotherProgramHasTheFileOpen`、`SaveFailsWhileAnotherProgramHasTheFileOpenAndSucceedsLater`，Task 4 `AFailedWriteIsRetriedAtTheNextInterval`。
2. 使用者在 Excel 改過紀錄檔後另存，編碼變成 Big5 或時間格式被改：不覆寫使用者的檔案，停用尖峰紀錄並在小面板說明原因。Task 2 `RejectsBytesThatAreNotUtf8`、`RejectsMalformedBootRows`，Task 3 `AnUnreadableFileDisablesTheStoreAndIsLeftAlone`。
3. 同一次開機內重新啟動 MemSweep：開機尖峰沿用已知的時間與前五名，不會因為 CSV 只記到秒與 0.01 GB 而變成「未知」或多出一筆；同一天的每日尖峰也接續。Task 1 `RestartWithinTheSameBootKeepsTheSavedPeak`、`RestartOnTheSameDayContinuesTodaysPeak`。
4. 快速啟動讓一次開機跨好幾個月：本次開機的紀錄不會被保留天數清掉。Task 1 `PruneNeverRemovesThisBootOrToday`。
5. 完整視窗開著好幾個小時、清單每 2 秒更新：展開的列不會收回，列是原地更新而不是整份重建（X-1）。Task 6 `RowsAreUpdatedInPlaceAndKeepTheirExpansion`、`ReorderedAppsAreMovedNotRecreated`。

## 計畫的解讀

以下是本計畫自己的決定，不是 SPEC 或 Design 的原文：

- 每日尖峰只看取樣到的 commit 已用（F-32），所以時間與前五名一定已知；開機尖峰才會有「未知」。
- 開機紀錄以開機時間比對，相差 60 秒以內視為同一次開機：CSV 只記到秒，Windows 校時也會讓開機時間略為偏移。只在第一次取樣時比對，之後一直沿用同一筆；尖峰上升時整筆換成本次取樣的值，開機時間也跟著更新，下次重新啟動比對得更準。
- 重新啟動時，Windows 的尖峰比已有紀錄高出不到 0.005 GB（CSV 兩位小數的一半）視為同一個尖峰：沿用時間與前五名，數值改用較高的精確值，之後的取樣才比較得準。
- 保留天數 N 天是「今天在內的 N 天」：預設 90 天時，2026-10-26 保留到 2026-07-29。開機紀錄以尖峰時間的日期判斷，時間未知時用開機時間的本機日期；本次開機與今天的紀錄一律保留。
- 保留天數清理在啟動後的第一次取樣時執行（那時才知道哪一筆是本次開機），之後在跨日時執行。
- 寫入節流以取樣時間計算：第一次取樣立即寫入，之後有變動時距離上次寫入滿 60 秒才寫；時鐘往回調時以時間差的絕對值判斷。
- 寫入失敗（例如 Excel 開著檔案）時不顯示警告，保留變動，下一個寫入時機再試。實測檔案被鎖住時，取代檔案丟出的是 `UnauthorizedAccessException`，所以和 `IOException` 都要接住。
- 停用尖峰紀錄的兩種情況：資料夾無法建立或無法寫入時顯示 Design 3.9 的「data 資料夾無法寫入，尖峰紀錄已停用」；既有的紀錄檔無法讀取或格式不符時顯示「peaks-boot.csv 無法讀取，尖峰紀錄已停用」（或 `peaks-daily.csv`），而且不覆寫該檔。停用時仍在記憶體中追蹤，小面板與完整視窗照常顯示本次執行期間的尖峰。
- CSV 細節：開機尖峰未知時「尖峰時間」與「第1名」填「未知」，其他名次與 GB 欄留空；少於五名時其餘欄位留空；名稱含逗號、雙引號或換行時依 RFC 4180 加雙引號。讀取時接受沒有 BOM、LF 換行與空白行；欄名、欄數、時間格式或 GB 數值不符，整個檔案視為無法讀取。
- 「程式」分頁用 `ItemsControl` 搭配原地更新的 `AppListRow`（Design 3.6 原本留給本計畫決定）：以名稱對應同一列，同名的應用程式（R-5 只合併相同路徑，不同路徑可能有相同的檔案描述）依出現順序對應；明細名稱寫成「claude.exe ×14」；「系統（不屬於任何程式）」一列沒有行程數、實際 RAM 與明細。
- 「尖峰紀錄」分頁新的在前：開機尖峰顯示開機時間與尖峰時間（Design 3.10「介面一律顯示開機時間」），每日尖峰顯示日期與時間；沒有紀錄時顯示「尚無紀錄」。
- 完整視窗第一次開啟時才建立，只在顯示時更新；關閉時真正關閉，等 UI 空閒時以 `GCCollectionMode.Aggressive` 回收（使用者選定的對策，Design 3.6 隨本計畫修訂）。原型實測：開著時約 95～105 MB，原本的「關閉時改為隱藏」隱藏後仍約 109 MB，關閉並回收後穩定在約 86 MB。這只回收 MemSweep 自己的記憶體，不是 AGENTS.md「System Safety」所說改變系統記憶體狀態的動作。展開狀態保存在 `FullWindowModel`，關閉再開仍在。
- 併入 PR #2 延後的小修：小面板繫結改為 `Mode=OneTime`（Task 7）；第一次取樣完成前收到的「顯示小面板」要求記下來，取樣完成後顯示（Task 7）；取樣用的 `CancellationTokenSource` 在結束時 Dispose（Task 5）；Esc 設定 `e.Handled`，以及小面板沒有取得焦點時改用前景視窗判斷是否收起（Task 9）。
- 程式圖示與自訂標題列（F-62）是使用者在計畫審閱期間提出的需求，樣式交由 Claude 決定：圖示畫成 commit 量表，與系統匣的門檻顏色一致；標題列用 WPF 內建的 `WindowChrome`，不引入第三方套件。圖示是二進位檔，計畫以 base64 附上並用 SHA-256 確認；它由 `.camp_claude` 裡的拋棄式產生器以 WPF 繪製，產生器不進版控，之後要改圖示時重新繪製即可。
- 最大化時要往內縮多少是實測決定的：原型探針中，以 WPF 的 `SystemParameters.WindowNonClientFrameThickness`（150% 時 3.33）內縮，標題列仍超出螢幕 6 px；改用 `SM_CXSIZEFRAME` + `SM_CXPADDEDBORDER`（5 + 6 px，換算為 7.33）後，標題列左上角與關閉按鈕右緣正好落在工作區的角落。
- 離線繪製的完整視窗預覽中發現三個外觀問題，一併在 Task 8 修正：深色主題下捲軸仍是系統的淺色（改為跟著主題的細捲軸）、最右欄太貼捲軸（右邊留 8）、「總覽」的寬度跟著內容縮到約 290（固定 480）。
- `TitleBar` 依 DPI 挑選圖示的那一格在 `Loaded` 時設定，單元測試不顯示視窗、觸發不到，所以只測挑選規則 `AppIcon.PickSize`，實際外觀由「Phase 收尾」的使用者確認。
- 小面板沒有取得焦點的情況只在使用者正在操作時出現：原型探針中，使用者一段時間沒有操作（Windows 的前景鎖定已逾時）時，由背景啟動的第二個執行個體仍讓小面板拿到焦點。所以 Task 9 的行為由「Phase 收尾」的手動確認驗證，單元測試只涵蓋判斷函式。
- 多個 STA 執行緒同時初始化 WPF 介面元件時會互相鎖死（原型中 10 個介面測試平行執行時，6 次有 5 次卡住；依序執行後連續 6 次都正常），所以介面測試類別標記 `[DoNotParallelize]`；圖示洩漏測試計算整個測試行程的 USER 物件，也改為不與其他測試並行。

## 共同執行規則

- 工作目錄是 repo 根目錄 `D:\AxeenWorld\CodeLab\Projects\MemSweep`，分支 `phase-03-peaks-window`。
- `dotnet` 指令一律在 `app/` 底下執行，用子殼層避免改變目前目錄，例如 `(cd app && dotnet test)`。
- 程式碼區塊要逐字照抄，包括全形符號：`：`、`（`、`）`、`「`、`」`、`、`、`…`、`×`、`▸`、`▾`。XAML 的屬性順序與縮排也照抄。不要刪除或移動步驟沒有提到的任何一行。
- 測試就是規格：不得修改或刪除測試裡的任何斷言。照抄後測試仍失敗，就回報 BLOCKED。
- 測試會在測試輸出資料夾（`app/artifacts/bin/MemSweep.Core.Tests/`）底下建立暫時的 `PeakStoreTests\` 資料夾並自行刪除。`[STATestMethod]` 的測試會在 STA 執行緒建立 WPF 控制項，但不顯示視窗。即時測試全部是唯讀查詢。不得執行任何會改變系統記憶體狀態的動作，也不得結束使用者的行程。
- 需要的暫存檔放在 `.camp_claude/` 的任務資料夾。commit 訊息用 Bash 的 heredoc 傳給 `git commit -F -`，不要用 PowerShell 寫訊息檔。
- commit 訊息與 `Co-Authored-By` 尾行照計畫原樣使用。
- 每個 Task 完成後推送。推送前先 `git fetch origin`，並確認遠端分支仍存在、沒有已合併或已關閉的 PR：

  ```bash
  git fetch origin
  git ls-remote --heads origin phase-03-peaks-window
  gh pr list --head phase-03-peaks-window --state all
  git push
  ```

  Task 1 是第一次推送，遠端還沒有這個分支，改用 `git push -u origin phase-03-peaks-window`。

---

### Task 1：每日尖峰與接續既有紀錄

**檔案：**

- 新增：`app/MemSweep.Core/DailyPeak.cs`、`app/MemSweep.Core/PeakHistory.cs`
- 修改（整份換成）：`app/MemSweep.Core/PeakTracker.cs`、`app/MemSweep.Core.Tests/PeakTrackerTests.cs`

**實作的條文：**

> - F-30 MemSweep 常駐期間每次取樣時，如果 commit 已用創下本次開機或當天的新高，就更新對應的紀錄，並記下那一刻的前五名應用程式（名稱與 commit）。
> - F-31 開機尖峰的數值以 Windows 記錄的開機以來尖峰為準。尖峰如果發生在 MemSweep 未執行的期間，該筆的發生時間與前五名標示為「未知」。〔Claude 決定〕
> - F-32 每日尖峰以本機時間午夜分日，只計入 MemSweep 執行期間的取樣。
> - F-33 每次開機一筆、每天一筆。保留天數可設定，預設 90 天，超過的紀錄自動刪除。〔預設值：Claude 決定〕

> - **開機尖峰**：以開機時間識別。每次取樣讀取 Windows 的開機以來尖峰：
>   - 數值比目前紀錄高，代表尖峰發生在上次取樣之後（最多 2 秒前），就以本次取樣的時間與前五名作為該筆紀錄。這是近似值。
>   - MemSweep 啟動時，如果 Windows 的尖峰高於這次開機已有的紀錄（或還沒有紀錄），代表尖峰發生在 MemSweep 未執行期間，發生時間與前五名都記為「未知」（F-31）。
> - **每日尖峰**：只看取樣到的 commit 已用；跨過本機午夜就開新的一筆（F-32）。
> - 保留天數清理：啟動時與跨日時執行，刪除尖峰時間（或日期）早於保留天數的列（F-33）。

**介面：**

- 使用：`BootPeak`、`PeakApp`（Phase 02）、`GroupedSnapshot`、`ReportFormatter.TopCount`。
- 產生：
  - `DailyPeak(DateOnly Date, long CommitBytes, long CommitLimitBytes, DateTimeOffset Time, IReadOnlyList<PeakApp> TopApps)`
  - `PeakHistory(IReadOnlyList<BootPeak> Boots, IReadOnlyList<DailyPeak> Days)`、`PeakHistory.Empty`
  - `PeakTracker()`、`PeakTracker(PeakHistory saved)`；屬性 `BootPeak`、`TodayPeak`、`History`；`bool Update(GroupedSnapshot grouped)`；`bool Prune(DateTimeOffset now, int retentionDays)`；`PeakTracker.BootTimeTolerance`

- [ ] **Step 1：寫失敗的測試**

`app/MemSweep.Core.Tests/PeakTrackerTests.cs` 整份換成：

```csharp
using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class PeakTrackerTests
{
    private static readonly DateTime Boot = new(2026, 10, 26, 0, 30, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Taipei = TimeSpan.FromHours(8);

    private static long Gb(double value) => MemoryMath.FromGigabytes(value);

    private static DateTimeOffset At(int month, int day, int hour, int minute, int second = 0) =>
        new(2026, month, day, hour, minute, second, Taipei);

    private static GroupedSnapshot Sample(double usedGb, double peakGb, DateTimeOffset timestamp, DateTime? bootUtc = null)
    {
        var typical = ReportSamples.Typical();
        var snapshot = typical.Snapshot with
        {
            CommitUsedBytes = Gb(usedGb),
            CommitPeakBytes = Gb(peakGb),
            Timestamp = timestamp,
            BootTimeUtc = bootUtc ?? Boot,
        };
        return typical with { Snapshot = snapshot };
    }

    private static BootPeak SavedBoot(DateTime bootUtc, double peakGb, DateTimeOffset? time) =>
        new(bootUtc, Gb(peakGb), Gb(39.4), time, time is null ? null : [new PeakApp("Steam", Gb(1.0))]);

    private static DailyPeak SavedDay(DateOnly date, double usedGb, DateTimeOffset time) =>
        new(date, Gb(usedGb), Gb(39.4), time, [new PeakApp("Steam", Gb(1.0))]);

    [TestMethod]
    public void NoPeaksBeforeTheFirstSample()
    {
        var tracker = new PeakTracker();

        Assert.IsNull(tracker.BootPeak);
        Assert.IsNull(tracker.TodayPeak);
        Assert.IsEmpty(tracker.History.Boots);
        Assert.IsEmpty(tracker.History.Days);
    }

    [TestMethod]
    public void FirstSampleBelowTheWindowsPeakIsUnknown()
    {
        var tracker = new PeakTracker();

        bool changed = tracker.Update(Sample(31.4, 36.8, ReportSamples.Timestamp));

        Assert.IsTrue(changed);
        var peak = tracker.BootPeak!;
        Assert.AreEqual(Gb(36.8), peak.CommitBytes);
        Assert.AreEqual(Gb(39.4), peak.CommitLimitBytes);
        Assert.AreEqual(Boot, peak.BootTimeUtc);
        Assert.IsFalse(peak.IsKnown);
        Assert.IsNull(peak.Time);
        Assert.IsNull(peak.TopApps);
        CollectionAssert.AreEqual(new[] { peak }, tracker.History.Boots.ToArray());
    }

    [TestMethod]
    public void FirstSampleAtTheWindowsPeakIsKnown()
    {
        var tracker = new PeakTracker();

        tracker.Update(Sample(36.8, 36.8, ReportSamples.Timestamp));

        var peak = tracker.BootPeak!;
        Assert.IsTrue(peak.IsKnown);
        Assert.AreEqual(ReportSamples.Timestamp, peak.Time);
        CollectionAssert.AreEqual(
            new[] { "Claude", "ChatGPT", "Microsoft Edge WebView2", AppGrouper.WindowsGroupName, "Node.js JavaScript Runtime" },
            peak.TopApps!.Select(a => a.Name).ToArray());
        Assert.AreEqual(Gb(3.2), peak.TopApps![0].CommitBytes);
    }

    [TestMethod]
    public void RisingWindowsPeakRecordsThisSampleAsTheApproximatePeak()
    {
        var tracker = new PeakTracker();
        tracker.Update(Sample(31.4, 36.8, ReportSamples.Timestamp));
        var later = ReportSamples.Timestamp.AddSeconds(2);

        bool changed = tracker.Update(Sample(37.9, 38.0, later));

        Assert.IsTrue(changed);
        var peak = tracker.BootPeak!;
        Assert.AreEqual(Gb(38.0), peak.CommitBytes);
        Assert.AreEqual(later, peak.Time);
        Assert.HasCount(5, peak.TopApps!);
        Assert.HasCount(1, tracker.History.Boots);
    }

    [TestMethod]
    public void UnchangedPeaksAreNotAnUpdate()
    {
        var tracker = new PeakTracker();
        tracker.Update(Sample(31.4, 36.8, ReportSamples.Timestamp));
        var before = tracker.History;

        bool changed = tracker.Update(Sample(30.0, 36.8, ReportSamples.Timestamp.AddSeconds(2)));

        Assert.IsFalse(changed);
        Assert.AreSame(before, tracker.History);
    }

    [TestMethod]
    public void FirstSampleStartsTodaysPeakFromTheSampleOnly()
    {
        var tracker = new PeakTracker();

        tracker.Update(Sample(31.4, 36.8, ReportSamples.Timestamp));

        // 每日尖峰只看取樣到的 commit 已用，不看 Windows 的開機以來尖峰（F-32）
        var today = tracker.TodayPeak!;
        Assert.AreEqual(new DateOnly(2026, 10, 26), today.Date);
        Assert.AreEqual(Gb(31.4), today.CommitBytes);
        Assert.AreEqual(Gb(39.4), today.CommitLimitBytes);
        Assert.AreEqual(ReportSamples.Timestamp, today.Time);
        Assert.HasCount(5, today.TopApps);
        Assert.AreEqual("Claude", today.TopApps[0].Name);
    }

    [TestMethod]
    public void HigherSampleRaisesTodaysPeakAndLowerSampleKeepsIt()
    {
        var tracker = new PeakTracker();
        tracker.Update(Sample(31.4, 36.8, At(10, 26, 14, 0)));

        Assert.IsTrue(tracker.Update(Sample(33.5, 36.8, At(10, 26, 14, 1))));
        Assert.IsFalse(tracker.Update(Sample(32.0, 36.8, At(10, 26, 14, 2))));
        // 和目前紀錄相同不算新高，時間維持第一次達到的時刻
        Assert.IsFalse(tracker.Update(Sample(33.5, 36.8, At(10, 26, 14, 3))));

        var today = tracker.TodayPeak!;
        Assert.AreEqual(Gb(33.5), today.CommitBytes);
        Assert.AreEqual(At(10, 26, 14, 1), today.Time);
        Assert.HasCount(1, tracker.History.Days);
    }

    [TestMethod]
    public void MidnightStartsANewDailyPeak()
    {
        var tracker = new PeakTracker();
        tracker.Update(Sample(33.0, 36.8, At(10, 26, 23, 59, 58)));

        bool changed = tracker.Update(Sample(30.0, 36.8, At(10, 27, 0, 0, 0)));

        Assert.IsTrue(changed);
        var days = tracker.History.Days;
        Assert.HasCount(2, days);
        Assert.AreEqual(new DateOnly(2026, 10, 26), days[0].Date);
        Assert.AreEqual(Gb(33.0), days[0].CommitBytes);
        Assert.AreEqual(new DateOnly(2026, 10, 27), days[1].Date);
        Assert.AreEqual(Gb(30.0), days[1].CommitBytes);
        Assert.AreSame(days[1], tracker.TodayPeak);
    }

    [TestMethod]
    public void HistoryIsReplacedInsteadOfModified()
    {
        var tracker = new PeakTracker();
        tracker.Update(Sample(31.4, 36.8, At(10, 26, 14, 0)));
        var before = tracker.History;

        tracker.Update(Sample(33.5, 36.8, At(10, 26, 14, 1)));

        Assert.AreNotSame(before, tracker.History);
        Assert.AreEqual(Gb(31.4), before.Days[0].CommitBytes);
        Assert.AreEqual(Gb(33.5), tracker.History.Days[0].CommitBytes);
    }

    [TestMethod]
    public void RestartOnTheSameDayContinuesTodaysPeak()
    {
        var saved = SavedDay(new DateOnly(2026, 10, 26), 35.0, At(10, 26, 9, 0));
        var tracker = new PeakTracker(new PeakHistory([], [saved]));

        tracker.Update(Sample(31.4, 36.8, At(10, 26, 14, 0)));

        Assert.AreSame(saved, tracker.TodayPeak);
        Assert.HasCount(1, tracker.History.Days);

        tracker.Update(Sample(35.5, 36.8, At(10, 26, 14, 1)));

        Assert.AreEqual(Gb(35.5), tracker.TodayPeak!.CommitBytes);
        Assert.AreEqual(At(10, 26, 14, 1), tracker.TodayPeak.Time);
        Assert.HasCount(1, tracker.History.Days);
    }

    [TestMethod]
    public void RestartWithinTheSameBootKeepsTheSavedPeak()
    {
        // CSV 只記到秒與 0.01 GB：開機時間差 1 秒、Windows 的尖峰比紀錄多 3 MB，仍是同一個尖峰
        var saved = SavedBoot(Boot.AddSeconds(-1), 36.80, At(10, 26, 12, 10));
        var tracker = new PeakTracker(new PeakHistory([saved], []));
        long windowsPeak = Gb(36.80) + 3 * 1024 * 1024;

        var sample = Sample(31.4, 36.8, At(10, 26, 14, 0));
        tracker.Update(sample with { Snapshot = sample.Snapshot with { CommitPeakBytes = windowsPeak } });

        var peak = tracker.BootPeak!;
        Assert.AreEqual(At(10, 26, 12, 10), peak.Time);
        Assert.AreSame(saved.TopApps, peak.TopApps);
        Assert.AreEqual(saved.BootTimeUtc, peak.BootTimeUtc);
        Assert.AreEqual(windowsPeak, peak.CommitBytes);
        Assert.HasCount(1, tracker.History.Boots);
    }

    [TestMethod]
    public void RestartWithTheSameValueIsNotABootChange()
    {
        var saved = SavedBoot(Boot, 36.8, At(10, 26, 12, 10));
        var savedDay = SavedDay(new DateOnly(2026, 10, 26), 35.0, At(10, 26, 9, 0));
        var tracker = new PeakTracker(new PeakHistory([saved], [savedDay]));

        bool changed = tracker.Update(Sample(31.4, 36.8, At(10, 26, 14, 0)));

        Assert.IsFalse(changed);
        Assert.AreSame(saved, tracker.BootPeak);
    }

    [TestMethod]
    public void HigherPeakWhileNotRunningReplacesTheSavedPeakWithAnUnknownOne()
    {
        var saved = SavedBoot(Boot, 36.8, At(10, 26, 12, 10));
        var tracker = new PeakTracker(new PeakHistory([saved], []));

        tracker.Update(Sample(31.4, 38.0, At(10, 26, 14, 0)));

        var peak = tracker.BootPeak!;
        Assert.IsFalse(peak.IsKnown);
        Assert.AreEqual(Gb(38.0), peak.CommitBytes);
        Assert.HasCount(1, tracker.History.Boots);
    }

    [TestMethod]
    public void ADifferentBootStartsANewRecord()
    {
        var earlier = SavedBoot(new DateTime(2026, 10, 25, 1, 0, 0, DateTimeKind.Utc), 39.0, At(10, 25, 20, 0));
        var tracker = new PeakTracker(new PeakHistory([earlier], []));

        tracker.Update(Sample(31.4, 36.8, At(10, 26, 14, 0)));

        var boots = tracker.History.Boots;
        Assert.HasCount(2, boots);
        Assert.AreSame(earlier, boots[0]);
        Assert.AreEqual(Boot, boots[1].BootTimeUtc);
        Assert.AreEqual(Gb(36.8), boots[1].CommitBytes);
    }

    [TestMethod]
    public void BootTimeOffByMoreThanTheToleranceIsADifferentBoot()
    {
        var saved = SavedBoot(Boot.AddSeconds(-61), 36.8, At(10, 26, 12, 10));
        var tracker = new PeakTracker(new PeakHistory([saved], []));

        tracker.Update(Sample(31.4, 36.8, At(10, 26, 14, 0)));

        Assert.HasCount(2, tracker.History.Boots);
        Assert.IsFalse(tracker.BootPeak!.IsKnown);
        Assert.AreEqual(TimeSpan.FromSeconds(60), PeakTracker.BootTimeTolerance);
    }

    [TestMethod]
    public void PruneKeepsTheRetentionDaysIncludingToday()
    {
        // 保留 90 天：2026-10-26 往前算，2026-07-29 是保留的第一天
        var history = new PeakHistory(
            [
                SavedBoot(new DateTime(2026, 7, 27, 1, 0, 0, DateTimeKind.Utc), 30.0, At(7, 28, 23, 0)),
                // 時間未知時以開機時間的本機日期判斷：UTC 07-28 18:00 是本機 07-29 02:00
                SavedBoot(new DateTime(2026, 7, 28, 18, 0, 0, DateTimeKind.Utc), 30.0, null),
            ],
            [
                SavedDay(new DateOnly(2026, 7, 28), 30.0, At(7, 28, 12, 0)),
                SavedDay(new DateOnly(2026, 7, 29), 30.0, At(7, 29, 12, 0)),
            ]);
        var tracker = new PeakTracker(history);
        tracker.Update(Sample(31.4, 36.8, At(10, 26, 14, 0)));

        bool removed = tracker.Prune(At(10, 26, 14, 0), 90);

        Assert.IsTrue(removed);
        CollectionAssert.AreEqual(
            new[] { new DateTime(2026, 7, 28, 18, 0, 0, DateTimeKind.Utc), Boot },
            tracker.History.Boots.Select(b => b.BootTimeUtc).ToArray());
        CollectionAssert.AreEqual(
            new[] { new DateOnly(2026, 7, 29), new DateOnly(2026, 10, 26) },
            tracker.History.Days.Select(d => d.Date).ToArray());
    }

    [TestMethod]
    public void PruneNeverRemovesThisBootOrToday()
    {
        // 快速啟動讓一次開機可以跨很多天；本次開機的紀錄即使早於保留天數也要保留
        var tracker = new PeakTracker();
        tracker.Update(Sample(31.4, 36.8, At(10, 26, 14, 0), new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)));

        bool removed = tracker.Prune(At(10, 26, 14, 0), 1);

        Assert.IsFalse(removed);
        Assert.HasCount(1, tracker.History.Boots);
        Assert.HasCount(1, tracker.History.Days);
    }

    [TestMethod]
    public void PruneWithNothingToRemoveKeepsTheHistory()
    {
        var tracker = new PeakTracker();
        tracker.Update(Sample(31.4, 36.8, At(10, 26, 14, 0)));
        var before = tracker.History;

        Assert.IsFalse(tracker.Prune(At(10, 26, 14, 0), 90));
        Assert.AreSame(before, tracker.History);
    }
}
```

- [ ] **Step 2：執行測試，確認失敗**

執行：`(cd app && dotnet test)`

預期：建置失敗，錯誤訊息包含（實際輸出另有檔案路徑與重複的行）

```text
error CS0246: 找不到類型或命名空間名稱 'DailyPeak' (是否遺漏了 using 指示詞或組件參考?)
```

- [ ] **Step 3：實作**

新增 `app/MemSweep.Core/DailyPeak.cs`：

```csharp
namespace MemSweep.Core;

/// <summary>
/// 某一天（本機時間）MemSweep 取樣到的 commit 已用最高值與當時的前五名（F-30、F-32）。
/// 只計入 MemSweep 執行期間的取樣，所以時間與前五名一定已知。
/// </summary>
public sealed record DailyPeak(
    DateOnly Date,
    long CommitBytes,
    long CommitLimitBytes,
    DateTimeOffset Time,
    IReadOnlyList<PeakApp> TopApps);
```

新增 `app/MemSweep.Core/PeakHistory.cs`：

```csharp
namespace MemSweep.Core;

/// <summary>所有尖峰紀錄（F-24、F-33）；兩份清單都依加入的先後排列，與 CSV 的列順序相同。</summary>
public sealed record PeakHistory(IReadOnlyList<BootPeak> Boots, IReadOnlyList<DailyPeak> Days)
{
    public static PeakHistory Empty { get; } = new([], []);
}
```

`app/MemSweep.Core/PeakTracker.cs` 整份換成：

```csharp
namespace MemSweep.Core;

/// <summary>
/// 追蹤開機尖峰與每日尖峰（F-30～F-32、Design 3.10），並保存全部尖峰紀錄。
/// 不是執行緒安全的：同一時間只能由一個執行緒呼叫。
/// </summary>
public sealed class PeakTracker
{
    /// <summary>同一次開機的開機時間可能因校時略有偏移，CSV 也只記到秒，差距在這個範圍內就視為同一次開機。</summary>
    public static readonly TimeSpan BootTimeTolerance = TimeSpan.FromSeconds(60);

    // CSV 的 GB 只保留兩位小數，Windows 的尖峰比已有紀錄高出不到半個最小單位時，視為同一個尖峰
    private static readonly long CsvRoundingBytes = MemoryMath.FromGigabytes(0.005);

    private readonly List<BootPeak> _boots;
    private readonly List<DailyPeak> _days;

    public PeakTracker()
        : this(PeakHistory.Empty)
    {
    }

    /// <summary>從已有的紀錄接續，例如 MemSweep 在同一次開機或同一天內重新啟動。</summary>
    public PeakTracker(PeakHistory saved)
    {
        _boots = saved.Boots.ToList();
        _days = saved.Days.ToList();
        History = saved;
    }

    /// <summary>本次開機的尖峰；第一次取樣前為 null。</summary>
    public BootPeak? BootPeak { get; private set; }

    /// <summary>最近一次取樣那一天的尖峰；第一次取樣前為 null。</summary>
    public DailyPeak? TodayPeak { get; private set; }

    /// <summary>全部尖峰紀錄；有變動時換成新的物件，舊物件不會被修改。</summary>
    public PeakHistory History { get; private set; }

    /// <summary>以一次取樣更新開機尖峰與每日尖峰；任一筆紀錄有變動時回傳 true。</summary>
    public bool Update(GroupedSnapshot grouped)
    {
        bool bootChanged = UpdateBoot(grouped);
        bool dayChanged = UpdateDay(grouped);
        if (!bootChanged && !dayChanged)
        {
            return false;
        }
        Publish();
        return true;
    }

    /// <summary>
    /// 刪除早於保留天數的紀錄：保留今天在內的 retentionDays 天（F-33）。
    /// 開機紀錄以尖峰時間的日期判斷，時間未知時用開機時間；本次開機與今天的紀錄一律保留。
    /// 有刪除時回傳 true。
    /// </summary>
    public bool Prune(DateTimeOffset now, int retentionDays)
    {
        var oldestKept = DateOnly.FromDateTime(now.DateTime).AddDays(1 - retentionDays);
        int removed = _boots.RemoveAll(b => !ReferenceEquals(b, BootPeak) && DateOf(b, now.Offset) < oldestKept)
            + _days.RemoveAll(d => !ReferenceEquals(d, TodayPeak) && d.Date < oldestKept);
        if (removed == 0)
        {
            return false;
        }
        Publish();
        return true;
    }

    private bool UpdateBoot(GroupedSnapshot grouped)
    {
        var snapshot = grouped.Snapshot;
        if (BootPeak is null)
        {
            // 第一次取樣：MemSweep 在同一次開機內重新啟動時，接續已有的紀錄
            var saved = _boots.LastOrDefault(b => (b.BootTimeUtc - snapshot.BootTimeUtc).Duration() <= BootTimeTolerance);
            if (saved is not null && snapshot.CommitPeakBytes <= saved.CommitBytes + CsvRoundingBytes)
            {
                // 沿用已有紀錄的時間與前五名；數值改用較高的 Windows 精確值，之後的取樣才比較得準
                var resumed = snapshot.CommitPeakBytes > saved.CommitBytes
                    ? saved with { CommitBytes = snapshot.CommitPeakBytes }
                    : saved;
                Replace(_boots, saved, resumed);
                BootPeak = resumed;
                return !ReferenceEquals(resumed, saved);
            }

            // Windows 的尖峰高於目前用量，代表尖峰發生在 MemSweep 未執行期間（F-31）
            var first = snapshot.CommitPeakBytes > snapshot.CommitUsedBytes
                ? new BootPeak(snapshot.BootTimeUtc, snapshot.CommitPeakBytes, snapshot.CommitLimitBytes, null, null)
                : BootFromSample(grouped);
            Replace(_boots, saved, first);
            BootPeak = first;
            return true;
        }

        if (snapshot.CommitPeakBytes <= BootPeak.CommitBytes)
        {
            return false;
        }

        // 尖峰發生在上次取樣之後（最多 2 秒前），以本次取樣的時間與前五名作為近似值
        var next = BootFromSample(grouped);
        Replace(_boots, BootPeak, next);
        BootPeak = next;
        return true;
    }

    private bool UpdateDay(GroupedSnapshot grouped)
    {
        var snapshot = grouped.Snapshot;
        var date = DateOnly.FromDateTime(snapshot.Timestamp.DateTime);
        if (TodayPeak?.Date != date)
        {
            // 第一次取樣或跨過午夜（F-32）：同一天已有紀錄時（MemSweep 重新啟動）就接續
            TodayPeak = _days.LastOrDefault(d => d.Date == date);
        }

        if (TodayPeak is not null && snapshot.CommitUsedBytes <= TodayPeak.CommitBytes)
        {
            return false;
        }

        var next = new DailyPeak(date, snapshot.CommitUsedBytes, snapshot.CommitLimitBytes, snapshot.Timestamp, TopApps(grouped));
        Replace(_days, TodayPeak, next);
        TodayPeak = next;
        return true;
    }

    private void Publish() => History = new PeakHistory(_boots.ToList(), _days.ToList());

    private static void Replace<T>(List<T> list, T? current, T next)
        where T : class
    {
        int index = current is null ? -1 : list.FindIndex(item => ReferenceEquals(item, current));
        if (index < 0)
        {
            list.Add(next);
        }
        else
        {
            list[index] = next;
        }
    }

    private static DateOnly DateOf(BootPeak boot, TimeSpan localOffset) =>
        DateOnly.FromDateTime((boot.Time ?? new DateTimeOffset(boot.BootTimeUtc).ToOffset(localOffset)).DateTime);

    private static BootPeak BootFromSample(GroupedSnapshot grouped)
    {
        var snapshot = grouped.Snapshot;
        return new BootPeak(
            snapshot.BootTimeUtc,
            snapshot.CommitPeakBytes,
            snapshot.CommitLimitBytes,
            snapshot.Timestamp,
            TopApps(grouped));
    }

    private static List<PeakApp> TopApps(GroupedSnapshot grouped) =>
        grouped.Apps
            .Take(ReportFormatter.TopCount)
            .Select(a => new PeakApp(a.Name, a.CommitBytes))
            .ToList();
}
```

- [ ] **Step 4：執行測試，確認通過**

執行：`(cd app && dotnet test)`

預期：總計 134，失敗 0。

- [ ] **Step 5：Commit**

```bash
git add app/MemSweep.Core app/MemSweep.Core.Tests
git status --short
git commit -F - <<'EOF'
新增每日尖峰並接續既有的尖峰紀錄

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

- [ ] **Step 6：推送**

第一次推送：`git fetch origin` 後執行 `git push -u origin phase-03-peaks-window`。

---

### Task 2：尖峰紀錄 CSV 格式

**檔案：**

- 新增：`app/MemSweep.Core/PeakCsv.cs`、`app/MemSweep.Core.Tests/PeakCsvTests.cs`

**實作的條文：**

> - F-34 紀錄存成 CSV，編碼為含 BOM 的 UTF-8，讓 Excel 直接開啟時中文不亂碼。〔Claude 決定〕

> 尖峰 CSV：含 BOM 的 UTF-8、CRLF 換行。欄位名稱用中文，方便在 Excel 閱讀；記憶體以 GB 表示，保留兩位小數。

> | `peaks-boot.csv` | 開機時間、尖峰時間、commit已用GB、commit上限GB、第1名…第5名、第1名GB…第5名GB |
> | `peaks-daily.csv` | 日期、尖峰時間、commit已用GB、commit上限GB、第1名…第5名、第1名GB…第5名GB |

> - 時間格式為本機時間 `yyyy-MM-dd HH:mm:ss`；未知時填「未知」。

**介面：**

- 使用：Task 1 的 `DailyPeak`；`BootPeak`、`PeakApp`、`MemoryMath`。
- 產生：`PeakCsv.BootFileName`、`PeakCsv.DailyFileName`、`PeakCsv.Unknown`；`byte[] WriteBoots(IEnumerable<BootPeak>, TimeZoneInfo)`、`byte[] WriteDays(IEnumerable<DailyPeak>, TimeZoneInfo)`；`List<BootPeak> ReadBoots(byte[], TimeZoneInfo)`、`List<DailyPeak> ReadDays(byte[], TimeZoneInfo)`，內容不符合格式時丟出 `FormatException`。

- [ ] **Step 1：寫失敗的測試**

新增 `app/MemSweep.Core.Tests/PeakCsvTests.cs`：

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
}
```

- [ ] **Step 2：執行測試，確認失敗**

執行：`(cd app && dotnet test)`

預期：建置失敗，錯誤訊息包含（實際輸出另有檔案路徑與重複的行）

```text
error CS0103: 名稱 'PeakCsv' 不存在於目前的內容中
```

- [ ] **Step 3：實作**

新增 `app/MemSweep.Core/PeakCsv.cs`：

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
    private const int TopCount = 5;
    private const int ColumnCount = 4 + TopCount * 2;

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
        // 日光節約時間跳過的時刻不會由 MemSweep 寫出，出現時視為格式不符
        if (!DateTime.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            || zone.IsInvalidTime(value))
        {
            throw new FormatException("時間格式不符：「" + text + "」");
        }
        return value;
    }

    private static string Gigabytes(long bytes) =>
        MemoryMath.ToGigabytes(bytes).ToString("0.00", CultureInfo.InvariantCulture);

    private static long ParseGigabytes(string text) =>
        double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double value)
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

- [ ] **Step 4：執行測試，確認通過**

執行：`(cd app && dotnet test)`

預期：總計 158，失敗 0。

- [ ] **Step 5：Commit**

```bash
git add app/MemSweep.Core/PeakCsv.cs app/MemSweep.Core.Tests/PeakCsvTests.cs
git status --short
git commit -F - <<'EOF'
新增尖峰紀錄的 CSV 格式

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

- [ ] **Step 6：推送**（依「共同執行規則」先 fetch 並檢查）

---

### Task 3：尖峰紀錄檔的讀寫

**檔案：**

- 新增：`app/MemSweep.Core/PeakStore.cs`、`app/MemSweep.Core.Tests/PeakStoreTests.cs`

**實作的條文：**

> - F-52 設定與尖峰紀錄都存放在 exe 所在資料夾下的 `data\` 子資料夾。

> 所在位置：`MemSweep.exe` 所在資料夾下的 `data\`，不存在時自動建立。無法寫入時，小面板顯示「data 資料夾無法寫入，尖峰紀錄已停用」，其他功能照常。

> - 寫入時機：尖峰有變動就標記，最多每 60 秒寫一次，正常結束時再寫一次。先寫暫存檔再取代原檔，異常結束最多遺失 60 秒內的更新。

**介面：**

- 使用：Task 1 的 `PeakHistory`；Task 2 的 `PeakCsv`。
- 產生：
  - `interface IPeakStore { void Save(PeakHistory history); }`，失敗時丟出 `IOException` 或 `UnauthorizedAccessException`
  - `PeakStoreOpening(PeakHistory History, IPeakStore? Store, string? Warning)`
  - `PeakStore.Open(string directory, TimeZoneInfo zone)`、`PeakStore.DefaultRetentionDays`（90）、`PeakStore.UnwritableWarning`

- [ ] **Step 1：寫失敗的測試**

新增 `app/MemSweep.Core.Tests/PeakStoreTests.cs`：

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

預期：建置失敗，錯誤訊息包含（實際輸出另有檔案路徑與重複的行）

```text
error CS0103: 名稱 'PeakStore' 不存在於目前的內容中
```

- [ ] **Step 3：實作**

新增 `app/MemSweep.Core/PeakStore.cs`：

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
        Replace(PeakCsv.BootFileName, PeakCsv.WriteBoots(history.Boots, _zone));
        Replace(PeakCsv.DailyFileName, PeakCsv.WriteDays(history.Days, _zone));
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

    // 先寫暫存檔再取代原檔，寫到一半中斷也不會留下殘缺的紀錄檔
    private void Replace(string fileName, byte[] content)
    {
        string path = Path.Combine(_directory, fileName);
        string temp = path + ".tmp";
        File.WriteAllBytes(temp, content);
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

- [ ] **Step 4：執行測試，確認通過**

執行：`(cd app && dotnet test)`

預期：總計 169，失敗 0。

- [ ] **Step 5：Commit**

```bash
git add app/MemSweep.Core/PeakStore.cs app/MemSweep.Core.Tests/PeakStoreTests.cs
git status --short
git commit -F - <<'EOF'
新增尖峰紀錄檔的讀寫

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

- [ ] **Step 6：推送**（依「共同執行規則」先 fetch 並檢查）

---

### Task 4：尖峰紀錄的寫入節流與保留天數

**檔案：**

- 新增：`app/MemSweep.Core/PeakRecorder.cs`、`app/MemSweep.Core.Tests/PeakRecorderTests.cs`

**實作的條文：**

> - 寫入時機：尖峰有變動就標記，最多每 60 秒寫一次，正常結束時再寫一次。先寫暫存檔再取代原檔，異常結束最多遺失 60 秒內的更新。
> - 保留天數清理：啟動時與跨日時執行，刪除尖峰時間（或日期）早於保留天數的列（F-33）。

**介面：**

- 使用：Task 1 的 `PeakTracker`；Task 3 的 `IPeakStore`、`PeakStoreOpening`。
- 產生：
  - `PeakState(BootPeak? BootPeak, PeakHistory History, string? StorageWarning)`
  - `PeakRecorder(PeakStoreOpening opening, int retentionDays)`；`PeakState State`；`void Record(GroupedSnapshot grouped)`；`void Flush()`；`PeakRecorder.WriteInterval`

- [ ] **Step 1：寫失敗的測試**

新增 `app/MemSweep.Core.Tests/PeakRecorderTests.cs`：

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
}
```

- [ ] **Step 2：執行測試，確認失敗**

執行：`(cd app && dotnet test)`

預期：建置失敗，錯誤訊息包含（實際輸出另有檔案路徑與重複的行）

```text
error CS0246: 找不到類型或命名空間名稱 'PeakRecorder' (是否遺漏了 using 指示詞或組件參考?)
```

- [ ] **Step 3：實作**

新增 `app/MemSweep.Core/PeakRecorder.cs`：

```csharp
namespace MemSweep.Core;

/// <summary>交給介面顯示的尖峰狀態；內容不會再被修改，可以交給其他執行緒。</summary>
public sealed record PeakState(BootPeak? BootPeak, PeakHistory History, string? StorageWarning);

/// <summary>
/// 以每次取樣更新尖峰紀錄並寫入 CSV（Design 3.9）：尖峰有變動就標記，最多每 60 秒寫一次，
/// 正常結束時再寫一次；保留天數清理在啟動後第一次取樣與跨日時執行（F-33）。
/// 取樣執行緒與結束時的 UI 執行緒都會呼叫，以 lock 保護。
/// </summary>
public sealed class PeakRecorder
{
    public static readonly TimeSpan WriteInterval = TimeSpan.FromSeconds(60);

    private readonly Lock _gate = new();
    private readonly PeakTracker _tracker;
    private readonly IPeakStore? _store;
    private readonly string? _warning;
    private readonly int _retentionDays;
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

- [ ] **Step 4：執行測試，確認通過**

執行：`(cd app && dotnet test)`

預期：總計 181，失敗 0。

- [ ] **Step 5：Commit**

```bash
git add app/MemSweep.Core/PeakRecorder.cs app/MemSweep.Core.Tests/PeakRecorderTests.cs
git status --short
git commit -F - <<'EOF'
新增尖峰紀錄的寫入節流與保留天數清理

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

- [ ] **Step 6：推送**（依「共同執行規則」先 fetch 並檢查）

---

### Task 5：常駐程式寫入尖峰紀錄

**檔案：**

- 修改（整份換成）：`app/MemSweep.Core/PanelModel.cs`、`app/MemSweep.Desktop/App.xaml.cs`、`app/MemSweep.Desktop/TrayController.cs`、`app/MemSweep.Desktop/FlyoutPanel.xaml`
- 修改（整份換成）：`app/MemSweep.Core.Tests/DisplayModelTests.cs`、`app/MemSweep.Core.Tests/FlyoutTests.cs`

**實作的條文：**

> - F-52 設定與尖峰紀錄都存放在 exe 所在資料夾下的 `data\` 子資料夾。

> 所在位置：`MemSweep.exe` 所在資料夾下的 `data\`，不存在時自動建立。無法寫入時，小面板顯示「data 資料夾無法寫入，尖峰紀錄已停用」，其他功能照常。

> - 每次取樣：`SystemReader` → `AppGrouper` → `PeakTracker`，結果發布給系統匣圖示與開著的視窗。

**介面：**

- 使用：Task 3 的 `PeakStore.Open`、`PeakStore.DefaultRetentionDays`；Task 4 的 `PeakRecorder`、`PeakState`。
- 產生：
  - `PanelModel` 多一個屬性 `string? Warning`；`PanelModel.Build(GroupedSnapshot grouped, BootPeak? peak, UsageThresholds thresholds, string? warning = null)`
  - `TrayController.Update(GroupedSnapshot grouped, PeakState peaks)`（Task 7 會修改 `TrayController`）
  - 小面板的 `WarningText`（Task 7 移到 `SummaryView`）

- [ ] **Step 1：寫失敗的測試**

`app/MemSweep.Core.Tests/DisplayModelTests.cs` 整份換成：

```csharp
using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class DisplayModelTests
{
    private static readonly DateTime Boot = new(2026, 10, 26, 0, 30, 0, DateTimeKind.Utc);

    private static BootPeak KnownPeak(DateTimeOffset time) =>
        new(Boot, MemoryMath.FromGigabytes(36.8), MemoryMath.FromGigabytes(39.4), time, [new PeakApp("Claude", 1)]);

    [TestMethod]
    [DataRow(0, UsageLevel.Green)]
    [DataRow(69, UsageLevel.Green)]
    [DataRow(70, UsageLevel.Orange)]
    [DataRow(84, UsageLevel.Orange)]
    [DataRow(85, UsageLevel.Red)]
    [DataRow(100, UsageLevel.Red)]
    public void DefaultThresholdsAre70And85(int percent, UsageLevel expected)
    {
        Assert.AreEqual(expected, UsageThresholds.Default.LevelOf(percent));
    }

    [TestMethod]
    public void CustomThresholds()
    {
        var thresholds = new UsageThresholds(60, 90);

        Assert.AreEqual(UsageLevel.Green, thresholds.LevelOf(59));
        Assert.AreEqual(UsageLevel.Orange, thresholds.LevelOf(60));
        Assert.AreEqual(UsageLevel.Orange, thresholds.LevelOf(89));
        Assert.AreEqual(UsageLevel.Red, thresholds.LevelOf(90));
    }

    [TestMethod]
    public void TrayIconTextIsTheCommitPercent()
    {
        var snapshot = ReportSamples.Typical().Snapshot;

        Assert.AreEqual(80, TrayText.CommitPercent(snapshot));
        Assert.AreEqual("80", TrayText.IconText(snapshot));
    }

    [TestMethod]
    public void TrayIconTextNeverExceeds100()
    {
        var snapshot = ReportSamples.Typical().Snapshot;
        var full = snapshot with { CommitUsedBytes = snapshot.CommitLimitBytes };
        var over = snapshot with { CommitUsedBytes = snapshot.CommitLimitBytes + MemoryMath.BytesPerGigabyte };

        Assert.AreEqual("100", TrayText.IconText(full));
        Assert.AreEqual("100", TrayText.IconText(over));
    }

    [TestMethod]
    public void TooltipShowsCommitAndRam()
    {
        string tooltip = TrayText.Tooltip(ReportSamples.Typical().Snapshot);

        Assert.AreEqual("MemSweep\nCommit 31.4 / 39.4 GB（80%）\nRAM 20.5 / 31.4 GB", tooltip);
        Assert.IsLessThanOrEqualTo(127, tooltip.Length);
    }

    [TestMethod]
    public void PanelGauges()
    {
        var panel = PanelModel.Build(ReportSamples.Typical(), null, UsageThresholds.Default);

        Assert.AreEqual("Commit", panel.Commit.Label);
        Assert.AreEqual("31.4 / 39.4 GB（80%）", panel.Commit.ValueText);
        Assert.AreEqual(31.4 / 39.4, panel.Commit.Fraction, 0.001);
        Assert.AreEqual(UsageLevel.Orange, panel.Commit.Level);
        Assert.AreEqual("實體 RAM", panel.Ram.Label);
        Assert.AreEqual("20.5 / 31.4 GB（65%）", panel.Ram.ValueText);
        Assert.AreEqual(20.5 / 31.4, panel.Ram.Fraction, 0.001);
        Assert.IsNull(panel.Ram.Level);
    }

    [TestMethod]
    public void PanelRowsAreTopFivePlusUnattributedWithBarsRelativeToTheLargest()
    {
        var panel = PanelModel.Build(ReportSamples.Typical(), null, UsageThresholds.Default);

        Assert.HasCount(6, panel.Rows);
        Assert.AreEqual(new PanelRow("Claude", "36", "3.2 GB", 3.2 / 6.9, false), panel.Rows[0] with { BarFraction = 3.2 / 6.9 });
        Assert.AreEqual(3.2 / 6.9, panel.Rows[0].BarFraction, 0.001);
        Assert.AreEqual("Node.js JavaScript Runtime", panel.Rows[4].Name);
        Assert.AreEqual("30", panel.Rows[4].CountText);
        var last = panel.Rows[5];
        Assert.AreEqual(GroupedSnapshot.UnattributedName, last.Name);
        Assert.AreEqual(string.Empty, last.CountText);
        Assert.AreEqual("6.9 GB", last.GigabytesText);
        Assert.AreEqual(1.0, last.BarFraction, 0.001);
        Assert.IsTrue(last.IsUnattributed);
        Assert.IsFalse(panel.Rows.Take(5).Any(r => r.IsUnattributed));
    }

    [TestMethod]
    public void PanelRowsWithNothingToCompareHaveEmptyBars()
    {
        var typical = ReportSamples.Typical();
        var empty = typical with { Apps = [], UnattributedBytes = 0 };

        var panel = PanelModel.Build(empty, null, UsageThresholds.Default);

        Assert.HasCount(1, panel.Rows);
        Assert.AreEqual(0.0, panel.Rows[0].BarFraction);
    }

    [TestMethod]
    public void PanelCarriesTheStorageWarning()
    {
        var withWarning = PanelModel.Build(ReportSamples.Typical(), null, UsageThresholds.Default, "data 資料夾無法寫入，尖峰紀錄已停用");
        var withoutWarning = PanelModel.Build(ReportSamples.Typical(), null, UsageThresholds.Default);

        Assert.AreEqual("data 資料夾無法寫入，尖峰紀錄已停用", withWarning.Warning);
        Assert.IsNull(withoutWarning.Warning);
    }

    [TestMethod]
    public void PeakTextBeforeTheFirstPeak()
    {
        var panel = PanelModel.Build(ReportSamples.Typical(), null, UsageThresholds.Default);

        Assert.AreEqual("開機以來 commit 尖峰：—", panel.PeakText);
    }

    [TestMethod]
    public void PeakTextWhenTheTimeIsUnknown()
    {
        var peak = new BootPeak(Boot, MemoryMath.FromGigabytes(39.4), MemoryMath.FromGigabytes(39.4), null, null);

        var panel = PanelModel.Build(ReportSamples.Typical(), peak, UsageThresholds.Default);

        Assert.AreEqual("開機以來 commit 尖峰：39.4 GB（時間未知）", panel.PeakText);
    }

    [TestMethod]
    public void PeakTextOnTheSameDayShowsOnlyTheTime()
    {
        var time = new DateTimeOffset(2026, 10, 26, 12, 10, 45, TimeSpan.FromHours(8));

        var panel = PanelModel.Build(ReportSamples.Typical(), KnownPeak(time), UsageThresholds.Default);

        Assert.AreEqual("開機以來 commit 尖峰：36.8 GB（12:10）", panel.PeakText);
    }

    [TestMethod]
    public void PeakTextOnAnEarlierDayShowsTheDate()
    {
        var time = new DateTimeOffset(2026, 10, 25, 21, 14, 0, TimeSpan.FromHours(8));

        var panel = PanelModel.Build(ReportSamples.Typical(), KnownPeak(time), UsageThresholds.Default);

        Assert.AreEqual("開機以來 commit 尖峰：36.8 GB（10/25 21:14）", panel.PeakText);
    }
}
```

`app/MemSweep.Core.Tests/FlyoutTests.cs` 整份換成：

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MemSweep.Core;
using MemSweep.Desktop;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class FlyoutTests
{
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    private static FlyoutPanel LaidOutPanel(PanelModel model)
    {
        var panel = new FlyoutPanel { DataContext = model };
        panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        panel.Arrange(new Rect(panel.DesiredSize));
        panel.UpdateLayout();
        return panel;
    }

    [TestMethod]
    public void PlacesTheFlyoutAtTheBottomRightOfTheWorkArea()
    {
        var (left, top) = FlyoutPlacement.Place(areaRight: 1920, areaBottom: 1032, width: 330, height: 400);

        Assert.AreEqual(1578.0, left);
        Assert.AreEqual(620.0, top);
    }

    [TestMethod]
    public void ThemeFollowsAppsUseLightTheme()
    {
        Assert.AreEqual(ThemeKind.Dark, ThemeReader.FromRegistryValue(0));
        Assert.AreEqual(ThemeKind.Light, ThemeReader.FromRegistryValue(1));
    }

    [TestMethod]
    public void MissingOrUnexpectedThemeValueIsLight()
    {
        Assert.AreEqual(ThemeKind.Light, ThemeReader.FromRegistryValue(null));
        Assert.AreEqual(ThemeKind.Light, ThemeReader.FromRegistryValue("0"));
    }

    [STATestMethod]
    public void PanelShowsGaugesPeakRowsAndButtons()
    {
        var peak = new BootPeak(
            new DateTime(2026, 10, 26, 0, 30, 0, DateTimeKind.Utc),
            MemoryMath.FromGigabytes(36.8),
            MemoryMath.FromGigabytes(39.4),
            new DateTimeOffset(2026, 10, 26, 12, 10, 0, TimeSpan.FromHours(8)),
            [new PeakApp("Claude", 1)]);
        var panel = LaidOutPanel(PanelModel.Build(ReportSamples.Typical(), peak, UsageThresholds.Default));

        var texts = Descendants(panel).OfType<TextBlock>().Select(t => t.Text).ToList();
        CollectionAssert.IsSubsetOf(
            new[]
            {
                "Commit",
                "31.4 / 39.4 GB（80%）",
                "實體 RAM",
                "20.5 / 31.4 GB（65%）",
                "開機以來 commit 尖峰：36.8 GB（12:10）",
                "Claude",
                "36",
                "3.2 GB",
                "系統（不屬於任何程式）",
                "6.9 GB",
            },
            texts);
        var buttons = Descendants(panel).OfType<Button>().Select(b => b.Content as string).ToList();
        CollectionAssert.AreEqual(new[] { "複製報告", "結束 MemSweep" }, buttons);
    }

    [STATestMethod]
    public void PanelShowsTheStorageWarningOnlyWhenThereIsOne()
    {
        const string warning = "peaks-daily.csv 無法讀取，尖峰紀錄已停用";
        var withWarning = LaidOutPanel(PanelModel.Build(ReportSamples.Typical(), null, UsageThresholds.Default, warning));
        var withoutWarning = LaidOutPanel(PanelModel.Build(ReportSamples.Typical(), null, UsageThresholds.Default));

        Assert.AreEqual(Visibility.Visible, withWarning.WarningText.Visibility);
        Assert.AreEqual(warning, withWarning.WarningText.Text);
        Assert.AreEqual(Visibility.Collapsed, withoutWarning.WarningText.Visibility);
    }

    [STATestMethod]
    public void PanelListsTopFivePlusUnattributed()
    {
        var panel = LaidOutPanel(PanelModel.Build(ReportSamples.Typical(), null, UsageThresholds.Default));

        var rows = Descendants(panel).OfType<ContentPresenter>().Where(c => c.Content is PanelRow).ToList();

        Assert.HasCount(6, rows);
    }
}
```

- [ ] **Step 2：執行測試，確認失敗**

執行：`(cd app && dotnet test)`

預期：建置失敗，錯誤訊息包含（實際輸出另有檔案路徑與重複的行）

```text
error CS1501: 方法 'Build' 沒有任何多載使用 4 個引數
error CS1061: 'FlyoutPanel' 未包含 'WarningText' 的定義，也找不到可接受類型 'FlyoutPanel' 第一個引數的可存取擴充方法 'WarningText' (是否遺漏 using 指示詞或組件參考?)
error CS1061: 'PanelModel' 未包含 'Warning' 的定義，也找不到可接受類型 'PanelModel' 第一個引數的可存取擴充方法 'Warning' (是否遺漏 using 指示詞或組件參考?)
```

- [ ] **Step 3：實作**

`app/MemSweep.Core/PanelModel.cs` 整份換成：

```csharp
using System.Globalization;

namespace MemSweep.Core;

/// <summary>量表：Level 為 null 表示不依門檻變色（實體 RAM）。</summary>
public sealed record GaugeModel(string Label, string ValueText, double Fraction, UsageLevel? Level);

/// <summary>小面板清單的一列；BarFraction 是相對於清單中最大一列的比例。</summary>
public sealed record PanelRow(string Name, string CountText, string GigabytesText, double BarFraction, bool IsUnattributed);

/// <summary>小面板要顯示的全部內容（F-11）；Warning 是尖峰紀錄停用的原因，沒有問題時為 null（Design 3.9）。</summary>
public sealed record PanelModel(GaugeModel Commit, GaugeModel Ram, string PeakText, IReadOnlyList<PanelRow> Rows, string? Warning)
{
    public static PanelModel Build(GroupedSnapshot grouped, BootPeak? peak, UsageThresholds thresholds, string? warning = null)
    {
        var s = grouped.Snapshot;
        int commitPercent = TrayText.CommitPercent(s);
        var commit = new GaugeModel(
            "Commit",
            GaugeText(s.CommitUsedBytes, s.CommitLimitBytes, commitPercent),
            Fraction(s.CommitUsedBytes, s.CommitLimitBytes),
            thresholds.LevelOf(commitPercent));
        var ram = new GaugeModel(
            "實體 RAM",
            GaugeText(s.PhysicalUsedBytes, s.PhysicalTotalBytes, MemoryMath.Percent(s.PhysicalUsedBytes, s.PhysicalTotalBytes)),
            Fraction(s.PhysicalUsedBytes, s.PhysicalTotalBytes),
            null);

        var top = grouped.Apps.Take(ReportFormatter.TopCount).ToList();
        long largest = Math.Max(grouped.UnattributedBytes, top.Count == 0 ? 0 : top.Max(a => a.CommitBytes));
        var rows = top
            .Select(a => new PanelRow(
                a.Name,
                a.ProcessCount.ToString(CultureInfo.InvariantCulture),
                MemoryMath.FormatGigabytes(a.CommitBytes) + " GB",
                Fraction(a.CommitBytes, largest),
                false))
            .Append(new PanelRow(
                GroupedSnapshot.UnattributedName,
                string.Empty,
                MemoryMath.FormatGigabytes(grouped.UnattributedBytes) + " GB",
                Fraction(grouped.UnattributedBytes, largest),
                true))
            .ToList();

        return new PanelModel(commit, ram, DescribePeak(peak, s.Timestamp), rows, warning);
    }

    private static string GaugeText(long used, long total, int percent) =>
        MemoryMath.FormatGigabytes(used) + " / " + MemoryMath.FormatGigabytes(total) + " GB（" + percent + "%）";

    private static double Fraction(long part, long whole) =>
        whole <= 0 ? 0 : Math.Clamp(part / (double)whole, 0, 1);

    private static string DescribePeak(BootPeak? peak, DateTimeOffset now)
    {
        const string prefix = "開機以來 commit 尖峰：";
        if (peak is null)
        {
            return prefix + "—";
        }

        string value = MemoryMath.FormatGigabytes(peak.CommitBytes) + " GB";
        if (peak.Time is not { } time)
        {
            return prefix + value + "（時間未知）";
        }

        // 同一天只顯示時間；開機尖峰可能跨日（快速啟動），不同天時加上日期
        string format = time.Date == now.Date ? "HH:mm" : "MM/dd HH:mm";
        return prefix + value + "（" + time.ToString(format, CultureInfo.InvariantCulture) + "）";
    }
}
```

`app/MemSweep.Desktop/FlyoutPanel.xaml` 整份換成：

```xml
<UserControl x:Class="MemSweep.Desktop.FlyoutPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Width="330"
             FontFamily="Segoe UI, Microsoft JhengHei UI"
             FontSize="12"
             Foreground="{DynamicResource TextBrush}">
  <UserControl.Resources>
    <Style x:Key="FillStyle" TargetType="Border">
      <Setter Property="CornerRadius" Value="4" />
      <Setter Property="RenderTransformOrigin" Value="0,0.5" />
    </Style>
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
  </UserControl.Resources>

  <Border Background="{DynamicResource PanelBackgroundBrush}"
          BorderBrush="{DynamicResource PanelBorderBrush}"
          BorderThickness="1"
          CornerRadius="8"
          Padding="14,12">
    <StackPanel>
      <!-- commit 量表：顏色依門檻（F-03） -->
      <DockPanel>
        <TextBlock DockPanel.Dock="Right" Text="{Binding Commit.ValueText}" />
        <TextBlock Text="{Binding Commit.Label}" FontWeight="SemiBold" />
      </DockPanel>
      <Grid Height="8" Margin="0,4,0,10">
        <Border Background="{DynamicResource TrackBrush}" CornerRadius="4" />
        <Border>
          <Border.Style>
            <Style TargetType="Border" BasedOn="{StaticResource FillStyle}">
              <Setter Property="Background" Value="{DynamicResource GreenBrush}" />
              <Style.Triggers>
                <DataTrigger Binding="{Binding Commit.Level}" Value="Orange">
                  <Setter Property="Background" Value="{DynamicResource OrangeBrush}" />
                </DataTrigger>
                <DataTrigger Binding="{Binding Commit.Level}" Value="Red">
                  <Setter Property="Background" Value="{DynamicResource RedBrush}" />
                </DataTrigger>
              </Style.Triggers>
            </Style>
          </Border.Style>
          <Border.RenderTransform>
            <ScaleTransform ScaleX="{Binding Commit.Fraction}" />
          </Border.RenderTransform>
        </Border>
      </Grid>

      <!-- 實體 RAM 量表：固定用強調色 -->
      <DockPanel>
        <TextBlock DockPanel.Dock="Right" Text="{Binding Ram.ValueText}" />
        <TextBlock Text="{Binding Ram.Label}" FontWeight="SemiBold" />
      </DockPanel>
      <Grid Height="8" Margin="0,4,0,10">
        <Border Background="{DynamicResource TrackBrush}" CornerRadius="4" />
        <Border Style="{StaticResource FillStyle}" Background="{DynamicResource AccentBrush}">
          <Border.RenderTransform>
            <ScaleTransform ScaleX="{Binding Ram.Fraction}" />
          </Border.RenderTransform>
        </Border>
      </Grid>

      <TextBlock Text="{Binding PeakText}" Foreground="{DynamicResource MutedTextBrush}" Margin="0,0,0,8" />

      <!-- 尖峰紀錄停用的原因（Design 3.9），沒有問題時不顯示 -->
      <TextBlock x:Name="WarningText"
                 Text="{Binding Warning}"
                 Foreground="{DynamicResource OrangeBrush}"
                 TextWrapping="Wrap"
                 Margin="0,0,0,8">
        <TextBlock.Style>
          <Style TargetType="TextBlock">
            <Style.Triggers>
              <DataTrigger Binding="{Binding Warning}" Value="{x:Null}">
                <Setter Property="Visibility" Value="Collapsed" />
              </DataTrigger>
            </Style.Triggers>
          </Style>
        </TextBlock.Style>
      </TextBlock>

      <!-- 前五名與「系統（不屬於任何程式）」 -->
      <ItemsControl ItemsSource="{Binding Rows}">
        <ItemsControl.ItemTemplate>
          <DataTemplate>
            <Border x:Name="Row" Padding="0,4" BorderBrush="{DynamicResource PanelBorderBrush}" BorderThickness="0">
              <Grid>
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width="*" />
                  <ColumnDefinition Width="Auto" />
                  <ColumnDefinition Width="80" />
                  <ColumnDefinition Width="54" />
                </Grid.ColumnDefinitions>
                <TextBlock x:Name="AppName" Text="{Binding Name}" TextTrimming="CharacterEllipsis" />
                <TextBlock Grid.Column="1"
                           Text="{Binding CountText}"
                           Foreground="{DynamicResource MutedTextBrush}"
                           Margin="6,0,0,0" />
                <Grid Grid.Column="2" Height="6" Margin="10,0,6,0">
                  <Border x:Name="Bar" Background="{DynamicResource BarBrush}" CornerRadius="3" RenderTransformOrigin="0,0.5">
                    <Border.RenderTransform>
                      <ScaleTransform ScaleX="{Binding BarFraction}" />
                    </Border.RenderTransform>
                  </Border>
                </Grid>
                <TextBlock Grid.Column="3" Text="{Binding GigabytesText}" HorizontalAlignment="Right" />
              </Grid>
            </Border>
            <DataTemplate.Triggers>
              <DataTrigger Binding="{Binding IsUnattributed}" Value="True">
                <Setter TargetName="AppName" Property="FontStyle" Value="Italic" />
                <Setter TargetName="AppName" Property="Foreground" Value="{DynamicResource MutedTextBrush}" />
                <Setter TargetName="Bar" Property="Background" Value="{DynamicResource UnattributedBarBrush}" />
                <Setter TargetName="Row" Property="BorderThickness" Value="0,1,0,0" />
                <Setter TargetName="Row" Property="Margin" Value="0,4,0,0" />
              </DataTrigger>
            </DataTemplate.Triggers>
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>

      <!-- 「完整清單…」在 Phase 03 加入 -->
      <StackPanel Orientation="Horizontal" Margin="0,12,0,0">
        <Button x:Name="CopyButton" Content="複製報告" Style="{StaticResource PanelButton}" Click="CopyButton_Click" />
        <Button Content="結束 MemSweep" Style="{StaticResource PanelButton}" Click="ExitButton_Click" />
      </StackPanel>
    </StackPanel>
  </Border>
</UserControl>
```

`app/MemSweep.Desktop/TrayController.cs` 整份換成：

```csharp
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MemSweep.Core;

namespace MemSweep.Desktop;

/// <summary>系統匣圖示、提示文字、右鍵選單與小面板（F-01～F-12）。必須在 UI 執行緒上使用。</summary>
internal sealed class TrayController : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private FlyoutWindow? _flyout;
    private Icon? _icon;
    private string? _iconKey;
    private GroupedSnapshot? _latest;
    private PeakState? _latestPeaks;

    public TrayController()
    {
        // 「完整清單…」在 Phase 03、「設定…」在 Phase 04 加入
        var menu = new ContextMenuStrip();
        menu.Items.Add("開啟小面板", null, (_, _) => ShowFlyout());
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
        // 還沒有第一次取樣的結果時沒有內容可顯示
        if (_latest is null)
        {
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
        var level = UsageThresholds.Default.LevelOf(TrayText.CommitPercent(snapshot));
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
        if (_flyout is { IsVisible: true })
        {
            _flyout.PanelView.DataContext = BuildPanel();
        }
    }

    public void Dispose()
    {
        _flyout?.Close();
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _icon?.Dispose();
    }

    private PanelModel BuildPanel() =>
        PanelModel.Build(_latest!, _latestPeaks?.BootPeak, UsageThresholds.Default, _latestPeaks?.StorageWarning);

    // 第一次開啟時才建立，之後只顯示／隱藏：只用系統匣時，行程不必載入 WPF 視窗（約省 40 MB）
    private FlyoutWindow EnsureFlyout()
    {
        if (_flyout is null)
        {
            _flyout = new FlyoutWindow();
            _flyout.PanelView.CopyClicked += CopyReport;
            _flyout.PanelView.ExitClicked += () => ExitRequested?.Invoke();
        }
        return _flyout;
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

        _theme = new ThemeWatcher(Resources);
        _tray = new TrayController();
        _tray.ExitRequested += () => Shutdown();
        _instance.ShowRequested += () => Dispatcher.InvokeAsync(() => _tray?.ShowFlyout());
        StartSampling();
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

    private void StartSampling()
    {
        // 設定與尖峰紀錄放在 exe 所在資料夾下的 data\（F-52）
        var opening = PeakStore.Open(Path.Combine(AppContext.BaseDirectory, "data"), TimeZoneInfo.Local);
        var peaks = new PeakRecorder(opening, PeakStore.DefaultRetentionDays);
        _peaks = peaks;

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
                Dispatcher.InvokeAsync(() => _tray?.Update(grouped, state));
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

預期：總計 183，失敗 0。

- [ ] **Step 5：Commit**

```bash
git add app/MemSweep.Core app/MemSweep.Desktop app/MemSweep.Core.Tests
git status --short
git commit -F - <<'EOF'
常駐程式寫入尖峰紀錄

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

- [ ] **Step 6：推送**（依「共同執行規則」先 fetch 並檢查）

---

### Task 6：完整視窗的顯示模型

**檔案：**

- 新增：`app/MemSweep.Core/ObservableObject.cs`、`app/MemSweep.Core/AppListRow.cs`、`app/MemSweep.Core/FullWindowModel.cs`
- 新增：`app/MemSweep.Core.Tests/FullWindowModelTests.cs`

**實作的條文：**

> - F-21 有三個分頁：總覽、程式、尖峰紀錄。
> - F-22 總覽：F-11 的全部資訊，另加目前的分頁檔大小。〔Claude 決定〕
> - F-23 程式：
>   - 所有應用程式依 commit 由大到小排列，欄位為名稱、行程數、commit、實際 RAM（工作集）。
>   - 每列可展開，列出組成它的行程，依執行檔名稱合併（例如「claude.exe ×14」）。node 行程再依啟動的套件區分（例如「node：playwright」）。〔Claude 決定〕
>   - 最後一列為「系統（不屬於任何程式）」。
> - F-24 尖峰紀錄：列出開機尖峰與每日尖峰紀錄（7.4），每筆顯示時間、commit 已用、commit 上限與當時的前五名應用程式。

**介面：**

- 使用：Task 4 的 `PeakState`；Task 5 的 `PanelModel.Build`（含 warning）；`AppGroup`、`AppComponent`、`PeakCsv.Unknown`。
- 產生：
  - `abstract class ObservableObject : INotifyPropertyChanged`
  - `ComponentRow(string Name, string CommitText, string WorkingSetText)`
  - `AppListRow`：`Key`、`IsUnattributed`、`Name`、`CountText`、`CommitText`、`WorkingSetText`、`Components`、可寫入的 `IsExpanded`
  - `PeakRow(string FirstText, string TimeText, string CommitText, string LimitText, string TopAppsText)`
  - `FullWindowModel(UsageThresholds thresholds, TimeZoneInfo zone)`；`Overview`、`PageFileText`、`Apps`（`ObservableCollection<AppListRow>`）、`BootPeaks`、`DailyPeaks`；`void Update(GroupedSnapshot grouped, PeakState peaks)`

- [ ] **Step 1：寫失敗的測試**

新增 `app/MemSweep.Core.Tests/FullWindowModelTests.cs`：

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

預期：建置失敗，錯誤訊息包含（實際輸出另有檔案路徑與重複的行）

```text
error CS0246: 找不到類型或命名空間名稱 'FullWindowModel' (是否遺漏了 using 指示詞或組件參考?)
```

- [ ] **Step 3：實作**

新增 `app/MemSweep.Core/ObservableObject.cs`：

```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MemSweep.Core;

/// <summary>屬性變動時通知介面。</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
```

新增 `app/MemSweep.Core/AppListRow.cs`：

```csharp
using System.Globalization;

namespace MemSweep.Core;

/// <summary>「程式」分頁明細的一列：同一執行檔合併後的行程，例如「claude.exe ×14」（F-23）。</summary>
public sealed record ComponentRow(string Name, string CommitText, string WorkingSetText);

/// <summary>
/// 「程式」分頁的一列（F-23）。完整視窗開著時每 2 秒原地更新，
/// 展開狀態與畫面上的物件才不會每次都重新建立。
/// </summary>
public sealed class AppListRow : ObservableObject
{
    private string _name = string.Empty;
    private string _countText = string.Empty;
    private string _commitText = string.Empty;
    private string _workingSetText = string.Empty;
    private IReadOnlyList<ComponentRow> _components = [];
    private bool _isExpanded;

    public AppListRow(string key, bool isUnattributed)
    {
        Key = key;
        IsUnattributed = isUnattributed;
    }

    /// <summary>辨識同一列用的鍵：應用程式名稱。</summary>
    public string Key { get; }

    public bool IsUnattributed { get; }

    public string Name { get => _name; private set => Set(ref _name, value); }

    public string CountText { get => _countText; private set => Set(ref _countText, value); }

    public string CommitText { get => _commitText; private set => Set(ref _commitText, value); }

    public string WorkingSetText { get => _workingSetText; private set => Set(ref _workingSetText, value); }

    public IReadOnlyList<ComponentRow> Components { get => _components; private set => Set(ref _components, value); }

    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }

    internal void Update(AppGroup app)
    {
        Name = app.Name;
        CountText = app.ProcessCount.ToString(CultureInfo.InvariantCulture);
        CommitText = FullWindowModel.Gigabytes(app.CommitBytes);
        WorkingSetText = FullWindowModel.Gigabytes(app.WorkingSetBytes);
        var components = app.Components
            .Select(c => new ComponentRow(
                c.Name + " ×" + c.ProcessCount.ToString(CultureInfo.InvariantCulture),
                FullWindowModel.Gigabytes(c.CommitBytes),
                FullWindowModel.Gigabytes(c.WorkingSetBytes)))
            .ToList();
        // 內容沒變就保留原本的清單，展開的明細才不會重建
        if (!components.SequenceEqual(Components))
        {
            Components = components;
        }
    }

    internal void UpdateUnattributed(long bytes)
    {
        Name = GroupedSnapshot.UnattributedName;
        CommitText = FullWindowModel.Gigabytes(bytes);
    }
}
```

新增 `app/MemSweep.Core/FullWindowModel.cs`：

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

    private readonly UsageThresholds _thresholds;
    private readonly TimeZoneInfo _zone;
    private PanelModel? _overview;
    private string _pageFileText = string.Empty;
    private PeakHistory? _history;
    private IReadOnlyList<PeakRow> _bootPeaks = [];
    private IReadOnlyList<PeakRow> _dailyPeaks = [];

    public FullWindowModel(UsageThresholds thresholds, TimeZoneInfo zone)
    {
        _thresholds = thresholds;
        _zone = zone;
    }

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
        Overview = PanelModel.Build(grouped, peaks.BootPeak, _thresholds, peaks.StorageWarning);
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

- [ ] **Step 4：執行測試，確認通過**

執行：`(cd app && dotnet test)`

預期：總計 193，失敗 0。

- [ ] **Step 5：Commit**

```bash
git add app/MemSweep.Core app/MemSweep.Core.Tests/FullWindowModelTests.cs
git status --short
git commit -F - <<'EOF'
新增完整視窗的顯示模型

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

- [ ] **Step 6：推送**（依「共同執行規則」先 fetch 並檢查）

---

### Task 7：完整視窗

**檔案：**

- 新增：`app/MemSweep.Desktop/SummaryView.xaml`、`SummaryView.xaml.cs`、`AppsView.xaml`、`AppsView.xaml.cs`、`PeaksView.xaml`、`PeaksView.xaml.cs`、`FullWindow.xaml`、`FullWindow.xaml.cs`
- 修改（整份換成）：`app/MemSweep.Desktop/FlyoutPanel.xaml`、`FlyoutPanel.xaml.cs`、`TrayController.cs`
- 新增：`app/MemSweep.Core.Tests/WpfLayout.cs`、`app/MemSweep.Core.Tests/FullWindowTests.cs`
- 修改（整份換成）：`app/MemSweep.Core.Tests/FlyoutTests.cs`、`app/MemSweep.Core.Tests/TrayIconRendererTests.cs`

**實作的條文：**

> - F-07 右鍵選單有「開啟小面板」、「完整清單…」、「設定…」、「結束 MemSweep」。〔Claude 決定〕

> - F-12 按鈕：
>   - 「完整清單…」：開啟完整視窗（7.3）。

> - F-20 一般視窗，可調整大小。關閉視窗只會收起，MemSweep 繼續常駐。
> - F-21 有三個分頁：總覽、程式、尖峰紀錄。

> - A-08 關閉小面板與完整視窗後，MemSweep 仍在系統匣；只有「結束 MemSweep」能結束它。

> - 右鍵選單（F-07）的項目隨 Phase 逐步出現：Phase 02 有「開啟小面板」「結束 MemSweep」，Phase 03 加「完整清單…」，Phase 04 加「設定…」。

> - 完整視窗（F-20～F-24）：一般視窗，三個分頁。第一次開啟時才建立，只在顯示時更新。關閉時真正關閉，並在 UI 空閒時以 `GCCollectionMode.Aggressive` 回收 MemSweep 自己的記憶體，下次開啟再建立：隱藏的視窗會保留整個畫面樹，Phase 03 原型實測隱藏後仍約 109 MB，關閉並回收後穩定在約 86 MB。「程式」分頁用 `ItemsControl`，每 2 秒原地更新各列而不整份重建；展開狀態保存在顯示模型，關閉再開仍在。

**介面：**

- 使用：Task 5 的 `PanelModel.Warning`、`TrayController.Update(GroupedSnapshot, PeakState)`；Task 6 的 `FullWindowModel`、`AppListRow`、`PeakRow`。
- 產生：
  - `SummaryView`（`WarningText`）；`FlyoutPanel.Summary`、`FlyoutPanel.FullListClicked`
  - `AppsView`、`PeaksView`（`BootEmpty`、`DailyEmpty`）
  - `FullWindow`：`ShowWindow()`；關閉時真正關閉
  - `TrayController.ShowFullWindow()`；完整視窗關閉後在 UI 空閒時執行 `GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true)`
  - 測試共用的 `WpfLayout.LaidOut`、`WpfLayout.Descendants`、`WpfLayout.Texts`

- [ ] **Step 1：寫失敗的測試**

新增 `app/MemSweep.Core.Tests/WpfLayout.cs`：

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MemSweep.Core.Tests;

/// <summary>在 STA 執行緒上不顯示視窗，直接排版 WPF 元件並走訪畫面樹。</summary>
internal static class WpfLayout
{
    public static T LaidOut<T>(T element, double width = double.PositiveInfinity)
        where T : FrameworkElement
    {
        element.Measure(new Size(width, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
        element.UpdateLayout();
        return element;
    }

    public static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    public static List<string> Texts(DependencyObject root) =>
        Descendants(root).OfType<TextBlock>().Select(t => t.Text).ToList();
}
```

新增 `app/MemSweep.Core.Tests/FullWindowTests.cs`：

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using MemSweep.Core;
using MemSweep.Desktop;

namespace MemSweep.Core.Tests;

// 多個 STA 執行緒同時初始化 WPF 介面元件時會互相鎖死，介面測試一律依序執行
[TestClass]
[DoNotParallelize]
public sealed class FullWindowTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time");
    private static readonly PeakState NoPeaks = new(null, PeakHistory.Empty, null);

    private static long Gb(double value) => MemoryMath.FromGigabytes(value);

    private static FullWindowModel ModelWithComponents()
    {
        var typical = ReportSamples.Typical();
        var claude = typical.Apps[0] with
        {
            Components = [new AppComponent("claude.exe", 14, Gb(2.0), Gb(1.0)), new AppComponent("node：@playwright/mcp", 2, Gb(0.4), Gb(0.3))],
        };
        var model = new FullWindowModel(UsageThresholds.Default, Zone);
        model.Update(typical with { Apps = [claude, .. typical.Apps.Skip(1)] }, NoPeaks);
        return model;
    }

    [STATestMethod]
    public void AppsViewListsEveryAppUnderTheColumnHeaders()
    {
        var view = WpfLayout.LaidOut(new AppsView { DataContext = ModelWithComponents() }, 700);

        var texts = WpfLayout.Texts(view);
        CollectionAssert.IsSubsetOf(
            new[] { "名稱", "行程數", "Commit", "實際 RAM", "Claude", "36", "3.2 GB", "1.6 GB", "Microsoft Edge", GroupedSnapshot.UnattributedName, "6.9 GB" },
            texts);
        CollectionAssert.DoesNotContain(texts, "claude.exe ×14");
    }

    [STATestMethod]
    public void TheToggleExpandsTheRowToShowItsComponents()
    {
        var model = ModelWithComponents();
        var view = WpfLayout.LaidOut(new AppsView { DataContext = model }, 700);

        WpfLayout.Descendants(view).OfType<ToggleButton>().First().IsChecked = true;
        view.UpdateLayout();

        Assert.IsTrue(model.Apps[0].IsExpanded);
        CollectionAssert.IsSubsetOf(new[] { "claude.exe ×14", "2.0 GB", "1.0 GB", "node：@playwright/mcp ×2" }, WpfLayout.Texts(view));
    }

    [STATestMethod]
    public void RowsWithoutComponentsHaveNoToggle()
    {
        var view = WpfLayout.LaidOut(new AppsView { DataContext = ModelWithComponents() }, 700);

        var toggles = WpfLayout.Descendants(view).OfType<ToggleButton>().ToList();

        Assert.HasCount(7, toggles);
        Assert.AreEqual(Visibility.Visible, toggles[0].Visibility);
        Assert.AreEqual(Visibility.Hidden, toggles[6].Visibility);
    }

    [STATestMethod]
    public void PeaksViewShowsEmptyTextWithoutRecords()
    {
        var model = new FullWindowModel(UsageThresholds.Default, Zone);
        model.Update(ReportSamples.Typical(), NoPeaks);

        var view = WpfLayout.LaidOut(new PeaksView { DataContext = model }, 700);

        Assert.AreEqual(Visibility.Visible, view.BootEmpty.Visibility);
        Assert.AreEqual(Visibility.Visible, view.DailyEmpty.Visibility);
        Assert.AreEqual("尚無紀錄", view.BootEmpty.Text);
    }

    [STATestMethod]
    public void PeaksViewListsBootAndDailyPeaks()
    {
        var time = new DateTimeOffset(2026, 10, 26, 14, 35, 2, TimeSpan.FromHours(8));
        var history = new PeakHistory(
            [new BootPeak(new DateTime(2026, 10, 26, 0, 30, 0, DateTimeKind.Utc), Gb(36.8), Gb(39.4), null, null)],
            [new DailyPeak(new DateOnly(2026, 10, 26), Gb(31.4), Gb(39.4), time, [new PeakApp("Claude", Gb(3.2))])]);
        var model = new FullWindowModel(UsageThresholds.Default, Zone);
        model.Update(ReportSamples.Typical(), new PeakState(history.Boots[0], history, null));

        var view = WpfLayout.LaidOut(new PeaksView { DataContext = model }, 700);

        Assert.AreEqual(Visibility.Collapsed, view.BootEmpty.Visibility);
        Assert.AreEqual(Visibility.Collapsed, view.DailyEmpty.Visibility);
        CollectionAssert.IsSubsetOf(
            new[]
            {
                "開機尖峰", "開機時間", "每日尖峰", "日期", "當時的前五名",
                "2026-10-26 08:30", "未知", "36.8 GB", "39.4 GB",
                "2026-10-26", "14:35", "31.4 GB", "Claude 3.2 GB",
            },
            WpfLayout.Texts(view));
    }

    [STATestMethod]
    public void SummaryUsesOneTimeBindingsAndFollowsANewDataContext()
    {
        var typical = ReportSamples.Typical();
        var summary = WpfLayout.LaidOut(
            new SummaryView { DataContext = PanelModel.Build(typical, null, UsageThresholds.Default, "data 資料夾無法寫入，尖峰紀錄已停用") },
            330);

        var bound = WpfLayout.Descendants(summary)
            .OfType<TextBlock>()
            .Select(t => BindingOperations.GetBinding(t, TextBlock.TextProperty))
            .OfType<Binding>()
            .ToList();
        Assert.IsGreaterThan(10, bound.Count);
        Assert.IsTrue(bound.All(b => b.Mode == BindingMode.OneTime));

        var lower = typical with { Snapshot = typical.Snapshot with { CommitUsedBytes = Gb(20.0) } };
        summary.DataContext = PanelModel.Build(lower, null, UsageThresholds.Default);
        summary.UpdateLayout();

        Assert.AreEqual(Visibility.Collapsed, summary.WarningText.Visibility);
        CollectionAssert.Contains(WpfLayout.Texts(summary), "20.0 / 39.4 GB（51%）");
    }

    [STATestMethod]
    public void TheOverviewTabShowsTheSummaryAndThePageFileSize()
    {
        var model = ModelWithComponents();
        var window = new FullWindow { DataContext = model };

        var content = WpfLayout.LaidOut((FrameworkElement)window.Content, 700);

        var texts = WpfLayout.Texts(content);
        CollectionAssert.IsSubsetOf(new[] { "總覽", "程式", "尖峰紀錄", "Commit", "31.4 / 39.4 GB（80%）", "分頁檔大小：8.0 GB" }, texts);
        window.Close();
    }
}
```

`app/MemSweep.Core.Tests/FlyoutTests.cs` 整份換成：

```csharp
using System.Windows;
using System.Windows.Controls;
using MemSweep.Core;
using MemSweep.Desktop;

namespace MemSweep.Core.Tests;

// 多個 STA 執行緒同時初始化 WPF 介面元件時會互相鎖死，介面測試一律依序執行
[TestClass]
[DoNotParallelize]
public sealed class FlyoutTests
{
    private static FlyoutPanel LaidOutPanel(PanelModel model) => WpfLayout.LaidOut(new FlyoutPanel { DataContext = model });

    [TestMethod]
    public void PlacesTheFlyoutAtTheBottomRightOfTheWorkArea()
    {
        var (left, top) = FlyoutPlacement.Place(areaRight: 1920, areaBottom: 1032, width: 330, height: 400);

        Assert.AreEqual(1578.0, left);
        Assert.AreEqual(620.0, top);
    }

    [TestMethod]
    public void ThemeFollowsAppsUseLightTheme()
    {
        Assert.AreEqual(ThemeKind.Dark, ThemeReader.FromRegistryValue(0));
        Assert.AreEqual(ThemeKind.Light, ThemeReader.FromRegistryValue(1));
    }

    [TestMethod]
    public void MissingOrUnexpectedThemeValueIsLight()
    {
        Assert.AreEqual(ThemeKind.Light, ThemeReader.FromRegistryValue(null));
        Assert.AreEqual(ThemeKind.Light, ThemeReader.FromRegistryValue("0"));
    }

    [STATestMethod]
    public void PanelShowsGaugesPeakRowsAndButtons()
    {
        var peak = new BootPeak(
            new DateTime(2026, 10, 26, 0, 30, 0, DateTimeKind.Utc),
            MemoryMath.FromGigabytes(36.8),
            MemoryMath.FromGigabytes(39.4),
            new DateTimeOffset(2026, 10, 26, 12, 10, 0, TimeSpan.FromHours(8)),
            [new PeakApp("Claude", 1)]);
        var panel = LaidOutPanel(PanelModel.Build(ReportSamples.Typical(), peak, UsageThresholds.Default));

        var texts = WpfLayout.Descendants(panel).OfType<TextBlock>().Select(t => t.Text).ToList();
        CollectionAssert.IsSubsetOf(
            new[]
            {
                "Commit",
                "31.4 / 39.4 GB（80%）",
                "實體 RAM",
                "20.5 / 31.4 GB（65%）",
                "開機以來 commit 尖峰：36.8 GB（12:10）",
                "Claude",
                "36",
                "3.2 GB",
                "系統（不屬於任何程式）",
                "6.9 GB",
            },
            texts);
        var buttons = WpfLayout.Descendants(panel).OfType<Button>().Select(b => b.Content as string).ToList();
        CollectionAssert.AreEqual(new[] { "完整清單…", "複製報告", "結束 MemSweep" }, buttons);
    }

    [STATestMethod]
    public void PanelShowsTheStorageWarningOnlyWhenThereIsOne()
    {
        const string warning = "peaks-daily.csv 無法讀取，尖峰紀錄已停用";
        var withWarning = LaidOutPanel(PanelModel.Build(ReportSamples.Typical(), null, UsageThresholds.Default, warning));
        var withoutWarning = LaidOutPanel(PanelModel.Build(ReportSamples.Typical(), null, UsageThresholds.Default));

        Assert.AreEqual(Visibility.Visible, withWarning.Summary.WarningText.Visibility);
        Assert.AreEqual(warning, withWarning.Summary.WarningText.Text);
        Assert.AreEqual(Visibility.Collapsed, withoutWarning.Summary.WarningText.Visibility);
    }

    [STATestMethod]
    public void PanelListsTopFivePlusUnattributed()
    {
        var panel = LaidOutPanel(PanelModel.Build(ReportSamples.Typical(), null, UsageThresholds.Default));

        var rows = WpfLayout.Descendants(panel).OfType<ContentPresenter>().Where(c => c.Content is PanelRow).ToList();

        Assert.HasCount(6, rows);
    }
}
```

`app/MemSweep.Core.Tests/TrayIconRendererTests.cs` 整份換成：

```csharp
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using MemSweep.Core;
using MemSweep.Desktop;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class TrayIconRendererTests
{
    private const uint UserObjects = 1;

    [DllImport("user32.dll")]
    private static extern uint GetGuiResources(nint process, uint flags);

    private static int CountPixels(Bitmap bitmap, Func<Color, bool> predicate)
    {
        int count = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (predicate(bitmap.GetPixel(x, y)))
                {
                    count++;
                }
            }
        }
        return count;
    }

    private static bool Near(Color pixel, Color target) =>
        pixel.A > 200
        && Math.Abs(pixel.R - target.R) < 40
        && Math.Abs(pixel.G - target.G) < 40
        && Math.Abs(pixel.B - target.B) < 40;

    [TestMethod]
    [DataRow(16)]
    [DataRow(24)]
    [DataRow(32)]
    public void RendersAnIconOfTheRequestedSize(int size)
    {
        using var icon = TrayIconRenderer.Render("80", UsageLevel.Orange, size);

        Assert.AreEqual(size, icon.Width);
        Assert.AreEqual(size, icon.Height);
    }

    [TestMethod]
    [DataRow("80", UsageLevel.Orange)]
    [DataRow("42", UsageLevel.Green)]
    [DataRow("100", UsageLevel.Red)]
    public void DrawsTheTextInTheLevelColorOnATransparentBackground(string text, UsageLevel level)
    {
        using var icon = TrayIconRenderer.Render(text, level, 32);
        using var bitmap = icon.ToBitmap();
        var color = TrayIconRenderer.ColorOf(level);

        Assert.IsGreaterThan(20, CountPixels(bitmap, p => Near(p, color)));
        foreach (var other in Enum.GetValues<UsageLevel>().Where(l => l != level))
        {
            Assert.AreEqual(0, CountPixels(bitmap, p => Near(p, TrayIconRenderer.ColorOf(other))));
        }
        Assert.AreEqual(0, bitmap.GetPixel(0, 0).A);
    }

    [TestMethod]
    public void LevelColorsAreDistinct()
    {
        var colors = Enum.GetValues<UsageLevel>().Select(TrayIconRenderer.ColorOf).Distinct().ToList();

        Assert.HasCount(3, colors);
    }

    // 計算的是整個測試行程的 USER 物件，其他測試同時建立 WPF 元件也會增加，所以不和其他測試並行
    [TestMethod]
    [DoNotParallelize]
    public void RenderingManyIconsDoesNotLeakHandles()
    {
        // 每 2 秒換一次圖示，漏掉 DestroyIcon 時 USER 物件會一直增加
        using var process = Process.GetCurrentProcess();
        long before = GetGuiResources(process.Handle, UserObjects);

        for (int i = 0; i < 300; i++)
        {
            using var icon = TrayIconRenderer.Render((i % 101).ToString(), UsageLevel.Red, 32);
        }

        long after = GetGuiResources(process.Handle, UserObjects);
        Assert.IsLessThan(30L, after - before);
    }
}
```

- [ ] **Step 2：執行測試，確認失敗**

執行：`(cd app && dotnet test)`

預期：建置失敗，錯誤訊息包含（實際輸出另有檔案路徑與重複的行）

```text
error CS0246: 找不到類型或命名空間名稱 'AppsView' (是否遺漏了 using 指示詞或組件參考?)
error CS1061: 'FlyoutPanel' 未包含 'Summary' 的定義，也找不到可接受類型 'FlyoutPanel' 第一個引數的可存取擴充方法 'Summary' (是否遺漏 using 指示詞或組件參考?)
error CS0246: 找不到類型或命名空間名稱 'PeaksView' (是否遺漏了 using 指示詞或組件參考?)
error CS1061: 'T' 未包含 'BootEmpty' 的定義，也找不到可接受類型 'T' 第一個引數的可存取擴充方法 'BootEmpty' (是否遺漏 using 指示詞或組件參考?)
error CS1061: 'T' 未包含 'DailyEmpty' 的定義，也找不到可接受類型 'T' 第一個引數的可存取擴充方法 'DailyEmpty' (是否遺漏 using 指示詞或組件參考?)
error CS0246: 找不到類型或命名空間名稱 'SummaryView' (是否遺漏了 using 指示詞或組件參考?)
error CS1061: 'T' 未包含 'WarningText' 的定義，也找不到可接受類型 'T' 第一個引數的可存取擴充方法 'WarningText' (是否遺漏 using 指示詞或組件參考?)
error CS0246: 找不到類型或命名空間名稱 'FullWindow' (是否遺漏了 using 指示詞或組件參考?)
```

- [ ] **Step 3：實作**

新增 `app/MemSweep.Desktop/SummaryView.xaml`：

```xml
<UserControl x:Class="MemSweep.Desktop.SummaryView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <!-- 小面板與完整視窗「總覽」共用的內容（F-11、F-22）；DataContext 是 PanelModel。
       每次更新都換成新的 PanelModel，所以繫結都用 OneTime，不必追蹤屬性變動 -->
  <UserControl.Resources>
    <Style x:Key="FillStyle" TargetType="Border">
      <Setter Property="CornerRadius" Value="4" />
      <Setter Property="RenderTransformOrigin" Value="0,0.5" />
    </Style>
  </UserControl.Resources>

  <StackPanel>
    <!-- commit 量表：顏色依門檻（F-03） -->
    <DockPanel>
      <TextBlock DockPanel.Dock="Right" Text="{Binding Commit.ValueText, Mode=OneTime}" />
      <TextBlock Text="{Binding Commit.Label, Mode=OneTime}" FontWeight="SemiBold" />
    </DockPanel>
    <Grid Height="8" Margin="0,4,0,10">
      <Border Background="{DynamicResource TrackBrush}" CornerRadius="4" />
      <Border>
        <Border.Style>
          <Style TargetType="Border" BasedOn="{StaticResource FillStyle}">
            <Setter Property="Background" Value="{DynamicResource GreenBrush}" />
            <Style.Triggers>
              <DataTrigger Binding="{Binding Commit.Level, Mode=OneTime}" Value="Orange">
                <Setter Property="Background" Value="{DynamicResource OrangeBrush}" />
              </DataTrigger>
              <DataTrigger Binding="{Binding Commit.Level, Mode=OneTime}" Value="Red">
                <Setter Property="Background" Value="{DynamicResource RedBrush}" />
              </DataTrigger>
            </Style.Triggers>
          </Style>
        </Border.Style>
        <Border.RenderTransform>
          <ScaleTransform ScaleX="{Binding Commit.Fraction, Mode=OneTime}" />
        </Border.RenderTransform>
      </Border>
    </Grid>

    <!-- 實體 RAM 量表：固定用強調色 -->
    <DockPanel>
      <TextBlock DockPanel.Dock="Right" Text="{Binding Ram.ValueText, Mode=OneTime}" />
      <TextBlock Text="{Binding Ram.Label, Mode=OneTime}" FontWeight="SemiBold" />
    </DockPanel>
    <Grid Height="8" Margin="0,4,0,10">
      <Border Background="{DynamicResource TrackBrush}" CornerRadius="4" />
      <Border Style="{StaticResource FillStyle}" Background="{DynamicResource AccentBrush}">
        <Border.RenderTransform>
          <ScaleTransform ScaleX="{Binding Ram.Fraction, Mode=OneTime}" />
        </Border.RenderTransform>
      </Border>
    </Grid>

    <TextBlock Text="{Binding PeakText, Mode=OneTime}" Foreground="{DynamicResource MutedTextBrush}" Margin="0,0,0,8" />

    <!-- 尖峰紀錄停用的原因（Design 3.9），沒有問題時不顯示 -->
    <TextBlock x:Name="WarningText"
               Text="{Binding Warning, Mode=OneTime}"
               Foreground="{DynamicResource OrangeBrush}"
               TextWrapping="Wrap"
               Margin="0,0,0,8">
      <TextBlock.Style>
        <Style TargetType="TextBlock">
          <Style.Triggers>
            <DataTrigger Binding="{Binding Warning, Mode=OneTime}" Value="{x:Null}">
              <Setter Property="Visibility" Value="Collapsed" />
            </DataTrigger>
          </Style.Triggers>
        </Style>
      </TextBlock.Style>
    </TextBlock>

    <!-- 前五名與「系統（不屬於任何程式）」 -->
    <ItemsControl ItemsSource="{Binding Rows, Mode=OneTime}">
      <ItemsControl.ItemTemplate>
        <DataTemplate>
          <Border x:Name="Row" Padding="0,4" BorderBrush="{DynamicResource PanelBorderBrush}" BorderThickness="0">
            <Grid>
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="80" />
                <ColumnDefinition Width="54" />
              </Grid.ColumnDefinitions>
              <TextBlock x:Name="AppName" Text="{Binding Name, Mode=OneTime}" TextTrimming="CharacterEllipsis" />
              <TextBlock Grid.Column="1"
                         Text="{Binding CountText, Mode=OneTime}"
                         Foreground="{DynamicResource MutedTextBrush}"
                         Margin="6,0,0,0" />
              <Grid Grid.Column="2" Height="6" Margin="10,0,6,0">
                <Border x:Name="Bar" Background="{DynamicResource BarBrush}" CornerRadius="3" RenderTransformOrigin="0,0.5">
                  <Border.RenderTransform>
                    <ScaleTransform ScaleX="{Binding BarFraction, Mode=OneTime}" />
                  </Border.RenderTransform>
                </Border>
              </Grid>
              <TextBlock Grid.Column="3" Text="{Binding GigabytesText, Mode=OneTime}" HorizontalAlignment="Right" />
            </Grid>
          </Border>
          <DataTemplate.Triggers>
            <DataTrigger Binding="{Binding IsUnattributed, Mode=OneTime}" Value="True">
              <Setter TargetName="AppName" Property="FontStyle" Value="Italic" />
              <Setter TargetName="AppName" Property="Foreground" Value="{DynamicResource MutedTextBrush}" />
              <Setter TargetName="Bar" Property="Background" Value="{DynamicResource UnattributedBarBrush}" />
              <Setter TargetName="Row" Property="BorderThickness" Value="0,1,0,0" />
              <Setter TargetName="Row" Property="Margin" Value="0,4,0,0" />
            </DataTrigger>
          </DataTemplate.Triggers>
        </DataTemplate>
      </ItemsControl.ItemTemplate>
    </ItemsControl>
  </StackPanel>
</UserControl>
```

新增 `app/MemSweep.Desktop/SummaryView.xaml.cs`：

```csharp
using System.Windows.Controls;

namespace MemSweep.Desktop;

/// <summary>commit 與 RAM 量表、開機尖峰、尖峰紀錄的警告、前五名（F-11）；小面板與完整視窗「總覽」共用。</summary>
public partial class SummaryView : UserControl
{
    public SummaryView()
    {
        InitializeComponent();
    }
}
```

`app/MemSweep.Desktop/FlyoutPanel.xaml` 整份換成：

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

`app/MemSweep.Desktop/FlyoutPanel.xaml.cs` 整份換成：

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace MemSweep.Desktop;

/// <summary>小面板的內容（F-11、F-12）；DataContext 是 <see cref="Core.PanelModel"/>。</summary>
public partial class FlyoutPanel : UserControl
{
    private const string CopyLabel = "複製報告";

    private readonly DispatcherTimer _copyFeedback;

    public FlyoutPanel()
    {
        InitializeComponent();
        _copyFeedback = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _copyFeedback.Tick += (_, _) =>
        {
            _copyFeedback.Stop();
            CopyButton.Content = CopyLabel;
        };
    }

    public event Action? FullListClicked;

    public event Action? CopyClicked;

    public event Action? ExitClicked;

    /// <summary>按下「複製報告」後，按鈕文字短暫顯示結果。</summary>
    public void ShowCopyResult(bool succeeded)
    {
        CopyButton.Content = succeeded ? "已複製" : "複製失敗";
        _copyFeedback.Stop();
        _copyFeedback.Start();
    }

    private void FullListButton_Click(object sender, RoutedEventArgs e) => FullListClicked?.Invoke();

    private void CopyButton_Click(object sender, RoutedEventArgs e) => CopyClicked?.Invoke();

    private void ExitButton_Click(object sender, RoutedEventArgs e) => ExitClicked?.Invoke();
}
```

新增 `app/MemSweep.Desktop/AppsView.xaml`：

```xml
<UserControl x:Class="MemSweep.Desktop.AppsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <!-- 「程式」分頁（F-23）；DataContext 是 FullWindowModel，列會原地更新，所以用一般的 OneWay 繫結 -->
  <UserControl.Resources>
    <BooleanToVisibilityConverter x:Key="VisibleWhenTrue" />
    <Style x:Key="ExpandToggle" TargetType="ToggleButton">
      <Setter Property="Foreground" Value="{DynamicResource MutedTextBrush}" />
      <Setter Property="Cursor" Value="Hand" />
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="ToggleButton">
            <Border Background="Transparent">
              <TextBlock x:Name="Glyph" Text="▸" HorizontalAlignment="Center" VerticalAlignment="Center" />
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsChecked" Value="True">
                <Setter TargetName="Glyph" Property="Text" Value="▾" />
              </Trigger>
              <Trigger Property="IsMouseOver" Value="True">
                <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key="HeaderText" TargetType="TextBlock">
      <Setter Property="Foreground" Value="{DynamicResource MutedTextBrush}" />
      <Setter Property="FontWeight" Value="SemiBold" />
    </Style>
  </UserControl.Resources>

  <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
    <StackPanel Margin="0,12,0,0">
      <!-- 欄位：展開、名稱、行程數、commit、實際 RAM（工作集） -->
      <Grid Margin="0,0,0,4">
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width="20" />
          <ColumnDefinition Width="*" />
          <ColumnDefinition Width="64" />
          <ColumnDefinition Width="84" />
          <ColumnDefinition Width="84" />
        </Grid.ColumnDefinitions>
        <TextBlock Grid.Column="1" Text="名稱" Style="{StaticResource HeaderText}" />
        <TextBlock Grid.Column="2" Text="行程數" Style="{StaticResource HeaderText}" HorizontalAlignment="Right" />
        <TextBlock Grid.Column="3" Text="Commit" Style="{StaticResource HeaderText}" HorizontalAlignment="Right" />
        <TextBlock Grid.Column="4" Text="實際 RAM" Style="{StaticResource HeaderText}" HorizontalAlignment="Right" />
      </Grid>
      <Border Height="1" Background="{DynamicResource PanelBorderBrush}" Margin="0,0,0,4" />

      <ItemsControl ItemsSource="{Binding Apps}">
        <ItemsControl.ItemTemplate>
          <DataTemplate>
            <StackPanel x:Name="Row">
              <Grid Margin="0,3">
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width="20" />
                  <ColumnDefinition Width="*" />
                  <ColumnDefinition Width="64" />
                  <ColumnDefinition Width="84" />
                  <ColumnDefinition Width="84" />
                </Grid.ColumnDefinitions>
                <ToggleButton x:Name="Toggle" IsChecked="{Binding IsExpanded}" Style="{StaticResource ExpandToggle}" />
                <TextBlock x:Name="AppName" Grid.Column="1" Text="{Binding Name}" TextTrimming="CharacterEllipsis" />
                <TextBlock Grid.Column="2" Text="{Binding CountText}" Foreground="{DynamicResource MutedTextBrush}" HorizontalAlignment="Right" />
                <TextBlock Grid.Column="3" Text="{Binding CommitText}" HorizontalAlignment="Right" />
                <TextBlock Grid.Column="4" Text="{Binding WorkingSetText}" HorizontalAlignment="Right" />
              </Grid>

              <!-- 明細：依執行檔名稱合併，node 再依套件區分 -->
              <ItemsControl ItemsSource="{Binding Components}"
                            Visibility="{Binding IsExpanded, Converter={StaticResource VisibleWhenTrue}}"
                            Margin="20,0,0,4">
                <ItemsControl.ItemTemplate>
                  <DataTemplate>
                    <Grid Margin="0,1">
                      <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="84" />
                        <ColumnDefinition Width="84" />
                      </Grid.ColumnDefinitions>
                      <TextBlock Text="{Binding Name}" Foreground="{DynamicResource MutedTextBrush}" TextTrimming="CharacterEllipsis" />
                      <TextBlock Grid.Column="1" Text="{Binding CommitText}" Foreground="{DynamicResource MutedTextBrush}" HorizontalAlignment="Right" />
                      <TextBlock Grid.Column="2" Text="{Binding WorkingSetText}" Foreground="{DynamicResource MutedTextBrush}" HorizontalAlignment="Right" />
                    </Grid>
                  </DataTemplate>
                </ItemsControl.ItemTemplate>
              </ItemsControl>
            </StackPanel>
            <DataTemplate.Triggers>
              <DataTrigger Binding="{Binding Components.Count}" Value="0">
                <Setter TargetName="Toggle" Property="Visibility" Value="Hidden" />
              </DataTrigger>
              <DataTrigger Binding="{Binding IsUnattributed}" Value="True">
                <Setter TargetName="AppName" Property="FontStyle" Value="Italic" />
                <Setter TargetName="AppName" Property="Foreground" Value="{DynamicResource MutedTextBrush}" />
                <Setter TargetName="Row" Property="Margin" Value="0,4,0,0" />
              </DataTrigger>
            </DataTemplate.Triggers>
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>
    </StackPanel>
  </ScrollViewer>
</UserControl>
```

新增 `app/MemSweep.Desktop/AppsView.xaml.cs`：

```csharp
using System.Windows.Controls;

namespace MemSweep.Desktop;

/// <summary>完整視窗的「程式」分頁：所有應用程式與可展開的明細（F-23）。</summary>
public partial class AppsView : UserControl
{
    public AppsView()
    {
        InitializeComponent();
    }
}
```

新增 `app/MemSweep.Desktop/PeaksView.xaml`：

```xml
<UserControl x:Class="MemSweep.Desktop.PeaksView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <!-- 「尖峰紀錄」分頁（F-24）；DataContext 是 FullWindowModel。清單只在紀錄有變動時整份換掉，所以用 OneWay -->
  <UserControl.Resources>
    <Style x:Key="SectionTitle" TargetType="TextBlock">
      <Setter Property="FontWeight" Value="SemiBold" />
      <Setter Property="FontSize" Value="13" />
      <Setter Property="Margin" Value="0,0,0,6" />
    </Style>
    <Style x:Key="HeaderText" TargetType="TextBlock">
      <Setter Property="Foreground" Value="{DynamicResource MutedTextBrush}" />
      <Setter Property="FontWeight" Value="SemiBold" />
    </Style>
    <Style x:Key="EmptyText" TargetType="TextBlock">
      <Setter Property="Text" Value="尚無紀錄" />
      <Setter Property="Foreground" Value="{DynamicResource MutedTextBrush}" />
      <Setter Property="Margin" Value="0,2,0,0" />
      <Setter Property="Visibility" Value="Collapsed" />
    </Style>
    <DataTemplate x:Key="PeakRowTemplate">
      <Grid Margin="0,3">
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width="120" />
          <ColumnDefinition Width="120" />
          <ColumnDefinition Width="84" />
          <ColumnDefinition Width="84" />
          <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>
        <TextBlock Text="{Binding FirstText}" />
        <TextBlock Grid.Column="1" Text="{Binding TimeText}" />
        <TextBlock Grid.Column="2" Text="{Binding CommitText}" HorizontalAlignment="Right" />
        <TextBlock Grid.Column="3" Text="{Binding LimitText}" Foreground="{DynamicResource MutedTextBrush}" HorizontalAlignment="Right" />
        <TextBlock Grid.Column="4" Text="{Binding TopAppsText}" Foreground="{DynamicResource MutedTextBrush}" TextWrapping="Wrap" Margin="16,0,0,0" />
      </Grid>
    </DataTemplate>
  </UserControl.Resources>

  <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
    <StackPanel Margin="0,12,0,0">
      <TextBlock Text="開機尖峰" Style="{StaticResource SectionTitle}" />
      <Grid Margin="0,0,0,4">
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width="120" />
          <ColumnDefinition Width="120" />
          <ColumnDefinition Width="84" />
          <ColumnDefinition Width="84" />
          <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>
        <TextBlock Text="開機時間" Style="{StaticResource HeaderText}" />
        <TextBlock Grid.Column="1" Text="尖峰時間" Style="{StaticResource HeaderText}" />
        <TextBlock Grid.Column="2" Text="Commit 已用" Style="{StaticResource HeaderText}" HorizontalAlignment="Right" />
        <TextBlock Grid.Column="3" Text="上限" Style="{StaticResource HeaderText}" HorizontalAlignment="Right" />
        <TextBlock Grid.Column="4" Text="當時的前五名" Style="{StaticResource HeaderText}" Margin="16,0,0,0" />
      </Grid>
      <Border Height="1" Background="{DynamicResource PanelBorderBrush}" Margin="0,0,0,4" />
      <ItemsControl x:Name="BootList" ItemsSource="{Binding BootPeaks}" ItemTemplate="{StaticResource PeakRowTemplate}" />
      <TextBlock x:Name="BootEmpty">
        <TextBlock.Style>
          <Style TargetType="TextBlock" BasedOn="{StaticResource EmptyText}">
            <Style.Triggers>
              <DataTrigger Binding="{Binding BootPeaks.Count}" Value="0">
                <Setter Property="Visibility" Value="Visible" />
              </DataTrigger>
            </Style.Triggers>
          </Style>
        </TextBlock.Style>
      </TextBlock>

      <TextBlock Text="每日尖峰" Style="{StaticResource SectionTitle}" Margin="0,20,0,6" />
      <Grid Margin="0,0,0,4">
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width="120" />
          <ColumnDefinition Width="120" />
          <ColumnDefinition Width="84" />
          <ColumnDefinition Width="84" />
          <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>
        <TextBlock Text="日期" Style="{StaticResource HeaderText}" />
        <TextBlock Grid.Column="1" Text="尖峰時間" Style="{StaticResource HeaderText}" />
        <TextBlock Grid.Column="2" Text="Commit 已用" Style="{StaticResource HeaderText}" HorizontalAlignment="Right" />
        <TextBlock Grid.Column="3" Text="上限" Style="{StaticResource HeaderText}" HorizontalAlignment="Right" />
        <TextBlock Grid.Column="4" Text="當時的前五名" Style="{StaticResource HeaderText}" Margin="16,0,0,0" />
      </Grid>
      <Border Height="1" Background="{DynamicResource PanelBorderBrush}" Margin="0,0,0,4" />
      <ItemsControl x:Name="DailyList" ItemsSource="{Binding DailyPeaks}" ItemTemplate="{StaticResource PeakRowTemplate}" />
      <TextBlock x:Name="DailyEmpty">
        <TextBlock.Style>
          <Style TargetType="TextBlock" BasedOn="{StaticResource EmptyText}">
            <Style.Triggers>
              <DataTrigger Binding="{Binding DailyPeaks.Count}" Value="0">
                <Setter Property="Visibility" Value="Visible" />
              </DataTrigger>
            </Style.Triggers>
          </Style>
        </TextBlock.Style>
      </TextBlock>
    </StackPanel>
  </ScrollViewer>
</UserControl>
```

新增 `app/MemSweep.Desktop/PeaksView.xaml.cs`：

```csharp
using System.Windows.Controls;

namespace MemSweep.Desktop;

/// <summary>完整視窗的「尖峰紀錄」分頁：開機尖峰與每日尖峰（F-24）。</summary>
public partial class PeaksView : UserControl
{
    public PeaksView()
    {
        InitializeComponent();
    }
}
```

新增 `app/MemSweep.Desktop/FullWindow.xaml`：

```xml
<Window x:Class="MemSweep.Desktop.FullWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:MemSweep.Desktop"
        Title="MemSweep"
        Width="760"
        Height="560"
        MinWidth="520"
        MinHeight="360"
        WindowStartupLocation="CenterScreen"
        FontFamily="Segoe UI, Microsoft JhengHei UI"
        FontSize="12"
        Background="{DynamicResource PanelBackgroundBrush}"
        Foreground="{DynamicResource TextBrush}">
  <!-- 完整視窗（F-20、F-21）；DataContext 是 FullWindowModel -->
  <Window.Resources>
    <!-- 自訂分頁標籤，深色模式下才不會出現系統預設的白色標籤 -->
    <Style TargetType="TabItem">
      <Setter Property="Foreground" Value="{DynamicResource MutedTextBrush}" />
      <Setter Property="Cursor" Value="Hand" />
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="TabItem">
            <Border x:Name="Tab" Padding="12,6" Margin="0,0,4,0" Background="Transparent" BorderBrush="Transparent" BorderThickness="0,0,0,2">
              <ContentPresenter ContentSource="Header" />
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsSelected" Value="True">
                <Setter TargetName="Tab" Property="BorderBrush" Value="{DynamicResource AccentBrush}" />
                <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />
              </Trigger>
              <Trigger Property="IsMouseOver" Value="True">
                <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
  </Window.Resources>

  <TabControl Margin="12" Padding="0" Background="Transparent" BorderThickness="0">
    <TabItem Header="總覽">
      <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
        <StackPanel Margin="0,12,0,0" MaxWidth="560" HorizontalAlignment="Left">
          <local:SummaryView DataContext="{Binding Overview}" />
          <TextBlock Text="{Binding PageFileText}" Foreground="{DynamicResource MutedTextBrush}" Margin="0,8,0,0" />
        </StackPanel>
      </ScrollViewer>
    </TabItem>
    <TabItem Header="程式">
      <local:AppsView />
    </TabItem>
    <TabItem Header="尖峰紀錄">
      <local:PeaksView />
    </TabItem>
  </TabControl>
</Window>
```

新增 `app/MemSweep.Desktop/FullWindow.xaml.cs`：

```csharp
using System.Windows;

namespace MemSweep.Desktop;

/// <summary>
/// 完整視窗（F-20）：一般視窗，可調整大小。關閉時真正關閉並釋放，MemSweep 繼續常駐（A-08）；
/// 下次開啟由 <see cref="TrayController"/> 重新建立（Design 3.6）。
/// </summary>
public partial class FullWindow : Window
{
    public FullWindow()
    {
        InitializeComponent();
    }

    public void ShowWindow()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Show();
        Activate();
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

/// <summary>系統匣圖示、提示文字、右鍵選單、小面板與完整視窗（F-01～F-24）。必須在 UI 執行緒上使用。</summary>
internal sealed class TrayController : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private FlyoutWindow? _flyout;
    private FullWindow? _fullWindow;
    private FullWindowModel? _fullModel;
    private bool _flyoutRequested;
    private Icon? _icon;
    private string? _iconKey;
    private GroupedSnapshot? _latest;
    private PeakState? _latestPeaks;

    public TrayController()
    {
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
        var level = UsageThresholds.Default.LevelOf(TrayText.CommitPercent(snapshot));
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
        PanelModel.Build(_latest!, _latestPeaks?.BootPeak, UsageThresholds.Default, _latestPeaks?.StorageWarning);

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
            _fullModel ??= new FullWindowModel(UsageThresholds.Default, TimeZoneInfo.Local);
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

預期：總計 200，失敗 0。

- [ ] **Step 5：Commit**

```bash
git add app/MemSweep.Desktop app/MemSweep.Core.Tests
git status --short
git commit -F - <<'EOF'
新增完整視窗

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

- [ ] **Step 6：推送**（依「共同執行規則」先 fetch 並檢查）

---

### Task 8：程式圖示與自訂標題列

**檔案：**

- 新增：`app/MemSweep.Desktop/Assets/MemSweep.ico`（二進位檔）、`app/MemSweep.Desktop/AppIcon.cs`、`TitleBar.xaml`、`TitleBar.xaml.cs`
- 修改（整份換成）：`app/MemSweep.Desktop/MemSweep.Desktop.csproj`、`app/MemSweep.Cli/MemSweep.Cli.csproj`、`app/MemSweep.Desktop/Native/User32.cs`、`FullWindow.xaml`、`AppsView.xaml`、`PeaksView.xaml`、`Themes/Light.xaml`、`Themes/Dark.xaml`
- 新增：`app/MemSweep.Core.Tests/TitleBarTests.cs`

**實作的條文：**

> - F-62 MemSweep.exe 與 memsweep-cli.exe 使用 MemSweep 專屬的程式圖示，不使用預設圖示。有標題列的視窗使用自訂標題列，不使用系統預設的標題列。

> - 程式圖示（F-62）：`MemSweep.Desktop/Assets/MemSweep.ico`，深藍圓角方塊上的 240 度 commit 量表，綠、橘、紅三段依預設門檻 70%、85% 分段（配色同深色主題），白色指針指向 80%。含 16、20、24、32、40、48、64、256 px 八格，每格都是 PNG。兩支 exe 以 `ApplicationIcon` 內嵌，自訂標題列依 DPI 取用最接近的一格；系統匣仍顯示數字（F-02）。

> - 自訂標題列（F-62）：視窗以 `WindowChrome` 取代系統標題列（`CaptionHeight` 32、`GlassFrameThickness` 0），拖曳、雙擊最大化、邊緣調整大小、右鍵系統選單與貼齊仍由系統處理。`TitleBar` 顯示圖示、視窗標題與最小化、最大化（還原）、關閉三個按鈕（Segoe Fluent Icons 字型），顏色跟著深淺色，滑過關閉按鈕時為紅底白字，視窗不在前景時標題變淡。最大化時內容往內縮 `SM_CXSIZEFRAME` + `SM_CXPADDEDBORDER`（WPF 的 `SystemParameters` 不含邊框間距），標題列才不會超出螢幕。不提供 Windows 11 滑過最大化按鈕時的貼齊版面選單，可用 Win+Z 或拖曳到螢幕邊緣。完整視窗的捲軸也改成跟著深淺色的細捲軸。設定視窗（Phase 04）沿用同一個 `TitleBar`。

**介面：**

- 使用：Task 7 的 `FullWindow`、`AppsView`、`PeaksView`；`User32`（Phase 02）。
- 產生：
  - `AppIcon.Source`、`AppIcon.PickSize(IReadOnlyList<int> sizes, int pixels)`、`AppIcon.Frames()`、`AppIcon.Frame(int pixels)`
  - `TitleBar`（`TitleText`、`MinimizeButton`、`MaximizeButton`、`CloseButton`、`IconImage`）、`TitleBar.MaximizedMargin`、`TitleBar.Inset(int sizeFrameX, int sizeFrameY, int paddedBorder, uint dpi)`
  - `FullWindow.Caption`、`FullWindow.Frame`；`User32.GetSystemMetrics`、`User32.GetDpiForSystem`
  - 主題資源 `CloseHoverBrush`、`ScrollThumbBrush`、`ScrollThumbHoverBrush`（Phase 04 的設定視窗也會用到 `TitleBar`）

- [ ] **Step 1：寫失敗的測試**

新增 `app/MemSweep.Core.Tests/TitleBarTests.cs`：

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

預期：建置失敗，錯誤訊息包含（實際輸出另有檔案路徑與重複的行）

```text
error CS1061: 'FullWindow' 未包含 'Caption' 的定義，也找不到可接受類型 'FullWindow' 第一個引數的可存取擴充方法 'Caption' (是否遺漏 using 指示詞或組件參考?)
error CS1503: 引數 2: 無法從 '方法群組' 轉換成 'System.Func<?, bool>'
error CS0103: 名稱 'TitleBar' 不存在於目前的內容中
error CS1061: 'FullWindow' 未包含 'Frame' 的定義，也找不到可接受類型 'FullWindow' 第一個引數的可存取擴充方法 'Frame' (是否遺漏 using 指示詞或組件參考?)
error CS0103: 名稱 'AppIcon' 不存在於目前的內容中
```

- [ ] **Step 3：實作**

先建立資料夾 `app/MemSweep.Desktop/Assets/`。

新增 `app/MemSweep.Desktop/Assets/MemSweep.ico`（二進位檔，24468 bytes）：在 repo 根目錄用 PowerShell 執行

```powershell
$base64 = @'
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
B9QZAAAAAElFTkSuQmCC
'@
[IO.File]::WriteAllBytes("$PWD\app\MemSweep.Desktop\Assets\MemSweep.ico", [Convert]::FromBase64String(($base64 -replace '\s', '')))
(Get-FileHash .\app\MemSweep.Desktop\Assets\MemSweep.ico -Algorithm SHA256).Hash
```

最後一行必須輸出 `513DAB08CEF6C410A9166F840F24CC758A90409F1817AEE56E76F5B2CA3C7B9F`。

`app/MemSweep.Desktop/MemSweep.Desktop.csproj` 整份換成：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <AssemblyName>MemSweep</AssemblyName>
    <RootNamespace>MemSweep.Desktop</RootNamespace>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <ApplicationIcon>Assets\MemSweep.ico</ApplicationIcon>
  </PropertyGroup>

  <ItemGroup>
    <!-- 同一個圖示也給自訂標題列使用（pack://application:,,,/MemSweep;component/Assets/MemSweep.ico） -->
    <Resource Include="Assets\MemSweep.ico" />
  </ItemGroup>

  <ItemGroup>
    <!-- WPF 與 WinForms 有同名型別（Application 等），WinForms 只在需要的檔案明確 using -->
    <Using Remove="System.Windows.Forms" />
    <Using Remove="System.Drawing" />
    <InternalsVisibleTo Include="MemSweep.Core.Tests" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\MemSweep.Core\MemSweep.Core.csproj" />
  </ItemGroup>

</Project>
```

`app/MemSweep.Cli/MemSweep.Cli.csproj` 整份換成：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <AssemblyName>memsweep-cli</AssemblyName>
    <RootNamespace>MemSweep.Cli</RootNamespace>
    <ApplicationIcon>..\MemSweep.Desktop\Assets\MemSweep.ico</ApplicationIcon>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\MemSweep.Core\MemSweep.Core.csproj" />
  </ItemGroup>

</Project>
```

新增 `app/MemSweep.Desktop/AppIcon.cs`：

```csharp
using System.Windows.Media.Imaging;

namespace MemSweep.Desktop;

/// <summary>
/// 程式圖示 Assets/MemSweep.ico（F-62）：exe 的圖示由專案的 ApplicationIcon 設定，
/// 這裡讓自訂標題列取用適合尺寸的一格。
/// </summary>
internal static class AppIcon
{
    public static readonly Uri Source = new("pack://application:,,,/MemSweep;component/Assets/MemSweep.ico");

    /// <summary>挑選不小於 pixels 的最小一格；每一格都比較小時用最大的一格。</summary>
    public static int PickSize(IReadOnlyList<int> sizes, int pixels)
    {
        var ordered = sizes.Order().ToList();
        return ordered.FirstOrDefault(size => size >= pixels, ordered[^1]);
    }

    public static IReadOnlyList<BitmapFrame> Frames() =>
        BitmapDecoder.Create(Source, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames;

    public static BitmapFrame Frame(int pixels)
    {
        var frames = Frames();
        int size = PickSize(frames.Select(f => f.PixelWidth).ToList(), pixels);
        return frames.First(f => f.PixelWidth == size);
    }
}
```

`app/MemSweep.Desktop/Native/User32.cs` 整份換成：

```csharp
using System.Runtime.InteropServices;

namespace MemSweep.Desktop.Native;

/// <summary>user32 的介面資源、前景視窗許可與畫面尺寸查詢；不涉及系統記憶體狀態。</summary>
internal static partial class User32
{
    /// <summary>ASFW_ANY：允許任何行程把視窗帶到前景。</summary>
    public const uint AsfwAny = uint.MaxValue;

    public const int SmCxSizeFrame = 32;
    public const int SmCySizeFrame = 33;
    public const int SmCxPaddedBorder = 92;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint icon);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AllowSetForegroundWindow(uint processId);

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForSystem();
}
```

新增 `app/MemSweep.Desktop/TitleBar.xaml`：

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
      <Button x:Name="MinimizeButton"
              Content="&#xE921;"
              ToolTip="最小化"
              Style="{StaticResource CaptionButton}"
              Click="MinimizeButton_Click" />
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

新增 `app/MemSweep.Desktop/TitleBar.xaml.cs`：

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MemSweep.Desktop.Native;

namespace MemSweep.Desktop;

/// <summary>
/// 自訂標題列（F-62、Design 3.6），放在使用 WindowChrome 的視窗頂端；按鈕直接操作所在的視窗。
/// </summary>
public partial class TitleBar : UserControl
{
    public TitleBar()
    {
        InitializeComponent();
        // 依目前的 DPI 挑最清楚的一格，例如 150% 時用 24 px
        Loaded += (_, _) => IconImage.Source = AppIcon.Frame((int)Math.Round(16 * VisualTreeHelper.GetDpi(this).DpiScaleX));
    }

    /// <summary>
    /// 使用 WindowChrome 的視窗最大化時，四邊會超出螢幕一個框線的寬度（調整大小的框線加上邊框間距），
    /// 內容要往內縮這麼多，標題列與按鈕才不會被切掉。WPF 的 SystemParameters 不含邊框間距，
    /// 所以直接向系統查詢，再依系統 DPI 換成 WPF 的單位（150% 時 5 + 6 px = 7.33）。
    /// </summary>
    public static Thickness MaximizedMargin =>
        Inset(
            User32.GetSystemMetrics(User32.SmCxSizeFrame),
            User32.GetSystemMetrics(User32.SmCySizeFrame),
            User32.GetSystemMetrics(User32.SmCxPaddedBorder),
            User32.GetDpiForSystem());

    /// <summary>框線與邊框間距是實際像素，依系統 DPI 換成 WPF 的單位。</summary>
    internal static Thickness Inset(int sizeFrameX, int sizeFrameY, int paddedBorder, uint dpi)
    {
        double scale = dpi / 96.0;
        double x = (sizeFrameX + paddedBorder) / scale;
        double y = (sizeFrameY + paddedBorder) / scale;
        return new Thickness(x, y, x, y);
    }

    private Window Host => Window.GetWindow(this)!;

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => Host.WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
        Host.WindowState = Host.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Host.Close();
}
```

`app/MemSweep.Desktop/Themes/Light.xaml` 整份換成：

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <SolidColorBrush x:Key="PanelBackgroundBrush" Color="#F9F9F9" />
  <SolidColorBrush x:Key="PanelBorderBrush" Color="#D0D0D0" />
  <SolidColorBrush x:Key="TextBrush" Color="#1B1B1B" />
  <SolidColorBrush x:Key="MutedTextBrush" Color="#6B6B6B" />
  <SolidColorBrush x:Key="TrackBrush" Color="#E3E3E3" />
  <SolidColorBrush x:Key="AccentBrush" Color="#0067C0" />
  <SolidColorBrush x:Key="BarBrush" Color="#4C8DF6" />
  <SolidColorBrush x:Key="UnattributedBarBrush" Color="#9E9E9E" />
  <SolidColorBrush x:Key="GreenBrush" Color="#2EA043" />
  <SolidColorBrush x:Key="OrangeBrush" Color="#E08A00" />
  <SolidColorBrush x:Key="RedBrush" Color="#D93A30" />
  <SolidColorBrush x:Key="ButtonBackgroundBrush" Color="#FFFFFF" />
  <SolidColorBrush x:Key="ButtonHoverBrush" Color="#EDEDED" />
  <SolidColorBrush x:Key="ButtonBorderBrush" Color="#C8C8C8" />
  <SolidColorBrush x:Key="CloseHoverBrush" Color="#C42B1C" />
  <SolidColorBrush x:Key="ScrollThumbBrush" Color="#C4C4C4" />
  <SolidColorBrush x:Key="ScrollThumbHoverBrush" Color="#9A9A9A" />
</ResourceDictionary>
```

`app/MemSweep.Desktop/Themes/Dark.xaml` 整份換成：

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <SolidColorBrush x:Key="PanelBackgroundBrush" Color="#2B2B2B" />
  <SolidColorBrush x:Key="PanelBorderBrush" Color="#454545" />
  <SolidColorBrush x:Key="TextBrush" Color="#F2F2F2" />
  <SolidColorBrush x:Key="MutedTextBrush" Color="#A0A0A0" />
  <SolidColorBrush x:Key="TrackBrush" Color="#3D3D3D" />
  <SolidColorBrush x:Key="AccentBrush" Color="#4CA3FF" />
  <SolidColorBrush x:Key="BarBrush" Color="#4CA3FF" />
  <SolidColorBrush x:Key="UnattributedBarBrush" Color="#8E8E93" />
  <SolidColorBrush x:Key="GreenBrush" Color="#3FB950" />
  <SolidColorBrush x:Key="OrangeBrush" Color="#FF9F0A" />
  <SolidColorBrush x:Key="RedBrush" Color="#FF453A" />
  <SolidColorBrush x:Key="ButtonBackgroundBrush" Color="#383838" />
  <SolidColorBrush x:Key="ButtonHoverBrush" Color="#444444" />
  <SolidColorBrush x:Key="ButtonBorderBrush" Color="#505050" />
  <SolidColorBrush x:Key="CloseHoverBrush" Color="#C42B1C" />
  <SolidColorBrush x:Key="ScrollThumbBrush" Color="#5C5C5C" />
  <SolidColorBrush x:Key="ScrollThumbHoverBrush" Color="#808080" />
</ResourceDictionary>
```

`app/MemSweep.Desktop/FullWindow.xaml` 整份換成：

```xml
<Window x:Class="MemSweep.Desktop.FullWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:MemSweep.Desktop"
        Title="MemSweep"
        Width="760"
        Height="560"
        MinWidth="520"
        MinHeight="360"
        WindowStartupLocation="CenterScreen"
        FontFamily="Segoe UI, Microsoft JhengHei UI"
        FontSize="12"
        Background="{DynamicResource PanelBackgroundBrush}"
        Foreground="{DynamicResource TextBrush}">
  <!-- 完整視窗（F-20、F-21）；DataContext 是 FullWindowModel -->
  <!-- 自訂標題列（F-62）：系統的標題列換成 TitleBar，拖曳、雙擊最大化、邊緣調整大小與貼齊仍由系統處理 -->
  <WindowChrome.WindowChrome>
    <WindowChrome CaptionHeight="32"
                  ResizeBorderThickness="6"
                  GlassFrameThickness="0"
                  CornerRadius="0"
                  UseAeroCaptionButtons="False" />
  </WindowChrome.WindowChrome>

  <Window.Resources>
    <!-- 自訂捲軸（只有垂直捲軸）：細的圓角滑塊，深色模式下才不會出現系統預設的淺色捲軸 -->
    <Style x:Key="ScrollPage" TargetType="RepeatButton">
      <Setter Property="Focusable" Value="False" />
      <Setter Property="IsTabStop" Value="False" />
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="RepeatButton">
            <Border Background="Transparent" />
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key="ScrollThumb" TargetType="Thumb">
      <Setter Property="Background" Value="{DynamicResource ScrollThumbBrush}" />
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="Thumb">
            <Border Margin="2" CornerRadius="3" Background="{TemplateBinding Background}" />
          </ControlTemplate>
        </Setter.Value>
      </Setter>
      <Style.Triggers>
        <Trigger Property="IsMouseOver" Value="True">
          <Setter Property="Background" Value="{DynamicResource ScrollThumbHoverBrush}" />
        </Trigger>
        <Trigger Property="IsDragging" Value="True">
          <Setter Property="Background" Value="{DynamicResource ScrollThumbHoverBrush}" />
        </Trigger>
      </Style.Triggers>
    </Style>
    <Style TargetType="ScrollBar">
      <Setter Property="Width" Value="10" />
      <Setter Property="MinWidth" Value="10" />
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="ScrollBar">
            <Track x:Name="PART_Track" IsDirectionReversed="True">
              <Track.DecreaseRepeatButton>
                <RepeatButton Command="ScrollBar.PageUpCommand" Style="{StaticResource ScrollPage}" />
              </Track.DecreaseRepeatButton>
              <Track.IncreaseRepeatButton>
                <RepeatButton Command="ScrollBar.PageDownCommand" Style="{StaticResource ScrollPage}" />
              </Track.IncreaseRepeatButton>
              <Track.Thumb>
                <Thumb Style="{StaticResource ScrollThumb}" />
              </Track.Thumb>
            </Track>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <!-- 自訂分頁標籤，深色模式下才不會出現系統預設的白色標籤 -->
    <Style TargetType="TabItem">
      <Setter Property="Foreground" Value="{DynamicResource MutedTextBrush}" />
      <Setter Property="Cursor" Value="Hand" />
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="TabItem">
            <Border x:Name="Tab" Padding="12,6" Margin="0,0,4,0" Background="Transparent" BorderBrush="Transparent" BorderThickness="0,0,0,2">
              <ContentPresenter ContentSource="Header" />
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsSelected" Value="True">
                <Setter TargetName="Tab" Property="BorderBrush" Value="{DynamicResource AccentBrush}" />
                <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />
              </Trigger>
              <Trigger Property="IsMouseOver" Value="True">
                <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
  </Window.Resources>

  <Border x:Name="Frame">
    <Border.Style>
      <Style TargetType="Border">
        <Style.Triggers>
          <DataTrigger Binding="{Binding WindowState, RelativeSource={RelativeSource AncestorType=Window}}" Value="Maximized">
            <Setter Property="Margin" Value="{x:Static local:TitleBar.MaximizedMargin}" />
          </DataTrigger>
        </Style.Triggers>
      </Style>
    </Border.Style>
    <DockPanel>
      <local:TitleBar x:Name="Caption" DockPanel.Dock="Top" />
      <TabControl Margin="12,4,12,12" Padding="0" Background="Transparent" BorderThickness="0">
        <TabItem Header="總覽">
          <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
            <StackPanel Margin="0,12,0,0" Width="480" HorizontalAlignment="Left">
              <local:SummaryView DataContext="{Binding Overview}" />
              <TextBlock Text="{Binding PageFileText}" Foreground="{DynamicResource MutedTextBrush}" Margin="0,8,0,0" />
            </StackPanel>
          </ScrollViewer>
        </TabItem>
        <TabItem Header="程式">
          <local:AppsView />
        </TabItem>
        <TabItem Header="尖峰紀錄">
          <local:PeaksView />
        </TabItem>
      </TabControl>
    </DockPanel>
  </Border>
</Window>
```

`app/MemSweep.Desktop/AppsView.xaml` 整份換成：

```xml
<UserControl x:Class="MemSweep.Desktop.AppsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <!-- 「程式」分頁（F-23）；DataContext 是 FullWindowModel，列會原地更新，所以用一般的 OneWay 繫結 -->
  <UserControl.Resources>
    <BooleanToVisibilityConverter x:Key="VisibleWhenTrue" />
    <Style x:Key="ExpandToggle" TargetType="ToggleButton">
      <Setter Property="Foreground" Value="{DynamicResource MutedTextBrush}" />
      <Setter Property="Cursor" Value="Hand" />
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="ToggleButton">
            <Border Background="Transparent">
              <TextBlock x:Name="Glyph" Text="▸" HorizontalAlignment="Center" VerticalAlignment="Center" />
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsChecked" Value="True">
                <Setter TargetName="Glyph" Property="Text" Value="▾" />
              </Trigger>
              <Trigger Property="IsMouseOver" Value="True">
                <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key="HeaderText" TargetType="TextBlock">
      <Setter Property="Foreground" Value="{DynamicResource MutedTextBrush}" />
      <Setter Property="FontWeight" Value="SemiBold" />
    </Style>
  </UserControl.Resources>

  <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
    <StackPanel Margin="0,12,8,0">
      <!-- 欄位：展開、名稱、行程數、commit、實際 RAM（工作集） -->
      <Grid Margin="0,0,0,4">
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width="20" />
          <ColumnDefinition Width="*" />
          <ColumnDefinition Width="64" />
          <ColumnDefinition Width="84" />
          <ColumnDefinition Width="84" />
        </Grid.ColumnDefinitions>
        <TextBlock Grid.Column="1" Text="名稱" Style="{StaticResource HeaderText}" />
        <TextBlock Grid.Column="2" Text="行程數" Style="{StaticResource HeaderText}" HorizontalAlignment="Right" />
        <TextBlock Grid.Column="3" Text="Commit" Style="{StaticResource HeaderText}" HorizontalAlignment="Right" />
        <TextBlock Grid.Column="4" Text="實際 RAM" Style="{StaticResource HeaderText}" HorizontalAlignment="Right" />
      </Grid>
      <Border Height="1" Background="{DynamicResource PanelBorderBrush}" Margin="0,0,0,4" />

      <ItemsControl ItemsSource="{Binding Apps}">
        <ItemsControl.ItemTemplate>
          <DataTemplate>
            <StackPanel x:Name="Row">
              <Grid Margin="0,3">
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width="20" />
                  <ColumnDefinition Width="*" />
                  <ColumnDefinition Width="64" />
                  <ColumnDefinition Width="84" />
                  <ColumnDefinition Width="84" />
                </Grid.ColumnDefinitions>
                <ToggleButton x:Name="Toggle" IsChecked="{Binding IsExpanded}" Style="{StaticResource ExpandToggle}" />
                <TextBlock x:Name="AppName" Grid.Column="1" Text="{Binding Name}" TextTrimming="CharacterEllipsis" />
                <TextBlock Grid.Column="2" Text="{Binding CountText}" Foreground="{DynamicResource MutedTextBrush}" HorizontalAlignment="Right" />
                <TextBlock Grid.Column="3" Text="{Binding CommitText}" HorizontalAlignment="Right" />
                <TextBlock Grid.Column="4" Text="{Binding WorkingSetText}" HorizontalAlignment="Right" />
              </Grid>

              <!-- 明細：依執行檔名稱合併，node 再依套件區分 -->
              <ItemsControl ItemsSource="{Binding Components}"
                            Visibility="{Binding IsExpanded, Converter={StaticResource VisibleWhenTrue}}"
                            Margin="20,0,0,4">
                <ItemsControl.ItemTemplate>
                  <DataTemplate>
                    <Grid Margin="0,1">
                      <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="84" />
                        <ColumnDefinition Width="84" />
                      </Grid.ColumnDefinitions>
                      <TextBlock Text="{Binding Name}" Foreground="{DynamicResource MutedTextBrush}" TextTrimming="CharacterEllipsis" />
                      <TextBlock Grid.Column="1" Text="{Binding CommitText}" Foreground="{DynamicResource MutedTextBrush}" HorizontalAlignment="Right" />
                      <TextBlock Grid.Column="2" Text="{Binding WorkingSetText}" Foreground="{DynamicResource MutedTextBrush}" HorizontalAlignment="Right" />
                    </Grid>
                  </DataTemplate>
                </ItemsControl.ItemTemplate>
              </ItemsControl>
            </StackPanel>
            <DataTemplate.Triggers>
              <DataTrigger Binding="{Binding Components.Count}" Value="0">
                <Setter TargetName="Toggle" Property="Visibility" Value="Hidden" />
              </DataTrigger>
              <DataTrigger Binding="{Binding IsUnattributed}" Value="True">
                <Setter TargetName="AppName" Property="FontStyle" Value="Italic" />
                <Setter TargetName="AppName" Property="Foreground" Value="{DynamicResource MutedTextBrush}" />
                <Setter TargetName="Row" Property="Margin" Value="0,4,0,0" />
              </DataTrigger>
            </DataTemplate.Triggers>
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>
    </StackPanel>
  </ScrollViewer>
</UserControl>
```

`app/MemSweep.Desktop/PeaksView.xaml` 整份換成：

```xml
<UserControl x:Class="MemSweep.Desktop.PeaksView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <!-- 「尖峰紀錄」分頁（F-24）；DataContext 是 FullWindowModel。清單只在紀錄有變動時整份換掉，所以用 OneWay -->
  <UserControl.Resources>
    <Style x:Key="SectionTitle" TargetType="TextBlock">
      <Setter Property="FontWeight" Value="SemiBold" />
      <Setter Property="FontSize" Value="13" />
      <Setter Property="Margin" Value="0,0,0,6" />
    </Style>
    <Style x:Key="HeaderText" TargetType="TextBlock">
      <Setter Property="Foreground" Value="{DynamicResource MutedTextBrush}" />
      <Setter Property="FontWeight" Value="SemiBold" />
    </Style>
    <Style x:Key="EmptyText" TargetType="TextBlock">
      <Setter Property="Text" Value="尚無紀錄" />
      <Setter Property="Foreground" Value="{DynamicResource MutedTextBrush}" />
      <Setter Property="Margin" Value="0,2,0,0" />
      <Setter Property="Visibility" Value="Collapsed" />
    </Style>
    <DataTemplate x:Key="PeakRowTemplate">
      <Grid Margin="0,3">
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width="120" />
          <ColumnDefinition Width="120" />
          <ColumnDefinition Width="84" />
          <ColumnDefinition Width="84" />
          <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>
        <TextBlock Text="{Binding FirstText}" />
        <TextBlock Grid.Column="1" Text="{Binding TimeText}" />
        <TextBlock Grid.Column="2" Text="{Binding CommitText}" HorizontalAlignment="Right" />
        <TextBlock Grid.Column="3" Text="{Binding LimitText}" Foreground="{DynamicResource MutedTextBrush}" HorizontalAlignment="Right" />
        <TextBlock Grid.Column="4" Text="{Binding TopAppsText}" Foreground="{DynamicResource MutedTextBrush}" TextWrapping="Wrap" Margin="16,0,0,0" />
      </Grid>
    </DataTemplate>
  </UserControl.Resources>

  <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
    <StackPanel Margin="0,12,8,0">
      <TextBlock Text="開機尖峰" Style="{StaticResource SectionTitle}" />
      <Grid Margin="0,0,0,4">
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width="120" />
          <ColumnDefinition Width="120" />
          <ColumnDefinition Width="84" />
          <ColumnDefinition Width="84" />
          <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>
        <TextBlock Text="開機時間" Style="{StaticResource HeaderText}" />
        <TextBlock Grid.Column="1" Text="尖峰時間" Style="{StaticResource HeaderText}" />
        <TextBlock Grid.Column="2" Text="Commit 已用" Style="{StaticResource HeaderText}" HorizontalAlignment="Right" />
        <TextBlock Grid.Column="3" Text="上限" Style="{StaticResource HeaderText}" HorizontalAlignment="Right" />
        <TextBlock Grid.Column="4" Text="當時的前五名" Style="{StaticResource HeaderText}" Margin="16,0,0,0" />
      </Grid>
      <Border Height="1" Background="{DynamicResource PanelBorderBrush}" Margin="0,0,0,4" />
      <ItemsControl x:Name="BootList" ItemsSource="{Binding BootPeaks}" ItemTemplate="{StaticResource PeakRowTemplate}" />
      <TextBlock x:Name="BootEmpty">
        <TextBlock.Style>
          <Style TargetType="TextBlock" BasedOn="{StaticResource EmptyText}">
            <Style.Triggers>
              <DataTrigger Binding="{Binding BootPeaks.Count}" Value="0">
                <Setter Property="Visibility" Value="Visible" />
              </DataTrigger>
            </Style.Triggers>
          </Style>
        </TextBlock.Style>
      </TextBlock>

      <TextBlock Text="每日尖峰" Style="{StaticResource SectionTitle}" Margin="0,20,0,6" />
      <Grid Margin="0,0,0,4">
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width="120" />
          <ColumnDefinition Width="120" />
          <ColumnDefinition Width="84" />
          <ColumnDefinition Width="84" />
          <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>
        <TextBlock Text="日期" Style="{StaticResource HeaderText}" />
        <TextBlock Grid.Column="1" Text="尖峰時間" Style="{StaticResource HeaderText}" />
        <TextBlock Grid.Column="2" Text="Commit 已用" Style="{StaticResource HeaderText}" HorizontalAlignment="Right" />
        <TextBlock Grid.Column="3" Text="上限" Style="{StaticResource HeaderText}" HorizontalAlignment="Right" />
        <TextBlock Grid.Column="4" Text="當時的前五名" Style="{StaticResource HeaderText}" Margin="16,0,0,0" />
      </Grid>
      <Border Height="1" Background="{DynamicResource PanelBorderBrush}" Margin="0,0,0,4" />
      <ItemsControl x:Name="DailyList" ItemsSource="{Binding DailyPeaks}" ItemTemplate="{StaticResource PeakRowTemplate}" />
      <TextBlock x:Name="DailyEmpty">
        <TextBlock.Style>
          <Style TargetType="TextBlock" BasedOn="{StaticResource EmptyText}">
            <Style.Triggers>
              <DataTrigger Binding="{Binding DailyPeaks.Count}" Value="0">
                <Setter Property="Visibility" Value="Visible" />
              </DataTrigger>
            </Style.Triggers>
          </Style>
        </TextBlock.Style>
      </TextBlock>
    </StackPanel>
  </ScrollViewer>
</UserControl>
```

- [ ] **Step 4：執行測試，確認通過**

執行：`(cd app && dotnet test)`

預期：總計 215，失敗 0。

- [ ] **Step 5：Commit**

```bash
git add app/MemSweep.Desktop app/MemSweep.Cli app/MemSweep.Core.Tests
git status --short
git commit -F - <<'EOF'
新增程式圖示與自訂標題列

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

- [ ] **Step 6：推送**（依「共同執行規則」先 fetch 並檢查）

---

### Task 9：小面板沒有取得焦點時也能收起

**檔案：**

- 修改（整份換成）：`app/MemSweep.Desktop/FlyoutWindow.xaml.cs`、`app/MemSweep.Desktop/FlyoutPlacement.cs`、`app/MemSweep.Desktop/Native/User32.cs`
- 修改（整份換成）：`app/MemSweep.Core.Tests/FlyoutTests.cs`、`app/MemSweep.Core.Tests/NativeSafetyTests.cs`

**實作的條文：**

> - F-10 從螢幕右下角彈出，點面板以外的地方就收起。

> - 小面板（F-10～F-12）：無邊框、不顯示在工作列、最上層；出現在主螢幕工作區的右下角（Windows 11 的通知區域固定在主螢幕底部的工作列）；失去焦點或按 Esc 時隱藏。視窗在第一次開啟時才建立，之後以顯示／隱藏切換（量測顯示關閉視窗也不會釋放記憶體，隱藏可讓重開更快）。Phase 02 實測：只掛在系統匣時行程約 18 MB，若啟動時就建立小面板約 58 MB，開過小面板後約 80 MB。

**介面：**

- 使用：Task 8 修改後的 `User32`。
- 產生：`User32.GetForegroundWindow()`；`InactiveCheck`（`KeepWatching`、`Hide`、`StopWatching`）；`FlyoutPlacement.Next(bool isActive, nint foregroundAtShow, nint foregroundNow)`。

- [ ] **Step 1：寫失敗的測試**

`app/MemSweep.Core.Tests/FlyoutTests.cs` 整份換成：

```csharp
using System.Windows;
using System.Windows.Controls;
using MemSweep.Core;
using MemSweep.Desktop;

namespace MemSweep.Core.Tests;

// 多個 STA 執行緒同時初始化 WPF 介面元件時會互相鎖死，介面測試一律依序執行
[TestClass]
[DoNotParallelize]
public sealed class FlyoutTests
{
    private static FlyoutPanel LaidOutPanel(PanelModel model) => WpfLayout.LaidOut(new FlyoutPanel { DataContext = model });

    [TestMethod]
    public void PlacesTheFlyoutAtTheBottomRightOfTheWorkArea()
    {
        var (left, top) = FlyoutPlacement.Place(areaRight: 1920, areaBottom: 1032, width: 330, height: 400);

        Assert.AreEqual(1578.0, left);
        Assert.AreEqual(620.0, top);
    }

    [TestMethod]
    public void AnInactivePanelHidesWhenTheForegroundWindowChanges()
    {
        Assert.AreEqual(InactiveCheck.KeepWatching, FlyoutPlacement.Next(isActive: false, foregroundAtShow: 100, foregroundNow: 100));
        Assert.AreEqual(InactiveCheck.Hide, FlyoutPlacement.Next(isActive: false, foregroundAtShow: 100, foregroundNow: 200));
        Assert.AreEqual(InactiveCheck.StopWatching, FlyoutPlacement.Next(isActive: true, foregroundAtShow: 100, foregroundNow: 200));
    }

    [TestMethod]
    public void ThemeFollowsAppsUseLightTheme()
    {
        Assert.AreEqual(ThemeKind.Dark, ThemeReader.FromRegistryValue(0));
        Assert.AreEqual(ThemeKind.Light, ThemeReader.FromRegistryValue(1));
    }

    [TestMethod]
    public void MissingOrUnexpectedThemeValueIsLight()
    {
        Assert.AreEqual(ThemeKind.Light, ThemeReader.FromRegistryValue(null));
        Assert.AreEqual(ThemeKind.Light, ThemeReader.FromRegistryValue("0"));
    }

    [STATestMethod]
    public void PanelShowsGaugesPeakRowsAndButtons()
    {
        var peak = new BootPeak(
            new DateTime(2026, 10, 26, 0, 30, 0, DateTimeKind.Utc),
            MemoryMath.FromGigabytes(36.8),
            MemoryMath.FromGigabytes(39.4),
            new DateTimeOffset(2026, 10, 26, 12, 10, 0, TimeSpan.FromHours(8)),
            [new PeakApp("Claude", 1)]);
        var panel = LaidOutPanel(PanelModel.Build(ReportSamples.Typical(), peak, UsageThresholds.Default));

        var texts = WpfLayout.Descendants(panel).OfType<TextBlock>().Select(t => t.Text).ToList();
        CollectionAssert.IsSubsetOf(
            new[]
            {
                "Commit",
                "31.4 / 39.4 GB（80%）",
                "實體 RAM",
                "20.5 / 31.4 GB（65%）",
                "開機以來 commit 尖峰：36.8 GB（12:10）",
                "Claude",
                "36",
                "3.2 GB",
                "系統（不屬於任何程式）",
                "6.9 GB",
            },
            texts);
        var buttons = WpfLayout.Descendants(panel).OfType<Button>().Select(b => b.Content as string).ToList();
        CollectionAssert.AreEqual(new[] { "完整清單…", "複製報告", "結束 MemSweep" }, buttons);
    }

    [STATestMethod]
    public void PanelShowsTheStorageWarningOnlyWhenThereIsOne()
    {
        const string warning = "peaks-daily.csv 無法讀取，尖峰紀錄已停用";
        var withWarning = LaidOutPanel(PanelModel.Build(ReportSamples.Typical(), null, UsageThresholds.Default, warning));
        var withoutWarning = LaidOutPanel(PanelModel.Build(ReportSamples.Typical(), null, UsageThresholds.Default));

        Assert.AreEqual(Visibility.Visible, withWarning.Summary.WarningText.Visibility);
        Assert.AreEqual(warning, withWarning.Summary.WarningText.Text);
        Assert.AreEqual(Visibility.Collapsed, withoutWarning.Summary.WarningText.Visibility);
    }

    [STATestMethod]
    public void PanelListsTopFivePlusUnattributed()
    {
        var panel = LaidOutPanel(PanelModel.Build(ReportSamples.Typical(), null, UsageThresholds.Default));

        var rows = WpfLayout.Descendants(panel).OfType<ContentPresenter>().Where(c => c.Content is PanelRow).ToList();

        Assert.HasCount(6, rows);
    }
}
```

`app/MemSweep.Core.Tests/NativeSafetyTests.cs` 整份換成：

```csharp
using System.Reflection;
using System.Runtime.InteropServices;
using MemSweep.Cli;
using MemSweep.Core;
using MemSweep.Desktop;

namespace MemSweep.Core.Tests;

/// <summary>A-07：出貨的組件不得宣告任何改變系統記憶體狀態或提升權限的 Win32 呼叫。</summary>
[TestClass]
public sealed class NativeSafetyTests
{
    private static readonly string[] ForbiddenEntryPoints =
    [
        "EmptyWorkingSet",
        "K32EmptyWorkingSet",
        "SetProcessWorkingSetSize",
        "SetProcessWorkingSetSizeEx",
        "NtSetSystemInformation",
        "SetSystemFileCacheSize",
        "AdjustTokenPrivileges",
        "TerminateProcess",
        "NtTerminateProcess",
        "NtSetInformationProcess",
        "VirtualFreeEx",
    ];

    // 只檢查以 DllImport／LibraryImport 宣告的呼叫；NativeLibrary.GetExport 或函式指標不在檢查範圍
    private static readonly Assembly[] ShippedAssemblies =
    [
        typeof(SystemReader).Assembly,
        typeof(CliApp).Assembly,
        typeof(SingleInstance).Assembly,
    ];

    private static List<string> EntryPoints() =>
        (from assembly in ShippedAssemblies
         from type in assembly.GetTypes()
         from method in type.GetMethods(
             BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
         where (method.Attributes & MethodAttributes.PinvokeImpl) != 0
         select method.GetCustomAttribute<DllImportAttribute>()?.EntryPoint ?? method.Name)
        .ToList();

    [TestMethod]
    public void FindsTheReadOnlyQueries()
    {
        var entryPoints = EntryPoints();

        CollectionAssert.IsSubsetOf(
            new[]
            {
                "K32GetPerformanceInfo",
                "NtQuerySystemInformation",
                "OpenProcess",
                "CloseHandle",
                "QueryFullProcessImageNameW",
                "NtQueryInformationProcess",
            },
            entryPoints);
    }

    [TestMethod]
    public void ScansTheDesktopAssembly()
    {
        CollectionAssert.IsSubsetOf(new[] { "DestroyIcon", "AllowSetForegroundWindow", "GetForegroundWindow" }, EntryPoints());
    }

    [TestMethod]
    public void DeclaresNoApiThatChangesMemoryStateOrPrivileges()
    {
        var found = EntryPoints()
            .Where(e => ForbiddenEntryPoints.Contains(e, StringComparer.OrdinalIgnoreCase))
            .ToList();

        Assert.IsEmpty(found);
    }
}
```

- [ ] **Step 2：執行測試，確認失敗**

執行：`(cd app && dotnet test)`

預期：建置失敗，錯誤訊息包含（實際輸出另有檔案路徑與重複的行）

```text
error CS0103: 名稱 'InactiveCheck' 不存在於目前的內容中
error CS0117: 'FlyoutPlacement' 未包含 'Next' 的定義
```

- [ ] **Step 3：實作**

`app/MemSweep.Desktop/Native/User32.cs` 整份換成：

```csharp
using System.Runtime.InteropServices;

namespace MemSweep.Desktop.Native;

/// <summary>user32 的介面資源、前景視窗與畫面尺寸查詢；不涉及系統記憶體狀態。</summary>
internal static partial class User32
{
    /// <summary>ASFW_ANY：允許任何行程把視窗帶到前景。</summary>
    public const uint AsfwAny = uint.MaxValue;

    public const int SmCxSizeFrame = 32;
    public const int SmCySizeFrame = 33;
    public const int SmCxPaddedBorder = 92;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint icon);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AllowSetForegroundWindow(uint processId);

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForSystem();

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();
}
```

`app/MemSweep.Desktop/FlyoutPlacement.cs` 整份換成：

```csharp
namespace MemSweep.Desktop;

/// <summary>小面板沒有取得焦點時，下一次檢查要做的事。</summary>
internal enum InactiveCheck
{
    KeepWatching,
    Hide,
    StopWatching,
}

/// <summary>小面板放在工作區右下角，與邊緣保留固定間距（F-10、Design 3.6）。</summary>
internal static class FlyoutPlacement
{
    public const double Margin = 12;

    public static (double Left, double Top) Place(double areaRight, double areaBottom, double width, double height) =>
        (areaRight - width - Margin, areaBottom - height - Margin);

    /// <summary>
    /// 小面板開啟時沒拿到焦點（沒有前景許可），就收不到失去焦點的事件：
    /// 使用者點了其他視窗、前景視窗換掉時收起；小面板之後拿到焦點，就交回一般的失去焦點處理。
    /// </summary>
    public static InactiveCheck Next(bool isActive, nint foregroundAtShow, nint foregroundNow) =>
        isActive ? InactiveCheck.StopWatching
        : foregroundNow != foregroundAtShow ? InactiveCheck.Hide
        : InactiveCheck.KeepWatching;
}
```

`app/MemSweep.Desktop/FlyoutWindow.xaml.cs` 整份換成：

```csharp
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MemSweep.Desktop.Native;

namespace MemSweep.Desktop;

/// <summary>
/// 小面板視窗（F-10）：無邊框、不在工作列、最上層；失去焦點或按 Esc 就隱藏。
/// 只建立一次，之後以顯示／隱藏切換（Design 3.6）。
/// </summary>
public partial class FlyoutWindow : Window
{
    private readonly DispatcherTimer _inactiveCheck;
    private DateTime _hiddenAtUtc = DateTime.MinValue;
    private nint _foregroundAtShow;

    public FlyoutWindow()
    {
        InitializeComponent();
        _inactiveCheck = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _inactiveCheck.Tick += (_, _) => CheckWhileInactive();
        Deactivated += (_, _) => HidePanel();
        SizeChanged += (_, _) => PlaceAtCorner();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                HidePanel();
                e.Handled = true;
            }
        };
    }

    /// <summary>
    /// 剛因失去焦點而隱藏。點系統匣圖示時焦點會先移走、讓小面板隱藏，同一次點擊不應再把它打開。
    /// </summary>
    public bool WasJustHidden => DateTime.UtcNow - _hiddenAtUtc < TimeSpan.FromMilliseconds(400);

    public void ShowPanel()
    {
        Show();
        PlaceAtCorner();
        if (!Activate())
        {
            // 沒有前景許可（例如由背景啟動的第二個執行個體叫出），改為定時檢查前景視窗
            _foregroundAtShow = User32.GetForegroundWindow();
            _inactiveCheck.Start();
        }
    }

    public void HidePanel()
    {
        _inactiveCheck.Stop();
        if (!IsVisible)
        {
            return;
        }
        Hide();
        _hiddenAtUtc = DateTime.UtcNow;
    }

    private void CheckWhileInactive()
    {
        switch (FlyoutPlacement.Next(IsActive, _foregroundAtShow, User32.GetForegroundWindow()))
        {
            case InactiveCheck.Hide:
                HidePanel();
                break;
            case InactiveCheck.StopWatching:
                _inactiveCheck.Stop();
                break;
        }
    }

    private void PlaceAtCorner()
    {
        // Windows 11 的通知區域固定在主螢幕底部的工作列，所以用主螢幕的工作區
        var area = SystemParameters.WorkArea;
        (Left, Top) = FlyoutPlacement.Place(area.Right, area.Bottom, ActualWidth, ActualHeight);
    }
}
```

- [ ] **Step 4：執行測試，確認通過**

執行：`(cd app && dotnet test)`

預期：總計 216，失敗 0。

- [ ] **Step 5：Commit**

```bash
git add app/MemSweep.Desktop app/MemSweep.Core.Tests
git status --short
git commit -F - <<'EOF'
小面板沒有取得焦點時也能收起

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

- [ ] **Step 6：推送**（依「共同執行規則」先 fetch 並檢查）

---

## Phase 收尾

- [ ] **完整檢查**：`(cd app && dotnet build -c Release)` 為 0 個警告、0 個錯誤；`(cd app && dotnet test)` 總計 216，失敗 0。
- [ ] **A-03 量測**：執行 `app/artifacts/bin/MemSweep.Desktop/release/MemSweep.exe`，再執行一次同一個 exe 叫出小面板，按「完整清單…」，三個分頁都切過、展開兩三列後關閉完整視窗；之後放著不動 60 分鐘，再以 `(Get-Process MemSweep).PrivateMemorySize64` 量測，必須不超過 100 MB。開始前提醒使用者不要按「結束 MemSweep」，量完用小面板的「結束 MemSweep」結束。
- [ ] **使用者驗收 A-05**：以 Excel 直接開啟 `app/artifacts/bin/MemSweep.Desktop/release/data/peaks-boot.csv` 與 `peaks-daily.csv`，中文欄名與程式名稱正常顯示。
- [ ] **使用者驗收 A-08**：關閉完整視窗、點小面板以外的地方、按 Esc 之後，MemSweep 仍在系統匣；只有「結束 MemSweep」會結束程式。
- [ ] **使用者確認 Task 9**：MemSweep 執行中時，在 PowerShell 執行 `Start-Sleep 5; & .\app\artifacts\bin\MemSweep.Desktop\release\MemSweep.exe`，5 秒內切到另一個視窗；小面板出現後，點第三個視窗（不是剛才切過去的那個），小面板要收起。
- [ ] **使用者確認圖示與標題列**：檔案總管裡 `MemSweep.exe` 與 `memsweep-cli.exe` 顯示量表圖示；完整視窗的工作列按鈕與標題列也是同一個圖示；標題列可拖曳、雙擊最大化、右鍵出現系統選單、拖曳邊緣調整大小；最大化時標題列與三個按鈕完整顯示在螢幕內；深淺色切換時標題列與捲軸跟著換色。
- [ ] **使用者外觀確認**：右鍵選單與小面板的「完整清單…」都會開啟完整視窗；三個分頁在淺色與深色下都清楚；「程式」分頁可展開明細，每 2 秒更新時展開的列不會收回；「尖峰紀錄」分頁有開機尖峰與每日尖峰。
- [ ] **開 PR**：所有 Task 都推送後，用 `/repo-open-pr` 開 PR（base `main`，head `phase-03-peaks-window`）。合併由使用者決定，使用 merge commit。
