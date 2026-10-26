using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class MemoryMathTests
{
    [TestMethod]
    public void OneGigabyteIs1024Cubed()
    {
        Assert.AreEqual(1_073_741_824L, MemoryMath.FromGigabytes(1));
        Assert.AreEqual(1_610_612_736L, MemoryMath.FromGigabytes(1.5));
        Assert.AreEqual(1.5, MemoryMath.ToGigabytes(1_610_612_736L));
    }

    [TestMethod]
    public void FormatGigabytesUsesOneDecimal()
    {
        Assert.AreEqual("31.4", MemoryMath.FormatGigabytes(MemoryMath.FromGigabytes(31.4)));
        Assert.AreEqual("0.0", MemoryMath.FormatGigabytes(0));
        Assert.AreEqual("8.0", MemoryMath.FormatGigabytes(8L * MemoryMath.BytesPerGigabyte));
    }

    [TestMethod]
    public void PercentRoundsHalfAwayFromZero()
    {
        Assert.AreEqual(80, MemoryMath.Percent(80, 100));
        Assert.AreEqual(33, MemoryMath.Percent(1, 3));
        Assert.AreEqual(67, MemoryMath.Percent(2, 3));
        Assert.AreEqual(1, MemoryMath.Percent(1, 200));
        Assert.AreEqual(100, MemoryMath.Percent(5, 5));
    }

    [TestMethod]
    public void PercentOfZeroTotalIsZero()
    {
        Assert.AreEqual(0, MemoryMath.Percent(10, 0));
    }
}
