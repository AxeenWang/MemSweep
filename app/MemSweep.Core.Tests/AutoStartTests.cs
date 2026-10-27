using MemSweep.Desktop;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class AutoStartTests
{
    private const string ExePath = @"D:\Tools\MemSweep\MemSweep.exe";
    private const string Command = "\"" + ExePath + "\"";

    [TestMethod]
    public void TheRunKeyFollowsTheDesign()
    {
        Assert.AreEqual(@"Software\Microsoft\Windows\CurrentVersion\Run", RunKeyRegistry.KeyPath);
        Assert.AreEqual("MemSweep", RunKeyRegistry.ValueName);
    }

    [TestMethod]
    public void TheCommandIsTheQuotedExePath()
    {
        Assert.AreEqual(Command, new AutoStart(new FakeAutoStartRegistry(), ExePath).Command);
    }

    [TestMethod]
    public void EnablingRegistersTheExe()
    {
        var registry = new FakeAutoStartRegistry();

        new AutoStart(registry, ExePath).Apply(true);

        Assert.AreEqual(Command, registry.Value);
    }

    [TestMethod]
    [DataRow(Command)]
    [DataRow("\"d:\\tools\\memsweep\\MEMSWEEP.EXE\"")]
    public void AnUnchangedPathIsNotRewritten(string registered)
    {
        var registry = new FakeAutoStartRegistry { Value = registered };

        new AutoStart(registry, ExePath).Apply(true);

        Assert.AreEqual(0, registry.Writes);
        Assert.AreEqual(registered, registry.Value);
    }

    [TestMethod]
    public void AMovedExeIsUpdated()
    {
        var registry = new FakeAutoStartRegistry { Value = "\"C:\\Users\\me\\Downloads\\MemSweep\\MemSweep.exe\"" };

        new AutoStart(registry, ExePath).Apply(true);

        Assert.AreEqual(Command, registry.Value);
    }

    [TestMethod]
    public void DisablingRemovesTheValue()
    {
        var registry = new FakeAutoStartRegistry { Value = Command };

        new AutoStart(registry, ExePath).Apply(false);

        Assert.IsNull(registry.Value);
        Assert.AreEqual(1, registry.Deletes);
    }

    [TestMethod]
    public void DisablingWithoutAValueTouchesNothing()
    {
        var registry = new FakeAutoStartRegistry();

        new AutoStart(registry, ExePath).Apply(false);

        Assert.AreEqual(0, registry.Deletes);
        Assert.AreEqual(0, registry.Writes);
    }
}
