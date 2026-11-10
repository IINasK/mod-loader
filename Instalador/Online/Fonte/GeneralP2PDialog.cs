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
    readonly Func<Task<List<string>>> findCyberpunk;
    int destinationRequest;
    bool findingDestination;
    readonly TextBox output=new TextBox{ReadOnly=true,WordWrap=false,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,Name="p2p-output"};
    readonly TextBox input=new TextBox{WordWrap=false,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,MaxLength=12000,Name="p2p-input"};
    readonly TextBox destination=new TextBox{ReadOnly=true,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,Name="p2p-destination"};
    readonly Label sendLabel=new Label{Dock=DockStyle.Fill,Margin=Padding.Empty,Font=GeneralStyle.Font(11),Name="p2p-send-label"};
    string mismatchMessage;
    readonly Label status=new Label{Dock=DockStyle.Fill,UseMnemonic=false,Name="p2p-status"};
    readonly Timer timer=new Timer{Interval=1000};
    readonly ToolTip tips=new ToolTip{UseAnimation=false,UseFading=false};
    readonly TableLayoutPanel body;
    readonly Panel targetRow;
    readonly TableLayoutPanel targetLayout;
    readonly Label folderHint=new Label{Dock=DockStyle.Fill,UseMnemonic=false,AutoEllipsis=true,Margin=Padding.Empty,Name="p2p-folder-hint"};
    readonly Panel cardStack=new Panel{BackColor=GeneralStyle.Surface};
    readonly GeneralScrollView cardScroll;
    readonly Dictionary<P2PSession,TransferCard> cards=new Dictionary<P2PSession,TransferCard>();
    readonly Button connect,choose,generate,copy;
    P2PSession offer;
    bool busy,polling,reviewingVerification;
    internal GeneralModList ReceivedList {get;private set;}
    internal bool InstallAfterReceive {get{return false;}}
    internal string InstalledReceiptId {get;private set;}
    internal string InstalledDestination {get;private set;}
    static string T(string pt,string en){return GeneralStyle.T(pt,en);}

    internal static string InstalledMessage {get{return T("Download concluído e mods instalados. Pode iniciar o jogo.",
        "Download complete and mods installed. You can start the game.");}}

    internal static bool AskUserFolders(Control dispatcher,Func<Control> owner,string[] folders){
        if(dispatcher.IsDisposed||!dispatcher.IsHandleCreated)return false;
        return (bool)dispatcher.Invoke((Func<bool>)(()=>{
            string title=T("Pastas do usuário","User folders");
            string message=T("Instalar também nestas pastas do seu usuário?","Also install into these folders in your user profile?")
                +Environment.NewLine+Environment.NewLine+String.Join(Environment.NewLine,folders);
            return GeneralDialog.Message(owner(),message,title,MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.Yes;
        }));
    }

    internal static Task<bool> AskConnection(Control dispatch,Func<Control> promptOwner,P2PService service,P2PSession session){
        var decision=new TaskCompletionSource<bool>();
        if(dispatch.IsDisposed||!dispatch.IsHandleCreated)return Task.FromResult(false);
        try{
            dispatch.BeginInvoke((Action)(()=>{
                var owner=promptOwner();
                if(owner==null||owner.IsDisposed||session.Discarded||session.Completed){decision.TrySetResult(false);return;}
                try{
                    using(var dialog=new GeneralDialog(T("Conexão solicitada","Connection requested"))){
                        dialog.ClientSize=new Size(560,210);
                        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new Padding(24,12,24,16)};
                        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
                        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                        layout.Controls.Add(new Label{Dock=DockStyle.Fill,Text=session.Name+"\r\n\r\n"+
                            T("Seu amigo quer receber este pacote.","Your friend wants to receive this pack.")},0,0);
                        var actions=GeneralTableDialogLayout.Commands();
                        var decline=GeneralStyle.Button(T("Recusar","Decline"),()=>dialog.Complete(DialogResult.No));
                        var accept=GeneralStyle.Button(T("Aceitar","Accept"),()=>dialog.Complete(DialogResult.Yes),true);
                        decline.Name="p2p-decline-request";accept.Name="p2p-accept-request";
                        actions.Controls.Add(decline);actions.Controls.Add(accept);
                        layout.Controls.Add(actions,0,1);dialog.Content.Controls.Add(layout);
                        dialog.CancelButton=decline;
                        GeneralStyle.Apply(dialog,GeneralStyle.Dark);
                        service.Notify();
                        decision.TrySetResult(dialog.ShowDialog(owner)==DialogResult.Yes);
                    }
                }catch(Exception ex){
                    OnlineDiagnostics.Record("p2p.approval",null,ex.GetType().Name);
                    decision.TrySetResult(false);
                }
            }));
        }catch(InvalidOperationException){decision.TrySetResult(false);}
        return decision.Task;
    }

    internal GeneralP2PDialog(P2PService service,GeneralModState state,FixedCatalog catalog,bool autoStart=true,Func<Task<List<string>>> findCyberpunk=null):base(T("Modpack e compartilhamento","Modpack and sharing")){
        this.service=service;this.state=state;this.autoStart=autoStart;this.catalog=catalog;
        this.findCyberpunk=findCyberpunk??(()=>Task.Run(()=>Discovery.Find()));
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
        sendLabel.Text=T("Link para enviar ao amigo","Link to send to your friend");
        body.Controls.Add(sendLabel,0,1);
        var send=Field(output);send.Padding=new Padding(14,16,72,8);
        copy=GeneralStyle.Button("",CopyInvitation);IconButton(copy,"copy",T("Copiar link","Copy link"));copy.Name="p2p-copy";
        if(state.List.Mods.Count==0){sendLabel.Visible=false;send.Visible=false;body.RowStyles[1].Height=0;body.RowStyles[2].Height=0;}
        send.Controls.Add(copy);send.SizeChanged+=(s,e)=>copy.Location=new Point(send.ClientSize.Width-62,6);body.Controls.Add(send,0,2);
        body.Controls.Add(Heading(T("Link recebido do amigo","Link received from your friend")),0,3);
        var receive=Field(input);receive.Padding=new Padding(14,16,14,8);body.Controls.Add(receive,0,4);
        targetRow=new Panel{Dock=DockStyle.Fill,Margin=new Padding(0,0,0,Gap)};
        targetLayout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=3,Margin=Padding.Empty};
        targetLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));targetLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        targetLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,LabelHeight));targetLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,ControlHeight));
        targetLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,0));
        var targetLabel=Heading(T("Instalar na pasta local","Install into local folder"));targetLayout.Controls.Add(targetLabel,0,0);targetLayout.SetColumnSpan(targetLabel,2);
        var destinationField=Field(destination);destinationField.Margin=Padding.Empty;targetLayout.Controls.Add(destinationField,0,1);
        choose=GeneralStyle.Button(T("Escolher pasta","Choose folder"),()=>{try{ChooseFolder();}catch(Exception ex){Failed(ex);}});choose.Margin=new Padding(8,0,0,0);choose.Name="p2p-folder";targetLayout.Controls.Add(choose,1,1);
        targetLayout.Controls.Add(folderHint,0,2);targetLayout.SetColumnSpan(folderHint,2);
        targetRow.Controls.Add(targetLayout);body.Controls.Add(targetRow,0,5);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Margin=Padding.Empty,Name="p2p-actions"};
        connect=GeneralStyle.Button(T("Conectar","Connect"),()=>Run(Connect),true);connect.Name="p2p-connect";connect.Margin=new Padding(8,0,0,0);actions.Controls.Add(connect);
        generate=GeneralStyle.Button(T("Gerar modpack","Generate modpack"),()=>Run(GenerateModpack));generate.Name="modpack-generate";generate.Margin=Padding.Empty;actions.Controls.Add(generate);
        var diagnostics=GeneralStyle.Button(T("Diagnóstico P2P","P2P diagnostics"),ShowDiagnostics);
        diagnostics.Name="p2p-diagnostic-options";diagnostics.Margin=new Padding(8,0,0,0);actions.Controls.Add(diagnostics);
        body.Controls.Add(actions,0,6);body.Controls.Add(status,0,7);
        status.Margin=new Padding(0,Gap,0,0);
        body.Controls.Add(cardScroll,0,9);body.SetColumnSpan(cardScroll,2);
        cardStack.ClientSizeChanged+=(s,e)=>LayoutCards();
        output.TextChanged+=(s,e)=>UpdateActions();status.TextChanged+=(s,e)=>UpdateActions();
        input.TextChanged+=async(s,e)=>await FindIncomingDestination();
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
    void CopyInvitation(){
        if(CopyOpenInvitation())return;
        Run(async()=>{await PrepareLink();CopyOpenInvitation();});
    }
    bool CopyOpenInvitation(){
        string code=service.OpenInvitation(offer);
        if(code==null)return false;
        try{
            Clipboard.SetText(code);
            service.ArmConnectionDiagnostic(offer.Id,true);
        }catch(System.Runtime.InteropServices.ExternalException ex){Failed(ex);}
        return true;
    }
    void ShowDiagnostics(){
        using(var dialog=new GeneralP2PDiagnosticDialog(service))dialog.ShowDialog(this);
    }
    void CopyDiagnostic(P2PSession session){
        var report=service.GetConnectionDiagnostic(session.Id);
        if(report==null)return;
        try{
            Clipboard.SetText(Safe.Json.Serialize(report));
            status.ForeColor=GeneralStyle.Foreground;
            status.Text=T("Diagnóstico copiado · ","Diagnostics copied · ")+report.Id;
        }catch(Exception ex){Failed(ex);}
    }
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
        copy.Enabled=!busy&&output.Text.StartsWith("ML-P2P1:",StringComparison.Ordinal)&&service.InvitationOpen(offer);
        connect.Enabled=!busy&&!findingDestination&&!String.IsNullOrWhiteSpace(input.Text);
        choose.Enabled=!busy;
        var incoming=IncomingInvite();
        string hint="";
        if(incoming!=null&&destination.Text.Length==0){
            if(findingDestination)hint=T("Procurando o Cyberpunk 2077 neste PC…","Looking for Cyberpunk 2077 on this PC…");
            else if(IsCyberpunkOffer(incoming))hint=T("Escolha a pasta que contém o executável do Cyberpunk 2077.","Choose the folder that contains the Cyberpunk 2077 executable.");
            else if(incoming.PrivateFolder)hint=T("O anfitrião usa uma pasta pessoal. Escolha a pasta do jogo neste PC.",
                "The host uses a personal folder. Choose the game folder on this PC.");
            else if(incoming.SuggestedFolder!=null)hint=T("A pasta sugerida não está disponível neste PC. Escolha a pasta do jogo.",
                "The suggested folder is not available on this PC. Choose the game folder.");
        }
        body.SuspendLayout();
        folderHint.Text=hint;tips.SetToolTip(folderHint,hint);
        folderHint.Visible=hint.Length!=0;
        targetLayout.RowStyles[2].Height=hint.Length==0?0:LabelHeight;
        targetRow.Visible=incoming!=null;body.RowStyles[5].Height=incoming==null?0:TargetHeight+(int)targetLayout.RowStyles[2].Height;
        status.Visible=status.Text.Length!=0;body.RowStyles[7].Height=status.Text.Length==0?0:Gap+42;
        body.ResumeLayout(false);
        FitCards();
    }
    void FitCards(){
        int chrome=HeaderHeight+Padding.Vertical+body.Padding.Vertical+(int)body.RowStyles.Cast<RowStyle>().Take(8).Sum(row=>row.Height);
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
    P2PInvite IncomingInvite(){
        try{var invite=P2PInvite.Decode(input.Text);return invite.Kind=="offer"?invite:null;}
        catch(IOException){return null;}
    }
    bool IsCyberpunkOffer(P2PInvite invite){
        return invite!=null&&(invite.Game==P2PInvite.CyberpunkGame||
            (invite.Game==null&&state.TemplateId==GeneralProjectStore.CyberpunkTemplate));
    }

    async Task FindIncomingDestination(){
        int request=++destinationRequest;
        var incoming=IncomingInvite();
        string suggested=incoming==null?null:incoming.LocalSuggestedFolder();
        bool cyberpunk=IsCyberpunkOffer(incoming);
        destination.Text=(cyberpunk
            ? P2PInvite.CyberpunkFolder(state.Destination)??P2PInvite.CyberpunkFolder(suggested)
            : suggested)??"";
        findingDestination=cyberpunk&&destination.Text.Length==0;
        UpdateActions();
        if(!findingDestination)return;
        string found=null;
        try{
            var folders=(await findCyberpunk()).Select(P2PInvite.CyberpunkFolder)
                .Where(folder=>folder!=null).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
            if(folders.Length==1)found=folders[0];
        }catch(Exception ex){OnlineDiagnostics.Record("p2p.destination.lookup",null,ex.GetType().Name);}
        if(IsDisposed||request!=destinationRequest)return;
        if(destination.Text.Length==0&&found!=null)destination.Text=found;
        findingDestination=false;
        UpdateActions();
    }

    string CheckedDestination(string path,P2PInvite incoming){
        string folder=WindowsFolderPicker.InstallationPath(path);
        if(IsCyberpunkOffer(incoming)&&P2PInvite.CyberpunkFolder(folder)==null)
            throw new IOException(T("Escolha a pasta do Cyberpunk 2077 que contém bin\\x64\\Cyberpunk2077.exe.",
                "Choose the Cyberpunk 2077 folder containing bin\\x64\\Cyberpunk2077.exe."));
        return folder;
    }

    void ChooseFolder(){
        string selected=WindowsFolderPicker.Choose(this,T("Pasta de instalação","Installation folder"),destination.Text);
        if(selected==null)return;
        destination.Text=CheckedDestination(selected,IncomingInvite());
        UpdateActions();
    }
    async Task PrepareLink(){
        var selected=ExportIds;
        string preset=ExportPreset;
        sendLabel.Text=T("Link para enviar ao amigo","Link to send to your friend");
        if(selected.Length==0){offer=null;output.Clear();return;}
        output.Text=T("Iniciando conexão…","Starting connection…");
        try{if(!service.Engine.Running)await Task.Run(()=>service.Start(Report));}
        catch{output.Clear();throw;}
        var sessions=service.SnapshotSessions();
        offer=sessions.LastOrDefault(s=>service.InvitationOpen(s)&&s.Deferred&&s.CompareBeforePack&&s.ExportPreset==preset&&
            (preset!=null||s.ExportAllPresets||state.SelectedPreset==null)&&s.SelectedMods!=null&&
            new HashSet<string>(s.SelectedMods,StringComparer.Ordinal).SetEquals(selected));
        if(offer==null){
            service.SetPreparationState(state);
            offer=await Task.Run(()=>service.BeginDeferred(state,selected,true,preset,preset==null,compareBeforePack:true));
        }else await Task.Run(()=>service.UpgradeInvitation(offer));
        output.Text=T("Preparando rota P2P…","Preparing P2P route…");
        await Task.Run(()=>service.Engine.PrepareInviteRoute());
        string code=service.OpenInvitation(offer);
        if(code==null){output.Clear();return;}
        sendLabel.Text+= " · "+P2PService.DiagnosticId(offer.Id);
        output.Text=code;status.Text="";
    }
    async Task Connect(){
        var invite=P2PInvite.Decode(input.Text.Trim());
        if(invite.Mode!="installed-zip")throw new IOException(T("Este link usa o formato antigo. Peça um novo link ao amigo.","This link uses the old format. Ask your friend for a new link."));
        string code=input.Text.Trim();
        if(invite.Kind=="offer"){
            if(!Directory.Exists(destination.Text))ChooseFolder();
            if(!Directory.Exists(destination.Text))throw new IOException(T("Escolha a pasta de instalação.","Choose the installation folder."));
            string target=CheckedDestination(destination.Text,invite);
            string prompt=invite.Schema==3?T("Comparar os mods do pacote ","Compare mods from the pack "):T("Receber e instalar o pacote ","Receive and install pack ");
            service.Notify();
            if(GeneralDialog.Message(this,prompt+invite.Name+T(" em:\n"," into:\n")+target+T("\n\nMantenha o jogo fechado. Configurações anteriores terão backup.","\n\nKeep the game closed. Previous settings will be backed up."),T("Conectar ao anfitrião","Connect to the host"),MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
            service.ArmConnectionDiagnostic(invite.Folder,false);
            P2PSession session;
            try{
                if(!service.Engine.Running)await Task.Run(()=>service.Start(Report));
                session=await Task.Run(()=>{
                    var accepted=service.Accept(code,target,true);
                    if(!accepted.Completed&&(accepted.Held||accepted.Error!=null||!accepted.AutoInstall||
                        !String.Equals(accepted.Destination,target,StringComparison.OrdinalIgnoreCase)))service.RetryInstalled(accepted,target);
                    return accepted;
                });
            }catch(Exception ex){
                service.ConnectionDiagnosticPhase(invite.Folder,"local_failure",ex);
                throw;
            }
            if(session.Completed){
                input.Clear();
                status.ForeColor=GeneralStyle.Foreground;
                status.Text=T("Este convite já foi concluído. Peça um novo convite para sincronizar novamente.",
                    "This invitation was already completed. Ask for a new invitation to sync again.");
                return;
            }
        }else{
            if(!service.Engine.Running)await Task.Run(()=>service.Start(Report));
            await Task.Run(()=>service.Authorize(code));
        }
        input.Clear();
        status.ForeColor=GeneralStyle.Foreground;
        status.Text=invite.Kind=="accept"?T("Resposta confirmada. Conectando ao amigo…","Reply confirmed. Connecting to your friend…"):"";
    }
    async void ChooseIncoming(P2PSession session,bool full){
        try{await service.ChooseIncoming(session,full,comparison=>{
            if(IsDisposed||!IsHandleCreated)return null;
            return (int[])Invoke((Func<int[]>)(()=>ReviewSyncExtras(comparison)));
        });}
        catch(Exception ex){if(!IsDisposed)Failed(ex);}
        if(!IsDisposed)await Poll();
    }
    int[] ReviewSyncExtras(P2PSyncComparison comparison){
        if(IsDisposed||comparison.Session.Discarded)return null;
        service.Notify();
        using(var dialog=new GeneralSyncReviewDialog(comparison)){
            var answer=dialog.ShowDialog(this);
            return answer==DialogResult.Yes?dialog.SelectedExtras:answer==DialogResult.No?new int[0]:null;
        }
    }

    async void ShowSyncDifferences(P2PSession session) {
        try {
            var differences = await Task.Run(() => service.ReadSyncDifferences(session));
            if (IsDisposed || !Visible || session.HistoryDismissed) return;
            using (var dialog = new GeneralSyncDifferencesDialog(differences))
                dialog.ShowDialog(this);
        } catch (Exception ex) { if (!IsDisposed) Failed(ex); }
    }
    void Retry(P2PSession session){Run(async()=>{
        if(!service.Engine.Running)await Task.Run(()=>service.Start(Report));
        await Task.Run(()=>{if(session.Sender)service.RetryPreparation(session);else service.RetryInstalled(session,session.Destination);});
    });}
    void CloseTransfer(P2PSession session){
        if(!session.Completed&&!session.Sender){Discard(session);return;}
        Run(()=>Task.Run(()=>{
            if(session.Completed)service.DismissCompletedTransfer(session);
            else service.CancelSend(session);
        }));
    }
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
    void AskToSkipVerification(){
        if(reviewingVerification||IsDisposed||!Visible)return;
        var issue=service.SnapshotSessions().Where(session=>!session.Discarded).Select(session=>session.VerificationIssue)
            .FirstOrDefault(pending=>pending!=null&&!pending.Decision.Task.IsCompleted);
        if(issue==null)return;
        reviewingVerification=true;
        service.Notify();
        try{
            var answer=GeneralDialog.Message(this,T("Erro ao verificar o mod ","Error verifying mod ")+issue.ModName+"\n\n"+issue.Message+"\n\n"+
                T("Deseja pular este mod e continuar? Mods que dependem dele também ficarão fora.","Skip this mod and continue? Mods that depend on it will also be left out."),
                T("Conferência dos mods","Mod verification"),MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2);
            issue.Decision.TrySetResult(answer==DialogResult.Yes);
        }finally{reviewingVerification=false;}
    }
    async Task Poll(){
        if(busy||polling||IsDisposed||ReceivedList!=null)return;
        polling=true;
        try{
            var sessions=service.SnapshotSessions().Where(s=>!s.Discarded&&!s.HistoryDismissed&&s.Mode=="installed-zip"&&
                (s.Peer!=null||s.Recipients.Length!=0)).ToArray();
            var views=await Task.Run(()=>{
                var result=new Dictionary<P2PSession,P2PTransferStatus>();
                if(!service.Engine.Running){foreach(var session in sessions)result.Add(session,new P2PTransferStatus{Unavailable=true});return result;}
                foreach(var session in sessions){
                    try{result.Add(session,service.TransferStatus(session));}
                    catch(System.Net.WebException){result.Add(session,new P2PTransferStatus{Unavailable=true});}
                }
                return result;
            });
            if(IsDisposed)return;
            RefreshCards(views);
            string unmatched=service.UnmatchedInvitationId;
            if(status.Text.Length==0||status.Text==mismatchMessage){
                mismatchMessage=unmatched!=null&&service.InvitationOpen(offer)
                    ?T("Pedido para outro convite (","Request for another invitation (")+unmatched+
                        T("). Copie e envie o link atual acima.","). Copy and send the current link above.")
                    :"";
                if(status.Text!=mismatchMessage){
                    status.ForeColor=GeneralStyle.Danger;status.Text=mismatchMessage;
                    tips.SetToolTip(status,mismatchMessage);
                }
            }
            UpdateActions();
        }catch(Exception ex){if(!IsDisposed)Failed(ex);}
        finally{polling=false;}
        AskToSkipVerification();
        if(!IsDisposed&&autoStart&&offer!=null&&(offer.Completed||offer.Discarded||offer.CompareBeforePack&&offer.Recipients.Length>0)&&!busy){
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
        readonly LinkLabel detail=new LinkLabel{
            Dock=DockStyle.Fill,AutoEllipsis=true,Margin=Padding.Empty,UseMnemonic=false,Name="p2p-transfer-status",
            UseCompatibleTextRendering=false,LinkArea=new LinkArea(0,0)
        };
        readonly FlowLayoutPanel actions=new FlowLayoutPanel{Dock=DockStyle.Fill,Margin=Padding.Empty,WrapContents=false};
        readonly Button pause,retry,import,reply,dismiss,update,full,diagnostic;
        P2PTransferStatus last;
        DateTime? waitingSince;
        internal int Order,Height;

        internal TransferCard(GeneralP2PDialog owner,P2PSession session){
            this.owner=owner;this.session=session;
            detail.LinkColor=detail.VisitedLinkColor=GeneralStyle.Accent;
            detail.ActiveLinkColor=GeneralStyle.Foreground;
            detail.LinkClicked+=(s,e)=>owner.ShowSyncDifferences(session);
            Frame.Padding=new Padding(16,12,16,12);Frame.AccessibleName=session.ToString();
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach(int height in new[]{28,42,42,0,0})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
            layout.RowCount=5;Frame.Controls.Add(layout);
            var title=Heading(session.ToString());title.Font=GeneralStyle.Font(13,true);title.AutoEllipsis=true;title.UseMnemonic=false;
            title.TextAlign=ContentAlignment.MiddleLeft;
            var header=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty};
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            diagnostic=GeneralStyle.Button("",()=>owner.CopyDiagnostic(session));
            owner.IconButton(diagnostic,"copy",T("Copiar diagnóstico","Copy diagnostics"));
            diagnostic.Name="p2p-copy-diagnostic";diagnostic.Visible=false;
            dismiss=GeneralStyle.Button("",()=>owner.CloseTransfer(session));
            owner.IconButton(dismiss,"close",T("Cancelar transferência","Cancel transfer"));dismiss.Name="p2p-dismiss";
            header.Controls.Add(title,0,0);header.Controls.Add(diagnostic,1,0);header.Controls.Add(dismiss,2,0);
            layout.Controls.Add(header,0,0);layout.Controls.Add(preparation,0,1);layout.Controls.Add(bars,0,2);
            layout.Controls.Add(detail,0,3);layout.Controls.Add(actions,0,4);
            bars.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            bars.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,0));
            pause=GeneralStyle.Button("",()=>owner.Run(()=>Task.Run(()=>owner.service.Pause(session))));
            owner.IconButton(pause,"pause",T("Pausar","Pause"));pause.Name="p2p-pause";bars.Controls.Add(pause,1,0);
            retry=GeneralStyle.Button(T("Retomar","Resume"),()=>owner.Retry(session));retry.Name="p2p-retry";
            import=GeneralStyle.Button(T("Importar lista","Import list"),()=>owner.Import(session));import.Name="p2p-import-list";
            reply=GeneralStyle.Button(T("Copiar resposta","Copy reply"),()=>{
                owner.Copy(owner.service.Invitation(session));
                owner.status.ForeColor=GeneralStyle.Foreground;
                owner.status.Text=T("Envie a resposta ao anfitrião. Ele deve colar em Link recebido do amigo e clicar em Conectar.","Send the reply to the host. They paste it in Link received from your friend and click Connect.");
                owner.UpdateActions();
            });reply.Name="p2p-reply";
            update=GeneralStyle.Button(T("Atualizar mods","Update mods"),()=>owner.ChooseIncoming(session,false),true);
            update.Name="p2p-update";
            full=GeneralStyle.Button(T("Pacote completo","Full pack"),()=>owner.ChooseIncoming(session,true));
            full.Name="p2p-full";
            actions.Controls.AddRange(new Control[]{update,full,retry,import,reply});
        }
        internal void Refresh(P2PTransferStatus view){
            if(view!=null&&(last==null||!session.Paused))last=view;
            view=last??new P2PTransferStatus();
            bool prepared=session.PayloadHash!=null||session.Completed||!session.Deferred;
            bool awaiting=session.CompareBeforePack&&!session.Completed&&session.PayloadHash==null&&!owner.service.HasSyncChoice(session);
            bool preview=awaiting&&owner.service.HasSyncPreview(session);
            bool waitingConnection=!session.Completed&&!session.Paused&&!view.RemotePaused&&session.Phase==null&&!preview&&
                (!view.Connected||!view.Accepted);
            if(waitingConnection&&!waitingSince.HasValue)waitingSince=DateTime.UtcNow;
            if(!waitingConnection)waitingSince=null;
            bool delayed=view.HostDecision==null&&waitingSince.HasValue&&(DateTime.UtcNow-waitingSince.Value).TotalSeconds>=30;
            diagnostic.Visible=(delayed||view.Unavailable||session.Error!=null)&&owner.service.GetConnectionDiagnostic(session.Id)!=null;
            var scan=session.SyncProgress;
            var issue=session.VerificationIssue;
            bool scanning=scan!=null||session.Sender&&awaiting&&!preview&&session.Stage!=null;
            bool waitingForHost=!session.Sender&&session.CompareBeforePack&&!prepared&&session.Error==null&&
                !session.Held&&!session.RecoveryPending&&!session.Paused&&!view.RemotePaused&&(!awaiting||session.SyncMode!=0);
            if(scanning){
                string phase=session.Sender?T("Conferindo mods do anfitrião","Checking host mods"):T("Comparando seus arquivos","Comparing your files");
                preparation.Caption=phase+(scan!=null&&scan.Total>0?" · "+scan.Done+"/"+scan.Total:"");
                preparation.Indeterminate=scan==null||scan.Total==0;
                preparation.Value=scan!=null&&scan.Total>0?scan.Done*100/scan.Total:0;
            }else{
                preparation.Indeterminate=waitingForHost||session.Sender&&session.Stage!=null&&session.PreparationTotal==0;
                preparation.Caption=prepared?T("Pacote preparado","Pack prepared"):session.Sender?T("Preparando pacote","Preparing pack"):
                    awaiting?T("Aguardando conferência do anfitrião","Waiting for host verification"):T("Aguardando preparo do anfitrião","Waiting for the host to prepare the pack");
                preparation.Value=prepared?100:session.PreparationTotal>0?(int)(session.PreparationBytes*100/session.PreparationTotal):0;
            }
            bool syncDifferences=session.SyncLocalExtrasKept>0||session.SyncHostFilesDifferent>0;
            bool ready=!session.Sender&&session.Completed&&session.ListImported&&session.ProjectError==null&&session.ModsOutsidePack==0&&!syncDifferences;
            string differencesLink=T("Ver diferenças fora do pacote","View differences outside the pack");
            string message=session.Error??"";
            if(session.Held||session.RecoveryPending)message=T("Arquivo preservado. Clique em Retomar quando quiser continuar.","File preserved. Click Resume when you are ready.");
            else if(session.Completed&&message.Length==0)message=session.Sender?T("Envio concluído","Send complete")
                :session.ProjectError??(ready?InstalledMessage
                    :session.ListImported&&syncDifferences?T("Projeto salvo · ","Project saved · ")+differencesLink
                    :session.ListImported&&session.ModsOutsidePack>0?T("Projeto salvo. Mods fora do pacote: ","Project saved. Mods outside the pack: ")+session.ModsOutsidePack
                    :session.ListImported?T("Instalação concluída. Projeto salvo.","Installation complete. Project saved."):T("Instalação concluída","Installation complete"));
            else if(message.Length==0&&session.NexusReviewing)message=T("Conferindo a lista no Nexus…","Checking the list on Nexus…");
            else if(message.Length==0&&session.Phase!=null)message=P2PService.PhaseLabel(session.Phase);
            else if(message.Length==0&&session.Paused)message=T("Pausado por você","Paused by you");
            else if(message.Length==0&&issue!=null)
                message=T("Erro ao verificar: ","Verification failed: ")+issue.ModName;
            else if(message.Length==0&&scanning&&scan!=null&&scan.Item!=null)message=scan.Item;
            else if(message.Length==0&&waitingConnection){
                if(view.Unavailable)message=T("Sem resposta do serviço P2P local. Reabra o Mod Loader se persistir.","Local P2P service unavailable. Reopen Mod Loader if this persists.");
                else if(view.HostDecision==P2PService.HostWaiting)message=T("Pedido recebido. Aguardando a decisão do anfitrião…",
                    "Request received. Waiting for the host to decide…");
                else if(view.HostDecision==P2PService.HostApproved)message=T("Convite aceito. Finalizando a conexão…",
                    "Invitation accepted. Finishing the connection…");
                else if(view.HostDecision==P2PService.HostDeclined)message=T("O anfitrião recusou o convite.","The host declined the invitation.");
                else if(!session.Sender&&view.Connected)message=T("Conectado ao anfitrião. Aguardando resposta ao convite…",
                    "Connected to the host. Waiting for a reply to the invitation…");
                else if(delayed)message=T("A conexão está demorando. Ainda tentando…","Connection is taking longer. Still trying…");
                else message=session.Sender?T("Convite aceito. Conectando ao amigo…","Invitation accepted. Connecting to your friend…")
                    :T("Conectando ao anfitrião…","Connecting to the host…");
            }
            else if(message.Length==0&&awaiting){
                if(session.Sender)message=preview?T("Aguardando a escolha do amigo","Waiting for your friend's choice"):T("Conferindo mods do anfitrião","Checking the host's mods");
                else if(session.SyncMode!=0)message=T("Aguardando a verificação do anfitrião","Waiting for the host to finish checking");
                else if(view.Accepted||preview)message=T("Conectado. Escolha as diferenças ou o pacote completo.","Connected. Choose differences or the full pack.");
                else message=T("Aguardando conexão com o anfitrião","Waiting to connect to the host");
            }
            else if(message.Length==0&&!session.Sender&&session.CompareBeforePack&&!prepared)message=T("Escolha enviada. Aguardando preparo do anfitrião.","Choice sent. Waiting for the host to prepare the pack.");
            else if(message.Length==0&&!session.CompareBeforePack&&!prepared)message=T("Convite antigo. Gere um novo convite para escolher como receber.","Older invitation. Create a new invitation to choose how to receive.");
            else if(message.Length==0&&view.RemotePaused)message=T("Pausado pelo amigo","Paused by friend");
            else if(message.Length==0&&!view.Connected&&view.Recipients.Length==0)message=T("Aguardando conexão","Waiting for connection");
            if(detail.Text!=message){
                detail.Text=message;
                int linkStart=message.LastIndexOf(differencesLink,StringComparison.Ordinal);
                detail.LinkArea=linkStart<0?new LinkArea(0,0):new LinkArea(linkStart,differencesLink.Length);
            }
            detail.ForeColor=session.Error!=null||session.ProjectError!=null||view.Unavailable?GeneralStyle.Danger
                :ready?(GeneralStyle.Dark?Color.PaleGreen:Color.DarkGreen):GeneralStyle.Foreground;
            owner.tips.SetToolTip(detail,message);
            var installing=!session.Sender&&session.Stage!=null&&session.Phase=="installing";
            var installProgress=session.InstallProgress;
            var entries=view.Recipients;
            if(installing){
                string phase=P2PService.InstallCaption(installProgress);
                int count=installProgress==null?0:installProgress.Total;
                entries=new[]{new P2PRecipientProgress{
                    Caption=phase,
                    Percent=count>0?installProgress.Done*100/count:0,Finished=true,Indeterminate=count==0
                }};
                message=installProgress==null||installProgress.File==null?"":installProgress.File;
                detail.Text=message;owner.tips.SetToolTip(detail,message);
            }
            bool showPreparation=!installing&&(!awaiting||scanning||waitingForHost);
            preparation.Visible=showPreparation;
            bars.Visible=!awaiting;
            layout.RowStyles[1].Height=showPreparation?42:0;
            if(entries.Length==0)entries=new[]{new P2PRecipientProgress{
                Caption=session.Sender?T("Envio do pacote","Pack upload"):T("Recebimento do pacote","Pack download"),
                Percent=session.Completed||session.Phase!=null?100:view.Percent,
                Finished=session.Completed||session.Phase!=null
            }};
            int pauseRow=Array.FindIndex(entries,item=>!item.Finished);
            bool canPause=!awaiting&&pauseRow>=0&&!session.Completed&&!session.Held&&!session.RecoveryPending&&(session.Sender||session.Stage==null);
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
                bar.Indeterminate=entries[i].Indeterminate;
                bar.Caption=entries[i].Caption;bar.Value=entries[i].Percent;
            }
            bars.ResumeLayout(true);
            bool showRetry=!session.Completed&&!session.RecoveryPending&&(session.Held||session.Error!=null);
            bool showDiscard=!session.Sender&&!session.Completed&&session.Stage==null&&!session.RecoveryPending;
            bool showImport=!session.Sender&&session.Completed&&!session.ListImported&&session.ReceiptId!=null&&
                (owner.service.RegisterProject==null||session.ProjectError!=null);
            if(owner.service.RegisterProject!=null)import.Text=T("Salvar projeto","Save project");
            bool showReply=!session.Sender&&!session.Completed&&session.Phase==null&&!session.Automatic&&P2PInvite.DeviceValid(owner.service.Engine.Device);
            bool showChoices=!session.Sender&&awaiting&&!session.Held&&!session.RecoveryPending&&session.Error==null;
            update.Visible=full.Visible=showChoices;
            retry.Visible=showRetry;import.Visible=showImport;reply.Visible=showReply;
            foreach(Control action in actions.Controls)action.Enabled=!owner.busy;
            update.Enabled=full.Enabled=showChoices&&!owner.busy&&session.SyncMode==0&&(view.Accepted||preview);
            if(update.Enabled)owner.service.NotifyChoice(session);
            dismiss.Visible=true;
            dismiss.Enabled=!owner.busy&&(session.Sender||session.Completed||showDiscard);
            dismiss.AccessibleName=session.Completed?T("Remover transferência concluída","Remove completed transfer")
                :session.Sender?T("Cancelar envio","Cancel send"):T("Cancelar recebimento","Cancel download");
            owner.tips.SetToolTip(dismiss,dismiss.AccessibleName);
            int titleHeight=dismiss.MinimumSize.Height;
            layout.RowStyles[0].Height=titleHeight;
            int actionsHeight=showChoices||showRetry||showImport||showReply?50:0;
            layout.RowStyles[2].Height=awaiting?0:44*entries.Length;
            layout.RowStyles[3].Height=message.Length==0?0:28;
            layout.RowStyles[4].Height=actionsHeight;
            Height=Frame.Padding.Vertical+titleHeight+(int)layout.RowStyles[1].Height+(int)layout.RowStyles[2].Height+(message.Length==0?0:28)+actionsHeight;
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
    internal bool CanApplyReceivedProject {get{return !working&&!scanning&&projectDraft==null&&!DraftNameChanged&&(Enabled||sharingDialog!=null);}}
    internal void ReloadReceivedProject(GeneralProject received){
        if(project==null||project.Id!=received.Id)return;
        var saved=projectStore.Load(received);
        GeneralModLibrary.RestoreKnownMappings(saved.List,seed);
        state.List=saved.List;state.Destination=saved.Destination;state.SelectedPreset=saved.SelectedPreset;
        state.ChosenVariants=saved.ChosenVariants;state.ResolvedDownloads=saved.ResolvedDownloads;
        state.CyberpunkImports=saved.CyberpunkImports;state.SnapshotReceipts=saved.SnapshotReceipts;
        marked.Clear();statuses.Clear();statusDetails.Clear();progress.Clear();
        ResetColumnFilters();CapturePresetDraft();RebuildPresets();
        BeginInvoke((Action)(async()=>{if(!IsDisposed&&!working&&!scanning)await Scan();}));
    }

    void PrepareP2P(){
            if(p2p==null){
                if(sharingHome!=null)p2p=sharingHome.SharingService(data);
                else{
                    p2p=new P2PService(Path.Combine(data,"P2P"),P2PService.TransferStore,offerRecovery:true);
                    p2p.EnableInstalledReceive(catalog,data);
                    p2p.ConfirmConnection=session=>GeneralP2PDialog.AskConnection(this,()=>SharingPromptOwner,p2p,session);
                    p2p.ChooseVersion=difference=>GeneralVersionChoiceDialog.Ask(SharingPromptOwner,difference);
                    p2p.ApproveUserFolders=folders=>GeneralP2PDialog.AskUserFolders(this,()=>SharingPromptOwner,folders);
                }
                p2p.SetPreparationState(CommittedProject());
                Action<P2PSession> installed=session=>{
                    if(IsDisposed||!IsHandleCreated)return;
                    BeginInvoke((Action)(async()=>{
                        try{
                            if(sharingHome==null&&String.Equals(state.Destination,session.Destination,StringComparison.OrdinalIgnoreCase)){
                                var received=p2p.InstalledManifest(session);
                                foreach(var mod in received.List.Mods)if(state.List.Mods.Any(m=>m.Id==mod.Id&&m.Variants.Any(v=>v.Id==mod.DefaultVariant)))state.SnapshotReceipts[mod.Id+"/"+mod.DefaultVariant]=session.ReceiptId;
                                Persist();if(!working&&!scanning)await Scan();
                            }
                            detail.Text=session.ModsOutsidePack==0&&session.SyncLocalExtrasKept==0&&session.SyncHostFilesDifferent==0
                                ?GeneralP2PDialog.InstalledMessage
                                :T("Pacote instalado. Confira as diferenças no compartilhamento.","Pack installed. Review differences in sharing.");
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
            if(!p2p.SnapshotSessions().Any(s=>!s.Completed&&(!s.Discarded||s.Sender)||Directory.Exists(p2p.Folder(s))))return;
            await Task.Run(()=>p2p.Start(_=>{}));
            if(!IsDisposed)detail.Text=T("Compartilhamento P2P retomado.","P2P sharing resumed.");
        }
        catch(Exception ex){if(!IsDisposed)detail.Text=T("Retomada P2P: ","P2P resume: ")+ex.Message;}
    }

    async void ShowP2P(){
        if(working||scanning||!ConfirmProjectNavigation())return;
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
