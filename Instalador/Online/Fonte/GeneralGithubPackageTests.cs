using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Threading.Tasks;

internal static class GeneralGithubPackageTests {
    internal const string Repository="example/any-game-mod";
    internal static void LiveMetadata(string parent) {
        string path=Safe.In(parent,"github-live-metadata.json");
        try {
            var package=GeneralGithubUpdatePackage.Read("https://github.com/jac3km4/redscript/releases/latest").GetAwaiter().GetResult();
            Safe.WriteJson(path,new{Passed=true,Source=package.Source,Version=package.Info.Version,MetadataOnly=true,Downloaded=false,Installed=false});
        }catch(SourcePackageException ex){Safe.WriteJson(path,new{Passed=false,State=ex.State,MetadataOnly=true,Downloaded=false,Installed=false});}
    }
    internal static int Run(string parent) {
        string root=OnlineTests.NestedRoot(parent,"github-");Directory.CreateDirectory(root);
        var checks=new List<string>();Action<bool,string> check=(ok,text)=>{if(!ok)throw new IOException(text);checks.Add(text);};
        try {
            foreach(string source in new[]{"https://github.com/"+Repository,"https://github.com/"+Repository+"/releases",
                "https://github.com/"+Repository+"/releases/latest","https://github.com/"+Repository+"/releases/tag/v1.0",
                "https://github.com/"+Repository+"?tab=readme-ov-file"})
                check(GithubModUpdateSession.Supports(source),"Public repository and release pages are recognized: "+source);
            foreach(string source in new[]{"http://github.com/"+Repository,"https://github.com.evil.example/"+Repository,
                "https://github.com@evil.example/"+Repository,"https://github.com/"+Repository+"/blob/main/mod.zip",
                "https://raw.githubusercontent.com/"+Repository+"/main/mod.zip","https://github.com/"+Repository+"/releases/download/v1.0/%2e%2e%5cmod.zip"})
                check(!GithubModUpdateSession.Supports(source),"Unverified GitHub source shapes are rejected: "+source);
            string archive=GeneralModWindow.GithubFixtureZip(root,"mod-v1.0-windows.zip","windows");
            string other=GeneralModWindow.GithubFixtureZip(root,"mod-v1.0-linux.zip","linux");
            var release=Release("v1.0",archive,other);
            string endpoint=null;
            var page=GeneralGithubUpdatePackage.Read("https://github.com/"+Repository+"/releases/tag/v1.0",fetch:uri=>{endpoint=uri.AbsoluteUri;return Safe.Json.Serialize(release);}).GetAwaiter().GetResult();
            check(endpoint=="https://api.github.com/repos/"+Repository+"/releases/tags/v1.0","A tagged release uses its exact public API endpoint.");
            var verified=page.Verify(archive).GetAwaiter().GetResult();
            check(verified.Sha256==Safe.Hash(archive)&&verified.SourceUrl==Url("v1.0",Path.GetFileName(archive))&&
                verified.Info.GithubChecked&&!verified.Info.NexusChecked,"Hash verification identifies the chosen archive without pretending it came from Nexus.");
            var renamed=Safe.In(root,"renamed.zip");File.Copy(archive,renamed);
            check(page.Verify(renamed).GetAwaiter().GetResult().SourceUrl==verified.SourceUrl,"A renamed archive is identified by the published digest rather than its local name.");
            string wrong=Safe.In(root,"wrong.zip");byte[] changed=File.ReadAllBytes(archive);changed[changed.Length/2]^=1;File.WriteAllBytes(wrong,changed);
            Reject(()=>page.Verify(wrong).GetAwaiter().GetResult(),"wrong-file",check,"A same-sized modified archive fails before installation.");
            Reject(()=>GeneralGithubUpdatePackage.Read(verified.SourceUrl,fetch:_=>Safe.Json.Serialize(Release("v2.0",archive))).GetAwaiter().GetResult(),"unavailable",check,"Metadata for a different tag cannot replace the requested release.");
            var foreign=Release("v1.0",archive);foreign.assets[0].browser_download_url="https://github.com/another/repo/releases/download/v1.0/"+Path.GetFileName(archive);
            Reject(()=>GeneralGithubUpdatePackage.Read(verified.SourceUrl,fetch:_=>Safe.Json.Serialize(foreign)).GetAwaiter().GetResult(),"unavailable",check,"A release cannot redirect asset identity to another repository.");
            var noDigest=Release("v1.0",archive);noDigest.assets[0].digest=null;
            Reject(()=>GeneralGithubUpdatePackage.Read(verified.SourceUrl,fetch:_=>Safe.Json.Serialize(noDigest)).GetAwaiter().GetResult(),"unverified",check,"A release without a published SHA-256 cannot silently approve an automatic replacement.");
            var draft=Release("v1.0",archive);draft.draft=true;
            Reject(()=>GeneralGithubUpdatePackage.Read(verified.SourceUrl,fetch:_=>Safe.Json.Serialize(draft)).GetAwaiter().GetResult(),"unavailable",check,"Draft releases cannot be installed as published packages.");
            var preview=Release("v1.0",archive);preview.prerelease=true;
            Reject(()=>GeneralGithubUpdatePackage.Read("https://github.com/"+Repository,fetch:_=>Safe.Json.Serialize(preview)).GetAwaiter().GetResult(),"unavailable",check,"An unpinned repository does not silently opt into a prerelease.");
            check(GeneralGithubUpdatePackage.Read(verified.SourceUrl,fetch:_=>Safe.Json.Serialize(preview)).Result.Verify(archive).Result.SourceUrl==verified.SourceUrl,
                "An explicitly registered prerelease asset remains installable without opting stable mods into previews.");
            var next=Release("v2.0",archive,other);
            foreach(var asset in next.assets){asset.name=asset.name.Replace("v1.0","v2.0");asset.browser_download_url=Url("v2.0",asset.name);}
            check(GeneralGithubUpdatePackage.Successor(GeneralGithubLink.Parse(verified.SourceUrl),next)==Url("v2.0","mod-v2.0-windows.zip"),
                "A versioned asset preserves its platform when matching the next published archive.");
            next.assets=next.assets.Concat(new[]{new GeneralUpdateChecker.Asset{name="mod-v1.0-windows.zip",state="uploaded",size=new FileInfo(archive).Length,
                digest="sha256:"+Safe.Hash(archive),browser_download_url=Url("v2.0","mod-v1.0-windows.zip")}}).ToArray();
            check(GeneralGithubUpdatePackage.Successor(GeneralGithubLink.Parse(verified.SourceUrl),next)==null,"Competing successor filenames require review instead of choosing the first asset.");
            var partialVersion=Release("v2.0",archive);partialVersion.assets[0].name="mod-v2.0.1-windows.zip";
            partialVersion.assets[0].browser_download_url=Url("v2.0",partialVersion.assets[0].name);
            check(GeneralGithubUpdatePackage.Successor(GeneralGithubLink.Parse(Url("v1.0","mod-v1.0.1-windows.zip")),partialVersion)==null,
                "A release version cannot be substituted inside a longer build version to guess an asset family.");
            var repository=new GithubModUpdateSession(_=>Safe.Json.Serialize(release));
            check(repository.Check("https://github.com/"+Repository,()=>true).Result.State=="choose-file","A repository page alone does not fabricate the installed version.");
            var saved=verified.Info.SavedCopy();saved.GithubAssetUrl="https://evil.example/mod.zip";
            check(!saved.SavedCopy().GithubChecked&&saved.SavedCopy().GithubAssetUrl==null,"Portable metadata never exports a verified GitHub label for an unrelated URL.");
            bool canceled=false;try{page.Verify(archive,()=>false).GetAwaiter().GetResult();}catch(OperationCanceledException){canceled=true;}
            check(canceled,"Cancellation stops verification without accepting a package.");
            GeneralGithubAutomaticTests.Run(root,check);
            GeneralModWindow.RunGithubMetadataTests(root,check);
            GeneralModWindow.RunNexusMirrorWindowTests(root,check);
            GeneralModWindow.RunGithubWindowTests(root,check);
            GeneralModWindow.RunBatchTests(root,check);
            GeneralModWindow.RunManualUpdateLinkTest(root,check);
            Safe.WriteJson(Safe.In(root,"resultado-github-packages.json"),new{Passed=true,Checks=checks,RealGameTouched=false,NetworkUsed=false});
            return checks.Count;
        }catch(Exception ex){Safe.WriteJson(Safe.In(root,"resultado-github-packages.json"),new{Passed=false,Checks=checks,Error=ex.ToString()});throw;}
    }
    internal static string Url(string version,string file){return "https://github.com/"+Repository+"/releases/download/"+version+"/"+Uri.EscapeDataString(file);}
    internal static GeneralUpdateChecker.Release Release(string version,params string[] paths){return new GeneralUpdateChecker.Release{tag_name=version,
        assets=paths.Select(path=>new GeneralUpdateChecker.Asset{name=Path.GetFileName(path),state="uploaded",size=new FileInfo(path).Length,
            digest="sha256:"+Safe.Hash(path),browser_download_url=Url(version,Path.GetFileName(path))}).ToArray()};}
    static void Reject(Action action,string state,Action<bool,string> check,string label){
        try{action();}catch(SourcePackageException ex){check(ex.State==state,label);return;}throw new IOException(label);
    }
}

internal sealed partial class GeneralModWindow {
    internal static string GithubFixtureZip(string root,string name,string content){return SourceUpdateZip(root,name,
        new Dictionary<string,string>{{"mods/fixture/main.txt",content},{"mods/fixture/anchor.txt","anchor"}});}
    internal static void RunGithubWindowTests(string root,Action<bool,string> check) {
        foreach(string scenario in new[]{"update","automatic","automatic-cancel","cancel","wrong-file","missing-digest","manual-initial"}) {
            string parent=Directory.CreateDirectory(Safe.In(root,scenario)).FullName;
            string game=Directory.CreateDirectory(Safe.In(parent,"game")).FullName,downloads=Directory.CreateDirectory(Safe.In(parent,"downloads")).FullName;
            File.WriteAllText(Safe.In(game,".fixture-only"),"fixture");
            string old=SourceUpdateZip(downloads,"mod-v1.0-windows.zip",new Dictionary<string,string>{{"mods/fixture/main.txt","old"},{"mods/fixture/obsolete.txt","obsolete"},{"mods/fixture/anchor.txt","anchor"}});
            string next=SourceUpdateZip(parent,"mod-v2.0-windows.zip",new Dictionary<string,string>{{"mods/fixture/main.txt","new"},{"mods/fixture/added.txt","added"},{"mods/fixture/anchor.txt","anchor"}});
            string source="https://github.com/"+GeneralGithubPackageTests.Repository;
            var entry=NewEntry("GitHub fixture",source);entry.Id="github-fixture";
            var state=new GeneralModState{Destination=game,DownloadFolder=downloads,List=new GeneralModList{Mods=new List<GeneralModEntry>{entry}}};
            if(scenario=="manual-initial") {
                state.ResolvedDownloads[P2PPack.Slot(entry,entry.Variants.Single(),0)]=old;
                entry.Variants.Single().SourceInfo[source]=new GeneralSourceInfo{Name="Old metadata",Version="v0.9",GithubChecked=true,
                    GithubAssetUrl=GeneralGithubPackageTests.Url("v0.9","mod-v0.9-windows.zip")};
            }
            var catalog=new FixedCatalog{Packages=new List<FixedMod>()};bool newer=false;
            Func<Uri,string> fetch=uri=>{
                bool latest=uri.AbsolutePath.EndsWith("/latest",StringComparison.Ordinal);
                bool second=latest?newer:uri.AbsolutePath.EndsWith("/v2.0",StringComparison.Ordinal);
                var release=GeneralGithubPackageTests.Release(second?"v2.0":"v1.0",second?next:old);
                if(scenario=="missing-digest"&&second)release.assets[0].digest=null;
                if(scenario=="manual-initial"&&!second)release.assets[0].digest=null;
                return Safe.Json.Serialize(release);
            };
            TestForm(catalog,state,Safe.In(parent,"data"),parent,async form=>{
                int nexusCalls=0,opened=0;string failure=null;
                form.testModUpdateSession=new NexusUpdateSession(null,(GeneralNexusLink link,string key,out System.Net.HttpStatusCode status)=>{nexusCalls++;status=0;return null;});
                form.testGithubPackage=url=>GeneralGithubUpdatePackage.Read(url,fetch:fetch);
                form.launch=url=>opened++;form.testFailurePrompt=text=>{failure=text;return false;};
                await form.RunWork(form.MakeWork(entry),game,false);
                if(scenario=="manual-initial") {
                    var initial=form.MakeWork(form.state.List.Mods.Single());GeneralSourceInfo manual;
                    check(form.Generic(game).Status(initial.Receipt(0))==GeneralModInstaller.Installed&&
                        (!initial.Variant.SourceInfo.TryGetValue(source,out manual)||!manual.GithubChecked),
                        "A release without a digest retains ordinary explicit manual installation, without falsely recording a verified GitHub identity.");return;
                }
                var work=form.MakeWork(form.state.List.Mods.Single());var initialInfo=work.Variant.SourceInfo[source];
                check(File.ReadAllText(Safe.In(game,"mods/fixture/main.txt"))=="old"&&initialInfo.GithubChecked&&!initialInfo.NexusChecked&&
                    initialInfo.GithubAssetUrl==GeneralGithubPackageTests.Url("v1.0",Path.GetFileName(old)),"Initial repository-source installation records the exact verified GitHub asset: "+scenario);
                check(nexusCalls==0&&opened==0&&!form.NeedsNexusConnection(),"GitHub installation needs no Nexus key or browser connection: "+scenario);
                newer=true;form.testGithubUpdateSession=new GithubModUpdateSession(fetch);
                await form.Scan();await WaitForVersionCheck(form);
                string target=GeneralGithubPackageTests.Url("v2.0",Path.GetFileName(next));
                if(scenario=="missing-digest") {
                    check(form.SelectedUpdates().Count==0&&form.CountVersions().Review==1&&File.ReadAllText(Safe.In(game,"mods/fixture/main.txt"))=="old",
                        "A release lacking a digest is listed for review, without replacing files or showing a false automatic update.");return;
                }
                var job=form.SelectedUpdates().Single();
                check(job.UpdateTarget==target&&form.CountVersions().Ready==1,"A checked repository source can update its pinned asset through the ordinary scanner: "+scenario);
                if(scenario=="update")CaptureUi(form,Safe.In(parent,"github-ready.png"));
                if(scenario=="update")File.Copy(next,Safe.In(downloads,Path.GetFileName(next)));
                else if(scenario.StartsWith("automatic",StringComparison.Ordinal)) {
                    form.testSourceDownload=async (package,sourceCache,control,progress)=>{
                        check(sourceCache.SourcesOnly,"Automatic update retains only a source index in project data.");
                        if(scenario=="automatic-cancel"){
                            progress(new DownloadProgress{Bytes=1,Total=10,Status="Baixando"});
                            form.skipRequests.Add(job);await Task.Delay(150);control.Checkpoint();return null;
                        }
                        string original=Safe.In(downloads,"previously-downloaded.zip");File.Copy(next,original);
                        var github=await GeneralGithubUpdatePackage.Read(target,fetch:fetch);
                        sourceCache.Remember(github.DownloadAsset,original);
                        return await package.Download(sourceCache,control,progress);
                    };
                }
                else {
                    form.launch=url=>{
                        opened++;check(url==target,"The browser opens the GitHub successor, preserving the registered provider.");
                        if(scenario=="cancel")form.skipRequests.Add(job);
                        else{string wrong=Safe.In(parent,"wrong.zip");byte[] bytes=File.ReadAllBytes(next);bytes[bytes.Length/2]^=1;File.WriteAllBytes(wrong,bytes);form.association=wrong;}
                    };
                }
                await form.RunWork(job,game,false);
                var updated=form.MakeWork(form.state.List.Mods.Single());
                if(scenario!="update"&&scenario!="automatic") {
                    check(File.ReadAllText(Safe.In(game,"mods/fixture/main.txt"))=="old"&&File.Exists(Safe.In(game,"mods/fixture/obsolete.txt"))&&
                        !File.Exists(Safe.In(game,"mods/fixture/added.txt"))&&updated.Variant.Sources.Single()==source,
                        "Cancelling or supplying an incorrect GitHub download preserves the installed package and registered source: "+scenario);
                    if(scenario=="automatic-cancel")check(opened==0,"Cancelling automatic download never opens a browser as a side effect.");return;
                }
                if(scenario=="automatic")check(opened==0,"Clicking Update completes an automatic GitHub download/install without opening the browser.");
                check(form.statuses[updated.Key]=="Instalado"&&File.ReadAllText(Safe.In(game,"mods/fixture/main.txt"))=="new"&&
                    !File.Exists(Safe.In(game,"mods/fixture/obsolete.txt"))&&File.ReadAllText(Safe.In(game,"mods/fixture/added.txt"))=="added",
                    "A verified GitHub successor uses the transactional installer and removes only obsolete owned files. Failure="+failure);
                check(updated.Variant.Sources.Single()==target&&updated.Variant.SourceInfo[target].GithubChecked&&!updated.Variant.SourceInfo[target].NexusChecked&&
                    form.Generic(game).Status(updated.Receipt(0))==GeneralModInstaller.Installed&&File.Exists(old),
                    "The committed GitHub source/version/receipt persist while the user's original download is preserved.");
                await form.Scan();await WaitForVersionCheck(form);
                check(form.SelectedUpdates().Count==0&&form.CountVersions().Ready==0&&form.statuses[updated.Key]=="Instalado",
                    "A subsequent scan recognizes the installed GitHub release and does not offer it again.");
                CaptureUi(form,Safe.In(parent,"github-updated.png"));
            });
        }
    }
}
