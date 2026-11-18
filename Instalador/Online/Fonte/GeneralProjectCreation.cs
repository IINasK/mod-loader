using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

internal static class ProjectCoverImage {
    internal static Bitmap Read(string path){
        if(String.IsNullOrWhiteSpace(path)||!File.Exists(path))throw new IOException(GeneralStyle.T("Escolha uma imagem existente.","Choose an existing image."));
        Safe.NoLinks(path);
        try{
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){
                if(stream.Length>20*1024*1024)throw new IOException(GeneralStyle.T("Escolha uma imagem de até 20 MB.","Choose an image up to 20 MB."));
                using(var source=Image.FromStream(stream,false,true)){
                    if((long)source.Width*source.Height>24000000)throw new IOException(GeneralStyle.T("A imagem é muito grande. Use uma capa com até 24 megapixels.","The image is too large. Use a cover up to 24 megapixels."));
                    double scale=Math.Min(1,Math.Min(600.0/source.Width,900.0/source.Height));
                    var image=new Bitmap(Math.Max(1,(int)(source.Width*scale)),Math.Max(1,(int)(source.Height*scale)),PixelFormat.Format32bppArgb);
                    try{
                        using(var graphics=Graphics.FromImage(image)){
                            graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;
                            graphics.DrawImage(source,new Rectangle(0,0,image.Width,image.Height));
                        }
                        return image;
                    }catch{image.Dispose();throw;}
                }
            }
        }catch(ArgumentException ex){throw new IOException(GeneralStyle.T("Não foi possível ler esta imagem.","This image could not be read."),ex);}
         catch(OutOfMemoryException ex){throw new IOException(GeneralStyle.T("Não foi possível ler esta imagem.","This image could not be read."),ex);}
    }
    internal static Bitmap ReadLocal(string path){
        if(path==null||!File.Exists(path))return null;
        try{return Read(path);}catch(IOException){return null;}catch(UnauthorizedAccessException){return null;}
    }
    internal static void Save(Image image,string projectData){
        if(image==null)return;
        Safe.NoLinks(projectData);
        Directory.CreateDirectory(projectData);
        string target=Safe.In(projectData,"project-cover.png");
        Safe.NoLinks(target);
        string temporary=target+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{
            using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
                image.Save(stream,ImageFormat.Png);stream.Flush(true);
            }
            if(File.Exists(target))File.Replace(temporary,target,null);else File.Move(temporary,target);
        }finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}

internal sealed class GeneralProjectCreationDialog : GeneralDialog {
    readonly TextBox projectName=new TextBox{Name="ProjectNameInput",MaxLength=120};
    readonly TextBox destination=new TextBox{Name="ProjectDestinationInput"};
    readonly ProjectCoverPicker cover=new ProjectCoverPicker{Name="ProjectCoverPicker"};
    readonly Label error=new Label{Name="ProjectCreationError",AutoSize=true,Dock=DockStyle.Fill};
    readonly Label imported=new Label{Name="ImportedProjectNotice",AutoSize=true,Dock=DockStyle.Fill};
    readonly GeneralButton save;
    readonly bool editing;
    bool coverChanged;
    GeneralModList importedList;
    internal string ResultName,ResultDestination;
    internal GeneralModList ResultList;
    internal bool HasCover {get{return cover.HasImage;}}
    internal GeneralProjectCreationDialog(string name="",GeneralModList list=null,string folderPath=null,string coverPath=null,bool editing=false):base(GeneralStyle.T(editing?"Editar projeto":"Novo projeto",editing?"Edit project":"New project")){
        this.editing=editing;
        ClientSize=new Size(980,560);MinimumSize=new Size(840,510);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Padding=new Padding(24,8,24,24)};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,31));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,69));
        cover.Dock=DockStyle.Fill;cover.Margin=new Padding(0,0,28,0);
        cover.Failed=message=>{error.Text=message;};
        cover.Changed=()=>{error.Text="";coverChanged=true;};
        layout.Controls.Add(cover,0,0);
        var right=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=7,Margin=Padding.Empty};
        right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        right.RowStyles.Add(new RowStyle(SizeType.Percent,40));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.Percent,60));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var nameRow=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=1,RowCount=2,Margin=Padding.Empty};
        nameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        nameRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));nameRow.RowStyles.Add(new RowStyle(SizeType.Absolute,44));
        nameRow.Controls.Add(new Label{Text=GeneralStyle.T("Nome do projeto *","Project name *"),AutoSize=true,Margin=new Padding(0,0,0,6)},0,0);
        projectName.AccessibleName=GeneralStyle.T("Nome do projeto","Project name");projectName.Text=name;
        nameRow.Controls.Add(GeneralTableDialogLayout.Input(projectName),0,1);
        right.Controls.Add(nameRow,0,1);
        destination.AccessibleName=GeneralStyle.T("Pasta de instalação","Installation folder");destination.Text=folderPath??"";
        var folder=GeneralOptionsDialog.FolderRow(destination.AccessibleName+(editing?"":" *"),destination,()=>{
            try{var path=WindowsFolderPicker.Choose(this,destination.AccessibleName,destination.Text);if(path!=null)destination.Text=path;}
            catch(Exception ex){error.Text=ex.Message;}
        });
        folder.Name="ProjectDestinationRow";right.Controls.Add(folder,0,2);
        if(editing){destination.ReadOnly=true;foreach(var button in folder.Controls.OfType<Button>())button.Enabled=false;}
        imported.ForeColor=GeneralStyle.Muted;imported.Margin=new Padding(0,12,0,0);right.Controls.Add(imported,0,3);
        error.ForeColor=GeneralStyle.Danger;error.Margin=new Padding(0,12,0,0);right.Controls.Add(error,0,4);
        var actions=GeneralTableDialogLayout.Commands();
        save=(GeneralButton)GeneralStyle.Button(GeneralStyle.T(editing?"Salvar":"Salvar e continuar",editing?"Save":"Save and continue"),Save,true);
        save.Name="SaveNewProject";
        var import=GeneralStyle.Button(GeneralStyle.T("Importar","Import"),Import);
        import.Name="ImportNewProject";import.Visible=!editing;
        actions.Controls.Add(save);actions.Controls.Add(import);right.Controls.Add(actions,0,6);
        layout.Controls.Add(right,1,0);Content.Controls.Add(layout);AcceptButton=save;
        projectName.TextChanged+=(s,e)=>RefreshSave();destination.TextChanged+=(s,e)=>RefreshSave();
        ApplyImport(list,name);if(editing)imported.Visible=false;
        if(coverPath!=null)cover.SelectImage(coverPath);coverChanged=false;
        GeneralStyle.Apply(this,GeneralStyle.Dark);
        projectName.BorderStyle=destination.BorderStyle=BorderStyle.None;RefreshSave();
    }
    void RefreshSave(){save.Enabled=!String.IsNullOrWhiteSpace(projectName.Text)&&(editing||!String.IsNullOrWhiteSpace(destination.Text));}
    internal void SetCover(string path){cover.SelectImage(path);}
    internal void ShowCoverPreview(Image image){cover.SetPreview(image);}
    internal void SaveCover(string data){if(!editing||coverChanged)cover.SaveTo(data);}
    internal void ApplyImport(GeneralModList list,string name){
        importedList=list;
        if(list==null)return;
        if(String.IsNullOrWhiteSpace(projectName.Text))projectName.Text=name;
        imported.Text=GeneralStyle.T("Projeto importado","Imported project")+" · "+list.Mods.Count+" mods";
    }
    void Import(){
        using(var open=new OpenFileDialog{Title=GeneralStyle.T("Importar projeto","Import project"),Filter="Mod Loader|*.json",CheckFileExists=true}){
            if(open.ShowDialog(this)!=DialogResult.OK)return;
            try{
                ImportDefinition(open.FileName);
            }catch(Exception ex){error.Text=ex.Message;}
        }
    }
    internal void ImportDefinition(string file){
        var list=new GeneralModLibrary(file).Import(file);
        ApplyImport(list,Path.GetFileNameWithoutExtension(file));error.Text="";
    }
    void Save(){
        try{
            string name=projectName.Text.Trim();
            if(name.Length==0||name.Length>120||name.Any(Char.IsControl))throw new IOException(GeneralStyle.T("Informe um nome válido para o projeto.","Enter a valid project name."));
            string path=editing?destination.Text:WindowsFolderPicker.InstallationPath(destination.Text);
            if(path.Length==0&&!editing)throw new IOException(GeneralStyle.T("Selecione a pasta de instalação.","Select the installation folder."));
            ResultName=name;ResultDestination=path;
            if(!editing){
                ResultList=importedList??new GeneralModList();
                if(ResultList.Presets.Count==0)ResultList.Presets.Add(new GeneralModPreset{Id="main",Name=name,ModIds=ResultList.Mods.Select(mod=>mod.Id).ToList()});
            }
            Complete(DialogResult.OK);
        }catch(Exception ex){error.Text=ex.Message;}
    }
    protected override bool ProcessCmdKey(ref Message message,Keys keys){
        if(keys==Keys.Escape){Complete(DialogResult.Cancel);return true;}
        return base.ProcessCmdKey(ref message,keys);
    }
}

internal sealed class ProjectCoverPicker : Button {
    Image image;
    readonly Timer animation=new Timer{Interval=16};
    bool dropTarget,hover;
    float highlight;
    float TargetHighlight {get{return Enabled&&(dropTarget||hover)?1:0;}}
    internal Action<string> Failed;
    internal Action Changed;
    internal bool HasImage {get{return image!=null;}}
    internal ProjectCoverPicker(){
        AllowDrop=true;Cursor=Cursors.Hand;AccessibleRole=AccessibleRole.PushButton;
        AccessibleName=GeneralStyle.T("Selecionar ou arrastar capa do projeto","Choose or drop project cover");
        SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
        animation.Tick+=(s,e)=>{highlight+=(TargetHighlight-highlight)*.24f;if(Math.Abs(highlight-TargetHighlight)<.015){highlight=TargetHighlight;animation.Stop();}Invalidate();};
        Click+=(s,e)=>{
            using(var open=new OpenFileDialog{Title=GeneralStyle.T("Capa do projeto","Project cover"),Filter=GeneralStyle.T("Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.gif","Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif"),CheckFileExists=true})
                if(open.ShowDialog(FindForm())==DialogResult.OK)SelectImage(open.FileName);
        };
        DragEnter+=(s,e)=>{bool valid=ImageDrop(e.Data)!=null;e.Effect=valid?DragDropEffects.Copy:DragDropEffects.None;SetDropTarget(valid);};
        DragOver+=(s,e)=>e.Effect=ImageDrop(e.Data)!=null?DragDropEffects.Copy:DragDropEffects.None;
        DragLeave+=(s,e)=>SetDropTarget(false);
        DragDrop+=(s,e)=>{var file=ImageDrop(e.Data);SetDropTarget(false);if(file!=null)SelectImage(file);};
    }
    internal static string ImageDrop(IDataObject data){
        if(data==null||!data.GetDataPresent(DataFormats.FileDrop))return null;
        var files=data.GetData(DataFormats.FileDrop) as string[];
        if(files==null||files.Length!=1)return null;
        return new[]{".png",".jpg",".jpeg",".bmp",".gif"}.Contains(Path.GetExtension(files[0]),StringComparer.OrdinalIgnoreCase)?files[0]:null;
    }
    internal void SetDropTarget(bool value){
        dropTarget=value;
        AnimateHighlight();
    }
    void AnimateHighlight(){if(IsDisposed||Disposing)return;if(GeneralStyle.AnimationsEnabled)animation.Start();else{highlight=TargetHighlight;Invalidate();}}
    protected override void OnMouseEnter(EventArgs e){hover=true;AnimateHighlight();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hover=false;AnimateHighlight();base.OnMouseLeave(e);}
    protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);if(animation!=null)AnimateHighlight();}
    internal void SelectImage(string path){
        try{var selected=ProjectCoverImage.Read(path);if(image!=null)image.Dispose();image=selected;Invalidate();if(Changed!=null)Changed();}
        catch(Exception ex){if(Failed!=null)Failed(ex.Message);}
    }
    internal void SetPreview(Image preview){if(image!=null)image.Dispose();image=preview==null?null:(Image)preview.Clone();Invalidate();}
    internal void SaveTo(string data){ProjectCoverImage.Save(image,data);}
    protected override void Dispose(bool disposing){if(disposing){animation.Dispose();if(image!=null){image.Dispose();image=null;}}base.Dispose(disposing);}
    protected override void OnPaint(PaintEventArgs e){
        var g=e.Graphics;g.Clear(GeneralStyle.Surface);g.SmoothingMode=SmoothingMode.AntiAlias;
        int inset=3*DeviceDpi/96;var rect=new Rectangle(inset,inset,Math.Max(1,Width-2*inset-1),Math.Max(1,Height-2*inset-1));
        using(var shape=GeneralStyle.Round(rect,14)){
            using(var fill=new SolidBrush(GeneralStyle.DrawerSurface))g.FillPath(fill,shape);
            if(image!=null){
                var clip=g.Save();g.SetClip(shape);
                double scale=Math.Max((double)rect.Width/image.Width,(double)rect.Height/image.Height);
                var crop=new RectangleF(0,0,(float)(rect.Width/scale),(float)(rect.Height/scale));crop.X=(image.Width-crop.Width)/2;crop.Y=(image.Height-crop.Height)/2;
                g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(image,rect,crop,GraphicsUnit.Pixel);g.Restore(clip);
            }
            if(highlight>0)using(var tint=new SolidBrush(Color.FromArgb((int)(45*highlight),GeneralStyle.Accent)))g.FillPath(tint,shape);
            using(var border=new Pen(highlight>0||Focused?GeneralStyle.Accent:GeneralStyle.Line,1.5f+highlight)){
                if(image==null)border.DashStyle=DashStyle.Dash;g.DrawPath(border,shape);
            }
        }
        if(image==null){
            int icon=48*DeviceDpi/96,center=Height/2-28*DeviceDpi/96;
            LucideIcons.Draw(g,"plus",new Rectangle((Width-icon)/2,center-icon/2,icon,icon),GeneralStyle.Accent);
            using(var font=GeneralStyle.Font(11))TextRenderer.DrawText(g,dropTarget?GeneralStyle.T("Solte para usar como capa","Drop to use as cover"):GeneralStyle.T("Arraste uma imagem para cá\nou clique para escolher","Drag an image here\nor click to choose"),font,
                new Rectangle(14,center+icon/2+12,Math.Max(1,Width-28),80*DeviceDpi/96),GeneralStyle.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.WordBreak);
        }
    }
}
