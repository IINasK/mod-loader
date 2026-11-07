using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class GeneralP2PDialog : GeneralDialog {
    const int Inset=24,Gap=16,LabelHeight=24,LinkHeight=56,ControlHeight=44;
    const int TargetHeight=LabelHeight+ControlHeight+Gap;
    readonly P2PService service;
    readonly GeneralModState state;
    readonly FixedCatalog catalog;
    readonly GeneralPicker presets=new GeneralPicker{Dock=DockStyle.Fill,Margin=Padding.Empty,Name="p2p-export-preset"};
    string ExportPreset {get{return presets.SelectedIndex<=0?null:state.List.Presets[presets.SelectedIndex-1].Id;}}
    string[] ExportIds {get{return ExportPreset==null?state.List.Mods.Select(item=>item.Id).ToArray():state.List.Presets[presets.SelectedIndex-1].ModIds.ToArray();}}
    readonly bool autoStart;
    readonly TextBox output=new TextBox{ReadOnly=true,WordWrap=false,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,Name="p2p-output"};
    readonly TextBox input=new TextBox{WordWrap=false,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,MaxLength=12000,Name="p2p-input"};
    readonly TextBox destination=new TextBox{ReadOnly=true,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,Name="p2p-destination"};
    readonly Label status=new Label{Dock=DockStyle.Fill,UseMnemonic=false,Name="p2p-status"};
    readonly Timer timer=new Timer{Interval=1000};
    readonly ToolTip tips=new ToolTip{UseAnimation=false,UseFading=false};
    readonly TableLayoutPanel body;
    readonly Panel targetRow;
    readonly Panel cardStack=new Panel{BackColor=GeneralStyle.Surface};
    readonly GeneralScrollView cardScroll;
    readonly Dictionary<P2PSession,TransferCard> cards=new Dictionary<P2PSession,TransferCard>();
    readonly Button connect,choose,generate,copy;
    P2PSession offer;
    bool busy,polling;
    internal GeneralModList ReceivedList {get;private set;}
    internal bool InstallAfterReceive {get{return false;}}
    internal string InstalledReceiptId {get;private set;}
    internal string InstalledDestination {get;private set;}
    static string T(string pt,string en){return GeneralStyle.T(pt,en);}

    internal GeneralP2PDialog(P2PService service,GeneralModState state,FixedCatalog catalog,bool autoStart=true):base(T("Modpack e compartilhamento","Modpack and sharing")){
        this.service=service;this.state=state;this.autoStart=autoStart;this.catalog=catalog;
        var previousChoice=service.ChooseVersion;
        Func<ModVersionDifference,bool> versionChoice=difference=>GeneralVersionChoiceDialog.Ask(this,difference);
        service.ChooseVersion=versionChoice;
        Disposed+=(s,e)=>{if(Object.ReferenceEquals(service.ChooseVersion,versionChoice))service.ChooseVersion=previousChoice;};
        Name="p2p-dialog";Content.Margin=Padding.Empty;
        ClientSize=new Size(Math.Min(720,Screen.FromPoint(Cursor.Position).WorkingArea.Width-28),420);
        cardScroll=new GeneralScrollView(cardStack,true,reserveBarSpace:true){Name="p2p-cards"};
        int rail=cardScroll.Bar.Width;
        body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=10,Padding=new Padding(Inset,Inset,Inset-rail,Inset),Margin=Padding.Empty};
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        // The scrollbar lives in the outer gutter, so the painted card edge stays aligned with the fields.
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,rail));
        foreach(int height in new[]{state.List.Mods.Count==0?0:ControlHeight+Gap,LabelHeight,LinkHeight+Gap,LabelHeight,LinkHeight+Gap,TargetHeight,ControlHeight,0,0,0})
            body.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
        Content.Controls.Add(body);
        var presetRow=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Margin=Padding.Empty,Padding=new Padding(0,0,0,Gap)};
        presetRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        presetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        var presetLabel=Heading(T("Preset para enviar","Preset to send"));
        presetLabel.AutoSize=true;presetLabel.TextAlign=ContentAlignment.MiddleLeft;presetLabel.Margin=new Padding(0,0,12,0);
        presetRow.Controls.Add(presetLabel,0,0);presetRow.Controls.Add(presets,1,0);
        presets.AccessibleName=presetLabel.Text;
        presets.Items.Add(T("Todos os presets","All presets"));
        foreach(var preset in state.List.Presets)presets.Items.Add(preset.Name);
        presets.SelectedIndex=state.List.Presets.FindIndex(p=>p.Id==state.SelectedPreset)+1;
        body.Controls.Add(presetRow,0,0);
        presetRow.Visible=state.List.Mods.Count!=0;
        var sendLabel=Heading(T("Link para enviar ao amigo","Link to send to your friend"));
        body.Controls.Add(sendLabel,0,1);
        var send=Field(output);send.Padding=new Padding(14,16,72,8);
        copy=GeneralStyle.Button("",()=>Copy(output.Text));IconButton(copy,"copy",T("Copiar link","Copy link"));copy.Name="p2p-copy";
        if(state.List.Mods.Count==0){sendLabel.Visible=false;send.Visible=false;body.RowStyles[1].Height=0;body.RowStyles[2].Height=0;}
        send.Controls.Add(copy);send.SizeChanged+=(s,e)=>copy.Location=new Point(send.ClientSize.Width-62,6);body.Controls.Add(send,0,2);
        body.Controls.Add(Heading(T("Link recebido do amigo","Link received from your friend")),0,3);
        var receive=Field(input);receive.Padding=new Padding(14,16,14,8);body.Controls.Add(receive,0,4);
        targetRow=new Panel{Dock=DockStyle.Fill,Margin=new Padding(0,0,0,Gap)};
        var target=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=2,Margin=Padding.Empty};
        target.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));target.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        target.RowStyles.Add(new RowStyle(SizeType.Absolute,LabelHeight));target.RowStyles.Add(new RowStyle(SizeType.Absolute,ControlHeight));
        var targetLabel=Heading(T("Instalar na pasta local","Install into local folder"));target.Controls.Add(targetLabel,0,0);target.SetColumnSpan(targetLabel,2);
        var destinationField=Field(destination);destinationField.Margin=Padding.Empty;target.Controls.Add(destinationField,0,1);
        choose=GeneralStyle.Button(T("Escolher pasta","Choose folder"),ChooseFolder);choose.Margin=new Padding(8,0,0,0);choose.Name="p2p-folder";target.Controls.Add(choose,1,1);
        targetRow.Controls.Add(target);body.Controls.Add(targetRow,0,5);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Margin=Padding.Empty,Name="p2p-actions"};
        connect=GeneralStyle.Button(T("Conectar","Connect"),()=>Run(Connect),true);connect.Name="p2p-connect";connect.Margin=new Padding(8,0,0,0);actions.Controls.Add(connect);
        generate=GeneralStyle.Button(T("Gerar modpack","Generate modpack"),()=>Run(GenerateModpack));generate.Name="modpack-generate";generate.Margin=Padding.Empty;actions.Controls.Add(generate);
        body.Controls.Add(actions,0,6);body.Controls.Add(status,0,7);
        status.Margin=new Padding(0,Gap,0,0);
        body.Controls.Add(cardScroll,0,9);body.SetColumnSpan(cardScroll,2);
        cardStack.ClientSizeChanged+=(s,e)=>LayoutCards();
        destination.Text=state.Destination??"";
        output.TextChanged+=(s,e)=>UpdateActions();input.TextChanged+=(s,e)=>UpdateActions();status.TextChanged+=(s,e)=>UpdateActions();
        presets.SelectedIndexChanged+=(s,e)=>{if(this.autoStart)Run(PrepareLink);};
        timer.Tick+=async(s,e)=>await Poll();
        FormClosing+=(s,e)=>{if(busy)e.Cancel=true;};
        FormClosed+=(s,e)=>timer.Stop();
        Disposed+=(s,e)=>{timer.Dispose();tips.Dispose();};
        GeneralStyle.Apply(this,GeneralStyle.Dark);UpdateActions();
        Shown+=async(s,e)=>{
            if(autoStart&&state.List.Mods.Count!=0)Run(PrepareLink);
            timer.Start();
            await Poll();
        };
    }

    void IconButton(Button button,string kind,string label){
        var icon=(GeneralButton)button;icon.IconKind=kind;icon.Bare=true;button.AutoSize=false;
        button.MinimumSize=new Size(44,44);button.Size=button.MinimumSize;button.Margin=Padding.Empty;
        button.AccessibleName=label;tips.SetToolTip(button,label);
    }
    static Label Heading(string text){return new Label{Text=text,Dock=DockStyle.Fill,Font=GeneralStyle.Font(11),Margin=Padding.Empty};}
    static GeneralSurface Field(Control control){var field=new GeneralSurface{Dock=DockStyle.Fill,Padding=new Padding(14,9,14,9),Margin=new Padding(0,0,0,Gap)};field.Controls.Add(control);return field;}
    void Copy(string text){if(!text.StartsWith("ML-P2P1:",StringComparison.Ordinal))return;try{Clipboard.SetText(text);}catch(Exception ex){Failed(ex);}}
    void Report(string text){if(!IsDisposed&&IsHandleCreated)BeginInvoke((Action)(()=>{if(!IsDisposed)status.Text=text;}));}
    void Failed(Exception ex){
        OnlineDiagnostics.Record("p2p.link.failed",null,ex.Message,new{ErrorType=ex.GetType().Name});
        status.ForeColor=GeneralStyle.Danger;status.Text=T("Falha: ","Failed: ")+ex.Message;
        tips.SetToolTip(status,status.Text);
    }
    async void Run(Func<Task> action){
        if(busy)return;
        busy=true;UpdateActions();
        try{while(polling)await Task.Delay(40);await action();}
        catch(InstalledSnapshotReviewException ex){Failed(ex);GeneralDialog.Message(this,ex.Message,T("Conferência dos mods","Mod verification"),MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        catch(Exception ex){Failed(ex);}
        finally{busy=false;if(!IsDisposed)UpdateActions();}
        await Poll();
    }
    void UpdateActions(){
        generate.Enabled=!busy&&!service.Installing&&ExportIds.Length!=0;
        presets.Enabled=!busy;
        copy.Enabled=output.Text.StartsWith("ML-P2P1:",StringComparison.Ordinal)&&(offer==null||!offer.Completed);
        connect.Enabled=!busy&&!String.IsNullOrWhiteSpace(input.Text);
        choose.Enabled=!busy;
        bool receiving=false;
        try{receiving=P2PInvite.Decode(input.Text).Kind=="offer";}catch(IOException){}
        body.SuspendLayout();
        targetRow.Visible=receiving;body.RowStyles[5].Height=receiving?TargetHeight:0;
        status.Visible=status.Text.Length!=0;body.RowStyles[7].Height=status.Text.Length==0?0:Gap+42;
        body.ResumeLayout(false);
        FitCards();
    }
    void FitCards(){
        int chrome=56+body.Padding.Vertical+(int)body.RowStyles.Cast<RowStyle>().Take(8).Sum(row=>row.Height);
        int maximum=Screen.FromControl(this).WorkingArea.Height-32;
        int shown=Math.Min(cardStack.MinimumSize.Height,Math.Max(0,maximum-chrome-Gap));
        int gap=shown==0?0:Gap;
        cardScroll.Visible=shown>0;
        body.RowStyles[8].Height=gap;body.RowStyles[9].Height=shown;
        int height=chrome+gap+shown;
        if(ClientSize.Height!=height){
            ClientSize=new Size(ClientSize.Width,height);
            if(Visible&&Location.X>-20000){
                var area=Screen.FromControl(this).WorkingArea;
                if(Bottom>area.Bottom-12)Top=Math.Max(area.Top+12,area.Bottom-12-Height);
            }
        }
        body.PerformLayout();
        cardScroll.RefreshScroll();
    }
    void LayoutCards(){
        int top=0;
        foreach(TransferCard card in cards.Values.OrderBy(c=>c.Order)){
            card.Frame.SetBounds(0,top,cardStack.ClientSize.Width,card.Height);
            top+=card.Height+Gap;
        }
        int height=Math.Max(0,top-Gap);
        if(cardStack.MinimumSize.Height!=height)cardStack.MinimumSize=new Size(0,height);
    }
    void RefreshCards(Dictionary<P2PSession,P2PTransferStatus> views){
        var sessions=service.SnapshotSessions();
        var latestSent=sessions.LastOrDefault(s=>s.Sender&&s.Completed);
        cardStack.SuspendLayout();
        int order=0;
        foreach(var session in sessions){
            if(session.Discarded||session.HistoryDismissed||session.Mode!="installed-zip")continue;
            if(session.Sender&&session.Peer==null&&session.Recipients.Length==0)continue;
            if(session.Completed&&!cards.ContainsKey(session)&&(session.Sender?session!=latestSent:session.ListImported))continue;
            TransferCard card;
            if(!cards.TryGetValue(session,out card)){
                card=new TransferCard(this,session);
                cards.Add(session,card);cardStack.Controls.Add(card.Frame);
                GeneralStyle.Apply(card.Frame,GeneralStyle.Dark);
            }
            card.Order=order++;
            P2PTransferStatus view;
            views.TryGetValue(session,out view);
            card.Refresh(view);
        }
        foreach(var session in cards.Keys.Where(s=>s.Discarded||s.HistoryDismissed||!sessions.Contains(s)).ToArray()){
            cards[session].Frame.Dispose();cards.Remove(session);
        }
        LayoutCards();
        cardStack.ResumeLayout(false);
        FitCards();
    }
    void ChooseFolder(){using(var folder=new FolderBrowserDialog{Description=T("Escolha a pasta local de instalação.","Choose the local installation folder."),SelectedPath=Directory.Exists(destination.Text)?destination.Text:""})if(folder.ShowDialog(this)==DialogResult.OK)destination.Text=folder.SelectedPath;}
    async Task PrepareLink(){
        var selected=ExportIds;
        string preset=ExportPreset;
        if(selected.Length==0){offer=null;output.Clear();return;}
        output.Text=T("Iniciando conexão…","Starting connection…");
        if(!service.Engine.Running)await Task.Run(()=>service.Start(Report));
        var sessions=service.SnapshotSessions();
        offer=sessions.LastOrDefault(s=>s.Sender&&s.Deferred&&!s.Completed&&s.ExportPreset==preset&&(preset!=null||s.ExportAllPresets||state.SelectedPreset==null)&&
            s.SelectedMods!=null&&s.SelectedMods.SequenceEqual(selected)&&(s.Lifecycle||s.Peer==null));
        if(offer==null){
            service.SetPreparationState(state);
            offer=await Task.Run(()=>service.BeginDeferred(state,selected,true,preset,preset==null));
        }else await Task.Run(()=>service.UpgradeInvitation(offer));
        output.Text=service.Invitation(offer);status.Text="";
    }
    async Task Connect(){
        var invite=P2PInvite.Decode(input.Text.Trim());
        if(invite.Mode!="installed-zip")throw new IOException(T("Este link usa o formato antigo. Peça um novo link ao amigo.","This link uses the old format. Ask your friend for a new link."));
        string code=input.Text.Trim();
        if(invite.Kind=="offer"){
            if(!Directory.Exists(destination.Text))ChooseFolder();
            if(!Directory.Exists(destination.Text))throw new IOException(T("Escolha a pasta de instalação.","Choose the installation folder."));
            string target=destination.Text;
            if(GeneralDialog.Message(this,T("Receber e instalar o pacote ","Receive and install pack ")+invite.Name+T(" em:\n"," into:\n")+target+T("\n\nMantenha o jogo fechado. Configurações anteriores terão backup.","\n\nKeep the game closed. Previous settings will be backed up."),T("Conectar e instalar","Connect and install"),MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
            if(!service.Engine.Running)await Task.Run(()=>service.Start(Report));
            await Task.Run(()=>{
                var session=service.Accept(code,target,true);
                if(!session.Completed&&(session.Held||session.Error!=null||!session.AutoInstall))service.RetryInstalled(session,target);
            });
        }else{
            if(!service.Engine.Running)await Task.Run(()=>service.Start(Report));
            await Task.Run(()=>service.Authorize(code));
        }
        input.Clear();status.Text="";
    }
    void Retry(P2PSession session){Run(async()=>{
        if(!service.Engine.Running)await Task.Run(()=>service.Start(Report));
        await Task.Run(()=>{if(session.Sender)service.RetryPreparation(session);else service.RetryInstalled(session,session.Destination);});
    });}
    void Discard(P2PSession session){Run(async()=>{
        if(GeneralDialog.Message(this,T("Descartar o arquivo recebido e reverter a parte registrada de uma instalação interrompida?","Discard the download and roll back the recorded part of an interrupted installation?"),T("Descartar recebimento","Discard download"),MessageBoxButtons.YesNo)==DialogResult.Yes)
            await Task.Run(()=>service.DiscardIncoming(session));
    });}
    void Import(P2PSession session){
        try{
            if(service.RegisterProject!=null){Run(()=>Task.Run(()=>service.PublishProject(session,true)));return;}
            var manifest=service.InstalledManifest(session);
            ReceivedList=manifest.Project??manifest.List;
            InstalledReceiptId=session.ReceiptId;InstalledDestination=session.Destination;
            Complete(DialogResult.OK);
        }catch(Exception ex){Failed(ex);}
    }
    async Task GenerateModpack(){
        status.ForeColor=GeneralStyle.Foreground;
        status.Text=T("Conferindo os mods antes de gerar o modpack…","Verifying mods before creating the modpack…");
        var selected=ExportIds;
        string preset=ExportPreset;
        var manifest=await Task.Run(()=>InstalledSnapshot.Inspect(P2PService.ExportState(state,preset,selected,preset==null),selected,catalog,service.ApplicationData,true,true));
        using(var save=new SaveFileDialog{Title=T("Salvar modpack","Save modpack"),Filter="Modpack ZIP|*.zip",FileName="Modpack.zip",OverwritePrompt=false}){
            if(save.ShowDialog(this)!=DialogResult.OK){status.Text="";return;}
            if(File.Exists(save.FileName))throw new IOException(T("Escolha um nome que ainda não exista.","Choose a name that does not already exist."));
            string path=save.FileName;
            await Task.Run(()=>InstalledSnapshot.CreateModpack(path,manifest,state.Destination,catalog,Report));
            status.Text=T("ZIP pronto. Extraia o conteúdo na pasta raiz do jogo de destino.","ZIP ready. Extract its contents into the destination game's root folder.");
        }
    }
    async Task Poll(){
        if(busy||polling||IsDisposed||ReceivedList!=null)return;
        polling=true;
        try{
            var sessions=service.SnapshotSessions().Where(s=>!s.Discarded&&!s.HistoryDismissed&&s.Mode=="installed-zip"&&
                (s.Peer!=null||s.Recipients.Length!=0)).ToArray();
            var views=await Task.Run(()=>{
                var result=new Dictionary<P2PSession,P2PTransferStatus>();
                if(!service.Engine.Running)return result;
                foreach(var session in sessions){
                    try{result.Add(session,service.TransferStatus(session));}
                    catch(System.Net.WebException){}
                }
                return result;
            });
            if(IsDisposed)return;
            RefreshCards(views);
            UpdateActions();
        }catch(Exception ex){if(!IsDisposed)Failed(ex);}
        finally{polling=false;}
        if(!IsDisposed&&autoStart&&offer!=null&&offer.Completed&&!busy){
            offer=null;Run(PrepareLink);
        }
    }

    sealed class TransferCard {
        readonly GeneralP2PDialog owner;
        readonly P2PSession session;
        internal readonly GeneralSurface Frame=new GeneralSurface{Name="p2p-card"};
        readonly TableLayoutPanel layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,Margin=Padding.Empty};
        readonly TableLayoutPanel bars=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Margin=Padding.Empty};
        readonly GeneralProgress preparation=new GeneralProgress{Dock=DockStyle.Fill,Margin=Padding.Empty,Name="p2p-preparation"};
        readonly List<GeneralProgress> progress=new List<GeneralProgress>();
        readonly Label detail=new Label{Dock=DockStyle.Fill,AutoEllipsis=true,Margin=Padding.Empty,UseMnemonic=false,Name="p2p-transfer-status"};
        readonly FlowLayoutPanel actions=new FlowLayoutPanel{Dock=DockStyle.Fill,Margin=Padding.Empty,WrapContents=false};
        readonly Button pause,retry,discard,import,reply,dismiss;
        P2PTransferStatus last;
        internal int Order,Height;

        internal TransferCard(GeneralP2PDialog owner,P2PSession session){
            this.owner=owner;this.session=session;
            Frame.Padding=new Padding(16,12,16,12);Frame.AccessibleName=session.ToString();
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach(int height in new[]{28,42,42,0,0})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
            layout.RowCount=5;Frame.Controls.Add(layout);
            var title=Heading(session.ToString());title.Font=GeneralStyle.Font(13,true);title.AutoEllipsis=true;title.UseMnemonic=false;
            title.TextAlign=ContentAlignment.MiddleLeft;
            var header=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            dismiss=GeneralStyle.Button("",()=>owner.Run(()=>Task.Run(()=>owner.service.DismissCompletedSend(session))));
            owner.IconButton(dismiss,"close",T("Remover envio concluído","Remove completed send"));dismiss.Name="p2p-dismiss";
            header.Controls.Add(title,0,0);header.Controls.Add(dismiss,1,0);
            layout.Controls.Add(header,0,0);layout.Controls.Add(preparation,0,1);layout.Controls.Add(bars,0,2);
            layout.Controls.Add(detail,0,3);layout.Controls.Add(actions,0,4);
            bars.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            bars.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,0));
            pause=GeneralStyle.Button("",()=>owner.Run(()=>Task.Run(()=>owner.service.Pause(session))));
            owner.IconButton(pause,"pause",T("Pausar","Pause"));pause.Name="p2p-pause";bars.Controls.Add(pause,1,0);
            retry=GeneralStyle.Button(T("Retomar","Resume"),()=>owner.Retry(session));retry.Name="p2p-retry";
            discard=GeneralStyle.Button(T("Descartar recebimento","Discard download"),()=>owner.Discard(session));
            import=GeneralStyle.Button(T("Importar lista","Import list"),()=>owner.Import(session));import.Name="p2p-import-list";
            reply=GeneralStyle.Button(T("Copiar resposta","Copy reply"),()=>owner.Copy(owner.service.Invitation(session)));
            actions.Controls.AddRange(new Control[]{retry,discard,import,reply});
        }
        internal void Refresh(P2PTransferStatus view){
            if(view!=null&&(last==null||!session.Paused))last=view;
            view=last??new P2PTransferStatus();
            bool prepared=session.PayloadHash!=null||session.Completed||!session.Deferred;
            preparation.Indeterminate=session.Sender&&session.Stage!=null&&session.PreparationTotal==0;
            preparation.Caption=prepared?T("Pacote preparado","Pack prepared"):session.Sender?T("Preparando pacote","Preparing pack"):T("Aguardando preparo do amigo","Waiting for your friend's pack");
            preparation.Value=prepared?100:session.PreparationTotal>0?(int)(session.PreparationBytes*100/session.PreparationTotal):0;
            string message=session.Error??"";
            if(session.Held||session.RecoveryPending)message=T("Arquivo preservado. Clique em Retomar quando quiser continuar.","File preserved. Click Resume when you are ready.");
            else if(session.Completed&&message.Length==0)message=session.Sender?T("Envio concluído","Send complete")
                :session.ProjectError??(session.ListImported?T("Instalação concluída. Projeto salvo.","Installation complete. Project saved."):T("Instalação concluída","Installation complete"));
            else if(message.Length==0&&session.Phase!=null)message=P2PService.PhaseLabel(session.Phase);
            else if(message.Length==0&&session.Paused)message=T("Pausado por você","Paused by you");
            else if(message.Length==0&&view.RemotePaused)message=T("Pausado pelo amigo","Paused by friend");
            else if(message.Length==0&&!view.Connected&&view.Recipients.Length==0)message=T("Aguardando conexão","Waiting for connection");
            detail.Text=message;detail.ForeColor=session.Error==null?GeneralStyle.Foreground:GeneralStyle.Danger;
            owner.tips.SetToolTip(detail,message);
            var installing=!session.Sender&&session.Stage!=null&&session.Phase=="installing";
            var installProgress=session.InstallProgress;
            var entries=view.Recipients;
            if(installing){
                string phase=installProgress!=null&&installProgress.Writing?T("Instalando arquivos","Installing files"):T("Verificando pacote","Verifying pack");
                int count=installProgress==null?0:installProgress.Total;
                entries=new[]{new P2PRecipientProgress{
                    Caption=phase+(count>0?" · "+installProgress.Done+"/"+count:""),
                    Percent=count>0?installProgress.Done*100/count:0,Finished=true
                }};
                message=installProgress==null||installProgress.File==null?phase:installProgress.File;
                detail.Text=message;owner.tips.SetToolTip(detail,message);
            }
            preparation.Visible=!installing;
            layout.RowStyles[1].Height=installing?0:42;
            if(entries.Length==0)entries=new[]{new P2PRecipientProgress{
                Caption=session.Sender?T("Envio do pacote","Pack upload"):T("Recebimento do pacote","Pack download"),
                Percent=session.Completed||session.Phase!=null?100:view.Percent,
                Finished=session.Completed||session.Phase!=null
            }};
            int pauseRow=Array.FindIndex(entries,item=>!item.Finished);
            bool canPause=pauseRow>=0&&!session.Completed&&!session.Held&&!session.RecoveryPending&&(session.Sender||session.Stage==null);
            bars.SuspendLayout();
            // Collapse the grid column as well as its button so completed bars recover the full width.
            bars.ColumnStyles[1].Width=canPause?52:0;
            pause.Visible=canPause;
            pause.Enabled=canPause&&!owner.busy&&owner.service.Engine.Running&&(!view.RemotePaused||session.Paused);
            ((GeneralButton)pause).IconKind=session.Paused?"play":"pause";
            pause.AccessibleName=session.Paused?T("Retomar","Resume"):view.RemotePaused?T("Pausado pelo amigo","Paused by friend"):T("Pausar","Pause");
            owner.tips.SetToolTip(pause,pause.AccessibleName);pause.Invalidate();
            while(progress.Count<entries.Length){
                var bar=new GeneralProgress{Dock=DockStyle.Fill,Margin=Padding.Empty,BackColor=GeneralStyle.Surface,Name="p2p-progress"};
                bars.RowStyles.Add(new RowStyle(SizeType.Absolute,44));
                bars.Controls.Add(bar,0,progress.Count);progress.Add(bar);
            }
            bars.RowCount=Math.Max(entries.Length,progress.Count);
            bars.SetRow(pause,canPause?pauseRow:0);
            for(int i=0;i<progress.Count;i++){
                var bar=progress[i];
                bar.Visible=i<entries.Length;bars.RowStyles[i].Height=i<entries.Length?44:0;
                if(i>=entries.Length)continue;
                bars.SetColumnSpan(bar,canPause&&i==pauseRow?1:2);
                bar.Indeterminate=installing&&(installProgress==null||installProgress.Total==0);
                bar.Caption=entries[i].Caption;bar.Value=entries[i].Percent;
            }
            bars.ResumeLayout(true);
            bool showRetry=!session.Completed&&!session.RecoveryPending&&(session.Held||session.Error!=null);
            bool showDiscard=!session.Sender&&!session.Completed&&session.Stage==null&&!session.RecoveryPending;
            bool showImport=!session.Sender&&session.Completed&&!session.ListImported&&session.ReceiptId!=null&&
                (owner.service.RegisterProject==null||session.ProjectError!=null);
            if(owner.service.RegisterProject!=null)import.Text=T("Salvar projeto","Save project");
            bool showReply=!session.Sender&&!session.Completed&&!session.Automatic&&P2PInvite.DeviceValid(owner.service.Engine.Device);
            retry.Visible=showRetry;discard.Visible=showDiscard;import.Visible=showImport;reply.Visible=showReply;
            foreach(Control action in actions.Controls)action.Enabled=!owner.busy;
            bool showDismiss=session.Sender&&session.Completed;
            dismiss.Visible=showDismiss;
            dismiss.Enabled=showDismiss&&!owner.busy;
            int titleHeight=showDismiss?dismiss.MinimumSize.Height:28;
            layout.RowStyles[0].Height=titleHeight;
            int actionsHeight=showRetry||showDiscard||showImport||showReply?50:0;
            layout.RowStyles[2].Height=44*entries.Length;
            layout.RowStyles[3].Height=message.Length==0?0:28;
            layout.RowStyles[4].Height=actionsHeight;
            Height=Frame.Padding.Vertical+titleHeight+(installing?0:42)+44*entries.Length+(message.Length==0?0:28)+actionsHeight;
        }
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
                    p2p=new P2PService(Path.Combine(data,"P2P"),P2PService.TransferStore,offerRecovery:true);
                    p2p.EnableInstalledReceive(catalog,data);
                    p2p.ChooseVersion=difference=>GeneralVersionChoiceDialog.Ask(SharingPromptOwner,difference);
                }
                p2p.SetPreparationState(state);
                Action<P2PSession> installed=session=>{
                    if(IsDisposed||!IsHandleCreated)return;
                    BeginInvoke((Action)(async()=>{
                        try{
                            if(sharingHome==null&&String.Equals(state.Destination,session.Destination,StringComparison.OrdinalIgnoreCase)){
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
            using(var dialog=new GeneralP2PDialog(p2p,state,catalog)){
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
