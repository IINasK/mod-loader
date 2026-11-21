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
    string inventoryRoot,inventoryDragKey,verifiedInventoryRoot;
    Dictionary<string,string[]> verifiedInventoryImports=new Dictionary<string,string[]>(StringComparer.Ordinal);
    InventoryLinkData inventoryLinks;
    HashSet<string> changedInventoryAttachments=new HashSet<string>(InventoryPaths);
    string inventoryLoadError;
    Func<InventoryLinkSuggestion,DialogResult> testInventoryLinkChoice;
    Func<string,string,bool> testManualInventoryLink;

    IEnumerable<string> InstalledEditorPaths(GeneralModEntry entry,GeneralModVariant variant,string root){
        if(variant.Sources.Any(GeneralModDownload.IsInstalledSource))return entry.DetectedPaths;
        if(!InventoryPaths.Equals(verifiedInventoryRoot,root))return Enumerable.Empty<string>();
        var paths=new List<string>();
        for(int index=0;index<variant.Sources.Count;index++){
            string id;string[] files;
            if(P2PPack.TrySlot(state.CyberpunkImports,entry,variant,index,out id)&&verifiedInventoryImports.TryGetValue(id,out files))
                paths.AddRange(files);
        }
        return paths.Distinct(InventoryPaths);
    }

    InventoryLinkData InventoryLinks {
        get {
            if(inventoryLinks!=null&&InventoryPaths.Equals(inventoryRoot,state.Destination))return inventoryLinks;
            if(!InventoryPaths.Equals(inventoryRoot,state.Destination))changedInventoryAttachments.Clear();
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
        return InventoryLinkStore.View(gameInventory,InventoryLinks,state.List.Mods,changedInventoryAttachments)
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
                var generic=reads.Generic.Value;
                if(!reads.Cyberpunk||generic.HasSnapshotRecord(slot))
                    return generic.Status(slot,null,reads.Hash,variant.InstallRoutes)==GeneralModInstaller.Installed;
                string id;
                return P2PPack.TrySlot(item.Imports,item.Entry,variant,index,out id)&&reads.Imports.Value.Contains(id);
            });
        }catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}
    }


    static bool IsLocalInventoryEntry(GeneralModEntry mod){
        return mod!=null&&mod.Variants.Count==1&&mod.Variants[0].Sources.Count==1&&
            GeneralModDownload.IsInstalledSource(mod.Variants[0].Sources[0]);
    }

    // Resolve against the edition-independent installation receipts. Peer projects only
    // supply the original source metadata, and can never add entries to this project.
    Dictionary<string,ModOutputInstallation> FindDetectedInventorySources(string root,ScanReads reads,
        Dictionary<string,HashSet<string>> detected){
        var used=new HashSet<string>(state.CyberpunkImports.Values,StringComparer.Ordinal);
        var suppressed=new HashSet<string>(state.SuppressedModRecovery,StringComparer.Ordinal);
        string destination=Path.GetFullPath(root).TrimEnd('\\');
        var sources=ReadUpdateContexts(root).Skip(1).Where(context=>context.State!=null&&
            !String.IsNullOrWhiteSpace(context.State.Destination)&&
            InventoryPaths.Equals(Path.GetFullPath(context.State.Destination).TrimEnd('\\'),destination))
            .SelectMany(context=>context.State.List.Mods.Where(entry=>!suppressed.Contains(entry.Id)).SelectMany(entry=>entry.Variants
                .Where(variant=>variant.CyberpunkId==null&&variant.Sources.Count==1&&
                    (GeneralNexusLink.Parse(variant.Sources[0])!=null||GithubModUpdateSession.Supports(variant.Sources[0])))
                .Select(variant=>new ModOutputInstallation{Entry=entry,Variant=variant.Id,Imports=context.State.CyberpunkImports})))
            .Select(record=>{
                string id;
                return P2PPack.TrySlot(record.Imports,record.Entry,record.Entry.Variants.Single(variant=>variant.Id==record.Variant),0,out id)&&
                    !used.Contains(id)?new{Id=id,Record=record}:null;
            }).Where(candidate=>candidate!=null).ToArray();
        var result=new Dictionary<string,ModOutputInstallation>(StringComparer.Ordinal);
        if(sources.Length==0||reads.Imports.Value.Count==0)return result;
        var candidates=sources.Where(source=>reads.VerifiedImportPaths.ContainsKey(source.Id))
            .GroupBy(source=>source.Id,StringComparer.Ordinal).Select(group=>{
                var matches=group.GroupBy(source=>source.Record.Entry.Variants.Single(variant=>variant.Id==source.Record.Variant).Sources[0],
                    StringComparer.Ordinal).Take(2).ToArray();
                return matches.Length==1?matches[0].First():null;
            }).Where(source=>source!=null).ToArray();
        var matchesByEntry=candidates.SelectMany(source=>detected.Where(entry=>entry.Value.IsSupersetOf(reads.VerifiedImportPaths[source.Id]))
            .Select(entry=>new{Entry=entry.Key,Source=source})).ToArray();
        var claimed=matchesByEntry.ToLookup(match=>match.Source.Id,StringComparer.Ordinal);
        foreach(var matches in matchesByEntry.ToLookup(match=>match.Entry,StringComparer.Ordinal)){
            var first=matches.First();
            if(matches.Count()==1&&claimed[first.Source.Id].Count()==1)result.Add(matches.Key,first.Source.Record);
        }
        return result;
    }

    async Task RestoreDetectedInventorySources(string root,ScanReads reads){
        var entries=state.List.Mods.Where(IsLocalInventoryEntry).ToArray();
        if(entries.Length==0)return;
        var entryIds=new HashSet<string>(entries.Select(entry=>entry.Id),StringComparer.Ordinal);
        var attachments=InventoryLinks.Attachments.Where(file=>file.Hash!=null&&file.Owners.Length==1&&
            entryIds.Contains(file.Owners[0])).ToArray();
        var matches=await Task.Run(()=>{
            var attached=attachments.Where(file=>MatchesInventoryAttachment(root,file,reads.Hash))
                .ToLookup(file=>file.Owners[0],file=>file.Path,StringComparer.Ordinal);
            var detected=entries.ToDictionary(entry=>entry.Id,entry=>new HashSet<string>(
                entry.DetectedPaths.Concat(attached[entry.Id]),InventoryPaths),StringComparer.Ordinal);
            return detected.Values.All(paths=>paths.Count==0)?new Dictionary<string,ModOutputInstallation>(StringComparer.Ordinal):
                FindDetectedInventorySources(root,reads,detected);
        });
        if(matches.Count==0||IsDisposed||exitApproved)return;
        var next=CopyProjectState();
        foreach(var entry in next.List.Mods.Where(mod=>matches.ContainsKey(mod.Id))){
            var record=matches[entry.Id];
            var original=record.Entry.Variants.Single(variant=>variant.Id==record.Variant);
            string import;
            if(!P2PPack.TrySlot(record.Imports,record.Entry,original,0,out import))continue;
            var presets=next.List.Presets.Where(preset=>preset.Configurations.ContainsKey(entry.Id)).ToArray();
            if(presets.Select(preset=>preset.Configurations[entry.Id]).Any(config=>!IsLocalInventoryEntry(config)||
                !new HashSet<string>(config.DetectedPaths,InventoryPaths).SetEquals(entry.DetectedPaths)))continue;
            ApplyDetectedSource(entry,original,record.Entry.Category,next.List.Categories);
            foreach(var preset in presets)
                ApplyDetectedSource(preset.Configurations[entry.Id],original,record.Entry.Category,preset.Categories??next.List.Categories);
            next.CyberpunkImports[P2PPack.Slot(entry,entry.Variants[0],0)]=import;
        }
        if(next.CyberpunkImports.Count==state.CyberpunkImports.Count)return;
        library.Save(next);
        state.List=next.List;
        state.CyberpunkImports=next.CyberpunkImports;
    }

    static void ApplyDetectedSource(GeneralModEntry entry,GeneralModVariant original,string category,List<string> categories){
        var variant=entry.Variants[0];
        variant.Sources=new List<string>(original.Sources);
        variant.References=new List<string>(original.References);
        variant.SourceInfo=GeneralPresetProfiles.Copy(original.SourceInfo);
        variant.InstallRoutes=GeneralPresetProfiles.Copy(original.InstallRoutes);
        if(String.IsNullOrEmpty(entry.Category)&&category!=null)
            entry.Category=categories.FirstOrDefault(value=>InventoryPaths.Equals(value,category));
        entry.DetectedPaths.Clear();
    }

    async Task RecoverModOutputs(string root,ScanReads reads){
        if(projectDraft!=null||!reads.Exists)return;
        try{
            if(reads.Cyberpunk&&!BuiltInProject)await RestoreDetectedInventorySources(root,reads);
            if(IsDisposed||exitApproved)return;
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
                }else{
                    var generic=Generic(root);
                    var sourceKeys=new HashSet<string>(StringComparer.Ordinal);
                    var importIds=new HashSet<string>(StringComparer.Ordinal);
                    for(int index=0;index<work.Variant.Sources.Count;index++){
                        string slot=P2PPack.Slot(work.Entry,work.Variant,index);
                        string legacy=P2PPack.LegacySlot(work.Entry,work.Variant,index);
                        if(generic.HasSnapshotRecord(slot))sourceKeys.Add(slot);
                        else if(legacy!=null&&generic.HasSnapshotRecord(legacy))sourceKeys.Add(legacy);
                        else if(cyberpunk){
                            string id;
                            if(P2PPack.TrySlot(record.Imports,work.Entry,work.Variant,index,out id))importIds.Add(id);
                        }
                    }
                    installed=generic.ProjectDeletionFiles(true)
                        .Where(file=>file.OwnerKeys.Any(sourceKeys.Contains)).Select(file=>file.Path).ToList();
                    if(cyberpunk&&importIds.Count>0)
                        installed=installed.Concat(Cyberpunk(root).ProjectDeletionFiles(importIds,true).Select(file=>file.Path));
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

    async void OpenInventoryTree(string selectedPath=null,IWin32Window owner=null){
        var window=owner as Form??this;
        if(GeneralDialog.FocusOpen<GeneralInventoryTree>(window)||!CanEditInventory)return;
        var files=DisplayedInventory().Where(file=>file.Kind!="verified-variant").ToArray();
        using(var dialog=new GeneralInventoryTree(state.Destination,files,selectedPath,CanSetInventoryOrigin)){
            if(await dialog.ShowModelessAsync(window)==DialogResult.Retry&&!IsDisposed)ShowInventoryOrigin(dialog.OriginFile);
        }
    }

    P2PSyncDifference FocusedInventoryFile(){
        return grid.CurrentRow==null?null:grid.CurrentRow.Tag as P2PSyncDifference;
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

    static bool CanSetInventoryOrigin(P2PSyncDifference file){
        return file!=null&&!String.IsNullOrEmpty(file.Path)&&!GeneralInstallRoutes.External(file.Path)&&
            (file.Kind=="found"||file.Kind=="unlinked");
    }

    static bool HasVerifiedInventoryOrigin(P2PSyncDifference file){
        return file.ProvenOwner!=null||file.VerifiedZip!=null||file.ModId!=null||
            file.LocalInModFolder&&file.InventoryGroupId!=null;
    }

    P2PSyncDifference[] InventoryOriginFiles(P2PSyncDifference item){
        string key=InventoryLinkStore.Key(item);
        return DisplayedInventory().Where(file=>CanSetInventoryOrigin(file)&&
            (item.Kind=="unlinked"&&item.InventoryGroupId==null?InventoryPaths.Equals(file.Path,item.Path):
                InventoryPaths.Equals(InventoryLinkStore.Key(file),key))).ToArray();
    }

    GeneralModEntry VerifiedInventoryParent(IEnumerable<P2PSyncDifference> files){
        var proven=files.Where(HasVerifiedInventoryOrigin).ToArray();
        if(proven.Length==0)return null;
        var direct=new HashSet<string>(proven.SelectMany(file=>new[]{file.ModId,file.ProvenOwner,
            file.InventoryGroupId!=null&&file.InventoryGroupId.StartsWith("owner:",StringComparison.Ordinal)
                ?file.InventoryGroupId.Substring(6):null}).Where(id=>id!=null),StringComparer.Ordinal);
        var paths=new HashSet<string>(proven.Select(file=>file.Path),InventoryPaths);
        var matches=state.List.Mods.Where(mod=>direct.Contains(mod.Id)||
            mod.Variants.Any(variant=>variant.CyberpunkId!=null&&direct.Contains(variant.CyberpunkId))||
            mod.DetectedPaths.Any(paths.Contains)).Take(2).ToArray();
        return matches.Length==1?matches[0]:null;
    }

    async void ShowInventoryOrigin(P2PSyncDifference item){
        if(!CanEditInventory||!CanSetInventoryOrigin(item))return;
        var files=InventoryOriginFiles(item);
        if(files.Length==0)return;
        string name=item.Mod??Path.GetFileName(item.Path);
        var parent=VerifiedInventoryParent(files);
        string folder=InventoryFolder(item.Path);
        bool automatic=parent!=null;
        string context=automatic?String.Format(T("Mod identificado: {0}","Identified mod: {0}"),parent.Name):
            files.Any(HasVerifiedInventoryOrigin)?T("Origem confirmada. Escolha um mod deste projeto para receber os arquivos.",
                "Origin verified. Choose a mod in this project to receive the files."):
                T("Escolha a qual mod pertencem estes arquivos.","Choose which mod these files belong to.");
        string caption=T("Definir origem","Set origin");
        var answer=GeneralDialog.Message(this,name+"\r\n"+folder+"\r\n\r\n"+context,caption,
            automatic?MessageBoxButtons.YesNoCancel:MessageBoxButtons.YesNo,MessageBoxIcon.Question,
            yesLabel:automatic?T("Vínculo automático","Automatic link"):T("Associar a um mod","Link to a mod"),
            noLabel:automatic?T("Vínculo manual","Manual link"):T("Cancelar","Cancel"),modNames:new[]{name,parent==null?null:parent.Name}.Where(value=>value!=null));
        if(!automatic&&answer==DialogResult.Yes||automatic&&answer==DialogResult.No)PickInventoryOwner(item);
        else if(automatic&&answer==DialogResult.Yes){
            try{await SaveInventoryAttachments(files,new[]{parent});}
            catch(Exception ex){if(!IsDisposed)ShowError(ex);}
        }
    }

    string InventoryFolder(string path){
        var folder=IsCyberpunk(state.Destination)?FixedInstaller.ModFolder(path):InstalledFileOwnership.GenericScope(path);
        return folder??(path.LastIndexOf('/')>0?path.Substring(0,path.LastIndexOf('/')):path);
    }


    bool CanMergeDetectedEntry(GeneralModEntry source){
        if(BuiltInProject||!CanEditInventory||!IsLocalInventoryEntry(source)||source.AlternativeGroup!=null||
            source.Variants[0].InstallRoutes.Count>0||state.List.Folders.Any(folder=>folder.ModIds.Contains(source.Id)))return false;
        string prefix=source.Id+"/";
        if(state.SnapshotReceipts.Keys.Any(key=>key.StartsWith(prefix,StringComparison.Ordinal))||
            state.CyberpunkImports.Keys.Any(key=>key.StartsWith(prefix,StringComparison.Ordinal))||
            state.List.Presets.Any(preset=>preset.Configurations.Values.Any(mod=>
                mod.Variants.Any(variant=>variant.RequiredMods.Contains(source.Id))))||
            state.List.Presets.Any(preset=>preset.Configurations.ContainsKey(source.Id)&&
                !IsLocalInventoryEntry(preset.Configurations[source.Id])))return false;
        var paths=new HashSet<string>(InventoryLinks.Attachments.Where(file=>file.Owners.Contains(source.Id))
            .Select(file=>file.Path),InventoryPaths);
        return paths.Count>0&&source.DetectedPaths.All(paths.Contains);
    }

    async void MergeDetectedEntry(GeneralModEntry source){
        if(!CanMergeDetectedEntry(source))return;
        try{
            string id=source.Id;
            if(!ConfirmProjectNavigation())return;
            source=state.List.Mods.SingleOrDefault(mod=>mod.Id==id);
            if(!CanMergeDetectedEntry(source))return;
            var owners=await PickAssociation(id,null,false,source.Name);
            if(owners==null||IsDisposed||exitApproved||!CanMergeDetectedEntry(source))return;
            ApplyDetectedEntryLink(source,owners);
            await Scan();
        }catch(Exception ex){if(!IsDisposed)ShowError(ex);}
    }

    void ApplyDetectedEntryLink(GeneralModEntry source,GeneralModEntry[] owners){
        if(!CanMergeDetectedEntry(source)||!state.List.Mods.Contains(source)||owners.Length==0||
            owners.Any(owner=>owner.Id==source.Id||!state.List.Mods.Contains(owner)))return;
        var next=CopyProjectState();
        GeneralPresetProfiles.Remove(next,source.Id,true);
        if(!next.SuppressedModRecovery.Contains(source.Id))next.SuppressedModRecovery.Add(source.Id);
        next.RecoveredModIds.Remove(source.Id);
        var store=new InventoryLinkStore(data,state.Destination);
        var prior=InventoryLinks;
        UpdateInventoryLinks(links=>{
            foreach(var file in links.Attachments.Where(file=>file.Owners.Contains(source.Id))){
                file.Owners=file.Owners.Where(id=>id!=source.Id).Concat(owners.Select(owner=>owner.Id))
                    .Distinct(StringComparer.Ordinal).ToArray();
            }
        },redraw:false);
        try{library.Save(next);}
        catch{
            store.Save(prior);
            inventoryLinks=prior;
            throw;
        }
        state.List=next.List;
        state.ChosenVariants=next.ChosenVariants;
        state.SuppressedModRecovery=next.SuppressedModRecovery;
        state.RecoveredModIds=next.RecoveredModIds;
        marked.Remove(source.Id);
    }

    async void PickInventoryOwner(P2PSyncDifference item){
        if(!CanEditInventory||!CanSetInventoryOrigin(item))return;
        var files=InventoryOriginFiles(item);
        if(files.Length==0)return;
        var before=state;
        try{
            var owners=await PickAssociation(null,null,false,item.Mod??Path.GetFileName(item.Path));
            if(owners!=null&&!IsDisposed&&!exitApproved&&Object.ReferenceEquals(before,state))
                await SaveInventoryAttachments(files,owners);
        }catch(Exception ex){if(!IsDisposed)ShowError(ex);}
    }

    async Task SaveInventoryAttachments(P2PSyncDifference[] files,GeneralModEntry[] owners){
        if(!CanEditInventory||owners.Length==0)return;
        string root=state.Destination;
        bool cyberpunk=IsCyberpunk(root);
        string[] ids=owners.Select(owner=>owner.Id).Distinct(StringComparer.Ordinal).ToArray();
        var paths=files.Select(file=>file.Path).Distinct(InventoryPaths).ToArray();
        scanning=true;Busy(true);
        try{
            var attachments=await Task.Run(()=>paths.Select(path=>{
                string target=Safe.In(root,path);
                Safe.NoLinks(target);
                if(!File.Exists(target))return null;
                bool managed=InventoryLinkStore.CanManageAttachment(path)&&(!cyberpunk||FixedInstaller.InventoryRegistrationPath(path));
                return new InventoryAttachment{Path=path,Owners=ids,Hash=managed?Safe.Hash(target):null};
            }).Where(attachment=>attachment!=null).ToArray());
            if(IsDisposed||exitApproved||!InventoryPaths.Equals(root,state.Destination)||attachments.Length==0)return;
            UpdateInventoryLinks(links=>{
                var linked=new HashSet<string>(attachments.Select(file=>file.Path),InventoryPaths);
                foreach(var group in links.Groups.ToArray()){
                    group.RemovePaths(linked);
                    if(group.Paths.Count==0)links.Groups.Remove(group);
                }
                links.Attachments.RemoveAll(file=>linked.Contains(file.Path));
                links.Attachments.AddRange(attachments);
            },redraw:false);
            changedInventoryAttachments.ExceptWith(attachments.Select(file=>file.Path));
            DrawRows();
        }finally{scanning=false;if(!IsDisposed)Busy(false);}
    }

    async void LinkInventoryToMod(string key,GeneralModEntry mod){
        if(!CanEditInventory||!state.List.Mods.Contains(mod))return;
        var files=InventoryRow(key);
        if(files.Length==0||files.Any(file=>!CanLinkInventoryFile(file)))return;
        string name=files[0].Mod??files[0].Path;
        bool confirmed=testManualInventoryLink!=null?testManualInventoryLink(name,mod.Name):
            GeneralDialog.Message(this,String.Format(T("Vincular {0} a {1}? Esta ação associa os caminhos na lista do projeto.",
                "Link {0} to {1}? This associates their paths in the project list."),name,mod.Name),
                T("Vincular ao mod","Link to mod"),MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.Yes;
        if(!confirmed)return;
        try{await SaveInventoryAttachments(files,new[]{mod});}
        catch(Exception ex){if(!IsDisposed)ShowError(ex);}
    }

    List<InventoryLinkMember> InventoryGroupMembers(InventoryLinkGroup group){
        if(group==null)return new List<InventoryLinkMember>();
        if(group.Linked.Count>0)return group.Linked;
        // Older groupings saved only the combined destinations. Recover boundaries
        // when the scan still identifies the original destination row by name.
        var paths=new HashSet<string>(group.Paths,InventoryPaths);
        var detected=gameInventory.Where(file=>file.Kind=="found"&&!String.IsNullOrEmpty(file.Mod)&&paths.Contains(file.Path))
            .GroupBy(file=>file.Path,InventoryPaths)
            .Where(files=>files.Select(file=>file.Mod).Distinct(InventoryPaths).Count()==1).Select(files=>files.First())
            .GroupBy(file=>file.Mod,InventoryPaths).ToArray();
        if(!detected.Any(part=>InventoryPaths.Equals(part.Key,group.Name)))return new List<InventoryLinkMember>();
        return detected.Where(part=>!InventoryPaths.Equals(part.Key,group.Name))
            .Select(part=>new InventoryLinkMember{Name=part.Key,Paths=part.Select(file=>file.Path)
                .Distinct(InventoryPaths).OrderBy(path=>path,InventoryPaths).ToList()}).ToList();
    }

    void SaveInventoryUnlinks(string groupId,string originalGroup,IEnumerable<InventoryLinkMember> members,Action saveProject=null){
        if(!CanEditInventory)throw new IOException(T("O inventário está em uso.","The inventory is busy."));
        var store=new InventoryLinkStore(data,state.Destination);
        var previous=store.Load();
        var next=GeneralPresetProfiles.Copy(previous);
        var group=next.Groups.FirstOrDefault(item=>item.Id==groupId&&item.CatalogId==null);
        if(group==null||Safe.Json.Serialize(group)!=originalGroup)
            throw new IOException(T("O grupo mudou. Reabra os detalhes para editar.",
                "The group changed. Reopen its details to edit."));
        group.Linked=InventoryGroupMembers(group).Select(part=>new InventoryLinkMember{
            Name=part.Name,Source=part.Source,Paths=part.Paths.ToList()}).ToList();
        foreach(var member in members)
            InventoryLinkStore.Combine(next,null,member.Name,null,member.Paths,member.Source);
        store.Save(next);
        try{if(saveProject!=null)saveProject();}
        catch{store.Save(previous);throw;}
        inventoryLinks=next;
    }

    void MergeInventoryRows(string from,string into) {
        if(!CanEditInventory||InventoryPaths.Equals(from,into))return;
        var source=InventoryRow(from);
        var target=InventoryRow(into);
        if(source.Length==0||target.Length==0)return;
        UpdateInventoryLinks(links=>{
            var previous=links.Groups.FirstOrDefault(group=>group.Id==from);
            var primary=links.Groups.FirstOrDefault(group=>group.Id==into);
            if(primary!=null)primary.Linked=InventoryGroupMembers(primary);
            var members=InventoryGroupMembers(previous).Select(part=>new InventoryLinkMember{
                Name=part.Name,Source=part.Source,Paths=part.Paths.ToList()}).ToList();
            var sourcePaths=previous==null?source.Select(file=>file.Path).ToList():previous.Paths.ToList();
            var linkedPaths=new HashSet<string>(members.SelectMany(part=>part.Paths),InventoryPaths);
            members.Insert(0,new InventoryLinkMember{Name=source[0].Mod??source[0].Path,Source=previous==null?null:previous.Source,
                Paths=sourcePaths.Where(path=>!linkedPaths.Contains(path)).ToList()});
            var merged=InventoryLinkStore.Combine(links,into,target[0].Mod??target[0].Path,null,
                sourcePaths.Concat(target.Select(file=>file.Path)),primary!=null&&primary.Source!=null?primary.Source:
                    previous==null?null:previous.Source);
            merged.Linked.AddRange(members);
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
