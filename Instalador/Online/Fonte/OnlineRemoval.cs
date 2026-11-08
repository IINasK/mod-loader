using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public sealed partial class FixedInstaller {
    public string DeactivateMany(IEnumerable<string> chosen,Action<string> report,int failAfter=-1,bool keepBackup=true) {
        if(chosen==null)throw new ArgumentNullException("chosen");
        var ids=new HashSet<string>(chosen,StringComparer.Ordinal);
        if(ids.Count==0)throw new IOException("Marque os mods que deseja remover.");
        if(ids.Any(id=>!catalog.Packages.Any(m=>m.Id==id)))throw new IOException("Mod fora da lista fixa.");
        bool preservesBase=catalog.Packages.Any(m=>ids.Contains(m.Id)&&m.Immutable);
        OnlineDiagnostics.Record("batch-remove.begin",null,keepBackup?"Remoção reversível solicitada.":"Desinstalação permanente solicitada.",new{Mods=ids.ToArray(),Root=game});
        var selected=new HashSet<string>(catalog.Packages.Where(m=>ids.Contains(m.Id)&&!m.Immutable).Select(m=>m.Id),StringComparer.Ordinal);
        string baseNote=preservesBase?" A base NightCoop/RED4ext foi preservada.":"";
        if(selected.Count==0)return "Nenhum complemento selecionado para remover."+baseNote;
        Closed();Safe.NoLinks(game);Directory.CreateDirectory(Data);
        using(var mutex=new FileStream(Safe.In(Data,"operacao.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
            EnsureNoIncomplete();
            var mods=FixedData.Expand(catalog,selected).Where(m=>selected.Contains(m.Id)).Reverse()
                .Select(m=>m.Id==NightCityDensity.ModId?NightCityDensity.Installed(catalog.Packages.Single(x=>x.Id==m.Id),game):m).ToList();
            var present=new List<FixedMod>();
            foreach(var mod in mods) {
                var scan=FixedData.Scan(mod,game,false);
                if(scan.IsConflict)throw new IOException(mod.Name+": há arquivos diferentes ou não verificáveis. Seus arquivos foram preservados; resolva a divergência antes de remover o lote.");
                if(scan.Correct+scan.Original>0)present.Add(mod);
            }
            if(present.Count==0)return "Os complementos selecionados não estão instalados. Nada foi alterado."+baseNote;
            var removing=new HashSet<string>(present.Select(m=>m.Id),StringComparer.Ordinal);
            var outside=catalog.Packages.Where(m=>!selected.Contains(m.Id)&&FixedData.Expand(catalog,new[]{m.Id}).Any(d=>removing.Contains(d.Id)))
                .Where(m=>{var scan=FixedData.Scan(m,game);return scan.Correct+scan.Original+scan.Different>0||scan.State=="Não verificável";}).Select(m=>m.Name).ToList();
            if(outside.Count>0)throw new IOException("A seleção deixaria dependências faltando para: "+String.Join(", ",outside)+". Selecione esses mods também ou remova-os primeiro. Nenhum arquivo do lote foi removido.");
            if(!keepBackup){
                var deletion=present.SelectMany(mod=>mod.Files.Where(file=>!file.UserData))
                    .GroupBy(file=>file.Path,StringComparer.OrdinalIgnoreCase).Select(group=>group.First()).ToArray();
                var protectedPaths=new HashSet<string>(catalog.Packages.Where(mod=>!selected.Contains(mod.Id))
                    .SelectMany(NightCityDensity.KnownFiles).Select(file=>file.Path),StringComparer.OrdinalIgnoreCase);
                foreach(var file in deletion){
                    Closed();
                    if(protectedPaths.Contains(file.Path))continue;
                    string target=Safe.In(game,file.Path);Safe.NoLinks(target);
                    if(!File.Exists(target))continue;
                    if(!FpvCustomization.MatchesFile(file,new FileInfo(target).Length,Safe.Hash(target)))
                        throw new IOException("Arquivo alterado após a conferência: "+file.Path);
                    File.Delete(target);
                    report("Retirado: "+file.Path);
                }
                return present.Count+" mod(s) desinstalado(s)."+baseNote;
            }
            var receipt=new OnlineReceipt{Id=Guid.NewGuid().ToString("N"),CatalogId=catalog.Id,CreatedUtc=DateTime.UtcNow.ToString("o"),Status="desativando",Operation="deactivate",BackupLayout="flat-v1",Mods=present.Select(m=>m.Id).ToList()};
            string ledger=Safe.In(Data,receipt.Id+".json");Save(ledger,receipt);
            try {
                BackupSaves(Safe.In(Data,"backups/"+receipt.Id+"/saves"));int moved=0;
                foreach(var mod in present)foreach(var file in mod.Files.Where(f=>!f.UserData)) {
                    Closed();string source=Safe.In(game,file.Path);
                    if(!File.Exists(source)){if(Directory.Exists(source))throw new IOException("Uma pasta ocupa o arquivo do mod: "+file.Path);continue;}
                    long size=new FileInfo(source).Length;string hash=Safe.Hash(source);
                    if(!FpvCustomization.MatchesFile(file,size,hash))throw new IOException("Arquivo alterado após a conferência: "+file.Path);
                    string backup=DisabledPath(receipt,file.Path);Directory.CreateDirectory(Path.GetDirectoryName(backup));Safe.NoLinks(backup);
                    var change=new OwnedChange{Path=file.Path,Before=hash};receipt.Files.Add(change);Save(ledger,receipt);
                    File.Move(source,backup);change.Applied=true;Save(ledger,receipt);moved++;
                    if(failAfter>=0&&moved>=failAfter)throw new IOException("Falha de teste na remoção em lote.");
                    report("Removido e guardado: "+mod.Name);
                }
                receipt.Status="desativado";Save(ledger,receipt);
                OnlineDiagnostics.Record("batch-remove.completed",null,"Lote guardado na quarentena.",new{Receipt=receipt.Id,Mods=receipt.Mods,Files=receipt.Files.Count,BasePreserved=preservesBase});
                return present.Count+" mod(s) removido(s) e guardado(s) com segurança. Desfazer última operação restaura este lote."+baseNote;
            }catch(Exception ex){OnlineDiagnostics.Record("batch-remove.failed",null,"Remoção interrompida; restauração segura será tentada.",new{Receipt=receipt.Id,Error=ex.GetType().Name,Files=receipt.Files.Count});try{UndoDeactivation(receipt,ledger,report);}catch{}throw;}
        }
    }
}
