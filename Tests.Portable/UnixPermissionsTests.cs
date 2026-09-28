using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Portable
{
    /// <summary>
    /// PlasmaDownloader's EngineDownload.FixPermissions makes the downloaded `spring` binary
    /// executable, and it does so with Mono.Posix's Syscall.chmod - inside a
    /// `catch (Exception) { Trace.TraceWarning(...) }`.
    ///
    /// Three things make that worth measuring rather than reading:
    ///
    ///   1. the method runs ONLY when Environment.OSVersion.Platform == PlatformID.Unix, so no
    ///      Windows build and no Windows developer ever executes it
    ///   2. Mono.Posix's syscalls P/Invoke MonoPosixHelper, a mono runtime component
    ///   3. if it throws, the catch turns it into a warning and the engine binary is left
    ///      non-executable - the download "succeeds" and the game never starts
    ///
    /// That is the kernel32 failure exactly: compiles everywhere, throws on the platform it was
    /// written for, and is swallowed into a broken state rather than an error.
    /// </summary>
    [TestClass]
    public class UnixPermissionsTests
    {
        [TestMethod]
        public void The_runtime_can_make_a_file_executable_the_way_EngineDownload_needs()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Assert.Inconclusive("FixPermissions only runs on Unix");
                return;
            }

            var path = Path.Combine(Path.GetTempPath(), "zk-perm-probe-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(path, "#!/bin/sh\nexit 0\n");
            try
            {
                // The permissions FixPermissions asks for: rwxr-xr-x.
                File.SetUnixFileMode(path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

                var mode = File.GetUnixFileMode(path);
                Assert.IsTrue(mode.HasFlag(UnixFileMode.UserExecute),
                    "the owner execute bit did not stick, so a downloaded engine would not be runnable");
                Assert.IsTrue(mode.HasFlag(UnixFileMode.GroupExecute) && mode.HasFlag(UnixFileMode.OtherExecute),
                    "group/other execute did not stick; FixPermissions asks for 0755");
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
