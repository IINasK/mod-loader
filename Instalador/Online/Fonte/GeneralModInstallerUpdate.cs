using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

public sealed partial class GeneralModInstaller {
    public sealed class RelatedReceipt {
        public string Data, Key, SnapshotId, Owner, Fingerprint;
        public bool CanonicalFingerprint;
    }
    // Kept in the committed receipt so a close between committing files and
    // saving the project cannot make the next scan expect the previous source.
    public sealed class UpgradeInfo {
        public string Owner, Preset, PreviousSource, Source, Version, PackageSha256;
        public string PreviousImport, PreviousSnapshot, PreviousFixed;
        public string PreviousPresetFingerprint;
        public string ExpectedPresetFingerprint;
        public long HistoryUtc;
        public string HistoryPreviousVersion,HistoryBackup;
        public bool PredecessorsRetired;
        public bool Reconciled;
        public List<string> PreviousReceiptIds = new List<string>();
        public List<string> RetiredKeys = new List<string>();
        public List<RelatedReceipt> RelatedReceipts = new List<RelatedReceipt>();
    }

    internal Record ReadPackageRecord(string key) {
        var record=ReadRecord(RecordPath(key));
        return record==null?null:CopyUpdateRecord(record);
    }
    internal Record ReadUpdateRecord(string key) {
        var record=ReadPackageRecord(key);
        return record!=null&&record.Upgrade!=null?record:null;
    }
    internal List<Record> ReadUpdateRecords() {
        Safe.NoLinks(records);
        if(!Directory.Exists(records))return new List<Record>();
        return Directory.EnumerateFiles(records,"*.json").Select(path=>ReadRecord(path))
            .Where(record=>record!=null&&record.Upgrade!=null).Select(CopyUpdateRecord).ToList();
    }
    internal Record MarkUpdatePredecessorsRetired(string key) {
        return MarkUpdateState(key,false);
    }
    internal Record MarkUpdateReconciled(string key) {
        return MarkUpdateState(key,true);
    }
    Record MarkUpdateState(string key,bool reconciled) {
        using(AcquireLock()) {
            var record=ReadRecord(RecordPath(key));
            if(record==null||record.Upgrade==null)throw new IOException("O registro da atualização não está mais disponível.");
            if(reconciled?!record.Upgrade.Reconciled:!record.Upgrade.PredecessorsRetired) {
                if(reconciled)record.Upgrade.Reconciled=true;else record.Upgrade.PredecessorsRetired=true;
                Safe.WriteJson(RecordPath(key),record);
            }
            return CopyUpdateRecord(record);
        }
    }
    static Record CopyUpdateRecord(Record record) {return Safe.Json.Deserialize<Record>(Safe.Json.Serialize(record));}

    static void ValidateUpgrade(string key,UpgradeInfo info,string hash) {
        if(info==null||!Regex.IsMatch(info.Owner??"",@"\A[A-Za-z0-9_-]{1,120}/[A-Za-z0-9_-]{1,120}\z")||
            !GeneralModDownload.IsWebSource(info.Source)||!GeneralModDownload.IsWebSource(info.PreviousSource)||
            key!=info.Owner+"/0/"+info.Source||!FixedData.HashValid(info.PackageSha256)||info.PackageSha256!=hash||
            info.Preset!=null&&!Regex.IsMatch(info.Preset,@"\A[A-Za-z0-9_-]{1,120}\z")||
            info.Version==null||info.Version.Length>120||info.Version.Any(Char.IsControl)||
            info.PreviousImport!=null&&!Regex.IsMatch(info.PreviousImport,@"\Afile-[a-f0-9]{64}\z")||
            info.PreviousSnapshot!=null&&!Regex.IsMatch(info.PreviousSnapshot,@"\A[a-f0-9]{32}\z")||
            info.PreviousFixed!=null&&!Regex.IsMatch(info.PreviousFixed,@"\A[A-Za-z0-9_-]{1,120}\z")||
            info.PreviousPresetFingerprint!=null&&!FixedData.HashValid(info.PreviousPresetFingerprint)||
            info.ExpectedPresetFingerprint!=null&&!FixedData.HashValid(info.ExpectedPresetFingerprint)||
            info.PreviousReceiptIds==null||info.PreviousReceiptIds.Count>1024||
            info.PreviousReceiptIds.Distinct(StringComparer.Ordinal).Count()!=info.PreviousReceiptIds.Count||
            info.PreviousReceiptIds.Any(id=>!Regex.IsMatch(id??"",@"\A[a-f0-9]{32}\z"))||
            info.RetiredKeys==null||info.RetiredKeys.Count>32||info.RetiredKeys.Distinct(StringComparer.Ordinal).Count()!=info.RetiredKeys.Count||
            info.RetiredKeys.Any(retired=>retired==key||retired==null||retired.Length>4600||
                !retired.StartsWith(info.Owner+"/",StringComparison.Ordinal)||retired.Any(Char.IsControl)))
            throw new IOException("O registro da atualização não corresponde ao pacote aprovado.");
        if(info.RelatedReceipts==null||info.RelatedReceipts.Count>1024)
            throw new IOException("A lista de registros relacionados à atualização é inválida.");
        foreach(var receipt in info.RelatedReceipts)ValidateRelatedReceipt(receipt);
        if(info.RelatedReceipts.Select(receipt=>receipt.Data.ToUpperInvariant()+"\n"+
            (receipt.SnapshotId??receipt.Key)+"\n"+receipt.Owner).Distinct(StringComparer.Ordinal).Count()!=info.RelatedReceipts.Count)
            throw new IOException("A atualização contém registros relacionados repetidos.");
    }
    internal static void ValidateRelatedReceipt(RelatedReceipt receipt) {
        if(receipt==null||String.IsNullOrWhiteSpace(receipt.Data)||receipt.Data.Length>1024||receipt.Data.Any(Char.IsControl)||
            !Regex.IsMatch(receipt.Data,@"\A[A-Za-z]:\\")||receipt.Data.Length<=3||
            !Regex.IsMatch(receipt.Owner??"",@"\A[A-Za-z0-9_-]{1,120}/[A-Za-z0-9_-]{1,120}\z")||
            !FixedData.HashValid(receipt.Fingerprint)||(receipt.Key==null)==(receipt.SnapshotId==null)||
            receipt.SnapshotId!=null&&!Regex.IsMatch(receipt.SnapshotId,@"\A[a-f0-9]{32}\z")||
            receipt.Key!=null&&(String.IsNullOrWhiteSpace(receipt.Key)||receipt.Key.Length>4600||receipt.Key.Any(Char.IsControl)||
                !receipt.Key.StartsWith(receipt.Owner+"/",StringComparison.Ordinal)))
            throw new IOException("O registro relacionado à atualização é inválido.");
        string canonical=Path.GetFullPath(receipt.Data).TrimEnd('\\');
        if(!String.Equals(canonical,receipt.Data,StringComparison.OrdinalIgnoreCase))
            throw new IOException("O destino do registro relacionado não é canônico.");
        Safe.NoLinks(canonical);
    }

    // The coordinator supplies an installed, verified baseline and the paths
    // used by other owners (including installers outside this receipt store).
    // The engine checks that proof again while holding its operation lock.
    internal Record UpdatePackage(string source,string key,Record baseline,UpgradeInfo upgrade,
        IEnumerable<string> sharedPaths,Action<string> report,Action checkpoint=null,Func<string,string> originalBackup=null,
        Action<RecordFile> validateFile=null,Action<string,int?> progress=null,Action<Record> backupVersion=null,
        Func<string,bool> skipEntry=null,IEnumerable<RecordFile> attachedFiles=null,bool requireMatchingPayload=false) {
        source=Path.GetFullPath(source);Safe.NoLinks(source);
        if(baseline==null||baseline.Files==null||baseline.Files.Count==0||baseline.Files.Count>20000)
            throw new IOException("A atualização precisa do registro completo da instalação anterior.");
        baseline=CopyUpdateRecord(baseline);
        if(upgrade==null)throw new IOException("A atualização precisa identificar o pacote aprovado.");
        ValidateUpgrade(key,upgrade,upgrade.PackageSha256);
        upgrade=Safe.Json.Deserialize<UpgradeInfo>(Safe.Json.Serialize(upgrade));
        GeneralInstallRoutes.Validate(baseline.InstallRoutes);
        GeneralInstallRoutes.CheckFolders(destination,baseline.InstallRoutes);
        if(baseline.Files.Any(file=>file==null||file.Size<0||!FixedData.HashValid(file.Sha256)||
            file.Before!=null&&!FixedData.HashValid(file.Before)))throw new IOException("O registro anterior contém arquivos inválidos.");
        ValidatePaths(baseline.Files.Select(file=>file.Path).ToArray());
        if(baseline.Files.Any(file=>GeneralInstallRoutes.External(file.Path)))
            throw new IOException("Esta atualização usa destinos pessoais. Revise os destinos antes de substituir os arquivos.");

        using(var lease=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read))
        using(var reader=BundledArchiveReader.Open(source,lease.Length,upgrade.PackageSha256))
        using(AcquireLock()) {
            var oldAtKey=ReadRecord(RecordPath(key));
            if(oldAtKey!=null&&oldAtKey.SourceSha256==upgrade.PackageSha256&&oldAtKey.Upgrade!=null&&
                oldAtKey.Files.All(file=>Matches(Target(file.Path),file)))return CopyUpdateRecord(oldAtKey);
            if(oldAtKey!=null)throw new IOException("Já existe outro pacote registrado para esta atualização.");
            var retired=new List<Record>();
            foreach(string retiredKey in UpdateRetiredKeys(upgrade)) {
                var record=ReadRecord(RecordPath(retiredKey));
                if(record==null)throw new IOException("O registro da versão anterior mudou. Execute a verificação novamente.");
                var related=upgrade.RelatedReceipts.SingleOrDefault(item=>item.Key==retiredKey&&RelatedStoreIsCurrent(item.Data));
                if(related!=null&&UpgradeReceiptFingerprint(retiredKey,related.CanonicalFingerprint)!=related.Fingerprint)
                    throw new IOException("O registro relacionado mudou depois da autorização.");
                bool same=record.Files.Count==baseline.Files.Count&&record.Files.All(file=>baseline.Files.Any(old=>SameUpdateFile(old,file)));
                if(related==null?!same:!AuthorizedUpdateAlias(record,baseline,upgrade,related))
                    throw new IOException("A atualização não inclui todos os arquivos do registro anterior.");
                retired.Add(record);
            }
            var ignored=new HashSet<string>(retired.Select(record=>RecordPath(record.Key)),StringComparer.OrdinalIgnoreCase);
            ignored.Add(RecordPath(key));
            var others=Directory.Exists(records)?Directory.EnumerateFiles(records,"*.json")
                .Where(path=>!ignored.Contains(path)).Select(path=>ReadRecord(path)).Where(record=>record!=null)
                .SelectMany(record=>record.Files).ToList():new List<RecordFile>();
            var shared=new HashSet<string>((sharedPaths??Enumerable.Empty<string>()).Select(Target),StringComparer.OrdinalIgnoreCase);
            foreach(var file in others)shared.Add(Target(file.Path));

            var entries=reader.Files();
            if(skipEntry!=null)entries=entries.Where(entry=>!skipEntry(entry.Path)).ToArray();
            if(entries.Length==0)throw new IOException("Nenhum arquivo de mod encontrado na atualização.");
            string[] paths=entries.Select(entry=>GeneralInstallRoutes.Map(entry.Path,baseline.InstallRoutes)).ToArray();
            ValidatePaths(paths);
            if(paths.Any(GeneralInstallRoutes.External))throw new IOException("A atualização contém um destino pessoal que precisa ser revisado.");
            var oldFiles=baseline.Files.ToDictionary(file=>file.Path,StringComparer.OrdinalIgnoreCase);
            var incoming=new HashSet<string>(paths,StringComparer.OrdinalIgnoreCase);
            if(requireMatchingPayload){
                var anchors=baseline.Files.Where(file=>!InstalledFileOwnership.Companion(file.Path)).ToArray();
                if(anchors.Length==0)anchors=baseline.Files.ToArray();
                if(!anchors.Any(file=>incoming.Contains(file.Path)))throw new IOException(GeneralStyle.T(
                    "O pacote escolhido não corresponde aos caminhos da variante instalada. Escolha o pacote da sua variante.",
                    "The selected archive does not match the installed variant's paths. Choose the archive for your variant."));
            }
            var replacements=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var attached=(attachedFiles??Enumerable.Empty<RecordFile>()).Where(file=>!oldFiles.ContainsKey(file.Path)).ToArray();
            foreach(var file in attached){
                string target=Target(file.Path);
                if(GeneralInstallRoutes.External(file.Path)||!InventoryLinkStore.CanManageAttachment(file.Path)||!file.Owned||
                    file.Before!=null||!FixedData.HashValid(file.Sha256)||shared.Contains(target))continue;
                if(!incoming.Contains(file.Path))continue;
                if(!Matches(target,file))
                    throw new IOException("Um arquivo vinculado foi alterado após o vínculo: "+file.Path);
                oldFiles.Add(file.Path,file);
                replacements.Add(file.Path);
            }
            var next=new Record{Key=key,SourceSha256=upgrade.PackageSha256,TransactionId=Guid.NewGuid().ToString("N"),InstallRoutes=baseline.InstallRoutes,
                Upgrade=upgrade,Files=new List<RecordFile>()};
            var pending=new PendingInstall{Stage=".mod-loader-"+Guid.NewGuid().ToString("N"),
                StageFiles=checked(entries.Length+baseline.Files.Count),RetiredRecords=retired,PackageUpdate=true};
            int nextSlot=entries.Length;
            string stage=Safe.In(destination,pending.Stage);
            var created=new List<string>();
            bool saved=false;
            var transientBackups=new HashSet<string>(StringComparer.Ordinal);
            Action step=()=>{if(checkpoint!=null)checkpoint();};
            try {
                step();SavePending(pending);saved=true;Directory.CreateDirectory(stage);
                for(int index=0;index<entries.Length;index++) {
                    step();var entry=entries[index];
                    string staged=UpdateSlot(stage,index);
                    reader.CopyEntry(entry.RawPath,input=>{
                        using(var output=new FileStream(staged,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                            InstalledSnapshotInstaller.CopyBounded(input,output,entry.Size);
                    });
                    next.Files.Add(new RecordFile{Path=paths[index],ArchivePath=entry.Path,Size=entry.Size,Sha256=Safe.Hash(staged)});
                    if(validateFile!=null)validateFile(next.Files[next.Files.Count-1]);
                    if(report!=null)report("Conferindo atualização: "+entry.Path);
                    if(progress!=null)progress("prepare",100*(index+1)/entries.Length);
                }
                Checkpoint("update-staged");
                // Check every original, including files removed by the new package.
                // A manual edit or moved file is never silently forgotten.
                foreach(var file in baseline.Files) {
                    step();string target=Target(file.Path);
                    if(!Matches(target,file))throw new IOException("Um arquivo da versão anterior mudou. Sua alteração foi preservada: "+file.Path);
                    if(file.Before!=null){ImportUpdateBackup(file.Before,originalBackup);transientBackups.Add(file.Before);}
                }
                for(int index=0;index<next.Files.Count;index++) {
                    var file=next.Files[index];string target=Target(file.Path);RecordFile old;
                    bool hasOld=oldFiles.TryGetValue(file.Path,out old);
                    string existing=UpdateHash(target);
                    bool changes=existing!=file.Sha256;
                    if(replacements.Contains(file.Path)&&existing!=old.Sha256)throw Conflict(file.Path);
                    if(changes&&existing!=null&&(!hasOld||!old.Owned||shared.Contains(target)))
                        throw new IOException("A atualização substituiria um arquivo preexistente ou compartilhado: "+file.Path);
                    file.Owned=hasOld?old.Owned:existing==null;
                    file.Before=hasOld?old.Before:null;
                    if(changes) {
                        if(existing!=null){SaveBackup(target,existing);transientBackups.Add(existing);}
                        pending.Writes.Add(new PendingWrite{Path=file.Path,Before=existing,After=file.Sha256,StageIndex=index});
                    }
                }
                var newPaths=new HashSet<string>(next.Files.Select(file=>file.Path),StringComparer.OrdinalIgnoreCase);
                foreach(var old in baseline.Files.Where(file=>!newPaths.Contains(file.Path)&&file.Owned&&!shared.Contains(Target(file.Path)))) {
                    step();SaveBackup(Target(old.Path),old.Sha256);transientBackups.Add(old.Sha256);
                    int slot=nextSlot++;
                    if(old.Before!=null)File.Copy(BackupPath(old.Before),UpdateSlot(stage,slot),false);
                    pending.Removed.Add(old);
                    pending.Writes.Add(new PendingWrite{Path=old.Path,Before=old.Sha256,After=old.Before,StageIndex=slot,Delete=old.Before==null});
                }
                if(next.Upgrade.HistoryUtc>0)next.Upgrade.HistoryUtc=DateTime.UtcNow.Ticks;
                pending.Desired=next;SavePending(pending);Checkpoint("update-prepared");
                step();
                // Recheck the complete plan before publishing even the first file.
                foreach(var old in baseline.Files)if(!Matches(Target(old.Path),old))throw Conflict(old.Path);
                foreach(var write in pending.Writes)if(UpdateHash(Target(write.Path))!=write.Before)throw Conflict(write.Path);
                if(backupVersion!=null){
                    var complete=CopyUpdateRecord(baseline);
                    complete.Files.AddRange(attached.Where(file=>replacements.Contains(file.Path)||Matches(Target(file.Path),file)));
                    backupVersion(complete);step();
                }
                int writesCompleted=0;
                if(progress!=null)progress("install",0);
                foreach(var write in pending.Writes) {
                    step();string target=Target(write.Path);Safe.NoLinks(target);
                    if(UpdateHash(target)!=write.Before)throw Conflict(write.Path);
                    if(write.Delete)File.Move(target,UpdateSlot(stage,write.StageIndex));
                    else {
                        GeneralInstallRoutes.CreateParents(destination,write.Path,created);
                        string staged=UpdateSlot(stage,write.StageIndex);
                        if(Safe.Hash(staged)!=write.After)throw new IOException("O arquivo preparado mudou: "+write.Path);
                        if(write.Before==null)File.Move(staged,target);else File.Replace(staged,target,null);
                    }
                    Checkpoint("update-written");
                    if(report!=null)report("Atualizado: "+write.Path);
                    if(progress!=null)progress("install",100*++writesCompleted/pending.Writes.Count);
                }
                step();
                foreach(var file in next.Files)if(!Matches(Target(file.Path),file))throw Conflict(file.Path);
                foreach(var file in pending.Removed)if(UpdateHash(Target(file.Path))!=file.Before)throw Conflict(file.Path);
                Safe.WriteJson(RecordPath(key),next);Checkpoint("update-committed");
                if(progress!=null)progress("cleanup",null);
                RecoverPendingInstall();saved=false;
                ReleaseUnreferencedBackups(transientBackups);
                return CopyUpdateRecord(next);
            } catch {
                if(saved)RecoverPendingInstall();
                foreach(string directory in created.AsEnumerable().Reverse())TryCleanup(()=>DeleteEmpty(directory));
                ReleaseUnreferencedBackups(transientBackups);
                throw;
            }
        }
    }
    static bool SameUpdateFile(RecordFile left,RecordFile right) {
        return String.Equals(left.Path,right.Path,StringComparison.OrdinalIgnoreCase)&&left.Sha256==right.Sha256&&
            left.Size==right.Size&&left.Owned==right.Owned&&left.Before==right.Before;
    }
    bool RelatedStoreIsCurrent(string data) {
        return String.Equals(Path.GetFullPath(Path.Combine(data,"Instalacoes")),Path.GetDirectoryName(records),StringComparison.OrdinalIgnoreCase);
    }
    internal bool IsCurrentRelatedStore(string data){return RelatedStoreIsCurrent(data);}
    IEnumerable<string> UpdateRetiredKeys(UpgradeInfo upgrade) {
        return upgrade.RetiredKeys.Concat(upgrade.RelatedReceipts.Where(item=>item.Key!=null&&RelatedStoreIsCurrent(item.Data)).Select(item=>item.Key))
            .Distinct(StringComparer.Ordinal);
    }
    static bool AuthorizedUpdateAlias(Record alias,Record baseline,UpgradeInfo upgrade,RelatedReceipt authorization) {
        if(authorization==null||alias.Files.Count!=baseline.Files.Count||!FixedData.HashValid(alias.SourceSha256)||
            baseline.SourceSha256!=null&&alias.SourceSha256!=baseline.SourceSha256||
            !GeneralInstallRoutes.Same(alias.InstallRoutes,baseline.InstallRoutes)||
            alias.Files.Any(file=>!baseline.Files.Any(old=>String.Equals(old.Path,file.Path,StringComparison.OrdinalIgnoreCase)&&old.Sha256==file.Sha256&&old.Size==file.Size)))return false;
        bool sameOwnership=alias.Files.All(file=>file.Owned)&&alias.Files.All(file=>baseline.Files.Any(old=>SameUpdateFile(old,file)));
        bool recognition=alias.Files.All(file=>!file.Owned&&file.Before==null);
        if(!sameOwnership&&!recognition)return false;
        var parts=alias.Key.Split(new[]{'/'},4);
        if(parts.Length!=4||parts[2]!="0")return false;
        var aliasSource=GeneralNexusLink.Parse(parts[3]);var previous=GeneralNexusLink.Parse(upgrade.PreviousSource);
        return aliasSource!=null&&previous!=null?aliasSource.Url==previous.Url:parts[3]==upgrade.PreviousSource;
    }
    static string UpdateSlot(string stage,int index) {return Safe.In(stage,index.ToString(System.Globalization.CultureInfo.InvariantCulture));}
    string UpdateHash(string path) {Safe.NoLinks(path);if(Directory.Exists(path))throw Conflict(path);return File.Exists(path)?Safe.Hash(path):null;}
    void ImportUpdateBackup(string hash,Func<string,string> originalBackup) {
        string path=BackupPath(hash);Safe.NoLinks(path);
        if(File.Exists(path)){if(Safe.Hash(path)!=hash)throw new IOException("O backup anterior mudou; atualização cancelada.");return;}
        string source=originalBackup==null?null:originalBackup(hash);
        if(source==null)throw new IOException("O backup da instalação anterior não está disponível.");
        SaveBackup(source,hash);
    }
}
