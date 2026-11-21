using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    void ToggleCacheMenu(string folder){
        if(working||scanning)return;
        menus.Toggle(this,storageSize,()=>new GeneralActionsMenu(cacheMenu:true),dialog=>Guard(()=>{
            string action=((GeneralActionsMenu)dialog).ActionId;
            if(action=="open-cache")storageSize.OpenFolder(folder);
            if(action=="p2p-temp")OpenP2PTemporaryFiles();
        }));
    }

    void OpenP2PTemporaryFiles(){
        string folder=P2PService.TransferStore;
        if(p2p!=null){
            var paths=p2p.SnapshotSessions().Where(session=>!session.Sender&&!session.Discarded).Select(p2p.Folder).Where(Directory.Exists).ToArray();
            if(paths.Length==1)folder=paths[0];
        }
        Safe.NoLinks(folder);
        if(!Directory.Exists(folder)){
            GeneralDialog.Message(this,T("Nenhum arquivo temporário P2P disponível.","No P2P temporary files available."),
                T("Temporários P2P","P2P temporary files"));
            return;
        }
        storageSize.OpenFolder(folder);
    }

    void ToggleListMenu(){
        if(working||scanning)return;
        menus.Toggle(this,optionsButton,()=>new GeneralActionsMenu(BuiltInProject,
            savePresetItems:DraftSelectionChanged),dialog=>Guard(()=>{
            string action=((GeneralActionsMenu)dialog).ActionId;
            if(action=="new")ProjectCommand("new");else if(action=="open")BackToProjects();
            else if(!BuiltInProject&&action=="import")ProjectCommand("import");else if(!BuiltInProject&&action=="export")ProjectCommand("export");
            else if(action=="save-menu")OpenSaveProjectMenu();
            else if(action=="details")ShowProjectDetails();
            else if(action=="share")ShowP2P();else if(action=="options")ShowOptions();
            else if(action=="save-preset-items")SavePresetSelection(false);
        }));
    }
    void OpenSaveProjectMenu(){
        menus.Toggle(this,optionsButton,()=>new GeneralActionsMenu(saveMenu:true),dialog=>Guard(()=>{
            switch(((GeneralActionsMenu)dialog).ActionId){
                case "save-project":
                    if(DraftNameChanged)SavePresetName(false);else SaveProjectEdits();
                    break;
                case "save-increment":SaveProjectIncrement();break;
                case "clone":ProjectCommand("clone");break;
            }
        }));
    }

    void SaveProjectIncrement(){
        if(projectStore==null||project==null)return;
        var revisionState=CopyProjectState();
        if(DraftNameChanged){
            string title=(draftName??"").Trim();
            if(title.Length==0||title.Length>120||title.Any(Char.IsControl))
                throw new IOException(T("Informe um nome válido para o preset.","Enter a valid preset name."));
            revisionState.List.Presets.Single(preset=>preset.Id==revisionState.SelectedPreset).Name=title;
        }
        string prefix=project.Name.Substring(0,Math.Min(98,project.Name.Length))+" · ";
        var names=new HashSet<string>(projectStore.Read().Projects.Select(item=>item.Name),StringComparer.CurrentCultureIgnoreCase);
        int revision=1;string name;
        do{name=prefix+(revision++).ToString("00");}while(names.Contains(name));
        projectStore.Duplicate(project,name,catalog,revisionState);
        GeneralDialog.Message(this,name,T("Incremento salvo","Revision saved"));
    }

    async void Replace(GeneralModList list){await ReplaceList(list);}
    async Task<bool> ReplaceList(GeneralModList list,bool clearBindings=false,string snapshotReceipt=null,string newDestination=null){
        if(working||scanning||list==null||!ConfirmProjectNavigation())return false;
        if(state.List.Mods.Count!=0&&GeneralDialog.Message(this,T("Trocar a lista atual? Exporte primeiro se quiser guardar uma cópia. Os arquivos instalados permanecem no lugar.","Replace the current list? Export it first to keep a copy. Installed files will stay in place."),T("Trocar lista","Replace list"),MessageBoxButtons.OKCancel,MessageBoxIcon.Question)!=DialogResult.OK)return false;
        try{
            GeneralModLibrary.RestoreKnownMappings(list,seed);state.List=list;state.SelectedPreset=list.Presets.FirstOrDefault()==null?null:list.Presets[0].Id;state.ChosenVariants.Clear();state.BodyChoice=null;state.ReceivedPreset=null;state.AppliedPreset=null;state.AppliedPresetFingerprint=null;GeneralPresetProfiles.Project(state);marked.Clear();statuses.Clear();statusDetails.Clear();progress.Clear();ResetColumnFilters();hasPresetDraft=false;
            if(clearBindings){
                foreach(string key in state.ResolvedDownloads.Keys.Where(key=>list.Mods.Any(mod=>key.StartsWith(mod.Id+"/",StringComparison.Ordinal))).ToArray())state.ResolvedDownloads.Remove(key);
                foreach(string key in state.CyberpunkImports.Keys.Where(key=>list.Mods.Any(mod=>key.StartsWith(mod.Id+"/",StringComparison.Ordinal))).ToArray())state.CyberpunkImports.Remove(key);
                foreach(string key in state.SnapshotReceipts.Keys.Where(key=>list.Mods.Any(mod=>key.StartsWith(mod.Id+"/",StringComparison.Ordinal))).ToArray())state.SnapshotReceipts.Remove(key);
            }
            if(snapshotReceipt!=null){
                var manifest=new InstalledSnapshotInstaller(newDestination??state.Destination,data,catalog,testMode).ReadManifest(snapshotReceipt);
                GeneralProjectStore.BindReceivedVariants(state,manifest,snapshotReceipt);
                state.ReceivedPreset=state.SelectedPreset;
            }
            if(newDestination!=null)state.Destination=newDestination;
            Persist();RebuildPresets();ReadPresetPresentation();await Scan();if(!IsDisposed)AdoptReceivedPreset();
            OnlineDiagnostics.Record("project.list.replaced",null,"Lista importada.",new{Count=list.Mods.Count});
            return true;
        }catch(Exception ex){ShowError(ex);return false;}
    }
    async void EditPreset(bool create){
        if(GeneralDialog.FocusOpen<GeneralPresetEditor>(this)||working||scanning)return;
        try{
            var currentPreset=create?null:state.List.Presets.FirstOrDefault(p=>p.Id==state.SelectedPreset);
            if(currentPreset==null)create=true;
            var item=currentPreset??new GeneralModPreset{Id="preset-"+Guid.NewGuid().ToString("N"),Name=T("Novo preset","New preset")};
            using(var dialog=new GeneralPresetEditor(item,state.List.Mods,item.ModIds)){
                if(await dialog.ShowModelessAsync(this)!=DialogResult.OK||IsDisposed||exitApproved||working||scanning||
                    !create&&!state.List.Presets.Contains(item))return;
                BeginProjectEdit();
                if(create)state.List.Presets.Add(dialog.Result);else state.List.Presets[state.List.Presets.IndexOf(item)]=dialog.Result;
                state.SelectedPreset=dialog.Result.Id;FinishProjectEdit();RebuildPresets();
            }
        }catch(Exception ex){ShowError(ex);}
    }
    void DeletePreset(int index){
        if(working||scanning||state.List.Presets.Count<=1||index<0||index>=state.List.Presets.Count)return;
        var item=state.List.Presets[index];
        if(GeneralDialog.Message(this,T("Excluir este preset? Os mods permanecem na lista.","Delete this preset? Mods will remain in the list.")+"\n"+item.Name,T("Excluir preset","Delete preset"),MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
        BeginProjectEdit();state.List.Presets.Remove(item);GeneralPresetProfiles.Project(state);FinishProjectEdit();RebuildPresets();
    }
    async void EditCategory(string category,bool create){
        if(GeneralDialog.FocusOpen<GeneralCategoryDetails>(this)||working||scanning)return;
        string presetId=state.SelectedPreset;
        try{
            string name=create?"":category??UncatName(),description=null;
            if(!create&&category==null)description=state.List.UncategorizedDescription;
            else if(category!=null)state.List.CategoryDescriptions.TryGetValue(category,out description);
            using(var dialog=new GeneralCategoryDetails(name,description,!create&&category==null,!create&&category!=null)){
                if(await dialog.ShowModelessAsync(this)!=DialogResult.OK||IsDisposed||exitApproved||working||scanning||
                    presetId!=state.SelectedPreset||!create&&category!=null&&!state.List.Categories.Contains(category))return;
                if(dialog.DeleteRequested){
                    RequestDeleteCategory(category);return;
                }
                string renamed=dialog.ResultName;
                string existing=state.List.Categories.FirstOrDefault(c=>String.Equals(c,renamed,StringComparison.OrdinalIgnoreCase));
                if(create&&existing!=null&&ActivePreset!=null){
                    BeginProjectEdit();
                    if(!ActivePreset.Categories.Contains(existing,StringComparer.OrdinalIgnoreCase))ActivePreset.Categories.Add(existing);
                    if(categoryFilters!=null)categoryFilters.Add(existing);FinishProjectEdit();DrawRows();return;
                }
                if(state.List.Categories.Any(c=>!String.Equals(c,category,StringComparison.OrdinalIgnoreCase)&&String.Equals(c,renamed,StringComparison.OrdinalIgnoreCase)))throw new IOException(T("Já existe uma categoria com esse nome.","A category with this name already exists."));
                BeginProjectEdit();if(create){state.List.Categories.Add(renamed);state.List.CategoryDescriptions[renamed]=dialog.ResultDescription;if(ActivePreset!=null)ActivePreset.Categories.Add(renamed);}
                else if(category==null){state.List.UncategorizedName=renamed;state.List.UncategorizedDescription=dialog.ResultDescription;}
                else RenameCategory(category,renamed,dialog.ResultDescription);
                if(create&&categoryFilters!=null)categoryFilters.Add(renamed);
                FinishProjectEdit();DrawRows();
            }
        }catch(Exception ex){ShowError(ex);}
    }
    void RenameCategory(string category,string renamed,string description){
        state.List.Categories[state.List.Categories.IndexOf(category)]=renamed;
        foreach(var preset in state.List.Presets)
            for(int i=0;i<preset.Categories.Count;i++)
                if(String.Equals(preset.Categories[i],category,StringComparison.OrdinalIgnoreCase))preset.Categories[i]=renamed;
        var entries=state.List.Mods.Concat(state.List.Presets.SelectMany(p=>p.Configurations.Values)).ToArray();
        foreach(var mod in entries.Where(item=>String.Equals(item.Category,category,StringComparison.OrdinalIgnoreCase)))
            mod.Category=renamed;
        foreach(var option in entries.SelectMany(mod=>mod.Variants).SelectMany(variant=>variant.WithNaturalOption()))
            if(String.Equals(option.Category,category,StringComparison.OrdinalIgnoreCase))option.Category=renamed;
        state.List.CategoryDescriptions.Remove(category);
        if(description!=null)state.List.CategoryDescriptions[renamed]=description;
        if(categoryFilters!=null&&categoryFilters.Remove(category))categoryFilters.Add(renamed);
    }
    void OpenDownloads(){
        if(!Directory.Exists(state.DownloadFolder))throw new IOException(T("A pasta de downloads não está disponível. Escolha outra em Opções.","The download folder is unavailable. Choose another one in Options."));
        Safe.NoLinks(state.DownloadFolder);Process.Start(new ProcessStartInfo(state.DownloadFolder){UseShellExecute=true});
    }
    bool SetDestination(string destination){
        if(String.Equals(state.Destination??"",destination,StringComparison.OrdinalIgnoreCase))return false;
        state.Destination=destination;state.ReceivedPreset=null;state.AppliedPreset=null;state.AppliedPresetFingerprint=null;ReadPresetPresentation();
        pendingVersion=null;
        ClearModVersions();
        gameInventory=new P2PSyncDifference[0];
        nexusManual.Clear();
        statuses.Clear();statusDetails.Clear();progress.Clear();
        return true;
    }
    async void ShowOptions(){
        if(GeneralDialog.FocusOpen<GeneralOptionsDialog>(this)||working||scanning)return;
        string destination=state.Destination;
        try{
            using(var dialog=new GeneralOptionsDialog(state.Theme,state.Language,state.DownloadFolder,cache.PreviewRetainedDownloads,
                state.Destination??"",root=>Cyberpunk(root).PreviewOldSaveBackups())){
                dialog.AnchorTo(optionsButton);
                var answer=await dialog.ShowModelessAsync(this);
                if(IsDisposed||exitApproved||working||scanning||!InventoryPaths.Equals(destination,state.Destination))return;
                if(answer!=DialogResult.OK){RefreshStorageSize();return;}
                bool moved=SetDestination(dialog.Destination);
                state.Theme=dialog.ThemeValue;state.Language=dialog.LanguageValue;state.DownloadFolder=dialog.DownloadFolder;
                Persist();if(projectStore!=null)projectStore.SavePreferences(state.Theme,state.Language,state.DownloadFolder);
                BuildInterface();if(moved)await Scan();else RefreshStorageSize();
            }
        }catch(Exception ex){if(!IsDisposed)ShowError(ex);}
    }
}

