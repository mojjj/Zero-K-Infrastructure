using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PlasmaShared;
using PlasmaShared.Imaging;
using ZkData;

namespace ZeroKWeb
{
    /// <summary>
    /// Corrects a map's stored images the first time somebody looks at the map, instead of in one
    /// sweep over the whole Resources folder.
    ///
    /// **The defect being corrected.** Maps registered before the 2026-09-25 ToBytes fix had the
    /// aspect ratio applied twice, so a 2:1 map's minimap, metal map and height map were stored
    /// squashed to 4:1, and the thumbnail derived from them was stretched back to the right
    /// dimensions around distorted content. A legacy image has aspect R squared where a correct one
    /// has R, and the database knows R - which is what ImageSizing.LooksLikeLegacyToBytes reads.
    ///
    /// **It re-registers rather than re-stretching** - Shared/PlasmaShared/IMAGING-MIGRATION.md
    /// calls these variant B and variant A. unitsync renders the images again from the map archive,
    /// so the result is what the map would get if it were registered today. Re-stretching is
    /// cheaper and needs no download, but it cannot recover detail the first resize threw away.
    /// Correcting on demand is what makes the expensive one affordable: the work lands on the maps
    /// people actually open, in the order they open them, rather than on all of them at once.
    ///
    /// Three things this deliberately does NOT do:
    ///
    /// - **It never throws into the request.** A map page that renders is worth more than a
    ///   correction, so everything here is caught and the page goes out either way.
    /// - **It tries each map once per process.** A popular map is requested constantly; without
    ///   that, a failing one would be retried on every hit. An app-pool recycle clears the record,
    ///   which is the retry.
    /// - **It corrects nothing itself.** ReregisterResource does the work, through the same path
    ///   /Admin/ReregisterResource drives by hand, so there is one implementation and not two.
    /// </summary>
    public static class LegacyMinimapUpdater
    {
        /// <summary>
        /// How many re-registrations may be in flight. Two, so a burst of map views cannot turn
        /// into a burst of archive downloads and unitsync scans on the machine serving the site.
        ///
        /// Worth knowing what this does and does not buy: AutoRegistrator.ReregisterResource takes
        /// a STATIC lock around the download and the scan, so two workers do not scan at once -
        /// the second waits. The limit is therefore about how much work is queued up behind that
        /// lock rather than about parallelism, and raising it would not make this faster.
        /// </summary>
        public const int MaxConcurrent = 2;

        /// <summary>
        /// The queue itself is PlasmaShared.BoundedWorkQueue, which has nothing to do with maps
        /// and is tested on .NET 9 in Tests.Portable - dedupe, the cap, picking the next one up as
        /// a worker finishes, and stopping on the first failure. What is left here is the part
        /// that is about maps.
        /// </summary>
        private static readonly BoundedWorkQueue Queue = new BoundedWorkQueue(MaxConcurrent, Correct,
            (name, ex) => Trace.TraceWarning("LegacyMinimapUpdater: stopping after {0} on {1}: {2}",
                ex.GetType().Name, name, ex.Message));

        /// <summary>
        /// Called from the map request. Cheap and synchronous: a set lookup and possibly an
        /// enqueue. Everything that touches the disk, the database or unitsync is on a worker, and
        /// nothing here can throw into the page.
        /// </summary>
        public static void Notice(string internalName)
        {
            try
            {
                Queue.Notice(internalName);
            }
            catch (Exception ex)
            {
                // A map page that renders is worth more than a correction.
                Trace.TraceWarning("LegacyMinimapUpdater: could not queue {0}: {1}", internalName, ex.Message);
            }
        }

        /// <summary>
        /// One map, on a worker. Checks first and does nothing for the many maps that are already
        /// correct; re-registers the ones that are not. A throw here stops the queue for the life
        /// of the process - see BoundedWorkQueue for why that is the right policy.
        /// </summary>
        private static void Correct(string internalName)
        {
            if (!IsLegacy(internalName)) return;

            Trace.TraceInformation("LegacyMinimapUpdater: re-registering {0}", internalName);
            var result = Global.AutoRegistrator.ReregisterResource(internalName);
            Trace.TraceInformation("LegacyMinimapUpdater: {0}", result);
        }

        /// <summary>
        /// Whether this map's stored minimap is one of the doubly-squashed ones. Reads the stored
        /// image and asks ImageSizing, which is the same question `ZkData.Core -- minimaps` asks of
        /// every resource at once - there is no second copy of the rule here.
        /// </summary>
        private static bool IsLegacy(string internalName)
        {
            double? ratio;
            using (var db = new ZkDataContext())
            {
                ratio = db.Resources
                    .Where(x => x.InternalName == internalName)
                    .Select(x => x.MapSizeRatio)
                    .FirstOrDefault();
            }

            if (ratio == null) return false;

            var path = Path.Combine(GlobalConst.SiteDiskPath, GlobalConst.ResourceFolder,
                internalName.EscapePath() + ".minimap.jpg");
            if (!File.Exists(path)) return false;

            var size = Images.Processor.Measure(File.ReadAllBytes(path));
            return ImageSizing.LooksLikeLegacyToBytes(size, ratio);
        }
    }
}
