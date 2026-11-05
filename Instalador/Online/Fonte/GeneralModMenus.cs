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
        if(listMenu!=null&&listMenu.Visible){listMenu.Close();return;}
        if(listMenu!=null)listMenu.Dispose();
        listMenu=GeneralMenuRenderer.Create();
        GeneralMenuRenderer.Add(listMenu,T("Nova lista","New list"),()=>Replace(new GeneralModList()));
        GeneralMenuRenderer.Add(listMenu,T("Importar lista","Import list"),()=>Guard(ImportList));
        GeneralMenuRenderer.Add(listMenu,T("Exportar lista","Export list"),()=>Guard(ExportList));
        listMenu.Items.Add(new ToolStripSeparator());
        GeneralMenuRenderer.Add(listMenu,T("Novo preset","New preset"),()=>EditPreset(true));
        GeneralMenuRenderer.Add(listMenu,T("Editar preset atual","Edit current preset"),()=>EditPreset(false));
        GeneralMenuRenderer.Add(listMenu,T("Excluir preset atual","Delete current preset"),()=>Guard(DeletePreset));
        listMenu.Items.Add(new ToolStripSeparator());
        GeneralMenuRenderer.Add(listMenu,T("Pacote Cyberpunk","Cyberpunk pack"),()=>Replace(GeneralModLibrary.Seed(catalog,new ModImportItem[0])));
        listMenu.Show(listButton,new Point(listButton.Width,listButton.Height+8),ToolStripDropDownDirection.BelowLeft);
    }
    async void Replace(GeneralModList list){
        if(working||scanning||list==null)return;
        if(state.List.Mods.Count!=0&&GeneralDialog.Message(this,T("Trocar a lista atual? Exporte primeiro se quiser guardar uma cópia. Os arquivos instalados permanecem no lugar.","Replace the current list? Export it first to keep a copy. Installed files will stay in place."),T("Trocar lista","Replace list"),MessageBoxButtons.OKCancel,MessageBoxIcon.Question)!=DialogResult.OK)return;
        try{
            GeneralModLibrary.RestoreKnownMappings(list,seed);state.List=list;state.SelectedPreset=null;state.ChosenVariants.Clear();marked.Clear();statuses.Clear();progress.Clear();
            Persist();RebuildFilter();RebuildPresets();await Scan();
        }catch(Exception ex){ShowError(ex);}
    }
    void ImportList(){using(var dialog=new OpenFileDialog{Filter=T("Lista do Mod Loader|*.json","Mod Loader list|*.json")})if(dialog.ShowDialog(this)==DialogResult.OK)Replace(library.Import(dialog.FileName));}
    void ExportList(){
        using(var dialog=new SaveFileDialog{Filter=T("Lista do Mod Loader|*.json","Mod Loader list|*.json"),FileName="mod-list.json"}){
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            library.Export(dialog.FileName,state.List);detail.Text=T("Lista exportada com categorias, tags e presets. Os arquivos dos mods não estão no JSON.","List exported with categories, tags and presets. Mod files are not included in the JSON.");
        }
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
        if(working||scanning)return;var item=state.List.Presets.FirstOrDefault(p=>p.Id==state.SelectedPreset);if(item==null)return;
        if(GeneralDialog.Message(this,T("Excluir este preset? Os mods permanecem na lista.","Delete this preset? Mods will remain in the list.")+"\n"+item.Name,T("Excluir preset","Delete preset"),MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
        state.List.Presets.Remove(item);state.SelectedPreset=null;Persist();RebuildPresets();
    }
    void EditCategory(string category,bool create){
        if(working||scanning)return;
        try{
            string name=create?"":category??UncatName(),description=null;
            if(!create&&category==null)description=state.List.UncategorizedDescription;
            else if(category!=null)state.List.CategoryDescriptions.TryGetValue(category,out description);
            using(var dialog=new GeneralCategoryDetails(name,description,!create&&category==null)){
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                string renamed=dialog.ResultName;
                if(state.List.Categories.Any(c=>!String.Equals(c,category,StringComparison.OrdinalIgnoreCase)&&String.Equals(c,renamed,StringComparison.OrdinalIgnoreCase)))throw new IOException(T("Já existe uma categoria com esse nome.","A category with this name already exists."));
                if(create){state.List.Categories.Add(renamed);state.List.CategoryDescriptions[renamed]=dialog.ResultDescription;}
                else if(category==null){state.List.UncategorizedName=renamed;state.List.UncategorizedDescription=dialog.ResultDescription;}
                else{
                    state.List.Categories[state.List.Categories.IndexOf(category)]=renamed;
                    foreach(var mod in state.List.Mods.Where(m=>String.Equals(m.Category,category,StringComparison.OrdinalIgnoreCase)))mod.Category=renamed;
                    state.List.CategoryDescriptions.Remove(category);state.List.CategoryDescriptions[renamed]=dialog.ResultDescription;
                }
                Persist();RebuildFilter(create?renamed:category==null?null:renamed,category==null&&!create?1:0);
            }
        }catch(Exception ex){ShowError(ex);}
    }
    async void ChooseFolder(bool downloads){
        if(working||scanning)return;
        try{
            using(var dialog=new FolderBrowserDialog{Description=downloads?T("Escolha onde o navegador salva os downloads.","Choose the folder where your browser saves downloads."):T("Escolha a pasta em que os mods serão instalados.","Choose the folder where mods will be installed."),SelectedPath=(downloads?state.DownloadFolder:state.Destination)??""}){
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                if(downloads)state.DownloadFolder=new OnlineDownloadWatch(dialog.SelectedPath).Folder;
                else{Safe.NoLinks(dialog.SelectedPath);state.Destination=dialog.SelectedPath;destination.Text=state.Destination;}
                Persist();
                if(!downloads){statuses.Clear();progress.Clear();DrawRows();await Scan();}
                else detail.Text=T("Downloads monitorados: ","Watching downloads in: ")+state.DownloadFolder;
            }
        }catch(Exception ex){ShowError(ex);}
    }
    void OpenDownloads(){
        if(!Directory.Exists(state.DownloadFolder))throw new IOException(T("A pasta de downloads não está disponível. Escolha outra em Opções.","The download folder is unavailable. Choose another one in Options."));
        Safe.NoLinks(state.DownloadFolder);Process.Start(new ProcessStartInfo(state.DownloadFolder){UseShellExecute=true});
    }
    void ShowOptions(){
        if(working||scanning)return;
        using(var dialog=new GeneralOptionsDialog(state.Theme,state.Language,state.DownloadFolder)){
            dialog.AnchorTo(optionsButton);
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            state.Theme=dialog.ThemeValue;state.Language=dialog.LanguageValue;state.DownloadFolder=dialog.DownloadFolder;
            Persist();BuildInterface();
        }
    }
}

