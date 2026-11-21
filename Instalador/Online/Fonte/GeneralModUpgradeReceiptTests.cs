using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;

internal static class GeneralModUpgradeReceiptTests {
    internal static int Run(string parent) {
        string root=Path.Combine(Path.GetFullPath(parent),"online-testes-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);Safe.NoLinks(root);
        var checks=new List<string>();Action<bool,string> check=(ok,message)=>{if(!ok)throw new IOException(message);checks.Add(message);};
        var ordered=Safe.Json.DeserializeObject("{\"Files\":[{\"Path\":\"mods/a\",\"Size\":3}],\"WriteUtc\":639307068033304389}");
        var reordered=Safe.Json.DeserializeObject("{\"WriteUtc\":639307068033304389,\"Files\":[{\"Size\":3,\"Path\":\"mods/a\"}]}");
        check(GeneralUpdateOwnership.ReceiptFingerprint(ordered)==GeneralUpdateOwnership.ReceiptFingerprint(reordered)&&
            GeneralUpdateOwnership.ReceiptFingerprint(ordered,false)!=GeneralUpdateOwnership.ReceiptFingerprint(reordered,false),
            "Canonical receipt identity is independent of object-property order, including nested file metadata.");
        check(GeneralUpdateOwnership.ReceiptFingerprint(ordered)!=GeneralUpdateOwnership.ReceiptFingerprint(Safe.Json.DeserializeObject(
            "{\"Files\":[{\"Path\":\"mods/a\",\"Size\":3}],\"WriteUtc\":639307068033304390}")),
            "Canonical proofs preserve full 64-bit write stamps instead of rounding them through floating point.");
        check(GeneralUpdateOwnership.ReceiptFingerprint(new[]{"a","b"})!=GeneralUpdateOwnership.ReceiptFingerprint(new[]{"b","a"})&&
            GeneralUpdateOwnership.ReceiptFingerprint(new{Path="a",Owned=true})!=GeneralUpdateOwnership.ReceiptFingerprint(new{Path="a",Owned=false}),
            "Canonical ordering never hides array order or changes in receipt ownership.");
        string game=Directory.CreateDirectory(Safe.In(root,"game")).FullName;
        File.WriteAllText(Safe.In(game,".fixture-only"),"isolated fixture");
        string data=Safe.In(root,"data");var generic=new GeneralModInstaller(game,Safe.In(data,"Instalacoes"));
        string package=Safe.In(root,"original.zip");
        using(var zip=ZipFile.Open(package,ZipArchiveMode.Create))
            using(var writer=new StreamWriter(zip.CreateEntry("mods/first.txt").Open()))writer.Write("original");
        string key="first/default/0/https://example.org/original.zip";
        generic.Install(package,key,null);
        string firstProof=generic.UpgradeReceiptFingerprint(key);
        check(FixedData.HashValid(firstProof)&&firstProof==generic.UpgradeReceiptFingerprint(key),
            "A stable generic receipt yields a repeatable full-state retirement proof.");
        var first=generic.ReadPackageRecord(key);
        check(first.TransactionId!=null,"New generic installations carry an installation identity.");
        generic.Install(package,key,null);
        string secondProof=generic.UpgradeReceiptFingerprint(key);
        check(first.TransactionId!=generic.ReadPackageRecord(key).TransactionId&&firstProof!=secondProof,
            "Reinstalling identical bytes under the same key produces a different transaction proof.");
        Throws(()=>generic.ForgetUpgradeReceipt(key,firstProof));
        check(generic.ReadPackageRecord(key)!=null&&File.ReadAllText(Safe.In(game,"mods/first.txt"))=="original",
            "An obsolete proof cannot retire a newly installed receipt with the same source key.");
        string pendingPath=(string)typeof(GeneralModInstaller).GetProperty("PendingPath",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(generic,null);
        Safe.WriteJson(pendingPath,new GeneralModInstaller.PendingInstall{Stage=".mod-loader-"+Guid.NewGuid().ToString("N"),StageFiles=0});
        string pendingHash=Safe.Hash(pendingPath);
        Throws(()=>generic.ForgetUpgradeReceipt(key,secondProof));
        check(File.Exists(pendingPath)&&Safe.Hash(pendingPath)==pendingHash&&generic.ReadPackageRecord(key)!=null,
            "Metadata retirement never recovers or consumes a foreign project's interrupted-install journal.");
        File.Delete(pendingPath);
        File.WriteAllText(Safe.In(game,"mods/first.txt"),"new committed payload");
        generic.ForgetUpgradeReceipt(key,secondProof);generic.ForgetUpgradeReceipt(key,secondProof);
        check(generic.ReadPackageRecord(key)==null&&File.ReadAllText(Safe.In(game,"mods/first.txt"))=="new committed payload",
            "Generic predecessor retirement is idempotent and never modifies the installed payload.");
        string legacyGame=Directory.CreateDirectory(Safe.In(root,"legacy-game")).FullName;
        var legacyEngine=new GeneralModInstaller(legacyGame,Safe.In(root,"legacy-data"));legacyEngine.Install(package,key,null);
        string legacyProof=legacyEngine.UpgradeReceiptFingerprint(key,false);
        legacyEngine.ForgetUpgradeReceipt(key,legacyProof,false);
        check(legacyEngine.ReadPackageRecord(key)==null&&File.ReadAllText(Safe.In(legacyGame,"mods/first.txt"))=="original"&&
            !Safe.Json.Deserialize<GeneralModInstaller.RelatedReceipt>("{\"Fingerprint\":\"legacy\"}").CanonicalFingerprint,
            "Existing unmarked receipt proofs keep the legacy verifier; canonicalization does not silently reinterpret them.");

        var catalog=new FixedCatalog{Packages=new List<FixedMod>()};
        string snapshotData=Safe.In(root,"snapshot-data");
        var snapshot=new InstalledSnapshotInstaller(game,snapshotData,catalog,true);
        string snapshotZip=MakeSnapshot(root,catalog);
        string id=snapshot.Install(snapshotZip,Safe.Hash(snapshotZip),null);
        string proof=snapshot.UpgradeReceiptFingerprint(id);
        var receipt=Read(snapshot,id);Write(snapshot,receipt);
        check(proof==snapshot.UpgradeReceiptFingerprint(id),
            "Re-encrypting an unchanged snapshot envelope leaves its logical retirement proof unchanged.");
        string cloneData=Safe.In(root,"clone-data");snapshot.CopyReceivedRecognition(id,cloneData);
        var clone=new InstalledSnapshotInstaller(game,cloneData,catalog,true);
        string cloneProof=clone.UpgradeReceiptFingerprint(id);
        check(cloneProof!=proof&&clone.ProjectDeletionFiles(true).All(file=>!file.Owned),
            "A copied recognition receipt with the same snapshot ID remains unowned and has its own logical proof.");
        Throws(()=>clone.ForgetUpgradeReceipt(id,new[]{"alpha/default","beta/default"},proof));
        check(clone.Status(id,"alpha/default")==GeneralModInstaller.Installed&&snapshot.Status(id,"alpha/default")==GeneralModInstaller.Installed,
            "The original snapshot's proof cannot retire a clone's distinct recognition receipt.");

        receipt=Read(snapshot,id);receipt.ActiveOwners.Remove("alpha/default");Write(snapshot,receipt);
        Throws(()=>snapshot.ForgetUpgradeReceipt(id,"beta/default",proof));
        check(snapshot.Status(id,"beta/default")==GeneralModInstaller.Installed,
            "A snapshot changed after authorization cannot retire another still-active owner using the stale proof.");
        // An already completed owner retirement may be replayed after a close
        // before the durable 'retired' flag was saved in the new local receipt.
        snapshot.ForgetUpgradeReceipt(id,"alpha/default",proof);
        string currentProof=snapshot.UpgradeReceiptFingerprint(id);
        snapshot.ForgetUpgradeReceipt(id,new[]{"beta/default","gamma/default"},currentProof);
        snapshot.ForgetUpgradeReceipt(id,new[]{"beta/default","gamma/default"},currentProof);
        check(snapshot.Status(id,"beta/default")==GeneralModInstaller.Missing&&snapshot.Status(id,"gamma/default")==GeneralModInstaller.Missing&&
            clone.Status(id,"beta/default")==GeneralModInstaller.Installed,
            "Batch retirement checks one original fingerprint, is idempotent and stays within the selected data store.");
        check(File.ReadAllText(Safe.In(game,"mods/alpha.txt"))=="alpha"&&File.ReadAllText(Safe.In(game,"mods/beta.txt"))=="beta"&&
            File.ReadAllText(Safe.In(game,"mods/gamma.txt"))=="gamma"&&File.ReadAllText(Safe.In(game,"mods/shared.txt"))=="shared",
            "Snapshot metadata retirement preserves all installed files, including files shared by retired owners.");

        // A fresh, complete snapshot has a different ID even when it installs
        // exactly the same source payload.
        string freshData=Safe.In(root,"fresh-data");
        var fresh=new InstalledSnapshotInstaller(game,freshData,catalog,true);
        string freshId=fresh.Install(snapshotZip,Safe.Hash(snapshotZip),null);
        check(freshId!=id,"Fresh snapshot installations receive a distinct transaction identity.");
        string freshProof=fresh.UpgradeReceiptFingerprint(freshId);
        fresh.ForgetUpgradeReceipt(freshId,new[]{"alpha/default","beta/default"},freshProof);
        check(fresh.Status(freshId,"gamma/default")==GeneralModInstaller.Installed&&fresh.Status(freshId,"alpha/default")==GeneralModInstaller.Missing,
            "Retiring selected owners preserves the unrelated active owners of the same snapshot.");

        var reference=new GeneralModInstaller.RelatedReceipt{Data=Path.GetFullPath(data),Key=key,Owner="first/default",Fingerprint=secondProof,CanonicalFingerprint=true};
        GeneralModInstaller.ValidateRelatedReceipt(reference);
        check(true,"A related receipt stores a canonical private data path, exact owner, key and fingerprint.");
        foreach(string path in new[]{"relative/data",@"C:\",@"\\server\share\data",data+@"\..\elsewhere"}) {
            var bad=Copy(reference);bad.Data=path;Throws(()=>GeneralModInstaller.ValidateRelatedReceipt(bad));
        }
        check(true,"Relative, root, network and noncanonical paths cannot be stored as related receipt targets.");
        var wrongOwner=Copy(reference);wrongOwner.Owner="unrelated/default";Throws(()=>GeneralModInstaller.ValidateRelatedReceipt(wrongOwner));
        var twoKinds=Copy(reference);twoKinds.SnapshotId=id;Throws(()=>GeneralModInstaller.ValidateRelatedReceipt(twoKinds));
        var wrongHash=Copy(reference);wrongHash.Fingerprint="not-a-fingerprint";Throws(()=>GeneralModInstaller.ValidateRelatedReceipt(wrongHash));
        check(true,"A related reference cannot target another owner's key, combine two receipt kinds or omit the proof.");
        var snapshotReference=new GeneralModInstaller.RelatedReceipt{Data=Path.GetFullPath(snapshotData),SnapshotId=id,Owner="alpha/default",Fingerprint=proof,CanonicalFingerprint=true};
        GeneralModInstaller.ValidateRelatedReceipt(snapshotReference);
        check(true,"Snapshot references keep their exact data store and owner separate from a reused snapshot ID.");
        foreach(string mode in new[]{"generic","snapshot","owned","stale","payload","source","route","archive","mixed","before","rollback"})AliasUpdate(root,mode,check);
        Safe.WriteJson(Safe.In(root,"resultado-upgrade-receipts.json"),new{Passed=true,Count=checks.Count,Checks=checks,RealGameTouched=false});
        return checks.Count;
    }

    static GeneralModInstaller.RelatedReceipt Copy(GeneralModInstaller.RelatedReceipt value) {
        return Safe.Json.Deserialize<GeneralModInstaller.RelatedReceipt>(Safe.Json.Serialize(value));
    }
    static void AliasUpdate(string root,string mode,Action<bool,string> check) {
        string folder=Directory.CreateDirectory(Safe.In(root,"alias-"+mode)).FullName;
        string game=Directory.CreateDirectory(Safe.In(folder,"game")).FullName;
        string originalData=Safe.In(folder,"owner"),aliasData=Safe.In(folder,"current");
        var original=new GeneralModInstaller(game,Safe.In(originalData,"Instalacoes"));
        var current=new GeneralModInstaller(game,Safe.In(aliasData,"Instalacoes"));
        const string source="https://www.nexusmods.com/fixturegame/mods/42?tab=files&file_id=10";
        const string target="https://www.nexusmods.com/fixturegame/mods/42?tab=files&file_id=11";
        string oldKey="original-owner/default/0/"+source,newKey="current-owner/default/0/"+target;
        string oldZip=SimpleZip(folder,"old.zip",new Dictionary<string,string>{{"mods/main.txt","old"},{"mods/stable.txt","stable"}});
        string newZip=SimpleZip(folder,"new.zip",new Dictionary<string,string>{{"mods/main.txt","new"},{"mods/stable.txt","stable"},{"mods/added.txt","added"}});
        original.Install(oldZip,oldKey,null);original.DuplicateRecognition(Safe.In(aliasData,"Instalacoes"));
        var baseline=original.ReadPackageRecord(oldKey);if(mode=="snapshot")baseline.SourceSha256=null;
        var alias=current.ReadPackageRecord(oldKey);
        if(mode=="payload")alias.Files[0].Sha256=InstalledSnapshot.Hash(Encoding.UTF8.GetBytes("unrelated"));
        if(mode=="source")alias.Key="original-owner/default/0/https://www.nexusmods.com/fixturegame/mods/99?tab=files&file_id=10";
        if(mode=="route")alias.InstallRoutes=GeneralInstallRoutes.Parse("mods = %GAME%/elsewhere",game);
        if(mode=="archive")alias.SourceSha256=new string('a',64);
        if(mode=="owned")foreach(var file in alias.Files)file.Owned=true;
        if(mode=="mixed")alias.Files[0].Owned=true;
        if(mode=="before")alias.Files[0].Before=new string('a',64);
        if(mode=="payload"||mode=="source"||mode=="route"||mode=="archive"||mode=="owned"||mode=="mixed"||mode=="before") {
            string before=RecordPath(current,oldKey);File.Delete(before);Safe.WriteJson(RecordPath(current,alias.Key),alias);oldKey=alias.Key;
        }
        string proof=current.UpgradeReceiptFingerprint(oldKey);
        var upgrade=new GeneralModInstaller.UpgradeInfo{Owner="current-owner/default",Preset="preset",PreviousSource=source,Source=target,
            Version="2.0",PackageSha256=Safe.Hash(newZip),RelatedReceipts=new List<GeneralModInstaller.RelatedReceipt>{
                new GeneralModInstaller.RelatedReceipt{Data=Path.GetFullPath(aliasData),Key=oldKey,Owner="original-owner/default",Fingerprint=proof,CanonicalFingerprint=true}}};
        if(mode=="stale")current.Install(oldZip,oldKey,null);
        if(mode=="rollback")current.InstallCheckpoint=phase=>{if(phase=="update-written")throw new IOException("Fixture interruption.");};
        bool succeeds=mode=="generic"||mode=="snapshot"||mode=="owned";
        if(succeeds) {
            var updated=current.UpdatePackage(newZip,newKey,baseline,upgrade,null,null);
            check(current.ReadPackageRecord(oldKey)==null&&current.Status(newKey)==GeneralModInstaller.Installed&&
                updated.Files.All(file=>file.Owned)&&original.ReadPackageRecord(baseline.Key)!=null,
                "A proven "+mode+" authority can replace and retire an exact current-project recognition alias without touching foreign metadata.");
        }else {
            Throws(()=>current.UpdatePackage(newZip,newKey,baseline,upgrade,null,null));
            check(current.ReadPackageRecord(oldKey)!=null&&current.ReadPackageRecord(oldKey).Files.Where(file=>file.Owned).Count()==(mode=="mixed"?1:0)&&
                current.ReadPackageRecord(newKey)==null&&File.ReadAllText(Safe.In(game,"mods/main.txt"))=="old"&&
                !File.Exists(Safe.In(game,"mods/added.txt"))&&!current.RecoveryPending,
                "An alias with "+mode+" evidence cannot change files, promote recognition ownership or discard its previous metadata.");
        }
    }
    static string RecordPath(GeneralModInstaller engine,string key) {
        return (string)typeof(GeneralModInstaller).GetMethod("RecordPath",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(engine,new object[]{key});
    }
    static string SimpleZip(string folder,string name,Dictionary<string,string> files) {
        string path=Safe.In(folder,name);using(var zip=ZipFile.Open(path,ZipArchiveMode.Create))foreach(var file in files)
            using(var writer=new StreamWriter(zip.CreateEntry(file.Key).Open()))writer.Write(file.Value);
        return path;
    }
    static InstalledSnapshotInstaller.Receipt Read(InstalledSnapshotInstaller engine,string id) {
        return (InstalledSnapshotInstaller.Receipt)typeof(InstalledSnapshotInstaller).GetMethod("Read",BindingFlags.Instance|BindingFlags.NonPublic)
            .Invoke(engine,new object[]{id});
    }
    static void Write(InstalledSnapshotInstaller engine,InstalledSnapshotInstaller.Receipt receipt) {
        typeof(InstalledSnapshotInstaller).GetMethod("Save",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(engine,new object[]{receipt});
    }
    static string MakeSnapshot(string root,FixedCatalog catalog) {
        var manifest=new InstalledSnapshotManifest{Name="Related owners fixture",Game="generic"};
        foreach(string id in new[]{"alpha","beta","gamma"})manifest.List.Mods.Add(new GeneralModEntry{
            Id=id,Name=id,DefaultVariant="default",Variants=new List<GeneralModVariant>{new GeneralModVariant{
                Id="default",Name="Default",Sources=new List<string>{"installed://"+id+"/default"}}}});
        var contents=new Dictionary<string,byte[]>();
        foreach(string id in new[]{"alpha","beta","gamma","shared"}) {
            byte[] bytes=Encoding.UTF8.GetBytes(id);string hash=InstalledSnapshot.Hash(bytes);
            contents[hash]=bytes;
            manifest.Files.Add(new InstalledSnapshotFile{Path="mods/"+id+".txt",Kind="file",Sha256=hash,Size=bytes.Length,
                Owners=id=="shared"?new List<string>{"alpha/default","beta/default"}:new List<string>{id+"/default"}});
        }
        InstalledSnapshot.Validate(manifest,catalog);
        string path=Safe.In(root,"snapshot.zip");
        using(var zip=ZipFile.Open(path,ZipArchiveMode.Create)) {
            using(var writer=new StreamWriter(zip.CreateEntry("manifest.json").Open(),new UTF8Encoding(false)))writer.Write(Safe.Json.Serialize(manifest));
            foreach(var content in contents)using(var output=zip.CreateEntry("files/"+content.Key).Open())output.Write(content.Value,0,content.Value.Length);
        }
        return path;
    }
    static void Throws(Action action) {try{action();}catch(IOException){return;}throw new IOException("O registro alterado deveria ter sido preservado.");}
}
