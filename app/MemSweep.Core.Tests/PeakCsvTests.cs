using System.Text;
using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class PeakCsvTests
{
    private const string BootHeader =
        "開機時間,尖峰時間,commit已用GB,commit上限GB,第1名,第2名,第3名,第4名,第5名,第1名GB,第2名GB,第3名GB,第4名GB,第5名GB";

    private const string DailyHeader =
        "日期,尖峰時間,commit已用GB,commit上限GB,第1名,第2名,第3名,第4名,第5名,第1名GB,第2名GB,第3名GB,第4名GB,第5名GB";

    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time");
    private static readonly DateTime Boot = new(2026, 10, 26, 0, 30, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset PeakTime = new(2026, 10, 26, 14, 35, 2, TimeSpan.FromHours(8));

    private static long Gb(double value) => MemoryMath.FromGigabytes(value);

    private static readonly PeakApp[] FiveApps =
    [
        new("Claude", Gb(3.2)),
        new("ChatGPT", Gb(2.2)),
        new("Microsoft Edge WebView2", Gb(1.8)),
        new(AppGrouper.WindowsGroupName, Gb(1.5)),
        new("Node.js JavaScript Runtime", Gb(1.4)),
    ];

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes.AsSpan(3));

    private static byte[] Utf8(string text) => [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(text)];

    [TestMethod]
    public void WritesUtf8WithBomCrlfAndChineseHeaders()
    {
        byte[] bytes = PeakCsv.WriteDays([new DailyPeak(new DateOnly(2026, 10, 26), Gb(31.4), Gb(39.4), PeakTime, FiveApps)], Zone);

        CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        string text = Text(bytes);
        StringAssert.StartsWith(text, DailyHeader + "\r\n");
        StringAssert.EndsWith(text, "\r\n");
        Assert.AreEqual(text.Split('\n').Length - 1, text.Split("\r\n").Length - 1);
    }

    [TestMethod]
    public void KnownBootPeakRow()
    {
        var boot = new BootPeak(Boot, Gb(36.8), Gb(39.4), PeakTime, FiveApps);

        string text = Text(PeakCsv.WriteBoots([boot], Zone));

        Assert.AreEqual(
            BootHeader + "\r\n" +
            "2026-10-26 08:30:00,2026-10-26 14:35:02,36.80,39.40," +
            "Claude,ChatGPT,Microsoft Edge WebView2,Windows（系統與服務）,Node.js JavaScript Runtime," +
            "3.20,2.20,1.80,1.50,1.40\r\n",
            text);
    }

    [TestMethod]
    public void UnknownBootPeakRow()
    {
        var boot = new BootPeak(Boot, Gb(36.8), Gb(39.4), null, null);

        string text = Text(PeakCsv.WriteBoots([boot], Zone));

        Assert.AreEqual(BootHeader + "\r\n" + "2026-10-26 08:30:00,未知,36.80,39.40,未知,,,,,,,,,\r\n", text);
    }

    [TestMethod]
    public void DailyRowWithFewerThanFiveAppsLeavesTheRestEmpty()
    {
        var day = new DailyPeak(new DateOnly(2026, 10, 26), Gb(31.4), Gb(39.4), PeakTime, FiveApps.Take(2).ToList());

        string text = Text(PeakCsv.WriteDays([day], Zone));

        Assert.AreEqual(DailyHeader + "\r\n" + "2026-10-26,2026-10-26 14:35:02,31.40,39.40,Claude,ChatGPT,,,,3.20,2.20,,,\r\n", text);
    }

    [TestMethod]
    public void TimesAreWrittenInTheGivenZone()
    {
        // 同一個時刻以 UTC 表示，寫出時仍換成本機時間
        var day = new DailyPeak(new DateOnly(2026, 10, 26), Gb(31.4), Gb(39.4), PeakTime.ToUniversalTime(), FiveApps);

        StringAssert.Contains(Text(PeakCsv.WriteDays([day], Zone)), ",2026-10-26 14:35:02,");
    }

    [TestMethod]
    public void GigabytesKeepTwoDecimals()
    {
        var day = new DailyPeak(new DateOnly(2026, 10, 26), Gb(36.8372), Gb(39.4), PeakTime, []);

        byte[] bytes = PeakCsv.WriteDays([day], Zone);

        StringAssert.Contains(Text(bytes), ",36.84,39.40,");
        Assert.AreEqual(Gb(36.84), PeakCsv.ReadDays(bytes, Zone)[0].CommitBytes);
    }

    [TestMethod]
    public void BootPeaksRoundTrip()
    {
        var boots = new[]
        {
            new BootPeak(Boot.AddDays(-1), Gb(39.0), Gb(39.4), null, null),
            new BootPeak(Boot, Gb(36.8), Gb(63.4), PeakTime, FiveApps),
        };

        var read = PeakCsv.ReadBoots(PeakCsv.WriteBoots(boots, Zone), Zone);

        Assert.HasCount(2, read);
        Assert.AreEqual(Boot.AddDays(-1), read[0].BootTimeUtc);
        Assert.AreEqual(DateTimeKind.Utc, read[0].BootTimeUtc.Kind);
        Assert.IsFalse(read[0].IsKnown);
        Assert.IsNull(read[0].TopApps);
        Assert.AreEqual(Gb(39.0), read[0].CommitBytes);
        Assert.AreEqual(Boot, read[1].BootTimeUtc);
        Assert.AreEqual(PeakTime, read[1].Time);
        Assert.AreEqual(TimeSpan.FromHours(8), read[1].Time!.Value.Offset);
        Assert.AreEqual(Gb(63.4), read[1].CommitLimitBytes);
        CollectionAssert.AreEqual(FiveApps, read[1].TopApps!.ToArray());
    }

    [TestMethod]
    public void DailyPeaksRoundTrip()
    {
        var days = new[]
        {
            new DailyPeak(new DateOnly(2026, 10, 25), Gb(30.5), Gb(39.4), PeakTime.AddDays(-1), FiveApps.Take(1).ToList()),
            new DailyPeak(new DateOnly(2026, 10, 26), Gb(31.4), Gb(39.4), PeakTime, FiveApps),
        };

        var read = PeakCsv.ReadDays(PeakCsv.WriteDays(days, Zone), Zone);

        Assert.HasCount(2, read);
        Assert.AreEqual(new DateOnly(2026, 10, 25), read[0].Date);
        Assert.AreEqual(Gb(30.5), read[0].CommitBytes);
        Assert.AreEqual(PeakTime.AddDays(-1), read[0].Time);
        CollectionAssert.AreEqual(FiveApps.Take(1).ToArray(), read[0].TopApps.ToArray());
        CollectionAssert.AreEqual(FiveApps, read[1].TopApps.ToArray());
    }

    [TestMethod]
    public void NamesWithCommasQuotesAndLineBreaksRoundTrip()
    {
        var apps = new[] { new PeakApp("Foo, \"Bar\" Helper", Gb(1.0)), new PeakApp("兩行\r\n名稱", Gb(0.5)) };
        var day = new DailyPeak(new DateOnly(2026, 10, 26), Gb(31.4), Gb(39.4), PeakTime, apps);

        byte[] bytes = PeakCsv.WriteDays([day], Zone);

        StringAssert.Contains(Text(bytes), ",\"Foo, \"\"Bar\"\" Helper\",\"兩行\r\n名稱\",");
        CollectionAssert.AreEqual(apps, PeakCsv.ReadDays(bytes, Zone)[0].TopApps.ToArray());
    }

    // 名稱來自各程式自訂的檔案描述或執行檔名稱；Excel 會把 = + - @ Tab CR LF 開頭的儲存格當成公式，即使欄位有雙引號
    [TestMethod]
    [DataRow("=1+2", "'=1+2")]
    [DataRow("+1", "'+1")]
    [DataRow("-1", "'-1")]
    [DataRow("@SUM(1)", "'@SUM(1)")]
    [DataRow("\tcmd", "'\tcmd")]
    [DataRow("\rcmd", "\"'\rcmd\"")]
    [DataRow("\ncmd", "\"'\ncmd\"")]
    [DataRow("=HYPERLINK(\"http://x\")", "\"'=HYPERLINK(\"\"http://x\"\")\"")]
    [DataRow("'=1+2", "''=1+2")]
    [DataRow("''-1", "'''-1")]
    public void NamesThatStartLikeFormulasGetALeadingApostrophe(string name, string field)
    {
        var day = new DailyPeak(new DateOnly(2026, 10, 26), Gb(31.4), Gb(39.4), PeakTime, [new PeakApp(name, Gb(1.0))]);

        string text = Text(PeakCsv.WriteDays([day], Zone));

        Assert.AreEqual(DailyHeader + "\r\n" + "2026-10-26,2026-10-26 14:35:02,31.40,39.40," + field + ",,,,,1.00,,,,\r\n", text);
    }

    [TestMethod]
    public void NamesThatStartLikeFormulasRoundTrip()
    {
        // 紀錄檔每次啟動會讀回，讀回時要去掉寫入時加上的單引號
        PeakApp[] bootApps = [new("=1+2", Gb(1.0)), new("+1", Gb(0.9)), new("-1", Gb(0.8)), new("@SUM(1)", Gb(0.7)), new("\tcmd", Gb(0.6))];
        PeakApp[] dayApps = [new("\rcmd", Gb(1.0)), new("\ncmd", Gb(0.9)), new("'=1+2", Gb(0.8)), new("''-1", Gb(0.7)), new("=HYPERLINK(\"http://x\")", Gb(0.6))];

        var boot = PeakCsv.ReadBoots(PeakCsv.WriteBoots([new BootPeak(Boot, Gb(36.8), Gb(39.4), PeakTime, bootApps)], Zone), Zone).Single();
        var day = PeakCsv.ReadDays(PeakCsv.WriteDays([new DailyPeak(new DateOnly(2026, 10, 26), Gb(31.4), Gb(39.4), PeakTime, dayApps)], Zone), Zone).Single();

        CollectionAssert.AreEqual(bootApps, boot.TopApps!.ToArray());
        CollectionAssert.AreEqual(dayApps, day.TopApps.ToArray());
    }

    [TestMethod]
    public void ReadsFormulaNamesWrittenByOlderVersions()
    {
        // 1.0.0 原樣寫出公式開頭的名稱，沒有加單引號；讀回時照舊
        byte[] content = Utf8(DailyHeader + "\r\n" + "2026-10-26,2026-10-26 14:35:02,31.40,39.40,=SUM(1),-1,,,,1.00,0.90,,,\r\n");

        var apps = PeakCsv.ReadDays(content, Zone).Single().TopApps;

        CollectionAssert.AreEqual(new[] { new PeakApp("=SUM(1)", Gb(1.0)), new PeakApp("-1", Gb(0.9)) }, apps.ToArray());
    }

    [TestMethod]
    public void OtherNamesAreWrittenAndReadAsIs()
    {
        // 單引號後面不是公式開頭、或 = - 不在開頭的名稱，不加也不去掉單引號
        PeakApp[] apps = [new("'abc", Gb(1.0)), new("'", Gb(0.9)), new("a=b", Gb(0.8)), new("Tool -x", Gb(0.7)), new("Claude", Gb(0.6))];
        var day = new DailyPeak(new DateOnly(2026, 10, 26), Gb(31.4), Gb(39.4), PeakTime, apps);

        byte[] bytes = PeakCsv.WriteDays([day], Zone);

        Assert.AreEqual(
            DailyHeader + "\r\n" + "2026-10-26,2026-10-26 14:35:02,31.40,39.40,'abc,',a=b,Tool -x,Claude,1.00,0.90,0.80,0.70,0.60\r\n",
            Text(bytes));
        CollectionAssert.AreEqual(apps, PeakCsv.ReadDays(bytes, Zone)[0].TopApps.ToArray());
    }

    [TestMethod]
    public void HeaderOnlyIsEmpty()
    {
        Assert.IsEmpty(PeakCsv.ReadBoots(Utf8(BootHeader + "\r\n"), Zone));
        Assert.IsEmpty(PeakCsv.ReadDays(Utf8(DailyHeader), Zone));
    }

    [TestMethod]
    public void ReadsLfLineEndingsBlankLinesAndNoBom()
    {
        string text = DailyHeader + "\n\n2026-10-26,2026-10-26 14:35:02,31.40,39.40,Claude,,,,,3.20,,,,\n\n";

        var read = PeakCsv.ReadDays(Encoding.UTF8.GetBytes(text), Zone);

        Assert.HasCount(1, read);
        Assert.AreEqual("Claude", read[0].TopApps[0].Name);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("Date,PeakTime,CommitGB,LimitGB,1,2,3,4,5,1GB,2GB,3GB,4GB,5GB")]
    [DataRow("開機時間,尖峰時間,commit已用GB,commit上限GB,第1名,第2名,第3名,第4名,第5名,第1名GB,第2名GB,第3名GB,第4名GB,第5名GB")]
    public void RejectsAWrongHeader(string header)
    {
        Assert.ThrowsExactly<FormatException>(() => PeakCsv.ReadDays(Utf8(header + "\r\n"), Zone));
    }

    [TestMethod]
    public void RejectsBytesThatAreNotUtf8()
    {
        // Excel 另存成「CSV（逗號分隔）」時會改用 Big5：「開機」= B6 7D BE F7
        byte[] big5 = [0xB6, 0x7D, 0xBE, 0xF7, (byte)','];

        Assert.ThrowsExactly<FormatException>(() => PeakCsv.ReadBoots(big5, Zone));
    }

    [TestMethod]
    public void RejectsBytesThatAreNotUtf8InsideARow()
    {
        // 欄名正確但名稱裡有不是 UTF-8 的位元組：照樣讀進來會把亂碼寫回檔案
        byte[] row = Encoding.UTF8.GetBytes("2026-10-26,2026-10-26 14:35:02,31.40,39.40,");
        byte[] rest = Encoding.UTF8.GetBytes(",,,,,3.20,,,,\r\n");
        byte[] content = [.. Utf8(DailyHeader + "\r\n"), .. row, 0xB6, 0x7D, .. rest];

        Assert.ThrowsExactly<FormatException>(() => PeakCsv.ReadDays(content, Zone));
    }

    [TestMethod]
    public void RejectsATimeSkippedByDaylightSaving()
    {
        // 美國太平洋時間 2026-03-08 02:00 直接跳到 03:00，02:30 不存在
        var pacific = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
        byte[] content = Utf8(BootHeader + "\r\n" + "2026-03-08 02:30:00,未知,36.80,39.40,未知,,,,,,,,,\r\n");

        Assert.ThrowsExactly<FormatException>(() => PeakCsv.ReadBoots(content, pacific));
    }

    [TestMethod]
    [DataRow("2026/10/26 08:30,2026-10-26 14:35:02,36.80,39.40,Claude,,,,,3.20,,,,")]
    [DataRow("2026-10-26 08:30:00,2026-10-26 14:35:02,36.80,39.40,Claude,,,,,3.20,,,")]
    [DataRow("2026-10-26 08:30:00,2026-10-26 14:35:02,-1.00,39.40,Claude,,,,,3.20,,,,")]
    [DataRow("2026-10-26 08:30:00,2026-10-26 14:35:02,36.80,39.40,Claude,,,,,abc,,,,")]
    [DataRow("2026-10-26 08:30:00,2026-10-26 14:35:02,36.80,39.40,\"Claude,,,,,3.20,,,,")]
    public void RejectsMalformedBootRows(string row)
    {
        Assert.ThrowsExactly<FormatException>(() => PeakCsv.ReadBoots(Utf8(BootHeader + "\r\n" + row + "\r\n"), Zone));
    }

    [TestMethod]
    [DataRow("2026-10-26,未知,31.40,39.40,未知,,,,,,,,,")]
    [DataRow("2026/10/26,2026-10-26 14:35:02,31.40,39.40,Claude,,,,,3.20,,,,")]
    public void RejectsMalformedDailyRows(string row)
    {
        Assert.ThrowsExactly<FormatException>(() => PeakCsv.ReadDays(Utf8(DailyHeader + "\r\n" + row + "\r\n"), Zone));
    }

    // 紀錄檔被改壞時，換算成 UTC 會超出範圍的時間與大到不合理的 GB 數值也是格式不符，MemSweep 啟動時才不會當掉
    [TestMethod]
    [DataRow("0001-01-01 07:59:59,未知,36.80,39.40,未知,,,,,,,,,")]
    [DataRow("2026-10-26 08:30:00,0001-01-01 07:59:59,36.80,39.40,Claude,,,,,3.20,,,,")]
    [DataRow("2026-10-26 08:30:00,2026-10-26 14:35:02,1048577.00,39.40,Claude,,,,,3.20,,,,")]
    [DataRow("2026-10-26 08:30:00,2026-10-26 14:35:02,36.80,39.40,Claude,,,,,99999999999.00,,,,")]
    public void RejectsBootValuesOutsideTheSupportedRange(string row)
    {
        Assert.ThrowsExactly<FormatException>(() => PeakCsv.ReadBoots(Utf8(BootHeader + "\r\n" + row + "\r\n"), Zone));
    }

    [TestMethod]
    [DataRow("2026-10-26,0001-01-01 07:59:59,31.40,39.40,Claude,,,,,3.20,,,,")]
    [DataRow("2026-10-26,2026-10-26 14:35:02,31.40,99999999999.00,Claude,,,,,3.20,,,,")]
    public void RejectsDailyValuesOutsideTheSupportedRange(string row)
    {
        Assert.ThrowsExactly<FormatException>(() => PeakCsv.ReadDays(Utf8(DailyHeader + "\r\n" + row + "\r\n"), Zone));
    }

    [TestMethod]
    public void AcceptsTheLimitsOfTheSupportedRange()
    {
        // 台北時間 0001-01-01 08:00:00 正好是 UTC 的最早時間；GB 最大 1048576（1 PB）
        byte[] content = Utf8(BootHeader + "\r\n" + "0001-01-01 08:00:00,未知,1048576.00,39.40,未知,,,,,,,,,\r\n");

        var boot = PeakCsv.ReadBoots(content, Zone).Single();

        Assert.AreEqual(DateTime.MinValue, boot.BootTimeUtc);
        Assert.AreEqual(Gb(1048576), boot.CommitBytes);
    }
}
