using System.Text.Json;
using System.Text.Json.Serialization;

namespace MemSweep.Core;

/// <summary>data\ 資料夾裡的 settings.json（F-50、F-52、Design 3.9）。</summary>
public sealed class SettingsStore
{
    public const string FileName = "settings.json";

    public SettingsStore(string directory)
    {
        FilePath = Path.Combine(directory, FileName);
    }

    public string FilePath { get; }

    /// <summary>檔案不存在、無法讀取或內容不合法時使用預設值，下次儲存時覆寫（Design 3.9）。</summary>
    public AppSettings Load()
    {
        try
        {
            // 使用者用其他程式開著檔案時也要讀得到
            using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var settings = JsonSerializer.Deserialize(stream, SettingsJson.Default.AppSettings);
            return settings is { IsValid: true } ? settings : AppSettings.Default;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return AppSettings.Default;
        }
    }

    /// <summary>寫入全部設定，資料夾不存在時建立；失敗時丟出 <see cref="IOException"/> 或 <see cref="UnauthorizedAccessException"/>。</summary>
    public void Save(AppSettings settings) =>
        AtomicFile.Write(FilePath, JsonSerializer.SerializeToUtf8Bytes(settings, SettingsJson.Default.AppSettings));
}

/// <summary>settings.json 的格式：欄位名稱為小駝峰，四個欄位都必須存在，換行與尖峰紀錄 CSV 一樣用 CRLF。</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    NewLine = "\r\n",
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext;
