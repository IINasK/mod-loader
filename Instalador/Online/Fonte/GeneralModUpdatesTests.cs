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
        check(result.State=="update"&&result.Version=="1.2"&&result.Url.Contains("skyrimspecialedition/mods/42"),"Follow author-declared update chain preserving game/mod identity");
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
    internal static void RunModUpdateUiTests(string parent){
        string root=OnlineTests.NestedRoot(parent,"versions-ui-");Directory.CreateDirectory(root);
        int checks=0;
        foreach(string language in new[]{"pt","en"}){
            var state=new GeneralModState{Language=language,Theme="dark",DownloadFolder=root};
            TestForm(FixedData.Load(),state,Path.Combine(root,language),root,form=>{
                form.modUpdates=new List<ModUpdateRow>{
                    new ModUpdateRow{Name="Mod de exemplo",Key="example/main",Source="https://www.nexusmods.com/skyrimspecialedition/mods/42?tab=files&file_id=10",Expected="1.0",Local="2.0",LocalStatus=GeneralModInstaller.Modified,Online=new NexusUpdateResult{State="update",Version="2.1",Url="https://www.nexusmods.com/skyrimspecialedition/mods/42?tab=files&file_id=12"}},
                    new ModUpdateRow{Name="Sem autenticação",Key="unknown/main",Online=NexusUpdateResult.Unknown("connect")}
                };
                form.ShowModUpdateNotice();
                var manualNewer=new ModUpdateRow{Local="3.0",LocalStatus=GeneralModInstaller.Modified,Online=new NexusUpdateResult{State="update",Version="2.1"}};
                if(!LocalAtLeastOnline(manualNewer)||!UpdateCaption(manualNewer).Contains(language=="pt"?"conferir":"verify"))throw new IOException("Newer manual version was called outdated");checks++;
                if(!form.modUpdateNotice.Text.Contains(language=="pt"?"Conectar":"Connect"))throw new IOException("Missing connection notice");checks++;
                Exception failed=null;int stage=0;var until=DateTime.UtcNow.AddSeconds(10);
                using(var timer=new System.Windows.Forms.Timer{Interval=80}){
                    timer.Tick+=(s,e)=>{
                        var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.Name=="ModVersions");
                        try{
                            if(DateTime.UtcNow>until)throw new IOException("Versions dialog timeout");
                            if(dialog==null)return;
                            var table=dialog.Controls.Find("ModVersionResults",true).OfType<DataGridView>().Single();
                            if(stage==0){
                                if(table.Rows.Count!=2||!Convert.ToString(table.Rows[0].Cells[2].Value).Contains(language=="pt"?"mais nova":"Newer"))throw new IOException("Manual newer version not shown");checks++;
                                foreach(var button in AllControls(dialog).OfType<Button>()){
                                    var bounds=dialog.RectangleToClient(button.RectangleToScreen(button.ClientRectangle));
                                    if(bounds.Left<0||bounds.Top<0||bounds.Right>dialog.ClientSize.Width||bounds.Bottom>dialog.ClientSize.Height)throw new IOException("Versions button clipped: "+button.Text);
                                }
                                checks++;
                                using(var capture=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(capture,new Rectangle(Point.Empty,capture.Size));capture.Save(Path.Combine(root,"versions-"+language+".png"));}
                                stage++;
                                AllControls(dialog).OfType<Button>().Single(b=>b.Text==(language=="pt"?"Revisar alteração":"Review change")).PerformClick();
                            }else{
                                var choice=Application.OpenForms.OfType<GeneralVersionChoiceDialog>().FirstOrDefault();
                                if(choice==null)return;
                                var replace=choice.Controls.Find("UseIncomingVersion",true).OfType<Button>().Single();
                                if(replace.Enabled)throw new IOException("Advisory review permitted unverified replacement");checks++;
                                choice.Controls.Find("KeepInstalledVersion",true).OfType<Button>().Single().PerformClick();
                                timer.Stop();dialog.Close();
                            }
                        }catch(Exception ex){failed=ex;timer.Stop();foreach(var open in Application.OpenForms.Cast<Form>().Where(f=>f!=form).ToArray())open.Close();}
                    };
                    timer.Start();form.ShowModUpdates();timer.Stop();
                }
                if(failed!=null)throw failed;
                if(stage!=1)throw new IOException("Review flow incomplete");
                // Opening and going back again must not lose the observations.
                if(form.modUpdates[0].Local!="2.0"||form.modUpdates[0].LocalStatus!=GeneralModInstaller.Modified)throw new IOException("Review altered evidence");checks++;
                return Task.FromResult(0);
            });
        }
        Safe.WriteJson(Path.Combine(root,"result.json"),new{Passed=true,Checks=checks,NativeWinForms=true});
    }
}
