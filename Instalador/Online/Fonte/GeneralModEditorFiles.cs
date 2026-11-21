using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

internal sealed partial class GeneralModEditor {
    IEnumerable<string> IGeneralModMessageContext.ModMessageNames {get{return new[]{modName.Text,groupName.Text,initialEntry.Name};}}
    readonly GeneralPackageDrop packageDrop=new GeneralPackageDrop();
    readonly List<Control> packageDetails=new List<Control>();
    internal Func<string,bool> TestPackageReplacement;

    void InitializePackageDrop(TableLayoutPanel layout) {
        AddEditorRow(layout,packageDrop);
        packageDrop.Selected=path=>AttachPackageSource(path);
        packageDrop.Failed=message=>{validation.Text=message;validation.Visible=true;};
        sources.AllowDrop=true;
        sources.DragEnter+=(s,e)=>e.Effect=!sources.ReadOnly&&GeneralPackageDrop.PackagePath(e.Data)!=null?DragDropEffects.Copy:DragDropEffects.None;
        sources.DragDrop+=(s,e)=>{
            var path=GeneralPackageDrop.PackagePath(e.Data);
            if(path!=null&&!sources.ReadOnly)AttachPackageSource(path);
        };
        sources.TextChanged+=(s,e)=>RefreshPackageDetails();
        RefreshPackageDetails();
    }

    bool HasPackageSource {
        get {
            string source=NormalizeEditorSource(sources.Text.Trim());
            return initialEntry.DetectedPaths.Count>0||GeneralModDownload.IsArchivePath(source)||
                GeneralModFolderSource.IsSource(source)||GeneralModDownload.IsWebSource(source);
        }
    }
    static string NormalizeEditorSource(string source) {
        try {if(GeneralModDownload.IsLocalPath(source)&&Directory.Exists(source))return GeneralModFolderSource.Normalize(source);}
        catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}catch(NotSupportedException){}
        return source;
    }
    void RefreshPackageDetails() {
        bool show=HasPackageSource&&!attachment;
        packageDrop.Visible=!HasPackageSource&&!attachment;
        packageDrop.Enabled=!finishing&&current!=null&&String.IsNullOrEmpty(current.CyberpunkId);
        foreach(var control in packageDetails)control.Visible=show;
    }

    internal bool AttachPackageSource(string path) {
        if(current==null||attachment||!String.IsNullOrEmpty(current.CyberpunkId))return false;
        try {
            path=Path.GetFullPath(path);Safe.NoLinks(path);
            bool folder=Directory.Exists(path);
            string source=folder?GeneralModFolderSource.Normalize(path):path;
            if(!folder&&(!GeneralModDownload.IsArchivePath(source)||!File.Exists(source)))
                throw new IOException(T("Escolha um ZIP, RAR, 7z ou uma pasta com os arquivos do mod.","Choose a ZIP, RAR, 7z or a folder containing the mod files."));
            string previous=sources.Text.Trim();
            if(previous.Length>0&&!String.Equals(previous,source,StringComparison.OrdinalIgnoreCase)) {
                bool replace=TestPackageReplacement!=null?TestPackageReplacement(source):GeneralDialog.Message(this,
                    T("Substituir a fonte atual pelo pacote selecionado? Os arquivos instalados só serão alterados ao confirmar a instalação.",
                        "Replace the current source with the selected package? Installed files only change after confirming installation."),
                    T("Trocar fonte do mod","Change mod source"),MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.Yes;
                if(!replace)return false;
            }
            string name=folder?new DirectoryInfo(path).Name:Path.GetFileNameWithoutExtension(path);
            if(String.IsNullOrWhiteSpace(modName.Text)){modName.Text=name;lastAutoName=name;}
            sources.Text=source;
            RefreshPackageDetails();ValidateInputs();ScheduleArchive();
            return true;
        }catch(Exception ex) {
            if(!(ex is IOException||ex is UnauthorizedAccessException||ex is ArgumentException||ex is NotSupportedException))throw;
            validation.Text=ex.Message;validation.Visible=true;return false;
        }
    }
}

internal sealed class GeneralPackageDrop : Button {
    readonly Timer animation=new Timer{Interval=16};
    bool hover,dropTarget;
    float highlight;
    internal Action<string> Selected,Failed;
    float Target {get{return Enabled&&(hover||dropTarget||Focused)?1:0;}}

    internal GeneralPackageDrop() {
        Name="ModPackageDrop";AccessibleName=GeneralStyle.T("Adicionar arquivo ou pasta do mod","Add mod archive or folder");
        AccessibleRole=AccessibleRole.PushButton;AllowDrop=true;Cursor=Cursors.Hand;
        Dock=DockStyle.Fill;MinimumSize=new Size(200,200);Height=210;Margin=new Padding(3,18,3,10);
        SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
        animation.Tick+=(s,e)=>{
            highlight+=(Target-highlight)*.25f;
            if(Math.Abs(highlight-Target)<.015f){highlight=Target;animation.Stop();}Invalidate();
        };
        Click+=(s,e)=>ChooseArchive();
        DragEnter+=(s,e)=>{bool valid=PackagePath(e.Data)!=null;e.Effect=valid?DragDropEffects.Copy:DragDropEffects.None;SetDropTarget(valid);};
        DragOver+=(s,e)=>e.Effect=PackagePath(e.Data)!=null?DragDropEffects.Copy:DragDropEffects.None;
        DragLeave+=(s,e)=>SetDropTarget(false);
        DragDrop+=(s,e)=>{string path=PackagePath(e.Data);SetDropTarget(false);if(path!=null&&Selected!=null)Selected(path);};
        var menu=GeneralMenuRenderer.Create();
        GeneralMenuRenderer.Add(menu,GeneralStyle.T("Escolher arquivo…","Choose archive…"),ChooseArchive);
        GeneralMenuRenderer.Add(menu,GeneralStyle.T("Escolher pasta…","Choose folder…"),()=>{
            try{string folder=WindowsFolderPicker.Choose(FindForm(),GeneralStyle.T("Pasta com os arquivos do mod","Folder containing the mod files"));
                if(folder!=null&&Selected!=null)Selected(folder);}
            catch(Exception ex){if(!(ex is IOException||ex is UnauthorizedAccessException))throw;if(Failed!=null)Failed(ex.Message);}
        });
        ContextMenuStrip=menu;
        Disposed+=(s,e)=>menu.Dispose();
    }
    internal static string PackagePath(IDataObject data) {
        if(data==null||!data.GetDataPresent(DataFormats.FileDrop))return null;
        var paths=data.GetData(DataFormats.FileDrop) as string[];
        if(paths==null||paths.Length!=1)return null;
        string path=paths[0];
        if(String.IsNullOrEmpty(path)||path.Length<=3||path.StartsWith(@"\\",StringComparison.Ordinal))return null;
        return Directory.Exists(path)||GeneralModDownload.IsArchivePath(path)&&File.Exists(path)?path:null;
    }
    internal void SetDropTarget(bool value){dropTarget=value;Animate();}
    void Animate(){if(IsDisposed||Disposing)return;if(GeneralStyle.AnimationsEnabled)animation.Start();else{highlight=Target;Invalidate();}}
    void ChooseArchive() {
        using(var open=new OpenFileDialog{Title=GeneralStyle.T("Pacote do mod","Mod archive"),Filter=GeneralStyle.T("Pacotes de mods|*.zip;*.rar;*.7z","Mod archives|*.zip;*.rar;*.7z"),CheckFileExists=true})
            if(open.ShowDialog(FindForm())==DialogResult.OK&&Selected!=null)Selected(open.FileName);
    }
    protected override void OnMouseEnter(EventArgs e){hover=true;Animate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hover=false;Animate();base.OnMouseLeave(e);}
    protected override void OnGotFocus(EventArgs e){Animate();base.OnGotFocus(e);}
    protected override void OnLostFocus(EventArgs e){Animate();base.OnLostFocus(e);}
    protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);if(animation!=null)Animate();}
    protected override void OnVisibleChanged(EventArgs e){base.OnVisibleChanged(e);if(!Visible&&animation!=null){animation.Stop();hover=dropTarget=false;highlight=0;}}
    protected override void Dispose(bool disposing){if(disposing)animation.Dispose();base.Dispose(disposing);}
    protected override void OnPaint(PaintEventArgs e) {
        var g=e.Graphics;g.Clear(GeneralStyle.Surface);g.SmoothingMode=SmoothingMode.AntiAlias;
        int inset=Math.Max(3,3*DeviceDpi/96);var bounds=new Rectangle(inset,inset,Math.Max(1,Width-inset*2-1),Math.Max(1,Height-inset*2-1));
        using(var shape=GeneralStyle.Round(bounds,14)) {
            using(var fill=new SolidBrush(GeneralStyle.DrawerSurface))g.FillPath(fill,shape);
            if(highlight>0)using(var tint=new SolidBrush(Color.FromArgb((int)(42*highlight),GeneralStyle.Accent)))g.FillPath(tint,shape);
            using(var border=new Pen(Enabled&&(highlight>0||Focused)?GeneralStyle.Accent:GeneralStyle.Line,1.5f+highlight)) {
                border.DashStyle=DashStyle.Dash;g.DrawPath(border,shape);
            }
        }
        int icon=38*DeviceDpi/96,center=Height/2-24*DeviceDpi/96;
        LucideIcons.Draw(g,"plus",new Rectangle((Width-icon)/2,center-icon/2,icon,icon),Enabled?GeneralStyle.Accent:GeneralStyle.Muted);
        using(var font=GeneralStyle.Font(12))TextRenderer.DrawText(g,
            dropTarget?GeneralStyle.T("Solte para adicionar o pacote","Drop to add the package"):GeneralStyle.T("Arraste um ZIP ou uma pasta para cá","Drop a ZIP or folder here"),font,
            new Rectangle(20,center+icon/2+14,Math.Max(1,Width-40),55*DeviceDpi/96),GeneralStyle.Muted,
            TextFormatFlags.HorizontalCenter|TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
    }
}

internal sealed partial class GeneralArchiveDestinations {
    ContextMenuStrip fileMenu;
    ToolStripItem moveItem,openItem,backupItem,outputItem,restoreItem;
    Func<List<string>> readOutputFolders;
    Action<List<string>> writeOutputFolders;
    TreeNode routeDropTarget;
    readonly Timer routeDragTimer=new Timer{Interval=60};
    int routeDragScroll;
    DateTime routeDragEntered;
    internal Func<string,string,bool> TestRouteConfirmation;

    internal void ConfigureOutputs(Func<List<string>> read,Action<List<string>> write) {
        readOutputFolders=read;writeOutputFolders=write;RefreshTreeActions();
    }
    void InitializeTreeActions() {
        fileMenu=GeneralMenuRenderer.Create();
        moveItem=GeneralMenuRenderer.Add(fileMenu,T("Mudar destino…","Change destination…"),Choose);
        openItem=GeneralMenuRenderer.Add(fileMenu,T("Abrir localização","Open location"),OpenSelectedLocation);
        backupItem=GeneralMenuRenderer.Add(fileMenu,T("Abrir backup desta versão","Open this version's backup"),OpenSelectedBackup);
        restoreItem=GeneralMenuRenderer.Add(fileMenu,T("Restaurar destino herdado","Restore inherited destination"),()=>{
            if(Selected==null||IsLocalNode(Selected)||IsVirtualNode(Selected))return;drafts.Remove(SelectedPath);RefreshVisualPlacement(Selected);NotifyDraftChanged();
        });
        fileMenu.Items.Add(new ToolStripSeparator());
        outputItem=GeneralMenuRenderer.Add(fileMenu,T("Vincular arquivos gerados nesta pasta","Associate files generated in this folder"),ToggleOutputFolder);
        fileMenu.Opening+=(s,e)=>{RefreshTreeActions();if(Selected==null)e.Cancel=true;};
        tree.ContextMenuStrip=fileMenu;
        tree.MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Right){int index=tree.IndexFromPoint(e.Location);if(index>=0)tree.SelectedIndex=index;}};
        tree.AllowDrop=true;
        tree.DragEnter+=(s,e)=>UpdateRouteDrag(e);
        tree.DragOver+=(s,e)=>UpdateRouteDrag(e);
        tree.DragLeave+=(s,e)=>ClearRouteDrag();
        tree.DragDrop+=(s,e)=>{
            var source=e.Data.GetData(typeof(TreeNode)) as TreeNode;var target=routeDropTarget;ClearRouteDrag();
            if(source!=null&&target!=null)MovePackageNodeTo(source,target);
        };
        routeDragTimer.Tick+=(s,e)=>{
            if(routeDropTarget==null)return;
            if(routeDragScroll!=0&&tree.Items.Count>0) {
                tree.TopIndex=Math.Max(0,Math.Min(tree.Items.Count-1,tree.TopIndex+routeDragScroll));scroll.RefreshScroll();
            }
            if(folded.Contains(routeDropTarget)&&DateTime.UtcNow-routeDragEntered>TimeSpan.FromMilliseconds(650))Toggle(routeDropTarget);
        };
        Disposed+=(s,e)=>{routeDragTimer.Dispose();fileMenu.Dispose();};
    }
    bool FolderNode(TreeNode node){return node!=null&&(IsVirtualNode(node)||node==root||node==localRoot||
        IsLocalNode(node)&&node.Nodes.Count>0||!IsLocalNode(node)&&packageFolders.Contains((string)node.Tag));}
    string DefaultFolder(TreeNode node){return Under("%GAME%",FolderNode(node)?(string)node.Tag:ParentPath((string)node.Tag));}
    string NodeFolder(TreeNode node) {
        string chosen;
        if(virtualFolders.TryGetValue(node,out chosen))return chosen;
        if(IsLocalNode(node))return Under("%GAME%",FolderNode(node)?(string)node.Tag:ParentPath((string)node.Tag));
        string folder;
        if(!drafts.TryGetValue((string)node.Tag,out folder)||String.IsNullOrWhiteSpace(folder))folder=SuggestedFolder(node);
        return String.IsNullOrWhiteSpace(folder)?DefaultFolder(node):folder;
    }
    string ActualNodePath(TreeNode node) {
        if(IsLocalNode(node))return LocalDestination(node);
        string folder=GeneralInstallRoutes.DirectoryFor(game,NodeFolder(node),false);
        return FolderNode(node)?folder:Path.Combine(folder,Path.GetFileName((string)node.Tag));
    }
    void RefreshTreeActions() {
        if(fileMenu==null)return;
        var node=Selected;bool package=node!=null&&!IsLocalNode(node)&&!IsVirtualNode(node);
        moveItem.Enabled=package;restoreItem.Enabled=package&&drafts.ContainsKey(SelectedPath);
        string backup=null;
        try{if(backupFile!=null)backup=backupFile();}catch(IOException){}catch(UnauthorizedAccessException){}
        backupItem.Visible=backup!=null;
        openItem.Enabled=false;
        try {if(node!=null){string path=ActualNodePath(node);openItem.Enabled=Directory.Exists(path)||File.Exists(path);}}
        catch(IOException){}catch(ArgumentException){}catch(NotSupportedException){}
        string output=OutputFolder(node);
        outputItem.Enabled=output!=null&&readOutputFolders!=null&&writeOutputFolders!=null;
        bool declared=output!=null&&readOutputFolders!=null&&(readOutputFolders()??new List<string>()).Contains(output,StringComparer.OrdinalIgnoreCase);
        outputItem.Text=declared?T("Desvincular pasta de arquivos gerados","Remove generated-folder association"):
            T("Vincular arquivos gerados nesta pasta","Associate files generated in this folder");
    }
    string OutputFolder(TreeNode node) {
        if(node==null||!FolderNode(node))return null;
        try {
            string folder=ReadRoutes(game,new Dictionary<string,string>{{"",NodeFolder(node)}}).Single().Destination;
            if(!folder.StartsWith("%GAME%/",StringComparison.Ordinal))return null;
            return ModOutputIndex.NormalizeFolders(new[]{folder.Substring(7)}).Single();
        }catch(IOException){return null;}catch(ArgumentException){return null;}
    }
    void ToggleOutputFolder() {
        string folder=OutputFolder(Selected);if(folder==null||readOutputFolders==null||writeOutputFolders==null)return;
        try {
            var folders=(readOutputFolders()??new List<string>()).ToList();
            int removed=folders.RemoveAll(path=>String.Equals(path,folder,StringComparison.OrdinalIgnoreCase));
            if(removed==0)folders.Add(folder);
            writeOutputFolders(ModOutputIndex.NormalizeFolders(folders));RefreshTreeActions();
        }catch(IOException ex){feedback.Text=ex.Message;}
    }
    void OpenSelectedLocation() {
        try {
            if(Selected==null)return;string path=ActualNodePath(Selected);Safe.NoLinks(path);
            if(File.Exists(path))System.Diagnostics.Process.Start("explorer.exe","/select,\""+path+"\"");
            else if(Directory.Exists(path))System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path){UseShellExecute=true});
        }catch(Exception ex){if(!(ex is IOException||ex is UnauthorizedAccessException||ex is System.ComponentModel.Win32Exception))throw;feedback.Text=ex.Message;}
    }
    void OpenSelectedBackup() {
        try {string path=backupFile==null?null:backupFile();if(path==null)return;Safe.NoLinks(path);
            System.Diagnostics.Process.Start("explorer.exe","/select,\""+path+"\"");}
        catch(Exception ex){if(!(ex is IOException||ex is UnauthorizedAccessException||ex is System.ComponentModel.Win32Exception))throw;feedback.Text=ex.Message;}
    }
    void UpdateRouteDrag(DragEventArgs e) {
        var source=e.Data.GetData(typeof(TreeNode)) as TreeNode;var point=tree.PointToClient(new Point(e.X,e.Y));
        int index=tree.IndexFromPoint(point);var target=index>=0?tree.Items[index] as TreeNode:null;
        bool valid=source!=null&&tree.Items.Contains(source)&&!IsLocalNode(source)&&!IsVirtualNode(source)&&source!=root&&FolderNode(target)&&
            source!=target&&(IsLocalNode(target)||!((string)target.Tag).StartsWith((string)source.Tag+"/",StringComparison.OrdinalIgnoreCase));
        e.Effect=valid?DragDropEffects.Move:DragDropEffects.None;
        if(!valid){ClearRouteDrag();return;}
        if(routeDropTarget!=target){routeDropTarget=target;routeDragEntered=DateTime.UtcNow;tree.Invalidate();}
        const int edge=32;
        routeDragScroll=point.Y<edge?-Math.Max(1,(edge-point.Y)/7):point.Y>tree.ClientSize.Height-edge?Math.Max(1,(point.Y-tree.ClientSize.Height+edge)/7):0;
        routeDragTimer.Start();
    }
    void ClearRouteDrag(){routeDragTimer.Stop();routeDropTarget=null;routeDragScroll=0;tree.Invalidate();}
    internal bool MovePackageNodeTo(TreeNode source,TreeNode target) {
        if(source==null||target==null||source==root||IsLocalNode(source)||IsVirtualNode(source)||!FolderNode(target)||source==target||
            !IsLocalNode(target)&&((string)target.Tag).StartsWith((string)source.Tag+"/",StringComparison.OrdinalIgnoreCase))return false;
        string folder=NodeFolder(target);
        if(FolderNode(source))folder=Under(folder,Path.GetFileName((string)source.Tag));
        return MovePackageNode(source,folder);
    }
    internal bool MovePackageNode(TreeNode source,string folder) {
        if(source==null||IsLocalNode(source)||IsVirtualNode(source)||!nodes.ContainsValue(source))return false;
        try {
            string path=(string)source.Tag;
            var candidate=new Dictionary<string,string>(drafts,StringComparer.OrdinalIgnoreCase);candidate[path]=folder;
            var routes=ReadRoutes(game,candidate);
            var files=preview==null?new string[0]:preview.Paths;
            var mapped=files.Select(file=>GeneralInstallRoutes.Map(file,routes)).ToArray();
            if(DestinationCollision(mapped))
                throw new IOException(T("Dois itens ficariam no mesmo destino. Escolha outra pasta.","Two items would occupy the same destination. Choose another folder."));
            string proposed=routes.Single(route=>String.Equals(route.Folder,path,StringComparison.OrdinalIgnoreCase)).Destination;
            string current=ReadRoutes(game,new Dictionary<string,string>{{"",NodeFolder(source)}}).Single().Destination;
            if(String.Equals(current,proposed,StringComparison.OrdinalIgnoreCase))return false;
            if(FolderNode(source)&&proposed.StartsWith(current+"/",StringComparison.OrdinalIgnoreCase))
                throw new IOException(T("Uma pasta não pode ser movida para dentro dela mesma.","A folder cannot move inside itself."));
            bool confirmed=TestRouteConfirmation!=null?TestRouteConfirmation(path,proposed):GeneralDialog.Message(FindForm(),
                T("Mudar o destino de ","Change the destination of ")+source.Text+T(" para "," to ")+proposed+"?\n\n"+
                T("A mudança será aplicada quando você salvar o mod.","The change will be applied when you save the mod."),
                T("Mudar destino","Change destination"),MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.Yes;
            if(!confirmed)return false;
            drafts[path]=proposed;RefreshVisualPlacement(source);NotifyDraftChanged();return true;
        }catch(Exception ex){if(!(ex is IOException||ex is ArgumentException||ex is NotSupportedException))throw;feedback.Text=ex.Message;return false;}
    }
    internal static bool DestinationCollision(string[] paths) {
        var known=new HashSet<string>(paths,StringComparer.OrdinalIgnoreCase);
        if(known.Count!=paths.Length)return true;
        foreach(string path in paths)
            for(int slash=path.LastIndexOf('/');slash>=0;slash=path.LastIndexOf('/',slash-1)) {
                if(known.Contains(path.Substring(0,slash)))return true;
                if(slash==0)break;
            }
        return false;
    }
}
