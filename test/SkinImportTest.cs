// GameBanana skins into costume packages, without the internet: a real skin's files (Dark Hado Blanka ver1, replacing
// BLK_01 with a model and color 2) alone and as several versions (they become colors 1, 2 ...), a True Ryu-like layout
// (model variations, a numbered color set, materials in another folder), a mod in a code of its own with its pictures;
// the package built with the real game's files (read only); shared codes (given, own, first upload wins, personal);
// unpacking a zip holding a 7z (Windows' tar). Usage: SkinImportTest <real game folder> <skin zip> <scratch folder>
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace ExMoreStuff
{
    static class SkinImportTest
    {
        static int failures;

        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) failures++; }

        static byte[] Entry(string zip, string name)
        {
            using (ZipArchive z = ZipFile.OpenRead(zip))
            {
                var e = z.Entries.FirstOrDefault(x => x.Name == name);
                if (e == null) return null;
                using (Stream s = e.Open())
                using (var data = new MemoryStream()) { s.CopyTo(data); return data.ToArray(); }
            }
        }

        static string GameFile(string game, string name)
        {
            foreach (string root in new[] { "patch_ae2_tu2", "patch_ae2", @"dlc\04_ae2", @"dlc\03_character_free", "resource" })
            {
                string path = Path.Combine(game, root, "battle", "chara", name.Substring(0, 3), name);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        static bool Same(byte[] a, byte[] b) { return a != null && b != null && a.SequenceEqual(b); }

        static KeyValuePair<string, byte[]> F(string path, byte[] data) { return new KeyValuePair<string, byte[]>(path, data); }

        // a skin that brings its fighter's voice (Juri gótica: JRI_E.csb and JRI_J.csb beside the costume): kept with the costume,
        // under its own name
        static void Voices(string game, string scratch)
        {
            Console.WriteLine("A skin with its fighter's voice");
            var files = new List<KeyValuePair<string, byte[]>>
            {
                new KeyValuePair<string, byte[]>("skin/RYU_02.obj.emo", File.ReadAllBytes(Directory.GetFiles(Path.Combine(game, "resource", "battle", "chara", "RYU"), "RYU_02.obj.emo")[0])),
                new KeyValuePair<string, byte[]>("skin/RYU_E.csb", System.Text.Encoding.ASCII.GetBytes("English voice")),
                new KeyValuePair<string, byte[]>("skin/RYU_J.csb", System.Text.Encoding.ASCII.GetBytes("Japanese voice")),
                new KeyValuePair<string, byte[]>("skin/KEN_E.csb", System.Text.Encoding.ASCII.GetBytes("someone else's voice")),
            };
            var found = SkinImport.Find(files);
            Check(found.Count == 1 && found[0].Voices.Count == 2 && found[0].Describe.EndsWith("its own voice"), "found with its voice (not Ken's): " + (found.Count > 0 ? found[0].Describe : "nothing"));
            string zip = Path.Combine(scratch, "voice test.zip");
            SkinImport.Build(game, found[0], 80, null, zip);
            using (ZipArchive archive = ZipFile.OpenRead(zip))
            {
                Func<string, string> text = name => { var e = archive.Entries.FirstOrDefault(x => x.Name == name); return e == null ? null : new StreamReader(e.Open()).ReadToEnd(); };
                Check(text("RYU_80_E.csb") == "English voice" && text("RYU_80_J.csb") == "Japanese voice" && !archive.Entries.Any(e => e.Name == "RYU_E.csb" || e.Name.StartsWith("KEN")),
                      "in the package as RYU_80_E.csb and RYU_80_J.csb, never over Ryu's own voice");
            }
            Item item = Installer.Package(zip);
            Check(item != null && item.Slot == 80, "still a whole EX More Stuff package");
            // the game's own voice brought along (as Juri gótica does): left out, it changes nothing
            files[1] = new KeyValuePair<string, byte[]>("skin/RYU_E.csb", File.ReadAllBytes(Path.Combine(game, "resource", "battle", "chara", "RYU", "RYU_E.csb")));
            SkinImport.Build(game, SkinImport.Find(files)[0], 80, null, zip);
            using (ZipArchive archive = ZipFile.OpenRead(zip))
                Check(!archive.Entries.Any(e => e.Name == "RYU_80_E.csb") && archive.Entries.Any(e => e.Name == "RYU_80_J.csb"),
                      "a copy of the game's own English voice is left out; the changed Japanese one stays");
        }

        // mods that crash as a costume (user 2026-10-13, Juri gótica): one that changes its fighter's moves, and a model with
        // many more bones than the game's costume and no physics of its own
        static void Crashing(string game, string scratch)
        {
            Console.WriteLine("Mods that would crash as a costume");
            Check(SkinImport.ChangesMoves(new[] { "completa/JRI.bac", "completa/JRI.bcm", "completa/JRI_01.obj.emo" }) == "it changes Juri's moves too" &&
                  SkinImport.ChangesMoves(new[] { "skin/RYU_01.obj.emo", "skin/RYU_01_01.col.emb", "readme.bac.txt" }) == null, "move files found by name (Juri gótica's JRI.bac), none in a plain skin");
            // a model of 900 bones (the skeleton at byte 32, its count there) over Ryu's Original (433)
            var model = new byte[64];
            System.Text.Encoding.ASCII.GetBytes("#EMO").CopyTo(model, 0);
            BitConverter.GetBytes(32u).CopyTo(model, 16);
            BitConverter.GetBytes((ushort)900).CopyTo(model, 32);
            Check(Weight.Bones(model) == 900, "the bone count read as Weight reads it");
            var files = new List<KeyValuePair<string, byte[]>> { new KeyValuePair<string, byte[]>("skin/RYU_01.obj.emo", model) };
            string zip = Path.Combine(scratch, "bones test.zip"), said = null;
            try { SkinImport.Build(game, SkinImport.Find(files)[0], 80, null, zip); } catch (InvalidDataException ex) { said = ex.Message; }
            Check(said != null && said.Contains("900 bones") && said.Contains("Ryu's Original") && !File.Exists(zip), "900 bones and no physics of its own: not built (" + said + ")");
            files.Add(new KeyValuePair<string, byte[]>("skin/RYU_01.bsr", new byte[16]));
            said = null;
            try { SkinImport.Build(game, SkinImport.Find(files)[0], 80, null, zip); } catch (InvalidDataException ex) { said = ex.Message; }
            Check(said == null && File.Exists(zip), "with physics of its own (as the SFxT ports): built");
            // Add Mod From File: a skin that changes its fighter's moves isn't put in, nothing is written
            var withMoves = new List<KeyValuePair<string, byte[]>> { new KeyValuePair<string, byte[]>("completa/JRI_01.obj.emo", model), new KeyValuePair<string, byte[]>("completa/JRI.bac", new byte[8]) };
            said = null;
            try { ModFile.InstallFiles(game, withMoves, "test", "Juri test", "", "", null, text => { }); } catch (InvalidDataException ex) { said = ex.Message; }
            Check(said != null && said.StartsWith("it changes Juri's moves too"), "Add Mod From File refuses it: " + said);
        }

        // a color texture file (#EMB) of one 4x4 DXT5 texture: its alpha endpoints and its color
        static byte[] Emb(byte alpha0, byte alpha1, ushort color)
        {
            var b = new byte[64 + 128 + 16];
            System.Text.Encoding.ASCII.GetBytes("#EMB").CopyTo(b, 0);
            BitConverter.GetBytes(1).CopyTo(b, 12);       // one texture
            BitConverter.GetBytes(32).CopyTo(b, 24);      // its entry at 32
            BitConverter.GetBytes(32).CopyTo(b, 32);      // its data 32 past the entry: 64
            BitConverter.GetBytes(144).CopyTo(b, 36);
            System.Text.Encoding.ASCII.GetBytes("DDS ").CopyTo(b, 64);
            BitConverter.GetBytes(4).CopyTo(b, 64 + 12);
            BitConverter.GetBytes(4).CopyTo(b, 64 + 16);
            System.Text.Encoding.ASCII.GetBytes("DXT5").CopyTo(b, 64 + 84);
            b[192] = alpha0; b[193] = alpha1;               // alpha block (indices 0: every pixel alpha0)
            BitConverter.GetBytes(color).CopyTo(b, 200);    // color block: c0, c1 the same, indices 0
            BitConverter.GetBytes(color).CopyTo(b, 202);
            return b;
        }

        // Female Shao Kahn (user 2026-10-13): her colors differ in their textures' alpha (the mask her materials tint), and
        // were taken for copies of color 1 and given patterns; colors 7-10 she doesn't have are patterns, and the card's
        // checker finds exactly those
        static void AlphaColors(string game, string scratch)
        {
            Console.WriteLine("Colors that differ only in their alpha");
            byte[] one = Emb(255, 255, 0x7BEF), masked = Emb(0, 0, 0x7BEF), copy = Emb(255, 255, 0x7BEF);
            Check(!ColorVariants.LookSame(ColorVariants.Look(one), ColorVariants.Look(masked)) && ColorVariants.LookSame(ColorVariants.Look(one), ColorVariants.Look(copy)),
                  "the same color with another alpha mask isn't a copy; a byte-for-byte copy is");
            var model = File.ReadAllBytes(Path.Combine(game, "resource", "battle", "chara", "RYU", "RYU_01.obj.emo"));
            var files = new List<KeyValuePair<string, byte[]>>
            {
                new KeyValuePair<string, byte[]>("skin/RYU_01.obj.emo", model),
                new KeyValuePair<string, byte[]>("skin/RYU_01_01.col.emb", one), new KeyValuePair<string, byte[]>("skin/RYU_01_02.col.emb", masked),
                new KeyValuePair<string, byte[]>("skin/RYU_01_03.col.emb", copy),
                // one material for all three (a copy needs the same material too)
                new KeyValuePair<string, byte[]>("skin/RYU_01_01.obj.emm", new byte[16]), new KeyValuePair<string, byte[]>("skin/RYU_01_02.obj.emm", new byte[16]),
                new KeyValuePair<string, byte[]>("skin/RYU_01_03.obj.emm", new byte[16]),
            };
            string zip = Path.Combine(scratch, "alpha test.zip");
            SkinImport.Build(game, SkinImport.Find(files)[0], 80, null, zip);
            var patterned = ColorVariants.PatternColors(zip, "RYU_80");
            Check(patterned.SequenceEqual(new[] { 3, 4, 5, 6, 7, 8, 9, 10 }), "colors 1 and 2 its own; 3 (a copy of 1) and 4-10 (not in it) patterns: " + string.Join(",", patterned));
        }

        static void Main(string[] args)
        {
            string game = args[0], skin = args[1], scratch = args[2];
            if (Directory.Exists(scratch)) Directory.Delete(scratch, true);
            Directory.CreateDirectory(scratch);
            AppFolders.Root = Path.Combine(scratch, "program");   // its Mods\ here, not beside the test

            Console.WriteLine("A real skin, one version");
            var files = GameBanana.Unpack(skin, Path.Combine(scratch, "work")).Select(f => F("ver1/" + f.Key, f.Value)).ToList();
            byte[] model = files.First(f => f.Key.EndsWith("BLK_01.obj.emo")).Value, color2 = files.First(f => f.Key.EndsWith("BLK_01_02.col.emb")).Value;
            var found = SkinImport.Find(files);
            Check(found.Count == 1 && found[0].Fighter == "BLK" && found[0].Number == 1, "one costume: Blanka's costume 1");
            var c = found[0];
            Check(Same(c.Parts[".obj.emo"], model), "its model");
            Check(Same(c.Colors[1].Col, color2) && c.Colors[0] == c.Colors[1], "color 2 is its own, and color 1 shows it too");
            Check(c.Colors.Skip(2).All(x => x == null), "colors 3-10 are the game's");
            string zip = Path.Combine(scratch, "one.zip");
            SkinImport.Build(game, c, 8, null, zip);
            using (ZipArchive z = ZipFile.OpenRead(zip))
            {
                var names = z.Entries.Select(e => e.Name).ToList();
                Check(names.Count == 33 && names.All(n => n.StartsWith("BLK_08")), "33 files, all BLK_08 (got " + names.Count + ")");
                Check(Enumerable.Range(2, 9).All(n => Same(Entry(zip, "BLK_08_" + n.ToString("D2") + ".png"), ColorVariants.PatternPicture(n - 1))), "colors 2-10's pictures: their patterns'");
            }
            // colors 3-10, which it doesn't bring, and color 2, the same as color 1: ready-made patterns 1-9
            var made = Enumerable.Range(2, 9).Select(n => Entry(zip, "BLK_08_" + n.ToString("D2") + ".col.emb")).ToList();
            Check(Enumerable.Range(0, 9).All(k => Same(made[k], ColorVariants.Patterned(color2, k + 1))), "colors 2-10: patterns 1-9, copied in");
            Check(made.Select(m => Convert.ToBase64String(m)).Distinct().Count() == 9, "no two of them alike");
            Check(!ColorVariants.LookSame(ColorVariants.Look(made[0]), ColorVariants.Look(color2)), "color 2 doesn't look like color 1 any more");
            Check(!Same(made[3], File.ReadAllBytes(GameFile(game, "BLK_01_05.col.emb"))), "color 5 isn't the game's any more");
            Check(Enumerable.Range(2, 9).All(n => Same(Entry(zip, "BLK_08_" + n.ToString("D2") + ".obj.emm"), File.ReadAllBytes(GameFile(game, "BLK_01_02.obj.emm")))),
                  "their material: the one the skin's color uses");
            Check(GameArt.ReadContainer(made[0]).All(e => GameArt.DecodeDds(e.Value) != null) && GameArt.ReadContainer(made[0]).Count == GameArt.ReadContainer(color2).Count,
                  "a pattern's textures read, as many as the skin's color has (its materials use them by number)");
            string again = Path.Combine(scratch, "again.zip");
            SkinImport.Build(game, c, 8, null, again);
            Check(Same(Entry(again, "BLK_08_07.col.emb"), made[5]), "built again: the same colors (every PC makes the same)");
            string modelZip = Path.Combine(scratch, "model.zip");
            SkinImport.Build(game, SkinImport.Find(new List<KeyValuePair<string, byte[]>> { F("m/BLK_01.obj.emo", model) }).Single(), 8, null, modelZip);
            using (ZipArchive z = ZipFile.OpenRead(modelZip))
                Check(!z.Entries.Any(e => e.Name.EndsWith(".png")), "a model alone, made for the game's colors: no pictures");
            Check(Same(Entry(modelZip, "BLK_08_05.col.emb"), File.ReadAllBytes(GameFile(game, "BLK_01_05.col.emb"))), "and its colors stay the game's");
            Check(Same(Entry(zip, "BLK_08.obj.emo"), model), "the skin's model");
            Check(Same(Entry(zip, "BLK_08.nml.emb"), File.ReadAllBytes(GameFile(game, "BLK_01.nml.emb"))), "the game's normals");
            Check(Same(Entry(zip, "BLK_08_01.col.emb"), color2), "color 1: the skin's color");
            Check(Same(Entry(zip, "BLK_08_01.obj.emm"), File.ReadAllBytes(GameFile(game, "BLK_01_02.obj.emm"))), "color 1's material: the game's for color 2, which it was made over");

            Console.WriteLine("Several versions become colors");
            var v2 = ColorVariants.Patterned(color2, 8);   // versions that look different (patterns 8 and 9, which colors 4-10 don't get)
            var v3 = ColorVariants.Patterned(color2, 9);
            var versions = new List<KeyValuePair<string, byte[]>>(files)
            {
                F("ver2/BLK_01.obj.emo", model), F("ver2/BLK_01_02.col.emb", v2),
                F("ver3/BLK_01.obj.emo", model), F("ver3/BLK_01_02.col.emb", v3),
            };
            c = SkinImport.Find(versions).Single();
            Check(c.Versions == 3 && !c.ModelsDiffer, "3 versions, one model");
            Check(Same(c.Colors[0].Col, color2) && Same(c.Colors[1].Col, v2) && Same(c.Colors[2].Col, v3) && c.Colors[3] == null, "colors 1-3: versions 1-3, 4-10 not brought");
            zip = Path.Combine(scratch, "versions.zip");
            SkinImport.Build(game, c, 8, null, zip);
            Check(Same(Entry(zip, "BLK_08_03.col.emb"), v3) && Same(Entry(zip, "BLK_08_03.obj.emm"), File.ReadAllBytes(GameFile(game, "BLK_01_02.obj.emm"))), "color 3: version 3 with the game's color-2 material");
            Check(Enumerable.Range(4, 7).All(n => { var m = Entry(zip, "BLK_08_" + n.ToString("D2") + ".col.emb"); return m != null && !Same(m, color2) && !Same(m, v2) && !Same(m, v3); }),
                  "colors 4-10: patterns, not the versions");
            Check(Same(Entry(zip, "BLK_08_02.col.emb"), v2), "color 2: version 2, kept (it looks different)");
            // a version that differs from another by a change nobody would see is made again too
            var nearly = (byte[])color2.Clone(); nearly[nearly.Length - 1] ^= 1;
            var copies = new List<KeyValuePair<string, byte[]>>(files) { F("ver2/BLK_01.obj.emo", model), F("ver2/BLK_01_02.col.emb", nearly) };
            c = SkinImport.Find(copies).Single();
            Check(Same(c.Colors[1].Col, nearly) && ColorVariants.LookSame(ColorVariants.Look(nearly), ColorVariants.Look(color2)), "version 2: a byte apart from version 1, it looks the same");
            zip = Path.Combine(scratch, "nearly.zip");
            SkinImport.Build(game, c, 8, null, zip);
            Check(Same(Entry(zip, "BLK_08_01.col.emb"), color2) && Same(Entry(zip, "BLK_08_02.col.emb"), ColorVariants.Patterned(color2, 1)),
                  "so color 2 gets pattern 1 instead");
            var other = (byte[])model.Clone(); other[other.Length - 1] ^= 1;
            versions[versions.Count - 2] = F("ver3/BLK_01.obj.emo", other);
            Check(SkinImport.Find(versions).Single().ModelsDiffer, "a version with another model is noticed");

            Console.WriteLine("A True Ryu-like layout");
            var ryu = new List<KeyValuePair<string, byte[]>>
            {
                F("t/Ryu/Model Variations/B/RYU_01.obj.emo", new byte[] { 2 }), F("t/Ryu/Model Variations/A/RYU_01.obj.emo", new byte[] { 1 }),
                F("t/Ryu/RYU_01.nml.emb", new byte[] { 9 }), F("t/Ryu/Colors/Full Gi/RYU_01_Alpha.col.emb", new byte[] { 7 }),
            };
            for (int n = 1; n <= 10; n++) { ryu.Add(F("t/Ryu/Colors/RYU_01_" + n.ToString("D2") + ".col.emb", new byte[] { 100, (byte)n })); ryu.Add(F("t/Ryu/RYU_01_" + n.ToString("D2") + ".obj.emm", new byte[] { 200, (byte)n })); }
            c = SkinImport.Find(ryu).Single();
            Check(Same(c.Parts[".obj.emo"], new byte[] { 1 }) && Same(c.Parts[".nml.emb"], new byte[] { 9 }), "the first model variation, its normals");
            Check(Enumerable.Range(0, 10).All(i => c.Colors[i] != null && c.Colors[i].Col[1] == i + 1 && c.Colors[i].Emm[1] == i + 1), "its 10 numbered colors with their materials from the other folder");

            Console.WriteLine("A mod in a code of its own");
            byte[] blue;
            using (var bitmap = new System.Drawing.Bitmap(8, 8))
            using (var png = new MemoryStream())
            {
                using (var g = System.Drawing.Graphics.FromImage(bitmap)) g.Clear(System.Drawing.Color.FromArgb(40, 70, 200));
                bitmap.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                blue = png.ToArray();
            }
            var own = new List<KeyValuePair<string, byte[]>> { F("m/BLK_23.obj.emo", model), F("m/BLK_23_01.col.emb", color2), F("m/BLK_23.png", new byte[] { 5 }), F("m/BLK_23_01.png", blue) };
            c = SkinImport.Find(own).Single();
            Check(c.OwnCode && c.Number == 23 && c.Pictures.Count == 2, "its code 23 and its pictures");
            zip = Path.Combine(scratch, "own.zip");
            SkinImport.Build(game, c, 23, new byte[] { 1, 2, 3 }, zip);
            Check(Entry(zip, "BLK_23.png") == null && Same(Entry(zip, "BLK_23_01.png"), blue),
                  "a mod page picture that won't read: no card picture from the mod's files (user: only GameBanana's), its own color pictures stay");
            SkinImport.Build(game, c, 23, null, zip);
            Check(Same(Entry(zip, "BLK_23.png"), new byte[] { 5 }), "not a GameBanana install (no page picture given): the mod's own card picture");
            SkinImport.Build(game, c, 23, blue, zip);
            System.Drawing.Color shown;
            using (var page = new System.Drawing.Bitmap(new MemoryStream(Entry(zip, "BLK_23.png")))) shown = page.GetPixel(0, 0);
            Check(shown.R == 40 && shown.G == 70 && shown.B == 200 && Same(Entry(zip, "BLK_23_01.png"), blue),
                  "the mod page's picture for the costume (thumbnail = the page's, user), its own color pictures stay");
            Check(Same(Entry(zip, "BLK_23_02.col.emb"), ColorVariants.Patterned(color2, 1)) && Same(Entry(zip, "BLK_23_02.png"), ColorVariants.PatternPicture(1)),
                  "the colors it doesn't bring: patterns, with their pictures");

            Console.WriteLine("Shared codes");
            Func<long, long, string[], GameBanana.Mod> mod = (id, added, names) => new GameBanana.Mod
            {
                Id = id, Added = added, Category = "Skins",
                Files = new List<GameBanana.ModFile> { new GameBanana.ModFile { Id = id * 10, Name = "x.zip", Clean = true, Contents = names.ToList() } },
            };
            var hado = mod(680656, 100, new[] { "BLK_01.obj.emo", "BLK_01_02.col.emb" });
            var first = mod(1, 200, new[] { "BLK_23.obj.emo" });
            var later = mod(2, 300, new[] { "BLK_23.obj.emo", "BLK_24.obj.emo" });
            var newer = mod(3, 400, new[] { "RYU_01.obj.emo" });
            var holders = SharedCodes.Holders(new[] { later, hado, first, newer });
            Check(SharedCodes.Slot(hado, "BLK", 1, holders) == 8, "Dark Hado Blanka: its given code, Blanka 08");
            Check(SharedCodes.Slot(first, "BLK", 23, holders) == 23, "a mod's own code: its own");
            Check(SharedCodes.Slot(later, "BLK", 23, holders) == 0 && SharedCodes.Slot(later, "BLK", 24, holders) == 24, "the same code later: personal; its other code: its own");
            Check(SharedCodes.Slot(newer, "RYU", 1, holders) == 0, "a new skin replacing a game costume: personal");
            var taken = mod(4, 500, new[] { "KEN_85.obj.emo" });
            Check(SharedCodes.Slot(taken, "KEN", 85, SharedCodes.Holders(new[] { taken })) == 0, "a mod made in slot 85 (80-99 are taken): no shared seat");

            Console.WriteLine("A zip holding a 7z");
            string inner = Path.Combine(scratch, "pack"), sevenZip = Path.Combine(scratch, "RYU.7z"), outer = Path.Combine(scratch, "outer.zip");
            Directory.CreateDirectory(Path.Combine(inner, "RYU"));
            File.WriteAllBytes(Path.Combine(inner, "RYU", "RYU_01.obj.emo"), model);
            File.WriteAllBytes(Path.Combine(inner, "RYU", "setup.exe"), new byte[] { 77, 90 });
            File.WriteAllBytes(Path.Combine(inner, "RYU", "RYU_01_02.COL.EMB"), color2);
            string tar = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe");
            using (var p = Process.Start(new ProcessStartInfo(tar, "--format 7zip -cf \"" + sevenZip + "\" -C \"" + inner + "\" RYU") { UseShellExecute = false, CreateNoWindow = true })) p.WaitForExit();
            using (ZipArchive z = ZipFile.Open(outer, ZipArchiveMode.Create))
            {
                z.CreateEntryFromFile(sevenZip, "RYU.7z");
                z.CreateEntry("readme.txt");
                z.CreateEntry("tool/run.bat");
            }
            var unpacked = GameBanana.Unpack(outer, Path.Combine(scratch, "work"));
            Check(unpacked.Any(f => f.Key == "RYU.7z/RYU/RYU_01.obj.emo" && Same(f.Value, model)), "its model, as RYU.7z/RYU/RYU_01.obj.emo");
            Check(unpacked.Any(f => f.Key == "RYU.7z/RYU/RYU_01_02.COL.EMB"), "a color in capitals too");
            Check(unpacked.Count == 2, "nothing else: no program, script or text file is taken out (got " + string.Join(", ", unpacked.Select(f => f.Key)) + ")");
            Check(!Directory.Exists(Path.Combine(scratch, "work", "x1")), "the work folder is cleaned up");

            Console.WriteLine("A downloaded mod file with a stage and a skin, into a made-up game folder");
            string fake = Path.Combine(scratch, "game");
            foreach (string name in new[] { "BLK_01.nml.emb", "BLK_01.shd.emo", "BLK_01.bsr" }.Concat(Enumerable.Range(1, 10).SelectMany(n => new[] { "BLK_01_" + n.ToString("D2") + ".col.emb", "BLK_01_" + n.ToString("D2") + ".obj.emm" })))
            {
                string from = GameFile(game, name);
                if (from == null) continue;
                string to = Path.Combine(fake, "resource", "battle", "chara", "BLK", name);
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.Copy(from, to);
            }
            string stageEmz = null, stageTex = null;
            foreach (string root in new[] { "patch_ae2_tu2", "patch_ae2", @"dlc\04_ae2", @"dlc\03_character_free", "resource" })
            {
                string at = Path.Combine(game, root, "battle", "stage");
                if (stageEmz == null && File.Exists(Path.Combine(at, "STG_TRN.emz"))) stageEmz = Path.Combine(at, "STG_TRN.emz");
                if (stageTex == null && File.Exists(Path.Combine(at, "STG_TRN.tex.emz"))) stageTex = Path.Combine(at, "STG_TRN.tex.emz");
            }
            Directory.CreateDirectory(Path.Combine(fake, "resource", "battle", "stage"));
            File.Copy(stageTex, Path.Combine(fake, "resource", "battle", "stage", "STG_TRN.tex.emz"));
            Directory.CreateDirectory(Path.Combine(fake, "patch_ae2_tu3"));
            File.WriteAllBytes(Path.Combine(fake, "SSFIV.exe"), new byte[0]);
            string modZip = Path.Combine(scratch, "my_stage_mod.zip");
            using (ZipArchive z = ZipFile.Open(modZip, ZipArchiveMode.Create))
            {
                z.CreateEntryFromFile(stageEmz, "Neon Training/STG_TRN.emz");             // a stage mod bringing its model only
                using (Stream s = z.CreateEntry("Neon Training/BGM_TRN.csb").Open()) s.Write(new byte[] { 1, 2, 3 }, 0, 3);
                using (Stream s = z.CreateEntry("Skin/BLK_01.obj.emo").Open()) s.Write(model, 0, model.Length);
                using (Stream s = z.CreateEntry("Skin/BLK_01_02.col.emb").Open()) s.Write(color2, 0, color2.Length);
                z.CreateEntry("Install me.exe");
            }
            var result = ModFile.Install(fake, modZip, null, null, "", null, null, text => { });
            Check(result.Problems.Count == 0, "no problems" + (result.Problems.Count > 0 ? ": " + string.Join("; ", result.Problems) : ""));
            Check(result.Into.SequenceEqual(new[] { "Blanka 84", "stage U01" }), "in: " + string.Join(", ", result.Into));
            string stageDir = Path.Combine(fake, "patch_ae2_tu3", "battle", "stage");
            byte[] built = File.Exists(Path.Combine(stageDir, "STG_U01.emz")) ? File.ReadAllBytes(Path.Combine(stageDir, "STG_U01.emz")) : null;
            Check(built != null && File.Exists(Path.Combine(stageDir, "STG_U01.tex.emz")), "STG_U01.emz (a personal code) and its textures (from the game)");
            Check(built != null && GameArt.ReadContainer(GameArt.Unpack(built)).All(e => !e.Key.StartsWith("TRN_", StringComparison.OrdinalIgnoreCase)), "re-coded inside: nothing named TRN_ left");
            Check(Same(File.ReadAllBytes(Path.Combine(fake, "patch_ae2_tu3", "battle", "sound", "bgm", "BGM_U01.csb")), new byte[] { 1, 2, 3 }), "its music as BGM_U01.csb");
            Check(File.Exists(Path.Combine(fake, "patch_ae2_tu3", "battle", "chara", "BLK", "BLK_84.obj.emo")), "the skin as Blanka 84 (a personal seat, 84 down)");
            var records = Installer.Load(fake);
            Check(records.Count == 2 && records.All(r => r.Source == "file" && File.Exists(r.Package)), "two of your own packages, each with its zip");
            Check(records.Any(r => r.Item.IsStage && r.Shown == "my stage mod"), "named after the file");
            Check(!Directory.EnumerateFiles(fake, "*.exe", SearchOption.AllDirectories).Any(f => !f.EndsWith("SSFIV.exe")), "the .exe inside never came out");
            result = ModFile.Install(fake, modZip, null, null, "", null, null, text => { });
            Check(result.Into.SequenceEqual(new[] { "Blanka 84", "stage U01" }) && Installer.Load(fake).Count == 2, "installing it again updates the same slot and code");
            string copy = Path.Combine(scratch, "same mod renamed.zip");
            File.Copy(modZip, copy);
            Check(Installer.AddedBefore(fake, modZip) != null && Installer.AddedBefore(fake, copy) != null, "Add Mod From File sees it was added before, under any name");

            Voices(game, scratch);
            Crashing(game, scratch);
            AlphaColors(game, scratch);

            Directory.Delete(scratch, true);
            Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
            Environment.Exit(failures == 0 ? 0 : 1);
        }
    }
}
