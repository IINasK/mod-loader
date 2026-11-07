using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class GeneralP2PDeferredTests {
    internal static int Run(string parent,string helper){
        string root=OnlineTests.NestedRoot(parent,"deferred-");Directory.CreateDirectory(root);var checks=new List<string>();
        Action<bool,string> check=(ok,text)=>{if(!ok)throw new IOException(text);checks.Add(text);};
        Action<Action,string> reject=(action,text)=>{try{action();}catch(IOException){checks.Add(text);return;}throw new IOException("Not rejected: "+text);};
        if(Safe.Hash(helper)!=SyncthingEngine.ExeHash)throw new IOException("Auxiliar oficial divergente.");
        var catalog=new FixedCatalog{Schema=1,Id="deferred-fixture",GameVersion="2.5",GameFileVersion="2.5.0.0",Packages=new List<FixedMod>(),Defaults=new List<FixedDefault>()};
        string game=Path.Combine(root,"source"),data=Path.Combine(root,"data"),receivedData=Path.Combine(root,"received-data"),target=Path.Combine(root,"target"),archive=Path.Combine(root,"original.zip");Directory.CreateDirectory(game);Directory.CreateDirectory(target);File.WriteAllText(Path.Combine(target,".fixture-only"),"test");
        using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create))using(var writer=new StreamWriter(zip.CreateEntry("mods/fixture.txt").Open()))writer.Write("deferred transfer");
        var state=new GeneralModState{Destination=game};state.List.Mods.Add(new GeneralModEntry{Id="fixture",Name="Fixture",DefaultVariant="default",Variants=new List<GeneralModVariant>{new GeneralModVariant{Id="default",Name="Main",Sources=new List<string>{archive}}}});
        new GeneralModInstaller(game,Path.Combine(data,"Instalacoes")).Install(archive,"fixture/default/0/"+archive,_=>{});File.Delete(archive);
        string key=new string('a',64),hash=new string('b',64),id="ml-"+new string('c',32);var ready=new P2PService.ReadyPack{Hash=hash,Size=100};ready.Tag=P2PService.ReadyTag(key,id,hash,100);string descriptor=Path.Combine(root,"ready-test.json");Safe.WriteJson(descriptor,ready);
        check(P2PService.ReadReady(descriptor,key,id).Hash==hash,"Authenticated readiness descriptor is accepted.");
        reject(()=>P2PService.ReadReady(descriptor,new string('d',64),id),"Descriptor from a different invitation is rejected.");reject(()=>P2PService.ReadReady(descriptor,key,"ml-"+new string('d',32)),"Descriptor cannot be replayed into another session.");ready.Size=101;Safe.WriteJson(descriptor,ready);reject(()=>P2PService.ReadReady(descriptor,key,id),"Modified descriptor size is rejected.");ready.Size=100;ready.Hash=new string('e',64);Safe.WriteJson(descriptor,ready);reject(()=>P2PService.ReadReady(descriptor,key,id),"Modified payload hash is rejected.");File.WriteAllText(descriptor,new string('x',2049));reject(()=>P2PService.ReadReady(descriptor,key,id),"Oversized descriptor is rejected before parsing.");
        using(var sender=new P2PService(Path.Combine(root,"sender"),Path.Combine(root,"send-files"),true))using(var receiver=new P2PService(Path.Combine(root,"receiver"),Path.Combine(root,"receive-files"),true)){
            sender.EnableInstalledReceive(catalog,data,true);receiver.EnableInstalledReceive(catalog,receivedData,true);sender.Start(_=>{},helper);receiver.Start(_=>{},helper);
            state.Destination=Path.Combine(root,"temporarily-unavailable");var sent=sender.BeginDeferred(state,new[]{"fixture"});string offer=sender.Invitation(sent);var decoded=P2PInvite.Decode(offer);
            check(decoded.Deferred&&decoded.Size==0&&decoded.Kind=="offer"&&!File.Exists(Path.Combine(sender.Folder(sent),"pack.zip")),"Valid connection invitation exists before inspecting or compressing game files.");
            reject(()=>sender.PrepareDeferred(sent,state,catalog,data,true,_=>{}),"Unavailable game files defer preparation without invalidating the invitation.");
            check(sender.Invitation(sent)==offer&&sent.PayloadHash==null&&!sent.Completed,"Preparation failure preserves the same live invitation.");
            var accepted=receiver.Accept(offer,target,true);string answer=receiver.Invitation(accepted);sender.Authorize(answer);sent.Error=null;
            sender.Engine.ConfigurePeer(receiver.Engine.Device,"tcp://127.0.0.1:"+receiver.Engine.ListenPort);receiver.Engine.ConfigurePeer(sender.Engine.Device,"tcp://127.0.0.1:"+sender.Engine.ListenPort);
            Thread.Sleep(3400);check(!sent.Completed&&!accepted.Completed&&accepted.Error==null&&!File.Exists(Safe.In(target,"mods/fixture.txt")),"Empty pending folder cannot be mistaken for a completed transfer or installation.");
            CheckCopyDialog(sender,state,catalog,data,root,checks);
            state.Destination=game;sender.PrepareDeferred(sent,state,catalog,data,true,_=>{});
            check(sender.Invitation(sent)==offer&&receiver.Invitation(accepted)==answer&&sent.PayloadHash!=sent.Hash&&sent.Size>0,"Invitation and reply remain stable after the real ZIP and its hash are ready.");
            using(var restored=new P2PService(sender.Root,sender.Store,true)){var saved=restored.Sessions.Single(item=>item.Id==sent.Id);check(saved.Deferred&&saved.Hash==sent.Hash&&saved.PayloadHash==sent.PayloadHash&&saved.Size==sent.Size&&saved.SelectedMods.SequenceEqual(new[]{"fixture"}),"Invitation identity, authenticated payload hash and selection persist for resume.");}
            Wait(()=>accepted.Completed||accepted.Error!=null,"Deferred receive did not finish.");check(accepted.Completed&&accepted.Error==null&&accepted.PayloadHash==sent.PayloadHash&&accepted.ReceiptId!=null,"Receiver authenticates the ready descriptor and installs the pinned ZIP.");
            check(File.ReadAllText(Safe.In(target,"mods/fixture.txt"))=="deferred transfer","Received files match the installed source without retaining its original archive.");
            try{Wait(()=>sent.Completed&&!Directory.Exists(sender.Folder(sent))&&!Directory.Exists(receiver.Folder(accepted)),"Deferred transfer cleanup did not finish.");}catch{try{Safe.WriteJson(Path.Combine(root,"cleanup-state.json"),new{Sender=sender.Engine.Get("db/completion?folder="+sent.Id+"&device="+sent.Peer)});}catch(Exception){}throw;}check(true,"Both temporary ZIP and readiness descriptor are removed after completion.");
            var receipt=new InstalledSnapshotInstaller(target,receivedData,catalog,true);check(receipt.Status(accepted.ReceiptId,"fixture/default")==GeneralModInstaller.Installed,"Installed receipt remains valid after deleting transfer files.");
        }
        Safe.WriteJson(Path.Combine(root,"resultado-p2p-deferred.json"),new{Passed=true,Tests=checks,LoopbackOnly=true,RealGameTouched=false});return checks.Count;
    }
    static void Wait(Func<bool> test,string message){var timer=Stopwatch.StartNew();while(!test()){if(timer.ElapsedMilliseconds>45000)throw new IOException(message);Thread.Sleep(100);}}
    static void CheckCopyDialog(P2PService service,GeneralModState state,FixedCatalog catalog,string data,string root,List<string> checks){
        bool animation=GeneralStyle.AnimationsEnabled;GeneralStyle.AnimationsEnabled=false;Exception error=null;bool copied=false;
        try{using(var host=new Form{ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new System.Drawing.Point(-30000,-30000)})using(var dialog=new GeneralP2PDialog(service,state,catalog)){
            dialog.ShowInTaskbar=false;dialog.StartPosition=FormStartPosition.Manual;dialog.Location=new System.Drawing.Point(-30000,-30000);
            host.Shown+=async(s,e)=>{try{dialog.Show(host);var output=(TextBox)dialog.Content.Controls.Find("p2p-output",true).Single();var copy=(Button)dialog.Content.Controls.Find("p2p-copy",true).Single();var watch=Stopwatch.StartNew();while(!copy.Enabled){if(watch.ElapsedMilliseconds>12000)throw new IOException("Opening did not show a copyable invitation before pack preparation.");await Task.Delay(20);}string text=output.Text;int count=service.Sessions.Count;string session=service.Sessions.Last().Id;copy.PerformClick();if(Clipboard.GetText()!=text||service.Sessions.Count!=count||service.Sessions.Last().Id!=session)throw new IOException("Copy did something other than copy the displayed invitation.");copied=true;using(var image=new System.Drawing.Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(image,new System.Drawing.Rectangle(System.Drawing.Point.Empty,image.Size));image.Save(Path.Combine(root,"invitation-before-zip.png"));}checks.Add("Opening shows the actual invitation even if pack inspection fails; the copy icon only copies the existing text.");}catch(Exception ex){error=ex;}dialog.Dispose();host.Close();};Application.Run(host);
        }}finally{GeneralStyle.AnimationsEnabled=animation;}if(error!=null)throw error;if(!copied)throw new IOException("Dialog did not exercise copy.");
    }
}