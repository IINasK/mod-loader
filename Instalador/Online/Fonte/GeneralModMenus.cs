using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    void ToggleListMenu(){
        if(working||scanning)return;
        menus.Toggle(this,optionsButton,()=>new GeneralActionsMenu(BuiltInProject),dialog=>Guard(()=>{
            string action=((GeneralActionsMenu)dialog).ActionId;
            if(action=="new")ProjectCommand("new");else if(action=="open")BackToProjects();
            else if(!BuiltInProject&&action=="import")ProjectCommand("import");else if(!BuiltInProject&&action=="export")ProjectCommand("export");
            else if(action=="clone")ProjectCommand("clone");else if(action=="share")ShowP2P();else if(action=="options")ShowOptions();
        }));
    }
    async void Replace(GeneralModList list){await ReplaceList(list);}
    async Task<bool> ReplaceList(GeneralModList list,bool clearBindings=false,string snapshotReceipt=null,string newDestination=null){
        if(working||scanning||list==null||!ConfirmProjectNavigation())return false;
        if(state.List.Mods.Count!=0&&GeneralDialog.Message(this,T("Trocar a lista atual? Exporte primeiro se quiser guardar uma cópia. Os arquivos instalados permanecem no lugar.","Replace the current list? Export it first to keep a copy. Installed files will stay in place."),T("Trocar lista","Replace list"),MessageBoxButtons.OKCancel,MessageBoxIcon.Question)!=DialogResult.OK)return false;
        try{
            GeneralModLibrary.RestoreKnownMappings(list,seed);state.List=list;state.SelectedPreset=list.Presets.FirstOrDefault()==null?null:list.Presets[0].Id;state.ChosenVariants.Clear();marked.Clear();statuses.Clear();statusDetails.Clear();progress.Clear();ResetColumnFilters();hasPresetDraft=false;
            if(clearBindings){
                foreach(string key in state.ResolvedDownloads.Keys.Where(key=>list.Mods.Any(mod=>key.StartsWith(mod.Id+"/",StringComparison.Ordinal))).ToArray())state.ResolvedDownloads.Remove(key);
                foreach(string key in state.CyberpunkImports.Keys.Where(key=>list.Mods.Any(mod=>key.StartsWith(mod.Id+"/",StringComparison.Ordinal))).ToArray())state.CyberpunkImports.Remove(key);
                foreach(string key in state.SnapshotReceipts.Keys.Where(key=>list.Mods.Any(mod=>key.StartsWith(mod.Id+"/",StringComparison.Ordinal))).ToArray())state.SnapshotReceipts.Remove(key);
            }
            if(snapshotReceipt!=null)foreach(var mod in new InstalledSnapshotInstaller(newDestination??state.Destination,data,catalog,testMode).ReadManifest(snapshotReceipt).List.Mods)state.SnapshotReceipts[mod.Id+"/"+mod.DefaultVariant]=snapshotReceipt;
            if(newDestination!=null)state.Destination=newDestination;
            Persist();RebuildPresets();await Scan();return true;
        }catch(Exception ex){ShowError(ex);return false;}
    }
    void EditPreset(bool create){
        if(working||scanning)return;
        try{
            var currentPreset=create?null:state.List.Presets.FirstOrDefault(p=>p.Id==state.SelectedPreset);
            if(currentPreset==null)create=true;
            var item=currentPreset??new GeneralModPreset{Id="preset-"+Guid.NewGuid().ToString("N"),Name=T("Novo preset","New preset")};
            using(var dialog=new GeneralPresetEditor(item,state.List.Mods,item.ModIds)){
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                if(create)state.List.Presets.Add(dialog.Result);else state.List.Presets[state.List.Presets.IndexOf(item)]=dialog.Result;
                state.SelectedPreset=dialog.Result.Id;Persist();RebuildPresets();
            }
        }catch(Exception ex){ShowError(ex);}
    }
    void DeletePreset(){
        if(working||scanning||ProtectedPreset)return;var item=state.List.Presets.FirstOrDefault(p=>p.Id==state.SelectedPreset);if(item==null)return;
        if(GeneralDialog.Message(this,T("Excluir este preset? Os mods permanecem na lista.","Delete this preset? Mods will remain in the list.")+"\n"+item.Name,T("Excluir preset","Delete preset"),MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
        BeginProjectEdit();state.List.Presets.Remove(item);state.SelectedPreset=null;FinishProjectEdit();RebuildPresets();
    }
    void EditCategory(string category,bool create){
        if(working||scanning)return;
        try{
            string name=create?"":category??UncatName(),description=null;
            if(!create&&category==null)description=state.List.UncategorizedDescription;
            else if(category!=null)state.List.CategoryDescriptions.TryGetValue(category,out description);
            using(var dialog=new GeneralCategoryDetails(name,description,!create&&category==null,!create&&category!=null)){
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                if(dialog.DeleteRequested){
                    if(GeneralDialog.Message(this,T("Excluir a categoria? Os mods vão para Sem categoria; os arquivos instalados serão preservados.","Delete this category? Mods will move to Uncategorized; installed files will stay in place."),T("Excluir categoria","Delete category"),MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
                    BeginProjectEdit();GeneralModLibrary.DeleteCategory(state.List,category);ResetColumnFilters();FinishProjectEdit();DrawRows();return;
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
                else{
                    state.List.Categories[state.List.Categories.IndexOf(category)]=renamed;
                    foreach(var item in state.List.Presets)for(int i=0;i<item.Categories.Count;i++)if(String.Equals(item.Categories[i],category,StringComparison.OrdinalIgnoreCase))item.Categories[i]=renamed;
                    foreach(var mod in state.List.Mods.Where(m=>String.Equals(m.Category,category,StringComparison.OrdinalIgnoreCase)))mod.Category=renamed;
                    state.List.CategoryDescriptions.Remove(category);state.List.CategoryDescriptions[renamed]=dialog.ResultDescription;
                }
                if(categoryFilters!=null&&category!=null&&categoryFilters.Remove(category))categoryFilters.Add(renamed);
                if(create&&categoryFilters!=null)categoryFilters.Add(renamed);
                FinishProjectEdit();DrawRows();
            }
        }catch(Exception ex){ShowError(ex);}
    }
    void OpenDownloads(){
        if(!Directory.Exists(state.DownloadFolder))throw new IOException(T("A pasta de downloads não está disponível. Escolha outra em Opções.","The download folder is unavailable. Choose another one in Options."));
        Safe.NoLinks(state.DownloadFolder);Process.Start(new ProcessStartInfo(state.DownloadFolder){UseShellExecute=true});
    }
    bool SetDestination(string destination){
        if(String.Equals(state.Destination??"",destination,StringComparison.OrdinalIgnoreCase))return false;
        state.Destination=destination;
        gameInventory=new P2PSyncDifference[0];
        statuses.Clear();statusDetails.Clear();progress.Clear();
        return true;
    }
    async void ShowOptions(){
        if(working||scanning)return;
        using(var dialog=new GeneralOptionsDialog(state.Theme,state.Language,state.DownloadFolder,cache.PreviewRetainedDownloads,state.Destination??"")){
            dialog.AnchorTo(optionsButton);
            if(dialog.ShowDialog(this)!=DialogResult.OK){RefreshStorageSize();return;}
            bool moved=SetDestination(dialog.Destination);
            state.Theme=dialog.ThemeValue;state.Language=dialog.LanguageValue;state.DownloadFolder=dialog.DownloadFolder;
            Persist();if(projectStore!=null)projectStore.SavePreferences(state.Theme,state.Language,state.DownloadFolder);BuildInterface();if(moved)await Scan();else RefreshStorageSize();
        }
    }
}

