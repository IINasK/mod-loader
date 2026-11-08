using System;
using System.Drawing;
using System.Windows.Forms;

internal sealed class GeneralActionsMenu : GeneralDialog {
    internal string ActionId {get;private set;}
    internal GeneralActionsMenu(bool builtIn=false,bool home=false){
        Text=GeneralStyle.T("Menu","Menu");ClientSize=new Size(350,home?184:builtIn?372:468);ShowCloseButton(false);
        var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,Padding=new Padding(14,6,14,14),Margin=Padding.Empty};
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));Content.Controls.Add(body);
        if(!home){
            body.RowStyles.Add(new RowStyle(SizeType.Absolute,28));
            body.Controls.Add(new Label{Text=GeneralStyle.T("Projetos","Projects"),Name="ProjectSection",Dock=DockStyle.Fill,Font=GeneralStyle.Font(12,true),Margin=new Padding(8,0,0,0)},0,0);
            Add(body,"new","Novo projeto","New project","new");
            Add(body,"open","Abrir projeto","Open project","open");
            Add(body,"clone","Duplicar projeto","Duplicate project","copy");
            if(!builtIn){Add(body,"export","Exportar projeto","Export project","export");Add(body,"import","Importar projeto","Import project","import");}
            int row=body.RowStyles.Count;body.RowStyles.Add(new RowStyle(SizeType.Absolute,18));
            var divider=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty,Name="MenuDivider"};
            divider.Paint+=(s,e)=>{using(var pen=new Pen(GeneralStyle.SoftLine))e.Graphics.DrawLine(pen,8,divider.Height/2,divider.Width-9,divider.Height/2);};body.Controls.Add(divider,0,row);
        }
        Add(body,"share","Modpack e compartilhamento","Modpack and sharing","share");
        Add(body,"options","Opções","Options","settings");
        body.RowStyles.Add(new RowStyle(SizeType.Percent,100));body.RowCount=body.RowStyles.Count;
        GeneralStyle.Apply(this,GeneralStyle.Dark);
    }
    void Add(TableLayoutPanel layout,string id,string pt,string en,string icon){
        int row=layout.RowStyles.Count;layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
        var button=(GeneralButton)GeneralStyle.Button(GeneralStyle.T(pt,en),()=>{ActionId=id;Complete(DialogResult.OK);});
        button.Name=id;button.IconKind=icon;button.TextAlign=ContentAlignment.MiddleLeft;button.Dock=DockStyle.Fill;button.Margin=new Padding(0,2,0,2);layout.Controls.Add(button,0,row);
    }
}
