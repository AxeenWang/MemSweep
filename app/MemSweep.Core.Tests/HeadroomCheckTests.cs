using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class HeadroomCheckTests
{
    [TestMethod]
    public void FreeEqualToNeedIsEnough()
    {
        var snapshot = ReportSamples.Typical().Snapshot;

        var result = HeadroomCheck.Evaluate(snapshot, 8);

        Assert.IsTrue(result.IsEnough);
        Assert.AreEqual(snapshot.CommitFreeBytes, result.FreeBytes);
        Assert.AreEqual(8L * MemoryMath.BytesPerGigabyte, result.NeedBytes);
    }

    [TestMethod]
    public void FreeBelowNeedIsNotEnough()
    {
        var result = HeadroomCheck.Evaluate(ReportSamples.Typical().Snapshot, 8.1);

        Assert.IsFalse(result.IsEnough);
        Assert.AreEqual(MemoryMath.FromGigabytes(8.1), result.NeedBytes);
    }
}
