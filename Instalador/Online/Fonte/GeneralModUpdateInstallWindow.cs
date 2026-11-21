using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class ModUpdateFinalizationException : IOException {
    internal ModUpdateFinalizationException(string name,IOException cause):base(GeneralStyle.T(
        "Os arquivos de "+name+" foram atualizados, mas o cadastro ficou pendente.\nReabra o Mod Loader e execute Verificar (F5) para concluir, sem baixar novamente.\n\nDetalhe: "+cause.Message,
        name+" files were updated, but saving the project is pending.\nReopen Mod Loader and run Verify (F5) to finish without downloading again.\n\nDetails: "+cause.Message),cause){}
}

internal sealed partial class GeneralModWindow {
    UpdateBatch updateSummary;
    bool UpdateInProgress {get{return working&&updateSummary!=null;}}
    Work ActiveUpdateRow(string id){
        return UpdateInProgress&&current!=null&&current.UpdatePhase!=null&&current.Entry.Id==id?current:null;
    }

    void UpdateFilePhase(Work work,string stage,string phase,int? percent=null){
        if(!UpdateInProgress||current!=work||work.Ended||work.Batch==null||work.Batch.Cancelled)return;
        work.UpdatePhase=phase;work.UpdatePercent=percent;
        RowStatus(work,stage,percent,phase);
    }

    void OpenUpdatePage(string url){
        try{launch(url);}
        catch(System.ComponentModel.Win32Exception){ }
        catch(IOException){ }
    }

    void ShowManualUpdate(Work work,string url){
        if(UpdateInProgress&&current==work){
            currentProgress.CaptionLink=()=>OpenUpdatePage(url);
            UpdateFilePhase(work,"Aguardando download",work.NoGithubMatch?
                T("Mod não encontrado no GitHub · Download manual","Mod not found on GitHub · Manual download"):
                T("Aguardando download manual","Waiting for manual download"));
            if(!work.ManualAttentionPlayed&&!testMode){GeneralAttention.Play();work.ManualAttentionPlayed=true;}
        }
        if(work.BrowserOpened)return;
        work.BrowserOpened=true;OpenUpdatePage(url);
    }

    sealed class UpdateBatch {
        internal int Total,Completed;
        internal volatile bool Cancelled;
        volatile bool nexusLimited,githubLimited;
        internal bool Manual {get{return nexusLimited||githubLimited;}}
        internal bool Limited(bool nexus){return nexus?nexusLimited:githubLimited;}
        internal void Limit(bool nexus){if(nexus)nexusLimited=true;else githubLimited=true;}
    }
    string PendingPackage(Work work,string extension){
        string key=InstalledSnapshot.Hash(System.Text.Encoding.UTF8.GetBytes(work.UpdateTarget));
        return Safe.In(data,"PendingUpdates/"+key+extension.ToLowerInvariant());
    }
    string[] PendingPackages(Work work){
        if(work.Batch==null||work.UpdateTarget==null)return new string[0];
        return new[]{".zip",".7z",".rar"}.Select(ext=>PendingPackage(work,ext)).Where(File.Exists).ToArray();
    }
    void RetainUpdate(Work work,string archive){
        if(work.Batch.Cancelled)throw new OperationCanceledException();
        if(Path.GetDirectoryName(archive)!=work.AutoUpdateDirectory||!GeneralModDownload.IsArchivePath(archive))
            throw new IOException("Invalid update staging file.");
        string pending=PendingPackage(work,Path.GetExtension(archive));Safe.NoLinks(pending);
        Directory.CreateDirectory(Path.GetDirectoryName(pending));
        if(File.Exists(pending))File.Replace(archive,pending,null);
        else File.Move(archive,pending);
        work.HaveDownload=true;
    }
    void CleanUpdateDownload(Work work){
        string folder=work.AutoUpdateDirectory;
        if(folder==null)return;
        try {
            Safe.NoLinks(folder);
            if(Directory.Exists(folder)){
                foreach(string path in Directory.GetFiles(folder)){Safe.NoLinks(path);File.Delete(path);}
                Directory.Delete(folder,false);
            }
            string index=Safe.In(data,"UpdateIndexes/"+Path.GetFileName(folder));
            Safe.NoLinks(index);
            if(Directory.Exists(index)){string map=Safe.In(index,"sources.json");Safe.NoLinks(map);File.Delete(map);Directory.Delete(index,false);}
        }catch(IOException ex){OnlineDiagnostics.Record("update.temp.cleanup",work.Entry.Id,ex.Message);}
        catch(UnauthorizedAccessException ex){OnlineDiagnostics.Record("update.temp.cleanup",work.Entry.Id,ex.Message);}
        work.AutoUpdateDirectory=null;
    }
    async Task WaitManualAfterLimit(Work work){
        var arrivals=new System.Collections.Concurrent.ConcurrentQueue<string>();
        var watchers=new List<FileSystemWatcher>();
        pendingWebSource=work.UpdateTarget;association=null;SetWait(true,true);
        RowStatus(work,"Aguardando download");
        try{
            foreach(string folder in GeneralModDownload.WatchFolders(state.DownloadFolder)){
                try{
                    var watcher=new FileSystemWatcher(folder);
                    watchers.Add(watcher);
                    watcher.Created+=(s,e)=>arrivals.Enqueue(e.FullPath);
                    watcher.Renamed+=(s,e)=>arrivals.Enqueue(e.FullPath);
                    watcher.EnableRaisingEvents=true;
                }catch(IOException){}catch(UnauthorizedAccessException){}
            }
            ShowManualUpdate(work,work.UpdateTarget);
            var nexus=GeneralNexusLink.Parse(work.UpdateTarget);
            var github=GeneralGithubLink.Parse(work.UpdateTarget);
            while(!IsDisposed&&!exitApproved&&!skipRequests.Contains(work)&&!work.Batch.Cancelled){
                string path=association;
                if(path==null&&arrivals.TryDequeue(out path)&&GeneralModDownload.IsArchivePath(path)){
                    string name=Path.GetFileName(path);
                    if(!(github!=null&&String.Equals(name,github.Asset,StringComparison.OrdinalIgnoreCase))&&
                        !(nexus!=null&&System.Text.RegularExpressions.Regex.IsMatch(name,@"(?:^|[-_ ])"+nexus.Mod+@"(?:[-_ ]|\.)")))path=null;
                }
                if(GeneralModDownload.IsArchivePath(path)&&File.Exists(path)){
                    state.ResolvedDownloads[work.Key+"/0/"+work.UpdateTarget]=path;Persist();return;
                }
                await Task.Delay(350);
            }
        }finally{foreach(var watcher in watchers)watcher.Dispose();currentProgress.CaptionLink=null;pendingWebSource=null;association=null;SetWait(false,false);}
    }

    Task<GeneralSourceUpdatePackage> PrepareUpdate(Work work){
        if(work.Prepared!=null)return work.Prepared;
        var captured=state;
        Func<bool> active=()=>!exitApproved&&!IsDisposed&&state==captured&&!work.Batch.Cancelled;
        return work.Prepared=PrepareUpdate(work,active);
    }
    async Task<GeneralSourceUpdatePackage> PrepareUpdate(Work work,Func<bool> active){
        var nexusLink=GeneralNexusLink.Parse(work.UpdateTarget);
        if(work.Batch.Limited(nexusLink!=null)){
            work.HaveDownload=PendingPackages(work).Length!=0;
            throw new SourcePackageException("rate-limit","API limit");
        }
        try{
            Func<Uri,string> github=uri=>GeneralNexusGithubMirror.ReadCached(data,uri,testGithubMirrorFetch);
            if(nexusLink==null){
                UpdateFilePhase(work,"Verificando",T("Buscando atualização no GitHub","Looking for an update on GitHub"));
                return await ReadSourceUpdatePackage(work.UpdateTarget,active,github);
            }
            var nexus=testUpdatePackage==null?await GeneralNexusUpdatePackage.Read(work.UpdateTarget,active):await testUpdatePackage(work.UpdateTarget);
            if(PendingPackages(work).Length!=0||work.Batch.Manual||testMode&&testGithubMirrorFetch==null)return GeneralSourceUpdatePackage.From(nexus);
            try{
                UpdateFilePhase(work,"Verificando",T("Buscando atualização no GitHub","Looking for an update on GitHub"));
                var mirror=await GeneralNexusGithubMirror.Resolve(nexus,work.Entry.Name,work.Variant.References.ToArray(),
                    ()=>active()&&!work.Batch.Manual,github);
                work.NoGithubMatch=mirror==null;
                if(mirror!=null)return GeneralSourceUpdatePackage.From(nexus,(cache,control,report)=>
                    GeneralNexusGithubMirror.DownloadConfirmed(nexus,mirror,cache,control,report,path=>RetainUpdate(work,path)));
            }catch(OperationCanceledException){if(!active()||!work.Batch.Manual)throw;}
            catch(SourcePackageException ex){
                if(ex.State=="rate-limit")work.Batch.Limit(ex is NexusPackageException);
            }catch(System.Net.WebException){}catch(IOException){}
            return GeneralSourceUpdatePackage.From(nexus);
        }catch(SourcePackageException ex){
            if(ex.State=="rate-limit")work.Batch.Limit(nexusLink!=null);
            throw;
        }
    }
    void PrepareFollowing(Work downloading){
        var batch=downloading.Batch;
        if(batch==null||batch.Cancelled||batch.Manual||exitApproved||skipRequests.Contains(downloading))return;
        var following=priority.Concat(normal).FirstOrDefault();
        if(following==null||following.Batch!=batch||following.UpdateTarget==null||following.Prepared!=null)return;
        var pending=PrepareUpdate(following);
        pending.ContinueWith(task=>{var observed=task.Exception;},TaskContinuationOptions.OnlyOnFaulted);
    }
    GeneralSourceUpdatePackage pendingSourceUpdate;
    Func<string,Task<GeneralNexusUpdatePackage>> testUpdatePackage;
    Func<string,Task<GeneralGithubUpdatePackage>> testGithubPackage;
    Func<Uri,string> testGithubMirrorFetch;
    Func<GeneralSourceUpdatePackage,DownloadCache,DownloadControl,Action<DownloadProgress>,Task<VerifiedModPackage>> testSourceDownload;
    Action<string> testUpdateCheckpoint;

    // A published successor and an automatic replacement are separate facts.
    // Composite/processed packages keep their existing review flow.
    string SourceUpdateTarget(Work work) {
        string candidate=SourceUpdateCandidate(work,versionsByWork[work.Key]);
        var proof=SourceUpdateOwnership(work);
        return candidate!=null&&proof!=null&&proof.CanUpdate&&versionsByWork[work.Key].Any(HasSourceUpdate)?candidate:null;
    }

    bool SupportsSourceReplacement(Work work) {
        if(work.Variant.Sources.Count!=1||work.Variant.NaturalOption!=null||work.BodyChoice!=null||
            work.Variant.CyberpunkVariant!=null||GeneralModLibrary.NeedsDependencyVersion(work.Entry,work.Variant)||
            work.Variant.InstallRoutes.Any(route=>!route.Destination.StartsWith("%GAME%",StringComparison.Ordinal)))return false;
        if(work.Variant.CyberpunkId==null)return true;
        var recipe=Recipe(work);
        return !recipe.Immutable&&!EquipmentExOptions.Available(recipe);
    }

    string[] ReviewUpdateArchives(Work work,IEnumerable<ModUpdateRow> rows) {
        if(!SupportsSourceReplacement(work))return new string[0];
        string original=work.Variant.Sources[0];
        var installed=GeneralGithubLink.Parse(CheckedGithubSource(work.Variant,original));
        if(installed==null||installed.Tag==null||installed.Asset==null)return new string[0];
        return rows.Where(row=>row.Source==original&&HasSourceUpdate(row)&&row.Online.ReviewArchives!=null)
            .SelectMany(row=>row.Online.ReviewArchives.Select(GeneralGithubLink.Parse).Where(asset=>
                GeneralGithubLink.SameRepository(installed,asset)&&asset.Asset!=null&&asset.Tag==row.Online.Version&&
                ModVersionNumbers.Compare(asset.Tag,installed.Tag)>0))
            .Select(asset=>asset.Url).Distinct(StringComparer.Ordinal).ToArray();
    }

    string SourceUpdateCandidate(Work work,IEnumerable<ModUpdateRow> rows) {
        if(!SupportsSourceReplacement(work))return null;
        string original=work.Variant.Sources[0];
        var github=GeneralGithubLink.Parse(CheckedGithubSource(work.Variant,original));
        if(github!=null) {
            if(github.Asset==null||github.Tag==null)return null;
            var targets=rows.Where(row=>row.Source==original&&row.Online!=null&&row.Online.State=="update"&&!LocalAtLeastOnline(row))
                .Select(row=>GeneralGithubLink.Parse(row.Online.Url)).Where(target=>GeneralGithubLink.SameRepository(github,target)&&
                    target.Asset!=null&&ModVersionNumbers.Compare(target.Tag,github.Tag)>0).Select(target=>target.Url).Distinct(StringComparer.Ordinal).ToArray();
            return targets.Length==1?targets[0]:null;
        }
        var source=GeneralNexusLink.Parse(original);
        if(source==null||source.File<=0)return null;
        var updates=rows.Where(row=>row.Online!=null&&row.Online.State=="update"&&!LocalAtLeastOnline(row)).Where(row=>row.Source==work.Variant.Sources[0])
            .Select(row=>GeneralNexusLink.Parse(row.Online.Url)).Where(link=>link!=null&&link.Game==source.Game&&link.Mod==source.Mod&&link.File!=source.File)
            .Select(link=>link.Url).Distinct(StringComparer.Ordinal).ToArray();
        return updates.Length==1?updates[0]:null;
    }

    async Task<GeneralSourceUpdatePackage> ReadSourceUpdatePackage(string source,Func<bool> active=null,Func<Uri,string> github=null) {
        if(GeneralNexusLink.Parse(source)!=null){
            var nexus=testUpdatePackage==null?await GeneralNexusUpdatePackage.Read(source,active):await testUpdatePackage(source);
            return GeneralSourceUpdatePackage.From(nexus,(cache,control,progress)=>GeneralNexusGithubMirror.Download(nexus,cache,control,progress,testMode?testGithubMirrorFetch:null));
        }
        if(GeneralGithubLink.Parse(source)!=null) {
            if(testMode&&testGithubPackage==null)throw GeneralGithubUpdatePackage.Failure("unavailable");
            return GeneralSourceUpdatePackage.From(testGithubPackage==null?
                await GeneralGithubUpdatePackage.Read(source,active,github):await testGithubPackage(source));
        }
        throw UpdateNeedsReview();
    }

    async Task<VerifiedModPackage> AwaitVerifiedUpdate(Work work,GeneralSourceUpdatePackage package,string root) {
        var rejected=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool attemptedAutomatic=false,automaticFailed=false;
        pendingSourceUpdate=package;pendingWebSource=package.Source;pendingWebInfo=package.Info;
        association=null;SetWait(true,true);
        try {
            while(true) {
                CheckSkip(work);
                if(IsDisposed||exitApproved)throw new OperationCanceledException();
                while(editingAttachment)await Task.Delay(100);
                string manual=association;association=null;
                string saved=null;
                if(work.Batch!=null&&work.UpdateTarget!=null)state.ResolvedDownloads.TryGetValue(work.Key+"/0/"+work.UpdateTarget,out saved);
                var candidates=manual==null?await Task.Run(()=>PendingPackages(work).Concat(new[]{saved}.Where(File.Exists)).Concat(package.Candidates(state.DownloadFolder)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()):new[]{manual};
                foreach(string path in candidates) {
                    string identity;
                    try {var info=new FileInfo(path);identity=path+"|"+info.Length+"|"+info.LastWriteTimeUtc.Ticks;}
                    catch(IOException){continue;}
                    if(manual==null&&rejected.Contains(identity))continue;
                    currentProgress.CaptionLink=null;
                    if(UpdateInProgress)UpdateFilePhase(work,"Conferindo download",T("Reconhecendo arquivo","Identifying archive"));
                    else RowStatus(work,"Conferindo download",null,work.Entry.Name+T(": confirmando o arquivo publicado no ",": confirming the published archive with ")+package.Provider+"…");
                    try {
                        if(work.Batch!=null&&work.Batch.Limited(package.Provider=="Nexus")){
                            work.HaveDownload=true;throw new SourcePackageException("rate-limit","API limit");
                        }
                        var verified=await package.Verify(path,()=>!IsDisposed&&!exitApproved&&!skipRequests.Contains(work));
                        CheckSkip(work);return verified;
                    }catch(SourcePackageException ex) {
                        if(ex.State=="rate-limit"&&work.Batch!=null){
                            if(work.UpdateTarget!=null&&!PendingPackages(work).Contains(path)){
                                state.ResolvedDownloads[work.Key+"/0/"+work.UpdateTarget]=path;
                                Persist();
                            }
                            work.HaveDownload=true;
                            work.Batch.Limit(package.Provider=="Nexus");
                        }
                        if(ex.State!="wrong-file"&&ex.State!="incomplete")throw;
                        if(work.Batch!=null&&ex.State=="wrong-file"&&PendingPackages(work).Contains(path)){Safe.NoLinks(path);File.Delete(path);}
                        rejected.Add(identity);
                        if(manual!=null)throw;
                    }catch(IOException) {
                        // A browser can still hold the final filename for writing.
                        // Retry it on the next poll; never accept its partial bytes.
                        if(manual!=null)throw;
                    }
                }
                if(!attemptedAutomatic&&(work.Batch==null||!work.Batch.Manual)&&package.CanDownload&&(!testMode||testSourceDownload!=null)) {
                    attemptedAutomatic=true;
                    try {
                        var downloaded=await DownloadSourceUpdate(work,package);
                        if(downloaded!=null){CheckSkip(work);return downloaded;}
                    }catch(OperationCanceledException){throw;}
                    catch(SourcePackageException ex){
                        if(ex.State=="rate-limit"&&work.Batch!=null)work.Batch.Limit(ex is NexusPackageException);
                        if(ex.State=="rate-limit"&&work.HaveDownload)throw;
                        automaticFailed=true;
                    }
                    catch(IOException){automaticFailed=true;}
                    catch(UnauthorizedAccessException){automaticFailed=true;}
                }
                CheckSkip(work);ShowManualUpdate(work,package.Source);
                if(!UpdateInProgress)RowStatus(work,"Aguardando download",null,(automaticFailed?T("Download automático indisponível. ","Automatic download unavailable. "):"")+work.Entry.Name+T(
                    ": conclua o download no ",": finish the download on ")+package.Provider+T(
                    ". O app confere o pacote e continua sozinho; você também pode arrastar o arquivo aqui.",
                    ". The app verifies the archive and continues automatically; you can also drop the archive here."));
                await Task.Delay(600);
            }
        }finally {
            currentProgress.CaptionLink=null;
            pendingSourceUpdate=null;pendingWebSource=null;pendingWebInfo=null;association=null;SetWait(false,false);
        }
    }

    async Task<VerifiedModPackage> DownloadSourceUpdate(Work work,GeneralSourceUpdatePackage package){
        var captured=state;
        var sourceCache=cache.SourcesOnly?cache:new DownloadCache(Path.Combine(data,"SourceDownloads"),()=>captured.DownloadFolder);
        if(work.Batch!=null&&work.UpdateTarget!=null){
            work.AutoUpdateDirectory=Safe.In(data,"UpdateDownloads/"+Guid.NewGuid().ToString("N"));
            string files=work.AutoUpdateDirectory;
            sourceCache=new DownloadCache(Safe.In(data,"UpdateIndexes/"+Path.GetFileName(files)),()=>files);
        }
        using(var control=new DownloadControl()) {
            bool finished=false,started=false;
            Action<DownloadProgress> progress=value=>{
                if(IsDisposed||!IsHandleCreated)return;
                try{BeginInvoke((Action)(()=>{
                    if(!finished&&!IsDisposed&&state==captured&&!control.IsCancelled){
                        if(!started&&value.Status=="Baixando"){started=true;PrepareFollowing(work);}
                        if(UpdateInProgress)
                            UpdateFilePhase(work,value.Status=="Verificando"?"Conferindo download":"Baixando",
                                value.Status=="Verificando"?T("Reconhecendo arquivo","Identifying archive"):
                                value.Status=="Conectando"?T("Preparando download","Preparing download"):
                                T("Baixando arquivos","Downloading files"),value.Total>0?(int?)value.Percent:null);
                        else RowStatus(work,"Baixando",value.Percent,work.Entry.Name+T(": baixando atualização…",": downloading update…"));
                    }
                }));}catch(InvalidOperationException){}
            };
            var task=testMode?testSourceDownload(package,sourceCache,control,progress):package.Download(sourceCache,control,progress);
            try {
                while(!task.IsCompleted){
                    if(IsDisposed||exitApproved||state!=captured||skipRequests.Contains(work))control.Cancel();
                    await Task.WhenAny(task,Task.Delay(100));
                }
                var result=await task;control.Checkpoint();CheckSkip(work);return result;
            }finally {
                finished=true;
            }
        }
    }

    static IOException UpdateNeedsReview(){return new IOException(GeneralStyle.T(
        "Não há um registro completo de autoria para substituir este pacote automaticamente. A instalação atual foi preservada; revise os arquivos do mod.",
        "There is no complete ownership record for automatically replacing this package. The current installation was preserved; review the mod files."));}

    void KeepCatalogDependencies(GeneralModVariant variant,string id) {
        var recipe=catalog.Packages.SingleOrDefault(mod=>mod.Id==id);
        foreach(string dependencyId in recipe==null?new string[0]:recipe.Dependencies) {
            var dependency=state.List.Mods.FirstOrDefault(mod=>mod.Id==dependencyId||mod.Variants.Any(option=>option.CyberpunkId==dependencyId));
            if(dependency!=null&&!variant.RequiredMods.Contains(dependency.Id))variant.RequiredMods.Add(dependency.Id);
        }
    }

    string UpdatedPresetFingerprint(Work updated,string source) {
        var items=PresetWorks().OrderBy(work=>work.Entry.Id).Select(work=>{
            if(work.Key==updated.Key) {
                var variant=GeneralPresetProfiles.Copy(work.Variant);
                KeepCatalogDependencies(variant,variant.CyberpunkId);
                variant.Sources=new List<string>{source};variant.CyberpunkId=null;variant.CyberpunkVariant=null;
                work=new Work{Entry=work.Entry,Variant=variant,BodyChoice=work.BodyChoice};
            }
            return new{work.Entry.Id,Recipe=PresetRecipeKey(work,forFingerprint:true),Required=work.Variant.RequiredMods};
        }).ToList();
        return InstalledSnapshot.Hash(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(state.Destination??".").ToUpperInvariant()+"\n"+Safe.Json.Serialize(items)));
    }

    HashSet<string> UpdateSharedPaths(Work work,string root,GeneralUpdateOwnershipPlan proof) {
        var baseline=proof.Baseline;
        var paths=new HashSet<string>(proof.ProtectedPaths,StringComparer.OrdinalIgnoreCase);
        string catalogOwner=work.Variant.CyberpunkId??(baseline.Upgrade==null?null:baseline.Upgrade.PreviousFixed);
        if(IsCyberpunk(root))foreach(var recipe in catalog.Packages.Where(recipe=>recipe.Id!=catalogOwner))
            foreach(var file in recipe.Files.Where(file=>baseline.Files.Any(old=>String.Equals(old.Path,file.Path,StringComparison.OrdinalIgnoreCase))))paths.Add(file.Path);
        return paths;
    }

    void ValidateUpdatedPath(GeneralModInstaller.RecordFile file,Work work,GeneralModInstaller.Record baseline,string root) {
        string path=file.Path;
        if(GeneralInstallRoutes.External(path)||path.Split('/','\\').Any(part=>part.StartsWith(".mod-loader",StringComparison.OrdinalIgnoreCase)||
            part.Equals(".CoopModsOnline",StringComparison.OrdinalIgnoreCase)||part.Equals(".git",StringComparison.OrdinalIgnoreCase)||part.Equals(".codex",StringComparison.OrdinalIgnoreCase)))
            throw new IOException(T("Destino protegido: ","Protected destination: ")+path);
        if(!IsCyberpunk(root))return;
        bool oldOwned=baseline.Files.Any(old=>old.Owned&&String.Equals(old.Path,path,StringComparison.OrdinalIgnoreCase));
        var immutable=catalog.Packages.Where(mod=>mod.Immutable).SelectMany(mod=>mod.Files);
        var reserved=catalog.Packages.Where(mod=>mod.Id!=work.Variant.CyberpunkId).SelectMany(NightCityDensity.KnownFiles);
        if(immutable.Any(known=>String.Equals(known.Path,path,StringComparison.OrdinalIgnoreCase))||
            reserved.Any(known=>String.Equals(known.Path,path,StringComparison.OrdinalIgnoreCase))&&!oldOwned||
            path.Equals("bin/x64/Cyberpunk2077.exe",StringComparison.OrdinalIgnoreCase))
            throw new IOException(T("Destino protegido: ","Protected destination: ")+path);
        if(!oldOwned&&!LocalModPackage.Allowed(path)&&!IsEstablishedPluginDataPath(path,baseline.Files))
            throw new IOException(T("A atualização trouxe um arquivo novo sem destino reconhecido: ","The update contains a new file with no recognized destination: ")+path);
    }

    // A verified predecessor can establish a plugin's existing data layout,
    // including versioned data names that change on each release.
    static bool IsEstablishedPluginDataPath(string path,IReadOnlyList<GeneralModInstaller.RecordFile> previous){
        const string plugins="bin/x64/plugins/";
        if(!path.StartsWith(plugins,StringComparison.OrdinalIgnoreCase))return false;
        string extension=Path.GetExtension(path);
        if(!extension.Equals(".bin",StringComparison.OrdinalIgnoreCase)&&
            !extension.Equals(".txt",StringComparison.OrdinalIgnoreCase))return false;
        int pluginEnd=path.IndexOf('/',plugins.Length);
        if(pluginEnd<0)return false;
        string entryPoint=path.Substring(0,pluginEnd)+".asi";
        if(!previous.Any(file=>file.Owned&&file.Path.Equals(entryPoint,StringComparison.OrdinalIgnoreCase)))return false;

        string folder=path.Substring(0,path.LastIndexOf('/')+1);
        var siblings=previous.Where(file=>file.Path.StartsWith(folder,StringComparison.OrdinalIgnoreCase)&&
            file.Path.IndexOf('/',folder.Length)<0).ToArray();
        return siblings.All(file=>file.Owned)&&
            siblings.Any(file=>extension.Equals(Path.GetExtension(file.Path),StringComparison.OrdinalIgnoreCase));
    }

    async Task<bool> InstallQueuedUpdate(Work work,string root){
        try{await InstallSourceUpdate(work,root);return true;}
        catch(SourcePackageException ex){
            if(ex.State!="rate-limit"||work.Batch==null)throw;
        }
        string saved;
        if(!work.HaveDownload&&PendingPackages(work).Length==0&&
            !(state.ResolvedDownloads.TryGetValue(work.Key+"/0/"+work.UpdateTarget,out saved)&&File.Exists(saved)))await WaitManualAfterLimit(work);
        RowStatus(work,UpdateAvailableStatus,null,T("Download pendente de conferência. Clique em Atualizar para retomar.","Download pending verification. Click Update to resume."));
        return false;
    }

    async Task InstallSourceUpdate(Work work,string root) {
        string previous=work.Variant.Sources.Single(),preset=state.SelectedPreset;
        bool allowed=SourceUpdateTarget(work)==work.UpdateTarget||work.ReviewSelectedPackage&&
            ReviewUpdateArchives(work,versionsByWork[work.Key]).Contains(work.UpdateTarget,StringComparer.Ordinal);
        if(!allowed)throw new IOException(T("A consulta mudou. Verifique os mods novamente.","The update lookup changed. Scan the mods again."));
        var before=state;bool appliedPreset=state.AppliedPreset==preset&&!NeedsPresetLoad;
        Func<bool> active=()=>!IsDisposed&&!exitApproved&&!skipRequests.Contains(work)&&Object.ReferenceEquals(state,before)&&state.Destination==root;
        var authorized=await Task.Run(()=>FreshSourceUpdateOwnership(work,root));
        RequireUpdateProof(authorized);
        CheckSkip(work);if(!active())throw new OperationCanceledException();
        var package=work.Batch==null?await ReadSourceUpdatePackage(work.UpdateTarget,active):await PrepareUpdate(work);
        if(package.CanDownload)UpdateFilePhase(work,"Verificando",T("Atualização encontrada","Update found"));
        var verified=await AwaitVerifiedUpdate(work,package,root);
        UpdateFilePhase(work,"Conferindo download",T("Arquivo reconhecido","Archive recognized"),100);
        string target=verified.SourceUrl??package.Source;
        CheckSkip(work);if(!active())throw new OperationCanceledException();
        UpdateFilePhase(work,"Verificando",T("Preparando arquivos","Preparing files"));
        GeneralSourceInfo previousInfo;
        work.Variant.SourceInfo.TryGetValue(previous,out previousInfo);
        var previousGit=GeneralGithubLink.Parse(previous);
        var upgrade=new GeneralModInstaller.UpgradeInfo{Owner=work.Key,Preset=preset,PreviousSource=previous,Source=target,
            HistoryUtc=DateTime.UtcNow.Ticks,HistoryBackup=work.BackupUpdate?GeneralUpdateBackup.NewName():null,
            HistoryPreviousVersion=authorized.Baseline.Upgrade!=null?authorized.Baseline.Upgrade.Version:
                work.Variant.CyberpunkId!=null?ProfileRecipe(work).Version:
                previousInfo!=null?previousInfo.Version:previousGit==null?null:previousGit.Tag,
            Version=verified.Info.Version??"",PackageSha256=verified.Sha256,
            PreviousFixed=work.Variant.CyberpunkId,
            PreviousPresetFingerprint=appliedPreset?state.AppliedPresetFingerprint:null,
            ExpectedPresetFingerprint=appliedPreset?UpdatedPresetFingerprint(work,target):null};
        GeneralModInstaller.Record installed=null;
        string lastPhase=null;int? lastPercent=null;
        Action<string,int?> progress=(phase,pct)=>{
            if(phase==lastPhase&&pct==lastPercent||IsDisposed||!IsHandleCreated)return;
            lastPhase=phase;lastPercent=pct;
            try{BeginInvoke((Action)(()=>ShowUpdateInstallProgress(work,phase,pct)));}
            catch(InvalidOperationException){}
        };
        await Task.Run(()=>{
            EnsurePresetGameClosed(root);CheckSkip(work);
            using(IsCyberpunk(root)?Cyberpunk(root).LockForRouteChange():null) {
              var leases=new List<IDisposable>();
              try {
                // Generic and snapshot writers share this per-project lock.
                // Keep every inspected peer stable until the payload commits.
                var contexts=ReadUpdateContexts(root);
                foreach(var context in contexts.Where(context=>context.State!=null&&!String.Equals(Path.GetFullPath(context.Data).TrimEnd('\\'),data.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))
                    .GroupBy(context=>context.Data,StringComparer.OrdinalIgnoreCase).Select(group=>group.First()).OrderBy(context=>context.Data,StringComparer.OrdinalIgnoreCase))
                    leases.Add(new GeneralModInstaller(root,Path.Combine(context.Data,"Instalacoes")).LockUpgradeReceiptMetadata());
                var proof=FreshSourceUpdateOwnership(work,root);RequireSameUpdateProof(authorized,proof);
                var engine=Generic(root);var baseline=proof.Baseline;
                upgrade.RelatedReceipts=proof.RelatedReceipts.ToList();
                var local=engine.ReadPackageRecord(baseline.Key);
                if(!proof.ForeignAuthority&&local!=null&&local.Files.All(file=>file.Owned)){
                    if(baseline.Upgrade!=null&&!baseline.Upgrade.Reconciled)throw new IOException(T("Conclua a recuperação da atualização anterior antes de continuar.","Finish recovering the previous update before continuing."));
                    upgrade.RetiredKeys.Add(baseline.Key);
                    if(baseline.Upgrade!=null)upgrade.PreviousFixed=baseline.Upgrade.PreviousFixed;
                }
                if(proof.NativeFixed!=null)upgrade.PreviousFixed=proof.NativeFixed;
                if(proof.NativeImport!=null)upgrade.PreviousImport=proof.NativeImport;
                upgrade.PreviousReceiptIds=proof.NativeReceiptIds.ToList();
                var shared=UpdateSharedPaths(work,root,proof);
                var attached=InventoryUpdateFiles(work,root,contexts,shared);
                installed=engine.UpdatePackage(verified.Path,work.Key+"/0/"+upgrade.Source,baseline,upgrade,shared,null,
                    ()=>{EnsurePresetGameClosed(root);CheckSkip(work);},validateFile:file=>ValidateUpdatedPath(file,work,baseline,root),progress:progress,
                    backupVersion:work.BackupUpdate?(Action<GeneralModInstaller.Record>)(old=>{
                        progress("backup",null);
                        GeneralUpdateBackup.Create(data,root,old.Files.Select(file=>new KeyValuePair<string,string>(file.Path,file.Sha256)),()=>CheckSkip(work),upgrade.HistoryBackup);
                    }):null,
                    skipEntry:path=>IsCyberpunk(root)&&baseline.InstallRoutes.Count==0&&path.IndexOf('/')<0&&
                        LocalModPackage.Documentation(path)&&!baseline.Files.Any(old=>String.Equals(old.Path,path,StringComparison.OrdinalIgnoreCase)),attachedFiles:attached,requireMatchingPayload:work.ReviewSelectedPackage);
              }finally {foreach(var lease in leases.AsEnumerable().Reverse())lease.Dispose();}
            }
        });
        work.Applied=true;
        ShowUpdateInstallProgress(work,"cleanup",null);
        try {
        if(testUpdateCheckpoint!=null)testUpdateCheckpoint("files-committed");
        await RetireUpdatePredecessor(installed,root);
        bool retained=PendingPackages(work).Contains(verified.Path);
        bool temporary=retained||work.AutoUpdateDirectory!=null&&Path.GetDirectoryName(verified.Path)==work.AutoUpdateDirectory;
        ApplyUpdateRecord(installed,verified.Info,temporary?null:verified.Path,verified.TransportRepository);
        foreach(string pending in PendingPackages(work)){Safe.NoLinks(pending);File.Delete(pending);}
        if(testUpdateCheckpoint!=null)testUpdateCheckpoint("project-saved");
        work.Entry=state.List.Mods.Single(entry=>entry.Id==work.Entry.Id);
        work.Variant=work.Entry.Variants.Single(variant=>variant.Id==work.Variant.Id);
        if(await Task.Run(()=>Generic(root).Status(work.Receipt(0)))!=GeneralModInstaller.Installed)throw new IOException(T("A conferência final da atualização falhou.","Final update verification failed."));
        // The previous file's observation must not paint the updated file with
        // its predecessor's version or leave the old update badge visible.
        modUpdates.RemoveAll(row=>row.Key==work.Key);
        modUpdates.Add(new ModUpdateRow{Key=work.Key,Source=upgrade.Source,Provider=GeneralGithubLink.Parse(upgrade.Source)!=null?"github":"nexus",Expected=upgrade.Version,
            LocalStatus="Instalado",Online=new NexusUpdateResult{State="current",SourceVersion=upgrade.Version,Version=upgrade.Version,Url=upgrade.Source}});
        versionsByWork=modUpdates.ToLookup(row=>row.Key,StringComparer.Ordinal);ShowModUpdateNotice();
        await TrackPresetInstall(work,root,true);
        if(appliedPreset&&PresetFingerprint==upgrade.ExpectedPresetFingerprint){
            new GeneralPresetDeployment(root,data,()=>EnsurePresetGameClosed(root)).RebindUpdatedFingerprint(preset,upgrade.PreviousPresetFingerprint,PresetFingerprint);
            state.AppliedPreset=preset;state.AppliedPresetFingerprint=PresetFingerprint;Persist();
        }
        await RememberModOutputBaseline(work,root);
        await Task.Run(()=>Generic(root).MarkUpdateReconciled(installed.Key));
        string detail=work.Entry.Name+T(": atualizado e conferido.",": updated and verified.");
        if(IsCyberpunk(root)&&installed.Files.Any(file=>catalog.Packages.SelectMany(mod=>mod.Files).Any(known=>
            String.Equals(known.Path,file.Path,StringComparison.OrdinalIgnoreCase))))detail+=T(
                " Para compartilhar esta nova versão por P2P, os destinos reservados ainda precisam da revisão do catálogo.",
                " Sharing this new version over P2P still requires catalog review for its reserved paths.");
        if(work.AutoUpdateDirectory!=null)CleanUpdateDownload(work);
        RowStatus(work,"Instalado",100,detail);
        OnlineDiagnostics.Record("mods.update.installed",work.Entry.Id,"Pacote atualizado e conferido.",new{Variant=work.Variant.Id,Version=upgrade.Version});
        }catch(IOException ex){throw new ModUpdateFinalizationException(work.Entry.Name,ex);}
    }

    void ShowUpdateInstallProgress(Work work,string phase,int? percent){
        if(phase=="prepare")UpdateFilePhase(work,"Verificando",T("Preparando arquivos","Preparing files"),percent);
        if(phase=="install")UpdateFilePhase(work,"Instalando",T("Instalando arquivos","Installing files"),percent);
        if(phase=="backup")UpdateFilePhase(work,"Verificando",T("Criando backup","Creating backup"));
        if(phase=="cleanup")UpdateFilePhase(work,"Instalando",T("Removendo resíduos","Cleaning up"),percent);
    }

    static void RequireUpdateProof(GeneralUpdateOwnershipPlan proof) {
        if(proof==null||!proof.CanUpdate)throw new IOException(proof==null?UpdateNeedsReview().Message:proof.Reason);
    }
    static void RequireSameUpdateProof(GeneralUpdateOwnershipPlan expected,GeneralUpdateOwnershipPlan actual) {
        RequireUpdateProof(actual);
        if(expected.Fingerprint!=actual.Fingerprint)throw new IOException(GeneralStyle.T(
            "A instalação ou os projetos relacionados mudaram durante a atualização. Verifique os mods novamente; os arquivos atuais foram preservados.",
            "The installation or related projects changed during the update. Scan the mods again; the current files were preserved."));
    }

    IEnumerable<GeneralModEntry> UpdateProfiles(string modId) {
        var live=state.List.Mods.FirstOrDefault(mod=>mod.Id==modId);
        if(live==null)yield break;
        yield return live;
        foreach(var preset in state.List.Presets) {
            GeneralModEntry configured;
            if(preset.ModIds.Contains(modId)&&preset.Configurations.TryGetValue(modId,out configured)&&configured!=live)yield return configured;
        }
    }

    static GeneralModVariant UpdateProfileVariant(GeneralModEntry entry,string variantId,GeneralModInstaller.UpgradeInfo upgrade) {
        return entry.Variants.FirstOrDefault(option=>option.Id==variantId&&option.NaturalOption==null&&option.CyberpunkVariant==null&&
            (option.CyberpunkId==null||option.CyberpunkId==upgrade.PreviousFixed)&&option.Sources.Count==1&&
            (option.Sources[0]==upgrade.PreviousSource||option.Sources[0]==upgrade.Source));
    }

    void ApplyUpdateRecord(GeneralModInstaller.Record record,GeneralSourceInfo info=null,string archive=null,string repository=null) {
        var upgrade=record.Upgrade;
        RecordUpdateHistory(state.Destination,record);
        var parts=upgrade.Owner.Split('/');
        foreach(var entry in UpdateProfiles(parts[0])) {
            var variant=UpdateProfileVariant(entry,parts[1],upgrade);
            if(variant==null)continue;
            variant.Sources[0]=upgrade.Source;variant.CyberpunkId=null;variant.CyberpunkVariant=null;
            if(upgrade.PreviousFixed!=null)KeepCatalogDependencies(variant,upgrade.PreviousFixed);
            // The verified package receipt replaces the provisional path list.
            entry.DetectedPaths.Clear();
            GeneralSourceInfo saved;
            if(!variant.SourceInfo.TryGetValue(upgrade.Source,out saved))saved=new GeneralSourceInfo{Name=entry.Name,Version=upgrade.Version,
                NexusChecked=GeneralNexusLink.Parse(upgrade.Source)!=null};
            variant.SourceInfo=new Dictionary<string,GeneralSourceInfo>{{upgrade.Source,info??saved}};
            if(repository!=null&&variant.References.Count<32&&!variant.References.Any(url=>GeneralGithubLink.SameRepository(
                GeneralGithubLink.Parse(url),GeneralGithubLink.Parse(repository))))variant.References.Add(repository);
        }
        if(archive!=null)state.ResolvedDownloads[record.Key]=archive;
        if(!upgrade.Reconciled){
            state.SnapshotReceipts.Remove(upgrade.Owner);
            foreach(string key in state.CyberpunkImports.Keys.Where(key=>WorkKeyFromReceipt(key)==upgrade.Owner).ToArray())state.CyberpunkImports.Remove(key);
        }
        Persist();
    }

    async Task RecoverSourceUpdates(string root) {
        if(projectDraft!=null||!Directory.Exists(root))return;
        var engine=Generic(root);
        var records=await Task.Run(()=>{
            if(engine.RecoveryPending){EnsurePresetGameClosed(root);engine.RecoverInterruptedInstall();}
            return engine.ReadUpdateRecords();
        });
        foreach(var record in records.Where(item=>item.Upgrade.Reconciled))RecordUpdateHistory(root,record);
        // Older builds committed the upgraded source only to the selected preset.
        // Verify the receipt only while another profile still points to its exact predecessor.
        foreach(var record in records.Where(record=>record.Upgrade.Reconciled)) {
            var upgrade=record.Upgrade;
            var parts=upgrade.Owner.Split('/');
            if(!UpdateProfiles(parts[0]).Select(entry=>UpdateProfileVariant(entry,parts[1],upgrade))
                .Any(variant=>variant!=null&&variant.Sources[0]!=upgrade.Source))continue;
            if(await Task.Run(()=>engine.Status(record.Key))==GeneralModInstaller.Installed)ApplyUpdateRecord(record);
        }
        foreach(var record in records.Where(record=>!record.Upgrade.Reconciled&&record.Upgrade.Preset==state.SelectedPreset)) {
            var upgrade=record.Upgrade;var parts=upgrade.Owner.Split('/');
            var entry=state.List.Mods.FirstOrDefault(mod=>mod.Id==parts[0]);
            if(entry==null)continue;var work=MakeWork(entry);
            if(work.Key!=upgrade.Owner||work.Variant.Sources.Count!=1||
                work.Variant.Sources[0]!=upgrade.Source&&work.Variant.Sources[0]!=upgrade.PreviousSource)continue;
            if(await Task.Run(()=>engine.Status(record.Key))!=GeneralModInstaller.Installed){
                OnlineDiagnostics.Record("mods.update.recovery.review",entry.Id,"Os arquivos mudaram após a atualização interrompida. Revise a instalação.");
                continue;
            }
            await RetireUpdatePredecessor(record,root);
            ApplyUpdateRecord(record);
            work=MakeWork(state.List.Mods.Single(mod=>mod.Id==entry.Id));
            await TrackPresetInstall(work,root,true);
            if(state.AppliedPreset==state.SelectedPreset&&upgrade.ExpectedPresetFingerprint!=null&&PresetFingerprint==upgrade.ExpectedPresetFingerprint) {
                new GeneralPresetDeployment(root,data,()=>EnsurePresetGameClosed(root))
                    .RebindUpdatedFingerprint(state.SelectedPreset,upgrade.PreviousPresetFingerprint,upgrade.ExpectedPresetFingerprint);
                state.AppliedPresetFingerprint=upgrade.ExpectedPresetFingerprint;Persist();
            }
            await Task.Run(()=>engine.MarkUpdateReconciled(record.Key));
        }
    }

    Task RetireUpdatePredecessor(GeneralModInstaller.Record record,string root) {
        return Task.Run(()=>{
            var upgrade=record.Upgrade;
            if(upgrade.PredecessorsRetired)return;
            // Receipt metadata contains private local paths, never a license to
            // write arbitrary folders. Recheck registration before retirement.
            var contexts=ReadUpdateContexts(root);
            var known=new HashSet<string>(contexts.Where(context=>context.State!=null&&context.Proof!=null)
                .Select(context=>Path.GetFullPath(context.Data).TrimEnd('\\')),StringComparer.OrdinalIgnoreCase);
            foreach(var related in upgrade.RelatedReceipts) {
                GeneralModInstaller.ValidateRelatedReceipt(related);
                if(!known.Contains(related.Data))throw new IOException(T(
                    "Um projeto relacionado não está mais disponível. Os arquivos atualizados foram mantidos; recupere o projeto antes de concluir seus registros.",
                    "A related project is no longer available. Updated files were kept; restore the project before completing its records."));
            }
            foreach(var related in upgrade.RelatedReceipts.Where(related=>related.Key!=null))
                new GeneralModInstaller(root,Path.Combine(related.Data,"Instalacoes")).ForgetUpgradeReceipt(related.Key,related.Fingerprint,related.CanonicalFingerprint);
            foreach(var group in upgrade.RelatedReceipts.Where(related=>related.SnapshotId!=null)
                .GroupBy(related=>related.Data.ToUpperInvariant()+"\n"+related.SnapshotId)) {
                var related=group.First();
                if(group.Any(item=>item.Fingerprint!=related.Fingerprint||item.CanonicalFingerprint!=related.CanonicalFingerprint))throw UpdateNeedsReview();
                new InstalledSnapshotInstaller(root,related.Data,catalog,testMode)
                    .ForgetUpgradeReceipt(related.SnapshotId,group.Select(item=>item.Owner),related.Fingerprint,related.CanonicalFingerprint);
            }
            if(upgrade.PreviousSnapshot!=null&&Snapshot(root).HasUpdatePredecessor(upgrade.PreviousSnapshot,upgrade.Owner))
                Snapshot(root).Remove(upgrade.PreviousSnapshot,new[]{upgrade.Owner},null,forgetOnly:true);
            if(upgrade.PreviousImport!=null)Cyberpunk(root).RetireUpdateImport(upgrade.PreviousImport,upgrade.PreviousReceiptIds);
            if(upgrade.PreviousFixed!=null)Cyberpunk(root).RetireUpdatedRecipe(upgrade.PreviousFixed,upgrade.PreviousReceiptIds);
            Generic(root).MarkUpdatePredecessorsRetired(record.Key);upgrade.PredecessorsRetired=true;
        });
    }
}

public sealed partial class FixedInstaller {
    internal GeneralModInstaller.Record ReadUpdateBaseline(FixedMod recipe) {
        if(recipe.Immutable||!FixedData.Scan(recipe,game).IsCorrect)throw new IOException("A versão anterior não está íntegra ou está protegida.");
        var receipts=AllReceiptPaths().Select(ReadReceipt).Where(receipt=>receipt.Status=="instalado"&&receipt.Mods.Contains(recipe.Id)).ToList();
        var files=new List<GeneralModInstaller.RecordFile>();
        foreach(var file in recipe.Files.Where(file=>!file.UserData)) {
            bool owned=receipts.Any(receipt=>receipt.Files.Any(change=>change.Path==file.Path&&change.Applied&&!change.Reverted&&!change.UndoStarted&&change.After==file.Sha256&&change.Before==null));
            if(!owned)throw new IOException("A atualização precisa de autoria comprovada dos arquivos anteriores: "+file.Path);
            files.Add(new GeneralModInstaller.RecordFile{Path=file.Path,ArchivePath=file.Entry,Sha256=file.Sha256,Size=file.Size,Owned=true});
        }
        return new GeneralModInstaller.Record{Key=recipe.Id,Files=files};
    }

    internal List<string> UpdatePredecessorIds(string id,bool imported) {
        return AllReceiptPaths().Select(ReadReceipt).Where(receipt=>receipt.Status=="instalado"&&receipt.Mods.Contains(id)&&
            (receipt.Operation==LocalModPackage.ReceiptOperation)==imported).Select(receipt=>receipt.Id).Distinct().ToList();
    }

    internal void RetireUpdatedRecipe(string id,IEnumerable<string> predecessorIds) {
        var recipe=catalog.Packages.SingleOrDefault(mod=>mod.Id==id);if(recipe==null)return;
        if(recipe.Immutable)throw new IOException("A base protegida não pode ser substituída.");
        var ids=new HashSet<string>(predecessorIds,StringComparer.Ordinal);
        var paths=new HashSet<string>(recipe.Files.Where(file=>!file.UserData).Select(file=>file.Path),StringComparer.OrdinalIgnoreCase);
        if(!AllReceiptPaths().Select(ReadReceipt).Any(receipt=>ids.Contains(receipt.Id)&&receipt.Status=="instalado"&&receipt.Mods.Contains(id)&&
            receipt.Files.Any(file=>paths.Contains(file.Path)&&file.Before==null&&file.Applied&&!file.Reverted)))return;
        using(LockForRouteChange()) {
            foreach(string ledger in AllReceiptPaths()) {
                var receipt=ReadReceipt(ledger);
                if(!ids.Contains(receipt.Id)||receipt.Status!="instalado"||!receipt.Mods.Contains(id)||receipt.Operation==LocalModPackage.ReceiptOperation)continue;
                bool changed=false;
                foreach(var file in receipt.Files.Where(file=>paths.Contains(file.Path)&&file.Before==null&&file.Applied&&!file.Reverted)) {file.Reverted=true;changed=true;}
                if(changed)Save(ledger,receipt);
            }
        }
    }

    internal void RetireUpdateImport(string id,IEnumerable<string> predecessorIds) {
        var ids=new HashSet<string>(predecessorIds,StringComparer.Ordinal);
        string folder=Safe.In(Data,"importados");Safe.NoLinks(folder);
        if(!Directory.Exists(folder)||!Directory.EnumerateFiles(folder,"*.json").Select(ReadReceipt)
            .Any(receipt=>ids.Contains(receipt.Id)&&receipt.Status=="instalado"&&receipt.Mods.Contains(id)))return;
        using(LockForRouteChange())foreach(string ledger in Directory.EnumerateFiles(folder,"*.json")) {
            var receipt=ReadReceipt(ledger);
            if(!ids.Contains(receipt.Id)||receipt.Status!="instalado"||!receipt.Mods.Contains(id))continue;
            receipt.Status="desfeito";Save(ledger,receipt);
        }
    }
}

public sealed partial class InstalledSnapshotInstaller {
    internal bool HasUpdatePredecessor(string id,string owner) {
        var receipt=Read(id);return receipt!=null&&receipt.Status=="installed"&&receipt.ActiveOwners.Contains(owner);
    }
}
