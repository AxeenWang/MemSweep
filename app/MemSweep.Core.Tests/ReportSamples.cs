using MemSweep.Core;

namespace MemSweep.Core.Tests;

/// <summary>報告與命令列測試共用的歸組快照，數字取自 2026-10-26 的實測。</summary>
internal static class ReportSamples
{
    public static readonly DateTimeOffset Timestamp = new(2026, 10, 26, 14, 35, 2, 789, TimeSpan.FromHours(8));

    private static long Gb(double value) => MemoryMath.FromGigabytes(value);

    private static AppGroup App(string name, int count, double commitGb, bool isWindows = false) =>
        new(name, isWindows, count, Gb(commitGb), Gb(commitGb) / 2, Array.Empty<AppComponent>());

    /// <summary>commit 31.4 / 39.4 GB、RAM 20.5 / 31.4 GB、尖峰 36.8 GB，六個應用程式。</summary>
    public static GroupedSnapshot Typical()
    {
        var snapshot = new SystemSnapshot(
            Timestamp,
            new DateTime(2026, 10, 26, 0, 30, 0, DateTimeKind.Utc),
            Gb(31.4),
            Gb(39.4),
            Gb(36.8),
            Gb(31.4),
            Gb(10.9),
            Gb(8),
            Array.Empty<ProcessSample>());
        var apps = new[]
        {
            App("Claude", 36, 3.2),
            App("ChatGPT", 13, 2.2),
            App("Microsoft Edge WebView2", 19, 1.8),
            App(AppGrouper.WindowsGroupName, 140, 1.5, isWindows: true),
            App("Node.js JavaScript Runtime", 30, 1.4),
            App("Microsoft Edge", 16, 1.3),
        };
        return new GroupedSnapshot(snapshot, apps, Gb(6.9));
    }

    /// <summary>只有兩個應用程式的快照。</summary>
    public static GroupedSnapshot TwoApps()
    {
        var typical = Typical();
        return typical with { Apps = typical.Apps.Take(2).ToList() };
    }
}
