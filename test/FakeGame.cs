// A stand-in SSFIV.exe for test\PerfTestTest.cs: does what the helper does in the real game, without the game. Reads
// exms_benchmark.txt beside it, notes what it saw in fake_log.txt (the fight asked for, whether dinput8.dll is the
// helper and not the stand-in Tom's mod, Steam's ID), changes config.ini as the game might, and writes a result:
// 250 fps with a stage or costume put in for the test, 297 without.
// Usage: built as <made-up game>\SSFIV.exe by PerfTestTest; argument: the config.ini to touch.
using System;
using System.IO;
using System.Linq;

static class FakeGame
{
    // a file PerfTestTest put in the patch folder "by hand" (its text starts with "hand") is where the game would load it
    // (the ones this fight uses: Blast Furnace and Hugo for stage 28, Training Stage and Ryu for stage 0)
    static bool Hand(string patch, string stage)
    {
        var used = stage == "28" ? new[] { @"stage\STG_BFU.emz", @"chara\HUG\HUG_01.obj.emo" } : new[] { @"stage\STG_TRN.tex.emz", @"chara\RYU\RYU_01_02.col.emb" };
        return used.Select(f => Path.Combine(patch, f)).Any(f => File.Exists(f) && File.ReadAllText(f).StartsWith("hand"));
    }

    static void Main(string[] args)
    {
        string game = AppDomain.CurrentDomain.BaseDirectory;
        var request = File.ReadAllLines(Path.Combine(game, "exms_benchmark.txt")).Select(l => l.Split(new[] { '=' }, 2)).Where(p => p.Length == 2).GroupBy(p => p[0]).ToDictionary(g => g.Key, g => g.First()[1]);
        string dll = Path.Combine(game, "dinput8.dll");
        bool helper = File.Exists(dll) && !File.ReadAllText(dll).Contains("Round BGM");
        string patch = Path.Combine(game, "patch_ae2_tu3", "battle");
        // something in Blast Furnace's or Ryu's Original's place (the player's own are taken off for a test)
        bool swapped = File.Exists(Path.Combine(patch, "stage", "STG_BFU.emz")) || File.Exists(Path.Combine(patch, "chara", "RYU", "RYU_01.obj.emo"));
        // which costume Ryu's Original is (its color 1's text: "custom 80 color 1" ...), "-" for the game's own
        string color1 = Path.Combine(patch, "chara", "RYU", "RYU_01_01.col.emb");
        string wears = File.Exists(color1) ? File.ReadAllText(color1).Replace(' ', '_') : "-";
        File.AppendAllText(Path.Combine(game, "fake_log.txt"), "p1=" + request["p1"] + " p2=" + request["p2"] + " stage=" + request["stage"] + " helper=" + helper +
                           " steam=" + Environment.GetEnvironmentVariable("SteamAppId") + " swapped=" + swapped + " hand=" + Hand(patch, request["stage"]) + " wears=" + wears + "\r\n");
        string config = Environment.GetEnvironmentVariable("EXMS_FAKE_CONFIG");
        if (config != null && File.Exists(config)) File.AppendAllText(config, "SystemSpeed=1.00000\r\n");
        // a photo (layer=1): the fighters' layer, transparent with P1 a block on the left and P2 one on the right, in the
        // color asked for (patch=26C88:00>NN: red 20 + 20 x NN; P2 green), P1 taller when the custom costume is in
        if (request.ContainsKey("layer"))
        {
            string colorPatch = File.ReadAllLines(Path.Combine(game, "exms_benchmark.txt")).FirstOrDefault(l => l.StartsWith("patch=26C88:00>"));
            int color = colorPatch != null ? Convert.ToInt32(colorPatch.Substring(15), 16) : 0;
            using (var layer = new System.Drawing.Bitmap(1600, 900, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = System.Drawing.Graphics.FromImage(layer))
                {
                    g.Clear(System.Drawing.Color.Transparent);
                    g.FillRectangle(new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 20 + 20 * color, 0, 0)), 300, swapped ? 250 : 400, 200, swapped ? 600 : 450);
                    g.FillRectangle(new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 0, 200, 0)), 1100, 300, 250, 550);
                }
                layer.Save(request["shot"], System.Drawing.Imaging.ImageFormat.Png);
            }
            File.WriteAllText(request["result"], "shots=saved\r\n");
            return;
        }
        // EXMS_FAKE_MODE: "crash" (a swapped run closes without a result), "slow" (a swapped run at 45 fps)
        string mode = Environment.GetEnvironmentVariable("EXMS_FAKE_MODE") ?? "";
        if (swapped && mode == "crash") return;
        // "speed only": a swapped run as on a game whose benchmark the helper can't catch (a player 2026-10-15): the speed the
        // game set, 60 / its average and never under 1 ("speed only slow": 45 fps)
        if (swapped && mode.StartsWith("speed only"))
        {
            File.WriteAllText(request["result"], "speed=" + (mode == "speed only slow" ? "1.333" : "1.000") + "\r\nerror=the benchmark's own numbers weren't caught\r\n");
            return;
        }
        string average = swapped ? (mode == "slow" ? "45.000" : "250.000") : "297.000";
        File.WriteAllText(request["result"], "average=" + average + "\r\nlowest=55.000\r\nhighest=390.000\r\nframes=999\r\nhistogram=0,0,0,0,0,3,987\r\nspeed=1.000\r\n");
    }
}
