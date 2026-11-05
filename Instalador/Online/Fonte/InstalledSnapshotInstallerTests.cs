using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

public static class InstalledSnapshotInstallerTests {
    static List<string> tests;
    public static int Run(string parent) {
        tests = new List<string>();
        string report = Path.Combine(parent, "resultado-snapshot-installer.json");
        try {
            RunChecks(parent);
            Safe.WriteJson(report, new { Passed = true, Tests = tests });
            return tests.Count;
        } catch (Exception exception) {
            Safe.WriteJson(report, new { Passed = false, Tests = tests, Error = exception.ToString() });
            throw;
        }
    }
    static void RunChecks(string parent) {
        string root = Path.Combine(parent, "online-testes-" + Guid.NewGuid().ToString("N"));
        string target = Path.Combine(root, "game"), data = Path.Combine(root, "data");
        Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, ".fixture-only"), "fixture");
        var catalog = new FixedCatalog { Packages = new List<FixedMod>() };
        var engine = new InstalledSnapshotInstaller(target, data, catalog, true);
        string original = Path.Combine(target, "original.txt"); File.WriteAllText(original, "original");
        string first = Pack(root, "first", catalog, new Dictionary<string,string> {
            { "mods/first.txt", "first" }, { "mods/shared.txt", "shared" }, { "original.txt", "original" }
        });
        string id = engine.Install(first, Safe.Hash(first), null);
        File.Delete(first);
        engine = new InstalledSnapshotInstaller(target, data, catalog, true);
        Check(engine.Status(id, "first/default") == GeneralModInstaller.Installed && engine.ReadManifest(id).List.Mods[0].Id == "first", "Recibo e manifesto sobrevivem à exclusão do ZIP.");
        Check(engine.ReadInstalledFiles(id, "first/default").Count == 3, "Arquivos instalados podem ser reexportados sem o ZIP.");
        string second = Pack(root, "second", catalog, new Dictionary<string,string> { { "mods/second.txt", "second" }, { "mods/shared.txt", "shared" } });
        string id2 = engine.Install(second, Safe.Hash(second), null);
        engine.Remove(id, new[] { "first/default" }, null);
        Check(!File.Exists(Safe.In(target, "mods/first.txt")) && File.Exists(Safe.In(target, "mods/shared.txt")) && File.ReadAllText(original) == "original", "Remoção preserva arquivo compartilhado e preexistente.");
        engine.Remove(id2, new[] { "second/default" }, null);
        Check(!File.Exists(Safe.In(target, "mods/shared.txt")) && File.ReadAllText(original) == "original", "O último proprietário remove apenas os arquivos criados pelo instalador.");

        string conflict = Pack(root, "conflict", catalog, new Dictionary<string,string> { { "new/early.txt", "new" }, { "original.txt", "different" } });
        Throws(() => engine.Install(conflict, Safe.Hash(conflict), null), "Arquivo preexistente divergente bloqueia a instalação.");
        Check(!File.Exists(Safe.In(target, "new/early.txt")) && File.ReadAllText(original) == "original", "Conflito impede alterações de todo o lote.");
        string rollback = Pack(root, "rollback", catalog, new Dictionary<string,string> { { "rollback/one.txt", "one" }, { "rollback/two.txt", "two" } });
        Throws(() => engine.Install(rollback, Safe.Hash(rollback), null, 1), "Falha de teste após a primeira cópia é propagada.");
        Check(!Directory.Exists(Safe.In(target, "rollback")), "Falha após a cópia desfaz o lote e limpa diretórios criados.");
        Check(!Directory.GetDirectories(target, ".mod-loader-snapshot-*").Any(), "Preparação temporária no volume de destino é limpa após falha.");
        Check(!Directory.GetDirectories(data, "stage-*", SearchOption.AllDirectories).Any(), "Os dados do programa não recebem arquivos descompactados em estágio.");
        string edited = Pack(root, "edited", catalog, new Dictionary<string,string> { { "edited.txt", "original-mod" } });
        string editedId = engine.Install(edited, Safe.Hash(edited), null);
        File.WriteAllText(Safe.In(target, "edited.txt"), "user-edit");
        Check(engine.Status(editedId, "edited/default") == GeneralModInstaller.Modified, "Detecta arquivo alterado.");
        engine.Remove(editedId, new[] { "edited/default" }, null);
        Check(File.ReadAllText(Safe.In(target, "edited.txt")) == "user-edit", "Remoção preserva alterações posteriores.");
        string concurrent2 = Pack(root, "concurrent2", catalog, new Dictionary<string,string> { { "concurrent2/one.txt", "one" }, { "concurrent2/two.txt", "two" } });
        Throws(() => engine.Install(concurrent2, Safe.Hash(concurrent2), message => {
            if (message == "Instalado: concurrent2/one.txt") File.WriteAllText(Safe.In(target, "concurrent2/one.txt"), "user-change");
        }, 2), "Falha de teste após duas cópias aciona reversão com edição concorrente.");
        Check(File.ReadAllText(Safe.In(target, "concurrent2/one.txt")) == "user-change" && !File.Exists(Safe.In(target, "concurrent2/two.txt")), "Reversão preserva edição concorrente e retira os outros arquivos.");
        string otherTarget = Path.Combine(root, "other"); Directory.CreateDirectory(otherTarget);
        Check(new InstalledSnapshotInstaller(otherTarget, data, catalog).Status(id, "first/default") == GeneralModInstaller.Missing, "Recibo pertence apenas à pasta original.");
        Throws(() => new InstalledSnapshotInstaller(otherTarget, data, catalog, true), "Exceção de teste exige o marcador de fixture isolada.");
        string bad = Pack(root, "bad", catalog, new Dictionary<string,string> { { "bad.txt", "bad" } });
        Throws(() => engine.Install(bad, new string('0', 64), null), "Hash incorreto do pacote é recusado antes de instalar.");

        ConfigTransactions(root);
    }

    static void ConfigTransactions(string parent) {
        string target = Path.Combine(parent, "config-game"), data = Path.Combine(parent, "config-data");
        Directory.CreateDirectory(target); File.WriteAllText(Safe.In(target, ".fixture-only"), "fixture");
        byte[] code = Encoding.UTF8.GetBytes("fixed fixture code");
        var recipe = new FixedMod {
            Id = "FixtureMod", Name = "Fixture mod", Profile = "Other", Dependencies = new string[0],
            Assets = new List<FixedAsset> { new FixedAsset { Id = "fixture", Url = "https://www.nexusmods.com/cyberpunk2077/mods/1" } },
            Files = new List<FixedFile> { new FixedFile { Path = "r6/scripts/fixture.reds", Sha256 = InstalledSnapshot.Hash(code), Size = code.Length } }
        };
        string configPath = "red4ext/plugins/mod_settings/user.ini";
        var catalog = new FixedCatalog {
            GameFileVersion = "2.5.0.0", Packages = new List<FixedMod> { recipe },
            Defaults = new List<FixedDefault> {
                new FixedDefault { ModId = recipe.Id, Path = configPath, Format = "ini-section", Section = "Selected" },
                new FixedDefault { ModId = recipe.Id, Path = configPath, Format = "ini-section", Section = "SelectedTwo" }
            }
        };
        var list = GeneralModLibrary.Seed(catalog, new ModImportItem[0]);
        var manifest = new InstalledSnapshotManifest { Name = "Config fixture", Game = "cyberpunk2077", GameFileVersion = catalog.GameFileVersion, List = list };
        string owner = "FixtureMod/default";
        manifest.Files.Add(new InstalledSnapshotFile { Path = recipe.Files[0].Path, Kind = "file", Sha256 = recipe.Files[0].Sha256, Size = code.Length, Owners = new List<string> { owner } });
        manifest.Files.Add(Config(configPath, "Selected", "{\"Value\":\"new\"}", owner));
        manifest.Files.Add(Config(configPath, "SelectedTwo", "{\"Other\":\"7\"}", owner));
        string ini = Safe.In(target, configPath); Directory.CreateDirectory(Path.GetDirectoryName(ini));
        string original = "; keep comment\r\n[Selected]\r\nValue = old\r\n[Unrelated]\r\nToken = local\r\n";
        File.WriteAllText(ini, original, new UTF8Encoding(false));
        string zip = Path.Combine(parent, "configs.zip"); WritePack(zip, manifest, new Dictionary<string,byte[]> { { manifest.Files[0].Sha256, code } });
        var engine = new InstalledSnapshotInstaller(target, data, catalog, true);
        Throws(() => engine.Install(zip, Safe.Hash(zip), null, 2), "Falha depois da configuração escrita é propagada.");
        Check(File.ReadAllText(ini) == original && !File.Exists(Safe.In(target, recipe.Files[0].Path)), "Falha restaura a configuração original e remove o código novo.");
        string id = engine.Install(zip, Safe.Hash(zip), null);
        string text = File.ReadAllText(ini);
        Check(text.Contains("Value = new") && text.Contains("Other = 7") && text.Contains("Token = local") && text.Contains("; keep comment"), "Seções selecionadas são agrupadas sem perder tokens ou comentários não relacionados.");
        engine.Remove(id, new[] { owner }, null);
        Check(File.ReadAllText(ini) == original && !File.Exists(Safe.In(target, recipe.Files[0].Path)), "Remoção restaura o backup da configuração preexistente.");
        id = engine.Install(zip, Safe.Hash(zip), null);
        File.WriteAllText(ini, File.ReadAllText(ini).Replace("Value = new", "Value = changed"));
        Check(engine.Status(id, owner) == GeneralModInstaller.Installed, "Alterar configuração editável mantém o código como instalado.");
        Check(engine.ReadInstalledFiles(id, owner).Single(f => f.Section == "Selected").Content.Contains("changed"), "Reexportação lê a configuração atual sanitizada.");
        File.AppendAllText(ini, "\n; edit after install\n");
        engine.Remove(id, new[] { owner }, null);
        Check(File.ReadAllText(ini).Contains("; edit after install"), "Configuração editada depois é preservada.");
    }

    static InstalledSnapshotFile Config(string path, string section, string content, string owner) {
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        return new InstalledSnapshotFile { Path = path, Section = section, Content = content, Kind = "ini-section", Size = bytes.Length, Sha256 = InstalledSnapshot.Hash(bytes), Owners = new List<string> { owner } };
    }
    static string Pack(string parent, string mod, FixedCatalog catalog, Dictionary<string,string> files) {
        var manifest = new InstalledSnapshotManifest { Name = mod, Game = "generic" };
        manifest.List.Mods.Add(new GeneralModEntry { Id = mod, Name = mod, DefaultVariant = "default", Variants = new List<GeneralModVariant> { new GeneralModVariant { Id = "default", Name = "Default", Sources = new List<string> { "installed://" + mod + "/default" } } } });
        var entries = new Dictionary<string,byte[]>();
        foreach (var file in files) {
            byte[] bytes = Encoding.UTF8.GetBytes(file.Value); string hash = InstalledSnapshot.Hash(bytes);
            manifest.Files.Add(new InstalledSnapshotFile { Path = file.Key, Kind = "file", Sha256 = hash, Size = bytes.Length, Owners = new List<string> { mod + "/default" } });
            entries[hash] = bytes;
        }
        InstalledSnapshot.Validate(manifest, catalog);
        string path = Path.Combine(parent, mod + ".zip"); WritePack(path, manifest, entries); return path;
    }
    static void WritePack(string path, InstalledSnapshotManifest manifest, Dictionary<string,byte[]> entries) {
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create)) {
            using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open(), new UTF8Encoding(false))) writer.Write(Safe.Json.Serialize(manifest));
            foreach (var entry in entries) using (var output = zip.CreateEntry("files/" + entry.Key).Open()) output.Write(entry.Value, 0, entry.Value.Length);
        }
    }
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); tests.Add(message); }
    static void Throws(Action action, string message) { try { action(); } catch (IOException) { tests.Add(message); return; } throw new Exception(message + " A operação insegura foi aceita."); }
}
