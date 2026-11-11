using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

static class OnlineExtraCleanupTests {
    static void Put(string game,string relative,string content) {
        string path=Safe.In(game,relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path,content);
    }

    internal static void RunSyncOwnership(string parent) {
        string root=OnlineTests.NestedRoot(parent,"sync-ownership-");Directory.CreateDirectory(root);
        FixedAsset asset;var catalog=OnlineTests.MakeCatalog(root,out asset);
        string game=OnlineTests.Fixture(root,"game"),data=Safe.In(root,"data");
        var engine=new FixedInstaller(catalog,game,true);
        var checks=new List<string>();
        Action<bool,string> check=(ok,name)=>{if(!ok)throw new IOException("PROTEÇÃO SYNC: "+name);checks.Add(name);};
        var host=new InstalledSnapshotManifest{List=new GeneralModList(),Files=new List<InstalledSnapshotFile>(),SyncGameInventory=new Dictionary<string,string>()};
        string known=catalog.Packages[1].Files[0].Path,baseFile=catalog.Packages[0].Files.Last().Path;
        Put(game,known,"test "+known);Put(game,baseFile,"test "+baseFile);
        const string dll="red4ext/plugins/Cyberteste/mod.dll",script="r6/scripts/Cyberteste/main.reds";
        const string changed="red4ext/plugins/Changed/mod.dll";
        string package=Safe.In(root,"Cyberteste.zip");
        using(var zip=System.IO.Compression.ZipFile.Open(package,System.IO.Compression.ZipArchiveMode.Create))
            foreach(string path in new[]{dll,script,changed})using(var writer=new StreamWriter(zip.CreateEntry(path).Open()))writer.Write(path);
        new GeneralModInstaller(game,Path.Combine(data,"Instalacoes")).Install(package,"Cyberteste",_=>{});
        var unknown=new[]{"cache/runtime.bin","red4ext/plugins/cache/runtime.txt","red4ext/plugins/Temp/generated.json",
            "red4ext/plugins/Cyberteste/generated.json","red4ext/plugins/Cyberteste/nested/extra.dll",
            "bin/x64/plugins/cyber_engine_tweaks/mods/Temp/init.lua",
            "bin/x64/plugins/cyber_engine_tweaks/mods/Temp/runtime.log",
            "bin/x64/plugins/cyber_engine_tweaks/mods/Temp/db.sqlite3",
            "r6/scripts/Temp/generated.reds","r6/tweaks/Temp/generated.yaml"};
        foreach(string path in unknown)Put(game,path,path);
        Directory.CreateDirectory(Safe.In(game,"red4ext/plugins/Cyberteste/empty-runtime"));
        Directory.CreateDirectory(Safe.In(game,"r6/scripts/empty-generic/nested"));
        Put(game,changed,"changed at runtime");
        Func<P2PSyncExtra[]> scan=()=>engine.PlanSyncExtras(host,appData:data);
        var review=scan();
        var proven=review.Where(extra=>extra.Proven).SelectMany(extra=>extra.Files).ToArray();
        check(new HashSet<string>(proven.Select(file=>file.Path)).SetEquals(new[]{known,dll,script}),
            "Só caminho e conteúdo exatos do catálogo ou recibo comprovam o vínculo, inclusive em múltiplas pastas.");
        check(review.Any(extra=>!extra.Proven&&extra.Files.Any(file=>file.Path==changed)),
            "Arquivo registrado com conteúdo modificado fica desconhecido, não autorizado pelo nome da pasta.");
        check(review.Any(extra=>!extra.Proven&&extra.Files.Any(file=>file.Path=="red4ext/plugins/Cyberteste/generated.json"))&&
            review.Any(extra=>!extra.Proven&&extra.Files.Any(file=>file.Path=="red4ext/plugins/Cyberteste/nested/extra.dll")),
            "Arquivo gerado e DLL desconhecida no diretório de um mod comprovado não herdam sua autorização.");
        check(review.Where(extra=>extra.Proven).All(extra=>extra.Files.All(file=>!unknown.Contains(file.Path)))&&
            review.Any(extra=>!extra.Proven&&extra.Files.Any(file=>file.Path=="r6/scripts/Temp/generated.reds")),
            "Pastas genéricas e código fora dos recibos são preservados; cache privado fica fora do escopo.");
        check(review.All(extra=>extra.Files.All(file=>file.Path!=baseFile)),"A base protegida não entra na limpeza.");
        bool refused=false;
        var forged=review.First(extra=>!extra.Proven&&extra.Files.Any(file=>file.Before!=null)).Files.ToList();
        try{engine.ApplySyncCleanup(forged,host,new string[0],_=>{},data);}catch(IOException){refused=true;}
        check(refused&&proven.All(file=>File.Exists(Safe.In(game,file.Path))),"Seleção forjada de desconhecido é recusada antes de mover qualquer arquivo.");
        refused=false;
        try{engine.ApplySyncCleanup(new List<OwnedChange>{new OwnedChange{Path="r6/scripts/empty-generic"}},host,new string[0],_=>{},data);}catch(IOException){refused=true;}
        check(refused&&Directory.Exists(Safe.In(game,"r6/scripts/empty-generic/nested")),"Seleção antiga de pasta vazia sem prova é recusada.");
        var manual=engine.PlanExtraReset(data);
        check(manual.Any(file=>file.Path==dll)&&manual.All(file=>!unknown.Contains(file.Path)&&file.Path!=changed),
            "A limpeza manual usa a mesma proteção, sem autorizar desconhecidos por outra tela.");
        var approved=proven.ToList();
        Put(game,dll,"changed after approval");refused=false;
        try{engine.ApplySyncCleanup(approved,host,new string[0],_=>{},data);}catch(IOException){refused=true;}
        check(refused&&File.Exists(Safe.In(game,known)),"Mudança após confirmação bloqueia a limpeza inteira.");
        Put(game,dll,dll);
        host.SyncGameInventory[dll]=Safe.Hash(Safe.In(game,dll));refused=false;
        try{engine.ApplySyncCleanup(approved,host,new string[0],_=>{},data);}catch(IOException){refused=true;}
        check(refused&&File.Exists(Safe.In(game,dll)),"Um arquivo agora pertencente ao anfitrião é protegido mesmo com aprovação anterior.");
        host.SyncGameInventory.Clear();
        engine.ApplySyncCleanup(approved,host,new string[0],_=>{},data);
        check(proven.All(file=>!File.Exists(Safe.In(game,file.Path))),"Só os três arquivos comprovados e aprovados são guardados no backup.");
        check(unknown.All(path=>File.ReadAllText(Safe.In(game,path))==path)&&File.ReadAllText(Safe.In(game,changed))=="changed at runtime",
            "Caches, arquivos gerados, nomes genéricos, logs e bancos permanecem intactos.");
        check(Directory.Exists(Safe.In(game,"red4ext/plugins/Cyberteste/empty-runtime"))&&
            Directory.Exists(Safe.In(game,"r6/scripts/empty-generic/nested"))&&!Directory.Exists(Safe.In(game,"r6/scripts/Cyberteste")),
            "Apenas ancestrais comprovados e literalmente vazios saem; subpastas desconhecidas permanecem.");
        engine.Undo(_=>{});
        check(proven.All(file=>Safe.Hash(Safe.In(game,file.Path))==file.Before)&&unknown.All(path=>File.ReadAllText(Safe.In(game,path))==path),
            "Desfazer restaura exatamente os arquivos movidos, sem tocar nos desconhecidos.");
        const string oldSetting="bin/x64/plugins/cyber_engine_tweaks/mods/stopNCPD/mod_user_settings.json";
        const string oldLog="bin/x64/plugins/cyber_engine_tweaks/mods/stopNCPD/stopNCPD.log";
        string historicalGame=OnlineTests.Fixture(root,"historical"),historicalData=Safe.In(root,"historical-data");
        Put(historicalGame,oldSetting,"{}");Put(historicalGame,oldLog,"generated locally");
        var officialCatalog=Safe.Json.Deserialize<FixedCatalog>(Safe.Json.Serialize(FixedData.Load()));
        officialCatalog.GameFileVersion=catalog.GameFileVersion;
        var official=new FixedInstaller(officialCatalog,historicalGame,true);
        var historical=official.PlanSyncExtras(host,appData:historicalData);
        var oldFile=historical.Where(extra=>extra.Proven).SelectMany(extra=>extra.Files).Single(file=>file.Path==oldSetting);
        check(historical.Where(extra=>!extra.Proven).SelectMany(extra=>extra.Files).Any(file=>file.Path==oldLog),
            "O NCPD manual é reconhecido pela assinatura do pacote anterior, mas seu log gerado continua desconhecido.");
        official.ApplySyncCleanup(new List<OwnedChange>{oldFile},host,new string[0],_=>{},historicalData);
        check(!File.Exists(Safe.In(historicalGame,oldSetting))&&File.ReadAllText(Safe.In(historicalGame,oldLog))=="generated locally",
            "Sincronizar retira o NCPD comprovado sem ampliar a autorização aos resíduos da mesma pasta.");
        Safe.WriteJson(Safe.In(root,"resultado-protecao-sincronizacao.json"),new{Passed=true,Tests=checks,RealGameTouched=false});
    }

    public static void Run(string parent) {
        string root=Path.Combine(Path.GetFullPath(parent),"online-testes-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        FixedAsset asset;
        var catalog=OnlineTests.MakeCatalog(root,out asset);
        string original=OnlineTests.Fixture(root,"game"),game=Safe.In(root,"g");
        Directory.Move(original,game);
        var engine=new FixedInstaller(catalog,new DownloadCache(Path.Combine(root,"cache")),game,null,true);
        var checks=new List<string>();
        Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("TESTE LIMPEZA: "+name);checks.Add(name);};
        Action<string> quiet=message=>{};
        var extras=new[]{
            "archive/pc/mod/outside.archive", "archive/pc/mod/outside.archive.xl",
            "r6/scripts/Another/another.reds", "r6/tweaks/Another/another.yaml",
            "red4ext/plugins/Other.dll", "red4ext/plugins/Other/config.ini",
            "bin/x64/plugins/cyber_engine_tweaks/mods/Other/init.lua", "mods/Legacy/info.json", "mods/Legacy/Legacy.archive"
        };
        string knownPath=catalog.Packages[1].Files[0].Path;
        var preserved=new[]{knownPath, "archive/pc/content/official.archive", "engine/config/platform/pc/personal.ini",
            "bin/x64/winmm.dll", "red4ext/plugins/NightCoop/personal.json", "mods/manual-not-redmod/notes.txt"};
        foreach(string path in extras.Concat(preserved))Put(game,path,path);
        string data=Safe.In(root,"registered");
        string package=Safe.In(root,"registered-extras.zip");
        using(var zip=System.IO.Compression.ZipFile.Open(package,System.IO.Compression.ZipArchiveMode.Create))
            foreach(string path in extras)using(var writer=new StreamWriter(zip.CreateEntry(path).Open()))writer.Write(path);
        new GeneralModInstaller(game,Path.Combine(data,"Instalacoes")).Install(package,"registered-extras",quiet);
        var plan=engine.PlanExtraReset(data);
        check(new HashSet<string>(plan.Select(f=>f.Path)).SetEquals(extras),"Escopo identifica só os extras em locais de mod");
        check(extras.Concat(preserved).All(p=>File.Exists(Safe.In(game,p))),"Prévia não retira arquivos");
        Put(game,extras[0],"editado apos aprovacao");
        bool refused=false;
        try { engine.ApplyExtraReset(plan,quiet,data); } catch(IOException) { refused=true; }
        check(refused&&extras.All(p=>File.Exists(Safe.In(game,p))),"Alteração após a confirmação bloqueia toda a limpeza");
        Put(game,extras[0],extras[0]);
        engine.ApplyExtraReset(engine.PlanExtraReset(data),quiet,data);
        check(extras.All(p=>!File.Exists(Safe.In(game,p)))&&preserved.All(p=>File.ReadAllText(Safe.In(game,p))==p),
            "Retira extras e preserva arquivos do catálogo, jogo base e ajustes fora do escopo");
        check(engine.PlanExtraReset(data).Count==0,"Segunda limpeza é idempotente");
        Put(game,extras[0],"destino ocupado");
        engine.Undo(quiet);
        check(File.ReadAllText(Safe.In(game,extras[0]))=="destino ocupado"&&extras.Skip(1).All(p=>File.ReadAllText(Safe.In(game,p))==p),
            "Desfazer preserva destino ocupado e restaura os demais");
        File.Delete(Safe.In(game,extras[0]));
        engine.Undo(quiet);
        check(extras.All(p=>File.ReadAllText(Safe.In(game,p))==p),"Desfazer pendente retoma quando o destino fica livre");
        bool failed=false;
        try { engine.ApplyExtraReset(engine.PlanExtraReset(data),message=>{if(message.StartsWith("Guardado:",StringComparison.Ordinal))throw new IOException("Falha de teste");},data); }
        catch(IOException) { failed=true; }
        check(failed&&extras.All(p=>File.ReadAllText(Safe.In(game,p))==p),"Falha durante movimentação restaura arquivos já movidos");
        Safe.WriteJson(Path.Combine(root,"resultado-limpeza.json"),new{Passed=checks.Count,Tests=checks,RealGameTouched=false});
    }
}
