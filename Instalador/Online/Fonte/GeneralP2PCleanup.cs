using System;
using System.IO;
using System.Linq;
using System.Threading;

internal sealed partial class P2PService {
    Timer cleanupRetry;
    int cleaning;
    // Completion is durable before removing bytes. The small signed status channel
    // remains available independently, so an offline friend does not retain a ZIP.
    internal void CleanupCompletedTransfers() {
        foreach(var session in SnapshotSessions())TryCleanupCompletedTransfer(session);
    }

    void TryCleanupCompletedTransfer(P2PSession session) {
        lock(session)lock(startGate) {
            if(disposed||session.Mode!="installed-zip"||!session.Completed||session.Stage!=null||
                session.NextCleanupUtc>DateTime.UtcNow)return;
            if(session.Sender&&session.Lifecycle&&
                (session.Recipients.Length==0||session.Recipients.Any(peer=>!Terminal(peer.Phase))))return;
            if(!session.Sender&&(installedCatalog==null||installedData==null||session.ReceiptId==null))return;
            bool mayRelease=session.Lifecycle||session.Sender||session.TransportReleased;
            bool transport=mayRelease&&Directory.Exists(Folder(session));
            bool installation=!session.Sender&&!session.InstallArtifactsCleaned;
            if(!transport&&!installation&&!session.CleanupPending)return;
            FileStream offlineLease=null;
            try {
                if(!Engine.Running) {
                    string home=Safe.In(Root,"syncthing");Safe.NoLinks(home);Directory.CreateDirectory(home);
                    // The same lease is held by the helper owner for its entire life.
                    // Never remove a shared folder while another app instance owns it.
                    string lease=Safe.In(home,"engine.lock");Safe.NoLinks(lease);
                    try{offlineLease=new FileStream(lease,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
                    catch(IOException){return;}
                }
                InstalledSnapshotInstaller installer=null;
                if(installation) {
                    installer=new InstalledSnapshotInstaller(session.Destination,installedData,installedCatalog,installedFixture);
                    installer.ReadManifest(session.ReceiptId); // authenticated, committed installation proof
                }
                if(transport) {
                    ValidateCleanupTarget(session);
                    if(Engine.Running)CleanupTransport(session,false);
                    else P2PPack.DeleteSnapshot(Folder(session),0);
                    session.TransportReleased=true;
                }
                if(installation) {
                    if(!installer.CleanupCompletedArtifacts(session.ReceiptId))
                        throw new IOException("A instalação ainda não tem um recibo concluído para liberar seus temporários.");
                    session.InstallArtifactsCleaned=true;
                }
                bool changed=session.CleanupPending||session.CleanupError!=null||installation||transport;
                session.CleanupPending=false;session.CleanupError=null;
                if(changed)Save();
            } catch(Exception ex) {
                if(!(ex is IOException)&&!(ex is UnauthorizedAccessException)&&!(ex is System.Net.WebException))throw;
                string kind=ex.GetType().Name;
                bool changed=!session.CleanupPending||session.CleanupError!=kind;
                session.CleanupPending=true;session.CleanupError=kind;
                session.NextCleanupUtc=DateTime.UtcNow.AddSeconds(5);
                ScheduleCleanupRetry();
                if(changed) {
                    OnlineDiagnostics.Record("p2p.cleanup.pending",null,"Limpeza local pendente; uma nova tentativa será feita.",
                        new{Id=DiagnosticId(session.Id),ErrorType=kind});
                    try{Save();}catch(IOException){}catch(UnauthorizedAccessException){}
                }
            } finally {if(offlineLease!=null)offlineLease.Dispose();}
        }
    }
    void ValidateCleanupTarget(P2PSession session) {
        string folder=Path.GetFullPath(Folder(session)).TrimEnd('\\');
        Func<string,string,bool> contains=(parent,path)=>String.Equals(parent,path,StringComparison.OrdinalIgnoreCase)||
            path.StartsWith(parent+"\\",StringComparison.OrdinalIgnoreCase);
        if(contains(folder,Root.TrimEnd('\\'))||!String.IsNullOrEmpty(session.Destination)&&
            (contains(folder,Path.GetFullPath(session.Destination).TrimEnd('\\'))||contains(Path.GetFullPath(session.Destination).TrimEnd('\\'),folder)))
            throw new IOException("A pasta temporária se sobrepõe aos dados privados ou à instalação. A limpeza foi preservada para revisão.");
        Safe.NoLinks(folder);
    }
    void ScheduleCleanupRetry() {
        if(cleanupRetry!=null||disposed)return;
        cleanupRetry=new Timer(_=>{
            if(disposed||Interlocked.Exchange(ref cleaning,1)!=0)return;
            try {
                CleanupCompletedTransfers();
                lock(startGate)if(!SnapshotSessions().Any(session=>session.CleanupPending)) {
                    var timer=Interlocked.Exchange(ref cleanupRetry,null);if(timer!=null)timer.Dispose();
                }
            } catch(Exception ex) {
                if(ex is OutOfMemoryException)throw;
                OnlineDiagnostics.Record("p2p.cleanup.retry.failed",null,"A limpeza será tentada novamente.",new{ErrorType=ex.GetType().Name});
            } finally {Interlocked.Exchange(ref cleaning,0);}
        },null,5000,5000);
        if(disposed){var timer=Interlocked.Exchange(ref cleanupRetry,null);if(timer!=null)timer.Dispose();}
    }
}
