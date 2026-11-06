using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

internal sealed class GeneralProjectDeleteChoice : GeneralDialog {
    readonly RadioButton remove=new RadioButton{Name="DeleteMods",AutoSize=true,Margin=new Padding(0,14,0,0)};
    internal bool RemoveMods {get{return remove.Checked;}}
    internal GeneralProjectDeleteChoice(string name):base(GeneralStyle.T("Excluir projeto","Delete project")){
        ClientSize=new Size(700,340);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(20,8,20,20)};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label{Text=name,AutoSize=true,Font=GeneralStyle.Font(14,true),MaximumSize=new Size(610,0),Margin=new Padding(0,0,0,18)},0,0);
        var app=new RadioButton{Name="DeleteAppOnly",Text=GeneralStyle.T("Excluir só do app — manter os mods instalados","Delete only from the app — keep installed mods"),Checked=true,AutoSize=true,Margin=Padding.Empty};
        remove.Text=GeneralStyle.T("Excluir do app e apagar os mods instalados","Delete from the app and erase installed mods");
        var choices=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=Padding.Empty};choices.Controls.Add(app);choices.Controls.Add(remove);layout.Controls.Add(choices,0,1);
        layout.Controls.Add(new Label{Text=GeneralStyle.T("Na próxima etapa você confere a exclusão. Não haverá Desfazer no app.","Review the deletion in the next step. There will be no Undo in the app."),AutoSize=true,MaximumSize=new Size(610,0),ForeColor=GeneralStyle.Muted,Margin=new Padding(0,18,0,8)},0,2);
        var buttons=GeneralTableDialogLayout.Commands();var next=GeneralStyle.Button(GeneralStyle.T("Continuar","Continue"),()=>Complete(DialogResult.OK),true);next.Name="ContinueDelete";
        var cancel=GeneralStyle.Button(GeneralStyle.T("Cancelar","Cancel"),()=>Complete(DialogResult.Cancel));cancel.Name="CancelDelete";buttons.Controls.Add(next);buttons.Controls.Add(cancel);layout.Controls.Add(buttons,0,3);Content.Controls.Add(layout);
        AcceptButton=cancel;CancelButton=cancel;GeneralStyle.Apply(this,GeneralStyle.Dark);
    }
}
internal sealed class GeneralProjectDeleteReview : GeneralDialog {
    internal GeneralProjectDeleteReview(string name,ProjectDeletePlan plan):base(GeneralStyle.T("Confirmar exclusão definitiva","Confirm permanent deletion")){
        ClientSize=new Size(760,510);MinimumSize=new Size(620,420);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(20,8,20,20)};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        string summary=plan==null?GeneralStyle.T("Excluir só do app. Os mods e arquivos do jogo continuarão no lugar.","Delete only from the app. Mods and game files will stay in place."):
            GeneralStyle.T("Apagar permanentemente ","Permanently erase ")+plan.Remove.Count+GeneralStyle.T(" arquivo(s) de mods e excluir o projeto do app."," mod file(s) and delete the project from the app.");
        layout.Controls.Add(new Label{Name="DeleteSummary",Text=name+"\r\n\r\n"+summary,AutoSize=true,MaximumSize=new Size(670,0),Margin=new Padding(0,0,0,12)},0,0);
        string text=GeneralStyle.T("Esta exclusão não tem Desfazer. Para recuperar o projeto, será necessário importar uma cópia; para recuperar mods apagados, será necessário reinstalá-los.","This deletion has no Undo. Recovering the project requires importing a copy; recovering erased mods requires reinstalling them.");
        if(plan!=null)text+="\r\n\r\n"+GeneralStyle.T("Destino: ","Destination: ")+plan.Root+"\r\n\r\n"+GeneralStyle.T("APAGAR","ERASE")+"\r\n"+String.Join("\r\n",plan.Remove.Select(file=>file.Path))+"\r\n\r\n"+GeneralStyle.T("PRESERVAR","PRESERVE")+" ("+plan.Preserve.Count+")\r\n"+String.Join("\r\n",plan.Preserve.Select(file=>file.Path+" — "+Reason(file.Reason)));
        var body=new TextBox{Name="DeleteFileReview",Multiline=true,ReadOnly=true,BorderStyle=BorderStyle.None,Dock=DockStyle.Fill,Text=text,Font=GeneralStyle.Font(11)};
        var surface=new GeneralSurface{Dock=DockStyle.Fill,Padding=new Padding(14),Margin=Padding.Empty};surface.Controls.Add(new GeneralScrollView(body));layout.Controls.Add(surface,0,1);
        var buttons=GeneralTableDialogLayout.Commands();var erase=(GeneralButton)GeneralStyle.Button(GeneralStyle.T("Excluir definitivamente","Delete permanently"),()=>Complete(DialogResult.OK),true);erase.Name="ConfirmProjectDelete";erase.Destructive=true;
        var cancel=GeneralStyle.Button(GeneralStyle.T("Cancelar","Cancel"),()=>Complete(DialogResult.Cancel));cancel.Name="CancelProjectDelete";buttons.Controls.Add(erase);buttons.Controls.Add(cancel);layout.Controls.Add(buttons,0,2);Content.Controls.Add(layout);
        AcceptButton=cancel;CancelButton=cancel;GeneralStyle.Apply(this,GeneralStyle.Dark);body.BorderStyle=BorderStyle.None;
    }
    static string Reason(string text){
        switch(text){
            case "Usado por outro projeto":return GeneralStyle.T(text,"Used by another project or mod");
            case "Base ou arquivo protegido":return GeneralStyle.T(text,"Base or protected file");
            case "Arquivo preexistente ou configuração preservada":return GeneralStyle.T(text,"Preexisting file or preserved settings");
            case "Registros divergentes":return GeneralStyle.T(text,"Conflicting records");
            case "Arquivo alterado desde a instalação":return GeneralStyle.T(text,"File edited since installation");
            default:return text;
        }
    }
}
