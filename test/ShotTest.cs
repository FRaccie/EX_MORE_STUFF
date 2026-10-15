// Opens EX More Stuff's window from a built exe and saves captures of its pages (roster, one fighter's costumes,
// stages top and scrolled, Advanced and its pages, Community, About, the replace dialog), without clicking. Usage: ShotTest <EXMoreStuff.exe> <out folder> [fighter code]
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class ShotTest
{
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);

    static Form form;
    static string folder;
    static Control creators;   // the Creators list, drawn on its own
    static bool tabHeart;
    static Control cardName;   // a costume card's creator name
    static Form ask;           // the delete question

    [STAThread]
    static void Main(string[] args)
    {
        // as Program.Main does (this opens the window without it): GitHub and GameBanana need TLS 1.2
        System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Assembly program = Assembly.LoadFrom(args[0]);
        folder = args[1];
        Directory.CreateDirectory(folder);
        string fighter = args.Length > 2 ? args[2] : "RYU";
        // the program's own folder (Mods\, settings.json) is the exe's, not this test's; and only a made-up game (an empty
        // SSFIV.exe): on a real one the window would take the player's packages into this Mods\ when it opens
        program.GetType("ExMoreStuff.AppFolders").GetProperty("Root").SetValue(null, Path.GetDirectoryName(Path.GetFullPath(args[0])));
        string game = (string)program.GetType("ExMoreStuff.Settings").GetProperty("GameFolder").GetValue(null);
        string exe = game == null ? null : Path.Combine(game, "SSFIV.exe");
        if (exe == null || !File.Exists(exe) || new FileInfo(exe).Length > 0)
        {
            Console.Error.WriteLine("settings.json beside the exe must point at a made-up game folder (an empty SSFIV.exe), not " + (game ?? "nothing"));
            Environment.Exit(2);
        }
        form = (Form)Activator.CreateInstance(program.GetType("ExMoreStuff.MainForm"), true);
        int step = 0;
        var timer = new Timer { Interval = 1500 };
        timer.Tick += (s, e) =>
        {
            switch (step++)
            {
                case 1: Save("roster"); Call("ShowCostumes", fighter); break;
                case 2:
                    Save("costumes");
                    // a GameBanana costume's gold name, clicked later (step 19): its creator's mods on Browse Mods
                    cardName = Find(form, c => c.GetType().Name == "GoldName" && c.Parent.GetType().Name == "ItemCard");
                    Call("ShowRoster");
                    Call("ShowTab", Field("stagesTab"));
                    break;
                case 3:
                    Save("stages");
                    ShootMenu((Control)Field("stagesPage"), "test_menu");
                    var list = (ScrollableControl)Field("stagesPage");
                    list.AutoScrollPosition = new Point(0, 600);
                    list.Invalidate(true);
                    break;
                case 4:
                    Save("stages_scrolled");
                    Call("ShowTab", Field("musicTab"));
                    Save("music_tab");
                    Call("ShowTab", Field("advancedTab"));
                    break;
                case 5:
                    Save("advanced");
                    var advanced = (ScrollableControl)Field("advancedPage");
                    advanced.AutoScrollPosition = new Point(0, 2000);   // its end: Performance warnings
                    advanced.Invalidate(true);
                    advanced.Update();
                    Save("advanced_scrolled");
                    // the stage line's + pressed (as a click does), shown, then put back
                    foreach (Control c in advanced.Controls)
                        if (c.GetType().Name == "LineSetting")
                        {
                            var set = c.GetType().GetMethod("Set", BindingFlags.NonPublic | BindingFlags.Instance);
                            set.Invoke(c, new object[] { 4.5 });
                            advanced.Update();
                            Save("advanced_line_changed");
                            set.Invoke(c, new object[] { 4.0 });
                            break;
                        }
                    advanced.AutoScrollPosition = Point.Empty;
                    SetField("openTool", "music");
                    Call("ShowTab", Field("advancedTab"));
                    break;
                case 6: Save("music"); SetField("openTool", "packages"); Call("ShowTab", Field("advancedTab")); break;
                case 7: Save("packages"); SetField("openTool", "storage"); Call("ShowTab", Field("advancedTab")); break;
                case 8: Save("storage"); SetField("openTool", "freecodes"); Call("ShowTab", Field("advancedTab")); break;
                case 9:
                {
                    Save("freecodes");
                    var page9 = Field("freeCodesPage");
                    ((Control)Inner(page9, "slot")).Text = "87";
                    ((Control)Inner(page9, "stage")).Text = "B01";
                    break;
                }
                case 10: Save("freecodes_check"); SetField("openTool", "oldmods"); Call("ShowTab", Field("advancedTab")); break;
                case 11:
                    Save("oldmods");
                    SetField("openTool", "creation");
                    var creation = Field("creationPage");   // as its card does: the game folder first
                    creation.GetType().GetMethod("SetGame").Invoke(creation, new[] { Field("game") });
                    Call("ShowTab", Field("advancedTab"));
                    break;
                case 12:
                    Save("creation");
                    // colors 2 and 5 left out, as clicks on them do
                    Control row = Find((Control)Field("creationPage"), c => c.GetType().Name == "CreationRow");
                    if (row != null)
                        foreach (int i in new[] { 1, 4 })
                            typeof(Control).GetMethod("OnMouseClick", BindingFlags.Instance | BindingFlags.NonPublic)
                                .Invoke(row, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 14 + i * 50 + 20, 58 + 30, 0) });
                    Save("creation_picked");
                    // Share a mod: the guide, top and bottom
                    SetField("openTool", "guide");
                    Call("ShowTab", Field("advancedTab"));
                    Save("guide");
                    var guideList = (ScrollableControl)Inner(Field("guidePage"), "list");
                    guideList.AutoScrollPosition = new Point(0, 3000);
                    guideList.Invalidate(true);
                    guideList.Update();
                    Save("guide_scrolled");
                    SetField("openTool", null);
                    Call("ShowTab", Field("communityTab"));
                    // the delete question, as a package's trash can asks it
                    ask = (Form)Activator.CreateInstance(form.GetType().Assembly.GetType("ExMoreStuff.DeleteDialog"),
                        "Stage U01 - STG BLD Frozen No Reactions", "It's taken out of the game and out of EX More Stuff. Songs you put on U01 go with it.\n\nThe original mod file you added is untouched.");
                    ask.Show(form);   // shot next step, once it has drawn (at once it comes out black)
                    break;
                case 15:
                    Save(ask, "delete_dialog");
                    ask.Close();
                    // Add Mod From File's question, once the files are picked
                    ask = (Form)Activator.CreateInstance(form.GetType().Assembly.GetType("ExMoreStuff.DockDialog"), new object[] { new[] { "Cammy X Lili Tekken Crossover - CMY 79.zip" } });
                    ask.Show(form);
                    Save("community");
                    var community = (ScrollableControl)Inner(Field("communityPage"), "list");
                    community.AutoScrollPosition = new Point(0, 3020);
                    community.Invalidate(true);
                    break;
                case 16:
                    Save(ask, "dock_dialog");
                    ask.Close();
                    Save("community_scrolled");
                    ((Control)Inner(Field("communityPage"), "search")).Text = "akuma";   // the search waits 0.3 s for more typing
                    break;
                case 17:
                {
                    Save("community_search");
                    var page15 = Field("communityPage");
                    ((Control)Inner(page15, "search")).Text = "";
                    // the Character list, drawn on its own (it drops down in a window of its own), then Blanka picked
                    var pickerType = page15.GetType().Assembly.GetType("ExMoreStuff.CharacterPicker");
                    var picker = (Control)Activator.CreateInstance(pickerType, true);
                    pickerType.GetField("Portrait").SetValue(picker, page15.GetType().GetField("Portrait").GetValue(page15));
                    pickerType.GetField("Selected").SetValue(picker, "RYU");
                    var shot = new Bitmap(picker.Width, picker.Height);
                    picker.DrawToBitmap(shot, new Rectangle(0, 0, picker.Width, picker.Height));
                    shot.Save(Path.Combine(folder, "character_picker.png"), ImageFormat.Png);
                    page15.GetType().GetField("pick", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(page15, "BLK");
                    page15.GetType().GetMethod("Fill", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(page15, null);
                    break;
                }
                case 18:
                    Save("community_blanka");
                    // from an installed mod's "Browse Mods" link: True Ryu's card, lit up
                    var page = Field("communityPage");
                    page.GetType().GetMethod("Highlight").Invoke(page, new object[] { "gamebanana:251565:" });
                    break;
                case 19:
                {
                    Save("community_highlight");
                    if (cardName != null)
                    {
                        typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(cardName, new object[] { EventArgs.Empty });
                        Save("community_creator");
                    }
                    // the Updates list (with Update all), when installed mods have newer versions
                    var page17 = Field("communityPage");
                    page17.GetType().GetField("pick", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(page17, "updates");
                    page17.GetType().GetMethod("Fill", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(page17, null);
                    break;
                }
                case 20:
                {
                    Save("community_updates");
                    // with a favorite creator (settings.json's favoriteCreators) who has new mods, the tab has a heart: clicking
                    // the tab opens Notifications; the Creators list (its pictures start loading) is drawn next step
                    Call("ShowTab", Field("stagesTab"));
                    var tab = (Control)Field("communityTab");
                    tabHeart = (bool)tab.GetType().GetProperty("Heart").GetValue(tab);
                    typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(tab, new object[] { EventArgs.Empty });
                    var page20 = Field("communityPage");
                    creators = (Control)page20.GetType().GetMethod("CreatorList", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(page20, null);
                    break;
                }
                case 21:
                {
                    Save("community_notifications");
                    var tab = (Control)Field("communityTab");
                    Console.WriteLine("tab heart before the click: " + tabHeart + ", after: " + tab.GetType().GetProperty("Heart").GetValue(tab));
                    var shot = new Bitmap(creators.Width, creators.Height);
                    creators.DrawToBitmap(shot, new Rectangle(0, 0, creators.Width, creators.Height));
                    shot.Save(Path.Combine(folder, "creator_picker.png"), ImageFormat.Png);
                    Call("ShowTab", Field("aboutTab"));
                    break;
                }
                case 22:
                {
                    // the Creators list again, its pictures in by now
                    var later = new Bitmap(creators.Width, creators.Height);
                    creators.DrawToBitmap(later, new Rectangle(0, 0, creators.Width, creators.Height));
                    later.Save(Path.Combine(folder, "creator_picker.png"), ImageFormat.Png);
                }
                    Save("about");
                    Call("ShowTab", Field("stagesTab"));
                    // The replace dialog is modal: opened from a posted call, the timer keeps ticking inside it.
                    form.BeginInvoke(new Action(() => Call("ChooseReplacement", "DET")));
                    break;
                case 24: Save(Dialog(), "replace_open"); Pick("TRN"); break;
                case 26: Save(Dialog(), "replace_trn"); Pick("VIE"); break;
                case 28: Save(Dialog(), "replace_vie"); Dialog().DialogResult = DialogResult.Cancel; break;
                case 29: timer.Stop(); form.Close(); break;
            }
        };
        form.Shown += (s, e) => timer.Start();
        Application.Run(form);
    }

    static object Field(string name)
    {
        return form.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
    }

    static object Inner(object owner, string name)
    {
        return owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
    }

    static void SetField(string name, object value)
    {
        form.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, value);
    }

    static void Call(string name, params object[] args)
    {
        form.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, args);
    }

    static Form Dialog()
    {
        foreach (Form open in Application.OpenForms) if (open != form) return open;
        return form;
    }

    static void Pick(string code)
    {
        Form dialog = Dialog();
        dialog.GetType().GetMethod("Pick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dialog, new object[] { code });
    }

    [DllImport("user32.dll")] static extern bool EnumThreadWindows(uint thread, EnumWindowsProc callback, IntPtr param);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    delegate bool EnumWindowsProc(IntPtr window, IntPtr param);

    // The first card mark's menu (the performance test), as a click opens it: drawn on its own, then closed.
    static void ShootMenu(Control page, string name)
    {
        Control mark = Find(page, c => c.GetType().Name == "WarningMark" && c.Visible);
        if (mark == null) return;
        typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(mark, new object[] { EventArgs.Empty });
        EnumThreadWindows(GetCurrentThreadId(), (window, param) =>
        {
            var menu = Control.FromHandle(window) as ToolStripDropDown;
            if (menu == null || !menu.Visible) return true;
            using (var bitmap = new Bitmap(menu.Width, menu.Height))
            {
                menu.DrawToBitmap(bitmap, new Rectangle(0, 0, menu.Width, menu.Height));
                bitmap.Save(Path.Combine(folder, name + ".png"), ImageFormat.Png);
            }
            menu.Close();
            return false;
        }, IntPtr.Zero);
    }

    static Control Find(Control root, Func<Control, bool> match)
    {
        foreach (Control c in root.Controls)
        {
            if (match(c)) return c;
            Control inner = Find(c, match);
            if (inner != null) return inner;
        }
        return null;
    }

    static void Save(string name) { Save(form, name); }

    static void Save(Form window, string name)
    {
        window.Refresh();
        using (var bitmap = new Bitmap(window.Width, window.Height))
        {
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                IntPtr dc = g.GetHdc();
                PrintWindow(window.Handle, dc, 2);
                g.ReleaseHdc(dc);
            }
            bitmap.Save(Path.Combine(folder, name + ".png"), ImageFormat.Png);
        }
    }
}
