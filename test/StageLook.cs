using System;
using System.IO;
using System.Reflection;

namespace ExMoreStuff
{
    // Developer run (user 2026-10-14: a docked stage's look, "hide them"): the game's benchmark fight on a stage with the
    // helper's hide=1 (the fighters' layer cleared every frame), saving a few frames, through PerfTest's own run (Begin
    // sets another dinput8.dll aside, Recover puts everything back). Opens the real game for a few seconds.
    //   StageLook <game folder> <stage code> <output folder>
    static class StageLook
    {
        static object Call(string name, params object[] args)
        {
            return typeof(PerfTest).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
        }

        static void Main(string[] args)
        {
            string game = args[0], code = args[1].ToUpperInvariant(), output = args[2];
            string mode = args.Length > 3 ? args[3] : "hide=1;slide=1;log=1";   // lines joined by ";"; or rtshots=1: every render target of one frame
            Directory.CreateDirectory(output);
            if (code == "RECOVER") { Console.WriteLine("recover: " + (PerfTest.Recover(game) ?? "nothing to put back")); return; }
            AppFolders.Root = output;   // the result file goes here, not beside the player's EX More Stuff
            int stage = (int)Call("StageId", code);
            string shot = Path.Combine(output, code + ".png");
            Call("Begin", game, null, null);
            try
            {
                Call("RunGame", game, "p1=0\r\np2=1\r\nstage=" + stage + "\r\nshot=" + shot + "\r\nshotat=" + (mode.Contains("slide=1") ? "14,55,999" : mode.StartsWith("hide=1") ? "100,240,400" : "240") + "\r\n" + mode.Replace(";", "\r\n") + "\r\nquit=shots\r\npatch=26B00:02>03\r\n");   // training mode: both stand idle
                Console.WriteLine("done");
            }
            catch (TargetInvocationException ex) { Console.WriteLine("game run: " + ex.InnerException.Message); }
            finally { Console.WriteLine("recover: " + (PerfTest.Recover(game) ?? "nothing to put back")); }
            foreach (string f in Directory.GetFiles(output, code + "*.png")) Console.WriteLine("  " + f);
        }
    }
}
