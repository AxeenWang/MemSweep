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
