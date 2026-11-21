using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

internal sealed class P2PVerificationFailure {
    internal readonly string Id, Name, Message;
    internal P2PVerificationFailure(string id,string name,string message){Id=id;Name=name;Message=message;}
}

internal sealed class P2PVerificationIssue {
    internal readonly P2PVerificationFailure[] Failures;
    internal readonly int Included;
    internal readonly TaskCompletionSource<bool> Decision=new TaskCompletionSource<bool>();
    internal P2PVerificationIssue(P2PVerificationFailure[] failures,int included){Failures=failures;Included=included;}
}

internal sealed partial class P2PService {
    // Deferred links authorize a connection first; HMAC binds its finished ZIP later.
    internal sealed class ReadyPack {
        public int Schema=1;
        public string Hash,Tag;
        public long Size;
    }
    internal P2PSession BeginDeferred(GeneralModState state,System.Collections.Generic.IEnumerable<string> ids,bool lifecycle=false,string exportPreset=null,bool exportAllPresets=false,bool compareBeforePack=false){
        if(compareBeforePack&&!lifecycle)throw new ArgumentException("A comparação requer o protocolo atual.");
        lock(startGate){
        if(!Engine.Running||Sessions.Count>=100)throw new IOException("Inicie a conexão ou remova compartilhamentos antigos.");
        string[] chosen=ids.Distinct(StringComparer.Ordinal).ToArray();
        if(chosen.Length==0||chosen.Any(id=>!state.List.Mods.Any(m=>m.Id==id)))throw new IOException("Escolha um preset com mods para compartilhar.");
        byte[] key=new byte[32];using(var random=RandomNumberGenerator.Create())random.GetBytes(key);
        var preset=exportAllPresets?null:state.List.Presets.FirstOrDefault(p=>p.Id==(exportPreset??state.SelectedPreset));
        var session=new P2PSession{Id="ml-"+Guid.NewGuid().ToString("N"),Sender=true,Mode="installed-zip",Deferred=true,Automatic=true,Lifecycle=lifecycle,CompareBeforePack=compareBeforePack,Destination=state.Destination,Hash=Safe.Hex(key),Name=preset==null?"Pacote instalado":preset.Name,SelectedMods=chosen,ExportPreset=exportAllPresets?state.SelectedPreset:exportPreset,ExportAllPresets=exportAllPresets};
        if(compareBeforePack){
            session.OfferSource=Safe.Json.Deserialize<GeneralModState>(Safe.Json.Serialize(state));
            session.OfferSource.TemplateId=state.TemplateId;session.OfferSource.ProjectName=state.ProjectName;
        }
        string folder=Folder(session);
        if(!String.IsNullOrEmpty(state.Destination)&&Path.GetFullPath(folder).StartsWith(Path.GetFullPath(state.Destination).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("A transferência deve ficar fora da pasta do jogo.");
        if(Directory.Exists(folder)||File.Exists(folder))throw new IOException("Destino da transferência já existe.");
        Directory.CreateDirectory(folder);
        try{File.WriteAllText(Safe.In(folder,".stignore"),DeferredIgnores(session),new UTF8Encoding(false));Engine.ConfigureFolder(session.Id,folder,session.Name,true);AddSession(session);return session;}
        catch{Sessions.Remove(session);try{Engine.RemoveFolder(session.Id);}catch(Exception){}try{P2PPack.DeleteSnapshot(folder);}catch(Exception){}throw;}
        }
    }
    internal void PrepareDeferred(P2PSession session,GeneralModState state,FixedCatalog catalog,string appData,bool includeConfigs,Action<string> report,InstalledSnapshotManifest plan=null,bool full=false){
        if(!SnapshotSessions().Contains(session)||!session.Sender||!session.Deferred||session.Completed||session.Discarded)throw new IOException("Envio antecipado inválido.");
        string zip=Safe.In(Folder(session),"pack.zip"),part=zip+".part";Safe.NoLinks(zip);Safe.NoLinks(part);
        DateTime nextCheck=DateTime.MinValue;bool remotePaused=false;
        Action checkpoint=()=>{
            while(true){
                if(session.Discarded)throw new OperationCanceledException();
                if(disposed)throw new IOException("Preparação interrompida; sessão preservada.");
                if(DateTime.UtcNow>=nextCheck){
                    nextCheck=DateTime.UtcNow.AddSeconds(1);
                    try{
                        remotePaused=session.Lifecycle
                            ?!session.Recipients.Any(p=>!Terminal(p.Phase)&&RecipientReady(session,p.Device))
                            :TransferStatus(session).RemotePaused;
                    }catch(System.Net.WebException){remotePaused=true;}
                }
                if(!session.Paused&&!remotePaused)return;
                System.Threading.Thread.Sleep(100);
            }
        };
        session.PreparationBytes=0;session.PreparationTotal=0;
        session.Stage=GeneralStyle.T("Conferindo e preparando o pacote…","Verifying and preparing the pack…");session.Error=null;Save();
        var elapsed=Stopwatch.StartNew();
        try{
            bool preparedHere=false;
            if(session.PayloadHash==null){
                if(File.Exists(zip))throw new IOException("Pacote sem registro de conclusão; preservado.");
                if(File.Exists(part))File.Delete(part);
                if(report!=null)report(session.Stage);
                Action<long,long> progress=(done,total)=>{session.PreparationBytes=done;session.PreparationTotal=total;};
                if(plan==null){
                    Action<int,int,string> scan=(done,total,item)=>session.SyncProgress=item==null?null:new P2PSyncProgress{Done=done,Total=total,Item=item};
                    var manifest=InspectForTransfer(session,review=>InstalledSnapshot.Inspect(
                        ExportState(state,session.ExportPreset,session.SelectedMods,session.ExportAllPresets),
                        session.SelectedMods,catalog,appData,includeConfigs,true,checkpoint,review,scan,includeUpdatePreferences:true));
                    IncludePortableOutputs(session,state.Destination,manifest,checkpoint);
                    if(manifest.Schema==3)InstalledSnapshot.Validate(manifest,catalog);
                    InstalledSnapshot.Write(part,state.Destination,manifest,report,checkpoint,progress,catalog);
                }else {
                    var previewConfigs=plan.Files.Where(file=>file.Generated).ToArray();
                    foreach(var config in previewConfigs){
                        checkpoint();
                        if(InstalledSnapshot.GeneratedConfigHash(config.Path,InstalledSnapshot.ReadGeneratedConfig(state.Destination,config.Path))!=config.Sha256)
                            throw new IOException(GeneralStyle.T("A configuração do anfitrião mudou após a conferência: ","The host setting changed after comparison: ")+config.Path);
                    }
                    if(full&&includeConfigs)InstalledSnapshot.AddFullPackConfigurations(plan,state,catalog,appData,checkpoint);
                    if(plan.HostConfigProtocol==0)IncludePortableOutputs(session,state.Destination,plan,checkpoint);
                    else if(previewConfigs.Length==0)InstalledSnapshot.AddOutputScopes(plan);
                    if(plan.Schema==3)InstalledSnapshot.Validate(plan,catalog);
                    if(plan.TemplateId==GeneralProjectStore.CyberpunkTemplate)VerifyHostOutsideFiles(session,state,plan,checkpoint);
                    InstalledSnapshot.Write(part,state.Destination,plan,report,checkpoint,progress,catalog);
                }
                checkpoint();
                string hash=SnapshotVerification.Hash(part,checkpoint);
                lock(Sessions){session.PayloadHash=hash;session.Size=new FileInfo(part).Length;Save();}
                preparedHere=true;
            }
            if(!File.Exists(zip)&&File.Exists(part)){
                if(new FileInfo(part).Length!=session.Size||!preparedHere&&SnapshotVerification.Hash(part,checkpoint)!=session.PayloadHash)
                    throw new IOException("Preparação interrompida divergente; preservada.");
                File.Move(part,zip);
            }else if(!File.Exists(zip)||new FileInfo(zip).Length!=session.Size||SnapshotVerification.Hash(zip,checkpoint)!=session.PayloadHash)
                throw new IOException("O pacote preparado foi alterado ou removido. Preserve os registros.");
            lock(session){
                if(session.Discarded)throw new OperationCanceledException();
                var ready=new ReadyPack{Hash=session.PayloadHash,Size=session.Size};
                ready.Tag=ReadyTag(session.Hash,session.Id,ready.Hash,ready.Size);
                Safe.WriteJson(Safe.In(Folder(session),"ready.json"),ready);
                session.Error=null;session.Stage=null;Save();
                // The ready descriptor is durable. A large ZIP can take longer to index
                // than the local API deadline, and the transport watcher still picks it up.
                ScanSyncFolder(session.Id);
                OnlineDiagnostics.Record("p2p.pack.ready",null,"Pacote preparado para envio.",
                    new{Id=DiagnosticId(session.Id),Bytes=session.Size,Milliseconds=elapsed.ElapsedMilliseconds});
            }
        }catch(Exception ex){session.Stage=null;if(!session.Discarded){session.Error=ex.Message;Save();}throw;}
        finally{session.SyncProgress=null;}
    }
    internal InstalledSnapshotManifest InspectForTransfer(P2PSession session,
        Func<Func<string,string,string,bool>,InstalledSnapshotManifest> inspect){
        var failures=new List<P2PVerificationFailure>();
        var manifest=inspect((id,name,message)=>{
            failures.Add(new P2PVerificationFailure(id,name,message));
            return true;
        });
        if(manifest.TemplateId==GeneralProjectStore.CyberpunkTemplate){
            var profile=manifest.Project.Presets.FirstOrDefault(preset=>preset.Id==session.ExportPreset)
                ??manifest.Project.Presets.FirstOrDefault();
            var selected=new HashSet<string>(session.SelectedMods,StringComparer.Ordinal);
            var includedIds=new HashSet<string>(manifest.List.Mods.Select(mod=>mod.Id),StringComparer.Ordinal);
            var absent=manifest.Project.Mods.Where(mod=>{
                string chosen;
                return selected.Contains(mod.Id)&&!includedIds.Contains(mod.Id)&&(mod.AlternativeGroup==null||
                    profile==null||profile.AlternativeChoices==null||!profile.AlternativeChoices.TryGetValue(mod.AlternativeGroup,out chosen)||chosen==mod.Id);
            }).Select(mod=>mod.Name).Concat(failures.Select(item=>item.Name)).Distinct().ToArray();
            if(absent.Length>0){
                foreach(var failure in failures)OnlineDiagnostics.Record("p2p.verify.failed",failure.Id,failure.Message);
                throw new IOException(GeneralStyle.T("Pacote pronto incompleto. Instale, ative ou repare estes mods no anfitrião e tente novamente: ",
                    "The ready-made pack is incomplete. Install, enable or repair these mods on the host and try again: ")+String.Join(", ",absent));
            }
            return manifest;
        }
        int included=manifest.List.Mods.Count;
        bool reviewed=ReviewSnapshotProblems(session,failures,included);
        if(failures.Count!=0&&included==0)
            throw new IOException(GeneralStyle.T("Nenhum mod passou na conferência. Confira os arquivos antes de tentar novamente.",
                "No mods passed verification. Check the files before trying again."));
        if(!reviewed)
            throw new OperationCanceledException(GeneralStyle.T("Envio cancelado na revisão dos mods.","Transfer cancelled during mod review."));
        return manifest;
    }

    internal bool ReviewSnapshotProblems(P2PSession session,IEnumerable<P2PVerificationFailure> failures,int included){
        var approved=new HashSet<string>(session.SkippedVerificationMods??new string[0],StringComparer.Ordinal);
        var pending=failures.Where(failure=>included==0||!approved.Contains(failure.Id)).ToArray();
        if(pending.Length==0)return true;
        if(session.SendVerifiedOnly.HasValue){
            foreach(var failure in pending)OnlineDiagnostics.Record("p2p.verify.failed",failure.Id,failure.Message);
            if(included==0)return false;
            if(!session.SendVerifiedOnly.Value)
                throw new IOException(GeneralStyle.T("Envio interrompido na conferência. Mods com falha: ",
                    "Transfer stopped at verification. Failed mods: ")+String.Join(", ",pending.Select(item=>item.Name)));
            lock(session){
                session.SkippedVerificationMods=approved.Concat(pending.Select(item=>item.Id)).Distinct(StringComparer.Ordinal).ToArray();
                Save();
            }
            return true;
        }
        var issue=new P2PVerificationIssue(pending,included);
        session.VerificationIssue=issue;
        try{
            Notify();
            while(!issue.Decision.Task.Wait(100)){
                if(disposed||session.Discarded||session.Completed)throw new OperationCanceledException();
            }
            lock(session){
                if(disposed||session.Discarded||session.Completed)throw new OperationCanceledException();
                if(included==0||!issue.Decision.Task.Result){
                    foreach(var failure in pending)OnlineDiagnostics.Record("p2p.verify.failed",failure.Id,failure.Message);
                    return false;
                }
                var previous=session.SkippedVerificationMods;
                session.SkippedVerificationMods=approved.Concat(pending.Select(failure=>failure.Id)).Distinct(StringComparer.Ordinal).ToArray();
                try{Save();}
                catch{session.SkippedVerificationMods=previous;throw;}
                foreach(var failure in pending)OnlineDiagnostics.Record("p2p.verify.skipped",failure.Id,failure.Message);
                return true;
            }
        }finally{session.VerificationIssue=null;}
    }

    internal static GeneralModState ExportState(GeneralModState state,string presetId,string[] ids,bool exportAllPresets=false){
        if(presetId==null&&!exportAllPresets)return state;
        var copy=Safe.Json.Deserialize<GeneralModState>(Safe.Json.Serialize(state));
        copy.TemplateId=state.TemplateId;copy.ProjectName=state.ProjectName;
        if(exportAllPresets){
            if(presetId!=null&&copy.SelectedPreset!=presetId){
                if(!copy.List.Presets.Any(item=>item.Id==presetId))throw new IOException("O preset escolhido para este envio não existe mais.");
                GeneralPresetProfiles.Switch(copy,presetId);
            }
            return copy;
        }
        copy.TemplateSubset=copy.TemplateId!=null;
        var preset=copy.List.Presets.SingleOrDefault(item=>item.Id==presetId);
        if(preset==null)throw new IOException("O preset escolhido para este envio não existe mais.");
        var chosen=new System.Collections.Generic.HashSet<string>(ids,StringComparer.Ordinal);
        copy.SelectedPreset=presetId;GeneralPresetProfiles.Project(copy);
        copy.List.Mods.RemoveAll(item=>!chosen.Contains(item.Id));
        preset.ModIds=copy.List.Mods.Select(item=>item.Id).ToList();
        copy.List.Presets=new System.Collections.Generic.List<GeneralModPreset>{preset};GeneralPresetProfiles.Prune(copy.List);
        return copy;
    }
    static void IncludePreviewOutputs(P2PSession session,string root,InstalledSnapshotManifest manifest,Action checkpoint){
        InstalledSnapshot.AddOutputScopes(manifest);
        InstalledSnapshot.AddGeneratedConfigs(manifest,root,session.GeneratedConfigSelection,checkpoint);
        var included=new HashSet<string>(manifest.Files.Where(file=>file.Generated).Select(file=>file.Path),StringComparer.OrdinalIgnoreCase);
        if(session.GeneratedConfigSelection.Any(item=>!included.Contains(item.Path)))
            throw new IOException(GeneralStyle.T("Uma configuração selecionada perdeu o vínculo com o mod. Confira o projeto e gere outro convite.",
                "A selected setting no longer belongs to a shared mod. Check the project and create a new invitation."));
    }

    internal static void IncludePortableOutputs(P2PSession session,string root,InstalledSnapshotManifest manifest,Action checkpoint){
        var elapsed=Stopwatch.StartNew();
        InstalledSnapshot.AddOutputScopes(manifest);
        var selected=session.GeneratedConfigSelection;
        if(selected!=null&&selected.Length>0&&manifest.Project!=null&&session.Recipients.Length>0&&session.Recipients.All(peer=>peer.SupportsGeneratedConfigs))
            InstalledSnapshot.AddGeneratedConfigs(manifest,root,selected.Where(item=>
                Path.GetExtension(item.Path).Equals(".json",StringComparison.OrdinalIgnoreCase)),checkpoint);
        OnlineDiagnostics.Record("p2p.pack.outputs",null,"Reutilizar o mapa de saídas do pacote.",new{
            Folders=manifest.OutputScopes.Count,Selected=selected==null?0:selected.Length,
            Included=manifest.Files.Count(file=>file.Generated),Milliseconds=elapsed.ElapsedMilliseconds
        });
    }

    internal static string ReadyTag(string key,string id,string hash,long size){
        if(!P2PPack.HashValid(key)||!P2PPack.HashValid(hash))throw new IOException("Assinatura de preparação inválida.");
        byte[] bytes=new byte[32];for(int i=0;i<bytes.Length;i++)bytes[i]=Byte.Parse(key.Substring(i*2,2),NumberStyles.HexNumber,CultureInfo.InvariantCulture);
        using(var mac=new HMACSHA256(bytes))return Safe.Hex(mac.ComputeHash(Encoding.UTF8.GetBytes("mod-loader-ready-1\n"+id+"\n"+hash+"\n"+size.ToString(CultureInfo.InvariantCulture))));
    }
    internal static ReadyPack ReadReady(string file,string key,string id){
        Safe.NoLinks(file);if(!File.Exists(file))return null;
        if(new FileInfo(file).Length>2048)throw new IOException("Descritor de pacote excessivo.");
        ReadyPack ready;try{ready=Safe.Json.Deserialize<ReadyPack>(File.ReadAllText(file));}catch(ArgumentException ex){throw new IOException("Descritor de pacote inválido.",ex);}
        if(ready==null||ready.Schema!=1||!P2PPack.HashValid(ready.Hash)||!P2PPack.HashValid(ready.Tag)||ready.Size<=0||ready.Size>P2PPack.MaxTotal)throw new IOException("Descritor de pacote inválido.");
        string expected=ReadyTag(key,id,ready.Hash,ready.Size);int difference=0;for(int i=0;i<expected.Length;i++)difference|=expected[i]^ready.Tag[i];
        if(difference!=0)throw new IOException("O pacote não corresponde à autorização do convite.");return ready;
    }
    bool ResolveDeferred(P2PSession session){
        var ready=ReadReady(Safe.In(Folder(session),"ready.json"),session.Hash,session.Id);if(ready==null)return false;
        if(session.PayloadHash!=null&&(session.PayloadHash!=ready.Hash||session.Size!=ready.Size))throw new IOException("O remetente mudou o pacote depois da autorização; preservado.");
        if(session.PayloadHash==null){
            lock(Sessions){session.PayloadHash=ready.Hash;session.Size=ready.Size;Save();}
            session.DownloadWatch=Stopwatch.StartNew();
        }
        return true;
    }
    internal bool DeferredPrepared(P2PSession session){return session.PayloadHash!=null&&File.Exists(Safe.In(Folder(session),"pack.zip"))&&File.Exists(Safe.In(Folder(session),"ready.json"));}
}
