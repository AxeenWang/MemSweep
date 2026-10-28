using System.Globalization;

namespace MemSweep.Core;

/// <summary>「程式」分頁明細的一列：同一執行檔合併後的行程，例如「claude.exe ×14」（F-23）。</summary>
public sealed record ComponentRow(string Name, string RamText, string CommitText);

/// <summary>
/// 「程式」分頁的一列（F-23）。完整視窗開著時每 2 秒原地更新，
/// 展開狀態與畫面上的物件才不會每次都重新建立。
/// </summary>
public sealed class AppListRow : ObservableObject
{
    private string _name = string.Empty;
    private string _countText = string.Empty;
    private string _ramText = string.Empty;
    private string _commitText = string.Empty;
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

    public string RamText { get => _ramText; private set => Set(ref _ramText, value); }

    public string CommitText { get => _commitText; private set => Set(ref _commitText, value); }

    public IReadOnlyList<ComponentRow> Components { get => _components; private set => Set(ref _components, value); }

    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }

    internal void Update(AppGroup app)
    {
        Name = app.Name;
        CountText = app.ProcessCount.ToString(CultureInfo.InvariantCulture);
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
            .ToList();
        // 內容沒變就保留原本的清單，展開的明細才不會重建
        if (!components.SequenceEqual(Components))
        {
            Components = components;
        }
    }

    internal void UpdateUnattributed(long ramBytes, long commitBytes)
    {
        Name = GroupedSnapshot.UnattributedName;
        RamText = FullWindowModel.Gigabytes(ramBytes);
        CommitText = FullWindowModel.Gigabytes(commitBytes);
    }
}
