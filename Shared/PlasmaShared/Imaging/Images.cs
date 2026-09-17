using System.IO;

namespace PlasmaShared.Imaging
{
    /// <summary>
    /// The image processor the server uses. One place to change when the implementation
    /// moves off System.Drawing, and one place for a test to substitute.
    /// </summary>
    public static class Images
    {
        public static IImageProcessor Processor = new SystemDrawingImageProcessor();

        /// <summary>Reads an upload into memory. Uploads are used twice - stored, and stored resized.</summary>
        public static byte[] ReadAll(Stream stream)
        {
            using (var buffer = new MemoryStream())
            {
                stream.CopyTo(buffer);
                return buffer.ToArray();
            }
        }
    }
}
