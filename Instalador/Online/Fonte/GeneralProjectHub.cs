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
        var link=GeneralNexusLink.Parse(source);if(link==null)return null;
        GeneralSourceInfo info;if(sourceMetadata.TryGetValue(link.Url,out info))return info;
        var pinned=link.Game=="cyberpunk2077"&&link.File>0?(
            from mod in catalog.Packages
            from asset in mod.Assets
            where asset.ModId==link.Mod&&asset.FileId==link.File
            select new GeneralSourceInfo{Name=mod.Name,FileName=asset.Name,Bytes=asset.Size,Version=mod.Version}
        ).FirstOrDefault():null;
        info=testMode?null:await GeneralNexusInfo.Read(source);
        if(pinned!=null){
            if(info!=null){pinned.Tags=info.Tags;pinned.Nsfw=info.Nsfw;pinned.ModVersion=info.ModVersion;pinned.NexusChecked=info.NexusChecked;}
            info=pinned;
        }
        if(info!=null){if(sourceMetadata.Count>=128)sourceMetadata.Clear();sourceMetadata[link.Url]=info;}
        return info;
    }
    bool storageReading,storageQueued;
    GeneralStorageSize measuredStorage=new GeneralStorageSize();
    Dictionary<string,long> measuredMods { get { return measuredStorage.ModBytes; } }
    async void RefreshStorageSize(){
        if(projectStore==null||project==null||storageSize==null)return;
        if(storageReading){storageQueued=true;return;}storageReading=true;
        try{do{
            storageQueued=false;int generation=++storageGeneration;var label=storageSize;
            try{var size=await Task.Run(()=>GeneralProjectStorage.Measure(projectStore,catalog,cache,project));if(!IsDisposed&&generation==storageGeneration&&!label.IsDisposed){label.SetSize(size,false);measuredStorage=size;UpdateMeasuredRows();}}
            catch(Exception){if(!IsDisposed&&generation==storageGeneration&&!label.IsDisposed){label.SetSize(null,false);measuredStorage=new GeneralStorageSize();UpdateMeasuredRows();}}
        }while(storageQueued&&!IsDisposed);}finally{storageReading=false;}
    }
    GeneralSourceInfo DependencySize(GeneralModEntry mod){
        if(mod.DependencySource==null)return null;
        return Selected(mod).SourceInfo.Values.FirstOrDefault(info=>info.Bytes>0);
    }
    string ModSpace(GeneralModEntry mod){
        if(GeneralModLibrary.NeedsDependencyVersion(mod,Selected(mod)))return "";
        long bytes;if(measuredMods.TryGetValue(mod.Id,out bytes))return GeneralStorageSize.Format(bytes);
        var info=DependencySize(mod);
        return info!=null?(info.SizeApproximate?"≈ ":"")+GeneralStorageSize.Format(info.Bytes):mod.DependencySource==null?"—":"";
    }
    void UpdateMeasuredRows(){
        if(grid==null||grid.IsDisposed)return;
        var byId=state.List.Mods.ToDictionary(mod=>mod.Id,StringComparer.Ordinal);
        foreach(var row in ModRows()){
            var mod=byId[(string)row.Tag];row.Cells["Size"].Value=ModSpace(mod);row.Cells["Size"].ToolTipText=SpaceTip(mod);
        }
        foreach(DataGridViewRow row in grid.Rows){
            var work=row.Tag as Work;
            if(work!=null&&work.BodyChoice!=null)row.Cells["Size"].Value=BodyOptionSpace(work);
            var section=row.Tag as SectionRow;
            if(section!=null&&section.Kind=="heading"){
                section.Size=CategorySize(section.ModIds);
                grid.InvalidateRow(row.Index);
            }
        }
        UpdateCounts();
    }
    string CategorySize(string[] ids){
        if(ids.Length==0)return "";
        long? bytes=measuredStorage.CategoryBytes(ids);
        return bytes.HasValue?(ids.All(measuredMods.ContainsKey)?"":"≥ ")+GeneralStorageSize.Format(bytes.Value):"—";
    }

    string SpaceTip(GeneralModEntry mod){
        if(GeneralModLibrary.NeedsDependencyVersion(mod,Selected(mod)))return T("Escolha a versão da dependência.","Choose the dependency version.");
        if(!measuredMods.ContainsKey(mod.Id)&&DependencySize(mod)!=null)return T("Tamanho do download informado pelo Nexus.","Download size reported by Nexus.");
        return measuredMods.ContainsKey(mod.Id)?T("Arquivos instalados deste mod. Arquivos compartilhados aparecem em cada mod; o total do projeto os conta uma vez.","Installed files for this mod. Shared files appear for each mod; the project total counts them once."):T("Tamanho não identificado: nenhum arquivo instalado foi localizado ou a leitura não foi concluída. O tamanho do download não é usado como tamanho instalado.","Size not identified: no installed files were found or measurement is incomplete. Download size is not used as installed size.");}
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
        if(scanning||associationChoice!=null||String.IsNullOrWhiteSpace(text))return;
        try{
            var links=text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(value=>value.Trim()).Where(GeneralModDownload.IsWebSource).Take(32).Select(GeneralModLibrary.ValidateSource).Distinct().ToArray();
            foreach(string source in links){
                var known=state.List.Mods.FirstOrDefault(mod=>mod.Variants.Any(variant=>variant.Sources.Contains(source)));
                var entry=known??NewEntry("",source);if(known==null)entry.Category=DefaultCategory;await EditMod(entry);
            }
        }catch(Exception ex){ShowError(ex);}
    }
    protected override bool ProcessCmdKey(ref Message message,Keys keys){
        if(associationChoice!=null){
            if(keys==Keys.Escape){associationChoice.TrySetResult(null);return true;}
            if(keys==Keys.Enter&&grid.ContainsFocus){ConfirmAlternative();return true;}
            return base.ProcessCmdKey(ref message,keys);
        }
        if(keys==(Keys.Control|Keys.V)&&!(ActiveControl is TextBoxBase)&&!(ActiveControl is GeneralPicker)){
            if(Clipboard.ContainsText()){string text=Clipboard.GetText();if(GeneralModDownload.IsWebSource(text.Trim())){ImportWebSources(text);return true;}}
        }
        return base.ProcessCmdKey(ref message,keys);
    }
}
