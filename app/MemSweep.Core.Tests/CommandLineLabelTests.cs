using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class CommandLineLabelTests
{
    [TestMethod]
    public void ScopedPackageWithForwardSlash()
    {
        Assert.AreEqual(
            "@playwright/mcp",
            CommandLineLabel.FromCommandLine(@"""C:\Program Files\nodejs\node.exe"" npx -y @playwright/mcp@latest"));
    }

    [TestMethod]
    public void ScopedPackageInsideNodeModulesPath()
    {
        Assert.AreEqual(
            "@wonderwhy-er/desktop-commander",
            CommandLineLabel.FromCommandLine(
                @"node C:\Users\me\AppData\Roaming\npm\node_modules\@wonderwhy-er\desktop-commander\dist\index.js"));
    }

    [TestMethod]
    public void UnscopedPackageUnderNodeModules()
    {
        Assert.AreEqual(
            "typescript",
            CommandLineLabel.FromCommandLine(@"node C:\work\node_modules\typescript\bin\tsc --watch"));
    }

    [TestMethod]
    public void DotFolderUnderNodeModulesFallsBackToScriptName()
    {
        Assert.AreEqual(
            "vite",
            CommandLineLabel.FromCommandLine(@"node C:\work\node_modules\.bin\..\vite\bin\vite.js"));
    }

    [TestMethod]
    public void ScriptFileName()
    {
        Assert.AreEqual("server", CommandLineLabel.FromCommandLine("node server.mjs --port 3000"));
        Assert.AreEqual("worker", CommandLineLabel.FromCommandLine(@"""C:\Program Files\nodejs\node.exe"" C:\tools\worker.cjs"));
    }

    [TestMethod]
    public void NoLabel()
    {
        Assert.IsNull(CommandLineLabel.FromCommandLine(@"""C:\Program Files\nodejs\node.exe"" --version"));
        Assert.IsNull(CommandLineLabel.FromCommandLine(string.Empty));
    }
}
