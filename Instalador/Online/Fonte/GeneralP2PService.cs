using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

internal sealed class P2PSession {
    public string Id, Name, Hash, Peer, Mode, Destination, ReceiptId, Error, Stage, RelayDevice;
    public bool Lifecycle, Held, ReceiptClosed, ExportAllPresets, CompareBeforePack;
    public volatile bool Discarded;
    public string Phase, TransactionId, ProjectError, ExportPreset;
    public int? ModsOutsidePack;
    public List<OwnedChange> SyncCleanup;
    public int SyncLocalExtrasKept, SyncHostFilesDifferent;
    [System.Web.Script.Serialization.ScriptIgnore] public volatile P2PInstallProgress InstallProgress;
    [System.Web.Script.Serialization.ScriptIgnore] public volatile P2PSyncProgress SyncProgress;
    [System.Web.Script.Serialization.ScriptIgnore] public volatile int SyncMode;
    public P2PRecipient[] Recipients=new P2PRecipient[0];
    [System.Web.Script.Serialization.ScriptIgnore] public bool RecoveryPending;
    public long Size;
    public bool Deferred, Automatic;
    public long PreparationBytes,PreparationTotal;
    public string PayloadHash;
    public string[] SelectedMods, SkippedVerificationMods;
    [System.Web.Script.Serialization.ScriptIgnore] public volatile P2PVerificationIssue VerificationIssue;
    [System.Web.Script.Serialization.ScriptIgnore] public GeneralModList ReviewedProject;
    [System.Web.Script.Serialization.ScriptIgnore] public bool ChoiceNotified;
    [System.Web.Script.Serialization.ScriptIgnore] public volatile bool NexusReviewing;
    public bool Sender, Paused, AutoInstall, Completed, ListImported, HistoryDismissed;
    public override string ToString(){return GeneralStyle.T(Sender?"Enviando · ":"Recebendo · ",Sender?"Sending · ":"Receiving · ")+Name;}
}
internal sealed class P2PTransferStatus {
    internal int Percent;
    internal bool Connected,RemotePaused,Accepted,Unavailable;
    internal P2PRecipientProgress[] Recipients=new P2PRecipientProgress[0];
}
internal sealed partial class P2PService : IDisposable {
    internal readonly string Root, Store;
    internal static string TransferStore {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),GeneralAppPaths.ProductName,"Pacotes P2P");}}
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
    internal Action<P2PSession> RegisterProject;
    internal Func<ModVersionDifference,bool> ChooseVersion;
    internal Func<P2PSession,Task<bool>> ConfirmConnection;
    internal Action<GeneralModList> ReviewReceived;
    internal Action<bool> Notice;
    internal void Notify(bool completed=false){
        var notice=Notice;
        if(notice!=null)try{notice(completed);}catch(Exception ex){OnlineDiagnostics.Record("p2p.sound",null,ex.GetType().Name);}
    }
    internal void NotifyChoice(P2PSession session){
        if(session.ChoiceNotified)return;
        session.ChoiceNotified=true;
        Notify();
    }
    internal string ApplicationData {get{return installedData;}}
    internal bool Installing {get{return installedSessions.Any(s=>s.Stage!=null&&!s.Completed&&String.IsNullOrEmpty(s.Error));}}
    readonly object manifestsLock=new object();
    readonly Dictionary<string,P2PManifest> allowedManifests=new Dictionary<string,P2PManifest>();
    internal P2PService(string root,string store,bool localOnly=false,bool offerRecovery=false){
        diagnosticFixture=localOnly;Root=Path.GetFullPath(root);Store=Path.GetFullPath(store);Safe.NoLinks(Root);Safe.NoLinks(Store);
        if(Root.TrimEnd('\\').Equals(Store.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))throw new IOException("Separe os arquivos privados da pasta de transferência.");
        statePath=Safe.In(Root,"sessions.json");Engine=new SyncthingEngine(Safe.In(Root,"syncthing"),localOnly);
        if(!localOnly){
            Notice=completed=>{if(completed)System.Media.SystemSounds.Asterisk.Play();else System.Media.SystemSounds.Exclamation.Play();};
            ReviewReceived=list=>GeneralNexusInfo.ReviewReceivedList(list);
        }
        if(File.Exists(statePath)){
            if(new FileInfo(statePath).Length>1000000)throw new IOException("Registro de compartilhamentos inválido.");
            Sessions=Safe.Json.Deserialize<List<P2PSession>>(File.ReadAllText(statePath));
        }else Sessions=new List<P2PSession>();
        if(Sessions==null||Sessions.Count>100)throw new IOException("Registro de compartilhamentos inválido.");
        var ids=new HashSet<string>();
        foreach(var session in Sessions){
            Validate(session);
            session.Stage=null;
            if(!ids.Add(session.Id))throw new IOException("Compartilhamento repetido.");
            if(offerRecovery&&!session.Sender&&!session.Completed&&!session.Discarded&&session.Mode=="installed-zip"){
                string folder=Folder(session);
                session.RecoveryPending=!session.Held&&(session.TransactionId!=null||session.Phase!=null||
                    (Directory.Exists(folder)&&Directory.EnumerateFiles(folder).Any(path=>Path.GetFileName(path).IndexOf("pack.zip",StringComparison.OrdinalIgnoreCase)>=0)));
            }
        }
        RefreshSessions();
    }
    static void Validate(P2PSession s){
        if(s==null||!Regex.IsMatch(s.Id??"","\\Aml-[a-f0-9]{32}\\z")||!P2PPack.HashValid(s.Hash)||String.IsNullOrWhiteSpace(s.Name)||s.Name.Length>300||s.Name.Any(Char.IsControl)||(s.Peer!=null&&!P2PInvite.DeviceValid(s.Peer)))throw new IOException("Registro de compartilhamento inválido.");
        if(s.RelayDevice!=null&&(s.Sender||!P2PInvite.DeviceValid(s.RelayDevice)))throw new IOException("Identidade de relay inválida na sessão.");
        if(s.Recipients==null||s.Recipients.Length>3||s.Recipients.Any(p=>p==null||!P2PInvite.DeviceValid(p.Device))||s.Recipients.Select(p=>p.Device).Distinct().Count()!=s.Recipients.Length)
            throw new IOException("Destinatários inválidos.");
        if(s.ExportPreset!=null&&!Regex.IsMatch(s.ExportPreset,@"\A[A-Za-z0-9_-]{1,120}\z"))throw new IOException("Preset de compartilhamento inválido.");
        if(s.TransactionId!=null&&!Regex.IsMatch(s.TransactionId,@"\A[a-f0-9]{32}\z"))
            throw new IOException("Transação inválida.");
        if(s.CompareBeforePack&&(!s.Lifecycle||!s.Automatic||s.Recipients.Length>1))throw new IOException("Registro de comparação inválido.");
        if(s.SyncCleanup!=null&&(s.Sender||!s.CompareBeforePack||s.SyncCleanup.Count>2000||
            s.SyncCleanup.Any(file=>file==null||!FixedInstaller.SyncInventoryEntry(file.Path,file.Before))))
            throw new IOException("A seleção de remoção P2P é inválida.");
        if(s.Automatic&&!s.Deferred)throw new IOException("Registro automático inválido.");
        if(s.Deferred&&(s.Mode!="installed-zip"||s.Size<0||s.Size>P2PPack.MaxTotal||(s.PayloadHash==null?s.Size!=0:!P2PPack.HashValid(s.PayloadHash)||s.Size==0))||s.SelectedMods!=null&&(s.SelectedMods.Length>1000||s.SelectedMods.Any(id=>!Regex.IsMatch(id??"",@"\A[A-Za-z0-9_-]{1,120}\z"))))throw new IOException("Registro antecipado inválido.");
        if(s.Mode!=null&&s.Mode!="installed-zip"||s.Mode=="installed-zip"&&!s.Deferred&&(s.Size<=0||s.Size>P2PPack.MaxTotal))throw new IOException("Registro de pacote instalado inválido.");
        if(s.ReceiptId!=null&&!Regex.IsMatch(s.ReceiptId,"\\A[a-f0-9]{32}\\z"))throw new IOException("Recibo de pacote inválido.");
    }
    internal string Folder(P2PSession s){
        Validate(s);
        string project=InstalledSnapshot.Hash(Encoding.UTF8.GetBytes(Root.ToUpperInvariant())).Substring(0,16);
        return Safe.In(Store,s.Lifecycle?"v2-"+project+"/"+s.Id:s.Id);
    }
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
            foreach(var session in SnapshotSessions()){
                RestoreSession(session,session.Id==excludeFolder);
            }
            RestoreAcceptanceName();
            if(pump!=null)pump.Dispose();pump=new Timer(_=>Pump(),null,1000,1000);
        }catch{Engine.Dispose();throw;}
    }
    void RefreshSessions(){
        receivers=Sessions.Where(s=>!s.Sender).ToArray();
        installedSessions=Sessions.Where(s=>s.Mode=="installed-zip").ToArray();
    }
    internal P2PSession[] SnapshotSessions(){lock(Sessions)return Sessions.ToArray();}
    void Save(){lock(Sessions){Safe.WriteJson(statePath,Sessions);RefreshSessions();}}
    internal void DismissCompletedTransfer(P2PSession session){
        lock(Sessions){
            if(!Sessions.Contains(session)||!session.Completed||session.Discarded||session.HistoryDismissed)return;
            session.HistoryDismissed=true;
            try{Save();}
            catch{session.HistoryDismissed=false;throw;}
            ForgetSyncDifferences(session);
        }
    }
    internal void CancelSend(P2PSession session){
        lock(session){
            if(!SnapshotSessions().Contains(session)||!session.Sender||session.Completed||session.Discarded)return;
            session.Discarded=true;
            try{Save();}
            catch{session.Discarded=false;throw;}
        }
    }
    void CleanCanceledSend(P2PSession session){
        lock(startGate){
            Engine.RemoveFolder(session.Id);
            if(session.Stage!=null)return;
            foreach(var recipient in session.Recipients)CloseReceipt(session,recipient.Device);
            P2PPack.DeleteSnapshot(Folder(session));
            ForgetSession(session);
        }
    }
    void AddSession(P2PSession session){lock(Sessions){Sessions.Add(session);try{Save();}catch{Sessions.Remove(session);throw;}}}
    void ForgetSession(P2PSession session){
        lock(Sessions){
            int index=Sessions.IndexOf(session);
            if(index<0)return;
            Sessions.RemoveAt(index);
            try{Save();}
            catch{Sessions.Insert(index,session);throw;}
        }
    }
    internal void EnableInstalledReceive(FixedCatalog catalog,string appData,bool fixture=false){installedCatalog=catalog;installedData=appData;installedFixture=fixture;}
    void Pump(){
        if(disposed||Interlocked.Exchange(ref pumping,1)!=0)return;
        try{
            try{DetectAcceptances();}catch(Exception ex){OnlineDiagnostics.Record("p2p.accept.waiting",null,ex.GetType().Name);}
            try{ObserveConnectionDiagnostics(DateTime.UtcNow);}catch(Exception ex){OnlineDiagnostics.Record("p2p.diagnostic.observe",null,ex.GetType().Name);}
            foreach(var session in installedSessions){
                if(disposed)break;
                try{ProcessInstalled(session);}
                catch(System.Net.WebException){/* The transport can reconnect without changing the installation state. */}
                catch(Exception ex){if(session.Stage==null){session.Error=ex.Message;Save();}}
            }
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
            Engine.ConfigureFolder(s.Id,folder,s.Name,true);AddSession(s);return s;
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
        if(s.Discarded)throw new IOException("Este recebimento foi descartado. Use um novo convite.");
        if((s.TransactionId!=null||s.CompareBeforePack&&HasSyncChoice(s))&&!String.Equals(s.Destination,destination,StringComparison.OrdinalIgnoreCase))
            throw new IOException("Retome a instalação na pasta original.");
        if(!Directory.Exists(destination))throw new IOException("Escolha uma pasta de instalação existente.");Safe.NoLinks(destination);
        s.Destination=Path.GetFullPath(destination);
        s.AutoInstall=true;s.Error=null;s.Held=false;s.RecoveryPending=false;s.Paused=false;
        Engine.Pause(s.Id,false);Save();
        }
    }
    internal void MarkImported(string receipt){foreach(var session in SnapshotSessions().Where(s=>!s.Sender&&s.Completed&&s.ReceiptId==receipt))session.ListImported=true;Save();}
    void ProcessInstalled(P2PSession s){
        lock(s)ProcessInstalledLocked(s);
    }
    void ProcessInstalledLocked(P2PSession s){
        if(s.Sender&&s.Discarded){CleanCanceledSend(s);return;}
        if(s.Lifecycle&&s.Sender){ProcessOutgoing(s);return;}
        if(!s.Sender&&s.Completed&&s.Stage==null)PublishProject(s);
        if(s.Lifecycle&&(s.Completed||s.Discarded)){if(s.Stage==null)FinishIncoming(s);return;}
        if(s.Lifecycle&&!s.Sender&&s.Phase!=null)PublishNotice(s);
        if(!s.Sender&&s.Stage!=null)return;
        if(s.Paused||s.Held||s.RecoveryPending||s.Discarded||!s.Completed&&s.Error!=null)return;
        if(s.Completed){
            if(!Directory.Exists(Folder(s)))return;
            if(!s.Sender){
                if(s.Peer==null)return;
                var remote=Engine.Get("db/completion?folder="+s.Id+"&device="+s.Peer);
                // Keep advertising the received index until the sender acknowledges delivery by unsharing.
                if(!remote.ContainsKey("remoteState")||Convert.ToString(remote["remoteState"])!="notSharing")return;
            }
            CleanupTransport(s);s.Error=null;Save();return;
        }
        if(s.Sender){
            if(s.Deferred&&!DeferredPrepared(s)){
                if(s.Peer!=null)StartDeferredPreparation(s,()=>RecipientReady(s,s.Peer));
                return;
            }
            if(s.Peer==null)return;
            var completion=Engine.Get("db/completion?folder="+s.Id+"&device="+s.Peer);
            if(!completion.ContainsKey("remoteState")||Convert.ToString(completion["remoteState"])!="valid"||!completion.ContainsKey("globalBytes")||Convert.ToInt64(completion["globalBytes"])<s.Size||Convert.ToDouble(completion["completion"])<100||Convert.ToInt64(completion["needBytes"])!=0)return;
            // Unshare first: deleting the sender's temporary ZIP must not delete the receiver's copy.
            Engine.RemoveFolder(s.Id);s.Completed=true;Save();CleanupTransport(s);return;
        }
        if(!s.AutoInstall||installedCatalog==null||installedData==null||s.Stage!=null)return;
        if(s.CompareBeforePack&&!HasSyncChoice(s))return;
        if(s.Deferred&&!ResolveDeferred(s))return;
        string zip=Safe.In(Folder(s),"pack.zip");
        if(!File.Exists(zip)||new FileInfo(zip).Length!=s.Size)return;
        StartInstall(s,zip);
    }
    void CleanupTransport(P2PSession s){
        lock(startGate){
        Engine.RemoveFolder(s.Id);
        try{P2PPack.DeleteSnapshot(Folder(s));}
        catch(IOException){
            if(SnapshotSessions().Any(other=>other.Id!=s.Id&&!other.Completed&&!other.Discarded&&!other.Paused))throw;
            // A stopped Windows watcher may retain the empty root. Restart only our idle helper.
            string executable=Engine.Executable;Engine.Dispose();
            try{P2PPack.DeleteSnapshot(Folder(s));}
            finally{if(!disposed)Start(_=>{},executable,s.Id);}
        }
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
            Engine.ConfigureFolder(session.Id,folder,session.Name,true);AddSession(session);return session;
        }catch{
            Sessions.Remove(session);try{Engine.RemoveFolder(session.Id);}catch(Exception){}
            try{P2PPack.DeleteSnapshot(folder);}catch(Exception){}throw;
        }
    }
    internal bool InvitationOpen(P2PSession session){
        return session!=null&&session.Sender&&!session.Completed&&!session.Discarded&&
            session.Peer==null&&session.Recipients.Length<(session.CompareBeforePack?1:3);
    }
    internal string OpenInvitation(P2PSession session){
        if(session==null)return null;
        lock(session)return Engine.Running&&InvitationOpen(session)&&SnapshotSessions().Contains(session)?Invitation(session):null;
    }
    internal string Invitation(P2PSession session){
        var invite=new P2PInvite{Schema=session.CompareBeforePack?3:session.Lifecycle?2:1,Kind=session.Sender?"offer":"accept",Device=Engine.Device,
            Folder=session.Id,ManifestHash=session.Hash,Name=session.Name,Mode=session.Mode,Size=session.Deferred?0:session.Size,Deferred=session.Deferred,Automatic=session.Automatic};
        if(session.Sender&&session.Mode=="installed-zip"){
            invite.SuggestHostFolder(session.Destination);
            invite.RelayDevice=Engine.InviteRelayDevice;
        }
        return invite.Encode();
    }
    internal P2PSession Accept(string code,string destination=null,bool autoInstall=false){
        var invite=P2PInvite.Decode(code);if(invite.Kind!="offer")throw new IOException("Cole o convite de quem está enviando o pacote.");
        if(!Engine.Running||invite.Device==Engine.Device)throw new IOException("Use o convite do outro computador.");
        var existing=SnapshotSessions().FirstOrDefault(s=>s.Id==invite.Folder);
        if(existing!=null){
            if(existing.Discarded)throw new IOException("Este recebimento foi descartado. Use um novo convite.");
            if(existing.Hash!=invite.ManifestHash||existing.Sender||existing.Peer!=invite.Device||existing.Deferred!=invite.Deferred||existing.Lifecycle!=(invite.Schema>=2)||existing.CompareBeforePack!=(invite.Schema==3)||existing.Mode!=invite.Mode||(!existing.Deferred&&existing.Size!=invite.Size))throw new IOException("Este convite conflita com outro compartilhamento.");
            if(existing.Automatic!=invite.Automatic&&(existing.Automatic||!invite.Automatic||existing.Completed))
                throw new IOException("Use o convite atualizado enviado pelo amigo.");
            if(invite.RelayDevice!=null&&invite.RelayDevice!=existing.RelayDevice&&!existing.Completed&&!existing.Held&&!existing.Paused){
                Engine.ConfigurePeerWithRelay(existing.Peer,invite.RelayDevice);
                existing.RelayDevice=invite.RelayDevice;Save();
            }
            if(existing.Automatic!=invite.Automatic){
                existing.Automatic=true;AdvertiseAcceptance(existing);Engine.ConfigureFolder(existing.Id,Folder(existing),ReceiveLabel(existing),false,existing.Peer);if(existing.Paused)Engine.Pause(existing.Id,true);Save();RefreshAcceptance(existing);
            }else if(existing.Automatic&&!existing.Completed&&!existing.Held&&!existing.Paused&&existing.PayloadHash==null){
                AdvertiseAcceptance(existing);
                RefreshAcceptance(existing);
            }
            return existing;
        }
        if(Sessions.Count>=100)throw new IOException("Remova compartilhamentos antigos antes de receber outro.");
        if(invite.Mode=="installed-zip"&&autoInstall){if(String.IsNullOrWhiteSpace(destination)||!Directory.Exists(destination))throw new IOException("Escolha a pasta de instalação antes de receber.");Safe.NoLinks(destination);}
        var session=new P2PSession{Id=invite.Folder,Name=invite.Name,Hash=invite.ManifestHash,Peer=invite.Device,Mode=invite.Mode,Size=invite.Size,Deferred=invite.Deferred,Automatic=invite.Automatic,Lifecycle=invite.Schema>=2,CompareBeforePack=invite.Schema==3,Destination=destination,AutoInstall=autoInstall,RelayDevice=invite.RelayDevice};
        string folder=Folder(session);Directory.CreateDirectory(folder);
        // Receive the pinned manifest first. Other remote files are ignored until its allow-list is validated.
        File.WriteAllText(Safe.In(folder,".stignore"),invite.Mode=="installed-zip"?(invite.Deferred?DeferredIgnores(session):"!pack.zip\n*\n"):"!pack.json\n*\n",new UTF8Encoding(false));
        AdvertiseAcceptance(session);Engine.ConfigurePeerWithRelay(session.Peer,session.RelayDevice);Engine.ConfigureFolder(session.Id,folder,ReceiveLabel(session),false,session.Peer);
        try{
            if(session.Lifecycle)ConfigureReceipt(session,session.Peer,false);
            AddSession(session);
        }catch{Engine.RemoveFolder(session.Id);throw;}
        return session;
    }
    internal P2PSession Authorize(string code){
        var answer=P2PInvite.Decode(code);if(answer.Kind!="accept")throw new IOException("Cole a resposta enviada por seu amigo.");
        var s=SnapshotSessions().SingleOrDefault(x=>x.Id==answer.Folder&&x.Sender);
        if(s==null)throw new IOException("A resposta não pertence a um envio ativo.");
        lock(s){
            if(answer.Schema!=(s.CompareBeforePack?3:s.Lifecycle?2:1)||s.Completed||s.Discarded||s.Hash!=answer.ManifestHash||s.Mode!=answer.Mode||s.Deferred!=answer.Deferred||s.Automatic!=answer.Automatic||(!s.Deferred&&s.Size!=answer.Size)||answer.Device==Engine.Device)throw new IOException("A resposta não pertence a um envio ativo.");
            if(s.Lifecycle){
                if(s.Recipients.Any(peer=>peer.Device==answer.Device))return s;
                if(s.Recipients.Length>=(s.CompareBeforePack?1:3))throw new IOException("Este convite já atingiu o limite de destinatários. Crie outro compartilhamento.");
                lock(startGate)AddRecipient(s,answer.Device);
                return s;
            }
            if(s.Peer!=null&&s.Peer!=answer.Device)throw new IOException("Este convite já está vinculado a outro amigo. Crie outro compartilhamento.");
            Engine.ConfigurePeer(answer.Device);Engine.ConfigureFolder(s.Id,Folder(s),s.Name,true,answer.Device);if(s.Paused)Engine.Pause(s.Id,true);s.Peer=answer.Device;Save();return s;
        }
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
    internal void Pause(P2PSession s){lock(s){if(s.Stage!=null&&!s.Sender||s.Completed||s.Discarded||s.Held)throw new IOException("A transferência terminou ou a instalação já começou; aguarde terminar.");Engine.Pause(s.Id,!s.Paused);s.Paused=!s.Paused;Save();}}
    internal P2PTransferStatus TransferStatus(P2PSession s){
        Validate(s);if(s.Lifecycle&&s.Sender)return OutgoingStatus(s);var result=new P2PTransferStatus();if(s.Completed){result.Percent=100;return result;}if(s.Phase!=null&&s.Phase!="discarded")result.Percent=100;if(s.Peer==null)return result;
        var connections=Engine.Get("system/connections")["connections"] as Dictionary<string,object>;object raw;
        if(connections!=null&&connections.TryGetValue(s.Peer,out raw))result.Connected=Convert.ToBoolean(((Dictionary<string,object>)raw)["connected"]);
        if(!result.Connected||s.Paused)return result;
        var completion=Engine.Get("db/completion?folder="+s.Id+"&device="+s.Peer);
        result.Accepted=completion.ContainsKey("remoteState")&&(Convert.ToString(completion["remoteState"])=="valid"||Convert.ToString(completion["remoteState"])=="paused");
        result.RemotePaused=completion.ContainsKey("remoteState")&&Convert.ToString(completion["remoteState"])=="paused";
        if(s.Size<=0||result.Percent==100)return result;
        if(s.Sender)result.Percent=(int)Math.Max(0,Math.Min(99,Convert.ToDouble(completion["completion"])));
        else{var local=Engine.Get("db/status?folder="+s.Id);long global=Convert.ToInt64(local["globalBytes"]),needed=Convert.ToInt64(local["needBytes"]);result.Percent=global==0?0:(int)Math.Max(0,Math.Min(99,(s.Size-needed)*100.0/s.Size));}
        long partial=PartialBytes(s);if(partial>0)result.Percent=Math.Max(result.Percent,(int)Math.Min(99,partial*100.0/s.Size));
        return result;
    }
    long PartialBytes(P2PSession s,string recipient=null){
        // Syncthing's needBytes counts unfinished files, not their downloaded blocks.
        var events=Engine.Request("GET","events?events="+(s.Sender?"RemoteDownloadProgress":"DownloadProgress")+"&limit=16&timeout=0",null) as System.Collections.IEnumerable;
        if(events==null)return 0;
        foreach(var entry in events.Cast<object>().OfType<Dictionary<string,object>>().Reverse()){
            var data=entry["data"] as Dictionary<string,object>;if(data==null)continue;object folderRaw,fileRaw;
            if(s.Sender){
                if(Convert.ToString(data["folder"])!=s.Id||Convert.ToString(data["device"])!=(recipient??s.Peer))continue;
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
        lock(s)lock(startGate){
        Validate(s);
        if(s.Stage!=null)throw new IOException("Aguarde a preparação terminar.");
        s.AutoInstall=false;s.Paused=true;Save();
        if(s.Lifecycle&&s.Sender)foreach(var recipient in s.Recipients)CloseReceipt(s,recipient.Device);
        if(!s.Completed)Engine.Pause(s.Id,true);
        CleanupTransport(s);ForgetSession(s);
        lock(manifestsLock)allowedManifests.Remove(s.Id);
        if(s.Peer!=null&&!SnapshotSessions().Any(other=>other.Peer==s.Peer||other.Recipients.Any(p=>p.Device==s.Peer)))Engine.Request("DELETE","config/devices/"+s.Peer,null);
        // Only this session's transport snapshot is removed; private imports, originals and installations remain.
        }
    }
    public void Dispose(){disposed=true;if(pump!=null){pump.Dispose();pump=null;}Engine.Dispose();}
}
