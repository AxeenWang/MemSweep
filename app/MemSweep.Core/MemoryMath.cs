using System.Globalization;

namespace MemSweep.Core;

/// <summary>記憶體數值的換算。全工具的 1 GB 都是 1024³ bytes，與工作管理員一致。</summary>
public static class MemoryMath
{
    public const long BytesPerGigabyte = 1L << 30;

    public static double ToGigabytes(long bytes) => bytes / (double)BytesPerGigabyte;

    public static long FromGigabytes(double gigabytes) => (long)Math.Round(gigabytes * BytesPerGigabyte);

    /// <summary>以小數一位表示 GB，例如「31.4」。</summary>
    public static string FormatGigabytes(long bytes) =>
        ToGigabytes(bytes).ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>四捨五入成整數百分比；total 不大於 0 時回傳 0。</summary>
    public static int Percent(long part, long total) =>
        total <= 0 ? 0 : (int)Math.Round(part * 100.0 / total, MidpointRounding.AwayFromZero);
}
