using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class SystemReaderTests
{
    [TestMethod]
    public void AFailedNtStatusIsReportedAsNtStatusException()
    {
        // 0xC0000017 是 STATUS_NO_MEMORY
        var e = Assert.ThrowsExactly<NtStatusException>(() => SystemReader.ThrowIfFailed(unchecked((int)0xC0000017), 5));

        Assert.AreEqual("NtQuerySystemInformation(5) 失敗：0xC0000017", e.Message);
    }

    [TestMethod]
    public void StatusSuccessDoesNotThrow()
    {
        SystemReader.ThrowIfFailed(0, 5);
    }
}
