using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Wic = System.Windows.Media.Imaging;

namespace ExMoreStuff
{
    // Pictures from GameBanana and from mods: the kinds GDI+ reads (JPEG, PNG, GIF, BMP), and WebP, which GameBanana keeps
    // some pictures in and GDI+ can't read: that through Windows' own image readers (WIC, whose WebP reader Windows 10 and
    // 11 come with). A black card was all a WebP picture gave before (2026-10-12).
    static class Pictures
    {
        /// <summary>The picture in `bytes`. Throws when it can't be read.</summary>
        public static Image Read(byte[] bytes)
        {
            return IsWebP(bytes) ? FromWebP(bytes) : Image.FromStream(new MemoryStream(bytes));
        }

        /// <summary>The picture as a PNG file, or null when it can't be read.</summary>
        public static byte[] Png(byte[] bytes) { return Png(bytes, 0); }

        /// <summary>A picture as PNG, made smaller when its long side is over `longest` (0: as it is): a mod's own picture
        /// going into a package (user 2026-10-18: the Samus Aran mod's 6116x6514 poster made a 48 MB AGL_09.png).</summary>
        public static byte[] Png(byte[] bytes, int longest)
        {
            if (bytes == null) return null;
            try
            {
                using (Image image = Read(bytes))
                using (var png = new MemoryStream())
                {
                    int side = Math.Max(image.Width, image.Height);
                    if (longest > 0 && side > longest)
                    {
                        int w = Math.Max(1, image.Width * longest / side), h = Math.Max(1, image.Height * longest / side);
                        using (var small = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                        {
                            using (Graphics g = Graphics.FromImage(small))
                            {
                                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                                g.DrawImage(image, 0, 0, w, h);
                            }
                            small.Save(png, ImageFormat.Png);
                        }
                    }
                    else image.Save(png, ImageFormat.Png);
                    return png.ToArray();
                }
            }
            catch (Exception) { return null; }
        }

        /// <summary>The longest side of a mod's own picture in a package (its card shows it small).</summary>
        public const int PackageSide = 512;

        // "RIFF", its size, "WEBP"
        static bool IsWebP(byte[] b)
        {
            return b != null && b.Length > 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P';
        }

        // read by WIC (through WPF), its pixels copied into a GDI+ bitmap
        static Bitmap FromWebP(byte[] bytes)
        {
            Wic.BitmapSource frame = Wic.BitmapDecoder.Create(new MemoryStream(bytes), Wic.BitmapCreateOptions.None, Wic.BitmapCacheOption.OnLoad).Frames[0];
            var bgra = new Wic.FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            int width = bgra.PixelWidth, height = bgra.PixelHeight, stride = width * 4;
            var pixels = new byte[stride * height];
            bgra.CopyPixels(pixels, stride, 0);
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);   // the same byte order as Bgra32
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try { for (int y = 0; y < height; y++) Marshal.Copy(pixels, y * stride, data.Scan0 + y * data.Stride, stride); }
            finally { bitmap.UnlockBits(data); }
            return bitmap;
        }
    }
}
