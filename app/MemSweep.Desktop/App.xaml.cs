using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using MemSweep.Core;

namespace MemSweep.Desktop;

/// <summary>常駐系統匣，直到使用者選「結束 MemSweep」（F-01）。</summary>
public partial class App : Application
{
    private SingleInstance? _instance;
    private ThemeWatcher? _theme;
    private TrayController? _tray;
    private CancellationTokenSource? _sampling;
    private PeakRecorder? _peaks;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // 必須在建立任何視窗前設定（Design 3.6）
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        _instance = SingleInstance.TryAcquire(SingleInstance.MutexName, SingleInstance.ShowPanelEventName);
        if (_instance is null)
        {
            Shutdown();
            return;
        }

        _theme = new ThemeWatcher(Resources);
        _tray = new TrayController();
        _tray.ExitRequested += () => Shutdown();
        _instance.ShowRequested += () => Dispatcher.InvokeAsync(() => _tray?.ShowFlyout());
        StartSampling();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _sampling?.Cancel();
        _sampling?.Dispose();
        // 還沒寫入的尖峰變動在結束前寫入（Design 3.9）
        _peaks?.Flush();
        _tray?.Dispose();
        _theme?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private void StartSampling()
    {
        // 設定與尖峰紀錄放在 exe 所在資料夾下的 data\（F-52）
        var opening = PeakStore.Open(Path.Combine(AppContext.BaseDirectory, "data"), TimeZoneInfo.Local);
        var peaks = new PeakRecorder(opening, PeakStore.DefaultRetentionDays);
        _peaks = peaks;

        // MemorySampler 只在取樣迴圈裡使用；PeakRecorder 另外會在結束時由 UI 執行緒呼叫，自己有 lock
        var sampler = MemorySampler.CreateDefault();
        var loop = new SamplingLoop(
            () =>
            {
                var grouped = sampler.Sample();
                peaks.Record(grouped);
                return grouped;
            },
            grouped =>
            {
                var state = peaks.State;
                Dispatcher.InvokeAsync(() => _tray?.Update(grouped, state));
            },
            SamplingLoop.DefaultPeriod);

        _sampling = new CancellationTokenSource();
        var token = _sampling.Token;
        Task.Run(() => loop.RunAsync(token)).ContinueWith(
            task => SamplingFailure.Raise(Dispatcher, task.Exception),
            TaskContinuationOptions.OnlyOnFaulted);
    }
}
