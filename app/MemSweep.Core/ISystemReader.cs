namespace MemSweep.Core;

/// <summary>取得系統記憶體狀態與行程清單的來源；測試以假的實作取代。</summary>
public interface ISystemReader
{
    SystemSnapshot Read();
}
