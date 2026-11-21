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
        Func<string,NexusUpdateSession> versionFiles=body=>new NexusUpdateSession("fixture-key",(GeneralNexusLink page,string key,out HttpStatusCode status)=>{
            status=HttpStatusCode.OK;return body;
        });
        var named=files.Replace("\"category_id\":1", "\"file_name\":\"main.zip\",\"category_id\":1");
        var lineage=versionFiles(named);
        var exact=new[]{new[]{new GeneralNexusRequirement{MinFile=10}}};
        check(lineage.DependencyChoices(link,0,exact,()=>true).Result.Automatic.file_id==12,
            "The only published successor in the required lineage is selected.");
        exact[0][0].MaxFile=11;
        var incompatible=lineage.DependencyChoices(link,0,exact,()=>true).Result;
        check(incompatible.Automatic==null&&incompatible.Choices.Length==0&&incompatible.Failure=="no-compatible-file",
            "A single published archive beyond the supported maximum cannot be selected.");
        check(lineage.DependencyChoices(link,0,new GeneralNexusRequirement[0][],()=>true).Result.Automatic.file_id==12,
            "Archived editions do not make a single current main file ambiguous.");
        check(versionFiles(named.Replace("\"category_id\":1","\"category_id\":3"))
                .DependencyChoices(link,0,new GeneralNexusRequirement[0][],()=>true).Result.Automatic==null,
            "An unpinned optional archive is not selected as the main file.");
        var twoCurrent=named.Replace("\"file_id\":11,\"category_id\":4","\"file_id\":11,\"file_name\":\"old-still-published.zip\",\"category_id\":1");
        exact[0][0].MaxFile=0;
        check(versionFiles(twoCurrent).DependencyChoices(link,0,exact,()=>true).Result.Automatic.file_id==12,
            "A documented successor takes precedence over earlier files still published in the same compatible line.");
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
        File.WriteAllText(Safe.In(destination,".fixture-only"),"test");
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
            if(form.primaryAction.Text!="Atualizar mod") {
                var proof=form.SourceUpdateOwnership(job);
                throw new IOException("Selecting a confirmed Nexus update must offer the controlled update queue. Action="+form.primaryAction.Text+
                    "; Status="+form.statuses[job.Key]+"; Ownership="+(proof==null?"missing":proof.Reason??proof.CanUpdate.ToString()));
            }
            if(form.SelectedUpdates().Single().UpdateTarget!=nexus.Replace("file_id=10","file_id=11"))
                throw new IOException("The update queue must bind the reported successor file instead of opening a generic release page.");
            form.updateFocusedId=null;
            Func<DataGridViewRow> installedRow=()=>form.ModRows().Single(row=>Equals(row.Tag,entry.Id));
            var cell=installedRow().Cells["Status"];
            if(requests!=2||!Equals(cell.Tag,UpdateAvailableStatus)||!Equals(cell.Value,"Atualizar · 1.0")||
                !cell.ToolTipText.Contains("origem 1.1")||form.InstallableUpdates().Count!=1)
                throw new IOException("Scan must show the installed API file version and the available successor as distinct values.");
            if(StatusFilterKey(GeneralStatusPresentation.Label(UpdateAvailableStatus))!="update")
                throw new IOException("The short update label must still use the update filter.");
            var absent=form.ModRows().Single(row=>Equals(row.Tag,missing.Id)).Cells["Status"];
            if(Convert.ToString(absent.Value).Contains("1.0")||form.modUpdates.Single(row=>row.Key==form.MakeWork(missing).Key).Online.State!="update"||
                !form.ModVersionSummary().Any(line=>line.Value=="2 de 2 mods"))
                throw new IOException("An absent mod is checked at the source and has no installed-version badge.");
            Action<VersionFilter> activate=filter=>{
                var label=form.footerNumbers[(int)filter];
                typeof(LinkLabel).GetMethod("OnLinkClicked",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)
                    .Invoke(label,new object[]{new LinkLabelLinkClickedEventArgs(label.Links[0])});
            };
            activate(VersionFilter.Review);
            if(form.ModRows().Any()||!form.clearFilters.Enabled)
                throw new IOException("Review must exclude a Nexus update that the controlled queue can install.");
            activate(VersionFilter.Update);
            if((form.ModRows().Select(row=>row.Tag).SingleOrDefault() as string)!=entry.Id)
                throw new IOException("Update must include the actionable Nexus update.");
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
            form.updateFocusedId=installed.Id;
            var open=form.launch;
            string openedLink=null;form.launch=url=>openedLink=url;
            try{
                form.StartSelected();
                await ChooseUpdateResponse("CancelPendingInstall");
                if(openedLink!=null||form.ManualUpdatesOnly(installed.Id)||form.SelectedUpdates().Count!=1)
                    throw new IOException("No must cancel only this attempt.");

                form.StartSelected();
                await ChooseUpdateResponse("DeferSelectedUpdates");
                if(openedLink!=null||form.SelectedUpdates().Count!=0||
                    !form.library.Load().ManualUpdateMods.SequenceEqual(new[]{installed.Id}))
                    throw new IOException("Don't ask again must persist just the selected mod and open no link.");

                form.grid.CurrentCell=installedRow().Cells["Name"];
                var menu=form.grid.ContextMenuStrip;
                menu.Show(form.grid,new Point(100,100));
                var action=(ToolStripMenuItem)menu.Items.Find("UpdateIndividualMod",false).Single();
                if(!action.Visible||action.Text!="Atualizar mod"||menu.Items[menu.Items.Count-1]!=action)
                    throw new IOException("The mod menu must keep the manual action at the bottom.");
                action.PerformClick();menu.Close();
                await ChooseUpdateResponse("CancelPendingInstall",manual:true);
                if(openedLink!=null||!form.ManualUpdatesOnly(installed.Id))
                    throw new IOException("Cancelling the manual confirmation keeps the saved preference and opens no link.");
            }finally{form.launch=open;form.updateFocusedId=null;}

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
            if(!Equals(installedRow().Cells["Status"].Value,"Instalado")||!form.ModVersionSummary().Any(line=>line.Value.Contains("2 sem consulta"))||
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
            var summary=new[]{VerificationLine("Arquivos locais","2 mods conferidos")}.Concat(form.ModVersionSummary())
                .Concat(new[]{VerificationLine("Tags do Nexus","11 consultas indisponíveis")});
            using(var review=new GeneralDependencyReview(summary,new GeneralDependencyIssue[0],_=>{})){
                review.StartPosition=FormStartPosition.Manual;review.Location=new Point(-30000,-30000);
                review.Show(form);
                Application.DoEvents();
                var visibleLabels=AllControls(review).OfType<Label>().Where(label=>label.Visible).ToArray();
                if(AllControls(review).Any(control=>control.Name=="ShowUnlistedInventory")||
                    !visibleLabels.Any(label=>label.Text=="Tags do Nexus"&&label.Font.Bold)||
                    !visibleLabels.Any(label=>label.Text=="11 consultas indisponíveis")||
                    !visibleLabels.Any(label=>label.Text.Contains("Nenhuma pendência local")))
                    throw new IOException("Verification must separate pending online queries and leave unlinked items out of the report.");
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

    static void CheckUpdateHistoryUi(string root) {
        string game=Directory.CreateDirectory(Safe.In(root,"history-game")).FullName;
        string appData=Safe.In(root,"history-data");
        string source="https://github.com/example/library/releases/download/v2.0/library.zip";
        var mod=NewEntry("Biblioteca de teste",source);
        mod.Variants.Add(new GeneralModVariant{Id="optional",Name="Alternativa",
            Sources=new List<string>{"https://github.com/example/library/releases/download/v2.0/optional.zip"}});
        var state=new GeneralModState{Destination=game,DownloadFolder=root,Language="pt",Theme="dark",
            List=new GeneralModList{Mods=new List<GeneralModEntry>{mod}}};
        TestForm(FixedData.Load(),state,appData,root,async form=>{
            var history=form.History();
            string backupRoot=Directory.CreateDirectory(Safe.In(appData,"BackupsAtualizacoes")).FullName;
            string backup=TestZip(backupRoot,"previous-version.zip","mods/library.txt","previous");
            var entry=new GeneralModUpdateHistory.Entry{Id=Guid.NewGuid().ToString("N"),ModId=mod.Id,
                Owner=form.MakeWork(mod).Key, Destination=game,Source=source,Version="2.0",PreviousVersion="1.0",
                Backup=Path.GetFileName(backup),InstalledUtc=DateTime.UtcNow.Ticks,Status="crash"};
            history.Save(entry,true);
            var previous=GeneralPresetProfiles.Copy(entry);
            previous.Id=Guid.NewGuid().ToString("N");
            previous.InstalledUtc=entry.InstalledUtc-TimeSpan.FromDays(1).Ticks;
            previous.Version="1.0";previous.PreviousVersion=null;previous.Status="confirmed";previous.Backup=null;
            history.Save(previous,true);
            form.updateEntries=history.Read();
            form.DrawRows();
            var row=form.ModRows().Single();
            int column=form.grid.Columns["Name"].Index;
            int x=form.grid.Columns[column].Width-12;
            if(!form.UpdateHistoryBadgeAt(row,column,x)||
                !row.Cells["Name"].ToolTipText.Contains("Atualizado recentemente"))
                throw new IOException("O mod com falha precisa manter a folhinha e a dica de atualização.");
            var click=new DataGridViewCellMouseEventArgs(column,row.Index,x,22,new MouseEventArgs(MouseButtons.Left,1,x,22,0));
            typeof(DataGridView).GetMethod("OnCellMouseClick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)
                .Invoke(form.grid,new object[]{click});
            await Until(()=>form.OwnedForms.OfType<GeneralModEditor>().Any(editor=>editor.Visible));
            var dialog=form.OwnedForms.OfType<GeneralModEditor>().Single(editor=>editor.Visible);
            var scroll=(GeneralScrollView)dialog.Controls.Find("ModEditorScroll",true).Single();
            var panel=dialog.Controls.Find("ModUpdateHistory",true).Single();
            await Until(()=>scroll.Bar.Value>0);
            if(panel.Top<1||!AllControls(panel).OfType<Label>().Any(label=>label.Text.StartsWith("1.0 → 2.0"))||
                !AllControls(panel).OfType<Label>().Any(label=>label.Text.StartsWith("Falha do jogo"))||
                !AllControls(panel).OfType<Label>().Any(label=>label.Text=="Teste confirmado · Sem backup"))
                throw new IOException("A folhinha abre diretamente o histórico, inclusive as versões anteriores e a falha.");
            scroll.ScrollTo(0);
            typeof(DataGridView).GetMethod("OnCellMouseClick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)
                .Invoke(form.grid,new object[]{click});
            await Until(()=>scroll.Bar.Value>0);
            if(form.OwnedForms.OfType<GeneralModEditor>().Count(editor=>editor.Visible)!=1)
                throw new IOException("A folhinha leva ao histórico da janela de edição já aberta.");
            var confirm=(Button)panel.Controls.Find("ConfirmModUpdate",true).Single();
            if(panel.Controls.Find("OpenModUpdateBackup",true).Length!=1)
                throw new IOException("O backup associado precisa aparecer em Atualizações recentes.");
            CaptureUi(dialog,Safe.In(root,"update-history-editor.png"));
            confirm.PerformClick();
            await Until(()=>history.Read().First().Status=="confirmed");
            row=form.ModRows().Single();
            if(form.ShowsUpdateHistoryBadge(row)||row.Cells["Name"].ToolTipText.Contains("Atualizado recentemente")||
                !File.Exists(backup)||panel.Controls.Find("ConfirmModUpdate",true).Length!=0)
                throw new IOException("Confirmar oculta a folhinha e preserva o backup e o acesso ao histórico.");
            dialog.Close();
        });
    }

    static async Task ChooseUpdateResponse(string action,bool manual=false){
        await Until(()=>Application.OpenForms.OfType<GeneralInstallConfirmation>().Any(item=>item.Visible));
        var dialog=Application.OpenForms.OfType<GeneralInstallConfirmation>().Single(item=>item.Visible);
        if(dialog.Text!="Você deseja atualizar?"||
            manual!=(dialog.Controls.Find("DeferSelectedUpdates",true).Length==0)||
            dialog.Controls.Find("ConfirmPendingInstall",true).Single().Text!="Sim"||
            dialog.Controls.Find("CancelPendingInstall",true).Single().Text!="Não")
            throw new IOException("The correct update options must be visible before opening a download.");
        ((Button)dialog.Controls.Find(action,true).Single()).PerformClick();
        await Until(()=>dialog.IsDisposed);
    }

    internal static void RunModUpdateUiTests(string parent){
        string root=OnlineTests.NestedRoot(parent,"versions-ui-");Directory.CreateDirectory(root);
        CheckInstalledVersionRows(root);
        CheckUpdateHistoryUi(root);
        string game=Directory.CreateDirectory(Safe.In(root,"dependency-game")).FullName;
        File.WriteAllText(Safe.In(game,".fixture-only"),"fixture");
        string page="https://www.nexusmods.com/fixturegame/mods/42";
        var dep=NewEntry("Shared library",page);
        dep.DependencySource=page;
        var state=new GeneralModState{Destination=game,DownloadFolder=root,Language="pt",
            List=new GeneralModList{Mods=new List<GeneralModEntry>{dep}}};
        TestForm(FixedData.Load(),state,Safe.In(root,"dependency-data"),root,async form=>{
            int reads=0;
            form.testModUpdateSession=new NexusUpdateSession("fixture-key",(GeneralNexusLink link,string key,out HttpStatusCode status)=>{
                Interlocked.Increment(ref reads);status=HttpStatusCode.OK;
                return "{\"files\":[{\"file_id\":18,\"category_id\":4,\"version\":\"1.0\"},{\"file_id\":19,\"category_id\":1,\"version\":\"2.0\",\"file_name\":\"library.zip\"}],\"file_updates\":[{\"old_file_id\":18,\"new_file_id\":19}]}";
            });
            await form.Scan();
            var entry=form.state.List.Mods.Single();
            await form.ChooseDependencyFile(form.MakeWork(entry));
            var saved=form.library.Load().List.Mods.Single().Variants.Single();
            if(reads!=1||GeneralNexusLink.Parse(saved.Sources.Single()).File!=19||
                saved.SourceInfo[saved.Sources.Single()].Version!="2.0"||File.Exists(Safe.In(game,"library.zip")))
                throw new IOException("Opening a project and selecting its sole published dependency file must persist the choice without a download.");

            var extra=form.CreateDependency(new GeneralNexusRequirement{Name="Library edition",MinFile=20,
                Source=new GeneralNexusLink{Game="fixturegame",Mod=43}},form.ActivePreset);
            form.testModUpdateSession=new NexusUpdateSession("fixture-key",(GeneralNexusLink link,string key,out HttpStatusCode status)=>{
                status=HttpStatusCode.OK;return "{\"files\":[{\"file_id\":20,\"category_id\":1,\"file_name\":\"base.zip\"},{\"file_id\":21,\"category_id\":1,\"file_name\":\"expanded.zip\"}]}";
            });
            var choosing=form.ChooseDependencyFile(form.MakeWork(extra));
            await Until(()=>form.OwnedForms.OfType<GeneralDependencyFileDialog>().Any(window=>window.Visible));
            var choice=form.OwnedForms.OfType<GeneralDependencyFileDialog>().Single(window=>window.Visible);
            var options=(GeneralPicker)choice.Controls.Find("DependencyFiles",true).Single();
            if(options.Items.Count!=2||options.SelectedIndex!=-1||((Button)choice.AcceptButton).Enabled)
                throw new IOException("Multiple archive editions must require a deliberate choice.");
            options.SelectedIndex=1;((Button)choice.AcceptButton).PerformClick();await choosing;
            var chosen=form.library.Load().List.Mods.Single(mod=>mod.Id==extra.Id).Variants.Single();
            if(GeneralNexusLink.Parse(chosen.Sources.Single()).File!=21)throw new IOException("The chosen dependency file was not saved.");
        });
        Safe.WriteJson(Path.Combine(root,"result.json"),new{Passed=true,NativeWinForms=true});
    }
}
