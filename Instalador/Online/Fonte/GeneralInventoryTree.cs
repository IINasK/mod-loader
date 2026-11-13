using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

internal sealed class GeneralInventoryTree : GeneralDialog {
    internal sealed class Entry {
        internal string Path,Name;
        internal Entry Parent;
        internal P2PSyncDifference File;
        internal bool ContainsRecord,ContainsProof,Open;
        internal readonly SortedDictionary<string,Entry> Children=new SortedDictionary<string,Entry>(StringComparer.OrdinalIgnoreCase);
        internal bool Folder {get{return File==null;}}
        internal int Depth {get{return Parent==null?0:Parent.Depth+1;}}
        public override string ToString(){return Name;}
    }

    readonly string root;
    readonly Entry tree;
    readonly ListBox list=new ListBox {
        Name="InventoryFileTree",Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,DrawMode=DrawMode.OwnerDrawFixed,
        ItemHeight=29,Font=GeneralStyle.Font(11)
    };
    readonly TextBox path=new TextBox{ReadOnly=true,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,Font=GeneralStyle.Font(11)};
    readonly Label origin=new Label{Dock=DockStyle.Fill,AutoEllipsis=true,TextAlign=ContentAlignment.MiddleLeft};
    readonly GeneralScrollView scroll;
    readonly Button reveal;
    Entry Selected {get{return list.SelectedItem as Entry;}}
    protected override bool AnimateAppearance {get{return false;}}

    internal GeneralInventoryTree(string root,IEnumerable<P2PSyncDifference> files)
        :base(T("Arquivos de mods no jogo","Mod files in the game")){
        this.root=root;
        tree=BuildTree(files,T("Pasta do jogo","Game folder"));
        ClientSize=new Size(790,580);
        MinimumSize=new Size(480,350);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(20,5,20,16)};
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,32));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        scroll=new GeneralScrollView(list);
        var frame=new GeneralSurface{Dock=DockStyle.Fill,Padding=new Padding(8),Margin=new Padding(0,0,0,9)};
        frame.Controls.Add(scroll);
        layout.Controls.Add(frame,0,0);
        path.AccessibleName=T("Caminho relativo à pasta do jogo","Path relative to the game folder");
        list.AccessibleName=T("Árvore de arquivos de mods","Mod file tree");
        layout.Controls.Add(path,0,1);
        layout.Controls.Add(origin,0,2);
        var commands=GeneralTableDialogLayout.Commands();
        var close=GeneralStyle.Button(T("Fechar","Close"),()=>Complete(DialogResult.Cancel));
        reveal=GeneralStyle.Button(T("Abrir no Explorador","Open in Explorer"),Reveal,true);
        commands.Controls.Add(close);
        commands.Controls.Add(reveal);
        layout.Controls.Add(commands,0,3);
        Content.Controls.Add(layout);
        CancelButton=close;

        list.DrawItem+=DrawEntry;
        list.SelectedIndexChanged+=(s,e)=>ShowSelection();
        list.MouseDown+=(s,e)=>{
            int index=list.IndexFromPoint(e.Location);
            if(e.Button!=MouseButtons.Left||index<0)return;
            var entry=(Entry)list.Items[index];
            if(entry.Folder&&e.X>=Indent(entry)&&e.X<Indent(entry)+20)Toggle(entry);
        };
        list.MouseDoubleClick+=(s,e)=>{
            int index=list.IndexFromPoint(e.Location);
            if(e.Button!=MouseButtons.Left||index<0)return;
            var entry=(Entry)list.Items[index];
            if(entry.Folder){
                if(e.X>=Indent(entry)+20)Toggle(entry);
            }else Reveal();
        };
        list.KeyDown+=(s,e)=>{
            var entry=Selected;
            if(entry==null)return;
            if(e.KeyCode==Keys.Right&&entry.Folder){
                if(!entry.Open)Toggle(entry);
                else if(entry.Children.Count>0)list.SelectedIndex++;
            }else if(e.KeyCode==Keys.Left){
                if(entry.Folder&&entry.Open)Toggle(entry);
                else if(entry.Parent!=null)list.SelectedItem=entry.Parent;
            }else if(e.KeyCode==Keys.Enter){
                if(entry.Folder)Toggle(entry);else Reveal();
            }else return;
            e.Handled=true;e.SuppressKeyPress=true;
        };
        RefreshTree(tree);
        GeneralStyle.Apply(this,GeneralStyle.Dark);
    }

    internal static Entry BuildTree(IEnumerable<P2PSyncDifference> files,string rootName){
        var root=new Entry{Name=rootName,Path="",Open=true};
        var groups=files.Where(file=>!String.IsNullOrWhiteSpace(file.Path)&&!GeneralInstallRoutes.External(file.Path))
            .GroupBy(file=>file.Path,StringComparer.OrdinalIgnoreCase);
        foreach(var group in groups){
            var file=group.FirstOrDefault(item=>item.RecordedFile)??group.FirstOrDefault(Proof)??group.First();
            var parent=root;
            foreach(string part in file.Path.Split('/')){
                Entry child;
                if(!parent.Children.TryGetValue(part,out child)){
                    child=new Entry{Name=part,Parent=parent,Path=parent.Path.Length==0?part:parent.Path+"/"+part};
                    parent.Children.Add(part,child);
                }
                parent=child;
                parent.ContainsRecord|=file.RecordedFile;
                parent.ContainsProof|=Proof(file);
            }
            parent.File=file;
        }
        return root;
    }

    static bool Proof(P2PSyncDifference file){return file.ProvenOwner!=null||file.VerifiedZip!=null;}

    void RefreshTree(Entry selected){
        int top=list.Items.Count==0?0:list.TopIndex;
        list.BeginUpdate();
        try{
            list.Items.Clear();
            AddVisible(tree);
            list.SelectedItem=selected;
            list.TopIndex=Math.Min(top,list.SelectedIndex);
        }finally{list.EndUpdate();}
        scroll.RefreshScroll();
        ShowSelection();
    }

    void AddVisible(Entry entry){
        list.Items.Add(entry);
        if(!entry.Open)return;
        foreach(var child in entry.Children.Values.OrderByDescending(item=>item.Folder))AddVisible(child);
    }

    void Toggle(Entry entry){
        if(entry.Children.Count==0)return;
        entry.Open=!entry.Open;
        RefreshTree(entry);
    }

    int Indent(Entry entry){return 6+Math.Min(entry.Depth*19,Math.Max(0,list.ClientSize.Width-155));}

    void DrawEntry(object sender,DrawItemEventArgs args){
        if(args.Index<0)return;
        var entry=(Entry)list.Items[args.Index];
        var row=args.Bounds;
        GeneralStyle.PaintChoice(args.Graphics,row,(args.State&DrawItemState.Selected)!=0);
        int x=Indent(entry),y=row.Top+(row.Height-17)/2;
        if(entry.Children.Count>0)LucideIcons.Draw(args.Graphics,entry.Open?"down":"right",new Rectangle(x,y,16,16),GeneralStyle.Muted);
        if(entry.Folder)LucideIcons.Draw(args.Graphics,"folder",new Rectangle(x+22,y,17,17),GeneralStyle.Foreground);
        var mark=new Rectangle(x+46,y,17,17);
        var flags=TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix|TextFormatFlags.EndEllipsis;
        if(entry.ContainsRecord)ModLoaderBrand.DrawIcon(args.Graphics,mark,true);
        else TextRenderer.DrawText(args.Graphics,entry.ContainsProof?"✓":"?",list.Font,mark,GeneralStyle.Muted,flags);
        if(entry.File!=null&&entry.File.RecordedChange)
            TextRenderer.DrawText(args.Graphics,"!",list.Font,new Rectangle(x+60,y,12,17),GeneralStyle.Muted,flags);
        TextRenderer.DrawText(args.Graphics,entry.Name,list.Font,
            new Rectangle(x+70,row.Top,Math.Max(0,row.Right-x-78),row.Height),GeneralStyle.Foreground,flags);
    }

    void ShowSelection(){
        var entry=Selected;
        reveal.Enabled=entry!=null;
        path.Text=entry==null||entry.Path.Length==0?root:entry.Path.Replace('/','\\');
        var file=entry==null?null:entry.File;
        if(entry==null)origin.Text="";
        else if(entry.Folder)origin.Text=entry.ContainsRecord?T("Contém arquivos registrados pelo Mod Loader","Contains files recorded by Mod Loader"):
            entry.ContainsProof?T("Contém arquivos com origem confirmada","Contains files with verified origin"):T("Origem desconhecida","Unknown origin");
        else if(file.RecordedFile)origin.Text=(file.RecordedChange?T("Alterado desde o registro","Changed since recording"):
            T("Registrado pelo Mod Loader","Recorded by Mod Loader"))+" · "+file.Mod;
        else if(Proof(file))origin.Text=T("Origem confirmada","Verified origin")+" · "+file.Mod;
        else if(file.LocalInModFolder)origin.Text=T("Arquivo local · origem desconhecida","Local file · unknown origin");
        else origin.Text=T("Origem desconhecida","Unknown origin");
    }

    void Reveal(){
        var entry=Selected;
        if(entry==null)return;
        try{
            string target=entry.Path.Length==0?root:Safe.In(root,entry.Path);
            Safe.NoLinks(target);
            if(Directory.Exists(target))Process.Start(new ProcessStartInfo(target){UseShellExecute=true});
            else if(File.Exists(target))Process.Start("explorer.exe","/select,\""+target+"\"");
            else throw new IOException(T("O arquivo ou a pasta não existe mais.","The file or folder no longer exists."));
        }catch(IOException ex){ShowOpenError(ex);}
        catch(UnauthorizedAccessException ex){ShowOpenError(ex);}
        catch(System.ComponentModel.Win32Exception ex){ShowOpenError(ex);}
    }

    void ShowOpenError(Exception ex){
        Message(this,ex.Message,T("Não foi possível abrir o local","Could not open location"),MessageBoxButtons.OK,MessageBoxIcon.Warning);
    }

    static string T(string pt,string en){return GeneralStyle.T(pt,en);}
}
