using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

public static class OnlineAutoInstallTests {
    static string Hash(string value){using(var h=SHA256.Create())return Safe.Hex(h.ComputeHash(Encoding.UTF8.GetBytes(value)));}
    static void Write(string root,string relative,string value){string file=Safe.In(root,relative);Directory.CreateDirectory(Path.GetDirectoryName(file));File.WriteAllText(file,value,new UTF8Encoding(false));}
    static string Game(string root,string name,bool updated=false){
        string game=Path.Combine(root,name);Directory.CreateDirectory(Path.Combine(game,"bin","x64"));
        File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..",updated?"FixtureUnsupported.exe":"FixtureGame.exe"),Path.Combine(game,"bin","x64","Cyberpunk2077.exe"));
        Write(game,"archive/pc/ep1/ep1.addcont_keystone","");Write(game,".fixture-only","test");return game;
    }
    static FixedAsset Asset(string name,int modId,int fileId){return new FixedAsset{Id="a0",Name=name+".zip",Format="raw",Size=Encoding.UTF8.GetByteCount(name),Sha256=Hash(name),ModId=modId,FileId=fileId,Url=modId==0?"https://github.com/example/project/releases/download/fixed/"+name+".zip":"https://www.nexusmods.com/cyberpunk2077/mods/"+modId+"?tab=files&file_id="+fileId};}
    static FixedMod Mod(string id,string relative,FixedAsset asset,string[] deps){return new FixedMod{Id=id,Name=id,Version="1.0",GameVersion="2.5",RequiresPL=true,Dependencies=deps,Assets=new List<FixedAsset>{asset},Files=new List<FixedFile>{new FixedFile{Asset=asset.Id,Entry="",Path=relative,Size=asset.Size,Sha256=asset.Sha256}}};}
    static FixedCatalog Catalog(){
        var baseAsset=Asset("base",0,0);var b=Mod("Dependency","archive/pc/mod/auto-dependency.archive",Asset("dependency",100,42),new[]{"NightCoop"});var a=Mod("Addon","archive/pc/mod/auto-addon.archive",Asset("addon",200,53),new[]{"Dependency"});
        var basis=Mod("NightCoop","bin/x64/winmm.dll",baseAsset,new string[0]);basis.Immutable=true;
        foreach(string p in new[]{"red4ext/RED4ext.dll","red4ext/LICENSE.txt","red4ext/THIRD_PARTY_LICENSES.txt","red4ext/plugins/NightCoop/NightCoop.dll"})basis.Files.Add(new FixedFile{Asset=baseAsset.Id,Entry="",Path=p,Size=baseAsset.Size,Sha256=baseAsset.Sha256});
        return new FixedCatalog{Schema=1,Id="auto-fixture",GameVersion="2.5",GameFileVersion="2.5.0.0",Packages=new List<FixedMod>{basis,b,a}};
    }
    static void Base(string game,FixedCatalog catalog){foreach(var f in catalog.Packages[0].Files)Write(game,f.Path,"base");}
    static void Cache(DownloadCache cache,string root,FixedAsset asset,string contents){string file=Path.Combine(root,asset.Name);if(!File.Exists(file))File.WriteAllText(file,contents,new UTF8Encoding(false));cache.Import(file,asset);}
    static FixedCatalog Clone(FixedCatalog c){return Safe.Json.Deserialize<FixedCatalog>(Safe.Json.Serialize(c));}
    static Dictionary<string,string> Snapshot(string root){return Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(p=>p.Substring(root.Length),p=>Safe.Hash(p));}
    static string Archive(string root,FixedMod mod,string contents){
        string path=Path.Combine(root,mod.Assets[0].Name);
        using(var zip=ZipFile.Open(path,ZipArchiveMode.Create))foreach(var f in mod.Files){f.Entry=f.Path;var entry=zip.CreateEntry(f.Entry);using(var writer=new StreamWriter(entry.Open(),new UTF8Encoding(false)))writer.Write(contents);}
        mod.Assets[0].Format="zip";mod.Assets[0].Size=new FileInfo(path).Length;mod.Assets[0].Sha256=Safe.Hash(path);return path;
    }
    public static void Run(string parent){
        string root=Path.Combine(Path.GetFullPath(parent),"auto-"+Guid.NewGuid().ToString("N").Substring(0,12));Directory.CreateDirectory(root);
        var passed=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("TESTE AUTO-INSTALL FALHOU: "+name);passed.Add(name);};
        Action<Action,string> refuse=(action,name)=>{bool failed=false;try{action();}catch(IOException){failed=true;}check(failed,name);};
        var catalog=Catalog();FixedData.Validate(catalog);string game=Game(root,"game");Base(game,catalog);var cache=new DownloadCache(Path.Combine(root,"cache"));
        var plan=AutoInstallPlan.Build(catalog,new[]{"Addon"},game,cache,false);
        check(plan.ExpandedMods.Select(m=>m.Id).SequenceEqual(new[]{"NightCoop","Dependency","Addon"})&&plan.Scans.Count==3,"Plano inclui somente autorização e dependências em ordem topológica");
        check(plan.ReadyMods.Count==0&&plan.MissingMods.Count==2&&!plan.Complete,"Dependência Nexus gratuita ausente impede instalação do complemento");
        check(plan.ManualMod.Id=="Dependency"&&plan.ManualAsset.FileId==42,"Primeiro download manual aponta exatamente o arquivo fixado da dependência");
        Cache(cache,root,catalog.Packages[2].Assets[0],"addon");plan=AutoInstallPlan.Build(catalog,new[]{"Addon"},game,cache,false);
        check(plan.ReadyMods.Count==0,"Pacote do complemento no cache não contorna dependência pendente");
        Cache(cache,root,catalog.Packages[1].Assets[0],"dependency");plan=AutoInstallPlan.Build(catalog,new[]{"Addon"},game,cache,false);
        check(plan.ReadyMods.Select(m=>m.Id).SequenceEqual(new[]{"Dependency","Addon"})&&plan.ManualMod==null,"Importar dependência libera dependência e complemento já disponível");
        var onlyB=new DownloadCache(Path.Combine(root,"cache-b"));Cache(onlyB,root,catalog.Packages[1].Assets[0],"dependency");plan=AutoInstallPlan.Build(catalog,new[]{"Addon"},game,onlyB,false);
        check(plan.ReadyMods.Select(m=>m.Id).SequenceEqual(new[]{"Dependency"})&&plan.ManualMod.Id=="Addon"&&plan.ManualAsset.FileId==53,"Dependência pode avançar enquanto o complemento aguarda seu arquivo exato");
        var empty=new DownloadCache(Path.Combine(root,"empty-cache"));plan=AutoInstallPlan.Build(catalog,new[]{"Addon"},game,empty,true);
        check(plan.ReadyMods.Count==2&&plan.ManualMod==null,"Conta Premium libera obtenção dos arquivos fixados sem presumir cache");
        var github=Clone(catalog);github.Packages[1].Assets[0].ModId=0;github.Packages[1].Assets[0].FileId=0;github.Packages[1].Assets[0].Url="https://github.com/example/project/releases/download/fixed/dependency.zip";
        plan=AutoInstallPlan.Build(github,new[]{"Dependency"},game,empty,false);check(plan.ReadyMods.Count==1&&plan.ManualMod==null,"Fonte oficial direta é baixável sem Premium");
        Write(game,catalog.Packages[1].Files[0].Path,"dependency");Write(game,catalog.Packages[2].Files[0].Path,"addon");plan=AutoInstallPlan.Build(catalog,new[]{"Addon"},game,empty,false);
        check(plan.Complete&&plan.MissingMods.Count==0&&plan.ReadyMods.Count==0&&plan.CachedAssets.Count==0,"Instalação manual correta não é reinstalada nem exige reler cache");
        string missingBase=Game(root,"missing-base");Write(missingBase,catalog.Packages[1].Files[0].Path,"dependency");var noBase=Clone(catalog);noBase.Packages[0].Assets[0].ModId=300;noBase.Packages[0].Assets[0].FileId=64;noBase.Packages[0].Assets[0].Url="https://www.nexusmods.com/cyberpunk2077/mods/300?tab=files&file_id=64";
        plan=AutoInstallPlan.Build(noBase,new[]{"Addon"},missingBase,cache,false);check(plan.ReadyMods.Count==0&&plan.ManualMod.Id=="NightCoop","Dependência instalada com ancestral ausente não libera o fechamento do complemento");
        var blocked=Clone(catalog);blocked.Packages[1].BlockedReason="falha reproduzida";refuse(()=>AutoInstallPlan.Build(blocked,new[]{"Addon"},game,cache,false),"Item retido bloqueia o fluxo automático, inclusive como dependência");
        refuse(()=>AutoInstallPlan.Build(catalog,new[]{"Unknown"},game,cache,false),"Mod desconhecido não entra na autorização automática");
        var wrongBuild=Clone(catalog);wrongBuild.GameFileVersion="2.5.0.99";refuse(()=>AutoInstallPlan.Build(wrongBuild,new[]{"Addon"},game,cache,false),"Build divergente bloqueia planejamento antes de qualquer cópia");
        string updated=Game(root,"updated",true);refuse(()=>AutoInstallPlan.Build(catalog,new[]{"Addon"},updated,cache,false),"Jogo atualizado bloqueia planejamento automático");
        Write(game,catalog.Packages[2].Files[0].Path,"edição alheia");refuse(()=>AutoInstallPlan.Build(catalog,new[]{"Addon"},game,cache,false),"Arquivo alterado recusa instalação automática");check(File.ReadAllText(Safe.In(game,catalog.Packages[2].Files[0].Path))=="edição alheia","Planejamento recusado preserva a alteração existente");
        string untouched=Game(root,"untouched");Base(untouched,catalog);var untouchedBefore=Snapshot(untouched);AutoInstallPlan.Build(catalog,new[]{"Addon"},untouched,cache,false);var after=Snapshot(untouched);
        check(untouchedBefore.Count==after.Count&&untouchedBefore.All(p=>after.ContainsKey(p.Key)&&after[p.Key]==p.Value)&&!Directory.Exists(Path.Combine(untouched,".CoopModsOnline")),"Planejamento não escreve metadados, arquivos ou recibos no jogo");
        var shared=Clone(catalog);shared.Packages[2].Assets[0]=shared.Packages[1].Assets[0];shared.Packages[2].Files[0].Size=shared.Packages[1].Assets[0].Size;shared.Packages[2].Files[0].Sha256=shared.Packages[1].Assets[0].Sha256;
        plan=AutoInstallPlan.Build(shared,new[]{"Addon"},untouched,cache,false);check(plan.CachedAssets.Count==1&&plan.ReadyMods.Count==2,"Mesmo SHA compartilhado é conferido uma única vez por plano");
        var inconsistent=Clone(shared);inconsistent.Packages[2].Assets[0].Size++;refuse(()=>AutoInstallPlan.Build(inconsistent,new[]{"Addon"},untouched,cache,false),"SHA com tamanhos incompatíveis não reutiliza verificação de cache");
        refuse(()=>AutoInstallPlan.Build(catalog,new[]{"Addon"},untouched,new DownloadCache(Path.Combine(untouched,"cache")),false),"Cache dentro da pasta do jogo é recusado antes de baixar");
        refuse(()=>AutoInstallPlan.Build(catalog,new[]{"Addon"},@"\\server\share\game",cache,false),"Caminho UNC é recusado antes de consultar executável ou rede");
        refuse(()=>AutoInstallPlan.Build(catalog,new[]{"Addon"},Path.GetPathRoot(root),cache,false),"Raiz de unidade é recusada antes de consultar arquivos");
        string flowRoot=Path.Combine(root,"online-testes-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(flowRoot);string flowGame=Game(flowRoot,"g");var flowCatalog=Catalog();Base(flowGame,flowCatalog);
        string archiveB=Archive(flowRoot,flowCatalog.Packages[1],"dependency"),archiveA=Archive(flowRoot,flowCatalog.Packages[2],"addon");FixedData.Validate(flowCatalog);
        var flowCache=new DownloadCache(Path.Combine(root,"flow-cache"));var importer=new ModImporter(flowCatalog,flowCache,Path.Combine(flowRoot,"pending.json"));var engine=new FixedInstaller(flowCatalog,flowCache,flowGame,null,true);var authorized=new[]{"Addon"};
        var importedB=importer.Import(archiveB);plan=AutoInstallPlan.Build(flowCatalog,authorized,flowGame,flowCache,false);
        check(importedB.SelectedIds.SequenceEqual(new[]{"Dependency"})&&plan.ReadyMods.Select(m=>m.Id).SequenceEqual(new[]{"Dependency"}),"Importação reconhecida de B libera somente B para fila previamente autorizada");
        engine.Install(plan.ReadyMods.Select(m=>m.Id),true,s=>{});
        check(FixedData.Scan(flowCatalog.Packages[1],flowGame).IsCorrect&&!FixedData.Scan(flowCatalog.Packages[2],flowGame).IsCorrect,"Motor real em fixture instala dependência e mantém complemento aguardando arquivo");
        var importedA=importer.Import(archiveA);plan=AutoInstallPlan.Build(flowCatalog,authorized,flowGame,flowCache,false);
        check(importedA.SelectedIds.SequenceEqual(new[]{"Addon"})&&plan.ReadyMods.Select(m=>m.Id).SequenceEqual(new[]{"Addon"}),"Importar A depois de B continua fila sem ação Atualizar");
        engine.Install(plan.ReadyMods.Select(m=>m.Id),true,s=>{});plan=AutoInstallPlan.Build(flowCatalog,authorized,flowGame,flowCache,false);
        check(plan.Complete&&plan.ReadyMods.Count==0&&Directory.GetFiles(Path.Combine(flowGame,".CoopModsOnline"),"*.json").Length==2,"Fluxo importar B e A conclui instalações isoladas sem rede ou scan completo da lista");
        Safe.WriteJson(Path.Combine(root,"resultado.json"),new{Passed=passed.Count,Tests=passed,NetworkUsed=false,RealGameTouched=false,PlanningOnly=false,FixtureInstallTested=true});File.WriteAllText(Path.Combine(parent,"ultimo-teste-auto-install.txt"),root);
    }
}
