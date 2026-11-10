using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

public sealed partial class FixedInstaller {
    internal FileStream LockForRouteChange() {
        Closed();
        Directory.CreateDirectory(Data);
        var handle = new FileStream(Safe.In(Data,"operacao.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try { EnsureNoIncomplete(); return handle; } catch { handle.Dispose(); throw; }
    }

    internal IEnumerable<string> ProtectedPriorFiles() {
        return AllReceiptPaths().Select(ReadReceipt)
            .Where(receipt=>receipt.Status=="instalado"&&receipt.Operation!=LocalModPackage.ReceiptOperation)
            .SelectMany(receipt=>receipt.Files)
            .Where(file=>file.Before!=null&&file.Applied&&!file.Reverted)
            .Select(file=>Safe.In(game,file.Path));
    }
}

internal sealed partial class GeneralModWindow {
    string RoutedFixedStatus(Work work,string root,Func<string,string> hash) {
        var recipe=Recipe(work);
        var paths=GeneralInstallRoutes.FixedPaths(catalog,recipe,work.Variant.InstallRoutes);
        int count=0;
        foreach(var file in recipe.Files) {
            string target=GeneralInstallRoutes.Resolve(root,paths[file.Path]);
            if(Directory.Exists(target))return GeneralModInstaller.Modified;
            if(!File.Exists(target))continue;
            if(new FileInfo(target).Length!=file.Size||(hash??Safe.Hash)(target)!=file.Sha256)
                return GeneralModInstaller.Modified;
            count++;
        }
        return count==recipe.Files.Count?"Instalado":count==0?GeneralModInstaller.Missing:GeneralModInstaller.Partial;
    }

    async Task<bool?> ChangeInstalledDestinations(GeneralModEntry old, GeneralModEntry edited) {
        var pairs=old.Variants.Select(variant=>new { Before=variant, After=edited.Variants.FirstOrDefault(item=>item.Id==variant.Id) })
            .Where(pair=>pair.After!=null&&!GeneralInstallRoutes.Same(pair.Before.InstallRoutes,pair.After.InstallRoutes)).ToList();
        if(pairs.Count==0)return false;
        string root=state.Destination;
        bool installed=false;
        foreach(var pair in pairs){
            string key=Key(old,pair.Before);
            if(pair.After.CyberpunkId!=null)GeneralInstallRoutes.FixedPaths(catalog,ProfileRecipe(new Work{Entry=edited,Variant=pair.After}),pair.After.InstallRoutes);
            if(String.IsNullOrEmpty(root)||!Directory.Exists(root))continue;
            if(state.CyberpunkImports.Keys.Any(item=>item.StartsWith(key+"/",StringComparison.Ordinal))||DisabledAt(root).Read().ContainsKey(key))
                throw new IOException(T("Reative ou desinstale a cópia registrada antes de mudar seu destino.","Enable or uninstall the recorded copy before changing its destination."));
            var before=new Work { Entry=old,Variant=pair.Before };
            var recipe=pair.Before.CyberpunkId==null?null:ProfileRecipe(before);
            if(recipe!=null&&recipe.Files.Any(file=>File.Exists(GeneralInstallRoutes.Resolve(root,GeneralInstallRoutes.Map(file.Path,pair.Before.InstallRoutes)))))
                installed=true;
            if(state.SnapshotReceipts.ContainsKey(key)||
                !IsCyberpunk(root)&&before.Variant.Sources.Select((source,index)=>before.Receipt(index)).Any(index=>Generic(root).Status(index)!=GeneralModInstaller.Missing))
                installed=true;
        }
        if(!installed)return false;
        if(working||pairs.Count!=1)throw new IOException(T("Altere o destino de uma versão instalada por vez, com a fila parada.","Change the destination of one installed version at a time, with the queue idle."));
        var change=pairs.Single();
        if(!change.Before.Sources.SequenceEqual(change.After.Sources,StringComparer.Ordinal)||change.Before.CyberpunkId==null&&change.Before.Sources.Count!=1)
            throw new IOException(T("Mantenha a fonte e altere o destino de um pacote instalado por vez.","Keep the source and change the destination of one installed package at a time."));
        if(!testMode&&GeneralDialog.Message(this,T("Mover os arquivos instalados deste mod para o novo destino?","Move this mod’s installed files to the new destination?"),
            T("Mudar destino","Change destination"),MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return null;
        EnsureGameClosed(root);
        var work=new Work{Entry=old,Variant=change.Before};
        var fixedFiles=change.Before.CyberpunkId==null?null:ProfileRecipe(work).Files;
        string snapshot;
        state.SnapshotReceipts.TryGetValue(work.Key,out snapshot);
        working=true;Busy(true);
        try{
            return await Task.Run(()=>{
                var shared=RouteOwners(root,work.Key);
                if(snapshot!=null)
                    return Snapshot(root).Relocate(snapshot,work.Key,change.Before.InstallRoutes,change.After.InstallRoutes,shared.Contains);
                using(IsCyberpunk(root)?Cyberpunk(root).LockForRouteChange():null)
                    return Generic(root).Relocate(work.Receipt(0),change.Before.InstallRoutes,change.After.InstallRoutes,shared.Contains,fixedFiles,LocalSource(work,0));
            });
        }finally{working=false;Busy(false);}
    }

    HashSet<string> RouteOwners(string root,string workKey) {
        var shared=ExternalFilesInOtherProjects();
        Action<string,IEnumerable<ProjectDeleteFile>> add=(folder,files)=>{
            foreach(var file in files)shared.Add(GeneralInstallRoutes.Resolve(folder,file.Path));
        };
        add(root,Snapshot(root).ProjectDeletionFiles().Where(file=>file.OwnerKeys.Any(owner=>owner!=workKey)));
        if(IsCyberpunk(root)){
            var engine=Cyberpunk(root);
            add(root,engine.ProjectDeletionFiles(null));
            shared.UnionWith(engine.ProtectedPriorFiles());
        }
        if(projectStore!=null&&project!=null)foreach(var peer in projectStore.Read().Projects.Where(item=>item.Id!=project.Id)){
            var other=projectStore.Load(peer);
            if(!String.Equals(other.Destination,root,StringComparison.OrdinalIgnoreCase))continue;
            string peerData=projectStore.DataPath(peer);
            add(root,new InstalledSnapshotInstaller(root,peerData,catalog).ProjectDeletionFiles());
            add(root,new GeneralModInstaller(root,Path.Combine(peerData,"Instalacoes")).ProjectDeletionFiles());
        }
        return shared;
    }
}

public sealed partial class GeneralModInstaller {
    internal bool Relocate(string key, List<GeneralInstallRoute> before, List<GeneralInstallRoute> after,
        Func<string,bool> shared, IEnumerable<FixedFile> fixedFiles = null, string package = null) {
        string receipt = RecordPath(key);
        using (AcquireLock()) {
            var old = ReadRecord(receipt);
            if (old == null && fixedFiles != null) {
                old = new Record { Key = key, InstallRoutes = before, Files = fixedFiles.Select(file => new RecordFile {
                    Path = GeneralInstallRoutes.Map(file.Path, before), ArchivePath = file.Path,
                    Sha256 = file.Sha256, Size = file.Size, Owned = true
                }).ToList() };
            }
            if (old == null || !old.Files.Any(file => File.Exists(Target(file.Path)))) return false;
            if (!GeneralInstallRoutes.Same(before, old.InstallRoutes))
                throw new IOException("O destino atual difere do registro de instalação.");
            GeneralInstallRoutes.CheckFolders(destination, after);
            var originalPaths=OriginalPaths(old,package);
            var next = new Record { Key = key, SourceSha256 = old.SourceSha256, InstallRoutes = after,
                Files = old.Files.Select(file => {
                    string original = file.ArchivePath ?? (before.Count==0?file.Path:originalPaths[file.Path]);
                    return new RecordFile { Path = GeneralInstallRoutes.Map(original, after), ArchivePath = original,
                        Sha256 = file.Sha256, Size = file.Size, Owned = file.Owned, Before = file.Before };
                }).ToList() };
            var others = new HashSet<string>(OtherFiles(receipt).Select(file => Target(file.Path)), StringComparer.OrdinalIgnoreCase);
            ApplyDestinations(receipt, next, old.Files.Select(file => Target(file.Path)).ToArray(), true,
                path => others.Contains(path) || shared != null && shared(path));
            return true;
        }
    }

    internal void InstallRoutedFixed(string key, FixedMod recipe, Dictionary<string,string> paths,
        List<GeneralInstallRoute> routes, DownloadCache cache, Action<string> report) {
        string receipt = RecordPath(key);
        using (AcquireLock()) {
            GeneralInstallRoutes.CheckFolders(destination, routes);
            var next = new Record { Key = key, InstallRoutes = routes, Files = recipe.Files.Select(file => new RecordFile {
                ArchivePath = file.Path, Path = paths[file.Path], Sha256 = file.Sha256, Size = file.Size, Owned = true
            }).ToList() };
            if (next.Files.All(file => Matches(Target(file.Path), file))) return;
            string stage = Safe.In(cache.Root, "preparacao/" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            try {
                using (cache.HoldPreparation(stage)) {
                    cache.Stage(recipe, stage, report);
                    ApplyDestinations(receipt, next, next.Files.Select(file => Safe.In(stage, file.ArchivePath)).ToArray(), false, null);
                }
            } finally { cache.CleanPreparation(stage); }
        }
    }

    void ApplyDestinations(string receipt, Record next, string[] sources, bool move, Func<string,bool> shared) {
        if(next.Files.Any(file=>!GeneralInstallRoutes.External(file.Path)&&!InstalledSnapshot.SafeGenericPath(file.Path)))
            throw new IOException("O caminho escolhido é reservado.");
        ValidatePaths(next.Files.Select(file => file.Path).ToArray());
        var changes = new List<int>();
        for (int i = 0; i < sources.Length; i++) {
            var file = next.Files[i];
            string target = Target(file.Path);
            if (!Matches(sources[i], file)) throw new IOException("Arquivo de origem ausente ou alterado: " + file.ArchivePath);
            if (String.Equals(sources[i], target, StringComparison.OrdinalIgnoreCase)) continue;
            if (File.Exists(target) || Directory.Exists(target)) {
                if (!move && Matches(target, file)) { file.Owned = false; continue; }
                throw Conflict(file.Path);
            }
            if (move && (!file.Owned || file.Before != null || shared(sources[i]) || shared(target)))
                throw new IOException("O arquivo pertence a outra instalação ou substitui um arquivo anterior: " + file.ArchivePath);
            changes.Add(i);
        }
        var copied = new List<int>();
        var deleted = new List<int>();
        var directories = new List<string>();
        try {
            foreach (int i in changes) {
                var file = next.Files[i];
                GeneralInstallRoutes.CreateParents(destination, file.Path, directories);
                InstalledSnapshotInstaller.ReplaceChecked(sources[i], Target(file.Path), file.Sha256, null);
                copied.Add(i);
            }
            if (move) foreach (int i in changes) {
                var file=next.Files[i];
                if(!Matches(Target(file.Path),file))throw Conflict(file.Path);
                InstalledSnapshotInstaller.DeleteVerified(sources[i], file.Sha256);
                deleted.Add(i);
            }
            Safe.WriteJson(receipt, next);
        } catch {
            foreach (int i in deleted.AsEnumerable().Reverse())
                TryCleanup(() => InstalledSnapshotInstaller.ReplaceChecked(Target(next.Files[i].Path), sources[i], next.Files[i].Sha256, null));
            foreach (int i in copied.AsEnumerable().Reverse()) {
                var file = next.Files[i];
                if (Matches(sources[i], file)) TryCleanup(() => InstalledSnapshotInstaller.DeleteVerified(Target(file.Path), file.Sha256));
            }
            foreach (string dir in directories.AsEnumerable().Reverse()) TryCleanup(() => DeleteEmpty(dir));
            throw;
        }
    }


    static Dictionary<string,string> OriginalPaths(Record record,string package) {
        if(record.InstallRoutes.Count==0||record.Files.All(file=>file.ArchivePath!=null))return null;
        if(package==null||!File.Exists(package)||record.SourceSha256==null)
            throw new IOException("O registro antigo precisa do pacote original para localizar os arquivos.");
        using(var reader=BundledArchiveReader.Open(package,new FileInfo(package).Length,record.SourceSha256)){
            var paths=reader.Files().ToDictionary(file=>GeneralInstallRoutes.Map(file.Path,record.InstallRoutes),file=>file.Path,StringComparer.OrdinalIgnoreCase);
            if(paths.Count!=record.Files.Count||record.Files.Any(file=>!paths.ContainsKey(file.Path)))
                throw new IOException("A árvore do pacote original difere do registro.");
            return paths;
        }
    }
}
