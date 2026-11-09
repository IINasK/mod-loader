using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

public sealed partial class OnlineWindow:Form {
    readonly FixedCatalog catalog;
    readonly DownloadCache cache;
    readonly NexusClient nexus=new NexusClient();
    readonly ModImporter importer;
    readonly Func<string,FixedInstaller> createDensityInstaller;
    readonly Func<string,FixedInstaller> createRemovalInstaller;
    readonly Func<string,FixedInstaller> createInstaller;
    readonly Func<string,bool> confirmRemoval=text=>MessageBox.Show(text,"Remover mods selecionados",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)==DialogResult.Yes;
    readonly Func<string,bool> confirmInstallation=text=>MessageBox.Show(text,"Confirmar instalação",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes;
    readonly Func<List<string>> findGames=Discovery.Find;
    readonly TextBox game=new TextBox();
    readonly DataGridView grid=new DataGridView();
    readonly Label status=new Label();
    readonly Label summary=new Label{AutoSize=true,Padding=new Padding(12,11,0,0)};
    readonly Label installGuide=new Label{Dock=DockStyle.Top,AutoSize=false,Height=106,Margin=new Padding(0,10,0,5),ForeColor=Color.DimGray};
    readonly CheckBox selectAllHeader=new CheckBox{Size=new Size(16,16),AccessibleName="Selecionar todos os mods visíveis",TabStop=true};
    readonly ProgressBar activity=new ProgressBar();
    readonly Label downloadFolderLabel=new Label();
    readonly Timer downloadWatchTimer=new Timer{Interval=2000};
    readonly OnlineBrowserSession browser;
    readonly Action<string> launchBrowser=url=>{Process.Start(BrowserRequest(url));};
    OnlineDownloadWatch downloadWatch;
    BrowserTab waitingTab;
    FixedMod waitingMod;
    FixedAsset waitingAsset;
    string watchedGame,downloadFolder;
    int watchedGeneration;
    bool pollingDownload,continueWatchedQueue;
    bool waitingPageSent;
    DateTime watchedSinceUtc;
    readonly List<Control> actions=new List<Control>();
    readonly Dictionary<string,DownloadControl> downloads=new Dictionary<string,DownloadControl>();
    readonly Dictionary<string,ModImportItem> pending=new Dictionary<string,ModImportItem>();
    readonly Dictionary<string,string> browserNotes=new Dictionary<string,string>();
    readonly ContextMenuStrip menu=new ContextMenuStrip();
    readonly ToolStripMenuItem audit=new ToolStripMenuItem("Auditar mod");
    readonly ToolStripMenuItem official=new ToolStripMenuItem("Abrir download oficial"),download=new ToolStripMenuItem("Baixar"),install=new ToolStripMenuItem("Instalar"),deactivate=new ToolStripMenuItem("Desativar"),pause=new ToolStripMenuItem("Pausar download"),cancel=new ToolStripMenuItem("Cancelar download"),undo=new ToolStripMenuItem("Desfazer última operação…");
    readonly bool preview;
    bool busy,automaticFlow;
    volatile bool downloadPhase;
    readonly HashSet<string> completedDownloads=new HashSet<string>();
    readonly HashSet<string> automaticQueue=new HashSet<string>(StringComparer.Ordinal);
    string automaticGame;
    volatile int automaticGeneration;
    string operationGame;
    List<string> operationIds;
    string pendingDensityId,pendingDensityRoot;
    int pendingDensityGeneration;
    bool densityDialogOpen;

    public OnlineWindow(FixedCatalog c,DownloadCache store,bool isPreview=false) {
        catalog=c;cache=store;preview=isPreview;
        if(preview)launchBrowser=url=>{throw new IOException("Teste isolado: abertura real do navegador bloqueada. Injete uma simulação explícita.");};
        createDensityInstaller=root=>new FixedInstaller(catalog,cache,root,Discovery.Saves());
        createRemovalInstaller=root=>new FixedInstaller(catalog,cache,root,null);
        createInstaller=root=>new FixedInstaller(catalog,cache,root,Discovery.Saves());
        importer=new ModImporter(c,store,Path.Combine(Path.GetDirectoryName(store.Root),"importados-pendentes.json"));
        browser=new OnlineBrowserSession(Path.Combine(Path.GetDirectoryName(store.Root),"Navegador"));
        downloadFolder=LoadDownloadFolder();
        Text="";Font=new Font("Segoe UI",10);BackColor=Color.White;
        ClientSize=new Size(1180,740);MinimumSize=new Size(1040,620);StartPosition=FormStartPosition.CenterScreen;AutoScaleMode=AutoScaleMode.Font;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=1,RowCount=10};Controls.Add(layout);
        for(int i=0;i<10;i++)layout.RowStyles.Add(new RowStyle(i==6?SizeType.Percent:SizeType.AutoSize,i==6?100:0));
        layout.Controls.Add(new Label{Text="Cyberpunk Coop · Mods",Font=new Font("Segoe UI",22,FontStyle.Bold),AutoSize=true,Margin=new Padding(0,0,0,4)},0,0);
        layout.Controls.Add(new Label{Text="Criado por Nask e Codex. Se achou ruim, faz você.",AutoSize=true,Margin=new Padding(0,0,0,12)},0,1);
        var pathRow=new TableLayoutPanel{ColumnCount=2,Dock=DockStyle.Top,AutoSize=true};
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,150));
        game.Dock=DockStyle.Fill;game.AutoSize=false;game.MinimumSize=new Size(0,35);game.Margin=new Padding(0,3,8,3);game.AccessibleName="Pasta raiz do Cyberpunk 2077";pathRow.Controls.Add(game,0,0);
        game.TextChanged+=(s,e)=>{OnlineDiagnostics.Record("ui.game.changed",null,"Caminho do jogo alterado na interface.",new{Game=game.Text});if(automaticQueue.Count>0||pendingDensityId!=null||downloadWatch!=null&&downloadWatch.IsArmed||facialQueue!=null&&facialQueue.IsArmed){ClearAutomaticQueue();status.Text="A pasta do jogo foi alterada. A fila de downloads, instalação ou densidade foi cancelada; autorize a seleção novamente.";}AutoDetectEdition();};
        var chooseGameButton=Button("Escolher jogo…",ChooseGame);chooseGameButton.AutoSize=false;chooseGameButton.Dock=DockStyle.Fill;pathRow.Controls.Add(chooseGameButton,1,0);layout.Controls.Add(pathRow,0,2);
        var tools=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,WrapContents=true,Margin=new Padding(0,7,0,9)};
        AddEditionControls(tools);
        tools.Controls.Add(Button("Densidade de NPCs…",ChooseDensityProfile));
        var facialButton=Button("Conjunto facial…",ChooseFacialDownloads);facialButton.Name="FacialDownloads";tools.Controls.Add(facialButton);
        tools.Controls.Add(Button("Abrir registros",OpenDiagnosticFolder));
        tools.Controls.Add(summary);
        tools.SetFlowBreak(summary,true);tools.Controls.Add(editionNotice);
        layout.Controls.Add(tools,0,3);
        var folders=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,Margin=new Padding(0,0,0,7)};
        folders.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));folders.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        downloadFolderLabel.Text="Downloads monitorados: "+downloadFolder;downloadFolderLabel.AutoEllipsis=true;downloadFolderLabel.Dock=DockStyle.Fill;downloadFolderLabel.TextAlign=ContentAlignment.MiddleLeft;
        folders.Controls.Add(downloadFolderLabel,0,0);folders.Controls.Add(Button("Pasta de downloads…",ChooseDownloadFolder),1,0);layout.Controls.Add(folders,0,4);
        var filters=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,WrapContents=false,Margin=new Padding(0,0,0,7)};
        AddCategoryControls(filters);layout.Controls.Add(filters,0,5);

        grid.Dock=DockStyle.Fill;grid.AllowUserToAddRows=false;grid.AllowUserToDeleteRows=false;grid.RowHeadersVisible=false;
        grid.BackgroundColor=Color.White;grid.BorderStyle=BorderStyle.FixedSingle;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;grid.MultiSelect=false;
        grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.None;grid.RowTemplate.Height=37;grid.ColumnHeadersHeight=34;
        grid.Columns.Add(new DataGridViewCheckBoxColumn{Name="Select",HeaderText="",Width=42});
        foreach(var p in new[]{new[]{"Mod","Mod","205"},new[]{"Version","Versão fixa","82"},new[]{"Installed","Instalação","132"},new[]{"State","Download","125"}})
            grid.Columns.Add(new DataGridViewTextBoxColumn{Name=p[0],HeaderText=p[1],Width=Int32.Parse(p[2]),ReadOnly=true});
        grid.Columns.Add(new DataGridViewColumn(new DownloadBarCell()){Name="Progress",HeaderText="Progresso",Width=132,ReadOnly=true});
        grid.Columns.Add(new DataGridViewButtonColumn{Name="Pause",HeaderText="",Width=82,ReadOnly=true,FlatStyle=FlatStyle.Flat,Visible=false});
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="TPP",HeaderText="Pacote coop",Width=157,ReadOnly=true,Visible=false});
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Notes",HeaderText="Observações",ReadOnly=true,Visible=false});
        grid.Columns["Mod"].AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill;grid.Columns["Mod"].MinimumWidth=205;
        foreach(var m in catalog.Packages.Where(m=>!IsBlocked(m))){
            rowCategories.Add(m.Id,ModCategories.CategoryOf(m.Id,m.Profile));
            int row=grid.Rows.Add(false,m.Name,m.Version,"A verificar","A verificar",0,"—",m.ThirdPerson??"—",m.Warning);
            var r=grid.Rows[row];r.Tag=m.Id;r.Cells["Mod"].ToolTipText=m.Id;r.Cells["TPP"].ToolTipText=m.ThirdPersonNotes;r.Cells["Notes"].ToolTipText=m.Warning;
            MarkBlocked(r,m);
        }
        AddFacialRows();
        grid.Controls.Add(selectAllHeader);actions.Add(selectAllHeader);selectAllHeader.BringToFront();
        selectAllHeader.Click+=(s,e)=>SelectAll();
        grid.SizeChanged+=(s,e)=>PositionSelectAll();grid.Scroll+=(s,e)=>PositionSelectAll();grid.ColumnWidthChanged+=(s,e)=>PositionSelectAll();grid.ColumnHeadersHeightChanged+=(s,e)=>PositionSelectAll();
        grid.CurrentCellDirtyStateChanged+=(s,e)=>{if(grid.IsCurrentCellDirty&&grid.CurrentCell!=null&&grid.CurrentCell.ColumnIndex==grid.Columns["Select"].Index)grid.CommitEdit(DataGridViewDataErrorContexts.Commit);};
        grid.CellValueChanged+=(s,e)=>{if(e.ColumnIndex==grid.Columns["Select"].Index)UpdateSelectAll();UpdateSummary();if(e.RowIndex>=0&&e.ColumnIndex>=0){var name=grid.Columns[e.ColumnIndex].Name;if(name=="Installed"||name=="State"||name=="Select")OnlineDiagnostics.Record(name=="Select"?"ui.selection.changed":"ui.row.changed",Convert.ToString(grid.Rows[e.RowIndex].Tag),"Valor exibido na lista mudou.",new{Game=game.Text,Field=name,Value=Convert.ToString(grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value),Queue=automaticQueue.ToArray()});}};
        grid.RowsAdded+=(s,e)=>{UpdateSelectAll();UpdateSummary();};grid.RowsRemoved+=(s,e)=>{UpdateSelectAll();UpdateSummary();};
        editionReady=true;PositionSelectAll();UpdateSelectAll();UpdateSummary();
        official.Click+=async(s,e)=>await OpenOfficial(CurrentMod());
        audit.Click+=(s,e)=>AuditMods(CurrentIds(),CurrentPending());
        download.Click+=async(s,e)=>await DownloadSelection(CurrentIds());
        install.Click+=(s,e)=>{var item=CurrentPending();if(item!=null)ShowImportedInstall(item);else Install(CurrentIds(),false);};
        deactivate.Click+=(s,e)=>DeactivateMod(CurrentMod());
        pause.Click+=(s,e)=>TogglePause(CurrentMod());cancel.Click+=(s,e)=>CancelDownload(CurrentMod());undo.Click+=(s,e)=>Undo();
        menu.Items.AddRange(new ToolStripItem[]{official,audit,download,install,deactivate,new ToolStripSeparator(),pause,cancel,new ToolStripSeparator(),undo});
        var licenses=new ToolStripMenuItem("Sobre e licenças…");licenses.Click+=(s,e)=>{using(var dialog=new ArchiveLicensesWindow())dialog.ShowDialog(this);};menu.Items.Add(new ToolStripSeparator());menu.Items.Add(licenses);
        menu.Opening+=(s,e)=>{
            var m=CurrentMod();var p=CurrentPending();var facial=CurrentFacial();DownloadControl control;bool active=downloadPhase&&m!=null&&!completedDownloads.Contains(m.Id)&&downloads.TryGetValue(m.Id,out control);
            official.Enabled=!busy&&!automaticFlow&&(m!=null||facial!=null||p!=null&&IsNexusUrl(p.Source));
            audit.Enabled=!busy&&!automaticFlow&&(m!=null||p!=null);
            download.Enabled=!busy&&!automaticFlow&&(m!=null&&IsSelectable(m.Id)||facial!=null&&IsSelectable(facial.Id));install.Enabled=p!=null||!busy&&!automaticFlow&&m!=null&&IsSelectable(m.Id);deactivate.Enabled=!busy&&!automaticFlow&&m!=null&&!m.Immutable;
            pause.Enabled=active;cancel.Enabled=facialQueue!=null&&facialQueue.IsArmed||active||downloadWatch!=null&&downloadWatch.IsArmed||!busy&&!automaticFlow&&(automaticQueue.Count>0||pendingDensityId!=null);
            pause.Text=active&&downloads[m.Id].IsPaused?"Continuar download":"Pausar download";
            cancel.Text=facialQueue!=null&&facialQueue.IsArmed?"Cancelar fila de downloads":active?"Cancelar download e instalação na fila":"Cancelar instalação na fila";
            undo.Enabled=!busy&&!automaticFlow;
        };
        grid.ContextMenuStrip=menu;
        grid.CellMouseDown+=(s,e)=>{if(e.Button==MouseButtons.Right&&e.RowIndex>=0){grid.ClearSelection();grid.Rows[e.RowIndex].Selected=true;grid.CurrentCell=grid.Rows[e.RowIndex].Cells["Mod"];}};
        grid.CellBeginEdit+=(s,e)=>{if(busy||automaticFlow)e.Cancel=true;};
        grid.CellContentClick+=(s,e)=>{if(e.RowIndex>=0&&e.ColumnIndex==grid.Columns["Pause"].Index)TogglePause(ModAt(e.RowIndex));};
        grid.CellDoubleClick+=(s,e)=>{if(e.RowIndex>=0&&e.ColumnIndex!=grid.Columns["Pause"].Index&&e.ColumnIndex!=grid.Columns["Select"].Index)ShowDetails(e.RowIndex);};
        layout.Controls.Add(grid,0,6);
        installGuide.Text=NormalDownloadGuide;layout.Controls.Add(installGuide,0,7);
        var bottom=new TableLayoutPanel{AutoSize=true,Dock=DockStyle.Top,RowCount=2,ColumnCount=1};
        activity.Dock=DockStyle.Top;activity.Height=5;activity.Style=ProgressBarStyle.Marquee;activity.Visible=false;bottom.Controls.Add(activity);
        status.AutoSize=false;status.Dock=DockStyle.Top;status.Height=40;status.Text="A lista é conferida ao abrir. Use Atualizar lista para verificar os mods novamente.";bottom.Controls.Add(status);layout.Controls.Add(bottom,0,8);
        var footer=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Margin=new Padding(0)};
        mainInstallButton=Button("Baixar e instalar",InstallSelection);footer.Controls.Add(mainInstallButton);
        skipCurrentModButton.Click+=async(s,e)=>await SkipCurrentDownload();footer.Controls.Add(skipCurrentModButton);
        footer.Controls.Add(Button("Atualizar lista",async()=>{await Run(()=>RefreshAll());await RefreshFacialInventory();}));layout.Controls.Add(footer,0,9);
        var remove=Button("Remover selecionados",async()=>await RemoveSelected());remove.BackColor=Color.Firebrick;remove.ForeColor=Color.White;remove.FlatStyle=FlatStyle.Flat;remove.UseVisualStyleBackColor=false;footer.Controls.Add(remove);
        footer.Controls.Add(Button("Limpar mods fora da lista",async()=>await CleanExtras()));
        downloadWatchTimer.Tick+=async(s,e)=>{await ProcessPriorityImports(false);await FlushDeferredDrops(false);await CheckFacialDownload();await CheckWatchedDownload();};
        if(!preview)downloadWatchTimer.Start();
        EnableDrops(this);
        FormClosing+=(s,e)=>{if(busy||automaticFlow){e.Cancel=true;status.Text="Há uma operação em andamento. Cancele o download pelo menu da linha ou aguarde a conclusão.";}else nexus.Disconnect();};
        FormClosed+=(s,e)=>{StopDownloadWatch();downloadWatchTimer.Dispose();browser.Dispose();};
        if(!preview)Shown+=async(s,e)=>{await InitializeGame();await RefreshFacialInventory();};
    }

    async Task InitializeGame(){
        List<string> found=null;
        if(!await Run(()=>{found=findGames();return "Procurando o jogo para conferir os mods…";}))return;
        if(found.Count>0)game.Text=found[0];
        if(String.IsNullOrWhiteSpace(game.Text)){LoadPending();status.Text="Escolha a pasta do jogo e clique em Atualizar lista para conferir os mods.";return;}
        await Run(()=>RefreshAll());
    }

    Button Button(string title,Action action){var b=new Button{Text=title,AutoSize=true,MinimumSize=new Size(138,35),Margin=new Padding(0,3,8,3)};b.Click+=(s,e)=>{if(!automaticFlow)action();};actions.Add(b);return b;}
    void OpenDiagnosticFolder(){try{string file=OnlineDiagnostics.FilePath;if(file==null)throw new IOException(OnlineDiagnostics.Issue??"Os registros são criados ao abrir o programa normalmente.");Process.Start(new ProcessStartInfo(Path.GetDirectoryName(file)){UseShellExecute=true});}catch(Exception ex){status.Text=Friendly(ex);}}
    string DownloadFolderSettings(){return Path.Combine(Path.GetDirectoryName(cache.Root),"pasta-downloads.json");}
    string LoadDownloadFolder(){
        try{string file=DownloadFolderSettings();Safe.NoLinks(file);if(File.Exists(file)&&new FileInfo(file).Length<=8192){string folder=Safe.Json.Deserialize<string>(File.ReadAllText(file));return new OnlineDownloadWatch(folder).Folder;}}
        catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}
        return OnlineDownloadWatch.DefaultFolder();
    }
    async void ChooseDownloadFolder(){
        if(busy||automaticFlow)return;
        using(var d=new FolderBrowserDialog{Description="Pasta onde o navegador salva os ZIP/RAR/7z. O app observa somente o arquivo esperado da seleção autorizada.",SelectedPath=downloadFolder,ShowNewFolderButton=true}){
            if(d.ShowDialog(this)!=DialogResult.OK)return;
            try{
                string folder=new OnlineDownloadWatch(d.SelectedPath).Folder;
                string root=GameRoot().TrimEnd('\\');
                if(folder.Equals(root,StringComparison.OrdinalIgnoreCase)||folder.StartsWith(root+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("Escolha uma pasta de downloads fora da pasta do jogo.");
                string file=DownloadFolderSettings();Safe.NoLinks(file);Directory.CreateDirectory(Path.GetDirectoryName(file));Safe.WriteJson(file,folder);
                StopDownloadWatch();downloadFolder=folder;downloadFolderLabel.Text="Downloads monitorados: "+folder;
                if(pendingDensityId!=null)await ContinueDensityProfile();
                else if(automaticQueue.Count>0)await ProcessAutomaticQueue(root);
                else status.Text="Pasta de downloads definida. Salve os arquivos do navegador nesta pasta; a espera começa ao baixar uma seleção autorizada.";
            }catch(Exception ex){status.Text=Friendly(ex);}
        }
    }
    void StopDownloadWatch(){CancelFacialQueue();if(downloadWatch!=null)downloadWatch.Cancel();downloadWatch=null;waitingTab=null;waitingMod=null;waitingAsset=null;watchedGame=null;waitingPageSent=false;watchedSinceUtc=DateTime.MinValue;UpdateDownloadSkipButton();}
    void BrowserOutcome(FixedMod mod,string stage,string message){
        if(mod!=null){browserNotes[mod.Id]=message;var row=Row(mod.Id);if(row!=null)ApplyBrowserNote(row,mod.Warning);}
        try{
            string folder=Path.Combine(Path.GetDirectoryName(cache.Root),"Logs");Safe.NoLinks(folder);Directory.CreateDirectory(folder);
            string file=Path.Combine(folder,"navegador-"+DateTime.UtcNow.ToString("yyyyMMdd")+".jsonl");Safe.NoLinks(file);
            if(!File.Exists(file)||new FileInfo(file).Length<1048576)File.AppendAllText(file,Safe.Json.Serialize(new{Utc=DateTime.UtcNow.ToString("o"),Mod=mod==null?null:mod.Id,Stage=stage,Message=message})+Environment.NewLine);
        }catch(IOException){}catch(UnauthorizedAccessException){}
    }
    void ApplyBrowserNote(DataGridViewRow row,string note){string extra;if(browserNotes.TryGetValue((string)row.Tag,out extra))note=(String.IsNullOrWhiteSpace(note)?"":note+" · ")+extra;row.Cells["Notes"].Value=note;row.Cells["Notes"].ToolTipText=note;}
    async Task CloseDownloadTab(FixedMod mod,BrowserTab tab){
        if(tab==null){BrowserOutcome(mod,"close-skipped","Guia mantida no navegador normal: a importação continua automaticamente, sem controlar as suas abas.");return;}
        bool closed=false;
        bool checkedOk=await Run(()=>{closed=browser.Close(tab);return closed?"Arquivo importado; aba correspondente fechada.":browser.LastCloseReason;});
        BrowserOutcome(mod,closed?"closed":"close-not-confirmed",checkedOk?(closed?"Aba do download fechada após importar.":browser.LastCloseReason):"Não foi possível confirmar o fechamento; aba preservada. Consulte o log de navegador do aplicativo.");
    }
    bool StartDownloadWatch(FixedMod mod,FixedAsset asset,string root,bool resumeQueue){
        StopDownloadWatch();
        try{
            string folder=Path.GetFullPath(downloadFolder).TrimEnd('\\'),gamePath=Path.GetFullPath(root).TrimEnd('\\');
            if(folder.Equals(gamePath,StringComparison.OrdinalIgnoreCase)||folder.StartsWith(gamePath+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("A pasta monitorada precisa ficar fora do jogo.");
            var watch=new OnlineDownloadWatch(folder);watch.Arm(new[]{asset});watch.Poll();downloadWatch=watch;waitingMod=mod;waitingAsset=asset;watchedGame=root;watchedGeneration=automaticGeneration;continueWatchedQueue=resumeQueue;watchedSinceUtc=DateTime.UtcNow;waitingPageSent=false;OnlineDiagnostics.Record("watch.armed",mod.Id,"Monitor do download autorizado iniciado.",new{Game=root,Folder=folder,Asset=asset.Id,Sha256=asset.Sha256,Size=asset.Size,ResumeInstall=resumeQueue,Queue=automaticQueue.ToArray(),Generation=automaticGeneration});UpdateDownloadSkipButton();return true;
        }catch(Exception ex){status.Text="Monitor indisponível: "+Friendly(ex)+" Use Pasta de downloads ou importe o arquivo original.";return false;}
    }
    async Task EnsureWatchedPage(){
        var watch=downloadWatch;var mod=waitingMod;var asset=waitingAsset;int generation=watchedGeneration;
        if(watch==null||!watch.IsArmed||waitingPageSent||watch.HasPendingCandidates)return;
        if(BrowserDownloadProgress.Read(downloadFolder,asset,watchedSinceUtc).Active)return;
        waitingPageSent=true;var tab=await OpenOfficial(mod,asset);
        if(downloadWatch==watch&&generation==automaticGeneration)waitingTab=tab;
    }
    void ShowBrowserProgress(string id,BrowserDownloadSnapshot progress){
        if(progress==null||!progress.Active)return;var row=Row(id);if(row==null)return;
        row.Cells["State"].Value="Baixando";row.Cells["Progress"].Value=progress.Percent.HasValue?(object)("~"+progress.Percent.Value):"—";
        row.Cells["State"].ToolTipText="Progresso aproximado pelo arquivo em gravação: "+(progress.ReceivedBytes/1024.0/1024).ToString("0.0")+" MB. 100% somente após conferir o arquivo completo.";
        status.Text="Baixando "+Convert.ToString(row.Cells["Mod"].Value)+" · "+(progress.Percent.HasValue?"aproximadamente "+progress.Percent.Value+"%":(progress.ReceivedBytes/1024.0/1024).ToString("0.0")+" MB observados");
    }
    async Task CheckWatchedDownload(){
        if(pollingDownload||busy||automaticFlow||densityDialogOpen||downloadWatch==null||!downloadWatch.IsArmed)return;
        pollingDownload=true;UpdateDownloadSkipButton();
        var watch=downloadWatch;var mod=waitingMod;var asset=waitingAsset;DateTime since=watchedSinceUtc;var tab=waitingTab;string root=watchedGame;int generation=watchedGeneration;bool resumeQueue=continueWatchedQueue;
        try{
            DownloadWatchMatch match=null;BrowserDownloadSnapshot progress=null;
            bool polled=await Run(()=>{match=watch.Poll((bytes,total)=>{SetStatus(mod.Id,"Conferindo arquivo",total==0?0:Math.Min(99,(int)(bytes*100.0/total)));Report(mod.Name+" · conferindo download concluído · "+bytes/1024/1024+" / "+total/1024/1024+" MB");});if(match==null)progress=BrowserDownloadProgress.Read(downloadFolder,asset,since);return "Aguardando "+mod.Name+" na pasta "+downloadFolder+". No Nexus, clique em Manual / Slow download.";});
            if(!polled){StopDownloadWatch();return;}
            if(downloadWatch!=watch||generation!=automaticGeneration)return;
            if(match==null){var row=Row(mod.Id);if(row!=null&&(progress==null||!progress.Active)){row.Cells["State"].Value=watch.HasPendingCandidates?"Conferindo Downloads":"Aguardando download";row.Cells["Progress"].Value="—";}ShowBrowserProgress(mod.Id,progress);await EnsureWatchedPage();return;}
            OnlineDiagnostics.Record("watch.match",mod.Id,"Arquivo estável corresponde ao download esperado.",new{Source=match.Path,Asset=match.Asset.Id,Sha256=match.Asset.Sha256,ResumeInstall=resumeQueue,Generation=generation});
            bool imported=await Run(()=>{var result=importer.Import(match.Path);if(!result.SelectedIds.Contains(mod.Id))throw new IOException("O arquivo concluído não corresponde ao mod aguardado.");return "Arquivo conferido e importado automaticamente: "+mod.Name;});
            if(!imported){StopDownloadWatch();return;}
            if(downloadWatch!=watch||generation!=automaticGeneration)return;
            StopDownloadWatch();SetStatus(mod.Id,"Pronto para instalar",100);
            await CloseDownloadTab(mod,tab);
            if(generation!=automaticGeneration)return;
            if(pendingDensityId!=null&&pendingDensityGeneration==generation&&DensityMod()!=null&&mod.Id==DensityMod().Id&&String.Equals(pendingDensityRoot,root,StringComparison.OrdinalIgnoreCase))await ContinueDensityProfile();
            else if(resumeQueue&&automaticQueue.Count>0)await ProcessAutomaticQueue(root);
            else status.Text="Download importado e conferido. Use Instalar para aplicar; nenhum arquivo foi instalado por esta ação de baixar.";
        }catch(Exception ex){StopDownloadWatch();status.Text=Friendly(ex)+" Arraste o arquivo baixado para continuar manualmente.";}
        finally{pollingDownload=false;UpdateDownloadSkipButton();}
    }
    void ChooseGame(){using(var d=new FolderBrowserDialog{Description="Pasta Cyberpunk 2077 que contém bin e archive",ShowNewFolderButton=false})if(d.ShowDialog(this)==DialogResult.OK)game.Text=d.SelectedPath;}
    void ChooseFacialDownloads(){
        if(busy||automaticFlow)return;
        if(automaticQueue.Count>0||pendingDensityId!=null||downloadWatch!=null&&downloadWatch.IsArmed){status.Text="Conclua ou cancele o download atual antes de abrir o conjunto facial.";return;}
        SelectFacialRows();
    }
    FixedMod DensityMod(){return catalog.Packages.FirstOrDefault(m=>m.DensityProfiles!=null&&m.DensityProfiles.Count>0);}
    string DensityNote(FixedMod mod,string root,string note){
        if(mod.DensityProfiles==null||mod.DensityProfiles.Count==0)return note;
        try{var current=NightCityDensity.Read(mod,root);return "Night City Alive "+(current.Version??"(versão não identificada)")+" · Densidade atual: "+current.Name+(String.IsNullOrWhiteSpace(note)?"":" · "+note);}
        catch(IOException ex){return "Densidade não verificada: "+ex.Message;}
    }
    async void ChooseDensityProfile(){
        if(busy||automaticFlow||pollingDownload||densityDialogOpen)return;
        string selected=null,root=null;densityDialogOpen=true;UpdateDownloadSkipButton();
        try{
            var mod=DensityMod();if(mod==null)throw new IOException("Este catálogo ainda não possui os perfis de densidade do Night City Alive.");
            if(!EditionGuard(new[]{mod.Id}))return;
            root=GameRoot();DensityProfileState current=null;
            bool read=await Run(()=>{current=NightCityDensity.Read(mod,root);return "Perfil de densidade lido. Escolha o ajuste e clique em Aplicar e instalar.";});
            if(!read)return;
            using(var f=new Form{Text="Densidade de NPCs · Night City Alive",ClientSize=new Size(740,325),StartPosition=FormStartPosition.CenterParent,Font=Font,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false}){
                var actual=new Label{Text="Versão: "+(current.Version??(current.Id=="absent"?"não instalado":"não identificada"))+" · Perfil atual: "+current.Name,Location=new Point(18,16),Size=new Size(704,30),Font=new Font(Font,FontStyle.Bold)};
                var detail=new Label{Text=current.Detail,Location=new Point(18,49),Size=new Size(704,55)};
                var label=new Label{Text="Novo perfil",Location=new Point(18,112),AutoSize=true};
                var choice=new ComboBox{Location=new Point(18,137),Width=345,DropDownStyle=ComboBoxStyle.DropDownList,AccessibleName="Densidade de NPCs"};
                choice.Items.AddRange(new object[]{"Baixa (recomendada)","Média","Alta"});
                string[] ids={"low","medium","high"};string preferred=pendingDensityId??current.Id;
                int index=Array.IndexOf(ids,preferred);choice.SelectedIndex=index<0?0:index;
                var explanation=new Label{Text="Baixa é a densidade recomendada.\nEste controle ajusta apenas o Night City Alive; Cyberpsycho e Dark Future ficam preservados.\nUse a mesma densidade nos dois computadores.",Location=new Point(18,180),Size=new Size(704,63)};
                var close=new Button{Text="Fechar",Location=new Point(380,267),Size=new Size(148,35),DialogResult=DialogResult.Cancel};
                var apply=new Button{Text="Aplicar e instalar",Location=new Point(540,267),Size=new Size(182,35),DialogResult=DialogResult.OK,Enabled=current.CanApply};
                f.Controls.AddRange(new Control[]{actual,detail,label,choice,explanation,close,apply});f.AcceptButton=apply;f.CancelButton=close;
                if(f.ShowDialog(this)==DialogResult.OK)selected=ids[choice.SelectedIndex];
            }
        }catch(Exception ex){status.Text=Friendly(ex);}
        finally{densityDialogOpen=false;UpdateDownloadSkipButton();}
        if(selected==null)return;
        ClearAutomaticQueue();pendingDensityId=selected;pendingDensityRoot=root;pendingDensityGeneration=automaticGeneration;
        await ContinueDensityProfile();
    }
    async Task ContinueDensityProfile(){
        if(busy||automaticFlow||pendingDensityId==null)return;
        string id=pendingDensityId,root=pendingDensityRoot;int generation=pendingDensityGeneration;automaticFlow=true;UpdateDownloadSkipButton();
        try{
            if(generation!=automaticGeneration||!String.Equals(GameRoot(),root,StringComparison.OrdinalIgnoreCase))throw new IOException("A troca de densidade foi cancelada porque a seleção ou a pasta do jogo mudou.");
            FixedMod target=null;FixedAsset manual=null;bool needsFiles=true;
            bool checkedOk=await Run(()=>{
                var mod=DensityMod();if(mod==null)throw new IOException("Perfis de densidade indisponíveis neste catálogo.");
                var current=NightCityDensity.Read(mod,root);if(!current.CanApply)throw new IOException(current.Detail);
                foreach(var dependency in FixedData.Expand(catalog,mod.Dependencies))if(!FixedData.Scan(dependency,root).IsCorrect)throw new IOException("Instale primeiro a base e os requisitos pelo botão Baixar e instalar: "+dependency.Name);
                target=NightCityDensity.Resolve(mod,id);FixedData.ValidateGame(root,new[]{target},catalog.GameFileVersion);needsFiles=current.Id!=id;
                if(needsFiles&&!nexus.Premium)manual=target.Assets.FirstOrDefault(a=>a.ModId>0&&!cache.Contains(a));
                return "Perfil "+target.Name+" conferido. Preparando a aplicação.";
            });
            if(!checkedOk){ClearAutomaticQueue();return;}
            if(generation!=automaticGeneration)return;
            if(manual!=null){
                bool watching=StartDownloadWatch(target,manual,root,false);if(watching)await EnsureWatchedPage();else await OpenOfficial(target,manual);
                if(generation!=automaticGeneration)return;
                status.Text=watching?"Aguardando o arquivo do perfil escolhido em "+downloadFolder+". No Nexus, clique em Manual / Slow download; o app confere e aplica a densidade automaticamente.":"Baixe o perfil pelo link aberto e importe o arquivo original para continuar. A troca de densidade permanece autorizada.";
                return;
            }
            if(needsFiles)PrepareDownloads(new[]{target});
            bool applied;
            try{applied=await Run(()=>{
                if(needsFiles)DownloadMods(new List<FixedMod>{target},root,false);
                if(generation!=automaticGeneration)throw new OperationCanceledException("Troca de densidade cancelada antes de alterar os arquivos.");
                string result=createDensityInstaller(root).ApplyDensityProfile(id,Report);
                RefreshMods(new[]{DensityMod()},root);return result;
            });}finally{if(needsFiles)EndDownloads();}
            ClearAutomaticQueue();if(!applied)return;
        }catch(Exception ex){ClearAutomaticQueue();status.Text=Friendly(ex);}
        finally{automaticFlow=false;foreach(var a in actions)a.Enabled=true;game.Enabled=true;activity.Visible=false;UpdateDownloadSkipButton();}
    }
    FixedMod ModAt(int row){return row<0?null:catalog.Packages.FirstOrDefault(x=>x.Id==(string)grid.Rows[row].Tag);}
    FixedMod CurrentMod(){return grid.CurrentRow==null?null:ModAt(grid.CurrentRow.Index);}
    ModImportItem CurrentPending(){return grid.CurrentRow==null?null:PendingAt(grid.CurrentRow.Index);}
    List<string> CurrentIds(){var m=CurrentMod();var facial=CurrentFacial();return m!=null?new List<string>{m.Id}:facial!=null?new List<string>{facial.Id}:new List<string>();}
    List<string> Chosen(){if(InvokeRequired)return operationIds.Where(IsSelectable).ToList();grid.EndEdit();return grid.Rows.Cast<DataGridViewRow>().Where(r=>r.Visible&&IsSelectable((string)r.Tag)&&Convert.ToBoolean(r.Cells["Select"].Value)).Select(r=>(string)r.Tag).ToList();}
    void SelectAll(){
        if(busy||automaticFlow)return;bool selected=selectAllHeader.Checked;grid.EndEdit();
                foreach(DataGridViewRow row in grid.Rows){
            var mod=ModAt(row.Index);
            if(selected&&(mod!=null&&mod.ManualSelection||PendingAt(row.Index)!=null))continue;
            row.Cells["Select"].Value=selected&&row.Visible&&IsSelectable((string)row.Tag);
        }
        UpdateSelectAll();
        var selectedIds=Chosen();status.Text=selectedIds.Any(id=>FacialById(id)!=null)?selectedIds.Count+" itens selecionados. Baixar selecionados inicia somente a fila dos arquivos Rosto. Os outros mods ficam preservados.":selectedIds.Count+" itens selecionados. Itens que exigem seleção manual ficam fora da seleção em massa. Nada foi instalado.";
    }
    void PositionSelectAll(){selectAllHeader.Location=new Point(1+(grid.Columns[0].Width-selectAllHeader.Width)/2-grid.HorizontalScrollingOffset,1+(grid.ColumnHeadersHeight-selectAllHeader.Height)/2);}
    void UpdateSelectAll(){
        var rows=grid.Rows.Cast<DataGridViewRow>().Where(r=>r.Visible&&IsSelectable((string)r.Tag)).Where(r=>{var mod=ModAt(r.Index);return mod==null||!mod.ManualSelection;}).ToList();
        int selected=rows.Count(r=>Convert.ToBoolean(r.Cells["Select"].Value));selectAllHeader.CheckState=selected==0?CheckState.Unchecked:selected==rows.Count?CheckState.Checked:CheckState.Indeterminate;
    }
    void UpdateSummary(){
        var rows=grid.Rows.Cast<DataGridViewRow>().Where(r=>r.Visible).ToList();int installed=rows.Count(r=>Convert.ToString(r.Cells["Installed"].Value)=="Instalado correto");int failed=rows.Count(r=>Convert.ToString(r.Cells["State"].Value)=="Falhou");
        int skipped=rows.Count(r=>Convert.ToString(r.Cells["State"].Value)=="Pulado");summary.Text="Mods na lista: "+rows.Count+"   |   Instalados: "+installed+"   |   Falhas: "+failed+(skipped==0?"":"   |   Pulados: "+skipped);
        if(mainInstallButton!=null){var selected=rows.Where(r=>Convert.ToBoolean(r.Cells["Select"].Value)&&IsSelectable((string)r.Tag)).ToList();bool hasFacial=selected.Any(r=>FacialById((string)r.Tag)!=null);bool localOnly=selected.Count==0&&rows.Any(r=>Convert.ToBoolean(r.Cells["Select"].Value)&&PendingAt(r.Index)!=null);mainInstallButton.Text=hasFacial?"Baixar selecionados":localOnly?"Instalar selecionados":"Baixar e instalar";installGuide.Text=hasFacial?FacialDownloadGuide:NormalDownloadGuide;}
    }
    string GameRoot(){string text=InvokeRequired?operationGame:game.Text.Trim();if(String.IsNullOrWhiteSpace(text))throw new IOException("Escolha a pasta do Cyberpunk 2077.");return Path.GetFullPath(text);}
    void Post(Action action){if(!IsDisposed&&IsHandleCreated)BeginInvoke(new Action(()=>{if(!IsDisposed)action();}));}
    void Report(string text){Post(()=>status.Text=text);}
    DataGridViewRow Row(string id){return grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r=>(string)r.Tag==id);}
    static bool IsBlocked(FixedMod mod){return mod!=null&&!String.IsNullOrWhiteSpace(mod.BlockedReason);}
    static void MarkBlocked(DataGridViewRow row,FixedMod mod){
        if(row==null||!IsBlocked(mod))return;
        string reason="Retido: "+mod.BlockedReason;
        row.Cells["Select"].Value=false;row.Cells["Select"].ReadOnly=true;
        row.Cells["Installed"].Value=row.Cells["State"].Value="Retido";
        row.Cells["Installed"].ToolTipText=row.Cells["State"].ToolTipText=reason;
        row.Cells["Notes"].Value=reason+(String.IsNullOrWhiteSpace(mod.Warning)?"":" · "+mod.Warning);
        row.Cells["Notes"].ToolTipText=Convert.ToString(row.Cells["Notes"].Value);
        row.Cells["Mod"].ToolTipText=mod.Id+"\n"+reason;
        row.DefaultCellStyle.BackColor=Color.FromArgb(255,246,226);row.Cells["State"].Style.ForeColor=Color.FromArgb(135,83,0);
    }
    void SetStatus(string id,string state,int percent){Post(()=>{var row=Row(id);if(row==null||skippedThisQueue.Contains(id))return;row.Cells["State"].Value=state;row.Cells["Progress"].Value=percent;row.Cells["State"].Style.ForeColor=state=="Falhou"?Color.Firebrick:Color.Black;if(state!="Falhou"&&state!="Cancelado")row.Cells["State"].ToolTipText="";var mod=catalog.Packages.FirstOrDefault(m=>m.Id==id);if(IsBlocked(mod)){MarkBlocked(row,mod);row.Cells["State"].ToolTipText+="\nDownload: "+state;}});}
    static string Friendly(Exception ex){return ex is UnauthorizedAccessException?"Sem permissão para acessar a pasta. Para instalar em Program Files, abra o programa como administrador pelo Windows.":ex.Message;}
    void ShowPreparationFailure(ModPreparationException failure){
        if(failure==null)return;
        var row=Row(failure.ModId);
        if(row!=null){row.Cells["State"].Value="Falhou";row.Cells["State"].Style.ForeColor=Color.Firebrick;row.Cells["State"].ToolTipText=failure.Message;if(row.Visible){grid.ClearSelection();row.Selected=true;grid.CurrentCell=row.Cells["Mod"];grid.FirstDisplayedScrollingRowIndex=row.Index;}}
        status.Text="Falhou: "+failure.ModName+". "+failure.InnerException.Message;
        UpdateSummary();
        OfferPreparationSkip(failure);
    }
    Func<FileApprovalRequest,bool> confirmFileMismatch=null;
    bool ApprovePreparationFailure(ModPreparationException failure){
        var request=failure==null?null:failure.InnerException as FileApprovalRequest;
        if(request==null||String.IsNullOrWhiteSpace(operationGame))return false;
        string destination=Safe.In(operationGame,request.Path);Safe.NoLinks(destination);
        if(File.Exists(destination)||Directory.Exists(destination))return false;
        string warning=failure.Message+"\r\n\r\nO tamanho ou conteúdo não corresponde ao catálogo. Isso pode causar falhas no jogo.\r\n\r\nInstalar mesmo assim? Esta aprovação vale apenas para este arquivo e este jogo. A instalação será registrada para remoção posterior pelo botão Remover selecionados.";
        bool accepted=confirmFileMismatch!=null?confirmFileMismatch(request):!preview&&MessageBox.Show(this,warning,"Arquivo diferente — instalar mesmo assim?",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)==DialogResult.Yes;
        if(!accepted)return false;
        UserFileApprovals.Approve(catalog,operationGame,request);
        OnlineDiagnostics.Record("stage.approval.accepted",request.ModId,"Usuário aprovou a identidade extraída diferente do catálogo.",new{Game=operationGame,Asset=request.AssetName,Entry=request.Entry,ExpectedSize=request.ExpectedSize,ActualSize=request.ActualSize,ActualSha256=request.ActualSha256});
        return true;
    }

    async Task<bool> Run(Func<string> work){
        if(busy)return false;operationGame=game.Text.Trim();operationIds=Chosen();busy=true;UpdateDownloadSkipButton();
        OnlineDiagnostics.Record("action.begin",null,"Tarefa do aplicativo iniciada.",new{Game=operationGame,Selected=operationIds,Queue=automaticQueue.ToArray()});
        foreach(var a in actions)a.Enabled=false;game.Enabled=false;activity.Visible=true;
        bool success=false;
        try{var approved=new HashSet<string>();while(true){try{status.Text=await Task.Run(work);success=true;OnlineDiagnostics.Record("action.complete",null,"Tarefa concluída.",new{Game=operationGame});break;}catch(Exception ex){var failure=ex as ModPreparationException;OnlineDiagnostics.Record("action.failed",failure==null?null:failure.ModId,"Tarefa falhou.",new{Game=operationGame,ErrorType=ex.GetType().Name,Error=Friendly(ex)});status.Text=Friendly(ex);string key=failure==null?null:failure.ModId+"/"+failure.Entry;if(key!=null&&!approved.Contains(key)&&ApprovePreparationFailure(failure)){approved.Add(key);continue;}ShowPreparationFailure(failure);break;}}}
        catch(Exception ex){status.Text=Friendly(ex);OnlineDiagnostics.Record("stage.approval.failed",null,"Não foi possível registrar a aprovação local.",new{Error=ex.Message});}
        finally{busy=false;foreach(var a in actions)a.Enabled=!automaticFlow;game.Enabled=!automaticFlow;activity.Visible=automaticFlow;UpdateDownloadSkipButton();}
        return success;
    }
    string RefreshAll(){
        string root=GameRoot();var visible=catalog.Packages.Where(m=>!IsBlocked(m)&&EditionEligible(m.Id)).ToList();RefreshMods(visible,root);LoadPending();
        try{FixedData.ValidateGame(root,visible,catalog.GameFileVersion);}
        catch(IOException ex){return ex.Message;}
        return "Jogo "+FixedData.GameVersion(root)+" · build "+FixedData.GameBuild(root)+". Conferência local concluída. Nada foi instalado.";
    }
    void RefreshMods(IEnumerable<FixedMod> mods,string root){
        foreach(var m in mods.Where(m=>!IsBlocked(m))){
            Report("Verificando "+m.Name);var scan=FixedData.Scan(m,root);bool cached=m.Assets.All(cache.Contains);string note=m.Warning;
            string localNote=DensityNote(m,root,note);string approvedWarning=UserFileApprovals.GetWarning(m,root);
            OnlineDiagnostics.Record("ui.scan.enqueue",m.Id,"Resultado de conferência enviado para a lista.",new{Game=root,State=scan.State,Correct=scan.Correct,Missing=scan.Missing,Different=scan.Different,Cached=cached});
            Post(()=>{var r=Row(m.Id);if(r==null||skippedThisQueue.Contains(m.Id))return;r.Cells["Installed"].Value=scan.State;r.Cells["Installed"].ToolTipText=scan.Detail;r.Cells["Installed"].ErrorText=approvedWarning??"";ApplyBrowserNote(r,localNote);if(!String.IsNullOrEmpty(approvedWarning))r.Cells["Notes"].Value=approvedWarning+"\r\n"+localNote;if(!downloads.ContainsKey(m.Id)){r.Cells["State"].Value=scan.IsCorrect?"Já instalado":cached?"Pronto para instalar":m.Id.StartsWith("face-",StringComparison.Ordinal)?"Não iniciado":"A baixar";r.Cells["Progress"].Value=cached||scan.IsCorrect?100:0;r.Cells["State"].Style.ForeColor=Color.Black;r.Cells["State"].ToolTipText="";}MarkBlocked(r,m);});
        }
    }

    static bool IsNexusUrl(string text){Uri uri;return Uri.TryCreate(text,UriKind.Absolute,out uri)&&uri.Scheme=="https"&&(uri.DnsSafeHost=="www.nexusmods.com"||uri.DnsSafeHost=="nexusmods.com");}
    internal static ProcessStartInfo BrowserRequest(string text){
        Uri uri;
        if(String.IsNullOrWhiteSpace(text)||text.Length>4096||text.Any(c=>Char.IsControl(c)||Char.IsWhiteSpace(c))||text.IndexOf('\\')>=0||!Uri.TryCreate(text,UriKind.Absolute,out uri)||uri.Scheme!="https"||uri.UserInfo!=""||!uri.IsDefaultPort||!new[]{"www.nexusmods.com","nexusmods.com","github.com","raw.githubusercontent.com","nightcoop.net"}.Contains(uri.DnsSafeHost))throw new IOException("Link oficial HTTPS inválido.");
        return new ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true,Verb="open"};
    }
    async Task<BrowserTab> OpenOfficial(FixedMod mod,FixedAsset requestedAsset=null){
        if(busy)return null;
        try{
            if(mod!=null&&requestedAsset==null&&mod.DensityProfiles!=null)mod=NightCityDensity.Default(mod);
            string url;
            if(mod==null){var facial=CurrentFacial();if(facial!=null){OpenFacialOfficial(facial);return null;}var p=CurrentPending();if(p==null||!IsNexusUrl(p.Source))return null;url=p.Source;}
            else {var a=requestedAsset??mod.Assets.FirstOrDefault(x=>x.ModId>0)??mod.Assets[0];url=a.ModId>0?"https://www.nexusmods.com/cyberpunk2077/mods/"+a.ModId+"?tab=files&file_id="+a.FileId:a.Url;}
            OnlineDiagnostics.Record("browser.open.request",mod==null?null:mod.Id,"Abrindo página oficial no navegador padrão.",new{HasInstallQueue=automaticQueue.Count>0,Watched=waitingMod==null?null:waitingMod.Id,ResumeInstall=continueWatchedQueue});
            bool opened=await Run(()=>{launchBrowser(url);OnlineDiagnostics.Record("browser.open.dispatched",mod==null?null:mod.Id,"URL oficial enviada ao Windows; não significa download nem instalação.");return "Link aberto no navegador padrão normal, usando a sessão do navegador. Clique em Manual / Slow download e salve na pasta monitorada.";});
            if(opened)BrowserOutcome(mod,"system-browser","Link aberto na sessão normal do navegador padrão; sem perfil separado ou controle de abas. A guia ficará aberta.");return null;
        }catch(Exception ex){status.Text=Friendly(ex);return null;}
    }

    void EnableDrops(Control control){
        control.AllowDrop=true;
        control.DragEnter+=(s,e)=>e.Effect=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;
        control.DragDrop+=(s,e)=>{
            if(e.Data.GetDataPresent(DataFormats.FileDrop)){ImportInputs((string[])e.Data.GetData(DataFormats.FileDrop));return;}
        };
        foreach(Control child in control.Controls)EnableDrops(child);
    }
    async void ImportInputs(string[] values){await ImportFiles(values,false);}
    async Task ImportFiles(string[] values,bool fromAutomaticQueue){
        if(busy||automaticFlow&&!fromAutomaticQueue){deferredDrops.AddRange(values);status.Text="Arquivo recebido. Ele aparece na lista assim que a operação atual terminar.";return;}
        OnlineDiagnostics.Record("ui.import.request",null,"Importação solicitada pelo usuário.",new{Count=values.Length,Game=game.Text,Queue=automaticQueue.ToArray()});
        var importedIds=new HashSet<string>(StringComparer.Ordinal);
        var watch=downloadWatch;var asset=waitingAsset;var mod=waitingMod;var tab=waitingTab;int generation=automaticGeneration;bool closeTab=false;
        bool imported=await Run(()=>{
            var messages=new List<string>();bool revealImport=false;
            foreach(string value in values){try{var result=importer.Import(value.Trim());messages.Add(result.Message);revealImport|=result.Pending;var ids=result.SelectedIds??new string[0];foreach(string id in ids){var m=catalog.Packages.Single(x=>x.Id==id);if(IsBlocked(m)){messages.Add(m.Name+" foi retirado da lista: "+m.BlockedReason);continue;}if(!EditionEligible(id)){messages.Add(m.Name+": arquivo conferido no cache, sem instalação automática neste perfil.");continue;}importedIds.Add(id);Post(()=>{var r=Row(id);if(r!=null)r.Cells["Select"].Value=true;});}}catch(IOException ex){messages.Add(ex.Message);}}
            if(mod!=null&&asset!=null&&importedIds.Contains(mod.Id))closeTab=cache.Contains(asset);
            LoadPendingRows(revealImport);return String.Join("  ",messages);
        });
        if(!imported||importedIds.Count==0||generation!=automaticGeneration)return;
        if(closeTab&&downloadWatch==watch){StopDownloadWatch();await CloseDownloadTab(mod,tab);}
        if(generation!=automaticGeneration)return;
        if(pendingDensityId!=null){
            var densityMod=DensityMod();
            if(densityMod!=null&&importedIds.Contains(densityMod.Id)){await ContinueDensityProfile();return;}
            ClearAutomaticQueue();
        }
        try{string root=GameRoot();AuthorizeAutomatic(importedIds,root,false);if(!fromAutomaticQueue)await ProcessAutomaticQueue(root);}
        catch(Exception ex){ClearAutomaticQueue();status.Text=Friendly(ex);}
    }
    void LoadPending(){LoadPendingRows(false);}
    void LoadPendingRows(bool revealImport){
        var faceHashes=new HashSet<string>(facialItems.Values.Select(i=>facialAssetResolver(i).Sha256));var rows=importer.ReadPending().Where(p=>!faceHashes.Contains(p.Sha256)).ToList();
        var installed=new HashSet<string>(StringComparer.Ordinal);
        string root=InvokeRequired?operationGame:game.Text.Trim();
        if(rows.Count>0&&!String.IsNullOrWhiteSpace(root)){
            try{installed=createRemovalInstaller(root).InstalledImports();}catch(IOException){}catch(UnauthorizedAccessException){}
        }
        Post(()=>{
            var marked=new HashSet<string>(grid.Rows.Cast<DataGridViewRow>().Where(r=>PendingAt(r.Index)!=null&&Convert.ToBoolean(r.Cells["Select"].Value)).Select(r=>(string)r.Tag),StringComparer.Ordinal);
            foreach(DataGridViewRow old in grid.Rows.Cast<DataGridViewRow>().Where(r=>pending.ContainsKey((string)r.Tag)).ToList())grid.Rows.Remove(old);
            pending.Clear();
            foreach(var p in rows){
                string key="pending:"+p.Id;pending[key]=p;
                bool done=installed.Contains(p.Id);
                string state=done?"Já instalado":priorityImports.Any(request=>request.Item.Id==p.Id)?"Na fila prioritária":"Pronto para instalar";
                int index=grid.Rows.Add(marked.Contains(key),p.Name,"—",done?"Instalado correto":"Não registrado",state,done?100:0,"—","—","Arquivo local");
                var row=grid.Rows[index];row.Tag=key;row.DefaultCellStyle.BackColor=Color.FromArgb(248,246,239);row.Cells["Mod"].ToolTipText=p.Source;
            }
            if(revealImport&&categoryFilter.SelectedIndex!=(int)ModCategory.All)categoryFilter.SelectedIndex=(int)ModCategory.Imported;
            ApplyListFilters();
        });
    }

    void PrepareDownloads(IEnumerable<FixedMod> mods){completedDownloads.Clear();foreach(var m in mods){downloads[m.Id]=new DownloadControl();var row=Row(m.Id);if(row==null)continue;row.Cells["Pause"].Value="Pausar";row.Cells["State"].Value="Na fila";MarkBlocked(row,m);}}
    void EndDownloads(){downloadPhase=false;foreach(var c in downloads.Values)c.Dispose();downloads.Clear();completedDownloads.Clear();foreach(DataGridViewRow r in grid.Rows){r.Cells["Pause"].Value="—";if(Convert.ToString(r.Cells["State"].Value)=="Na fila")r.Cells["State"].Value="Não iniciado";}}
    void Completed(string id){Post(()=>{completedDownloads.Add(id);var row=Row(id);if(row!=null)row.Cells["Pause"].Value="—";});}
    void TogglePause(FixedMod mod){
        DownloadControl control;if(!downloadPhase||mod==null||completedDownloads.Contains(mod.Id)||!downloads.TryGetValue(mod.Id,out control))return;
        if(control.IsPaused){control.Resume();Row(mod.Id).Cells["Pause"].Value="Pausar";Row(mod.Id).Cells["State"].Value="Continuando";}
        else{control.Pause();Row(mod.Id).Cells["Pause"].Value="Continuar";Row(mod.Id).Cells["State"].Value="Pausado";}
        MarkBlocked(Row(mod.Id),mod);
    }
    void CancelDownload(FixedMod mod){if(facialQueue!=null&&facialQueue.IsArmed){StopDownloadWatch();status.Text="Fila de downloads faciais cancelada. Downloads posteriores não continuam esta seleção.";return;}
        DownloadControl control;
        if(downloadPhase&&mod!=null&&!completedDownloads.Contains(mod.Id)&&downloads.TryGetValue(mod.Id,out control)){
            ClearAutomaticQueue();foreach(var item in downloads.Values)item.Cancel();var row=Row(mod.Id);if(row!=null){row.Cells["State"].Value="Cancelando";row.Cells["Pause"].Value="—";}return;
        }
        if(downloadWatch!=null&&downloadWatch.IsArmed||!busy&&(automaticQueue.Count>0||pendingDensityId!=null)){ClearAutomaticQueue();status.Text="Download assistido, instalação e troca de densidade na fila cancelados. Nenhum arquivo futuro retomará essa seleção.";}
    }
    void DownloadMods(List<FixedMod> mods,string root,bool skipInstalled=true){
        downloadPhase=true;
        try{foreach(var m in mods){
            var control=downloads[m.Id];int lastPercent=0;
            try{control.Checkpoint();
            if(skipInstalled&&FixedData.Scan(m,root).IsCorrect){SetStatus(m.Id,"Já instalado",100);Completed(m.Id);continue;}
            long total=m.Assets.Sum(a=>a.Size),done=0;
            foreach(var a in m.Assets){long before=done;
                nexus.Download(m,a,cache,control,p=>{
                    long received=before+Math.Min(a.Size,p.Bytes);int percent=total<=0?0:(int)Math.Min(99,received*100.0/total);
                    if(p.Status=="Concluído"&&before+a.Size==total)percent=100;
                    lastPercent=percent;SetStatus(m.Id,p.Status,percent);Report(m.Name+" · "+p.Status+" · "+(received/1024/1024)+" / "+(total/1024/1024)+" MB");
                });done+=a.Size;
            }
            SetStatus(m.Id,"Pronto para instalar",100);Completed(m.Id);
            }catch(Exception ex){SetStatus(m.Id,control.IsCancelled?"Cancelado":"Falhou",lastPercent);string error=ex.Message;Post(()=>{var row=Row(m.Id);if(row!=null)row.Cells["State"].ToolTipText=(IsBlocked(m)?"Retido: "+m.BlockedReason+"\nDownload: ":"")+error;});throw;}
        }}finally{downloadPhase=false;}
    }
    async Task DownloadSelection(List<string> ids){
        if(!EditionGuard(ids))return;
        if(HandleFacialSelection(ids,true,true))return;
        if(busy||automaticFlow||ids.Count==0)return;
        try{
        var mods=FixedData.Expand(catalog,ids);FixedMod manual=null;FixedAsset manualAsset=null;string root=GameRoot();
        bool ready=await Run(()=>{FixedData.ValidateGame(root,mods,catalog.GameFileVersion);RefreshMods(mods,root);foreach(var m in mods){if(FixedData.Scan(m,root).IsCorrect)continue;var asset=m.Assets.FirstOrDefault(a=>a.ModId>0&&!cache.Contains(a));if(asset!=null){manual=m;manualAsset=asset;break;}}return "Arquivos e versões conferidos.";});
        if(!ready)return;
        if(manual!=null&&!nexus.Premium){int generation=automaticGeneration;bool watching=StartDownloadWatch(manual,manualAsset,root,false);if(watching)await EnsureWatchedPage();else await OpenOfficial(manual,manualAsset);if(generation!=automaticGeneration)return;status.Text=watching?"Conclua o download no site e salve em "+downloadFolder+". O app importa o arquivo sozinho; esta ação não instala mods.":"Conclua o download e arraste o arquivo para a janela. Nenhum mod foi instalado.";return;}
        PrepareDownloads(mods);
        try{await Run(()=>{DownloadMods(mods,root);return "Downloads concluídos e conferidos. Nenhum mod foi instalado.";});}
        finally{EndDownloads();}
        }catch(Exception ex){status.Text=Friendly(ex);}
    }
    void ClearAutomaticQueue(){OnlineDiagnostics.Record("queue.cleared",null,"Fila de instalação encerrada ou cancelada.",new{Game=automaticGame,Mods=automaticQueue.ToArray(),Generation=automaticGeneration});StopDownloadWatch();automaticQueue.Clear();skippedThisQueue.Clear();preparationWasSkipped=false;automaticGame=null;pendingDensityId=null;pendingDensityRoot=null;pendingDensityGeneration=0;automaticGeneration++;}
    void AuthorizeAutomatic(IEnumerable<string> ids,string root,bool replace){
        ids=ids.ToList();RequireEdition(ids);
        var allowed=ids.Distinct().Where(id=>catalog.Packages.Any(m=>m.Id==id&&!IsBlocked(m))).ToList();
        if(replace||automaticGame==null||!String.Equals(automaticGame,root,StringComparison.OrdinalIgnoreCase))ClearAutomaticQueue();
        automaticGame=root;foreach(string id in allowed)automaticQueue.Add(id);
        OnlineDiagnostics.Record("queue.authorized",null,"Instalação automática autorizada pelo fluxo atual.",new{Game=root,Mods=automaticQueue.ToArray(),Replace=replace,Generation=automaticGeneration});
    }
    void ShowPlan(AutoInstallPlan plan){
        foreach(var m in plan.ExpandedMods.Where(m=>!IsBlocked(m))){
            var scan=plan.Scans[m.Id];bool cached=m.Assets.All(a=>{bool found;return plan.CachedAssets.TryGetValue(a.Sha256,out found)&&found;});
            OnlineDiagnostics.Record("ui.plan.enqueue",m.Id,"Plano conferido enviado para a lista.",new{Game=automaticGame,State=scan.State,Correct=scan.Correct,Missing=scan.Missing,Different=scan.Different,Cached=cached});
            string note=m.Warning;
            string localNote=DensityNote(m,GameRoot(),note);
            Post(()=>{var row=Row(m.Id);if(row==null||skippedThisQueue.Contains(m.Id))return;row.Cells["Installed"].Value=scan.State;row.Cells["Installed"].ToolTipText=scan.Detail;ApplyBrowserNote(row,localNote);row.Cells["State"].Value=scan.IsCorrect?(plan.MissingMods.Any(x=>x.Id==m.Id)?"Aplicar padrão":"Já instalado"):cached?"Pronto para instalar":"A baixar";row.Cells["Progress"].Value=scan.IsCorrect||cached?100:0;row.Cells["State"].Style.ForeColor=Color.Black;row.Cells["State"].ToolTipText="";});
        }
    }
    async Task ProcessAutomaticQueue(string root){
        if(busy||automaticFlow)return;automaticFlow=true;UpdateDownloadSkipButton();
        try{
            while(automaticQueue.Count>0){
                await ProcessPriorityImports(true);
                await FlushDeferredDrops(true);
                if(automaticQueue.Count==0)return;
                var ids=automaticQueue.ToList();if(!EditionGuard(ids)){ClearAutomaticQueue();return;}int generation=automaticGeneration;AutoInstallPlan plan=null;
                bool checkedOk=await Run(()=>{plan=AutoInstallPlan.Build(catalog,ids,root,cache,nexus.Premium);ShowPlan(plan);return "Itens autorizados e dependências conferidos.";});
                if(!checkedOk){ClearAutomaticQueue();return;}
                if(generation!=automaticGeneration)return;
                if(priorityImports.Count>0||deferredDrops.Count>0)continue;
                if(plan.Complete){status.Text=CompleteAutomaticQueue("Processo concluído. Os mods autorizados e suas dependências estão corretos; arquivos anteriores foram preservados.");return;}
                if(plan.ReadyMods.Count>0){
                    var ready=plan.ReadyMods;var readyIds=ready.Select(m=>m.Id).ToList();PrepareDownloads(ready);
                    bool applied;preparationWasSkipped=false;
                    try{applied=await Run(()=>{
                        DownloadMods(ready,root);
                        if(generation!=automaticGeneration)throw new OperationCanceledException("Instalação na fila cancelada; nenhuma nova cópia foi iniciada.");
                        foreach(var item in ready)SetStatus(item.Id,"Instalando",100);string result=createInstaller(root).Install(readyIds,true,Report);
                        RefreshMods(plan.ExpandedMods,root);return result;
                    });}finally{EndDownloads();}
                    if(!applied){if(preparationWasSkipped){preparationWasSkipped=false;continue;}ClearAutomaticQueue();return;}
                    continue;
                }
                if(plan.ManualMod!=null){
                    bool watching=StartDownloadWatch(plan.ManualMod,plan.ManualAsset,root,true);
                    if(watching)await EnsureWatchedPage();else await OpenOfficial(plan.ManualMod,plan.ManualAsset);if(generation!=automaticGeneration)return;
                    status.Text=watching?"Aguardando "+plan.ManualMod.Name+" em "+downloadFolder+". Clique em Manual / Slow download no Nexus; ao concluir, o app importa e continua sozinho."+" A aba do navegador padrão não será fechada.":"Escolha a pasta correta em Pasta de downloads ou importe o arquivo original para continuar.";
                    return;
                }
                status.Text="Aguardando os arquivos conferidos das dependências da seleção autorizada.";return;
            }
            status.Text=CompleteAutomaticQueue("Processo concluído. Os mods autorizados foram instalados ou já estavam corretos.");
        }finally{automaticFlow=false;foreach(var a in actions)a.Enabled=true;game.Enabled=true;activity.Visible=false;UpdateDownloadSkipButton();}
    }
    async void Install(List<string> ids,bool withDownload){
        if(!EditionGuard(ids))return;
        if(busy||automaticFlow)return;
        if(HandleFacialSelection(ids,withDownload,false))return;
        OnlineDiagnostics.Record("ui.install.request",null,"Botão ou menu de instalação acionado.",new{Game=game.Text,Mods=ids,WithDownload=withDownload});
        try{
            if(ids.Count==0)throw new IOException("Marque os mods que deseja instalar.");string root=GameRoot();AutoInstallPlan plan=null;bool missingCache=false;
            bool checkedOk=await Run(()=>{
                plan=AutoInstallPlan.Build(catalog,ids,root,cache,nexus.Premium);ShowPlan(plan);
                missingCache=plan.MissingMods.Where(m=>!plan.Scans[m.Id].IsCorrect).SelectMany(m=>m.Assets).Any(a=>!plan.CachedAssets[a.Sha256]);
                return "Seleção e dependências conferidas.";
            });
            if(!checkedOk)return;if(plan.Complete){ClearAutomaticQueue();status.Text="Os itens selecionados já estão corretos. Nada foi alterado.";return;}
            if(!withDownload&&missingCache){status.Text="Há arquivos ausentes no cache. Use Baixar e instalar ou importe os arquivos originais.";return;}
            string names=String.Join("\n",plan.MissingMods.Take(18).Select(m=>"• "+m.Name+" "+m.Version))+(plan.MissingMods.Count>18?"\n… e mais "+(plan.MissingMods.Count-18)+" componente(s).":"");
            var manualNotes=String.Join("\n",plan.MissingMods.Where(m=>m.ManualSelection).Select(m=>m.Name+" — "+m.Warning));
            if(!confirmInstallation((withDownload?"Baixar e instalar":"Instalar")+" "+plan.MissingMods.Count+" componente(s) em:\n"+root+"\n\n"+names+(String.IsNullOrEmpty(manualNotes)?"":"\n\n"+manualNotes)+"\n\nOs itens prontos serão instalados; arquivos corretos serão preservados.\nNa conta gratuita, conclua cada download oficial na pasta monitorada; o aplicativo confere e continua automaticamente.\nSerá feito backup dos saves. Continuar?"))return;
            OnlineDiagnostics.Record("ui.install.confirmed",null,"Plano de instalação confirmado na interface.",new{Game=root,Mods=ids});AuthorizeAutomatic(ids,root,true);await ProcessAutomaticQueue(root);
        }catch(Exception ex){ClearAutomaticQueue();status.Text=Friendly(ex);}
    }
    async Task RemoveSelected(){
        if(busy||automaticFlow)return;
        try{
            var ids=Chosen();if(ids.Count==0){var current=CurrentMod();if(current!=null)ids.Add(current.Id);}
            var removable=catalog.Packages.Where(m=>ids.Contains(m.Id)&&!m.Immutable).ToList();
            if(removable.Count==0){status.Text="Marque os complementos que deseja remover. A base NightCoop/RED4ext é preservada.";return;}
            string root=GameRoot(),names=String.Join("\n",removable.Take(18).Select(m=>m.Name))+(removable.Count>18?"\n… e mais "+(removable.Count-18)+" mod(s).":"");
            if(!confirmRemoval("Remover "+removable.Count+" mod(s) selecionado(s) de:\n"+root+"\n\n"+names+"\n\nOs arquivos conferidos serão guardados para desfazer. Saves, arquivos alheios e a base NightCoop/RED4ext serão preservados. Dependentes fora da seleção precisam ser removidos primeiro. Continuar?"))return;
            OnlineDiagnostics.Record("ui.remove.confirmed",null,"Remoção dos selecionados confirmada.",new{Game=root,Mods=ids});ClearAutomaticQueue();
            bool removed=await Run(()=>{string result=createRemovalInstaller(root).DeactivateMany(ids,Report);RefreshMods(catalog.Packages,root);return result;});
            if(removed)foreach(var mod in removable){var row=Row(mod.Id);if(row!=null)row.Cells["Select"].Value=false;}
        }catch(Exception ex){OnlineDiagnostics.Record("ui.remove.failed",null,"Falha na ação de remover.",new{ErrorType=ex.GetType().Name});status.Text=Friendly(ex);}
    }
    void DeactivateMod(FixedMod mod){
        if(busy||automaticFlow||mod==null)return;
        OnlineDiagnostics.Record("ui.deactivate.request",mod.Id,"Menu de desativação acionado.",new{Game=game.Text});
        if(MessageBox.Show(this,"Desativar "+mod.Name+"?\n\nOs arquivos serão preservados para desfazer. Dependências compartilhadas e a base do coop não serão retiradas.","Desativar mod",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
        ClearAutomaticQueue();
        var ignored=Run(()=>{string root=GameRoot();string result=new FixedInstaller(catalog,cache,root,null).Deactivate(mod.Id,Report);RefreshMods(catalog.Packages,root);return result;});
    }
    void Undo(){
        if(busy||automaticFlow)return;
        if(MessageBox.Show(this,"Desfazer a última instalação, remoção ou limpeza feita por este gerenciador?\nArquivos anteriores ou alterados depois serão preservados.","Desfazer operação",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
        ClearAutomaticQueue();
        var ignored=Run(()=>{string root=GameRoot();string result=new FixedInstaller(catalog,cache,root,null).Undo(Report);RefreshMods(catalog.Packages,root);LoadPending();return result;});
    }
    async void AuditMods(List<string> ids,ModImportItem imported){
        if(busy||automaticFlow)return;
        if(ids.Any(id=>FacialById(id)!=null)){status.Text="As linhas Rosto são preparações de download, ainda sem pacotes conferidos. Dois cliques mostram a fonte e a variante; a auditoria dos arquivos vem após o download.";return;}
        if(ids.Count==0&&imported==null){var current=CurrentMod();if(current!=null)ids.Add(current.Id);}
        if(ids.Count==0&&imported==null){status.Text="Marque os mods ou selecione um item para consultar a auditoria.";return;}
        string root=game.Text.Trim();ModAuditReport report=null;status.Text="Auditando os mods e suas dependências. Esta análise não altera arquivos.";
        if(!await Run(()=>{report=ModAudit.Build(catalog,ids,root,imported);return "Auditoria concluída. Relatório informativo; nenhuma instalação foi iniciada.";}))return;
        using(var window=new ModAuditWindow(report))window.ShowDialog(this);
    }
    async void ShowDetails(int row){
        var imported=PendingAt(row);if(imported!=null){ShowImportedInstall(imported);return;}
        if(busy||automaticFlow)return;
        OnlineDiagnostics.Record("ui.details.request",Convert.ToString(grid.Rows[row].Tag),"Duplo clique abriu detalhes do mod.",new{Game=game.Text});
        var facial=FacialById((string)grid.Rows[row].Tag);if(facial!=null){ShowFacialDetails(facial);return;}
        var m=ModAt(row);if(m==null)return;
        string root=game.Text.Trim(),notes=Convert.ToString(grid.Rows[row].Cells["Notes"].Value);ModDetailsInfo info=null;
        if(!await Run(()=>{info=ModDetailsInfo.Read(m,root,notes,catalog);return "Detalhes de "+m.Name+" conferidos.";}))return;
        using(var details=new ModDetailsWindow(info,launchBrowser))details.ShowDialog(this);
    }
    public void Preview(string file){
        game.Text=@"D:\Jogos\Cyberpunk 2077";
        grid.Rows[0].Cells["Installed"].Value="Instalado correto";grid.Rows[0].Cells["State"].Value="Já instalado";grid.Rows[0].Cells["Progress"].Value=100;
        grid.Rows[1].Cells["State"].Value="Baixando";grid.Rows[1].Cells["Progress"].Value=67;grid.Rows[1].Cells["Pause"].Value="Pausar";
        grid.Rows[2].Cells["State"].Value="Pausado";grid.Rows[2].Cells["Progress"].Value=32;grid.Rows[2].Cells["Pause"].Value="Continuar";
        grid.Rows[3].Cells["State"].Value="Falhou";grid.Rows[3].Cells["State"].Style.ForeColor=Color.Firebrick;grid.Rows[3].Cells["Progress"].Value=18;
        ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;Location=new Point(-30000,-30000);Show();Application.DoEvents();PerformLayout();
        using(var bmp=new Bitmap(Width,Height)){DrawToBitmap(bmp,new Rectangle(0,0,Width,Height));bmp.Save(file,System.Drawing.Imaging.ImageFormat.Png);}Close();
    }
}

public sealed class DownloadBarCell:DataGridViewTextBoxCell {
    protected override void Paint(Graphics g,Rectangle clip,Rectangle bounds,int rowIndex,DataGridViewElementStates state,object value,object formattedValue,string error,DataGridViewCellStyle style,DataGridViewAdvancedBorderStyle border,DataGridViewPaintParts parts){
        if(value is string&&(string)value=="—"){base.Paint(g,clip,bounds,rowIndex,state,value,value,error,style,border,parts);return;}
        base.Paint(g,clip,bounds,rowIndex,state,null,null,error,style,border,parts&~DataGridViewPaintParts.ContentForeground);
        bool estimated=value is string&&((string)value).StartsWith("~",StringComparison.Ordinal);int percent=0;if(value!=null)Int32.TryParse(Convert.ToString(value).TrimStart('~'),out percent);percent=Math.Max(0,Math.Min(100,percent));
        var track=new Rectangle(bounds.X+7,bounds.Y+9,Math.Max(1,bounds.Width-14),Math.Max(12,bounds.Height-18));
        using(var brush=new SolidBrush(Color.FromArgb(228,233,240)))g.FillRectangle(brush,track);
        if(percent>0)using(var brush=new SolidBrush(Color.FromArgb(47,139,188)))g.FillRectangle(brush,new Rectangle(track.X,track.Y,track.Width*percent/100,track.Height));
        TextRenderer.DrawText(g,(estimated?"~":"")+percent+"%",style.Font,track,Color.Black,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
    }
}

static class OnlineProgram {
    [STAThread]static int Main(string[] args){try{
#if VELOPACK
        Velopack.VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
#endif
        System.Net.ServicePointManager.SecurityProtocol=System.Net.SecurityProtocolType.Tls12;
        if(args.Length==2&&args[0]=="--self-test"){OnlineTests.Run(args[1]);return 0;}
        if(args.Length==2&&args[0]=="--self-test-p2p"){GeneralP2PTests.Run(args[1]);return 0;}
        if(args.Length==2&&args[0]=="--self-test-snapshot"){InstalledSnapshotTests.Run(args[1]);InstalledSnapshotInstallerTests.Run(args[1],true);return 0;}
        if(args.Length==3&&args[0]=="--self-test-snapshot-network"){InstalledSnapshotP2PTests.RunNetwork(args[1],args[2]);return 0;}
        if(args.Length==3&&args[0]=="--self-test-p2p-dialog-network"){InstalledSnapshotP2PTests.RunDialogNetwork(args[1],args[2]);return 0;}
        if(args.Length==2&&args[0]=="--self-test-p2p-helper"){GeneralP2PTests.RunHelper(args[1]);return 0;}
        if(args.Length==3&&args[0]=="--self-test-p2p-network"){GeneralP2PTests.RunNetwork(args[1],args[2]);return 0;}
        var c=FixedData.Load();
        if(args.Length==3&&args[0]=="--inspect"){string game=Path.GetFullPath(args[1]).TrimEnd('\\'),output=Path.GetFullPath(args[2]);if(output.StartsWith(game+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("O relatório deve ficar fora do jogo.");var report=c.Packages.Select(m=>FixedData.Scan(m,game)).ToList();string issue=null;try{FixedData.ValidateGame(game,c.Packages,c.GameFileVersion);}catch(IOException ex){issue=ex.Message;}Safe.WriteJson(output,new{GameVersion=FixedData.GameVersion(game),Build=FixedData.GameBuild(game),VersionAndRequirementsMatch=issue==null,Issue=issue,Mods=report});return 0;}
        if(args.Length==3&&args[0]=="--verify-local-sources"){OnlineTests.VerifySources(c,args[1],args[2]);return 0;}
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        if(args.Length==2&&args[0]=="--self-test-general"){GeneralModWindow.RunGeneralTests(args[1]);return 0;}
        if(args.Length==2&&args[0]=="--self-test-clone"){GeneralModWindow.RunProjectCloneTests(args[1]);return 0;}
        if(args.Length==2&&args[0]=="--self-test-header"){GeneralButtonPaintTests.Run(args[1]);GeneralModWindow.RunProjectHubTests(args[1]);return 0;}
        if(args.Length==2&&args[0]=="--self-test-projects"){GeneralProjectStoreTests.Run(args[1]);GeneralModWindow.RunProjectHomeTests(args[1]);GeneralModWindow.RunProjectDeletionTests(args[1]);GeneralModWindow.RunProjectHubTests(args[1]);GeneralBrandTests.Run(args[1]);return 0;}
        if(args.Length==2&&args[0]=="--self-test-menus"){GeneralMenuHostTests.Run(args[1]);GeneralModWindow.RunPresetDraftTests(args[1]);GeneralTableDialogTests.Run(args[1]);GeneralPopupStabilityTests.Run(args[1]);return 0;}
        if(args.Length==2&&args[0]=="--self-test-p2p-ui"){GeneralP2PUiTests.Run(args[1]);return 0;}
        if(args.Length==2&&args[0]=="--preview-p2p"){GeneralP2PUiTests.Preview(args[1]);return 0;}
        if(args.Length==2&&args[0]=="--self-test-no-copy"){GeneralModWindow.RunNoCopyTests(args[1]);return 0;}
        if(args.Length==2&&args[0]=="--self-test-storage"){GeneralModWindow.RunStorageTests(args[1]);return 0;}
        if(args.Length==3&&args[0]=="--self-test-p2p-sync"){GeneralP2PLifecycleTests.RunSync(args[1],args[2]);return 0;}
        if(args.Length==3&&args[0]=="--self-test-nightcoop"){NightCoopReleaseTests.Run(args[1],args[2]);return 0;}
        if(args.Length==3&&args[0]=="--self-test-p2p-engine"){GeneralP2PLifecycleTests.RunEngine(args[1],args[2]);return 0;}
        if(args.Length==3&&args[0]=="--self-test-p2p-engine-owner"){GeneralP2PLifecycleTests.RunEngineOwner(args[1],args[2]);return 0;}
        if(args.Length==3&&args[0]=="--self-test-p2p-lifecycle"){GeneralP2PLifecycleTests.Run(args[1],args[2]);return 0;}
        if(args.Length==3&&args[0]=="--self-test-p2p-automatic"){GeneralP2PAutomaticTests.Run(args[1],args[2]);return 0;}
        if(args.Length==3&&args[0]=="--self-test-p2p-diagnostics"){GeneralP2PDiagnosticTests.Run(args[1],args[2]);return 0;}
        if(args.Length==3&&args[0]=="--self-test-p2p-deferred"){GeneralP2PDeferredTests.Run(args[1],args[2]);return 0;}
        if(args.Length==2&&args[0]=="--self-test-versions"){ModVersionChoiceTests.Run(args[1]);return 0;}
        if(args.Length==2&&args[0]=="--self-test-updates"){GeneralUpdatesTests.Run(args[1]);GeneralAutomaticUpdateTests.Run(args[1]);return 0;}
        if(args.Length==2&&args[0]=="--self-test-paths"){GeneralAppPathsTests.Run(args[1]);return 0;}
        var paths=GeneralAppPaths.Current();
        var cache=new DownloadCache(paths.Cache);
        if(args.Length==2&&args[0]=="--preview"){using(var f=new OnlineWindow(c,cache,true))f.Preview(args[1]);return 0;}
        if(args.Length==2&&args[0]=="--preview-general"){
            var fixture=new GeneralModState{List=GeneralModLibrary.Seed(c,new ModImportItem[0]),Destination=@"D:\Jogos\Cyberpunk 2077"};
            using(var f=new GeneralModWindow(c,cache,Path.GetTempPath(),fixture))f.Preview(args[1]);return 0;
        }
        if(args.Length!=0)throw new IOException("Argumento não reconhecido. A instalação exige confirmação na interface.");
        OnlineDiagnostics.Start(paths.Logs);string exeHash=null;try{if(OnlineDiagnostics.Enabled)exeHash=Safe.Hash(Application.ExecutablePath);}catch(Exception){}OnlineDiagnostics.Record("app.start",null,"Aplicativo iniciado com diagnóstico ativo.",new{AppVersion=GeneralUpdateChecker.CurrentVersion,Executable=Path.GetFileName(Application.ExecutablePath),ExecutableSha256=exeHash,Catalog=c.Id,CatalogSha256=OnlineIdentity.CatalogHash});
#if VELOPACK
        GeneralVelopackUpdate.CheckAtStartup();
#else
        if(!GeneralAppPaths.IsDevelopment&&GeneralAutomaticUpdate.TryStart(Application.ExecutablePath,paths)){OnlineDiagnostics.Stop();return 0;}
#endif
        Application.Run(new GeneralProjectHome(c,cache,paths.Data));OnlineDiagnostics.Stop();return 0;
    }catch(Exception ex){if(args.Length>0){File.WriteAllText(Path.Combine(Path.GetTempPath(),"CyberpunkCoopOnline-erro.txt"),ex.ToString());return 1;}MessageBox.Show(ex.Message,"Não foi possível abrir o programa",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}}
}
