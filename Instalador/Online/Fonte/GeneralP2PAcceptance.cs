using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

internal sealed partial class P2PService {
    internal static string AcceptanceProof(string key,string folder,string device,bool lifecycle=false){
        if(!P2PPack.HashValid(key)||!P2PInvite.DeviceValid(device))throw new IOException("Aceitação inválida.");
        var bytes=new byte[32];for(int i=0;i<bytes.Length;i++)bytes[i]=Convert.ToByte(key.Substring(i*2,2),16);
        using(var mac=new HMACSHA256(bytes))return "MLA1|"+folder+"|"+Safe.Hex(mac.ComputeHash(Encoding.UTF8.GetBytes((lifecycle?"mod-loader-accept-2\n":"mod-loader-accept-1\n")+folder+"\n"+device)));
    }
    internal static bool AcceptanceMatches(string proof,string key,string folder,string device,bool lifecycle=false){
        if(!P2PInvite.DeviceValid(device)||proof==null||!proof.StartsWith("MLA1|"+folder+"|",StringComparison.Ordinal))return false;
        string expected=AcceptanceProof(key,folder,device,lifecycle);if(proof.Length!=expected.Length)return false;
        int different=0;for(int i=0;i<proof.Length;i++)different|=proof[i]^expected[i];return different==0;
    }
    string ReceiveLabel(P2PSession s){return s.Automatic?AcceptanceProof(s.Hash,s.Id,Engine.Device,s.Lifecycle):s.Name;}
    void AdvertiseAcceptance(P2PSession s){
        if(!s.Automatic)return;
        lock(startGate)Engine.Request("PATCH","config/devices/"+Engine.Device,new{name=ReceiveLabel(s)});
    }
    void RefreshAcceptance(P2PSession s){
        if(SnapshotSessions().Any(other=>other!=s&&other.Peer==s.Peer&&!other.Completed&&!other.Discarded&&!other.Held&&!other.Paused))return;
        lock(startGate){
            var device=Engine.Get("config/devices/"+s.Peer);if(device.ContainsKey("paused")&&Convert.ToBoolean(device["paused"]))return;
            // Re-advertise the upgraded proof; the peer's pending-folder cache may still hold the previous label.
            Engine.Request("PATCH","config/devices/"+s.Peer,new{paused=true});
            try{
                var watch=System.Diagnostics.Stopwatch.StartNew();
                while(watch.ElapsedMilliseconds<5000){var connections=Engine.Get("system/connections")["connections"] as Dictionary<string,object>;object raw;if(connections==null||!connections.TryGetValue(s.Peer,out raw)||!Convert.ToBoolean(((Dictionary<string,object>)raw)["connected"]))break;System.Threading.Thread.Sleep(50);}
            }finally{Engine.Request("PATCH","config/devices/"+s.Peer,new{paused=false});}
        }
    }
    void RestoreAcceptanceName(){var pending=SnapshotSessions().LastOrDefault(s=>!s.Sender&&s.Automatic&&!s.Completed&&!s.Discarded);if(pending!=null)AdvertiseAcceptance(pending);}
    internal bool UpgradeInvitation(P2PSession s){
        lock(s)lock(startGate){if(!s.Sender||!s.Deferred||s.Automatic||s.Completed||s.Discarded||s.Peer!=null)return false;s.Automatic=true;Save();return true;}
    }
    readonly Dictionary<P2PSession,Dictionary<string,Task<bool>>> connectionDecisions=
        new Dictionary<P2PSession,Dictionary<string,Task<bool>>>();

    volatile string unmatchedInvitationId;
    internal string UnmatchedInvitationId {get{return unmatchedInvitationId;}}

    void DetectUnmatchedInvitation(Dictionary<string,object> folders,HashSet<string> verifiedPeers){
        var recognized=new HashSet<string>(SnapshotSessions().Where(s=>!s.Discarded).Select(s=>s.Id),StringComparer.Ordinal);
        DateTimeOffset newest=DateTimeOffset.UtcNow.AddMinutes(-2);
        string mismatch=null;
        foreach(var entry in folders){
            if(recognized.Contains(entry.Key)||!Regex.IsMatch(entry.Key,@"\Aml-[a-f0-9]{32}\z"))continue;
            var folder=entry.Value as Dictionary<string,object>;
            object raw;
            var peers=folder!=null&&folder.TryGetValue("offeredBy",out raw)?raw as Dictionary<string,object>:null;
            if(peers==null)continue;
            foreach(var peer in peers){
                if(!P2PInvite.DeviceValid(peer.Key)||peer.Key==Engine.Device||verifiedPeers.Contains(peer.Key))continue;
                var details=peer.Value as Dictionary<string,object>;
                object label,time;
                if(details==null||!details.TryGetValue("label",out label)||!details.TryGetValue("time",out time))continue;
                string proof=Convert.ToString(label),prefix="MLA1|"+entry.Key+"|";
                if(!proof.StartsWith(prefix,StringComparison.Ordinal)||!P2PPack.HashValid(proof.Substring(prefix.Length)))continue;
                DateTimeOffset observed;
                if(!DateTimeOffset.TryParse(Convert.ToString(time,CultureInfo.InvariantCulture),CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,out observed)||observed<newest)continue;
                // An unmatched label is only a hint. It never authorizes a device or a share.
                newest=observed;mismatch=DiagnosticId(entry.Key);
            }
        }
        if(mismatch!=null&&mismatch!=unmatchedInvitationId)
            OnlineDiagnostics.Record("p2p.invitation.unmatched",null,"Pedido recebido para um convite ausente neste projeto.",new{Id=mismatch});
        unmatchedInvitationId=mismatch;
    }

    void DetectAcceptances(){
        var waiting=installedSessions.Where(s=>s.Sender&&s.Automatic&&!s.Completed&&!s.Discarded&&(s.Lifecycle?s.Recipients.Length<(s.CompareBeforePack?1:3):s.Peer==null)).ToArray();
        foreach(var session in connectionDecisions.Keys.Where(session=>!waiting.Contains(session)).ToArray())
            connectionDecisions.Remove(session);
        if(waiting.Length==0){unmatchedInvitationId=null;return;}
        foreach(var session in waiting){
            Dictionary<string,Task<bool>> decisions;
            if(!connectionDecisions.TryGetValue(session,out decisions))continue;
            var accepted=decisions.FirstOrDefault(item=>item.Value.Status==TaskStatus.RanToCompletion&&item.Value.Result);
            if(accepted.Key==null){
                if(decisions.Count!=0&&decisions.Values.All(task=>task.IsCompleted))
                    ConnectionDiagnosticPhase(session.Id,"host_declined");
                continue;
            }
            lock(session)lock(startGate){
                if(session.Completed||session.Discarded||session.Recipients.Length!=0)continue;
                AddRecipient(session,accepted.Key);
                connectionDecisions.Remove(session);
            }
        }
        var devices=Engine.Get("cluster/pending/devices");var folders=Engine.Get("cluster/pending/folders");
        var verifiedPeers=new HashSet<string>(SnapshotSessions().Where(s=>s.Sender&&!s.Completed&&!s.Discarded)
            .SelectMany(s=>s.Recipients.Select(peer=>peer.Device).Concat(s.Peer==null?new string[0]:new[]{s.Peer})),StringComparer.Ordinal);
        foreach(var s in waiting){
            var candidates=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var pair in devices){var details=pair.Value as Dictionary<string,object>;if(details!=null&&details.ContainsKey("name"))candidates[pair.Key]=Convert.ToString(details["name"]);}
            object offered;var folder=folders.TryGetValue(s.Id,out offered)?offered as Dictionary<string,object>:null;
            var peers=folder!=null&&folder.ContainsKey("offeredBy")?folder["offeredBy"] as Dictionary<string,object>:null;
            if(peers!=null)foreach(var pair in peers){var details=pair.Value as Dictionary<string,object>;if(details!=null&&details.ContainsKey("label")){
                string label=Convert.ToString(details["label"]);
                if(AcceptanceMatches(label,s.Hash,s.Id,pair.Key,s.Lifecycle))candidates[pair.Key]=label;
            }}
            foreach(var peer in candidates){
                if(peer.Key==Engine.Device||!AcceptanceMatches(peer.Value,s.Hash,s.Id,peer.Key,s.Lifecycle))continue;
                verifiedPeers.Add(peer.Key);
                lock(s)lock(startGate){
                    if(s.Completed||s.Discarded)break;
                    if(s.Lifecycle){
                        if(s.Recipients.Length>=(s.CompareBeforePack?1:3)||s.Recipients.Any(p=>p.Device==peer.Key))continue;
                        var confirm=ConfirmConnection;
                        if(s.CompareBeforePack&&confirm!=null){
                            Dictionary<string,Task<bool>> decisions;
                            if(!connectionDecisions.TryGetValue(s,out decisions))
                                connectionDecisions[s]=decisions=new Dictionary<string,Task<bool>>(StringComparer.Ordinal);
                            if(decisions.ContainsKey(peer.Key)||decisions.Count>=16||
                                connectionDecisions.Values.Any(group=>group.Values.Any(task=>!task.IsCompleted)))continue;
                            ConnectionDiagnosticPhase(s.Id,"waiting_for_host_choice");
                            decisions.Add(peer.Key,confirm(s));
                            break;
                        }
                        AddRecipient(s,peer.Key);
                        continue;
                    }
                    if(s.Peer!=null)break;
                    // Only possession of this invitation can authorize this exact TLS device; never trust a pending name alone.
                    Authorize(new P2PInvite{Kind="accept",Device=peer.Key,Folder=s.Id,ManifestHash=s.Hash,Name=s.Name,Mode=s.Mode,Deferred=s.Deferred,Automatic=true,Size=0}.Encode());
                    OnlineDiagnostics.Record("p2p.accepted",null,"Convite aceito automaticamente.");
                }
                break;
            }
        }
        DetectUnmatchedInvitation(folders,verifiedPeers);
    }
}
