using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;

public sealed class FixedDensityProfile {
    public string Id,Name;
    public List<FixedAsset> Assets;
    public List<FixedFile> Files;
}
public sealed class DensityProfileState {
    public string Id,Name,Detail,Version;
    public bool CanApply,Partial;
}
public static class NightCityDensity {
    public const string ModId="22-NightCityAlive-NoREDMOD";
    public const string LegacyPath="r6/tweaks/NightCityAlive/aigeneralsettings/zz-coop-low-density.yml";
    const string LegacyContent="# Perfil local de densidade baixa: preservar os demais encontros.\r\nAIGeneralSettings.ReactionSystem.aggressiveCiviliansLimit: 1\r\n";
    static readonly string[] Layers={"r6/tweaks/NightCityAlive/passengers/low-to-std.yml","r6/tweaks/NightCityAlive/aigeneralsettings/low-to-std.yml"};
    public static bool Supported(string id){return id=="low"||id=="medium"||id=="high";}
    public static IEnumerable<FixedAsset> Assets(FixedMod mod) {
        return mod.Assets.Concat(mod.DensityProfiles==null?Enumerable.Empty<FixedAsset>():mod.DensityProfiles.SelectMany(p=>p.Assets)).GroupBy(a=>a.Sha256).Select(g=>g.First());
    }
    public static FixedMod Default(FixedMod mod) {
        return mod.DensityProfiles==null?mod:Resolve(mod,"low");
    }
    public static FixedMod ForInstall(FixedMod mod,string game) {
        if(mod.Id!=ModId)return mod;
        var state=Read(mod,game);return Supported(state.Id)?Resolve(mod,state.Id):mod;
    }
    public static FixedMod Resolve(FixedMod mod,string id) {
        if(mod==null||mod.Id!=ModId||!Supported(id))throw new IOException("Densidade inválida.");
        var p=mod.DensityProfiles==null?null:mod.DensityProfiles.SingleOrDefault(x=>x.Id==id);
        if(p==null){if(id=="medium"&&mod.DensityProfiles==null)return mod;throw new IOException("Esta densidade ainda não possui receita auditada nesta edição. Nenhum arquivo foi alterado.");}
        var result=Safe.Json.Deserialize<FixedMod>(Safe.Json.Serialize(mod));
        result.Files=p.Files;result.Assets=p.Assets;
        return result;
    }
    static FixedMod Legacy(FixedMod mod) {
        var standard=Resolve(mod,"medium");
        if(!Layers.All(p=>standard.Files.Any(f=>f.Path==p)))return null;
        var legacy=Safe.Json.Deserialize<FixedMod>(Safe.Json.Serialize(standard));
        legacy.Files=standard.Files.Where(f=>!Layers.Contains(f.Path)).ToList();
        byte[] bytes=Encoding.UTF8.GetBytes(LegacyContent);string hash;
        using(var sha=SHA256.Create())hash=Safe.Hex(sha.ComputeHash(bytes));
        legacy.Files.Add(new FixedFile{Path=LegacyPath,Entry="",Asset="",Size=bytes.Length,Sha256=hash});
        return legacy;
    }
    public static IEnumerable<FixedFile> KnownFiles(FixedMod mod) {
        if(mod.Id!=ModId)return mod.Files;
        var all=mod.Files.Concat(mod.DensityProfiles==null?Enumerable.Empty<FixedFile>():mod.DensityProfiles.SelectMany(p=>p.Files)).ToList();
        var legacy=Legacy(mod);if(legacy!=null)all.AddRange(legacy.Files);
        return all.GroupBy(f=>f.Path+"\n"+f.Sha256,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).ToList();
    }
    static bool Match(FixedMod candidate,List<FixedFile> all,string game) {
        foreach(var f in candidate.Files) {
            string p=Safe.In(game,f.Path);
            if(!File.Exists(p)||new FileInfo(p).Length!=f.Size||Safe.Hash(p)!=f.Sha256)return false;
        }
        foreach(string path in all.Select(f=>f.Path).Distinct(StringComparer.OrdinalIgnoreCase)) {
            if(candidate.Files.Any(f=>f.Path.Equals(path,StringComparison.OrdinalIgnoreCase)))continue;
            string p=Safe.In(game,path);if(File.Exists(p)||Directory.Exists(p))return false;
        }
        return true;
    }
    public static DensityProfileState Read(FixedMod mod,string game) {
        if(mod==null||mod.Id!=ModId)throw new IOException("Night City Alive não encontrado na lista.");
        var all=KnownFiles(mod).ToList();Safe.NoLinks(game);
        var candidates=mod.DensityProfiles==null?new[]{"medium"}:new[]{"low","medium","high"};
        foreach(string id in candidates)if(Match(Resolve(mod,id),all,game))return new DensityProfileState{Id=id,Version=mod.Version,Name=id=="low"?"Baixa (recomendada)":id=="medium"?"Média":"Alta",Detail="Pacote completo conferido por hashes. Reinicie o jogo após trocar.",CanApply=true};
        var legacy=Legacy(mod);
        if(legacy!=null&&Match(legacy,all,game))return new DensityProfileState{Id="legacy-low",Version=mod.Version,Name="Baixa personalizada",Detail="Perfil local anterior reconhecido. Aplicar substitui pelo pacote oficial escolhido.",CanApply=true};
        bool absent=all.All(f=>{string p=Safe.In(game,f.Path);return !File.Exists(p)&&!Directory.Exists(p);});
        if(!absent){
            var partial=new List<string>();
            foreach(string id in candidates){
                var candidate=Resolve(mod,id);bool fits=true;int present=0;
                foreach(string path in all.Select(f=>f.Path).Distinct(StringComparer.OrdinalIgnoreCase)){
                    string p=Safe.In(game,path);if(Directory.Exists(p)){fits=false;break;}if(!File.Exists(p))continue;
                    var f=candidate.Files.SingleOrDefault(x=>x.Path.Equals(path,StringComparison.OrdinalIgnoreCase));
                    if(f==null||new FileInfo(p).Length!=f.Size||Safe.Hash(p)!=f.Sha256){fits=false;break;}present++;
                }
                if(fits&&present>0&&present<candidate.Files.Count)partial.Add(id);
            }
            if(partial.Count==1)return new DensityProfileState{Id=partial[0],Name=partial[0]=="low"?"Baixa (recomendada)":partial[0]=="medium"?"Média":"Alta",Detail="Perfil parcial identificado. Use Instalar para reparar a mesma densidade antes de trocar.",CanApply=false,Partial=true};
        }
        return absent?new DensityProfileState{Id="absent",Name="Não instalado",Detail="Baixa é o padrão recomendado. Os requisitos da base precisam estar instalados.",CanApply=true}:new DensityProfileState{Id="conflict",Name="Parcial ou alterado",Detail="Há arquivos diferentes ou uma mistura de densidades. Restaure o backup ou resolva o conflito antes de trocar.",CanApply=false};
    }
    public static FixedMod Installed(FixedMod mod,string game) {
        var state=Read(mod,game);
        if(state.Id=="legacy-low")return Legacy(mod);
        if(Supported(state.Id))return Resolve(mod,state.Id);
        if(state.Id=="absent")return Default(mod);
        throw new IOException(state.Detail);
    }
    public static ModScan Scan(FixedMod mod,string game) {
        var s=new ModScan{Id=mod.Id};
        try {
            var state=Read(mod,game);
            if(state.Partial){var files=Resolve(mod,state.Id).Files;s.Correct=files.Count(f=>File.Exists(Safe.In(game,f.Path)));s.Missing=files.Count-s.Correct;s.State="Parcial";s.Detail=(String.IsNullOrEmpty(state.Version)?"Versão não identificada. ":"Versão reconhecida: "+state.Version+". ")+"Densidade: "+state.Name+". "+state.Detail;}
            else if(Supported(state.Id)||state.Id=="legacy-low"){s.State="Instalado correto";s.Correct=Installed(mod,game).Files.Count;s.Detail=(String.IsNullOrEmpty(state.Version)?"Versão não identificada. ":"Versão reconhecida: "+state.Version+". ")+"Densidade: "+state.Name+". "+state.Detail;}
            else if(state.Id=="absent"){s.State="Não instalado";s.Missing=Default(mod).Files.Count;s.Detail="Densidade padrão: Baixa (recomendada).";}
            else{s.State="Diferente/alterado";s.Different=1;s.Detail=state.Detail;}
        }catch(IOException){s.State="Não verificável";s.Detail="Não foi possível ler a densidade com segurança.";}
        catch(UnauthorizedAccessException){s.State="Não verificável";s.Detail="Sem permissão para conferir a densidade.";}
        return s;
    }
    public static void Validate(FixedCatalog catalog) {
        foreach(var mod in catalog.Packages.Where(m=>m.DensityProfiles!=null)){
            if(mod.Id!=ModId||mod.DensityProfiles.Count!=3||mod.Immutable||mod.RequiresRedmod||mod.Version!="2.3")throw new IOException("Variantes de densidade inválidas.");
            var ids=new HashSet<string>();
            foreach(var p in mod.DensityProfiles){
                if(p==null||!Supported(p.Id)||!ids.Add(p.Id)||p.Assets==null||p.Assets.Count!=1||p.Files==null||p.Files.Count==0||p.Files.Count>100)throw new IOException("Receita de densidade inválida.");
                var a=p.Assets[0];int fid=p.Id=="low"?164034:p.Id=="medium"?164033:164031;
                if(a==null||a.ModId!=10395||a.FileId!=fid||a.Format!="zip"||!FixedData.HashValid(a.Sha256)||a.Size<=0||a.Size>1024*1024||a.Url!="https://www.nexusmods.com/cyberpunk2077/mods/10395?tab=files&file_id="+fid)throw new IOException("Fonte de densidade inválida.");
                var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(var f in p.Files){
                    if(f==null||f.Asset!=a.Id||!paths.Add(f.Path)||!Managed(f.Path)||f.Path==LegacyPath||!FixedData.HashValid(f.Sha256)||f.Size<0||f.Size>1024*1024||catalog.Packages.Any(m=>m.Id!=ModId&&m.Files.Any(x=>x.Path.Equals(f.Path,StringComparison.OrdinalIgnoreCase))))throw new IOException("Caminho ou colisão na densidade.");
                    Safe.In(Path.GetTempPath(),f.Path);Safe.In(Path.GetTempPath(),f.Entry);
                }
            }
        }
    }
    static bool Managed(string p){return p!=null&&(p.StartsWith("r6/scripts/NightCityAlive/",StringComparison.Ordinal)||p.StartsWith("r6/tweaks/NightCityAlive/",StringComparison.Ordinal));}
    public static void ValidateReceipt(FixedCatalog c,OnlineReceipt r) {
        var mod=c.Packages.SingleOrDefault(m=>m.Id==ModId);
        if(mod==null||r.Mods.Count!=1||r.Mods[0]!=ModId||!(Supported(r.DensityBefore)||r.DensityBefore=="absent"||r.DensityBefore=="legacy-low")||!Supported(r.DensityAfter))throw new IOException("Registro de densidade inválido.");
        var allowed=KnownFiles(mod).ToList();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var f in r.Files){
            if(f==null||!seen.Add(f.Path)||(f.Before==null&&f.After==null)||f.Before==f.After||!allowed.Any(x=>x.Path==f.Path&&(f.Before==null||x.Sha256==f.Before))||!allowed.Any(x=>x.Path==f.Path&&(f.After==null||x.Sha256==f.After)))throw new IOException("Registro de densidade não corresponde às receitas.");
        }
    }
}
public sealed partial class FixedInstaller {
    string DensityBackup(OnlineReceipt r,string path){return Safe.In(Data,"backups/"+r.Id+"/densidade/"+path);}
    public string ApplyDensityProfile(string id,Action<string> report,int failAfter=-1) {
        var mod=catalog.Packages.SingleOrDefault(m=>m.Id==NightCityDensity.ModId);
        var target=NightCityDensity.Resolve(mod,id);
        FixedData.RequireInstallable(new[]{mod});FixedData.ValidateGame(game,new[]{mod},catalog.GameFileVersion);Closed();
        foreach(var d in FixedData.Expand(catalog,mod.Dependencies))if(!FixedData.Scan(d,game).IsCorrect)throw new IOException("Instale primeiro a base e os requisitos pelo botão Baixar e instalar: "+d.Name);
        Directory.CreateDirectory(Data);
        using(var mutex=new FileStream(Safe.In(Data,"operacao.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
            EnsureNoIncomplete();var state=NightCityDensity.Read(mod,game);if(!state.CanApply)throw new IOException(state.Detail);
            if(state.Id==id)return "A densidade "+state.Name+" já está instalada. Nada foi alterado.";
            var before=state.Id=="absent"?new List<FixedFile>():NightCityDensity.Installed(mod,game).Files;
            string operationId=Guid.NewGuid().ToString("N"),stage=Safe.In(cache.Root,"preparacao/"+operationId);Directory.CreateDirectory(stage);
            long bytes=target.Files.Sum(f=>f.Size)+before.Sum(f=>f.Size);
            if(new DriveInfo(Path.GetPathRoot(game)).AvailableFreeSpace<bytes*2+100L*1024*1024)throw new IOException("Espaço insuficiente para trocar a densidade.");
            cache.Stage(target,stage,report);
            var r=new OnlineReceipt{Id=operationId,CatalogId=catalog.Id,Status="preparando",CreatedUtc=DateTime.UtcNow.ToString("o"),Operation="density",DensityBefore=state.Id,DensityAfter=id,Mods=new List<string>{mod.Id}};
            foreach(string path in before.Select(f=>f.Path).Union(target.Files.Select(f=>f.Path),StringComparer.OrdinalIgnoreCase)){
                var previous=before.SingleOrDefault(f=>f.Path==path);var next=target.Files.SingleOrDefault(f=>f.Path==path);
                if(previous!=null&&next!=null&&previous.Sha256==next.Sha256)continue;
                r.Files.Add(new OwnedChange{Path=path,Before=previous==null?null:previous.Sha256,After=next==null?null:next.Sha256});
            }
            string ledger=Safe.In(Data,r.Id+".json");Save(ledger,r);
            try {
                BackupSaves(Safe.In(Data,"backups/"+r.Id+"/saves"));r.Status="instalando";Save(ledger,r);int written=0;
                foreach(var f in r.Files){
                    Closed();string dest=Safe.In(game,f.Path);
                    if(f.Before==null){if(File.Exists(dest)||Directory.Exists(dest))throw new IOException("Destino ocupado após conferência: "+f.Path);}
                    else{
                        if(!File.Exists(dest)||Safe.Hash(dest)!=f.Before)throw new IOException("Arquivo alterado após conferência: "+f.Path);
                        string backup=DensityBackup(r,f.Path);Directory.CreateDirectory(Path.GetDirectoryName(backup));Safe.NoLinks(backup);
                        File.Move(dest,backup);if(Safe.Hash(backup)!=f.Before)throw new IOException("Backup divergente.");
                    }
                    if(f.After!=null){
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));Safe.NoLinks(dest);
                        if(File.Exists(dest)||Directory.Exists(dest))throw new IOException("Destino ocupado: "+f.Path);
                        string tmp=dest+".coop-"+r.Id+".tmp";
                        try{File.Copy(Safe.In(stage,f.Path),tmp,false);if(Safe.Hash(tmp)!=f.After)throw new IOException("Cópia divergente.");Safe.NoLinks(dest);File.Move(tmp,dest);}
                        finally{if(File.Exists(tmp)){Safe.NoLinks(tmp);File.Delete(tmp);}}
                    }
                    f.Applied=true;Save(ledger,r);written++;report("Aplicando densidade: "+f.Path);
                    if(failAfter>=0&&written>=failAfter)throw new IOException("Falha de teste na troca de densidade.");
                }
                if(NightCityDensity.Read(mod,game).Id!=id)throw new IOException("A densidade final não conferiu.");
                r.Status="instalado";Save(ledger,r);
                return "Densidade "+NightCityDensity.Read(mod,game).Name+" instalada. Versão anterior guardada para desfazer. Use a mesma escolha nos dois PCs e reinicie o jogo.";
            }catch{try{UndoDensity(r,ledger,report);}catch{}throw;}
        }
    }
    string UndoDensity(OnlineReceipt r,string ledger,Action<string> report) {
        r.Status="desfazendo";Save(ledger,r);int pending=0;
        foreach(var f in r.Files.AsEnumerable().Reverse()){
            if(f.Reverted)continue;
            Closed();string dest=Safe.In(game,f.Path),backup=DensityBackup(r,f.Path);
            bool exists=File.Exists(dest);
            if(Directory.Exists(dest)){pending++;continue;}
            if(!exists&&f.Applied&&f.After!=null&&!f.UndoStarted){pending++;report("Remoção posterior preservada: "+f.Path);continue;}
            if(exists&&!f.Applied&&f.Before==null){pending++;report("Autoria incerta; arquivo preservado: "+f.Path);continue;}
            if(f.Before!=null&&exists&&Safe.Hash(dest)==f.Before){f.Reverted=true;Save(ledger,r);continue;}
            if(exists){
                if(f.After==null||Safe.Hash(dest)!=f.After){pending++;report("Alteração posterior preservada: "+f.Path);continue;}
                if(f.Before!=null&&(!File.Exists(backup)||Safe.Hash(backup)!=f.Before)){pending++;continue;}
                f.UndoStarted=true;Save(ledger,r);
                File.Delete(dest);
            }
            if(f.Before!=null){
                if(!File.Exists(backup)||Safe.Hash(backup)!=f.Before){pending++;continue;}
                f.UndoStarted=true;Save(ledger,r);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));Safe.NoLinks(dest);File.Move(backup,dest);
            }
            f.Reverted=true;Save(ledger,r);
        }
        r.Status=pending==0?"desfeito":"restauracao_pendente";Save(ledger,r);
        return pending==0?"Troca de densidade desfeita. Perfil anterior restaurado; arquivos alheios preservados.":"Restauração com conflitos: arquivos alterados depois foram preservados. Resolva os conflitos antes de continuar.";
    }
}
