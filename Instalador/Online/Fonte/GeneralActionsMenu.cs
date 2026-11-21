using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

internal sealed class GeneralActionsMenu : GeneralDialog {
    internal string ActionId {get;private set;}

    internal GeneralActionsMenu(bool builtIn=false,bool home=false,bool saveMenu=false,bool savePresetItems=false,bool hasTemplateCopies=false,bool showTemplateCopies=false){
        Text=saveMenu?GeneralStyle.T("Salvar projeto","Save project"):GeneralStyle.T("Menu","Menu");
        ShowCloseButton(false);
        var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,Padding=new Padding(14,6,14,14),Margin=Padding.Empty};
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));Content.Controls.Add(body);
        if(saveMenu){
            Add(body,"save-project","Salvar","Save","save");
            Add(body,"save-increment","Salvar incremento","Save revision","copy");
            Add(body,"clone","Duplicar projeto","Duplicate project","copy");
        }else{
            if(!home){
                Section(body,"NavigationSection","Navegação","Navigation");
                Add(body,"new","Novo projeto","New project","new");
                Add(body,"open","Abrir projeto","Open project","open");
                Add(body,"save-menu","Salvar projeto","Save project","save");
                Section(body,"ProjectSection","Projeto","Project");
                Add(body,"details","Ver detalhes","View details","list");
                if(savePresetItems)Add(body,"save-preset-items","Atualizar itens do preset","Update preset items","save");
                if(!builtIn){
                    Add(body,"import","Importar projeto","Import project","import");
                    Add(body,"export","Exportar projeto","Export project","export");
                }
                Section(body,"ToolsSection","Ferramentas","Tools");
            }else if(hasTemplateCopies){
                Add(body,"template-copies",showTemplateCopies?"Ocultar cópias do pacote":"Mostrar cópias do pacote",
                    showTemplateCopies?"Hide pack copies":"Show pack copies","copy");
            }
            Add(body,"share","Modpack e compartilhamento","Modpack and sharing","share");
            Add(body,"options","Opções","Options","settings");
        }
        int contentHeight=(int)body.RowStyles.Cast<RowStyle>().Sum(row=>row.Height);
        ClientSize=new Size(350,HeaderHeight+GeneralStyle.RoundedFaceClearance*2+body.Padding.Vertical+contentHeight+8);
        body.RowStyles.Add(new RowStyle(SizeType.Percent,100));body.RowCount=body.RowStyles.Count;
        GeneralStyle.Apply(this,GeneralStyle.Dark);
    }
    void Section(TableLayoutPanel layout,string id,string pt,string en){
        int row=layout.RowStyles.Count;
        bool first=row==0;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,first?28:40));
        var label=new Label{Text=GeneralStyle.T(pt,en),Name=id,Dock=DockStyle.Fill,Font=GeneralStyle.Font(12,true),
            TextAlign=ContentAlignment.BottomLeft,Margin=new Padding(8,0,0,4)};
        if(!first)label.Paint+=(s,e)=>{
            using(var pen=new Pen(GeneralStyle.SoftLine))e.Graphics.DrawLine(pen,0,7,label.Width-9,7);
        };
        layout.Controls.Add(label,0,row);
    }
    void Add(TableLayoutPanel layout,string id,string pt,string en,string icon){
        int row=layout.RowStyles.Count;layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
        var button=(GeneralButton)GeneralStyle.Button(GeneralStyle.T(pt,en),()=>{ActionId=id;Complete(DialogResult.OK);});
        button.Name=id;button.IconKind=icon;button.TextAlign=ContentAlignment.MiddleLeft;button.Dock=DockStyle.Fill;
        button.Margin=new Padding(0,2,0,2);layout.Controls.Add(button,0,row);
    }
}
