using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    int storageGeneration;
    readonly Dictionary<string,GeneralSourceInfo> sourceMetadata=new Dictionary<string,GeneralSourceInfo>();
    async Task<GeneralSourceInfo> LookupSourceInfo(string source){
        GeneralSourceInfo info;if(sourceMetadata.TryGetValue(source,out info))return info;
        var link=GeneralNexusLink.Parse(source);if(link==null)return null;
        if(link.Game=="cyberpunk2077")foreach(var mod in catalog.Packages)foreach(var asset in mod.Assets)if(asset.ModId==link.Mod&&asset.FileId==link.File){info=new GeneralSourceInfo{Name=mod.Name,FileName=asset.Name,Bytes=asset.Size};sourceMetadata[source]=info;return info;}
        if(testMode)return null;
        info=await GeneralNexusInfo.Read(source);if(info!=null){if(sourceMetadata.Count>=128)sourceMetadata.Clear();sourceMetadata[source]=info;}return info;
    }
    async void RefreshStorageSize(){
        if(projectStore==null||project==null||storageSize==null)return;
        int generation=++storageGeneration;var label=storageSize;
        try{var size=await Task.Run(()=>GeneralProjectStorage.Measure(projectStore,catalog,cache,project));if(!IsDisposed&&generation==storageGeneration&&!label.IsDisposed)label.Text=GeneralStorageSize.ProjectText(size);}
        catch(Exception){if(!IsDisposed&&generation==storageGeneration&&!label.IsDisposed)label.Text=T("Espaço indisponível","Size unavailable");}
    }
    void ToggleProjectsPanel(){
        if(working||scanning)return;
        menus.Toggle(this,projectsButton,()=>new GeneralProjectsPanel(projectStore,catalog,cache,project==null?null:project.Id),dialog=>Guard(()=>{
            string action=((GeneralProjectsPanel)dialog).ActionId;
            if(action=="new")ProjectCommand("new");else if(action=="import")ProjectCommand("import-project");
            else if(action=="ready"){if(project==null||project.TemplateId!="cyberpunk-2077")ProjectCommand("ready");}
            else if(action!=null&&action.StartsWith("project/",StringComparison.Ordinal)&&(project==null||action!="project/"+project.Id))ProjectCommand(action);
        }));
    }
    static string DroppedText(IDataObject data){return data.GetDataPresent(DataFormats.UnicodeText)?data.GetData(DataFormats.UnicodeText) as string:data.GetDataPresent(DataFormats.Text)?data.GetData(DataFormats.Text) as string:null;}
    async void ImportWebSources(string text){
        if(scanning||String.IsNullOrWhiteSpace(text))return;
        try{
            var links=text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(value=>value.Trim()).Where(GeneralModDownload.IsWebSource).Take(32).Select(GeneralModLibrary.ValidateSource).Distinct().ToArray();
            foreach(string source in links){
                var known=state.List.Mods.FirstOrDefault(mod=>mod.Variants.Any(variant=>variant.Sources.Contains(source)));
                var entry=known??NewEntry("",source);if(known==null)entry.Category=DefaultCategory;await EditMod(entry);
            }
        }catch(Exception ex){ShowError(ex);}
    }
    protected override bool ProcessCmdKey(ref Message message,Keys keys){
        if(keys==(Keys.Control|Keys.V)&&!(ActiveControl is TextBoxBase)&&!(ActiveControl is GeneralPicker)){
            if(Clipboard.ContainsText()){string text=Clipboard.GetText();if(GeneralModDownload.IsWebSource(text.Trim())){ImportWebSources(text);return true;}}
        }
        return base.ProcessCmdKey(ref message,keys);
    }
}
