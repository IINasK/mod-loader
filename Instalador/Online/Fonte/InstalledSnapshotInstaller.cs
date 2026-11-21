using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

public sealed partial class InstalledSnapshotInstaller {
    public sealed class Receipt {
        public string Id, Root, Status, PackageHash;
        public List<string> SkippedDestinations = new List<string>();
        public bool Recoverable, ManualCapture;
        public InstalledSnapshotManifest Manifest;
        public List<string> ActiveOwners = new List<string>();
        public List<Change> Files = new List<Change>();
    }
    public sealed class Change {
        public string Path, Before, After;
        public string Expected;
        public long Size;
        public bool Owned, Applied, Reverted;
    }

    readonly string destination, appData, records;
    readonly FixedCatalog catalog;
    readonly bool fixture;

    public InstalledSnapshotInstaller(string destination, string appData, FixedCatalog catalog, bool fixture = false) {
        this.destination = Path.GetFullPath(destination).TrimEnd('\\');
        this.appData = Path.GetFullPath(appData).TrimEnd('\\');
        if (this.destination.Length <= 3 || this.destination.StartsWith(@"\\") || this.appData.StartsWith(@"\\"))
            throw new IOException("Escolha pastas locais para a instalação e os dados do programa.");
        if ((this.destination + "\\").StartsWith(this.appData + "\\", StringComparison.OrdinalIgnoreCase)
            || (this.appData + "\\").StartsWith(this.destination + "\\", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Os dados do programa precisam ficar separados da pasta de instalação.");
        Safe.NoLinks(this.destination); Safe.NoLinks(this.appData);
        if (!Directory.Exists(this.destination)) throw new IOException("Escolha uma pasta de instalação existente.");
        if (fixture && (!File.Exists(Safe.In(this.destination, ".fixture-only"))
            || !Regex.IsMatch(this.destination, @"(?i)[\\/]online-testes-[a-f0-9]{32}([\\/]|$)")))
            throw new IOException("Exceção de teste permitida apenas dentro de uma fixture isolada.");
        this.fixture = fixture;
        this.catalog = catalog;
        records = Safe.In(this.appData, "Snapshots/" + Fingerprint(this.destination.ToUpperInvariant()));
    }

    public string Install(string zipPath, string expectedHash, Action<string> report, int failAfter = -1, Func<ModVersionDifference,bool> chooseVersion = null, Action<string> transaction = null, Action checkpoint = null, Action<P2PInstallProgress> progress = null, bool allowReuse = false, Func<string[],bool> approveUserFolders = null,Func<int,bool?> chooseUpdates=null,bool requireExact=false,IEnumerable<OwnedChange> exactCleanup=null,InstalledSnapshotManifest exactPreview=null,bool receivingP2P=false) {
        // The archive is leased throughout verification and copying; no archive is kept in the receipt store.
        zipPath = Path.GetFullPath(zipPath); Safe.NoLinks(zipPath);
        using (var source = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read)) {
            var manifest = InstalledSnapshot.Read(source, expectedHash, catalog);
            if(receivingP2P&&manifest.TemplateId==GeneralProjectStore.CyberpunkTemplate&&(!requireExact||exactPreview==null))
                throw new IOException(GeneralStyle.T("O pacote pronto exige um novo convite com conferência de paridade.","The built-in pack needs a new invitation with parity verification."));
            if(exactPreview!=null)P2PService.VerifyExactContents(exactPreview,manifest);
            if(!allowReuse&&manifest.Files.Any(file=>file.ReuseLocal))throw new IOException("O recebimento não autorizou reutilizar arquivos locais.");
            using (AcquireLock(manifest.Game)) {
                if(manifest.Schema<2)ValidateEnvironment(manifest);
                else Closed(manifest.Game);
                var receipts = AllReceipts().ToList();
                var pending=receipts.Where(r=>r.Status=="preparing"||r.Status=="installing").ToList();
                if(pending.Count!=0){
                    if(pending.Count!=1||!pending[0].Recoverable||!(pending[0].PackageHash==null?SameManifest(pending[0].Manifest,manifest):pending[0].PackageHash==expectedHash))throw new IOException("Há outra instalação interrompida nesta pasta. Preserve os recibos e restaure a operação antes de continuar.");
                    if(requireExact)RequireAllDestinations(pending[0].SkippedDestinations);
                    if(manifest.Schema>=2)ValidateEnvironment(pending[0].Manifest);
                    if(transaction!=null)transaction(pending[0].Id);
                    if(pending[0].Manifest.Schema>=2)
                        GeneralInstallRoutes.CheckFolders(destination,pending[0].Manifest.List.Mods.SelectMany(mod=>mod.Variants.Single().InstallRoutes));
                    return Resume(pending[0],report,checkpoint,progress);
                }
                var skippedDestinations=InstalledSnapshot.SkipMissingDestinations(manifest,catalog,destination,approveUserFolders);
                if(manifest.Schema>=2){
                    skippedDestinations.AddRange(SkipIncompatibleMods(manifest,chooseVersion));
                    if(requireExact)RequireAllDestinations(skippedDestinations);
                    ValidateEnvironment(manifest);
                }
                var complete=receipts.FirstOrDefault(r=>r.Status=="installed"&&r.ActiveOwners.Count==WorkKeys(manifest).Count()&&SameManifest(r.Manifest,manifest)&&r.Files.All(Matches));
                if(complete!=null){
                    if(requireExact)RequireAllDestinations(complete.SkippedDestinations);
                    CleanStage(Safe.In(destination,".mod-loader-snapshot-"+complete.Id));return complete.Id;
                }
                var skipBackup=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var approvals=ConfirmVersions(manifest,chooseVersion,receipts,skippedDestinations,chooseUpdates,skipBackup,requireExact,exactCleanup);
                if(requireExact)RequireAllDestinations(skippedDestinations);
                var groups=manifest.Files.GroupBy(f=>f.Path,StringComparer.OrdinalIgnoreCase).ToArray();
                CheckDistinctTargets(groups.Select(group=>group.Key));
                CheckSpace(manifest);
                var ownership = receipts.Where(r => r.Status == "installed" && r.ActiveOwners.Count > 0)
                    .SelectMany(r => r.Files.Where(f => !f.Reverted && f.Applied && f.Owned).Select(f => new { Receipt = r, File = f }))
                    .ToLookup(x => x.File.Path, StringComparer.OrdinalIgnoreCase);
                var receipt = new Receipt {
                    Id = Guid.NewGuid().ToString("N"), Root = destination, Status = "preparing", Manifest = manifest, Recoverable=true,
                    ActiveOwners = WorkKeys(manifest).ToList(), PackageHash=manifest.Schema>=2?expectedHash:null, SkippedDestinations=skippedDestinations
                };
                string stage = Safe.In(destination, ".mod-loader-snapshot-" + receipt.Id);
                var directories = new List<string>();
                var writtenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                bool keepStage=false;
                if(transaction!=null)transaction(receipt.Id);
                Directory.CreateDirectory(stage);
                try {
                    var prepared=new Change[groups.Length];
                    if(progress!=null)progress(new P2PInstallProgress(false,0,groups.Length,null));
                    int preparedCount=0;
                    var prepareProgress=new object();
                    RunFileWorkers(groups.Length,false,checkpoint,
                        () => ZipFile.OpenRead(zipPath),
                        (zip,index) => {
                            var group=groups[index];
                            var change=PrepareChange(group,zip,manifest,stage,approvals);
                            if (change.Expected == change.After) {
                                var previous=ownership[change.Path].FirstOrDefault(x=>x.File.After==change.Expected);
                                if(previous!=null) {
                                    change.Owned=true;
                                    change.Before=previous.File.Before;
                                    if(change.Before!=null)Backup(BackupPath(previous.Receipt,change.Path),BackupPath(receipt,change.Path),change.Before);
                                }
                            } else if(change.Expected!=null) {
                                // Approved replacements retain a verified backup in addition to the transaction rollback file.
                                change.Owned=true;
                                if(skipBackup.Contains(change.Path))change.Before=null;
                                else Backup(Target(change.Path),BackupPath(receipt,change.Path),change.Expected);
                            }
                            prepared[index]=change;
                            lock(prepareProgress) {
                                Report(report,"Conferido: "+group.Key);
                                if(progress!=null)progress(new P2PInstallProgress(false,++preparedCount,groups.Length,group.Key));
                            }
                        });
                    receipt.Files.AddRange(prepared);
                    if(checkpoint!=null)checkpoint();
                    receipt.Status = "installing";
                    // The durable plan precedes all writes. Resume/discard also check actual file hashes.
                    Save(receipt);
                    try {
                        ApplyPrepared(receipt,stage,false,report,checkpoint,progress,directories,writtenPaths,failAfter);
                        receipt.Status = "installed"; Save(receipt);
                        return receipt.Id;
                    } catch(OperationCanceledException) {
                        keepStage=true;
                        throw;
                    } catch {
                        Rollback(receipt, writtenPaths, stage);
                        foreach (string directory in directories.AsEnumerable().Reverse()) TryCleanup(() => DeleteEmpty(directory));
                        throw;
                    }
                } finally { if(!keepStage)CleanStage(stage); }
            }
        }
    }

    static void RequireAllDestinations(IEnumerable<string> skipped){
        if(skipped!=null&&skipped.Any())throw new IOException(GeneralStyle.T("A sincronização exige estes mods: ","Synchronization requires these mods: ")+String.Join(", ",skipped));
    }

    void CheckDistinctTargets(IEnumerable<string> paths) {
        var targets=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var path in paths)
            if(!targets.Add(Target(path)))throw new IOException("O pacote aponta mais de um arquivo para o mesmo destino.");
        foreach(var target in targets)
            for(var parent=Path.GetDirectoryName(target);parent!=null;parent=Path.GetDirectoryName(parent))
                if(targets.Contains(parent))throw Conflict(parent);
    }

    Change PrepareChange(IGrouping<string,InstalledSnapshotFile> group,ZipArchive zip,InstalledSnapshotManifest manifest,string stage,Dictionary<string,string> approvals) {
        string target=Target(group.Key);
        if(Directory.Exists(target))throw Conflict(group.Key);
        string before=HashOrNull(target);
        string prepared=Safe.In(stage,Fingerprint(group.Key));
        var file=group.First();
        string after;
        if(file.ReuseLocal) {
            if(before!=file.Sha256||new FileInfo(target).Length!=file.Size)
                throw new IOException(GeneralStyle.T("Arquivo local mudou após a comparação. Peça um novo convite: ","A local file changed after comparison. Ask for a new invitation: ")+file.Path);
            using(var input=File.OpenRead(target))InstalledSnapshot.ValidateCustomData(manifest,file,input,catalog);
            after=before;
        } else if(file.Kind=="file") {
            ZipArchiveEntry entry=zip.GetEntry("files/"+file.Sha256);
            if(entry==null||entry.Length!=file.Size)throw new IOException("Arquivo ausente no pacote: "+file.Path);
            if(before!=null&&before!=file.Sha256) {
                string approved;
                if(!approvals.TryGetValue(file.Path,out approved)||approved!=before)throw Conflict(file.Path);
            }
            // Matching destinations have already been checked and do not need to be extracted again.
            if(before!=file.Sha256) {
                using(var input=entry.Open())
                using(var output=new FileStream(prepared,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                    CopyBounded(input,output,file.Size);
                if(Safe.Hash(prepared)!=file.Sha256)throw new IOException("Arquivo divergente no pacote: "+file.Path);
            }
            after=file.Sha256;
        } else {
            byte[] content=before!=null?InstalledSnapshotConfig.Read(target):null;
            foreach(var section in group.Where(section=>!section.Generated||before==null))
                content=InstalledSnapshotConfig.Apply(section,content);
            File.WriteAllBytes(prepared,content);
            after=InstalledSnapshot.Hash(content);
        }
        if(before!=null&&before!=after)Backup(target,Safe.In(stage,Fingerprint(group.Key)+".rollback"),before);
        long size=file.Kind=="file"?file.Size:new FileInfo(prepared).Length;
        return new Change { Path=group.Key, Before=before, Expected=before, After=after, Size=size, Owned=before==null&&!file.Generated };
    }

    void ApplyPrepared(Receipt receipt,string stage,bool resume,Action<string> report,Action checkpoint,
        Action<P2PInstallProgress> progress,List<string> directories,HashSet<string> writtenPaths,int failAfter=-1) {
        int written=0,processed=0,total=receipt.Files.Count;
        var state=new object();
        Closed(receipt.Manifest.Game);
        if(progress!=null)progress(new P2PInstallProgress(true,0,total,null));
        RunFileWorkers<object>(total,failAfter>=0,checkpoint,() => null,(ignored,index) => {
            var change=receipt.Files[index];
            string target=Target(change.Path);
            bool needsWrite=resume?HashOrNull(target)!=change.After:change.Expected!=change.After;
            if(needsWrite) {
                Closed(receipt.Manifest.Game);
                lock(state)CreateParents(change.Path,directories);
                string name=Fingerprint(change.Path);
                ReplaceChecked(Safe.In(stage,name),target,change.After,change.Expected,Safe.In(stage,"replace-"+name));
            } else if(!resume&&(Directory.Exists(target)||HashOrNull(target)!=change.Expected)) {
                throw new IOException("O destino mudou durante a preparação: "+change.Path);
            }
            lock(state) {
                if(needsWrite&&writtenPaths!=null)writtenPaths.Add(change.Path);
                change.Applied=true;
                if(needsWrite&&failAfter>=0&&++written>=failAfter)throw new IOException("Falha de teste após a cópia.");
                if(needsWrite||resume)Report(report,(resume?"Retomado: ":"Instalado: ")+change.Path);
                if(progress!=null)progress(new P2PInstallProgress(true,++processed,total,change.Path));
            }
        });
    }

    static void RunFileWorkers<T>(int count,bool serial,Action checkpoint,Func<T> open,Action<T,int> action) where T:class {
        if(count==0)return;
        var options=new ParallelOptions { MaxDegreeOfParallelism=serial?1:Math.Min(10,Environment.ProcessorCount) };
        try {
            Parallel.For(0,count,options,open,(index,state,worker) => {
                if(checkpoint!=null)checkpoint();
                action(worker,index);
                return worker;
            },worker => { var resource=worker as IDisposable;if(resource!=null)resource.Dispose(); });
        } catch(AggregateException error) {
            var failures=error.Flatten().InnerExceptions;
            ExceptionDispatchInfo.Capture(failures.FirstOrDefault(failure=>!(failure is OperationCanceledException))??failures[0]).Throw();
        }
    }

    List<string> SkipIncompatibleMods(InstalledSnapshotManifest manifest,Func<ModVersionDifference,bool> choose){
        var skipped=new HashSet<string>(StringComparer.Ordinal);
        if(manifest.Game!="cyberpunk2077"||choose==null)return new List<string>();
        if(!fixture)FixedData.ValidateGame(destination,new FixedMod[0],manifest.GameFileVersion);
        var entries=manifest.List.Mods.Where(mod=>mod.Variants.Single().CyberpunkId!=null).ToArray();
        var dependencies=entries.ToDictionary(mod=>mod.Id,mod=>new HashSet<string>(
            FixedData.Expand(catalog,new[]{mod.Variants.Single().CyberpunkId}).Select(item=>item.Id),StringComparer.Ordinal));
        var recipes=FixedData.Expand(catalog,entries.Select(mod=>mod.Variants.Single().CyberpunkId));
        foreach(var recipe in recipes){
            string conflict=FixedData.Incompatibility(recipe,destination);
            if(conflict==null)continue;
            var affected=entries.Where(mod=>dependencies[mod.Id].Contains(recipe.Id)).ToArray();
            if(recipe.Immutable||affected.All(mod=>skipped.Contains(mod.Id)))continue;
            var difference=new ModVersionDifference{Name=recipe.Name,Incoming=recipe.Version,
                Current=GeneralStyle.T("Arquivos extras na instalação local","Extra files in the local installation"),
                CanReplace=false,AllowSkip=true,Detail=conflict};
            if(difference.Skip(choose))skipped.UnionWith(affected.Select(mod=>mod.Id));
        }
        return InstalledSnapshot.SkipMods(manifest,catalog,skipped);
    }

    Dictionary<string,string> ConfirmVersions(InstalledSnapshotManifest manifest,Func<ModVersionDifference,bool> choose,
        List<Receipt> receipts,List<string> skippedMods,Func<int,bool?> chooseUpdates,HashSet<string> skipBackup,bool requireExact,IEnumerable<OwnedChange> exactCleanup){
        var ids=new HashSet<string>(manifest.List.Mods.Select(m=>m.Id),StringComparer.Ordinal);
        var paths=new HashSet<string>(manifest.Files.Where(f=>f.Kind=="file").Select(f=>f.Path),StringComparer.OrdinalIgnoreCase);
        if(requireExact&&exactCleanup!=null){
            foreach(var item in exactCleanup)
                if(FixedData.HashValid(item.Before)&&!IsImmutable(item.Path)&&HashOrNull(Target(item.Path))==item.Before)paths.Add(item.Path);
        }
        var obsolete=receipts.Where(r=>r.Status=="installed").SelectMany(r=>r.Manifest.Files
            .Where(f=>f.Kind=="file"&&!paths.Contains(f.Path)&&(File.Exists(Target(f.Path))||Directory.Exists(Target(f.Path))))
            .SelectMany(f=>f.Owners.Where(o=>r.ActiveOwners.Contains(o)).Select(o=>new{Id=o.Split('/')[0],f.Path})))
            .Where(item=>ids.Contains(item.Id)).ToLookup(item=>item.Id,item=>item.Path,StringComparer.Ordinal);
        var stock = manifest.List.Mods.SelectMany(mod => mod.Variants)
            .FirstOrDefault(variant => variant.CyberpunkId == EquipmentExOptions.ModId && variant.CyberpunkVariant == EquipmentExOptions.StockId);
        if (stock != null) {
            var equipment = catalog.Packages.Single(recipe => recipe.Id == EquipmentExOptions.ModId);
            var original = EquipmentExOptions.Resolve(equipment, EquipmentExOptions.StockId);
            var stockPaths = new HashSet<string>(original.Files.Select(file => file.Path), StringComparer.OrdinalIgnoreCase);
            if (equipment.Files.Any(file => !stockPaths.Contains(file.Path) && (File.Exists(Target(file.Path)) || Directory.Exists(Target(file.Path)))))
                throw new IOException(GeneralStyle.T("O pacote usa Equipment-EX sem WEAVE. Troque para Original no Mod Loader e tente novamente.",
                    "This pack uses Equipment-EX without WEAVE. Switch to Stock in Mod Loader and try again."));
        }
        var skipped=new HashSet<string>(StringComparer.Ordinal);
        foreach(var group in obsolete){
            var mod=manifest.List.Mods.Single(entry=>entry.Id==group.Key);
            var difference=new ModVersionDifference{Name=mod.Name,Current=GeneralStyle.T("Outra versão registrada","Another recorded version"),
                Incoming=manifest.Name,CanReplace=false,AllowSkip=manifest.Schema>=2&&choose!=null&&!group.Any(IsImmutable),
                Detail=GeneralStyle.T("A versão instalada tem arquivos extras que não estão no pacote. Remova a versão anterior pelo gerenciador para evitar misturar versões.",
                    "The installed version has extra files absent from the pack. Remove the previous version through the manager to avoid mixing versions.")
                    +"\r\n"+String.Join("\r\n",group.Distinct(StringComparer.OrdinalIgnoreCase).Take(6))};
            if(difference.AllowSkip){if(difference.Skip(choose))skipped.Add(mod.Id);}
            else difference.Require(choose);
        }
        var expected=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var changed=manifest.Files.Where(f=>f.Kind=="file"&&!f.ReuseLocal).GroupBy(f=>f.Path,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).Where(f=>{
            string hash=HashOrNull(Target(f.Path));
            if(hash==null||hash==f.Sha256)return false;
            expected.Add(f.Path,hash);return true;
        }).ToList();
        var approvedUpdates=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(chooseUpdates!=null&&manifest.Schema>=2&&changed.Count>0){
            var eligible=RecognizedVersionChanges(manifest,receipts,changed,expected,skipped,requireExact);
            if(eligible.Count>0){
                bool? backup=chooseUpdates(eligible.Count);
                if(!backup.HasValue)skipped.UnionWith(eligible);
                else foreach(var file in changed.Where(file=>file.Owners.All(owner=>eligible.Contains(owner.Split('/')[0])))){
                    approvedUpdates.Add(file.Path);
                    if(!backup.Value)skipBackup.Add(file.Path);
                }
            }
        }
        foreach(var group in changed.Where(file=>!approvedUpdates.Contains(file.Path)).GroupBy(f=>String.Join(", ",manifest.List.Mods.Where(m=>f.Owners.Any(owner=>owner.StartsWith(m.Id+"/",StringComparison.Ordinal))).Select(m=>m.Name)))){
            var files=group.ToList();var known=catalog.Packages.FirstOrDefault(m=>m.Files.Any(f=>files.Any(p=>p.Path.Equals(f.Path,StringComparison.OrdinalIgnoreCase))));
            var affected=new HashSet<string>(files.SelectMany(file=>file.Owners).Select(owner=>owner.Split('/')[0]),StringComparer.Ordinal);
            if(affected.All(skipped.Contains))continue;
            var difference=new ModVersionDifference{Name=group.Key,AllowSkip=manifest.Schema>=2&&choose!=null&&
                !files.Any(file=>IsImmutable(file.Path)),Current=String.Join("; ",files.Select(f=>ModVersionDifference.FileVersion(Target(f.Path))).Distinct()),Incoming=known==null?GeneralStyle.T("Conteúdo do pacote recebido","Received pack content"):known.Version,CanReplace=!files.Any(f=>IsImmutable(f.Path)&&!NightCoopRelease.AllowsUpgrade(f,expected[f.Path],catalog)),Detail=ModVersionDifference.ReplacementDetail(files.Select(f=>f.Path))};
            if(difference.AllowSkip){
                if(difference.Skip(choose))skipped.UnionWith(affected);
            }else difference.Require(choose);
        }
        skippedMods.AddRange(InstalledSnapshot.SkipMods(manifest,catalog,skipped));
        return expected;
    }

    HashSet<string> RecognizedVersionChanges(InstalledSnapshotManifest incoming,List<Receipt> receipts,
        List<InstalledSnapshotFile> changed,Dictionary<string,string> hashes,HashSet<string> skipped,bool allowDowngrade){
        var incomingPaths=new HashSet<string>(incoming.Files.Select(file=>file.Path),StringComparer.OrdinalIgnoreCase);
        var conflicts=changed.SelectMany(file=>file.Owners.Select(owner=>new{Id=owner.Split('/')[0],File=file}))
            .ToLookup(entry=>entry.Id,entry=>entry.File,StringComparer.Ordinal);
        var installed=receipts.Where(receipt=>receipt.Status=="installed").SelectMany(receipt=>receipt.Manifest.List.Mods
            .Where(mod=>receipt.ActiveOwners.Contains(InstalledSnapshot.Owner(mod,mod.Variants.Single())))
            .Select(mod=>new{Receipt=receipt,Mod=mod})).ToLookup(row=>row.Mod.Id,StringComparer.Ordinal);
        GeneralModState local=null;
        try{local=new GeneralModLibrary(Safe.In(appData,"lista.json")).LoadForInspection(true);}
        catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}catch(InvalidOperationException){}
        var engine=new GeneralModInstaller(destination,Safe.In(appData,"Instalacoes"));
        var eligible=new HashSet<string>(StringComparer.Ordinal);
        foreach(var mod in incoming.List.Mods){
            var files=conflicts[mod.Id].ToArray();
            if(skipped.Contains(mod.Id)||files.Length==0||files.Any(file=>IsImmutable(file.Path)))continue;
            var next=mod.Variants.Single();
            var recipe=catalog.Packages.FirstOrDefault(item=>item.Id==next.CyberpunkId&&item.PreviousRelease!=null);
            if(recipe!=null&&ModVersionNumbers.Compare(recipe.Version,recipe.PreviousRelease.Version)>0&&
                files.All(file=>recipe.Files.Any(known=>String.Equals(known.Path,file.Path,StringComparison.OrdinalIgnoreCase)&&
                    known.Sha256==file.Sha256&&known.PreviousSha256==hashes[file.Path]))){
                eligible.Add(mod.Id);continue;
            }
            if(installed[mod.Id].Any(row=>{
                var previous=row.Mod.Variants.Single();
                string owner=InstalledSnapshot.Owner(row.Mod,previous);
                var previousPaths=new HashSet<string>(row.Receipt.Manifest.Files.Where(file=>file.Kind=="file"&&file.Owners.Contains(owner)).Select(file=>file.Path),StringComparer.OrdinalIgnoreCase);
                return RecognizedVersionChange(previous,next,allowDowngrade)&&files.All(file=>previousPaths.Contains(file.Path)&&
                    row.Receipt.Files.Any(old=>old.Applied&&!old.Reverted&&String.Equals(old.Path,file.Path,StringComparison.OrdinalIgnoreCase)&&old.After==hashes[file.Path]));
            })){
                eligible.Add(mod.Id);continue;
            }
            var oldMod=local==null?null:local.List.Mods.FirstOrDefault(entry=>entry.Id==mod.Id);
            var oldVariant=oldMod==null?null:oldMod.Variants.FirstOrDefault(option=>option.Id==next.Id);
            if(oldVariant==null||!RecognizedVersionChange(oldVariant,next,allowDowngrade))continue;
            GeneralModInstaller.Record record;
            try{record=engine.ReadPackageRecord(P2PPack.Slot(oldMod,oldVariant,0));}
            catch(IOException){continue;}catch(UnauthorizedAccessException){continue;}
            if(record!=null&&record.Files.All(file=>incomingPaths.Contains(file.Path)||!File.Exists(Target(file.Path)))&&
                files.All(file=>record.Files.Any(old=>old.Owned&&String.Equals(old.Path,file.Path,StringComparison.OrdinalIgnoreCase)&&old.Sha256==hashes[file.Path])))
                eligible.Add(mod.Id);
        }
        // A shared path is eligible only if every affected mod has proof of an older version.
        bool reduced;
        do{
            int count=eligible.Count;
            foreach(var file in changed){
                var owners=file.Owners.Select(owner=>owner.Split('/')[0]).ToArray();
                if(owners.Any(owner=>!eligible.Contains(owner)))eligible.ExceptWith(owners);
            }
            reduced=eligible.Count!=count;
        }while(reduced);
        return eligible;
    }

    static bool RecognizedVersionChange(GeneralModVariant previous,GeneralModVariant next,bool allowDowngrade){
        if(previous.Id!=next.Id||previous.Sources.Count!=1||next.Sources.Count!=1)return false;
        string from=previous.Sources[0],to=next.Sources[0];
        if(from==to)return false;
        var oldNexus=GeneralNexusLink.Parse(from);var newNexus=GeneralNexusLink.Parse(to);
        var oldGithub=GeneralGithubLink.Parse(from);var newGithub=GeneralGithubLink.Parse(to);
        if(oldNexus!=null&&newNexus!=null&&oldNexus.Game==newNexus.Game&&oldNexus.Mod==newNexus.Mod&&
            oldNexus.File>0&&newNexus.File>0){
            GeneralSourceInfo before,after;
            return previous.SourceInfo.TryGetValue(from,out before)&&next.SourceInfo.TryGetValue(to,out after)&&
                before.NexusChecked&&after.NexusChecked&&(ModVersionNumbers.Compare(after.Version,before.Version)>0||
                    allowDowngrade&&ModVersionNumbers.Compare(before.Version,after.Version)>0);
        }
        return GeneralGithubLink.SameRepository(oldGithub,newGithub)&&oldGithub.Asset!=null&&newGithub.Asset!=null&&
            GeneralGithubUpdatePackage.SameAssetLine(oldGithub,newGithub)&&(ModVersionNumbers.Compare(newGithub.Tag,oldGithub.Tag)>0||
                allowDowngrade&&ModVersionNumbers.Compare(oldGithub.Tag,newGithub.Tag)>0);
    }

    internal IEnumerable<SnapshotOutputScope> ReadOutputScopes(){
        foreach(var receipt in AllReceipts(true,true).Where(record=>record.Status=="installed"))
            foreach(var scope in receipt.Manifest.OutputScopes.Where(item=>receipt.ActiveOwners.Contains(item.Owner)))
                yield return scope;
    }

    internal IEnumerable<ModOutputInstallation> OutputRecoveryCandidates(){
        foreach(var receipt in AllReceipts(true,true).Where(record=>record.Status=="installed"))
            foreach(var entry in receipt.Manifest.List.Mods)
                if(receipt.ActiveOwners.Contains(InstalledSnapshot.Owner(entry,entry.Variants.Single())))
                    yield return new ModOutputInstallation{Entry=entry,Variant=entry.DefaultVariant,SnapshotId=receipt.Id};
    }

    public List<InstalledSnapshotFile> ReadInstalledFiles(string receiptId, string workKey, bool includeConfigs=true, bool configsOnly=false,Action checkpoint=null) {
        var receipt = Read(receiptId);
        if (receipt == null || receipt.Status != "installed" || !receipt.ActiveOwners.Contains(workKey))
            throw new IOException("O mod não tem recibo de snapshot instalado nesta pasta.");
        return ReadInstalledFiles(receipt,workKey,includeConfigs,configsOnly,checkpoint);
    }

    internal List<InstalledSnapshotFile> ReadMatchingInstalledFiles(string receiptId,GeneralModEntry mod,GeneralModVariant variant,
        bool includeConfigs=true,bool configsOnly=false,Action checkpoint=null){
        var receipt=Read(receiptId);
        string owner=InstalledSnapshot.Owner(mod,variant);
        if(receipt==null||receipt.Status!="installed"||!receipt.ActiveOwners.Contains(owner)||
            !InstalledSnapshot.MatchesReceiptVariant(receipt.Manifest,mod,variant))return null;
        return ReadInstalledFiles(receipt,owner,includeConfigs,configsOnly,checkpoint);
    }

    List<InstalledSnapshotFile> ReadInstalledFiles(Receipt receipt,string workKey,bool includeConfigs,bool configsOnly,Action checkpoint) {
        var files = receipt.Manifest.Files.Where(f => !f.Generated&&f.Owners.Contains(workKey)&&
            (f.Kind=="file"?!configsOnly:includeConfigs)).ToList();
        return SnapshotVerification.Map(files,(file,poll)=>{
            var change = receipt.Files.Single(f => f.Path.Equals(file.Path, StringComparison.OrdinalIgnoreCase));
            if (file.Kind == "file") {
                if (!SnapshotVerification.Matches(Target(change.Path),change.Size,change.After,poll)) throw new IOException("O arquivo instalado está ausente ou alterado: " + file.Path);
                return file;
            } else {
                string target = Target(file.Path);
                if (!File.Exists(target) || new FileInfo(target).Length > 1048576) throw new IOException("Configuração instalada ausente ou excessiva: " + file.Path);
                byte[] current = InstalledSnapshotConfig.Read(target);
                string content = file.Kind == "json" ? InstalledSnapshotConfig.Json(current) : InstalledSnapshotConfig.Section(current, file.Section);
                if (content == null) throw new IOException("Seção de configuração instalada ausente: " + file.Section);
                byte[] bytes = Encoding.UTF8.GetBytes(content);
                return new InstalledSnapshotFile { Path = file.Path, Kind = file.Kind, Section = file.Section,
                    Content = content, Size = bytes.LongLength, Sha256 = InstalledSnapshot.Hash(bytes), Owners = new List<string>(file.Owners) };
            }
        },checkpoint).ToList();
    }
    string Resume(Receipt receipt,Action<string> report,Action checkpoint,Action<P2PInstallProgress> progress){
        if(receipt.Files.Count!=receipt.Manifest.Files.Select(f=>f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count())throw new IOException("Preparação interrompida incompleta. Preserve o pacote e o recibo.");
        string stage=Safe.In(destination,".mod-loader-snapshot-"+receipt.Id);Safe.NoLinks(stage);
        CheckDistinctTargets(receipt.Files.Select(file=>file.Path));
        RunFileWorkers<object>(receipt.Files.Count,false,checkpoint,() => null,(ignored,index) => {
            var file=receipt.Files[index];
            string current=HashOrNull(Target(file.Path));
            if(current==file.After)return;
            string prepared=Safe.In(stage,Fingerprint(file.Path));
            if(current!=file.Expected||!File.Exists(prepared)||new FileInfo(prepared).Length!=file.Size||Safe.Hash(prepared)!=file.After)
                throw new IOException("Não foi possível retomar: arquivo alterado ou preparação ausente em "+file.Path+". Sua alteração foi preservada.");
        });
        receipt.Status="installing";Save(receipt);
        ApplyPrepared(receipt,stage,true,report,checkpoint,progress,new List<string>(),null);
        receipt.Status="installed";Save(receipt);CleanStage(stage);
        return receipt.Id;
    }

    internal string DiscardInterrupted(string transaction,string zip,string expectedHash) {
        Receipt receipt;
        if(transaction!=null){
            receipt=Read(transaction);
            if(receipt==null){
                CleanStage(Safe.In(destination,".mod-loader-snapshot-"+transaction));
                return null;
            }
        }else{
            if(expectedHash==null||!File.Exists(zip))return null;
            var pending=AllReceipts().Where(r=>r.Recoverable&&(r.Status=="preparing"||r.Status=="installing")).ToArray();
            if(pending.Length==0)return null;
            var manifest=InstalledSnapshot.Read(zip,expectedHash,catalog);
            receipt=pending.SingleOrDefault(r=>r.PackageHash==null?SameManifest(r.Manifest,manifest):r.PackageHash==expectedHash);
            if(receipt==null)return null;
        }
        if(receipt.Status=="installed")return receipt.Id;
        if(receipt.Status!="preparing"&&receipt.Status!="installing")return null;
        using(AcquireLock(receipt.Manifest.Game)){
            Closed(receipt.Manifest.Game);
            string stage=Safe.In(destination,".mod-loader-snapshot-"+receipt.Id);
            var written=new HashSet<string>(receipt.Files.Where(f=>f.Expected!=f.After&&
                (f.Applied||HashOrNull(Target(f.Path))==f.After)).Select(f=>f.Path),StringComparer.OrdinalIgnoreCase);
            Rollback(receipt,written,stage);
            CleanStage(stage);
        }
        return null;
    }

    internal InstalledSnapshotManifest ReadManifest(string receiptId) {
        var receipt = Read(receiptId);
        if (receipt == null || receipt.Status != "installed") throw new IOException("Recibo de snapshot instalado ausente.");
        return receipt.Manifest;
    }

    public string Status(string receiptId, string workKey) {
        return CheckStatus(IndexStatus(Read(receiptId)),workKey,Safe.Hash);
    }

    internal sealed class StatusScan {
        readonly InstalledSnapshotInstaller installer;
        readonly Func<string,string> readHash;
        readonly ConcurrentDictionary<string,Lazy<Receipt>> receipts =
            new ConcurrentDictionary<string,Lazy<Receipt>>(StringComparer.Ordinal);
        readonly ConcurrentDictionary<string,Lazy<ILookup<string,StatusFile>>> indexes =
            new ConcurrentDictionary<string,Lazy<ILookup<string,StatusFile>>>(StringComparer.Ordinal);

        internal StatusScan(InstalledSnapshotInstaller installer,Func<string,string> readHash) {
            this.installer=installer;
            this.readHash=readHash;
        }

        Receipt Read(string id){
            return receipts.GetOrAdd(id,key=>new Lazy<Receipt>(()=>installer.ReadCore(key,true))).Value;
        }

        internal bool Matches(string receiptId,GeneralModEntry mod,GeneralModVariant variant){
            var receipt=Read(receiptId);
            return receipt!=null&&receipt.Status=="installed"&&InstalledSnapshot.MatchesReceiptVariant(receipt.Manifest,mod,variant);
        }

        internal string Status(string receiptId,string workKey) {
            var index=indexes.GetOrAdd(receiptId,id=>new Lazy<ILookup<string,StatusFile>>(()=>IndexStatus(Read(id)))).Value;
            return installer.CheckStatus(index,workKey,readHash);
        }
    }

    sealed class StatusFile {
        internal Change File;
        internal bool Code;
    }

    static ILookup<string,StatusFile> IndexStatus(Receipt receipt) {
        var owners=new List<KeyValuePair<string,StatusFile>>();
        if(receipt!=null&&receipt.Status=="installed"){
            var active=new HashSet<string>(receipt.ActiveOwners,StringComparer.Ordinal);
            var paths=receipt.Manifest.Files.Where(file=>!file.Generated).ToLookup(file=>file.Path,StringComparer.OrdinalIgnoreCase);
            foreach(var file in receipt.Files){
                var entries=paths[file.Path];
                var check=new StatusFile{File=file,Code=entries.Any(entry=>entry.Kind=="file")};
                foreach(string owner in entries.SelectMany(entry=>entry.Owners).Where(active.Contains).Distinct(StringComparer.Ordinal))
                    owners.Add(new KeyValuePair<string,StatusFile>(owner,check));
            }
        }
        return owners.ToLookup(pair=>pair.Key,pair=>pair.Value,StringComparer.Ordinal);
    }

    string CheckStatus(ILookup<string,StatusFile> index,string owner,Func<string,string> readHash) {
        int present=0,expected=0;
        foreach(var check in index[owner]){
            expected++;
            var file=check.File;
            string target=Target(file.Path);
            if(Directory.Exists(target))return GeneralModInstaller.Modified;
            if(!File.Exists(target))continue;
            if(check.Code&&(new FileInfo(target).Length!=file.Size||readHash(target)!=file.After))return GeneralModInstaller.Modified;
            present++;
        }
        return present==0?GeneralModInstaller.Missing:present==expected?GeneralModInstaller.Installed:GeneralModInstaller.Partial;
    }

    internal bool Relocate(string receiptId, string workKey, List<GeneralInstallRoute> before,
        List<GeneralInstallRoute> after, Func<string,bool> shared) {
        var initial = Read(receiptId);
        if (initial == null) return false;
        using (AcquireLock(initial.Manifest.Game)) {
            var receipt = Read(receiptId);
            Closed(receipt.Manifest.Game);
            if (receipt.Status != "installed" || !receipt.ActiveOwners.Contains(workKey))
                throw new IOException("O mod não tem instalação ativa neste recibo.");
            var receipts = AllReceipts().ToList();
            if (receipts.Any(other => other.Status == "preparing" || other.Status == "installing"))
                throw new IOException("Conclua a instalação interrompida antes de mover arquivos.");

            var manifest = receipt.Manifest;
            var mod = manifest.List.Mods.Single(item => InstalledSnapshot.Owner(item, item.Variants.Single()) == workKey);
            var variant = mod.Variants.Single();
            if (variant.CyberpunkId == null)
                throw new IOException("Este recibo não contém os caminhos originais. Reimporte o pacote original para alterar o destino.");
            if (!GeneralInstallRoutes.Same(before, variant.InstallRoutes))
                throw new IOException("O destino atual difere do recibo da instalação.");
            var localCatalog = HistoricalReceipts.CatalogForSnapshot(catalog, manifest);
            var recipe = InstalledSnapshot.FixedRecipe(localCatalog, variant);
            var current = GeneralInstallRoutes.FixedPaths(localCatalog, recipe, before);
            var next = GeneralInstallRoutes.FixedPaths(localCatalog, recipe, after);
            GeneralInstallRoutes.CheckFolders(destination, after,allowNewGameSubfolders:true);
            var existing = new HashSet<string>(receipts.Where(other => other.Id != receiptId && other.Status == "installed")
                .SelectMany(other => other.Manifest.Files.Where(file => file.Owners.Any(other.ActiveOwners.Contains)))
                .Select(file => Target(file.Path)), StringComparer.OrdinalIgnoreCase);
            var changes = new List<SnapshotMove>();
            foreach (var file in recipe.Files.Where(file => !file.UserData)) {
                string oldPath = current[file.Path], newPath = next[file.Path];
                var change = receipt.Files.Single(item => item.Path.Equals(oldPath, StringComparison.OrdinalIgnoreCase));
                if (!change.Applied || change.Reverted || !Matches(change))
                    throw new IOException("Arquivo de origem ausente ou alterado: " + oldPath);
                if (oldPath.Equals(newPath, StringComparison.OrdinalIgnoreCase)) continue;
                var payload = manifest.Files.Single(item => item.Kind == "file" && item.Path.Equals(oldPath, StringComparison.OrdinalIgnoreCase));
                string source = Target(oldPath), target = Target(newPath);
                if (!change.Owned || change.Before != null || payload.Owners.Count != 1 ||
                    existing.Contains(source) || existing.Contains(target) || shared != null && (shared(source) || shared(target)))
                    throw new IOException("O arquivo é compartilhado, preexistente ou substitui um arquivo anterior: " + oldPath);
                if (File.Exists(target) || Directory.Exists(target) || changes.Any(item => item.Target.Equals(target, StringComparison.OrdinalIgnoreCase)))
                    throw Conflict(newPath);
                changes.Add(new SnapshotMove { Source = source, Target = target, Path = newPath, Hash = change.After });
                payload.Path = newPath;
                change.Path = newPath;
            }
            if (manifest.Project == null)
                manifest.Project = Safe.Json.Deserialize<GeneralModList>(Safe.Json.Serialize(manifest.List));
            manifest.Schema = 2;
            manifest.Project.Mods.Single(item => item.Id == mod.Id).Variants.Single(item => item.Id == variant.Id).InstallRoutes = after;
            variant.InstallRoutes = after;
            receipt.PackageHash = null;
            InstalledSnapshot.Validate(manifest, localCatalog, true);

            var directories = new List<string>();
            var copied = new List<SnapshotMove>();
            var removed = new List<SnapshotMove>();
            try {
                foreach (var move in changes) {
                    Closed(manifest.Game);
                    CreateParents(move.Path, directories);
                    ReplaceChecked(move.Source, move.Target, move.Hash, null);
                    copied.Add(move);
                }
                foreach (var move in changes) {
                    Closed(manifest.Game);
                    if (HashOrNull(move.Target) != move.Hash) throw Conflict(move.Path);
                    DeleteChecked(move.Source, move.Hash);
                    removed.Add(move);
                }
                Save(receipt);
            } catch {
                foreach (var move in removed.AsEnumerable().Reverse())
                    TryCleanup(() => ReplaceChecked(move.Target, move.Source, move.Hash, null));
                foreach (var move in copied.AsEnumerable().Reverse())
                    TryCleanup(() => { if (HashOrNull(move.Source) == move.Hash) DeleteChecked(move.Target, move.Hash); });
                foreach (string directory in directories.AsEnumerable().Reverse())
                    TryCleanup(() => DeleteEmpty(directory));
                throw;
            }
            return true;
        }
    }

    sealed class SnapshotMove {
        internal string Source, Target, Path, Hash;
    }

    public string Remove(string receiptId, IEnumerable<string> workKeys, Action<string> report, bool forgetOnly=false, Func<string,bool> preserveExternal=null,Func<string,bool> preserveAttached=null) {
        var initial = Read(receiptId);
        if (initial == null) return "Nenhum snapshot instalado para remover.";
        using (AcquireLock(initial.Manifest.Game)) {
            var receipt = Read(receiptId); Closed(receipt.Manifest.Game);
            var removing = new HashSet<string>(workKeys ?? new string[0], StringComparer.Ordinal);
            if (removing.Any(k => !WorkKeys(receipt.Manifest).Contains(k))) throw new IOException("Mod fora do recibo de instalação.");
            removing.IntersectWith(receipt.ActiveOwners);
            var others = AllReceipts().Where(r => r.Id != receipt.Id && r.Status == "installed" && r.ActiveOwners.Count > 0).ToList();
            if(!forgetOnly)EnsureDependencies(receipt, removing, others);
            var remaining = new HashSet<string>(receipt.ActiveOwners.Where(k => !removing.Contains(k)), StringComparer.Ordinal);
            var pathOwners=receipt.Manifest.Files.ToLookup(file=>file.Path,file=>file.Owners,StringComparer.OrdinalIgnoreCase);
            var otherFiles=others.SelectMany(other=>{
                var activePaths=new HashSet<string>(other.Manifest.Files.Where(file=>file.Owners.Any(other.ActiveOwners.Contains))
                    .Select(file=>file.Path),StringComparer.OrdinalIgnoreCase);
                return other.Files.Where(file=>file.Applied&&!file.Reverted&&activePaths.Contains(file.Path));
            }).ToLookup(file=>file.Path,StringComparer.OrdinalIgnoreCase);
            if(!forgetOnly&&receipt.Files.Any(change=>change.Applied&&!change.Reverted&&
                pathOwners[change.Path].SelectMany(keys=>keys).Any(removing.Contains)&&
                !pathOwners[change.Path].SelectMany(keys=>keys).Any(remaining.Contains)&&
                otherFiles[change.Path].Any(other=>other.After!=change.After&&other.Before==change.After)&&
                !otherFiles[change.Path].Any(other=>other.After==change.After)))
                throw new IOException("Outro mod substituiu arquivos deste pacote. Remova a versão ativa antes de remover o pacote anterior.");
            int removed = 0, preserved = 0;
            foreach (var change in forgetOnly ? Enumerable.Empty<Change>() : receipt.Files.AsEnumerable().Reverse()) {
                var owners = pathOwners[change.Path].SelectMany(keys=>keys).Distinct().ToList();
                if (!owners.Any(removing.Contains)) continue;
                if (owners.Any(remaining.Contains) || receipt.Manifest.Game == "cyberpunk2077" && IsImmutable(change.Path)
                    || otherFiles[change.Path].Any(other=>other.After==change.After)||preserveAttached!=null&&preserveAttached(change.Path)) {
                    preserved++; continue;
                }
                if (!change.Owned || change.Reverted || !change.Applied || !Matches(change) || GeneralInstallRoutes.External(change.Path)&&preserveExternal!=null&&preserveExternal(change.Path)) { preserved++; continue; }
                Closed(receipt.Manifest.Game);
                if (change.Before == null) DeleteChecked(Target(change.Path), change.After);
                else ReplaceChecked(VerifiedBackup(receipt, change), Target(change.Path), change.Before, change.After);
                change.Reverted = true; Save(receipt); removed++;
                Report(report, "Removido: " + change.Path);
            }
            receipt.ActiveOwners = remaining.ToList(); Save(receipt);
            return removed + " arquivo(s) removido(s). " + preserved + " arquivo(s) preexistente(s), compartilhado(s), da base ou alterado(s) preservado(s).";
        }
    }

    void ValidateEnvironment(InstalledSnapshotManifest manifest) {
        Closed(manifest.Game);
        if (manifest.Game != "cyberpunk2077") {
            if (File.Exists(Safe.In(destination, "bin/x64/Cyberpunk2077.exe"))) throw new IOException("Um snapshot genérico precisa de outro destino; esta pasta pertence ao Cyberpunk 2077.");
            return;
        }
        var ids = new HashSet<string>(manifest.List.Mods.SelectMany(m => m.Variants).Select(v => v.CyberpunkId).Where(s => !String.IsNullOrEmpty(s)), StringComparer.Ordinal);
        var mods = FixedData.Expand(catalog, ids);
        FixedData.RequireInstallable(mods);
        if (manifest.GameFileVersion != catalog.GameFileVersion) throw new IOException("O snapshot foi preparado para outra versão do jogo.");
        if (!fixture) FixedData.ValidateGame(destination, mods, manifest.GameFileVersion);
        else foreach (var mod in mods) { string conflict = FixedData.Incompatibility(mod, destination); if (conflict != null) throw new IOException(conflict); }
        foreach (var file in manifest.Files.Where(f => IsImmutable(f.Path))) {
            var known = catalog.Packages.Where(m => m.Immutable).SelectMany(m => m.Files).Single(f => f.Path.Equals(file.Path, StringComparison.OrdinalIgnoreCase));
            if (file.Kind != "file" || file.Sha256 != known.Sha256 || file.Size != known.Size) throw new IOException("A base NightCoop/RED4ext precisa corresponder ao catálogo original.");
        }
    }

    void CheckSpace(InstalledSnapshotManifest manifest) {
        long staged = 0, copies = 0, backups = 0;
        foreach (var group in manifest.Files.GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)) {
            var file = group.First();
            if(file.ReuseLocal)continue;
            string path = Target(file.Path);
            if (file.Kind == "file") {
                staged = checked(staged + file.Size);
                if (!File.Exists(path)) copies = checked(copies + file.Size);
                else if(HashOrNull(path)!=file.Sha256){long size=new FileInfo(path).Length;backups=checked(backups+size);staged=checked(staged+size);copies=checked(copies+file.Size);}
            } else {
                staged = checked(staged + 3L * 1048576);
                copies = checked(copies + 2L * 1048576);
                if (File.Exists(path)) {
                    long size = new FileInfo(path).Length;
                    if (size > 1048576) throw new IOException("Configuração local excessiva: " + file.Path);
                    backups = checked(backups + size);
                }
            }
        }
        string targetDrive = Path.GetPathRoot(destination), dataDrive = Path.GetPathRoot(records);
        long targetNeeded = checked(staged + copies + 100L * 1048576);
        long dataNeeded = checked(backups + 32L * 1048576);
        if (String.Equals(targetDrive, dataDrive, StringComparison.OrdinalIgnoreCase)) targetNeeded = checked(targetNeeded + dataNeeded);
        if (new DriveInfo(targetDrive).AvailableFreeSpace < targetNeeded || new DriveInfo(dataDrive).AvailableFreeSpace < dataNeeded)
            throw new IOException("Espaço insuficiente para preparar os arquivos e preservar as configurações anteriores.");
    }

    void EnsureDependencies(Receipt receipt, HashSet<string> removing, List<Receipt> others) {
        if (receipt.Manifest.Game != "cyberpunk2077") return;
        var removedIds = new HashSet<string>(receipt.Manifest.List.Mods.SelectMany(m => m.Variants.Where(v => removing.Contains(m.Id + "/" + v.Id))).Select(v => v.CyberpunkId).Where(s => !String.IsNullOrEmpty(s)), StringComparer.Ordinal);
        foreach (var r in others.Concat(new[] { receipt }))
            foreach (var mod in r.Manifest.List.Mods)
                foreach (var v in mod.Variants)
                    if (r.ActiveOwners.Contains(mod.Id + "/" + v.Id) && (r.Id != receipt.Id || !removing.Contains(mod.Id + "/" + v.Id))) removedIds.Remove(v.CyberpunkId ?? "");
        removedIds.RemoveWhere(id => catalog.Packages.Any(m => m.Id == id && m.Immutable));
        foreach (var mod in catalog.Packages.Where(m => !removedIds.Contains(m.Id))) {
            if (!FixedData.Expand(catalog, new[] { mod.Id }).Any(m => removedIds.Contains(m.Id))) continue;
            var scan = FixedData.Scan(mod, destination, false);
            if (scan.Correct + scan.Original + scan.Different > 0 || scan.State == "Não verificável")
                throw new IOException("Remova também o mod que depende desta seleção: " + mod.Name + ". Nenhum arquivo foi removido.");
        }
    }

    bool IsImmutable(string path) { return catalog != null && catalog.Packages.Any(m => m.Immutable && m.Files.Any(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase))); }
    static bool SameManifest(InstalledSnapshotManifest left,InstalledSnapshotManifest right){
        string first=Safe.Json.Serialize(left),second=Safe.Json.Serialize(right);
        if(first==second)return true;
        return SameJson(Safe.Json.DeserializeObject(first),Safe.Json.DeserializeObject(second));
    }
    static bool SameJson(object left,object right){
        var fields=left as IDictionary<string,object>;
        if(fields!=null){
            var other=right as IDictionary<string,object>;
            return other!=null&&fields.Count==other.Count&&fields.All(field=>{
                object value;
                return other.TryGetValue(field.Key,out value)&&SameJson(field.Value,value);
            });
        }
        var items=left as object[];
        if(items!=null){
            var other=right as object[];
            return other!=null&&items.Length==other.Length&&items.Zip(other,SameJson).All(equal=>equal);
        }
        return Object.Equals(left,right);
    }
    static IEnumerable<string> WorkKeys(InstalledSnapshotManifest manifest) { return manifest.List.Mods.SelectMany(m => m.Variants.Select(v => m.Id + "/" + v.Id)); }
    void Closed(string game) {
        if (fixture || game != "cyberpunk2077") return;
        foreach (var process in Process.GetProcessesByName("Cyberpunk2077")) { process.Dispose(); throw new IOException("Feche o jogo antes de instalar ou remover mods."); }
    }
    FileStream AcquireLock(string game) {
        string folder = game == "cyberpunk2077" ? Safe.In(destination, ".CoopModsOnline")
            : Safe.In(appData, "Instalacoes/" + Fingerprint((destination + "\\").ToUpperInvariant()));
        Directory.CreateDirectory(folder); Safe.NoLinks(folder);
        return new FileStream(Safe.In(folder, game == "cyberpunk2077" ? "operacao.lock" : "operations.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    string ReceiptPath(string id) {
        if (!Regex.IsMatch(id ?? "", "^[a-f0-9]{32}$")) throw new IOException("Identificador de snapshot inválido.");
        return Safe.In(records, id + ".json");
    }
    string BackupPath(Receipt receipt, string path) { return Safe.In(records, "backups/" + receipt.Id + "/" + Fingerprint(path) + ".bin"); }
    string VerifiedBackup(Receipt receipt, Change change) {
        string backup = BackupPath(receipt, change.Path);
        if (!File.Exists(backup) || Safe.Hash(backup) != change.Before) throw new IOException("O backup local não confere; o arquivo atual foi preservado.");
        return backup;
    }
    void Save(Receipt receipt) {
        byte[] content = Encoding.UTF8.GetBytes(Safe.Json.Serialize(receipt));
        Safe.WriteJson(ReceiptPath(receipt.Id), new ReceiptEnvelope { Protected = Convert.ToBase64String(ProtectedData.Protect(content, null, DataProtectionScope.CurrentUser)) });
    }
    Receipt Read(string id){return ReadCore(id,false);}
    Receipt ReadCore(string id,bool inspection) {
        string path = ReceiptPath(id);
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 24000000) throw new IOException("Recibo de snapshot excessivo.");
        Receipt receipt;
        try {
            ReceiptEnvelope envelope;
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, inspection?FileShare.ReadWrite|FileShare.Delete:FileShare.Read))
                envelope = Safe.Json.Deserialize<ReceiptEnvelope>(Encoding.UTF8.GetString(InstalledSnapshot.ReadBytes(input, 24000000)));
            receipt = Safe.Json.Deserialize<Receipt>(Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(envelope.Protected), null, DataProtectionScope.CurrentUser)));
        } catch (Exception ex) {
            if (ex is OutOfMemoryException) throw;
            throw new IOException("Recibo de snapshot alterado, corrompido ou criado por outra conta do Windows.", ex);
        }
        if (receipt == null || receipt.Id != id || !String.Equals(receipt.Root, destination, StringComparison.OrdinalIgnoreCase)
            || receipt.Files == null || receipt.Files.Count > 20000 || receipt.ActiveOwners == null
            || !new[] { "preparing", "installing", "installed", "rolledback", "rollback-preserved" }.Contains(receipt.Status)) throw new IOException("Recibo de snapshot inválido.");
        InstalledSnapshot.Validate(receipt.Manifest,HistoricalReceipts.CatalogForSnapshot(catalog,receipt.Manifest),true);
        var keys = new HashSet<string>(WorkKeys(receipt.Manifest), StringComparer.Ordinal);
        if (receipt.ActiveOwners.Count > keys.Count || receipt.ActiveOwners.Distinct().Count() != receipt.ActiveOwners.Count || receipt.ActiveOwners.Any(k => !keys.Contains(k))) throw new IOException("Proprietários inválidos no recibo.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var manifestPaths=receipt.Manifest.Files.ToLookup(file=>file.Path,StringComparer.OrdinalIgnoreCase);
        foreach (var file in receipt.Files) {
            if (file == null || !paths.Add(file.Path) || !FixedData.HashValid(file.After) || file.Size < 0 || file.Size > InstalledSnapshot.MaxFileBytes
                || file.Before != null && !FixedData.HashValid(file.Before)||file.Expected!=null&&!FixedData.HashValid(file.Expected)
                || !manifestPaths.Contains(file.Path)) throw new IOException("Arquivo inválido no recibo de snapshot.");
            Target(file.Path);
            var payload = manifestPaths[file.Path].First();
            if (payload.Kind == "file" && (payload.Sha256 != file.After || payload.Size != file.Size)) throw new IOException("Arquivo do recibo não corresponde ao snapshot.");
        }
        if (receipt.Status == "installed" && paths.Count != manifestPaths.Count) throw new IOException("Recibo de snapshot incompleto.");
        return receipt;
    }
    IEnumerable<Receipt> AllReceipts(bool inspection=false,bool skipUnreadable=false) {
        Safe.NoLinks(records);
        if (!Directory.Exists(records)) yield break;
        foreach (string path in Directory.GetFiles(records, "*.json")) {
            Receipt receipt;
            try{receipt=ReadCore(Path.GetFileNameWithoutExtension(path),inspection);}
            catch(IOException ex) {if(!skipUnreadable)throw;LogUnreadableReceipt(ex);continue;}
            catch(UnauthorizedAccessException ex) {if(!skipUnreadable)throw;LogUnreadableReceipt(ex);continue;}
            if(receipt!=null)yield return receipt;
        }
    }
    static void LogUnreadableReceipt(Exception error){
        OnlineDiagnostics.Failure("scan.receipt.failed",error,"Ler o recibo local. A conferência dos outros recibos continua.");
    }
    bool Matches(Change file) { string path = Target(file.Path); return File.Exists(path) && new FileInfo(path).Length == file.Size && Safe.Hash(path) == file.After; }
    void Rollback(Receipt receipt, HashSet<string> written, string stage) {
        int preserved = 0;
        foreach (var file in receipt.Files.AsEnumerable().Reverse()) {
            if (!written.Contains(file.Path)) continue;
            try {
                string target = Target(file.Path);
                if (HashOrNull(target) != file.After) { preserved++; continue; }
                string before = file.Expected;
                if (before == null) DeleteChecked(target, file.After);
                else ReplaceChecked(Safe.In(stage, Fingerprint(file.Path) + ".rollback"), target, before, file.After);
                file.Reverted = true;
            } catch (IOException) { preserved++; } catch (UnauthorizedAccessException) { preserved++; }
        }
        receipt.ActiveOwners.Clear(); receipt.Status = preserved == 0 ? "rolledback" : "rollback-preserved"; Save(receipt);
    }
    internal static void ReplaceChecked(string source, string target, string desiredHash, string expectedCurrent,string preparedTemp=null) {
        Safe.NoLinks(source); Safe.NoLinks(target);
        if(preparedTemp!=null&&!String.Equals(Path.GetPathRoot(preparedTemp),Path.GetPathRoot(target),StringComparison.OrdinalIgnoreCase))
            preparedTemp=null;
        string temp = preparedTemp??target + ".snapshot-" + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            if(preparedTemp!=null){Safe.NoLinks(temp);if(File.Exists(temp))File.Delete(temp);}
            File.Copy(source, temp, false);
            if (Safe.Hash(temp) != desiredHash) throw new IOException("O arquivo preparado mudou durante a cópia.");
            if (Directory.Exists(target) || HashOrNull(target) != expectedCurrent) throw new IOException("O destino mudou durante a cópia; sua alteração foi preservada.");
            if (expectedCurrent == null) File.Move(temp, target); else File.Replace(temp, target, null);
        } finally { TryCleanup(() => { Safe.NoLinks(temp); if (File.Exists(temp)) File.Delete(temp); }); }
    }
    internal static void DeleteVerified(string target, string expected) { DeleteChecked(target, expected); }
    static void DeleteChecked(string target, string expected) {
        Safe.NoLinks(target);
        // Keep writers out during the final comparison. Validate before requesting deletion.
        using (var stream = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
        using (var hash = SHA256.Create()) {
            if (Safe.Hex(hash.ComputeHash(stream)) != expected) throw new IOException("O destino mudou antes da remoção.");
            if (HashOrNull(target) != expected) throw new IOException("O destino mudou antes da remoção.");
            File.Delete(target);
        }
    }
    static void Backup(string source, string target, string expected) {
        Safe.NoLinks(source); Safe.NoLinks(target); Directory.CreateDirectory(Path.GetDirectoryName(target));
        File.Copy(source, target, false);
        if (Safe.Hash(target) != expected) throw new IOException("O arquivo mudou antes do backup.");
    }
    internal static void CopyBounded(Stream source, Stream target, long size) {
        byte[] buffer = new byte[524288]; long total = 0; int count;
        while ((count = source.Read(buffer, 0, buffer.Length)) > 0) { total += count; if (total > size) throw new IOException("Arquivo maior que o declarado."); target.Write(buffer, 0, count); }
        if (total != size) throw new IOException("Arquivo incompleto no pacote.");
    }
    string Target(string path) { return GeneralInstallRoutes.Resolve(destination,path); }
    void CreateParents(string relative, List<string> created) {
        GeneralInstallRoutes.CreateParents(destination,relative,created);
    }
    internal string[] ReadSkippedDestinations(string id) {
        var receipt=Read(id);
        return receipt==null?new string[0]:receipt.SkippedDestinations.ToArray();
    }
    static string HashOrNull(string path) { Safe.NoLinks(path); return File.Exists(path) ? Safe.Hash(path) : null; }
    static string Fingerprint(string value) { using (var sha = SHA256.Create()) return Safe.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Substring(0, 32); }
    static void Report(Action<string> report, string message) { if (report != null) report(message); }
    static IOException Conflict(string path) { return new IOException("O destino já contém outro arquivo ou uma pasta: " + path + ". O conteúdo existente foi preservado."); }
    static void TryCleanup(Action action) { try { action(); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    static void DeleteEmpty(string path) { Safe.NoLinks(path); if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path); }
    static void CleanStage(string stage) {
        TryCleanup(() => {
            Safe.NoLinks(stage);
            if (!Directory.Exists(stage)) return;
            foreach (string file in Directory.GetFiles(stage)) { Safe.NoLinks(file); File.Delete(file); }
            DeleteEmpty(stage);
        });
    }
}
