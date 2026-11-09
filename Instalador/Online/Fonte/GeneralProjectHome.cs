using System;
using System.Drawing;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class GeneralProjectHome : Form {
    readonly FixedCatalog catalog;
    readonly DownloadCache cache;
    readonly GeneralProjectStore store;
    readonly bool fixture;
    readonly Dictionary<string,P2PService> sharing=new Dictionary<string,P2PService>(StringComparer.OrdinalIgnoreCase);
    readonly GeneralMenuHost menus=new GeneralMenuHost();
    GeneralModWindow sharingWindow;
    GeneralP2PDialog sharingDialog;
    string ReceiveData {get{return Path.Combine(store.Root,"P2P-Inbox");}}
    ListBox saved;
    LinkLabel update;
    GeneralStorageLabel storageSize;
    GeneralUpdateRelease available=null;
    int buildVersion,hoverSaved=-1;
    internal bool DeletingProject {get;private set;}
    readonly ToolTip savedTips=new ToolTip{UseAnimation=false,UseFading=false};
    internal int HoveredSavedProject {get{return hoverSaved;}}
    internal Action<GeneralProject> OpenRequested;
    internal GeneralProjectHome(FixedCatalog catalog,DownloadCache cache,string root,bool fixture=false){
        this.catalog=catalog;this.cache=cache;this.fixture=fixture;store=new GeneralProjectStore(root);OpenRequested=OpenProject;
        Text=GeneralAppPaths.WindowTitle;Icon=ModLoaderBrand.AppIcon;ClientSize=new Size(1020,740);MinimumSize=new Size(900,680);
        StartPosition=FormStartPosition.CenterScreen;AutoScaleMode=AutoScaleMode.Dpi;Build();Disposed+=(s,e)=>{
            savedTips.Dispose();menus.Dispose();
            foreach(var service in sharing.Values)try{service.Dispose();}catch(Exception){}
        };FormClosing+=(s,e)=>{if(DeletingProject)e.Cancel=true;};
        Shown+=(s,e)=>RefreshStorageSize();
        if(!fixture)Shown+=async(s,e)=>await RestoreSharing();
#if !VELOPACK
        if(!fixture&&!GeneralAppPaths.IsDevelopment)Shown+=async(s,e)=>{available=await GeneralUpdateChecker.Session.CheckOnce();if(!IsDisposed)UpdateNotice();};
#endif
    }
    static string T(string pt,string en){return GeneralStyle.T(pt,en);}
    internal void Build(){
        var index=store.Read();GeneralStyle.UseTheme(index.Theme);GeneralStyle.Language=index.Language;
        menus.Close();buildVersion++;hoverSaved=-1;saved=null;
        SuspendLayout();while(Controls.Count>0){var old=Controls[0];Controls.Remove(old);old.Dispose();}
        Font=GeneralStyle.Font(12);BackColor=GeneralStyle.Background;ForeColor=GeneralStyle.Foreground;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=8,Padding=new Padding(32,28,32,24)};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        for(int i=0;i<8;i++)layout.RowStyles.Add(i==4?new RowStyle(SizeType.Percent,100):new RowStyle(SizeType.AutoSize));Controls.Add(layout);
        var header=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=2,Margin=Padding.Empty};
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,44));
        var brand=new FlowLayoutPanel{AutoSize=true,WrapContents=false,Margin=Padding.Empty};brand.Controls.Add(new ModLoaderLogo{Size=new Size(44,44),Margin=new Padding(0,0,12,0)});
        var brandTitle=new Label{Text="MOD LOADER",AutoSize=true,Font=GeneralStyle.Font(27,true),ForeColor=GeneralStyle.Accent,Margin=Padding.Empty};brand.Controls.Add(brandTitle);header.Controls.Add(brand,0,0);
        var menu=(GeneralButton)GeneralStyle.Button("",ToggleHomeMenu);menu.Name="HomeMenu";menu.Bare=true;menu.IconKind="menu";menu.AccessibleName=T("Menu","Menu");menu.Size=new Size(44,44);menu.AutoSize=false;menu.MinimumSize=new Size(44,44);menu.Dock=DockStyle.Fill;menu.Margin=Padding.Empty;header.Controls.Add(menu,1,0);layout.Controls.Add(header,0,0);
        var intro=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=Padding.Empty};
        intro.Controls.Add(new Label{Name="HomeIntro",Text=T("Criado por Nask e Codex. Se achou ruim, faz você.","Made by Nask and Codex. If you don't like it, make your own."),AutoSize=true,ForeColor=GeneralStyle.Muted,Margin=new Padding(0,10,0,4)});
        storageSize=new GeneralStorageLabel(cache){Name="HomeStorage",Text=T("Calculando espaço…","Calculating size…"),AutoSize=true,Font=GeneralStyle.Font(10.5f),ForeColor=GeneralStyle.Muted,Margin=new Padding(0,0,0,18),MaximumSize=new Size(Math.Max(200,ClientSize.Width-68),0)};intro.Controls.Add(storageSize);layout.Controls.Add(intro,0,1);
        var actions=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=2,Margin=new Padding(0,0,0,22)};actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        var create=GeneralStyle.Button(T("Criar novo projeto","Create new project"),()=>Guard(()=>OpenRequested(CreateProject(false))),true);create.Name="NewProject";create.Dock=DockStyle.Fill;create.AutoSize=false;create.MinimumSize=new Size(140,44);create.MaximumSize=new Size(0,44);create.Height=44;create.Margin=new Padding(0,0,8,0);actions.Controls.Add(create,0,0);
        var import=GeneralStyle.Button(T("Importar projeto","Import project"),()=>Guard(()=>OpenRequested(ImportProject())));import.Name="ImportProject";import.Dock=DockStyle.Fill;import.AutoSize=false;import.MinimumSize=new Size(140,44);import.MaximumSize=new Size(0,44);import.Height=44;import.Margin=new Padding(8,0,0,0);actions.Controls.Add(import,1,0);layout.Controls.Add(actions,0,2);
        layout.Controls.Add(Heading(T("Projetos salvos","Saved projects")),0,3);
        var surface=new GeneralSurface{Dock=DockStyle.Fill,Padding=new Padding(10),Margin=new Padding(0,0,0,18)};
        var custom=index.Projects.Where(item=>item.TemplateId==null).ToArray();
        if(custom.Length==0)surface.Controls.Add(new Label{Name="EmptyProjects",Text=T("Seus projetos salvos aparecerão aqui.","Your saved projects will appear here."),Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,ForeColor=GeneralStyle.Muted});
        else{
            saved=new ListBox{Name="SavedProjects",Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,IntegralHeight=false,DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=56,AccessibleName=T("Projetos salvos","Saved projects")};
            foreach(var project in custom.Reverse())saved.Items.Add(new Choice(project));
            saved.DrawItem+=(s,e)=>{if(e.Index<0)return;bool selected=hoverSaved>=0?e.Index==hoverSaved:(e.State&DrawItemState.Selected)!=0;GeneralStyle.PaintChoice(e.Graphics,e.Bounds,selected);using(var font=GeneralStyle.Font(13,true))TextRenderer.DrawText(e.Graphics,((ListBox)s).Items[e.Index].ToString(),font,new Rectangle(e.Bounds.X+12,e.Bounds.Y,e.Bounds.Width-68,e.Bounds.Height),GeneralStyle.Foreground,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);var trash=DeleteProjectBounds(e.Bounds);LucideIcons.Draw(e.Graphics,"trash",new Rectangle(trash.X+12,trash.Y+12,20,20),GeneralStyle.Danger);};
            saved.AccessibleDescription=T("Enter abre o projeto selecionado. Delete exclui com duas confirmações.","Enter opens the selected project. Delete removes it after two confirmations.");
            saved.MouseMove+=(s,e)=>{var list=(ListBox)s;int at=list.IndexFromPoint(e.Location);if(at!=hoverSaved){hoverSaved=at;list.Invalidate();}string hint=at>=0&&DeleteProjectBounds(list.GetItemRectangle(at)).Contains(e.Location)?T("Excluir projeto","Delete project"):"";if(savedTips.GetToolTip(list)!=hint)savedTips.SetToolTip(list,hint);};
            saved.MouseLeave+=(s,e)=>{hoverSaved=-1;savedTips.SetToolTip((Control)s,"");((ListBox)s).Invalidate();};
            saved.MouseClick+=(s,e)=>{var list=(ListBox)s;int at=list.IndexFromPoint(e.Location);if(e.Button!=MouseButtons.Left||at<0)return;var item=((Choice)list.Items[at]).Project;if(DeleteProjectBounds(list.GetItemRectangle(at)).Contains(e.Location))DeleteSavedProject(item);else Guard(()=>OpenRequested(item));};
            saved.KeyDown+=(s,e)=>{var list=(ListBox)s;if(list.SelectedItem==null)return;if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;Guard(()=>OpenRequested(((Choice)list.SelectedItem).Project));}else if(e.KeyCode==Keys.Delete){e.SuppressKeyPress=true;DeleteSavedProject(((Choice)list.SelectedItem).Project);}};
            surface.Controls.Add(new GeneralScrollView(saved));
        }
        layout.Controls.Add(surface,0,4);layout.Controls.Add(Heading(T("Projetos prontos","Ready-made projects")),0,5);
        var ready=GeneralStyle.Button("Cyberpunk 2077  ·  "+T("Pacote pronto","Ready-made pack"),()=>Guard(()=>OpenRequested(CreateProject(true))));ready.Name="ReadyCyberpunk";ready.Dock=DockStyle.Fill;ready.MinimumSize=new Size(200,56);ready.Margin=new Padding(0,0,0,14);layout.Controls.Add(ready,0,6);
        update=new LinkLabel{Name="HomeUpdate",AutoSize=true,LinkColor=GeneralStyle.Accent,ActiveLinkColor=GeneralStyle.Foreground,Margin=Padding.Empty};update.LinkClicked+=(s,e)=>{if(available!=null)Guard(()=>GeneralModDownload.OpenSource(available.Page));};layout.Controls.Add(update,0,7);UpdateNotice();
        GeneralStyle.Apply(this,GeneralStyle.Dark);brandTitle.ForeColor=GeneralStyle.Accent;ResumeLayout(true);GeneralNativeTheme.ApplyTree(this);if(IsHandleCreated)RefreshStorageSize();
    }
    async void RefreshStorageSize(){
        int version=buildVersion;var label=storageSize;
        try{var size=await Task.Run(()=>GeneralProjectStorage.Measure(store,catalog,cache));if(!IsDisposed&&version==buildVersion&&!label.IsDisposed)label.SetSize(size,true);}
        catch(Exception){if(!IsDisposed&&version==buildVersion&&!label.IsDisposed)label.SetSize(null,true);}
    }
    internal static Rectangle DeleteProjectBounds(Rectangle row){return new Rectangle(row.Right-44,row.Y+(row.Height-44)/2,44,44);}
    async void DeleteSavedProject(GeneralProject project){await DeleteSavedProjectAsync(project);}
    internal async Task DeleteSavedProjectAsync(GeneralProject project){
        if(DeletingProject||project==null||project.TemplateId!=null)return;
        DeletingProject=true;
        try{
            bool removeMods;
            using(var choice=new GeneralProjectDeleteChoice(project.Name)){if(choice.ShowDialog(this)!=DialogResult.OK)return;removeMods=choice.RemoveMods;}
            var engine=new GeneralProjectDeletion(store,catalog,fixture);ProjectDeletePlan plan=null;
            if(removeMods){UseWaitCursor=true;Enabled=false;try{plan=await Task.Run(()=>engine.Plan(project));}finally{Enabled=true;UseWaitCursor=false;}}
            using(var review=new GeneralProjectDeleteReview(project.Name,plan))if(review.ShowDialog(this)!=DialogResult.OK)return;
            UseWaitCursor=true;Enabled=false;
            try{await Task.Run(()=>{if(removeMods)engine.Apply(plan);else store.Delete(project);});}finally{Enabled=true;UseWaitCursor=false;}
            Build();
        }catch(Exception ex){GeneralDialog.Message(this,ex.Message+T("\r\n\r\nSe a remoção foi interrompida, o projeto permanece na lista para você conferir e tentar novamente. Arquivos já apagados não têm Desfazer.","\r\n\r\nIf removal was interrupted, the project stays listed so you can review and retry. Already erased files have no Undo."),T("Não foi possível concluir","Unable to complete"),MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        finally{DeletingProject=false;if(!IsDisposed){Enabled=true;UseWaitCursor=false;}}
    }
    static Label Heading(string text){return new Label{Text=text,AutoSize=true,Font=GeneralStyle.Font(15,true),Margin=new Padding(0,0,0,10)};}
    void UpdateNotice(){if(update==null||update.IsDisposed)return;update.Visible=available!=null;if(available!=null)update.Text=T("Nova versão ","New version ")+available.Version+T(" — baixar"," — download");}
    internal GeneralProject CreateProject(bool ready){
        var preferences=store.Read();
        if(ready){
            var existing=preferences.Projects.FirstOrDefault(item=>item.TemplateId=="cyberpunk-2077");if(existing!=null)return existing;
            return store.Create("Cyberpunk 2077",GeneralModLibrary.Seed(catalog,new ModImportItem[0]),preferences.Theme,preferences.Language,!fixture&&!GeneralAppPaths.IsDevelopment?Discovery.Find().FirstOrDefault():null,"cyberpunk-2077");
        }
        using(var name=new GeneralNameDialog(T("Nome do projeto","Project name"),"")){
            if(name.ShowDialog(this)!=DialogResult.OK)return null;
            var list=new GeneralModList();list.Presets.Add(new GeneralModPreset{Id="main",Name=name.ResultName});
            return store.Create(name.ResultName,list,preferences.Theme,preferences.Language);
        }
    }
    GeneralProject ImportProject(){
        using(var open=new OpenFileDialog{Title=T("Importar projeto","Import project"),Filter=T("Projeto Mod Loader|*.json","Mod Loader project|*.json")}){
            if(open.ShowDialog(this)!=DialogResult.OK)return null;
            using(var name=new GeneralNameDialog(T("Nome do projeto","Project name"),Path.GetFileNameWithoutExtension(open.FileName))){
                if(name.ShowDialog(this)!=DialogResult.OK)return null;
                var list=new GeneralModLibrary(Path.Combine(store.Root,"lista.json")).Import(open.FileName);
                var preferences=store.Read();return store.Create(name.ResultName,list,preferences.Theme,preferences.Language);
            }
        }
    }
    void OpenProject(GeneralProject project){
        while(project!=null){
            string action=null;
            try{
                var restored=store.Load(project);
                using(var window=new GeneralModWindow(catalog,cache,store.DataPath(project),fixture?restored:null)){
                    window.AttachProject(store,project);
                    if(!fixture){sharingWindow=window;window.AttachSharingHome(this);}
                    window.Shown+=(s,e)=>{window.Refresh();Hide();};window.ShowDialog();action=window.ProjectAction;
                }
                if(action=="new"||action=="import"||action=="import-project"){ShowHome();project=action=="new"?CreateProject(false):ImportProject();}
                else project=action=="ready"?CreateProject(true):action!=null&&action.StartsWith("project/",StringComparison.Ordinal)?store.Read().Projects.FirstOrDefault(item=>item.Id==action.Substring(8)):null;
            }catch{ShowHome();throw;}
        }
        ShowHome();
    }
    internal void DetachSharingWindow(GeneralModWindow window){
        if(sharingWindow==window)sharingWindow=null;
    }

    Control SharingPromptOwner {
        get{
            if(sharingDialog!=null&&!sharingDialog.IsDisposed&&sharingDialog.Visible)return sharingDialog;
            var window=sharingWindow;
            return window!=null&&!window.IsDisposed?window.SharingPromptOwner:this;
        }
    }

    internal P2PService SharingService(string projectData){
        P2PService service;
        if(sharing.TryGetValue(projectData,out service))return service;
        service=new P2PService(Path.Combine(projectData,"P2P"),
            fixture?Path.Combine(store.Root,"P2P-Transfers"):P2PService.TransferStore,localOnly:fixture,offerRecovery:true);
        service.EnableInstalledReceive(catalog,projectData,fixture);
        service.ConfirmConnection=session=>GeneralP2PDialog.AskConnection(this,()=>SharingPromptOwner,service,session);
        service.ApproveUserFolders=folders=>GeneralP2PDialog.AskUserFolders(this,()=>SharingPromptOwner,folders);
        service.RegisterProject=session=>{
            if(IsDisposed||!IsHandleCreated)return false;
            return (bool)Invoke((Func<bool>)(()=>{
                var window=sharingWindow;
                if(window!=null&&!window.IsDisposed&&!window.CanApplyReceivedProject)return false;
                var received=store.CreateReceived(session,projectData,catalog);
                if(window!=null&&!window.IsDisposed)window.ReloadReceivedProject(received);
                if(Visible)Build();
                return true;
            }));
        };
        service.ChooseVersion=difference=>{
            if(IsDisposed||!IsHandleCreated)return false;
            return (bool)Invoke((Func<bool>)(()=>GeneralVersionChoiceDialog.Ask(SharingPromptOwner,difference)));
        };
        sharing.Add(projectData,service);
        return service;
    }

    async Task RestoreSharing(){
        await RestoreSharing(ReceiveData,null);
        foreach(var project in store.Read().Projects)
            await RestoreSharing(store.DataPath(project),project);
    }

    async Task RestoreSharing(string projectData,GeneralProject project){
        if(IsDisposed||!File.Exists(Path.Combine(projectData,"P2P","sessions.json")))return;
        try{
            var service=SharingService(projectData);
            if(!service.SnapshotSessions().Any(s=>!s.Completed&&!s.Discarded||Directory.Exists(service.Folder(s))||s.Lifecycle&&(s.Sender?s.Recipients.Any(peer=>!peer.ReceiptClosed):!s.ReceiptClosed)||!s.Sender&&s.Completed&&!s.ListImported))return;
            if(project!=null)service.SetPreparationState(store.Load(project));
            await RecoverSharing(this,service);
            await Task.Run(()=>{
                foreach(var session in service.SnapshotSessions().Where(s=>!s.Sender&&s.Completed&&!s.ListImported))
                    service.PublishProject(session,true);
            });
            if(!IsDisposed)await Task.Run(()=>service.Start(_=>{}));
        }catch(Exception ex){
            if(!IsDisposed)GeneralDialog.Message(this,ex.Message,T("Retomada P2P","P2P recovery"),MessageBoxButtons.OK,MessageBoxIcon.Warning);
        }
    }

    internal static async Task RecoverSharing(Control owner,P2PService service){
        foreach(var session in service.SnapshotSessions().Where(s=>s.RecoveryPending)){
            if(owner.IsDisposed)return;
            service.Notify();
            using(var dialog=new GeneralDialog(T("Recebimento interrompido","Interrupted download"))){
                dialog.ClientSize=new Size(650,284);
                var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new Padding(24,12,24,20)};
                layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                var message=new Label{Dock=DockStyle.Fill,Text=session.Name+"\r\n\r\n"+T(
                    "O download ou a instalação foi interrompido. Preserve o arquivo para retomar pelo mesmo compartilhamento ou descarte o recebimento e reverta as alterações registradas.",
                    "The download or installation was interrupted. Keep the file to resume the same transfer, or discard it and roll back recorded changes.")};
                var actions=GeneralTableDialogLayout.Commands();
                var keep=GeneralStyle.Button(T("Preservar para depois","Keep for later"),()=>dialog.Complete(DialogResult.No),true);
                var discard=GeneralStyle.Button(T("Descartar recebimento","Discard download"),()=>dialog.Complete(DialogResult.Yes));
                actions.Controls.Add(keep);actions.Controls.Add(discard);
                layout.Controls.Add(message,0,0);layout.Controls.Add(actions,0,1);dialog.Content.Controls.Add(layout);
                dialog.AcceptButton=keep;dialog.CancelButton=keep;GeneralStyle.Apply(dialog,GeneralStyle.Dark);
                if(dialog.ShowDialog(owner)==DialogResult.Yes){
                    try{await Task.Run(()=>service.DiscardIncoming(session));}
                    catch(Exception ex){if(!owner.IsDisposed)GeneralDialog.Message(owner,ex.Message,T("Recebimento preservado","Download preserved"),MessageBoxButtons.OK,MessageBoxIcon.Warning);}
                }else await Task.Run(()=>service.PreserveInterrupted(session));
            }
        }
    }

    void ShowHome(){if(IsDisposed)return;Build();Show();Activate();}
    void ToggleHomeMenu(){
        var anchor=Controls.Find("HomeMenu",true).Single();
        menus.Toggle(this,anchor,()=>new GeneralActionsMenu(home:true),dialog=>Guard(()=>{
            if(((GeneralActionsMenu)dialog).ActionId=="share")ShowSharing();
            else Options(anchor);
        }));
    }

    void ShowSharing(){
        var service=SharingService(ReceiveData);
        using(var dialog=new GeneralP2PDialog(service,new GeneralModState(),catalog)){
            sharingDialog=dialog;
            try{dialog.ShowDialog(this);}
            finally{sharingDialog=null;}
        }
    }

    void Options(Control anchor){
        var preferences=store.Read();using(var dialog=new GeneralOptionsDialog(preferences.Theme,preferences.Language,preferences.DownloadFolder,cache.PreviewRetainedDownloads)){
            dialog.AnchorTo(anchor);if(dialog.ShowDialog(this)!=DialogResult.OK){RefreshStorageSize();return;}
            store.SavePreferences(dialog.ThemeValue,dialog.LanguageValue,dialog.DownloadFolder);Build();
        }
    }
    void Guard(Action action){int before=buildVersion;try{action();if(Visible&&before==buildVersion)Build();}catch(Exception ex){GeneralDialog.Message(this,ex.Message,T("Não foi possível continuar","Unable to continue"),MessageBoxButtons.OK,MessageBoxIcon.Warning);}}
    sealed class Choice {internal readonly GeneralProject Project;internal Choice(GeneralProject project){Project=project;}public override string ToString(){return Project.Name;}}
}

internal sealed partial class GeneralModWindow {
    GeneralProjectStore projectStore;
    GeneralProject project;
    internal string ProjectAction {get;private set;}
    internal void AttachProject(GeneralProjectStore store,GeneralProject project){
        projectStore=store;this.project=project;state.TemplateId=project.TemplateId;state.ProjectName=project.Name;var preferences=store.Read();
        bool refreshed=false;
        if(BuiltInProject){
            if(seed.Mods.Any(item=>GeneralModLibrary.IsFacialMod(item.Id))){
                if(!state.List.Categories.Contains(GeneralModLibrary.BodyCategory)){
                    state.List.Categories.Add(GeneralModLibrary.BodyCategory);
                    refreshed=true;
                }
                foreach(var item in state.List.Mods.Where(item=>GeneralModLibrary.IsFacialMod(item.Id)&&item.Category=="Aparência")){
                    item.Category=GeneralModLibrary.BodyCategory;
                    refreshed=true;
                }
            }
            var added=seed.Mods.Where(item=>!state.List.Mods.Any(current=>current.Id==item.Id)).ToArray();
            if(added.Length!=0){
                state.List.Mods.AddRange(added);
                foreach(var category in seed.Categories)
                    if(!state.List.Categories.Contains(category))state.List.Categories.Add(category);
                foreach(var reference in seed.Presets){
                    var saved=state.List.Presets.FirstOrDefault(p=>p.Id==reference.Id);
                    if(saved!=null)saved.ModIds.AddRange(added.Where(item=>reference.ModIds.Contains(item.Id)).Select(item=>item.Id));
                }
                refreshed=true;
            }
            const string eclipse=GeneralModLibrary.EclipseTattooId;
            foreach(var reference in seed.Presets.Where(p=>p.ModIds.Contains(eclipse))){
                var saved=state.List.Presets.FirstOrDefault(p=>p.Id==reference.Id);
                if(saved==null||saved.ModIds.Contains(eclipse))continue;
                saved.ModIds.Add(eclipse);
                refreshed=true;
            }
            if(GeneralModLibrary.GroupBodyOptions(state.List))refreshed=true;
        }
        if(refreshed||state.Theme!=preferences.Theme||state.Language!=preferences.Language){
            state.Theme=preferences.Theme;state.Language=preferences.Language;Persist();BuildInterface();
        }
        Text=GeneralAppPaths.WindowTitle+" — "+project.Name;
        UpdateTableTitle();
    }
    bool BuiltInProject {get{return project!=null&&!String.IsNullOrEmpty(project.TemplateId);}}
    void BackToProjects(){if(working||scanning)return;Close();}
    void ProjectCommand(string action){
        if(working||scanning)return;
        if(BuiltInProject&&(action=="export"||action=="import"||action=="delete-project"))return;
        if(action=="clone"){
            if(projectStore==null||project==null||!ConfirmProjectNavigation())return;
            string initial=T("Cópia de ","Copy of ")+project.Name;initial=initial.Substring(0,Math.Min(120,initial.Length));
            using(var name=new GeneralNameDialog(T("Duplicar projeto","Duplicate project"),initial,T("A cópia usa a mesma pasta do jogo. Os arquivos dos mods não são duplicados.","The copy uses the same game folder. Mod files are not duplicated."))){
                if(name.ShowDialog(this)!=DialogResult.OK)return;
                var copy=projectStore.Duplicate(project,name.ResultName,catalog);ProjectCommand("project/"+copy.Id);
            }
            return;
        }
        if(action=="export"){
            using(var save=new SaveFileDialog{Filter=T("Projeto Mod Loader|*.json","Mod Loader project|*.json"),FileName=project==null?"projeto.json":String.Concat(project.Name.Where(c=>!Path.GetInvalidFileNameChars().Contains(c)))+".json"})
                if(save.ShowDialog(this)==DialogResult.OK)library.Export(save.FileName,state.List);
            return;
        }
        if(action=="delete-project"){
            if(!ConfirmProjectNavigation())return;
            if(projectStore==null||project==null)return;
            if(GeneralDialog.Message(this,T("Excluir este projeto da lista de projetos salvos? Mods instalados, registros e backups serão preservados.","Remove this project from saved projects? Installed mods, records and backups will be preserved.")+"\n\n"+project.Name,T("Excluir projeto","Delete project"),MessageBoxButtons.OKCancel,MessageBoxIcon.Warning)!=DialogResult.OK)return;
            projectStore.Delete(project);
        }
        ProjectAction=action;Close();if(!IsDisposed&&Visible)ProjectAction=null;
    }
}
