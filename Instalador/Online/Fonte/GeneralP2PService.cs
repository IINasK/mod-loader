using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

internal sealed class P2PSession {
    public string Id, Name, Hash, Peer, Mode, Destination, ReceiptId, Error, Stage;
    public long Size;
    public bool Sender, Paused, AutoInstall, Completed, ListImported;
    public override string ToString(){return GeneralStyle.T(Sender?"Enviando · ":"Recebendo · ",Sender?"Sending · ":"Receiving · ")+Name;}
}
internal sealed class P2PTransferStatus {
    internal int Percent;
    internal bool Connected,RemotePaused;
}
internal sealed class P2PService : IDisposable {
    internal readonly string Root, Store;
    internal readonly SyncthingEngine Engine;
    internal readonly List<P2PSession> Sessions;
    readonly string statePath;
    readonly object startGate=new object();
    Timer pump;
    int pumping;
    volatile bool disposed;
    P2PSession[] receivers=new P2PSession[0];
    P2PSession[] installedSessions=new P2PSession[0];
    FixedCatalog installedCatalog;
    string installedData;
    bool installedFixture;
    internal event Action<P2PSession> SnapshotInstalled;
    internal string ApplicationData {get{return installedData;}}
    internal bool Installing {get{return installedSessions.Any(s=>s.Stage!=null&&!s.Completed&&String.IsNullOrEmpty(s.Error));}}
    readonly object manifestsLock=new object();
    readonly Dictionary<string,P2PManifest> allowedManifests=new Dictionary<string,P2PManifest>();
    internal P2PService(string root,string store,bool localOnly=false){
        Root=Path.GetFullPath(root);Store=Path.GetFullPath(store);Safe.NoLinks(Root);Safe.NoLinks(Store);
        if(Root.TrimEnd('\\').Equals(Store.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))throw new IOException("Separe os arquivos privados da pasta de transferência.");
        statePath=Safe.In(Root,"sessions.json");Engine=new SyncthingEngine(Safe.In(Root,"syncthing"),localOnly);
        if(File.Exists(statePath)){
            if(new FileInfo(statePath).Length>1000000)throw new IOException("Registro de compartilhamentos inválido.");
            Sessions=Safe.Json.Deserialize<List<P2PSession>>(File.ReadAllText(statePath));
        }else Sessions=new List<P2PSession>();
        if(Sessions==null||Sessions.Count>100)throw new IOException("Registro de compartilhamentos inválido.");
        var ids=new HashSet<string>();foreach(var s in Sessions){Validate(s);s.Stage=null;if(!ids.Add(s.Id))throw new IOException("Compartilhamento repetido.");}
    }
    static void Validate(P2PSession s){
        if(s==null||!Regex.IsMatch(s.Id??"","\\Aml-[a-f0-9]{32}\\z")||!P2PPack.HashValid(s.Hash)||String.IsNullOrWhiteSpace(s.Name)||s.Name.Length>300||s.Name.Any(Char.IsControl)||(s.Peer!=null&&!P2PInvite.DeviceValid(s.Peer)))throw new IOException("Registro de compartilhamento inválido.");
        if(s.Mode!=null&&s.Mode!="installed-zip"||s.Mode=="installed-zip"&&(s.Size<=0||s.Size>P2PPack.MaxTotal))throw new IOException("Registro de pacote instalado inválido.");
        if(s.ReceiptId!=null&&!Regex.IsMatch(s.ReceiptId,"\\A[a-f0-9]{32}\\z"))throw new IOException("Recibo de pacote inválido.");
    }
    internal string Folder(P2PSession s){Validate(s);return Safe.In(Store,s.Id);}
    internal string PrivateFolder(P2PSession s){Validate(s);return Safe.In(Root,"received");}
    internal void Start(Action<string> report,string testExe=null,string excludeFolder=null){
        lock(startGate)StartCore(report,testExe,excludeFolder);
    }
    void StartCore(Action<string> report,string testExe,string excludeFolder){
        if(disposed)throw new ObjectDisposedException("P2PService");
        if(Engine.Running)return;
        try{
            CleanupPreparation();
            Engine.Start(testExe??SyncthingEngine.Ensure(Root,report));
            if(disposed)throw new ObjectDisposedException("P2PService");
            lock(manifestsLock)allowedManifests.Clear();
            foreach(var s in Sessions){if(s.Id==excludeFolder||s.Mode=="installed-zip"&&s.Completed)continue;if(s.Peer!=null)Engine.ConfigurePeer(s.Peer);Engine.ConfigureFolder(s.Id,Folder(s),s.Name,s.Sender,s.Peer);if(s.Paused)Engine.Pause(s.Id,true);}
            receivers=Sessions.Where(s=>!s.Sender).ToArray();
            installedSessions=Sessions.Where(s=>s.Mode=="installed-zip").ToArray();
            if(pump!=null)pump.Dispose();pump=new Timer(_=>Pump(),null,2000,3000);
        }catch{Engine.Dispose();throw;}
    }
    void Save(){Safe.WriteJson(statePath,Sessions);receivers=Sessions.Where(s=>!s.Sender).ToArray();installedSessions=Sessions.Where(s=>s.Mode=="installed-zip").ToArray();}
    internal void EnableInstalledReceive(FixedCatalog catalog,string appData,bool fixture=false){installedCatalog=catalog;installedData=appData;installedFixture=fixture;}
    void Pump(){
        if(disposed||Interlocked.Exchange(ref pumping,1)!=0)return;
        try{
            foreach(var s in installedSessions){if(disposed)break;if(s.Paused||!s.Completed&&!String.IsNullOrEmpty(s.Error))continue;try{ProcessInstalled(s);}catch(Exception ex){s.Error=ex.Message;s.Stage=null;Save();}}
            foreach(var s in receivers){if(disposed)break;if(s.Paused||s.Mode=="installed-zip")continue;try{if(File.Exists(Safe.In(Folder(s),"pack.json")))AllowFiles(s);}catch(Exception ex){OnlineDiagnostics.Record("p2p.waiting",null,"Aguardando manifesto conferido.",new{ErrorType=ex.GetType().Name});}}
        }
        finally{Interlocked.Exchange(ref pumping,0);}
    }
    internal P2PSession ShareInstalled(GeneralModState state,IEnumerable<string> ids,FixedCatalog catalog,string appData,bool includeConfigs,Action<string> report){
        if(!Engine.Running||Sessions.Count>=100)throw new IOException("Inicie a conexão ou remova compartilhamentos antigos.");
        var s=new P2PSession{Id="ml-"+Guid.NewGuid().ToString("N"),Sender=true,Mode="installed-zip"};
        string folder=FolderForNew(s.Id);
        if(!String.IsNullOrEmpty(state.Destination)&&Path.GetFullPath(folder).StartsWith(Path.GetFullPath(state.Destination).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("A transferência deve ficar fora da pasta do jogo.");
        Directory.CreateDirectory(folder);string zip=Safe.In(folder,"pack.zip");
        string preparation=Safe.In(Root,"creating.json");Safe.WriteJson(preparation,new{Id=s.Id});
        try{
            var manifest=InstalledSnapshot.Create(zip,state,ids,catalog,appData,includeConfigs,report,true);
            s.Name=manifest.Name;s.Hash=Safe.Hash(zip);s.Size=new FileInfo(zip).Length;
            Engine.ConfigureFolder(s.Id,folder,s.Name,true);Sessions.Add(s);Save();return s;
        }catch{Sessions.Remove(s);try{Engine.RemoveFolder(s.Id);}catch(Exception){}try{P2PPack.DeleteSnapshot(folder);}catch(Exception){}throw;}
        finally{Safe.NoLinks(preparation);if((Sessions.Any(x=>x.Id==s.Id)||!Directory.Exists(folder))&&File.Exists(preparation))File.Delete(preparation);}
    }
    void CleanupPreparation(){
        string marker=Safe.In(Root,"creating.json");Safe.NoLinks(marker);if(!File.Exists(marker))return;
        if(new FileInfo(marker).Length>1024)throw new IOException("Registro de preparação inválido.");
        var record=Safe.Json.Deserialize<Dictionary<string,string>>(File.ReadAllText(marker));string id;
        if(record==null||record.Count!=1||!record.TryGetValue("Id",out id)||!Regex.IsMatch(id??"","\\Aml-[a-f0-9]{32}\\z"))throw new IOException("Registro de preparação inválido.");
        if(!Sessions.Any(s=>s.Id==id))P2PPack.DeleteSnapshot(Safe.In(Store,id));File.Delete(marker);
    }
    string FolderForNew(string id){return Safe.In(Store,id);}
    internal InstalledSnapshotManifest InstalledManifest(P2PSession s){
        if(s.ReceiptId==null||installedCatalog==null)throw new IOException("O pacote ainda não foi instalado.");
        return new InstalledSnapshotInstaller(s.Destination,installedData,installedCatalog,installedFixture).ReadManifest(s.ReceiptId);
    }
    internal void RetryInstalled(P2PSession s,string destination){
        lock(s){
        if(s.Mode!="installed-zip"||s.Sender||s.Completed)throw new IOException("Selecione um pacote recebido ainda não instalado.");
        if(s.Stage!=null)throw new IOException("A instalação já começou. Aguarde terminar.");
        if(!Directory.Exists(destination))throw new IOException("Escolha uma pasta de instalação existente.");Safe.NoLinks(destination);
        s.Destination=Path.GetFullPath(destination);s.AutoInstall=true;s.Error=null;Save();
        }
    }
    internal void MarkImported(string receipt){foreach(var session in Sessions.Where(s=>!s.Sender&&s.Completed&&s.ReceiptId==receipt))session.ListImported=true;Save();}
    void ProcessInstalled(P2PSession s){
        lock(s)ProcessInstalledLocked(s);
    }
    void ProcessInstalledLocked(P2PSession s){
        if(s.Completed){if(Directory.Exists(Folder(s))){CleanupTransport(s);s.Error=null;Save();}return;}
        if(s.Sender){
            if(s.Peer==null)return;
            var completion=Engine.Get("db/completion?folder="+s.Id+"&device="+s.Peer);
            if(!completion.ContainsKey("globalBytes")||Convert.ToInt64(completion["globalBytes"])<s.Size||Convert.ToDouble(completion["completion"])<100||Convert.ToInt64(completion["needBytes"])!=0)return;
            // Unshare first: deleting the sender's temporary ZIP must not delete the receiver's copy.
            Engine.RemoveFolder(s.Id);s.Completed=true;Save();CleanupTransport(s);return;
        }
        if(!s.AutoInstall||installedCatalog==null||installedData==null)return;
        string zip=Safe.In(Folder(s),"pack.zip");
        if(!File.Exists(zip)||new FileInfo(zip).Length!=s.Size)return;
        s.Stage="Conferindo e instalando…";
        if(Safe.Hash(zip)!=s.Hash)throw new IOException("O ZIP recebido não corresponde ao convite. A instalação foi cancelada.");
        var installer=new InstalledSnapshotInstaller(s.Destination,installedData,installedCatalog,installedFixture);
        s.ReceiptId=installer.Install(zip,s.Hash,message=>s.Stage=message);s.Completed=true;s.Error=null;s.Stage=null;Save();
        var handler=SnapshotInstalled;if(handler!=null)handler(s);
        CleanupTransport(s);
    }
    void CleanupTransport(P2PSession s){
        Engine.RemoveFolder(s.Id);
        try{P2PPack.DeleteSnapshot(Folder(s));}
        catch(IOException){
            if(Sessions.Any(other=>other.Id!=s.Id&&!other.Completed&&!other.Paused))throw;
            // A stopped Windows watcher may retain the empty root. Restart only our idle helper.
            string executable=Engine.Executable;Engine.Dispose();
            try{P2PPack.DeleteSnapshot(Folder(s));}
            finally{if(!disposed)Start(_=>{},executable,s.Id);}
        }
    }
    internal P2PSession Share(GeneralModState state,IEnumerable<string> ids,FixedCatalog catalog,DownloadCache cache,Action<string> report){
        if(!Engine.Running||Sessions.Count>=100)throw new IOException("Inicie a conexão ou remova compartilhamentos antigos.");
        var session=new P2PSession{Id="ml-"+Guid.NewGuid().ToString("N"),Sender=true};
        // Never allow exporting original archives directly from the active destination tree.
        string folder=Safe.In(Store,session.Id);
        if(!String.IsNullOrEmpty(state.Destination)&&Path.GetFullPath(folder).StartsWith(Path.GetFullPath(state.Destination).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("A pasta de compartilhamento deve ficar fora da instalação do jogo.");
        try{
            var pack=P2PPack.Create(folder,state,ids,catalog,cache,report);session.Name=pack.Name;session.Hash=Safe.Hash(Safe.In(folder,"pack.json"));
            Engine.ConfigureFolder(session.Id,folder,session.Name,true);Sessions.Add(session);Save();return session;
        }catch{
            Sessions.Remove(session);try{Engine.RemoveFolder(session.Id);}catch(Exception){}
            try{P2PPack.DeleteSnapshot(folder);}catch(Exception){}throw;
        }
    }
    internal string Invitation(P2PSession session){return new P2PInvite{Kind=session.Sender?"offer":"accept",Device=Engine.Device,Folder=session.Id,ManifestHash=session.Hash,Name=session.Name,Mode=session.Mode,Size=session.Size}.Encode();}
    internal P2PSession Accept(string code,string destination=null,bool autoInstall=false){
        var invite=P2PInvite.Decode(code);if(invite.Kind!="offer")throw new IOException("Cole o convite de quem está enviando o pacote.");
        if(!Engine.Running||invite.Device==Engine.Device)throw new IOException("Use o convite do outro computador.");
        var existing=Sessions.FirstOrDefault(s=>s.Id==invite.Folder);
        if(existing!=null){if(existing.Hash!=invite.ManifestHash||existing.Sender||existing.Peer!=invite.Device)throw new IOException("Este convite conflita com outro compartilhamento.");return existing;}
        if(Sessions.Count>=100)throw new IOException("Remova compartilhamentos antigos antes de receber outro.");
        if(invite.Mode=="installed-zip"&&autoInstall){if(String.IsNullOrWhiteSpace(destination)||!Directory.Exists(destination))throw new IOException("Escolha a pasta de instalação antes de receber.");Safe.NoLinks(destination);}
        var session=new P2PSession{Id=invite.Folder,Name=invite.Name,Hash=invite.ManifestHash,Peer=invite.Device,Mode=invite.Mode,Size=invite.Size,Destination=destination,AutoInstall=autoInstall};
        string folder=Folder(session);Directory.CreateDirectory(folder);
        // Receive the pinned manifest first. Other remote files are ignored until its allow-list is validated.
        File.WriteAllText(Safe.In(folder,".stignore"),invite.Mode=="installed-zip"?"!pack.zip\n*\n":"!pack.json\n*\n",new UTF8Encoding(false));
        Engine.ConfigurePeer(session.Peer);Engine.ConfigureFolder(session.Id,folder,session.Name,false,session.Peer);
        Sessions.Add(session);try{Save();}catch{Sessions.Remove(session);Engine.RemoveFolder(session.Id);throw;}return session;
    }
    internal P2PSession Authorize(string code){
        var answer=P2PInvite.Decode(code);if(answer.Kind!="accept")throw new IOException("Cole a resposta enviada por seu amigo.");
        var s=Sessions.SingleOrDefault(x=>x.Id==answer.Folder&&x.Sender);
        if(s==null||s.Completed||s.Hash!=answer.ManifestHash||s.Mode!=answer.Mode||s.Size!=answer.Size||answer.Device==Engine.Device)throw new IOException("A resposta não pertence a um envio ativo.");
        if(s.Peer!=null&&s.Peer!=answer.Device)throw new IOException("Este convite já está vinculado a outro amigo. Crie outro compartilhamento.");
        Engine.ConfigurePeer(answer.Device);Engine.ConfigureFolder(s.Id,Folder(s),s.Name,true,answer.Device);if(s.Paused)Engine.Pause(s.Id,true);s.Peer=answer.Device;Save();return s;
    }
    internal P2PManifest AllowFiles(P2PSession s){
        lock(manifestsLock){
        P2PManifest approved;if(allowedManifests.TryGetValue(s.Id,out approved))return approved;
        var m=P2PPack.Read(Folder(s),s.Hash);
        var ignores=new List<string>{"!pack.json"};ignores.AddRange(m.Files.Select(f=>"!"+f.File));ignores.Add("*");
        string text=String.Join("\n",ignores)+"\n",path=Safe.In(Folder(s),".stignore");
        if(!File.Exists(path)||File.ReadAllText(path)!=text){
            Engine.Request("POST","db/ignores?folder="+s.Id,new{ignore=ignores});
        }
        allowedManifests.Add(s.Id,m);return m;
        }
    }
    internal void Pause(P2PSession s){lock(s){if(s.Stage!=null||s.Completed)throw new IOException("A transferência terminou ou a instalação já começou; aguarde terminar.");Engine.Pause(s.Id,!s.Paused);s.Paused=!s.Paused;Save();}}
    internal P2PTransferStatus TransferStatus(P2PSession s){
        Validate(s);var result=new P2PTransferStatus();if(s.Completed){result.Percent=100;return result;}if(s.Peer==null)return result;
        var connections=Engine.Get("system/connections")["connections"] as Dictionary<string,object>;object raw;
        if(connections!=null&&connections.TryGetValue(s.Peer,out raw))result.Connected=Convert.ToBoolean(((Dictionary<string,object>)raw)["connected"]);
        if(!result.Connected||s.Paused)return result;
        var completion=Engine.Get("db/completion?folder="+s.Id+"&device="+s.Peer);
        result.RemotePaused=completion.ContainsKey("remoteState")&&Convert.ToString(completion["remoteState"])=="paused";
        if(s.Sender)result.Percent=(int)Math.Max(0,Math.Min(99,Convert.ToDouble(completion["completion"])));
        else{var local=Engine.Get("db/status?folder="+s.Id);long global=Convert.ToInt64(local["globalBytes"]),needed=Convert.ToInt64(local["needBytes"]);result.Percent=global==0?0:(int)Math.Max(0,Math.Min(99,(s.Size-needed)*100.0/s.Size));}
        long partial=PartialBytes(s);if(partial>0)result.Percent=Math.Max(result.Percent,(int)Math.Min(99,partial*100.0/s.Size));
        return result;
    }
    long PartialBytes(P2PSession s){
        // Syncthing's needBytes counts unfinished files, not their downloaded blocks.
        var events=Engine.Request("GET","events?events="+(s.Sender?"RemoteDownloadProgress":"DownloadProgress")+"&limit=16&timeout=0",null) as System.Collections.IEnumerable;
        if(events==null)return 0;
        foreach(var entry in events.Cast<object>().OfType<Dictionary<string,object>>().Reverse()){
            var data=entry["data"] as Dictionary<string,object>;if(data==null)continue;object folderRaw,fileRaw;
            if(s.Sender){
                if(Convert.ToString(data["folder"])!=s.Id||Convert.ToString(data["device"])!=s.Peer)continue;
                var files=data["state"] as Dictionary<string,object>;if(files==null||!files.TryGetValue("pack.zip",out fileRaw))return 0;
                var file=Engine.Get("db/file?folder="+s.Id+"&file=pack.zip")["local"] as Dictionary<string,object>;
                return file!=null&&file.ContainsKey("blockSize")?Math.Min(s.Size,Convert.ToInt64(fileRaw)*Convert.ToInt64(file["blockSize"])):0;
            }
            if(!data.TryGetValue(s.Id,out folderRaw))return 0;
            var folder=folderRaw as Dictionary<string,object>;if(folder==null||!folder.TryGetValue("pack.zip",out fileRaw))return 0;
            var progress=fileRaw as Dictionary<string,object>;return progress!=null&&progress.ContainsKey("bytesDone")?Math.Min(s.Size,Convert.ToInt64(progress["bytesDone"])):0;
        }
        return 0;
    }
    internal void Remove(P2PSession s){
        Validate(s);s.AutoInstall=false;s.Paused=true;Save();
        if(!s.Completed)Engine.Pause(s.Id,true);
        CleanupTransport(s);Sessions.Remove(s);Save();
        lock(manifestsLock)allowedManifests.Remove(s.Id);
        if(s.Peer!=null&&!Sessions.Any(other=>other.Peer==s.Peer))Engine.Request("DELETE","config/devices/"+s.Peer,null);
        // Only this session's transport snapshot is removed; private imports, originals and installations remain.
    }
    public void Dispose(){disposed=true;if(pump!=null){pump.Dispose();pump=null;}Engine.Dispose();}
}
