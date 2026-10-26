using System.Windows;
using Microsoft.Win32;

namespace MemSweep.Desktop;

internal enum ThemeKind
{
    Light,
    Dark,
}

/// <summary>讀取 Windows 的應用程式深淺色設定（F-61、Design 3.6）。</summary>
internal static class ThemeReader
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>AppsUseLightTheme 為 0 是深色；沒有這個值或型別不對時視為淺色。</summary>
    public static ThemeKind FromRegistryValue(object? value) =>
        value is int number && number == 0 ? ThemeKind.Dark : ThemeKind.Light;

    public static ThemeKind ReadCurrent()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return FromRegistryValue(key?.GetValue("AppsUseLightTheme"));
    }
}

/// <summary>依目前設定套用淺色或深色資源，並在使用者切換時跟著換。</summary>
internal sealed class ThemeWatcher : IDisposable
{
    private readonly ResourceDictionary _resources;
    private ThemeKind? _current;

    public ThemeWatcher(ResourceDictionary resources)
    {
        _resources = resources;
        Apply();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // 這個事件在 SystemEvents 自己的執行緒上觸發
        if (e.Category == UserPreferenceCategory.General)
        {
            Application.Current?.Dispatcher.InvokeAsync(Apply);
        }
    }

    private void Apply()
    {
        var kind = ThemeReader.ReadCurrent();
        if (kind == _current)
        {
            return;
        }
        _current = kind;
        _resources.MergedDictionaries.Clear();
        _resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/MemSweep;component/Themes/{kind}.xaml"),
        });
    }
}
