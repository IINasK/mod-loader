using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

public static class InstalledSnapshotInstallerTests {
    static List<string> tests;
    public static int Run(string parent,bool testOfficialProject=false) {
        tests = new List<string>();
        string report = Path.Combine(parent, "resultado-snapshot-installer.json");
        try {
            RunChecks(parent,testOfficialProject);
            Safe.WriteJson(report, new { Passed = true, Tests = tests });
            return tests.Count;
        } catch (Exception exception) {
            Safe.WriteJson(report, new { Passed = false, Tests = tests, Error = exception.ToString() });
            throw;
        }
    }
    static void RunChecks(string parent,bool testOfficialProject) {
        string root = OnlineTests.NestedRoot(parent,"online-testes-");
        string target = Path.Combine(root, "game"), data = Path.Combine(root, "data");
        Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, ".fixture-only"), "fixture");
        var catalog = new FixedCatalog { Packages = new List<FixedMod>() };
        var engine = new InstalledSnapshotInstaller(target, data, catalog, true);
        string original = Path.Combine(target, "original.txt"); File.WriteAllText(original, "original");
        string first = Pack(root, "first", catalog, new Dictionary<string,string> {
            { "mods/first.txt", "first" }, { "mods/shared.txt", "shared" }, { "original.txt", "original" }
        });
        string id = engine.Install(first, Safe.Hash(first), null);
        File.Delete(first);
        engine = new InstalledSnapshotInstaller(target, data, catalog, true);
        Check(engine.Status(id, "first/default") == GeneralModInstaller.Installed && engine.ReadManifest(id).List.Mods[0].Id == "first", "Recibo e manifesto sobrevivem à exclusão do ZIP.");
        var scan=new InstalledSnapshotInstaller.StatusScan(engine,Safe.Hash);
        Check(scan.Status(id,"first/default")==GeneralModInstaller.Installed&&scan.Status(id,"missing/default")==GeneralModInstaller.Missing,
            "A varredura indexada confere somente os proprietários ativos do recibo.");
        string installed=Safe.In(target,"mods/first.txt");
        File.WriteAllText(installed,"edits");
        var nextScan=new InstalledSnapshotInstaller.StatusScan(engine,Safe.Hash);
        Check(nextScan.Status(id,"first/default")==GeneralModInstaller.Modified,
            "Uma nova varredura detecta arquivo alterado mesmo quando o tamanho é igual.");
        File.WriteAllText(installed,"first");
        Check(engine.ReadInstalledFiles(id, "first/default").Count == 3, "Arquivos instalados podem ser reexportados sem o ZIP.");
        string second = Pack(root, "second", catalog, new Dictionary<string,string> { { "mods/second.txt", "second" }, { "mods/shared.txt", "shared" } });
        string id2 = engine.Install(second, Safe.Hash(second), null);
        engine.Remove(id, new[] { "first/default" }, null);
        Check(!File.Exists(Safe.In(target, "mods/first.txt")) && File.Exists(Safe.In(target, "mods/shared.txt")) && File.ReadAllText(original) == "original", "Remoção preserva arquivo compartilhado e preexistente.");
        engine.Remove(id2, new[] { "second/default" }, null);
        Check(!File.Exists(Safe.In(target, "mods/shared.txt")) && File.ReadAllText(original) == "original", "O último proprietário remove apenas os arquivos criados pelo instalador.");

        string conflict = Pack(root, "conflict", catalog, new Dictionary<string,string> { { "new/early.txt", "new" }, { "original.txt", "different" } });
        Throws(() => engine.Install(conflict, Safe.Hash(conflict), null), "Arquivo preexistente divergente bloqueia a instalação.");
        Check(!File.Exists(Safe.In(target, "new/early.txt")) && File.ReadAllText(original) == "original", "Conflito impede alterações de todo o lote.");
        string rollback = Pack(root, "rollback", catalog, new Dictionary<string,string> { { "rollback/one.txt", "one" }, { "rollback/two.txt", "two" } });
        Throws(() => engine.Install(rollback, Safe.Hash(rollback), null, 1), "Falha de teste após a primeira cópia é propagada.");
        Check(!Directory.Exists(Safe.In(target, "rollback")), "Falha após a cópia desfaz o lote e limpa diretórios criados.");
        Check(!Directory.GetDirectories(target, ".mod-loader-snapshot-*").Any(), "Preparação temporária no volume de destino é limpa após falha.");
        Check(!Directory.GetDirectories(data, "stage-*", SearchOption.AllDirectories).Any(), "Os dados do programa não recebem arquivos descompactados em estágio.");
        string edited = Pack(root, "edited", catalog, new Dictionary<string,string> { { "edited.txt", "original-mod" } });
        string editedId = engine.Install(edited, Safe.Hash(edited), null);
        File.WriteAllText(Safe.In(target, "edited.txt"), "user-edit");
        Check(engine.Status(editedId, "edited/default") == GeneralModInstaller.Modified, "Detecta arquivo alterado.");
        engine.Remove(editedId, new[] { "edited/default" }, null);
        Check(File.ReadAllText(Safe.In(target, "edited.txt")) == "user-edit", "Remoção preserva alterações posteriores.");
        string concurrent2 = Pack(root, "concurrent2", catalog, new Dictionary<string,string> { { "concurrent2/one.txt", "one" }, { "concurrent2/two.txt", "two" } });
        Throws(() => engine.Install(concurrent2, Safe.Hash(concurrent2), message => {
            if (message == "Instalado: concurrent2/one.txt") File.WriteAllText(Safe.In(target, "concurrent2/one.txt"), "user-change");
        }, 2), "Falha de teste após duas cópias aciona reversão com edição concorrente.");
        Check(File.ReadAllText(Safe.In(target, "concurrent2/one.txt")) == "user-change" && !File.Exists(Safe.In(target, "concurrent2/two.txt")), "Reversão preserva edição concorrente e retira os outros arquivos.");
        string otherTarget = Path.Combine(root, "other"); Directory.CreateDirectory(otherTarget);
        Check(new InstalledSnapshotInstaller(otherTarget, data, catalog).Status(id, "first/default") == GeneralModInstaller.Missing, "Recibo pertence apenas à pasta original.");
        Throws(() => new InstalledSnapshotInstaller(otherTarget, data, catalog, true), "Exceção de teste exige o marcador de fixture isolada.");
        string bad = Pack(root, "bad", catalog, new Dictionary<string,string> { { "bad.txt", "bad" } });
        Throws(() => engine.Install(bad, new string('0', 64), null), "Hash incorreto do pacote é recusado antes de instalar.");

        ConfigTransactions(root);
        ResumeTransactions(root,catalog);
        if(testOfficialProject){
            TestPortableDestinations(root,parent,catalog);
            BuiltInProjectReceive(root,catalog);
        }
    }
    static void TestPortableDestinations(string root,string parent,FixedCatalog catalog){
        string sender=Safe.In(root,"portable/sender/game"),receiver=Safe.In(root,"portable/receiver/game");
        string senderData=Safe.In(root,"portable/sender/data"),inbox=Safe.In(root,"portable/receiver/data");
        string senderProfile=Safe.In(root,"portable/sender/personal"),receiverProfile=Safe.In(root,"portable/receiver/personal");
        foreach(string folder in new[]{Safe.In(sender,"Addons"),Safe.In(receiver,"Addons"),
            Safe.In(senderProfile,"ExampleGame/Mods"),Safe.In(senderProfile,"ExampleGame/Optional"),Safe.In(receiverProfile,"ExampleGame/Mods")})
            Directory.CreateDirectory(folder);
        string url="https://www.nexusmods.com/cyberpunk2077/mods/123?tab=files&file_id=1";
        string archive=Safe.In(root,"portable/mod.zip");
        using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create)){
            foreach(string name in new[]{"game/core.txt","personal/prefs.txt"})
                using(var writer=new StreamWriter(zip.CreateEntry(name).Open()))writer.Write(name);
        }
        var state=new GeneralModState{Destination=sender,ProjectName="Example Game",SelectedPreset="solo"};
        GeneralInstallRoutes.FixtureAppData=senderProfile;
        try{
            var mixed=RoutedEntry("mixed",url);
            mixed.Variants[0].InstallRoutes=GeneralInstallRoutes.Parse("game/core.txt = Addons\npersonal = "+Safe.In(senderProfile,"ExampleGame/Mods"),sender);
            string displayRoutes=GeneralInstallRoutes.Format(mixed.Variants[0].InstallRoutes);
            if(displayRoutes.Contains("\n")||!displayRoutes.Contains(" | ")||
                !GeneralInstallRoutes.Same(mixed.Variants[0].InstallRoutes,GeneralInstallRoutes.Parse(displayRoutes,sender)))
                throw new IOException("Legacy multiline routes must round-trip through the single-line editor.");
            Check(mixed.Variants[0].InstallRoutes[1].Destination=="%APPDATA%/ExampleGame/Mods","O destino absoluto do remetente vira uma referência portável.");
            var individual=GeneralInstallRoutes.Parse("game = Addons | game/core.txt = Config",sender);
            Check(GeneralInstallRoutes.Map("game/core.txt",individual)=="Config/core.txt"&&
                GeneralInstallRoutes.Map("game/other.txt",individual)=="Addons/other.txt",
                "O destino de um arquivo prevalece sobre o destino de sua pasta.");
            var missing=RoutedEntry("missing",url);
            missing.Variants[0].InstallRoutes=GeneralInstallRoutes.Parse("%APPDATA%/ExampleGame/Optional",sender);
            var plain=RoutedEntry("plain",url);
            var dependent=RoutedEntry("dependent",url);
            dependent.Variants[0].RequiredMods.Add(missing.Id);
            state.List.Mods.AddRange(new[]{mixed,missing,plain,dependent});
            state.List.Presets.Add(new GeneralModPreset{Id="solo",Name="Solo",ModIds=state.List.Mods.Select(mod=>mod.Id).ToList()});
            var installer=new GeneralModInstaller(sender,Path.Combine(senderData,"Instalacoes"));
            foreach(var mod in state.List.Mods)
                installer.Install(archive,mod.Id+"/default/0/"+url,null,installRoutes:mod.Variants[0].InstallRoutes);
            Check(File.Exists(Safe.In(sender,"Addons/core.txt"))&&File.Exists(Safe.In(senderProfile,"ExampleGame/Mods/prefs.txt"))&&
                installer.Status("mixed/default/0/"+url)==GeneralModInstaller.Installed,
                "Um pacote instala arquivo e pasta em destinos próprios e registra ambos no recibo.");
            Throws(()=>GeneralInstallRoutes.Parse("%APPDATA%/Microsoft/Windows/Start Menu/Programs/Startup",sender),
                "Destinos de inicialização do Windows são recusados.");
            string snapshot=Safe.In(root,"portable/pack.zip");
            var manifest=InstalledSnapshot.Create(snapshot,state,state.List.Mods.Select(mod=>mod.Id),catalog,senderData,false,null);
            string manifestText=Safe.Json.Serialize(manifest);
            Check(manifest.Schema==2&&manifest.ProjectName=="Example Game"&&!manifestText.Contains("sender")&&manifestText.Contains("%APPDATA%"),
                "O manifesto portável contém o nome do projeto e omite os caminhos locais do remetente.");
            GeneralInstallRoutes.FixtureAppData=receiverProfile;
            var receiving=new InstalledSnapshotInstaller(receiver,inbox,catalog);
            string hash=Safe.Hash(snapshot);
            string approved=null;
            string receipt=receiving.Install(snapshot,hash,null,approveUserFolders:folders=>{approved=folders.Single();return true;});
            Check(approved==Safe.In(receiverProfile,"ExampleGame/Mods")&&File.Exists(Safe.In(receiver,"Addons/core.txt"))&&
                File.Exists(Safe.In(receiverProfile,"ExampleGame/Mods/prefs.txt"))&&File.Exists(Safe.In(receiver,"game/core.txt")),
                "O receptor confirma a pasta pessoal local e instala as partes pessoal e do jogo.");
            Check(receiving.ReadSkippedDestinations(receipt).SequenceEqual(new[]{"missing","dependent"})&&
                receiving.Status(receipt,"missing/default")==GeneralModInstaller.Missing&&
                !Directory.Exists(Safe.In(receiverProfile,"ExampleGame/Optional")),
                "Pasta ausente pula o mod inteiro, preserva os outros e não cria o destino.");
            var store=new GeneralProjectStore(Safe.In(parent,"q"));
            var project=store.CreateReceived(new P2PSession{Id="ml-"+Guid.NewGuid().ToString("N"),Destination=receiver,ReceiptId=receipt},inbox,catalog);
            var received=store.Load(project);
            var pending=received.List.Mods.Single(mod=>mod.Id=="missing");
            Check(project.Name=="Example Game"&&received.SnapshotReceipts.Count==2&&pending.Category=="Não instalados"&&
                pending.Variants[0].Sources.Single()==url&&pending.Variants[0].InstallRoutes[0].Destination=="%APPDATA%/ExampleGame/Optional"&&
                received.List.Presets[0].Categories.Contains("Não instalados")&&
                received.List.Mods.Single(mod=>mod.Id=="dependent").Variants[0].RequiredMods.SequenceEqual(new[]{pending.Id}),
                "O projeto recebido guarda dados, links e regras do mod pulado em uma categoria própria.");
            receiving.Remove(receipt,new[]{"mixed/default"},null);
            Check(!File.Exists(Safe.In(receiver,"Addons/core.txt"))&&!File.Exists(Safe.In(receiverProfile,"ExampleGame/Mods/prefs.txt"))&&
                File.Exists(Safe.In(receiver,"game/core.txt")),"A remoção confere e alcança os dois destinos registrados.");
            receipt=receiving.Install(snapshot,hash,null,approveUserFolders:folders=>false);
            Check(receiving.ReadSkippedDestinations(receipt).SequenceEqual(new[]{"mixed","missing","dependent"})&&
                !File.Exists(Safe.In(receiverProfile,"ExampleGame/Mods/prefs.txt")),
                "Recusar a pasta pessoal também pula o mod completo sem tocar nos demais.");
        }finally{GeneralInstallRoutes.FixtureAppData=null;}
    }

    static GeneralModEntry RoutedEntry(string id,string source){
        return new GeneralModEntry{Id=id,Name=id,DefaultVariant="default",Variants=new List<GeneralModVariant>{
            new GeneralModVariant{Id="default",Name="Default",Sources=new List<string>{source}}}};
    }

    static void BuiltInProjectReceive(string root,FixedCatalog catalog){
        catalog.GameFileVersion="2.5.0.0";
        string target=Path.Combine(root,"cp"),inbox=Path.Combine(root,"inbox"),archive=Path.Combine(root,"official.zip");
        Directory.CreateDirectory(Safe.In(target,"bin/x64"));
        File.WriteAllText(Safe.In(target,"bin/x64/Cyberpunk2077.exe"),"fixture");
        File.WriteAllText(Safe.In(target,".fixture-only"),"fixture");
        var store=new GeneralProjectStore(Path.Combine(root,"l"));
        var mod=new GeneralModEntry{Id="received",Name="Incoming",DefaultVariant="default",
            Variants=new List<GeneralModVariant>{new GeneralModVariant{Id="default",Name="Default",Sources=new List<string>{"https://example.org/mod.zip"}}}};
        var list=new GeneralModList{Mods=new List<GeneralModEntry>{mod},Presets=new List<GeneralModPreset>{
            new GeneralModPreset{Id="all",Name="Com DLC",ModIds=new List<string>{mod.Id}},
            new GeneralModPreset{Id="no-pl",Name="Sem DLC",ModIds=new List<string>{mod.Id}}}};
        var official=store.Create("Cyberpunk 2077",list,"light","pt",target,GeneralProjectStore.CyberpunkTemplate);
        var state=store.Load(official);
        var local=store.Create("Cyberpunk 2077",list,"light","pt",target);
        var partial=P2PService.ExportState(state,"no-pl",new[]{mod.Id});
        var inspected=InstalledSnapshot.Inspect(partial,new[]{mod.Id},catalog,inbox,false,true);
        Check(inspected.TemplateId==GeneralProjectStore.CyberpunkTemplate&&inspected.TemplateSubset,
            "A identidade do pacote oficial atravessa a preparação do preset.");
        var custom=InstalledSnapshot.Inspect(store.Load(local),new[]{mod.Id},catalog,inbox,false,true);
        Check(custom.TemplateId==null,"Um projeto personalizado do mesmo jogo e nome não ganha identidade oficial.");

        var manifest=new InstalledSnapshotManifest{Name="Sem DLC",Game="cyberpunk2077",GameFileVersion=catalog.GameFileVersion,
            TemplateId=inspected.TemplateId,TemplateSubset=true,Project=partial.List};
        var alternate=new GeneralModVariant{Id="alternate",Name="Variant sent",Sources=new List<string>{"https://example.org/alternate.zip"}};
        manifest.Project.Mods.Single().Variants.Add(alternate);
        var transferred=new GeneralModEntry{Id=mod.Id,Name=mod.Name,DefaultVariant=alternate.Id,
            Variants=new List<GeneralModVariant>{alternate}};
        manifest.List.Mods.Add(transferred);
        byte[] bytes=Encoding.UTF8.GetBytes("official fixture");
        string hash=InstalledSnapshot.Hash(bytes);
        manifest.Files.Add(new InstalledSnapshotFile{Path="archive/pc/mod/official.archive",Kind="file",Sha256=hash,Size=bytes.Length,
            Owners=new List<string>{mod.Id+"/alternate"}});
        WritePack(archive,manifest,new Dictionary<string,byte[]>{{hash,bytes}});
        var engine=new InstalledSnapshotInstaller(target,inbox,catalog,true);
        string receipt=engine.Install(archive,Safe.Hash(archive),null);
        var session=new P2PSession{Id="ml-"+Guid.NewGuid().ToString("N"),Destination=target,ReceiptId=receipt};
        var result=store.CreateReceived(session,inbox,catalog);
        var refreshed=store.Load(result);
        Check(result.Id==official.Id&&store.Read().Projects.Count==2&&Directory.GetDirectories(Safe.In(store.Root,"Projetos")).Length==2,
            "Receber o pacote oficial reutiliza o projeto e a pasta existentes, preservando o projeto personalizado.");
        Check(refreshed.SelectedPreset=="no-pl"&&refreshed.List.Presets.Any(item=>item.Id=="all")&&
            refreshed.SnapshotReceipts[mod.Id+"/alternate"]==receipt&&refreshed.ChosenVariants[mod.Id]=="alternate"&&
            refreshed.List.Mods.Single(item=>item.Id==mod.Id).DefaultVariant=="default",
            "O projeto oficial preserva o outro preset e ativa a variante efetivamente instalada, mesmo quando o padrão da lista é diferente.");
        var freshStore=new GeneralProjectStore(Path.Combine(root,"n"));
        var first=freshStore.CreateReceived(session,inbox,catalog);
        session.Id="ml-"+Guid.NewGuid().ToString("N");
        var again=freshStore.CreateReceived(session,inbox,catalog);
        Check(first.TemplateId==GeneralProjectStore.CyberpunkTemplate&&again.Id==first.Id&&freshStore.Read().Projects.Count==1,
            "No primeiro recebimento é criado somente o projeto pronto, reutilizado nos próximos convites.");
    }

    static void ResumeTransactions(string parent,FixedCatalog catalog){
        string target=Path.Combine(parent,"resume-game"),data=Path.Combine(parent,"resume-data");Directory.CreateDirectory(target);File.WriteAllText(Safe.In(target,".fixture-only"),"fixture");
        string zip=Pack(parent,"resume",catalog,new Dictionary<string,string>{{"mods/a.txt","aaa"},{"mods/b.txt","bbb"}});var engine=new InstalledSnapshotInstaller(target,data,catalog,true);
        string id=engine.Install(zip,Safe.Hash(zip),null);var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var receipt=(InstalledSnapshotInstaller.Receipt)typeof(InstalledSnapshotInstaller).GetMethod("Read",flags).Invoke(engine,new object[]{id});receipt.Status="installing";receipt.Files.ForEach(f=>f.Applied=false);
        string stage=Safe.In(target,".mod-loader-snapshot-"+id);Directory.CreateDirectory(stage);var last=receipt.Files.Last();string prepared=Safe.In(stage,InstalledSnapshot.Hash(Encoding.UTF8.GetBytes(last.Path)).Substring(0,32));File.Copy(Safe.In(target,last.Path),prepared);File.Delete(Safe.In(target,last.Path));
        typeof(InstalledSnapshotInstaller).GetMethod("Save",flags).Invoke(engine,new object[]{receipt});
        var restarted=new InstalledSnapshotInstaller(target,data,catalog,true);string resumed=restarted.Install(zip,Safe.Hash(zip),null);
        Check(resumed==id&&restarted.Status(id,"resume/default")==GeneralModInstaller.Installed&&!Directory.Exists(stage),"Reinício retoma recibo interrompido, reconhece cópia anterior ao último Save e limpa o estágio.");
        Check(restarted.Install(zip,Safe.Hash(zip),null)==id,"Reinício após concluir reaproveita recibo sem duplicar instalação ou backups.");
        receipt.Status="installing";typeof(InstalledSnapshotInstaller).GetMethod("Save",flags).Invoke(restarted,new object[]{receipt});File.WriteAllText(Safe.In(target,last.Path),"alteração do usuário");
        Throws(()=>restarted.Install(zip,Safe.Hash(zip),null),"Retomada recusa arquivo alterado pelo usuário após a interrupção.");Check(File.ReadAllText(Safe.In(target,last.Path))=="alteração do usuário"&&File.Exists(zip),"Falha na retomada preserva alteração local e pacote original.");
    }

    static void ConfigTransactions(string parent) {
        string target = Path.Combine(parent, "config-game"), data = Path.Combine(parent, "config-data");
        Directory.CreateDirectory(target); File.WriteAllText(Safe.In(target, ".fixture-only"), "fixture");
        byte[] code = Encoding.UTF8.GetBytes("fixed fixture code");
        var recipe = new FixedMod {
            Id = "FixtureMod", Name = "Fixture mod", Profile = "Other", Dependencies = new string[0],
            Assets = new List<FixedAsset> { new FixedAsset { Id = "fixture", Url = "https://www.nexusmods.com/cyberpunk2077/mods/1" } },
            Files = new List<FixedFile> { new FixedFile { Path = "r6/scripts/fixture.reds", Sha256 = InstalledSnapshot.Hash(code), Size = code.Length } }
        };
        string configPath = "red4ext/plugins/mod_settings/user.ini";
        var catalog = new FixedCatalog {
            GameFileVersion = "2.5.0.0", Packages = new List<FixedMod> { recipe },
            Defaults = new List<FixedDefault> {
                new FixedDefault { ModId = recipe.Id, Path = configPath, Format = "ini-section", Section = "Selected" },
                new FixedDefault { ModId = recipe.Id, Path = configPath, Format = "ini-section", Section = "SelectedTwo" }
            }
        };
        var list = GeneralModLibrary.Seed(catalog, new ModImportItem[0]);
        var manifest = new InstalledSnapshotManifest { Name = "Config fixture", Game = "cyberpunk2077", GameFileVersion = catalog.GameFileVersion, List = list };
        string owner = "FixtureMod/default";
        manifest.Files.Add(new InstalledSnapshotFile { Path = recipe.Files[0].Path, Kind = "file", Sha256 = recipe.Files[0].Sha256, Size = code.Length, Owners = new List<string> { owner } });
        manifest.Files.Add(Config(configPath, "Selected", "{\"Value\":\"new\"}", owner));
        manifest.Files.Add(Config(configPath, "SelectedTwo", "{\"Other\":\"7\"}", owner));
        string ini = Safe.In(target, configPath); Directory.CreateDirectory(Path.GetDirectoryName(ini));
        string original = "; keep comment\r\n[Selected]\r\nValue = old\r\n[Unrelated]\r\nToken = local\r\n";
        File.WriteAllText(ini, original, new UTF8Encoding(false));
        string zip = Path.Combine(parent, "configs.zip"); WritePack(zip, manifest, new Dictionary<string,byte[]> { { manifest.Files[0].Sha256, code } });
        var engine = new InstalledSnapshotInstaller(target, data, catalog, true);
        Throws(() => engine.Install(zip, Safe.Hash(zip), null, 2), "Falha depois da configuração escrita é propagada.");
        Check(File.ReadAllText(ini) == original && !File.Exists(Safe.In(target, recipe.Files[0].Path)), "Falha restaura a configuração original e remove o código novo.");
        string id = engine.Install(zip, Safe.Hash(zip), null);
        string text = File.ReadAllText(ini);
        Check(text.Contains("Value = new") && text.Contains("Other = 7") && text.Contains("Token = local") && text.Contains("; keep comment"), "Seções selecionadas são agrupadas sem perder tokens ou comentários não relacionados.");
        engine.Remove(id, new[] { owner }, null);
        Check(File.ReadAllText(ini) == original && !File.Exists(Safe.In(target, recipe.Files[0].Path)), "Remoção restaura o backup da configuração preexistente.");
        id = engine.Install(zip, Safe.Hash(zip), null);
        File.WriteAllText(ini, File.ReadAllText(ini).Replace("Value = new", "Value = changed"));
        Check(engine.Status(id, owner) == GeneralModInstaller.Installed, "Alterar configuração editável mantém o código como instalado.");
        Check(engine.ReadInstalledFiles(id, owner).Single(f => f.Section == "Selected").Content.Contains("changed"), "Reexportação lê a configuração atual sanitizada.");
        File.AppendAllText(ini, "\n; edit after install\n");
        engine.Remove(id, new[] { owner }, null);
        Check(File.ReadAllText(ini).Contains("; edit after install"), "Configuração editada depois é preservada.");
    }

    static InstalledSnapshotFile Config(string path, string section, string content, string owner) {
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        return new InstalledSnapshotFile { Path = path, Section = section, Content = content, Kind = "ini-section", Size = bytes.Length, Sha256 = InstalledSnapshot.Hash(bytes), Owners = new List<string> { owner } };
    }
    static string Pack(string parent, string mod, FixedCatalog catalog, Dictionary<string,string> files) {
        var manifest = new InstalledSnapshotManifest { Name = mod, Game = "generic" };
        manifest.List.Mods.Add(new GeneralModEntry { Id = mod, Name = mod, DefaultVariant = "default", Variants = new List<GeneralModVariant> { new GeneralModVariant { Id = "default", Name = "Default", Sources = new List<string> { "installed://" + mod + "/default" } } } });
        var entries = new Dictionary<string,byte[]>();
        foreach (var file in files) {
            byte[] bytes = Encoding.UTF8.GetBytes(file.Value); string hash = InstalledSnapshot.Hash(bytes);
            manifest.Files.Add(new InstalledSnapshotFile { Path = file.Key, Kind = "file", Sha256 = hash, Size = bytes.Length, Owners = new List<string> { mod + "/default" } });
            entries[hash] = bytes;
        }
        InstalledSnapshot.Validate(manifest, catalog);
        string path = Path.Combine(parent, mod + ".zip"); WritePack(path, manifest, entries); return path;
    }
    static void WritePack(string path, InstalledSnapshotManifest manifest, Dictionary<string,byte[]> entries) {
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create)) {
            using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open(), new UTF8Encoding(false))) writer.Write(Safe.Json.Serialize(manifest));
            foreach (var entry in entries) using (var output = zip.CreateEntry("files/" + entry.Key).Open()) output.Write(entry.Value, 0, entry.Value.Length);
        }
    }
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); tests.Add(message); }
    static void Throws(Action action, string message) { try { action(); } catch (IOException) { tests.Add(message); return; } throw new Exception(message + " A operação insegura foi aceita."); }
}
