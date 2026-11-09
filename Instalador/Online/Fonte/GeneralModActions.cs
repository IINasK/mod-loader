using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    Button primaryAction;
    enum ModAction { None, Disable, ChooseDependency, Enable, Change, Install, Download }

    List<Work> SelectedJobs() {
        var jobs = Marked();
        if (jobs.Count == 0 && FocusedEntry() != null) jobs.Add(MakeWork(FocusedEntry()));
        return jobs;
    }

    ModAction ActionFor(Work work, HashSet<string> selectedIds) {
        if(GeneralModLibrary.NeedsDependencyVersion(work.Entry,work.Variant))return ModAction.ChooseDependency;
        string status;
        statuses.TryGetValue(work.Key, out status);
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
        string status;
        return statuses.TryGetValue(Key(entry, Selected(entry)), out status)
            && (GeneralStatusPresentation.Kind(status) == "installed" || status == GeneralStatusPresentation.ManualBody);
    }

    ModAction ClassifyJobs(List<Work> jobs, out List<Work> actionable) {
        var ids = new HashSet<string>(jobs.Select(work => work.Entry.Id), StringComparer.Ordinal);
        var actions = jobs.Select(work => new { Work = work, Action = ActionFor(work, ids) }).ToList();
        var command = actions.Count == 0 ? ModAction.None : actions.Max(item => item.Action);
        actionable = actions.Where(item => command == ModAction.Download
            ? item.Action == ModAction.Download || item.Action == ModAction.Install
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
        List<Work> ignored;
        var action = ClassifyJobs(SelectedJobs(), out ignored);
        primaryAction.Text = action == ModAction.Disable ? T("Desabilitar", "Disable")
            : action == ModAction.ChooseDependency ? GeneralStatusPresentation.Label(GeneralStatusPresentation.ChooseDependency)
            : action == ModAction.Enable ? T("Reativar", "Enable")
            : action == ModAction.Change ? T("Mudar versão", "Change version")
            : action == ModAction.Install ? T("Instalar", "Install")
            : T("Baixar", "Download");
        primaryAction.Enabled = action != ModAction.None;
    }

    DisabledMods DisabledAt(string root) {
        return new DisabledMods(root, data, () => EnsureGameClosed(root));
    }

    void KeepLinkedDependencies(List<Work> jobs,string root) {
        var removing=new HashSet<string>(jobs.Select(work=>work.Entry.Id),StringComparer.Ordinal);
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

    Dictionary<string, List<DisabledModFile>> PlanDisable(List<Work> jobs, string root) {
        EnsureGameClosed(root);
        KeepLinkedDependencies(jobs,root);
        var result = jobs.ToDictionary(work => work.Key, work => new List<DisabledModFile>(), StringComparer.Ordinal);
        var selected = new HashSet<string>(result.Keys, StringComparer.Ordinal);
        bool cyberpunk = IsCyberpunk(root);
        var allFiles = Snapshot(root).ProjectDeletionFiles().ToList();
        var fixedIds = new HashSet<string>(jobs.Select(work => work.Variant.CyberpunkId).Where(id => id != null), StringComparer.Ordinal);
        if (!cyberpunk) {
            foreach (var file in Generic(root).ProjectDeletionFiles()) {
                file.OwnerKeys = file.OwnerKeys.Select(WorkKeyFromReceipt).Distinct().ToList();
                allFiles.Add(file);
            }
        } else {
            var importOwners = state.CyberpunkImports.ToLookup(item => item.Value, item => WorkKeyFromReceipt(item.Key), StringComparer.Ordinal);
            var imports = Cyberpunk(root).ProjectDeletionFiles(null).ToList();
            foreach (var file in imports) {
                string id = file.OwnerKeys.Single();
                file.OwnerKeys = importOwners[id].Concat(jobs.Where(work => work.Entry.Id == id).Select(work => work.Key)).Distinct().ToList();
                if (file.OwnerKeys.Count == 0) file.OwnerKeys.Add(id);
                allFiles.Add(file);
            }
            var fixedWork = jobs.Where(work => work.Variant.CyberpunkId != null).ToLookup(work => work.Variant.CyberpunkId);
            foreach (var package in catalog.Packages) {
                var recipe = package;
                if (package.Id == NightCityDensity.ModId) {
                    var density = NightCityDensity.Read(package, root);
                    if (!density.CanApply && !fixedIds.Contains(package.Id)) continue;
                    if (density.Id == "absent") continue;
                    recipe = NightCityDensity.Installed(package, root);
                }
                var files = recipe.Files.Where(file => !file.UserData).ToList();
                if (!files.Any(file => File.Exists(Safe.In(root, file.Path)))) continue;
                if (!fixedIds.Contains(package.Id) && FixedData.Expand(catalog, package.Dependencies).Any(dep => fixedIds.Contains(dep.Id)))
                    throw new IOException(T("Ainda usado por: ", "Still used by: ") + package.Name);
                foreach (var file in files) {
                    string target = Safe.In(root, file.Path);
                    if (!File.Exists(target)) continue;
                    long size = new FileInfo(target).Length;
                    string hash = file.Sha256;
                    bool owned = !recipe.Immutable && fixedIds.Contains(package.Id);
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
                            if (GameEdition.NeedsPL(recipe) && !GameEdition.HasPL(root))
                                throw new IOException(T("Requer Phantom Liberty.", "Requires Phantom Liberty."));
                            var batch = new HashSet<string>(jobs.Select(item => item.Variant.CyberpunkId), StringComparer.Ordinal);
                            var missing = FixedData.Expand(catalog, recipe.Dependencies).FirstOrDefault(dependency =>
                                !batch.Contains(dependency.Id) && !FixedData.Scan(dependency, root).IsCorrect);
                            if (missing != null) throw new IOException(T("Ative primeiro: ", "Enable first: ") + missing.Name);
                        }
                    }
                    store.Enable(jobs.Select(work => work.Key));
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
        var snapshots = jobs.Where(work => state.SnapshotReceipts.ContainsKey(work.Key) && work.Variant.CyberpunkId != "NightCoop").ToList();
        foreach (var group in snapshots.GroupBy(work => new { Receipt = state.SnapshotReceipts[work.Key], Disabled = disabled.Contains(work.Key) })) {
            notes.Add(Snapshot(root).Remove(group.Key.Receipt, group.Select(work => work.Key), Report, group.Key.Disabled,preserveExternal));
            foreach (var work in group) state.SnapshotReceipts.Remove(work.Key);
        }
        var standard = jobs.Except(snapshots).ToList();
        var fixedIds = standard.Where(work => work.Variant.CyberpunkId != null && !disabled.Contains(work.Key))
            .Select(work => work.Variant.CyberpunkId).Distinct().ToArray();
        if (fixedIds.Length > 0) {
            if (!IsCyberpunk(root)) throw new IOException(T("Selecione a pasta do Cyberpunk 2077.", "Select the Cyberpunk 2077 folder."));
            notes.Add(Cyberpunk(root).DeactivateMany(fixedIds, Report, keepBackup: false));
        }
        bool cyberpunk = IsCyberpunk(root);
        foreach (var work in standard.Where(item => item.Variant.CyberpunkId == null)) {
            for (int index = work.Variant.Sources.Count - 1; index >= 0; index--) {
                string key = work.Receipt(index);
                if (!cyberpunk) notes.Add(Generic(root).Remove(key, disabled.Contains(work.Key), owner => !disabled.Contains(WorkKeyFromReceipt(owner)),preserveExternal));
                else {
                    string id = CyberpunkImportId(work, index);
                    if (id == null) continue;
                    bool anotherOwner = state.CyberpunkImports.Any(pair => pair.Value == id && !jobs.Any(item => item.Key == WorkKeyFromReceipt(pair.Key)));
                    if (!anotherOwner) notes.Add(Cyberpunk(root).RemoveImported(id, Report, disabled.Contains(work.Key)));
                    state.CyberpunkImports.Remove(key);
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
            if(!IsCyberpunk(other.Destination))
                files=files.Concat(new GeneralModInstaller(other.Destination,Path.Combine(peerData,"Instalacoes")).ProjectDeletionFiles(true));
            foreach(var file in files.Where(file=>GeneralInstallRoutes.External(file.Path)))
                result.Add(GeneralInstallRoutes.Resolve(other.Destination,file.Path));
        }
        return result;
    }

    List<Work> ConflictingAlternatives(Work incoming, string root) {
        if (incoming.Entry.AlternativeGroup == null) return new List<Work>();
        var found = new List<Work>();
        foreach (var entry in state.List.Mods.Where(item => item.Id != incoming.Entry.Id && item.AlternativeGroup == incoming.Entry.AlternativeGroup)) {
            var conflict = entry.Variants.OrderByDescending(variant => variant.Sources.Count)
                .Select(variant => new Work { Entry = entry, Variant = variant }).FirstOrDefault(candidate => {
                    string status = Status(candidate, root);
                    return GeneralStatusPresentation.Kind(status) == "installed" || status == GeneralStatusPresentation.ManualBody;
                });
            if (conflict != null) found.Add(conflict);
        }
        return found;
    }
}
