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
        "cmd.exe", "conhost.exe", "OpenConsole.exe", "pwsh.exe", "powershell.exe", "bash.exe", "sh.exe",
        "node.exe", "node_repl.exe", "bun.exe", "python.exe", "pythonw.exe", "uv.exe", "uvx.exe", "msedgewebview2.exe",
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
                || IsOrphanHelper(parent, byPid)
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

    // R-1 第二點的解讀：輔助行程不是應用程式。輔助行程一路往上都是輔助行程，最後父行程不存在或是殼層，
    // 代表啟動它的應用程式已不存在，它的子孫各自成為最上層，不會跟著它併進 Windows 或殼層那一列
    private static bool IsOrphanHelper(ProcessSample process, Dictionary<int, ProcessSample> byPid)
    {
        var visited = new HashSet<int>();
        var current = process;
        while (HelperNames.Contains(current.ImageName) && visited.Add(current.Pid))
        {
            if (!TryGetParent(current, byPid, out var parent) || ShellAndSystemNames.Contains(parent.ImageName))
            {
                return true;
            }
            current = parent;
        }
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
