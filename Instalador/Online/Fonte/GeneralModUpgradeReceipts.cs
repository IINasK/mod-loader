using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

public sealed partial class GeneralModInstaller {
    internal string UpgradeReceiptFingerprint(string key,bool canonical=true) {
        string path=RecordPath(key);Safe.NoLinks(path);
        if(!File.Exists(path))return null;
        var before=new FileInfo(path);long ticks=before.LastWriteTimeUtc.Ticks,length=before.Length;
        var record=ReadRecord(path,true);
        var after=new FileInfo(path);
        if(record==null||!after.Exists||after.LastWriteTimeUtc.Ticks!=ticks||after.Length!=length)
            throw new IOException("O registro da instalação mudou durante a conferência.");
        // Older installers do not write TransactionId. The write stamp also
        // distinguishes a fresh, otherwise identical legacy installation.
        return GeneralUpdateOwnership.ReceiptFingerprint(new{Record=record,WriteUtc=ticks,Length=length},canonical);
    }

    internal void ForgetUpgradeReceipt(string key,string expectedFingerprint,bool canonical=true) {
        if(!FixedData.HashValid(expectedFingerprint))throw new IOException("A prova do registro anterior é inválida.");
        string initial=RecordPath(key);Safe.NoLinks(initial);
        if(!File.Exists(initial))return;
        using(LockUpgradeReceiptMetadata()) {
            string path=RecordPath(key);Safe.NoLinks(path);
            if(!File.Exists(path))return;
            if(UpgradeReceiptFingerprint(key,canonical)!=expectedFingerprint)
                throw new IOException("O registro anterior mudou depois da autorização. Sua instalação foi preservada.");
            // Metadata only: the new transaction already committed and owns its
            // verified files. This must never run the normal uninstall path.
            File.Delete(path);
        }
    }

    internal IDisposable LockUpgradeReceiptMetadata() {
        Safe.NoLinks(records);Directory.CreateDirectory(records);
        string path=Safe.In(records,"operations.lock");Safe.NoLinks(path);
        var handle=new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        try {
            // This lease deliberately does not call RecoverPendingInstall:
            // retiring a related record is a metadata-only operation.
            if(RecoveryPending)throw new IOException("Conclua a operação interrompida do projeto relacionado antes de transferir o registro da instalação.");
            return handle;
        }catch {handle.Dispose();throw;}
    }
}

public sealed partial class InstalledSnapshotInstaller {
    internal string UpgradeReceiptFingerprint(string id,bool canonical=true) {
        var receipt=ReadCore(id,true);
        return receipt==null?null:GeneralUpdateOwnership.ReceiptFingerprint(receipt,canonical);
    }

    internal void ForgetUpgradeReceipt(string id,string owner,string expectedFingerprint,bool canonical=true) {
        ForgetUpgradeReceipt(id,new[]{owner},expectedFingerprint,canonical);
    }

    internal void ForgetUpgradeReceipt(string id,IEnumerable<string> owners,string expectedFingerprint,bool canonical=true) {
        if(!FixedData.HashValid(expectedFingerprint)||owners==null)throw new IOException("A prova dos registros anteriores é inválida.");
        var requested=owners.ToArray();
        if(requested.Length==0||requested.Length>1000||requested.Distinct(StringComparer.Ordinal).Count()!=requested.Length||
            requested.Any(owner=>!Regex.IsMatch(owner??"",@"\A[A-Za-z0-9_-]{1,120}/[A-Za-z0-9_-]{1,120}\z")))
            throw new IOException("Os proprietários dos registros anteriores são inválidos.");
        var initial=Read(id);if(initial==null)return;
        using(AcquireLock(initial.Manifest.Game)) {
            var receipt=Read(id);if(receipt==null)return;
            var active=new HashSet<string>(requested.Where(receipt.ActiveOwners.Contains),StringComparer.Ordinal);
            if(active.Count==0)return;
            if(receipt.Status!="installed"||GeneralUpdateOwnership.ReceiptFingerprint(receipt,canonical)!=expectedFingerprint||
                requested.Any(owner=>!WorkKeys(receipt.Manifest).Contains(owner)))
                throw new IOException("O recibo anterior mudou depois da autorização. Sua instalação foi preservada.");
            // All approved owners from one receipt are retired in one write so
            // each owner is checked against the same original fingerprint.
            receipt.ActiveOwners.RemoveAll(active.Contains);
            Save(receipt);
        }
    }
}
