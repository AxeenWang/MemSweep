using System.Globalization;

namespace MemSweep.Core;

/// <summary>系統匣圖示上的數字與顏色、滑鼠停留時的提示文字（F-02、F-03、F-05）。圖示看 RAM（SPEC 1.4）。</summary>
public static class TrayText
{
    /// <summary>
    /// RAM 使用率，限制在 0～100。和 commit 一樣四捨五入；Windows 的 dwMemoryLoad 是無條件捨去，
    /// 兩者最多差 1，在 A-10 的容許範圍內，是刻意的。
    /// </summary>
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
