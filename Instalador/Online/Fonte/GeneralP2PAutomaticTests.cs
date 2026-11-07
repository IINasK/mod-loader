using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class GeneralP2PAutomaticTests {
    internal static int Run(string parent,string helper){
        string root=OnlineTests.NestedRoot(parent,"automatic-");Directory.CreateDirectory(root);var checks=new List<string>();
        Action<bool,string> check=(ok,text)=>{if(!ok)throw new IOException(text);checks.Add(text);};
        string key=new string('a',64),folder="ml-"+new string('b',32),device=String.Join("-",Enumerable.Repeat("AAAAAAA",8));string proof=P2PService.AcceptanceProof(key,folder,device);
        check(P2PService.AcceptanceMatches(proof,key,folder,device),"Invitation capability authenticates the accepting TLS device.");
        check(!P2PService.AcceptanceMatches(proof,new string('c',64),folder,device),"A pending device without the invitation secret cannot join.");
        check(!P2PService.AcceptanceMatches(proof,key,"ml-"+new string('c',32),device),"Acceptance cannot be replayed into a different session.");
        check(!P2PService.AcceptanceMatches(proof,key,folder,String.Join("-",Enumerable.Repeat("BBBBBBB",8))),"Acceptance proof is bound to its exact TLS device.");
        GeneralStyle.UseTheme("black");GeneralStyle.AnimationsEnabled=false;
        foreach(var color in new[]{GeneralStyle.Background,GeneralStyle.Surface,GeneralStyle.Foreground,GeneralStyle.Muted,GeneralStyle.Accent,GeneralStyle.Primary,GeneralStyle.Line,GeneralStyle.SoftLine,GeneralStyle.Selection})check(color.R==color.G&&color.G==color.B,"Black theme structural color is neutral grayscale: "+color.ToArgb());
        var catalog=new FixedCatalog{Schema=1,Id="automatic-fixture",GameVersion="2.5",GameFileVersion="2.5.0.0",Packages=new List<FixedMod>(),Defaults=new List<FixedDefault>()};
        string game=Path.Combine(root,"game"),target=Path.Combine(root,"target"),data=Path.Combine(root,"data"),receivedData=Path.Combine(root,"received"),archive=Path.Combine(root,"original.zip");Directory.CreateDirectory(game);Directory.CreateDirectory(target);File.WriteAllText(Path.Combine(target,".fixture-only"),"test");
        using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create))using(var output=new StreamWriter(zip.CreateEntry("mods/test.txt").Open()))output.Write("automatic acceptance");
        var state=new GeneralModState{Destination=game,Theme="black"};state.List.Mods.Add(new GeneralModEntry{Id="fixture",Name="Fixture",DefaultVariant="default",Variants=new List<GeneralModVariant>{new GeneralModVariant{Id="default",Name="Main",Sources=new List<string>{archive}}}});
        new GeneralModInstaller(game,Path.Combine(data,"Instalacoes")).Install(archive,"fixture/default/0/"+archive,_=>{});File.Delete(archive);
        var library=new GeneralModLibrary(Path.Combine(root,"state.json"));library.Save(state);check(library.Load().Theme=="black","Black theme persists in project state.");var projects=new GeneralProjectStore(Path.Combine(root,"projects"));projects.SavePreferences("black","pt",null);check(projects.Read().Theme=="black","Black theme persists in the home preferences.");
        using(var sender=new P2PService(Path.Combine(root,"sender"),Path.Combine(root,"send-files"),true))using(var receiver=new P2PService(Path.Combine(root,"receiver"),Path.Combine(root,"receive-files"),true)){
            sender.EnableInstalledReceive(catalog,data,true);receiver.EnableInstalledReceive(catalog,receivedData,true);sender.Start(_=>{},helper);receiver.Start(_=>{},helper);
            Exception failure=null;P2PSession sent=null,accepted=null;
            using(var host=new Form{ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-30000,-30000)})using(var dialog=new GeneralP2PDialog(sender,state,new[]{"fixture"},catalog,new DownloadCache(Path.Combine(root,"cache")))){
                dialog.ShowInTaskbar=false;dialog.StartPosition=FormStartPosition.Manual;dialog.Location=new Point(-30000,-30000);
                host.Shown+=async(s,e)=>{try{
                    dialog.Show(host);var output=(TextBox)dialog.Content.Controls.Find("p2p-output",true).Single();var copy=(Button)dialog.Content.Controls.Find("p2p-copy",true).Single();
                    await Wait(()=>copy.Enabled,"No automatic invitation appeared.");sent=sender.Sessions.Single();string offer=output.Text;copy.PerformClick();
                    check(Clipboard.GetText()==offer&&sent.Automatic,"Opening fills a copy-only automatic invitation.");
                    await Task.Delay(1300);check(sent.PayloadHash==null&&!Directory.GetFiles(sender.Folder(sent),"*.zip*").Any(),"Leaving the dialog open without a friend creates no ZIP or partial ZIP.");
                    var bar=(GeneralProgress)dialog.Content.Controls.Find("p2p-progress",true).Single();check(!bar.Visible,"No download loading bar is shown before acceptance.");Capture(dialog,Path.Combine(root,"waiting-black.png"));
                    dialog.Hide();
                    accepted=receiver.Accept(offer,target,true);receiver.Engine.ConfigurePeer(sender.Engine.Device,"tcp://127.0.0.1:"+sender.Engine.ListenPort);
                    await Wait(()=>sent.Recipients.Length!=0,"Host did not detect acceptance without a reply code.");check(sent.Recipients.Single().Device==receiver.Engine.Device,"Host pins the proven recipient automatically without manual Authorize or reply paste.");
                    await Wait(()=>sent.PayloadHash!=null||sent.Error!=null,"Preparation did not start after the recipient connected.");check(sent.PayloadHash!=null&&sent.Error==null,"Background service starts preparation even with its dialog hidden.");
                    using(var reopened=new GeneralP2PDialog(sender,state,new[]{"fixture"},catalog,new DownloadCache(Path.Combine(root,"cache")),false)){
                        var selected=(GeneralPicker)reopened.Content.Controls.Find("p2p-transfers",true).Single();
                        check(selected.SelectedIndex==0&&sender.ActiveSelection==sent.Id,"A new sharing dialog selects the same ongoing transfer.");
                    }
                    dialog.Show();Capture(dialog,Path.Combine(root,"connected-black.png"));
                    await Wait(()=>accepted.Completed||accepted.Error!=null,"Automatic receive did not complete.");check(accepted.Completed&&accepted.Error==null&&File.ReadAllText(Path.Combine(target,"mods/test.txt"))=="automatic acceptance","Receiver installs verified files automatically after one-sided acceptance.");
                    await Wait(()=>sent.Completed&&!Directory.Exists(sender.Folder(sent))&&!Directory.Exists(receiver.Folder(accepted)),"Automatic transfer temporary cleanup did not finish.");check(true,"Both sides remove temporary transfer files after confirmation.");
                }catch(Exception ex){failure=ex;}while((bool)typeof(GeneralP2PDialog).GetField("preparing",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(dialog))await Task.Delay(30);dialog.Dispose();host.Close();};Application.Run(host);
            }
            if(failure!=null)throw failure;
            // The same trusted device still needs a fresh invitation proof for the next pack.
            var second=sender.BeginDeferred(state,new[]{"fixture"});second.Automatic=false;string previous=sender.Invitation(second);var incoming=receiver.Accept(previous,target,true);
            check(sender.UpgradeInvitation(second)&&sender.Invitation(second)!=previous,"Unbound legacy invitations upgrade to automatic acceptance and display an updated code.");
            var upgraded=receiver.Accept(sender.Invitation(second),target,true);check(Object.ReferenceEquals(upgraded,incoming)&&incoming.Automatic,"Receiver upgrades its existing pending session when the updated invitation is pasted, without duplicate files.");
            receiver.Engine.ConfigurePeer(sender.Engine.Device,"tcp://127.0.0.1:"+sender.Engine.ListenPort);
            Wait(()=>second.Peer==receiver.Engine.Device,"Existing device did not authorize its new session via the pending-folder proof.").GetAwaiter().GetResult();check(true,"Already known TLS devices authenticate the next invitation without a new reply code.");
            receiver.Pause(incoming);Wait(()=>sender.TransferStatus(second).RemotePaused,"Sender did not detect the recipient's preparation pause.").GetAwaiter().GetResult();
            var work=Task.Run(()=>sender.PrepareDeferred(second,state,catalog,data,true,_=>{}));
            Wait(()=>second.Stage!=null,"Paused preparation did not begin.").GetAwaiter().GetResult();System.Threading.Thread.Sleep(400);check(!work.IsCompleted&&second.PayloadHash==null&&!second.Paused,"Preparation checkpoints honor the recipient's remote pause before writing payload bytes.");
            sender.Pause(second);receiver.Pause(incoming);System.Threading.Thread.Sleep(1200);check(!work.IsCompleted&&second.Paused&&!incoming.Paused,"Releasing the recipient's pause does not release the sender's own preparation pause.");
            sender.Pause(second);work.GetAwaiter().GetResult();check(second.PayloadHash!=null&&!second.Paused,"Sender can resume its paused preparation and finish the same pack.");
            Wait(()=>incoming.Completed||incoming.Error!=null,"Second automatic receive failed.").GetAwaiter().GetResult();check(incoming.Completed&&incoming.Error==null,"Second automatic session installs after resuming preparation.");
            Wait(()=>second.Completed&&!Directory.Exists(sender.Folder(second))&&!Directory.Exists(receiver.Folder(incoming)),"Second transfer did not clean temporary files.").GetAwaiter().GetResult();
        }
        Safe.WriteJson(Path.Combine(root,"resultado-p2p-automatic.json"),new{Passed=true,Tests=checks,LoopbackOnly=true,RealGameTouched=false});return checks.Count;
    }
    static async Task Wait(Func<bool> ready,string failure){var watch=Stopwatch.StartNew();while(!ready()){if(watch.ElapsedMilliseconds>65000)throw new IOException(failure);await Task.Delay(50);}}
    static void Capture(Form form,string file){using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(file);}}
}
