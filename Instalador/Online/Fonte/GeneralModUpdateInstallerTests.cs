using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;

internal static class GeneralModUpdateInstallerTests {
    const string Owner="fixture-mod/main";
    const string PreviousSource="https://www.nexusmods.com/fixturegame/mods/123?tab=files&file_id=10";
    const string NextSource="https://www.nexusmods.com/fixturegame/mods/123?tab=files&file_id=20";
    static string OldKey {get{return Owner+"/0/"+PreviousSource;}}
    static string NewKey {get{return Owner+"/0/"+NextSource;}}

    sealed class Fixture {
        internal string Folder,Game,State,Old,Next;
        internal GeneralModInstaller Engine;
        internal GeneralModInstaller.Record Baseline;
        internal GeneralModInstaller.UpgradeInfo Upgrade;
        internal Fixture(string parent,string name,Dictionary<string,string> oldFiles=null,Dictionary<string,string> nextFiles=null) {
            Folder=Safe.In(parent,name);Directory.CreateDirectory(Folder);
            Game=Safe.In(Folder,"game");State=Safe.In(Folder,"state");Directory.CreateDirectory(Game);
            Old=Zip(Folder,"old.zip",oldFiles??new Dictionary<string,string>{{"Data/mod.txt","old"},{"Data/obsolete.txt","obsolete"}});
            Next=Zip(Folder,"new.zip",nextFiles??new Dictionary<string,string>{{"Data/mod.txt","new"},{"Data/added.txt","added"}});
            Engine=new GeneralModInstaller(Game,State);Engine.Install(Old,OldKey,null);
            Baseline=Engine.ReadPackageRecord(OldKey);
            Upgrade=new GeneralModInstaller.UpgradeInfo {Owner=Owner,Preset="preset-one",PreviousSource=PreviousSource,
                Source=NextSource,Version="2.0",PackageSha256=Safe.Hash(Next),RetiredKeys=new List<string>{OldKey}};
        }
        internal GeneralModInstaller.Record Update(IEnumerable<string> shared=null,Action checkpoint=null,Func<string,string> backup=null,
            Action<GeneralModInstaller.RecordFile> validate=null) {
            return Engine.UpdatePackage(Next,NewKey,Baseline,Upgrade,shared,null,checkpoint,backup,validate);
        }
        internal string Read(string relative) {return File.ReadAllText(Safe.In(Game,relative));}
    }

    internal static int Run(string parent) {
        string root=Path.Combine(Path.GetFullPath(parent),"online-testes-"+Guid.NewGuid().ToString("N"));
        Safe.NoLinks(root);Directory.CreateDirectory(root);
        var checks=new List<string>();Action<bool,string> check=(ok,message)=>{if(!ok)throw new IOException(message);checks.Add(message);};
        var normal=new Fixture(root,"normal");
        var record=normal.Update();
        check(normal.Read("Data/mod.txt")=="new"&&normal.Read("Data/added.txt")=="added"&&!File.Exists(Safe.In(normal.Game,"Data/obsolete.txt")),
            "Changed, added and removed files follow the new package, without obsolete remnants.");
        check(normal.Engine.Status(NewKey)==GeneralModInstaller.Installed&&normal.Engine.ReadPackageRecord(OldKey)==null&&
            normal.Engine.ReadUpdateRecord(NewKey).Upgrade.Source==NextSource&&record.SourceSha256==Safe.Hash(normal.Next),
            "The committed receipt atomically records the new source, package hash and version and retires the former receipt.");
        check(normal.Engine.ReadUpdateRecords().Count==1,"Successful updates can be recovered from receipts before a project save.");
        check(Directory.GetFiles(normal.State,"*.bin",SearchOption.AllDirectories).Length==0&&
            Directory.GetDirectories(normal.Game,".mod-loader-*").Length==0,
            "A successful update releases rollback-only backups and its extraction stage.");
        var marked=normal.Engine.MarkUpdatePredecessorsRetired(NewKey);
        var markedAgain=normal.Engine.MarkUpdatePredecessorsRetired(NewKey);
        check(marked.Upgrade.PredecessorsRetired&&Safe.Json.Serialize(marked)==Safe.Json.Serialize(markedAgain),
            "The durable predecessor-retirement marker is idempotent.");
        marked.Upgrade.Version="changed copy";
        check(normal.Engine.ReadUpdateRecord(NewKey).Upgrade.Version=="2.0",
            "Reading or marking an update returns a defensive copy of its committed descriptor.");
        var reconciled=normal.Engine.MarkUpdateReconciled(NewKey);
        check(reconciled.Upgrade.Reconciled&&normal.Engine.MarkUpdateReconciled(NewKey).Upgrade.Reconciled&&
            reconciled.Upgrade.PredecessorsRetired,
            "The project-reconciliation marker is durable and independent from predecessor retirement.");
        normal.Engine.Remove(NewKey);
        check(Directory.GetFiles(normal.Game,"*",SearchOption.AllDirectories).Length==0,
            "Uninstalling the updated mod removes its current files without restoring an obsolete mod version.");

        var retained=new Fixture(root,"preexisting",new Dictionary<string,string>{{"Data/mod.txt","old"}},
            new Dictionary<string,string>{{"Data/mod.txt","new"},{"Data/existing.txt","existing"}});
        File.WriteAllText(Safe.In(retained.Game,"Data/existing.txt"),"existing");retained.Update();retained.Engine.Remove(NewKey);
        check(retained.Read("Data/existing.txt")=="existing","An identical file predating this update remains unowned and survives uninstall.");

        var original=new Fixture(root,"original");
        string externalBackup=Safe.In(original.Folder,"original.txt");File.WriteAllText(externalBackup,"before-any-mod");
        string originalHash=Safe.Hash(externalBackup);
        original.Baseline.Files.Single(file=>file.Path=="Data/mod.txt").Before=originalHash;
        // Foreign receipts can carry ownership and original backups without
        // inventing that a newly discovered file belongs to this mod.
        original.Upgrade.RetiredKeys.Clear();
        original.Engine=new GeneralModInstaller(original.Game,Safe.In(original.Folder,"foreign-state"));
        original.Update(backup:hash=>hash==originalHash?externalBackup:null);original.Engine.Remove(NewKey);
        check(original.Read("Data/mod.txt")=="before-any-mod"&&!File.Exists(Safe.In(original.Game,"Data/obsolete.txt")),
            "An imported baseline preserves the original pre-mod backup across update and uninstall.");
        check(Directory.GetFiles(Safe.In(original.Folder,"foreign-state"),"*.bin",SearchOption.AllDirectories).Length==0,
            "Restoring the original during uninstall releases its last referenced backup.");

        var repair=new Fixture(root,"repair");
        repair.Upgrade.PreviousReceiptIds.Add(new string('a',32));repair.Update();repair.Engine.MarkUpdatePredecessorsRetired(NewKey);repair.Engine.MarkUpdateReconciled(NewKey);
        File.Delete(Safe.In(repair.Game,"Data/added.txt"));
        repair.Engine.Install(repair.Next,NewKey,null,expectedSourceHash:repair.Upgrade.PackageSha256);
        var repaired=repair.Engine.ReadUpdateRecord(NewKey);
        check(repair.Engine.Status(NewKey)==GeneralModInstaller.Installed&&repaired!=null&&
            repaired.Upgrade.PredecessorsRetired&&repaired.Upgrade.Reconciled&&repaired.Upgrade.PreviousReceiptIds.SequenceEqual(new[]{new string('a',32)}),
            "Repairing an approved updated package preserves its durable identity and retirement marker.");
        string different=Zip(repair.Folder,"different.zip",new Dictionary<string,string>{{"Data/mod.txt","different"},{"Data/added.txt","added"}});
        repair.Engine.Install(different,NewKey,null,_=>true);
        check(repair.Engine.ReadUpdateRecord(NewKey)==null,
            "Installing different package bytes cannot inherit the verified update descriptor.");

        var removedOriginal=new Fixture(root,"removed-original");
        string beforeObsolete=Safe.In(removedOriginal.Folder,"preexisting.txt");File.WriteAllText(beforeObsolete,"original-obsolete");
        string beforeObsoleteHash=Safe.Hash(beforeObsolete);
        removedOriginal.Baseline.Files.Single(file=>file.Path=="Data/obsolete.txt").Before=beforeObsoleteHash;
        removedOriginal.Upgrade.RetiredKeys.Clear();
        removedOriginal.Engine=new GeneralModInstaller(removedOriginal.Game,Safe.In(removedOriginal.Folder,"foreign"));
        removedOriginal.Update(backup:hash=>hash==beforeObsoleteHash?beforeObsolete:null);
        check(removedOriginal.Read("Data/obsolete.txt")=="original-obsolete",
            "When a new version drops an override, the original file is restored rather than deleted.");

        var edited=new Fixture(root,"edited");File.WriteAllText(Safe.In(edited.Game,"Data/mod.txt"),"user edited");
        Throws(()=>edited.Update());
        check(edited.Read("Data/mod.txt")=="user edited"&&!File.Exists(Safe.In(edited.Game,"Data/added.txt"))&&
            edited.Engine.ReadPackageRecord(OldKey)!=null&&!edited.Engine.RecoveryPending,
            "A manual change blocks the entire update before any installed file changes.");
        var editedRemoved=new Fixture(root,"edited-removed");File.WriteAllText(Safe.In(editedRemoved.Game,"Data/obsolete.txt"),"user extra");
        Throws(()=>editedRemoved.Update());
        check(editedRemoved.Read("Data/mod.txt")=="old"&&editedRemoved.Read("Data/obsolete.txt")=="user extra",
            "A manually modified obsolete file is preserved and is not silently forgotten.");

        var linked=new Fixture(root,"linked-update",new Dictionary<string,string>{{"Data/mod.txt","old"}},
            new Dictionary<string,string>{{"Data/mod.txt","new"},{"Data/extra/replace.txt","current"}});
        var attached=new List<GeneralModInstaller.RecordFile>();
        foreach(string path in new[]{"Data/extra/replace.txt","Data/extra/keep.txt"}){
            string target=Safe.In(linked.Game,path);
            Directory.CreateDirectory(Path.GetDirectoryName(target));File.WriteAllText(target,"before");
            attached.Add(new GeneralModInstaller.RecordFile{Path=path,Size=new FileInfo(target).Length,Sha256=Safe.Hash(target),Owned=true});
        }
        string[] backedUp=null;
        Func<string[],GeneralModInstaller.Record> updateLinked=sharedPaths=>linked.Engine.UpdatePackage(linked.Next,NewKey,linked.Baseline,
            linked.Upgrade,sharedPaths,null,backupVersion:backup=>backedUp=backup.Files.Select(file=>file.Path).ToArray(),attachedFiles:attached);
        Throws(()=>updateLinked(new[]{"Data/extra/replace.txt"}));
        check(linked.Read("Data/extra/replace.txt")=="before","A shared attachment cannot be overwritten by one owner.");
        File.WriteAllText(Safe.In(linked.Game,"Data/extra/replace.txt"),"edited");
        Throws(()=>updateLinked(new string[0]));
        check(linked.Read("Data/mod.txt")=="old","A changed attachment blocks replacement before any payload writes.");
        File.WriteAllText(Safe.In(linked.Game,"Data/extra/replace.txt"),"before");
        var updatedAttachments=updateLinked(new string[0]);
        check(updatedAttachments.Files.Any(file=>file.Path=="Data/extra/replace.txt"&&file.Owned)&&
            linked.Read("Data/extra/replace.txt")=="current"&&linked.Read("Data/extra/keep.txt")=="before"&&
            attached.All(file=>backedUp.Contains(file.Path)),
            "A verified linked file can be updated by the package, and unchanged linked files are kept and included in the optional backup.");

        var unrelated=new Fixture(root,"review-mismatch",new Dictionary<string,string>{{"Data/mod.bin","old"}},
            new Dictionary<string,string>{{"Data/different.bin","unrelated"}});
        Throws(()=>unrelated.Engine.UpdatePackage(unrelated.Next,NewKey,unrelated.Baseline,unrelated.Upgrade,null,null,
            requireMatchingPayload:true));
        check(unrelated.Read("Data/mod.bin")=="old"&&!File.Exists(Safe.In(unrelated.Game,"Data/different.bin")),
            "A reviewed release archive with no matching payload path cannot replace the installed variant.");

        var shared=new Fixture(root,"shared");Throws(()=>shared.Update(new[]{"Data/mod.txt"}));
        check(shared.Read("Data/mod.txt")=="old"&&!File.Exists(Safe.In(shared.Game,"Data/added.txt")),
            "Replacing a file used by another owner blocks all writes.");
        var sharedRemoved=new Fixture(root,"shared-removed");sharedRemoved.Update(new[]{"Data/obsolete.txt"});
        check(sharedRemoved.Read("Data/obsolete.txt")=="obsolete"&&sharedRemoved.Engine.ReadUpdateRecord(NewKey).Files.All(file=>file.Path!="Data/obsolete.txt"),
            "An obsolete file still used by another owner stays on disk without becoming a file of the new package.");
        var sharedReceipt=new Fixture(root,"shared-receipt");
        string peer=Zip(sharedReceipt.Folder,"peer.zip",new Dictionary<string,string>{{"Data/mod.txt","old"}});
        sharedReceipt.Engine.Install(peer,"other-mod/main/0/https://example.org/peer.zip",null);
        Throws(()=>sharedReceipt.Update());
        check(sharedReceipt.Read("Data/mod.txt")=="old","Shared ownership in existing generic receipts is checked even when the caller supplies no shared paths.");

        var collision=new Fixture(root,"collision");File.WriteAllText(Safe.In(collision.Game,"Data/added.txt"),"unrelated");Throws(()=>collision.Update());
        check(collision.Read("Data/added.txt")=="unrelated"&&collision.Read("Data/mod.txt")=="old",
            "A new package file cannot overwrite an unrelated preexisting file.");
        var unowned=new Fixture(root,"unowned");unowned.Baseline.Files.Single(file=>file.Path=="Data/mod.txt").Owned=false;
        unowned.Upgrade.RetiredKeys.Clear();unowned.Engine=new GeneralModInstaller(unowned.Game,Safe.In(unowned.Folder,"foreign"));
        Throws(()=>unowned.Update());check(unowned.Read("Data/mod.txt")=="old","An unowned baseline does not authorize replacement.");

        var failure=new Fixture(root,"failure");int written=0;
        failure.Engine.InstallCheckpoint=stage=>{if(stage=="update-written"&&++written==3)throw new IOException("fixture write failure");};
        Throws(()=>failure.Update());
        check(failure.Read("Data/mod.txt")=="old"&&failure.Read("Data/obsolete.txt")=="obsolete"&&
            !File.Exists(Safe.In(failure.Game,"Data/added.txt"))&&failure.Engine.ReadPackageRecord(OldKey)!=null&&
            failure.Engine.ReadPackageRecord(NewKey)==null&&!failure.Engine.RecoveryPending,
            "Failure after replacement, creation and deletion rolls back the full transaction and its receipt.");
        var cancelled=new Fixture(root,"cancelled");
        cancelled.Engine.InstallCheckpoint=stage=>{if(stage=="update-written")throw new OperationCanceledException();};
        bool cancelledAsRequested=false;try{cancelled.Update();}catch(OperationCanceledException){cancelledAsRequested=true;}
        check(cancelledAsRequested&&cancelled.Read("Data/mod.txt")=="old"&&!cancelled.Engine.RecoveryPending,
            "Cancellation after a write rolls back before releasing the operation lock.");

        var badHash=new Fixture(root,"bad-hash");badHash.Upgrade.PackageSha256=new string('a',64);Throws(()=>badHash.Update());
        check(badHash.Read("Data/mod.txt")=="old"&&!badHash.Engine.RecoveryPending,"The package SHA-256 must match the approved download before extraction.");
        var badReceipt=new Fixture(root,"bad-receipt");badReceipt.Upgrade.PreviousReceiptIds.Add("../unrelated");Throws(()=>badReceipt.Update());
        check(badReceipt.Read("Data/mod.txt")=="old"&&!badReceipt.Engine.RecoveryPending,
            "Predecessor transaction identifiers must be bounded exact identities before any staging or installation.");
        var traversal=new Fixture(root,"traversal",nextFiles:new Dictionary<string,string>{{"../escaped.txt","bad"}});Throws(()=>traversal.Update());
        check(!File.Exists(Safe.In(traversal.Folder,"escaped.txt"))&&traversal.Read("Data/mod.txt")=="old","Archive traversal is rejected before installation.");
        var protectedPath=new Fixture(root,"protected");Throws(()=>protectedPath.Update(validate:file=>{if(file.Path=="Data/added.txt")throw new IOException("protected fixture path");}));
        check(protectedPath.Read("Data/mod.txt")=="old"&&!protectedPath.Engine.RecoveryPending,
            "The coordinator can reject protected destinations after inspection and before any writes.");

        foreach(string phase in new[]{"prepared","written","committed"}) {
            var crash=new Fixture(root,"crash-"+phase);Crash(crash,phase);
            check(crash.Engine.RecoveryPending,"Abrupt process exit leaves a durable "+phase+" journal.");
            var reopened=new GeneralModInstaller(crash.Game,crash.State);reopened.RecoverInterruptedInstall();
            bool committed=phase=="committed";
            check(!reopened.RecoveryPending&&crash.Read("Data/mod.txt")== (committed?"new":"old")&&
                File.Exists(Safe.In(crash.Game,"Data/added.txt"))==committed&&
                File.Exists(Safe.In(crash.Game,"Data/obsolete.txt"))!=committed&&
                (reopened.ReadPackageRecord(NewKey)!=null)==committed&&(reopened.ReadPackageRecord(OldKey)!=null)!=committed,
                "Reopening after an abrupt "+phase+" exit recovers both disk files and matching receipt identities.");
            check(Directory.GetFiles(crash.State,"*.bin",SearchOption.AllDirectories).Length==0&&
                Directory.GetDirectories(crash.Game,".mod-loader-*").Length==0,
                "Recovery after a "+phase+" exit releases transient backups and the stage.");
        }
        var afterCrash=new Fixture(root,"crash-manual");Crash(afterCrash,"written");File.WriteAllText(Safe.In(afterCrash.Game,"Data/mod.txt"),"manual after crash");
        Throws(()=>new GeneralModInstaller(afterCrash.Game,afterCrash.State).RecoverInterruptedInstall());
        check(afterCrash.Read("Data/mod.txt")=="manual after crash"&&afterCrash.Engine.RecoveryPending,
            "Recovery preserves a manual edit made after an interrupted update and retains its journal.");
        var moved=new Fixture(root,"crash-moved");Crash(moved,"written");File.Move(Safe.In(moved.Game,"Data/mod.txt"),Safe.In(moved.Game,"Data/manual.txt"));
        Throws(()=>new GeneralModInstaller(moved.Game,moved.State).RecoverInterruptedInstall());
        check(moved.Read("Data/manual.txt")=="new"&&!File.Exists(Safe.In(moved.Game,"Data/mod.txt"))&&moved.Engine.RecoveryPending,
            "Recovery does not recreate an updated file moved manually after a crash.");

        Safe.WriteJson(Safe.In(root,"resultado-mod-update-installer.json"),new{Passed=true,Count=checks.Count,Checks=checks});
        return checks.Count;
    }

    static void Crash(Fixture fixture,string phase) {
        File.WriteAllText(Safe.In(fixture.Folder,".update-fixture-only"),"isolated fixture");
        Safe.WriteJson(Safe.In(fixture.Folder,"baseline.json"),fixture.Baseline);
        Safe.WriteJson(Safe.In(fixture.Folder,"upgrade.json"),fixture.Upgrade);
        string exe=typeof(GeneralModUpdateInstallerTests).Assembly.Location;
        var start=new ProcessStartInfo(exe,"--self-test-mod-update-crash \""+fixture.Folder+"\" "+phase){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=fixture.Folder};
        using(var process=Process.Start(start)) {
            if(!process.WaitForExit(45000)){process.Kill();throw new IOException("O teste isolado de interrupção não terminou.");}
            if(process.ExitCode!=83)throw new IOException("O processo isolado não atingiu a interrupção esperada: "+phase+" ("+process.ExitCode+").");
        }
    }
    internal static void CrashWorker(string folder,string phase) {
        folder=Path.GetFullPath(folder);Safe.NoLinks(folder);
        if(!File.Exists(Safe.In(folder,".update-fixture-only"))||!Regex.IsMatch(folder,@"(?i)[\\/]online-testes-[a-f0-9]{32}[\\/]")||
            !new[]{"prepared","written","committed"}.Contains(phase))throw new IOException("A interrupção só pode ser testada em uma fixture isolada.");
        var engine=new GeneralModInstaller(Safe.In(folder,"game"),Safe.In(folder,"state"));
        var baseline=Safe.Json.Deserialize<GeneralModInstaller.Record>(File.ReadAllText(Safe.In(folder,"baseline.json")));
        var upgrade=Safe.Json.Deserialize<GeneralModInstaller.UpgradeInfo>(File.ReadAllText(Safe.In(folder,"upgrade.json")));
        int writes=0;
        engine.InstallCheckpoint=stage=>{
            if(phase=="prepared"&&stage=="update-prepared"||phase=="committed"&&stage=="update-committed"||
                phase=="written"&&stage=="update-written"&&++writes==3)Environment.Exit(83);
        };
        engine.UpdatePackage(Safe.In(folder,"new.zip"),NewKey,baseline,upgrade,null,null);
        throw new IOException("O ponto de interrupção não foi executado.");
    }
    static string Zip(string folder,string name,Dictionary<string,string> files) {
        string path=Safe.In(folder,name);
        using(var zip=ZipFile.Open(path,ZipArchiveMode.Create))foreach(var pair in files)
            using(var writer=new StreamWriter(zip.CreateEntry(pair.Key).Open()))writer.Write(pair.Value);
        return path;
    }
    static void Throws(Action action) {try{action();}catch(IOException){return;}throw new IOException("A operação deveria ter preservado a instalação e recusado a alteração.");}
}
