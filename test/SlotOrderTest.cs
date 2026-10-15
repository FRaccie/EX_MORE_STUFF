// Where a player's own costume goes (SkinImport.FreeSlot): the personal seats 84 down to 80, then 40 down to 8, then
// 79 down to 41 (user 2026-10-12: they upload their own GameBanana mods from 79 down), skipping seats a GameBanana mod
// holds and anything already in the game. Usage: SlotOrderTest <scratch folder>
using System;
using System.IO;
using System.Linq;

namespace ExMoreStuff
{
    static class SlotOrderTest
    {
        static int failures;

        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) failures++; }

        static void Main(string[] args)
        {
            string game = Path.Combine(args[0], "game");
            if (Directory.Exists(args[0])) Directory.Delete(args[0], true);
            string ryu = Path.Combine(Game.PatchFolder(game), "battle", "chara", "RYU");
            Directory.CreateDirectory(ryu);
            Action<int> use = slot => File.WriteAllBytes(Path.Combine(ryu, "RYU_" + slot.ToString("D2") + ".obj.emo"), new byte[1]);

            Check(SkinImport.FreeSlot(game, "RYU") == 84, "first 84");
            foreach (int s in new[] { 84, 83, 82, 81 }) use(s);
            Check(SkinImport.FreeSlot(game, "RYU") == 80, "then down to 80");
            use(80);
            Check(SkinImport.FreeSlot(game, "RYU") == 40, "the personal seats full: 40");
            SharedCodes.Held = new System.Collections.Generic.HashSet<string> { "RYU 39" };
            use(40);
            Check(SkinImport.FreeSlot(game, "RYU") == 38, "then 40 down, past a seat a GameBanana mod holds (39)");
            foreach (int s in Enumerable.Range(8, 31)) use(s);
            Check(SkinImport.FreeSlot(game, "RYU") == 79, "40 down to 8 full: from the top, 79");
            use(79);
            Check(SkinImport.FreeSlot(game, "RYU") == 78, "then 79 down");
            foreach (int s in Enumerable.Range(41, 38)) use(s);
            Check(SkinImport.FreeSlot(game, "RYU") == 0, "all taken: none (85-99 aren't used)");
            Check(SkinImport.NoRoom("RYU") == "Ryu has no free slot left: uninstall one of Ryu's costumes to make room", "and the player is told to make room: " + SkinImport.NoRoom("RYU"));
            Console.WriteLine(failures == 0 ? "All passed" : failures + " failed");
            Environment.Exit(failures == 0 ? 0 : 1);
        }
    }
}
