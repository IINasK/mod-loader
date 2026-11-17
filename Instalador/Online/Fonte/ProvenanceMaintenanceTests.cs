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
        Safe.WriteJson(Path.Combine(root,"result.json"),new{Passed=true,Checks=checks,RealGameTouched=false,
            NetworkUsed=false,AutomaticGeneratedFileDeletion=false,ExtraPayloadCopies=0,RestoredBackupBytesReleased=8192});
        return checks.Count;
    }

    static string Zip(string root,string name,string entry,string content){
        string path=Path.Combine(root,name);
        using(var archive=ZipFile.Open(path,ZipArchiveMode.Create))
        using(var writer=new StreamWriter(archive.CreateEntry(entry).Open()))writer.Write(content);
        return path;
    }
}
