// What EX More Stuff's "Make its color pictures in the game" does, for one installed costume of the player's, from
// the command line: the 10 pictures (PerfTest.ColorPictures), a copy of each in <preview folder>, then into the package
// (PerfTest.PutPictures). Usage: MakeColorPictures <game folder> <fighter> <slot> <preview folder>
using System;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace ExMoreStuff
{
    static class MakeColorPictures
    {
        static void Main(string[] args)
        {
            string game = args[0], fighter = args[1], preview = args[3];
            int slot = int.Parse(args[2]);
            Installed record = Installer.Load(game).Single(r => r.Item.IsCostume && r.Item.Fighter == fighter && r.Item.Slot == slot);
            if (record.Source != "file" || !File.Exists(record.Package)) throw new InvalidOperationException("not a package of the player's with its zip");
            var made = PerfTest.ColorPictures(game, record.Item, null, Console.WriteLine);
            Directory.CreateDirectory(preview);
            foreach (var picture in made) picture.Value.Save(Path.Combine(preview, record.Item.Prefix + "_" + picture.Key.ToString("D2") + ".png"), ImageFormat.Png);
            PerfTest.PutPictures(game, record.Item, record, made);
            Console.WriteLine("Put into " + record.Package + " and the game");
        }
    }
}
