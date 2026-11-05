using System;
using System.Drawing;
using System.Windows.Forms;

internal sealed class GeneralActionsMenu : GeneralDialog {
    internal string ActionId {get;private set;}
    internal GeneralActionsMenu(bool packages=false){
        Text=GeneralStyle.T(packages?"Pacotes":"Menu",packages?"Packs":"Menu");ClientSize=new Size(620,packages?340:230);
        var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=4,Padding=new Padding(20,12,20,20),Margin=Padding.Empty};
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        if(packages)for(int row=0;row<4;row++)body.RowStyles.Add(new RowStyle(SizeType.Percent,25));
        else{
            body.Padding=new Padding(20,8,20,20);
            foreach(int height in new[]{28,54,10,54})body.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
        }
        Content.Controls.Add(body);
        if(packages){
            var note=new Label{Text=GeneralStyle.T("Escolha um pacote. Importar um pacote troca a lista ativa e preserva os arquivos instalados.","Choose a pack. Loading a pack replaces the active list and preserves installed files."),AutoSize=false,Dock=DockStyle.Fill};body.Controls.Add(note,0,0);body.SetColumnSpan(note,2);
            Add(body,"cyberpunk","Cyberpunk 2077","Cyberpunk 2077",0,1);body.SetColumnSpan(body.GetControlFromPosition(0,1),2);
        }else{
            var section=new Label{Text=GeneralStyle.T("Projeto","Project"),Name="ProjectSection",Dock=DockStyle.Fill,Font=GeneralStyle.Font(12,true),Margin=Padding.Empty};
            body.Controls.Add(section,0,0);body.SetColumnSpan(section,2);
            Add(body,"new","Novo projeto","New project",0,1);Add(body,"import","Abrir projeto","Open project",1,1);
            Add(body,"export","Exportar projeto","Export project",0,3);Add(body,"delete-project","Excluir projeto","Delete project",1,3);
            foreach(Control control in body.Controls){
                var button=control as GeneralButton;if(button==null)continue;
                button.Margin=body.GetColumn(button)==0?new Padding(0,0,5,0):new Padding(5,0,0,0);
                button.Destructive=button.Name=="delete-project";
            }
        }
        GeneralStyle.Apply(this,GeneralStyle.Dark);
    }
    void Add(TableLayoutPanel layout,string id,string pt,string en,int column,int row){
        var button=GeneralStyle.Button(GeneralStyle.T(pt,en),()=>{ActionId=id;Complete(DialogResult.OK);});
        button.Name=id;button.Dock=DockStyle.Fill;button.Margin=new Padding(0,0,column==0?10:0,10);layout.Controls.Add(button,column,row);
    }
}
