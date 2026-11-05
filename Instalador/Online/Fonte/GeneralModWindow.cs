using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow : Form {
    readonly FixedCatalog catalog;
    readonly DownloadCache cache;
    readonly GeneralModLibrary library;
    readonly GeneralModList seed;
    readonly string data;
    readonly bool testMode;
    GeneralModState state;
    readonly HashSet<string> marked=new HashSet<string>();
    readonly Dictionary<string,string> statuses=new Dictionary<string,string>();
    readonly Dictionary<string,int> progress=new Dictionary<string,int>();
    readonly List<Control> idle=new List<Control>();
    TextBox destination;
    GeneralPicker filter,preset;
    DataGridView grid;
    Label detail,counts;
    Button next,listButton,reloadButton,optionsButton;
    int hoveredRow=-1;
    GeneralProgress currentProgress,totalProgress;
    bool drawing,working,scanning;
    string startupIssue;
    readonly ToolTip tips=new ToolTip{UseAnimation=false,UseFading=false};
    ContextMenuStrip listMenu;
    static string T(string pt,string en){return GeneralStyle.T(pt,en);}

    sealed class SectionRow {
        internal string Kind,Category;
        internal SectionRow(string kind,string category){Kind=kind;Category=category;}
    }
    internal GeneralModWindow(FixedCatalog catalog,DownloadCache cache,string appData,GeneralModState fixture=null) {
        this.catalog=catalog;this.cache=cache;data=appData;testMode=fixture!=null;
        if(testMode)GeneralStyle.AnimationsEnabled=false;
        if(testMode)launch=url=>{throw new IOException("A abertura do navegador foi bloqueada no teste.");};
        library=new GeneralModLibrary(Path.Combine(data,"lista.json"));
        seed=GeneralModLibrary.Seed(catalog,new ModImportItem[0]);state=fixture;
        if(state==null){try{state=library.Load();}catch(Exception ex){startupIssue=ex.Message;}}
        if(state!=null)GeneralModLibrary.RestoreKnownMappings(state.List,seed);
        if(state==null){
            var pending=new List<ModImportItem>();
            try{pending=new ModImporter(catalog,cache,Path.Combine(Path.GetDirectoryName(cache.Root),"importados-pendentes.json")).ReadPending().Where(x=>!ModImporter.ExcludedFromVanillaProfile(x)).ToList();}
            catch(Exception ex){startupIssue=(startupIssue??"")+" "+ex.Message;}
            state=new GeneralModState{List=GeneralModLibrary.Seed(catalog,pending),DownloadFolder=OnlineDownloadWatch.DefaultFolder(),Destination=Discovery.Find().FirstOrDefault()};
            try{
                string previous=Path.Combine(Path.GetDirectoryName(cache.Root),"pasta-downloads.json");Safe.NoLinks(previous);
                if(File.Exists(previous)&&new FileInfo(previous).Length<=8192)state.DownloadFolder=new OnlineDownloadWatch(Safe.Json.Deserialize<string>(File.ReadAllText(previous))).Folder;
            }catch(IOException){}catch(ArgumentException){}catch(UnauthorizedAccessException){}catch(InvalidOperationException){}
        }
        if(state.ResolvedDownloads==null)state.ResolvedDownloads=new Dictionary<string,string>();
        if(state.CyberpunkImports==null)state.CyberpunkImports=new Dictionary<string,string>();
        if(state.ChosenVariants==null)state.ChosenVariants=new Dictionary<string,string>();
        if(state.List.Presets.Count==0&&state.List.Mods.Any(m=>m.Id=="NightCoop")){
            var ids=new HashSet<string>(state.List.Mods.Select(m=>m.Id),StringComparer.Ordinal);
            foreach(var original in seed.Presets)state.List.Presets.Add(new GeneralModPreset{Id=original.Id,Name=original.Name,ModIds=original.ModIds.Where(ids.Contains).ToList()});
        }
        if(String.IsNullOrWhiteSpace(state.DownloadFolder))state.DownloadFolder=OnlineDownloadWatch.DefaultFolder();
        Text="Mod Loader 1.1.2";ClientSize=new Size(1240,820);MinimumSize=new Size(1000,680);StartPosition=FormStartPosition.CenterScreen;AutoScaleMode=AutoScaleMode.Dpi;
        BuildInterface();
        FormClosing+=(s,e)=>{
            if(working){RequestSkip();normal.Clear();priority.Clear();e.Cancel=true;detail.Text=T("Aguarde a operação atual terminar antes de fechar.","Wait for the current operation to finish before closing.");}
            else if(scanning){RequestSkip();e.Cancel=true;}
        };
        if(fixture==null)Shown+=async(s,e)=>await Scan();
    }
    void BuildInterface(){
        GeneralStyle.Dark=state.Theme=="dark";GeneralStyle.Language=state.Language??"pt";
        int oldFilter=filter==null?0:filter.SelectedIndex;
        string group=filter!=null&&oldFilter>=2?filter.SelectedItem as string:null;
        SuspendLayout();
        if(listMenu!=null){listMenu.Dispose();listMenu=null;}
        while(Controls.Count>0){var old=Controls[0];Controls.Remove(old);old.Dispose();}
        idle.Clear();Font=GeneralStyle.Font(12);BackColor=GeneralStyle.Background;ForeColor=GeneralStyle.Foreground;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(32,24,32,22),ColumnCount=1,RowCount=8,BackColor=BackColor};
        for(int i=0;i<8;i++)layout.RowStyles.Add(i==4?new RowStyle(SizeType.Percent,100):new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);
        var heading=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=3,Margin=Padding.Empty};
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,44));
        var titles=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=Padding.Empty};
        titles.Controls.Add(new Label{Text="MOD LOADER",AutoSize=true,Font=GeneralStyle.Font(27,true),ForeColor=GeneralStyle.Accent,Margin=Padding.Empty});
        titles.Controls.Add(new Label{Text=T("Criado por Nask e Codex. Se achou ruim, faz você.","Made by Nask and Codex. Don't like it? Make your own."),AutoSize=true,Font=GeneralStyle.Font(10.5f),ForeColor=GeneralStyle.Muted,Margin=new Padding(0,0,0,14)});
        heading.Controls.Add(titles,0,0);
        listButton=Command(T("Gerenciar","Manage"),ToggleListMenu,true);listButton.Anchor=AnchorStyles.Top|AnchorStyles.Right;listButton.Margin=new Padding(0,0,12,0);heading.Controls.Add(listButton,1,0);
        optionsButton=IconButton("menu",T("Opções","Options"),ShowOptions,true);((GeneralButton)optionsButton).Bare=true;optionsButton.Anchor=AnchorStyles.Top|AnchorStyles.Right;optionsButton.Margin=Padding.Empty;
        heading.Controls.Add(optionsButton,2,0);layout.Controls.Add(heading,0,0);
        var path=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=2,Margin=new Padding(0,0,0,14)};
        path.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));path.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var pathSurface=new GeneralSurface{Dock=DockStyle.Fill,Height=44,MinimumSize=new Size(100,44),MaximumSize=new Size(0,44),Padding=new Padding(16,11,16,0),Margin=new Padding(0,0,12,0)};
        destination=new TextBox{ReadOnly=true,BorderStyle=BorderStyle.None,Dock=DockStyle.Fill,Text=state.Destination??"",Font=GeneralStyle.Font(11),AccessibleName=T("Pasta de instalação","Installation folder")};
        pathSurface.Controls.Add(destination);path.Controls.Add(pathSurface,0,0);path.Controls.Add(Command(T("Escolher pasta","Choose folder"),()=>ChooseFolder(false),true),1,0);layout.Controls.Add(path,0,1);
        var filterRow=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=1,Margin=new Padding(0,0,0,10)};
        filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        var filters=Flow();filters.Margin=Padding.Empty;filters.WrapContents=false;filterRow.Controls.Add(filters,0,0);layout.Controls.Add(filterRow,0,2);
        filters.Controls.Add(FieldLabel(T("Categoria","Category")));
        filter=Picker(210);filters.Controls.Add(filter);
        filters.Controls.Add(FieldLabel(T("Preset","Preset")));
        preset=Picker(250);filters.Controls.Add(preset);
        var editPreset=Command(T("Editar preset","Edit preset"),()=>EditPreset(false),true);filters.Controls.Add(editPreset);
        reloadButton=IconButton("refresh",T("Atualizar lista","Refresh list"),async()=>await Scan(),true);((GeneralButton)reloadButton).Bare=true;filters.Controls.Add(reloadButton);
        var summary=Flow();summary.Margin=new Padding(0,0,0,12);layout.Controls.Add(summary,0,3);
        counts=new Label{AutoSize=true,Font=GeneralStyle.Font(10.5f),ForeColor=GeneralStyle.Muted,Margin=Padding.Empty};summary.Controls.Add(counts);
        grid=new DataGridView{Dock=DockStyle.Fill,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,AllowUserToResizeColumns=false,
            RowHeadersVisible=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,BackgroundColor=GeneralStyle.Surface,BorderStyle=BorderStyle.None,
            CellBorderStyle=DataGridViewCellBorderStyle.None,ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None,AutoGenerateColumns=false,EnableHeadersVisualStyles=false,
            ScrollBars=ScrollBars.Vertical,Margin=Padding.Empty,ColumnHeadersHeight=44,ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing};
        grid.DefaultCellStyle=new DataGridViewCellStyle{BackColor=GeneralStyle.Surface,ForeColor=GeneralStyle.Foreground,SelectionBackColor=GeneralStyle.Selection,SelectionForeColor=GeneralStyle.Foreground,Font=GeneralStyle.Font(12),Padding=new Padding(10,0,8,0)};
        grid.ColumnHeadersDefaultCellStyle=new DataGridViewCellStyle{BackColor=GeneralStyle.Surface,ForeColor=GeneralStyle.Muted,Font=GeneralStyle.Font(10.5f,true),Padding=new Padding(10,0,8,0),SelectionBackColor=GeneralStyle.Surface};
        grid.Columns.Add(new DataGridViewCheckBoxColumn{Name="Mark",HeaderText="",Width=44,SortMode=DataGridViewColumnSortMode.NotSortable});
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Name",HeaderText="Mods",AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill,ReadOnly=true,SortMode=DataGridViewColumnSortMode.NotSortable});
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Variant",HeaderText=T("Variante","Variant"),Width=160,ReadOnly=true,SortMode=DataGridViewColumnSortMode.NotSortable});
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Status",HeaderText=T("Situação","Status"),Width=205,ReadOnly=true,SortMode=DataGridViewColumnSortMode.NotSortable});
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Progress",Visible=false,ReadOnly=true});
        grid.RowTemplate.Height=44;
        grid.CurrentCellDirtyStateChanged+=(s,e)=>{if(grid.IsCurrentCellDirty)grid.CommitEdit(DataGridViewDataErrorContexts.Commit);};
        grid.CellValueChanged+=(s,e)=>{
            if(drawing||e.RowIndex<0||e.ColumnIndex!=0)return;
            var row=grid.Rows[e.RowIndex];string id=row.Tag as string;if(id==null)return;
            if(Equals(row.Cells[0].Value,true))marked.Add(id);else marked.Remove(id);UpdateCounts();
        };
        grid.ColumnHeaderMouseClick+=(s,e)=>{if(e.ColumnIndex!=0)return;var rows=ModRows().ToList();bool check=rows.Any(r=>!marked.Contains((string)r.Tag));foreach(var r in rows)r.Cells[0].Value=check;};
        grid.CellClick+=(s,e)=>{if(e.RowIndex>=0)HandleSection(grid.Rows[e.RowIndex].Tag as SectionRow);};
        grid.CellDoubleClick+=(s,e)=>{if(e.RowIndex>=0&&e.ColumnIndex>0){var id=grid.Rows[e.RowIndex].Tag as string;if(id!=null)Edit(state.List.Mods.Single(m=>m.Id==id));}};
        grid.KeyDown+=(s,e)=>{if(e.KeyCode!=Keys.Enter&&e.KeyCode!=Keys.F2)return;e.Handled=true;var item=FocusedEntry();if(item!=null)Edit(item);else if(grid.CurrentRow!=null)HandleSection(grid.CurrentRow.Tag as SectionRow);};
        grid.RowPrePaint+=PaintSection;grid.CellPainting+=PaintName;
        grid.CellMouseEnter+=(s,e)=>SetHover(e.RowIndex);
        grid.MouseLeave+=(s,e)=>SetHover(-1);
        grid.DataError+=(s,e)=>{e.ThrowException=false;};
        var menu=GeneralMenuRenderer.Create();
        GeneralMenuRenderer.Add(menu,T("Detalhes e variantes","Details and variants"),()=>{var item=FocusedEntry();if(item!=null)Edit(item);});
        GeneralMenuRenderer.Add(menu,T("Abrir fonte no navegador","Open source in browser"),()=>Guard(()=>{var item=FocusedEntry();if(item!=null){var url=Selected(item).Sources.FirstOrDefault(GeneralModDownload.IsWebSource);if(url!=null)GeneralModDownload.OpenSource(url);}}));
        GeneralMenuRenderer.Add(menu,T("Tirar da lista","Remove from list"),()=>Guard(RemoveEntry));
        menu.Items.Add(new ToolStripSeparator());
        var undo=GeneralMenuRenderer.Add(menu,T("Cyberpunk: desfazer operação","Cyberpunk: undo operation"),async()=>await CyberpunkMaintenance(false));
        var clean=GeneralMenuRenderer.Add(menu,T("Cyberpunk: limpar extras","Cyberpunk: remove extra mods"),async()=>await CyberpunkMaintenance(true));
        menu.Opening+=(s,e)=>{bool available=!working&&!scanning&&IsCyberpunk(state.Destination);undo.Visible=available;clean.Visible=available;e.Cancel=FocusedEntry()==null;};
        grid.CellMouseDown+=(s,e)=>{if(e.Button==MouseButtons.Right&&e.RowIndex>=0&&grid.Rows[e.RowIndex].Tag is string)grid.CurrentCell=grid.Rows[e.RowIndex].Cells[1];};grid.ContextMenuStrip=menu;
        layout.Controls.Add(grid,0,4);
        detail=new Label{Dock=DockStyle.Fill,AutoSize=true,Font=GeneralStyle.Font(10.5f),ForeColor=GeneralStyle.Muted,Margin=new Padding(0,10,0,4),MaximumSize=new Size(0,46)};
        detail.Text=startupIssue??T("Dê dois cliques para editar. Use Adicionar item no final de cada categoria ou arraste um ZIP, RAR ou 7z.","Double-click to edit. Use Add item at the end of a category or drop a ZIP, RAR or 7z.");
        layout.Controls.Add(detail,0,5);
        var bars=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=2,RowCount=2,Margin=new Padding(0,4,0,10)};
        bars.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));bars.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        next=Command(T("Pular mod","Skip mod"),RequestSkip);next.Visible=false;next.MinimumSize=new Size(112,44);next.Margin=new Padding(0,3,14,0);bars.Controls.Add(next,0,0);
        currentProgress=new GeneralProgress{Dock=DockStyle.Fill,Caption=T("Item atual","Current item"),Margin=new Padding(0,0,0,5)};
        totalProgress=new GeneralProgress{Dock=DockStyle.Fill,Caption=T("Progresso geral","Overall progress"),Margin=Padding.Empty};
        bars.Controls.Add(currentProgress,1,0);bars.Controls.Add(totalProgress,1,1);layout.Controls.Add(bars,0,6);
        var bottom=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=2,Margin=Padding.Empty};bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var remove=Command(T("Desinstalar selecionados","Uninstall selected"),async()=>await Uninstall(),true);((GeneralButton)remove).Destructive=true;bottom.Controls.Add(remove,0,0);remove.Anchor=AnchorStyles.Left;
        var install=Command(T("Baixar/Instalar","Download/Install"),StartSelected);((GeneralButton)install).Primary=true;install.MinimumSize=new Size(226,52);install.Font=GeneralStyle.Font(13,true);bottom.Controls.Add(install,1,0);
        layout.Controls.Add(bottom,0,7);
        foreach(Control control in new Control[]{destination,filter,preset}){control.BackColor=GeneralStyle.Surface;control.ForeColor=GeneralStyle.Foreground;}
        EnableDrops(this);
        RebuildFilter(group,oldFilter);RebuildPresets();
        filter.SelectedIndexChanged+=(s,e)=>{if(!drawing)DrawRows();};
        preset.SelectedIndexChanged+=(s,e)=>{if(drawing)return;state.SelectedPreset=preset.SelectedIndex>0?state.List.Presets[preset.SelectedIndex-1].Id:null;Guard(Persist);DrawRows();};
        idle.Add(preset);Busy(working||scanning);ResumeLayout(true);GeneralNativeTheme.ApplyTree(this);
    }
    static Label FieldLabel(string text){return new Label{Text=text,AutoSize=true,Font=GeneralStyle.Font(11),ForeColor=GeneralStyle.Muted,Margin=new Padding(0,11,10,0)};}
    static FlowLayoutPanel Flow(){return new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,WrapContents=true,BackColor=Color.Transparent};}
    static GeneralPicker Picker(int width){return new GeneralPicker{DropDownStyle=ComboBoxStyle.DropDownList,Width=width,Font=GeneralStyle.Font(12),Margin=new Padding(0,0,16,0),BackColor=GeneralStyle.Surface,ForeColor=GeneralStyle.Foreground};}
    Button Command(string text,Action action,bool onlyIdle=false){
        var b=GeneralStyle.Button(text,()=>Guard(action));b.Margin=new Padding(0,0,10,0);b.Font=GeneralStyle.Font(11);if(onlyIdle)idle.Add(b);return b;
    }
    Button IconButton(string kind,string title,Action action,bool onlyIdle=false){
        var button=(GeneralButton)Command("",action,onlyIdle);button.IconKind=kind;button.AutoSize=false;button.MinimumSize=new Size(44,44);button.Size=new Size(44,44);button.AccessibleName=title;tips.SetToolTip(button,title);return button;
    }
    void EnableDrops(Control target){
        target.AllowDrop=true;target.DragEnter+=(s,e)=>e.Effect=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;
        target.DragDrop+=(s,e)=>Guard(()=>Drop((string[])e.Data.GetData(DataFormats.FileDrop)));
        foreach(Control child in target.Controls)EnableDrops(child);
    }
    void Guard(Action action){try{action();}catch(Exception ex){ShowError(ex);}}
    void ShowError(Exception ex){detail.Text=ex.Message;GeneralDialog.Message(this,ex.Message,T("Não foi possível concluir","Unable to complete"),MessageBoxButtons.OK,MessageBoxIcon.Warning);}
    GeneralModEntry FocusedEntry(){string id=grid.CurrentRow==null?null:grid.CurrentRow.Tag as string;return id==null?null:state.List.Mods.FirstOrDefault(x=>x.Id==id);}
    IEnumerable<DataGridViewRow> ModRows(){return grid.Rows.Cast<DataGridViewRow>().Where(r=>r.Tag is string);}
    GeneralModVariant Selected(GeneralModEntry entry){string id;return state.ChosenVariants.TryGetValue(entry.Id,out id)?entry.Variants.FirstOrDefault(v=>v.Id==id)??entry.Variants.First(v=>v.Id==entry.DefaultVariant):entry.Variants.First(v=>v.Id==entry.DefaultVariant);}
    static string Key(GeneralModEntry entry,GeneralModVariant variant){return entry.Id+"/"+variant.Id;}
    void Persist(){library.Save(state);startupIssue=null;}
    string UncatName(){return state.List.UncategorizedName=="Sem categoria"?T("Sem categoria","Uncategorized"):state.List.UncategorizedName;}
    void RebuildFilter(string selected=null,int old=0){
        if(selected==null&&filter.SelectedIndex>=2)selected=filter.SelectedItem as string;
        drawing=true;filter.Items.Clear();filter.Items.AddRange(new object[]{T("Todas","All categories"),UncatName()});filter.Items.AddRange(state.List.Categories.Cast<object>().ToArray());
        int found=selected==null?-1:state.List.Categories.IndexOf(selected);filter.SelectedIndex=found>=0?found+2:old==1?1:0;drawing=false;DrawRows();
    }
    void RebuildPresets(){
        drawing=true;preset.Items.Clear();preset.Items.Add(T("Todos os itens","All items"));preset.Items.AddRange(state.List.Presets.Select(p=>p.Name).Cast<object>().ToArray());
        int selected=state.List.Presets.FindIndex(p=>p.Id==state.SelectedPreset);preset.SelectedIndex=selected+1;drawing=false;DrawRows();
    }
    List<GeneralModEntry> VisibleMods(){
        var active=state.List.Presets.FirstOrDefault(p=>p.Id==state.SelectedPreset);
        var allowed=active==null?null:new HashSet<string>(active.ModIds,StringComparer.Ordinal);
        return state.List.Mods.Where(m=>(allowed==null||allowed.Contains(m.Id))&&
            (filter.SelectedIndex<1||filter.SelectedIndex==1&&String.IsNullOrEmpty(m.Category)||filter.SelectedIndex>=2&&m.Category==(string)filter.SelectedItem)).ToList();
    }
    void DrawRows(){
        if(grid==null)return;string old=grid.CurrentRow==null?null:grid.CurrentRow.Tag as string;
        int scroll=grid.FirstDisplayedScrollingRowIndex;drawing=true;hoveredRow=-1;grid.Rows.Clear();
        var visible=VisibleMods();var groups=new List<string>();
        if(filter.SelectedIndex<=1)groups.Add(null);
        if(filter.SelectedIndex!=1)groups.AddRange(filter.SelectedIndex>=2?(IEnumerable<string>)new[]{(string)filter.SelectedItem}:state.List.Categories);
        foreach(var category in groups){
            if(grid.Rows.Count>0)AddSection("divider",category,23);
            string description;
            if(category==null)description=state.List.UncategorizedDescription;
            else if(!state.List.CategoryDescriptions.TryGetValue(category,out description))description=null;
            AddSection("heading",category,String.IsNullOrWhiteSpace(description)?54:78);
            foreach(var m in visible.Where(m=>String.Equals(m.Category,category,StringComparison.OrdinalIgnoreCase)||String.IsNullOrEmpty(m.Category)&&category==null)){
                var v=Selected(m);string key=Key(m,v),status;int pct;statuses.TryGetValue(key,out status);bool hasProgress=progress.TryGetValue(key,out pct);
                int i=grid.Rows.Add(marked.Contains(m.Id),m.Name,v.Name,GeneralStatusPresentation.Label(status),hasProgress?(object)pct:null);var row=grid.Rows[i];row.Tag=m.Id;row.Cells["Status"].ToolTipText=GeneralStyle.Status(status??"Não conferido");
                row.Cells["Name"].ToolTipText=m.Description??"";row.Cells["Status"].ToolTipText=status??"";
                if(m.Id==old)grid.CurrentCell=row.Cells[1];
            }
            AddSection("add",category,44);
        }
        AddSection("new",null,56);
        if(old==null)grid.ClearSelection();
        if(scroll>=0&&scroll<grid.RowCount)try{grid.FirstDisplayedScrollingRowIndex=scroll;}catch(InvalidOperationException){}
        drawing=false;UpdateCounts();
    }
    void AddSection(string kind,string category,int height){
        int i=grid.Rows.Add();var row=grid.Rows[i];row.Tag=new SectionRow(kind,category);row.Height=height;row.ReadOnly=true;
        row.Cells["Name"].Value=kind=="heading"?category??UncatName():kind=="add"?T("Adicionar item","Add item"):kind=="new"?T("Criar nova categoria","Create new category"):"";
    }
    void UpdateCounts(){
        if(counts==null)return;var rows=ModRows().ToList();int selected=rows.Count(r=>marked.Contains((string)r.Tag));
        grid.Columns[0].HeaderText="";
        grid.Columns[0].HeaderCell.ToolTipText=T("Selecionar todos os itens visíveis","Select all visible items");
        grid.InvalidateCell(0,-1);
        int installed=rows.Count(r=>{var m=state.List.Mods.Single(x=>x.Id==(string)r.Tag);string status;return statuses.TryGetValue(Key(m,Selected(m)),out status)&&GeneralStatusPresentation.Kind(status)=="installed";});
        int failed=rows.Count(r=>{var m=state.List.Mods.Single(x=>x.Id==(string)r.Tag);string status;return statuses.TryGetValue(Key(m,Selected(m)),out status)&&GeneralStatusPresentation.Kind(status)=="error";});
        counts.Text=T("Mods na lista: ","Mods in list: ")+rows.Count+"   ·   "+T("Instalados: ","Installed: ")+installed+"   ·   "+T("Falhas: ","Failed: ")+failed;
    }
    void PaintSection(object sender,DataGridViewRowPrePaintEventArgs e){
        var section=grid.Rows[e.RowIndex].Tag as SectionRow;if(section==null)return;
        e.Handled=true;var g=e.Graphics;var bounds=e.RowBounds;bounds.Width=grid.ClientSize.Width-(grid.Controls.OfType<VScrollBar>().Any(s=>s.Visible)?SystemInformation.VerticalScrollBarWidth:0);
        using(var b=new SolidBrush(e.RowIndex==hoveredRow&&section.Kind!="divider"?GeneralStyle.Selection:GeneralStyle.Surface))g.FillRectangle(b,bounds);
        if(section.Kind=="divider"){using(var p=new Pen(GeneralStyle.SoftLine))g.DrawLine(p,bounds.X+20,bounds.Y+bounds.Height/2,bounds.Right-20,bounds.Y+bounds.Height/2);return;}
        string text,description=null;int y=bounds.Y+8;
        Font font=GeneralStyle.Font(12);
        if(section.Kind=="heading"){
            text=section.Category??UncatName();font=GeneralStyle.Font(15,true);y=bounds.Y+12;
            if(section.Category==null)description=state.List.UncategorizedDescription;else state.List.CategoryDescriptions.TryGetValue(section.Category,out description);
        }else text=section.Kind=="add"?"+ "+T("Adicionar item","Add item"):"+ "+T("Criar nova categoria","Create new category");
        TextRenderer.DrawText(g,text,font,new Rectangle(bounds.X+18,y,bounds.Width-40,30),section.Kind=="heading"?GeneralStyle.Foreground:GeneralStyle.Accent,TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
        if(!String.IsNullOrWhiteSpace(description))TextRenderer.DrawText(g,description.Replace("\r"," ").Replace("\n"," "),GeneralStyle.Font(10.5f),new Rectangle(bounds.X+18,y+31,bounds.Width-40,24),GeneralStyle.Muted,TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
    }
    void PaintName(object sender,DataGridViewCellPaintingEventArgs e){
        if(e.RowIndex>=0&&e.ColumnIndex==grid.Columns["Status"].Index&&grid.Rows[e.RowIndex].Tag is string){PaintStatus(e);return;}
        if(e.RowIndex==-1&&e.ColumnIndex==0){
            e.PaintBackground(e.ClipBounds,true);var rows=ModRows().ToList();int selected=rows.Count(r=>marked.Contains((string)r.Tag));
            var value=selected==0?System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal:selected==rows.Count?System.Windows.Forms.VisualStyles.CheckBoxState.CheckedNormal:System.Windows.Forms.VisualStyles.CheckBoxState.MixedNormal;
            CheckBoxRenderer.DrawCheckBox(e.Graphics,new Point(e.CellBounds.Left+16,e.CellBounds.Top+(e.CellBounds.Height-14)/2),value);e.Handled=true;return;
        }
        if(e.RowIndex<0||e.ColumnIndex!=1)return;string id=grid.Rows[e.RowIndex].Tag as string;if(id==null)return;
        var mod=state.List.Mods.Single(m=>m.Id==id);if(!mod.Nsfw)return;
        e.PaintBackground(e.ClipBounds,true);int suffix=54;var box=e.CellBounds;box.Inflate(-10,0);
        int nameWidth=Math.Min(box.Width-suffix,TextRenderer.MeasureText(mod.Name,e.CellStyle.Font).Width);
        TextRenderer.DrawText(e.Graphics,mod.Name,e.CellStyle.Font,new Rectangle(box.X,box.Y,Math.Max(1,nameWidth),box.Height),e.CellStyle.ForeColor,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);
        TextRenderer.DrawText(e.Graphics,"NSFW",GeneralStyle.Font(9.5f,true),new Rectangle(box.X+nameWidth+5,box.Y,suffix-5,box.Height),GeneralStyle.Danger,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
        e.Handled=true;
    }
    void SetHover(int row){
        if(row==hoveredRow)return;
        int old=hoveredRow;hoveredRow=row;
        foreach(int at in new[]{old,row}){
            if(at<0||at>=grid.RowCount)continue;
            var section=grid.Rows[at].Tag as SectionRow;if(section!=null&&section.Kind=="divider")continue;
            grid.Rows[at].DefaultCellStyle.BackColor=at==hoveredRow?GeneralStyle.Selection:GeneralStyle.Surface;grid.InvalidateRow(at);
        }
        grid.Cursor=row>=0&&row<grid.RowCount?Cursors.Hand:Cursors.Default;
    }
    void PaintStatus(DataGridViewCellPaintingEventArgs e){
        string id=(string)grid.Rows[e.RowIndex].Tag,raw;var item=state.List.Mods.Single(m=>m.Id==id);
        if(!statuses.TryGetValue(Key(item,Selected(item)),out raw))raw="Não conferido";
        string kind=GeneralStatusPresentation.Kind(raw),label=GeneralStatusPresentation.Label(raw);
        Color fill=ColorTranslator.FromHtml(kind=="installed"?"#157347":kind=="error"?"#b42348":kind=="active"?"#a85000":"#202026");
        e.PaintBackground(e.ClipBounds,true);e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using(var font=GeneralStyle.Font(10.5f)){
            int width=Math.Min(e.CellBounds.Width-20,TextRenderer.MeasureText(label,font).Width+22);
            var rect=new Rectangle(e.CellBounds.X+10,e.CellBounds.Y+(e.CellBounds.Height-30)/2,Math.Max(20,width),30);
            using(var round=GeneralStyle.Round(rect,15))using(var brush=new SolidBrush(fill))e.Graphics.FillPath(brush,round);
            TextRenderer.DrawText(e.Graphics,label,font,new Rectangle(rect.X+11,rect.Y,rect.Width-22,rect.Height),Color.White,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
        }
        e.Handled=true;
    }
    void HandleSection(SectionRow section){
        if(section==null||working||scanning)return;
        if(section.Kind=="add"){var item=NewEntry("",null);item.Category=section.Category;Edit(item);}
        if(section.Kind=="heading")EditCategory(section.Category,false);
        if(section.Kind=="new")EditCategory(null,true);
    }
    static GeneralModEntry NewEntry(string name,string source){
        var v=new GeneralModVariant{Id="default",Name=T("Principal","Main")};if(source!=null)v.Sources.Add(source);
        return new GeneralModEntry{Id="mod-"+Guid.NewGuid().ToString("N"),Name=name,DefaultVariant=v.Id,Variants=new List<GeneralModVariant>{v}};
    }
    async void Edit(GeneralModEntry item){await EditMod(item);}
    async Task EditMod(GeneralModEntry item){
        if(scanning)return;
        if((current!=null&&current.Entry.Id==item.Id)||(priorityCurrent!=null&&priorityCurrent.Entry.Id==item.Id)){detail.Text=T("Aguarde esta operação terminar antes de editar suas fontes.","Wait for this operation to finish before editing its sources.");return;}
        try{
            bool existing=state.List.Mods.Contains(item);var selected=Selected(item);
            string info=T("Destino: ","Destination: ")+(state.Destination??T("Escolha uma pasta na tela principal.","Choose a folder on the main screen."));
            var recipe=catalog.Packages.FirstOrDefault(m=>m.Id==selected.CyberpunkId);if(recipe!=null)info=T("Versão do pacote: ","Package version: ")+recipe.Version+"   ·   "+info;
            using(var dialog=new GeneralModEditor(item,state.List.Categories,selected.Id,!working||current!=null,info)){
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                ApplyEdited(item,dialog.Result,dialog.SelectedVariant,existing);DrawRows();
                if(dialog.InstallRequested)Enqueue(new[]{MakeWork(dialog.Result)},true);else if(!working)await Scan();
            }
        }catch(Exception ex){ShowError(ex);}
    }
    void ApplyEdited(GeneralModEntry before,GeneralModEntry after,string variant,bool existing){
        if(existing){
            foreach(var pendingList in new[]{normal,priority})foreach(var queued in pendingList.Where(w=>w.Entry.Id==before.Id).ToList()){
                var nextVariant=after.Variants.FirstOrDefault(v=>v.Id==variant);
                if(nextVariant!=null&&queued.Variant.Id==variant&&queued.Variant.CyberpunkId==nextVariant.CyberpunkId&&queued.Variant.CyberpunkVariant==nextVariant.CyberpunkVariant&&queued.Variant.Sources.SequenceEqual(nextVariant.Sources,StringComparer.Ordinal)){
                    queued.Entry=after;queued.Variant=nextVariant;
                }else{pendingList.Remove(queued);operationTotal=Math.Max(operationFinished,operationTotal-1);}
            }
            foreach(var old in before.Variants){
                var replacement=after.Variants.FirstOrDefault(v=>v.Id==old.Id);
                if(replacement==null||!replacement.Sources.SequenceEqual(old.Sources,StringComparer.Ordinal)){statuses.Remove(Key(before,old));progress.Remove(Key(before,old));}
            }
            if(working)UpdateFlow(null,"");
        }
        string group=after.Category;
        if(!String.IsNullOrEmpty(group)&&!state.List.Categories.Contains(group,StringComparer.OrdinalIgnoreCase))state.List.Categories.Add(group);
        if(existing)state.List.Mods[state.List.Mods.IndexOf(before)]=after;else{
            state.List.Mods.Add(after);var active=state.List.Presets.FirstOrDefault(p=>p.Id==state.SelectedPreset);
            if(active!=null&&!active.ModIds.Contains(after.Id))active.ModIds.Add(after.Id);
        }
        if(variant==after.DefaultVariant)state.ChosenVariants.Remove(after.Id);else state.ChosenVariants[after.Id]=variant;
        Persist();RebuildFilter(after.Category,after.Category==null?1:0);
    }
    async void Drop(string[] files){
        if(files==null||scanning)return;
        try{
            var paths=files.Where(f=>GeneralModDownload.IsArchivePath(f)&&File.Exists(f)).Select(GeneralModLibrary.ValidateSource).ToList();
            if(paths.Count==0)throw new IOException(T("Arraste um arquivo ZIP, RAR ou 7z concluído.","Drop a completed ZIP, RAR or 7z file."));
            if(waiting&&priorityCurrent==null&&current!=null&&paths.Count==1){
                var target=current;string source=paths[0];
                var added=NewEntry(Path.GetFileNameWithoutExtension(source),source);added.Category=filter.SelectedIndex>=2?(string)filter.SelectedItem:null;
                editingAttachment=true;
                try{using(var editor=new GeneralModEditor(added,state.List.Categories,added.DefaultVariant,true,T("Adicione este mod ou associe o arquivo ao download atual.","Add this mod or attach its file to the current download."))){
                    editor.OfferAttachment(target.Entry);
                    if(editor.ShowDialog(this)!=DialogResult.OK)return;
                    if(editor.AttachRequested){
                        if(!Object.ReferenceEquals(current,target)||!waiting)return;
                        var meta=editor.Result;
                        var updated=new GeneralModEntry{Id=target.Entry.Id,Name=meta.Name,Category=meta.Category,Description=meta.Description,Tags=meta.Tags,Nsfw=meta.Nsfw,DefaultVariant=target.Entry.DefaultVariant,Variants=target.Entry.Variants};
                        ApplyEdited(target.Entry,updated,target.Variant.Id,true);target.Entry=updated;association=source;
                    }else{
                        ApplyEdited(added,editor.Result,editor.SelectedVariant,false);
                        if(editor.InstallRequested)Enqueue(new[]{MakeWork(editor.Result)},true);
                    }
                }}finally{editingAttachment=false;}
                return;
            }
            foreach(string file in paths){
                var known=state.List.Mods.FirstOrDefault(m=>m.Variants.Any(v=>v.Sources.Contains(file,StringComparer.OrdinalIgnoreCase)));
                var entry=known??NewEntry(Path.GetFileNameWithoutExtension(file),file);
                if(known==null)entry.Category=filter.SelectedIndex>=2?(string)filter.SelectedItem:null;
                await EditMod(entry);
            }
        }catch(Exception ex){ShowError(ex);}
    }
    void RemoveEntry(){
        var item=FocusedEntry();if(item==null||working||scanning)return;
        if(GeneralDialog.Message(this,T("Tirar da lista sem desinstalar?","Remove from the list without uninstalling?")+"\n"+item.Name,T("Tirar da lista","Remove from list"),MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
        state.List.Mods.Remove(item);state.ChosenVariants.Remove(item.Id);foreach(var p in state.List.Presets)p.ModIds.Remove(item.Id);marked.Remove(item.Id);Persist();DrawRows();
    }
    void Busy(bool value){foreach(var control in idle)control.Enabled=!value;if(next!=null){next.Visible=working||scanning;next.Enabled=working||scanning;}}
    internal void Preview(string path){
        ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;Location=new Point(-30000,-30000);Show();Application.DoEvents();PerformLayout();
        using(var bmp=new Bitmap(Width,Height)){DrawToBitmap(bmp,new Rectangle(0,0,Width,Height));bmp.Save(path,System.Drawing.Imaging.ImageFormat.Png);}Close();
    }
}

