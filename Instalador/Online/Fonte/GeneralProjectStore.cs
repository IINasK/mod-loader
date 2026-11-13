using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

internal sealed class GeneralProject {
    public string Id, Name;
    public string TemplateId;
    public bool Legacy;
    public int WindowWidth, WindowHeight;
}

internal sealed class GeneralProjectIndex {
    public int Schema = 1;
    public string Theme = "light-red", Language = "pt";
    public string DownloadFolder = OnlineDownloadWatch.DefaultFolder();
    public SwitchPerformance SwitchSettings = new SwitchPerformance();
    public List<GeneralProject> Projects = new List<GeneralProject>();
}

internal sealed class GeneralProjectStore {
    const int MaxIndexBytes = 1048576;
    internal const string CyberpunkTemplate = "cyberpunk-2077";
    readonly object gate = new object();
    internal string Root { get; private set; }
    string IndexPath { get { return Path.Combine(Root, "projects.json"); } }

    internal GeneralProjectStore(string root) {
        if (String.IsNullOrWhiteSpace(root)) throw new IOException("A pasta dos projetos está vazia.");
        Root = Path.GetFullPath(root);
        Safe.NoLinks(Root);
    }

    internal string IndexFingerprint() {
        lock (gate) {
            Safe.NoLinks(IndexPath);
            return File.Exists(IndexPath) ? Safe.Hash(IndexPath) : "";
        }
    }

    internal GeneralProjectIndex Read(bool inspection=false) {
        lock (gate) {
            Safe.NoLinks(IndexPath);
            if (File.Exists(IndexPath)) {
                try {
                    using (var stream = new FileStream(IndexPath,FileMode.Open,FileAccess.Read,inspection?FileShare.ReadWrite|FileShare.Delete:FileShare.Read)) {
                        if (stream.Length > MaxIndexBytes) throw new IOException("O índice de projetos excede 1 MB.");
                        using (var reader = new StreamReader(stream)) {
                            var fields = Safe.Json.DeserializeObject(reader.ReadToEnd()) as Dictionary<string, object>;
                            if (fields == null || !fields.ContainsKey("Schema") || !fields.ContainsKey("Projects")) throw new IOException("Formato do índice de projetos inválido.");
                            var index = Safe.Json.ConvertToType<GeneralProjectIndex>(fields);
                            Validate(index);
                            return index;
                        }
                    }
                } catch (ArgumentException ex) { throw new IOException("Não foi possível ler o índice de projetos. O arquivo foi preservado.", ex); }
                catch (InvalidOperationException ex) { throw new IOException("Não foi possível ler o índice de projetos. O arquivo foi preservado.", ex); }
            }
            var initial = new GeneralProjectIndex();
            string previous = Path.Combine(Root, "lista.json");
            Safe.NoLinks(previous);
            if (File.Exists(previous)) {
                var legacy = new GeneralProject { Id = "legacy", Name = "Projeto anterior", Legacy = true };
                try {
                    var state = new GeneralModLibrary(previous).Load();
                    if (state != null) {
                        var selected = state.List.Presets.FirstOrDefault(item => item.Id == state.SelectedPreset);
                        if (selected != null) legacy.Name = selected.Name.Length > 120 ? selected.Name.Substring(0, 120).Trim() : selected.Name;
                        initial.Theme = state.Theme; initial.Language = state.Language;
                        initial.DownloadFolder = state.DownloadFolder;
                    }
                } catch (IOException) { }
                initial.Projects.Add(legacy);
                if(!inspection)Write(initial);
            }
            return initial;
        }
    }

    internal string DataPath(GeneralProject project) {
        ValidateProject(project);
        string path = project.Legacy ? Root : Safe.In(Root, "Projetos/" + project.Id);
        Safe.NoLinks(path);
        return path;
    }

    internal GeneralProject Create(string name, GeneralModList list, string theme, string language, string destination = null, string templateId = null) {
        lock (gate) {
            name = ProjectName(name);
            ValidatePreferences(theme, language);
            list = list ?? new GeneralModList();
            GeneralModLibrary.ValidateList(list, true);
            var index = Read();
            if (templateId != null) {
                ValidateTemplate(templateId, name, false);
                var existing = index.Projects.FirstOrDefault(item => item.TemplateId == templateId);
                if (existing != null) return existing;
            }
            if (index.Projects.Count >= 200) throw new IOException("Use no máximo 200 projetos nesta biblioteca.");
            var project = new GeneralProject { Id = Guid.NewGuid().ToString("N"), Name = name, TemplateId = templateId };
            string data = DataPath(project);
            var state = new GeneralModState {
                List = list, Theme = theme, Language = language, Destination = destination,
                DownloadFolder = index.DownloadFolder,
                SelectedPreset = list.Presets.Count == 0 ? null : list.Presets[0].Id
            };
            GeneralPresetProfiles.Project(state);
            new GeneralModLibrary(Path.Combine(data, "lista.json")).Save(state);
            index.Projects.Add(project);
            Write(index);
            return project;
        }
    }

    internal GeneralProject CreateReceived(P2PSession session,string sourceData,FixedCatalog catalog){
        lock(gate){
            var index=Read();
            var id=session.Id.Substring(3);
            var source=new InstalledSnapshotInstaller(session.Destination,sourceData,catalog);
            var manifest=source.ReadManifest(session.ReceiptId);
            bool official=manifest.TemplateId==CyberpunkTemplate;
            var existing=index.Projects.FirstOrDefault(item=>official?item.TemplateId==CyberpunkTemplate:item.Id==id);
            if(existing!=null&&!official)return existing;
            if(existing==null&&index.Projects.Count>=200)throw new IOException("Use no máximo 200 projetos nesta biblioteca.");
            var incoming=session.ReviewedProject??manifest.Project??manifest.List;
            var name=(manifest.ProjectName??manifest.Name).Trim();
            var project=existing??new GeneralProject{Id=id,Name=official?"Cyberpunk 2077":ProjectName(name.Substring(0,Math.Min(120,name.Length)).TrimEnd()),
                TemplateId=official?CyberpunkTemplate:null};
            string data=DataPath(project);
            var previous=existing==null?null:Load(existing);
            var list=official&&manifest.TemplateSubset
                ?MergeReceivedPreset(previous==null?GeneralModLibrary.Seed(catalog,new ModImportItem[0]):Safe.Json.Deserialize<GeneralModList>(Safe.Json.Serialize(previous.List)),incoming)
                :incoming;
            var skipped=new HashSet<string>(source.ReadSkippedDestinations(session.ReceiptId),StringComparer.Ordinal);
            if(skipped.Count>0){
                string group=index.Language=="en"?"Not installed":"Não instalados";
                if(!list.Categories.Contains(group,StringComparer.OrdinalIgnoreCase))list.Categories.Add(group);
                foreach(var mod in list.Mods.Concat(list.Presets.Where(p=>p.Configurations!=null).SelectMany(p=>p.Configurations.Values)).Where(mod=>skipped.Contains(mod.Id)))mod.Category=group;
                foreach(var preset in list.Presets.Where(preset=>preset.ModIds.Any(skipped.Contains)))
                    if(!preset.Categories.Contains(group,StringComparer.OrdinalIgnoreCase))preset.Categories.Add(group);
            }
            var state=new GeneralModState{
                List=list,Theme=index.Theme,Language=index.Language,Destination=session.Destination,
                DownloadFolder=index.DownloadFolder,SelectedPreset=incoming.Presets.Select(preset=>preset.Id).FirstOrDefault(),
                BodyChoice=manifest.BodyChoice??(previous!=null&&manifest.TemplateSubset&&
                    !manifest.List.Mods.Any(mod=>GeneralModLibrary.IsHystBody(mod.Id))?previous.BodyChoice:null)
            };
            if(previous!=null){
                KeepReceivedBindings(previous,state);
                if(manifest.TemplateSubset){
                    var replaced=new HashSet<string>(incoming.Mods.Select(mod=>mod.Id),StringComparer.Ordinal);
                    foreach(var choice in previous.ChosenVariants.Where(pair=>!replaced.Contains(pair.Key)))
                        state.ChosenVariants[choice.Key]=choice.Value;
                }
            }
            var receivedDefinitions=state.List.Mods.ToDictionary(m=>m.Id,StringComparer.Ordinal);
            GeneralPresetProfiles.Project(state);
            BindReceivedVariants(state,manifest,session.ReceiptId,receivedDefinitions);
            if(!String.Equals(Path.GetFullPath(sourceData),data,StringComparison.OrdinalIgnoreCase))
                source.CopyReceivedRecognition(session.ReceiptId,data);
            new GeneralModLibrary(Path.Combine(data,"lista.json")).Save(state);
            if(existing==null){index.Projects.Add(project);Write(index);}
            return project;
        }
    }

    internal static void BindReceivedVariants(GeneralModState state,InstalledSnapshotManifest manifest,string receipt,Dictionary<string,GeneralModEntry> receivedDefinitions=null){
        var entries=state.List.Mods.ToDictionary(mod=>mod.Id,StringComparer.Ordinal);
        foreach(var mod in manifest.List.Mods){
            GeneralModEntry entry;
            if(!entries.TryGetValue(mod.Id,out entry))continue;
            if(!entry.Variants.Any(variant=>variant.Id==mod.DefaultVariant)){
                GeneralModEntry source;
                if(receivedDefinitions==null||!receivedDefinitions.TryGetValue(mod.Id,out source))continue;
                var sent=mod.Variants.Single();
                var alternate=source.Variants.FirstOrDefault(v=>v.Id==sent.Id&&GeneralModLibrary.SamePayload(v,sent));
                if(alternate==null)continue;
                entry.Variants.Add(GeneralPresetProfiles.Copy(alternate));
            }
            state.SnapshotReceipts[mod.Id+"/"+mod.DefaultVariant]=receipt;
            if(entry.DefaultVariant==mod.DefaultVariant)state.ChosenVariants.Remove(mod.Id);
            else state.ChosenVariants[mod.Id]=mod.DefaultVariant;
        }
        if(manifest.BodyChoice!=null)state.BodyChoice=manifest.BodyChoice;
    }

    static GeneralModList MergeReceivedPreset(GeneralModList baseline,GeneralModList incoming){
        var mods=incoming.Mods.ToDictionary(mod=>mod.Id,StringComparer.Ordinal);
        var presets=incoming.Presets.ToDictionary(preset=>preset.Id,StringComparer.Ordinal);
        baseline.Mods=baseline.Mods.Select(mod=>mods.ContainsKey(mod.Id)?mods[mod.Id]:mod)
            .Concat(incoming.Mods.Where(mod=>!baseline.Mods.Any(existing=>existing.Id==mod.Id))).ToList();
        baseline.Presets=baseline.Presets.Where(preset=>!presets.ContainsKey(preset.Id)).Concat(incoming.Presets).ToList();
        baseline.Categories=baseline.Categories.Concat(incoming.Categories).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach(var description in incoming.CategoryDescriptions)baseline.CategoryDescriptions[description.Key]=description.Value;
        foreach(var group in incoming.AlternativeGroups){
            if(GeneralModLibrary.GroupColorAvailable(baseline,group.Key,group.Value.Color))baseline.AlternativeGroups[group.Key]=group.Value;
        }
        return baseline;
    }

    static void KeepReceivedBindings(GeneralModState previous,GeneralModState received){
        bool sameRoot=String.Equals(previous.Destination,received.Destination,StringComparison.OrdinalIgnoreCase);
        var original=previous.List.Mods.ToDictionary(mod=>mod.Id,StringComparer.Ordinal);
        foreach(var mod in received.List.Mods){
            GeneralModEntry before;
            if(!original.TryGetValue(mod.Id,out before))continue;
            foreach(var option in mod.Variants){
                var variant=GeneralModLibrary.BodyOption(received.BodyChoice,mod,option);
                var oldVariant=before.Variants.Where(item=>item.Id==variant.Id)
                    .Select(item=>GeneralModLibrary.BodyOption(previous.BodyChoice,before,item))
                    .SingleOrDefault(item=>item.Sources.SequenceEqual(variant.Sources,StringComparer.Ordinal));
                if(oldVariant==null)continue;
                bool sameRoutes=GeneralInstallRoutes.Same(oldVariant.InstallRoutes,variant.InstallRoutes);
                string owner=InstalledSnapshot.Owner(mod,variant),value;
                if(sameRoot&&sameRoutes&&previous.SnapshotReceipts.TryGetValue(owner,out value))received.SnapshotReceipts[owner]=value;
                for(int index=0;index<variant.Sources.Count;index++){
                    string slot=P2PPack.Slot(mod,variant,index);
                    if(previous.ResolvedDownloads.TryGetValue(slot,out value))received.ResolvedDownloads[slot]=value;
                    if(sameRoot&&sameRoutes&&previous.CyberpunkImports.TryGetValue(slot,out value))received.CyberpunkImports[slot]=value;
                }
            }
        }
    }

    internal void SaveWindowSize(GeneralProject project,int width,int height){
        lock(gate){
            var index=Read();
            var saved=Registered(index,project);
            if(saved.WindowWidth==width&&saved.WindowHeight==height)return;
            saved.WindowWidth=width;saved.WindowHeight=height;
            Write(index);
            project.WindowWidth=width;project.WindowHeight=height;
        }
    }

    internal GeneralProject Duplicate(GeneralProject source,string name,FixedCatalog catalog,GeneralModState edited=null,
        Action<string,GeneralModState> register=null){
        lock(gate){
            name=ProjectName(name);var index=Read();var registered=Registered(index,source);
            if(index.Projects.Count>=200)throw new IOException("Use no máximo 200 projetos nesta biblioteca.");
            if(edited!=null){
                string baseName=name.Substring(0,Math.Min(114,name.Length));
                for(int suffix=2;index.Projects.Any(item=>String.Equals(item.Name,name,StringComparison.OrdinalIgnoreCase));suffix++)
                    name=baseName+" ("+suffix+")";
            }
            var state=Safe.Json.Deserialize<GeneralModState>(Safe.Json.Serialize(edited??Load(source)));
            var copy=new GeneralProject{Id=Guid.NewGuid().ToString("N"),Name=name,WindowWidth=registered.WindowWidth,WindowHeight=registered.WindowHeight};
            string target=DataPath(copy),from=DataPath(source);
            DownloadCache.DuplicateSources(Path.Combine(from,"Cache"),Path.Combine(target,"Cache"));
            if(!String.IsNullOrWhiteSpace(state.Destination)&&Directory.Exists(state.Destination)){
                new GeneralModInstaller(state.Destination,Path.Combine(from,"Instalacoes")).DuplicateRecognition(Path.Combine(target,"Instalacoes"));
                new InstalledSnapshotInstaller(state.Destination,from,catalog).DuplicateRecognition(target);
                var links=new InventoryLinkStore(from,state.Destination).Load();
                if(links.Groups.Count>0||links.Ignored.Count>0)new InventoryLinkStore(target,state.Destination).Save(links);
            }else state.SnapshotReceipts.Clear();
            if(register!=null)register(target,state);
            new GeneralModLibrary(Path.Combine(target,"lista.json")).Save(state);
            index.Projects.Add(copy);Write(index);return copy;
        }
    }

    internal GeneralModState Load(GeneralProject project,bool inspection=false) {
        lock (gate) {
            var registered = Registered(Read(inspection), project);
            var state = new GeneralModLibrary(Path.Combine(DataPath(registered), "lista.json")).LoadForInspection(inspection);
            if (state == null) throw new IOException("O arquivo deste projeto não foi encontrado. Nenhum dado foi substituído.");
            state.TemplateId=registered.TemplateId;state.ProjectName=registered.Name;
            return state;
        }
    }

    internal void Delete(GeneralProject project) {
        lock (gate) {
            var index = Read();
            var registered = Registered(index, project);
            if (registered.TemplateId != null) throw new IOException("O projeto padrão faz parte do Mod Loader e não pode ser excluído.");
            index.Projects.Remove(registered);
            Write(index);
        }
    }

    internal void SavePreferences(string theme, string language, string downloadFolder = null,SwitchPerformance switchSettings=null) {
        lock (gate) {
            ValidatePreferences(theme, language);
            var index = Read(); index.Theme = theme; index.Language = language;
            if (downloadFolder != null) index.DownloadFolder = DownloadPath(downloadFolder, true);
            if(switchSettings!=null)index.SwitchSettings=switchSettings;
            Write(index);
        }
    }

    internal void SaveSwitchPerformance(SwitchPerformance settings) {
        lock(gate) {
            var index=Read();
            index.SwitchSettings=settings;
            Write(index);
        }
    }
    static GeneralProject Registered(GeneralProjectIndex index, GeneralProject project) {
        ValidateProject(project);
        var registered = index.Projects.FirstOrDefault(item => item.Id == project.Id && item.Legacy == project.Legacy);
        if (registered == null) throw new IOException("O projeto não pertence à biblioteca de projetos salvos.");
        return registered;
    }

    void Write(GeneralProjectIndex index) {
        Validate(index);
        if (Encoding.UTF8.GetByteCount(Safe.Json.Serialize(index)) > MaxIndexBytes) throw new IOException("O índice de projetos excede 1 MB.");
        Safe.NoLinks(IndexPath);
        Safe.WriteJson(IndexPath, index);
    }

    static void Validate(GeneralProjectIndex index) {
        if (index == null || index.Schema != 1 || index.Projects == null || index.Projects.Count > 200) throw new IOException("Índice de projetos inválido.");
        ValidatePreferences(index.Theme, index.Language);
        if(index.SwitchSettings==null)index.SwitchSettings=new SwitchPerformance();
        index.SwitchSettings.Normalize();
        index.DownloadFolder = DownloadPath(String.IsNullOrWhiteSpace(index.DownloadFolder) ? OnlineDownloadWatch.DefaultFolder() : index.DownloadFolder, false);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var templates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var project in index.Projects) {
            ValidateProject(project);
            if (!ids.Add(project.Id)) throw new IOException("O índice contém um projeto repetido.");
            if (project.TemplateId != null && !templates.Add(project.TemplateId)) throw new IOException("O índice contém um projeto padrão repetido.");
        }
    }

    static void ValidateProject(GeneralProject project) {
        if (project == null || (project.Legacy ? project.Id != "legacy" : !Regex.IsMatch(project.Id ?? "", @"\A[a-f0-9]{32}\z"))) throw new IOException("Identificador de projeto inválido.");
        project.Name = ProjectName(project.Name);
        ValidateTemplate(project.TemplateId, project.Name, project.Legacy);
    }

    static void ValidateTemplate(string templateId, string name, bool legacy) {
        if (templateId == null) return;
        if (templateId != CyberpunkTemplate || name != "Cyberpunk 2077" || legacy)
            throw new IOException("Identidade ou nome do projeto padrão inválido.");
    }

    static string ProjectName(string name) {
        if (String.IsNullOrWhiteSpace(name) || name.Length > 120 || name.Any(Char.IsControl)) throw new IOException("Informe um nome de projeto válido, com até 120 caracteres.");
        return name.Trim();
    }

    static void ValidatePreferences(string theme, string language) {
        if (theme != "light" && theme != "dark" && theme != "light-red" && theme != "black") throw new IOException("Tema de projeto inválido.");
        if (language != "pt" && language != "en") throw new IOException("Idioma de projeto inválido.");
    }

    static string DownloadPath(string folder, bool requireExisting) {
        if (folder == null || folder.Any(Char.IsControl) || !Regex.IsMatch(folder, @"\A[A-Za-z]:[\\/].+")) throw new IOException("Escolha uma pasta local absoluta para os downloads.");
        string full = Path.GetFullPath(folder).TrimEnd('\\');
        if (full.Length <= 3) throw new IOException("Escolha uma pasta de downloads, não a raiz do disco.");
        Safe.NoLinks(full);
        // A missing old folder must remain editable from Home; only a new choice needs to exist now.
        return requireExisting || Directory.Exists(full) ? new OnlineDownloadWatch(full).Folder : full;
    }
}
