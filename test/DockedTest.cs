// Files docked for edits (Docked.cs): Add Mod From File's "dock for edits" list kept beside the program (a file listed
// once, whatever its case; one gone leaves the list when it's read; one that isn't a package isn't shown), and a docked
// file's code changed in its zip only (CodeChange.MoveDocked: a costume's slot, a stage's code, re-coded inside).
// Usage: DockedTest <real game folder> <scratch folder>   (copies of the real Training Stage)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace ExMoreStuff
{
    static class DockedTest
    {
        static int failures;

        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) failures++; }

        static void Zip(string path, Dictionary<string, byte[]> files)
        {
            using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create))
                foreach (var f in files)
                    using (Stream s = archive.CreateEntry(f.Key).Open()) s.Write(f.Value, 0, f.Value.Length);
        }

        static string[] Names(string zip) { using (ZipArchive archive = ZipFile.OpenRead(zip)) return archive.Entries.Select(e => e.Name).OrderBy(n => n).ToArray(); }

        static void Main(string[] args)
        {
            string real = args[0], scratch = args[1];
            if (Directory.Exists(scratch)) Directory.Delete(scratch, true);
            Directory.CreateDirectory(scratch);
            AppFolders.Root = Path.Combine(scratch, "program");

            var costumeFiles = new Dictionary<string, byte[]> { { "RYU_80.obj.emo", new byte[64] }, { "RYU_80.nml.emb", new byte[16] } };
            for (int c = 1; c <= 10; c++)
            {
                costumeFiles["RYU_80_" + c.ToString("D2") + ".col.emb"] = new byte[16];
                costumeFiles["RYU_80_" + c.ToString("D2") + ".obj.emm"] = new byte[16];
            }
            string costume = Path.Combine(scratch, "My Ryu.zip"), stage = Path.Combine(scratch, "My Stage.zip"), loose = Path.Combine(scratch, "Loose skin.zip");
            Zip(costume, costumeFiles);
            string trn = Path.Combine(real, "resource", "battle", "stage", "STG_TRN");
            Zip(stage, StagePack.Suffixes.ToDictionary(s => "STG_U12" + s, s => StagePack.Recode(File.ReadAllBytes(trn + s), "TRN", "U12")));
            Zip(loose, new Dictionary<string, byte[]> { { "skin/RYU_01.obj.emo", new byte[64] } });

            Console.WriteLine("The list");
            Check(Docked.Load().Count == 0 && !Docked.Has(costume), "nothing docked to begin with");
            Docked.Add(costume);
            Docked.Add(stage);
            Docked.Add(loose);
            Docked.Add(costume.ToUpperInvariant());
            var list = Docked.Load();
            Check(list.Count == 2 && list[0].Package == costume && list[0].Item.Fighter == "RYU" && list[0].Item.Slot == 80 && list[1].Item.Code == "U12",
                  "listed once each, in order: the costume (Ryu 80) and the stage (U12)");
            Check(list.All(r => r.Source == Docked.Source), "as docked, not installed");
            Check(Docked.Has(loose) && !list.Any(r => r.Package == loose), "a zip that isn't a package stays listed but isn't shown");

            Console.WriteLine("A code changed in the zip only");
            Installed ryu = list[0], u12 = list[1];
            Check(CodeChange.DockedProblem(ryu, "", 80) == "that's its slot already" && CodeChange.DockedProblem(ryu, "", 5) != null && CodeChange.DockedProblem(ryu, "", 79) == null,
                  "slot 80 is its own, 5 isn't a custom slot, 79 is fine");
            CodeChange.MoveDocked(ryu, "", 79);
            Check(Names(costume).All(n => n.StartsWith("RYU_79")) && Names(costume).Length == 22 && !File.Exists(costume + ".was"), "its files are RYU_79 now (all 22), nothing left beside it");
            Check(CodeChange.DockedProblem(u12, "TRN", 0) != null && CodeChange.DockedProblem(u12, "U1", 0) != null && CodeChange.DockedProblem(u12, "U13", 0) == null,
                  "TRN is the game's, U1 isn't a code, U13 is fine");
            CodeChange.MoveDocked(u12, "U13", 0);
            Item moved = Installer.Package(stage);
            Check(moved != null && moved.Code == "U13" && Names(stage).All(n => n.StartsWith("STG_U13")), "the stage is U13 now, still a whole package");
            list = Docked.Load();
            Check(list.Count == 2 && list[0].Item.Slot == 79 && list[1].Item.Code == "U13", "the list reads them as moved");

            Console.WriteLine("Gone, and taken off");
            File.Delete(costume);
            list = Docked.Load();
            Check(list.Count == 1 && list[0].Package == stage && !Docked.Has(costume), "its file gone: off the list when it's read");
            Docked.Remove(stage);
            Check(!Docked.Has(stage) && File.Exists(stage), "Remove takes it off the list; the file stays");
            Docked.Remove(loose);
            Check(!File.Exists(AppFolders.DataFile("docked.json")), "nothing docked: no list file left");

            Console.WriteLine("A mod that isn't a package: built as an add builds it, then docked (user 2026-10-14: Urban War Zone)");
            string game = Path.Combine(scratch, "game"), mod = Path.Combine(scratch, "Urban War Zone.zip"), other = Path.Combine(scratch, "Night Street.zip");
            Directory.CreateDirectory(Path.Combine(game, "patch_ae2_tu3"));
            Zip(mod, StagePack.Suffixes.ToDictionary(s => "Urban War Zone/STG_TRN" + s, s => File.ReadAllBytes(trn + s)));
            Zip(other, StagePack.Suffixes.ToDictionary(s => "STG_TRN" + s, s => File.ReadAllBytes(trn + s)));
            var urban = ModFile.Dock(game, mod, text => { });
            list = Docked.Load();
            Check(urban.Into.SequenceEqual(new[] { "stage U01" }) && urban.Problems.Count == 0 && list.Count == 1 && list[0].Item.Code == "U01", "docked as stage U01");
            Check(list[0].Package == mod && Names(mod).All(n => n.StartsWith("STG_U01")) && Installer.Package(mod) != null, "the zip added is the package now (STG_U01, user 2026-10-14)");
            Check(!Directory.Exists(AppFolders.StagePackages) || Directory.GetFiles(AppFolders.StagePackages, "Urban War Zone*.zip").Length == 0, "no copy left in Mods\\Stages");
            Check(Installer.Load(game).Count == 0 && !Directory.Exists(Path.Combine(game, "patch_ae2_tu3", "battle", "stage")), "nothing in the game");
            var night = ModFile.Dock(game, other, text => { });
            Check(night.Into.SequenceEqual(new[] { "stage U02" }), "the next one gets U02: a docked package's code is taken");

            Console.WriteLine("A preview picture as the stage picture (user 2026-10-14)");
            Installed u01 = Docked.Load().First(r => r.Item.Code == "U01");
            string previews = PerfTest.PreviewFolder(u01.Package), shot = Path.Combine(previews, "preview_55.png");
            Directory.CreateDirectory(previews);
            using (var big = new Bitmap(1920, 1080)) { using (Graphics g = Graphics.FromImage(big)) g.Clear(Color.SeaGreen); big.Save(shot, System.Drawing.Imaging.ImageFormat.Png); }
            Check(PerfTest.StagePictureShot(u01.Package) == null, "none chosen yet");
            PerfTest.UseAsStagePicture(u01.Package, u01.Item, shot);
            byte[] picture = Installer.PackagePicture(u01.Package, u01.Item);
            using (var read = picture != null ? new Bitmap(new MemoryStream(picture)) : null)
                Check(read != null && read.Width == 640 && read.Height == 360 && read.GetPixel(320, 180).ToArgb() == Color.SeaGreen.ToArgb(), "its zip's STG_U01.png is that picture at 640x360");
            Check(Names(u01.Package).Count(n => n == "STG_U01.png") == 1 && Installer.Package(u01.Package) != null, "one picture, still a whole package");
            Check(PerfTest.StagePictureShot(u01.Package) == shot, "noted as the one chosen");

            Check(u01.Package == mod && Installer.PackagePicture(mod, u01.Item).SequenceEqual(picture), "the picture is in the zip the player added");

            Console.WriteLine("Docked again: the zip is a package now, docked as it is with its picture (Add Mod From File's dock)");
            Docked.Remove(mod);
            Check(Installer.Package(mod) != null, "a package: docked as it is, nothing built");
            Docked.Add(mod);
            Check(Docked.Load().Any(r => r.Package == mod && r.Item.Code == "U01") && Installer.PackagePicture(mod, u01.Item).SequenceEqual(picture), "docked again as stage U01, its picture still in it");
            foreach (var r in Docked.Load()) Docked.Remove(r.Package);

            Console.WriteLine(failures == 0 ? "All passed" : failures + " failed");
            Environment.Exit(failures == 0 ? 0 : 1);
        }
    }
}
