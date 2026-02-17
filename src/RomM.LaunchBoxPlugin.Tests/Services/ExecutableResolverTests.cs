using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RomMbox.Services.Install;
using RomMbox.Services.Logging;

namespace RomMbox.Tests.Services
{
    [TestClass]
    public sealed class ExecutableResolverTests
    {
        private sealed class NullSink : ILogSink
        {
            public void Write(LogMessage message)
            {
            }
        }

        [TestMethod]
        public void Resolve_UsesManifestExecutable()
        {
            var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var exeDir = Path.Combine(root, "bin");
            Directory.CreateDirectory(exeDir);
            var exePath = Path.Combine(exeDir, "game.exe");
            File.WriteAllText(exePath, string.Empty);
            var manifest = "{\"Executable\":\"bin\\\\game.exe\",\"Arguments\":[\"-fullscreen\"]}";
            File.WriteAllText(Path.Combine(root, "manifest.json"), manifest);

            try
            {
                var resolver = new ExecutableResolver(new LoggingService(LogLevel.Debug, new NullSink()));
                var result = resolver.Resolve(root);
                Assert.IsTrue(result.Success);
                Assert.AreEqual(exePath, result.ExecutablePath);
                Assert.AreEqual(1, result.Arguments.Count);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}
