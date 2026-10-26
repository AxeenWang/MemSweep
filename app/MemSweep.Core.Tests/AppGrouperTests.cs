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
    public void HelperStartedFromTheShellDoesNotAbsorbItsChildren()
    {
        var grouped = new Fixture()
            .Add(10, 4, "explorer.exe", 90, @"C:\Windows\explorer.exe", "Microsoft Corporation", "Windows 檔案總管")
            .Add(600, 10, "pwsh.exe", 60, @"C:\Apps\PS\pwsh.exe", "Microsoft Corporation", "PowerShell 7")
            .Add(610, 600, "node.exe", 35, @"C:\Program Files\nodejs\node.exe", "Node.js", "Node.js JavaScript Runtime")
            .Group();

        Assert.AreEqual("pwsh.exe×1", Components(Row(grouped, "PowerShell 7")));
        Assert.AreEqual("node.exe×1", Components(Row(grouped, "Node.js JavaScript Runtime")));
    }

    [TestMethod]
    public void OrphanedCmdWrapperDoesNotHideNodeInTheWindowsRow()
    {
        // MCP 常以 cmd /c npx 啟動；啟動它的 Claude 或 Codex 結束後，留下 cmd.exe → node.exe
        var grouped = new Fixture()
            .Add(1200, 9999, "cmd.exe", 4, @"C:\Windows\System32\cmd.exe", "Microsoft Corporation", "Windows 命令處理程式")
            .Add(1210, 1200, "node.exe", 300, @"C:\Program Files\nodejs\node.exe", "Node.js", "Node.js JavaScript Runtime", "@playwright/mcp")
            .Add(1220, 9998, "cmd.exe", 5, @"C:\Windows\System32\cmd.exe", "Microsoft Corporation", "Windows 命令處理程式")
            .Add(1230, 1220, "node.exe", 200, @"C:\Program Files\nodejs\node.exe", "Node.js", "Node.js JavaScript Runtime", "@upstash/context7-mcp")
            .Group();

        var node = Row(grouped, "Node.js JavaScript Runtime");
        Assert.AreEqual(2, node.ProcessCount);
        Assert.AreEqual(500 * Mb, node.CommitBytes);
        Assert.AreEqual("cmd.exe×2", Components(Row(grouped, AppGrouper.WindowsGroupName)));
    }

    [TestMethod]
    public void GitBashAndCodexRuntimeAreHelpers()
    {
        var grouped = new Fixture()
            .Add(1300, 9999, "claude.exe", 650, @"C:\Users\me\claude-code\claude.exe", "Anthropic PBC", "Claude Code")
            .Add(1310, 1300, "bash.exe", 6, @"C:\Program Files\Git\usr\bin\bash.exe", "The Git Development Community", "GNU Bash")
            .Add(1320, 1310, "sh.exe", 5, @"C:\Program Files\Git\usr\bin\sh.exe", "The Git Development Community", "GNU sh")
            .Add(1400, 9999, "codex.exe", 100, @"C:\Users\me\Codex\bin\codex.exe")
            .Add(1410, 1400, "node_repl.exe", 70, @"C:\Users\me\Codex\runtimes\cua_node\bin\node_repl.exe")
            .Group();

        Assert.AreEqual("claude.exe×1, bash.exe×1, sh.exe×1", Components(Row(grouped, "Claude Code")));
        Assert.AreEqual("codex.exe×1, node_repl.exe×1", Components(Row(grouped, "codex.exe")));
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
