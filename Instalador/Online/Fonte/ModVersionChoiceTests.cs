using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows.Forms;

internal static class ModVersionChoiceTests {
    internal static int Run(string parent){
        string priorTheme=GeneralStyle.Dark?"dark":GeneralStyle.RedTheme?"light-red":"light",priorLanguage=GeneralStyle.Language;bool priorAnimations=GeneralStyle.AnimationsEnabled;
        try{
        string root=OnlineTests.NestedRoot(parent,"versions-");Directory.CreateDirectory(root);var tests=new List<string>();
        Action<bool,string> check=(ok,text)=>{if(!ok)throw new IOException(text);tests.Add(text);};
        Action<Action,string> reject=(action,text)=>{try{action();}catch(IOException){tests.Add(text);return;}throw new IOException("Not rejected: "+text);};
        string target=Path.Combine(root,"game"),data=Path.Combine(root,"data");Directory.CreateDirectory(target);
        string file=Path.Combine(target,"mod.txt");File.WriteAllText(file,"old");
        string zip=Zip(root,"incoming.zip",new Dictionary<string,string>{{"mod.txt","new"},{"second.txt","second"}});
        var engine=new GeneralModInstaller(target,data);int prompts=0;
        reject(()=>engine.Install(zip,"key",null),"No default replacement without an explicit decision.");
        reject(()=>engine.Install(zip,"key",null,d=>{prompts++;return false;}),"Keeping current defers the whole custom item.");
        check(prompts==1&&File.ReadAllText(file)=="old"&&!File.Exists(Path.Combine(target,"second.txt")),"Keeping current makes no partial writes.");
        engine.Install(zip,"key",null,d=>{prompts++;check(d.Current.Contains("identif")&&d.Incoming=="incoming.zip","Unknown version is not invented; incoming package is identified.");return true;});
        check(prompts==2&&File.ReadAllText(file)=="new"&&engine.Status("key")==GeneralModInstaller.Installed,"Explicit choice replaces and registers the custom version.");
        engine.Install(zip,"key",null,d=>{throw new IOException("Unexpected prompt");});check(File.ReadAllText(file)=="new","Identical installed files do not ask again.");
        string expanded=Zip(root,"expanded.zip",new Dictionary<string,string>{{"new/path.txt","different structure"}});
        reject(()=>engine.Install(expanded,"key",null,d=>{check(!d.CanReplace,"Changed file tree requires removing the previous version first.");return false;}),"Changed package structure is preserved instead of mixing versions.");
        check(File.ReadAllText(file)=="new"&&!Directory.Exists(Path.Combine(target,"new")),"Declining an unsupported structural replacement preserves the installed tree.");
        engine.Remove("key");check(File.ReadAllText(file)=="old"&&!File.Exists(Path.Combine(target,"second.txt")),"Uninstall restores the prior file and removes only newly installed files.");
        reject(()=>engine.Install(zip,"race",null,d=>{File.WriteAllText(file,"user-edit");return true;}),"File changed while choosing is rejected.");
        check(File.ReadAllText(file)=="user-edit"&&!File.Exists(Path.Combine(target,"second.txt")),"User change during prompt is preserved.");
        File.WriteAllText(file,"old");string rollbackData=Path.Combine(root,"rollback-data");var rollback=new GeneralModInstaller(target,rollbackData);
        string receiptDir=null;
        reject(()=>rollback.Install(zip,"rollback",message=>{if(receiptDir==null){receiptDir=Directory.GetDirectories(rollbackData).Single();System.Security.Cryptography.SHA256 sha=System.Security.Cryptography.SHA256.Create();using(sha){string hash=Safe.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes("rollback"))).Substring(0,32);Directory.CreateDirectory(Path.Combine(receiptDir,hash+".json"));}}},d=>true),"Receipt write failure rolls back approved replacement.");
        check(File.ReadAllText(file)=="old"&&!File.Exists(Path.Combine(target,"second.txt")),"Rollback restores current version without leaving new files.");
        var catalog=new FixedCatalog{Packages=new List<FixedMod>()};string pack=Pack(root,catalog);
        var snapshots=new InstalledSnapshotInstaller(target,Path.Combine(root,"snapshots"),catalog);
        reject(()=>snapshots.Install(pack,Safe.Hash(pack),null),"Received pack never replaces conflicts silently.");
        int packPrompts=0;
        reject(()=>snapshots.Install(pack,Safe.Hash(pack),null,-1,d=>{packPrompts++;return false;}),"Received pack can keep the current installation.");
        check(packPrompts==1&&File.ReadAllText(file)=="old"&&!File.Exists(Path.Combine(target,"second.txt")),"Declining received version writes no pack files.");
        reject(()=>snapshots.Install(pack,Safe.Hash(pack),null,1,d=>true),"Received pack copy failure triggers rollback after approval.");
        check(File.ReadAllText(file)=="old"&&!File.Exists(Path.Combine(target,"second.txt")),"Approved received version rollback preserves old files.");
        string id=snapshots.Install(pack,Safe.Hash(pack),null,-1,d=>{packPrompts++;return true;});
        check(packPrompts==2&&File.ReadAllText(file)=="pack","Received pack uses the chosen version.");
        snapshots.Remove(id,new[]{"custom/default"},null);check(File.ReadAllText(file)=="old"&&!File.Exists(Path.Combine(target,"second.txt")),"Removing received replacement restores the previous installation.");
        File.WriteAllText(Path.Combine(target,"second.txt"),"second-old");
        int grouped=0;reject(()=>snapshots.Install(pack,Safe.Hash(pack),null,-1,d=>{grouped++;return false;}),"Multiple conflicting files in a mod can be kept together.");
        check(grouped==1&&File.ReadAllText(file)=="old"&&File.ReadAllText(Path.Combine(target,"second.txt"))=="second-old","Pack requests one choice per mod group before copying any files.");
        File.Delete(Path.Combine(target,"second.txt"));
        reject(()=>snapshots.Install(pack,Safe.Hash(pack),null,-1,d=>{File.WriteAllText(file,"changed-during-choice");return true;}),"Pack rejects destination mutation during choice.");
        check(File.ReadAllText(file)=="changed-during-choice"&&!File.Exists(Path.Combine(target,"second.txt")),"Pack preserves edits made during review.");
        var protectedCatalog=new FixedCatalog{Packages=new List<FixedMod>{new FixedMod{Id="Protected",Name="Protected",Immutable=true,Version="1.0",Files=new List<FixedFile>{new FixedFile{Path="red4ext/RED4ext.dll"}}}}};
        string protectedPath=Safe.In(target,"red4ext/RED4ext.dll");Directory.CreateDirectory(Path.GetDirectoryName(protectedPath));File.WriteAllText(protectedPath,"protected");
        string protectedPack=Pack(root,protectedCatalog,new Dictionary<string,string>{{"red4ext/RED4ext.dll","incoming"}},"protected.zip");
        var protectedInstaller=new InstalledSnapshotInstaller(target,Path.Combine(root,"protected-data"),protectedCatalog);
        reject(()=>protectedInstaller.Install(protectedPack,Safe.Hash(protectedPack),null,-1,d=>{check(!d.CanReplace,"Protected base cannot be offered as replaceable.");return true;}),"Pack cannot override the protected base even with affirmative callback.");
        check(File.ReadAllText(protectedPath)=="protected","Protected base remains intact.");
        string structuralTarget=Path.Combine(root,"structural-target");Directory.CreateDirectory(structuralTarget);var structural=new InstalledSnapshotInstaller(structuralTarget,Path.Combine(root,"structural-data"),catalog);
        string oldPack=Pack(root,catalog,new Dictionary<string,string>{{"old-code.txt","old"},{"mod.txt","old"}},"old-tree.zip");structural.Install(oldPack,Safe.Hash(oldPack),null);
        reject(()=>structural.Install(pack,Safe.Hash(pack),null,-1,d=>{check(!d.CanReplace,"Pack does not mix an owned old file tree with a new version.");return true;}),"Known obsolete code requires explicit removal first.");
        check(File.ReadAllText(Path.Combine(structuralTarget,"old-code.txt"))=="old"&&File.ReadAllText(Path.Combine(structuralTarget,"mod.txt"))=="old","Unsupported structural pack change preserves both old files.");
        using(var service=new P2PService(Path.Combine(root,"p2p-data"),Path.Combine(root,"p2p-transport"),true)){
            service.EnableInstalledReceive(catalog,Path.Combine(root,"p2p-receipts"));int choices=0;service.ChooseVersion=d=>{choices++;return false;};
            var session=new P2PSession{Id="ml-"+Guid.NewGuid().ToString("N"),Name="Conflict pack",Hash=Safe.Hash(pack),Size=new FileInfo(pack).Length,Mode="installed-zip",Destination=target,AutoInstall=true};
            string folder=service.Folder(session);Directory.CreateDirectory(folder);File.Copy(pack,Path.Combine(folder,"pack.zip"));
            typeof(P2PService).GetMethod("ProcessInstalledLocked",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(service,new object[]{session});
            check(System.Threading.SpinWait.SpinUntil(()=>session.Stage==null,10000)&&choices==1&&session.Phase=="retry"&&!String.IsNullOrEmpty(session.Error),"P2P receive worker forwards the version choice and records the deferred installation.");
            check(!session.Completed&&File.Exists(Path.Combine(folder,"pack.zip"))&&File.ReadAllText(file)=="changed-during-choice","Keeping current in P2P preserves payload for retry and never marks the pack installed.");
        }
        string binary=GeneralAutomaticUpdateTests.MakeExe(root,"version.dll","1.2.3","Fixture");check(ModVersionDifference.FileVersion(binary)=="1.2.3.0","Readable binary version is displayed without executing the file.");
        var blocked=new ModVersionDifference{Name="Protected",CanReplace=false,Detail="protected"};reject(()=>blocked.Require(d=>true),"Explicit choice cannot bypass protected replacement guard.");
        foreach(string language in new[]{"pt","en"})foreach(string theme in new[]{"light","light-red","dark"}){
            GeneralStyle.UseTheme(theme);GeneralStyle.Language=language;
            using(var dialog=new GeneralVersionChoiceDialog(new ModVersionDifference{Name="Mod",Current="1.0",Incoming="2.0",Detail="test"})){dialog.CreateControl();dialog.PerformLayout();var keep=dialog.Controls.Find("KeepInstalledVersion",true).OfType<Button>().Single();var replace=dialog.Controls.Find("UseIncomingVersion",true).OfType<Button>().Single();check(keep.Enabled&&replace.Enabled&&dialog.AcceptButton==keep,"Safe default and explicit choice in "+language+"/"+theme);}
        }
        GeneralStyle.Language="pt";GeneralStyle.UseTheme("light-red");GeneralModWindow.RunVersionQueueTests(root,check);
        check(GeneralModInstallerTests.Run(root)==11,"Existing generic installation/removal regression checks pass.");
        InstalledSnapshotInstallerTests.Run(root);check(true,"Existing received pack installation/removal regression checks pass.");
        Safe.WriteJson(Path.Combine(root,"resultado-version-choice.json"),new{Passed=true,Tests=tests,RealGameTouched=false,NetworkUsed=false});return tests.Count;
        }finally{GeneralStyle.UseTheme(priorTheme);GeneralStyle.Language=priorLanguage;GeneralStyle.AnimationsEnabled=priorAnimations;}
    }
    static string Zip(string root,string name,Dictionary<string,string> files){string path=Path.Combine(root,name);using(var zip=ZipFile.Open(path,ZipArchiveMode.Create))foreach(var file in files)using(var writer=new StreamWriter(zip.CreateEntry(file.Key).Open(),new UTF8Encoding(false)))writer.Write(file.Value);return path;}
    static string Pack(string root,FixedCatalog catalog,Dictionary<string,string> files=null,string name="pack.zip"){
        var m=new InstalledSnapshotManifest{Name="test",Game="generic"};m.List.Mods.Add(new GeneralModEntry{Id="custom",Name="Custom",DefaultVariant="default",Variants=new List<GeneralModVariant>{new GeneralModVariant{Id="default",Name="Main",Sources=new List<string>{"installed://custom/default"}}}});
        var bytes=new Dictionary<string,byte[]>();foreach(var p in files??new Dictionary<string,string>{{"mod.txt","pack"},{"second.txt","second"}}){byte[] b=Encoding.UTF8.GetBytes(p.Value);string h=InstalledSnapshot.Hash(b);bytes[h]=b;m.Files.Add(new InstalledSnapshotFile{Path=p.Key,Kind="file",Sha256=h,Size=b.Length,Owners=new List<string>{"custom/default"}});}
        InstalledSnapshot.Validate(m,catalog);string path=Path.Combine(root,name);using(var zip=ZipFile.Open(path,ZipArchiveMode.Create)){using(var writer=new StreamWriter(zip.CreateEntry("manifest.json").Open(),new UTF8Encoding(false)))writer.Write(Safe.Json.Serialize(m));foreach(var b in bytes)using(var stream=zip.CreateEntry("files/"+b.Key).Open())stream.Write(b.Value,0,b.Value.Length);}return path;
    }
}
internal sealed partial class GeneralModWindow {
    internal static void RunVersionQueueTests(string parent,Action<bool,string> check){
        string root=OnlineTests.NestedRoot(parent,"queue-versions-");Directory.CreateDirectory(root);string destination=Path.Combine(root,"game"),downloads=Path.Combine(root,"downloads");Directory.CreateDirectory(destination);Directory.CreateDirectory(downloads);File.WriteAllText(Path.Combine(destination,"item.txt"),"old");
        string source=TestZip(root,"custom.zip","item.txt","new");var entry=NewEntry("Choose version",source);var state=new GeneralModState{Destination=destination,DownloadFolder=downloads,List=new GeneralModList()};state.List.Mods.Add(entry);
        TestForm(FixedData.Load(),state,Path.Combine(root,"data"),root,async form=>{
            int prompts=0;form.suppressQueueErrors=true;form.testVersionChoice=d=>{prompts++;return false;};var work=form.MakeWork(entry);await form.RunWork(work,destination,false);
            check(prompts==1&&form.statuses[work.Key]=="Versão mantida"&&File.ReadAllText(Path.Combine(destination,"item.txt"))=="old","Queue keep decision is visible and never counted as installed.");
            form.testVersionChoice=d=>{prompts++;return true;};await form.RunWork(form.MakeWork(entry),destination,false);check(prompts==2&&form.statuses[work.Key]=="Instalado"&&File.ReadAllText(Path.Combine(destination,"item.txt"))=="new","Queue replacement waits for explicit version choice and verifies final status.");
            File.Delete(source);TestZip(root,"custom.zip","item.txt","version 3");form.testVersionChoice=d=>{prompts++;return false;};await form.RunWork(form.MakeWork(entry),destination,false);
            check(form.Status(form.MakeWork(entry),destination)=="Versão diferente","Scan identifies a changed source instead of claiming the old version matches.");
            check(prompts==3&&File.ReadAllText(Path.Combine(destination,"item.txt"))=="new"&&form.statuses[work.Key]=="Versão mantida","Changed archive at same source path is detected despite the old installed receipt.");
        });
        string knownRoot=OnlineTests.Fixture(root,"known-game");var catalog=FixedData.Load();catalog.GameFileVersion="2.5.0.0";
        string old="old content",fresh="new content";string path="r6/tweaks/version-choice-test.yaml";Directory.CreateDirectory(Path.GetDirectoryName(Safe.In(knownRoot,path)));File.WriteAllText(Safe.In(knownRoot,path),old,new UTF8Encoding(false));
        var recipe=new FixedMod{Id="version-choice-test",Name="Version dependency",Version="2.0",GameVersion="2.5",Dependencies=new string[0],Assets=new List<FixedAsset>{new FixedAsset{Id="test",ModId=1,FileId=1,Name="fixture.zip",Sha256=TestHash(fresh),Size=1,Format="zip",Url="https://www.nexusmods.com/cyberpunk2077/mods/1?tab=files&file_id=1"}},Files=new List<FixedFile>{new FixedFile{Path=path,Asset="test",Entry=path,Sha256=TestHash(fresh),Size=Encoding.UTF8.GetByteCount(fresh),Patch=new FixedTextPatch{SourceSha256=TestHash(old),SourceSize=Encoding.UTF8.GetByteCount(old)}}}};
        catalog.Packages.Add(recipe);var known=GeneralModLibrary.Seed(catalog,new ModImportItem[0]).Mods.Single(m=>m.Id==recipe.Id);var knownState=new GeneralModState{Destination=knownRoot,DownloadFolder=downloads,List=new GeneralModList()};known.Category=null;knownState.List.Mods.Add(known);
        TestForm(catalog,knownState,Path.Combine(root,"known-data"),root,async form=>{
            int prompts=0;form.testVersionChoice=d=>{prompts++;check(d.CanReplace&&d.Incoming=="2.0","Recognized previous version offers the requested catalog version.");return false;};
            var work=form.MakeWork(known);await form.RunWork(work,knownRoot,false);check(prompts==1&&form.statuses[work.Key]=="Versão mantida"&&File.ReadAllText(Safe.In(knownRoot,path))==old,"Known-version queue preserves the installed version when declined.");
            File.WriteAllText(Safe.In(knownRoot,path),"unknown edit",new UTF8Encoding(false));form.testVersionChoice=d=>{prompts++;check(!d.CanReplace,"Unrecognized conflicting code is not silently made replaceable.");return false;};
            await form.RunWork(form.MakeWork(known),knownRoot,false);check(prompts==2&&File.ReadAllText(Safe.In(knownRoot,path))=="unknown edit","Unknown code conflict asks and preserves the existing file.");
        });
    }
}