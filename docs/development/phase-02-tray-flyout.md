# MemSweep Phase 02：系統匣與小面板 實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**目標：** 完成常駐系統匣的 `MemSweep.exe`：數字圖示與提示文字、右鍵選單、小面板、單一執行個體、深淺色，以及記憶體中的開機尖峰追蹤；並併入 PR #1 延後的兩項小修（CLI 結束碼 3、A-07 清單補強）。

**架構：** 與介面無關的邏輯放在 MemSweep.Core（`PeakTracker`、`UsageThresholds`、`TrayText`、`PanelModel`、`SamplingLoop`），可以直接單元測試。MemSweep.Desktop 只放介面：`App` 組合取樣迴圈，`TrayController` 管 `NotifyIcon` 與小面板，`FlyoutPanel`（UserControl）是小面板版面，`FlyoutWindow` 承載它。

**技術：** C#、.NET 10、WPF（軟體繪圖）、WinForms `NotifyIcon`、`System.Drawing`、`LibraryImport`、MSTest 4.5.1（含 `[STATestMethod]`）。

**依據：** [SPEC](../spec.md)、[Design](../design.md)（含隨本計畫提交的修訂）。本計畫引用的條文都是原文；計畫自己的解讀集中在「計畫的解讀」一節。

**分支：** `phase-02-tray-flyout`（已從 `origin/main` 建立）。

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

1. 常駐好幾天、圖示每 2 秒更新：每次換圖示都要釋放控制代碼，USER 物件不能一直增加。Task 5 `RenderingManyIconsDoesNotLeakHandles`。
2. 常駐期間某一次取樣遇到 Win32 錯誤：略過那一次，系統匣繼續更新，程式不結束。Task 4 `SkipsFailedSamplesAndKeepsRunning`。
3. 使用者又開了一次 MemSweep.exe（例如開機啟動之後又從開始功能表點）：不會出現第二個圖示，原本的執行個體顯示小面板。Task 5 `SecondInstanceAsksTheFirstToShowThePanel`。
4. Windows 深淺色設定的登錄值不存在或型別不對：以淺色顯示，不會出錯。Task 6 `MissingOrUnexpectedThemeValueIsLight`。
5. Claude 用 `memsweep-cli` 時取樣失敗：回傳固定的結束碼 3 與一行錯誤訊息，不印堆疊。Task 1 `SamplingFailureIsExitCode3`。

## 計畫的解讀

以下是本計畫自己的決定，不是 SPEC 或 Design 的原文：

- `PeakTracker` 第一次取樣時，Windows 的尖峰若等於目前用量，代表尖峰就是現在，記為已知（時間與前五名取自這次取樣）；高於目前用量才記為「未知」。
- 每日尖峰沒有在 Phase 02 顯示，連同 CSV 一起留到 Phase 03 實作；Phase 02 的 `PeakTracker` 只追蹤開機尖峰。
- 小面板第一次開啟時才建立視窗（Design 3.6 已修訂）：只掛在系統匣時行程約 18 MB，開過小面板後約 80 MB。
- 小面板的名稱與行程數分成兩欄（草圖 D 是同一欄），長名稱以「…」截斷。
- 「複製報告」按下後，按鈕文字顯示「已複製」或「複製失敗」（剪貼簿被其他程式佔用時）2 秒。
- 取樣迴圈因程式錯誤（Win32 錯誤以外的例外）結束時，在 UI 執行緒重新拋出，讓程式明顯當掉，而不是圖示停在舊數字。
- 圖示以字形的實際外框縮放到填滿圖示；兩位數以寬度為限，「100」會自動縮小。
- 單一執行個體只用 Mutex 是否已存在判斷、不持有所有權，所以釋放時不受建立執行緒限制。
- 預設名稱 `Local\MemSweep.SingleInstance`、`Local\MemSweep.ShowPanel` 照 Design 3.7；常數互相比較的測試會被 MSTest 分析器（MSTEST0032）擋下，所以名稱只靠引用條文把關，測試改用隨機名稱，避免碰到正在執行的 MemSweep。
- MemSweep.Desktop 同時開 `UseWPF` 與 `UseWindowsForms`，兩者有同名型別（`Application` 等），所以移除 WinForms 的隱含 using，需要時在檔案內明確 using。
- 測試專案參考 MemSweep.Desktop（WinExe）：A-07 要掃描它的組件，介面元件也要測；WindowsDesktop 執行環境會自動帶入。

## 共同執行規則

- 工作目錄是 repo 根目錄 `D:\AxeenWorld\CodeLab\Projects\MemSweep`，分支 `phase-02-tray-flyout`。
- `dotnet` 指令一律在 `app/` 底下執行，用子殼層避免改變目前目錄，例如 `(cd app && dotnet test)`。
- 程式碼區塊要逐字照抄，包括全形符號：`：`、`（`、`）`、`「`、`」`、`…`、`—`。XAML 的屬性順序與縮排也照抄。不要刪除或移動步驟沒有提到的任何一行。
- 測試就是規格：不得修改或刪除測試裡的任何斷言。照抄後測試仍失敗，就回報 BLOCKED。
- `[STATestMethod]` 的測試會在 STA 執行緒建立 WPF 控制項，但不顯示視窗。即時測試全部是唯讀查詢。不得執行任何會改變系統記憶體狀態的動作，也不得結束使用者的行程。
- 需要的暫存檔放在 `.camp_claude/` 的任務資料夾。commit 訊息用 Bash 的 heredoc 傳給 `git commit -F -`，不要用 PowerShell 寫訊息檔。
- commit 訊息與 `Co-Authored-By` 尾行照計畫原樣使用。
- 每個 Task 完成後推送。推送前先 `git fetch origin`，並確認遠端分支仍存在、沒有已合併或已關閉的 PR：

  ```bash
  git fetch origin
  git ls-remote --heads origin phase-02-tray-flyout
  gh pr list --head phase-02-tray-flyout --state all
  git push
  ```

  Task 1 是第一次推送，遠端還沒有這個分支，改用 `git push -u origin phase-02-tray-flyout`。

---

### Task 1：命令列取樣失敗時回傳結束碼 3

**檔案：**

- 修改（整份換成）：`app/MemSweep.Cli/CliApp.cs`、`app/MemSweep.Core.Tests/CliAppTests.cs`

**實作的條文：**

> | 指令 | 行為 |
> | --- | --- |
> | 無法取得系統記憶體資訊 | 錯誤訊息寫到 stderr，不印堆疊，結束碼 3 |

**介面：**

- 產生：`CliApp.ExitSampleError = 3`；`CliApp.Usage` 多一行結束碼說明；`CliApp.Run` 在取樣丟出 `Win32Exception` 或 `InvalidOperationException` 時回傳 3。

- [ ] **Step 1：寫失敗的測試**

`app/MemSweep.Core.Tests/CliAppTests.cs` 整份換成：

```csharp
using System.ComponentModel;
using MemSweep.Cli;
using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class CliAppTests
{
    private sealed class Run
    {
        public Run(params string[] args)
        {
            var output = new StringWriter();
            var error = new StringWriter();
            ExitCode = CliApp.Run(
                args,
                () =>
                {
                    SampleCalls++;
                    return ReportSamples.Typical();
                },
                output,
                error);
            Output = output.ToString();
            Error = error.ToString();
        }

        public int ExitCode { get; }
        public string Output { get; }
        public string Error { get; }
        public int SampleCalls { get; private set; }
    }

    [TestMethod]
    [DataRow(new string[0])]
    [DataRow(new[] { "--help" })]
    [DataRow(new[] { "-h" })]
    public void PrintsUsageWithoutSampling(string[] args)
    {
        var run = new Run(args);

        Assert.AreEqual(CliApp.ExitOk, run.ExitCode);
        Assert.AreEqual(CliApp.Usage, run.Output);
        Assert.AreEqual(0, run.SampleCalls);
    }

    [TestMethod]
    public void StatusPrintsTheTextSummary()
    {
        var run = new Run("status");

        Assert.AreEqual(CliApp.ExitOk, run.ExitCode);
        Assert.AreEqual(ReportFormatter.ToText(ReportSamples.Typical()), run.Output);
        Assert.AreEqual(string.Empty, run.Error);
    }

    [TestMethod]
    public void StatusJsonPrintsJson()
    {
        var run = new Run("status", "--json");

        Assert.AreEqual(CliApp.ExitOk, run.ExitCode);
        Assert.AreEqual(ReportFormatter.ToJson(ReportSamples.Typical()), run.Output);
    }

    [TestMethod]
    [DataRow("5", CliApp.ExitOk)]
    [DataRow("7.5", CliApp.ExitOk)]
    [DataRow("8", CliApp.ExitOk)]
    [DataRow("8.5", CliApp.ExitNotEnough)]
    [DataRow("10", CliApp.ExitNotEnough)]
    public void CheckReturnsExitCodeByFreeCommit(string need, int expectedExitCode)
    {
        // 樣本的剩餘 commit 是 8.0 GB
        var run = new Run("check", "--need", need);

        Assert.AreEqual(expectedExitCode, run.ExitCode);
        StringAssert.StartsWith(run.Output, expectedExitCode == CliApp.ExitOk ? "足夠：" : "不足：");
        Assert.AreEqual(1, run.SampleCalls);
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("-1")]
    [DataRow("abc")]
    [DataRow("NaN")]
    [DataRow("Infinity")]
    [DataRow("7,5")]
    public void CheckRejectsInvalidNeed(string need)
    {
        var run = new Run("check", "--need", need);

        Assert.AreEqual(CliApp.ExitUsageError, run.ExitCode);
        Assert.AreEqual("錯誤：--need 需要大於 0 的 GB 數值，收到「" + need + "」\n" + CliApp.Usage, run.Error);
        Assert.AreEqual(string.Empty, run.Output);
        Assert.AreEqual(0, run.SampleCalls);
    }

    [TestMethod]
    [DataRow(new[] { "status" })]
    [DataRow(new[] { "status", "--json" })]
    [DataRow(new[] { "check", "--need", "5" })]
    public void SamplingFailureIsExitCode3(string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = CliApp.Run(args, () => throw new Win32Exception(5, "存取被拒。"), output, error);

        Assert.AreEqual(CliApp.ExitSampleError, exitCode);
        Assert.AreEqual("錯誤：無法取得系統記憶體資訊：存取被拒。\n", error.ToString());
        Assert.AreEqual(string.Empty, output.ToString());
    }

    [TestMethod]
    public void NtStatusFailureIsExitCode3()
    {
        var error = new StringWriter();

        int exitCode = CliApp.Run(
            ["status"],
            () => throw new InvalidOperationException("NtQuerySystemInformation(5) 失敗：0xC0000017"),
            new StringWriter(),
            error);

        Assert.AreEqual(CliApp.ExitSampleError, exitCode);
        Assert.AreEqual("錯誤：無法取得系統記憶體資訊：NtQuerySystemInformation(5) 失敗：0xC0000017\n", error.ToString());
    }

    [TestMethod]
    [DataRow(new[] { "status", "--xml" })]
    [DataRow(new[] { "check" })]
    [DataRow(new[] { "check", "--need" })]
    [DataRow(new[] { "check", "10" })]
    [DataRow(new[] { "purge" })]
    public void UnknownArgumentsAreUsageErrors(string[] args)
    {
        var run = new Run(args);

        Assert.AreEqual(CliApp.ExitUsageError, run.ExitCode);
        Assert.AreEqual("錯誤：無法辨識的參數「" + string.Join(" ", args) + "」\n" + CliApp.Usage, run.Error);
        Assert.AreEqual(0, run.SampleCalls);
    }
}
```

- [ ] **Step 2：執行測試，確認失敗**

執行：`(cd app && dotnet test)`

預期：建置失敗，錯誤為

```text
error CS0117: 'CliApp' 未包含 'ExitSampleError' 的定義
```

- [ ] **Step 3：實作**

`app/MemSweep.Cli/CliApp.cs` 整份換成：

```csharp
using System.ComponentModel;
using System.Globalization;
using MemSweep.Core;

namespace MemSweep.Cli;

/// <summary>memsweep-cli 的參數解析與執行（F-40～F-43）。只在需要時取樣，且不讀寫 data\。</summary>
public static class CliApp
{
    public const int ExitOk = 0;
    public const int ExitNotEnough = 1;
    public const int ExitUsageError = 2;
    public const int ExitSampleError = 3;

    public static readonly string Usage =
        "用法：\n" +
        "  memsweep-cli status             顯示 commit、RAM、尖峰與前五名\n" +
        "  memsweep-cli status --json      以 JSON 輸出相同內容\n" +
        "  memsweep-cli check --need <GB>  剩餘 commit 足夠時結束碼 0，不足時 1\n" +
        "結束碼：參數錯誤為 2，無法取得系統記憶體資訊為 3\n";

    public static int Run(string[] args, Func<GroupedSnapshot> sample, TextWriter output, TextWriter error)
    {
        try
        {
            return RunCommand(args, sample, output, error);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            // 取樣失敗不印堆疊，讓呼叫端只需要認得固定的結束碼
            error.Write("錯誤：無法取得系統記憶體資訊：" + e.Message + "\n");
            return ExitSampleError;
        }
    }

    private static int RunCommand(string[] args, Func<GroupedSnapshot> sample, TextWriter output, TextWriter error)
    {
        switch (args)
        {
            case [] or ["--help"] or ["-h"]:
                output.Write(Usage);
                return ExitOk;

            case ["status"]:
                output.Write(ReportFormatter.ToText(sample()));
                return ExitOk;

            case ["status", "--json"]:
                output.Write(ReportFormatter.ToJson(sample()));
                return ExitOk;

            case ["check", "--need", var text] when TryParseGigabytes(text, out double need):
                var grouped = sample();
                var result = HeadroomCheck.Evaluate(grouped.Snapshot, need);
                output.Write(ReportFormatter.ToCheckText(grouped, result));
                return result.IsEnough ? ExitOk : ExitNotEnough;

            case ["check", "--need", var text]:
                error.Write("錯誤：--need 需要大於 0 的 GB 數值，收到「" + text + "」\n" + Usage);
                return ExitUsageError;

            default:
                error.Write("錯誤：無法辨識的參數「" + string.Join(" ", args) + "」\n" + Usage);
                return ExitUsageError;
        }
    }

    private static bool TryParseGigabytes(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value)
        && value > 0;
}
```

- [ ] **Step 4：執行測試，確認通過**

執行：`(cd app && dotnet test)`

預期：總計 78，失敗 0。

- [ ] **Step 5：Commit**

```bash
git add app/MemSweep.Cli/CliApp.cs app/MemSweep.Core.Tests/CliAppTests.cs
git status --short
git commit -F - <<'EOF'
命令列取樣失敗時回傳結束碼 3

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

- [ ] **Step 6：推送**

第一次推送：`git fetch origin` 後執行 `git push -u origin phase-02-tray-flyout`。

---

### Task 2：開機尖峰追蹤

**檔案：**

- 新增：`app/MemSweep.Core/BootPeak.cs`、`app/MemSweep.Core/PeakTracker.cs`
- 新增：`app/MemSweep.Core.Tests/PeakTrackerTests.cs`

**實作的條文：**

> - F-31 開機尖峰的數值以 Windows 記錄的開機以來尖峰為準。尖峰如果發生在 MemSweep 未執行的期間，該筆的發生時間與前五名標示為「未知」。〔Claude 決定〕
> - **開機尖峰**：以開機時間識別。每次取樣讀取 Windows 的開機以來尖峰：
> - 數值比目前紀錄高，代表尖峰發生在上次取樣之後（最多 2 秒前），就以本次取樣的時間與前五名作為該筆紀錄。這是近似值。
> - MemSweep 啟動時，如果 Windows 的尖峰高於這次開機已有的紀錄（或還沒有紀錄），代表尖峰發生在 MemSweep 未執行期間，發生時間與前五名都記為「未知」（F-31）。

**介面：**

- 使用：Phase 01 的 `GroupedSnapshot`、`SystemSnapshot`、`ReportFormatter.TopCount`；測試用 `ReportSamples`。
- 產生：
  - `record PeakApp(string Name, long CommitBytes)`
  - `record BootPeak(DateTime BootTimeUtc, long CommitBytes, long CommitLimitBytes, DateTimeOffset? Time, IReadOnlyList<PeakApp>? TopApps)`，`bool IsKnown`
  - `sealed class PeakTracker`：`BootPeak? BootPeak { get; }`、`bool Update(GroupedSnapshot grouped)`

- [ ] **Step 1：寫失敗的測試**

`app/MemSweep.Core.Tests/PeakTrackerTests.cs`：

```csharp
using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class PeakTrackerTests
{
    private static GroupedSnapshot Sample(double usedGb, double peakGb, DateTimeOffset timestamp)
    {
        var typical = ReportSamples.Typical();
        var snapshot = typical.Snapshot with
        {
            CommitUsedBytes = MemoryMath.FromGigabytes(usedGb),
            CommitPeakBytes = MemoryMath.FromGigabytes(peakGb),
            Timestamp = timestamp,
        };
        return typical with { Snapshot = snapshot };
    }

    [TestMethod]
    public void FirstSampleBelowTheWindowsPeakIsUnknown()
    {
        var tracker = new PeakTracker();

        bool changed = tracker.Update(Sample(31.4, 36.8, ReportSamples.Timestamp));

        Assert.IsTrue(changed);
        var peak = tracker.BootPeak!;
        Assert.AreEqual(MemoryMath.FromGigabytes(36.8), peak.CommitBytes);
        Assert.AreEqual(MemoryMath.FromGigabytes(39.4), peak.CommitLimitBytes);
        Assert.AreEqual(new DateTime(2026, 10, 26, 0, 30, 0, DateTimeKind.Utc), peak.BootTimeUtc);
        Assert.IsFalse(peak.IsKnown);
        Assert.IsNull(peak.Time);
        Assert.IsNull(peak.TopApps);
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
        Assert.AreEqual(MemoryMath.FromGigabytes(3.2), peak.TopApps![0].CommitBytes);
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
        Assert.AreEqual(MemoryMath.FromGigabytes(38.0), peak.CommitBytes);
        Assert.AreEqual(later, peak.Time);
        Assert.HasCount(5, peak.TopApps!);
    }

    [TestMethod]
    public void UnchangedWindowsPeakIsNotAnUpdate()
    {
        var tracker = new PeakTracker();
        tracker.Update(Sample(31.4, 36.8, ReportSamples.Timestamp));
        var before = tracker.BootPeak;

        bool changed = tracker.Update(Sample(33.0, 36.8, ReportSamples.Timestamp.AddSeconds(2)));

        Assert.IsFalse(changed);
        Assert.AreSame(before, tracker.BootPeak);
    }

    [TestMethod]
    public void NoPeakBeforeTheFirstSample()
    {
        Assert.IsNull(new PeakTracker().BootPeak);
    }
}
```

- [ ] **Step 2：執行測試，確認失敗**

執行：`(cd app && dotnet test)`

預期：建置失敗，錯誤為

```text
error CS0246: 找不到類型或命名空間名稱 'PeakTracker' (是否遺漏了 using 指示詞或組件參考?)
```

- [ ] **Step 3：實作**

`app/MemSweep.Core/BootPeak.cs`：

```csharp
namespace MemSweep.Core;

/// <summary>尖峰當下的一個前五名應用程式。</summary>
public sealed record PeakApp(string Name, long CommitBytes);

/// <summary>
/// 本次開機的 commit 尖峰。Time 與 TopApps 為 null 表示尖峰發生在 MemSweep 未執行期間，
/// 發生時間與前五名都無從得知（F-31）。
/// </summary>
public sealed record BootPeak(
    DateTime BootTimeUtc,
    long CommitBytes,
    long CommitLimitBytes,
    DateTimeOffset? Time,
    IReadOnlyList<PeakApp>? TopApps)
{
    public bool IsKnown => Time is not null;
}
```

`app/MemSweep.Core/PeakTracker.cs`：

```csharp
namespace MemSweep.Core;

/// <summary>
/// 追蹤本次開機的 commit 尖峰（Design 3.10）。Phase 02 只保存在記憶體中；
/// 只能由取樣的那一個執行緒呼叫 <see cref="Update"/>。
/// </summary>
public sealed class PeakTracker
{
    public BootPeak? BootPeak { get; private set; }

    /// <summary>以一次取樣更新開機尖峰；尖峰有變動時回傳 true。</summary>
    public bool Update(GroupedSnapshot grouped)
    {
        var snapshot = grouped.Snapshot;
        if (BootPeak is null)
        {
            // 第一次取樣：Windows 記錄的尖峰高於目前用量，代表尖峰發生在 MemSweep 執行之前
            BootPeak = snapshot.CommitPeakBytes > snapshot.CommitUsedBytes
                ? new BootPeak(snapshot.BootTimeUtc, snapshot.CommitPeakBytes, snapshot.CommitLimitBytes, null, null)
                : FromSample(grouped);
            return true;
        }

        if (snapshot.CommitPeakBytes <= BootPeak.CommitBytes)
        {
            return false;
        }

        // 尖峰發生在上次取樣之後（最多 2 秒前），以本次取樣的時間與前五名作為近似值
        BootPeak = FromSample(grouped);
        return true;
    }

    private static BootPeak FromSample(GroupedSnapshot grouped)
    {
        var snapshot = grouped.Snapshot;
        return new BootPeak(
            snapshot.BootTimeUtc,
            snapshot.CommitPeakBytes,
            snapshot.CommitLimitBytes,
            snapshot.Timestamp,
            grouped.Apps
                .Take(ReportFormatter.TopCount)
                .Select(a => new PeakApp(a.Name, a.CommitBytes))
                .ToList());
    }
}
```

- [ ] **Step 4：執行測試，確認通過**

執行：`(cd app && dotnet test)`

預期：總計 83，失敗 0。

- [ ] **Step 5：Commit**

```bash
git add app/MemSweep.Core app/MemSweep.Core.Tests
git status --short
git commit -F - <<'EOF'
新增開機尖峰追蹤

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

- [ ] **Step 6：推送**（依「共同執行規則」先 fetch 並檢查）

---

### Task 3：系統匣與小面板的顯示模型

**檔案：**

- 新增：`app/MemSweep.Core/UsageThresholds.cs`、`app/MemSweep.Core/TrayText.cs`、`app/MemSweep.Core/PanelModel.cs`
- 新增：`app/MemSweep.Core.Tests/DisplayModelTests.cs`

**實作的條文：**

> - F-02 圖示以整數顯示目前的 commit 使用率，例如「80」。
> - F-03 數字顏色依門檻變化：低於橘色門檻為綠色；達到橘色門檻但低於紅色門檻為橘色；達到紅色門檻以上為紅色。門檻可在設定調整，預設橘色 70%、紅色 85%。〔預設值：Claude 決定〕
> - F-05 滑鼠停在圖示上時顯示提示：commit 已用 / 上限（GB 與百分比）、實體 RAM 已用 / 總量（GB）。
> - F-11 內容：
> - commit 量表：已用 / 上限（GB）與使用率，顏色規則同 F-03。
> - 實體 RAM 量表：已用 / 總量（GB）與使用率。
> - 開機以來尖峰：數值與發生時間，規則見 F-31。
> - commit 前五名的應用程式：名稱、行程數、commit（GB）、相對長條。
> - 「系統（不屬於任何程式）」一列，固定顯示在前五名之後。
> - 提示文字（F-05）範例：`MemSweep` / `Commit 31.4 / 39.4 GB（80%）` / `RAM 20.5 / 31.4 GB`。

**介面：**

- 使用：Task 2 的 `BootPeak`；Phase 01 的 `GroupedSnapshot`、`MemoryMath`、`ReportFormatter.TopCount`。
- 產生：
  - `enum UsageLevel { Green, Orange, Red }`
  - `record UsageThresholds(int OrangePercent, int RedPercent)`：`static Default`（70、85）、`UsageLevel LevelOf(int percent)`
  - `static class TrayText`：`int CommitPercent(SystemSnapshot)`、`string IconText(SystemSnapshot)`、`string Tooltip(SystemSnapshot)`
  - `record GaugeModel(string Label, string ValueText, double Fraction, UsageLevel? Level)`
  - `record PanelRow(string Name, string CountText, string GigabytesText, double BarFraction, bool IsUnattributed)`
  - `record PanelModel(GaugeModel Commit, GaugeModel Ram, string PeakText, IReadOnlyList<PanelRow> Rows)`：`static PanelModel Build(GroupedSnapshot grouped, BootPeak? peak, UsageThresholds thresholds)`

- [ ] **Step 1：寫失敗的測試**

`app/MemSweep.Core.Tests/DisplayModelTests.cs`：

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

- [ ] **Step 2：執行測試，確認失敗**

執行：`(cd app && dotnet test)`

預期：建置失敗，錯誤包含

```text
error CS0246: 找不到類型或命名空間名稱 'UsageLevel' (是否遺漏了 using 指示詞或組件參考?)
error CS0103: 名稱 'UsageLevel' 不存在於目前的內容中
```

- [ ] **Step 3：實作**

`app/MemSweep.Core/UsageThresholds.cs`：

```csharp
namespace MemSweep.Core;

/// <summary>commit 使用率的顏色等級（F-03）。</summary>
public enum UsageLevel
{
    Green,
    Orange,
    Red,
}

/// <summary>橘色與紅色門檻（百分比）。Phase 04 之前一律使用預設值 70、85。</summary>
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

`app/MemSweep.Core/TrayText.cs`：

```csharp
using System.Globalization;

namespace MemSweep.Core;

/// <summary>系統匣圖示上的數字與滑鼠停留時的提示文字（F-02、F-05）。</summary>
public static class TrayText
{
    /// <summary>commit 使用率，限制在 0～100。</summary>
    public static int CommitPercent(SystemSnapshot snapshot) =>
        Math.Clamp(MemoryMath.Percent(snapshot.CommitUsedBytes, snapshot.CommitLimitBytes), 0, 100);

    public static string IconText(SystemSnapshot snapshot) =>
        CommitPercent(snapshot).ToString(CultureInfo.InvariantCulture);

    public static string Tooltip(SystemSnapshot snapshot) =>
        "MemSweep\n" +
        "Commit " + MemoryMath.FormatGigabytes(snapshot.CommitUsedBytes) + " / " +
        MemoryMath.FormatGigabytes(snapshot.CommitLimitBytes) + " GB（" + CommitPercent(snapshot) + "%）\n" +
        "RAM " + MemoryMath.FormatGigabytes(snapshot.PhysicalUsedBytes) + " / " +
        MemoryMath.FormatGigabytes(snapshot.PhysicalTotalBytes) + " GB";
}
```

`app/MemSweep.Core/PanelModel.cs`：

```csharp
using System.Globalization;

namespace MemSweep.Core;

/// <summary>量表：Level 為 null 表示不依門檻變色（實體 RAM）。</summary>
public sealed record GaugeModel(string Label, string ValueText, double Fraction, UsageLevel? Level);

/// <summary>小面板清單的一列；BarFraction 是相對於清單中最大一列的比例。</summary>
public sealed record PanelRow(string Name, string CountText, string GigabytesText, double BarFraction, bool IsUnattributed);

/// <summary>小面板要顯示的全部內容（F-11）。</summary>
public sealed record PanelModel(GaugeModel Commit, GaugeModel Ram, string PeakText, IReadOnlyList<PanelRow> Rows)
{
    public static PanelModel Build(GroupedSnapshot grouped, BootPeak? peak, UsageThresholds thresholds)
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

        return new PanelModel(commit, ram, DescribePeak(peak, s.Timestamp), rows);
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

- [ ] **Step 4：執行測試，確認通過**

執行：`(cd app && dotnet test)`

預期：總計 100，失敗 0。

- [ ] **Step 5：Commit**

```bash
git add app/MemSweep.Core app/MemSweep.Core.Tests
git status --short
git commit -F - <<'EOF'
新增系統匣與小面板的顯示模型

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

- [ ] **Step 6：推送**（依「共同執行規則」先 fetch 並檢查）

---

### Task 4：每 2 秒取樣迴圈

**檔案：**

- 新增：`app/MemSweep.Core/SamplingLoop.cs`
- 新增：`app/MemSweep.Core.Tests/SamplingLoopTests.cs`

**實作的條文：**

> - F-04 每 2 秒更新一次。〔Claude 決定〕
> - 背景執行緒以 `PeriodicTimer` 每 2 秒取樣一次（F-04），完成後透過 `Dispatcher` 交給 UI 更新，避免查詢新行程的資訊時卡住介面。

**介面：**

- 使用：Phase 01 的 `GroupedSnapshot`。
- 產生：`sealed class SamplingLoop(Func<GroupedSnapshot> sample, Action<GroupedSnapshot> publish, TimeSpan period)`：`static readonly TimeSpan DefaultPeriod`（2 秒）、`Task RunAsync(CancellationToken)`。立即取樣一次，之後每個週期一次；`Win32Exception`、`InvalidOperationException` 只略過那一次，其他例外讓迴圈結束；取消時正常返回。

- [ ] **Step 1：寫失敗的測試**

`app/MemSweep.Core.Tests/SamplingLoopTests.cs`：

```csharp
using System.ComponentModel;
using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class SamplingLoopTests
{
    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(20);

    [TestMethod]
    public async Task PublishesEverySample()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var published = new List<GroupedSnapshot>();
        var sample = ReportSamples.Typical();
        var loop = new SamplingLoop(
            () => sample,
            g =>
            {
                published.Add(g);
                if (published.Count == 3)
                {
                    cts.Cancel();
                }
            },
            Fast);

        await loop.RunAsync(cts.Token);

        Assert.HasCount(3, published);
        Assert.AreSame(sample, published[0]);
    }

    [TestMethod]
    public async Task SkipsFailedSamplesAndKeepsRunning()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        int calls = 0;
        int published = 0;
        var loop = new SamplingLoop(
            () =>
            {
                calls++;
                return calls switch
                {
                    1 => throw new Win32Exception(5),
                    2 => throw new InvalidOperationException("NtQuerySystemInformation(5) 失敗：0xC0000017"),
                    _ => ReportSamples.Typical(),
                };
            },
            _ =>
            {
                published++;
                if (published == 2)
                {
                    cts.Cancel();
                }
            },
            Fast);

        await loop.RunAsync(cts.Token);

        Assert.AreEqual(4, calls);
        Assert.AreEqual(2, published);
    }

    [TestMethod]
    public async Task OtherExceptionsStopTheLoop()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var loop = new SamplingLoop(() => throw new NullReferenceException(), _ => { }, Fast);

        await Assert.ThrowsExactlyAsync<NullReferenceException>(() => loop.RunAsync(cts.Token));
    }

    [TestMethod]
    public void DefaultPeriodIsTwoSeconds()
    {
        Assert.AreEqual(TimeSpan.FromSeconds(2), SamplingLoop.DefaultPeriod);
    }
}
```

- [ ] **Step 2：執行測試，確認失敗**

執行：`(cd app && dotnet test)`

預期：建置失敗，錯誤包含

```text
error CS0246: 找不到類型或命名空間名稱 'SamplingLoop' (是否遺漏了 using 指示詞或組件參考?)
error CS0103: 名稱 'SamplingLoop' 不存在於目前的內容中
```

- [ ] **Step 3：實作**

`app/MemSweep.Core/SamplingLoop.cs`：

```csharp
using System.ComponentModel;

namespace MemSweep.Core;

/// <summary>
/// 立即取樣一次，之後每隔固定時間取樣並發布結果（Design 3.4）。
/// 取樣時的 Win32 錯誤只略過那一次；其他例外視為程式錯誤，讓迴圈結束。
/// </summary>
public sealed class SamplingLoop
{
    public static readonly TimeSpan DefaultPeriod = TimeSpan.FromSeconds(2);

    private readonly Func<GroupedSnapshot> _sample;
    private readonly Action<GroupedSnapshot> _publish;
    private readonly TimeSpan _period;

    public SamplingLoop(Func<GroupedSnapshot> sample, Action<GroupedSnapshot> publish, TimeSpan period)
    {
        _sample = sample;
        _publish = publish;
        _period = period;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_period);
        do
        {
            GroupedSnapshot? grouped = null;
            try
            {
                grouped = _sample();
            }
            catch (Exception e) when (e is Win32Exception or InvalidOperationException)
            {
                // 這一次取樣失敗，畫面保留上一次的結果，下次再試
            }
            if (grouped is not null)
            {
                _publish(grouped);
            }
        }
        while (await WaitForNextTickAsync(timer, cancellationToken));
    }

    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
```

- [ ] **Step 4：執行測試，確認通過**

執行：`(cd app && dotnet test)`

預期：總計 104，失敗 0。

- [ ] **Step 5：Commit**

```bash
git add app/MemSweep.Core app/MemSweep.Core.Tests
git status --short
git commit -F - <<'EOF'
新增每 2 秒取樣迴圈

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

- [ ] **Step 6：推送**（依「共同執行規則」先 fetch 並檢查）

---

### Task 5：系統匣常駐程式

**檔案：**

- 新增：`app/MemSweep.Desktop/MemSweep.Desktop.csproj`、`App.xaml`、`App.xaml.cs`、`SingleInstance.cs`、`TrayIconRenderer.cs`、`TrayController.cs`、`Native/User32.cs`
- 修改（整份換成）：`app/MemSweep.slnx`、`app/MemSweep.Core.Tests/MemSweep.Core.Tests.csproj`、`app/MemSweep.Core.Tests/NativeSafetyTests.cs`
- 新增：`app/MemSweep.Core.Tests/SingleInstanceTests.cs`、`app/MemSweep.Core.Tests/TrayIconRendererTests.cs`

**實作的條文：**

> - F-01 MemSweep 啟動後常駐系統匣，直到使用者手動結束。關閉任何視窗都不會結束 MemSweep。
> - F-60 同一時間只允許一個 MemSweep.exe。再次啟動時，叫出既有執行個體的小面板，新的執行個體隨即結束。〔Claude 決定〕
> - 以 `System.Drawing` 依目前 DPI 的小圖示尺寸繪製數字圖示（F-02、F-03）。使用率 100% 時以較小字型畫出「100」。
> - 每次更新後釋放舊圖示的 `HICON`，避免控制代碼累積。
> - 右鍵選單（F-07）的項目隨 Phase 逐步出現：Phase 02 有「開啟小面板」「結束 MemSweep」，Phase 03 加「完整清單…」，Phase 04 加「設定…」。
> - `App.OnStartup` 在建立任何視窗前設定 `RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly`。
> - 每次取樣：`SystemReader` → `AppGrouper` → `PeakTracker`，結果發布給系統匣圖示與開著的視窗。
> - 以具名 Mutex `Local\MemSweep.SingleInstance` 判斷是否已有執行個體。
> - 第二個執行個體設定具名事件 `Local\MemSweep.ShowPanel` 後立即結束；第一個執行個體收到事件就顯示小面板。
> - **A-07 自動檢查**：以反射列出 Core、Desktop、Cli 組件中所有 P/Invoke 進入點，確認沒有 `EmptyWorkingSet`、`SetProcessWorkingSetSize`、`SetProcessWorkingSetSizeEx`、`NtSetSystemInformation`、`SetSystemFileCacheSize`、`AdjustTokenPrivileges`、`TerminateProcess`、`NtTerminateProcess`、`NtSetInformationProcess`、`VirtualFreeEx`。這項檢查只涵蓋以 `DllImport`／`LibraryImport` 宣告的呼叫。

**介面：**

- 使用：Task 2 的 `PeakTracker`、`BootPeak`；Task 3 的 `UsageLevel`、`UsageThresholds`、`TrayText`；Task 4 的 `SamplingLoop`；Phase 01 的 `MemorySampler`。
- 產生（組件名稱 `MemSweep`，命名空間 `MemSweep.Desktop`）：
  - `public sealed class SingleInstance : IDisposable`：常數 `MutexName`、`ShowPanelEventName`；`static SingleInstance? TryAcquire(string mutexName, string showPanelEventName)`；`event Action? ShowRequested`
  - `internal static class TrayIconRenderer`：`Color ColorOf(UsageLevel)`、`Icon Render(string text, UsageLevel level, int size)`
  - `internal sealed class TrayController : IDisposable`：`event Action? ExitRequested`、`void Update(GroupedSnapshot, BootPeak?)`（Task 6 會整份換掉）
  - `internal static partial class User32`：`DestroyIcon`（Task 6 會擴充）
  - `public partial class App : Application`（Task 6 會修改）

- [ ] **Step 1：寫失敗的測試**

`app/MemSweep.Core.Tests/MemSweep.Core.Tests.csproj` 整份換成：

```xml
<Project Sdk="MSTest.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\MemSweep.Core\MemSweep.Core.csproj" />
    <ProjectReference Include="..\MemSweep.Cli\MemSweep.Cli.csproj" />
    <ProjectReference Include="..\MemSweep.Desktop\MemSweep.Desktop.csproj" />
  </ItemGroup>

</Project>
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
        CollectionAssert.Contains(EntryPoints(), "DestroyIcon");
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

`app/MemSweep.Core.Tests/SingleInstanceTests.cs`：

```csharp
using MemSweep.Desktop;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class SingleInstanceTests
{
    // 每個測試用不同的名稱，不會碰到正在執行的 MemSweep
    private static (string Mutex, string Event) UniqueNames()
    {
        string id = Guid.NewGuid().ToString("N");
        return ($@"Local\MemSweep.Test.{id}", $@"Local\MemSweep.Test.{id}.ShowPanel");
    }

    [TestMethod]
    public void SecondInstanceAsksTheFirstToShowThePanel()
    {
        var (mutex, ev) = UniqueNames();
        using var first = SingleInstance.TryAcquire(mutex, ev);
        Assert.IsNotNull(first);
        using var shown = new ManualResetEventSlim();
        first.ShowRequested += shown.Set;

        var second = SingleInstance.TryAcquire(mutex, ev);

        Assert.IsNull(second);
        Assert.IsTrue(shown.Wait(TimeSpan.FromSeconds(5)));
    }

    [TestMethod]
    public void NameIsFreeAgainAfterDispose()
    {
        var (mutex, ev) = UniqueNames();
        SingleInstance.TryAcquire(mutex, ev)!.Dispose();

        using var again = SingleInstance.TryAcquire(mutex, ev);

        Assert.IsNotNull(again);
    }
}
```

`app/MemSweep.Core.Tests/TrayIconRendererTests.cs`：

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

    [TestMethod]
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

預期：建置失敗（`MemSweep.Desktop` 專案還不存在），錯誤包含

```text
warning MSB9008: 參考的專案 ..\MemSweep.Desktop\MemSweep.Desktop.csproj 不存在。
error CS0234: 命名空間 'MemSweep' 中沒有類型或命名空間名稱 'Desktop' (是否遺漏了組件參考?)
error CS1069: 命名空間 'System.Drawing' 中找不到類型名稱 'Bitmap'。此類型已轉送到組件 'System.Drawing.Common, Version=0.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51'，請考慮加入該組件的參考。
```

- [ ] **Step 3：實作**

`app/MemSweep.Desktop/MemSweep.Desktop.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <AssemblyName>MemSweep</AssemblyName>
    <RootNamespace>MemSweep.Desktop</RootNamespace>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>

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

`app/MemSweep.Desktop/Native/User32.cs`：

```csharp
using System.Runtime.InteropServices;

namespace MemSweep.Desktop.Native;

/// <summary>user32 的介面資源釋放；不涉及系統記憶體狀態。</summary>
internal static partial class User32
{
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint icon);
}
```

`app/MemSweep.Desktop/SingleInstance.cs`：

```csharp
namespace MemSweep.Desktop;

/// <summary>
/// 單一執行個體（F-60、Design 3.7）：第一個執行個體建立具名 Mutex 與事件；
/// 之後啟動的執行個體設定事件請它顯示小面板，自己隨即結束。
/// </summary>
public sealed class SingleInstance : IDisposable
{
    public const string MutexName = @"Local\MemSweep.SingleInstance";
    public const string ShowPanelEventName = @"Local\MemSweep.ShowPanel";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showPanel;
    private readonly RegisteredWaitHandle _registration;

    private SingleInstance(Mutex mutex, EventWaitHandle showPanel)
    {
        _mutex = mutex;
        _showPanel = showPanel;
        _registration = ThreadPool.RegisterWaitForSingleObject(
            showPanel, (_, _) => ShowRequested?.Invoke(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>其他執行個體要求顯示小面板；在執行緒集區上觸發。</summary>
    public event Action? ShowRequested;

    /// <summary>取得執行個體；回傳 null 表示已有執行個體在執行，並已通知它顯示小面板。</summary>
    public static SingleInstance? TryAcquire(string mutexName, string showPanelEventName)
    {
        // 只用 Mutex 是否已存在來判斷，不持有所有權，所以不必在建立它的執行緒上釋放
        var mutex = new Mutex(initiallyOwned: false, mutexName, out bool createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            if (EventWaitHandle.TryOpenExisting(showPanelEventName, out var existing))
            {
                using (existing)
                {
                    existing.Set();
                }
            }
            return null;
        }

        return new SingleInstance(mutex, new EventWaitHandle(false, EventResetMode.AutoReset, showPanelEventName));
    }

    public void Dispose()
    {
        _registration.Unregister(null);
        _showPanel.Dispose();
        _mutex.Dispose();
    }
}
```

`app/MemSweep.Desktop/TrayIconRenderer.cs`：

```csharp
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using MemSweep.Core;
using MemSweep.Desktop.Native;

namespace MemSweep.Desktop;

/// <summary>畫出系統匣的數字圖示（F-02、F-03、Design 3.5）。</summary>
internal static class TrayIconRenderer
{
    public static Color ColorOf(UsageLevel level) => level switch
    {
        UsageLevel.Green => Color.FromArgb(46, 160, 67),
        UsageLevel.Orange => Color.FromArgb(255, 159, 10),
        _ => Color.FromArgb(255, 69, 58),
    };

    /// <summary>回傳的圖示擁有自己的控制代碼，由呼叫端 Dispose。</summary>
    public static Icon Render(string text, UsageLevel level, int size)
    {
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(ColorOf(level)))
        using (var path = GlyphPath(text, size))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.FillPath(brush, path);
        }

        nint handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            // GetHicon 建立的控制代碼不會隨 Icon 釋放，必須自己銷毀
            User32.DestroyIcon(handle);
        }
    }

    // 依字形的實際外框縮放並置中，數字才能填滿小圖示；「100」較寬，會自動縮小
    private static GraphicsPath GlyphPath(string text, int size)
    {
        var path = new GraphicsPath();
        using var family = new FontFamily("Segoe UI");
        path.AddString(text, family, (int)FontStyle.Bold, 100f, PointF.Empty, StringFormat.GenericTypographic);
        var bounds = path.GetBounds();
        float margin = size >= 24 ? 2f : 1f;
        float scale = Math.Min((size - 2 * margin) / bounds.Width, (size - 2 * margin) / bounds.Height);
        using var transform = new Matrix();
        transform.Translate(size / 2f, size / 2f);
        transform.Scale(scale, scale);
        transform.Translate(-(bounds.X + bounds.Width / 2f), -(bounds.Y + bounds.Height / 2f));
        path.Transform(transform);
        return path;
    }
}
```

`app/MemSweep.Desktop/TrayController.cs`：

```csharp
using System.Drawing;
using System.Windows.Forms;
using MemSweep.Core;

namespace MemSweep.Desktop;

/// <summary>系統匣圖示、提示文字與右鍵選單（F-01～F-07）。必須在 UI 執行緒上使用。</summary>
internal sealed class TrayController : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private Icon? _icon;
    private string? _iconKey;

    public TrayController()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("結束 MemSweep", null, (_, _) => ExitRequested?.Invoke());
        _notifyIcon = new NotifyIcon { Text = "MemSweep", ContextMenuStrip = menu };
    }

    public event Action? ExitRequested;

    public void Update(GroupedSnapshot grouped, BootPeak? peak)
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
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _icon?.Dispose();
    }
}
```

`app/MemSweep.Desktop/App.xaml`：

```xml
<Application x:Class="MemSweep.Desktop.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             ShutdownMode="OnExplicitShutdown" />
```

`app/MemSweep.Desktop/App.xaml.cs`：

```csharp
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using MemSweep.Core;

namespace MemSweep.Desktop;

/// <summary>常駐系統匣，直到使用者選「結束 MemSweep」（F-01）。</summary>
public partial class App : Application
{
    private SingleInstance? _instance;
    private TrayController? _tray;
    private CancellationTokenSource? _sampling;

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

        _tray = new TrayController();
        _tray.ExitRequested += () => Shutdown();
        StartSampling();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _sampling?.Cancel();
        _tray?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private void StartSampling()
    {
        // MemorySampler 與 PeakTracker 只在取樣迴圈裡依序使用，不會同時被兩個執行緒存取
        var sampler = MemorySampler.CreateDefault();
        var tracker = new PeakTracker();
        var loop = new SamplingLoop(
            () =>
            {
                var grouped = sampler.Sample();
                tracker.Update(grouped);
                return grouped;
            },
            grouped =>
            {
                var peak = tracker.BootPeak;
                Dispatcher.InvokeAsync(() => _tray?.Update(grouped, peak));
            },
            SamplingLoop.DefaultPeriod);

        _sampling = new CancellationTokenSource();
        var token = _sampling.Token;
        Task.Run(() => loop.RunAsync(token)).ContinueWith(
            task => Dispatcher.InvokeAsync(() => throw new InvalidOperationException("取樣迴圈意外結束", task.Exception)),
            TaskContinuationOptions.OnlyOnFaulted);
    }
}
```

`app/MemSweep.slnx` 整份換成：

```xml
<Solution>
  <Project Path="MemSweep.Cli/MemSweep.Cli.csproj" />
  <Project Path="MemSweep.Core.Tests/MemSweep.Core.Tests.csproj" />
  <Project Path="MemSweep.Core/MemSweep.Core.csproj" />
  <Project Path="MemSweep.Desktop/MemSweep.Desktop.csproj" />
</Solution>
```

- [ ] **Step 4：執行測試，確認通過**

執行：`(cd app && dotnet test)`

預期：總計 115，失敗 0。

- [ ] **Step 5：Commit**

```bash
git add app/MemSweep.slnx app/MemSweep.Desktop app/MemSweep.Core.Tests
git status --short
git commit -F - <<'EOF'
新增系統匣常駐程式

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

- [ ] **Step 6：推送**（依「共同執行規則」先 fetch 並檢查）

---

### Task 6：小面板

**檔案：**

- 新增：`app/MemSweep.Desktop/FlyoutPanel.xaml`、`FlyoutPanel.xaml.cs`、`FlyoutWindow.xaml`、`FlyoutWindow.xaml.cs`、`FlyoutPlacement.cs`、`Theme.cs`、`Themes/Light.xaml`、`Themes/Dark.xaml`
- 修改（整份換成）：`app/MemSweep.Desktop/TrayController.cs`、`App.xaml.cs`、`SingleInstance.cs`、`Native/User32.cs`
- 新增：`app/MemSweep.Core.Tests/FlyoutTests.cs`

**實作的條文：**

> - F-06 左鍵點擊開啟小面板（7.2）。
> - F-10 從螢幕右下角彈出，點面板以外的地方就收起。
> - F-12 按鈕：
> - 「複製報告」：把與 `memsweep-cli status`（F-40）相同的純文字摘要複製到剪貼簿。〔Claude 決定〕
> - 「結束 MemSweep」。
> - F-61 介面使用繁體中文，深淺色跟隨 Windows 設定。〔Claude 決定〕
> - A-08 關閉小面板與完整視窗後，MemSweep 仍在系統匣；只有「結束 MemSweep」能結束它。
> - 小面板（F-10～F-12）：無邊框、不顯示在工作列、最上層；出現在主螢幕工作區的右下角（Windows 11 的通知區域固定在主螢幕底部的工作列）；失去焦點或按 Esc 時隱藏。視窗在第一次開啟時才建立，之後以顯示／隱藏切換（量測顯示關閉視窗也不會釋放記憶體，隱藏可讓重開更快）。Phase 02 實測：只掛在系統匣時行程約 18 MB，若啟動時就建立小面板約 58 MB，開過小面板後約 80 MB。
> - 小面板開著時點系統匣圖示，焦點會先移走讓小面板隱藏；隱藏後 400 毫秒內的同一次點擊不再把它打開，所以點圖示可以開關小面板。
> - 深淺色（F-61）：自備淺色與深色兩組 `ResourceDictionary`，依 `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize` 的 `AppsUseLightTheme` 切換，並監聽 `SystemEvents.UserPreferenceChanged`。不使用 WPF 仍在實驗階段的 `ThemeMode`。
> - 「複製報告」（F-12）使用 `ReportFormatter` 的文字摘要。
> - 第二個執行個體是使用者剛啟動的前景行程，設定事件前先呼叫 `AllowSetForegroundWindow(ASFW_ANY)` 把前景許可讓出，第一個執行個體的小面板才拿得到焦點。

**介面：**

- 使用：Task 3 的 `PanelModel`、`PanelRow`、`UsageThresholds`；Task 5 的 `TrayController`、`SingleInstance`、`User32`、`App`。
- 產生：
  - `public partial class FlyoutPanel : UserControl`（DataContext 是 `PanelModel`）：`event Action? CopyClicked`、`event Action? ExitClicked`、`void ShowCopyResult(bool succeeded)`
  - `public partial class FlyoutWindow : Window`：`PanelView`（`FlyoutPanel`）、`bool WasJustHidden`、`void ShowPanel()`、`void HidePanel()`
  - `internal static class FlyoutPlacement`：`const double Margin = 12`、`(double Left, double Top) Place(double areaRight, double areaBottom, double width, double height)`
  - `internal enum ThemeKind { Light, Dark }`、`internal static class ThemeReader`（`FromRegistryValue`、`ReadCurrent`）、`internal sealed class ThemeWatcher : IDisposable`
  - `TrayController.ShowFlyout()`；`User32.AllowSetForegroundWindow`、`User32.AsfwAny`

- [ ] **Step 1：寫失敗的測試**

`app/MemSweep.Core.Tests/FlyoutTests.cs`：

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

預期：建置失敗，錯誤包含

```text
error CS0246: 找不到類型或命名空間名稱 'FlyoutPanel' (是否遺漏了 using 指示詞或組件參考?)
```

- [ ] **Step 3：實作**

`app/MemSweep.Desktop/FlyoutPlacement.cs`：

```csharp
namespace MemSweep.Desktop;

/// <summary>小面板放在工作區右下角，與邊緣保留固定間距（F-10、Design 3.6）。</summary>
internal static class FlyoutPlacement
{
    public const double Margin = 12;

    public static (double Left, double Top) Place(double areaRight, double areaBottom, double width, double height) =>
        (areaRight - width - Margin, areaBottom - height - Margin);
}
```

`app/MemSweep.Desktop/Theme.cs`：

```csharp
using System.Windows;
using Microsoft.Win32;

namespace MemSweep.Desktop;

internal enum ThemeKind
{
    Light,
    Dark,
}

/// <summary>讀取 Windows 的應用程式深淺色設定（F-61、Design 3.6）。</summary>
internal static class ThemeReader
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>AppsUseLightTheme 為 0 是深色；沒有這個值或型別不對時視為淺色。</summary>
    public static ThemeKind FromRegistryValue(object? value) =>
        value is int number && number == 0 ? ThemeKind.Dark : ThemeKind.Light;

    public static ThemeKind ReadCurrent()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return FromRegistryValue(key?.GetValue("AppsUseLightTheme"));
    }
}

/// <summary>依目前設定套用淺色或深色資源，並在使用者切換時跟著換。</summary>
internal sealed class ThemeWatcher : IDisposable
{
    private readonly ResourceDictionary _resources;
    private ThemeKind? _current;

    public ThemeWatcher(ResourceDictionary resources)
    {
        _resources = resources;
        Apply();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // 這個事件在 SystemEvents 自己的執行緒上觸發
        if (e.Category == UserPreferenceCategory.General)
        {
            Application.Current?.Dispatcher.InvokeAsync(Apply);
        }
    }

    private void Apply()
    {
        var kind = ThemeReader.ReadCurrent();
        if (kind == _current)
        {
            return;
        }
        _current = kind;
        _resources.MergedDictionaries.Clear();
        _resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/MemSweep;component/Themes/{kind}.xaml"),
        });
    }
}
```

`app/MemSweep.Desktop/Themes/Light.xaml`：

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
</ResourceDictionary>
```

`app/MemSweep.Desktop/Themes/Dark.xaml`：

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
</ResourceDictionary>
```

`app/MemSweep.Desktop/FlyoutPanel.xaml`：

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

`app/MemSweep.Desktop/FlyoutPanel.xaml.cs`：

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

    public event Action? CopyClicked;

    public event Action? ExitClicked;

    /// <summary>按下「複製報告」後，按鈕文字短暫顯示結果。</summary>
    public void ShowCopyResult(bool succeeded)
    {
        CopyButton.Content = succeeded ? "已複製" : "複製失敗";
        _copyFeedback.Stop();
        _copyFeedback.Start();
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e) => CopyClicked?.Invoke();

    private void ExitButton_Click(object sender, RoutedEventArgs e) => ExitClicked?.Invoke();
}
```

`app/MemSweep.Desktop/FlyoutWindow.xaml`：

```xml
<Window x:Class="MemSweep.Desktop.FlyoutWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:MemSweep.Desktop"
        Title="MemSweep"
        WindowStyle="None"
        AllowsTransparency="True"
        Background="Transparent"
        ResizeMode="NoResize"
        ShowInTaskbar="False"
        Topmost="True"
        SizeToContent="WidthAndHeight">
  <local:FlyoutPanel x:Name="PanelView" />
</Window>
```

`app/MemSweep.Desktop/FlyoutWindow.xaml.cs`：

```csharp
using System.Windows;
using System.Windows.Input;

namespace MemSweep.Desktop;

/// <summary>
/// 小面板視窗（F-10）：無邊框、不在工作列、最上層；失去焦點或按 Esc 就隱藏。
/// 只建立一次，之後以顯示／隱藏切換（Design 3.6）。
/// </summary>
public partial class FlyoutWindow : Window
{
    private DateTime _hiddenAtUtc = DateTime.MinValue;

    public FlyoutWindow()
    {
        InitializeComponent();
        Deactivated += (_, _) => HidePanel();
        SizeChanged += (_, _) => PlaceAtCorner();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                HidePanel();
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
        Activate();
    }

    public void HidePanel()
    {
        if (!IsVisible)
        {
            return;
        }
        Hide();
        _hiddenAtUtc = DateTime.UtcNow;
    }

    private void PlaceAtCorner()
    {
        // Windows 11 的通知區域固定在主螢幕底部的工作列，所以用主螢幕的工作區
        var area = SystemParameters.WorkArea;
        (Left, Top) = FlyoutPlacement.Place(area.Right, area.Bottom, ActualWidth, ActualHeight);
    }
}
```

`app/MemSweep.Desktop/Native/User32.cs` 整份換成：

```csharp
using System.Runtime.InteropServices;

namespace MemSweep.Desktop.Native;

/// <summary>user32 的介面資源與前景視窗許可；不涉及系統記憶體狀態。</summary>
internal static partial class User32
{
    /// <summary>ASFW_ANY：允許任何行程把視窗帶到前景。</summary>
    public const uint AsfwAny = uint.MaxValue;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint icon);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AllowSetForegroundWindow(uint processId);
}
```

`app/MemSweep.Desktop/SingleInstance.cs` 整份換成：

```csharp
using MemSweep.Desktop.Native;

namespace MemSweep.Desktop;

/// <summary>
/// 單一執行個體（F-60、Design 3.7）：第一個執行個體建立具名 Mutex 與事件；
/// 之後啟動的執行個體設定事件請它顯示小面板，自己隨即結束。
/// </summary>
public sealed class SingleInstance : IDisposable
{
    public const string MutexName = @"Local\MemSweep.SingleInstance";
    public const string ShowPanelEventName = @"Local\MemSweep.ShowPanel";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showPanel;
    private readonly RegisteredWaitHandle _registration;

    private SingleInstance(Mutex mutex, EventWaitHandle showPanel)
    {
        _mutex = mutex;
        _showPanel = showPanel;
        _registration = ThreadPool.RegisterWaitForSingleObject(
            showPanel, (_, _) => ShowRequested?.Invoke(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>其他執行個體要求顯示小面板；在執行緒集區上觸發。</summary>
    public event Action? ShowRequested;

    /// <summary>取得執行個體；回傳 null 表示已有執行個體在執行，並已通知它顯示小面板。</summary>
    public static SingleInstance? TryAcquire(string mutexName, string showPanelEventName)
    {
        // 只用 Mutex 是否已存在來判斷，不持有所有權，所以不必在建立它的執行緒上釋放
        var mutex = new Mutex(initiallyOwned: false, mutexName, out bool createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            if (EventWaitHandle.TryOpenExisting(showPanelEventName, out var existing))
            {
                using (existing)
                {
                    // 使用者剛啟動的這個行程在前景，把前景許可讓給第一個執行個體，小面板才拿得到焦點
                    User32.AllowSetForegroundWindow(User32.AsfwAny);
                    existing.Set();
                }
            }
            return null;
        }

        return new SingleInstance(mutex, new EventWaitHandle(false, EventResetMode.AutoReset, showPanelEventName));
    }

    public void Dispose()
    {
        _registration.Unregister(null);
        _showPanel.Dispose();
        _mutex.Dispose();
    }
}
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
    private BootPeak? _latestPeak;

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
        flyout.PanelView.DataContext = PanelModel.Build(_latest, _latestPeak, UsageThresholds.Default);
        flyout.ShowPanel();
    }

    public void Update(GroupedSnapshot grouped, BootPeak? peak)
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
        _latestPeak = peak;
        if (_flyout is { IsVisible: true })
        {
            _flyout.PanelView.DataContext = PanelModel.Build(grouped, peak, UsageThresholds.Default);
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
        _tray?.Dispose();
        _theme?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private void StartSampling()
    {
        // MemorySampler 與 PeakTracker 只在取樣迴圈裡依序使用，不會同時被兩個執行緒存取
        var sampler = MemorySampler.CreateDefault();
        var tracker = new PeakTracker();
        var loop = new SamplingLoop(
            () =>
            {
                var grouped = sampler.Sample();
                tracker.Update(grouped);
                return grouped;
            },
            grouped =>
            {
                var peak = tracker.BootPeak;
                Dispatcher.InvokeAsync(() => _tray?.Update(grouped, peak));
            },
            SamplingLoop.DefaultPeriod);

        _sampling = new CancellationTokenSource();
        var token = _sampling.Token;
        Task.Run(() => loop.RunAsync(token)).ContinueWith(
            task => Dispatcher.InvokeAsync(() => throw new InvalidOperationException("取樣迴圈意外結束", task.Exception)),
            TaskContinuationOptions.OnlyOnFaulted);
    }
}
```

- [ ] **Step 4：執行測試，確認通過**

執行：`(cd app && dotnet test)`

預期：總計 120，失敗 0。

- [ ] **Step 5：Commit**

```bash
git add app/MemSweep.Desktop app/MemSweep.Core.Tests
git status --short
git commit -F - <<'EOF'
新增小面板

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

- [ ] **Step 6：推送**（依「共同執行規則」先 fetch 並檢查）

---

## Phase 收尾

- [ ] **完整檢查**：`(cd app && dotnet build -c Release)` 為 0 個警告、0 個錯誤；`(cd app && dotnet test)` 總計 120，失敗 0。
- [ ] **A-03 量測**：執行 `app/artifacts/bin/MemSweep.Desktop/release/MemSweep.exe`，再執行一次同一個 exe 叫出小面板，之後放著不動 60 分鐘，再以 `(Get-Process MemSweep).PrivateMemorySize64` 量測，必須不超過 100 MB。量完用小面板的「結束 MemSweep」結束。
- [ ] **使用者驗收 A-08**：點小面板以外的地方、按 Esc、再點一次系統匣圖示，小面板都會收起而圖示仍在；只有「結束 MemSweep」（小面板按鈕或右鍵選單）會結束程式。
- [ ] **使用者外觀確認**：圖示數字與顏色（綠／橘／紅）；切換 Windows「應用程式模式」深淺色後，小面板跟著換色；「複製報告」貼上的內容與 `memsweep-cli status` 相同。
- [ ] **開 PR**：所有 Task 都推送後，用 `/repo-open-pr` 開 PR（base `main`，head `phase-02-tray-flyout`）。合併由使用者決定，使用 merge commit。
