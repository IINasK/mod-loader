using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public sealed partial class InstalledSnapshotInstaller {
    // No payload hashing or scan of the game. Only authenticated receipt paths are
    // eligible; active rollback plans and uninstall originals remain untouched.
    internal bool CleanupCompletedArtifacts(string receiptId) {
        var initial=Read(receiptId);
        if(initial==null||initial.Status!="installed")return false;
        using(AcquireLock(initial.Manifest.Game)) {
            var receipt=Read(receiptId);
            if(receipt==null||receipt.Status!="installed")return false;
            string stage=Safe.In(destination,".mod-loader-snapshot-"+receipt.Id);
            Safe.NoLinks(stage);
            if(Directory.Exists(stage)) {
                foreach(var change in receipt.Files) {
                    DeleteTemporary(Safe.In(stage,Fingerprint(change.Path)));
                    DeleteTemporary(Safe.In(stage,Fingerprint(change.Path)+".rollback"));
                }
                DeleteEmpty(stage);
            }
            var activePaths=new HashSet<string>(receipt.Manifest.Files
                .Where(file=>file.Owners.Any(receipt.ActiveOwners.Contains)).Select(file=>file.Path),StringComparer.OrdinalIgnoreCase);
            string backups=Safe.In(records,"backups/"+receipt.Id);Safe.NoLinks(backups);
            if(Directory.Exists(backups)) {
                foreach(var change in receipt.Files) {
                    bool required=change.Before!=null&&change.Owned&&change.Applied&&!change.Reverted&&activePaths.Contains(change.Path);
                    if(!required)DeleteTemporary(BackupPath(receipt,change.Path));
                }
                DeleteEmpty(backups);
            }
            return true;
        }
    }
    static void DeleteTemporary(string path) {
        Safe.NoLinks(path);
        if(File.Exists(path))File.Delete(path);
    }
}
