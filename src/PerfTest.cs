using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace ExMoreStuff
{
    // The performance test (user 2026-10-11; docs\STAGE_PERFORMANCE_PLAN.md in the modding project): the game's own
    // benchmark run with the fight EX More Stuff asks for. EX More Stuff puts its helper (res\helper\dinput8.dll, from
    // helper\exms_helper.cpp) next to SSFIV.exe for the run only; it boots the game straight into the benchmark with
    // that fight, writes the benchmark's numbers and closes the game.
    // - The PC check (user: "where the user's PC stands"), once, the first time the player installs or applies
    //   anything: Hugo vs Poison on Blast Furnace, the game's heaviest stage (Weight.BiggestStage).
    // - A custom stage (the player's choice: its card's green circle or "!"): the same fight with the custom stage in
    //   Blast Furnace's place (Replacements, as the Stages page does it), compared with the PC check.
    // - A custom costume: its fighter on both sides (user) on Training Stage, the custom costume in the Original's place
    //   (CostumeSwaps), compared with the same fight in the Original (once per fighter).
    // Anything past its warning line that fails its test (doesn't load, or doesn't keep full speed) is switched off and
    // can't be switched on again on this PC (user: "should not be loadable": it would crash the game for its owner and
    // break the match for P2), whatever line the player set by hand (Weight.Lockable); a new version of it may be tried.
    // Each test also moves the automatic warning lines (Weight.FromPcCheck, Weight.Learn). The comparison runs are kept per PC and graphics
    // settings. Everything is put back after a run: the swap, the player's own swap or replacement in that place,
    // another dinput8.dll that was there (Tom's Round BGM mod), config.ini; at the next start too if it stopped halfway.
    static class PerfTest
    {
        public class Result
        {
            public double Average, Lowest, Baseline;
            public string Against;   // what the comparison ran on: "Blast Furnace", "Ryu's Original costume"
            public bool FullSpeed { get { return Average >= FullSpeedFps; } }
            /// <summary>How much slower than the game's own, in percent (0 or less: as fast or faster).</summary>
            public double Slower { get { return Baseline > 0 ? (Baseline - Average) * 100 / Baseline : 0; } }
        }

        /// <summary>The game didn't get through the test with it (it crashed, closed or never finished): it didn't load.</summary>
        public class LoadFailed : Exception { public LoadFailed(string message) : base(message) { } }

        const double FullSpeedFps = 59;          // the game plays at 60: an average at it or above keeps full speed
        const string Helper = "dinput8.dll", Request = "exms_benchmark.txt", Aside = "dinput8.dll.ex_more_stuff_test";
        const string PendingName = "ex_more_stuff_test.json";
        static readonly TimeSpan Longest = TimeSpan.FromMinutes(6);
        // the PC check's fight (user: Blast Furnace, the heaviest, then Hugo vs Poison), also a custom stage's
        const string Heaviest = "BFU";
        static readonly string[] PcFighters = { "HUG", "PSN" };
        static readonly int[] PcFight = { Array.IndexOf(Fighters.Codes, PcFighters[0]), Array.IndexOf(Fighters.Codes, PcFighters[1]), StageId(Heaviest) };

        // the game's stage IDs: the place in Stages.Codes, with 22 and 23 left out (unused) from Pit Stop 109 on
        static int StageId(string code) { int i = Array.IndexOf(Stages.Codes, code); return i < 22 ? i : i + 2; }

        // for test\PerfTestTest.cs: another config.ini, and no need for Steam (its made-up game doesn't use it)
        public static string ConfigFile = null;
        public static bool NeedSteam = true;

        static string ConfigPath
        {
            get { return ConfigFile ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CAPCOM", "SUPERSTREETFIGHTERIV", "config.ini"); }
        }

        static string ResultsFile { get { return AppFolders.DataFile("performance.json"); } }
        static string PendingPath(string game) { return Path.Combine(Game.PatchFolder(game), PendingName); }

        /// <summary>Why a test can't run now, or null.</summary>
        public static string Blocked()
        {
            if (Game.IsRunning()) return "Street Fighter IV is running: close it first";
            if (NeedSteam && Process.GetProcessesByName("steam").Length == 0) return "Steam isn't running: the game needs it to start";
            return null;
        }

        /// <summary>The PC check's result for the game's graphics settings now (the average), or null before it ran.</summary>
        public static double? PcResult()
        {
            double average;
            return Baselines().TryGetValue(PcKey(), out average) ? average : (double?)null;
        }

        static string PcKey() { return "pc:" + GraphicsKey(); }

        /// <summary>The PC check: Hugo vs Poison on the game's own Blast Furnace (off the window's thread, about 12 s).</summary>
        public static Result CheckPc(string game, Action<string> say)
        {
            string blocked = Blocked();
            if (blocked != null) throw new InvalidOperationException(blocked);
            string key = PcKey();   // the settings as the player has them (config.ini comes back after)
            try
            {
                Begin(game, Heaviest, null);
                say("Checking how this PC runs the game: the game opens for a few seconds and closes by itself...");
                Result r;
                try { r = Benchmark(game, PcFight, DevMode.Shot("PC check - Blast Furnace, Hugo vs Poison")); }
                catch (LoadFailed ex) { throw new InvalidOperationException("the game's own Blast Furnace didn't finish its test: " + ex.Message); }
                SaveBaseline(key, r.Average);
                Weight.FromPcCheck(r.Average);   // where the stages' warning line starts on this PC (user)
                return r;
            }
            finally { Recover(game); }
        }

        /// <summary>Tests an installed custom stage or costume (off the window's thread: about 12 seconds a run; a stage is
        /// compared with the PC check, run first if it hasn't been; a costume runs once, no comparison: user 2026-10-13,
        /// "dont run the test twice ... just run the costume test"). Throws LoadFailed when the game didn't get through it.
        /// `flagged`: past its warning line (Weight.Lockable), so failing locks it (Locked). The automatic warning line learns
        /// from it.</summary>
        public static Result Run(string game, Item item, bool flagged, Action<string> say)
        {
            string blocked = Blocked();
            if (blocked != null) throw new InvalidOperationException(blocked);
            string fighter = item.IsCostume ? item.Fighter : null;
            int id = fighter != null ? Array.IndexOf(Fighters.Codes, fighter) : 0;
            int[] fight = item.IsStage ? PcFight : new[] { id, id, 0 };   // a costume: its fighter on both sides, Training Stage
            string against = item.IsStage ? Stages.Name(Heaviest) : null;   // a costume: nothing to compare with
            string pcKey = PcKey();   // the settings as the player has them, read before a run changes config.ini
            double times = Weight.TimesInGame(game, item);   // its files as installed, before the test swaps anything
            try
            {
                Begin(game, item.IsStage ? Heaviest : null, fighter);
                double baseline = 0;
                if (item.IsStage && !Baselines().TryGetValue(pcKey, out baseline))
                {
                    say("Checking how this PC runs the game's own Blast Furnace first (once)...");
                    try { baseline = Benchmark(game, fight, DevMode.Shot(against + " (comparison)")).Average; }
                    catch (LoadFailed ex) { throw new InvalidOperationException("the game's own " + against + " didn't finish its test: " + ex.Message); }
                    SaveBaseline(pcKey, baseline);
                    Weight.FromPcCheck(baseline);   // that was the PC check
                }
                say("Putting it in place of " + (item.IsStage ? against : Fighters.Name(fighter) + "'s Original costume") + " for the test...");
                if (item.IsStage) Replacements.Replace(game, Heaviest, item.Code);
                else CostumeSwaps.Replace(game, fighter, 1, item.Slot);
                say("Testing it: the game opens for a few seconds and closes by itself...");
                Result result;
                try { result = Benchmark(game, fight, DevMode.Shot(Shown(item))); }
                catch (LoadFailed ex)
                {
                    Weight.Learn(item.IsStage, times, false);
                    if (flagged) Lock(item, "it didn't load in the game's test on this PC (" + ex.Message + ")");
                    throw;
                }
                result.Baseline = baseline;
                result.Against = against;
                SaveResult(item.Key, result);
                Weight.Learn(item.IsStage, times, result.FullSpeed);
                if (flagged && !result.FullSpeed) Lock(item, "it ran at " + result.Average.ToString("0") + " fps in the game's test on this PC, below full speed (60)");
                return result;
            }
            finally { Recover(game); }
        }

        // What has to come back, written down before anything changes (Recover reads it, also after a crash); then the
        // place the test uses is cleared: another dinput8.dll aside, the player's own replacement or swap there off, and
        // files put in the patch folder by hand for the stage it plays on or the fighter's Original (a port over Blast
        // Furnace, a skin over Ryu's Original: user, "it loaded the wrong scenario") aside, so the game's own load.
        static void Begin(string game, string stage, string fighter)
        {
            if (File.Exists(PendingPath(game))) Recover(game);
            var pending = new Pending { Stage = stage, Fighter = fighter, Began = DateTime.UtcNow.Ticks };
            if (File.Exists(ConfigPath)) pending.Config = Convert.ToBase64String(File.ReadAllBytes(ConfigPath));
            Replacement theirStage = stage != null ? Replacements.Load(game).FirstOrDefault(r => r.Stage == stage) : null;
            pending.TheirStage = theirStage != null ? theirStage.Source : null;
            CostumeSwap theirSwap = fighter != null ? CostumeSwaps.Load(game).FirstOrDefault(s => s.Fighter == fighter && s.Costume == 1) : null;
            pending.TheirSwap = theirSwap != null ? theirSwap.Source : 0;
            pending.Moved = File.Exists(Path.Combine(game, Helper));
            Write(game, pending);
            if (pending.Moved) File.Move(Path.Combine(game, Helper), Path.Combine(game, Aside));   // Tom's Round BGM mod, or another mod's
            if (pending.TheirStage != null) { string note = Replacements.Restore(game, stage); if (note != null) throw new IOException(note); }
            if (pending.TheirSwap != 0) { string note = CostumeSwaps.Restore(game, fighter, 1); if (note != null) throw new IOException(note); }
            // what the run plays: Hugo vs Poison on Blast Furnace, or the costume's fighter on Training Stage
            pending.Aside = HandPlaced(game, stage ?? "TRN", fighter != null ? new[] { fighter } : PcFighters);
            Write(game, pending);
            string patch = Game.PatchFolder(game);
            foreach (string relative in pending.Aside)
            {
                string to = Path.Combine(patch, AsideFolder, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                if (File.Exists(to)) throw new IOException("an older copy of " + Path.GetFileName(relative) + " is already in " + AsideFolder);
                File.Move(Path.Combine(patch, relative), to);
            }
        }

        const string AsideFolder = "ex_more_stuff_test_aside";

        // The patch folder's files for the stage a run plays on and the fighter's Original costume (EX More Stuff's own
        // replacement and swap there are already off): put there by hand, so not the game's own.
        static List<string> HandPlaced(string game, string stage, IEnumerable<string> fighters)
        {
            string patch = Game.PatchFolder(game);
            var found = new List<string>();
            foreach (string suffix in StagePack.Suffixes)
            {
                string relative = StagePack.RelativePath(stage, suffix);
                if (File.Exists(Path.Combine(patch, relative))) found.Add(relative);
            }
            foreach (string fighter in fighters)
            {
                string chara = Path.Combine(patch, "battle", "chara", fighter);
                if (!Directory.Exists(chara)) continue;
                var original = new System.Text.RegularExpressions.Regex("^" + fighter + @"_01(\.(obj\.emo|shd\.emo|nml\.emb|bsr|csb)|_\d\d\.(col\.emb|obj\.emm))$",
                                                                       System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                found.AddRange(Directory.GetFiles(chara).Where(f => original.IsMatch(Path.GetFileName(f))).Select(f => f.Substring(patch.Length + 1)));
            }
            return found;
        }

        /// <summary>Puts back what a test changed (its swap, the player's own replacement or swap in that place, the
        /// dinput8.dll it moved aside, config.ini) if one stopped halfway; nothing when no test was going. Returns a note
        /// when something stayed, or null.</summary>
        public static string Recover(string game)
        {
            Pending pending = Read(game);
            if (pending == null || Game.IsRunning()) return null;
            var notes = new List<string>();
            Action<Action> attempt = step => { try { step(); } catch (Exception ex) { notes.Add(ex.Message); } };
            foreach (string file in new[] { Helper, Request })
                attempt(() => { if (IsOurs(game, file)) File.Delete(Path.Combine(game, file)); });   // only ours goes
            // the test's own swap goes, the files moved aside come back, then the player's own replacement or swap
            if (pending.Stage != null) attempt(() => { string n = Replacements.Restore(game, pending.Stage); if (n != null) notes.Add(n); });
            if (pending.Fighter != null) attempt(() => { string n = CostumeSwaps.Restore(game, pending.Fighter, 1); if (n != null) notes.Add(n); });
            string patch = Game.PatchFolder(game);
            foreach (string relative in pending.Aside ?? new List<string>())
                attempt(() =>
                {
                    string from = Path.Combine(patch, AsideFolder, relative), to = Path.Combine(patch, relative);
                    if (!File.Exists(from)) return;
                    if (File.Exists(to)) throw new IOException(Path.GetFileName(relative) + " is in its place again, so the one moved aside stays in " + AsideFolder);
                    File.Move(from, to);
                });
            attempt(() =>
            {
                string aside = Path.Combine(patch, AsideFolder);
                if (!Directory.Exists(aside)) return;
                foreach (string dir in Directory.GetDirectories(aside, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
                    if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
                if (!Directory.EnumerateFileSystemEntries(aside).Any()) Directory.Delete(aside);
            });
            if (pending.Stage != null && pending.TheirStage != null) attempt(() => Replacements.Replace(game, pending.Stage, pending.TheirStage));
            if (pending.Fighter != null && pending.TheirSwap != 0) attempt(() => CostumeSwaps.Replace(game, pending.Fighter, 1, pending.TheirSwap));
            if (pending.Moved)
                attempt(() => { if (File.Exists(Path.Combine(game, Aside)) && !File.Exists(Path.Combine(game, Helper))) File.Move(Path.Combine(game, Aside), Path.Combine(game, Helper)); });
            // the game's settings as they were, if the test changed them (written since it began)
            if (pending.Config != null && File.Exists(ConfigPath) && File.GetLastWriteTimeUtc(ConfigPath).Ticks > pending.Began)
                attempt(() => File.WriteAllBytes(ConfigPath, Convert.FromBase64String(pending.Config)));
            if (notes.Count == 0) File.Delete(PendingPath(game));
            return notes.Count == 0 ? null : "The last performance test didn't put everything back: " + string.Join("; ", notes);
        }

        // our helper or our request file (the helper's bytes as built in): never another mod's dinput8.dll
        static bool IsOurs(string game, string file)
        {
            string path = Path.Combine(game, file);
            if (!File.Exists(path)) return false;
            if (file == Request) return true;
            using (var sha = SHA256.Create()) return sha.ComputeHash(File.ReadAllBytes(path)).SequenceEqual(sha.ComputeHash(HelperBytes()));
        }

        static byte[] HelperBytes()
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("helper.dll"))
            using (var data = new MemoryStream())
            {
                if (s == null) throw new InvalidOperationException("this copy of EX More Stuff was built without its test helper");
                s.CopyTo(data);
                return data.ToArray();
            }
        }

        // One run of the game with the helper and a request (the fight by the game's IDs, and what else the helper is
        // asked for): the result file's lines; LoadFailed when the game didn't get through it.
        static Dictionary<string, string> RunGame(string game, string request)
        {
            string result = AppFolders.DataFile("benchmark_result.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(result));
            if (File.Exists(result)) File.Delete(result);
            File.WriteAllBytes(Path.Combine(game, Helper), HelperBytes());
            try
            {
                File.WriteAllText(Path.Combine(game, Request), "result=" + result + "\r\n" + request, new UTF8Encoding(false));
                var start = new ProcessStartInfo(Path.Combine(game, "SSFIV.exe")) { WorkingDirectory = game, UseShellExecute = false };
                start.EnvironmentVariables["SteamAppId"] = "45760";   // as EMBER starts it: straight from the exe, Steam still needs to know the game
                start.EnvironmentVariables["SteamGameId"] = "45760";
                using (Process process = Process.Start(start))
                    if (!process.WaitForExit((int)Longest.TotalMilliseconds))
                    {
                        try { process.Kill(); process.WaitForExit(10000); } catch (Exception) { }
                        throw new LoadFailed("the game didn't finish the test in " + Longest.TotalMinutes + " minutes");
                    }
            }
            finally
            {
                foreach (string file in new[] { Helper, Request })
                    try { if (IsOurs(game, file)) File.Delete(Path.Combine(game, file)); } catch (Exception) { }
            }
            if (!File.Exists(result)) throw new LoadFailed("the game closed before the test was done");
            var values = File.ReadAllLines(result).Select(l => l.Split(new[] { '=' }, 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);
            string error;
            if (values.TryGetValue("error", out error)) throw new LoadFailed(error);
            return values;
        }

        static string FightLines(int[] fight) { return "p1=" + fight[0] + "\r\np2=" + fight[1] + "\r\nstage=" + fight[2] + "\r\n"; }

        // One run of the benchmark: its numbers.
        static Result Benchmark(string game, int[] fight, string shot)
        {
            var values = RunGame(game, FightLines(fight) + (shot != null ? "shot=" + shot + "\r\n" : ""));   // developer mode: a frame of the fight
            double average, lowest;
            if (!values.ContainsKey("average") || !double.TryParse(values["average"], NumberStyles.Float, CultureInfo.InvariantCulture, out average))
                throw new LoadFailed("the test gave no average");
            double.TryParse(values.ContainsKey("lowest") ? values["lowest"] : "0", NumberStyles.Float, CultureInfo.InvariantCulture, out lowest);
            return new Result { Average = average, Lowest = lowest };
        }

        // Color pictures (user 2026-10-12, for Ember's character select; Tom: "a render of the character with the
        // costume for each color"): the benchmark's fight in training mode (game mode 3 for its 2 at RVA 0x26B00: both
        // fighters stand idle), the color in its request (RVA 0x26C88: 0 is color 1, for both sides), and the helper
        // saves the layer the game draws the fighters on (no stage, no life bars: transparent around them) 60 frames
        // in. P1 is the costume's fighter wearing it (in the Original's place), P2 Ryu (Ken beside Ryu), cut away.
        const string TrainingMode = "patch=26B00:02>03\r\n";
        public const int ColorCount = 10;
        public const int PictureWidth = 256, PictureHeight = 384;   // as the pattern pictures (ColorVariants), for Ember's gallery

        /// <summary>An installed custom costume in the colors asked for (1-10; all when null), P1 standing idle as the game
        /// draws it, cut out (off the window's thread: about 7 seconds of game a color). `say` says which color. A docked
        /// file (`docked`, its zip: not in the game) is unpacked beside the program for the runs and in Ryu's Original's
        /// place from there; the unpacked files go after.</summary>
        public static SortedDictionary<int, Bitmap> ColorPictures(string game, Item item, IList<int> colors, Action<string> say, bool fullSize = false, string docked = null)
        {
            colors = colors ?? Enumerable.Range(1, ColorCount).ToList();
            string blocked = Blocked();
            if (blocked != null) throw new InvalidOperationException(blocked);
            string fighter = item.Fighter;
            int id = Array.IndexOf(Fighters.Codes, fighter), other = id == 0 ? 1 : 0;
            var pictures = new SortedDictionary<int, Bitmap>();
            string unpacked = docked != null ? AppFolders.DataFile("docked_costume") : null;
            try
            {
                Begin(game, null, fighter);
                if (docked != null) CostumeSwaps.ReplaceWith(game, fighter, 1, item.Slot, Unpack(docked, item, unpacked));
                else CostumeSwaps.Replace(game, fighter, 1, item.Slot);
                string shot = AppFolders.DataFile("color_picture.png");
                int done = 0;
                foreach (int color in colors.Where(c => c >= 1 && c <= ColorCount).Distinct().OrderBy(c => c))
                {
                    say("Color " + color + (colors.Count > 1 ? " (" + ++done + " of " + colors.Count + ")" : "") + ": the game opens for a few seconds and closes by itself...");
                    if (File.Exists(shot)) File.Delete(shot);
                    RunGame(game, FightLines(new[] { id, other, 0 }) + TrainingMode + "patch=26C88:00>" + (color - 1).ToString("X2") + "\r\n" +
                                  "shot=" + shot + "\r\nshotat=60\r\nlayer=1\r\nquit=shots\r\n");
                    if (!File.Exists(shot)) throw new LoadFailed("the game saved no picture of color " + color);
                    using (var whole = new Bitmap(new MemoryStream(File.ReadAllBytes(shot)))) pictures[color] = fullSize ? FullSize(whole) : Cutout(whole);
                    File.Delete(shot);
                }
                return pictures;
            }
            catch (Exception)
            {
                foreach (Bitmap p in pictures.Values) p.Dispose();
                throw;
            }
            finally
            {
                Recover(game);
                if (unpacked != null) try { Directory.Delete(unpacked, true); } catch (Exception) { }
            }
        }

        // Stage preview (user 2026-10-14: a docked stage seen in the game, "no characters, screen starts on the left, pans
        // to the right and closes"; its pictures shown on its card, each full size on a click): the docked stage in
        // Training Stage's place from its zip, the benchmark in training mode (both stand idle) with the helper's hide=1
        // (no fighters, no overlay) and slide=1 (held at the left wall 1.5 s, carried to the right in about 8 s, the game
        // running as fast as it can so the camera tilts little: user), pictures at the left wall, the middle and the
        // right wall. About 17 s with the game starting. Begin and Recover put Training Stage and the rest back.
        const string PreviewShots = "shotat=14,55,999\r\nhide=1\r\nslide=1\r\nquit=shots\r\n";
        static readonly int[] PreviewFrames = { 14, 55, 999 };

        /// <summary>Where a docked stage's preview pictures are kept: beside the program (data), a folder per zip.</summary>
        public static string PreviewFolder(string zipPath)
        {
            string key;
            using (var sha = SHA256.Create())
                key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(zipPath).ToLowerInvariant()))).Replace("-", "").Substring(0, 16);
            return Path.Combine(AppFolders.DataFile("stage_previews"), key);
        }

        /// <summary>A docked stage's preview pictures (left, middle, right), the ones there are.</summary>
        public static List<string> PreviewPictures(string zipPath)
        {
            string folder = PreviewFolder(zipPath);
            return PreviewFrames.Select(f => Path.Combine(folder, "preview_" + f + ".png")).Where(File.Exists).ToList();
        }

        /// <summary>The preview picture last made the stage's picture (UseAsStagePicture), or null.</summary>
        public static string StagePictureShot(string zipPath)
        {
            string note = Path.Combine(PreviewFolder(zipPath), "picture.txt");
            try { if (File.Exists(note)) { string shot = Path.Combine(PreviewFolder(zipPath), File.ReadAllText(note).Trim()); if (File.Exists(shot)) return shot; } }
            catch (Exception) { }
            return null;
        }

        /// <summary>One of a docked stage's preview pictures as its stage picture (user 2026-10-14: "from here i should be
        /// able to make one of these images the stage thumbnail"): made 640x360, as stage pictures are, into its zip as
        /// STG_<code>.png (the one there replaced), and noted as the one chosen.</summary>
        public static void UseAsStagePicture(string zipPath, Item item, string shot)
        {
            byte[] png;
            using (var full = new Bitmap(new MemoryStream(File.ReadAllBytes(shot))))
            using (var small = new Bitmap(640, 360))
            {
                using (Graphics g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(full, 0, 0, small.Width, small.Height);
                }
                using (var data = new MemoryStream()) { small.Save(data, ImageFormat.Png); png = data.ToArray(); }
            }
            string name = item.Prefix + ".png";   // STG_<code>.png: the first name a package's picture is looked for under
            using (var zip = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Update))
            {
                foreach (var old in zip.Entries.Where(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)).ToList()) old.Delete();
                using (Stream s = zip.CreateEntry(name).Open()) s.Write(png, 0, png.Length);
            }
            File.WriteAllText(Path.Combine(PreviewFolder(zipPath), "picture.txt"), Path.GetFileName(shot));
        }

        /// <summary>A docked stage (its zip: not in the game) previewed in the game; its pictures' paths. Off the window's
        /// thread.</summary>
        public static List<string> StagePreview(string game, Item item, string zipPath)
        {
            string blocked = Blocked();
            if (blocked != null) throw new InvalidOperationException(blocked);
            var files = new Dictionary<string, byte[]>();
            using (var zip = System.IO.Compression.ZipFile.OpenRead(zipPath))
                foreach (string suffix in StagePack.Suffixes)
                {
                    var entry = zip.Entries.FirstOrDefault(e => e.Name.Equals("STG_" + item.Code + suffix, StringComparison.OrdinalIgnoreCase));
                    if (entry == null) throw new InvalidDataException("STG_" + item.Code + suffix + " isn't in its zip");
                    using (Stream s = entry.Open())
                    using (var data = new MemoryStream()) { s.CopyTo(data); files[suffix] = data.ToArray(); }
                }
            string folder = PreviewFolder(zipPath), shot = Path.Combine(folder, "preview.png");
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
            try
            {
                Begin(game, "TRN", null);
                Replacements.ReplaceWith(game, "TRN", "docked", item.Code, files);
                RunGame(game, FightLines(new[] { 0, 1, StageId("TRN") }) + TrainingMode + "shot=" + shot + "\r\n" + PreviewShots);
            }
            finally { Recover(game); }
            var made = PreviewPictures(zipPath);
            if (made.Count == 0) throw new LoadFailed("the game saved no pictures");
            return made;
        }

        // a docked costume's files out of its zip into `folder` (emptied first), by what follows its prefix
        static Dictionary<string, string> Unpack(string zipPath, Item item, string folder)
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
            var part = new System.Text.RegularExpressions.Regex("^" + item.Prefix + @"(\.(obj\.emo|shd\.emo|nml\.emb|bsr|csb)|_\d\d\.(col\.emb|obj\.emm))$",
                                                                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var zip = System.IO.Compression.ZipFile.OpenRead(zipPath))
                foreach (var entry in zip.Entries.Where(e => part.IsMatch(e.Name)))
                {
                    string rest = entry.Name.Substring(item.Prefix.Length), path = Path.Combine(folder, entry.Name);
                    if (files.ContainsKey(rest)) continue;
                    using (Stream s = entry.Open())
                    using (Stream to = File.Create(path)) s.CopyTo(to);
                    files[rest] = path;
                }
            return files;
        }

        /// <summary>Color pictures into a package of the player's: its zip's <prefix>_<CC>.png replaced for the colors made
        /// (the others stay as they are; ready to share), then the package into the game again (a docked file: not, it
        /// stays out of the game).</summary>
        public static void PutPictures(string game, Item item, Installed record, IDictionary<int, Bitmap> made)
        {
            using (var zip = System.IO.Compression.ZipFile.Open(record.Package, System.IO.Compression.ZipArchiveMode.Update))
                foreach (var picture in made)
                {
                    string entry = item.Prefix + "_" + picture.Key.ToString("D2") + ".png";
                    foreach (var old in zip.Entries.Where(e => string.Equals(e.Name, entry, StringComparison.OrdinalIgnoreCase)).ToList()) old.Delete();
                    using (Stream s = zip.CreateEntry(entry).Open()) picture.Value.Save(s, ImageFormat.Png);
                }
            if (record.Source != Docked.Source) Installer.InstallPackage(game, item, record.Package, "file");
        }

        /// <summary>P1 from the fighters' layer: the first run of columns from the left with anything in them (P2 stands
        /// apart further right; fighters too close: up to the middle), standing at the bottom of a transparent 256 x 384.</summary>
        public static Bitmap Cutout(Bitmap layer)
        {
            Rectangle crop = P1(layer);
            double scale = Math.Min(PictureWidth * 0.92 / crop.Width, PictureHeight * 0.94 / crop.Height);
            int dw = Math.Max(1, (int)Math.Round(crop.Width * scale)), dh = Math.Max(1, (int)Math.Round(crop.Height * scale));
            var picture = new Bitmap(PictureWidth, PictureHeight, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(picture))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                int bottomMargin = (int)(PictureHeight * 0.03);
                g.DrawImage(layer, new Rectangle((PictureWidth - dw) / 2, PictureHeight - bottomMargin - dh, dw, dh), crop, GraphicsUnit.Pixel);
            }
            return picture;
        }

        /// <summary>P1 at the game's own size (user 2026-10-12: "generate full size"): cut out as it is, with a little
        /// room around, transparent.</summary>
        public static Bitmap FullSize(Bitmap layer)
        {
            Rectangle crop = P1(layer);
            const int room = 12;
            var picture = new Bitmap(crop.Width + room * 2, crop.Height + room * 2, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(picture))
            {
                g.Clear(Color.Transparent);
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(layer, new Rectangle(room, room, crop.Width, crop.Height), crop, GraphicsUnit.Pixel);
            }
            return picture;
        }

        // where P1 is in the fighters' layer: of the runs of columns with anything in them (a gap of empty columns apart),
        // the one starting left of the middle with the most drawn in it, so a detached speck left of P1 (Female Shao Kahn's
        // little emblem, user 2026-10-13: it was taken for her) is left out; P2 stands apart further right; fighters too
        // close: up to the middle. As tall as P1.
        static Rectangle P1(Bitmap layer)
        {
            int w = layer.Width, h = layer.Height;
            var px = new int[w * h];
            using (var argb = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(argb)) { g.CompositingMode = CompositingMode.SourceCopy; g.DrawImage(layer, 0, 0, w, h); }
                var data = argb.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                for (int y = 0; y < h; y++) System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, px, y * w, w);
                argb.UnlockBits(data);
            }
            Func<int, int, bool> solid = (x, y) => ((px[y * w + x] >> 24) & 0xFF) > 16;
            var mass = new int[w];   // drawn pixels in each column
            for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) if (solid(x, y)) mass[x]++;
            var runs = new List<int[]>();   // left, right, drawn pixels
            int start = -1, last = -1, gap = 0, minGap = Math.Max(4, w / 64);
            long drawn = 0;
            for (int x = 0; x <= w; x++)
            {
                if (x < w && mass[x] > 0) { if (start < 0) { start = x; drawn = 0; } last = x; gap = 0; drawn += mass[x]; }
                else if (start >= 0 && (++gap >= minGap || x == w)) { runs.Add(new[] { start, last, (int)Math.Min(drawn, int.MaxValue) }); start = -1; }
            }
            int[] p1 = runs.Where(r => r[0] < w / 2).OrderByDescending(r => r[2]).FirstOrDefault();
            if (p1 == null) throw new InvalidDataException("the picture has no fighter in it");
            int left = p1[0], right = p1[1];
            if (right > w * 0.6) right = Math.Max(left + 1, w / 2);   // no gap (the fighters touch): P1's side of the middle
            int top = h, bottom = -1;
            for (int y = 0; y < h; y++)
                for (int x = left; x <= right; x++)
                    if (solid(x, y)) { top = Math.Min(top, y); bottom = Math.Max(bottom, y); break; }
            return new Rectangle(left, top, right - left + 1, bottom - top + 1);
        }

        // The game's graphics settings ([DISPLAY] and [GRAPHICS] of config.ini) as a short fingerprint: comparison runs
        // are made again when they change, as the game makes its own benchmark result invalid then.
        static string GraphicsKey()
        {
            if (!File.Exists(ConfigPath)) return "none";
            var kept = new StringBuilder();
            bool inside = false;
            foreach (string line in File.ReadAllLines(ConfigPath))
            {
                string t = line.Trim();
                if (t.StartsWith("[")) inside = t == "[DISPLAY]" || t == "[GRAPHICS]";
                if (inside) kept.AppendLine(t);
            }
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(kept.ToString()))).Replace("-", "").Substring(0, 12).ToLowerInvariant();
        }

        // performance.json beside the program: { "baselines": { key: average }, "results": { item key: {...} },
        // "locked": { item key: { "why", "version", "when" } } }, read once (Locked is asked often) and kept in step.
        static Dictionary<string, object> cache;
        static string cachedFrom;

        static Dictionary<string, object> Load()
        {
            if (cache != null && cachedFrom == ResultsFile) return cache;
            try { cache = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(ResultsFile)); }
            catch (Exception) { cache = null; }
            cache = cache ?? new Dictionary<string, object>();
            cachedFrom = ResultsFile;
            return cache;
        }

        static void Save(Dictionary<string, object> j)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ResultsFile));
            File.WriteAllText(ResultsFile, new JavaScriptSerializer().Serialize(j));
            cache = j;
            cachedFrom = ResultsFile;
        }

        static Dictionary<string, object> Part(Dictionary<string, object> j, string name)
        {
            object v;
            var part = j.TryGetValue(name, out v) ? v as Dictionary<string, object> : null;
            if (part == null) j[name] = part = new Dictionary<string, object>();
            return part;
        }

        static Dictionary<string, double> Baselines()
        {
            return Part(Load(), "baselines").ToDictionary(p => p.Key, p => Convert.ToDouble(p.Value));
        }

        static void SaveBaseline(string key, double average)
        {
            var j = Load();
            Part(j, "baselines")[key] = Math.Round(average, 2);
            Save(j);
        }

        static void SaveResult(string key, Result r)
        {
            var j = Load();
            Part(j, "results")[key] = new Dictionary<string, object>
            {
                { "average", Math.Round(r.Average, 2) }, { "lowest", Math.Round(r.Lowest, 2) }, { "baseline", Math.Round(r.Baseline, 2) },
                { "against", r.Against }, { "when", DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) },
            };
            Save(j);
        }

        /// <summary>The last test of an item on this PC, or null.</summary>
        public static Result Last(string key)
        {
            object v;
            var r = Part(Load(), "results").TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
            if (r == null) return null;
            try
            {
                return new Result { Average = Convert.ToDouble(r["average"]), Lowest = Convert.ToDouble(r["lowest"]), Baseline = Convert.ToDouble(r["baseline"]),
                                    Against = Convert.ToString(r["against"]) };
            }
            catch (Exception) { return null; }
        }

        // an item as the player knows it, for its developer-mode frame: "Stage U01 - Mishima Estate Roof", "Ryu Slot 80 - Manon"
        static string Shown(Item item) { return (item.IsStage ? "" : Fighters.Name(item.Fighter) + " ") + item.Title; }

        // which version of an item failed: a catalog item's fingerprint and version; the same for every copy of a package
        static string Version(Item item) { return (item.Sha256 ?? "") + "|" + (item.Version ?? ""); }

        static void Lock(Item item, string why)
        {
            var j = Load();
            Part(j, "locked")[item.Key] = new Dictionary<string, object>
            {
                { "why", why }, { "version", Version(item) }, { "when", DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) },
            };
            Save(j);
        }

        /// <summary>Why this version of an item can't be switched on on this PC (it had a size warning and failed its
        /// test), or null when it can.</summary>
        public static string Locked(Item item)
        {
            object v;
            var r = Part(Load(), "locked").TryGetValue(item.Key, out v) ? v as Dictionary<string, object> : null;
            if (r == null || Convert.ToString(r["version"]) != Version(item)) return null;
            return Convert.ToString(r["why"]);
        }

        /// <summary>A new copy of an item went in (Installer.InstallPackage: an update, a file added again): it may be tried again.</summary>
        public static void Unlock(string key)
        {
            var j = Load();
            if (Part(j, "locked").Remove(key)) Save(j);
        }

        /// <summary>A result in a sentence or two, for the player.</summary>
        public static string Describe(string name, Result r)
        {
            // a costume has nothing to compare with (one run, user 2026-10-13); a stage, the PC check's Blast Furnace
            string compared = r.Baseline <= 0 || string.IsNullOrEmpty(r.Against) ? ""
                            : r.Slower < 5 ? ": about as fast as the game's own " + r.Against + " (" + r.Baseline.ToString("0") + " fps)"
                            : ": " + r.Slower.ToString("0") + "% slower than the game's own " + r.Against + " (" + r.Baseline.ToString("0") + " fps)";
            return name + " ran at " + r.Average.ToString("0") + " fps on average in the game's benchmark" + compared + ". " +
                   (r.FullSpeed ? "It should play at full speed on this PC." : "It may not keep full speed (60 fps) on this PC.");
        }

        sealed class Pending
        {
            public string Stage, Fighter, Config, TheirStage;
            public List<string> Aside;   // hand-placed patch files moved to ex_more_stuff_test_aside for the test
            public int TheirSwap;
            public bool Moved;
            public long Began;   // UTC ticks
        }

        static void Write(string game, Pending p)
        {
            Directory.CreateDirectory(Game.PatchFolder(game));
            File.WriteAllText(PendingPath(game), new JavaScriptSerializer().Serialize(p));
        }

        static Pending Read(string game)
        {
            try { return File.Exists(PendingPath(game)) ? new JavaScriptSerializer().Deserialize<Pending>(File.ReadAllText(PendingPath(game))) : null; }
            catch (Exception) { return null; }
        }
    }
}
