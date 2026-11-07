using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

internal sealed class P2PSyncProgress {
    internal int Done, Total;
    internal string Item;
}

internal sealed class P2PSyncComparison {
    internal P2PSession Session;
    internal string PreviewHash, Destination;
    internal string[] Required, Missing, Different, LocalOnly;
    internal int Matching, Unavailable;
}

internal sealed partial class P2PService {
    const int MaxSyncMessage = 12000000;
    const string PreviewName = "sync-preview.json", ChoiceName = "sync-choice.json";

    sealed class SyncMessage {
        public string Payload, Tag;
    }

    sealed class SyncChoice {
        public string PreviewHash;
        public bool Full;
        public string[] Required;
    }

    static string DeferredIgnores(P2PSession session) {
        return (session.CompareBeforePack ? "!sync-preview.json\n" : "")+"!ready.json\n!pack.zip\n*\n";
    }

    string PreviewPath(P2PSession session) { return Safe.In(Folder(session),PreviewName); }
    string ChoicePath(P2PSession session,string recipient) { return Safe.In(NoticeDirectory(session,recipient),ChoiceName); }
    internal bool HasSyncPreview(P2PSession session) { return session.CompareBeforePack&&File.Exists(PreviewPath(session)); }
    internal bool HasSyncChoice(P2PSession session) {
        if(!session.CompareBeforePack||Engine.Device==null)return false;
        var recipient=session.Sender?session.Recipients.FirstOrDefault():null;
        return session.Sender?recipient!=null&&File.Exists(ChoicePath(session,recipient.Device)):File.Exists(ChoicePath(session,Engine.Device));
    }

    void WriteSyncMessage(P2PSession session,string device,string path,string kind,object value) {
        string payload=Safe.Json.Serialize(value);
        var bytes=Encoding.UTF8.GetBytes(payload);
        var tag=ReadyTag(session.Hash,session.Id+"\n"+device+"\n"+kind,InstalledSnapshot.Hash(bytes),bytes.LongLength);
        var envelope=new SyncMessage{Payload=payload,Tag=tag};
        if(Encoding.UTF8.GetByteCount(Safe.Json.Serialize(envelope))>MaxSyncMessage)
            throw new IOException("A lista de comparação P2P é grande demais.");
        Safe.WriteJson(path,envelope);
    }

    string ReadSyncMessage(P2PSession session,string device,string path,string kind,out string hash) {
        byte[] raw;
        using(var input=File.OpenRead(path))raw=InstalledSnapshot.ReadBytes(input,MaxSyncMessage);
        var envelope=Safe.Json.Deserialize<SyncMessage>(Encoding.UTF8.GetString(raw));
        if(envelope==null||envelope.Payload==null||!P2PPack.HashValid(envelope.Tag))
            throw new IOException("A mensagem de comparação P2P é inválida.");
        var bytes=Encoding.UTF8.GetBytes(envelope.Payload);
        hash=InstalledSnapshot.Hash(bytes);
        string expected=ReadyTag(session.Hash,session.Id+"\n"+device+"\n"+kind,hash,bytes.LongLength);
        int mismatch=0;
        for(int i=0;i<expected.Length;i++)mismatch|=expected[i]^envelope.Tag[i];
        if(mismatch!=0)throw new IOException("A mensagem de comparação não pertence a este convite.");
        return envelope.Payload;
    }

    InstalledSnapshotManifest ReadSyncPreview(P2PSession session,string device,out string hash) {
        string json=ReadSyncMessage(session,device,PreviewPath(session),PreviewName,out hash);
        var preview=Safe.Json.Deserialize<InstalledSnapshotManifest>(json);
        InstalledSnapshot.Validate(preview,installedCatalog);
        if(preview.Project==null||preview.Files.Any(file=>file.Kind!="file"||file.ReuseLocal))
            throw new IOException("A lista de comparação do anfitrião é inválida.");
        return preview;
    }

    void ScanSyncFolder(string id) {
        try{lock(startGate)Engine.Request("POST","db/scan?folder="+id,null);}
        catch(WebException){/* The transport watcher retries when it reconnects. */}
    }

    bool TryPrepareSync(P2PSession session,GeneralModState state,out InstalledSnapshotManifest delta) {
        delta=null;
        if(session.Recipients.Length!=1)return false;
        string device=session.Recipients[0].Device;
        if(!HasSyncPreview(session)){
            session.Stage="Conferindo mods para comparação…";
            Task.Run(()=>{
                try{
                    var source=ExportState(state,session.ExportPreset,session.SelectedMods,session.ExportAllPresets);
                    var preview=InstalledSnapshot.Inspect(source,session.SelectedMods,installedCatalog,installedData,false,true,()=>{
                        if(disposed||session.Completed||session.Discarded)throw new OperationCanceledException();
                    },skipProblem:(id,name,error)=>ReviewSnapshotProblem(session,id,name,error),
                      inspectProgress:(done,total,item)=>session.SyncProgress=new P2PSyncProgress{Done=done,Total=total,Item=item});
                    if(disposed||session.Completed||session.Discarded)return;
                    WriteSyncMessage(session,device,PreviewPath(session),PreviewName,preview);
                    ScanSyncFolder(session.Id);
                }catch(Exception ex){
                    if(!disposed&&!session.Discarded){session.Error=ex.Message;Save();}
                }finally{session.SyncProgress=null;session.Stage=null;}
            });
            return false;
        }
        string choiceFile=ChoicePath(session,device);
        if(!File.Exists(choiceFile))return false;

        string previewHash, ignored;
        var previewManifest=ReadSyncPreview(session,device,out previewHash);
        var choice=Safe.Json.Deserialize<SyncChoice>(ReadSyncMessage(session,device,choiceFile,ChoiceName,out ignored));
        if(choice==null||choice.PreviewHash!=previewHash||choice.Required==null||choice.Required.Length>previewManifest.List.Mods.Count)
            throw new IOException("A escolha não corresponde à comparação deste convite.");
        var required=new HashSet<string>(choice.Required,StringComparer.Ordinal);
        if(required.Count!=choice.Required.Length||required.Any(id=>!previewManifest.List.Mods.Any(mod=>mod.Id==id))||choice.Full&&required.Count!=0)
            throw new IOException("A seleção de mods para sincronizar é inválida.");
        if(!choice.Full){
            foreach(var file in previewManifest.Files){
                file.ReuseLocal=file.Owners.All(owner=>!required.Contains(owner.Split('/')[0]));
            }
            delta=previewManifest;
        }
        return true;
    }

    internal P2PSyncComparison CompareIncoming(P2PSession session,GeneralModState local=null,Action<int,int,string> report=null,bool full=false) {
        if(session.Sender||session.Completed||session.Discarded||!session.CompareBeforePack||HasSyncChoice(session)||!HasSyncPreview(session))
            throw new IOException("A comparação ainda não está disponível.");
        string hash;
        var preview=ReadSyncPreview(session,Engine.Device,out hash);
        string destination=session.Destination;
        Safe.NoLinks(destination);
        if(!Directory.Exists(destination))throw new IOException("A pasta selecionada não está disponível.");
        bool cyberpunk=File.Exists(Safe.In(destination,"bin/x64/Cyberpunk2077.exe"));
        if(cyberpunk!=(preview.Game=="cyberpunk2077"))
            throw new IOException("O pacote e a pasta de instalação pertencem a tipos de jogo diferentes.");

        if(full)return new P2PSyncComparison{Session=session,PreviewHash=hash,Destination=destination,Required=new string[0]};

        var missing=new HashSet<string>(StringComparer.Ordinal);
        var different=new HashSet<string>(StringComparer.Ordinal);
        int done=0;
        foreach(var file in preview.Files){
            if(disposed||session.Discarded)throw new OperationCanceledException();
            if(report!=null)report(done,preview.Files.Count,file.Path);
            done++;
            string path=Safe.In(destination,file.Path);
            if(File.Exists(path)){
                bool matching;
                try{matching=new FileInfo(path).Length==file.Size&&Safe.Hash(path)==file.Sha256;}
                catch(IOException){matching=false;}
                catch(UnauthorizedAccessException){matching=false;}
                if(!matching)different.UnionWith(file.Owners);
            }else if(Directory.Exists(path))different.UnionWith(file.Owners);
            else missing.UnionWith(file.Owners);
        }
        if(report!=null)report(preview.Files.Count,preview.Files.Count,null);
        var absentNames=new List<string>();
        var differentNames=new List<string>();
        var required=new List<string>();
        foreach(var mod in preview.List.Mods){
            string owner=InstalledSnapshot.Owner(mod,mod.Variants.Single());
            if(different.Contains(owner))differentNames.Add(mod.Name);
            else if(missing.Contains(owner))absentNames.Add(mod.Name);
            else continue;
            required.Add(mod.Id);
        }
        var localOnly=new List<string>();
        if(local!=null&&String.Equals(local.Destination,destination,StringComparison.OrdinalIgnoreCase)){
            var hostIds=new HashSet<string>(preview.List.Mods.Select(mod=>mod.Id),StringComparer.Ordinal);
            foreach(var mod in local.List.Mods.Where(mod=>!hostIds.Contains(mod.Id))){
                if(disposed||session.Discarded)throw new OperationCanceledException();
                string chosen;
                if(!local.ChosenVariants.TryGetValue(mod.Id,out chosen))chosen=mod.DefaultVariant;
                var variant=mod.Variants.SingleOrDefault(item=>item.Id==chosen);
                if(variant!=null&&!InstalledSnapshot.Uninstalled(local,mod,variant,installedCatalog,destination,installedData))localOnly.Add(mod.Name);
            }
        }
        return new P2PSyncComparison{
            Session=session,PreviewHash=hash,Destination=destination,Required=required.ToArray(),Missing=absentNames.ToArray(),Different=differentNames.ToArray(),
            LocalOnly=localOnly.ToArray(),Matching=preview.List.Mods.Count-required.Count,Unavailable=preview.Project.Mods.Count-preview.List.Mods.Count
        };
    }

    bool AllowsLocalReuse(P2PSession session){
        if(!session.CompareBeforePack)return false;
        string ignored;
        var choice=Safe.Json.Deserialize<SyncChoice>(ReadSyncMessage(session,Engine.Device,ChoicePath(session,Engine.Device),ChoiceName,out ignored));
        return choice!=null&&!choice.Full;
    }

    internal async Task ChooseIncoming(P2PSession session,bool full) {
        lock(session){
            if(session.SyncMode!=0||session.Sender||session.Completed||session.Discarded||!session.CompareBeforePack||HasSyncChoice(session))return;
            session.SyncMode=full?2:1;
        }
        try{
            while(!HasSyncPreview(session)){
                if(disposed||session.Discarded)return;
                await Task.Delay(250).ConfigureAwait(false);
            }
            await Task.Run(()=>{
                if(disposed||session.Discarded)return;
                var comparison=CompareIncoming(session,report:(done,total,item)=>
                    session.SyncProgress=new P2PSyncProgress{Done=done,Total=total,Item=item},full:full);
                if(!disposed&&!session.Discarded)SubmitSyncChoice(comparison,full);
            }).ConfigureAwait(false);
        }catch(Exception ex){
            if(!disposed&&!session.Discarded){
                session.Error=ex.Message;
                Save();
            }
        }finally{session.SyncProgress=null;session.SyncMode=0;}
    }

    internal void SubmitSyncChoice(P2PSyncComparison comparison,bool full) {
        var session=comparison.Session;
        lock(session){
            if(!SnapshotSessions().Contains(session)||session.Sender||session.Completed||session.Discarded||HasSyncChoice(session))
                throw new IOException("Este recebimento já foi confirmado ou encerrado.");
            string hash;
            ReadSyncPreview(session,Engine.Device,out hash);
            if(hash!=comparison.PreviewHash||!String.Equals(session.Destination,comparison.Destination,StringComparison.OrdinalIgnoreCase))throw new IOException("A lista do anfitrião mudou. Confira os mods novamente.");
            var choice=new SyncChoice{PreviewHash=hash,Full=full,Required=full?new string[0]:comparison.Required};
            WriteSyncMessage(session,Engine.Device,ChoicePath(session,Engine.Device),ChoiceName,choice);
            ScanSyncFolder(ReceiptFolderId(session,Engine.Device));
        }
    }
}
