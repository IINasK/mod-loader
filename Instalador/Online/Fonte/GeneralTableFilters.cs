using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Globalization;
using System.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    HashSet<string> categoryFilters,variantFilters,statusFilters;
    string nameFilter="";
    TextBox searchBox;
    string searchQuery="";
    string[] searchTerms=new string[0];
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr parameter,string text);
    Control BuildSearch(){
        var surface=new GeneralSurface{Name="ModSearchSurface",Dock=DockStyle.Fill,Margin=new Padding(14,4,14,10),Padding=new Padding(18,2,18,2)};
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty,BackColor=GeneralStyle.Surface};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,28));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));surface.Controls.Add(layout);
        var icon=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};icon.Paint+=(s,e)=>LucideIcons.Draw(e.Graphics,"search",new Rectangle(0,(icon.Height-18)/2,18,18),GeneralStyle.Muted);layout.Controls.Add(icon,0,0);
        searchBox=new TextBox{Name="ModSearch",BorderStyle=BorderStyle.None,Dock=DockStyle.Fill,MaxLength=160,Font=GeneralStyle.Font(11),Margin=new Padding(0,10,0,0),BackColor=GeneralStyle.Surface,ForeColor=GeneralStyle.Foreground,Text=searchQuery,AccessibleName=T("Pesquisar por nome, variante ou situação","Search by name, variant or status")};
        tips.SetToolTip(searchBox,searchBox.AccessibleName+T(". Aceita partes do nome e pequenos erros de digitação.",". Accepts partial names and minor typos."));layout.Controls.Add(searchBox,1,0);
        searchBox.HandleCreated+=(s,e)=>SendMessage(((Control)s).Handle,0x1501,new IntPtr(1),T("Pesquisar nome, variante ou situação…","Search name, variant or status…"));
        var clear=(GeneralButton)GeneralStyle.Button("",()=>{searchBox.Clear();searchBox.Focus();});clear.Name="ClearSearch";clear.Bare=true;clear.IconKind="close";clear.AutoSize=false;clear.MinimumSize=new Size(44,44);clear.Dock=DockStyle.Fill;clear.Margin=Padding.Empty;clear.AccessibleName=T("Limpar pesquisa","Clear search");clear.Enabled=searchQuery.Length>0;layout.Controls.Add(clear,2,0);
        var input=searchBox;var delay=new Timer{Interval=150};
        Action apply=()=>{delay.Stop();searchQuery=input.Text;searchTerms=GeneralModSearch.Terms(searchQuery);clear.Enabled=searchQuery.Length>0;DrawRows();};
        delay.Tick+=(s,e)=>apply();input.TextChanged+=(s,e)=>{delay.Stop();delay.Start();};input.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;apply();}else if(e.KeyCode==Keys.Escape){e.SuppressKeyPress=true;input.Clear();apply();}};
        input.Disposed+=(s,e)=>delay.Dispose();return surface;
    }
    TextBox tableName;
    Button presetSettings;
    GeneralModPreset ActivePreset {get{return state.List.Presets.FirstOrDefault(p=>p.Id==state.SelectedPreset);}}
    IEnumerable<string> PresetCategories(){
        var active=ActivePreset;
        if(active==null)return state.List.Categories;
        var used=new HashSet<string>(state.List.Mods.Where(mod=>active.ModIds.Contains(mod.Id)).Select(mod=>mod.Category),StringComparer.OrdinalIgnoreCase);
        return state.List.Categories.Where(category=>used.Contains(category)||active.Categories.Contains(category,StringComparer.OrdinalIgnoreCase));
    }
    string DefaultCategory {get{return categoryFilters!=null&&categoryFilters.Count==1?categoryFilters.Single():null;}}
    void ResetColumnFilters(){categoryFilters=variantFilters=statusFilters=null;nameFilter="";searchQuery="";searchTerms=new string[0];if(searchBox!=null&&!searchBox.IsDisposed)searchBox.Clear();}
    static string StatusFilterKey(string value){
        if(value=="Pendente"||value=="Pending")return "pending";
        if(value=="Baixando"||value=="Downloading")return "active";
        if(value=="Download concluído"||value=="Download complete")return "complete";
        if(value=="Instalando"||value=="Installing")return "installing";
        if(value=="Instalado"||value=="Installed")return "installed";
        if(value=="Outra versão escolhida"||value=="Other version selected")return "other-choice";
        if(value=="Corpo detectado"||value=="Body detected")return "manual-body";
        if(value=="Desabilitado"||value=="Disabled")return "disabled";
        return "error";
    }
    bool ColumnAllows(GeneralModEntry mod){
        string raw=ListStatus(mod);
        return (categoryFilters==null||categoryFilters.Contains(mod.Category??""))&&
            (variantFilters==null||variantFilters.Contains(Selected(mod).Name))&&
            (statusFilters==null||statusFilters.Any(value=>StatusFilterKey(value)==StatusFilterKey(GeneralStatusPresentation.Label(raw))))&&
            (String.IsNullOrWhiteSpace(nameFilter)||mod.Name.IndexOf(nameFilter,StringComparison.CurrentCultureIgnoreCase)>=0)&&
            GeneralModSearch.Matches(searchTerms,mod.Nsfw?mod.Name+" NSFW Not Safe for Work":mod.Name,Selected(mod).Name,GeneralStatusPresentation.Label(raw));
    }
    void ShowColumnFilter(int column){
        if(column<1||column>3)return;
        string uncat=UncatName();
        var values=column==1?new[]{uncat}.Concat(PresetCategories()).ToList():column==2?state.List.Mods.Select(mod=>Selected(mod).Name).Distinct().OrderBy(value=>value).ToList():
            new[]{T("Pendente","Pending"),T("Baixando","Downloading"),T("Download concluído","Download complete"),
                T("Instalando","Installing"),T("Instalado","Installed"),
                GeneralStatusPresentation.Label(GeneralStatusPresentation.OtherChoice),GeneralStatusPresentation.Label(GeneralStatusPresentation.ManualBody),
                GeneralStatusPresentation.Label(GeneralStatusPresentation.Disabled),
                T("Falha","Failed")}.ToList();
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
        if(!hasPresetDraft||draftPresetId!=state.SelectedPreset)CapturePresetDraft();
        var selected=ActivePreset;tableName.ReadOnly=selected==null||ProtectedPreset;settingDraftName=true;
        try{tableName.Text=selected==null?T("Todos os itens","All items"):draftName;}finally{settingDraftName=false;}
        tableName.AccessibleName=T("Nome do preset","Preset name");
        UpdatePresetCommands();
    }
    void SaveTableTitle(){
        var selected=ActivePreset;if(selected==null||ProtectedPreset||tableName==null||tableName.Text==selected.Name)return;
        string name=tableName.Text.Trim();
        if(name.Length==0||name.Length>120||name.Any(Char.IsControl)){tableName.Text=selected.Name;return;}
        selected.Name=name;Persist();CapturePresetDraft();RebuildPresets();
    }
    void ShowPresetSettings(){
        if(working||scanning)return;
        using(var menu=new GeneralPresetActionsDialog(ActivePreset==null?T("Preset","Preset"):ActivePreset.Name)){
            menu.AnchorTo(presetSettings);if(menu.ShowDialog(this)!=DialogResult.OK)return;SavePresetSelection(menu.ActionId=="new");
        }
    }
    void SavePresetSelection(bool create){
        if(working||scanning)return;
        var selected=ActivePreset;if(selected==null||ProtectedPreset)create=true;
        using(var name=new GeneralNameDialog(T(create?"Novo preset":"Atualizar preset",create?"New preset":"Update preset"),create?"":selected.Name)){
            if(name.ShowDialog(this)!=DialogResult.OK)return;
            var ids=PresetCheckedIds();
            if(create){selected=new GeneralModPreset{Id="preset-"+Guid.NewGuid().ToString("N")};state.List.Presets.Add(selected);}
            else if(GeneralDialog.Message(this,T("Substituir os itens deste preset pelos mods marcados?","Replace this preset's items with the checked mods?"),T("Atualizar preset","Update preset"),MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
            selected.Name=name.ResultName;selected.ModIds=ids;state.SelectedPreset=selected.Id;Persist();CapturePresetDraft();RebuildPresets();
        }
    }
    void ClearPreset(){
        if(working||scanning)return;
        var selected=ActivePreset;
        if(selected==null){ResetColumnFilters();marked.Clear();DrawRows();return;}
        if(GeneralDialog.Message(this,T("Esvaziar este preset? Isso não desinstala mods nem apaga seus arquivos.","Empty this preset? This does not uninstall mods or delete their files."),T("Limpar preset","Clear preset"),MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
        selected.ModIds.Clear();selected.Categories.Clear();marked.Clear();ResetColumnFilters();Persist();DrawRows();
    }
}

// Same matching policy as G3 ERP's local search: accents, fragments, bounded word typos; numbers stay literal.
internal static class GeneralModSearch {
    static string Normalize(string value){
        var text=new StringBuilder();bool space=true;
        foreach(char letter in (value??"").Normalize(NormalizationForm.FormKD)){
            if(CharUnicodeInfo.GetUnicodeCategory(letter)==UnicodeCategory.NonSpacingMark)continue;
            if(letter=='\''||letter=='’')continue;
            if(Char.IsLetterOrDigit(letter)){text.Append(Char.ToLowerInvariant(letter));space=false;}
            else if(!space){text.Append(' ');space=true;}
        }
        return text.ToString().Trim();
    }
    internal static string[] Terms(string query){return Normalize(query).Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray();}
    internal static bool Matches(string[] terms,params string[] fields){
        if(terms.Length==0)return true;
        var words=fields.SelectMany(Terms).Distinct().ToArray();
        return terms.All(term=>words.Any(word=>word.Contains(term))||term.Length>=4&&term.Length<=32&&!term.Any(Char.IsDigit)&&words.Any(word=>Approximate(term,word)));
    }
    static bool Approximate(string term,string word){
        if(word.Any(Char.IsDigit))return false;
        int limit=term.Length>=8?2:1;
        foreach(int length in new[]{word.Length,term.Length-1,term.Length,term.Length+1}.Distinct())
            if(length>=3&&length<=word.Length&&Math.Abs(length-term.Length)<=limit&&Distance(term,word.Substring(0,length),limit)<=limit)return true;
        return false;
    }
    static int Distance(string a,string b,int limit){
        int[] previous=Enumerable.Range(0,b.Length+1).ToArray(),before=previous;
        for(int i=1;i<=a.Length;i++){
            var row=new int[b.Length+1];row[0]=i;int smallest=i;
            for(int j=1;j<=b.Length;j++){
                row[j]=Math.Min(Math.Min(row[j-1]+1,previous[j]+1),previous[j-1]+(a[i-1]==b[j-1]?0:1));
                if(i>1&&j>1&&a[i-1]==b[j-2]&&a[i-2]==b[j-1])row[j]=Math.Min(row[j],before[j-2]+1);
                smallest=Math.Min(smallest,row[j]);
            }
            if(smallest>limit)return limit+1;before=previous;previous=row;
        }
        return previous[b.Length];
    }
}
