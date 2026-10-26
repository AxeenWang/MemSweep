using System.Runtime.InteropServices;

namespace MemSweep.Desktop.Native;

/// <summary>user32 的介面資源與前景視窗許可；不涉及系統記憶體狀態。</summary>
internal static partial class User32
{
    /// <summary>ASFW_ANY：允許任何行程把視窗帶到前景。</summary>
    public const uint AsfwAny = uint.MaxValue;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint icon);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AllowSetForegroundWindow(uint processId);
}
