using MemSweep.Core;
using MemSweep.Desktop;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class SettingsServiceTests
{
    private const string ExePath = @"D:\Tools\MemSweep\MemSweep.exe";
    private const string Command = "\"" + ExePath + "\"";

    // 測試資料夾放在測試輸出資料夾底下，不碰 repo 以外的位置
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "SettingsServiceTests", Guid.NewGuid().ToString("N"));
    private readonly FakeAutoStartRegistry _registry = new();

    private string DataDirectory => Path.Combine(_root, "data");

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private SettingsService Service(AppSettings? saved = null)
    {
        if (saved is not null)
        {
            new SettingsStore(DataDirectory).Save(saved);
        }
        return new SettingsService(new SettingsStore(DataDirectory), new AutoStart(_registry, ExePath));
    }

    [TestMethod]
    public void StartsWithTheSavedSettings()
    {
        var service = Service(new AppSettings(60, 90, false, 30));

        Assert.AreEqual(new AppSettings(60, 90, false, 30), service.Current);
    }

    [TestMethod]
    public void SaveWritesTheFileAndAnnouncesTheChange()
    {
        var service = Service();
        var announced = new List<AppSettings>();
        service.Changed += announced.Add;
        var next = new AppSettings(60, 90, false, 30);

        string? error = service.Save(next);

        Assert.IsNull(error);
        Assert.AreEqual(next, service.Current);
        Assert.AreEqual(next, new SettingsStore(DataDirectory).Load());
        CollectionAssert.AreEqual(new[] { next }, announced);
    }

    [TestMethod]
    public void TurningAutoStartOnRegistersTheExe()
    {
        var service = Service();

        Assert.IsNull(service.Save(AppSettings.Default with { AutoStart = true }));

        Assert.AreEqual(Command, _registry.Value);
    }

    [TestMethod]
    public void TurningAutoStartOffRemovesIt()
    {
        _registry.Value = Command;
        var service = Service(AppSettings.Default with { AutoStart = true });

        Assert.IsNull(service.Save(AppSettings.Default));

        Assert.IsNull(_registry.Value);
    }

    [TestMethod]
    public void OtherChangesLeaveTheRegistryAlone()
    {
        // 例如另一份 MemSweep 登記的開機項目
        _registry.Value = "\"C:\\Other\\MemSweep.exe\"";
        var service = Service();

        Assert.IsNull(service.Save(AppSettings.Default with { OrangeThreshold = 60, RetentionDays = 30 }));

        Assert.AreEqual(0, _registry.Writes);
        Assert.AreEqual(0, _registry.Deletes);
        Assert.AreEqual("\"C:\\Other\\MemSweep.exe\"", _registry.Value);
    }

    [TestMethod]
    public void AFailedWriteKeepsTheCurrentSettingsAndTheRegistry()
    {
        var service = Service();
        bool announced = false;
        service.Changed += _ => announced = true;
        // data 的位置被檔案佔住，資料夾建立不了
        Directory.CreateDirectory(_root);
        File.WriteAllText(DataDirectory, "不是資料夾");

        string? error = service.Save(AppSettings.Default with { AutoStart = true, RetentionDays = 30 });

        Assert.AreEqual("無法寫入 settings.json，設定沒有儲存", error);
        Assert.AreEqual(AppSettings.Default, service.Current);
        Assert.IsFalse(announced);
        Assert.IsNull(_registry.Value);
    }

    [TestMethod]
    public void ARegistryFailureIsReportedAfterTheSettingsAreSaved()
    {
        var service = Service();
        _registry.Failure = new UnauthorizedAccessException();
        var next = AppSettings.Default with { AutoStart = true };

        string? error = service.Save(next);

        Assert.AreEqual("設定已儲存，但無法變更開機自動啟動", error);
        Assert.AreEqual(next, service.Current);
        Assert.AreEqual(next, new SettingsStore(DataDirectory).Load());
    }

    [TestMethod]
    public void StartupSyncUpdatesAMovedExe()
    {
        _registry.Value = "\"C:\\Users\\me\\Downloads\\MemSweep\\MemSweep.exe\"";
        var service = Service(AppSettings.Default with { AutoStart = true });

        service.SyncAutoStart();

        Assert.AreEqual(Command, _registry.Value);
    }

    [TestMethod]
    public void StartupSyncLeavesTheRegistryAloneWhenAutoStartIsOff()
    {
        _registry.Value = Command;
        var service = Service();

        service.SyncAutoStart();

        Assert.AreEqual(Command, _registry.Value);
        Assert.AreEqual(0, _registry.Deletes);
    }

    [TestMethod]
    public void AStartupSyncFailureIsRetriedAtTheNextStartup()
    {
        _registry.Failure = new System.Security.SecurityException();
        var service = Service(AppSettings.Default with { AutoStart = true });

        service.SyncAutoStart();

        Assert.IsNull(_registry.Value);
        Assert.IsTrue(service.Current.AutoStart);
    }
}
