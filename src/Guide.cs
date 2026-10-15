using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ExMoreStuff
{
    // Advanced > Uploading Guide (user 2026-10-12: "a very basic and in some parts self explanatory guide"): a mod already
    // made, made ready to share in three steps (dock it, get it ready, on Browse Mods for everyone), each with buttons to
    // the tools it needs (Go: "addfile", "freecodes", "creation", "browse"). No upload step (user 2026-10-13: "get rid of
    // the upload to game banana option").
    class GuidePanel : Clear
    {
        public event Action<string> Go;
        readonly ScrollList list = new ScrollList { Dock = DockStyle.Fill };
        int filledWidth;

        public GuidePanel() { Controls.Add(list); }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible && filledWidth == 0) Fill();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Visible && Math.Abs(list.ClientSize.Width - filledWidth) > 4) Fill();
        }

        void Fill()
        {
            filledWidth = list.ClientSize.Width;
            int width = Math.Max(400, list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 16);
            var controls = new List<Control>
            {
                new Heading("Uploading Guide", width),
                ShadowLabel.Note("Made a costume or stage? These steps get it ready to share. The buttons open each tool.", width),
                new Heading("1   Dock it", width),
                ShadowLabel.Note("Add Mod From File, pick your mod's zip and press Dock for edits. It stays out of your game, and its card waits at the top of " +
                                 "Advanced.\n\nThe zip needs to be a package made for EX More Stuff: its files in the zip's root, named for its slot or stage code " +
                                 "(CMY_79.obj.emo ..., STG_U12.emz ...), and a costume with all 10 colors.", width),
                Buttons(width, Tuple.Create("Add Mod From File", "addfile")),
                new Heading("2   Get it ready", width),
                ShadowLabel.Note("Its code: find one nobody uses on Free codes (costume slots " + Catalog.FirstCustomCostume + "-" + Catalog.LastShared + " are for sharing), " +
                                 "type it on the docked card and press Change.\n\nColor pictures (a costume): press Color pictures on the docked card. The game " +
                                 "opens once per color and the pictures go into the zip, for rollback games' character select. Right-click a color there for a " +   // ROLLBACK-NAME: 'rollback games' stands for EMBER, never named to players (user 2026-10-14)
                                 "full-size picture for your mod's page.\n\nMusic (a stage): coming soon on the Music tab. A stage's own music can already " +
                                 "go in its zip as BGM_<its code>.csb.", width),
                Buttons(width, Tuple.Create("Free codes", "freecodes"), Tuple.Create("Costume creation", "creation")),
                new Heading("3   On Browse Mods", width),
                ShadowLabel.Note("A mod on GameBanana is on Browse Mods for everyone once GameBanana has checked its file (press Refresh to see it). Players " +
                                 "install it in one click, in the code you chose, which is yours from then on (Free codes shows it taken). Remove the docked " +
                                 "card when you're done.", width),
                Buttons(width, Tuple.Create("Browse Mods", "browse")),
                // where the support buttons on the cards come from (user 2026-10-14: "so people know how it grabs the link")
                new Heading("Ko-fi and Patreon", width),
                ShadowLabel.Note("Add your Ko-fi or Patreon to your GameBanana profile as a donation method. EX More Stuff reads it from there and puts a small " +
                                 "Ko-fi or Patreon button on the cards of your mods and Mod Packs, so players can support you. It's read along with " +
                                 "GameBanana's mod list, so press Refresh after you add it. Only ko-fi.com and patreon.com links are shown.", width),
            };
            list.SuspendLayout();
            foreach (Control c in list.Controls.Cast<Control>().ToList()) c.Dispose();
            list.Controls.Clear();
            foreach (Control c in controls) { list.Controls.Add(c); list.SetFlowBreak(c, true); }
            list.ResumeLayout();
        }

        // a step's buttons in a row under its text
        Control Buttons(int width, params Tuple<string, string>[] buttons)
        {
            var row = new Clear { Size = new Size(width - 16, 40), Margin = new Padding(8, 0, 8, 6) };
            int x = 0;
            foreach (var b in buttons)
            {
                var button = new FlatButton(b.Item1) { Location = new Point(x, 2) };
                string where = b.Item2;
                button.Click += (s, e) => { var go = Go; if (go != null) go(where); };
                row.Controls.Add(button);
                x = button.Right + 8;
            }
            return row;
        }
    }
}
