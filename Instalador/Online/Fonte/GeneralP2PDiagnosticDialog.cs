using System;
using System.Drawing;
using System.Windows.Forms;

internal sealed class GeneralP2PDiagnosticDialog : GeneralDialog {
    static string T(string pt,string en){return GeneralStyle.T(pt,en);}

    internal GeneralP2PDiagnosticDialog(P2PService service):base(T("Diagnóstico P2P","P2P diagnostics")){
        Name="p2p-diagnostic-dialog";
        ClientSize=new Size(620,340);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24,12,24,20),ColumnCount=1,RowCount=6};
                foreach(int height in new[]{32,26,44,34,40})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label{Dock=DockStyle.Fill,AutoEllipsis=true,TextAlign=ContentAlignment.MiddleLeft,Margin=Padding.Empty},0,0);
        var feedback=(Label)layout.GetControlFromPosition(0,0);
        var report=service.GetConnectionDiagnostic();
        feedback.Text=report==null?T("Nenhuma tentativa registrada.","No connection attempt recorded."):T("ID do diagnóstico: ","Diagnostics ID: ")+report.Id;
        layout.Controls.Add(new Label{Dock=DockStyle.Fill,Margin=Padding.Empty,Text=T("Endereço HTTPS para receber relatórios","HTTPS address for reports")},0,1);
        var settings=service.DiagnosticPreferences();
        var address=new TextBox{Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,MaxLength=2048,
            Text=settings.Endpoint??"",Name="p2p-diagnostic-endpoint"};
        var field=new GeneralSurface{Dock=DockStyle.Fill,Margin=Padding.Empty,Padding=new Padding(14,12,14,6)};
        field.Controls.Add(address);layout.Controls.Add(field,0,2);
        var enabled=new CheckBox{Dock=DockStyle.Fill,Margin=Padding.Empty,Checked=settings.Enabled,Name="p2p-diagnostic-enabled",
            Text=T("Enviar automaticamente falhas ao conectar ao anfitrião","Automatically report failures when connecting to the host")};
        layout.Controls.Add(enabled,0,3);
        var note=new Label{Dock=DockStyle.Fill,Margin=Padding.Empty,Text=T("O relatório inclui a versão e as etapas da conexão.",
            "The report includes the app version and connection steps.")};
        layout.Controls.Add(note,0,4);
        var actions=GeneralTableDialogLayout.Commands();
        var copy=GeneralStyle.Button(T("Copiar diagnóstico","Copy diagnostics"),()=>{
            var latest=service.GetConnectionDiagnostic();
            if(latest==null)return;
            try{
                Clipboard.SetText(Safe.Json.Serialize(latest));
                feedback.Text=T("Diagnóstico copiado · ","Diagnostics copied · ")+latest.Id;
            }catch(System.Runtime.InteropServices.ExternalException){feedback.Text=T("Não foi possível copiar. Tente novamente.","Could not copy. Try again.");}
        });
        copy.Name="p2p-diagnostic-copy";copy.Enabled=report!=null;
        var save=GeneralStyle.Button(T("Salvar","Save"),()=>{
            string value=address.Text.Trim();
            if((enabled.Checked||value.Length!=0)&&P2PDiagnosticPreferences.HttpsEndpoint(value)==null){
                note.Text=T("Informe um endereço HTTPS válido.","Enter a valid HTTPS address.");return;
            }
            try{
                service.SaveDiagnosticPreferences(new P2PDiagnosticPreferences{Enabled=enabled.Checked,Endpoint=value});
                Complete(DialogResult.OK);
            }catch(Exception){note.Text=T("Não foi possível salvar as opções.","Could not save the settings.");}
        },true);
        save.Name="p2p-diagnostic-save";
        actions.Controls.Add(copy);actions.Controls.Add(save);
        layout.Controls.Add(actions,0,5);
        Content.Controls.Add(layout);
        GeneralStyle.Apply(this,GeneralStyle.Dark);
    }
}
