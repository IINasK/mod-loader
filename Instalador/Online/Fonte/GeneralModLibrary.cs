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
    public List<GeneralModEntry> Mods = new List<GeneralModEntry>();
}

public sealed class GeneralModPreset {
    public string Id, Name;
    public List<string> ModIds = new List<string>();
}

public sealed class GeneralModEntry {
    public string Id, Name, Category, DefaultVariant;
    public string Description;
    public List<string> Tags = new List<string>();
    public bool Nsfw;
    public List<GeneralModVariant> Variants = new List<GeneralModVariant>();
}

public sealed class GeneralModVariant {
    public string Id, Name, CyberpunkId, CyberpunkVariant;
    public List<string> Sources = new List<string>();
}

public sealed class GeneralModState {
    public string Destination, DownloadFolder;
    public string Theme = "light", Language = "pt", SelectedPreset;
    public GeneralModList List = new GeneralModList();
    public Dictionary<string, string> ChosenVariants = new Dictionary<string, string>();
    public Dictionary<string, string> ResolvedDownloads = new Dictionary<string, string>();
    public Dictionary<string, string> CyberpunkImports = new Dictionary<string, string>();
}

public sealed class GeneralModLibrary {
    const int MaxJsonBytes = 8000000;
    static readonly string[] CyberpunkCategories = {
        null, "Kit principal", "Núcleo e dependências", "Jogabilidade", "Lojas", "Veículos",
        "Roupas e acessórios", "Aparência", "Modo Foto", "Áudio", "Importados", "Outros"
    };
    readonly string statePath;

    public GeneralModLibrary(string statePath) {
        this.statePath = Path.GetFullPath(statePath);
    }

    public GeneralModState Load() {
        Safe.NoLinks(statePath);
        if (!File.Exists(statePath)) return null;
        var state = Read<GeneralModState>(statePath, "List");
        ValidateList(state.List, true);
        ValidateChoices(state);
        return state;
    }

    public void Save(GeneralModState state) {
        if (state == null) throw new IOException("Estado do Mod Loader inválido.");
        ValidateList(state.List, true);
        ValidateChoices(state);
        Write(statePath, state);
    }

    public void Export(string path, GeneralModList list) {
        ValidateList(list, true);
        string fullPath = Path.GetFullPath(path);
        if (String.Equals(fullPath, statePath, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Escolha outro nome para a lista exportada.");
        Write(fullPath, new {
            list.Schema, list.Categories, list.CategoryDescriptions, list.Presets, list.UncategorizedName, list.UncategorizedDescription,
            Mods = list.Mods.Select(mod => new {
                mod.Id, mod.Name, mod.Category, mod.DefaultVariant, mod.Description, mod.Tags, mod.Nsfw,
                Variants = mod.Variants.Select(variant => new { variant.Id, variant.Name, variant.Sources }).ToList()
            }).ToList()
        });
    }

    public GeneralModList Import(string path) {
        var list = Read<GeneralModList>(Path.GetFullPath(path), "Schema", "Categories", "Mods");
        ValidateList(list, false);
        return list;
    }

    public static void RestoreKnownMappings(GeneralModList list, GeneralModList trustedSeed) {
        ValidateList(list, false);
        var byId = trustedSeed.Mods.ToDictionary(mod => mod.Id, StringComparer.Ordinal);
        foreach (var entry in list.Mods) {
            GeneralModEntry trusted;
            if (!byId.TryGetValue(entry.Id, out trusted)) continue;
            foreach (var variant in entry.Variants) {
                var original = trusted.Variants.FirstOrDefault(candidate => candidate.Id == variant.Id && candidate.Sources.SequenceEqual(variant.Sources, StringComparer.Ordinal));
                if (original == null) continue;
                variant.CyberpunkId = original.CyberpunkId;
                variant.CyberpunkVariant = original.CyberpunkVariant;
            }
        }
    }

    public static GeneralModList Seed(FixedCatalog catalog, IEnumerable<ModImportItem> items) {
        var list = new GeneralModList { Categories = CyberpunkCategories.Skip(1).ToList() };
        list.Categories.AddRange(new[]{"Roupas femininas","Roupas masculinas","Roupas para ambas"});
        foreach (var mod in catalog.Packages.Where(mod => String.IsNullOrWhiteSpace(mod.BlockedReason))) {
            var fit=ModCategories.Supports(mod.Id,ClothingFit.Both)?"Roupas para ambas":
                ModCategories.Supports(mod.Id,ClothingFit.Feminine)?"Roupas femininas":
                ModCategories.Supports(mod.Id,ClothingFit.Masculine)?"Roupas masculinas":null;
            var entry = new GeneralModEntry {
                Id = mod.Id, Name = mod.Name,
                Category = fit??CyberpunkCategories[(int)ModCategories.CategoryOf(mod.Id, mod.Profile)],
                DefaultVariant = mod.DensityProfiles == null ? "default" : "low"
            };
            if (mod.DensityProfiles == null) {
                entry.Variants.Add(FromFixed(mod.Id, "default", "Principal", mod.Assets, null));
            } else {
                foreach (var profile in mod.DensityProfiles)
                    entry.Variants.Add(FromFixed(mod.Id, profile.Id, profile.Name, profile.Assets, profile.Id));
            }
            list.Mods.Add(entry);
        }
        foreach (var item in items.Where(item => item.Kind == "Arquivo")) {
            list.Mods.Add(new GeneralModEntry {
                Id = item.Id, Name = item.Name, Category = CyberpunkCategories[(int)ModCategory.Imported], DefaultVariant = "default",
                Variants = new List<GeneralModVariant> { new GeneralModVariant {
                    Id = "default", Name = "Principal", Sources = new List<string> { item.Source }
                } }
            });
        }
        list.Presets.Add(new GeneralModPreset { Id = "all", Name = "Todos", ModIds = list.Mods.Select(mod => mod.Id).ToList() });
        list.Presets.Add(new GeneralModPreset {
            Id = "no-pl", Name = "Sem Phantom Liberty",
            ModIds = catalog.Packages.Where(mod => String.IsNullOrWhiteSpace(mod.BlockedReason) &&
                FixedData.Expand(catalog, new[] { mod.Id }).All(dependency => !GameEdition.NeedsPL(dependency) && String.IsNullOrWhiteSpace(dependency.BlockedReason)))
                .Select(mod => mod.Id).ToList()
        });
        ValidateList(list, true);
        return list;
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
        if (GeneralModDownload.IsArchivePath(value)) return Path.GetFullPath(value);
        if (!GeneralModDownload.IsWebSource(value))
            throw new IOException("Use o caminho completo de um ZIP, RAR ou 7z, ou um link HTTPS público.");
        var uri = new Uri(value);
        if (uri.Fragment.Length != 0) throw new IOException("Use o endereço público sem o trecho depois de #.");
        if (uri.Query.Length != 0 && !IsPublicNexusQuery(uri))
            throw new IOException("Use o link público sem parâmetros de sessão, assinatura ou outros dados privados.");
        return uri.AbsoluteUri;
    }

    static bool IsPublicNexusQuery(Uri uri) {
        if (!(uri.Host == "www.nexusmods.com" || uri.Host == "nexusmods.com") || !uri.IsDefaultPort ||
            !Regex.IsMatch(uri.AbsolutePath, @"^/[^/]+/mods/[0-9]+/?$", RegexOptions.IgnoreCase)) return false;
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

    static void ValidateList(GeneralModList list, bool trustRecipes) {
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
                if (variant == null || !ValidId(variant.Id) || !variantIds.Add(variant.Id) || variant.Sources == null || variant.Sources.Count == 0 || variant.Sources.Count > 32)
                    throw new IOException("Versão inválida ou repetida em " + mod.Name + ".");
                variant.Name = RequiredText(variant.Name, 300, "Nome da versão");
                for (int index = 0; index < variant.Sources.Count; index++) variant.Sources[index] = ValidateSource(variant.Sources[index]);
                if (!trustRecipes) { variant.CyberpunkId = null; variant.CyberpunkVariant = null; }
            }
            if (!variantIds.Contains(mod.DefaultVariant)) throw new IOException("Escolha a versão principal de " + mod.Name + ".");
        }
        if (list.Presets == null) list.Presets = new List<GeneralModPreset>();
        if (list.Presets.Count > 100) throw new IOException("Use no máximo 100 presets por lista.");
        var presetIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var preset in list.Presets) {
            if (preset == null || !ValidId(preset.Id) || !presetIds.Add(preset.Id) || preset.ModIds == null || preset.ModIds.Count > 1000)
                throw new IOException("A lista contém um preset inválido ou repetido.");
            preset.Name = RequiredText(preset.Name, 300, "Nome do preset");
            var members = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in preset.ModIds)
                if (id == null || !ids.Contains(id) || !members.Add(id)) throw new IOException("O preset contém um mod desconhecido ou repetido.");
        }
    }

    static void ValidateChoices(GeneralModState state) {
        state.Theme = String.IsNullOrEmpty(state.Theme) ? "light" : state.Theme;
        state.Language = String.IsNullOrEmpty(state.Language) ? "pt" : state.Language;
        if (state.Theme != "light" && state.Theme != "dark") throw new IOException("Tema inválido.");
        if (state.Language != "pt" && state.Language != "en") throw new IOException("Idioma inválido.");
        if (!String.IsNullOrEmpty(state.SelectedPreset) && !ValidId(state.SelectedPreset)) throw new IOException("Preset selecionado inválido.");
        if (!state.List.Presets.Any(preset => preset.Id == state.SelectedPreset)) state.SelectedPreset = null;
        if (state.ChosenVariants == null) state.ChosenVariants = new Dictionary<string, string>();
        if (state.ResolvedDownloads == null) state.ResolvedDownloads = new Dictionary<string, string>();
        if (state.CyberpunkImports == null) state.CyberpunkImports = new Dictionary<string, string>();
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

    static T Read<T>(string path, params string[] requiredFields) {
        Safe.NoLinks(path);
        try {
            using (var stream = File.OpenRead(path)) {
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
