using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class GeneralP2PDiagnosticDialog : GeneralDialog {
    static string T(string pt,string en){return GeneralStyle.T(pt,en);}

    static string Stage(string phase){
        switch(phase){
            case "local_failure":return T("Falha na conexão local","Local connection failed");
            case "local_service_unavailable":return T("Serviço P2P indisponível","P2P service unavailable");
            case "peer_unreachable":return T("Anfitrião desconectado","Host unreachable");
            case "peer_connected_waiting_for_approval":return T("Aguardando aprovação","Waiting for approval");
            case "choice_failed":return T("Falha na comparação","Comparison failed");
            case "transfer_failed":return T("Falha na transferência","Transfer failed");
            case "install_failed":return T("Falha na instalação","Installation failed");
            case "project_save_failed":return T("Falha ao salvar o projeto","Could not save the project");
            case "mods_outside_pack":return T("Mods fora do pacote","Mods outside the pack");
            case "manual":return T("Enviado pelo receptor","Sent by the receiver");
            default:return phase;
        }
    }

    internal GeneralP2PDiagnosticDialog(P2PService service,P2PSession selected=null):base(T("Diagnóstico P2P","P2P diagnostics")){
        Name="p2p-diagnostic-dialog";
        ClientSize=new Size(650,342);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24,12,24,20),ColumnCount=1,RowCount=5};
        foreach(int height in new[]{26,44,54})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,64));
        var sessions=service.SnapshotSessions().Where(item=>item.Lifecycle&&!item.Discarded&&!item.HistoryDismissed)
            .OrderByDescending(item=>item.ConnectedUtc??item.CreatedUtc,StringComparer.Ordinal).ToArray();
        var picker=new GeneralPicker{Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList,Margin=Padding.Empty,
            AccessibleName=T("Transferência","Transfer"),Name="p2p-diagnostic-session"};
        foreach(var session in sessions)picker.Items.Add(session+" · "+P2PService.DiagnosticId(session.Id));
        if(sessions.Length!=0)picker.SelectedIndex=selected==null?0:Math.Max(0,Array.IndexOf(sessions,selected));
        picker.Enabled=sessions.Length!=0;
        Func<P2PSession> current=()=>picker.SelectedIndex<0?null:sessions[picker.SelectedIndex];

        layout.Controls.Add(new Label{Dock=DockStyle.Fill,Margin=Padding.Empty,Text=T("Transferência","Transfer")},0,0);
        layout.Controls.Add(picker,0,1);
        layout.Controls.Add(new Label{Dock=DockStyle.Fill,Margin=Padding.Empty,
            Text=T("Falhas no recebimento são enviadas pelo P2P ao anfitrião.\nO relatório inclui versão, etapa e tipo de falha.",
                "Download failures are sent to the host over P2P.\nThe report includes the version, stage and error type.")},0,2);
        var feedback=new Label{Dock=DockStyle.Fill,Margin=Padding.Empty,Name="p2p-diagnostic-status"};
        layout.Controls.Add(feedback,0,3);
        var actions=GeneralTableDialogLayout.Commands();
        Func<P2PDiagnosticReport[]> reports=()=>{
            var session=current();
            if(session!=null){
                var queued=session.Sender?service.ReceivedDiagnostics(session):service.LocalDiagnostics(session);
                if(queued.Length!=0)return queued;
            }
            var local=service.GetConnectionDiagnostic(session==null?null:session.Id);
            return local==null?new P2PDiagnosticReport[0]:new[]{local};
        };
        var copy=GeneralStyle.Button(T("Copiar diagnóstico","Copy diagnostics"),()=>{
            var items=reports();
            if(items.Length==0)return;
            try{
                Clipboard.SetText(items.Length==1?Safe.Json.Serialize(items[0]):Safe.Json.Serialize(items));
                feedback.Text=T("Diagnóstico copiado.","Diagnostics copied.");
            }catch(System.Runtime.InteropServices.ExternalException){feedback.Text=T("Não foi possível copiar.","Could not copy.");}
        });
        copy.Name="p2p-diagnostic-copy";
        var send=GeneralStyle.Button(T("Enviar ao anfitrião","Send to host"),()=>{},true);
        send.Name="p2p-diagnostic-send";
        bool sending=false;
        bool sendFailed=false;
        Action refresh=()=>{
            var session=current();
            var incoming=session!=null&&session.Sender?service.ReceivedDiagnostics(session):new P2PDiagnosticReport[0];
            var local=session!=null&&!session.Sender?service.LocalDiagnostics(session):new P2PDiagnosticReport[0];
            var last=incoming.LastOrDefault()??local.LastOrDefault()??service.GetConnectionDiagnostic(session==null?null:session.Id);
            copy.Enabled=last!=null;
            send.Enabled=!sending&&service.CanSendDiagnostic(session);
            feedback.Text=incoming.Length>0?T("Relatórios recebidos neste convite: ","Reports received on this invitation: ")+incoming.Length+
                "\n"+T("Última etapa: ","Last stage: ")+Stage(incoming.Last().Phase):
                sendFailed?T("Não foi possível iniciar o envio. Confira a conexão P2P.","Could not start sending. Check the P2P connection."):
                local.Length>0?T("Relatórios disponíveis para enviar pelo P2P: ","Reports ready to share over P2P: ")+local.Length:
                last!=null?T("ID do diagnóstico: ","Diagnostics ID: ")+last.Id:
                T("Nenhum diagnóstico registrado.","No diagnostics recorded.");
        };
        send.Click+=async(s,e)=>{
            var session=current();
            sending=true;sendFailed=false;send.Enabled=false;
            try{
                await Task.Run(()=>service.SendDiagnostic(session));
                if(!service.Engine.Running)await Task.Run(()=>service.Start(_=>{}));
            }catch(Exception ex){
                sendFailed=true;
                OnlineDiagnostics.Record("p2p.diagnostic.action_failed",null,ex.GetType().Name);
            }finally{sending=false;if(!IsDisposed)refresh();}
        };
        picker.SelectedIndexChanged+=(s,e)=>{sendFailed=false;refresh();};
        actions.Controls.Add(copy);actions.Controls.Add(send);
        layout.Controls.Add(actions,0,4);
        Content.Controls.Add(layout);
        GeneralStyle.Apply(this,GeneralStyle.Dark);
        var timer=new Timer{Interval=2000};
        timer.Tick+=(s,e)=>{if(!sending)refresh();};
        Shown+=(s,e)=>{refresh();timer.Start();};
        Disposed+=(s,e)=>timer.Dispose();
        refresh();
    }
}
