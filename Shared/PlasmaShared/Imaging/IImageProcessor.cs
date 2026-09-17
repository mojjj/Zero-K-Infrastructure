using System.Drawing;

namespace PlasmaShared.Imaging
{
    /// <summary>
    /// The image operations the server performs on uploads, expressed without naming an
    /// imaging library.
    ///
    /// This is step 1 of Shared/PlasmaShared/IMAGING-MIGRATION.md. System.Drawing.Common
    /// is Windows-only on .NET 9, so the port needs these operations behind something that
    /// can be reimplemented - on ImageSharp, most likely. Nothing in this file names
    /// Bitmap, Image or Graphics; <see cref="Size"/> is from System.Drawing.Primitives,
    /// which is cross-platform, so this interface already compiles on .NET 9.
    ///
    /// Encoded bytes rather than a stream or an image object, because an upload is read
    /// once and then used twice - saved whole and saved again resized - and because bytes
    /// are the one currency every imaging library speaks.
    ///
    /// Resizing and saving are a single operation on purpose. Splitting them would mean
    /// encoding the resized image to bytes and then re-encoding it to the file, which for
    /// a JPEG target costs a generation of quality that the current code does not pay.
    /// </summary>
    public interface IImageProcessor
    {
        /// <summary>Dimensions of an encoded image, without decoding it fully if avoidable.</summary>
        Size Measure(byte[] image);

        /// <summary>Writes the image to a path, in the format that path's extension implies.</summary>
        void Save(byte[] image, string path);

        /// <summary>Resizes and writes in one step, in the format the path's extension implies.</summary>
        void SaveResized(byte[] image, Size target, string path);
    }
}
