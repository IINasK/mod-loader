using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing;
using System.Windows.Forms;

internal static class GeneralModUpdatesTests {
    internal static void Run(string parent){
        string root=OnlineTests.NestedRoot(parent,"updates-");Directory.CreateDirectory(root);
        var checks=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new IOException(name);checks.Add(name);};
        var link=GeneralNexusLink.Parse("https://www.nexusmods.com/skyrimspecialedition/mods/42?tab=files&file_id=10");
        string files="{\"files\":[{\"file_id\":10,\"category_id\":4,\"version\":\"1.0\"},{\"file_id\":11,\"category_id\":4,\"version\":\"1.1\"},{\"file_id\":12,\"category_id\":1,\"version\":\"1.2\"}],\"file_updates\":[{\"old_file_id\":10,\"new_file_id\":11},{\"old_file_id\":11,\"new_file_id\":12}]}";
        var result=NexusUpdateSession.Parse(files,link);
        check(result.State=="update"&&result.SourceVersion=="1.0"&&result.Version=="1.2"&&result.Url.Contains("skyrimspecialedition/mods/42"),"Follow author-declared update chain preserving game/mod identity");
        link.File=12;check(NexusUpdateSession.Parse(files,link).State=="current","Selected current variant has no reported successor");
        link.File=0;check(NexusUpdateSession.Parse(files,link).State=="choose-file","Mod page alone does not guess file variant");link.File=10;
        check(NexusUpdateSession.Parse(files.Replace("\"new_file_id\":12","\"new_file_id\":10"),link).State=="ambiguous","Update chain cycle is rejected");
        check(NexusUpdateSession.Parse(files.Replace("\"old_file_id\":11","\"old_file_id\":10"),link).State=="ambiguous","Branching successors require review");
        check(NexusUpdateSession.Parse(files.Replace("\"category_id\":1","\"category_id\":6"),link).State=="unavailable","Deleted file is not offered");
        check(NexusUpdateSession.Parse("{}",link).State=="unavailable"&&NexusUpdateSession.Parse("not json",link).State=="unavailable","Bad responses never mark current");
        check(NexusUpdateSession.Parse(files.Replace("\"file_id\":11","\"file_id\":10"),link).State=="unavailable","Duplicate file identity rejected");
        check(ModVersionNumbers.Compare("1.10","1.9")==1&&ModVersionNumbers.Compare("v2.0","2.0.0.0")==0,"Numeric version ordering and zero padding");
        check(ModVersionNumbers.Compare("1.2-beta","1.1")==null&&ModVersionNumbers.Compare("Latest","1.1")==null,"Ambiguous author labels are not invented versions");
        check(ModVersionNumbers.Compare("1.0","2.0")==-1&&ModVersionNumbers.Compare(null,"1.1")==null,"Older and unknown versions distinguished");
        check(ModVersionNumbers.Compare("١.٢","1.0")==null,"Unicode numeric labels remain unknown without throwing");
        int calls=0,running=0,peak=0;
        var session=new NexusUpdateSession("fixture-key",(GeneralNexusLink source,string key,out HttpStatusCode status)=>{
            Interlocked.Increment(ref calls);int count=Interlocked.Increment(ref running);lock(checks)peak=Math.Max(peak,count);
            Thread.Sleep(25);Interlocked.Decrement(ref running);status=HttpStatusCode.OK;return files;
        });
        Task.WaitAll(Enumerable.Range(0,20).Select(i=>session.Check(link,()=>true)).ToArray());
        check(calls==1,"Concurrent requests for same mod are coalesced");
        Task.WaitAll(Enumerable.Range(0,8).Select(i=>session.Check(new GeneralNexusLink{Game="stardewvalley",Mod=i+1,File=10},()=>true)).ToArray());
        check(calls==9&&peak<=2,"Different games separated and at most two network workers");
        var anonymous=new NexusUpdateSession(null,(GeneralNexusLink source,string key,out HttpStatusCode status)=>{throw new Exception("Should not call");});
        check(anonymous.Check(link,()=>true).Result.State=="connect","No credentials produces connect state without network");
        check(session.Check(link,()=>false).Result.State=="cancelled","Closed view rejects stale results");
        check(session.Check(link,()=>true).Result.State=="update","Reopening a view reuses valid session result");
        bool viewing=true;
        var delayed=session.Check(new GeneralNexusLink{Game="fallout4",Mod=99,File=10},()=>viewing);
        viewing=false;
        check(delayed.Result.State=="cancelled"&&session.Check(new GeneralNexusLink{Game="fallout4",Mod=99,File=10},()=>true).Result.State=="update","Closing during a shared request does not poison reopening");
        calls=0;
        var limited=new NexusUpdateSession("fixture-key",(GeneralNexusLink source,string key,out HttpStatusCode status)=>{calls++;status=(HttpStatusCode)429;return null;});
        check(limited.Check(link,()=>true).Result.State=="rate-limit","Rate limit is visible");
        check(limited.Check(new GeneralNexusLink{Game="other",Mod=1,File=10},()=>true).Result.State=="rate-limit"&&calls==1,"Rate limit stops subsequent requests");
        var rejected=new NexusUpdateSession("fixture-key",(GeneralNexusLink source,string key,out HttpStatusCode status)=>{status=HttpStatusCode.Unauthorized;return null;});
        check(rejected.Check(link,()=>true).Result.State=="authentication","Invalid key never claims latest version");
        var offline=new NexusUpdateSession("fixture-key",(GeneralNexusLink source,string key,out HttpStatusCode status)=>{status=0;return null;});
        check(offline.Check(link,()=>true).Result.State=="unavailable","Offline is unknown rather than current");
        int githubCalls=0;
        var github=new GithubModUpdateSession(endpoint=>{
            Interlocked.Increment(ref githubCalls);
            if(endpoint.AbsoluteUri!="https://api.github.com/repos/example/any-game/releases/latest")
                throw new IOException("The source must resolve to its official repository release endpoint.");
            return "{\"tag_name\":\"v2.1\",\"draft\":false,\"prerelease\":false}";
        });
        var upstream=github.Check("https://github.com/example/any-game/releases/download/v2.0/asset.zip",()=>true).Result;
        check(upstream.State=="update"&&upstream.SourceVersion=="v2.0"&&upstream.Version=="v2.1"&&githubCalls==1,
            "GitHub mod releases use the repository independently of game or mod name.");
        check(github.Check("https://github.com/example/any-game/releases/download/v2.1/other.zip",()=>true).Result.State=="current"&&githubCalls==1,
            "Multiple assets in one GitHub repository reuse the release response.");
        check(!GithubModUpdateSession.Supports("https://github.com.evil.example/example/any-game/releases/download/v2.0/asset.zip")&&
            !GithubModUpdateSession.Supports("https://raw.githubusercontent.com/example/any-game/commit/mod.lua"),
            "Unverified release identities are not guessed.");
        string credentialRoot=Path.Combine(root,"credentials");
        NexusCredentials.Save(credentialRoot,"fixture-private-key");
        check(!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(credentialRoot,"nexus-key.dat"))).Contains("fixture-private-key"),"Credential protected by per-user DPAPI");
        check(!NexusCredentials.Valid("bad\r\nkey")&&!NexusCredentials.Valid(new string('a',257)),"Header injection and oversized keys rejected");
        string local=Path.Combine(root,"game");Directory.CreateDirectory(local);
        check(ModVersionNumbers.ReadLocal(local,new[]{"missing.dll","config.json"})==null,"No guessed version from non-binary or missing file");
        File.Copy(System.Reflection.Assembly.GetExecutingAssembly().Location,Path.Combine(local,"manual.dll"));
        check(ModVersionNumbers.Compare(ModVersionNumbers.ReadLocal(local,new[]{"manual.dll"}),"1.0")==1,"Manually copied PE reports newer version without execution");
        Safe.WriteJson(Path.Combine(root,"result.json"),new{Passed=true,Checks=checks,LiveAuthenticatedNexus=false});
    }
}

internal sealed partial class GeneralModWindow {
    internal static void RunArchiveXLUpdateTest(string parent,string sources){
        string root=OnlineTests.NestedRoot(parent,"archive-update-");
        Directory.CreateDirectory(root);
        string game=Safe.In(root,"g"),saves=Directory.CreateDirectory(Safe.In(root,"s")).FullName;
        string executable=Safe.In(game,"bin/x64/Cyberpunk2077.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executable));
        File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","FixtureGame.exe"),executable);
        File.WriteAllText(Safe.In(game,".fixture-only"),"test");
        Directory.CreateDirectory(Safe.In(game,"archive/pc/ep1"));
        File.WriteAllText(Safe.In(game,"archive/pc/ep1/ep1.addcont_keystone"),"");
        var current=FixedData.Load();
        current.GameFileVersion="2.5.0.0";
        var list=GeneralModLibrary.Seed(current,new ModImportItem[0]);
        var after=current.Packages.Single(mod=>mod.Id=="ArchiveXL");
        list.Mods.Single(mod=>mod.Id==after.Id).Variants.Single().Sources=after.PreviousRelease.Assets.Select(asset=>asset.Url).ToList();
        var legacy=CatalogModVersions.ForReceipt(current,new InstalledSnapshotManifest{List=list});
        var before=legacy.Packages.Single(mod=>mod.Id==after.Id);
        var cache=new DownloadCache(Safe.In(root,"cache"));
        foreach(var asset in before.Assets.Concat(after.Assets).Concat(current.Packages.Single(mod=>mod.Id=="NightCoop").Assets))
            cache.Import(Safe.In(sources,asset.Name),asset);
        new FixedInstaller(legacy,cache,game,saves,true).Install(new[]{before.Id},true,_=>{});
        var scan=FixedData.Scan(after,game);
        if(scan.State!=UpdateAvailableStatus||scan.Original!=4||scan.Different!=0||scan.Missing!=0)
            throw new IOException("ArchiveXL 1.28.6 did not produce the update badge.");
        var state=new GeneralModState{List=list,Destination=game,DownloadFolder=root,Language="pt"};
        TestForm(current,state,Safe.In(root,"app"),root,async form=>{
            form.testModUpdateSession=new NexusUpdateSession(null);
            await form.Scan();
            await WaitForVersionCheck(form);
            var archive=form.state.List.Mods.Single(mod=>mod.Id==before.Id);
            var basis=form.state.List.Mods.Single(mod=>mod.Id=="NightCoop");
            var archiveRow=form.ModRows().Single(row=>Equals(row.Tag,archive.Id));
            if(!Equals(archiveRow.Cells["Status"].Value,"Atualizar · 1.28.6")||form.primaryAction.Text!="Atualizar mods")
                throw new IOException("The real scan did not display the update badge and batch action: "+archiveRow.Cells["Status"].Tag+" / "+form.primaryAction.Text);
            string baseKey=form.MakeWork(basis).Key;
            form.statuses[baseKey]=UpdateAvailableStatus;
            if(form.SelectedUpdates().Count!=2)throw new IOException("No selection did not include both update candidates.");
            form.updateFocusedId=archive.Id;
            form.RefreshPrimaryAction();
            if(form.primaryAction.Text!="Atualizar mod"||form.SelectedUpdates().Single().Entry.Id!=archive.Id)
                throw new IOException("Selecting a row did not restrict the update.");
            form.marked.Add(basis.Id);
            if(form.SelectedUpdates().Single().Entry.Id!=basis.Id)throw new IOException("A mark did not restrict the batch.");
            form.marked.Add(archive.Id);
            if(form.SelectedUpdates().Count!=2)throw new IOException("Marking both updates did not select both.");
            form.marked.Clear();
            form.statuses[baseKey]="Instalado";
            var selected=form.SelectedUpdates().Single();
            int prompts=0;
            form.testVersionChoice=difference=>{prompts++;return false;};
            await form.RunWork(selected,game,false);
            if(prompts!=1||!FixedData.Scan(before,game).IsCorrect||form.SelectedUpdates().Count!=1)
                throw new IOException("Closing the version choice must leave the recognized update available.");
            selected=form.SelectedUpdates().Single();
            selected.UpdateRequested=true;
            await form.RunWork(selected,game,false);
            if(prompts!=1||!FixedData.Scan(after,game).IsCorrect||form.SelectedUpdates().Count!=0)
                throw new IOException("An explicit update must install the known successor without a second confirmation.");
        });
        Safe.WriteJson(Safe.In(root,"result.json"),new{Passed=true,Previous=before.Version,Installed=after.Version,RealGameTouched=false});
    }

    static async Task WaitForVersionCheck(GeneralModWindow form){
        for(int tries=0;form.checkingModUpdates&&tries<100;tries++)await Task.Delay(20);
        if(form.checkingModUpdates)throw new IOException("The fixture version check did not finish.");
    }

    static void CheckInstalledVersionRows(string root){
        string destination=Directory.CreateDirectory(Safe.In(root,"version-game")).FullName;
        string source=Safe.In(root,"fixture-version.zip");
        using(var zip=System.IO.Compression.ZipFile.Open(source,System.IO.Compression.ZipArchiveMode.Create))
        using(var text=new StreamWriter(zip.CreateEntry("mods/sample.txt").Open()))text.Write("installed payload");
        string nexus="https://www.nexusmods.com/skyrimspecialedition/mods/42?tab=files&file_id=10";
        var entry=NewEntry("Installed fixture",nexus);
        var missing=NewEntry("Not installed fixture","https://www.nexusmods.com/skyrimspecialedition/mods/44?tab=files&file_id=10");
        var state=new GeneralModState{Destination=destination,DownloadFolder=root,Language="pt",Theme="dark",
            List=new GeneralModList{Mods=new List<GeneralModEntry>{entry,missing}}};
        TestForm(FixedData.Load(),state,Safe.In(root,"version-data"),root,async form=>{
            var installed=form.state.List.Mods.Single(mod=>mod.Id==entry.Id);
            var job=form.MakeWork(installed);
            form.Generic(destination).Install(source,job.Receipt(0),_=>{});
            int requests=0,startedBeforeLocal=0;
            using(var lookupStarted=new ManualResetEventSlim()){
                form.testStage=(key,phase)=>{
                    if(phase=="scan.begin"&&startedBeforeLocal==0){
                        if(!lookupStarted.Wait(3000))throw new IOException("Nexus lookup must start alongside the local scan.");
                        startedBeforeLocal++;
                    }
                };
                form.testModUpdateSession=new NexusUpdateSession("fixture-key",(GeneralNexusLink link,string key,out HttpStatusCode status)=>{
                    Interlocked.Increment(ref requests);
                    lookupStarted.Set();
                    status=HttpStatusCode.OK;
                    return "{\"files\":[{\"file_id\":10,\"category_id\":4,\"version\":\"1.0\"},{\"file_id\":11,\"category_id\":1,\"version\":\"1.1\"}],\"file_updates\":[{\"old_file_id\":10,\"new_file_id\":11}]}";
                });
                form.testGithubUpdateSession=new GithubModUpdateSession(_=>{throw new IOException("These fixtures only reference Nexus.");});
                try{await form.Scan();}
                finally{form.testStage=null;}
            }
            if(startedBeforeLocal!=1)throw new IOException("The fixture did not observe an overlapping lookup.");
            form.updateFocusedId=installed.Id;
            form.RefreshPrimaryAction();
            if(form.primaryAction.Text!="Abrir atualização")throw new IOException("Selecting an online update must not fall back to Disable.");
            var open=form.launch;
            string openedLink=null;
            form.launch=url=>openedLink=url;
            try{form.StartSelected();}
            finally{form.launch=open;}
            if(openedLink!=nexus.Replace("file_id=10","file_id=11"))throw new IOException("The action must open the reported successor file.");
            form.updateFocusedId=null;
            Func<DataGridViewRow> installedRow=()=>form.ModRows().Single(row=>Equals(row.Tag,entry.Id));
            var cell=installedRow().Cells["Status"];
            if(requests!=2||!Equals(cell.Tag,UpdateAvailableStatus)||!Equals(cell.Value,"Atualizar · 1.0")||
                !cell.ToolTipText.Contains("origem 1.1")||form.InstallableUpdates().Count!=0)
                throw new IOException("Scan must show the installed API file version and the available successor as distinct values.");
            if(StatusFilterKey(GeneralStatusPresentation.Label(UpdateAvailableStatus))!="update")
                throw new IOException("The short update label must still use the update filter.");
            var absent=form.ModRows().Single(row=>Equals(row.Tag,missing.Id)).Cells["Status"];
            if(Convert.ToString(absent.Value).Contains("1.0")||form.modUpdates.Single(row=>row.Key==form.MakeWork(missing).Key).Online.State!="update"||
                !form.ModVersionSummary().Contains("2/2 consultados"))
                throw new IOException("An absent mod is checked at the source and has no installed-version badge.");
            Action<VersionFilter> activate=filter=>{
                var label=form.footerNumbers[(int)filter];
                typeof(LinkLabel).GetMethod("OnLinkClicked",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)
                    .Invoke(label,new object[]{new LinkLabelLinkClickedEventArgs(label.Links[0])});
            };
            activate(VersionFilter.Review);
            if((form.ModRows().Select(row=>row.Tag).SingleOrDefault() as string)!=entry.Id||!form.clearFilters.Enabled)
                throw new IOException("Review must filter the main table to the installed mod.");
            activate(VersionFilter.Update);
            if(form.ModRows().Any())throw new IOException("Update must include only actionable updates.");
            activate(VersionFilter.Checked);
            if(form.ModRows().Count()!=2)throw new IOException("Checked must show both consulted mods.");
            activate(VersionFilter.Pending);
            if(form.ModRows().Any())throw new IOException("Pending must exclude checked mods.");
            form.clearFilters.PerformClick();
            if(form.HasListFilters||form.ModRows().Count()!=2)throw new IOException("The header undo must clear a version filter.");
            using(var image=new Bitmap(form.Width,form.Height)){
                form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));
                image.Save(Safe.In(root,"installed-version-table.png"));
            }
            form.testModUpdateSession=new NexusUpdateSession(null,(GeneralNexusLink link,string key,out HttpStatusCode status)=>{
                throw new IOException("Without authentication the fixture must not request Nexus.");
            });
            var other=form.state.List.Mods.Single(mod=>mod.Id==missing.Id);
            var sources=form.MakeWork(other).Variant.Sources;
            sources.Add("https://github.com/fixture/mod/releases/download/v1.0/mod.zip");
            var finishLookup=new TaskCompletionSource<bool>();
            form.testGithubUpdateSession=new GithubModUpdateSession(_=>{
                if(!finishLookup.Task.Wait(8000))throw new IOException("The local scan waited for GitHub.");
                return null;
            });
            try{
                await form.Scan();
                if(form.scanning||!form.checkingModUpdates||form.selectedFooterAction!=FooterAction.ConnectNexus||!form.footerAction.Enabled)
                    throw new IOException("The connection action must be available even with a pending background lookup.");
            }finally{finishLookup.TrySetResult(true);}
            await WaitForVersionCheck(form);
            sources.RemoveAt(sources.Count-1);
            await form.Scan();
            await WaitForVersionCheck(form);
            if(!Equals(installedRow().Cells["Status"].Value,"Instalado")||!form.ModVersionSummary().Contains("2 sem consulta")||
                form.selectedFooterAction!=FooterAction.ConnectNexus)
                throw new IOException("The local installation remains visible while the unauthenticated lookup remains unresolved.");
            form.ClientSize=new Size(1000,680);
            form.PerformLayout();
            if(!form.footerNumbers[0].Text.StartsWith("0/2")||!form.footerNumbers[3].Text.StartsWith("2\n")||
                form.versionFooter.Height<38||form.footerNumbers.Any(label=>label.Width<72))
                throw new IOException("The footer filters must fit the compact layout.");
            var footerPosition=form.versionFooter.PointToScreen(Point.Empty);
            form.marked.Add(entry.Id);form.DrawRows();
            if(form.selectedFooterAction!=FooterAction.Uninstall||!form.footerAction.Enabled||
                form.versionFooter.PointToScreen(Point.Empty)!=footerPosition)
                throw new IOException("Uninstall must stay accessible without moving the version filters.");
            form.marked.Clear();form.DrawRows();
            using(var capture=new Bitmap(form.Width,form.Height)){
                form.DrawToBitmap(capture,new Rectangle(Point.Empty,capture.Size));
                capture.Save(Safe.In(root,"version-footer.png"));
            }
            CheckNexusConnectionDialog(form,root);
            using(var review=new GeneralDependencyReview("Arquivos locais · 2 mods conferidos\r\n"+form.ModVersionSummary()+
                "\r\nTags do Nexus · 11 consultas indisponíveis",new GeneralDependencyIssue[0],0,_=>{},
                new[]{new P2PSyncDifference{Kind="found",Path="mods/unlinked.txt"}})){
                review.StartPosition=FormStartPosition.Manual;review.Location=new Point(-30000,-30000);
                review.Show(form);
                Application.DoEvents();
                var button=AllControls(review).OfType<Button>().Single(control=>control.Name=="ShowUnlistedInventory");
                var visibleLabels=AllControls(review).OfType<Label>().Where(label=>label.Visible).ToArray();
                if(!button.Text.Contains("arquivos não vinculados")||!visibleLabels.Any(label=>label.Text.Contains("11 consultas"))||
                    !visibleLabels.Any(label=>label.Text.Contains("Nenhuma pendência local")))
                    throw new IOException("Verification must explain the unlinked inventory and preserve pending query results.");
                using(var image=new Bitmap(review.Width,review.Height)){
                    review.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));
                    image.Save(Safe.In(root,"verification-results.png"));
                }
                review.Close();
            }
        });
    }

    static void CheckNexusConnectionDialog(GeneralModWindow form,string root){
        string opened=null;
        int attempts=0,stage=0;
        Exception failure=null;
        using(var dialog=new GeneralNexusConnectionDialog(url=>opened=url,key=>{
            attempts++;
            return Task.FromResult(key=="fixture-key"?NexusKeyCheck.Accepted:NexusKeyCheck.Rejected);
        }))
        using(var timer=new System.Windows.Forms.Timer{Interval=65}){
            dialog.StartPosition=FormStartPosition.Manual;dialog.Location=new Point(-30000,-30000);
            timer.Tick+=(s,e)=>{
                try{
                    var warning=Application.OpenForms.OfType<NexusKeyTrustDialog>().FirstOrDefault();
                    if(warning!=null){
                        if(opened!=null||!AllControls(warning).OfType<GeneralSurface>().Any(surface=>surface.OutlineColor==Color.FromArgb(224,72,82)))
                            throw new IOException("Confirm before opening Nexus, with a red-outline warning.");
                        if(stage==2){
                            stage++;
                            warning.Controls.Find("TrustNexusNo",true).OfType<Button>().Single().PerformClick();
                        }else{
                            using(var capture=new Bitmap(warning.Width,warning.Height)){
                                warning.DrawToBitmap(capture,new Rectangle(Point.Empty,capture.Size));
                                capture.Save(Safe.In(root,"nexus-trust.png"));
                            }
                            warning.Controls.Find("TrustNexusYes",true).OfType<Button>().Single().PerformClick();
                        }
                        return;
                    }
                    var input=AllControls(dialog).OfType<TextBox>().Single(control=>control.Name=="NexusPersonalKey");
                    var submit=AllControls(dialog).OfType<Button>().Single(control=>control.Name=="ValidateNexusKey");
                    if(stage++==0){
                        if(!input.UseSystemPasswordChar||submit.Enabled||opened!=null||
                            !AllControls(dialog).OfType<Label>().Any(label=>label.Text.Contains("Personal API Key")&&label.Text.Contains("role até o fim"))||
                            !AllControls(dialog).OfType<Button>().Any(button=>button.Name=="PasteNexusKey"&&button.Visible))
                            throw new IOException("The dialog must explain which key to use before opening the browser.");
                        var paste=AllControls(dialog).OfType<Button>().Single(button=>button.Name=="PasteNexusKey");
                        if(paste.Height!=input.Parent.Height||paste.Bottom>paste.Parent.ClientSize.Height||
                            paste.Top!=input.Parent.Top||paste.Left<=input.Parent.Right)
                            throw new IOException("The paste button and masked field must fit on the same line.");
                        using(var capture=new Bitmap(dialog.Width,dialog.Height)){
                            dialog.DrawToBitmap(capture,new Rectangle(Point.Empty,capture.Size));
                            capture.Save(Safe.In(root,"nexus-connection.png"));
                        }
                        var browse=AllControls(dialog).OfType<Button>().Single(control=>control.Name=="OpenNexusApiAccess");
                        stage=2;browse.PerformClick();
                        if(opened!=null)throw new IOException("Declining must not open the site.");
                        browse.PerformClick();
                        input.Text="invalid-fixture-key";submit.PerformClick();
                    }else{
                        if(!input.Enabled||dialog.PersonalKey!=null||
                            opened!="https://next.nexusmods.com/settings/api-keys#:~:text=Personal%20API%20Key"||
                            !AllControls(dialog).OfType<Label>().Single(label=>label.Name=="NexusConnectionFeedback").Text.Contains("recusou"))
                            throw new IOException("A rejected personal key must stay editable and unsaved.");
                        timer.Stop();input.Text="fixture-key";submit.PerformClick();
                    }
                }catch(Exception ex){
                    failure=ex;timer.Stop();
                    foreach(var warning in Application.OpenForms.OfType<NexusKeyTrustDialog>().ToArray())warning.Close();
                    dialog.Close();
                }
            };
            timer.Start();
            var result=dialog.ShowDialog(form);
            if(failure!=null)throw failure;
            if(result!=DialogResult.OK||dialog.PersonalKey!="fixture-key"||attempts!=2)
                throw new IOException("The connection dialog did not accept the validated test key.");
        }
    }

    internal static void RunModUpdateUiTests(string parent){
        string root=OnlineTests.NestedRoot(parent,"versions-ui-");Directory.CreateDirectory(root);
        CheckInstalledVersionRows(root);
        Safe.WriteJson(Path.Combine(root,"result.json"),new{Passed=true,NativeWinForms=true});
    }
}
