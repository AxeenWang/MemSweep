# MemSweep Phase 01：核心與命令列 實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**目標：** 完成 MemSweep.Core 的系統讀取、行程資訊、應用程式歸組與報告，以及 `memsweep-cli` 的 `status`、`status --json`、`check --need` 三種指令。

**架構：** `SystemReader` 以 Win32 唯讀查詢取得快照，`ProcessInfoCache` 透過 `Win32ProcessInfoProvider` 補上路徑與版本資訊，`AppGrouper` 依 SPEC 第 8 章歸組，`MemorySampler` 串起三者。`ReportFormatter` 與 `HeadroomCheck` 產生報告，`CliApp` 解析參數並決定結束碼。

**技術：** C#、.NET 10（SDK 10.0.401）、`LibraryImport`、`System.Text.Json`（`Utf8JsonWriter`）、MSTest（`MSTest.Sdk` 4.5.1，Microsoft.Testing.Platform）。

**依據：** [SPEC](../spec.md)、[Design](../design.md)。本計畫引用的條文都是原文；計畫自己的解讀集中在「計畫的解讀」一節。

**分支：** `phase-01-core-cli`（已從 `origin/main` 建立）。

## 全域限制

以下條文適用於每個 Task：

> - Identifiers are English: code symbols, file and folder names, and branch names.
> - Documents, code comments, and commit messages are written in Traditional Chinese (Taiwan).
> - Commit subjects are Traditional Chinese only, with no type prefix such as `feat:` or `fix:`.
> - Push after each completed Task. Open the pull request only after every Task in the Phase is done.
> - Running anything that changes system memory state requires the user's explicit approval for that run.
> - Never terminate the user's processes to free memory.
> - E-4 以一般使用者權限即可使用全部功能。
> - E-5 所有系統查詢都是唯讀。
> - 執行時期不依賴任何第三方套件。開發時期唯一額外的套件是 MSTest。
> - 全工具的 1 GB 都是 1024³ bytes，與工作管理員一致。

## Review Focus

SPEC 沒有直接寫到、但實際使用最可能出問題的情況。每一項都已在負責的 Task 裡加上測試：

1. 行程很多、行程清單超過預設 1 MB 緩衝區（忙碌的電腦）：應自動加大後重試，不丟例外。Task 2 `GrowsBuffersThatAreTooSmall`。
2. node 的命令列超過 4096 bytes（MCP 參數很長）：仍要取得完整命令列。Task 3 `ReadsCommandLinesLongerThanTheInitialBuffer`。
3. 取樣後、查詢資訊前行程已結束，或是開不了的受保護行程：該行程資訊為未知，取樣照常完成。Task 3 `ProcessThatCannotBeOpenedIsUnknown`。
4. 用逗號當小數點輸入 `--need 7,5`：視為參數錯誤（結束碼 2），不會被當成 75。Task 6 `CheckRejectsInvalidNeed`。
5. 輸出被管線接走（Claude 透過 Bash 讀取）：中文以 UTF-8 輸出，不亂碼。Task 6 `StatusTextIsUtf8`。

## 計畫的解讀

以下是本計畫自己的決定，不是 SPEC 或 Design 的原文：

- 百分比四捨五入成整數（`MidpointRounding.AwayFromZero`），GB 一律顯示小數一位。
- PID 0 在系統回傳的名稱是空字串，本計畫命名為 `Idle`。
- JSON 縮排兩格、換行用 `\n`、中文不跳脫，時間截到秒並帶時區。
- `check` 足夠時只輸出一行；不足時再加上前五名與「系統（不屬於任何程式）」。參數錯誤一律寫到 stderr，結束碼 2，且不取樣。
- `--need` 只接受以小數點表示、大於 0 的有限數字。
- `memsweep-cli` 把主控台輸出編碼設成 UTF-8。在 cmd 視窗執行後，該視窗的字碼頁會變成 65001，這是 .NET 主控台程式的一般做法，暫不處理。
- A-02（單一行程 commit 與工作管理員一致）在本 Phase 以即時測試 `ListsCurrentProcessWithItsCommit` 驗證；2026-10-26 也已比對過 546 個行程完全一致。可以逐行程查看的畫面要到 Phase 03。
- 建置與測試指令寫入 AGENTS.md 屬於文件，依規則直接提交到 `main`，所以放在 PR 合併之後（見「Phase 收尾」）。

## 共同執行規則

- 工作目錄是 repo 根目錄 `D:\AxeenWorld\CodeLab\Projects\MemSweep`，分支 `phase-01-core-cli`。
- `dotnet` 指令一律在 `app/` 底下執行：`app/global.json` 指定了 SDK、MSTest.Sdk 版本與測試執行器，從 repo 根目錄執行時讀不到它，`dotnet test` 會失敗。用子殼層執行，避免改變目前目錄，例如 `(cd app && dotnet test)`。
- 程式碼區塊要逐字照抄，包括全形符號：`：`（U+FF1A）、`（`、`）`、`「`、`」`，以及 `1024³` 的上標 `³`。不要刪除或移動步驟沒有提到的任何一行。
- 測試就是規格：不得修改或刪除測試裡的任何斷言。照抄後測試仍失敗，就回報 BLOCKED。
- 即時測試（`TestCategory("Live")`）會讀取真實系統，全部是唯讀查詢；Task 3 會啟動並等待自己的 `cmd.exe` 子行程。不得執行任何會改變系統記憶體狀態的動作，也不得結束使用者的行程。
- 需要的暫存檔放在 `.camp_claude/` 的任務資料夾。commit 訊息用 Bash 的 heredoc 傳給 `git commit -F -`，不要用 PowerShell 寫訊息檔（會加上 BOM）。
- commit 訊息與 `Co-Authored-By` 尾行照計畫原樣使用，不要換成其他模型名稱。
- 每個 Task 完成後推送。推送前先 `git fetch origin`，並確認遠端分支仍存在、沒有已合併或已關閉的 PR：

  ```bash
  git fetch origin
  git ls-remote --heads origin phase-01-core-cli
  gh pr list --head phase-01-core-cli --state all
  git push
  ```

  Task 1 是第一次推送，遠端還沒有這個分支，改用 `git push -u origin phase-01-core-cli`。

---

### Task 1：方案骨架與記憶體數值換算

**檔案：**

- 新增：`app/global.json`、`app/Directory.Build.props`、`app/MemSweep.slnx`
- 新增：`app/MemSweep.Core/MemSweep.Core.csproj`、`app/MemSweep.Core/MemoryMath.cs`
- 新增：`app/MemSweep.Core.Tests/MemSweep.Core.Tests.csproj`、`app/MemSweep.Core.Tests/MSTestSettings.cs`、`app/MemSweep.Core.Tests/MemoryMathTests.cs`
- 修改：`.gitignore`

**實作的條文：**

> - 建置產出統一放在 `app/artifacts/`（`UseArtifactsOutput`），各專案資料夾內不會出現 `bin/`、`obj/`。
> - Phase 01 在 `.gitignore` 加入 `/app/artifacts/`、`.vs/`、`*.user`。
> - 全工具的 1 GB 都是 1024³ bytes，與工作管理員一致。

**介面：**

- 產生：`MemoryMath.BytesPerGigabyte`（`long`）、`double ToGigabytes(long bytes)`、`long FromGigabytes(double gigabytes)`、`string FormatGigabytes(long bytes)`、`int Percent(long part, long total)`。

- [ ] **Step 1：建立方案骨架**

`app/global.json`：

```json
{
  "sdk": {
    "version": "10.0.401",
    "rollForward": "latestFeature"
  },
  "msbuild-sdks": {
    "MSTest.Sdk": "4.5.1"
  },
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
```

`app/Directory.Build.props`：

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <UseArtifactsOutput>true</UseArtifactsOutput>
  </PropertyGroup>
</Project>
```

`app/MemSweep.slnx`：

```xml
<Solution>
  <Project Path="MemSweep.Core.Tests/MemSweep.Core.Tests.csproj" />
  <Project Path="MemSweep.Core/MemSweep.Core.csproj" />
</Solution>
```

`app/MemSweep.Core/MemSweep.Core.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="MemSweep.Core.Tests" />
  </ItemGroup>

</Project>
```

`app/MemSweep.Core.Tests/MemSweep.Core.Tests.csproj`：

```xml
<Project Sdk="MSTest.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\MemSweep.Core\MemSweep.Core.csproj" />
  </ItemGroup>

</Project>
```

`app/MemSweep.Core.Tests/MSTestSettings.cs`：

```csharp
[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]
```

`.gitignore` 整份換成：

```gitignore
# CodeLab agent areas: temp, persistent tools, user references
/.agent_tools/
/.camp_codex/
/.camp_claude/
/.camp_references/

# Personal agent settings
/CLAUDE.local.md
/.claude/settings.local.json

# Build output and IDE files
/app/artifacts/
.vs/
*.user
```

- [ ] **Step 2：寫失敗的測試**

`app/MemSweep.Core.Tests/MemoryMathTests.cs`：

```csharp
using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class MemoryMathTests
{
    [TestMethod]
    public void OneGigabyteIs1024Cubed()
    {
        Assert.AreEqual(1_073_741_824L, MemoryMath.FromGigabytes(1));
        Assert.AreEqual(1_610_612_736L, MemoryMath.FromGigabytes(1.5));
        Assert.AreEqual(1.5, MemoryMath.ToGigabytes(1_610_612_736L));
    }

    [TestMethod]
    public void FormatGigabytesUsesOneDecimal()
    {
        Assert.AreEqual("31.4", MemoryMath.FormatGigabytes(MemoryMath.FromGigabytes(31.4)));
        Assert.AreEqual("0.0", MemoryMath.FormatGigabytes(0));
        Assert.AreEqual("8.0", MemoryMath.FormatGigabytes(8L * MemoryMath.BytesPerGigabyte));
    }

    [TestMethod]
    public void PercentRoundsHalfAwayFromZero()
    {
        Assert.AreEqual(80, MemoryMath.Percent(80, 100));
        Assert.AreEqual(33, MemoryMath.Percent(1, 3));
        Assert.AreEqual(67, MemoryMath.Percent(2, 3));
        Assert.AreEqual(1, MemoryMath.Percent(1, 200));
        Assert.AreEqual(100, MemoryMath.Percent(5, 5));
    }

    [TestMethod]
    public void PercentOfZeroTotalIsZero()
    {
        Assert.AreEqual(0, MemoryMath.Percent(10, 0));
    }
}
```

- [ ] **Step 3：執行測試，確認失敗**

執行：`(cd app && dotnet test)`

預期：建置失敗，錯誤為

```text
error CS0103: 名稱 'MemoryMath' 不存在於目前的內容中
```

- [ ] **Step 4：實作**

`app/MemSweep.Core/MemoryMath.cs`：

```csharp
using System.Globalization;

namespace MemSweep.Core;

/// <summary>記憶體數值的換算。全工具的 1 GB 都是 1024³ bytes，與工作管理員一致。</summary>
public static class MemoryMath
{
    public const long BytesPerGigabyte = 1L << 30;

    public static double ToGigabytes(long bytes) => bytes / (double)BytesPerGigabyte;

    public static long FromGigabytes(double gigabytes) => (long)Math.Round(gigabytes * BytesPerGigabyte);

    /// <summary>以小數一位表示 GB，例如「31.4」。</summary>
    public static string FormatGigabytes(long bytes) =>
        ToGigabytes(bytes).ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>四捨五入成整數百分比；total 不大於 0 時回傳 0。</summary>
    public static int Percent(long part, long total) =>
        total <= 0 ? 0 : (int)Math.Round(part * 100.0 / total, MidpointRounding.AwayFromZero);
}
```

- [ ] **Step 5：執行測試，確認通過**

執行：`(cd app && dotnet test)`

預期：總計 4，失敗 0。

- [ ] **Step 6：Commit**

```bash
git add .gitignore app/global.json app/Directory.Build.props app/MemSweep.slnx app/MemSweep.Core app/MemSweep.Core.Tests
git status --short
git commit -F - <<'EOF'
建立 app 方案骨架與記憶體數值換算

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

`git status --short` 不應出現 `app/artifacts/`。

- [ ] **Step 7：推送**

第一次推送：`git fetch origin` 後執行 `git push -u origin phase-01-core-cli`。

---

### Task 2：系統記憶體與行程清單讀取

**檔案：**

- 新增：`app/MemSweep.Core/ProcessSample.cs`、`SystemSnapshot.cs`、`ISystemReader.cs`、`SystemReader.cs`
- 新增：`app/MemSweep.Core/Native/Kernel32.cs`、`app/MemSweep.Core/Native/Ntdll.cs`
- 新增：`app/MemSweep.Core.Tests/TestNative.cs`、`app/MemSweep.Core.Tests/SystemReaderLiveTests.cs`

**實作的條文：**

> | 用途 | API |
> | --- | --- |
> | commit 已用、上限、開機以來尖峰；實體 RAM | `K32GetPerformanceInfo` |
> | 開機時間 | `NtQuerySystemInformation(SystemTimeOfDayInformation)` |
> | 所有行程的 PID、父 PID、建立時間、映像名稱、私有 commit、工作集 | `NtQuerySystemInformation(SystemProcessInformation)` |
> | 分頁檔大小 | `NtQuerySystemInformation(SystemPageFileInformation)` |
>
> - 行程 commit 採用 `SYSTEM_PROCESS_INFORMATION` 的 `PagefileUsage`（即 `PrivateUsage`），預期與工作管理員「認可大小」一致；Phase 01 以 A-02 驗證。
> - A-01 commit 已用與上限，和同一時間工作管理員「已認可」顯示的數字相差不超過 0.1 GB。

**介面：**

- 產生：
  - `record ProcessSample(int Pid, int ParentPid, DateTime CreateTimeUtc, string ImageName, long PrivateCommitBytes, long WorkingSetBytes)`
  - `record SystemSnapshot(DateTimeOffset Timestamp, DateTime BootTimeUtc, long CommitUsedBytes, long CommitLimitBytes, long CommitPeakBytes, long PhysicalTotalBytes, long PhysicalAvailableBytes, long PageFileTotalBytes, IReadOnlyList<ProcessSample> Processes)`，另有 `CommitFreeBytes`、`PhysicalUsedBytes`
  - `interface ISystemReader { SystemSnapshot Read(); }`
  - `sealed class SystemReader : ISystemReader`：公開建構函式無參數；`internal SystemReader(int processBufferSize, int pageFileBufferSize)` 給測試用
  - `internal static unsafe partial class Kernel32`、`Ntdll` 與它們的結構（Task 3 會擴充這兩個檔案）

- [ ] **Step 1：寫失敗的測試**

`app/MemSweep.Core.Tests/TestNative.cs`：

```csharp
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MemSweep.Core.Tests;

/// <summary>即時系統測試用來交叉比對的另一組 API。</summary>
internal static class TestNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    public static MemoryStatusEx GetMemoryStatus()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        return status;
    }
}
```

`app/MemSweep.Core.Tests/SystemReaderLiveTests.cs`：

```csharp
using System.Diagnostics;
using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
[TestCategory("Live")]
public sealed class SystemReaderLiveTests
{
    private const double Megabytes64 = 64.0 * 1024 * 1024;

    [TestMethod]
    public void ReadsCommitPhysicalMemoryAndBootTime()
    {
        var snapshot = new SystemReader().Read();

        Assert.IsGreaterThan(0L, snapshot.CommitUsedBytes);
        Assert.IsGreaterThan(snapshot.CommitUsedBytes, snapshot.CommitLimitBytes);
        Assert.IsGreaterThanOrEqualTo(snapshot.CommitUsedBytes, snapshot.CommitPeakBytes);
        Assert.IsGreaterThan(snapshot.PhysicalAvailableBytes, snapshot.PhysicalTotalBytes);
        Assert.IsLessThan(DateTime.UtcNow, snapshot.BootTimeUtc);
        Assert.IsGreaterThan(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), snapshot.BootTimeUtc);
    }

    [TestMethod]
    public void CommitMatchesGlobalMemoryStatusEx()
    {
        var snapshot = new SystemReader().Read();
        var status = TestNative.GetMemoryStatus();

        // ullTotalPageFile 就是 commit 上限；已用量在兩次查詢之間會變動，只要求相差 1 GB 以內
        Assert.AreEqual((double)status.TotalPageFile, snapshot.CommitLimitBytes, Megabytes64);
        Assert.AreEqual((double)status.TotalPhys, snapshot.PhysicalTotalBytes, Megabytes64);
        long usedByStatus = (long)(status.TotalPageFile - status.AvailPageFile);
        Assert.AreEqual((double)usedByStatus, snapshot.CommitUsedBytes, MemoryMath.BytesPerGigabyte);
    }

    [TestMethod]
    public void CommitLimitIsPhysicalMemoryPlusPageFiles()
    {
        var snapshot = new SystemReader().Read();

        Assert.IsGreaterThanOrEqualTo(0L, snapshot.PageFileTotalBytes);
        Assert.AreEqual(
            (double)(snapshot.PhysicalTotalBytes + snapshot.PageFileTotalBytes),
            snapshot.CommitLimitBytes,
            MemoryMath.BytesPerGigabyte);
    }

    [TestMethod]
    public void ListsCurrentProcessWithItsCommit()
    {
        using var current = Process.GetCurrentProcess();
        var snapshot = new SystemReader().Read();
        current.Refresh();

        var self = snapshot.Processes.Single(p => p.Pid == Environment.ProcessId);
        Assert.AreEqual(current.ProcessName + ".exe", self.ImageName, ignoreCase: true);
        Assert.IsGreaterThan(0, self.ParentPid);
        Assert.AreEqual((double)current.PrivateMemorySize64, self.PrivateCommitBytes, current.PrivateMemorySize64 * 0.25);
        Assert.IsGreaterThan(0L, self.WorkingSetBytes);
        Assert.IsLessThan(1.0, Math.Abs((current.StartTime.ToUniversalTime() - self.CreateTimeUtc).TotalSeconds));
    }

    [TestMethod]
    public void GrowsBuffersThatAreTooSmall()
    {
        var expected = new SystemReader().Read();
        var snapshot = new SystemReader(processBufferSize: 16, pageFileBufferSize: 16).Read();

        Assert.AreEqual(expected.PageFileTotalBytes, snapshot.PageFileTotalBytes);
        Assert.IsGreaterThan(50, snapshot.Processes.Count);
        Assert.IsTrue(snapshot.Processes.Any(p => p.Pid == Environment.ProcessId));
    }

    [TestMethod]
    public void ListsIdleAndSystemProcesses()
    {
        var snapshot = new SystemReader().Read();

        Assert.AreEqual("Idle", snapshot.Processes.Single(p => p.Pid == 0).ImageName);
        Assert.AreEqual("System", snapshot.Processes.Single(p => p.Pid == 4).ImageName);
        Assert.IsGreaterThan(50, snapshot.Processes.Count);
    }
}
```

- [ ] **Step 2：執行測試，確認失敗**

執行：`(cd app && dotnet test)`

預期：建置失敗，錯誤為

```text
error CS0246: 找不到類型或命名空間名稱 'SystemReader' (是否遺漏了 using 指示詞或組件參考?)
```

- [ ] **Step 3：實作**

`app/MemSweep.Core/ProcessSample.cs`：

```csharp
namespace MemSweep.Core;

/// <summary>單一行程在某次取樣時的狀態。</summary>
/// <param name="Pid">行程 ID。</param>
/// <param name="ParentPid">建立它的父行程 ID；父行程可能已結束，或 ID 已被重複使用。</param>
/// <param name="CreateTimeUtc">行程建立時間（UTC）。</param>
/// <param name="ImageName">映像名稱，例如「claude.exe」；PID 0 為「Idle」。</param>
/// <param name="PrivateCommitBytes">私有 commit，即工作管理員「認可大小」。</param>
/// <param name="WorkingSetBytes">工作集，即實際佔用的 RAM。</param>
public sealed record ProcessSample(
    int Pid,
    int ParentPid,
    DateTime CreateTimeUtc,
    string ImageName,
    long PrivateCommitBytes,
    long WorkingSetBytes);
```

`app/MemSweep.Core/SystemSnapshot.cs`：

```csharp
namespace MemSweep.Core;

/// <summary>一次取樣得到的系統記憶體狀態與所有行程。</summary>
/// <param name="Timestamp">取樣時間（本機時區）。</param>
/// <param name="BootTimeUtc">本次開機時間（UTC）。</param>
/// <param name="CommitUsedBytes">commit 已用。</param>
/// <param name="CommitLimitBytes">commit 上限。</param>
/// <param name="CommitPeakBytes">Windows 記錄的開機以來 commit 尖峰。</param>
/// <param name="PhysicalTotalBytes">實體 RAM 總量。</param>
/// <param name="PhysicalAvailableBytes">實體 RAM 可用量。</param>
/// <param name="PageFileTotalBytes">所有分頁檔目前大小的總和。</param>
/// <param name="Processes">所有行程。</param>
public sealed record SystemSnapshot(
    DateTimeOffset Timestamp,
    DateTime BootTimeUtc,
    long CommitUsedBytes,
    long CommitLimitBytes,
    long CommitPeakBytes,
    long PhysicalTotalBytes,
    long PhysicalAvailableBytes,
    long PageFileTotalBytes,
    IReadOnlyList<ProcessSample> Processes)
{
    public long CommitFreeBytes => CommitLimitBytes - CommitUsedBytes;

    public long PhysicalUsedBytes => PhysicalTotalBytes - PhysicalAvailableBytes;
}
```

`app/MemSweep.Core/ISystemReader.cs`：

```csharp
namespace MemSweep.Core;

/// <summary>取得系統記憶體狀態與行程清單的來源；測試以假的實作取代。</summary>
public interface ISystemReader
{
    SystemSnapshot Read();
}
```

`app/MemSweep.Core/Native/Kernel32.cs`：

```csharp
using System.Runtime.InteropServices;

namespace MemSweep.Core.Native;

/// <summary>kernel32 的唯讀查詢。</summary>
internal static partial class Kernel32
{
    [LibraryImport("kernel32.dll", EntryPoint = "K32GetPerformanceInfo", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetPerformanceInfo(out PerformanceInformation performanceInformation, uint size);
}

/// <summary>PERFORMANCE_INFORMATION；數量欄位的單位是分頁。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PerformanceInformation
{
    public uint Size;
    public nuint CommitTotal;
    public nuint CommitLimit;
    public nuint CommitPeak;
    public nuint PhysicalTotal;
    public nuint PhysicalAvailable;
    public nuint SystemCache;
    public nuint KernelTotal;
    public nuint KernelPaged;
    public nuint KernelNonpaged;
    public nuint PageSize;
    public uint HandleCount;
    public uint ProcessCount;
    public uint ThreadCount;
}
```

`app/MemSweep.Core/Native/Ntdll.cs`（`SystemProcessInformationEntry` 的欄位順序與型別決定記憶體位移，必須逐字照抄）：

```csharp
using System.Runtime.InteropServices;

namespace MemSweep.Core.Native;

/// <summary>ntdll 的唯讀查詢。</summary>
internal static unsafe partial class Ntdll
{
    public const int SystemTimeOfDayInformation = 3;
    public const int SystemProcessInformation = 5;
    public const int SystemPageFileInformation = 18;
    public const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

    [LibraryImport("ntdll.dll")]
    public static partial int NtQuerySystemInformation(
        int systemInformationClass,
        void* systemInformation,
        uint systemInformationLength,
        out uint returnLength);
}

[StructLayout(LayoutKind.Sequential)]
internal struct UnicodeString
{
    public ushort Length;
    public ushort MaximumLength;
    public nint Buffer;
}

/// <summary>SYSTEM_TIMEOFDAY_INFORMATION；時間為 FILETIME（UTC）。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SystemTimeOfDayInformationData
{
    public long BootTime;
    public long CurrentTime;
    public long TimeZoneBias;
    public uint TimeZoneId;
    public uint Reserved;
    public ulong BootTimeBias;
    public ulong SleepTimeBias;
}

/// <summary>SYSTEM_PROCESS_INFORMATION 的固定部分（之後接著執行緒陣列，這裡用不到）。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SystemProcessInformationEntry
{
    public uint NextEntryOffset;
    public uint NumberOfThreads;
    public long WorkingSetPrivateSize;
    public uint HardFaultCount;
    public uint NumberOfThreadsHighWatermark;
    public ulong CycleTime;
    public long CreateTime;
    public long UserTime;
    public long KernelTime;
    public UnicodeString ImageName;
    public int BasePriority;
    public nint UniqueProcessId;
    public nint InheritedFromUniqueProcessId;
    public uint HandleCount;
    public uint SessionId;
    public nuint UniqueProcessKey;
    public nuint PeakVirtualSize;
    public nuint VirtualSize;
    public uint PageFaultCount;
    public nuint PeakWorkingSetSize;
    public nuint WorkingSetSize;
    public nuint QuotaPeakPagedPoolUsage;
    public nuint QuotaPagedPoolUsage;
    public nuint QuotaPeakNonPagedPoolUsage;
    public nuint QuotaNonPagedPoolUsage;
    public nuint PagefileUsage;
    public nuint PeakPagefileUsage;
    public nuint PrivatePageCount;
}

/// <summary>SYSTEM_PAGEFILE_INFORMATION；大小欄位的單位是分頁。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SystemPageFileInformationEntry
{
    public uint NextEntryOffset;
    public uint TotalSize;
    public uint TotalInUse;
    public uint PeakUsage;
    public UnicodeString PageFileName;
}
```

`app/MemSweep.Core/SystemReader.cs`：

```csharp
using System.ComponentModel;
using System.Runtime.InteropServices;
using MemSweep.Core.Native;

namespace MemSweep.Core;

/// <summary>以 Win32 唯讀查詢取得 <see cref="SystemSnapshot"/>。</summary>
public sealed unsafe class SystemReader : ISystemReader
{
    private const int DefaultProcessBufferSize = 1 << 20;
    private const int DefaultPageFileBufferSize = 4096;

    // 行程清單的緩衝區在多次取樣間重複使用，不足時加大
    private byte[] _processBuffer;
    private readonly int _pageFileBufferSize;

    public SystemReader()
        : this(DefaultProcessBufferSize, DefaultPageFileBufferSize)
    {
    }

    /// <summary>測試用：指定很小的初始緩衝區，驗證不足時會加大重試。</summary>
    internal SystemReader(int processBufferSize, int pageFileBufferSize)
    {
        _processBuffer = new byte[processBufferSize];
        _pageFileBufferSize = pageFileBufferSize;
    }

    public SystemSnapshot Read()
    {
        var timestamp = DateTimeOffset.Now;
        var performance = ReadPerformance();
        long pageSize = (long)performance.PageSize;
        return new SystemSnapshot(
            timestamp,
            ReadBootTimeUtc(),
            (long)performance.CommitTotal * pageSize,
            (long)performance.CommitLimit * pageSize,
            (long)performance.CommitPeak * pageSize,
            (long)performance.PhysicalTotal * pageSize,
            (long)performance.PhysicalAvailable * pageSize,
            ReadPageFileTotalBytes(pageSize),
            ReadProcesses());
    }

    private static PerformanceInformation ReadPerformance()
    {
        if (!Kernel32.GetPerformanceInfo(out var info, (uint)sizeof(PerformanceInformation)))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        return info;
    }

    private static DateTime ReadBootTimeUtc()
    {
        SystemTimeOfDayInformationData data;
        int status = Ntdll.NtQuerySystemInformation(
            Ntdll.SystemTimeOfDayInformation, &data, (uint)sizeof(SystemTimeOfDayInformationData), out _);
        ThrowIfFailed(status, Ntdll.SystemTimeOfDayInformation);
        return DateTime.FromFileTimeUtc(data.BootTime);
    }

    private long ReadPageFileTotalBytes(long pageSize)
    {
        var buffer = new byte[_pageFileBufferSize];
        while (true)
        {
            int status;
            uint needed;
            fixed (byte* p = buffer)
            {
                status = Ntdll.NtQuerySystemInformation(
                    Ntdll.SystemPageFileInformation, p, (uint)buffer.Length, out needed);
                if (status == 0)
                {
                    return needed == 0 ? 0 : SumPageFilePages(p) * pageSize;
                }
            }
            if (status != Ntdll.StatusInfoLengthMismatch)
            {
                ThrowIfFailed(status, Ntdll.SystemPageFileInformation);
            }
            buffer = new byte[Math.Max(needed, (uint)buffer.Length * 2)];
        }
    }

    private static long SumPageFilePages(byte* buffer)
    {
        long pages = 0;
        uint offset = 0;
        while (true)
        {
            var entry = (SystemPageFileInformationEntry*)(buffer + offset);
            pages += entry->TotalSize;
            if (entry->NextEntryOffset == 0)
            {
                return pages;
            }
            offset += entry->NextEntryOffset;
        }
    }

    private List<ProcessSample> ReadProcesses()
    {
        while (true)
        {
            int status;
            uint needed;
            fixed (byte* p = _processBuffer)
            {
                status = Ntdll.NtQuerySystemInformation(
                    Ntdll.SystemProcessInformation, p, (uint)_processBuffer.Length, out needed);
                if (status == 0)
                {
                    return ParseProcesses(p);
                }
            }
            if (status != Ntdll.StatusInfoLengthMismatch)
            {
                ThrowIfFailed(status, Ntdll.SystemProcessInformation);
            }
            // 兩次呼叫之間可能又多了行程，多留 64 KB
            _processBuffer = new byte[Math.Max(needed + 64 * 1024, (uint)_processBuffer.Length * 2)];
        }
    }

    private static List<ProcessSample> ParseProcesses(byte* buffer)
    {
        var processes = new List<ProcessSample>(capacity: 640);
        uint offset = 0;
        while (true)
        {
            var entry = (SystemProcessInformationEntry*)(buffer + offset);
            int pid = (int)entry->UniqueProcessId;
            string imageName = entry->ImageName.Length == 0
                ? (pid == 0 ? "Idle" : string.Empty)
                : new string((char*)entry->ImageName.Buffer, 0, entry->ImageName.Length / 2);
            processes.Add(new ProcessSample(
                pid,
                (int)entry->InheritedFromUniqueProcessId,
                DateTime.FromFileTimeUtc(entry->CreateTime),
                imageName,
                (long)entry->PagefileUsage,
                (long)entry->WorkingSetSize));
            if (entry->NextEntryOffset == 0)
            {
                return processes;
            }
            offset += entry->NextEntryOffset;
        }
    }

    private static void ThrowIfFailed(int status, int informationClass)
    {
        if (status != 0)
        {
            throw new InvalidOperationException(
                $"NtQuerySystemInformation({informationClass}) 失敗：0x{status:X8}");
        }
    }
}
```

- [ ] **Step 4：執行測試，確認通過**

執行：`(cd app && dotnet test)`

預期：總計 10，失敗 0。`ListsCurrentProcessWithItsCommit` 通過代表結構位移正確（映像名稱、PID、commit、建立時間都和 .NET 的 `Process` 一致）。

- [ ] **Step 5：Commit**

```bash
git add app/MemSweep.Core app/MemSweep.Core.Tests
git status --short
git commit -F - <<'EOF'
新增系統記憶體與行程清單讀取

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

- [ ] **Step 6：推送**（依「共同執行規則」先 fetch 並檢查）

---

### Task 3：行程資訊查詢與快取

**檔案：**

- 新增：`app/MemSweep.Core/ProcessInfo.cs`、`IProcessInfoProvider.cs`、`CommandLineLabel.cs`、`ProcessInfoCache.cs`、`Win32ProcessInfoProvider.cs`
- 修改（整份換成）：`app/MemSweep.Core/Native/Kernel32.cs`、`app/MemSweep.Core/Native/Ntdll.cs`
- 新增：`app/MemSweep.Core.Tests/CommandLineLabelTests.cs`、`ProcessInfoCacheTests.cs`、`Win32ProcessInfoProviderLiveTests.cs`

**實作的條文：**

> | 元件 | 職責 | SPEC |
> | --- | --- | --- |
> | `ProcessInfoCache` | 以（PID、建立時間）為鍵，快取執行檔路徑、檔案描述、公司名稱與 node 套件標籤；行程結束後移除 | 第 8 章 |
>
> | 用途 | API |
> | --- | --- |
> | 執行檔完整路徑 | `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` + `QueryFullProcessImageNameW` |
> | 檔案描述與公司名稱 | `System.Diagnostics.FileVersionInfo`（以路徑快取） |
> | node 套件標籤 | `NtQueryInformationProcess(ProcessCommandLineInformation)` |
>
> - 受保護的行程開不了控制代碼時，路徑、描述、公司視為取不到，不重試。

**介面：**

- 使用：Task 2 的 `ProcessSample`、`SystemReader`、`Kernel32`、`Ntdll`、`UnicodeString`。
- 產生：
  - `record ProcessInfo(string? ExecutablePath, string? CompanyName, string? FileDescription, string? PackageLabel)`，`ProcessInfo.Unknown`
  - `interface IProcessInfoProvider { ProcessInfo GetInfo(ProcessSample process); }`
  - `static class CommandLineLabel { string? FromCommandLine(string commandLine); }`
  - `sealed class ProcessInfoCache(IProcessInfoProvider provider)`：`IReadOnlyDictionary<int, ProcessInfo> Resolve(IReadOnlyList<ProcessSample> processes)`
  - `sealed class Win32ProcessInfoProvider : IProcessInfoProvider`；`internal static string? ReadCommandLine(int pid)` 給測試用

- [ ] **Step 1：寫失敗的測試**

`app/MemSweep.Core.Tests/CommandLineLabelTests.cs`：

```csharp
using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class CommandLineLabelTests
{
    [TestMethod]
    public void ScopedPackageWithForwardSlash()
    {
        Assert.AreEqual(
            "@playwright/mcp",
            CommandLineLabel.FromCommandLine(@"""C:\Program Files\nodejs\node.exe"" npx -y @playwright/mcp@latest"));
    }

    [TestMethod]
    public void ScopedPackageInsideNodeModulesPath()
    {
        Assert.AreEqual(
            "@wonderwhy-er/desktop-commander",
            CommandLineLabel.FromCommandLine(
                @"node C:\Users\me\AppData\Roaming\npm\node_modules\@wonderwhy-er\desktop-commander\dist\index.js"));
    }

    [TestMethod]
    public void UnscopedPackageUnderNodeModules()
    {
        Assert.AreEqual(
            "typescript",
            CommandLineLabel.FromCommandLine(@"node C:\work\node_modules\typescript\bin\tsc --watch"));
    }

    [TestMethod]
    public void DotFolderUnderNodeModulesFallsBackToScriptName()
    {
        Assert.AreEqual(
            "vite",
            CommandLineLabel.FromCommandLine(@"node C:\work\node_modules\.bin\..\vite\bin\vite.js"));
    }

    [TestMethod]
    public void ScriptFileName()
    {
        Assert.AreEqual("server", CommandLineLabel.FromCommandLine("node server.mjs --port 3000"));
        Assert.AreEqual("worker", CommandLineLabel.FromCommandLine(@"""C:\Program Files\nodejs\node.exe"" C:\tools\worker.cjs"));
    }

    [TestMethod]
    public void NoLabel()
    {
        Assert.IsNull(CommandLineLabel.FromCommandLine(@"""C:\Program Files\nodejs\node.exe"" --version"));
        Assert.IsNull(CommandLineLabel.FromCommandLine(string.Empty));
    }
}
```

`app/MemSweep.Core.Tests/ProcessInfoCacheTests.cs`：

```csharp
using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class ProcessInfoCacheTests
{
    private static readonly DateTime Created = new(2026, 10, 26, 1, 0, 0, DateTimeKind.Utc);

    private sealed class CountingProvider : IProcessInfoProvider
    {
        public List<ProcessSample> Calls { get; } = new();

        public ProcessInfo GetInfo(ProcessSample process)
        {
            Calls.Add(process);
            return new ProcessInfo($@"C:\apps\{process.ImageName}#{Calls.Count}", null, null, null);
        }
    }

    private static ProcessSample Sample(int pid, string name, DateTime created) =>
        new(pid, 1, created, name, 0, 0);

    [TestMethod]
    public void QueriesEachProcessOnce()
    {
        var provider = new CountingProvider();
        var cache = new ProcessInfoCache(provider);
        var processes = new[] { Sample(100, "a.exe", Created), Sample(200, "b.exe", Created) };

        var first = cache.Resolve(processes);
        var second = cache.Resolve(processes);

        Assert.HasCount(2, provider.Calls);
        Assert.AreEqual(@"C:\apps\a.exe#1", first[100].ExecutablePath);
        Assert.AreEqual(@"C:\apps\b.exe#2", second[200].ExecutablePath);
    }

    [TestMethod]
    public void RequeriesWhenPidIsReused()
    {
        var provider = new CountingProvider();
        var cache = new ProcessInfoCache(provider);
        cache.Resolve([Sample(100, "a.exe", Created)]);

        var result = cache.Resolve([Sample(100, "c.exe", Created.AddMinutes(5))]);

        Assert.HasCount(2, provider.Calls);
        Assert.AreEqual(@"C:\apps\c.exe#2", result[100].ExecutablePath);
    }

    [TestMethod]
    public void ForgetsExitedProcesses()
    {
        var provider = new CountingProvider();
        var cache = new ProcessInfoCache(provider);
        var a = Sample(100, "a.exe", Created);
        cache.Resolve([a]);
        cache.Resolve([Sample(200, "b.exe", Created)]);

        var result = cache.Resolve([a]);

        Assert.HasCount(3, provider.Calls);
        Assert.AreEqual(@"C:\apps\a.exe#3", result[100].ExecutablePath);
    }
}
```

`app/MemSweep.Core.Tests/Win32ProcessInfoProviderLiveTests.cs`：

```csharp
using System.Diagnostics;
using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
[TestCategory("Live")]
public sealed class Win32ProcessInfoProviderLiveTests
{
    private static ProcessSample CurrentProcessSample()
    {
        var snapshot = new SystemReader().Read();
        return snapshot.Processes.Single(p => p.Pid == Environment.ProcessId);
    }

    [TestMethod]
    public void CurrentProcessHasPathAndVersionInfo()
    {
        var info = new Win32ProcessInfoProvider().GetInfo(CurrentProcessSample());

        Assert.AreEqual(Environment.ProcessPath, info.ExecutablePath, ignoreCase: true);
        var version = FileVersionInfo.GetVersionInfo(Environment.ProcessPath!);
        Assert.AreEqual(version.CompanyName?.Trim(), info.CompanyName);
        Assert.AreEqual(version.FileDescription?.Trim(), info.FileDescription);
        Assert.IsNull(info.PackageLabel);
    }

    [TestMethod]
    public void IdleAndSystemAreUnknown()
    {
        var provider = new Win32ProcessInfoProvider();

        Assert.AreSame(ProcessInfo.Unknown, provider.GetInfo(new ProcessSample(0, 0, DateTime.MinValue, "Idle", 0, 0)));
        Assert.AreSame(ProcessInfo.Unknown, provider.GetInfo(new ProcessSample(4, 0, DateTime.MinValue, "System", 0, 0)));
    }

    [TestMethod]
    public void ReadsCommandLinesLongerThanTheInitialBuffer()
    {
        // 初始緩衝區 4096 bytes 只裝得下約 2000 個字元
        string filler = new('x', 6000);
        var start = new ProcessStartInfo("cmd.exe", "/c ping -n 4 127.0.0.1 >nul & rem " + filler)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var child = Process.Start(start)!;
        try
        {
            string? commandLine = Win32ProcessInfoProvider.ReadCommandLine(child.Id);

            Assert.IsNotNull(commandLine);
            StringAssert.EndsWith(commandLine, filler);
        }
        finally
        {
            child.WaitForExit();
        }
    }

    [TestMethod]
    public void ProcessThatCannotBeOpenedIsUnknown()
    {
        // PID 一定是 4 的倍數，3 不可能存在
        var info = new Win32ProcessInfoProvider().GetInfo(new ProcessSample(3, 0, DateTime.MinValue, "ghost.exe", 0, 0));

        Assert.AreSame(ProcessInfo.Unknown, info);
    }
}
```

- [ ] **Step 2：執行測試，確認失敗**

執行：`(cd app && dotnet test)`

預期：建置失敗，錯誤包含

```text
error CS0246: 找不到類型或命名空間名稱 'IProcessInfoProvider' (是否遺漏了 using 指示詞或組件參考?)
error CS0246: 找不到類型或命名空間名稱 'ProcessInfo' (是否遺漏了 using 指示詞或組件參考?)
```

- [ ] **Step 3：實作**

`app/MemSweep.Core/Native/Kernel32.cs` 整份換成：

```csharp
using System.Runtime.InteropServices;

namespace MemSweep.Core.Native;

/// <summary>kernel32 的唯讀查詢。</summary>
internal static unsafe partial class Kernel32
{
    [LibraryImport("kernel32.dll", EntryPoint = "K32GetPerformanceInfo", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetPerformanceInfo(out PerformanceInformation performanceInformation, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryFullProcessImageName(nint process, uint flags, char* exeName, ref uint size);
}

/// <summary>PERFORMANCE_INFORMATION；數量欄位的單位是分頁。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PerformanceInformation
{
    public uint Size;
    public nuint CommitTotal;
    public nuint CommitLimit;
    public nuint CommitPeak;
    public nuint PhysicalTotal;
    public nuint PhysicalAvailable;
    public nuint SystemCache;
    public nuint KernelTotal;
    public nuint KernelPaged;
    public nuint KernelNonpaged;
    public nuint PageSize;
    public uint HandleCount;
    public uint ProcessCount;
    public uint ThreadCount;
}
```

`app/MemSweep.Core/Native/Ntdll.cs` 整份換成：

```csharp
using System.Runtime.InteropServices;

namespace MemSweep.Core.Native;

/// <summary>ntdll 的唯讀查詢。</summary>
internal static unsafe partial class Ntdll
{
    public const int SystemTimeOfDayInformation = 3;
    public const int SystemProcessInformation = 5;
    public const int SystemPageFileInformation = 18;
    public const int ProcessCommandLineInformation = 60;
    public const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

    [LibraryImport("ntdll.dll")]
    public static partial int NtQuerySystemInformation(
        int systemInformationClass,
        void* systemInformation,
        uint systemInformationLength,
        out uint returnLength);

    [LibraryImport("ntdll.dll")]
    public static partial int NtQueryInformationProcess(
        nint processHandle,
        int processInformationClass,
        void* processInformation,
        uint processInformationLength,
        out uint returnLength);
}

[StructLayout(LayoutKind.Sequential)]
internal struct UnicodeString
{
    public ushort Length;
    public ushort MaximumLength;
    public nint Buffer;
}

/// <summary>SYSTEM_TIMEOFDAY_INFORMATION；時間為 FILETIME（UTC）。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SystemTimeOfDayInformationData
{
    public long BootTime;
    public long CurrentTime;
    public long TimeZoneBias;
    public uint TimeZoneId;
    public uint Reserved;
    public ulong BootTimeBias;
    public ulong SleepTimeBias;
}

/// <summary>SYSTEM_PROCESS_INFORMATION 的固定部分（之後接著執行緒陣列，這裡用不到）。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SystemProcessInformationEntry
{
    public uint NextEntryOffset;
    public uint NumberOfThreads;
    public long WorkingSetPrivateSize;
    public uint HardFaultCount;
    public uint NumberOfThreadsHighWatermark;
    public ulong CycleTime;
    public long CreateTime;
    public long UserTime;
    public long KernelTime;
    public UnicodeString ImageName;
    public int BasePriority;
    public nint UniqueProcessId;
    public nint InheritedFromUniqueProcessId;
    public uint HandleCount;
    public uint SessionId;
    public nuint UniqueProcessKey;
    public nuint PeakVirtualSize;
    public nuint VirtualSize;
    public uint PageFaultCount;
    public nuint PeakWorkingSetSize;
    public nuint WorkingSetSize;
    public nuint QuotaPeakPagedPoolUsage;
    public nuint QuotaPagedPoolUsage;
    public nuint QuotaPeakNonPagedPoolUsage;
    public nuint QuotaNonPagedPoolUsage;
    public nuint PagefileUsage;
    public nuint PeakPagefileUsage;
    public nuint PrivatePageCount;
}

/// <summary>SYSTEM_PAGEFILE_INFORMATION；大小欄位的單位是分頁。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SystemPageFileInformationEntry
{
    public uint NextEntryOffset;
    public uint TotalSize;
    public uint TotalInUse;
    public uint PeakUsage;
    public UnicodeString PageFileName;
}
```

`app/MemSweep.Core/ProcessInfo.cs`：

```csharp
namespace MemSweep.Core;

/// <summary>行程的靜態資訊；取不到的欄位為 null。</summary>
/// <param name="ExecutablePath">執行檔完整路徑。</param>
/// <param name="CompanyName">執行檔版本資訊的公司名稱（已去頭尾空白）。</param>
/// <param name="FileDescription">執行檔版本資訊的檔案描述（已去頭尾空白）。</param>
/// <param name="PackageLabel">node 行程從命令列取出的套件或腳本名稱。</param>
public sealed record ProcessInfo(
    string? ExecutablePath,
    string? CompanyName,
    string? FileDescription,
    string? PackageLabel)
{
    public static ProcessInfo Unknown { get; } = new(null, null, null, null);
}
```

`app/MemSweep.Core/IProcessInfoProvider.cs`：

```csharp
namespace MemSweep.Core;

/// <summary>查詢單一行程的 <see cref="ProcessInfo"/>；測試以假的實作取代。</summary>
public interface IProcessInfoProvider
{
    ProcessInfo GetInfo(ProcessSample process);
}
```

`app/MemSweep.Core/CommandLineLabel.cs`：

```csharp
using System.Text.RegularExpressions;

namespace MemSweep.Core;

/// <summary>從 node 行程的命令列取出用來區分的標籤。</summary>
public static partial class CommandLineLabel
{
    /// <summary>
    /// 依序找：具名空間套件（@scope/name）、node_modules 底下的套件名稱、.js/.cjs/.mjs 腳本名稱；
    /// 都找不到時回傳 null。
    /// </summary>
    public static string? FromCommandLine(string commandLine)
    {
        var scoped = ScopedPackage().Match(commandLine);
        if (scoped.Success)
        {
            return $"{scoped.Groups[1].Value}/{scoped.Groups[2].Value}";
        }

        var package = NodeModulesPackage().Match(commandLine);
        if (package.Success)
        {
            return package.Groups[1].Value;
        }

        var script = ScriptFile().Match(commandLine);
        return script.Success ? script.Groups[1].Value : null;
    }

    [GeneratedRegex(@"(@[\w.-]+)[\\/]([\w.-]+)")]
    private static partial Regex ScopedPackage();

    // 跳過 .bin 這類以點開頭的資料夾
    [GeneratedRegex(@"node_modules[\\/](?!\.)([\w.-]+)")]
    private static partial Regex NodeModulesPackage();

    [GeneratedRegex(@"([\w.-]+)\.[cm]?js\b")]
    private static partial Regex ScriptFile();
}
```

`app/MemSweep.Core/ProcessInfoCache.cs`：

```csharp
namespace MemSweep.Core;

/// <summary>
/// 以（PID、建立時間）為鍵快取 <see cref="ProcessInfo"/>：同一個行程只向 provider 查一次，
/// PID 被重複使用時會重新查詢，已結束的行程從快取移除。
/// </summary>
public sealed class ProcessInfoCache
{
    private readonly IProcessInfoProvider _provider;
    private Dictionary<(int Pid, DateTime CreateTimeUtc), ProcessInfo> _entries = new();

    public ProcessInfoCache(IProcessInfoProvider provider)
    {
        _provider = provider;
    }

    /// <summary>回傳這次快照中每個 PID 的行程資訊。</summary>
    public IReadOnlyDictionary<int, ProcessInfo> Resolve(IReadOnlyList<ProcessSample> processes)
    {
        var next = new Dictionary<(int Pid, DateTime CreateTimeUtc), ProcessInfo>(processes.Count);
        var result = new Dictionary<int, ProcessInfo>(processes.Count);
        foreach (var process in processes)
        {
            var key = (process.Pid, process.CreateTimeUtc);
            if (!_entries.TryGetValue(key, out var info))
            {
                info = _provider.GetInfo(process);
            }
            next[key] = info;
            result[process.Pid] = info;
        }
        _entries = next;
        return result;
    }
}
```

`app/MemSweep.Core/Win32ProcessInfoProvider.cs`：

```csharp
using System.Diagnostics;
using MemSweep.Core.Native;

namespace MemSweep.Core;

/// <summary>以 PROCESS_QUERY_LIMITED_INFORMATION 唯讀查詢行程資訊；開不了的行程回傳 <see cref="ProcessInfo.Unknown"/>。</summary>
public sealed unsafe class Win32ProcessInfoProvider : IProcessInfoProvider
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    // 版本資訊以執行檔路徑快取，同一支程式的多個行程只讀一次
    private readonly Dictionary<string, (string? Company, string? Description)> _versionInfo =
        new(StringComparer.OrdinalIgnoreCase);

    public ProcessInfo GetInfo(ProcessSample process)
    {
        // Idle 與 System 沒有執行檔
        if (process.Pid is 0 or 4)
        {
            return ProcessInfo.Unknown;
        }

        nint handle = Kernel32.OpenProcess(ProcessQueryLimitedInformation, false, (uint)process.Pid);
        if (handle == 0)
        {
            return ProcessInfo.Unknown;
        }

        try
        {
            string? path = QueryImagePath(handle);
            string? label = string.Equals(process.ImageName, "node.exe", StringComparison.OrdinalIgnoreCase)
                ? CommandLineLabel.FromCommandLine(QueryCommandLine(handle) ?? string.Empty)
                : null;
            var (company, description) = path is null ? (null, null) : GetVersionInfo(path);
            return new ProcessInfo(path, company, description, label);
        }
        finally
        {
            Kernel32.CloseHandle(handle);
        }
    }

    /// <summary>測試用：讀取指定行程的命令列；開不了時回傳 null。</summary>
    internal static string? ReadCommandLine(int pid)
    {
        nint handle = Kernel32.OpenProcess(ProcessQueryLimitedInformation, false, (uint)pid);
        if (handle == 0)
        {
            return null;
        }

        try
        {
            return QueryCommandLine(handle);
        }
        finally
        {
            Kernel32.CloseHandle(handle);
        }
    }

    private static string? QueryImagePath(nint handle)
    {
        const int capacity = 1024;
        char* buffer = stackalloc char[capacity];
        uint size = capacity;
        return Kernel32.QueryFullProcessImageName(handle, 0, buffer, ref size)
            ? new string(buffer, 0, (int)size)
            : null;
    }

    private static string? QueryCommandLine(nint handle)
    {
        var buffer = new byte[4096];
        while (true)
        {
            int status;
            uint needed;
            fixed (byte* p = buffer)
            {
                status = Ntdll.NtQueryInformationProcess(
                    handle, Ntdll.ProcessCommandLineInformation, p, (uint)buffer.Length, out needed);
                if (status == 0)
                {
                    var text = (UnicodeString*)p;
                    return text->Length == 0 ? string.Empty : new string((char*)text->Buffer, 0, text->Length / 2);
                }
            }
            if (status != Ntdll.StatusInfoLengthMismatch || needed <= buffer.Length)
            {
                return null;
            }
            buffer = new byte[needed];
        }
    }

    private (string? Company, string? Description) GetVersionInfo(string path)
    {
        if (!_versionInfo.TryGetValue(path, out var info))
        {
            try
            {
                var version = FileVersionInfo.GetVersionInfo(path);
                info = (Normalize(version.CompanyName), Normalize(version.FileDescription));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                info = (null, null);
            }
            _versionInfo[path] = info;
        }
        return info;
    }

    private static string? Normalize(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
```

- [ ] **Step 4：執行測試，確認通過**

執行：`(cd app && dotnet test)`

預期：總計 23，失敗 0。

- [ ] **Step 5：Commit**

```bash
git add app/MemSweep.Core app/MemSweep.Core.Tests
git status --short
git commit -F - <<'EOF'
新增行程資訊查詢與快取

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

- [ ] **Step 6：推送**（依「共同執行規則」先 fetch 並檢查）

---

### Task 4：應用程式歸組

**檔案：**

- 新增：`app/MemSweep.Core/GroupedSnapshot.cs`、`AppGrouper.cs`、`MemorySampler.cs`
- 新增：`app/MemSweep.Core.Tests/AppGrouperTests.cs`、`MemorySamplerLiveTests.cs`

**實作的條文（SPEC 第 8 章）：**

> - R-1 從每個行程往上追父行程，找出它所屬的最上層行程。遇到下列任一情況就停止往上，該行程成為最上層：
> - 父行程是 Windows 殼層或系統行程，例如 explorer.exe、sihost.exe、services.exe、wininit.exe。
> - 父行程已不存在。
> - 該行程是另一個獨立的應用程式，而不是啟動者的一部分。例如從 Steam 啟動的遊戲、在終端機或 IDE 裡執行的 Claude Code 與 Codex CLI、從 IDE 偵錯啟動的修改器、由其他程式開出的瀏覽器。
> - R-2 輔助行程一律歸屬啟動它的應用程式，不會因 R-1 第三點而自成一列。輔助行程包含命令列殼層與腳本執行環境（例如 cmd.exe、conhost.exe、pwsh.exe、node.exe、python.exe），以及 msedgewebview2.exe。
> - R-3 父行程的建立時間晚於子行程時，視為父行程已不存在，避免行程 ID 被重複使用而誤判。
> - R-4 最上層行程加上歸屬它的所有子孫行程，合成一個應用程式。名稱取最上層行程執行檔的檔案描述，取不到時用執行檔名稱。
> - R-5 多個最上層行程的執行檔路徑相同時，合併為一列，行程數合計。例如父程式已結束、各自成為最上層的 node 行程，合成一列。
> - R-6 執行檔位於 Windows 資料夾（例如 `C:\Windows`）下的最上層行程，全部合併為一列「Windows（系統與服務）」。
> - R-7 R-1 第三點「獨立應用程式」的判斷方式（例如比對執行檔的發行者），以及殼層、系統行程與輔助行程的清單，由 Design 決定，實作時可依實測調整。結果必須符合 A-09 的範例。
> - A-09 應用程式歸組符合下列範例：
> - 從 Steam 啟動的遊戲自成一列，不併入 Steam。
> - 在終端機或 IDE 裡執行的 Claude Code、Codex CLI 自成一列，不併入終端機或 IDE。
> - Claude 桌面版的工作階段與它啟動的 MCP node 行程，併入 Claude 那一列。
> - WebView2 行程併入使用它的應用程式。
> - 父程式已結束的 node 行程合成一列。

**實作的條文（Design 3.3）：**

> - 殼層與系統行程（往上追到它們就停止）：`Idle`、`System`、`Secure System`、`Registry`、`Memory Compression`、`smss.exe`、`csrss.exe`、`wininit.exe`、`winlogon.exe`、`services.exe`、`svchost.exe`、`userinit.exe`、`explorer.exe`、`sihost.exe`。
> - 輔助行程（R-2，一律歸屬啟動者）：`cmd.exe`、`conhost.exe`、`OpenConsole.exe`、`pwsh.exe`、`powershell.exe`、`node.exe`、`bun.exe`、`python.exe`、`pythonw.exe`、`uv.exe`、`uvx.exe`、`msedgewebview2.exe`。
>
> **「獨立應用程式」的判斷**（R-1 第三點）：行程不是輔助行程，而且它和父行程的執行檔公司名稱（`FileVersionInfo.CompanyName`）第一個單字不同，或任一方取不到公司名稱。第一個單字是去頭尾空白後，開頭連續的字母或數字，比較時不分大小寫。

步驟 4：

> 4. 最上層執行檔位於 `%SystemRoot%` 之下，或取不到路徑而名稱在殼層與系統清單中的，全部併入「Windows（系統與服務）」（R-6）。

步驟 6：

> 6. 展開明細（F-23）依執行檔名稱合併計數；node 行程再依命令列取出的套件標籤分開，例如命令列含 `@playwright/mcp` 就標為「node：@playwright/mcp」，取不到時標為「node」。

3.2 節：

> - 「系統（不屬於任何程式）」= commit 已用 − 所有行程 `PagefileUsage` 的總和，小於 0 時以 0 計。

**介面：**

- 使用：Task 2 的 `SystemSnapshot`、`ProcessSample`、`ISystemReader`、`SystemReader`；Task 3 的 `ProcessInfo`、`IProcessInfoProvider`、`ProcessInfoCache`、`Win32ProcessInfoProvider`。
- 產生：
  - `record AppComponent(string Name, int ProcessCount, long CommitBytes, long WorkingSetBytes)`
  - `record AppGroup(string Name, bool IsWindows, int ProcessCount, long CommitBytes, long WorkingSetBytes, IReadOnlyList<AppComponent> Components)`
  - `record GroupedSnapshot(SystemSnapshot Snapshot, IReadOnlyList<AppGroup> Apps, long UnattributedBytes)`，常數 `UnattributedName = "系統（不屬於任何程式）"`
  - `sealed class AppGrouper`：`AppGrouper()`、`AppGrouper(string windowsDirectory)`、`GroupedSnapshot Group(SystemSnapshot snapshot, IReadOnlyDictionary<int, ProcessInfo> infos)`、常數 `WindowsGroupName = "Windows（系統與服務）"`、`internal static string? CompanyKey(string? companyName)`
  - `sealed class MemorySampler`：`MemorySampler(ISystemReader, IProcessInfoProvider, AppGrouper)`、`static MemorySampler CreateDefault()`、`GroupedSnapshot Sample()`

- [ ] **Step 1：寫失敗的測試**

`app/MemSweep.Core.Tests/AppGrouperTests.cs`：

```csharp
using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class AppGrouperTests
{
    private const long Mb = 1024L * 1024;

    /// <summary>組合測試用快照；行程預設在開機後第 pid 秒建立，所以 PID 較小的父行程一定較早建立。</summary>
    private sealed class Fixture
    {
        private static readonly DateTime Boot = new(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc);
        private readonly List<ProcessSample> _processes = new();
        private readonly Dictionary<int, ProcessInfo> _infos = new();

        public Fixture Add(
            int pid,
            int parentPid,
            string imageName,
            long commitMb,
            string? path = null,
            string? company = null,
            string? description = null,
            string? label = null,
            int? createdSecond = null)
        {
            var created = Boot.AddSeconds(createdSecond ?? pid);
            _processes.Add(new ProcessSample(pid, parentPid, created, imageName, commitMb * Mb, commitMb * Mb / 2));
            _infos[pid] = path is null && company is null && description is null && label is null
                ? ProcessInfo.Unknown
                : new ProcessInfo(path, company, description, label);
            return this;
        }

        public GroupedSnapshot Group(long commitUsedMb = 100_000)
        {
            var snapshot = new SystemSnapshot(
                new DateTimeOffset(2026, 10, 26, 14, 35, 2, TimeSpan.FromHours(8)),
                Boot,
                commitUsedMb * Mb,
                200_000 * Mb,
                150_000 * Mb,
                32_768 * Mb,
                16_384 * Mb,
                8_192 * Mb,
                _processes);
            return new AppGrouper(@"C:\Windows").Group(snapshot, _infos);
        }
    }

    private static AppGroup Row(GroupedSnapshot grouped, string name) => grouped.Apps.Single(a => a.Name == name);

    private static string Components(AppGroup app) =>
        string.Join(", ", app.Components.Select(c => $"{c.Name}×{c.ProcessCount}"));

    [TestMethod]
    public void GameLaunchedBySteamIsItsOwnRow()
    {
        var grouped = new Fixture()
            .Add(20, 9999, "steam.exe", 300, @"C:\Steam\steam.exe", "Valve Corporation", "Steam")
            .Add(30, 20, "Game.exe", 12_000, @"D:\Games\Game\Game.exe", "Krafton Inc.", "Some Game")
            .Add(40, 20, "steamwebhelper.exe", 500, @"C:\Steam\bin\steamwebhelper.exe", "Valve Corporation", "Steam Client WebHelper")
            .Group();

        Assert.AreEqual(2, Row(grouped, "Steam").ProcessCount);
        Assert.AreEqual(800 * Mb, Row(grouped, "Steam").CommitBytes);
        Assert.AreEqual(1, Row(grouped, "Some Game").ProcessCount);
        Assert.AreEqual(12_000 * Mb, Row(grouped, "Some Game").CommitBytes);
    }

    [TestMethod]
    public void ClaudeCodeInTerminalIsItsOwnRow()
    {
        var grouped = new Fixture()
            .Add(10, 4, "explorer.exe", 90, @"C:\Windows\explorer.exe", "Microsoft Corporation", "Windows 檔案總管")
            .Add(100, 10, "WindowsTerminal.exe", 70, @"C:\Apps\WT\WindowsTerminal.exe", "Microsoft Corporation", "Windows Terminal")
            .Add(110, 100, "pwsh.exe", 60, @"C:\Apps\PS\pwsh.exe", "Microsoft Corporation", "PowerShell 7")
            .Add(120, 110, "claude.exe", 650, @"C:\Users\me\claude-code\claude.exe", "Anthropic PBC", "Claude Code")
            .Add(130, 120, "node.exe", 80, @"C:\Program Files\nodejs\node.exe", "Node.js", "Node.js JavaScript Runtime", "@playwright/mcp")
            .Group();

        var terminal = Row(grouped, "Windows Terminal");
        Assert.AreEqual(130 * Mb, terminal.CommitBytes);
        Assert.AreEqual("WindowsTerminal.exe×1, pwsh.exe×1", Components(terminal));
        var claudeCode = Row(grouped, "Claude Code");
        Assert.AreEqual(730 * Mb, claudeCode.CommitBytes);
        Assert.AreEqual("claude.exe×1, node：@playwright/mcp×1", Components(claudeCode));
    }

    [TestMethod]
    public void ClaudeDesktopSessionsAndMcpStayInClaude()
    {
        var grouped = new Fixture()
            .Add(200, 4, "sihost.exe", 5, @"C:\Windows\System32\sihost.exe", "Microsoft Corporation", "Shell Infrastructure Host")
            .Add(210, 200, "claude.exe", 400, @"C:\Program Files\WindowsApps\Claude\app\claude.exe", "Anthropic", "Claude")
            .Add(220, 210, "claude.exe", 300, @"C:\Program Files\WindowsApps\Claude\app\claude.exe", "Anthropic", "Claude")
            .Add(230, 210, "claude.exe", 650, @"C:\Users\me\claude-code\claude.exe", "Anthropic PBC", "Claude Code")
            .Add(240, 230, "cmd.exe", 4, @"C:\Windows\system32\cmd.exe", "Microsoft Corporation", "Windows 命令處理程式")
            .Add(250, 240, "node.exe", 200, @"C:\Program Files\nodejs\node.exe", "Node.js", "Node.js JavaScript Runtime", "@wonderwhy-er/desktop-commander")
            .Group();

        var claude = Row(grouped, "Claude");
        Assert.AreEqual(5, claude.ProcessCount);
        Assert.AreEqual(1_554 * Mb, claude.CommitBytes);
        Assert.AreEqual("claude.exe×3, node：@wonderwhy-er/desktop-commander×1, cmd.exe×1", Components(claude));
        Assert.IsFalse(grouped.Apps.Any(a => a.Name == "Claude Code"));
    }

    [TestMethod]
    public void WebView2JoinsTheAppThatUsesIt()
    {
        var grouped = new Fixture()
            .Add(300, 9999, "claude.exe", 400, @"C:\Apps\Claude\claude.exe", "Anthropic", "Claude")
            .Add(310, 300, "msedgewebview2.exe", 150, @"C:\EdgeWebView\msedgewebview2.exe", "Microsoft Corporation", "Microsoft Edge WebView2")
            .Add(320, 310, "msedgewebview2.exe", 90, @"C:\EdgeWebView\msedgewebview2.exe", "Microsoft Corporation", "Microsoft Edge WebView2")
            .Group();

        Assert.HasCount(1, grouped.Apps);
        Assert.AreEqual("claude.exe×1, msedgewebview2.exe×2", Components(Row(grouped, "Claude")));
    }

    [TestMethod]
    public void OrphanNodeProcessesWithTheSamePathMergeIntoOneRow()
    {
        var grouped = new Fixture()
            .Add(400, 9001, "node.exe", 70, @"C:\Program Files\nodejs\node.exe", "Node.js", "Node.js JavaScript Runtime", "@playwright/mcp")
            .Add(410, 9002, "node.exe", 50, @"C:\Program Files\nodejs\node.exe", "Node.js", "Node.js JavaScript Runtime", "@playwright/mcp")
            .Add(420, 9003, "NODE.EXE", 30, @"C:\PROGRAM FILES\NODEJS\NODE.EXE", "Node.js", "Node.js JavaScript Runtime")
            .Add(430, 9004, "node.exe", 20, @"C:\Users\me\.cache\runtime\node.exe", "Node.js", "Node.js JavaScript Runtime")
            .Group();

        Assert.HasCount(2, grouped.Apps);
        var merged = grouped.Apps[0];
        Assert.AreEqual("Node.js JavaScript Runtime", merged.Name);
        Assert.AreEqual(3, merged.ProcessCount);
        Assert.AreEqual(150 * Mb, merged.CommitBytes);
        Assert.AreEqual("node：@playwright/mcp×2, NODE.EXE×1", Components(merged));
        Assert.AreEqual(20 * Mb, grouped.Apps[1].CommitBytes);
    }

    [TestMethod]
    public void ProcessWithoutCompanyIsItsOwnRow()
    {
        var grouped = new Fixture()
            .Add(500, 9999, "devenv.exe", 900, @"C:\VS\devenv.exe", "Microsoft Corporation", "Microsoft Visual Studio")
            .Add(510, 500, "Trainer.exe", 40, @"D:\Work\Trainer\bin\Trainer.exe")
            .Group();

        Assert.AreEqual(900 * Mb, Row(grouped, "Microsoft Visual Studio").CommitBytes);
        Assert.AreEqual(40 * Mb, Row(grouped, "Trainer.exe").CommitBytes);
    }

    [TestMethod]
    public void HelperStartedFromTheShellIsItsOwnRow()
    {
        var grouped = new Fixture()
            .Add(10, 4, "explorer.exe", 90, @"C:\Windows\explorer.exe", "Microsoft Corporation", "Windows 檔案總管")
            .Add(600, 10, "pwsh.exe", 60, @"C:\Apps\PS\pwsh.exe", "Microsoft Corporation", "PowerShell 7")
            .Add(610, 600, "node.exe", 35, @"C:\Program Files\nodejs\node.exe", "Node.js", "Node.js JavaScript Runtime")
            .Group();

        Assert.AreEqual("pwsh.exe×1, node.exe×1", Components(Row(grouped, "PowerShell 7")));
    }

    [TestMethod]
    public void ParentCreatedAfterChildIsIgnored()
    {
        // PID 700 已被一個比子行程晚建立的新行程重複使用
        var grouped = new Fixture()
            .Add(700, 9999, "newapp.exe", 25, @"C:\Apps\New\newapp.exe", "New Corp", "New App", createdSecond: 5000)
            .Add(710, 700, "oldchild.exe", 15, @"C:\Apps\Old\oldchild.exe", "New Corp", "Old Child", createdSecond: 4000)
            .Group();

        Assert.AreEqual(25 * Mb, Row(grouped, "New App").CommitBytes);
        Assert.AreEqual(15 * Mb, Row(grouped, "Old Child").CommitBytes);
    }

    [TestMethod]
    public void WindowsProcessesMergeIntoTheWindowsRow()
    {
        var grouped = new Fixture()
            .Add(0, 0, "Idle", 0, createdSecond: 0)
            .Add(4, 0, "System", 1, createdSecond: 1)
            .Add(8, 4, "Registry", 9, createdSecond: 2)
            .Add(12, 4, "Memory Compression", 3, createdSecond: 3)
            .Add(800, 9999, "services.exe", 7, @"C:\WINDOWS\system32\services.exe", "Microsoft Corporation", "Services")
            .Add(810, 800, "svchost.exe", 40, @"C:\WINDOWS\system32\svchost.exe", "Microsoft Corporation", "Host Process")
            .Add(820, 9999, "dwm.exe", 900, @"C:\Windows\System32\dwm.exe", "Microsoft Corporation", "Desktop Window Manager")
            .Group();

        Assert.HasCount(1, grouped.Apps);
        var windows = grouped.Apps[0];
        Assert.AreEqual(AppGrouper.WindowsGroupName, windows.Name);
        Assert.IsTrue(windows.IsWindows);
        Assert.AreEqual(7, windows.ProcessCount);
        Assert.AreEqual(960 * Mb, windows.CommitBytes);
    }

    [TestMethod]
    public void UnknownProcessOutsideTheSystemListIsNamedByImage()
    {
        var grouped = new Fixture()
            .Add(900, 9999, "mystery.exe", 11)
            .Add(910, 9999, "Secure System", 2, createdSecond: 1)
            .Group();

        var mystery = Row(grouped, "mystery.exe");
        Assert.IsFalse(mystery.IsWindows);
        Assert.AreEqual(11 * Mb, mystery.CommitBytes);
        Assert.IsTrue(Row(grouped, AppGrouper.WindowsGroupName).IsWindows);
    }

    [TestMethod]
    public void AppsAreSortedByCommitDescending()
    {
        var grouped = new Fixture()
            .Add(1000, 9999, "small.exe", 10, @"C:\A\small.exe", "A Corp", "Small")
            .Add(1010, 9999, "big.exe", 300, @"C:\B\big.exe", "B Corp", "Big")
            .Add(1020, 9999, "mid.exe", 120, @"C:\C\mid.exe", "C Corp", "Mid")
            .Group();

        CollectionAssert.AreEqual(new[] { "Big", "Mid", "Small" }, grouped.Apps.Select(a => a.Name).ToArray());
    }

    [TestMethod]
    public void UnattributedIsCommitUsedMinusAllProcessCommit()
    {
        var fixture = new Fixture()
            .Add(1100, 9999, "a.exe", 300, @"C:\A\a.exe", "A Corp", "A")
            .Add(1110, 9999, "b.exe", 200, @"C:\B\b.exe", "B Corp", "B");

        Assert.AreEqual(1_500 * Mb, fixture.Group(commitUsedMb: 2_000).UnattributedBytes);
        Assert.AreEqual(0L, fixture.Group(commitUsedMb: 400).UnattributedBytes);
    }

    [TestMethod]
    public void CompanyKeyIsTheFirstWordInLowerCase()
    {
        Assert.AreEqual("anthropic", AppGrouper.CompanyKey("Anthropic PBC"));
        Assert.AreEqual("anthropic", AppGrouper.CompanyKey("  Anthropic"));
        Assert.AreEqual("openai", AppGrouper.CompanyKey("OpenAI OpCo, LLC"));
        Assert.AreEqual("micro", AppGrouper.CompanyKey("Micro-Star INT'L CO., LTD."));
        Assert.AreEqual("騰訊科技", AppGrouper.CompanyKey("騰訊科技 有限公司"));
        Assert.IsNull(AppGrouper.CompanyKey("   "));
        Assert.IsNull(AppGrouper.CompanyKey(null));
    }
}
```

`app/MemSweep.Core.Tests/MemorySamplerLiveTests.cs`：

```csharp
using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
[TestCategory("Live")]
public sealed class MemorySamplerLiveTests
{
    [TestMethod]
    public void EveryProcessBelongsToExactlyOneApp()
    {
        var grouped = MemorySampler.CreateDefault().Sample();
        var processes = grouped.Snapshot.Processes;
        long processCommit = processes.Sum(p => p.PrivateCommitBytes);

        Assert.AreEqual(processes.Count, grouped.Apps.Sum(a => a.ProcessCount));
        Assert.AreEqual(processCommit, grouped.Apps.Sum(a => a.CommitBytes));
        Assert.AreEqual(Math.Max(0, grouped.Snapshot.CommitUsedBytes - processCommit), grouped.UnattributedBytes);
    }

    [TestMethod]
    public void WindowsRowHoldsTheSystemProcesses()
    {
        var grouped = MemorySampler.CreateDefault().Sample();

        var windows = grouped.Apps.Single(a => a.IsWindows);
        Assert.AreEqual(AppGrouper.WindowsGroupName, windows.Name);
        Assert.IsTrue(windows.Components.Any(c => c.Name == "System"));
        Assert.IsTrue(windows.Components.Any(c => c.Name == "svchost.exe"));
    }
}
```

- [ ] **Step 2：執行測試，確認失敗**

執行：`(cd app && dotnet test)`

預期：建置失敗，錯誤包含

```text
error CS0246: 找不到類型或命名空間名稱 'GroupedSnapshot' (是否遺漏了 using 指示詞或組件參考?)
error CS0246: 找不到類型或命名空間名稱 'AppGroup' (是否遺漏了 using 指示詞或組件參考?)
```

- [ ] **Step 3：實作**

`app/MemSweep.Core/GroupedSnapshot.cs`：

```csharp
namespace MemSweep.Core;

/// <summary>應用程式明細的一列：同一應用程式內、同一執行檔（node 再依標籤區分）的行程合計。</summary>
public sealed record AppComponent(string Name, int ProcessCount, long CommitBytes, long WorkingSetBytes);

/// <summary>一個應用程式，即畫面與報告中的一列。</summary>
public sealed record AppGroup(
    string Name,
    bool IsWindows,
    int ProcessCount,
    long CommitBytes,
    long WorkingSetBytes,
    IReadOnlyList<AppComponent> Components);

/// <summary>歸組後的快照；Apps 依 commit 由大到小排列。</summary>
public sealed record GroupedSnapshot(
    SystemSnapshot Snapshot,
    IReadOnlyList<AppGroup> Apps,
    long UnattributedBytes)
{
    public const string UnattributedName = "系統（不屬於任何程式）";
}
```

`app/MemSweep.Core/AppGrouper.cs`：

```csharp
namespace MemSweep.Core;

/// <summary>依 SPEC 第 8 章，把快照中的行程歸組成應用程式。</summary>
public sealed class AppGrouper
{
    public const string WindowsGroupName = "Windows（系統與服務）";

    // 往上追到這些行程就停止；沒有路徑時也靠這份名單判斷是否屬於 Windows（R-1、R-6）
    private static readonly HashSet<string> ShellAndSystemNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Idle", "System", "Secure System", "Registry", "Memory Compression",
        "smss.exe", "csrss.exe", "wininit.exe", "winlogon.exe", "services.exe", "svchost.exe",
        "userinit.exe", "explorer.exe", "sihost.exe",
    };

    // 輔助行程一律歸屬啟動它的應用程式（R-2）
    private static readonly HashSet<string> HelperNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd.exe", "conhost.exe", "OpenConsole.exe", "pwsh.exe", "powershell.exe",
        "node.exe", "bun.exe", "python.exe", "pythonw.exe", "uv.exe", "uvx.exe", "msedgewebview2.exe",
    };

    private readonly string _windowsDirectoryPrefix;

    public AppGrouper()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.Windows))
    {
    }

    public AppGrouper(string windowsDirectory)
    {
        _windowsDirectoryPrefix = windowsDirectory.TrimEnd('\\') + "\\";
    }

    public GroupedSnapshot Group(SystemSnapshot snapshot, IReadOnlyDictionary<int, ProcessInfo> infos)
    {
        var byPid = new Dictionary<int, ProcessSample>(snapshot.Processes.Count);
        foreach (var process in snapshot.Processes)
        {
            byPid[process.Pid] = process;
        }

        var topOf = new Dictionary<int, int>(snapshot.Processes.Count);
        var builders = new Dictionary<string, GroupBuilder>(StringComparer.OrdinalIgnoreCase);
        long processCommit = 0;
        foreach (var process in snapshot.Processes)
        {
            var top = byPid[FindTop(process, byPid, infos, topOf)];
            var topInfo = InfoOf(infos, top.Pid);
            bool isWindows = IsWindowsTop(top, topInfo);
            // R-5：路徑相同的最上層行程合併成一列
            string key = isWindows ? "\0windows" : topInfo.ExecutablePath ?? "\0name:" + top.ImageName;
            if (!builders.TryGetValue(key, out var builder))
            {
                string name = isWindows ? WindowsGroupName : topInfo.FileDescription ?? top.ImageName;
                builder = new GroupBuilder(name, isWindows);
                builders[key] = builder;
            }
            builder.Add(process, InfoOf(infos, process.Pid));
            processCommit += process.PrivateCommitBytes;
        }

        var apps = builders.Values
            .Select(b => b.Build())
            .OrderByDescending(a => a.CommitBytes)
            .ThenBy(a => a.Name, StringComparer.Ordinal)
            .ToList();
        return new GroupedSnapshot(snapshot, apps, Math.Max(0, snapshot.CommitUsedBytes - processCommit));
    }

    /// <summary>公司名稱的第一個單字（連續的字母或數字），轉小寫；取不到時回傳 null。</summary>
    internal static string? CompanyKey(string? companyName)
    {
        if (companyName is null)
        {
            return null;
        }
        var text = companyName.AsSpan().Trim();
        int length = 0;
        while (length < text.Length && char.IsLetterOrDigit(text[length]))
        {
            length++;
        }
        return length == 0 ? null : text[..length].ToString().ToLowerInvariant();
    }

    private static int FindTop(
        ProcessSample start,
        Dictionary<int, ProcessSample> byPid,
        IReadOnlyDictionary<int, ProcessInfo> infos,
        Dictionary<int, int> topOf)
    {
        var chain = new List<int>();
        var current = start;
        int top;
        while (true)
        {
            if (topOf.TryGetValue(current.Pid, out top))
            {
                break;
            }
            chain.Add(current.Pid);
            if (!TryGetParent(current, byPid, out var parent)
                || ShellAndSystemNames.Contains(parent.ImageName)
                || IsIndependent(current, InfoOf(infos, current.Pid), InfoOf(infos, parent.Pid))
                || chain.Contains(parent.Pid))
            {
                top = current.Pid;
                break;
            }
            current = parent;
        }
        foreach (int pid in chain)
        {
            topOf[pid] = top;
        }
        return top;
    }

    // R-3：父行程的建立時間晚於子行程，代表 PID 已被重複使用，視為父行程不存在
    private static bool TryGetParent(ProcessSample child, Dictionary<int, ProcessSample> byPid, out ProcessSample parent)
    {
        if (child.ParentPid != child.Pid
            && byPid.TryGetValue(child.ParentPid, out var candidate)
            && candidate.CreateTimeUtc <= child.CreateTimeUtc)
        {
            parent = candidate;
            return true;
        }
        parent = null!;
        return false;
    }

    // R-1 第三點：不是輔助行程，且公司名稱第一個單字和父行程不同，或任一方取不到公司名稱
    private static bool IsIndependent(ProcessSample child, ProcessInfo childInfo, ProcessInfo parentInfo)
    {
        if (HelperNames.Contains(child.ImageName))
        {
            return false;
        }
        string? childKey = CompanyKey(childInfo.CompanyName);
        string? parentKey = CompanyKey(parentInfo.CompanyName);
        return childKey is null || parentKey is null || childKey != parentKey;
    }

    // R-6：執行檔在 Windows 資料夾下，或取不到路徑而名稱在殼層與系統名單中
    private bool IsWindowsTop(ProcessSample top, ProcessInfo info) =>
        info.ExecutablePath is { } path
            ? path.StartsWith(_windowsDirectoryPrefix, StringComparison.OrdinalIgnoreCase)
            : ShellAndSystemNames.Contains(top.ImageName);

    private static ProcessInfo InfoOf(IReadOnlyDictionary<int, ProcessInfo> infos, int pid) =>
        infos.TryGetValue(pid, out var info) ? info : ProcessInfo.Unknown;

    private sealed class GroupBuilder(string name, bool isWindows)
    {
        private readonly Dictionary<string, (string Name, int Count, long Commit, long WorkingSet)> _components =
            new(StringComparer.OrdinalIgnoreCase);
        private int _count;
        private long _commit;
        private long _workingSet;

        public void Add(ProcessSample process, ProcessInfo info)
        {
            _count++;
            _commit += process.PrivateCommitBytes;
            _workingSet += process.WorkingSetBytes;

            // 明細依執行檔名稱合併；node 再依套件標籤區分（F-23）
            string componentName = info.PackageLabel is { } label
                && string.Equals(process.ImageName, "node.exe", StringComparison.OrdinalIgnoreCase)
                    ? "node：" + label
                    : process.ImageName;
            _components.TryGetValue(componentName, out var c);
            _components[componentName] = (
                c.Name ?? componentName,
                c.Count + 1,
                c.Commit + process.PrivateCommitBytes,
                c.WorkingSet + process.WorkingSetBytes);
        }

        public AppGroup Build() => new(
            name,
            isWindows,
            _count,
            _commit,
            _workingSet,
            _components.Values
                .Select(c => new AppComponent(c.Name, c.Count, c.Commit, c.WorkingSet))
                .OrderByDescending(c => c.CommitBytes)
                .ThenBy(c => c.Name, StringComparer.Ordinal)
                .ToList());
    }
}
```

`app/MemSweep.Core/MemorySampler.cs`：

```csharp
namespace MemSweep.Core;

/// <summary>取樣並歸組：SystemReader → ProcessInfoCache → AppGrouper。</summary>
public sealed class MemorySampler
{
    private readonly ISystemReader _reader;
    private readonly ProcessInfoCache _cache;
    private readonly AppGrouper _grouper;

    public MemorySampler(ISystemReader reader, IProcessInfoProvider provider, AppGrouper grouper)
    {
        _reader = reader;
        _cache = new ProcessInfoCache(provider);
        _grouper = grouper;
    }

    public static MemorySampler CreateDefault() =>
        new(new SystemReader(), new Win32ProcessInfoProvider(), new AppGrouper());

    public GroupedSnapshot Sample()
    {
        var snapshot = _reader.Read();
        return _grouper.Group(snapshot, _cache.Resolve(snapshot.Processes));
    }
}
```

- [ ] **Step 4：執行測試，確認通過**

執行：`(cd app && dotnet test)`

預期：總計 38，失敗 0。

- [ ] **Step 5：Commit**

```bash
git add app/MemSweep.Core app/MemSweep.Core.Tests
git status --short
git commit -F - <<'EOF'
新增應用程式歸組

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

- [ ] **Step 6：推送**（依「共同執行規則」先 fetch 並檢查）

---

### Task 5：文字與 JSON 報告、剩餘 commit 檢查

**檔案：**

- 新增：`app/MemSweep.Core/HeadroomCheck.cs`、`ReportFormatter.cs`
- 新增：`app/MemSweep.Core.Tests/ReportSamples.cs`、`ReportFormatterTests.cs`、`HeadroomCheckTests.cs`

**實作的條文：**

> - F-40 `memsweep-cli status`：輸出純文字摘要，包含 commit 已用 / 上限、實體 RAM、開機以來尖峰、前五名應用程式與「系統（不屬於任何程式）」。〔指令形式：Claude 決定〕
> - F-41 `memsweep-cli status --json`：以 JSON 輸出相同內容，記憶體數值以 bytes 整數表示。〔Claude 決定〕
> - 剩餘 commit 大於或等於指定的 GB：結束碼 0。
> - 剩餘 commit 不足：結束碼 1，並輸出剩餘 commit 與前五名應用程式。
> - JSON 內容與文字摘要相同（前五名，不是全部應用程式），記憶體以 bytes 整數表示：
> - 輸出使用 UTF-8。

**介面：**

- 使用：Task 4 的 `GroupedSnapshot`、`AppGroup`、`AppGrouper.WindowsGroupName`；Task 2 的 `SystemSnapshot`；Task 1 的 `MemoryMath`。
- 產生：
  - `record HeadroomResult(bool IsEnough, long FreeBytes, long NeedBytes)`
  - `static class HeadroomCheck { HeadroomResult Evaluate(SystemSnapshot snapshot, double needGigabytes); }`
  - `static class ReportFormatter`：`const int TopCount = 5`、`string ToText(GroupedSnapshot)`、`string ToCheckText(GroupedSnapshot, HeadroomResult)`、`string ToJson(GroupedSnapshot)`

- [ ] **Step 1：寫失敗的測試**

`app/MemSweep.Core.Tests/ReportSamples.cs`：

```csharp
using MemSweep.Core;

namespace MemSweep.Core.Tests;

/// <summary>報告與命令列測試共用的歸組快照，數字取自 2026-10-26 的實測。</summary>
internal static class ReportSamples
{
    public static readonly DateTimeOffset Timestamp = new(2026, 10, 26, 14, 35, 2, 789, TimeSpan.FromHours(8));

    private static long Gb(double value) => MemoryMath.FromGigabytes(value);

    private static AppGroup App(string name, int count, double commitGb, bool isWindows = false) =>
        new(name, isWindows, count, Gb(commitGb), Gb(commitGb) / 2, Array.Empty<AppComponent>());

    /// <summary>commit 31.4 / 39.4 GB、RAM 20.5 / 31.4 GB、尖峰 36.8 GB，六個應用程式。</summary>
    public static GroupedSnapshot Typical()
    {
        var snapshot = new SystemSnapshot(
            Timestamp,
            new DateTime(2026, 10, 26, 0, 30, 0, DateTimeKind.Utc),
            Gb(31.4),
            Gb(39.4),
            Gb(36.8),
            Gb(31.4),
            Gb(10.9),
            Gb(8),
            Array.Empty<ProcessSample>());
        var apps = new[]
        {
            App("Claude", 36, 3.2),
            App("ChatGPT", 13, 2.2),
            App("Microsoft Edge WebView2", 19, 1.8),
            App(AppGrouper.WindowsGroupName, 140, 1.5, isWindows: true),
            App("Node.js JavaScript Runtime", 30, 1.4),
            App("Microsoft Edge", 16, 1.3),
        };
        return new GroupedSnapshot(snapshot, apps, Gb(6.9));
    }

    /// <summary>只有兩個應用程式的快照。</summary>
    public static GroupedSnapshot TwoApps()
    {
        var typical = Typical();
        return typical with { Apps = typical.Apps.Take(2).ToList() };
    }
}
```

`app/MemSweep.Core.Tests/ReportFormatterTests.cs`：

```csharp
using System.Text.Json;
using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class ReportFormatterTests
{
    [TestMethod]
    public void TextSummaryListsTopFiveAndUnattributed()
    {
        string expected =
            "MemSweep  2026-10-26 14:35:02\n" +
            "Commit  31.4 / 39.4 GB（80%），剩 8.0 GB\n" +
            "RAM     20.5 / 31.4 GB（65%）\n" +
            "尖峰    36.8 GB（開機以來）\n" +
            "前五名\n" +
            "  Claude  36 個行程  3.2 GB\n" +
            "  ChatGPT  13 個行程  2.2 GB\n" +
            "  Microsoft Edge WebView2  19 個行程  1.8 GB\n" +
            "  Windows（系統與服務）  140 個行程  1.5 GB\n" +
            "  Node.js JavaScript Runtime  30 個行程  1.4 GB\n" +
            "系統（不屬於任何程式）  6.9 GB\n";

        Assert.AreEqual(expected, ReportFormatter.ToText(ReportSamples.Typical()));
    }

    [TestMethod]
    public void TextSummaryWithFewerThanFiveApps()
    {
        string text = ReportFormatter.ToText(ReportSamples.TwoApps());

        StringAssert.EndsWith(
            text,
            "前五名\n  Claude  36 個行程  3.2 GB\n  ChatGPT  13 個行程  2.2 GB\n系統（不屬於任何程式）  6.9 GB\n");
    }

    [TestMethod]
    public void JsonHasTheSameContentInBytes()
    {
        var grouped = ReportSamples.Typical();
        string json = ReportFormatter.ToJson(grouped);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.AreEqual("2026-10-26T14:35:02+08:00", root.GetProperty("timestamp").GetString());
        var commit = root.GetProperty("commit");
        Assert.AreEqual(grouped.Snapshot.CommitUsedBytes, commit.GetProperty("usedBytes").GetInt64());
        Assert.AreEqual(grouped.Snapshot.CommitLimitBytes, commit.GetProperty("limitBytes").GetInt64());
        Assert.AreEqual(grouped.Snapshot.CommitFreeBytes, commit.GetProperty("freeBytes").GetInt64());
        Assert.AreEqual(grouped.Snapshot.CommitPeakBytes, commit.GetProperty("peakBytes").GetInt64());
        var physical = root.GetProperty("physical");
        Assert.AreEqual(grouped.Snapshot.PhysicalUsedBytes, physical.GetProperty("usedBytes").GetInt64());
        Assert.AreEqual(grouped.Snapshot.PhysicalTotalBytes, physical.GetProperty("totalBytes").GetInt64());
        var top = root.GetProperty("topApps").EnumerateArray().ToList();
        Assert.HasCount(5, top);
        Assert.AreEqual("Claude", top[0].GetProperty("name").GetString());
        Assert.AreEqual(36, top[0].GetProperty("processCount").GetInt32());
        Assert.AreEqual(grouped.Apps[4].CommitBytes, top[4].GetProperty("commitBytes").GetInt64());
        Assert.AreEqual(grouped.UnattributedBytes, root.GetProperty("unattributedBytes").GetInt64());
    }

    [TestMethod]
    public void JsonKeepsChineseReadableAndUsesLineFeeds()
    {
        string json = ReportFormatter.ToJson(ReportSamples.Typical());

        StringAssert.Contains(json, "\"name\": \"Windows（系統與服務）\"");
        Assert.DoesNotContain("\r", json);
        StringAssert.EndsWith(json, "}\n");
    }

    [TestMethod]
    public void CheckTextWhenEnough()
    {
        var grouped = ReportSamples.Typical();
        var result = HeadroomCheck.Evaluate(grouped.Snapshot, 5);

        Assert.AreEqual("足夠：剩餘 commit 8.0 GB，需要 5.0 GB\n", ReportFormatter.ToCheckText(grouped, result));
    }

    [TestMethod]
    public void CheckTextWhenNotEnoughListsTopApps()
    {
        var grouped = ReportSamples.Typical();
        var result = HeadroomCheck.Evaluate(grouped.Snapshot, 10);

        string text = ReportFormatter.ToCheckText(grouped, result);

        StringAssert.StartsWith(text, "不足：剩餘 commit 8.0 GB，需要 10.0 GB\n前五名\n  Claude  36 個行程  3.2 GB\n");
        StringAssert.EndsWith(text, "系統（不屬於任何程式）  6.9 GB\n");
    }
}
```

`app/MemSweep.Core.Tests/HeadroomCheckTests.cs`：

```csharp
using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class HeadroomCheckTests
{
    [TestMethod]
    public void FreeEqualToNeedIsEnough()
    {
        var snapshot = ReportSamples.Typical().Snapshot;

        var result = HeadroomCheck.Evaluate(snapshot, 8);

        Assert.IsTrue(result.IsEnough);
        Assert.AreEqual(snapshot.CommitFreeBytes, result.FreeBytes);
        Assert.AreEqual(8L * MemoryMath.BytesPerGigabyte, result.NeedBytes);
    }

    [TestMethod]
    public void FreeBelowNeedIsNotEnough()
    {
        var result = HeadroomCheck.Evaluate(ReportSamples.Typical().Snapshot, 8.1);

        Assert.IsFalse(result.IsEnough);
        Assert.AreEqual(MemoryMath.FromGigabytes(8.1), result.NeedBytes);
    }
}
```

- [ ] **Step 2：執行測試，確認失敗**

執行：`(cd app && dotnet test)`

預期：建置失敗，錯誤包含

```text
error CS0103: 名稱 'ReportFormatter' 不存在於目前的內容中
error CS0103: 名稱 'HeadroomCheck' 不存在於目前的內容中
```

- [ ] **Step 3：實作**

`app/MemSweep.Core/HeadroomCheck.cs`：

```csharp
namespace MemSweep.Core;

/// <summary>剩餘 commit 檢查的結果。</summary>
public sealed record HeadroomResult(bool IsEnough, long FreeBytes, long NeedBytes);

/// <summary>判斷剩餘 commit 是否足夠（F-42）。</summary>
public static class HeadroomCheck
{
    /// <summary>剩餘 commit 大於或等於需要的量即為足夠。</summary>
    public static HeadroomResult Evaluate(SystemSnapshot snapshot, double needGigabytes)
    {
        long need = MemoryMath.FromGigabytes(needGigabytes);
        return new HeadroomResult(snapshot.CommitFreeBytes >= need, snapshot.CommitFreeBytes, need);
    }
}
```

`app/MemSweep.Core/ReportFormatter.cs`：

```csharp
using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MemSweep.Core;

/// <summary>文字摘要與 JSON（F-40、F-41、F-42）；換行一律是 \n。</summary>
public static class ReportFormatter
{
    public const int TopCount = 5;

    public static string ToText(GroupedSnapshot grouped)
    {
        var s = grouped.Snapshot;
        var text = new StringBuilder();
        text.Append("MemSweep  ")
            .Append(s.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
            .Append('\n');
        text.Append("Commit  ").Append(Gb(s.CommitUsedBytes)).Append(" / ").Append(Gb(s.CommitLimitBytes))
            .Append(" GB（").Append(MemoryMath.Percent(s.CommitUsedBytes, s.CommitLimitBytes))
            .Append("%），剩 ").Append(Gb(s.CommitFreeBytes)).Append(" GB\n");
        text.Append("RAM     ").Append(Gb(s.PhysicalUsedBytes)).Append(" / ").Append(Gb(s.PhysicalTotalBytes))
            .Append(" GB（").Append(MemoryMath.Percent(s.PhysicalUsedBytes, s.PhysicalTotalBytes)).Append("%）\n");
        text.Append("尖峰    ").Append(Gb(s.CommitPeakBytes)).Append(" GB（開機以來）\n");
        AppendTopApps(text, grouped);
        return text.ToString();
    }

    public static string ToCheckText(GroupedSnapshot grouped, HeadroomResult result)
    {
        var text = new StringBuilder();
        text.Append(result.IsEnough ? "足夠" : "不足")
            .Append("：剩餘 commit ").Append(Gb(result.FreeBytes))
            .Append(" GB，需要 ").Append(Gb(result.NeedBytes)).Append(" GB\n");
        if (!result.IsEnough)
        {
            AppendTopApps(text, grouped);
        }
        return text.ToString();
    }

    public static string ToJson(GroupedSnapshot grouped)
    {
        var s = grouped.Snapshot;
        var buffer = new ArrayBufferWriter<byte>();
        var options = new JsonWriterOptions
        {
            Indented = true,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        using (var json = new Utf8JsonWriter(buffer, options))
        {
            json.WriteStartObject();
            json.WriteString("timestamp", TruncateToSeconds(s.Timestamp));
            json.WriteStartObject("commit");
            json.WriteNumber("usedBytes", s.CommitUsedBytes);
            json.WriteNumber("limitBytes", s.CommitLimitBytes);
            json.WriteNumber("freeBytes", s.CommitFreeBytes);
            json.WriteNumber("peakBytes", s.CommitPeakBytes);
            json.WriteEndObject();
            json.WriteStartObject("physical");
            json.WriteNumber("usedBytes", s.PhysicalUsedBytes);
            json.WriteNumber("totalBytes", s.PhysicalTotalBytes);
            json.WriteEndObject();
            json.WriteStartArray("topApps");
            foreach (var app in grouped.Apps.Take(TopCount))
            {
                json.WriteStartObject();
                json.WriteString("name", app.Name);
                json.WriteNumber("processCount", app.ProcessCount);
                json.WriteNumber("commitBytes", app.CommitBytes);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteNumber("unattributedBytes", grouped.UnattributedBytes);
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n";
    }

    private static void AppendTopApps(StringBuilder text, GroupedSnapshot grouped)
    {
        text.Append("前五名\n");
        foreach (var app in grouped.Apps.Take(TopCount))
        {
            text.Append("  ").Append(app.Name).Append("  ").Append(app.ProcessCount)
                .Append(" 個行程  ").Append(Gb(app.CommitBytes)).Append(" GB\n");
        }
        text.Append(GroupedSnapshot.UnattributedName).Append("  ").Append(Gb(grouped.UnattributedBytes)).Append(" GB\n");
    }

    private static string Gb(long bytes) => MemoryMath.FormatGigabytes(bytes);

    private static DateTimeOffset TruncateToSeconds(DateTimeOffset time) =>
        new(time.Ticks - time.Ticks % TimeSpan.TicksPerSecond, time.Offset);
}
```

- [ ] **Step 4：執行測試，確認通過**

執行：`(cd app && dotnet test)`

預期：總計 46，失敗 0。

- [ ] **Step 5：Commit**

```bash
git add app/MemSweep.Core app/MemSweep.Core.Tests
git status --short
git commit -F - <<'EOF'
新增文字與 JSON 報告及剩餘 commit 檢查

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

- [ ] **Step 6：推送**（依「共同執行規則」先 fetch 並檢查）

---

### Task 6：memsweep-cli 命令列

**檔案：**

- 新增：`app/MemSweep.Cli/MemSweep.Cli.csproj`、`CliApp.cs`、`Program.cs`
- 修改（整份換成）：`app/MemSweep.slnx`、`app/MemSweep.Core.Tests/MemSweep.Core.Tests.csproj`
- 新增：`app/MemSweep.Core.Tests/CliAppTests.cs`、`CliProcessLiveTests.cs`

**實作的條文：**

> - F-42 `memsweep-cli check --need <GB>`：〔Claude 決定〕
> - 參數錯誤：結束碼 2。
> - F-43 命令列不依賴 MemSweep.exe 是否常駐，直接向系統查詢，全程唯讀，也不讀寫尖峰紀錄檔。
> - A-04 `check --need` 的結束碼符合 F-42 的三種情況。
>
> | 指令 | 行為 |
> | --- | --- |
> | 無參數、`--help` | 用法說明，結束碼 0 |
> | 參數錯誤 | 錯誤訊息與用法，結束碼 2 |
>
> - `<GB>` 可帶小數，必須大於 0。全工具的 1 GB 都是 1024³ bytes，與工作管理員一致。

**介面：**

- 使用：Task 4 的 `MemorySampler`、`GroupedSnapshot`；Task 5 的 `ReportFormatter`、`HeadroomCheck`。
- 產生：`static class CliApp`（命名空間 `MemSweep.Cli`）：`ExitOk = 0`、`ExitNotEnough = 1`、`ExitUsageError = 2`、`static readonly string Usage`、`int Run(string[] args, Func<GroupedSnapshot> sample, TextWriter output, TextWriter error)`；組件名稱 `memsweep-cli`。

- [ ] **Step 1：寫失敗的測試**

`app/MemSweep.Core.Tests/MemSweep.Core.Tests.csproj` 整份換成：

```xml
<Project Sdk="MSTest.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\MemSweep.Core\MemSweep.Core.csproj" />
    <ProjectReference Include="..\MemSweep.Cli\MemSweep.Cli.csproj" />
  </ItemGroup>

</Project>
```

`app/MemSweep.Core.Tests/CliAppTests.cs`：

```csharp
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

`app/MemSweep.Core.Tests/CliProcessLiveTests.cs`：

```csharp
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace MemSweep.Core.Tests;

[TestClass]
[TestCategory("Live")]
public sealed class CliProcessLiveTests
{
    private static (int ExitCode, string Output) RunCli(params string[] args)
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "memsweep-cli.exe"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
        };
        foreach (string arg in args)
        {
            start.ArgumentList.Add(arg);
        }
        using var process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    [TestMethod]
    public void StatusJsonFromTheRealExecutable()
    {
        var (exitCode, output) = RunCli("status", "--json");

        Assert.AreEqual(0, exitCode);
        using var doc = JsonDocument.Parse(output);
        Assert.IsGreaterThan(0L, doc.RootElement.GetProperty("commit").GetProperty("usedBytes").GetInt64());
        Assert.HasCount(5, doc.RootElement.GetProperty("topApps").EnumerateArray().ToList());
    }

    [TestMethod]
    public void StatusTextIsUtf8()
    {
        var (exitCode, output) = RunCli("status");

        Assert.AreEqual(0, exitCode);
        StringAssert.Contains(output, "前五名\n");
        StringAssert.Contains(output, "系統（不屬於任何程式）");
    }

    [TestMethod]
    public void CheckWithHugeNeedIsNotEnough()
    {
        var (exitCode, output) = RunCli("check", "--need", "100000");

        Assert.AreEqual(1, exitCode);
        StringAssert.StartsWith(output, "不足：");
    }
}
```

- [ ] **Step 2：執行測試，確認失敗**

執行：`(cd app && dotnet test)`

預期：建置失敗（`MemSweep.Cli` 專案還不存在），錯誤包含

```text
error CS0234: 命名空間 'MemSweep' 中沒有類型或命名空間名稱 'Cli' (是否遺漏了組件參考?)
error CS0103: 名稱 'CliApp' 不存在於目前的內容中
```

- [ ] **Step 3：實作**

`app/MemSweep.Cli/MemSweep.Cli.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <AssemblyName>memsweep-cli</AssemblyName>
    <RootNamespace>MemSweep.Cli</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\MemSweep.Core\MemSweep.Core.csproj" />
  </ItemGroup>

</Project>
```

`app/MemSweep.Cli/CliApp.cs`：

```csharp
using System.Globalization;
using MemSweep.Core;

namespace MemSweep.Cli;

/// <summary>memsweep-cli 的參數解析與執行（F-40～F-43）。只在需要時取樣，且不讀寫 data\。</summary>
public static class CliApp
{
    public const int ExitOk = 0;
    public const int ExitNotEnough = 1;
    public const int ExitUsageError = 2;

    public static readonly string Usage =
        "用法：\n" +
        "  memsweep-cli status             顯示 commit、RAM、尖峰與前五名\n" +
        "  memsweep-cli status --json      以 JSON 輸出相同內容\n" +
        "  memsweep-cli check --need <GB>  剩餘 commit 足夠時結束碼 0，不足時 1\n";

    public static int Run(string[] args, Func<GroupedSnapshot> sample, TextWriter output, TextWriter error)
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

`app/MemSweep.Cli/Program.cs`：

```csharp
using System.Text;
using MemSweep.Cli;
using MemSweep.Core;

Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
return CliApp.Run(args, () => MemorySampler.CreateDefault().Sample(), Console.Out, Console.Error);
```

`app/MemSweep.slnx` 整份換成：

```xml
<Solution>
  <Project Path="MemSweep.Cli/MemSweep.Cli.csproj" />
  <Project Path="MemSweep.Core.Tests/MemSweep.Core.Tests.csproj" />
  <Project Path="MemSweep.Core/MemSweep.Core.csproj" />
</Solution>
```

- [ ] **Step 4：執行測試，確認通過**

執行：`(cd app && dotnet test)`

預期：總計 70，失敗 0。

- [ ] **Step 5：Commit**

```bash
git add app/MemSweep.slnx app/MemSweep.Cli app/MemSweep.Core.Tests
git status --short
git commit -F - <<'EOF'
新增 memsweep-cli 命令列

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

- [ ] **Step 6：推送**（依「共同執行規則」先 fetch 並檢查）

---

### Task 7：禁止改變記憶體狀態的 Win32 呼叫檢查

**檔案：**

- 新增：`app/MemSweep.Core.Tests/NativeSafetyTests.cs`

**實作的條文：**

> - A-07 程式碼中沒有任何改變系統記憶體狀態或提升權限的呼叫，例如 `EmptyWorkingSet`、`SetProcessWorkingSetSize`、`NtSetSystemInformation`、`AdjustTokenPrivileges`。
> - **A-07 自動檢查**：以反射列出 Core、Desktop、Cli 組件中所有 P/Invoke 進入點，確認沒有 `EmptyWorkingSet`、`SetProcessWorkingSetSize`、`SetProcessWorkingSetSizeEx`、`NtSetSystemInformation`、`SetSystemFileCacheSize`、`AdjustTokenPrivileges`。

**介面：**

- 使用：Task 2 的 `SystemReader`（取 Core 組件）、Task 6 的 `CliApp`（取 Cli 組件）。Desktop 組件在 Phase 02 才出現，屆時要加進 `ShippedAssemblies`。

- [ ] **Step 1：寫測試**

`app/MemSweep.Core.Tests/NativeSafetyTests.cs`：

```csharp
using System.Reflection;
using System.Runtime.InteropServices;
using MemSweep.Cli;
using MemSweep.Core;

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
    ];

    // Phase 02 加入 MemSweep.Desktop 後，也要把它的組件加進這份清單
    private static readonly Assembly[] ShippedAssemblies =
    [
        typeof(SystemReader).Assembly,
        typeof(CliApp).Assembly,
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
    public void DeclaresNoApiThatChangesMemoryStateOrPrivileges()
    {
        var found = EntryPoints()
            .Where(e => ForbiddenEntryPoints.Contains(e, StringComparer.OrdinalIgnoreCase))
            .ToList();

        Assert.IsEmpty(found);
    }
}
```

- [ ] **Step 2：執行測試，確認通過**

這是守門測試，現有程式碼本來就符合，所以會直接通過。原型驗證過：在 `Kernel32.cs` 宣告 `EmptyWorkingSet` 時，`DeclaresNoApiThatChangesMemoryStateOrPrivileges` 會失敗。不要為了驗證而在專案裡加上這種宣告。

執行：`(cd app && dotnet test)`

預期：總計 72，失敗 0。

- [ ] **Step 3：Commit**

```bash
git add app/MemSweep.Core.Tests/NativeSafetyTests.cs
git status --short
git commit -F - <<'EOF'
新增禁止改變記憶體狀態的 Win32 呼叫檢查

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

- [ ] **Step 4：推送**（依「共同執行規則」先 fetch 並檢查）

---

## Phase 收尾

- [ ] **完整檢查**：`(cd app && dotnet build -c Release)` 為 0 個警告、0 個錯誤；`(cd app && dotnet test)` 總計 72，失敗 0。
- [ ] **實機確認 A-09**：執行 `app/artifacts/bin/MemSweep.Cli/debug/memsweep-cli.exe status`，確認有「Claude」一列（包含工作階段），且沒有單獨的「Claude Code」列。
- [ ] **使用者驗收 A-01**：同時開著工作管理員「效能 > 記憶體」，對照 `status` 的 Commit 行與「已認可」，相差不超過 0.1 GB。
- [ ] **開 PR**：所有 Task 都推送後，用 `/repo-open-pr` 開 PR（base `main`，head `phase-01-core-cli`）。合併由使用者決定，使用 merge commit。
- [ ] **合併後更新規則檔**（文件直接進 `main`）：把 AGENTS.md「Technology」一節最後一行換成下面三行，AGENTS_cht.md「技術」一節同步改成對應的繁中內容，一起提交並推送到 `main`：

  ```markdown
  - Run every `dotnet` command inside `app/`, because `app/global.json` pins the SDK, the MSTest.Sdk version, and the test runner. For example: `(cd app && dotnet build)`.
  - Test: `(cd app && dotnet test)`. Unit tests only: `(cd app && dotnet test --filter "TestCategory!=Live")`. Live tests read the real system and are read-only.
  - Publish commands are added in Phase 04.
  ```
