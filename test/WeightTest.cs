// The size check (Weight.cs): a stage's unpacked size from its #EMZ headers, a costume's bones, model and colors, each
// against the game's biggest; a warning past the player's line (4 and 3 by default, in settings.json), read from the
// game folder or from a package's zip; the warnings for what an install put in.
// Usage: WeightTest <scratch folder>
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace ExMoreStuff
{
    static class WeightTest
    {
        static int failures;

        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) failures++; }

        // a packed stage file's 16-byte header saying how big it is unpacked (what the size check reads)
        static void Emz(string path, uint unpacked)
        {
            var head = new byte[16];
            head[0] = (byte)'#'; head[1] = (byte)'E'; head[2] = (byte)'M'; head[3] = (byte)'Z';
            BitConverter.GetBytes(unpacked).CopyTo(head, 8);
            File.WriteAllBytes(path, head.Concat(new byte[64]).ToArray());
        }

        // a model whose skeleton (at byte 32) has `bones` bones
        static void Emo(string path, ushort bones)
        {
            var data = new byte[64];
            data[0] = (byte)'#'; data[1] = (byte)'E'; data[2] = (byte)'M'; data[3] = (byte)'O';
            BitConverter.GetBytes(32u).CopyTo(data, 16);
            BitConverter.GetBytes(bones).CopyTo(data, 32);
            File.WriteAllBytes(path, data);
        }

        static void Costume(string folder, string prefix, ushort bones, long firstColor)
        {
            Emo(Path.Combine(folder, prefix + ".obj.emo"), bones);
            File.WriteAllBytes(Path.Combine(folder, prefix + ".nml.emb"), new byte[16]);
            for (int c = 1; c <= 10; c++)
            {
                using (var f = File.Create(Path.Combine(folder, prefix + "_" + c.ToString("D2") + ".col.emb"))) f.SetLength(c == 1 ? firstColor : 1000);
                File.WriteAllBytes(Path.Combine(folder, prefix + "_" + c.ToString("D2") + ".obj.emm"), new byte[16]);
            }
        }

        static void Main(string[] args)
        {
            string scratch = args[0];
            if (Directory.Exists(scratch)) Directory.Delete(scratch, true);
            Directory.CreateDirectory(scratch);
            AppFolders.Root = scratch;
            string game = Path.Combine(scratch, "game");
            string stages = Path.Combine(Game.PatchFolder(game), "battle", "stage"), ryu = Path.Combine(Game.PatchFolder(game), "battle", "chara", "RYU");
            Directory.CreateDirectory(stages);
            Directory.CreateDirectory(ryu);

            Console.WriteLine("Lines");
            Check(Weight.StageLine == 4 && Weight.CostumeLine == 3, "4 for stages and 3 for costumes by default");

            Console.WriteLine("Stages");
            var big = new Item { Type = "stage", Code = "U50" };
            var small = new Item { Type = "stage", Code = "U51" };
            Emz(Path.Combine(stages, "STG_U50.emz"), 100000000);
            Emz(Path.Combine(stages, "STG_U50.tex.emz"), 30000000);   // 130 MB unpacked: 4.3 times Blast Furnace
            Emz(Path.Combine(stages, "STG_U51.emz"), 60000000);       // 2 times, no textures file
            string warning = Weight.InGame(game, big, "Big Stage");
            Check(warning == "Big Stage is about 4.3× the size of the biggest SF4 stage, so it may not perform well.", "a stage over the line: " + warning);
            Check(Weight.InGame(game, small, "Small Stage") == null, "one under it: no warning");
            Check(Weight.InGame(game, new Item { Type = "stage", Code = "U52" }, "Gone") == null, "a stage whose files aren't there: no warning");

            Console.WriteLine("Costumes");
            Costume(ryu, "RYU_80", 1600, 1000);            // 3.2 times RYU_05's bones
            Costume(ryu, "RYU_81", 300, 22000000);         // a 22 MB color: 3.1 times BLK_07's
            Costume(ryu, "RYU_82", 600, 6000000);          // bigger than the game's, under the line
            warning = Weight.InGame(game, new Item { Type = "costume", Fighter = "RYU", Slot = 80 }, "Ryu Slot 80");
            Check(warning == "Ryu Slot 80 has 1,600 bones, about 3.2× the most an SF4 costume has, so it may not perform well.", "too many bones: " + warning);
            warning = Weight.InGame(game, new Item { Type = "costume", Fighter = "RYU", Slot = 81 }, "Ryu Slot 81");
            Check(warning == "Ryu Slot 81's colors are up to about 3.1× the size of the biggest SF4 color, so it may not perform well.", "a big color: " + warning);
            Check(Weight.InGame(game, new Item { Type = "costume", Fighter = "RYU", Slot = 82 }, "Ryu Slot 82") == null, "a costume under the line: no warning");

            Console.WriteLine("Packages");
            string zip = Path.Combine(scratch, "big.zip");
            using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(Path.Combine(stages, "STG_U50.emz"), "STG_U50.emz");
                archive.CreateEntryFromFile(Path.Combine(stages, "STG_U50.tex.emz"), "STG_U50.tex.emz");
            }
            Check(Weight.InZip(zip, big, "Big Stage") == "Big Stage is about 4.3× the size of the biggest SF4 stage, so it may not perform well.", "read from a package's zip");
            string costumeZip = Path.Combine(scratch, "ryu80.zip");
            using (ZipArchive archive = ZipFile.Open(costumeZip, ZipArchiveMode.Create))
                foreach (string f in Directory.GetFiles(ryu, "RYU_80*")) archive.CreateEntryFromFile(f, Path.GetFileName(f));
            Check((Weight.InZip(costumeZip, new Item { Type = "costume", Fighter = "RYU", Slot = 80 }, "Ryu Slot 80") ?? "").Contains("1,600 bones"), "a model's bones read from a zip");
            Check(Weight.InZip(Path.Combine(scratch, "missing.zip"), big, "Big Stage") == null, "a zip that isn't there: no warning");

            Console.WriteLine("Where the lines come from (tests only: players don't set them, user 2026-10-14)");
            Check(Weight.StageLine == 4 && Weight.Source(true) == "4 by default", "none yet: 4 by default");
            Weight.FromPcCheck(297);
            Check(Weight.StageLine == 4.5 && Weight.Source(true) == "from your tests", "the PC check at 297 fps starts it at 4.5");
            Weight.FromPcCheck(120);
            Check(Weight.StageLine == 2, "at 120 fps, 2");
            Check(Weight.InGame(game, small, "Small Stage") == null, "a 2x stage at the line: no warning");
            Weight.Learn(true, 6.23, true);
            Check(Weight.StageLine == 6.3, "a 6.23x stage at full speed raises it to 6.3 (" + Weight.StageLine + ")");
            Weight.Learn(true, 8, false);
            Weight.Learn(true, 2, true);
            Check(Weight.StageLine == 6.3, "a fail above it and a pass under it leave it");
            Weight.Learn(true, 3.05, false);
            Check(Weight.StageLine == 3, "a 3.05x stage that fails lowers it to 3 (" + Weight.StageLine + ")");
            Weight.Learn(true, 3, false);
            Check(Weight.StageLine == 2.9, "a 3x one to 2.9 (" + Weight.StageLine + ")");
            Check(Weight.InGame(game, big, "Big Stage") != null && Weight.Lockable(game, big), "a 4.3x stage past it: a \"!\", and a failed test would lock it");
            Weight.Learn(false, 3.4, true);
            Check(Weight.CostumeLine == 3.4 && Weight.StageLine == 2.9, "costumes have their own line: a 3.4x costume at full speed raises only it");

            Console.WriteLine("After an install");
            File.Delete(Path.Combine(stages, "STG_U50.emz"));
            File.Delete(Path.Combine(stages, "STG_U50.tex.emz"));
            var before = Weight.Keys(game);
            Installer.InstallPackage(game, big, zip, "file");
            var notes = Weight.New(game, before);
            Check(notes.Count == 1 && notes[0].StartsWith("Stage U50 is about 4.3×"), "what just went in is said: " + string.Join(" | ", notes));
            Check(Weight.New(game, Weight.Keys(game)).Count == 0, "and nothing that was in before");

            Console.WriteLine(failures == 0 ? "All passed" : failures + " failed");
            Environment.Exit(failures == 0 ? 0 : 1);
        }
    }
}
