using System.Globalization;
using MemSweep.Core;

namespace MemSweep.Cli;

/// <summary>memsweep-cli 的參數解析與執行（F-40～F-43）。只在需要時取樣，且不讀寫 data\。</summary>
public static class CliApp
{
    public const int ExitOk = 0;
    public const int ExitNotEnough = 1;
    public const int ExitUsageError = 2;

    public static readonly string Usage =
        "用法：\n" +
        "  memsweep-cli status             顯示 commit、RAM、尖峰與前五名\n" +
        "  memsweep-cli status --json      以 JSON 輸出相同內容\n" +
        "  memsweep-cli check --need <GB>  剩餘 commit 足夠時結束碼 0，不足時 1\n";

    public static int Run(string[] args, Func<GroupedSnapshot> sample, TextWriter output, TextWriter error)
    {
        switch (args)
        {
            case [] or ["--help"] or ["-h"]:
                output.Write(Usage);
                return ExitOk;

            case ["status"]:
                output.Write(ReportFormatter.ToText(sample()));
                return ExitOk;

            case ["status", "--json"]:
                output.Write(ReportFormatter.ToJson(sample()));
                return ExitOk;

            case ["check", "--need", var text] when TryParseGigabytes(text, out double need):
                var grouped = sample();
                var result = HeadroomCheck.Evaluate(grouped.Snapshot, need);
                output.Write(ReportFormatter.ToCheckText(grouped, result));
                return result.IsEnough ? ExitOk : ExitNotEnough;

            case ["check", "--need", var text]:
                error.Write("錯誤：--need 需要大於 0 的 GB 數值，收到「" + text + "」\n" + Usage);
                return ExitUsageError;

            default:
                error.Write("錯誤：無法辨識的參數「" + string.Join(" ", args) + "」\n" + Usage);
                return ExitUsageError;
        }
    }

    private static bool TryParseGigabytes(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value)
        && value > 0;
}
