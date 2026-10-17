using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ExMoreStuff
{
    // Advanced > Costume creation (user 2026-10-12: "a new little sub menu for costume creation"): tools for people
    // making costumes. The first: color pictures. A costume of the player's own (a package with its zip) in each of its
    // 10 colors as the game draws it, standing idle, no stage or life bars (PerfTest.ColorPictures), put into its package
    // as <prefix>_<CC>.png for EMBER's character select, ready to share (Tom: "a render of the character with the costume
    // for each color in chara select"). Each row shows the pictures its package has.
    class CreationPanel : Clear
    {
        string game;
        readonly ScrollList list = new ScrollList { Dock = DockStyle.Fill };
        readonly ShadowLabel status = new ShadowLabel { Wrap = false, Height = ShadowLabel.LineHeight, Margin = new Padding(8, 0, 8, 4) };
        int filledWidth;
        bool stale, working;
        // a search over the list (user 2026-10-16: "costume creation tab needs a search function"): every word in its name,
        // fighter or zip; it stays put while the rows below it change
        readonly TextBox search = new TextBox { Width = 220, BackColor = Theme.Well, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Font(10.5f), Margin = new Padding(8, 2, 8, 6) };
        readonly Timer typing = new Timer { Interval = 300 };

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        public event Action<bool> Working;    // pictures being made (the rest of the window waits)
        public event Action<string> Changed;  // a package got new pictures (its key): the window reads its lists again
        public Func<string> Blocked;          // why nothing can be made right now, or null

        public CreationPanel()
        {
            Controls.Add(list);
            search.HandleCreated += (s, e) => SendMessage(search.Handle, 0x1501, (IntPtr)1, "Search costumes");   // EM_SETCUEBANNER: the grey hint
            search.TextChanged += (s, e) => { typing.Stop(); typing.Start(); };
            typing.Tick += (s, e) => { typing.Stop(); ShowRows(); };
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
            if (!Visible && !working) stale = true;   // left: all colors picked again next time (nothing is kept, user)
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Visible && Math.Abs(list.ClientSize.Width - filledWidth) > 4) Fill();
        }

        // costumes docked for edits (Docked: not in the game) and the player's own costume packages whose zip is there, the
        // newest first (user 2026-10-16: "the top list should be the newest ones you added or installed"): by when its zip was
        // made (adding, installing or docking it makes it; color pictures go into the same zip and don't move it)
        List<Installed> Costumes()
        {
            if (!Game.IsGameFolder(game)) return new List<Installed>();
            return Docked.Load().Where(r => r.Item.IsCostume)
                .Concat(Installer.Load(game).Where(r => r.Item.IsCostume && r.Source == "file" && !string.IsNullOrEmpty(r.Package) && File.Exists(r.Package)))
                .OrderByDescending(r => Made(r.Package)).ThenBy(r => Fighters.Name(r.Item.Fighter)).ThenBy(r => r.Item.Slot).ToList();
        }

        static DateTime Made(string zip)
        {
            try { return string.IsNullOrEmpty(zip) || !File.Exists(zip) ? DateTime.MinValue : File.GetCreationTimeUtc(zip); }
            catch (Exception) { return DateTime.MinValue; }
        }

        static string DockedZip(Installed record) { return record.Source == Docked.Source ? record.Package : null; }

        void Fill(bool keepPlace = false)
        {
            Point place = list.AutoScrollPosition;
            stale = false;
            filledWidth = list.ClientSize.Width;
            int width = Math.Max(400, list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 16);
            var controls = new List<Control>
            {
                new Heading("Costume creation", width),
                ShadowLabel.Note("Tools for making costumes.\n\nColor pictures: EX More Stuff opens the game once per color, a few seconds each, and takes a picture of " +
                                 "your costume standing idle, as the game draws it (no stage, no life bars). They go into the costume's package for rollback games' " +   // ROLLBACK-NAME: 'rollback games' stands for EMBER, never named to players (user 2026-10-14)
                                 "character select, ready to share. Steam needs to be running and the game closed.", width),
            };
            controls.Add(search);
            status.Width = width - 16;
            controls.Add(status);
            list.SuspendLayout();
            foreach (Control c in list.Controls.Cast<Control>().ToList()) if (c is CreationRow) c.Dispose();
            list.Controls.Clear();
            foreach (Control c in controls) { list.Controls.Add(c); list.SetFlowBreak(c, true); }
            list.ResumeLayout();
            ShowRows();
            // made again after something done here (a debug color, pictures): where it was (user 2026-10-16: "it pulls the list
            // all the way to the top when its finished")
            if (keepPlace) list.AutoScrollPosition = new Point(-place.X, -place.Y);
        }

        // the costumes the search finds, under the search box (the top stays, so typing goes on)
        void ShowRows()
        {
            int width = Math.Max(400, list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 16);
            string[] words = search.Text.Trim().ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var all = Costumes();
            var costumes = all.Where(r => words.All(w => (Weight.Name(r) + " " + Fighters.Name(r.Item.Fighter) + " " + Path.GetFileName(r.Package)).ToLowerInvariant().Contains(w))).ToList();
            var rows = new List<Control>();
            if (all.Count == 0) rows.Add(ShadowLabel.Note("No costume packages of your own yet: add one with Advanced > Add Mod From File (docked for edits, or " +
                                                          "into the game), or from Browse Mods.", width));
            else if (costumes.Count == 0) rows.Add(ShadowLabel.Note("No costumes match \"" + search.Text.Trim() + "\".", width));
            foreach (Installed record in costumes)
            {
                var row = new CreationRow(Weight.Name(record) + (DockedZip(record) != null ? "   (docked)" : ""), Path.GetFileName(record.Package), Pictures(record), width) { Margin = new Padding(8, 4, 8, 4) };
                Installed r = record;
                row.Make.Click += async (s, e) => await Make(r, row.Colors);
                row.FullSizeAsked += async color => await MakeFullSize(r, color);
                row.ForceAsked += async color => await ForceDebug(r, color);
                row.Busy = working;
                rows.Add(row);
            }
            list.SuspendLayout();
            int keep = list.Controls.IndexOf(status) + 1;
            while (list.Controls.Count > keep) { Control c = list.Controls[keep]; list.Controls.RemoveAt(keep); c.Dispose(); }
            foreach (Control c in rows) { list.Controls.Add(c); list.SetFlowBreak(c, true); }
            list.ResumeLayout();
        }

        // the color pictures a package has (<prefix>_01.png ... _10.png in its zip), null where there's none
        static Image[] Pictures(Installed record)
        {
            var found = new Image[PerfTest.ColorCount];
            try
            {
                using (ZipArchive zip = ZipFile.OpenRead(record.Package))
                    for (int c = 1; c <= found.Length; c++)
                    {
                        string name = record.Item.Prefix + "_" + c.ToString("D2") + ".png";
                        ZipArchiveEntry entry = zip.Entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
                        if (entry == null) continue;
                        using (Stream s = entry.Open())
                        using (var data = new MemoryStream())
                        {
                            s.CopyTo(data);
                            found[c - 1] = Image.FromStream(new MemoryStream(data.ToArray()));
                        }
                    }
            }
            catch (Exception) { }
            return found;
        }

        void Say(string text, bool problem = false)
        {
            status.Text = text;
            status.ForeColor = problem ? Theme.Warning : Theme.Text;
            if (problem) BugReport.Noted(text);
        }

        async Task Make(Installed record, List<int> colors)
        {
            if (colors.Count == 0) return;
            string blocked = (Blocked != null ? Blocked() : null) ?? PerfTest.Blocked();
            if (blocked != null) { Say("Can't make them now: " + blocked, true); return; }
            string name = Weight.Name(record), g = game, problem = null;
            string which = colors.Count == PerfTest.ColorCount ? "each color" : colors.Count == 1 ? "color " + colors[0] : "colors " + string.Join(", ", colors);
            if (MessageBox.Show(FindForm(), "EX More Stuff opens the game " + (colors.Count == 1 ? "once" : colors.Count + " times") + ", a few seconds each, and takes a picture of " +
                    name + " standing in " + which + ". " + (colors.Count == 1 ? "It goes" : "They go") + " into its package for rollback games' character select.", "EX More Stuff",   // ROLLBACK-NAME: 'rollback games' stands for EMBER, never named to players (user 2026-10-14)
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
            SortedDictionary<int, Bitmap> made = null;
            SetWorking(true);
            try
            {
                made = await Task.Run(() => PerfTest.ColorPictures(g, record.Item, colors, text => BeginInvoke((Action)(() => Say(text))), false, DockedZip(record)));
                Say("Putting the pictures into " + name + "'s package...");
                await Task.Run(() => PerfTest.PutPictures(g, record.Item, record, made));
            }
            catch (Exception ex) { problem = ex.Message; }
            finally { SetWorking(false); }
            if (problem != null)
            {
                Say("The color pictures weren't made: " + problem, true);
                if (made != null) foreach (Bitmap b in made.Values) b.Dispose();
                return;
            }
            Say(name + ": " + (made.Count == 1 ? "color " + made.Keys.First() + "'s picture" : made.Count + " color pictures") + " made and put into its package");
            var changed = Changed;
            if (changed != null) changed(record.Item.Key);
            Fill(true);
            using (var preview = new PicturePreview(name + ": color pictures", made)) preview.ShowDialog(FindForm());
            // then its character's folder in Mods, its zip picked (user 2026-10-12): ready to share
            try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + record.Package + "\""); } catch (Exception) { }
        }

        // One color at the game's own size (a thumbnail for the mod's page, say), beside the package's zip as
        // "<zip name> - color N.png" (made again: replaced); the package isn't touched. Then Explorer shows it.
        async Task MakeFullSize(Installed record, int color)
        {
            string blocked = (Blocked != null ? Blocked() : null) ?? PerfTest.Blocked();
            if (blocked != null) { Say("Can't make it now: " + blocked, true); return; }
            string name = Weight.Name(record), g = game, problem = null;
            string file = Path.Combine(Path.GetDirectoryName(record.Package), Path.GetFileNameWithoutExtension(record.Package) + " - color " + color + ".png");
            SetWorking(true);
            try
            {
                var made = await Task.Run(() => PerfTest.ColorPictures(g, record.Item, new[] { color }, text => BeginInvoke((Action)(() => Say(text))), true, DockedZip(record)));
                using (Bitmap picture = made[color]) picture.Save(file, System.Drawing.Imaging.ImageFormat.Png);
            }
            catch (Exception ex) { problem = ex.Message; }
            finally { SetWorking(false); }
            if (problem != null) { Say("The full-size picture wasn't made: " + problem, true); return; }
            Say(name + ": color " + color + " at full size saved beside its zip (" + Path.GetFileName(file) + ")");
            try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + file + "\""); } catch (Exception) { }
        }

        // a color made a debug color (ColorVariants.Force): in its package, and in the game's files when it's in
        async Task ForceDebug(Installed record, int color)
        {
            if (working) return;
            bool inGame = DockedZip(record) == null;
            if (inGame && Game.IsRunning()) { Say("Can't change it now: close the game first", true); return; }
            string name = Weight.Name(record), g = game, problem = null;
            if (MessageBox.Show(FindForm(), "Make color " + color + " of " + name + " a debug color? One of EX More Stuff's patterns takes the place of its textures, in its package" +
                    (inGame ? " and in the game" : "") + ". Its own color " + color + " can only come back by installing the mod again.", "EX More Stuff",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            SetWorking(true);
            try
            {
                await Task.Run(() =>
                {
                    var made = ColorVariants.Force(record.Package, record.Item.Prefix, color);
                    if (!inGame) return;
                    // the game's copies of those files (the record names where each went)
                    foreach (string file in record.Files)
                    {
                        byte[] data;
                        if (made.TryGetValue(Path.GetFileName(file), out data)) SafeFile.WriteAllBytes(Path.Combine(Game.PatchFolder(g), file), data);
                    }
                });
            }
            catch (Exception ex) { problem = ex.Message; }
            finally { SetWorking(false); }
            if (problem != null) { Say("Color " + color + " wasn't changed: " + problem, true); return; }
            Say(name + ": color " + color + " is a debug color now");
            var changed = Changed;
            if (changed != null) changed(record.Item.Key);
            Fill(true);
        }

        void SetWorking(bool on)
        {
            working = on;
            UseWaitCursor = on;
            foreach (CreationRow row in list.Controls.OfType<CreationRow>()) row.Busy = on;
            var handler = Working;
            if (handler != null) handler(on);
        }
    }

    // One costume on Costume creation: its name and zip, the color pictures it has (empty frames where there's none),
    // and its buttons. Each color is picked (framed in yellow) to begin with; a click on one leaves it out or takes it
    // back, All picks them all or none (user 2026-10-12: to make again only color 10, say), and Make makes the picked.
    class CreationRow : Clear
    {
        public readonly FlatButton Make = new FlatButton("Make color pictures", true);
        readonly FlatButton all = new FlatButton("All");
        public event Action<int> FullSizeAsked;   // a color's right-click menu: that color at the game's own size
        public event Action<int> ForceAsked;      // ... or that color made a debug color (user 2026-10-16: "right click > force debug color")
        readonly string title, detail;
        readonly Image[] pictures;
        readonly bool[] picked;
        bool busy;
        const int Thumb = 44;

        public CreationRow(string title, string detail, Image[] pictures, int width)
        {
            this.title = title;
            this.detail = detail;
            this.pictures = pictures;
            picked = Enumerable.Repeat(true, pictures.Length).ToArray();
            Make.Location = new Point(width - 16 - Make.Width - 14, 14);
            all.Size = new Size(Make.Width, 32);
            all.Location = new Point(Make.Left, Make.Bottom + 8);
            all.Click += (s, e) => { bool on = !picked.All(p => p); for (int i = 0; i < picked.Length; i++) picked[i] = on; Picked(); };
            Size = new Size(width - 16, Math.Max(64 + Thumb * 3 / 2 + 14, all.Bottom + 14));
            Controls.Add(Make);
            Controls.Add(all);
        }

        /// <summary>The colors picked (1-10).</summary>
        public List<int> Colors { get { return Enumerable.Range(1, picked.Length).Where(c => picked[c - 1]).ToList(); } }

        /// <summary>While pictures are being made (here or for another costume): nothing can be changed.</summary>
        public bool Busy { set { busy = value; Picked(); } }

        void Picked()
        {
            Make.Enabled = !busy && picked.Any(p => p);
            all.Enabled = !busy;
            Invalidate();
        }

        Rectangle Cell(int i) { return new Rectangle(14 + i * (Thumb + 6), 58, Thumb, Thumb * 3 / 2); }

        int CellAt(Point p) { for (int i = 0; i < pictures.Length; i++) if (Cell(i).Contains(p)) return i; return -1; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = !busy && CellAt(e.Location) >= 0 ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int i = CellAt(e.Location);
            if (busy || i < 0) return;
            if (e.Button == MouseButtons.Right)   // its menu: this color at full size
            {
                ContextMenuStrip menu = DarkMenu.Create();
                var full = new ToolStripMenuItem("Make a full-size picture of color " + (i + 1));
                int color = i + 1;
                full.Click += (s, a) => { var asked = FullSizeAsked; if (asked != null) asked(color); };
                menu.Items.Add(full);
                var force = new ToolStripMenuItem("Force debug color");
                force.Click += (s, a) => { var asked = ForceAsked; if (asked != null) asked(color); };
                menu.Items.Add(force);
                menu.Closed += (s, a) => CharacterPicker.BeginDispose(menu);
                menu.Show(this, e.Location);
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            picked[i] = !picked[i];
            Picked();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Smooth(g);
            using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 8))
            {
                using (var fill = new SolidBrush(Color.FromArgb(215, Theme.Card))) g.FillPath(fill, path);
                using (var pen = new Pen(Theme.Line)) g.DrawPath(pen, path);
            }
            Theme.Draw(g, title, Theme.Bold(10.5f), Theme.Text, new Rectangle(14, 10, Make.Left - 24, 22), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            Theme.Draw(g, detail, Theme.Font(8.5f), Theme.Muted, new Rectangle(14, 32, Make.Left - 24, 18), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            for (int i = 0; i < pictures.Length; i++)
            {
                Rectangle cell = Cell(i);
                using (var well = new SolidBrush(Theme.Well)) g.FillRectangle(well, cell);
                if (pictures[i] != null) g.DrawImage(pictures[i], cell);
                if (!picked[i]) using (var dim = new SolidBrush(Color.FromArgb(160, 0, 0, 0))) g.FillRectangle(dim, cell);   // left out
                Theme.Draw(g, (i + 1).ToString(), Theme.Font(7.5f), picked[i] ? Theme.Text : Theme.Muted, new Rectangle(cell.X, cell.Bottom - 14, cell.Width - 3, 14), TextFormatFlags.Right | TextFormatFlags.Bottom);
                if (picked[i]) using (var frame = new Pen(Theme.Accent, 2f)) g.DrawRectangle(frame, cell.X - 1, cell.Y - 1, cell.Width + 1, cell.Height + 1);   // picked
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) foreach (Image p in pictures) if (p != null) p.Dispose();
            base.Dispose(disposing);
        }
    }
}
