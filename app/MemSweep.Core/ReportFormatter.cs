using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MemSweep.Core;

/// <summary>文字摘要與 JSON（F-40、F-41、F-42）；換行一律是 \n。</summary>
public static class ReportFormatter
{
    public const int TopCount = 5;

    public static string ToText(GroupedSnapshot grouped)
    {
        var s = grouped.Snapshot;
        var text = new StringBuilder();
        text.Append("MemSweep  ")
            .Append(s.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
            .Append('\n');
        text.Append("Commit  ").Append(Gb(s.CommitUsedBytes)).Append(" / ").Append(Gb(s.CommitLimitBytes))
            .Append(" GB（").Append(MemoryMath.Percent(s.CommitUsedBytes, s.CommitLimitBytes))
            .Append("%），剩 ").Append(Gb(s.CommitFreeBytes)).Append(" GB\n");
        text.Append("RAM     ").Append(Gb(s.PhysicalUsedBytes)).Append(" / ").Append(Gb(s.PhysicalTotalBytes))
            .Append(" GB（").Append(MemoryMath.Percent(s.PhysicalUsedBytes, s.PhysicalTotalBytes)).Append("%）\n");
        text.Append("尖峰    ").Append(Gb(s.CommitPeakBytes)).Append(" GB（開機以來）\n");
        AppendTopApps(text, grouped);
        return text.ToString();
    }

    public static string ToCheckText(GroupedSnapshot grouped, HeadroomResult result)
    {
        var text = new StringBuilder();
        text.Append(result.IsEnough ? "足夠" : "不足")
            .Append("：剩餘 commit ").Append(Gb(result.FreeBytes))
            .Append(" GB，需要 ").Append(Gb(result.NeedBytes)).Append(" GB\n");
        if (!result.IsEnough)
        {
            AppendTopApps(text, grouped);
        }
        return text.ToString();
    }

    public static string ToJson(GroupedSnapshot grouped)
    {
        var s = grouped.Snapshot;
        var buffer = new ArrayBufferWriter<byte>();
        var options = new JsonWriterOptions
        {
            Indented = true,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        using (var json = new Utf8JsonWriter(buffer, options))
        {
            json.WriteStartObject();
            json.WriteString("timestamp", TruncateToSeconds(s.Timestamp));
            json.WriteStartObject("commit");
            json.WriteNumber("usedBytes", s.CommitUsedBytes);
            json.WriteNumber("limitBytes", s.CommitLimitBytes);
            json.WriteNumber("freeBytes", s.CommitFreeBytes);
            json.WriteNumber("peakBytes", s.CommitPeakBytes);
            json.WriteEndObject();
            json.WriteStartObject("physical");
            json.WriteNumber("usedBytes", s.PhysicalUsedBytes);
            json.WriteNumber("totalBytes", s.PhysicalTotalBytes);
            json.WriteEndObject();
            json.WriteStartArray("topApps");
            foreach (var app in grouped.Apps.Take(TopCount))
            {
                json.WriteStartObject();
                json.WriteString("name", app.Name);
                json.WriteNumber("processCount", app.ProcessCount);
                json.WriteNumber("commitBytes", app.CommitBytes);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteNumber("unattributedBytes", grouped.UnattributedCommitBytes);
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n";
    }

    private static void AppendTopApps(StringBuilder text, GroupedSnapshot grouped)
    {
        text.Append("前五名\n");
        foreach (var app in grouped.Apps.Take(TopCount))
        {
            text.Append("  ").Append(app.Name).Append("  ").Append(app.ProcessCount)
                .Append(" 個行程  ").Append(Gb(app.CommitBytes)).Append(" GB\n");
        }
        text.Append(GroupedSnapshot.UnattributedName).Append("  ").Append(Gb(grouped.UnattributedCommitBytes)).Append(" GB\n");
    }

    private static string Gb(long bytes) => MemoryMath.FormatGigabytes(bytes);

    private static DateTimeOffset TruncateToSeconds(DateTimeOffset time) =>
        new(time.Ticks - time.Ticks % TimeSpan.TicksPerSecond, time.Offset);
}
