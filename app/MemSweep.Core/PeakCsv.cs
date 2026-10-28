using System.Globalization;
using System.Text;

namespace MemSweep.Core;

/// <summary>
/// 尖峰紀錄 CSV 的格式（F-34、Design 3.9）：含 BOM 的 UTF-8、CRLF 換行、中文欄名，
/// 記憶體以 GB 保留兩位小數，時間為本機時間 yyyy-MM-dd HH:mm:ss，未知時填「未知」。
/// 名稱開頭像 Excel 公式時前面加單引號，讀回時去掉。
/// </summary>
public static class PeakCsv
{
    public const string BootFileName = "peaks-boot.csv";
    public const string DailyFileName = "peaks-daily.csv";
    public const string Unknown = "未知";

    public const string BootHeader =
        "開機時間,尖峰時間,commit已用GB,commit上限GB,第1名,第2名,第3名,第4名,第5名,第1名GB,第2名GB,第3名GB,第4名GB,第5名GB";

    public const string DailyHeader =
        "日期,尖峰時間,commit已用GB,commit上限GB,第1名,第2名,第3名,第4名,第5名,第1名GB,第2名GB,第3名GB,第4名GB,第5名GB";

    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";
    private const string DateFormat = "yyyy-MM-dd";
    // 前五名的欄數與文字摘要的前五名相同
    private const int TopCount = ReportFormatter.TopCount;
    private const int ColumnCount = 4 + TopCount * 2;

    // 1 PB；更大的數值不會由 MemSweep 寫出，換算成 bytes 時也會溢位
    private const double MaxGigabytes = 1024 * 1024;

    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static byte[] WriteBoots(IEnumerable<BootPeak> boots, TimeZoneInfo zone) =>
        Encode(BootHeader, boots.Select(b => BootFields(b, zone)));

    public static byte[] WriteDays(IEnumerable<DailyPeak> days, TimeZoneInfo zone) =>
        Encode(DailyHeader, days.Select(d => DayFields(d, zone)));

    /// <summary>讀取 peaks-boot.csv；內容不符合格式時丟出 <see cref="FormatException"/>。</summary>
    public static List<BootPeak> ReadBoots(byte[] content, TimeZoneInfo zone) =>
        Decode(content, BootHeader).Select(fields => ParseBoot(fields, zone)).ToList();

    /// <summary>讀取 peaks-daily.csv；內容不符合格式時丟出 <see cref="FormatException"/>。</summary>
    public static List<DailyPeak> ReadDays(byte[] content, TimeZoneInfo zone) =>
        Decode(content, DailyHeader).Select(fields => ParseDay(fields, zone)).ToList();

    private static string[] BootFields(BootPeak boot, TimeZoneInfo zone)
    {
        string bootTime = TimeZoneInfo.ConvertTimeFromUtc(boot.BootTimeUtc, zone).ToString(TimeFormat, CultureInfo.InvariantCulture);
        string peakTime = boot.Time is { } time ? FormatTime(time, zone) : Unknown;
        return Row(bootTime, peakTime, boot.CommitBytes, boot.CommitLimitBytes, boot.TopApps);
    }

    private static string[] DayFields(DailyPeak day, TimeZoneInfo zone) =>
        Row(day.Date.ToString(DateFormat, CultureInfo.InvariantCulture), FormatTime(day.Time, zone), day.CommitBytes, day.CommitLimitBytes, day.TopApps);

    private static string[] Row(string first, string time, long commit, long limit, IReadOnlyList<PeakApp>? topApps)
    {
        var fields = new string[ColumnCount];
        Array.Fill(fields, string.Empty);
        fields[0] = first;
        fields[1] = time;
        fields[2] = Gigabytes(commit);
        fields[3] = Gigabytes(limit);
        if (topApps is null)
        {
            // 尖峰發生在 MemSweep 未執行期間，前五名無從得知（F-31）
            fields[4] = Unknown;
            return fields;
        }
        for (int i = 0; i < Math.Min(TopCount, topApps.Count); i++)
        {
            fields[4 + i] = GuardFormula(topApps[i].Name);
            fields[4 + TopCount + i] = Gigabytes(topApps[i].CommitBytes);
        }
        return fields;
    }

    private static BootPeak ParseBoot(string[] fields, TimeZoneInfo zone)
    {
        var bootUtc = TimeZoneInfo.ConvertTimeToUtc(ParseLocal(fields[0], zone), zone);
        if (fields[1] == Unknown)
        {
            return new BootPeak(bootUtc, ParseGigabytes(fields[2]), ParseGigabytes(fields[3]), null, null);
        }
        return new BootPeak(bootUtc, ParseGigabytes(fields[2]), ParseGigabytes(fields[3]), ParseTime(fields[1], zone), ParseTopApps(fields));
    }

    private static DailyPeak ParseDay(string[] fields, TimeZoneInfo zone)
    {
        if (!DateOnly.TryParseExact(fields[0], DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new FormatException("日期格式不符：「" + fields[0] + "」");
        }
        return new DailyPeak(date, ParseGigabytes(fields[2]), ParseGigabytes(fields[3]), ParseTime(fields[1], zone), ParseTopApps(fields));
    }

    private static List<PeakApp> ParseTopApps(string[] fields)
    {
        var apps = new List<PeakApp>();
        for (int i = 0; i < TopCount && fields[4 + i].Length > 0; i++)
        {
            apps.Add(new PeakApp(UnguardFormula(fields[4 + i]), ParseGigabytes(fields[4 + TopCount + i])));
        }
        return apps;
    }

    private static string FormatTime(DateTimeOffset time, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(time, zone).ToString(TimeFormat, CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTime(string text, TimeZoneInfo zone)
    {
        var local = ParseLocal(text, zone);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private static DateTime ParseLocal(string text, TimeZoneInfo zone)
    {
        // 日光節約時間跳過的時刻，與換算成 UTC 會超出範圍的時刻（例如台北時間 0001-01-01 凌晨），
        // 都不會由 MemSweep 寫出，出現時視為格式不符
        if (!DateTime.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            || zone.IsInvalidTime(value)
            || !IsWithinUtcRange(value, zone))
        {
            throw new FormatException("時間格式不符：「" + text + "」");
        }
        return value;
    }

    private static bool IsWithinUtcRange(DateTime local, TimeZoneInfo zone)
    {
        long utcTicks = local.Ticks - zone.GetUtcOffset(local).Ticks;
        return utcTicks >= DateTime.MinValue.Ticks && utcTicks <= DateTime.MaxValue.Ticks;
    }

    private static string Gigabytes(long bytes) =>
        MemoryMath.ToGigabytes(bytes).ToString("0.00", CultureInfo.InvariantCulture);

    private static long ParseGigabytes(string text) =>
        double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double value) && value <= MaxGigabytes
            ? MemoryMath.FromGigabytes(value)
            : throw new FormatException("GB 數值格式不符：「" + text + "」");

    private static byte[] Encode(string header, IEnumerable<string[]> rows)
    {
        var text = new StringBuilder(header).Append("\r\n");
        foreach (var fields in rows)
        {
            text.AppendJoin(',', fields.Select(Escape)).Append("\r\n");
        }
        return [.. Utf8WithBom.GetPreamble(), .. Utf8WithBom.GetBytes(text.ToString())];
    }

    // 名稱來自各程式自訂的檔案描述或執行檔名稱；Excel 會把以這些字元開頭的儲存格當成公式（CSV injection），即使欄位有雙引號
    private const string FormulaStarts = "=+-@\t\r\n";

    /// <summary>
    /// 名稱開頭像公式時前面加一個單引號，讓 Excel 當成文字。本來就以單引號接這些字元開頭的名稱也加，往返才不會少一個單引號。
    /// </summary>
    private static string GuardFormula(string name) =>
        StartsLikeFormula(name.AsSpan().TrimStart('\'')) ? "'" + name : name;

    // 去掉 GuardFormula 加上的單引號；1.0.0 寫的名稱沒有加，以公式字元開頭時照原樣讀回
    private static string UnguardFormula(string field) =>
        field.StartsWith('\'') && StartsLikeFormula(field.AsSpan().TrimStart('\'')) ? field[1..] : field;

    private static bool StartsLikeFormula(ReadOnlySpan<char> text) =>
        !text.IsEmpty && FormulaStarts.Contains(text[0]);

    private static string Escape(string field) =>
        field.AsSpan().IndexOfAny(",\"\r\n") < 0 ? field : "\"" + field.Replace("\"", "\"\"") + "\"";

    private static IEnumerable<string[]> Decode(byte[] content, string header)
    {
        string text;
        try
        {
            var span = content.AsSpan();
            var preamble = Utf8WithBom.Preamble;
            text = StrictUtf8.GetString(span.StartsWith(preamble) ? span[preamble.Length..] : span);
        }
        catch (DecoderFallbackException e)
        {
            throw new FormatException("不是 UTF-8 編碼", e);
        }

        var records = SplitRecords(text);
        if (records.Count == 0 || string.Join(',', records[0]) != header)
        {
            throw new FormatException("欄位名稱不符");
        }
        foreach (var fields in records.Skip(1))
        {
            if (fields.Length != ColumnCount)
            {
                throw new FormatException("欄位數不符：" + fields.Length);
            }
            yield return fields;
        }
    }

    /// <summary>依 RFC 4180 拆成記錄與欄位：雙引號內可以有逗號、換行與成對的雙引號；空白行略過。</summary>
    private static List<string[]> SplitRecords(string text)
    {
        var records = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        bool recordHasContent = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c != '"')
                {
                    field.Append(c);
                }
                else if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else
                {
                    quoted = false;
                }
                continue;
            }

            switch (c)
            {
                case '"':
                    quoted = true;
                    recordHasContent = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    recordHasContent = true;
                    break;
                case '\r' when i + 1 < text.Length && text[i + 1] == '\n':
                    break;
                case '\n':
                    EndRecord();
                    break;
                default:
                    field.Append(c);
                    recordHasContent = true;
                    break;
            }
        }
        if (quoted)
        {
            throw new FormatException("雙引號沒有成對");
        }
        EndRecord();
        return records;

        void EndRecord()
        {
            if (recordHasContent)
            {
                fields.Add(field.ToString());
                records.Add(fields.ToArray());
            }
            fields.Clear();
            field.Clear();
            recordHasContent = false;
        }
    }
}
