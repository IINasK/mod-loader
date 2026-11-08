using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

public static class OnlineDefaultsQueueTests {
    const string Ini="red4ext/plugins/mod_settings/user.ini";
    static void Put(string game,string path,string text){string p=Safe.In(game,path);Directory.CreateDirectory(Path.GetDirectoryName(p));File.WriteAllText(p,text,new UTF8Encoding(false));}
    public static void Run(string parent){
        string root=Path.Combine(Path.GetFullPath(parent),"online-testes-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var passed=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("TESTE DEFAULTS QUEUE FALHOU: "+name);passed.Add(name);};
        FixedAsset asset;var catalog=OnlineTests.MakeCatalog(root,out asset);var drive=catalog.Packages[1];drive.Id="mod-33954";drive.Version="1.5";
        string content="{\"steerNormalPct\":\"75\",\"steerSportPct\":\"50\",\"steerGentlePct\":\"65\",\"steeringSmoothing\":\"false\"}";string hash;
        using(var sha=SHA256.Create())hash=Safe.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(content)));
        catalog.Defaults=new List<FixedDefault>{new FixedDefault{Id="drive-defaults",ModId=drive.Id,Version=drive.Version,Path=Ini,Format="ini-section",Section="ImmersiveDrivingSettings",Content=content,Sha256=hash}};
        FixedData.Validate(catalog);var cache=new DownloadCache(Safe.In(root,"cache"));
        string game=OnlineTests.Fixture(root,"existing");foreach(var f in catalog.Packages.SelectMany(m=>m.Files))Put(game,f.Path,"test "+f.Path);
        var before=catalog.Packages.SelectMany(m=>m.Files).ToDictionary(f=>f.Path,f=>Safe.Hash(Safe.In(game,f.Path)));
        var plan=AutoInstallPlan.Build(catalog,new[]{drive.Id},game,cache,false);
        check(!plan.Complete&&plan.ReadyMods.Count==1&&plan.ReadyMods[0].Id==drive.Id&&plan.ManualMod==null&&plan.CachedAssets.Count==0,"Mod correto com padrão ausente fica pronto sem ZIP, download ou navegador");
        check(!plan.MissingMods.Where(m=>!plan.Scans[m.Id].IsCorrect).SelectMany(m=>m.Assets).Any(a=>!plan.CachedAssets[a.Sha256]),"Verificação de cache da UI aceita operação somente de configuração");
        var engine=new FixedInstaller(catalog,cache,game,null,true);engine.Install(new[]{drive.Id},true,s=>{});
        check(File.ReadAllText(Safe.In(game,Ini)).Contains("steerNormalPct = 75")&&File.ReadAllText(Safe.In(game,Ini)).Contains("steeringSmoothing = false"),"Motor aplica os valores capturados sem reinstalar código");
        check(before.All(p=>Safe.Hash(Safe.In(game,p.Key))==p.Value)&&Directory.GetFiles(Safe.In(game,".CoopModsOnline"),"*.json").Length==0,"Operação de configuração não altera código nem cria recibo falso de mod");
        check(AutoInstallPlan.Build(catalog,new[]{drive.Id},game,cache,false).Complete,"Fila termina após aplicar configuração em vez de repetir download");
        Put(game,Ini,"[ImmersiveDrivingSettings]\r\nsteerNormalPct = 99\r\n");string custom=Safe.Hash(Safe.In(game,Ini));engine.Install(new[]{drive.Id},true,s=>{});
        check(Safe.Hash(Safe.In(game,Ini))==custom&&AutoInstallPlan.Build(catalog,new[]{drive.Id},game,cache,false).Complete,"Configuração pessoal existente preservada em nova versão do instalador");
        string clean=OnlineTests.Fixture(root,"clean");cache.Import(Safe.In(root,"fixture.zip"),asset);new FixedInstaller(catalog,cache,clean,null,true).Install(new[]{drive.Id},true,s=>{});
        check(catalog.Packages.All(m=>FixedData.Scan(m,clean).IsCorrect)&&!ModDefaults.Needs(catalog,drive.Id,clean),"Instalação normal usa a fila existente e aplica padrão depois dos arquivos");
        string failed=OnlineTests.Fixture(root,"failed");bool refused=false;try{new FixedInstaller(catalog,cache,failed,null,true).Install(new[]{drive.Id},true,s=>{},1);}catch(IOException){refused=true;}
        check(refused&&!File.Exists(Safe.In(failed,Ini)),"Falha durante cópia não deixa padrão de mod não instalado");
        var embedded=FixedData.Load();check(embedded.Defaults.Count==8&&embedded.Packages.Count==158,"Oito padrões sem FPV preservam os158 registros restantes");
        var defaults=embedded.Defaults.Single(d=>d.ModId=="mod-33954");var values=Safe.Json.Deserialize<Dictionary<string,string>>(defaults.Content);
        check(values["steerNormalPct"]=="75"&&values["steerSportPct"]=="50"&&values["steerGentlePct"]=="65"&&values["steeringSmoothing"]=="false","Steering atual exportado exatamente como salvo pelo usuário");
        check(!embedded.Defaults.Any(d=>d.Path.Contains("SpotOnRadio")||d.Path.Contains("RadioExt")||d.Path.Contains("immersive_third_person")),"Credenciais de rádio, corpo e estado do ITP não são exportados");
        Safe.WriteJson(Path.Combine(root,"resultado-defaults-queue.json"),new{Passed=passed.Count,Tests=passed,RealGameTouched=false,RealBrowserUsed=false});
    }
}
