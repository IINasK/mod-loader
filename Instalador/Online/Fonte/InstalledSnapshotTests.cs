using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

internal static class InstalledSnapshotTests {
    internal static int Run(string parent) {
        string root=OnlineTests.NestedRoot(parent,"ss-");Directory.CreateDirectory(root);
        var checks=new List<string>();
        Action<bool,string> check=(ok,label)=>{if(!ok)throw new IOException(label);checks.Add(label);};
        Action<Action,string> refuse=(action,label)=>{try{action();}catch(IOException){checks.Add(label);return;}throw new IOException("Não bloqueou: "+label);};
        var catalog=new FixedCatalog{Schema=1,Id="fixture",GameVersion="2.5",GameFileVersion="2.5.0.0",Packages=new List<FixedMod>(),Defaults=new List<FixedDefault>()};
        string game=Path.Combine(root,"g"),data=Path.Combine(root,"d"),original=Path.Combine(root,"original.zip");Directory.CreateDirectory(game);Directory.CreateDirectory(data);
        var state=State(game,original);string work="fixture/default";
        Write(game,"mods/fixture.txt","installed content");Write(game,"mods/copy.txt","installed content");
        WriteGenericRecord(game,data,work+"/0/"+original,new[]{"mods/fixture.txt","mods/copy.txt"});
        File.WriteAllText(original,"old download");File.Delete(original);
        string zip=Path.Combine(root,"p.zip");var manifest=InstalledSnapshot.Create(zip,state,new[]{"fixture"},catalog,data,true,s=>{});
        check(manifest.Files.Count==2,"Exporta arquivos instalados por recibo sem precisar do ZIP original.");
        check(manifest.List.Mods.Single().Variants.Single().Sources.Single()=="installed://fixture/default","Substitui fonte privada por identidade de instalação.");
        check(!Safe.Json.Serialize(manifest).Contains(root.Replace("\\","\\\\")),"Não serializa caminhos privados do remetente.");
        var read=InstalledSnapshot.Read(zip,Safe.Hash(zip),catalog);
        check(read.Files.All(f=>f.Sha256==Safe.Hash(Safe.In(game,"mods/fixture.txt"))),"ZIP e bytes instalados conferidos por hashes.");
        using(var archive=ZipFile.OpenRead(zip))check(archive.Entries.Count==2&&archive.GetEntry("manifest.json")!=null&&archive.GetEntry("files/"+manifest.Files[0].Sha256)!=null,"Um ZIP contém somente manifesto e blobs deduplicados.");
        check(!Directory.Exists(Path.Combine(data,"SnapshotReadCache")),"Exportação não cria cache de pacotes.");
        var publicState=Copy(state);string page="https://www.nexusmods.com/cyberpunk2077/mods/9083";
        publicState.List.Mods[0].Name="Meu mod personalizado";publicState.List.Mods[0].Variants[0].Sources=new List<string>{page};
        publicState.List.Categories.Add("Minha categoria");publicState.List.CategoryDescriptions["Minha categoria"]="Descrição mantida";
        publicState.List.Presets=new List<GeneralModPreset>{new GeneralModPreset{Id="primeiro",Name="Minha seleção",ModIds=new List<string>{"fixture"}},new GeneralModPreset{Id="segundo",Name="Outro preset",ModIds=new List<string>{"fixture"}}};publicState.SelectedPreset="segundo";
        WriteGenericRecord(game,data,work+"/0/"+page,new[]{"mods/fixture.txt","mods/copy.txt"});
        string publicZip=Path.Combine(root,"public.zip");var publicManifest=InstalledSnapshot.Create(publicZip,publicState,new[]{"fixture"},catalog,data,true,s=>{});
        check(publicManifest.List.Mods[0].Name=="Meu mod personalizado"&&publicManifest.List.Mods[0].Variants[0].Sources.SequenceEqual(new[]{page}),"Preserva nome personalizado e link público original sem exportar caminhos locais.");
        check(publicManifest.List.Presets.Count==2&&publicManifest.List.Presets[0].Id=="segundo"&&publicManifest.List.Presets[1].Name=="Minha seleção"&&publicManifest.List.CategoryDescriptions["Minha categoria"]=="Descrição mantida","Preserva nomes, membros e categorias dos presets e coloca o ativo primeiro.");
        check(InstalledSnapshot.Read(publicZip,Safe.Hash(publicZip),catalog).List.Presets.Count==2,"Metadados preservados continuam válidos na leitura do ZIP.");
        var bodyState=State(game,page);
        var body=bodyState.List.Mods.Single();
        body.Id=GeneralModLibrary.EbbBody;
        var ebb=body.Variants.Single();
        ebb.DisplayName="Corpo EBB";ebb.Description="EBB";
        ebb.NaturalOption=new GeneralModVariant{Id=ebb.Id,Name="Natural",DisplayName="Corpo Natural",Description="Natural",
            Tags=new List<string>{"Natural"},Sources=new List<string>{original}};
        bodyState.BodyChoice=GeneralModLibrary.NaturalBody;
        WriteGenericRecord(game,data,body.Id+"/"+ebb.Id+"/0/"+original,new[]{"mods/fixture.txt"});
        string bodyZip=Path.Combine(root,"body.zip");
        InstalledSnapshot.Create(bodyZip,bodyState,new[]{body.Id},catalog,data,false,s=>{});
        var bodyPack=InstalledSnapshot.Read(bodyZip,Safe.Hash(bodyZip),catalog);
        var effective=bodyPack.List.Mods.Single().Variants.Single();
        var full=bodyPack.Project.Mods.Single().Variants.Single();
        check(bodyPack.BodyChoice==GeneralModLibrary.NaturalBody&&effective.Description=="Natural"&&effective.Tags.SequenceEqual(new[]{"Natural"})&&
            effective.Sources.Single()=="installed://"+body.Id+"/"+ebb.Id&&effective.NaturalOption==null&&
            full.Description=="EBB"&&full.Sources.Single()==page&&full.NaturalOption.Description=="Natural"&&
            full.NaturalOption.Sources.SequenceEqual(effective.Sources)&&!Safe.Json.Serialize(bodyPack).Contains(root.Replace("\\","\\\\")),
            "Snapshot envia os arquivos da opção Natural e mantém as configurações distintas do projeto sem vazar caminhos locais.");
        var skippedBody=Copy(bodyPack);
        skippedBody.Schema=2;
        var missingFolder=GeneralInstallRoutes.Parse("%GAME%/missing-body-folder",game);
        skippedBody.List.Mods.Single().Variants.Single().InstallRoutes=missingFolder;
        skippedBody.Project.Mods.Single().Variants.Single().NaturalOption.InstallRoutes=missingFolder;
        check(InstalledSnapshot.SkipMissingDestinations(skippedBody,catalog,game,null).SequenceEqual(new[]{body.Id})&&
            skippedBody.BodyChoice==null&&skippedBody.Project.Mods.Single().Variants.Single().NaturalOption!=null,
            "Recepção sem o destino do Natural conserva os campos editáveis e solta a escolha instalada.");
        InstalledSnapshot.Validate(skippedBody,catalog);
        full.NaturalOption.Sources[0]=original;
        refuse(()=>InstalledSnapshot.Validate(bodyPack,catalog),"Bloqueia origem privada em uma configuração Natural recebida.");
        refuse(()=>InstalledSnapshot.Read(zip,new string('0',64),catalog),"Recusa hash de convite divergente.");
        Write(game,"mods/fixture.txt","later edit");
        refuse(()=>InstalledSnapshot.Create(Path.Combine(root,"bad.zip"),state,new[]{"fixture"},catalog,data,true,s=>{}),"Recusa arquivo instalado alterado após recibo.");
        check(!File.Exists(Path.Combine(root,"bad.zip")),"Não deixa ZIP de preparação falhada.");
        File.Delete(Safe.In(game,"mods/fixture.txt"));
        refuse(()=>InstalledSnapshot.Create(Path.Combine(root,"missing.zip"),state,new[]{"fixture"},catalog,data,true,s=>{}),"Recusa arquivo instalado ausente.");
        var corrupt=Copy(manifest);corrupt.Files[0].Path="../outside.txt";refuse(()=>InstalledSnapshot.Validate(corrupt,catalog),"Bloqueia traversal de caminho recebido.");
        corrupt=Copy(manifest);corrupt.Files[0].Path="mods/account.sav";refuse(()=>InstalledSnapshot.Validate(corrupt,catalog),"Bloqueia caminho de save em pacote genérico.");
        corrupt=Copy(manifest);corrupt.Files[0].Owners.Add("unknown/default");refuse(()=>InstalledSnapshot.Validate(corrupt,catalog),"Bloqueia proprietário desconhecido.");
        corrupt=Copy(manifest);corrupt.Files.Add(Copy(corrupt.Files[0]));refuse(()=>InstalledSnapshot.Validate(corrupt,catalog),"Bloqueia destinos repetidos.");
        corrupt=Copy(manifest);corrupt.List.Mods[0].Variants[0].Sources[0]=original;refuse(()=>InstalledSnapshot.Validate(corrupt,catalog),"Bloqueia origem privada no manifesto.");
        string extra=Path.Combine(root,"extra.zip");File.Copy(zip,extra);
        using(var archive=ZipFile.Open(extra,ZipArchiveMode.Update))using(var writer=new StreamWriter(archive.CreateEntry("unknown.txt").Open()))writer.Write("extra");
        refuse(()=>InstalledSnapshot.Read(extra,Safe.Hash(extra),catalog),"Bloqueia entrada ZIP não declarada.");
        string duplicate=Path.Combine(root,"dup.zip");File.Copy(zip,duplicate);
        using(var archive=ZipFile.Open(duplicate,ZipArchiveMode.Update))using(var writer=new StreamWriter(archive.CreateEntry("manifest.json").Open()))writer.Write("{}");
        refuse(()=>InstalledSnapshot.Read(duplicate,Safe.Hash(duplicate),catalog),"Bloqueia entradas ZIP com mesmo nome.");
        string badBlob=Path.Combine(root,"blob.zip");File.Copy(zip,badBlob);
        using(var archive=ZipFile.Open(badBlob,ZipArchiveMode.Update)){archive.GetEntry(InstalledSnapshot.Entry(manifest.Files[0])).Delete();using(var writer=new StreamWriter(archive.CreateEntry(InstalledSnapshot.Entry(manifest.Files[0])).Open()))writer.Write("wrong bytes");}
        refuse(()=>InstalledSnapshot.Read(badBlob,Safe.Hash(badBlob),catalog),"Bloqueia blob de tamanho ou hash diferente.");
        var config=Config("{\"volume\":\"3\"}","Chosen");
        string merged=Encoding.UTF8.GetString(InstalledSnapshotConfig.Apply(config,Encoding.UTF8.GetBytes("; keep\r\n[Other]\r\nvalue = 8\r\n[Chosen]\r\nvolume = 1\r\n[Last]\r\nlast = 2\r\n")));
        check(merged.Contains("[Other]\r\nvalue = 8")&&merged.Contains("[Last]\r\nlast = 2"),"Merge preserva seções INI não selecionadas.");
        check(merged.Contains("volume = 3")&&!merged.Contains("volume = 1"),"Merge aplica valores selecionados do remetente.");
        refuse(()=>InstalledSnapshotConfig.Apply(Config("{\"password\":\"private\"}","Chosen"),null),"Bloqueia senha em configuração.");
        refuse(()=>InstalledSnapshotConfig.Apply(Config("{\"api_key\":\"private\"}","Chosen"),null),"Bloqueia chave de API em configuração.");
        refuse(()=>InstalledSnapshotConfig.Apply(Config("{\"folder\":\"C:/Private\"}","Chosen"),null),"Bloqueia caminho local em configuração.");
        refuse(()=>InstalledSnapshotConfig.Apply(Config("{\"url\":\"https://private.example\"}","Chosen"),null),"Bloqueia endereço privado em configuração.");
        refuse(()=>InstalledSnapshotConfig.Apply(config,Encoding.UTF8.GetBytes("[Chosen]\na = 1\n[Chosen]\na = 2")),"Preserva INI ambíguo recusando merge.");
        string customGame=Path.Combine(root,"c"),customData=Path.Combine(root,"cd");Directory.CreateDirectory(customGame);Directory.CreateDirectory(customData);
        Write(customGame,"bin/x64/Cyberpunk2077.exe","fixture only");Write(customGame,"r6/scripts/Fixture/main.reds","// installed fixture");
        string importId="file-"+new string('b',64),receiptId=Guid.NewGuid().ToString("N");
        var receipt=new OnlineReceipt{Id=receiptId,CatalogId=catalog.Id,Operation=LocalModPackage.ReceiptOperation,Status="instalado",CreatedUtc=DateTime.UtcNow.ToString("o"),Mods=new List<string>{importId},Files=new List<OwnedChange>{new OwnedChange{Path="r6/scripts/Fixture/main.reds",After=Safe.Hash(Safe.In(customGame,"r6/scripts/Fixture/main.reds")),Applied=true}}};
        WriteProtectedReceipt(customGame,receipt);
        var customState=State(customGame,original);customState.CyberpunkImports[work+"/0/"+original]=importId;
        string customZip=Path.Combine(root,"custom.zip");var custom=InstalledSnapshot.Create(customZip,customState,new[]{"fixture"},catalog,customData,false,s=>{});
        check(custom.Files.Single().Path=="r6/scripts/Fixture/main.reds","Custom Cyberpunk exportado por recibo DPAPI com ZIP original apagado.");
        check(InstalledSnapshot.Read(customZip,Safe.Hash(customZip),catalog).Files.Count==1,"Custom recebido passa política de caminhos do jogo.");
        var badReceipt=Copy(receipt);badReceipt.Files[0].After=new string('c',64);WriteProtectedReceipt(customGame,badReceipt);
        refuse(()=>InstalledSnapshot.Create(Path.Combine(root,"receipt-changed.zip"),customState,new[]{"fixture"},catalog,customData,false,s=>{}),"Recibo custom com bytes divergentes bloqueia exportação.");
        var manualBody=State(customGame,"https://www.nexusmods.com/cyberpunk2077/mods/4654?tab=files&file_id=170224");
        var hyst=manualBody.List.Mods.Single();hyst.Id="hyst-ebb-rb";
        const string hystMarker="archive/pc/mod/EBBRB.xl";
        check(InstalledSnapshot.Uninstalled(manualBody,hyst,hyst.Variants.Single(),catalog,customGame,customData),
            "Corpo Hyst ausente fica fora do envio normalmente.");
        Write(customGame,hystMarker,"incomplete manual installation");
        bool reviewedHyst=false;
        InstalledSnapshot.Inspect(manualBody,new[]{hyst.Id},catalog,customData,false,true,
            skipProblem:(id,name,error)=>{reviewedHyst=id==hyst.Id;return true;});
        check(reviewedHyst,"Hyst presente sem recibo passa pela revisão em vez de desaparecer silenciosamente.");
        var fixedFeet=Fixed(GeneralModLibrary.VanillaFeetId,"archive/pc/mod/Vanilla_ArchiveXL_FeetComponentsFV.archive","feet",new string[0]);
        catalog.Packages.Add(fixedFeet);
        var sync=new InstalledSnapshotManifest{SyncGameInventory=new Dictionary<string,string>()};
        sync.List.Mods.Add(hyst);
        var feet=State(customGame,original).List.Mods.Single();feet.Variants.Single().CyberpunkId=fixedFeet.Id;
        sync.Project=new GeneralModList();sync.Project.Mods.Add(feet);
        string syncGame=OnlineTests.Fixture(root,"hyst-sync");
        Write(syncGame,fixedFeet.Files[0].Path,"feet");
        var extras=new FixedInstaller(catalog,syncGame).PlanSyncExtras(sync);
        check(extras.Any(candidate=>candidate.Files.Any(file=>file.Path==fixedFeet.Files[0].Path)),
            "Sincronização com Hyst oferece os pés vanilla do receptor para revisão.");
        catalog.Packages.Remove(fixedFeet);
        string secretGame=Path.Combine(root,"s"),secretData=Path.Combine(root,"sd");Directory.CreateDirectory(secretGame);Directory.CreateDirectory(secretData);
        Write(secretGame,"mods/config.json","{\"access_token\":\"private\"}");WriteGenericRecord(secretGame,secretData,work+"/0/"+original,new[]{"mods/config.json"});
        refuse(()=>InstalledSnapshot.Create(Path.Combine(root,"secret.zip"),State(secretGame,original),new[]{"fixture"},catalog,secretData,true,s=>{}),"JSON personalizado não vaza credenciais apesar de recibo válido.");
        check(!File.Exists(Path.Combine(root,"secret.zip")),"Remove ZIP falhado durante inspeção do conteúdo privado.");
        var withoutPrivate=InstalledSnapshot.Inspect(State(secretGame,original),new[]{"fixture"},catalog,secretData,true,true,
            skipProblem:(id,name,error)=>id=="fixture"&&name=="Fixture");
        check(withoutPrivate.List.Mods.Count==0&&withoutPrivate.Files.Count==0&&withoutPrivate.Project.Mods.Count==1,
            "Pular mod com configuração privada não exporta os bytes e mantém sua definição sem caminho local.");
        var core=Fixed("NightCoop","red4ext/RED4ext.dll","pinned core",new string[0]);core.Immutable=true;
        var addon=Fixed("Audioware","red4ext/plugins/Audioware/plugin.dll","pinned addon",new[]{"NightCoop"});
        var independent=Fixed("ArchiveXL","red4ext/plugins/ArchiveXL/ArchiveXL.dll","pinned independent",new string[0]);
        string userPath="bin/x64/plugins/cyber_engine_tweaks/mods/Fixture/settings.json";
        addon.Files.Add(new FixedFile{Asset="asset",Entry="settings.json",Path=userPath,UserData=true,Size=2,Sha256=InstalledSnapshot.Hash(Encoding.UTF8.GetBytes("{}"))});
        var fixedCatalog=new FixedCatalog{Schema=1,Id="fixedfixture",GameVersion="2.5",GameFileVersion="2.5.0.0",Packages=new List<FixedMod>{core,addon,independent},Defaults=new List<FixedDefault>{new FixedDefault{Id="default",ModId=addon.Id,Version=addon.Version,Path="red4ext/plugins/mod_settings/user.ini",Format="ini-section",Section="Audioware.AudiowareConfig",Content="{\"enabled\":\"true\"}",Sha256=InstalledSnapshot.Hash(Encoding.UTF8.GetBytes("{\"enabled\":\"true\"}"))}}};
        string fixedGame=Path.Combine(root,"f"),fixedData=Path.Combine(root,"fd");Directory.CreateDirectory(fixedGame);Directory.CreateDirectory(fixedData);
        Write(fixedGame,"bin/x64/Cyberpunk2077.exe","fixture only");Write(fixedGame,core.Files[0].Path,"pinned core");Write(fixedGame,addon.Files[0].Path,"pinned addon");Write(fixedGame,userPath,"{\"volume\":2}");
        Write(fixedGame,"red4ext/plugins/mod_settings/user.ini","[Other]\npassword = PRIVATE_DO_NOT_SEND\n[Audioware.AudiowareConfig]\nenabled = false\n");
        var fixedState=new GeneralModState{Destination=fixedGame,List=GeneralModLibrary.Seed(fixedCatalog,new ModImportItem[0])};fixedState.List.Categories.Clear();foreach(var item in fixedState.List.Mods)item.Category=null;
        string fixedZip=Path.Combine(root,"fixed.zip");var fixedManifest=InstalledSnapshot.Create(fixedZip,fixedState,new[]{addon.Id},fixedCatalog,fixedData,true,s=>{});
        check(fixedManifest.List.Mods.Count==2&&fixedManifest.List.Mods.Any(m=>m.Id==core.Id),"Inclui dependência fixada ausente da seleção original.");
        check(fixedManifest.List.Categories.Contains("Kit principal"),"Inclui categoria da dependência ausente na lista de origem.");
        check(fixedManifest.Files.Count(f=>f.Kind=="file")==2&&fixedManifest.Files.Count(f=>f.Kind!="file")==2,"Exporta código exato com JSON e seção INI permitidos.");
        check(!Safe.Json.Serialize(fixedManifest).Contains("PRIVATE_DO_NOT_SEND"),"Exporta somente seção INI selecionada sem dados de outra seção.");
        check(fixedManifest.Files.Single(f=>f.Kind=="ini-section").Content.Contains("false"),"Lê configuração atual em vez do padrão embutido.");
        check(fixedManifest.Files.Single(f=>f.Kind=="json").Content.Contains("volume"),"Lê UserData JSON instalado validado.");
        var withoutConfigs=InstalledSnapshot.Create(Path.Combine(root,"no-config.zip"),fixedState,new[]{addon.Id},fixedCatalog,fixedData,false,s=>{});
        check(withoutConfigs.Files.All(f=>f.Kind=="file"),"Opção sem configurações não inclui dados de perfil.");
        corrupt=Copy(fixedManifest);corrupt.Files.RemoveAll(f=>f.Path==core.Files[0].Path);refuse(()=>InstalledSnapshot.Validate(corrupt,fixedCatalog),"Bloqueia ausência de código fixado da base.");
        corrupt=Copy(fixedManifest);corrupt.Files.Single(f=>f.Path==core.Files[0].Path).Sha256=new string('d',64);refuse(()=>InstalledSnapshot.Validate(corrupt,fixedCatalog),"Bloqueia hash diferente de RED4ext fixado.");
        corrupt=Copy(fixedManifest);corrupt.List.Mods.RemoveAll(m=>m.Id==core.Id);corrupt.Files.RemoveAll(f=>f.Owners.Contains(core.Id+"/default"));refuse(()=>InstalledSnapshot.Validate(corrupt,fixedCatalog),"Bloqueia pacote sem dependência transitiva.");
        corrupt=Copy(fixedManifest);corrupt.List.Mods.Single(m=>m.Id==addon.Id).Variants[0].Sources[0]="https://example.com/wrong.zip";refuse(()=>InstalledSnapshot.Validate(corrupt,fixedCatalog),"Bloqueia fonte alterada de componente conhecido.");
        Write(fixedGame,core.Files[0].Path,"different RED4ext");refuse(()=>InstalledSnapshot.Create(Path.Combine(root,"wrong-core.zip"),fixedState,new[]{addon.Id},fixedCatalog,fixedData,true,s=>{}),"Recusa exportar base instalada diferente da versão fixada.");
        Write(fixedGame,independent.Files[0].Path,"pinned independent");
        var reviewed=new List<string>();var steps=new List<int>();
        var continued=InstalledSnapshot.Inspect(fixedState,new[]{addon.Id,independent.Id},fixedCatalog,fixedData,true,true,
            skipProblem:(id,name,error)=>{reviewed.Add(name);return id==core.Id&&error.Contains(core.Files[0].Path);},
            inspectProgress:(done,total,name)=>steps.Add(done));
        check(reviewed.SequenceEqual(new[]{core.Name})&&steps.SequenceEqual(new[]{0,1,2,3}),"Informa o mod com falha e avança a conferência após pular.");
        check(continued.List.Mods.Single().Id==independent.Id&&continued.Files.Single().Owners.Single()==independent.Id+"/default"&&
            continued.Project.Mods.Any(mod=>mod.Id==addon.Id),"Pular a base exclui seus dependentes da transferência e mantém a definição do projeto.");
        refuse(()=>InstalledSnapshot.Inspect(fixedState,new[]{addon.Id,independent.Id},fixedCatalog,fixedData,true,true,skipProblem:(id,name,error)=>false),
            "Recusar a opção de pular mantém a verificação bloqueada.");
        RunFixedRouteTests(root,check,refuse);
        RunDensityTests(root);
        Safe.WriteJson(Path.Combine(root,"resultado-snapshot.json"),new{Passed=true,Checks=checks.Count,Tests=checks,RealGameTouched=false,OriginalArchivesRequired=false,NetworkUsed=false});
        return checks.Count;
    }
    static void RunFixedRouteTests(string root,Action<bool,string> check,Action<Action,string> refuse){
        string sender=Safe.In(root,"fr/s"),receiver=Safe.In(root,"fr/r");
        string senderProfile=Safe.In(root,"fr/sa"),receiverProfile=Safe.In(root,"fr/ra");
        string senderData=Safe.In(root,"fr/sd"),receiverData=Safe.In(root,"fr/rd");
        string original="archive/pc/mod/fixture.archive";
        var routed=Fixed("ArchiveXL",original,"routed archive",new string[0]);
        routed.Files.Add(Fixed(routed.Id,"r6/scripts/Fixture/test.reds","routed script",new string[0]).Files.Single());
        var dependent=Fixed("Audioware","r6/scripts/Fixture/dependent.reds","dependent script",new[]{routed.Id});
        var unrelated=Fixed("Independent","archive/pc/mod/other/fixture.archive","other archive",new string[0]);
        var catalog=new FixedCatalog{GameVersion="2.5",GameFileVersion="2.5.0.0",Packages=new List<FixedMod>{routed,dependent,unrelated}};
        Write(sender,"bin/x64/Cyberpunk2077.exe","fixture");
        Write(receiver,".fixture-only","fixture");
        Write(receiver,"bin/x64/Cyberpunk2077.exe","fixture");
        Directory.CreateDirectory(Safe.In(receiver,"Redirected"));
        string local=Safe.In(senderProfile,"Fixture/Mods");
        Write(senderProfile,"Fixture/Mods/fixture.archive","routed archive");
        Write(sender,"Redirected/test.reds","routed script");
        Write(sender,dependent.Files[0].Path,"dependent script");
        Write(sender,unrelated.Files[0].Path,"other archive");
        var state=new GeneralModState{Destination=sender,List=GeneralModLibrary.Seed(catalog,new ModImportItem[0])};
        var variant=state.List.Mods.Single(mod=>mod.Id==routed.Id).Variants.Single();
        GeneralInstallRoutes.FixtureAppData=senderProfile;
        try{
            variant.InstallRoutes=GeneralInstallRoutes.Parse(original+" = "+local+" | r6/scripts/Fixture = Redirected",sender);
            refuse(()=>GeneralInstallRoutes.FixedPaths(catalog,routed,GeneralInstallRoutes.Parse(original+" = archive/pc/mod/other",sender)),
                "Uma rota do catálogo não pode ocupar destino reservado a outro arquivo.");
            string zip=Safe.In(root,"fr/pack.zip");
            var manifest=InstalledSnapshot.Create(zip,state,state.List.Mods.Select(mod=>mod.Id),catalog,senderData,false,null);
            string external=manifest.Files.Single(file=>GeneralInstallRoutes.External(file.Path)).Path;
            check(manifest.Schema==2&&manifest.Game=="cyberpunk2077"&&manifest.Files.Any(file=>file.Path=="Redirected/test.reds")&&
                !Safe.Json.Serialize(manifest).Contains("fr/sa")&&!File.Exists(Safe.In(sender,original)),
                "Exporta os caminhos mapeados de mod fixado sem expor o perfil do remetente.");
            var corrupt=Copy(manifest);
            corrupt.Files.Single(file=>file.Path==external).Path=external.Replace("fixture.archive","unapproved.archive");
            refuse(()=>InstalledSnapshot.Validate(corrupt,catalog),"Bloqueia destino divergente da rota e receita exatas.");
            corrupt=Copy(manifest);
            corrupt.Files.Single(file=>file.Path==external).Sha256=new string('b',64);
            refuse(()=>InstalledSnapshot.Validate(corrupt,catalog),"Bloqueia conteúdo divergente mesmo com rota exata.");
            string offline=Safe.In(root,"fr/offline.zip");
            InstalledSnapshot.CreateModpack(offline,manifest,sender,catalog,null);
            using(var archive=ZipFile.OpenRead(offline))
                check(archive.GetEntry("Redirected/test.reds")!=null&&archive.GetEntry(external)!=null&&archive.GetEntry("_modloader/project.json")!=null,
                    "O modpack guarda a árvore do jogo, o arquivo externo separado e as rotas no projeto.");
            GeneralInstallRoutes.FixtureAppData=receiverProfile;
            var engine=new InstalledSnapshotInstaller(receiver,receiverData,catalog,true);
            string hash=Safe.Hash(zip);
            string receipt=engine.Install(zip,hash,null,approveUserFolders:folders=>true);
            check(engine.ReadSkippedDestinations(receipt).SequenceEqual(new[]{routed.Id,dependent.Id})&&
                engine.Status(receipt,unrelated.Id+"/default")==GeneralModInstaller.Installed&&
                !File.Exists(Safe.In(receiver,"Redirected/test.reds"))&&!File.Exists(Safe.In(receiver,dependent.Files[0].Path)),
                "O receptor pula o mod fixado inteiro e seu dependente quando a pasta externa não existe.");
            Directory.CreateDirectory(Safe.In(receiverProfile,"Fixture/Mods"));
            receipt=engine.Install(zip,hash,null,approveUserFolders:folders=>folders.Single()==Safe.In(receiverProfile,"Fixture/Mods"));
            check(engine.ReadSkippedDestinations(receipt).Length==0&&engine.Status(receipt,routed.Id+"/default")==GeneralModInstaller.Installed&&
                File.Exists(Safe.In(receiver,"Redirected/test.reds"))&&File.Exists(Safe.In(receiverProfile,"Fixture/Mods/fixture.archive"))&&
                !File.Exists(Safe.In(receiver,original)),
                "Após confirmar o perfil local, instala e confere os destinos do mod fixado no computador receptor.");
            string key=routed.Id+"/default";
            string oldArchive=Safe.In(receiverProfile,"Fixture/Mods/fixture.archive");
            string oldScript=Safe.In(receiver,"Redirected/test.reds");
            string newArchive=Safe.In(receiverProfile,"Fixture/New/fixture.archive");
            string newScript=Safe.In(receiver,"New/test.reds");
            Directory.CreateDirectory(Path.GetDirectoryName(newArchive));
            Directory.CreateDirectory(Path.GetDirectoryName(newScript));
            var next=GeneralInstallRoutes.Parse(original+" = %APPDATA%/Fixture/New | r6/scripts/Fixture = New",receiver);
            using(var locked=new FileStream(oldScript,FileMode.Open,FileAccess.Read,FileShare.Read))
                refuse(()=>engine.Relocate(receipt,key,variant.InstallRoutes,next,null),
                    "A tentativa de mover um arquivo bloqueado desfaz a movimentação anterior.");
            check(File.Exists(oldArchive)&&File.Exists(oldScript)&&!File.Exists(newArchive)&&!File.Exists(newScript)&&
                engine.Status(receipt,key)==GeneralModInstaller.Installed,
                "Falha durante a movimentação preserva os arquivos antigos e o recibo.");
            check(engine.Relocate(receipt,key,variant.InstallRoutes,next,null)&&File.Exists(newArchive)&&File.Exists(newScript)&&
                !File.Exists(oldArchive)&&!File.Exists(oldScript)&&engine.Status(receipt,key)==GeneralModInstaller.Installed&&
                engine.Status(receipt,dependent.Id+"/default")==GeneralModInstaller.Installed&&
                GeneralInstallRoutes.Same(engine.ReadManifest(receipt).Project.Mods.Single(mod=>mod.Id==routed.Id).Variants.Single().InstallRoutes,next),
                "Muda os destinos verificados e atualiza o mesmo recibo e a definição do projeto recebido.");
        }finally{GeneralInstallRoutes.FixtureAppData=null;}
    }

    internal static int RunDensityTests(string parent){
        string root=OnlineTests.NestedRoot(parent,"online-testes-");Directory.CreateDirectory(root);
        var checks=new List<string>();Action<bool,string> check=(ok,label)=>{if(!ok)throw new IOException(label);checks.Add(label);};
        Action<Action,string> refuse=(action,label)=>{try{action();}catch(IOException){checks.Add(label);return;}throw new IOException("Não bloqueou: "+label);};
        var density=Fixed(NightCityDensity.ModId,"r6/tweaks/FixtureDensity/medium.yml","medium",new string[0]);
        density.DensityProfiles=new List<FixedDensityProfile>();
        foreach(string level in new[]{"low","medium","high"}){var profile=Fixed(NightCityDensity.ModId,"r6/tweaks/FixtureDensity/"+level+".yml",level,new string[0]);density.DensityProfiles.Add(new FixedDensityProfile{Id=level,Name=level,Assets=profile.Assets,Files=profile.Files});}
        var densityCatalog=new FixedCatalog{Schema=1,Id="densityfixture",GameVersion="2.5",GameFileVersion="2.5.0.0",Packages=new List<FixedMod>{density},Defaults=new List<FixedDefault>()};
        string densityGame=Path.Combine(root,"n"),densityData=Path.Combine(root,"nd");Directory.CreateDirectory(densityGame);Directory.CreateDirectory(densityData);Write(densityGame,"bin/x64/Cyberpunk2077.exe","fixture only");Write(densityGame,"r6/tweaks/FixtureDensity/high.yml","high");
        var densityState=new GeneralModState{Destination=densityGame,List=GeneralModLibrary.Seed(densityCatalog,new ModImportItem[0])};densityState.ChosenVariants[density.Id]="low";
        var densityManifest=InstalledSnapshot.Create(Path.Combine(root,"density.zip"),densityState,new[]{density.Id},densityCatalog,densityData,false,s=>{});
        check(densityManifest.List.Mods.Single().DefaultVariant=="high"&&densityManifest.List.Mods.Single().Variants.Single().CyberpunkVariant=="high","Exporta densidade NCA realmente instalada apesar da seleção antiga.");
        check(densityManifest.Files.Single().Owners.Single()==density.Id+"/high"&&densityManifest.Files.Single().Path.EndsWith("/high.yml"),"Manifesto liga bytes e proprietário à mesma variante instalada.");
        var legacyDensity=Copy(density);var medium=legacyDensity.DensityProfiles.Single(p=>p.Id=="medium");
        foreach(string layer in new[]{"r6/tweaks/NightCityAlive/passengers/low-to-std.yml","r6/tweaks/NightCityAlive/aigeneralsettings/low-to-std.yml"})medium.Files.Add(Fixed(density.Id,layer,"layer",new string[0]).Files.Single());
        var legacyCatalog=Copy(densityCatalog);legacyCatalog.Packages=new List<FixedMod>{legacyDensity};
        File.Delete(Safe.In(densityGame,"r6/tweaks/FixtureDensity/high.yml"));
        Write(densityGame,"r6/tweaks/FixtureDensity/medium.yml","medium");
        Write(densityGame,NightCityDensity.LegacyPath,"# Perfil local de densidade baixa: preservar os demais encontros.\r\nAIGeneralSettings.ReactionSystem.aggressiveCiviliansLimit: 1\r\n");
        var legacyState=new GeneralModState{Destination=densityGame,List=GeneralModLibrary.Seed(legacyCatalog,new ModImportItem[0])};
        check(GeneralModWindow.DensityCountsAsInstalled(legacyCatalog,legacyState,densityData),"O contador do projeto inclui a Baixa personalizada reconhecida com o preset Baixa.");
        var legacyManifest=InstalledSnapshot.Create(Path.Combine(root,"legacy.zip"),legacyState,new[]{density.Id},legacyCatalog,densityData,false,s=>{},true);
        var legacyRead=InstalledSnapshot.Read(Path.Combine(root,"legacy.zip"),Safe.Hash(Path.Combine(root,"legacy.zip")),legacyCatalog);
        check(legacyRead.List.Mods.Single().DefaultVariant=="legacy-low"&&legacyRead.Project.Mods.Single().DefaultVariant=="legacy-low","Perfil antigo conhecido exporta e valida com a identidade exata, sem converter a densidade.");
        check(legacyManifest.Files.Any(f=>f.Path==NightCityDensity.LegacyPath)&&!legacyManifest.Files.Any(f=>f.Path.EndsWith("low-to-std.yml")),"Snapshot inclui a configuração baixa antiga e omite as camadas removidas.");
        GeneralModLibrary.RestoreKnownMappings(legacyRead.Project,GeneralModLibrary.Seed(legacyCatalog,new ModImportItem[0]));
        check(legacyRead.Project.Mods.Single().Variants.Single(v=>v.Id=="legacy-low").CyberpunkVariant=="legacy-low","Reabrir projeto recebido conserva reconhecimento do perfil antigo.");
        string modpack=Path.Combine(root,"legacy-modpack.zip");InstalledSnapshot.CreateModpack(modpack,legacyManifest,densityGame,legacyCatalog,s=>{});
        using(var archive=ZipFile.OpenRead(modpack))check(archive.GetEntry(NightCityDensity.LegacyPath)!=null&&archive.GetEntry("_modloader/project.json")!=null&&!archive.Entries.Any(e=>e.FullName.StartsWith("files/")),"Gerar modpack usa árvore relativa do jogo e metadados, com o perfil antigo preservado.");
        Write(densityGame,NightCityDensity.LegacyPath,"unknown edit");
        refuse(()=>InstalledSnapshot.Create(Path.Combine(root,"legacy-bad.zip"),legacyState,new[]{density.Id},legacyCatalog,densityData,false,s=>{},true),"Perfil antigo alterado continua bloqueado, sem relaxar hashes.");
        string target=Path.Combine(root,"receive"),receivedData=Path.Combine(root,"received-data");Directory.CreateDirectory(target);Write(target,".fixture-only","test");
        var receiver=new InstalledSnapshotInstaller(target,receivedData,legacyCatalog,true);
        var receipt=receiver.Install(Path.Combine(root,"legacy.zip"),Safe.Hash(Path.Combine(root,"legacy.zip")),s=>{});
        check(NightCityDensity.Read(legacyDensity,target).Id=="legacy-low"&&receiver.Status(receipt,density.Id+"/legacy-low")==GeneralModInstaller.Installed,"Recepção e instalação em fixture preservam a mesma densidade antiga e conferem o recibo.");
        Safe.WriteJson(Path.Combine(root,"resultado-snapshot-density.json"),new{Passed=true,Checks=checks,RealGameTouched=false});return checks.Count;
    }
    static FixedMod Fixed(string id,string path,string content,string[] dependencies){return new FixedMod{Id=id,Name=id,Version="1.0",GameVersion="2.5",Dependencies=dependencies,Assets=new List<FixedAsset>{new FixedAsset{Id="asset",Name=id+".zip",Format="zip",Url="https://github.com/fixture/fixture/releases/download/v1/"+id+".zip",Sha256=new string('a',64),Size=1}},Files=new List<FixedFile>{new FixedFile{Asset="asset",Entry=path,Path=path,Size=Encoding.UTF8.GetByteCount(content),Sha256=InstalledSnapshot.Hash(Encoding.UTF8.GetBytes(content))}}};}
    static T Copy<T>(T value){return Safe.Json.Deserialize<T>(Safe.Json.Serialize(value));}
    static GeneralModState State(string game,string original){var state=new GeneralModState{Destination=game};state.List.Mods.Add(new GeneralModEntry{Id="fixture",Name="Fixture",DefaultVariant="default",Variants=new List<GeneralModVariant>{new GeneralModVariant{Id="default",Name="Principal",Sources=new List<string>{original}}}});return state;}
    static void Write(string root,string path,string content){string target=Safe.In(root,path);Directory.CreateDirectory(Path.GetDirectoryName(target));File.WriteAllText(target,content,new UTF8Encoding(false));}
    static InstalledSnapshotFile Config(string content,string section){byte[] bytes=Encoding.UTF8.GetBytes(content);return new InstalledSnapshotFile{Path="red4ext/plugins/mod_settings/user.ini",Kind="ini-section",Section=section,Content=content,Size=bytes.Length,Sha256=InstalledSnapshot.Hash(bytes)};}
    static void WriteGenericRecord(string game,string data,string key,IEnumerable<string> paths) {
        string fingerprint=InstalledSnapshot.Hash(Encoding.UTF8.GetBytes((Path.GetFullPath(game).TrimEnd('\\')+"\\").ToUpperInvariant())).Substring(0,32);
        string name=InstalledSnapshot.Hash(Encoding.UTF8.GetBytes(key)).Substring(0,32)+".json";
        var record=new GeneralModInstaller.Record{Key=key,Files=paths.Select(path=>new GeneralModInstaller.RecordFile{Path=path,Sha256=Safe.Hash(Safe.In(game,path)),Size=new FileInfo(Safe.In(game,path)).Length,Owned=true}).ToList()};
        Safe.WriteJson(Path.Combine(data,"Instalacoes",fingerprint,name),record);
    }
    static void WriteProtectedReceipt(string game,OnlineReceipt receipt){byte[] bytes=Encoding.UTF8.GetBytes(Safe.Json.Serialize(receipt));Safe.WriteJson(Safe.In(game,".CoopModsOnline/importados/"+receipt.Id+".json"),new ReceiptEnvelope{Protected=Convert.ToBase64String(ProtectedData.Protect(bytes,null,DataProtectionScope.CurrentUser))});}
}
