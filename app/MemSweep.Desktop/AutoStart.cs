using Microsoft.Win32;

namespace MemSweep.Desktop;

/// <summary>開機自動啟動用到的登錄值；測試以假的實作取代，開發期間不在使用者電腦上實際切換（Design 3.8）。</summary>
internal interface IAutoStartRegistry
{
    /// <summary>目前登記的命令；沒有登記時為 null。</summary>
    string? Read();

    void Write(string command);

    void Delete();
}

/// <summary>開機自動啟動（F-51、Design 3.8）：以目前使用者的身分登記，不需要系統管理員權限。</summary>
internal sealed class AutoStart
{
    private readonly IAutoStartRegistry _registry;

    public AutoStart(IAutoStartRegistry registry, string exePath)
    {
        _registry = registry;
        Command = "\"" + exePath + "\"";
    }

    /// <summary>登記的內容：加上引號的 MemSweep.exe 完整路徑。</summary>
    public string Command { get; }

    /// <summary>開啟時寫入，內容相同就不寫（exe 被移動過才會不同）；關閉時刪除。失敗時丟出登錄的例外。</summary>
    public void Apply(bool enabled)
    {
        string? registered = _registry.Read();
        if (enabled)
        {
            if (!string.Equals(registered, Command, StringComparison.OrdinalIgnoreCase))
            {
                _registry.Write(Command);
            }
        }
        else if (registered is not null)
        {
            _registry.Delete();
        }
    }
}

/// <summary>HKCU\Software\Microsoft\Windows\CurrentVersion\Run 裡名為 MemSweep 的值（Design 3.8）。</summary>
internal sealed class RunKeyRegistry : IAutoStartRegistry
{
    public static readonly string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static readonly string ValueName = "MemSweep";

    public string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(ValueName) as string;
    }

    public void Write(string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue(ValueName, command, RegistryValueKind.String);
    }

    public void Delete()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
