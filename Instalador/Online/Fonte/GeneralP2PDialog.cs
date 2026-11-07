using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class GeneralP2PDialog : GeneralDialog {
    readonly P2PService service;
    readonly GeneralModState state;
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
    readonly Button connect,pause,choose,generate,copy;
    readonly ToolTip tips=new ToolTip{UseAnimation=false,UseFading=false};
    P2PSession selected;
    bool busy,polling,remotePaused,returning,preparing;
    internal GeneralModList ReceivedList {get;private set;}
    internal bool InstallAfterReceive {get{return false;}}
    internal string InstalledReceiptId {get;private set;}
    internal string InstalledDestination {get;private set;}
    static string T(string pt,string en){return GeneralStyle.T(pt,en);}
    internal GeneralP2PDialog(P2PService service,GeneralModState state,System.Collections.Generic.IEnumerable<string> ids,FixedCatalog catalog,DownloadCache cache,bool autoStart=true):base(T("Modpack e compartilhamento","Modpack and sharing")){
        this.service=service;this.state=state;this.ids=ids.ToArray();this.catalog=catalog;
        var previousChoice=service.ChooseVersion;Func<ModVersionDifference,bool> versionChoice=difference=>GeneralVersionChoiceDialog.Ask(this,difference);service.ChooseVersion=versionChoice;
        Disposed+=(s,e)=>{if(Object.ReferenceEquals(service.ChooseVersion,versionChoice))service.ChooseVersion=previousChoice;};
        ClientSize=new Size(720,620);Name="p2p-dialog";
        body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=9,Padding=new Padding(24,8,24,20),Margin=Padding.Empty};
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        foreach(int height in new[]{24,64,24,64,76,56,50,50})body.RowStyles.Add(new RowStyle(SizeType.Absolute,height));body.RowStyles.Add(new RowStyle(SizeType.Percent,100));Content.Controls.Add(body);
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
        output.Name="p2p-output";input.Name="p2p-input";destination.Name="p2p-destination";status.Name="p2p-status";destination.Text=state.Destination??"";
        output.TextChanged+=(s,e)=>UpdateActions();input.TextChanged+=(s,e)=>UpdateActions();timer.Tick+=async(s,e)=>await Poll();
        selected=service.Sessions.LastOrDefault(s=>!s.Completed)??service.Sessions.LastOrDefault(s=>!s.Sender&&s.Completed&&!s.ListImported);
        FormClosing+=(s,e)=>{if(busy||preparing){e.Cancel=true;status.Text=T("Aguarde a operação terminar. Use pausa para controlar a preparação ou o envio.","Wait for the operation to finish. Use pause to control preparation or sending.");}};FormClosed+=(s,e)=>timer.Stop();Disposed+=(s,e)=>{timer.Dispose();tips.Dispose();};
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
        generate.Enabled=!busy&&!preparing&&!service.Installing;
        copy.Enabled=output.Text.StartsWith("ML-P2P1:",StringComparison.Ordinal);
        bool receiving=selected!=null&&!selected.Sender;try{receiving|=P2PInvite.Decode(input.Text).Kind=="offer";}catch(IOException){}
        bool automatic=selected!=null&&selected.Automatic,received=automatic&&!selected.Sender,hostConnected=automatic&&selected.Sender&&selected.Peer!=null;
        Row(0,!received,24,sendLabel);Row(1,!received,64,sendField);
        Row(2,!hostConnected,24,receiveLabel);Row(3,!hostConnected,64,receiveField);input.ReadOnly=received;
        Row(4,receiving,76,targetRow);
        bool active=selected!=null&&(selected.Peer!=null||selected.Stage!=null||selected.Completed);
        Row(6,active||preparing,50,preparation);Row(7,active||preparing,50,transferRow);
        connect.Visible=!hostConnected||selected.Error!=null;connect.Enabled=!busy&&!preparing&&(selected==null||selected.Stage==null);choose.Enabled=!busy&&!preparing&&(selected==null||selected.Sender||!selected.Completed&&selected.Stage==null);
        pause.Visible=selected!=null&&!selected.Completed;pause.Enabled=!busy&&service.Engine.Running&&selected!=null&&!selected.Completed&&(selected.Sender||selected.Stage==null)&&(!remotePaused||selected.Paused);
        var icon=(GeneralButton)pause;icon.IconKind=selected!=null&&selected.Paused?"play":"pause";pause.AccessibleName=selected!=null&&selected.Paused?T("Retomar","Resume"):remotePaused?T("Pausado pelo amigo","Paused by friend"):T("Pausar","Pause");tips.SetToolTip(pause,pause.AccessibleName);pause.Invalidate();
        int height=56+body.Padding.Vertical+80+(int)body.RowStyles.Cast<RowStyle>().Take(8).Sum(row=>row.Height);if(ClientSize.Height!=height)ClientSize=new Size(ClientSize.Width,height);
    }
    void SelectSession(P2PSession session){selected=session;remotePaused=false;output.Text=session==null||!P2PInvite.DeviceValid(service.Engine.Device)?"":service.Invitation(session);if(session!=null&&!session.Sender)destination.Text=session.Destination??destination.Text;UpdateActions();}
    void ChooseFolder(){using(var folder=new FolderBrowserDialog{Description=T("Escolha a pasta local de instalação.","Choose the local installation folder."),SelectedPath=Directory.Exists(destination.Text)?destination.Text:""})if(folder.ShowDialog(this)==DialogResult.OK)destination.Text=folder.SelectedPath;}
    async Task PrepareLink(){
        if(ids.Length==0&&selected==null){output.Text=T("Adicione mods para enviar ou cole um convite abaixo.","Add mods to send or paste an invitation below.");status.Text=T("Cole o convite do amigo e clique em Conectar.","Paste your friend's invitation and click Connect.");return;}
        if(!service.Engine.Running)await Task.Run(()=>service.Start(Report));timer.Start();
        if(selected!=null){bool upgraded=await Task.Run(()=>service.UpgradeInvitation(selected));SelectSession(selected);if(upgraded)status.Text=T("Convite atualizado. Copie este novo código e envie ao amigo para conectar automaticamente.","Invitation updated. Copy this new code and send it to your friend to connect automatically.");return;}
        var session=await Task.Run(()=>service.BeginDeferred(state,ids));SelectSession(session);
        status.Text=T("Convite pronto. Aguardando seu amigo aceitar; nenhum ZIP foi preparado.","Invitation ready. Waiting for your friend to accept; no ZIP has been prepared.");
    }
    async Task Connect(){
        if(!service.Engine.Running)await Task.Run(()=>service.Start(Report));timer.Start();
        string code=input.Text.Trim();
        if(code.Length==0){
            if(selected==null){await PrepareLink();return;}
            if(!selected.Sender&&selected.Error!=null){string target=destination.Text;await Task.Run(()=>service.RetryInstalled(selected,target));}
            SelectSession(selected);
            if(selected.Sender&&selected.Deferred&&!service.DeferredPrepared(selected)){var transfer=await Task.Run(()=>service.TransferStatus(selected));if(transfer.Connected&&transfer.Accepted&&!selected.Paused&&!transfer.RemotePaused)StartPreparation(selected);}
            return;
        }
        var invite=P2PInvite.Decode(code);if(invite.Mode!="installed-zip")throw new IOException(T("Este link usa o formato antigo. Peça um novo link ao amigo.","This link uses the old format. Ask your friend for a new link."));
        if(invite.Kind=="offer"){
            if(!Directory.Exists(destination.Text))ChooseFolder();if(!Directory.Exists(destination.Text))throw new IOException(T("Escolha a pasta de instalação.","Choose the installation folder."));string target=destination.Text;
            if(GeneralDialog.Message(this,T("Receber e instalar o pacote ","Receive and install pack ")+invite.Name+T(" em:\n"," into:\n")+target+T("\n\nMantenha o jogo fechado. Configurações anteriores terão backup.","\n\nKeep the game closed. Previous settings will be backed up."),T("Conectar e instalar","Connect and install"),MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
            var session=await Task.Run(()=>{var received=service.Accept(code,target,true);if(!received.Completed)service.RetryInstalled(received,target);return received;});SelectSession(session);status.Text=session.Automatic?T("Convite aceito. Conectando ao amigo automaticamente…","Invitation accepted. Connecting to your friend automatically…"):T("Envie o link de resposta acima ao amigo.","Send the reply link above to your friend.");
            if(!session.Automatic)input.Clear();
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
            if(session.Completed){preparation.Indeterminate=false;preparation.Value=100;preparation.Caption=T("Pacote preparado","Pack prepared");
                if(Directory.Exists(service.Folder(session))){progress.Value=99;progress.Caption=T("Finalizando limpeza","Finishing cleanup");status.Text=T("Finalizando a limpeza do arquivo temporário…","Finishing temporary archive cleanup…");return;}
                progress.Value=100;progress.Caption=session.Sender?T("Envio concluído","Send complete"):T("Instalação concluída","Installation complete");status.Text=T("Concluído. Arquivo temporário removido.","Complete. Temporary archive removed.");
                if(!session.Sender&&!session.ListImported){var manifest=service.InstalledManifest(session);ReceivedList=manifest.Project??manifest.List;InstalledReceiptId=session.ReceiptId;InstalledDestination=session.Destination;returning=true;Complete(DialogResult.OK);}return;
            }
            if(session.Error!=null&&!preparing){status.Text=T("Falha: ","Failed: ")+session.Error+T(" Use Conectar para tentar novamente."," Use Connect to retry.");status.ForeColor=GeneralStyle.Danger;return;}
            var transfer=await Task.Run(()=>service.TransferStatus(session));if(IsDisposed)return;remotePaused=transfer.RemotePaused;
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
    void PrepareP2P(){
            if(p2p==null){
                p2p=new P2PService(Path.Combine(data,"P2P"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Mod Loader","Pacotes P2P"));
                p2p.EnableInstalledReceive(catalog,data);
                p2p.ChooseVersion=difference=>GeneralVersionChoiceDialog.Ask(this,difference);
                p2p.SnapshotInstalled+=session=>{
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
                FormClosed+=(s,e)=>{try{p2p.Dispose();}catch(Exception){}};
            }
    }
    async Task ResumeP2P(){
        if(testMode||!File.Exists(Path.Combine(data,"P2P","sessions.json")))return;
        try{PrepareP2P();if(!p2p.Sessions.Any(s=>!s.Completed||Directory.Exists(p2p.Folder(s))))return;await Task.Run(()=>p2p.Start(_=>{}));if(!IsDisposed)detail.Text=T("Compartilhamento P2P retomado.","P2P sharing resumed.");else p2p.Dispose();}
        catch(Exception ex){if(!IsDisposed)detail.Text=T("Retomada P2P: ","P2P resume: ")+ex.Message;}
    }

    async void ShowP2P(){
        if(working||scanning)return;
        try{
            PrepareP2P();
            var chosen=state.List.Presets.FirstOrDefault(p=>p.Id==state.SelectedPreset);
            var ids=chosen==null?state.List.Mods.Select(m=>m.Id):chosen.ModIds;
            using(var dialog=new GeneralP2PDialog(p2p,state,ids,catalog,cache)){
                if(dialog.ShowDialog(this)!=DialogResult.OK||dialog.ReceivedList==null)return;
                if(!await ReplaceList(dialog.ReceivedList,true,dialog.InstalledReceiptId,dialog.InstalledDestination))return;
                if(dialog.InstalledReceiptId!=null)p2p.MarkImported(dialog.InstalledReceiptId);
                if(dialog.InstallAfterReceive){foreach(var mod in state.List.Mods)marked.Add(mod.Id);DrawRows();StartSelected();}
                else detail.Text=T("Pacote instalado e conferido; presets e nomes importados.","Pack installed and verified; presets and names imported.");
            }
        }catch(Exception ex){ShowError(ex);}
    }
}
