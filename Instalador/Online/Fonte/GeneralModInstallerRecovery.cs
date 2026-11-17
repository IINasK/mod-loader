using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public sealed partial class GeneralModInstaller {
    internal Action<string> InstallCheckpoint;
    internal sealed class PendingInstall {
        public string Stage;
        public int StageFiles;
        public Record Desired;
        public List<PendingWrite> Writes = new List<PendingWrite>();
    }
    internal sealed class PendingWrite {
        public string Path, Before, After;
        public int StageIndex;
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
            !Guid.TryParseExact(pending.Stage.Substring(12),"N",out stageId)||pending.StageFiles<0||pending.StageFiles>20000||pending.Writes==null||pending.Writes.Any(write=>write==null))
            throw new IOException("Registro de recuperação de instalação inválido.");
        string stage=Safe.In(destination,pending.Stage);Safe.NoLinks(stage);
        if(pending.Desired==null&&pending.Writes.Count!=0)throw new IOException("Registro de recuperação incompleto.");
        bool committed=false;
        var restored=new HashSet<string>(StringComparer.Ordinal);
        if(pending.Desired!=null) {
            var desired=pending.Desired;
            if(desired.Files==null||desired.Files.Count==0||desired.Files.Any(file=>file==null)||!FixedData.HashValid(desired.SourceSha256))throw new IOException("Recuperação de recibo inválida.");
            GeneralInstallRoutes.Validate(desired.InstallRoutes);
            if(new HashSet<string>(desired.Files.Select(file=>Target(file.Path)),StringComparer.OrdinalIgnoreCase).Count!=desired.Files.Count)
                throw new IOException("Recuperação contém destinos repetidos.");
            foreach(var file in desired.Files)if(file.Size<0||!FixedData.HashValid(file.Sha256)||file.Before!=null&&!FixedData.HashValid(file.Before))throw new IOException("Recuperação de arquivo inválida.");
            var current=ReadRecord(RecordPath(desired.Key));
            committed=current!=null&&Safe.Json.Serialize(current)==Safe.Json.Serialize(desired);
            foreach(var write in pending.Writes) {
                var expected=desired.Files.SingleOrDefault(f=>String.Equals(f.Path,write.Path,StringComparison.OrdinalIgnoreCase));
                if(expected==null||expected.Sha256!=write.After||write.Before!=null&&!FixedData.HashValid(write.Before)||write.StageIndex<0||write.StageIndex>=pending.StageFiles)throw new IOException("Recuperação de alteração inválida.");
                Target(write.Path);
            }
        }
        if(!committed) {
            // Validate the entire rollback first, preserving manual edits and moved files.
            foreach(var write in pending.Writes) {
                if(NotApplied(write,stage))continue;
                string target=Target(write.Path);Safe.NoLinks(target);
                if(Directory.Exists(target))throw RecoveryChanged(write.Path);
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
                if(!File.Exists(target)) {if(write.Before!=null)throw RecoveryChanged(write.Path);continue;}
                if(Safe.Hash(target)==write.Before)continue;
                if(Safe.Hash(target)!=write.After)throw RecoveryChanged(write.Path);
                if(write.Before==null)InstalledSnapshotInstaller.DeleteVerified(target,write.After);else {RestoreBackup(write.Before,target,write.After);restored.Add(write.Before);}
            }
        }
        // Only enumerated stage slots owned by this transaction may be removed.
        for(int i=0;i<pending.StageFiles;i++) {
            string path=Safe.In(stage,i.ToString(System.Globalization.CultureInfo.InvariantCulture));Safe.NoLinks(path);
            if(File.Exists(path))File.Delete(path);
        }
        DeleteEmpty(stage);
        File.Delete(PendingPath);
        ReleaseUnreferencedBackups(restored);
    }
    static bool NotApplied(PendingWrite write,string stage) {
        // Internal publication atomically consumes its stage file. If it still
        // exists, an identical file appearing at the destination is not ours.
        return !GeneralInstallRoutes.External(write.Path)&&File.Exists(Safe.In(stage,write.StageIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }
    static IOException RecoveryChanged(string path) {
        return new IOException("Uma instalação foi interrompida e este arquivo mudou depois. Preserve sua alteração e restaure o arquivo para retomar a recuperação: "+path);
    }
}
