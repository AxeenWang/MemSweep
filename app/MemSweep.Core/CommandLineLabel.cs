using System.Text.RegularExpressions;

namespace MemSweep.Core;

/// <summary>從 node 行程的命令列取出用來區分的標籤。</summary>
public static partial class CommandLineLabel
{
    /// <summary>
    /// 依序找：具名空間套件（@scope/name）、node_modules 底下的套件名稱、.js/.cjs/.mjs 腳本名稱；
    /// 都找不到時回傳 null。
    /// </summary>
    public static string? FromCommandLine(string commandLine)
    {
        var scoped = ScopedPackage().Match(commandLine);
        if (scoped.Success)
        {
            return $"{scoped.Groups[1].Value}/{scoped.Groups[2].Value}";
        }

        var package = NodeModulesPackage().Match(commandLine);
        if (package.Success)
        {
            return package.Groups[1].Value;
        }

        var script = ScriptFile().Match(commandLine);
        return script.Success ? script.Groups[1].Value : null;
    }

    [GeneratedRegex(@"(@[\w.-]+)[\\/]([\w.-]+)")]
    private static partial Regex ScopedPackage();

    // 跳過 .bin 這類以點開頭的資料夾
    [GeneratedRegex(@"node_modules[\\/](?!\.)([\w.-]+)")]
    private static partial Regex NodeModulesPackage();

    [GeneratedRegex(@"([\w.-]+)\.[cm]?js\b")]
    private static partial Regex ScriptFile();
}
