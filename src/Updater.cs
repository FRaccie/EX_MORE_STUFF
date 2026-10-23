using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ExMoreStuff
{
    // EX More Stuff updating itself (the header's update link). The release's zip and the .sha256 next to it (both made by
    // .github/workflows/release.yml) are downloaded and the zip checked against it; its EXMoreStuff.exe then takes this one's
    // place: this exe is renamed EXMoreStuff.exe.old (Windows lets a running exe be renamed) and the new one moved in. Once the
    // window has closed, Program.Main starts the new one; if it falls over or doesn't get its window going within 30 s, the
    // old one goes back and starts instead. The new one deletes the .old one once the old one has let go of it.
    static class Updater
    {
        static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        static string Exe { get { return Application.ExecutablePath; } }
        static string Old { get { return Exe + ".old"; } }
        static bool ready;

        /// <summary>Downloads, checks and puts in place the zip at `url`. Returns null when done, else why not, in a few words.</summary>
        public static async Task<string> Download(string url)
        {
            string fresh = Exe + ".new";
            try
            {
                byte[] zip = await http.GetByteArrayAsync(url);
                string expected = (await http.GetStringAsync(url + ".sha256")).Trim().Split(' ', '\t')[0];
                using (var sha = SHA256.Create())
                    if (!BitConverter.ToString(sha.ComputeHash(zip)).Replace("-", "").Equals(expected, StringComparison.OrdinalIgnoreCase))
                        return "the download didn't match its checksum";
                using (var archive = new ZipArchive(new MemoryStream(zip)))
                {
                    ZipArchiveEntry entry = archive.GetEntry("EXMoreStuff.exe");
                    if (entry == null) return "the download has no EXMoreStuff.exe";
                    entry.ExtractToFile(fresh, true);
                }
                if (File.Exists(Old)) File.Delete(Old);
                File.Move(Exe, Old);
                File.Move(fresh, Exe);
                ready = true;
                return null;
            }
            catch (HttpRequestException) { return "the download didn't come through"; }
            catch (TaskCanceledException) { return "the download took too long"; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
            {
                try { if (!File.Exists(Exe) && File.Exists(Old)) File.Move(Old, Exe); File.Delete(fresh); } catch (Exception) { }
                return ex.Message.TrimEnd('.');
            }
        }

        /// <summary>After the window closed: starts the new version put in place by Download, if any.</summary>
        public static void Relaunch()
        {
            if (!ready) return;
            try
            {
                Process started = Process.Start(Exe);
                bool up;
                try { up = started.WaitForInputIdle(30000); }
                catch (InvalidOperationException) { up = false; started.WaitForExit(10000); }   // gone already, or no window at all
                if (up || started.HasExited && started.ExitCode == 0) return;
                if (!started.HasExited) { started.Kill(); started.WaitForExit(5000); }
                File.Delete(Exe);
                File.Move(Old, Exe);
                Process.Start(Exe);
            }
            catch (Exception) { }
        }

        /// <summary>At start: the version this one replaced, if it's still there (tried again for a minute while the
        /// old one is still checking this one started).</summary>
        public static void CleanUp()
        {
            Task.Run(async () =>
            {
                for (int i = 0; i < 12 && File.Exists(Old); i++)
                {
                    try { File.Delete(Old); } catch (Exception) { await Task.Delay(5000); }
                }
            });
        }
    }
}
