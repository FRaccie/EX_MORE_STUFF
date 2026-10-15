using System;
using System.Collections.Generic;
using System.Linq;

namespace ExMoreStuff
{
    // Favorite creators on Browse Mods (user 2026-10-11): a heart by a creator's name keeps them in settings.json, by
    // their GameBanana member number, so a creator who renames their account stays a favorite. Their mods show under
    // Favorites; Notifications shows their mods from the last 6 months that aren't installed, newest first, and the
    // Browse Mods tab gets a heart while one of those is newer than when Notifications was last opened.
    static class Creators
    {
        public static readonly TimeSpan NewFor = TimeSpan.FromDays(183);   // "6 months"

        static HashSet<string> favorites;

        static HashSet<string> Favorites
        {
            get { return favorites ?? (favorites = new HashSet<string>(Settings.FavoriteCreators, StringComparer.OrdinalIgnoreCase)); }
        }

        /// <summary>Reads the favorites from the settings again (tests, after changing where the program's folder is).</summary>
        public static void Forget() { favorites = null; }

        /// <summary>A creator's key: their GameBanana member number, or their name in a list kept before numbers were read.</summary>
        public static string Key(GameBanana.Mod mod) { return mod.AuthorId > 0 ? mod.AuthorId.ToString() : NameKey(mod); }

        static string NameKey(GameBanana.Mod mod) { return "name:" + (mod.Author ?? "").Trim().ToLowerInvariant(); }

        public static bool IsFavorite(GameBanana.Mod mod) { return Favorites.Contains(Key(mod)) || Favorites.Contains(NameKey(mod)); }

        /// <summary>Makes the mod's creator a favorite, or not any more; returns whether they are now.</summary>
        public static bool Toggle(GameBanana.Mod mod)
        {
            bool now = !IsFavorite(mod);
            if (now) Favorites.Add(Key(mod));
            else { Favorites.Remove(Key(mod)); Favorites.Remove(NameKey(mod)); }
            Settings.FavoriteCreators = Favorites.OrderBy(k => k, StringComparer.Ordinal).ToList();
            return now;
        }

        /// <summary>What's new from the favorite creators: their mods uploaded in the last 6 months that aren't installed
        /// (`installed`: by mod), newest first.</summary>
        public static List<GameBanana.Mod> New(IEnumerable<GameBanana.Mod> mods, Func<GameBanana.Mod, bool> installed, DateTime now)
        {
            long since = new DateTimeOffset(now - NewFor).ToUnixTimeSeconds();
            return mods.Where(m => IsFavorite(m) && m.Added >= since && !installed(m)).OrderByDescending(m => m.Added).ToList();
        }

        /// <summary>Whether any of `news` is newer than when Notifications was last opened.</summary>
        public static bool Unseen(IEnumerable<GameBanana.Mod> news)
        {
            long seen = Settings.NotificationsSeen;
            return news.Any(m => m.Added > seen);
        }

        /// <summary>Notifications was opened: what it shows counts as seen.</summary>
        public static void Seen(IEnumerable<GameBanana.Mod> news)
        {
            long newest = news.Select(m => m.Added).DefaultIfEmpty(0).Max();
            if (newest > Settings.NotificationsSeen) Settings.NotificationsSeen = newest;
        }
    }
}
