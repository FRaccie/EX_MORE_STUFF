using System;
using System.IO;
using System.Linq;

namespace ExMoreStuff
{
    // Developer mode (user 2026-10-11): holding the header's shrine for 5 seconds switches it on or off, and the shrine is
    // blue while it's on. What it does: a performance test saves one frame of each benchmark run (the stage as the game
    // draws it, user 2026-10-12) in ShotFolder, the developer's folder: on any other PC it isn't there, so nothing is saved.
    static class DevMode
    {
        public const string ShotFolder = @"X:\Games\SF4 MODDING\Claude\exmore\dev_shots";

        public static bool On
        {
            get { try { return Settings.Developer; } catch (Exception) { return false; } }
            set { try { Settings.Developer = value; } catch (Exception) { } }
        }

        /// <summary>Where a benchmark run's frame goes ("2026-10-12 080512 Stage U01 - Mishima Estate Roof.png"), or null
        /// when developer mode is off or its folder isn't on this PC.</summary>
        public static string Shot(string what)
        {
            if (!On || !Directory.Exists(ShotFolder)) return null;
            string name = new string(what.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
            return Path.Combine(ShotFolder, DateTime.Now.ToString("yyyy-MM-dd HHmmss") + " " + name + ".png");
        }
    }
}
