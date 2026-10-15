// EX More Stuff's own 7z reader (SevenZip.cs): the LZMA and LZMA2 decoder against data packed by liblzma (Python's lzma,
// test data made by make_vectors.py: several settings, data spanning many LZMA2 chunks, incompressible data stored), then
// real 7z mods (GameBanana's 7 USF4 skins that come only as 7z, LZMA, which Windows 10's tar can't unpack): every file
// out with its CRC checked, through GameBanana.Unpack as an install does, and what the skin import finds in them; a
// damaged copy is refused, not unpacked wrong. Usage: SevenZipTest <test data folder> <folder of 7z mods> <scratch folder>
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ExMoreStuff
{
    static class SevenZipTest
    {
        static int failures;

        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) failures++; }

        static void Main(string[] args)
        {
            string vectors = args[0], mods = args[1], scratch = args[2];
            Console.WriteLine("LZMA and LZMA2 against liblzma");
            foreach (string packed in Directory.GetFiles(vectors).Where(p => p.EndsWith(".lzma") || p.EndsWith(".lzma2")).OrderBy(p => p))
            {
                string name = Path.GetFileName(packed);
                byte[] want = File.ReadAllBytes(Path.Combine(vectors, name.Substring(0, name.IndexOf('_')) + ".bin")), input = File.ReadAllBytes(packed);
                var output = new byte[want.Length];
                string problem = null;
                var watch = Stopwatch.StartNew();
                try
                {
                    if (packed.EndsWith(".lzma2")) Lzma.Lzma2(input, 0, input.Length, output);
                    else
                    {
                        var lzma = new Lzma(output);
                        lzma.SetProps(input[0]);
                        lzma.Reset();
                        lzma.Chunk(input, 13, output.Length);   // 13: settings, dictionary size, unpacked size
                    }
                }
                catch (Exception ex) { problem = ex.GetType().Name + ": " + ex.Message; }
                Check(problem == null && output.SequenceEqual(want), name + ": " + want.Length.ToString("N0") + " bytes" + (problem != null ? " (" + problem + ")" : "") +
                                                                   (want.Length > 100000 ? " in " + watch.ElapsedMilliseconds + " ms" : ""));
            }

            Console.WriteLine("GameBanana's 7z mods");
            if (Directory.Exists(scratch)) Directory.Delete(scratch, true);
            foreach (string archive in Directory.GetFiles(mods, "*.7z").OrderBy(p => p))
            {
                string name = Path.GetFileName(archive);
                try
                {
                    var watch = Stopwatch.StartNew();
                    var all = SevenZip.Read(archive, n => true);
                    long ms = watch.ElapsedMilliseconds;
                    var kept = GameBanana.Unpack(archive, Path.Combine(scratch, "work"));
                    var costumes = SkinImport.Find(kept);
                    Check(all.Count > 0 && kept.Count > 0 && costumes.Count > 0,
                          name + ": " + all.Count + " files (" + all.Sum(f => (long)f.Value.Length).ToString("N0") + " bytes) in " + ms + " ms, CRCs fine; " + kept.Count +
                          " kept; " + string.Join("; ", costumes.Select(c => c.Fighter + "_" + c.Number.ToString("D2") + ": " + c.Describe)));
                }
                catch (Exception ex) { Check(false, name + ": " + ex.GetType().Name + ": " + ex.Message); }
            }

            Console.WriteLine("A damaged copy");
            string first = Directory.GetFiles(mods, "*.7z").OrderBy(p => p).First(), damaged = Path.Combine(scratch, "damaged.7z");
            Directory.CreateDirectory(scratch);
            byte[] bytes = File.ReadAllBytes(first);
            bytes[bytes.Length / 3] ^= 0x5A;   // inside the packed data
            File.WriteAllBytes(damaged, bytes);
            string said = null;
            try { SevenZip.Read(damaged, n => true); } catch (InvalidDataException ex) { said = ex.Message; } catch (IndexOutOfRangeException ex) { said = "index: " + ex.Message; }
            Check(said != null, "refused, not unpacked wrong: " + said);
            Directory.Delete(scratch, true);

            Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
            Environment.Exit(failures == 0 ? 0 : 1);
        }
    }
}
