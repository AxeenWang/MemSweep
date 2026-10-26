using System.Windows.Threading;

namespace MemSweep.Desktop;

/// <summary>取樣迴圈因程式錯誤結束時，在 UI 執行緒重新拋出，讓程式明顯當掉，而不是圖示停在舊數字。</summary>
internal static class SamplingFailure
{
    // 必須用 BeginInvoke：InvokeAsync 會把例外收進回傳的 Task，沒有人觀察就消失
    public static void Raise(Dispatcher dispatcher, Exception? cause) =>
        dispatcher.BeginInvoke(new Action(() => throw new InvalidOperationException("取樣迴圈意外結束", cause)));
}
