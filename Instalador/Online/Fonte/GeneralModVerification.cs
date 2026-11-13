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
        readonly ConcurrentDictionary<string,Lazy<Task<GeneralSourceInfo>>> pages=new ConcurrentDictionary<string,Lazy<Task<GeneralSourceInfo>>>();
        readonly ConcurrentDictionary<string,Lazy<Task<List<GeneralNexusRequirement[]>>>> requirements=
            new ConcurrentDictionary<string,Lazy<Task<List<GeneralNexusRequirement[]>>>>();
        internal readonly List<Work> Checked=new List<Work>();
        internal ScanReads Reads;
        internal bool InventoryUnavailable;
        internal int MetadataChanged,UnavailableMetadata,UnavailableRequirements;

        internal ProjectVerification(bool online) { this.online=online; }
        static IEnumerable<GeneralNexusLink> Sources(Work work) {
            return work.Variant.Sources.Select(GeneralNexusLink.Parse).Where(link=>link!=null);
        }
        static string Page(GeneralNexusLink link) { return new GeneralNexusLink{Game=link.Game,Mod=link.Mod}.Url; }
        static async Task<TResult> Lookup<TResult>(Func<Task<TResult>> read) where TResult:class {
            try{return await read();}catch(Exception){return null;}
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
        internal void Record(Work work) {
            Checked.Add(work);
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
        internal IEnumerable<GeneralNexusRequirement[]> Requirements(Work work) {
            foreach(var link in Sources(work)){
                Lazy<Task<List<GeneralNexusRequirement[]>>> request;
                var result=requirements.TryGetValue(link.Url,out request)?request.Value.Result:null;
                if(result==null){UnavailableRequirements++;continue;}
                foreach(var item in result)yield return item;
            }
        }
    }

    async Task VerifyProject() {
        if(projectDraft!=null&&!ConfirmProjectNavigation())return;
        if(working||scanning)return;
        if(String.IsNullOrWhiteSpace(state.Destination)||!Directory.Exists(state.Destination)){
            detail.Text=T("Selecione a pasta de instalação para verificar.","Select the installation folder to verify.");
            return;
        }
        try {
            var audit=new ProjectVerification(!testMode);
            await Scan(audit);
            if(IsDisposed||audit.Reads==null)return;
            await ReviewInventoryLinks();
            if(IsDisposed)return;
            var install=new Dictionary<string,Work>(StringComparer.Ordinal);
            var issues=ReviewDependencies(audit,install);
            var summary=String.Format(T("{0} mods verificados · {1} com tags atualizadas","{0} mods checked · {1} with updated tags"),
                audit.Checked.Count,audit.MetadataChanged);
            if(audit.UnavailableMetadata>0)
                summary+="\r\n"+String.Format(T("Tags do Nexus indisponíveis em {0} fonte(s).","Nexus tags unavailable for {0} source(s)."),audit.UnavailableMetadata);
            if(audit.UnavailableRequirements>0)
                summary+="\r\n"+(GeneralNexusInfo.HasTestKey
                    ?String.Format(T("Dependências do Nexus não verificadas em {0} fonte(s).","Nexus dependencies unverified for {0} source(s)."),audit.UnavailableRequirements)
                    :T("Dependências online do Nexus exigem autenticação.","Online Nexus dependencies require authentication."));
            if(audit.InventoryUnavailable)
                summary+="\r\n"+T("Não foi possível ler os arquivos fora da lista.","Could not read files outside the list.");
            detail.Text=issues.Count==0?T("Verificação concluída.","Verification complete."):T("Verificação concluída com dependências para revisar.","Verification complete with dependencies to review.");
            using(var dialog=new GeneralDependencyReview(summary,issues,install.Count,launch,DisplayedInventory().Where(file=>file.Kind=="found").ToArray(),AddReviewedDependency)){
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            }
            string root=Root();
            var selected=ChooseAlternatives(install.Values.ToList(),entries=>{
                using(var dialog=new GeneralModAlternativeDialog(entries))
                    return dialog.ShowDialog(this)==DialogResult.OK?dialog.SelectedId:null;
            });
            if(selected==null||selected.Count==0)return;
            selected=WithLinkedDependencies(selected);
            var pending=new List<Work>();
            List<string> downloads;
            scanning=true;Busy(true);
            try{downloads=await Task.Run(()=>PlanSelected(selected,root,pending));}
            finally{scanning=false;Busy(false);}
            if(IsDisposed)return;
            if(pending.Count>0){
                foreach(var work in pending)work.DependencyQueue=true;
                ConfirmAndEnqueue(pending,root,downloads,false);
            }
        }catch(Exception ex){if(!IsDisposed)ShowError(ex);}
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
        var names=state.List.Mods.ToDictionary(mod=>mod.Id,mod=>mod.Name,StringComparer.Ordinal);
        var registered=new List<ModFileAnchor>();
        Action<ProjectDeleteFile,Func<string,IEnumerable<string>>> add=(file,owners)=>{
            foreach(string owner in file.OwnerKeys.SelectMany(owners).Distinct(StringComparer.Ordinal))
                registered.Add(new ModFileAnchor{Owner=owner,Path=file.Path,Hash=file.Hash});
        };
        Func<string,IEnumerable<string>> lookup=key=>byWork.ContainsKey(key)?new[]{byWork[key]}:new string[0];
        foreach(var file in Snapshot(destination).ProjectDeletionFiles(true,skipUnreadable:true))add(file,lookup);
        var exact=new HashSet<string>(state.List.Mods.SelectMany(mod=>mod.DetectedPaths),StringComparer.OrdinalIgnoreCase);
        var extras=new List<P2PSyncDifference>();
        var registeredNames=new Dictionary<string,string>(names,StringComparer.Ordinal);
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
            foreach(var file in engine.ProjectDeletionFiles(new HashSet<string>(imports.Select(item=>item.Key),StringComparer.Ordinal),true))
                add(file,key=>imports[key].SelectMany(lookup));
            foreach(var mod in state.List.Mods.Where(mod=>GeneralModLibrary.IsHystBody(mod.Id))){
                var files=new List<InstalledSnapshotFile>();
                var installed=InstalledSnapshot.DetectManualHyst(destination,mod,seed.Mods.FirstOrDefault(item=>item.Id==mod.Id),files,reads==null?null:(Func<string,string>)reads.Hash);
                if(installed==null)continue;
                foreach(var file in files)registered.Add(new ModFileAnchor{Owner=mod.Id,Path=file.Path,Hash=file.Sha256});
                extras.Add(new P2PSyncDifference{Kind="verified-variant",Mod=mod.Name,ModId=mod.Id,
                    VariantId=installed.Id,VerifiedBytes=files.Sum(file=>file.Size)});
            }
            exact.UnionWith(registered.Where(file=>names.ContainsKey(file.Owner)).Select(file=>file.Path));
            extras.AddRange(engine.ReadUnlistedModFiles(exact,data,reads==null?null:(Func<string,string>)reads.Hash));
            foreach(var owner in engine.OwnershipNames())
                if(!registeredNames.ContainsKey(owner.Key))registeredNames.Add(owner.Key,owner.Value);
        }
        foreach(var file in Generic(destination).ProjectDeletionFiles(true))add(file,key=>lookup(WorkKeyFromReceipt(key)));
        Func<string,string> scope=cyberpunk?(Func<string,string>)FixedInstaller.ModFolder:InstalledFileOwnership.GenericScope;
        var associated=InstalledFileOwnership.FindAssociated(destination,registered,scope,
            cyberpunk?extras.Where(file=>file.Kind=="found").Select(file=>file.Path):null);
        var labels=zipLabels??new Dictionary<string,InventoryZipLabel>(InventoryPaths);
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
            }catch(IOException){}catch(UnauthorizedAccessException){}
        }
        var observedScopes=cyberpunk
            ? GroupModFolders(destination,extras,registered,registeredNames,names,exact,labels,reads==null?(Func<string,string>)Safe.Hash:reads.Hash)
            : new HashSet<string>(InventoryPaths);
        extras.RemoveAll(file=>file.Kind=="found"&&file.VerifiedZip==null&&file.ProvenOwner==null&&!file.LocalInModFolder&&
            associated.ContainsKey(file.Path)&&names.ContainsKey(associated[file.Path])&&!labels.ContainsKey(file.Path)&&
            !observedScopes.Contains(scope(file.Path)));
        foreach(var pair in associated.Where(item=>names.ContainsKey(item.Value)&&!labels.ContainsKey(item.Key)&&
            !observedScopes.Contains(scope(item.Key))))
            extras.Add(new P2PSyncDifference{Kind="associated",Mod=names[pair.Value],ModId=pair.Value,Path=pair.Key});
        return extras;
    }

    static HashSet<string> GroupModFolders(string root,List<P2PSyncDifference> inventory,IEnumerable<ModFileAnchor> registered,
        Dictionary<string,string> names,Dictionary<string,string> projectNames,HashSet<string> listed,
        Dictionary<string,InventoryZipLabel> labels,Func<string,string> readHash) {
        var files=inventory.Where(file=>file.Kind=="found").ToDictionary(file=>file.Path,InventoryPaths);
        var confirmed=new Dictionary<string,string>(InventoryPaths);
        foreach(var file in files.Values){
            string owner=file.VerifiedZip!=null?file.InventoryGroupId:file.ProvenOwner;
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
        var knownZipOwners=labels.Where(pair=>FixedInstaller.ModFolder(pair.Key)!=null)
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

    List<GeneralDependencyIssue> ReviewDependencies(ProjectVerification audit,Dictionary<string,Work> install) {
        var preset=ActivePreset;
        var entries=state.List.Mods.Select(MakeWork).ToList();
        var members=preset==null?null:new HashSet<string>(preset.ModIds,StringComparer.Ordinal);
        var recipes=entries.Where(work=>work.Variant.CyberpunkId!=null)
            .ToLookup(work=>work.Variant.CyberpunkId,StringComparer.Ordinal);
        var sources=entries.SelectMany(work=>work.Variant.Sources.Select(GeneralNexusLink.Parse)
            .Where(link=>link!=null).Select(link=>new{Work=work,Link=link}))
            .ToLookup(item=>item.Link.Game+"/"+item.Link.Mod,StringComparer.Ordinal);
        var issues=new List<GeneralDependencyIssue>();
        bool changed=false;
        foreach(var work in audit.Checked.Where(work=>members==null||members.Contains(work.Entry.Id)).ToArray()){
            if(work.Variant.CyberpunkId!=null){
                foreach(var recipe in ExpandWork(work,state.Destination).Where(recipe=>recipe.Id!=work.Variant.CyberpunkId)){
                    var dependency=recipes[recipe.Id].FirstOrDefault(candidate=>members==null||members.Contains(candidate.Entry.Id))
                        ??recipes[recipe.Id].FirstOrDefault();
                    if(dependency==null){
                        var existing=state.List.Mods.FirstOrDefault(entry=>entry.Id==recipe.Id&&Selected(entry).CyberpunkId==recipe.Id);
                        if(existing!=null)dependency=MakeWork(existing);
                    }
                    bool listed=dependency!=null&&(members==null||members.Contains(dependency.Entry.Id));
                    bool installed=audit.Reads.Cyberpunk&&audit.Reads.Fixed(recipe).IsCorrect;
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
                    string dependencyStatus=null;
                    if(dependency!=null)statuses.TryGetValue(dependency.Key,out dependencyStatus);
                    if(!installed&&dependency!=null&&audit.Reads.Cyberpunk&&availableEdition&&dependencyStatus!=GeneralStatusPresentation.Disabled)
                        install[dependency.Key]=dependency;
                    string url=(dependency==null?recipe.Assets.Select(asset=>asset.ModId>0?
                        new GeneralNexusLink{Game="cyberpunk2077",Mod=asset.ModId,File=asset.FileId}.Url:asset.Url):
                        dependency.Variant.Sources).FirstOrDefault(GeneralModDownload.IsWebSource);
                    issues.Add(new GeneralDependencyIssue{Mod=work.Entry.Name,Requirement=recipe.Name,Url=url,
                        Status=installed?T("Instalado · fora do preset","Installed · outside preset"):
                            !availableEdition?T("Requer Phantom Liberty","Requires Phantom Liberty"):
                            !audit.Reads.Cyberpunk?T("Destino incompatível","Incompatible destination"):
                            dependencyStatus==GeneralStatusPresentation.Disabled?T("Desabilitada","Disabled"):
                            T("Falta instalar","Installation missing")});
                }
            }
            foreach(var group in audit.Requirements(work)){
                bool present=group.Any(target=>sources[target.Source.Game+"/"+target.Source.Mod].Any(item=>{
                    string status;
                    return (members==null||members.Contains(item.Work.Entry.Id))&&
                        (item.Link.File==target.MinFile||target.MaxFile>0&&item.Link.File==target.MaxFile)&&
                        statuses.TryGetValue(item.Work.Key,out status)&&GeneralStatusPresentation.Kind(status)=="installed";
                }));
                if(present)continue;
                bool chooseMod=group.Select(item=>item.Source.Url).Distinct().Count()>1;
                foreach(var target in group.GroupBy(item=>Tuple.Create(item.Source.Url,item.MinFile,item.MaxFile)).Select(items=>items.First())){
                    var candidates=sources[target.Source.Game+"/"+target.Source.Mod];
                    var existing=candidates.Select(item=>item.Work.Entry).FirstOrDefault()
                        ??state.List.Mods.FirstOrDefault(entry=>entry.DependencySource==target.Source.Url||
                            Selected(entry).Sources.Select(GeneralNexusLink.Parse).Any(link=>link!=null&&
                                link.Mod==target.Source.Mod&&link.Game==target.Source.Game));
                    bool missing=existing==null;
                    if(!chooseMod&&missing&&CanAddDependency(preset)){
                        var entry=CreateDependency(target,preset);
                        if(group.Length>1)entry.DependencyFile=0;
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
                    bool foundDifferent=candidates.Any(item=>{
                        string status;
                        return statuses.TryGetValue(item.Work.Key,out status)&&GeneralStatusPresentation.Kind(status)=="installed";
                    });
                    issues.Add(new GeneralDependencyIssue{Mod=work.Entry.Name,Requirement=target.Name+" · "+range,Url=url,
                        Candidate=chooseMod&&missing?target:null,
                        Status=chooseMod?T("Uma das opções","One of these options"):missing?T("Escolher versão","Choose version"):
                            foundDifferent?T("Faixa não confirmada","Version range unconfirmed"):T("Verificar arquivo requerido","Check required file")});
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

    GeneralModEntry CreateDependency(GeneralNexusRequirement target,GeneralModPreset preset) {
        var entry=NewEntry(target.Name,target.Source.Url);
        entry.DependencySource=target.Source.Url;entry.Nsfw=target.Nsfw;
        entry.DependencyFile=target.MinFile==target.MaxFile?target.MinFile:0;
        entry.Tags.Add("Dependência detectada");
        entry.Variants[0].Name=T("Indefinida","Undefined");
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
    internal string Mod,Requirement,Status,Url;
    internal GeneralNexusRequirement Candidate;
}

internal sealed class GeneralDependencyReview : GeneralDialog {
    internal GeneralDependencyReview(string summary,IReadOnlyList<GeneralDependencyIssue> issues,int installable,Action<string> open,
        IReadOnlyList<P2PSyncDifference> inventory=null,Action<GeneralNexusRequirement> add=null)
        :base(GeneralStyle.T("Resultado da verificação","Verification results")) {
        RequiresDecision=installable>0||add!=null;
        ClientSize=new Size(860,Math.Min(550,290+issues.Count*35));
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(20,8,20,20)};
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label{Text=summary,Dock=DockStyle.Top,AutoSize=true,Margin=new Padding(0,0,0,12)},0,0);
        var table=new GeneralModWindow.ModListGrid{Name="DependencyResults",Dock=DockStyle.Fill,ReadOnly=true,
            AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,RowHeadersVisible=false,
            MultiSelect=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,
            ColumnHeadersHeight=36,EnableHeadersVisualStyles=false,BackgroundColor=GeneralStyle.Surface,BorderStyle=BorderStyle.None};
        foreach(string name in new[]{GeneralStyle.T("Mod","Mod"),GeneralStyle.T("Dependência","Dependency"),GeneralStyle.T("Situação","Status")})
            table.Columns.Add(new DataGridViewTextBoxColumn{HeaderText=name,SortMode=DataGridViewColumnSortMode.NotSortable});
        table.DefaultCellStyle=new DataGridViewCellStyle{BackColor=GeneralStyle.Surface,ForeColor=GeneralStyle.Foreground,
            SelectionBackColor=GeneralStyle.Selection,SelectionForeColor=GeneralStyle.Foreground,Font=GeneralStyle.Font(11),Padding=new Padding(8,0,8,0)};
        table.ColumnHeadersDefaultCellStyle=new DataGridViewCellStyle{BackColor=GeneralStyle.Surface,ForeColor=GeneralStyle.Muted,
            Font=GeneralStyle.Font(11,true),Padding=new Padding(8,0,8,0)};
        table.RowTemplate.Height=36;
        foreach(var issue in issues){
            var row=table.Rows[table.Rows.Add(issue.Mod,issue.Requirement,issue.Status)];
            row.Tag=issue;
            foreach(DataGridViewCell cell in row.Cells)cell.ToolTipText=Convert.ToString(cell.Value);
        }
        var view=new GeneralSurface{Dock=DockStyle.Fill,Padding=new Padding(10),Margin=Padding.Empty};
        if(issues.Count>0)view.Controls.Add(new GeneralScrollView(table));
        else view.Controls.Add(new Label{Text=GeneralStyle.T("Nenhuma pendência no catálogo local.","No missing dependencies in the local catalog."),Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter});
        layout.Controls.Add(view,0,1);
        var commands=GeneralTableDialogLayout.Commands();
        var done=GeneralStyle.Button(GeneralStyle.T("Fechar","Close"),()=>Complete(DialogResult.Cancel));
        var browser=GeneralStyle.Button(GeneralStyle.T("Abrir página","Open page"),()=>{
            var issue=table.CurrentRow==null?null:table.CurrentRow.Tag as GeneralDependencyIssue;
            if(issue==null||!GeneralModDownload.IsWebSource(issue.Url))return;
            try{open(issue.Url);}catch(Exception ex){Message(this,ex.Message,Text);}
        });
        browser.Name="OpenDependencyPage";
        Button addChoice=null;
        addChoice=GeneralStyle.Button(GeneralStyle.T("Adicionar opção","Add option"),()=>{
            var issue=table.CurrentRow==null?null:table.CurrentRow.Tag as GeneralDependencyIssue;
            if(issue==null||issue.Candidate==null||add==null)return;
            try{add(issue.Candidate);issue.Candidate=null;addChoice.Enabled=false;}catch(Exception ex){Message(this,ex.Message,Text);}
        });
        browser.Enabled=addChoice.Enabled=false;
        table.CurrentCellChanged+=(s,e)=>{
            var issue=table.CurrentRow==null?null:table.CurrentRow.Tag as GeneralDependencyIssue;
            browser.Enabled=issue!=null&&GeneralModDownload.IsWebSource(issue.Url);
            addChoice.Enabled=issue!=null&&issue.Candidate!=null;
        };
        commands.Controls.Add(done);commands.Controls.Add(browser);
        if(add!=null&&issues.Any(issue=>issue.Candidate!=null))commands.Controls.Add(addChoice);
        if(inventory!=null&&inventory.Count>0){
            var outside=GeneralStyle.Button(GeneralStyle.T("Fora da lista","Outside the list")+" ("+inventory.Count+")",()=>{
                using(var dialog=new GeneralSyncDifferencesDialog(inventory,true))dialog.ShowDialog(this);
            });
            outside.Name="ShowUnlistedInventory";
            commands.Controls.Add(outside);
        }
        if(installable>0){
            var install=GeneralStyle.Button(GeneralStyle.T("Instalar do catálogo","Install from catalog")+" ("+installable+")",()=>Complete(DialogResult.OK),true);
            install.Name="InstallCatalogDependencies";
            commands.Controls.Add(install);
        }
        layout.Controls.Add(commands,0,2);
        Content.Controls.Add(layout);CancelButton=done;GeneralStyle.Apply(this,GeneralStyle.Dark);
    }
}
