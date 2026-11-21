using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    static void WriteZipEntry(ZipArchive zip,string path,string contents){
        using(var writer=new StreamWriter(zip.CreateEntry(path).Open()))writer.Write(contents);
    }

    static GeneralModEntry DetectedEntryForTest(string name,params string[] paths){
        var entry=NewEntry(name,null);
        entry.DetectedPaths.AddRange(paths);
        entry.Variants[0].Sources.Add("installed://"+entry.Id+"/"+entry.DefaultVariant);
        return entry;
    }

    static void CheckInventoryGroupingAndZipCache(string root){
        const string archive="archive/pc/mod/SharedLabel.archive";
        const string script="r6/scripts/SharedLabel/one.reds";
        const string secondScript="r6/scripts/SharedLabel/nested/two.reds";
        const string otherRoot="r6/tweaks/SharedLabel/other.yaml";
        var candidates=new[]{archive,script,secondScript,otherRoot}.Select(path=>
            new P2PSyncDifference{Kind="found",Path=path,Mod="SharedLabel"}).ToArray();
        if(candidates.Select(InventoryLinkStore.Key).Distinct(InventoryPaths).Count()!=3||
            InventoryLinkStore.Key(candidates[1])!=InventoryLinkStore.Key(candidates[2]))
            throw new IOException("Raw inventory groups must follow actual folders and paths, even when names collide.");

        var links=new InventoryLinkData();
        var saved=InventoryLinkStore.Combine(links,null,"Saved group",null,new[]{archive,script});
        candidates[1].VerifiedZip="matching.zip";
        candidates[1].InventoryGroupId="zip:SharedLabel";
        var grouped=InventoryLinkStore.View(candidates,links,new GeneralModEntry[0]).ToArray();
        if(grouped.Count(item=>InventoryLinkStore.Key(item)==saved.Id)!=2||
            grouped.Single(item=>item.Path==script).VerifiedZip!="matching.zip")
            throw new IOException("ZIP evidence must be preserved without splitting the user-saved group.");
        candidates[1].ProvenOwner="other-owner";
        grouped=InventoryLinkStore.View(candidates,links,new GeneralModEntry[0]).ToArray();
        if(grouped.Count(item=>InventoryLinkStore.Key(item)==saved.Id)!=1||
            grouped.Single(item=>item.Path==script).ProvenOwner!="other-owner")
            throw new IOException("A saved group cannot override proof of a separate mod owner.");

        string game=Directory.CreateDirectory(Safe.In(root,"cache-game")).FullName;
        string downloads=Directory.CreateDirectory(Safe.In(root,"cache-zips")).FullName;
        string installed=Safe.In(game,script);
        Directory.CreateDirectory(Path.GetDirectoryName(installed));
        File.WriteAllText(installed,"old");
        var labels=new Dictionary<string,InventoryZipLabel>(InventoryPaths){
            {script,new InventoryZipLabel{Name="previous",Archive="previous.zip",Hash=Safe.Hash(installed)}}
        };
        InventoryLinkStore.DiscoverZips(game,new[]{downloads},new[]{script},labels,Safe.Hash);
        if(labels[script].Archive!="previous.zip")throw new IOException("A valid ZIP label is reused across scans.");
        File.WriteAllText(installed,"new");
        string currentZip=Safe.In(downloads,"current.zip");
        using(var zip=ZipFile.Open(currentZip,ZipArchiveMode.Create))WriteZipEntry(zip,script,"new");
        InventoryLinkStore.DiscoverZips(game,new[]{downloads},new[]{script},labels,Safe.Hash);
        if(labels[script].Archive!="current.zip"||labels[script].Hash!=Safe.Hash(installed))
            throw new IOException("A stale ZIP label cannot prevent matching a new archive at the same path.");
    }

    internal static void RunInventoryLinkTests(string parent){
        string root=OnlineTests.NestedRoot(parent,"inv-links-");
        Directory.CreateDirectory(root);
        CheckInventoryGroupingAndZipCache(root);
        string game=OnlineTests.Fixture(root,"game"),appData=Safe.In(root,"data");
        const string main="archive/pc/mod/local-drone.archive", input="red4ext/plugins/local-drone-input/input.dll";
        const string third="r6/scripts/local-drone-extra/engine.reds", catalogBase="archive/pc/mod/registered.archive";
        const string catalogExtra="r6/scripts/registered-extra/feature.reds";
        var paths=new[]{main,input,third,catalogBase,catalogExtra};
        foreach(string relative in paths){
            string file=Safe.In(game,relative);
            Directory.CreateDirectory(Path.GetDirectoryName(file));File.WriteAllText(file,"contents");
        }
        var hashes=paths.Select(path=>Safe.Hash(Safe.In(game,path))).ToArray();
        const string knownUrl="https://www.nexusmods.com/cyberpunk2077/mods/9991?tab=files&file_id=2";
        const string localUrl="https://www.nexusmods.com/cyberpunk2077/mods/9990";
        var registered=NewEntry("Registered mod",knownUrl);
        registered.Variants.Add(new GeneralModVariant{Id="unmapped",Name="Local option",
            Sources=new List<string>{"installed://"+registered.Id+"/unmapped"}});
        registered.DetectedPaths.Add(catalogBase);
        var other=NewEntry("Another registered mod","https://example.org/another-mod.zip");
        var catalog=new FixedCatalog{Schema=1,Id="inventory-links-fixture",GameVersion="2.5",GameFileVersion="2.5.0.0",Packages=new List<FixedMod>()};
        var state=new GeneralModState{List=new GeneralModList{Mods=new List<GeneralModEntry>{registered,other}},Language="pt",DownloadFolder=root};
        Action<bool,string> check=(ok,message)=>{if(!ok)throw new IOException(message);};
        var variantRecord=new P2PSyncDifference{Kind="verified-variant",ModId=registered.Id,VariantId="default"};
        var observedFiles=new List<P2PSyncDifference>{variantRecord,new P2PSyncDifference{Kind="found",Path=third}};
        AssociateDeclaredOutputs(observedFiles,new Dictionary<string,string>(InventoryPaths),
            new Dictionary<string,string>{{registered.Id,registered.Name}});
        check(observedFiles.Count==2&&observedFiles.Contains(variantRecord),
            "An inventory metadata record without a file path survives reconciliation with no declarations.");
        AssociateDeclaredOutputs(observedFiles,new Dictionary<string,string>(InventoryPaths){{third,registered.Id}},
            new Dictionary<string,string>{{registered.Id,registered.Name}});
        check(observedFiles.Contains(variantRecord)&&observedFiles.Single(file=>file.Path==third).Kind=="associated",
            "A pathless active-variant record also survives when a declared output is linked.");
        TestForm(catalog,state,appData,root,async form=>{
            form.SetDestination(game);
            await form.Scan();
            var first=form.VisibleGameInventory().Single(file=>file.Path==main);
            var second=form.VisibleGameInventory().Single(file=>file.Path==input);
            string original=InventoryLinkStore.Key(first);
            var target=form.grid.Rows.Cast<DataGridViewRow>().Single(row=>Object.ReferenceEquals(row.Tag,first));
            var registeredRow=form.ModRows().Single(row=>Equals(row.Tag,registered.Id));
            var argsData=new DataObject(InventoryDragFormat,InventoryLinkStore.Key(second));
            Func<DataGridViewRow,DragEventArgs> at=row=>{
                form.grid.FirstDisplayedScrollingRowIndex=row.Index;
                var cell=form.grid.GetCellDisplayRectangle(form.grid.Columns["Name"].Index,row.Index,false);
                var p=form.grid.PointToScreen(new Point(cell.X+10,cell.Y+Math.Max(1,cell.Height/2)));
                return new DragEventArgs(argsData,0,p.X,p.Y,DragDropEffects.Move,DragDropEffects.None);
            };
            form.inventoryDragKey=InventoryLinkStore.Key(second);
            check(form.InventoryDropEffect(form.grid,at(registeredRow))==DragDropEffects.Move,"A registered mod accepts a manual inventory link.");
            var drop=at(target);
            check(form.InventoryDropEffect(form.grid,drop)==DragDropEffects.Move,"Two unidentified rows accept a move.");
            typeof(Control).GetMethod("OnDragDrop",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form.grid,new object[]{drop});
            form.inventoryDragKey=null;
            var store=new InventoryLinkStore(appData,game);
            var saved=store.Load().Groups.Single();
            check(saved.Paths.Count==2&&saved.Paths.Contains(main)&&saved.Paths.Contains(input)&&
                form.VisibleGameInventory().Where(file=>file.Path==main||file.Path==input).Select(InventoryLinkStore.Key).Distinct().Count()==1,
                "Dropping onto an unidentified row groups both destinations and persists the result.");
            check(!form.VisibleGameInventory().Any(file=>InventoryLinkStore.Key(file)==original),
                "The drop replaces the old visual identities.");
            check(new InventoryLinkStore(appData,root).Load().Groups.Count==0,"A different installation folder has its own grouping.");
            check(saved.Linked.Count==1&&saved.Linked[0].Name=="local-drone-input"&&saved.Linked[0].Paths.SequenceEqual(new[]{input}),
                "The merge records the original name and destinations of the dragged item.");
            form.UpdateInventoryLinks(links=>links.Groups.Single().Linked.Clear());
            var groupItem=form.VisibleGameInventory().First(file=>file.InventoryGroupId==saved.Id);
            string originalGroup=Safe.Json.Serialize(store.Load().Groups.Single());
            int initialModCount=form.state.List.Mods.Count;
            foreach(bool confirm in new[]{false,true}){
                bool sawMember=false,removedMember=false,staged=false,linkOnly=false;
                using(var timer=new Timer{Interval=45}){
                    timer.Tick+=(s,e)=>{
                        var editor=Application.OpenForms.OfType<GeneralModEditor>().FirstOrDefault(item=>item.Visible);
                        if(editor==null)return;
                        timer.Stop();
                        try{
                            var linked=editor.Controls.Find("LinkedInventory",true).Single();
                            var path=linked.Controls.Find("LinkedInventoryPath",true).Single();
                            var unlink=(Button)linked.Controls.Find("UnlinkInventoryMember",true).Single();
                            sawMember=path.Text=="red4ext/plugins/local-drone-input"&&unlink.AccessibleName.Contains("local-drone-input");
                            var scroller=AllControls(editor).OfType<GeneralScrollView>().First(view=>view.Content.Contains(linked));
                            scroller.ScrollTo(scroller.Bar.Limit);
                            if(!confirm)CaptureUi(editor,Safe.In(root,"linked-inventory.png"));
                            unlink.PerformClick();
                            removedMember=!linked.Visible;
                            staged=store.Load().Groups.Count==1&&Safe.Json.Serialize(store.Load().Groups.Single())==originalGroup;
                            var save=(Button)editor.AcceptButton;
                            linkOnly=save.Enabled&&save.Text=="Salvar";
                            if(confirm)save.PerformClick();
                            else editor.CancelButton.PerformClick();
                        }finally{if(editor.Visible)editor.Close();}
                    };
                    var entry=form.DetectedInventoryEntry(groupItem,form.InventoryRow(saved.Id));
                    timer.Start();
                    var edit=form.EditMod(entry,true,inventoryGroupId:saved.Id);
                    try{await Until(()=>edit.IsCompleted);}
                    catch(IOException ex){
                        throw new IOException("Inventory editor remained open after "+(confirm?"Save":"Cancel")+".",ex);
                    }
                    await edit;
                }
                check(sawMember&&removedMember&&staged&&linkOnly,"Unlink stages the split and enables saving links alone.");
                if(!confirm)
                    check(store.Load().Groups.Count==1&&Safe.Json.Serialize(store.Load().Groups.Single())==originalGroup,
                        "Cancel discards the unlink and reopening the editor shows the member.");
            }
            check(store.Load().Groups.Count==2&&store.Load().Groups.Single(group=>group.Id==saved.Id).Paths.SequenceEqual(new[]{main})&&
                form.VisibleGameInventory().Any(file=>file.Mod=="local-drone-input"&&file.Path==input)&&
                form.state.List.Mods.Count==initialModCount&&Safe.Hash(Safe.In(game,input))==hashes[1],
                "Saving a split returns the component to the inventory without registering either mod or changing files.");
            var detached=store.Load().Groups.Single(group=>group.Id!=saved.Id);
            form.MergeInventoryRows(detached.Id,saved.Id);
            check(store.Load().Groups.Single().Linked.Single().Paths.SequenceEqual(new[]{input}),
                "The detached item's identity remains available when it is linked again.");

            form.testNexusPreviews=(link,budget)=>{
                string[] expected=link.Mod==9990?new[]{main,input,third}:new[]{catalogBase,catalogExtra};
                return new[]{new NexusFilePreview{Id=2,Files=expected.ToDictionary(path=>path,path=>(long?)8,StringComparer.OrdinalIgnoreCase)}};
            };
            int localPrompts=0;
            form.testInventoryLinkChoice=proposal=>{
                check(proposal.Target.CatalogId==null&&proposal.Paths.SequenceEqual(new[]{third}),"The saved URL connects only matching extra routes.");
                localPrompts++;
                return DialogResult.No;
            };
            await form.SaveInventorySource(saved.Id,localUrl);
            check(localPrompts==1&&store.Load().Groups.Single().Source==localUrl&&form.InventoryRow(saved.Id).Length==2,
                "Pasting a Nexus link checks matching paths immediately and No leaves them separate.");
            form.testInventoryLinkChoice=proposal=>{
                if(proposal.Target.CatalogId!=null)return DialogResult.Yes;
                localPrompts++;
                return DialogResult.Ignore;
            };
            using(var timer=new Timer{Interval=40}){
                timer.Tick+=(sender,args)=>{
                    var review=Application.OpenForms.OfType<GeneralDependencyReview>().FirstOrDefault(item=>item.Visible);
                    if(review==null)return;
                    timer.Stop();
                    review.Close();
                };
                timer.Start();
                await form.VerifyProject();
            }
            check(localPrompts==2&&store.Load().Ignored.SequenceEqual(new[]{"cyberpunk2077/9990"}),
                "No is asked again on verification and Never persists the Nexus mod identity.");
            check(form.DisplayedInventory().Any(file=>file.ModId==registered.Id&&file.Path==catalogExtra&&file.Kind=="associated")&&
                !form.VisibleGameInventory().Any(file=>file.Path==catalogExtra),"Yes associates an unidentified fragment with a catalog mod in the view.");
            form.inventoryLinks=null;
            await form.Scan();
            await form.ReviewInventoryLinks();
            check(localPrompts==2&&form.InventoryRow(saved.Id).Length==2,"Grouping and the Never choice survive another scan and reload.");
            form.UpdateInventoryLinks(links=>links.Groups.RemoveAll(group=>group.Id==saved.Id||group.CatalogId==registered.Id));
            check(form.VisibleGameInventory().Any(file=>file.Path==catalogExtra)&&
                form.VisibleGameInventory().Where(file=>file.Path==main||file.Path==input).Select(InventoryLinkStore.Key).Distinct().Count()==2,
                "Undo restores the separate inventory rows.");
            var remaining=form.VisibleGameInventory().Single(file=>file.Path==main);
            string key=InventoryLinkStore.Key(remaining);
            form.testManualInventoryLink=(source,owner)=>false;
            form.LinkInventoryToMod(key,registered);
            check(store.Load().Groups.Count==0,"Cancelling confirmation keeps the item outside the catalog.");
            form.marked.Add(other.Id);
            form.PickInventoryOwner(remaining);
            check(form.associationChoice!=null,"The owner picker is active before Escape.");
            check(form.footerAction.Text=="Cancelar"&&!form.versionFooter.Visible,
                "The picker replaces the left action and hides update counters: "+form.footerAction.Text+" / "+form.versionFooter.Visible);
            check(form.primaryAction.Text=="Aplicar vínculo"&&!form.primaryAction.Enabled,
                "The right action awaits a selection: "+form.primaryAction.Text+" / "+form.primaryAction.Enabled);
            form.ToggleInventoryOwner(form.ModRows().Single(row=>Equals(row.Tag,registered.Id)));
            var escape=Message.Create(form.grid.Handle,0x100,(IntPtr)Keys.Escape,IntPtr.Zero);
            check(form.ProcessCmdKey(ref escape,Keys.Escape),"Escape is handled by the picker.");
            for(int tries=0;tries<30&&form.associationChoice!=null;tries++)await System.Threading.Tasks.Task.Delay(15);
            check(!form.pickingInventoryOwner&&form.associationChoice==null&&store.Load().Attachments.Count==0&&
                form.marked.SetEquals(new[]{other.Id}),"Escape cancels the link and restores the original selection.");
            form.marked.Clear();
            int linksConfirmed=0;
            form.testManualInventoryLink=(source,owner)=>{linksConfirmed++;return owner==registered.Name;};
            form.PickInventoryOwner(remaining);
            check(form.pickingInventoryOwner&&form.associationChoice!=null,"Menu opens the in-list owner picker.");
            using(var image=new Bitmap(form.Width,form.Height)){
                form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));
                image.Save(Safe.In(root,"inventory-owner-picker.png"),System.Drawing.Imaging.ImageFormat.Png);
            }
            registeredRow=form.ModRows().Single(row=>Equals(row.Tag,registered.Id));
            var click=new DataGridViewCellMouseEventArgs(form.grid.Columns["Name"].Index,registeredRow.Index,10,10,
                new MouseEventArgs(MouseButtons.Left,1,10,10,0));
            typeof(DataGridView).GetMethod("OnCellMouseClick",BindingFlags.Instance|BindingFlags.NonPublic)
                .Invoke(form.grid,new object[]{click});
            form.ToggleInventoryOwner(form.ModRows().Single(row=>Equals(row.Tag,other.Id)));
            check(form.primaryAction.Enabled&&store.Load().Attachments.Count==0,"Selections stay pending until Apply.");
            form.primaryAction.PerformClick();
            for(int tries=0;tries<60&&store.Load().Attachments.Count==0;tries++)await System.Threading.Tasks.Task.Delay(15);
            check(linksConfirmed==0&&store.Load().Attachments.Count==1&&store.Load().Attachments[0].Owners.Length==2&&
                form.DisplayedInventory().Count(file=>file.Kind=="associated"&&file.Path==main)==2&&
                !form.VisibleGameInventory().Any(file=>file.Path==main),"Apply attaches the files to both mods and clears the loose row.");
            form.UpdateInventoryLinks(links=>links.Attachments.Clear());
            registeredRow=form.ModRows().Single(row=>Equals(row.Tag,registered.Id));
            form.inventoryDragKey=key;
            var direct=at(registeredRow);
            direct=new DragEventArgs(new DataObject(InventoryDragFormat,key),0,direct.X,direct.Y,DragDropEffects.Move,DragDropEffects.None);
            typeof(Control).GetMethod("OnDragDrop",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form.grid,new object[]{direct});
            form.inventoryDragKey=null;
            for(int tries=0;tries<60&&store.Load().Attachments.Count==0;tries++)await System.Threading.Tasks.Task.Delay(15);
            check(linksConfirmed==1&&store.Load().Attachments.Count==1&&store.Load().Attachments[0].Path==main,"Dropping on a registered mod uses the same association storage.");
            form.UpdateInventoryLinks(links=>links.Attachments.Clear());
            check(state.SnapshotReceipts.Count==0&&state.CyberpunkImports.Count==0&&
                paths.Select(path=>Safe.Hash(Safe.In(game,path))).SequenceEqual(hashes),"Grouping leaves installed content and all ownership records untouched.");

            var knownFile=form.VisibleGameInventory().FirstOrDefault(file=>file.Path==main);
            check(knownFile!=null,"The raw inventory row returns after removing the local attachment.");
            knownFile.ProvenOwner=registered.Id;
            form.DrawRows();
            var knownRow=form.grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(row=>Object.ReferenceEquals(row.Tag,knownFile));
            check(knownRow!=null,"The verified file has its own inventory row.");
            bool showedChoices=false;
            using(var choose=new Timer{Interval=35}){
                choose.Tick+=(s,e)=>{
                    var dialog=Application.OpenForms.OfType<GeneralDialog>().SingleOrDefault(item=>item.Text=="Definir origem");
                    if(dialog==null)return;
                    choose.Stop();
                    var buttons=AllControls(dialog).OfType<Button>().ToArray();
                    showedChoices=buttons.Any(button=>button.Text=="Vínculo manual");
                    buttons.Single(button=>button.Text=="Vínculo automático").PerformClick();
                };
                choose.Start();form.OpenRowEditor(knownRow);
            }
            for(int tries=0;tries<60&&store.Load().Attachments.Count==0;tries++)await System.Threading.Tasks.Task.Delay(15);
            check(showedChoices&&store.Load().Attachments.Count==1&&store.Load().Attachments[0].Owners.SequenceEqual(new[]{registered.Id}),
                "Double-click on verified inventory offers manual or automatic linking and reuses a confirmed parent.");
            string managed=Safe.In(game,main),modifiedAttachment=Safe.In(game,input),untouched=Safe.In(game,third);
            form.UpdateInventoryLinks(links=>{
                links.Attachments.Single().Owners=new[]{registered.Id,other.Id};
                links.Attachments.Add(new InventoryAttachment{Path=input,Hash=Safe.Hash(modifiedAttachment),Owners=new[]{other.Id}});
            });
            File.WriteAllText(modifiedAttachment,"user edit");
            form.RemoveInventoryAttachments(game,new HashSet<string>(StringComparer.Ordinal){registered.Id});
            check(File.Exists(managed)&&store.Load().Attachments.Single(file=>file.Path==main).Owners.SequenceEqual(new[]{other.Id}),
                "Uninstall of one owner preserves its shared attachment.");
            form.RemoveInventoryAttachments(game,new HashSet<string>(StringComparer.Ordinal){other.Id});
            check(!File.Exists(managed)&&File.ReadAllText(modifiedAttachment)=="user edit"&&File.Exists(untouched)&&store.Load().Attachments.Count==0,
                "Uninstall of the last owner removes the verified attachment and preserves edits and unrelated files.");
            File.WriteAllText(managed,"contents");File.WriteAllText(modifiedAttachment,"contents");

            const string nightFolder="red4ext/plugins/NightCoop/";
            const string nightDll=nightFolder+"NightCoop.dll", nightLog=nightFolder+"logs/nightcoop.log",
                nightUnknownExtension=nightFolder+"output/session.custom";
            foreach(string relative in new[]{nightDll,nightLog,nightUnknownExtension}){
                string file=Safe.In(game,relative);
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file,"night fixture");
            }
            var night=NewEntry("NightCoop","https://www.nexusmods.com/cyberpunk2077/mods/9992");
            night.Variants[0].CyberpunkId="night-fixture";
            state.List.Mods.Add(night);
            catalog.Packages.Add(new FixedMod{Id="night-fixture",Name="NightCoop",Version="1",
                Dependencies=new string[0],Assets=new List<FixedAsset>(),
                Files=new List<FixedFile>{new FixedFile{Path=nightDll,Sha256=Safe.Hash(Safe.In(game,nightDll)),Size=13}}});
            var nightInventory=form.ReadGameInventory(game);
            check(new[]{nightLog,nightUnknownExtension}.All(path=>nightInventory.Any(file=>
                file.Path==path&&file.Kind=="associated"&&file.ModId==night.Id&&file.LocalInModFolder&&
                file.ProvenOwner==null&&file.VerifiedZip==null)), "Dedicated NightCoop folder groups logs and files of any extension.");
            state.List.Mods.Remove(night);
            catalog.Packages.RemoveAt(catalog.Packages.Count-1);

            const string legacyFile="red4ext/plugins/legacy-owner/plugin.dll";
            const string generatedFile="red4ext/plugins/legacy-owner/run.log";
            string legacyFullPath=Safe.In(game,legacyFile);
            Directory.CreateDirectory(Path.GetDirectoryName(legacyFullPath));
            File.WriteAllText(legacyFullPath,"verified");
            File.WriteAllText(Safe.In(game,generatedFile),"local log");
            var legacy=new FixedMod{Id="legacy-recipe",Name="Legacy name",Version="1",
                Dependencies=new string[0],Assets=new List<FixedAsset>(),
                Files=new List<FixedFile>{new FixedFile{Path=legacyFile,Sha256=Safe.Hash(legacyFullPath),Size=8}}};
            catalog.Packages.Add(legacy);
            await form.Scan();
            check(form.VisibleGameInventory().Any(file=>file.Path==legacyFile&&file.ProvenOwner==legacy.Id),
                "A verified file without a mod in this project stays outside the catalog.");
            var outside=form.grid.Rows.Cast<DataGridViewRow>().Single(row=>row.Tag is P2PSyncDifference&&
                ((P2PSyncDifference)row.Tag).Path==legacyFile);
            bool manualOnly=false;
            using(var timer=new Timer{Interval=35}){
                timer.Tick+=(s,e)=>{
                    var dialog=Application.OpenForms.OfType<GeneralDialog>().SingleOrDefault(item=>item.Text=="Definir origem");
                    if(dialog==null)return;
                    timer.Stop();
                    var buttons=AllControls(dialog).OfType<Button>().ToArray();
                    manualOnly=buttons.Any(button=>button.Text=="Associar a um mod")&&
                        buttons.All(button=>button.Text!="Vínculo automático");
                    dialog.Close();
                };
                timer.Start();form.OpenRowEditor(outside);
            }
            check(manualOnly&&state.List.Mods.Count==2&&store.Load().Attachments.Count==0,
                "A verified origin without an existing parent offers linking without creating a catalog entry.");
            string registeredKey=Key(registered,registered.Variants[0]);
            state.CyberpunkImports.Add(registeredKey,legacy.Id);
            await form.Scan();
            var legacyInventory=form.DisplayedInventory().Where(file=>file.Path==legacyFile||file.Path==generatedFile).ToArray();
            check(legacyInventory.Length==2&&legacyInventory.All(file=>file.Kind=="associated"&&file.ModId==registered.Id)&&
                legacyInventory.Any(file=>file.Path==legacyFile&&file.ProvenOwner==legacy.Id)&&
                legacyInventory.Any(file=>file.Path==generatedFile&&file.LocalInModFolder&&file.ProvenOwner==null)&&
                !form.VisibleGameInventory().Any(file=>file.Path==legacyFile||file.Path==generatedFile),
                "A unique project binding associates verified files and keeps generated files labeled as local.");
            string acknowledged=InventoryLinkStore.LocalFileKey(registered.Id,generatedFile);
            for(int visit=0;visit<2;visit++){
                Exception editorFailure=null;
                bool inspected=false;
                using(var timer=new Timer{Interval=40}){
                    timer.Tick+=(sender,args)=>{
                        var editor=Application.OpenForms.OfType<GeneralModEditor>().FirstOrDefault(item=>item.Visible);
                        if(editor==null)return;
                        timer.Stop();
                        try{
                            var fileTree=AllControls(editor).OfType<ListBox>().Single(control=>control.Name=="ArchiveFileTree");
                            var location=AllControls(editor).OfType<TextBox>().Single(control=>control.Name=="ArchiveCurrentDestination");
                            var keyDown=typeof(Control).GetMethod("OnKeyDown",BindingFlags.Instance|BindingFlags.NonPublic);
                            fileTree.SelectedItem=fileTree.Items.Cast<TreeNode>().Single(entryNode=>entryNode.Text=="Arquivos locais");
                            keyDown.Invoke(fileTree,new object[]{new KeyEventArgs(Keys.Right)});
                            var log=fileTree.Items.Cast<TreeNode>().Single(entryNode=>(string)entryNode.Tag==generatedFile);
                            fileTree.SelectedItem=log;
                            check(location.ReadOnly&&location.Text==Safe.In(game,generatedFile),
                                "The generated file shows its full game path without allowing route edits.");
                            check((fileTree.AccessibleDescription!=null)==(visit==0),
                                "New appears on the first visit and stays cleared after reopening.");
                            if(visit==0){
                                using(var screenshot=new Bitmap(fileTree.Width,fileTree.Height)){
                                    fileTree.DrawToBitmap(screenshot,fileTree.ClientRectangle);
                                    screenshot.Save(Safe.In(root,"local-file-new.png"));
                                }
                            }
                            var verified=fileTree.Items.Cast<TreeNode>().Single(entryNode=>(string)entryNode.Tag==legacyFile);
                            fileTree.SelectedItem=verified;
                            check(fileTree.AccessibleDescription==null,"A verified package file is not marked new.");
                            fileTree.SelectedItem=log;
                            var bounds=fileTree.GetItemRectangle(fileTree.SelectedIndex);
                            typeof(Control).GetMethod("OnMouseClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(fileTree,
                                new object[]{new MouseEventArgs(MouseButtons.Left,1,bounds.X+10,bounds.Y+10,0)});
                            check(fileTree.AccessibleDescription==null&&store.Load().SeenLocalFiles.Contains(acknowledged),
                                "Clicking clears the badge and saves acknowledgement immediately.");
                            inspected=true;
                        }catch(Exception ex){editorFailure=ex;}
                        finally{editor.Close();}
                    };
                    timer.Start();
                    await form.EditMod(registered);
                }
                if(editorFailure!=null)throw editorFailure;
                check(inspected,"The mod editor opened for local files.");
                form.inventoryLinks=null;
            }
            var secondOwner=NewEntry("Second mod",null);
            state.List.Mods.Add(secondOwner);
            string secondKey=Key(secondOwner,secondOwner.Variants[0]);
            state.CyberpunkImports.Add(secondKey,legacy.Id);
            await form.Scan();
            check(form.VisibleGameInventory().Any(file=>file.Path==legacyFile&&file.ProvenOwner==legacy.Id)&&
                !form.DisplayedInventory().Any(file=>file.Path==legacyFile&&file.Kind=="associated"),
                "A verified file with two candidate project mods stays outside the catalog.");
            state.CyberpunkImports.Remove(secondKey);
            state.CyberpunkImports.Remove(registeredKey);
            state.List.Mods.Remove(secondOwner);
            registered.DetectedPaths.Add(legacyFile);
            await form.Scan();
            check(form.DisplayedInventory().Any(file=>file.Path==generatedFile&&file.Kind=="associated"&&file.ModId==registered.Id)&&
                !form.VisibleGameInventory().Any(file=>file.Path==legacyFile||file.Path==generatedFile),
                "Registering a verified path links its local companion on the next scan.");
            registered.DetectedPaths.Remove(legacyFile);
            catalog.Packages.Remove(legacy);
            string legacyArchive=Safe.In(root,"legacy-origin.zip");
            using(var zip=ZipFile.Open(legacyArchive,ZipArchiveMode.Create))WriteZipEntry(zip,legacyFile,"verified");
            await form.Scan();
            check(form.VisibleGameInventory().Any(file=>file.Path==legacyFile&&file.VerifiedZip=="legacy-origin.zip"),
                "The local scan offers packages verified by the original ZIP even without a receipt.");
            registered.DetectedPaths.Add(legacyFile);
            await form.Scan();
            check(form.DisplayedInventory().Any(file=>file.Path==generatedFile&&file.Kind=="associated"&&file.ModId==registered.Id),
                "A verified ZIP can also anchor a newly detected local companion after registration.");
            registered.DetectedPaths.Remove(legacyFile);
            File.Delete(legacyArchive);
            const string localDrone="red4ext/plugins/FPVDroneInput/FPVDroneInput.dll";
            check(FixedInstaller.InventoryRegistrationPath(localDrone)&&!FixedInstaller.SyncModPath(localDrone)&&
                !FixedInstaller.InventoryRegistrationPath("red4ext/plugins/FPVDroneInput/input.log"),
                "Local registration can identify the drone without including it in sharing or including runtime logs.");

            const string framework="bin/x64/plugins/cyber_engine_tweaks/mods/framework/";
            string exact=framework+"projects/villa.json", changed=framework+"projects/changed.json",
                unknown=framework+"projects/unknown.json", dependency=framework+"init.lua";
            foreach(string relative in new[]{exact,changed,unknown,dependency}){
                string file=Safe.In(game,relative);
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file,"installed");
            }
            string frameworkLog=framework+"nativeInteractions.log";
            File.WriteAllText(Safe.In(game,frameworkLog),"runtime log");
            string villa=Safe.In(root,"villa.zip"), nif=Safe.In(root,"nif.zip");
            using(var zip=ZipFile.Open(villa,ZipArchiveMode.Create)){
                WriteZipEntry(zip,exact,"installed");
                WriteZipEntry(zip,changed,"different");
            }
            using(var zip=ZipFile.Open(nif,ZipArchiveMode.Create))WriteZipEntry(zip,dependency,"installed");
            File.WriteAllText(Safe.In(root,"corrupt.zip"),"not an archive");
            await form.Scan();
            check(form.VisibleGameInventory().Any(file=>file.Path==exact&&file.VerifiedZip=="villa.zip")&&
                form.VisibleGameInventory().Any(file=>file.Path==dependency&&file.VerifiedZip=="nif.zip"),
                "A regular scan uses the existing download ZIPs without manual verification.");
            string duplicate=Safe.In(root,"another origin.zip");
            File.Copy(villa,duplicate);
            await form.Scan();
            check(form.VisibleGameInventory().Any(file=>file.Path==exact&&file.VerifiedZip==null&&!file.LocalInModFolder),
                "Two packages claiming the same file leave it unknown.");
            File.Delete(duplicate);
            check(await form.VerifyInventoryZip(villa,"Villa V")==1&&await form.VerifyInventoryZip(nif,"Native Interactions")==1,
                "ZIP verification confirms byte matches across packages sharing a folder.");
            form.inventoryLinks=null;
            await form.Scan();
            var found=form.VisibleGameInventory().ToDictionary(file=>file.Path,InventoryPaths);
            check(found[exact].Mod=="Villa V"&&found[dependency].Mod=="Native Interactions"&&
                found[changed].VerifiedZip==null&&found[unknown].VerifiedZip==null&&
                found[frameworkLog].ProvenOwner==null&&!found[frameworkLog].LocalInModFolder,
                "Reload preserves exact origins and keeps mismatched or unlisted neighbors unverified.");
            check(form.grid.Rows.Cast<DataGridViewRow>().Any(row=>Equals(row.Cells["Origin"].Value,"ZIP")&&Equals(row.Cells["Name"].Value,"Villa V"))&&
                form.grid.Rows.Cast<DataGridViewRow>().Any(row=>row.Tag is P2PSyncDifference&&
                    InventoryLinkStore.Key((P2PSyncDifference)row.Tag)==InventoryLinkStore.Key(found[unknown])&&Equals(row.Cells["Origin"].Value,"?")),
                "The inventory shows ZIP for verified files and a question mark for the remaining files.");
            var installedEntry=NewEntry("Recorded mod",null);
            string receiptZip=Safe.In(root,"recorded.zip");
            installedEntry.Variants[0].Sources.Add(receiptZip);
            state.List.Mods.Add(installedEntry);
            string recordedFile=framework+"registered.lua", changedFile=framework+"edited.lua";
            using(var archive=ZipFile.Open(receiptZip,ZipArchiveMode.Create)){
                WriteZipEntry(archive,recordedFile,"registered");
                WriteZipEntry(archive,changedFile,"original");
            }
            var work=form.MakeWork(installedEntry);
            form.Generic(game).Install(receiptZip,work.Receipt(0),null);
            File.WriteAllText(Safe.In(game,changedFile),"modified by user");
            await form.Scan();
            var tree=GeneralInventoryTree.BuildTree(form.DisplayedInventory(),"Game");
            Func<string,GeneralInventoryTree.Entry> node=relative=>relative.TrimEnd('/').Split('/')
                .Aggregate(tree,(branch,name)=>branch.Children[name]);
            check(node(framework).Folder&&node(framework).ContainsRecord&&
                node(recordedFile).File.RecordedFile&&!node(recordedFile).File.RecordedChange,
                "A mixed folder and its matching receipt file show the Mod Loader origin.");
            check(node(changedFile).File.RecordedChange&&node(changedFile).File.Mod.Contains("Recorded mod")&&
                !node(unknown).ContainsRecord&&!node(unknown).ContainsProof&&node(frameworkLog).File!=null&&
                !node(frameworkLog).ContainsRecord&&!node(frameworkLog).ContainsProof,
                "Modified and unknown files are distinguished inside the same folder.");
            check(tree.Children.ContainsKey("archive")&&!node("archive/pc/mod").ContainsRecord,
                "A folder without a receipt does not inherit the Mod Loader mark from another branch.");
            var inventoryHeading=form.grid.Rows.Cast<DataGridViewRow>().Single(row=>row.Cells["Name"].ToolTipText==InventoryInternalLabel);
            Exception dialogError=null;
            using(var timer=new Timer{Interval=30}){
                timer.Tick+=(s,e)=>{
                    timer.Stop();
                    var dialog=Application.OpenForms.OfType<GeneralInventoryTree>().SingleOrDefault();
                    if(dialog==null){dialogError=new IOException("Explorer dialog did not open.");return;}
                    try{
                        var list=(ListBox)dialog.Controls.Find("InventoryFileTree",true).Single();
                        check(list.Items.Count>1&&list.Items.Cast<GeneralInventoryTree.Entry>().Any(entry=>entry.Name=="bin"),
                            "The inventory section opens the real expandable file tree.");
                    }catch(Exception ex){dialogError=ex;}
                    finally{dialog.Close();}
                };
                timer.Start();
                form.HandleSection((SectionRow)inventoryHeading.Tag);
                await Until(()=>!timer.Enabled);
            }
            if(dialogError!=null)throw dialogError;
            using(var font=GeneralStyle.Font(11,true)){
                var actions=form.InventoryHeaderActions(new Rectangle(0,0,form.CategoryContentRight,54),font);
                check(form.InventoryHeaderActionAt(actions[0].Left+1,20)==1&&
                    form.InventoryHeaderActionAt(actions[1].Left+1,20)==2&&form.InventoryHeaderActionAt(actions[1].Left+1,1)==0,
                    "The two header labels have distinct click targets.");
            }
            using(var details=new GeneralSyncDifferencesDialog(new[]{found[exact],found[dependency]},true,showSource:true)){
                var table=(DataGridView)details.Controls.Find("SyncDifferenceFiles",true).Single();
                var selected=table.Rows.Cast<DataGridViewRow>().Single(row=>row.Tag==found[exact]);
                check(Equals(selected.Cells[3].Value,"villa.zip"),"Details show the source ZIP.");
            }
            var menu=form.grid.ContextMenuStrip;
            var rowForVilla=form.grid.Rows.Cast<DataGridViewRow>().Single(row=>row.Tag is P2PSyncDifference && ((P2PSyncDifference)row.Tag).Path==exact);
            form.grid.CurrentCell=rowForVilla.Cells["Name"];
            menu.Show(form,new Point(20,20));
            check(menu.Items.Cast<ToolStripItem>().Where(item=>item.Available).Select(item=>item.Text)
                .SequenceEqual(new[]{"Categorizar","Detalhes","Abrir local no Mod Loader","Abrir local no Explorador","Definir origem…"}),
                "The inventory context menu opens one editor, the focused origin dialog and both location actions.");
            menu.Close();
            using(var local=new GeneralInventoryTree(game,form.DisplayedInventory(),found[exact].Path)){
                var list=(ListBox)local.Controls.Find("InventoryFileTree",true).Single();
                var selected=(GeneralInventoryTree.Entry)list.SelectedItem;
                check(selected.Path==exact&&selected.Parent.Open,"The internal shortcut opens the file's containing branch.");
            }
            File.WriteAllText(Safe.In(game,exact),"modified!");
            await form.Scan();
            check(form.VisibleGameInventory().Single(file=>file.Path==exact).VerifiedZip==null,
                "Changing the installed bytes removes the confirmed label on the next scan.");

            var misplaced=DetectedEntryForTest("Uncategorized component",third);
            state.List.Mods.Add(misplaced);
            form.ActivePreset.ModIds.Add(misplaced.Id);
            form.UpdateInventoryLinks(links=>links.Attachments.Add(new InventoryAttachment{
                Path=third,Hash=hashes[2],Owners=new[]{misplaced.Id,other.Id}}));
            form.ApplyDetectedEntryLink(misplaced,new[]{registered});
            check(!form.state.List.Mods.Any(mod=>mod.Id==misplaced.Id)&&
                !form.ActivePreset.ModIds.Contains(misplaced.Id)&&
                store.Load().Attachments.Single(file=>file.Path==third).Owners.SequenceEqual(new[]{other.Id,registered.Id})&&
                Safe.Hash(Safe.In(game,third))==hashes[2],
                "Merging a mistaken catalog row preserves the files, their hashes and their other existing owners.");

            const string originalPath="r6/scripts/source-fixture/core.reds",editedPath="r6/scripts/source-edited/core.reds",
                configPath="r6/scripts/source-fixture/settings.txt";
            string originalArchive=Safe.In(root,"retained-source.zip"),editedArchive=Safe.In(root,"edited-source.zip");
            using(var zip=ZipFile.Open(originalArchive,ZipArchiveMode.Create))WriteZipEntry(zip,originalPath,"stable edition");
            using(var zip=ZipFile.Open(editedArchive,ZipArchiveMode.Create))WriteZipEntry(zip,editedPath,"stable edition");
            var originalImport=ImportItem(originalArchive);var editedImport=ImportItem(editedArchive);
            form.Cyberpunk(game).InstallImported(originalImport,_=>{});
            form.Cyberpunk(game).InstallImported(editedImport,_=>{});
            File.WriteAllText(Safe.In(game,editedPath),"changed after installation");
            File.WriteAllText(Safe.In(game,configPath),"local config");
            var capsule=DetectedEntryForTest("Detected source");
            var edited=DetectedEntryForTest("Edited source",editedPath);
            var stable=NewEntry("Published source",localUrl);
            stable.Category="Fixture category";
            form.state.List.Categories.Add(stable.Category);
            form.ActivePreset.Categories.Add(stable.Category);
            var stableEdited=NewEntry("Published edited source",knownUrl);
            var peer=new GeneralModState{Destination=game,List=new GeneralModList{Mods=new List<GeneralModEntry>{stable,stableEdited}}};
            peer.CyberpunkImports[P2PPack.Slot(stable,stable.Variants[0],0)]=LocalModPackage.RoutedId(originalImport.Sha256,stable.Variants[0].InstallRoutes);
            peer.CyberpunkImports[P2PPack.Slot(stableEdited,stableEdited.Variants[0],0)]=LocalModPackage.RoutedId(editedImport.Sha256,stableEdited.Variants[0].InstallRoutes);
            form.testUpdateContexts=()=>new[]{new GeneralUpdateOwnershipContext{Data=Safe.In(root,"peer"),State=peer}};
            form.state.List.Mods.AddRange(new[]{capsule,edited});
            form.ActivePreset.ModIds.AddRange(new[]{capsule.Id,edited.Id});
            form.UpdateInventoryLinks(links=>{
                links.Attachments.Add(new InventoryAttachment{Path=originalPath,Hash=Safe.Hash(Safe.In(game,originalPath)),Owners=new[]{capsule.Id}});
                links.Attachments.Add(new InventoryAttachment{Path=configPath,Owners=new[]{capsule.Id}});
            });
            await form.Scan();
            var restored=form.state.List.Mods.Single(mod=>mod.Id==capsule.Id);
            var persisted=new GeneralModLibrary(Safe.In(appData,"lista.json")).Load();
            check(restored.Variants.Single().Sources.SequenceEqual(new[]{localUrl})&&restored.Category==stable.Category&&
                form.statuses[form.MakeWork(restored).Key]=="Instalado"&&form.ActivePreset.ModIds.Contains(capsule.Id)&&
                !form.state.List.Mods.Any(mod=>mod.Id==stable.Id)&&
                persisted.List.Mods.Single(mod=>mod.Id==capsule.Id).Variants[0].Sources[0]==localUrl&&
                store.Load().Attachments.Single(file=>file.Path==configPath).Owners.SequenceEqual(new[]{capsule.Id}),
                "An intact peer receipt restores the original update source on the existing row, without duplicating it or changing local associations.");
            check(IsLocalInventoryEntry(form.state.List.Mods.Single(mod=>mod.Id==edited.Id))&&
                File.ReadAllText(Safe.In(game,editedPath))=="changed after installation",
                "An edited package cannot acquire a peer source from matching paths alone.");

            const string looseRoot="bin/x64/plugins/cyber_engine_tweaks/mods/local-category-test/";
            string[] looseFiles={looseRoot+"init.lua",looseRoot+"lib.lua"};
            Directory.CreateDirectory(Safe.In(game,looseRoot+"sub"));
            foreach(string path in looseFiles)File.WriteAllText(Safe.In(game,path),"local content");
            await form.Scan();
            var loose=form.grid.Rows.Cast<DataGridViewRow>().Single(row=>row.Tag is P2PSyncDifference&&
                looseFiles.Contains(((P2PSyncDifference)row.Tag).Path));
            form.grid.CurrentCell=loose.Cells["Name"];
            menu.Show(form,new Point(20,20));
            var categories=menu.Items.OfType<ToolStripMenuItem>().Single(item=>item.Name=="CategorizeMods");
            check(categories.Available&&categories.Enabled&&categories.HasDropDownItems,
                "An unidentified row exposes its category submenu before the first hover.");
            categories.ShowDropDown();
            categories.DropDown.Items.Cast<ToolStripItem>().Single(item=>item.Text==stable.Category).PerformClick();
            menu.Close();
            var categorized=form.state.List.Mods.Single(mod=>mod.DetectedPaths.Contains(looseFiles[0]));
            check(categorized.Category==stable.Category&&form.ActivePreset.ModIds.Contains(categorized.Id)&&
                new HashSet<string>(categorized.DetectedPaths,InventoryPaths).SetEquals(looseFiles)&&
                !form.VisibleGameInventory().Any(file=>looseFiles.Contains(file.Path))&&
                form.statuses[form.MakeWork(categorized).Key]==GeneralStatusPresentation.DetectedFiles,
                "Categorizing a detected row registers its files together in the active preset.");
            form.SaveProjectEdits();
            await form.Scan();
            check(form.library.Load().List.Mods.Single(mod=>mod.Id==categorized.Id).Category==stable.Category&&
                !form.VisibleGameInventory().Any(file=>looseFiles.Contains(file.Path))&&
                looseFiles.All(path=>File.ReadAllText(Safe.In(game,path))=="local content"),
                "The category survives save and rescan and the local files are untouched.");

            const string reused="r6/scripts/reused-attachment/feature.reds";
            string reusedFile=Safe.In(game,reused);
            Directory.CreateDirectory(Path.GetDirectoryName(reusedFile));
            File.WriteAllText(reusedFile,"first");
            await form.Scan();
            var existingAttachment=form.VisibleGameInventory().Single(file=>file.Path==reused);
            var firstOwner=form.state.List.Mods.Single(mod=>mod.Id==registered.Id);
            await form.SaveInventoryAttachments(new[]{existingAttachment},new[]{firstOwner});
            await form.Scan();
            check(form.DisplayedInventory().Any(file=>file.Path==reused&&file.Kind=="associated"&&file.ModId==firstOwner.Id),
                "Unchanged content remains linked after a rescan.");
            var timestamp=File.GetLastWriteTimeUtc(reusedFile);
            File.WriteAllText(reusedFile,"other");
            File.SetLastWriteTimeUtc(reusedFile,timestamp);
            await form.Scan();
            check(form.VisibleGameInventory().Any(file=>file.Path==reused)&&
                !form.DisplayedInventory().Any(file=>file.Path==reused&&file.ModId==firstOwner.Id)&&
                store.Load().Attachments.Single(file=>file.Path==reused).Hash!=Safe.Hash(reusedFile),
                "Replacing an attachment at the same path and size surfaces it again without silently moving its old proof.");
            var shared=new HashSet<string>(InventoryPaths);
            var candidates=form.InventoryUpdateFiles(form.MakeWork(firstOwner),game,form.ReadUpdateContexts(game),shared);
            check(shared.Contains(reused)&&candidates.All(file=>file.Path!=reused)&&File.ReadAllText(reusedFile)=="other",
                "An update treats the replacement as protected instead of importing it from an old attachment.");
            var newOwner=form.state.List.Mods.Single(mod=>mod.Id==other.Id);
            await form.SaveInventoryAttachments(new[]{form.VisibleGameInventory().Single(file=>file.Path==reused)},new[]{newOwner});
            check(form.DisplayedInventory().Any(file=>file.Path==reused&&file.ModId==newOwner.Id)&&
                store.Load().Attachments.Single(file=>file.Path==reused).Hash==Safe.Hash(reusedFile),
                "An explicit new link takes effect immediately with the current content proof.");
        });
    }
}
