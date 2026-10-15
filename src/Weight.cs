using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace ExMoreStuff
{
    // The size check (user 2026-10-11, docs\STAGE_PERFORMANCE_PLAN.md in the modding project): how a custom stage or
    // costume compares with the game's biggest, from a few header bytes (nothing is unpacked), and a warning when it's
    // past the player's line (Advanced > Performance warnings). A warning only informs: nothing is ever blocked,
    // switched off or removed because of it (user: "a warning is not a halt of the player's desires").
    static class Weight
    {
        // The game's biggest, measured 2026-10-11 on USF4 (resource, dlc and the official patch folders): the same on every PC.
        public const long BiggestStage = 30525378;   // Blast Furnace (BFU) unpacked: STG_BFU.emz + STG_BFU.tex.emz
        public const int MostBones = 507;            // RYU_05
        public const long BiggestModel = 2674248;    // DJY_04.obj.emo
        public const long BiggestColor = 7035320;    // BLK_07_01.col.emb
        public const double StageDefault = 4, CostumeDefault = 3;
        public const double Lowest = 1, Highest = 50;

        // Each line is the automatic one: the default, then what this PC's tests showed (the PC check sets the stages'
        // start, FromPcCheck; a test moves it, Learn). Players don't set it (user 2026-10-14: "just make it unchangeable";
        // from 2026-10-12 a line could be typed in Advanced, and a line set that way is no longer read). In settings.json:
        // stageWarningAuto and stageWarningBy ("tests" or none); the same for costumes. Read once, kept in step.
        sealed class Line
        {
            public double Auto;
            public bool Learned;
        }

        static Line stage, costume;

        static Line Lines(bool forStage)
        {
            if (stage == null)
            {
                stage = Read(Settings.StageWarningBy, Settings.StageWarning, Settings.StageWarningAuto, StageDefault);
                costume = Read(Settings.CostumeWarningBy, Settings.CostumeWarning, Settings.CostumeWarningAuto, CostumeDefault);
            }
            return forStage ? stage : costume;
        }

        static Line Read(string by, double hand, double? auto, double fallback)
        {
            return new Line { Auto = auto ?? fallback, Learned = auto != null };   // `hand`: a line typed before 2026-10-14, not read
        }

        static void Keep(bool forStage)
        {
            Line l = Lines(forStage);
            string by = l.Learned ? "tests" : null;
            if (forStage) { Settings.StageWarningAuto = l.Learned ? l.Auto : (double?)null; Settings.StageWarningBy = by; }
            else { Settings.CostumeWarningAuto = l.Learned ? l.Auto : (double?)null; Settings.CostumeWarningBy = by; }
        }

        /// <summary>Stages more than this many times the biggest SF4 stage get a warning (4 by default, then as tests set it).</summary>
        public static double StageLine { get { return Lines(true).Auto; } }

        /// <summary>The same for costumes: bones, model or a color more than this many times the game's biggest (3 by default).</summary>
        public static double CostumeLine { get { return Lines(false).Auto; } }

        /// <summary>Where a line came from, for Advanced.</summary>
        public static string Source(bool forStage)
        {
            Line l = Lines(forStage);
            return l.Learned ? "from your tests" : (forStage ? StageDefault : CostumeDefault) + " by default";
        }

        /// <summary>The PC check's result (Hugo vs Poison on Blast Furnace, average fps) sets where the stages' line
        /// starts (user): how many times Blast Furnace's size this PC could take before dropping under 60 fps, if speed
        /// went down with size, in steps of 0.5 (297 fps: 4.5; 120 fps: 2). A rough start; tests move it from there.</summary>
        public static void FromPcCheck(double fps)
        {
            Line l = Lines(true);
            l.Auto = Math.Max(Lowest, Math.Min(Highest, Math.Floor(fps / 60 * 2) / 2));
            l.Learned = true;
            Keep(true);
        }

        /// <summary>A test of something `times` the game's biggest: passed (it loaded and kept full speed) above the
        /// automatic line, the line goes up to it; failed at or under it, just under it. Results that disagree keep the
        /// line between the biggest pass and the smallest fail, leaning careful (the last fail wins).</summary>
        public static void Learn(bool forStage, double times, bool passed)
        {
            if (double.IsNaN(times) || times <= 0) return;
            Line l = Lines(forStage);
            double tenth = Math.Floor(times * 10) / 10;
            if (passed && times > l.Auto) l.Auto = Math.Ceiling(times * 10) / 10;
            else if (!passed && times <= l.Auto) l.Auto = tenth < times ? tenth : Math.Round(times - 0.1, 1);
            else return;
            l.Auto = Math.Max(Lowest, Math.Min(Highest, l.Auto));
            l.Learned = true;
            Keep(forStage);
        }

        /// <summary>Whether an installed item would be locked if it failed its test (PerfTest): past its line.</summary>
        public static bool Lockable(string game, Item item) { return TimesInGame(game, item) > (item.IsStage ? StageLine : CostumeLine); }

        /// <summary>How many times the game's biggest an installed item is (0 when its files can't be read).</summary>
        public static double TimesInGame(string game, Item item)
        {
            string said;
            return Measure(item, "", Opener(game, item), Sizer(game, item), out said);
        }

        static Func<string, Stream> Opener(string game, Item item)
        {
            string folder = Game.FolderFor(game, item);
            return file => { string p = Path.Combine(folder, file); return File.Exists(p) ? File.OpenRead(p) : null; };
        }

        static Func<string, long> Sizer(string game, Item item)
        {
            string folder = Game.FolderFor(game, item);
            return file => { var f = new FileInfo(Path.Combine(folder, file)); return f.Exists ? f.Length : -1; };
        }

        /// <summary>The warning for an installed item (its files in the game), or null. `name` is how the player knows it.</summary>
        public static string InGame(string game, Item item, string name)
        {
            return Check(item, name, Opener(game, item), Sizer(game, item));
        }

        /// <summary>The warning for a package (a zip of an item's files, as EX More Stuff keeps them), or null.</summary>
        public static string InZip(string zip, Item item, string name)
        {
            try
            {
                using (ZipArchive archive = ZipFile.OpenRead(zip))
                {
                    Func<string, ZipArchiveEntry> entry = file => archive.Entries.FirstOrDefault(e => string.Equals(e.Name, file, StringComparison.OrdinalIgnoreCase));
                    return Check(item, name, file => { var e = entry(file); return e != null ? e.Open() : null; },
                                             file => { var e = entry(file); return e != null ? e.Length : -1; });
                }
            }
            catch (Exception) { return null; }   // a zip that can't be read: Apply says why
        }

        // `open` and `size` give a file of the item by name (null and -1 when it isn't there)
        static string Check(Item item, string name, Func<string, Stream> open, Func<string, long> size)
        {
            string said;
            double times = Measure(item, name, open, size, out said);
            return times > (item.IsStage ? StageLine : CostumeLine) ? said : null;
        }

        // How many times the game's biggest an item is (a stage: its size; a costume: the most of its bones, model and
        // colors; a color: its size), and how to say it. Files that can't be read: 0 (no warning; the game says if
        // they're broken).
        static double Measure(Item item, string name, Func<string, Stream> open, Func<string, long> size, out string said)
        {
            said = null;
            try
            {
                if (item.IsStage)
                {
                    long bytes = Unpacked(open, size, item.Prefix + ".emz") + Unpacked(open, size, item.Prefix + ".tex.emz");
                    double times = (double)bytes / BiggestStage;
                    said = name + " is about " + Times(times) + " the size of the biggest SF4 stage, so it may not perform well.";
                    return times;
                }
                if (item.IsColor)
                {
                    double times = (double)size(item.Prefix + ".col.emb") / BiggestColor;
                    said = name + " is about " + Times(times) + " the size of the biggest SF4 color, so it may not perform well.";
                    return times;
                }
                int bones = Bones(open, item.Prefix + ".obj.emo");
                double byBones = (double)bones / MostBones, byModel = (double)size(item.Prefix + ".obj.emo") / BiggestModel;
                double byColor = Enumerable.Range(1, 10).Max(c => (double)size(item.Prefix + "_" + c.ToString("D2") + ".col.emb")) / BiggestColor;
                double most = Math.Max(byBones, Math.Max(byModel, byColor));
                said = most == byBones ? name + " has " + bones.ToString("N0") + " bones, about " + Times(byBones) + " the most an SF4 costume has, so it may not perform well."
                     : most == byModel ? name + "'s model is about " + Times(byModel) + " the size of the biggest SF4 costume's, so it may not perform well."
                     : name + "'s colors are up to about " + Times(byColor) + " the size of the biggest SF4 color, so it may not perform well.";
                return most;
            }
            catch (Exception) { return 0; }
        }

        public static string Times(double times) { return times.ToString(times < 10 ? "0.#" : "0") + "×"; }

        // A stage file's unpacked size: bytes 8-11 of its 16-byte #EMZ header; a file that isn't packed, its own size.
        static long Unpacked(Func<string, Stream> open, Func<string, long> size, string file)
        {
            using (Stream s = open(file))
            {
                if (s == null) return 0;
                var head = new byte[16];
                if (Read(s, head) == 16 && head[0] == '#' && head[1] == 'E' && head[2] == 'M' && head[3] == 'Z') return BitConverter.ToUInt32(head, 8);
            }
            return Math.Max(0, size(file));
        }

        /// <summary>A model's bones (an .obj.emo's bytes), 0 when it isn't one.</summary>
        public static int Bones(byte[] model) { return model == null ? 0 : Bones(name => new MemoryStream(model), ""); }

        // A model's bones: the skeleton's position is at byte 16 of its #EMO header, its bone count the first 2 bytes there.
        static int Bones(Func<string, Stream> open, string file)
        {
            using (Stream s = open(file))
            {
                if (s == null) return 0;
                var head = new byte[20];
                if (Read(s, head) < 20 || head[0] != '#' || head[1] != 'E' || head[2] != 'M' || head[3] != 'O') return 0;
                long at = BitConverter.ToUInt32(head, 16);
                if (at < 20) return 0;
                if (s.CanSeek) s.Position = at;
                else for (long left = at - 20; left > 0; )   // a zip's file: read up to it
                {
                    var skip = new byte[(int)Math.Min(left, 65536)];
                    int n = s.Read(skip, 0, skip.Length);
                    if (n <= 0) return 0;
                    left -= n;
                }
                var count = new byte[2];
                return Read(s, count) == 2 ? BitConverter.ToUInt16(count, 0) : 0;
            }
        }

        static int Read(Stream s, byte[] buffer)
        {
            int got = 0, n;
            while (got < buffer.Length && (n = s.Read(buffer, got, buffer.Length - got)) > 0) got += n;
            return got;
        }

        /// <summary>The warnings for what went in since `before` (the installed items' keys then), for an install's message.</summary>
        public static List<string> New(string game, ICollection<string> before)
        {
            return Installer.Load(game).Where(r => !before.Contains(r.Item.Key)).Select(r => InGame(game, r.Item, Name(r))).Where(w => w != null).ToList();
        }

        /// <summary>An installed item as the player knows it: "Ryu Slot 75 - Manon", "Stage U01 - Mishima Estate Roof".</summary>
        public static string Name(Installed record)
        {
            return (record.Item.IsStage ? "" : Fighters.Name(record.Item.Fighter) + " ") + record.Item.TitleNamed(record.Shown ?? record.Item.Name);
        }

        public static HashSet<string> Keys(string game) { return new HashSet<string>(Installer.Load(game).Select(r => r.Item.Key)); }
    }
}
