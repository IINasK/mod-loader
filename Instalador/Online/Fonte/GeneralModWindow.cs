using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow : Form {
    readonly FixedCatalog catalog;
    DownloadCache cache;
    GeneralModLibrary library;
    readonly GeneralModList seed;
    string data;
    readonly bool testMode;
    GeneralModState state;
    readonly HashSet<string> marked=new HashSet<string>();
    readonly HashSet<string> expandedVersions=new HashSet<string>(StringComparer.Ordinal);
    readonly HashSet<string> collapsedCategories=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    const int CategoryIndent=26,CategoryToggleWidth=52;
    readonly Dictionary<string,string> statuses=new Dictionary<string,string>();
    readonly Dictionary<string,string> statusDetails=new Dictionary<string,string>();
    readonly Dictionary<string,int> progress=new Dictionary<string,int>();
    readonly List<Control> idle=new List<Control>();
    GeneralPicker preset;
    DataGridView grid;
    Label detail,counts;
    Button next,optionsButton,expandList,projectsButton,homeButton,removeButton;
    GeneralStorageLabel storageSize;
    TableLayoutPanel mainLayout,tableTools;
    bool listExpanded;
    int hoveredRow=-1;
    GeneralProgress currentProgress,totalProgress;
    bool drawing,working,scanning;
    string startupIssue;
    readonly ToolTip tips=new ToolTip{UseAnimation=false,UseFading=false};
    readonly GeneralMenuHost menus=new GeneralMenuHost();
    static string T(string pt,string en){return GeneralStyle.T(pt,en);}

    [Flags]
    enum CategoryBranch { Middle=0, First=1, Last=2, Continuation=4 }

    sealed class SectionRow {
        internal string Kind,Category,Size;
        internal string[] ModIds;
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
            state=new GeneralModState{List=GeneralModLibrary.Seed(catalog,pending),DownloadFolder=OnlineDownloadWatch.DefaultFolder(),Destination=GeneralAppPaths.IsDevelopment?null:Discovery.Find().FirstOrDefault()};
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
        GeneralPresetProfiles.Initialize(state);GeneralPresetProfiles.Project(state);
        Text=GeneralAppPaths.WindowTitle;Icon=ModLoaderBrand.AppIcon;ClientSize=new Size(1240,820);MinimumSize=new Size(1000,680);StartPosition=FormStartPosition.CenterScreen;AutoScaleMode=AutoScaleMode.Dpi;
        BuildInterface();
        FormClosing+=CloseModWindow;
        FormClosed+=(s,e)=>ReleaseProjectSharing();
        Disposed+=(s,e)=>menus.Dispose();
        Shown+=(s,e)=>RefreshStorageSize();
        if(fixture==null)Shown+=async(s,e)=>{
#if !VELOPACK
            CheckUpdatesOnStartup();
#endif
            await RecoverPresetOnOpen();ReadPresetPresentation();await Scan();if(!IsDisposed)RenderPresetResult();if(!IsDisposed&&!exitApproved)await ResumeP2P();
        };
    }
    bool exitApproved,waitingToExit;

    bool FinishingOperations {get{
        return working||scanning||loadingPreset||associationChoice!=null||
            (sharingHome!=null?sharingHome.InstallingTransfer:p2p!=null&&p2p.Installing);
    }}

    void CloseModWindow(object sender,FormClosingEventArgs e){
        if(ProjectAction!=null){
            e.Cancel=working||scanning||!ConfirmProjectNavigation();
            if(e.Cancel)ProjectAction=null;
            return;
        }
        if(!exitApproved){
            bool transferAccepted=sharingHome!=null?sharingHome.ConfirmTransferExit(this,working||scanning):
                !(working||scanning||p2p!=null&&p2p.HasActiveTransfers)||GeneralProjectHome.AskTransferExit(this);
            if(!transferAccepted){e.Cancel=true;return;}
            if(associationChoice!=null&&GeneralDialog.Message(this,T(
                "Descartar as alterações do editor aberto e fechar o Mod Loader?",
                "Discard the changes in the open editor and close Mod Loader?"),
                T("Alterações não salvas","Unsaved changes"),MessageBoxButtons.YesNo,MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2)!=DialogResult.Yes){e.Cancel=true;return;}
            if(!ConfirmProjectNavigation()){e.Cancel=true;return;}
            exitApproved=true;
            if(loadingPreset)CancelPresetLoad();
            if(associationChoice!=null)associationChoice.TrySetResult(null);
            if(working){
                if(current!=null)skipRequests.Add(current);
                RequestSkip();
                normal.Clear();
                priority.Clear();
            }
        }
        if(!FinishingOperations)return;
        e.Cancel=true;
        if(waitingToExit)return;
        waitingToExit=true;
        detail.Text=T("Concluindo a operação para fechar…","Finishing the operation before closing…");
        CloseAfterOperations();
    }

    async void CloseAfterOperations(){
        while(!IsDisposed&&FinishingOperations)await Task.Delay(200);
        if(!IsDisposed)Close();
    }

    void BuildInterface(){
        GeneralStyle.UseTheme(state.Theme);GeneralStyle.Language=state.Language??"pt";
        SuspendLayout();
        primaryAction=null;
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
        presetSettings=IconButton("save",T("Salvar alterações","Save changes"),SaveCurrentPreset,true);((GeneralButton)presetSettings).Bare=true;presetSettings.Name="SavePreset";presetSettings.Margin=Padding.Empty;
        deletePreset=IconButton("trash",T("Excluir preset","Delete preset"),DeletePreset,true);((GeneralButton)deletePreset).Bare=true;((GeneralButton)deletePreset).Destructive=true;deletePreset.Name="DeletePreset";deletePreset.Margin=Padding.Empty;

        clearFilters=IconButton("undo",T("Restaurar filtros e ordem padrão","Reset filters and sort order"),ClearListFilters,true);
        ((GeneralButton)clearFilters).Bare=true;clearFilters.Name="ClearFilters";clearFilters.Margin=Padding.Empty;

        var summary=Flow();summary.Margin=new Padding(0,0,0,12);layout.Controls.Add(summary,0,3);
        counts=new Label{AutoSize=true,Font=GeneralStyle.Font(10.5f),ForeColor=GeneralStyle.Muted,Margin=Padding.Empty};summary.Controls.Add(counts);
        storageSize=new GeneralStorageLabel(cache){Name="ProjectStorage",AutoSize=true,Font=GeneralStyle.Font(10.5f),ForeColor=GeneralStyle.Muted,Margin=new Padding(16,0,0,0)};summary.Controls.Add(storageSize);
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
        tableHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));for(int i=1;i<tableHeading.ColumnCount;i++)tableHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,44));
        tableName=new TextBox{Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,Font=GeneralStyle.Font(16,true),MaxLength=120,Margin=new Padding(0,8,12,0),BackColor=GeneralStyle.Surface,ForeColor=GeneralStyle.Foreground};
        tableName.TextChanged+=(s,e)=>DraftTitleChanged();tableName.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;Guard(SaveCurrentPreset);}};
        tableHeading.Controls.Add(tableName,0,0);
        tableHeading.Controls.Add(preset,1,0);tableHeading.Controls.Add(presetSettings,2,0);tableHeading.Controls.Add(deletePreset,3,0);tableHeading.Controls.Add(clearFilters,4,0);
        expandList=IconButton("maximize",T("Expandir lista","Expand list"),()=>SetListExpanded(!listExpanded));((GeneralButton)expandList).Bare=true;expandList.Name="ExpandList";expandList.Margin=Padding.Empty;tableHeading.Controls.Add(expandList,5,0);
        var tableTitle=new Panel{Dock=DockStyle.Fill,Height=64,Margin=Padding.Empty,Padding=new Padding(0,0,0,12),BackColor=GeneralStyle.Surface,Name="PresetHeading"};
        tableTitle.Controls.Add(tableHeading);preset.PopupAnchor=tableName;
        tableTitle.Paint+=(s,e)=>{using(var pen=new Pen(GeneralStyle.Line))e.Graphics.DrawLine(pen,16,tableTitle.Height-6,tableTitle.Width-17,tableTitle.Height-6);};
        tableTools=new TableLayoutPanel{Name="TableTools",Dock=DockStyle.Fill,AutoSize=true,ColumnCount=1,RowCount=2,Margin=Padding.Empty,BackColor=GeneralStyle.Surface};
        tableTools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));tableTools.RowStyles.Add(new RowStyle(SizeType.Absolute,64));tableTools.RowStyles.Add(new RowStyle(SizeType.Absolute,62));
        tableTools.Controls.Add(tableTitle,0,0);tableTools.Controls.Add(BuildSearch(),0,1);layout.Controls.Add(tableTools,0,4);
        grid=new ModListGrid{Dock=DockStyle.Fill,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,AllowUserToResizeColumns=true,
            RowHeadersVisible=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,BackgroundColor=GeneralStyle.Surface,BorderStyle=BorderStyle.None,
            CellBorderStyle=DataGridViewCellBorderStyle.None,ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None,AutoGenerateColumns=false,EnableHeadersVisualStyles=false,
            ScrollBars=ScrollBars.Vertical,Margin=Padding.Empty,ColumnHeadersHeight=44,ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing};
        grid.DefaultCellStyle=new DataGridViewCellStyle{BackColor=GeneralStyle.Surface,ForeColor=GeneralStyle.Foreground,SelectionBackColor=GeneralStyle.Selection,SelectionForeColor=GeneralStyle.Foreground,Font=GeneralStyle.Font(12),Padding=new Padding(10,0,8,0)};
        optionSelectedFont=GeneralStyle.Font(12,true);
        groupIndicatorFont=GeneralStyle.Font(9.5f,true);
        var selectedFont=optionSelectedFont;var groupFont=groupIndicatorFont;
        grid.Disposed+=(s,e)=>{selectedFont.Dispose();groupFont.Dispose();};
        grid.ColumnHeadersDefaultCellStyle=new DataGridViewCellStyle{BackColor=GeneralStyle.Surface,ForeColor=GeneralStyle.Muted,Font=GeneralStyle.Font(10.5f,true),Padding=new Padding(10,0,8,0),SelectionBackColor=GeneralStyle.Surface};
        grid.Columns.Add(new DataGridViewCheckBoxColumn{Name="Mark",HeaderText="",Width=44,SortMode=DataGridViewColumnSortMode.NotSortable});
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Name",HeaderText="Mods",MinimumWidth=160,FillWeight=85,AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill,ReadOnly=true,SortMode=DataGridViewColumnSortMode.NotSortable});
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Variant",HeaderText=T("Variante","Variant"),Width=160,ReadOnly=true,SortMode=DataGridViewColumnSortMode.NotSortable});
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Status",HeaderText=T("Situação","Status"),Width=205,ReadOnly=true,SortMode=DataGridViewColumnSortMode.NotSortable});
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Progress",Visible=false,ReadOnly=true});
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Size",HeaderText=T("Espaço","Space"),MinimumWidth=108,FillWeight=15,AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill,ReadOnly=true,SortMode=DataGridViewColumnSortMode.NotSortable,DefaultCellStyle=new DataGridViewCellStyle{Alignment=DataGridViewContentAlignment.MiddleRight,ForeColor=GeneralStyle.Muted}});
        grid.Columns["Size"].DisplayIndex=2;
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Group",HeaderText=T("Grupo","Group"),Width=92,ReadOnly=true,SortMode=DataGridViewColumnSortMode.NotSortable});
        grid.Columns["Group"].DisplayIndex=grid.Columns["Status"].DisplayIndex+1;
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Origin",HeaderText=T("Origem","Origin"),Width=56,ReadOnly=true,SortMode=DataGridViewColumnSortMode.NotSortable,
            HeaderCell=new DataGridViewColumnHeaderCell{Style=new DataGridViewCellStyle{Alignment=DataGridViewContentAlignment.MiddleCenter,Padding=Padding.Empty}}});
        grid.Columns["Origin"].HeaderText=T("Origem","Origin");
        grid.Columns["Origin"].HeaderCell.ToolTipText=T("Origem do item na lista","Item origin in the list");

        grid.Columns["Size"].HeaderCell.ToolTipText=T("Espaço ocupado pelos arquivos instalados do mod; — significa não identificado.","Space used by installed mod files; — means not identified.");
        grid.Columns.Add(new DataGridViewTextBoxColumn{
            Name="CategoryIndent",Width=CategoryIndent,MinimumWidth=CategoryIndent,Resizable=DataGridViewTriState.False,
            ReadOnly=true,SortMode=DataGridViewColumnSortMode.NotSortable
        });
        grid.Columns["CategoryIndent"].DisplayIndex=0;
        grid.Columns["Origin"].DisplayIndex=grid.Columns.Count-1;
        grid.RowTemplate.Height=44;
        grid.CurrentCellDirtyStateChanged+=(s,e)=>{if(grid.IsCurrentCellDirty)grid.CommitEdit(DataGridViewDataErrorContexts.Commit);};
        grid.CellValueChanged+=(s,e)=>{
            if(drawing||associationChoice!=null||e.RowIndex<0||e.ColumnIndex!=0)return;
            var row=grid.Rows[e.RowIndex];string id=row.Tag as string;if(id==null)return;
            if(Equals(row.Cells[0].Value,true))marked.Add(id);else marked.Remove(id);UpdateCounts();
        };
        grid.ColumnHeaderMouseClick+=(s,e)=>{if(associationChoice!=null)return;if(e.ColumnIndex>0){
            ShowColumnFilter(e.ColumnIndex);return;
        }var rows=ModRows().Where(r=>r.Visible).ToList();bool check=rows.Any(r=>!marked.Contains((string)r.Tag));foreach(var r in rows)r.Cells[0].Value=check;};

        bool markBeforeMouseDown=false;
        grid.CellMouseClick+=(s,e)=>{
            if(e.Button!=MouseButtons.Left||e.Clicks!=1||e.RowIndex<0||e.ColumnIndex<0||associationChoice!=null)return;
            var row=grid.Rows[e.RowIndex];
            var section=row.Tag as SectionRow;
            if(section!=null){
                int x=grid.GetCellDisplayRectangle(e.ColumnIndex,e.RowIndex,false).X+e.X;
                if(section.Kind=="heading"&&x>=CategoryContentRight-CategoryToggleWidth)ToggleCategory(row,section);
                else HandleSection(section);
                return;
            }
            if(e.ColumnIndex==grid.Columns["Variant"].Index&&e.X>=grid.Columns[e.ColumnIndex].Width-30&&ToggleVersions(row))return;
            if(e.ColumnIndex==grid.Columns["Name"].Index&&row.Cells["Group"].Tag is string&&e.Y>=row.Height/2){
                ToggleVersions(row);return;
            }
            if(row.Tag is Work){SelectVersion((Work)row.Tag);return;}
            if(e.ColumnIndex!=0||!(row.Tag is string))return;
            var mark=row.Cells["Mark"];
            bool desiredMark=!markBeforeMouseDown;
            var clickedGrid=(DataGridView)s;
            // Let the native checkbox click finish before expanding its hit area within the checkbox column.
            clickedGrid.BeginInvoke((Action)(()=>{
                if(mark.DataGridView==clickedGrid&&associationChoice==null&&!Equals(mark.Value,desiredMark))
                    mark.Value=desiredMark;
            }));
        };
        grid.CellMouseDoubleClick+=(s,e)=>{
            if(associationChoice!=null||e.Button!=MouseButtons.Left||e.RowIndex<0||e.ColumnIndex<=0)return;
            var row=grid.Rows[e.RowIndex];
            OpenRowEditor(row);
        };
        grid.KeyDown+=(s,e)=>{
            if(e.KeyCode!=Keys.Enter&&e.KeyCode!=Keys.F2)return;
            e.Handled=true;e.SuppressKeyPress=true;
            if(associationChoice!=null||grid.CurrentRow==null)return;
            var row=grid.CurrentRow;
            var section=row.Tag as SectionRow;
            if(section!=null&&section.Kind=="heading"&&e.KeyCode==Keys.Enter)ToggleCategory(row,section);
            else if(section!=null)HandleSection(section);
            else if(e.KeyCode==Keys.F2&&row.Tag is Work&&!working&&!scanning)EditBodyOption((Work)row.Tag);
            else if(e.KeyCode!=Keys.Enter||grid.CurrentCell.OwningColumn.Name!="Variant"||!ToggleVersions(row))OpenRowEditor(row);
        };
        grid.RowPrePaint+=PaintSection;grid.CellPainting+=PaintName;
        grid.SelectionChanged+=(s,e)=>RefreshPrimaryAction();
        grid.CurrentCellChanged+=(s,e)=>RefreshPrimaryAction();
        grid.CellMouseEnter+=(s,e)=>SetHover(e.RowIndex);
        grid.MouseLeave+=(s,e)=>SetHover(-1);
        grid.DataError+=(s,e)=>{e.ThrowException=false;};
        var menu=GeneralMenuRenderer.Create();
        GeneralMenuRenderer.Add(menu,T("Detalhes e variantes","Details and variants"),()=>OpenRowEditor(grid.CurrentRow));
        GeneralMenuRenderer.Add(menu,T("Abrir fonte no navegador","Open source in browser"),()=>Guard(()=>{var item=FocusedEntry();if(item!=null){var url=Selected(item).Sources.FirstOrDefault(GeneralModDownload.IsWebSource);if(url!=null)GeneralModDownload.OpenSource(url);}}));
        var removeEntry=GeneralMenuRenderer.Add(menu,T("Tirar da lista","Remove from list"),()=>Guard(RemoveEntry));
        menu.Opening+=(s,e)=>{var focused=FocusedEntry();removeEntry.Visible=focused!=null&&!IsBigBody(focused);};
        var linkInventory=GeneralMenuRenderer.Add(menu,T("Vincular link do Nexus…","Link Nexus URL…"),()=>
            SetInventorySource(grid.CurrentRow==null?null:grid.CurrentRow.Tag as P2PSyncDifference));
        var splitInventory=GeneralMenuRenderer.Add(menu,T("Desfazer agrupamento ou vínculo","Undo grouping or link"),()=>Guard(()=>{
            var group=SelectedInventoryLink();
            if(group!=null&&CanEditInventory)UpdateInventoryLinks(links=>links.Groups.RemoveAll(item=>item.Id==group.Id));
        }));
        menu.Opening+=(s,e)=>{
            linkInventory.Visible=CanEditInventory&&grid.CurrentRow!=null&&grid.CurrentRow.Tag is P2PSyncDifference;
            splitInventory.Visible=CanEditInventory&&SelectedInventoryLink()!=null;
        };
        menu.Items.Add(new ToolStripSeparator());
        var failureDetails=GeneralMenuRenderer.Add(menu,T("Ver erro da instalação","View installation error"),ShowFocusedFailure);
        failureDetails.Name="ViewInstallFailure";
        var copyFailures=GeneralMenuRenderer.Add(menu,T("Copiar erros da instalação","Copy installation errors"),CopyFailures);copyFailures.Name="CopyInstallFailures";
        var undo=GeneralMenuRenderer.Add(menu,T("Cyberpunk: desfazer operação","Cyberpunk: undo operation"),async()=>await CyberpunkMaintenance(false));
        var clean=GeneralMenuRenderer.Add(menu,T("Cyberpunk: limpar extras","Cyberpunk: remove extra mods"),async()=>await CyberpunkMaintenance(true));
        menu.Opening+=(s,e)=>{if(associationChoice!=null){e.Cancel=true;return;}bool available=!working&&!scanning&&IsCyberpunk(state.Destination);undo.Visible=available;clean.Visible=available;var focused=FocusedEntry();string status;failureDetails.Visible=focused!=null&&statuses.TryGetValue(Key(focused,Selected(focused)),out status)&&GeneralStatusPresentation.Kind(status)=="error";copyFailures.Visible=statuses.Values.Any(value=>GeneralStatusPresentation.Kind(value)=="error");e.Cancel=focused==null&&(grid.CurrentRow==null||!(grid.CurrentRow.Tag is P2PSyncDifference));};
        grid.CellMouseDown+=(s,e)=>{
            if(e.RowIndex<0)return;
            var row=grid.Rows[e.RowIndex];
            if(!(row.Tag is string)&&!(row.Tag is P2PSyncDifference))return;
            if(e.Button==MouseButtons.Left&&e.ColumnIndex==0&&row.Tag is string)markBeforeMouseDown=Equals(row.Cells["Mark"].Value,true);
            if(e.Button==MouseButtons.Right)grid.CurrentCell=row.Cells["Name"];
        };
        EnableInventoryDrag(grid);
        grid.ContextMenuStrip=menu;grid.Disposed+=(s,e)=>menu.Dispose();
        layout.Controls.Add(new GeneralScrollView(grid),0,5);
        grid.ScrollBars=ScrollBars.Horizontal;
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
                if(associationChoice==null)
                    SetTableToolsVisible(showToolsAfterScroll||modGrid.FirstDisplayedScrollingRowIndex<=0);
                if(hoveredRow>=0)SetHover(-1);
            }));
        };
        detail=new Label{Dock=DockStyle.Fill,AutoEllipsis=true,TextAlign=ContentAlignment.MiddleCenter,
            Font=GeneralStyle.Font(10.5f),ForeColor=GeneralStyle.Muted,Margin=new Padding(16,0,16,0)};
        detail.Click+=(s,e)=>ShowPresetIssues();
        detail.Text=startupIssue??T("Marque na primeira coluna ou dê dois cliques na linha para editar. Use Adicionar item ou arraste um ZIP, RAR ou 7z.","Use the first column to check a mod or double-click its row to edit. Use Add item or drop a ZIP, RAR or 7z.");
        var bars=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=false,Height=44,MinimumSize=new Size(0,44),ColumnCount=3,RowCount=1,Margin=new Padding(0,4,0,10)};
        bars.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bars.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        bars.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        next=Command(T("Pular mod","Skip mod"),RequestSkip);next.Visible=false;next.MinimumSize=new Size(112,44);next.Margin=new Padding(0,0,14,0);bars.Controls.Add(next,0,0);
        currentProgress=new GeneralProgress{Dock=DockStyle.Fill,Caption=T("Item atual","Current item"),Margin=new Padding(0,3,8,0)};
        totalProgress=new GeneralProgress{Dock=DockStyle.Fill,Caption=T("Progresso geral","Overall progress"),Margin=new Padding(8,3,0,0)};
        totalProgress.SegmentActivated+=FilterCoverage;
        bars.Controls.Add(currentProgress,1,0);bars.Controls.Add(totalProgress,2,0);layout.Controls.Add(bars,0,6);
        var bottom=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=false,Height=56,MinimumSize=new Size(0,56),ColumnCount=4,RowCount=1,Margin=Padding.Empty};bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));bottom.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var remove=removeButton=Command(T("Desinstalar","Uninstall"),async()=>await Uninstall(),true);
        ((GeneralButton)remove).Destructive=true;((GeneralButton)remove).Primary=true;
        remove.Name="UninstallSelected";remove.MinimumSize=new Size(226,52);remove.Font=GeneralStyle.Font(13,true);
        remove.Margin=Padding.Empty;remove.Anchor=AnchorStyles.Left;remove.Visible=false;bottom.Controls.Add(remove,0,0);
        var install=primaryAction=Command(T("Baixar","Download"),StartSelected,true);((GeneralButton)install).Primary=true;install.Name="DownloadInstall";install.MinimumSize=new Size(226,52);install.Font=GeneralStyle.Font(13,true);install.Margin=Padding.Empty;install.Anchor=AnchorStyles.Right;bottom.Controls.Add(install,3,0);
        var verify=(GeneralButton)IconButton("refresh",T("Verificar projeto","Verify project"),async()=>await VerifyProject(),true);
        verify.Bare=true;verify.Name="VerifyProject";verify.MinimumSize=new Size(44,52);verify.Margin=new Padding(0,0,10,0);
        verify.Anchor=AnchorStyles.Right;bottom.Controls.Add(verify,2,0);
        bottom.Controls.Add(detail,1,0);
        layout.Controls.Add(bottom,0,7);
        preset.BackColor=GeneralStyle.Surface;preset.ForeColor=GeneralStyle.Foreground;
        EnableDrops(this);
        RebuildPresets();
        preset.SelectedIndexChanged+=(s,e)=>{
            if(drawing)return;
            string requested=preset.SelectedIndex>0?state.List.Presets[preset.SelectedIndex-1].Id:null;
            if(ConfirmPresetNavigation()){
                if(state.SelectedPreset!=requested)coverageFilter=null;
                GeneralPresetProfiles.Switch(state,requested);pendingVersion=null;CapturePresetDraft();Guard(Persist);
            }
            RebuildPresets();
        };
        idle.Add(preset);Busy(working||scanning);SetListExpanded(listExpanded);ResumeLayout(true);GeneralNativeTheme.ApplyTree(this);
    }
    void SetTableToolsVisible(bool visible){
        if(tableTools.Visible==visible)return;
        mainLayout.SuspendLayout();
        try{
            tableTools.Visible=visible;
            mainLayout.RowStyles[4].SizeType=visible?SizeType.AutoSize:SizeType.Absolute;
            if(!visible)mainLayout.RowStyles[4].Height=0;
        }finally{mainLayout.ResumeLayout(true);}
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
        target.AllowDrop=true;
        DragEventHandler showEffect=(s,e)=>e.Effect=e.Data.GetDataPresent(InventoryDragFormat)?InventoryDropEffect(target,e):
            associationChoice!=null?DragDropEffects.None:e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:
            DroppedText(e.Data)!=null?DragDropEffects.Link:DragDropEffects.None;
        target.DragEnter+=showEffect;
        if(target==grid)target.DragOver+=showEffect;
        target.DragDrop+=(s,e)=>Guard(()=>{
            if(associationChoice!=null)return;
            if(e.Data.GetDataPresent(InventoryDragFormat)){
                if(InventoryDropEffect(target,e)==DragDropEffects.Move)
                    MergeInventoryRows(inventoryDragKey,InventoryLinkStore.Key(InventoryRowAt(e.X,e.Y)));
                return;
            }
            if(e.Data.GetDataPresent(DataFormats.FileDrop)){Drop((string[])e.Data.GetData(DataFormats.FileDrop));return;}
            string text=DroppedText(e.Data);
            var row=target==grid&&CanEditInventory?InventoryRowAt(e.X,e.Y):null;
            if(row!=null&&text!=null&&GeneralNexusLink.Parse(text.Trim())!=null){DropInventorySource(row,text.Trim());return;}
            ImportWebSources(text);
        });
        foreach(Control child in target.Controls)EnableDrops(child);
    }
    void Guard(Action action){try{action();}catch(Exception ex){ShowError(ex);}}
    void ShowError(Exception ex){detail.Text=ex.Message;GeneralDialog.Message(this,ex.Message,T("Não foi possível concluir","Unable to complete"),MessageBoxButtons.OK,MessageBoxIcon.Warning);}
    GeneralModEntry FocusedEntry(){string id=grid.CurrentRow==null?null:grid.CurrentRow.Tag as string;return id==null?null:state.List.Mods.FirstOrDefault(x=>x.Id==id);}
    IEnumerable<DataGridViewRow> ModRows(){return grid.Rows.Cast<DataGridViewRow>().Where(r=>r.Tag is string);}
    GeneralModVariant Selected(GeneralModEntry entry){
        string id;
        if(!state.ChosenVariants.TryGetValue(entry.Id,out id)&&GeneralModLibrary.IsHystBody(entry.Id)){
            var installed=gameInventory.FirstOrDefault(file=>file.Kind=="verified-variant"&&file.ModId==entry.Id);
            if(installed!=null)id=installed.VariantId;
        }
        NexusManualMatch detected;
        if(id==null&&nexusManual.TryGetValue(entry.Id,out detected)&&entry.Variants.Contains(detected.Variant))
            return detected.Variant;
        return entry.Variants.FirstOrDefault(variant=>variant.Id==id)??entry.Variants.First(variant=>variant.Id==entry.DefaultVariant);
    }
    static string Key(GeneralModEntry entry,GeneralModVariant variant){return GeneralModLibrary.InstallationKey(entry,variant);}
    void Persist(){GeneralPresetProfiles.Capture(state);library.Save(CommittedProject());startupIssue=null;}
    string UncatName(){return state.List.UncategorizedName=="Sem categoria"?T("Sem categoria","Uncategorized"):state.List.UncategorizedName;}
    void RebuildPresets(){
        drawing=true;preset.Items.Clear();preset.Items.Add(T("Todos os itens","All items"));preset.Items.AddRange(state.List.Presets.Select(p=>p.Name).Cast<object>().ToArray());
        int selected=state.List.Presets.FindIndex(p=>p.Id==state.SelectedPreset);preset.SelectedIndex=selected+1;drawing=false;UpdateTableTitle();DrawRows();
    }
    List<GeneralModEntry> VisibleMods(){
        if(associationChoice!=null)
            return state.List.Mods.Where(mod=>mod.Id!=associationSourceId&&GeneralModSearch.Matches(searchTerms,mod.Name,Selected(mod).Name,"")).ToList();
        var active=ActivePreset;
        var allowed=active==null?null:new HashSet<string>(active.ModIds,StringComparer.Ordinal);
        if(coverageFilter!=null){
            var segment=ProjectCoverage().FirstOrDefault(item=>String.Equals(item.Category,coverageFilter.Category,StringComparison.OrdinalIgnoreCase));
            allowed=new HashSet<string>(segment==null?new string[0]:segment.ModIds,StringComparer.Ordinal);
        }
        var candidates=state.List.Mods.Where(m=>allowed==null||allowed.Contains(m.Id)).ToList();
        var body=BigBodyRepresentative(candidates);
        return candidates.Where(m=>(!IsBigBody(m)||m==body)&&ColumnAllows(m)).ToList();
    }
    void DrawRows(){
        if(grid==null)return;string old=grid.CurrentRow==null?null:grid.CurrentRow.Tag as string;
        var oldVersion=grid.CurrentRow==null?null:grid.CurrentRow.Tag as Work;
        int scroll=grid.FirstDisplayedScrollingRowIndex;drawing=true;hoveredRow=-1;grid.Rows.Clear();
        var visible=VisibleMods();var groups=new List<string>();
        bool picking=associationChoice!=null;
        var bodyRow=picking?null:visible.FirstOrDefault(IsBigBody);
        if(bodyRow!=null&&state.List.Mods.Any(mod=>IsBigBody(mod)&&marked.Contains(mod.Id))){
            marked.RemoveWhere(id=>id!=bodyRow.Id&&state.List.Mods.Any(mod=>mod.Id==id&&IsBigBody(mod)));
            marked.Add(bodyRow.Id);
        }

        bool flat=!picking&&orderColumn!=null;
        if(flat){
            foreach(var mod in OrderedMods(visible))AddModRow(mod,old,false,false,null);
        }else{
            groups.Add(null);groups.AddRange(picking?state.List.Categories:PresetCategories());
            if(!picking&&categoryFilters!=null)groups=groups.Where(category=>categoryFilters.Contains(category??"")).ToList();
            if(picking||coverageFilter!=null||variantFilters!=null||statusFilters!=null||groupFilters!=null||!String.IsNullOrWhiteSpace(nameFilter)||searchTerms.Length>0)groups=groups.Where(category=>visible.Any(mod=>String.Equals(mod.Category,category,StringComparison.OrdinalIgnoreCase)||String.IsNullOrEmpty(mod.Category)&&category==null)).ToList();
            foreach(var category in groups){
                if(grid.Rows.Count>0)AddSection("divider",category,23);
                string description;
                if(category==null)description=state.List.UncategorizedDescription;
                else if(!state.List.CategoryDescriptions.TryGetValue(category,out description))description=null;
                var members=visible.Where(mod=>String.Equals(mod.Category??"",category??"",StringComparison.OrdinalIgnoreCase)).ToList();
                var heading=AddSection("heading",category,String.IsNullOrWhiteSpace(description)?54:78);
                var section=(SectionRow)heading.Tag;
                section.ModIds=members.Select(mod=>mod.Id).ToArray();
                section.Size=CategorySize(section.ModIds);
                bool collapsed=CategoryCollapsed(section);
                foreach(var mod in members){
                    var branch=mod==members[0]?CategoryBranch.First:CategoryBranch.Middle;
                    if(mod==members[members.Count-1])branch|=CategoryBranch.Last;
                    AddModRow(mod,old,picking,collapsed,branch);
                }
                if(!picking&&groupFilters==null&&coverageFilter==null&&searchTerms.Length==0)AddSection("add",category,44).Visible=!collapsed;
            }
        }
        var inventory=VisibleGameInventory().GroupBy(InventoryLinkStore.Key,StringComparer.OrdinalIgnoreCase).ToList();
        if(inventory.Count>0){
            if(!flat){
                if(grid.Rows.Count>0)AddSection("divider",null,23);
                AddSection("inventory",T("Fora do catálogo do projeto","Outside the project catalog"),54);
            }
            var inventoryMembers=DisplayedInventory().Where(file=>file.Kind=="found").ToLookup(InventoryLinkStore.Key,InventoryPaths);
            var savedGroups=InventoryLinks.Groups.ToDictionary(group=>group.Id,StringComparer.Ordinal);
            foreach(var entry in inventory){
                int index=grid.Rows.Add();
                var row=grid.Rows[index];
                row.Tag=entry.First();
                row.Cells["Mark"]=new DataGridViewTextBoxCell();
                row.ReadOnly=true;
                row.Cells["Name"].Value=entry.First().Mod??entry.First().Path;
                var members=inventoryMembers[entry.Key];
                InventoryLinkGroup group;savedGroups.TryGetValue(entry.Key,out group);
                row.Cells["Name"].ToolTipText=String.Join(Environment.NewLine,members.Select(file=>file.Path).Take(12)
                    .Concat(group==null||group.Source==null?Enumerable.Empty<string>():new[]{group.Source}));
                row.Cells["Status"].Value=GameInventoryStatus;
                row.Cells["Origin"].Value="?";
                row.Cells["Origin"].ToolTipText=T(
                    "Arquivos encontrados no jogo. Arraste sobre outro item não identificado para agrupar.",
                    "Game files found outside this project. Drag onto another unidentified item to group them.");
            }
        }
        if(!picking&&!flat&&groupFilters==null&&coverageFilter==null&&searchTerms.Length==0&&(visible.Count>0||statusFilters==null))AddSection("new",null,56);
        else if(visible.Count==0&&inventory.Count==0)AddSection("empty",null,56);
        if(oldVersion!=null){
            var row=grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(candidate=>candidate.Visible&&
                candidate.Tag is Work&&SameVersion((Work)candidate.Tag,oldVersion));
            if(row!=null)grid.CurrentCell=row.Cells["Name"];
        }else if(old==null)grid.ClearSelection();
        if(scroll>=0&&scroll<grid.RowCount)try{grid.FirstDisplayedScrollingRowIndex=scroll;}catch(InvalidOperationException){}
        drawing=false;UpdateCounts();
    }
    void AddModRow(GeneralModEntry mod,string previous,bool picking,bool collapsed,CategoryBranch? branch){
        var variant=Selected(mod);
        string key=MakeWork(mod).Key,status=ListStatus(mod),raw=InstallationStatus(mod,status);
        int percent;
        var row=grid.Rows[grid.Rows.Add(marked.Contains(mod.Id),picking?mod.Name:ModDisplayName(mod),
            NexusVariantName(mod),GeneralStatusPresentation.Label(raw),progress.TryGetValue(key,out percent)?(object)percent:null,ModSpace(mod))];
        row.Tag=mod.Id;
        row.Cells["Status"].Tag=raw;
        row.Cells["Status"].ToolTipText=TableStatusDetail(mod,key,status);
        row.Cells["Size"].ToolTipText=SpaceTip(mod);
        row.Cells["Name"].ToolTipText=variant.Description??mod.Description;
        row.Cells["Name"].Tag=GeneralModLibrary.BodyOption(state.BodyChoice,mod,variant).Nsfw??mod.Nsfw;
        row.Cells["Origin"].Value="Mod Loader";
        row.Cells["Origin"].ToolTipText=variant.CyberpunkId==null
            ?T("Item cadastrado neste projeto do Mod Loader.","Item registered in this Mod Loader project.")
            :T("Item do catálogo do Mod Loader. Os arquivos também podem ter sido instalados manualmente.","Mod Loader catalog item. The files may also have been installed manually.");
        if(status==GeneralStatusPresentation.ChooseDependency)
            row.Cells["Name"].Style.ForeColor=row.Cells["Name"].Style.SelectionForeColor=ColorTranslator.FromHtml(GeneralStyle.Dark?"#ffbf80":"#9a4e00");
        row.Cells["CategoryIndent"].Tag=branch;
        if(collapsed)row.Visible=false;
        else if(mod.Id==previous)grid.CurrentCell=row.Cells["Name"];
        if(!picking)AddVersionRows(mod,row,collapsed);
    }

    DataGridViewRow AddSection(string kind,string category,int height){
        int i=grid.Rows.Add();var row=grid.Rows[i];row.Tag=new SectionRow(kind,category);row.Height=height;row.ReadOnly=true;
        row.Cells["Name"].Value=kind=="heading"?category??UncatName():kind=="inventory"?category:kind=="add"?T("Adicionar item","Add item"):kind=="new"?T("Criar nova categoria","Create new category"):"";
        return row;
    }

    static string DrawerKey(GeneralModEntry mod){
        return IsBigBody(mod)?GeneralModLibrary.BodyGroup:mod.Id;
    }

    bool ToggleVersions(DataGridViewRow row){
        var versions=row.Cells["Variant"].Tag as List<Work>;
        if(versions==null||versions.Count==0)return false;
        var id=DrawerKey(state.List.Mods.Single(mod=>mod.Id==(string)row.Tag));
        string parentId=(string)row.Tag;
        if(!expandedVersions.Add(id))expandedVersions.Remove(id);
        DrawRows();
        var parent=ModRows().FirstOrDefault(candidate=>candidate.Visible&&(string)candidate.Tag==parentId);
        if(parent!=null)grid.CurrentCell=parent.Cells["Variant"];
        return true;
    }

    void AddVersionRows(GeneralModEntry entry,DataGridViewRow parent,bool hidden){
        var versions=IsBigBody(entry)
            ?BodyVersionsForPreset()
            :entry.Variants.Skip(entry.Variants.Count>1?0:1).Select(variant=>new Work{Entry=entry,Variant=variant}).ToList();
        if(versions.Count==0)return;
        var cell=parent.Cells["Variant"];
        cell.Tag=versions;
        cell.Style.Padding=new Padding(10,0,30,0);
        cell.ToolTipText=T("Mostrar ou ocultar outras versões","Show or hide other versions");
        parent.Cells["Name"].Tag=versions.Any(work=>work.Variant.Nsfw??work.Entry.Nsfw);
        parent.Cells["Group"].Tag=NexusVariantName(entry);
        parent.Height=56;
        parent.Cells["Name"].ToolTipText=T("Clique na versão em dourado para mostrar ou ocultar o grupo.","Click the version in gold to show or hide the group.");
        if(!expandedVersions.Contains(DrawerKey(entry)))return;
        bool body=IsBigBody(entry);
        parent.Cells["Mark"].Tag="body-branch";
        var categoryBranch=parent.Cells["CategoryIndent"].Tag as CategoryBranch?;
        bool continues=categoryBranch.HasValue&&(categoryBranch.Value&CategoryBranch.Last)==0;
        for(int index=0;index<versions.Count;index++){
            var work=versions[index];
            string name=VersionDisplayName(work);
            string label=body?BodyChoiceLabel(work):work.Variant.Name;
            bool chosen=ActivePreset==null?VersionIsActive(work):IsPresetVersionSelected(work);
            string raw;
            statuses.TryGetValue(work.Key,out raw);
            bool installed=InstalledVersion(raw)||raw==GeneralStatusPresentation.Disabled;
            string status=installed?T("Instalado","Installed"):T("Não instalado","Not installed");
            var row=grid.Rows[grid.Rows.Add(null,name,label,status,null,BodyOptionSpace(work))];
            row.Tag=work;
            row.Cells["Name"].Tag=work.Variant.Nsfw??work.Entry.Nsfw;
            row.Height=36;
            row.ReadOnly=true;
            row.DefaultCellStyle.BackColor=GeneralStyle.DrawerSurface;
            row.Cells["Mark"].Tag=index==versions.Count-1?CategoryBranch.Last:CategoryBranch.Middle;
            if(continues)row.Cells["CategoryIndent"].Tag=CategoryBranch.Continuation;
            var textColor=chosen?GeneralStyle.Foreground:GeneralStyle.Muted;
            foreach(string column in new[]{"Name","Variant","Size"}){
                var style=row.Cells[column].Style;
                style.ForeColor=style.SelectionForeColor=textColor;
                if(chosen)style.Font=optionSelectedFont;
            }
            row.Cells["Name"].ToolTipText=String.Join("\r\n",new[]{work.Variant.Description??work.Entry.Description,
                T("Clique para escolher. Confirme no botão do rodapé. F2 abre os detalhes.","Click to select. Apply with the footer button. F2 opens details.")}.Where(value=>!String.IsNullOrWhiteSpace(value)));
            {
                row.Cells["Status"].Tag=chosen;
                row.Cells["Status"].ToolTipText=raw==GeneralStatusPresentation.Disabled
                    ?T("Versão guardada no cache deste projeto.","Version stored in this project's cache.")
                    :installed&&!chosen?T("Compartilha os arquivos da versão ativa.","Shares the files of the active version."):status;
                row.Cells["Size"].ToolTipText=row.Cells["Size"].Value.ToString().StartsWith("≈",StringComparison.Ordinal)
                    ?T("Tamanho do download. O espaço instalado ainda não foi medido.","Download size. Installed size has not been measured.")
                    :VerifiedBodyBytes(work.Entry,work.Variant).HasValue?T("Tamanho dos arquivos instalados e verificados.","Size of the verified installed files."):SpaceTip(work.Entry);
                row.Cells["Origin"].Value=parent.Cells["Origin"].Value;
                row.Cells["Origin"].ToolTipText=parent.Cells["Origin"].ToolTipText;
            }
            if(hidden)row.Visible=false;
        }
    }

    Font optionSelectedFont,groupIndicatorFont;

    string InstallationStatus(GeneralModEntry entry,string status){
        if(!IsBigBody(entry))return status;
        if(status==GeneralStatusPresentation.OtherChoice)return "Não iniciado";
        if(status!=GeneralStatusPresentation.ManualBody)return status;
        return VerifiedBodyBytes(entry,GeneralModLibrary.BodyOption(state.BodyChoice,entry,Selected(entry))).HasValue
            ?"Instalado":"Não conferido";
    }

    long? VerifiedBodyBytes(GeneralModEntry entry,GeneralModVariant variant){
        if(!IsBigBody(entry))return null;
        var standard=entry.Variants.FirstOrDefault(item=>item.Id==variant.Id);
        if(standard==null||!standard.Sources.SequenceEqual(variant.Sources,StringComparer.Ordinal)||
            !GeneralInstallRoutes.Same(standard.InstallRoutes,variant.InstallRoutes))return null;
        var match=gameInventory.FirstOrDefault(file=>file.Kind=="verified-variant"&&file.ModId==entry.Id&&file.VariantId==variant.Id);
        return match==null?null:match.VerifiedBytes;
    }

    string BodyOptionSpace(Work work){
        var verified=VerifiedBodyBytes(work.Entry,work.Variant);
        if(verified.HasValue)return GeneralStorageSize.Format(verified.Value);
        long installed;
        var selected=GeneralModLibrary.BodyOption(work.BodyChoice,work.Entry,Selected(work.Entry));
        if(work.Variant==selected&&IsActiveAlternative(work.Entry)&&measuredMods.TryGetValue(work.Entry.Id,out installed))return GeneralStorageSize.Format(installed);
        var sizes=work.Variant.Sources.Distinct(StringComparer.Ordinal).Select(source=>{
            GeneralSourceInfo info;
            return work.Variant.SourceInfo.TryGetValue(source,out info)?info.Bytes:0;
        }).ToList();
        return sizes.Count>0&&sizes.All(size=>size>0)?"≈ "+GeneralStorageSize.Format(sizes.Sum()):"—";
    }

    bool CanToggleCategory { get { return associationChoice==null&&coverageFilter==null&&searchTerms.Length==0; } }
    bool CategoryCollapsed(SectionRow section){return CanToggleCategory&&collapsedCategories.Contains(section.Category??"");}
    int CategoryContentRight {
        get { return grid.ClientSize.Width-(grid.Controls.OfType<VScrollBar>().Any(bar=>bar.Visible)?SystemInformation.VerticalScrollBarWidth:0); }
    }
    void ToggleCategory(DataGridViewRow heading,SectionRow section){
        if(!CanToggleCategory)return;
        bool collapse=collapsedCategories.Add(section.Category??"");
        if(!collapse)collapsedCategories.Remove(section.Category??"");
        grid.SuspendLayout();
        try{
            if(collapse)grid.CurrentCell=heading.Cells["Name"];
            for(int index=heading.Index+1;index<grid.RowCount;index++){
                var row=grid.Rows[index];
                var next=row.Tag as SectionRow;
                if(next!=null&&next.Kind!="add")break;
                row.Visible=!collapse;
            }
        }finally{grid.ResumeLayout(true);}
        grid.InvalidateRow(heading.Index);
        grid.InvalidateCell(0,-1);
        UpdateUninstallButton();
    }
    void UpdateUninstallButton(){
        if(removeButton!=null)removeButton.Visible=associationChoice==null&&UninstallSelection().Any();
    }
    void UpdateCounts(){
        if(counts==null)return;var rows=ModRows().ToList();int selected=rows.Count(r=>marked.Contains((string)r.Tag));
        grid.Columns[0].HeaderText="";
        grid.Columns[0].HeaderCell.ToolTipText=T("Selecionar todos os itens visíveis","Select all visible items");
        grid.InvalidateCell(0,-1);
        var visible=rows.Select(row=>state.List.Mods.Single(mod=>mod.Id==(string)row.Tag)).ToList();
        int installed=visible.Where(mod=>GeneralStatusPresentation.Kind(ListStatus(mod))=="installed")
            .Select(GeneralModLibrary.ChoiceKey).Distinct(StringComparer.Ordinal).Count();
        int failed=rows.Count(r=>{var m=state.List.Mods.Single(x=>x.Id==(string)r.Tag);string status;return statuses.TryGetValue(Key(m,Selected(m)),out status)&&GeneralStatusPresentation.Kind(status)=="error";});
        UpdateUninstallButton();
        RefreshPrimaryAction();
        counts.Text=T("Mods na lista: ","Mods in list: ")+rows.Count+"   ·   "+T("Medidos: ","Measured: ")+rows.Count(r=>measuredMods.ContainsKey((string)r.Tag))+"/"+rows.Count+"   ·   "+T("Instalados: ","Installed: ")+installed+"   ·   "+T("Falhas: ","Failed: ")+failed;UpdatePresetCommands();UpdatePackCoverage();
    }
    internal sealed class ModListGrid : DataGridView {
        public ModListGrid(){DoubleBuffered=true;}
    }
    void PaintSection(object sender,DataGridViewRowPrePaintEventArgs e){
        var section=grid.Rows[e.RowIndex].Tag as SectionRow;if(section==null)return;
        e.Handled=true;var g=e.Graphics;var bounds=e.RowBounds;bounds.Width=CategoryContentRight;
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
            bool heading=section.Kind=="heading"||section.Kind=="inventory";
            if(heading){
                text=section.Category??UncatName();y=bounds.Y+12;
                if(section.Kind=="heading"){
                    if(section.Category==null)description=state.List.UncategorizedDescription;
                    else state.List.CategoryDescriptions.TryGetValue(section.Category,out description);
                }
            }else if(section.Kind=="empty")text=T("Nenhum mod encontrado. Tente outro termo ou limpe os filtros.","No mods found. Try another term or clear the filters.");
            else text=section.Kind=="add"?"+ "+T("Adicionar item","Add item"):"+ "+T("Criar nova categoria","Create new category");
            const TextFormatFlags flags=TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.PreserveGraphicsClipping|TextFormatFlags.VerticalCenter;
            bool toggle=section.Kind=="heading"&&CanToggleCategory;
            int x=bounds.X+18+(section.Kind=="add"?CategoryIndent:0);
            int right=bounds.Right-(toggle?CategoryToggleWidth:20);
            using(var font=GeneralStyle.Font(heading?15:12,heading))
            using(var small=GeneralStyle.Font(10.5f)){
                string size=section.Size??"";
                int sizeWidth=size.Length==0?0:TextRenderer.MeasureText(size,small).Width+16;
                int width=Math.Max(1,Math.Min(right-x-sizeWidth,TextRenderer.MeasureText(text,font).Width));
                TextRenderer.DrawText(g,text,font,new Rectangle(x,y,width,30),heading?GeneralStyle.Foreground:GeneralStyle.Accent,flags);
                if(sizeWidth>0)TextRenderer.DrawText(g,size,small,new Rectangle(x+width+12,y,sizeWidth-12,30),GeneralStyle.Muted,flags);
            }
            if(toggle)LucideIcons.Draw(g,CategoryCollapsed(section)?"right":"down",new Rectangle(bounds.Right-36,y+6,18,18),GeneralStyle.Foreground);
            if(!String.IsNullOrWhiteSpace(description))using(var font=GeneralStyle.Font(10.5f))TextRenderer.DrawText(g,description.Replace("\r"," ").Replace("\n"," "),font,new Rectangle(bounds.X+18,y+31,bounds.Width-40,24),GeneralStyle.Muted,TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.PreserveGraphicsClipping);
        }finally{g.Restore(saved);}
    }
    static void PaintCategoryBranch(Graphics graphics,Rectangle bounds,CategoryBranch branch){
        int spine=bounds.X+10;
        int center=bounds.Top+bounds.Height/2;
        int start=bounds.Top+((branch&CategoryBranch.First)!=0?5:0);
        const int radius=9;
        bool last=(branch&CategoryBranch.Last)!=0;

        var previous=graphics.SmoothingMode;
        try{
            graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var pen=new Pen(Color.FromArgb(160,GeneralStyle.Line)))
            using(var elbow=new GraphicsPath()){
                if(branch==CategoryBranch.Continuation){
                    graphics.DrawLine(pen,spine,bounds.Top,spine,bounds.Bottom);
                    return;
                }
                if(last)elbow.AddLine(spine,start,spine,center-radius);
                else graphics.DrawLine(pen,spine,start,spine,bounds.Bottom);
                elbow.AddArc(spine,center-2*radius,2*radius,2*radius,180,-90);
                elbow.AddLine(spine+radius,center,bounds.Right-3,center);
                graphics.DrawPath(pen,elbow);
            }
        }finally{graphics.SmoothingMode=previous;}
    }
    void PaintName(object sender,DataGridViewCellPaintingEventArgs e){
        if(e.ColumnIndex==grid.Columns["CategoryIndent"].Index){
            e.PaintBackground(e.ClipBounds,true);
            if(e.RowIndex>=0){
                var branch=grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Tag as CategoryBranch?;
                if(branch.HasValue)PaintCategoryBranch(e.Graphics,e.CellBounds,branch.Value);
            }
            e.Handled=true;return;
        }
        if(e.RowIndex==-1&&e.ColumnIndex>0){
            e.PaintBackground(e.ClipBounds,true);
            string column=grid.Columns[e.ColumnIndex].Name;
            if(associationChoice==null&&IsFilterColumn(column)){
                bool active=ColumnFilterActive(column)||orderColumn==column;
                PaintFilterHeader(e,e.CellBounds,grid.Columns[e.ColumnIndex].HeaderText,active);
                e.Handled=true;return;
            }
        }
        if(e.RowIndex>=0&&e.ColumnIndex==0&&grid.Rows[e.RowIndex].Tag is Work){
            e.PaintBackground(e.ClipBounds,true);
            var bounds=e.CellBounds;
            bounds.X+=(bounds.Width/2)-10;bounds.Width-=(e.CellBounds.Width/2)-10;
            PaintCategoryBranch(e.Graphics,bounds,(CategoryBranch)grid.Rows[e.RowIndex].Cells[0].Tag);
            e.Handled=true;return;
        }
        if(e.RowIndex>=0&&e.ColumnIndex==0&&Equals(grid.Rows[e.RowIndex].Cells[0].Tag,"body-branch")){
            e.Paint(e.ClipBounds,DataGridViewPaintParts.All);
            int spine=e.CellBounds.Left+e.CellBounds.Width/2;
            using(var pen=new Pen(Color.FromArgb(160,GeneralStyle.Line)))
                e.Graphics.DrawLine(pen,spine,e.CellBounds.Top+e.CellBounds.Height/2+8,spine,e.CellBounds.Bottom);
            e.Handled=true;return;
        }
        if(e.RowIndex>=0&&e.ColumnIndex==grid.Columns["Status"].Index&&grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Tag is bool){
            bool selected=(bool)grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Tag;
            PaintBadge(e,Convert.ToString(e.FormattedValue),selected?Color.FromArgb(11,116,75):Color.FromArgb(58,58,62),selected?Color.White:Color.FromArgb(210,210,214));
            return;
        }
        if(e.RowIndex>=0&&e.ColumnIndex==grid.Columns["Variant"].Index&&grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Tag is List<Work>){
            e.Paint(e.ClipBounds,DataGridViewPaintParts.All);
            var owner=state.List.Mods.Single(item=>item.Id==(string)grid.Rows[e.RowIndex].Tag);
            LucideIcons.Draw(e.Graphics,expandedVersions.Contains(DrawerKey(owner))?"down":"right",
                new Rectangle(e.CellBounds.Right-24,e.CellBounds.Y+(e.CellBounds.Height-14)/2,14,14),GeneralStyle.Muted);
            e.Handled=true;return;
        }
        if(e.RowIndex>=0&&e.ColumnIndex==grid.Columns["Origin"].Index){
            var item=grid.Rows[e.RowIndex].Tag;
            e.PaintBackground(e.ClipBounds,true);
            var bounds=e.CellBounds;
            if(item is string||item is Work){
                var icon=new Rectangle(bounds.X+(bounds.Width-20)/2,bounds.Y+(bounds.Height-20)/2,20,20);
                ModLoaderBrand.Draw(e.Graphics,icon,true);
            }else if(item is P2PSyncDifference){
                TextRenderer.DrawText(e.Graphics,"?",e.CellStyle.Font,bounds,GeneralStyle.Muted,
                    TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.PreserveGraphicsClipping);
            }
            e.Handled=true;return;
        }
        if(e.RowIndex>=0&&grid.Rows[e.RowIndex].Tag is string){
            if(e.ColumnIndex==grid.Columns["Status"].Index){PaintStatus(e);return;}

        }
        if(e.RowIndex==-1&&e.ColumnIndex==0){
            e.PaintBackground(e.ClipBounds,true);var rows=ModRows().Where(r=>r.Visible).ToList();int selected=rows.Count(r=>marked.Contains((string)r.Tag));
            var value=selected==0?System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal:selected==rows.Count?System.Windows.Forms.VisualStyles.CheckBoxState.CheckedNormal:System.Windows.Forms.VisualStyles.CheckBoxState.MixedNormal;
            CheckBoxRenderer.DrawCheckBox(e.Graphics,new Point(e.CellBounds.Left+16,e.CellBounds.Top+(e.CellBounds.Height-14)/2),value);e.Handled=true;return;
        }
        if(e.RowIndex<0||e.ColumnIndex!=grid.Columns["Name"].Index)return;
        var row=grid.Rows[e.RowIndex];
        bool adult=Equals(row.Cells["Name"].Tag,true);
        string version=row.Cells["Group"].Tag as string;
        if(!adult&&version==null)return;
        string name=Convert.ToString(e.FormattedValue);
        e.PaintBackground(e.ClipBounds,true);
        var box=e.CellBounds;box.Inflate(-10,0);
        if(version!=null)box.Height=box.Height/2+4;
        int suffix=adult?52:0;
        int nameWidth=Math.Min(Math.Max(1,box.Width-suffix),TextRenderer.MeasureText(name,e.CellStyle.Font).Width);
        const TextFormatFlags flags=TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine|TextFormatFlags.PreserveGraphicsClipping;
        TextRenderer.DrawText(e.Graphics,name,e.CellStyle.Font,new Rectangle(box.X,box.Y,nameWidth,box.Height),e.CellStyle.ForeColor,flags);
        if(adult)TextRenderer.DrawText(e.Graphics,"NSFW",groupIndicatorFont,new Rectangle(box.X+nameWidth+4,box.Y,suffix-4,box.Height),GeneralStyle.Danger,flags);
        if(version!=null){
            Color gold=ColorTranslator.FromHtml(GeneralStyle.Dark?"#F2C15A":"#805704");
            TextRenderer.DrawText(e.Graphics,version,groupIndicatorFont,
                new Rectangle(box.X,e.CellBounds.Y+e.CellBounds.Height/2,box.Width,e.CellBounds.Height/2-4),gold,flags);
        }
        e.Handled=true;
    }
    static void PaintFilterHeader(DataGridViewCellPaintingEventArgs e,Rectangle bounds,string label,bool active){
        var saved=e.Graphics.Save();
        try{
            e.Graphics.SetClip(Rectangle.Intersect(bounds,e.ClipBounds),System.Drawing.Drawing2D.CombineMode.Intersect);
            TextRenderer.DrawText(e.Graphics,label,e.CellStyle.Font,new Rectangle(bounds.X+8,bounds.Y,Math.Max(1,bounds.Width-30),bounds.Height),
                GeneralStyle.Muted,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.PreserveGraphicsClipping);
            LucideIcons.Draw(e.Graphics,"down",new Rectangle(bounds.Right-20,bounds.Y+15,14,14),active?GeneralStyle.Accent:GeneralStyle.Muted);
        }finally{e.Graphics.Restore(saved);}
    }
    void SetHover(int row){
        if(row==hoveredRow)return;
        int old=hoveredRow;hoveredRow=row;
        foreach(int at in new[]{old,row}){
            if(at<0||at>=grid.RowCount)continue;
            var item=grid.Rows[at];
            var section=item.Tag as SectionRow;if(section!=null&&section.Kind=="divider")continue;
            Color resting=item.Tag is Work?GeneralStyle.DrawerSurface:GeneralStyle.Surface;
            item.DefaultCellStyle.BackColor=at==hoveredRow?GeneralStyle.Selection:resting;
            grid.InvalidateRow(at);
        }
        grid.Cursor=row>=0&&row<grid.RowCount?Cursors.Hand:Cursors.Default;
    }
    void PaintStatus(DataGridViewCellPaintingEventArgs e){
        string raw=grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Tag as string;
        PaintBadge(e,GeneralStatusPresentation.Label(raw),GeneralStatusPresentation.ColorFor(raw),Color.White);
    }
    static void PaintBadge(DataGridViewCellPaintingEventArgs e,string label,Color fill,Color textColor){
        e.PaintBackground(e.ClipBounds,true);e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using(var font=GeneralStyle.Font(10.5f)){
            int width=Math.Min(e.CellBounds.Width-20,TextRenderer.MeasureText(label,font).Width+22);
            var rect=new Rectangle(e.CellBounds.X+10,e.CellBounds.Y+(e.CellBounds.Height-30)/2,Math.Max(20,width),30);
            using(var round=GeneralStyle.Round(rect,15))using(var brush=new SolidBrush(fill))e.Graphics.FillPath(brush,round);
            TextRenderer.DrawText(e.Graphics,label,font,new Rectangle(rect.X+11,rect.Y,rect.Width-22,rect.Height),textColor,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.PreserveGraphicsClipping|TextFormatFlags.EndEllipsis);
        }
        e.Handled=true;
    }
    void HandleSection(SectionRow section){
        if(section==null||working||scanning||associationChoice!=null)return;
        if(section.Kind=="add"){var item=NewEntry("",null);item.Category=section.Category;Edit(item);}
        if(section.Kind=="heading")EditCategory(section.Category,false);
        if(section.Kind=="new")EditCategory(null,true);
    }
    static GeneralModEntry NewEntry(string name,string source){
        var v=new GeneralModVariant{Id="default",Name=T("Principal","Main")};if(source!=null)v.Sources.Add(source);
        return new GeneralModEntry{Id="mod-"+Guid.NewGuid().ToString("N"),Name=name,DefaultVariant=v.Id,Variants=new List<GeneralModVariant>{v}};
    }
    void OpenRowEditor(DataGridViewRow row){
        if(row==null)return;
        var id=row.Tag as string;
        if(id!=null){
            var mod=state.List.Mods.Single(item=>item.Id==id);
            Edit(mod);
        }
        else if(row.Tag is Work)SelectVersion((Work)row.Tag);
        else if(row.Tag is P2PSyncDifference)EditDetectedMod((P2PSyncDifference)row.Tag);
    }

    async void EditDetectedMod(P2PSyncDifference item){
        if(working||scanning||!ConfirmProjectNavigation())return;
        string name=item.Mod??item.Path;
        var entry=NewEntry(name.Length<=300?name:name.Substring(0,300),null);
        entry.DetectedPaths=InventoryRow(InventoryLinkStore.Key(item))
            .Select(file=>file.Path).Where(FixedInstaller.SyncModPath).Distinct(StringComparer.OrdinalIgnoreCase).Take(2000).ToList();
        if(entry.DetectedPaths.Count==0)return;
        var group=InventoryLinks.Groups.FirstOrDefault(value=>value.Id==item.InventoryGroupId);
        entry.Variants[0].Sources.Add(group!=null&&group.Source!=null?group.Source:"installed://"+entry.Id+"/"+entry.DefaultVariant);
        await EditMod(entry,true);
    }

    async void Edit(GeneralModEntry item){await EditMod(item);}
    async void EditBodyOption(Work body){await EditMod(body.Entry,initialVariant:body.Variant,bodyChoice:body.BodyChoice);}
    List<Work> BodyVersionsForPreset(){
        return BigBodyVersions(state.List.Mods,IsCyberpunk(state.Destination)&&GameEdition.HasPL(state.Destination))
            .Where(work=>ActivePreset==null||ActivePreset.ModIds.Contains(work.Entry.Id)).ToList();
    }

    static string VersionDisplayName(Work work){
        return work.Variant.DisplayName??(work.BodyChoice==GeneralModLibrary.NaturalBody
            ?"Hyst EBBN + RB (Natural)":work.Entry.Name);
    }

    static GeneralModVariant CloneVariant(GeneralModVariant variant){
        return Safe.Json.Deserialize<GeneralModVariant>(Safe.Json.Serialize(variant));
    }

    GeneralModEntry BodyEditorEntry(GeneralModEntry entry,List<Work> choices){
        var result=new GeneralModEntry { Id=entry.Id,Name=ModDisplayName(entry),Nsfw=choices.Any(work=>work.Variant.Nsfw??work.Entry.Nsfw),
            AlternativeGroup=entry.AlternativeGroup, DefaultVariant=choices[0].BodyChoice };
        foreach(var choice in choices){
            var version=CloneVariant(choice.Variant);
            version.Id=choice.BodyChoice;
            version.Name=BodyChoiceLabel(choice);
            version.NaturalOption=null;
            version.DisplayName=VersionDisplayName(choice);
            version.Category=version.Category??choice.Entry.Category??"";
            version.Description=version.Description??(choice.BodyChoice==GeneralModLibrary.NaturalBody
                ?T("Formato natural do EBB com quadril RB. Usa o arquivo do EBB + RB e os refits do perfil escolhido.","Natural EBB shape with RB hips. Uses the EBB + RB archive and the selected profile’s refits.")
                :choice.Entry.Description);
            version.Tags=version.Tags??choice.Entry.Tags.ToList();
            version.NsfwOverride=version.Nsfw.HasValue?version.NsfwOverride:choice.Entry.NsfwOverride;
            version.Nsfw=version.Nsfw??choice.Entry.Nsfw;
            result.Variants.Add(version);
        }
        return result;
    }

    async Task<bool> ApplyBodyEditor(GeneralModEditor dialog,List<Work> choices){
        var changed=new List<KeyValuePair<GeneralModEntry,GeneralModEntry>>();
        foreach(var group in choices.GroupBy(work=>work.Entry)){
            var original=group.Key;
            var edited=Safe.Json.Deserialize<GeneralModEntry>(Safe.Json.Serialize(original));
            foreach(var work in group){
                var replacement=CloneVariant(dialog.Result.Variants.Single(version=>version.Id==work.BodyChoice));
                var target=edited.Variants.Single(version=>version.Id==work.Variant.Id);
                replacement.Id=target.Id;
                if(GeneralModLibrary.IsHystBody(original.Id))replacement.Name=target.Name;
                if(work.BodyChoice==GeneralModLibrary.NaturalBody)target.NaturalOption=replacement;
                else{
                    replacement.NaturalOption=target.NaturalOption;
                    edited.Variants[edited.Variants.IndexOf(target)]=replacement;
                }
            }
            changed.Add(new KeyValuePair<GeneralModEntry,GeneralModEntry>(original,edited));
        }
        var selected=choices.FirstOrDefault(work=>work.BodyChoice==dialog.SelectedVariant);
        var added=dialog.Result.Variants.Where(version=>!choices.Any(work=>work.BodyChoice==version.Id))
            .Select(version=>new GeneralModEntry{
                Id=version.Id, Name=version.DisplayName, Category=version.Category, Description=version.Description,
                Tags=version.Tags, Nsfw=version.Nsfw??false, NsfwOverride=version.NsfwOverride,
                AlternativeGroup=GeneralModLibrary.BodyGroup, DefaultVariant=version.Id,
                Variants=new List<GeneralModVariant>{version}
            }).ToList();
        var routes=changed.Where(pair=>pair.Key.Variants.Zip(pair.Value.Variants,(before,after)=>
            !GeneralInstallRoutes.Same(before.InstallRoutes,after.InstallRoutes)).Any(value=>value)).ToList();
        if(routes.Count>1)throw new IOException(T("Altere o destino de um corpo por vez.","Change the destination of one body at a time."));
        bool? scope=PresetEditScope(choices[0].Entry);if(!scope.HasValue)return false;bool? moved=false;
        if(!moved.HasValue)return false;
        BeginProjectEdit();
        state.List.AlternativeGroups[GeneralModLibrary.BodyGroup].DisplayName=dialog.Result.Name;
        if(scope.Value)foreach(var p in state.List.Presets.Where(p=>choices.Any(w=>p.ModIds.Contains(w.Entry.Id))))
            p.Groups[GeneralModLibrary.BodyGroup]=GeneralPresetProfiles.Copy(state.List.AlternativeGroups[GeneralModLibrary.BodyGroup]);
        if(selected!=null) {
            state.BodyChoice=selected.BodyChoice;
            foreach(var p in state.List.Presets.Where(p=>p.Id==state.SelectedPreset||scope.Value&&p.ModIds.Contains(selected.Entry.Id))){p.BodyChoice=selected.BodyChoice;p.AlternativeChoices[GeneralModLibrary.BodyGroup]=selected.Entry.Id;}
        }
        foreach(var pair in changed){
            string profile=selected!=null&&pair.Key==selected.Entry?selected.Variant.Id:Selected(pair.Key).Id;
            GeneralPresetProfiles.Edit(state,pair.Key,pair.Value,profile,scope.Value,pair.Key.Id==choices[0].Entry.Id?dialog.ChangedPresetIds:null);
            var live=state.List.Mods.Single(m=>m.Id==pair.Key.Id);
            ApplyEdited(live,live,profile,true,dialog.NewCategories,finish:false);
        }
        foreach(var entry in added){
            var presetIds=state.List.Presets.Where(preset=>choices.All(work=>preset.ModIds.Contains(work.Entry.Id))).ToList();
            ApplyEdited(entry,entry,entry.DefaultVariant,false,dialog.NewCategories,finish:false);
            foreach(var preset in presetIds)
                if(!preset.ModIds.Contains(entry.Id))preset.ModIds.Add(entry.Id);
        }
        FinishProjectEdit();
        DrawRows();
        if(dialog.InstallRequested||moved.Value)SaveProjectEdits();
        if(dialog.InstallRequested&&!NeedsPresetLoad)ConfirmUrgentInstall(state.List.Mods.Single(entry=>entry.Id==(selected==null?dialog.SelectedVariant:selected.Entry.Id)),dialog.SelectedVariant);
        else await Scan();
        return true;
    }

    async Task EditMod(GeneralModEntry item,bool fromInventory=false,GeneralModVariant initialVariant=null,string bodyChoice=null){
        if(scanning||associationChoice!=null)return;
        if((current!=null&&current.Entry.Id==item.Id)||(priorityCurrent!=null&&priorityCurrent.Entry.Id==item.Id)){detail.Text=T("Aguarde esta operação terminar antes de editar suas fontes.","Wait for this operation to finish before editing its sources.");return;}
        try{
            bool existing=state.List.Mods.Contains(item);var selected=initialVariant??Selected(item);
            bool editingBody=IsBigBody(item);
            if(working&&(editingBody||BuiltInProject))return;
            var bodyVersions=editingBody?BodyVersionsForPreset():null;
            var editorEntry=editingBody?BodyEditorEntry(item,bodyVersions):item;
            string editorSelection=editingBody?bodyChoice??ActiveBodyChoice(item):selected.Id;
            string info="";
            if(item.DetectedPaths.Count>0)info=T(
                "Arquivos detectados. Confira o pacote de origem para confirmar o mod completo.",
                "Detected files. Check the original archive to confirm the complete mod.")+"\r\n"+
                String.Join("\r\n",item.DetectedPaths.Take(8));
            var companions=DisplayedInventory().Where(file=>file.Kind=="associated"&&file.ModId==item.Id).ToList();
            if(companions.Count>0)info+="\r\n"+T("Arquivos locais associados", "Associated local files")
                +" ("+companions.Count+")\r\n"+String.Join("\r\n",companions.Select(file=>file.Path).Take(8));
            using(var dialog=new GeneralModEditor(editorEntry,PresetCategories(),editorSelection,!fromInventory&&(!working||current!=null),
                info,LookupSourceInfo,working?null:state.List.Mods,state.List,projectDraft:!working,
                installationFolder:state.Destination,inventoryDraft:fromInventory,
                                refreshLookup:source=>testMode?LookupSourceInfo(source):GeneralNexusInfo.Read(source),
                catalogPaths:variant=>{
                    if(String.IsNullOrEmpty(variant.CyberpunkId))return Enumerable.Empty<string>();
                    return ProfileRecipe(new Work{Entry=item,Variant=variant}).Files.Select(file=>file.Path);
                }, downloadedSource:(variant,index)=>{
                    if(!editingBody)return LocalSource(new Work{Entry=item,Variant=variant},index);
                                        var work=bodyVersions.FirstOrDefault(choice=>choice.BodyChoice==variant.Id);
                    if(work==null)return null;
                    return LocalSource(work,index);
                },fixedChoices:editingBody,archivePreview:testMode
                    ?(Func<GeneralNexusLink,NexusFilePreview[]>)(link=>testNexusPreviews==null?null:testNexusPreviews(link,()=>!IsDisposed))
                    :nexusScan.PreviewSession(GeneralNexusInfo.ReadFilePreviews),
                cyberpunkLayout:IsCyberpunk(state.Destination),backupFile:variant=>{
                    if(!Directory.Exists(state.Destination)||variant==null)return null;
                    var selectedWork=editingBody?bodyVersions.FirstOrDefault(choice=>choice.BodyChoice==variant.Id):null;
                    var original=item.Variants.FirstOrDefault(option=>option.Id==variant.Id);
                    string key=selectedWork!=null?selectedWork.Key:original==null?null:Key(item,original);
                    return key==null?null:DisabledAt(state.Destination).BackupFile(key);
                })){
                dialog.ConfigurePresets(state.List.Presets,state.SelectedPreset,item.Id,existing);
                DialogResult answer;
                while((answer=dialog.ShowDialog(this))==DialogResult.Retry){
                    var linked=await PickAlternative(item.Id,null,true);
                    if(linked!=null)dialog.AddRequiredMod(linked);
                    if(IsDisposed||exitApproved)return;
                }
                if(answer!=DialogResult.OK)return;
                if(editingBody){await ApplyBodyEditor(dialog,bodyVersions);return;}
                bool? scope=existing?PresetEditScope(item):false;if(!scope.HasValue)return;
                bool? moved=false;
                if(!moved.HasValue)return;
                BeginProjectEdit();
                dialog.Result.AlternativeGroup=item.AlternativeGroup;
                var editedVariant=dialog.Result.Variants.Single(variant=>variant.Id==dialog.SelectedVariant);
                bool needsScan=!existing||selected.Id!=editedVariant.Id||
                    !selected.Sources.SequenceEqual(editedVariant.Sources,StringComparer.Ordinal)||!GeneralInstallRoutes.Same(selected.InstallRoutes,editedVariant.InstallRoutes);
                if(existing&&scope.Value)ApplyCategoryName(dialog.CategoryRenameFrom,dialog.Result);
                ApplyEdited(item,dialog.Result,dialog.SelectedVariant,existing,dialog.NewCategories,finish:false);
                GeneralPresetProfiles.Edit(state,item,dialog.Result,dialog.SelectedVariant,scope.Value,existing?dialog.ChangedPresetIds:dialog.PresetIds,selected.Id);
                FinishProjectEdit();DrawRows();
                if(fromInventory||moved.Value)SaveProjectEdits();
                if(dialog.InstallRequested){SaveProjectEdits();if(!NeedsPresetLoad)ConfirmUrgentInstall(state.List.Mods.Single(m=>m.Id==dialog.Result.Id));}
                else if(!working&&needsScan)await Scan();
            }
        }catch(Exception ex){ShowError(ex);}
    }
    void ApplyCategoryName(string categoryFrom,GeneralModEntry entry){
        string name=entry.Category;
        if(String.IsNullOrEmpty(name))return;
        string previous=state.List.Categories.FirstOrDefault(item=>String.Equals(item,categoryFrom,StringComparison.OrdinalIgnoreCase));
        string known=state.List.Categories.FirstOrDefault(item=>String.Equals(item,name,StringComparison.OrdinalIgnoreCase));
        if(previous!=null&&previous!=name&&(known==null||known==previous)){
            string description;
            state.List.CategoryDescriptions.TryGetValue(previous,out description);
            RenameCategory(previous,name,description);
        }else if(known!=null)entry.Category=known;
    }
    void ApplyEdited(GeneralModEntry before,GeneralModEntry after,string variant,bool existing,Dictionary<string,string> newCategories=null,bool finish=true){
        if(existing){
            foreach(var pendingList in new[]{normal,priority})foreach(var queued in pendingList.Where(w=>w.Entry.Id==before.Id).ToList()){
                var nextVariant=after.Variants.FirstOrDefault(v=>v.Id==variant);
                if(nextVariant!=null&&queued.Variant.Id==variant&&queued.Variant.CyberpunkId==nextVariant.CyberpunkId&&queued.Variant.CyberpunkVariant==nextVariant.CyberpunkVariant&&queued.Variant.Sources.SequenceEqual(nextVariant.Sources,StringComparer.Ordinal)&&GeneralInstallRoutes.Same(queued.Variant.InstallRoutes,nextVariant.InstallRoutes)){
                    queued.Entry=after;queued.Variant=nextVariant;
                }else{pendingList.Remove(queued);operationTotal=Math.Max(operationFinished,operationTotal-1);}
            }
            foreach(var old in before.Variants){
                var replacement=after.Variants.FirstOrDefault(v=>v.Id==old.Id);
                if(replacement==null||!replacement.Sources.SequenceEqual(old.Sources,StringComparer.Ordinal)){statuses.Remove(Key(before,old));statusDetails.Remove(Key(before,old));progress.Remove(Key(before,old));state.SnapshotReceipts.Remove(Key(before,old));}
            }
            if(working)UpdateFlow(null,"");
        }
        if(newCategories!=null){
            foreach(var category in newCategories.Where(category=>!state.List.Categories.Contains(category.Key,StringComparer.OrdinalIgnoreCase))){
                state.List.Categories.Add(category.Key);
                state.List.CategoryDescriptions[category.Key]=category.Value;
                if(ActivePreset!=null)ActivePreset.Categories.Add(category.Key);
                if(categoryFilters!=null)categoryFilters.Add(category.Key);
            }
        }
        string group=after.Category;
        if(!String.IsNullOrEmpty(group)&&!state.List.Categories.Contains(group,StringComparer.OrdinalIgnoreCase))state.List.Categories.Add(group);
        if(existing)state.List.Mods[state.List.Mods.IndexOf(before)]=after;else{
            state.List.Mods.Add(after);var active=state.List.Presets.FirstOrDefault(p=>p.Id==state.SelectedPreset);
            if(active!=null&&!active.ModIds.Contains(after.Id))active.ModIds.Add(after.Id);
        }
        if(variant==after.DefaultVariant&&!GeneralModLibrary.IsHystBody(after.Id))state.ChosenVariants.Remove(after.Id);
        else state.ChosenVariants[after.Id]=variant;
        if(finish){FinishProjectEdit();DrawRows();}
    }
    sealed class CatalogDrop {
        internal Work Work;
        internal FixedAsset Asset;
        internal int SourceIndex;
    }

    CatalogDrop FindCatalogDrop(string file, Work[] candidates) {
        Safe.NoLinks(file);
        long bytes = new FileInfo(file).Length;
        var digest = new Lazy<string>(() => Safe.Hash(file));
        var fixedMatches = new List<CatalogDrop>();
        foreach (var work in candidates.Where(item => item.Variant.CyberpunkId != null)) {
            var recipe = Recipe(work);
            if (work.Variant.CyberpunkVariant != null)
                recipe = ProfileRecipe(work);
            var asset = recipe.Assets.FirstOrDefault(item => item.Size == bytes && item.Sha256 == digest.Value);
            if (asset != null) fixedMatches.Add(new CatalogDrop { Work = work, Asset = asset });
        }
        if (fixedMatches.Count != 0)
            return fixedMatches.All(match => match.Work.Entry.Id == fixedMatches[0].Work.Entry.Id) ? fixedMatches[0] : null;

        CatalogDrop found = null;
        foreach (var work in candidates.Where(item => item.Variant.CyberpunkId == null)) {
            for (int index = 0; index < work.Variant.Sources.Count; index++) {
                string source = work.Variant.Sources[index];
                GeneralSourceInfo info;
                work.Variant.SourceInfo.TryGetValue(source, out info);
                if (!String.Equals(source, file, StringComparison.OrdinalIgnoreCase) &&
                    !GeneralModDownload.MatchesCompleted(file, source, info, null)) continue;
                if (found != null && found.Work.Entry.Id != work.Entry.Id) return null;
                if (found == null) found = new CatalogDrop { Work = work, SourceIndex = index };
                break;
            }
        }
        return found;
    }

    bool CatalogDropHasLocalSources(Work work) {
        return work.DropConflicts != null && work.Variant.CyberpunkId == null &&
            Enumerable.Range(0, work.Variant.Sources.Count).All(index => File.Exists(LocalSource(work, index)));
    }

    Func<string, bool> testCatalogDropPrompt;
    async Task ImportCatalogDrop(string file, CatalogDrop match) {
        if(projectDraft!=null&&!ConfirmProjectNavigation())return;
        var work = match.Work;
        if(!state.List.Mods.Contains(work.Entry))return;
        if (new[] { current, priorityCurrent }.Any(item => item != null && item.Entry.Id == work.Entry.Id)) {
            detail.Text = work.Entry.Name + T(": já está na fila.", ": already in the queue.");
            return;
        }
        if (work.Entry.AlternativeGroup != null && normal.Concat(priority).Concat(new[] { current, priorityCurrent }).Any(item =>
            item != null && item.Entry.Id != work.Entry.Id && item.Entry.AlternativeGroup == work.Entry.AlternativeGroup)) {
            detail.Text = T("Outra alternativa deste grupo já está na fila.", "Another alternative from this group is already queued.");
            return;
        }
        if (String.IsNullOrWhiteSpace(state.Destination) || !Directory.Exists(state.Destination)) {
            string folder = WindowsFolderPicker.Choose(this, T("Pasta de instalação", "Installation folder"));
            if (folder == null) return;
            SetDestination(WindowsFolderPicker.InstallationPath(folder));
            Persist();
        }

        string root = Root();
        var conflicts = await Task.Run(() => ConflictingAlternatives(work, root));
        if (IsDisposed) return;
        string variant = work.Variant.Name;
        string version;
        if (match.Asset != null) version = Recipe(work).Version;
        else {
            GeneralSourceInfo info;
            work.Variant.SourceInfo.TryGetValue(work.Variant.Sources[match.SourceIndex], out info);
            version = info == null ? null : info.Version ?? info.ModVersion;
        }

        string message = T("Importar e instalar ", "Import and install ") + work.Entry.Name + "?\n\n" + variant;
        if (!String.IsNullOrEmpty(version) && !variant.Contains(version)) message += " · " + version;
        if (conflicts.Count != 0)
            message += T("\n\nEstas alternativas serão desativadas após preparar os arquivos da nova versão. Elas não podem ficar ativas juntas.",
                "\n\nThese alternatives will be disabled once the new files are ready. They cannot be active together.") +
                "\n" + String.Join("\n", conflicts.Select(old => old.Entry.Name));
        if (IsCyberpunk(root) && (work.Variant.CyberpunkVariant != null || work.Variant.CyberpunkId != null && EquipmentExOptions.Available(Recipe(work)))) {
            var recipe = Recipe(work); var equipment = EquipmentExOptions.Available(recipe); var target = equipment ? work.Variant.Id : work.Variant.CyberpunkVariant; var density = await Task.Run(() => equipment ? EquipmentExOptions.Read(recipe, root) : NightCityDensity.Read(recipe, root));
            if (density.CanApply && density.Id != "absent" && density.Id != target &&
                !(density.Id == "legacy-low" && work.Variant.CyberpunkVariant == "low")) {
                message += T("\n\nSerá substituída a variante ativa: ", "\n\nThe active variant will be replaced: ") + density.Name;
                work.DropDensity = density.Id;
            }
        }
        if (IsDisposed) return;
        bool confirmed = testMode ? testCatalogDropPrompt != null && testCatalogDropPrompt(message) :
            GeneralDialog.Message(this, message, T("Importar mod do catálogo", "Import catalog mod"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        if (!confirmed) return;

        if (match.Asset != null) await Task.Run(() => cache.Import(file, match.Asset));
        else if (GeneralModDownload.IsWebSource(work.Variant.Sources[match.SourceIndex])) {
            state.ResolvedDownloads[work.Receipt(match.SourceIndex)] = file;
            Persist();
        }
        work.DropConflicts = new HashSet<string>(conflicts.Select(old => old.Key), StringComparer.Ordinal);
        Enqueue(new[] { work }, true);
    }

    async void Drop(string[] files) {
        if (files == null || editingAttachment || associationChoice != null) return;
        editingAttachment = true;
        try {
            var paths = files.Where(file => GeneralModDownload.IsArchivePath(file) && File.Exists(file))
                .Select(GeneralModLibrary.ValidateSource).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (paths.Count == 0) throw new IOException(T("Arraste um arquivo ZIP, RAR ou 7z concluído.", "Drop a completed ZIP, RAR or 7z file."));
            while (scanning && !IsDisposed) await Task.Delay(100);
            if (IsDisposed) return;

            foreach (string file in paths) {
                var target = waiting && priorityCurrent == null ? current : null;
                if (target != null && association == null) {
                    var expected = pendingFixedAsset;
                    var web = pendingWebSource;
                    var info = pendingWebInfo;
                    if (await Task.Run(() => GeneralModDownload.MatchesCompleted(file, web, info, expected)) &&
                        Object.ReferenceEquals(current, target) && waiting && association == null) {
                        association = file;
                        continue;
                    }
                }
                var candidates = state.List.Mods.SelectMany(entry => entry.Variants
                    .OrderByDescending(variant => Object.ReferenceEquals(Selected(entry), variant))
                    .ThenBy(variant => variant.Sources.Count)
                    .Select(variant => new Work { Entry = entry, Variant = variant })).ToArray();
                var match = await Task.Run(() => FindCatalogDrop(file, candidates));
                if (IsDisposed) return;
                if (match != null) {
                    await ImportCatalogDrop(file, match);
                    continue;
                }

                var added = NewEntry(Path.GetFileNameWithoutExtension(file), file);
                added.Category = DefaultCategory;
                if (paths.Count != 1 || target == null || !Object.ReferenceEquals(current, target) || !waiting || association != null || BuiltInProject) {
                    await EditMod(added);
                    continue;
                }
                using (var editor = new GeneralModEditor(added, PresetCategories(), added.DefaultVariant, true,
                    T("Adicione este mod ou associe o arquivo ao download atual.", "Add this mod or attach its file to the current download."),groupList:state.List,installationFolder:state.Destination)) {
                    editor.OfferAttachment(target.Entry);
                    if (editor.ShowDialog(this) != DialogResult.OK) return;
                    if (editor.AttachRequested) {
                        if (!Object.ReferenceEquals(current, target) || !waiting) return;
                        var meta = editor.Result;
                        var updated = new GeneralModEntry { Id = target.Entry.Id, Name = meta.Name, Category = meta.Category,
                            Description = meta.Description, Tags = meta.Tags, CompatibilityGroups=target.Entry.CompatibilityGroups, Nsfw = meta.Nsfw, NsfwOverride=meta.NsfwOverride, DetectedPaths=target.Entry.DetectedPaths, AlternativeGroup = target.Entry.AlternativeGroup,
                            DefaultVariant = target.Entry.DefaultVariant, Variants = target.Entry.Variants, DependencySource = target.Entry.DependencySource,
                            DependencyFile = target.Entry.DependencyFile };
                        ApplyEdited(target.Entry, updated, target.Variant.Id, true, editor.NewCategories);
                        target.Entry = updated;
                        association = file;
                    } else {
                        ApplyEdited(added, editor.Result, editor.SelectedVariant, false, editor.NewCategories);
                        if (editor.InstallRequested) ConfirmUrgentInstall(editor.Result);
                    }
                }
            }
        } catch (Exception ex) { if (!IsDisposed) ShowError(ex); }
        finally { editingAttachment = false; }
    }

    void RemoveEntry(){
        var item=FocusedEntry();if(item==null||working||scanning)return;
        if(GeneralDialog.Message(this,T("Tirar da lista sem desinstalar?","Remove from the list without uninstalling?")+"\n"+item.Name,T("Tirar da lista","Remove from list"),MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
        var scope=PresetEditScope(item,true);if(!scope.HasValue)return;
        BeginProjectEdit();GeneralPresetProfiles.Remove(state,item.Id,scope.Value);marked.Remove(item.Id);FinishProjectEdit();DrawRows();
    }
    void Busy(bool value){if(primaryAction is GeneralButton)((GeneralButton)primaryAction).ShowActivity=value&&(loadingPreset||working);foreach(var control in idle)control.Enabled=!value;if(totalProgress!=null)totalProgress.Enabled=!value;UpdateOperationButton();UpdatePresetCommands();RefreshPrimaryAction();}
    void UpdateOperationButton(){
        if(next==null)return;
        next.Text=loadingPreset?T("Cancelar troca","Cancel switch"):T("Pular mod","Skip mod");
        next.Visible=loadingPreset?presetCancellation!=null:working||scanning;
        next.Enabled=loadingPreset?presetCancellation!=null&&!presetCancellation.IsCancellationRequested:working||scanning;
    }
    internal void Preview(string path){
        ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;Location=new Point(-30000,-30000);Show();Application.DoEvents();PerformLayout();
        using(var bmp=new Bitmap(Width,Height)){DrawToBitmap(bmp,new Rectangle(0,0,Width,Height));bmp.Save(path,System.Drawing.Imaging.ImageFormat.Png);}Close();
    }
}

