using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

public sealed class InstalledSnapshotManifest {
    public int Schema=1;
    public string Name,Game,GameFileVersion,TemplateId,ProjectName,BodyChoice;
    public bool TemplateSubset;
    public GeneralModList List=new GeneralModList();
    public GeneralModList Project;
    public Dictionary<string,string> SyncGameInventory;
    public List<InstalledSnapshotFile> Files=new List<InstalledSnapshotFile>();
}
public sealed class InstalledSnapshotFile {
    public string Path,Sha256,Kind,Section,Content;
    public long Size;
    public bool ReuseLocal;
    public List<string> Owners=new List<string>();
}
public static partial class InstalledSnapshot {
    public const int MaxFiles=10000;
    public const long MaxFileBytes=16L*1024*1024*1024,MaxTotalBytes=128L*1024*1024*1024;
    static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
    public static string Hash(byte[] bytes){using(var sha=SHA256.Create())return Safe.Hex(sha.ComputeHash(bytes));}
    public static string Entry(InstalledSnapshotFile file){return "files/"+file.Sha256;}
    public static string Owner(GeneralModEntry mod,GeneralModVariant variant){return mod.Id+"/"+variant.Id;}
    static T Clone<T>(T value){return Safe.Json.Deserialize<T>(Safe.Json.Serialize(value));}
    static string Selected(GeneralModState state,GeneralModEntry mod){string id;return state.ChosenVariants.TryGetValue(mod.Id,out id)?id:mod.DefaultVariant;}
    static bool IsCyberpunk(string root){return File.Exists(Safe.In(root,"bin/x64/Cyberpunk2077.exe"));}
    static bool IncludesBodyChoice(GeneralModList list,string choice) {
        return list.Mods.Any(mod=>(GeneralModLibrary.IsHystBody(mod.Id)||
            mod.AlternativeGroup==GeneralModLibrary.BodyGroup&&mod.Id!=GeneralModLibrary.VanillaFeetId)&&
            mod.Id==(choice==GeneralModLibrary.NaturalBody?GeneralModLibrary.EbbBody:choice));
    }
    internal static bool SafeGenericPath(string path) {
        Safe.In(System.IO.Path.GetTempPath(),path);
        return !Regex.IsMatch(path,@"(^|/)(?:\.git|\.codex|\.agents|\.aws|\.CoopModsOnline|saves?|saved games|accounts?|credentials?|cookies?|logs?|cache|profiles?|userdata)(?:/|\.|$)|(?:^|/)(?:user\.settings|loginusers\.vdf|config\.vdf|steam_api\.ini|.*\.(?:dmp|log|sav|dat|db|sqlite))$",RegexOptions.IgnoreCase);
    }
    static GeneralModVariant LegacyDensityVariant(FixedCatalog catalog){
        var variant=Clone(GeneralModLibrary.Seed(catalog,new ModImportItem[0]).Mods.Single(m=>m.Id==NightCityDensity.ModId).Variants.Single(v=>v.CyberpunkVariant=="medium"));
        variant.Id="legacy-low";variant.CyberpunkVariant="legacy-low";variant.Name="Baixa personalizada (legado)";return variant;
    }
    internal static FixedMod FixedRecipe(FixedCatalog catalog,GeneralModVariant variant) {
        var mod=catalog.Packages.SingleOrDefault(m=>m.Id==variant.CyberpunkId);
        if(mod==null||!String.IsNullOrEmpty(mod.BlockedReason))throw new IOException("Componente desconhecido ou retido.");
        if(mod.Id==NightCityDensity.ModId) {
            if(variant.CyberpunkVariant=="legacy-low"){var legacy=NightCityDensity.Legacy(mod);if(legacy!=null)return legacy;}
            if(!NightCityDensity.Supported(variant.CyberpunkVariant))throw new IOException("Densidade instalada não suportada para compartilhamento.");
            return NightCityDensity.Resolve(mod,variant.CyberpunkVariant);
        }
        if (EquipmentExOptions.Available(mod)) return EquipmentExOptions.Resolve(mod, variant.CyberpunkVariant ?? EquipmentExOptions.WeaveId);
        if(variant.CyberpunkVariant!=null)throw new IOException("Variante fixa inválida.");
        return mod;
    }
    public static InstalledSnapshotManifest Inspect(GeneralModState state,IEnumerable<string> ids,FixedCatalog catalog,string appData,bool includeConfigs,bool allowMissing=false,Action checkpoint=null,
        Func<string,string,string,bool> skipProblem=null,Action<int,int,string> inspectProgress=null) {
        if(state==null||state.List==null||ids==null||String.IsNullOrWhiteSpace(state.Destination))throw new IOException("Escolha uma pasta de instalação e um preset.");
        string root=Path.GetFullPath(state.Destination);Safe.NoLinks(root);
        if(!Directory.Exists(root))throw new IOException("Pasta de instalação ausente.");
        bool cyberpunk=IsCyberpunk(root);
        var selected=new HashSet<string>(ids,StringComparer.Ordinal);
        if(selected.Count==0||selected.Any(id=>!state.List.Mods.Any(m=>m.Id==id)))throw new IOException("Selecione um preset com mods instalados.");
        var list=new GeneralModList{UncategorizedName=state.List.UncategorizedName,UncategorizedDescription=state.List.UncategorizedDescription};
        var manifest=new InstalledSnapshotManifest{Name=state.List.Presets.FirstOrDefault(p=>p.Id==state.SelectedPreset)==null?"Pacote instalado":state.List.Presets.First(p=>p.Id==state.SelectedPreset).Name,ProjectName=state.ProjectName,Game=cyberpunk?"cyberpunk2077":"generic",GameFileVersion=cyberpunk?catalog.GameFileVersion:null,List=list};
        var seed=GeneralModLibrary.Seed(catalog,new ModImportItem[0]);
        var entries=state.List.Mods.Where(m=>selected.Contains(m.Id)).Select(Clone).ToList();
        if(cyberpunk) {
            var fixedIds=entries.Select(m=>m.Variants.Single(v=>v.Id==Selected(state,m)).CyberpunkId).Where(id=>id!=null).ToList();
            foreach(var dependency in FixedData.Expand(catalog,fixedIds,EquipmentExOptions.StockActive(catalog,root))) {
                if(entries.Any(m=>m.Variants.Single(v=>v.Id==Selected(state,m)).CyberpunkId==dependency.Id))continue;
                entries.Add(Clone(seed.Mods.Single(m=>m.Id==dependency.Id)));
            }
        }
        var fixedEngine=cyberpunk?new FixedInstaller(catalog,root):null;
        var genericEngine=cyberpunk?null:new GeneralModInstaller(root,Path.Combine(appData,"Instalacoes"));
        var receivedEngine=new InstalledSnapshotInstaller(root,appData,catalog);
        var disabled=new DisabledMods(root,appData).Read();
        var problems=new List<string>();
        var byPath=new Dictionary<string,InstalledSnapshotFile>(StringComparer.Ordinal);
        for(int index=0;index<entries.Count;index++){
            var mod=entries[index];
            if(checkpoint!=null)checkpoint();
            if(inspectProgress!=null)inspectProgress(index,entries.Count,mod.Name);
            try {
            var variant=GeneralModLibrary.BodyOption(state.BodyChoice,mod,mod.Variants.Single(v=>v.Id==Selected(state,mod)));
            if(disabled.ContainsKey(Owner(mod,variant))){
                if(allowMissing)continue;
                throw new IOException("Este mod está desabilitado.");
            }
            if(allowMissing&&Uninstalled(state,mod,variant,catalog,root,appData))continue;
            if(cyberpunk && variant.CyberpunkId==NightCityDensity.ModId) {
                var density=NightCityDensity.Read(catalog.Packages.Single(m=>m.Id==variant.CyberpunkId),root);
                if(density.Id=="legacy-low"){variant=LegacyDensityVariant(catalog);mod.Variants.Add(variant);}
                else {if(!NightCityDensity.Supported(density.Id)||density.Partial)throw new IOException(density.Detail);variant=mod.Variants.Single(v=>v.CyberpunkVariant==density.Id);}
            }
            if (cyberpunk && variant.CyberpunkId == EquipmentExOptions.ModId) {
                var equipment = catalog.Packages.Single(recipe => recipe.Id == EquipmentExOptions.ModId);
                if (EquipmentExOptions.Available(equipment)) {
                    var option = EquipmentExOptions.Read(equipment, root);
                    if (!option.CanApply || option.Partial || option.Id == "absent")
                        throw new IOException(option.Detail ?? "Equipment-EX ausente ou incompleto.");
                    variant = mod.Variants.SingleOrDefault(candidate => candidate.Id == option.Id)
                        ?? Clone(seed.Mods.Single(entry => entry.Id == mod.Id).Variants.Single(candidate => candidate.Id == option.Id));
                }
            }
            string work=Owner(mod,variant);
            var originalSources=variant.Sources.ToList();
            List<InstalledSnapshotFile> files=new List<InstalledSnapshotFile>();
            string snapshotId;
            if(variant.CyberpunkId!="NightCoop"&&state.SnapshotReceipts!=null&&state.SnapshotReceipts.TryGetValue(work,out snapshotId)){
                if(mod.Id==GeneralModLibrary.EbbBody&&mod.Variants.Single(v=>v.Id==variant.Id).NaturalOption!=null){
                    var installed=receivedEngine.ReadManifest(snapshotId).List.Mods.Single(entry=>entry.Id==mod.Id).Variants.Single();
                    var expected=Clone(variant);
                    MakeSourcesPortable(expected,mod.Id);
                    if(!installed.Sources.SequenceEqual(expected.Sources,StringComparer.Ordinal)||
                        !GeneralInstallRoutes.Same(installed.InstallRoutes,variant.InstallRoutes))
                        throw new IOException("O recibo do corpo corresponde a outra configuração de instalação.");
                }
                files=receivedEngine.ReadInstalledFiles(snapshotId,work);
            }
            else if(variant.CyberpunkId!=null) {
                if(!cyberpunk)throw new IOException(mod.Name+": o destino não é Cyberpunk 2077.");
                var recipe=FixedRecipe(catalog,variant);
                var paths=GeneralInstallRoutes.FixedPaths(catalog,recipe,variant.InstallRoutes);
                var invalid=recipe.Files.Where(file=>!file.UserData).Where(file=>{
                    string path=GeneralInstallRoutes.Resolve(root,paths[file.Path]);
                    return !File.Exists(path)||new FileInfo(path).Length!=file.Size||Safe.Hash(path)!=file.Sha256;
                }).Select(file=>paths[file.Path]).ToList();
                if(invalid.Count>0)throw new IOException("Arquivos ausentes ou alterados ("+invalid.Count+"): "+String.Join("; ",invalid.Take(30)));
                foreach(var file in recipe.Files) {
                    string installed=GeneralInstallRoutes.Resolve(root,paths[file.Path]);
                    if(file.UserData) {
                        if(includeConfigs&&File.Exists(installed))files.Add(Config(file.Path,"json",null,InstalledSnapshotConfig.Json(InstalledSnapshotConfig.Read(installed))));
                        continue;
                    }
                    files.Add(new InstalledSnapshotFile{Path=paths[file.Path],Sha256=file.Sha256,Size=file.Size,Kind="file"});
                }
                if(includeConfigs&&catalog.Defaults!=null)foreach(var config in catalog.Defaults.Where(d=>d.ModId==recipe.Id)) {
                    string path=Safe.In(root,config.Path);if(!File.Exists(path))continue;
                    string content=config.Format=="json"?InstalledSnapshotConfig.Json(InstalledSnapshotConfig.Read(path)):InstalledSnapshotConfig.Section(InstalledSnapshotConfig.Read(path),config.Section);
                    if(content!=null)files.Add(Config(config.Path,config.Format,config.Section,content));
                }
            }else if(cyberpunk&&HasManualHystFiles(mod.Id,root)&&
                (state.CyberpunkImports==null||!originalSources.Select((source,sourceIndex)=>work+"/"+sourceIndex+"/"+source).Any(state.CyberpunkImports.ContainsKey))) {
                var knownBody=seed.Mods.FirstOrDefault(item=>item.Id==mod.Id);
                variant=ReadManualHyst(root,mod,knownBody,state.SelectedPreset!="no-pl",files,bodyChoice:state.BodyChoice);
                work=Owner(mod,variant);
                originalSources=variant.Sources.ToList();
                if(disabled.ContainsKey(work))throw new IOException("Este mod está desabilitado.");
            }else {
                for(int i=0;i<originalSources.Count;i++) {
                    string key=work+"/"+i+"/"+originalSources[i];
                    if(cyberpunk) {
                        string id;
                        if(state.CyberpunkImports==null||!state.CyberpunkImports.TryGetValue(key,out id)) {
                            id=mod.Id.StartsWith("file-",StringComparison.Ordinal)?mod.Id:null;
                            if(id==null)throw new IOException(mod.Name+": recibo instalado não encontrado.");
                        }
                        files.AddRange(fixedEngine.ExportSnapshotImportedFiles(id));
                    }else files.AddRange(genericEngine.ExportSnapshotFiles(key,variant.InstallRoutes));
                }
            }
            if(cyberpunk && files.Any(RetiredPackEntries.LocalDroneFile)) continue;
            mod.Variants=new List<GeneralModVariant>{variant};mod.DefaultVariant=variant.Id;
            variant.NaturalOption=null;
            MakeSourcesPortable(variant,mod.Id);
            var additions=new Dictionary<string,InstalledSnapshotFile>(StringComparer.Ordinal);
            var shared=new HashSet<InstalledSnapshotFile>();
            foreach(var file in files) {
                if(!includeConfigs&&file.Kind!="file")continue;
                if(skipProblem!=null&&variant.CyberpunkId==null&&file.Kind=="file"&&IsCustomConfig(file.Path))
                    using(var input=File.OpenRead(GeneralInstallRoutes.Resolve(root,file.Path)))ValidateCustomConfig(file,input);
                string key=file.Path.ToUpperInvariant()+"\n"+file.Section;
                InstalledSnapshotFile same;
                if(byPath.TryGetValue(key,out same)||additions.TryGetValue(key,out same)){
                    if(same.Kind!=file.Kind||same.Size!=file.Size||same.Sha256!=file.Sha256)
                        throw new IOException("Dois mods usam conteúdo diferente em "+file.Path);
                    shared.Add(same);
                }else{
                    file.ReuseLocal=false;
                    file.Owners=new List<string>{work};
                    additions.Add(key,file);
                }
            }
            if(!files.Any(f=>f.Kind=="file"))throw new IOException("Nenhum arquivo instalado foi encontrado.");
            mod.DetectedPaths.Clear();list.Mods.Add(mod);
            foreach(var pair in additions){byPath.Add(pair.Key,pair.Value);manifest.Files.Add(pair.Value);}
            foreach(var file in shared)if(!file.Owners.Contains(work))file.Owners.Add(work);
            }catch(Exception ex){
                if(!(ex is IOException)&&!(ex is UnauthorizedAccessException))throw;
                if(skipProblem==null)problems.Add(mod.Name+": "+ex.Message);
                else if(!skipProblem(mod.Id,mod.Name,ex.Message))
                    throw new InstalledSnapshotReviewException(new[]{mod.Name+": "+ex.Message});
            }
        }
        if(inspectProgress!=null)inspectProgress(entries.Count,entries.Count,null);
        if(problems.Count>0)throw new InstalledSnapshotReviewException(problems);
        if(cyberpunk&&skipProblem!=null){
            var includedRecipes=new HashSet<string>(list.Mods.Select(m=>m.Variants[0].CyberpunkId).Where(id=>id!=null),StringComparer.Ordinal);
            var removed=new HashSet<string>(StringComparer.Ordinal);
            list.Mods.RemoveAll(mod=>{
                var variant=mod.Variants[0];
                if(variant.CyberpunkId==null||FixedData.Expand(catalog,new[]{variant.CyberpunkId},EquipmentExOptions.StockActive(catalog,root)).All(recipe=>includedRecipes.Contains(recipe.Id)))return false;
                removed.Add(Owner(mod,variant));
                return true;
            });
            foreach(var file in manifest.Files)file.Owners.RemoveAll(removed.Contains);
            manifest.Files.RemoveAll(file=>file.Owners.Count==0);
        }
        if(allowMissing||skipProblem!=null){
            int omitted;
            do{
                var available=new HashSet<string>(list.Mods.Select(mod=>mod.Id),StringComparer.Ordinal);
                var owners=new HashSet<string>(list.Mods.Where(mod=>mod.Variants.Single().RequiredMods.Any(id=>!available.Contains(id)))
                    .Select(mod=>Owner(mod,mod.Variants.Single())),StringComparer.Ordinal);
                omitted=list.Mods.RemoveAll(mod=>owners.Contains(Owner(mod,mod.Variants.Single())));
                if(omitted>0){
                    foreach(var file in manifest.Files)file.Owners.RemoveAll(owners.Contains);
                    manifest.Files.RemoveAll(file=>file.Owners.Count==0);
                }
            }while(omitted>0);
        }
        list.Categories=state.List.Categories.Concat(list.Mods.Where(m=>!String.IsNullOrWhiteSpace(m.Category)).Select(m=>m.Category)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach(string category in list.Categories) {string description;if(state.List.CategoryDescriptions.TryGetValue(category,out description))list.CategoryDescriptions[category]=description;}
        foreach(string groupId in list.Mods.Select(mod=>mod.AlternativeGroup).Where(id=>id!=null).Distinct(StringComparer.Ordinal)){
            GeneralAlternativeGroup group;
            if(state.List.AlternativeGroups.TryGetValue(groupId,out group))list.AlternativeGroups.Add(groupId,Clone(group));
        }
        var included=new HashSet<string>(list.Mods.Select(m=>m.Id),StringComparer.Ordinal);
        foreach(var original in state.List.Presets.OrderByDescending(p=>p.Id==state.SelectedPreset)){
            var copy=Clone(original);copy.ModIds=copy.ModIds.Where(included.Contains).ToList();list.Presets.Add(copy);
        }
        if(list.Presets.Count==0)list.Presets.Add(new GeneralModPreset{Id="installed",Name=manifest.Name,ModIds=list.Mods.Select(m=>m.Id).ToList()});
        if(IncludesBodyChoice(list,state.BodyChoice))manifest.BodyChoice=state.BodyChoice;
        if(allowMissing||HasInstallRoutes(manifest)||manifest.BodyChoice!=null){
            manifest.Project=ProjectDefinition(state,manifest.List);
            if(cyberpunk&&state.TemplateId==GeneralProjectStore.CyberpunkTemplate){
                manifest.TemplateId=state.TemplateId;
                manifest.TemplateSubset=state.TemplateSubset;
            }
        }
        if(HasInstallRoutes(manifest))manifest.Schema=2;
        Validate(manifest,catalog);
        return manifest;
    }
    public static InstalledSnapshotManifest Create(string zipPath,GeneralModState state,IEnumerable<string> ids,FixedCatalog catalog,string appData,bool includeConfigs,Action<string> report,bool allowMissing=false,Action checkpoint=null,Action<long,long> progress=null) {
        var manifest=Inspect(state,ids,catalog,appData,includeConfigs,allowMissing,checkpoint);
        Write(zipPath,state.Destination,manifest,report,checkpoint,progress);
        return manifest;
    }
    internal static void Write(string zipPath,string root,InstalledSnapshotManifest manifest,Action<string> report,Action checkpoint,Action<long,long> progress){
        root=Path.GetFullPath(root);
        string output=Path.GetFullPath(zipPath);Safe.NoLinks(output);
        var payload=manifest.Files.Where(f=>f.Kind=="file"&&!f.ReuseLocal).GroupBy(f=>f.Sha256).Select(g=>g.First()).ToList();
        long done=0,expected=payload.Sum(file=>file.Size);if(progress!=null)progress(0,expected);
        bool created=false;
        try {
            using(var stream=new FileStream(output,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None)) {
                created=true;
                using(var archive=new ZipArchive(stream,ZipArchiveMode.Create,true)) {
                    var metadata=archive.CreateEntry("manifest.json",CompressionLevel.Optimal);
                    using(var target=metadata.Open()){byte[] bytes=Utf8.GetBytes(Safe.Json.Serialize(manifest));target.Write(bytes,0,bytes.Length);}
                    foreach(var file in payload) {
                        if(report!=null)report("Preparando "+file.Path);
                        string source=GeneralInstallRoutes.Resolve(root,file.Path,true);
                        using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read))
                        using(var sha=SHA256.Create())
                        using(var target=archive.CreateEntry(Entry(file),CompressionLevel.Fastest).Open()) {
                            ValidateCustomData(manifest,file,input);input.Position=0;
                            byte[] buffer=new byte[1024*1024];long total=0;int count;
                            while((count=input.Read(buffer,0,buffer.Length))>0){if(checkpoint!=null)checkpoint();total+=count;if(total>file.Size)throw new IOException("Arquivo mudou durante a exportação: "+file.Path);sha.TransformBlock(buffer,0,count,null,0);target.Write(buffer,0,count);done+=count;if(progress!=null)progress(done,expected);}
                            sha.TransformFinalBlock(new byte[0],0,0);
                            if(total!=file.Size||Safe.Hex(sha.Hash)!=file.Sha256)throw new IOException("Arquivo mudou durante a exportação: "+file.Path);
                        }
                    }
                }
                stream.Flush(true);
            }
        }catch {if(created){Safe.NoLinks(output);File.Delete(output);}throw;}
    }
    static InstalledSnapshotFile Config(string path,string kind,string section,string content){byte[] bytes=Utf8.GetBytes(content);return new InstalledSnapshotFile{Path=path,Kind=kind,Section=section,Content=content,Size=bytes.Length,Sha256=Hash(bytes)};}
    public static void Validate(InstalledSnapshotManifest manifest,FixedCatalog catalog) {
        Validate(manifest,catalog,false);
    }
    internal static void Validate(InstalledSnapshotManifest manifest,FixedCatalog catalog,bool localReceipt) {
        if(manifest==null||(manifest.Schema!=1&&manifest.Schema!=2)||manifest.Schema==2&&manifest.Project==null||manifest.List==null||manifest.List.Mods==null||manifest.Files==null||manifest.Files.Count==0&&manifest.Project==null||manifest.Files.Count>MaxFiles||String.IsNullOrWhiteSpace(manifest.Name)||manifest.Name.Length>300||!(manifest.Game=="cyberpunk2077"||manifest.Game=="generic")||(manifest.Game=="cyberpunk2077"&&manifest.GameFileVersion!=catalog.GameFileVersion)||(manifest.Game=="generic"&&manifest.GameFileVersion!=null))throw new IOException("Manifesto de arquivos instalados inválido.");
        if(manifest.ProjectName!=null&&(String.IsNullOrWhiteSpace(manifest.ProjectName)||manifest.ProjectName.Length>120||manifest.ProjectName.Any(Char.IsControl)))
            throw new IOException("Nome do projeto recebido inválido.");
        GeneralModLibrary.ValidateList(manifest.List,true);
        if(manifest.BodyChoice!=null&&(manifest.Project==null||!IncludesBodyChoice(manifest.List,manifest.BodyChoice)))
            throw new IOException("Opção de corpo inválida no pacote.");
        if(manifest.TemplateId!=null&&manifest.Project==null||manifest.TemplateSubset&&manifest.TemplateId==null)
            throw new IOException("Identidade do projeto padrão inválida.");
        if(manifest.Project!=null)ValidateProject(manifest,catalog,localReceipt);
        if(manifest.Schema==1&&HasInstallRoutes(manifest))throw new IOException("Destinos personalizados exigem um pacote atualizado.");
        var owners=new Dictionary<string,GeneralModVariant>(StringComparer.Ordinal);
        var recipes=new Dictionary<string,FixedMod>(StringComparer.Ordinal);
        var recipePaths=new Dictionary<string,Dictionary<string,FixedFile>>(StringComparer.Ordinal);
        var ids=new HashSet<string>(StringComparer.Ordinal);
        if(manifest.List.Mods.Count==0&&manifest.Project==null||manifest.List.Mods.Count>1000)throw new IOException("Lista de instalação inválida.");
        foreach(var mod in manifest.List.Mods) {
            if(mod==null||!Regex.IsMatch(mod.Id??"",@"\A[A-Za-z0-9_-]{1,120}\z")||!ids.Add(mod.Id)||String.IsNullOrWhiteSpace(mod.Name)||mod.Name.Length>300||mod.Variants==null||mod.Variants.Count!=1)throw new IOException("Mod instalado inválido.");
            var variant=mod.Variants[0];
            if(variant==null||!Regex.IsMatch(variant.Id??"",@"\A[A-Za-z0-9_-]{1,120}\z")||variant.Id!=mod.DefaultVariant||variant.Sources==null||variant.Sources.Count==0||variant.Sources.Count>32)throw new IOException("Variante instalada inválida.");
            if(variant.NaturalOption!=null)throw new IOException("O pacote instalado deve conter só a opção usada.");
            string owner=Owner(mod,variant);owners.Add(owner,variant);
            if(variant.CyberpunkId!=null) {
                if(manifest.Game!="cyberpunk2077")throw new IOException("Receita Cyberpunk em destino genérico.");
                var recipe=FixedRecipe(catalog,variant);recipes.Add(owner,recipe);
                var paths=GeneralInstallRoutes.FixedPaths(catalog,recipe,variant.InstallRoutes);
                recipePaths.Add(owner,recipe.Files.Where(file=>!file.UserData).ToDictionary(file=>paths[file.Path],StringComparer.Ordinal));
                var seed=recipe.Id==NightCityDensity.ModId&&variant.CyberpunkVariant=="legacy-low"?LegacyDensityVariant(catalog):GeneralModLibrary.Seed(catalog,new ModImportItem[0]).Mods.Single(m=>m.Id==recipe.Id).Variants.Single(v=>v.CyberpunkVariant==variant.CyberpunkVariant);
                if(!seed.Sources.SequenceEqual(variant.Sources,StringComparer.Ordinal))throw new IOException("Fontes fixas alteradas no pacote.");
            }else if(variant.CyberpunkVariant!=null||!(variant.Sources.Count==1&&variant.Sources[0]=="installed://"+mod.Id+"/"+variant.Id)&&!variant.Sources.All(GeneralModDownload.IsWebSource))throw new IOException("O pacote contém caminhos privados de origem.");
        }
        foreach(var mod in manifest.List.Mods)
            if(mod.Variants.Single().RequiredMods.Any(id=>!ids.Contains(id)))
                throw new IOException("Dependência vinculada não incluída: "+mod.Name+".");
        foreach(var recipe in recipes.Values)foreach(string dependency in recipe.Dependencies)if(!recipes.Values.Any(m=>m.Id==dependency))throw new IOException("Dependência não incluída: "+dependency);
        var keys=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var regular=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0,configs=0;
        var reserved=new HashSet<string>(catalog.Packages.SelectMany(NightCityDensity.KnownFiles).Select(f=>f.Path),StringComparer.OrdinalIgnoreCase);
        foreach(var file in manifest.Files) {
            if(file==null||file.Size<0||file.Size>MaxFileBytes||!FixedData.HashValid(file.Sha256)||file.Owners==null||file.Owners.Count==0||file.Owners.Count>1000||file.Owners.Distinct(StringComparer.Ordinal).Count()!=file.Owners.Count||file.Owners.Any(o=>!owners.ContainsKey(o))||!keys.Add(file.Path+"\n"+file.Section))throw new IOException("Arquivo instalado inválido ou repetido.");
            if(manifest.Game=="cyberpunk2077"&&RetiredPackEntries.LocalDroneFile(file))
                throw new IOException("O drone FPV é local e não faz parte do pacote compartilhado.");
            Safe.In(Path.GetTempPath(),file.Path);total+=file.Size;if(total>MaxTotalBytes)throw new IOException("Pacote instalado grande demais.");
            if(GeneralInstallRoutes.External(file.Path)){
                if(manifest.Schema!=2||file.Kind!="file")throw new IOException("Destino externo não autorizado neste pacote.");
                string target=GeneralInstallRoutes.ExternalDestination(file.Path);
                if(file.Owners.Any(owner=>!owners[owner].InstallRoutes.Any(route=>String.Equals(route.Destination,target,StringComparison.OrdinalIgnoreCase))))
                    throw new IOException("O arquivo aponta para um destino ausente na versão do mod.");
            }
            if(file.Kind=="file") {
                if(file.Content!=null||file.Section!=null||!regular.Add(file.Path))throw new IOException("Arquivo binário contém metadados inválidos.");
                foreach(string owner in file.Owners) {
                    Dictionary<string,FixedFile> paths;
                    if(recipePaths.TryGetValue(owner,out paths)) {
                        FixedFile expected;
                        if(!paths.TryGetValue(file.Path,out expected)||expected.Size!=file.Size||expected.Sha256!=file.Sha256)
                            throw new IOException("Arquivo diverge da receita fixada: "+file.Path);
                    }else if(!SafeGenericPath(file.Path)||(manifest.Game=="cyberpunk2077"&&(!LocalModPackage.Allowed(file.Path)||reserved.Contains(file.Path))))throw new IOException("Destino personalizado não autorizado: "+file.Path);
                }
            }else {
                if(file.ReuseLocal)throw new IOException("Configuração local não pode ser referenciada pelo pacote.");
                InstalledSnapshotConfig.Validate(file);configs+=file.Size;if(configs>1048576)throw new IOException("Configurações grandes demais.");
                foreach(string owner in file.Owners) {
                    FixedMod recipe;
                    if(!recipes.TryGetValue(owner,out recipe)||!(recipe.Files.Any(f=>f.UserData&&f.Path==file.Path&&file.Kind=="json")||(catalog.Defaults!=null&&catalog.Defaults.Any(d=>d.ModId==recipe.Id&&d.Path==file.Path&&d.Format==file.Kind&&d.Section==file.Section))))throw new IOException("Configuração fora da lista autorizada: "+file.Path);
                }
            }
        }
        foreach(var file in manifest.Files) {
            if(file.Kind!="file"&&regular.Contains(file.Path))throw new IOException("Configuração colide com arquivo fixado.");
            for(int slash=file.Path.LastIndexOf('/');slash>=0;slash=file.Path.LastIndexOf('/',slash-1))if(manifest.Files.Any(f=>String.Equals(f.Path,file.Path.Substring(0,slash),StringComparison.OrdinalIgnoreCase)))throw new IOException("Arquivo ocupa diretório de outro arquivo.");
        }
        foreach(var group in manifest.Files.Where(f=>f.Kind=="file").GroupBy(f=>f.Sha256))if(group.Select(f=>f.Size).Distinct().Count()!=1)throw new IOException("Tamanhos divergentes para o mesmo conteúdo.");
        foreach(string owner in owners.Keys) {
            if(!manifest.Files.Any(f=>f.Kind=="file"&&f.Owners.Contains(owner)))throw new IOException("Mod sem arquivos instalados.");
            Dictionary<string,FixedFile> paths;
            if(recipePaths.TryGetValue(owner,out paths))
                foreach(var expected in paths.Keys)
                    if(!manifest.Files.Any(f=>f.Kind=="file"&&f.Owners.Contains(owner)&&f.Path==expected))
                        throw new IOException("Arquivo fixado ausente: "+expected);
        }
    }
    public static InstalledSnapshotManifest Read(string zipPath,string expectedHash,FixedCatalog catalog) {
        if(!FixedData.HashValid(expectedHash))throw new IOException("Hash de pacote inválido.");Safe.NoLinks(zipPath);
        using(var stream=new FileStream(zipPath,FileMode.Open,FileAccess.Read,FileShare.Read)) {
            if(stream.Length>MaxTotalBytes)throw new IOException("Pacote grande demais.");
            using(var sha=SHA256.Create())if(Safe.Hex(sha.ComputeHash(stream))!=expectedHash)throw new IOException("O pacote transferido não corresponde ao convite.");
            stream.Position=0;
            using(var zip=new ZipArchive(stream,ZipArchiveMode.Read,true)) {
                if(zip.Entries.Count>MaxFiles+1)throw new IOException("Pacote com entradas demais.");
                var entries=new Dictionary<string,ZipArchiveEntry>(StringComparer.Ordinal);
                foreach(var entry in zip.Entries)if(!entries.TryAddCompat(entry.FullName,entry))throw new IOException("Entrada repetida no pacote.");
                ZipArchiveEntry metadata;if(!entries.TryGetValue("manifest.json",out metadata)||metadata.Length>8000000)throw new IOException("Manifesto ausente ou grande demais.");
                InstalledSnapshotManifest manifest;
                using(var input=metadata.Open())manifest=Safe.Json.Deserialize<InstalledSnapshotManifest>(Utf8.GetString(ReadBytes(input,8000000)));
                Validate(manifest,catalog);
                var expected=manifest.Files.Where(f=>f.Kind=="file"&&!f.ReuseLocal).GroupBy(f=>f.Sha256).Select(g=>g.First()).ToList();
                if(entries.Count!=expected.Count+1)throw new IOException("Arquivos não declarados no pacote.");
                foreach(var file in expected) {
                    ZipArchiveEntry entry;if(!entries.TryGetValue(Entry(file),out entry)||entry.Length!=file.Size)throw new IOException("Arquivo ausente ou tamanho divergente: "+file.Path);
                    using(var input=entry.Open())if(HashStream(input,file.Size)!=file.Sha256)throw new IOException("Arquivo transferido divergente: "+file.Path);
                }
                foreach(var file in manifest.Files.Where(f=>f.Kind=="file"&&!f.ReuseLocal))using(var input=entries[Entry(file)].Open())ValidateCustomData(manifest,file,input);
                return manifest;
            }
        }
    }
    internal static void ValidateCustomData(InstalledSnapshotManifest manifest,InstalledSnapshotFile file,Stream input) {
        if(IsCustomConfig(file.Path)&&file.Owners.Any(owner=>manifest.List.Mods.Any(m=>m.Variants.Any(v=>Owner(m,v)==owner&&v.CyberpunkId==null))))
            ValidateCustomConfig(file,input);
    }
    static bool IsCustomConfig(string path){
        string extension=System.IO.Path.GetExtension(path).ToLowerInvariant();
        return extension==".json"||extension==".ini"||extension==".toml";
    }
    static void ValidateCustomConfig(InstalledSnapshotFile file,Stream input){
        if(file.Size>262144)throw new IOException("Configuração personalizada grande demais: "+file.Path);
        using(var memory=new MemoryStream()) {
            byte[] content=ReadBytes(input,262144);memory.Write(content,0,content.Length);
            string text=InstalledSnapshotConfig.Decode(memory.ToArray());
            if(System.IO.Path.GetExtension(file.Path).Equals(".json",StringComparison.OrdinalIgnoreCase))InstalledSnapshotConfig.Object(text);
            else if(Regex.IsMatch(text,@"(?im)^\s*[^\r\n=]*(?:password|passwd|secret|credential|oauth|cookie|bearer|authorization|access.?token|refresh.?token|client.?id|api.?key|\btoken\b)[^\r\n=]*="))throw new IOException("Configuração personalizada contém dados privados: "+file.Path);
        }
    }
    internal static byte[] ReadBytes(Stream input,int maximum) {
        using(var memory=new MemoryStream()) {
            byte[] buffer=new byte[16384];int count;
            while((count=input.Read(buffer,0,buffer.Length))>0){if(memory.Length+count>maximum)throw new IOException("Entrada de dados excede o limite permitido.");memory.Write(buffer,0,count);}
            return memory.ToArray();
        }
    }
    static string HashStream(Stream input,long expected) {
        using(var sha=SHA256.Create()) {
            byte[] buffer=new byte[1024*1024];long total=0;int count;
            while((count=input.Read(buffer,0,buffer.Length))>0){total+=count;if(total>expected)throw new IOException("Entrada ZIP excede o tamanho declarado.");sha.TransformBlock(buffer,0,count,null,0);}
            if(total!=expected)throw new IOException("Entrada ZIP menor que o tamanho declarado.");sha.TransformFinalBlock(new byte[0],0,0);return Safe.Hex(sha.Hash);
        }
    }
    static bool TryAddCompat<TKey,TValue>(this Dictionary<TKey,TValue> values,TKey key,TValue value){if(values.ContainsKey(key))return false;values.Add(key,value);return true;}
}
