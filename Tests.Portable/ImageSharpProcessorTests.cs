using System.Drawing;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlasmaShared.Imaging;
using SixLabors.ImageSharp;          // Save(Stream, encoder) is an extension method
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Size = System.Drawing.Size;    // the seam speaks System.Drawing.Size
using Six = SixLabors.ImageSharp;

namespace Tests.Portable
{
    /// <summary>
    /// The ImageSharp implementation of the imaging seam, exercised on .NET 9, on Linux -
    /// where System.Drawing.Common cannot run at all.
    ///
    /// That is the point of these tests. The port's imaging story is a claim until
    /// something decodes and re-encodes a real image off Windows; this does, with real
    /// bytes and real files, and the assertions are about what the server depends on:
    /// dimensions, that the output is a readable image, and that the format follows the
    /// file extension the way the old code did.
    /// </summary>
    [TestClass]
    public class ImageSharpProcessorTests
    {
        private readonly IImageProcessor processor = new ImageSharpImageProcessor();
        private string dir;

        [TestInitialize]
        public void SetUp()
        {
            dir = Path.Combine(Path.GetTempPath(), "zk-imaging-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
        }

        /// <summary>A PNG of the given size, with a recognisable non-uniform pattern.</summary>
        private static byte[] MakeImage(int width, int height)
        {
            using (var image = new Six.Image<Rgba32>(width, height))
            using (var buffer = new MemoryStream())
            {
                image.Mutate(x => x.BackgroundColor(Six.Color.CornflowerBlue));
                image.Save(buffer, new Six.Formats.Png.PngEncoder());
                return buffer.ToArray();
            }
        }

        [TestMethod]
        public void Measure_reports_the_real_dimensions()
        {
            Assert.AreEqual(new Size(640, 480), processor.Measure(MakeImage(640, 480)));
            Assert.AreEqual(new Size(1, 1), processor.Measure(MakeImage(1, 1)));
        }

        [TestMethod]
        public void Save_writes_a_file_that_reads_back_at_the_same_size()
        {
            var path = Path.Combine(dir, "original.png");
            processor.Save(MakeImage(320, 200), path);

            Assert.IsTrue(File.Exists(path), "nothing was written");
            Assert.AreEqual(new Size(320, 200), processor.Measure(File.ReadAllBytes(path)));
        }

        [TestMethod]
        public void SaveResized_writes_the_requested_size()
        {
            var path = Path.Combine(dir, "thumb.png");
            processor.SaveResized(MakeImage(1000, 250), new Size(120, 30), path);

            Assert.AreEqual(new Size(120, 30), processor.Measure(File.ReadAllBytes(path)));
        }

        [TestMethod]
        public void The_file_extension_chooses_the_format()
        {
            // The System.Drawing code relied on Image.Save(path) inferring the encoder from
            // the extension - news images are saved under their uploaded extension. The
            // replacement has to behave the same or uploads silently change format.
            var jpeg = Path.Combine(dir, "photo.jpg");
            processor.Save(MakeImage(64, 64), jpeg);

            using (var stream = File.OpenRead(jpeg))
            {
                var format = Six.Image.DetectFormat(stream);
                Assert.AreEqual("JPEG", format.Name, "a .jpg path should produce a JPEG");
            }
        }

        [TestMethod]
        public void A_thumbnail_of_a_wide_image_keeps_its_proportions()
        {
            // The composition every migrated call site performs, end to end through a real
            // encoder this time rather than a recording double.
            var source = MakeImage(1600, 400);
            var size = processor.Measure(source);
            var target = new Size(256, ImageSizing.ProportionalHeight(size.Width, size.Height, 256));

            var path = Path.Combine(dir, "wide.png");
            processor.SaveResized(source, target, path);

            Assert.AreEqual(new Size(256, 64), processor.Measure(File.ReadAllBytes(path)));
        }

        [TestMethod]
        public void Resizing_to_a_single_pixel_does_not_throw()
        {
            // ImageSizing.BoundedByLongestSide clamps to 1 rather than 0 for extreme
            // aspect ratios; this is the other half of that guard.
            var path = Path.Combine(dir, "tiny.png");
            processor.SaveResized(MakeImage(4000, 3), new Size(256, 1), path);

            Assert.AreEqual(new Size(256, 1), processor.Measure(File.ReadAllBytes(path)));
        }

        [TestCleanup]
        public void TearDown()
        {
            if (dir != null && Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
