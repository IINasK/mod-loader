using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

internal sealed class GeneralProjectsMenu : GeneralDialog {
    readonly GeneralProject[] projects;
    readonly ListBox saved;
    internal string ProjectId {get;private set;}
    internal bool ReadyProject {get;private set;}
    internal GeneralProjectsMenu(IEnumerable<GeneralProject> projects,string selectedId){
        this.projects=(projects??Enumerable.Empty<GeneralProject>()).Where(project=>String.IsNullOrEmpty(project.TemplateId)).ToArray();
        Text=GeneralStyle.T("Projetos","Projects");ShowCloseButton(false);
        int listHeight=Math.Min(184,this.projects.Length*46);
        ClientSize=new Size(350,this.projects.Length==0?164:196+listHeight);
        var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=this.projects.Length==0?3:5,Padding=new Padding(14,6,14,14),Margin=Padding.Empty};
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));Content.Controls.Add(body);
        int row=0;
        if(this.projects.Length>0){
            body.RowStyles.Add(new RowStyle(SizeType.Absolute,28));body.Controls.Add(Heading(GeneralStyle.T("Seus projetos","Your projects"),"SavedProjectSection"),0,row++);
            body.RowStyles.Add(new RowStyle(SizeType.Absolute,listHeight));
            saved=new ListBox{Name="SavedProjects",BorderStyle=BorderStyle.None,IntegralHeight=false,DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=46,Dock=DockStyle.Fill,AccessibleName=GeneralStyle.T("Seus projetos","Your projects")};
            foreach(var project in this.projects)saved.Items.Add(project.Name);
            saved.DrawItem+=(s,e)=>{
                if(e.Index<0)return;bool hover=(e.State&DrawItemState.Selected)!=0;
                using(var brush=new SolidBrush(hover?GeneralStyle.Selection:GeneralStyle.Surface))e.Graphics.FillRectangle(brush,e.Bounds);
                LucideIcons.Draw(e.Graphics,"open",new Rectangle(e.Bounds.X+10,e.Bounds.Y+13,20,20),GeneralStyle.Foreground);
                TextRenderer.DrawText(e.Graphics,this.projects[e.Index].Name,saved.Font,new Rectangle(e.Bounds.X+42,e.Bounds.Y,e.Bounds.Width-76,e.Bounds.Height),GeneralStyle.Foreground,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
                if(this.projects[e.Index].Id==selectedId)LucideIcons.Draw(e.Graphics,"check",new Rectangle(e.Bounds.Right-28,e.Bounds.Y+14,18,18),GeneralStyle.Accent);
            };
            saved.MouseClick+=(s,e)=>{int index=saved.IndexFromPoint(e.Location);if(e.Button==MouseButtons.Left)Choose(index);};
            saved.MouseMove+=(s,e)=>{int index=saved.IndexFromPoint(e.Location);if(index>=0&&saved.SelectedIndex!=index)saved.SelectedIndex=index;};
            saved.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter){e.Handled=true;Choose(saved.SelectedIndex);}};
            var scroll=new GeneralScrollView(saved){Margin=Padding.Empty};body.Controls.Add(scroll,0,row++);
        }
        body.RowStyles.Add(new RowStyle(SizeType.Absolute,this.projects.Length==0?28:42));
        var builtIn=Heading(GeneralStyle.T("Projetos padrão","Built-in projects"),"BuiltInProjectSection");if(this.projects.Length>0)builtIn.Padding=new Padding(0,14,0,0);body.Controls.Add(builtIn,0,row++);
        body.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
        var ready=(GeneralButton)GeneralStyle.Button("Cyberpunk 2077",()=>{ReadyProject=true;Complete(DialogResult.OK);});
        ready.Name="cyberpunk";ready.IconKind="package";ready.TextAlign=ContentAlignment.MiddleLeft;ready.Dock=DockStyle.Fill;ready.Margin=new Padding(0,0,0,4);body.Controls.Add(ready,0,row);
        body.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        GeneralStyle.Apply(this,GeneralStyle.Dark);
    }
    static Label Heading(string text,string name){return new Label{Name=name,Text=text,Dock=DockStyle.Fill,Font=GeneralStyle.Font(12,true),Margin=new Padding(8,0,0,0)};}
    void Choose(int index){if(index<0||index>=projects.Length)return;ProjectId=projects[index].Id;Complete(DialogResult.OK);}
}
