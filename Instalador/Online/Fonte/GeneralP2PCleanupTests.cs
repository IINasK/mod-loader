using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;

internal static class GeneralP2PCleanupTests {
    const string Device="AAAAAAA-AAAAAAA-AAAAAAA-AAAAAAA-AAAAAAA-AAAAAAA-AAAAAAA-AAAAAAA";
    internal static int Run(string parent) {
        string root=OnlineTests.NestedRoot(parent,"cleanup-");Directory.CreateDirectory(root);
        var checks=new List<string>();Action<bool,string> check=(ok,label)=>{if(!ok)throw new IOException(label);checks.Add(label);};
        try {
            OfflineSenders(root,check);
            UnsafeTargets(root,check);
            ReceiverArtifacts(root,check);
            Safe.WriteJson(Safe.In(root,"resultado-p2p-cleanup.json"),new{Passed=true,Checks=checks,RealGameTouched=false,NetworkUsed=false});
            return checks.Count;
        } catch(Exception ex) {
            Safe.WriteJson(Safe.In(root,"resultado-p2p-cleanup.json"),new{Passed=false,Checks=checks,Error=ex.ToString()});throw;
        }
    }
    static P2PSession Session(bool sender,bool complete,bool lifecycle=false) {
        return new P2PSession{Id="ml-"+Guid.NewGuid().ToString("N"),Name="Fixture",Hash=new string('a',64),Size=16,
            Mode="installed-zip",Sender=sender,Completed=complete,Lifecycle=lifecycle,Peer=sender?null:Device};
    }
    static string Payload(P2PService service,P2PSession session) {
        string folder=service.Folder(session);Directory.CreateDirectory(folder);
        File.WriteAllText(Safe.In(folder,"pack.zip"),"fixture payload");
        File.WriteAllText(Safe.In(folder,"pack.zip.part"),"partial");
        File.WriteAllText(Safe.In(folder,"ready.json"),"fixture metadata");
        Directory.CreateDirectory(Safe.In(folder,".stfolder"));return folder;
    }
    static void Persist(P2PService service){Safe.WriteJson(Safe.In(service.Root,"sessions.json"),service.Sessions);}
    static void OfflineSenders(string root,Action<bool,string> check) {
        string privateRoot=Safe.In(root,"sender"),store=Safe.In(root,"transfers");
        var done=Session(true,true);done.Paused=true;done.Held=true;
        P2PSession active=Session(true,false),paused=Session(true,false),many=Session(true,false,true),inconsistent=Session(true,true,true);
        paused.Paused=true;paused.Held=true;
        many.Recipients=new[]{new P2PRecipient{Device=Device,Phase="installed"},new P2PRecipient{Device=Device.Replace('A','B'),Phase="retry"}};
        inconsistent.Recipients=new[]{new P2PRecipient{Device=Device,Phase="retry"}};
        var completeMany=Session(true,true,true);
        completeMany.Recipients=new[]{new P2PRecipient{Device=Device,Phase="installed"},new P2PRecipient{Device=Device.Replace('A','B'),Phase="discarded"}};
        string completedPath,activePath,pausedPath,manyPath,inconsistentPath,completeManyPath;
        using(var service=new P2PService(privateRoot,store,true)) {
            service.Sessions.AddRange(new[]{done,active,paused,many,inconsistent,completeMany});
            completedPath=Payload(service,done);activePath=Payload(service,active);pausedPath=Payload(service,paused);
            manyPath=Payload(service,many);inconsistentPath=Payload(service,inconsistent);completeManyPath=Payload(service,completeMany);
            string untracked=Directory.CreateDirectory(Safe.In(store,"unknown")).FullName;File.WriteAllText(Safe.In(untracked,"personal.zip"),"preserve");
            string originals=Directory.CreateDirectory(Safe.In(privateRoot,"received")).FullName;File.WriteAllText(Safe.In(originals,"original.zip"),"preserve legacy source");
            Persist(service);
            string home=Directory.CreateDirectory(Safe.In(privateRoot,"syncthing")).FullName;
            using(var otherOwner=new FileStream(Safe.In(home,"engine.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
                service.CleanupCompletedTransfers();
                check(Directory.Exists(completedPath),"A helper lease held by another instance prevents offline deletion.");
            }
        }
        using(var reopened=new P2PService(privateRoot,store,true)) {
            check(!reopened.Engine.Running&&!Directory.Exists(completedPath),"Reopening removes a completed sender's ZIP, partial ZIP and metadata without starting the helper or using the network.");
            check(!Directory.Exists(completeManyPath),"A completed multi-recipient send is cleaned after every recipient is installed or discarded.");
            check(Directory.Exists(activePath)&&Directory.Exists(pausedPath)&&Directory.Exists(manyPath),"Active, paused, held and partially delivered transfers retain their resume data.");
            check(Directory.Exists(inconsistentPath),"An inconsistent completion flag cannot delete a send whose recipient still requires a retry.");
            check(File.Exists(Safe.In(store,"unknown/personal.zip"))&&File.Exists(Safe.In(privateRoot,"received/original.zip")),"Cleanup preserves unregistered files and private legacy mod sources.");
            var completed=reopened.Sessions.Single(s=>s.Id==done.Id);
            check(completed.TransportReleased&&!completed.CleanupPending,"Successful offline cleanup is persisted independently of pause and hold flags.");
            string locked=Payload(reopened,completed);
            using(var lease=new FileStream(Safe.In(locked,"pack.zip"),FileMode.Open,FileAccess.Read,FileShare.Read)) {
                reopened.CleanupCompletedTransfers();
                check(completed.CleanupPending&&completed.Error==null&&File.Exists(Safe.In(locked,"pack.zip")),"A locked ZIP records a retryable cleanup failure without changing a successful installation into a transfer error.");
            }
            var deadline=DateTime.UtcNow.AddSeconds(12);
            while((Directory.Exists(locked)||completed.CleanupPending)&&DateTime.UtcNow<deadline)System.Threading.Thread.Sleep(40);
            check(!Directory.Exists(locked)&&!completed.CleanupPending&&!reopened.Engine.Running,"A background retry finishes cleanup after releasing the ZIP, without reopening sharing or starting the helper.");
            reopened.CleanupCompletedTransfers();
            check(!Directory.Exists(completedPath),"Repeated cleanup is idempotent and does not recreate a completed transfer directory.");
        }
    }
    static void ReceiverArtifacts(string root,Action<bool,string> check) {
        var catalog=new FixedCatalog{Packages=new List<FixedMod>()};
        string game=Directory.CreateDirectory(Safe.In(root,"game")).FullName,data=Directory.CreateDirectory(Safe.In(root,"data")).FullName;
        File.WriteAllText(Safe.In(game,".fixture-only"),"fixture");
        Directory.CreateDirectory(Safe.In(game,"mods"));File.WriteAllText(Safe.In(game,"mods/changed.txt"),"original user content");
        string zip=Pack(root,new Dictionary<string,string>{{"mods/changed.txt","updated"},{"mods/new.txt","new"}});
        var installer=new InstalledSnapshotInstaller(game,data,catalog,true);
        string id=installer.Install(zip,Safe.Hash(zip),null,chooseVersion:difference=>true);
        var flags=BindingFlags.NonPublic|BindingFlags.Instance;
        var receipt=(InstalledSnapshotInstaller.Receipt)typeof(InstalledSnapshotInstaller).GetMethod("Read",flags).Invoke(installer,new object[]{id});
        var changed=receipt.Files.Single(file=>file.Path=="mods/changed.txt");var added=receipt.Files.Single(file=>file.Path=="mods/new.txt");
        Func<InstalledSnapshotInstaller.Change,string> backup=file=>(string)typeof(InstalledSnapshotInstaller).GetMethod("BackupPath",flags).Invoke(installer,new object[]{receipt,file.Path});
        string required=backup(changed),redundant=backup(added);File.WriteAllText(redundant,"unused transfer backup");
        string stage=Directory.CreateDirectory(Safe.In(game,".mod-loader-snapshot-"+id)).FullName;
        string staged=Safe.In(stage,Fingerprint(added.Path)),rollback=Safe.In(stage,Fingerprint(changed.Path)+".rollback");
        File.WriteAllText(staged,"completed staging");File.WriteAllText(rollback,"temporary rollback");
        string unknown=Safe.In(stage,"manual-note.txt");File.WriteAllText(unknown,"not loader output");
        var done=Session(false,true,true);done.ReceiptId=id;done.Destination=game;
        var legacy=Session(false,true);legacy.ReceiptId=id;legacy.Destination=game;legacy.Paused=true;
        var interrupted=Session(false,false,true);interrupted.ReceiptId=id;interrupted.Destination=game;interrupted.Held=true;
        string payload,legacyPayload,interruptedPayload,privateRoot=Safe.In(root,"receiver"),store=Safe.In(root,"received-transport");
        using(var service=new P2PService(privateRoot,store,true)) {
            service.Sessions.AddRange(new[]{done,legacy,interrupted});payload=Payload(service,done);legacyPayload=Payload(service,legacy);interruptedPayload=Payload(service,interrupted);Persist(service);
        }
        using(var service=new P2PService(privateRoot,store,true)) {
            check(Directory.Exists(payload),"A receiver waits for its authenticated installation receipt before freeing the last received package.");
            done=service.Sessions.Single(s=>s.Id==done.Id);legacy=service.Sessions.Single(s=>s.Id==legacy.Id);
            using(var locked=new FileStream(rollback,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                service.EnableInstalledReceive(catalog,data,true);
                check(!Directory.Exists(payload)&&done.CleanupPending&&File.Exists(rollback),"A completed lifecycle receiver frees its ZIP offline even if a temporary rollback file needs a later deletion retry.");
            }
            done.NextCleanupUtc=DateTime.MinValue;legacy.NextCleanupUtc=DateTime.MinValue;service.CleanupCompletedTransfers();
            check(!File.Exists(staged)&&!File.Exists(rollback)&&!File.Exists(redundant),"Completed staging, rollback copies and backups unused by the uninstall receipt are removed.");
            check(File.Exists(required)&&File.ReadAllText(required)=="original user content", "The one original needed for safe uninstall remains intact.");
            check(File.Exists(unknown)&&File.Exists(zip),"Unknown files and the original source archive remain intact.");
            check(done.InstallArtifactsCleaned&&!done.CleanupPending&&installer.Status(id,"fixture/default")==GeneralModInstaller.Installed,"The receipt still recognizes the installed mod after temporary cleanup.");
            check(Directory.Exists(legacyPayload),"Legacy receivers retain their transport index until the old sender acknowledges delivery.");
            legacy.TransportReleased=true;Persist(service);service.CleanupCompletedTransfers();
            check(!Directory.Exists(legacyPayload),"A durable legacy acknowledgment releases the ZIP even if the old session remained paused.");
            check(Directory.Exists(interruptedPayload),"An interrupted receiver retains its entire transport for resume.");
            Directory.CreateDirectory(stage);File.WriteAllText(staged,"resume data");
            receipt.Status="installing";typeof(InstalledSnapshotInstaller).GetMethod("Save",flags).Invoke(installer,new object[]{receipt});
            check(!installer.CleanupCompletedArtifacts(id)&&File.Exists(staged),"An in-progress installation journal retains its staged files and rollback data.");
            receipt.Status="installed";typeof(InstalledSnapshotInstaller).GetMethod("Save",flags).Invoke(installer,new object[]{receipt});
            installer.CleanupCompletedArtifacts(id);installer.Remove(id,new[]{"fixture/default"},null);
            check(File.ReadAllText(Safe.In(game,"mods/changed.txt"))=="original user content"&&!File.Exists(Safe.In(game,"mods/new.txt")),"Uninstall still restores the preexisting file and removes only the mod's new file after cleanup.");
        }
    }
    static void UnsafeTargets(string root,Action<bool,string> check) {
        var session=Session(true,true);
        string store=Directory.CreateDirectory(Safe.In(root,"overlap")).FullName;
        string privateRoot=Directory.CreateDirectory(Safe.In(store,session.Id+"/private")).FullName;
        using(var service=new P2PService(privateRoot,store,true)) {
            service.Sessions.Add(session);string payload=Payload(service,session);Persist(service);service.CleanupCompletedTransfers();
            check(Directory.Exists(payload)&&File.Exists(Safe.In(privateRoot,"sessions.json"))&&session.CleanupPending,
                "An unsafe transport path cannot delete its own private session records.");
        }
        session=Session(true,true);privateRoot=Safe.In(root,"overlap-private");
        using(var service=new P2PService(privateRoot,Safe.In(root,"game-overlap"),true)) {
            service.Sessions.Add(session);string payload=Payload(service,session);session.Destination=payload;
            File.WriteAllText(Safe.In(payload,"game-file.txt"),"preserve");Persist(service);service.CleanupCompletedTransfers();
            check(File.Exists(Safe.In(payload,"game-file.txt"))&&session.CleanupPending,
                "A saved destination overlapping the transport cannot turn completion cleanup into a game-folder deletion.");
        }
    }
    static string Fingerprint(string path){return InstalledSnapshot.Hash(Encoding.UTF8.GetBytes(path)).Substring(0,32);}
    static string Pack(string root,Dictionary<string,string> files) {
        var manifest=new InstalledSnapshotManifest{Name="Fixture",Game="generic"};
        manifest.List.Mods.Add(new GeneralModEntry{Id="fixture",Name="Fixture",DefaultVariant="default",Variants=new List<GeneralModVariant>{
            new GeneralModVariant{Id="default",Name="Default",Sources=new List<string>{"installed://fixture/default"}}}});
        string path=Safe.In(root,"original-package.zip");
        using(var zip=ZipFile.Open(path,ZipArchiveMode.Create)) {
            foreach(var pair in files) {
                byte[] bytes=Encoding.UTF8.GetBytes(pair.Value);string hash=InstalledSnapshot.Hash(bytes);
                manifest.Files.Add(new InstalledSnapshotFile{Path=pair.Key,Kind="file",Sha256=hash,Size=bytes.Length,Owners=new List<string>{"fixture/default"}});
                using(var stream=zip.CreateEntry("files/"+hash).Open())stream.Write(bytes,0,bytes.Length);
            }
            using(var writer=new StreamWriter(zip.CreateEntry("manifest.json").Open(),new UTF8Encoding(false)))writer.Write(Safe.Json.Serialize(manifest));
        }
        return path;
    }
}
