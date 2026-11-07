using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class GeneralP2PDialog : GeneralDialog {
    readonly P2PService service;
    readonly GeneralModState state;
    readonly bool autoStartEnabled;
    readonly FixedCatalog catalog;
    readonly string[] ids;
    readonly TextBox output=new TextBox{ReadOnly=true,WordWrap=false,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None};
    readonly TextBox input=new TextBox{WordWrap=false,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,MaxLength=12000};
    readonly TextBox destination=new TextBox{ReadOnly=true,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None};
    readonly Label status=new Label{Dock=DockStyle.Fill,UseMnemonic=false};
    readonly GeneralProgress preparation=new GeneralProgress{Dock=DockStyle.Fill,Height=44,Name="p2p-preparation"};
    readonly GeneralProgress progress=new GeneralProgress{Dock=DockStyle.Fill,Height=44,Name="p2p-progress"};
    readonly Timer timer=new Timer{Interval=1000};
    readonly TableLayoutPanel body;
    readonly Panel targetRow;
    readonly TableLayoutPanel transferRow;
    readonly Control sendLabel,sendField,receiveLabel,receiveField;
    readonly Button connect,pause,choose,generate,copy,newSend,background,discard,importList;
    readonly GeneralPicker transfers=new GeneralPicker{Dock=DockStyle.Fill,Name="p2p-transfers"};
    P2PSession[] displayed=new P2PSession[0];
    bool selecting;
    readonly ToolTip tips=new ToolTip{UseAnimation=false,UseFading=false};
    P2PSession selected;
    bool busy,polling,remotePaused,returning,preparing;
    internal GeneralModList ReceivedList {get;private set;}
    internal bool InstallAfterReceive {get{return false;}}
    internal string InstalledReceiptId {get;private set;}
    internal string InstalledDestination {get;private set;}
    static string T(string pt,string en){return GeneralStyle.T(pt,en);}
    internal GeneralP2PDialog(P2PService service,GeneralModState state,System.Collections.Generic.IEnumerable<string> ids,FixedCatalog catalog,DownloadCache cache,bool autoStart=true):base(T("Modpack e compartilhamento","Modpack and sharing")){
        this.service=service;this.state=state;autoStartEnabled=autoStart;this.ids=ids.ToArray();this.catalog=catalog;service.SetPreparationState(state);
        var previousChoice=service.ChooseVersion;Func<ModVersionDifference,bool> versionChoice=difference=>GeneralVersionChoiceDialog.Ask(this,difference);service.ChooseVersion=versionChoice;
        Disposed+=(s,e)=>{if(Object.ReferenceEquals(service.ChooseVersion,versionChoice))service.ChooseVersion=previousChoice;};
        ClientSize=new Size(720,620);Name="p2p-dialog";
        body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=11,Padding=new Padding(24,8,24,20),Margin=Padding.Empty};
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        foreach(int height in new[]{24,64,24,64,76,56,50,50,80,52})body.RowStyles.Add(new RowStyle(SizeType.Absolute,height));body.RowStyles.Add(new RowStyle(SizeType.Percent,100));Content.Controls.Add(body);
        sendLabel=Heading(T("Link para enviar ao amigo","Link to send to your friend"));body.Controls.Add(sendLabel,0,0);
        var send=Field(output);sendField=send;send.Padding=new Padding(14,16,72,8);
        copy=GeneralStyle.Button("",()=>{if(output.Text.StartsWith("ML-P2P1:",StringComparison.Ordinal))try{Clipboard.SetText(output.Text);}catch(Exception ex){status.Text=ex.Message;}});
        ConfigureIcon(copy,"copy",T("Copiar link","Copy link"));copy.Name="p2p-copy";
        send.Controls.Add(copy);send.SizeChanged+=(s,e)=>copy.Location=new Point(send.ClientSize.Width-62,6);body.Controls.Add(send,0,1);
        receiveLabel=Heading(T("Link recebido do amigo","Link received from your friend"));body.Controls.Add(receiveLabel,0,2);var receive=Field(input);receiveField=receive;receive.Padding=new Padding(14,16,14,8);body.Controls.Add(receive,0,3);
        targetRow=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};var target=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=3,Margin=Padding.Empty};target.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));target.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));target.RowStyles.Add(new RowStyle(SizeType.Absolute,24));target.RowStyles.Add(new RowStyle(SizeType.Absolute,44));target.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        target.Controls.Add(Heading(T("Instalar na pasta local","Install into local folder")),0,0);target.SetColumnSpan(target.GetControlFromPosition(0,0),2);var destinationField=Field(destination);destinationField.Margin=Padding.Empty;target.Controls.Add(destinationField,0,1);
        choose=GeneralStyle.Button(T("Escolher pasta","Choose folder"),ChooseFolder);choose.Margin=new Padding(8,0,0,0);choose.Name="p2p-folder";target.Controls.Add(choose,1,1);targetRow.Controls.Add(target);body.Controls.Add(targetRow,0,4);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Margin=Padding.Empty};
        connect=GeneralStyle.Button(T("Conectar","Connect"),()=>Run(Connect),true);connect.Name="p2p-connect";connect.Margin=new Padding(8,0,0,0);actions.Controls.Add(connect);
        generate=GeneralStyle.Button(T("Gerar modpack","Generate modpack"),()=>Run(GenerateModpack));generate.Name="modpack-generate";generate.Margin=Padding.Empty;actions.Controls.Add(generate);body.Controls.Add(actions,0,5);
        body.Controls.Add(preparation,0,6);
        transferRow=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};transferRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));transferRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,52));transferRow.RowStyles.Add(new RowStyle(SizeType.Percent,100));transferRow.Controls.Add(progress,0,0);
        pause=GeneralStyle.Button("",()=>Run(()=>{var session=selected;return Task.Run(()=>service.Pause(session));}));ConfigureIcon(pause,"pause",T("Pausar","Pause"));pause.Name="p2p-pause";pause.Margin=new Padding(8,0,0,0);transferRow.Controls.Add(pause,1,0);body.Controls.Add(transferRow,0,7);body.Controls.Add(status,0,8);
        var selectionRow=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Margin=Padding.Empty};
        selectionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        selectionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        newSend=GeneralStyle.Button(T("Novo envio","New send"),()=>Run(NewSend));
        newSend.Margin=new Padding(8,0,0,0);
        selectionRow.Controls.Add(transfers,0,0);selectionRow.Controls.Add(newSend,1,0);
        // Keep selection separate from the invite fields and transfer progress.
        body.Controls.Add(selectionRow,0,9);
        var sessionActions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty};
        background=GeneralStyle.Button(T("Segundo plano","Run in background"),()=>Complete(DialogResult.Cancel));
        discard=GeneralStyle.Button(T("Descartar recebimento","Discard download"),()=>Run(DiscardSelected));
        importList=GeneralStyle.Button(T("Importar lista","Import list"),ImportSelected);
        importList.Name="p2p-import-list";
        sessionActions.Controls.Add(background);sessionActions.Controls.Add(discard);sessionActions.Controls.Add(importList);
        body.Controls.Add(sessionActions,0,10);
        transfers.SelectedIndexChanged+=(s,e)=>{
            if(!selecting&&transfers.SelectedIndex>=0)SelectSession(displayed[transfers.SelectedIndex]);
        };
        output.Name="p2p-output";input.Name="p2p-input";destination.Name="p2p-destination";status.Name="p2p-status";destination.Text=state.Destination??"";
        output.TextChanged+=(s,e)=>UpdateActions();input.TextChanged+=(s,e)=>UpdateActions();timer.Tick+=async(s,e)=>await Poll();
        var sessions=service.SnapshotSessions();
        selected=sessions.FirstOrDefault(s=>s.Id==service.ActiveSelection)
            ??sessions.LastOrDefault(s=>!s.Completed&&!s.Discarded)
            ??sessions.LastOrDefault(s=>!s.Sender&&s.Completed&&!s.ListImported);
        FormClosing+=(s,e)=>{if(busy)e.Cancel=true;};FormClosed+=(s,e)=>timer.Stop();Disposed+=(s,e)=>{timer.Dispose();tips.Dispose();};
        GeneralStyle.Apply(this,GeneralStyle.Dark);status.Text=T("Envie o convite. Quando seu amigo aceitar, a preparação começa automaticamente.","Send the invitation. Preparation starts automatically when your friend accepts.");UpdateActions();
        if(autoStart){output.Text=T("Iniciando conexão…","Starting connection…");Shown+=(s,e)=>Run(PrepareLink,false);}
    }
    void ConfigureIcon(Button button,string kind,string label){var icon=(GeneralButton)button;icon.IconKind=kind;icon.Bare=true;button.AutoSize=false;button.MinimumSize=new Size(44,44);button.Size=button.MinimumSize;button.Margin=Padding.Empty;button.AccessibleName=label;tips.SetToolTip(button,label);}
    static Label Heading(string text){return new Label{Text=text,Dock=DockStyle.Fill,Font=GeneralStyle.Font(11),Margin=Padding.Empty};}
    static GeneralSurface Field(Control control){var field=new GeneralSurface{Dock=DockStyle.Fill,Padding=new Padding(14,9,14,9),Margin=new Padding(0,0,0,8)};field.Controls.Add(control);return field;}
    void Report(string text){if(!IsDisposed&&IsHandleCreated)BeginInvoke((Action)(()=>{if(!IsDisposed)status.Text=preparing?T("Amigo conectado. ","Friend connected. ")+text:text;}));}
    void Failed(Exception ex){if(!output.Text.StartsWith("ML-P2P1:",StringComparison.Ordinal))output.Text=T("Não foi possível preparar o convite.","Could not prepare the invitation.");OnlineDiagnostics.Record("p2p.link.failed",null,ex.Message,new{ErrorType=ex.GetType().Name});status.Text=T("Falha: ","Failed: ")+ex.Message;status.ForeColor=GeneralStyle.Danger;}
    async void Run(Func<Task> action,bool showReview=true){if(busy)return;busy=true;UpdateActions();try{while(polling)await Task.Delay(40);await action();}catch(InstalledSnapshotReviewException ex){Failed(ex);if(showReview)GeneralDialog.Message(this,ex.Message,T("Conferência dos mods","Mod verification"),MessageBoxButtons.OK,MessageBoxIcon.Warning);}catch(Exception ex){Failed(ex);}finally{busy=false;if(!IsDisposed)UpdateActions();}}
    void Row(int row,bool visible,int height,params Control[] controls){foreach(var control in controls)control.Visible=visible;body.RowStyles[row].Height=visible?height:0;}
    void UpdateActions(){
        RefreshTransfers();
        generate.Enabled=!busy&&!preparing&&!service.Installing;
        newSend.Enabled=!busy&&ids.Length!=0;
        background.Enabled=!busy;
        discard.Visible=selected!=null&&!selected.Sender&&!selected.Completed&&!selected.Discarded;
        discard.Enabled=!busy&&selected!=null&&selected.Stage==null;
        importList.Visible=selected!=null&&!selected.Sender&&selected.Completed&&!selected.ListImported;
        importList.Enabled=!busy;
        transfers.Enabled=!busy;
        copy.Enabled=output.Text.StartsWith("ML-P2P1:",StringComparison.Ordinal)&&(selected==null||!selected.Completed&&!selected.Discarded);
        bool receiving=selected!=null&&!selected.Sender;try{receiving|=P2PInvite.Decode(input.Text).Kind=="offer";}catch(IOException){}
        bool received=selected!=null&&selected.Automatic&&!selected.Sender;
        Row(0,!received,24,sendLabel);Row(1,!received,64,sendField);
        Row(2,true,24,receiveLabel);Row(3,true,64,receiveField);
        input.ReadOnly=false;
        Row(4,receiving,76,targetRow);
        bool active=selected!=null&&(selected.Peer!=null||selected.Recipients.Length!=0||selected.Stage!=null||selected.Completed);
        Row(6,active||preparing,50,preparation);Row(7,active||preparing,50,transferRow);
        bool retry=selected!=null&&(selected.Held||selected.Error!=null);
        connect.Text=retry&&String.IsNullOrWhiteSpace(input.Text)?T("Retomar","Resume"):T("Conectar","Connect");
        connect.Enabled=!busy&&(!String.IsNullOrWhiteSpace(input.Text)||retry||selected==null);
        if(!autoStartEnabled&&selected==null)connect.Enabled=!busy;
        choose.Enabled=!busy&&(selected==null||selected.Sender||selected.Stage==null&&selected.TransactionId==null);
        pause.Visible=selected!=null&&!selected.Completed&&!selected.Discarded&&!selected.Held;pause.Enabled=!busy&&service.Engine.Running&&selected!=null&&!selected.Completed&&(selected.Sender||selected.Stage==null)&&(!remotePaused||selected.Paused);
        var icon=(GeneralButton)pause;icon.IconKind=selected!=null&&selected.Paused?"play":"pause";pause.AccessibleName=selected!=null&&selected.Paused?T("Retomar","Resume"):remotePaused?T("Pausado pelo amigo","Paused by friend"):T("Pausar","Pause");tips.SetToolTip(pause,pause.AccessibleName);pause.Invalidate();
        int height=56+body.Padding.Vertical+(int)body.RowStyles.Cast<RowStyle>().Sum(row=>row.Height);if(ClientSize.Height!=height)ClientSize=new Size(ClientSize.Width,height);
    }
    void RefreshTransfers(){
        var sessions=service.SnapshotSessions().Where(s=>!s.Discarded).ToArray();
        int index=Array.IndexOf(sessions,selected);
        selecting=true;
        try{
            if(!displayed.SequenceEqual(sessions)){
                displayed=sessions;
                transfers.Items.Clear();
                for(int i=0;i<sessions.Length;i++)transfers.Items.Add((i+1)+". "+sessions[i]);
            }
            transfers.SelectedIndex=index;
        }finally{selecting=false;}
    }
    async Task NewSend(){
        if(!service.Engine.Running)await Task.Run(()=>service.Start(Report));
        service.SetPreparationState(state);
        var session=await Task.Run(()=>service.BeginDeferred(state,ids,true));
        SelectSession(session);timer.Start();
        status.Text=T("Convite pronto para até três amigos.","Invitation ready for up to three friends.");
    }
    async Task DiscardSelected(){
        var session=selected;
        if(session==null||GeneralDialog.Message(this,T("Descartar o arquivo recebido e reverter a parte registrada de uma instalação interrompida?","Discard the download and roll back the recorded part of an interrupted installation?"),T("Descartar recebimento","Discard download"),MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
        await Task.Run(()=>service.DiscardIncoming(session));
        SelectSession(service.SnapshotSessions().LastOrDefault(s=>!s.Discarded));
    }
    void ImportSelected(){
        var session=selected;
        if(session==null||session.Sender||!session.Completed)return;
        try{
            var manifest=service.InstalledManifest(session);
            ReceivedList=manifest.Project??manifest.List;
            InstalledReceiptId=session.ReceiptId;InstalledDestination=session.Destination;
            returning=true;Complete(DialogResult.OK);
        }catch(Exception ex){Failed(ex);}
    }
    void SelectSession(P2PSession session){
        selected=session;
        service.ActiveSelection=session==null?null:session.Id;
        remotePaused=false;
        preparation.Value=0;preparation.Indeterminate=false;
        progress.Value=0;progress.Indeterminate=false;
        status.Text="";
        status.ForeColor=GeneralStyle.Foreground;
        destination.Text=session!=null&&!session.Sender?session.Destination??state.Destination??"":state.Destination??"";
        output.Text=session==null||!P2PInvite.DeviceValid(service.Engine.Device)?"":service.Invitation(session);
        UpdateActions();
    }
    void ChooseFolder(){using(var folder=new FolderBrowserDialog{Description=T("Escolha a pasta local de instalação.","Choose the local installation folder."),SelectedPath=Directory.Exists(destination.Text)?destination.Text:""})if(folder.ShowDialog(this)==DialogResult.OK)destination.Text=folder.SelectedPath;}
    async Task PrepareLink(){
        if(ids.Length==0&&selected==null){output.Text=T("Adicione mods para enviar ou cole um convite abaixo.","Add mods to send or paste an invitation below.");status.Text=T("Cole o convite do amigo e clique em Conectar.","Paste your friend's invitation and click Connect.");return;}
        if(!service.Engine.Running)await Task.Run(()=>service.Start(Report));timer.Start();
        if(selected!=null){bool upgraded=await Task.Run(()=>service.UpgradeInvitation(selected));SelectSession(selected);if(upgraded)status.Text=T("Convite atualizado. Copie este novo código e envie ao amigo para conectar automaticamente.","Invitation updated. Copy this new code and send it to your friend to connect automatically.");return;}
        await NewSend();
    }
    async Task Connect(){
        if(!service.Engine.Running)await Task.Run(()=>service.Start(Report));timer.Start();
        string code=input.Text.Trim();
        if(code.Length==0){
            if(selected==null){await PrepareLink();return;}
            if(!selected.Sender&&(selected.Error!=null||selected.Held)){string target=selected.Destination;await Task.Run(()=>service.RetryInstalled(selected,target));}
            if(selected.Sender&&selected.Lifecycle)service.RetryPreparation(selected);
            SelectSession(selected);
            if(selected.Sender&&!selected.Lifecycle&&selected.Deferred&&!service.DeferredPrepared(selected)){var transfer=await Task.Run(()=>service.TransferStatus(selected));if(transfer.Connected&&transfer.Accepted&&!selected.Paused&&!transfer.RemotePaused)StartPreparation(selected);}
            return;
        }
        var invite=P2PInvite.Decode(code);if(invite.Mode!="installed-zip")throw new IOException(T("Este link usa o formato antigo. Peça um novo link ao amigo.","This link uses the old format. Ask your friend for a new link."));
        if(invite.Kind=="offer"){
            if(!Directory.Exists(destination.Text))ChooseFolder();if(!Directory.Exists(destination.Text))throw new IOException(T("Escolha a pasta de instalação.","Choose the installation folder."));string target=destination.Text;
            if(GeneralDialog.Message(this,T("Receber e instalar o pacote ","Receive and install pack ")+invite.Name+T(" em:\n"," into:\n")+target+T("\n\nMantenha o jogo fechado. Configurações anteriores terão backup.","\n\nKeep the game closed. Previous settings will be backed up."),T("Conectar e instalar","Connect and install"),MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
            var session=await Task.Run(()=>{var received=service.Accept(code,target,true);if(!received.Completed&&(received.Held||received.Error!=null||!received.AutoInstall))service.RetryInstalled(received,target);return received;});SelectSession(session);status.Text=session.Automatic?T("Convite aceito. Conectando ao amigo automaticamente…","Invitation accepted. Connecting to your friend automatically…"):T("Envie o link de resposta acima ao amigo.","Send the reply link above to your friend.");
            input.Clear();
        }else{var session=await Task.Run(()=>service.Authorize(code));SelectSession(session);input.Clear();}
    }
    async void StartPreparation(P2PSession session){
        if(preparing)return;preparing=true;preparation.Value=0;UpdateActions();
        try{await PrepareFiles(session);}catch(Exception ex){if(!IsDisposed)Failed(ex);}finally{preparing=false;if(!IsDisposed)UpdateActions();}
    }
    async Task PrepareFiles(P2PSession session){await Task.Run(()=>service.PrepareDeferred(session,state,catalog,service.ApplicationData,true,Report));}
    async Task GenerateModpack(){
        status.Text=T("Conferindo os mods antes de gerar o modpack…","Verifying mods before creating the modpack…");
        var manifest=await Task.Run(()=>InstalledSnapshot.Inspect(state,ids,catalog,service.ApplicationData,true,true));
        using(var save=new SaveFileDialog{Title=T("Salvar modpack","Save modpack"),Filter="Modpack ZIP|*.zip",FileName="Modpack.zip",OverwritePrompt=false}){
            if(save.ShowDialog(this)!=DialogResult.OK)return;
            if(File.Exists(save.FileName))throw new IOException(T("Escolha um nome que ainda não exista.","Choose a name that does not already exist."));
            string output=save.FileName;
            await Task.Run(()=>InstalledSnapshot.CreateModpack(output,manifest,state.Destination,catalog,Report));
            progress.Value=100;progress.Caption=T("Modpack gerado","Modpack created");status.Text=T("ZIP pronto. Extraia o conteúdo na pasta raiz do jogo de destino: ","ZIP ready. Extract its contents into the destination game's root folder: ")+output;
        }
    }
    async Task Poll(){
        if(busy||polling||!service.Engine.Running||selected==null||returning)return;polling=true;var session=selected;
        try{
            if(session.Lifecycle&&session.Sender){
                var outgoing=await Task.Run(()=>service.TransferStatus(session));
                if(IsDisposed||selected!=session)return;
                preparation.Indeterminate=session.Stage!=null&&session.PreparationTotal==0;
                preparation.Value=session.PreparationTotal>0?(int)(session.PreparationBytes*100/session.PreparationTotal):session.PayloadHash!=null?100:0;
                preparation.Caption=session.Stage!=null?T("Preparando pacote","Preparing pack"):T("Preparação","Preparation");
                progress.Value=outgoing.Percent;progress.Caption=T("Transferência do pacote","Pack transfer");
                status.ForeColor=session.Error==null?GeneralStyle.Foreground:GeneralStyle.Danger;
                status.Text=session.Error??(outgoing.Recipients.Length==0
                    ?T("Aguardando amigos aceitarem o convite.","Waiting for friends to accept the invitation.")
                    :String.Join("\r\n",outgoing.Recipients));
                return;
            }
            if(session.Completed){preparation.Indeterminate=false;preparation.Value=100;preparation.Caption=T("Pacote preparado","Pack prepared");
                if(Directory.Exists(service.Folder(session))){progress.Value=99;progress.Caption=T("Finalizando limpeza","Finishing cleanup");status.Text=T("Finalizando a limpeza do arquivo temporário…","Finishing temporary archive cleanup…");return;}
                progress.Value=100;progress.Caption=session.Sender?T("Envio concluído","Send complete"):T("Instalação concluída","Installation complete");status.Text=T("Concluído. Arquivo temporário removido.","Complete. Temporary archive removed.");
                return;
            }
            if(session.Held||session.RecoveryPending){
                status.Text=T("Arquivo preservado. Clique em Retomar quando quiser continuar.","File preserved. Click Resume when you are ready.");
                return;
            }
            if(session.Phase!=null&&!session.Sender){
                progress.Value=100;progress.Caption=T("Download concluído","Download complete");
                status.Text=P2PService.PhaseLabel(session.Phase)+(session.Error==null?"":"\r\n"+session.Error);
                return;
            }
            if(session.Error!=null&&!preparing){status.Text=T("Falha: ","Failed: ")+session.Error+T(" Use Conectar para tentar novamente."," Use Connect to retry.");status.ForeColor=GeneralStyle.Danger;return;}
            var transfer=await Task.Run(()=>service.TransferStatus(session));if(IsDisposed||selected!=session)return;remotePaused=transfer.RemotePaused;
            bool connected=transfer.Connected&&transfer.Accepted;status.ForeColor=connected?Color.FromArgb(64,163,112):GeneralStyle.Foreground;
            if(session.Sender&&session.Deferred&&session.PayloadHash==null&&!preparing&&connected&&!session.Paused&&!remotePaused)StartPreparation(session);
            preparation.Indeterminate=preparing&&session.PreparationTotal==0;
            preparation.Value=session.PreparationTotal>0?(int)Math.Min(100,session.PreparationBytes*100/session.PreparationTotal):session.PayloadHash!=null?100:0;
            preparation.Caption=preparing?T("Preparando pacote","Preparing pack"):session.PayloadHash!=null?T("Pacote preparado","Pack prepared"):session.Sender?T("Aguardando conexão","Waiting for connection"):T("Aguardando preparo do amigo","Waiting for your friend's pack");preparation.Invalidate();
            progress.Value=transfer.Percent;progress.Caption=session.Sender?T("Envio do pacote","Pack upload"):T("Recebimento do pacote","Pack download");progress.Indeterminate=false;
            status.Text=session.Paused?T("Pausado por você. Só você pode retomar sua pausa.","Paused by you. Only you can release your pause."):remotePaused?T("Pausado pelo amigo. Aguarde ele retomar.","Paused by your friend. Wait for them to resume."):!connected?session.Peer==null?T("Aguardando seu amigo aceitar o convite.","Waiting for your friend to accept the invitation."):T("Aguardando conexão do amigo. A sessão foi preservada.","Waiting for your friend's connection. The session is preserved."):preparing?T("Amigo conectado. Preparando os arquivos para envio…","Friend connected. Preparing files to send…"):session.Stage!=null?session.Stage:session.PayloadHash==null?T("Conectado. Aguardando o preparo do pacote…","Connected. Waiting for pack preparation…"):session.Sender?T("Amigo conectado. Enviando pacote…","Friend connected. Sending pack…"):T("Conectado. Recebendo; a instalação começa após a conferência.","Connected. Receiving; installation starts after verification.");
        }catch(Exception){status.Text=T("Conexão interrompida. Tentando retomar; o progresso foi preservado.","Connection interrupted. Retrying; progress was preserved.");}
        finally{polling=false;if(!IsDisposed)UpdateActions();}
    }
}

internal sealed partial class GeneralModWindow {
    P2PService p2p;
    GeneralProjectHome sharingHome;
    GeneralP2PDialog sharingDialog;

    internal void AttachSharingHome(GeneralProjectHome owner){
        sharingHome=owner;
        FormClosed+=(s,e)=>owner.DetachSharingWindow(this);
    }

    internal Control SharingPromptOwner {
        get{return sharingDialog!=null&&!sharingDialog.IsDisposed&&sharingDialog.Visible?(Control)sharingDialog:this;}
    }
    void PrepareP2P(){
            if(p2p==null){
                if(sharingHome!=null)p2p=sharingHome.SharingService(data);
                else{
                    p2p=new P2PService(Path.Combine(data,"P2P"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Mod Loader","Pacotes P2P"),offerRecovery:true);
                    p2p.EnableInstalledReceive(catalog,data);
                    p2p.ChooseVersion=difference=>GeneralVersionChoiceDialog.Ask(SharingPromptOwner,difference);
                }
                p2p.SetPreparationState(state);
                Action<P2PSession> installed=session=>{
                    if(IsDisposed||!IsHandleCreated)return;
                    BeginInvoke((Action)(async()=>{
                        try{
                            if(String.Equals(state.Destination,session.Destination,StringComparison.OrdinalIgnoreCase)){
                                var received=p2p.InstalledManifest(session);
                                foreach(var mod in received.List.Mods)if(state.List.Mods.Any(m=>m.Id==mod.Id&&m.Variants.Any(v=>v.Id==mod.DefaultVariant)))state.SnapshotReceipts[mod.Id+"/"+mod.DefaultVariant]=session.ReceiptId;
                                Persist();if(!working&&!scanning)await Scan();
                            }
                            detail.Text=T("Pacote recebido, instalado e conferido em: ","Pack received, installed and verified in: ")+session.Destination;
                        }catch(Exception ex){ShowError(ex);}
                    }));
                };
                p2p.SnapshotInstalled+=installed;
                FormClosed+=(s,e)=>{
                    p2p.SnapshotInstalled-=installed;
                    if(sharingHome==null)try{p2p.Dispose();}catch(Exception){}
                };
            }
    }
    async Task ResumeP2P(){
        if(testMode||!File.Exists(Path.Combine(data,"P2P","sessions.json")))return;
        try{
            PrepareP2P();
            if(sharingHome==null)await GeneralProjectHome.RecoverSharing(this,p2p);
            if(!p2p.SnapshotSessions().Any(s=>!s.Completed&&!s.Discarded||Directory.Exists(p2p.Folder(s))))return;
            await Task.Run(()=>p2p.Start(_=>{}));
            if(!IsDisposed)detail.Text=T("Compartilhamento P2P retomado.","P2P sharing resumed.");
        }
        catch(Exception ex){if(!IsDisposed)detail.Text=T("Retomada P2P: ","P2P resume: ")+ex.Message;}
    }

    async void ShowP2P(){
        if(working||scanning)return;
        try{
            PrepareP2P();
            var chosen=state.List.Presets.FirstOrDefault(p=>p.Id==state.SelectedPreset);
            var ids=chosen==null?state.List.Mods.Select(m=>m.Id):chosen.ModIds;
            using(var dialog=new GeneralP2PDialog(p2p,state,ids,catalog,cache)){
                DialogResult result;
                sharingDialog=dialog;
                try{result=dialog.ShowDialog(this);}
                finally{sharingDialog=null;}
                if(result!=DialogResult.OK||dialog.ReceivedList==null)return;
                if(!await ReplaceList(dialog.ReceivedList,true,dialog.InstalledReceiptId,dialog.InstalledDestination))return;
                if(dialog.InstalledReceiptId!=null)p2p.MarkImported(dialog.InstalledReceiptId);
                if(dialog.InstallAfterReceive){foreach(var mod in state.List.Mods)marked.Add(mod.Id);DrawRows();StartSelected();}
                else detail.Text=T("Pacote instalado e conferido; presets e nomes importados.","Pack installed and verified; presets and names imported.");
            }
        }catch(Exception ex){ShowError(ex);}
    }
}
