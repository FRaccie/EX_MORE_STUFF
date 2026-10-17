using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ExMoreStuff
{
    static class Program
    {
        public const string Title = "EX More Stuff";
        // The catalog everyone uses. A catalog.txt beside the program (one line: an address or a file path) points
        // it elsewhere, for testing or a catalog kept on disk.
        public const string DefaultCatalog = "https://raw.githubusercontent.com/oopswhats/EX_MORE_STUFF/main/catalog.json";

        [STAThread]
        static void Main()
        {
            // One window at a time: starting it again brings the open one to the front.
            bool first;
            using (var single = new Mutex(true, @"Local\EXMoreStuff", out first))
            {
                if (!first) { ShowOpenWindow(); return; }
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
        }

        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);

        static void ShowOpenWindow()
        {
            int self = Process.GetCurrentProcess().Id;
            Process open = Process.GetProcesses().FirstOrDefault(p => p.Id != self && p.MainWindowTitle == Title &&
                                                                      !p.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase));
            if (open == null) return;
            if (IsIconic(open.MainWindowHandle)) ShowWindow(open.MainWindowHandle, 9);   // SW_RESTORE
            SetForegroundWindow(open.MainWindowHandle);
        }

        public static string CatalogSource()
        {
            string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "catalog.txt");
            if (File.Exists(local))
                foreach (string line in File.ReadAllLines(local))
                    if (line.Trim().Length > 0) return line.Trim();
            return DefaultCatalog;
        }
    }

    // The few things remembered between runs (the game folder; SFxT's folder; packages kept in the game without their zip), in
    // settings.json beside the program (AppFolders).
    static class Settings
    {
        static string SettingsFile { get { return AppFolders.DataFile("settings.json"); } }

        static Dictionary<string, object> Read()
        {
            try { return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(SettingsFile)) ?? new Dictionary<string, object>(); }
            catch (Exception) { return new Dictionary<string, object>(); }
        }

        static void Write(string key, object value)
        {
            var j = Read();
            j[key] = value;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
                SafeFile.WriteAllText(SettingsFile, new JavaScriptSerializer().Serialize(j), true);
            }
            catch (Exception) { }
        }

        public static string GameFolder
        {
            get { object v; return Read().TryGetValue("gameFolder", out v) && v != null ? Convert.ToString(v) : null; }
            set { Write("gameFolder", value); }
        }

        /// <summary>Developer mode (DevMode): switched on or off by holding the header's shrine for 5 seconds.</summary>
        public static bool Developer
        {
            get { object v; return Read().TryGetValue("developer", out v) && v is bool && (bool)v; }
            set { Write("developer", value); }
        }

        /// <summary>Advanced > Performance warnings: stages more than this many times the biggest SF4 stage are flagged (Weight).</summary>
        public static double StageWarning
        {
            get { object v; return Read().TryGetValue("stageWarning", out v) && v != null ? Convert.ToDouble(v) : Weight.StageDefault; }
            set { Write("stageWarning", value); }
        }

        /// <summary>The same for costumes: bones, model or a color more than this many times the game's biggest (Weight).</summary>
        public static double CostumeWarning
        {
            get { object v; return Read().TryGetValue("costumeWarning", out v) && v != null ? Convert.ToDouble(v) : Weight.CostumeDefault; }
            set { Write("costumeWarning", value); }
        }

        /// <summary>Where each warning line came from (Weight): null the default, "tests", or "you" (set by hand: tests leave it).</summary>
        public static string StageWarningBy
        {
            get { object v; return Read().TryGetValue("stageWarningBy", out v) && v != null ? Convert.ToString(v) : null; }
            set { Write("stageWarningBy", value); }
        }

        /// <summary>The automatic lines this PC's tests set (Weight.FromPcCheck, Learn), or null before any.</summary>
        public static double? StageWarningAuto
        {
            get { object v; return Read().TryGetValue("stageWarningAuto", out v) && v != null ? Convert.ToDouble(v) : (double?)null; }
            set { Write("stageWarningAuto", value); }
        }

        public static double? CostumeWarningAuto
        {
            get { object v; return Read().TryGetValue("costumeWarningAuto", out v) && v != null ? Convert.ToDouble(v) : (double?)null; }
            set { Write("costumeWarningAuto", value); }
        }

        public static string CostumeWarningBy
        {
            get { object v; return Read().TryGetValue("costumeWarningBy", out v) && v != null ? Convert.ToString(v) : null; }
            set { Write("costumeWarningBy", value); }
        }

        /// <summary>Mods rated 18+ on GameBanana left off Browse Mods (the About tab's switch, off to begin with; user 2026-10-17).</summary>
        public static bool HideNsfw
        {
            get { object v; return Read().TryGetValue("hideNsfw", out v) && v is bool && (bool)v; }
            set { Write("hideNsfw", value); }
        }

        /// <summary>Street Fighter X Tekken's folder, once chosen for its music (Music page), or null.</summary>
        public static string SfxtFolder
        {
            get { object v; return Read().TryGetValue("sfxtFolder", out v) && v != null ? Convert.ToString(v) : null; }
            set { Write("sfxtFolder", value); }
        }

        /// <summary>Favorite creators on Browse Mods (Creators.Key: their GameBanana member numbers).</summary>
        public static List<string> FavoriteCreators
        {
            get
            {
                object v;
                var list = Read().TryGetValue("favoriteCreators", out v) ? v as System.Collections.ArrayList : null;
                return list == null ? new List<string>() : list.Cast<object>().Select(Convert.ToString).ToList();
            }
            set { Write("favoriteCreators", value); }
        }

        /// <summary>The newest upload Notifications showed when it was last opened (Unix seconds); 0 never.</summary>
        public static long NotificationsSeen
        {
            get { object v; return Read().TryGetValue("notificationsSeen", out v) && v != null ? Convert.ToInt64(v) : 0; }
            set { Write("notificationsSeen", value); }
        }

        /// <summary>Packages (Item.Key) the player chose to keep in the game after deleting their zip: not asked about again.</summary>
        public static List<string> KeptWithoutZip
        {
            get
            {
                object v;
                var list = Read().TryGetValue("keptWithoutZip", out v) ? v as System.Collections.ArrayList : null;
                return list == null ? new List<string>() : list.Cast<object>().Select(Convert.ToString).ToList();
            }
            set { Write("keptWithoutZip", value); }
        }
    }
}
