using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public sealed class GeneralModList {
    public int Schema = 1;
    public string UncategorizedName = "Sem categoria", UncategorizedDescription;
    public List<string> Categories = new List<string>();
    public Dictionary<string, string> CategoryDescriptions = new Dictionary<string, string>();
    public List<GeneralModPreset> Presets = new List<GeneralModPreset>();
    public Dictionary<string,GeneralAlternativeGroup> AlternativeGroups = new Dictionary<string,GeneralAlternativeGroup>(StringComparer.Ordinal);
    public List<GeneralModEntry> Mods = new List<GeneralModEntry>();
}

public sealed class GeneralModPreset {
    public string Id, Name;
    public List<string> ModIds = new List<string>();
    public List<string> Categories = new List<string>();
}

public sealed class GeneralAlternativeGroup {
    public string Name, Color;
}

public sealed class GeneralModEntry {
    public string Id, Name, Category, DefaultVariant, AlternativeGroup;
    public string Description, DependencySource;
    public int DependencyFile;
    public List<string> Tags = new List<string>();
    public List<string> CompatibilityGroups = new List<string>();
    public bool Nsfw;
    public bool? NsfwOverride;
    public List<string> DetectedPaths = new List<string>();
    public List<GeneralModVariant> Variants = new List<GeneralModVariant>();
}

public sealed class GeneralModVariant {
    public string Id, Name, CyberpunkId, CyberpunkVariant;
    public List<string> RequiredMods = new List<string>();
    public List<GeneralInstallRoute> InstallRoutes = new List<GeneralInstallRoute>();
    public List<string> CompatibilityGroups = new List<string>();
    public List<string> Sources = new List<string>();
    public Dictionary<string,GeneralSourceInfo> SourceInfo = new Dictionary<string,GeneralSourceInfo>();
}

public sealed class GeneralModState {
    public string Destination, DownloadFolder;
    internal string TemplateId, ProjectName;
    internal bool TemplateSubset;
    public string Theme = "light", Language = "pt", SelectedPreset;
    public GeneralModList List = new GeneralModList();
    public Dictionary<string, string> ChosenVariants = new Dictionary<string, string>();
    public Dictionary<string, string> ResolvedDownloads = new Dictionary<string, string>();
    public Dictionary<string, string> CyberpunkImports = new Dictionary<string, string>();
    public Dictionary<string, string> SnapshotReceipts = new Dictionary<string, string>();
}

public sealed class GeneralModLibrary {
    const int MaxJsonBytes = 8000000;
    static readonly string[] CyberpunkCategories = {
        null, "Kit principal", "Núcleo e dependências", "Jogabilidade", "Lojas", "Veículos",
        "Roupas e acessórios", "Aparência", "Modo Foto", "Áudio", "Importados", "Outros"
    };
    internal const string BodyCategory = "Melhorias do corpo";
    internal const string VanillaFeetId = "mod-7049";
    const string BodyGroup = "hyst-femv-body";
    internal const string EclipseTattooId = "mod-33230";
    internal static bool IsHystBody(string id) { return id == "hyst-ebb-rb" || id == "hyst-ebbp-rb"; }
    internal static bool IsFacialMod(string id) { return id.StartsWith("face-", StringComparison.Ordinal); }
    readonly string statePath;

    public GeneralModLibrary(string statePath) {
        this.statePath = Path.GetFullPath(statePath);
    }

    public GeneralModState Load(){return LoadForInspection(false);}
    internal GeneralModState LoadForInspection(bool inspection) {
        Safe.NoLinks(statePath);
        if (!File.Exists(statePath)) return null;
        var state = Read<GeneralModState>(statePath, inspection,"List");
        ValidateList(state.List, true);
        ValidateChoices(state);
        RetiredPackEntries.Remove(state);
        ApplyAuditedAdultFlags(state.List);
        return state;
    }

    public void Save(GeneralModState state) {
        if (state == null) throw new IOException("Estado do Mod Loader inválido.");
        ValidateList(state.List, true);
        ValidateChoices(state);
        ApplyAuditedAdultFlags(state.List);
        Write(statePath, state);
    }

    public void Export(string path, GeneralModList list) {
        ValidateList(list, true);
        ApplyAuditedAdultFlags(list);
        string fullPath = Path.GetFullPath(path);
        if (String.Equals(fullPath, statePath, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Escolha outro nome para a lista exportada.");
        Write(fullPath, new {
            list.Schema, list.Categories, list.CategoryDescriptions, list.Presets, list.UncategorizedName, list.UncategorizedDescription, list.AlternativeGroups,
            Mods = list.Mods.Select(mod => new {
                mod.Id, mod.Name, mod.Category, mod.DefaultVariant, mod.Description, mod.Tags, mod.CompatibilityGroups, mod.Nsfw, mod.NsfwOverride, mod.AlternativeGroup, mod.DependencySource, mod.DependencyFile,
                Variants = mod.Variants.Select(variant => new { variant.Id, variant.Name, variant.Sources, variant.SourceInfo, variant.CompatibilityGroups, variant.RequiredMods, variant.InstallRoutes }).ToList()
            }).ToList()
        });
    }

    internal static List<string> ValidateCompatibilityGroups(IEnumerable<string> groups) {
        var valid = (groups ?? Enumerable.Empty<string>()).Select(group => RequiredText(group, 18, "Grupo"))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (valid.Count > 12 || valid.Any(group => group.Contains(",")))
            throw new IOException("Use até 12 grupos de 18 caracteres, separados por vírgulas.");
        return valid;
    }

    internal static string ChoiceKey(GeneralModEntry mod) {
        return mod.AlternativeGroup==null?"mod/"+mod.Id:"choice/"+mod.AlternativeGroup;
    }

    internal static string GroupDefaultName(string id) {
        return id==BodyGroup?GeneralStyle.T("Corpo","Body"):GeneralStyle.T("Alternativas","Alternatives");
    }

    internal static bool GroupBodyOptions(GeneralModList list) {
        if(!list.Mods.Any(mod=>IsHystBody(mod.Id)))return false;
        var ungrouped=list.Mods.Where(mod=>(mod.Id==VanillaFeetId||IsHystBody(mod.Id))&&mod.AlternativeGroup!=BodyGroup).ToList();
        foreach(var mod in ungrouped)mod.AlternativeGroup=BodyGroup;
        return ungrouped.Count!=0;
    }

    internal static bool GroupColorAvailable(GeneralModList list,string groupId,string color) {
        return !list.AlternativeGroups.Any(pair=>pair.Key!=groupId&&String.Equals(pair.Value.Color,color,StringComparison.OrdinalIgnoreCase));
    }

    internal static string DefaultGroupColor(GeneralModList list,string id) {
        int seed=PackCoverage.CategoryColor(id).ToArgb() & 0xffffff;
        for(int offset=0;;offset++){
            int shade=(seed+offset*191)&4095;
            string color="#"+(220+shade%16).ToString("X2")+(141+shade/16%32).ToString("X2")+(9+shade/512*5).ToString("X2");
            if(GroupColorAvailable(list,id,color))return color;
        }
    }

    static void ValidateAlternativeGroups(GeneralModList list) {
        var source=list.AlternativeGroups??new Dictionary<string,GeneralAlternativeGroup>();
        if(source.Count>1000)throw new IOException("A lista contém grupos demais.");
        var ids=list.Mods.Select(mod=>mod.AlternativeGroup).Where(id=>id!=null).Distinct(StringComparer.Ordinal).OrderBy(id=>id,StringComparer.Ordinal).ToList();
        list.AlternativeGroups=new Dictionary<string,GeneralAlternativeGroup>(StringComparer.Ordinal);
        foreach(string id in ids){
            GeneralAlternativeGroup group;
            if(!source.TryGetValue(id,out group))continue;
            if(group==null||!Regex.IsMatch(group.Color??"",@"\A#[0-9a-fA-F]{6}\z")||!GroupColorAvailable(list,id,group.Color))
                throw new IOException("Use uma cor diferente para cada grupo.");
            string name=String.IsNullOrWhiteSpace(group.Name)?null:RequiredText(group.Name,18,"Grupo");
            list.AlternativeGroups.Add(id,new GeneralAlternativeGroup{Name=name,Color=group.Color.ToUpperInvariant()});
        }
        foreach(string id in ids.Where(id=>!list.AlternativeGroups.ContainsKey(id)))
            list.AlternativeGroups.Add(id,new GeneralAlternativeGroup{Color=DefaultGroupColor(list,id)});
    }

    internal static void AssociateAlternative(GeneralModEntry entry, GeneralModEntry other) {
        if (other == null) { entry.AlternativeGroup = null; return; }
        if (entry.Id == other.Id) throw new IOException("Escolha outro mod para associar.");
        if (String.IsNullOrEmpty(other.AlternativeGroup)) other.AlternativeGroup = Guid.NewGuid().ToString("N");
        entry.AlternativeGroup = other.AlternativeGroup;
    }
    public static void DeleteCategory(GeneralModList list, string category) {
        string requested = RequiredText(category, 120, "Categoria");
        ValidateList(list, true);
        string existing = list.Categories.FirstOrDefault(item => String.Equals(item, requested, StringComparison.OrdinalIgnoreCase));
        if (existing == null) throw new IOException("A categoria não pertence à lista. O grupo sem categoria não pode ser excluído.");
        foreach (var mod in list.Mods.Where(item => String.Equals(item.Category, existing, StringComparison.OrdinalIgnoreCase)))
            mod.Category = null;
        list.Categories.Remove(existing);
        list.CategoryDescriptions.Remove(existing);
        foreach (var preset in list.Presets) preset.Categories.RemoveAll(item => String.Equals(item, existing, StringComparison.OrdinalIgnoreCase));
    }

    public GeneralModList Import(string path) {
        var list = Read<GeneralModList>(Path.GetFullPath(path), "Schema", "Categories", "Mods");
        ValidateList(list, false);
        RetiredPackEntries.Remove(list);
        ApplyAuditedAdultFlags(list);
        return list;
    }

    public static void RestoreKnownMappings(GeneralModList list, GeneralModList trustedSeed) {
        ValidateList(list, false);
        RetiredPackEntries.Remove(list);
        var byId = trustedSeed.Mods.ToDictionary(mod => mod.Id, StringComparer.Ordinal);
        foreach (var entry in list.Mods) {
            GeneralModEntry trusted;
            if (!byId.TryGetValue(entry.Id, out trusted)) continue;
            if (entry.Id == EquipmentExOptions.ModId && trusted.Variants.Any(v => v.Id == EquipmentExOptions.StockId)) {
                var old = entry.Variants.SingleOrDefault(v => v.Id == EquipmentExOptions.WeaveId);
                var stock = trusted.Variants.Single(v => v.Id == EquipmentExOptions.StockId);
                var weave = trusted.Variants.Single(v => v.Id == EquipmentExOptions.WeaveId);
                bool oldStock = old != null && old.Sources.SequenceEqual(stock.Sources, StringComparer.Ordinal);
                bool official = oldStock || old != null && old.Sources.SequenceEqual(weave.Sources, StringComparer.Ordinal)
                    || entry.Variants.Any(v => v.Id == stock.Id && v.Sources.SequenceEqual(stock.Sources, StringComparer.Ordinal));
                if (oldStock) old.Sources = weave.Sources.ToList();
                if (official) {
                    foreach (var option in trusted.Variants) {
                        var saved = entry.Variants.SingleOrDefault(v => v.Id == option.Id);
                        if (saved == null) entry.Variants.Add(Safe.Json.Deserialize<GeneralModVariant>(Safe.Json.Serialize(option)));
                        else if (saved.Sources.SequenceEqual(option.Sources, StringComparer.Ordinal)) saved.Name = option.Name;
                    }
                    if (entry.Name == "Equipment-EX + WEAVE") entry.Name = trusted.Name;
                }
            }
            foreach (var variant in entry.Variants) {
                var original = trusted.Variants.FirstOrDefault(candidate => candidate.Id == variant.Id && candidate.Sources.SequenceEqual(variant.Sources, StringComparer.Ordinal));
                if(original==null&&entry.Id=="NightCoop"&&variant.Sources.SequenceEqual(new[]{NightCoopRelease.PriorUrl})){
                    original=trusted.Variants.SingleOrDefault(candidate=>candidate.Id==variant.Id&&candidate.Sources.SequenceEqual(new[]{NightCoopRelease.Url}));
                    if(original!=null)variant.Sources=original.Sources.ToList();
                }
                if(original==null&&entry.Id==NightCityDensity.ModId&&variant.Id=="legacy-low")original=trusted.Variants.FirstOrDefault(candidate=>candidate.CyberpunkVariant=="medium"&&candidate.Sources.SequenceEqual(variant.Sources,StringComparer.Ordinal));
                if (original == null) continue;
                variant.CyberpunkId = original.CyberpunkId;
                variant.CyberpunkVariant = entry.Id==NightCityDensity.ModId&&variant.Id=="legacy-low"?"legacy-low":original.CyberpunkVariant;
            }
        }
    }

    public static GeneralModList Seed(FixedCatalog catalog, IEnumerable<ModImportItem> items) {
        var list = new GeneralModList { Categories = CyberpunkCategories.Skip(1).ToList() };
        list.Categories.AddRange(new[]{"Roupas femininas","Roupas masculinas","Roupas para ambas",BodyCategory});
        foreach (var mod in catalog.Packages.Where(mod => String.IsNullOrWhiteSpace(mod.BlockedReason))) {
            var fit=ModCategories.Supports(mod.Id,ClothingFit.Both)?"Roupas para ambas":
                ModCategories.Supports(mod.Id,ClothingFit.Feminine)?"Roupas femininas":
                ModCategories.Supports(mod.Id,ClothingFit.Masculine)?"Roupas masculinas":null;
            var entry = new GeneralModEntry {
                Id = mod.Id, Name = mod.Name, Description = mod.Description,
                Category = IsFacialMod(mod.Id) || mod.Id == EclipseTattooId ? BodyCategory : fit??CyberpunkCategories[(int)ModCategories.CategoryOf(mod.Id, mod.Profile)],
                DefaultVariant = mod.DensityProfiles == null ? "default" : "low"
            };
            if (EquipmentExOptions.Available(mod)) {
                entry.Name = "Equipment-EX";
                var stock = EquipmentExOptions.Resolve(mod, EquipmentExOptions.StockId);
                entry.Variants.Add(FromFixed(mod.Id, EquipmentExOptions.WeaveId, "Com WEAVE (padrão)", mod.Assets, null));
                entry.Variants.Add(FromFixed(mod.Id, EquipmentExOptions.StockId, "Original (sem WEAVE)", stock.Assets, EquipmentExOptions.StockId));
                list.Mods.Add(entry);
                continue;
            }
            if (mod.DensityProfiles == null) {
                entry.Variants.Add(FromFixed(mod.Id, "default", "Principal", mod.Assets, null));
            } else {
                foreach (var profile in mod.DensityProfiles)
                    entry.Variants.Add(FromFixed(mod.Id, profile.Id, profile.Name, profile.Assets, profile.Id));
            }
            list.Mods.Add(entry);
        }
        if (list.Mods.Any(mod => mod.Id == "face-16930-99414")) {
            using (var reader = new StreamReader(typeof(GeneralModLibrary).Assembly.GetManifestResourceStream("ModLoader.Hyst.modloader.json"))) {
                var hyst = Safe.Json.Deserialize<GeneralModList>(reader.ReadToEnd());
                list.Mods.AddRange(hyst.Mods.Where(mod => IsHystBody(mod.Id)));
            }
        }
        foreach (var item in items.Where(item => item.Kind == "Arquivo"&&!RetiredPackEntries.PreparedArchive(item))) {
            list.Mods.Add(new GeneralModEntry {
                Id = item.Id, Name = item.Name, Category = CyberpunkCategories[(int)ModCategory.Imported], DefaultVariant = "default",
                Variants = new List<GeneralModVariant> { new GeneralModVariant {
                    Id = "default", Name = "Principal", Sources = new List<string> { item.Source }
                } }
            });
        }
        GroupBodyOptions(list);
        list.Presets.Add(new GeneralModPreset { Id = "all", Name = "Com DLC Phantom Liberty", ModIds = list.Mods.Select(mod => mod.Id).ToList() });
        list.Presets.Add(new GeneralModPreset {
            Id = "no-pl", Name = "Sem DLC Phantom Liberty",
            ModIds = catalog.Packages.Where(mod => String.IsNullOrWhiteSpace(mod.BlockedReason) &&
                FixedData.Expand(catalog, new[] { mod.Id }).All(dependency => !GameEdition.NeedsPL(dependency) && String.IsNullOrWhiteSpace(dependency.BlockedReason)))
                .Select(mod => mod.Id).Concat(list.Mods.Where(mod => IsHystBody(mod.Id)).Select(mod => mod.Id)).ToList()
        });
        ValidateList(list, true);
        ApplyAuditedAdultFlags(list);
        return list;
    }

    internal static bool NeedsDependencyVersion(GeneralModEntry mod,GeneralModVariant variant) {
        if(mod.DependencySource==null)return false;
        var expected=GeneralNexusLink.Parse(mod.DependencySource);
        var links=variant.Sources.Select(GeneralNexusLink.Parse)
            .Where(link=>link!=null&&link.Game==expected.Game&&link.Mod==expected.Mod).ToList();
        return !links.Any(link=>link.File>0&&(mod.DependencyFile==0||link.File==mod.DependencyFile))||links.Any(link=>link.File==0);
    }

    static void ApplyAuditedAdultFlags(GeneralModList list) {
        foreach(var mod in list.Mods)
            mod.Nsfw=mod.NsfwOverride??(mod.Nsfw||mod.Variants.SelectMany(variant=>variant.Sources).Any(GeneralNexusInfo.IsAuditedAdultSource));
    }

    static GeneralModVariant FromFixed(string modId, string variantId, string name, List<FixedAsset> assets, string density) {
        var sources = assets.Select(asset => asset.ModId == 0 ? asset.Url :
            "https://www.nexusmods.com/cyberpunk2077/mods/" + asset.ModId + "?tab=files&file_id=" + asset.FileId);
        return new GeneralModVariant {
            Id = variantId, Name = name, CyberpunkId = modId, CyberpunkVariant = density,
            Sources = sources.Distinct(StringComparer.Ordinal).ToList()
        };
    }

    public static string ValidateSource(string source) {
        string value = RequiredText(source, 4096, "Fonte do mod");
        if(GeneralModDownload.IsInstalledSource(value))return value;
        if (GeneralModDownload.IsArchivePath(value)) return Path.GetFullPath(value);
        if (!GeneralModDownload.IsWebSource(value))
            throw new IOException("Use o caminho completo de um ZIP, RAR ou 7z, ou um link HTTPS público.");
        var uri = new Uri(value);
        if (uri.Fragment.Length != 0) throw new IOException("Use o endereço público sem o trecho depois de #.");
        if (uri.Query.Length != 0 && !IsPublicNexusQuery(uri))
            throw new IOException("Use o link público sem parâmetros de sessão, assinatura ou outros dados privados.");
        return uri.AbsoluteUri;
    }

    internal static void NormalizeSources(GeneralModVariant variant){
        var info=new Dictionary<string,GeneralSourceInfo>();foreach(var pair in variant.SourceInfo)info[ValidateSource(pair.Key)]=pair.Value;
        variant.Sources=variant.Sources.Select(ValidateSource).Distinct().ToList();variant.SourceInfo=info;
    }

    static bool IsPublicNexusQuery(Uri uri) {
        if (!(uri.Host == "www.nexusmods.com" || uri.Host == "nexusmods.com") || !uri.IsDefaultPort ||
            !Regex.IsMatch(uri.AbsolutePath, @"^/(?:[a-z]{2}/)?(?:games/)?[a-z0-9][a-z0-9_-]{0,99}/mods/[0-9]+/?$", RegexOptions.IgnoreCase)) return false;
        var parameters = new HashSet<string>(StringComparer.Ordinal);
        foreach (string pair in uri.Query.Substring(1).Split('&')) {
            string[] parts = pair.Split('=');
            if (parts.Length != 2 || !parameters.Add(parts[0])) return false;
            if (parts[0] == "file_id" && Regex.IsMatch(parts[1], @"^[1-9][0-9]{0,9}$")) continue;
            if (parts[0] == "tab" && Regex.IsMatch(parts[1], @"^(files|description|posts|images|logs|bugs|stats|videos)$")) continue;
            return false;
        }
        return true;
    }

    internal static void ValidateList(GeneralModList list, bool trustRecipes) {
        if (list == null || list.Schema != 1 || list.Mods == null || list.Mods.Count > 1000 || list.Categories == null || list.Categories.Count > 1000)
            throw new IOException("A lista de mods está inválida ou excede o limite.");
        list.UncategorizedName = String.IsNullOrWhiteSpace(list.UncategorizedName) ? "Sem categoria" : RequiredText(list.UncategorizedName, 120, "Nome do grupo sem categoria");
        list.UncategorizedDescription = OptionalDescription(list.UncategorizedDescription);
        var categories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < list.Categories.Count; index++) {
            string category = RequiredText(list.Categories[index], 120, "Categoria");
            if (categories.ContainsKey(category)) throw new IOException("A lista contém categorias repetidas.");
            categories.Add(category, category);
            list.Categories[index] = category;
        }
        if (list.CategoryDescriptions == null) list.CategoryDescriptions = new Dictionary<string, string>();
        if (list.CategoryDescriptions.Count > 1000) throw new IOException("A lista contém descrições de categorias demais.");
        var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var description in list.CategoryDescriptions) {
            string category;
            if (!categories.TryGetValue(description.Key.Trim(), out category) || descriptions.ContainsKey(category))
                throw new IOException("A descrição deve pertencer a uma categoria única da lista.");
            descriptions.Add(category, OptionalDescription(description.Value));
        }
        list.CategoryDescriptions = descriptions;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mod in list.Mods) {
            if (mod == null || !ValidId(mod.Id) || !ids.Add(mod.Id) || mod.Variants == null || mod.Variants.Count == 0 || mod.Variants.Count > 50)
                throw new IOException("A lista contém um mod inválido ou repetido.");
            mod.Name = RequiredText(mod.Name, 300, "Nome do mod");
            mod.Description = OptionalDescription(mod.Description);
            if(!trustRecipes||mod.DetectedPaths==null)mod.DetectedPaths=new List<string>();
            if(mod.DetectedPaths.Count>2000||mod.DetectedPaths.Any(path=>!FixedInstaller.SyncModPath(path)))
                throw new IOException("Caminhos detectados inválidos em "+mod.Name+".");
            mod.DetectedPaths=mod.DetectedPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if(mod.DependencyFile<0||mod.DependencyFile>0&&mod.DependencySource==null)
                throw new IOException("Versão da dependência inválida.");
            if(mod.DependencySource!=null){
                var dependency=GeneralNexusLink.Parse(mod.DependencySource);
                if(dependency==null||dependency.File!=0)throw new IOException("Origem de dependência inválida.");
                mod.DependencySource=dependency.Url;
            }
            mod.CompatibilityGroups = ValidateCompatibilityGroups(mod.CompatibilityGroups);
            if (mod.Tags == null) mod.Tags = new List<string>();
            if (mod.Tags.Count > 32) throw new IOException("Use no máximo 32 tags por mod.");
            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < mod.Tags.Count; index++) {
                mod.Tags[index] = RequiredText(mod.Tags[index], 60, "Tag");
                if (!tags.Add(mod.Tags[index])) throw new IOException("O mod contém tags repetidas.");
            }
            if (!String.IsNullOrWhiteSpace(mod.Category)) {
                string category;
                if (!categories.TryGetValue(mod.Category.Trim(), out category)) throw new IOException("Categoria desconhecida em " + mod.Name + ".");
                mod.Category = category;
            } else mod.Category = null;
            var variantIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var variant in mod.Variants) {
                if (variant != null) GeneralInstallRoutes.Validate(variant.InstallRoutes);
                if (variant == null || !ValidId(variant.Id) || !variantIds.Add(variant.Id) || variant.Sources == null || variant.Sources.Count == 0 || variant.Sources.Count > 32)
                    throw new IOException("Versão inválida ou repetida em " + mod.Name + ".");
                variant.Name = RequiredText(variant.Name, 300, "Nome da versão");
                variant.CompatibilityGroups = ValidateCompatibilityGroups(variant.CompatibilityGroups);
                if(variant.RequiredMods==null)variant.RequiredMods=new List<string>();
                if(variant.RequiredMods.Count>32||variant.RequiredMods.Any(id=>!ValidId(id)||id==mod.Id)||
                    variant.RequiredMods.Distinct(StringComparer.Ordinal).Count()!=variant.RequiredMods.Count)
                    throw new IOException("Vínculos de dependência inválidos em "+mod.Name+".");
                if(variant.SourceInfo==null)variant.SourceInfo=new Dictionary<string,GeneralSourceInfo>();
                if(variant.SourceInfo.Count>32||variant.SourceInfo.Any(pair=>!variant.Sources.Contains(pair.Key)||pair.Value==null||pair.Value.Bytes<0||pair.Value.Bytes>128L*1024*1024*1024||String.IsNullOrWhiteSpace(pair.Value.Name)||pair.Value.Name.Length>300||pair.Value.Name.Any(Char.IsControl)||(pair.Value.FileName??"").Length>300||(pair.Value.FileName??"").Any(Char.IsControl)||!ValidSourceVersion(pair.Value.Version)||!ValidSourceVersion(pair.Value.ModVersion)))throw new IOException("Metadados de arquivo inválidos.");
                NormalizeSources(variant);
                if (!trustRecipes) { variant.CyberpunkId = null; variant.CyberpunkVariant = null; }
            }
            if (String.IsNullOrEmpty(mod.AlternativeGroup)) mod.AlternativeGroup = null;
            else if (!ValidId(mod.AlternativeGroup)) throw new IOException("Associação de alternativas inválida.");
            if (!variantIds.Contains(mod.DefaultVariant)) throw new IOException("Escolha a versão principal de " + mod.Name + ".");
        }
        ValidateAlternativeGroups(list);
        if (list.Presets == null) list.Presets = new List<GeneralModPreset>();
        if (list.Presets.Count > 100) throw new IOException("Use no máximo 100 presets por lista.");
        var presetIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var preset in list.Presets) {
            if (preset == null || !ValidId(preset.Id) || !presetIds.Add(preset.Id) || preset.ModIds == null || preset.ModIds.Count > 1000)
                throw new IOException("A lista contém um preset inválido ou repetido.");
            preset.Name = RequiredText(preset.Name, 300, "Nome do preset");
            if (preset.Categories == null) preset.Categories = new List<string>();
            if (preset.Categories.Count > 1000) throw new IOException("O preset contém categorias demais.");
            var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string category in preset.Categories)
                if (category == null || !list.Categories.Contains(category, StringComparer.OrdinalIgnoreCase) || !groups.Add(category))
                    throw new IOException("O preset contém uma categoria desconhecida ou repetida.");
            var members = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in preset.ModIds)
                if (id == null || !ids.Contains(id) || !members.Add(id)) throw new IOException("O preset contém um mod desconhecido ou repetido.");
        }
    }

    static void ValidateChoices(GeneralModState state) {
        state.Theme = String.IsNullOrEmpty(state.Theme) ? "light" : state.Theme;
        state.Language = String.IsNullOrEmpty(state.Language) ? "pt" : state.Language;
        if (state.Theme != "light" && state.Theme != "dark" && state.Theme != "light-red" && state.Theme != "black") throw new IOException("Tema inválido.");
        if (state.Language != "pt" && state.Language != "en") throw new IOException("Idioma inválido.");
        if (!String.IsNullOrEmpty(state.SelectedPreset) && !ValidId(state.SelectedPreset)) throw new IOException("Preset selecionado inválido.");
        if (!state.List.Presets.Any(preset => preset.Id == state.SelectedPreset)) state.SelectedPreset = null;
        if (state.ChosenVariants == null) state.ChosenVariants = new Dictionary<string, string>();
        if (state.ResolvedDownloads == null) state.ResolvedDownloads = new Dictionary<string, string>();
        if (state.CyberpunkImports == null) state.CyberpunkImports = new Dictionary<string, string>();
        if (state.SnapshotReceipts == null) state.SnapshotReceipts = new Dictionary<string, string>();
        if(state.SnapshotReceipts.Count>5000||state.SnapshotReceipts.Any(pair=>!Regex.IsMatch(pair.Key??"",@"\A[A-Za-z0-9_-]{1,120}/[A-Za-z0-9_-]{1,120}\z")||!Regex.IsMatch(pair.Value??"",@"\A[a-f0-9]{32}\z")))throw new IOException("Referência de pacote instalado inválida.");
        foreach(var id in state.CyberpunkImports.Values)
            if(id==null||!Regex.IsMatch(id,@"\Afile-[a-f0-9]{64}\z"))throw new IOException("Registro de importação local inválido.");
        foreach(var path in state.ResolvedDownloads.Values)
            if(!GeneralModDownload.IsArchivePath(path))throw new IOException("Referência local de download inválida.");
        var mods = state.List.Mods.ToDictionary(mod => mod.Id, StringComparer.Ordinal);
        foreach (string id in state.ChosenVariants.Keys.ToArray()) {
            GeneralModEntry mod;
            if (!mods.TryGetValue(id, out mod) || !mod.Variants.Any(variant => variant.Id == state.ChosenVariants[id]))
                state.ChosenVariants.Remove(id);
        }
    }

    static bool ValidSourceVersion(string value){return value==null||(value.Length<=120&&!value.Any(Char.IsControl));}
    static string RequiredText(string value, int maxLength, string label) {
        if (String.IsNullOrWhiteSpace(value) || value.Length > maxLength || value.Any(Char.IsControl))
            throw new IOException(label + " inválido ou vazio.");
        return value.Trim();
    }

    static string OptionalDescription(string value) {
        if (value == null) return "";
        if (value.Length > 4000 || value.Any(character => Char.IsControl(character) && character != '\r' && character != '\n' && character != '\t'))
            throw new IOException("Descrição inválida ou maior que 4000 caracteres.");
        return value.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
    }

    static bool ValidId(string id) {
        return id != null && Regex.IsMatch(id, @"\A[A-Za-z0-9_-]{1,120}\z");
    }

    static T Read<T>(string path, params string[] requiredFields){return Read<T>(path,false,requiredFields);}
    static T Read<T>(string path,bool inspection, params string[] requiredFields) {
        Safe.NoLinks(path);
        try {
            using (var stream = new FileStream(path,FileMode.Open,FileAccess.Read,inspection?FileShare.ReadWrite|FileShare.Delete:FileShare.Read)) {
                if (stream.Length > MaxJsonBytes) throw new IOException("A lista excede o limite de 8 MB.");
                using (var reader = new StreamReader(stream)) {
                    string text = reader.ReadToEnd();
                    var fields = Safe.Json.DeserializeObject(text) as Dictionary<string, object>;
                    if (fields == null || !requiredFields.All(fields.ContainsKey)) throw new IOException("Formato de lista inválido.");
                    return Safe.Json.ConvertToType<T>(fields);
                }
            }
        } catch (ArgumentException ex) { throw new IOException("Não foi possível ler a lista de mods.", ex); }
        catch (InvalidOperationException ex) { throw new IOException("Não foi possível ler a lista de mods.", ex); }
    }

    static void Write(string path, object value) {
        if (Encoding.UTF8.GetByteCount(Safe.Json.Serialize(value)) > MaxJsonBytes)
            throw new IOException("A lista excede o limite de 8 MB.");
        Safe.WriteJson(path, value);
    }
}
