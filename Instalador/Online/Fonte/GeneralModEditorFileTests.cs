using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class GeneralModEditorFileTests {
    internal static int RunFiles(string parent) {
        string root=Path.Combine(Path.GetFullPath(parent),"online-testes-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        string game=Directory.CreateDirectory(Safe.In(root,"game")).FullName;
        string folder=Directory.CreateDirectory(Safe.In(root,"original-folder")).FullName;
        Directory.CreateDirectory(Safe.In(folder,"mods/one"));Directory.CreateDirectory(Safe.In(folder,"mods/two"));
        File.WriteAllText(Safe.In(folder,"mods/one/first.txt"),"one");File.WriteAllText(Safe.In(folder,"mods/two/second.txt"),"two");
        string archive=Zip(root,"source.zip",new Dictionary<string,string>{{"mods/one/first.txt","one"},{"mods/two/second.txt","two"}});
        string second=Zip(root,"second.zip",new Dictionary<string,string>{{"mods/other.txt","other"}});
        var checks=new List<string>();Action<bool,string> check=(ok,message)=>{if(!ok)throw new IOException(message);checks.Add(message);};
        bool animate=GeneralStyle.AnimationsEnabled,dark=GeneralStyle.Dark,red=GeneralStyle.RedTheme,black=GeneralStyle.BlackTheme;
        GeneralStyle.AnimationsEnabled=false;GeneralStyle.UseTheme("dark");
        try {
            using(var owner=new Form{ClientSize=new Size(1000,780)}) {
                owner.Show();
                var entry=Entry("");entry.Variants[0].OutputFolders.Add("mods/preserved");
                var presets=new[]{new GeneralModPreset{Id="first",Name="Primeiro"},new GeneralModPreset{Id="second",Name="Segundo"}};
                GeneralPicker detachedPicker;
                using(var editor=new GeneralModEditor(entry,new string[0],"default",true,"",alternatives:new GeneralModEntry[0],installationFolder:game)) {
                    editor.ConfigurePresets(presets,"first",entry.Id,false);editor.Show(owner);Pump();
                    var source=Field<TextBox>(editor,"sources");var picker=Field<GeneralPicker>(editor,"variants");detachedPicker=picker;
                    var drop=All(editor).OfType<GeneralPackageDrop>().Single();var destinations=Field<GeneralArchiveDestinations>(editor,"archiveDestinations");
                    var preset=All(editor).Single(control=>control.Name=="ModPresetMembership");
                    check(!picker.Visible&&picker.Parent==null&&picker.Text=="Principal",
                        "The version picker is absent while the selected variant model is preserved.");
                    check(preset.Visible&&preset.RectangleToScreen(preset.ClientRectangle).Bottom<source.RectangleToScreen(source.ClientRectangle).Top,
                        "Preset membership is the first visible selector above the source field.");
                    check(drop.Visible&&drop.Enabled&&!destinations.Visible&&drop.Height>=200,
                        "An empty new item presents the large dashed package drop area and hides unavailable package details.");
                    check(!All(editor).Any(control=>control.Name=="DeclaredOutputFolders"||control.Name=="OpenVariantFiles"),
                        "The standalone declared-output field and open-folder button have been removed.");
                    Capture(editor,Safe.In(root,"editor-empty-top-dark.png"));
                    Reveal(editor,drop);Capture(editor,Safe.In(root,"editor-empty-drop-dark.png"));
                    var accepted=new DataObject(DataFormats.FileDrop,new[]{folder});
                    check(GeneralPackageDrop.PackagePath(accepted)==folder&&GeneralPackageDrop.PackagePath(new DataObject(DataFormats.FileDrop,new[]{archive,second}))==null,
                        "The source drop accepts one original folder or package and rejects ambiguous multiple attachments.");
                    check(editor.AttachPackageSource(folder),"A non-Nexus folder attaches through the generic source adapter.");
                    StopTimer(editor,"archiveDelay");Pump((Task)Call(editor,"LoadArchive"));
                    check(!drop.Visible&&destinations.Visible&&source.Text==GeneralModFolderSource.Normalize(folder)&&Field<TextBox>(editor,"modName").Text=="original-folder",
                        "Attaching a folder replaces the drop area with its canonical source, name and package details.");
                    check(Field<Button>(editor,"save").Enabled&&Field<Button>(editor,"install").Enabled,
                        "A generic folder can be saved or installed without a Nexus account or Nexus URL.");
                    var tree=All(destinations).OfType<ListBox>().Single(control=>control.Name=="ArchiveFileTree");
                    var nodes=Field<Dictionary<string,TreeNode>>(destinations,"nodes");
                    check(nodes.ContainsKey("mods/one/first.txt")&&nodes.ContainsKey("mods/two/second.txt"),
                        "The attached original folder is previewed by relative file paths without an added wrapper folder.");
                    var location=All(destinations).OfType<TextBox>().Single(control=>control.Name=="ArchiveCurrentDestination");
                    check(location.Parent.RectangleToScreen(location.Parent.ClientRectangle).Bottom<tree.RectangleToScreen(tree.ClientRectangle).Top,
                        "Current location is above the package-root tree, directly within file destinations.");
                    Reveal(editor,destinations);Capture(editor,Safe.In(root,"editor-attached-folder-dark.png"));
                    tree.SelectedItem=nodes["mods/one"];
                    tree.ContextMenuStrip.Show(tree,new Point(50,Math.Min(tree.Height-20,tree.GetItemRectangle(tree.SelectedIndex).Bottom)));Pump();
                    var menu=tree.ContextMenuStrip;
                    check(menu.Items.Cast<ToolStripItem>().Any(item=>item.Enabled&&item.Text=="Mudar destino…")&&
                        menu.Items.Cast<ToolStripItem>().Any(item=>item.Text=="Vincular arquivos gerados nesta pasta"),
                        "The tree context menu separates destination changes from generated-output association.");
                    Capture(menu,Safe.In(root,"editor-tree-context-dark.png"));menu.Close();
                    var declare=menu.Items.Cast<ToolStripItem>().Single(item=>item.Text=="Vincular arquivos gerados nesta pasta");declare.PerformClick();
                    var version=Field<GeneralModVariant>(editor,"current");
                    check(version.OutputFolders.Contains("mods/one")&&version.OutputFolders.Contains("mods/preserved")&&version.InstallRoutes.Count==0&&
                        entry.Variants[0].OutputFolders.SequenceEqual(new[]{"mods/preserved"}),
                        "Declaring an output folder updates only the draft's provenance metadata, preserving existing declarations and installation routes.");
                    tree.ContextMenuStrip.Show(tree,new Point(50,20));Pump();
                    menu.Items.Cast<ToolStripItem>().Single(item=>item.Text=="Desvincular pasta de arquivos gerados").PerformClick();menu.Close();
                    check(version.OutputFolders.SequenceEqual(new[]{"mods/preserved"}),"An output declaration can be removed without changing the source or routes.");

                    int prompts=0;destinations.TestRouteConfirmation=(path,target)=>{prompts++;return false;};
                    check(!destinations.MovePackageNodeTo(nodes["mods/one/first.txt"],nodes["mods/two"])&&prompts==1&&
                        nodes["mods/one/first.txt"].Parent==nodes["mods/one"],
                        "Cancelling a file-drop confirmation leaves the destination draft unchanged.");
                    destinations.TestRouteConfirmation=(path,target)=>{prompts++;return true;};
                    check(destinations.MovePackageNodeTo(nodes["mods/one/first.txt"],nodes["mods/two"]),
                        "Dropping a package file into another package folder prepares a confirmed per-file route.");
                    check(nodes["mods/one/first.txt"].Parent==nodes["mods/two"]&&(string)nodes["mods/one/first.txt"].Tag=="mods/one/first.txt",
                        "A confirmed drop visibly reparents the file while retaining its original archive identity.");
                    destinations.ShowPreview(GeneralArchivePreview.Load(null,null,new[]{"mods/one/first.txt","mods/two/second.txt"},false));
                    check(nodes["mods/one/first.txt"].Parent==nodes["mods/two"],"Refreshing the package preview reconstructs the confirmed visual placement from its route drafts.");
                    tree.SelectedItem=nodes["mods/one"];
                    check(nodes["mods/one"].Nodes.Count==0&&location.Text==Path.Combine(game,"mods","one"),
                        "An emptied source folder retains its folder type and its correct current location.");
                    check(destinations.MovePackageNodeTo(nodes["mods/two/second.txt"],nodes["mods/one"]),
                        "An emptied original folder remains a valid drop destination.");
                    All(destinations).OfType<Button>().Single(button=>button.Name=="ResetArchiveDestination").PerformClick();
                    tree.SelectedItem=nodes["mods/one/first.txt"];
                    All(destinations).OfType<Button>().Single(button=>button.Name=="ResetArchiveDestination").PerformClick();
                    check(nodes["mods/one/first.txt"].Parent==nodes["mods/one"],"Restoring an inherited destination also restores the original visual parent.");
                    check(destinations.MovePackageNode(nodes["mods/one/first.txt"],"%GAME%/chosen/new"),"A selected destination outside the package tree is represented as a route draft.");
                    check((string)nodes["mods/one/first.txt"].Tag=="mods/one/first.txt"&&
                        (string)nodes["mods/one/first.txt"].Parent.Tag=="%GAME%/chosen/new"&&
                        tree.Items.Cast<TreeNode>().Any(node=>node.Text=="Destinos escolhidos"),
                        "A virtual destination branch explicitly represents chosen folders without inventing source paths.");
                    destinations.ShowPreview(GeneralArchivePreview.Load(null,null,new[]{"mods/one/first.txt","mods/two/second.txt"},false));
                    check((string)nodes["mods/one/first.txt"].Parent.Tag=="%GAME%/chosen/new","Virtual destination placement also survives a preview rebuild.");
                    tree.SelectedItem=nodes["mods/one/first.txt"];
                    All(destinations).OfType<Button>().Single(button=>button.Name=="ResetArchiveDestination").PerformClick();
                    destinations.MovePackageNodeTo(nodes["mods/one/first.txt"],nodes["mods/two"]);
                    var drafts=Field<Dictionary<string,string>>(destinations,"drafts");
                    check(drafts["mods/one/first.txt"]=="%GAME%/mods/two"&&
                        GeneralInstallRoutes.Map("mods/one/first.txt",GeneralArchiveDestinations.ReadRoutes(game,drafts))=="mods/two/first.txt"&&
                        Directory.GetFiles(game,"*",SearchOption.AllDirectories).Length==0,
                        "The route targets the new folder and remains metadata-only until the editor is saved and the guarded installer acts.");
                    check(!destinations.MovePackageNodeTo(nodes["mods"],nodes["mods/one"]),"A folder cannot be dragged into its own descendant.");
                    editor.TestPackageReplacement=_=>false;
                    check(!editor.AttachPackageSource(second)&&source.Text==GeneralModFolderSource.Normalize(folder),
                        "Cancelling source replacement preserves the original folder reference and pending route edits.");
                    Field<TextBox>(editor,"modName").Text="Novo nome do mod";
                    check(GeneralMessageText.ModNames(editor).Contains("Novo nome do mod"),
                        "Confirmation messages recognize the unsaved mod name for the shared emphasis style.");
                    Field<Button>(editor,"save").PerformClick();
                    check(editor.Result!=null&&editor.SelectedVariant=="default"&&editor.Result.Variants.Single().Sources.Single()==GeneralModFolderSource.Normalize(folder)&&
                        editor.Result.Variants.Single().InstallRoutes.Any(route=>route.Folder=="mods/one/first.txt")&&
                        editor.Result.Variants.Single().OutputFolders.SequenceEqual(new[]{"mods/preserved"})&&editor.PresetIds.SequenceEqual(new[]{"first"}),
                        "Saving preserves variant identity, preset membership, folder source, confirmed file route and provenance declarations.");
                }
                check(detachedPicker.IsDisposed,"Closing the editor disposes the detached legacy variant control.");
                using(var editor=new GeneralModEditor(Entry(""),new string[0],"default",true,"",installationFolder:game)) {
                    editor.Show(owner);Pump();
                    Field<TextBox>(editor,"modName").Text="Generic HTTPS mod";
                    Field<TextBox>(editor,"sources").Text=folder;
                    check(Field<Button>(editor,"save").Enabled&&Field<GeneralModVariant>(editor,"current").Sources[0]==GeneralModFolderSource.Normalize(folder),
                        "Pasting an existing folder path is normalized without requiring the user to add a trailing separator.");
                    Field<TextBox>(editor,"sources").Text="https://example.org/mods/generic.zip";
                    check(Field<Button>(editor,"save").Enabled,"A valid generic HTTPS package source is accepted without being disguised as a Nexus link.");
                    editor.TestPackageReplacement=_=>true;check(editor.AttachPackageSource(archive),"An archive can replace a generic source after confirmation.");
                    StopTimer(editor,"archiveDelay");Pump((Task)Call(editor,"LoadArchive"));
                    check(Field<Button>(editor,"save").Enabled&&Field<TextBox>(editor,"sources").Text==archive,
                        "The attached ZIP is a local source with its own preview and no Nexus authentication requirement.");
                    var destinations=Field<GeneralArchiveDestinations>(editor,"archiveDestinations");
                    var tree=All(destinations).OfType<ListBox>().Single(control=>control.Name=="ArchiveFileTree");
                    destinations.ShowPreview(GeneralArchivePreview.Load(null,null,new[]{"a/same.txt","b/same.txt"},false));
                    var nodes=Field<Dictionary<string,TreeNode>>(destinations,"nodes");int prompted=0;
                    destinations.TestRouteConfirmation=(path,target)=>{prompted++;return true;};
                    check(!destinations.MovePackageNodeTo(nodes["a/same.txt"],nodes["b"])&&prompted==0,
                        "A drop that would collapse two package files onto one destination is rejected before confirmation.");
                    check(GeneralArchiveDestinations.DestinationCollision(new[]{"a","a-b","a/file"})&&
                        !GeneralArchiveDestinations.DestinationCollision(new[]{"a","a-b","elsewhere/file"}),
                        "Destination collisions include a file occupying an ancestor folder even when another sorted name separates the paths.");
                    check(destinations.MovePackageNodeTo(nodes["a"],nodes["b"])&&nodes["a"].Parent==nodes["b"]&&(string)nodes["a"].Tag=="a",
                        "A folder drop visibly moves its entire subtree and retains the original folder identity.");
                    GeneralStyle.UseTheme("light");GeneralStyle.Apply(editor,false);Reveal(editor,destinations);
                    check(Field<TextBox>(editor,"description").BorderStyle==BorderStyle.None,
                        "Applying the common light theme preserves the borderless description input inside its rounded surface.");
                    Capture(editor,Safe.In(root,"editor-archive-light.png"));
                    editor.Close();
                }
                owner.Close();
            }
            check(File.ReadAllText(Safe.In(folder,"mods/one/first.txt"))=="one"&&File.ReadAllText(Safe.In(folder,"mods/two/second.txt"))=="two",
                "Editing and previewing the source leaves the original folder's files untouched.");
            Safe.WriteJson(Safe.In(root,"resultado-editor-files.json"),new{Passed=true,Count=checks.Count,Checks=checks,RealGameTouched=false});
            return checks.Count;
        }finally {GeneralStyle.AnimationsEnabled=animate;GeneralStyle.Dark=dark;GeneralStyle.RedTheme=red;GeneralStyle.BlackTheme=black;}
    }
    static GeneralModEntry Entry(string source) {
        return new GeneralModEntry{Id="editor-fixture",Name="",DefaultVariant="default",Variants=new List<GeneralModVariant>{
            new GeneralModVariant{Id="default",Name="Principal",Sources=new List<string>{source}}}};
    }
    static T Field<T>(object instance,string name)where T:class {
        return (T)instance.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).GetValue(instance);
    }
    static object Call(object instance,string method){return instance.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(instance,null);}
    static IEnumerable<Control> All(Control parent){foreach(Control control in parent.Controls){yield return control;foreach(var child in All(control))yield return child;}}
    static void StopTimer(object instance,string name){Field<System.Windows.Forms.Timer>(instance,name).Stop();}
    static void Pump(Task task=null) {
        var elapsed=Stopwatch.StartNew();
        do{Application.DoEvents();if(task==null||task.IsCompleted)break;if(elapsed.Elapsed>TimeSpan.FromSeconds(20))throw new IOException("A prévia do editor não terminou.");Thread.Sleep(10);}while(true);
        if(task!=null)task.GetAwaiter().GetResult();
    }
    static void Reveal(Form editor,Control target) {
        var scroll=All(editor).OfType<GeneralScrollView>().Single(control=>control.Name=="ModEditorScroll");
        var layout=target.Parent;while(layout.Parent!=scroll&&layout.Parent!=null)layout=layout.Parent;
        var area=scroll.RectangleToClient(target.RectangleToScreen(target.ClientRectangle));
        if(area.Bottom>scroll.ClientSize.Height)scroll.ScrollTo(-layout.Top+area.Bottom-scroll.ClientSize.Height+12);
        else if(area.Top<0)scroll.ScrollTo(Math.Max(0,-layout.Top+area.Top-12));
        Pump();
    }
    static void Capture(Control control,string path){using(var bitmap=new Bitmap(control.Width,control.Height)){control.DrawToBitmap(bitmap,new Rectangle(Point.Empty,control.Size));bitmap.Save(path);}}
    static string Zip(string root,string name,Dictionary<string,string> files) {
        string path=Safe.In(root,name);using(var zip=ZipFile.Open(path,ZipArchiveMode.Create))foreach(var file in files)
            using(var writer=new StreamWriter(zip.CreateEntry(file.Key).Open()))writer.Write(file.Value);return path;
    }
}
