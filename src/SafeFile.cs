using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ExMoreStuff
{
    // Files written whole or not at all (user 2026-10-17: "lets say the user is moving files, and their EXMS crashes, we
    // dont move files until the final step right"): the new file goes beside the old one as <name>.exms-writing and only
    // replaces it once it's complete, so a crash, a killed program or a full disk never leaves half a file where the game
    // or EX More Stuff reads it. EX More Stuff's lists keep the one before as <name>.bak, read when the list itself won't
    // read. A zip is changed on a copy that's swapped in when it's done. Leftovers of a stopped write are cleared at start.
    static class SafeFile
    {
        public const string Writing = ".exms-writing";

        /// <summary>`data` as the file at `path`, whole or not at all; `backup` keeps the one before as <path>.bak.</summary>
        public static void WriteAllBytes(string path, byte[] data, bool backup = false)
        {
            string temp = path + Writing;
            if (File.Exists(temp)) File.Delete(temp);   // left by a write a crash stopped
            using (var f = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                f.Write(data, 0, data.Length);
                f.Flush(true);
            }
            Swap(temp, path, backup);
        }

        public static void WriteAllText(string path, string text, bool backup = false)
        {
            WriteAllBytes(path, new UTF8Encoding(false).GetBytes(text), backup);
        }

        /// <summary>A finished temporary file in the place of `path` (replacing it, or put there when there's none).</summary>
        public static void Swap(string temp, string path, bool backup = false)
        {
            if (File.Exists(path)) File.Replace(temp, path, backup ? path + ".bak" : null, true);
            else File.Move(temp, path);
        }

        /// <summary>A zip entry as the file at `target`, whole or not at all.</summary>
        public static void Extract(ZipArchiveEntry entry, string target)
        {
            string temp = target + Writing;
            if (File.Exists(temp)) File.Delete(temp);
            entry.ExtractToFile(temp, true);
            Swap(temp, target);
        }

        /// <summary>A list's text: the file, or when it won't read (`reads` says no) the one before it (.bak); null when
        /// there's no file. Throws when neither reads.</summary>
        public static string ReadList(string path, Func<string, bool> reads)
        {
            if (!File.Exists(path)) return null;
            string text = File.ReadAllText(path);
            if (reads(text)) return text;
            string before = path + ".bak";
            if (File.Exists(before)) { string old = File.ReadAllText(before); if (reads(old)) return old; }
            throw new InvalidDataException(Path.GetFileName(path) + " is damaged and has no copy from before");
        }

        /// <summary>A zip changed on a copy (`change` gets it open for updating), swapped in when it's done.</summary>
        public static T EditZip<T>(string path, Func<ZipArchive, T> change)
        {
            string temp = path + Writing;
            if (File.Exists(temp)) File.Delete(temp);
            File.Copy(path, temp);
            T result;
            try { using (var zip = ZipFile.Open(temp, ZipArchiveMode.Update)) result = change(zip); }
            catch { try { File.Delete(temp); } catch (Exception) { } throw; }
            Swap(temp, path);
            return result;
        }

        public static void EditZip(string path, Action<ZipArchive> change) { EditZip(path, zip => { change(zip); return 0; }); }

        /// <summary>Leftovers of writes a crash stopped, in `folder` and below: removed (at start; never while writing).</summary>
        public static void CleanUp(string folder)
        {
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
                foreach (string f in Directory.GetFiles(folder, "*" + Writing, SearchOption.AllDirectories))
                    try { File.Delete(f); } catch (Exception) { }
            }
            catch (Exception) { }
        }
    }
}
