using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text;
using System.Threading.Tasks;

internal sealed class P2PRecipient {
    public string Device, Phase;
    public bool ReceiptClosed;
}

public sealed class P2PInstallProgress {
    public readonly bool Writing;
    public readonly int Done, Total;
    public readonly string File;
    internal P2PInstallProgress(bool writing,int done,int total,string file){
        Writing=writing;Done=done;Total=total;File=file;
    }
}

internal sealed class P2PRecipientProgress {
    internal string Caption;
    internal int Percent;
    internal bool Finished;
}

internal sealed partial class P2PService {
    sealed class InstallNotice {
        public string Phase, Hash, Tag;
    }

    static readonly string[] NoticePhases = { "review", "installing", "retry", "installed", "discarded" };
    static readonly string EmptyHash = new string('0', 64);
    volatile GeneralModState preparationState;
    int installing;

    internal void SetPreparationState(GeneralModState state) {
        preparationState = state==null?null:Safe.Json.Deserialize<GeneralModState>(Safe.Json.Serialize(state));
    }

    string ReceiptFolderId(P2PSession session, string recipient) {
        return "ml-" + InstalledSnapshot.Hash(Encoding.UTF8.GetBytes("install-status\n" + session.Id + "\n" + recipient)).Substring(0, 32);
    }

    string NoticeDirectory(P2PSession session, string recipient) {
        return Safe.In(Root, "transfer-status/" + ReceiptFolderId(session, recipient));
    }

    void ConfigureReceipt(P2PSession session, string remote, bool host) {
        string recipient = host ? remote : Engine.Device;
        string folder = NoticeDirectory(session, recipient);
        Directory.CreateDirectory(folder);
        string ignore=Safe.In(folder,".stignore");
        const string rules="!status.json\n*\n";
        Safe.NoLinks(ignore);
        if(!File.Exists(ignore))File.WriteAllText(ignore,rules);
        else if(new FileInfo(ignore).Length>128||File.ReadAllText(ignore)!=rules)
            throw new IOException("As regras da pasta de status P2P foram alteradas.");
        Engine.ConfigureFolder(ReceiptFolderId(session, recipient), folder, "Mod Loader install status", !host, remote);
    }

    void RestoreSession(P2PSession session,bool skipPayload) {
        if(session.Lifecycle&&session.Sender) {
            foreach(var recipient in session.Recipients.Where(peer=>!peer.ReceiptClosed)) {
                Engine.ConfigurePeer(recipient.Device);
                ConfigureReceipt(session,recipient.Device,true);
            }
            if(!session.Completed&&!skipPayload)ConfigureOutgoing(session);
            return;
        }
        if(session.Peer!=null)Engine.ConfigurePeer(session.Peer);
        if(session.Lifecycle&&!session.Sender&&!session.ReceiptClosed)ConfigureReceipt(session,session.Peer,false);
        if(skipPayload||session.Discarded||session.Mode=="installed-zip"&&session.Completed&&(session.Sender||session.Lifecycle||!Directory.Exists(Folder(session))))return;
        Engine.ConfigureSharedFolder(session.Id,Folder(session),session.Sender?session.Name:ReceiveLabel(session),
            session.Sender,session.Peer==null?new string[0]:new[]{session.Peer},session.Paused||session.Held||session.RecoveryPending);
    }

    void ConfigureOutgoing(P2PSession session) {
        Engine.ConfigureSharedFolder(session.Id,Folder(session),session.Name,true,
            session.Recipients.Where(peer=>!Terminal(peer.Phase)).Select(peer=>peer.Device),session.Paused);
    }

    void AddRecipient(P2PSession session,string device) {
        Engine.ConfigurePeer(device);
        ConfigureReceipt(session,device,true);
        var previous=session.Recipients;
        session.Recipients=previous.Concat(new[]{new P2PRecipient{Device=device}}).ToArray();
        try{Save();ConfigureOutgoing(session);}
        catch{
            session.Recipients=previous;
            Save();
            throw;
        }
    }

    static bool Terminal(string phase) { return phase=="installed"||phase=="discarded"; }

    string NoticeTag(P2PSession session,string recipient,string phase,string hash) {
        return ReadyTag(session.Hash,session.Id+"\ninstall-status\n"+recipient+"\n"+phase,hash,0);
    }

    void PublishNotice(P2PSession session) {
        if(!session.Lifecycle||session.ReceiptClosed||disposed||!Engine.Running)return;
        string directory=NoticeDirectory(session,Engine.Device);
        string path=Safe.In(directory,"status.json");
        string hash=session.PayloadHash??EmptyHash;
        var notice=new InstallNotice{Phase=session.Phase,Hash=hash,Tag=NoticeTag(session,Engine.Device,session.Phase,hash)};
        string json=Safe.Json.Serialize(notice);
        if(File.Exists(path)&&new FileInfo(path).Length<=2048&&File.ReadAllText(path)==json)return;
        Safe.WriteJson(path,notice);
        Engine.Request("POST","db/scan?folder="+ReceiptFolderId(session,Engine.Device),null);
    }

    InstallNotice ReadNotice(P2PSession session,P2PRecipient recipient) {
        string path=Safe.In(NoticeDirectory(session,recipient.Device),"status.json");
        if(!File.Exists(path)||new FileInfo(path).Length>2048)return null;
        InstallNotice notice;
        try{notice=Safe.Json.Deserialize<InstallNotice>(File.ReadAllText(path));}
        catch(ArgumentException){return null;}
        if(notice==null||!NoticePhases.Contains(notice.Phase)||!P2PPack.HashValid(notice.Hash)||!P2PPack.HashValid(notice.Tag))return null;
        if(notice.Hash!=session.PayloadHash&&(notice.Phase!="discarded"||notice.Hash!=EmptyHash))return null;
        string expected=NoticeTag(session,recipient.Device,notice.Phase,notice.Hash);
        int different=0;
        for(int i=0;i<expected.Length;i++)different|=expected[i]^notice.Tag[i];
        return different==0?notice:null;
    }

    void ProcessOutgoing(P2PSession session) {
        if(session.Completed){
            if(Directory.Exists(Folder(session)))CleanupTransport(session);
            foreach(var recipient in session.Recipients.Where(peer=>!peer.ReceiptClosed)){
                var completion=Engine.Get("db/completion?folder="+ReceiptFolderId(session,recipient.Device)+"&device="+recipient.Device);
                if(completion==null||!completion.ContainsKey("remoteState")||Convert.ToString(completion["remoteState"])!="notSharing")continue;
                CloseReceipt(session,recipient.Device);
                recipient.ReceiptClosed=true;Save();
            }
            return;
        }
        foreach(var recipient in session.Recipients){
            if(Terminal(recipient.Phase))continue;
            var notice=ReadNotice(session,recipient);
            if(notice==null||recipient.Phase==notice.Phase)continue;
            recipient.Phase=notice.Phase;
            Save();
        }
        if(session.Recipients.Length!=0&&session.Recipients.All(peer=>Terminal(peer.Phase))){
            // The receipt is durable before the shared ZIP is removed.
            if(session.Stage!=null)return;
            session.Completed=true;Save();
            Engine.RemoveFolder(session.Id);
            CleanupTransport(session);
            return;
        }
        StartDeferredPreparation(session,()=>session.Recipients.Any(peer=>!Terminal(peer.Phase)&&RecipientReady(session,peer.Device)));
    }

    void StartDeferredPreparation(P2PSession session,Func<bool> ready){
        if(session.Paused||session.Stage!=null||session.Error!=null||DeferredPrepared(session))return;
        var state=preparationState;
        if(state==null||installedCatalog==null||installedData==null||!ready())return;
        if(!String.Equals(session.Destination,state.Destination,StringComparison.OrdinalIgnoreCase)){
            session.Error="A pasta de origem do pacote mudou. Abra o projeto com a pasta original e tente novamente.";
            Save();
            return;
        }
        session.Stage="Preparando pacote…";
        Task.Run(()=>{
            try{PrepareDeferred(session,state,installedCatalog,installedData,true,null);}
            catch(Exception ex){if(!disposed){session.Error=ex.Message;session.Stage=null;Save();}}
        });
    }

    bool RecipientReady(P2PSession session,string device) {
        var completion=Engine.Get("db/completion?folder="+session.Id+"&device="+device);
        return completion!=null&&completion.ContainsKey("remoteState")&&Convert.ToString(completion["remoteState"])=="valid";
    }

    P2PTransferStatus OutgoingStatus(P2PSession session) {
        var result=new P2PTransferStatus();
        var lines=new List<P2PRecipientProgress>();
        int index=0,total=0,measured=0;
        foreach(var recipient in session.Recipients){
            index++;
            string label=GeneralStyle.T("Amigo ","Friend ")+index;
            if(recipient.Phase=="discarded"){
                lines.Add(new P2PRecipientProgress{Caption=label+" · "+PhaseLabel(recipient.Phase),Finished=true});
                continue;
            }
            measured++;
            if(Terminal(recipient.Phase)){
                total+=100;
                lines.Add(new P2PRecipientProgress{Caption=label+" · "+PhaseLabel(recipient.Phase),Percent=100,Finished=true});
                continue;
            }
            int percent=0;
            bool paused=session.Paused;
            if(Engine.Running){
                var status=Engine.Get("db/completion?folder="+session.Id+"&device="+recipient.Device);
                if(status!=null&&status.ContainsKey("remoteState")){
                    string remote=Convert.ToString(status["remoteState"]);
                    paused|=remote=="paused";
                    result.Connected|=remote=="valid"||remote=="paused";
                    result.Accepted|=remote=="valid"||remote=="paused";
                    if(session.Size>0&&status.ContainsKey("globalBytes")&&Convert.ToInt64(status["globalBytes"])>=session.Size)
                        percent=(int)Math.Max(0,Math.Min(100,Convert.ToDouble(status["completion"])));
                }
            }
            if(recipient.Phase!=null)percent=100;
            else if(Engine.Running&&session.Size>0&&percent<100){
                long partial=PartialBytes(session,recipient.Device);
                percent=Math.Max(percent,(int)Math.Min(99,partial*100.0/session.Size));
            }
            total+=percent;
            string detail=recipient.Phase!=null?PhaseLabel(recipient.Phase)
                : percent==100?GeneralStyle.T("Aguardando instalação","Waiting for installation")
                : paused?GeneralStyle.T("Pausado","Paused")
                : GeneralStyle.T("Enviando","Sending");
            lines.Add(new P2PRecipientProgress{Caption=label+" · "+detail,Percent=percent,Finished=percent==100});
        }
        result.Recipients=lines.ToArray();
        result.Percent=measured==0?0:total/measured;
        return result;
    }

    internal static string PhaseLabel(string phase) {
        switch(phase){
            case "review":return GeneralStyle.T("Aguardando escolha de versão","Waiting for a version choice");
            case "installing":return GeneralStyle.T("Instalando","Installing");
            case "retry":return GeneralStyle.T("Instalação adiada. Pode retomar.","Installation postponed. Can resume.");
            case "installed":return GeneralStyle.T("Instalado","Installed");
            case "discarded":return GeneralStyle.T("Recebimento descartado","Download discarded");
            default:return "";
        }
    }

    void CloseReceipt(P2PSession session,string recipient) {
        string folder=NoticeDirectory(session,recipient);
        if(!Directory.Exists(folder))return;
        Engine.RemoveFolder(ReceiptFolderId(session,recipient));
        P2PPack.DeleteSnapshot(folder);
    }

    void FinishIncoming(P2PSession session) {
        if(!session.ReceiptClosed)PublishNotice(session);
        if(Directory.Exists(Folder(session)))CleanupTransport(session);
        string directory=NoticeDirectory(session,Engine.Device);
        if(session.ReceiptClosed){
            if(Directory.Exists(directory))CloseReceipt(session,Engine.Device);
            return;
        }
        string id=ReceiptFolderId(session,Engine.Device);
        var remote=Engine.Get("db/completion?folder="+id+"&device="+session.Peer);
        string path=Safe.In(directory,"status.json");
        if(remote==null||!remote.ContainsKey("remoteState")||Convert.ToString(remote["remoteState"])!="valid"
            ||!File.Exists(path)||Convert.ToInt64(remote["globalBytes"])<new FileInfo(path).Length
            ||Convert.ToDouble(remote["completion"])<100||Convert.ToInt64(remote["needBytes"])!=0)return;
        session.ReceiptClosed=true;Save();
        CloseReceipt(session,Engine.Device);
    }

    void SetPhase(P2PSession session,string phase) {
        session.Phase=phase;Save();
        try{PublishNotice(session);}
        catch(System.Net.WebException){/* The next pass advertises the durable state. */}
        catch(IOException){/* The receipt may still be indexed by the transport. */}
        catch(UnauthorizedAccessException){/* The next pass retries publication. */}
    }

    void StartInstall(P2PSession session,string zip) {
        if(Interlocked.CompareExchange(ref installing,1,0)!=0)return;
        session.Stage="Conferindo e instalando…";
        Task.Run(()=>{
            try{
                Action checkpoint=()=>{if(disposed)throw new OperationCanceledException();};
                checkpoint();
                session.InstallProgress=null;
                SetPhase(session,"installing");
                var installer=new InstalledSnapshotInstaller(session.Destination,installedData,installedCatalog,installedFixture);
                session.ReceiptId=installer.Install(zip,session.Deferred?session.PayloadHash:session.Hash,message=>session.Stage=message,-1,difference=>{
                    SetPhase(session,"review");
                    var choice=ChooseVersion;
                    bool replace=choice!=null&&choice(difference);
                    checkpoint();
                    if(replace)SetPhase(session,"installing");
                    return replace;
                },id=>{session.TransactionId=id;Save();},checkpoint,value=>session.InstallProgress=value);
                session.Completed=true;
                session.Error=null;
                SetPhase(session,"installed");
                var handler=SnapshotInstalled;
                if(handler!=null)handler(session);
            }catch(OperationCanceledException){/* The transport and any committed installation journal are kept. */}
            catch(Exception ex){
                if(!disposed){
                    session.Error=ex.Message;
                    SetPhase(session,"retry");
                }
            }finally{session.Stage=null;Interlocked.Exchange(ref installing,0);}
        });
    }

    internal void PublishProject(P2PSession session,bool retry=false){
        lock(session){
            if(RegisterProject==null||!session.Completed||session.Sender||session.ListImported||session.ReceiptId==null||
                session.ProjectError!=null&&!retry)return;
            try{
                RegisterProject(session);
                session.ListImported=true;
                session.ProjectError=null;
            }catch(Exception ex){
                session.ProjectError=GeneralStyle.T("Mods instalados. Não foi possível salvar o projeto: ","Mods installed. Could not save the project: ")+ex.Message;
            }
            Save();
        }
    }

    internal void PreserveInterrupted(P2PSession session) {
        lock(session){
            session.Held=true;
            session.RecoveryPending=false;
            if(Engine.Running)Engine.Pause(session.Id,true);
            Save();
        }
    }

    internal void DiscardIncoming(P2PSession session) {
        lock(session){
            if(session.Sender||session.Completed||session.Stage!=null)
                throw new IOException("Selecione um recebimento interrompido.");
            PreserveInterrupted(session);
            string zip=Safe.In(Folder(session),"pack.zip");
            string expectedHash=session.Deferred?session.PayloadHash:session.Hash;
            if(session.TransactionId!=null||(!session.Lifecycle&&expectedHash!=null&&File.Exists(zip))){
                var installer=new InstalledSnapshotInstaller(session.Destination,installedData,installedCatalog,installedFixture);
                string installed=installer.DiscardInterrupted(session.TransactionId,zip,expectedHash);
                if(installed!=null){
                    session.ReceiptId=installed;
                    session.Completed=true;
                    session.Error=null;
                    SetPhase(session,"installed");
                    return;
                }
            }
            session.Discarded=true;
            session.AutoInstall=false;
            session.Error=null;
            SetPhase(session,"discarded");
            if(Engine.Running)Engine.RemoveFolder(session.Id);
            P2PPack.DeleteSnapshot(Folder(session));
        }
    }

    internal void RetryPreparation(P2PSession session) {
        lock(session){
            if(!session.Sender||session.Stage!=null||session.Completed)return;
            session.Error=null;
            Save();
        }
    }
}
