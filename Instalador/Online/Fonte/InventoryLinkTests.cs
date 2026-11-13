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

    internal static void RunInventoryLinkTests(string parent){
        string root=OnlineTests.NestedRoot(parent,"inv-links-");
        Directory.CreateDirectory(root);
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
        var catalog=new FixedCatalog{Schema=1,Id="inventory-links-fixture",GameVersion="2.5",GameFileVersion="2.5.0.0",Packages=new List<FixedMod>()};
        var state=new GeneralModState{List=new GeneralModList{Mods=new List<GeneralModEntry>{registered}},Language="pt",DownloadFolder=root};
        Action<bool,string> check=(ok,message)=>{if(!ok)throw new IOException(message);};
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
            check(form.InventoryDropEffect(form.grid,at(registeredRow))==DragDropEffects.None,"Manual drag cannot merge a catalog row.");
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
            check(state.SnapshotReceipts.Count==0&&state.CyberpunkImports.Count==0&&
                paths.Select(path=>Safe.Hash(Safe.In(game,path))).SequenceEqual(hashes),"Grouping leaves installed content and all ownership records untouched.");

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
                .SequenceEqual(new[]{"Detalhes","Abrir local no Mod Loader","Abrir local no Explorador"}),
                "The inventory context menu exposes Details and both location actions directly.");
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
        });
    }
}
