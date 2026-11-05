using System;
using System.Drawing;
using System.Windows.Forms;

internal sealed class GeneralActionsMenu : GeneralDialog {
    internal string ActionId {get;private set;}
    internal GeneralActionsMenu(){
        Text=GeneralStyle.T("Menu","Menu");ClientSize=new Size(350,420);ShowCloseButton(false);
        var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=9,Padding=new Padding(14,6,14,14),Margin=Padding.Empty};
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        foreach(int height in new[]{28,48,48,48,48,18,48,48})body.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
        body.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        Content.Controls.Add(body);
        body.Controls.Add(new Label{Text=GeneralStyle.T("Listas","Lists"),Name="ListSection",Dock=DockStyle.Fill,Font=GeneralStyle.Font(12,true),Margin=new Padding(8,0,0,0)},0,0);
        Add(body,"new","Nova lista","New list","new",1);
        Add(body,"open","Abrir lista","Open list","open",2);
        Add(body,"export","Exportar lista","Export list","export",3);
        Add(body,"import","Importar lista","Import list","import",4);
        var divider=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty,Name="MenuDivider"};
        divider.Paint+=(s,e)=>{using(var pen=new Pen(GeneralStyle.SoftLine))e.Graphics.DrawLine(pen,8,divider.Height/2,divider.Width-9,divider.Height/2);};body.Controls.Add(divider,0,5);
        Add(body,"share","Compartilhar","Share","share",6);
        Add(body,"options","Opções","Options","settings",7);
        GeneralStyle.Apply(this,GeneralStyle.Dark);
    }
    void Add(TableLayoutPanel layout,string id,string pt,string en,string icon,int row){
        var button=(GeneralButton)GeneralStyle.Button(GeneralStyle.T(pt,en),()=>{ActionId=id;Complete(DialogResult.OK);});
        button.Name=id;button.IconKind=icon;button.TextAlign=ContentAlignment.MiddleLeft;button.Dock=DockStyle.Fill;button.Margin=new Padding(0,0,0,4);layout.Controls.Add(button,0,row);
    }
}
