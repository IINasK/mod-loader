using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

internal sealed partial class GeneralModWindow {
    Func<IEnumerable<GeneralUpdateOwnershipContext>> testUpdateContexts;

    sealed class SourceOwnershipCache {
        internal GeneralModState State;
        internal string Root,Preset,Source,BodyChoice,Fixed,FixedVariant;
        internal bool Natural;
        internal List<GeneralInstallRoute> Routes;
        internal GeneralUpdateOwnershipPlan Plan;
    }
    readonly Dictionary<string,SourceOwnershipCache> sourceUpdateOwnership=new Dictionary<string,SourceOwnershipCache>(StringComparer.Ordinal);

    sealed class SharedInstalledUpdate {
        internal GeneralModInstaller Installer;
        internal GeneralModInstaller.Record Receipt;
        internal GeneralModVariant Variant;
        internal string Fingerprint;
    }

    readonly Dictionary<string,GeneralModInstaller.UpgradeInfo> sharedInstalledVersions=
        new Dictionary<string,GeneralModInstaller.UpgradeInfo>(StringComparer.Ordinal);

    ILookup<string,SharedInstalledUpdate> ReadSharedInstalledUpdates(string root){
        var updates=new List<SharedInstalledUpdate>();
        if(String.IsNullOrWhiteSpace(root)||!Directory.Exists(root))return updates.ToLookup(item=>item.Receipt.Upgrade.Owner);
        foreach(var peer in ReadUpdateContexts(root).Skip(1).Where(context=>context.State!=null&&!String.IsNullOrWhiteSpace(context.Proof))){
            try{
                if(String.IsNullOrWhiteSpace(peer.State.Destination)||!String.Equals(Path.GetFullPath(peer.State.Destination).TrimEnd('\\'),
                    Path.GetFullPath(root).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))continue;
                var engine=new GeneralModInstaller(root,Path.Combine(peer.Data,"Instalacoes"));
                if(engine.RecoveryPending)continue;
                var mods=peer.State.List.Mods.ToDictionary(entry=>entry.Id,StringComparer.Ordinal);
                foreach(var receipt in engine.ReadUpdateRecords()){
                    var update=receipt.Upgrade;
                    if(!update.Reconciled)continue;
                    string[] owner=update.Owner.Split('/');
                    GeneralModEntry tracked;
                    if(!mods.TryGetValue(owner[0],out tracked))continue;
                    var option=UpdateProfileVariant(tracked,owner[1],update);
                    if(option==null||option.Sources[0]!=update.Source||
                        !GeneralInstallRoutes.Same(option.InstallRoutes,receipt.InstallRoutes))continue;
                    updates.Add(new SharedInstalledUpdate{Installer=engine,Receipt=receipt,Variant=option,
                        Fingerprint=GeneralUpdateOwnership.ReceiptFingerprint(receipt)});
                }
            }catch(IOException ex){OnlineDiagnostics.Record("scan.shared.receipt",null,ex.GetType().Name);}
            catch(UnauthorizedAccessException ex){OnlineDiagnostics.Record("scan.shared.receipt",null,ex.GetType().Name);}
        }
        return updates.ToLookup(item=>item.Receipt.Upgrade.Owner,StringComparer.Ordinal);
    }

    GeneralModInstaller.UpgradeInfo VerifySharedInstalledUpdate(Work work,ScanReads reads){
        var match=FindSharedInstalledUpdate(work,reads);
        return match==null?null:match.Receipt.Upgrade;
    }

    SharedInstalledUpdate FindSharedInstalledUpdate(Work work,ScanReads reads){
        if(work.Variant.Sources.Count!=1||work.Variant.NaturalOption!=null||work.Variant.CyberpunkVariant!=null||
            work.Variant.CyberpunkId!=null&&Recipe(work).Immutable)return null;
        foreach(var match in reads.SharedUpdates.Value[work.Key]){
            var receipt=match.Receipt;
            var update=receipt.Upgrade;
            string current=work.Variant.Sources[0];
            if(current!=update.Source&&current!=update.PreviousSource||
                work.Variant.CyberpunkId!=null&&work.Variant.CyberpunkId!=update.PreviousFixed||
                !GeneralInstallRoutes.Same(work.Variant.InstallRoutes,receipt.InstallRoutes))continue;
            try{
                if(!match.Installer.RecoveryPending&&
                    match.Installer.Status(receipt.Key,null,reads.Hash,work.Variant.InstallRoutes)==GeneralModInstaller.Installed&&
                    GeneralUpdateOwnership.ReceiptFingerprint(match.Installer.ReadUpdateRecord(receipt.Key))==match.Fingerprint&&
                    !match.Installer.RecoveryPending)
                    return match;
            }catch(IOException ex){OnlineDiagnostics.Record("scan.shared.verify",work.Entry.Id,ex.GetType().Name);}
            catch(UnauthorizedAccessException ex){OnlineDiagnostics.Record("scan.shared.verify",work.Entry.Id,ex.GetType().Name);}
        }
        return null;
    }

    sealed class SharedInventoryEvidence {
        internal Work Work;
        internal SharedInstalledUpdate Match;
    }

    // Inventory and status use the same complete receipt verification. These
    // observations do not transfer removal authority into the current project.
    List<SharedInventoryEvidence> ReadSharedInventoryEvidence(string root,ScanReads reads){
        reads=reads??new ScanReads(this,root);
        var result=new List<SharedInventoryEvidence>();
        foreach(var work in state.List.Mods.Select(MakeWork)){
            var match=FindSharedInstalledUpdate(work,reads);
            if(match!=null)result.Add(new SharedInventoryEvidence{Work=work,Match=match});
        }
        return result;
    }

    void ClearSourceUpdateOwnership(){sourceUpdateOwnership.Clear();}

    GeneralUpdateOwnershipPlan SourceUpdateOwnership(Work work) {
        SourceOwnershipCache cached;
        if(!sourceUpdateOwnership.TryGetValue(work.Key,out cached)||!Object.ReferenceEquals(cached.State,state)||
            !String.Equals(cached.Root,state.Destination,StringComparison.OrdinalIgnoreCase)||cached.Preset!=state.SelectedPreset||
            work.Variant.Sources.Count!=1||cached.Source!=work.Variant.Sources[0]||cached.BodyChoice!=work.BodyChoice||
            cached.Fixed!=work.Variant.CyberpunkId||cached.FixedVariant!=work.Variant.CyberpunkVariant||
            cached.Natural!=(work.Variant.NaturalOption!=null)||!GeneralInstallRoutes.Same(cached.Routes,work.Variant.InstallRoutes))return null;
        return cached.Plan;
    }

    GeneralUpdateOwnershipPlan FreshSourceUpdateOwnership(Work work,string root) {
        var contexts=ReadUpdateContexts(root);
        return GeneralUpdateOwnership.Resolve(root,contexts[0],work.Entry,work.Variant,catalog,contexts.Skip(1),testMode);
    }

    // The registered roots are bounded. Never crawl arbitrary AppData folders or
    // let a missing index silently stand for evidence that no peer owns a file.
    List<GeneralUpdateOwnershipContext> ReadUpdateContexts(string root) {
        var current=new GeneralUpdateOwnershipContext{Data=Path.GetFullPath(data),Name=project==null?state.ProjectName:project.Name,
            State=GeneralPresetProfiles.Copy(state)};
        current.Proof=GeneralUpdateOwnership.ReceiptFingerprint(new{current.State.Destination,current.State.List});
        var contexts=new List<GeneralUpdateOwnershipContext>{current};
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase){current.Data.TrimEnd('\\')};
        Action<GeneralUpdateOwnershipContext> add=context=>{
            if(context==null){contexts.Add(new GeneralUpdateOwnershipContext{Data=data,Name=T("Projeto indisponível","Unavailable project")});return;}
            if(context.State==null){contexts.Add(context.Fresh());return;}
            try {
                string key=Path.GetFullPath(context.Data).TrimEnd('\\');Safe.NoLinks(key);
                if(seen.Add(key))contexts.Add(context.Fresh());
            }catch(IOException){contexts.Add(new GeneralUpdateOwnershipContext{Data=data,Name=context.Name});}
             catch(UnauthorizedAccessException){contexts.Add(new GeneralUpdateOwnershipContext{Data=data,Name=context.Name});}
        };
        if(testMode) {
            if(testUpdateContexts!=null)try {
                var supplied=(testUpdateContexts()??Enumerable.Empty<GeneralUpdateOwnershipContext>()).Take(1001).ToArray();
                foreach(var context in supplied.Take(1000))add(context);
                if(supplied.Length>1000)add(null);
            }catch(IOException){add(null);}
            return contexts;
        }
        var stores=new List<GeneralProjectStore>();
        var roots=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Action<string> knownStore=path=>{
            try {
                path=Path.GetFullPath(path).TrimEnd('\\');Safe.NoLinks(path);
                if(roots.Add(path)&&(File.Exists(Path.Combine(path,"projects.json"))||File.Exists(Path.Combine(path,"lista.json"))))
                    stores.Add(new GeneralProjectStore(path));
            }catch(IOException){add(null);}
             catch(UnauthorizedAccessException){add(null);}
        };
        if(projectStore!=null)knownStore(projectStore.Root);
        string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach(string relative in new[]{"ModLoaderDev/Config","ModLoader/Config","ModLoaderGeral"})knownStore(Path.Combine(local,relative.Replace('/',Path.DirectorySeparatorChar)));
        foreach(var store in stores.Take(4)) {
            try {
                string indexProof=store.IndexFingerprint();
                var registered=store.Read(true).Projects;
                if(registered.Count>200){add(null);continue;}
                foreach(var peer in registered) {
                    string peerData=store.DataPath(peer),path=Path.Combine(peerData,"lista.json");
                    if(seen.Contains(Path.GetFullPath(peerData).TrimEnd('\\')))continue;
                    try {
                        Safe.NoLinks(path);
                        if(!File.Exists(path)||new FileInfo(path).Length>8000000)throw new IOException("Metadados do projeto indisponíveis.");
                        string proof=Safe.Hash(path);var other=store.Load(peer,true);
                        if(other==null||proof!=Safe.Hash(path))throw new IOException("O projeto mudou durante a conferência.");
                        if(String.IsNullOrWhiteSpace(other.Destination)||!String.Equals(Path.GetFullPath(other.Destination).TrimEnd('\\'),
                            Path.GetFullPath(root).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))continue;
                        add(new GeneralUpdateOwnershipContext{Data=peerData,State=other,Name=peer.Name,Proof=indexProof+"/"+proof});
                    }catch(IOException){add(new GeneralUpdateOwnershipContext{Data=peerData,Name=peer.Name});}
                     catch(UnauthorizedAccessException){add(new GeneralUpdateOwnershipContext{Data=peerData,Name=peer.Name});}
                }
                if(indexProof!=store.IndexFingerprint())add(null);
            }catch(IOException){add(null);}
             catch(UnauthorizedAccessException){add(null);}
        }
        return contexts;
    }

    static bool MissingSourceStatus(string status) {
        return status==null||status=="Não iniciado"||status==GeneralModInstaller.Missing||status=="Pronto para instalar"||
            status=="Arquivo não encontrado"||status==GeneralStatusPresentation.DetectedFiles;
    }

    async Task AuditSourceUpdateOwnership(List<ModUpdateRow> rows,string root,Func<string,string> readHash=null,Func<bool> active=null) {
        var captured=state;
        var observations=rows.ToLookup(row=>row.Key,StringComparer.Ordinal);
        var works=state.List.Mods.Select(MakeWork).Where(work=>SourceUpdateCandidate(work,observations[work.Key])!=null||
            ReviewUpdateArchives(work,observations[work.Key]).Length>0).ToArray();
        if(works.Length==0)return;
        var result=await Task.Run(()=>{
            var contexts=ReadUpdateContexts(root);
            var hashes=new ConcurrentDictionary<string,Lazy<string>>(StringComparer.OrdinalIgnoreCase);
            Func<string,string> hash=readHash??(path=>hashes.GetOrAdd(Path.GetFullPath(path),name=>new Lazy<string>(()=>Safe.Hash(name))).Value);
            var audited=new Dictionary<string,SourceOwnershipCache>(StringComparer.Ordinal);
            foreach(var work in works) {
                if(active!=null&&!active())break;
                var plan=GeneralUpdateOwnership.Resolve(root,contexts[0],work.Entry,work.Variant,catalog,contexts.Skip(1),testMode,hash);
                audited[work.Key]=new SourceOwnershipCache{State=captured,Root=root,Preset=captured.SelectedPreset,Source=work.Variant.Sources[0],
                    BodyChoice=work.BodyChoice,Fixed=work.Variant.CyberpunkId,FixedVariant=work.Variant.CyberpunkVariant,
                    Natural=work.Variant.NaturalOption!=null,Routes=GeneralPresetProfiles.Copy(work.Variant.InstallRoutes),Plan=plan};
            }
            return audited;
        });
        if(IsDisposed||!Object.ReferenceEquals(captured,state)||!String.Equals(state.Destination,root,StringComparison.OrdinalIgnoreCase)||active!=null&&!active())return;
        foreach(var pair in result) {
            sourceUpdateOwnership[pair.Key]=pair.Value;
            if(!pair.Value.Plan.Recognized)continue;
            string status;statuses.TryGetValue(pair.Key,out status);
            if(MissingSourceStatus(status))statuses[pair.Key]="Instalado";
            foreach(var row in rows.Where(row=>row.Key==pair.Key&&MissingSourceStatus(row.LocalStatus)))row.LocalStatus="Instalado";
        }
    }
}
