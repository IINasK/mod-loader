using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

public sealed class GeneralModInstaller {
    public const string Installed = "Instalado";
    public const string Missing = "Não instalado";
    public const string Partial = "Instalação parcial";
    public const string Modified = "Arquivos alterados";

    public sealed class Record {
        public string Key;
        public List<RecordFile> Files;
    }

    public sealed class RecordFile {
        public string Path;
        public string Sha256;
        public long Size;
        public bool Owned;
    }

    readonly string destination;
    readonly string records;

    public GeneralModInstaller(string destination, string stateDirectory) {
        this.destination = Path.GetFullPath(destination);
        string state = Path.GetFullPath(stateDirectory);
        if (this.destination.StartsWith(@"\\", StringComparison.Ordinal) || state.StartsWith(@"\\", StringComparison.Ordinal))
            throw new IOException("Escolha uma pasta local para instalar os mods.");
        string targetPrefix = this.destination.TrimEnd('\\') + "\\";
        string statePrefix = state.TrimEnd('\\') + "\\";
        if (targetPrefix.StartsWith(statePrefix, StringComparison.OrdinalIgnoreCase) || statePrefix.StartsWith(targetPrefix, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Escolha uma pasta de instalação separada dos dados do Mod Loader.");
        Safe.NoLinks(this.destination);
        if (!Directory.Exists(this.destination)) throw new IOException("Escolha uma pasta de instalação existente.");
        records = Safe.In(state, Fingerprint(targetPrefix.ToUpperInvariant()));
    }

    public string Status(string key) {
        Record record = ReadRecord(RecordPath(key));
        if (record == null) return Missing;
        int present = 0;
        foreach (RecordFile file in record.Files) {
            string target = Safe.In(destination, file.Path);
            if (Directory.Exists(target)) return Modified;
            if (!File.Exists(target)) continue;
            if (!Matches(target, file)) return Modified;
            present++;
        }
        return present == 0 ? Missing : present == record.Files.Count ? Installed : Partial;
    }

    public string Install(string source, string key, Action<string> report) {
        string receipt = RecordPath(key);
        source = Path.GetFullPath(source);
        Safe.NoLinks(source);
        if (!File.Exists(source)) throw new IOException("O arquivo selecionado não está disponível: " + source);

        using (AcquireLock()) {
            Record previous = ReadRecord(receipt);
            var previousFiles = previous == null
                ? new Dictionary<string, RecordFile>(StringComparer.OrdinalIgnoreCase)
                : previous.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
            var record = new Record { Key = key, Files = new List<RecordFile>() };
            var staged = new List<string>();
            var createdFiles = new List<RecordFile>();
            var createdDirectories = new List<string>();
            string staging = Safe.In(destination, ".mod-loader-" + Guid.NewGuid().ToString("N"));

            try {
                using (var reader = BundledArchiveReader.Open(source, new FileInfo(source).Length, Safe.Hash(source))) {
                    ReaderArchiveEntry[] entries = reader.Files();
                    ValidatePaths(entries);
                    if (previous != null && previousFiles.Count != entries.Length) throw ChangedPackage();
                    Directory.CreateDirectory(staging);
                    for (int i = 0; i < entries.Length; i++) {
                        ReaderArchiveEntry entry = entries[i];
                        string stageFile = Safe.In(staging, i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        staged.Add(stageFile);
                        if (report != null) report("Conferindo " + entry.Path);
                        reader.CopyEntry(entry.RawPath, input => {
                            using (var output = new FileStream(stageFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                                input.CopyTo(output);
                        });
                        var file = new RecordFile { Path = entry.Path, Size = entry.Size, Sha256 = Safe.Hash(stageFile) };
                        RecordFile old;
                        if (previous != null && (!previousFiles.TryGetValue(file.Path, out old) || old.Sha256 != file.Sha256))
                            throw ChangedPackage();
                        record.Files.Add(file);
                    }
                }

                var existingOwners = new HashSet<string>(OtherFiles(receipt).Where(file => file.Owned)
                    .Select(file => file.Path + "\n" + file.Sha256), StringComparer.OrdinalIgnoreCase);
                foreach (RecordFile file in record.Files) {
                    string target = Safe.In(destination, file.Path);
                    if (File.Exists(target) && !Matches(target, file)) throw Conflict(file.Path);
                    RecordFile old;
                    file.Owned = previousFiles.TryGetValue(file.Path, out old) && old.Owned
                        || existingOwners.Contains(file.Path + "\n" + file.Sha256);
                }

                for (int i = 0; i < record.Files.Count; i++) {
                    RecordFile file = record.Files[i];
                    string target = Safe.In(destination, file.Path);
                    if (File.Exists(target)) {
                        if (!Matches(target, file)) throw Conflict(file.Path);
                        continue;
                    }
                    CreateParents(file.Path, createdDirectories);
                    Safe.NoLinks(target);
                    File.Move(staged[i], target);
                    createdFiles.Add(file);
                    file.Owned = true;
                }
                Safe.WriteJson(receipt, record);
                return "Instalado. " + record.Files.Count + " arquivo(s) conferido(s).";
            } catch {
                foreach (RecordFile file in createdFiles.AsEnumerable().Reverse()) {
                    TryCleanup(() => {
                        string target = Safe.In(destination, file.Path);
                        if (Matches(target, file)) File.Delete(target);
                    });
                }
                foreach (string directory in createdDirectories.AsEnumerable().Reverse()) TryCleanup(() => DeleteEmpty(directory));
                throw;
            } finally {
                foreach (string file in staged) TryCleanup(() => { Safe.NoLinks(file); File.Delete(file); });
                TryCleanup(() => DeleteEmpty(staging));
            }
        }
    }

    public string Remove(string key) {
        string receipt = RecordPath(key);
        using (AcquireLock()) {
            Record record = ReadRecord(receipt);
            if (record == null) return "Nenhuma instalação registrada para remover.";
            var shared = new HashSet<string>(OtherFiles(receipt).Select(file => file.Path), StringComparer.OrdinalIgnoreCase);
            int removed = 0;
            int preserved = 0;
            foreach (RecordFile file in record.Files) {
                string target = Safe.In(destination, file.Path);
                if (!File.Exists(target)) continue;
                if (!file.Owned || shared.Contains(file.Path) || !Matches(target, file)) {
                    preserved++;
                    continue;
                }
                File.Delete(target);
                removed++;
            }
            File.Delete(receipt);
            return removed + " arquivo(s) removido(s). " + preserved + " arquivo(s) preexistente(s), compartilhado(s) ou alterado(s) preservado(s).";
        }
    }

    void ValidatePaths(ReaderArchiveEntry[] entries) {
        if (entries.Length == 0) throw new IOException("O pacote não contém arquivos para instalar.");
        var paths = new HashSet<string>(entries.Select(entry => entry.Path), StringComparer.OrdinalIgnoreCase);
        foreach (ReaderArchiveEntry entry in entries) {
            string target = Safe.In(destination, entry.Path);
            if (Directory.Exists(target)) throw Conflict(entry.Path);
            for (int slash = entry.Path.LastIndexOf('/'); slash >= 0; slash = entry.Path.LastIndexOf('/', slash - 1)) {
                string parent = entry.Path.Substring(0, slash);
                if (paths.Contains(parent) || File.Exists(Safe.In(destination, parent))) throw Conflict(parent);
            }
        }
    }

    void CreateParents(string relative, List<string> created) {
        int slash = relative.IndexOf('/');
        while (slash >= 0) {
            string directory = Safe.In(destination, relative.Substring(0, slash));
            if (!Directory.Exists(directory)) {
                Directory.CreateDirectory(directory);
                created.Add(directory);
            }
            slash = relative.IndexOf('/', slash + 1);
        }
    }

    FileStream AcquireLock() {
        string path = Safe.In(records, "operations.lock");
        Directory.CreateDirectory(records);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    string RecordPath(string key) {
        if (String.IsNullOrWhiteSpace(key)) throw new ArgumentException("O mod precisa de um identificador.", "key");
        return Safe.In(records, Fingerprint(key) + ".json");
    }

    Record ReadRecord(string path) {
        Safe.NoLinks(path);
        if (!File.Exists(path)) return null;
        var serializer = new JavaScriptSerializer { MaxJsonLength = Safe.Json.MaxJsonLength };
        Record record = serializer.Deserialize<Record>(File.ReadAllText(path));
        if (record == null || record.Files == null || record.Files.Count == 0 || RecordPath(record.Key) != path)
            throw new IOException("Registro de instalação inválido: " + path);
        foreach (RecordFile file in record.Files) {
            if (file == null || file.Size < 0 || !FixedData.HashValid(file.Sha256)) throw new IOException("Registro de arquivo inválido: " + path);
            Safe.In(destination, file.Path);
        }
        return record;
    }

    IEnumerable<RecordFile> OtherFiles(string receipt) {
        foreach (string path in Directory.EnumerateFiles(records, "*.json")) {
            if (String.Equals(path, receipt, StringComparison.OrdinalIgnoreCase)) continue;
            Record record = ReadRecord(path);
            if (record == null) continue;
            foreach (RecordFile file in record.Files) yield return file;
        }
    }

    static bool Matches(string path, RecordFile file) {
        return File.Exists(path) && new FileInfo(path).Length == file.Size && Safe.Hash(path) == file.Sha256;
    }

    static string Fingerprint(string value) {
        using (var hash = SHA256.Create()) return Safe.Hex(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Substring(0, 32);
    }

    static void DeleteEmpty(string directory) {
        Safe.NoLinks(directory);
        if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
    }

    static void TryCleanup(Action cleanup) {
        try { cleanup(); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    static IOException Conflict(string path) { return new IOException("O destino já tem um arquivo diferente ou uma pasta conflitante: " + path); }

    static IOException ChangedPackage() { return new IOException("Este mod já tem outra versão registrada nesta pasta. Remova a instalação anterior antes de trocar o pacote."); }
}
