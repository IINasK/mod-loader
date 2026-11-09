using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
        const string alternate="https://www.nexusmods.com/cyberpunk2077/mods/33230?tab=files&file_id=162991";
        state.List.Mods[0].Variants.Add(new GeneralModVariant{Id="alternate",Name="Alternate",Sources=new List<string>{alternate}});
        new GeneralModInstaller(game,Path.Combine(data,"Instalacoes")).Install(archive,"fixture/default/0/"+archive,_=>{});File.Delete(archive);
        var library=new GeneralModLibrary(Path.Combine(root,"state.json"));library.Save(state);check(library.Load().Theme=="black","Black theme persists in project state.");var projects=new GeneralProjectStore(Path.Combine(root,"projects"));projects.SavePreferences("black","pt",null);check(projects.Read().Theme=="black","Black theme persists in the home preferences.");
        using(var sender=new P2PService(Path.Combine(root,"sender"),Path.Combine(root,"send-files"),true))using(var receiver=new P2PService(Path.Combine(root,"receiver"),Path.Combine(root,"receive-files"),true)){
            sender.EnableInstalledReceive(catalog,data,true);receiver.EnableInstalledReceive(catalog,receivedData,true);sender.Start(_=>{},helper);receiver.Start(_=>{},helper);
            int hostChoices=0,receivedSounds=0,hostFinished=0;
            sender.Notice=complete=>{if(complete)System.Threading.Interlocked.Increment(ref hostFinished);else System.Threading.Interlocked.Increment(ref hostChoices);};
            receiver.Notice=complete=>{if(complete)System.Threading.Interlocked.Increment(ref receivedSounds);};
            var libraryReceived=new GeneralProjectStore(Path.Combine(root,"p"));
            receiver.RegisterProject=session=>libraryReceived.CreateReceived(session,receivedData,catalog);
            receiver.ReviewReceived=list=>GeneralNexusInfo.ReviewReceivedList(list,url=>Task.FromResult(
                new GeneralSourceInfo{Name="Mod from Nexus",ModVersion="1.5",NexusChecked=true,Tags=new[]{"From Nexus"}}));
            Exception failure=null;P2PSession sent=null,accepted=null;
            using(var host=new Form{ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-30000,-30000)})using(var dialog=new GeneralP2PDialog(sender,state,catalog)){
                dialog.ShowInTaskbar=false;dialog.StartPosition=FormStartPosition.Manual;dialog.Location=new Point(-30000,-30000);
                sender.ConfirmConnection=session=>GeneralP2PDialog.AskConnection(host,()=>dialog.Visible?(Control)dialog:host,sender,session);
                host.Shown+=async(s,e)=>{try{
                    dialog.Show(host);var output=(TextBox)dialog.Content.Controls.Find("p2p-output",true).Single();var copy=(Button)dialog.Content.Controls.Find("p2p-copy",true).Single();
                    await Wait(()=>copy.Enabled,"No automatic invitation appeared.");sent=sender.Sessions.Single();string offer=output.Text;copy.PerformClick();
                    check(Clipboard.GetText()==offer&&sent.Automatic,"Opening fills a copy-only automatic invitation.");
                    await Task.Delay(1300);check(sent.PayloadHash==null&&!Directory.GetFiles(sender.Folder(sent),"*.zip*").Any(),"Leaving the dialog open without a friend creates no ZIP or partial ZIP.");
                    check(dialog.Content.Controls.Find("p2p-card",true).Length==0,"An unaccepted invitation does not create a transfer card.");Capture(dialog,Path.Combine(root,"waiting-black.png"));
                    dialog.Hide();
                    accepted=receiver.Accept(offer,target,true);receiver.Engine.ConfigurePeer(sender.Engine.Device,"tcp://127.0.0.1:"+sender.Engine.ListenPort);
                    await Wait(()=>Application.OpenForms.Cast<Form>().Any(form=>form.Controls.Find("p2p-accept-request",true).Length!=0),"Host did not receive a connection request.");
                    check(sent.Recipients.Length==0&&sent.PayloadHash==null&&hostChoices==1,"A proven invite plays a single sound and waits for the host.");
                    var approve=Application.OpenForms.Cast<Form>().SelectMany(form=>form.Controls.Find("p2p-accept-request",true)).OfType<Button>().Single();
                    approve.PerformClick();
                    await Wait(()=>sent.Recipients.Length!=0,"Host did not approve the request without a reply code.");
                    check(sent.Recipients.Single().Device==receiver.Engine.Device,"Host approval pins the proven recipient without a reply code.");
                    await Wait(()=>receiver.HasSyncPreview(accepted)||sent.Error!=null,"Host did not publish the comparison.");
                    check(sent.Error==null&&sent.PayloadHash==null,"The host waits for the receiver's choice before building the pack.");
                    await receiver.ChooseIncoming(accepted,true);
                    await Wait(()=>sent.PayloadHash!=null||sent.Error!=null,"Preparation did not start after the recipient chose a full pack.");check(sent.PayloadHash!=null&&sent.Error==null,"Background service starts preparation even with its dialog hidden.");
                    using(var reopened=new GeneralP2PDialog(sender,state,catalog,false)){
                        reopened.Show(host);
                        await Wait(()=>reopened.Content.Controls.Find("p2p-card",true).Length==1,"The transfer was not reconstructed on reopening.");
                        check(reopened.Content.Controls.Find("p2p-transfers",true).Length==0,"Reopening shows the ongoing transfer as a card without a session selector.");
                        reopened.Close();
                    }
                    dialog.Show();Capture(dialog,Path.Combine(root,"connected-black.png"));
                    await Wait(()=>accepted.Completed||accepted.Error!=null,"Automatic receive did not complete.");check(accepted.Completed&&accepted.Error==null&&File.ReadAllText(Path.Combine(target,"mods/test.txt"))=="automatic acceptance","Receiver installs verified files automatically after one-sided acceptance.");
                    await Wait(()=>sent.Completed&&accepted.ListImported&&!Directory.Exists(sender.Folder(sent))&&!Directory.Exists(receiver.Folder(accepted)),"Automatic transfer temporary cleanup did not finish.");
                    check(receivedSounds==2&&hostFinished==1,"Download, installation and confirmed send signal completion once per milestone.");
                    var imported=libraryReceived.Load(libraryReceived.Read().Projects.Single());
                    var mod=imported.List.Mods.Single();
                    check(mod.Tags.Contains("From Nexus")&&mod.Variants.Single(v=>v.Id=="alternate").SourceInfo[alternate].NexusChecked&&
                        imported.SnapshotReceipts.ContainsKey(mod.Id+"/"+mod.DefaultVariant),
                        "The whole imported list is reviewed, including alternate Nexus sources, and installed mods retain Mod Loader recognition.");
                }catch(Exception ex){failure=ex;}if(!dialog.IsDisposed)dialog.Dispose();if(!host.IsDisposed)host.Close();};Application.Run(host);
            }
            if(failure!=null)throw failure;
            // The same trusted device still needs a fresh invitation proof for the next pack.
            sender.SetPreparationState(null);
            int declined=0;
            sender.ConfirmConnection=session=>{
                System.Threading.Interlocked.Increment(ref declined);
                return Task.FromResult(false);
            };
            var fallback=sender.BeginDeferred(state,new[]{"fixture"},true,compareBeforePack:true);
            var fallbackReceive=receiver.Accept(sender.Invitation(fallback),target,true);
            Wait(()=>declined==1,"A known friend did not request approval for the new invitation.").GetAwaiter().GetResult();
            System.Threading.Thread.Sleep(2100);
            check(declined==1&&fallback.Recipients.Length==0&&fallback.PayloadHash==null,"Declining stops transfer and does not repeat the request.");
            var reply=P2PInvite.Decode(receiver.Invitation(fallbackReceive));
            string valid=reply.Encode();
            reply.ManifestHash=new string('d',64);
            bool rejected=false;
            try{sender.Authorize(reply.Encode());}catch(IOException){rejected=true;}
            check(rejected&&fallback.Recipients.Length==0,"Manual fallback requires the correct invitation secret.");
            sender.Authorize(valid);sender.Authorize(valid);
            check(fallback.Recipients.Length==1&&fallback.Recipients[0].Device==receiver.Engine.Device,"Copy reply authorizes only this recipient and is idempotent.");
            sender.CancelSend(fallback);
            receiver.DiscardIncoming(fallbackReceive);
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
