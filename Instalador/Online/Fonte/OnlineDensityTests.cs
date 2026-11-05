using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

public static class OnlineDensityTests {
    const string ModId="22-NightCityAlive-NoREDMOD";
    const string Shared="r6/scripts/NightCityAlive/Fixture.reds";
    const string Changed="r6/tweaks/NightCityAlive/fixture-density.yml";
    static readonly Action<string> Quiet=s=>{};
    sealed class Fixture {
        public string Game; public FixedMod Mod; public FixedInstaller Engine;
    }
    static void Text(string root,string relative,string value) {
        string path=Safe.In(root,relative);Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path,value,new UTF8Encoding(false));
    }
    static string HashText(string value) {using(var h=SHA256.Create())return Safe.Hex(h.ComputeHash(Encoding.UTF8.GetBytes(value)));}
    static FixedFile FileSpec(string path,string text) {return new FixedFile{Asset="a0",Entry=path,Path=path,Sha256=HashText(text),Size=Encoding.UTF8.GetByteCount(text)};}
    static string Only(string profile) {return "r6/tweaks/NightCityAlive/fixture-"+profile+".yml";}
    static string Contents(string path,string profile) {return path==Shared?"// fixture shared\n":path==Changed?"Fixture.Density: "+profile+"\n":"Fixture.Only: "+profile+"\n";}
    static FixedAsset Archive(string root,string name,int fileId,List<FixedFile> files,Func<FixedFile,string> contents) {
        string zip=Safe.In(root,name+".zip");
        using(var z=ZipFile.Open(zip,ZipArchiveMode.Create))foreach(var f in files) {
            var entry=z.CreateEntry(f.Entry);entry.LastWriteTime=new DateTimeOffset(1980,1,1,0,0,0,TimeSpan.Zero);
            using(var writer=new StreamWriter(entry.Open(),new UTF8Encoding(false)))writer.Write(contents(f));
        }
        return new FixedAsset{Id="a0",Name=name+".zip",Format="zip",ModId=fileId==0?0:10395,FileId=fileId,
            Url=fileId==0?"https://nightcoop.net/fixture.zip":"https://www.nexusmods.com/cyberpunk2077/mods/10395?tab=files&file_id="+fileId,
            Size=new FileInfo(zip).Length,Sha256=Safe.Hash(zip)};
    }
    static FixedCatalog Catalog(string root,DownloadCache cache) {
        string[] paths={"bin/x64/winmm.dll","red4ext/RED4ext.dll","red4ext/LICENSE.txt","red4ext/THIRD_PARTY_LICENSES.txt","red4ext/plugins/NightCoop/NightCoop.dll"};
        var baseFiles=paths.Select(p=>FileSpec(p,"fixture base "+p)).ToList();
        var baseAsset=Archive(root,"base",0,baseFiles,f=>"fixture base "+f.Path);
        var profiles=new List<FixedDensityProfile>();string[] ids={"low","medium","high"};int[] fileIds={164034,164033,164031};
        for(int i=0;i<ids.Length;i++) {
            string id=ids[i];var files=new[]{Shared,Changed,Only(id)}.Select(p=>FileSpec(p,Contents(p,id))).ToList();
            var asset=Archive(root,"density-"+id,fileIds[i],files,f=>Contents(f.Path,id));
            cache.Import(Safe.In(root,asset.Name),asset);
            profiles.Add(new FixedDensityProfile{Id=id,Name=id,Assets=new List<FixedAsset>{asset},Files=files});
        }
        var standard=profiles.Single(p=>p.Id=="medium");
        return new FixedCatalog{Schema=1,Id="density-fixture",GameVersion="2.5",GameFileVersion="2.5.0.0",Packages=new List<FixedMod>{
            new FixedMod{Id="NightCoop",Name="Base fixture",Version="fixture",Profile="Kit inicial",GameVersion="2.5",RequiresPL=true,Immutable=true,Dependencies=new string[0],Assets=new List<FixedAsset>{baseAsset},Files=baseFiles},
            new FixedMod{Id=ModId,Name="NCA fixture",Version="2.3",Profile="Complementos",GameVersion="2.5",RequiresPL=true,Dependencies=new string[0],Assets=standard.Assets,Files=standard.Files,DensityProfiles=profiles}
        }};
    }
    static Fixture Game(string root,string name,FixedCatalog catalog,DownloadCache cache,string profile) {
        string game=Safe.In(root,name.Substring(0,Math.Min(6,name.Length))+"-"+Guid.NewGuid().ToString("N").Substring(0,8));Directory.CreateDirectory(game);
        string exe=Safe.In(game,"bin/x64/Cyberpunk2077.exe");Directory.CreateDirectory(Path.GetDirectoryName(exe));
        File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","FixtureGame.exe"),exe);
        Text(game,".fixture-only","density fixture");Text(game,"archive/pc/ep1/ep1.addcont_keystone","");
        foreach(var f in catalog.Packages[0].Files)Text(game,f.Path,"fixture base "+f.Path);
        var mod=catalog.Packages.Single(m=>m.Id==ModId);
        if(profile!=null)foreach(var f in NightCityDensity.Resolve(mod,profile).Files)Text(game,f.Path,Contents(f.Path,profile));
        return new Fixture{Game=game,Mod=mod,Engine=new FixedInstaller(catalog,cache,game,null,true)};
    }
    static Dictionary<string,string> Snapshot(string game) {
        return Directory.GetFiles(game,"*",SearchOption.AllDirectories)
            .Where(p=>!p.StartsWith(Path.Combine(game,".CoopModsOnline")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            .ToDictionary(p=>p.Substring(game.Length+1),p=>Safe.Hash(p),StringComparer.OrdinalIgnoreCase);
    }
    static bool Same(Dictionary<string,string> before,string game) {
        var after=Snapshot(game);return before.Count==after.Count&&before.All(p=>after.ContainsKey(p.Key)&&after[p.Key]==p.Value);
    }
    static List<OnlineReceipt> Receipts(string game) {
        string data=Safe.In(game,".CoopModsOnline");var result=new List<OnlineReceipt>();if(!Directory.Exists(data))return result;
        foreach(string path in Directory.GetFiles(data,"*.json")) {
            var envelope=Safe.Json.Deserialize<ReceiptEnvelope>(File.ReadAllText(path));
            string json=Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(envelope.Protected),null,DataProtectionScope.CurrentUser));
            result.Add(Safe.Json.Deserialize<OnlineReceipt>(json));
        }
        return result;
    }
    static void WriteReceipt(string game,OnlineReceipt receipt) {
        byte[] plain=Encoding.UTF8.GetBytes(Safe.Json.Serialize(receipt));
        Safe.WriteJson(Safe.In(game,".CoopModsOnline/"+receipt.Id+".json"),new ReceiptEnvelope{Protected=Convert.ToBase64String(ProtectedData.Protect(plain,null,DataProtectionScope.CurrentUser))});
    }
    public static int Run(string parent) {
        string root=Path.Combine(Path.GetFullPath(parent),"online-testes-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var passed=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("TESTE DENSIDADE FALHOU: "+name);passed.Add(name);};
        Action<Action,string> refuse=(action,name)=>{bool failed=false;try{action();}catch(IOException){failed=true;}check(failed,name);};
        var cache=new DownloadCache(Safe.In(root,"cache"));var catalog=Catalog(root,cache);FixedData.Validate(catalog);
        check(true,"Catalogo sintetico valido com base imutavel e tres receitas Nexus fixadas");
        var mod=catalog.Packages.Single(m=>m.Id==ModId);
        check(mod.DensityProfiles.All(p=>p.Assets.Single().ModId==10395)&&mod.DensityProfiles.Select(p=>p.Assets.Single().FileId).SequenceEqual(new[]{164034,164033,164031}),"IDs de download distintos e fixos por densidade");
        foreach(string from in new[]{"low","medium","high"})foreach(string to in new[]{"low","medium","high"}.Where(id=>id!=from)) {
            var f=Game(root,"troca-"+from+"-"+to,catalog,cache,from);var before=Snapshot(f.Game);
            Text(f.Game,"archive/pc/mod/terceiro.archive","terceiro");before=Snapshot(f.Game);
            check(NightCityDensity.Read(f.Mod,f.Game).Id==from,"Reconhece perfil manual "+from+" antes de "+to);check(NightCityDensity.Read(f.Mod,f.Game).Version=="2.3"&&FixedData.Scan(f.Mod,f.Game).Detail.Contains("Versão reconhecida: 2.3"),"Versao instalada confirmada por hashes para "+from);check(ModDetailsInfo.Read(f.Mod,f.Game,"").InstalledVersion.StartsWith("2.3 · "),"Detalhes mostram versao e densidade de "+from);
            f.Engine.ApplyDensityProfile(to,Quiet);
            check(!Directory.GetDirectories(Safe.In(cache.Root,"preparacao")).Any(),"Troca "+from+" para "+to+" remove a preparação extraída");
            check(NightCityDensity.Read(f.Mod,f.Game).Id==to&&FixedData.Scan(f.Mod,f.Game).IsCorrect,"Troca "+from+" para "+to+" reconhecida pelo scan");
            check(!File.Exists(Safe.In(f.Game,Only(from)))&&File.Exists(Safe.In(f.Game,Only(to))),"Troca "+from+" para "+to+" retira camada antiga e instala nova");
            check(Receipts(f.Game).Single().Operation=="density","Troca "+from+" para "+to+" registra operacao density");
            check(File.ReadAllText(Safe.In(f.Game,"archive/pc/mod/terceiro.archive"))=="terceiro","Troca "+from+" para "+to+" preserva arquivo alheio");
            f.Engine.Undo(Quiet);check(Same(before,f.Game)&&NightCityDensity.Read(f.Mod,f.Game).Id==from,"Desfazer "+from+" para "+to+" restaura exatamente o perfil anterior");
        }
        var noop=Game(root,"sem-alteracao",catalog,cache,"low");var noBefore=Snapshot(noop.Game);noop.Engine.ApplyDensityProfile("low",Quiet);
        check(Same(noBefore,noop.Game)&&Receipts(noop.Game).Count==0,"Mesmo perfil nao altera arquivos nem cria recibo");
        foreach(string id in new[]{"low","high"}) {
            var keep=Game(root,"instalacao-preserva-"+id,catalog,cache,id);var original=Snapshot(keep.Game);keep.Engine.Install(new[]{ModId},true,Quiet);
            check(Same(original,keep.Game)&&NightCityDensity.Read(keep.Mod,keep.Game).Id==id,"Instalar preserva perfil completo "+id);
            File.Delete(Safe.In(keep.Game,Shared));keep.Engine.Install(new[]{ModId},true,Quiet);
            check(Same(original,keep.Game)&&NightCityDensity.Read(keep.Mod,keep.Game).Id==id,"Reparo de arquivo comum preserva perfil "+id);
        }
        var fresh=Game(root,"primeira-instalacao",catalog,cache,null);check(NightCityDensity.Read(fresh.Mod,fresh.Game).Version==null,"Ausencia nao herda versao do catalogo");Text(fresh.Game,Shared,Contents(Shared,"low"));check(NightCityDensity.Read(fresh.Mod,fresh.Game).Version==null,"Instalacao parcial nao afirma versao completa");File.Delete(Safe.In(fresh.Game,Shared));fresh.Engine.Install(new[]{ModId},true,Quiet);
        check(NightCityDensity.Read(fresh.Mod,fresh.Game).Id=="low","Primeira instalacao usa densidade Baixa");
        var fail=Game(root,"rollback-falha",catalog,cache,"medium");var failBefore=Snapshot(fail.Game);
        refuse(()=>fail.Engine.ApplyDensityProfile("high",Quiet,1),"Falha injetada durante troca e propagada");
        check(!Directory.GetDirectories(Safe.In(cache.Root,"preparacao")).Any(),"Troca com falha remove preparação depois do rollback");
        check(Same(failBefore,fail.Game)&&NightCityDensity.Read(fail.Mod,fail.Game).Id=="medium","Rollback restaura arquivos anteriores apos falha");
        var edited=Game(root,"alterado",catalog,cache,"medium");Text(edited.Game,Changed,"edicao do usuario");var editBefore=Snapshot(edited.Game);
        check(!NightCityDensity.Read(edited.Mod,edited.Game).CanApply,"Perfil com arquivo diferente bloqueia seletor");check(NightCityDensity.Read(edited.Mod,edited.Game).Version==null&&!ModDetailsInfo.Read(edited.Mod,edited.Game,"").InstalledVersion.StartsWith("2.3"),"Arquivo alterado nao recebe versao instalada presumida");
        refuse(()=>edited.Engine.ApplyDensityProfile("low",Quiet),"Arquivo modificado bloqueia troca");check(Same(editBefore,edited.Game),"Troca bloqueada preserva edicao e demais arquivos");
        refuse(()=>noop.Engine.ApplyDensityProfile("desconhecido",Quiet),"ID de perfil desconhecido recusado");
        var later=Game(root,"edicao-posterior",catalog,cache,"low");later.Engine.ApplyDensityProfile("high",Quiet);Text(later.Game,Changed,"edicao posterior");later.Engine.Undo(Quiet);
        check(File.ReadAllText(Safe.In(later.Game,Changed))=="edicao posterior","Desfazer preserva arquivo editado apos troca");
        check(File.Exists(Safe.In(later.Game,Only("low")))&&!File.Exists(Safe.In(later.Game,Only("high"))),"Desfazer com edicao restaura camadas anteriores seguras");
        check(Receipts(later.Game).Single().Status=="restauracao_pendente","Edicao posterior deixa restauracao pendente explicita");
        Text(later.Game,Changed,Contents(Changed,"high"));later.Engine.Undo(Quiet);
        check(NightCityDensity.Read(later.Mod,later.Game).Id=="low"&&Receipts(later.Game).Single().Status=="desfeito","Resolver edicao e repetir desfazer conclui restauracao sem falso conflito");
        check(!File.Exists(Safe.In(later.Game,Only("high"))),"Repetir desfazer nao recria arquivo novo ja retirado na tentativa anterior");
        var removed=Game(root,"remocao-posterior",catalog,cache,"low");removed.Engine.ApplyDensityProfile("high",Quiet);
        File.Delete(Safe.In(removed.Game,Changed));File.Delete(Safe.In(removed.Game,Only("high")));removed.Engine.Undo(Quiet);
        check(!File.Exists(Safe.In(removed.Game,Changed))&&!File.Exists(Safe.In(removed.Game,Only("high"))),"Desfazer preserva remocoes posteriores de arquivos substituidos e novos");
        check(Receipts(removed.Game).Single().Status=="restauracao_pendente","Remocao posterior nao e confundida com desfazer iniciado pelo app");
        var removedAfterUndo=Snapshot(removed.Game);removed.Engine.Undo(Quiet);
        check(Same(removedAfterUndo,removed.Game),"Repetir desfazer continua preservando remocoes feitas pelo usuario");
        var interrupted=Game(root,"undo-interrompido",catalog,cache,"medium");var interruptedBefore=Snapshot(interrupted.Game);interrupted.Engine.ApplyDensityProfile("high",Quiet);
        var interruptedReceipt=Receipts(interrupted.Game).Single();interruptedReceipt.Status="desfazendo";
        interruptedReceipt.Files.Single(file=>file.Path==Only("high")).UndoStarted=true;WriteReceipt(interrupted.Game,interruptedReceipt);
        File.Delete(Safe.In(interrupted.Game,Only("high")));
        new FixedInstaller(catalog,cache,interrupted.Game,null,true).Undo(Quiet);
        check(Same(interruptedBefore,interrupted.Game)&&Receipts(interrupted.Game).Single().Status=="desfeito","Nova instancia retoma exclusao apos UndoStarted antes de Reverted e restaura perfil completo");
        foreach(string id in new[]{"low","medium","high"}) {
            var off=Game(root,"desativar-"+id,catalog,cache,id);var offBefore=Snapshot(off.Game);off.Engine.Deactivate(ModId,Quiet);
            check(!NightCityDensity.Resolve(off.Mod,id).Files.Any(file=>File.Exists(Safe.In(off.Game,file.Path))),"Desativar retira todos os arquivos do perfil "+id);
            check(catalog.Packages[0].Files.All(file=>File.Exists(Safe.In(off.Game,file.Path))),"Desativar "+id+" preserva cinco arquivos da base");
            off.Engine.Undo(Quiet);check(Same(offBefore,off.Game)&&NightCityDensity.Read(off.Mod,off.Game).Id==id,"Desfazer desativacao restaura perfil "+id);
        }
        var empty=Game(root,"cache-ausente",catalog,cache,"medium");var emptyBefore=Snapshot(empty.Game);
        var noCache=new FixedInstaller(catalog,new DownloadCache(Safe.In(root,"cache-vazio")),empty.Game,null,true);
        refuse(()=>noCache.ApplyDensityProfile("high",Quiet),"Sem pacote validado troca e recusada");check(Same(emptyBefore,empty.Game),"Staging sem cache nao altera jogo fixture");
        check(!Directory.GetDirectories(Safe.In(root,"cache-vazio/preparacao")).Any(),"Troca sem pacote remove a preparação vazia");
        Action<Action<FixedCatalog>,string> invalid=(edit,label)=>{
            var toxic=Safe.Json.Deserialize<FixedCatalog>(Safe.Json.Serialize(catalog));edit(toxic);refuse(()=>FixedData.Validate(toxic),label);
        };
        invalid(c=>c.Packages.Single(m=>m.Id==ModId).DensityProfiles.RemoveAt(0),"Catalogo exige as tres densidades fixadas");
        invalid(c=>c.Packages.Single(m=>m.Id==ModId).DensityProfiles[2].Files[0].Path="red4ext/RED4ext.dll","Variante nao pode colidir com loader imutavel");
        invalid(c=>c.Packages.Single(m=>m.Id==ModId).DensityProfiles[0].Files[0].Entry="../fora.reds","Entrada da variante nao pode escapar do staging");
        invalid(c=>c.Packages.Single(m=>m.Id==ModId).DensityProfiles[2].Assets[0].FileId=164034,"Variante Alta nao aceita pin da Baixa");
        invalid(c=>c.Packages.Single(m=>m.Id==ModId).DensityProfiles[0].Assets[0].Sha256="incorreto","Variante sem hash fixo valida e recusada");
        invalid(c=>c.Packages.Single(m=>m.Id==ModId).DensityProfiles[0].Assets[0].Url="https://example.org/low.zip","Link fora da fonte oficial da variante e recusado");
        Safe.WriteJson(Path.Combine(root,"resultado-densidade.json"),new{Passed=passed.Count,Tests=passed,FixtureOnly=true,RealGameTouched=false});
        Console.WriteLine(passed.Count+" testes de densidade aprovados em "+root);return passed.Count;
    }
}
