// GameBanana's list kept on the PC and built into the program, without GameBanana: the built-in list reads back whole
// (mods, files, links, checksums, contents), and what's saved reads back the same. Usage: GameBananaKeptTest <scratch folder>
using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ExMoreStuff
{
    static class GameBananaKeptTest
    {
        static int failures;

        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) failures++; }

        static void Main(string[] args)
        {
            var load = typeof(GameBanana).GetMethod("Load", BindingFlags.NonPublic | BindingFlags.Static);
            Func<string, System.Collections.Generic.List<GameBanana.Mod>> read = text => { var a = new object[] { text, DateTime.MinValue }; return (System.Collections.Generic.List<GameBanana.Mod>)load.Invoke(null, a); };
            Console.WriteLine("The list built into the program");
            string builtIn;
            using (var s = typeof(GameBanana).Assembly.GetManifestResourceStream("gamebanana.json")) builtIn = new StreamReader(s).ReadToEnd();
            var mods = read(builtIn);
            Check(mods.Count >= 30, mods.Count + " mods");
            var hado = mods.FirstOrDefault(m => m.Id == 680656);
            Check(hado != null && hado.Name.StartsWith("Dark Hado Blanka") && hado.Author == "retrostuff64" && hado.IsSkin, "Dark Hado Blanka, by retrostuff64, a skin");
            Check(hado != null && hado.Files.Count == 8 && hado.Files.All(f => f.Download.StartsWith("https://") && f.Md5.Length == 32 && f.Clean && f.Contents.Count > 0),
                  "its 8 files, each with its link, MD5, scan result and contents");
            Check(SharedCodes.Holders(mods).ContainsKey("BLK 08") && SharedCodes.HeldBy.ContainsKey("stage B01"), "seats from it: Blanka 08, stage B01");

            Check(!builtIn.Contains("\"checking\"") ? mods.All(m => m.Files.All(f => !f.Checking)) : true, "a list from before \"checking\" was kept: nothing taken as still being checked");

            Console.WriteLine("Saved and read back");
            Directory.CreateDirectory(args[0]);
            string path = Path.Combine(args[0], "kept.json");
            var fresh = hado.Files.Last();
            fresh.Clean = false; fresh.Checking = true;   // a new upload GameBanana is still checking (user 2026-10-13)
            GameBanana.Save(mods, path);
            var again = read(File.ReadAllText(path));
            Check(again.Count == mods.Count && again.Zip(mods, (a, b) => a.Id == b.Id && a.Name == b.Name && a.Files.Count == b.Files.Count &&
                  a.Files.Zip(b.Files, (x, y) => x.Download == y.Download && x.Md5 == y.Md5 && x.Contents.SequenceEqual(y.Contents)).All(z => z)).All(z => z), "the same mods, files, links and contents");
            var back = again.First(m => m.Id == 680656).Files.Last();
            Check(back.Checking && !back.Clean && again.First(m => m.Id == 680656).Files.Count(f => f.Checking) == 1, "a file GameBanana is still checking reads back as such");

            Console.WriteLine("Collections kept with the list (user 2026-10-14)");
            Check(GameBanana.Collections.Count == 0, "none in a list kept before collections were read, or none that keeps the rule");
            string withOne = File.ReadAllText(path).Replace("\"collections\":[]",
                "\"collections\":[{\"id\":900001,\"name\":\"SFxT Tekken Crossovers\",\"owner\":\"oops_whats\",\"ownerId\":123,\"mods\":[680656,726996]}]");
            Check(withOne.Contains("900001"), "a list with one collection");
            read(withOne);
            var c = GameBanana.Collections.FirstOrDefault();
            Check(c != null && c.Name == "SFxT Tekken Crossovers" && c.Owner == "oops_whats" && c.OwnerId == 123 && c.Mods.SequenceEqual(new long[] { 680656, 726996 }) &&
                  c.Page == "https://gamebanana.com/collections/900001", "its collection read back: name, maker, mods, page");
            GameBanana.Save(again, path);
            Check(File.ReadAllText(path).Contains("\"name\":\"SFxT Tekken Crossovers\""), "and saved again with the list");
            Directory.Delete(args[0], true);
            Console.WriteLine("A file's first 2 minutes (user 2026-10-15: \"it just breaks if you try using it\")");
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var young = new GameBanana.ModFile { Added = now - 30, Clean = true };
            var passed = new GameBanana.ModFile { Added = now - 200, Clean = true };
            var scanning = new GameBanana.ModFile { Added = now - 200, Checking = true };
            Check(!young.Ready && young.Waiting && young.SettlesIn > 80 && young.SettlesIn <= 90, "passed 30 s ago: still checking, ready in about 90 s");
            Check(passed.Ready && !passed.Waiting && passed.SettlesIn == 0, "passed, uploaded 200 s ago: offered");
            Check(!scanning.Ready && scanning.Waiting, "GameBanana still scanning it: not offered");

            Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
            Environment.Exit(failures == 0 ? 0 : 1);
        }
    }
}
