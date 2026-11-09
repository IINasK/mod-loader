using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class P2PSyncExtra {
    internal string Name;
    internal OwnedChange[] Files;
    public override string ToString(){
        return Name+" · "+(Files.Length==1&&Files[0].Before==null
            ?GeneralStyle.T("pasta vazia","empty folder")
            :Files.Length+" "+GeneralStyle.T("arquivo(s)","file(s)"));
    }
}

public sealed partial class FixedInstaller {
    const string ExtraResetOperation = "reset-extras";
    const string RedmodRoot = "mods";
    const int ExtraResetLimit = 2000;
    static readonly string[] ExtraFileRoots = {"archive/pc/mod", "r6/scripts", "r6/tweaks"};
    static readonly string[] ExtraBundleRoots = {"red4ext/plugins", "bin/x64/plugins/cyber_engine_tweaks/mods", RedmodRoot};

    static bool InFolder(string path, string folder) {
        return path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);
    }

    static bool ExtraFilePath(string path) {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (InFolder(path, ExtraFileRoots[0])) return extension == ".archive" || extension == ".xl";
        if (InFolder(path, ExtraFileRoots[1])) return extension == ".reds";
        if (InFolder(path, ExtraFileRoots[2])) return extension == ".yaml" || extension == ".yml" || extension == ".tweak";
        if (InFolder(path, ExtraBundleRoots[0]) && path.Substring(ExtraBundleRoots[0].Length+1).IndexOf('/') < 0)
            return extension == ".dll";
        return ExtraBundleRoots.Any(root => InFolder(path, root) && path.Substring(root.Length+1).Contains("/"));
    }

    string RelativeGamePath(string path) {
        return path.Substring(game.Length+1).Replace('\\','/');
    }

    IEnumerable<string> ExtraTreeFiles(string directory) {
        var pending = new Stack<string>();
        pending.Push(directory);
        int visited = 0;
        while (pending.Count > 0) {
            string current = pending.Pop();
            Safe.NoLinks(current);
            foreach (string path in Directory.EnumerateFileSystemEntries(current)) {
                if (++visited > 20000) throw new IOException("Há arquivos demais para conferir nesta pasta.");
                Safe.NoLinks(path);
                if (Directory.Exists(path)) pending.Push(path);
                else yield return RelativeGamePath(path);
            }
        }
    }

    List<string> FindExtraPaths(bool includeKnown=false,bool verifyBuild=true) {
        if (verifyBuild&&(!Installer.VersionSupported(FixedData.GameVersion(game)) || FixedData.GameBuild(game) != catalog.GameFileVersion))
            throw new IOException("A limpeza precisa da mesma versão do jogo indicada no catálogo.");
        var known = new HashSet<string>(catalog.Packages.SelectMany(NightCityDensity.KnownFiles).Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
        var paths = new List<string>();
        foreach (string root in ExtraFileRoots) {
            string directory = Safe.In(game, root);
            if (Directory.Exists(directory)) paths.AddRange(ExtraTreeFiles(directory).Where(ExtraFilePath));
        }
        foreach (string root in ExtraBundleRoots) {
            string directory = Safe.In(game, root);
            if (!Directory.Exists(directory)) continue;
            if (root == ExtraBundleRoots[0])
                paths.AddRange(Directory.EnumerateFiles(directory, "*.dll").Select(RelativeGamePath));
            foreach (string bundle in Directory.EnumerateDirectories(directory)) {
                string relative = RelativeGamePath(bundle);
                Safe.NoLinks(bundle);
                if (!includeKnown&&known.Any(path => InFolder(path, relative))) continue;
                if (root == RedmodRoot && !File.Exists(Safe.In(game, relative + "/info.json"))) continue;
                paths.AddRange(ExtraTreeFiles(bundle));
            }
        }
        paths = paths.Where(path => (includeKnown||!known.Contains(path)) && ExtraFilePath(path)).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        if (!includeKnown && paths.Count > ExtraResetLimit) throw new IOException("A lista de arquivos fora do catálogo excede o limite de limpeza.");
        return paths;
    }

    internal List<P2PSyncDifference> ReadUnlistedModFiles(IEnumerable<string> listedPaths) {
        var listed=new HashSet<string>(listedPaths,StringComparer.OrdinalIgnoreCase);
        var names=catalog.Packages.SelectMany(mod=>NightCityDensity.KnownFiles(mod).Select(file=>new{file.Path,mod.Name}))
            .GroupBy(item=>item.Path,StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group=>group.Key,group=>group.First().Name,StringComparer.OrdinalIgnoreCase);
        return FindExtraPaths(true,false).Where(path=>SyncModPath(path)&&!listed.Contains(path)).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path=>new P2PSyncDifference{Kind="found",Path=path,Mod=names.ContainsKey(path)?names[path]:SyncExtraName(path)})
            .ToList();
    }

    internal List<OwnedChange> PlanExtraReset() {
        return FindExtraPaths().Select(path => new OwnedChange { Path=path, Before=Safe.Hash(Safe.In(game,path)) }).ToList();
    }

    internal static bool SyncModPath(string path) {
        return !String.IsNullOrEmpty(path)&&ExtraFilePath(path)&&InstalledSnapshot.SafeGenericPath(path);
    }

    static bool SyncBundlePath(string path) {
        return !String.IsNullOrEmpty(path)&&InstalledSnapshot.SafeGenericPath(path)&&ExtraBundleRoots.Any(root=>
            InFolder(path,root)&&path.IndexOf('/',root.Length+1)<0);
    }

    internal static bool SyncInventoryEntry(string path,string hash) {
        return hash==null?SyncBundlePath(path):SyncModPath(path)&&FixedData.HashValid(hash);
    }

    static bool EmptyDirectory(string path) {
        Safe.NoLinks(path);
        return Directory.Exists(path)&&!Directory.EnumerateFileSystemEntries(path).Any();
    }

    IEnumerable<string> EmptySyncBundles() {
        foreach(string root in ExtraBundleRoots){
            string directory=Safe.In(game,root);
            if(!Directory.Exists(directory))continue;
            foreach(string bundle in Directory.EnumerateDirectories(directory)){
                string relative=RelativeGamePath(bundle);
                if(SyncBundlePath(relative)&&EmptyDirectory(bundle))yield return relative;
            }
        }
    }

    internal Dictionary<string,string> ReadSyncInventory(IEnumerable<string> packagePaths,Action checkpoint=null) {
        var included=new HashSet<string>(packagePaths,StringComparer.OrdinalIgnoreCase);
        var paths=FindExtraPaths(true).Where(path=>SyncModPath(path)&&!included.Contains(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var empty=EmptySyncBundles().ToArray();
        if(paths.Length+empty.Length>InstalledSnapshot.MaxFiles)throw new IOException("Há mods demais fora do pacote para comparar.");
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(string path in paths){if(checkpoint!=null)checkpoint();result.Add(path,Safe.Hash(Safe.In(game,path)));}
        foreach(string path in empty)result.Add(path,null);
        return result;
    }

    HashSet<string> SyncProtectedPaths(InstalledSnapshotManifest host) {
        var available=new HashSet<string>(host.List.Mods.Select(mod=>mod.Id),StringComparer.Ordinal);
        var unavailable=new HashSet<string>((host.Project??host.List).Mods.Where(mod=>!available.Contains(mod.Id))
            .SelectMany(mod=>mod.Variants).Select(variant=>variant.CyberpunkId).Where(id=>id!=null),StringComparer.Ordinal);
        if(available.Any(GeneralModLibrary.IsHystBody))
            unavailable.Remove(GeneralModLibrary.VanillaFeetId);
        var protectedPaths=host.Files.Select(file=>file.Path).Concat(host.SyncGameInventory.Keys);
        return new HashSet<string>(protectedPaths.Concat(catalog.Packages.Where(mod=>mod.Immutable||unavailable.Contains(mod.Id))
            .SelectMany(NightCityDensity.KnownFiles).Select(file=>file.Path)),StringComparer.OrdinalIgnoreCase);
    }

    bool SyncCleanupPath(string path,HashSet<string> keep) {
        if(!SyncModPath(path)||keep.Contains(path))return false;
        string bundle=ExtraBundleRoots.FirstOrDefault(root=>InFolder(path,root));
        if(bundle==null)return true;
        int end=path.IndexOf('/',bundle.Length+1);
        if(end<0)return true;
        string folder=path.Substring(0,end);
        return !catalog.Packages.Where(mod=>mod.Immutable).SelectMany(NightCityDensity.KnownFiles).Any(file=>InFolder(file.Path,folder));
    }

    static bool SyncCleanupBundle(string path,HashSet<string> keep) {
        return SyncBundlePath(path)&&!keep.Contains(path)&&!keep.Any(file=>InFolder(file,path));
    }

    internal P2PSyncExtra[] PlanSyncExtras(InstalledSnapshotManifest host,Action checkpoint=null) {
        if(host.SyncGameInventory==null)return new P2PSyncExtra[0];
        var keep=SyncProtectedPaths(host);
        var paths=FindExtraPaths(true).Where(path=>SyncCleanupPath(path,keep)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var empty=EmptySyncBundles().Where(path=>SyncCleanupBundle(path,keep)).ToArray();
        if(paths.Length+empty.Length>ExtraResetLimit)throw new IOException("Há arquivos extras demais para revisar nesta sincronização.");
        var names=catalog.Packages.SelectMany(mod=>NightCityDensity.KnownFiles(mod).Select(file=>new{file.Path,mod.Name}))
            .GroupBy(item=>item.Path,StringComparer.OrdinalIgnoreCase).ToDictionary(group=>group.Key,group=>group.First().Name,StringComparer.OrdinalIgnoreCase);
        var files=new List<OwnedChange>();
        foreach(string path in paths){if(checkpoint!=null)checkpoint();files.Add(new OwnedChange{Path=path,Before=Safe.Hash(Safe.In(game,path))});}
        return files.GroupBy(file=>{string name;return names.TryGetValue(file.Path,out name)?name:SyncExtraName(file.Path);},StringComparer.OrdinalIgnoreCase)
            .Select(group=>new P2PSyncExtra{Name=group.Key,Files=group.ToArray()})
            .Concat(empty.Select(path=>new P2PSyncExtra{Name=Path.GetFileName(path),Files=new[]{new OwnedChange{Path=path}}}))
            .OrderBy(extra=>extra.Name,StringComparer.OrdinalIgnoreCase).ToArray();
    }

    static string SyncExtraName(string path) {
        string root=ExtraBundleRoots.Concat(ExtraFileRoots).First(folder=>InFolder(path,folder));
        var relative=path.Substring(root.Length+1);
        int slash=relative.IndexOf('/');
        if(slash>=0)return relative.Substring(0,slash);
        var stem=Path.GetFileNameWithoutExtension(relative);
        return stem.EndsWith(".archive",StringComparison.OrdinalIgnoreCase)?Path.GetFileNameWithoutExtension(stem):stem;
    }

    internal void ApplySyncCleanup(List<OwnedChange> approved,InstalledSnapshotManifest host,IEnumerable<string> installedPaths,Action<string> report) {
        if(approved==null||approved.Count==0)return;
        if(host.SyncGameInventory==null||approved.Count>ExtraResetLimit)throw new IOException("A comparação dos mods extras não está disponível.");
        var keep=SyncProtectedPaths(host);
        keep.UnionWith(installedPaths);
        if(approved.Any(file=>file==null||!SyncInventoryEntry(file.Path,file.Before)||
            !(file.Before==null?SyncCleanupBundle(file.Path,keep):SyncCleanupPath(file.Path,keep)))||
            approved.Select(file=>file.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=approved.Count)
            throw new IOException("A seleção contém arquivos protegidos ou inválidos.");
        ApplyExtraCleanup(()=>{
            var current=new List<OwnedChange>();
            foreach(var file in approved){
                string path=Safe.In(game,file.Path);
                if(!File.Exists(path)&&!Directory.Exists(path))continue;
                if(!MatchesExtraBackup(file,path))
                    throw new IOException("Um mod extra mudou depois da confirmação. Gere um novo convite para revisar novamente: "+file.Path);
                current.Add(new OwnedChange{Path=file.Path,Before=file.Before});
            }
            return current;
        },report,sync:true,afterMove:()=>{
            var bundles=approved.Where(file=>file.Before!=null).Select(file=>ExtraBundleRoots.FirstOrDefault(root=>InFolder(file.Path,root)))
                .Where(root=>root!=null).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach(string root in bundles){
                foreach(string bundle in approved.Where(file=>file.Before!=null&&InFolder(file.Path,root))
                    .Select(file=>root+"/"+file.Path.Substring(root.Length+1).Split('/')[0]).Distinct(StringComparer.OrdinalIgnoreCase)){
                    if(!SyncCleanupBundle(bundle,keep))continue;
                    string path=Safe.In(game,bundle);
                    if(EmptyDirectory(path))Directory.Delete(path,false);
                }
            }
        });
    }

    internal string ApplyExtraReset(List<OwnedChange> approved,Action<string> report) {
        if(approved==null||approved.Count==0)return "Nenhum mod fora da lista encontrado.";
        return ApplyExtraCleanup(()=>{
            var current=PlanExtraReset();
            if(!current.Select(f=>f.Path+"|"+f.Before).SequenceEqual(approved.Select(f=>f.Path+"|"+f.Before),StringComparer.OrdinalIgnoreCase))
                throw new IOException("Os arquivos mudaram após a conferência. Abra a limpeza novamente para revisar a lista atual.");
            return current;
        },report);
    }

    static bool MatchesExtraBackup(OwnedChange change,string path) {
        return change.Before==null?EmptyDirectory(path):File.Exists(path)&&Safe.Hash(path)==change.Before;
    }

    string ApplyExtraCleanup(Func<List<OwnedChange>> plan,Action<string> report,bool sync=false,Action afterMove=null) {
        Closed();
        Directory.CreateDirectory(Data);
        using (var mutex = new FileStream(Safe.In(Data,"operacao.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
            EnsureNoIncomplete();
            var current=plan();
            if(current.Count==0){
                if(afterMove!=null)afterMove();
                return "Nenhum mod extra precisa ser guardado.";
            }
            var receipt = new OnlineReceipt {
                Id=Guid.NewGuid().ToString("N"), CatalogId=catalog.Id, CreatedUtc=DateTime.UtcNow.ToString("o"),
                Status="desativando", Operation=ExtraResetOperation, BackupLayout=sync?"flat-v2":"flat-v1"
            };
            string ledger = Safe.In(Data, "limpezas/" + receipt.Id + ".json");
            Save(ledger, receipt);
            try {
                foreach (var change in current) {
                    Closed();
                    string source = Safe.In(game, change.Path);
                    if (!MatchesExtraBackup(change,source)) throw new IOException("O arquivo mudou durante a limpeza: " + change.Path);
                    string backup = DisabledPath(receipt, change.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(backup));
                    receipt.Files.Add(change);
                    Save(ledger, receipt);
                    if(change.Before==null)Directory.Move(source,backup);
                    else File.Move(source,backup);
                    change.Applied = true;
                    Save(ledger, receipt);
                    report("Guardado: " + change.Path);
                }
                if(afterMove!=null)afterMove();
                receipt.Status="desativado";
                Save(ledger, receipt);
                return receipt.Files.Count + " arquivo(s) fora da lista guardado(s). Use Desfazer última operação para restaurar.";
            } catch {
                try { UndoDeactivation(receipt,ledger,report); } catch { }
                throw;
            }
        }
    }

    IEnumerable<string> AllReceiptPaths() {
        foreach (var path in Directory.GetFiles(Data, "*.json")) yield return path;
        string imports = Safe.In(Data, "importados");
        Safe.NoLinks(imports);
        if (Directory.Exists(imports))
            foreach (var path in Directory.GetFiles(imports, "*.json")) yield return path;
        string folder = Safe.In(Data, "limpezas");
        if (Directory.Exists(folder))
            foreach (var path in Directory.GetFiles(folder, "*.json")) yield return path;
    }

    void ValidateExtraResetReceipt(OnlineReceipt receipt, string path) {
        string folder = Safe.In(Data, "limpezas");
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!String.Equals(Path.GetDirectoryName(path), folder, StringComparison.OrdinalIgnoreCase)
            || (receipt.BackupLayout!="flat-v1"&&receipt.BackupLayout!="flat-v2") || receipt.Mods.Count != 0 || receipt.Files.Count > ExtraResetLimit)
            throw new IOException("Registro de limpeza inválido.");
        foreach (var file in receipt.Files) {
            if (file == null || String.IsNullOrEmpty(file.Path) || !unique.Add(file.Path) || file.After!=null
                || (file.Before==null?(receipt.BackupLayout!="flat-v2"||!SyncBundlePath(file.Path)):
                    !ExtraFilePath(file.Path)||!FixedData.HashValid(file.Before)))
                throw new IOException("Arquivo fora do escopo da limpeza.");
            Safe.In(game, file.Path);
        }
    }
}

public sealed partial class OnlineWindow {
    async Task CleanExtras() {
        if (busy || automaticFlow || pollingDownload || pollingFacial || densityDialogOpen) return;
        if (automaticQueue.Count > 0 || pendingDensityId != null ||
            (downloadWatch != null && downloadWatch.IsArmed) || (facialQueue != null && facialQueue.IsArmed)) {
            status.Text="Conclua ou cancele a fila atual antes de limpar mods.";
            return;
        }
        try {
            string root=GameRoot();
            List<OwnedChange> plan=null;
            if (!await Run(() => {
                plan=createRemovalInstaller(root).PlanExtraReset();
                return plan.Count==0 ? "Nenhum arquivo de mod fora da lista encontrado." : plan.Count+" arquivo(s) para revisar.";
            }) || plan.Count==0) return;
            if (!ConfirmExtraPlan(root,plan)) return;
            if (MessageBox.Show(this,
                "Confirmar a retirada de "+plan.Count+" arquivo(s) do jogo? Eles serão guardados para restauração.",
                "Confirmar limpeza de mods",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes) return;
            if (!String.Equals(GameRoot(),root,StringComparison.OrdinalIgnoreCase)) return;
            await Run(() => {
                var result=createRemovalInstaller(root).ApplyExtraReset(plan,Report);
                RefreshMods(catalog.Packages,root);
                return result;
            });
        } catch (Exception ex) { status.Text=Friendly(ex); }
    }

    bool ConfirmExtraPlan(string root,List<OwnedChange> plan) {
        using(var dialog=new Form { Text="Revisar mods fora da lista", Width=820, Height=490, StartPosition=FormStartPosition.CenterParent, MinimizeBox=false, MaximizeBox=false }) {
            var content=new TableLayoutPanel { Dock=DockStyle.Fill, Padding=new Padding(14), ColumnCount=1, RowCount=3 };
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var intro=new Label { AutoSize=true, MaximumSize=new Size(760,0), Text=root+"\r\n\r\nRevise os arquivos que vão sair do jogo. A limpeza abrange archives, scripts, tweaks e pastas de mods do CET, RED4ext e REDmod." };
            var paths=new TextBox { Dock=DockStyle.Fill, ReadOnly=true, Multiline=true, WordWrap=false, ScrollBars=ScrollBars.Both, Text=String.Join("\r\n",plan.Select(f=>f.Path)), AccessibleName="Arquivos que serão guardados" };
            var buttons=new FlowLayoutPanel { AutoSize=true, Dock=DockStyle.Fill, FlowDirection=FlowDirection.RightToLeft };
            var next=new Button { Text="Continuar", AutoSize=true, DialogResult=DialogResult.OK };
            var cancel=new Button { Text="Cancelar", AutoSize=true, DialogResult=DialogResult.Cancel };
            buttons.Controls.Add(next);
            buttons.Controls.Add(cancel);
            content.Controls.Add(intro,0,0);
            content.Controls.Add(paths,0,1);
            content.Controls.Add(buttons,0,2);
            dialog.Controls.Add(content);
            dialog.CancelButton=cancel;
            dialog.AcceptButton=cancel;
            return dialog.ShowDialog(this)==DialogResult.OK;
        }
    }
}
