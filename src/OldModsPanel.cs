using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ExMoreStuff
{
    // Advanced > Old mods: mods put in the game's patch folders by hand (ModScan), and "Import old mods", which brings the
    // stages and costumes among them into EX More Stuff: each becomes a package of the player's in one of their own seats
    // (kept in Mods\), and its original loose files move to Mods\Legacy mods backup (AppFolders.BackUpOldMod; user 2026-10-14:
    // "placed somewhere safe"; they went to the Recycle Bin before). Other files are only listed.
    class OldModsPanel : Clear
    {
        string game;
        readonly ScrollList list = new ScrollList { Dock = DockStyle.Fill };
        readonly FlatButton import = new FlatButton("Import old mods", true);
        readonly ShadowLabel status = new ShadowLabel { Wrap = false, Height = ShadowLabel.LineHeight, Margin = new Padding(8, 0, 8, 4) };
        List<ModScan.Found> found = new List<ModScan.Found>();
        int filledWidth;
        bool stale, scanning;

        public event Action Changed;          // packages were added (the window reads its lists again)
        public event Action<bool> Working;    // an import started or ended (the rest of the window waits)
        public Func<Task> BeforeImport;       // the window's first-time PC check (PerfTest), before anything goes in

        public OldModsPanel()
        {
            Controls.Add(list);
            import.Margin = new Padding(8, 4, 8, 8);
            import.Click += async (s, e) => await Import();
        }

        public void SetGame(string folder)
        {
            game = folder;
            if (Visible) Fill(); else stale = true;
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible && (stale || filledWidth == 0)) Fill();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (game != null && Visible && filledWidth > 0 && Math.Abs(list.ClientSize.Width - filledWidth) > 4) Show(found);
        }

        // scans in the background (a few thousand files), then lists what it found
        async void Fill()
        {
            if (scanning) { stale = true; return; }   // again once this scan is done
            stale = false;
            if (!Game.IsGameFolder(game)) { found = new List<ModScan.Found>(); Show(found); return; }
            scanning = true;
            Say("Looking through the game's folders...");
            string g = game;
            try { found = await Task.Run(() => ModScan.Scan(g)); Say(""); }
            catch (Exception ex) { found = new List<ModScan.Found>(); Say("The game's folders couldn't be read: " + ex.Message, true); }
            finally { scanning = false; }
            Show(found);
            if (stale && Visible) Fill();
        }

        void Show(List<ModScan.Found> mods)
        {
            filledWidth = list.ClientSize.Width;
            int width = Math.Max(400, list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 16);
            int stages = mods.Count(m => m.Kind == ModScan.Kind.Stage), costumes = mods.Count(m => m.Kind == ModScan.Kind.Costume);
            var controls = new List<Control>
            {
                new Heading("Import Your Legacy Mods", width),   // user 2026-10-14 (was "Old mods")
                ShadowLabel.Note("Manage your installed mods with EX More Stuff instead of loose files over the game's own. Each stage and costume becomes a package " +
                                 "of yours in its own slot, and its original files are moved to Mods\\Legacy mods backup. To put one over a game stage or costume " +
                                 "again, pick it on the Stages page or that fighter's page. Other files (HUD, sounds, moves) stay as they are.", width),
            };
            import.Enabled = stages + costumes > 0;
            import.Text = stages + costumes > 0 ? "Import legacy mods (" + (stages + costumes) + ")" : "Import legacy mods";
            import.Width = TextRenderer.MeasureText(import.Text, import.Font).Width + 36;
            controls.Add(import);
            status.Width = width - 16;
            controls.Add(status);
            if (mods.Count == 0 && !scanning) controls.Add(ShadowLabel.Note("No legacy mods found: everything in the game's folders is the game's own or EX More Stuff's.", width));
            foreach (var m in mods)
                controls.Add(new OldModRow
                {
                    Size = new Size(width, 64), Margin = new Padding(6, 3, 6, 3), Title = m.What,
                    Detail = m.Files.Count + " file" + (m.Files.Count > 1 ? "s" : "") + "   ·   " + m.Files[0] + (m.Files.Count > 1 ? " ..." : "") + "   ·   " + m.Newest.ToString("d MMM yyyy"),
                    State = m.CanImport ? (m.Kind == ModScan.Kind.Stage ? "Stage: can be imported" : "Costume: can be imported") : "Stays as it is",
                    CanImport = m.CanImport,
                });
            list.SuspendLayout();
            foreach (Control c in list.Controls.Cast<Control>().ToList()) if (c != import && c != status) c.Dispose();
            list.Controls.Clear();
            foreach (Control c in controls) { list.Controls.Add(c); list.SetFlowBreak(c, true); }
            list.ResumeLayout();
        }

        async Task Import()
        {
            var importable = found.Where(m => m.CanImport).ToList();
            if (importable.Count == 0) return;
            if (Game.IsRunning()) { Say("Close the game first, then import", true); return; }
            // every mod that moves listed, then Replaced Content or Only As Bonus Content (LegacyDialog, user 2026-10-14)
            LegacyDialog.Choice how;
            using (var ask = new LegacyDialog(importable.Select(m => m.What).ToList())) { ask.ShowDialog(FindForm()); how = ask.Picked; }
            if (how == LegacyDialog.Choice.Cancel) return;
            if (BeforeImport != null) await BeforeImport();
            bool keep = how == LegacyDialog.Choice.Replaced;
            var done = new List<string>();
            var problems = new List<string>();
            string g = game;
            Action<string> backUp = path => AppFolders.BackUpOldMod(g, path.Substring(g.TrimEnd('\\').Length + 1));   // safe in Mods\Legacy mods backup
            var before = Weight.Keys(g);
            Busy(true);
            try
            {
                int step = 0;
                foreach (var m in importable)
                {
                    Say(string.Format("Importing {0} ({1}/{2})...", m.What, ++step, importable.Count));
                    try { string where = await Task.Run(() => ModScan.Import(g, m, keep, backUp)); done.Add(m.What + " -> " + where); }
                    catch (Exception ex) { problems.Add(m.What + ": " + ex.Message); }
                }
            }
            finally { Busy(false); }
            var changed = Changed;
            if (changed != null) changed(); else Fill();   // the window's reload lists them again
            var notes = Weight.New(g, before);   // size warnings for what came in: they inform, nothing was stopped
            string noted = notes.Count > 0 ? "\n\nNote: " + string.Join("\n\n", notes) : "";
            if (problems.Count > 0)
                MessageBox.Show(FindForm(), (done.Count > 0 ? done.Count + " imported.\n\n" : "") + "Some couldn't be imported:\n\n" + string.Join("\n", problems) + noted,
                    "EX More Stuff", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else if (notes.Count > 0)
                MessageBox.Show(FindForm(), done.Count + " imported." + noted, "EX More Stuff", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Say(done.Count + " imported" + (keep ? ", showing where they were" : "") + (problems.Count > 0 ? "; " + problems.Count + " couldn't be" : ""), problems.Count > 0);
        }

        void Busy(bool on)
        {
            import.Enabled = !on;
            UseWaitCursor = on;
            var working = Working;
            if (working != null) working(on);
        }

        void Say(string text, bool problem = false)
        {
            status.Text = text;
            status.ForeColor = problem ? Theme.Warning : Theme.Text;
        }
    }

    // The import question (user 2026-10-14, their words): the mods that move, that their files go to EX More Stuff's
    // folders, then Replaced Content (they keep playing in place of the game's) or Only As Bonus Content (the game's own
    // come back, theirs are extra picks in rollback games). Either way they're imported the same.
    class LegacyDialog : Form
    {
        public enum Choice { Cancel, Replaced, Bonus }

        public Choice Picked { get; private set; }

        public LegacyDialog(IList<string> mods)
        {
            Text = "Import Your Legacy Mods";
            Font = Theme.Font(9f);
            BackColor = Theme.Base;
            ForeColor = Theme.Text;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            const int width = 600, pad = 22;
            const int shown = 10;   // more than this: "and N more"
            string list = string.Join("\n", mods.Take(shown).Select(m => "    " + m)) + (mods.Count > shown ? "\n    and " + (mods.Count - shown) + " more" : "");
            string moved = "Their files will be moved to EX More Stuff's folders: each becomes a package in Mods, and its original files go to Mods\\Legacy mods backup.";
            Func<string, Font, int, int> tall = (t, f, w) => TextRenderer.MeasureText(t, f, new Size(w, 0), TextFormatFlags.WordBreak).Height;
            int y = 18;
            var heading = new Label { Text = "Manage your legacy DLC as...", AutoSize = false, BackColor = Color.Transparent, Font = Theme.Bold(13f), ForeColor = Theme.Text,
                                      Bounds = new Rectangle(pad, y, width - 2 * pad, 30) };
            y += 38;
            var names = new Label { Text = list, AutoSize = false, BackColor = Color.Transparent, Font = Theme.Font(9.5f), ForeColor = Theme.Text,
                                    Bounds = new Rectangle(pad, y, width - 2 * pad, tall(list, Theme.Font(9.5f), width - 2 * pad) + 4) };
            y = names.Bottom + 8;
            var move = new Label { Text = moved, AutoSize = false, BackColor = Color.Transparent, Font = Theme.Font(9.5f), ForeColor = Theme.Muted,
                                   Bounds = new Rectangle(pad, y, width - 2 * pad, tall(moved, Theme.Font(9.5f), width - 2 * pad) + 4) };
            y = move.Bottom + 18;
            // the two choices side by side, each with what it means under it
            int column = (width - 2 * pad - 16) / 2;
            var replaced = new FlatButton("Replaced Content", true) { Width = column, Location = new Point(pad, y) };
            var bonus = new FlatButton("Only As Bonus Content") { Width = column, Location = new Point(pad + column + 16, y) };
            const string replacedText = "Your content replaces original SF4 Costumes and Stages", bonusText = "Your content becomes bonus items during rollback games.";   // ROLLBACK-NAME: 'rollback games' stands for EMBER, never named to players (user 2026-10-14)
            int under = Math.Max(tall(replacedText, Theme.Font(9f), column), tall(bonusText, Theme.Font(9f), column)) + 4;
            var replacedAbout = new Label { Text = replacedText, AutoSize = false, BackColor = Color.Transparent, Font = Theme.Font(9f), ForeColor = Theme.Muted,
                                            TextAlign = ContentAlignment.TopCenter, Bounds = new Rectangle(pad, replaced.Bottom + 6, column, under) };
            var bonusAbout = new Label { Text = bonusText, AutoSize = false, BackColor = Color.Transparent, Font = Theme.Font(9f), ForeColor = Theme.Muted,
                                         TextAlign = ContentAlignment.TopCenter, Bounds = new Rectangle(bonus.Left, bonus.Bottom + 6, column, under) };
            y = replacedAbout.Bottom + 14;
            const string anyTime = "Your content can be set to Replaced or not at any time with EXMS";
            var note = new Label { Text = anyTime, AutoSize = false, BackColor = Color.Transparent, Font = Theme.Font(9f), ForeColor = Theme.Muted,
                                   TextAlign = ContentAlignment.TopCenter, Bounds = new Rectangle(pad, y, width - 2 * pad, tall(anyTime, Theme.Font(9f), width - 2 * pad) + 4) };
            y = note.Bottom + 14;
            var cancel = new FlatButton("Cancel");
            cancel.Location = new Point(width - pad - cancel.Width, y);
            ClientSize = new Size(width, cancel.Bottom + 18);
            replaced.Click += (s, e) => { Picked = Choice.Replaced; DialogResult = DialogResult.OK; };
            bonus.Click += (s, e) => { Picked = Choice.Bonus; DialogResult = DialogResult.OK; };
            cancel.Click += (s, e) => { Picked = Choice.Cancel; DialogResult = DialogResult.Cancel; };
            Controls.AddRange(new Control[] { heading, names, move, replaced, bonus, replacedAbout, bonusAbout, note, cancel });
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.DarkTitleBar(Handle); }

        protected override bool ProcessCmdKey(ref Message msg, Keys keys)
        {
            if (keys == Keys.Escape) { Picked = Choice.Cancel; DialogResult = DialogResult.Cancel; return true; }
            return base.ProcessCmdKey(ref msg, keys);
        }
    }

    // One old mod: what it is, its files, and whether it can be imported.
    class OldModRow : Clear
    {
        public string Title, Detail;
        public string State;
        public bool CanImport;

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Smooth(g);
            using (var path = Theme.Rounded(new RectangleF(1, 1, Width - 3, Height - 3), 10))
            {
                using (var fill = new SolidBrush(Theme.Card)) g.FillPath(fill, path);
                using (var pen = new Pen(Theme.Line)) g.DrawPath(pen, path);
            }
            int right = Width - 16;
            Theme.Draw(g, State, Theme.Bold(9f), CanImport ? Theme.GoodText : Theme.Muted, new Rectangle(right - 220, 10, 220, 22), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            Theme.Draw(g, Title, Theme.Bold(10.5f), Theme.Text, new Rectangle(14, 10, right - 240, 22), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            Theme.Draw(g, Detail, Theme.Font(8.5f), Theme.Muted, new Rectangle(14, 34, right - 14, 18), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.PathEllipsis);
        }
    }
}
