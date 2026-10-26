using System.Runtime.InteropServices;

namespace MemSweep.Desktop.Native;

/// <summary>user32 的介面資源、前景視窗與畫面尺寸查詢；不涉及系統記憶體狀態。</summary>
internal static partial class User32
{
    /// <summary>ASFW_ANY：允許任何行程把視窗帶到前景。</summary>
    public const uint AsfwAny = uint.MaxValue;

    public const int SmCxSizeFrame = 32;
    public const int SmCySizeFrame = 33;
    public const int SmCxPaddedBorder = 92;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint icon);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AllowSetForegroundWindow(uint processId);

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForSystem();

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();
}
