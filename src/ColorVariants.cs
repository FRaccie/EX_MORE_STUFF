using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ExMoreStuff
{
    // The colors a skin doesn't bring, and the ones that look the same as another of its colors (user 2026-10-11: a
    // GameBanana skin with 3 colors had the game's own in 4-10, made for the game's model, so they looked wrong; a Sakura
    // skin's colors 1 and 2 differed by nothing anyone could see): a ready-made pattern texture copied in as every
    // texture of that color, each made color its own pattern and colors (res\patterns, made once by test\MakePatterns.cs:
    // stripes, checks, waves ...). Nothing is worked out from the skin's textures, so it costs next to nothing, and every
    // PC gets the same. Comparing colors (Look, LookSame) decodes their top levels once.
    static class ColorVariants
    {
        public const int Patterns = 9;

        static byte[] Resource(string name)
        {
            using (Stream s = typeof(ColorVariants).Assembly.GetManifestResourceStream(name))
            {
                if (s == null) throw new FileNotFoundException("the program's " + name + " is missing");
                using (var data = new MemoryStream()) { s.CopyTo(data); return data.ToArray(); }
            }
        }

        /// <summary>A .col.emb shaped like `col` (as many textures, which the materials use by number) with pattern
        /// `n` (1-9) as each of them.</summary>
        public static byte[] Patterned(byte[] col, int n)
        {
            byte[] pattern = Resource("patterns.pattern" + ((n - 1) % Patterns + 1) + ".dds");
            int count = col != null && col.Length >= 32 && Encoding.ASCII.GetString(col, 0, 4) == "#EMB" ? Math.Max(1, BitConverter.ToInt32(col, 12)) : 1;
            using (var buffer = new MemoryStream())
            using (var w = new BinaryWriter(buffer))
            {
                // #EMB: the color's own first 12 header bytes, the count, the (offset, length) list, each texture on a 64-byte boundary
                if (count > 0 && col != null && col.Length >= 12 && Encoding.ASCII.GetString(col, 0, 4) == "#EMB") w.Write(col, 0, 12);
                else w.Write(new byte[] { 0x23, 0x45, 0x4D, 0x42, 0xFE, 0xFF, 0x20, 0x00, 0x01, 0x00, 0x01, 0x00 });
                w.Write(count); w.Write(0L); w.Write(32); w.Write(0);
                w.Write(new byte[8 * count]);
                for (int i = 0; i < count; i++)
                {
                    while (buffer.Position % 64 != 0) w.Write((byte)0);
                    long at = buffer.Position;
                    w.Write(pattern);
                    long end = buffer.Position;
                    buffer.Position = 32 + 8 * i;
                    w.Write((int)(at - (32 + 8 * i)));
                    w.Write(pattern.Length);
                    buffer.Position = end;
                }
                w.Flush();
                return buffer.ToArray();
            }
        }

        static readonly Dictionary<string, List<int>> patterned = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        static byte[][] patternStarts;

        /// <summary>The colors (1-10) of a package that are pattern fills, not the mod's own textures (user 2026-10-13: the
        /// card's checker, "this costume has incomplete textures"): a color whose first texture starts as one of the
        /// patterns, read from the first few KB of each color in the zip. Kept per zip and its time.</summary>
        public static List<int> PatternColors(string zipPath, string prefix)
        {
            string key = zipPath + "|" + File.GetLastWriteTimeUtc(zipPath).Ticks;
            lock (patterned)
            {
                List<int> known;
                if (patterned.TryGetValue(key, out known)) return known;
            }
            if (patternStarts == null)
            {
                var starts = new byte[Patterns][];
                for (int n = 1; n <= Patterns; n++) { byte[] dds = Resource("patterns.pattern" + n + ".dds"); starts[n - 1] = new byte[Math.Min(1024, dds.Length)]; Array.Copy(dds, starts[n - 1], starts[n - 1].Length); }
                patternStarts = starts;
            }
            var colors = new List<int>();
            using (var zip = System.IO.Compression.ZipFile.OpenRead(zipPath))
                for (int c = 1; c <= 10; c++)
                {
                    string name = prefix + "_" + c.ToString("D2") + ".col.emb";
                    var entry = zip.Entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (entry == null) continue;
                    var head = new byte[16384];
                    int read = 0;
                    using (Stream s = entry.Open()) for (int got; read < head.Length && (got = s.Read(head, read, head.Length - read)) > 0; ) read += got;
                    if (read < 32 || Encoding.ASCII.GetString(head, 0, 4) != "#EMB") continue;
                    int table = BitConverter.ToInt32(head, 24);
                    if (table < 0 || table + 8 > read) continue;
                    int start = BitConverter.ToInt32(head, table) + table;
                    if (patternStarts.Any(p => start >= 0 && start + p.Length <= read && Same(head, start, p))) colors.Add(c);
                }
            lock (patterned) patterned[key] = colors;
            return colors;
        }

        static bool Same(byte[] data, int at, byte[] part)
        {
            for (int i = 0; i < part.Length; i++) if (data[at + i] != part[i]) return false;
            return true;
        }

        /// <summary>Pattern `n`'s picture for Ember's color gallery (256 x 384).</summary>
        public static byte[] PatternPicture(int n) { return Resource("patterns.pattern" + ((n - 1) % Patterns + 1) + ".png"); }

        /// <summary>A .col.emb as it looks: its textures' top levels, decoded (RGBA: the alpha too, which the game's
        /// materials use as the mask for their tints), to compare with LookSame.</summary>
        public static List<byte[]> Look(byte[] col)
        {
            var looks = new List<byte[]>();
            if (col == null || col.Length < 32 || Encoding.ASCII.GetString(col, 0, 4) != "#EMB") return looks;
            int count = BitConverter.ToInt32(col, 12), table = BitConverter.ToInt32(col, 24);
            for (int i = 0; i < count; i++)
            {
                int entry = table + i * 8;
                if (entry + 8 > col.Length) break;
                int start = BitConverter.ToInt32(col, entry) + entry, length = BitConverter.ToInt32(col, entry + 4);
                if (start < 0 || length < 128 || start + length > col.Length || Encoding.ASCII.GetString(col, start, 4) != "DDS ") { looks.Add(new byte[0]); continue; }
                int height = BitConverter.ToInt32(col, start + 12), width = BitConverter.ToInt32(col, start + 16);
                string fourcc = Encoding.ASCII.GetString(col, start + 84, 4);
                int block = fourcc == "DXT1" ? 8 : fourcc == "DXT3" || fourcc == "DXT5" ? 16 : 0, blocks = ((width + 3) / 4) * ((height + 3) / 4);
                if (block == 0 || start + 128 + blocks * block > start + length) { looks.Add(new byte[0]); continue; }
                var rgba = new byte[blocks * 16 * 4];
                var palette = new int[4, 3];
                var alphas = new int[8];
                for (int k = 0, at = start + 128; k < blocks; k++, at += block)
                {
                    int c = block == 8 ? at : at + 8, c0 = BitConverter.ToUInt16(col, c), c1 = BitConverter.ToUInt16(col, c + 2);
                    uint indices = BitConverter.ToUInt32(col, c + 4);
                    Unpack(c0, palette, 0);
                    Unpack(c1, palette, 1);
                    bool fourColors = block == 16 || c0 > c1;
                    for (int ch = 0; ch < 3; ch++)
                    {
                        if (fourColors) { palette[2, ch] = (2 * palette[0, ch] + palette[1, ch]) / 3; palette[3, ch] = (palette[0, ch] + 2 * palette[1, ch]) / 3; }
                        else { palette[2, ch] = (palette[0, ch] + palette[1, ch]) / 2; palette[3, ch] = 0; }
                    }
                    // the alpha: DXT5's two endpoints and 3-bit indices, DXT3's 4 bits a pixel, DXT1's see-through index 3
                    ulong alphaBits = 0;
                    if (fourcc == "DXT5")
                    {
                        int a0 = col[at], a1 = col[at + 1];
                        alphas[0] = a0; alphas[1] = a1;
                        if (a0 > a1) for (int j = 1; j < 7; j++) alphas[j + 1] = ((7 - j) * a0 + j * a1) / 7;
                        else { for (int j = 1; j < 5; j++) alphas[j + 1] = ((5 - j) * a0 + j * a1) / 5; alphas[6] = 0; alphas[7] = 255; }
                        for (int j = 0; j < 6; j++) alphaBits |= (ulong)col[at + 2 + j] << (8 * j);
                    }
                    for (int p = 0; p < 16; p++)
                    {
                        int n = (int)((indices >> (2 * p)) & 3), o = (k * 16 + p) * 4;
                        rgba[o] = (byte)palette[n, 0]; rgba[o + 1] = (byte)palette[n, 1]; rgba[o + 2] = (byte)palette[n, 2];
                        rgba[o + 3] = (byte)(fourcc == "DXT5" ? alphas[(int)((alphaBits >> (3 * p)) & 7)]
                                           : fourcc == "DXT3" ? ((col[at + p / 2] >> (4 * (p % 2))) & 15) * 17
                                           : !fourColors && n == 3 ? 0 : 255);
                    }
                }
                looks.Add(rgba);
            }
            return looks;
        }

        /// <summary>Whether two colors (Look) look the same: every texture of one nearly as the other's, its color and
        /// alpha (a copy, or a change nobody would see: under 1 pixel in 1000 visibly changed and under 2 of 255 apart on
        /// average). Texture by texture, so a change to one isn't lost among the big ones that stay (user 2026-10-13: Female
        /// Shao Kahn's 6 colors, which differ in their alpha masks, were taken for copies when it was averaged over all).</summary>
        public static bool LookSame(List<byte[]> a, List<byte[]> b)
        {
            if (a.Count == 0 || a.Count != b.Count) return false;
            for (int t = 0; t < a.Count; t++)
            {
                byte[] x = a[t], y = b[t];
                if (x.Length != y.Length) return false;
                long difference = 0;
                int changed = 0, pixels = x.Length / 4;
                for (int i = 0; i < x.Length; i += 4)
                {
                    int d = Math.Abs(x[i] - y[i]) + Math.Abs(x[i + 1] - y[i + 1]) + Math.Abs(x[i + 2] - y[i + 2]) + Math.Abs(x[i + 3] - y[i + 3]);
                    difference += d;
                    if (d > 24) changed++;
                }
                if (changed * 1000L > pixels || difference > 2L * x.Length) return false;
            }
            return true;
        }

        static void Unpack(int c, int[,] palette, int k)
        {
            palette[k, 0] = ((c >> 11) & 31) * 255 / 31;
            palette[k, 1] = ((c >> 5) & 63) * 255 / 63;
            palette[k, 2] = (c & 31) * 255 / 31;
        }
    }
}
