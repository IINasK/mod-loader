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

    static void RunManualCapture(string root,FixedCatalog catalog,Action<bool,string> check) {
        string game=OnlineTests.Fixture(root,"m"),data=Safe.In(root,"manual-data");
        const string lua="bin/x64/plugins/cyber_engine_tweaks/mods/Emoji/init.lua";
        const string photo="bin/x64/plugins/cyber_engine_tweaks/mods/Emoji/fotos/photo.png";
        const string script="r6/scripts/SomeOtherName/main.reds";
        const string existing="r6/tweaks/Emoji/settings.yaml";
        Put(game,existing,"old");
        var capture=new ManualInstallCapture(game,true);
        Put(game,lua,"load emoji");Put(game,script,"new script");Put(game,photo,"personal photo");Put(game,existing,"new");
        var changes=capture.Changes();
        check(changes.Count==4&&!changes.Single(file=>file.Path==existing).Added,
            "A comparação distingue arquivo preexistente e detecta mudanças em três diretórios.");
        var variant=new GeneralModVariant{Id="default",Name="Principal",Sources=new List<string>{"installed://emoji/default"}};
        var entry=new GeneralModEntry{Id="emoji",Name="Emoji",DefaultVariant=variant.Id,Variants=new List<GeneralModVariant>{variant}};
        var selected=changes.Where(file=>file.Path!=photo).ToArray();
        var installer=new InstalledSnapshotInstaller(game,data,catalog,true);
        Put(game,script,"changed after review");
        bool refused=false;
        try{installer.RegisterManual(entry,selected);}catch(IOException){refused=true;}
        check(refused&&!installer.ProjectDeletionFiles().Any(),"Uma mudança após a captura invalida o registro inteiro.");
        Put(game,script,"new script");
        var receipt=installer.RegisterManual(entry,selected);
        var state=new GeneralModState{Destination=game,List=new GeneralModList{Mods=new List<GeneralModEntry>{entry}}};
        state.SnapshotReceipts.Add("emoji/default",receipt);
        installer=new InstalledSnapshotInstaller(game,data,catalog,true);
        check(installer.Status(receipt,"emoji/default")==GeneralModInstaller.Installed&&installer.ProjectDeletionFiles().Count()==3,
            "O registro por arquivo continua disponível após reabrir.");
        string zip=Safe.In(root,"manual-p2p.zip");
        InstalledSnapshot.Create(zip,state,new[]{entry.Id},catalog,data,false,null);
        var manifest=InstalledSnapshot.Read(zip,Safe.Hash(zip),catalog);
        check(new HashSet<string>(manifest.Files.Select(file=>file.Path)).SetEquals(new[]{lua,script,existing}),
            "O ZIP P2P contém somente os arquivos aprovados e preserva seus caminhos.");
        var engine=new FixedInstaller(catalog,game,true);
        var inventory=engine.ReadSyncInventory(new InstalledSnapshotFile[0],appData:data);
        check(new HashSet<string>(inventory.Keys).SetEquals(new[]{lua,script}),
            "A foto também fica fora do inventário enviado na comparação, mesmo sem o mod no pacote.");
        var host=new InstalledSnapshotManifest{List=new GeneralModList(),SyncGameInventory=new Dictionary<string,string>()};
        var extras=engine.PlanSyncExtras(host,appData:data).Where(extra=>extra.Proven).SelectMany(extra=>extra.Files).ToList();
        check(new HashSet<string>(extras.Select(file=>file.Path)).SetEquals(new[]{lua,script}),
            "A limpeza P2P só oferece os arquivos novos registrados.");
        engine.ApplySyncCleanup(extras,host,new string[0],_=>{},data);
        check(File.ReadAllText(Safe.In(game,photo))=="personal photo"&&File.ReadAllText(Safe.In(game,existing))=="new",
            "A limpeza P2P preserva a pasta de fotos e o arquivo preexistente.");
        engine.Undo(_=>{});
        installer.Remove(receipt,new[]{"emoji/default"},null);
        check(!File.Exists(Safe.In(game,lua))&&!File.Exists(Safe.In(game,script))&&
            File.ReadAllText(Safe.In(game,photo))=="personal photo"&&File.ReadAllText(Safe.In(game,existing))=="new",
            "A desinstalação remove o mod de várias pastas e preserva a foto e o arquivo preexistente.");
    }

    internal static void RunSyncOwnership(string parent) {
        string root=OnlineTests.NestedRoot(parent,"sync-ownership-");Directory.CreateDirectory(root);
        FixedAsset asset;var catalog=OnlineTests.MakeCatalog(root,out asset);
        string game=OnlineTests.Fixture(root,"game"),data=Safe.In(root,"data");
        var engine=new FixedInstaller(catalog,game,true);
        var checks=new List<string>();
        Action<bool,string> check=(ok,name)=>{if(!ok)throw new IOException("PROTEÇÃO SYNC: "+name);checks.Add(name);};
        RunManualCapture(root,catalog,check);
        var host=new InstalledSnapshotManifest{List=new GeneralModList(),Files=new List<InstalledSnapshotFile>(),SyncGameInventory=new Dictionary<string,string>()};
        string known=catalog.Packages[1].Files[0].Path,baseFile=catalog.Packages[0].Files.Last().Path;
        Put(game,known,"test "+known);Put(game,baseFile,"test "+baseFile);
        const string dll="red4ext/plugins/TelemetryBridge/mod.dll",script="r6/scripts/AppearanceTools/main.reds";
        const string changed="red4ext/plugins/Changed/mod.dll";
        string package=Safe.In(root,"Cyberteste.zip");
        using(var zip=System.IO.Compression.ZipFile.Open(package,System.IO.Compression.ZipArchiveMode.Create))
            foreach(string path in new[]{dll,script,changed})using(var writer=new StreamWriter(zip.CreateEntry(path).Open()))writer.Write(path);
        new GeneralModInstaller(game,Path.Combine(data,"Instalacoes")).Install(package,"Cyberteste",_=>{});
        var unknown=new[]{"cache/runtime.bin","red4ext/plugins/cache/runtime.txt","red4ext/plugins/Temp/generated.json",
            "red4ext/plugins/TelemetryBridge/generated.json","red4ext/plugins/TelemetryBridge/nested/extra.dll",
            "bin/x64/plugins/cyber_engine_tweaks/mods/Temp/init.lua",
            "bin/x64/plugins/cyber_engine_tweaks/mods/Temp/runtime.log",
            "bin/x64/plugins/cyber_engine_tweaks/mods/Temp/db.sqlite3",
            "r6/scripts/Temp/generated.reds","r6/tweaks/Temp/generated.yaml"};
        foreach(string path in unknown)Put(game,path,path);
        const string sameBytesElsewhere="red4ext/plugins/Unrelated/mod.dll";
        Put(game,sameBytesElsewhere,dll);
        Directory.CreateDirectory(Safe.In(game,"red4ext/plugins/TelemetryBridge/empty-runtime"));
        Directory.CreateDirectory(Safe.In(game,"r6/scripts/empty-generic/nested"));
        Put(game,changed,"changed at runtime");
        Func<P2PSyncExtra[]> scan=()=>engine.PlanSyncExtras(host,appData:data);
        var review=scan();
        var proven=review.Where(extra=>extra.Proven).SelectMany(extra=>extra.Files).ToArray();
        check(review.Any(extra=>extra.Proven&&new HashSet<string>(extra.Files.Select(file=>file.Path)).SetEquals(new[]{dll,script})),
            "O mesmo recibo agrupa arquivos de diretórios com nomes diferentes na sincronização.");
        var inventory=engine.ReadUnlistedModFiles(new string[0],data);
        var plugin=inventory.Single(file=>file.Path==dll);
        check(plugin.ProvenOwner=="Cyberteste"&&InventoryLinkStore.Key(plugin)==InventoryLinkStore.Key(inventory.Single(file=>file.Path==script))&&
            inventory.Single(file=>file.Path==changed).ProvenOwner==null,
            "O inventário usa o mesmo ID entre diretórios e mantém o arquivo modificado sem identificação comprovada.");
        check(inventory.Single(file=>file.Path=="red4ext/plugins/TelemetryBridge/generated.json").ProvenOwner==null&&
            inventory.Single(file=>file.Path==sameBytesElsewhere).ProvenOwner==null,
            "Arquivos vizinhos e cópias com o mesmo hash em um caminho sem registro permanecem sem identificação comprovada.");
        check(new HashSet<string>(proven.Select(file=>file.Path)).SetEquals(new[]{known,dll,script}),
            "Só caminho e conteúdo exatos do catálogo ou recibo comprovam o vínculo, inclusive em múltiplas pastas.");
        check(review.Any(extra=>!extra.Proven&&extra.Files.Any(file=>file.Path==changed)),
            "Arquivo registrado com conteúdo modificado fica desconhecido, não autorizado pelo nome da pasta.");
        check(review.Any(extra=>!extra.Proven&&extra.Files.Any(file=>file.Path=="red4ext/plugins/TelemetryBridge/generated.json"))&&
            review.Any(extra=>!extra.Proven&&extra.Files.Any(file=>file.Path=="red4ext/plugins/TelemetryBridge/nested/extra.dll")),
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
        var selectedMod=engine.PlanExtraReset(data,InventoryLinkStore.View(inventory,new InventoryLinkData(),new GeneralModEntry[0])
            .Where(file=>InventoryLinkStore.Key(file)==InventoryLinkStore.Key(plugin)).Select(file=>file.Path));
        check(new HashSet<string>(selectedMod.Select(file=>file.Path)).SetEquals(new[]{dll,script}),
            "Selecionar a linha revisa apenas os arquivos comprovados daquele mod em seus vários diretórios.");
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
        check(Directory.Exists(Safe.In(game,"red4ext/plugins/TelemetryBridge/empty-runtime"))&&
            Directory.Exists(Safe.In(game,"r6/scripts/empty-generic/nested"))&&!Directory.Exists(Safe.In(game,"r6/scripts/AppearanceTools")),
            "Apenas ancestrais comprovados e literalmente vazios saem; subpastas desconhecidas permanecem.");
        engine.Undo(_=>{});
        check(proven.All(file=>Safe.Hash(Safe.In(game,file.Path))==file.Before)&&unknown.All(path=>File.ReadAllText(Safe.In(game,path))==path),
            "Desfazer restaura exatamente os arquivos movidos, sem tocar nos desconhecidos.");
        const string oldSetting="bin/x64/plugins/cyber_engine_tweaks/mods/stopNCPD/mod_user_settings.json";
        const string oldLog="bin/x64/plugins/cyber_engine_tweaks/mods/stopNCPD/stopNCPD.log";
        string historicalGame=Safe.In(root,"h"),historicalData=Safe.In(root,"historical-data");
        Directory.Move(OnlineTests.Fixture(root,"historical"),historicalGame);
        Put(historicalGame,oldSetting,"{}");Put(historicalGame,oldLog,"generated locally");
        var officialCatalog=Safe.Json.Deserialize<FixedCatalog>(Safe.Json.Serialize(FixedData.Load()));
        officialCatalog.GameFileVersion=catalog.GameFileVersion;
        var official=new FixedInstaller(officialCatalog,historicalGame,true);
        var historical=official.PlanSyncExtras(host,appData:historicalData);
        var oldFile=historical.Where(extra=>extra.Proven).SelectMany(extra=>extra.Files).Single(file=>file.Path==oldSetting);
        check(historical.Where(extra=>!extra.Proven).SelectMany(extra=>extra.Files).Any(file=>file.Path==oldLog),
            "O NCPD manual é reconhecido pela assinatura do pacote anterior, mas seu log gerado continua desconhecido.");
        check(official.ReadUnlistedModFiles(new string[0]).Any(file=>file.Path==oldSetting&&file.Mod=="Immersive NCPD Hotline"),
            "O inventário identifica o NPCD pelo nome do pacote histórico.");
        check(historical.Any(extra=>extra.Proven&&extra.Name=="Immersive NCPD Hotline"),"A sincronização agrupa o NPCD comprovado.");
        var selection=new[]{oldSetting,oldLog};
        var selectedPlan=official.PlanExtraReset(historicalData,selection);
        check(selectedPlan.Count==1&&selectedPlan[0].Path==oldFile.Path,"Retirar só o mod escolhido preserva arquivos desconhecidos na mesma pasta.");
        official.ApplyExtraReset(selectedPlan,_=>{},historicalData,selection);
        check(!File.Exists(Safe.In(historicalGame,oldSetting))&&File.ReadAllText(Safe.In(historicalGame,oldLog))=="generated locally",
            "A remoção seletiva retira o NCPD comprovado sem ampliar a autorização aos resíduos da mesma pasta.");
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
