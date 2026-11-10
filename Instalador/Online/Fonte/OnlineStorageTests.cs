using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public static class OnlineStorageTests {
    public static int Run(string parent) {
        var previous=StorageMaintenance.OtherLoaderProbe;StorageMaintenance.OtherLoaderProbe=()=>false;
        try{return RunCases(parent);}finally{StorageMaintenance.OtherLoaderProbe=previous;}
    }
    static int RunCases(string parent) {
        string root=Path.Combine(Path.GetFullPath(parent),"online-testes-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var tests=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new IOException(name);tests.Add(name);};
        FixedAsset asset;var catalog=OnlineTests.MakeCatalog(root,out asset);var cache=new DownloadCache(Path.Combine(root,"cache"));
        cache.Import(Path.Combine(root,"fixture.zip"),asset);
        string original=Safe.Hash(cache.PathFor(asset));
        string saves=Path.Combine(root,"saves");Directory.CreateDirectory(saves);File.WriteAllText(Path.Combine(saves,"save.dat"),"fixture preserved");
        string game=OnlineTests.Fixture(root,"success");var engine=new FixedInstaller(catalog,cache,game,saves,true);
        engine.Install(new[]{"Extra"},true,s=>{});
        check(Empty(cache),"Instalação concluída remove sua preparação temporária.");
        check(FixedData.Scan(catalog.Packages[1],game).IsCorrect,"Limpeza preserva os arquivos instalados e conferidos.");
        check(cache.Contains(asset)&&Safe.Hash(cache.PathFor(asset))==original,"Limpeza preserva o pacote comprimido para reinstalar.");
        check(Directory.GetFiles(Path.Combine(game,".CoopModsOnline","backups"),"save.dat",SearchOption.AllDirectories).Length==1,"Backup do save sintético permanece disponível.");
        engine.Undo(s=>{});
        check(!File.Exists(Path.Combine(game,"archive","pc","mod","test-a.archive")),"Desfazer funciona depois de eliminar a preparação.");
        TestSaveBackupRotation(root,catalog,cache,saves,check);

        string failure=OnlineTests.Fixture(root,"failure");bool failed=false;
        try{new FixedInstaller(catalog,cache,failure,saves,true).Install(new[]{"Extra"},true,s=>{},1);}catch(IOException){failed=true;}
        check(failed&&Empty(cache),"Falha de cópia e rollback não deixam preparação extraída.");
        var broken=Safe.Json.Deserialize<FixedCatalog>(Safe.Json.Serialize(catalog));broken.Packages[1].Files[0].Sha256=new string('0',64);
        failed=false;try{new FixedInstaller(broken,cache,OnlineTests.Fixture(root,"bad"),saves,true).Install(new[]{"Extra"},true,s=>{});}catch(IOException){failed=true;}
        check(failed&&Empty(cache),"Falha ao conferir a extração também remove a preparação.");
        string outside=Path.Combine(root,"outside");Directory.CreateDirectory(outside);File.WriteAllText(Path.Combine(outside,"sentinel.txt"),"keep");
        check(!cache.CleanPreparation(outside)&&File.Exists(Path.Combine(outside,"sentinel.txt")),"Limpeza recusa pasta fora de preparacao.");
        string arbitrary=Path.Combine(cache.Root,"preparacao","personal");Directory.CreateDirectory(arbitrary);File.WriteAllText(Path.Combine(arbitrary,"sentinel.txt"),"keep");
        check(!cache.CleanPreparation(arbitrary)&&File.Exists(Path.Combine(arbitrary,"sentinel.txt")),"Limpeza recusa pasta que não possui ID de transação.");
        check(File.ReadAllText(Path.Combine(saves,"save.dat"))=="fixture preserved","Save original permanece idêntico.");
        string orphan=Preparation(cache,"orphan"),active=Preparation(cache,"active"),young=Preparation(cache,"young");
        using(cache.HoldPreparation(active)){
            Directory.SetLastWriteTimeUtc(orphan,DateTime.UtcNow.AddDays(-2));Directory.SetLastWriteTimeUtc(active,DateTime.UtcNow.AddDays(-2));
            new DownloadCache(cache.Root);
            check(Directory.Exists(active)&&Directory.Exists(orphan),"Manutenção não remove preparações enquanto uma operação mantém lease ativo.");
            bool refused=false;try{cache.ClearDownloads();}catch(IOException){refused=true;}
            check(refused&&cache.Contains(asset),"Limpar downloads é bloqueado por uma operação em andamento.");
        }
        StorageMaintenance.OtherLoaderProbe=()=>true;
        new DownloadCache(cache.Root);
        check(Directory.Exists(orphan),"Outro Mod Loader aberto impede a limpeza de órfãos antigos.");
        bool otherRefused=false;try{cache.ClearDownloads();}catch(IOException){otherRefused=true;}
        check(otherRefused&&cache.Contains(asset),"Outro Mod Loader aberto também impede a limpeza explícita do cache.");
        StorageMaintenance.OtherLoaderProbe=()=>false;
        new DownloadCache(cache.Root);
        check(!Directory.Exists(orphan)&&!Directory.Exists(active),"Abertura recolhe preparações órfãs com mais de 24 horas.");
        check(Directory.Exists(young)&&Directory.Exists(arbitrary),"Preparações recentes e pastas desconhecidas são preservadas.");
        check(cache.Contains(asset),"Manutenção automática não apaga pacotes comprimidos.");
        string unrelated=Path.Combine(cache.Root,"user.cache");File.WriteAllText(unrelated,"keep");
        string changed=Path.Combine(cache.Root,new string('a',64)+".cache");
        string locked=Path.Combine(cache.Root,new string('b',64)+".cache");
        string arrived=Path.Combine(cache.Root,new string('c',64)+".cache");
        File.WriteAllText(changed,"before");File.WriteAllText(locked,"busy");
        var preview=cache.PreviewRetainedDownloads();
        check(preview.Count==3&&preview.Bytes==asset.Size+10&&cache.Contains(asset),"A prévia conta apenas cópias do cache e não altera arquivos.");
        File.WriteAllText(changed,"different length");File.WriteAllText(arrived,"new after consent");
        int skipped;long removed;
        using(var guard=new FileStream(locked,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
            removed=preview.Apply(out skipped);
        check(removed==asset.Size&&skipped==2&&!File.Exists(cache.PathFor(asset)),"Limpeza explícita informa bytes removidos e itens ignorados.");
        check(File.Exists(changed)&&File.Exists(locked)&&File.Exists(arrived),"Limpeza preserva arquivos bloqueados, alterados ou que chegaram depois da prévia.");
        check(File.ReadAllText(unrelated)=="keep"&&File.Exists(Path.Combine(root,"fixture.zip")),"Limpar cache não apaga arquivos desconhecidos nem o download original.");
        check(Directory.GetFiles(Path.Combine(game,".CoopModsOnline","backups"),"save.dat",SearchOption.AllDirectories).Length==1,"Limpar cache preserva os backups usados para desfazer.");
        var absent=new DownloadCache(Path.Combine(root,"absent-cache"));check(absent.ClearDownloads()==0&&!Directory.Exists(absent.Root),"Cache ausente não cria pasta ao ser limpo.");
        TestCyberpunkRoutes(root,catalog,cache,check);
        string logs=Path.Combine(root,"logs");Directory.CreateDirectory(logs);
        for(int i=0;i<12;i++){
            string file=Path.Combine(logs,"diagnostico-20260101-000000-"+i.ToString("x8")+".jsonl");
            using(var output=File.Create(file))output.SetLength(8L*1024*1024);
            File.SetLastWriteTimeUtc(file,DateTime.UtcNow.AddDays(-12+i));
        }
        string protectedLog=Path.Combine(logs,"diagnostico-20260101-000000-00000000.jsonl");
        string unknownLog=Path.Combine(logs,"personal.txt");File.WriteAllText(unknownLog,"keep");
        using(var guard=new FileStream(protectedLog,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){
            OnlineDiagnostics.Start(logs);OnlineDiagnostics.Record("storage-fixture",null,"fixture");
            check(OnlineDiagnostics.Enabled&&File.Exists(protectedLog),"Rotação preserva um registro em uso e o logger continua funcionando.");
            OnlineDiagnostics.Stop();
        }
        StorageMaintenance.RotateLogs(logs);
        var remaining=new DirectoryInfo(logs).GetFiles("diagnostico-*.jsonl");
        check(remaining.Length<=8&&remaining.Sum(file=>file.Length)<=64L*1024*1024,"Rotação limita históricos a oito arquivos e 64 MiB.");
        check(File.ReadAllText(unknownLog)=="keep","Rotação não apaga arquivos que não pertencem ao logger.");
        OnlineDiagnostics.Start(logs);
        remaining=new DirectoryInfo(logs).GetFiles("diagnostico-*.jsonl");
        check(remaining.Length<=8&&remaining.Sum(file=>file.Length)<=64L*1024*1024,"Abrir uma sessão reserva espaço para o registro atual dentro do limite.");
        OnlineDiagnostics.Stop();
        Safe.WriteJson(Path.Combine(root,"resultado-armazenamento.json"),new{Passed=true,Tests=tests,RealGameTouched=false,NetworkUsed=false});return tests.Count;
    }
    static void TestSaveBackupRotation(string root,FixedCatalog catalog,DownloadCache cache,string saves,Action<bool,string> check){
        string game=OnlineTests.Fixture(root,"r");
        string data=Path.Combine(game,".CoopModsOnline"), backups=Path.Combine(data,"backups");
        string latest=Path.Combine(backups,"saves-current"), original=Path.Combine(saves,"save.dat");
        var engine=new FixedInstaller(catalog,cache,game,saves,true);
        Action install=()=>engine.Install(new[]{"Extra"},true,s=>{});
        install();
        string legacyId=Directory.GetFiles(data,"*.json").Single();
        string legacy=Path.Combine(backups,Path.GetFileNameWithoutExtension(legacyId),"saves");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy));
        Directory.Move(latest,legacy);
        check(engine.PreviewOldSaveBackups().Count==0&&File.Exists(Path.Combine(legacy,"save.dat")),
            "Sem uma cópia rotativa, o backup legado mais recente fica disponível.");
        engine.Undo(s=>{});
        install();
        string copy=Path.Combine(latest,"save.dat");
        File.SetLastWriteTimeUtc(copy,new DateTime(2020,1,1,0,0,0,DateTimeKind.Utc));
        engine.Undo(s=>{});
        install();
        check(File.GetLastWriteTimeUtc(copy).Year==2020,"Operações com os mesmos saves reutilizam o backup já conferido.");
        string previous=Path.Combine(backups,"saves-previous"), pending=Path.Combine(backups,"saves-pending");
        engine.Undo(s=>{});
        Directory.Move(latest,previous);
        Directory.CreateDirectory(pending);
        File.WriteAllText(Path.Combine(pending,"incomplete"),"partial");
        install();
        check(File.GetLastWriteTimeUtc(copy).Year==2020&&!Directory.Exists(previous)&&!Directory.Exists(pending),
            "Após interrupção durante a troca, a próxima operação recupera a cópia conferida e descarta a preparação incompleta.");
        File.WriteAllText(original,"new state");
        engine.Undo(s=>{});
        install();
        check(File.ReadAllText(copy)=="new state"&&!Directory.Exists(previous)&&!Directory.Exists(pending),
            "Uma mudança no save atualiza a cópia rotativa sem criar um novo histórico.");
        var plan=engine.PreviewOldSaveBackups();
        check(plan.Count==1&&plan.Bytes==new FileInfo(Path.Combine(legacy,"save.dat")).Length&&Directory.Exists(legacy),
            "A prévia contabiliza somente a cópia histórica autenticada.");
        string added=Path.Combine(legacy,"new-after-preview.dat");
        File.WriteAllText(added,"keep");
        int skipped;
        check(plan.Apply(out skipped)==0&&skipped==1&&File.Exists(added),
            "Uma cópia que mudou depois da prévia não é apagada.");
        File.Delete(added);
        string unknown=Path.Combine(backups,new string('f',32),"saves");
        Directory.CreateDirectory(unknown);
        File.WriteAllText(Path.Combine(unknown,"save.dat"),"unknown");
        string modBackup=Path.Combine(Path.GetDirectoryName(legacy),"desativados","original.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(modBackup));
        File.WriteAllText(modBackup,"keep mod backup");
        plan=engine.PreviewOldSaveBackups();
        check(plan.Apply(out skipped)==plan.Bytes&&skipped==0&&!Directory.Exists(legacy)&&File.Exists(Path.Combine(unknown,"save.dat"))&&
            File.ReadAllText(copy)=="new state"&&File.ReadAllText(original)=="new state"&&File.ReadAllText(modBackup)=="keep mod backup",
            "Limpeza libera o histórico aprovado e preserva os saves originais, o último backup e pastas sem registro.");
        engine.Undo(s=>{});
        check(!File.Exists(Safe.In(game,catalog.Packages[1].Files[0].Path)),"Desfazer a instalação continua funcionando após limpar cópias de saves.");
        File.WriteAllText(original,"fixture preserved");
    }
    static void TestCyberpunkRoutes(string root,FixedCatalog catalog,DownloadCache cache,Action<bool,string> check){
        string game=OnlineTests.Fixture(root,"routes");
        string source=Safe.In(root,"routed.zip");
        using(var zip=System.IO.Compression.ZipFile.Open(source,System.IO.Compression.ZipArchiveMode.Create))
            using(var writer=new StreamWriter(zip.CreateEntry("package/item.archive").Open()))writer.Write("custom archive");
        string sha=Safe.Hash(source);
        var item=new ModImportItem{Id="file-"+sha,Name=Path.GetFileName(source),Source=source,Sha256=sha,Size=new FileInfo(source).Length,Kind="Arquivo"};
        var routes=GeneralInstallRoutes.Parse("package/item.archive = archive/pc/mod/Custom",game);
        Directory.CreateDirectory(Safe.In(game,"archive/pc/mod/Custom"));
        var engine=new FixedInstaller(catalog,cache,game,null,true);
        engine.InstallImported(item,s=>{},routes:routes);
        string destination=Safe.In(game,"archive/pc/mod/Custom/item.archive");
        string id=LocalModPackage.RoutedId(sha,routes);
        check(id!=item.Id&&File.Exists(destination)&&engine.InstalledImports().Contains(id)&&!File.Exists(Safe.In(game,"package/item.archive")),
            "O importador usa a rota por arquivo e registra a nova localização.");
        engine.RemoveImported(id,s=>{});
        check(!File.Exists(destination)&&File.Exists(source),"A desinstalação do importado usa o destino registrado e preserva o download.");
        bool refused=false;
        try{engine.InstallImported(item,s=>{},routes:GeneralInstallRoutes.Parse("package/item.archive = bin/x64",game));}
        catch(IOException){refused=true;}
        check(refused&&!File.Exists(Safe.In(game,"bin/x64/item.archive")),"A rota do Cyberpunk preserva as pastas fora das áreas de mods.");
    }
    static bool Empty(DownloadCache cache){string path=Path.Combine(cache.Root,"preparacao");return !Directory.Exists(path)||Directory.GetDirectories(path).Length==0;}
    static string Preparation(DownloadCache cache,string contents){string path=Path.Combine(cache.Root,"preparacao",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path,"file.txt"),contents);return path;}
}
