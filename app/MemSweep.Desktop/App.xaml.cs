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
        _tray?.Dispose();
        _theme?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private void StartSampling()
    {
        // MemorySampler 與 PeakTracker 只在取樣迴圈裡依序使用，不會同時被兩個執行緒存取
        var sampler = MemorySampler.CreateDefault();
        var tracker = new PeakTracker();
        var loop = new SamplingLoop(
            () =>
            {
                var grouped = sampler.Sample();
                tracker.Update(grouped);
                return grouped;
            },
            grouped =>
            {
                var peak = tracker.BootPeak;
                Dispatcher.InvokeAsync(() => _tray?.Update(grouped, peak));
            },
            SamplingLoop.DefaultPeriod);

        _sampling = new CancellationTokenSource();
        var token = _sampling.Token;
        Task.Run(() => loop.RunAsync(token)).ContinueWith(
            task => Dispatcher.InvokeAsync(() => throw new InvalidOperationException("取樣迴圈意外結束", task.Exception)),
            TaskContinuationOptions.OnlyOnFaulted);
    }
}
