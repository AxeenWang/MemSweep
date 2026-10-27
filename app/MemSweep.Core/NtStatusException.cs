namespace MemSweep.Core;

/// <summary>
/// <c>NtQuerySystemInformation</c> 回傳失敗的 NTSTATUS。取樣迴圈與 memsweep-cli 只把它和
/// <see cref="System.ComponentModel.Win32Exception"/> 當成「無法取得系統記憶體資訊」；
/// 其他例外，包括程式錯誤常見的 <see cref="InvalidOperationException"/>，都視為程式錯誤（Design 3.4、3.11）。
/// </summary>
public sealed class NtStatusException(int status, int informationClass)
    : Exception($"NtQuerySystemInformation({informationClass}) 失敗：0x{status:X8}");
