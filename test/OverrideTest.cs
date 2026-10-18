// Override stages (Overrides.cs): a mod with override_<CODE>.txt goes in its game stage's place, no code of its own:
// its zip in Mods\Stages (the stage under the target's code, converted when it came under another), listed, and in
// the stage's place right away (a Replacement "override:<id>"); installing it again updates it; the Stages page can put
// the game's own back and pick it again; Add Mod From File's path (ModFile.InstallFiles) does the same.
// Usage: OverrideTest <real game folder> <scratch folder>   (copies of the real Training Stage)
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace ExMoreStuff
{
    static class OverrideTest
    {
        static int failures;

        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) failures++; }

        static string[] Names(byte[] file) { return GameArt.ReadContainer(GameArt.Unpack(file)).Select(e => e.Key).ToArray(); }

        static void Main(string[] args)
        {
            string real = args[0], scratch = args[1];
            if (Directory.Exists(scratch)) Directory.Delete(scratch, true);
            string game = Path.Combine(scratch, "game");
            Directory.CreateDirectory(Path.Combine(game, "resource", "battle", "stage"));
            File.WriteAllBytes(Path.Combine(game, "SSFIV.exe"), new byte[0]);
            AppFolders.Root = Path.Combine(scratch, "program");
            Func<string, byte[]> trn = suffix => File.ReadAllBytes(Path.Combine(real, "resource", "battle", "stage", "STG_TRN" + suffix));

            Console.WriteLine("Which stage it replaces");
            Check(Overrides.Target(new[] { "pack/readme.txt", "pack/sub/override_kor.txt" }) == "KOR", "override_kor.txt in a folder: KOR");
            Check(Overrides.Target(new[] { "override_XYZ.txt" }) == null && Overrides.Target(new[] { "STG_KOR.emz" }) == null, "not one of the game's stages, or no marker: none");

            Console.WriteLine("Installing one made under another code (Training Stage's files, for Festival at the Old Temple)");
            var mod = new List<KeyValuePair<string, byte[]>>
            {
                new KeyValuePair<string, byte[]>("pack/STG_TRN.emz", trn(".emz")),
                new KeyValuePair<string, byte[]>("pack/STG_TRN.tex.emz", trn(".tex.emz")),
                new KeyValuePair<string, byte[]>("pack/override_KOR.txt", Encoding.ASCII.GetBytes("no NPCs")),
                new KeyValuePair<string, byte[]>("pack/readme.txt", Encoding.ASCII.GetBytes("hi")),
            };
            byte[] shot;   // its page's picture: a real one (one that can't be read is left out)
            using (var picture = new System.Drawing.Bitmap(8, 8))
            using (var png = new MemoryStream()) { picture.Save(png, System.Drawing.Imaging.ImageFormat.Png); shot = png.ToArray(); }
            string target = Overrides.Install(game, mod, "gamebanana:1:override", "Quiet Temple", "oops", "v1", shot);
            var list = Overrides.Load(game);
            Check(target == "KOR" && list.Count == 1 && list[0].Name == "Quiet Temple" && list[0].Target == "KOR", "listed: Quiet Temple, in place of KOR");
            string zip = list[0].Package;
            Check(zip.StartsWith(AppFolders.StagePackages) && Path.GetFileName(zip) == "Quiet Temple - replaces KOR.zip", "its zip in Mods\\Stages: " + Path.GetFileName(zip));
            using (ZipArchive archive = ZipFile.OpenRead(zip))
            {
                var names = archive.Entries.Select(e => e.Name).OrderBy(n => n).ToArray();
                Check(names.SequenceEqual(new[] { "override_KOR.txt", "STG_KOR.emz", "STG_KOR.png", "STG_KOR.tex.emz" }), "it holds STG_KOR, its marker and picture, nothing else: " + string.Join(", ", names));
            }
            byte[] inZip = Overrides.StageBytes(game, "override:gamebanana:1:override", ".emz");
            Check(Names(inZip).Any(n => n.StartsWith("KOR_")) && !Names(inZip).Any(n => n.StartsWith("TRN_")), "converted: TRN_ entries now KOR_");
            var replaced = Replacements.Load(game);
            string patchFile = Path.Combine(Game.PatchFolder(game), "battle", "stage", "STG_KOR.emz");
            Check(replaced.Count == 1 && replaced[0].Stage == "KOR" && replaced[0].Source == "override:gamebanana:1:override" && File.Exists(patchFile),
                  "in Festival at the Old Temple's place right away");
            Check(GameArt.Unpack(File.ReadAllBytes(patchFile)).SequenceEqual(GameArt.Unpack(inZip)), "the game gets its files as they are in the zip");

            Console.WriteLine("Again (an update)");
            Overrides.Install(game, mod, "gamebanana:1:override", "Quiet Temple", "oops", "v2", null);
            list = Overrides.Load(game);
            Check(list.Count == 1 && list[0].Version == "v2" && list[0].Package == zip && Replacements.Load(game).Single().Source == "override:gamebanana:1:override",
                  "one entry, its zip updated in place, still in KOR's place");

            Console.WriteLine("The Stages page");
            Check(Replacements.Restore(game, "KOR") == null && !File.Exists(patchFile) && Overrides.Load(game).Count == 1, "the game's own back: the override stays listed");
            Check(Replacements.Problem(game, "KOR", "override:gamebanana:1:override", s => s) == null, "it can be picked again");
            Replacements.Replace(game, "KOR", "override:gamebanana:1:override");
            Check(File.Exists(patchFile), "picked again: in KOR's place");
            Replacements.Restore(game, "KOR");

            Console.WriteLine("Add Mod From File's path");
            var result = ModFile.InstallFiles(game, mod, "file:abc", "From A File", null, "", null, s => { });
            Check(result.Into.SequenceEqual(new[] { "in place of Festival at the Old Temple" }) && Overrides.Load(game).Count == 2 &&
                  Replacements.Load(game).Single().Source == "override:file:abc:override", "it goes in as an override too: " + string.Join(", ", result.Into));

            Console.WriteLine("Uninstall (Browse Mods' Uninstall, user 2026-10-17)");
            string fileZip = Overrides.Load(game).Single(o => o.Id == "file:abc:override").Package;
            Overrides.Uninstall(game, "file:abc:override");
            Check(!Replacements.Load(game).Any() && !File.Exists(patchFile), "the game's own KOR back, the override's files out of the game");
            Check(Overrides.Load(game).Count == 1 && Overrides.Load(game)[0].Id == "gamebanana:1:override", "off the list, the other stays");
            Check(!File.Exists(fileZip) && File.Exists(zip), "its zip to the Recycle Bin, the other's kept");
            Overrides.Uninstall(game, "gamebanana:1:override");
            Check(Overrides.Load(game).Count == 0 && !File.Exists(zip) && !File.Exists(Path.Combine(Game.PatchFolder(game), "ex_more_stuff_overrides.json")),
                  "the last one out: the list file goes too");

            Console.WriteLine("Problems");
            var two = new List<KeyValuePair<string, byte[]>>(mod) { new KeyValuePair<string, byte[]>("pack/STG_DET.emz", trn(".emz")), new KeyValuePair<string, byte[]>("pack/STG_DET.tex.emz", trn(".tex.emz")) };
            string problem = null;
            try { Overrides.Install(game, two, "x", "Two", null, null, null); } catch (InvalidDataException ex) { problem = ex.Message; }
            Check(problem == "it has more than one stage and none named STG_KOR", "two stages, neither named for KOR: " + problem);

            Console.WriteLine("code_<CODE>.txt: a game stage's files, its own code in EX More Stuff (user 2026-10-18)");
            Check(Overrides.OwnCode(new[] { "x/code_t01.txt", "STG_LBX.emz" }) == "T01" && Overrides.OwnCode(new[] { "code_KOR.txt" }) == null &&
                  Overrides.OwnCode(new[] { "code_T01.txt", "code_T02.txt" }) == null, "code_t01.txt: T01; a game stage's code or two codes: none");
            var coded = new List<KeyValuePair<string, byte[]>>
            {
                new KeyValuePair<string, byte[]>("STG_TRN.emz", trn(".emz")),
                new KeyValuePair<string, byte[]>("STG_TRN.tex.emz", trn(".tex.emz")),
                new KeyValuePair<string, byte[]>("code_T01.txt", new byte[0]),
            };
            string asked = null;
            result = ModFile.InstallFiles(game, coded, "gamebanana:7", "Lab Copy", null, "", null, s => { }, "", c => { asked = c; return c == "T01" ? "T01" : null; });
            Installed lab = Installer.Load(game).SingleOrDefault(r => r.Item.IsStage && r.Item.Code == "T01");
            Check(asked == "T01" && lab != null && result.Shared && result.Into.SequenceEqual(new[] { "stage T01" }), "in as its own stage T01, shared: " + string.Join(", ", result.Problems.Concat(result.Into)));
            byte[] t01 = File.ReadAllBytes(Path.Combine(Game.PatchFolder(game), "battle", "stage", "STG_T01.emz"));
            Check(Names(t01).Any(n => n.StartsWith("T01_")) && !Names(t01).Any(n => n.StartsWith("TRN_")), "re-coded as it went in: TRN_ entries now T01_");
            Check(Replacements.Load(game).Count == 0 && Overrides.Load(game).Count == 0, "not a replacement here");

            Console.WriteLine("needs_exms_<version>.txt");
            var later = new List<KeyValuePair<string, byte[]>>(coded) { new KeyValuePair<string, byte[]>("needs_exms_9.5.txt", new byte[0]) };
            problem = null;
            try { ModFile.InstallFiles(game, later, "file:later", "Later", null, "", null, s => { }); } catch (InvalidDataException ex) { problem = ex.Message; }
            Check(problem != null && problem.StartsWith("it needs EX More Stuff 9.5 or newer. "), "refused, asks for 9.5: " + problem);
            Check(Overrides.TooNew(new[] { "needs_exms_0.92.txt" }) == null && Overrides.TooNew(new[] { "needs_exms_0.5.txt" }) == null && Overrides.TooNew(new[] { "readme.txt" }) == null,
                  "this version or older, or none: fine");

            Console.WriteLine("slot_<FIGHTER>_<NN>.txt: a game costume's files, its own shared slot (user 2026-10-18)");
            Func<long, long, string[], GameBanana.Mod> gb = (id, added, names) =>
            {
                var m = new GameBanana.Mod { Id = id, Added = added, Name = "mod " + id };
                var f = new GameBanana.ModFile { Id = id, Name = "m" + id + ".zip", Clean = true };
                f.Contents.AddRange(names);
                m.Files.Add(f);
                return m;
            };
            var ryu = gb(501, 100, new[] { "RYU_01.obj.emo", "RYU_01.nml.emb", "RYU_01_01.col.emb", "slot_RYU_61.txt" });
            var holders = SharedCodes.Holders(new[] { ryu });
            Check(holders.ContainsKey("RYU 61") && holders["RYU 61"].Id == 501 && SharedCodes.Slot(ryu, "RYU", 1, holders) == 61, "RYU_01 files with slot_RYU_61.txt: shared slot 61");
            var earlier = gb(400, 50, new[] { "RYU_61.obj.emo", "RYU_61.nml.emb", "RYU_61_01.col.emb" });
            holders = SharedCodes.Holders(new[] { ryu, earlier });
            Check(SharedCodes.Slot(ryu, "RYU", 1, holders) == 0 && SharedCodes.Slot(earlier, "RYU", 61, holders) == 61, "an earlier upload holds RYU 61: this one goes in a personal slot");
            var twoRyu = gb(502, 100, new[] { "RYU_01.obj.emo", "RYU_02.obj.emo", "slot_RYU_09.txt" });
            Check(SharedCodes.Slot(twoRyu, "RYU", 1, SharedCodes.Holders(new[] { twoRyu })) == 0, "two of the game's costumes: the marker isn't used");
            Check(Overrides.OwnSlots(new[] { "slot_RYU_80.txt", "slot_XXX_10.txt", "slot_ken_12.txt" }).Select(o => o.Key + o.Value).SequenceEqual(new[] { "KEN12" }),
                  "a personal slot or not a fighter: none; lower case fine");

            Console.WriteLine(failures == 0 ? "All passed" : failures + " failed");
            Environment.Exit(failures == 0 ? 0 : 1);
        }
    }
}
