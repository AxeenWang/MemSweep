namespace MemSweep.Core;

/// <summary>寫入尖峰紀錄的地方；測試以假的實作取代。</summary>
public interface IPeakStore
{
    /// <summary>寫入全部紀錄；失敗時丟出 <see cref="IOException"/> 或 <see cref="UnauthorizedAccessException"/>。</summary>
    void Save(PeakHistory history);
}

/// <summary>開啟 data\ 的結果：Store 為 null 表示尖峰紀錄已停用，Warning 是顯示給使用者的原因。</summary>
public sealed record PeakStoreOpening(PeakHistory History, IPeakStore? Store, string? Warning);

/// <summary>data\ 資料夾裡的 peaks-boot.csv 與 peaks-daily.csv（F-34、F-52、Design 3.9）。</summary>
public sealed class PeakStore : IPeakStore
{
    public const int DefaultRetentionDays = 90;
    public const string UnwritableWarning = "data 資料夾無法寫入，尖峰紀錄已停用";

    private readonly string _directory;
    private readonly TimeZoneInfo _zone;

    private PeakStore(string directory, TimeZoneInfo zone)
    {
        _directory = directory;
        _zone = zone;
    }

    /// <summary>
    /// 建立資料夾（不存在時）並載入紀錄。資料夾無法寫入，或既有的紀錄檔無法讀取時停用尖峰紀錄，
    /// 也不會覆寫那些檔案；其他功能照常（Design 3.9）。
    /// </summary>
    public static PeakStoreOpening Open(string directory, TimeZoneInfo zone)
    {
        try
        {
            Directory.CreateDirectory(directory);
            // 資料夾已存在但沒有寫入權限時 CreateDirectory 不會失敗，所以實際建立一個暫存檔試試
            using var probe = new FileStream(
                Path.Combine(directory, PeakCsv.BootFileName + ".tmp"),
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                1,
                FileOptions.DeleteOnClose);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new PeakStoreOpening(PeakHistory.Empty, null, UnwritableWarning);
        }

        var store = new PeakStore(directory, zone);
        string fileName = PeakCsv.BootFileName;
        try
        {
            var boots = store.Read(fileName, PeakCsv.ReadBoots);
            fileName = PeakCsv.DailyFileName;
            var days = store.Read(fileName, PeakCsv.ReadDays);
            return new PeakStoreOpening(new PeakHistory(boots, days), store, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            return new PeakStoreOpening(PeakHistory.Empty, null, fileName + " 無法讀取，尖峰紀錄已停用");
        }
    }

    public void Save(PeakHistory history)
    {
        Replace(PeakCsv.BootFileName, PeakCsv.WriteBoots(history.Boots, _zone));
        Replace(PeakCsv.DailyFileName, PeakCsv.WriteDays(history.Days, _zone));
    }

    private List<T> Read<T>(string fileName, Func<byte[], TimeZoneInfo, List<T>> parse)
    {
        string path = Path.Combine(_directory, fileName);
        if (!File.Exists(path))
        {
            return [];
        }

        // 其他程式（例如 Excel）開著檔案時也要讀得到
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var content = new MemoryStream();
        stream.CopyTo(content);
        return parse(content.ToArray(), _zone);
    }

    // 先寫暫存檔再取代原檔，寫到一半中斷也不會留下殘缺的紀錄檔
    private void Replace(string fileName, byte[] content)
    {
        string path = Path.Combine(_directory, fileName);
        string temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            stream.Write(content);
            // 只寫進系統快取就取代原檔的話，斷電或當機後原檔可能整份是 0 而無法讀取，尖峰紀錄就此停用
            stream.Flush(flushToDisk: true);
        }
        try
        {
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            File.Delete(temp);
            throw;
        }
    }
}
