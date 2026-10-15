// The performance test (PerfTest.cs) from start to end in a made-up game folder, with test\FakeGame.cs as its SSFIV.exe
// (no real game is started): the PC check (Hugo vs Poison on Blast Furnace), a stage in Blast Furnace's place and a
// costume on both sides, the helper in place of the stand-in Tom's mod during a run and Tom's back after, the player's
// own replacement or swap kept, config.ini put back, comparison runs made once per graphics settings, results kept,
// the lock (a size warning and a failed test: didn't load, or below full speed) and its end, and Recover after a test
// that stopped halfway.
// Usage: PerfTestTest <real game folder> <scratch folder>   (copies of the real Training Stage and Pit Stop 109)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace ExMoreStuff
{
    static class PerfTestTest
    {
        static int failures;

        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) failures++; }

        static void Package(string game, Item item, string scratch, Dictionary<string, byte[]> files)
        {
            string zip = Path.Combine(scratch, item.Prefix + ".zip");
            if (File.Exists(zip)) File.Delete(zip);
            using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
                foreach (var f in files)
                    using (Stream s = archive.CreateEntry(f.Key).Open()) s.Write(f.Value, 0, f.Value.Length);
            Installer.InstallPackage(game, item, zip, "file");
        }

        static Dictionary<string, byte[]> Costume(string prefix, string what)
        {
            var files = new Dictionary<string, byte[]> { { prefix + ".obj.emo", new byte[64] }, { prefix + ".nml.emb", new byte[16] } };
            for (int c = 1; c <= 10; c++)
            {
                files[prefix + "_" + c.ToString("D2") + ".col.emb"] = System.Text.Encoding.ASCII.GetBytes(what + " color " + c);
                files[prefix + "_" + c.ToString("D2") + ".obj.emm"] = new byte[16];
            }
            return files;
        }

        static string[] Log(string game) { string p = Path.Combine(game, "fake_log.txt"); return File.Exists(p) ? File.ReadAllLines(p) : new string[0]; }

        static void Main(string[] args)
        {
            string real = args[0], scratch = args[1];
            if (Directory.Exists(scratch)) Directory.Delete(scratch, true);
            string game = Path.Combine(scratch, "game");
            string resource = Path.Combine(game, "resource", "battle", "stage");
            Directory.CreateDirectory(resource);
            foreach (string code in new[] { "TRN", "DET" })
                foreach (string suffix in StagePack.Suffixes)
                    File.Copy(Path.Combine(real, "resource", "battle", "stage", "STG_" + code + suffix), Path.Combine(resource, "STG_" + code + suffix));
            string ryu = Path.Combine(game, "resource", "battle", "chara", "RYU");
            Directory.CreateDirectory(ryu);
            foreach (var f in Costume("RYU_01", "the game's")) File.WriteAllBytes(Path.Combine(ryu, f.Key), f.Value);
            AppFolders.Root = Path.Combine(scratch, "program");
            PerfTest.NeedSteam = false;
            PerfTest.ConfigFile = Path.Combine(scratch, "config.ini");
            const string settings = "[SYSTEM]\r\nSystemSpeed=-1.00000\r\n[DISPLAY]\r\nResolutionX=1920\r\n[GRAPHICS]\r\nShadow=HIGH\r\n";
            File.WriteAllText(PerfTest.ConfigFile, settings);
            Environment.SetEnvironmentVariable("EXMS_FAKE_CONFIG", PerfTest.ConfigFile);

            // the stand-in game, and a stand-in Tom's Round BGM mod beside it
            var csc = Process.Start(new ProcessStartInfo(@"C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe",
                "-nologo -target:exe -out:\"" + Path.Combine(game, "SSFIV.exe") + "\" \"" + Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "test", "FakeGame.cs") + "\"")
                { UseShellExecute = false, RedirectStandardOutput = true });
            Console.Write(csc.StandardOutput.ReadToEnd());
            csc.WaitForExit();
            File.WriteAllText(Path.Combine(game, "dinput8.dll"), "Tom's Round BGM mod (stand-in)");

            // a custom stage (Training Stage under code U50) and two custom Ryu costumes, installed as packages
            var stage = new Item { Type = "stage", Code = "U50", Name = "Test Stage" };
            Package(game, stage, scratch, StagePack.Suffixes.ToDictionary(s => "STG_U50" + s, s => StagePack.Recode(File.ReadAllBytes(Path.Combine(resource, "STG_TRN" + s)), "TRN", "U50")));
            var costume = new Item { Type = "costume", Fighter = "RYU", Slot = 80, Name = "Test Costume" };
            Package(game, costume, scratch, Costume("RYU_80", "custom 80"));
            Package(game, new Item { Type = "costume", Fighter = "RYU", Slot = 81, Name = "Theirs" }, scratch, Costume("RYU_81", "custom 81"));
            var said = new List<string>();

            string patchStage = Path.Combine(Game.PatchFolder(game), "battle", "stage");
            string tom = Path.Combine(game, "dinput8.dll");
            Func<bool> clean = () => File.ReadAllText(tom) == "Tom's Round BGM mod (stand-in)" && !File.Exists(Path.Combine(game, "dinput8.dll.ex_more_stuff_test")) &&
                                     !File.Exists(Path.Combine(game, "exms_benchmark.txt")) && !File.Exists(Path.Combine(Game.PatchFolder(game), "ex_more_stuff_test.json"));

            Console.WriteLine("The PC check");
            Check(PerfTest.PcResult() == null, "not made yet");
            PerfTest.Result r = PerfTest.CheckPc(game, said.Add);
            string[] log = Log(game);
            Check(log.Length == 1 && log[0].StartsWith("p1=42 p2=41 stage=28 helper=True steam=45760 swapped=False"),
                  "one run: Hugo vs Poison on the game's own Blast Furnace, the helper in place, Steam's ID: " + string.Join(" | ", log));
            Check(r.Average == 297 && PerfTest.PcResult() == 297, "kept: 297 fps");
            Check(Weight.StageLine == 4.5 && Weight.Source(true) == "from your tests", "it sets where the stages' warning line starts: 297 / 60 -> 4.5 (" + Weight.StageLine + ")");
            Check(clean(), "Tom's mod is back, no request or test record left");
            Check(File.ReadAllText(PerfTest.ConfigFile) == settings, "config.ini as it was (the game had changed it)");

            Console.WriteLine("A stage");
            r = PerfTest.Run(game, stage, false, said.Add);
            log = Log(game);
            Check(log.Length == 2 && log[1].StartsWith("p1=42 p2=41 stage=28 helper=True steam=45760 swapped=True"),
                  "one run (compared with the PC check): Hugo vs Poison with the custom stage in Blast Furnace's place: " + log.Last());
            Check(r.Average == 250 && r.Baseline == 297 && r.Against == "Blast Furnace" && Math.Round(r.Slower) == 16 && r.FullSpeed, "the result: 250 against 297, 16% slower, full speed");
            Check(!File.Exists(Path.Combine(patchStage, "STG_BFU.emz")) && Replacements.Load(game).Count == 0 && clean(), "Blast Furnace is the game's own again; all put back");
            var last = PerfTest.Last(stage.Key);
            Check(last != null && last.Average == 250 && last.Baseline == 297, "the result is kept");
            Check(PerfTest.Describe("Stage U50 - Test Stage", r) == "Stage U50 - Test Stage ran at 250 fps on average in the game's benchmark: 16% slower than the game's own Blast Furnace (297 fps). It should play at full speed on this PC.",
                  "said as: " + PerfTest.Describe("Stage U50 - Test Stage", r));
            Check(PerfTest.Locked(stage) == null, "not locked");
            File.WriteAllText(PerfTest.ConfigFile, settings.Replace("Shadow=HIGH", "Shadow=LOW"));
            Check(PerfTest.PcResult() == null, "other graphics settings: no PC check for them yet");
            PerfTest.Run(game, stage, false, said.Add);
            log = Log(game);
            Check(log.Length == 4 && log[2].Contains("swapped=False") && PerfTest.PcResult() == 297, "so the test makes it first (the game's own Blast Furnace), then tests");
            Check(said.Any(s => s.Contains("Blast Furnace first")), "and says so: " + said.First(s => s.Contains("first")));

            Console.WriteLine("A stage when the player replaced Blast Furnace");
            Replacements.Replace(game, "BFU", "TRN");
            PerfTest.Run(game, stage, false, said.Add);
            Check(Log(game).Length == 5, "one run");
            var theirs = Replacements.Load(game);
            Check(theirs.Count == 1 && theirs[0].Stage == "BFU" && theirs[0].Source == "TRN" && File.Exists(Path.Combine(patchStage, "STG_BFU.emz")), "their Blast Furnace replacement is back after");
            Replacements.Restore(game, "BFU");

            Console.WriteLine("A costume");
            CostumeSwaps.Replace(game, "RYU", 1, 81);   // the player's own: Theirs plays as Ryu's Original
            r = PerfTest.Run(game, costume, false, said.Add);
            log = Log(game).Skip(5).ToArray();
            Check(log.Length == 1 && log[0].StartsWith("p1=0 p2=0 stage=0 helper=True") && log[0].Contains("swapped=True"),
                  "one run only (no comparison run): the custom costume in Ryu's Original's place, Ryu on both sides on Training Stage: " + string.Join(" | ", log));
            string told = PerfTest.Describe("Test Costume", r);
            Check(r.Against == null && r.Baseline == 0 && !told.Contains("game's own"), "compared with nothing: " + told);
            var swap = CostumeSwaps.Load(game).SingleOrDefault();
            string original = Path.Combine(Game.PatchFolder(game), "battle", "chara", "RYU", "RYU_01_01.col.emb");
            Check(swap != null && swap.Source == 81 && File.ReadAllText(original) == "custom 81 color 1", "the player's own swap is back (81 as the Original)");
            CostumeSwaps.Restore(game, "RYU", 1);

            Console.WriteLine("Not loading, too slow");
            Environment.SetEnvironmentVariable("EXMS_FAKE_MODE", "crash");
            string didntLoad = null;
            try { PerfTest.Run(game, stage, false, said.Add); } catch (PerfTest.LoadFailed ex) { didntLoad = ex.Message; }
            Check(didntLoad == "the game closed before the test was done", "the game closed without a result: it didn't load (" + didntLoad + ")");
            Check(PerfTest.Locked(stage) == null, "without a size warning it isn't locked");
            Check(Weight.StageLine == 1, "a stage that failed at 0.03 times Blast Furnace's size: the automatic line goes as low as it can, 1 (" + Weight.StageLine + ")");
            Check(!File.Exists(Path.Combine(patchStage, "STG_BFU.emz")) && Replacements.Load(game).Count == 0 && clean(), "all put back after it");
            didntLoad = null;
            try { PerfTest.Run(game, stage, true, said.Add); } catch (PerfTest.LoadFailed ex) { didntLoad = ex.Message; }
            Check(didntLoad != null && (PerfTest.Locked(stage) ?? "").StartsWith("it didn't load"), "with a size warning it's locked: " + PerfTest.Locked(stage));
            Check(PerfTest.Locked(new Item { Type = "stage", Code = "U50", Version = "2" }) == null, "another version of it isn't");
            Package(game, stage, scratch, StagePack.Suffixes.ToDictionary(x => "STG_U50" + x, x => StagePack.Recode(File.ReadAllBytes(Path.Combine(resource, "STG_TRN" + x)), "TRN", "U50")));
            Check(PerfTest.Locked(stage) == null, "put in again (new files): it may be tried again");
            Environment.SetEnvironmentVariable("EXMS_FAKE_MODE", "slow");
            r = PerfTest.Run(game, stage, false, said.Add);
            Check(r.Average == 45 && !r.FullSpeed && PerfTest.Locked(stage) == null, "45 fps without a size warning: said, not locked");
            r = PerfTest.Run(game, stage, true, said.Add);
            Check((PerfTest.Locked(stage) ?? "").Contains("below full speed"), "45 fps with one: locked (" + PerfTest.Locked(stage) + ")");
            Environment.SetEnvironmentVariable("EXMS_FAKE_MODE", null);
            PerfTest.Unlock(stage.Key);

            Console.WriteLine("A test that stopped halfway");
            Replacements.Replace(game, "BFU", "U50");   // the test's swap still there, the player's own (TRN) off for it, Tom's mod aside
            File.Move(tom, Path.Combine(game, "dinput8.dll.ex_more_stuff_test"));
            File.WriteAllText(Path.Combine(Game.PatchFolder(game), "ex_more_stuff_test.json"),
                "{\"Stage\":\"BFU\",\"TheirStage\":\"TRN\",\"Fighter\":null,\"Config\":null,\"TheirSwap\":0,\"Moved\":true,\"Began\":0}");
            Check(PerfTest.Recover(game) == null, "Recover says nothing went wrong");
            theirs = Replacements.Load(game);
            Check(theirs.Count == 1 && theirs[0].Source == "TRN" && clean(), "the test's swap goes, the player's own replacement, Tom's mod and the record are back");
            Check(PerfTest.Recover(game) == null, "and again: nothing to do");
            Replacements.Restore(game, "BFU");

            Console.WriteLine("Color pictures");
            int runs = Log(game).Length;
            var colors = PerfTest.ColorPictures(game, costume, null, said.Add).Values.ToList();
            log = Log(game).Skip(runs).ToArray();
            Check(colors.Count == 10 && log.Length == 10 && log.All(l => l.StartsWith("p1=0 p2=1 stage=0") && l.Contains("swapped=True")),
                  "10 runs: Ryu (in the custom costume) with Ken on Training Stage: " + (log.Length > 0 ? log[0] : ""));
            Check(colors.All(c => c.Width == 256 && c.Height == 384), "each 256 x 384");
            Func<System.Drawing.Bitmap, System.Drawing.Color> middle = c => c.GetPixel(128, 300);
            Check(Enumerable.Range(0, 10).All(i => middle(colors[i]).R == 20 + 20 * i && middle(colors[i]).G == 0 && middle(colors[i]).A == 255),
                  "P1 in color 1 ... 10, cut out (red " + string.Join(",", colors.Select(c => middle(c).R)) + ")");
            Check(colors.All(c => c.GetPixel(2, 2).A == 0 && Enumerable.Range(0, 256).All(x => c.GetPixel(x, 200).G == 0)), "transparent around him, nothing of P2");
            Check(colors.All(c => Enumerable.Range(0, 384).Count(y => c.GetPixel(128, y).A > 0) > 300), "standing tall in the picture (P1's 600 pixels to about 360)");
            Check(CostumeSwaps.Load(game).Count == 0 && clean(), "the Original and everything else put back after");
            foreach (var c in colors) c.Dispose();
            runs = Log(game).Length;
            var some = PerfTest.ColorPictures(game, costume, new[] { 10, 3 }, said.Add);
            Check(Log(game).Length == runs + 2 && some.Keys.SequenceEqual(new[] { 3, 10 }) && middle(some[3]).R == 60 && middle(some[10]).R == 200,
                  "only colors 3 and 10 picked: two runs, those two pictures (red " + string.Join(",", some.Values.Select(c => middle(c).R)) + ")");
            Check(said.Last().StartsWith("Color 10 (2 of 2)"), "said as " + said.Last());
            foreach (var c in some.Values) c.Dispose();

            Console.WriteLine("A speck left of P1");
            // Female Shao Kahn (user 2026-10-13): a little emblem apart from her, left of her, was taken for her
            using (var layer = new System.Drawing.Bitmap(1600, 900, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = System.Drawing.Graphics.FromImage(layer))
                {
                    g.Clear(System.Drawing.Color.Transparent);
                    g.FillRectangle(System.Drawing.Brushes.Yellow, 60, 500, 10, 10);    // the speck
                    g.FillRectangle(System.Drawing.Brushes.Red, 300, 250, 200, 600);    // P1
                    g.FillRectangle(System.Drawing.Brushes.Lime, 1100, 300, 250, 550);  // P2
                }
                using (var cut = PerfTest.Cutout(layer))
                    Check(cut.GetPixel(128, 300).R == 255 && cut.GetPixel(128, 300).G == 0 && Enumerable.Range(0, 384).Count(y => cut.GetPixel(128, y).A > 0) > 300,
                          "P1 is the biggest thing on the left, the speck left out: " + cut.GetPixel(128, 300));
            }

            Console.WriteLine("Color pictures of a docked file (not in the game)");
            string dockedZip = Path.Combine(scratch, "docked RYU_83.zip");
            using (ZipArchive archive = ZipFile.Open(dockedZip, ZipArchiveMode.Create))
                foreach (var f in Costume("RYU_83", "docked 83"))
                    using (Stream s = archive.CreateEntry(f.Key).Open()) s.Write(f.Value, 0, f.Value.Length);
            var docked = new Item { Type = "costume", Fighter = "RYU", Slot = 83, Name = "Docked" };
            runs = Log(game).Length;
            var fromZip = PerfTest.ColorPictures(game, docked, new[] { 2 }, said.Add, false, dockedZip);
            log = Log(game).Skip(runs).ToArray();
            Check(fromZip.Keys.SequenceEqual(new[] { 2 }) && log.Length == 1 && log[0].Contains("swapped=True") && log[0].Contains("wears=docked_83_color_1"),
                  "one run, Ryu wearing the docked file's costume, from its zip: " + (log.Length > 0 ? log[0] : ""));
            Check(CostumeSwaps.Load(game).Count == 0 && clean() && !Directory.Exists(Path.Combine(AppFolders.Root, "docked_costume")), "the Original back after, the unpacked files gone");
            Func<bool> outOfGame = () => !Installer.Load(game).Any(i => i.Item.Slot == 83) && !File.Exists(Path.Combine(Game.PatchFolder(game), "battle", "chara", "RYU", "RYU_83.obj.emo"));
            Check(outOfGame(), "it was never put in the game");
            PerfTest.PutPictures(game, docked, new Installed { Item = docked, Package = dockedZip, Source = Docked.Source }, fromZip);
            using (ZipArchive archive = ZipFile.OpenRead(dockedZip)) Check(archive.Entries.Any(e => e.Name == "RYU_83_02.png"), "its picture went into its zip");
            Check(outOfGame(), "and it still isn't in the game");
            foreach (var c in fromZip.Values) c.Dispose();

            Console.WriteLine("Files put in the patch folder by hand");
            var hand = new[] { @"battle\stage\STG_BFU.emz", @"battle\stage\STG_TRN.tex.emz", @"battle\chara\HUG\HUG_01.obj.emo", @"battle\chara\RYU\RYU_01_02.col.emb" }
                .Select(f => Path.Combine(Game.PatchFolder(game), f)).ToArray();
            foreach (string f in hand) { Directory.CreateDirectory(Path.GetDirectoryName(f)); File.WriteAllText(f, "hand: " + Path.GetFileName(f)); }
            int before = Log(game).Length;
            PerfTest.CheckPc(game, said.Add);
            log = Log(game);
            Check(log.Length == before + 1 && log.Last().Contains("swapped=False hand=False"), "the PC check plays the game's own Blast Furnace, Hugo and Poison: " + log.Last());
            Check(hand.All(f => File.Exists(f) && File.ReadAllText(f) == "hand: " + Path.GetFileName(f)) && !Directory.Exists(Path.Combine(Game.PatchFolder(game), "ex_more_stuff_test_aside")),
                  "the hand-placed files are back after, the aside folder gone");
            PerfTest.Run(game, costume, false, said.Add);
            log = Log(game);
            Check(log.Last().StartsWith("p1=0 p2=0 stage=0") && log.Last().Contains("swapped=True hand=False"), "a costume test: the game's own Training Stage and Ryu's Original under the custom costume: " + log.Last());
            Check(hand.All(f => File.Exists(f) && File.ReadAllText(f) == "hand: " + Path.GetFileName(f)) && CostumeSwaps.Load(game).Count == 0, "all back after it too");
            foreach (string f in hand) File.Delete(f);

            Console.WriteLine(failures == 0 ? "All passed" : failures + " failed");
            Environment.Exit(failures == 0 ? 0 : 1);
        }
    }
}
