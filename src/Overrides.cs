using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace ExMoreStuff
{
    // Override stages (user 2026-10-12: "a new type of stage download, one that doesn't go to its own code ... an
    // override stage"): a stage made to play in one game stage's place, released without a code of its own, so it adds no
    // entry to EMBER's stage list. Its mod says which stage with a file named override_<CODE>.txt (override_KOR.txt; what's
    // in it doesn't matter), so Browse Mods knows before downloading. EX More Stuff keeps it as a zip of its own in
    // Mods\Stages and, once it's in, plays it in that stage's place right away (user): a Replacement whose source is
    // "override:<id>", as the Stages page's replacements are made. Only this PC sees it, online too; the stage's music stays.
    // The Stages page puts the game's own back or picks another any time. Their list: ex_more_stuff_overrides.json in the
    // patch folder, beside the other lists.
    class Override
    {
        public string Id, Name, Author, Version, Target, Package;
    }

    static class Overrides
    {
        const string ListName = "ex_more_stuff_overrides.json";
        public const string Prefix = "override:";
        static readonly Regex Marker = new Regex(@"^override_([A-Za-z0-9]{3})\.txt$", RegexOptions.IgnoreCase);
        static readonly Regex StageFile = new Regex(@"^STG_([A-Za-z0-9]{3})(\.tex)?\.emz$", RegexOptions.IgnoreCase);

        static string ListPath(string game) { return Path.Combine(Game.PatchFolder(game), ListName); }

        static string Name(string path) { return path.Substring(Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\')) + 1); }

        /// <summary>The game stage a mod's file names say it replaces (override_KOR.txt anywhere in it: one of the game's
        /// stages), or null.</summary>
        public static string Target(IEnumerable<string> paths)
        {
            foreach (string path in paths)
            {
                Match m = Marker.Match(Name(path));
                if (m.Success && Array.IndexOf(Stages.Codes, m.Groups[1].Value.ToUpperInvariant()) >= 0) return m.Groups[1].Value.ToUpperInvariant();
            }
            return null;
        }

        // A stage's own code in EX More Stuff (user 2026-10-18): code_<CODE>.txt beside stage files under a game stage's code
        // (STG_LBX.emz), so one upload is a plain replacement for players copying it in by hand and its own stage <CODE> in
        // EX More Stuff, re-coded as it goes in (ModFile.BuildStage). Like override_<CODE>.txt, read from the file names.
        static readonly Regex CodeMarker = new Regex(@"^code_([A-Za-z0-9]{3})\.txt$", RegexOptions.IgnoreCase);

        public static bool IsCodeMarker(string path) { return CodeMarker.IsMatch(Name(path)); }

        /// <summary>The stage code a mod's code_<CODE>.txt gives it (one, a custom code), or null.</summary>
        public static string OwnCode(IEnumerable<string> paths)
        {
            var codes = paths.Select(p => CodeMarker.Match(Name(p))).Where(m => m.Success).Select(m => m.Groups[1].Value.ToUpperInvariant()).Distinct().ToList();
            return codes.Count == 1 && Stages.IsCustomCode(codes[0]) ? codes[0] : null;
        }

        // A costume's own shared slot in EX More Stuff (user 2026-10-18, as code_<CODE>.txt for stages): slot_<FIGHTER>_<NN>.txt
        // (slot_RYU_08.txt) beside files of one of the game's costumes (RYU_01...), so one upload replaces that costume for
        // players copying it in by hand and is its own costume in shared slot NN (8 up to the last shared one) in EX More
        // Stuff, once its GameBanana mod holds that slot (SharedCodes). One a fighter.
        static readonly Regex SlotMarker = new Regex(@"^slot_([A-Za-z0-9]{3})_(\d\d)\.txt$", RegexOptions.IgnoreCase);

        /// <summary>Each fighter's shared slot a mod's slot_<FIGHTER>_<NN>.txt gives it (one marker a fighter, a shared slot).</summary>
        public static Dictionary<string, int> OwnSlots(IEnumerable<string> paths)
        {
            var found = paths.Select(p => SlotMarker.Match(Name(p))).Where(m => m.Success)
                .Select(m => new { Fighter = m.Groups[1].Value.ToUpperInvariant(), Slot = int.Parse(m.Groups[2].Value) }).Distinct().ToList();
            return found.Where(f => Fighters.IsCode(f.Fighter) && f.Slot >= Catalog.FirstCustomCostume && f.Slot <= Catalog.LastShared)
                .GroupBy(f => f.Fighter).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.First().Slot);
        }

        // A mod made for a newer EX More Stuff (user 2026-10-18: "a stage uploaded with this file will need old EXMS version
        // users to require an update"): needs_exms_<version>.txt (needs_exms_0.93.txt) in it; older versions refuse it.
        // Versions before 0.92 don't know this file.
        static readonly Regex NeedsMarker = new Regex(@"^needs_exms_(\d+(?:\.\d+){1,3})\.txt$", RegexOptions.IgnoreCase);

        /// <summary>Why this EX More Stuff can't install a mod that asks for a newer one ("needs EX More Stuff 0.93 or newer.
        /// ..."), or null.</summary>
        public static string TooNew(IEnumerable<string> paths)
        {
            Version needs = null;
            foreach (string path in paths)
            {
                Match m = NeedsMarker.Match(Name(path));
                Version v;
                if (m.Success && Version.TryParse(m.Groups[1].Value, out v) && (needs == null || v > needs)) needs = v;
            }
            Version have = typeof(Overrides).Assembly.GetName().Version;
            if (needs == null || new Version(have.Major, have.Minor) >= new Version(needs.Major, needs.Minor)) return null;
            string named = needs.Major + "." + needs.Minor;
            Version latest = Catalog.ProgramVersion;
            return "needs EX More Stuff " + named + " or newer. " +
                   (latest != null && new Version(latest.Major, latest.Minor) >= new Version(needs.Major, needs.Minor)
                       ? "Update with \"Version " + latest + " is out\" at the top, then install it."
                       : "Update EX More Stuff, then install it.");
        }

        public static bool IsOverride(string source) { return source != null && source.StartsWith(Prefix, StringComparison.Ordinal); }

        public static List<Override> Load(string game)
        {
            try
            {
                if (!Game.IsGameFolder(game) || !File.Exists(ListPath(game))) return new List<Override>();
                return new JavaScriptSerializer().Deserialize<List<Override>>(File.ReadAllText(ListPath(game))) ?? new List<Override>();
            }
            catch (Exception) { return new List<Override>(); }
        }

        static void Save(string game, List<Override> list)
        {
            if (list.Count == 0) { if (File.Exists(ListPath(game))) File.Delete(ListPath(game)); return; }
            Directory.CreateDirectory(Game.PatchFolder(game));
            SafeFile.WriteAllText(ListPath(game), new JavaScriptSerializer().Serialize(list), true);
        }

        /// <summary>The override a replacement's source names ("override:<id>"), or null.</summary>
        public static Override Find(string game, string source)
        {
            return IsOverride(source) ? Load(game).FirstOrDefault(o => Prefix + o.Id == source) : null;
        }

        /// <summary>Installs an override from a mod's files (Browse Mods, Add Mod From File): its stage (named for its
        /// target, else the one stage in the mod; under another code it's converted) as STG_<target> in a zip of its own
        /// in Mods\Stages with the marker and a picture, listed, and in its stage's place right away. Installing the same
        /// id again updates it. Returns the target.</summary>
        public static string Install(string game, IList<KeyValuePair<string, byte[]>> files, string id, string name, string author, string version, byte[] picture)
        {
            if (Game.IsRunning()) throw new InvalidOperationException("close the game first");
            string target = Target(files.Select(f => f.Key));
            if (target == null) throw new InvalidDataException("it has no override_<stage>.txt naming one of the game's stages");
            var stages = files.Select(f => new { File = f, Match = StageFile.Match(Name(f.Key)) }).Where(x => x.Match.Success)
                .GroupBy(x => x.Match.Groups[1].Value.ToUpperInvariant()).ToList();
            var stage = stages.FirstOrDefault(g => g.Key == target) ?? (stages.Count == 1 ? stages[0] : null);
            if (stage == null)
                throw new InvalidDataException(stages.Count == 0 ? "it has no stage files (STG_" + target + ".emz)" : "it has more than one stage and none named STG_" + target);
            byte[] model = stage.Where(x => !x.Match.Groups[2].Success).Select(x => x.File.Value).FirstOrDefault();
            byte[] textures = stage.Where(x => x.Match.Groups[2].Success).Select(x => x.File.Value).FirstOrDefault();
            if (model == null || textures == null) throw new InvalidDataException("it needs both STG_" + stage.Key + ".emz and STG_" + stage.Key + ".tex.emz");
            if (stage.Key != target) { model = StagePack.Recode(model, stage.Key, target); textures = StagePack.Recode(textures, stage.Key, target); }

            var list = Load(game);
            Override old = list.FirstOrDefault(o => o.Id == id);
            string folder = AppFolders.StagePackages;
            Directory.CreateDirectory(folder);
            string clean = Regex.Replace(name ?? "", @"[^\w\s\-\.\(\)]", "").Trim();
            string zip = old != null && !string.IsNullOrEmpty(old.Package) ? old.Package
                       : AppFolders.PlaceFor(folder, (clean.Length > 0 ? clean : "Override") + " - replaces " + target + ".zip", null);
            string temp = zip + ".making";
            if (File.Exists(temp)) File.Delete(temp);   // left by a write a crash stopped
            using (ZipArchive archive = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                Action<string, byte[]> add = (entry, data) => { using (Stream s = archive.CreateEntry(entry).Open()) s.Write(data, 0, data.Length); };
                add("STG_" + target + ".emz", model);
                add("STG_" + target + ".tex.emz", textures);
                add("override_" + target + ".txt", Encoding.UTF8.GetBytes((name ?? "") + "\r\nPlays in place of " + Stages.Name(target) + " (" + target + ") on this PC: an override stage.\r\n"));
                byte[] png = Pictures.Png(picture);   // a WebP one made readable (Pictures)
                if (png != null) add("STG_" + target + ".png", png);
            }
            SafeFile.Swap(temp, zip);   // the old zip until the new one is complete

            list.RemoveAll(o => o.Id == id);
            list.Add(new Override { Id = id, Name = name, Author = author, Version = version, Target = target, Package = zip });
            Save(game, list);
            // in its stage's place right away (user): whatever played there before goes first
            if (Replacements.Load(game).Any(r => r.Stage == target)) Replacements.Restore(game, target);
            Replacements.Replace(game, target, Prefix + id);
            return target;
        }

        /// <summary>An override out (Browse Mods' Uninstall): the game's own stage back where it played, off the list, its
        /// zip to the Recycle Bin.</summary>
        public static void Uninstall(string game, string id)
        {
            if (Game.IsRunning()) throw new InvalidOperationException("close the game first");
            foreach (var r in Replacements.Load(game).Where(r => r.Source == Prefix + id).ToList()) Replacements.Restore(game, r.Stage);
            var list = Load(game);
            foreach (Override o in list.Where(o => o.Id == id).ToList())
            {
                if (!string.IsNullOrEmpty(o.Package) && File.Exists(o.Package)) PackagesPanel.RecycleZip(o.Package);
                list.Remove(o);
            }
            Save(game, list);
        }

        /// <summary>An override's stage file for Replacements (".emz" or ".tex.emz", under its target's code), or null.</summary>
        public static byte[] StageBytes(string game, string source, string suffix)
        {
            Override o = Find(game, source);
            if (o == null || !File.Exists(o.Package)) return null;
            using (ZipArchive archive = ZipFile.OpenRead(o.Package))
            {
                ZipArchiveEntry entry = archive.Entries.FirstOrDefault(e => string.Equals(e.Name, "STG_" + o.Target + suffix, StringComparison.OrdinalIgnoreCase));
                if (entry == null) return null;
                using (Stream s = entry.Open())
                using (var data = new MemoryStream()) { s.CopyTo(data); return data.ToArray(); }
            }
        }

        /// <summary>An override's picture (STG_<target>.png in its zip), or null.</summary>
        public static byte[] Picture(string game, string source)
        {
            Override o = Find(game, source);
            if (o == null || !File.Exists(o.Package)) return null;
            try
            {
                using (ZipArchive archive = ZipFile.OpenRead(o.Package))
                {
                    ZipArchiveEntry entry = archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
                    if (entry == null) return null;
                    using (Stream s = entry.Open())
                    using (var data = new MemoryStream()) { s.CopyTo(data); return data.ToArray(); }
                }
            }
            catch (Exception) { return null; }
        }
    }
}
