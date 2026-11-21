using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    internal static void RunVersionProgressTests(string parent){
        string root=OnlineTests.NestedRoot(parent,"version-progress-");Directory.CreateDirectory(root);
        var checks=new List<string>();
        Action<bool,string> check=(ok,message)=>{if(!ok)throw new IOException(message);checks.Add(message);};
        string game=Directory.CreateDirectory(Safe.In(root,"g")).FullName;
        File.WriteAllText(Safe.In(game,".fixture-only"),"test");
        Func<int,string> source=id=>"https://www.nexusmods.com/fixturegame/mods/"+id+"?tab=files&file_id=10";
        var first=NewEntry("Multiple sources",source(41));first.Variants.Single().Sources.Add(source(42));
        var second=NewEntry("Second mod",source(43));
        var state=new GeneralModState{Destination=game,DownloadFolder=root,Language="pt",Theme="dark",
            List=new GeneralModList{Mods=new List<GeneralModEntry>{first,second}}};
        TestForm(FixedData.Load(),state,Safe.In(root,"d"),root,async form=>{
            using(var firstGate=new ManualResetEventSlim())
            using(var secondGate=new ManualResetEventSlim())
            using(var thirdGate=new ManualResetEventSlim()){
                bool secondFinished=false,thirdStarted=false;
                form.testModUpdateSession=new NexusUpdateSession("fixture-key",(GeneralNexusLink link,string key,out HttpStatusCode status)=>{
                    if(link.Mod==41)firstGate.Wait(8000);
                    else if(link.Mod==42){secondGate.Wait(8000);secondFinished=true;}
                    else {thirdStarted=true;thirdGate.Wait(8000);}
                    status=HttpStatusCode.OK;
                    return "{\"files\":[{\"file_id\":10,\"category_id\":1,\"version\":\"1.0\"}],\"file_updates\":[]}";
                });
                var installed=form.MakeWork(second);
                form.Generic(game).Install(TestZip(root,"second.zip","mods/second.txt","fixture"),installed.Receipt(0),null);
                bool sawLocal=false;
                form.testStage=(key,stage)=>{
                    if(stage!="scan.begin")return;
                    sawLocal=true;
                    check(form.totalProgress.Caption.StartsWith("Mods baixados / instalados: ")&&form.totalProgress.Segments!=null,
                        "The right bar shows pack availability from the first local scan item.");
                };
                var scan=form.Scan();
                try{
                    await Until(()=>form.checkingModUpdates);
                    check(sawLocal&&form.totalProgress.Value==50&&form.totalProgress.Caption=="Mods baixados / instalados: 1 / 2"&&form.scanning&&form.currentProgress.Caption=="Verificando atualizações · 0/2 mods"&&
                        form.currentProgress.Value==0&&!form.currentProgress.Indeterminate&&!form.next.Visible,
                        "After the local scan, the left bar visibly starts version checking instead of leaving the last local item.");
                    secondGate.Set();await Until(()=>secondFinished&&thirdStarted);await Task.Delay(80);
                    check(form.currentProgress.Value==0&&form.currentProgress.Caption.Contains("0/2"),
                        "Finishing only one source of a composite mod does not count that mod as checked.");
                    thirdGate.Set();await Until(()=>form.currentProgress.Value==50);
                    check(form.currentProgress.Caption=="Verificando atualizações · 1/2 mods"&&form.scanning,
                        "Out-of-order responses advance the visible per-mod count and percentage while the scanner remains busy.");
                    form.ClientSize=new Size(1000,680);form.PerformLayout();
                    using(var font=GeneralStyle.Font(10.5f))check(TextRenderer.MeasureText(form.currentProgress.Caption,font).Width<form.currentProgress.Width-52,
                        "The phase and mod count fit the compact progress bar without hiding the counter.");
                    using(var capture=new Bitmap(form.Width,form.Height)){
                        form.DrawToBitmap(capture,new Rectangle(Point.Empty,capture.Size));capture.Save(Safe.In(root,"checking-updates.png"));
                    }
                }finally{firstGate.Set();secondGate.Set();thirdGate.Set();}
                await scan;
                check(form.totalProgress.Value==50&&form.totalProgress.Caption=="Mods baixados / instalados: 1 / 2"&&!form.scanning&&!form.checkingModUpdates&&form.currentProgress.Value==100&&
                    form.currentProgress.Caption=="Atualizações verificadas · 2/2 mods",
                    "The phase reaches 100% only when version checking and local version reads have finished.");
            }
            form.testModUpdateSession=new NexusUpdateSession(null);
            await form.RefreshModVersions(true);
            check(form.currentProgress.Caption=="Consulta de versões · 0/2 mods · 2 sem resultado"&&form.selectedFooterAction==FooterAction.ConnectNexus,
                "Missing authentication stays pending and offers connection instead of claiming the versions were checked.");

            form.versionFilter=VersionFilter.Pending;
            form.DrawRows();
            var unconfirmed=form.ModRows().Single(row=>Equals(row.Tag,second.Id));
            var footerTitle=form.versionFooter.Controls["VersionFooterTitle"];
            var noResult=form.footerNumbers[(int)VersionFilter.Pending];
            check(footerTitle.Text=="Consulta de atualizações"&&footerTitle.Visible&&footerTitle.Bottom<=noResult.Top&&
                noResult.Text=="2\nSem resultado"&&form.tips.GetToolTip(noResult).Contains("versão mais recente")&&
                Convert.ToString(unconfirmed.Cells["Status"].Value).Contains("\nConecte sua conta Nexus")&&
                GeneralStatusPresentation.Kind(unconfirmed.Cells["Status"].Tag as string)=="installed"&&
                unconfirmed.Height>form.grid.RowTemplate.Height&&
                TextRenderer.MeasureText(noResult.Text,noResult.Font).Width<=noResult.ClientSize.Width&&
                TextRenderer.MeasureText(noResult.Text,noResult.Font).Height<=noResult.ClientSize.Height,
                "The update-check heading and concise filter fit the compact footer, and the installed mod retains its badge.");
            CaptureUi(form,Safe.In(root,"version-filter.png"));
            form.versionFilter=null;
            form.DrawRows();
            check(!Convert.ToString(form.ModRows().Single(row=>Equals(row.Tag,second.Id)).Cells["Status"].Value).Contains("\n"),
                "Leaving the version filter restores the normal row.");
            foreach(bool newOperation in new[]{false,true})using(var gate=new ManualResetEventSlim()){
                form.testModUpdateSession=new NexusUpdateSession("fixture-key",(GeneralNexusLink link,string key,out HttpStatusCode status)=>{
                    gate.Wait(8000);status=HttpStatusCode.OK;return "{\"files\":[{\"file_id\":10,\"category_id\":1,\"version\":\"1.0\"}]}";
                });
                var pending=form.RefreshModVersions(true);
                await Until(()=>form.checkingModUpdates);
                if(newOperation)form.working=true;else form.ClearModVersions();
                form.currentProgress.Caption="Outra operação";form.currentProgress.Value=37;
                gate.Set();await pending;
                check(form.currentProgress.Caption=="Outra operação"&&form.currentProgress.Value==37,
                    newOperation?"A late version response does not overwrite an active installation's progress.":
                        "A discarded scan cannot repaint a newer operation's progress.");
                form.working=false;
            }

            var unsupported=NewEntry("Unsupported source","https://example.org/mod.zip");
            form.state.List.Mods.Add(unsupported);
            if(form.ActivePreset!=null)form.ActivePreset.ModIds.Add(unsupported.Id);
            const string response="{\"files\":[{\"file_id\":10,\"category_id\":1,\"version\":\"1.0\"}],\"file_updates\":[]}";
            form.testModUpdateSession=new NexusUpdateSession("fixture-key",(GeneralNexusLink link,string key,out HttpStatusCode status)=>{
                status=link.Mod==42?HttpStatusCode.ServiceUnavailable:HttpStatusCode.OK;
                return link.Mod==42?null:response;
            });
            await form.RefreshModVersions(true);
            form.versionFilter=VersionFilter.Pending;
            form.DrawRows();
            var retry=form.retryVersionCheck;
            CaptureUi(form,Safe.In(root,"version-retry.png"));
            var retrySize=TextRenderer.MeasureText(retry.Text,retry.Font);
            check(retry.Visible&&noResult.Text=="2\nSem resultado"&&
                retrySize.Width<=retry.ClientSize.Width&&retrySize.Height<=retry.ClientSize.Height,
                "The recheck action fits above the unresolved filter at minimum width. Visible="+retry.Visible+
                "; count="+noResult.Text+"; text="+retrySize+"; available="+retry.ClientSize);
            var requests=new System.Collections.Concurrent.ConcurrentQueue<int>();
            int scans=0;
            form.testStage=(key,stage)=>{if(stage=="scan.begin")scans++;};
            using(var gate=new ManualResetEventSlim()){
                form.testModUpdateSession=new NexusUpdateSession("fixture-key",(GeneralNexusLink link,string key,out HttpStatusCode status)=>{
                    requests.Enqueue(link.Mod);gate.Wait(8000);status=HttpStatusCode.OK;return response;
                });
                typeof(LinkLabel).GetMethod("OnLinkClicked",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)
                    .Invoke(retry,new object[]{new LinkLabelLinkClickedEventArgs(retry.Links[0])});
                try{
                    await Until(()=>form.checkingModUpdates&&!requests.IsEmpty);
                    check(form.currentProgress.Caption=="Reconsultando atualizações · 0/1 mods"&&
                        !form.scanning&&scans==0,"Rechecking starts its own progress without running the local scan.");
                }finally{gate.Set();}
                await Until(()=>!form.checkingModUpdates);
            }
            check(requests.SequenceEqual(new[]{42})&&scans==0&&form.CountVersions(true).Checked==2&&
                form.currentProgress.Caption=="Nova consulta · 1/1 com resultado"&&form.currentProgress.Value==100&&
                noResult.Text=="1\nSem resultado"&&!retry.Visible&&Equals(form.ModRows().Single().Tag,unsupported.Id),
                "Retry queries only the unresolved supported source, retains earlier successes, and leaves unsupported mods in the filter.");
        });
        VersionSourceReviewDoesNotOpenGithub(Safe.In(root,"review"),check);
        Safe.WriteJson(Safe.In(root,"result.json"),new{Passed=true,Checks=checks,NativeWinForms=true,RealGameTouched=false,LiveNetwork=false});
    }

    static void VersionSourceReviewDoesNotOpenGithub(string root,Action<bool,string> check){
        Directory.CreateDirectory(root);string game=Directory.CreateDirectory(Safe.In(root,"g")).FullName;
        File.WriteAllText(Safe.In(game,".fixture-only"),"test");
        var item=NewEntry("Original vendor","https://github.com/example/vendor/releases/download/v1.0/vendor-windows.zip");
        var state=new GeneralModState{Destination=game,DownloadFolder=root,Language="pt",Theme="dark",List=new GeneralModList{Mods=new List<GeneralModEntry>{item}}};
        TestForm(FixedData.Load(),state,Safe.In(root,"d"),root,async form=>{
            string archive=TestZip(root,"old.zip","mods/example.txt","old");var work=form.MakeWork(form.state.List.Mods.Single());
            form.Generic(game).Install(archive,work.Receipt(0),null);
            form.testModUpdateSession=new NexusUpdateSession(null);
            form.testGithubUpdateSession=new GithubModUpdateSession(_=>"{\"tag_name\":\"v2.0\",\"draft\":false,\"prerelease\":false}");
            await form.Scan();await WaitForVersionCheck(form);
            form.updateFocusedId=work.Entry.Id;form.RefreshPrimaryAction();
            var row=form.ModRows().Single();
            check(form.primaryAction.Text=="Revisar atualização"&&Convert.ToString(row.Cells["Status"].Value)=="Revisar · v1.0"&&
                form.SelectedSourceReviews().Single().Id==work.Entry.Id&&form.SelectedUpdates().Count==0,
                "An unbound provider update is offered as review, with its installed version, rather than as a browser download.");
            var alternative=new GeneralModVariant{Id="alternate",Name="Alternative",
                Sources=new List<string>{"https://example.org/alternate.zip"}};
            work.Entry.Variants.Add(alternative);
            form.DrawRows();
            check(((string)form.ModRows().Single().Cells["Status"].Value).StartsWith("Instalado",StringComparison.Ordinal)&&
                form.SelectedSourceReviews().Single().Id==work.Entry.Id,
                "The folder shows its active version as installed while the source review remains available.");
            form.state.ChosenVariants[work.Entry.Id]=alternative.Id;
            form.statuses[Key(work.Entry,alternative)]="Não instalado";
            form.DrawRows();
            check(Equals(form.ModRows().Single().Cells["Status"].Value,"Não instalado"),
                "A folder does not appear installed just because a different version is installed.");
            form.state.ChosenVariants.Remove(work.Entry.Id);
            work.Entry.Variants.Remove(alternative);
            form.DrawRows();
            int launches=0;form.launch=_=>launches++;
            form.StartSelected();
            await Until(()=>Application.OpenForms.OfType<GeneralSourceReviewDialog>().Any(dialog=>dialog.Visible));
            var firstReview=Application.OpenForms.OfType<GeneralSourceReviewDialog>().Single(dialog=>dialog.Visible);
            bool kept=firstReview.Controls.Find("RecheckReviewedUpdate",true).Single().Enabled&&
                !firstReview.Controls.Find("ApplyReviewedUpdate",true).Single().Visible;
            ((Button)firstReview.Controls.Find("KeepInstalledVersion",true).Single()).PerformClick();
            await Until(()=>firstReview.IsDisposed);
            check(kept&&launches==0&&File.ReadAllText(Safe.In(game,"mods/example.txt"))=="old",
                "Review gives a clear keep action and offers a recheck when it has no confirmed archive.");

            string next=TestZip(root,"vendor-core-windows.zip","mods/example.txt","new");
            const string nextSource="https://github.com/example/vendor/releases/download/v2.0/vendor-core-windows.zip";
            string release=Safe.Json.Serialize(new{tag_name="v2.0",draft=false,prerelease=false,
                assets=new[]{new{name=Path.GetFileName(next),state="uploaded",size=new FileInfo(next).Length,
                    digest="sha256:"+Safe.Hash(next),browser_download_url=nextSource}}});
            form.testGithubPackage=source=>GeneralGithubUpdatePackage.Read(source,null,_=>release);
            int step=0;
            using(var flow=new System.Windows.Forms.Timer{Interval=50}){
                flow.Tick+=(s,e)=>{
                    var dialog=Application.OpenForms.OfType<GeneralSourceReviewDialog>().FirstOrDefault();
                    if(step==0&&dialog!=null){
                        var versions=dialog.Controls.Find("ReviewVersions",true).Single().Text;
                        if(!versions.Contains("v1.0")||!versions.Contains("v2.0"))return;
                        form.testGithubUpdateSession=new GithubModUpdateSession(_=>release);
                        step=1;
                        ((Button)dialog.Controls.Find("RecheckReviewedUpdate",true).Single()).PerformClick();
                    }else if(step==1&&dialog!=null){
                        var archives=(GeneralPicker)dialog.Controls.Find("ReviewArchives",true).Single();
                        if(!archives.Visible||archives.Items.Count!=2)return;
                        archives.SelectedIndex=1;
                        CaptureUi(dialog,Safe.In(root,"review-action.png"));
                        step=2;
                        ((Button)dialog.Controls.Find("ApplyReviewedUpdate",true).Single()).PerformClick();
                    }else if(step==2){
                        var confirm=Application.OpenForms.OfType<GeneralInstallConfirmation>().FirstOrDefault();
                        if(confirm==null)return;
                        step=3;flow.Stop();
                        ((Button)confirm.Controls.Find("ConfirmPendingInstall",true).Single()).PerformClick();
                    }
                };
                flow.Start();form.StartSelected();
                await Until(()=>step==3&&!form.working&&form.state.List.Mods.Single().Variants.Single().Sources.Single()==nextSource);
            }
            check(launches==0&&File.ReadAllText(Safe.In(game,"mods/example.txt"))=="new",
                "A recheck refreshes the same dialog, permits selecting a verified asset, and installs it through the normal queue.");
        });
    }
}
