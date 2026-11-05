using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

internal static class GeneralTableDialogTests {
    internal static int Run(string parent) {
        string root = Path.Combine(Path.GetFullPath(parent), "table-dialogs-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var checks = new List<string>(); Action<bool, string> check = (ok, message) => { if (!ok) throw new IOException(message); checks.Add(message); };
        bool animations = GeneralStyle.AnimationsEnabled, originalDark = GeneralStyle.Dark; string originalLanguage = GeneralStyle.Language; GeneralStyle.AnimationsEnabled = false;
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
            foreach(bool dark in new[]{false,true})foreach(string language in new[]{"pt","en"}){
                GeneralStyle.Dark=dark;GeneralStyle.Language=language;
                using(var menu=new GeneralActionsMenu()){
                    Open(menu);var buttons=Descendants(menu).OfType<Button>().Where(button=>button.Name!="").ToArray();
                    check(buttons.Length==4&&buttons.Select(button=>button.Name).OrderBy(name=>name).SequenceEqual(new[]{"delete-project","export","import","new"}),"Project menu contains exactly the four project actions: "+dark+"/"+language);
                    check(Find<Label>(menu,"ProjectSection").Text==(language=="pt"?"Projeto":"Project"),"Project section heading is translated: "+language);
                    var create=Find<Button>(menu,"new");var open=Find<Button>(menu,"import");var export=Find<Button>(menu,"export");var delete=Find<GeneralButton>(menu,"delete-project");
                    check(create.Top==open.Top&&export.Top==delete.Top&&create.Left==export.Left&&open.Left==delete.Left&&export.Top>create.Bottom,"Project actions use aligned two-column rows: "+dark+"/"+language);
                    check(buttons.All(button=>button.Height==create.Height&&Math.Abs(button.Width-create.Width)<=1)&&buttons.All(button=>button.Bottom<=button.Parent.ClientSize.Height),"Buttons have matching sizes and remain inside the popup: "+dark+"/"+language);
                    check(delete.Destructive&&!((GeneralButton)create).Destructive,"Only delete project is styled as destructive: "+dark+"/"+language);
                    check(create.Text==(language=="pt"?"Novo projeto":"New project")&&open.Text==(language=="pt"?"Abrir projeto":"Open project"),"New/open labels match the selected language: "+language);
                    using(var bitmap=new Bitmap(menu.Width,menu.Height)){menu.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(root,"project-menu-"+language+"-"+(dark?"dark":"light")+".png"),System.Drawing.Imaging.ImageFormat.Png);}
                    menu.Close();
                }
            }
            foreach(string id in new[]{"new","import","export","delete-project"})using(var menu=new GeneralActionsMenu()){
                Open(menu);Find<Button>(menu,id).PerformClick();check(menu.ActionId==id&&menu.DialogResult==DialogResult.OK,"Project action returns its explicit identifier: "+id);
            }
        } finally { GeneralStyle.AnimationsEnabled = animations; GeneralStyle.Dark=originalDark;GeneralStyle.Language=originalLanguage; }
        Safe.WriteJson(Path.Combine(root, "resultado-table-dialogs.json"), new { Passed = true, Tests = checks, RealGameTouched = false, NetworkUsed = false });
        return checks.Count;
    }
    static void Open(Form dialog) { dialog.ShowInTaskbar = false; dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new Point(-30000, -30000); dialog.Show(); Application.DoEvents(); }
    static T Find<T>(Control parent, string name) where T : Control { return Descendants(parent).OfType<T>().Single(control => control.Name == name); }
    static IEnumerable<Control> Descendants(Control parent) { foreach (Control child in parent.Controls) { yield return child; foreach (Control next in Descendants(child)) yield return next; } }
}
