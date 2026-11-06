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
    readonly TextBox output=new TextBox{ReadOnly=true,Multiline=true,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None};
    readonly TextBox input=new TextBox{Multiline=true,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,MaxLength=12000};
    readonly TextBox destination=new TextBox{ReadOnly=true,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None};
    readonly Label status=new Label{Dock=DockStyle.Fill,UseMnemonic=false};
    readonly GeneralProgress progress=new GeneralProgress{Dock=DockStyle.Fill,Height=44};
    readonly Timer timer=new Timer{Interval=1000};
    readonly Panel targetRow;
    readonly Button connect,pause,choose;
    readonly ToolTip tips=new ToolTip{UseAnimation=false,UseFading=false};
    P2PSession selected;
    bool busy,polling,remotePaused,returning;
    internal GeneralModList ReceivedList {get;private set;}
    internal bool InstallAfterReceive {get{return false;}}
    internal string InstalledReceiptId {get;private set;}
    internal string InstalledDestination {get;private set;}
    static string T(string pt,string en){return GeneralStyle.T(pt,en);}
    internal GeneralP2PDialog(P2PService service,GeneralModState state,System.Collections.Generic.IEnumerable<string> ids,FixedCatalog catalog,DownloadCache cache,bool autoStart=true):base(T("Compartilhar P2P","Share over P2P")){
        this.service=service;this.state=state;this.ids=ids.ToArray();this.catalog=catalog;
        ClientSize=new Size(690,540);Name="p2p-dialog";
        var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=8,Padding=new Padding(24,8,24,20),Margin=Padding.Empty};
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        foreach(int height in new[]{24,76,24,76,76,56,50})body.RowStyles.Add(new RowStyle(SizeType.Absolute,height));body.RowStyles.Add(new RowStyle(SizeType.Percent,100));Content.Controls.Add(body);
        body.Controls.Add(Heading(T("Link para enviar ao amigo","Link to send to your friend")),0,0);
        var send=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};send.RowStyles.Add(new RowStyle(SizeType.Percent,100));send.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));send.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,44));
        send.Controls.Add(Field(output),0,0);var copy=GeneralStyle.Button("",()=>{if(output.Text.Length>0)try{Clipboard.SetText(output.Text);}catch(Exception ex){status.Text=ex.Message;}});
        ((GeneralButton)copy).IconKind="copy";((GeneralButton)copy).Bare=true;copy.AutoSize=false;copy.MinimumSize=new Size(44,44);copy.Size=copy.MinimumSize;copy.Margin=Padding.Empty;copy.Name="p2p-copy";copy.AccessibleName=T("Copiar link","Copy link");tips.SetToolTip(copy,copy.AccessibleName);send.Controls.Add(copy,1,0);body.Controls.Add(send,0,1);
        body.Controls.Add(Heading(T("Link recebido do amigo","Link received from your friend")),0,2);body.Controls.Add(Field(input),0,3);
        targetRow=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};var target=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=3,Margin=Padding.Empty};target.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));target.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));target.RowStyles.Add(new RowStyle(SizeType.Absolute,24));target.RowStyles.Add(new RowStyle(SizeType.Absolute,44));target.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        target.Controls.Add(Heading(T("Instalar na pasta local","Install into local folder")),0,0);target.SetColumnSpan(target.GetControlFromPosition(0,0),2);var destinationField=Field(destination);destinationField.Margin=Padding.Empty;target.Controls.Add(destinationField,0,1);
        choose=GeneralStyle.Button(T("Escolher pasta","Choose folder"),ChooseFolder);choose.Margin=new Padding(8,0,0,0);choose.Name="p2p-folder";target.Controls.Add(choose,1,1);targetRow.Controls.Add(target);body.Controls.Add(targetRow,0,4);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Margin=Padding.Empty};
        connect=GeneralStyle.Button(T("Conectar","Connect"),()=>Run(Connect),true);connect.Name="p2p-connect";connect.Margin=new Padding(8,0,0,0);actions.Controls.Add(connect);
        pause=GeneralStyle.Button(T("Pausar","Pause"),()=>Run(()=>{var session=selected;return Task.Run(()=>service.Pause(session));}));pause.Name="p2p-pause";pause.Margin=Padding.Empty;actions.Controls.Add(pause);body.Controls.Add(actions,0,5);body.Controls.Add(progress,0,6);body.Controls.Add(status,0,7);
        output.Name="p2p-output";input.Name="p2p-input";destination.Name="p2p-destination";status.Name="p2p-status";destination.Text=state.Destination??"";
        input.TextChanged+=(s,e)=>UpdateActions();timer.Tick+=async(s,e)=>await Poll();
        selected=service.Sessions.LastOrDefault(s=>!s.Completed)??service.Sessions.LastOrDefault(s=>!s.Sender&&s.Completed&&!s.ListImported);
        FormClosing+=(s,e)=>{if(busy){e.Cancel=true;status.Text=T("Aguarde a preparação terminar.","Wait for preparation to finish.");}};FormClosed+=(s,e)=>timer.Stop();Disposed+=(s,e)=>{timer.Dispose();tips.Dispose();};
        GeneralStyle.Apply(this,GeneralStyle.Dark);status.Text=T("Para enviar o preset atual, deixe o segundo campo vazio e clique em Conectar. Para receber, cole o link do amigo.","To share the current preset, leave the second field empty and click Connect. To receive, paste your friend's link.");UpdateActions();
        if(autoStart)Shown+=(s,e)=>Run(async()=>{await Task.Run(()=>service.Start(Report));SelectSession(selected);timer.Start();});
    }
    static Label Heading(string text){return new Label{Text=text,Dock=DockStyle.Fill,Font=GeneralStyle.Font(11),Margin=Padding.Empty};}
    static GeneralSurface Field(Control control){var field=new GeneralSurface{Dock=DockStyle.Fill,Padding=new Padding(14,9,14,9),Margin=new Padding(0,0,0,8)};field.Controls.Add(control);return field;}
    void Report(string text){if(!IsDisposed&&IsHandleCreated)BeginInvoke((Action)(()=>{if(!IsDisposed)status.Text=text;}));}
    async void Run(Func<Task> action){if(busy)return;busy=true;UpdateActions();try{while(polling)await Task.Delay(40);await action();}catch(Exception ex){status.Text=T("Falha: ","Failed: ")+ex.Message;}finally{busy=false;if(!IsDisposed)UpdateActions();}}
    void UpdateActions(){
        bool receiving=selected!=null&&!selected.Sender;try{receiving|=P2PInvite.Decode(input.Text).Kind=="offer";}catch(IOException){}
        targetRow.Visible=receiving;var layout=(TableLayoutPanel)targetRow.Parent;layout.RowStyles[4].Height=receiving?76:0;
        connect.Enabled=!busy&&(selected==null||selected.Stage==null);choose.Enabled=!busy&&(selected==null||selected.Sender||!selected.Completed&&selected.Stage==null);
        pause.Visible=selected!=null&&!selected.Completed;pause.Enabled=!busy&&service.Engine.Running&&selected!=null&&!selected.Completed&&selected.Stage==null&&(!remotePaused||selected.Paused);
        pause.Text=selected!=null&&selected.Paused?T("Retomar","Resume"):remotePaused?T("Pausado pelo amigo","Paused by friend"):T("Pausar","Pause");
    }
    void SelectSession(P2PSession session){selected=session;remotePaused=false;output.Text=session==null||!service.Engine.Running?"":service.Invitation(session);if(session!=null&&!session.Sender)destination.Text=session.Destination??destination.Text;UpdateActions();}
    void ChooseFolder(){using(var folder=new FolderBrowserDialog{Description=T("Escolha a pasta local de instalação.","Choose the local installation folder."),SelectedPath=Directory.Exists(destination.Text)?destination.Text:""})if(folder.ShowDialog(this)==DialogResult.OK)destination.Text=folder.SelectedPath;}
    async Task Connect(){
        if(!service.Engine.Running)await Task.Run(()=>service.Start(Report));timer.Start();
        string code=input.Text.Trim();
        if(code.Length==0){
            if(selected!=null&&!selected.Completed){if(!selected.Sender&&selected.Error!=null){string target=destination.Text;await Task.Run(()=>service.RetryInstalled(selected,target));}SelectSession(selected);status.Text=T("Compartilhamento retomado. Envie o link acima ao amigo.","Sharing resumed. Send the link above to your friend.");return;}
            var session=await Task.Run(()=>service.ShareInstalled(state,ids,catalog,service.ApplicationData,true,Report));SelectSession(session);status.Text=T("Envie o link acima. Depois cole aqui o link de resposta do amigo e clique em Conectar.","Send the link above. Then paste your friend's reply link here and click Connect.");return;
        }
        var invite=P2PInvite.Decode(code);if(invite.Mode!="installed-zip")throw new IOException(T("Este link usa o formato antigo. Peça um novo link ao amigo.","This link uses the old format. Ask your friend for a new link."));
        if(invite.Kind=="offer"){
            if(!Directory.Exists(destination.Text))ChooseFolder();if(!Directory.Exists(destination.Text))throw new IOException(T("Escolha a pasta de instalação.","Choose the installation folder."));string target=destination.Text;
            if(GeneralDialog.Message(this,T("Receber e instalar o pacote ","Receive and install pack ")+invite.Name+T(" em:\n"," into:\n")+target+T("\n\nMantenha o jogo fechado. Configurações anteriores terão backup.","\n\nKeep the game closed. Previous settings will be backed up."),T("Conectar e instalar","Connect and install"),MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
            var session=await Task.Run(()=>{var received=service.Accept(code,target,true);if(!received.Completed)service.RetryInstalled(received,target);return received;});SelectSession(session);status.Text=T("Envie o link de resposta acima ao amigo. O recebimento e a instalação continuarão automaticamente.","Send the reply link above to your friend. Receiving and installation will continue automatically.");
        }else{var session=await Task.Run(()=>service.Authorize(code));SelectSession(session);status.Text=T("Conectado. A transferência continua automaticamente.","Connected. Transfer continues automatically.");}
        input.Clear();
    }
    async Task Poll(){
        if(busy||polling||!service.Engine.Running||selected==null||returning)return;polling=true;var session=selected;
        try{
            if(session.Completed){if(Directory.Exists(service.Folder(session))){progress.Value=99;status.Text=T("Finalizando a limpeza do arquivo temporário…","Finishing temporary archive cleanup…");return;}progress.Value=100;progress.Caption=session.Sender?T("Envio concluído","Send complete"):T("Instalação concluída","Installation complete");status.Text=T("Concluído. Arquivo temporário removido.","Complete. Temporary archive removed.");
                if(!session.Sender&&!session.ListImported){ReceivedList=service.InstalledManifest(session).List;InstalledReceiptId=session.ReceiptId;InstalledDestination=session.Destination;returning=true;Complete(DialogResult.OK);}return;}
            if(session.Error!=null){status.Text=T("Falha: ","Failed: ")+session.Error+T(" O arquivo foi preservado. Use Conectar para tentar novamente."," The file was preserved. Use Connect to retry.");return;}
            if(session.Stage!=null){progress.Value=99;progress.Caption=T("Conferindo e instalando","Verifying and installing");status.Text=session.Stage;return;}
            var transfer=await Task.Run(()=>service.TransferStatus(session));if(IsDisposed)return;remotePaused=transfer.RemotePaused;progress.Value=transfer.Percent;progress.Caption=session.Sender?T("Enviando","Sending"):T("Recebendo","Receiving");
            status.Text=session.Paused?T("Pausado por você. Só você pode retomar sua pausa.","Paused by you. Only you can release your pause."):remotePaused?T("Pausado pelo amigo. Aguarde ele retomar.","Paused by your friend. Wait for them to resume."):session.Peer==null?T("Aguardando o link de resposta do amigo.","Waiting for your friend's reply link."):!transfer.Connected?T("Aguardando reconexão do amigo. A transferência será retomada.","Waiting for your friend to reconnect. Transfer will resume."):session.Sender?T("Enviando pacote…","Sending pack…"):T("Recebendo pacote; a instalação começa após a conferência.","Receiving pack; installation starts after verification.");
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
        try{PrepareP2P();if(!p2p.Sessions.Any(s=>!s.Completed))return;await Task.Run(()=>p2p.Start(_=>{}));if(!IsDisposed)detail.Text=T("Compartilhamento P2P retomado.","P2P sharing resumed.");else p2p.Dispose();}
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
