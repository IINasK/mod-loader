using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

internal static class GeneralTableDialogTests {
    internal static int Run(string parent) {
        string root = OnlineTests.NestedRoot(parent,"table-dialogs-"); Directory.CreateDirectory(root);
        var checks = new List<string>(); Action<bool, string> check = (ok, message) => { if (!ok) throw new IOException(message); checks.Add(message); };
        bool animations = GeneralStyle.AnimationsEnabled, originalDark = GeneralStyle.Dark, originalRed = GeneralStyle.RedTheme; string originalLanguage = GeneralStyle.Language; GeneralStyle.AnimationsEnabled = false;
        try {
            using (var dialog = new GeneralNameDialog("Preset", "  Friends  ")) {
                Open(dialog); var name = Find<TextBox>(dialog, "NameInput"); var save = Find<Button>(dialog, "SaveName");
                name.Text = "   "; check(!save.Enabled, "An empty name disables confirmation.");
                name.Text = "  New friends  "; save.PerformClick();
                check(dialog.DialogResult == DialogResult.OK && dialog.ResultName == "New friends", "Name is trimmed and returned only on confirmation.");
            }
            using (var dialog = new GeneralNameDialog("Preset", "Original")) {
                Open(dialog); Find<TextBox>(dialog, "NameInput").Text = "Discarded"; Find<Button>(dialog, "CancelName").PerformClick();
                check(dialog.DialogResult == DialogResult.Cancel && dialog.ResultName == null, "Cancel does not publish the edited name.");
            }
            var pending = Enumerable.Range(1, 25).Select(index => "Mod " + index).ToArray();
            using (var dialog = new GeneralInstallConfirmation(Path.Combine(root, "game"), 30, pending)) {
                Open(dialog);
                var table = Find<DataGridView>(dialog, "PendingDownloads");
                var scroll = Descendants(dialog).OfType<GeneralScrollView>().Single();
                scroll.RefreshScroll();
                check(table.ReadOnly && table.Rows.Count == 25 && table.Rows.Cast<DataGridViewRow>().Select(row => (string)row.Cells[0].Value).SequenceEqual(pending),
                    "Install confirmation shows all pending downloads in individual read-only rows.");
                check(scroll.Bar.Visible && table.DisplayedRowCount(false) < pending.Length, "Long confirmations use the shared scrollbar.");
                table.CurrentCell = null;
                table.Rows[3].Visible = false;
                scroll.Bar.MoveTo(6);
                check(table.FirstDisplayedScrollingRowIndex == 7 && scroll.Bar.Value == 6,
                    "Dragging updates the scrollbar and table synchronously after hiding a row.");
                table.Rows[3].Visible = true;
                scroll.ScrollTo(0);
                TestMiddleScroll(scroll, table, check);
                scroll.ScrollTo(24);
                check(table.Rows[24].Displayed && Find<Button>(dialog, "ConfirmPendingInstall").Visible, "Scrolling reaches the last mod while actions stay visible.");
                using (var image = new Bitmap(dialog.Width, dialog.Height)) {
                    dialog.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                    image.Save(Path.Combine(root, "pending-downloads.png"));
                }
                Find<Button>(dialog, "CancelPendingInstall").PerformClick();
                check(dialog.DialogResult == DialogResult.Cancel, "Cancel leaves the installation unconfirmed.");
            }
            using (var dialog = new GeneralInstallConfirmation(Path.Combine(root, "game"), 2, new string[0])) {
                Open(dialog);
                check(!Descendants(dialog).OfType<DataGridView>().Any(), "Already downloaded files can be installed without an empty download table.");
                check(!Descendants(dialog).OfType<CheckBox>().Any(), "Installation confirmation has no separate adult-content opt-in.");
                Find<Button>(dialog, "ConfirmPendingInstall").PerformClick();
                check(dialog.DialogResult == DialogResult.OK, "Ready-to-install files can be confirmed.");
            }
            using (var dialog = new GeneralModAlternativeDialog(new[] {
                new GeneralModEntry { Id = "regular", Name = "Regular", Description = "Less projected" },
                new GeneralModEntry { Id = "push", Name = "Push-Up", Description = "Stronger effect" }
            })) {
                Open(dialog);
                var options = Find<GeneralPicker>(dialog, "AlternativeChoices");
                var use = Find<Button>(dialog, "UseAlternative");
                check(options.Items.Count == 2 && !use.Enabled &&
                    Find<TextBox>(dialog, "AlternativeDetails").Text.Contains("Stronger effect"),
                    "Alternative selection explains both options and requires an explicit choice.");
                options.SelectedIndex = 1;
                use.PerformClick();
                check(dialog.DialogResult == DialogResult.OK && dialog.SelectedId == "push", "The alternative dialog returns the chosen mod.");
            }
            var original = new HashSet<string>(new[] { "A" }, StringComparer.Ordinal);
            using (var dialog = new GeneralColumnFilterDialog("Filter", new[] { "B", "A", "", "A" }, original, "old", true)) {
                Open(dialog); var list = Find<CheckedListBox>(dialog, "ColumnChoices");
                check(list.Items.Count == 3 && list.CheckedIndices.Cast<int>().SequenceEqual(new[] { 1 }), "Filter deduplicates values and copies the initial selection.");
                check(Descendants(dialog).OfType<GeneralScrollView>().Any(view => Object.ReferenceEquals(view.Content, list)), "The filter list uses the shared custom scroll view.");
                Find<Button>(dialog, "SelectAllValues").PerformClick();
                check(list.CheckedItems.Count == 3 && original.SetEquals(new[] { "A" }), "Select all does not mutate the supplied selection.");
                Find<Button>(dialog, "ClearValues").PerformClick();
                check(list.CheckedItems.Count == 0, "Clear only changes the dialog draft.");
                list.SetItemChecked(0, true); Find<TextBox>(dialog, "ColumnSearch").Text = "  needle  "; Find<Button>(dialog, "ApplyColumnFilter").PerformClick();
                check(dialog.DialogResult == DialogResult.OK && dialog.ResultValues.SetEquals(new[] { "B" }) && dialog.SearchText == "needle" && original.SetEquals(new[] { "A" }), "Apply returns independent values and search without mutating the caller.");
            }
            using (var dialog = new GeneralColumnFilterDialog("Filter", new[] { "A", "B" })) {
                Open(dialog); check(Find<CheckedListBox>(dialog, "ColumnChoices").CheckedItems.Count == 2, "A null initial selection means all values.");
                Find<Button>(dialog, "ClearValues").PerformClick(); Find<Button>(dialog, "CancelColumnFilter").PerformClick();
                check(dialog.ResultValues == null && dialog.SearchText == null, "Cancel leaves filter outputs unpublished.");
            }
            using (var dialog = new GeneralColumnFilterDialog("Filter", new string[0], new string[0])) {
                Open(dialog); Find<Button>(dialog, "ApplyColumnFilter").PerformClick();
                check(dialog.ResultValues.Count == 0, "An empty choice list can be confirmed safely.");
            }
            foreach (string id in new[] { "new", "update" }) using (var dialog = new GeneralPresetActionsDialog("Friends")) {
                Open(dialog); Find<Button>(dialog, id == "new" ? "NewPreset" : "UpdatePreset").PerformClick();
                check(dialog.ActionId == id && dialog.DialogResult == DialogResult.OK, "Preset action returns the explicit operation: " + id);
            }
            using (var dialog = new GeneralPresetActionsDialog("Friends")) {
                Open(dialog); Find<Button>(dialog, "CancelPresetActions").PerformClick();
                check(dialog.ActionId == null && dialog.DialogResult == DialogResult.Cancel, "Cancel does not select a preset action.");
            }
            var projects=new[]{new GeneralProject{Id=Guid.NewGuid().ToString("N"),Name="Meu primeiro projeto"},new GeneralProject{Id=Guid.NewGuid().ToString("N"),Name="Outro projeto"}};
            foreach(string theme in new[]{"light","dark","light-red"})foreach(string language in new[]{"pt","en"}){
                GeneralStyle.UseTheme(theme);GeneralStyle.Language=language;
                using(var menu=new GeneralActionsMenu()){
                    Open(menu);var buttons=Descendants(menu).OfType<Button>().Where(button=>button.Name!="").ToArray();
                    check(buttons.Length==8&&buttons.Select(button=>button.Name).OrderBy(name=>name).SequenceEqual(new[]{"clone","export","import","manual","new","open","options","share"}),"List menu includes manual installation capture: "+theme+"/"+language);
                    check(Find<Label>(menu,"ProjectSection").Text==(language=="pt"?"Projetos":"Projects"),"List section heading is translated: "+language);
                    var create=Find<Button>(menu,"new");var open=Find<Button>(menu,"open");var sequence=new[]{"new","open","clone","manual","export","import","share","options"}.Select(id=>Find<Button>(menu,id)).ToArray();
                    check(((TableLayoutPanel)create.Parent).ColumnCount==1&&sequence.Select(button=>button.Left).Distinct().Count()==1&&sequence.Zip(sequence.Skip(1),(a,b)=>a.Bottom<=b.Top).All(value=>value),"List actions form one aligned vertical column: "+theme+"/"+language);
                    check(buttons.All(button=>button.Height==44&&button.Width==create.Width&&button.Bottom<=button.Parent.ClientSize.Height),"Buttons have matching sizes and remain inside the popup: "+theme+"/"+language+" "+String.Join(";",buttons.Select(button=>button.Name+"="+button.Bounds+" parent="+button.Parent.ClientSize)));
                    check(buttons.Cast<GeneralButton>().All(button=>!String.IsNullOrEmpty(button.IconKind)&&button.TextAlign==ContentAlignment.MiddleLeft&&!button.Destructive),"Every menu row has an icon and left aligned text: "+theme+"/"+language);
                    var divider=Find<Panel>(menu,"MenuDivider");int lineY=divider.Top+divider.Height/2;
                    check(lineY-Find<Button>(menu,"import").Bottom==Find<Button>(menu,"share").Top-lineY,"Section divider is centered between the painted action bounds: "+theme+"/"+language);
                    check(create.Text==(language=="pt"?"Novo projeto":"New project")&&open.Text==(language=="pt"?"Abrir projeto":"Open project"),"New/open labels match the selected language: "+language);
                    check(Descendants(menu).OfType<Button>().Single(button=>button.AccessibleName==(language=="pt"?"Fechar":"Close")).Visible==false,"The menu does not display an X close button: "+theme+"/"+language);
                    using(var bitmap=new Bitmap(menu.Width,menu.Height)){menu.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(root,"list-menu-"+language+"-"+theme+".png"),System.Drawing.Imaging.ImageFormat.Png);}
                    menu.Close();
                }

            }
            using(var review=new ManualInstallReview(new[]{
                new ManualInstallCapture.FileState{Path="mods/emoji/init.lua",Added=true},
                new ManualInstallCapture.FileState{Path="mods/emoji/fotos/photo.png",Added=true}
            })){
                Open(review);
                var files=Descendants(review).OfType<CheckedListBox>().Single();
                check(review.Selected.Length==0&&files.CheckedItems.Count==0,"Manual capture starts with all files unchecked.");
                files.SetItemChecked(0,true);
                check(review.Selected.Single().Path=="mods/emoji/init.lua","Manual capture returns only the explicitly checked files.");
                review.Close();
            }
            foreach(string id in new[]{"new","open","clone","manual","export","import","share","options"})using(var menu=new GeneralActionsMenu()){
                Open(menu);Find<Button>(menu,id).PerformClick();check(menu.ActionId==id&&menu.DialogResult==DialogResult.OK,"List action returns its explicit identifier: "+id);
            }
            foreach(string theme in new[]{"light","dark","light-red"})foreach(string language in new[]{"pt","en"}){
                GeneralStyle.UseTheme(theme);GeneralStyle.Language=language;using(var menu=new GeneralActionsMenu(true)){Open(menu);
                var buttons=Descendants(menu).OfType<Button>().Where(button=>button.Name!="").ToArray();
                check(buttons.Select(button=>button.Name).OrderBy(id=>id).SequenceEqual(new[]{"clone","manual","new","open","options","share"}),"Built-in menu offers manual capture and modpack sharing: "+theme+"/"+language);
                check(buttons.All(button=>button.Height==44&&button.Bottom<=button.Parent.Height),"Built-in menu actions fit without clipping: "+theme+"/"+language);
                using(var bitmap=new Bitmap(menu.Width,menu.Height)){menu.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(root,"builtin-menu-"+language+"-"+theme+".png"));}
            }

            }
        } finally { GeneralStyle.AnimationsEnabled = animations; GeneralStyle.Dark=originalDark;GeneralStyle.RedTheme=originalRed;GeneralStyle.Language=originalLanguage; }
        Safe.WriteJson(Path.Combine(root, "resultado-table-dialogs.json"), new { Passed = true, Tests = checks, RealGameTouched = false, NetworkUsed = false });
        return checks.Count;
    }
    static void TestMiddleScroll(GeneralScrollView scroll, DataGridView table, Action<bool, string> check) {
        const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        Cursor original = scroll.Cursor;
        var middle = Message.Create(table.Handle, 0x0207, IntPtr.Zero, IntPtr.Zero);
        check(Application.FilterMessage(ref middle) && scroll.Cursor == Cursors.NoMoveVert,
            "Middle click on the table activates continuous scrolling.");
        var filter = typeof(GeneralScrollView).GetField("middleScroll", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        var anchor = filter.GetType().GetField("anchor", privateInstance);
        var tick = filter.GetType().GetMethod("Scroll", privateInstance);
        var cursor = Cursor.Position;
        anchor.SetValue(filter, new Point(cursor.X, cursor.Y - 200));
        tick.Invoke(filter, null);
        tick.Invoke(filter, null);
        int after = table.FirstDisplayedScrollingRowIndex;
        check(after > 0, "The middle scroll advances repeatedly while the pointer remains below the anchor.");
        anchor.SetValue(filter, new Point(cursor.X, cursor.Y + 200));
        tick.Invoke(filter, null);
        tick.Invoke(filter, null);
        check(table.FirstDisplayedScrollingRowIndex < after, "Moving above the anchor reverses continuous scrolling.");
        var escape = Message.Create(table.Handle, 0x0100, (IntPtr)Keys.Escape, IntPtr.Zero);
        check(Application.FilterMessage(ref escape) && scroll.Cursor == original, "Escape stops the middle scroll and restores the cursor.");
    }

    static void Open(Form dialog) { dialog.ShowInTaskbar = false; dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new Point(-30000, -30000); dialog.Show(); Application.DoEvents(); }
    static T Find<T>(Control parent, string name) where T : Control { return Descendants(parent).OfType<T>().Single(control => control.Name == name); }
    static IEnumerable<Control> Descendants(Control parent) { foreach (Control child in parent.Controls) { yield return child; foreach (Control next in Descendants(child)) yield return next; } }
}
