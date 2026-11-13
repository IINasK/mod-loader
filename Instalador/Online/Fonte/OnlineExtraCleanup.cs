using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class P2PSyncExtra {
    internal string Name, GroupKey;
    internal bool Proven, Managed;
    internal OwnedChange[] Files;
    public override string ToString(){
        return Name+(Proven?"":" · "+GeneralStyle.T("Origem desconhecida — preservado","Unknown origin — preserved"))+" · "+(Files.Length==1&&Files[0].Before==null
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
    static readonly string[] CleanupBundleRoots = ExtraBundleRoots.Concat(new[]{ExtraFileRoots[1]}).ToArray();

    static bool InFolder(string path, string folder) {
        return path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);
    }

    internal static string[] ManualCaptureRoots() {
        return ExtraFileRoots.Concat(ExtraBundleRoots.Where(root=>root!=RedmodRoot)).ToArray();
    }

    internal static bool ManualCapturePath(string path) {
        return SyncModPath(path)&&LocalModPackage.Allowed(path);
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

    void VerifyExtraCleanupBuild() {
        if(!Installer.VersionSupported(FixedData.GameVersion(game))||FixedData.GameBuild(game)!=catalog.GameFileVersion)
            throw new IOException("A limpeza precisa da mesma versão do jogo indicada no catálogo.");
    }

    HashSet<string> KnownModPaths() {
        return new HashSet<string>(catalog.Packages.SelectMany(NightCityDensity.KnownFiles).Select(file=>file.Path),StringComparer.OrdinalIgnoreCase);
    }

    List<string> FindExtraPaths(bool includeKnown=false,bool verifyBuild=true) {
        if(verifyBuild)VerifyExtraCleanupBuild();
        var known = KnownModPaths();
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

    internal static string ModFolder(string path) {
        foreach(string root in ExtraBundleRoots.Concat(ExtraFileRoots.Skip(1))) {
            if(!InFolder(path,root))continue;
            int child=path.IndexOf('/',root.Length+1);
            return child<0?null:path.Substring(0,child);
        }
        return null;
    }

    internal IEnumerable<ModFileAnchor> CatalogAnchors() {
        return catalog.Packages.SelectMany(mod=>NightCityDensity.KnownFiles(mod)
            .Select(file=>new ModFileAnchor{Owner=mod.Id,Path=file.Path,Hash=file.Sha256}));
    }

    Dictionary<string,string> AssociatedExtras(IEnumerable<string> paths,IEnumerable<InstalledSnapshotFile> files) {
        var anchors=files.Where(file=>file.Kind=="file").SelectMany(file=>file.Owners.Select(owner=>
            new ModFileAnchor{Owner=owner.Split('/')[0],Path=file.Path,Hash=file.Sha256}));
        return InstalledFileOwnership.FindAssociated(game,CatalogAnchors().Concat(anchors),ModFolder,paths);
    }
    internal List<P2PSyncDifference> ReadUnlistedModFiles(IEnumerable<string> listedPaths,string appData=null,Func<string,string> readHash=null) {
        var listed=new HashSet<string>(listedPaths,StringComparer.OrdinalIgnoreCase);
        var ownership=SyncOwnership(appData);
        var names=OwnershipNames();
        var result=new List<P2PSyncDifference>();
        foreach(string path in FindExtraPaths(true,false).Where(path=>InstalledSnapshot.SafeGenericPath(path)&&!listed.Contains(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)){
            string owner=null;
            if(ownership.Contains(path)){
                try{
                    string hash=(readHash??Safe.Hash)(Safe.In(game,path));
                    owner=UniqueOwner(ownership[path].Where(file=>file.Hash==hash));
                }catch(IOException){}catch(UnauthorizedAccessException){}
            }
            result.Add(new P2PSyncDifference{Kind="found",Path=path,Mod=ExtraName(path,owner,names),ProvenOwner=owner});
        }
        return result;
    }

    internal List<OwnedChange> PlanExtraReset(string appData=null,IEnumerable<string> selected=null) {
        var ownership=SyncOwnership(appData);
        var paths=selected==null?FindExtraPaths():SelectedExtraPaths(selected);
        return paths.Where(path=>ownership.Contains(path)&&SyncModPath(path))
            .Select(path=>new OwnedChange{Path=path,Before=Safe.Hash(Safe.In(game,path))})
            .Where(file=>ownership[file.Path].Any(anchor=>anchor.Hash==file.Before)).ToList();
    }

    List<string> SelectedExtraPaths(IEnumerable<string> selected) {
        VerifyExtraCleanupBuild();
        var paths=selected.Where(SyncModPath).Distinct(StringComparer.OrdinalIgnoreCase).Take(ExtraResetLimit+1).ToArray();
        if(paths.Length>ExtraResetLimit)throw new IOException("Há arquivos extras demais para revisar nesta limpeza.");
        var known=KnownModPaths();
        var protectedBundles=new HashSet<string>(known.Where(path=>ExtraBundleRoots.Any(root=>InFolder(path,root)))
            .Select(ModFolder).Where(folder=>folder!=null),StringComparer.OrdinalIgnoreCase);
        return paths.Where(path=>!known.Contains(path)&&!protectedBundles.Contains(ModFolder(path))&&File.Exists(Safe.In(game,path)))
            .OrderBy(path=>path,StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static bool SyncModPath(string path) {
        return !String.IsNullOrEmpty(path)&&!RetiredPackEntries.LocalDronePath(path)&&!InstalledFileOwnership.PrivateState(path)&&ExtraFilePath(path)&&InstalledSnapshot.SafeGenericPath(path);
    }

    static bool SyncBundlePath(string path,bool localCleanup=false) {
        return !String.IsNullOrEmpty(path)&&!RetiredPackEntries.LocalDronePath(path)&&InstalledSnapshot.SafeGenericPath(path)&&
            (localCleanup?CleanupBundleRoots:ExtraBundleRoots).Any(root=>InFolder(path,root)&&path.IndexOf('/',root.Length+1)<0);
    }

    // These files only enter the receiver's quarantine, never the shared inventory.
    static bool LocalCetResidue(string path) {
        if(String.IsNullOrEmpty(path)||!InFolder(path,ExtraBundleRoots[1])||!ExtraFilePath(path)||RetiredPackEntries.LocalDronePath(path))
            return false;
        if(!path.EndsWith(".log",StringComparison.OrdinalIgnoreCase)&&
            !Path.GetFileName(path).Equals("db.sqlite3",StringComparison.OrdinalIgnoreCase))return false;
        string scope=Path.ChangeExtension(path,".txt");
        return !InstalledFileOwnership.PrivateState(scope)&&InstalledSnapshot.SafeGenericPath(scope);
    }

    internal static bool SyncCleanupEntry(string path,string hash) {
        return hash==null?SyncBundlePath(path,true):(SyncModPath(path)||LocalCetResidue(path))&&FixedData.HashValid(hash);
    }

    internal static bool SyncInventoryEntry(string path,string hash) {
        return hash==null?SyncBundlePath(path):SyncModPath(path)&&FixedData.HashValid(hash);
    }

    static bool EmptyDirectory(string path) {
        Safe.NoLinks(path);
        return Directory.Exists(path)&&!Directory.EnumerateFileSystemEntries(path).Any();
    }

    bool EmptySyncTree(string path) {
        Safe.NoLinks(path);
        return Directory.Exists(path)&&!ExtraTreeFiles(path).Any();
    }

    IEnumerable<string> EmptyCleanupBundles() {
        foreach(string root in CleanupBundleRoots){
            string directory=Safe.In(game,root);
            if(!Directory.Exists(directory))continue;
            foreach(string bundle in Directory.EnumerateDirectories(directory)){
                string relative=RelativeGamePath(bundle);
                if(SyncBundlePath(relative,true)&&EmptySyncTree(bundle))
                    yield return relative;
            }
        }
    }

    internal Dictionary<string,string> ReadSyncInventory(IReadOnlyCollection<InstalledSnapshotFile> packageFiles,Action checkpoint=null,string appData=null) {
        var included=new HashSet<string>(packageFiles.Select(file=>file.Path),StringComparer.OrdinalIgnoreCase);
        var ownership=SyncOwnership(appData);
        var paths=ownership.Select(group=>group.Key).Where(path=>SyncModPath(path)&&!included.Contains(path));
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(string path in paths){
            if(checkpoint!=null)checkpoint();
            string target=Safe.In(game,path);Safe.NoLinks(target);
            if(!File.Exists(target))continue;
            string hash=Safe.Hash(target);
            if(!ownership[path].Any(file=>file.Hash==hash))continue;
            if(result.Count>=InstalledSnapshot.MaxFiles)throw new IOException("Há mods demais fora do pacote para comparar.");
            result.Add(path,hash);
        }
        return result;
    }

    HashSet<string> SyncProtectedPaths(InstalledSnapshotManifest host) {
        var available=new HashSet<string>(host.List.Mods.Select(mod=>mod.Id),StringComparer.Ordinal);
        var unavailable=new HashSet<string>((host.Project??host.List).Mods.Where(mod=>!available.Contains(mod.Id))
            .SelectMany(mod=>mod.Variants).Select(variant=>variant.CyberpunkId).Where(id=>id!=null),StringComparer.Ordinal);
        if(available.Any(GeneralModLibrary.IsHystBody))
            unavailable.Remove(GeneralModLibrary.VanillaFeetId);
        var protectedPaths=host.Files.Select(file=>file.Path).Concat(host.SyncGameInventory.Keys)
            .Concat(AssociatedExtras(FindExtraPaths(true),host.Files).Keys);
        return new HashSet<string>(protectedPaths.Concat(catalog.Packages.Where(mod=>mod.Immutable||unavailable.Contains(mod.Id))
            .SelectMany(NightCityDensity.KnownFiles).Select(file=>file.Path)),StringComparer.OrdinalIgnoreCase);
    }

    bool SyncCleanupPath(string path,HashSet<string> keep) {
        if(LocalCetResidue(path))return SyncCleanupBundle(ModFolder(path),keep);
        if(!SyncModPath(path)||keep.Contains(path))return false;
        string bundle=ExtraBundleRoots.FirstOrDefault(root=>InFolder(path,root));
        if(bundle==null)return true;
        int end=path.IndexOf('/',bundle.Length+1);
        if(end<0)return true;
        string folder=path.Substring(0,end);
        return !catalog.Packages.Where(mod=>mod.Immutable).SelectMany(NightCityDensity.KnownFiles).Any(file=>InFolder(file.Path,folder));
    }

    static bool SyncCleanupBundle(string path,HashSet<string> keep) {
        return SyncBundlePath(path,true)&&!keep.Contains(path)&&!keep.Any(file=>InFolder(file,path));
    }

    // Only exact recorded paths and hashes prove ownership; folder names and companions do not.
    ILookup<string,ModFileAnchor> SyncOwnership(string appData) {
        var anchors=CatalogAnchors().Concat(HistoricalReceipts.CleanupAnchors(catalog)).ToList();
        Action<Func<IEnumerable<ProjectDeleteFile>>> add=read=>{
            try {
                var recorded=read().Where(file=>!file.PreserveManualOriginal&&file.OwnerKeys!=null&&file.OwnerKeys.Count>0&&FixedData.HashValid(file.Hash))
                    .SelectMany(file=>file.OwnerKeys.Select(key=>new ModFileAnchor{Path=file.Path,Hash=file.Hash,Owner=key.Split('/')[0],LocalReceipt=true})).ToArray();
                anchors.AddRange(recorded);
            } catch(IOException) { OnlineDiagnostics.Record("p2p.sync.ownership.unavailable",null,"Recibo indisponível; arquivos sem outra prova serão preservados."); }
              catch(UnauthorizedAccessException) { OnlineDiagnostics.Record("p2p.sync.ownership.unavailable",null,"Recibo inacessível; arquivos sem outra prova serão preservados."); }
        };
        if(appData!=null){
            add(()=>new GeneralModInstaller(game,Path.Combine(appData,"Instalacoes")).ProjectDeletionFiles(true));
            add(()=>new InstalledSnapshotInstaller(game,appData,catalog,fixture).ProjectDeletionFiles(true,skipUnreadable:true));
        }
        if(Directory.Exists(Data)){
            foreach(string path in AllReceiptPaths().Where(path=>!String.Equals(Path.GetDirectoryName(path),Safe.In(Data,"limpezas"),StringComparison.OrdinalIgnoreCase))){
                try {
                    var receipt=ReadReceipt(path);
                    if(receipt.Status!="instalado"||receipt.Mods.Count==0||
                        receipt.Operation!=null&&receipt.Operation!="install"&&receipt.Operation!=LocalModPackage.ReceiptOperation)continue;
                    anchors.AddRange(receipt.Files.Where(file=>file.Applied&&!file.Reverted&&!file.UndoStarted&&FixedData.HashValid(file.After))
                        .Select(file=>new ModFileAnchor{Path=file.Path,Hash=file.After,Owner=receipt.Mods.Count==1?receipt.Mods[0]:null,LocalReceipt=true}));
                } catch(IOException) { OnlineDiagnostics.Record("p2p.sync.ownership.unavailable",null,"Recibo inválido; arquivos sem outra prova serão preservados."); }
                  catch(UnauthorizedAccessException) { OnlineDiagnostics.Record("p2p.sync.ownership.unavailable",null,"Recibo inacessível; arquivos sem outra prova serão preservados."); }
            }
        }
        return anchors.Where(file=>!String.IsNullOrEmpty(file.Path)&&!GeneralInstallRoutes.External(file.Path)&&FixedData.HashValid(file.Hash))
            .ToLookup(file=>file.Path,StringComparer.OrdinalIgnoreCase);
    }

    internal P2PSyncExtra[] PlanSyncExtras(InstalledSnapshotManifest host,Action checkpoint=null,string appData=null) {
        if(host.SyncGameInventory==null)return new P2PSyncExtra[0];
        var keep=SyncProtectedPaths(host);
        var paths=FindExtraPaths(true).Where(path=>SyncCleanupPath(path,keep)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var empty=EmptyCleanupBundles().Where(path=>SyncCleanupBundle(path,keep)).ToArray();
        if(paths.Length+empty.Length>ExtraResetLimit)throw new IOException("Há arquivos extras demais para revisar nesta sincronização.");
        var ownership=SyncOwnership(appData);
        var names=OwnershipNames();
        var files=new List<P2PSyncExtra>();
        foreach(string path in paths){
            if(checkpoint!=null)checkpoint();
            string hash=Safe.Hash(Safe.In(game,path));
            var matches=ownership[path].Where(file=>file.Hash==hash).ToArray();
            string owner=UniqueOwner(matches);
            string name=ExtraName(path,owner,names);
            bool managed=matches.Any(match=>match.LocalReceipt);
            string group=owner!=null?(managed?"managed:":"catalog:")+owner:matches.Length>0?"file:"+path:"unknown:"+name;
            files.Add(new P2PSyncExtra{Name=name,GroupKey=group,Proven=matches.Length>0,Managed=managed,Files=new[]{new OwnedChange{Path=path,Before=hash}}});
        }
        // Unknown files stay separate even when their folder has the name of a registered mod.
        return files.GroupBy(extra=>extra.GroupKey,StringComparer.Ordinal)
            .Select(group=>new P2PSyncExtra{Name=group.First().Name,Proven=group.First().Proven,Managed=group.First().Managed,Files=group.SelectMany(extra=>extra.Files).ToArray()})
            .Concat(empty.Select(path=>new P2PSyncExtra{Name=Path.GetFileName(path),Files=new[]{new OwnedChange{Path=path}}}))
            .OrderByDescending(extra=>extra.Proven).ThenBy(extra=>extra.Name).ToArray();
    }

    Dictionary<string,string> OwnershipNames() {
        return catalog.Packages.Concat(HistoricalReceipts.CleanupRecipes(catalog))
            .Where(mod=>!String.IsNullOrWhiteSpace(mod.Name)).GroupBy(mod=>mod.Id,StringComparer.Ordinal)
            .ToDictionary(group=>group.Key,group=>group.First().Name,StringComparer.Ordinal);
    }

    static string UniqueOwner(IEnumerable<ModFileAnchor> matches) {
        var ids=matches.Select(file=>file.Owner).Where(id=>!String.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).Take(2).ToArray();
        return ids.Length==1?ids[0]:null;
    }

    static string ExtraName(string path,string owner,Dictionary<string,string> names) {
        string name;
        return owner!=null&&names.TryGetValue(owner,out name)?name:SyncExtraName(path);
    }

    static string SyncExtraName(string path) {
        string root=ExtraBundleRoots.Concat(ExtraFileRoots).First(folder=>InFolder(path,folder));
        var relative=path.Substring(root.Length+1);
        int slash=relative.IndexOf('/');
        if(slash>=0)return relative.Substring(0,slash);
        var stem=Path.GetFileNameWithoutExtension(relative);
        return stem.EndsWith(".archive",StringComparison.OrdinalIgnoreCase)?Path.GetFileNameWithoutExtension(stem):stem;
    }

    static bool SyncCleanupFolder(string path) {
        return !String.IsNullOrEmpty(path)&&!RetiredPackEntries.LocalDronePath(path)&&InstalledSnapshot.SafeGenericPath(path)&&
            CleanupBundleRoots.Any(root=>InFolder(path,root));
    }

    static IEnumerable<string> SyncParentFolders(string file) {
        for(int slash=file.LastIndexOf('/');slash>0;slash=file.LastIndexOf('/',slash-1)){
            string parent=file.Substring(0,slash);
            if(!SyncCleanupFolder(parent))yield break;
            yield return parent;
        }
    }

    internal void ApplySyncCleanup(List<OwnedChange> approved,InstalledSnapshotManifest host,IEnumerable<string> installedPaths,Action<string> report,string appData=null) {
        if(approved==null||approved.Count==0)return;
        if(host.SyncGameInventory==null||approved.Count>ExtraResetLimit)throw new IOException("A comparação dos mods extras não está disponível.");
        var keep=SyncProtectedPaths(host);
        keep.UnionWith(installedPaths);
        var ownership=SyncOwnership(appData);
        if(approved.Any(file=>file==null||file.Before==null||!SyncCleanupEntry(file.Path,file.Before)||
            !ownership[file.Path].Any(anchor=>anchor.Hash==file.Before)||
            !SyncCleanupPath(file.Path,keep))||
            approved.Select(file=>file.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=approved.Count)
            throw new IOException("A seleção contém arquivos protegidos ou sem vínculo comprovado. Compare os mods novamente.");
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
        },report,sync:true,emptyShells:()=>approved.SelectMany(file=>SyncParentFolders(file.Path))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(path=>path.Length)
            .Where(path=>!keep.Contains(path)&&!keep.Any(file=>InFolder(file,path))&&EmptyDirectory(Safe.In(game,path)))
            .Select(path=>new OwnedChange{Path=path}));
    }

    internal string ApplyExtraReset(List<OwnedChange> approved,Action<string> report,string appData=null,IEnumerable<string> selected=null) {
        if(approved==null||approved.Count==0)return "Nenhum mod fora da lista encontrado.";
        return ApplyExtraCleanup(()=>{
            var current=PlanExtraReset(appData,selected);
            if(!current.Select(f=>f.Path+"|"+f.Before).SequenceEqual(approved.Select(f=>f.Path+"|"+f.Before),StringComparer.OrdinalIgnoreCase))
                throw new IOException("Os arquivos mudaram após a conferência. Abra a limpeza novamente para revisar a lista atual.");
            return current;
        },report);
    }

    bool MatchesExtraBackup(OwnedChange change,string path) {
        return change.Before==null?EmptySyncTree(path):File.Exists(path)&&Safe.Hash(path)==change.Before;
    }

    string ApplyExtraCleanup(Func<List<OwnedChange>> plan,Action<string> report,bool sync=false,Func<IEnumerable<OwnedChange>> emptyShells=null) {
        Closed();
        Directory.CreateDirectory(Data);
        using (var mutex = new FileStream(Safe.In(Data,"operacao.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
            EnsureNoIncomplete();
            var current=plan();
            if(current.Count==0&&emptyShells!=null)current.AddRange(emptyShells());
            if(current.Count==0)return "Nenhum mod extra precisa ser guardado.";
            var receipt = new OnlineReceipt {
                Id=Guid.NewGuid().ToString("N"), CatalogId=catalog.Id, CreatedUtc=DateTime.UtcNow.ToString("o"),
                Status="desativando", Operation=ExtraResetOperation, BackupLayout=sync?"flat-v2":"flat-v1"
            };
            string ledger = Safe.In(Data, "limpezas/" + receipt.Id + ".json");
            Save(ledger, receipt);
            try {
                Action<OwnedChange> move=change=>{
                    Closed();
                    if(receipt.Files.Count>=ExtraResetLimit)throw new IOException("Há arquivos demais para guardar nesta limpeza.");
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
                };
                foreach(var change in current)move(change);
                if(emptyShells!=null)foreach(var shell in emptyShells())move(shell);
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
                || (file.Before==null?(receipt.BackupLayout!="flat-v2"||!SyncCleanupFolder(file.Path)):
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
