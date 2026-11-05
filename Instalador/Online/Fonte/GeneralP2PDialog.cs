using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class GeneralP2PDialog : GeneralDialog {
    readonly P2PService service;
    readonly GeneralModState state;
    readonly FixedCatalog catalog;
    readonly DownloadCache cache;
    readonly string[] ids;
    readonly ListBox sessions=new ListBox{Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,IntegralHeight=false};
    readonly TextBox input=new TextBox{Multiline=true,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,MaxLength=12000};
    readonly TextBox output=new TextBox{Multiline=true,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,ReadOnly=true};
    readonly Label status=new Label{Dock=DockStyle.Fill,AutoEllipsis=true};
    readonly GeneralProgress progress=new GeneralProgress{Dock=DockStyle.Fill,Height=46};
    readonly List<Button> commands=new List<Button>();
    readonly Timer timer=new Timer{Interval=1500};
    readonly CheckBox includeConfigs=new CheckBox{AutoSize=true,Checked=true};
    readonly HashSet<string> autoReturn=new HashSet<string>();
    readonly Button pause,load,install;
    bool busy,polling;
    internal GeneralModList ReceivedList {get;private set;}
    internal bool InstallAfterReceive {get;private set;}
    internal string InstalledReceiptId {get;private set;}
    internal string InstalledDestination {get;private set;}
    static string T(string pt,string en){return GeneralStyle.T(pt,en);}
    P2PSession Selected {get{return sessions.SelectedItem as P2PSession;}}
    internal GeneralP2PDialog(P2PService service,GeneralModState state,IEnumerable<string> ids,FixedCatalog catalog,DownloadCache cache,bool autoStart=true):base(T("Compartilhar pacote P2P","Share pack over P2P")){
        this.service=service;this.state=state;this.ids=ids.ToArray();this.catalog=catalog;this.cache=cache;
        sessions.DrawMode=DrawMode.OwnerDrawFixed;sessions.ItemHeight=28;
        sessions.DrawItem+=(s,e)=>{
            if(e.Index<0)return;bool selected=(e.State&DrawItemState.Selected)!=0;
            using(var background=new SolidBrush(selected?GeneralStyle.Selection:GeneralStyle.Surface))e.Graphics.FillRectangle(background,e.Bounds);
            var bounds=e.Bounds;bounds.Inflate(-8,0);
            TextRenderer.DrawText(e.Graphics,Convert.ToString(sessions.Items[e.Index]),sessions.Font,bounds,GeneralStyle.Foreground,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
            if((e.State&DrawItemState.Focus)!=0)e.DrawFocusRectangle();
        };
        ClientSize=new Size(900,720);Name="p2p-dialog";
        var body=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24,8,24,20),ColumnCount=1,RowCount=10};
        int[] heights={96,48,118,48,30,74,48,74,48,70};
        for(int row=0;row<heights.Length;row++)body.RowStyles.Add(row==2?new RowStyle(SizeType.Percent,100):new RowStyle(SizeType.Absolute,heights[row]));
        Content.Controls.Add(body);
        var intro=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};intro.RowStyles.Add(new RowStyle(SizeType.Percent,100));intro.RowStyles.Add(new RowStyle(SizeType.Absolute,28));
        intro.Controls.Add(new Label{Name="p2p-intro",Dock=DockStyle.Fill,Text=T("Exporte um ZIP dos mods instalados. Envie o convite e autorize a resposta do amigo. Ele recebe, confere e instala automaticamente; o ZIP temporário é removido.","Export a ZIP of installed mods. Send the invitation and authorize your friend's reply. They receive, verify and install automatically; the temporary ZIP is removed.")},0,0);
        includeConfigs.AutoSize=false;includeConfigs.Dock=DockStyle.Fill;includeConfigs.Margin=Padding.Empty;includeConfigs.Name="p2p-settings";
        includeConfigs.Text=T("Incluir configurações seguras dos mods","Include safe mod settings");intro.Controls.Add(includeConfigs,0,1);body.Controls.Add(intro,0,0);
        var top=Flow();body.Controls.Add(top,0,1);
        Add(top,T("Exportar mods instalados","Export installed mods"),()=>Share(),true,"p2p-share");
        Add(top,T("Receber convite","Receive invitation"),()=>Accept(),false,"p2p-receive");
        Add(top,T("Autorizar resposta","Authorize reply"),()=>Authorize(),false,"p2p-authorize");
        body.Controls.Add(new GeneralScrollView(sessions),0,2);
        var sessionCommands=Flow();body.Controls.Add(sessionCommands,0,3);
        Add(sessionCommands,T("Copiar convite/resposta","Copy invitation/reply"),()=>Copy());
        pause=Add(sessionCommands,T("Pausar/Retomar","Pause/Resume"),()=>Run(()=>{if(Selected!=null)service.Pause(Selected);return Task.FromResult(0);}));
        Add(sessionCommands,T("Encerrar e liberar espaço","Stop and free space"),()=>Remove());
        body.Controls.Add(new Label{Dock=DockStyle.Fill,Text=T("Cole aqui o convite ou a resposta do seu amigo:","Paste your friend's invitation or reply here:")},0,4);
        body.Controls.Add(Field(input),0,5);
        var codeCommands=Flow();body.Controls.Add(codeCommands,0,6);
        Add(codeCommands,T("Colar da área de transferência","Paste from clipboard"),()=>{try{input.Text=Clipboard.GetText();}catch(Exception ex){status.Text=ex.Message;}});
        Add(codeCommands,T("Abrir pasta do pacote","Open pack folder"),()=>{if(Selected!=null)try{Process.Start(new ProcessStartInfo(service.Folder(Selected)){UseShellExecute=true});}catch(Exception ex){status.Text=ex.Message;}});
        body.Controls.Add(Field(output),0,7);
        var actions=Flow();actions.FlowDirection=FlowDirection.RightToLeft;body.Controls.Add(actions,0,8);
        install=Add(actions,T("Carregar/Instalar","Load/Install"),()=>LoadPack(true),true,"p2p-install");
        load=Add(actions,T("Carregar lista","Load list"),()=>LoadPack(false),false,"p2p-load");
        var bottom=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};bottom.RowStyles.Add(new RowStyle(SizeType.Absolute,36));bottom.RowStyles.Add(new RowStyle(SizeType.Percent,100));bottom.Controls.Add(progress,0,0);bottom.Controls.Add(status,0,1);body.Controls.Add(bottom,0,9);
        input.Name="p2p-input";output.Name="p2p-output";sessions.Name="p2p-sessions";status.Name="p2p-status";
        sessions.SelectedIndexChanged+=(s,e)=>SelectSession();timer.Tick+=async(s,e)=>await Poll();
        FormClosing+=(s,e)=>{if(busy){e.Cancel=true;status.Text=T("Aguarde a preparação terminar.","Wait for preparation to finish.");}};
        FormClosed+=(s,e)=>{timer.Stop();};Disposed+=(s,e)=>timer.Dispose();
        GeneralStyle.Apply(this,GeneralStyle.Dark);Refill();
        status.Text=T("Nenhum arquivo será enviado antes da autorização dos dois computadores.","No files are sent until both computers authorize the connection.");
        if(autoStart)Shown+=(s,e)=>Run(async()=>{await Task.Run(()=>service.Start(Report));Refill();timer.Start();status.Text=T("Conexão pronta. Compartilhe um preset ou cole um convite.","Ready. Share a preset or paste an invitation.");});
    }
    static FlowLayoutPanel Flow(){return new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty};}
    static GeneralSurface Field(Control control){var field=new GeneralSurface{Dock=DockStyle.Fill,Padding=new Padding(14,8,14,8),Margin=new Padding(0,0,0,8)};field.Controls.Add(control);return field;}
    Button Add(FlowLayoutPanel panel,string text,Action action,bool primary=false,string name=null){var b=GeneralStyle.Button(text,action,primary);b.Name=name??"";b.Margin=new Padding(0,0,8,0);commands.Add(b);panel.Controls.Add(b);return b;}
    void Report(string text){if(!IsDisposed&&IsHandleCreated)BeginInvoke((Action)(()=>{if(!IsDisposed)status.Text=text;}));}
    async void Run(Func<Task> operation){
        if(busy)return;busy=true;SetEnabled();
        try{while(polling)await Task.Delay(50);await operation();}catch(Exception ex){status.Text=T("Falha: ","Failed: ")+ex.Message;}
        finally{busy=false;if(!IsDisposed)SetEnabled();}
    }
    void SetEnabled(){
        foreach(var b in commands)b.Enabled=!busy&&service.Engine.Running;
        var s=Selected;load.Enabled=install.Enabled=!busy&&service.Engine.Running&&s!=null&&!s.Sender;
        if(s!=null&&s.Stage!=null){foreach(var b in commands)b.Enabled=false;return;}
        if(s!=null&&s.Mode=="installed-zip"){load.Enabled&=s.Completed;install.Enabled&=!s.Completed&&!String.IsNullOrEmpty(s.Error);pause.Enabled&=!s.Completed&&s.Stage==null;}
    }
    void Refill(P2PSession select=null){var previous=select??Selected;sessions.Items.Clear();sessions.Items.AddRange(service.Sessions.Cast<object>().ToArray());if(previous!=null&&sessions.Items.Contains(previous))sessions.SelectedItem=previous;else if(sessions.Items.Count>0)sessions.SelectedIndex=sessions.Items.Count-1;SetEnabled();}
    void SelectSession(){var s=Selected;output.Text=s==null||!service.Engine.Running?T("Seu convite ou resposta aparecerá aqui.","Your invitation or reply will appear here."):service.Invitation(s);progress.Value=0;SetEnabled();}
    void Share(){
        if(GeneralDialog.Message(this,T("Criar um ZIP temporário usando somente os arquivos instalados dos mods deste preset e suas dependências? Não precisa dos downloads originais. Confirme que pode redistribuir os mods escolhidos. Feche o jogo antes de preparar. Saves e contas não serão enviados.","Create a temporary ZIP using only installed files from this preset and its dependencies? Original downloads are not required. Confirm you may redistribute the selected mods. Close the game before preparing. Saves and accounts are excluded."),T("Exportar instalados","Export installed mods"),MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
        bool settings=includeConfigs.Checked;
        Run(async()=>{var session=await Task.Run(()=>service.ShareInstalled(state,ids,catalog,service.ApplicationData,settings,Report));Refill(session);status.Text=T("ZIP pronto. Copie o convite e envie ao amigo. O temporário será removido após o envio.","ZIP ready. Copy the invitation and send it to your friend. The temporary is removed after transfer.");});
    }
    void Accept(){
        string code=input.Text;P2PInvite invite;
        try{invite=P2PInvite.Decode(code);if(invite.Kind!="offer")throw new IOException(T("Cole um convite de envio.","Paste a sender invitation."));}catch(Exception ex){status.Text=ex.Message;return;}
        string target=null;
        if(invite.Mode=="installed-zip"){
            target=state.Destination;
            if(String.IsNullOrWhiteSpace(target)||!Directory.Exists(target))using(var folder=new FolderBrowserDialog{Description=T("Escolha a pasta do jogo para instalar o pacote.","Choose the game folder for this pack.")}){if(folder.ShowDialog(this)!=DialogResult.OK)return;target=folder.SelectedPath;}
        }
        string question=T("Receber o pacote ","Receive pack ")+invite.Name+T(" deste dispositivo?\n"," from this device?\n")+invite.Device;
        if(target!=null)question+=T("\n\nInstalar automaticamente após a conferência em:\n","\n\nAutomatically install after verification into:\n")+target+T("\nAs configurações incluídas serão aplicadas com backup. Mantenha o jogo fechado.","\nIncluded settings will be applied with backup. Keep the game closed.");
        if(GeneralDialog.Message(this,question,T("Autorizar remetente","Authorize sender"),MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
        Run(async()=>{var s=await Task.Run(()=>service.Accept(code,target,target!=null));if(s.Mode=="installed-zip"&&!s.Completed)autoReturn.Add(s.Id);Refill(s);status.Text=T("Copie a resposta abaixo e envie ao remetente para ele autorizar.","Copy the reply below and send it to the sender for authorization.");});
    }
    void Authorize(){
        string code=input.Text;P2PInvite reply;try{reply=P2PInvite.Decode(code);}catch(Exception ex){status.Text=ex.Message;return;}
        if(GeneralDialog.Message(this,T("Autorizar este computador a receber o pacote?\n","Authorize this computer to receive the pack?\n")+reply.Device,T("Autorizar amigo","Authorize friend"),MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
        Run(async()=>{var s=await Task.Run(()=>service.Authorize(code));Refill(s);status.Text=T("Amigo autorizado. A transferência começa quando os dois estiverem conectados.","Friend authorized. Transfer starts when both computers are connected.");});
    }
    void Copy(){if(Selected==null)return;try{Clipboard.SetText(service.Invitation(Selected));status.Text=T("Copiado. Envie o código ao seu amigo.","Copied. Send the code to your friend.");}catch(Exception ex){status.Text=ex.Message;}}
    void Remove(){
        var s=Selected;if(s==null)return;
        if(GeneralDialog.Message(this,T("Encerrar este compartilhamento e remover sua cópia de transferência? Os originais, arquivos recebidos já preparados e mods instalados serão preservados.","Stop sharing and remove this transfer copy? Originals, prepared received files and installed mods will be preserved."),T("Encerrar compartilhamento","Stop sharing"),MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
        Run(async()=>{await Task.Run(()=>service.Remove(s));Refill();status.Text=T("Compartilhamento encerrado.","Sharing stopped.");});
    }
    void LoadPack(bool installNow){
        var s=Selected;if(s==null||s.Sender)return;
        if(s.Mode=="installed-zip"){
            if(s.Completed)Run(()=>{ReceivedList=service.InstalledManifest(s).List;InstalledReceiptId=s.ReceiptId;InstalledDestination=s.Destination;busy=false;Complete(DialogResult.OK);return Task.FromResult(0);});
            else if(installNow)Run(()=>{service.RetryInstalled(s,s.Destination);return Task.FromResult(0);});
            return;
        }
        Run(async()=>{ReceivedList=await Task.Run(()=>P2PPack.Receive(service.Folder(s),s.Hash,service.PrivateFolder(s),catalog,cache,Report));InstallAfterReceive=installNow;busy=false;Complete(DialogResult.OK);});
    }
    async Task Poll(){
        if(busy||polling||!service.Engine.Running||Selected==null)return;polling=true;var s=Selected;
        try{
            var result=await Task.Run(()=>{
                if(s.Mode=="installed-zip"){
                    if(s.Completed)return new object[]{100,s.Sender?T("Enviado. ZIP temporário removido.","Sent. Temporary ZIP removed."):T("Instalado e conferido. ZIP temporário removido.","Installed and verified. Temporary ZIP removed.")};
                    if(!String.IsNullOrEmpty(s.Error))return new object[]{0,T("Falha: ","Failed: ")+s.Error};
                    if(s.Stage!=null)return new object[]{99,s.Stage};
                    var snapshot=service.Engine.Get("db/status?folder="+s.Id);long snapshotNeeded=Convert.ToInt64(snapshot["needBytes"]);
                    int snapshotPct=s.Sender?0:(int)Math.Max(0,Math.Min(99,(s.Size-snapshotNeeded)*100.0/s.Size));
                    if(s.Sender&&s.Peer!=null){var c=service.Engine.Get("db/completion?folder="+s.Id+"&device="+s.Peer);snapshotPct=(int)Math.Min(99,Convert.ToDouble(c["completion"]));}
                    return new object[]{snapshotPct,s.Paused?T("Pausado","Paused"):s.Peer==null?T("Aguardando resposta do amigo.","Waiting for your friend's reply."):s.Sender?T("Enviando ZIP temporário…","Sending temporary ZIP…"):T("Recebendo ZIP; a instalação começa após a conferência.","Receiving ZIP; installation starts after verification.")};
                }
                var connection=service.Engine.Get("system/connections");var connections=connection["connections"] as Dictionary<string,object>;
                bool online=false;if(s.Peer!=null&&connections!=null&&connections.ContainsKey(s.Peer)){var peer=connections[s.Peer] as Dictionary<string,object>;online=peer!=null&&Convert.ToBoolean(peer["connected"]);}
                if(s.Sender){
                    double percentage=0;if(s.Peer!=null){var completion=service.Engine.Get("db/completion?folder="+s.Id+"&device="+s.Peer);percentage=Convert.ToDouble(completion["completion"]);}
                    return new object[]{(int)percentage,s.Paused?T("Pausado","Paused"):s.Peer==null?T("Aguardando a resposta do amigo.","Waiting for your friend's reply."):online?T("Enviando · ","Sending · ")+percentage.ToString("0")+"%":T("Aguardando seu amigo conectar.","Waiting for your friend to connect.")};
                }
                string manifest=Safe.In(service.Folder(s),"pack.json");if(!File.Exists(manifest))return new object[]{0,online?T("Recebendo manifesto…","Receiving manifest…"):T("Aguardando autorização/conexão do remetente.","Waiting for sender authorization/connection.")};
                var pack=service.AllowFiles(s);long total=pack.Files.Sum(f=>f.Size);
                var transfer=service.Engine.Get("db/status?folder="+s.Id);long needed=Convert.ToInt64(transfer["needBytes"]);
                bool arrived=pack.Files.All(f=>File.Exists(Safe.In(service.Folder(s),f.File))&&new FileInfo(Safe.In(service.Folder(s),f.File)).Length==f.Size);
                int pct=total==0?99:(int)Math.Max(0,Math.Min(99,(total-needed)*100.0/total));
                string note=s.Paused?T("Pausado","Paused"):arrived?T("Arquivos recebidos; clique em Carregar lista para conferir, ou Carregar/Instalar.","Files received; click Load list to verify, or Load/Install."):(online?T("Recebendo · estimado","Receiving · estimated"):T("Aguardando remetente","Waiting for sender"))+" · "+pct+"%";
                return new object[]{pct,note};
            });
            if(!IsDisposed&&Selected==s){progress.Value=Convert.ToInt32(result[0]);progress.Caption=s.Name;status.Text=Convert.ToString(result[1]);progress.Invalidate();SetEnabled();}
            if(!IsDisposed&&s.Completed&&!s.Sender&&autoReturn.Remove(s.Id))LoadPack(false);
        }catch(Exception ex){if(!IsDisposed)status.Text=T("Aguardando ou falha: ","Waiting or failed: ")+ex.Message;}
        finally{polling=false;}
    }
}

internal sealed partial class GeneralModWindow {
    P2PService p2p;
    async void ShowP2P(){
        if(working||scanning)return;
        try{
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
            var chosen=state.List.Presets.FirstOrDefault(p=>p.Id==state.SelectedPreset);
            var ids=chosen==null?state.List.Mods.Select(m=>m.Id):chosen.ModIds;
            using(var dialog=new GeneralP2PDialog(p2p,state,ids,catalog,cache)){
                if(dialog.ShowDialog(this)!=DialogResult.OK||dialog.ReceivedList==null)return;
                if(!await ReplaceList(dialog.ReceivedList,true,dialog.InstalledReceiptId,dialog.InstalledDestination))return;
                if(dialog.InstallAfterReceive){foreach(var mod in state.List.Mods)marked.Add(mod.Id);DrawRows();StartSelected();}
                else detail.Text=T("Pacote recebido e conferido. Selecione os itens e clique em Baixar/Instalar.","Pack received and verified. Select items and click Download/Install.");
            }
        }catch(Exception ex){ShowError(ex);}
    }
}
