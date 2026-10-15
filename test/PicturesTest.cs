// Pictures: a WebP picture (GameBanana keeps some in WebP; GDI+ can't read them) read through Windows' WIC, made into a
// PNG GDI+ reads, and shown on a Browse Mods card (CommunityPanel.Picture); a JPEG as before. Needs the internet
// (GameBanana's pictures). Usage: PicturesTest
using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace ExMoreStuff
{
    static class PicturesTest
    {
        static int failures;

        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) failures++; }

        static void Main()
        {
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
            byte[] webp = GameBanana.Bytes("https://images.gamebanana.com/img/ss/mods/220-90_5c79831d90b09.webp").Result;
            byte[] jpeg = GameBanana.Bytes("https://images.gamebanana.com/img/ss/mods/220-90_6acd116f74e18.jpg").Result;
            bool gdiReadsWebp = true;
            try { Image.FromStream(new MemoryStream(webp)).Dispose(); } catch (Exception) { gdiReadsWebp = false; }
            Check(!gdiReadsWebp, "GDI+ alone can't read the WebP (why Pictures is there)");
            using (Image image = Pictures.Read(webp)) Check(image.Width == 220 && image.Height == 132, "the WebP read: " + image.Width + "x" + image.Height);
            using (var image = (Bitmap)Pictures.Read(webp))
            {
                Color middle = image.GetPixel(110, 66);
                Check(middle.A == 255 && middle.R + middle.G + middle.B > 0, "its middle pixel is a real color: " + middle);
            }
            byte[] png = Pictures.Png(webp);
            Check(png != null && png[1] == 'P' && png[2] == 'N' && png[3] == 'G', "made a PNG (" + (png == null ? 0 : png.Length) + " bytes)");
            using (Image back = Image.FromStream(new MemoryStream(png))) Check(back.Width == 220 && back.Height == 132, "GDI+ reads that PNG");
            using (Image image = Pictures.Read(jpeg)) Check(image.Width == 220, "a JPEG still reads: " + image.Width + "x" + image.Height);
            Check(Pictures.Png(new byte[] { 1, 2, 3 }) == null, "not a picture: no PNG, no crash");
            var picture = typeof(CommunityPanel).GetMethod("Picture", BindingFlags.Static | BindingFlags.NonPublic);
            using (Image card = ((Task<Image>)picture.Invoke(null, new object[] { "https://images.gamebanana.com/img/ss/mods/220-90_5c79831d90b09.webp" })).Result)
                Check(card != null && card.Width == 220, "a Browse Mods card gets the WebP picture");
            Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
            Environment.Exit(failures == 0 ? 0 : 1);
        }
    }
}
