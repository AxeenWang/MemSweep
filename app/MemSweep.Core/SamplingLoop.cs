using System.ComponentModel;

namespace MemSweep.Core;

/// <summary>
/// 立即取樣一次，之後每隔固定時間取樣並發布結果（Design 3.4）。
/// 取樣時的 Win32 錯誤只略過那一次；其他例外視為程式錯誤，讓迴圈結束。
/// </summary>
public sealed class SamplingLoop
{
    public static readonly TimeSpan DefaultPeriod = TimeSpan.FromSeconds(2);

    private readonly Func<GroupedSnapshot> _sample;
    private readonly Action<GroupedSnapshot> _publish;
    private readonly TimeSpan _period;

    public SamplingLoop(Func<GroupedSnapshot> sample, Action<GroupedSnapshot> publish, TimeSpan period)
    {
        _sample = sample;
        _publish = publish;
        _period = period;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_period);
        do
        {
            GroupedSnapshot? grouped = null;
            try
            {
                grouped = _sample();
            }
            catch (Exception e) when (e is Win32Exception or InvalidOperationException)
            {
                // 這一次取樣失敗，畫面保留上一次的結果，下次再試
            }
            if (grouped is not null)
            {
                _publish(grouped);
            }
        }
        while (await WaitForNextTickAsync(timer, cancellationToken));
    }

    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
