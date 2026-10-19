using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace ExMoreStuff
{
    // Effects mods (user 2026-10-18: Tom's RYX.vfx files, "add support for this kind of effect"): a fighter's move effects
    // (fireballs, trails, auras: <FIGHTER>.vfx.emz, .vfx.ptex.emz, .vfx.ttex.emz, .vfx.bvs), the same in every costume of
    // his, and only on this PC (online too). On GameBanana they're in Skins (Other/Misc), told apart by those files. Each
    // is a zip of its own in the fighter's folder in Mods (with a picture), listed in ex_more_stuff_effects.json in the
    // patch folder. One on at a time per fighter, its files in patch_ae2_tu3\battle\chara\<FIGHTER>\; the game's own copies
    // there (Seth's, Hakan's and Poison's) are kept aside in ex_more_stuff_effects_backup and put back when it goes.
    class Effect
    {
        public string Id, Name, Author, Version, Fighter, Package;
    }

    static class Effects
    {
        const string ListName = "ex_more_stuff_effects.json", BackupName = "ex_more_stuff_effects_backup", PictureName = "effects.png";
        static readonly Regex VfxFile = new Regex(@"^([A-Za-z0-9]{3})\.vfx\.(emz|ptex\.emz|ttex\.emz|bvs)$", RegexOptions.IgnoreCase);

        // the list: the effects mods, which is on for each fighter, and what that put in the game (and kept aside)
        sealed class State
        {
            public List<Effect> Effects = new List<Effect>();
            public Dictionary<string, string> On = new Dictionary<string, string>();
            public Dictionary<string, List<string>> Placed = new Dictionary<string, List<string>>();
            public Dictionary<string, List<string>> Kept = new Dictionary<string, List<string>>();
        }

        static string Name(string path) { return path.Substring(Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\')) + 1); }

        /// <summary>Whether a file is a fighter's effects file (RYX.vfx.emz ...).</summary>
        public static bool IsFile(string path)
        {
            Match m = VfxFile.Match(Name(path));
            return m.Success && Fighters.IsCode(m.Groups[1].Value.ToUpperInvariant());
        }

        /// <summary>The fighters whose effects files a mod holds.</summary>
        public static List<string> FightersIn(IEnumerable<string> paths)
        {
            return paths.Select(p => VfxFile.Match(Name(p))).Where(m => m.Success).Select(m => m.Groups[1].Value.ToUpperInvariant())
                .Where(Fighters.IsCode).Distinct().ToList();
        }

        static string ListPath(string game) { return Path.Combine(Game.PatchFolder(game), ListName); }

        static State Read(string game)
        {
            try
            {
                if (!Game.IsGameFolder(game)) return new State();
                var json = new JavaScriptSerializer();
                string text = SafeFile.ReadList(ListPath(game), t => { try { json.Deserialize<State>(t); return true; } catch (Exception) { return false; } });
                return text == null ? new State() : json.Deserialize<State>(text) ?? new State();
            }
            catch (Exception) { return new State(); }
        }

        static void Write(string game, State state)
        {
            if (state.Effects.Count == 0 && state.Placed.Values.All(p => p.Count == 0) && state.Kept.Values.All(k => k.Count == 0))
            {
                if (File.Exists(ListPath(game))) File.Delete(ListPath(game));
                return;
            }
            Directory.CreateDirectory(Game.PatchFolder(game));
            SafeFile.WriteAllText(ListPath(game), new JavaScriptSerializer().Serialize(state), true);
        }

        public static List<Effect> Load(string game) { return Read(game).Effects; }

        /// <summary>Which effects mod is on for each fighter (fighter -> its id).</summary>
        public static Dictionary<string, string> On(string game) { return new Dictionary<string, string>(Read(game).On); }

        /// <summary>Installs effects from a mod's files (Browse Mods, Add Mod From File): each fighter's as a zip of its own
        /// with the mod's picture, listed, and on for that fighter right away (another one on goes off). The same id again
        /// updates it. Returns the fighters.</summary>
        public static List<string> Install(string game, IList<KeyValuePair<string, byte[]>> files, string id, string name, string author, string version, byte[] picture)
        {
            if (Game.IsRunning()) throw new InvalidOperationException("close the game first");
            var fighters = FightersIn(files.Select(f => f.Key));
            if (fighters.Count == 0) throw new InvalidDataException("it has no effects files (<fighter>.vfx.emz ...)");
            byte[] png = Pictures.Png(picture, Pictures.PackageSide);
            string clean = Regex.Replace(name ?? "", @"[^\w\s\-\.\(\)]", "").Trim();
            foreach (string fighter in fighters)
            {
                var state = Read(game);
                string entryId = id + ":effects:" + fighter;
                Effect old = state.Effects.FirstOrDefault(e => e.Id == entryId);
                string folder = AppFolders.FighterPackages(fighter);
                Directory.CreateDirectory(folder);
                string zip = old != null && !string.IsNullOrEmpty(old.Package) ? old.Package
                           : AppFolders.PlaceFor(folder, (clean.Length > 0 ? clean : "Effects") + " - " + Fighters.Name(fighter) + " effects.zip", null);
                var package = new List<KeyValuePair<string, byte[]>>();
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in files.Where(f => IsFile(f.Key) && Name(f.Key).StartsWith(fighter + ".", StringComparison.OrdinalIgnoreCase)))
                    if (names.Add(Name(f.Key))) package.Add(new KeyValuePair<string, byte[]>(fighter + Name(f.Key).Substring(3), f.Value));   // the first of each, named in caps
                if (png != null) package.Add(new KeyValuePair<string, byte[]>(PictureName, png));
                ModFile.Write(package, zip);
                state.Effects.RemoveAll(e => e.Id == entryId);
                state.Effects.Add(new Effect { Id = entryId, Name = name, Author = author, Version = version, Fighter = fighter, Package = zip });
                Write(game, state);
                Set(game, fighter, entryId);   // on right away; its files again if it was on (an update)
            }
            return fighters;
        }

        /// <summary>A fighter's effects: `id`'s on (null: the game's own). What was in goes first, the game's own copies back.</summary>
        public static void Set(string game, string fighter, string id)
        {
            if (Game.IsRunning()) throw new InvalidOperationException("close the game first");
            string patch = Game.PatchFolder(game);
            var state = Read(game);
            // out: what's in, then the game's own back
            List<string> placed, kept;
            if (state.Placed.TryGetValue(fighter, out placed))
                foreach (string relative in placed.ToList())
                {
                    string path = Path.Combine(patch, relative);
                    if (File.Exists(path)) File.Delete(path);
                    placed.Remove(relative);
                }
            if (state.Kept.TryGetValue(fighter, out kept))
                foreach (string relative in kept.ToList())
                {
                    string from = Path.Combine(patch, BackupName, relative), to = Path.Combine(patch, relative);
                    if (File.Exists(from) && !File.Exists(to)) File.Move(from, to);
                    kept.Remove(relative);
                }
            state.On.Remove(fighter);
            Write(game, state);
            if (id == null) { Tidy(patch); return; }
            Effect effect = state.Effects.FirstOrDefault(e => e.Id == id && e.Fighter == fighter);
            if (effect == null) throw new InvalidOperationException("those effects aren't installed");
            if (!File.Exists(effect.Package)) throw new FileNotFoundException("its zip isn't at " + effect.Package + " any more");
            placed = state.Placed[fighter] = new List<string>();
            kept = state.Kept[fighter] = new List<string>();
            string chara = Path.Combine(patch, "battle", "chara", fighter);
            Directory.CreateDirectory(chara);
            try
            {
                using (ZipArchive zip = ZipFile.OpenRead(effect.Package))
                    foreach (ZipArchiveEntry entry in zip.Entries.Where(e => IsFile(e.Name) && e.Name.StartsWith(fighter + ".", StringComparison.OrdinalIgnoreCase)))
                    {
                        string target = Path.Combine(chara, entry.Name), relative = target.Substring(patch.Length + 1);
                        if (File.Exists(target))   // the game's own (an update of its): kept aside
                        {
                            string backup = Path.Combine(patch, BackupName, relative);
                            Directory.CreateDirectory(Path.GetDirectoryName(backup));
                            if (File.Exists(backup)) throw new IOException("an older copy of " + entry.Name + " is already in " + BackupName);
                            File.Move(target, backup);
                            kept.Add(relative);
                            Write(game, state);
                        }
                        SafeFile.Extract(entry, target);
                        placed.Add(relative);
                        Write(game, state);
                    }
                state.On[fighter] = id;
                Write(game, state);
            }
            catch
            {
                try { Set(game, fighter, null); } catch (Exception) { }   // half in: the game's own back
                throw;
            }
        }

        static void Tidy(string patch)
        {
            try
            {
                string backup = Path.Combine(patch, BackupName);
                if (!Directory.Exists(backup)) return;
                foreach (string dir in Directory.GetDirectories(backup, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
                    if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
                if (!Directory.EnumerateFileSystemEntries(backup).Any()) Directory.Delete(backup);
            }
            catch (Exception) { }
        }

        /// <summary>An effects mod out for good: off (the game's own back), off the list, its zip to the Recycle Bin.</summary>
        public static void Uninstall(string game, string id)
        {
            if (Game.IsRunning()) throw new InvalidOperationException("close the game first");
            var state = Read(game);
            foreach (Effect e in state.Effects.Where(e => e.Id == id).ToList())
            {
                string on;
                if (state.On.TryGetValue(e.Fighter, out on) && on == id) Set(game, e.Fighter, null);
                state = Read(game);
                state.Effects.RemoveAll(x => x.Id == id);
                Write(game, state);
                if (!string.IsNullOrEmpty(e.Package) && File.Exists(e.Package)) PackagesPanel.RecycleZip(e.Package);
            }
        }

        /// <summary>An effects mod's picture (effects.png in its zip), or null.</summary>
        public static byte[] Picture(Effect effect)
        {
            try
            {
                if (effect == null || !File.Exists(effect.Package)) return null;
                using (ZipArchive zip = ZipFile.OpenRead(effect.Package))
                {
                    ZipArchiveEntry entry = zip.Entries.FirstOrDefault(e => e.Name.Equals(PictureName, StringComparison.OrdinalIgnoreCase));
                    if (entry == null) return null;
                    using (Stream s = entry.Open())
                    using (var data = new MemoryStream()) { s.CopyTo(data); return data.ToArray(); }
                }
            }
            catch (Exception) { return null; }
        }
    }
}
