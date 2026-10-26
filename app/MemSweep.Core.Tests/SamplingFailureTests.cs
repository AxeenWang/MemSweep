using System.Windows.Threading;
using MemSweep.Desktop;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class SamplingFailureTests
{
    [STATestMethod]
    public void SamplingFailureReachesTheDispatcherUnhandledException()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        Exception? unhandled = null;
        DispatcherUnhandledExceptionEventHandler handler = (_, e) =>
        {
            unhandled = e.Exception;
            e.Handled = true;
        };
        dispatcher.UnhandledException += handler;
        try
        {
            SamplingFailure.Raise(dispatcher, new NullReferenceException());

            // 處理完佇列中優先順序較高的工作後結束這個訊息迴圈
            var frame = new DispatcherFrame();
            dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
        finally
        {
            dispatcher.UnhandledException -= handler;
        }

        Assert.IsInstanceOfType<InvalidOperationException>(unhandled);
        Assert.IsInstanceOfType<NullReferenceException>(unhandled.InnerException);
    }
}
