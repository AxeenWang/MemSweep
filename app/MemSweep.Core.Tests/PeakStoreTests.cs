using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class PeakStoreTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time");
    private static readonly DateTimeOffset PeakTime = new(2026, 10, 26, 14, 35, 2, TimeSpan.FromHours(8));

    // 測試資料夾放在測試輸出資料夾底下，不碰 repo 以外的位置
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "PeakStoreTests", Guid.NewGuid().ToString("N"));

    private string DataDirectory => Path.Combine(_root, "data");

    private static long Gb(double value) => MemoryMath.FromGigabytes(value);

    private static PeakHistory History(int days) =>
        new(
            [new BootPeak(new DateTime(2026, 10, 26, 0, 30, 0, DateTimeKind.Utc), Gb(36.8), Gb(39.4), PeakTime, [new PeakApp("Claude", Gb(3.2))])],
            Enumerable.Range(0, days)
                .Select(i => new DailyPeak(new DateOnly(2026, 10, 26).AddDays(-i), Gb(31.4), Gb(39.4), PeakTime.AddDays(-i), [new PeakApp("Claude", Gb(3.2))]))
                .Reverse()
                .ToList());

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [TestMethod]
    public void OpenCreatesTheDataFolder()
    {
        var opening = PeakStore.Open(DataDirectory, Zone);

        Assert.IsTrue(Directory.Exists(DataDirectory));
        Assert.IsNotNull(opening.Store);
        Assert.IsNull(opening.Warning);
        Assert.IsEmpty(opening.History.Boots);
        Assert.IsEmpty(opening.History.Days);
        Assert.IsEmpty(Directory.GetFileSystemEntries(DataDirectory));
    }

    [TestMethod]
    public void SavedRecordsAreReadBackOnTheNextOpen()
    {
        PeakStore.Open(DataDirectory, Zone).Store!.Save(History(days: 2));

        var opening = PeakStore.Open(DataDirectory, Zone);

        CollectionAssert.AreEquivalent(
            new[] { "peaks-boot.csv", "peaks-daily.csv" },
            Directory.GetFiles(DataDirectory).Select(Path.GetFileName).ToArray());
        Assert.HasCount(1, opening.History.Boots);
        Assert.AreEqual(PeakTime, opening.History.Boots[0].Time);
        CollectionAssert.AreEqual(
            new[] { new DateOnly(2026, 10, 25), new DateOnly(2026, 10, 26) },
            opening.History.Days.Select(d => d.Date).ToArray());
    }

    [TestMethod]
    public void SaveWritesTheCsvFormat()
    {
        PeakStore.Open(DataDirectory, Zone).Store!.Save(History(days: 1));

        byte[] written = File.ReadAllBytes(Path.Combine(DataDirectory, "peaks-daily.csv"));

        CollectionAssert.AreEqual(PeakCsv.WriteDays(History(days: 1).Days, Zone), written);
    }

    [TestMethod]
    public void SaveReplacesTheWholeFile()
    {
        var store = PeakStore.Open(DataDirectory, Zone).Store!;
        store.Save(History(days: 3));

        store.Save(History(days: 1));

        Assert.HasCount(1, PeakStore.Open(DataDirectory, Zone).History.Days);
    }

    [TestMethod]
    public void AFailedSaveLeavesTheOldFileIntact()
    {
        var store = PeakStore.Open(DataDirectory, Zone).Store!;
        store.Save(History(days: 1));
        string path = Path.Combine(DataDirectory, "peaks-daily.csv");
        byte[] before = File.ReadAllBytes(path);
        // 暫存檔的位置被資料夾佔住，寫到一半就失敗
        Directory.CreateDirectory(path + ".tmp");

        Assert.Throws<Exception>(() => store.Save(History(days: 3)));

        CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
    }

    [TestMethod]
    public void AFolderThatCannotBeCreatedDisablesTheStore()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(DataDirectory, "不是資料夾");

        var opening = PeakStore.Open(DataDirectory, Zone);

        Assert.IsNull(opening.Store);
        Assert.AreEqual("data 資料夾無法寫入，尖峰紀錄已停用", opening.Warning);
        Assert.IsEmpty(opening.History.Days);
    }

    [TestMethod]
    public void AFolderThatCannotBeWrittenDisablesTheStore()
    {
        // 試寫的暫存檔位置被資料夾佔住，等同沒有寫入權限
        Directory.CreateDirectory(Path.Combine(DataDirectory, "peaks-boot.csv.tmp"));

        var opening = PeakStore.Open(DataDirectory, Zone);

        Assert.IsNull(opening.Store);
        Assert.AreEqual("data 資料夾無法寫入，尖峰紀錄已停用", opening.Warning);
    }

    [TestMethod]
    [DataRow("peaks-boot.csv")]
    [DataRow("peaks-daily.csv")]
    public void AnUnreadableFileDisablesTheStoreAndIsLeftAlone(string fileName)
    {
        PeakStore.Open(DataDirectory, Zone).Store!.Save(History(days: 1));
        string path = Path.Combine(DataDirectory, fileName);
        byte[] broken = [0xB6, 0x7D, 0xBE, 0xF7];
        File.WriteAllBytes(path, broken);

        var opening = PeakStore.Open(DataDirectory, Zone);

        Assert.IsNull(opening.Store);
        Assert.AreEqual(fileName + " 無法讀取，尖峰紀錄已停用", opening.Warning);
        Assert.IsEmpty(opening.History.Boots);
        CollectionAssert.AreEqual(broken, File.ReadAllBytes(path));
    }

    [TestMethod]
    public void ReadsWhileAnotherProgramHasTheFileOpen()
    {
        PeakStore.Open(DataDirectory, Zone).Store!.Save(History(days: 2));

        // 模擬 Excel：以讀寫方式開啟，只允許其他程式讀取
        using var excel = new FileStream(Path.Combine(DataDirectory, "peaks-daily.csv"), FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        var opening = PeakStore.Open(DataDirectory, Zone);

        Assert.IsNotNull(opening.Store);
        Assert.HasCount(2, opening.History.Days);
    }

    [TestMethod]
    public void SaveFailsWhileAnotherProgramHasTheFileOpenAndSucceedsLater()
    {
        var store = PeakStore.Open(DataDirectory, Zone).Store!;
        store.Save(History(days: 1));

        using (new FileStream(Path.Combine(DataDirectory, "peaks-boot.csv"), FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
        {
            // 被鎖住的檔案無法取代時，Windows 回報「存取被拒」或共用違規，兩種都可能
            var error = Assert.Throws<Exception>(() => store.Save(History(days: 2)));
            Assert.IsTrue(error is IOException or UnauthorizedAccessException, error.GetType().Name);
            Assert.IsFalse(File.Exists(Path.Combine(DataDirectory, "peaks-boot.csv.tmp")));
        }

        store.Save(History(days: 2));
        Assert.HasCount(2, PeakStore.Open(DataDirectory, Zone).History.Days);
    }
}
