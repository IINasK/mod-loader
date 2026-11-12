using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    Button primaryAction;
    Work pendingVersion;
    enum ModAction { None, Disable, ChooseDependency, Enable, Change, Update, Install, Download }

    static bool IsBigBody(GeneralModEntry mod) {
        return mod.AlternativeGroup==GeneralModLibrary.BodyGroup&&mod.Id!=GeneralModLibrary.VanillaFeetId;
    }

    string ModDisplayName(GeneralModEntry mod) {
        if(!IsBigBody(mod))return mod.Name;
        GeneralAlternativeGroup group;
        return state.List.AlternativeGroups.TryGetValue(GeneralModLibrary.BodyGroup,out group)
            ?group.DisplayName??"Big Body":"Big Body";
    }

    GeneralModEntry BigBodyRepresentative(IEnumerable<GeneralModEntry> entries) {
        var body=entries.Where(IsBigBody).ToList();
        string preferred=state.BodyChoice==GeneralModLibrary.NaturalBody?GeneralModLibrary.EbbBody:state.BodyChoice;
        return body.FirstOrDefault(mod=>mod.Id==preferred)??body.FirstOrDefault(IsActiveAlternative)??body.FirstOrDefault();
    }

    List<Work> BigBodyVersions(IEnumerable<GeneralModEntry> entries,bool dlc) {
        var versions=new List<Work>();
        foreach(var entry in entries.Where(IsBigBody)){
            var variant=Selected(entry);
            var active=VersionWorks(entry).OrderByDescending(work=>work.Variant.Sources.Count).FirstOrDefault(work=>{
                string status;
                return statuses.TryGetValue(work.Key,out status)&&InstalledVersion(status);
            });
            if(!state.ChosenVariants.ContainsKey(entry.Id)&&active!=null)variant=entry.Variants.Single(option=>option.Id==active.Variant.Id);
            else if(!state.ChosenVariants.ContainsKey(entry.Id)){
                var saved=entry.Variants.FirstOrDefault(option=>{
                    string status;
                    return statuses.TryGetValue(Key(entry,option),out status)&&status==GeneralStatusPresentation.Disabled;
                });
                variant=saved??entry.Variants.FirstOrDefault(option=>option.Id==(dlc?"with-refit-dlc":"with-refit-base"))??variant;
            }
            if(!dlc&&variant.Id=="with-refit-dlc")
                variant=entry.Variants.FirstOrDefault(option=>option.Id=="with-refit-base")??variant;
            versions.Add(new Work{Entry=entry,Variant=variant,BodyChoice=entry.Id,SwitchAlternatives=true,SaveVariant=true});
            // EBBN is a shape in the EBB + RB archive. A separate choice keeps the shared files under one owner.
            if(entry.Id==GeneralModLibrary.EbbBody)
                versions.Add(new Work{Entry=entry,Variant=variant.NaturalOption??variant,BodyChoice=GeneralModLibrary.NaturalBody,SwitchAlternatives=true,SaveVariant=true});
        }
        return versions.OrderBy(work=>work.BodyChoice==GeneralModLibrary.NaturalBody?2:work.BodyChoice==GeneralModLibrary.PushBody?1:0).ToList();
    }

        static string BodyChoiceLabel(Work work){
        return BodyChoiceLabel(work.BodyChoice)??work.Variant.Name;
    }

    static string BodyChoiceLabel(string choice){
        return choice==GeneralModLibrary.NaturalBody?"EBBN + RB":choice==GeneralModLibrary.PushBody?"EBBP + RB":choice==GeneralModLibrary.EbbBody?"EBB + RB":null;
    }

    string ActiveBodyChoice(GeneralModEntry entry){
        return entry.Id==GeneralModLibrary.EbbBody&&state.BodyChoice==GeneralModLibrary.NaturalBody
            ?GeneralModLibrary.NaturalBody:entry.Id;
    }

    bool VersionIsActive(Work work) {
        string status;
        if(!statuses.TryGetValue(work.Key,out status)||!InstalledVersion(status))return false;
        if(work.Entry.Id!=GeneralModLibrary.EbbBody||work.BodyChoice==null)return true;
        var regular=work.Entry.Variants.Single(variant=>variant.Id==work.Variant.Id);
        if(regular.NaturalOption!=null&&!GeneralModLibrary.SamePayload(regular,regular.NaturalOption))return true;
        return work.BodyChoice==ActiveBodyChoice(work.Entry);
    }

    static bool InstalledVersion(string status) {
        return status=="Instalado"||status=="Instalado correto"||status==GeneralStatusPresentation.ManualBody||
            status==GeneralStatusPresentation.NexusManual;
    }

    void SelectVersion(Work choice){
        if(working||scanning||associationChoice!=null)return;
        if(SameVersion(MakeWork(choice.Entry),choice)&&(choice.Entry.AlternativeGroup==null||
            (ActivePreset!=null?PresetWorks().Any(w=>w.Entry.Id==choice.Entry.Id):IsActiveAlternative(choice.Entry))))return;
        var scope=PresetEditScope(choice.Entry);if(!scope.HasValue)return;
        GeneralPresetProfiles.Capture(state);
        foreach(var p in state.List.Presets.Where(p=>p.ModIds.Contains(choice.Entry.Id)&&(scope.Value||p.Id==state.SelectedPreset))){
            if(!p.Configurations[choice.Entry.Id].Variants.Any(v=>v.Id==choice.Variant.Id))continue;
            p.VariantChoices[choice.Entry.Id]=choice.Variant.Id;
            if(choice.Entry.AlternativeGroup!=null)p.AlternativeChoices[choice.Entry.AlternativeGroup]=choice.Entry.Id;
            if(choice.BodyChoice!=null)p.BodyChoice=choice.BodyChoice;
        }
        if(ActivePreset!=null){
            state.ChosenVariants[choice.Entry.Id]=choice.Variant.Id;
            if(choice.BodyChoice!=null)state.BodyChoice=choice.BodyChoice;
        }
        Persist();
        pendingVersion=choice;
        choice.SwitchAlternatives=true;
        choice.SaveVariant=true;
        DrawRows();
        RefreshPrimaryAction();
    }

    IEnumerable<Work> VersionWorks(GeneralModEntry entry) {
        foreach(var variant in entry.Variants){
            yield return new Work{Entry=entry,Variant=variant,BodyChoice=IsBigBody(entry)?entry.Id:null};
            if(variant.NaturalOption!=null&&!GeneralModLibrary.SamePayload(variant,variant.NaturalOption))
                yield return new Work{Entry=entry,Variant=variant.NaturalOption,BodyChoice=GeneralModLibrary.NaturalBody};
        }
    }

    List<Work> SelectedJobs() {
        var jobs = Marked();
        var focused=FocusedEntry();
        var choice=pendingVersion;
        if(choice!=null&&state.List.Mods.Contains(choice.Entry)&&
            grid.CurrentRow!=null&&grid.CurrentRow.Tag is Work&&SameVersion((Work)grid.CurrentRow.Tag,choice))
            return new List<Work>{choice};
        if(jobs.Count==0&&focused!=null)jobs.Add(MakeWork(focused));
        int index=jobs.FindIndex(work=>IsBigBody(work.Entry));
        if(index>=0){
            var entry=jobs[index].Entry;
            var chosen=BigBodyVersions(new[]{entry},IsCyberpunk(state.Destination)&&GameEdition.HasPL(state.Destination))
                .Single(work=>work.BodyChoice==ActiveBodyChoice(entry));
            jobs[index]=chosen;
            jobs.RemoveAll(work=>work.Entry.Id==GeneralModLibrary.VanillaFeetId||
                chosen.Entry.Id!=GeneralModLibrary.EbbBody&&work.Entry.Id==GeneralModLibrary.EclipseTattooId);
        }
        return jobs;
    }

    static bool SameVersion(Work left,Work right) {
        return left.Key==right.Key&&left.BodyChoice==right.BodyChoice;
    }

    ModAction ActionFor(Work work, HashSet<string> selectedIds) {
        if(presetProtected.Contains(work.Entry.Id))return ModAction.None;

        string status;
        statuses.TryGetValue(work.Key, out status);
        if(work==pendingVersion&&!VersionIsActive(work)&&
            (status==GeneralStatusPresentation.Disabled||VersionWorks(work.Entry).Any(VersionIsActive)||
             work.Entry.AlternativeGroup!=null&&state.List.Mods.Any(other=>
                 other.AlternativeGroup==work.Entry.AlternativeGroup&&IsActiveAlternative(other))))
            return ModAction.Update;
        if(status==GeneralStatusPresentation.NexusManual)return ModAction.None;
        if(GeneralModLibrary.NeedsDependencyVersion(work.Entry,work.Variant))return ModAction.ChooseDependency;
        bool active = GeneralStatusPresentation.Kind(status) == "installed";

        if (active) {
            if (work.Variant.CyberpunkId == null || !Recipe(work).Immutable) return ModAction.Disable;
            return ModAction.None;
        }
        if (status == GeneralStatusPresentation.ManualBody||
            work.Entry.DetectedPaths.Count>0&&work.Variant.Sources.Any(GeneralModDownload.IsInstalledSource)) return ModAction.None;
        var alternative = state.List.Mods.FirstOrDefault(other => other.Id != work.Entry.Id && work.Entry.AlternativeGroup != null
            && other.AlternativeGroup == work.Entry.AlternativeGroup && IsActiveAlternative(other));
        if (alternative != null)
            return selectedIds.Contains(alternative.Id) ? ModAction.None : ModAction.Change;
        if (status == GeneralStatusPresentation.Disabled) return ModAction.Enable;
        if (status == "Atualização disponível" || status == "Versão diferente"
            || (status != null && status.StartsWith("Instalado: ", StringComparison.Ordinal))) return ModAction.Change;
        if (status == "Pronto para instalar" || (status == null && work.Local && work.Variant.Sources.All(File.Exists))) return ModAction.Install;
        return ModAction.Download;
    }

    bool IsActiveAlternative(GeneralModEntry entry) {
        return VersionWorks(entry).Any(work=>{
            string status;
            return statuses.TryGetValue(work.Key,out status)&&InstalledVersion(status);
        });
    }

    ModAction ClassifyJobs(List<Work> jobs, out List<Work> actionable) {
        var ids = new HashSet<string>(jobs.Select(work => work.Entry.Id), StringComparer.Ordinal);
        var actions = jobs.Select(work => new { Work = work, Action = ActionFor(work, ids) }).ToList();
        var command = actions.Count == 0 ? ModAction.None : actions.Max(item => item.Action);
        actionable = actions.Where(item => command == ModAction.Download
            ? item.Action == ModAction.Download || item.Action == ModAction.Install || item.Work.BodyChoice!=null&&item.Action==ModAction.Change
            : command != ModAction.None && item.Action == command).Select(item => item.Work).ToList();
        return command;
    }

    void RefreshPrimaryAction() {
        if (primaryAction == null || grid == null || drawing || working || scanning || associationChoice != null) return;
        if(String.IsNullOrWhiteSpace(state.Destination)){
            primaryAction.Text=T("Selecionar pasta","Select folder");
            primaryAction.Enabled=true;
            return;
        }
        if(NeedsPresetLoad){primaryAction.Text=T("Carregar preset","Load preset");primaryAction.Enabled=ActivePreset!=null;return;}
        List<Work> ignored;
        var action = ClassifyJobs(SelectedJobs(), out ignored);
        primaryAction.Text = action == ModAction.Disable ? T("Desabilitar", "Disable")
            : action == ModAction.ChooseDependency ? GeneralStatusPresentation.Label(GeneralStatusPresentation.ChooseDependency)
            : action == ModAction.Enable ? T("Reativar", "Enable")
            : action == ModAction.Update ? T("Atualizar", "Update")
            : action == ModAction.Change ? T("Mudar versão", "Change version")
            : action == ModAction.Install ? T("Instalar", "Install")
            : T("Baixar", "Download");
        primaryAction.Enabled = action != ModAction.None;
    }

    DisabledMods DisabledAt(string root) {
        return new DisabledMods(root, data, () => EnsureGameClosed(root));
    }

    void KeepLinkedDependencies(List<Work> jobs,string root,string replacementId=null) {
        var removing=new HashSet<string>(jobs.Select(work=>work.Entry.Id).Where(id=>id!=replacementId),StringComparer.Ordinal);
        var removingVariants=new HashSet<string>(jobs.Select(work=>work.Key),StringComparer.Ordinal);
        var affected=state.List.Mods.Where(mod=>
            mod.Variants.Any(variant=>variant.RequiredMods.Any(removing.Contains))).ToList();
        if(affected.Count==0)return;
        var reads=new ScanReads(this,root);
        foreach(var mod in affected)foreach(var variant in mod.Variants.Where(item=>item.RequiredMods.Any(removing.Contains))){
            if(removingVariants.Contains(Key(mod,variant)))continue;
            string status=Status(new Work{Entry=mod,Variant=variant},root,reads);
            if(GeneralStatusPresentation.Kind(status)=="installed"||status==GeneralStatusPresentation.ManualBody)
                throw new IOException(T("Inclua o mod dependente na mesma operação: ","Include the dependent mod in the same operation: ")+mod.Name);
        }
    }

    Dictionary<string, List<DisabledModFile>> PlanDisable(List<Work> jobs, string root,string replacementId=null) {
        EnsureGameClosed(root);
        KeepLinkedDependencies(jobs,root,replacementId);
        var result = jobs.ToDictionary(work => work.Key, work => new List<DisabledModFile>(), StringComparer.Ordinal);
        var selected = new HashSet<string>(result.Keys, StringComparer.Ordinal);
        bool cyberpunk = IsCyberpunk(root);
        var allFiles = Snapshot(root).ProjectDeletionFiles().ToList();
        foreach(var work in jobs.Where(work=>work.Key!=InstalledSnapshot.Owner(work.Entry,work.Variant))){
            string receipt;
            if(!TryWorkSnapshot(work,root,out receipt))continue;
            string owner=InstalledSnapshot.Owner(work.Entry,work.Variant);
            foreach(var file in allFiles.Where(file=>file.OwnerKeys.Contains(owner))){
                file.OwnerKeys.Remove(owner);
                file.OwnerKeys.Add(work.Key);
            }
        }
        var fixedIds = new HashSet<string>(jobs.Select(work => work.Variant.CyberpunkId).Where(id => id != null), StringComparer.Ordinal);
        var routedIds = new HashSet<string>(jobs.Where(work=>work.Variant.InstallRoutes.Count>0).Select(work=>work.Variant.CyberpunkId),StringComparer.Ordinal);
        var aliases=state.List.Mods.Where(entry=>entry.Id==GeneralModLibrary.EbbBody).SelectMany(VersionWorks)
            .SelectMany(work=>Enumerable.Range(0,work.Variant.Sources.Count)
                .Select(index=>new{Work=work,Legacy=P2PPack.LegacySlot(work.Entry,work.Variant,index)}))
            .Where(item=>item.Legacy!=null).ToDictionary(item=>item.Legacy,item=>item.Work.Key,StringComparer.Ordinal);
        Func<string,string> receiptOwner=key=>{
            string alias;
            return aliases.TryGetValue(key,out alias)?alias:WorkKeyFromReceipt(key);
        };
        foreach (var file in Generic(root).ProjectDeletionFiles()) {
            file.OwnerKeys = file.OwnerKeys.Select(receiptOwner).Distinct().ToList();
            allFiles.Add(file);
        }
        if (cyberpunk) {
            var importOwners = state.CyberpunkImports.ToLookup(item => item.Value, item => receiptOwner(item.Key), StringComparer.Ordinal);
            var imports = Cyberpunk(root).ProjectDeletionFiles(null).ToList();
            foreach (var file in imports) {
                string id = file.OwnerKeys.Single();
                file.OwnerKeys = importOwners[id].Concat(jobs.Where(work => work.Entry.Id == id).Select(work => work.Key)).Distinct().ToList();
                if (file.OwnerKeys.Count == 0) file.OwnerKeys.Add(id);
                allFiles.Add(file);
            }
            var fixedWork = jobs.Where(work => work.Variant.CyberpunkId != null).ToLookup(work => work.Variant.CyberpunkId);
            bool stockEquipment = EquipmentExOptions.StockActive(catalog, root);
            foreach (var package in catalog.Packages) {
                var recipe = EquipmentExOptions.Available(package) && stockEquipment ? EquipmentExOptions.Resolve(package,EquipmentExOptions.StockId) : package;
                if (package.Id == NightCityDensity.ModId) {
                    var density = NightCityDensity.Read(package, root);
                    if (!density.CanApply && !fixedIds.Contains(package.Id)) continue;
                    if (density.Id == "absent") continue;
                    recipe = NightCityDensity.Installed(package, root);
                }
                var files = recipe.Files.Where(file => !file.UserData).ToList();
                if (!files.Any(file => File.Exists(Safe.In(root, file.Path)))) continue;
                if (!fixedIds.Contains(package.Id) && FixedData.Expand(catalog, recipe.Dependencies,stockEquipment).Any(dep => fixedIds.Contains(dep.Id)))
                    throw new IOException(T("Ainda usado por: ", "Still used by: ") + package.Name);
                foreach (var file in files) {
                    string target = Safe.In(root, file.Path);
                    if (!File.Exists(target)) continue;
                    long size = new FileInfo(target).Length;
                    string hash = file.Sha256;
                    bool owned = !recipe.Immutable && fixedIds.Contains(package.Id) && !routedIds.Contains(package.Id);
                    if (owned) {
                        hash = Safe.Hash(target);
                        if (!FpvCustomization.MatchesFile(file, size, hash))
                            throw new IOException(T("Arquivo alterado: ", "Modified file: ") + file.Path);
                    }
                    allFiles.Add(new ProjectDeleteFile {
                        Path = file.Path, Hash = hash, Size = size, Owned = owned,
                        OwnerKeys = owned ? fixedWork[package.Id].Select(work => work.Key).ToList() : new List<string> { package.Id }
                    });
                }
            }
            foreach(var work in jobs.Where(work=>IsBigBody(work.Entry)&&GeneralModLibrary.IsHystBody(work.Entry.Id))){
                if(allFiles.Any(file=>file.OwnerKeys.Contains(work.Key)))continue;
                var verified=new List<InstalledSnapshotFile>();
                var known=seed.Mods.FirstOrDefault(mod=>mod.Id==work.Entry.Id);
                var active=InstalledSnapshot.DetectManualHyst(root,work.Entry,known,verified);
                if(active==null||active.Id!=work.Variant.Id)continue;
                allFiles.AddRange(verified.Select(file=>new ProjectDeleteFile{
                    Path=file.Path, Hash=file.Sha256, Size=file.Size, Owned=true, OwnerKeys=new List<string>{work.Key}
                }));
            }
        }

        var shared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (projectStore != null && project != null) {
            foreach (var peer in projectStore.Read().Projects.Where(item => item.Id != project.Id)) {
                var other = projectStore.Load(peer);
                if (String.IsNullOrWhiteSpace(other.Destination) ||
                    !String.Equals(Path.GetFullPath(other.Destination).TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue;
                string peerData = projectStore.DataPath(peer);
                var files = new InstalledSnapshotInstaller(root, peerData, catalog).ProjectDeletionFiles();
                if (!cyberpunk) files = files.Concat(new GeneralModInstaller(root, Path.Combine(peerData, "Instalacoes")).ProjectDeletionFiles());
                foreach (var file in files) shared.Add(file.Path);
                if (cyberpunk) foreach (var key in other.CyberpunkImports.Values)
                    foreach (var file in Cyberpunk(root).ProjectDeletionFiles(new HashSet<string>(new[]{key}))) shared.Add(file.Path);
            }
        }
        var inactive = new HashSet<string>(DisabledAt(root).Read().Keys, StringComparer.Ordinal);
        foreach (var file in allFiles) file.OwnerKeys.RemoveAll(inactive.Contains);
        var personalPeers=new Lazy<HashSet<string>>(ExternalFilesInOtherProjects);
        foreach (var group in allFiles.Where(file => file.OwnerKeys.Count > 0).GroupBy(file => file.Path, StringComparer.OrdinalIgnoreCase)) {
            if(GeneralInstallRoutes.External(group.Key)&&personalPeers.Value.Contains(GeneralInstallRoutes.Resolve(root,group.Key)))continue;
            var owners = group.SelectMany(file => file.OwnerKeys ?? new List<string>()).Distinct().ToArray();
            if (!owners.Any(selected.Contains) || owners.Any(owner => !selected.Contains(owner)) || shared.Contains(group.Key)) continue;
            if (group.Any(file => file.ReplacesPrevious))
                throw new IOException(T("Este mod substitui um arquivo preexistente: ", "This mod replaces a pre-existing file: ") + group.Key);
            if (group.Any(file => !file.Owned) || !DisabledMods.AllowedPath(group.Key)) continue;
            var first = group.First();
            if (group.Any(file => file.Hash != first.Hash || file.Size != first.Size))
                throw new IOException(T("Registros divergentes: ", "Conflicting installation records: ") + group.Key);
            foreach (var owner in owners) result[owner].Add(new DisabledModFile { Path = first.Path, Hash = first.Hash, Size = first.Size });
        }
        foreach (var work in jobs) if (result[work.Key].Count == 0)
            throw new IOException(work.Entry.Name + T(": nenhum arquivo exclusivo com registro verificado.", ": no exclusive files with a verified installation record."));
        if (cyberpunk && jobs.Any(work => work.Entry.Id == "hyst-ebb-rb") && !fixedIds.Contains(GeneralModLibrary.EclipseTattooId)
            && catalog.Packages.Any(package => package.Id == GeneralModLibrary.EclipseTattooId
                && package.Files.Any(file => !file.UserData && File.Exists(Safe.In(root, file.Path)))))
            throw new IOException(T("Inclua a tatuagem Eclipse para desabilitar este corpo.", "Include the Eclipse tattoo to disable this body."));
        return result;
    }

    static string WorkKeyFromReceipt(string key) {
        int first = key.IndexOf('/');
        int second = first < 0 ? -1 : key.IndexOf('/', first + 1);
        return second < 0 ? key : key.Substring(0, second);
    }

    async Task ToggleMods(List<Work> jobs, string root, bool enable) {
        working = true;
        Busy(true);
        try {
            await Task.Run(() => {
                var store = DisabledAt(root);
                if (enable) {
                    if (jobs.Where(work => work.Entry.AlternativeGroup != null).GroupBy(work => work.Entry.AlternativeGroup).Any(group => group.Count() > 1))
                        throw new IOException(T("Selecione só uma versão de cada mod alternativo.", "Select only one version from each alternative group."));
                    foreach (var work in jobs) {
                        if (ConflictingAlternatives(work, root).Count > 0)
                            throw new IOException(T("Use Mudar versão para trocar alternativas.", "Use Change version to switch alternatives."));
                        CheckCompatibilityGroups(work,root,new List<Work>(),jobs);
                        if (work.Variant.CyberpunkId != GeneralModLibrary.EclipseTattooId || !jobs.Any(item => item.Entry.Id == "hyst-ebb-rb"))
                            CheckHystBody(work, root);
                        if (work.Variant.CyberpunkId != null) {
                            var recipe = Recipe(work);
                            if (EquipmentExOptions.Available(recipe)) recipe = ProfileRecipe(work);
                            if (GameEdition.NeedsPL(recipe) && !GameEdition.HasPL(root))
                                throw new IOException(T("Requer Phantom Liberty.", "Requires Phantom Liberty."));
                            var batch = new HashSet<string>(jobs.Select(item => item.Variant.CyberpunkId), StringComparer.Ordinal);
                            var missing = FixedData.Expand(catalog, recipe.Dependencies,EquipmentExOptions.StockActive(catalog,root)).FirstOrDefault(dependency =>
                                !batch.Contains(dependency.Id) && !FixedData.Scan(dependency, root).IsCorrect);
                            if (missing != null) throw new IOException(T("Ative primeiro: ", "Enable first: ") + missing.Name);
                        }
                    }
                    store.Enable(jobs.Select(work => PresetStoredKey(work,root)));
                } else store.Disable(PlanDisable(jobs, root));
            });
            detail.Text = enable ? T("Mods reativados.", "Mods enabled.") : T("Mods desabilitados.", "Mods disabled.");
        } finally { working = false; Busy(false); }
        await Scan();
    }

    string RemoveManagedWorks(List<Work> jobs, string root, HashSet<string> disabled) {
        EnsureGameClosed(root);
        KeepLinkedDependencies(jobs,root);
        var notes = new List<string>();
        var personalPeers=new Lazy<HashSet<string>>(ExternalFilesInOtherProjects);
        Func<string,bool> preserveExternal=path=>personalPeers.Value.Contains(GeneralInstallRoutes.Resolve(root,path));
        var snapshots = jobs.Where(work => {
            string receipt;
            return work.Variant.CyberpunkId!="NightCoop"&&TryWorkSnapshot(work,root,out receipt);
        }).ToList();
        foreach (var group in snapshots.GroupBy(work => new {
            Receipt=state.SnapshotReceipts[InstalledSnapshot.Owner(work.Entry,work.Variant)], Disabled=disabled.Contains(work.Key)
        })) {
            notes.Add(Snapshot(root).Remove(group.Key.Receipt,group.Select(work=>InstalledSnapshot.Owner(work.Entry,work.Variant)),Report,group.Key.Disabled,preserveExternal));
            foreach (var work in group) state.SnapshotReceipts.Remove(InstalledSnapshot.Owner(work.Entry,work.Variant));
        }
        var standard = jobs.Except(snapshots).ToList();
        var fixedIds = standard.Where(work => work.Variant.CyberpunkId != null && work.Variant.InstallRoutes.Count==0 && !disabled.Contains(work.Key))
            .Select(work => work.Variant.CyberpunkId).Distinct().ToArray();
        if (fixedIds.Length > 0) {
            if (!IsCyberpunk(root)) throw new IOException(T("Selecione a pasta do Cyberpunk 2077.", "Select the Cyberpunk 2077 folder."));
            notes.Add(Cyberpunk(root).DeactivateMany(fixedIds, Report, keepBackup: false));
        }
        bool cyberpunk = IsCyberpunk(root);
        foreach(var work in standard.Where(item=>item.Variant.CyberpunkId!=null&&item.Variant.InstallRoutes.Count>0))
            using(Cyberpunk(root).LockForRouteChange())
                notes.Add(Generic(root).Remove(work.Receipt(0),disabled.Contains(work.Key),null,preserveExternal));
        foreach (var work in standard.Where(item => item.Variant.CyberpunkId == null)) {
            for (int index = work.Variant.Sources.Count - 1; index >= 0; index--) {
                string key = work.Receipt(index);
                if (!cyberpunk) {
                    var engine=Generic(root);
                    notes.Add(engine.Remove(ExistingReceipt(engine,work,index),disabled.Contains(work.Key),
                        owner=>!disabled.Contains(WorkKeyFromReceipt(owner)),preserveExternal));
                }
                else {
                    string id = CyberpunkImportId(work, index);
                    if (id == null) continue;
                    bool anotherOwner = state.CyberpunkImports.Any(pair => pair.Value == id && !jobs.Any(item => item.Key == WorkKeyFromReceipt(pair.Key)));
                    if (!anotherOwner) notes.Add(Cyberpunk(root).RemoveImported(id, Report, disabled.Contains(work.Key)));
                    state.CyberpunkImports.Remove(key);
                    string legacy=P2PPack.LegacySlot(work.Entry,work.Variant,index);
                    if(legacy!=null)state.CyberpunkImports.Remove(legacy);
                }
            }
        }
        return notes.Count == 0 ? T("Mods desinstalados.", "Mods uninstalled.") : String.Join(" ", notes.Distinct());
    }

    HashSet<string> ExternalFilesInOtherProjects(){
        var result=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(projectStore==null||project==null)return result;
        foreach(var peer in projectStore.Read().Projects.Where(item=>item.Id!=project.Id)){
            var other=projectStore.Load(peer);
            if(String.IsNullOrWhiteSpace(other.Destination)||!Directory.Exists(other.Destination))continue;
            string peerData=projectStore.DataPath(peer);
            var files=new InstalledSnapshotInstaller(other.Destination,peerData,catalog).ProjectDeletionFiles(true);
            files=files.Concat(new GeneralModInstaller(other.Destination,Path.Combine(peerData,"Instalacoes")).ProjectDeletionFiles(true));
            foreach(var file in files.Where(file=>GeneralInstallRoutes.External(file.Path)))
                result.Add(GeneralInstallRoutes.Resolve(other.Destination,file.Path));
        }
        return result;
    }

    List<Work> ConflictingAlternatives(Work incoming, string root) {
        var found = new List<Work>();
        var reads=new ScanReads(this,root);
        foreach (var entry in state.List.Mods.Where(item => item.Id==incoming.Entry.Id||
            incoming.Entry.AlternativeGroup!=null&&item.AlternativeGroup==incoming.Entry.AlternativeGroup)) {
            var conflict = VersionWorks(entry).OrderByDescending(candidate=>candidate.Variant.Sources.Count)
                .Where(candidate=>candidate.Key!=incoming.Key&&(entry.Id!=incoming.Entry.Id||
                    candidate.Variant.CyberpunkId==null||candidate.Variant.CyberpunkId!=incoming.Variant.CyberpunkId))
                .FirstOrDefault(candidate=>InstalledVersion(Status(candidate,root,reads)));
            if (conflict != null) found.Add(conflict);
        }
        if(IsBigBody(incoming.Entry)){
            var feet=state.List.Mods.FirstOrDefault(entry=>entry.Id==GeneralModLibrary.VanillaFeetId);
            if(feet!=null){
                var existing=MakeWork(feet);
                if(GeneralStatusPresentation.Kind(Status(existing,root))=="installed")found.Add(existing);
            }
        }
        if(IsBigBody(incoming.Entry)&&found.Any(old=>old.Entry.Id==GeneralModLibrary.EbbBody)){
            var eclipse=state.List.Mods.FirstOrDefault(entry=>entry.Id==GeneralModLibrary.EclipseTattooId);
            if(eclipse!=null){
                var tattoo=MakeWork(eclipse);
                if(GeneralStatusPresentation.Kind(Status(tattoo,root))=="installed")found.Add(tattoo);
            }
        }
        return found;
    }
}
