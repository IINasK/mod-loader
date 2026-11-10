using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public static class GeneralModLibraryV2Tests {
    public static int Run(string parent) {
        string root = OnlineTests.NestedRoot(parent,"biblioteca-v2-");
        Directory.CreateDirectory(root);
        int passed = 0;
        Action<bool, string> check = (ok, message) => {
            if (!ok) throw new IOException(message);
            passed++;
        };
        var library = new GeneralModLibrary(Path.Combine(root, "estado.json"));
        var list = Example();
        var state = new GeneralModState {
            List = list, Destination = @"D:\PrivateGame", DownloadFolder = @"D:\PrivateDownloads",
            Theme = "dark", Language = "en", SelectedPreset = "friends"
        };
        library.Save(state);
        var loaded = library.Load();
        check(loaded.Theme == "dark" && loaded.Language == "en" && loaded.SelectedPreset == "friends", "Preferências e preset selecionado persistem.");
        check(loaded.List.Mods[0].Description == "Descrição\nDuas linhas" && loaded.List.Mods[0].Tags.SequenceEqual(new[] { "Veículos", "Casual" }) && loaded.List.Mods[0].Nsfw, "Descrição multilinha, tags e NSFW persistem e são normalizados.");
        check(loaded.List.CategoryDescriptions["Coleção"] == "Mods favoritos" && loaded.List.UncategorizedName == "Avulsos" && loaded.List.UncategorizedDescription == "Para organizar depois", "Descrições e nome do grupo sem categoria persistem.");
        check(loaded.List.Presets.Single().ModIds.SequenceEqual(new[] { "custom" }), "Preset mantém membros explícitos.");

        string export = Path.Combine(root, "lista.json");
        library.Export(export, loaded.List);
        string portable = File.ReadAllText(export);
        check(!new[] { "Destination", "DownloadFolder", "ResolvedDownloads", "ChosenVariants", "CyberpunkId", "Theme", "Language", "SelectedPreset", "PrivateGame", "PrivateDownloads" }.Any(portable.Contains), "Exportação exclui preferências e caminhos de máquina fora das referências de mods.");
        var imported = library.Import(export);
        check(imported.Schema == 1 && imported.Mods[0].Nsfw && imported.Mods[0].Tags.SequenceEqual(list.Mods[0].Tags) && imported.Presets[0].Name == "Para amigos" && imported.CategoryDescriptions.Count == 1 && imported.UncategorizedName == "Avulsos", "Metadados novos sobrevivem ao compartilhamento no schema 1.");
        var linked=loaded.List.Mods[0];
        linked.Variants[0].RequiredMods.Add("base-mod");
        linked.NsfwOverride=false;
        library.Save(loaded);
        var refreshed=library.Load().List.Mods[0];
        check(refreshed.Variants[0].RequiredMods.SequenceEqual(new[]{"base-mod"})&&!refreshed.Nsfw&&refreshed.NsfwOverride==false,
            "Vínculo de dependência e escolha manual de NSFW sobrevivem ao salvamento.");
        check(imported.Mods[0].Variants[0].Sources[0] == @"D:\mods\pacote.zip", "Referência de pacote permanece explícita sem incluir seu conteúdo.");

        var bodies = Example();
        var body = bodies.Mods.Single();
        body.Id = GeneralModLibrary.EbbBody;
        bodies.Presets[0].ModIds[0] = body.Id;
        var regular = body.Variants.Single();
        regular.Description = "Somente EBB";
        regular.Tags = new List<string> { " EBB " };
        regular.NaturalOption = new GeneralModVariant {
            Id = regular.Id, Name = "EBBN", DisplayName = "Hyst Natural", Category = "coleção",
            Description = "", Tags = new List<string>(), NsfwOverride = false,
            Sources = new List<string> { "https://example.com/natural.zip" }, RequiredMods = new List<string> { "natural-refit" }
        };
        body.Variants.Add(new GeneralModVariant { Id = "old", Name = "Outra versão", Sources = new List<string> { "https://example.com/old.zip" } });
        library.Save(new GeneralModState { List = bodies });
        var roundtrip = library.Load().List;
        library.Export(export, roundtrip);
        var savedBody = library.Import(export).Mods.Single();
        var savedRegular = savedBody.Variants.Single(v => v.Id == regular.Id);
        var savedNatural = GeneralModLibrary.BodyOption(GeneralModLibrary.NaturalBody, savedBody, savedRegular);
        check(savedRegular.Description == "Somente EBB" && savedRegular.Tags.SequenceEqual(new[] { "EBB" }) &&
            savedNatural.DisplayName == "Hyst Natural" && savedNatural.Description == "" && savedNatural.Tags.Count == 0 &&
            savedNatural.Nsfw == false && savedNatural.Category == "Coleção" && savedNatural.Sources.Single().EndsWith("/natural.zip") &&
            savedNatural.RequiredMods.SequenceEqual(new[] { "natural-refit" }) && savedBody.Variants.Single(v => v.Id == "old").Tags == null,
            "Versões têm metadados próprios, preservando a diferença entre campos vazios e campos herdados na exportação.");
        GeneralModLibrary.DeleteCategory(roundtrip, "Coleção");
        check(roundtrip.Mods.Single().Variants.Single(v => v.Id == regular.Id).NaturalOption.Category == "",
            "Excluir uma categoria atualiza as opções Natural que a usavam.");
        regular.NaturalOption.NaturalOption = new GeneralModVariant();
        Throws(() => library.Save(new GeneralModState { List = bodies })); passed++;

        File.WriteAllText(export, "{\"Schema\":1,\"Categories\":[],\"Mods\":[{\"Id\":\"old\",\"Name\":\"Legado\",\"DefaultVariant\":\"default\",\"Variants\":[{\"Id\":\"default\",\"Name\":\"Principal\",\"Sources\":[\"https://example.com/mod.zip\"]}]}]}");
        var legacy = library.Import(export);
        File.WriteAllText(Path.Combine(root, "estado.json"), "{\"List\":" + File.ReadAllText(export) + "}");
        var oldState = library.Load();
        check(legacy.Presets.Count == 0 && legacy.CategoryDescriptions.Count == 0 && legacy.Mods[0].Tags.Count == 0 && !legacy.Mods[0].Nsfw && legacy.Mods[0].Description == "" && legacy.UncategorizedName == "Sem categoria", "JSON 1.1 migra sem campos novos obrigatórios.");
        check(oldState.Theme == "light" && oldState.Language == "pt" && oldState.SelectedPreset == null, "Preferências ausentes recebem os padrões.");
        legacy.CategoryDescriptions = null;
        legacy.Presets = null;
        legacy.Mods[0].Tags = null;
        library.Save(new GeneralModState { List = legacy, Theme = null, Language = null });
        check(library.Load().List.Mods[0].Tags.Count == 0, "Opcionais explicitamente nulos são migrados.");

        var editable = Example();
        editable.Presets[0].Id = "all";
        editable.Presets[0].Name = "Meu grupo";
        editable.Presets[0].ModIds.Clear();
        library.Save(new GeneralModState { List = editable, SelectedPreset = "removed" });
        check(library.Load().List.Presets[0].ModIds.Count == 0 && library.Load().SelectedPreset == null, "Preset all é editável e seleção excluída é limpa.");

        Action<Action<GeneralModList>> rejectList = mutate => {
            var invalid = Example();
            mutate(invalid);
            Throws(() => library.Save(new GeneralModState { List = invalid }));
            passed++;
        };
        rejectList(value => value.Mods[0].Description = new string('x', 4001));
        rejectList(value => value.Mods[0].Description = "controle\0inválido");
        rejectList(value => value.Mods[0].Tags = Enumerable.Range(0, 33).Select(number => "tag" + number).ToList());
        rejectList(value => value.Mods[0].Tags.Add("casual"));
        rejectList(value => value.Mods[0].Tags.Add(""));
        rejectList(value => value.Mods[0].Tags.Add(new string('x', 61)));
        rejectList(value => value.Mods[0].Variants[0].DisplayName = "");
        rejectList(value => value.Mods[0].Variants[0].Description = new string('x', 4001));
        rejectList(value => value.Mods[0].Variants[0].Tags = new List<string> { "Tag", "tag" });
        rejectList(value => value.Mods[0].Variants[0].Category = "Não cadastrada");
        rejectList(value => value.Mods[0].Variants[0].NaturalOption = new GeneralModVariant());
        rejectList(value => value.CategoryDescriptions.Add("Inexistente", "Descrição"));
        rejectList(value => value.CategoryDescriptions["Coleção"] = new string('x', 4001));
        rejectList(value => value.CategoryDescriptions.Add("coleção", "Duplicada"));
        rejectList(value => value.Presets.Add(value.Presets[0]));
        rejectList(value => value.Presets[0].ModIds.Add("missing"));
        rejectList(value => value.Presets[0].ModIds.Add("custom"));
        rejectList(value => value.Presets[0].Name = "");
        rejectList(value => value.Presets[0].Id = "../preset");
        rejectList(value => value.Presets[0].ModIds = null);
        rejectList(value => value.Presets = Enumerable.Range(0, 101).Select(number => new GeneralModPreset { Id = "p" + number, Name = "p" + number }).ToList());
        rejectList(value => value.UncategorizedName = new string('x', 121));
        rejectList(value => value.UncategorizedDescription = "inválida\0");
        Throws(() => library.Save(new GeneralModState { List = Example(), Theme = "automatic" })); passed++;
        Throws(() => library.Save(new GeneralModState { List = Example(), Language = "other" })); passed++;
        Throws(() => library.Save(new GeneralModState { List = Example(), SelectedPreset = "../bad" })); passed++;
        string savedHash = Safe.Hash(Path.Combine(root, "estado.json"));
        File.WriteAllText(export, "{\"Schema\":1,\"Categories\":[],\"Mods\":[],\"Presets\":[{\"Id\":\"bad\",\"Name\":\"Bad\",\"ModIds\":[\"unknown\"]}]}");
        Throws(() => library.Import(export));
        check(savedHash == Safe.Hash(Path.Combine(root, "estado.json")), "Importação inválida preserva o estado anterior.");

        var catalog = FixedData.Load();
        var seed = GeneralModLibrary.Seed(catalog, new ModImportItem[0]);
        var noPl = seed.Presets.Single(preset => preset.Id == "no-pl");
        check(seed.Presets.Single(preset => preset.Id == "all").ModIds.SequenceEqual(seed.Mods.Select(mod => mod.Id)), "Todos contém cada entrada visível do catálogo.");
        var expected = seed.Mods.Where(mod => GeneralModLibrary.IsHystBody(mod.Id) || GameEdition.Reason(catalog, new[] { mod.Id }) == null).Select(mod => mod.Id).ToArray();
        check(noPl.ModIds.SequenceEqual(expected), "Sem Phantom Liberty considera os requisitos transitivos do catálogo real.");
        var dependent = new FixedCatalog { Packages = new List<FixedMod> {
            Fixed("base", false, new string[0]), Fixed("dlc", true, new string[0]),
            Fixed("dependent", false, new[] { "dlc" })
        } };
        var dependencySeed = GeneralModLibrary.Seed(dependent, new ModImportItem[0]);
        check(dependencySeed.Presets.Single(preset => preset.Id == "no-pl").ModIds.SequenceEqual(new[] { "base" }), "Dependência indireta de DLC exclui o mod do preset sem expansão.");
        Safe.WriteJson(Path.Combine(root, "resultado.json"), new { Passed = passed, RealGameTouched = false, NetworkUsed = false });
        return passed;
    }

    static GeneralModList Example() {
        return new GeneralModList {
            Categories = new List<string> { "Coleção" },
            CategoryDescriptions = new Dictionary<string, string> { { "Coleção", " Mods favoritos " } },
            UncategorizedName = "Avulsos", UncategorizedDescription = " Para organizar depois ",
            Presets = new List<GeneralModPreset> { new GeneralModPreset { Id = "friends", Name = "Para amigos", ModIds = new List<string> { "custom" } } },
            Mods = new List<GeneralModEntry> { new GeneralModEntry {
                Id = "custom", Name = "Pacote", Category = "Coleção", DefaultVariant = "default", Nsfw = true,
                Description = " Descrição\r\nDuas linhas ", Tags = new List<string> { " Veículos ", "Casual" },
                Variants = new List<GeneralModVariant> { new GeneralModVariant { Id = "default", Name = "Principal", Sources = new List<string> { @"D:\mods\pacote.zip" } } }
            } }
        };
    }

    static FixedMod Fixed(string id, bool requiresPl, string[] dependencies) {
        return new FixedMod {
            Id = id, Name = id, RequiresPL = requiresPl, Dependencies = dependencies,
            Assets = new List<FixedAsset> { new FixedAsset { ModId = 1, FileId = 1 } }
        };
    }

    static void Throws(Action action) {
        try { action(); } catch (IOException) { return; }
        throw new IOException("Um dado inválido foi aceito pela biblioteca.");
    }
}
