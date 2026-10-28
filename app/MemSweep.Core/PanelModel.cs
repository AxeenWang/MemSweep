using System.Globalization;

namespace MemSweep.Core;

/// <summary>量表：Level 為 null 表示不依門檻變色（commit）。</summary>
public sealed record GaugeModel(string Label, string ValueText, double Fraction, UsageLevel? Level);

/// <summary>小面板清單的一列；BarFraction 是相對於清單中最大一列的比例。</summary>
public sealed record PanelRow(string Name, string CountText, string GigabytesText, double BarFraction, bool IsUnattributed);

/// <summary>
/// 小面板要顯示的全部內容（F-11）：RAM 量表依門檻變色，commit 量表不變色；前五名與「系統」列都以 RAM 計。
/// Warning 是尖峰紀錄停用的原因，沒有問題時為 null（Design 3.9）。
/// </summary>
public sealed record PanelModel(GaugeModel Ram, GaugeModel Commit, string PeakText, IReadOnlyList<PanelRow> Rows, string? Warning)
{
    public static PanelModel Build(GroupedSnapshot grouped, BootPeak? peak, UsageThresholds thresholds, string? warning = null)
    {
        var s = grouped.Snapshot;
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
        var rows = top
            .Select(a => new PanelRow(
                a.Name,
                a.ProcessCount.ToString(CultureInfo.InvariantCulture),
                MemoryMath.FormatGigabytes(a.RamBytes) + " GB",
                Fraction(a.RamBytes, largest),
                false))
            .Append(new PanelRow(
                GroupedSnapshot.UnattributedName,
                string.Empty,
                MemoryMath.FormatGigabytes(grouped.UnattributedRamBytes) + " GB",
                Fraction(grouped.UnattributedRamBytes, largest),
                true))
            .ToList();

        return new PanelModel(ram, commit, DescribePeak(peak, s.Timestamp), rows, warning);
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
