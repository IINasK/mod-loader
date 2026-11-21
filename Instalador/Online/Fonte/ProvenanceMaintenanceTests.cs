using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

internal static class ProvenanceMaintenanceTests {
    internal static int Run(string parent) {
        string root=Path.Combine(Path.GetFullPath(parent),"provenance-"+Guid.NewGuid().ToString("N").Substring(0,8));
        Directory.CreateDirectory(root);
        var checks=new List<string>();
        Action<bool,string> check=(ok,message)=>{if(!ok)throw new IOException(message);checks.Add(message);};
        string game=Path.Combine(root,"g"),state=Path.Combine(root,"s");
        Directory.CreateDirectory(game);
        string target=Path.Combine(game,"settings.txt");
        File.WriteAllText(target,new string('x',8192));
        string original=Safe.Hash(target);
        string package=Zip(root,"a.zip","settings.txt","new content");
        var installer=new GeneralModInstaller(game,state);
        installer.Install(package,"one",null,d=>true);
        string receipts=Directory.GetDirectories(state).Single();
        string backups=Path.Combine(receipts,"backups"),backup=Path.Combine(backups,original+".bin");
        check(File.Exists(backup),"Replacement retains a verified rollback backup.");
        string unknown=Path.Combine(backups,"personal.bin");File.WriteAllText(unknown,"personal");
        installer.Install(package,"two",null,d=>true);
        installer.Remove("one");
        check(File.Exists(backup)&&File.ReadAllText(target)=="new content","A second active receipt keeps the shared backup reference.");
        installer.Remove("two");
        check(Safe.Hash(target)==original&&!File.Exists(backup),"Successful final restoration releases its 8192-byte redundant backup.");
        check(File.ReadAllText(unknown)=="personal","Cleanup never sweeps unknown backups.");

        installer.Install(package,"modified",null,d=>true);
        File.WriteAllText(target,"manual changes");
        installer.Remove("modified");
        check(File.Exists(backup)&&File.ReadAllText(target)=="manual changes","Modified target and its only original backup are preserved.");
        string journal=Path.Combine(receipts,"pending-install.journal");
        File.WriteAllText(journal,"pending recovery");
        installer.ReleaseUnreferencedBackups(new[]{original});
        check(File.Exists(backup),"Any pending recovery prevents backup reclamation.");
        File.Delete(journal);
        File.WriteAllText(Path.Combine(receipts,"unreadable.json"),"invalid receipt");
        installer.ReleaseUnreferencedBackups(new[]{original});
        check(File.Exists(backup),"Unreadable receipt preserves potential backup dependencies.");
        File.Delete(Path.Combine(receipts,"unreadable.json"));
        File.WriteAllText(backup,"changed backup");
        installer.ReleaseUnreferencedBackups(new[]{original});
        check(File.ReadAllText(backup)=="changed backup","Divergent backup content is never deleted by its filename.");

        string plugin="plugins/example/module.dll",folder=Path.Combine(game,"plugins","example");
        Directory.CreateDirectory(folder);File.WriteAllText(Safe.In(game,plugin),"module");
        var anchors=new List<ModFileAnchor>{new ModFileAnchor{Owner="example",Path=plugin,Hash=Safe.Hash(Safe.In(game,plugin))}};
        string[] local={"settings.json","runtime.log","local.cache","slot.sav","custom.dll"};
        foreach(string name in local)File.WriteAllText(Path.Combine(folder,name),"local user data");
        var found=InstalledFileOwnership.FindAssociated(game,anchors,InstalledFileOwnership.GenericScope);
        check(found.Count==4&&!found.ContainsKey("plugins/example/custom.dll"),"Generic games associate data candidates but never infer code ownership.");
        check(GeneratedFileProvenance.Category("plugins/example/settings.json")=="configuration"&&
            GeneratedFileProvenance.Category("plugins/example/runtime.log")=="log"&&
            GeneratedFileProvenance.Category("plugins/example/local.cache")=="cache"&&
            GeneratedFileProvenance.Category("plugins/example/slot.sav")=="save"&&
            GeneratedFileProvenance.Category("plugins/example/state.sqlite3")=="data",
            "Configuration, log, cache, save and database hints are independent of the game.");
        check(InstalledFileOwnership.PrivateState("plugins/example/slot.sav")&&
            InstalledFileOwnership.PrivateState("plugins/example/saves/slot.dat")&&
            InstalledFileOwnership.PrivateState("plugins/example/savegames/slot.bin")&&
            !InstalledFileOwnership.PrivateState("plugins/example/runtime.log"),
            "Generic save files and save folders are excluded from extra-file cleanup.");
        var inferred=new P2PSyncDifference{Path="plugins/example/settings.json",Mod="Example",LocalInModFolder=true};
        check(inferred.ProvenOwner==null&&!inferred.RecordedFile&&inferred.VerifiedZip==null&&
            GeneratedFileProvenance.Describe(inferred).Contains(GeneralStyle.T("autoria não confirmada","creator not confirmed")),
            "Contextual classification does not create installation proof or deletion ownership.");
        anchors.Add(new ModFileAnchor{Owner="other",Path="plugins/example/custom.dll",Hash=Safe.Hash(Path.Combine(folder,"custom.dll"))});
        check(InstalledFileOwnership.FindAssociated(game,anchors,InstalledFileOwnership.GenericScope).Count==0,
            "Shared folder ambiguity prevents automatic association.");
        anchors.RemoveAt(1);
        File.WriteAllText(Safe.In(game,plugin),"manual upgraded module");
        check(InstalledFileOwnership.FindAssociated(game,anchors,InstalledFileOwnership.GenericScope).Count==0,
            "Replacing the anchor invalidates inferred association.");
        check(!InstalledFileOwnership.Walk(game,new[]{"plugins/removed"}).Any(),
            "A manually removed mod folder does not abort inventory of other mods.");
        check(Directory.GetFiles(game,"*",SearchOption.AllDirectories).Length==7&&
            File.ReadAllText(Path.Combine(folder,"slot.sav"))=="local user data",
            "Provenance inspection writes no tags into payloads and preserves local saves.");
        CheckGeneratedOutputs(OnlineTests.NestedRoot(parent,"output-"),check);
        Safe.WriteJson(Path.Combine(root,"result.json"),new{Passed=true,Checks=checks,RealGameTouched=false,
            NetworkUsed=false,AutomaticGeneratedFileDeletion=false,ExtraPayloadCopies=0,RestoredBackupBytesReleased=8192});
        return checks.Count;
    }

    static void CheckGeneratedOutputs(string root,Action<bool,string> check){
        string host=Path.Combine(root,"h"),receiver=Path.Combine(root,"r"),metadata=Path.Combine(root,"m");
        Directory.CreateDirectory(host);Directory.CreateDirectory(receiver);
        File.WriteAllText(Path.Combine(receiver,".fixture-only"),"fixture");
        const string code="mods/clothes/main.bin",preexisting="mods/clothes/older.json",
            generated="mods/clothes/fresh.json",runLog="mods/clothes/runtime.log";
        Directory.CreateDirectory(Path.GetDirectoryName(Safe.In(host,code)));
        File.WriteAllText(Safe.In(host,code),"installed");
        File.WriteAllText(Safe.In(host,preexisting),"{\"quality\":1}");
        var mod=new GeneralModEntry{Id="clothes",Name="Clothes",DefaultVariant="default",
            Variants=new List<GeneralModVariant>{new GeneralModVariant{Id="default",Name="Main",Sources=new List<string>{"installed://clothes/default"}}}};
        Func<string,P2PSyncDifference> observed=path=>new P2PSyncDifference{Path=path,ModId=mod.Id,Kind="associated",LocalInModFolder=true};
        var baseline=observed(preexisting);
        new ModOutputIndex(metadata,host).Reconcile(new[]{baseline},new ModOutputInstallation{Entry=mod,Variant="default"});
        File.WriteAllText(Safe.In(host,generated),"{\"quality\":2,\"enabled\":true}");
        File.WriteAllText(Safe.In(host,runLog),"local only");
        var fresh=observed(generated);
        var reopened=new ModOutputIndex(metadata,host);
        reopened.Reconcile(new[]{baseline,fresh,observed(runLog)});
        check(baseline.PreexistingOutput&&!baseline.GeneratedOutput&&fresh.GeneratedOutput&&reopened.Load().Files.Count==3,
            "A new app instance remembers the installation baseline and identifies a later output.");
        check(!ModOutputIndex.CanSend(runLog)&&!ModOutputIndex.CanSend("mods/clothes/saves/slot.json"),
            "Generated logs and private state are never eligible for transfer.");
        var manifest=new InstalledSnapshotManifest{Schema=2,Name="Clothes",Game="generic",Project=new GeneralModList()};
        manifest.List.Mods.Add(mod);
        manifest.Project.Mods.Add(mod);
        manifest.Files.Add(new InstalledSnapshotFile{Path=code,Kind="file",Sha256=Safe.Hash(Safe.In(host,code)),Size=9,
            Owners=new List<string>{"clothes/default"}});
        string json=InstalledSnapshot.ReadGeneratedConfig(host,generated);
        var selected=new P2POutputSelection{Owner=mod.Id,Path=generated,
            Hash=InstalledSnapshot.Hash(System.Text.Encoding.UTF8.GetBytes(json))};
        mod.Variants[0].OutputFolders.Add("output/clothes");
        string declared=Safe.In(host,"output/clothes/settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(declared));
        File.WriteAllText(declared,"{\"quality\":1}");
        var anchors=new[]{new ModFileAnchor{Path=code,Owner=mod.Id,Hash=Safe.Hash(Safe.In(host,code))}};
        var claim=new KeyValuePair<string,string>("output/clothes",mod.Id);
        check(InstalledFileOwnership.FindDeclared(host,new[]{claim},anchors,Safe.Hash).Single().Key=="output/clothes/settings.json"&&
            !InstalledFileOwnership.FindDeclared(host,new[]{claim,new KeyValuePair<string,string>("output/clothes","other")},anchors,Safe.Hash).Any(),
            "A declared destination outside the package folder works when its installed owner is unique.");
        var session=new P2PSession{GeneratedConfigSelection=new[]{selected},
            Recipients=new[]{new P2PRecipient{SupportsGeneratedConfigs=false}}};
        var catalog=new FixedCatalog{Packages=new List<FixedMod>()};
        P2PService.IncludePortableOutputs(session,host,manifest,null);
        check(manifest.Schema==2&&manifest.OutputScopes.Count==2&&manifest.Files.Count==1,
            "Older receivers get portable scope metadata and the original payload format.");
        string standard=Path.Combine(root,"standard.zip");
        InstalledSnapshot.Write(standard,host,manifest,null,null,null);
        session.Recipients[0].SupportsGeneratedConfigs=true;
        P2PService.IncludePortableOutputs(session,host,manifest,null);
        check(manifest.Schema==3&&manifest.Files.Single(file=>file.Generated).Path==generated,
            "Compatible receivers get only the output explicitly selected by the host.");
        string zip=Path.Combine(root,"o.zip");
        InstalledSnapshot.Write(zip,host,manifest,null,null,null);
        Directory.CreateDirectory(Path.GetDirectoryName(Safe.In(receiver,generated)));
        File.WriteAllText(Safe.In(receiver,generated),"{\"quality\":9}");
        var engine=new InstalledSnapshotInstaller(receiver,metadata,catalog,true);
        var id=engine.Install(zip,Safe.Hash(zip),null);
        check(File.ReadAllText(Safe.In(receiver,generated))=="{\"quality\":9}"&&
            engine.Status(id,"clothes/default")==GeneralModInstaller.Installed&&engine.ReadOutputScopes().Count()==2&&
            engine.ReadInstalledFiles(id,"clothes/default").Count==1,
            "The receiver preserves existing local settings and does not treat generated outputs as installed files.");
        string clean=Path.Combine(root,"clean");
        Directory.CreateDirectory(clean);
        File.WriteAllText(Safe.In(clean,".fixture-only"),"fixture");
        var cleanEngine=new InstalledSnapshotInstaller(clean,metadata,catalog,true);
        string cleanId=cleanEngine.Install(zip,Safe.Hash(zip),null);
        check(File.ReadAllText(Safe.In(clean,generated))==json,"An approved generated config is installed only when missing.");
        cleanEngine.Remove(cleanId,new[]{"clothes/default"},null);
        check(File.Exists(Safe.In(clean,generated)),"A generated config remains local after uninstall.");
        const string lua="mods/clothes/camera_settings.lua";
        var encoding=new System.Text.UnicodeEncoding(false,true,true);
        byte[] originalLua=encoding.GetPreamble().Concat(encoding.GetBytes("-- Camera settings\r\nreturn { zoom = 2, levels = { 1, 2 }, mode = 'auto' }\r\n")).ToArray();
        File.WriteAllBytes(Safe.In(host,lua),originalLua);
        string encoded=InstalledSnapshot.ReadGeneratedConfig(host,lua);
        var luaChoice=new P2POutputSelection{Owner=mod.Id,Path=lua,Hash=InstalledSnapshot.GeneratedConfigHash(lua,encoded)};
        InstalledSnapshot.AddGeneratedConfigs(manifest,host,new[]{luaChoice},null);
        var luaFile=manifest.Files.Single(f=>f.Path==lua);
        check(luaFile.Kind==PortableLuaSettings.Kind&&luaFile.Sha256==Safe.Hash(Safe.In(host,lua))&&luaFile.Size==originalLua.Length,
            "A reviewed Lua table carries the original byte hash, including encoding and line endings.");
        string luaZip=Path.Combine(root,"settings.zip");
        InstalledSnapshot.Write(luaZip,host,manifest,null,null,null);
        string luaPath=Safe.In(receiver,lua);
        bool blockedIncoming=false;
        try{engine.Install(luaZip,Safe.Hash(luaZip),null,receivingP2P:true);}catch(IOException){blockedIncoming=true;}
        check(blockedIncoming&&!File.Exists(luaPath),"P2P Lua needs an approved preview even on a clean destination.");
        blockedIncoming=false;
        try{engine.Install(zip,Safe.Hash(zip),null,receivingP2P:true,approvedGeneratedConfigs:manifest.Files.Where(f=>f.Generated));}
        catch(IOException){blockedIncoming=true;}
        check(blockedIncoming&&!File.Exists(luaPath),"Generic projects reject a package that omits an approved settings file.");
        File.WriteAllText(luaPath,"return { zoom=1 }");
        string receiverOnly="mods/clothes/offline.json";
        File.WriteAllText(Safe.In(receiver,receiverOnly),"{\"level\":3}");
        string oldId=engine.Install(luaZip,Safe.Hash(luaZip),null);
        check(File.ReadAllText(luaPath)=="return { zoom=1 }","A settings package without consent keeps previous values.");
        HashSet<string> applied;
        var outputIndex=new ModOutputIndex(metadata,receiver);
        outputIndex.CaptureReceivedBaseline(receiver,engine.ReadReceivedManifest(oldId,out applied),applied);
        var state=new GeneralModState{List=manifest.List};
        check(!GeneralP2PDialog.HostConfigCandidates(metadata,receiver,state,new[]{mod.Id}).Any(),
            "Preserved settings and unknown settings already on the receiver stay outside its next outgoing package.");
        string acceptedId=engine.Install(luaZip,Safe.Hash(luaZip),null,approvedGeneratedConfigs:manifest.Files.Where(f=>f.Generated));
        outputIndex.CaptureReceivedBaseline(receiver,engine.ReadReceivedManifest(acceptedId,out applied),applied);
        var onward=GeneralP2PDialog.HostConfigCandidates(metadata,receiver,state,new[]{mod.Id});
        check(onward.Length==2&&onward.Any(f=>f.Path==lua&&f.Hash==luaFile.Sha256)&&onward.All(f=>f.Path!=receiverOnly),
            "The receiver can send on the two received settings immediately without a scan or sharing other local settings.");
        check(File.ReadAllBytes(luaPath).SequenceEqual(originalLua),"The approved transaction replaces different settings byte for byte.");
        var wrongApproval=new InstalledSnapshotFile{Generated=true,Path=lua,Kind=luaFile.Kind,Owners=luaFile.Owners,Size=luaFile.Size,Sha256=new string('0',64)};
        bool invalid=false;
        try{engine.Install(luaZip,Safe.Hash(luaZip),null,approvedGeneratedConfigs:new[]{manifest.Files.Single(f=>f.Path==generated),wrongApproval});}
        catch(IOException){invalid=true;}
        check(invalid&&File.ReadAllBytes(luaPath).SequenceEqual(originalLua),"A changed settings preview cannot authorize a replacement.");
        string receiverScript="return getActiveProfile()";
        File.WriteAllText(luaPath,receiverScript);
        invalid=false;
        try{engine.Install(luaZip,Safe.Hash(luaZip),null,approvedGeneratedConfigs:manifest.Files.Where(f=>f.Generated));}
        catch(IOException){invalid=true;}
        check(invalid&&File.ReadAllText(luaPath)==receiverScript,"Existing executable Lua remains intact when it is not a settings table.");
        foreach(string rejected in new[]{"return { zoom = os.execute('command') }","return {}; print('extra')",
            "return { apiKey = 'private' }","-- C:/Users/personal\nreturn {}","return { value = 'C:/Users/personal' }",
            "--[=[ code ]=]\nreturn { foo=true }","return { a=true,a=false }","return { a=true } " + (char)0}){
            var unsafeBytes=System.Text.Encoding.UTF8.GetBytes(rejected);
            try{PortableLuaSettings.Encode(unsafeBytes);throw new InvalidOperationException("Unsafe outgoing Lua accepted.");}
            catch(IOException){}
            var incoming=new InstalledSnapshotFile{Generated=true,Path=lua,Kind=PortableLuaSettings.Kind,Content=Convert.ToBase64String(unsafeBytes),
                Sha256=InstalledSnapshot.Hash(unsafeBytes),Size=unsafeBytes.Length,Owners=new List<string>{"clothes/default"}};
            invalid=false;
            try{InstalledSnapshotConfig.Apply(incoming,null);}catch(IOException){invalid=true;}
            check(invalid,"Nonportable or executable data is rejected on send and receive.");
        }
        check(!ModOutputIndex.CanSend("mods/clothes/init.lua")&&!ModOutputIndex.CanSend("mods/clothes/logs/settings.lua"),
            "Regular scripts and log folders are never offered as generated settings.");
        File.WriteAllText(Safe.In(host,generated),"{\"name\":\"local-machine\"}");
        bool blocked=false;
        try{InstalledSnapshot.ReadGeneratedConfig(host,generated);}catch(IOException){blocked=true;}
        check(blocked,"Strings in generated settings stay local.");
    }

    static string Zip(string root,string name,string entry,string content){
        string path=Path.Combine(root,name);
        using(var archive=ZipFile.Open(path,ZipArchiveMode.Create))
        using(var writer=new StreamWriter(archive.CreateEntry(entry).Open()))writer.Write(content);
        return path;
    }
}
