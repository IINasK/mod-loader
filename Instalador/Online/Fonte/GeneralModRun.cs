using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    sealed class Work {
        internal GeneralModEntry Entry;
        internal GeneralModVariant Variant;
        internal bool Ended,Applied,SwitchAlternatives,DependencyQueue,SaveVariant;
        internal HashSet<string> DropConflicts;
        internal string DropDensity, BodyChoice, ScanError;
        internal double Fraction;
        internal string Key { get{return GeneralModWindow.Key(Entry,Variant);} }
        internal bool Local { get{return Variant.CyberpunkId==null&&Variant.Sources.All(GeneralModDownload.IsArchivePath);} }
        internal string Receipt(int index){return Key+"/"+index+"/"+Variant.Sources[index];}
    }
    readonly List<Work> normal=new List<Work>(),priority=new List<Work>();
    Work current, priorityCurrent;
    bool waiting,NeedUnknownFile,editingAttachment,suppressQueueErrors;
    Func<string,bool> testFailurePrompt;
    readonly HashSet<Work> skipRequests=new HashSet<Work>();
    Work scanCurrent;
    int operationTotal,operationFinished;
    Action<string,string> testStage;
    string association, pendingWebSource;
    GeneralSourceInfo pendingWebInfo;
    FixedAsset pendingFixedAsset;
    string testBrowserDownloadChannel;
    string BrowserDownloadChannel { get { return testMode ? testBrowserDownloadChannel : BrowserDownloadBridge.Channel; } }
    Action<string> launch=GeneralModDownload.OpenSource;
    GeneralModInstaller Generic(string root){return new GeneralModInstaller(root,Path.Combine(data,"Instalacoes"));}
    InstalledSnapshotInstaller Snapshot(string root){return new InstalledSnapshotInstaller(root,data,catalog,testMode);}
    FixedInstaller Cyberpunk(string root){return new FixedInstaller(catalog,cache,root,testMode?Path.Combine(data,"fixture-saves"):Discovery.Saves(),testMode);}
    static bool IsCyberpunk(string root){return !String.IsNullOrEmpty(root)&&File.Exists(Path.Combine(root,"bin","x64","Cyberpunk2077.exe"));}
    FixedMod Recipe(Work work){return catalog.Packages.Single(m=>m.Id==work.Variant.CyberpunkId);}
    List<FixedMod> ExpandWork(Work work, string root) {
        var recipe = Recipe(work);
        bool stock = EquipmentExOptions.Available(recipe)
            ? work.Variant.Id == EquipmentExOptions.StockId
            : EquipmentExOptions.StockActive(catalog, root);
        return FixedData.Expand(catalog, new[] { recipe.Id }, stock);
    }

    FixedMod ProfileRecipe(Work work) {
        var recipe = Recipe(work);
        if (EquipmentExOptions.Available(recipe)) return EquipmentExOptions.Resolve(recipe, work.Variant.Id);
        return work.Variant.CyberpunkVariant == null ? recipe : NightCityDensity.Resolve(recipe, work.Variant.CyberpunkVariant);
    }
    Work MakeWork(GeneralModEntry item){
        var selected=Selected(item);
        return new Work{Entry=item,Variant=GeneralModLibrary.BodyOption(state.BodyChoice,item,selected),
            BodyChoice=IsBigBody(item)?ActiveBodyChoice(item):null};
    }
    string LocalSource(Work work,int index){
        string source=work.Variant.Sources[index],resolved;
        return GeneralModDownload.IsArchivePath(source)?source:
            P2PPack.TrySlot(state.ResolvedDownloads,work.Entry,work.Variant,index,out resolved)?resolved:null;
    }
    static string ExistingReceipt(GeneralModInstaller engine,Work work,int index){
        string key=work.Receipt(index);
        string legacy=P2PPack.LegacySlot(work.Entry,work.Variant,index);
        return legacy!=null&&!engine.HasSnapshotRecord(key)&&engine.HasSnapshotRecord(legacy)?legacy:key;
    }
    static ModImportItem ImportItem(string source){
        string hash=Safe.Hash(source);
        return new ModImportItem{Id="file-"+hash,Kind="Arquivo",Name=Path.GetFileName(source),Source=source,Sha256=hash,Size=new FileInfo(source).Length};
    }
    string CyberpunkImportId(Work work,int index,Func<string,string> readHash=null){
        string id;
        if(P2PPack.TrySlot(state.CyberpunkImports,work.Entry,work.Variant,index,out id))return id;
        if(work.Variant.InstallRoutes.Count==0&&work.Entry.Id.StartsWith("file-",StringComparison.Ordinal)&&work.Entry.Id.Length==69)return work.Entry.Id;
        string path=LocalSource(work,index);
        return path!=null&&File.Exists(path)?LocalModPackage.RoutedId(readHash==null?Safe.Hash(path):readHash(path),work.Variant.InstallRoutes):null;
    }
    List<Work> Marked(){return ModRows().Where(r=>marked.Contains((string)r.Tag)).Select(r=>MakeWork(state.List.Mods.Single(m=>m.Id==(string)r.Tag))).ToList();}
    void Stage(Work work,string phase){if(testMode&&testStage!=null)testStage(work.Key,phase);}
    void RequestSkip(){var work=scanning?scanCurrent:priorityCurrent??current;if(work!=null)skipRequests.Add(work);}
    void CheckSkip(Work work){if(skipRequests.Contains(work))throw new OperationCanceledException();}
    void UpdateFlow(Work work,string status,int? percent=null){
        if(work!=null){
            work.Fraction=percent.HasValue?Math.Max(0,Math.Min(100,percent.Value))/100.0:work.Fraction;
            currentProgress.Value=percent.GetValueOrDefault();currentProgress.Indeterminate=!percent.HasValue;
            ShowWorkCaption(work,status);
        }
        var active=priorityCurrent??current??scanCurrent;
        double fraction=active==null||active.Ended?0:active.Fraction;
        UpdatePackCoverage();

        totalProgress.Indeterminate=false;totalProgress.Invalidate();
    }
    void ShowWorkCaption(Work work,string status){
        currentProgress.NotApplicable=ListStatus(work.Entry)==GeneralStatusPresentation.OtherChoice;
        if(currentProgress.NotApplicable){
            currentProgress.Caption=work.Entry.Name+" · "+GeneralStatusPresentation.Label(GeneralStatusPresentation.OtherChoice);
            currentProgress.Value=0;
            currentProgress.Indeterminate=false;
        }else currentProgress.Caption=work.Entry.Name+" · "+(status==GeneralStatusPresentation.DetectedFiles
            ?GeneralStatusPresentation.Label(status):GeneralStyle.Status(status));
        currentProgress.Invalidate();
    }
    string Root(){
        if(p2p!=null&&p2p.Installing)throw new IOException(T("Aguarde a instalação do pacote recebido terminar.","Wait for the received pack installation to finish."));
        if(String.IsNullOrWhiteSpace(state.Destination)||!Directory.Exists(state.Destination))throw new IOException(T("Escolha a pasta de instalação em Opções.","Choose the installation folder in Options."));
        string root=Path.GetFullPath(state.Destination);Safe.NoLinks(root);
        return root;
    }
    void RowStatus(Work work,string status,int? pct=null,string message=null){
        string previous;statuses.TryGetValue(work.Key,out previous);
        statuses[work.Key]=status;
        if(message!=null)statusDetails[work.Key]=message;else statusDetails.Remove(work.Key);
        if((coverageFilter!=null||statusFilters!=null||orderColumn=="Status"||work.Entry.AlternativeGroup!=null||work.Entry.Variants.Count>1)&&previous!=status)DrawRows();
        if(pct.HasValue)progress[work.Key]=pct.Value;else progress.Remove(work.Key);
        var visible=grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r=>Equals(r.Tag,work.Entry.Id));
        var listed=state.List.Mods.FirstOrDefault(m=>m.Id==work.Entry.Id);
        if(visible!=null&&listed!=null&&MakeWork(listed).Key==work.Key){
            string shown=ListStatus(listed);
            string raw=InstallationStatus(listed,shown);
            visible.Cells["Status"].Tag=raw;
            visible.Cells["Status"].Value=GeneralStatusPresentation.Label(raw);visible.Cells["Status"].ToolTipText=TableStatusDetail(listed,work.Key,shown);
            visible.Cells["Progress"].Value=pct.HasValue?(object)pct.Value:"—";grid.InvalidateCell(visible.Cells["Status"]);UpdateCounts();
        }
        if(message!=null)detail.Text=message;
        if(working||scanning)UpdateFlow(work,status,pct);
    }
    string ListStatus(GeneralModEntry mod){

        string raw;statuses.TryGetValue(MakeWork(mod).Key,out raw);
        if(raw!=GeneralStatusPresentation.NexusManual&&raw!=GeneralStatusPresentation.NexusAmbiguous&&
            GeneralModLibrary.NeedsDependencyVersion(mod,Selected(mod)))return GeneralStatusPresentation.ChooseDependency;
        if(mod.AlternativeGroup!=null&&(raw==null||raw=="Não iniciado"||raw=="Não instalado")&&
            state.List.Mods.Any(other=>{
                if(other.Id==mod.Id||other.AlternativeGroup!=mod.AlternativeGroup)return false;
                return IsActiveAlternative(other);
            }))
            return GeneralStatusPresentation.OtherChoice;
        return raw;
    }
    string TableStatusDetail(GeneralModEntry entry,string key,string status){
        return StatusDetail(key,IsBigBody(entry)&&status==GeneralStatusPresentation.OtherChoice?"Não iniciado":status);
    }
    string StatusDetail(string key,string status){
        if(status==GeneralStatusPresentation.NexusManual)
            return T("Os caminhos e os tamanhos disponíveis coincidem com uma única prévia do Nexus. A origem dos arquivos não foi verificada por hash.",
                "The paths and available sizes match a single Nexus preview. File origin has not been verified by hash.");
        if(status==GeneralStatusPresentation.NexusAmbiguous)
            return T("Há mais de uma versão possível ou faltam dados para descartar outra variante.",
                "More than one version is possible, or there is insufficient information to rule out another variant.");
        if(status==GeneralStatusPresentation.ManualBody&&gameInventory.Any(file=>file.Kind=="verified-variant"&&file.ModId+"/"+file.VariantId==key))
            return T("Versão instalada manualmente, conferida pelo conteúdo dos arquivos.",
                "Manually installed version, verified against the file contents.");
        if(status==GeneralStatusPresentation.ManualBody)
            return T("Arquivos do corpo encontrados no jogo. Falta o registro de instalação usado na exportação.",
                "Body files found in the game. The installation record used for export is missing.");
        if(status==GeneralStatusPresentation.DetectedFiles)
            return T("A lista registra os caminhos vistos na verificação. O pacote completo ainda não foi conferido.",
                "The list records paths seen during scanning. The complete package is still unverified.");
        if(status==GeneralStatusPresentation.OtherChoice)return GeneralStatusPresentation.Label(status);
        string message;return statusDetails.TryGetValue(key,out message)?message:GeneralStyle.Status(status??"Não conferido");
    }
    string FailureReport(){
        var lines=new List<string>{GeneralAppPaths.WindowTitle,"Catalog: "+OnlineIdentity.CatalogHash};
        foreach(var mod in state.List.Mods){var variant=Selected(mod);string key=Key(mod,variant),status;if(!statuses.TryGetValue(key,out status)||GeneralStatusPresentation.Kind(status)!="error")continue;lines.Add("\r\n"+mod.Name+" ["+mod.Id+"/"+variant.Id+"]\r\n"+StatusDetail(key,status));}
        return String.Join("\r\n",lines);
    }
    void ShowFocusedFailure(){var mod=FocusedEntry();if(mod==null)return;string key=Key(mod,Selected(mod)),status;if(statuses.TryGetValue(key,out status)&&GeneralStatusPresentation.Kind(status)=="error")GeneralDialog.Message(this,StatusDetail(key,status),T("Detalhes da falha","Failure details"));}
    void CopyFailures(){try{Clipboard.SetText(FailureReport());detail.Text=T("Erros copiados. Cole a mensagem para compartilhar o diagnóstico.","Errors copied. Paste to share the diagnostics.");}catch(Exception ex){ShowError(ex);}}
    sealed class ScanReads {
        readonly ConcurrentDictionary<string,Lazy<string>> hashes=new ConcurrentDictionary<string,Lazy<string>>(StringComparer.OrdinalIgnoreCase);
        readonly ConcurrentDictionary<FixedMod,Lazy<ModScan>> fixedMods=new ConcurrentDictionary<FixedMod,Lazy<ModScan>>();
        readonly string root;
        internal readonly bool Exists,Cyberpunk,PhantomLiberty;
        internal readonly Lazy<InstalledSnapshotInstaller.StatusScan> Snapshots;
        internal readonly Lazy<GeneralModInstaller> Generic;
        internal readonly Lazy<HashSet<string>> Imports, Disabled;
        internal readonly Lazy<ILookup<long,string>> DownloadNames;
        internal int HashCount {get{return hashes.Count;}}
        internal ScanReads(GeneralModWindow window,string root){
            this.root=root;
            Snapshots=new Lazy<InstalledSnapshotInstaller.StatusScan>(()=>new InstalledSnapshotInstaller.StatusScan(window.Snapshot(root),Hash));
            Generic=new Lazy<GeneralModInstaller>(()=>window.Generic(root));
            Exists=!String.IsNullOrEmpty(root)&&Directory.Exists(root);
            Cyberpunk=IsCyberpunk(root);
            PhantomLiberty=Cyberpunk&&GameEdition.HasPL(root);
            Imports=new Lazy<HashSet<string>>(()=>window.Cyberpunk(root).InstalledImports(Hash));
            Disabled=new Lazy<HashSet<string>>(()=>new HashSet<string>(window.DisabledAt(root).Read().Keys,StringComparer.Ordinal));
            DownloadNames=new Lazy<ILookup<long,string>>(()=>LocalDownloadNames(window.state.DownloadFolder));
        }
        internal string Hash(string path){return hashes.GetOrAdd(Path.GetFullPath(path),key=>new Lazy<string>(()=>Safe.Hash(key))).Value;}
        internal ModScan Fixed(FixedMod mod){return fixedMods.GetOrAdd(mod,item=>new Lazy<ModScan>(()=>FixedData.Scan(item,root,true,Hash,false))).Value;}
    }

    static ILookup<long,string> LocalDownloadNames(string folder) {
        var names = new List<KeyValuePair<long, string>>();
        try {
            if (Directory.Exists(folder)) {
                Safe.NoLinks(folder);
                foreach (string path in Directory.EnumerateFiles(folder).Take(10000).Where(GeneralModDownload.IsArchivePath)) {
                    try {
                        var info = new FileInfo(path);
                        names.Add(new KeyValuePair<long, string>(info.Length, info.Name));
                    } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }
        } catch (IOException) { } catch (UnauthorizedAccessException) { }
        return names.ToLookup(item => item.Key, item => item.Value);
    }

    bool LocalFixedAssets(FixedMod recipe, string root, ScanReads reads) {
        if (reads == null) return false;
        Func<FixedAsset, bool> present = asset => {
            if (reads.DownloadNames.Value[asset.Size].Any(name => name.Equals(asset.Name, StringComparison.OrdinalIgnoreCase)
                || (name.StartsWith(Path.GetFileNameWithoutExtension(asset.Name) + " (", StringComparison.OrdinalIgnoreCase)
                    && name.EndsWith(")" + Path.GetExtension(asset.Name), StringComparison.OrdinalIgnoreCase))))
                return true;
            var cached = new FileInfo(cache.PathFor(asset));
            return cached.Exists && cached.Length == asset.Size;
        };
        return recipe.Assets.Count > 0 && recipe.Assets.All(present) &&
            FixedData.Expand(catalog, recipe.Dependencies,EquipmentExOptions.StockActive(catalog,root)).All(dependency =>
                reads.Fixed(dependency).IsCorrect || dependency.Assets.All(present));
    }

    string Status(Work work,string root,ScanReads reads=null,bool includeNexus=true){
        if(GeneralModLibrary.NeedsDependencyVersion(work.Entry,work.Variant))return GeneralStatusPresentation.ChooseDependency;
        bool exists=reads==null?!String.IsNullOrEmpty(root)&&Directory.Exists(root):reads.Exists;
        bool cyberpunk=reads==null?IsCyberpunk(root):reads.Cyberpunk;
        if(exists&&(reads==null?DisabledAt(root).Read().ContainsKey(work.Key):reads.Disabled.Value.Contains(work.Key)))
            return GeneralStatusPresentation.Disabled;
        Func<string,string> hash=reads==null?null:(Func<string,string>)reads.Hash;
        string snapshot;
        if(exists&&work.Variant.CyberpunkId!="NightCoop"&&work.Variant.CyberpunkId!=EquipmentExOptions.ModId&&TryWorkSnapshot(work,root,out snapshot)){
            string owner=InstalledSnapshot.Owner(work.Entry,work.Variant);
            string found=reads==null?Snapshot(root).Status(snapshot,owner):reads.Snapshots.Value.Status(snapshot,owner);
            if(found!=GeneralModInstaller.Missing||work.Variant.CyberpunkId==null)return found;
        }
        if(work.Variant.CyberpunkId!=null){
            if(!cyberpunk)return "Requer Cyberpunk 2077";
            var recipe=Recipe(work);
            if(GameEdition.NeedsPL(recipe)&&!(reads==null?GameEdition.HasPL(root):reads.PhantomLiberty))return "Requer Phantom Liberty";
            if (EquipmentExOptions.Available(recipe)) {
                var option = EquipmentExOptions.Read(recipe, root, hash);
                if (!option.CanApply) return "Diferente/alterado";
                if (option.Id == work.Variant.Id && !option.Partial) return "Instalado";
                if (option.Id != "absent" && option.Id != work.Variant.Id) return "Versão diferente";
                if (option.Partial) return "Parcial";
                return LocalFixedAssets(ProfileRecipe(work), root, reads) ? "Pronto para instalar" : "Não instalado";
            }
            if(work.Variant.CyberpunkVariant!=null){
                var density=NightCityDensity.Read(recipe,root);
                if(density.Id==work.Variant.CyberpunkVariant)return "Instalado";
                if(!density.CanApply||density.Partial)return density.Name??"Instalação parcial";
                if(density.Id=="legacy-low"&&work.Variant.CyberpunkVariant=="low")return "Instalado: "+density.Name;
                if(NightCityDensity.Supported(density.Id))return "Instalado: "+density.Name;
                return LocalFixedAssets(NightCityDensity.Resolve(recipe,work.Variant.CyberpunkVariant),root,reads)?"Pronto para instalar":"Não instalado";
            }
            if(work.Variant.InstallRoutes.Count>0)
                return RoutedFixedStatus(work,root,hash);
            var scan=reads==null?FixedData.Scan(recipe,root):reads.Fixed(recipe);
            if(scan.State=="Não instalado"&&LocalFixedAssets(recipe,root,reads))return "Pronto para instalar";
            return scan.IsCorrect?"Instalado":scan.State=="Atualização disponível"?scan.State:scan.Original>0?"Versão diferente":scan.State;
        }
        if(exists){
            List<string> results;
            if(cyberpunk){
                var installed=reads==null?Cyberpunk(root).InstalledImports():reads.Imports.Value;
                results=Enumerable.Range(0,work.Variant.Sources.Count).Select(i=>{
                    return installed.Contains(CyberpunkImportId(work,i,hash))?GeneralModInstaller.Installed:GeneralModInstaller.Missing;
                }).ToList();
            }else{
                var engine=reads==null?Generic(root):reads.Generic.Value;
                results=Enumerable.Range(0,work.Variant.Sources.Count)
                    .Select(i=>engine.Status(ExistingReceipt(engine,work,i),LocalSource(work,i),hash??(Func<string,string>)Safe.Hash,work.Variant.InstallRoutes)).ToList();
            }
            if(results.All(r=>r==GeneralModInstaller.Installed))return "Instalado";
            if(results.Contains("Versão diferente"))return "Versão diferente";
            if(results.Contains(GeneralModInstaller.Modified))return GeneralModInstaller.Modified;
            if(results.Any(r=>r!=GeneralModInstaller.Missing))return GeneralModInstaller.Partial;
        }
        if(cyberpunk&&GeneralModLibrary.IsHystBody(work.Entry.Id)){
            var known=seed.Mods.FirstOrDefault(mod=>mod.Id==work.Entry.Id);
            var detected=InstalledSnapshot.DetectManualHyst(root,work.Entry,known,null,hash,work.BodyChoice);
            if(detected!=null)
                return detected.Id==work.Variant.Id?GeneralStatusPresentation.ManualBody:"Versão diferente";
            if(HasManualHystBody(work,root)){
                var saved=work.Entry.Variants.FirstOrDefault(version=>version.Id==work.Variant.Id);
                bool customNatural=work.BodyChoice==GeneralModLibrary.NaturalBody&&saved!=null&&saved.NaturalOption!=null&&
                    (!saved.Sources.SequenceEqual(work.Variant.Sources,StringComparer.Ordinal)||
                     !GeneralInstallRoutes.Same(saved.InstallRoutes,work.Variant.InstallRoutes));
                return customNatural?"Versão diferente":GeneralStatusPresentation.ManualBody;
            }
        }
        if(exists&&includeNexus&&MatchesNexusInstallation(work,root))
            return GeneralStatusPresentation.NexusManual;
        if(work.Variant.Sources.Any(s=>GeneralModDownload.IsArchivePath(s)&&!File.Exists(s)))return "Arquivo não encontrado";
        if(work.Variant.Sources.Count>0&&Enumerable.Range(0,work.Variant.Sources.Count).All(index=>File.Exists(LocalSource(work,index))))
            return "Pronto para instalar";
        if(cyberpunk&&work.Variant.Id==work.Entry.DefaultVariant&&work.Entry.DetectedPaths.Any(path=>File.Exists(Safe.In(root,path))))
            return GeneralStatusPresentation.DetectedFiles;
        return "Não iniciado";
    }
    bool MatchesNexusInstallation(Work work,string root) {
        NexusManualMatch match;
        return String.Equals(root,state.Destination,StringComparison.OrdinalIgnoreCase)&&
            nexusManual.TryGetValue(work.Entry.Id,out match)&&Object.ReferenceEquals(match.Variant,work.Variant)&&match.StillPresent(root);
    }

    async Task Scan(ProjectVerification audit=null){
        if(working||scanning)return;
        if(String.IsNullOrWhiteSpace(state.Destination)){
            RefreshPrimaryAction();
            RefreshStorageSize();
            return;
        }
        scanning=true;Busy(true);
        string root=state.Destination;
        nexusManual=new Dictionary<string,NexusManualMatch>(StringComparer.Ordinal);
        foreach(var key in statuses.Where(item=>item.Value==GeneralStatusPresentation.NexusManual||
            item.Value==GeneralStatusPresentation.NexusAmbiguous).Select(item=>item.Key).ToArray())statuses.Remove(key);
        var works=state.List.Mods.Select(MakeWork).ToList();
        operationTotal=works.Count;operationFinished=0;skipRequests.Clear();
        string previous=detail.Text;detail.Text=T("Conferindo a pasta de instalação…","Checking the installation folder…");
        try{
            var elapsed=Stopwatch.StartNew();
            var reads=new ScanReads(this,root);
            if(audit!=null)audit.Reads=reads;
            var inventory=reads.Exists?Task.Run(()=>TryReadGameInventory(root,reads)):null;
            int workers=Math.Max(1,Math.Min(4,Environment.ProcessorCount/2));
            var visibleRows=ModRows().ToDictionary(row=>(string)row.Tag);
            var pending=new Queue<Task<string>>();
            Func<Work,Task<string>> verify=work=>Task.Run(async()=>{
                if(audit!=null)await audit.ReadSources(work);
                try{return Status(work,root,reads,false);}
                catch(Exception ex){work.ScanError=ex.Message;return "Não verificável";}
            });
            int scheduled=0;
            while(scheduled<Math.Min(works.Count,workers))pending.Enqueue(verify(works[scheduled++]));
            totalProgress.Segments=null;
            for(int i=0;i<works.Count;i++){
                var work=works[i];
                scanCurrent=work;
                currentProgress.Caption=work.Entry.Name+" · "+T("Verificando","Checking");
                currentProgress.Value=0;currentProgress.Indeterminate=true;currentProgress.NotApplicable=false;
                Stage(work,"scan.begin");
                string found=await pending.Dequeue();
                if(scheduled<works.Count)pending.Enqueue(verify(works[scheduled++]));
                bool skipped=skipRequests.Contains(work);
                string result=skipped?"Verificação pulada":found;
                if(!skipped&&audit!=null)audit.Record(work);
                statusDetails.Remove(work.Key);
                if(!skipped&&!String.IsNullOrWhiteSpace(work.ScanError))statusDetails[work.Key]=work.ScanError;
                statuses[work.Key]=result;progress.Remove(work.Key);
                DataGridViewRow row;
                if(visibleRows.TryGetValue(work.Entry.Id,out row)&&row.DataGridView==grid){
                    string raw=InstallationStatus(work.Entry,result);
                    row.Cells["Status"].Tag=raw;
                    row.Cells["Status"].Value=GeneralStatusPresentation.Label(raw);
                    row.Cells["Status"].ToolTipText=TableStatusDetail(work.Entry,work.Key,result);
                    grid.InvalidateCell(row.Cells["Status"]);
                }
                operationFinished++;work.Ended=true;Stage(work,"scan.end");
                totalProgress.Caption=T("Mods verificados: ","Mods checked: ")+operationFinished+" / "+operationTotal;
                totalProgress.Value=100*operationFinished/operationTotal;
                if(operationFinished%8==0)await Task.Yield();
            }
            if(works.Count>0){
                var last=works[works.Count-1];currentProgress.Indeterminate=false;
                ShowWorkCaption(last,statuses[last.Key]);
                currentProgress.Value=statuses[last.Key]=="Instalado"?100:0;
            }
            OnlineDiagnostics.Record("general.scan.complete",null,"Varredura da lista concluída.",
                new{Mods=works.Count,Workers=workers,UniqueHashes=reads.HashCount,ImportsRead=reads.Imports.IsValueCreated,ElapsedMs=elapsed.ElapsedMilliseconds});
            var foundFiles=inventory==null?null:await inventory;
            gameInventory=foundFiles??new List<P2PSyncDifference>();
            foreach(var active in gameInventory.Where(file=>file.Kind=="verified-variant")){
                string key=active.ModId+"/"+active.VariantId;
                string status;
                if(!statuses.TryGetValue(key,out status)||status=="Não iniciado"||status==GeneralStatusPresentation.ManualBody)
                    statuses[key]=GeneralStatusPresentation.ManualBody;
            }

            scanCurrent=null;
            var checkedKeys=new HashSet<string>(works.Select(work=>work.Key),StringComparer.Ordinal);
            var otherVersions=state.List.Mods.Where(entry=>entry.Variants.Count>1||IsBigBody(entry))
                .SelectMany(VersionWorks).Where(work=>checkedKeys.Add(work.Key)).ToArray();
            var versionStatuses=await Task.Run(()=>otherVersions.Select(work=>{
                try{return Status(work,root,reads,false);}
                catch(Exception ex){work.ScanError=ex.Message;return "Não verificável";}
            }).ToArray());
            for(int i=0;i<otherVersions.Length;i++){
                var work=otherVersions[i];
                statuses[work.Key]=versionStatuses[i];
                statusDetails.Remove(work.Key);
                if(!String.IsNullOrWhiteSpace(work.ScanError))statusDetails[work.Key]=work.ScanError;
            }
            await ScanNexusPreviews(works,root,reads);

            if(audit!=null){
                audit.InventoryUnavailable=reads.Cyberpunk&&foundFiles==null;
                if(audit.MetadataChanged>0)Persist();
            }
            DrawRows();RefreshStorageSize();
        }finally{scanCurrent=null;detail.Text=previous;scanning=false;Busy(false);}
    }
    async Task ScanNexusPreviews(List<Work> works,string root,ScanReads reads) {
        if(IsDisposed||!reads.Exists||!NexusPreviewsAvailable)return;
        var candidates=works.Where(work=>!skipRequests.Contains(work)&&NexusManualScan.CanInspect(work.Entry)&&
            (statuses[work.Key]=="Não iniciado"||statuses[work.Key]=="Pronto para instalar"||
            statuses[work.Key]==GeneralStatusPresentation.DetectedFiles)).ToList();
        if(candidates.Count==0)return;
        detail.Text=T("Comparando com os arquivos do Nexus…","Comparing with Nexus file previews…");
        var fetch=testNexusPreviews??GeneralNexusInfo.ReadFilePreviews;
        nexusManual=await Task.Run(()=>nexusScan.Inspect(root,reads.Cyberpunk,candidates.Select(work=>work.Entry),fetch));
        foreach(var work in candidates){
            NexusManualMatch match;
            if(!nexusManual.TryGetValue(work.Entry.Id,out match))continue;
            if(match.Variant==null){statuses[work.Key]=GeneralStatusPresentation.NexusAmbiguous;continue;}
            string key=Key(work.Entry,match.Variant);
            string local=work.Key==key?statuses[key]:await Task.Run(()=>
                Status(new Work{Entry=work.Entry,Variant=match.Variant},root,reads,false));
            if(local==GeneralStatusPresentation.Disabled){nexusManual.Remove(work.Entry.Id);continue;}
            statuses[key]=GeneralStatusPresentation.Kind(local)=="installed"?local:GeneralStatusPresentation.NexusManual;
            if(work.Key!=key)statusDetails[work.Key]=T("Outra variante foi detectada via Nexus: ","Another variant was detected via Nexus: ")+match.Variant.Name;
        }
    }

    async void StartSelected(){
        if(projectDraft!=null&&!ConfirmProjectNavigation())return;
        if(working||scanning)return;
        try{
            if(String.IsNullOrWhiteSpace(state.Destination)){
                string selected=WindowsFolderPicker.Choose(this,T("Pasta de instalação","Installation folder"));
                if(selected==null)return;
                SetDestination(WindowsFolderPicker.InstallationPath(selected));
                Persist();
                await Scan();
                return;
            }
            List<Work> jobs;
            var selection=SelectedJobs();
            var command=ClassifyJobs(selection,out jobs);
            if(command==ModAction.None)return;
            if(command==ModAction.ChooseDependency){await EditMod(jobs[0].Entry);return;}
            if(command==ModAction.Disable||command==ModAction.Enable){
                await ToggleMods(jobs,Root(),command==ModAction.Enable);
                return;
            }
            if(jobs.Count>1&&IsCyberpunk(root:state.Destination)&&!GameEdition.HasPL(state.Destination)){
                jobs=jobs.Where(w=>w.Variant.CyberpunkId==null||!FixedData.Expand(catalog,new[]{w.Variant.CyberpunkId}).Any(GameEdition.NeedsPL)).ToList();
                if(jobs.Count==0){detail.Text=T("Os mods marcados precisam de Phantom Liberty.","The selected mods require Phantom Liberty.");return;}
            }
            if(jobs.Count==0)return;
            string root=Root();
            jobs=ChooseAlternatives(jobs,entries=>{
                using(var dialog=new GeneralModAlternativeDialog(entries))
                    return dialog.ShowDialog(this)==DialogResult.OK?dialog.SelectedId:null;
            });
            if(jobs==null||jobs.Count==0){detail.Text=T("Nenhum mod foi colocado na fila.","No mods were queued.");return;}

            if(command==ModAction.Change){
                foreach(var work in jobs)work.SwitchAlternatives=work.Entry.AlternativeGroup!=null;
                if(jobs.Any(work=>work.SwitchAlternatives&&!IsBigBody(work.Entry))&&GeneralDialog.Message(this,
                    T("Desabilitar a versão ativa depois de preparar a substituta?","Disable the active version after the replacement is ready?"),
                    T("Mudar versão","Change version"),MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            }
            jobs=WithLinkedDependencies(jobs);
            var pending=new List<Work>();
            List<string> downloads;
            scanning=true;Busy(true);next.Visible=false;
            detail.Text=T("Conferindo os mods selecionados…","Checking the selected mods…");
            try{downloads=await Task.Run(()=>PlanSelected(jobs,root,pending));}
            finally{scanning=false;Busy(false);}
            if(IsDisposed)return;
            if(pending.Count==0){
                detail.Text=T("Os mods selecionados já estão instalados.","The selected mods are already installed.");
                return;
            }
            ConfirmAndEnqueue(pending,root,downloads,false);
        }catch(Exception ex){if(!IsDisposed)ShowError(ex);}
    }

    static List<Work> ChooseAlternatives(List<Work> jobs,Func<IReadOnlyList<GeneralModEntry>,string> choose){
        var excluded=new HashSet<string>(StringComparer.Ordinal);
        foreach(var group in jobs.Where(work=>work.Entry.AlternativeGroup!=null).GroupBy(work=>work.Entry.AlternativeGroup)){
            var entries=group.Select(work=>work.Entry).ToList();
            if(entries.Count<2)continue;
            string selected=choose(entries);
            if(selected==null)return null;
            if(selected.Length!=0&&!entries.Any(entry=>entry.Id==selected))throw new IOException("Alternativa inválida.");
            foreach(var entry in entries.Where(entry=>entry.Id!=selected))excluded.Add(entry.Id);
        }
        return jobs.Where(work=>!excluded.Contains(work.Entry.Id)).ToList();
    }
    List<Work> WithLinkedDependencies(List<Work> selected){
        if(!selected.Any(work=>work.Variant.RequiredMods.Count>0))return selected;
        var jobs=selected.ToDictionary(work=>work.Entry.Id,StringComparer.Ordinal);
        var visiting=new HashSet<string>(StringComparer.Ordinal);
        var resolved=new HashSet<string>(StringComparer.Ordinal);
        var ordered=new List<Work>();
        Action<Work> visit=null;
        visit=work=>{
            if(resolved.Contains(work.Entry.Id))return;
            if(!visiting.Add(work.Entry.Id))
                throw new IOException(T("Dependências circulares em ","Circular dependencies for ")+work.Entry.Name);
            foreach(string id in work.Variant.RequiredMods){
                Work required;
                if(!jobs.TryGetValue(id,out required)){
                    var entry=state.List.Mods.FirstOrDefault(mod=>mod.Id==id);
                    if(entry==null)throw new IOException(T("Dependência ausente da lista: ","Dependency missing from the list: ")+id);
                    if(entry.AlternativeGroup!=null&&jobs.Values.Any(job=>job.Entry.Id!=id&&job.Entry.AlternativeGroup==entry.AlternativeGroup))
                        throw new IOException(T("A versão escolhida conflita com a dependência: ","The selected version conflicts with the dependency: ")+entry.Name);
                    required=MakeWork(entry);
                    required.DependencyQueue=true;
                    jobs.Add(id,required);
                }
                visit(required);
            }
            visiting.Remove(work.Entry.Id);
            resolved.Add(work.Entry.Id);
            ordered.Add(work);
        };
        foreach(var work in selected)visit(work);
        return ordered;
    }

    List<string> PlanSelected(List<Work> selected,string root,List<Work> pending){
        var reads=new ScanReads(this,root);
        var known=new Dictionary<string,FixedMod>();
        var downloads=new List<string>();
        foreach(var work in selected){
            string status=Status(work,root,reads);
            if(IsSharedBodySwitch(work,status)){pending.Add(work);continue;}
            if(status==GeneralStatusPresentation.Disabled){
                if(work.SwitchAlternatives||work.DependencyQueue)pending.Add(work);
                continue;
            }
            if(work.Variant.CyberpunkId==null){
                if(GeneralStatusPresentation.Kind(status)=="installed"||status==GeneralStatusPresentation.ManualBody||
                    status==GeneralStatusPresentation.NexusManual)continue;
                pending.Add(work);
                if(work.Variant.Sources.Select((source,index)=>GeneralModDownload.IsWebSource(source)&&!File.Exists(LocalSource(work,index))).Any(missing=>missing))
                    downloads.Add(work.Entry.Name+" — "+work.Variant.Name);
                continue;
            }
            if(SnapshotInstalled(work,root))continue;
            if(status=="Requer Cyberpunk 2077"||status=="Requer Phantom Liberty"){pending.Add(work);continue;}

            var recipe=Recipe(work);
            var unresolved=new List<FixedMod>();
            bool blocked=false;
            foreach(var mod in ExpandWork(work,root)){
                if (mod.Id == recipe.Id && EquipmentExOptions.Available(recipe)) {
                    if (status == "Instalado") continue;
                    if (!EquipmentExOptions.Read(recipe, root, reads.Hash).CanApply) { blocked = true; break; }
                    unresolved.Add(ProfileRecipe(work));
                    continue;
                }
                if(mod.Id==recipe.Id&&work.Variant.CyberpunkVariant!=null){
                    if(GeneralStatusPresentation.Kind(status)=="installed")continue;
                    if(!NightCityDensity.Read(recipe,root).CanApply){blocked=true;break;}
                    unresolved.Add(NightCityDensity.Resolve(recipe,work.Variant.CyberpunkVariant));
                    continue;
                }
                var scan=reads.Fixed(mod);
                if(scan.IsCorrect)continue;
                if(scan.IsConflict||!String.IsNullOrEmpty(mod.BlockedReason)||GameEdition.NeedsPL(mod)&&!reads.PhantomLiberty){blocked=true;break;}
                unresolved.Add(mod);
            }
            if(!blocked&&unresolved.Count==0)continue;
            pending.Add(work);
            if(!blocked)foreach(var mod in unresolved)
                known[mod.Id+"/"+String.Join("/",mod.Assets.Select(asset=>asset.Sha256))]=mod;
        }
        var missingAssets=MissingArchiveHashes(known.Values.SelectMany(mod=>mod.Assets));
        downloads.AddRange(known.Values.Where(mod=>mod.Assets.Any(asset=>missingAssets.Contains(asset.Sha256))).Select(mod=>mod.Name));
        return downloads;
    }

    HashSet<string> MissingArchiveHashes(IEnumerable<FixedAsset> assets){
        var missing=assets.GroupBy(asset=>asset.Sha256).Select(group=>group.First())
            .Where(asset=>!cache.Contains(asset)).ToLookup(asset=>asset.Size);
        var hashes=new HashSet<string>(missing.SelectMany(group=>group).Select(asset=>asset.Sha256),StringComparer.Ordinal);
        if(hashes.Count==0||!Directory.Exists(state.DownloadFolder))return hashes;
        Safe.NoLinks(state.DownloadFolder);
        foreach(string file in Directory.EnumerateFiles(state.DownloadFolder).Take(10000)){
            if(!GeneralModDownload.IsArchivePath(file))continue;
            try{
                var info=new FileInfo(file);
                if(!missing[info.Length].Any(asset=>hashes.Contains(asset.Sha256)))continue;
                Safe.NoLinks(file);
                using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.None))
                using(var sha=System.Security.Cryptography.SHA256.Create())
                    hashes.Remove(Safe.Hex(sha.ComputeHash(stream)));
            }catch(IOException){}catch(UnauthorizedAccessException){}
            if(hashes.Count==0)break;
        }
        return hashes;
    }

    bool SnapshotInstalled(Work work,string root){
        string receipt;
        return work.Variant.CyberpunkId!="NightCoop"&&work.Variant.CyberpunkId!=EquipmentExOptions.ModId&&TryWorkSnapshot(work,root,out receipt)&&
            Snapshot(root).Status(receipt,InstalledSnapshot.Owner(work.Entry,work.Variant))==GeneralModInstaller.Installed;
    }

    bool TryWorkSnapshot(Work work,string root,out string receipt){
        return state.SnapshotReceipts.TryGetValue(InstalledSnapshot.Owner(work.Entry,work.Variant),out receipt)&&
            SnapshotMatchesBody(work,root,receipt);
    }

    bool SnapshotMatchesBody(Work work,string root,string receipt){
        return InstalledSnapshot.MatchesBodyReceipt(Snapshot(root),work.Entry,work.Variant,receipt);
    }
    void ConfirmAndEnqueue(List<Work> jobs,string root,IReadOnlyList<string> downloads,bool urgent){
        if(!urgent){
            using(var dialog=new GeneralInstallConfirmation(root,jobs.Count,downloads)){
                if(dialog.ShowDialog(this)!=DialogResult.OK){
                    detail.Text=T("Instalação cancelada.","Installation canceled.");
                    return;
                }
            }
        }
        if(IsCyberpunk(root)&&jobs.Any(work=>GeneralModLibrary.IsHystBody(work.Entry.Id)))
            jobs.RemoveAll(work=>work.Variant.CyberpunkId==GeneralModLibrary.VanillaFeetId);
        if(jobs.Any(work=>work.Entry.Id=="hyst-ebb-rb"))
            jobs=jobs.OrderBy(work=>work.Variant.CyberpunkId==GeneralModLibrary.EclipseTattooId).ToList();
        if(jobs.Any(work=>work.Variant.CompatibilityGroups.Count>0||work.Variant.RequiredMods.Count>0)){
            var ordered=new List<Work>();
            while(jobs.Count>0){
                int index=jobs.FindIndex(work=>!jobs.Any(other=>other!=work&&(
                    work.Variant.RequiredMods.Contains(other.Entry.Id)||work.Variant.CompatibilityGroups.Any(group=>
                        other.Entry.CompatibilityGroups.Contains(group,StringComparer.OrdinalIgnoreCase)))));
                if(index<0)index=0;
                ordered.Add(jobs[index]);jobs.RemoveAt(index);
            }
            jobs=ordered;
        }
        Enqueue(jobs,urgent);
    }

    void ConfirmUrgentInstall(GeneralModEntry entry,string bodyChoice=null){
        if(GeneralModLibrary.NeedsDependencyVersion(entry,Selected(entry)))return;
        if(entry.AlternativeGroup!=null&&normal.Concat(priority).Concat(new[]{current,priorityCurrent}).Any(work=>
            work!=null&&work.Entry.Id!=entry.Id&&work.Entry.AlternativeGroup==entry.AlternativeGroup)){
            detail.Text=T("Outra alternativa deste grupo já está na fila.","Another alternative from this group is already queued.");
            return;
        }
        string root=Root();
        var requested=IsBigBody(entry)?BigBodyVersions(new[]{entry},GameEdition.HasPL(root))
            .First(work=>work.BodyChoice==(bodyChoice??ActiveBodyChoice(entry))):MakeWork(entry);
        ConfirmAndEnqueue(WithLinkedDependencies(new List<Work>{requested}),root,new string[0],true);
    }

    async void Enqueue(IEnumerable<Work> jobs,bool urgent){
        if(scanning)return;
        string root;try{root=Root();}catch(Exception ex){detail.Text=ex.Message;return;}
        if(!working){operationTotal=0;operationFinished=0;skipRequests.Clear();suppressQueueErrors=false;}
        foreach(var work in jobs){
            if((current!=null&&current.Entry.Id==work.Entry.Id)||(priorityCurrent!=null&&priorityCurrent.Entry.Id==work.Entry.Id)||priority.Any(w=>w.Entry.Id==work.Entry.Id))continue;
            var existing=normal.FirstOrDefault(w=>w.Entry.Id==work.Entry.Id);
            if(existing!=null){if(!urgent)continue;normal.Remove(existing);}else operationTotal++;
            (urgent?priority:normal).Add(work);RowStatus(work,"Na fila");
        }
        if(working)return;
        working=true;Busy(true);
        try{
            while(priority.Count+normal.Count>0){
                var list=priority.Count>0?priority:normal;
                current=list[0];list.RemoveAt(0);
                Busy(true);
                await RunWork(current,root,false);
                current=null;
            }
        }finally{working=false;Busy(false);SetWait(false,false);}
    }
    async Task RunWork(Work work,string root,bool interrupt){
        var suspended=new List<Work>();
        bool incomingStarted=false, completed=false;
        try{
            skipRequests.Remove(work);
            if(GeneralModLibrary.NeedsDependencyVersion(work.Entry,work.Variant)){
                RowStatus(work,GeneralStatusPresentation.ChooseDependency);
                return;
            }
            RowStatus(work,"Verificando",null,work.Entry.Name+T(": verificando arquivos…",": checking files…"));
            OnlineDiagnostics.Record("general.install.begin",work.Entry.Id,"Iniciando item da fila.",new{Variant=work.Variant.Id});
            if(work.Variant.InstallRoutes.Count>0){

                try{GeneralInstallRoutes.CheckFolders(root,work.Variant.InstallRoutes);}
                catch(DirectoryNotFoundException ex){RowStatus(work,"Pulado",null,work.Entry.Name+": "+ex.Message);return;}
            }
            if(SnapshotInstalled(work,root)){RememberInstalledVariant(work);RowStatus(work,"Instalado",100);return;}
            if(work.SaveVariant&&work.BodyChoice!=null&&await Task.Run(()=>IsSharedBodySwitch(work,Status(work,root)))){
                RememberInstalledVariant(work);RowStatus(work,"Instalado",100);return;
            }
            if(work.Variant.InstallRoutes.Count==0&&await Task.Run(()=>MatchesNexusInstallation(work,root))){
                RowStatus(work,GeneralStatusPresentation.NexusManual);
                return;
            }
            var conflicts=await Task.Run(()=>ConflictingAlternatives(work,root));
            if(conflicts.Count>0&&!work.SwitchAlternatives&&
                (work.DropConflicts==null||!work.DropConflicts.IsSupersetOf(conflicts.Select(old=>old.Key))))
                throw new IOException(T("Use Mudar versão para trocar: ","Use Change version to switch: ")+conflicts[0].Entry.Name);
            if(work.Variant.CompatibilityGroups.Count>0||work.Variant.RequiredMods.Count>0)
                await Task.Run(()=>CheckCompatibilityGroups(work,root,conflicts));
            var store=DisabledAt(root);
            if(conflicts.Any(old=>GeneralModLibrary.IsHystBody(old.Entry.Id)))
                await Task.Run(()=>PlanDisable(conflicts,root,work.Entry.Id));
            List<DisabledModFile> cachedFiles;
            bool cached=(await Task.Run(()=>store.Read())).TryGetValue(work.Key,out cachedFiles);
            bool restoredIncoming=false;
            if(!cached&&work.Variant.Sources.Any(GeneralModDownload.IsInstalledSource))
                throw new IOException(T("Este mod veio de um pacote instalado. Para reparar, receba um novo pacote do remetente.","This mod came from an installed pack. Receive a new pack from the sender to repair it."));
            Func<Task> change=async()=>{
                if(conflicts.Count>0){
                    CheckSkip(work);
                    var plan=await Task.Run(()=>PlanDisable(conflicts,root,work.Entry.Id));
                    await Task.Run(()=>store.Disable(plan,compress:true));
                    suspended.AddRange(conflicts);
                }
                CheckHystBody(work,root);
            };
            if(conflicts.Count==0&&!cached)CheckHystBody(work,root);
            if(IsCyberpunk(root)&&HasManualHystBody(work,root)&&
                await Task.Run(()=>Status(work,root))==GeneralStatusPresentation.ManualBody){
                RowStatus(work,GeneralStatusPresentation.ManualBody);
                return;
            }
            Exception switchFailure=null;
            try {
                if(cached){
                    await change();
                    await Task.Run(()=>store.Enable(new[]{work.Key}));
                    restoredIncoming=true;
                    incomingStarted=true;
                }else if(work.Variant.CyberpunkId!=null)
                    await InstallKnown(work,root,async()=>{await change();incomingStarted=true;});
                else{
                    string[] prepared=conflicts.Count>0?await PrepareChangeFiles(work,root):null;
                    await change();
                    incomingStarted=true;
                    await InstallGeneral(work,root,prepared);
                }
                var final=await Task.Run(()=>Status(work,root));
                if(!InstalledVersion(final))
                    throw new IOException(T("A conferência final não confirmou esta variante: ","Final verification did not confirm this variant: ")+GeneralStyle.Status(final));
            }catch(Exception ex){switchFailure=ex;}
            if(switchFailure!=null){
                if(suspended.Count>0){
                    if(restoredIncoming){
                        await Task.Run(()=>store.Disable(new Dictionary<string,List<DisabledModFile>>{{work.Key,cachedFiles}},compress:true));
                    }else if(incomingStarted&&work.Applied){
                        await Task.Run(()=>RemoveManagedWorks(new List<Work>{work},root,new HashSet<string>()));
                        Persist();
                    }
                    await Task.Run(()=>store.Enable(suspended.Select(old=>old.Key)));
                    foreach(var old in suspended)RowStatus(old,"Instalado",100);
                    suspended.Clear();
                }
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(switchFailure).Throw();
            }
            if((work.DependencyQueue||work.Entry.DependencySource!=null)&&work.Entry.Tags.Count<32&&
                !work.Entry.Tags.Contains("Instalado pela fila",StringComparer.OrdinalIgnoreCase)){
                work.Entry.Tags.Add("Instalado pela fila");
                Persist();
            }
            foreach(var old in suspended.Where(old=>IsBigBody(old.Entry)&&old.Entry.Id!=work.Entry.Id))
                state.ChosenVariants[old.Entry.Id]=old.Variant.Id;
            RememberInstalledVariant(work);
            completed=true;
            RowStatus(work,"Instalado",100,work.Entry.Name+T(": instalado e conferido.",": installed and verified."));
            OnlineDiagnostics.Record("general.install.complete",work.Entry.Id,"Instalação conferida.",new{Variant=work.Variant.Id});
        }catch(ModVersionKeptException ex){RowStatus(work,"Versão mantida",null,ex.Message);OnlineDiagnostics.Record("general.version.kept",work.Entry.Id,ex.Message);}
        catch(OperationCanceledException){RowStatus(work,work.Applied?"Parcial/pulado":"Pulado",null,work.Entry.Name+T(": pulado. A fila continua.",": skipped. The queue continues."));}
        catch(Exception ex){
            string message=work.Entry.Name+": "+ex.Message;
            RowStatus(work,"Falhou",null,message);
            OnlineDiagnostics.Record("general.install.failed",work.Entry.Id,"Falha no item; a fila continua.",new{Variant=work.Variant.Id,ErrorType=ex.GetType().Name,Message=ex.Message,Exception=ex.ToString(),AppVersion=GeneralUpdateChecker.CurrentVersion,Catalog=OnlineIdentity.CatalogHash});
            if(!interrupt&&!suppressQueueErrors){
                if(testMode){if(testFailurePrompt!=null)suppressQueueErrors=testFailurePrompt(message);}
                else GeneralDialog.Message(this,message,T("Falha na instalação","Installation failed"),MessageBoxButtons.OK,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button1,value=>suppressQueueErrors=value,T("Ocultar os próximos avisos desta instalação","Hide further warnings for this installation"));
            }
        }
        finally{
            foreach(var old in suspended){
                if(completed)RowStatus(old,GeneralStatusPresentation.Disabled);
                else {
                    string status=GeneralStatusPresentation.Disabled;
                    try { status=Status(old,root); } catch(IOException) { }
                    RowStatus(old,status);
                }
            }
            RefreshStorageSize();work.Ended=true;operationFinished++;UpdateFlow(null,"");skipRequests.Remove(work);Stage(work,"work.end");
        }
    }
    bool IsSharedBodySwitch(Work work,string status){
        if(work.BodyChoice==null||work.Entry.Id!=GeneralModLibrary.EbbBody||!InstalledVersion(status)||
            work.BodyChoice==ActiveBodyChoice(work.Entry))return false;
        var regular=work.Entry.Variants.Single(variant=>variant.Id==work.Variant.Id);
        return regular.NaturalOption==null||GeneralModLibrary.SamePayload(regular,regular.NaturalOption);
    }

    void RememberInstalledVariant(Work work){
        if(work.DropConflicts==null&&!work.SaveVariant)return;
        if(work.Variant.Id==work.Entry.DefaultVariant&&!GeneralModLibrary.IsHystBody(work.Entry.Id))state.ChosenVariants.Remove(work.Entry.Id);
        else state.ChosenVariants[work.Entry.Id]=work.Variant.Id;
        if(work.BodyChoice!=null)state.BodyChoice=work.BodyChoice;
        if(pendingVersion!=null&&SameVersion(work,pendingVersion))pendingVersion=null;
        Persist();DrawRows();
    }

    void CheckCompatibilityGroups(Work work,string root,List<Work> conflicts,IReadOnlyList<Work> enabling=null){
        if(work.Variant.CompatibilityGroups.Count==0&&work.Variant.RequiredMods.Count==0)return;
        var reads=new ScanReads(this,root);
        Func<GeneralModEntry,bool> active=mod=>!conflicts.Any(other=>other.Entry.Id==mod.Id)&&mod.Variants.Any(variant=>{
            string status=Status(new Work{Entry=mod,Variant=variant},root,reads);
            return GeneralStatusPresentation.Kind(status)=="installed"||status==GeneralStatusPresentation.ManualBody;
        });
        foreach(string id in work.Variant.RequiredMods){
            if(enabling!=null&&enabling.Any(other=>other.Entry.Id==id))continue;
            var required=state.List.Mods.FirstOrDefault(mod=>mod.Id==id);
            if(required==null||!active(required))
                throw new IOException(T("Instale ou ative primeiro a dependência: ","Install or enable the required mod first: ")+(required==null?id:required.Name));
        }
        foreach(string group in work.Variant.CompatibilityGroups){
            if(enabling!=null&&enabling.Any(other=>other!=work&&other.Entry.CompatibilityGroups.Contains(group,StringComparer.OrdinalIgnoreCase)))continue;
            bool matched=state.List.Mods.Where(mod=>mod.Id!=work.Entry.Id&&mod.CompatibilityGroups.Contains(group,StringComparer.OrdinalIgnoreCase)).Any(active);
            if(!matched)throw new IOException(T("A variante precisa de um mod ativo do grupo ","This version requires an active mod in group ")+group+".");
        }
    }

    static bool HasManualHystBody(Work work,string root){
        if(work.Variant.Id!="default"||!GeneralModLibrary.IsHystBody(work.Entry.Id))return false;
        bool pushUp=work.Entry.Id=="hyst-ebbp-rb";
        const string mods="archive/pc/mod/";
        string marker=Safe.In(root,mods+(pushUp?"EBBPRB.xl":"EBBRB.xl"));
        string archive=Safe.In(root,mods+(pushUp?"05_Hyst_Refit_ForcedBigBoobaPUSHUP_Booty.archive":"05_Hyst_Refit_ForcedBigBreastsBooty.archive"));
        string other=Safe.In(root,mods+(pushUp?"EBBRB.xl":"EBBPRB.xl"));
        if(!File.Exists(marker)||!File.Exists(archive)||File.Exists(other))return false;
        Safe.NoLinks(marker);Safe.NoLinks(archive);
        return new FileInfo(marker).Length>0&&new FileInfo(archive).Length>0;
    }
    static void CheckHystBody(Work work,string root){
        bool ebb=work.Entry.Id=="hyst-ebb-rb",pushUp=work.Entry.Id=="hyst-ebbp-rb";
        bool vanillaFeet=work.Variant.CyberpunkId==GeneralModLibrary.VanillaFeetId;
        bool eclipse=work.Variant.CyberpunkId==GeneralModLibrary.EclipseTattooId;
        if((!ebb&&!pushUp&&!vanillaFeet&&!eclipse)||!IsCyberpunk(root))return;
        const string baseBody="archive/pc/mod/EBBRB.xl",pushBody="archive/pc/mod/EBBPRB.xl";
        if(eclipse){
            if(!File.Exists(Safe.In(root,baseBody))||File.Exists(Safe.In(root,pushBody)))
                throw new IOException(T("Instale primeiro o corpo Hyst EBB + RB sem Push-Up para usar a tatuagem Eclipse.",
                    "Install the Hyst EBB + RB body without Push-Up before installing the Eclipse tattoo."));
            return;
        }
        const string feet="archive/pc/mod/Vanilla_ArchiveXL_FeetComponentsFV.archive";
        var paths=vanillaFeet?new[]{baseBody,pushBody}:new[]{ebb?pushBody:baseBody,feet};
        if(paths.Any(path=>File.Exists(Safe.In(root,path))))
            throw new IOException(T("Outra versão de corpo ou o Togglable Feet do corpo padrão está ativo. Remova o conflito antes de instalar.",
                "Another body version or vanilla Togglable Feet is active. Remove the conflict before installing."));
        if(!vanillaFeet&&work.Variant.Id=="with-refit-dlc"&&!GameEdition.HasPL(root))
            throw new IOException(T("Esta variante de roupa requer Phantom Liberty.","This clothing variant requires Phantom Liberty."));
    }
    Func<ModVersionDifference,bool> testVersionChoice;
    bool ChooseVersion(ModVersionDifference difference){return testMode?(testVersionChoice!=null&&testVersionChoice(difference)):GeneralVersionChoiceDialog.Ask(this,difference);}
    async Task InstallKnown(Work work,string root,Func<Task> beforeInstall=null){
        if(!IsCyberpunk(root))throw new IOException(T("Selecione a pasta do Cyberpunk 2077 para usar este mod do pacote.","Select the Cyberpunk 2077 folder to use this pack item."));
        EnsureGameClosed(root);
        var recipe=Recipe(work);
        var variants=ExpandWork(work,root);
        variants[variants.Count-1]=ProfileRecipe(work);
        FixedData.RequireInstallable(variants);FixedData.ValidateGame(root,variants,catalog.GameFileVersion);
        var routes=work.Variant.InstallRoutes;
        var mapped=routes.Count==0?null:GeneralInstallRoutes.FixedPaths(catalog,recipe,routes);
        var pending=new List<FixedMod>();
        foreach(var mod in variants){
            CheckSkip(work);
            if(mapped!=null&&mod.Id==recipe.Id){
                string routedStatus=await Task.Run(()=>RoutedFixedStatus(work,root,null));
                if(routedStatus=="Instalado")continue;
                if(routedStatus==GeneralModInstaller.Modified)
                    throw new IOException(T("Há arquivos diferentes no destino escolhido.","There are modified files in the selected destination."));
                pending.Add(mod);
                continue;
            }
            if (mod.Id == recipe.Id && EquipmentExOptions.Available(recipe)) {
                var installed = await Task.Run(() => EquipmentExOptions.Read(recipe, root));
                if (!installed.CanApply) throw new IOException(installed.Detail);
                if (installed.Id == work.Variant.Id && !installed.Partial) continue;
                if (installed.Id != "absent" && installed.Id != work.Variant.Id && installed.Id != work.DropDensity)
                    new ModVersionDifference {
                        Name = recipe.Name, Current = installed.Name, Incoming = mod.Name,
                        Detail = T("A troca guarda os arquivos anteriores para desfazer.", "Switching backs up the previous files for undo.")
                    }.Require(ChooseVersion);
                pending.Add(mod);
                continue;
            }
            if(mod.Id==recipe.Id&&work.Variant.CyberpunkVariant!=null){
                var density=await Task.Run(()=>NightCityDensity.Read(recipe,root));
                if(!density.CanApply)throw new IOException(density.Detail);
                if(density.Id==work.Variant.CyberpunkVariant||density.Id=="legacy-low"&&work.Variant.CyberpunkVariant=="low")continue;
                if(density.Id!=null&&density.Id!="absent"&&density.Id!=work.DropDensity)new ModVersionDifference{Name=mod.Name,Current=density.Name,Incoming=work.Variant.Name,Detail=T("A variante instalada será substituída pela selecionada.","The installed variant will be replaced with the selected one.")}.Require(ChooseVersion);
            }else{
                var scan=await Task.Run(()=>FixedData.Scan(mod,root));
                if(scan.IsCorrect)continue;
                if(scan.Original>0||scan.IsConflict){
                    var file=mod.Files.FirstOrDefault(f=>!f.UserData&&File.Exists(Safe.In(root,f.Path))&&Safe.Hash(Safe.In(root,f.Path))!=f.Sha256);
                    var difference=new ModVersionDifference{Name=mod.Name,Current=mod.Id=="NightCoop"&&scan.State=="Atualização disponível"?"0.1.2-beta":file!=null&&FpvCustomization.IsWeaveUpgrade(file)&&scan.Original>0?"Equipment-EX 1.2.10":file==null?scan.State:ModVersionDifference.FileVersion(Safe.In(root,file.Path)),Incoming=mod.Version,CanReplace=!scan.IsConflict,Detail=scan.IsConflict?T("Há conteúdo não reconhecido ou incompatível. Resolva o conflito antes de substituir; a base protegida permanece intacta.","Unrecognized or incompatible content was found. Resolve the conflict before replacing; the protected base remains intact."):T("Versão anterior reconhecida. A troca usa o backup e a conferência de arquivos do instalador.","Recognized previous version. Replacement uses the installer's backup and file verification.")};
                    difference.Require(ChooseVersion);
                }
            }
            pending.Add(mod);
        }
        if(pending.Count==0)return;
        foreach(var mod in pending)foreach(var asset in mod.Assets){
            CheckSkip(work);
            if(await Task.Run(()=>cache.Contains(asset)))continue;
            if(asset.ModId==0){
                if(asset.Url==NightCoopRelease.Url&&await Task.Run(()=>ImportExistingNightCoop(asset)))continue;
                if(testMode)throw new IOException("Download de rede bloqueado no teste.");
                RowStatus(work,"Baixando",null,"Baixando "+asset.Name+"…");
                using(var control=new DownloadControl()){
                    var transfer=Task.Run(()=>new NexusClient().Download(mod,asset,cache,control,p=>{
                        if(!IsDisposed&&IsHandleCreated)BeginInvoke((Action)(()=>RowStatus(work,"Baixando",Math.Min(99,p.Percent),asset.Name)));
                    }));
                    while(!transfer.IsCompleted){if(skipRequests.Contains(work))control.Cancel();await Task.WhenAny(transfer,Task.Delay(150));}
                    await transfer;
                }
            }else await AwaitKnown(work,asset,root);
        }
        CheckSkip(work);
        if(beforeInstall!=null)await beforeInstall();
        RowStatus(work,"Instalando",null,work.Entry.Name+T(": preparando e instalando…",": preparing and installing…"));
        Action install=()=>{
            var engine=Cyberpunk(root);
            if(mapped!=null){
                if(recipe.Dependencies.Length>0)engine.Install(recipe.Dependencies,true,Report);
                if(pending.Any(mod=>mod.Id==recipe.Id))
                    using(engine.LockForRouteChange())Generic(root).InstallRoutedFixed(work.Receipt(0),recipe,mapped,routes,cache,Report);
                return;
            }
            if (EquipmentExOptions.Available(recipe)) {
                var target = ProfileRecipe(work);
                if (target.Dependencies.Length > 0) engine.Install(target.Dependencies, true, Report);
                if (pending.Any(mod => mod.Id == recipe.Id)) engine.ApplyEquipmentExProfile(work.Variant.Id, Report);
                return;
            }
            if(work.Variant.CyberpunkVariant==null){engine.Install(new[]{recipe.Id},true,Report);return;}
            if(recipe.Dependencies.Length>0)engine.Install(recipe.Dependencies,true,Report);
            if(pending.Any(mod=>mod.Id==recipe.Id))engine.ApplyDensityProfile(work.Variant.CyberpunkVariant,Report);
        };
        var approvals=new HashSet<string>();
        while(true){
            try{Stage(work,"install.begin");await Task.Run(install);work.Applied=true;Stage(work,"install.end");break;}
            catch(ModPreparationException ex){
                var request=ex.InnerException as FileApprovalRequest;
                if(request==null||testMode||!approvals.Add(request.ModId+"/"+request.Entry))throw;
                string path=Safe.In(root,request.Path);Safe.NoLinks(path);
                if(File.Exists(path)||Directory.Exists(path)||GeneralDialog.Message(this,ex.Message+"\n\nO arquivo difere do catálogo e pode causar falhas. Instalar mesmo assim?","Arquivo diferente",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)throw;
                UserFileApprovals.Approve(catalog,root,request);
            }
        }
    }
    bool ImportExistingNightCoop(FixedAsset asset){
        if(String.IsNullOrEmpty(state.DownloadFolder)||!Directory.Exists(state.DownloadFolder))return false;
        Safe.NoLinks(state.DownloadFolder);
        string pattern=Path.GetFileNameWithoutExtension(asset.Name)+"*.zip";
        foreach(string path in Directory.EnumerateFiles(state.DownloadFolder,pattern,SearchOption.TopDirectoryOnly)){
            Safe.NoLinks(path);
            if(new FileInfo(path).Length!=asset.Size||Safe.Hash(path)!=asset.Sha256)continue;
            cache.Import(path,asset);
            return true;
        }
        return false;
    }
    async Task AwaitKnown(Work work,FixedAsset asset,string root){
        using(var browser=BrowserDownloadInbox.Listen(BrowserDownloadChannel))
        using(var watch=new GeneralModDownload(state.DownloadFolder,asset)){
            bool opened=false,checkRecent=!testMode;
            association=null;pendingFixedAsset=asset;SetWait(true,true);
            try{
                while(true){
                    CheckSkip(work);
                    while(editingAttachment)await Task.Delay(100);
                    await DrainLocalPriority(root);
                    if(association!=null){
                        string selected=association;association=null;
                        try{await Task.Run(()=>cache.Import(selected,asset));return;}
                        catch(IOException ex){if(!testMode)GeneralDialog.Message(this,ex.Message,T("Arquivo diferente","Different file"),MessageBoxButtons.OK,MessageBoxIcon.Warning);}
                    }
                    var file=await Task.Run(()=>(browser==null?null:browser.TakeMatch(null,null,asset))??
                        (checkRecent?BrowserDownloadBridge.FindRecent(null,null,asset):null)??watch.Poll());
                    checkRecent=false;
                    while(editingAttachment)await Task.Delay(100);
                    CheckSkip(work);
                    if(file!=null){await Task.Run(()=>cache.Import(file,asset));return;}
                    var snapshot=await Task.Run(()=>watch.Progress());
                    if(!opened&&!watch.HasPendingCandidates&&!snapshot.Active){
                        CheckSkip(work);
                        launch("https://www.nexusmods.com/cyberpunk2077/mods/"+asset.ModId+"?tab=files&file_id="+asset.FileId);opened=true;
                    }
                    RowStatus(work,snapshot.Active?"Baixando":watch.HasPendingCandidates?"Conferindo download":"Aguardando download",
                        snapshot.Percent.HasValue?(int?)Math.Min(snapshot.Percent.Value,99):null,
                        asset.Name+T(" — clique em Manual / Slow Download. O app confere a pasta e continua sozinho."," — click Manual / Slow Download. The app checks the folder and continues automatically."));
                    if(snapshot.Estimated&&snapshot.Active){currentProgress.Caption+=T(" · estimado"," · estimated");currentProgress.Invalidate();}
                    await Task.Delay(600);
                }
            }finally{SetWait(false,false);association=null;pendingFixedAsset=null;}
        }
    }
    async Task<string[]> PrepareChangeFiles(Work work,string root){
        var paths=new string[work.Variant.Sources.Count];
        for(int index=0;index<paths.Length;index++){
            CheckSkip(work);
            string source=work.Variant.Sources[index];
            string path=LocalSource(work,index);
            if(GeneralModDownload.IsWebSource(source)&&!File.Exists(path))path=await AwaitAttachment(work,source,root);
            if(!File.Exists(path))throw new IOException(T("Arquivo da nova versão não encontrado.","New version file not found."));
            Safe.NoLinks(path);
            paths[index]=path;
        }
        return paths;
    }

    async Task InstallGeneral(Work work,string root,string[] prepared=null){
        EnsureGameClosed(root);
        bool cyberpunk=IsCyberpunk(root);
        var engine=cyberpunk?null:Generic(root);
        if(cyberpunk)FixedData.ValidateGame(root,new FixedMod[0],catalog.GameFileVersion);
        for(int i=0;i<work.Variant.Sources.Count;i++){
            CheckSkip(work);
            string source=work.Variant.Sources[i],receipt=work.Receipt(i);
            string candidate=LocalSource(work,i);
            if(engine!=null&&await Task.Run(()=>engine.Status(receipt,null,Safe.Hash,work.Variant.InstallRoutes))==GeneralModInstaller.Installed&&(candidate==null||!File.Exists(candidate)||await Task.Run(()=>engine.SourceMatches(candidate,receipt))))continue;
            if(cyberpunk){
                int index=i;
                if(await Task.Run(()=>Cyberpunk(root).InstalledImports().Contains(CyberpunkImportId(work,index))))continue;
            }
            bool web=GeneralModDownload.IsWebSource(source);
            string actual=prepared==null?LocalSource(work,i):prepared[i];
            if(web&&(actual==null||!File.Exists(actual)))actual=await AwaitAttachment(work,source,root);
            source=actual;
            if(!File.Exists(source))throw new IOException(T("Arquivo não encontrado: ","File not found: ")+source+T(". Dê dois cliques no mod para corrigir a fonte.",". Double-click the mod to update its source."));
            CheckSkip(work);
            RowStatus(work,"Instalando",(int)(i*99.0/work.Variant.Sources.Count),work.Entry.Name+": "+Path.GetFileName(source));
            string installSource=source;
            string localId=null;
            Stage(work,"install.begin");
            await Task.Run(()=>{
                EnsureGameClosed(root);
                if(cyberpunk){var item=ImportItem(installSource);Cyberpunk(root).InstallImported(item,Report,ChooseVersion,work.Variant.InstallRoutes);localId=LocalModPackage.RoutedId(item.Sha256,work.Variant.InstallRoutes);}
                else engine.Install(installSource,receipt,Report,difference=>{difference.Name=work.Entry.Name;return ChooseVersion(difference);},work.Variant.InstallRoutes);
            });
            work.Applied=true;Stage(work,"install.end");
            if(web)state.ResolvedDownloads[receipt]=source;
            if(localId!=null)state.CyberpunkImports[receipt]=localId;
            if(web||localId!=null)Persist();
        }
    }
    async Task<string> AwaitAttachment(Work work,string source,string root){
        if(work==priorityCurrent)
            throw new IOException(T("O arquivo importado não está mais disponível.","The imported file is no longer available."));
        var browser=BrowserDownloadInbox.Listen(GeneralNexusLink.Parse(source)==null?null:BrowserDownloadChannel);
        association=null;pendingWebSource=source;
        work.Variant.SourceInfo.TryGetValue(source,out pendingWebInfo);
        SetWait(true,true);
        try{
            bool opened=false;
            while(association==null){
                CheckSkip(work);
                if(editingAttachment){await Task.Delay(100);continue;}
                await DrainLocalPriority(root);
                var info=pendingWebInfo;
                var path=await Task.Run(()=>(browser==null?null:browser.TakeMatch(source,info,null))??
                    (!opened&&!testMode?BrowserDownloadBridge.FindRecent(source,info,null):null)??
                    GeneralModDownload.FindCompleted(state.DownloadFolder,source,info));
                while(editingAttachment)await Task.Delay(100);
                CheckSkip(work);
                if(association!=null)return association;
                if(path!=null)return path;
                if(!opened&&!editingAttachment&&association==null){
                    RowStatus(work,"Aguardando arquivo",null,work.Entry.Name+T(": conclua o download no navegador ou arraste o arquivo pronto aqui.",
                        ": finish the download in your browser or drop the completed file here."));
                    launch(source);opened=true;
                }
                if(association==null)await Task.Delay(600);
            }
            CheckSkip(work);
            return association;
        }finally{
            if(browser!=null)browser.Dispose();
            SetWait(false,false);association=null;pendingWebSource=null;pendingWebInfo=null;
        }
    }
    async Task DrainLocalPriority(string root){
        if(current!=null)CheckSkip(current);
        while(priority.Count>0&&(priority[0].Local||CatalogDropHasLocalSources(priority[0]))){
            priorityCurrent=priority[0];priority.RemoveAt(0);
            bool couldAttach=NeedUnknownFile;NeedUnknownFile=false;
            try{await RunWork(priorityCurrent,root,true);}
            finally{priorityCurrent=null;NeedUnknownFile=couldAttach;if(current!=null)UpdateFlow(current,statuses.ContainsKey(current.Key)?statuses[current.Key]:"Aguardando download");}
        }
    }
    void SetWait(bool value,bool needsFile){waiting=value;NeedUnknownFile=value&&needsFile;next.Visible=working||scanning;next.Enabled=working||scanning;}
    void Report(string message){
        if(!IsDisposed&&IsHandleCreated)BeginInvoke((Action)(()=>{
            if(state.Language=="en"){
                foreach(var prefix in new[]{new[]{"Preparando ","Preparing "},new[]{"Conferindo ","Checking "},new[]{"Instalado: ","Installed: "},new[]{"Retirado: ","Removed: "},new[]{"Importando ","Importing "}})
                    if(message.StartsWith(prefix[0],StringComparison.Ordinal)){message=prefix[1]+message.Substring(prefix[0].Length);break;}
            }
            detail.Text=message;
        }));
    }
    void EnsureGameClosed(string root){
        if(testMode||!IsCyberpunk(root))return;
        var processes=Process.GetProcessesByName("Cyberpunk2077");
        foreach(var process in processes)process.Dispose();
        if(processes.Length!=0)throw new IOException("Feche o Cyberpunk 2077 antes de instalar ou remover mods.");
    }
    IEnumerable<string> UninstallSelection(){
        foreach(var row in ModRows()){
            string id=(string)row.Tag;
            if(!row.Visible||!marked.Contains(id))continue;
            string status=row.Cells["Status"].Tag as string;
            if(GeneralStatusPresentation.Kind(status)=="installed"||status==GeneralStatusPresentation.Disabled)yield return id;
        }
    }
    async Task Uninstall(){
        if(projectDraft!=null&&!ConfirmProjectNavigation())return;
        if(working||scanning)return;
        try{
            var checkedIds=new HashSet<string>(UninstallSelection(),StringComparer.Ordinal);
            var jobs=state.List.Mods.Where(mod=>checkedIds.Contains(mod.Id)).Select(MakeWork).ToList();
            if(jobs.Count==0)return;
            string root=Root();
            if(GeneralDialog.Message(this,T("Desinstalar os mods selecionados e apagar suas cópias desabilitadas?","Uninstall the selected mods and delete their disabled copies?"),
                T("Confirmar desinstalação","Confirm uninstall"),MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            working=true;Busy(true);
            try{
                string result=await Task.Run(()=>{
                    var store=DisabledAt(root);
                    var disabled=new HashSet<string>(store.Read().Keys,StringComparer.Ordinal);
                    string notes=RemoveManagedWorks(jobs,root,disabled);
                    store.Discard(jobs.Select(work=>work.Key));
                    return notes;
                });
                Persist();                detail.Text=result;
            }finally{working=false;Busy(false);}
            await Scan();
        }catch(Exception ex){Guard(()=>{throw new IOException(ex.Message,ex);});}
    }
    async Task CyberpunkMaintenance(bool clean){
        if(working||scanning)return;
        string root=state.Destination;
        if(!IsCyberpunk(root))return;
        working=true;Busy(true);
        bool changed=false;
        try{
            var engine=Cyberpunk(root);
            if(clean){
                var plan=await Task.Run(()=>engine.PlanExtraReset());
                if(plan.Count==0){detail.Text="Nenhum arquivo fora do pacote Cyberpunk foi encontrado.";return;}
                using(var review=new GeneralDialog(T("Revisar arquivos fora do pacote Cyberpunk","Review files outside the Cyberpunk pack")){ClientSize=new Size(820,475),RequiresDecision=true}){
                    var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20,8,20,20),ColumnCount=1,RowCount=2};
                    layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    var field=new GeneralSurface{Dock=DockStyle.Fill,Padding=new Padding(12),Margin=Padding.Empty};
                    var paths=new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,Dock=DockStyle.Fill,Text=String.Join("\r\n",plan.Select(p=>p.Path))};
                    field.Controls.Add(paths);layout.Controls.Add(field,0,0);
                    var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(0,14,0,0),WrapContents=false};
                    var go=GeneralStyle.Button(T("Revisado, continuar","Reviewed, continue"),()=>review.Complete(DialogResult.OK),true);
                    var cancel=GeneralStyle.Button(T("Cancelar","Cancel"),()=>review.Complete(DialogResult.Cancel));
                    buttons.Controls.Add(go);buttons.Controls.Add(cancel);layout.Controls.Add(buttons,0,1);review.Content.Controls.Add(layout);
                    review.AcceptButton=go;review.CancelButton=cancel;GeneralStyle.Apply(review,GeneralStyle.Dark);paths.BorderStyle=BorderStyle.None;
                    if(review.ShowDialog(this)!=DialogResult.OK)return;
                }
                if(GeneralDialog.Message(this,"Retirar os "+plan.Count+" arquivos listados? Eles serão guardados para desfazer a operação.","Confirmar limpeza",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
                detail.Text=await Task.Run(()=>engine.ApplyExtraReset(plan,Report));
            }else{
                if(GeneralDialog.Message(this,"Desfazer a última operação do Cyberpunk?","Confirmar reversão",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
                detail.Text=await Task.Run(()=>engine.Undo(Report));
            }
            changed=true;
        }catch(Exception ex){ShowError(ex);}
        finally{working=false;Busy(false);}
        if(changed)await Scan();
    }
}

