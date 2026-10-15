// PerfTest.Cutout on a fighters' layer the helper saved from the game (a photo run): P1 cut out at 256 x 384.
// Usage: CutoutCheck <layer.png> <out.png>
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace ExMoreStuff
{
    static class CutoutCheck
    {
        static void Main(string[] args)
        {
            using (var layer = new Bitmap(new MemoryStream(File.ReadAllBytes(args[0]))))
            using (Bitmap picture = args.Length > 2 && args[2] == "full" ? PerfTest.FullSize(layer) : PerfTest.Cutout(layer))
                picture.Save(args[1], ImageFormat.Png);
        }
    }
}
