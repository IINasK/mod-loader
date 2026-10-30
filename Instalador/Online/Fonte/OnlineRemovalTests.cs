using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

public static class OnlineRemovalTests {
    static readonly Action<string> Quiet=s=>{};
    const string Personal="bin/x64/plugins/cyber_engine_tweaks/mods/RemovalFixture/Data/user.lua";
    const string Preset="bin/x64/plugins/cyber_engine_tweaks/mods/RemovalFixture/Character Presets/user.preset";
    sealed class Fixture {public string Root,Game;public FixedCatalog Catalog;public DownloadCache Cache;public FixedInstaller Engine;}
    static string Hash(string text){using(var sha=SHA256.Create())return Safe.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(text)));}
    static void Text(string root,string path,string text){string file=Safe.In(root,path);Directory.CreateDirectory(Path.GetDirectoryName(file));File.WriteAllText(file,text,new UTF8Encoding(false));}
    static FixedFile Spec(string path,string text){return new FixedFile{Asset="a0",Entry=path,Path=path,Sha256=Hash(text),Size=Encoding.UTF8.GetByteCount(text)};}
    static string Content(FixedFile file){return "fixture "+file.Path;}
    static FixedAsset Archive(string root,string name,List<FixedFile> files,Func<FixedFile,string> content,int fileId=0){
        string path=Safe.In(root,name+".zip");using(var zip=ZipFile.Open(path,ZipArchiveMode.Create))foreach(var f in files){var entry=zip.CreateEntry(f.Path);using(var writer=new StreamWriter(entry.Open(),new UTF8Encoding(false)))writer.Write(content(f));}
        return new FixedAsset{Id="a0",Name=name+".zip",Url=fileId==0?"https://github.com/example/project/releases/download/test/fixture.zip":"https://www.nexusmods.com/cyberpunk2077/mods/10395?tab=files&file_id="+fileId,ModId=fileId==0?0:10395,FileId=fileId,Format="zip",Size=new FileInfo(path).Length,Sha256=Safe.Hash(path)};
    }
    static Fixture New(string parent,bool install=true){
        string root=Path.Combine(parent,"online-testes-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string game=Safe.In(root,"g");Directory.CreateDirectory(game);
        Text(game,".fixture-only","test");string exe=Safe.In(game,"bin/x64/Cyberpunk2077.exe");Directory.CreateDirectory(Path.GetDirectoryName(exe));File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","FixtureGame.exe"),exe);Text(game,"archive/pc/ep1/ep1.addcont_keystone","");
        string[] basePaths={"bin/x64/winmm.dll","red4ext/RED4ext.dll","red4ext/LICENSE.txt","red4ext/THIRD_PARTY_LICENSES.txt","red4ext/plugins/NightCoop/NightCoop.dll"};
        var baseFiles=basePaths.Select(p=>Spec(p,"fixture "+p)).ToList();var depFiles=new[]{"r6/scripts/removal-dependency.reds"}.Select(p=>Spec(p,"fixture "+p)).ToList();
        var addonFiles=new[]{"bin/x64/plugins/cyber_engine_tweaks/mods/RemovalFixture/init.lua","archive/pc/mod/removal-addon.archive"}.Select(p=>Spec(p,"fixture "+p)).ToList();
        var asset=Archive(root,"fixture",baseFiles.Concat(depFiles).Concat(addonFiles).ToList(),Content);
        var catalog=new FixedCatalog{Schema=1,Id="removal-fixture",GameVersion="2.5",GameFileVersion="2.5.0.0",Packages=new List<FixedMod>{
            new FixedMod{Id="NightCoop",Name="Base fixture",Version="fixture",GameVersion="2.5",RequiresPL=true,Immutable=true,Dependencies=new string[0],Assets=new List<FixedAsset>{asset},Files=baseFiles},
            new FixedMod{Id="Dependency",Name="Dependência fixture",Version="fixture",GameVersion="2.5",RequiresPL=true,Dependencies=new[]{"NightCoop"},Assets=new List<FixedAsset>{asset},Files=depFiles},
            new FixedMod{Id="Addon",Name="Complemento fixture",Version="fixture",GameVersion="2.5",RequiresPL=true,Dependencies=new[]{"Dependency"},Assets=new List<FixedAsset>{asset},Files=addonFiles}}};
        FixedData.Validate(catalog);var cache=new DownloadCache(Safe.In(root,"cache"));cache.Import(Safe.In(root,asset.Name),asset);var engine=new FixedInstaller(catalog,cache,game,null,true);
        if(install)engine.Install(new[]{"Addon"},true,Quiet);else foreach(var f in baseFiles)Text(game,f.Path,Content(f));
        Text(game,Personal,"user-data");Text(game,Preset,"user-preset");Text(game,"r6/scripts/unmanaged.reds","unmanaged");
        return new Fixture{Root=root,Game=game,Catalog=catalog,Cache=cache,Engine=engine};
    }
    static List<OnlineReceipt> Receipts(Fixture fixture){
        string data=Safe.In(fixture.Game,".CoopModsOnline");if(!Directory.Exists(data))return new List<OnlineReceipt>();
        return Directory.GetFiles(data,"*.json").Select(path=>{var envelope=Safe.Json.Deserialize<ReceiptEnvelope>(File.ReadAllText(path));return Safe.Json.Deserialize<OnlineReceipt>(Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(envelope.Protected),null,DataProtectionScope.CurrentUser)));}).OrderByDescending(r=>r.CreatedUtc).ToList();
    }
    static FixedMod Mod(Fixture f,string id){return f.Catalog.Packages.Single(m=>m.Id==id);}
    static bool Correct(Fixture f,string id){return FixedData.Scan(Mod(f,id),f.Game).IsCorrect;}
    static bool PersonalIntact(Fixture f){return File.ReadAllText(Safe.In(f.Game,Personal))=="user-data"&&File.ReadAllText(Safe.In(f.Game,Preset))=="user-preset"&&File.ReadAllText(Safe.In(f.Game,"r6/scripts/unmanaged.reds"))=="unmanaged";}
    static string Snapshot(Fixture f){return String.Join("\n",f.Catalog.Packages.SelectMany(m=>m.Files).Select(x=>x.Path).Concat(new[]{Personal,Preset,"r6/scripts/unmanaged.reds"}).OrderBy(p=>p).Select(p=>p+"="+(File.Exists(Safe.In(f.Game,p))?Safe.Hash(Safe.In(f.Game,p)):"missing")));}
    static string Backup(Fixture f,OnlineReceipt receipt,string path){return Safe.In(f.Game,".CoopModsOnline/backups/"+receipt.Id+"/desativados/"+Hash(path)+".bin");}
    static void ForgeReceipt(Fixture fixture,string[] mods,FixedFile file){
        var receipt=new OnlineReceipt{Id=Guid.NewGuid().ToString("N"),CatalogId=fixture.Catalog.Id,CreatedUtc=DateTime.UtcNow.AddMinutes(1).ToString("o"),Status="desativado",Operation="deactivate",BackupLayout="flat-v1",Mods=mods.ToList(),Files=new List<OwnedChange>{new OwnedChange{Path=file.Path,Before=file.Sha256,Applied=true}}};
        byte[] plain=Encoding.UTF8.GetBytes(Safe.Json.Serialize(receipt));Safe.WriteJson(Safe.In(fixture.Game,".CoopModsOnline/"+receipt.Id+".json"),new ReceiptEnvelope{Protected=Convert.ToBase64String(ProtectedData.Protect(plain,null,DataProtectionScope.CurrentUser))});
    }
    static FixedMod AddDensity(Fixture fixture,string profile){
        const string shared="r6/scripts/NightCityAlive/removal-fixture.reds";string[] layers={"r6/tweaks/NightCityAlive/passengers/low-to-std.yml","r6/tweaks/NightCityAlive/aigeneralsettings/low-to-std.yml"};
        var profiles=new List<FixedDensityProfile>();string[] ids={"low","medium","high"};int[] fileIds={164034,164033,164031};
        for(int i=0;i<ids.Length;i++){
            string id=ids[i];var paths=new List<string>{shared};if(id!="low")paths.AddRange(layers);if(id=="high")paths.Add("r6/tweaks/NightCityAlive/removal-high.yml");
            var files=paths.Select(p=>Spec(p,"fixture "+p)).ToList();var asset=Archive(fixture.Root,"density-"+id,files,Content,fileIds[i]);profiles.Add(new FixedDensityProfile{Id=id,Name=id,Assets=new List<FixedAsset>{asset},Files=files});
        }
        var medium=profiles.Single(p=>p.Id=="medium");var mod=new FixedMod{Id=NightCityDensity.ModId,Name="NCA fixture",Version="2.3",GameVersion="2.5",RequiresPL=true,Dependencies=new[]{"NightCoop"},Assets=medium.Assets,Files=medium.Files,DensityProfiles=profiles};fixture.Catalog.Packages.Add(mod);FixedData.Validate(fixture.Catalog);
        var filesToWrite=profiles.Single(p=>p.Id==(profile=="legacy-low"?"low":profile)).Files;foreach(var file in filesToWrite)Text(fixture.Game,file.Path,Content(file));
        if(profile=="legacy-low")Text(fixture.Game,NightCityDensity.LegacyPath,"# Perfil local de densidade baixa: preservar os demais encontros.\r\nAIGeneralSettings.ReactionSystem.aggressiveCiviliansLimit: 1\r\n");
        return mod;
    }
    public static void Run(string parent){
        var passed=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("TESTE REMOCAO FALHOU: "+name);passed.Add(name);};
        Action<Action,string> refuse=(action,name)=>{bool rejected=false;try{action();}catch(IOException){rejected=true;}check(rejected,name);};
        var f=New(parent);string before=Snapshot(f);int receipts=Receipts(f).Count;
        refuse(()=>f.Engine.DeactivateMany(new[]{"Dependency"},Quiet),"Remover dependencia isolada protege complemento instalado fora da selecao");
        check(Snapshot(f)==before&&Receipts(f).Count==receipts,"Falha de pre-validacao nao move arquivos nem cria recibo de remocao");
        refuse(()=>f.Engine.DeactivateMany(new[]{"Unknown"},Quiet),"IDs desconhecidos nao sao transformados em remocao generica");
        refuse(()=>f.Engine.DeactivateMany(new string[0],Quiet),"Selecao vazia nao executa remocao");
        string result=f.Engine.DeactivateMany(new[]{"NightCoop","Dependency","Addon","Addon"},Quiet);var receipt=Receipts(f).First();
        check(result.Contains("2 mod(s)")&&result.Contains("base NightCoop/RED4ext")&&Correct(f,"NightCoop"),"Remover todos deduplica selecao e preserva base imutavel");
        check(!Correct(f,"Dependency")&&!Correct(f,"Addon")&&receipt.Mods.Count==2&&!receipt.Mods.Contains("NightCoop")&&receipt.Operation=="deactivate"&&receipt.BackupLayout=="flat-v1"&&receipt.Status=="desativado"&&receipt.Files.Count==3,"Lote dependente/dependencia usa um recibo e quarentena flat de tres arquivos");
        check(receipt.Files.All(change=>change.Applied&&File.Exists(Backup(f,receipt,change.Path))&&Safe.Hash(Backup(f,receipt,change.Path))==change.Before),"Todos os arquivos retirados possuem backup verificado pelo hash");
        check(PersonalIntact(f)&&f.Cache.Contains(Mod(f,"Addon").Assets[0]),"Remocao preserva Data, Character Presets, arquivos alheios e pacote em cache");
        f.Engine.Undo(Quiet);check(Snapshot(f)==before&&Receipts(f).First().Status=="desfeito","Desfazer unico restaura todo o lote dependente/dependencia");
        receipts=Receipts(f).Count;f.Engine.DeactivateMany(new[]{"NightCoop"},Quiet);check(Snapshot(f)==before&&Receipts(f).Count==receipts,"Selecionar somente a base e no-op sem recibo novo");
        var baseOnly=New(parent,false);baseOnly.Engine.DeactivateMany(new[]{"NightCoop"},Quiet);check(Correct(baseOnly,"NightCoop")&&!Directory.Exists(Safe.In(baseOnly.Game,".CoopModsOnline")),"Base isolada nao cria diretorio de operacao nem altera seus cinco arquivos");
        var partial=New(parent);string absent=Mod(partial,"Addon").Files[1].Path;File.Delete(Safe.In(partial.Game,absent));before=Snapshot(partial);partial.Engine.DeactivateMany(new[]{"Dependency","Addon"},Quiet);
        check(Receipts(partial).First().Files.Count==2&&PersonalIntact(partial),"Lote parcial retira somente arquivos presentes e conhecidos");
        partial.Engine.Undo(Quiet);check(Snapshot(partial)==before&&!File.Exists(Safe.In(partial.Game,absent)),"Desfazer lote parcial nao inventa arquivo que ja estava ausente");
        var changed=New(parent);Text(changed.Game,Mod(changed,"Dependency").Files[0].Path,"user-change");before=Snapshot(changed);receipts=Receipts(changed).Count;
        refuse(()=>changed.Engine.DeactivateMany(new[]{"Addon","Dependency"},Quiet),"Arquivo alterado em qualquer mod do lote impede mover os anteriores");
        check(Snapshot(changed)==before&&Receipts(changed).Count==receipts,"Preflight completo preserva alteracoes e todos os demais mods selecionados");
        var failure=New(parent);before=Snapshot(failure);refuse(()=>failure.Engine.DeactivateMany(new[]{"Dependency","Addon"},Quiet,1),"Falha apos primeira movimentacao e reproduzida sem tocar jogo real");
        check(Snapshot(failure)==before&&Receipts(failure).First().Status=="desfeito","Rollback da falha parcial restaura lote e encerra recibo incompleto");
        var race=New(parent);bool edited=false;refuse(()=>race.Engine.DeactivateMany(new[]{"Dependency","Addon"},message=>{if(!edited){edited=true;Text(race.Game,Mod(race,"Dependency").Files[0].Path,"later-edit");}}),"Mudanca depois do preflight e detectada imediatamente antes de mover o arquivo");
        check(Correct(race,"Addon")&&File.ReadAllText(Safe.In(race.Game,Mod(race,"Dependency").Files[0].Path))=="later-edit"&&PersonalIntact(race),"Rollback restaura arquivos ja movidos e preserva edicao concorrente");
        var occupied=New(parent);occupied.Engine.DeactivateMany(new[]{"Dependency","Addon"},Quiet);string occupiedPath=Mod(occupied,"Addon").Files[1].Path;Text(occupied.Game,occupiedPath,"posterior");occupied.Engine.Undo(Quiet);
        check(File.ReadAllText(Safe.In(occupied.Game,occupiedPath))=="posterior"&&Receipts(occupied).First().Status=="restauracao_pendente"&&Correct(occupied,"Dependency"),"Undo preserva destino alterado depois e restaura restante seguro do lote");
        File.Delete(Safe.In(occupied.Game,occupiedPath));occupied.Engine.Undo(Quiet);check(Correct(occupied,"Addon")&&Receipts(occupied).First().Status=="desfeito","Undo pode concluir lote apos conflito resolvido sem perder quarentena");
        var tampered=New(parent);tampered.Engine.DeactivateMany(new[]{"Dependency","Addon"},Quiet);receipt=Receipts(tampered).First();var first=receipt.Files.First();File.WriteAllText(Backup(tampered,receipt,first.Path),"corrupted");tampered.Engine.Undo(Quiet);
        check(!File.Exists(Safe.In(tampered.Game,first.Path))&&Receipts(tampered).First().Status=="restauracao_pendente","Backup alterado nao e restaurado sobre jogo e permanece para investigacao");
        foreach(string profile in new[]{"high","legacy-low"}){
            var density=New(parent);var mod=AddDensity(density,profile);check(NightCityDensity.Read(mod,density.Game).Id==profile,"Fixture de densidade "+profile+" reconhecida antes da remocao");
            string initial=Snapshot(density);density.Engine.DeactivateMany(density.Catalog.Packages.Select(m=>m.Id),Quiet);check(Correct(density,"NightCoop")&&NightCityDensity.Read(mod,density.Game).Id=="absent"&&PersonalIntact(density),"Remocao em lote preserva base/dados e retira densidade "+profile+" atualmente instalada");
            density.Engine.Undo(Quiet);check(Snapshot(density)==initial&&NightCityDensity.Read(mod,density.Game).Id==profile,"Undo de lote restaura variante "+profile+" sem substituir pela Baixa padrao");
        }
        bool forgedPreserved=true;
        foreach(string kind in new[]{"mods duplicados","mod desconhecido","base na selecao","selecao vazia","arquivo da base","arquivo de dependencia nao selecionada","arquivo alheio"}){
            var forged=New(parent);string[] mods={"Addon","Dependency"};var file=Mod(forged,"Addon").Files[0];
            if(kind=="mods duplicados")mods=new[]{"Addon","Addon"};
            if(kind=="mod desconhecido")mods=new[]{"Addon","Unknown"};
            if(kind=="base na selecao")mods=new[]{"Addon","NightCoop"};
            if(kind=="selecao vazia")mods=new string[0];
            if(kind=="arquivo da base")file=Mod(forged,"NightCoop").Files[0];
            if(kind=="arquivo de dependencia nao selecionada"){mods=new[]{"Addon"};file=Mod(forged,"Dependency").Files[0];}
            if(kind=="arquivo alheio")file=Spec("r6/scripts/unmanaged.reds","unmanaged");
            before=Snapshot(forged);ForgeReceipt(forged,mods,file);refuse(()=>forged.Engine.Undo(Quiet),"Recibo DPAPI forjado e recusado: "+kind);forgedPreserved&=Snapshot(forged)==before&&PersonalIntact(forged);
        }
        check(forgedPreserved,"Recibos forjados nao movem nem alteram arquivos da base, dependencias ou terceiros");
        string evidence=Path.Combine(parent,"removal-"+Guid.NewGuid().ToString("N").Substring(0,12));Directory.CreateDirectory(evidence);Safe.WriteJson(Path.Combine(evidence,"resultado-remocao.json"),new{Passed=passed.Count,Tests=passed,FixtureOnly=true,RealGameModified=false,SavesRead=false,PayloadExecuted=false});File.WriteAllText(Path.Combine(parent,"ultimo-teste-remocao.txt"),evidence);
    }
}
