namespace MemSweep.Core;

/// <summary>查詢單一行程的 <see cref="ProcessInfo"/>；測試以假的實作取代。</summary>
public interface IProcessInfoProvider
{
    ProcessInfo GetInfo(ProcessSample process);
}
