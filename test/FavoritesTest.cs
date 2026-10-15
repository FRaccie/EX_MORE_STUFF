// Favorite creators (Creators.cs): a heart keeps a creator by their GameBanana member number in settings.json (by name in
// a list kept before numbers were read); Notifications is their mods from the last 6 months that aren't installed,
// newest first; the tab's heart shows while one is newer than when Notifications was last opened.
// Usage: FavoritesTest <scratch folder>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ExMoreStuff
{
    static class FavoritesTest
    {
        static int failures;

        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) failures++; }

        static GameBanana.Mod Mod(long id, long author, string name, DateTime added)
        {
            return new GameBanana.Mod { Id = id, AuthorId = author, Author = name, Name = "mod " + id, Added = new DateTimeOffset(added).ToUnixTimeSeconds() };
        }

        static void Main(string[] args)
        {
            string scratch = args[0];
            if (Directory.Exists(scratch)) Directory.Delete(scratch, true);
            Directory.CreateDirectory(scratch);
            AppFolders.Root = scratch;
            Creators.Forget();
            DateTime now = DateTime.Now;
            var fresh = Mod(1, 10, "Maker", now.AddDays(-10));
            var old = Mod(2, 10, "Maker", now.AddDays(-200));
            var other = Mod(3, 20, "Someone", now.AddDays(-5));
            var nameOnly = Mod(4, 0, "Old List", now.AddDays(-1));
            var mods = new List<GameBanana.Mod> { fresh, old, other, nameOnly };

            Console.WriteLine("Favorites");
            Check(!Creators.IsFavorite(fresh) && Creators.Key(fresh) == "10" && Creators.Key(nameOnly) == "name:old list", "keys: the member number, or the name when the list has none");
            Check(Creators.Toggle(fresh) && Creators.IsFavorite(old) && !Creators.IsFavorite(other), "a heart makes the creator a favorite (all their mods)");
            Creators.Forget();
            Check(Creators.IsFavorite(fresh) && Settings.FavoriteCreators.SequenceEqual(new[] { "10" }), "kept in settings.json by member number");
            var renamed = Mod(5, 10, "New Name", now);
            Check(Creators.IsFavorite(renamed), "a creator who renamed their account stays a favorite");
            Check(Creators.Toggle(nameOnly) && Creators.IsFavorite(nameOnly), "a creator known only by name can be one too");

            Console.WriteLine("Notifications");
            Func<GameBanana.Mod, bool> none = m => false;
            var news = Creators.New(mods, none, now);
            Check(news.Select(m => m.Id).SequenceEqual(new long[] { 4, 1 }), "favorites' mods from the last 6 months, newest first (not the old one, not someone else's)");
            Check(Creators.New(mods, m => m.Id == 1, now).Select(m => m.Id).SequenceEqual(new long[] { 4 }), "not the installed ones");
            Check(Creators.Unseen(news), "not opened yet: the tab's heart shows");
            Creators.Seen(news);
            Check(!Creators.Unseen(news) && Settings.NotificationsSeen == nameOnly.Added, "opened: the heart goes");
            var newer = Mod(6, 10, "Maker", now.AddMinutes(1));
            Check(Creators.Unseen(Creators.New(mods.Concat(new[] { newer }), none, now.AddMinutes(2))), "something newer: it comes back");

            Console.WriteLine("No longer a favorite");
            Check(!Creators.Toggle(fresh) && !Creators.IsFavorite(old) && Creators.IsFavorite(nameOnly), "the heart again: not a favorite any more, the other stays");
            Creators.Forget();
            Check(Settings.FavoriteCreators.SequenceEqual(new[] { "name:old list" }), "and settings.json says so");

            Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
            Environment.Exit(failures == 0 ? 0 : 1);
        }
    }
}
