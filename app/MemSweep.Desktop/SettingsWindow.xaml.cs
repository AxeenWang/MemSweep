using System.Windows;
using MemSweep.Core;

namespace MemSweep.Desktop;

/// <summary>
/// 設定視窗（F-50、Design 3.6）：DataContext 是 <see cref="SettingsForm"/>。按「儲存」時檢查輸入並交給 <see cref="Save"/>，
/// 成功就關閉。關閉時真正關閉並釋放，下次開啟由 <see cref="TrayController"/> 重新建立。
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    /// <summary>儲存新的設定：回傳 null 表示成功，否則是要顯示在視窗裡的訊息。</summary>
    public Func<AppSettings, string?>? Save { get; set; }

    public void ShowWindow()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Show();
        Activate();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var form = (SettingsForm)DataContext;
        if (form.TryRead() is not { } settings)
        {
            return;
        }
        if (Save?.Invoke(settings) is { } error)
        {
            form.ShowError(error);
            return;
        }
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();
}
