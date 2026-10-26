using System.Reflection;
using System.Runtime.InteropServices;
using MemSweep.Cli;
using MemSweep.Core;
using MemSweep.Desktop;

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
        "TerminateProcess",
        "NtTerminateProcess",
        "NtSetInformationProcess",
        "VirtualFreeEx",
    ];

    // 只檢查以 DllImport／LibraryImport 宣告的呼叫；NativeLibrary.GetExport 或函式指標不在檢查範圍
    private static readonly Assembly[] ShippedAssemblies =
    [
        typeof(SystemReader).Assembly,
        typeof(CliApp).Assembly,
        typeof(SingleInstance).Assembly,
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
    public void ScansTheDesktopAssembly()
    {
        CollectionAssert.Contains(EntryPoints(), "DestroyIcon");
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
