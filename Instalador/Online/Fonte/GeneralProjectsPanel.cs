using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class GeneralProjectsPanel : GeneralDialog {
    internal string ActionId {get;private set;}
    internal GeneralProjectsPanel(GeneralProjectStore store,FixedCatalog catalog,DownloadCache cache,string current=null):base(GeneralStyle.T("Projetos","Projects")){
        ClientSize=new Size(580,490);ShowCloseButton(false);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new Padding(18,6,18,18)};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));foreach(int height in new[]{54,48,28})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,32));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));Content.Controls.Add(layout);
        var actions=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};actions.RowStyles.Add(new RowStyle(SizeType.Absolute,48));actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        Add(actions,"new","Criar projeto","Create project","new",0);Add(actions,"import","Importar projeto","Import project","import",1);layout.Controls.Add(actions,0,0);
        var metrics=new GeneralStorageLabel(cache){Name="ProjectsStorage",AutoEllipsis=true,Text=GeneralStyle.T("Calculando espaço…","Calculating size…"),Dock=DockStyle.Fill,Font=GeneralStyle.Font(10),ForeColor=GeneralStyle.Muted,Margin=Padding.Empty};layout.Controls.Add(metrics,0,1);
        layout.Controls.Add(new Label{Text=GeneralStyle.T("Projetos salvos","Saved projects"),Font=GeneralStyle.Font(12,true),Dock=DockStyle.Fill,Margin=Padding.Empty},0,2);
        var field=new GeneralSurface{Dock=DockStyle.Fill,Padding=new Padding(8),Margin=Padding.Empty};layout.Controls.Add(field,0,3);
        var projects=store==null?new GeneralProject[0]:store.Read().Projects.Where(item=>item.TemplateId==null).Reverse().ToArray();
        GeneralScrollView projectScroll=null;
        if(projects.Length==0)field.Controls.Add(new Label{Text=GeneralStyle.T("Seus projetos aparecerão aqui.","Your projects will appear here."),Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter});
        else{
            var list=new ListBox{Name="ProjectChoices",BorderStyle=BorderStyle.None,DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=44,IntegralHeight=false};list.Items.AddRange(projects.Select(item=>item.Name).Cast<object>().ToArray());
            list.SelectedIndex=Array.FindIndex(projects,item=>item.Id==current);
            list.DrawItem+=(s,e)=>{if(e.Index<0)return;GeneralStyle.PaintChoice(e.Graphics,e.Bounds,(e.State&DrawItemState.Selected)!=0);TextRenderer.DrawText(e.Graphics,projects[e.Index].Name,e.Font,new Rectangle(e.Bounds.X+12,e.Bounds.Y,e.Bounds.Width-24,e.Bounds.Height),GeneralStyle.Foreground,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);};
            list.MouseMove+=(s,e)=>{int at=list.IndexFromPoint(e.Location);if(at>=0)list.SelectedIndex=at;};
            Action choose=()=>{if(list.SelectedIndex>=0){ActionId="project/"+projects[list.SelectedIndex].Id;Complete(DialogResult.OK);}};
            list.MouseClick+=(s,e)=>{if(e.Button==MouseButtons.Left&&list.IndexFromPoint(e.Location)>=0)choose();};list.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;choose();}};field.Controls.Add(projectScroll=new GeneralScrollView(list));
        }
        layout.Controls.Add(new Label{Text=GeneralStyle.T("Projetos prontos","Ready-made projects"),Dock=DockStyle.Fill,Font=GeneralStyle.Font(12,true),Margin=new Padding(0,10,0,0)},0,4);
        var ready=GeneralStyle.Button("Cyberpunk 2077",()=>{ActionId="ready";Complete(DialogResult.OK);});ready.Name="ReadyProject";ready.Dock=DockStyle.Fill;ready.Margin=Padding.Empty;layout.Controls.Add(ready,0,5);
        GeneralStyle.Apply(this,GeneralStyle.Dark);
        Shown+=async(s,e)=>{if(projectScroll!=null)projectScroll.RefreshScroll();if(store==null){metrics.Text="";return;}try{var size=await Task.Run(()=>GeneralProjectStorage.Measure(store,catalog,cache));if(!IsDisposed)metrics.SetSize(size,true);}catch(Exception){if(!IsDisposed)metrics.SetSize(null,true);}};
    }
    void Add(TableLayoutPanel panel,string action,string pt,string en,string icon,int column){var button=(GeneralButton)GeneralStyle.Button(GeneralStyle.T(pt,en),()=>{ActionId=action;Complete(DialogResult.OK);},column==0);button.Name="Project"+action;button.IconKind=icon;button.AutoSize=false;button.MinimumSize=new Size(0,44);button.MaximumSize=new Size(0,44);button.Dock=DockStyle.Fill;button.Margin=column==0?new Padding(0,0,6,6):new Padding(6,0,0,6);panel.Controls.Add(button,column,0);}
}
