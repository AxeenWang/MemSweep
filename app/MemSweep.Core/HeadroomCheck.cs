namespace MemSweep.Core;

/// <summary>剩餘 commit 檢查的結果。</summary>
public sealed record HeadroomResult(bool IsEnough, long FreeBytes, long NeedBytes);

/// <summary>判斷剩餘 commit 是否足夠（F-42）。</summary>
public static class HeadroomCheck
{
    /// <summary>剩餘 commit 大於或等於需要的量即為足夠。</summary>
    public static HeadroomResult Evaluate(SystemSnapshot snapshot, double needGigabytes)
    {
        long need = MemoryMath.FromGigabytes(needGigabytes);
        return new HeadroomResult(snapshot.CommitFreeBytes >= need, snapshot.CommitFreeBytes, need);
    }
}
