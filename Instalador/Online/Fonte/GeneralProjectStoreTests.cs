using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

internal static class GeneralProjectStoreTests {
    internal static int Run(string parent) {
        string root = Path.Combine(Path.GetFullPath(parent), "projects-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var checks = new List<string>(); var skipped = new List<string>();
        Action<bool, string> check = (ok, message) => { if (!ok) throw new IOException(message); checks.Add(message); };
        var store = new GeneralProjectStore(Path.Combine(root, "library"));
        var initial = store.Read();
        check(initial.Projects.Count == 0 && initial.Theme == "light-red" && initial.Language == "pt", "Biblioteca nova começa sem projetos nem pacote Cyberpunk automático.");
        var empty = store.Create("Meu projeto", null, "light-red", "pt");
        var emptyState = store.Load(empty);
        check(emptyState.List.Mods.Count == 0 && emptyState.List.Categories.Count == 0 && emptyState.List.Presets.Count == 0, "Projeto novo aceita lista vazia sem sugestões ou seed.");
        check(emptyState.Destination == null && !String.IsNullOrEmpty(emptyState.DownloadFolder) && emptyState.Theme == "light-red", "Novo projeto guarda preferências e downloads sem escolher ou alterar um jogo.");
        check(store.Read().Projects.Single().Name == "Meu projeto" && store.DataPath(empty).EndsWith(Path.Combine("Projetos", empty.Id)), "Metadados e pasta identificada por GUID persistem.");

        string source = Path.Combine(root, "original.zip"); File.WriteAllText(source, "Original reference; never installed.");
        string sourceHash = Safe.Hash(source), game = Path.Combine(root, "no-game-created");
        var list = Example(source);
        var second = store.Create("Outro projeto", list, "dark", "en", game);
        check(store.Read().Projects.Count == 2 && store.DataPath(second) != store.DataPath(empty), "Projetos têm estados e pastas isoladas.");
        var secondState = store.Load(second);
        check(secondState.Destination == game && secondState.SelectedPreset == "kit" && secondState.List.Mods.Single().Name == "Example", "Projeto preenchido mantém lista, primeiro preset e destino explícitos.");
        check(!Directory.Exists(game) && Safe.Hash(source) == sourceHash, "Criar projeto não instala mods nem toca no destino ou no pacote original.");
        string indexPath = Path.Combine(store.Root, "projects.json"), indexText = File.ReadAllText(indexPath);
        check(!new[] { "Variants", "Sources", "ResolvedDownloads", "SnapshotReceipts", "Destination" }.Any(indexText.Contains), "Índice guarda identidade e preferências da biblioteca, sem conteúdo dos mods ou destinos privados dos projetos.");
        check(Directory.GetFiles(store.DataPath(second), "*", SearchOption.AllDirectories).Length == 1, "Projeto não duplica os arquivos de download.");
        string emptyHash = Safe.Hash(Path.Combine(store.DataPath(empty), "lista.json"));
        secondState.List.Categories.Add("Pessoal"); secondState.Theme = "light";
        secondState.SnapshotReceipts["example/default"] = Guid.NewGuid().ToString("N");
        new GeneralModLibrary(Path.Combine(store.DataPath(second), "lista.json")).Save(secondState);
        check(store.Load(second).List.Categories.Contains("Pessoal") && Safe.Hash(Path.Combine(store.DataPath(empty), "lista.json")) == emptyHash, "Editar e reabrir um projeto preserva o outro byte a byte.");
        check(store.Load(second).SnapshotReceipts["example/default"] == secondState.SnapshotReceipts["example/default"], "Reabrir mantém referências locais de instalação.");

        var library = new GeneralModLibrary(Path.Combine(store.DataPath(second), "lista.json"));
        string exported = Path.Combine(root, "export.json"); library.Export(exported, secondState.List);
        var imported = store.Create("Importado", library.Import(exported), "light", "pt");
        check(store.Load(imported).List.Mods.Single().Variants[0].Sources.Single() == source && store.Load(imported).List.Presets.Single().Name == "Kit", "Exportação/importação existente cria projeto independente e preserva organização e fontes.");
        check(!File.ReadAllText(exported).Contains("SnapshotReceipts") && !File.ReadAllText(exported).Contains("Destination"), "Arquivo compartilhado de projeto continua sem recibos e preferências privadas.");
        foreach (string theme in new[] { "light", "dark", "light-red" }) {
            store.SavePreferences(theme, "en"); var reopened = new GeneralProjectStore(store.Root).Read();
            check(reopened.Theme == theme && reopened.Language == "en" && reopened.Projects.Count == 3, "Preferências da biblioteca persistem sem mudar projetos: " + theme);
        }
        string downloads = Path.Combine(root, "downloads"); Directory.CreateDirectory(downloads);
        store.SavePreferences("light-red", "pt", downloads);
        var configured = store.Create("Usa downloads escolhidos", null, "light-red", "pt");
        check(store.Read().DownloadFolder == downloads && store.Load(configured).DownloadFolder == downloads, "Pasta escolhida nas opções da Home persiste e é usada nos novos projetos.");
        string preferencesHash = Safe.Hash(indexPath);
        Throws(() => store.SavePreferences("light", "pt", "relative-downloads"));
        Throws(() => store.SavePreferences("light", "pt", Path.Combine(root, "missing-downloads")));
        check(Safe.Hash(indexPath) == preferencesHash, "Escolha de downloads relativa ou ausente não sobrescreve as preferências.");
        var title = store.Create("../apenas um título", null, "light", "pt");
        check(Path.GetDirectoryName(store.DataPath(title)) == Path.Combine(store.Root, "Projetos") && !Directory.Exists(Path.Combine(root, "apenas um título")), "Nome do projeto é título, nunca caminho de pasta.");
        foreach (string name in new[] { "", "  ", "nome\0inválido", new string('x', 121) }) {
            int before = store.Read().Projects.Count; Throws(() => store.Create(name, null, "light", "pt"));
            check(store.Read().Projects.Count == before, "Nome inválido não registra projeto: tamanho " + name.Length);
        }
        foreach (var project in new[] { new GeneralProject { Id = "../escape", Name = "Bad" }, new GeneralProject { Id = "legacy", Name = "Bad" }, new GeneralProject { Id = empty.Id, Name = "Bad", Legacy = true } }) {
            Throws(() => store.DataPath(project)); checks.Add("Identidade ou indicação legacy inválida não fornece caminho.");
        }
        Throws(() => store.Load(new GeneralProject { Id = Guid.NewGuid().ToString("N"), Name = "Unknown" }));
        checks.Add("Não abre estado não registrado na biblioteca.");
        Throws(() => store.SavePreferences("other", "pt")); Throws(() => store.SavePreferences("light", "other"));
        checks.Add("Tema ou idioma não aceito não sobrescreve o índice.");

        string protectedPath = Path.Combine(store.DataPath(second), "Instalacoes", "fixture-receipt.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(protectedPath)); File.WriteAllText(protectedPath, "Receipt and backups remain local.");
        string savedStateHash = Safe.Hash(Path.Combine(store.DataPath(second), "lista.json")), receiptHash = Safe.Hash(protectedPath);
        store.Delete(second);
        check(!store.Read().Projects.Any(item => item.Id == second.Id) && Safe.Hash(protectedPath) == receiptHash && Safe.Hash(Path.Combine(store.DataPath(second), "lista.json")) == savedStateHash, "Excluir projeto remove somente sua entrada e preserva lista, recibos e backups locais.");
        check(store.Read().Projects.Any(item => item.Id == empty.Id) && Safe.Hash(source) == sourceHash && !Directory.Exists(game), "Exclusão não afeta outros projetos, arquivos originais ou jogo.");

        string legacyRoot = Path.Combine(root, "legacy-data"); Directory.CreateDirectory(legacyRoot);
        string legacyStatePath = Path.Combine(legacyRoot, "lista.json");
        var previous = new GeneralModState { List = Example(source), SelectedPreset = "kit", Theme = "dark", Language = "en", Destination = game, DownloadFolder = downloads };
        previous.List.Presets[0].Name = "Meu preset anterior";
        new GeneralModLibrary(legacyStatePath).Save(previous);
        byte[] entropy = Encoding.UTF8.GetBytes("project-migration-test");
        string legacyReceipt = Path.Combine(legacyRoot, "Instalacoes", "recibo.dat"); Directory.CreateDirectory(Path.GetDirectoryName(legacyReceipt));
        File.WriteAllBytes(legacyReceipt, ProtectedData.Protect(Encoding.UTF8.GetBytes("preserved receipt"), entropy, DataProtectionScope.CurrentUser));
        string legacyHash = Safe.Hash(legacyStatePath), dpapiHash = Safe.Hash(legacyReceipt);
        var legacyStore = new GeneralProjectStore(legacyRoot); var migrated = legacyStore.Read(); var legacy = migrated.Projects.Single();
        check(legacy.Id == "legacy" && legacy.Legacy && legacy.Name == "Meu preset anterior" && migrated.Theme == "dark" && migrated.Language == "en" && migrated.DownloadFolder == downloads, "Migração registra estado anterior com nome e preferências existentes, incluindo downloads.");
        check(legacyStore.DataPath(legacy) == legacyRoot && !Directory.Exists(Path.Combine(legacyRoot, "Projetos")) && Safe.Hash(legacyStatePath) == legacyHash && Safe.Hash(legacyReceipt) == dpapiHash, "Migração não move/copia/regrava estado ou pasta de recibos.");
        check(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(legacyReceipt), entropy, DataProtectionScope.CurrentUser)) == "preserved receipt", "Recibo DPAPI continua legível no mesmo perfil e caminho.");
        check(legacyStore.Load(legacy).List.Mods.Single().Id == "example" && legacyStore.Read().Projects.Count == 1, "Projeto anterior abre sem duplicar a migração.");
        legacyStore.Delete(legacy);
        check(new GeneralProjectStore(legacyRoot).Read().Projects.Count == 0 && File.Exists(legacyStatePath) && Safe.Hash(legacyReceipt) == dpapiHash, "Excluir legacy não apaga dados nem o faz ressurgir no próximo início.");
        legacyStore.Create("Novo depois da migração", null, "light-red", "pt");
        check(legacyStore.Read().Projects.Count == 1 && !legacyStore.Read().Projects.Any(item => item.Legacy), "Criar depois da exclusão não readiciona o projeto anterior.");

        string invalidRoot = Path.Combine(root, "bad-index"); Directory.CreateDirectory(invalidRoot);
        string invalidPath = Path.Combine(invalidRoot, "projects.json"); var invalidStore = new GeneralProjectStore(invalidRoot);
        foreach (string bad in new[] { "{broken", "null", "{}", "{\"Schema\":1,\"Projects\":null}", "{\"Schema\":2,\"Projects\":[]}", "{\"Schema\":1,\"Projects\":[{\"Id\":\"../outside\",\"Name\":\"Unsafe\"}]}" }) {
            File.WriteAllText(invalidPath, bad); string before = Safe.Hash(invalidPath);
            Throws(() => invalidStore.Read()); Throws(() => invalidStore.SavePreferences("dark", "en"));
            check(Safe.Hash(invalidPath) == before, "Índice corrompido é recusado e preservado, sem reconstrução silenciosa.");
        }
        foreach (var invalid in new[] {
            new GeneralProjectIndex { Projects = new List<GeneralProject> { empty, empty } },
            new GeneralProjectIndex { Projects = new List<GeneralProject> { new GeneralProject { Id = Guid.NewGuid().ToString("N"), Name = "" } } },
            new GeneralProjectIndex { Theme = "other" },
            new GeneralProjectIndex { DownloadFolder = "../outside" },
            new GeneralProjectIndex { Projects = Enumerable.Range(0, 201).Select(i => new GeneralProject { Id = Guid.NewGuid().ToString("N"), Name = "P" + i }).ToList() }
        }) {
            Safe.WriteJson(invalidPath, invalid); string before = Safe.Hash(invalidPath); Throws(() => invalidStore.Read());
            check(Safe.Hash(invalidPath) == before, "Índice com metadados inválidos permanece intacto.");
        }
        File.WriteAllBytes(invalidPath, new byte[1048577]); Throws(() => invalidStore.Read());
        check(new FileInfo(invalidPath).Length == 1048577, "Índice acima de 1 MB não é lido nem sobrescrito.");
        string missingOldFolder = Path.Combine(root, "old-download-folder-no-longer-present");
        Safe.WriteJson(invalidPath, new GeneralProjectIndex { DownloadFolder = missingOldFolder });
        check(invalidStore.Read().DownloadFolder == missingOldFolder, "Pasta antiga removida não impede abrir a Home e escolher outra.");

        TestJunction(root, store, check, skipped);
        Safe.WriteJson(Path.Combine(root, "resultado-project-store.json"), new { Passed = true, Tests = checks, Skipped = skipped, RealGameTouched = false, NetworkUsed = false });
        return checks.Count;
    }

    static GeneralModList Example(string source) {
        return new GeneralModList {
            Mods = new List<GeneralModEntry> { new GeneralModEntry { Id = "example", Name = "Example", DefaultVariant = "default", Variants = new List<GeneralModVariant> { new GeneralModVariant { Id = "default", Name = "Main", Sources = new List<string> { source } } } } },
            Presets = new List<GeneralModPreset> { new GeneralModPreset { Id = "kit", Name = "Kit", ModIds = new List<string> { "example" } } }
        };
    }

    static void TestJunction(string root, GeneralProjectStore store, Action<bool, string> check, List<string> skipped) {
        string outside = Path.Combine(root, "junction-target"), id = Guid.NewGuid().ToString("N"), link = Path.Combine(store.Root, "Projetos", id);
        Directory.CreateDirectory(outside); Directory.CreateDirectory(Path.GetDirectoryName(link));
        string untouched = Path.Combine(outside, "preserved.txt"); File.WriteAllText(untouched, "Never follow a project junction."); string hash = Safe.Hash(untouched);
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), "/d /c mklink /J \"" + link + "\" \"" + outside + "\"") {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true
        };
        using (var process = Process.Start(start)) {
            process.StandardOutput.ReadToEnd(); process.StandardError.ReadToEnd(); process.WaitForExit();
            if (process.ExitCode != 0) { skipped.Add("O Windows não permitiu criar a junction de fixture; NoLinks permanece obrigatório no backend."); return; }
        }
        try {
            Throws(() => store.DataPath(new GeneralProject { Id = id, Name = "Unsafe junction" }));
            check(Safe.Hash(untouched) == hash && Directory.GetFiles(outside).Length == 1, "Project path recusa junction e preserva o destino externo.");
        } finally {
            if ((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0) Directory.Delete(link, false);
        }
    }

    static void Throws(Action action) {
        try { action(); } catch (IOException) { return; }
        throw new IOException("Operação inválida de projeto foi aceita.");
    }
}
