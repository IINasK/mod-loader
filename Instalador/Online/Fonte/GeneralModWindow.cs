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
    readonly Dictionary<string,string> statusDetails=new Dictionary<string,string>();
    readonly Dictionary<string,int> progress=new Dictionary<string,int>();
    readonly List<Control> idle=new List<Control>();
    GeneralPicker preset;
    DataGridView grid;
    Label detail,counts;
    Button next,reloadButton,optionsButton,expandList,projectsButton,homeButton;
    Label storageSize;
    TableLayoutPanel mainLayout;
    bool listExpanded;
    int hoveredRow=-1;
    GeneralProgress currentProgress,totalProgress;
    bool drawing,working,scanning;
    string startupIssue;
    readonly ToolTip tips=new ToolTip{UseAnimation=false,UseFading=false};
    readonly GeneralMenuHost menus=new GeneralMenuHost();
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
        if(state!=null){
            GeneralModLibrary.RestoreKnownMappings(state.List,seed);
            if(state.List.Mods.Any(m=>m.Id=="NightCoop")){
                foreach(var item in state.List.Presets){
                    if(item.Id=="all"&&item.Name=="Todos")item.Name="Com DLC Phantom Liberty";
                    if(item.Id=="no-pl"&&item.Name=="Sem Phantom Liberty")item.Name="Sem DLC Phantom Liberty";
                }
            }
        }
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
        if(state.SnapshotReceipts==null)state.SnapshotReceipts=new Dictionary<string,string>();
        if(state.List.Presets.Count==0&&state.List.Mods.Any(m=>m.Id=="NightCoop")){
            var ids=new HashSet<string>(state.List.Mods.Select(m=>m.Id),StringComparer.Ordinal);
            foreach(var original in seed.Presets)state.List.Presets.Add(new GeneralModPreset{Id=original.Id,Name=original.Name,ModIds=original.ModIds.Where(ids.Contains).ToList()});
        }
        if(String.IsNullOrWhiteSpace(state.DownloadFolder))state.DownloadFolder=OnlineDownloadWatch.DefaultFolder();
        if(!testMode)this.cache=new DownloadCache(Path.Combine(data,"Cache"),()=>state.DownloadFolder,cache.Root);
        if(state.SelectedPreset==null&&state.List.Presets.Any(p=>p.Id=="all"))state.SelectedPreset="all";
        Text="Mod Loader "+GeneralUpdateChecker.CurrentVersion;Icon=ModLoaderBrand.AppIcon;ClientSize=new Size(1240,820);MinimumSize=new Size(1000,680);StartPosition=FormStartPosition.CenterScreen;AutoScaleMode=AutoScaleMode.Dpi;
        BuildInterface();
        FormClosing+=(s,e)=>{
            if(working){RequestSkip();normal.Clear();priority.Clear();e.Cancel=true;detail.Text=T("Aguarde a operação atual terminar antes de fechar.","Wait for the current operation to finish before closing.");}
            else if(scanning){RequestSkip();e.Cancel=true;}
            else if(!ConfirmPresetNavigation())e.Cancel=true;
        };
        Disposed+=(s,e)=>menus.Dispose();
        Shown+=(s,e)=>RefreshStorageSize();
        if(fixture==null)Shown+=async(s,e)=>{
#if !VELOPACK
            CheckUpdatesOnStartup();
#endif
            await Scan();if(!IsDisposed)await ResumeP2P();
        };
    }
    void BuildInterface(){
        GeneralStyle.UseTheme(state.Theme);GeneralStyle.Language=state.Language??"pt";
        SuspendLayout();
        while(Controls.Count>0){var old=Controls[0];Controls.Remove(old);old.Dispose();}
        idle.Clear();Font=GeneralStyle.Font(12);BackColor=GeneralStyle.Background;ForeColor=GeneralStyle.Foreground;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(32,24,32,22),ColumnCount=1,RowCount=8,BackColor=BackColor};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        for(int i=0;i<8;i++)layout.RowStyles.Add(i==5?new RowStyle(SizeType.Percent,100):new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);mainLayout=layout;
        menus.Close();
        var heading=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=4,Margin=Padding.Empty};
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,44));heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,44));
        var titles=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=Padding.Empty};
        var brand=new FlowLayoutPanel{AutoSize=true,WrapContents=false,Margin=Padding.Empty};
        brand.Controls.Add(new ModLoaderLogo{Size=new Size(44,44),Margin=new Padding(0,0,12,0),Name="ProjectLogo"});
        brand.Controls.Add(new Label{Text="MOD LOADER",AutoSize=true,Font=GeneralStyle.Font(27,true),ForeColor=GeneralStyle.Accent,Margin=Padding.Empty});
        titles.Controls.Add(brand);
        titles.Controls.Add(new Label{Text=T("Criado por Nask e Codex. Se achou ruim, faz você.","Made by Nask and Codex. If you don't like it, make your own."),AutoSize=true,Font=GeneralStyle.Font(10.5f),ForeColor=GeneralStyle.Muted,Margin=new Padding(0,0,0,14)});
        heading.Controls.Add(titles,0,0);
        optionsButton=IconButton("menu",T("Menu","Menu"),ToggleListMenu,true);((GeneralButton)optionsButton).Bare=true;optionsButton.Anchor=AnchorStyles.Top|AnchorStyles.Right;optionsButton.Margin=Padding.Empty;
        projectsButton=Command(T("Projetos","Projects"),ToggleProjectsPanel,true);projectsButton.Name="ProjectsButton";((GeneralButton)projectsButton).IconKind="folder";projectsButton.Margin=new Padding(0,0,8,0);heading.Controls.Add(projectsButton,1,0);
        homeButton=IconButton("home",T("Voltar ao menu principal","Back to main menu"),BackToProjects,true);homeButton.Name="BackHome";((GeneralButton)homeButton).Bare=true;homeButton.Margin=Padding.Empty;heading.Controls.Add(homeButton,2,0);
        heading.Controls.Add(optionsButton,3,0);layout.Controls.Add(heading,0,0);
        preset=Picker(44);preset.MinimumSize=new Size(44,44);preset.Width=44;preset.Margin=Padding.Empty;preset.IconOnly=true;preset.AccessibleName=T("Escolher preset","Choose preset");tips.SetToolTip(preset,preset.AccessibleName);
        presetSettings=IconButton("save",T("Salvar preset","Save preset"),SaveCurrentPreset,true);((GeneralButton)presetSettings).Bare=true;presetSettings.Name="SavePreset";presetSettings.Margin=Padding.Empty;
        deletePreset=IconButton("trash",T("Excluir preset","Delete preset"),DeletePreset,true);((GeneralButton)deletePreset).Bare=true;((GeneralButton)deletePreset).Destructive=true;deletePreset.Name="DeletePreset";deletePreset.Margin=Padding.Empty;
        reloadButton=IconButton("refresh",T("Atualizar lista","Refresh list"),async()=>await Scan(),true);((GeneralButton)reloadButton).Bare=true;reloadButton.Margin=Padding.Empty;
        var summary=Flow();summary.Margin=new Padding(0,0,0,12);layout.Controls.Add(summary,0,3);
        counts=new Label{AutoSize=true,Font=GeneralStyle.Font(10.5f),ForeColor=GeneralStyle.Muted,Margin=Padding.Empty};summary.Controls.Add(counts);
        storageSize=new Label{Name="ProjectStorage",AutoSize=true,Font=GeneralStyle.Font(10.5f),ForeColor=GeneralStyle.Muted,Margin=new Padding(16,0,0,0)};summary.Controls.Add(storageSize);
        tips.SetToolTip(storageSize,T("Soma dos arquivos de mods identificados, downloads referenciados e dados locais do projeto. O mesmo caminho não é contado duas vezes. Cache contém registros e dados de apoio, sem novas cópias dos mods. Downloads originais entram somente no total.","Sum of identified mod files, referenced downloads and local project data. The same path is counted once. Cache contains records and support data, without new mod copies. Original downloads count only toward total."));
        var temporary=new LinkLabel{Text=T("Temporários P2P","P2P temporary files"),AutoSize=true,
            LinkColor=GeneralStyle.Accent,ActiveLinkColor=GeneralStyle.Foreground,Margin=new Padding(16,0,0,0),Font=GeneralStyle.Font(10.5f)};
        temporary.LinkClicked+=(s,e)=>Guard(()=>{
            string folder=P2PService.TransferStore;
            if(p2p!=null){
                var paths=p2p.SnapshotSessions().Where(session=>!session.Sender&&!session.Discarded).Select(p2p.Folder).Where(Directory.Exists).ToArray();
                if(paths.Length==1)folder=paths[0];
            }
            Safe.NoLinks(folder);
            if(!Directory.Exists(folder)){detail.Text=T("Nenhum arquivo temporário P2P disponível.","No P2P temporary files available.");return;}
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder){UseShellExecute=true});
        });
        summary.Controls.Add(temporary);
        AddUpdateNotice(summary);
        var tableHeading=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=6,Height=52,Margin=Padding.Empty,Padding=new Padding(14,4,0,4),BackColor=GeneralStyle.Surface};
        tableHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));for(int i=1;i<6;i++)tableHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,44));
        tableName=new TextBox{Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,Font=GeneralStyle.Font(16,true),MaxLength=120,Margin=new Padding(0,8,12,0),BackColor=GeneralStyle.Surface,ForeColor=GeneralStyle.Foreground};
        tableName.TextChanged+=(s,e)=>DraftTitleChanged();tableName.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;Guard(SaveCurrentPreset);}};
        tableHeading.Controls.Add(tableName,0,0);
        tableHeading.Controls.Add(preset,1,0);tableHeading.Controls.Add(presetSettings,2,0);tableHeading.Controls.Add(deletePreset,3,0);tableHeading.Controls.Add(reloadButton,4,0);
        expandList=IconButton("maximize",T("Expandir lista","Expand list"),()=>SetListExpanded(!listExpanded));((GeneralButton)expandList).Bare=true;expandList.Name="ExpandList";expandList.Margin=Padding.Empty;tableHeading.Controls.Add(expandList,5,0);
        var tableTitle=new Panel{Dock=DockStyle.Fill,Height=64,Margin=Padding.Empty,Padding=new Padding(0,0,0,12),BackColor=GeneralStyle.Surface,Name="PresetHeading"};
        tableTitle.Controls.Add(tableHeading);
        tableTitle.Paint+=(s,e)=>{using(var pen=new Pen(GeneralStyle.Line))e.Graphics.DrawLine(pen,16,tableTitle.Height-6,tableTitle.Width-17,tableTitle.Height-6);};
        var tableTools=new TableLayoutPanel{Name="TableTools",Dock=DockStyle.Fill,AutoSize=true,ColumnCount=1,RowCount=2,Margin=Padding.Empty,BackColor=GeneralStyle.Surface};
        tableTools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));tableTools.RowStyles.Add(new RowStyle(SizeType.Absolute,64));tableTools.RowStyles.Add(new RowStyle(SizeType.Absolute,62));
        tableTools.Controls.Add(tableTitle,0,0);tableTools.Controls.Add(BuildSearch(),0,1);layout.Controls.Add(tableTools,0,4);
        grid=new ModListGrid{Dock=DockStyle.Fill,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,AllowUserToResizeColumns=false,
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
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Size",HeaderText=T("Espaço","Space"),Width=112,ReadOnly=true,SortMode=DataGridViewColumnSortMode.NotSortable,DefaultCellStyle=new DataGridViewCellStyle{Alignment=DataGridViewContentAlignment.MiddleRight,ForeColor=GeneralStyle.Muted}});
        grid.Columns["Size"].DisplayIndex=2;grid.Columns["Size"].HeaderCell.ToolTipText=T("Espaço ocupado pelos arquivos instalados do mod; — significa não identificado.","Space used by installed mod files; — means not identified.");
        grid.RowTemplate.Height=44;
        grid.CurrentCellDirtyStateChanged+=(s,e)=>{if(grid.IsCurrentCellDirty)grid.CommitEdit(DataGridViewDataErrorContexts.Commit);};
        grid.CellValueChanged+=(s,e)=>{
            if(drawing||e.RowIndex<0||e.ColumnIndex!=0)return;
            var row=grid.Rows[e.RowIndex];string id=row.Tag as string;if(id==null)return;
            if(Equals(row.Cells[0].Value,true))marked.Add(id);else marked.Remove(id);UpdateCounts();
        };
        grid.ColumnHeaderMouseClick+=(s,e)=>{if(e.ColumnIndex>0){ShowColumnFilter(e.ColumnIndex);return;}var rows=ModRows().ToList();bool check=rows.Any(r=>!marked.Contains((string)r.Tag));foreach(var r in rows)r.Cells[0].Value=check;};
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
        var failureDetails=GeneralMenuRenderer.Add(menu,T("Ver erro da instalação","View installation error"),ShowFocusedFailure);
        failureDetails.Name="ViewInstallFailure";
        var copyFailures=GeneralMenuRenderer.Add(menu,T("Copiar erros da instalação","Copy installation errors"),CopyFailures);copyFailures.Name="CopyInstallFailures";
        var undo=GeneralMenuRenderer.Add(menu,T("Cyberpunk: desfazer operação","Cyberpunk: undo operation"),async()=>await CyberpunkMaintenance(false));
        var clean=GeneralMenuRenderer.Add(menu,T("Cyberpunk: limpar extras","Cyberpunk: remove extra mods"),async()=>await CyberpunkMaintenance(true));
        menu.Opening+=(s,e)=>{bool available=!working&&!scanning&&IsCyberpunk(state.Destination);undo.Visible=available;clean.Visible=available;var focused=FocusedEntry();string status;failureDetails.Visible=focused!=null&&statuses.TryGetValue(Key(focused,Selected(focused)),out status)&&GeneralStatusPresentation.Kind(status)=="error";copyFailures.Visible=statuses.Values.Any(value=>GeneralStatusPresentation.Kind(value)=="error");e.Cancel=focused==null;};
        grid.CellMouseDown+=(s,e)=>{if(e.Button==MouseButtons.Right&&e.RowIndex>=0&&grid.Rows[e.RowIndex].Tag is string)grid.CurrentCell=grid.Rows[e.RowIndex].Cells[1];};grid.ContextMenuStrip=menu;grid.Disposed+=(s,e)=>menu.Dispose();
        layout.Controls.Add(new GeneralScrollView(grid),0,5);
        var modGrid=grid;
        bool scrollUpdateQueued=false,showToolsAfterScroll=true;
        grid.Scroll+=(s,e)=>{
            if(e.ScrollOrientation!=ScrollOrientation.VerticalScroll||e.NewValue==e.OldValue)return;
            showToolsAfterScroll=e.NewValue==0||e.NewValue<e.OldValue;
            if(scrollUpdateQueued||!modGrid.IsHandleCreated)return;
            scrollUpdateQueued=true;
            // Let the native scroll finish moving pixels before changing the grid's bounds.
            modGrid.BeginInvoke((Action)(()=>{
                scrollUpdateQueued=false;
                if(modGrid.IsDisposed)return;
                if(tableTools.Visible!=showToolsAfterScroll){
                    layout.SuspendLayout();
                    tableTools.Visible=showToolsAfterScroll;
                    layout.RowStyles[4].SizeType=showToolsAfterScroll?SizeType.AutoSize:SizeType.Absolute;
                    if(!showToolsAfterScroll)layout.RowStyles[4].Height=0;
                    layout.ResumeLayout(true);
                }
                modGrid.Invalidate();
            }));
        };
        detail=new Label{Dock=DockStyle.Fill,AutoEllipsis=true,TextAlign=ContentAlignment.MiddleCenter,
            Font=GeneralStyle.Font(10.5f),ForeColor=GeneralStyle.Muted,Margin=new Padding(16,0,16,0)};
        detail.Text=startupIssue??T("Dê dois cliques para editar. Use Adicionar item no final de cada categoria ou arraste um ZIP, RAR ou 7z.","Double-click to edit. Use Add item at the end of a category or drop a ZIP, RAR or 7z.");
        var bars=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=false,Height=44,MinimumSize=new Size(0,44),ColumnCount=3,RowCount=1,Margin=new Padding(0,4,0,10)};
        bars.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bars.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        bars.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        next=Command(T("Pular mod","Skip mod"),RequestSkip);next.Visible=false;next.MinimumSize=new Size(112,44);next.Margin=new Padding(0,0,14,0);bars.Controls.Add(next,0,0);
        currentProgress=new GeneralProgress{Dock=DockStyle.Fill,Caption=T("Item atual","Current item"),Margin=new Padding(0,3,8,0)};
        totalProgress=new GeneralProgress{Dock=DockStyle.Fill,Caption=T("Progresso geral","Overall progress"),Margin=new Padding(8,3,0,0)};
        bars.Controls.Add(currentProgress,1,0);bars.Controls.Add(totalProgress,2,0);layout.Controls.Add(bars,0,6);
        var bottom=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=false,Height=56,MinimumSize=new Size(0,56),ColumnCount=3,RowCount=1,Margin=Padding.Empty};bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));bottom.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var remove=Command(T("Desinstalar selecionados","Uninstall selected"),async()=>await Uninstall(),true);((GeneralButton)remove).Destructive=true;((GeneralButton)remove).Primary=true;remove.Name="UninstallSelected";remove.MinimumSize=new Size(226,52);remove.Font=GeneralStyle.Font(13,true);remove.Margin=Padding.Empty;remove.Anchor=AnchorStyles.Left;bottom.Controls.Add(remove,0,0);
        var install=Command(T("Baixar/Instalar","Download/Install"),StartSelected);((GeneralButton)install).Primary=true;install.Name="DownloadInstall";install.MinimumSize=new Size(226,52);install.Font=GeneralStyle.Font(13,true);install.Margin=Padding.Empty;install.Anchor=AnchorStyles.Right;bottom.Controls.Add(install,2,0);
        bottom.Controls.Add(detail,1,0);
        layout.Controls.Add(bottom,0,7);
        preset.BackColor=GeneralStyle.Surface;preset.ForeColor=GeneralStyle.Foreground;
        EnableDrops(this);
        RebuildPresets();
        preset.SelectedIndexChanged+=(s,e)=>{if(drawing)return;if(!ConfirmPresetNavigation()){drawing=true;preset.SelectedIndex=state.List.Presets.FindIndex(p=>p.Id==state.SelectedPreset)+1;drawing=false;return;}state.SelectedPreset=preset.SelectedIndex>0?state.List.Presets[preset.SelectedIndex-1].Id:null;CapturePresetDraft();Guard(Persist);UpdateTableTitle();DrawRows();};
        idle.Add(preset);Busy(working||scanning);SetListExpanded(listExpanded);ResumeLayout(true);GeneralNativeTheme.ApplyTree(this);
    }
    void SetListExpanded(bool expanded){
        menus.Close();listExpanded=expanded;mainLayout.SuspendLayout();
        foreach(int row in new[]{0,1,2,3,6,7}){
            foreach(Control control in mainLayout.Controls)if(mainLayout.GetRow(control)==row)control.Visible=!expanded;
            mainLayout.RowStyles[row].SizeType=expanded?SizeType.Absolute:SizeType.AutoSize;
            mainLayout.RowStyles[row].Height=0;
        }
        mainLayout.Padding=expanded?new Padding(16):new Padding(32,24,32,22);
        ((GeneralButton)expandList).IconKind=expanded?"minimize":"maximize";
        string label=expanded?T("Restaurar visualização","Restore view"):T("Expandir lista","Expand list");expandList.AccessibleName=label;tips.SetToolTip(expandList,label);expandList.Invalidate();
        mainLayout.ResumeLayout(true);if(Visible)grid.Focus();
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
        target.AllowDrop=true;target.DragEnter+=(s,e)=>e.Effect=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DroppedText(e.Data)!=null?DragDropEffects.Link:DragDropEffects.None;
        target.DragDrop+=(s,e)=>Guard(()=>{if(e.Data.GetDataPresent(DataFormats.FileDrop))Drop((string[])e.Data.GetData(DataFormats.FileDrop));else ImportWebSources(DroppedText(e.Data));});
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
    void RebuildPresets(){
        drawing=true;preset.Items.Clear();preset.Items.Add(T("Todos os itens","All items"));preset.Items.AddRange(state.List.Presets.Select(p=>p.Name).Cast<object>().ToArray());
        int selected=state.List.Presets.FindIndex(p=>p.Id==state.SelectedPreset);preset.SelectedIndex=selected+1;drawing=false;UpdateTableTitle();DrawRows();
    }
    List<GeneralModEntry> VisibleMods(){
        var active=state.List.Presets.FirstOrDefault(p=>p.Id==state.SelectedPreset);
        var allowed=active==null?null:new HashSet<string>(active.ModIds,StringComparer.Ordinal);
        return state.List.Mods.Where(m=>(allowed==null||allowed.Contains(m.Id))&&ColumnAllows(m)).ToList();
    }
    void DrawRows(){
        if(grid==null)return;string old=grid.CurrentRow==null?null:grid.CurrentRow.Tag as string;
        int scroll=grid.FirstDisplayedScrollingRowIndex;drawing=true;hoveredRow=-1;grid.Rows.Clear();
        var visible=VisibleMods();var groups=new List<string>();
        groups.Add(null);groups.AddRange(PresetCategories());
        if(categoryFilters!=null)groups=groups.Where(category=>categoryFilters.Contains(category??"")).ToList();
        if(variantFilters!=null||statusFilters!=null||!String.IsNullOrWhiteSpace(nameFilter)||searchTerms.Length>0)groups=groups.Where(category=>visible.Any(mod=>String.Equals(mod.Category,category,StringComparison.OrdinalIgnoreCase)||String.IsNullOrEmpty(mod.Category)&&category==null)).ToList();
        foreach(var category in groups){
            if(grid.Rows.Count>0)AddSection("divider",category,23);
            string description;
            if(category==null)description=state.List.UncategorizedDescription;
            else if(!state.List.CategoryDescriptions.TryGetValue(category,out description))description=null;
            AddSection("heading",category,String.IsNullOrWhiteSpace(description)?54:78);
            foreach(var m in visible.Where(m=>String.Equals(m.Category,category,StringComparison.OrdinalIgnoreCase)||String.IsNullOrEmpty(m.Category)&&category==null)){
                var v=Selected(m);string key=Key(m,v),status;int pct;statuses.TryGetValue(key,out status);bool hasProgress=progress.TryGetValue(key,out pct);
                int i=grid.Rows.Add(marked.Contains(m.Id),m.Name,v.Name,GeneralStatusPresentation.Label(status),hasProgress?(object)pct:null,ModSpace(m.Id));var row=grid.Rows[i];row.Tag=m.Id;row.Cells["Status"].ToolTipText=StatusDetail(key,status);
                row.Cells["Size"].ToolTipText=SpaceTip(m.Id);row.Cells["Name"].ToolTipText=m.Description??"";
                if(m.Id==old)grid.CurrentCell=row.Cells[1];
            }
            if(searchTerms.Length==0)AddSection("add",category,44);
        }
        if(searchTerms.Length==0)AddSection("new",null,56);
        else if(visible.Count==0)AddSection("empty",null,56);
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
        counts.Text=T("Mods na lista: ","Mods in list: ")+rows.Count+"   ·   "+T("Medidos: ","Measured: ")+rows.Count(r=>measuredMods.ContainsKey((string)r.Tag))+"/"+rows.Count+"   ·   "+T("Instalados: ","Installed: ")+installed+"   ·   "+T("Falhas: ","Failed: ")+failed;UpdatePresetCommands();UpdatePackCoverage();
    }
    sealed class ModListGrid : DataGridView {
        public ModListGrid(){DoubleBuffered=true;}
    }
    void PaintSection(object sender,DataGridViewRowPrePaintEventArgs e){
        var section=grid.Rows[e.RowIndex].Tag as SectionRow;if(section==null)return;
        e.Handled=true;var g=e.Graphics;var bounds=e.RowBounds;bounds.Width=grid.ClientSize.Width-(grid.Controls.OfType<VScrollBar>().Any(s=>s.Visible)?SystemInformation.VerticalScrollBarWidth:0);
        var viewport=grid.ClientRectangle;
        viewport.Y+=grid.ColumnHeadersHeight;
        viewport.Height=Math.Max(0,viewport.Height-grid.ColumnHeadersHeight);
        var paintBounds=Rectangle.Intersect(Rectangle.Intersect(bounds,viewport),e.ClipBounds);
        if(paintBounds.Width<=0||paintBounds.Height<=0)return;
        var saved=g.Save();
        try{
            g.SetClip(paintBounds,System.Drawing.Drawing2D.CombineMode.Intersect);
            using(var b=new SolidBrush(e.RowIndex==hoveredRow&&section.Kind!="divider"?GeneralStyle.Selection:GeneralStyle.Surface))g.FillRectangle(b,bounds);
            if(section.Kind=="divider"){using(var p=new Pen(GeneralStyle.SoftLine))g.DrawLine(p,bounds.X+20,bounds.Y+bounds.Height/2,bounds.Right-20,bounds.Y+bounds.Height/2);return;}
            string text,description=null;int y=bounds.Y+8;
            if(section.Kind=="heading"){
                text=section.Category??UncatName();y=bounds.Y+12;
                if(section.Category==null)description=state.List.UncategorizedDescription;else state.List.CategoryDescriptions.TryGetValue(section.Category,out description);
            }else if(section.Kind=="empty")text=T("Nenhum mod encontrado. Tente outro termo ou limpe os filtros.","No mods found. Try another term or clear the filters.");
            else text=section.Kind=="add"?"+ "+T("Adicionar item","Add item"):"+ "+T("Criar nova categoria","Create new category");
            using(var font=GeneralStyle.Font(section.Kind=="heading"?15:12,section.Kind=="heading"))TextRenderer.DrawText(g,text,font,new Rectangle(bounds.X+18,y,bounds.Width-40,30),section.Kind=="heading"?GeneralStyle.Foreground:GeneralStyle.Accent,TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.PreserveGraphicsClipping);
            if(!String.IsNullOrWhiteSpace(description))using(var font=GeneralStyle.Font(10.5f))TextRenderer.DrawText(g,description.Replace("\r"," ").Replace("\n"," "),font,new Rectangle(bounds.X+18,y+31,bounds.Width-40,24),GeneralStyle.Muted,TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.PreserveGraphicsClipping);
        }finally{g.Restore(saved);}
    }
    void PaintName(object sender,DataGridViewCellPaintingEventArgs e){
        if(e.RowIndex==-1&&e.ColumnIndex>0){
            e.PaintBackground(e.ClipBounds,true);
            if(e.ColumnIndex<=3){
                bool active=e.ColumnIndex==1?categoryFilters!=null||!String.IsNullOrEmpty(nameFilter):e.ColumnIndex==2?variantFilters!=null:statusFilters!=null;
                TextRenderer.DrawText(e.Graphics,grid.Columns[e.ColumnIndex].HeaderText,e.CellStyle.Font,new Rectangle(e.CellBounds.X+10,e.CellBounds.Y,e.CellBounds.Width-36,e.CellBounds.Height),GeneralStyle.Muted,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.PreserveGraphicsClipping);
                LucideIcons.Draw(e.Graphics,"down",new Rectangle(e.CellBounds.Right-25,e.CellBounds.Y+15,14,14),active?GeneralStyle.Accent:GeneralStyle.Muted);e.Handled=true;return;
            }
        }
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
        TextRenderer.DrawText(e.Graphics,mod.Name,e.CellStyle.Font,new Rectangle(box.X,box.Y,Math.Max(1,nameWidth),box.Height),e.CellStyle.ForeColor,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine|TextFormatFlags.PreserveGraphicsClipping);
        using(var font=GeneralStyle.Font(9.5f,true))TextRenderer.DrawText(e.Graphics,"NSFW",font,new Rectangle(box.X+nameWidth+5,box.Y,suffix-5,box.Height),GeneralStyle.Danger,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.PreserveGraphicsClipping);
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
            TextRenderer.DrawText(e.Graphics,label,font,new Rectangle(rect.X+11,rect.Y,rect.Width-22,rect.Height),Color.White,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.PreserveGraphicsClipping|TextFormatFlags.EndEllipsis);
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
            using(var dialog=new GeneralModEditor(item,PresetCategories(),selected.Id,!working||current!=null,info,LookupSourceInfo)){
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
                if(replacement==null||!replacement.Sources.SequenceEqual(old.Sources,StringComparer.Ordinal)){statuses.Remove(Key(before,old));statusDetails.Remove(Key(before,old));progress.Remove(Key(before,old));state.SnapshotReceipts.Remove(Key(before,old));}
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
        Persist();DrawRows();
    }
    async void Drop(string[] files){
        if(files==null||scanning)return;
        try{
            var paths=files.Where(f=>GeneralModDownload.IsArchivePath(f)&&File.Exists(f)).Select(GeneralModLibrary.ValidateSource).ToList();
            if(paths.Count==0)throw new IOException(T("Arraste um arquivo ZIP, RAR ou 7z concluído.","Drop a completed ZIP, RAR or 7z file."));
            if(waiting&&priorityCurrent==null&&current!=null&&paths.Count==1){
                var target=current;string source=paths[0];
                var added=NewEntry(Path.GetFileNameWithoutExtension(source),source);added.Category=DefaultCategory;
                editingAttachment=true;
                try{using(var editor=new GeneralModEditor(added,PresetCategories(),added.DefaultVariant,true,T("Adicione este mod ou associe o arquivo ao download atual.","Add this mod or attach its file to the current download."))){
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
                if(known==null)entry.Category=DefaultCategory;
                await EditMod(entry);
            }
        }catch(Exception ex){ShowError(ex);}
    }
    void RemoveEntry(){
        var item=FocusedEntry();if(item==null||working||scanning)return;
        if(GeneralDialog.Message(this,T("Tirar da lista sem desinstalar?","Remove from the list without uninstalling?")+"\n"+item.Name,T("Tirar da lista","Remove from list"),MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
        state.List.Mods.Remove(item);state.ChosenVariants.Remove(item.Id);foreach(var p in state.List.Presets)p.ModIds.Remove(item.Id);marked.Remove(item.Id);Persist();DrawRows();
    }
    void Busy(bool value){foreach(var control in idle)control.Enabled=!value;if(next!=null){next.Visible=working||scanning;next.Enabled=working||scanning;}UpdatePresetCommands();}
    internal void Preview(string path){
        ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;Location=new Point(-30000,-30000);Show();Application.DoEvents();PerformLayout();
        using(var bmp=new Bitmap(Width,Height)){DrawToBitmap(bmp,new Rectangle(0,0,Width,Height));bmp.Save(path,System.Drawing.Imaging.ImageFormat.Png);}Close();
    }
}

