using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ExMoreStuff
{
    // Browse Mods' Creators list (user 2026-10-11): Favorites first (all favorite creators' mods at once), then the
    // favorite creators, then everyone else alphabetically; each with their GameBanana profile picture on the left, how
    // many mods they have, and a heart on the right to make them a favorite or not. A dark panel that drops down under
    // the Creators button, as the Character list does; the mouse wheel scrolls a long one.
    class CreatorPicker : Clear
    {
        public sealed class Row
        {
            public string Key, Name, Picture;
            public int Count;
            public bool Favorite;
        }

        public const string FavoritesKey = "favorites";
        public event Action<string> Picked;   // a creator's key, or FavoritesKey
        public Func<Row, bool> Hearted;       // a row's heart was clicked: whether the creator is a favorite now
        public event Action<Row, Rectangle> HeartedAt;   // the same, with where the heart is (for the message above it)
        public Func<string, Image> Picture;   // a profile picture by its address (null while it's being read)
        public string Selected;
        readonly List<Row> rows;
        readonly int favoritesCount;

        const int ListWidth = 360, RowHeight = 40, Pad = 12, Header = 50, MostRows = 12;
        int hover = -1, scroll;
        bool overHeart;

        public CreatorPicker(List<Row> rows, int favoritesCount)
        {
            this.rows = rows;
            this.favoritesCount = favoritesCount;
            Size = new Size(ListWidth, Header + Math.Min(MostRows, rows.Count + 1) * RowHeight + Pad);
            Cursor = Cursors.Hand;
            BackColor = Theme.Strip;
        }

        int Places { get { return rows.Count + 1; } }   // Favorites, then the creators
        int Shown { get { return Math.Min(MostRows, Places); } }

        Rectangle Cell(int place) { return new Rectangle(Pad, Header + (place - scroll) * RowHeight, Width - Pad * 2, RowHeight - 4); }

        static Rectangle HeartArea(Rectangle cell) { return new Rectangle(cell.Right - 32, cell.Y, 28, cell.Height); }

        int At(Point p)
        {
            for (int place = scroll; place < Math.Min(Places, scroll + Shown); place++) if (Cell(place).Contains(p)) return place;
            return -1;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int was = scroll;
            scroll = Math.Max(0, Math.Min(Places - Shown, scroll - Math.Sign(e.Delta) * 2));
            if (scroll != was) Invalidate();
            base.OnMouseWheel(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int now = At(e.Location);
            bool heart = now > 0 && HeartArea(Cell(now)).Contains(e.Location);
            if (now != hover || heart != overHeart) { hover = now; overHeart = heart; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; overHeart = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int place = At(e.Location);
            if (place < 0) return;
            if (place > 0 && HeartArea(Cell(place)).Contains(e.Location))
            {
                Row row = rows[place - 1];
                if (Hearted != null) row.Favorite = Hearted(row);
                if (HeartedAt != null) HeartedAt(row, HeartArea(Cell(place)));
                Invalidate();
                return;
            }
            var picked = Picked;
            if (picked != null) picked(place == 0 ? FavoritesKey : rows[place - 1].Key);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Smooth(g);
            using (var fill = new SolidBrush(Theme.Strip)) g.FillRectangle(fill, ClientRectangle);
            using (var pen = new Pen(Theme.Line)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            Theme.Draw(g, "Creators", Theme.Bold(13f), Theme.Text, new Rectangle(Pad + 4, 12, 300, 30), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            using (var pen = new Pen(Theme.Line)) g.DrawLine(pen, Pad, Header - 8, Width - Pad, Header - 8);
            for (int place = scroll; place < Math.Min(Places, scroll + Shown); place++)
            {
                Rectangle cell = Cell(place);
                Row row = place > 0 ? rows[place - 1] : null;
                string key = row != null ? row.Key : FavoritesKey;
                bool chosen = key == Selected;
                if (chosen || place == hover)
                    using (var path = Theme.Rounded(cell, 8))
                    using (var brush = new SolidBrush(chosen ? Color.FromArgb(70, Theme.Accent) : Color.FromArgb(22, 255, 255, 255)))
                    {
                        g.FillPath(brush, path);
                        if (chosen) using (var pen = new Pen(Theme.Accent)) g.DrawPath(pen, path);
                    }
                // the picture on the left: the creator's profile picture, or a heart for Favorites
                var face = new Rectangle(cell.X + 6, cell.Y + 3, cell.Height - 6, cell.Height - 6);
                using (var path = Theme.Rounded(face, 6))
                {
                    using (var well = new SolidBrush(Theme.Well)) g.FillPath(well, path);
                    Image picture = row != null && !string.IsNullOrEmpty(row.Picture) && Picture != null ? Picture(row.Picture) : null;
                    if (row == null) HeartButton.Draw(g, face, true, false, 12f);
                    else if (picture != null)
                    {
                        g.SetClip(path);
                        int side = Math.Min(picture.Width, picture.Height);
                        g.DrawImage(picture, face, new Rectangle((picture.Width - side) / 2, (picture.Height - side) / 2, side, side), GraphicsUnit.Pixel);
                        g.ResetClip();
                    }
                    else   // no picture of their own (or still coming): their name's first letter
                        Theme.Draw(g, row.Name.Length > 0 ? row.Name.Substring(0, 1).ToUpperInvariant() : "?", Theme.Bold(12f), Theme.Muted, face,
                                   TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                int count = row != null ? row.Count : favoritesCount;
                Theme.Draw(g, row != null ? row.Name : "Favorites", Theme.Bold(10f), row != null || count > 0 ? Theme.Text : Theme.Muted,
                           new Rectangle(face.Right + 10, cell.Y, cell.Width - face.Width - 96, cell.Height), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                Theme.Draw(g, count.ToString(), Theme.Font(9f), chosen ? Theme.AccentHover : Theme.Muted, new Rectangle(cell.Right - 76, cell.Y, 36, cell.Height),
                           TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                if (row != null) HeartButton.Draw(g, HeartArea(cell), row.Favorite, place == hover && overHeart, 11f);
            }
            if (Places > Shown)   // a thin bar showing where in a long list this is
            {
                int track = Shown * RowHeight, bar = Math.Max(20, track * Shown / Places), at = Header + (track - bar) * scroll / (Places - Shown);
                using (var brush = new SolidBrush(Color.FromArgb(60, 255, 255, 255))) g.FillRectangle(brush, Width - 6, at, 3, bar);
            }
        }

        /// <summary>Shows the list under `under`, closing when one is picked or the player clicks elsewhere.</summary>
        public static ToolStripDropDown Show(Control under, CreatorPicker picker, Action<string> picked)
        {
            var host = new ToolStripControlHost(picker) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = picker.Size };
            var drop = new ToolStripDropDown { Padding = Padding.Empty, Renderer = new CharacterPicker.Dark(), DropShadowEnabled = true };
            drop.Items.Add(host);
            picker.Picked += key => { drop.Close(); picked(key); };
            drop.Closed += (s, e) => CharacterPicker.BeginDispose(drop);
            drop.Show(under, new Point(0, under.Height + 4));
            picker.Focus();   // so the mouse wheel scrolls it
            return drop;
        }
    }
}
