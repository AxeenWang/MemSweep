using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Shell;
using MemSweep.Core;
using MemSweep.Desktop;

namespace MemSweep.Core.Tests;

// 多個 STA 執行緒同時初始化 WPF 介面元件時會互相鎖死，介面測試一律依序執行
[TestClass]
[DoNotParallelize]
public sealed class SettingsWindowTests
{
    private static SettingsWindow NewWindow(AppSettings? current = null)
    {
        var window = new SettingsWindow { DataContext = new SettingsForm(current ?? AppSettings.Default) };
        WpfLayout.LaidOut((FrameworkElement)window.Content, 360);
        return window;
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    [STATestMethod]
    public void TheWindowShowsEverySetting()
    {
        var window = NewWindow(new AppSettings(60, 90, true, 30));

        CollectionAssert.IsSubsetOf(
            new[] { "MemSweep 設定", "RAM 使用率的顏色", "橘色門檻", "紅色門檻", "保留天數", "開機時自動啟動 MemSweep", "儲存", "取消" },
            WpfLayout.Texts((DependencyObject)window.Content));
        Assert.AreEqual("60", window.OrangeBox.Text);
        Assert.AreEqual("90", window.RedBox.Text);
        Assert.AreEqual("30", window.RetentionBox.Text);
        Assert.IsTrue(window.AutoStartBox.IsChecked);
        Assert.AreEqual(Visibility.Collapsed, window.ErrorText.Visibility);
        window.Close();
    }

    [STATestMethod]
    public void EditsReachTheFormAsTheyAreTyped()
    {
        var window = NewWindow();
        var form = (SettingsForm)window.DataContext;

        window.OrangeBox.Text = "60";
        window.RetentionBox.Text = "30";
        window.AutoStartBox.IsChecked = true;

        Assert.AreEqual("60", form.OrangeText);
        Assert.AreEqual("30", form.RetentionText);
        Assert.IsTrue(form.AutoStart);
        window.Close();
    }

    [STATestMethod]
    public void SaveHandsOverTheSettingsAndCloses()
    {
        var window = NewWindow();
        AppSettings? saved = null;
        bool closed = false;
        window.Save = settings =>
        {
            saved = settings;
            return null;
        };
        window.Closed += (_, _) => closed = true;
        window.OrangeBox.Text = "60";
        window.AutoStartBox.IsChecked = true;

        Click(window.SaveButton);

        Assert.AreEqual(new AppSettings(60, 85, true, 90), saved);
        Assert.IsTrue(closed);
    }

    [STATestMethod]
    public void InvalidInputIsShownAndNothingIsSaved()
    {
        var window = NewWindow();
        bool saved = false;
        bool closed = false;
        window.Save = _ =>
        {
            saved = true;
            return null;
        };
        window.Closed += (_, _) => closed = true;
        window.RedBox.Text = "50";

        Click(window.SaveButton);

        Assert.IsFalse(saved);
        Assert.IsFalse(closed);
        Assert.AreEqual(Visibility.Visible, window.ErrorText.Visibility);
        Assert.AreEqual("橘色門檻必須小於紅色門檻", window.ErrorText.Text);
        window.Close();
    }

    [STATestMethod]
    public void ASaveFailureIsShownAndTheWindowStaysOpen()
    {
        var window = NewWindow();
        bool closed = false;
        window.Save = _ => "無法寫入 settings.json，設定沒有儲存";
        window.Closed += (_, _) => closed = true;

        Click(window.SaveButton);

        Assert.IsFalse(closed);
        Assert.AreEqual(Visibility.Visible, window.ErrorText.Visibility);
        Assert.AreEqual("無法寫入 settings.json，設定沒有儲存", window.ErrorText.Text);
        window.Close();
    }

    [STATestMethod]
    public void CancelClosesWithoutSaving()
    {
        var window = NewWindow();
        bool saved = false;
        bool closed = false;
        window.Save = _ =>
        {
            saved = true;
            return null;
        };
        window.Closed += (_, _) => closed = true;
        window.OrangeBox.Text = "60";

        Click(window.CancelButton);

        Assert.IsFalse(saved);
        Assert.IsTrue(closed);
    }

    [STATestMethod]
    public void EnterSavesAndEscapeCancels()
    {
        var window = NewWindow();

        Assert.IsTrue(window.SaveButton.IsDefault);
        Assert.IsTrue(window.CancelButton.IsCancel);
        window.Close();
    }

    [STATestMethod]
    public void TheWindowUsesTheCustomTitleBarWithOnlyACloseButton()
    {
        var window = NewWindow();

        var chrome = WindowChrome.GetWindowChrome(window);
        Assert.IsNotNull(chrome);
        Assert.AreEqual(32.0, chrome.CaptionHeight);
        Assert.AreEqual(ResizeMode.NoResize, window.ResizeMode);
        Assert.AreEqual("MemSweep 設定", window.Caption.TitleText.Text);
        Assert.AreEqual(Visibility.Collapsed, window.Caption.MinimizeButton.Visibility);
        Assert.AreEqual(Visibility.Collapsed, window.Caption.MaximizeButton.Visibility);
        Assert.AreEqual(Visibility.Visible, window.Caption.CloseButton.Visibility);
        window.Close();
    }

    [STATestMethod]
    public void NumberBoxesTurnOffTheInputMethod()
    {
        var window = NewWindow();

        Assert.IsFalse(InputMethod.GetIsInputMethodEnabled(window.OrangeBox));
        Assert.IsFalse(InputMethod.GetIsInputMethodEnabled(window.RedBox));
        Assert.IsFalse(InputMethod.GetIsInputMethodEnabled(window.RetentionBox));
        window.Close();
    }
}
