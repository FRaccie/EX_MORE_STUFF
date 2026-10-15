// Makes the ready-made pattern textures EX More Stuff copies in for a skin's missing colors (ColorVariants): 9
// patterns, each its own two colors, as a 128 x 128 DXT1 texture with its mip levels (res\patterns\pattern<N>.dds)
// and a 256 x 384 picture for Ember's color gallery (pattern<N>.png). Run once; the files are part of the program.
// Usage: MakePatterns <res\patterns folder>
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace ExMoreStuff
{
    static class MakePatterns
    {
        // pattern N at (u, v) in 0..1: true for its first color
        static bool On(int n, double u, double v)
        {
            switch (n)
            {
                case 1: return (int)(v * 16) % 2 == 0;                                        // stripes across
                case 2: return ((int)(u * 8) + (int)(v * 8)) % 2 == 0;                          // checker
                case 3: return (int)((u + v) * 12) % 2 == 0;                                    // diagonal stripes
                case 4: return (int)(u * 16) % 2 == 0;                                          // stripes down
                case 5: return (int)Math.Floor(v * 12 + 0.6 * Math.Sin(u * Math.PI * 8)) % 2 == 0;   // waves
                case 6: { double x = u * 8 % 1 - 0.5, y = v * 8 % 1 - 0.5; return x * x + y * y < 0.09; }   // dots
                case 7: return ((int)(u * 4) + (int)(v * 4)) % 2 == 0;                          // big checker
                case 8: return (int)((u - v + 1) * 8) % 2 == 0;                                 // diagonal the other way
                default: return u * 8 % 1 < 0.15 || v * 8 % 1 < 0.15;                           // grid
            }
        }

        // each pattern's color, against a darker shade of itself (75%): low contrast (user)
        static readonly Color[] Hues =
        {
            Color.FromArgb(200, 60, 60),     // red
            Color.FromArgb(60, 90, 200),     // blue
            Color.FromArgb(60, 170, 80),     // green
            Color.FromArgb(140, 80, 190),    // purple
            Color.FromArgb(220, 130, 40),    // orange
            Color.FromArgb(50, 180, 200),    // cyan
            Color.FromArgb(220, 110, 170),   // pink
            Color.FromArgb(220, 200, 60),    // yellow
            Color.FromArgb(170, 170, 170),   // grey
        };
        static readonly Color[,] Colors = new Color[9, 2];

        static MakePatterns()
        {
            for (int n = 0; n < 9; n++)
            {
                Colors[n, 0] = Hues[n];
                Colors[n, 1] = Color.FromArgb(Hues[n].R * 3 / 4, Hues[n].G * 3 / 4, Hues[n].B * 3 / 4);
            }
        }

        static int Pack(Color c) { return ((c.R * 31 + 127) / 255 << 11) | ((c.G * 63 + 127) / 255 << 5) | (c.B * 31 + 127) / 255; }

        static void Main(string[] args)
        {
            Directory.CreateDirectory(args[0]);
            const int size = 128;
            int levels = 0;
            for (int s = size; s >= 1; s /= 2) levels++;
            for (int n = 1; n <= 9; n++)
            {
                int a = Pack(Colors[n - 1, 0]), b = Pack(Colors[n - 1, 1]);
                using (var file = new MemoryStream())
                using (var w = new BinaryWriter(file))
                {
                    // DDS header: DXT1 with mip levels
                    w.Write(0x20534444); w.Write(124); w.Write(0x1 | 0x2 | 0x4 | 0x1000 | 0x20000 | 0x80000); w.Write(size); w.Write(size);
                    w.Write(size * size / 2); w.Write(0); w.Write(levels); w.Write(new byte[44]);
                    w.Write(32); w.Write(0x4); w.Write(0x31545844); w.Write(new byte[20]);
                    w.Write(0x8 | 0x1000 | 0x400000); w.Write(new byte[16]);
                    for (int level = 0, s = size; level < levels; level++, s = Math.Max(1, s / 2))
                        for (int by = 0; by < Math.Max(1, s / 4); by++)
                            for (int bx = 0; bx < Math.Max(1, s / 4); bx++)
                            {
                                // each block holds the pattern's two colors exactly: first color index 0, second index 1
                                uint indices = 0;
                                for (int i = 0; i < 16; i++)
                                {
                                    int x = Math.Min(s - 1, bx * 4 + i % 4), y = Math.Min(s - 1, by * 4 + i / 4);
                                    if (!On(n, (x + 0.5) / s, (y + 0.5) / s)) indices |= 1u << (2 * i);
                                }
                                int c0 = a, c1 = b;
                                if (c0 < c1) { c0 = b; c1 = a; indices ^= 0x55555555; }   // four colors need the first above the second
                                if (c0 == c1) { c1 = c0 > 0 ? c0 - 1 : 0; c0 = Math.Max(c0, 1); indices = 0; }
                                w.Write((ushort)c0); w.Write((ushort)c1); w.Write(indices);
                            }
                    w.Flush();
                    File.WriteAllBytes(Path.Combine(args[0], "pattern" + n + ".dds"), file.ToArray());
                }
                using (var picture = new Bitmap(256, 384))
                {
                    for (int y = 0; y < 384; y++)
                        for (int x = 0; x < 256; x++)
                            picture.SetPixel(x, y, Colors[n - 1, On(n, (x + 0.5) / 256, (y + 0.5) / 256) ? 0 : 1]);
                    picture.Save(Path.Combine(args[0], "pattern" + n + ".png"), ImageFormat.Png);
                }
            }
            Console.WriteLine("9 patterns in " + args[0]);
        }
    }
}
