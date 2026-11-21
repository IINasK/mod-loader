using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public sealed partial class GeneralModInstaller {
    internal Action<string> InstallCheckpoint;
    internal sealed class PendingInstall {
        public string Stage;
        public int StageFiles;
        public bool PackageUpdate;
        public Record Desired;
        public List<PendingWrite> Writes = new List<PendingWrite>();
        public List<RecordFile> Removed = new List<RecordFile>();
        public List<Record> RetiredRecords = new List<Record>();
    }
    internal sealed class PendingWrite {
        public string Path, Before, After;
        public int StageIndex;
        public bool Delete;
    }
    string PendingPath { get { return Safe.In(records,"pending-install.journal"); } }
    internal bool RecoveryPending { get { Safe.NoLinks(PendingPath); return File.Exists(PendingPath); } }
    internal void RecoverInterruptedInstall() { using(AcquireLock()) { } }
    void Checkpoint(string name) { if(InstallCheckpoint!=null)InstallCheckpoint(name); }
    void SavePending(PendingInstall pending) { Safe.WriteJson(PendingPath,pending); }

    void RecoverPendingInstall() {
        if(!RecoveryPending)return;
        var pending=Safe.Json.Deserialize<PendingInstall>(GeneralProjectStorage.ReadText(PendingPath,16000000));
        Guid stageId;
        if(pending==null||pending.Stage==null||!pending.Stage.StartsWith(".mod-loader-",StringComparison.Ordinal)||
            !Guid.TryParseExact(pending.Stage.Substring(12),"N",out stageId)||pending.StageFiles<0||pending.StageFiles>40000||pending.Writes==null||pending.Writes.Any(write=>write==null)||
            pending.Removed==null||pending.RetiredRecords==null)
            throw new IOException("Registro de recuperação de instalação inválido.");
        string stage=Safe.In(destination,pending.Stage);Safe.NoLinks(stage);
        if(pending.Desired==null&&pending.Writes.Count!=0)throw new IOException("Registro de recuperação incompleto.");
        bool committed=false;
        var restored=new HashSet<string>(StringComparer.Ordinal);
        if(pending.Desired!=null) {
            var desired=pending.Desired;
            Guid transaction;
            if(desired.TransactionId!=null&&!Guid.TryParseExact(desired.TransactionId,"N",out transaction))throw new IOException("Identidade da recuperação inválida.");
            bool upgrade=desired.Upgrade!=null;
            if(upgrade)ValidateUpgrade(desired.Key,desired.Upgrade,desired.SourceSha256);
            if(pending.PackageUpdate&&!upgrade)throw new IOException("Recuperação de atualização sem identidade de origem.");
            if(!pending.PackageUpdate&&(pending.StageFiles>20000||pending.Removed.Count!=0||pending.RetiredRecords.Count!=0||pending.Writes.Any(write=>write.Delete)))
                throw new IOException("Recuperação de instalação não reconhece remoções.");
            if(desired.Files==null||desired.Files.Count==0||desired.Files.Any(file=>file==null)||!FixedData.HashValid(desired.SourceSha256))throw new IOException("Recuperação de recibo inválida.");
            GeneralInstallRoutes.Validate(desired.InstallRoutes);
            if(new HashSet<string>(desired.Files.Select(file=>Target(file.Path)),StringComparer.OrdinalIgnoreCase).Count!=desired.Files.Count)
                throw new IOException("Recuperação contém destinos repetidos.");
            foreach(var file in desired.Files)if(file.Size<0||!FixedData.HashValid(file.Sha256)||file.Before!=null&&!FixedData.HashValid(file.Before))throw new IOException("Recuperação de arquivo inválida.");
            if(pending.Removed.Count>20000||pending.Removed.Any(file=>file==null||!file.Owned||file.Size<0||!FixedData.HashValid(file.Sha256)||
                file.Before!=null&&!FixedData.HashValid(file.Before)||GeneralInstallRoutes.External(file.Path))||
                pending.Removed.Select(file=>Target(file.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=pending.Removed.Count||
                pending.Removed.Any(file=>desired.Files.Any(next=>String.Equals(next.Path,file.Path,StringComparison.OrdinalIgnoreCase))))
                throw new IOException("Recuperação de arquivos removidos inválida.");
            if(pending.PackageUpdate) {
                var expectedRetired=new HashSet<string>(UpdateRetiredKeys(desired.Upgrade),StringComparer.Ordinal);
                if(pending.RetiredRecords.Count!=expectedRetired.Count||pending.RetiredRecords.Any(record=>record==null)||
                    pending.RetiredRecords.Select(record=>record.Key).Distinct(StringComparer.Ordinal).Count()!=pending.RetiredRecords.Count||
                    pending.RetiredRecords.Any(record=>!expectedRetired.Contains(record.Key)))
                    throw new IOException("Recuperação dos registros anteriores inválida.");
            }
            var current=ReadRecord(RecordPath(desired.Key));
            committed=current!=null&&GeneralUpdateOwnership.ReceiptJson(current)==GeneralUpdateOwnership.ReceiptJson(desired);
            foreach(var write in pending.Writes) {
                var expected=desired.Files.SingleOrDefault(f=>String.Equals(f.Path,write.Path,StringComparison.OrdinalIgnoreCase));
                var removed=pending.Removed.SingleOrDefault(f=>String.Equals(f.Path,write.Path,StringComparison.OrdinalIgnoreCase));
                bool known=expected!=null&&!write.Delete&&expected.Sha256==write.After||removed!=null&&removed.Sha256==write.Before&&removed.Before==write.After&&write.Delete==(write.After==null);
                if(!known||write.Before!=null&&!FixedData.HashValid(write.Before)||write.StageIndex<0||write.StageIndex>=pending.StageFiles)throw new IOException("Recuperação de alteração inválida.");
                Target(write.Path);
            }
            if(pending.Writes.Select(write=>Target(write.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=pending.Writes.Count||
                pending.Writes.Select(write=>write.StageIndex).Distinct().Count()!=pending.Writes.Count)
                throw new IOException("Recuperação de instalação contém alterações repetidas.");
        }
        if(!committed) {
            // Validate the entire rollback first, preserving manual edits and moved files.
            foreach(var write in pending.Writes) {
                if(NotApplied(write,stage))continue;
                string target=Target(write.Path);Safe.NoLinks(target);
                if(Directory.Exists(target))throw RecoveryChanged(write.Path);
                if(write.Delete) {
                    string deleted=UpdateSlot(stage,write.StageIndex);Safe.NoLinks(deleted);
                    if(Safe.Hash(deleted)!=write.Before||!Directory.Exists(Path.GetDirectoryName(target)))throw RecoveryChanged(write.Path);
                    if(File.Exists(target)&&Safe.Hash(target)!=write.Before)throw RecoveryChanged(write.Path);
                    continue;
                }
                if(!File.Exists(target)) { if(write.Before!=null)throw RecoveryChanged(write.Path);continue; }
                string hash=Safe.Hash(target);
                if(hash==write.Before)continue;
                if(hash!=write.After)throw RecoveryChanged(write.Path);
                if(write.Before!=null) {
                    string backup=BackupPath(write.Before);Safe.NoLinks(backup);
                    if(!File.Exists(backup)||Safe.Hash(backup)!=write.Before)throw RecoveryChanged(write.Path);
                }
            }
            foreach(var write in pending.Writes.AsEnumerable().Reverse()) {
                if(NotApplied(write,stage))continue;
                string target=Target(write.Path);
                if(write.Delete) {
                    string deleted=UpdateSlot(stage,write.StageIndex);
                    if(!File.Exists(target))File.Move(deleted,target);
                    else if(Safe.Hash(target)!=write.Before)throw RecoveryChanged(write.Path);
                    restored.Add(write.Before);
                    continue;
                }
                if(!File.Exists(target)) {if(write.Before!=null)throw RecoveryChanged(write.Path);continue;}
                if(Safe.Hash(target)==write.Before)continue;
                if(Safe.Hash(target)!=write.After)throw RecoveryChanged(write.Path);
                if(write.Before==null)InstalledSnapshotInstaller.DeleteVerified(target,write.After);else {RestoreBackup(write.Before,target,write.After);restored.Add(write.Before);}
            }
        }
        if(committed&&pending.PackageUpdate) {
            foreach(var old in pending.RetiredRecords) {
                string path=RecordPath(old.Key);
                var current=ReadRecord(path);
                if(current==null)continue;
                var related=pending.Desired.Upgrade.RelatedReceipts.SingleOrDefault(item=>item.Key==old.Key&&RelatedStoreIsCurrent(item.Data));
                if(related!=null&&UpgradeReceiptFingerprint(old.Key,related.CanonicalFingerprint)!=related.Fingerprint)throw new IOException("O registro relacionado mudou durante a atualização.");
                if(GeneralUpdateOwnership.ReceiptJson(current)!=GeneralUpdateOwnership.ReceiptJson(old))throw new IOException("O registro anterior mudou durante a atualização.");
                File.Delete(path);
                foreach(var file in old.Files)if(file.Before!=null)restored.Add(file.Before);
            }
        }
        // Only enumerated stage slots owned by this transaction may be removed.
        for(int i=0;i<pending.StageFiles;i++) {
            string path=Safe.In(stage,i.ToString(System.Globalization.CultureInfo.InvariantCulture));Safe.NoLinks(path);
            if(File.Exists(path))File.Delete(path);
        }
        DeleteEmpty(stage);
        File.Delete(PendingPath);
        foreach(var write in pending.Writes)if(write.Before!=null)restored.Add(write.Before);
        ReleaseUnreferencedBackups(restored);
    }
    static bool NotApplied(PendingWrite write,string stage) {
        // Internal publication atomically consumes its stage file. If it still
        // exists, an identical file appearing at the destination is not ours.
        bool exists=File.Exists(Safe.In(stage,write.StageIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return write.Delete?!exists:!GeneralInstallRoutes.External(write.Path)&&exists;
    }
    static IOException RecoveryChanged(string path) {
        return new IOException("Uma instalação foi interrompida e este arquivo mudou depois. Preserve sua alteração e restaure o arquivo para retomar a recuperação: "+path);
    }
}
