using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

public sealed partial class FixedInstaller {
    void EnsureNoIncomplete() {
        foreach(string path in AllReceiptPaths()) {
            var old=ReadReceipt(path);
            if(new[]{"preparando","instalando","falha","desfazendo","desativando","restauracao_pendente"}.Contains(old.Status))
                throw new IOException("Existe uma operação incompleta. Use Desfazer última operação antes de continuar.");
        }
    }
    public string Deactivate(string modId,Action<string> report,int failAfter=-1) {
        var mod=catalog.Packages.SingleOrDefault(m=>m.Id==modId);
        if(mod==null)throw new IOException("Mod fora da lista fixa.");
        if(mod.Immutable)throw new IOException("A base NightCoop/RED4ext é imutável e não pode ser desativada por este programa.");
        Closed();Safe.NoLinks(game);
        Directory.CreateDirectory(Data);
        using(var mutex=new FileStream(Safe.In(Data,"operacao.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
            EnsureNoIncomplete();
            if(modId==NightCityDensity.ModId)mod=NightCityDensity.Installed(mod,game);
            var scan=FixedData.Scan(mod,game,false);
            if(scan.IsConflict)throw new IOException("Há arquivos diferentes ou não verificáveis. Desativação bloqueada para preservar suas alterações.");
            if(scan.Correct+scan.Original==0)return "Este mod não está instalado. Nada foi alterado.";
            bool stockEquipment=EquipmentExOptions.StockActive(catalog,game);
            var dependents=catalog.Packages.Where(m=>m.Id!=modId&&FixedData.Expand(catalog,new[]{m.Id},stockEquipment).Any(d=>d.Id==modId))
                .Where(m=>{var s=FixedData.Scan(m,game);return s.Correct+s.Original+s.Different>0||s.State=="Não verificável";}).Select(m=>m.Name).ToList();
            if(dependents.Count>0)throw new IOException("Desative primeiro os mods que usam este componente: "+String.Join(", ",dependents));
            var r=new OnlineReceipt{Id=Guid.NewGuid().ToString("N"),CatalogId=catalog.Id,CreatedUtc=DateTime.UtcNow.ToString("o"),Status="desativando",Operation="deactivate",BackupLayout="flat-v1",Mods=new List<string>{modId}};
            string ledger=Safe.In(Data,r.Id+".json");Save(ledger,r);
            try {
                BackupSaves(Safe.In(Data,"backups/"+r.Id+"/saves"));int moved=0;
                foreach(var f in mod.Files.Where(f=>!f.UserData)) {
                    Closed();string source=Safe.In(game,f.Path);
                    if(!File.Exists(source)){if(Directory.Exists(source))throw new IOException("Uma pasta ocupa o arquivo do mod.");continue;}
                    long size=new FileInfo(source).Length;string hash=Safe.Hash(source);
                    if(!FpvCustomization.MatchesFile(f,size,hash))throw new IOException("Arquivo alterado após a conferência: "+f.Path);
                    string backup=DisabledPath(r,f.Path);Directory.CreateDirectory(Path.GetDirectoryName(backup));Safe.NoLinks(backup);
                    var change=new OwnedChange{Path=f.Path,Before=hash};r.Files.Add(change);Save(ledger,r);
                    File.Move(source,backup);change.Applied=true;Save(ledger,r);moved++;
                    if(failAfter>=0&&moved>=failAfter)throw new IOException("Falha de teste na desativação.");
                    report("Desativado: "+mod.Name);
                }
                r.Status="desativado";Save(ledger,r);return "Mod desativado e guardado com segurança. Desfazer última operação restaura os arquivos.";
            }catch{try{UndoDeactivation(r,ledger,report);}catch{}throw;}
        }
    }
    string DisabledPath(OnlineReceipt r,string relative) {
        if(r.BackupLayout=="flat-v1"||r.BackupLayout=="flat-v2")using(var sha=SHA256.Create()){
            string hash=Safe.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(relative)));
            string name=r.BackupLayout=="flat-v1"?"desativados/"+hash:"d/"+hash.Substring(0,32);
            return Safe.In(Data,"backups/"+r.Id+"/"+name+".bin");
        }
        return Safe.In(Data,"backups/"+r.Id+"/desativados/"+relative);
    }
    void EnsureUndoDependencies(OnlineReceipt receipt) {
        if(receipt.Operation=="deactivate"||receipt.Operation=="density"||receipt.Operation==EquipmentExOptions.Operation)return;
        var removing=new HashSet<string>(receipt.Files.Where(f=>f.Before==null&&f.Applied&&File.Exists(Safe.In(game,f.Path))&&Safe.Hash(Safe.In(game,f.Path))==f.After).Select(f=>f.Path),StringComparer.OrdinalIgnoreCase);
        var components=catalog.Packages.Where(m=>m.Files.Any(f=>removing.Contains(f.Path))).Select(m=>m.Id).ToList();
        // A verified rollback can leave stock Equipment-EX installed without WEAVE or its new dependencies.
        var equipment=catalog.Packages.SingleOrDefault(m=>m.Id=="mod-6945");
        var script=equipment==null?null:equipment.Files.SingleOrDefault(FpvCustomization.IsWeaveUpgrade);
        var replacement=script==null?null:receipt.Files.SingleOrDefault(f=>f.Path==script.Path&&f.Applied&&FpvCustomization.UpgradePair(script,f.Before,f.After));
        bool restoresStock=replacement!=null&&File.Exists(Safe.In(game,script.Path))&&Safe.Hash(Safe.In(game,script.Path))==replacement.After&&
            File.Exists(UpgradeBackup(receipt,script.Path))&&Safe.Hash(UpgradeBackup(receipt,script.Path))==replacement.Before&&
            equipment.Files.Where(f=>f!=script).All(f=>{
                string path=Safe.In(game,f.Path);
                if(f.Asset==script.Asset)return removing.Contains(f.Path)||!File.Exists(path)&&!Directory.Exists(path);
                return !removing.Contains(f.Path)&&File.Exists(path)&&Safe.Hash(path)==f.Sha256;
            });
        bool stockEquipment=restoresStock||EquipmentExOptions.StockActive(catalog,game);
        if(restoresStock)components.Remove(equipment.Id);
        foreach(var m in catalog.Packages) {
            if(!FixedData.Expand(catalog,new[]{m.Id},stockEquipment).Any(d=>d.Id!=m.Id&&components.Contains(d.Id)))continue;
            var scan=FixedData.Scan(m,game);
            if(scan.State=="Não verificável"||m.Files.Any(f=>!removing.Contains(f.Path)&&(File.Exists(Safe.In(game,f.Path))||Directory.Exists(Safe.In(game,f.Path)))))
                throw new IOException("Desfazer retiraria uma dependência de "+m.Name+". Desative esse mod primeiro; seus arquivos foram preservados.");
        }
    }
    string UndoDeactivation(OnlineReceipt r,string ledger,Action<string> report) {
        int pending=0;r.Status="desfazendo";Save(ledger,r);
        foreach(var f in r.Files.AsEnumerable().Reverse()) {
            Closed();string dest=Safe.In(game,f.Path),backup=DisabledPath(r,f.Path);
            // A verified quarantine file is safe to restore after a crash even before Applied was saved.
            if(File.Exists(dest)||Directory.Exists(dest)) {
                if(MatchesExtraBackup(f,dest))continue;
                pending++;report("Preservado; destino ocupado: "+f.Path);continue;
            }
            if(!MatchesExtraBackup(f,backup)){pending++;report("Backup ausente ou alterado; preservado: "+f.Path);continue;}
            Directory.CreateDirectory(Path.GetDirectoryName(dest));Safe.NoLinks(dest);
            if(f.Before==null)Directory.Move(backup,dest);
            else File.Move(backup,dest);
            report("Restaurado: "+f.Path);
        }
        r.Status=pending==0?"desfeito":"restauracao_pendente";Save(ledger,r);
        return pending==0?"Desativação desfeita. Arquivos e saves atuais preservados.":"Restauração com conflitos: "+pending+" destino(s) ocupado(s) ou backup(s) diferentes. Arquivos preservados; resolva o conflito e use Desfazer novamente.";
    }
}
