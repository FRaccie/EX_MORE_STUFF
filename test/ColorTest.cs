// New colors (30-99) in a made-up game folder: a color package (RYU_01_30) recognized and put in as it is, named; a
// mod holding a color in a folder without its material, which borrows color 1's and takes the next free number (31);
// a color of a custom costume (SKR_10_40); a color moved to another number in Package codes (its zip rewritten), the
// refusals; deleting one; colors not counted as costume slots. Usage: ColorTest <real game folder> <scratch folder>
// (build_test.bat ColorTest)
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace ExMoreStuff
{
    static class ColorTest
    {
        static int failures;

        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) failures++; }

        static string[] Names(string zip)
        {
            using (ZipArchive z = ZipFile.OpenRead(zip)) return z.Entries.Select(e => e.Name).OrderBy(n => n).ToArray();
        }

        static string RealFile(string real, string fighter, string name)
        {
            foreach (string root in new[] { "patch_ae2_tu2", "patch_ae2", @"dlc\04_ae2", @"dlc\03_character_free", "resource" })
            {
                string path = Path.Combine(real, root, "battle", "chara", fighter, name);
                if (File.Exists(path)) return path;
            }
            throw new FileNotFoundException(name);
        }

        static void Main(string[] args)
        {
            string real = args[0], game = Path.Combine(args[1], "game"), packages = Path.Combine(args[1], "packages");
            if (Directory.Exists(args[1])) Directory.Delete(args[1], true);
            AppFolders.Root = Path.Combine(args[1], "program");   // its Mods\ here, not beside the test
            string patch = Path.Combine(game, "patch_ae2_tu3"), chara = Path.Combine(patch, "battle", "chara");
            Directory.CreateDirectory(patch);
            Directory.CreateDirectory(packages);
            // the game's Ryu color 1 material, which a color without one borrows
            string emm1 = RealFile(real, "RYU", "RYU_01_01.obj.emm");
            Directory.CreateDirectory(Path.Combine(game, "resource", "battle", "chara", "RYU"));
            File.Copy(emm1, Path.Combine(game, "resource", "battle", "chara", "RYU", "RYU_01_01.obj.emm"));
            byte[] col = File.ReadAllBytes(RealFile(real, "RYU", "RYU_01_02.col.emb")), emm = File.ReadAllBytes(RealFile(real, "RYU", "RYU_01_02.obj.emm"));

            Console.WriteLine("A color package");
            string zip = Path.Combine(packages, "Navy Ryu.zip");
            using (ZipArchive z = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                using (Stream s = z.CreateEntry("RYU_01_30.col.emb").Open()) s.Write(col, 0, col.Length);
                using (Stream s = z.CreateEntry("RYU_01_30.obj.emm").Open()) s.Write(emm, 0, emm.Length);
                using (Stream s = z.CreateEntry("RYU_01_30.png").Open()) s.WriteByte(7);
            }
            Item item = Installer.Package(zip);
            Check(item != null && item.IsColor && !item.IsCostume && item.Fighter == "RYU" && item.Slot == 1 && item.Color == 30, "recognized: Ryu costume 1, color 30");
            Check(item.Prefix == "RYU_01_30" && item.Key == "color:RYU:1:30" && item.Title.StartsWith("Costume 1 color 30") && item.Problem() == null, "its prefix, key and title");
            Check(Item.FromJson(item.ToJson()).Key == item.Key, "its listing round-trips");
            Check(Seats.Warning(item) == null, "no seat warning: colors aren't shared");
            Installer.InstallPackage(game, item, zip, "file");
            Installer.Rename(game, item.Key, "Navy");
            string ryu = Path.Combine(chara, "RYU");
            Check(File.Exists(Path.Combine(ryu, "RYU_01_30.col.emb")) && File.Exists(Path.Combine(ryu, "RYU_01_30.obj.emm")) && File.Exists(Path.Combine(ryu, "RYU_01_30.png")),
                  "its files beside the costume's");
            Check(File.ReadAllText(Path.Combine(ryu, "RYU_01_30.txt")) == "Navy", "its name for Ember: RYU_01_30.txt");
            using (ZipArchive z = ZipFile.Open(Path.Combine(packages, "bad.zip"), ZipArchiveMode.Create))
                using (Stream s = z.CreateEntry("RYU_01_31.col.emb").Open()) s.Write(col, 0, col.Length);
            Check(Installer.Package(Path.Combine(packages, "bad.zip")) == null, "a package without its material isn't a complete package");

            Console.WriteLine("A mod with a color in a folder, without its material");
            var files = new List<KeyValuePair<string, byte[]>> { new KeyValuePair<string, byte[]>("Ryu colors/RYU_01_30.col.emb", col) };
            var found = ModFile.FindColors(files);
            Check(found.Count == 1 && found[0].Number == 30 && found[0].Emm == null, "found: color 30, no material");
            var result = ModFile.InstallFiles(game, files, "file:test", "Navy Again", "me", "", new byte[] { 1, 2 }, s => { });
            Check(result.Problems.Count == 0 && result.Into.SequenceEqual(new[] { "Ryu costume 1 color 31" }), "it goes in color 31, 30 being taken (" + string.Join("; ", result.Problems.Concat(result.Into)) + ")");
            Check(File.ReadAllBytes(Path.Combine(ryu, "RYU_01_31.obj.emm")).SequenceEqual(File.ReadAllBytes(emm1)), "its material: the game's color 1's");
            Check(ModFile.FreeColor(game, "RYU", 1, 30) == 32 && ModFile.FreeColor(game, "RYU", 1, 50) == 50 && ModFile.FreeColor(game, "RYU", 2, 30) == 30, "free colors per costume");

            Console.WriteLine("A color of a custom costume");
            files = new List<KeyValuePair<string, byte[]>>
            {
                new KeyValuePair<string, byte[]>("x/SKR_10_40.col.emb", col), new KeyValuePair<string, byte[]>("x/SKR_10_40.obj.emm", emm),
                new KeyValuePair<string, byte[]>("x/SKR_10_40.png", new byte[] { 9 }),
            };
            result = ModFile.InstallFiles(game, files, "file:skr", "Worker Blue", "me", "", null, s => { });
            Installed skr = Installer.Load(game).Single(r => r.Item.Fighter == "SKR");
            Check(result.Problems.Count == 0 && skr.Item.IsColor && skr.Item.Slot == 10 && skr.Item.Color == 40 && skr.Item.Title.StartsWith("Slot 10 color 40"), "Sakura 10 color 40");
            Check(File.ReadAllBytes(Path.Combine(chara, "SKR", "SKR_10_40.png")).SequenceEqual(new byte[] { 9 }), "with its own picture");

            Console.WriteLine("A pack: its colors of one costume in one zip, one install (user 2026-10-18)");
            files = new List<KeyValuePair<string, byte[]>>();
            foreach (int n in new[] { 30, 31, 32 })
            {
                files.Add(new KeyValuePair<string, byte[]>("p/KEN_02_" + n + ".col.emb", col));
                files.Add(new KeyValuePair<string, byte[]>("p/KEN_02_" + n + ".obj.emm", emm));
            }
            result = ModFile.InstallFiles(game, files, "file:kenpack", "Ken Pack", "me", "", null, s => { });
            var ken = Installer.Load(game).Where(r => r.Item.Fighter == "KEN").OrderBy(r => r.Item.Color).ToList();
            Check(result.Problems.Count == 0 && result.Into.SequenceEqual(new[] { "Ken costume 2 colors 30, 31, 32" }), "one install: " + string.Join("; ", result.Problems.Concat(result.Into)));
            Check(ken.Select(r => r.Item.Color).SequenceEqual(new[] { 30, 31, 32 }) && ken.Select(r => r.Package).Distinct().Count() == 1 &&
                  Path.GetFileName(ken[0].Package) == "Ken Pack - KEN_02 colors.zip", "three colors, one zip: " + Path.GetFileName(ken[0].Package));
            Check(Directory.GetFiles(Path.GetDirectoryName(ken[0].Package), "*.zip").Length == 1, "no zip a color in Mods");
            Check(ken.All(r => r.Files.All(f => Path.GetFileName(f).StartsWith(r.Item.Prefix + "."))) && ken.All(r => r.Files.Count == 3),
                  "each color holds only its own files (and its name)");
            Check(Installer.Packages(ken[0].Package).Select(i => i.Color).SequenceEqual(new[] { 30, 31, 32 }), "its zip read as its three colors");
            Check(!PackagesPanel.RecyclesZip(game, ken[0]), "deleting one color leaves the zip the others use");
            result = ModFile.InstallFiles(game, files, "file:kenpack", "Ken Pack", "me", "", null, s => { });
            ken = Installer.Load(game).Where(r => r.Item.Fighter == "KEN").ToList();
            Check(result.Problems.Count == 0 && ken.Count == 3 && ken.Select(r => r.Package).Distinct().Count() == 1 &&
                  Directory.GetFiles(Path.GetDirectoryName(ken[0].Package), "*.zip").Length == 1, "put in again: the same three colors, the same zip");

            Console.WriteLine("Package codes: a pack moves together (one up: each lands on a number freed by then)");
            foreach (Installed r in ken.OrderByDescending(r => r.Item.Color).ToList())
                CodeChange.Apply(game, Installer.Load(game).First(x => x.Item.Key == r.Item.Key), null, r.Item.Color + 1);
            ken = Installer.Load(game).Where(r => r.Item.Fighter == "KEN").OrderBy(r => r.Item.Color).ToList();
            Check(ken.Select(r => r.Item.Color).SequenceEqual(new[] { 31, 32, 33 }) && ken.Select(r => r.Package).Distinct().Count() == 1, "now 31-33, one zip");
            Check(Names(ken[0].Package).SequenceEqual(new[] { "KEN_02_31.col.emb", "KEN_02_31.obj.emm", "KEN_02_32.col.emb", "KEN_02_32.obj.emm", "KEN_02_33.col.emb", "KEN_02_33.obj.emm" }),
                  "its zip renamed: " + string.Join(", ", Names(ken[0].Package)));
            string kenFolder = Path.Combine(chara, "KEN");
            Check(!File.Exists(Path.Combine(kenFolder, "KEN_02_30.col.emb")) && File.Exists(Path.Combine(kenFolder, "KEN_02_33.col.emb")), "in the game as 31-33");

            Console.WriteLine("A pack added before, a zip a color: merged into one");
            var chun = new List<string>();
            foreach (int n in new[] { 30, 31 })
            {
                var c = new Item { Id = "file:chun" + n, Type = "color", Fighter = "CHN", Slot = 1, Color = n, Code = "", Name = "Chun Pack", Author = "", Version = "" };
                Directory.CreateDirectory(AppFolders.PackagesFor(c));
                string z1 = Path.Combine(AppFolders.PackagesFor(c), "Chun Pack - " + c.Prefix + ".zip");
                ModFile.Write(new List<KeyValuePair<string, byte[]>> { new KeyValuePair<string, byte[]>(c.Prefix + ".col.emb", col), new KeyValuePair<string, byte[]>(c.Prefix + ".obj.emm", emm) }, z1);
                Installer.InstallPackage(game, c, z1, "file");
                chun.Add(z1);
            }
            var gone = new List<string>();
            Check(Installer.MergeColorZips(game, p => { gone.Add(p); File.Delete(p); }) == 1, "one pack merged");
            var chn = Installer.Load(game).Where(r => r.Item.Fighter == "CHN").ToList();
            Check(chn.Count == 2 && chn.Select(r => r.Package).Distinct().Count() == 1 && Path.GetFileName(chn[0].Package) == "Chun Pack - CHN_01 colors.zip" &&
                  Names(chn[0].Package).SequenceEqual(new[] { "CHN_01_30.col.emb", "CHN_01_30.obj.emm", "CHN_01_31.col.emb", "CHN_01_31.obj.emm" }), "both colors in one zip, the list pointing there");
            Check(gone.Count == 2 && chun.All(z => !File.Exists(z)), "the old zips gone");
            Check(Installer.MergeColorZips(game, p => File.Delete(p)) == 0 && ken.All(r => File.Exists(r.Package)), "nothing merged again; Ken's pack untouched");

            Console.WriteLine("Colors aren't costume slots");
            files = new List<KeyValuePair<string, byte[]>> { new KeyValuePair<string, byte[]>("y/RYU_84_30.col.emb", col), new KeyValuePair<string, byte[]>("y/RYU_84_30.obj.emm", emm) };
            ModFile.InstallFiles(game, files, "file:y", "On 84", "me", "", null, s => { });
            Check(SkinImport.FreeSlot(game, "RYU") == 84, "a color on Ryu 84 leaves slot 84 free for a costume");

            Console.WriteLine("Package codes: another number");
            Installed navy = Installer.Load(game).Single(r => r.Item.Key == "color:RYU:1:30");
            Check(CodeChange.Problem(game, navy, null, 31) != null && CodeChange.Problem(game, navy, null, 29) != null && CodeChange.Problem(game, navy, null, 30) != null,
                  "refused: 31 (taken), 29 (not a new color), 30 (its own)");
            Check(CodeChange.Problem(game, navy, null, 35) == null, "35 is fine");
            CodeChange.Apply(game, navy, null, 35);
            Check(Names(zip).SequenceEqual(new[] { "RYU_01_35.col.emb", "RYU_01_35.obj.emm", "RYU_01_35.png" }), "its zip rewritten as RYU_01_35");
            Check(!File.Exists(Path.Combine(ryu, "RYU_01_30.col.emb")) && File.Exists(Path.Combine(ryu, "RYU_01_35.col.emb")) &&
                  File.ReadAllText(Path.Combine(ryu, "RYU_01_35.txt")) == "Navy", "in the game as color 35, its name kept");

            Console.WriteLine("Delete");
            navy = Installer.Load(game).Single(r => r.Item.Key == "color:RYU:1:35");
            Check(PackagesPanel.RecyclesZip(game, navy), "EX More Stuff's copy of its zip goes to the Recycle Bin with it");
            Check(PackagesPanel.DeleteQuestion(game, navy).EndsWith("\n\nThe original mod file you added is untouched."), "the question says the file added stays");
            navy.Theirs = true;
            Check(!PackagesPanel.RecyclesZip(game, navy), "a zip that's the player's own file (picked inside Mods) is never recycled");
            navy.Theirs = false;
            string movedZip = Path.Combine(args[1], "moved.zip");
            File.Move(zip, movedZip);
            Check(!PackagesPanel.RecyclesZip(game, navy), "a moved zip is left alone");
            string navyId = navy.Item.Id;
            navy.Item.Id = "gamebanana:123:RYU0135";
            Check(PackagesPanel.DeleteQuestion(game, navy).EndsWith("\n\nYou can download it again from Browse Mods."), "a download: it can be downloaded again");
            navy.Item.Id = navyId;
            PackagesPanel.PurgeForGood(game, navy);
            Check(!File.Exists(Path.Combine(ryu, "RYU_01_35.col.emb")) && !File.Exists(Path.Combine(ryu, "RYU_01_35.txt")) &&
                  !Installer.Load(game).Any(r => r.Item.Key == "color:RYU:1:35") && File.Exists(movedZip), "out of the game (its name file too) and off the list; the moved zip kept");
            Check(File.Exists(Path.Combine(ryu, "RYU_01_31.col.emb")), "the other color stays");

            Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
            Environment.Exit(failures == 0 ? 0 : 1);
        }
    }
}
