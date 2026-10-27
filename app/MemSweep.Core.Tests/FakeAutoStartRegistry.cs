using MemSweep.Desktop;

namespace MemSweep.Core.Tests;

/// <summary>代替 HKCU 的 Run 機碼；測試不得實際寫入使用者的開機項目（Design 3.8）。</summary>
internal sealed class FakeAutoStartRegistry : IAutoStartRegistry
{
    public string? Value { get; set; }

    public int Writes { get; private set; }

    public int Deletes { get; private set; }

    public Exception? Failure { get; set; }

    public string? Read() => Value;

    public void Write(string command)
    {
        ThrowIfFailing();
        Writes++;
        Value = command;
    }

    public void Delete()
    {
        ThrowIfFailing();
        Deletes++;
        Value = null;
    }

    private void ThrowIfFailing()
    {
        if (Failure is not null)
        {
            throw Failure;
        }
    }
}
