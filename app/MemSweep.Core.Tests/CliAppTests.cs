using System.ComponentModel;
using MemSweep.Cli;
using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class CliAppTests
{
    private sealed class Run
    {
        public Run(params string[] args)
        {
            var output = new StringWriter();
            var error = new StringWriter();
            ExitCode = CliApp.Run(
                args,
                () =>
                {
                    SampleCalls++;
                    return ReportSamples.Typical();
                },
                output,
                error);
            Output = output.ToString();
            Error = error.ToString();
        }

        public int ExitCode { get; }
        public string Output { get; }
        public string Error { get; }
        public int SampleCalls { get; private set; }
    }

    [TestMethod]
    [DataRow(new string[0])]
    [DataRow(new[] { "--help" })]
    [DataRow(new[] { "-h" })]
    public void PrintsUsageWithoutSampling(string[] args)
    {
        var run = new Run(args);

        Assert.AreEqual(CliApp.ExitOk, run.ExitCode);
        Assert.AreEqual(CliApp.Usage, run.Output);
        Assert.AreEqual(0, run.SampleCalls);
    }

    [TestMethod]
    public void StatusPrintsTheTextSummary()
    {
        var run = new Run("status");

        Assert.AreEqual(CliApp.ExitOk, run.ExitCode);
        Assert.AreEqual(ReportFormatter.ToText(ReportSamples.Typical()), run.Output);
        Assert.AreEqual(string.Empty, run.Error);
    }

    [TestMethod]
    public void StatusJsonPrintsJson()
    {
        var run = new Run("status", "--json");

        Assert.AreEqual(CliApp.ExitOk, run.ExitCode);
        Assert.AreEqual(ReportFormatter.ToJson(ReportSamples.Typical()), run.Output);
    }

    [TestMethod]
    [DataRow("5", CliApp.ExitOk)]
    [DataRow("7.5", CliApp.ExitOk)]
    [DataRow("8", CliApp.ExitOk)]
    [DataRow("8.5", CliApp.ExitNotEnough)]
    [DataRow("10", CliApp.ExitNotEnough)]
    public void CheckReturnsExitCodeByFreeCommit(string need, int expectedExitCode)
    {
        // 樣本的剩餘 commit 是 8.0 GB
        var run = new Run("check", "--need", need);

        Assert.AreEqual(expectedExitCode, run.ExitCode);
        StringAssert.StartsWith(run.Output, expectedExitCode == CliApp.ExitOk ? "足夠：" : "不足：");
        Assert.AreEqual(1, run.SampleCalls);
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("-1")]
    [DataRow("abc")]
    [DataRow("NaN")]
    [DataRow("Infinity")]
    [DataRow("7,5")]
    public void CheckRejectsInvalidNeed(string need)
    {
        var run = new Run("check", "--need", need);

        Assert.AreEqual(CliApp.ExitUsageError, run.ExitCode);
        Assert.AreEqual("錯誤：--need 需要大於 0 的 GB 數值，收到「" + need + "」\n" + CliApp.Usage, run.Error);
        Assert.AreEqual(string.Empty, run.Output);
        Assert.AreEqual(0, run.SampleCalls);
    }

    [TestMethod]
    [DataRow(new[] { "status" })]
    [DataRow(new[] { "status", "--json" })]
    [DataRow(new[] { "check", "--need", "5" })]
    public void SamplingFailureIsExitCode3(string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = CliApp.Run(args, () => throw new Win32Exception(5, "存取被拒。"), output, error);

        Assert.AreEqual(CliApp.ExitSampleError, exitCode);
        Assert.AreEqual("錯誤：無法取得系統記憶體資訊：存取被拒。\n", error.ToString());
        Assert.AreEqual(string.Empty, output.ToString());
    }

    [TestMethod]
    public void NtStatusFailureIsExitCode3()
    {
        var error = new StringWriter();

        int exitCode = CliApp.Run(
            ["status"],
            () => throw new NtStatusException(unchecked((int)0xC0000017), 5),
            new StringWriter(),
            error);

        Assert.AreEqual(CliApp.ExitSampleError, exitCode);
        Assert.AreEqual("錯誤：無法取得系統記憶體資訊：NtQuerySystemInformation(5) 失敗：0xC0000017\n", error.ToString());
    }

    [TestMethod]
    public void ProgrammingErrorsAreNotReportedAsSamplingFailures()
    {
        // 結束碼 3 只代表無法取得系統記憶體資訊（Design 3.11）；程式錯誤照常讓 memsweep-cli 結束並印出堆疊
        Assert.ThrowsExactly<InvalidOperationException>(() => CliApp.Run(
            ["status"],
            () => throw new InvalidOperationException("Sequence contains no elements"),
            new StringWriter(),
            new StringWriter()));
    }

    [TestMethod]
    [DataRow(new[] { "status", "--xml" })]
    [DataRow(new[] { "check" })]
    [DataRow(new[] { "check", "--need" })]
    [DataRow(new[] { "check", "10" })]
    [DataRow(new[] { "purge" })]
    public void UnknownArgumentsAreUsageErrors(string[] args)
    {
        var run = new Run(args);

        Assert.AreEqual(CliApp.ExitUsageError, run.ExitCode);
        Assert.AreEqual("錯誤：無法辨識的參數「" + string.Join(" ", args) + "」\n" + CliApp.Usage, run.Error);
        Assert.AreEqual(0, run.SampleCalls);
    }
}
