using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

internal sealed partial class GeneralModWindow {
    sealed class SharedUpdateFixture {
        internal string Root,OldZip,NewZip,OwnerData,CloneData,OwnerSlot,SnapshotId;
        internal GeneralModState State,OwnerState,CloneState;
        internal FixedCatalog Catalog;
    }

    internal static void RunSharedUpdateTests(string parent) {
        string root=OnlineTests.NestedRoot(parent,"shared-updates-");Directory.CreateDirectory(root);
        var checks=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new IOException(name);checks.Add(name);};
        SharedUpdateRoundTrip(Safe.In(root,"generic"),false,check);
        SharedUpdateRoundTrip(Safe.In(root,"snapshot"),true,check);
        foreach(string reason in new[]{"unowned","missing-proof","different-source","modified","missing","unavailable"})
            SharedUpdateBlocked(Safe.In(root,reason),reason,check);
        SharedUpdateChangedDuringDownload(Safe.In(root,"changed-receipt"),false,check);
        SharedUpdateChangedDuringDownload(Safe.In(root,"changed-file"),true,check);
        SharedUpdateCrash(Safe.In(root,"crash"),check);
        SharedInventoryReceipt(Safe.In(root,"inventory-generic"),false,check);
        SharedInventoryReceipt(Safe.In(root,"inventory-cyberpunk"),true,check);
        Safe.WriteJson(Safe.In(root,"result.json"),new{Passed=true,Checks=checks,RealGameTouched=false,LiveAuthenticatedNexus=false});
    }

    static void SharedInventoryReceipt(string root,bool cyberpunk,Action<bool,string> check){
        Directory.CreateDirectory(root);
        string game=Directory.CreateDirectory(Safe.In(root,"g")).FullName;
        File.WriteAllText(Safe.In(game,".fixture-only"),"test");
        if(cyberpunk)SourceUpdateGameExecutable(game);
        string scope=cyberpunk?"r6/scripts/shared-inventory":"mods/shared-inventory";
        string anchor=scope+"/module.bin",payload=scope+"/package.json",generated=scope+"/generated/state.bin";
        string output="outputs/shared-inventory",outputFile=output+"/result.json";
        string domain=cyberpunk?"cyberpunk2077":"stardewvalley";
        string oldSource="https://www.nexusmods.com/"+domain+"/mods/543?tab=files&file_id=10";
        string newSource=oldSource.Replace("file_id=10","file_id=20");
        string oldZip=SourceUpdateZip(root,"old.zip",new Dictionary<string,string>{{anchor,"old"},{payload,"old package"}});
        string newZip=SourceUpdateZip(root,"new.zip",new Dictionary<string,string>{{anchor,"new"},{payload,"new package"}});
        var entry=NewEntry("Shared inventory fixture",oldSource);entry.Id="shared-inventory";
        var current=new GeneralModState{Destination=game,Language="pt",Theme="dark",DownloadFolder=root,
            List=new GeneralModList{Mods=new List<GeneralModEntry>{entry}}};
        string peerData=Directory.CreateDirectory(Safe.In(root,"peer")).FullName;
        var peer=GeneralPresetProfiles.Copy(current);
        var peerVariant=peer.List.Mods.Single().Variants.Single();
        string owner=InstalledSnapshot.Owner(entry,entry.Variants.Single()),oldKey=owner+"/0/"+oldSource,newKey=owner+"/0/"+newSource;
        var installer=new GeneralModInstaller(game,Path.Combine(peerData,"Instalacoes"));
        installer.Install(oldZip,oldKey,_=>{});
        var update=new GeneralModInstaller.UpgradeInfo{Owner=owner,PreviousSource=oldSource,Source=newSource,Version="2.0",
            PackageSha256=Safe.Hash(newZip),RetiredKeys=new List<string>{oldKey}};
        installer.UpdatePackage(newZip,newKey,installer.ReadPackageRecord(oldKey),update,null,_=>{});
        installer.MarkUpdatePredecessorsRetired(newKey);installer.MarkUpdateReconciled(newKey);
        peerVariant.Sources[0]=newSource;peerVariant.OutputFolders.Add(output);
        string peerList=Safe.In(peerData,"lista.json");new GeneralModLibrary(peerList).Save(peer);
        foreach(string path in new[]{generated,outputFile}){
            Directory.CreateDirectory(Path.GetDirectoryName(Safe.In(game,path)));File.WriteAllText(Safe.In(game,path),"local output");
        }
        string peerProof=Safe.Hash(peerList),data=Safe.In(root,"current");
        TestForm(SourceUpdateCatalog(),current,data,root,async form=>{
            var context=SharedUpdateContext(peerData,peer,"Peer fixture");
            form.testUpdateContexts=()=>new[]{context};
            var work=form.MakeWork(form.state.List.Mods.Single());
            check(form.VerifySharedInstalledUpdate(work,new ScanReads(form,game))!=null,
                domain+": status accepts the complete shared reconciled receipt.");
            var inventory=form.ReadGameInventory(game);
            check(new[]{anchor,payload}.All(path=>inventory.Any(file=>file.Path==path&&file.Kind=="associated"&&file.ModId==entry.Id&&file.ProvenOwner==entry.Id))&&
                !inventory.Any(file=>new[]{anchor,payload}.Contains(file.Path)&&file.Kind=="found"),
                domain+": inventory reuses the status receipt for exact package paths instead of listing them outside the project.");
            work.Entry.DetectedPaths.Add(anchor);
            check(form.ReadGameInventory(game).Count(file=>file.Path==anchor&&file.Kind=="associated")==1,
                domain+": an older manual detection does not duplicate the verified shared package row.");
            work.Entry.DetectedPaths.Clear();
            check(new[]{generated,outputFile}.All(path=>inventory.Any(file=>file.Path==path&&file.Kind=="associated"&&file.ModId==entry.Id&&
                file.LocalInModFolder&&file.ProvenOwner==null)),
                domain+": verified exclusive folder and peer-declared output folder provide contextual associations without false payload ownership.");
            check(Safe.Hash(peerList)==peerProof&&form.Generic(game).ReadUpdateRecords().Count==0&&
                work.Variant.Sources.Single()==oldSource&&work.Variant.OutputFolders.Count==0,
                domain+": inventory is read-only and does not copy peer receipts or rewrite the current preset.");
            var captured=new ScanReads(form,game);
            if(!captured.SharedUpdates.Value.Contains(work.Key))throw new IOException("The shared receipt fixture was not captured.");
            string receiptPath=Directory.GetFiles(Path.Combine(peerData,"Instalacoes"),"*.json",SearchOption.AllDirectories).Single();
            string originalReceipt=File.ReadAllText(receiptPath);
            var changedReceipt=installer.ReadUpdateRecord(newKey);changedReceipt.Upgrade.Version="3.0";
            Safe.WriteJson(receiptPath,changedReceipt);
            check(form.VerifySharedInstalledUpdate(work,captured)==null,
                domain+": receipt metadata changed after discovery cannot validate stale inventory evidence.");
            File.WriteAllText(receiptPath,originalReceipt);
            context.Proof=null;
            check(form.ReadSharedInventoryEvidence(game,null).Count==0,domain+": an unproven project cannot supply inventory ownership.");
            context.Proof=peerProof;
            string savedDestination=peer.Destination;peer.Destination=Directory.CreateDirectory(Safe.In(root,"other-game")).FullName;
            check(form.ReadSharedInventoryEvidence(game,null).Count==0,domain+": a receipt store declared for another destination is rejected.");
            peer.Destination=savedDestination;
            string expectedSource=work.Variant.Sources[0];work.Variant.Sources[0]=expectedSource.Replace(domain,"othergame");
            check(form.ReadSharedInventoryEvidence(game,null).Count==0,domain+": equal mod and file numbers from another game do not match the source identity.");
            work.Variant.Sources[0]=expectedSource;
            var other=NewEntry("Other folder owner","https://www.nexusmods.com/"+domain+"/mods/999?tab=files&file_id=1");
            other.Id="other-inventory-owner";other.Variants.Single().OutputFolders.Add(output);form.state.List.Mods.Add(other);
            string otherPath=scope+"/other.bin",otherZip=SourceUpdateZip(root,"other.zip",new Dictionary<string,string>{{otherPath,"another module"}});
            form.Generic(game).Install(otherZip,P2PPack.Slot(other,other.Variants.Single(),0),_=>{});
            inventory=form.ReadGameInventory(game);
            check(!inventory.Any(file=>new[]{generated,outputFile}.Contains(file.Path)&&file.Kind=="associated"),
                domain+": another registered owner in the folder or declared scope keeps generated output ambiguous.");
            File.WriteAllText(Safe.In(game,anchor),"manual alteration");
            check(form.ReadSharedInventoryEvidence(game,null).Count==0&&
                !form.ReadGameInventory(game).Any(file=>file.Path==payload&&file.ProvenOwner==entry.Id),
                domain+": a changed package invalidates the complete shared inventory proof on the next scan.");
            await Task.FromResult(0);
        });
    }

    static SharedUpdateFixture SharedUpdateSetup(string root,bool snapshot) {
        var fixture=new SharedUpdateFixture{Root=root,Catalog=SourceUpdateCatalog()};
        fixture.State=SourceUpdateState(root,out fixture.OldZip,out fixture.NewZip);
        fixture.OwnerData=Directory.CreateDirectory(Safe.In(root,"owner")).FullName;
        fixture.CloneData=Directory.CreateDirectory(Safe.In(root,"clone")).FullName;
        fixture.OwnerState=GeneralPresetProfiles.Copy(fixture.State);fixture.OwnerState.ProjectName="Projeto original";
        fixture.OwnerState.List.Mods.Single().Id="original-owner";
        var entry=fixture.OwnerState.List.Mods.Single();var variant=entry.Variants.Single();
        fixture.OwnerSlot=P2PPack.Slot(entry,variant,0);
        if(snapshot) {
            string sender=Directory.CreateDirectory(Safe.In(root,"sg")).FullName;
            File.WriteAllText(Safe.In(sender,".fixture-only"),"test");
            string senderData=Directory.CreateDirectory(Safe.In(root,"sd")).FullName;
            var senderState=GeneralPresetProfiles.Copy(fixture.OwnerState);senderState.Destination=sender;
            new GeneralModInstaller(sender,Path.Combine(senderData,"Instalacoes")).Install(fixture.OldZip,fixture.OwnerSlot,_=>{});
            string pack=Safe.In(root,"old-pack.zip");
            InstalledSnapshot.Create(pack,senderState,new[]{entry.Id},fixture.Catalog,senderData,false,_=>{});
            var installer=new InstalledSnapshotInstaller(fixture.State.Destination,fixture.OwnerData,fixture.Catalog,true);
            fixture.SnapshotId=installer.Install(pack,Safe.Hash(pack),_=>{});
            fixture.OwnerState.SnapshotReceipts[entry.Id+"/"+variant.Id]=fixture.SnapshotId;
            installer.DuplicateRecognition(fixture.CloneData);
        } else {
            var installer=new GeneralModInstaller(fixture.State.Destination,Path.Combine(fixture.OwnerData,"Instalacoes"));
            installer.Install(fixture.OldZip,fixture.OwnerSlot,_=>{});
            installer.DuplicateRecognition(Path.Combine(fixture.CloneData,"Instalacoes"));
        }
        new GeneralModLibrary(Safe.In(fixture.OwnerData,"lista.json")).Save(fixture.OwnerState);
        fixture.CloneState=GeneralPresetProfiles.Copy(fixture.OwnerState);fixture.CloneState.ProjectName="Projeto duplicado";
        new GeneralModLibrary(Safe.In(fixture.CloneData,"lista.json")).Save(fixture.CloneState);
        return fixture;
    }

    static GeneralUpdateOwnershipContext SharedUpdateContext(string data,GeneralModState state,string name) {
        string path=Safe.In(data,"lista.json");
        return new GeneralUpdateOwnershipContext{Data=data,State=state,Name=name,Proof=File.Exists(path)?Safe.Hash(path):null};
    }

    static GeneralUpdateOwnershipContext[] SharedUpdateContexts(SharedUpdateFixture fixture) {
        return new[]{SharedUpdateContext(fixture.OwnerData,fixture.OwnerState,"Projeto original"),
            SharedUpdateContext(fixture.CloneData,fixture.CloneState,"Projeto duplicado")};
    }


    static void SharedUpdateRoundTrip(string root,bool snapshot,Action<bool,string> check) {
        var fixture=SharedUpdateSetup(root,snapshot);string data=Safe.In(root,"d");
        var secondPlaylist=GeneralPresetProfiles.Copy(fixture.State);
        GeneralModState updatedProject=null;
        TestForm(fixture.Catalog,fixture.State,data,root,async form=>{
            int contexts=0;form.testUpdateContexts=()=>{contexts++;return SharedUpdateContexts(fixture);};
            form.testModUpdateSession=SourceUpdateSession();form.Persist();await form.Scan();await WaitForVersionCheck(form);
            var job=form.SelectedUpdates().Single();
            check(job.UpdateTarget==UpdateFixtureNew&&form.CountVersions().Ready==1,
                "A complete "+(snapshot?"snapshot":"generic")+" owner in a registered peer supplies update proof across different entry IDs.");
            int scans=contexts;
            for(int repeat=0;repeat<3;repeat++){form.DrawRows();form.RefreshPrimaryAction();form.CountVersions();}
            check(contexts==scans,"Painting rows and counting updates reuse the ownership index instead of enumerating projects again.");
            foreach(var stage in new[]{"Na fila","Aguardando download","Conferindo download","Baixando","Instalando"}){
                form.RowStatus(job,stage);
                form.DrawRows();
                var row=form.grid.Rows.Cast<System.Windows.Forms.DataGridViewRow>().Single(r=>Equals(r.Tag,job.Entry.Id));
                string expected=stage=="Instalando"?stage:UpdateAvailableStatus;
                check(Equals(row.Cells["Status"].Tag,expected)&&
                    row.Cells["Status"].Value.ToString().StartsWith(GeneralStatusPresentation.Label(expected)),
                    "The table shows the expected badge while the shared update is "+stage+".");
            }
            int downloads=0,opened=0;
            form.testUpdatePackage=target=>{downloads++;return SourceUpdatePackage(target,fixture.NewZip);};
            form.launch=_=>opened++;
            string error=null;form.testFailurePrompt=message=>{error=message;return false;};
            await form.RunWork(job,fixture.State.Destination,false);
            var updated=form.MakeWork(form.state.List.Mods.Single());
            check(form.statuses[updated.Key]=="Instalado"&&downloads==1&&opened==0&&
                updated.Variant.Sources.Single()==UpdateFixtureNew&&File.ReadAllText(Safe.In(fixture.State.Destination,"mods/first/main.txt"))=="new"&&
                !File.Exists(Safe.In(fixture.State.Destination,"mods/first/obsolete.txt"))&&File.Exists(Safe.In(fixture.State.Destination,"mods/first/added.txt")),
                "Updating directly replaces the shared payload through one verified download and publishes a new receipt. Failure="+error);
            if(snapshot) {
                string owner=fixture.OwnerState.List.Mods.Single().Id+"/default";
                check(new InstalledSnapshotInstaller(fixture.State.Destination,fixture.OwnerData,fixture.Catalog,true).Status(fixture.SnapshotId,owner)==GeneralModInstaller.Missing,
                    "Only after commit is the original peer snapshot owner retired without deleting the new files.");
                check(new InstalledSnapshotInstaller(fixture.State.Destination,fixture.CloneData,fixture.Catalog,true).Status(fixture.SnapshotId,owner)==GeneralModInstaller.Missing,
                    "The duplicate snapshot with the same ID in another data folder is retired separately.");
            }else {
                check(!new GeneralModInstaller(fixture.State.Destination,Path.Combine(fixture.OwnerData,"Instalacoes")).HasSnapshotRecord(fixture.OwnerSlot),
                    "Only after commit is the original peer generic receipt retired without deleting the new files.");
                check(!new GeneralModInstaller(fixture.State.Destination,Path.Combine(fixture.CloneData,"Instalacoes")).HasSnapshotRecord(fixture.OwnerSlot),
                    "A recognition-only duplicate is retired without granting it ownership.");
            }
            check(new GeneralModLibrary(Safe.In(fixture.OwnerData,"lista.json")).Load().List.Mods.Single().Variants.Single().Sources.Single()==UpdateFixtureOld&&
                new GeneralModLibrary(Safe.In(fixture.CloneData,"lista.json")).Load().List.Mods.Single().Variants.Single().Sources.Single()==UpdateFixtureOld,
                "Peer preset definitions intentionally keep the previous source instead of falsely recording a new installation.");
            await form.Scan();await WaitForVersionCheck(form);
            check(form.statuses[updated.Key]=="Instalado"&&form.SelectedUpdates().Count==0,
                "The updated local project remains installed on the next scan without obsolete peer claims or a repeated update.");
            updatedProject=form.library.Load();
        });
        TestForm(fixture.Catalog,secondPlaylist,Safe.In(root,"other-playlist"),root,async form=>{
            form.testUpdateContexts=()=>new[]{SharedUpdateContext(data,updatedProject,"Projeto atualizado")};
            form.testModUpdateSession=SourceUpdateSession();
            await form.Scan();await WaitForVersionCheck(form);
            var work=form.MakeWork(form.state.List.Mods.Single());
            check(form.statuses[work.Key]=="Instalado"&&form.sharedInstalledVersions[work.Key].Version=="2.0"&&
                form.SelectedUpdates().Count==0&&form.VersionInStatus(work.Key,"Instalado")=="2.0"&&
                work.Variant.Sources.Single()==UpdateFixtureOld,
                "A second playlist recognizes the verified successor at the same destination without changing its saved source.");
            File.WriteAllText(Safe.In(secondPlaylist.Destination,"mods/first/main.txt"),"edited after update");
            await form.Scan();await WaitForVersionCheck(form);
            check(!form.sharedInstalledVersions.ContainsKey(work.Key)&&form.statuses[work.Key]!="Instalado",
                "A shared receipt stops proving installation when its file changes.");
        });
    }

    static void SharedUpdateBlocked(string root,string reason,Action<bool,string> check) {
        var fixture=SharedUpdateSetup(root,false);
        var contexts=SharedUpdateContexts(fixture);
        if(reason=="unowned")contexts=new[]{contexts[1]};
        if(reason=="missing-proof")contexts[0].Proof=null;
        if(reason=="different-source") {
            fixture.OwnerState.List.Mods.Single().Variants.Single().Sources[0]="https://www.nexusmods.com/stardewvalley/mods/99?tab=files&file_id=10";
            fixture.OwnerState.List.Presets.Clear();fixture.OwnerState.PresetStateVersion=0;
            new GeneralModLibrary(Safe.In(fixture.OwnerData,"lista.json")).Save(fixture.OwnerState);contexts=SharedUpdateContexts(fixture);
        }
        if(reason=="modified")File.WriteAllText(Safe.In(fixture.State.Destination,"mods/first/main.txt"),"manual change");
        if(reason=="missing")File.Delete(Safe.In(fixture.State.Destination,"mods/first/main.txt"));
        if(reason=="unavailable") {
            string path=Safe.In(fixture.OwnerData,"lista.json");File.WriteAllText(path,"not valid project metadata");
            contexts[0].Proof=Safe.Hash(path);contexts[0].State=null;
        }
        TestForm(fixture.Catalog,fixture.State,Safe.In(root,"d"),root,async form=>{
            form.testUpdateContexts=()=>contexts;form.testModUpdateSession=SourceUpdateSession();form.Persist();
            await form.Scan();await WaitForVersionCheck(form);
            check(form.SelectedUpdates().Count==0&&form.CountVersions().Ready==0,
                "The scan does not offer automatic replacement when peer proof is "+reason+".");
            check(form.state.List.Mods.Single().Variants.Single().Sources.Single()==UpdateFixtureOld,
                "A blocked "+reason+" preflight preserves the selected source and leaves review to the user.");
        });
    }

    static void SharedUpdateChangedDuringDownload(string root,bool physicalFile,Action<bool,string> check) {
        var fixture=SharedUpdateSetup(root,false);
        TestForm(fixture.Catalog,fixture.State,Safe.In(root,"d"),root,async form=>{
            form.testUpdateContexts=()=>SharedUpdateContexts(fixture);form.testModUpdateSession=SourceUpdateSession();form.Persist();
            await form.Scan();await WaitForVersionCheck(form);var job=form.SelectedUpdates().Single();
            form.testUpdatePackage=async target=>{
                if(physicalFile)File.WriteAllText(Safe.In(fixture.State.Destination,"mods/first/main.txt"),"manual edit during download");
                else {
                    string receipt=Directory.GetFiles(Path.Combine(fixture.OwnerData,"Instalacoes"),"*.json",SearchOption.AllDirectories).Single();
                    File.AppendAllText(receipt," ");
                }
                return await SourceUpdatePackage(target,fixture.NewZip);
            };
            form.testFailurePrompt=_=>false;await form.RunWork(job,fixture.State.Destination,false);
            check(form.statuses[job.Key]=="Falhou"&&
                form.state.List.Mods.Single().Variants.Single().Sources.Single()==UpdateFixtureOld&&
                !form.Generic(fixture.State.Destination).HasSnapshotRecord(job.Key+"/0/"+UpdateFixtureNew),
                "A "+(physicalFile?"manual file edit":"peer receipt change")+" during an update invalidates the proof before replacement.");
            check(File.ReadAllText(Safe.In(fixture.State.Destination,"mods/first/main.txt"))==(physicalFile?"manual edit during download":"old")&&
                File.Exists(Safe.In(fixture.State.Destination,"mods/first/obsolete.txt"))&&!File.Exists(Safe.In(fixture.State.Destination,"mods/first/added.txt")),
                "Changes made during the download remain intact and no partial new payload is installed.");
        });
    }

    static void SharedUpdateCrash(string root,Action<bool,string> check) {
        var fixture=SharedUpdateSetup(root,false);
        TestForm(fixture.Catalog,fixture.State,Safe.In(root,"d"),root,async form=>{
            form.testUpdateContexts=()=>SharedUpdateContexts(fixture);form.testModUpdateSession=SourceUpdateSession();form.Persist();
            await form.Scan();await WaitForVersionCheck(form);var job=form.SelectedUpdates().Single();
            form.testUpdatePackage=target=>SourceUpdatePackage(target,fixture.NewZip);
            bool crashed=false;form.testUpdateCheckpoint=phase=>{if(phase=="files-committed"){crashed=true;throw new IOException("Fixture close after shared commit.");}};
            form.testFailurePrompt=_=>false;await form.RunWork(job,fixture.State.Destination,false);
            check(crashed&&File.ReadAllText(Safe.In(fixture.State.Destination,"mods/first/main.txt"))=="new"&&
                form.state.List.Mods.Single().Variants.Single().Sources.Single()==UpdateFixtureOld&&
                new GeneralModInstaller(fixture.State.Destination,Path.Combine(fixture.OwnerData,"Instalacoes")).HasSnapshotRecord(fixture.OwnerSlot),
                "The interruption occurs after durable file commit but before source save or foreign receipt retirement.");
            form.testUpdateCheckpoint=null;await form.RecoverSourceUpdates(fixture.State.Destination);
            var updated=form.MakeWork(form.state.List.Mods.Single());
            check(updated.Variant.Sources.Single()==UpdateFixtureNew&&form.Generic(fixture.State.Destination).Status(updated.Receipt(0))==GeneralModInstaller.Installed&&
                !new GeneralModInstaller(fixture.State.Destination,Path.Combine(fixture.OwnerData,"Instalacoes")).HasSnapshotRecord(fixture.OwnerSlot)&&
                !new GeneralModInstaller(fixture.State.Destination,Path.Combine(fixture.CloneData,"Instalacoes")).HasSnapshotRecord(fixture.OwnerSlot),
                "Recovery completes the source and retires only the exact shared predecessors recorded before the close.");
            await form.Scan();await WaitForVersionCheck(form);
            check(form.statuses[updated.Key]=="Instalado"&&form.SelectedUpdates().Count==0,
                "The recovered shared update survives the next scan without downloading or reinstalling.");
        });
    }
}
