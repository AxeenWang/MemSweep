using System.Runtime.InteropServices;

namespace MemSweep.Desktop.Native;

/// <summary>user32 的介面資源釋放；不涉及系統記憶體狀態。</summary>
internal static partial class User32
{
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint icon);
}
