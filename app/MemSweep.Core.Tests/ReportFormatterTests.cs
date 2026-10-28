using System.Text.Json;
using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class ReportFormatterTests
{
    [TestMethod]
    public void TextSummaryListsTopFiveAndUnattributed()
    {
        string expected =
            "MemSweep  2026-10-26 14:35:02\n" +
            "Commit  31.4 / 39.4 GB（80%），剩 8.0 GB\n" +
            "RAM     20.5 / 31.4 GB（65%）\n" +
            "尖峰    36.8 GB（開機以來）\n" +
            "前五名\n" +
            "  Claude  36 個行程  3.2 GB\n" +
            "  ChatGPT  13 個行程  2.2 GB\n" +
            "  Microsoft Edge WebView2  19 個行程  1.8 GB\n" +
            "  Windows（系統與服務）  140 個行程  1.5 GB\n" +
            "  Node.js JavaScript Runtime  30 個行程  1.4 GB\n" +
            "系統（不屬於任何程式）  6.9 GB\n";

        Assert.AreEqual(expected, ReportFormatter.ToText(ReportSamples.Typical()));
    }

    [TestMethod]
    public void TextSummaryWithFewerThanFiveApps()
    {
        string text = ReportFormatter.ToText(ReportSamples.TwoApps());

        StringAssert.EndsWith(
            text,
            "前五名\n  Claude  36 個行程  3.2 GB\n  ChatGPT  13 個行程  2.2 GB\n系統（不屬於任何程式）  6.9 GB\n");
    }

    [TestMethod]
    public void JsonHasTheSameContentInBytes()
    {
        var grouped = ReportSamples.Typical();
        string json = ReportFormatter.ToJson(grouped);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.AreEqual("2026-10-26T14:35:02+08:00", root.GetProperty("timestamp").GetString());
        var commit = root.GetProperty("commit");
        Assert.AreEqual(grouped.Snapshot.CommitUsedBytes, commit.GetProperty("usedBytes").GetInt64());
        Assert.AreEqual(grouped.Snapshot.CommitLimitBytes, commit.GetProperty("limitBytes").GetInt64());
        Assert.AreEqual(grouped.Snapshot.CommitFreeBytes, commit.GetProperty("freeBytes").GetInt64());
        Assert.AreEqual(grouped.Snapshot.CommitPeakBytes, commit.GetProperty("peakBytes").GetInt64());
        var physical = root.GetProperty("physical");
        Assert.AreEqual(grouped.Snapshot.PhysicalUsedBytes, physical.GetProperty("usedBytes").GetInt64());
        Assert.AreEqual(grouped.Snapshot.PhysicalTotalBytes, physical.GetProperty("totalBytes").GetInt64());
        var top = root.GetProperty("topApps").EnumerateArray().ToList();
        Assert.HasCount(5, top);
        Assert.AreEqual("Claude", top[0].GetProperty("name").GetString());
        Assert.AreEqual(36, top[0].GetProperty("processCount").GetInt32());
        Assert.AreEqual(grouped.Apps[4].CommitBytes, top[4].GetProperty("commitBytes").GetInt64());
        Assert.AreEqual(grouped.UnattributedCommitBytes, root.GetProperty("unattributedBytes").GetInt64());
    }

    [TestMethod]
    public void JsonKeepsChineseReadableAndUsesLineFeeds()
    {
        string json = ReportFormatter.ToJson(ReportSamples.Typical());

        StringAssert.Contains(json, "\"name\": \"Windows（系統與服務）\"");
        Assert.DoesNotContain("\r", json);
        StringAssert.EndsWith(json, "}\n");
    }

    [TestMethod]
    public void CheckTextWhenEnough()
    {
        var grouped = ReportSamples.Typical();
        var result = HeadroomCheck.Evaluate(grouped.Snapshot, 5);

        Assert.AreEqual("足夠：剩餘 commit 8.0 GB，需要 5.0 GB\n", ReportFormatter.ToCheckText(grouped, result));
    }

    [TestMethod]
    public void CheckTextWhenNotEnoughListsTopApps()
    {
        var grouped = ReportSamples.Typical();
        var result = HeadroomCheck.Evaluate(grouped.Snapshot, 10);

        string text = ReportFormatter.ToCheckText(grouped, result);

        StringAssert.StartsWith(text, "不足：剩餘 commit 8.0 GB，需要 10.0 GB\n前五名\n  Claude  36 個行程  3.2 GB\n");
        StringAssert.EndsWith(text, "系統（不屬於任何程式）  6.9 GB\n");
    }
}
