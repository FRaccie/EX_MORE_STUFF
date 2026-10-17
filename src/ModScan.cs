using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace ExMoreStuff
{
    // Mods put in the game by hand (Advanced > Old mods): files in the game's patch folders that aren't the game's
    // official update files (res\official.json: each one's path and size, made by test\OfficialManifest.cs from a clean
    // install) and weren't put there by EX More Stuff. Grouped by what they are. A stage or a costume can be brought in
    // (Import): it becomes a package of the player's in one of their own seats, and its loose files leave the game folder.
    static class ModScan
    {
        public static readonly string[] PatchFolders = { "patch", "patch_ae2", "patch_ae2_tu1", "patch_ae2_tu1b", "patch_ae2_tu2", "patch_ae2_tu3" };

        public enum Kind { Stage, Costume, Other }

        public sealed class Found
        {
            public Kind Kind;
            public string Key, What, Code, Fighter;   // Code: a stage's; Fighter: a costume's
            public int Costume;
            public readonly List<string> Files = new List<string>();   // paths in the game folder
            public DateTime Newest;
            public bool CanImport { get { return Kind != Kind.Other; } }
        }

        static Dictionary<string, long> official;

        /// <summary>For tests: the official files as given (path in the game folder -> size).</summary>
        public static void UseOfficial(Dictionary<string, long> files) { official = new Dictionary<string, long>(files, StringComparer.OrdinalIgnoreCase); }

        static Dictionary<string, long> Official()
        {
            if (official != null) return official;
            var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            using (Stream s = typeof(ModScan).Assembly.GetManifestResourceStream("official.json"))
                if (s != null)
                    using (var r = new StreamReader(s))
                    {
                        var root = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(r.ReadToEnd());
                        foreach (var f in ((System.Collections.ArrayList)root["files"]).OfType<Dictionary<string, object>>())
                            result[Convert.ToString(f["path"])] = Convert.ToInt64(f["size"]);
                    }
            return official = result;
        }

        // what EX More Stuff put in the patch folder itself: packages, songs, replaced stages and costumes
        static HashSet<string> Ours(string game)
        {
            var ours = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Func<string, string> inPatch = f => Path.Combine("patch_ae2_tu3", f);
            foreach (Installed r in Installer.Load(game)) foreach (string f in r.Files) ours.Add(inPatch(f));
            try { foreach (string f in MusicBank.Load(game).Keys) ours.Add(inPatch(Path.Combine("battle", "sound", "bgm", f))); } catch (Exception) { }
            foreach (Replacement r in Replacements.Load(game)) foreach (string f in r.Files.Keys) ours.Add(inPatch(f));
            foreach (CostumeSwap s in CostumeSwaps.Load(game)) foreach (string f in s.Files.Keys) ours.Add(inPatch(f));
            return ours;
        }

        static readonly Regex StageFile = new Regex(@"^STG_([A-Za-z0-9]{3})(\.tex)?\.emz$", RegexOptions.IgnoreCase);
        static readonly Regex CostumeFile = new Regex(@"^([A-Za-z0-9]{3})_(\d\d)(\.|_)", RegexOptions.IgnoreCase);

        /// <summary>The mods in the game's patch folders, grouped by what they are.</summary>
        public static List<Found> Scan(string game)
        {
            var ours = Ours(game);
            var known = Official();
            var groups = new Dictionary<string, Found>();
            foreach (string folder in PatchFolders)
            {
                string root = Path.Combine(game, folder);
                if (!Directory.Exists(root)) continue;
                foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    string inside = path.Substring(root.Length + 1), relative = Path.Combine(folder, inside);
                    if (inside.StartsWith("ex_more_stuff", StringComparison.OrdinalIgnoreCase) || ours.Contains(relative)) continue;
                    long size;
                    var info = new FileInfo(path);
                    if (known.TryGetValue(relative, out size) && size == info.Length) continue;   // the game's own
                    Found found = Describe(inside);
                    Found group;
                    if (!groups.TryGetValue(found.Key, out group)) groups[found.Key] = group = found;
                    group.Files.Add(relative);
                    if (info.LastWriteTime > group.Newest) group.Newest = info.LastWriteTime;
                }
            }
            return groups.Values.OrderBy(g => g.Kind).ThenBy(g => g.What, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // what a file in a patch folder is (its path inside the folder)
        static Found Describe(string inside)
        {
            string folder = Path.GetDirectoryName(inside) ?? "", name = Path.GetFileName(inside);
            string[] parts = folder.Split(Path.DirectorySeparatorChar);
            Match m = StageFile.Match(name);
            if (m.Success && folder.Equals(Path.Combine("battle", "stage"), StringComparison.OrdinalIgnoreCase))
            {
                string code = m.Groups[1].Value.ToUpperInvariant();
                bool game = Array.IndexOf(Stages.Codes, code) >= 0;
                return new Found { Kind = Kind.Stage, Key = "stage:" + code, Code = code,
                                   What = game ? Stages.Name(code) + " (the game's stage, replaced)" : "Stage " + code + " (put in by hand)" };
            }
            m = CostumeFile.Match(name);
            if (parts.Length == 3 && parts[0].Equals("battle", StringComparison.OrdinalIgnoreCase) && parts[1].Equals("chara", StringComparison.OrdinalIgnoreCase))
            {
                string fighter = parts[2].ToUpperInvariant();
                if (m.Success && Fighters.IsCode(fighter) && m.Groups[1].Value.ToUpperInvariant() == fighter)
                {
                    int costume = int.Parse(m.Groups[2].Value);
                    return new Found { Kind = Kind.Costume, Key = "costume:" + fighter + ":" + costume, Fighter = fighter, Costume = costume,
                                       What = Fighters.Name(fighter) + (costume < Catalog.FirstCustomCostume ? "'s " + CostumeGrid.CostumeName(costume) + " (the game's costume, replaced)"
                                                                                                              : " " + costume.ToString("D2") + " (a costume put in by hand)") };
                }
                if (Fighters.IsCode(fighter)) return new Found { Kind = Kind.Other, Key = "chara:" + fighter, What = Fighters.Name(fighter) + ": other files (moves, effects, voice ...)" };
            }
            if (folder.Equals(Path.Combine("battle", "sound", "bgm"), StringComparison.OrdinalIgnoreCase) && name.EndsWith(".csb", StringComparison.OrdinalIgnoreCase))
            {
                MusicSlot slot = MusicSlot.FromFile(name);
                return new Found { Kind = Kind.Other, Key = "music:" + name.ToUpperInvariant(), What = "Music: " + slot.Name + (slot.Round > 1 ? ", round " + slot.Round : "") };
            }
            return new Found { Kind = Kind.Other, Key = "folder:" + folder.ToLowerInvariant(), What = "Files in " + (folder == "" ? "the folder itself" : folder) };
        }

        /// <summary>Brings a found stage or costume into EX More Stuff: its files made into a package of the player's in one of
        /// their own seats (a stage U01 up, a costume 84 down), its loose files taken out of the game folder by `discard`
        /// (into Mods\Legacy mods backup), the package installed. `keep`: what it replaced plays as it again (a stage or costume
        /// replacement, switched on the Stages page or the fighter's page; if that can't be done, the import still stands and
        /// the answer says why). Returns where it went ("stage U01", "Abel 84").</summary>
        public static string Import(string game, Found found, bool keep, Action<string> discard)
        {
            if (Game.IsRunning()) throw new InvalidOperationException("close the game first");
            if (!found.CanImport) throw new InvalidOperationException(found.What + " can't be brought in");
            var files = found.Files.Select(f => new KeyValuePair<string, byte[]>("old/" + Path.GetFileName(f), File.ReadAllBytes(Path.Combine(game, f)))).ToList();
            string where;
            if (found.Kind == Kind.Stage)
            {
                var stage = ModFile.FindStages(files).FirstOrDefault();
                if (stage == null) throw new InvalidDataException("no stage file in it");
                string code = ModFile.FreeStageCode(game);
                if (code == null) throw new InvalidOperationException("all your own stage codes are in use");
                string name = (Array.IndexOf(Stages.Codes, found.Code) >= 0 ? Stages.Name(found.Code) : "Stage " + found.Code) + " mod";
                Directory.CreateDirectory(AppFolders.StagePackages);
                string zip = AppFolders.PlaceFor(AppFolders.StagePackages, name + " - " + code + ".zip", null);
                ModFile.BuildStage(game, stage, code, null, zip);
                foreach (string f in found.Files) discard(Path.Combine(game, f));
                Installer.InstallPackage(game, new Item { Id = "import:stage:" + found.Code, Type = "stage", Fighter = "", Code = code, Name = name,
                                                          Description = name + ": brought in from the game folder" }, zip, "file");
                where = "stage " + code;
                if (keep && Array.IndexOf(Stages.Codes, found.Code) >= 0) where += KeptShowing(() => Replacements.Replace(game, found.Code, code));
            }
            else
            {
                // the fighter's voice put in by hand goes into its package too (user 2026-10-13: part of the costume), and stays
                // where it is: it's the whole fighter's
                files.AddRange(ModdedVoices(game, found.Fighter).Select(f => new KeyValuePair<string, byte[]>("old/" + Path.GetFileName(f), File.ReadAllBytes(Path.Combine(game, f)))));
                var costume = SkinImport.Find(files).FirstOrDefault();
                if (costume == null) throw new InvalidDataException("no model or color of its own in it");
                int slot = SkinImport.FreeSlot(game, found.Fighter);
                if (slot == 0) throw new InvalidOperationException(SkinImport.NoRoom(found.Fighter));
                string name = (found.Costume < Catalog.FirstCustomCostume ? CostumeGrid.CostumeName(found.Costume) : "Costume " + found.Costume.ToString("D2")) + " mod";
                string folder = AppFolders.FighterPackages(found.Fighter);
                Directory.CreateDirectory(folder);
                string zip = AppFolders.PlaceFor(folder, name + " - " + found.Fighter + " " + slot.ToString("D2") + ".zip", null);
                SkinImport.Build(game, costume, slot, null, zip);
                foreach (string f in found.Files) discard(Path.Combine(game, f));
                Installer.InstallPackage(game, new Item { Id = "import:costume:" + found.Fighter + ":" + found.Costume, Type = "costume", Fighter = found.Fighter, Slot = slot,
                                                          Code = "", Name = name, Description = name + ": brought in from the game folder" }, zip, "file");
                where = Fighters.Name(found.Fighter) + " " + slot.ToString("D2");
                if (keep && found.Costume < Catalog.FirstCustomCostume) where += KeptShowing(() => CostumeSwaps.Replace(game, found.Fighter, found.Costume, slot));
            }
            return where;
        }

        /// <summary>Whether a package came from Import (Advanced > Import Your Legacy Mods): one that can be put back.</summary>
        public static bool Imported(Installed record) { return (record.Item.Id ?? "").StartsWith("import:"); }

        static readonly Regex Numbered = new Regex(@" \(\d+\)\.[^.]+$");

        /// <summary>An imported package's original files in Mods\Legacy mods backup (backup path -> its place in the game
        /// folder): those of the latest day holding any, found by what they are (Describe), as the import found them, so
        /// imports from before this was kept are found too. A second file of the same name that day (" (2)") is left.</summary>
        public static List<KeyValuePair<string, string>> Originals(Installed record)
        {
            string key = (record.Item.Id ?? "").Substring("import:".Length);
            var none = new List<KeyValuePair<string, string>>();
            if (!Directory.Exists(AppFolders.OldModsBackup)) return none;
            foreach (string day in Directory.GetDirectories(AppFolders.OldModsBackup).OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal))
            {
                var files = new List<KeyValuePair<string, string>>();
                foreach (string path in Directory.EnumerateFiles(day, "*", SearchOption.AllDirectories))
                {
                    string relative = path.Substring(day.Length + 1);
                    int slash = relative.IndexOf(Path.DirectorySeparatorChar);
                    if (slash < 0 || Array.IndexOf(PatchFolders, relative.Substring(0, slash).ToLowerInvariant()) < 0 || Numbered.IsMatch(relative)) continue;
                    if (Describe(relative.Substring(slash + 1)).Key == key) files.Add(new KeyValuePair<string, string>(path, relative));
                }
                if (files.Count > 0) return files;
            }
            return none;
        }

        /// <summary>Undoes an import (user 2026-10-15: "do it for each one individually"): the package deleted as its trash
        /// can deletes it (what it stood in for given back, its zip to the Recycle Bin), then its original files moved back
        /// from Mods\Legacy mods backup to where they were. Nothing changes when one of them can't go back (gone from the
        /// backup, or another file in its place now). Returns a note (songs on it, files that stayed), or null.</summary>
        public static string PutBack(string game, Installed record)
        {
            if (Game.IsRunning()) throw new InvalidOperationException("close the game first");
            var originals = Originals(record);
            if (originals.Count == 0) throw new IOException("its original files aren't in Mods\\Legacy mods backup any more");
            var ours = Ours(game);
            foreach (var f in originals)
                if (File.Exists(Path.Combine(game, f.Value)) && !ours.Contains(f.Value))
                    throw new IOException("the game folder has another " + Path.GetFileName(f.Value) + " in " + Path.GetDirectoryName(f.Value) + " now");
            bool recycle = PackagesPanel.RecyclesZip(game, record);
            var notes = new List<string>();
            string kept = PackagesPanel.PurgeForGood(game, record);
            if (kept != null) notes.Add(kept);
            if (recycle) PackagesPanel.RecycleZip(record.Package);
            // each copied back and checked (its size and contents) before its backup goes (user 2026-10-15: "move the files,
            // confirm the size and then delete"); one that doesn't match stays in the backup
            var stayed = new List<string>();
            foreach (var f in originals)
            {
                string to = Path.Combine(game, f.Value);
                if (File.Exists(to)) { stayed.Add(Path.GetFileName(to)); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.Copy(f.Key, to);
                if (new FileInfo(to).Length == new FileInfo(f.Key).Length && Installer.Sha256(to) == Installer.Sha256(f.Key)) File.Delete(f.Key);
                else { File.Delete(to); stayed.Add(Path.GetFileName(to)); }
            }
            // the backup's folders left empty go
            foreach (string folder in originals.Select(f => Path.GetDirectoryName(f.Key)).Distinct())
                for (string d = folder; d != null && d.Length > AppFolders.OldModsBackup.Length; d = Path.GetDirectoryName(d))
                    try { if (Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); else break; }
                    catch (IOException) { break; }
            if (stayed.Count > 0) notes.Add("still in Mods\\Legacy mods backup (it couldn't be put back): " + string.Join(", ", stayed));
            return notes.Count > 0 ? string.Join("\n", notes) : null;
        }

        // a fighter's voice files put in by hand (<F>_E.csb, <F>_J.csb in a patch folder, not the game's own or EX More Stuff's):
        // the last patch folder's when several have one, as the game loads it. Files in the game's own folders are left alone.
        static List<string> ModdedVoices(string game, string fighter)
        {
            var ours = Ours(game);
            var known = Official();
            var found = new Dictionary<string, string>();
            foreach (string folder in PatchFolders)
                foreach (string language in new[] { "E", "J" })
                {
                    string relative = Path.Combine(folder, "battle", "chara", fighter, fighter + "_" + language + ".csb"), path = Path.Combine(game, relative);
                    long size;
                    if (!File.Exists(path) || ours.Contains(relative) || (known.TryGetValue(relative, out size) && size == new FileInfo(path).Length)) continue;
                    found[language] = relative;
                }
            return found.Values.ToList();
        }

        // the replacement that keeps an imported mod showing where it was: "" when made, else why not
        static string KeptShowing(Action replace)
        {
            try { replace(); return ""; }
            catch (Exception ex) { return " (it couldn't keep showing where it was: " + ex.Message + ")"; }
        }
    }
}
