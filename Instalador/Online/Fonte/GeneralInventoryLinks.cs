using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    const string InventoryDragFormat="ModLoader.InventoryLink";
    static readonly StringComparer InventoryPaths=StringComparer.OrdinalIgnoreCase;
    string inventoryRoot,inventoryDragKey;
    InventoryLinkData inventoryLinks;
    string inventoryLoadError;
    Func<InventoryLinkSuggestion,DialogResult> testInventoryLinkChoice;
    Func<string,string,bool> testManualInventoryLink;

    InventoryLinkData InventoryLinks {
        get {
            if(inventoryLinks!=null&&InventoryPaths.Equals(inventoryRoot,state.Destination))return inventoryLinks;
            inventoryRoot=state.Destination;
            inventoryLoadError=null;
            try {
                inventoryLinks=String.IsNullOrWhiteSpace(inventoryRoot)?new InventoryLinkData():new InventoryLinkStore(data,inventoryRoot).Load();
            }catch(Exception ex){
                if(!(ex is IOException||ex is UnauthorizedAccessException||ex is ArgumentException||ex is InvalidOperationException))throw;
                inventoryLoadError=ex.Message;inventoryLinks=new InventoryLinkData();
            }
            return inventoryLinks;
        }
    }

    IEnumerable<P2PSyncDifference> DisplayedInventory(){
        var recognized=new HashSet<string>(nexusManual.Values.Where(match=>match.Files!=null)
            .SelectMany(match=>match.Files.Keys),InventoryPaths);
        return InventoryLinkStore.View(gameInventory,InventoryLinks,state.List.Mods)
            .Where(file=>file.Kind!="found"||file.InventoryGroupId!=null||!recognized.Contains(file.Path));
    }

    void UpdateInventoryLinks(Action<InventoryLinkData> update,bool redraw=true){
        var current=InventoryLinks;
        if(inventoryLoadError!=null)throw new IOException(inventoryLoadError);
        var copy=Safe.Json.Deserialize<InventoryLinkData>(Safe.Json.Serialize(current));
        update(copy);
        new InventoryLinkStore(data,state.Destination).Save(copy);
        inventoryLinks=copy;
        if(redraw)DrawRows();
    }

    LinkLabel detectedModsReview,unlinkedFilesReview;

    void AddDetectedModsReview(Control summary){
        detectedModsReview=new LinkLabel{
            Name="ReviewDetectedMods",AutoSize=true,Visible=false,LinkColor=GeneralStyle.Accent,
            ActiveLinkColor=GeneralStyle.Foreground,Margin=new Padding(16,0,0,0),Font=GeneralStyle.Font(10.5f)
        };
        detectedModsReview.LinkClicked+=(s,e)=>Guard(ReviewDetectedMods);
        summary.Controls.Add(detectedModsReview);
        unlinkedFilesReview=new LinkLabel{Name="ReviewUnlinkedFiles",AutoSize=true,Visible=false,LinkColor=GeneralStyle.Accent,
            ActiveLinkColor=GeneralStyle.Foreground,Margin=new Padding(16,0,0,0),Font=GeneralStyle.Font(10.5f)};
        unlinkedFilesReview.LinkClicked+=(s,e)=>Guard(()=>{
            if(!CanEditInventory)return;
            using(var dialog=new GeneralInventoryTree(state.Destination,DisplayedInventory().Where(file=>file.Kind=="unlinked")))
                dialog.ShowDialog(this);
        });
        summary.Controls.Add(unlinkedFilesReview);
    }

    IGrouping<string,P2PSyncDifference>[] DetectedModGroups(){
        var listed=new HashSet<string>(state.List.Mods.SelectMany(mod=>mod.DetectedPaths),InventoryPaths);
        return DisplayedInventory().Where(file=>file.Kind=="found"&&!file.LocalInModFolder&&!listed.Contains(file.Path)&&
            FixedInstaller.InventoryRegistrationPath(file.Path)).GroupBy(InventoryLinkStore.Key,InventoryPaths).ToArray();
    }

    void UpdateDetectedModsReview(){
        if(detectedModsReview==null)return;
        int count=DetectedModGroups().Length;
        detectedModsReview.Text=T("Revisar mods detectados","Review detected mods")+" ("+count+")";
        detectedModsReview.Visible=count>0;
        detectedModsReview.Enabled=CanEditInventory;
        int unlinked=gameInventory.Count(file=>file.Kind=="unlinked");
        unlinkedFilesReview.Text=T("Arquivos sem vínculo","Unlinked files")+" ("+unlinked+")";
        unlinkedFilesReview.Visible=unlinked>0;
        unlinkedFilesReview.Enabled=CanEditInventory;
    }

    void ReviewDetectedMods(){
        if(!CanEditInventory)return;
        var groups=DetectedModGroups();
        if(groups.Length==0)return;
        P2PSyncDifference selected=null;
        bool linkExisting=false;
        using(var dialog=new GeneralDialog(T("Mods detectados no jogo","Mods detected in the game")){ClientSize=new Size(780,495)}){
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(20,8,20,20)};
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var instructions=new Label{AutoSize=true,Margin=new Padding(0,0,0,10),Text=T(
                "Selecione um item para cadastrar no projeto ou vincular a um mod existente.",
                "Select an item to register in the project or link to an existing mod.")};
            layout.Controls.Add(instructions,0,0);
            var table=GeneralTableDialogLayout.SingleColumnTable("DetectedMods",dialog.Text,T("Mod ou grupo de arquivos","Mod or file group"));
            table.Columns.Add(new DataGridViewTextBoxColumn{HeaderText=T("Arquivos","Files"),Width=82,SortMode=DataGridViewColumnSortMode.NotSortable});
            table.Columns.Add(new DataGridViewTextBoxColumn{HeaderText=T("Origem","Origin"),Width=166,SortMode=DataGridViewColumnSortMode.NotSortable});
            foreach(var group in groups){
                var first=group.FirstOrDefault(file=>file.ProvenOwner!=null||file.VerifiedZip!=null)??group.First();
                string origin=first.ProvenOwner!=null?T("Confirmada","Verified"):
                    first.VerifiedZip!=null?T("ZIP conferido","Verified ZIP"):T("A confirmar","Unconfirmed");
                var row=table.Rows[table.Rows.Add(first.Mod??first.Path,group.Count(),origin)];
                row.Tag=first;
                row.Cells[0].ToolTipText=String.Join(Environment.NewLine,group.Select(file=>file.Path).Take(12));
            }
            layout.Controls.Add(GeneralTableDialogLayout.TableSurface(table),0,1);
            var commands=GeneralTableDialogLayout.Commands();
            Action<bool> choose=link=>{
                if(table.CurrentRow==null)return;
                var item=(P2PSyncDifference)table.CurrentRow.Tag;
                if(link&&!CanLinkInventoryFile(item))return;
                selected=item;
                linkExisting=link;
                dialog.Complete(DialogResult.OK);
            };
            var add=GeneralStyle.Button(T("Cadastrar…","Register…"),()=>choose(false),true);
            add.Name="RegisterDetectedMod";
            var linkMod=GeneralStyle.Button(T("Vincular a um mod…","Link to a mod…"),()=>choose(true));
            var close=GeneralStyle.Button(T("Fechar","Close"),()=>dialog.Complete(DialogResult.Cancel));
            commands.Controls.AddRange(new Control[]{add,linkMod,close});
            Action update=()=>{
                var item=table.CurrentRow==null?null:table.CurrentRow.Tag as P2PSyncDifference;
                add.Enabled=item!=null;
                linkMod.Enabled=CanLinkInventoryFile(item);
            };
            table.SelectionChanged+=(s,e)=>update();
            table.CellDoubleClick+=(s,e)=>{if(e.RowIndex>=0)choose(false);};
            update();
            layout.Controls.Add(commands,0,2);
            dialog.Content.Controls.Add(layout);
            dialog.CancelButton=close;
            GeneralStyle.Apply(dialog,GeneralStyle.Dark);
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        }
        if(linkExisting)PickInventoryOwner(selected);
        else RegisterDetectedMod(selected);
    }

    List<ModOutputInstallation> FindRecoverableMods(string root,ScanReads reads){
        var records=new ModOutputIndex(data,root).Load().Installations;
        records.AddRange(Snapshot(root).OutputRecoveryCandidates());
        if(reads.Cyberpunk&&projectStore!=null){
            foreach(var source in projectStore.Read(true).Projects.Where(item=>project==null||item.Id!=project.Id)){
                GeneralModState previous;
                try{previous=projectStore.Load(source,true);}
                catch(IOException){continue;}
                catch(UnauthorizedAccessException){continue;}
                if(!InventoryPaths.Equals(previous.Destination,root))continue;
                var imports=previous.CyberpunkImports.ToLookup(pair=>WorkKeyFromReceipt(pair.Key),StringComparer.Ordinal);
                foreach(var entry in previous.List.Mods)
                    foreach(var variant in entry.Variants.Where(option=>imports.Contains(Key(entry,option))))
                        records.Add(new ModOutputInstallation{Entry=entry,Variant=variant.Id,
                            Imports=imports[Key(entry,variant)].ToDictionary(pair=>pair.Key,pair=>pair.Value)});
            }
        }
        var present=new HashSet<string>(state.List.Mods.Select(mod=>mod.Id),StringComparer.Ordinal);
        present.UnionWith(state.SuppressedModRecovery??new List<string>());
        var sources=new HashSet<string>(state.List.Mods.SelectMany(mod=>RecoverySources(mod)),StringComparer.Ordinal);
        var valid=new List<ModOutputInstallation>();
        foreach(var group in records.Where(item=>item!=null&&item.Entry!=null&&item.Entry.Id!=null&&!present.Contains(item.Entry.Id))
            .GroupBy(item=>item.Entry.Id,StringComparer.Ordinal)){
            var matches=group.Where(item=>RetiredPackEntries.CanRecover(item.Entry)&&
                !RecoverySources(item.Entry).Any(sources.Contains)&&VerifyOutputRecovery(item,root,reads))
                .GroupBy(item=>item.Variant+"\n"+String.Join("\n",item.Imports.OrderBy(pair=>pair.Key).Select(pair=>pair.Value))+
                    "\n"+String.Join("\n",RecoverySources(item.Entry))).Select(items=>items.First()).Take(2).ToArray();
            if(matches.Length==1)valid.Add(matches[0]);
        }
        var duplicateIds=valid.SelectMany(item=>RecoverySources(item.Entry).Select(source=>new{source,item.Entry.Id}))
            .GroupBy(item=>item.source,StringComparer.Ordinal).Where(items=>items.Select(item=>item.Id).Distinct().Count()>1)
                .SelectMany(items=>items.Select(item=>item.Id));
        var duplicates=new HashSet<string>(duplicateIds,StringComparer.Ordinal);
        return valid.Where(item=>!duplicates.Contains(item.Entry.Id)).ToList();
    }

    static IEnumerable<string> RecoverySources(GeneralModEntry mod){
        return mod.Variants.SelectMany(variant=>variant.Sources).Select(GeneralNexusLink.Parse).Where(link=>link!=null)
            .Select(link=>link.Game+"/"+link.Mod).Distinct(StringComparer.Ordinal).OrderBy(source=>source,StringComparer.Ordinal);
    }

    bool VerifyOutputRecovery(ModOutputInstallation item,string root,ScanReads reads){
        try{
            var variant=item.Entry.Variants.FirstOrDefault(option=>option.Id==item.Variant);
            if(variant==null||variant.Sources.Count==0)return false;
            string owner=InstalledSnapshot.Owner(item.Entry,variant);
            if(item.SnapshotId!=null)
                return Snapshot(root).ReadInstalledFiles(item.SnapshotId,owner,includeConfigs:false).Count>0;
            if(variant.CyberpunkId!=null){
                if(!reads.Cyberpunk||!catalog.Packages.Any(mod=>mod.Id==variant.CyberpunkId))return false;
                var recipe=ProfileRecipe(new Work{Entry=item.Entry,Variant=variant});
                return reads.Fixed(recipe).IsCorrect;
            }
            return Enumerable.Range(0,variant.Sources.Count).All(index=>{
                string slot=P2PPack.Slot(item.Entry,variant,index);
                if(!reads.Cyberpunk)return Generic(root).Status(slot,null,reads.Hash,variant.InstallRoutes)==GeneralModInstaller.Installed;
                string id;
                return P2PPack.TrySlot(item.Imports,item.Entry,variant,index,out id)&&reads.Imports.Value.Contains(id);
            });
        }catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}
    }

    async Task RecoverModOutputs(string root,ScanReads reads){
        if(projectDraft!=null||!reads.Exists)return;
        try{
            var missing=await Task.Run(()=>FindRecoverableMods(root,reads));
            if(missing.Count==0||IsDisposed||exitApproved)return;
            var next=CopyProjectState();
            if(next.RecoveredModIds==null)next.RecoveredModIds=new List<string>();
            foreach(var item in missing){
                var entry=item.Entry;
                entry.DefaultVariant=item.Variant;
                next.List.Mods.Add(entry);
                if(!next.RecoveredModIds.Contains(entry.Id))next.RecoveredModIds.Add(entry.Id);
                foreach(var binding in item.Imports)next.CyberpunkImports[binding.Key]=binding.Value;
                if(item.SnapshotId!=null)next.SnapshotReceipts[entry.Id+"/"+item.Variant]=item.SnapshotId;
            }
            library.Save(next);
            state.List=next.List;
            state.RecoveredModIds=next.RecoveredModIds;
            state.CyberpunkImports=next.CyberpunkImports;
            state.SnapshotReceipts=next.SnapshotReceipts;
        }catch(IOException ex){OnlineDiagnostics.Failure("inventory.recovery",ex,"Recuperar cadastros com recibo íntegro.");}
         catch(UnauthorizedAccessException ex){OnlineDiagnostics.Failure("inventory.recovery",ex,"Recuperar cadastros com recibo íntegro.");}
    }

    async Task ReconcileModOutputs(string root,IEnumerable<P2PSyncDifference> files){
        try{
            var installed=state.List.Mods.Where(mod=>mod.Variants.Any(variant=>{
                string status;
                return statuses.TryGetValue(Key(mod,variant),out status)&&GeneralStatusPresentation.Kind(status)=="installed";
            })).Select(mod=>mod.Id).ToArray();
            string projectData=data;
            await Task.Run(()=>new ModOutputIndex(projectData,root).Reconcile(files,verifiedOwners:installed));
        }
        catch(IOException ex){OnlineDiagnostics.Failure("inventory.outputs",ex,"Atualizar o índice de arquivos gerados.");}
        catch(UnauthorizedAccessException ex){OnlineDiagnostics.Failure("inventory.outputs",ex,"Atualizar o índice de arquivos gerados.");}
    }

    async Task RememberModOutputBaseline(Work work,string root){
        try{
            var key=Key(work.Entry,work.Variant);
            var record=new ModOutputInstallation{
                Entry=Safe.Json.Deserialize<GeneralModEntry>(Safe.Json.Serialize(work.Entry)),Variant=work.Variant.Id,
                Imports=state.CyberpunkImports.Where(pair=>WorkKeyFromReceipt(pair.Key)==key).ToDictionary(pair=>pair.Key,pair=>pair.Value)
            };
            string projectData=data;
            await Task.Run(()=>{
                bool cyberpunk=IsCyberpunk(root);
                IEnumerable<string> installed;
                if(work.Variant.CyberpunkId!=null){
                    var recipe=ProfileRecipe(work);
                    var routed=GeneralInstallRoutes.FixedPaths(catalog,recipe,work.Variant.InstallRoutes);
                    installed=recipe.Files.Where(file=>!file.UserData).Select(file=>routed[file.Path]);
                }else if(cyberpunk){
                    installed=Cyberpunk(root).ProjectDeletionFiles(new HashSet<string>(record.Imports.Values,StringComparer.Ordinal),true)
                        .Select(file=>file.Path);
                }else{
                    installed=Generic(root).ProjectDeletionFiles(true)
                        .Where(file=>file.OwnerKeys.Any(owner=>WorkKeyFromReceipt(owner)==key)).Select(file=>file.Path);
                }
                var paths=new HashSet<string>(installed.Where(path=>!GeneralInstallRoutes.External(path)),InventoryPaths);
                Func<string,string> scope=cyberpunk?(Func<string,string>)FixedInstaller.ModFolder:InstalledFileOwnership.GenericScope;
                var scopes=paths.Where(path=>!InstalledFileOwnership.Companion(path)).Select(scope).Where(folder=>folder!=null)
                    .Concat(work.Variant.OutputFolders??new List<string>());
                var files=InstalledFileOwnership.Walk(root,scopes).Where(path=>!paths.Contains(path))
                    .Select(path=>new P2PSyncDifference{Path=path,Kind="associated",ModId=work.Entry.Id,LocalInModFolder=true});
                new ModOutputIndex(projectData,root).Reconcile(files,record);
            });
        }catch(IOException ex){OnlineDiagnostics.Failure("inventory.outputs.baseline",ex,"Registrar os arquivos presentes ao instalar o mod.");}
         catch(UnauthorizedAccessException ex){OnlineDiagnostics.Failure("inventory.outputs.baseline",ex,"Registrar os arquivos presentes ao instalar o mod.");}
    }

    void OpenInventoryTree(string selectedPath=null,IWin32Window owner=null){
        if(!CanEditInventory)return;
        var files=DisplayedInventory().Where(file=>file.Kind!="verified-variant").ToArray();
        using(var dialog=new GeneralInventoryTree(state.Destination,files,selectedPath))dialog.ShowDialog(owner??this);
    }

    P2PSyncDifference FocusedInventoryFile(){
        return grid.CurrentRow==null?null:grid.CurrentRow.Tag as P2PSyncDifference;
    }

    void ShowInventoryDetails(P2PSyncDifference item){
        if(item==null||!CanEditInventory)return;
        var files=DisplayedInventory().Where(file=>file.Kind=="found"&&
            InventoryPaths.Equals(InventoryLinkStore.Key(file),InventoryLinkStore.Key(item))).ToArray();
        using(var dialog=new GeneralSyncDifferencesDialog(files,true,showSource:true)){
            dialog.ShowDialog(this);
        }
    }

    P2PSyncDifference[] InventoryRow(string key){
        return DisplayedInventory().Where(file=>file.Kind=="found"&&!file.LocalInModFolder&&InventoryPaths.Equals(InventoryLinkStore.Key(file),key)).ToArray();
    }

    bool CanEditInventory {get{return !working&&!scanning&&associationChoice==null&&!String.IsNullOrWhiteSpace(state.Destination);}}
    static bool CanLinkInventoryFile(P2PSyncDifference file){
        return file!=null&&file.Kind=="found"&&file.VerifiedZip==null&&file.ProvenOwner==null&&!file.LocalInModFolder;
    }
    object InventoryTargetAt(int screenX,int screenY){
        var point=grid.PointToClient(new Point(screenX,screenY));
        int index=grid.HitTest(point.X,point.Y).RowIndex;
        return index<0?null:grid.Rows[index].Tag;
    }
    P2PSyncDifference InventoryRowAt(int screenX,int screenY){
        return InventoryTargetAt(screenX,screenY) as P2PSyncDifference;
    }

    void EnableInventoryDrag(DataGridView table){
        string key=null;
        Point down=Point.Empty;
        table.MouseDown+=(s,e)=>{
            key=null;
            int index=table.HitTest(e.X,e.Y).RowIndex;
            if(e.Button!=MouseButtons.Left||!CanEditInventory||index<0)return;
            var item=table.Rows[index].Tag as P2PSyncDifference;
            if(!CanLinkInventoryFile(item))return;
            key=InventoryLinkStore.Key(item);down=e.Location;
        };
        table.MouseUp+=(s,e)=>key=null;
        table.MouseMove+=(s,e)=>{
            if(key==null||e.Button!=MouseButtons.Left||!CanEditInventory)return;
            var size=SystemInformation.DragSize;
            if(new Rectangle(down.X-size.Width/2,down.Y-size.Height/2,size.Width,size.Height).Contains(e.Location))return;
            inventoryDragKey=key;key=null;
            try{
                using(var scroll=new Timer{Interval=90}){
                    scroll.Tick+=(sender,args)=>ScrollInventoryDrag(table);
                    scroll.Start();
                    table.DoDragDrop(new DataObject(InventoryDragFormat,inventoryDragKey),DragDropEffects.Move);
                }
            }finally{inventoryDragKey=null;}
        };
    }

    DragDropEffects InventoryDropEffect(Control target,DragEventArgs e) {
        if(target!=grid||!CanEditInventory||inventoryDragKey==null||
            !InventoryPaths.Equals(e.Data.GetData(InventoryDragFormat) as string,inventoryDragKey))return DragDropEffects.None;
        var destination=InventoryTargetAt(e.X,e.Y);
        if(destination is string)return DragDropEffects.Move;
        var item=destination as P2PSyncDifference;
        return CanLinkInventoryFile(item)&&!InventoryPaths.Equals(InventoryLinkStore.Key(item),inventoryDragKey)
            ?DragDropEffects.Move:DragDropEffects.None;
    }

    static void ScrollInventoryDrag(DataGridView table){
        if(table.IsDisposed)return;
        var point=table.PointToClient(Cursor.Position);
        if(!table.ClientRectangle.Contains(point))return;
        int start=table.ColumnHeadersVisible?table.ColumnHeadersHeight:0;
        int speed=DragScrollSpeed(point.Y,start,table.ClientSize.Height,table.DeviceDpi);
        int first=table.FirstDisplayedScrollingRowIndex;
        if(speed==0||first<0)return;
        int index=first;
        for(int step=0;step<Math.Abs(speed);step++){
            int next=speed<0?table.Rows.GetPreviousRow(index,DataGridViewElementStates.Visible)
                :table.Rows.GetNextRow(index,DataGridViewElementStates.Visible);
            if(next<0)break;
            index=next;
        }
        if(index!=first)table.FirstDisplayedScrollingRowIndex=index;
    }

    static int DragScrollSpeed(int y,int top,int bottom,int dpi){
        int zone=Math.Min(64*dpi/96,Math.Max(1,(bottom-top)/3));
        if(y<top+zone)return -(1+3*Math.Min(zone,Math.Max(0,zone-(y-top)))/zone);
        if(y>=bottom-zone)return 1+3*Math.Min(zone,Math.Max(0,zone-(bottom-y)))/zone;
        return 0;
    }

    void DropInventoryGroup(DragEventArgs args){
        string source=inventoryDragKey;
        object target=InventoryTargetAt(args.X,args.Y);
        var file=target as P2PSyncDifference;
        if(file!=null){MergeInventoryRows(source,InventoryLinkStore.Key(file));return;}
        var mod=state.List.Mods.FirstOrDefault(item=>item.Id==target as string);
        if(mod==null)return;
        BeginInvoke((Action)(()=>Guard(()=>LinkInventoryToMod(source,mod))));
    }

    async void PickInventoryOwner(P2PSyncDifference item){
        if(!CanEditInventory||!CanLinkInventoryFile(item))return;
        string key=InventoryLinkStore.Key(item),root=state.Destination;
        try{
            var owner=await PickAlternative(null,null,inventoryOwner:true);
            if(owner!=null&&!IsDisposed&&InventoryPaths.Equals(root,state.Destination))LinkInventoryToMod(key,owner);
        }catch(Exception ex){if(!IsDisposed)ShowError(ex);}
    }

    void LinkInventoryToMod(string key,GeneralModEntry mod){
        if(!CanEditInventory||!state.List.Mods.Contains(mod))return;
        var files=InventoryRow(key);
        if(files.Length==0||files.Any(file=>!CanLinkInventoryFile(file)))return;
        string name=files[0].Mod??files[0].Path;
        bool confirmed=testManualInventoryLink!=null?testManualInventoryLink(name,mod.Name):
            GeneralDialog.Message(this,String.Format(T("Vincular {0} a {1}? Esta ação associa os caminhos na lista do projeto.",
                "Link {0} to {1}? This associates their paths in the project list."),name,mod.Name),
                T("Vincular ao mod","Link to mod"),MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.Yes;
        if(!confirmed)return;
        UpdateInventoryLinks(links=>{
            var previous=links.Groups.FirstOrDefault(group=>group.Id==key);
            var paths=files.Select(file=>file.Path);
            if(previous!=null)paths=paths.Concat(previous.Paths);
            InventoryLinkStore.Combine(links,null,mod.Name,mod.Id,paths);
        });

    }

    void MergeInventoryRows(string from,string into) {
        if(!CanEditInventory||InventoryPaths.Equals(from,into))return;
        var source=InventoryRow(from);
        var target=InventoryRow(into);
        if(source.Length==0||target.Length==0)return;
        UpdateInventoryLinks(links=>{
            var previous=links.Groups.FirstOrDefault(group=>group.Id==from);
            var primary=links.Groups.FirstOrDefault(group=>group.Id==into);
            InventoryLinkStore.Combine(links,into,target[0].Mod??target[0].Path,null,
                source.Concat(target).Select(file=>file.Path).Concat(previous==null?Enumerable.Empty<string>():previous.Paths),
                primary==null||primary.Source==null?(previous==null?null:previous.Source):primary.Source);
        });

    }

    InventoryLinkGroup SelectedInventoryLink(){
        var item=grid.CurrentRow==null?null:grid.CurrentRow.Tag as P2PSyncDifference;
        var mod=FocusedEntry();
        return InventoryLinks.Groups.FirstOrDefault(group=>item!=null&&group.Id==item.InventoryGroupId||
            mod!=null&&group.CatalogId==mod.Id);
    }

    async void DropInventorySource(P2PSyncDifference item,string source){
        try{await SaveInventorySource(InventoryLinkStore.Key(item),source);}
        catch(Exception ex){if(!IsDisposed)ShowError(ex);}
    }

    async void SetInventorySource(P2PSyncDifference item) {
        if(item==null||item.Kind!="found"||!CanEditInventory)return;
        try {
            string key=InventoryLinkStore.Key(item);
            var previous=InventoryLinks.Groups.FirstOrDefault(group=>group.Id==key);
            using(var dialog=new GeneralNameDialog(T("Vincular link do Nexus","Link Nexus URL"),previous==null?null:previous.Source,
                fieldLabel:T("Link do mod ou arquivo","Mod or file URL"),maxLength:512)){
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                await SaveInventorySource(key,dialog.ResultName);
            }
        }catch(Exception ex){if(!IsDisposed)ShowError(ex);}
    }

    async Task SaveInventorySource(string key,string value) {
        if(!CanEditInventory)return;
        var link=GeneralNexusLink.Parse(value);
        if(link==null)throw new IOException(T("Cole um link de mod do Nexus.","Paste a Nexus mod link."));
        var files=InventoryRow(key);
        if(files.Length==0||files.Any(file=>file.VerifiedZip!=null||file.ProvenOwner!=null))return;
        string id=null;
        UpdateInventoryLinks(links=>{
            var group=InventoryLinkStore.Combine(links,key,files[0].Mod??files[0].Path,null,files.Select(file=>file.Path),link.Url);
            id=group.Id;
        });
        if(NexusPreviewsAvailable)await ReviewInventoryLinks(id);
    }

    async void VerifyInventoryZip() {
        if(!CanEditInventory||inventoryLoadError!=null)return;
        using(var dialog=new OpenFileDialog{Title=T("Escolher ZIP original do mod","Choose original mod ZIP"),
            Filter="ZIP (*.zip)|*.zip",InitialDirectory=state.DownloadFolder,CheckFileExists=true}){
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            using(var name=new GeneralNameDialog(T("Nome do pacote","Package name"),Path.GetFileNameWithoutExtension(dialog.FileName),
                fieldLabel:T("Nome do mod","Mod name"),maxLength:300)){
                if(name.ShowDialog(this)!=DialogResult.OK)return;
                try{
                    await VerifyInventoryZip(dialog.FileName,name.ResultName);
                }catch(Exception ex){if(!IsDisposed)ShowError(ex);}
            }
        }
    }

    async Task<int> VerifyInventoryZip(string zip,string name) {
        string root=state.Destination;
        var candidates=gameInventory.Where(file=>file.Kind=="found"||file.LocalInModFolder).Select(file=>file.Path).ToArray();
        scanning=true;Busy(true);

        Dictionary<string,InventoryZipLabel> matches;
        try{matches=await Task.Run(()=>InventoryLinkStore.MatchZip(root,zip,name,IsCyberpunk(root),candidates));}
        finally{scanning=false;if(!IsDisposed)Busy(false);}
        if(IsDisposed||!InventoryPaths.Equals(root,state.Destination)||matches.Count==0)return 0;
        UpdateInventoryLinks(links=>{
            foreach(var item in matches)links.ZipLabels[item.Key]=item.Value;
        });
        await Scan();
        return matches.Count;
    }

    bool NexusPreviewsAvailable {get{return testNexusPreviews!=null||!testMode&&GeneralAppPaths.IsDevelopment&&GeneralNexusInfo.HasTestKey;}}

    InventoryPreviewOwner[] InventoryPreviewOwners(string onlyGroup,P2PSyncDifference[] inventory) {
        var owners=new List<InventoryPreviewOwner>();
        foreach(var group in InventoryLinks.Groups.Where(item=>item.CatalogId==null&&item.Source!=null&&(onlyGroup==null||item.Id==onlyGroup))){
            var link=GeneralNexusLink.Parse(group.Source);
            var anchors=inventory.Where(file=>file.Kind=="found"&&file.InventoryGroupId==group.Id).Select(file=>file.Path).ToArray();
            if(link!=null&&anchors.Length>0)
                owners.Add(new InventoryPreviewOwner{Id=group.Id,Name=group.Name,RowId=group.Id,Anchors=anchors,Link=link,Variant=new GeneralModVariant()});
        }
        if(onlyGroup==null)foreach(var mod in state.List.Mods)foreach(var variant in mod.Variants){
            var sources=variant.Sources.Select(GeneralNexusLink.Parse).Where(link=>link!=null).ToList();
            var recipe=catalog.Packages.FirstOrDefault(item=>item.Id==variant.CyberpunkId);
            if(recipe!=null){
                if(recipe.NexusModId>0)sources.Add(new GeneralNexusLink{Game="cyberpunk2077",Mod=recipe.NexusModId,File=recipe.NexusFileId});
                foreach(var asset in NightCityDensity.Assets(recipe)){
                    var link=GeneralNexusLink.Parse(asset.Url);
                    if(link!=null)sources.Add(link);
                    else if(asset.ModId>0)sources.Add(new GeneralNexusLink{Game="cyberpunk2077",Mod=asset.ModId,File=asset.FileId});
                }
            }
            foreach(var link in sources.GroupBy(item=>item.Url,StringComparer.Ordinal).Select(group=>group.First()))
                owners.Add(new InventoryPreviewOwner{Id=mod.Id,Name=mod.Name,CatalogId=mod.Id,Link=link,Variant=variant});
        }
        return owners.Where(owner=>!InventoryLinks.Ignored.Contains(owner.IgnoreKey,StringComparer.Ordinal)).ToArray();
    }

    async Task ReviewInventoryLinks(string onlyGroup=null){
        if(!CanEditInventory||!NexusPreviewsAvailable)return;
        var inventory=DisplayedInventory().ToArray();
        if(!inventory.Any(file=>file.Kind=="found"))return;
        var owners=InventoryPreviewOwners(onlyGroup,inventory);
        if(owners.Length==0)return;
        string root=state.Destination;
        bool cyberpunk=IsCyberpunk(root);

        var read=nexusScan.PreviewSession(testNexusPreviews??GeneralNexusInfo.ReadFilePreviews);
        InventoryLinkSuggestion[] proposals;
        scanning=true;Busy(true);
        try{proposals=await Task.Run(()=>InventoryLinkSuggestions.Find(root,cyberpunk,owners,inventory,read));}
        finally{scanning=false;if(!IsDisposed)Busy(false);}
        if(IsDisposed||!InventoryPaths.Equals(root,state.Destination))return;
        foreach(var proposal in proposals){
            if(InventoryLinks.Ignored.Contains(proposal.Target.IgnoreKey,StringComparer.Ordinal)||InventoryRow(proposal.RowId).Length==0||
                proposal.Target.RowId!=null&&InventoryRow(proposal.Target.RowId).Length==0)continue;
            var choice=testInventoryLinkChoice==null?AskInventoryLink(proposal):testInventoryLinkChoice(proposal);
            if(choice==DialogResult.Ignore)UpdateInventoryLinks(links=>links.Ignored.Add(proposal.Target.IgnoreKey));
            if(choice!=DialogResult.Yes)continue;
            if(proposal.Target.RowId!=null){MergeInventoryRows(proposal.RowId,proposal.Target.RowId);continue;}
            UpdateInventoryLinks(links=>InventoryLinkStore.Combine(links,null,proposal.Target.Name,
                proposal.Target.CatalogId,proposal.Paths));
        }

    }

    DialogResult AskInventoryLink(InventoryLinkSuggestion proposal){
        using(var dialog=new GeneralDialog(T("Agrupar arquivos detectados","Group detected files")){ClientSize=new Size(630,294)}){
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new Padding(20,8,20,20)};
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var message=new TextBox{Text=proposal.Target.Name+"  +  "+proposal.Name+"\r\n\r\n"+
                T("Os caminhos coincidem com a prévia do Nexus. Juntar na visualização?","The paths match the Nexus preview. Group them in the list?"),
                ReadOnly=true,Multiline=true,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,Font=GeneralStyle.Font(12)};
            layout.Controls.Add(new GeneralScrollView(message),0,0);
            var buttons=GeneralTableDialogLayout.Commands();
            var yes=GeneralStyle.Button(T("Sim","Yes"),()=>dialog.Complete(DialogResult.Yes),true);
            var no=GeneralStyle.Button(T("Não","No"),()=>dialog.Complete(DialogResult.No));
            var never=GeneralStyle.Button(T("Não, nunca mais para este mod","No, never for this mod"),()=>dialog.Complete(DialogResult.Ignore));
            buttons.Controls.Add(yes);buttons.Controls.Add(no);buttons.Controls.Add(never);
            dialog.AcceptButton=no;dialog.CancelButton=no;
            layout.Controls.Add(buttons,0,1);dialog.Content.Controls.Add(layout);
            GeneralStyle.Apply(dialog,GeneralStyle.Dark);message.BorderStyle=BorderStyle.None;
            return dialog.ShowDialog(this);
        }
    }
}
