namespace MemSweep.Core;

/// <summary>所有尖峰紀錄（F-24、F-33）；兩份清單都依加入的先後排列，與 CSV 的列順序相同。</summary>
public sealed record PeakHistory(IReadOnlyList<BootPeak> Boots, IReadOnlyList<DailyPeak> Days)
{
    public static PeakHistory Empty { get; } = new([], []);
}
