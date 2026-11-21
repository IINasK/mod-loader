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
    internal bool Packing;
}

internal sealed class P2PSyncComparison {
    internal P2PSession Session;
    internal string PreviewHash, Destination;
    internal bool Exact;
    internal string[] UserFolders=new string[0];
    internal P2PReceiveOptions ReceiveOptions;
    internal string[] Required, Missing, Different, LocalOnly;
    internal P2PSyncExtra[] Extras=new P2PSyncExtra[0];
    internal string[] HostOutsideDifferences=new string[0];
    internal HashSet<string> HostOutsideMissing=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    internal int Matching, Unavailable;
}

internal sealed class P2PSyncDifference {
    internal string InventoryGroupId, ProvenOwner, VerifiedZip;
    internal bool GeneratedOutput,PreexistingOutput;
    internal bool LocalInModFolder, RecordedFile, RecordedChange;
    internal long? VerifiedBytes;
    public string Kind, Mod, Path, ModId, VariantId;
}

internal sealed partial class P2PService {
    const int MaxSyncMessage = 12000000;
    const int ExactParityVersion = 2;
    const string PreviewName = "sync-preview.json", ChoiceName = "sync-choice.json";

    sealed class SyncMessage {
        public string Payload, Tag, ProgressTag;
        public int Done, Total;
        public bool Packing;
    }

    sealed class SyncChoice {
        public string PreviewHash;
        public bool Full,Exact;
        public int ParityVersion;
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

    void WriteSyncMessage(P2PSession session,string device,string path,string kind,object value,P2PSyncProgress progress=null) {
        string payload=Safe.Json.Serialize(value);
        var bytes=Encoding.UTF8.GetBytes(payload);
        var tag=ReadyTag(session.Hash,session.Id+"\n"+device+"\n"+kind,InstalledSnapshot.Hash(bytes),bytes.LongLength);
        var envelope=new SyncMessage{Payload=payload,Tag=tag};
        if(progress!=null){
            envelope.Done=progress.Done;
            envelope.Total=progress.Total;
            envelope.Packing=progress.Packing;
            envelope.ProgressTag=HostScanTag(session,device,tag,progress.Done,progress.Total,progress.Packing);
        }
        if(Encoding.UTF8.GetByteCount(Safe.Json.Serialize(envelope))>MaxSyncMessage)
            throw new IOException("A lista de comparação P2P é grande demais.");
        Safe.WriteJson(path,envelope);
    }

    string ReadSyncMessage(P2PSession session,string device,string path,string kind,out string hash,int maximum=MaxSyncMessage,Action<SyncMessage> readEnvelope=null) {
        byte[] raw;
        using(var input=File.OpenRead(path))raw=InstalledSnapshot.ReadBytes(input,maximum);
        var envelope=Safe.Json.Deserialize<SyncMessage>(Encoding.UTF8.GetString(raw));
        if(envelope==null||envelope.Payload==null||!P2PPack.HashValid(envelope.Tag))
            throw new IOException("A mensagem de comparação P2P é inválida.");
        var bytes=Encoding.UTF8.GetBytes(envelope.Payload);
        hash=InstalledSnapshot.Hash(bytes);
        string expected=ReadyTag(session.Hash,session.Id+"\n"+device+"\n"+kind,hash,bytes.LongLength);
        int mismatch=0;
        for(int i=0;i<expected.Length;i++)mismatch|=expected[i]^envelope.Tag[i];
        if(mismatch!=0)throw new IOException("A mensagem de comparação não pertence a este convite.");
        if(readEnvelope!=null)readEnvelope(envelope);
        return envelope.Payload;
    }

    InstalledSnapshotManifest ReadSyncPreview(P2PSession session,string device,out string hash) {
        string json=ReadSyncMessage(session,device,PreviewPath(session),PreviewName,out hash);
        var preview=Safe.Json.Deserialize<InstalledSnapshotManifest>(json);
        InstalledSnapshot.Validate(preview,installedCatalog);
        if(preview.Project==null||preview.Files.Any(file=>file.Kind!="file"||file.ReuseLocal))
            throw new IOException("A lista de comparação do anfitrião é inválida.");
        var inventory=preview.SyncGameInventory;
        if(preview.TemplateId==GeneralProjectStore.CyberpunkTemplate&&
            (inventory==null||!P2PPack.HashValid(preview.OutsideModFingerprint)||preview.OutsideFolderIds==null||preview.OutsideLuaSettings==null))
            throw new IOException(GeneralStyle.T("O anfitrião precisa atualizar o Mod Loader e gerar um novo convite para conferir o pacote pronto.",
                "The host must update Mod Loader and create a new invitation to verify the built-in pack."));
        if(preview.OutsideFolderIds!=null&&(preview.OutsideFolderIds.Length>InstalledSnapshot.MaxFiles||
            preview.OutsideFolderIds.Any(id=>!P2PPack.HashValid(id))||preview.OutsideFolderIds.Distinct().Count()!=preview.OutsideFolderIds.Length))
            throw new IOException("O inventário de pastas do anfitrião é inválido.");
        if(preview.OutsideLuaSettings!=null&&(preview.OutsideLuaSettings.Count>InstalledSnapshot.MaxFiles||
            preview.OutsideLuaSettings.Any(item=>!P2PPack.HashValid(item.Key)||!P2PPack.HashValid(item.Value))))
            throw new IOException("O inventário de configurações Lua do anfitrião é inválido.");
        if(inventory!=null&&(preview.Game!="cyberpunk2077"||inventory.Count>InstalledSnapshot.MaxFiles||
            inventory.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=inventory.Count||
            inventory.Any(item=>!FixedInstaller.SyncInventoryEntry(item.Key,item.Value))))
            throw new IOException("O inventário do anfitrião contém arquivos inválidos.");
        return preview;
    }

    void ScanSyncFolder(string id,string file=null) {
        string route="db/scan?folder="+id+(file==null?"":"&sub="+Uri.EscapeDataString(file));
        try{lock(startGate)Engine.Request("POST",route,null);}
        catch(WebException){/* The transport watcher retries when it reconnects. */}
    }

    bool TryPrepareSync(P2PSession session,GeneralModState state,out InstalledSnapshotManifest plan,out bool full) {
        plan=null;full=false;
        if(session.Recipients.Length!=1)return false;
        string device=session.Recipients[0].Device;
        if(!HasSyncPreview(session)){
            session.Stage="Conferindo mods para comparação…";
            Task.Run(()=>{
                var elapsed=System.Diagnostics.Stopwatch.StartNew();
                try{
                    var source=ExportState(state,session.ExportPreset,session.SelectedMods,session.ExportAllPresets);
                    var preview=InspectForTransfer(session,review=>InstalledSnapshot.Inspect(source,session.SelectedMods,
                        installedCatalog,installedData,false,true,()=>{
                            if(disposed||session.Completed||session.Discarded)throw new OperationCanceledException();
                        },skipProblem:review,
                        inspectProgress:(done,total,item)=>session.SyncProgress=new P2PSyncProgress{Done=done,Total=total,Item=item},
                        includeUpdatePreferences:true));
                    if(preview.Game=="cyberpunk2077"){
                        var engine=new FixedInstaller(installedCatalog,source.Destination,installedFixture);
                        Action checkpoint=()=>{
                            if(disposed||session.Discarded)throw new OperationCanceledException();
                        };
                        preview.SyncGameInventory=engine.ReadSyncInventory(preview.Files,checkpoint,installedData);
                        if(preview.TemplateId==GeneralProjectStore.CyberpunkTemplate){
                            preview.OutsideLuaSettings=new Dictionary<string,string>(StringComparer.Ordinal);
                            preview.OutsideModFingerprint=engine.OutsideModFingerprint(preview.Files,null,checkpoint,preview.OutsideLuaSettings);
                            preview.OutsideFolderIds=engine.OutsideGroupFolders(preview.Files,null,checkpoint).Keys.OrderBy(id=>id,StringComparer.Ordinal).ToArray();
                        }
                    }
                    if(disposed||session.Completed||session.Discarded)return;
                    WriteSyncMessage(session,device,PreviewPath(session),PreviewName,preview);
                    ScanSyncFolder(session.Id,PreviewName);
                    OnlineDiagnostics.Record("p2p.preview.ready",null,"Comparação do anfitrião disponível.",
                        new{Id=DiagnosticId(session.Id),Files=preview.Files.Count,Milliseconds=elapsed.ElapsedMilliseconds});
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
        if(previewManifest.TemplateId==GeneralProjectStore.CyberpunkTemplate&&(!choice.Exact||choice.ParityVersion!=ExactParityVersion))
            throw new IOException(GeneralStyle.T("O receptor precisa atualizar o Mod Loader para sincronizar todas as versões do pacote pronto.",
                "The recipient must update Mod Loader to sync every version in the built-in pack."));
        var required=new HashSet<string>(choice.Required,StringComparer.Ordinal);
        if(required.Count!=choice.Required.Length||required.Any(id=>!previewManifest.List.Mods.Any(mod=>mod.Id==id))||choice.Full&&required.Count!=0)
            throw new IOException("A seleção de mods para sincronizar é inválida.");
        if(!choice.Full){
            foreach(var file in previewManifest.Files){
                file.ReuseLocal=file.Owners.All(owner=>!required.Contains(owner.Split('/')[0]));
            }
        }
        previewManifest.SyncGameInventory=null;
        plan=previewManifest;
        full=choice.Full;
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

        var comparison=new P2PSyncComparison{Session=session,PreviewHash=hash,Destination=destination,
            Exact=preview.TemplateId==GeneralProjectStore.CyberpunkTemplate,Required=new string[0]};
        var userFolders=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var mod in preview.List.Mods){
            try{userFolders.UnionWith(GeneralInstallRoutes.ExternalFolders(destination,mod.Variants.Single().InstallRoutes));}
            catch(DirectoryNotFoundException){if(comparison.Exact)throw;}
        }
        comparison.UserFolders=userFolders.OrderBy(path=>path,StringComparer.OrdinalIgnoreCase).ToArray();
        if(full&&!comparison.Exact)return comparison;

        var missing=new HashSet<string>(StringComparer.Ordinal);
        var different=new HashSet<string>(StringComparer.Ordinal);
        int done=0;object progressGate=new object();
        if(report!=null&&preview.Files.Count>0)report(0,preview.Files.Count,null);
        var fileStates=SnapshotVerification.Map(preview.Files,(file,poll)=>{
            int status=0;
            try{
            string path;
            try{path=GeneralInstallRoutes.Resolve(destination,file.Path);}
            catch(DirectoryNotFoundException){return 1;}
            if(File.Exists(path)){
                bool matching;
                try{matching=SnapshotVerification.Matches(path,file.Size,file.Sha256,poll);}
                catch(IOException){matching=false;}
                catch(UnauthorizedAccessException){matching=false;}
                if(!matching)status=2;
            }else if(Directory.Exists(path))status=2;
            else status=1;
            return status;
            }finally{
                lock(progressGate){done++;if(report!=null&&done<preview.Files.Count)report(done,preview.Files.Count,file.Path);}
            }
        },()=>{
            if(disposed||session.Discarded)throw new OperationCanceledException();
        });
        for(int index=0;index<preview.Files.Count;index++){
            if(fileStates[index]==1)missing.UnionWith(preview.Files[index].Owners);
            else if(fileStates[index]==2)different.UnionWith(preview.Files[index].Owners);
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
        var extras=new P2PSyncExtra[0];
        var outside=new List<string>();var outsideMissing=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(cyberpunk&&preview.SyncGameInventory!=null){
            Action checkpoint=()=>{if(disposed||session.Discarded)throw new OperationCanceledException();};
            if(report!=null)report(0,0,"Mods fora do pacote");
            extras=new FixedInstaller(installedCatalog,destination,installedFixture).PlanSyncExtras(preview,checkpoint,installedData);
            if(comparison.Exact){
                var hostFolders=new HashSet<string>(preview.OutsideFolderIds,StringComparer.Ordinal);
                bool ignoresEmpty=NightCoopRelease.IgnoresEmptyGroups(preview.Files);
                extras=extras.Where(extra=>!extra.Files.All(file=>file.Before==null&&
                    (ignoresEmpty||hostFolders.Contains(FixedInstaller.SyncFolderId(file.Path))))).ToArray();
            }
            foreach(var item in preview.SyncGameInventory){
                checkpoint();
                string path=Safe.In(destination,item.Key);
                bool isFile=File.Exists(path), isDirectory=Directory.Exists(path);
                if(item.Value==null?!isDirectory:!isFile||Safe.Hash(path)!=item.Value){
                    outside.Add(item.Key);
                    if(!isFile&&!isDirectory)outsideMissing.Add(item.Key);
                }
            }
        }
        comparison.Required=required.ToArray();
        comparison.Missing=absentNames.ToArray();
        comparison.Different=differentNames.ToArray();
        comparison.Extras=extras;
        comparison.HostOutsideDifferences=outside.ToArray();
        comparison.HostOutsideMissing=outsideMissing;
        comparison.LocalOnly=localOnly.ToArray();
        comparison.Matching=preview.List.Mods.Count-required.Count;
        comparison.Unavailable=InstalledSnapshot.CountOutsidePack(preview);
        return comparison;
    }

    void VerifyOutsideFiles(P2PSession session,InstalledSnapshotManifest preview,List<OwnedChange> removal,Action checkpoint){
        var pending=new HashSet<string>((removal??new List<OwnedChange>()).Select(item=>item.Path),StringComparer.OrdinalIgnoreCase);
        foreach(var item in preview.SyncGameInventory){
            checkpoint();
            string path=Safe.In(session.Destination,item.Key);
            Safe.NoLinks(path);
            if(item.Value==null?Directory.Exists(path):File.Exists(path)&&SnapshotVerification.Hash(path,checkpoint)==item.Value)continue;
            throw new IOException(GeneralStyle.T("O anfitrião tem um arquivo diferente fora do pacote. Inclua o mod no projeto e gere outro convite: ",
                "The host has a different file outside the pack. Include its mod in the project and create a new invitation: ")+item.Key);
        }
        var engine=new FixedInstaller(installedCatalog,session.Destination,installedFixture);
        var local=engine.OutsideGroupFolders(preview.Files,pending,checkpoint);
        var remote=new HashSet<string>(preview.OutsideFolderIds,StringComparer.Ordinal);
        if(!remote.SetEquals(local.Keys)){
            var names=local.Where(item=>!remote.Contains(item.Key)).Select(item=>item.Value).ToArray();
            int missing=remote.Count(id=>!local.ContainsKey(id));
            var lines=new List<string>{GeneralStyle.T("O NightCoop também conta pastas de mods vazias.","NightCoop also counts empty mod folders.")};
            if(names.Length>0)lines.Add(GeneralStyle.T("Só neste PC: ","Only on this PC: ")+String.Join(", ",names.Take(8))+(names.Length>8?" …":""));
            if(missing>0)lines.Add(GeneralStyle.T("Pastas só no anfitrião: ","Folders only on the host: ")+missing);
            lines.Add(GeneralStyle.T("Confira as pastas em mods e r6/tweaks nos dois PCs antes de tentar novamente.",
                "Compare the mods and r6/tweaks folders on both PCs before retrying."));
            throw new IOException(String.Join(Environment.NewLine,lines));
        }
        var settings=new Dictionary<string,string>(StringComparer.Ordinal);
        if(engine.OutsideModFingerprint(preview.Files,pending,checkpoint,settings)!=preview.OutsideModFingerprint){
            int missing=preview.OutsideLuaSettings.Keys.Count(id=>!settings.ContainsKey(id));
            int different=settings.Count(item=>preview.OutsideLuaSettings.ContainsKey(item.Key)&&preview.OutsideLuaSettings[item.Key]!=item.Value);
            int extra=settings.Keys.Count(id=>!preview.OutsideLuaSettings.ContainsKey(id));
            if(missing+different+extra>0)
                throw new IOException(String.Format(GeneralStyle.T("Configurações Lua fora do pacote: {0} ausentes neste PC, {1} diferentes, {2} extras locais. Confira os arquivos settings.lua nas pastas dos mods nos dois PCs. Depois de igualar as configurações, refaça a comparação.",
                    "Lua settings outside the pack: {0} missing on this PC, {1} different, {2} local extras. Check the settings.lua files in the mod folders on both PCs. Once the settings match, compare again."),missing,different,extra));
            throw new IOException(GeneralStyle.T("Os arquivos de mods fora do pacote ainda diferem. Confira os mods extras nos dois PCs antes de sincronizar.",
                "Mod files outside the pack still differ. Review extra mods on both PCs before syncing."));
        }
    }

    internal void VerifyHostOutsideFiles(P2PSession session,GeneralModState state,InstalledSnapshotManifest plan,Action checkpoint){
        if(plan.TemplateId!=GeneralProjectStore.CyberpunkTemplate)return;
        string ignored;
        var preview=ReadSyncPreview(session,session.Recipients.Single().Device,out ignored);
        var engine=new FixedInstaller(installedCatalog,state.Destination,installedFixture);
        if(!new HashSet<string>(preview.OutsideFolderIds,StringComparer.Ordinal).SetEquals(engine.OutsideGroupFolders(preview.Files,null,checkpoint).Keys)||
            engine.OutsideModFingerprint(preview.Files,null,checkpoint)!=preview.OutsideModFingerprint)
            throw new IOException(GeneralStyle.T("Os mods do anfitrião mudaram após a conferência. Gere outro convite.",
                "The host's mods changed after comparison. Create a new invitation."));
        plan.OutsideModFingerprint=null;
        plan.OutsideFolderIds=null;
        plan.OutsideLuaSettings=null;
    }

    internal InstalledSnapshotManifest ReadExactPreview(P2PSession session){
        if(!session.CompareBeforePack||session.ReceiveOptions==null||!session.ReceiveOptions.Exact)
            throw new IOException(GeneralStyle.T("Este pacote precisa de um novo convite com conferência de paridade.","This pack needs a new invitation with parity verification."));
        string hash,ignored;
        var preview=ReadSyncPreview(session,Engine.Device,out hash);
        var choice=Safe.Json.Deserialize<SyncChoice>(ReadSyncMessage(session,Engine.Device,ChoicePath(session,Engine.Device),ChoiceName,out ignored));
        if(preview.TemplateId!=GeneralProjectStore.CyberpunkTemplate||choice==null||!choice.Exact||choice.ParityVersion!=ExactParityVersion||choice.PreviewHash!=hash)
            throw new IOException(GeneralStyle.T("A seleção inicial deste convite mudou.","The initial selection for this invitation has changed."));
        return preview;
    }

    internal static void VerifyExactContents(InstalledSnapshotManifest preview,InstalledSnapshotManifest installed){
        if(preview.TemplateId!=installed.TemplateId||Safe.Json.Serialize(preview.List)!=Safe.Json.Serialize(installed.List)||
            !(preview.ManualUpdateMods??new List<string>()).SequenceEqual(installed.ManualUpdateMods??new List<string>()))
            throw new IOException(GeneralStyle.T("Os mods recebidos não correspondem à seleção do anfitrião.","Received mods do not match the host's selection."));
        var expected=preview.Files.OrderBy(file=>file.Path,StringComparer.OrdinalIgnoreCase).ToArray();
        var actual=installed.Files.Where(file=>file.Kind=="file").OrderBy(file=>file.Path,StringComparer.OrdinalIgnoreCase).ToArray();
        if(expected.Length!=actual.Length||!expected.Zip(actual,(a,b)=>a.Path==b.Path&&a.Sha256==b.Sha256&&a.Size==b.Size&&a.Owners.SequenceEqual(b.Owners)).All(match=>match))
            throw new IOException(GeneralStyle.T("A lista dos arquivos recebidos diverge da conferência inicial.","The received file list differs from the initial comparison."));
    }

    internal void VerifyExactSync(P2PSession session,InstalledSnapshotManifest installed,Action checkpoint){
        if(installed.TemplateId!=GeneralProjectStore.CyberpunkTemplate&&(session.ReceiveOptions==null||!session.ReceiveOptions.Exact))return;
        var preview=ReadExactPreview(session);
        VerifyExactContents(preview,installed);
        VerifyOutsideFiles(session,preview,null,checkpoint);
        var matches=SnapshotVerification.Map(preview.Files,(file,poll)=>SnapshotVerification.Matches(
            GeneralInstallRoutes.Resolve(session.Destination,file.Path),file.Size,file.Sha256,poll),checkpoint);
        int changed=Array.IndexOf(matches,false);
        if(changed>=0)throw new IOException(GeneralStyle.T("Arquivo diferente na conferência final: ","A file differs at final verification: ")+preview.Files[changed].Path);
    }

    void ApplyApprovedSyncCleanup(P2PSession session,InstalledSnapshotManifest installed) {
        if(session.SyncCleanup==null||session.SyncCleanup.Count==0)return;
        string hash,ignored;
        var preview=ReadSyncPreview(session,Engine.Device,out hash);
        var choice=Safe.Json.Deserialize<SyncChoice>(ReadSyncMessage(session,Engine.Device,ChoicePath(session,Engine.Device),ChoiceName,out ignored));
        if(choice==null||(choice.Full&&!choice.Exact)||choice.PreviewHash!=hash||installed.Game!="cyberpunk2077")
            throw new IOException("Refaça a comparação antes de remover mods extras.");
        new FixedInstaller(installedCatalog,session.Destination,installedFixture)
            .ApplySyncCleanup(session.SyncCleanup,preview,installed.Files.Select(file=>file.Path),message=>session.Stage=message,installedData);
        OnlineDiagnostics.Record("p2p.sync.cleanup.complete",null,"Extras confirmados guardados no backup local.",new{Files=session.SyncCleanup.Count});
        session.SyncCleanup=null;
    }

    bool AllowsLocalReuse(P2PSession session){
        if(!session.CompareBeforePack)return false;
        string ignored;
        var choice=Safe.Json.Deserialize<SyncChoice>(ReadSyncMessage(session,Engine.Device,ChoicePath(session,Engine.Device),ChoiceName,out ignored));
        return choice!=null&&!choice.Full;
    }

    internal async Task ChooseIncoming(P2PSession session,bool full,Func<P2PSyncComparison,int[]> reviewExtras=null) {
        lock(session){
            if(session.SyncMode!=0||session.Sender||session.Completed||session.Discarded||!session.CompareBeforePack||HasSyncChoice(session))return;
            session.SyncMode=full?2:1;
        }
        var elapsed=System.Diagnostics.Stopwatch.StartNew();
        try{
            while(!HasSyncPreview(session)){
                if(disposed||session.Discarded)return;
                await Task.Delay(250).ConfigureAwait(false);
            }
            await Task.Run(()=>{
                if(disposed||session.Discarded)return;
                var comparison=CompareIncoming(session,report:(done,total,item)=>
                    session.SyncProgress=new P2PSyncProgress{Done=done,Total=total,Item=item},full:full);
                int[] selected=new int[0];
                if(reviewExtras!=null){
                    selected=reviewExtras(comparison);
                    if(selected==null)return;
                }
                if(!disposed&&!session.Discarded){
                    SubmitSyncChoice(comparison,full,selected);
                    OnlineDiagnostics.Record("p2p.choice.duration",null,"Escolha do recebimento enviada.",
                        new{Id=DiagnosticId(session.Id),Milliseconds=elapsed.ElapsedMilliseconds});
                }
            }).ConfigureAwait(false);
        }catch(Exception ex){
            if(!disposed&&!session.Discarded){
                session.Error=ex.Message;
                ReportDiagnosticFailure(session,"choice_failed",ex);
                OnlineDiagnostics.Failure("p2p.choice.failed",ex,"Comparar os mods e enviar a escolha ao anfitrião.",
                    stage:full?"full":"update");
                Save();
            }
        }finally{session.SyncProgress=null;session.SyncMode=0;}
    }

    static IEnumerable<P2PSyncDifference> HostDifferences(P2PSyncComparison comparison) {
        return comparison.HostOutsideDifferences.Select(path => new P2PSyncDifference {
            Kind = comparison.HostOutsideMissing.Contains(path) ? "missing" : "different", Path = path
        });
    }

    string SyncDifferencesPath(P2PSession session) {
        return Safe.In(PrivateFolder(session), session.Id + "-sync-differences.json");
    }

    internal P2PSyncDifference[] ReadSyncDifferences(P2PSession session) {
        string path = SyncDifferencesPath(session);
        if (!File.Exists(path)) return null;
        return Safe.Json.Deserialize<P2PSyncDifference[]>(GeneralProjectStorage.ReadText(path, MaxSyncMessage));
    }

    void ForgetSyncDifferences(P2PSession session) {
        if (session.Sender || !session.CompareBeforePack) return;
        try {
            string path = SyncDifferencesPath(session);
            Safe.NoLinks(path);
            File.Delete(path);
        } catch (IOException) { }
          catch (UnauthorizedAccessException) { }
    }

    internal void SubmitSyncChoice(P2PSyncComparison comparison,bool full,int[] selected=null) {
        var session=comparison.Session;
        selected=selected??new int[0];
        if(selected.Distinct().Count()!=selected.Length||selected.Any(index=>index<0||index>=comparison.Extras.Length||!comparison.Extras[index].Proven))
            throw new IOException("A seleção de mods extras é inválida.");
        var cleanup=selected.SelectMany(index=>comparison.Extras[index].Files).ToList();
        string hash;
        var preview=ReadSyncPreview(session,Engine.Device,out hash);
        bool exact=preview.TemplateId==GeneralProjectStore.CyberpunkTemplate;
        if(full&&!exact&&selected.Length!=0)throw new IOException("A seleção de mods extras é inválida.");
        if(exact){
            if(comparison.ReceiveOptions==null||!comparison.ReceiveOptions.Exact||
                !comparison.ReceiveOptions.AllowsFolders(comparison.UserFolders))
                throw new IOException(GeneralStyle.T("Confirme as opções do pacote e os destinos antes de sincronizar.",
                    "Confirm the pack options and destinations before syncing."));
            VerifyOutsideFiles(session,preview,cleanup,()=>{if(disposed||session.Discarded)throw new OperationCanceledException();});
        }
        lock(session){
            if(!SnapshotSessions().Contains(session)||session.Sender||session.Completed||session.Discarded||HasSyncChoice(session))
                throw new IOException("Este recebimento já foi confirmado ou encerrado.");
            if(hash!=comparison.PreviewHash||!String.Equals(session.Destination,comparison.Destination,StringComparison.OrdinalIgnoreCase))throw new IOException("A lista do anfitrião mudou. Confira os mods novamente.");
            session.ReceiveOptions=comparison.ReceiveOptions??session.ReceiveOptions;
            session.SyncCleanup=cleanup.Count==0?null:cleanup;
            OnlineDiagnostics.Record("p2p.sync.cleanup.approved",null,"Remoção de extras confirmada.",new{Mods=selected.Length,Files=cleanup.Count});
            var approved = new HashSet<int>(selected);
            var kept = comparison.Extras.Where((extra, index) => !approved.Contains(index)).ToArray();
            var differences = kept.SelectMany(extra => extra.Files.Select(file => new P2PSyncDifference {
                Kind = "kept", Mod = extra.Name, Path = file.Path
            })).Concat(HostDifferences(comparison)).ToArray();
            if (differences.Length > 0)
                Safe.WriteJson(SyncDifferencesPath(session), differences);
            else ForgetSyncDifferences(session);
            session.SyncLocalExtrasKept = kept.Length;
            session.SyncHostFilesDifferent = comparison.HostOutsideDifferences.Length;
            Save();
            var choice=new SyncChoice{PreviewHash=hash,Full=full,Exact=exact,ParityVersion=exact?ExactParityVersion:0,Required=full?new string[0]:comparison.Required};
            WriteSyncMessage(session,Engine.Device,ChoicePath(session,Engine.Device),ChoiceName,choice);
            OnlineDiagnostics.Record("p2p.choice.submitted",null,"Seleção de recebimento confirmada.",
                new{Id=DiagnosticId(session.Id),Mode=full?"full":"update",Count=choice.Required.Length});
            session.NextChoiceScanUtc=DateTime.UtcNow.AddSeconds(5);
            ScanSyncFolder(ReceiptFolderId(session,Engine.Device),ChoiceName);
        }
    }
}

public sealed partial class FixedInstaller {
    internal static string SyncFolderId(string relative){return InstalledSnapshot.Hash(Encoding.UTF8.GetBytes(relative.ToUpperInvariant()));}

    // NightCoop inventories these top-level groups even with zero payload files.
    // Missing folders covered by the incoming files will be created by installation.
    internal Dictionary<string,string> OutsideGroupFolders(IEnumerable<InstalledSnapshotFile> files,IEnumerable<string> removal,Action checkpoint){
        var groups=new Dictionary<string,string>(StringComparer.Ordinal);
        if(NightCoopRelease.IgnoresEmptyGroups(files))return groups;
        var covered=new HashSet<string>(files.Select(file=>ModFolder(file.Path)).Where(folder=>folder!=null),StringComparer.OrdinalIgnoreCase);
        var removing=new HashSet<string>(removal??Enumerable.Empty<string>(),StringComparer.OrdinalIgnoreCase);
        var pruned=new HashSet<string>(removing.SelectMany(SyncParentFolders),StringComparer.OrdinalIgnoreCase);
        foreach(string root in new[]{RedmodRoot,"r6/tweaks"}){
            string directory=Safe.In(game,root);
            Safe.NoLinks(directory);
            if(!Directory.Exists(directory))continue;
            foreach(string child in Directory.EnumerateDirectories(directory)){
                checkpoint();
                Safe.NoLinks(child);
                string relative=RelativeGamePath(child);
                if(covered.Contains(relative)||pruned.Contains(relative)&&GroupWillBeRemoved(child,removing,pruned,checkpoint))continue;
                if(groups.Count>=InstalledSnapshot.MaxFiles)throw new IOException("Há pastas de mods demais para comparar.");
                groups.Add(SyncFolderId(relative),relative);
            }
        }
        return groups;
    }

    bool GroupWillBeRemoved(string directory,HashSet<string> removal,HashSet<string> pruned,Action checkpoint){
        var pending=new Stack<string>();pending.Push(directory);
        while(pending.Count>0){
            foreach(string child in Directory.EnumerateFileSystemEntries(pending.Pop())){
                checkpoint();
                Safe.NoLinks(child);
                string relative=RelativeGamePath(child);
                if(Directory.Exists(child)){
                    if(!pruned.Contains(relative))return false;
                    pending.Push(child);
                }else if(!removal.Contains(relative))return false;
            }
        }
        return true;
    }

    // compat-b1 enumerates mod formats in the game roots. Owned files of any type
    // are already covered by the package manifest and receipt checks.
    static readonly HashSet<string> NightCoopPayloadExtensions=new HashSet<string>(new[]{
        ".dll",".reds",".archive",".xl",".yaml",".yml",".tweak",".asi",".lua",".ws"
    },StringComparer.OrdinalIgnoreCase);
    static bool NightCoopPayload(string path){
        string extension=Path.GetExtension(path);
        if(InFolder(path,ExtraFileRoots[0])||InFolder(path,ExtraFileRoots[1])||InFolder(path,ExtraFileRoots[2]))
            return ExtraFilePath(path);
        if(InFolder(path,ExtraBundleRoots[1]))return String.Equals(extension,".lua",StringComparison.OrdinalIgnoreCase);
        return NightCoopPayloadExtensions.Contains(extension)||(InFolder(path,RedmodRoot)&&
            Path.GetFileName(path).Equals("info.json",StringComparison.OrdinalIgnoreCase));
    }

    internal string OutsideModFingerprint(IEnumerable<InstalledSnapshotFile> files,IEnumerable<string> removal,Action checkpoint,
        IDictionary<string,string> luaSettings=null){
        var exclude=new HashSet<string>(files.Select(file=>file.Path),StringComparer.OrdinalIgnoreCase);
        if(removal!=null)exclude.UnionWith(removal);
        string plugins=Safe.In(game,"bin/x64/plugins");Safe.NoLinks(plugins);
        var asi=Directory.Exists(plugins)?Directory.EnumerateFiles(plugins,"*.asi",SearchOption.TopDirectoryOnly)
            .Select(RelativeGamePath):Enumerable.Empty<string>();
        var paths=FindExtraPaths(true,false).Concat(asi).Where(path=>!exclude.Contains(path)&&!InstalledFileOwnership.PrivateState(path)&&NightCoopPayload(path))
            .OrderBy(path=>path,StringComparer.OrdinalIgnoreCase).ToArray();
        if(paths.Length>InstalledSnapshot.MaxFiles)throw new IOException("Há arquivos de mods demais fora do pacote.");
        var hashes=SnapshotVerification.Map(paths,(path,poll)=>SnapshotVerification.Hash(Safe.In(game,path),poll),checkpoint);
        if(luaSettings!=null)for(int index=0;index<paths.Length;index++)
            if(paths[index].EndsWith("/settings.lua",StringComparison.OrdinalIgnoreCase)||
                paths[index].EndsWith("_settings.lua",StringComparison.OrdinalIgnoreCase))
                luaSettings.Add(SyncFolderId(paths[index]),hashes[index]);
        var entries=paths.Select((path,index)=>path.ToUpperInvariant()+"\n"+hashes[index]);
        return InstalledSnapshot.Hash(Encoding.UTF8.GetBytes(String.Join("\n",entries)));
    }
}
