using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ExMoreStuff
{
    // About > Report a bug (user 2026-10-13: "where can they send us some info without an email"): the user's Tally form,
    // opened in the player's browser with what EX More Stuff knows filled in, so they need no account or email; the
    // button's tooltip says what's sent (the user made them all Tally hidden fields). Form: its link with the URL parameters version, windows and lasterror
    // (Tally hidden fields of those names, the default answers of visible questions) and linux (a hidden field only, user
    // 2026-10-13: whether it runs on Linux, through Wine or Proton), VERSION, WINDOWS, LASTERROR and LINUX replaced here;
    // empty, the button doesn't show.
    static class BugReport
    {
        const string Form = "https://tally.so/r/dWLyAd?version=VERSION&windows=WINDOWS&lasterror=LASTERROR&linux=LINUX";

        static string lastProblem;

        // while there's a form, and this copy isn't over 2 years old by the PC's clock (user 2026-10-13: reports from a copy
        // that old aren't wanted; a newer one will be out)
        public static bool Ready { get { return Form != "" && !TooOld(Built(), DateTime.Now); } }

        /// <summary>Whether a copy built at `built` is too old to report bugs at `now`: more than 2 years since. A build date
        /// that can't be read isn't too old.</summary>
        public static bool TooOld(DateTime? built, DateTime now) { return built != null && now > built.Value.AddYears(2); }

        /// <summary>When this copy was built: the time the compiler writes in the program's header (its PE TimeDateStamp,
        /// the build time: build.bat's csc isn't deterministic), or null when it can't be read or isn't believable.</summary>
        public static DateTime? Built()
        {
            try
            {
                byte[] head = new byte[4096];
                using (var s = System.IO.File.OpenRead(typeof(BugReport).Assembly.Location)) s.Read(head, 0, head.Length);
                int pe = BitConverter.ToInt32(head, 0x3C);
                if (pe < 0 || pe + 12 > head.Length || head[pe] != 'P' || head[pe + 1] != 'E') return null;
                DateTime built = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(BitConverter.ToUInt32(head, pe + 8)).ToLocalTime();
                return built.Year >= 2024 && built.Year < 2100 ? built : (DateTime?)null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>A problem EX More Stuff just showed the player (an install that failed, changes that didn't go through):
        /// the form's "last message" answer.</summary>
        public static void Noted(string problem) { if (!string.IsNullOrEmpty(problem)) lastProblem = problem.Length > 1500 ? problem.Substring(0, 1500) : problem; }

        /// <summary>The form's address with this copy's version, Windows (or Wine), the last problem and Linux or not filled in.</summary>
        public static string Link(string version)
        {
            string wine = Wine();
            return Form.Replace("VERSION", Uri.EscapeDataString(version ?? "")).Replace("WINDOWS", Uri.EscapeDataString(wine ?? WindowsName()))
                       .Replace("LASTERROR", Uri.EscapeDataString(Scrub(lastProblem ?? ""))).Replace("LINUX", Uri.EscapeDataString(wine != null ? "yes: " + wine : "no"));
        }

        /// <summary>A message with the player's own folder taken out of its paths (user 2026-10-13: "lets protect user
        /// privacy"): C:\Users\name\... becomes %USERPROFILE%\..., and any other X:\Users\name\ becomes X:\Users\(user)\, so no
        /// Windows user name is sent.</summary>
        public static string Scrub(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');
            if (profile.Length > 3) text = System.Text.RegularExpressions.Regex.Replace(text, System.Text.RegularExpressions.Regex.Escape(profile), "%USERPROFILE%",
                                                                                         System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return System.Text.RegularExpressions.Regex.Replace(text, @"([A-Za-z]:[\\/]Users[\\/])[^\\/:*?""<>|\r\n]+", "$1(user)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        // "Windows 10 Home 22H2 (build 19045)": from the registry (.NET Framework's own version number can't tell 10 from 11)
        static string WindowsName()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    string product = Convert.ToString(key.GetValue("ProductName")), display = Convert.ToString(key.GetValue("DisplayVersion") ?? key.GetValue("ReleaseId"));
                    string build = Convert.ToString(key.GetValue("CurrentBuild"));
                    int number;
                    if (int.TryParse(build, out number) && number >= 22000) product = product.Replace("Windows 10", "Windows 11");   // 11 still says 10 there
                    return (product + " " + display).Trim() + (build != "" ? " (build " + build + ")" : "");
                }
            }
            catch (Exception) { return Environment.OSVersion.VersionString; }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)] static extern IntPtr GetProcAddress(IntPtr module, string name);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr WineVersion();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void WineHost(out IntPtr system, out IntPtr release);

        // Running through Wine (Proton is Wine): "Linux, Proton (Wine 9.0)", else null. Wine's ntdll has wine_get_version,
        // Windows' doesn't; wine_get_host_version names the system underneath (Linux, or Darwin: macOS).
        static string Wine()
        {
            try
            {
                IntPtr ntdll = GetModuleHandle("ntdll.dll"), get = ntdll == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(ntdll, "wine_get_version");
                if (get == IntPtr.Zero) return null;
                string version = Marshal.PtrToStringAnsi(((WineVersion)Marshal.GetDelegateForFunctionPointer(get, typeof(WineVersion)))());
                string system = "Linux";
                IntPtr host = GetProcAddress(ntdll, "wine_get_host_version");
                if (host != IntPtr.Zero)
                {
                    IntPtr name, release;
                    ((WineHost)Marshal.GetDelegateForFunctionPointer(host, typeof(WineHost)))(out name, out release);
                    string s = Marshal.PtrToStringAnsi(name);
                    if (s == "Darwin") system = "macOS"; else if (!string.IsNullOrEmpty(s)) system = s;
                }
                bool proton = Environment.GetEnvironmentVariable("STEAM_COMPAT_DATA_PATH") != null;   // Proton sets it for the game's prefix
                return system + ", " + (proton ? "Proton (Wine " + version + ")" : "Wine " + version);
            }
            catch (Exception) { return "Linux or macOS, Wine (its version unknown)"; }
        }
    }
}
