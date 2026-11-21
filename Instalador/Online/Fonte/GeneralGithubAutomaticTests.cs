using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Collections.Generic;

internal static class GeneralGithubAutomaticTests {
    internal static void Run(string parent,Action<bool,string> check){
        string root=Directory.CreateDirectory(Safe.In(parent,"automatic-transport")).FullName;
        string archive=GeneralModWindow.GithubFixtureZip(root,"mod-v2.0.zip","second");
        var release=GeneralGithubPackageTests.Release("v2.0",archive);
        string source=GeneralGithubPackageTests.Url("v2.0",Path.GetFileName(archive));
        var package=GeneralGithubUpdatePackage.Read(source,fetch:_=>Safe.Json.Serialize(release)).Result;
        var unsigned=GeneralGithubPackageTests.Release("v2.0",archive);unsigned.assets[0].digest=null;
        var metadata=GeneralGithubUpdatePackage.ReadSourceInfo(source,_=>Safe.Json.Serialize(unsigned)).Result;
        check(metadata.Name==GeneralGithubPackageTests.Repository.Split('/').Last()&&metadata.Bytes==new FileInfo(archive).Length&&
            metadata.Version=="v2.0"&&!metadata.GithubChecked&&metadata.GithubAssetUrl==null,"API metadata is available without an asset digest but never pretends the archive is installed or verified.");
        string second=GeneralModWindow.GithubFixtureZip(root,"mod-v2.0-other.zip","second");
        var multi=GeneralGithubPackageTests.Release("v2.0",archive,second);
        var multiple=GeneralGithubUpdatePackage.ReadSourceInfo("https://github.com/"+GeneralGithubPackageTests.Repository,_=>Safe.Json.Serialize(multi)).Result;
        check(multiple.FileName==null&&multiple.Bytes==0&&multiple.Version==null&&multiple.ModVersion=="v2.0",
            "A multi-asset repository exposes release metadata without choosing a platform or package.");
        multi.assets[1].digest=null;
        check(GeneralGithubUpdatePackage.Read("https://github.com/"+GeneralGithubPackageTests.Repository,fetch:_=>Safe.Json.Serialize(multi)).Result.DownloadAsset==null,
            "One signed archive beside an unsigned alternative does not silently select the first candidate.");
        check(GeneralNexusGithubMirror.Repositories("[url=https://github.com/example/any-game-mod]Source[/url]").SequenceEqual(new[]{"https://github.com/example/any-game-mod"}),
            "Nexus author descriptions can declare a GitHub repository without a per-game mapping.");
        check(GeneralNexusGithubMirror.Repositories("https://github.com.evil.example/a/b http://github.com/a/b").Length==0,
            "Forged GitHub hosts and unencrypted discovery hints are ignored.");
        string downloads=Directory.CreateDirectory(Safe.In(root,"downloads")).FullName;
        var cache=new DownloadCache(Safe.In(root,"index"),()=>downloads);var asset=package.DownloadAsset;
        string collision=Safe.In(downloads,asset.Name);File.WriteAllText(collision,"user original");
        using(var control=new DownloadControl()) {
            string temporary=cache.DownloadTemporaryPath();File.Copy(archive,temporary);
            NexusClient.CompleteDownload(cache,asset,temporary,control,preserveFileName:true);
            check(!File.Exists(temporary)&&File.ReadAllText(collision)=="user original"&&cache.Contains(asset)&&cache.PathFor(asset)!=collision,
                "The shared transport preserves a user's same-name original and removes its owned temporary after commit.");
            check(Directory.GetFiles(cache.Root,"*",System.IO.SearchOption.AllDirectories).All(path=>Path.GetExtension(path)==".json"),
                "Automatic source downloads create no permanent package copy in the project cache.");
            temporary=cache.DownloadTemporaryPath();byte[] bad=File.ReadAllBytes(archive);bad[bad.Length/2]^=1;File.WriteAllBytes(temporary,bad);
            bool rejected=false;try{NexusClient.CompleteDownload(cache,asset,temporary,control);}catch(IOException){rejected=true;}
            check(rejected&&!File.Exists(temporary)&&cache.Contains(asset),"A wrong SHA-256 removes the owned partial and preserves the previously verified original.");
            temporary=cache.DownloadTemporaryPath();File.Copy(archive,temporary);
            rejected=false;try{NexusClient.CompleteDownload(cache,asset,temporary,control,_=>{throw new IOException("Identity rejected");});}catch(IOException){rejected=true;}
            check(rejected&&!File.Exists(temporary),"A provider identity rejection happens before commit and cleans the temporary archive.");
        }
        long bytes=asset.Size;
        string digest;using(var stream=File.OpenRead(archive))using(var md5=MD5.Create())digest=Safe.Hex(md5.ComputeHash(stream));
        string nexusUrl="https://www.nexusmods.com/stardewvalley/mods/42?tab=files&file_id=120";
        bool correct=true;string description="Source: https://github.com/"+GeneralGithubPackageTests.Repository;
        int hashRequests=0,githubRequests=0;
        GeneralNexusPackageRequest request=(Uri uri,string key,out HttpStatusCode status)=>{
            check(key=="fixture-key"&&uri.Host=="api.nexusmods.com","Nexus credentials remain confined to the Nexus API.");status=HttpStatusCode.OK;
            if(uri.AbsolutePath.EndsWith("/mods/42.json",StringComparison.Ordinal))return Safe.Json.Serialize(new{mod_id=42,description=description});
            if(uri.AbsolutePath.Contains("md5_search")){
                hashRequests++;if(!uri.AbsolutePath.EndsWith("/"+digest+".json",StringComparison.Ordinal))return "[]";
                return Safe.Json.Serialize(new[]{new{mod=new{domain_name="stardewvalley",mod_id=42},file_details=new{file_id=correct?120:121,category_id=1,size_in_bytes=bytes}}});
            }
            return Safe.Json.Serialize(new{file_id=120,category_id=1,name="Generic fixture",file_name=asset.Name,version="2.0",size_in_bytes=bytes});
        };
        var nexus=GeneralNexusUpdatePackage.Read(nexusUrl,"fixture-key",request).Result;
        Func<Uri,string> fetch=uri=>{githubRequests++;check(uri.Host=="api.github.com","GitHub release discovery uses the public GitHub API.");return Safe.Json.Serialize(release);};
        using(var control=new DownloadControl()){
            VerifiedModPackage verified;
            var previousContext=System.Threading.SynchronizationContext.Current;
            using(var uiContext=new WindowsFormsSynchronizationContext()){
                System.Threading.SynchronizationContext.SetSynchronizationContext(uiContext);
                try{
                    var pending=GeneralNexusGithubMirror.Download(nexus,cache,control,_=>{},fetch);
                    check(pending.Wait(5000),"Mirror discovery and verification complete without requiring the UI thread to pump messages.");
                    verified=pending.GetAwaiter().GetResult();
                }finally{System.Threading.SynchronizationContext.SetSynchronizationContext(previousContext);}
            }
            check(verified.SourceUrl==nexus.Link.Url&&verified.Info.NexusChecked&&!verified.Info.GithubChecked&&verified.Sha256==asset.Sha256&&hashRequests==1,
                "An author-linked mirror is accepted only after the Nexus hash lookup confirms the exact game/mod/file; the Nexus source is retained.");
            correct=false;bool rejected=false;
            try{GeneralNexusGithubMirror.Download(nexus,cache,control,_=>{},fetch).GetAwaiter().GetResult();}catch(SourcePackageException){rejected=true;}
            check(rejected,"Same version, size and GitHub digest cannot approve another Nexus variant.");correct=true;
            description+=" https://github.com/another/dependency";int before=githubRequests;
            check(GeneralNexusGithubMirror.Download(nexus,cache,control,_=>{},fetch).Result==null&&githubRequests==before,
                "Several author-linked repositories keep the Nexus download flow instead of guessing among mods and dependencies.");
            description="Mod without an explicit repository";int searches=0;
            var discovered=GeneralNexusGithubMirror.Resolve(nexus,"Generic fixture",new string[0],()=>true,uri=>{
                if(uri.AbsolutePath=="/search/repositories"){
                    searches++;return Safe.Json.Serialize(new{items=new[]{new{full_name=GeneralGithubPackageTests.Repository}}});
                }
                return Safe.Json.Serialize(release);
            }).GetAwaiter().GetResult();
            check(searches==1&&discovered!=null&&discovered.DownloadAsset.Sha256==asset.Sha256,"One repository search finds the release matching the selected size and version.");
            var limited=GeneralNexusUpdatePackage.Read(nexusUrl,"fixture-key",(Uri uri,string key,out HttpStatusCode status)=>{
                if(uri.AbsolutePath.Contains("md5_search")){status=(HttpStatusCode)429;return null;}
                return request(uri,key,out status);
            }).Result;
            string parked=null;
            try{GeneralNexusGithubMirror.DownloadConfirmed(limited,discovered,cache,control,_=>{},path=>parked=path).GetAwaiter().GetResult();}
            catch(NexusPackageException ex){check(ex.State=="rate-limit","Quota leaves the digest-checked archive awaiting Nexus identity.");}
            check(parked==cache.PathFor(asset)&&nexus.Verify(parked).Result.Sha256==asset.Sha256,"Retained bytes can be confirmed on retry without a new download.");
            string temporary=cache.DownloadTemporaryPath();File.Copy(archive,temporary);
            check(nexus.VerifyDownloaded(temporary).Result.Sha256==asset.Sha256,"Only an owned transport temporary can be checked before its archive filename is committed.");
            File.Delete(temporary);
            temporary=Safe.In(downloads,"foreign.part");File.Copy(archive,temporary);rejected=false;
            try{nexus.VerifyDownloaded(temporary).GetAwaiter().GetResult();}catch(SourcePackageException){rejected=true;}
            check(rejected&&File.Exists(temporary),"An arbitrary browser partial is not accepted or deleted as a Loader temporary.");
        }
        Safe.WriteJson(Safe.In(root,"result.json"),new{Passed=true,NetworkUsed=false,RealGameTouched=false});
    }
    internal static void LiveDownload(string parent){
        string root=Directory.CreateDirectory(Safe.In(parent,"live-github-download")).FullName;
        string downloads=Directory.CreateDirectory(Safe.In(root,"downloads")).FullName;
        var package=GeneralGithubUpdatePackage.Read("https://github.com/jac3km4/redscript/releases/download/v1.0.1/redscript-v1.0.1-windows.zip").GetAwaiter().GetResult();
        var cache=new DownloadCache(Safe.In(root,"index"),()=>downloads);
        using(var control=new DownloadControl()){
            var verified=package.Download(cache,control,_=>{}).GetAwaiter().GetResult();
            Safe.WriteJson(Safe.In(root,"result.json"),new{Passed=Safe.Hash(verified.Path)==verified.Sha256,Source=verified.SourceUrl,
                Bytes=verified.Bytes,Sha256=verified.Sha256,NetworkUsed=true,Installed=false,Executed=false,
                Partials=Directory.GetFiles(downloads,"*.part").Length,ProjectCachePackages=Directory.GetFiles(cache.Root,"*.zip").Length});
        }
    }
}

internal sealed partial class GeneralModWindow {
    internal static void RunNexusMirrorWindowTests(string parent,Action<bool,string> check){
        string root=Directory.CreateDirectory(Safe.In(parent,"nexus-mirror-window")).FullName;
        string oldZip,newZip;var state=SourceUpdateState(root,out oldZip,out newZip);
        var release=GeneralGithubPackageTests.Release("v2.0",newZip);
        release.assets[0].name="mod-v2.0.zip";release.assets[0].browser_download_url=GeneralGithubPackageTests.Url("v2.0","mod-v2.0.zip");
        TestForm(new FixedCatalog{Packages=new List<FixedMod>()},state,Safe.In(root,"d"),root,async form=>{
            var job=await SourceUpdateReady(form,root,oldZip);int opened=0,automatic=0;
            form.launch=_=>{opened++;form.skipRequests.Add(job);};
            form.testGithubMirrorFetch=_=>Safe.Json.Serialize(release);
            form.testUpdatePackage=target=>GeneralNexusUpdatePackage.Read(target,"fixture-key",(Uri uri,string key,out HttpStatusCode status)=>{
                status=HttpStatusCode.OK;
                if(uri.AbsolutePath.EndsWith("/mods/42.json",StringComparison.Ordinal))
                    return Safe.Json.Serialize(new{mod_id=42,description="Source https://github.com/"+GeneralGithubPackageTests.Repository});
                if(uri.AbsolutePath.Contains("md5_search"))return uri.AbsolutePath.EndsWith("/"+SourceUpdateMd5(newZip)+".json",StringComparison.Ordinal)
                    ?Safe.Json.Serialize(new[]{new{mod=new{mod_id=42,domain_name="stardewvalley"},file_details=new{file_id=11,category_id=1,size_in_bytes=new FileInfo(newZip).Length}}}):"[]";
                return Safe.Json.Serialize(new{file_id=11,category_id=1,name="Nexus mirror fixture",file_name="mod-v2.0.zip",version="2.0",size_in_bytes=new FileInfo(newZip).Length});
            });
            form.testSourceDownload=async (package,sourceCache,control,progress)=>{
                automatic++;
                form.Close();
                check(!form.IsDisposed&&!form.grid.Enabled&&form.selectedFooterAction==FooterAction.CancelUpdate&&form.footerAction.Enabled,
                    "The update keeps the screen locked with the footer cancel button available.");
                progress(new DownloadProgress{Status="Baixando",Bytes=40,Total=100});
                await Until(()=>form.currentProgress.Value==40);
                check(form.ModRows().Single().Cells["Status"].Value.ToString().Contains("40%")&&
                    form.totalProgress.Caption=="Mods atualizados: 0 / 1",
                    "Download percentage appears on the active mod and the right bar counts applied updates only.");
                var github=await GeneralGithubUpdatePackage.Read(release.assets[0].browser_download_url,fetch:form.testGithubMirrorFetch);
                string temp=sourceCache.DownloadTemporaryPath();File.Copy(newZip,temp);
                NexusClient.CompleteDownload(sourceCache,github.DownloadAsset,temp,control,preserveFileName:true);
                return await package.Download(sourceCache,control,progress);
            };
            using(var confirm=new Timer{Interval=25}){
                confirm.Tick+=(s,e)=>{
                    var dialog=Application.OpenForms.OfType<GeneralInstallConfirmation>().FirstOrDefault();
                    if(dialog!=null)dialog.DialogResult=DialogResult.OK;
                };
                confirm.Start();form.primaryAction.PerformClick();
                await Until(()=>automatic>0&&!form.working&&!form.scanning);confirm.Stop();
            }
            var work=form.MakeWork(form.state.List.Mods.Single());GeneralSourceInfo info;
            var backups=Directory.GetFiles(Safe.In(form.data,"BackupsAtualizacoes"),"*.zip");
            using(var backup=System.IO.Compression.ZipFile.OpenRead(backups.Single()))
            using(var old=new StreamReader(backup.GetEntry("mods/first/main.txt").Open()))
                check(old.ReadToEnd()=="old","The checked update dialog archives the previous installed version, not the downloaded package.");
            check(form.totalProgress.Caption=="Mods atualizados: 1 / 1"&&form.totalProgress.Value==100&&form.grid.Enabled,
                "The right bar reports the final update result and the app unlocks.");
            check(work.Variant.References.Contains("https://github.com/"+GeneralGithubPackageTests.Repository)&&File.Exists(newZip)&&
                !Directory.EnumerateFiles(Safe.In(form.data,"UpdateDownloads"),"*",SearchOption.AllDirectories).Any(),
                "Updating through the primary button saves the confirmed repository and releases the owned download.");
            check(automatic==1&&opened==0&&File.ReadAllText(Safe.In(state.Destination,"mods/first/main.txt"))=="new"&&
                !File.Exists(Safe.In(state.Destination,"mods/first/obsolete.txt")),"A Nexus update uses its verified GitHub mirror automatically through the same transactional installer.");
            check(work.Variant.Sources.Single()==UpdateFixtureNew&&work.Variant.SourceInfo.TryGetValue(UpdateFixtureNew,out info)&&info.NexusChecked&&!info.GithubChecked&&
                form.Generic(state.Destination).Status(work.Receipt(0))==GeneralModInstaller.Installed,
                "Automatic mirror transport keeps the Nexus source, new version and successful installation receipt together.");
            await form.Scan();await WaitForVersionCheck(form);
            check(form.CountVersions().Ready==0,"A second scan does not repeat a Nexus update delivered through GitHub.");
        });
    }
    internal static void RunGithubMetadataTests(string parent,Action<bool,string> check){
        string root=Directory.CreateDirectory(Safe.In(parent,"metadata-editor")).FullName;
        string archive=GithubFixtureZip(root,"metadata.zip","metadata");
        var release=GeneralGithubPackageTests.Release("v2.0",archive);
        string source="https://github.com/"+GeneralGithubPackageTests.Repository;
        Func<string,Task<GeneralSourceInfo>> lookup=url=>GeneralGithubUpdatePackage.ReadSourceInfo(url,_=>Safe.Json.Serialize(release));
        TestForm(new FixedCatalog{Packages=new List<FixedMod>()},new GeneralModState(),Safe.In(root,"data"),root,async form=>{
            using(var editor=new GeneralModEditor(NewEntry("",source),new string[0],"default",true,"",lookup,refreshLookup:lookup)){
                editor.Show(form);
                var name=EditorField<TextBox>(editor,"modName");var quick=EditorField<Button>(editor,"quickFill");
                await Until(()=>EditorField<Label>(editor,"fileDetails").Text.Contains("v2.0"));
                check(name.Text==GeneralGithubPackageTests.Repository.Split('/').Last()&&quick.Visible&&
                    EditorField<Label>(editor,"fileDetails").Text.Contains("v2.0"),"Pasting a GitHub repository fills API name/version/size and offers the shared quick-fill action.");
                name.Text="My custom mod";
                EditorField<TextBox>(editor,"sources").Text=GeneralGithubPackageTests.Url("v2.0",Path.GetFileName(archive));await Task.Delay(850);
                check(name.Text=="My custom mod","GitHub metadata preserves a name the user entered manually.");
                using(var choose=new Timer{Interval=30}){
                    choose.Tick+=(s,e)=>{
                        var dialog=Application.OpenForms.OfType<GeneralDialog>().FirstOrDefault(item=>item.Text==quick.Text);
                        if(dialog!=null)AllControls(dialog).OfType<Button>().Single(button=>button.Text=="Sobrescrever").PerformClick();
                    };
                    choose.Start();quick.PerformClick();await Until(()=>quick.Enabled&&name.Text==GeneralGithubPackageTests.Repository.Split('/').Last());choose.Stop();
                }
                check(name.Text==GeneralGithubPackageTests.Repository.Split('/').Last(),"GitHub quick-fill refreshes the registered provider without a Nexus credential.");
                CaptureUi(editor,Safe.In(root,"github-source-editor.png"));editor.Close();
            }
            var options=GeneralGithubPackageTests.Release("v2.0",archive,GithubFixtureZip(root,"other-platform.zip","other"));
            options.body="Nexus page: https://www.nexusmods.com/stardewvalley/mods/42";
            using(var editor=new GeneralModEditor(NewEntry("Options",source),new string[0],"default",true,"",
                url=>GeneralGithubUpdatePackage.ReadSourceInfo(url,_=>Safe.Json.Serialize(options)))){
                editor.Show(form);await Task.Delay(900);
                var counterpart=EditorField<TextBox>(editor,"counterpart");
                var picker=EditorField<GeneralPicker>(editor,"assetPicker");var save=EditorField<Button>(editor,"save");
                check(counterpart.Text=="https://www.nexusmods.com/stardewvalley/mods/42"&&picker.Visible&&picker.Items.Count==2&&!save.Enabled,
                    "The editor displays the declared Nexus reference and requires an explicit platform choice. Reference="+counterpart.Text+" Visible="+picker.Visible+" Choices="+picker.Items.Count+" Save="+save.Enabled);
                var primary=EditorField<TextBox>(editor,"sources");
                primary.Text="https://example.com/mod.zip";
                check(save.Enabled&&!picker.Visible&&EditorField<TextBox>(editor,"counterpart")==null,"Switching to a custom source clears the release choice and enables saving.");
                primary.Text=source;await Until(()=>picker.Visible);
                counterpart=EditorField<TextBox>(editor,"counterpart");
                counterpart.Text="https://www.nexusmods.com/stardewvalley/mods/43";
                picker.SelectedIndex=1;await Task.Delay(850);
                check(EditorField<TextBox>(editor,"counterpart").Text.EndsWith("/43"),"Picking a release asset preserves the manually edited reference.");
                save.PerformClick();await Until(()=>editor.Result!=null);
                var option=editor.Result.Variants.Single();
                check(option.Sources.SequenceEqual(new[]{options.assets[1].browser_download_url})&&option.References.SequenceEqual(new[]{"https://www.nexusmods.com/stardewvalley/mods/43"}),
                    "The saved variant contains exactly one package and a separate reference.");
            }
            var pending=new TaskCompletionSource<GeneralSourceInfo>();
            using(var editor=new GeneralModEditor(NewEntry("",source),new string[0],"default",true,"",url=>pending.Task)){
                editor.Show(form);await Task.Delay(850);EditorField<TextBox>(editor,"sources").Text="https://example.com/another.zip";
                pending.SetResult(await lookup(source));await Task.Delay(100);
                check(EditorField<TextBox>(editor,"modName").Text=="","A late GitHub response cannot rename a mod after its source changed.");editor.Close();
            }
            var entry=NewEntry("Verified installed mod",source);
            var info=await lookup(source);info.Version="v1.0";info.GithubChecked=true;
            info.GithubAssetUrl=GeneralGithubPackageTests.Url("v1.0","metadata.zip");entry.Variants.Single().SourceInfo[source]=info;
            using(var editor=new GeneralModEditor(entry,new string[0],"default",true,"",lookup)){
                editor.Show(form);await Task.Delay(850);var current=EditorField<GeneralModVariant>(editor,"current");
                check(current.SourceInfo[source].Version=="v1.0"&&current.SourceInfo[source].ModVersion=="v2.0"&&current.SourceInfo[source].GithubChecked,
                    "Refreshing GitHub metadata keeps the verified installed version separate from the latest release.");editor.Close();
            }
        });
    }
}

internal sealed partial class GeneralModWindow {
    internal static void RunBatchTests(string parent,Action<bool,string> check){
        var root=Directory.CreateDirectory(Safe.In(parent,"batch")).FullName;
        string oldZip,newZip;var state=SourceUpdateState(root,out oldZip,out newZip);
        var metrics=new List<object>();
        var endpoint=new Uri("https://api.github.com/repos/example/test/releases/latest");
        int lookups=0;
        Func<Uri,string> read=_=>{lookups++;return "{}";};
        GeneralNexusGithubMirror.ReadCached(root,endpoint,read);
        GeneralNexusGithubMirror.ReadCached(root,endpoint,read);
        check(lookups==1,"A GitHub response is reused across update attempts.");
        var metadata=Safe.In(root,"GithubUpdateMetadata");
        File.SetLastWriteTimeUtc(Directory.GetFiles(metadata).Single(),DateTime.UtcNow.AddHours(-25));
        GeneralNexusGithubMirror.ExpireLookups(root);
        check(!Directory.Exists(metadata),"GitHub metadata older than a day is removed.");
        GeneralNexusGithubMirror.ReadCached(root,endpoint,read);
        check(lookups==2,"An expired GitHub response is fetched again.");
        TestForm(new FixedCatalog{Packages=new List<FixedMod>()},state,Safe.In(root,"d"),root,async form=>{
            foreach(string scenario in new[]{"serial","overlap","cancel","single"}){
                bool overlap=scenario!="serial";
                var batch=new UpdateBatch();
                var jobs=Enumerable.Range(1,scenario=="single"?1:4).Select(i=>{
                    var entry=NewEntry("Item "+i,GeneralGithubPackageTests.Url("v2.0","item"+i+".zip"));
                    entry.Id="item"+i;return new Work{Entry=entry,Variant=entry.Variants[0],Batch=batch,UpdateTarget=entry.Variants[0].Sources[0]};
                }).ToArray();
                var queried=new List<string>();
                var watch=System.Diagnostics.Stopwatch.StartNew();long first=0;
                form.testGithubPackage=async url=>{
                    queried.Add(url);await Task.Delay(100);
                    var release=GeneralGithubPackageTests.Release("v2.0",newZip);
                    var asset=release.assets.Single();asset.name=GeneralGithubLink.Parse(url).Asset;asset.browser_download_url=url;
                    return await GeneralGithubUpdatePackage.Read(url,fetch:_=>Safe.Json.Serialize(release));
                };
                form.testSourceDownload=async (package,cache,control,progress)=>{
                    if(first==0)first=watch.ElapsedMilliseconds;
                    progress(new DownloadProgress{Status="Baixando",Bytes=1,Total=2});
                    await Task.Delay(220);
                    if(scenario=="cancel")form.RequestSkip();
                    if(batch.Cancelled)control.Cancel();
                    control.Checkpoint();return null;
                };
                if(overlap)form.normal.AddRange(jobs);
                foreach(var job in jobs){
                    if(overlap)form.normal.Remove(job);
                    form.current=job;
                    var package=await form.PrepareUpdate(job);
                    try{await form.DownloadSourceUpdate(job,package);}
                    catch(OperationCanceledException){if(!batch.Cancelled)throw;}
                    finally{form.CleanUpdateDownload(job);}
                    int done=Array.IndexOf(jobs,job)+1;
                    check(queried.Count<=Math.Min(jobs.Length,done+1),"At most one upcoming lookup after item "+done+" in "+scenario);
                    if(batch.Cancelled)break;
                }
                check(queried.SequenceEqual(jobs.Take(scenario=="cancel"?2:jobs.Length).Select(job=>job.UpdateTarget)),
                    "One lookup per selected item in order, cancellation stops after the second: "+scenario);
                metrics.Add(new{Scenario=scenario,ElapsedMs=watch.ElapsedMilliseconds,FirstDownloadMs=first,ItemsQueried=queried.Count});
                form.current=null;form.normal.Clear();
            }
            Safe.WriteJson(Safe.In(root,"timings.json"),metrics);
            var work=await SourceUpdateReady(form,root,oldZip);
            work.Batch=new UpdateBatch();
            int opened=0,prompts=0;
            form.testFailurePrompt=_=>{prompts++;return false;};
            form.launch=url=>{check(url==work.UpdateTarget,"Quota fallback opens the exact source URL.");opened++;form.skipRequests.Add(work);};
            form.testUpdatePackage=_=>{throw new NexusPackageException("rate-limit","fixture");};
            await form.RunWork(work,state.Destination,false);
            check(work.Batch.Manual&&opened==1&&prompts==0&&!work.Applied,"Quota before metadata opens one page without a failure dialog.");
            form.skipRequests.Clear();work=await SourceUpdateReadyAfterQuota(form,state);
            work.Batch=new UpdateBatch();
            form.testUpdatePackage=url=>GeneralNexusUpdatePackage.Read(url,"fixture",(Uri uri,string key,out HttpStatusCode status)=>{
                status=HttpStatusCode.OK;
                if(uri.AbsolutePath.Contains("md5_search")){status=(HttpStatusCode)429;return null;}
                return Safe.Json.Serialize(new{file_id=11,category_id=1,name="item",file_name=Path.GetFileName(newZip),
                    size_in_bytes=new FileInfo(newZip).Length,version="2.0"});
            });
            await form.RunWork(work,state.Destination,false);
            check(work.HaveDownload&&opened==1&&prompts==0&&File.Exists(newZip)&&!work.Applied,
                "Quota during confirmation keeps the original download pending without reopening the page.");
        });
    }
    internal static void RunManualUpdateLinkTest(string parent,Action<bool,string> check){
        string root=Safe.In(parent,"manual-link");
        string oldZip,newZip;var state=SourceUpdateState(root,out oldZip,out newZip);
        TestForm(new FixedCatalog{Packages=new List<FixedMod>()},state,Safe.In(root,"d"),root,async form=>{
            var job=await SourceUpdateReady(form,root,oldZip);
            int pages=0,queries=0;
            form.testUpdatePackage=url=>GeneralNexusUpdatePackage.Read(url,"fixture",(Uri uri,string key,out HttpStatusCode status)=>{
                status=HttpStatusCode.OK;
                if(uri.AbsolutePath.Contains("md5_search"))
                    return uri.AbsolutePath.EndsWith("/"+SourceUpdateMd5(newZip)+".json",StringComparison.Ordinal)?
                        Safe.Json.Serialize(new[]{new{mod=new{mod_id=42,domain_name="stardewvalley"},
                        file_details=new{file_id=11,category_id=1,size_in_bytes=new FileInfo(newZip).Length}}}):"[]";
                if(uri.AbsolutePath.EndsWith("/mods/42.json"))return "{\"mod_id\":42,\"name\":\"Fixture\"}";
                return Safe.Json.Serialize(new{file_id=11,category_id=1,name="item",file_name="manual-expected.zip",
                    size_in_bytes=new FileInfo(newZip).Length,version="2.0"});
            });
            form.testGithubMirrorFetch=_=>{queries++;return "{\"items\":[]}";};
            form.launch=url=>{
                if(url!=job.UpdateTarget)throw new InvalidOperationException("Wrong manual download link");
                pages++;
                if(pages==1)throw new System.ComponentModel.Win32Exception();
            };
            using(var confirm=new Timer{Interval=30}){
                confirm.Tick+=(s,e)=>{
                    var dialog=Application.OpenForms.OfType<GeneralInstallConfirmation>().FirstOrDefault();
                    if(dialog==null)return;
                    AllControls(dialog).OfType<CheckBox>().Single(box=>box.Name=="BackupUpdateSelection").Checked=false;
                    AllControls(dialog).OfType<Button>().Single(button=>button.Name=="ConfirmPendingInstall").PerformClick();
                };
                confirm.Start();
                form.primaryAction.PerformClick();
                await Until(()=>form.currentProgress.CaptionLink!=null&&pages==1);
                check(form.currentProgress.Caption.Contains("Mod não encontrado no GitHub")&&form.selectedFooterAction==FooterAction.CancelUpdate,
                    "The manual phase presents a clickable link even when automatic browser opening fails.");
                form.currentProgress.CaptionLink();
                check(pages==2,"Clicking the progress link reopens the exact Nexus file.");
                form.footerAction.PerformClick();
                await Until(()=>!form.working);await WaitForVersionCheck(form);
                check(form.currentProgress.CaptionLink==null&&form.grid.Enabled&&form.totalProgress.Caption=="Mods atualizados: 0 / 1",
                    "Cancelling releases the screen and retains the number of verified updates.");
                form.primaryAction.PerformClick();
                await Until(()=>form.currentProgress.CaptionLink!=null&&pages==3);
                check(queries==1,"A cancelled GitHub search can be reused on the next update.");
                form.Drop(new[]{newZip});
                await Until(()=>!form.working);confirm.Stop();
                check(File.ReadAllText(Safe.In(state.Destination,"mods/first/main.txt"))=="new"&&File.Exists(newZip)&&
                    !Directory.Exists(Safe.In(form.data,"BackupsAtualizacoes"))&&form.totalProgress.Caption=="Mods atualizados: 1 / 1",
                    "Dropping the verified manual archive installs it without an optional backup and preserves the downloaded original.");
            }
        });
    }

    static async Task<Work> SourceUpdateReadyAfterQuota(GeneralModWindow form,GeneralModState state){
        await form.Scan();await WaitForVersionCheck(form);
        return form.SelectedUpdates().Single();
    }
}
