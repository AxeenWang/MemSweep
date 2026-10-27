using System.Text.Json.Serialization;

namespace MemSweep.Core;

/// <summary>設定視窗可以調整的項目（F-50），存成 data\settings.json（Design 3.9）。</summary>
public sealed record AppSettings(int OrangeThreshold, int RedThreshold, bool AutoStart, int RetentionDays)
{
    public const int MinRetentionDays = 1;
    public const int MaxRetentionDays = 3650;

    /// <summary>橘色 70%、紅色 85%（F-03），開機自動啟動關閉（F-51），保留 90 天（F-33）。</summary>
    public static AppSettings Default { get; } =
        new(UsageThresholds.Default.OrangePercent, UsageThresholds.Default.RedPercent, false, 90);

    [JsonIgnore]
    public UsageThresholds Thresholds => new(OrangeThreshold, RedThreshold);

    /// <summary>1 ≤ 橘色 &lt; 紅色 ≤ 100；保留天數 1～3650（Design 3.6）。</summary>
    [JsonIgnore]
    public bool IsValid =>
        OrangeThreshold >= 1 && OrangeThreshold < RedThreshold && RedThreshold <= 100
        && RetentionDays is >= MinRetentionDays and <= MaxRetentionDays;
}
