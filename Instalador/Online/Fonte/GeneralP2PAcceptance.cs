using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

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
        if(SnapshotSessions().Any(other=>other!=s&&other.Peer==s.Peer&&!other.Completed&&!other.Paused))return;
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
    void RestoreAcceptanceName(){var pending=SnapshotSessions().LastOrDefault(s=>!s.Sender&&s.Automatic&&!s.Completed);if(pending!=null)AdvertiseAcceptance(pending);}
    internal bool UpgradeInvitation(P2PSession s){
        lock(s)lock(startGate){if(!s.Sender||!s.Deferred||s.Automatic||s.Completed||s.Peer!=null)return false;s.Automatic=true;Save();return true;}
    }
    void DetectAcceptances(){
        var waiting=installedSessions.Where(s=>s.Sender&&s.Automatic&&!s.Completed&&(s.Lifecycle?s.Recipients.Length<(s.CompareBeforePack?1:3):s.Peer==null)).ToArray();if(waiting.Length==0)return;
        var devices=Engine.Get("cluster/pending/devices");var folders=Engine.Get("cluster/pending/folders");
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
                lock(s)lock(startGate){
                    if(s.Completed)break;
                    if(s.Lifecycle){
                        if(s.Recipients.Length>=(s.CompareBeforePack?1:3)||s.Recipients.Any(p=>p.Device==peer.Key))continue;
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
    }
}
