using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

internal static class GeneralModFolderSourceTests {
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool CreateHardLink(string path,string existing,IntPtr security);
    internal static void Run(string parent){
        string root=OnlineTests.NestedRoot(parent,"folder-source-");Directory.CreateDirectory(root);
        var checks=new List<string>();Action<bool,string> check=(ok,label)=>{if(!ok)throw new IOException(label);checks.Add(label);};
        string original=Directory.CreateDirectory(Safe.In(root,"original")).FullName;
        Directory.CreateDirectory(Safe.In(original,"mods/fixture"));
        string first=Safe.In(original,"mods/fixture/main.txt"),second=Safe.In(original,"mods/fixture/settings.ini");
        File.WriteAllText(first,"original mod");File.WriteAllText(second,"enabled=true");
        string source=GeneralModFolderSource.Normalize(original),data=Safe.In(root,"support"),game=Directory.CreateDirectory(Safe.In(root,"game")).FullName;
        check(GeneralModFolderSource.IsSource(source)&&GeneralModDownload.IsLocalSource(source)&&!GeneralModDownload.IsArchivePath(source),"Folder references are explicit local sources and never pretend to be archives.");
        check(GeneralModLibrary.ValidateSource(original)==source,"An existing dropped folder becomes a canonical persistent folder reference.");
        check(GeneralArchivePreview.Load(source,null,null,false).Paths.SequenceEqual(new[]{"mods/fixture/main.txt","mods/fixture/settings.ini"}),"Folder preview lists its contents without an extra wrapper or an archive copy.");
        string key="folder-fixture/main/0/"+source;var installer=new GeneralModInstaller(game,Safe.In(data,"Instalacoes"));string firstHash;
        using(var package=GeneralModFolderSource.Prepare(source,data)){
            firstHash=Safe.Hash(package.Path);
            installer.Install(package.Path,key,_=>{});
            check(File.ReadAllText(Safe.In(game,"mods/fixture/main.txt"))=="original mod"&&installer.Status(key)==GeneralModInstaller.Installed,"An arbitrary game's folder installs through the existing verified receipt installer.");
            check(installer.ExportSnapshotFiles(key).Count==2&&GeneralModFolderSource.Matches(source,installer.ReadPackageRecord(key),Safe.Hash),"Installed-file snapshot export and source recognition use the same receipt without hashing a directory.");
        }
        check(!Directory.EnumerateDirectories(Safe.In(data,"FolderImports")).Any(),"Successful folder staging removes its owned temporary ZIP.");
        using(var package=GeneralModFolderSource.Prepare(source,data))check(Safe.Hash(package.Path)==firstHash,"The transient folder archive is deterministic for unchanged paths and content.");
        using(var active=GeneralModFolderSource.Prepare(source,data))using(var another=GeneralModFolderSource.Prepare(source,data))
            check(File.Exists(active.Path)&&File.Exists(another.Path),"Staging cleanup preserves another active folder capture.");
        string stale=Directory.CreateDirectory(Safe.In(data,"FolderImports/"+Guid.NewGuid().ToString("N"))).FullName;
        File.WriteAllText(Safe.In(stale,"owner.txt"),"ModLoader.FolderImport/1\n"+Path.GetFileName(stale));File.WriteAllText(Safe.In(stale,"lease"),"");File.WriteAllText(Safe.In(stale,"payload.zip"),"interrupted staging");
        string unknown=Directory.CreateDirectory(Safe.In(data,"FolderImports/unknown")).FullName;File.WriteAllText(Safe.In(unknown,"keep.txt"),"unrecognized");
        using(var package=GeneralModFolderSource.Prepare(source,data))check(!Directory.Exists(stale)&&File.Exists(Safe.In(unknown,"keep.txt")),"Recovery cleans a marked abandoned package while preserving unknown support files.");
        File.Delete(Safe.In(unknown,"keep.txt"));Directory.Delete(unknown);
        File.WriteAllText(second,"enabled=false");
        check(!GeneralModFolderSource.Matches(source,installer.ReadPackageRecord(key),Safe.Hash),"Changing an original folder file is detected independently of the installed copy.");
        File.WriteAllText(second,"enabled=true");
        bool cancelled=false;try{GeneralModFolderSource.Prepare(source,data,()=>{throw new OperationCanceledException();});}catch(OperationCanceledException){cancelled=true;}
        check(cancelled&&!Directory.EnumerateDirectories(Safe.In(data,"FolderImports")).Any(),"Cancelling before staging preserves the installed files and leaves no package.");
        bool changed=false;
        try{using(var package=GeneralModFolderSource.Prepare(source,data,null,_=>{File.WriteAllText(Safe.In(original,"new.txt"),"new");})) {}}
        catch(IOException){changed=true;}
        check(changed&&installer.Status(key)==GeneralModInstaller.Installed&&!Directory.EnumerateDirectories(Safe.In(data,"FolderImports")).Any(),"A file added during staging rejects the snapshot before installation and cleans only owned staging.");
        File.Delete(Safe.In(original,"new.txt"));
        bool writeBlocked=false;using(var package=GeneralModFolderSource.Prepare(source,data,null,_=>{try{File.WriteAllText(second,"unexpected");}catch(IOException){writeBlocked=true;}})){}
        check(writeBlocked&&File.ReadAllText(second)=="enabled=true","Read leases prevent an original file from being overwritten during folder capture.");
        bool renamed=false,renameBlocked=false,rebindRejected=false;
        string moved=Safe.In(root,"moved-original");
        try{
            using(var package=GeneralModFolderSource.Prepare(source,data,null,_=>{
                if(renamed||renameBlocked)return;
                try{Directory.Move(original,moved);}catch(IOException){renameBlocked=true;return;}
                renamed=true;
                foreach(string relative in new[]{"mods/fixture/main.txt","mods/fixture/settings.ini"}){
                    string replacement=Safe.In(original,relative);Directory.CreateDirectory(Path.GetDirectoryName(replacement));File.Copy(Safe.In(moved,relative),replacement);
                }
            })){}
        }catch(IOException){rebindRejected=true;}
        finally{if(renamed){Directory.Move(original,Safe.In(root,"rebound-copy"));Directory.Move(moved,original);}}
        check(renamed?rebindRejected:renameBlocked&&!rebindRejected,renamed?"A source rebound after an allowed ancestor rename is rejected by the final held-handle identity check.":"Windows blocks an ancestor rename while the original files have read leases.");
        bool wrongHandle=false;using(var input=new FileStream(first,FileMode.Open,FileAccess.Read,FileShare.Read))
            try{GeneralModFolderSource.ValidateHandle(input,second);}catch(IOException){wrongHandle=true;}
        check(wrongHandle,"A held file handle cannot be validated as another source path.");
        string linked=Directory.CreateDirectory(Safe.In(root,"linked")).FullName;
        check(CreateHardLink(Safe.In(linked,"hard.txt"),first,IntPtr.Zero),"The isolated fixture can create a native hard link.");
        bool linkRejected=false;try{GeneralModFolderSource.PreviewPaths(GeneralModFolderSource.Normalize(linked));}catch(IOException){linkRejected=true;}
        File.Delete(Safe.In(linked,"hard.txt"));
        check(linkRejected&&File.ReadAllText(first)=="original mod","Hardlinked source files are rejected without changing the original.");
        foreach(string unsafePath in new[]{"C:\\",@"\\server\share\mod\",@"C:\mods\..\unsafe\",@"C:\mods\CON\"}){
            bool rejected=false;try{GeneralModFolderSource.Normalize(unsafePath);}catch(IOException){rejected=true;}catch(ArgumentException){rejected=true;}
            check(rejected,"Unsafe folder reference is rejected: "+(unsafePath.Length<=3?"drive root":"invalid local path"));
        }
        string empty=Directory.CreateDirectory(Safe.In(root,"empty")).FullName;bool emptyRejected=false;
        try{GeneralModFolderSource.PreviewPaths(GeneralModFolderSource.Normalize(empty));}catch(IOException){emptyRejected=true;}
        check(emptyRejected,"Empty folders do not create a false successful mod installation.");
        installer.Remove(key);
        check(!File.Exists(Safe.In(game,"mods/fixture/main.txt"))&&File.ReadAllText(first)=="original mod"&&File.ReadAllText(second)=="enabled=true","Uninstall removes only installed owned files and preserves the original folder.");
        Directory.Move(original,Safe.In(root,"unavailable"));
        check(GeneralModLibrary.ValidateSource(source)==source,"The canonical source survives a missing original folder after restart.");
        GeneralModWindow.RunFolderSourceWindowTests(Safe.In(root,"window"),check);
        GeneralModWindow.RunFolderSourceCyberpunkTests(Safe.In(root,"cyberpunk"),check);
        Safe.WriteJson(Safe.In(root,"result.json"),new{Passed=true,Checks=checks,RealGameTouched=false,NetworkUsed=false});
    }
}

internal sealed partial class GeneralModWindow {
    internal static void RunFolderSourceCyberpunkTests(string root,Action<bool,string> check){
        Directory.CreateDirectory(root);string game=Directory.CreateDirectory(Safe.In(root,"g")).FullName;SourceUpdateGameExecutable(game);
        string original=Directory.CreateDirectory(Safe.In(root,"original/archive/pc/mod")).FullName;
        File.WriteAllText(Safe.In(original,"fixture.archive"),"folder fixture");original=Safe.In(root,"original");
        string blocked=Directory.CreateDirectory(Safe.In(root,"blocked/bin/x64")).FullName;
        File.WriteAllText(Safe.In(blocked,"Cyberpunk2077.exe"),"untrusted replacement");blocked=Safe.In(root,"blocked");
        var entry=NewEntry("Cyberpunk folder",GeneralModFolderSource.Normalize(original));
        var unsafeEntry=NewEntry("Protected folder",GeneralModFolderSource.Normalize(blocked));
        var state=new GeneralModState{Destination=game,List=new GeneralModList{Mods=new List<GeneralModEntry>{entry,unsafeEntry}}};
        TestForm(SourceUpdateCatalog(),state,Safe.In(root,"d"),root,async form=>{
            string executable=Safe.In(game,"bin/x64/Cyberpunk2077.exe"),before=Safe.Hash(executable);
            await form.Scan();var work=form.MakeWork(entry);await form.RunWork(work,game,false);
            check(form.Status(work,game)=="Instalado"&&form.state.CyberpunkImports.ContainsKey(work.Receipt(0)),"Cyberpunk folder sources use the existing protected imported receipt rather than bypassing its checks.");
            var manifest=InstalledSnapshot.Inspect(form.state,new[]{entry.Id},form.catalog,form.data,false);
            check(manifest.Files.Count==1&&manifest.Files.Single().Path=="archive/pc/mod/fixture.archive","Cyberpunk folder imports participate in the existing installed-file export.");
            var bad=form.MakeWork(unsafeEntry);await form.RunWork(bad,game,false);
            check(Safe.Hash(executable)==before&&!form.state.CyberpunkImports.ContainsKey(bad.Receipt(0))&&!form.Generic(game).HasSnapshotRecord(bad.Receipt(0)),"A folder cannot overwrite the protected game executable or acquire an unverified generic receipt.");
            check(!Directory.EnumerateDirectories(Safe.In(form.data,"FolderImports")).Any(),"A protected-folder failure also removes its owned staging archive.");
            form.RemoveManagedWorks(new List<Work>{work},game,new HashSet<string>());
            check(!File.Exists(Safe.In(game,"archive/pc/mod/fixture.archive"))&&File.Exists(Safe.In(original,"archive/pc/mod/fixture.archive")),"Cyberpunk folder removal preserves the original package folder.");
        });
    }
    internal static void RunFolderSourceWindowTests(string root,Action<bool,string> check){
        Directory.CreateDirectory(root);string game=Directory.CreateDirectory(Safe.In(root,"g")).FullName;
        File.WriteAllText(Safe.In(game,".fixture-only"),"test");
        string original=Directory.CreateDirectory(Safe.In(root,"original")).FullName;File.WriteAllText(Safe.In(original,"fixture.txt"),"folder fixture");
        var entry=NewEntry("Generic folder",GeneralModFolderSource.Normalize(original));
        entry.Variants[0].InstallRoutes=GeneralInstallRoutes.Parse("%GAME%/mods/test",game);
        var state=new GeneralModState{Destination=game,List=new GeneralModList{Mods=new List<GeneralModEntry>{entry}}};
        TestForm(FixedData.Load(),state,Safe.In(root,"d"),root,async form=>{
            await form.Scan();var work=form.MakeWork(entry);
            check(form.Status(work,game)=="Pronto para instalar"&&work.Local,"A local folder participates in scan and the existing local installation queue.");
            await form.RunWork(work,game,false);
            check(form.Status(work,game)=="Instalado"&&File.ReadAllText(Safe.In(game,"mods/test/fixture.txt"))=="folder fixture","The actual app installs folder contents using the configured route.");
            check(work.Variant.Sources.Single()==GeneralModFolderSource.Normalize(original)&&form.state.ResolvedDownloads.Count==0,"The app retains the original folder reference and never saves a transient ZIP reference.");
            await form.Scan();check(form.Status(work,game)=="Instalado","Rescanning recognizes folder installations using verified installed files.");
            File.WriteAllText(Safe.In(original,"fixture.txt"),"changed folder fixture");
            check(form.Status(work,game)=="Versão diferente","A scan distinguishes a changed folder source from its installed version.");
            File.WriteAllText(Safe.In(original,"fixture.txt"),"folder fixture");
            var manifest=InstalledSnapshot.Inspect(form.state,new[]{entry.Id},form.catalog,form.data,false);
            check(manifest.Files.Count==1&&manifest.List.Mods.Single().Variants.Single().Sources.All(GeneralModDownload.IsInstalledSource)&&!Safe.Json.Serialize(manifest).Contains(original),"P2P exports the installed receipt and portable identity without the original folder's private path.");
            bool legacyBlocked=false;
            try{P2PPack.Create(Safe.In(root,"legacy-share"),form.state,new[]{entry.Id},form.catalog,form.cache,_=>{});}
            catch(IOException ex){legacyBlocked=ex.Message.Contains("arquivos instalados");}
            check(legacyBlocked&&!Directory.Exists(Safe.In(root,"legacy-share")),
                "The legacy archive exporter cannot serialize a private folder source; its incomplete package is removed.");
            form.RemoveManagedWorks(new List<Work>{work},game,new HashSet<string>());
            check(!File.Exists(Safe.In(game,"mods/test/fixture.txt"))&&File.Exists(Safe.In(original,"fixture.txt")),"The app's ordinary removal path preserves the original folder.");
        });
    }
}
