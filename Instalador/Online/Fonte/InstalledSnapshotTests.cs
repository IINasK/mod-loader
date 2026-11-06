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
        string secretGame=Path.Combine(root,"s"),secretData=Path.Combine(root,"sd");Directory.CreateDirectory(secretGame);Directory.CreateDirectory(secretData);
        Write(secretGame,"mods/config.json","{\"access_token\":\"private\"}");WriteGenericRecord(secretGame,secretData,work+"/0/"+original,new[]{"mods/config.json"});
        refuse(()=>InstalledSnapshot.Create(Path.Combine(root,"secret.zip"),State(secretGame,original),new[]{"fixture"},catalog,secretData,true,s=>{}),"JSON personalizado não vaza credenciais apesar de recibo válido.");
        check(!File.Exists(Path.Combine(root,"secret.zip")),"Remove ZIP falhado durante inspeção do conteúdo privado.");
        var core=Fixed("NightCoop","red4ext/RED4ext.dll","pinned core",new string[0]);core.Immutable=true;
        var addon=Fixed("Audioware","red4ext/plugins/Audioware/plugin.dll","pinned addon",new[]{"NightCoop"});
        string userPath="bin/x64/plugins/cyber_engine_tweaks/mods/Fixture/settings.json";
        addon.Files.Add(new FixedFile{Asset="asset",Entry="settings.json",Path=userPath,UserData=true,Size=2,Sha256=InstalledSnapshot.Hash(Encoding.UTF8.GetBytes("{}"))});
        var fixedCatalog=new FixedCatalog{Schema=1,Id="fixedfixture",GameVersion="2.5",GameFileVersion="2.5.0.0",Packages=new List<FixedMod>{core,addon},Defaults=new List<FixedDefault>{new FixedDefault{Id="default",ModId=addon.Id,Version=addon.Version,Path="red4ext/plugins/mod_settings/user.ini",Format="ini-section",Section="Audioware.AudiowareConfig",Content="{\"enabled\":\"true\"}",Sha256=InstalledSnapshot.Hash(Encoding.UTF8.GetBytes("{\"enabled\":\"true\"}"))}}};
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
        var density=Fixed(NightCityDensity.ModId,"r6/tweaks/FixtureDensity/medium.yml","medium",new string[0]);
        density.DensityProfiles=new List<FixedDensityProfile>();
        foreach(string level in new[]{"low","medium","high"}){var profile=Fixed(NightCityDensity.ModId,"r6/tweaks/FixtureDensity/"+level+".yml",level,new string[0]);density.DensityProfiles.Add(new FixedDensityProfile{Id=level,Name=level,Assets=profile.Assets,Files=profile.Files});}
        var densityCatalog=new FixedCatalog{Schema=1,Id="densityfixture",GameVersion="2.5",GameFileVersion="2.5.0.0",Packages=new List<FixedMod>{density},Defaults=new List<FixedDefault>()};
        string densityGame=Path.Combine(root,"n"),densityData=Path.Combine(root,"nd");Directory.CreateDirectory(densityGame);Directory.CreateDirectory(densityData);Write(densityGame,"bin/x64/Cyberpunk2077.exe","fixture only");Write(densityGame,"r6/tweaks/FixtureDensity/high.yml","high");
        var densityState=new GeneralModState{Destination=densityGame,List=GeneralModLibrary.Seed(densityCatalog,new ModImportItem[0])};densityState.ChosenVariants[density.Id]="low";
        var densityManifest=InstalledSnapshot.Create(Path.Combine(root,"density.zip"),densityState,new[]{density.Id},densityCatalog,densityData,false,s=>{});
        check(densityManifest.List.Mods.Single().DefaultVariant=="high"&&densityManifest.List.Mods.Single().Variants.Single().CyberpunkVariant=="high","Exporta densidade NCA realmente instalada apesar da seleção antiga.");
        check(densityManifest.Files.Single().Owners.Single()==density.Id+"/high"&&densityManifest.Files.Single().Path.EndsWith("/high.yml"),"Manifesto liga bytes e proprietário à mesma variante instalada.");
        Safe.WriteJson(Path.Combine(root,"resultado-snapshot.json"),new{Passed=true,Checks=checks.Count,Tests=checks,RealGameTouched=false,OriginalArchivesRequired=false,NetworkUsed=false});
        return checks.Count;
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
