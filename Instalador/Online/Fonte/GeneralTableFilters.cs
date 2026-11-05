using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    HashSet<string> categoryFilters,variantFilters,statusFilters;
    string nameFilter="";
    TextBox tableName;
    Button presetSettings;
    GeneralModPreset ActivePreset {get{return state.List.Presets.FirstOrDefault(p=>p.Id==state.SelectedPreset);}}
    string DefaultCategory {get{return categoryFilters!=null&&categoryFilters.Count==1?categoryFilters.Single():null;}}
    void ResetColumnFilters(){categoryFilters=variantFilters=statusFilters=null;nameFilter="";}
    static string StatusFilterKey(string value){return value=="Não baixado"||value=="Not downloaded"?"pending":value=="Baixando"||value=="Downloading"?"active":value=="Download concluído"||value=="Download complete"?"complete":"error";}
    bool ColumnAllows(GeneralModEntry mod){
        string raw;statuses.TryGetValue(Key(mod,Selected(mod)),out raw);
        return (categoryFilters==null||categoryFilters.Contains(mod.Category??""))&&
            (variantFilters==null||variantFilters.Contains(Selected(mod).Name))&&
            (statusFilters==null||statusFilters.Any(value=>StatusFilterKey(value)==StatusFilterKey(GeneralStatusPresentation.Label(raw))))&&
            (String.IsNullOrWhiteSpace(nameFilter)||mod.Name.IndexOf(nameFilter,StringComparison.CurrentCultureIgnoreCase)>=0);
    }
    void ShowColumnFilter(int column){
        if(column<1||column>3)return;
        string uncat=UncatName();
        var values=column==1?new[]{uncat}.Concat(state.List.Categories).ToList():column==2?state.List.Mods.Select(mod=>Selected(mod).Name).Distinct().OrderBy(value=>value).ToList():
            new[]{T("Não baixado","Not downloaded"),T("Baixando","Downloading"),T("Download concluído","Download complete"),T("Falha","Failed")}.ToList();
        var current=column==1?categoryFilters:column==2?variantFilters:statusFilters;
        IEnumerable<string> selected=current==null?null:column==3?values.Where(value=>current.Any(item=>StatusFilterKey(item)==StatusFilterKey(value))):current.Select(value=>column==1&&value==""?uncat:value);
        using(var dialog=new GeneralColumnFilterDialog(column==1?T("Filtrar mods","Filter mods"):column==2?T("Filtrar variantes","Filter variants"):T("Filtrar situação","Filter status"),values,selected,nameFilter,column==1)){
            var rect=grid.GetCellDisplayRectangle(column,-1,true);dialog.AnchorBelow(grid,rect,false);
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            var picked=dialog.ResultValues;
            if(column==1){categoryFilters=picked.Count==values.Count?null:new HashSet<string>(picked.Select(value=>value==uncat?"":value));nameFilter=dialog.SearchText??"";}
            else if(column==2)variantFilters=picked.Count==values.Count?null:picked;
            else statusFilters=picked.Count==values.Count?null:picked;
            DrawRows();
        }
    }
    void UpdateTableTitle(){
        if(tableName==null)return;
        var selected=ActivePreset;tableName.ReadOnly=selected==null;tableName.Text=selected==null?T("Todos os itens","All items"):selected.Name;
        tableName.AccessibleName=T("Nome do preset","Preset name");
    }
    void SaveTableTitle(){
        var selected=ActivePreset;if(selected==null||tableName==null||tableName.Text==selected.Name)return;
        string name=tableName.Text.Trim();
        if(name.Length==0||name.Length>120||name.Any(Char.IsControl)){tableName.Text=selected.Name;return;}
        selected.Name=name;Persist();RebuildPresets();
    }
    void ShowPresetSettings(){
        if(working||scanning)return;
        using(var menu=new GeneralPresetActionsDialog(ActivePreset==null?T("Preset","Preset"):ActivePreset.Name)){
            menu.AnchorTo(presetSettings);if(menu.ShowDialog(this)!=DialogResult.OK)return;SavePresetSelection(menu.ActionId=="new");
        }
    }
    void SavePresetSelection(bool create){
        if(working||scanning)return;
        var selected=ActivePreset;if(selected==null)create=true;
        using(var name=new GeneralNameDialog(T(create?"Novo preset":"Atualizar preset",create?"New preset":"Update preset"),create?"":selected.Name)){
            if(name.ShowDialog(this)!=DialogResult.OK)return;
            var ids=Marked().Select(work=>work.Entry.Id).Distinct().ToList();
            if(create){selected=new GeneralModPreset{Id="preset-"+Guid.NewGuid().ToString("N")};state.List.Presets.Add(selected);}
            else if(GeneralDialog.Message(this,T("Substituir os itens deste preset pelos mods marcados?","Replace this preset's items with the checked mods?"),T("Atualizar preset","Update preset"),MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
            selected.Name=name.ResultName;selected.ModIds=ids;state.SelectedPreset=selected.Id;Persist();RebuildPresets();
        }
    }
    void ClearPreset(){
        if(working||scanning)return;
        var selected=ActivePreset;
        if(selected==null){ResetColumnFilters();marked.Clear();DrawRows();return;}
        if(GeneralDialog.Message(this,T("Esvaziar este preset? Isso não desinstala mods nem apaga seus arquivos.","Empty this preset? This does not uninstall mods or delete their files."),T("Limpar preset","Clear preset"),MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
        selected.ModIds.Clear();marked.Clear();ResetColumnFilters();Persist();DrawRows();
    }
}
