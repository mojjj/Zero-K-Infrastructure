using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace PlasmaShared.Imaging
{
    /// <summary>
    /// <see cref="IImageProcessor"/> on System.Drawing - what the server has always used.
    ///
    /// Deliberately a thin wrapper: every method does exactly what the call site it
    /// replaced did, in the same order, so introducing the seam changes no pixels. The
    /// interesting version of this class is the one that does not exist yet, on a
    /// cross-platform library; this one exists so that swapping to it touches one file
    /// instead of six call sites.
    /// </summary>
    public class SystemDrawingImageProcessor : IImageProcessor
    {
        public Size Measure(byte[] image)
        {
            using (var stream = new MemoryStream(image))
            using (var loaded = Image.FromStream(stream))
                return loaded.Size;
        }

        public void Save(byte[] image, string path)
        {
            using (var stream = new MemoryStream(image))
            using (var loaded = Image.FromStream(stream))
                loaded.Save(path);
        }

        public void SaveResized(byte[] image, Size target, string path)
        {
            using (var stream = new MemoryStream(image))
            using (var loaded = Image.FromStream(stream))
            using (var resized = loaded.GetResized(target.Width, target.Height, InterpolationMode.HighQualityBicubic))
                resized.Save(path);
        }
    }
}
