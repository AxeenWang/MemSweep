# MemSweep Phase 05：以 RAM 為主 實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**目標：** 系統匣圖示的數字與顏色、小面板、「總覽」與「程式」分頁改以實體 RAM 為主；commit 退到第二位，仍顯示在提示文字、小面板與「程式」分頁。尖峰紀錄、`memsweep-cli`（含 JSON 與 `check`）與「複製報告」維持以 commit 為主，輸出完全不變。

**架構：** `SystemReader` 從原本就在讀的 `SYSTEM_PROCESS_INFORMATION` 多取 `WorkingSetPrivateSize`（私用工作集），不新增 API。`AppGrouper` 讓每個應用程式與明細同時有 commit 與 RAM 合計，「系統（不屬於任何程式）」也有兩種計法；`GroupedSnapshot.Apps` 維持依 commit 排序（命令列、報告、尖峰紀錄照舊），畫面改用新的 `AppsByRam`。`TrayText` 的數字、顏色與提示改看 RAM；`PanelModel` 的 RAM 量表依門檻變色、commit 量表不變色，前五名與「系統」列以 RAM 計；「程式」分頁依 RAM 排序，欄位改為 RAM、commit。

**技術：** C#、.NET 10、WPF（軟體繪圖）、WinForms `NotifyIcon`、MSTest 4.5.1（含 `[STATestMethod]`）。

**依據：** [SPEC](../spec.md)（1.4 與相關條文）、[Design](../design.md)（第 5 章 Phase 05、3.1、3.2、3.5、3.6、3.11、第 4 章、第 6 章風險 5），兩者於 main 的 abde774 核准。本計畫引用的條文都是原文；計畫自己的解讀集中在「計畫的解讀」一節。

**分支：** `phase-05-ram-first`（開工時從 `origin/main` 建立）。

## 全域限制

以下條文適用於每個 Task：

> - Identifiers are English: code symbols, file and folder names, and branch names.
> - Documents, code comments, and commit messages are written in Traditional Chinese (Taiwan).
> - Commit subjects are Traditional Chinese only, with no type prefix such as `feat:` or `fix:`.
> - Push after each completed Task. Open the pull request only after every Task in the Phase is done.
> - Running anything that changes system memory state requires the user's explicit approval for that run. This includes trimming the working sets of other processes, purging the standby or modified page lists, flushing the file cache, and enabling token privileges.
> - Do not start elevated (administrator) processes or change system settings such as the page file without the user's explicit approval.
> - Never terminate the user's processes to free memory.
> - Run every `dotnet` command inside `app/`, because `app/global.json` pins the SDK, the MSTest.Sdk version, and the test runner. For example: `(cd app && dotnet build)`.
> - E-5 所有系統查詢都是唯讀。
> - X-1 MemSweep 本身成為記憶體大戶。常駐時本身的 commit 目標不超過 100 MB。〔Claude 決定〕
> - X-3 顯示的數字和 Windows 工作管理員對不起來，讓使用者無法信任。〔Claude 決定〕
> - A-07 程式碼中沒有任何改變系統記憶體狀態或提升權限的呼叫，例如 `EmptyWorkingSet`、`SetProcessWorkingSetSize`、`NtSetSystemInformation`、`AdjustTokenPrivileges`。

範圍（SPEC 1.4，不在這個 Phase 改動）：

> 尖峰紀錄、命令列與「複製報告」維持以 commit 為主：尖峰紀錄是事後查 commit 是否接近上限的唯一依據；命令列與「複製報告」用於讓 Claude 判斷會不會遇到 ENOMEM，而決定這件事的是 commit〔使用者決定〕。

## Review Focus

SPEC 沒有直接寫到、但實際使用最可能出問題的情況。每一項都已在負責的 Task 裡加上測試：

1. 圖示的數字和工作管理員差 1：Windows 的 `dwMemoryLoad` 是無條件捨去，MemSweep 沿用四捨五入（和 commit 一致），兩者最多差 1 個百分點，在 A-10 的容許範圍內。Task 3 `RamPercentMatchesTheMemoryLoad`（即時測試）；工作管理員標題的實際算法由 Phase 收尾的 A-10 人工驗收確認。
2. 兩個程式的 RAM 剛好相同：依名稱排序，畫面不會因為更新順序而跳動。Task 2 `AppsByRamOrdersByRamWhileAppsKeepTheCommitOrder`，Task 5 `ComponentsAreSortedByRamAndShowTheProcessCountAfterTheName`。
3. 取樣當下所有行程的 RAM 合計大於「RAM 已用」（兩者不是同一瞬間取得）：「系統」列以 0 計，不會出現負數。Task 2 `UnattributedRamIsRamInUseMinusAllProcessRam`。
4. 「系統（不屬於任何程式）」以 RAM 計通常是最大的一列（開工檢查時約 8 GB，含核心、驅動程式、共用的 DLL 與記憶體壓縮）：前五名的長條以含「系統」列在內的最大值為準，其他列不會全部畫滿。Task 4 `PanelRowsAreTheTopFiveByRamPlusUnattributedWithBarsRelativeToTheLargest`。
5. 可用量為 0 或取樣時大於總量造成使用率超過 100%：圖示最多顯示「100」。Task 3 `TrayIconTextNeverExceeds100`。

## 計畫的解讀

以下是本計畫自己的決定，不是 SPEC 或 Design 的原文：

- **開工檢查（Design 風險 5，2026-10-28 已完成）**：以唯讀查詢比對，（總量 − 可用量）÷ 總量 = 60.57%，`GlobalMemoryStatusEx.dwMemoryLoad` = 60%；406 個行程的 `WorkingSetPrivateSize` 與效能計數器 `Process\Working Set - Private`（工作管理員使用的同一組資料）387 個完全相同、403 個相差 5% 以內，其餘 3 個是兩次查詢之間的變動；沒有任何行程的私用工作集大於工作集。口徑一致，不需要修改 Design。
- **資料**：`ProcessSample` 新增 `PrivateWorkingSetBytes`；`WorkingSetBytes` 保留，只供即時測試核對（Design 3.2）。`AppGroup`、`AppComponent` 的 `WorkingSetBytes` 改名為 `RamBytes`，內容改為私用工作集的合計；原本含共用部分的工作集合計不再計算。
- **「系統」列**：`GroupedSnapshot.UnattributedBytes` 改名為 `UnattributedCommitBytes`，並新增 `UnattributedRamBytes`。只改程式裡的名稱，JSON 的 `unattributedBytes` 欄位名稱與內容都不變（SPEC 7.5）。
- **排序**：`GroupedSnapshot.AppsByRam` 依 RAM 由大到小，相同時依名稱（`StringComparer.Ordinal`）；`Apps` 維持依 commit，命令列、報告與尖峰紀錄都不用改。明細只在「程式」分頁顯示，由 `AppListRow` 依 RAM 排序，`AppGrouper` 裡明細的順序不變。
- **使用率**：RAM 使用率沿用 `MemoryMath.Percent` 的四捨五入並限制在 0～100，和 commit 使用率相同。
- **圖示顏色**：抽出 `TrayText.IconLevel(snapshot, thresholds)`，圖示顏色才能單元測試；`TrayController` 只改成呼叫它（`TrayController` 會建立系統匣圖示，不寫單元測試，由 Phase 收尾的驗收確認）。
- **小面板**：`PanelModel` 的參數順序改為 `Ram` 在前，和畫面一致。commit 量表使用原本 RAM 量表的 `AccentBrush`（Design 3.6 的「中性色」）。
- **即時測試**：A-10 的自動化替代是 RAM 使用率與 `dwMemoryLoad` 相差 1 以內；A-11 的自動化替代是測試行程自己的私用工作集與 `K32GetProcessMemoryInfo`（`PROCESS_MEMORY_COUNTERS_EX2.PrivateWorkingSetSize`）相差 25% 以內。後者的宣告放在測試專案的 `TestNative`，A-07 只掃描 Core、Desktop、Cli 組件。
- **測試資料**：`ReportSamples.Typical()` 的六個應用程式改為各自指定 RAM，依 RAM 的順序（Windows、Claude、Microsoft Edge、WebView2、ChatGPT、Node.js）和依 commit 的順序不同；Claude 的 RAM 維持 1.6 GB，「系統」列以 RAM 計為 12.3 GB（20.5 − 8.2）。`AppGrouperTests` 的工作集與 RAM 刻意給不同的值（commit 的一半與四分之一），分得出程式用的是哪一個。
- **原型驗證**：本計畫的每一段程式碼都取自 `.camp_claude` 裡的原型，每個 Task 都先看到 Step 2 的失敗再實作；共 28 個刻意改壞的規則都被測試抓到（例如前五名改回依 commit、「系統」列改用 commit、RAM 相同時不依名稱、XAML 的欄位或顏色繫結對調）。原型的 Release 建置 0 個警告，`dotnet test` 總計 341，失敗 0。
- **不在這個分支**：版本號 1.1.0 在合併後依 AGENTS.md「Releases」另開 `release/v1.1.0`；AGENTS.md 與 AGENTS_cht.md 的專案描述已隨 SPEC 與 Design 提交到 main（abde774）。

## 共同執行規則

- 工作目錄是 repo 根目錄 `D:\AxeenWorld\CodeLab\Projects\MemSweep`，分支 `phase-05-ram-first`。
- `dotnet` 指令一律在 `app/` 底下執行，用子殼層避免改變目前目錄，例如 `(cd app && dotnet test)`。
- 程式碼區塊要逐字照抄，包括全形符號：`：`、`（`、`）`、`「`、`」`、`、`、`～`、`×`、`−`。XAML 的屬性順序與縮排也照抄。「找到這一段」必須在檔案中只出現一次，整段換成後不要刪除或移動步驟沒有提到的任何一行。
- 測試就是規格：不得修改或刪除測試裡的任何斷言。照抄後測試仍失敗，就回報 BLOCKED。
- 即時測試（`Live` 類別）全部是唯讀查詢；`[STATestMethod]` 的測試會在 STA 執行緒建立 WPF 控制項，但不顯示視窗。不得執行任何會改變系統記憶體狀態的動作，也不得結束使用者的行程。
- 需要的暫存檔放在 `.camp_claude/` 的任務資料夾。commit 訊息用 Bash 的 heredoc 傳給 `git commit -F -`，不要用 PowerShell 寫訊息檔。
- commit 訊息與 `Co-Authored-By` 尾行照計畫原樣使用。
- 每個 Task 完成後推送。推送前先 `git fetch origin`，並確認遠端分支仍存在、沒有已合併或已關閉的 PR：

  ```bash
  git fetch origin
  git ls-remote --heads origin phase-05-ram-first
  gh pr list --head phase-05-ram-first --state all
  git push
  ```

  Task 1 是第一次推送，遠端還沒有這個分支，改用 `git push -u origin phase-05-ram-first`。

---

### Task 1：讀取各行程的私用工作集

**檔案：**

- 修改：`app/MemSweep.Core/ProcessSample.cs`、`app/MemSweep.Core/SystemReader.cs`
- 測試：`app/MemSweep.Core.Tests/TestNative.cs`、`app/MemSweep.Core.Tests/SystemReaderLiveTests.cs`；`AppGrouperTests.cs`、`ProcessInfoCacheTests.cs`、`Win32ProcessInfoProviderLiveTests.cs` 只補上新的建構參數

**實作的條文：**

> | 行程 RAM | 單一行程的私用工作集：只屬於這個行程、目前放在實體 RAM 裡的部分，不含與其他行程共用的部分（例如共用的 DLL），即工作管理員「詳細資料」頁的「記憶體」欄 |
> - A-11 在取樣時間相近時，任一行程的 RAM 和工作管理員「詳細資料」頁的「記憶體」欄相差不超過 5%。〔Claude 決定〕

> - 行程 RAM 採用同一筆 `SYSTEM_PROCESS_INFORMATION` 的 `WorkingSetPrivateSize`（私用工作集），不新增 API；預期與工作管理員「詳細資料」頁的「記憶體」欄一致，Phase 05 以 A-11 驗證。原本的工作集（`WorkingSetSize`，含共用的 DLL 等）只用在 F-23 的「實際 RAM」欄，Phase 05 起不再顯示；`SystemReader` 仍讀取它，供即時測試核對私用工作集不大於工作集（第 4 章）。
> - **即時系統測試**（標記 `Live` 類別，唯讀）：`SystemReader` 取得的數值合理；commit 已用與 `Win32_OperatingSystem` 的數值相差在容許範圍內，作為 A-01 的自動化替代。Phase 05 起加上：各行程的私用工作集不大於工作集；RAM 使用率與 `GlobalMemoryStatusEx` 的 `dwMemoryLoad` 相差不超過 1 個百分點，作為 A-10 的自動化替代。

**介面：**

- 產生：`ProcessSample(int Pid, int ParentPid, DateTime CreateTimeUtc, string ImageName, long PrivateCommitBytes, long WorkingSetBytes, long PrivateWorkingSetBytes)`
- 測試用：`TestNative.GetPrivateWorkingSet()` 回傳目前行程的私用工作集（`long`）

- [ ] **Step 1：寫失敗的測試**

修改 `app/MemSweep.Core.Tests/TestNative.cs`：

找到這一段：

```csharp
        return status;
    }
```

整段換成：

```csharp
        return status;
    }

    // PROCESS_MEMORY_COUNTERS_EX2：PrivateWorkingSetSize 是另一個取得私用工作集的 API
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCountersEx2
    {
        public uint Size;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
        public nuint PrivateUsage;
        public nuint PrivateWorkingSetSize;
        public ulong SharedCommitUsage;
    }

    [DllImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfo(nint process, ref ProcessMemoryCountersEx2 counters, uint size);

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    /// <summary>目前行程的私用工作集。</summary>
    public static long GetPrivateWorkingSet()
    {
        var counters = new ProcessMemoryCountersEx2 { Size = (uint)Marshal.SizeOf<ProcessMemoryCountersEx2>() };
        if (!GetProcessMemoryInfo(GetCurrentProcess(), ref counters, counters.Size))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        return (long)counters.PrivateWorkingSetSize;
    }
```

修改 `app/MemSweep.Core.Tests/SystemReaderLiveTests.cs`：

找到這一段：

```csharp
        Assert.IsLessThan(1.0, Math.Abs((current.StartTime.ToUniversalTime() - self.CreateTimeUtc).TotalSeconds));
    }

    [TestMethod]
```

整段換成：

```csharp
        Assert.IsLessThan(1.0, Math.Abs((current.StartTime.ToUniversalTime() - self.CreateTimeUtc).TotalSeconds));
    }

    [TestMethod]
    public void PrivateWorkingSetMatchesGetProcessMemoryInfo()
    {
        var snapshot = new SystemReader().Read();
        long expected = TestNative.GetPrivateWorkingSet();

        // 行程 RAM 用另一個 API 交叉比對，作為 A-11 的自動化替代；兩次查詢之間會變動，只要求相差 25% 以內
        var self = snapshot.Processes.Single(p => p.Pid == Environment.ProcessId);
        Assert.AreEqual((double)expected, self.PrivateWorkingSetBytes, expected * 0.25);
    }

    [TestMethod]
    public void PrivateWorkingSetIsPartOfTheWorkingSet()
    {
        var snapshot = new SystemReader().Read();

        var larger = snapshot.Processes.Where(p => p.PrivateWorkingSetBytes > p.WorkingSetBytes).Select(p => p.ImageName).ToList();
        Assert.IsEmpty(larger);
        Assert.IsGreaterThan(0L, snapshot.Processes.Sum(p => p.PrivateWorkingSetBytes));
    }

    [TestMethod]
```

修改 `app/MemSweep.Core.Tests/AppGrouperTests.cs`：

找到這一段：

```csharp
            _processes.Add(new ProcessSample(pid, parentPid, created, imageName, commitMb * Mb, commitMb * Mb / 2));
```

整段換成：

```csharp
            _processes.Add(new ProcessSample(pid, parentPid, created, imageName, commitMb * Mb, commitMb * Mb / 2, commitMb * Mb / 4));
```

修改 `app/MemSweep.Core.Tests/ProcessInfoCacheTests.cs`：

找到這一段：

```csharp
        new(pid, 1, created, name, 0, 0);
```

整段換成：

```csharp
        new(pid, 1, created, name, 0, 0, 0);
```

修改 `app/MemSweep.Core.Tests/Win32ProcessInfoProviderLiveTests.cs`：

（1）找到這一段：

```csharp
        Assert.AreSame(ProcessInfo.Unknown, provider.GetInfo(new ProcessSample(0, 0, DateTime.MinValue, "Idle", 0, 0)));
        Assert.AreSame(ProcessInfo.Unknown, provider.GetInfo(new ProcessSample(4, 0, DateTime.MinValue, "System", 0, 0)));
```

整段換成：

```csharp
        Assert.AreSame(ProcessInfo.Unknown, provider.GetInfo(new ProcessSample(0, 0, DateTime.MinValue, "Idle", 0, 0, 0)));
        Assert.AreSame(ProcessInfo.Unknown, provider.GetInfo(new ProcessSample(4, 0, DateTime.MinValue, "System", 0, 0, 0)));
```

（2）找到這一段：

```csharp
        var info = new Win32ProcessInfoProvider().GetInfo(new ProcessSample(3, 0, DateTime.MinValue, "ghost.exe", 0, 0));
```

整段換成：

```csharp
        var info = new Win32ProcessInfoProvider().GetInfo(new ProcessSample(3, 0, DateTime.MinValue, "ghost.exe", 0, 0, 0));
```

- [ ] **Step 2：確認測試失敗**

執行：`(cd app && dotnet test)`

預期：建置失敗，錯誤為 `CS1061: 'ProcessSample' 未包含 'PrivateWorkingSetBytes' 的定義` 與 `CS1729: 'ProcessSample' 未包含使用 7 個引數的建構函式`。

- [ ] **Step 3：實作**

修改 `app/MemSweep.Core/ProcessSample.cs`：

（1）找到這一段：

```csharp
/// <param name="WorkingSetBytes">工作集，即實際佔用的 RAM。</param>
```

整段換成：

```csharp
/// <param name="WorkingSetBytes">工作集，含與其他行程共用的部分（例如共用的 DLL）；只供測試核對私用工作集。</param>
/// <param name="PrivateWorkingSetBytes">私用工作集，即 SPEC 的「行程 RAM」與工作管理員「詳細資料」頁的「記憶體」欄。</param>
```

（2）找到這一段：

```csharp
    long WorkingSetBytes);
```

整段換成：

```csharp
    long WorkingSetBytes,
    long PrivateWorkingSetBytes);
```

修改 `app/MemSweep.Core/SystemReader.cs`：

找到這一段：

```csharp
                (long)entry->WorkingSetSize));
```

整段換成：

```csharp
                (long)entry->WorkingSetSize,
                entry->WorkingSetPrivateSize));
```

- [ ] **Step 4：確認測試通過**

執行：`(cd app && dotnet test)`

預期：總計 335，失敗 0。

- [ ] **Step 5：commit 並推送**

```bash
git add app/MemSweep.Core/ProcessSample.cs app/MemSweep.Core/SystemReader.cs app/MemSweep.Core.Tests/TestNative.cs app/MemSweep.Core.Tests/SystemReaderLiveTests.cs app/MemSweep.Core.Tests/AppGrouperTests.cs app/MemSweep.Core.Tests/ProcessInfoCacheTests.cs app/MemSweep.Core.Tests/Win32ProcessInfoProviderLiveTests.cs
git commit -F - <<'EOF'
讀取各行程的私用工作集

SystemReader 從同一筆 SYSTEM_PROCESS_INFORMATION 多取 WorkingSetPrivateSize，
作為行程 RAM（Design 3.2）；工作集保留給測試核對。即時測試以
K32GetProcessMemoryInfo 交叉比對，作為 A-11 的自動化替代。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
git fetch origin
git ls-remote --heads origin phase-05-ram-first
gh pr list --head phase-05-ram-first --state all
git push -u origin phase-05-ram-first
```

---

### Task 2：歸組加上 RAM 合計與以 RAM 計的「系統」列

**檔案：**

- 修改：`app/MemSweep.Core/GroupedSnapshot.cs`（整份換成）、`app/MemSweep.Core/AppGrouper.cs`；跟著改名的 `AppListRow.cs`、`FullWindowModel.cs`、`PanelModel.cs`、`ReportFormatter.cs`
- 測試：`app/MemSweep.Core.Tests/AppGrouperTests.cs`、`MemorySamplerLiveTests.cs`、`ReportSamples.cs`；跟著改名的 `DisplayModelTests.cs`、`ReportFormatterTests.cs`

**實作的條文：**

> | 系統（不屬於任何程式） | 以 commit 計時為 commit 已用 − 所有行程 commit 的總和；以 RAM 計時為 RAM 已用 − 所有行程 RAM 的總和 |

> | `AppGrouper` | 依 3.3 的演算法，把快照轉成應用程式清單與「系統（不屬於任何程式）」。每個應用程式與明細都有 commit 與 RAM 兩種合計，「系統」也有兩種計法（3.2）。清單依 commit 排序，供命令列、報告與尖峰紀錄使用；畫面自行改依 RAM 排序（3.6） | 第 8 章、F-11、F-23 |
> - 「系統（不屬於任何程式）」以 commit 計 = commit 已用 − 所有行程 `PagefileUsage` 的總和；以 RAM 計 = RAM 已用 − 所有行程 `WorkingSetPrivateSize` 的總和。兩者小於 0 時都以 0 計。

**介面：**

- 使用：`ProcessSample.PrivateWorkingSetBytes`（Task 1）
- 產生：
  - `AppComponent(string Name, int ProcessCount, long CommitBytes, long RamBytes)`
  - `AppGroup(string Name, bool IsWindows, int ProcessCount, long CommitBytes, long RamBytes, IReadOnlyList<AppComponent> Components)`
  - `GroupedSnapshot(SystemSnapshot Snapshot, IReadOnlyList<AppGroup> Apps, long UnattributedCommitBytes, long UnattributedRamBytes)`；`IReadOnlyList<AppGroup> AppsByRam`

- [ ] **Step 1：寫失敗的測試**

修改 `app/MemSweep.Core.Tests/AppGrouperTests.cs`：

（1）找到這一段：

```csharp
            int? createdSecond = null)
        {
            var created = Boot.AddSeconds(createdSecond ?? pid);
            _processes.Add(new ProcessSample(pid, parentPid, created, imageName, commitMb * Mb, commitMb * Mb / 2, commitMb * Mb / 4));
```

整段換成：

```csharp
            int? createdSecond = null,
            long? ramMb = null)
        {
            var created = Boot.AddSeconds(createdSecond ?? pid);
            // 工作集與 RAM 刻意給不同的值，才分得出程式用的是哪一個
            _processes.Add(new ProcessSample(pid, parentPid, created, imageName, commitMb * Mb, commitMb * Mb / 2, (ramMb ?? commitMb / 4) * Mb));
```

（2）找到這一段：

```csharp
        public GroupedSnapshot Group(long commitUsedMb = 100_000)
```

整段換成：

```csharp
        public GroupedSnapshot Group(long commitUsedMb = 100_000, long physicalAvailableMb = 16_384)
```

（3）找到這一段：

```csharp
                16_384 * Mb,
```

整段換成：

```csharp
                physicalAvailableMb * Mb,
```

（4）找到這一段：

```csharp
        Assert.AreEqual(1_500 * Mb, fixture.Group(commitUsedMb: 2_000).UnattributedBytes);
        Assert.AreEqual(0L, fixture.Group(commitUsedMb: 400).UnattributedBytes);
```

整段換成：

```csharp
        Assert.AreEqual(1_500 * Mb, fixture.Group(commitUsedMb: 2_000).UnattributedCommitBytes);
        Assert.AreEqual(0L, fixture.Group(commitUsedMb: 400).UnattributedCommitBytes);
    }

    [TestMethod]
    public void AppsAndComponentsAddUpCommitAndRam()
    {
        var grouped = new Fixture()
            .Add(1200, 9999, "claude.exe", 300, @"C:\Claude\claude.exe", "Anthropic", "Claude", ramMb: 120)
            .Add(1210, 1200, "claude.exe", 100, @"C:\Claude\claude.exe", "Anthropic", "Claude", ramMb: 70)
            .Add(1220, 1200, "node.exe", 50, @"C:\node\node.exe", "OpenJS", "Node.js", ramMb: 40)
            .Group();

        var claude = Row(grouped, "Claude");
        Assert.AreEqual(450 * Mb, claude.CommitBytes);
        Assert.AreEqual(230 * Mb, claude.RamBytes);
        CollectionAssert.AreEqual(
            new[] { new AppComponent("claude.exe", 2, 400 * Mb, 190 * Mb), new AppComponent("node.exe", 1, 50 * Mb, 40 * Mb) },
            claude.Components.ToArray());
    }

    [TestMethod]
    public void UnattributedRamIsRamInUseMinusAllProcessRam()
    {
        // 實體 RAM 32768 MB，兩個行程的 RAM 合計 250 MB
        var fixture = new Fixture()
            .Add(1300, 9999, "a.exe", 300, @"C:\A\a.exe", "A Corp", "A", ramMb: 150)
            .Add(1310, 9999, "b.exe", 200, @"C:\B\b.exe", "B Corp", "B", ramMb: 100);

        Assert.AreEqual(1_750 * Mb, fixture.Group(physicalAvailableMb: 32_768 - 2_000).UnattributedRamBytes);
        Assert.AreEqual(0L, fixture.Group(physicalAvailableMb: 32_768 - 200).UnattributedRamBytes);
    }

    [TestMethod]
    public void AppsByRamOrdersByRamWhileAppsKeepTheCommitOrder()
    {
        var grouped = new Fixture()
            .Add(1400, 9999, "game.exe", 300, @"C:\Game\game.exe", "Game Corp", "Game", ramMb: 50)
            .Add(1410, 9999, "edge.exe", 100, @"C:\Edge\edge.exe", "Edge Corp", "Edge", ramMb: 200)
            .Add(1420, 9999, "chat.exe", 80, @"C:\Chat\chat.exe", "Chat Corp", "Chat", ramMb: 200)
            .Group();

        CollectionAssert.AreEqual(new[] { "Game", "Edge", "Chat" }, grouped.Apps.Select(a => a.Name).ToArray());
        // RAM 相同時依名稱排序
        CollectionAssert.AreEqual(new[] { "Chat", "Edge", "Game" }, grouped.AppsByRam.Select(a => a.Name).ToArray());
```

修改 `app/MemSweep.Core.Tests/MemorySamplerLiveTests.cs`：

（1）找到這一段：

```csharp
        long processCommit = processes.Sum(p => p.PrivateCommitBytes);
```

整段換成：

```csharp
        long processCommit = processes.Sum(p => p.PrivateCommitBytes);
        long processRam = processes.Sum(p => p.PrivateWorkingSetBytes);
```

（2）找到這一段：

```csharp
        Assert.AreEqual(Math.Max(0, grouped.Snapshot.CommitUsedBytes - processCommit), grouped.UnattributedBytes);
```

整段換成：

```csharp
        Assert.AreEqual(Math.Max(0, grouped.Snapshot.CommitUsedBytes - processCommit), grouped.UnattributedCommitBytes);
        Assert.AreEqual(processRam, grouped.Apps.Sum(a => a.RamBytes));
        Assert.AreEqual(Math.Max(0, grouped.Snapshot.PhysicalUsedBytes - processRam), grouped.UnattributedRamBytes);
```

修改 `app/MemSweep.Core.Tests/ReportSamples.cs`：

（1）找到這一段：

```csharp
    private static AppGroup App(string name, int count, double commitGb, bool isWindows = false) =>
        new(name, isWindows, count, Gb(commitGb), Gb(commitGb) / 2, Array.Empty<AppComponent>());

    /// <summary>commit 31.4 / 39.4 GB、RAM 20.5 / 31.4 GB、尖峰 36.8 GB，六個應用程式。</summary>
```

整段換成：

```csharp
    private static AppGroup App(string name, int count, double commitGb, double ramGb, bool isWindows = false) =>
        new(name, isWindows, count, Gb(commitGb), Gb(ramGb), Array.Empty<AppComponent>());

    /// <summary>commit 31.4 / 39.4 GB、RAM 20.5 / 31.4 GB、尖峰 36.8 GB，六個應用程式；依 RAM 的順序和依 commit 的不同。</summary>
```

（2）找到這一段：

```csharp
            App("Claude", 36, 3.2),
            App("ChatGPT", 13, 2.2),
            App("Microsoft Edge WebView2", 19, 1.8),
            App(AppGrouper.WindowsGroupName, 140, 1.5, isWindows: true),
            App("Node.js JavaScript Runtime", 30, 1.4),
            App("Microsoft Edge", 16, 1.3),
        };
        return new GroupedSnapshot(snapshot, apps, Gb(6.9));
```

整段換成：

```csharp
            App("Claude", 36, 3.2, 1.6),
            App("ChatGPT", 13, 2.2, 0.9),
            App("Microsoft Edge WebView2", 19, 1.8, 1.2),
            App(AppGrouper.WindowsGroupName, 140, 1.5, 2.4, isWindows: true),
            App("Node.js JavaScript Runtime", 30, 1.4, 0.7),
            App("Microsoft Edge", 16, 1.3, 1.4),
        };
        return new GroupedSnapshot(snapshot, apps, Gb(6.9), Gb(12.3));
```

修改 `app/MemSweep.Core.Tests/DisplayModelTests.cs`：

找到這一段：

```csharp
        var empty = typical with { Apps = [], UnattributedBytes = 0 };
```

整段換成：

```csharp
        var empty = typical with { Apps = [], UnattributedCommitBytes = 0 };
```

修改 `app/MemSweep.Core.Tests/ReportFormatterTests.cs`：

找到這一段：

```csharp
        Assert.AreEqual(grouped.UnattributedBytes, root.GetProperty("unattributedBytes").GetInt64());
```

整段換成：

```csharp
        Assert.AreEqual(grouped.UnattributedCommitBytes, root.GetProperty("unattributedBytes").GetInt64());
```

- [ ] **Step 2：確認測試失敗**

執行：`(cd app && dotnet test)`

預期：建置失敗，錯誤包含 `'AppGroup' 未包含 'RamBytes' 的定義`、`'GroupedSnapshot' 未包含 'AppsByRam' 的定義`、`'GroupedSnapshot' 未包含 'UnattributedCommitBytes' 的定義`、`'GroupedSnapshot' 未包含 'UnattributedRamBytes' 的定義` 與 `'GroupedSnapshot' 未包含使用 4 個引數的建構函式`。

- [ ] **Step 3：實作**

`app/MemSweep.Core/GroupedSnapshot.cs` 整份換成：

```csharp
namespace MemSweep.Core;

/// <summary>應用程式明細的一列：同一應用程式內、同一執行檔（node 再依標籤區分）的行程合計。</summary>
public sealed record AppComponent(string Name, int ProcessCount, long CommitBytes, long RamBytes);

/// <summary>一個應用程式，即畫面與報告中的一列；RAM 是各行程私用工作集的合計。</summary>
public sealed record AppGroup(
    string Name,
    bool IsWindows,
    int ProcessCount,
    long CommitBytes,
    long RamBytes,
    IReadOnlyList<AppComponent> Components);

/// <summary>
/// 歸組後的快照。Apps 依 commit 由大到小，供命令列、報告與尖峰紀錄使用；畫面改用 AppsByRam（Design 3.1）。
/// 「系統（不屬於任何程式）」有以 commit 計與以 RAM 計兩種（Design 3.2）。
/// </summary>
public sealed record GroupedSnapshot(
    SystemSnapshot Snapshot,
    IReadOnlyList<AppGroup> Apps,
    long UnattributedCommitBytes,
    long UnattributedRamBytes)
{
    public const string UnattributedName = "系統（不屬於任何程式）";

    /// <summary>依 RAM 由大到小，相同時依名稱。</summary>
    public IReadOnlyList<AppGroup> AppsByRam =>
        Apps.OrderByDescending(a => a.RamBytes).ThenBy(a => a.Name, StringComparer.Ordinal).ToList();
}
```

修改 `app/MemSweep.Core/AppGrouper.cs`：

（1）找到這一段：

```csharp
        long processCommit = 0;
```

整段換成：

```csharp
        long processCommit = 0;
        long processRam = 0;
```

（2）找到這一段：

```csharp
            processCommit += process.PrivateCommitBytes;
```

整段換成：

```csharp
            processCommit += process.PrivateCommitBytes;
            processRam += process.PrivateWorkingSetBytes;
```

（3）找到這一段：

```csharp
        return new GroupedSnapshot(snapshot, apps, Math.Max(0, snapshot.CommitUsedBytes - processCommit));
```

整段換成：

```csharp
        return new GroupedSnapshot(
            snapshot,
            apps,
            Math.Max(0, snapshot.CommitUsedBytes - processCommit),
            Math.Max(0, snapshot.PhysicalUsedBytes - processRam));
```

（4）找到這一段：

```csharp
        private readonly Dictionary<string, (string Name, int Count, long Commit, long WorkingSet)> _components =
```

整段換成：

```csharp
        private readonly Dictionary<string, (string Name, int Count, long Commit, long Ram)> _components =
```

（5）找到這一段：

```csharp
        private long _workingSet;
```

整段換成：

```csharp
        private long _ram;
```

（6）找到這一段：

```csharp
            _workingSet += process.WorkingSetBytes;
```

整段換成：

```csharp
            _ram += process.PrivateWorkingSetBytes;
```

（7）找到這一段：

```csharp
                c.WorkingSet + process.WorkingSetBytes);
```

整段換成：

```csharp
                c.Ram + process.PrivateWorkingSetBytes);
```

（8）找到這一段：

```csharp
            _workingSet,
            _components.Values
                .Select(c => new AppComponent(c.Name, c.Count, c.Commit, c.WorkingSet))
```

整段換成：

```csharp
            _ram,
            _components.Values
                .Select(c => new AppComponent(c.Name, c.Count, c.Commit, c.Ram))
```

修改 `app/MemSweep.Core/AppListRow.cs`：

（1）找到這一段：

```csharp
        WorkingSetText = FullWindowModel.Gigabytes(app.WorkingSetBytes);
```

整段換成：

```csharp
        WorkingSetText = FullWindowModel.Gigabytes(app.RamBytes);
```

（2）找到這一段：

```csharp
                FullWindowModel.Gigabytes(c.WorkingSetBytes)))
```

整段換成：

```csharp
                FullWindowModel.Gigabytes(c.RamBytes)))
```

修改 `app/MemSweep.Core/FullWindowModel.cs`：

找到這一段：

```csharp
        Place(index++, UnattributedKey, true).UpdateUnattributed(grouped.UnattributedBytes);
```

整段換成：

```csharp
        Place(index++, UnattributedKey, true).UpdateUnattributed(grouped.UnattributedCommitBytes);
```

修改 `app/MemSweep.Core/PanelModel.cs`：

（1）找到這一段：

```csharp
        long largest = Math.Max(grouped.UnattributedBytes, top.Count == 0 ? 0 : top.Max(a => a.CommitBytes));
```

整段換成：

```csharp
        long largest = Math.Max(grouped.UnattributedCommitBytes, top.Count == 0 ? 0 : top.Max(a => a.CommitBytes));
```

（2）找到這一段：

```csharp
                MemoryMath.FormatGigabytes(grouped.UnattributedBytes) + " GB",
                Fraction(grouped.UnattributedBytes, largest),
```

整段換成：

```csharp
                MemoryMath.FormatGigabytes(grouped.UnattributedCommitBytes) + " GB",
                Fraction(grouped.UnattributedCommitBytes, largest),
```

修改 `app/MemSweep.Core/ReportFormatter.cs`：

（1）找到這一段：

```csharp
            json.WriteNumber("unattributedBytes", grouped.UnattributedBytes);
```

整段換成：

```csharp
            json.WriteNumber("unattributedBytes", grouped.UnattributedCommitBytes);
```

（2）找到這一段：

```csharp
        text.Append(GroupedSnapshot.UnattributedName).Append("  ").Append(Gb(grouped.UnattributedBytes)).Append(" GB\n");
```

整段換成：

```csharp
        text.Append(GroupedSnapshot.UnattributedName).Append("  ").Append(Gb(grouped.UnattributedCommitBytes)).Append(" GB\n");
```

- [ ] **Step 4：確認測試通過**

執行：`(cd app && dotnet test)`

預期：總計 338，失敗 0。

- [ ] **Step 5：commit 並推送**

```bash
git add app/MemSweep.Core app/MemSweep.Core.Tests
git commit -F - <<'EOF'
歸組加上 RAM 合計與以 RAM 計的「系統」列

AppGroup 與 AppComponent 改為 commit 與 RAM（私用工作集）兩種合計，
GroupedSnapshot 新增以 RAM 計的「系統（不屬於任何程式）」與依 RAM 排序的
AppsByRam；Apps 維持依 commit，命令列、報告與尖峰紀錄不變（Design 3.1、3.2）。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
git fetch origin
git ls-remote --heads origin phase-05-ram-first
gh pr list --head phase-05-ram-first --state all
git push
```

---

### Task 3：系統匣的數字、顏色與提示改看 RAM

**檔案：**

- 修改：`app/MemSweep.Core/TrayText.cs`（整份換成）、`app/MemSweep.Desktop/TrayController.cs`、`app/MemSweep.Desktop/SettingsWindow.xaml`
- 測試：`app/MemSweep.Core.Tests/DisplayModelTests.cs`、`SystemReaderLiveTests.cs`、`SettingsWindowTests.cs`

**實作的條文：**

> - F-02 圖示以整數顯示目前的 RAM 使用率，例如「52」〔使用者決定〕。
> - F-03 數字顏色依 RAM 使用率與門檻變化：低於橘色門檻為綠色；達到橘色門檻但低於紅色門檻為橘色；達到紅色門檻以上為紅色。門檻可在設定調整，預設橘色 70%、紅色 85%。〔預設值：Claude 決定；改看 RAM：使用者決定〕commit 不另外警示（1.4）。
> - F-05 滑鼠停在圖示上時顯示提示：實體 RAM 已用 / 總量（GB 與百分比）、commit 已用 / 上限（GB 與百分比），RAM 在前。〔順序與 RAM 百分比：Claude 決定〕
> - A-10 RAM 使用率和同一時間工作管理員「處理程序」頁「記憶體」欄標題上的百分比，相差不超過 1 個百分點。〔Claude 決定〕

> - 以 `System.Drawing` 依目前 DPI 的小圖示尺寸繪製數字圖示（F-02、F-03）。使用率 100% 時以較小字型畫出「100」。Phase 05 起數字與顏色都依 RAM 使用率（SPEC 1.4），門檻設定與繪製方式不變。
> - 提示文字（F-05）範例：`MemSweep` / `RAM 16.5 / 31.4 GB（52%）` / `Commit 27.4 / 63.4 GB（43%）`。
> - RAM 已用 = `K32GetPerformanceInfo` 的實體 RAM 總量 − 可用量（換算成 bytes），RAM 使用率依此計算；預期與工作管理員「處理程序」頁「記憶體」欄標題的百分比一致，Phase 05 以 A-10 驗證。

設定視窗門檻那一區的標題（Design 3.6）：

> - 設定視窗（F-50）：橘色門檻、紅色門檻、開機自動啟動、保留天數。驗證規則：1 ≤ 橘色 < 紅色 ≤ 100；保留天數 1～3650。Phase 05 起門檻那一區的標題由「commit 使用率的顏色」改為「RAM 使用率的顏色」，`settings.json` 的欄位與驗證規則不變。

**介面：**

- 使用：`SystemSnapshot.PhysicalUsedBytes`、`PhysicalTotalBytes`（既有）
- 產生：`TrayText.RamPercent(SystemSnapshot)` → `int`；`TrayText.IconLevel(SystemSnapshot, UsageThresholds)` → `UsageLevel`；`TrayText.CommitPercent` 不變

- [ ] **Step 1：寫失敗的測試**

修改 `app/MemSweep.Core.Tests/DisplayModelTests.cs`：

（1）找到這一段：

```csharp
    public void TrayIconTextIsTheCommitPercent()
    {
        var snapshot = ReportSamples.Typical().Snapshot;

        Assert.AreEqual(80, TrayText.CommitPercent(snapshot));
        Assert.AreEqual("80", TrayText.IconText(snapshot));
```

整段換成：

```csharp
    public void TrayIconTextIsTheRamPercent()
    {
        // RAM 20.5 / 31.4 GB 是 65%，commit 31.4 / 39.4 GB 是 80%
        var snapshot = ReportSamples.Typical().Snapshot;

        Assert.AreEqual(65, TrayText.RamPercent(snapshot));
        Assert.AreEqual(80, TrayText.CommitPercent(snapshot));
        Assert.AreEqual("65", TrayText.IconText(snapshot));
```

（2）找到這一段：

```csharp
        var full = snapshot with { CommitUsedBytes = snapshot.CommitLimitBytes };
        var over = snapshot with { CommitUsedBytes = snapshot.CommitLimitBytes + MemoryMath.BytesPerGigabyte };
```

整段換成：

```csharp
        var full = snapshot with { PhysicalAvailableBytes = 0 };
        var over = snapshot with { PhysicalAvailableBytes = -MemoryMath.BytesPerGigabyte };
```

（3）找到這一段：

```csharp
    public void TooltipShowsCommitAndRam()
```

整段換成：

```csharp
    public void TrayIconColorFollowsTheRamPercent()
    {
        // RAM 65%、commit 80%：依 RAM 是綠色與橘色，依 commit 會是橘色與紅色
        var snapshot = ReportSamples.Typical().Snapshot;

        Assert.AreEqual(UsageLevel.Green, TrayText.IconLevel(snapshot, UsageThresholds.Default));
        Assert.AreEqual(UsageLevel.Orange, TrayText.IconLevel(snapshot, new UsageThresholds(60, 75)));
    }

    [TestMethod]
    public void TooltipShowsRamThenCommit()
```

（4）找到這一段：

```csharp
        Assert.AreEqual("MemSweep\nCommit 31.4 / 39.4 GB（80%）\nRAM 20.5 / 31.4 GB", tooltip);
```

整段換成：

```csharp
        Assert.AreEqual("MemSweep\nRAM 20.5 / 31.4 GB（65%）\nCommit 31.4 / 39.4 GB（80%）", tooltip);
```

修改 `app/MemSweep.Core.Tests/SystemReaderLiveTests.cs`：

找到這一段：

```csharp
        Assert.AreEqual((double)usedByStatus, snapshot.CommitUsedBytes, MemoryMath.BytesPerGigabyte);
    }
```

整段換成：

```csharp
        Assert.AreEqual((double)usedByStatus, snapshot.CommitUsedBytes, MemoryMath.BytesPerGigabyte);
    }

    [TestMethod]
    public void RamPercentMatchesTheMemoryLoad()
    {
        var snapshot = new SystemReader().Read();
        var status = TestNative.GetMemoryStatus();

        // A-10 的自動化替代：dwMemoryLoad 是 Windows 算的 RAM 使用率（無條件捨去），MemSweep 四捨五入，最多相差 1
        Assert.AreEqual((double)status.MemoryLoad, TrayText.RamPercent(snapshot), 1.0);
    }
```

修改 `app/MemSweep.Core.Tests/SettingsWindowTests.cs`：

找到這一段：

```csharp
            new[] { "MemSweep 設定", "橘色門檻", "紅色門檻", "保留天數", "開機時自動啟動 MemSweep", "儲存", "取消" },
```

整段換成：

```csharp
            new[] { "MemSweep 設定", "RAM 使用率的顏色", "橘色門檻", "紅色門檻", "保留天數", "開機時自動啟動 MemSweep", "儲存", "取消" },
```

- [ ] **Step 2：確認測試失敗**

執行：`(cd app && dotnet test)`

預期：建置失敗，錯誤為 `CS0117: 'TrayText' 未包含 'RamPercent' 的定義` 與 `CS0117: 'TrayText' 未包含 'IconLevel' 的定義`。

- [ ] **Step 3：實作**

`app/MemSweep.Core/TrayText.cs` 整份換成：

```csharp
using System.Globalization;

namespace MemSweep.Core;

/// <summary>系統匣圖示上的數字與顏色、滑鼠停留時的提示文字（F-02、F-03、F-05）。圖示看 RAM（SPEC 1.4）。</summary>
public static class TrayText
{
    /// <summary>RAM 使用率，限制在 0～100。</summary>
    public static int RamPercent(SystemSnapshot snapshot) =>
        Math.Clamp(MemoryMath.Percent(snapshot.PhysicalUsedBytes, snapshot.PhysicalTotalBytes), 0, 100);

    /// <summary>commit 使用率，限制在 0～100。</summary>
    public static int CommitPercent(SystemSnapshot snapshot) =>
        Math.Clamp(MemoryMath.Percent(snapshot.CommitUsedBytes, snapshot.CommitLimitBytes), 0, 100);

    public static string IconText(SystemSnapshot snapshot) =>
        RamPercent(snapshot).ToString(CultureInfo.InvariantCulture);

    public static UsageLevel IconLevel(SystemSnapshot snapshot, UsageThresholds thresholds) =>
        thresholds.LevelOf(RamPercent(snapshot));

    public static string Tooltip(SystemSnapshot snapshot) =>
        "MemSweep\n" +
        "RAM " + MemoryMath.FormatGigabytes(snapshot.PhysicalUsedBytes) + " / " +
        MemoryMath.FormatGigabytes(snapshot.PhysicalTotalBytes) + " GB（" + RamPercent(snapshot) + "%）\n" +
        "Commit " + MemoryMath.FormatGigabytes(snapshot.CommitUsedBytes) + " / " +
        MemoryMath.FormatGigabytes(snapshot.CommitLimitBytes) + " GB（" + CommitPercent(snapshot) + "%）";
}
```

修改 `app/MemSweep.Desktop/TrayController.cs`：

找到這一段：

```csharp
        var level = _settings.Current.Thresholds.LevelOf(TrayText.CommitPercent(snapshot));
```

整段換成：

```csharp
        var level = TrayText.IconLevel(snapshot, _settings.Current.Thresholds);
```

修改 `app/MemSweep.Desktop/SettingsWindow.xaml`：

找到這一段：

```xml
        <TextBlock Text="commit 使用率的顏色" FontWeight="SemiBold" />
```

整段換成：

```xml
        <TextBlock Text="RAM 使用率的顏色" FontWeight="SemiBold" />
```

- [ ] **Step 4：確認測試通過**

執行：`(cd app && dotnet test)`

預期：總計 340，失敗 0。

- [ ] **Step 5：commit 並推送**

```bash
git add app/MemSweep.Core/TrayText.cs app/MemSweep.Desktop/TrayController.cs app/MemSweep.Desktop/SettingsWindow.xaml app/MemSweep.Core.Tests
git commit -F - <<'EOF'
系統匣的數字、顏色與提示改看 RAM

圖示的數字與顏色改依 RAM 使用率（F-02、F-03），提示文字改為 RAM 在前並補上
百分比（F-05）；圖示顏色抽成 TrayText.IconLevel。設定視窗門檻那一區的標題改為
「RAM 使用率的顏色」。即時測試以 dwMemoryLoad 作為 A-10 的自動化替代。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
git fetch origin
git ls-remote --heads origin phase-05-ram-first
gh pr list --head phase-05-ram-first --state all
git push
```

---

### Task 4：小面板與「總覽」改以 RAM 為主

**檔案：**

- 修改：`app/MemSweep.Core/PanelModel.cs`、`app/MemSweep.Core/FullWindowModel.cs`、`app/MemSweep.Desktop/SummaryView.xaml`
- 測試：`app/MemSweep.Core.Tests/DisplayModelTests.cs`、`FullWindowModelTests.cs`、`FlyoutTests.cs`

**實作的條文：**

> - F-11 內容：
>   - 實體 RAM 量表：已用 / 總量（GB）與使用率，顏色規則同 F-03。
>   - commit 量表：已用 / 上限（GB）與使用率，不依門檻變色。〔Claude 決定〕
>   - 開機以來 commit 尖峰：數值與發生時間，規則見 F-31。
>   - RAM 前五名的應用程式：名稱、行程數、RAM（GB）、相對長條〔使用者決定〕。
>   - 「系統（不屬於任何程式）」一列（以 RAM 計），固定顯示在前五名之後。
> - F-22 總覽：F-11 的全部資訊，另加目前的分頁檔大小。〔Claude 決定〕

> - 小面板與「總覽」的內容（F-11、F-22，Phase 05 起）：RAM 量表在前，依門檻變色；commit 量表在後，固定使用目前 RAM 量表的中性色。前五名依 RAM 由大到小；「系統（不屬於任何程式）」列以 RAM 計，不參與排序，固定在前五名之後（F-11）。數值與相對長條都是 RAM。尖峰那一行維持「開機以來 commit 尖峰」。

**介面：**

- 使用：`GroupedSnapshot.AppsByRam`、`UnattributedRamBytes`、`AppGroup.RamBytes`（Task 2）；`TrayText.RamPercent`、`CommitPercent`（Task 3）
- 產生：`PanelModel(GaugeModel Ram, GaugeModel Commit, string PeakText, IReadOnlyList<PanelRow> Rows, string? Warning)`；`Ram.Level` 依門檻，`Commit.Level` 一律為 `null`

- [ ] **Step 1：寫失敗的測試**

修改 `app/MemSweep.Core.Tests/DisplayModelTests.cs`：

（1）找到這一段：

```csharp
    public void PanelGauges()
    {
        var panel = PanelModel.Build(ReportSamples.Typical(), null, UsageThresholds.Default);
```

整段換成：

```csharp
    public void PanelGauges()
    {
        // RAM 65% 在預設門檻下是綠色；commit 80% 不依門檻變色
        var panel = PanelModel.Build(ReportSamples.Typical(), null, UsageThresholds.Default);

        Assert.AreEqual("實體 RAM", panel.Ram.Label);
        Assert.AreEqual("20.5 / 31.4 GB（65%）", panel.Ram.ValueText);
        Assert.AreEqual(20.5 / 31.4, panel.Ram.Fraction, 0.001);
        Assert.AreEqual(UsageLevel.Green, panel.Ram.Level);
```

（2）找到這一段：

```csharp
        Assert.AreEqual(UsageLevel.Orange, panel.Commit.Level);
        Assert.AreEqual("實體 RAM", panel.Ram.Label);
        Assert.AreEqual("20.5 / 31.4 GB（65%）", panel.Ram.ValueText);
        Assert.AreEqual(20.5 / 31.4, panel.Ram.Fraction, 0.001);
        Assert.IsNull(panel.Ram.Level);
```

整段換成：

```csharp
        Assert.IsNull(panel.Commit.Level);
```

（3）找到這一段：

```csharp
    public void PanelRowsAreTopFivePlusUnattributedWithBarsRelativeToTheLargest()
```

整段換成：

```csharp
    public void PanelRowsAreTheTopFiveByRamPlusUnattributedWithBarsRelativeToTheLargest()
```

（4）找到這一段：

```csharp
        Assert.HasCount(6, panel.Rows);
        Assert.AreEqual(new PanelRow("Claude", "36", "3.2 GB", 3.2 / 6.9, false), panel.Rows[0] with { BarFraction = 3.2 / 6.9 });
        Assert.AreEqual(3.2 / 6.9, panel.Rows[0].BarFraction, 0.001);
        Assert.AreEqual("Node.js JavaScript Runtime", panel.Rows[4].Name);
        Assert.AreEqual("30", panel.Rows[4].CountText);
        var last = panel.Rows[5];
        Assert.AreEqual(GroupedSnapshot.UnattributedName, last.Name);
        Assert.AreEqual(string.Empty, last.CountText);
        Assert.AreEqual("6.9 GB", last.GigabytesText);
```

整段換成：

```csharp
        // 依 RAM：Windows 2.4、Claude 1.6、Edge 1.4、WebView2 1.2、ChatGPT 0.9；Node.js 0.7 排第六，不顯示
        CollectionAssert.AreEqual(
            new[] { AppGrouper.WindowsGroupName, "Claude", "Microsoft Edge", "Microsoft Edge WebView2", "ChatGPT", GroupedSnapshot.UnattributedName },
            panel.Rows.Select(r => r.Name).ToArray());
        Assert.AreEqual(new PanelRow("Claude", "36", "1.6 GB", 1.6 / 12.3, false), panel.Rows[1] with { BarFraction = 1.6 / 12.3 });
        Assert.AreEqual(1.6 / 12.3, panel.Rows[1].BarFraction, 0.001);
        var last = panel.Rows[5];
        Assert.AreEqual(string.Empty, last.CountText);
        Assert.AreEqual("12.3 GB", last.GigabytesText);
```

（5）找到這一段：

```csharp
        var empty = typical with { Apps = [], UnattributedCommitBytes = 0 };
```

整段換成：

```csharp
        var empty = typical with { Apps = [], UnattributedRamBytes = 0 };
```

修改 `app/MemSweep.Core.Tests/FullWindowModelTests.cs`：

（1）找到這一段：

```csharp
        Assert.AreEqual(UsageLevel.Orange, model.Overview!.Commit.Level);
```

整段換成：

```csharp
        Assert.AreEqual(UsageLevel.Green, model.Overview!.Ram.Level);
```

（2）找到這一段：

```csharp
        Assert.AreEqual(UsageLevel.Red, model.Overview!.Commit.Level);
```

整段換成：

```csharp
        Assert.AreEqual(UsageLevel.Orange, model.Overview!.Ram.Level);
```

修改 `app/MemSweep.Core.Tests/FlyoutTests.cs`：

（1）找到這一段：

```csharp
using System.Windows.Controls;
```

整段換成：

```csharp
using System.Windows.Controls;
using System.Windows.Data;
```

（2）找到這一段：

```csharp
                "Commit",
                "31.4 / 39.4 GB（80%）",
                "實體 RAM",
                "20.5 / 31.4 GB（65%）",
```

整段換成：

```csharp
                "實體 RAM",
                "20.5 / 31.4 GB（65%）",
                "Commit",
                "31.4 / 39.4 GB（80%）",
```

（3）找到這一段：

```csharp
                "3.2 GB",
                "系統（不屬於任何程式）",
                "6.9 GB",
```

整段換成：

```csharp
                "1.6 GB",
                "系統（不屬於任何程式）",
                "12.3 GB",
```

（4）找到這一段：

```csharp
        Assert.HasCount(6, rows);
    }
```

整段換成：

```csharp
        Assert.HasCount(6, rows);
    }

    [STATestMethod]
    public void TheRamGaugeComesFirstAndIsTheOneColoredByTheThresholds()
    {
        var panel = LaidOutPanel(PanelModel.Build(ReportSamples.Typical(), null, UsageThresholds.Default));

        var texts = WpfLayout.Texts(panel.Summary);
        Assert.IsLessThan(texts.IndexOf("Commit"), texts.IndexOf("實體 RAM"));
        // 只有一個量表依門檻變色，而且看的是 RAM
        var colored = WpfLayout.Descendants(panel.Summary).OfType<Border>().Single(b => b.Style is { Triggers.Count: > 0 });
        var paths = colored.Style.Triggers.OfType<DataTrigger>().Select(t => ((Binding)t.Binding).Path.Path).Distinct().ToArray();
        CollectionAssert.AreEqual(new[] { "Ram.Level" }, paths);
    }
```

- [ ] **Step 2：確認測試失敗**

執行：`(cd app && dotnet test)`

預期：總計 341，失敗 6：`PanelGauges`、`PanelRowsAreTheTopFiveByRamPlusUnattributedWithBarsRelativeToTheLargest`、`PanelRowsWithNothingToCompareHaveEmptyBars`、`NewThresholdsApplyAtTheNextUpdate`、`PanelShowsGaugesPeakRowsAndButtons`、`TheRamGaugeComesFirstAndIsTheOneColoredByTheThresholds`。

- [ ] **Step 3：實作**

修改 `app/MemSweep.Core/PanelModel.cs`：

（1）找到這一段：

```csharp
/// <summary>量表：Level 為 null 表示不依門檻變色（實體 RAM）。</summary>
```

整段換成：

```csharp
/// <summary>量表：Level 為 null 表示不依門檻變色（commit）。</summary>
```

（2）找到這一段：

```csharp
/// <summary>小面板要顯示的全部內容（F-11）；Warning 是尖峰紀錄停用的原因，沒有問題時為 null（Design 3.9）。</summary>
public sealed record PanelModel(GaugeModel Commit, GaugeModel Ram, string PeakText, IReadOnlyList<PanelRow> Rows, string? Warning)
```

整段換成：

```csharp
/// <summary>
/// 小面板要顯示的全部內容（F-11）：RAM 量表依門檻變色，commit 量表不變色；前五名與「系統」列都以 RAM 計。
/// Warning 是尖峰紀錄停用的原因，沒有問題時為 null（Design 3.9）。
/// </summary>
public sealed record PanelModel(GaugeModel Ram, GaugeModel Commit, string PeakText, IReadOnlyList<PanelRow> Rows, string? Warning)
```

（3）找到這一段：

```csharp
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
        long largest = Math.Max(grouped.UnattributedCommitBytes, top.Count == 0 ? 0 : top.Max(a => a.CommitBytes));
```

整段換成：

```csharp
        int ramPercent = TrayText.RamPercent(s);
        var ram = new GaugeModel(
            "實體 RAM",
            GaugeText(s.PhysicalUsedBytes, s.PhysicalTotalBytes, ramPercent),
            Fraction(s.PhysicalUsedBytes, s.PhysicalTotalBytes),
            thresholds.LevelOf(ramPercent));
        var commit = new GaugeModel(
            "Commit",
            GaugeText(s.CommitUsedBytes, s.CommitLimitBytes, TrayText.CommitPercent(s)),
            Fraction(s.CommitUsedBytes, s.CommitLimitBytes),
            null);

        var top = grouped.AppsByRam.Take(ReportFormatter.TopCount).ToList();
        long largest = Math.Max(grouped.UnattributedRamBytes, top.Count == 0 ? 0 : top.Max(a => a.RamBytes));
```

（4）找到這一段：

```csharp
                MemoryMath.FormatGigabytes(a.CommitBytes) + " GB",
                Fraction(a.CommitBytes, largest),
```

整段換成：

```csharp
                MemoryMath.FormatGigabytes(a.RamBytes) + " GB",
                Fraction(a.RamBytes, largest),
```

（5）找到這一段：

```csharp
                MemoryMath.FormatGigabytes(grouped.UnattributedCommitBytes) + " GB",
                Fraction(grouped.UnattributedCommitBytes, largest),
```

整段換成：

```csharp
                MemoryMath.FormatGigabytes(grouped.UnattributedRamBytes) + " GB",
                Fraction(grouped.UnattributedRamBytes, largest),
```

（6）找到這一段：

```csharp
        return new PanelModel(commit, ram, DescribePeak(peak, s.Timestamp), rows, warning);
```

整段換成：

```csharp
        return new PanelModel(ram, commit, DescribePeak(peak, s.Timestamp), rows, warning);
```

修改 `app/MemSweep.Core/FullWindowModel.cs`：

找到這一段：

```csharp
    /// <summary>commit 量表的顏色門檻；設定變更後從下一次 Update 開始套用。</summary>
```

整段換成：

```csharp
    /// <summary>RAM 量表的顏色門檻；設定變更後從下一次 Update 開始套用。</summary>
```

修改 `app/MemSweep.Desktop/SummaryView.xaml`：

找到這一段：

```xml
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
```

整段換成：

```xml
    <!-- 實體 RAM 量表：顏色依門檻（F-03、F-11） -->
    <DockPanel>
      <TextBlock DockPanel.Dock="Right" Text="{Binding Ram.ValueText, Mode=OneTime}" />
      <TextBlock Text="{Binding Ram.Label, Mode=OneTime}" FontWeight="SemiBold" />
    </DockPanel>
    <Grid Height="8" Margin="0,4,0,10">
      <Border Background="{DynamicResource TrackBrush}" CornerRadius="4" />
      <Border>
        <Border.Style>
          <Style TargetType="Border" BasedOn="{StaticResource FillStyle}">
            <Setter Property="Background" Value="{DynamicResource GreenBrush}" />
            <Style.Triggers>
              <DataTrigger Binding="{Binding Ram.Level, Mode=OneTime}" Value="Orange">
                <Setter Property="Background" Value="{DynamicResource OrangeBrush}" />
              </DataTrigger>
              <DataTrigger Binding="{Binding Ram.Level, Mode=OneTime}" Value="Red">
                <Setter Property="Background" Value="{DynamicResource RedBrush}" />
              </DataTrigger>
            </Style.Triggers>
          </Style>
        </Border.Style>
        <Border.RenderTransform>
          <ScaleTransform ScaleX="{Binding Ram.Fraction, Mode=OneTime}" />
        </Border.RenderTransform>
      </Border>
    </Grid>

    <!-- commit 量表：固定用強調色，不依門檻變色（F-11） -->
    <DockPanel>
      <TextBlock DockPanel.Dock="Right" Text="{Binding Commit.ValueText, Mode=OneTime}" />
      <TextBlock Text="{Binding Commit.Label, Mode=OneTime}" FontWeight="SemiBold" />
    </DockPanel>
    <Grid Height="8" Margin="0,4,0,10">
      <Border Background="{DynamicResource TrackBrush}" CornerRadius="4" />
      <Border Style="{StaticResource FillStyle}" Background="{DynamicResource AccentBrush}">
        <Border.RenderTransform>
          <ScaleTransform ScaleX="{Binding Commit.Fraction, Mode=OneTime}" />
```

- [ ] **Step 4：確認測試通過**

執行：`(cd app && dotnet test)`

預期：總計 341，失敗 0。

- [ ] **Step 5：commit 並推送**

```bash
git add app/MemSweep.Core/PanelModel.cs app/MemSweep.Core/FullWindowModel.cs app/MemSweep.Desktop/SummaryView.xaml app/MemSweep.Core.Tests
git commit -F - <<'EOF'
小面板與總覽改以 RAM 為主

RAM 量表移到前面並依門檻變色，commit 量表改為固定的強調色；前五名與
「系統（不屬於任何程式）」列改以 RAM 計（F-11、F-22、Design 3.6）。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
git fetch origin
git ls-remote --heads origin phase-05-ram-first
gh pr list --head phase-05-ram-first --state all
git push
```

---

### Task 5：「程式」分頁依 RAM 排序與使用說明

**檔案：**

- 修改：`app/MemSweep.Core/AppListRow.cs`、`app/MemSweep.Core/FullWindowModel.cs`、`app/MemSweep.Desktop/AppsView.xaml`、`README.md`
- 測試：`app/MemSweep.Core.Tests/FullWindowModelTests.cs`、`FullWindowTests.cs`

**實作的條文：**

> - F-23 程式：
>   - 所有應用程式依 RAM 由大到小排列，欄位為名稱、行程數、RAM、commit〔使用者決定〕。
>   - 每列可展開，列出組成它的行程，依執行檔名稱合併（例如「claude.exe ×14」）。node 行程再依啟動的套件區分（例如「node：playwright」）。展開的明細同樣顯示 RAM 與 commit。〔Claude 決定〕
>   - 最後一列為「系統（不屬於任何程式）」，同時顯示以 RAM 計與以 commit 計的數值。

> - 完整視窗（F-20～F-24）：一般視窗，三個分頁。第一次開啟時才建立，只在顯示時更新。關閉時真正關閉，並在 UI 空閒時以 `GCCollectionMode.Aggressive` 回收 MemSweep 自己的記憶體，下次開啟再建立：隱藏的視窗會保留整個畫面樹，Phase 03 原型實測隱藏後仍約 109 MB，關閉並回收後穩定在約 86 MB。「程式」分頁用 `ItemsControl`，每 2 秒原地更新各列而不整份重建；展開狀態保存在顯示模型，關閉再開仍在。Phase 05 起「程式」分頁依 RAM 由大到小排序，欄位依序為名稱、行程數、RAM、commit；展開的明細也依 RAM 排序並顯示 RAM 與 commit；「系統」列兩欄分別是以 RAM 計與以 commit 計的數值。RAM 比 commit 變動頻繁，數值相近的兩列可能隨更新互換位置，這是預期行為。

**介面：**

- 使用：`GroupedSnapshot.AppsByRam`、`UnattributedRamBytes`、`UnattributedCommitBytes`、`AppComponent.RamBytes`（Task 2）
- 產生：`ComponentRow(string Name, string RamText, string CommitText)`；`AppListRow.RamText`（取代 `WorkingSetText`）；`AppListRow.UpdateUnattributed(long ramBytes, long commitBytes)`

- [ ] **Step 1：寫失敗的測試**

修改 `app/MemSweep.Core.Tests/FullWindowModelTests.cs`：

（1）找到這一段：

```csharp
    public void AppsListsEveryAppByCommitThenUnattributed()
```

整段換成：

```csharp
    public void AppsListsEveryAppByRamThenUnattributed()
```

（2）找到這一段：

```csharp
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
```

整段換成：

```csharp
        CollectionAssert.AreEqual(
            new[]
            {
                AppGrouper.WindowsGroupName, "Claude", "Microsoft Edge", "Microsoft Edge WebView2", "ChatGPT",
                "Node.js JavaScript Runtime", GroupedSnapshot.UnattributedName,
            },
            model.Apps.Select(a => a.Name).ToArray());
        var claude = model.Apps[1];
        Assert.AreEqual("36", claude.CountText);
        Assert.AreEqual("1.6 GB", claude.RamText);
        Assert.AreEqual("3.2 GB", claude.CommitText);
        Assert.IsFalse(claude.IsUnattributed);
        var last = model.Apps[6];
        Assert.IsTrue(last.IsUnattributed);
        Assert.AreEqual(string.Empty, last.CountText);
        Assert.AreEqual("12.3 GB", last.RamText);
        Assert.AreEqual("6.9 GB", last.CommitText);
```

（3）找到這一段：

```csharp
    public void ComponentsShowTheProcessCountAfterTheName()
    {
```

整段換成：

```csharp
    public void ComponentsAreSortedByRamAndShowTheProcessCountAfterTheName()
    {
        // cmd.exe 與 claude.exe 的 RAM 相同，依名稱排序
```

（4）找到這一段：

```csharp
            new AppComponent("claude.exe", 14, Gb(2.0), Gb(1.0)),
            new AppComponent("node：@playwright/mcp", 2, Gb(0.4), Gb(0.3)));
```

整段換成：

```csharp
            new AppComponent("cmd.exe", 1, Gb(0.1), Gb(1.0)),
            new AppComponent("claude.exe", 14, Gb(2.0), Gb(1.0)),
            new AppComponent("node：@playwright/mcp", 2, Gb(0.4), Gb(1.2)));
```

（5）找到這一段：

```csharp
                new ComponentRow("claude.exe ×14", "2.0 GB", "1.0 GB"),
                new ComponentRow("node：@playwright/mcp ×2", "0.4 GB", "0.3 GB"),
            },
            model.Apps[0].Components.ToArray());
        Assert.AreEqual("16", model.Apps[0].CountText);
```

整段換成：

```csharp
                new ComponentRow("node：@playwright/mcp ×2", "1.2 GB", "0.4 GB"),
                new ComponentRow("claude.exe ×14", "1.0 GB", "2.0 GB"),
                new ComponentRow("cmd.exe ×1", "1.0 GB", "0.1 GB"),
            },
            model.Apps[0].Components.ToArray());
        Assert.AreEqual("17", model.Apps[0].CountText);
```

（6）找到這一段：

```csharp
        CollectionAssert.AreEquivalent(new[] { "CommitText", "WorkingSetText" }, changed);
```

整段換成：

```csharp
        CollectionAssert.AreEquivalent(new[] { "RamText", "CommitText" }, changed);
```

修改 `app/MemSweep.Core.Tests/FullWindowTests.cs`：

（1）找到這一段：

```csharp
        model.Update(typical with { Apps = [claude, .. typical.Apps.Skip(1)] }, NoPeaks);
        return model;
    }
```

整段換成：

```csharp
        model.Update(typical with { Apps = [claude, .. typical.Apps.Skip(1)] }, NoPeaks);
        return model;
    }

    // 文字所在的欄；畫面上的左右順序看欄，不看元素的先後
    private static int ColumnOf(DependencyObject root, string text) =>
        Grid.GetColumn(WpfLayout.Descendants(root).OfType<TextBlock>().Single(t => t.Text == text));
```

（2）找到這一段：

```csharp
            new[] { "名稱", "行程數", "Commit", "實際 RAM", "Claude", "36", "3.2 GB", "1.6 GB", "Microsoft Edge", GroupedSnapshot.UnattributedName, "6.9 GB" },
            texts);
        CollectionAssert.DoesNotContain(texts, "claude.exe ×14");
```

整段換成：

```csharp
            new[] { "名稱", "行程數", "RAM", "Commit", "Claude", "36", "1.6 GB", "3.2 GB", "Microsoft Edge", GroupedSnapshot.UnattributedName, "12.3 GB", "6.9 GB" },
            texts);
        CollectionAssert.DoesNotContain(texts, "claude.exe ×14");
        // RAM 欄在 commit 欄之前：標題與 Claude 那一列都是
        Assert.IsLessThan(ColumnOf(view, "Commit"), ColumnOf(view, "RAM"));
        Assert.IsLessThan(ColumnOf(view, "3.2 GB"), ColumnOf(view, "1.6 GB"));
```

（3）找到這一段：

```csharp

        WpfLayout.Descendants(view).OfType<ToggleButton>().First().IsChecked = true;
        view.UpdateLayout();

        Assert.IsTrue(model.Apps[0].IsExpanded);
        CollectionAssert.IsSubsetOf(new[] { "claude.exe ×14", "2.0 GB", "1.0 GB", "node：@playwright/mcp ×2" }, WpfLayout.Texts(view));
```

整段換成：

```csharp
        var claude = model.Apps.Single(a => a.Name == "Claude");

        WpfLayout.Descendants(view).OfType<ToggleButton>().Single(t => t.DataContext == claude).IsChecked = true;
        view.UpdateLayout();

        Assert.IsTrue(claude.IsExpanded);
        var texts = WpfLayout.Texts(view);
        CollectionAssert.IsSubsetOf(new[] { "claude.exe ×14", "1.0 GB", "2.0 GB", "node：@playwright/mcp ×2" }, texts);
        Assert.IsLessThan(ColumnOf(view, "2.0 GB"), ColumnOf(view, "1.0 GB"));
```

（4）找到這一段：

```csharp
        Assert.HasCount(7, toggles);
        Assert.AreEqual(Visibility.Visible, toggles[0].Visibility);
```

整段換成：

```csharp
        // 依 RAM 排序後 Claude 是第二列，只有它有明細
        Assert.HasCount(7, toggles);
        Assert.AreEqual(Visibility.Hidden, toggles[0].Visibility);
        Assert.AreEqual(Visibility.Visible, toggles[1].Visibility);
```

- [ ] **Step 2：確認測試失敗**

執行：`(cd app && dotnet test)`

預期：建置失敗，錯誤為 `CS1061: 'AppListRow' 未包含 'RamText' 的定義`。

- [ ] **Step 3：實作**

修改 `app/MemSweep.Core/AppListRow.cs`：

（1）找到這一段：

```csharp
public sealed record ComponentRow(string Name, string CommitText, string WorkingSetText);
```

整段換成：

```csharp
public sealed record ComponentRow(string Name, string RamText, string CommitText);
```

（2）找到這一段：

```csharp
    private string _commitText = string.Empty;
    private string _workingSetText = string.Empty;
```

整段換成：

```csharp
    private string _ramText = string.Empty;
    private string _commitText = string.Empty;
```

（3）找到這一段：

```csharp
    public string CommitText { get => _commitText; private set => Set(ref _commitText, value); }

    public string WorkingSetText { get => _workingSetText; private set => Set(ref _workingSetText, value); }
```

整段換成：

```csharp
    public string RamText { get => _ramText; private set => Set(ref _ramText, value); }

    public string CommitText { get => _commitText; private set => Set(ref _commitText, value); }
```

（4）找到這一段：

```csharp
        CommitText = FullWindowModel.Gigabytes(app.CommitBytes);
        WorkingSetText = FullWindowModel.Gigabytes(app.RamBytes);
        var components = app.Components
            .Select(c => new ComponentRow(
                c.Name + " ×" + c.ProcessCount.ToString(CultureInfo.InvariantCulture),
                FullWindowModel.Gigabytes(c.CommitBytes),
                FullWindowModel.Gigabytes(c.RamBytes)))
```

整段換成：

```csharp
        RamText = FullWindowModel.Gigabytes(app.RamBytes);
        CommitText = FullWindowModel.Gigabytes(app.CommitBytes);
        // 明細和應用程式一樣依 RAM 排序（Design 3.6）
        var components = app.Components
            .OrderByDescending(c => c.RamBytes)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .Select(c => new ComponentRow(
                c.Name + " ×" + c.ProcessCount.ToString(CultureInfo.InvariantCulture),
                FullWindowModel.Gigabytes(c.RamBytes),
                FullWindowModel.Gigabytes(c.CommitBytes)))
```

（5）找到這一段：

```csharp
    internal void UpdateUnattributed(long bytes)
    {
        Name = GroupedSnapshot.UnattributedName;
        CommitText = FullWindowModel.Gigabytes(bytes);
```

整段換成：

```csharp
    internal void UpdateUnattributed(long ramBytes, long commitBytes)
    {
        Name = GroupedSnapshot.UnattributedName;
        RamText = FullWindowModel.Gigabytes(ramBytes);
        CommitText = FullWindowModel.Gigabytes(commitBytes);
```

修改 `app/MemSweep.Core/FullWindowModel.cs`：

（1）找到這一段：

```csharp
    /// <summary>所有應用程式依 commit 由大到小，最後一列是「系統（不屬於任何程式）」（F-23）。</summary>
```

整段換成：

```csharp
    /// <summary>所有應用程式依 RAM 由大到小，最後一列是「系統（不屬於任何程式）」（F-23）。</summary>
```

（2）找到這一段：

```csharp
        foreach (var app in grouped.Apps)
```

整段換成：

```csharp
        foreach (var app in grouped.AppsByRam)
```

（3）找到這一段：

```csharp
        Place(index++, UnattributedKey, true).UpdateUnattributed(grouped.UnattributedCommitBytes);
```

整段換成：

```csharp
        Place(index++, UnattributedKey, true).UpdateUnattributed(grouped.UnattributedRamBytes, grouped.UnattributedCommitBytes);
```

修改 `app/MemSweep.Desktop/AppsView.xaml`：

（1）找到這一段：

```xml
      <!-- 欄位：展開、名稱、行程數、commit、實際 RAM（工作集） -->
```

整段換成：

```xml
      <!-- 欄位：展開、名稱、行程數、RAM（私用工作集）、commit（F-23） -->
```

（2）找到這一段：

```xml
        <TextBlock Grid.Column="3" Text="Commit" Style="{StaticResource HeaderText}" HorizontalAlignment="Right" />
        <TextBlock Grid.Column="4" Text="實際 RAM" Style="{StaticResource HeaderText}" HorizontalAlignment="Right" />
```

整段換成：

```xml
        <TextBlock Grid.Column="3" Text="RAM" Style="{StaticResource HeaderText}" HorizontalAlignment="Right" />
        <TextBlock Grid.Column="4" Text="Commit" Style="{StaticResource HeaderText}" HorizontalAlignment="Right" />
```

（3）找到這一段：

```xml
                <TextBlock Grid.Column="3" Text="{Binding CommitText}" HorizontalAlignment="Right" />
                <TextBlock Grid.Column="4" Text="{Binding WorkingSetText}" HorizontalAlignment="Right" />
```

整段換成：

```xml
                <TextBlock Grid.Column="3" Text="{Binding RamText}" HorizontalAlignment="Right" />
                <TextBlock Grid.Column="4" Text="{Binding CommitText}" HorizontalAlignment="Right" />
```

（4）找到這一段：

```xml
                      <TextBlock Grid.Column="1" Text="{Binding CommitText}" Foreground="{DynamicResource MutedTextBrush}" HorizontalAlignment="Right" />
                      <TextBlock Grid.Column="2" Text="{Binding WorkingSetText}" Foreground="{DynamicResource MutedTextBrush}" HorizontalAlignment="Right" />
```

整段換成：

```xml
                      <TextBlock Grid.Column="1" Text="{Binding RamText}" Foreground="{DynamicResource MutedTextBrush}" HorizontalAlignment="Right" />
                      <TextBlock Grid.Column="2" Text="{Binding CommitText}" Foreground="{DynamicResource MutedTextBrush}" HorizontalAlignment="Right" />
```

修改 `README.md`：

（1）找到這一段：

```markdown
唯讀的 Windows commit 監看工具：系統匣顯示 commit 使用率、佔用 commit 的應用程式與 commit 尖峰，另附命令列工具 `memsweep-cli`，讓代理在吃記憶體的工作前先檢查剩餘 commit。
```

整段換成：

```markdown
唯讀的 Windows 記憶體監看工具：系統匣顯示實體 RAM 使用率與使用 RAM 的應用程式，並顯示 commit 使用率與 commit 尖峰；另附命令列工具 `memsweep-cli`，讓代理在吃記憶體的工作前先檢查剩餘 commit。
```

（2）找到這一段：

```markdown
- **系統匣圖示**：目前的 commit 使用率（%）。低於橘色門檻為綠色，達到橘色門檻為橘色，達到紅色門檻以上為紅色；預設橘色 70%、紅色 85%。滑鼠停在圖示上會顯示 commit 與實體 RAM 的用量。
- **左鍵點圖示**：開關小面板，顯示 commit 與 RAM 量表、開機以來的尖峰、commit 前五名的應用程式與「系統（不屬於任何程式）」。「複製報告」會把與 `memsweep-cli status` 相同的摘要複製到剪貼簿。
- **右鍵選單**：開啟小面板、完整清單…、設定…、結束 MemSweep。
- **完整清單**：「總覽」「程式」「尖峰紀錄」三個分頁。「程式」列出所有應用程式，可展開看組成的行程；「尖峰紀錄」列出開機尖峰與每日尖峰。
- **設定**：橘色與紅色門檻（1～100，橘色必須小於紅色）、尖峰紀錄保留天數（1～3650，預設 90）、開機時自動啟動（預設關閉）。減少保留天數後，超過天數的紀錄會在下一次取樣時刪除。
```

整段換成：

```markdown
- **系統匣圖示**：目前的實體 RAM 使用率（%），和工作管理員「處理程序」頁「記憶體」欄標題的百分比相同。低於橘色門檻為綠色，達到橘色門檻為橘色，達到紅色門檻以上為紅色；預設橘色 70%、紅色 85%。滑鼠停在圖示上會顯示實體 RAM 與 commit 的用量。commit 不會讓圖示變色。
- **左鍵點圖示**：開關小面板，顯示 RAM 與 commit 量表、開機以來的 commit 尖峰、RAM 前五名的應用程式與「系統（不屬於任何程式）」。「複製報告」會把與 `memsweep-cli status` 相同的摘要複製到剪貼簿；摘要以 commit 為主，前五名可能和小面板不同。
- **右鍵選單**：開啟小面板、完整清單…、設定…、結束 MemSweep。
- **完整清單**：「總覽」「程式」「尖峰紀錄」三個分頁。「程式」依 RAM 列出所有應用程式，也顯示 commit，可展開看組成的行程；「尖峰紀錄」列出 commit 的開機尖峰與每日尖峰。
- **設定**：RAM 使用率的橘色與紅色門檻（1～100，橘色必須小於紅色）、尖峰紀錄保留天數（1～3650，預設 90）、開機時自動啟動（預設關閉）。減少保留天數後，超過天數的紀錄會在下一次取樣時刪除。
```

- [ ] **Step 4：確認測試通過與命令列不變**

執行：`(cd app && dotnet test)`

預期：總計 341，失敗 0。

命令列與尖峰紀錄不在這個 Phase 改動。執行 `git diff --stat origin/main -- app/MemSweep.Cli app/MemSweep.Core/HeadroomCheck.cs app/MemSweep.Core/PeakTracker.cs app/MemSweep.Core/PeakCsv.cs app/MemSweep.Core/PeakRecorder.cs`，預期沒有任何輸出；`app/MemSweep.Core/ReportFormatter.cs` 只有 Task 2 的兩處改名。

- [ ] **Step 5：commit 並推送**

```bash
git add app/MemSweep.Core/AppListRow.cs app/MemSweep.Core/FullWindowModel.cs app/MemSweep.Desktop/AppsView.xaml README.md app/MemSweep.Core.Tests
git commit -F - <<'EOF'
「程式」分頁依 RAM 排序並更新使用說明

所有應用程式與展開的明細改依 RAM 排序，欄位改為 RAM、commit；
「系統（不屬於任何程式）」列同時顯示兩種計法（F-23、Design 3.6）。
README 的使用說明改為以 RAM 為主。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
git fetch origin
git ls-remote --heads origin phase-05-ram-first
gh pr list --head phase-05-ram-first --state all
git push
```

---

## Phase 收尾

> - **人工驗收**（各 Phase 結束時）：A-01 對照工作管理員、A-02、A-03 常駐 1 小時量測、A-05 以 Excel 開啟、A-06、A-08；Phase 05 加上 A-10、A-11 對照工作管理員。
> 5. **RAM 的口徑**：工作管理員「處理程序」頁「記憶體」欄標題的百分比如果不是（總量 − 可用量）÷ 總量，或「詳細資料」頁的「記憶體」欄不是 `WorkingSetPrivateSize`（例如暫停中的市集應用程式只算作用中的部分），A-10、A-11 會失敗。Phase 05 開工時先以實機快照對照工作管理員；不符時先回報，再修改本文件。

- [ ] **完整檢查**：`(cd app && dotnet build -c Release)` 為 0 個警告、0 個錯誤；`(cd app && dotnet test)` 總計 341，失敗 0。
- [ ] **發佈驗收版**：先請使用者從系統匣結束 MemSweep（正式版在 `D:\Utility\MemSweep\`，驗收版要等它結束才能取得單一執行個體），確認 `MemSweep` 行程都已結束，再以 AGENTS.md 的發佈指令輸出到 `app/artifacts/publish/MemSweep/`，用 `explorer.exe` 啟動。驗收期間只開這一支。
- [ ] **A-03 量測**：以發佈的驗收版量測。叫出小面板，按「完整清單…」，三個分頁都切過、展開兩三列後關閉；之後放著不動 60 分鐘，以 `(Get-Process MemSweep).PrivateMemorySize64` 量測，必須不超過 100 MB。開始前提醒使用者不要按「結束 MemSweep」。
- [ ] **自動化檢查**：A-04（發佈資料夾的 `memsweep-cli.exe check --need 1`、`--need 99999`、`--need abc` 結束碼為 0、1、2）；A-07（`NativeSafetyTests` 通過）；`memsweep-cli status` 的輸出格式與 Phase 04 相同。
- [ ] **使用者驗收 A-10、A-11**：同一時間對照工作管理員「處理程序」頁「記憶體」欄標題的百分比與系統匣圖示的數字；任選幾個行程，對照「詳細資料」頁的「記憶體」欄與完整視窗「程式」分頁展開後的 RAM。若工作管理員的百分比總是比圖示少 1（無條件捨去），先回報，不要直接修改。
- [ ] **使用者確認畫面**：圖示的數字與顏色跟著 RAM；提示文字 RAM 在前；小面板與「總覽」RAM 量表在前且依門檻變色、commit 量表固定顏色；前五名依 RAM；「程式」分頁依 RAM 排序、欄位為 RAM、commit；設定視窗的標題為「RAM 使用率的顏色」；淺色與深色都清楚。
- [ ] **開 PR**：所有 Task 都推送後，用 `/repo-open-pr` 開 PR（base `main`，head `phase-05-ram-first`）。合併由使用者決定，使用 merge commit；合併後清理分支。
- [ ] **發佈 1.1.0**（PR 合併後、經使用者同意）：

> - A version change goes through a `release/vX.Y.Z` branch and a pull request.
