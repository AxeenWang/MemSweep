using System.Diagnostics;
using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
[TestCategory("Live")]
public sealed class Win32ProcessInfoProviderLiveTests
{
    private static ProcessSample CurrentProcessSample()
    {
        var snapshot = new SystemReader().Read();
        return snapshot.Processes.Single(p => p.Pid == Environment.ProcessId);
    }

    [TestMethod]
    public void CurrentProcessHasPathAndVersionInfo()
    {
        var info = new Win32ProcessInfoProvider().GetInfo(CurrentProcessSample());

        Assert.AreEqual(Environment.ProcessPath, info.ExecutablePath, ignoreCase: true);
        var version = FileVersionInfo.GetVersionInfo(Environment.ProcessPath!);
        Assert.AreEqual(version.CompanyName?.Trim(), info.CompanyName);
        Assert.AreEqual(version.FileDescription?.Trim(), info.FileDescription);
        Assert.IsNull(info.PackageLabel);
    }

    [TestMethod]
    public void IdleAndSystemAreUnknown()
    {
        var provider = new Win32ProcessInfoProvider();

        Assert.AreSame(ProcessInfo.Unknown, provider.GetInfo(new ProcessSample(0, 0, DateTime.MinValue, "Idle", 0, 0)));
        Assert.AreSame(ProcessInfo.Unknown, provider.GetInfo(new ProcessSample(4, 0, DateTime.MinValue, "System", 0, 0)));
    }

    [TestMethod]
    public void ReadsCommandLinesLongerThanTheInitialBuffer()
    {
        // 初始緩衝區 4096 bytes 只裝得下約 2000 個字元
        string filler = new('x', 6000);
        var start = new ProcessStartInfo("cmd.exe", "/c ping -n 4 127.0.0.1 >nul & rem " + filler)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var child = Process.Start(start)!;
        try
        {
            string? commandLine = Win32ProcessInfoProvider.ReadCommandLine(child.Id);

            Assert.IsNotNull(commandLine);
            StringAssert.EndsWith(commandLine, filler);
        }
        finally
        {
            child.WaitForExit();
        }
    }

    [TestMethod]
    public void ProcessThatCannotBeOpenedIsUnknown()
    {
        // PID 一定是 4 的倍數，3 不可能存在
        var info = new Win32ProcessInfoProvider().GetInfo(new ProcessSample(3, 0, DateTime.MinValue, "ghost.exe", 0, 0));

        Assert.AreSame(ProcessInfo.Unknown, info);
    }
}
