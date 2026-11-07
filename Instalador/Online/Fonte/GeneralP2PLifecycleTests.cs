using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

internal static class GeneralP2PLifecycleTests {
    static readonly FixedCatalog Catalog=new FixedCatalog{
        Schema=1,Id="lifecycle",GameVersion="2.5",GameFileVersion="2.5.0.0",
        Packages=new List<FixedMod>(),Defaults=new List<FixedDefault>()
    };

    internal static void Run(string parent,string helper){
        string root=OnlineTests.NestedRoot(parent,"lifecycle-");
        Directory.CreateDirectory(root);
        var checks=new List<string>();
        try{
            Network(root,helper,checks);
            Rollback(root,checks);
            Safe.WriteJson(Path.Combine(root,"resultado-p2p-lifecycle.json"),new{Passed=true,Tests=checks});
        }catch(Exception ex){
            Safe.WriteJson(Path.Combine(root,"resultado-p2p-lifecycle.json"),new{Passed=false,Tests=checks,Error=ex.ToString()});
            throw;
        }
    }

    static void Network(string root,string helper,List<string> checks){
        var source=Source(root,"host","shared.txt","from host");
        string left=Target(root,"left"),right=Target(root,"right"),leftData=Path.Combine(root,"left-data");
        var relay=Source(root,"friend","forwarded.txt","from friend",left,leftData);
        string thirdTarget=Target(root,"third");
        File.WriteAllText(Safe.In(left,"mods/shared.txt"),"original");
        var gate=new ManualResetEventSlim();
        var host=Service(root,"host-peer",source.Item2,helper);
        var first=Service(root,"first-peer",leftData,helper);
        var second=Service(root,"second-peer",Path.Combine(root,"right-data"),helper);
        var third=Service(root,"third-peer",Path.Combine(root,"third-data"),helper);
        try{
            first.ChooseVersion=_=>{gate.Wait(40000);return false;};
            host.SetPreparationState(source.Item1);
            first.SetPreparationState(relay.Item1);
            var sent=host.BeginDeferred(source.Item1,new[]{source.Item1.List.Mods.Single().Id},true);
            string invitation=host.Invitation(sent);
            Check(checks,P2PInvite.Decode(invitation).Schema==2&&!File.Exists(Safe.In(host.Folder(sent),"pack.zip")),
                "The new invitation creates no payload before a recipient connects.");
            var incoming=first.Accept(invitation,left,true);
            var incomingSecond=second.Accept(invitation,right,true);
            var incomingThird=third.Accept(invitation,thirdTarget,true);
            ConnectTo(first,host);ConnectTo(second,host);ConnectTo(third,host);
            Wait(()=>sent.Recipients.Length==3,"Three friends did not join the same invitation.");
            Wait(()=>sent.Recipients.Any(p=>p.Phase=="review")&&incomingSecond.Completed&&incomingThird.Completed,"The version review did not arrive while the other receiver completed.");
            Check(checks,!sent.Completed&&File.Exists(Safe.In(host.Folder(sent),"pack.zip")),
                "The host retains its one ZIP while one recipient is choosing a version.");
            Check(checks,host.TransferStatus(sent).Percent==100&&host.TransferStatus(sent).Recipients.Any(line=>line.Contains(P2PService.PhaseLabel("review"))),
                "The sender can show 100% downloaded while installation is awaiting the version choice.");
            Check(checks,File.ReadAllText(Safe.In(left,"mods/shared.txt"))=="original",
                "A version review leaves the existing file intact.");

            var forwarded=first.BeginDeferred(relay.Item1,new[]{relay.Item1.List.Mods.Single().Id},true);
            var forwardedReceive=second.Accept(first.Invitation(forwarded),right,true);
            ConnectTo(second,first);
            Wait(()=>forwardedReceive.Completed,"The friend could not upload while its incoming version review was open.");
            Check(checks,File.ReadAllText(Safe.In(right,"mods/forwarded.txt"))=="from friend"&&!incoming.Completed,
                "A receiver can send a different sharing while awaiting a version choice.");

            gate.Set();
            Wait(()=>incoming.Phase=="retry"&&incoming.Stage==null,"Keeping the current version did not defer installation.");
            string payload=Safe.In(first.Folder(incoming),"pack.zip");
            string zipHash=Safe.Hash(payload);
            first.Dispose();
            first=Service(root,"first-peer",leftData,helper,false);
            incoming=first.SnapshotSessions().Single(s=>!s.Sender);
            Check(checks,incoming.RecoveryPending&&Safe.Hash(payload)==zipHash,"A restart identifies the interrupted receiving session and preserves its ZIP.");
            first.PreserveInterrupted(incoming);
            first.Start(_=>{},helper);
            ConnectTo(first,host);
            Check(checks,incoming.Held&&!incoming.Completed&&Safe.Hash(payload)==zipHash,
                "Keeping for later preserves bytes and waits for an explicit retry.");
            first.ChooseVersion=_=>true;
            first.RetryInstalled(incoming,left);
            Wait(()=>incoming.Completed&&sent.Completed,"Retrying the original transfer did not install and acknowledge the package.");
            Check(checks,File.ReadAllText(Safe.In(left,"mods/shared.txt"))=="from host"&&host.Invitation(sent)==invitation,
                "The original invitation resumes installation and applies the accepted version.");
            Wait(()=>!Directory.Exists(host.Folder(sent))&&!Directory.Exists(first.Folder(incoming))&&!Directory.Exists(second.Folder(incomingSecond))&&!Directory.Exists(third.Folder(incomingThird)),
                "Confirmed transfers did not clean their payload directories.");
            Check(checks,sent.Recipients.All(p=>p.Phase=="installed"),"The sender finishes only after all three installation receipts arrive.");
        }finally{
            gate.Set();first.Dispose();second.Dispose();third.Dispose();host.Dispose();gate.Dispose();
        }
    }

    static void Rollback(string root,List<string> checks){
        var source=Source(root,"rollback","rollback.txt","incoming");
        string target=Target(root,"rollback-target"),data=Path.Combine(root,"rollback-data"),zip=Path.Combine(root,"rollback-snapshot.zip");
        File.WriteAllText(Safe.In(target,"mods/rollback.txt"),"local before");
        InstalledSnapshot.Create(zip,source.Item1,new[]{source.Item1.List.Mods.Single().Id},Catalog,source.Item2,true,_=>{});
        var installer=new InstalledSnapshotInstaller(target,data,Catalog,true);
        string transaction=null;
        try{
            installer.Install(zip,Safe.Hash(zip),message=>{
                if(message.StartsWith("Instalado:",StringComparison.Ordinal))throw new OperationCanceledException();
            },-1,_=>true,id=>transaction=id);
            throw new IOException("Installation should have been interrupted after a write.");
        }catch(OperationCanceledException){}
        Check(checks,transaction!=null&&File.ReadAllText(Safe.In(target,"mods/rollback.txt"))=="incoming",
            "An interrupted write leaves a durable transaction for recovery.");
        installer.DiscardInterrupted(transaction,zip,Safe.Hash(zip));
        Check(checks,File.ReadAllText(Safe.In(target,"mods/rollback.txt"))=="local before",
            "Discard restores the recorded prior version of the interrupted file.");
        installer.Install(zip,Safe.Hash(zip),_=>{},-1,_=>true);
        Check(checks,File.ReadAllText(Safe.In(target,"mods/rollback.txt"))=="incoming",
            "The rollback clears the unfinished transaction so a subsequent installation can proceed.");
    }

    static Tuple<GeneralModState,string> Source(string root,string name,string filename,string content,string game=null,string data=null){
        game=game??Target(root,name);
        data=data??Path.Combine(root,name+"-data");
        string archive=Path.Combine(root,name+".zip");
        using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create))
        using(var output=new StreamWriter(zip.CreateEntry("mods/"+filename).Open()))output.Write(content);
        var state=new GeneralModState{Destination=game};
        string id="fixture-"+name;
        state.List.Mods.Add(new GeneralModEntry{Id=id,Name=name,DefaultVariant="default",Variants=new List<GeneralModVariant>{
            new GeneralModVariant{Id="default",Name="Main",Sources=new List<string>{archive}}}});
        new GeneralModInstaller(game,Path.Combine(data,"Instalacoes")).Install(archive,id+"/default/0/"+archive,_=>{});
        return Tuple.Create(state,data);
    }

    static string Target(string root,string name){
        string path=Safe.In(root,name);
        Directory.CreateDirectory(Safe.In(path,"mods"));
        File.WriteAllText(Safe.In(path,".fixture-only"),"test");
        return path;
    }

    static P2PService Service(string root,string name,string data,string helper,bool start=true){
        var service=new P2PService(Path.Combine(root,name),Path.Combine(root,name+"-files"),true,true);
        service.EnableInstalledReceive(Catalog,data,true);
        if(start)service.Start(_=>{},helper);
        return service;
    }

    static void ConnectTo(P2PService receiver,P2PService sender){
        receiver.Engine.ConfigurePeer(sender.Engine.Device,"tcp://127.0.0.1:"+sender.Engine.ListenPort);
    }

    static void Wait(Func<bool> condition,string error){
        var elapsed=Stopwatch.StartNew();
        while(!condition()){
            if(elapsed.ElapsedMilliseconds>45000)throw new IOException(error);
            Thread.Sleep(75);
        }
    }

    static void Check(List<string> checks,bool value,string description){
        if(!value)throw new IOException(description);
        checks.Add(description);
    }
}
