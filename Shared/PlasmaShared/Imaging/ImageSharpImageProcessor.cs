// SixLabors.ImageSharp is imported wholesale rather than aliased because Save(string) is
// an extension method and an alias does not bring extensions into scope. That collides
// with System.Drawing.Size, so the interface's Size is spelled out where it appears.
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace PlasmaShared.Imaging
{
    /// <summary>
    /// <see cref="IImageProcessor"/> on ImageSharp - managed, cross-platform, and the way
    /// off System.Drawing.Common, which is Windows-only on .NET 9.
    ///
    /// This is not wired up. <see cref="Images.Processor"/> still defaults to the
    /// System.Drawing implementation, because switching what the server writes to disk
    /// wants comparing on a real deployment, not a compiler. What this class buys now is
    /// that the destination exists and is proven to work: it is linked into Tests.Portable
    /// and exercised there on .NET 9, on Linux, where System.Drawing cannot run at all.
    ///
    /// Version note: ImageSharp 2.1.13. The 2.x line is Apache-2.0 and supports .NET
    /// Framework 4.8, so it can live in this codebase before the port rather than after.
    /// 3.x and later need .NET 6+ and carry the Six Labors Split License. Older 2.1.x
    /// patch levels (2.1.9 and below) have published high-severity advisories; 2.1.13 does
    /// not.
    /// </summary>
    public class ImageSharpImageProcessor : IImageProcessor
    {
        public System.Drawing.Size Measure(byte[] image)
        {
            var info = Image.Identify(image);
            return new System.Drawing.Size(info.Width, info.Height);
        }

        public void Save(byte[] image, string path)
        {
            using (var loaded = Image.Load(image))
                loaded.Save(path);
        }

        public void SaveResized(byte[] image, System.Drawing.Size target, string path)
        {
            using (var loaded = Image.Load(image))
            {
                // Bicubic to match what the System.Drawing implementation asks for with
                // InterpolationMode.HighQualityBicubic. The two will not be pixel-identical
                // - different libraries, different kernels - which is exactly why the
                // switch needs looking at rather than assuming.
                loaded.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(target.Width, target.Height),
                    Sampler = KnownResamplers.Bicubic,
                    Mode = ResizeMode.Stretch,
                }));
                loaded.Save(path);
            }
        }
    }
}
