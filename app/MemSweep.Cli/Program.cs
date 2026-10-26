using System.Text;
using MemSweep.Cli;
using MemSweep.Core;

Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
return CliApp.Run(args, () => MemorySampler.CreateDefault().Sample(), Console.Out, Console.Error);
