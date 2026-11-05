using System;
using System.Drawing;
using System.Windows.Forms;

internal sealed class GeneralActionsMenu : GeneralDialog {
    internal string ActionId {get;private set;}
    internal GeneralActionsMenu(bool packages=false){
        Text=GeneralStyle.T(packages?"Pacotes":"Menu",packages?"Packs":"Menu");ClientSize=new Size(620,340);
        var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=4,Padding=new Padding(20,12,20,20),Margin=Padding.Empty};
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        for(int row=0;row<4;row++)body.RowStyles.Add(new RowStyle(SizeType.Percent,25));
        Content.Controls.Add(body);
        if(packages){
            var note=new Label{Text=GeneralStyle.T("Escolha um pacote. Importar um pacote troca a lista ativa e preserva os arquivos instalados.","Choose a pack. Loading a pack replaces the active list and preserves installed files."),AutoSize=false,Dock=DockStyle.Fill};body.Controls.Add(note,0,0);body.SetColumnSpan(note,2);
            Add(body,"cyberpunk","Cyberpunk 2077","Cyberpunk 2077",0,1);body.SetColumnSpan(body.GetControlFromPosition(0,1),2);
        }else{
            Add(body,"new","Nova lista","New list",0,0);Add(body,"import","Importar lista","Import list",1,0);
            Add(body,"export","Exportar lista","Export list",0,1);Add(body,"new-preset","Novo preset","New preset",1,1);
            Add(body,"edit-preset","Atualizar preset","Update preset",0,2);Add(body,"delete-preset","Excluir preset","Delete preset",1,2);
            Add(body,"options","Opções","Options",0,3);body.SetColumnSpan(body.GetControlFromPosition(0,3),2);
        }
        GeneralStyle.Apply(this,GeneralStyle.Dark);
    }
    void Add(TableLayoutPanel layout,string id,string pt,string en,int column,int row){
        var button=GeneralStyle.Button(GeneralStyle.T(pt,en),()=>{ActionId=id;Complete(DialogResult.OK);});
        button.Name=id;button.Dock=DockStyle.Fill;button.Margin=new Padding(0,0,column==0?10:0,10);layout.Controls.Add(button,column,row);
    }
}
