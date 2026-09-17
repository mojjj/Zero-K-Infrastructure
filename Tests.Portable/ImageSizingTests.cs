using System.Drawing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlasmaShared.Imaging;

namespace Tests.Portable
{
    /// <summary>
    /// Target sizes for image scaling. A wrong size here distorts an image silently rather
    /// than failing, so this arithmetic is worth pinning before the imaging library is
    /// swapped underneath it.
    /// </summary>
    [TestClass]
    public class ImageSizingTests
    {
        // ---- ScaledToFit: map thumbnails ------------------------------------------

        [TestMethod]
        public void A_landscape_image_fills_the_box_horizontally()
        {
            Assert.AreEqual(new Size(200, 100), ImageSizing.ScaledToFit(2.0, 200));
        }

        [TestMethod]
        public void A_portrait_image_fills_the_box_vertically()
        {
            Assert.AreEqual(new Size(100, 200), ImageSizing.ScaledToFit(0.5, 200));
        }

        [TestMethod]
        public void A_square_image_fills_the_box_exactly()
        {
            Assert.AreEqual(new Size(200, 200), ImageSizing.ScaledToFit(1.0, 200));
        }

        [TestMethod]
        public void Scaling_truncates_rather_than_rounds()
        {
            // (int) cast, as the original did. 200/3 is 66.67, which becomes 66.
            Assert.AreEqual(new Size(200, 66), ImageSizing.ScaledToFit(3.0, 200));
        }

        [TestMethod]
        public void An_unknown_ratio_gets_a_square()
        {
            // Resource.MapSizeRatio is nullable. The original comparison chain fell through
            // to the square branch for null, because both "null > 1" and "null < 1" are
            // false. Pinned so the nullable signature keeps that behaviour.
            Assert.AreEqual(new Size(200, 200), ImageSizing.ScaledToFit(null, 200));
        }

        // ---- ProportionalHeight: news thumbnails ----------------------------------

        [TestMethod]
        public void Thumbnail_height_keeps_the_proportions()
        {
            Assert.AreEqual(60, ImageSizing.ProportionalHeight(240, 120, 120));
            Assert.AreEqual(120, ImageSizing.ProportionalHeight(120, 120, 120));
        }

        [TestMethod]
        public void Thumbnail_height_rounds_to_nearest()
        {
            // Math.Round, unlike ScaledToFit's truncation. 120/7*3 = 51.43 -> 51.
            Assert.AreEqual(51, ImageSizing.ProportionalHeight(7, 3, 120));
        }

        [TestMethod]
        public void A_zero_width_image_does_not_divide_by_zero()
        {
            Assert.AreEqual(0, ImageSizing.ProportionalHeight(0, 100, 120));
        }

        // ---- LegacyToBytesSize: the rule AutoRegistrator uploads through ----------

        [TestMethod]
        public void The_legacy_rule_leaves_a_square_image_alone()
        {
            // Which is why the defect below has gone unnoticed: unitsync hands out square
            // minimaps, and the fault only shows once FixAspectRatio has corrected them.
            Assert.AreEqual(new Size(512, 512), ImageSizing.LegacyToBytesSize(512, 512));
        }

        [TestMethod]
        public void The_legacy_rule_applies_the_aspect_ratio_a_second_time()
        {
            // CHARACTERISATION, NOT APPROVAL. UnitSync.GetMinimap already calls
            // FixAspectRatio, so the image arriving here is in proportion. Applying the
            // ratio again squashes it: a 2:1 image is uploaded as 4:1.
            Assert.AreEqual(new Size(1024, 256), ImageSizing.LegacyToBytesSize(1024, 512));
            Assert.AreEqual(new Size(128, 512), ImageSizing.LegacyToBytesSize(256, 512));
        }

        [TestMethod]
        public void The_legacy_rule_ignores_the_requested_maximum_size()
        {
            // There is no size parameter here because ToBytes never used the one it takes.
            // AutoRegistrator passes 256 as "max size of minimap to be sent to server" and
            // a 1024-wide minimap stays 1024 wide.
            Assert.AreEqual(1024, ImageSizing.LegacyToBytesSize(1024, 1024).Width);
        }

        // ---- BoundedByLongestSide: what it was presumably meant to do -------------

        [TestMethod]
        public void The_intended_rule_keeps_proportions_and_bounds_the_longest_side()
        {
            Assert.AreEqual(new Size(256, 128), ImageSizing.BoundedByLongestSide(1024, 512, 256));
            Assert.AreEqual(new Size(128, 256), ImageSizing.BoundedByLongestSide(512, 1024, 256));
            Assert.AreEqual(new Size(256, 256), ImageSizing.BoundedByLongestSide(1024, 1024, 256));
        }

        [TestMethod]
        public void The_intended_rule_does_not_enlarge_a_small_image()
        {
            Assert.AreEqual(new Size(64, 32), ImageSizing.BoundedByLongestSide(64, 32, 256));
        }

        [TestMethod]
        public void The_intended_rule_never_collapses_a_side_to_zero()
        {
            // An extreme panorama would otherwise round its short side to 0, which is not a
            // valid bitmap.
            Assert.AreEqual(1, ImageSizing.BoundedByLongestSide(10000, 3, 256).Height);
        }
    }
}
