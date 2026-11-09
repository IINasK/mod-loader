using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

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
}

internal sealed class P2PDiagnosticPreferences {
    public bool Enabled {get;set;}
    public string Endpoint {get;set;}
    internal static Uri HttpsEndpoint(string address){
        Uri url;
        return address!=null&&address.Length<=2048&&Uri.TryCreate(address,UriKind.Absolute,out url)&&
            url.Scheme==Uri.UriSchemeHttps&&url.Host.Length!=0&&url.UserInfo.Length==0&&url.Fragment.Length==0?url:null;
    }
    internal static P2PDiagnosticPreferences Load(string path){
        try{
            Safe.NoLinks(path);
            if(File.Exists(path)&&new FileInfo(path).Length<8192)
                return Safe.Json.Deserialize<P2PDiagnosticPreferences>(File.ReadAllText(path))??new P2PDiagnosticPreferences();
        }catch(Exception ex){OnlineDiagnostics.Record("p2p.diagnostic.preferences",null,ex.GetType().Name);}
        return new P2PDiagnosticPreferences();
    }
    internal static void Post(Uri endpoint,byte[] body){
        var request=(HttpWebRequest)WebRequest.Create(endpoint);
        request.Method="POST";request.ContentType="application/json";request.ContentLength=body.Length;
        request.AllowAutoRedirect=false;request.UseDefaultCredentials=false;
        request.Timeout=8000;request.ReadWriteTimeout=8000;
        using(var stream=request.GetRequestStream())stream.Write(body,0,body.Length);
        using(var response=(HttpWebResponse)request.GetResponse()){
            int status=(int)response.StatusCode;
            if(status<200||status>=300)throw new IOException("Unexpected HTTP status.");
        }
    }
    internal static void SendIfEnabled(string path,P2PDiagnosticReport report){
        var settings=Load(path);
        var endpoint=HttpsEndpoint(settings.Endpoint);
        if(!settings.Enabled||endpoint==null)return;
        try{
            Post(endpoint,Encoding.UTF8.GetBytes(Safe.Json.Serialize(report)));
            OnlineDiagnostics.Record("p2p.diagnostic.delivered",null,report.Id);
        }catch(Exception ex){
            OnlineDiagnostics.Record("p2p.diagnostic.delivery_failed",null,report.Id,new{ErrorType=ex.GetType().Name});
        }
    }
}

internal sealed partial class P2PService {
    sealed class ConnectionAttempt {
        internal string Folder;
        internal bool Sender,TimedOut;
        internal DateTime Started;
        internal P2PDiagnosticReport Report;
    }
    readonly object diagnosticGate=new object();
    readonly Dictionary<string,ConnectionAttempt> connectionAttempts=new Dictionary<string,ConnectionAttempt>();
    readonly bool diagnosticFixture;
    string DiagnosticPreferencesPath {get{
        return Safe.In(diagnosticFixture?Root:GeneralAppPaths.Current().Data,"p2p-diagnostic-preferences.json");
    }}
    string LastDiagnosticPath {get{return Safe.In(Root,"p2p-diagnostic.json");}}
    internal P2PDiagnosticPreferences DiagnosticPreferences(){return P2PDiagnosticPreferences.Load(DiagnosticPreferencesPath);}
    internal void SaveDiagnosticPreferences(P2PDiagnosticPreferences preferences){
        string path=DiagnosticPreferencesPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        Safe.NoLinks(path);Safe.WriteJson(path,preferences);
    }
    static string DiagnosticId(string folder){
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
            AppVersion=GeneralUpdateChecker.CurrentVersion,StartedUtc=attempt.Started.ToString("o"),UpdatedUtc=now.ToString("o"),
            ElapsedSeconds=(int)Math.Max(0,(now-attempt.Started).TotalSeconds),Phase=phase,HelperRunning=Engine.Running,
            LocalApiResponded=api,PeerConnected=connected,FailureType=failure};
        attempt.Report=report;
        OnlineDiagnostics.Record("p2p.diagnostic",null,phase,report);
        try{Directory.CreateDirectory(Root);Safe.NoLinks(LastDiagnosticPath);Safe.WriteJson(LastDiagnosticPath,report);}
        catch(Exception ex){OnlineDiagnostics.Record("p2p.diagnostic.save",null,ex.GetType().Name);}
        if(send&&!attempt.Sender&&!diagnosticFixture){
            string path=DiagnosticPreferencesPath;
            Task.Run(()=>P2PDiagnosticPreferences.SendIfEnabled(path,report));
        }
    }
    internal P2PDiagnosticReport GetConnectionDiagnostic(string folder=null){
        lock(diagnosticGate){
            ConnectionAttempt attempt;
            if(folder!=null&&connectionAttempts.TryGetValue(folder,out attempt))return attempt.Report;
            try{
                string path=LastDiagnosticPath;Safe.NoLinks(path);
                if(File.Exists(path)&&new FileInfo(path).Length<8192){
                    var report=Safe.Json.Deserialize<P2PDiagnosticReport>(File.ReadAllText(path));
                    if(report!=null&&(folder==null||report.Id==DiagnosticId(folder)))return report;
                }
            }catch(Exception ex){OnlineDiagnostics.Record("p2p.diagnostic.read",null,ex.GetType().Name);}
            return null;
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
            if(phase=="host_declined"||phase=="connected"||phase=="host_approved"||phase=="local_failure")continue;
            if(attempt.Sender&&(session.Peer!=null||session.Recipients.Length!=0)){
                ConnectionDiagnosticPhase(session.Id,"host_approved");continue;
            }
            if(!attempt.Sender&&(session.Completed||session.Phase!=null||
                (session.CompareBeforePack&&HasSyncPreview(session)))){
                ConnectionDiagnosticPhase(session.Id,"connected");continue;
            }
            if(attempt.TimedOut||(now-attempt.Started).TotalSeconds<30||phase=="waiting_for_host_choice")continue;
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
                    current.Report.Phase!=phase||current.TimedOut)continue;
                current.TimedOut=true;
                string result=!responded?"local_service_unavailable":attempt.Sender?"no_verified_request":
                    connected==true?"peer_connected_waiting_for_approval":"peer_unreachable";
                RecordConnectionDiagnostic(current,result,responded,connected,failure,true);
            }
        }
    }
}
