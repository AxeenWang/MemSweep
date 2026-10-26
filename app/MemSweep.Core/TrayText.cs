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
