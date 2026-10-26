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
