// Effects mods (Effects) in a made-up game folder: a mod with Evil Ryu's effects files goes in through Add Mod From File's
// path (ModFile.InstallFiles) as its own entry, on right away, its zip in Mods; the game's own RYX.vfx.bvs already in the
// patch folder is kept aside and comes back; a second effects mod takes over (one at a time); off; uninstalled.
// Usage: EffectsTest <scratch folder> (build_test.bat EffectsTest)
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace ExMoreStuff
{
    static class EffectsTest
    {
        static int failures;

        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) failures++; }

        static void Main(string[] args)
        {
            string scratch = args[0];
            if (Directory.Exists(scratch)) Directory.Delete(scratch, true);
            string game = Path.Combine(scratch, "game");
            string chara = Path.Combine(game, "patch_ae2_tu3", "battle", "chara", "RYX");
            Directory.CreateDirectory(chara);
            Directory.CreateDirectory(Path.Combine(game, "resource"));
            File.WriteAllBytes(Path.Combine(game, "SSFIV.exe"), new byte[0]);
            AppFolders.Root = Path.Combine(scratch, "program");
            File.WriteAllText(Path.Combine(chara, "RYX.vfx.bvs"), "the game's own");   // as Seth's, Hakan's and Poison's are
            Func<string, string, KeyValuePair<string, byte[]>> file = (name, text) => new KeyValuePair<string, byte[]>(name, Encoding.ASCII.GetBytes(text));

            Console.WriteLine("Which files");
            Check(Effects.IsFile("x/RYX.vfx.emz") && Effects.IsFile("ryx.vfx.ptex.emz") && Effects.IsFile("RYX.vfx.bvs") && !Effects.IsFile("RYX.emz") && !Effects.IsFile("XXX.vfx.emz"),
                  "RYX.vfx.emz / .ptex.emz / .bvs (any case), not other files or fighters");
            Check(Effects.FightersIn(new[] { "a/RYX.vfx.emz", "b/RYX.vfx.bvs", "KEN.vfx.emz", "RYU_01.obj.emo" }).SequenceEqual(new[] { "RYX", "KEN" }), "the fighters in a mod");

            Console.WriteLine("Add Mod From File's path");
            var mod = new List<KeyValuePair<string, byte[]>>
            {
                file("Tom/RYX.vfx.emz", "fx A"), file("Tom/RYX.vfx.ptex.emz", "ptex A"), file("Tom/RYX.vfx.ttex.emz", "ttex A"), file("Tom/RYX.vfx.bvs", "bvs A"), file("Tom/readme.txt", "hi"),
            };
            var result = ModFile.InstallFiles(game, mod, "file:tom", "Red Aura", "Tom", "", null, s => { });
            Check(result.Problems.Count == 0 && result.Into.SequenceEqual(new[] { "Evil Ryu's effects" }), "in as Evil Ryu's effects: " + string.Join("; ", result.Problems.Concat(result.Into)));
            var list = Effects.Load(game);
            Check(list.Count == 1 && list[0].Fighter == "RYX" && list[0].Name == "Red Aura" && File.Exists(list[0].Package) && list[0].Package.StartsWith(AppFolders.Mods),
                  "listed, its zip in Mods: " + (list.Count > 0 ? list[0].Package.Substring(AppFolders.Root.Length) : ""));
            using (var zip = ZipFile.OpenRead(list[0].Package))
                Check(zip.Entries.Select(e => e.Name).OrderBy(n => n).SequenceEqual(new[] { "RYX.vfx.bvs", "RYX.vfx.emz", "RYX.vfx.ptex.emz", "RYX.vfx.ttex.emz" }), "its zip holds the four files, nothing else");
            Check(Effects.On(game)["RYX"] == list[0].Id && File.ReadAllText(Path.Combine(chara, "RYX.vfx.emz")) == "fx A" && File.ReadAllText(Path.Combine(chara, "RYX.vfx.bvs")) == "bvs A",
                  "on right away, its files in the patch folder");
            string kept = Path.Combine(game, "patch_ae2_tu3", "ex_more_stuff_effects_backup", "battle", "chara", "RYX", "RYX.vfx.bvs");
            Check(File.Exists(kept) && File.ReadAllText(kept) == "the game's own", "the game's own RYX.vfx.bvs kept aside");

            Console.WriteLine("A second one: one at a time");
            var other = new List<KeyValuePair<string, byte[]>> { file("RYX.vfx.emz", "fx B") };
            ModFile.InstallFiles(game, other, "file:other", "Blue Aura", null, "", null, s => { });
            Check(Effects.Load(game).Count == 2 && Effects.On(game)["RYX"].StartsWith("file:other") && File.ReadAllText(Path.Combine(chara, "RYX.vfx.emz")) == "fx B",
                  "the new one on, its file in");
            Check(!File.Exists(Path.Combine(chara, "RYX.vfx.ptex.emz")) && File.ReadAllText(Path.Combine(chara, "RYX.vfx.bvs")) == "the game's own" && !File.Exists(kept),
                  "the first one's files out, the game's own bvs back (the new one has none)");
            string red = Effects.Load(game).Single(e => e.Name == "Red Aura").Id;
            Effects.Set(game, "RYX", red);
            Check(Effects.On(game)["RYX"] == red && File.ReadAllText(Path.Combine(chara, "RYX.vfx.emz")) == "fx A" && File.Exists(kept), "switched back to the first");

            Console.WriteLine("Off");
            Effects.Set(game, "RYX", null);
            Check(!Effects.On(game).ContainsKey("RYX") && !File.Exists(Path.Combine(chara, "RYX.vfx.emz")) && File.ReadAllText(Path.Combine(chara, "RYX.vfx.bvs")) == "the game's own",
                  "the game's own effects: its files out, its bvs back");
            Check(!Directory.Exists(Path.Combine(game, "patch_ae2_tu3", "ex_more_stuff_effects_backup")), "nothing left aside");

            Console.WriteLine("Uninstall");
            string redZip = Effects.Load(game).Single(e => e.Id == red).Package;
            Effects.Set(game, "RYX", red);
            Effects.Uninstall(game, red);
            Check(Effects.Load(game).Count == 1 && !File.Exists(redZip) && !File.Exists(Path.Combine(chara, "RYX.vfx.emz")) && File.ReadAllText(Path.Combine(chara, "RYX.vfx.bvs")) == "the game's own",
                  "off the list, its zip to the Recycle Bin, the game's own back");
            Effects.Uninstall(game, Effects.Load(game)[0].Id);
            Check(Effects.Load(game).Count == 0 && !File.Exists(Path.Combine(game, "patch_ae2_tu3", "ex_more_stuff_effects.json")), "the last one out: the list file goes too");

            Console.WriteLine(failures == 0 ? "All passed" : failures + " failed");
            Environment.Exit(failures == 0 ? 0 : 1);
        }
    }
}
