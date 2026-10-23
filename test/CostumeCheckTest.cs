// The costume check on a costume made from the game's own C.Viper (costume 1, put in slot 90): clean, it's OK; with its
// neck moved it desyncs, a material taken out of a color draws white, a part left out is missing; the fixer's copy is
// OK again, and the original stays as it was.
// Build: csc -target:exe -main:ExMoreStuff.CostumeCheckTest (the references of build.bat) test\CostumeCheckTest.cs src\*.cs
// Usage: CostumeCheckTest <game> <empty work folder> [<results window picture.png>]
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ExMoreStuff
{
    static class CostumeCheckTest
    {
        static int failed;

        static void Check(bool ok, string what) { Console.WriteLine((ok ? "ok    " : "FAIL  ") + what); if (!ok) failed++; }

        [STAThread]
        static void Main(string[] args)
        {
            string game = args[0], work = Path.Combine(args[1], "AGL_90");
            Directory.CreateDirectory(work);
            Func<string, string> stock = name => SkinImport.GameFile(game, "AGL", name);
            File.Copy(stock("AGL_01.obj.emo"), Path.Combine(work, "AGL_90.obj.emo"), true);
            foreach (string part in new[] { ".shd.emo", ".nml.emb", ".bsr" }) File.Copy(stock("AGL_01" + part), Path.Combine(work, "AGL_90" + part), true);
            for (int c = 1; c <= 10; c++)
                foreach (string part in new[] { ".col.emb", ".obj.emm" })
                    File.Copy(stock("AGL_01_" + c.ToString("D2") + part), Path.Combine(work, "AGL_90_" + c.ToString("D2") + part), true);

            var clean = CostumeCheck.Check(game, CostumeCheck.FromFolder(work)).Single();
            Check(clean.Verdict == CostumeCheck.Verdict.Ok || clean.Verdict == CostumeCheck.Verdict.Mirror, "the game's own model is OK (" + clean.Verdict + ")");

            // the neck 0.1 higher (Samus AGL_09 had it 0.098 lower), color 3's first material (eye) renamed eyes, no .bsr
            string model = Path.Combine(work, "AGL_90.obj.emo");
            byte[] emo = File.ReadAllBytes(model);
            int s = BitConverter.ToInt32(emo, 16), nodes = s + BitConverter.ToInt32(emo, s + 8), names = s + BitConverter.ToInt32(emo, s + 12);
            for (int i = 0; i < BitConverter.ToUInt16(emo, s); i++)
            {
                int at = s + BitConverter.ToInt32(emo, names + i * 4);
                if (System.Text.Encoding.ASCII.GetString(emo, at, 5) != "Neck\0") continue;
                int y = nodes + i * 80 + 16 + 13 * 4;
                Buffer.BlockCopy(BitConverter.GetBytes(BitConverter.ToSingle(emo, y) + 0.1f), 0, emo, y, 4);
            }
            File.WriteAllBytes(model, emo);
            string emm = Path.Combine(work, "AGL_90_03.obj.emm");
            byte[] m = File.ReadAllBytes(emm);
            int t = BitConverter.ToInt32(m, 0xc), first = t + BitConverter.ToInt32(m, t + 4);
            m[first + 3] = (byte)'s';
            File.WriteAllBytes(emm, m);
            File.Delete(Path.Combine(work, "AGL_90.bsr"));
            byte[] before = File.ReadAllBytes(model);

            var broken = CostumeCheck.Check(game, CostumeCheck.FromFolder(work)).Single();
            Func<string, bool> has = kind => broken.Findings.Any(f => f.Kind == kind);
            Check(broken.Verdict == CostumeCheck.Verdict.Faulty, "the changed costume is FAULTY");
            Check(broken.Findings.Any(f => f.Kind == "DESYNC" && f.Text.Contains("NECK")), "the moved neck desyncs");
            Check(broken.Findings.Any(f => f.Kind == "WHITE" && f.Text.Contains("colors 03")), "color 3 draws white");
            Check(broken.Findings.Any(f => f.Kind == "FILES" && f.Text.Contains("AGL_90.bsr")), "the .bsr is missing");
            Console.WriteLine(CostumeCheck.Report(game, new List<CostumeCheck.Result> { broken }));

            var done = new List<string>();
            string copy = CostumeCheck.Fix(game, CostumeCheck.FromFolder(work), done);
            foreach (string d in done) Console.WriteLine("  fixed: " + d);
            var fixedResult = CostumeCheck.Check(game, CostumeCheck.FromFolder(copy)).Single();
            Check(fixedResult.Verdict == CostumeCheck.Verdict.Ok || fixedResult.Verdict == CostumeCheck.Verdict.Mirror, "the fixed copy is OK (" + fixedResult.Verdict + ")");
            Check(File.ReadAllBytes(model).SequenceEqual(before), "the original stays as it was");
            Check(done.Any(d => d.Contains("AGL_90_03: eyes -> eye")) && !done.Any(d => d.Contains("in the model")), "color 3 gets its eye back, the model keeps its names");

            // a folder of several costumes (as the game's chara folder): one of them fixed into a zip of its own, as the
            // results window does
            foreach (string f in Directory.GetFiles(copy)) File.Copy(f, Path.Combine(work, Path.GetFileName(f).Replace("AGL_90", "AGL_91")), true);
            var both = CostumeCheck.Check(game, CostumeCheck.FromFolder(work));
            Check(both.Count == 2 && both.Single(x => x.Name == "AGL_91").Verdict != CostumeCheck.Verdict.Faulty, "a folder of two: AGL_91 (the fixed files) isn't faulty");
            string zip = Path.Combine(args[1], "AGL_90 - fixed.zip");
            var zipDone = new List<string>();
            CostumeCheck.Fix(game, CostumeCheck.Only(CostumeCheck.FromFolder(work), "AGL_90"), zipDone, zip);
            var inZip = CostumeCheck.FromZip(zip);
            Check(inZip.Names.All(n => n.StartsWith("AGL_90")) && inZip.Names.Count > 20, "the zip holds AGL_90's files only (" + inZip.Names.Count + ")");
            var zipResult = CostumeCheck.Check(game, inZip).Single();
            Check(zipResult.Verdict != CostumeCheck.Verdict.Faulty, "the zipped copy isn't faulty (" + zipResult.Verdict + ")");

            // the results window, off screen, as a picture (args[2])
            if (args.Length > 2)
            {
                var source = CostumeCheck.FromFolder(work);
                var window = new CostumeCheckWindow(game, "Costume check: test", both, both.ToDictionary(x => x, x => source))
                { StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new System.Drawing.Point(-4000, -4000) };
                window.Shown += (o, e) =>
                {
                    using (var shot = new System.Drawing.Bitmap(window.Width, window.Height))
                    {
                        window.DrawToBitmap(shot, new System.Drawing.Rectangle(0, 0, window.Width, window.Height));
                        shot.Save(args[2]);
                    }
                    window.Close();
                };
                System.Windows.Forms.Application.Run(window);
                Check(File.Exists(args[2]), "the results window drew");
            }
            Console.WriteLine(failed == 0 ? "all passed" : failed + " failed");
            Environment.Exit(failed == 0 ? 0 : 1);
        }
    }
}
