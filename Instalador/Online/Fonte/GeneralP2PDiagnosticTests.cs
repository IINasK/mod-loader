using System;
using System.IO;
using System.Linq;

internal static class GeneralP2PDiagnosticTests {
    internal static void Run(string parent){
        string root=OnlineTests.NestedRoot(parent,"diagnostics-");
        Directory.CreateDirectory(root);
        string data=Safe.In(root,"p2p"),store=Safe.In(root,"packages");
        string device=String.Join("-",Enumerable.Repeat("AAAAAAA",8));
        string other=String.Join("-",Enumerable.Repeat("BBBBBBB",8));
        var session=new P2PSession{
            Id="ml-"+new string('a',32),Name="Receiver fixture",Hash=new string('a',64),
            Lifecycle=true,Deferred=true,Mode="installed-zip",Peer=device,CreatedUtc=DateTime.UtcNow.ToString("o")
        };
        var unrelated=new P2PSession{
            Id="ml-"+new string('b',32),Name="Unrelated fixture",Hash=new string('b',64),
            Lifecycle=true,Deferred=true,Mode="installed-zip",Peer=other,CreatedUtc=DateTime.UtcNow.ToString("o")
        };
        Directory.CreateDirectory(data);
        Safe.WriteJson(Safe.In(data,"sessions.json"),new[]{session,unrelated});
        using(var service=new P2PService(data,store,true)){
            var received=service.SnapshotSessions().First();
            service.ArmConnectionDiagnostic(received.Id,false);
            service.ConnectionDiagnosticPhase(received.Id,"local_failure",new IOException(@"C:\Users\fixture\secret.txt"));
            Check(service.NeedsDiagnosticConnection()&&!service.Engine.Running,"Offline report survives while the helper is unavailable.");
            service.ReportDiagnosticFailure(received,"local_failure",new IOException("repeat"));
            Check(service.LocalDiagnostics(received).Length==1,"Repeated identical failure within one poll is coalesced.");
            for(int i=0;i<12;i++)service.SendDiagnostic(received);
            var pending=service.LocalDiagnostics(received);
            Check(pending.Length==8&&pending.All(report=>report.Phase=="manual"),"The local report history is bounded.");
            Check(service.LocalDiagnostics(service.SnapshotSessions()[1]).Length==0,"A report is never assigned to an unrelated invitation.");
        }
        using(var reopened=new P2PService(data,store,true)){
            Check(reopened.LocalDiagnostics(reopened.SnapshotSessions()[0]).Length==8,
                "Reports queued offline survive reopening.");
        }
        Safe.WriteJson(Safe.In(root,"resultado-p2p-diagnostics.json"),new{Passed=true,Tests=5,LoopbackOnly=true});
    }

    static void Check(bool ok,string message){
        if(!ok)throw new IOException(message);
    }
}
