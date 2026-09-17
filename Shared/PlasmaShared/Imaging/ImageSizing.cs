using System;
using System.Drawing;

namespace PlasmaShared.Imaging
{
    /// <summary>
    /// Target sizes for image scaling, separated from the drawing that applies them.
    ///
    /// This is deliberately free of System.Drawing.Common. <see cref="Size"/> comes from
    /// System.Drawing.Primitives, which is part of the .NET 9 shared framework - it is the
    /// imaging types (Bitmap, Graphics, Image) that are Windows-only. So this file is
    /// linked into Tests.Portable and its arithmetic is checked there, while the pixel work
    /// stays where it is. See Shared/PlasmaShared/IMAGING-MIGRATION.md.
    ///
    /// Getting a target size wrong distorts an image without failing, which is why this
    /// half is worth pinning down first.
    /// </summary>
    public static class ImageSizing
    {
        /// <summary>
        /// Largest size with the given width-to-height ratio that fits in a
        /// <paramref name="maxSize"/> square. A ratio above 1 is landscape.
        ///
        /// The ratio is nullable because Resource.MapSizeRatio is: a map whose dimensions
        /// are unknown gets a square, which is what the original comparison chain did by
        /// accident, since both "null > 1" and "null < 1" are false.
        /// </summary>
        public static Size ScaledToFit(double? widthToHeightRatio, int maxSize)
        {
            if (widthToHeightRatio > 1) return new Size(maxSize, (int)(maxSize / widthToHeightRatio.Value));
            if (widthToHeightRatio < 1) return new Size((int)(maxSize * widthToHeightRatio.Value), maxSize);
            return new Size(maxSize, maxSize);
        }

        /// <summary>
        /// Height that keeps <paramref name="width"/> x <paramref name="height"/> in
        /// proportion when the width is set to <paramref name="targetWidth"/>.
        /// </summary>
        public static int ProportionalHeight(int width, int height, int targetWidth)
        {
            if (width <= 0) return 0;
            return (int)Math.Round((double)targetWidth / width * height);
        }

        /// <summary>
        /// The size <see cref="Utils.ToBytes"/> has always resized to.
        ///
        /// It is preserved here rather than corrected, because changing it changes the
        /// minimaps, metal maps and height maps that AutoRegistrator uploads for every new
        /// map. Note what it does: it takes an image that is ALREADY in proportion and
        /// applies that proportion a second time, so a 2:1 image comes back 4:1. For a
        /// square image it is a no-op, which is why it has survived.
        ///
        /// See ToBytesShouldBe for what it was presumably meant to do.
        /// </summary>
        public static Size LegacyToBytesSize(int width, int height)
        {
            if (height == 0) return new Size(width, 0);
            var ratio = (float)width / height;
            return ratio > 1
                ? new Size(width, (int)(height / ratio))
                : new Size((int)(width * ratio), height);
        }

        /// <summary>
        /// What <see cref="LegacyToBytesSize"/> was presumably meant to do: leave the
        /// proportions alone and bound the longest side by <paramref name="maxSize"/>.
        /// Not wired up - switching to it changes what gets uploaded, which is a decision
        /// rather than a refactor.
        /// </summary>
        public static Size BoundedByLongestSide(int width, int height, int maxSize)
        {
            if (width <= 0 || height <= 0) return new Size(0, 0);
            if (width <= maxSize && height <= maxSize) return new Size(width, height);

            return width >= height
                ? new Size(maxSize, Math.Max(1, (int)Math.Round((double)height * maxSize / width)))
                : new Size(Math.Max(1, (int)Math.Round((double)width * maxSize / height)), maxSize);
        }
    }
}
