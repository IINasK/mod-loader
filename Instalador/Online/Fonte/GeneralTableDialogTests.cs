using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

internal static class GeneralTableDialogTests {
    static void AuditLayouts(string root,Action<bool,string> check){
        var mods=new[]{new GeneralModEntry{Id="one",Name="Mod de teste",Variants=new List<GeneralModVariant>{
            new GeneralModVariant{Id="main",Name="Principal",Sources=new List<string>{"https://example.test/example.zip"}}
        },DefaultVariant="main"}};
        var preset=new GeneralModPreset{Id="kit",Name="Com DLC Phantom Liberty",ModIds=new List<string>{"one"}};
        Directory.CreateDirectory(Path.Combine(root,".CoopModsOnline","backups"));
        var examples=new Dictionary<string,Func<GeneralDialog>>{
            {"name",()=>new GeneralNameDialog("Preset",preset.Name)},
            {"folder",()=>new GeneralNameDialog("Criar pasta",preset.Name,chooseSelectionMode:true)},
            {"described-name",()=>new GeneralNameDialog("Projeto","Nome do projeto","A cópia usa a mesma pasta do jogo.")},
            {"filter",()=>new GeneralColumnFilterDialog("Mods",new[]{"Principal","Opcional"},withSearch:true)},
            {"install",()=>new GeneralInstallConfirmation(new[]{new GeneralInstallConfirmation.Choice("Mod de teste",true)})},
            {"variants",()=>new GeneralModAlternativeDialog(mods)},
            {"version-conflict",()=>new GeneralVersionChoiceDialog(new ModVersionDifference{Name=mods[0].Name,Current="1.0",Incoming="2.0"})},
            {"category",()=>new GeneralCategoryDetails("Jogabilidade","Descrição",canDelete:true)},
            {"category-remove",()=>new GeneralCategoryRemovalDialog("Jogabilidade",3)},
            {"categories",()=>new GeneralCategoryEditor(new[]{"Jogabilidade","Núcleo"})},
            {"preset",()=>new GeneralPresetEditor(preset,mods,preset.ModIds)},
            {"options-home",()=>new GeneralOptionsDialog("dark","pt",root)},
            {"options-project",()=>new GeneralOptionsDialog("dark","pt",root,()=>null,root,_=>null)},
            {"nexus",()=>new GeneralNexusConnectionDialog(_=>{},_=>System.Threading.Tasks.Task.FromResult(NexusKeyCheck.Accepted))},
            {"nexus-trust",()=>new NexusKeyTrustDialog()},
            {"dependencies",()=>new GeneralDependencyReview(new[]{new KeyValuePair<string,string>("Auditoria","Concluída")},new GeneralDependencyIssue[0],_=>{})},
            {"manual-capture",()=>new ManualInstallReview(new ManualInstallCapture.FileState[0])},
            {"project-create",()=>new GeneralProjectCreationDialog()},
            {"project-edit",()=>new GeneralProjectCreationDialog("Projeto de teste",null,root,editing:true)},
            {"project-delete",()=>new GeneralProjectDeleteChoice("Projeto de teste")},
            {"project-delete-review",()=>new GeneralProjectDeleteReview("Projeto de teste",null)},
            {"sync-review",()=>new GeneralSyncDifferencesDialog(new P2PSyncDifference[0],true)}
        };
        foreach(string theme in new[]{"light","dark","black","light-red"})foreach(string language in new[]{"pt","en"}){
            GeneralStyle.UseTheme(theme);GeneralStyle.Language=language;
            foreach(var example in examples)using(var dialog=example.Value()){
                Open(dialog);
                var actions=Descendants(dialog).OfType<FlowLayoutPanel>().Single(panel=>panel.Name=="DialogActions");
                var buttons=actions.Controls.OfType<Button>().Where(button=>button.Visible).OrderBy(button=>button.Left).ToArray();
                string label=example.Key+"/"+theme+"/"+language;
                check(buttons.Length!=0&&buttons.All(button=>actions.ClientRectangle.Contains(button.Bounds)&&button.Height>=44)&&
                    buttons.Zip(buttons.Skip(1),(left,right)=>left.Right<=right.Left).All(fits=>fits),
                    label+" keeps the full action targets inside the row. "+actions.ClientSize+" "+String.Join(" ",buttons.Select(button=>button.Bounds)));
                var table=(TableLayoutPanel)actions.Parent;
                int row=table.GetPositionFromControl(actions).Row;
                var previous=row>0?table.GetControlFromPosition(0,row-1):null;
                if(previous!=null&&previous.Visible)
                    check(actions.Top-previous.Bottom>=12,label+" keeps a gap above the actions.");
                if(example.Key=="options-project"){
                    var tools=Descendants(dialog).OfType<FlowLayoutPanel>().Single(panel=>panel.Name=="OptionsMaintenance");
                    var toolButtons=tools.Controls.OfType<Button>().ToArray();
                    check(toolButtons.Length==3&&toolButtons.All(button=>tools.ClientRectangle.Contains(button.Bounds))&&
                        toolButtons.Select(button=>button.Top).Distinct().Count()==1,label+" fits the three maintenance commands in one row.");
                }
                if(example.Key=="install"){
                    var modsTable=Find<DataGridView>(dialog,"InstallChoices");
                    check(modsTable.GetRowDisplayRectangle(0,true).Height>=modsTable.RowTemplate.Height,
                        label+" keeps the single mod row fully visible.");
                }
                if(example.Key=="name"){
                    var field=Find<TextBox>(dialog,"NameInput").Parent;
                    check(Math.Abs(field.Right-actions.Left-buttons.Last().Right)<=1&&field.Height>=44,
                        label+" aligns the field and its primary action. "+field.Bounds+" "+actions.Bounds+" "+buttons.Last().Bounds);
                    var corner=dialog.PointToScreen(new Point(dialog.ClientSize.Width-8,dialog.ClientSize.Height-8));
                    var hit=Message.Create(dialog.Handle,0x84,IntPtr.Zero,new IntPtr((corner.X&0xffff)|((corner.Y&0xffff)<<16)));
                    hit.Result=(IntPtr)1;GeneralPopupPlacement.ResizeHit(dialog,ref hit);
                    check(hit.Result==(IntPtr)17,label+" exposes the diagonal resize area at the corner.");
                }
                if(theme=="dark"&&language=="pt")using(var capture=new Bitmap(dialog.Width,dialog.Height)){
                    dialog.DrawToBitmap(capture,new Rectangle(Point.Empty,capture.Size));
                    capture.Save(Path.Combine(root,"layout-"+example.Key+".png"));
                }
            }
        }
        GeneralStyle.UseTheme("light");GeneralStyle.Language="pt";
    }

    internal static int Run(string parent) {
        string root = OnlineTests.NestedRoot(parent,"table-dialogs-"); Directory.CreateDirectory(root);
        var checks = new List<string>(); Action<bool, string> check = (ok, message) => { if (!ok) throw new IOException(message); checks.Add(message); };
        bool animations = GeneralStyle.AnimationsEnabled, originalDark = GeneralStyle.Dark, originalRed = GeneralStyle.RedTheme; string originalLanguage = GeneralStyle.Language; GeneralStyle.AnimationsEnabled = false;
        try {
            AuditLayouts(root,check);
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
            using (var dialog = new GeneralInstallConfirmation(pending.Select(name=>new GeneralInstallConfirmation.Choice(name,true)).ToArray())) {
                Open(dialog);
                var table = Find<DataGridView>(dialog, "InstallChoices");
                var scroll = Descendants(dialog).OfType<GeneralScrollView>().Single();
                scroll.RefreshScroll();
                check(table.Rows.Count==25&&dialog.SelectedIndices.Length==25&&
                    table.Rows.Cast<DataGridViewRow>().Select(row=>(string)row.Cells[1].Value).SequenceEqual(pending),
                    "Install confirmation starts with every queued mod checked and the original names only.");
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
            using(var dialog=new GeneralInstallConfirmation(new[]{
                new GeneralInstallConfirmation.Choice("Base",false),
                new GeneralInstallConfirmation.Choice("Dependent",true,0),
                new GeneralInstallConfirmation.Choice("Independent",false)
            })){
                Open(dialog);
                var table=Find<DataGridView>(dialog,"InstallChoices");
                var all=Find<CheckBox>(dialog,"ToggleInstallSelection");
                var proceed=Find<Button>(dialog,"ConfirmPendingInstall");
                var summary=Find<Label>(dialog,"InstallSummary");
                const BindingFlags events=BindingFlags.Instance|BindingFlags.NonPublic;
                var click=typeof(CheckBox).GetMethod("OnClick",events);
                var rowClick=typeof(DataGridView).GetMethod("OnCellClick",events);
                check(dialog.SelectedIndices.SequenceEqual(new[]{0,1,2})&&all.Checked&&all.Text=="Desselecionar tudo"&&summary.Text.Contains("1 com download pendente"),
                    "The review includes both ready and pending items without the installation path.");
                click.Invoke(all,new object[]{EventArgs.Empty});
                check(!proceed.Enabled&&dialog.SelectedIndices.Length==0&&all.Text=="Selecionar tudo",
                    "Deselect all clears the queue and disables Continue.");
                click.Invoke(all,new object[]{EventArgs.Empty});
                check(dialog.SelectedIndices.Length==3&&all.Text=="Desselecionar tudo","Select all restores every checkbox.");
                rowClick.Invoke(table,new object[]{new DataGridViewCellEventArgs(0,0)});
                check(dialog.SelectedIndices.SequenceEqual(new[]{2})&&all.CheckState==CheckState.Indeterminate&&summary.Text.Contains("0 com download pendente"),
                    "Unchecking a prerequisite unchecks its dependent and updates the download count.");
                rowClick.Invoke(table,new object[]{new DataGridViewCellEventArgs(1,1)});
                check(dialog.SelectedIndices.Length==3,"Clicking a mod name selects that mod and its required prerequisite.");
                rowClick.Invoke(table,new object[]{new DataGridViewCellEventArgs(0,2)});
                proceed.PerformClick();
                check(dialog.DialogResult==DialogResult.OK&&dialog.SelectedIndices.SequenceEqual(new[]{0,1}),
                    "Continue returns just the checked rows in queue order.");
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
                    Open(menu);var buttons=Descendants(menu.Content).OfType<Button>().Where(button=>button.Name!="").ToArray();
                    check(buttons.Length==8&&buttons.Select(button=>button.Name).OrderBy(name=>name).SequenceEqual(new[]{"details","export","import","new","open","options","save-menu","share"}),"List menu exposes the current project actions: "+theme+"/"+language);
                    check(Find<Label>(menu,"NavigationSection").Text==(language=="pt"?"Navegação":"Navigation")&&
                        Find<Label>(menu,"ProjectSection").Visible&&Find<Label>(menu,"ToolsSection").Visible,"List section heading is translated: "+language);
                    var create=Find<Button>(menu,"new");var open=Find<Button>(menu,"open");var sequence=new[]{"new","open","save-menu","details","import","export","share","options"}.Select(id=>Find<Button>(menu,id)).ToArray();
                    check(((TableLayoutPanel)create.Parent).ColumnCount==1&&sequence.Select(button=>button.Left).Distinct().Count()==1&&sequence.Zip(sequence.Skip(1),(a,b)=>a.Bottom<=b.Top).All(value=>value),"List actions form one aligned vertical column: "+theme+"/"+language);
                    check(buttons.All(button=>button.Height==44&&button.Width==create.Width&&button.Bottom<=button.Parent.ClientSize.Height),"Buttons have matching sizes and remain inside the popup: "+theme+"/"+language+" "+String.Join(";",buttons.Select(button=>button.Name+"="+button.Bounds+" parent="+button.Parent.ClientSize)));
                    check(buttons.Cast<GeneralButton>().All(button=>!String.IsNullOrEmpty(button.IconKind)&&button.TextAlign==ContentAlignment.MiddleLeft&&!button.Destructive),"Every menu row has an icon and left aligned text: "+theme+"/"+language);
                    var projectSection=Find<Label>(menu,"ProjectSection");var toolsSection=Find<Label>(menu,"ToolsSection");
                    check(Find<Button>(menu,"save-menu").Bottom<=projectSection.Top&&Find<Button>(menu,"export").Bottom<=toolsSection.Top,
                        "The sections separate navigation, project and tools: "+theme+"/"+language);
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
            foreach(string id in new[]{"new","open","save-menu","details","import","export","share","options"})using(var menu=new GeneralActionsMenu()){
                Open(menu);Find<Button>(menu,id).PerformClick();check(menu.ActionId==id&&menu.DialogResult==DialogResult.OK,"List action returns its explicit identifier: "+id);
            }
            foreach(string theme in new[]{"light","dark","light-red"})foreach(string language in new[]{"pt","en"}){
                GeneralStyle.UseTheme(theme);GeneralStyle.Language=language;using(var menu=new GeneralActionsMenu(true)){Open(menu);
                var buttons=Descendants(menu.Content).OfType<Button>().Where(button=>button.Name!="").ToArray();
                check(buttons.Select(button=>button.Name).OrderBy(id=>id).SequenceEqual(new[]{"details","new","open","options","save-menu","share"}),"Built-in menu offers project actions and modpack sharing: "+theme+"/"+language);
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
