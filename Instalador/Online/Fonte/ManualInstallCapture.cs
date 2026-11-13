using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;

internal sealed class ManualInstallCapture {
    internal sealed class FileState {
        internal string Path, Hash;
        internal long Size;
        internal bool Added;
    }

    readonly string root;
    readonly Func<string,bool> allowed;
    readonly string[] scanRoots;
    readonly Dictionary<string,FileState> before;

    internal ManualInstallCapture(string root, bool cyberpunk) {
        this.root=Path.GetFullPath(root).TrimEnd('\\');
        if(this.root.Length<=3||this.root.StartsWith(@"\\",StringComparison.Ordinal))
            throw new IOException("Escolha a pasta local de instalação do jogo.");
        allowed=cyberpunk?(Func<string,bool>)FixedInstaller.ManualCapturePath:InstalledSnapshot.SafeGenericPath;
        scanRoots=cyberpunk?FixedInstaller.ManualCaptureRoots():new[]{""};
        before=Read().ToDictionary(file=>file.Path,StringComparer.OrdinalIgnoreCase);
    }

    internal List<FileState> Changes() {
        var result=new List<FileState>();
        foreach(var file in Read()){
            FileState previous;
            file.Added=!before.TryGetValue(file.Path,out previous);
            if(file.Added||file.Hash!=previous.Hash)result.Add(file);
        }
        return result.OrderBy(file=>file.Path,StringComparer.OrdinalIgnoreCase).ToList();
    }

    IEnumerable<FileState> Read() {
        var pending=new Stack<string>(scanRoots.Select(path=>path.Length==0?root:Safe.In(root,path)));
        int entries=0;
        while(pending.Count>0){
            string folder=pending.Pop();
            Safe.NoLinks(folder);
            if(!Directory.Exists(folder))continue;
            foreach(string item in Directory.EnumerateFileSystemEntries(folder)){
                if(++entries>200000)throw new IOException("Há arquivos demais para capturar nesta instalação.");
                string relative=item.Substring(root.Length+1).Replace('\\','/');
                var attributes=File.GetAttributes(item);
                if((attributes&FileAttributes.ReparsePoint)!=0)continue;
                if((attributes&FileAttributes.Directory)!=0){
                    if(InstalledSnapshot.SafeGenericPath(relative))pending.Push(item);
                }else if(allowed(relative)){
                    using(var stream=new FileStream(item,FileMode.Open,FileAccess.Read,FileShare.Read))
                    using(var hash=System.Security.Cryptography.SHA256.Create())
                        yield return new FileState{Path=relative,Size=stream.Length,Hash=Safe.Hex(hash.ComputeHash(stream))};
                }
            }
        }
    }
}

public sealed partial class InstalledSnapshotInstaller {
    internal string RegisterManual(GeneralModEntry entry, IReadOnlyCollection<ManualInstallCapture.FileState> selected) {
        if(selected.Count==0)throw new IOException("Selecione os arquivos da instalação manual.");
        bool cyberpunk=File.Exists(Safe.In(destination,"bin/x64/Cyberpunk2077.exe"));
        string game=cyberpunk?"cyberpunk2077":"generic";
        string owner=InstalledSnapshot.Owner(entry,entry.Variants.Single());
        var manifest=new InstalledSnapshotManifest {
            Schema=1,Name=entry.Name,Game=game,GameFileVersion=cyberpunk?catalog.GameFileVersion:null,
            List=new GeneralModList{Mods=new List<GeneralModEntry>{entry}},
            Files=selected.Select(file=>new InstalledSnapshotFile{
                Path=file.Path,Sha256=file.Hash,Size=file.Size,Kind="file",Owners=new List<string>{owner}
            }).ToList()
        };
        var receipt=new Receipt{
            Id=Guid.NewGuid().ToString("N"),Root=destination,Status="installed",Manifest=manifest,
            ActiveOwners=new List<string>{owner},ManualCapture=true,
            Files=selected.Select(file=>new Change{
                Path=file.Path,After=file.Hash,Size=file.Size,Applied=true,Owned=file.Added
            }).ToList()
        };
        using(AcquireLock(game)){
            Closed(game);
            InstalledSnapshot.Validate(manifest,catalog,true);
            var managed=new HashSet<string>(ProjectDeletionFiles().Select(file=>file.Path),StringComparer.OrdinalIgnoreCase);
            managed.UnionWith(new GeneralModInstaller(destination,Path.Combine(appData,"Instalacoes"))
                .ProjectDeletionFiles().Select(file=>file.Path));
            if(cyberpunk)managed.UnionWith(new FixedInstaller(catalog,destination,fixture).ProjectDeletionFiles(null).Select(file=>file.Path));
            if(receipt.Files.Any(file=>managed.Contains(file.Path)))
                throw new IOException("Um dos arquivos selecionados já tem registro de instalação. Desmarque-o e tente novamente.");
            if(receipt.Files.Any(file=>!Matches(file)))
                throw new IOException("Um arquivo mudou após a captura. Confira a instalação novamente.");
            Save(receipt);
        }
        return receipt.Id;
    }

    internal void ForgetManual(string id) {
        var receipt=Read(id);
        if(receipt==null||!receipt.ManualCapture)return;
        using(AcquireLock(receipt.Manifest.Game))File.Delete(ReceiptPath(id));
    }
}

internal sealed partial class GeneralModWindow {
    async void CaptureManualInstall() {
        if(working||scanning||associationChoice!=null||!ConfirmProjectNavigation())return;
        string root=state.Destination;
        if(String.IsNullOrWhiteSpace(root)||!Directory.Exists(root)){
            ShowError(new IOException(T("Escolha a pasta de instalação nas opções.", "Choose the installation folder in Options.")));
            return;
        }
        string title=T("Registrar instalação manual","Record manual installation");
        using(var name=new GeneralNameDialog(title,"",
            T("O Mod Loader vai conferir a pasta antes da instalação.","Mod Loader will scan the folder before you install."),
            T("Nome do mod","Mod name"))){
            if(name.ShowDialog(this)!=DialogResult.OK)return;
            working=true;
            Busy(true);
            bool saved=false;
            try{
                EnsureGameClosed(root);
                detail.Text=T("Conferindo arquivos antes da instalação…","Scanning files before installation…");
                bool cyberpunk=IsCyberpunk(root);
                var capture=await Task.Run(()=>new ManualInstallCapture(root,cyberpunk));
                if(IsDisposed||exitApproved)return;
                if(GeneralDialog.Message(this,T("Instale o mod manualmente. Quando terminar, clique em OK para revisar os arquivos.",
                    "Install the mod manually. When finished, click OK to review the files."),title,MessageBoxButtons.OKCancel,MessageBoxIcon.Information)!=DialogResult.OK)return;
                EnsureGameClosed(root);
                detail.Text=T("Conferindo alterações…","Checking for changes…");
                var changes=await Task.Run(()=>capture.Changes());
                if(IsDisposed||exitApproved)return;
                if(changes.Count==0){
                    GeneralDialog.Message(this,T("Nenhum arquivo novo ou alterado foi encontrado.","No new or changed files were found."),title,MessageBoxButtons.OK,MessageBoxIcon.Information);
                    return;
                }
                using(var review=new ManualInstallReview(changes)){
                    if(review.ShowDialog(this)!=DialogResult.OK)return;
                    var entry=NewEntry(name.ResultName,null);
                    var variant=entry.Variants.Single();
                    string owner=InstalledSnapshot.Owner(entry,variant);
                    variant.Sources.Add("installed://"+owner);
                    var files=review.Selected;
                    working=false;
                    BeginProjectEdit();
                    InstalledSnapshotInstaller installer=null;
                    string receipt=null;
                    try{
                        ApplyEdited(entry,entry,variant.Id,false,finish:false);
                        FinishProjectEdit();
                        SaveProjectEdits((target,targetState)=>{
                            installer=new InstalledSnapshotInstaller(root,target,catalog,testMode);
                            receipt=installer.RegisterManual(entry,files);
                            targetState.SnapshotReceipts[owner]=receipt;
                        });
                    }catch{
                        if(receipt!=null){installer.ForgetManual(receipt);state.SnapshotReceipts.Remove(owner);}
                        if(projectDraft!=null)DiscardProjectEdits();
                        throw;
                    }
                    saved=true;
                }
            }catch(Exception ex){if(!IsDisposed)ShowError(ex);}
            finally{
                working=false;
                if(!IsDisposed&&!exitApproved){
                    Busy(false);
                    detail.Text=saved?T("Instalação manual registrada.","Manual installation recorded."):"";
                }
            }
            if(saved&&!IsDisposed&&!exitApproved)await Scan();
        }
    }
}

internal sealed class ManualInstallReview : GeneralDialog {
    readonly CheckedListBox files=new CheckedListBox{Dock=DockStyle.Fill,CheckOnClick=true,IntegralHeight=false};
    readonly IReadOnlyList<ManualInstallCapture.FileState> changes;

    internal ManualInstallReview(IReadOnlyList<ManualInstallCapture.FileState> changes)
        :base(T("Revisar instalação manual","Review manual installation")) {
        this.changes=changes;
        ClientSize=new Size(800,480);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20,8,20,16),ColumnCount=1,RowCount=3};
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label{AutoSize=true,Text=T("Marque somente os arquivos do mod. Arquivos preexistentes serão preservados ao desinstalar.",
            "Select only the mod's files. Uninstall preserves pre-existing files.")},0,0);
        foreach(var file in changes)files.Items.Add((file.Added?T("Novo","New"):T("Alterado","Changed"))+"  ·  "+file.Path);
        var surface=new GeneralSurface{Dock=DockStyle.Fill,Padding=new Padding(10)};
        surface.Controls.Add(new GeneralScrollView(files));layout.Controls.Add(surface,0,1);
        var commands=GeneralTableDialogLayout.Commands();
        var save=GeneralStyle.Button(T("Registrar","Record"),()=>Complete(DialogResult.OK),true);
        save.Enabled=false;
        files.ItemCheck+=(sender,args)=>{
            int count=files.CheckedItems.Count+(args.NewValue==CheckState.Checked?1:0)-(args.CurrentValue==CheckState.Checked?1:0);
            save.Enabled=count>0;
        };
        var cancel=GeneralStyle.Button(T("Cancelar","Cancel"),()=>Complete(DialogResult.Cancel));
        commands.Controls.Add(save);commands.Controls.Add(cancel);
        layout.Controls.Add(commands,0,2);Content.Controls.Add(layout);
        CancelButton=cancel;
        GeneralStyle.Apply(this,GeneralStyle.Dark);files.BorderStyle=BorderStyle.None;
    }

    internal ManualInstallCapture.FileState[] Selected { get {return files.CheckedIndices.Cast<int>().Select(index=>changes[index]).ToArray();} }

    static string T(string pt,string en){return GeneralStyle.T(pt,en);}
}
