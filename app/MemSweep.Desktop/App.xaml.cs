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

        // 設定與尖峰紀錄放在 exe 所在資料夾下的 data\（F-52）
        string data = Path.Combine(AppContext.BaseDirectory, "data");
        var settings = new SettingsService(new SettingsStore(data), new AutoStart(new RunKeyRegistry(), Environment.ProcessPath!));
        settings.SyncAutoStart();

        _theme = new ThemeWatcher(Resources);
        _tray = new TrayController(settings);
        _tray.ExitRequested += () => Shutdown();
        // UI 執行緒的工作一律用 BeginInvoke，例外才不會像 InvokeAsync 那樣消失（Design 3.4）
        _instance.ShowRequested += () => Dispatcher.BeginInvoke(new Action(() => _tray?.ShowFlyout()));
        StartSampling(data, settings);
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

    private void StartSampling(string data, SettingsService settings)
    {
        var opening = PeakStore.Open(data, TimeZoneInfo.Local);
        var peaks = new PeakRecorder(opening, settings.Current.RetentionDays);
        _peaks = peaks;
        // 保留天數改變後，下一次取樣就依新的天數清理（Design 3.9）
        settings.Changed += changed => peaks.SetRetentionDays(changed.RetentionDays);

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
                Dispatcher.BeginInvoke(new Action(() => _tray?.Update(grouped, state)));
            },
            SamplingLoop.DefaultPeriod);

        _sampling = new CancellationTokenSource();
        var token = _sampling.Token;
        Task.Run(() => loop.RunAsync(token)).ContinueWith(
            task => SamplingFailure.Raise(Dispatcher, task.Exception),
            TaskContinuationOptions.OnlyOnFaulted);
    }
}
