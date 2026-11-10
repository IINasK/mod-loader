using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Globalization;
using System.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    HashSet<string> categoryFilters,variantFilters,statusFilters,groupFilters;
    string orderColumn;
    bool reverseOrder;
    Button clearFilters;
    bool HasListFilters {get{return coverageFilter!=null||categoryFilters!=null||variantFilters!=null||
        statusFilters!=null||groupFilters!=null||orderColumn!=null||nameFilter.Length!=0||searchQuery.Length!=0;}}
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
        searchBox=new TextBox{Name="ModSearch",BorderStyle=BorderStyle.None,Dock=DockStyle.Fill,MaxLength=160,Font=GeneralStyle.Font(11),Margin=new Padding(0,10,0,0),BackColor=GeneralStyle.Surface,ForeColor=GeneralStyle.Foreground,Text=searchQuery,AccessibleName=T("Pesquisar por nome, tag, variante ou situação","Search by name, tag, variant or status")};
        tips.SetToolTip(searchBox,searchBox.AccessibleName+T(". Aceita partes do nome e pequenos erros de digitação.",". Accepts partial names and minor typos."));layout.Controls.Add(searchBox,1,0);
        searchBox.HandleCreated+=(s,e)=>SendMessage(((Control)s).Handle,0x1501,new IntPtr(1),T("Pesquisar nome, tag, variante ou situação…","Search name, tag, variant or status…"));
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
        var categories=state.List.Categories.AsEnumerable();
        if(active!=null){
            var used=new HashSet<string>(state.List.Mods.Where(mod=>active.ModIds.Contains(mod.Id)).Select(mod=>mod.Category),StringComparer.OrdinalIgnoreCase);
            categories=categories.Where(category=>used.Contains(category)||active.Categories.Contains(category,StringComparer.OrdinalIgnoreCase));
        }
        var discovered=new HashSet<string>(state.List.Mods.Where(mod=>mod.DependencySource!=null&&
            (active==null||active.ModIds.Contains(mod.Id))).Select(mod=>mod.Category),StringComparer.OrdinalIgnoreCase);
        return categories.OrderBy(category=>discovered.Contains(category));
    }
    string DefaultCategory {get{return categoryFilters!=null&&categoryFilters.Count==1?categoryFilters.Single():null;}}
    void ResetColumnFilters(){
        categoryFilters=variantFilters=statusFilters=groupFilters=null;
        orderColumn=null;reverseOrder=false;
        coverageFilter=null;
        nameFilter=searchQuery="";
        searchTerms=new string[0];
        if(searchBox!=null&&!searchBox.IsDisposed)searchBox.Clear();
    }

    void ClearListFilters(){
        ResetColumnFilters();
        RedrawFromTop();
    }

    void RedrawFromTop(){
        DrawRows();
        if(grid.Rows.Count>0)grid.FirstDisplayedScrollingRowIndex=0;
        SetTableToolsVisible(true);
    }
    static string StatusFilterKey(string value){
        if(value=="Pendente"||value=="Pending")return "pending";
        if(value=="Baixando"||value=="Downloading")return "active";
        if(value=="Download concluído"||value=="Download complete")return "complete";
        if(value=="Instalando"||value=="Installing")return "installing";
        if(value=="Instalado"||value=="Installed")return "installed";
        if(value=="Outra versão escolhida"||value=="Other version selected")return "other-choice";

        if(value=="Desabilitado"||value=="Disabled")return "disabled";
        if(value==GeneralStatusPresentation.ChooseDependency||value=="Choose version")return "dependency-version";
        if(value=="Encontrado no jogo"||value=="Found in game")return "game-inventory";
        if(value==GeneralStatusPresentation.DetectedFiles||value=="Files detected")return "detected-files";
        return "error";
    }
    static string GameInventoryStatus {get{return T("Encontrado no jogo","Found in game");}}
    IEnumerable<P2PSyncDifference> VisibleGameInventory(){
        if(associationChoice!=null||coverageFilter!=null||categoryFilters!=null||variantFilters!=null||groupFilters!=null||
            statusFilters!=null&&!statusFilters.Any(value=>StatusFilterKey(value)=="game-inventory"))
            return Enumerable.Empty<P2PSyncDifference>();
        var included=new HashSet<string>(state.List.Mods.SelectMany(mod=>mod.DetectedPaths),StringComparer.OrdinalIgnoreCase);
        return DisplayedInventory().Where(file=>file.Kind=="found"&&!included.Contains(file.Path)&&
            (String.IsNullOrWhiteSpace(nameFilter)||(file.Mod??file.Path).IndexOf(nameFilter,StringComparison.CurrentCultureIgnoreCase)>=0)&&
            GeneralModSearch.Matches(searchTerms,file.Mod,file.Path,GameInventoryStatus));
    }
    bool ColumnAllows(GeneralModEntry mod){
        string raw=InstallationStatus(mod,ListStatus(mod));
        var variant=Selected(mod);
        var searchable=IsBigBody(mod)?BodyVersionsForPreset().SelectMany(work=>
            (work.Variant.Tags??work.Entry.Tags).Concat(new[]{VersionDisplayName(work),BodyChoiceLabel(work)})):
            variant.Tags??mod.Tags;
        return (categoryFilters==null||categoryFilters.Contains(mod.Category??""))&&
            (groupFilters==null||groupFilters.Contains(VersionGroupName(mod)))&&
            (variantFilters==null||variantFilters.Contains(NexusVariantName(mod)))&&
            (statusFilters==null||statusFilters.Any(value=>StatusFilterKey(value)==StatusFilterKey(GeneralStatusPresentation.Label(raw))))&&
            (String.IsNullOrWhiteSpace(nameFilter)||(ModDisplayName(mod)+" "+mod.Name).IndexOf(nameFilter,StringComparison.CurrentCultureIgnoreCase)>=0)&&
            GeneralModSearch.Matches(searchTerms, new[]{ModDisplayName(mod),variant.DisplayName??mod.Name,mod.Nsfw?mod.Name+" NSFW Not Safe for Work":mod.Name,NexusVariantName(mod),GeneralStatusPresentation.Label(raw)}
                .Concat(searchable).ToArray());
    }
    static bool IsFilterColumn(string column){
        return column=="Name"||column=="Size"||column=="Variant"||column=="Status"||column=="Group";
    }
    bool ColumnFilterActive(string column){
        switch(column){
            case "Name":return categoryFilters!=null||nameFilter.Length>0;
            case "Variant":return variantFilters!=null;
            case "Status":return statusFilters!=null;
            case "Group":return groupFilters!=null;
            default:return false;
        }
    }
    string VersionGroupName(GeneralModEntry mod){
        return (IsBigBody(mod)?BodyVersionsForPreset().Count>1:mod.Variants.Count>1)?ModDisplayName(mod):"";
    }
    static int VariantRank(string name){
        return String.Equals(name,"Principal",StringComparison.OrdinalIgnoreCase)||
            String.Equals(name,"Main",StringComparison.OrdinalIgnoreCase)||
            String.Equals(name,"Default",StringComparison.OrdinalIgnoreCase)?0:1;
    }
    int StatusRank(GeneralModEntry mod){
        string raw=InstallationStatus(mod,ListStatus(mod));
        if(raw==GeneralStatusPresentation.NexusManual)return 0;
        switch(StatusFilterKey(GeneralStatusPresentation.Label(raw))){
            case "installed":return 0;
            case "installing":return 1;
            case "complete":return 2;
            case "active":return 3;
            case "disabled":return 4;
            case "pending":return 9;
            default:return 5;
        }
    }
    IEnumerable<GeneralModEntry> OrderedMods(IEnumerable<GeneralModEntry> mods){
        var names=StringComparer.CurrentCultureIgnoreCase;
        if(orderColumn=="Size")
            return mods.Select(mod=>new{Mod=mod,Bytes=SortableModBytes(mod)})
                .OrderBy(item=>!item.Bytes.HasValue)
                .ThenBy(item=>reverseOrder?item.Bytes:-item.Bytes)
                .ThenBy(item=>ModDisplayName(item.Mod),names).Select(item=>item.Mod);
        if(orderColumn=="Variant"){
            var entries=mods.Select(mod=>new{Mod=mod,Variant=IsBigBody(mod)?NexusVariantName(mod):Selected(mod).Name});
            return (reverseOrder?entries.OrderByDescending(item=>VariantRank(item.Variant)).ThenByDescending(item=>item.Variant,names)
                :entries.OrderBy(item=>VariantRank(item.Variant)).ThenBy(item=>item.Variant,names))
                .ThenBy(item=>ModDisplayName(item.Mod),names).Select(item=>item.Mod);
        }
        return mods.OrderBy(mod=>reverseOrder?-StatusRank(mod):StatusRank(mod))
            .ThenBy(mod=>ModDisplayName(mod),names);
    }
    void ShowColumnFilter(int index){
        string column=grid.Columns[index].Name;
        if(!IsFilterColumn(column))return;
        string uncat=UncatName();
        var values=new List<string>();
        string title;
        string[] sorts=null;
        HashSet<string> current=null;
        switch(column){
            case "Name":
                title=T("Filtrar mods","Filter mods");
                values=new[]{uncat}.Concat(PresetCategories()).ToList();
                current=categoryFilters;
                break;
            case "Group":
                title=T("Filtrar grupos","Filter groups");
                values=state.List.Mods.Select(VersionGroupName).Distinct().OrderBy(value=>value).ToList();
                current=groupFilters;
                break;
            case "Variant":
                title=T("Variantes","Variants");
                values=state.List.Mods.Select(NexusVariantName).Distinct().OrderBy(value=>value).ToList();
                current=variantFilters;
                sorts=new[]{T("Principais primeiro","Main versions first"),T("Ordem inversa","Reverse order")};
                break;
            case "Status":
                title=T("Situação","Status");
                values=new[]{T("Pendente","Pending"),T("Baixando","Downloading"),T("Download concluído","Download complete"),
                    T("Instalando","Installing"),T("Instalado","Installed"),
                    GeneralStatusPresentation.Label(GeneralStatusPresentation.OtherChoice),
                    GeneralStatusPresentation.Label(GeneralStatusPresentation.Disabled),
                    GeneralStatusPresentation.Label(GeneralStatusPresentation.ChooseDependency),
                    T("Falha","Failed")}.ToList();
                if(gameInventory.Any(file=>file.Kind=="found"))values.Add(GameInventoryStatus);
                foreach(var status in new[]{GeneralStatusPresentation.DetectedFiles,GeneralStatusPresentation.NexusManual,GeneralStatusPresentation.NexusAmbiguous})
                    if(statuses.Values.Contains(status))values.Add(GeneralStatusPresentation.Label(status));
                current=statusFilters;
                sorts=new[]{T("Instalados primeiro","Installed first"),T("Pendentes primeiro","Pending first")};
                break;
            default:
                title=T("Ordenar por espaço","Sort by space");
                sorts=new[]{T("Maior primeiro","Largest first"),T("Menor primeiro","Smallest first")};
                break;
        }
        IEnumerable<string> selected=current==null?null:column=="Status"
            ?values.Where(value=>current.Any(item=>StatusFilterKey(item)==StatusFilterKey(value)))
            :current.Select(value=>column=="Name"&&value==""?uncat:value);
        using(var dialog=new GeneralColumnFilterDialog(title,values,selected,nameFilter,column=="Name",sorts,column!="Size")){
            dialog.AnchorBelow(grid,grid.GetCellDisplayRectangle(index,-1,true),false);
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            var picked=dialog.ResultValues;
            if(column=="Name"){
                categoryFilters=picked.Count==values.Count?null:new HashSet<string>(picked.Select(value=>value==uncat?"":value));
                nameFilter=dialog.SearchText??"";
            }else if(column=="Group")groupFilters=picked.Count==values.Count?null:picked;
            else if(column=="Variant")variantFilters=picked.Count==values.Count?null:picked;
            else if(column=="Status")statusFilters=picked.Count==values.Count?null:picked;
            if(dialog.ReverseOrder.HasValue){orderColumn=column;reverseOrder=dialog.ReverseOrder.Value;}
            RedrawFromTop();
        }
    }
    void UpdateTableTitle(){
        if(tableName==null)return;
        if(!hasPresetDraft||draftPresetId!=state.SelectedPreset)CapturePresetDraft();
        var selected=ActivePreset;tableName.ReadOnly=selected==null;settingDraftName=true;
        try{tableName.Text=selected==null?T("Todos os itens","All items"):draftName;}finally{settingDraftName=false;}
        tableName.AccessibleName=T("Nome do preset","Preset name");
        UpdatePresetCommands();
    }
    void SaveTableTitle(){
        var selected=ActivePreset;if(selected==null||tableName==null||tableName.Text==selected.Name)return;
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
        var selected=ActivePreset;if(selected==null)create=true;
        using(var name=new GeneralNameDialog(T(create?"Novo preset":"Atualizar preset",create?"New preset":"Update preset"),create?"":selected.Name)){
            if(name.ShowDialog(this)!=DialogResult.OK)return;
            var ids=PresetCheckedIds();
            if(create)selected=new GeneralModPreset{Id="preset-"+Guid.NewGuid().ToString("N")};
            else if(GeneralDialog.Message(this,T("Substituir os itens deste preset pelos mods marcados?","Replace this preset's items with the checked mods?"),T("Atualizar preset","Update preset"),MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
            BeginProjectEdit();
            if(create)state.List.Presets.Add(selected);
            selected.Name=name.ResultName;selected.ModIds=ids;state.SelectedPreset=selected.Id;
            FinishProjectEdit();
            if(projectDraft!=null)SaveProjectEdits();
            CapturePresetDraft();RebuildPresets();
        }
    }
    void ClearPreset(){
        if(working||scanning)return;
        var selected=ActivePreset;
        if(selected==null){ResetColumnFilters();marked.Clear();DrawRows();return;}
        if(GeneralDialog.Message(this,T("Esvaziar este preset? Isso não desinstala mods nem apaga seus arquivos.","Empty this preset? This does not uninstall mods or delete their files."),T("Limpar preset","Clear preset"),MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
        BeginProjectEdit();
        selected.ModIds.Clear();selected.Categories.Clear();marked.Clear();ResetColumnFilters();FinishProjectEdit();DrawRows();
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
