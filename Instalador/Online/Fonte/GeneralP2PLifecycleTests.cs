using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

internal static class GeneralP2PLifecycleTests {
    static readonly FixedCatalog Catalog=new FixedCatalog{
        Schema=1,Id="lifecycle",GameVersion="2.5",GameFileVersion="2.5.0.0",
        Packages=new List<FixedMod>(),Defaults=new List<FixedDefault>()
    };

    internal static void Run(string parent,string helper){
        string root=OnlineTests.NestedRoot(parent,"lifecycle-");
        Directory.CreateDirectory(root);
        var checks=new List<string>();
        try{
            Network(root,helper,checks);
            Rollback(root,checks);
            Safe.WriteJson(Path.Combine(root,"resultado-p2p-lifecycle.json"),new{Passed=true,Tests=checks});
        }catch(Exception ex){
            Safe.WriteJson(Path.Combine(root,"resultado-p2p-lifecycle.json"),new{Passed=false,Tests=checks,Error=ex.ToString()});
            throw;
        }
    }

    internal static void RunEngine(string parent,string helper){
        string root=OnlineTests.NestedRoot(parent,"engine-");
        Directory.CreateDirectory(root);
        string ready=Safe.In(root,".fixture-engine");
        File.WriteAllText(ready,"");
        string home=Safe.In(root,"syncthing");
        var checks=new List<string>();
        string identity;
        var launch=new ProcessStartInfo(typeof(GeneralP2PLifecycleTests).Assembly.Location,
            "--self-test-p2p-engine-owner \""+root+"\" \""+helper+"\""){UseShellExecute=false,CreateNoWindow=true};
        using(var owner=Process.Start(launch)){
            try{
                Wait(()=>owner.HasExited||new FileInfo(ready).Length>0,"Fixture owner did not start the helper.");
                if(owner.HasExited)throw new IOException("Fixture owner exited before starting the helper.");
                identity=File.ReadAllText(ready);
                Check(checks,P2PInvite.DeviceValid(identity),"The fixture launched the verified Syncthing helper.");
            }finally{if(!owner.HasExited)owner.Kill();owner.WaitForExit();}
        }
        using(var reopened=new SyncthingEngine(home,true)){
            reopened.Start(helper);
            Check(checks,reopened.Running&&reopened.Device==identity,
                "An abruptly closed Mod Loader releases Syncthing and reopens with the same identity.");
        }
        using(var reopened=new SyncthingEngine(home,true)){
            reopened.Start(helper);
            Check(checks,reopened.Device==identity,"Normal close also releases the helper.");
        }
        string log=Safe.In(root,"startup.log");
        GeneralStyle.Language="en";
        File.WriteAllText(log,"Failed to acquire lock: is another Syncthing instance already running?");
        Check(checks,SyncthingEngine.StartupFailure(log,0,1).Message.Contains("previous helper"),
            "A fresh lock error identifies the previous helper.");
        Check(checks,!SyncthingEngine.StartupFailure(log,new FileInfo(log).Length,1).Message.Contains("previous helper"),
            "An older lock error does not hide a different current startup failure.");
        Safe.WriteJson(Safe.In(root,"resultado-p2p-engine.json"),new{Passed=true,Tests=checks});
    }

    internal static void RunEngineOwner(string root,string helper){
        string ready=Safe.In(root,".fixture-engine");
        if(!File.Exists(ready)||!System.Text.RegularExpressions.Regex.IsMatch(root,@"(?i)[\\/]online-testes-[a-f0-9]{32}(?:[\\/]|$)"))
            throw new IOException("O teste exige uma pasta de simulação isolada.");
        using(var engine=new SyncthingEngine(Safe.In(root,"syncthing"),true)){
            engine.Start(helper);
            File.WriteAllText(ready,engine.Device);
            Thread.Sleep(60000);
        }
    }

    static void Network(string root,string helper,List<string> checks){
        var source=Source(root,"host","shared.txt","from host");
        string left=Target(root,"left"),right=Target(root,"right"),leftData=Path.Combine(root,"left-data");
        var relay=Source(root,"friend","forwarded.txt","from friend",left,leftData);
        string thirdTarget=Target(root,"third");
        File.WriteAllText(Safe.In(left,"mods/shared.txt"),"original");
        source.Item1.List.Presets.Add(new GeneralModPreset{Id="no-pl",Name="Sem DLC Phantom Liberty",ModIds=source.Item1.List.Mods.Select(m=>m.Id).ToList()});
        source.Item1.List.Presets.Add(new GeneralModPreset{Id="all",Name="Com DLC Phantom Liberty",ModIds=source.Item1.List.Mods.Select(m=>m.Id).ToList()});
        source.Item1.SelectedPreset="no-pl";
        var all=P2PService.ExportState(source.Item1,null,source.Item1.List.Mods.Select(item=>item.Id).ToArray(),true);
        Check(checks,all.SelectedPreset==null&&all.List.Presets.Count==2&&source.Item1.SelectedPreset=="no-pl",
            "Exporting all presets keeps both and clears the single-preset label without editing the source.");
        var gate=new ManualResetEventSlim();
        var host=Service(root,"host-peer",source.Item2,helper);
        var first=Service(root,"first-peer",leftData,helper);
        var second=Service(root,"second-peer",Path.Combine(root,"right-data"),helper);
        string thirdData=Path.Combine(root,"third-data");
        var third=Service(root,"third-peer",thirdData,helper);
        var library=new GeneralProjectStore(Path.Combine(root,"lib"));
        third.RegisterProject=session=>{library.CreateReceived(session,thirdData,Catalog);return true;};
        try{
            first.ChooseVersion=_=>{gate.Wait(40000);return false;};
            host.SetPreparationState(source.Item1);
            first.SetPreparationState(relay.Item1);
            var sent=host.BeginDeferred(source.Item1,new[]{source.Item1.List.Mods.Single().Id},true,"no-pl");
            string invitation=host.Invitation(sent);
            Check(checks,P2PInvite.Decode(invitation).Schema==2&&!File.Exists(Safe.In(host.Folder(sent),"pack.zip")),
                "The new invitation creates no payload before a recipient connects.");
            var incoming=first.Accept(invitation,left,true);
            var incomingSecond=second.Accept(invitation,right,true);
            var incomingThird=third.Accept(invitation,thirdTarget,true);
            ConnectTo(first,host);ConnectTo(second,host);ConnectTo(third,host);
            Wait(()=>sent.Recipients.Length==3,"Three friends did not join the same invitation.");
            Wait(()=>sent.Recipients.Any(p=>p.Phase=="review")&&incomingSecond.Completed&&incomingThird.Completed,"The version review did not arrive while the other receiver completed.");
            Wait(()=>incomingThird.ListImported,"Installed transfer did not register its new project.");
            var project=library.Read().Projects.Single();
            var projectState=library.Load(project);
            Check(checks,project.Name=="Sem DLC Phantom Liberty"&&projectState.Destination==thirdTarget&&projectState.List.Presets.Single().Id=="no-pl"&&projectState.SnapshotReceipts.Count==1,
                "The chosen preset and installation receipt automatically create a separate project.");
            Check(checks,library.CreateReceived(incomingThird,thirdData,Catalog).Id==project.Id&&library.Read().Projects.Count==1,
                "Retrying registration does not duplicate the received project.");
            Check(checks,!sent.Completed&&File.Exists(Safe.In(host.Folder(sent),"pack.zip")),
                "The host retains its one ZIP while one recipient is choosing a version.");
            Check(checks,host.TransferStatus(sent).Percent==100&&host.TransferStatus(sent).Recipients.Any(line=>line.Caption.Contains(P2PService.PhaseLabel("review"))),
                "The sender can show 100% downloaded while installation is awaiting the version choice.");
            Check(checks,File.ReadAllText(Safe.In(left,"mods/shared.txt"))=="original",
                "A version review leaves the existing file intact.");

            incoming.Phase="installing";
            Wait(()=>host.TransferStatus(sent).Recipients.Any(line=>line.Indeterminate&&line.Caption.Contains(P2PService.PhaseLabel("installing"))),
                "An older receiver without progress must show an indeterminate installation.");
            incoming.InstallProgress=new P2PInstallProgress(false,2,5,"mods/shared.txt");
            Wait(()=>host.TransferStatus(sent).Recipients.Any(line=>line.Percent==40&&line.Caption.Contains("2/5")&&!line.Indeterminate),
                "The host did not receive verification progress while the receiver was busy.");
            incoming.InstallProgress=new P2PInstallProgress(true,3,5,"mods/shared.txt");
            Wait(()=>host.TransferStatus(sent).Recipients.Any(line=>line.Percent==60&&line.Caption.Contains("3/5")&&!line.Caption.Contains("mods/shared.txt")),
                "The host did not receive installation progress without the receiver's file path.");
            incoming.Phase="review";
            Check(checks,host.TransferStatus(sent).Percent==100&&!sent.Completed,"Remote installation progress does not change delivery completion.");
            var forwarded=first.BeginDeferred(relay.Item1,new[]{relay.Item1.List.Mods.Single().Id},true);
            var forwardedReceive=second.Accept(first.Invitation(forwarded),right,true);
            ConnectTo(second,first);
            Wait(()=>forwardedReceive.Completed,"The friend could not upload while its incoming version review was open.");
            Check(checks,File.ReadAllText(Safe.In(right,"mods/forwarded.txt"))=="from friend"&&!incoming.Completed,
                "A receiver can send a different sharing while awaiting a version choice.");

            var canceled=host.BeginDeferred(source.Item1,new[]{source.Item1.List.Mods.Single().Id},true);
            host.Pause(canceled);
            var canceledReceive=second.Accept(host.Invitation(canceled),right,true);
            Wait(()=>canceled.Recipients.Length==1,"The transfer to be canceled did not connect.");
            host.CancelSend(canceled);
            Wait(()=>!host.SnapshotSessions().Contains(canceled)&&!Directory.Exists(host.Folder(canceled)),
                "Canceling did not detach and clean the selected outgoing transport.");
            Check(checks,host.Engine.Running&&!sent.Completed&&File.Exists(Safe.In(host.Folder(sent),"pack.zip")),
                "Canceling an active transfer preserves another sharing on the same connection engine.");
            second.DiscardIncoming(canceledReceive);

            gate.Set();
            Wait(()=>incoming.Phase=="retry"&&incoming.Stage==null,"Keeping the current version did not defer installation.");
            string payload=Safe.In(first.Folder(incoming),"pack.zip");
            string zipHash=Safe.Hash(payload);
            first.Dispose();
            first=Service(root,"first-peer",leftData,helper,false);
            incoming=first.SnapshotSessions().Single(s=>!s.Sender);
            Check(checks,incoming.RecoveryPending&&Safe.Hash(payload)==zipHash,"A restart identifies the interrupted receiving session and preserves its ZIP.");
            first.PreserveInterrupted(incoming);
            first.Start(_=>{},helper);
            ConnectTo(first,host);
            Check(checks,incoming.Held&&!incoming.Completed&&Safe.Hash(payload)==zipHash,
                "Keeping for later preserves bytes and waits for an explicit retry.");
            first.ChooseVersion=_=>true;
            first.RetryInstalled(incoming,left);
            Wait(()=>incoming.Completed&&sent.Completed,"Retrying the original transfer did not install and acknowledge the package.");
            Check(checks,File.ReadAllText(Safe.In(left,"mods/shared.txt"))=="from host"&&host.Invitation(sent)==invitation,
                "The original invitation resumes installation and applies the accepted version.");
            Wait(()=>!Directory.Exists(host.Folder(sent))&&!Directory.Exists(first.Folder(incoming))&&!Directory.Exists(second.Folder(incomingSecond))&&!Directory.Exists(third.Folder(incomingThird)),
                "Confirmed transfers did not clean their payload directories.");
            Check(checks,sent.Recipients.All(p=>p.Phase=="installed"),"The sender finishes only after all three installation receipts arrive.");
        }finally{
            gate.Set();first.Dispose();second.Dispose();third.Dispose();host.Dispose();gate.Dispose();
        }
    }

    internal static void RunSync(string parent,string helper){
        string root=OnlineTests.NestedRoot(parent,"sync-");
        Directory.CreateDirectory(root);
        var checks=new List<string>();
        try{
            OnlineExtraCleanupTests.RunSyncOwnership(parent);
            Sync(root,helper,checks);
            CyberpunkSync(root,helper,checks);
            Safe.WriteJson(Path.Combine(root,"resultado-p2p-sync.json"),new{Passed=true,Tests=checks});
        }catch(Exception ex){
            Safe.WriteJson(Path.Combine(root,"resultado-p2p-sync.json"),new{Passed=false,Tests=checks,Error=ex.ToString()});
            throw;
        }
    }

    static void Sync(string root,string helper,List<string> checks){
        var source=Source(root,"sync-host","same.txt","shared contents");
        source.Item1.List.Mods.AddRange(Source(root,"diff","different.txt","host version",source.Item1.Destination,source.Item2).Item1.List.Mods);
        source.Item1.List.Mods.AddRange(Source(root,"miss","missing.txt","missing payload",source.Item1.Destination,source.Item2).Item1.List.Mods);
        var ids=source.Item1.List.Mods.Select(mod=>mod.Id).ToArray();
        string target=Target(root,"sync-receiver");
        string same=Safe.In(target,"mods/same.txt"),different=Safe.In(target,"mods/different.txt");
        File.WriteAllText(same,"shared contents");
        File.WriteAllText(different,"local version");
        var localOnly=Source(root,"local-only","local-only.txt","keep local",target,Path.Combine(root,"sync-data"));
        var stamp=new DateTime(2020,1,1,0,0,0,DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(same,stamp);
        using(var host=Service(root,"sync-host-p2p",source.Item2,helper))
        using(var friend=Service(root,"sync-friend-p2p",Path.Combine(root,"sync-data"),helper))
        using(var install=new ManualResetEventSlim()){
            host.SetPreparationState(source.Item1);
            friend.ChooseVersion=_=>install.Wait(40000);
            foreach(bool full in new[]{false,true}){
                install.Reset();
                File.WriteAllText(different,"local version");
                var sent=host.BeginDeferred(source.Item1,ids,true,compareBeforePack:true);
                var invitation=host.Invitation(sent);
                var received=friend.Accept(invitation,target,true);
                ConnectTo(friend,host);
                Wait(()=>friend.HasSyncPreview(received),"The signed comparison preview did not reach the receiver. "+sent.Error);
                string zip=Safe.In(host.Folder(sent),"pack.zip");
                Check(checks,P2PInvite.Decode(invitation).Schema==3&&!File.Exists(zip)&&sent.PayloadHash==null,
                    "The sender builds no ZIP until the receiver makes a choice.");
                var scanned=new List<int>();
                var comparison=friend.CompareIncoming(received,localOnly.Item1,(done,total,item)=>scanned.Add(done));
                Check(checks,scanned.SequenceEqual(new[]{0,1,2,3}),"The receiver reports all three physical files and scan completion.");
                Check(checks,comparison.Different.SequenceEqual(new[]{"diff"})&&
                    comparison.Missing.SequenceEqual(full?new string[0]:new[]{"miss"})&&comparison.Matching==(full?2:1)&&comparison.LocalOnly.SequenceEqual(new[]{"local-only"}),
                    "Real contents identify matching, missing and different mods.");
                if(full)
                    friend.CompareIncoming(received,report:(done,total,item)=>{throw new IOException("Full pack should not hash local files.");},full:true);
                host.SetPreparationState(null);
                friend.ChooseIncoming(received,full).GetAwaiter().GetResult();
                Wait(()=>sent.Recipients.Single().SyncChoicePhase=="choice_sent",
                    "The host did not receive the fast receiver choice.");
                Check(checks,sent.PayloadHash==null&&host.HasSyncChoice(sent),
                    "The host sees the confirmed choice before pack preparation, for both full and delta transfers.");
                host.SetPreparationState(source.Item1);
                Wait(()=>received.Phase=="review","The receiver did not reach the version confirmation. "+sent.Error+" "+received.Error);
                var packed=InstalledSnapshot.Read(zip,sent.PayloadHash,Catalog);
                Check(checks,packed.List.Mods.Count==3&&packed.Files.Single(file=>file.Path=="mods/same.txt").ReuseLocal==!full,
                    full?"The full option sends the complete pack.":"The delta keeps all records and reuses the matching file.");
                using(var archive=ZipFile.OpenRead(zip))
                    Check(checks,archive.Entries.Count==(full?4:3),"ZIP contains exactly the requested binary payloads.");
                install.Set();
                Wait(()=>received.Completed&&sent.Completed,"The comparison transfer did not complete. "+sent.Error+" "+received.Error);
                Check(checks,File.ReadAllText(different)=="host version"&&File.Exists(Safe.In(target,"mods/missing.txt"))&&File.GetLastWriteTimeUtc(same)==stamp&&File.ReadAllText(Safe.In(target,"mods/local-only.txt"))=="keep local",
                    "The receiver installs changes and preserves the existing identical file.");
                Check(checks,friend.InstalledManifest(received).List.Mods.Count==3,"The installed receipt retains the complete mod list.");
            }
        }
    }

    static void CyberpunkSync(string root,string helper,List<string> checks){
        FixedAsset unused;
        var catalog=OnlineTests.MakeCatalog(root,out unused);
        string source=OnlineTests.Fixture(root,"game-host"),target=OnlineTests.Fixture(root,"game-friend");
        string data=Safe.In(root,"cp-data"),hostData=Safe.In(root,"cp-source");
        var mods=GeneralModLibrary.Seed(catalog,new ModImportItem[0]);
        var state=new GeneralModState{Destination=source,List=mods};
        foreach(var file in catalog.Packages.SelectMany(mod=>mod.Files)){
            WriteSyncFile(source,file.Path,"test "+file.Path);
            WriteSyncFile(target,file.Path,"test "+file.Path);
        }
        string stale=catalog.Packages[1].Files[0].Path;
        WriteSyncFile(target,stale,"old version");
        const string outside="r6/scripts/Outside/one.reds",remove="archive/pc/mod/remove.archive",
            sidecar="archive/pc/mod/remove.archive.xl",keep="r6/tweaks/keep.yaml",baseFile="red4ext/plugins/NightCoop/preference.ini";
        WriteSyncFile(target,outside,"local version");
        const string empty="red4ext/plugins/stale-folder",sharedEmpty="red4ext/plugins/shared-empty",bundle="red4ext/plugins/stale-plugin";
        foreach(string path in new[]{remove,sidecar,keep,baseFile,bundle+"/mod.dll"})WriteSyncFile(target,path,path);
        const string cet="bin/x64/plugins/cyber_engine_tweaks/mods/";
        string hotline=cet+"NPCD_Hotline",stop=cet+"stopNCPD",sharedCet=cet+"shared";
        const string script="r6/scripts/NCPD_Hotline",oldShell="r6/scripts/OldShell";
        foreach(string path in new[]{hotline+"/init.lua",hotline+"/NPCD_Hotline.log",hotline+"/db.sqlite3",
            stop+"/stopNCPD.log",stop+"/db.sqlite3",script+"/nested/mod.reds",sharedCet+"/db.sqlite3"})
            WriteSyncFile(target,path,path);
        WriteSyncFile(target,sharedCet+"/init.lua","shared CET");
        Directory.CreateDirectory(Safe.In(target,hotline+"/nested/empty"));
        Directory.CreateDirectory(Safe.In(target,oldShell+"/nested"));
        Directory.CreateDirectory(Safe.In(target,empty));
        Directory.CreateDirectory(Safe.In(source,sharedEmpty));
        Directory.CreateDirectory(Safe.In(target,sharedEmpty));
        string hostPackage=Safe.In(root,"host-outside-mods.zip");
        using(var zip=ZipFile.Open(hostPackage,ZipArchiveMode.Create)){
            using(var writer=new StreamWriter(zip.CreateEntry(outside).Open()))writer.Write("host-only version");
            using(var writer=new StreamWriter(zip.CreateEntry(sharedCet+"/init.lua").Open()))writer.Write("shared CET");
        }
        new GeneralModInstaller(source,Path.Combine(hostData,"Instalacoes")).Install(hostPackage,"outside-mod",_=>{});
        string oldPackage=Safe.In(root,"old-receiver-mods.zip");
        var installer=new GeneralModInstaller(target,Path.Combine(data,"Instalacoes"));
        foreach(var mod in new[]{
            new[]{remove,sidecar},new[]{keep},new[]{bundle+"/mod.dll",hotline+"/init.lua",script+"/nested/mod.reds"}
        }){
            using(var zip=System.IO.Compression.ZipFile.Open(oldPackage,System.IO.Compression.ZipArchiveMode.Create))
                foreach(string path in mod)
                    using(var writer=new StreamWriter(zip.CreateEntry(path).Open()))writer.Write(path);
            installer.Install(oldPackage,Path.GetFileNameWithoutExtension(mod[0]),_=>{});
            File.Delete(oldPackage);
        }
        using(var host=Service(root,"cp-host",hostData,helper,catalog:catalog))
        using(var friend=Service(root,"cp-friend",data,helper,catalog:catalog)){
            host.SetPreparationState(state);
            friend.ChooseVersion=_=>true;
            var sent=host.BeginDeferred(state,new[]{"Extra"},true,compareBeforePack:true);
            var received=friend.Accept(host.Invitation(sent),target,true);
            ConnectTo(friend,host);
            Wait(()=>friend.HasSyncPreview(received),"Cyberpunk inventory did not reach the receiver. "+sent.Error);
            bool shown=false;
            friend.ChooseIncoming(received,false,review=>{
                shown=true;
                Check(checks,review.Different.Contains("Mod de teste")&&review.HostOutsideDifferences.SequenceEqual(new[]{outside}),
                    "Comparison identifies an older package file and a verified host file outside the preset.");
                Check(checks,review.Extras.Single(extra=>extra.Proven&&extra.Name=="remove").Files.Length==2&&
                    !review.Extras.Single(extra=>extra.Name=="stale-folder").Proven,
                    "Registered archive sidecars have proof; empty folders alone do not prove ownership.");
                Check(checks,review.Extras.Any(extra=>extra.Proven&&extra.Files.Length==3&&extra.Files.Any(file=>file.Path==hotline+"/init.lua"))&&
                    review.Extras.Where(extra=>extra.Name=="NPCD_Hotline"&&!extra.Proven).SelectMany(extra=>extra.Files).Any(file=>file.Path==hotline+"/db.sqlite3")&&
                    !review.Extras.Single(extra=>extra.Name=="stopNCPD").Proven&&
                    !review.Extras.Single(extra=>extra.Name=="OldShell").Proven&&!review.Extras.Any(extra=>extra.Name=="shared"),
                    "Runtime files and empty script shells remain unknown even beside a proven mod, while host state is protected.");
                bool blocked=false;
                try { friend.SubmitSyncChoice(review,false,Enumerable.Range(0,review.Extras.Length).ToArray()); }
                catch(IOException){blocked=true;}
                Check(checks,blocked&&!friend.HasSyncChoice(received),"An unfiltered select-all cannot authorize unknown files through the service.");
                return null;
            }).GetAwaiter().GetResult();
            Check(checks,shown&&!friend.HasSyncChoice(received)&&File.Exists(Safe.In(target,remove))&&sent.PayloadHash==null,
                "Closing the review makes no changes and sends no choice.");
            friend.ChooseIncoming(received,false,review=>Enumerable.Range(0,review.Extras.Length)
                .Where(index=>review.Extras[index].Proven&&!review.Extras[index].Files.Any(file=>file.Path==keep)).ToArray()).GetAwaiter().GetResult();
            Wait(()=> {
                if(received.Error!=null||sent.Error!=null)throw new IOException("Cyberpunk sync: "+received.Error+" "+sent.Error);
                return received.Completed&&sent.Completed;
            },"Selected Cyberpunk sync did not finish.");
            Check(checks,Directory.Exists(Safe.In(target,empty))&&!Directory.Exists(Safe.In(target,bundle))&&Directory.Exists(Safe.In(target,sharedEmpty)),
                "Only the empty ancestor of a removed registered file is pruned; unknown and shared folders remain.");
            Check(checks,!File.Exists(Safe.In(target,remove))&&!File.Exists(Safe.In(target,sidecar))&&File.Exists(Safe.In(target,keep))&&File.Exists(Safe.In(target,baseFile))&&
                File.ReadAllText(Safe.In(target,outside))=="local version"&&File.ReadAllText(Safe.In(target,stale))=="test "+stale,
                "Only checked extras are moved. Existing host files and unchecked mods stay while the approved version is updated.");
            Check(checks,!FixedInstaller.SyncInventoryEntry(stop+"/stopNCPD.log",Safe.Hash(Safe.In(target,sharedCet+"/db.sqlite3")))&&
                !FixedInstaller.SyncInventoryEntry(stop+"/db.sqlite3",Safe.Hash(Safe.In(target,sharedCet+"/db.sqlite3")))&&
                friend.InstalledManifest(received).Files.All(file=>!file.Path.EndsWith(".log")&&!file.Path.EndsWith(".sqlite3")),
                "CET runtime leftovers stay local and are never added to the transmitted mod inventory or package.");
            Check(checks,new[]{hotline,stop,oldShell}.All(path=>Directory.Exists(Safe.In(target,path)))&&
                !Directory.Exists(Safe.In(target,script))&&!File.Exists(Safe.In(target,hotline+"/init.lua"))&&
                File.Exists(Safe.In(target,hotline+"/db.sqlite3"))&&File.Exists(Safe.In(target,sharedCet+"/db.sqlite3")),
                "Only proven files move; generated logs/databases, unknown trees and unrelated empty subdirectories remain.");
            Check(checks,received.SyncLocalExtrasKept>1&&received.SyncHostFilesDifferent==1,"Known differences remain visible after installation.");
            using(var reopened=new P2PService(friend.Root,friend.Store,true)){
                var session=reopened.SnapshotSessions().Single(item=>item.Id==received.Id);
                var differences=reopened.ReadSyncDifferences(session);
                Check(checks,differences.Length>2&&differences.Any(item=>item.Kind=="kept"&&item.Mod=="keep"&&item.Path==keep)&&
                    differences.Any(item=>item.Kind=="different"&&item.Path==outside),
                    "Completed comparison keeps only unresolved paths and their origin across restarts.");
                reopened.DismissCompletedTransfer(session);
                Check(checks,reopened.ReadSyncDifferences(session)==null,"Dismissing the transfer discards its local differences report.");
            }
            new FixedInstaller(catalog,target,true).Undo(_=>{});
            Check(checks,File.Exists(Safe.In(target,remove))&&File.Exists(Safe.In(target,sidecar))&&
                Directory.Exists(Safe.In(target,empty))&&File.Exists(Safe.In(target,bundle+"/mod.dll")),"The removal journal restores files and empty folders.");
            Check(checks,Directory.Exists(Safe.In(target,hotline+"/nested/empty"))&&
                File.ReadAllText(Safe.In(target,hotline+"/db.sqlite3"))==hotline+"/db.sqlite3"&&
                File.ReadAllText(Safe.In(target,stop+"/stopNCPD.log"))==stop+"/stopNCPD.log"&&
                File.Exists(Safe.In(target,script+"/nested/mod.reds"))&&Directory.Exists(Safe.In(target,oldShell+"/nested")),
                "Undo restores CET logs and database, scripts and nested empty directories.");
        }
    }

    static void WriteSyncFile(string root,string path,string content){
        string target=Safe.In(root,path);
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        File.WriteAllText(target,content);
    }

    static void Rollback(string root,List<string> checks){
        var source=Source(root,"rollback","rollback.txt","incoming");
        string target=Target(root,"rollback-target"),data=Path.Combine(root,"rollback-data"),zip=Path.Combine(root,"rollback-snapshot.zip");
        File.WriteAllText(Safe.In(target,"mods/rollback.txt"),"local before");
        InstalledSnapshot.Create(zip,source.Item1,new[]{source.Item1.List.Mods.Single().Id},Catalog,source.Item2,true,_=>{});
        var installer=new InstalledSnapshotInstaller(target,data,Catalog,true);
        string transaction=null;
        try{
            installer.Install(zip,Safe.Hash(zip),message=>{
                if(message.StartsWith("Instalado:",StringComparison.Ordinal))throw new OperationCanceledException();
            },-1,_=>true,id=>transaction=id);
            throw new IOException("Installation should have been interrupted after a write.");
        }catch(OperationCanceledException){}
        Check(checks,transaction!=null&&File.ReadAllText(Safe.In(target,"mods/rollback.txt"))=="incoming",
            "An interrupted write leaves a durable transaction for recovery.");
        installer.DiscardInterrupted(transaction,zip,Safe.Hash(zip));
        Check(checks,File.ReadAllText(Safe.In(target,"mods/rollback.txt"))=="local before",
            "Discard restores the recorded prior version of the interrupted file.");
        installer.Install(zip,Safe.Hash(zip),_=>{},-1,_=>true);
        Check(checks,File.ReadAllText(Safe.In(target,"mods/rollback.txt"))=="incoming",
            "The rollback clears the unfinished transaction so a subsequent installation can proceed.");
    }

    static Tuple<GeneralModState,string> Source(string root,string name,string filename,string content,string game=null,string data=null){
        game=game??Target(root,name);
        data=data??Path.Combine(root,name+"-data");
        string archive=Path.Combine(root,name+".zip");
        using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create))
        using(var output=new StreamWriter(zip.CreateEntry("mods/"+filename).Open()))output.Write(content);
        var state=new GeneralModState{Destination=game};
        string id="fixture-"+name;
        state.List.Mods.Add(new GeneralModEntry{Id=id,Name=name,DefaultVariant="default",Variants=new List<GeneralModVariant>{
            new GeneralModVariant{Id="default",Name="Main",Sources=new List<string>{archive}}}});
        new GeneralModInstaller(game,Path.Combine(data,"Instalacoes")).Install(archive,id+"/default/0/"+archive,_=>{});
        return Tuple.Create(state,data);
    }

    static string Target(string root,string name){
        string path=Safe.In(root,name);
        Directory.CreateDirectory(Safe.In(path,"mods"));
        File.WriteAllText(Safe.In(path,".fixture-only"),"test");
        return path;
    }

    static P2PService Service(string root,string name,string data,string helper,bool start=true,FixedCatalog catalog=null){
        var service=new P2PService(Path.Combine(root,name),Path.Combine(root,name+"-files"),true,true);
        service.EnableInstalledReceive(catalog??Catalog,data,true);
        if(start)service.Start(_=>{},helper);
        return service;
    }

    static void ConnectTo(P2PService receiver,P2PService sender){
        receiver.Engine.ConfigurePeer(sender.Engine.Device,"tcp://127.0.0.1:"+sender.Engine.ListenPort);
    }

    static void Wait(Func<bool> condition,string error){
        var elapsed=Stopwatch.StartNew();
        while(!condition()){
            if(elapsed.ElapsedMilliseconds>45000)throw new IOException(error);
            Thread.Sleep(75);
        }
    }

    static void Check(List<string> checks,bool value,string description){
        if(!value)throw new IOException(description);
        checks.Add(description);
    }
}
