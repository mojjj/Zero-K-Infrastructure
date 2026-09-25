using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PlasmaShared.Imaging
{
    /// <summary>
    /// Turns the tightly packed RGB24 buffers <see cref="PixelBuffers"/> produces into GDI+
    /// Bitmaps.
    ///
    /// **Deliberately the only System.Drawing left in the unitsync path**, and deliberately not
    /// linked into any .NET 9 project. UnitSync still returns Bitmaps because Map, ToBytes and
    /// AutoRegistrator all speak them; what has moved out is the arithmetic, which is now tested
    /// on both stacks. This is the seam where the remaining work will cut.
    ///
    /// Row padding and channel order are the two things GDI+ will not tell you about until an
    /// image looks wrong, so both are handled here, once, with names on them.
    /// </summary>
    public static class GdiPixelBridge
    {
        /// <summary>
        /// A 24-bit bitmap from tightly packed RGB bytes. The buffer is copied row by row, because
        /// GDI+ pads each row to a four-byte boundary and a straight copy would shear the image.
        /// </summary>
        public static Bitmap FromRgb24(byte[] pixels, Size size)
        {
            if (pixels == null) throw new ArgumentNullException("pixels");
            if (pixels.Length < size.Width * size.Height * 3)
                throw new ArgumentException("buffer is smaller than " + size.Width + "x" + size.Height, "pixels");

            // GDI+ wants BGR; a copy is taken so the caller's buffer is not reordered underneath it.
            var bgr = PixelBuffers.SwapRedAndBlue((byte[])pixels.Clone());

            var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb);
            var data = bitmap.LockBits(new Rectangle(Point.Empty, size), ImageLockMode.WriteOnly, bitmap.PixelFormat);
            try
            {
                var rowBytes = size.Width * 3;
                for (var y = 0; y < size.Height; y++)
                    Marshal.Copy(bgr, y * rowBytes, IntPtr.Add(data.Scan0, y * data.Stride), rowBytes);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
            return bitmap;
        }

        /// <summary>Copies a native buffer out, so nothing downstream holds a pointer unitsync owns.</summary>
        public static byte[] CopyFrom(IntPtr source, int length)
        {
            var buffer = new byte[length];
            Marshal.Copy(source, buffer, 0, length);
            return buffer;
        }
    }
}
