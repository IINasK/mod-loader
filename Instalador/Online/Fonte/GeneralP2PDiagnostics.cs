using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

internal sealed class P2PDiagnosticReport {
    public int Schema {get;set;}
    public string Id {get;set;}
    public string Side {get;set;}
    public string AppVersion {get;set;}
    public string StartedUtc {get;set;}
    public string UpdatedUtc {get;set;}
    public int ElapsedSeconds {get;set;}
    public string Phase {get;set;}
    public bool HelperRunning {get;set;}
    public bool? LocalApiResponded {get;set;}
    public bool? PeerConnected {get;set;}
    public string FailureType {get;set;}
    public int? ErrorCode {get;set;}
    public int? ModsOutsidePack {get;set;}
}

internal sealed partial class P2PService {
    sealed class ConnectionAttempt {
        internal string Folder;
        internal bool Sender,ReportedDelay;
        internal DateTime Started;
        internal P2PDiagnosticReport Report;
    }
    readonly object diagnosticGate=new object();
    readonly Dictionary<string,ConnectionAttempt> connectionAttempts=new Dictionary<string,ConnectionAttempt>();
    const string DiagnosticName="report.json";
    const int DiagnosticLimit=8;
    const int MaxDiagnosticMessage=12288;
    readonly HashSet<string> configuredDiagnostics=new HashSet<string>(StringComparer.Ordinal);
    bool diagnosticFoldersRecovered;
    readonly HashSet<string> staleDiagnosticFolders=new HashSet<string>(StringComparer.Ordinal);

    static bool SupportsDiagnostics(P2PSession session){
        return session.Lifecycle&&!session.Discarded&&!session.HistoryDismissed&&session.Mode=="installed-zip"&&
            (session.Sender?session.Recipients.Length!=0:session.Peer!=null);
    }

    internal bool CanSendDiagnostic(P2PSession session){
        return session!=null&&!session.Sender&&SupportsDiagnostics(session)&&SnapshotSessions().Contains(session);
    }

    internal bool NeedsDiagnosticConnection(){
        return SnapshotSessions().Any(session=>SupportsDiagnostics(session)&&
            (session.Sender||File.Exists(DiagnosticQueue(session))));
    }

    string DiagnosticQueue(P2PSession session){
        return Safe.In(Root,"dq/"+session.Id+".json");
    }

    string DiagnosticFolderId(P2PSession session,string receiver){
        return "ml-"+InstalledSnapshot.Hash(Encoding.UTF8.GetBytes("diagnostics\n"+session.Id+"\n"+receiver)).Substring(0,32);
    }

    string DiagnosticFolder(string id){
        return Safe.In(Root,"dr/"+id);
    }

    // Keep the wire report structured. No exception messages, paths, or raw logs cross the connection.
    static bool ValidDiagnostic(P2PDiagnosticReport report,P2PSession session){
        DateTimeOffset started,updated;
        return report!=null&&report.Schema==1&&report.Side=="receiver"&&report.Id==DiagnosticId(session.Id)&&
            Regex.IsMatch(report.AppVersion??"", @"\A[0-9]+(?:\.[0-9]+){1,3}\z")&&report.AppVersion.Length<=24&&
            Regex.IsMatch(report.Phase??"", @"\A[a-z_]{1,60}\z")&&
            (report.FailureType==null||Regex.IsMatch(report.FailureType,@"\A[A-Za-z][A-Za-z0-9]{0,79}\z"))&&
            DiagnosticTime(report.StartedUtc,out started)&&DiagnosticTime(report.UpdatedUtc,out updated)&&
            updated>=started&&report.ElapsedSeconds>=0&&
            (!report.ModsOutsidePack.HasValue||report.ModsOutsidePack>=0&&report.ModsOutsidePack<=1000);
    }

    static bool DiagnosticTime(string value,out DateTimeOffset parsed){
        parsed=default(DateTimeOffset);
        return value!=null&&value.Length<=36&&DateTimeOffset.TryParseExact(value,"o",CultureInfo.InvariantCulture,
            DateTimeStyles.None,out parsed);
    }

    static P2PDiagnosticReport[] ParseDiagnostics(string json,P2PSession session){
        var reports=Safe.Json.Deserialize<P2PDiagnosticReport[]>(json);
        if(reports==null||reports.Length==0||reports.Length>DiagnosticLimit||reports.Any(report=>!ValidDiagnostic(report,session)))
            throw new IOException("Relatório P2P inválido.");
        return reports;
    }

    P2PDiagnosticReport[] ReadDiagnosticQueue(P2PSession session){
        string path=DiagnosticQueue(session);
        Safe.NoLinks(path);
        if(!File.Exists(path))return new P2PDiagnosticReport[0];
        using(var input=File.OpenRead(path))
            return ParseDiagnostics(Encoding.UTF8.GetString(InstalledSnapshot.ReadBytes(input,MaxDiagnosticMessage)),session);
    }

    P2PDiagnosticReport DiagnosticFor(P2PSession session,string phase,Exception error=null){
        var now=DateTime.UtcNow;
        DateTime started;
        if(!DateTime.TryParse(session.CreatedUtc,out started)||started.ToUniversalTime()>now)started=now;
        started=started.ToUniversalTime();
        return new P2PDiagnosticReport{
            Schema=1,Id=DiagnosticId(session.Id),Side="receiver",AppVersion=GeneralUpdateChecker.CurrentVersion,
            Phase=phase,FailureType=error==null?null:error.GetType().Name,
            ErrorCode=error==null?(int?)null:error.HResult,ModsOutsidePack=session.ModsOutsidePack,
            StartedUtc=new DateTimeOffset(started).ToString("o"),UpdatedUtc=new DateTimeOffset(now).ToString("o"),
            ElapsedSeconds=(int)Math.Min(Int32.MaxValue,(now-started).TotalSeconds),HelperRunning=Engine.Running
        };
    }

    void QueueDiagnostic(P2PSession session,P2PDiagnosticReport report,bool automatic){
        if(!CanSendDiagnostic(session)||!ValidDiagnostic(report,session))return;
        lock(diagnosticGate){
            var queue=ReadDiagnosticQueue(session);
            var previous=queue.LastOrDefault();
            if(automatic&&previous!=null&&previous.Phase==report.Phase&&previous.FailureType==report.FailureType&&
                DateTimeOffset.UtcNow-DateTimeOffset.Parse(previous.UpdatedUtc)<TimeSpan.FromSeconds(10))return;
            string path=DiagnosticQueue(session);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            Safe.NoLinks(path);
            Safe.WriteJson(path,queue.Skip(Math.Max(0,queue.Length-DiagnosticLimit+1)).Concat(new[]{report}).ToArray());
        }
    }

    internal void SendDiagnostic(P2PSession session){
        if(!CanSendDiagnostic(session))throw new IOException(GeneralStyle.T("Escolha um recebimento P2P.","Choose a P2P download."));
        QueueDiagnostic(session,DiagnosticFor(session,"manual"),false);
    }

    internal void ReportDiagnosticFailure(P2PSession session,string phase,Exception error){
        if(!CanSendDiagnostic(session))return;
        TryDiagnosticIo(()=>QueueDiagnostic(session,DiagnosticFor(session,phase,error),true));
    }

    void ConfigureDiagnostics(string id,string peer,bool host){
        if(configuredDiagnostics.Contains(id))return;
        string folder=DiagnosticFolder(id);
        Safe.NoLinks(folder);
        Directory.CreateDirectory(folder);
        string ignore=Safe.In(folder,".stignore"),rules="!"+DiagnosticName+"\n*\n";
        Safe.NoLinks(ignore);
        if(!File.Exists(ignore))File.WriteAllText(ignore,rules);
        else if(new FileInfo(ignore).Length>128||File.ReadAllText(ignore)!=rules)
            throw new IOException("As regras da pasta de diagnóstico P2P foram alteradas.");
        Engine.EnsurePeer(peer);
        Engine.ConfigureFolder(id,folder,"Mod Loader diagnostics",!host,peer);
        configuredDiagnostics.Add(id);
    }

    void RestoreDiagnosticFolders(){
        string root=Safe.In(Root,"dr");
        Safe.NoLinks(root);
        if(!Directory.Exists(root))return;
        foreach(string folder in Directory.GetDirectories(root)){
            string id=Path.GetFileName(folder);
            if(Regex.IsMatch(id,@"\Aml-[a-f0-9]{32}\z"))staleDiagnosticFolders.Add(id);
        }
    }

    void ProcessDiagnosticTransfers(P2PSession[] sessions){
        if(!Engine.Running)return;
        if(!diagnosticFoldersRecovered){
            RestoreDiagnosticFolders();
            diagnosticFoldersRecovered=true;
        }
        var active=new HashSet<string>(StringComparer.Ordinal);
        foreach(var session in sessions.Where(SupportsDiagnostics)){
            if(session.Sender){
                foreach(var recipient in session.Recipients){
                    string id=DiagnosticFolderId(session,recipient.Device);
                    active.Add(id);
                    TryDiagnosticIo(()=>ConfigureDiagnostics(id,recipient.Device,true));
                }
            }else if(Engine.Device!=null&&File.Exists(DiagnosticQueue(session))){
                string id=DiagnosticFolderId(session,Engine.Device);
                active.Add(id);
                TryDiagnosticIo(()=>PublishDiagnostics(session,id));
            }
        }
        foreach(string id in staleDiagnosticFolders.Concat(configuredDiagnostics).Distinct().Where(id=>!active.Contains(id)).ToArray())
            TryDiagnosticIo(()=>{Engine.RemoveFolder(id);configuredDiagnostics.Remove(id);staleDiagnosticFolders.Remove(id);});
        staleDiagnosticFolders.ExceptWith(active);
    }

    void PublishDiagnostics(P2PSession session,string id){
        // The invitation pins the host's TLS device. Only that device is offered this folder.
        ConfigureDiagnostics(id,session.Peer,false);
        var reports=ReadDiagnosticQueue(session);
        if(reports.Length==0)return;
        string path=Safe.In(DiagnosticFolder(id),DiagnosticName);
        Safe.NoLinks(path);
        if(File.Exists(path)){
            string ignored;
            try{
                if(ReadSyncMessage(session,Engine.Device,path,DiagnosticName,out ignored,MaxDiagnosticMessage)==Safe.Json.Serialize(reports))
                    return;
            }catch(IOException){/* Rebuild the envelope from the validated queue. */}
            catch(ArgumentException){/* Rebuild invalid local JSON. */}
            catch(InvalidOperationException){/* Rebuild invalid local JSON. */}
        }
        WriteSyncMessage(session,Engine.Device,path,DiagnosticName,reports);
        ScanSyncFolder(id,DiagnosticName);
    }

    internal P2PDiagnosticReport[] LocalDiagnostics(P2PSession session){
        P2PDiagnosticReport[] reports=new P2PDiagnosticReport[0];
        if(session!=null&&!session.Sender)TryDiagnosticIo(()=>reports=ReadDiagnosticQueue(session));
        return reports;
    }

    internal P2PDiagnosticReport[] ReceivedDiagnostics(P2PSession session){
        var reports=new List<P2PDiagnosticReport>();
        if(session==null||!session.Sender)return reports.ToArray();
        foreach(var recipient in session.Recipients){
            TryDiagnosticIo(()=>{
                string path=Safe.In(DiagnosticFolder(DiagnosticFolderId(session,recipient.Device)),DiagnosticName);
                Safe.NoLinks(path);
                if(!File.Exists(path))return;
                string ignored;
                reports.AddRange(ParseDiagnostics(ReadSyncMessage(session,recipient.Device,path,DiagnosticName,out ignored,MaxDiagnosticMessage),session));
            });
        }
        return reports.OrderBy(report=>report.UpdatedUtc,StringComparer.Ordinal).ToArray();
    }

    static void TryDiagnosticIo(Action action){
        try{action();}
        catch(Exception ex){OnlineDiagnostics.Record("p2p.diagnostic.delivery_failed",null,ex.GetType().Name);}
    }
    internal static string DiagnosticId(string folder){
        using(var hash=SHA256.Create())return Safe.Hex(hash.ComputeHash(Encoding.UTF8.GetBytes(folder))).Substring(0,16);
    }
    internal void ArmConnectionDiagnostic(string folder,bool sender){
        lock(diagnosticGate){
            if(disposed)return;
            var attempt=new ConnectionAttempt{Folder=folder,Sender=sender,Started=DateTime.UtcNow};
            connectionAttempts[folder]=attempt;
            foreach(var key in connectionAttempts.Values.OrderByDescending(item=>item.Started).Skip(100).Select(item=>item.Folder).ToArray())
                connectionAttempts.Remove(key);
            RecordConnectionDiagnostic(attempt,sender?"invitation_copied":"connection_requested");
        }
    }
    internal void ConnectionDiagnosticPhase(string folder,string phase,Exception error=null){
        lock(diagnosticGate){
            ConnectionAttempt attempt;
            if(connectionAttempts.TryGetValue(folder,out attempt))
                RecordConnectionDiagnostic(attempt,phase,failure:error==null?null:error.GetType().Name,send:error!=null);
        }
    }
    void RecordConnectionDiagnostic(ConnectionAttempt attempt,string phase,bool? api=null,bool? connected=null,string failure=null,bool send=false){
        if(attempt.Report!=null&&attempt.Report.Phase==phase)return;
        var now=DateTime.UtcNow;
        var report=new P2PDiagnosticReport{Schema=1,Id=DiagnosticId(attempt.Folder),Side=attempt.Sender?"host":"receiver",
            AppVersion=GeneralUpdateChecker.CurrentVersion,StartedUtc=new DateTimeOffset(attempt.Started).ToString("o"),UpdatedUtc=new DateTimeOffset(now).ToString("o"),
            ElapsedSeconds=(int)Math.Max(0,(now-attempt.Started).TotalSeconds),Phase=phase,HelperRunning=Engine.Running,
            LocalApiResponded=api,PeerConnected=connected,FailureType=failure};
        attempt.Report=report;
        OnlineDiagnostics.Record("p2p.diagnostic",null,phase,report);
        if(send&&!attempt.Sender){
            var session=SnapshotSessions().FirstOrDefault(item=>item.Id==attempt.Folder&&!item.Sender);
            if(session!=null)TryDiagnosticIo(()=>QueueDiagnostic(session,report,true));
        }
    }
    internal P2PDiagnosticReport GetConnectionDiagnostic(string folder=null){
        lock(diagnosticGate){
            ConnectionAttempt attempt;
            if(folder!=null&&connectionAttempts.TryGetValue(folder,out attempt))return attempt.Report;
            return folder==null?connectionAttempts.Values.Select(value=>value.Report)
                .Where(report=>report!=null).OrderByDescending(report=>report.UpdatedUtc,StringComparer.Ordinal).FirstOrDefault():null;
        }
    }
    internal void ObserveConnectionDiagnostics(DateTime now){
        ConnectionAttempt[] attempts;
        lock(diagnosticGate)attempts=connectionAttempts.Values.ToArray();
        if(attempts.Length==0)return;
        var sessions=SnapshotSessions().ToDictionary(s=>s.Id);
        foreach(var attempt in attempts){
            P2PSession session;
            if(!sessions.TryGetValue(attempt.Folder,out session)||session.Discarded)continue;
            if(session.Paused||session.Held||session.RecoveryPending)continue;
            string phase=attempt.Report.Phase;
            if(phase=="connected"||phase=="local_failure")continue;
            if(attempt.Sender&&(phase==HostDeclined||phase==HostApproved))continue;
            if(attempt.Sender&&(session.Peer!=null||session.Recipients.Length!=0)){
                ConnectionDiagnosticPhase(session.Id,"host_approved");continue;
            }
            if(!attempt.Sender&&(session.Completed||session.Phase!=null||
                (session.CompareBeforePack&&HasSyncPreview(session)))){
                ConnectionDiagnosticPhase(session.Id,"connected");continue;
            }
            if(!attempt.Sender){
                string decision=ReadHostDecision(session,Engine.Device);
                if(decision!=null){
                    ConnectionDiagnosticPhase(session.Id,decision);
                    continue;
                }
            }
            if(attempt.ReportedDelay||(now-attempt.Started).TotalSeconds<30||
                phase==HostWaiting||phase==HostApproved||phase==HostDeclined)continue;
            bool responded=false;
            bool? connected=null;
            string failure=null;
            try{
                var peers=Engine.Get("system/connections")["connections"] as Dictionary<string,object>;
                responded=true;
                object value;
                if(!attempt.Sender)connected=peers!=null&&peers.TryGetValue(session.Peer,out value)&&
                    Convert.ToBoolean(((Dictionary<string,object>)value)["connected"]);
            }catch(Exception ex){failure=ex.GetType().Name;}
            lock(diagnosticGate){
                ConnectionAttempt current;
                if(!connectionAttempts.TryGetValue(session.Id,out current)||!Object.ReferenceEquals(attempt,current)||
                    current.Report.Phase!=phase||current.ReportedDelay)continue;
                current.ReportedDelay=true;
                string result=!responded?"local_service_unavailable":attempt.Sender?"no_verified_request":
                    connected==true?"peer_connected_waiting_for_approval":"peer_unreachable";
                RecordConnectionDiagnostic(current,result,responded,connected,failure,true);
            }
        }
    }
}
