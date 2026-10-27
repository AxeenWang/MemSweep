using System.Text;
using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class SettingsStoreTests
{
    // 測試資料夾放在測試輸出資料夾底下，不碰 repo 以外的位置
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "SettingsStoreTests", Guid.NewGuid().ToString("N"));

    private string DataDirectory => Path.Combine(_root, "data");

    private string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void WriteSettings(string json)
    {
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(SettingsPath, json);
    }

    [TestMethod]
    public void DefaultsFollowTheSpec()
    {
        Assert.AreEqual(new AppSettings(70, 85, false, 90), AppSettings.Default);
        Assert.AreEqual(UsageThresholds.Default, AppSettings.Default.Thresholds);
        Assert.IsTrue(AppSettings.Default.IsValid);
    }

    [TestMethod]
    [DataRow(1, 2, 1, true)]
    [DataRow(99, 100, 3650, true)]
    [DataRow(0, 85, 90, false)]
    [DataRow(85, 85, 90, false)]
    [DataRow(86, 85, 90, false)]
    [DataRow(70, 101, 90, false)]
    [DataRow(70, 85, 0, false)]
    [DataRow(70, 85, 3651, false)]
    public void ValidationFollowsTheDesign(int orange, int red, int retentionDays, bool valid)
    {
        Assert.AreEqual(valid, new AppSettings(orange, red, false, retentionDays).IsValid);
    }

    [TestMethod]
    public void AMissingFileGivesTheDefaultsWithoutCreatingAnything()
    {
        var settings = new SettingsStore(DataDirectory).Load();

        Assert.AreEqual(AppSettings.Default, settings);
        Assert.IsFalse(Directory.Exists(DataDirectory));
    }

    [TestMethod]
    public void SavedSettingsAreReadBack()
    {
        var store = new SettingsStore(DataDirectory);
        var saved = new AppSettings(60, 90, true, 30);

        store.Save(saved);

        Assert.AreEqual(SettingsPath, store.FilePath);
        Assert.AreEqual(saved, new SettingsStore(DataDirectory).Load());
    }

    [TestMethod]
    public void SaveWritesTheDesignFormat()
    {
        new SettingsStore(DataDirectory).Save(AppSettings.Default);

        byte[] written = File.ReadAllBytes(SettingsPath);
        // 沒有 BOM
        Assert.AreEqual((byte)'{', written[0]);
        Assert.AreEqual(
            "{\r\n  \"orangeThreshold\": 70,\r\n  \"redThreshold\": 85,\r\n  \"autoStart\": false,\r\n  \"retentionDays\": 90\r\n}",
            Encoding.UTF8.GetString(written));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("不是 JSON")]
    [DataRow("null")]
    [DataRow("[]")]
    [DataRow("""{"orangeThreshold":70,"redThreshold":85,"autoStart":false}""")]
    [DataRow("""{"orangeThreshold":60,"redThreshold":90,"retentionDays":30}""")]
    [DataRow("""{"orangeThreshold":"70","redThreshold":85,"autoStart":false,"retentionDays":90}""")]
    [DataRow("""{"orangeThreshold":70.5,"redThreshold":85,"autoStart":false,"retentionDays":90}""")]
    [DataRow("""{"orangeThreshold":85,"redThreshold":85,"autoStart":true,"retentionDays":90}""")]
    [DataRow("""{"orangeThreshold":70,"redThreshold":85,"autoStart":true,"retentionDays":0}""")]
    public void InvalidContentGivesTheDefaults(string json)
    {
        WriteSettings(json);

        Assert.AreEqual(AppSettings.Default, new SettingsStore(DataDirectory).Load());
    }

    [TestMethod]
    public void UnknownFieldsAreIgnored()
    {
        WriteSettings("""{"orangeThreshold":60,"redThreshold":90,"autoStart":true,"retentionDays":30,"theme":"dark"}""");

        Assert.AreEqual(new AppSettings(60, 90, true, 30), new SettingsStore(DataDirectory).Load());
    }

    [TestMethod]
    public void InvalidContentIsOverwrittenByTheNextSave()
    {
        WriteSettings("不是 JSON");
        var store = new SettingsStore(DataDirectory);

        store.Save(store.Load() with { RetentionDays = 30 });

        Assert.AreEqual(AppSettings.Default with { RetentionDays = 30 }, store.Load());
    }

    [TestMethod]
    public void ReadsWhileAnotherProgramHasTheFileOpen()
    {
        new SettingsStore(DataDirectory).Save(new AppSettings(60, 90, true, 30));

        // 模擬文字編輯器：以讀寫方式開啟，只允許其他程式讀取
        using var editor = new FileStream(SettingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);

        Assert.AreEqual(new AppSettings(60, 90, true, 30), new SettingsStore(DataDirectory).Load());
    }

    [TestMethod]
    public void SaveFailsWhenTheDataFolderCannotBeCreated()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(DataDirectory, "不是資料夾");

        Assert.Throws<IOException>(() => new SettingsStore(DataDirectory).Save(AppSettings.Default));
    }
}
