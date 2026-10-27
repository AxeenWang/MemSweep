namespace MemSweep.Core;

/// <summary>data\ 裡的檔案一律先寫暫存檔再取代原檔，寫到一半中斷也不會留下殘缺的檔案（Design 3.9）。</summary>
internal static class AtomicFile
{
    /// <summary>資料夾不存在時（例如執行中被刪除）先建立；失敗時丟出 <see cref="IOException"/> 或 <see cref="UnauthorizedAccessException"/>。</summary>
    public static void Write(string path, byte[] content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            stream.Write(content);
            // 只寫進系統快取就取代原檔的話，斷電或當機後原檔可能整份是 0 而無法讀取
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
