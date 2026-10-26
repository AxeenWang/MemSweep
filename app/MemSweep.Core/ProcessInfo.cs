namespace MemSweep.Core;

/// <summary>行程的靜態資訊；取不到的欄位為 null。</summary>
/// <param name="ExecutablePath">執行檔完整路徑。</param>
/// <param name="CompanyName">執行檔版本資訊的公司名稱（已去頭尾空白）。</param>
/// <param name="FileDescription">執行檔版本資訊的檔案描述（已去頭尾空白）。</param>
/// <param name="PackageLabel">node 行程從命令列取出的套件或腳本名稱。</param>
public sealed record ProcessInfo(
    string? ExecutablePath,
    string? CompanyName,
    string? FileDescription,
    string? PackageLabel)
{
    public static ProcessInfo Unknown { get; } = new(null, null, null, null);
}
