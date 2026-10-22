using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ExMoreStuff
{
    // The costume check (FRaccie 2026-10-23): what in a custom costume breaks it in the game or online, read from the
    // files, with a fixer for what can be put right and a text report to look into. The rules come from the decompiled
    // game (Steam 1.05) and the costumes seen so far:
    //  - Online, a player without the costume loads its stand-in, the fighter's costume 1 (2 or 3 in a mirror match,
    //    Ember's ResolveAppearance). Gameplay reads 37 "locator" bones of the loaded model, found by name
    //    (Actor::getLocatorBoneName 0x563260, table 0xa60640): the 24 body hurtboxes run between them
    //    (buildBodyHurtQuads 0x55b2a0, pairs 0x94ffe8), WAIST moves the fighter, LND sets the position, TRS is the
    //    distance, height and close/far. A bone's translation, rotation and scale come from the animation only where it
    //    keys them, else from the model's rest pose (EmModel::calcBoneMatrices 0x6c6ea0); arms and legs get their
    //    rotation from the model's IK records (solveConstraints 0x6c9390). So the costume's rest pose of those bones must
    //    be the stand-in's, bit for bit, or the match desyncs (Samus AGL_09, 2026-10-22).
    //  - An .obj.emm with more materials than its model declares (part table +2, 0 = 256) isn't used at all
    //    (EmModel::bindMaterialSet 0x6c6080): that color draws untextured. A material the model uses that the .obj.emm
    //    lacks draws white (Juri JRI_38, Sakura SKR_34, Ken KEN_08).
    //  - The game loads .obj.emo, .shd.emo, .nml.emb, .bsr per costume (Vega also .clw.emo, .msk.emo;
    //    CostumeResourceHolder::load 0x5e5f80); Ember only offers a costume with all ten colors.
    static class CostumeCheck
    {
        // Actor::getLocatorBoneName's table; findBoneIndex compares without case
        static readonly string[] Gameplay = { "WAIST", "STOMACH", "CHEST", "NECK", "HEAD", "LARM1", "LARM2", "LHANDROT", "LHN",
            "LLEG1", "LLEG2", "LFOOT", "LFN", "RARM1", "RARM2", "RHANDROT", "RHN", "RLEG1", "RLEG2", "RFOOT", "RFN", "LHOPTION",
            "RHOPTION", "TRS", "LND" };
        // only effects and sounds use these, as far as traced
        static readonly string[] Effects = { "FACE_LEYE", "FACE_REYE", "WAN", "HEN", "HEOPTION", "LARMEFF", "RARMEFF", "LLEGEFF", "RLEGEFF" };

        static readonly Regex Model = new Regex(@"^([A-Za-z]{3})_(\d\d)\.obj\.emo$", RegexOptions.IgnoreCase);

        public enum Verdict { Ok, Mirror, Noise, Faulty }

        public sealed class Finding
        {
            public string Kind, Text;   // Kind: DESYNC, DESYNC?, IK, EFFECTS, WHITE, EMM>CAP, FILES, COLORS, SLOT, STOCK
            public bool Fixable;
        }

        public sealed class Result
        {
            public string Name, Where;   // "AGL_09", the folder or zip
            public Verdict Verdict;
            public readonly List<Finding> Findings = new List<Finding>();
        }

        // ---- reading the files ----

        sealed class Bone
        {
            public string Name;
            public int Parent;   // -1: none
            public float[] Local;   // 16, row vectors
        }

        static int I32(byte[] d, int o) { return BitConverter.ToInt32(d, o); }
        static int U16(byte[] d, int o) { return BitConverter.ToUInt16(d, o); }

        static string CString(byte[] d, int o)
        {
            int end = o;
            while (end < d.Length && d[end] != 0) end++;
            return Encoding.GetEncoding(28591).GetString(d, o, end - o);
        }

        /// <summary>A model's skeleton: names, parents, rest matrices (node records of 80 bytes, matrix at +16).</summary>
        static List<Bone> Skeleton(byte[] emo)
        {
            var bones = new List<Bone>();
            int s = I32(emo, 16);
            if (s == 0) return bones;
            int count = U16(emo, s), nodes = s + I32(emo, s + 8), names = s + I32(emo, s + 12);
            for (int i = 0; i < count; i++)
            {
                int o = nodes + i * 80, parent = U16(emo, o);
                var m = new float[16];
                for (int k = 0; k < 16; k++) m[k] = BitConverter.ToSingle(emo, o + 16 + k * 4);
                bones.Add(new Bone { Name = CString(emo, s + I32(emo, names + i * 4)), Parent = parent < count ? parent : -1, Local = m });
            }
            return bones;
        }

        /// <summary>How many materials the model takes (part table +2; 0 means 256, EmModelData 0x718ad0).</summary>
        static int MaterialCapacity(byte[] emo)
        {
            int capacity = BitConverter.ToInt16(emo, I32(emo, 0xc) + 2);
            return capacity > 0 ? capacity : 256;
        }

        // Every model record of the model: parts (part table), their EMG blocks, the models in those (layout as the
        // usf4-nesica tools fix_costume.py / render_usf4_costumes.py read it).
        static IEnumerable<int> Models(byte[] emo)
        {
            int pt = I32(emo, 0xc);
            for (int p = 0; p < BitConverter.ToInt16(emo, pt); p++)
            {
                int po = pt + I32(emo, pt + 8 + p * 4);
                for (int g = 0; g < I32(emo, po); g++)
                {
                    int go = po + I32(emo, po + 4 + g * 4);
                    for (int r = 0; r < U16(emo, go + 6); r++) yield return go + I32(emo, go + 8 + r * 4);
                }
            }
        }

        // a model record's submeshes: (record offset, material name offset)
        static IEnumerable<int> Submeshes(byte[] emo, int model)
        {
            int count = U16(emo, model + 0x1a), table = I32(emo, model + 0x1c);
            for (int i = 0; i < count; i++) yield return model + I32(emo, model + table + i * 4) + 0x10;
        }

        static List<string> UsedMaterials(byte[] emo)
        {
            return Models(emo).SelectMany(m => Submeshes(emo, m)).Select(s => CString(emo, s + 6)).Distinct().ToList();
        }

        /// <summary>The material names of an .obj.emm.</summary>
        static List<string> Materials(byte[] emm)
        {
            int t = I32(emm, 0xc);
            return Enumerable.Range(0, I32(emm, t)).Select(i => CString(emm, t + I32(emm, t + 4 + i * 4))).ToList();
        }

        // The game finds materials whatever the case (304 of the stock models' names differ from their .emm only in case:
        // Juri's "skin", Yun's "Shoes"), so every lookup here ignores it.
        static bool Has(List<string> materials, string name) { return materials.Contains(name, StringComparer.OrdinalIgnoreCase); }

        /// <summary>Upper-case bone name -> the transform kinds (0 translation, 1 rotation, 2 scale) that at least one of the
        /// fighter's animations leaves to the model. Animation files: every .ema of the fighter but the camera's; in the face
        /// file only the bones it animates count (a layer of its own).</summary>
        static Dictionary<string, HashSet<int>> Unkeyed(string game, string fighter)
        {
            var loose = new Dictionary<string, HashSet<int>>();
            foreach (string kind in new[] { "obj", "uc1", "uc2", "dm1", "dm2", "fce" })
            {
                string path = SkinImport.GameFile(game, fighter, fighter + "." + kind + ".ema");
                if (path == null) continue;
                byte[] d = File.ReadAllBytes(path);
                int s = I32(d, 12);
                if (s == 0) continue;
                var names = Enumerable.Range(0, U16(d, s)).Select(i => CString(d, s + I32(d, s + I32(d, s + 12) + i * 4)).ToUpperInvariant()).ToList();
                int header = U16(d, 6);
                for (int a = 0; a < U16(d, 16); a++)
                {
                    int o = I32(d, header + a * 4);
                    if (o == 0) continue;
                    var axes = new Dictionary<int, HashSet<int>>();   // bone * 3 + kind -> axes keyed
                    for (int c = 0; c < U16(d, o + 2); c++)
                    {
                        int co = o + I32(d, o + 20 + c * 4);
                        int key = U16(d, co) * 3 + d[co + 2];
                        HashSet<int> set;
                        if (!axes.TryGetValue(key, out set)) axes[key] = set = new HashSet<int>();
                        set.Add(d[co + 3] & 3);
                    }
                    var animated = new HashSet<int>(axes.Keys.Select(k => k / 3));
                    for (int b = 0; b < names.Count; b++)
                    {
                        if (kind == "fce" && !animated.Contains(b)) continue;
                        for (int k = 0; k < 3; k++)
                        {
                            HashSet<int> set;
                            if (axes.TryGetValue(b * 3 + k, out set) && set.Count == 3) continue;
                            HashSet<int> l;
                            if (!loose.TryGetValue(names[b], out l)) loose[names[b]] = l = new HashSet<int>();
                            l.Add(k);
                        }
                    }
                }
            }
            return loose;
        }

        // ---- the locator rule ----

        // upper-case name -> bone, and every bone on the way from a locator to the root -> the locators it moves
        static Dictionary<string, Bone> ByName(List<Bone> bones)
        {
            var by = new Dictionary<string, Bone>();
            foreach (Bone b in bones) by[b.Name.ToUpperInvariant()] = b;
            return by;
        }

        static string ParentName(List<Bone> bones, Bone b) { return b.Parent < 0 ? null : bones[b.Parent].Name.ToUpperInvariant(); }

        static Dictionary<string, HashSet<string>> Chains(List<Bone> bones)
        {
            var by = ByName(bones);
            var above = new Dictionary<string, HashSet<string>>();
            foreach (string locator in Gameplay.Concat(Effects))
            {
                string name = locator;
                Bone b;
                while (name != null && by.TryGetValue(name, out b))
                {
                    HashSet<string> set;
                    if (!above.TryGetValue(name, out set)) above[name] = set = new HashSet<string>();
                    set.Add(locator);
                    name = ParentName(bones, b);
                }
            }
            return above;
        }

        static float[] Multiply(float[] a, float[] b)
        {
            var r = new float[16];
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                {
                    double v = 0;
                    for (int k = 0; k < 4; k++) v += (double)a[i * 4 + k] * b[k * 4 + j];
                    r[i * 4 + j] = (float)v;
                }
            return r;
        }

        static float[] World(List<Bone> bones, int i)
        {
            Bone b = bones[i];
            return b.Parent < 0 ? (float[])b.Local.Clone() : Multiply(b.Local, World(bones, b.Parent));
        }

        static string Size(double d) { return d >= 1e-3 ? "moved" : d >= 1e-4 ? "small" : "noise"; }

        static string Number(double d) { return d.ToString(d < 1e-4 ? "0.0e0" : "0.000000", System.Globalization.CultureInfo.InvariantCulture); }

        sealed class Difference
        {
            public string Level, Bone, What, Size;
        }

        /// <summary>Where the custom model's locator chains differ from the stand-in model in what the animations leave to
        /// the model. Level DESYNC (gameplay locator, translation or scale, missing, other parent), DESYNC? (a rotation IK may
        /// override), IK (the IK records of gameplay bones differ), EFFECTS (only effect locators).</summary>
        static List<Difference> LocatorDifferences(byte[] custom, byte[] standIn, Dictionary<string, HashSet<int>> loose)
        {
            List<Bone> mine = Skeleton(custom), theirs = Skeleton(standIn);
            Dictionary<string, Bone> myBy = ByName(mine), theirBy = ByName(theirs);
            var need = Chains(theirs);
            // above TRS the bones are named per model: TRS's world rest pose covers them
            var roots = new HashSet<string>();
            Bone trs;
            for (string n = theirBy.TryGetValue("TRS", out trs) ? ParentName(theirs, trs) : null; n != null; n = ParentName(theirs, theirBy[n])) roots.Add(n);
            var found = new List<Difference>();
            Func<string, bool, string> level = (name, rotation) =>
                !need[name].Overlaps(Gameplay) ? "EFFECTS" : rotation ? "DESYNC?" : "DESYNC";
            foreach (string name in need.Keys.Where(n => !roots.Contains(n)).OrderBy(n => n, StringComparer.Ordinal))
            {
                Bone a, b = theirBy[name];
                if (!myBy.TryGetValue(name, out a)) { found.Add(new Difference { Level = level(name, false), Bone = name, What = "missing", Size = "moved" }); continue; }
                if (name == "TRS" || b.Parent < 0)
                {
                    float[] wa = World(mine, mine.IndexOf(a)), wb = World(theirs, theirs.IndexOf(b));
                    double d = Enumerable.Range(0, 16).Max(k => Math.Abs((double)wa[k] - wb[k]));
                    if (d > 0) found.Add(new Difference { Level = level(name, false), Bone = name, What = "world rest pose " + Number(d), Size = Size(d) });
                    continue;
                }
                if (ParentName(mine, a) != ParentName(theirs, b))
                {
                    found.Add(new Difference { Level = level(name, false), Bone = name, What = "parent " + ParentName(mine, a) + ", stand-in " + ParentName(theirs, b), Size = "moved" });
                    continue;
                }
                HashSet<int> kinds;
                loose.TryGetValue(name, out kinds);
                if (kinds == null) continue;
                double[] scaleA = Scales(a.Local), scaleB = Scales(b.Local);
                double translation = Enumerable.Range(12, 3).Max(k => Math.Abs((double)a.Local[k] - b.Local[k]));
                double scale = Enumerable.Range(0, 3).Max(k => Math.Abs(scaleA[k] - scaleB[k]));
                double rotation = Enumerable.Range(0, 3).SelectMany(r => Enumerable.Range(0, 3).Select(c =>
                    Math.Abs(a.Local[r * 4 + c] / Math.Max(scaleA[r], 1e-9) - b.Local[r * 4 + c] / Math.Max(scaleB[r], 1e-9)))).Max();
                if (translation > 0 && kinds.Contains(0)) found.Add(new Difference { Level = level(name, false), Bone = name, What = "translation " + Number(translation), Size = Size(translation) });
                if (rotation > 0 && kinds.Contains(1)) found.Add(new Difference { Level = level(name, true), Bone = name, What = "rotation " + Number(rotation), Size = Size(rotation) });
                if (scale > 0 && kinds.Contains(2)) found.Add(new Difference { Level = level(name, false), Bone = name, What = "scale " + Number(scale), Size = Size(scale) });
            }
            // IK records that set a bone on a gameplay chain (the record's first bone; the jaw's and such only draw)
            var chain = new HashSet<string>(need.Where(n => n.Value.Overlaps(Gameplay)).Select(n => n.Key));
            Func<byte[], List<string>> ik = emo => IkRecords(emo).Where(r => chain.Contains(r.Split(':')[1].Split(',')[0])).ToList();
            if (!ik(custom).SequenceEqual(ik(standIn)))
                found.Add(new Difference { Level = "IK", Bone = "IK", What = "IK records of gameplay bones differ from the stand-in's", Size = "moved" });
            return found;
        }

        static double[] Scales(float[] m)
        {
            return Enumerable.Range(0, 3).Select(r => Math.Sqrt(Enumerable.Range(0, 3).Sum(c => (double)m[r * 4 + c] * m[r * 4 + c]))).ToArray();
        }

        // the skeleton's IK records (skeleton +0x20, count +4) with their bone numbers as names, the rest as bytes
        static List<string> IkRecords(byte[] emo)
        {
            var list = new List<string>();
            int s = I32(emo, 16);
            if (s == 0) return list;
            int count = U16(emo, s + 4), at = I32(emo, s + 0x20);
            if (at == 0) return list;
            var names = Skeleton(emo).Select(b => b.Name.ToUpperInvariant()).ToList();
            int o = s + at;
            for (int i = 0; i < count; i++)
            {
                int method = U16(emo, o), size = U16(emo, o + 2), n = method == 0 ? 5 : 3;
                var text = new StringBuilder(method + ":");
                for (int k = 0; k < n; k++) { int id = U16(emo, o + 6 + k * 2); text.Append(id < names.Count ? names[id] : id.ToString()).Append(','); }
                for (int k = 6 + n * 2; k < size; k++) text.Append(emo[o + k].ToString("x2"));
                list.Add(text.ToString());
                o += size;
            }
            return list;
        }

        // ---- checking ----

        /// <summary>A costume's files by name (folder or zip), read on demand.</summary>
        public sealed class Source
        {
            public string Where;
            public Func<string, byte[]> Read;   // null when there's no such file
            public List<string> Names;
        }

        public static Source FromFolder(string folder)
        {
            var names = Directory.GetFiles(folder).Select(Path.GetFileName).ToList();
            return new Source { Where = folder, Names = names, Read = n => { string p = Path.Combine(folder, n); return File.Exists(p) ? File.ReadAllBytes(p) : null; } };
        }

        public static Source FromZip(string zip)
        {
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            using (var z = ZipFile.OpenRead(zip))
                foreach (var e in z.Entries.Where(e => e.Name != ""))
                    using (var s = e.Open())
                    using (var m = new MemoryStream()) { s.CopyTo(m); files[e.Name] = m.ToArray(); }
            return new Source { Where = zip, Names = files.Keys.ToList(), Read = n => { byte[] b; return files.TryGetValue(n, out b) ? b : null; } };
        }

        /// <summary>Every costume model (CHR_NN.obj.emo) in a folder (and the folders in it), a zip, or a zip's folder.</summary>
        public static List<Source> Find(string path)
        {
            var found = new List<Source>();
            if (File.Exists(path)) { if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) found.Add(FromZip(path)); }
            else if (Directory.Exists(path))
            {
                foreach (string dir in new[] { path }.Concat(Directory.GetDirectories(path, "*", SearchOption.AllDirectories)))
                    if (Directory.GetFiles(dir, "*.obj.emo").Any(f => Model.IsMatch(Path.GetFileName(f)))) found.Add(FromFolder(dir));
                foreach (string zip in Directory.GetFiles(path, "*.zip", SearchOption.AllDirectories)) found.Add(FromZip(zip));
            }
            return found.Where(s => s.Names.Any(n => Model.IsMatch(n))).ToList();
        }

        /// <summary>The game's stock model of a costume (1-based), or null.</summary>
        static byte[] Stock(string game, string fighter, int costume)
        {
            string path = SkinImport.GameFile(game, fighter, fighter + "_" + costume.ToString("D2") + ".obj.emo");
            return path == null ? null : File.ReadAllBytes(path);
        }

        public static List<Result> Check(string game, Source source)
        {
            var results = new List<Result>();
            var animations = new Dictionary<string, Dictionary<string, HashSet<int>>>();
            foreach (string model in source.Names.Where(n => Model.IsMatch(n)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                Match m = Model.Match(model);
                string fighter = m.Groups[1].Value.ToUpperInvariant(), stem = model.Substring(0, 6);
                int slot = int.Parse(m.Groups[2].Value);
                var r = new Result { Name = stem.ToUpperInvariant(), Where = source.Where };
                Action<string, string, bool> add = (kind, text, fixable) => r.Findings.Add(new Finding { Kind = kind, Text = text, Fixable = fixable });
                byte[] emo = source.Read(model);
                if (slot < Catalog.FirstCustomCostume || slot > Catalog.LastSlot) add("SLOT", "costume " + slot.ToString("D2") + " is outside " + Catalog.FirstCustomCostume.ToString("D2") + ".." + Catalog.LastSlot + ": Ember doesn't offer it", false);

                // the locators, against each stand-in
                var verdicts = new Dictionary<int, List<Difference>>();
                if (!animations.ContainsKey(fighter)) animations[fighter] = Unkeyed(game, fighter);
                for (int standIn = 1; standIn <= 3; standIn++)
                {
                    byte[] stock = Stock(game, fighter, standIn);
                    if (stock != null) verdicts[standIn] = LocatorDifferences(emo, stock, animations[fighter]);
                }
                if (verdicts.Count == 0) add("STOCK", "the game's " + fighter + "_01..03.obj.emo weren't found: the locators weren't checked", false);
                foreach (string level in new[] { "DESYNC", "DESYNC?", "IK", "EFFECTS" })
                {
                    var per = verdicts.ToDictionary(v => v.Key, v => v.Value.Where(d => d.Level == level).ToList());
                    if (!per.Values.Any(v => v.Count > 0)) continue;
                    int key = per.ContainsKey(1) && per[1].Count > 0 ? 1 : per.First(v => v.Value.Count > 0).Key;
                    var text = string.Join("; ", new[] { "moved", "small", "noise" }.Select(size => new { size, bones = per[key].Where(d => d.Size == size).ToList() })
                        .Where(x => x.bones.Count > 0).Select(x => x.size + " " + x.bones.Count + ": " + string.Join(", ", x.bones.Select(d => d.Bone + " " + d.What))));
                    string clean = string.Join(",", per.Where(v => v.Value.Count == 0).Select(v => v.Key.ToString("D2")));
                    add(level, "vs stand-in " + key.ToString("D2") + ": " + text + "; clean vs " + (clean == "" ? "none" : clean), (level == "DESYNC" || level == "DESYNC?") && key == 1);
                }
                List<Difference> first;
                var mine = verdicts.TryGetValue(1, out first) ? first.Where(d => d.Level != "EFFECTS").ToList() : new List<Difference>();
                bool mirror = verdicts.Where(v => v.Key != 1).Any(v => v.Value.Any(d => d.Level != "EFFECTS"));
                r.Verdict = mine.Any(d => d.Size != "noise") ? Verdict.Faulty : mine.Count > 0 ? Verdict.Noise : mirror ? Verdict.Mirror : Verdict.Ok;

                // files and materials
                var parts = new List<string> { ".obj.emo", ".shd.emo", ".nml.emb", ".bsr" };
                if (fighter == "BLR") parts.AddRange(new[] { ".clw.emo", ".msk.emo" });
                var lost = parts.Where(p => !source.Names.Contains(stem + p, StringComparer.OrdinalIgnoreCase)).ToList();
                if (lost.Count > 0) add("FILES", "missing " + string.Join(" ", lost.Select(p => stem + p)) + " (the game loads them for every costume)", true);
                int capacity = MaterialCapacity(emo);
                var used = UsedMaterials(emo);
                var absent = new List<string>();
                var white = new Dictionary<string, List<string>>();   // lacking materials -> colors
                for (int color = 1; color <= 10; color++)
                {
                    string name = stem + "_" + color.ToString("D2");
                    byte[] emm = source.Read(name + ".obj.emm");
                    if (emm == null || source.Read(name + ".col.emb") == null) { absent.Add(color.ToString("D2")); continue; }
                    var have = Materials(emm);
                    if (have.Count > capacity) add("EMM>CAP", name + ".obj.emm has " + have.Count + " materials, the model takes " + capacity + ": the game doesn't use it (untextured)", false);
                    var lacking = used.Where(u => !Has(have, u)).ToList();
                    if (lacking.Count == 0) continue;
                    string key = string.Join(", ", lacking);
                    if (!white.ContainsKey(key)) white[key] = new List<string>();
                    white[key].Add(color.ToString("D2"));
                }
                foreach (var w in white) add("WHITE", "colors " + string.Join(" ", w.Value) + ": the .obj.emm lacks " + w.Key + " (drawn white)", true);
                if (absent.Count > 0) add("COLORS", "missing colors " + string.Join(" ", absent) + ": Ember only offers a costume with all ten", false);
                if (r.Findings.Any(f => f.Kind == "FILES" || f.Kind == "EMM>CAP" || f.Kind == "WHITE" || f.Kind == "COLORS" || f.Kind == "SLOT")) r.Verdict = Verdict.Faulty;
                results.Add(r);
            }
            return results;
        }

        // ---- fixing ----

        /// <summary>The costume's model with the stand-in's (costume 1) rest pose for every bone on a locator chain (named the
        /// same in both), its inverse bind matrices rebuilt and every skinned vertex moved with its bones, so the mesh sits on
        /// them as before. Returns how many bones changed.</summary>
        public static int Align(byte[] emo, byte[] standIn)
        {
            List<Bone> mine = Skeleton(emo), theirs = Skeleton(standIn);
            var theirBy = ByName(theirs);
            var need = Chains(theirs);
            int s = I32(emo, 16), nodes = s + I32(emo, s + 8), inverse = s + I32(emo, s + 0x1c);
            var oldWorld = Enumerable.Range(0, mine.Count).Select(i => World(mine, i)).ToList();
            int changed = 0;
            foreach (Bone b in mine)
            {
                Bone t;
                if (!need.ContainsKey(b.Name.ToUpperInvariant()) || !theirBy.TryGetValue(b.Name.ToUpperInvariant(), out t)) continue;
                if (!b.Local.SequenceEqual(t.Local)) { b.Local = (float[])t.Local.Clone(); changed++; }
            }
            if (changed == 0) return 0;
            var newWorld = Enumerable.Range(0, mine.Count).Select(i => World(mine, i)).ToList();
            var change = Enumerable.Range(0, mine.Count).Select(i => Multiply(Inverse(oldWorld[i]), newWorld[i])).ToList();
            for (int i = 0; i < mine.Count; i++)
            {
                for (int k = 0; k < 16; k++) Buffer.BlockCopy(BitConverter.GetBytes(mine[i].Local[k]), 0, emo, nodes + i * 80 + 16 + k * 4, 4);
                float[] inv = Inverse(newWorld[i]);
                for (int k = 0; k < 16; k++) Buffer.BlockCopy(BitConverter.GetBytes(inv[k]), 0, emo, inverse + i * 64 + k * 4, 4);
            }
            // vertices: position, normal at +12; skinned (size >= 64): 4 bone numbers at +48 into the submesh's node list,
            // 3 weights at +52 (the 4th is 1 - their sum)
            foreach (int model in Models(emo))
            {
                int vCount = U16(emo, model + 0x10), vSize = U16(emo, model + 0x12), vOff = I32(emo, model + 0x14);
                if (vSize < 64) continue;
                var done = new HashSet<int>();
                foreach (int sub in Submeshes(emo, model))
                {
                    int idxCount = U16(emo, sub + 2), nodeCount = U16(emo, sub + 4);
                    var nodeList = Enumerable.Range(0, nodeCount).Select(k => U16(emo, sub + 38 + idxCount * 2 + k * 2)).ToArray();
                    for (int k = 0; k < idxCount; k++)
                    {
                        int v = U16(emo, sub + 38 + k * 2);
                        if (v >= vCount || !done.Add(v)) continue;
                        int o = model + vOff + v * vSize;
                        float[] w = { BitConverter.ToSingle(emo, o + 52), BitConverter.ToSingle(emo, o + 56), BitConverter.ToSingle(emo, o + 60), 0 };
                        w[3] = 1 - w[0] - w[1] - w[2];
                        var mat = new double[16];
                        for (int j = 0; j < 4; j++)
                        {
                            int id = emo[o + 48 + j];
                            if (w[j] == 0 || id >= nodeList.Length || nodeList[id] >= change.Count) continue;
                            for (int q = 0; q < 16; q++) mat[q] += w[j] * change[nodeList[id]][q];
                        }
                        Transform(emo, o, mat, true);
                        Transform(emo, o + 12, mat, false);
                    }
                }
            }
            return changed;
        }

        static void Transform(byte[] d, int o, double[] m, bool point)
        {
            double x = BitConverter.ToSingle(d, o), y = BitConverter.ToSingle(d, o + 4), z = BitConverter.ToSingle(d, o + 8);
            var r = new double[3];
            for (int c = 0; c < 3; c++) r[c] = x * m[c] + y * m[4 + c] + z * m[8 + c] + (point ? m[12 + c] : 0);
            if (!point) { double len = Math.Sqrt(r[0] * r[0] + r[1] * r[1] + r[2] * r[2]); if (len > 0) for (int c = 0; c < 3; c++) r[c] /= len; }
            for (int c = 0; c < 3; c++) Buffer.BlockCopy(BitConverter.GetBytes((float)r[c]), 0, d, o + c * 4, 4);
        }

        static float[] Inverse(float[] m)
        {
            var a = new double[4, 8];
            for (int r = 0; r < 4; r++) { for (int c = 0; c < 4; c++) a[r, c] = m[r * 4 + c]; a[r, 4 + r] = 1; }
            for (int c = 0; c < 4; c++)
            {
                int p = c;
                for (int r = c + 1; r < 4; r++) if (Math.Abs(a[r, c]) > Math.Abs(a[p, c])) p = r;
                for (int k = 0; k < 8; k++) { double t = a[c, k]; a[c, k] = a[p, k]; a[p, k] = t; }
                double div = a[c, c];
                for (int k = 0; k < 8; k++) a[c, k] /= div;
                for (int r = 0; r < 4; r++)
                {
                    if (r == c) continue;
                    double f = a[r, c];
                    for (int k = 0; k < 8; k++) a[r, k] -= f * a[c, k];
                }
            }
            var inv = new float[16];
            for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) inv[r * 4 + c] = (float)a[r, 4 + c];
            return inv;
        }

        /// <summary>The missing materials put right, as usf4-nesica's fix_costume.py does: when one of the game's costumes
        /// of the fighter has every material the model uses, its .obj.emm per color replaces the costume's (the model was
        /// built on it, Juri JRI_38); otherwise the model's missing names are renamed to the closest material every color has
        /// (same name in other case, else the most alike, else the one the model uses most; Sakura SKR_34). Never adds a
        /// material (the color's .obj.emm must stay within the model's capacity). Returns what it did, or null.</summary>
        public static string FixMaterials(string game, string fighter, string stem, byte[] emo, Dictionary<string, byte[]> files)
        {
            var used = UsedMaterials(emo);
            var colors = Enumerable.Range(1, 10).Select(c => stem + "_" + c.ToString("D2") + ".obj.emm").Where(files.ContainsKey).ToList();
            if (colors.Count == 0) return null;
            var have = colors.ToDictionary(c => c, c => Materials(files[c]));
            if (!have.Values.Any(h => used.Any(u => !Has(h, u)))) return null;
            // a color whose .emm has a material the model doesn't use, named like the one it lacks: rename it in that .emm
            var renamed = new List<string>();
            foreach (string c in colors)
            {
                byte[] emm = files[c];
                int t = I32(emm, 0xc);
                foreach (string missing in used.Where(u => !Has(have[c], u)).ToList())
                {
                    var spare = Enumerable.Range(0, have[c].Count).Where(i => !Has(used, have[c][i]))
                        .Select(i => new { i, r = Alike(have[c][i].ToLowerInvariant(), missing.ToLowerInvariant()) })
                        .Where(x => x.r >= 0.75).OrderByDescending(x => x.r).FirstOrDefault();   // eyes/eye, not ring/finger
                    if (spare == null) continue;
                    byte[] bytes = new byte[32];
                    Encoding.GetEncoding(28591).GetBytes(missing, 0, Math.Min(missing.Length, 31), bytes, 0);
                    Buffer.BlockCopy(bytes, 0, emm, t + I32(emm, t + 4 + spare.i * 4), 32);
                    renamed.Add(c.Substring(0, c.Length - 8) + ": " + have[c][spare.i] + " -> " + missing);
                    have[c][spare.i] = missing;
                }
            }
            string inEmm = renamed.Count > 0 ? "materials renamed in the colors: " + string.Join(", ", renamed) : null;
            if (!have.Values.Any(h => used.Any(u => !Has(h, u)))) return inEmm;
            int capacity = MaterialCapacity(emo);
            for (int costume = 1; costume <= 7; costume++)
            {
                string first = SkinImport.GameFile(game, fighter, fighter + "_" + costume.ToString("D2") + "_01.obj.emm");
                if (first == null) continue;
                var stock = Materials(File.ReadAllBytes(first));
                if (used.Any(u => !Has(stock, u)) || stock.Count > capacity) continue;
                var broken = colors.Where(c => used.Any(u => !Has(have[c], u))).ToList();
                foreach (string c in broken)
                {
                    string game_ = SkinImport.GameFile(game, fighter, fighter + "_" + costume.ToString("D2") + c.Substring(6));
                    files[c] = File.ReadAllBytes(game_ ?? first);
                }
                return (inEmm != null ? inEmm + "; " : "") + "colors " + string.Join(" ", broken.Select(c => c.Substring(c.Length - 10, 2))) +
                    ": materials from the game's costume " + costume.ToString("D2") + " (it has every material the model uses)";
            }
            var common = have.Values.Aggregate((a, b) => a.Intersect(b, StringComparer.OrdinalIgnoreCase).ToList());
            if (common.Count == 0) return null;
            var counts = Models(emo).SelectMany(m => Submeshes(emo, m)).Select(sm => CString(emo, sm + 6)).Where(n => Has(common, n))
                .GroupBy(n => n).OrderByDescending(g => g.Count()).Select(g => g.Key).ToList();
            var map = new Dictionary<string, string>();
            foreach (string missing in used.Where(u => !Has(common, u)))
            {
                string close = common.Select(n => new { n, r = Alike(n, missing) }).Where(x => x.r >= 0.5).OrderByDescending(x => x.r).Select(x => x.n).FirstOrDefault();
                map[missing] = close ?? (counts.Count > 0 ? counts[0] : common[0]);
            }
            foreach (int sm in Models(emo).SelectMany(m => Submeshes(emo, m)).ToList())
            {
                string name = CString(emo, sm + 6), to;
                if (!map.TryGetValue(name, out to)) continue;
                byte[] bytes = new byte[32];
                Encoding.GetEncoding(28591).GetBytes(to, 0, Math.Min(to.Length, 31), bytes, 0);
                Buffer.BlockCopy(bytes, 0, emo, sm + 6, 32);
            }
            return (inEmm != null ? inEmm + "; " : "") + "materials renamed in the model: " + string.Join(", ", map.Select(p => p.Key + " -> " + p.Value));
        }

        // how alike two names are, 0..1 (like Python's difflib ratio, on the longest common subsequence)
        static double Alike(string a, string b)
        {
            var l = new int[a.Length + 1, b.Length + 1];
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    l[i, j] = a[i - 1] == b[j - 1] ? l[i - 1, j - 1] + 1 : Math.Max(l[i - 1, j], l[i, j - 1]);
            return a.Length + b.Length == 0 ? 1 : 2.0 * l[a.Length, b.Length] / (a.Length + b.Length);
        }

        /// <summary>A fixed copy of the costumes in a folder or zip, next to it ("&lt;name&gt; - fixed", a folder or a zip):
        /// locators aligned to the stand-in, materials put right, missing parts (.shd.emo, .nml.emb, .bsr) from the game's
        /// costume 1. The original stays as it is. Returns the copy's path and what was done.</summary>
        public static string Fix(string game, Source source, List<string> done)
        {
            var files = source.Names.ToDictionary(n => n, n => source.Read(n), StringComparer.OrdinalIgnoreCase);
            foreach (string model in source.Names.Where(n => Model.IsMatch(n)).ToList())
            {
                string fighter = model.Substring(0, 3).ToUpperInvariant(), stem = model.Substring(0, 6);
                byte[] standIn = Stock(game, fighter, 1);
                if (standIn == null) { done.Add(stem + ": the game's " + fighter + "_01.obj.emo wasn't found, not aligned"); continue; }
                foreach (string part in new[] { ".obj.emo", ".shd.emo" })
                {
                    byte[] data;
                    if (!files.TryGetValue(stem + part, out data)) continue;
                    data = (byte[])data.Clone();
                    string stockFile = part == ".obj.emo" ? null : SkinImport.GameFile(game, fighter, fighter + "_01" + part);
                    if (part != ".obj.emo" && stockFile == null) continue;
                    byte[] stockPart = part == ".obj.emo" ? standIn : File.ReadAllBytes(stockFile);
                    int bones = Align(data, stockPart);
                    if (bones > 0) { files[stem + part] = data; done.Add(stem + part + ": " + bones + " locator-chain bones set to the stand-in's (costume 01), the mesh moved with them"); }
                }
                string materials = FixMaterials(game, fighter, stem, files[model], files);
                if (materials != null) done.Add(stem + ": " + materials);
                foreach (string part in new[] { ".shd.emo", ".nml.emb", ".bsr" })
                {
                    if (files.ContainsKey(stem + part)) continue;
                    string from = SkinImport.GameFile(game, fighter, fighter + "_01" + part);
                    if (from == null) continue;
                    files[stem + part] = File.ReadAllBytes(from);
                    done.Add(stem + part + ": missing, the game's costume 01's put in");
                }
            }
            string where = source.Where.TrimEnd('\\', '/');
            bool zip = where.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            string target = zip ? where.Substring(0, where.Length - 4) + " - fixed.zip" : where + " - fixed";
            if (zip)
            {
                if (File.Exists(target)) File.Delete(target);
                using (var z = ZipFile.Open(target, ZipArchiveMode.Create))
                    foreach (var f in files)
                        using (var s = z.CreateEntry(f.Key).Open()) s.Write(f.Value, 0, f.Value.Length);
            }
            else
            {
                Directory.CreateDirectory(target);
                foreach (var f in files) File.WriteAllBytes(Path.Combine(target, f.Key), f.Value);
            }
            return target;
        }

        // ---- the report ----

        public static string Report(string game, List<Result> results)
        {
            var t = new StringBuilder();
            t.AppendLine("EX More Stuff costume check, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            t.AppendLine("Game: " + game);
            t.AppendLine(results.Count + " costumes: " + string.Join(", ", Enum.GetValues(typeof(Verdict)).Cast<Verdict>().Reverse()
                .Select(v => results.Count(r => r.Verdict == v) + " " + v.ToString().ToUpperInvariant())));
            t.AppendLine();
            foreach (var r in results.OrderByDescending(r => r.Verdict).ThenBy(r => r.Name))
            {
                t.AppendLine(r.Name + "  " + r.Verdict.ToString().ToUpperInvariant() + "  " + r.Where);
                foreach (var f in r.Findings) t.AppendLine("    " + f.Kind.PadRight(8) + " " + f.Text + (f.Fixable ? "  [fixable]" : ""));
            }
            t.AppendLine();
            t.AppendLine("Verdicts: FAULTY breaks in the game or desyncs online against a player without it; NOISE differs from the");
            t.AppendLine("stand-in only at rounding level (plays the same, but not bit for bit); MIRROR is clean against costume 01 and");
            t.AppendLine("differs from 02/03, the stand-ins in a mirror match; OK.");
            t.AppendLine("DESYNC: a gameplay locator bone (hurtboxes, WAIST, LND, TRS) or a bone above it differs from the stand-in where");
            t.AppendLine("the animations leave it to the model. DESYNC?: a rotation IK may override. IK: the IK records of gameplay bones");
            t.AppendLine("differ (not fixed automatically). EFFECTS: only");
            t.AppendLine("effect locators differ (effects sit elsewhere on that PC; not a fault). Sizes: moved >= 0.001, small >= 0.0001,");
            t.AppendLine("noise below. WHITE: a material the model uses is missing from a color. EMM>CAP: a color has more materials than");
            t.AppendLine("the model takes. FILES / COLORS / SLOT: files or colors the game or Ember need are missing, or the slot isn't 08-99.");
            return t.ToString();
        }
    }
}
