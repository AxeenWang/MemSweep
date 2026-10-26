using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace MemSweep.Core.Tests;

[TestClass]
[TestCategory("Live")]
public sealed class CliProcessLiveTests
{
    private static (int ExitCode, string Output) RunCli(params string[] args)
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "memsweep-cli.exe"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
        };
        foreach (string arg in args)
        {
            start.ArgumentList.Add(arg);
        }
        using var process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    [TestMethod]
    public void StatusJsonFromTheRealExecutable()
    {
        var (exitCode, output) = RunCli("status", "--json");

        Assert.AreEqual(0, exitCode);
        using var doc = JsonDocument.Parse(output);
        Assert.IsGreaterThan(0L, doc.RootElement.GetProperty("commit").GetProperty("usedBytes").GetInt64());
        Assert.HasCount(5, doc.RootElement.GetProperty("topApps").EnumerateArray().ToList());
    }

    [TestMethod]
    public void StatusTextIsUtf8()
    {
        var (exitCode, output) = RunCli("status");

        Assert.AreEqual(0, exitCode);
        StringAssert.Contains(output, "前五名\n");
        StringAssert.Contains(output, "系統（不屬於任何程式）");
    }

    [TestMethod]
    public void CheckWithHugeNeedIsNotEnough()
    {
        var (exitCode, output) = RunCli("check", "--need", "100000");

        Assert.AreEqual(1, exitCode);
        StringAssert.StartsWith(output, "不足：");
    }
}
