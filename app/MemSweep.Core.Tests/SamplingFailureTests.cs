using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
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

    [TestMethod]
    public void TheDesktopNeverQueuesWorkWithInvokeAsync()
    {
        // InvokeAsync 會把例外收進沒人觀察的 Task，UI 執行緒的工作一律用 BeginInvoke（Design 3.4）
        using var stream = File.OpenRead(typeof(SingleInstance).Assembly.Location);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();

        var calls = reader.MemberReferences
            .Select(reader.GetMemberReference)
            .Where(m => reader.GetString(m.Name) == "InvokeAsync" && m.Parent.Kind == HandleKind.TypeReference)
            .Select(m => reader.GetString(reader.GetTypeReference((TypeReferenceHandle)m.Parent).Name) + ".InvokeAsync")
            .ToList();

        Assert.IsEmpty(calls);
    }
}
