using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ZkData.UnitSyncLib;

namespace Tests.Portable
{
    /// <summary>
    /// Calls the real Spring engine.
    ///
    /// <see cref="UnitSyncResolutionTests"/> answered what the runtime LOOKS FOR when asked for
    /// `unitsync`, which needed no engine. This answers the next question, which does: whether the
    /// 135 P/Invoke declarations in NativeMethods.cs actually bind and marshal on .NET 9 on Linux.
    /// Until an engine was in the loop that was the oldest untested claim in the port -
    /// ZkLobbyServer/PORTABILITY.md carried it as "no entry point of the 135 has been called".
    ///
    /// **Skipped unless an engine is present**, so CI stays green without one:
    ///
    ///     ./tools/fetch-engine.sh                       # ~42MB, into the scratch dir it prints
    ///     ZK_DOTNET_MOUNT=&lt;that dir&gt; ./tools/dotnet.sh test Tests.Portable/Tests.Portable.csproj
    ///
    /// The engine is deliberately NOT downloaded into the repository: it is 42MB, it is not ours,
    /// and this checkout is synced by Synology Drive, which replicates anything in the tree and
    /// restores it after deletion.
    ///
    /// What these still do not cover: everything that needs game data. GetMinimap, GetHeightMap
    /// and GetMetalMap are the calls that return pixel buffers and are the reason
    /// System.Drawing.Common is still referenced - they need map archives, which no engine zip
    /// contains. See Shared/PlasmaShared/IMAGING-MIGRATION.md.
    /// </summary>
    [TestClass]
    public class UnitSyncEngineTests
    {
        private static string EngineDir => Environment.GetEnvironmentVariable("ZK_UNITSYNC_DIR");

        /// <summary>
        ///     The runtime probes next to the assembly, so the engine's library is placed there
        ///     rather than the assembly being moved to the engine.
        /// </summary>
        [TestInitialize]
        public void PutTheEngineWhereTheRuntimeLooks()
        {
            if (string.IsNullOrEmpty(EngineDir)) return;

            // unitsync finds base/springcontent.sdz through SPRING_DATADIR, which tools/dotnet.sh
            // sets when it mounts an engine. It has to be in the environment before the native
            // library loads: setting it here with Environment.SetEnvironmentVariable is too late,
            // and Init still fails with "Required base file 'base/springcontent.sdz' does not
            // exist". Measured both ways rather than reasoned about.

            var source = Path.Combine(EngineDir, "libunitsync.so");
            var target = Path.Combine(AppContext.BaseDirectory, "libunitsync.so");
            if (File.Exists(source) && !File.Exists(target)) File.Copy(source, target);
        }

        private static bool Skip()
        {
            if (!string.IsNullOrEmpty(EngineDir) && File.Exists(Path.Combine(EngineDir, "libunitsync.so"))) return false;
            Assert.Inconclusive("no engine: set ZK_UNITSYNC_DIR (see ./tools/fetch-engine.sh)");
            return true;
        }

        [TestMethod]
        public void A_real_entry_point_binds_marshals_and_answers()
        {
            if (Skip()) return;

            var version = UnitSync.NativeMethods.GetSpringVersion();

            Assert.IsFalse(string.IsNullOrWhiteSpace(version),
                "GetSpringVersion returned nothing. The library loaded - otherwise this would have "
                + "thrown DllNotFoundException - so the declaration binds and the call returns, but "
                + "the string did not marshal back.");
            Console.WriteLine("        engine reports: " + version);
        }

        [TestMethod]
        public void The_engine_initialises_and_reports_an_empty_archive_set()
        {
            if (Skip()) return;

            // isServer: true is what AutoRegistrator's Unitsyncer passes; id is a client id.
            Assert.IsTrue(UnitSync.NativeMethods.Init(true, 0),
                "unitsync refused to initialise: " + (UnitSync.NativeMethods.GetNextError() ?? "and reported no error"));
            try
            {
                // No archives have been added, so both counts must be 0. The point is not the
                // number - it is that an int comes back across the boundary at all, from a call
                // that does real work inside the engine rather than returning a constant.
                Assert.AreEqual(0, UnitSync.NativeMethods.GetMapCount(), "a map appeared without any archive being added");
                Assert.AreEqual(0, UnitSync.NativeMethods.GetPrimaryModCount(), "a game appeared without any archive being added");
            }
            finally
            {
                UnitSync.NativeMethods.UnInit();
            }
        }
    }
}
