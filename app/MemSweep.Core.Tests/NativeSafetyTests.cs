using System.Reflection;
using System.Runtime.InteropServices;
using MemSweep.Cli;
using MemSweep.Core;

namespace MemSweep.Core.Tests;

/// <summary>A-07：出貨的組件不得宣告任何改變系統記憶體狀態或提升權限的 Win32 呼叫。</summary>
[TestClass]
public sealed class NativeSafetyTests
{
    private static readonly string[] ForbiddenEntryPoints =
    [
        "EmptyWorkingSet",
        "K32EmptyWorkingSet",
        "SetProcessWorkingSetSize",
        "SetProcessWorkingSetSizeEx",
        "NtSetSystemInformation",
        "SetSystemFileCacheSize",
        "AdjustTokenPrivileges",
    ];

    // Phase 02 加入 MemSweep.Desktop 後，也要把它的組件加進這份清單
    private static readonly Assembly[] ShippedAssemblies =
    [
        typeof(SystemReader).Assembly,
        typeof(CliApp).Assembly,
    ];

    private static List<string> EntryPoints() =>
        (from assembly in ShippedAssemblies
         from type in assembly.GetTypes()
         from method in type.GetMethods(
             BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
         where (method.Attributes & MethodAttributes.PinvokeImpl) != 0
         select method.GetCustomAttribute<DllImportAttribute>()?.EntryPoint ?? method.Name)
        .ToList();

    [TestMethod]
    public void FindsTheReadOnlyQueries()
    {
        var entryPoints = EntryPoints();

        CollectionAssert.IsSubsetOf(
            new[]
            {
                "K32GetPerformanceInfo",
                "NtQuerySystemInformation",
                "OpenProcess",
                "CloseHandle",
                "QueryFullProcessImageNameW",
                "NtQueryInformationProcess",
            },
            entryPoints);
    }

    [TestMethod]
    public void DeclaresNoApiThatChangesMemoryStateOrPrivileges()
    {
        var found = EntryPoints()
            .Where(e => ForbiddenEntryPoints.Contains(e, StringComparer.OrdinalIgnoreCase))
            .ToList();

        Assert.IsEmpty(found);
    }
}
