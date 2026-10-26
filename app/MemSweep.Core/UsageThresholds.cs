namespace MemSweep.Core;

/// <summary>commit 使用率的顏色等級（F-03）。</summary>
public enum UsageLevel
{
    Green,
    Orange,
    Red,
}

/// <summary>橘色與紅色門檻（百分比）。Phase 04 之前一律使用預設值 70、85。</summary>
public sealed record UsageThresholds(int OrangePercent, int RedPercent)
{
    public static UsageThresholds Default { get; } = new(70, 85);

    /// <summary>低於橘色門檻為綠色；達到橘色門檻但低於紅色門檻為橘色；達到紅色門檻以上為紅色。</summary>
    public UsageLevel LevelOf(int percent) =>
        percent >= RedPercent ? UsageLevel.Red
        : percent >= OrangePercent ? UsageLevel.Orange
        : UsageLevel.Green;
}
