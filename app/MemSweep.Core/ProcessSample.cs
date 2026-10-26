namespace MemSweep.Core;

/// <summary>單一行程在某次取樣時的狀態。</summary>
/// <param name="Pid">行程 ID。</param>
/// <param name="ParentPid">建立它的父行程 ID；父行程可能已結束，或 ID 已被重複使用。</param>
/// <param name="CreateTimeUtc">行程建立時間（UTC）。</param>
/// <param name="ImageName">映像名稱，例如「claude.exe」；PID 0 為「Idle」。</param>
/// <param name="PrivateCommitBytes">私有 commit，即工作管理員「認可大小」。</param>
/// <param name="WorkingSetBytes">工作集，即實際佔用的 RAM。</param>
public sealed record ProcessSample(
    int Pid,
    int ParentPid,
    DateTime CreateTimeUtc,
    string ImageName,
    long PrivateCommitBytes,
    long WorkingSetBytes);
