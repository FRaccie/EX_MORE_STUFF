using System;
using System.IO;
using Microsoft.Win32;

namespace ExMoreStuff
{
    // GameBanana's 1-Click install (user 2026-10-21, their guide: gamebanana.com/wikis/1999): a mod page's button opens
    // exms:<file URL>,<item type>,<item id>. EX More Stuff registers the exms: link with Windows for this user each time it
    // starts (pointing at wherever it is now; nothing to install). A link opens EX More Stuff with it, or, when it's open
    // already, is handed to the open window through a small file beside the program, which installs that mod from Browse
    // Mods as its own Install button does.
    static class OneClick
    {
        public const string Scheme = "exms";

        /// <summary>A link EX More Stuff was started with, for the window to take once it's ready.</summary>
        public static string Pending;

        public static string InboxName { get { return "one_click.txt"; } }
        static string Inbox { get { return AppFolders.DataFile(InboxName); } }

        /// <summary>exms: links open this program (HKEY_CURRENT_USER, so no administrator is asked); quietly nothing when
        /// it can't.</summary>
        public static void Register()
        {
            try
            {
                string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string command = "\"" + exe + "\" \"%1\"";
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + Scheme))
                {
                    if (key == null) return;
                    using (RegistryKey open = key.OpenSubKey(@"shell\open\command"))
                        if (open != null && string.Equals(open.GetValue("") as string, command, StringComparison.OrdinalIgnoreCase)) return;   // already this exe
                    key.SetValue("", "URL:EX More Stuff");
                    key.SetValue("URL Protocol", "");
                    using (RegistryKey icon = key.CreateSubKey("DefaultIcon")) icon.SetValue("", "\"" + exe + "\",0");
                    using (RegistryKey open = key.CreateSubKey(@"shell\open\command")) open.SetValue("", command);
                }
            }
            catch (Exception) { }
        }

        /// <summary>A link for the window that's open: left beside the program for it to take.</summary>
        public static void Pass(string link)
        {
            try { SafeFile.WriteAllText(Inbox, link); } catch (Exception) { }
        }

        /// <summary>A link left for this window, or null; taken (gone after).</summary>
        public static string Take()
        {
            try
            {
                if (!File.Exists(Inbox)) return null;
                string link = File.ReadAllText(Inbox).Trim();
                File.Delete(Inbox);
                return link.Length > 0 ? link : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>A 1-Click link's GameBanana item: its id as Browse Mods keeps it (a Sound's is negative) and the file's
        /// address. False when it isn't one.</summary>
        public static bool Parse(string link, out long id, out string file)
        {
            id = 0;
            file = null;
            if (link == null || !link.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase)) return false;
            string rest = Uri.UnescapeDataString(link.Substring(Scheme.Length + 1).TrimStart('/'));
            int b = rest.LastIndexOf(','), a = b > 0 ? rest.LastIndexOf(',', b - 1) : -1;
            long number;
            if (a <= 0 || !long.TryParse(rest.Substring(b + 1).Trim().TrimEnd('/'), out number) || number <= 0) return false;
            string type = rest.Substring(a + 1, b - a - 1).Trim();
            file = rest.Substring(0, a).Trim();
            id = type.Equals("Sound", StringComparison.OrdinalIgnoreCase) ? -number : number;
            return type.Equals("Mod", StringComparison.OrdinalIgnoreCase) || type.Equals("Sound", StringComparison.OrdinalIgnoreCase);
        }
    }
}
