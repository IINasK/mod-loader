using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    const string DependencyCategory="Dependências",CoreDependencyCategory="Núcleo e dependências";
    IReadOnlyList<P2PSyncDifference> gameInventory=new P2PSyncDifference[0];

    readonly NexusManualScan nexusScan=new NexusManualScan();
    Dictionary<string,NexusManualMatch> nexusManual=new Dictionary<string,NexusManualMatch>(StringComparer.Ordinal);
    Func<GeneralNexusLink,Func<bool>,NexusFilePreview[]> testNexusPreviews;

    string NexusVariantName(GeneralModEntry mod) {
        var variant=Selected(mod);
        if(IsBigBody(mod))return BodyChoiceLabel(ActiveBodyChoice(mod))??Selected(mod).Name;
        NexusManualMatch match;
        return nexusManual.TryGetValue(mod.Id,out match)&&Object.ReferenceEquals(match.Variant,variant)&&
            !String.IsNullOrEmpty(match.Version)&&!variant.Name.Contains(match.Version)?variant.Name+" · "+match.Version:variant.Name;
    }

    sealed class ProjectVerification {
        readonly bool online;
        readonly System.Threading.SemaphoreSlim remoteReads=new System.Threading.SemaphoreSlim(4);
        readonly ConcurrentDictionary<string,Lazy<Task<GeneralSourceInfo>>> pages=new ConcurrentDictionary<string,Lazy<Task<GeneralSourceInfo>>>();
        readonly ConcurrentDictionary<string,Lazy<Task<List<GeneralNexusRequirement[]>>>> requirements=
            new ConcurrentDictionary<string,Lazy<Task<List<GeneralNexusRequirement[]>>>>();
        internal readonly List<Work> Checked=new List<Work>();
        internal readonly List<Work> Divergent=new List<Work>();
        internal ScanReads Reads;
        internal int MetadataChanged,UnavailableMetadata,UnavailableRequirements;

        internal ProjectVerification(bool online) { this.online=online; }
        static IEnumerable<GeneralNexusLink> Sources(Work work) {
            return work.Variant.Sources.Select(GeneralNexusLink.Parse).Where(link=>link!=null);
        }
        static string Page(GeneralNexusLink link) { return new GeneralNexusLink{Game=link.Game,Mod=link.Mod}.Url; }
        async Task<TResult> Lookup<TResult>(Func<Task<TResult>> read) where TResult:class {
            await remoteReads.WaitAsync();
            try{return await read();}catch(Exception){return null;}
            finally{remoteReads.Release();}
        }
        internal async Task ReadSources(Work work) {
            if(!online)return;
            foreach(var link in Sources(work)){
                string page=Page(link);
                await pages.GetOrAdd(page,url=>new Lazy<Task<GeneralSourceInfo>>(()=>Lookup(async()=>{
                    var info=await GeneralNexusInfo.Read(url);
                    await Task.Delay(200);
                    return info;
                }))).Value;
                if(link.File>0&&GeneralNexusInfo.HasTestKey)
                    await requirements.GetOrAdd(link.Url,url=>new Lazy<Task<List<GeneralNexusRequirement[]>>>(
                        ()=>Lookup(()=>GeneralNexusInfo.ReadRequirements(link)))).Value;
            }
        }
        internal void Record(Work work,string status=null) {
            Checked.Add(work);
            if(status=="Diferente/alterado"||status==GeneralModInstaller.Modified||status==GeneralModInstaller.Partial||status=="Parcial")Divergent.Add(work);
            int count=work.Entry.Tags.Count;
            bool adult=work.Entry.Nsfw;
            foreach(var link in Sources(work)){
                Lazy<Task<GeneralSourceInfo>> request;
                var metadata=pages.TryGetValue(Page(link),out request)?request.Value.Result:null;
                if(metadata==null||!metadata.NexusChecked)UnavailableMetadata++;
                if(metadata==null)continue;
                if(metadata.Nsfw&&work.Entry.NsfwOverride==null)work.Entry.Nsfw=true;
                foreach(string tag in metadata.Tags??new string[0]){
                    if(work.Entry.Tags.Count>=32)break;
                    if(!work.Entry.Tags.Contains(tag,StringComparer.OrdinalIgnoreCase))work.Entry.Tags.Add(tag);
                }
            }
            if(count!=work.Entry.Tags.Count||adult!=work.Entry.Nsfw)MetadataChanged++;
        }
        internal GeneralSourceInfo PublishedPage(string page){
            Lazy<Task<GeneralSourceInfo>> request;
            return pages.TryGetValue(page,out request)?request.Value.Result:null;
        }
        internal Dictionary<string,List<GeneralNexusRequirement[]>> PublishedRequirements(){
            return requirements.Where(pair=>pair.Value.IsValueCreated&&pair.Value.Value.Status==TaskStatus.RanToCompletion&&pair.Value.Value.Result!=null)
                .ToDictionary(pair=>pair.Key,pair=>pair.Value.Value.Result,StringComparer.Ordinal);
        }
        internal IEnumerable<GeneralNexusRequirement[]> Requirements(Work work,bool countUnavailable=true) {
            foreach(var link in Sources(work)){
                Lazy<Task<List<GeneralNexusRequirement[]>>> request;
                var result=requirements.TryGetValue(link.Url,out request)?request.Value.Result:null;
                if(result==null){if(countUnavailable)UnavailableRequirements++;continue;}
                foreach(var item in result)yield return item;
            }
        }
    }

    static KeyValuePair<string,string> VerificationLine(string heading,string value){
        return new KeyValuePair<string,string>(heading,value);
    }

    async void ShowProjectDetails(){
        if(GeneralDialog.FocusOpen<GeneralDependencyReview>(this,"project-details"))return;
        var issues=new List<GeneralDependencyIssue>();
        if(!String.IsNullOrWhiteSpace(state.Destination)&&Directory.Exists(state.Destination)){
            var deployed=new GeneralPresetDeployment(Root(),data).Read();
            foreach(string issue in deployed.Issues){
                int separator=issue.IndexOf(':');
                issues.Add(new GeneralDependencyIssue{Mod=separator>0?issue.Substring(0,separator):ActivePreset==null?"":ActivePreset.Name,
                    Requirement=T("Preset","Preset"),Status=separator>0?issue.Substring(separator+1).TrimStart():issue});
            }
        }
        using(var dialog=new GeneralDependencyReview(ModVersionSummary(),issues,launch,
            title:T("Detalhes do projeto","Project details")))
            await dialog.ShowModelessAsync(this,"project-details");
    }

    bool verifyingProject;

    async Task VerifyProject() {
        if(GeneralDialog.FocusOpen<GeneralDependencyReview>(this,"verify"))return;
        if(working||scanning||verifyingProject||associationChoice!=null||exitApproved)return;
        verifyingProject=true;
        try {
            UpdateVerificationAction();
            if(projectDraft!=null&&!ConfirmProjectNavigation())return;
            if(String.IsNullOrWhiteSpace(state.Destination)||!Directory.Exists(state.Destination))return;
            var audit=new ProjectVerification(!testMode);
            await Scan(audit);
            if(IsDisposed||audit.Reads==null)return;
            await ReviewInventoryLinks();
            if(IsDisposed)return;
            var install=new Dictionary<string,Work>(StringComparer.Ordinal);
            var issues=ReviewDependencies(audit,install);
            var summary=new List<KeyValuePair<string,string>>{
                VerificationLine(T("Arquivos locais","Local files"),String.Format(T("{0} mods conferidos","{0} mods checked"),audit.Checked.Count))
            };
            summary.AddRange(ModVersionSummary());
            if(audit.UnavailableMetadata>0)
                summary.Add(VerificationLine(T("Tags do Nexus","Nexus tags"),
                    String.Format(T("{0} consultas indisponíveis","{0} queries unavailable"),audit.UnavailableMetadata)));
            if(audit.UnavailableRequirements>0)
                summary.Add(VerificationLine(T("Dependências do Nexus","Nexus dependencies"),GeneralNexusInfo.HasTestKey
                    ?String.Format(T("{0} consultas sem resposta","{0} unanswered queries"),audit.UnavailableRequirements)
                    :T("Conecte sua conta para consultar","Connect your account to check")));
            string verifyRoot=Root(),verifyPreset=state.SelectedPreset;
            var repairs=new Dictionary<GeneralDependencyIssue,Work>();
            foreach(var work in audit.Divergent.Where(CanReviewReplacement)){
                var recipe=Recipe(work);
                var scan=await Task.Run(()=>audit.Reads.Fixed(recipe));
                if(scan.State!="Diferente/alterado"&&!presetProtected.Contains(work.Entry.Id))continue;
                if(scan.State!="Diferente/alterado"&&scan.State!="Parcial")continue;
                var issue=new GeneralDependencyIssue{Mod=work.Entry.Name,Requirement=recipe.Version,
                    Status=T("Arquivos divergentes","Files differ"),CanRepair=true,Category=work.Entry.Category};
                issues.Add(issue);repairs.Add(issue,work);
            }
            if(IsDisposed)return;

            Work repair=null;
            var pendingDependencies=new List<Work>();
            Action<GeneralNexusRequirement> add=target=>{
                if(IsDisposed||exitApproved||working||scanning||state.SelectedPreset!=verifyPreset||
                    !InventoryPaths.Equals(state.Destination,verifyRoot))
                    throw new IOException(T("O preset mudou. Abra uma nova verificação para adicionar a dependência.",
                        "The preset changed. Run verification again to add the dependency."));
                AddReviewedDependency(target);
            };
            using(var dialog=new GeneralDependencyReview(summary,issues,launch,add,
                repairs.Count==0?null:(Action<GeneralDependencyIssue>)(issue=>repair=repairs[issue]),
                categories:PresetCategories(),uncategorized:UncatName(),viewInstalled:ViewInstalledDependency)){
                if(await dialog.ShowModelessAsync(this,"verify")!=DialogResult.OK||IsDisposed||exitApproved||
                    working||scanning||state.SelectedPreset!=verifyPreset||!InventoryPaths.Equals(state.Destination,verifyRoot))return;
                pendingDependencies.AddRange(dialog.SelectedInstallKeys.Select(key=>install[key]));
            }
            if(repair!=null){
                repair.ReviewReplacement=true;
                if(state.List.Mods.Contains(repair.Entry)&&MakeWork(repair.Entry).Key==repair.Key&&
                    String.Equals(verifyRoot,Root(),StringComparison.OrdinalIgnoreCase))Enqueue(new[]{repair},false);
                return;
            }
            string root=Root();
            var selected=ChooseAlternatives(pendingDependencies,entries=>{
                using(var dialog=new GeneralModAlternativeDialog(entries))
                    return dialog.ShowDialog(this)==DialogResult.OK?dialog.SelectedId:null;
            });
            if(selected==null||selected.Count==0)return;
            selected=WithLinkedDependencies(selected);
            var pending=new List<Work>();
            scanning=true;Busy(true);
            try{await Task.Run(()=>PlanSelected(selected,root,pending));}
            finally{scanning=false;Busy(false);}
            if(IsDisposed)return;
            if(pending.Count>0){
                foreach(var work in pending)work.DependencyQueue=true;
                await ConfirmAndEnqueue(pending,root,false);
            }
        }catch(Exception ex){if(!IsDisposed)ShowError(ex);}
        finally{
            verifyingProject=false;
            if(!IsDisposed)UpdateVerificationAction();
        }
    }

    bool CanReviewReplacement(Work work){
        if(work.Variant.CyberpunkId==null||work.Variant.CyberpunkVariant!=null||work.Variant.InstallRoutes.Count!=0)return false;
        var recipe=Recipe(work);
        return !recipe.Immutable&&!EquipmentExOptions.Available(recipe)&&String.IsNullOrEmpty(recipe.BlockedReason);
    }

    List<P2PSyncDifference> TryReadGameInventory(string destination,ScanReads reads,Dictionary<string,InventoryZipLabel> zipLabels) {
        try{return ReadGameInventory(destination,reads,zipLabels);}
        catch(IOException ex){OnlineDiagnostics.Failure("scan.inventory.failed",ex,"Identificar os mods na pasta do jogo.");return null;}
        catch(UnauthorizedAccessException ex){OnlineDiagnostics.Failure("scan.inventory.failed",ex,"Ler a pasta do jogo.");return null;}
    }

    List<P2PSyncDifference> ReadGameInventory(string destination,ScanReads reads=null,Dictionary<string,InventoryZipLabel> zipLabels=null) {
        destination=Path.GetFullPath(destination).TrimEnd('\\');
        bool cyberpunk=IsCyberpunk(destination);
        var byWork=state.List.Mods.SelectMany(mod=>mod.Variants.Select(variant=>new{Mod=mod,Key=Key(mod,variant)}))
            .ToDictionary(item=>item.Key,item=>item.Mod.Id,StringComparer.Ordinal);
        var receiptNames=state.List.Mods.SelectMany(mod=>mod.Variants.Select(variant=>new{
            Key=Key(mod,variant), Legacy=InstalledSnapshot.Owner(mod,variant), Name=mod.Name+" · "+variant.Name
        })).SelectMany(item=>new[]{new{Key=item.Key,item.Name},new{Key=item.Legacy,item.Name}})
            .ToLookup(item=>item.Key,item=>item.Name,StringComparer.Ordinal);
        var recorded=new List<ModFileAnchor>();
        var names=state.List.Mods.ToDictionary(mod=>mod.Id,mod=>mod.Name,StringComparer.Ordinal);
        var registered=new List<ModFileAnchor>();
        Action<ProjectDeleteFile,Func<string,IEnumerable<string>>> add=(file,owners)=>{
            foreach(string owner in file.OwnerKeys.SelectMany(owners).Distinct(StringComparer.Ordinal))
                registered.Add(new ModFileAnchor{Owner=owner,Path=file.Path,Hash=file.Hash,LocalReceipt=true});
        };
        Action<ProjectDeleteFile,IEnumerable<string>> record=(file,owners)=>{
            foreach(string owner in owners.Distinct(StringComparer.Ordinal))
                recorded.Add(new ModFileAnchor{Owner=owner,Path=file.Path,Hash=file.Hash,LocalReceipt=true});
        };
        Func<string,IEnumerable<string>> receiptName=key=>receiptNames[WorkKeyFromReceipt(key)].DefaultIfEmpty(WorkKeyFromReceipt(key));
        Func<string,IEnumerable<string>> lookup=key=>byWork.ContainsKey(key)?new[]{byWork[key]}:new string[0];
        foreach(var file in Snapshot(destination).ProjectDeletionFiles(true,skipUnreadable:true)){
            add(file,lookup);
            record(file,file.OwnerKeys.SelectMany(receiptName));
        }
        var detectedOwners=state.List.Mods.SelectMany(mod=>mod.DetectedPaths.Select(path=>new{Path=path,mod.Id}))
            .ToLookup(file=>file.Path,file=>file.Id,InventoryPaths);
        var exact=new HashSet<string>(detectedOwners.Select(group=>group.Key),InventoryPaths);
        var extras=new List<P2PSyncDifference>();
        var registeredNames=new Dictionary<string,string>(names,StringComparer.Ordinal);
        var shared=ReadSharedInventoryEvidence(destination,reads);
        var sharedPaths=new HashSet<string>(InventoryPaths);
        foreach(var evidence in shared){
            string owner=evidence.Work.Entry.Id;
            foreach(var file in evidence.Match.Receipt.Files.Where(file=>!GeneralInstallRoutes.External(file.Path))){
                registered.Add(new ModFileAnchor{Owner=owner,Path=file.Path,Hash=file.Sha256,LocalReceipt=true});
                recorded.Add(new ModFileAnchor{Owner=names[owner]+" · "+evidence.Match.Receipt.Upgrade.Version,
                    Path=file.Path,Hash=file.Sha256,LocalReceipt=true});
                exact.Add(file.Path);
                sharedPaths.Add(file.Path);
            }
        }
        foreach(var group in shared.SelectMany(item=>item.Match.Receipt.Files
            .Where(file=>!GeneralInstallRoutes.External(file.Path)).Select(file=>new{file.Path,Owner=item.Work.Entry.Id}))
            .GroupBy(file=>file.Path,InventoryPaths)){
            var owners=group.Select(file=>file.Owner).Distinct(StringComparer.Ordinal).Take(2).ToArray();
            if(owners.Length==1)extras.Add(new P2PSyncDifference{Kind="associated",Path=group.Key,
                Mod=names[owners[0]],ModId=owners[0],ProvenOwner=owners[0]});
        }
        if(cyberpunk){
            var engine=Cyberpunk(destination);
            var fixedOwners=state.List.Mods.SelectMany(mod=>mod.Variants.Where(variant=>variant.CyberpunkId!=null)
                .Select(variant=>new{Id=variant.CyberpunkId,Owner=mod.Id})).ToLookup(item=>item.Id,item=>item.Owner,StringComparer.Ordinal);
            foreach(var anchor in engine.CatalogAnchors()){
                var owners=fixedOwners[anchor.Owner].Distinct(StringComparer.Ordinal).ToArray();
                if(owners.Length==0)registered.Add(anchor);
                else {
                    exact.Add(anchor.Path);
                    foreach(string owner in owners)registered.Add(new ModFileAnchor{Owner=owner,Path=anchor.Path,Hash=anchor.Hash});
                }
            }
            var imports=state.CyberpunkImports.ToLookup(item=>item.Value,item=>WorkKeyFromReceipt(item.Key),StringComparer.Ordinal);
            foreach(var file in engine.ProjectDeletionFiles(new HashSet<string>(imports.Select(item=>item.Key),StringComparer.Ordinal),true)){
                add(file,key=>imports[key].SelectMany(lookup));
                record(file,file.OwnerKeys.SelectMany(key=>imports[key]).SelectMany(receiptName));
            }
            var namedPaths=new HashSet<string>(recorded.Select(file=>file.Path),InventoryPaths);
            recorded.AddRange(engine.ReceiptInventory().Where(file=>!namedPaths.Contains(file.Path)));
            foreach(var mod in state.List.Mods.Where(mod=>GeneralModLibrary.IsHystBody(mod.Id))){
                var files=new List<InstalledSnapshotFile>();
                var installed=InstalledSnapshot.DetectManualHyst(destination,mod,seed.Mods.FirstOrDefault(item=>item.Id==mod.Id),files,reads==null?null:(Func<string,string>)reads.Hash);
                if(installed==null)continue;
                foreach(var file in files)registered.Add(new ModFileAnchor{Owner=mod.Id,Path=file.Path,Hash=file.Sha256});
                extras.Add(new P2PSyncDifference{Kind="verified-variant",Mod=mod.Name,ModId=mod.Id,
                    VariantId=installed.Id,VerifiedBytes=files.Sum(file=>file.Size)});
            }
            exact.UnionWith(registered.Where(file=>names.ContainsKey(file.Owner)).Select(file=>file.Path));
            foreach(var file in engine.ReadUnlistedModFiles(exact.Where(path=>!detectedOwners.Contains(path)),data,
                reads==null?null:(Func<string,string>)reads.Hash,true)){
                if(sharedPaths.Contains(file.Path))continue;
                if(file.ProvenOwner!=null){
                    string owner=file.ProvenOwner;
                    var candidates=fixedOwners[owner].Concat(imports[owner].SelectMany(lookup)).Concat(detectedOwners[file.Path])
                        .Concat(names.ContainsKey(owner)?new[]{owner}:Enumerable.Empty<string>())
                        .Distinct(StringComparer.Ordinal).Take(2).ToArray();
                    if(candidates.Length==1){
                        file.Kind="associated";
                        file.ModId=candidates[0];
                        file.Mod=names[file.ModId];
                    }
                }
                if(!exact.Contains(file.Path)||file.Kind=="associated"||file.Kind=="found"&&detectedOwners.Contains(file.Path))extras.Add(file);
            }
            foreach(var owner in engine.OwnershipNames())
                if(!registeredNames.ContainsKey(owner.Key))registeredNames.Add(owner.Key,owner.Value);
        }
        foreach(var file in Generic(destination).ProjectDeletionFiles(true)){
            add(file,key=>lookup(WorkKeyFromReceipt(key)));
            record(file,file.OwnerKeys.SelectMany(receiptName));
        }
        var portable=Snapshot(destination).ReadOutputScopes()
            .Where(item=>item.Anchor.StartsWith(item.Path+"/",StringComparison.OrdinalIgnoreCase))
            .GroupBy(item=>item.Anchor,InventoryPaths)
            .Where(group=>group.Select(item=>item.Path).Distinct(InventoryPaths).Count()==1)
            .ToDictionary(group=>group.Key,group=>group.First().Path,InventoryPaths);
        Func<string,string> scope=path=>{
            string folder;
            return portable.TryGetValue(path,out folder)?folder:
                cyberpunk?FixedInstaller.ModFolder(path):InstalledFileOwnership.GenericScope(path);
        };
        var associated=InstalledFileOwnership.FindAssociated(destination,registered,scope,
            cyberpunk?extras.Where(file=>file.Kind=="found").Select(file=>file.Path):null,
            reads==null?null:(Func<string,string>)reads.Hash,includeAll:!cyberpunk);
        var labels=zipLabels??new Dictionary<string,InventoryZipLabel>(InventoryPaths);
        Func<string,string> readHash=reads==null?(Func<string,string>)Safe.Hash:reads.Hash;
        var ambiguous=cyberpunk&&reads!=null?InventoryLinkStore.DiscoverZips(destination,
            testMode?new[]{state.DownloadFolder}:GeneralModDownload.WatchFolders(state.DownloadFolder),
            extras.Where(file=>file.Kind=="found"&&file.ProvenOwner==null).Select(file=>file.Path),labels,readHash)
            :new HashSet<string>(InventoryPaths);
        foreach(var file in extras.Where(item=>item.Kind=="found")){
            InventoryZipLabel label;
            if(file.ProvenOwner!=null||!labels.TryGetValue(file.Path,out label))continue;
            try {
                string path=GeneralInstallRoutes.Resolve(destination,file.Path);
                Safe.NoLinks(path);
                if((reads==null?(Func<string,string>)Safe.Hash:reads.Hash)(path)!=label.Hash)continue;
                file.Mod=label.Name;
                file.VerifiedZip=label.Archive;
                file.InventoryGroupId="zip:"+label.Name;
                var owners=detectedOwners[file.Path].Take(2).ToArray();
                if(owners.Length==1){
                    file.Kind="associated";
                    file.ModId=owners[0];
                    file.Mod=names[file.ModId];
                }
            }catch(IOException){}catch(UnauthorizedAccessException){}
        }
        extras.RemoveAll(file=>file.Kind=="found"&&detectedOwners.Contains(file.Path));
        var observedScopes=cyberpunk
            ? GroupModFolders(destination,extras,registered,registeredNames,names,exact,labels,readHash,ambiguous)
            : new HashSet<string>(InventoryPaths);
        extras.RemoveAll(file=>file.Kind=="found"&&file.VerifiedZip==null&&file.ProvenOwner==null&&!file.LocalInModFolder&&
            associated.ContainsKey(file.Path)&&names.ContainsKey(associated[file.Path])&&!labels.ContainsKey(file.Path)&&
            !observedScopes.Contains(scope(file.Path))&&!ambiguous.Contains(file.Path));
        foreach(var pair in associated.Where(item=>names.ContainsKey(item.Value)&&!labels.ContainsKey(item.Key)&&!ambiguous.Contains(item.Key)&&
            !observedScopes.Contains(scope(item.Key))))
            extras.Add(new P2PSyncDifference{Kind="associated",Mod=names[pair.Value],ModId=pair.Value,Path=pair.Key,LocalInModFolder=true});
        var declarations=state.List.Mods.SelectMany(mod=>(Selected(mod).OutputFolders??new List<string>())
            .Select(folder=>new KeyValuePair<string,string>(folder,mod.Id)))
            .Concat(shared.SelectMany(item=>(item.Match.Variant.OutputFolders??new List<string>())
                .Select(folder=>new KeyValuePair<string,string>(folder,item.Work.Entry.Id))));
        var declaredOutputs=InstalledFileOwnership.FindDeclared(destination,declarations,registered,readHash)
            .GroupBy(pair=>pair.Key,InventoryPaths).ToDictionary(group=>group.Key,group=>group.First().Value,InventoryPaths);
        AssociateDeclaredOutputs(extras,declaredOutputs,names);
        var associatedPaths=new HashSet<string>(extras.Where(file=>file.Kind=="associated").Select(file=>file.Path),InventoryPaths);
        extras.RemoveAll(file=>file.Kind=="inventory-local"&&associatedPaths.Contains(file.Path));
        var recordedFiles=RecordedInventory(destination,recorded,readHash).ToArray();
        extras.AddRange(recordedFiles);
        if(!cyberpunk){
            var folders=recordedFiles.Select(file=>file.Path.LastIndexOf('/')>0
                ?file.Path.Substring(0,file.Path.LastIndexOf('/')):null).Where(path=>path!=null).Distinct(InventoryPaths);
            var known=new HashSet<string>(extras.Select(file=>file.Path).Concat(exact),InventoryPaths);
            foreach(string path in InstalledFileOwnership.Walk(destination,folders).Where(known.Add))
                extras.Add(new P2PSyncDifference{Kind="inventory-local",Path=path});
        }
        foreach(var file in extras.Where(item=>item.Kind=="inventory-local"||
            item.Kind=="found"&&item.LocalInModFolder))
            file.Kind="unlinked";
        return extras;
    }

    static void AssociateDeclaredOutputs(List<P2PSyncDifference> inventory,Dictionary<string,string> outputs,
        Dictionary<string,string> names){
        var identified=new HashSet<string>(inventory.Where(file=>file.Path!=null&&
            (file.ProvenOwner!=null||file.VerifiedZip!=null)).Select(file=>file.Path),InventoryPaths);
        foreach(var file in inventory.Where(file=>file.Path!=null&&file.Kind=="associated")){
            string declaredOwner;
            if(outputs.TryGetValue(file.Path,out declaredOwner)&&declaredOwner!=file.ModId)identified.Add(file.Path);
        }
        foreach(string path in identified)outputs.Remove(path);
        inventory.RemoveAll(file=>file.Path!=null&&outputs.ContainsKey(file.Path));
        inventory.AddRange(outputs.Select(item=>new P2PSyncDifference{Kind="associated",Mod=names[item.Value],
            ModId=item.Value,Path=item.Key,LocalInModFolder=true}));
    }

    static IEnumerable<P2PSyncDifference> RecordedInventory(string root,IEnumerable<ModFileAnchor> records,
        Func<string,string> readHash){
        foreach(var group in records.Where(file=>!GeneralInstallRoutes.External(file.Path)&&FixedData.HashValid(file.Hash))
            .GroupBy(file=>file.Path,InventoryPaths)){
            string hash;
            try{
                string path=Safe.In(root,group.Key);
                Safe.NoLinks(path);
                if(!File.Exists(path))continue;
                hash=readHash(path);
            }catch(IOException){continue;}catch(UnauthorizedAccessException){continue;}
            var matching=group.Where(file=>file.Hash==hash).ToArray();
            var names=(matching.Length==0?group:matching.AsEnumerable()).Select(file=>file.Owner)
                .Distinct(InventoryPaths).OrderBy(name=>name,InventoryPaths);
            yield return new P2PSyncDifference{Path=group.Key,Kind="inventory-record",Mod=String.Join(", ",names),
                RecordedFile=true,RecordedChange=matching.Length==0};
        }
    }

    static HashSet<string> GroupModFolders(string root,List<P2PSyncDifference> inventory,IEnumerable<ModFileAnchor> registered,
        Dictionary<string,string> names,Dictionary<string,string> projectNames,HashSet<string> listed,
        Dictionary<string,InventoryZipLabel> labels,Func<string,string> readHash,HashSet<string> ambiguous) {
        var files=inventory.Where(file=>file.Kind=="found"||file.ProvenOwner!=null||file.VerifiedZip!=null).ToDictionary(file=>file.Path,InventoryPaths);
        var confirmed=new Dictionary<string,string>(InventoryPaths);
        foreach(string path in ambiguous)confirmed[path]=String.Empty;
        foreach(var file in files.Values){
            string owner=file.ModId??(file.VerifiedZip!=null?file.InventoryGroupId:file.ProvenOwner);
            if(file.Kind=="found"&&owner!=null&&projectNames.ContainsKey(owner))owner=String.Empty;
            if(owner!=null){
                confirmed[file.Path]=owner;
                if(!names.ContainsKey(owner))names[owner]=file.Mod;
            }
        }
        foreach(var anchor in registered.Where(file=>FixedInstaller.ModFolder(file.Path)!=null&&
            !GeneralInstallRoutes.External(file.Path)&&FixedData.HashValid(file.Hash))){
            if(files.ContainsKey(anchor.Path))continue;
            try{
                string path=Safe.In(root,anchor.Path);Safe.NoLinks(path);
                if(!File.Exists(path)||readHash(path)!=anchor.Hash)continue;
                string existing;
                confirmed[anchor.Path]=confirmed.TryGetValue(anchor.Path,out existing)&&existing!=anchor.Owner?"":anchor.Owner;
            }catch(IOException){}catch(UnauthorizedAccessException){}
        }
        var scopes=confirmed.Where(pair=>FixedInstaller.ModFolder(pair.Key)!=null)
            .ToLookup(pair=>FixedInstaller.ModFolder(pair.Key),InventoryPaths);
        foreach(string path in InstalledFileOwnership.Walk(root,scopes.Select(group=>group.Key))){
            if(listed.Contains(path)||confirmed.ContainsKey(path))continue;
            string folder=FixedInstaller.ModFolder(path);
            if(!scopes.Contains(folder))continue;
            P2PSyncDifference item;
            if(!files.TryGetValue(path,out item)){
                item=new P2PSyncDifference{Kind="found",Path=path,Mod=Path.GetFileName(folder)};
                files.Add(path,item);
                inventory.Add(item);
            }
        }
        var byFolder=files.Values.ToLookup(file=>FixedInstaller.ModFolder(file.Path),InventoryPaths);
        var knownZipOwners=labels.Where(pair=>FixedInstaller.ModFolder(pair.Key)!=null&&!confirmed.ContainsKey(pair.Key))
            .ToLookup(pair=>FixedInstaller.ModFolder(pair.Key),pair=>"zip:"+pair.Value.Name,InventoryPaths);
        foreach(var group in scopes){
            var owners=group.Select(pair=>pair.Value).Distinct(StringComparer.Ordinal).ToArray();
            if(owners.Length!=1||owners[0].Length==0||
                !group.Any(pair=>InventoryPaths.Equals(Path.GetDirectoryName(pair.Key).Replace('\\','/'),group.Key)))continue;
            string owner=owners[0],name;
            if(!names.TryGetValue(owner,out name)||
                knownZipOwners[group.Key].Any(label=>!InventoryPaths.Equals(label,"zip:"+name)))continue;
            bool inProject=projectNames.ContainsKey(owner);
            foreach(var item in byFolder[group.Key]){
                if(confirmed.ContainsKey(item.Path)||labels.ContainsKey(item.Path))continue;
                item.Mod=name;
                item.LocalInModFolder=true;
                item.Kind=inProject?"associated":"found";
                item.ModId=inProject?owner:null;
                item.InventoryGroupId=owner.StartsWith("zip:",StringComparison.Ordinal)?owner:"owner:"+owner;
            }
        }
        return new HashSet<string>(scopes.Select(group=>group.Key),InventoryPaths);
    }

    void ViewInstalledDependency(string id){
        var entry=state.List.Mods.FirstOrDefault(mod=>mod.Id==id);
        if(entry!=null)Edit(entry);
    }

    bool VerifiedDependencyInstalled(Work work){
        string status;
        return statuses.TryGetValue(work.Key,out status)&&GeneralStatusPresentation.Kind(status)=="installed";
    }

    bool? MatchesDependencyRange(Work work,GeneralNexusRequirement target){
        string status;
        if(target.MaxFile>0&&target.MaxFile==target.MinFile||
            !statuses.TryGetValue(work.Key,out status)||!VersionInstalled(status))return null;
        string version=VersionInStatus(work.Key,status);
        var minimum=ModVersionNumbers.Compare(version,target.MinVersion);
        var maximum=ModVersionNumbers.Compare(version,target.MaxVersion);
        if(!minimum.HasValue||target.MaxFile>0&&!maximum.HasValue)return null;
        return minimum.Value>=0&&(target.MaxFile==0||maximum.Value<=0);
    }

    static string NexusDependencyPage(GeneralNexusLink link){return link.Game+"/"+link.Mod;}

    static string DependencyName(string value){
        return new string((value??"").Where(Char.IsLetterOrDigit).Select(Char.ToUpperInvariant).ToArray());
    }
    static IEnumerable<string> DependencyNames(Work work){
        yield return DependencyName(work.Entry.Name);
        foreach(string source in work.Variant.Sources){
            var repo=GeneralGithubLink.Parse(source);
            if(repo!=null)yield return DependencyName(repo.Repository.Split('/').Last());
        }
    }
    IEnumerable<string> RegisteredDependencyPages(Work work){
        foreach(var link in work.Variant.Sources.Select(GeneralNexusLink.Parse).Where(link=>link!=null))
            yield return NexusDependencyPage(link);
        var recipe=catalog.Packages.FirstOrDefault(item=>item.Id==(work.Variant.CyberpunkId??work.Entry.Id));
        if(recipe!=null)foreach(int id in recipe.Assets.Select(asset=>asset.ModId).Concat(new[]{recipe.NexusModId}).Where(id=>id>0))
            yield return "cyberpunk2077/"+id;
    }
    static bool UneditedDependencySuggestion(GeneralModEntry mod,string page,string name,GeneralSourceInfo published){
        if(mod.DependencySource!=page||mod.Name!=name||mod.DependencyFile!=0||
            mod.AlternativeGroup!=null||mod.DetectedPaths.Count!=0||mod.NsfwOverride!=null||
            !String.IsNullOrEmpty(mod.Description)||mod.CompatibilityGroups.Count!=0||
            mod.Category!=DependencyCategory&&mod.Category!=CoreDependencyCategory||
            !mod.Tags.Contains("Dependência detectada")||mod.Variants.Count!=1)return false;
        var variant=mod.Variants[0];
        if(variant.Id!="default"||mod.DefaultVariant!=variant.Id||variant.Name!="Indefinida"&&variant.Name!="Undefined"||
            variant.CyberpunkId!=null||variant.DisplayName!=null||variant.Category!=null||!String.IsNullOrEmpty(variant.Description)||
            variant.NsfwOverride!=null||variant.Tags!=null||variant.NaturalOption!=null||variant.CompatibilityGroups.Count!=0||
            variant.InstallRoutes.Count!=0||variant.OutputFolders.Count!=0||variant.RequiredMods.Count!=0||
            variant.Sources.Count!=1||variant.Sources[0]!=page)return false;
        var tags=published==null?null:published.Tags;
        return mod.Tags.All(tag=>tag=="Dependência detectada"||tags!=null&&tags.Contains(tag,StringComparer.OrdinalIgnoreCase))&&
            variant.References.All(reference=>published!=null&&String.Equals(reference,published.Reference,StringComparison.OrdinalIgnoreCase));
    }
    bool ReconcileDependencySuggestions(List<Work> works,Func<string,GeneralSourceInfo> metadata,HashSet<string> requiredPages){
        if(projectDraft!=null||loadingPreset||!works.Any(work=>work.Entry.DependencySource!=null))return false;
        var installed=works.Where(work=>work.Entry.DependencySource==null&&VerifiedDependencyInstalled(work)).ToArray();
        var pages=installed.SelectMany(work=>RegisteredDependencyPages(work).Distinct().Select(page=>new{Work=work,Page=page}))
            .ToLookup(item=>item.Page,item=>item.Work,StringComparer.Ordinal);
        var names=installed.SelectMany(work=>DependencyNames(work).Distinct().Select(name=>new{Work=work,Name=name}))
            .ToLookup(item=>item.Name,item=>item.Work,StringComparer.Ordinal);
        var removed=new HashSet<string>(StringComparer.Ordinal);
        var configurations=state.List.Mods.Concat(state.List.Presets.SelectMany(preset=>preset.Configurations.Values)).ToArray();
        var linked=new HashSet<string>(configurations.SelectMany(mod=>mod.Variants).SelectMany(v=>v.WithNaturalOption())
            .SelectMany(v=>v.RequiredMods).Concat(state.List.Folders.SelectMany(folder=>folder.ModIds))
            .Concat(InventoryLinks.Attachments.SelectMany(file=>file.Owners))
            .Concat(InventoryLinks.Groups.Select(group=>group.CatalogId)),StringComparer.Ordinal);
        if(inventoryLoadError!=null)return false;
        var generic=Generic(state.Destination);
        foreach(var work in works.Where(work=>work.Entry.DependencySource!=null)){
            var suggestion=work.Entry;string url=suggestion.DependencySource;
            var published=metadata==null?null:metadata(url);
            if(linked.Contains(suggestion.Id)||!UneditedDependencySuggestion(suggestion,url,suggestion.Name,published)||
                published!=null&&!String.Equals(published.Name,suggestion.Name,StringComparison.OrdinalIgnoreCase))continue;
            var presets=state.List.Presets.Where(p=>p.ModIds.Contains(suggestion.Id)).ToArray();
            if(presets.Any(p=>p.Configurations.ContainsKey(suggestion.Id)&&!UneditedDependencySuggestion(
                p.Configurations[suggestion.Id],url,suggestion.Name,published)))continue;
            var candidates=pages[NexusDependencyPage(GeneralNexusLink.Parse(url))].ToArray();
            // Names only retire an empty automatic suggestion if the live audit
            // can present the unresolved requirement again for human review.
            if(candidates.Length==0&&requiredPages!=null&&requiredPages.Contains(url))
                candidates=names[DependencyName(suggestion.Name)].ToArray();
            if(candidates.Length!=1||presets.Length==0||presets.Any(p=>!p.ModIds.Contains(candidates[0].Entry.Id)))continue;
            string prefix=suggestion.Id+"/";
            if(state.CyberpunkImports.Keys.Concat(state.SnapshotReceipts.Keys).Concat(state.ResolvedDownloads.Keys)
                .Any(key=>key.StartsWith(prefix,StringComparison.Ordinal))||generic.HasSnapshotRecord(work.Receipt(0))||
                presetProtected.Contains(suggestion.Id))continue;
            removed.Add(suggestion.Id);
            GeneralPresetProfiles.Remove(state,suggestion.Id,true);
            statuses.Remove(work.Key);statusDetails.Remove(work.Key);progress.Remove(work.Key);marked.Remove(suggestion.Id);
        }
        works.RemoveAll(work=>removed.Contains(work.Entry.Id));
        return removed.Count>0;
    }

    List<GeneralDependencyIssue> ReviewDependencies(ProjectVerification audit,Dictionary<string,Work> install) {
        var preset=ActivePreset;
        var entries=state.List.Mods.Select(MakeWork).ToList();
        var members=preset==null?null:new HashSet<string>(preset.ModIds,StringComparer.Ordinal);
        var catalogIds=new HashSet<string>(catalog.Packages.Select(recipe=>recipe.Id),StringComparer.Ordinal);
        var recipes=entries.Select(work=>new{Work=work,Id=work.Variant.CyberpunkId??work.Entry.Id})
            .Where(item=>catalogIds.Contains(item.Id)).ToLookup(item=>item.Id,item=>item.Work,StringComparer.Ordinal);
        var nexusRecipes=catalog.Packages.SelectMany(recipe=>recipe.Assets.Where(asset=>asset.ModId>0).Select(asset=>asset.ModId)
            .Concat(new[]{recipe.NexusModId}).Where(id=>id>0).Distinct().Select(id=>new{Recipe=recipe.Id,Page="cyberpunk2077/"+id}))
            .ToLookup(item=>item.Page,item=>item.Recipe,StringComparer.Ordinal);
        var sources=entries.SelectMany(work=>work.Variant.Sources.Select(GeneralNexusLink.Parse)
            .Where(link=>link!=null).Select(link=>new{Work=work,Link=link}))
            .ToLookup(item=>NexusDependencyPage(item.Link),StringComparer.Ordinal);
        var installedNames=entries.Where(VerifiedDependencyInstalled).SelectMany(work=>DependencyNames(work).Distinct()
            .Select(name=>new{Work=work,Name=name})).ToLookup(item=>item.Name,item=>item.Work,StringComparer.Ordinal);
        Func<Work,bool> inPreset=work=>members==null||members.Contains(work.Entry.Id);
        Func<string,IEnumerable<Work>> related=page=>sources[page].Select(item=>item.Work)
            .Concat(nexusRecipes[page].SelectMany(id=>recipes[id])).Distinct();
        publishedRequirements=audit.PublishedRequirements();
        var issues=new List<GeneralDependencyIssue>();
        bool changed=false;
        foreach(var work in audit.Checked.Where(work=>members==null||members.Contains(work.Entry.Id)).ToArray()){
            if(work.Variant.CyberpunkId!=null){
                foreach(var recipe in ExpandWork(work,state.Destination).Where(recipe=>recipe.Id!=work.Variant.CyberpunkId)){
                    var dependency=recipes[recipe.Id].FirstOrDefault(candidate=>members==null||members.Contains(candidate.Entry.Id))
                        ??recipes[recipe.Id].FirstOrDefault();
                    bool listed=dependency!=null&&(members==null||members.Contains(dependency.Entry.Id));
                    string dependencyStatus=null;
                    bool checkedStatus=dependency!=null&&statuses.TryGetValue(dependency.Key,out dependencyStatus);
                    bool installed=audit.Reads.Cyberpunk&&(checkedStatus?VersionInstalled(dependencyStatus):audit.Reads.Fixed(recipe).IsCorrect);
                    bool availableEdition=!GameEdition.NeedsPL(recipe)||
                        (audit.Reads.PhantomLiberty&&(preset==null||preset.Id!="no-pl"));
                    if(listed&&installed)continue;
                    if(availableEdition&&dependency==null&&!state.List.Mods.Any(entry=>entry.Id==recipe.Id)){
                        var approved=seed.Mods.FirstOrDefault(entry=>entry.Id==recipe.Id);
                        if(approved!=null&&CanAddDependency(preset)){
                            var added=Safe.Json.Deserialize<GeneralModEntry>(Safe.Json.Serialize(approved));
                            AddDependency(added,preset);
                            dependency=MakeWork(added);
                            if(members!=null)members.Add(added.Id);
                            changed=true;
                        }
                    }else if(availableEdition&&dependency!=null&&!listed&&preset.ModIds.Count<1000){
                        preset.ModIds.Add(dependency.Entry.Id);
                        members.Add(dependency.Entry.Id);
                        changed=true;
                    }
                    bool missing=!installed&&(!checkedStatus||dependencyStatus=="Não instalado"||MissingSourceStatus(dependencyStatus));
                    string installKey=null;
                    if(missing&&dependency!=null&&audit.Reads.Cyberpunk&&availableEdition){
                        installKey=dependency.Key;
                        install[installKey]=dependency;
                    }
                    string url=(dependency==null?recipe.Assets.Select(asset=>asset.ModId>0?
                        new GeneralNexusLink{Game="cyberpunk2077",Mod=asset.ModId,File=asset.FileId}.Url:asset.Url):
                        dependency.Variant.Sources).FirstOrDefault(GeneralModDownload.IsWebSource);
                    issues.Add(new GeneralDependencyIssue{Mod=work.Entry.Name,Requirement=recipe.Name,Url=url,
                        Category=work.Entry.Category,InstallKey=installKey,
                        Status=installed?T("Instalado · fora do preset","Installed · outside preset"):
                            !availableEdition?T("Requer Phantom Liberty","Requires Phantom Liberty"):
                            !audit.Reads.Cyberpunk?T("Destino incompatível","Incompatible destination"):
                            dependencyStatus==GeneralStatusPresentation.Disabled?T("Desabilitada","Disabled"):
                            missing?T("Falta instalar","Installation missing"):GeneralStatusPresentation.Label(dependencyStatus)});
                }
            }
            foreach(var group in audit.Requirements(work)){
                bool present=group.Any(target=>{
                    string page=NexusDependencyPage(target.Source);
                    return sources[page].Any(item=>inPreset(item.Work)&&VerifiedDependencyInstalled(item.Work)&&
                        (item.Link.File==target.MinFile||target.MaxFile>0&&item.Link.File==target.MaxFile));
                });
                if(present)continue;
                bool chooseMod=group.Select(item=>item.Source.Url).Distinct().Count()>1;
                foreach(var target in group.GroupBy(item=>Tuple.Create(item.Source.Url,item.MinFile,item.MaxFile)).Select(items=>items.First())){
                    string page=NexusDependencyPage(target.Source);
                    var candidates=related(page).OrderByDescending(VerifiedDependencyInstalled).ToArray();
                    var installed=candidates.FirstOrDefault(VerifiedDependencyInstalled);
                    var sameName=installedNames[DependencyName(target.Name)].Take(2).ToArray();
                    var possible=installed==null&&sameName.Length==1?sameName[0]:null;
                    var existing=candidates.Select(candidate=>candidate.Entry).FirstOrDefault()
                        ??state.List.Mods.FirstOrDefault(entry=>entry.DependencySource==target.Source.Url);
                    bool missing=existing==null;
                    if(!chooseMod&&missing&&possible==null&&CanAddDependency(preset)){
                        var entry=CreateDependency(target,preset,group.Length==1);
                        if(members!=null)members.Add(entry.Id);
                        changed=true;
                    }else if(!chooseMod&&!missing&&preset!=null&&!preset.ModIds.Contains(existing.Id)&&preset.ModIds.Count<1000){
                        preset.ModIds.Add(existing.Id);
                        members.Add(existing.Id);
                        changed=true;
                    }
                    string range=(target.MinVersion??target.MinFile.ToString())+
                        (target.MaxFile==0?"+":target.MaxFile==target.MinFile?"":" – "+(target.MaxVersion??target.MaxFile.ToString()));
                    string url=target.MaxFile==target.MinFile?
                        new GeneralNexusLink{Game=target.Source.Game,Mod=target.Source.Mod,File=target.MinFile}.Url:
                        target.Source.Url+"?tab=files";
                    var local=installed??possible;
                    bool provenSource=installed!=null&&sources[page].Any(item=>item.Work==installed);
                    issues.Add(new GeneralDependencyIssue{Mod=work.Entry.Name,Requirement=target.Name+" · "+range,Url=url,
                        Category=work.Entry.Category,InstalledModId=local==null?null:local.Entry.Id,
                        Candidate=chooseMod&&missing&&possible==null?target:null,
                        Status=installed!=null?(provenSource&&MatchesDependencyRange(installed,target)==false
                            ?T("Versão fora da faixa","Version outside the supported range")
                            :T("Instalado · arquivo a confirmar","Installed · verify archive")):
                            possible!=null?T("Possível mod instalado · confirmar origem","Possible installed mod · verify source"):
                            chooseMod?T("Uma das opções","One of these options"):T("Escolher arquivo","Choose archive")});
                }
            }
        }
        if(changed){Persist();DrawRows();}
        return issues.GroupBy(issue=>Tuple.Create(issue.Mod,issue.Requirement,issue.Status,issue.Url)).Select(items=>items.First()).ToList();
    }

    void AddReviewedDependency(GeneralNexusRequirement target) {
        var preset=ActivePreset;
        if(!CanAddDependency(preset)||state.List.Mods.Any(entry=>entry.DependencySource==target.Source.Url))return;
        BeginProjectEdit();
        CreateDependency(target,preset);
        FinishProjectEdit();DrawRows();
    }

    GeneralModEntry CreateDependency(GeneralNexusRequirement target,GeneralModPreset preset,bool singleRange=true) {
        int pinned=singleRange&&target.MinFile==target.MaxFile?target.MinFile:0;
        var source=new GeneralNexusLink{Game=target.Source.Game,Mod=target.Source.Mod,File=pinned};
        var entry=NewEntry(target.Name,source.Url);
        entry.DependencySource=target.Source.Url;entry.Nsfw=target.Nsfw;
        entry.DependencyFile=pinned;
        entry.Tags.Add("Dependência detectada");
        entry.Variants[0].Name=pinned==0?T("Indefinida","Undefined"):target.MinVersion??T("Principal","Main");
        AddDependency(entry,preset);
        return entry;
    }

    bool CanAddDependency(GeneralModPreset preset) {
        return state.List.Mods.Count<1000&&(preset==null||preset.ModIds.Count<1000);
    }
    void AddDependency(GeneralModEntry entry,GeneralModPreset preset) {
        string category=state.List.Categories.Contains(CoreDependencyCategory)?CoreDependencyCategory:DependencyCategory;
        if(!state.List.Categories.Contains(category))state.List.Categories.Add(category);
        entry.Category=category;
        state.List.Mods.Add(entry);
        if(preset!=null)preset.ModIds.Add(entry.Id);
    }
}

internal sealed class GeneralDependencyIssue {
    internal string Mod,Requirement,Status,Url,Category,InstallKey,InstalledModId;
    internal GeneralNexusRequirement Candidate;
    internal bool CanRepair;
}

internal sealed class GeneralDependencyReview : GeneralDialog {
    readonly HashSet<string> selectedInstallKeys=new HashSet<string>(StringComparer.Ordinal);
    internal IEnumerable<string> SelectedInstallKeys {get{return selectedInstallKeys;}}

    internal GeneralDependencyReview(IEnumerable<KeyValuePair<string,string>> summary,IReadOnlyList<GeneralDependencyIssue> issues,Action<string> open,
        Action<GeneralNexusRequirement> add=null,Action<GeneralDependencyIssue> repair=null,string title=null,
        IEnumerable<string> categories=null,string uncategorized=null,Action<string> viewInstalled=null)
        :base(title??GeneralStyle.T("Resultado da verificação","Verification results")) {
        selectedInstallKeys.UnionWith(issues.Select(issue=>issue.InstallKey).Where(key=>key!=null));
        RequiresDecision=selectedInstallKeys.Count>0||issues.Any(issue=>issue.Candidate!=null||issue.CanRepair);
        var lines=summary.ToArray();
        ClientSize=new Size(900,Math.Min(600,250+lines.Length*27+issues.Count*35));
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(20,8,20,20)};
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        if(lines.Length>0){
            var overview=new TableLayoutPanel{Name="VerificationOverview",Dock=DockStyle.Top,AutoSize=true,
                ColumnCount=2,RowCount=lines.Length,Margin=new Padding(0,0,0,13)};
            overview.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            overview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            for(int index=0;index<lines.Length;index++){
                overview.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                overview.Controls.Add(new Label{Text=lines[index].Key,AutoSize=true,Font=GeneralStyle.Font(12,true),
                    Margin=new Padding(0,2,20,4)},0,index);
                overview.Controls.Add(new Label{Text=lines[index].Value,AutoSize=true,
                    Margin=new Padding(0,2,0,4)},1,index);
            }
            layout.Controls.Add(overview,0,0);
        }
        var table=BuildResults(issues,categories,uncategorized);
        var view=new GeneralSurface{Dock=DockStyle.Fill,Padding=new Padding(10),Margin=Padding.Empty};
        if(issues.Count>0)view.Controls.Add(new GeneralScrollView(table));
        else view.Controls.Add(new Label{Text=GeneralStyle.T("Nenhuma pendência local apontada pela verificação.","No local issues reported by the check."),Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter});
        layout.Controls.Add(view,0,1);
        var commands=GeneralTableDialogLayout.Commands();
        var done=GeneralStyle.Button(GeneralStyle.T("Fechar","Close"),()=>Complete(DialogResult.Cancel));
        var browser=GeneralStyle.Button(GeneralStyle.T("Ver arquivo publicado","View published archive"),()=>{
            var issue=table.CurrentRow==null?null:table.CurrentRow.Tag as GeneralDependencyIssue;
            if(issue==null||!GeneralModDownload.IsWebSource(issue.Url))return;
            try{open(issue.Url);}catch(Exception ex){Message(this,ex.Message,Text);}
        });
        browser.Name="OpenDependencyPage";
        var installedMod=GeneralStyle.Button(GeneralStyle.T("Ver mod instalado","View installed mod"),()=>{
            var issue=table.CurrentRow==null?null:table.CurrentRow.Tag as GeneralDependencyIssue;
            if(issue!=null&&issue.InstalledModId!=null&&viewInstalled!=null)viewInstalled(issue.InstalledModId);
        });
        installedMod.Name="ViewInstalledDependency";
        Button addChoice=null;
        addChoice=GeneralStyle.Button(GeneralStyle.T("Adicionar opção","Add option"),()=>{
            var issue=table.CurrentRow==null?null:table.CurrentRow.Tag as GeneralDependencyIssue;
            if(issue==null||issue.Candidate==null||add==null)return;
            try{add(issue.Candidate);issue.Candidate=null;addChoice.Enabled=false;}catch(Exception ex){Message(this,ex.Message,Text);}
        });
        var repairButton=GeneralStyle.Button(GeneralStyle.T("Revisar substituição","Review replacement"),()=>{
            var issue=table.CurrentRow==null?null:table.CurrentRow.Tag as GeneralDependencyIssue;
            if(issue==null||!issue.CanRepair||repair==null)return;
            repair(issue);Complete(DialogResult.OK);
        });
        repairButton.Name="ReviewModReplacement";
        browser.Enabled=addChoice.Enabled=repairButton.Enabled=installedMod.Enabled=false;
        table.CurrentCellChanged+=(s,e)=>{
            var issue=table.CurrentRow==null?null:table.CurrentRow.Tag as GeneralDependencyIssue;
            browser.Enabled=issue!=null&&GeneralModDownload.IsWebSource(issue.Url);
            addChoice.Enabled=issue!=null&&issue.Candidate!=null;
            installedMod.Enabled=issue!=null&&issue.InstalledModId!=null;
            if(repair!=null)repairButton.Enabled=issue!=null&&issue.CanRepair;
        };
        commands.Controls.Add(done);
        browser.Visible=issues.Any(issue=>GeneralModDownload.IsWebSource(issue.Url));
        commands.Controls.Add(browser);
        if(viewInstalled!=null&&issues.Any(issue=>issue.InstalledModId!=null))commands.Controls.Add(installedMod);
        else installedMod.Dispose();
        if(repair!=null)commands.Controls.Add(repairButton);else repairButton.Dispose();
        if(add!=null&&issues.Any(issue=>issue.Candidate!=null))commands.Controls.Add(addChoice);
        if(selectedInstallKeys.Count>0){
            var install=GeneralStyle.Button("",()=>Complete(DialogResult.OK),true);
            install.Name="InstallCatalogDependencies";
            Action refresh=()=>{
                install.Text=GeneralStyle.T("Instalar dependências","Install dependencies")+" ("+selectedInstallKeys.Count+")";
                install.Enabled=selectedInstallKeys.Count>0;
            };
            table.CellContentClick+=(s,e)=>{if(e.ColumnIndex==0&&e.RowIndex>=0)ToggleInstall(table,e.RowIndex,refresh);};
            table.KeyDown+=(s,e)=>{
                if(e.KeyCode!=Keys.Space||table.CurrentRow==null)return;
                ToggleInstall(table,table.CurrentRow.Index,refresh);
                e.SuppressKeyPress=true;
            };
            refresh();commands.Controls.Add(install);
        }
        layout.Controls.Add(commands,0,2);
        Content.Controls.Add(layout);CancelButton=done;GeneralStyle.Apply(this,GeneralStyle.Dark);
    }

    GeneralModWindow.ModListGrid BuildResults(IReadOnlyList<GeneralDependencyIssue> issues,IEnumerable<string> categories,string uncategorized){
        var table=GeneralTableDialogLayout.ReadOnlyTable("DependencyResults",GeneralStyle.T("Pendências","Issues"));
        table.Columns.Add(new DataGridViewCheckBoxColumn{Name="Mark",HeaderText="",Width=44,SortMode=DataGridViewColumnSortMode.NotSortable});
        foreach(string title in new[]{GeneralStyle.T("Mod","Mod"),GeneralStyle.T("Dependência","Dependency"),GeneralStyle.T("Situação","Status")})
            table.Columns.Add(new DataGridViewTextBoxColumn{HeaderText=title,AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill,
                SortMode=DataGridViewColumnSortMode.NotSortable});
        var groups=issues.ToLookup(issue=>issue.Category??"");
        var order=new[]{""}.Concat(categories??Enumerable.Empty<string>()).Concat(groups.Select(group=>group.Key)).Distinct();
        var headingFont=GeneralStyle.Font(13,true);
        table.Disposed+=(s,e)=>headingFont.Dispose();
        foreach(string category in order.Where(category=>groups.Contains(category))){
            if(categories!=null){
                var heading=table.Rows[table.Rows.Add()];
                heading.Tag=category.Length==0?(uncategorized??GeneralStyle.T("Sem categoria","Uncategorized")):category;
                heading.Height=46;
            }
            foreach(var issue in groups[category]){
                var row=table.Rows[table.Rows.Add(issue.InstallKey!=null,issue.Mod,issue.Requirement,issue.Status)];
                row.Tag=issue;
                foreach(DataGridViewCell cell in row.Cells.Cast<DataGridViewCell>().Skip(1))
                    cell.ToolTipText=Convert.ToString(cell.Value);
            }
        }
        table.RowPrePaint+=(s,e)=>{
            string heading=table.Rows[e.RowIndex].Tag as string;
            if(heading==null)return;
            e.Handled=true;
            using(var fill=new SolidBrush(GeneralStyle.Surface))e.Graphics.FillRectangle(fill,e.RowBounds);
            var bounds=e.RowBounds;bounds.X+=16;bounds.Width-=16;
            TextRenderer.DrawText(e.Graphics,heading,headingFont,bounds,GeneralStyle.Foreground,
                TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);
        };
        table.CellPainting+=(s,e)=>{
            if(e.RowIndex<0||e.ColumnIndex!=0)return;
            var issue=table.Rows[e.RowIndex].Tag as GeneralDependencyIssue;
            if(issue!=null&&issue.InstallKey!=null)return;
            e.PaintBackground(e.ClipBounds,true);
            e.Handled=true;
        };
        table.CellMouseEnter+=(s,e)=>{
            if(e.RowIndex<0||!(table.Rows[e.RowIndex].Tag is GeneralDependencyIssue))return;
            table.Rows[e.RowIndex].DefaultCellStyle.BackColor=GeneralStyle.Selection;
            table.Cursor=Cursors.Hand;
        };
        table.CellMouseLeave+=(s,e)=>{
            if(e.RowIndex<0)return;
            table.Rows[e.RowIndex].DefaultCellStyle.BackColor=GeneralStyle.Surface;
            table.Cursor=Cursors.Default;
        };
        return table;
    }

    void ToggleInstall(DataGridView table,int row,Action refresh){
        var issue=table.Rows[row].Tag as GeneralDependencyIssue;
        if(issue==null||issue.InstallKey==null)return;
        bool marked=selectedInstallKeys.Add(issue.InstallKey);
        if(!marked)selectedInstallKeys.Remove(issue.InstallKey);
        foreach(DataGridViewRow item in table.Rows){
            var candidate=item.Tag as GeneralDependencyIssue;
            if(candidate!=null&&candidate.InstallKey==issue.InstallKey)item.Cells[0].Value=marked;
        }
        refresh();
    }
}
