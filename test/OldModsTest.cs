// Import old mods (Advanced > Old mods) in a made-up game folder: by hand, the Training Stage was replaced (STG_TRN in the
// patch folder), Ryu's Alternate 1 replaced with his Original's files (RYU_02*), and a Ryu 40 put in; also a changed
// RYU.bac and a folder of stray files, which only get listed, and an official update file, which is left out. Imported:
// the stage becomes stage U01 (in Mods\Stages) still playing as the Training Stage, Alternate 1 becomes Ryu 84 (in
// Mods\Characters\Ryu) still playing as it, Ryu 40 becomes Ryu 83 with nothing replaced; their loose files are gone.
// Usage: OldModsTest <real game folder> <scratch folder>   (build_test.bat OldModsTest)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ExMoreStuff
{
    static class OldModsTest
    {
        static int failures;

        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) failures++; }

        static bool Same(string a, string b) { return File.ReadAllBytes(a).SequenceEqual(File.ReadAllBytes(b)); }

        static void Main(string[] args)
        {
            string real = args[0], scratch = args[1], game = Path.Combine(scratch, "game");
            if (Directory.Exists(scratch)) Directory.Delete(scratch, true);
            AppFolders.Root = Path.Combine(scratch, "exms");
            string realRyu = Path.Combine(real, "resource", "battle", "chara", "RYU"), realStage = Path.Combine(real, "resource", "battle", "stage");
            string res = Path.Combine(game, "resource", "battle", "chara", "RYU"), resStage = Path.Combine(game, "resource", "battle", "stage");
            string patch = Path.Combine(game, "patch_ae2_tu3"), chara = Path.Combine(patch, "battle", "chara", "RYU"), stage = Path.Combine(patch, "battle", "stage");
            foreach (string d in new[] { res, resStage, chara, stage }) Directory.CreateDirectory(d);
            // the game's own: Ryu's Original and Alternate 1, the Training Stage
            foreach (string f in Directory.GetFiles(realRyu, "RYU_01*").Concat(Directory.GetFiles(realRyu, "RYU_02*"))) File.Copy(f, Path.Combine(res, Path.GetFileName(f)));
            foreach (string f in new[] { "STG_TRN.emz", "STG_TRN.tex.emz" }) File.Copy(Path.Combine(realStage, f), Path.Combine(resStage, f));
            // put in by hand: the Training Stage replaced, Alternate 1 as the Original, a Ryu 40
            foreach (string f in new[] { "STG_TRN.emz", "STG_TRN.tex.emz" }) File.Copy(Path.Combine(realStage, f), Path.Combine(stage, f));
            foreach (string f in Directory.GetFiles(realRyu, "RYU_01*"))
            {
                File.Copy(f, Path.Combine(chara, "RYU_02" + Path.GetFileName(f).Substring(6)));
                File.Copy(f, Path.Combine(chara, "RYU_40" + Path.GetFileName(f).Substring(6)));
            }
            File.WriteAllText(Path.Combine(chara, "RYU.bac"), "a changed move list");
            File.WriteAllText(Path.Combine(chara, "RYU_E.csb"), "a changed English voice");   // a voice mod (as Cody's chain noises taken out)
            Directory.CreateDirectory(Path.Combine(patch, "battle", "stage_oopsV1"));
            File.WriteAllText(Path.Combine(patch, "battle", "stage_oopsV1", "notes.txt"), "stray");
            // an official update file (path and size as listed): the game's own
            string official = Path.Combine(patch, "battle", "chara", "RYU", "RYU.bcm");
            File.WriteAllText(official, "0123456789");
            ModScan.UseOfficial(new Dictionary<string, long> { { @"patch_ae2_tu3\battle\chara\RYU\RYU.bcm", 10 } });

            Console.WriteLine("What it finds");
            var found = ModScan.Scan(game);
            foreach (var f in found) Console.WriteLine("       " + f.Kind + ": " + f.What + " (" + f.Files.Count + ")");
            var trn = found.SingleOrDefault(f => f.Key == "stage:TRN");
            var alt1 = found.SingleOrDefault(f => f.Key == "costume:RYU:2");
            var forty = found.SingleOrDefault(f => f.Key == "costume:RYU:40");
            Check(trn != null && trn.Files.Count == 2 && trn.CanImport, "the replaced Training Stage: its two files");
            Check(alt1 != null && alt1.What.Contains("Alternate 1") && forty != null && forty.CanImport, "Ryu's Alternate 1 replaced, and a Ryu 40");
            Check(found.Any(f => f.Kind == ModScan.Kind.Other && f.Files.Contains(@"patch_ae2_tu3\battle\chara\RYU\RYU.bac")) &&
                  found.Any(f => f.Kind == ModScan.Kind.Other && f.What.Contains("stage_oopsV1")), "the changed RYU.bac and the stray folder: listed, not importable");
            Check(!found.Any(f => f.Files.Contains(@"patch_ae2_tu3\battle\chara\RYU\RYU.bcm")), "the official update file left out");

            var discarded = new List<string>();
            Action<string> discard = path => { discarded.Add(path); File.Delete(path); };

            Console.WriteLine("The Training Stage, kept showing");
            string where = ModScan.Import(game, trn, true, discard);
            Installed u01 = Installer.Load(game).SingleOrDefault(r => r.Item.IsStage);
            Check(where == "stage U01" && u01 != null && u01.Item.Code == "U01", "in as stage U01: " + where);
            Check(u01 != null && Path.GetDirectoryName(u01.Package) == AppFolders.StagePackages && File.Exists(u01.Package), "its zip in Mods\\Stages");
            Check(discarded.Count == 2 && File.Exists(Path.Combine(stage, "STG_U01.emz")), "its loose files discarded, STG_U01 in the game");
            Check(Replacements.Load(game).Any(r => r.Stage == "TRN" && r.Source == "U01") && File.Exists(Path.Combine(stage, "STG_TRN.emz")),
                  "the Training Stage still plays as it (a replacement)");

            Console.WriteLine("Ryu's Alternate 1, kept showing");
            discarded.Clear();
            where = ModScan.Import(game, alt1, true, discard);
            Installed ryu84 = Installer.Load(game).SingleOrDefault(r => r.Item.IsCostume && r.Item.Slot == 84);
            Check(where == "Ryu 84" && ryu84 != null, "in as Ryu 84 (the first of your own seats): " + where);
            Check(ryu84 != null && Path.GetDirectoryName(ryu84.Package) == Path.Combine(AppFolders.Mods, "Characters", "Ryu"), "its zip in Mods\\Characters\\Ryu");
            Check(discarded.Count == alt1.Files.Count && File.Exists(Path.Combine(chara, "RYU_84.obj.emo")), "its loose files discarded, RYU_84 in the game");
            Check(CostumeSwaps.Load(game).Any(s => s.Key == "RYU:2" && s.Source == 84) && Same(Path.Combine(chara, "RYU_02.obj.emo"), Path.Combine(chara, "RYU_84.obj.emo")),
                  "Alternate 1 still plays as it (a costume replacement)");
            using (var zip = System.IO.Compression.ZipFile.OpenRead(ryu84.Package))
            {
                var voice = zip.Entries.FirstOrDefault(e => e.Name == "RYU_84_E.csb");
                Check(voice != null && new StreamReader(voice.Open()).ReadToEnd() == "a changed English voice" && !zip.Entries.Any(e => e.Name == "RYU_84_J.csb"),
                      "the hand-placed English voice went into its package as RYU_84_E.csb (no Japanese one: none was changed)");
            }
            Check(File.ReadAllText(Path.Combine(chara, "RYU_E.csb")) == "a changed English voice" && !discarded.Any(d => d.EndsWith("RYU_E.csb")) &&
                  File.Exists(Path.Combine(chara, "RYU_84_E.csb")), "the voice stays where it was (the whole fighter's); RYU_84_E.csb beside the costume"); 

            Console.WriteLine("Ryu 40, the game's own back");
            discarded.Clear();
            where = ModScan.Import(game, forty, false, discard);
            Check(where == "Ryu 83" && !File.Exists(Path.Combine(chara, "RYU_40.obj.emo")) && File.Exists(Path.Combine(chara, "RYU_83.obj.emo")), "in as Ryu 83, RYU_40 gone: " + where);
            Check(CostumeSwaps.Load(game).Count == 1, "nothing more replaced");

            Console.WriteLine("Keeping it showing can't be done: imported all the same");
            // a whole Alternate 2 put in, which this game doesn't have
            foreach (string f in Directory.GetFiles(res, "RYU_01*")) File.Copy(f, Path.Combine(chara, "RYU_03" + Path.GetFileName(f).Substring(6)));
            var three = ModScan.Scan(game).Single(f => f.Key == "costume:RYU:3");
            where = ModScan.Import(game, three, true, discard);
            Check(where.StartsWith("Ryu 82 (it couldn't keep showing where it was") && Installer.Load(game).Any(r => r.Item.Slot == 82), "in as Ryu 82: " + where);

            Console.WriteLine("Looking again");
            var after = ModScan.Scan(game);
            foreach (var f in after) Console.WriteLine("       " + f.Kind + ": " + f.What + " (" + f.Files.Count + ")");
            Check(after.All(f => !f.CanImport) && after.Count == 2, "only the two that stay as they are");
            try { ModScan.Import(game, after[0], false, discard); Check(false, "other files refused"); }
            catch (InvalidOperationException) { Check(true, "other files refused"); }

            Console.WriteLine("The originals kept safe (Mods\\Legacy mods backup, the game's folders inside)");
            string loose = Path.Combine("patch_ae2_tu3", "battle", "stage", "STG_ZZZ.emz");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(game, loose)));
            File.WriteAllText(Path.Combine(game, loose), "first");
            string kept = AppFolders.BackUpOldMod(game, loose);
            string day = Path.Combine(AppFolders.OldModsBackup, DateTime.Now.ToString("yyyy-MM-dd"));
            Check(kept == Path.Combine(day, loose) && File.ReadAllText(kept) == "first" && !File.Exists(Path.Combine(game, loose)), "moved out of the game, its folders kept: " + kept);
            File.WriteAllText(Path.Combine(game, loose), "second");
            string again = AppFolders.BackUpOldMod(game, loose);
            Check(again == Path.Combine(day, "patch_ae2_tu3", "battle", "stage", "STG_ZZZ (2).emz") && File.ReadAllText(kept) == "first" && File.ReadAllText(again) == "second",
                  "a second one with that name doesn't replace the first: " + again);

            // their originals as the import would have kept them (this test's imports deleted them): found by what they are
            Console.WriteLine("Put back, one at a time (user 2026-10-15)");
            Action<string, string, string> backedUp = (folder, name, from) =>
            {
                string to = Path.Combine(day, "patch_ae2_tu3", folder, name);
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.Copy(from, to);
            };
            foreach (string f in new[] { "STG_TRN.emz", "STG_TRN.tex.emz" }) backedUp(Path.Combine("battle", "stage"), f, Path.Combine(realStage, f));
            foreach (string f in Directory.GetFiles(realRyu, "RYU_01*")) backedUp(Path.Combine("battle", "chara", "RYU"), "RYU_40" + Path.GetFileName(f).Substring(6), f);
            foreach (string f in Directory.GetFiles(realRyu, "RYU_01*")) backedUp(Path.Combine("battle", "chara", "RYU"), "RYU_03" + Path.GetFileName(f).Substring(6), f);
            Func<int, Installed> costume = slot => Installer.Load(game).SingleOrDefault(r => r.Item.IsCostume && r.Item.Slot == slot);
            u01 = Installer.Load(game).Single(r => r.Item.IsStage && r.Item.Code == "U01");
            Check(ModScan.Imported(u01) && ModScan.Originals(u01).Count == 2 && ModScan.Originals(costume(83)).Count == Directory.GetFiles(realRyu, "RYU_01*").Length,
                  "each import's originals found: the Training Stage's 2, Ryu 40's");
            Check(ModScan.Originals(u01).All(o => !o.Key.Contains("(2)")), "a second file of the same name that day isn't taken");

            string zip01 = u01.Package;
            ModScan.PutBack(game, u01);
            Check(!Installer.Load(game).Any(r => r.Item.Code == "U01") && !File.Exists(Path.Combine(stage, "STG_U01.emz")) && !File.Exists(zip01), "stage U01 deleted, its zip gone");
            Check(!Replacements.Load(game).Any(r => r.Stage == "TRN") && Same(Path.Combine(stage, "STG_TRN.emz"), Path.Combine(realStage, "STG_TRN.emz")) &&
                  Same(Path.Combine(stage, "STG_TRN.tex.emz"), Path.Combine(realStage, "STG_TRN.tex.emz")), "the replaced Training Stage's own files back where they were (EX More Stuff's replacement off first)");
            Check(!File.Exists(Path.Combine(day, "patch_ae2_tu3", "battle", "stage", "STG_TRN.emz")) && File.Exists(Path.Combine(day, "patch_ae2_tu3", "battle", "stage", "STG_ZZZ.emz")),
                  "out of the backup; another mod's backup stays");

            ModScan.PutBack(game, costume(83));
            Check(costume(83) == null && !File.Exists(Path.Combine(chara, "RYU_83.obj.emo")) && Same(Path.Combine(chara, "RYU_40.obj.emo"), Path.Combine(realRyu, "RYU_01.obj.emo")),
                  "Ryu 83 deleted, RYU_40 back as it was");
            Check(costume(84) != null && costume(82) != null, "the other imports stay");

            File.WriteAllText(Path.Combine(chara, "RYU_03.obj.emo"), "another mod put in since");
            try { ModScan.PutBack(game, costume(82)); Check(false, "another file in its place: refused"); }
            catch (IOException ex) { Check(ex.Message.Contains("RYU_03.obj.emo") && costume(82) != null && ModScan.Originals(costume(82)).Count > 0, "another file in its place: refused, nothing changed: " + ex.Message); }
            try { ModScan.PutBack(game, costume(84)); Check(false, "no originals: refused"); }
            catch (IOException ex) { Check(costume(84) != null, "no originals in the backup: refused, nothing changed: " + ex.Message); }

            Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
            Environment.Exit(failures == 0 ? 0 : 1);
        }
    }
}
