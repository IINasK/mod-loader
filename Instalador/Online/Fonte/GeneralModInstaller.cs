using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

public sealed partial class GeneralModInstaller {
    public const string Installed = "Instalado";
    public const string Missing = "Não instalado";
    public const string Partial = "Instalação parcial";
    public const string Modified = "Arquivos alterados";

    public sealed class Record {
        public string Key,SourceSha256;
        public List<RecordFile> Files;
        public List<GeneralInstallRoute> InstallRoutes = new List<GeneralInstallRoute>();
    }

    public sealed class RecordFile {
        public string Path;
        public string Sha256;
        public long Size;
        public bool Owned;
        public string Before;
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
        return Status(key,null,Safe.Hash);
    }

    internal string Status(string key,string source,Func<string,string> readHash,List<GeneralInstallRoute> installRoutes=null) {
        Record record = ReadRecord(RecordPath(key));
        if (record == null) return Missing;
        if(installRoutes!=null&&!GeneralInstallRoutes.Same(installRoutes,record.InstallRoutes))return "Versão diferente";
        if(record.SourceSha256!=null&&source!=null&&File.Exists(source)&&readHash(source)!=record.SourceSha256)return "Versão diferente";
        int present = 0;
        foreach (RecordFile file in record.Files) {
            string target = Target(file.Path);
            if (Directory.Exists(target)) return Modified;
            if (!File.Exists(target)) continue;
            if (new FileInfo(target).Length!=file.Size||readHash(target)!=file.Sha256) return Modified;
            present++;
        }
        return present == 0 ? Missing : present == record.Files.Count ? Installed : Partial;
    }

    internal bool SourceMatches(string source,string key){var record=ReadRecord(RecordPath(key));return record!=null&&record.SourceSha256!=null&&File.Exists(source)&&Safe.Hash(source)==record.SourceSha256;}
    public string Install(string source, string key, Action<string> report, Func<ModVersionDifference,bool> chooseVersion = null, List<GeneralInstallRoute> installRoutes = null) {
        string receipt = RecordPath(key);
        source = Path.GetFullPath(source);
        Safe.NoLinks(source);
        if (!File.Exists(source)) throw new IOException("O arquivo selecionado não está disponível: " + source);

        installRoutes=installRoutes??new List<GeneralInstallRoute>();
        GeneralInstallRoutes.Validate(installRoutes);
        GeneralInstallRoutes.CheckFolders(destination,installRoutes);
        using (AcquireLock()) {
            Record previous = ReadRecord(receipt);
            var previousFiles = previous == null
                ? new Dictionary<string, RecordFile>(StringComparer.OrdinalIgnoreCase)
                : previous.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
            var record = new Record { Key = key, SourceSha256=Safe.Hash(source), InstallRoutes=installRoutes, Files = new List<RecordFile>() };
            var staged = new List<string>();
            var createdFiles = new List<RecordFile>();
            var replacedFiles = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            var createdDirectories = new List<string>();
            string staging = Safe.In(destination, ".mod-loader-" + Guid.NewGuid().ToString("N"));

            try {
                using (var reader = BundledArchiveReader.Open(source, new FileInfo(source).Length, record.SourceSha256)) {
                    ReaderArchiveEntry[] entries = reader.Files();
                    string[] paths=entries.Select(entry=>GeneralInstallRoutes.Map(entry.Path,installRoutes)).ToArray();
                    ValidatePaths(paths);
                    if(previous!=null&&(previousFiles.Count!=paths.Length||paths.Any(path=>!previousFiles.ContainsKey(path))))
                        new ModVersionDifference{Name=Path.GetFileName(source),Current="Pacote registrado",Incoming=Path.GetFileName(source),CanReplace=false,Detail="A lista de arquivos mudou. Remova a versão anterior pelo gerenciador antes de instalar esta versão, evitando misturar arquivos antigos e novos."}.Require(chooseVersion);
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
                        var file = new RecordFile { Path = paths[i], Size = entry.Size, Sha256 = Safe.Hash(stageFile) };
                        record.Files.Add(file);
                    }
                }

                var existingOwners = new HashSet<string>(OtherFiles(receipt).Where(file => file.Owned)
                    .Select(file => file.Path + "\n" + file.Sha256), StringComparer.OrdinalIgnoreCase);
                var conflicts=record.Files.Where(file=>File.Exists(Target(file.Path))&&!Matches(Target(file.Path),file)).ToList();
                var expected=conflicts.ToDictionary(file=>file.Path,file=>Safe.Hash(Target(file.Path)),StringComparer.OrdinalIgnoreCase);
                if(conflicts.Count>0)new ModVersionDifference{Name=Path.GetFileName(source),Current=String.Join("; ",conflicts.Select(file=>ModVersionDifference.FileVersion(Target(file.Path))).Distinct()),Incoming=Path.GetFileName(source),Detail=String.Join("\r\n",conflicts.Take(6).Select(file=>file.Path))+"\r\n"+GeneralStyle.T("A troca guarda os arquivos anteriores para restaurar ao remover. Nenhuma troca ocorre sem sua escolha.","Replacement backs up the previous files for restoration on removal. Nothing is replaced without your choice.")}.Require(chooseVersion);
                foreach (RecordFile file in record.Files) {
                    string target = Target(file.Path);RecordFile old;
                    file.Owned = previousFiles.TryGetValue(file.Path, out old) && old.Owned
                        || existingOwners.Contains(file.Path + "\n" + file.Sha256);
                    if(expected.ContainsKey(file.Path)){
                        if(Safe.Hash(target)!=expected[file.Path])throw Conflict(file.Path);
                        SaveBackup(target,expected[file.Path]);file.Before=expected[file.Path];file.Owned=true;
                    }else if(previousFiles.TryGetValue(file.Path,out old))file.Before=old.Before;
                }

                for (int i = 0; i < record.Files.Count; i++) {
                    RecordFile file = record.Files[i];
                    string target = GeneralInstallRoutes.Resolve(destination,file.Path,true);
                    if(expected.ContainsKey(file.Path)&&!File.Exists(target))throw Conflict(file.Path);
                    if (File.Exists(target)) {
                        if (!Matches(target, file)) {
                            string before; if(!expected.TryGetValue(file.Path,out before)||Safe.Hash(target)!=before)throw Conflict(file.Path);
                            if(GeneralInstallRoutes.External(file.Path))InstalledSnapshotInstaller.ReplaceChecked(staged[i],target,file.Sha256,before);
                            else File.Replace(staged[i],target,null);
                            replacedFiles.Add(file.Path,before);
                        }
                        continue;
                    }
                    GeneralInstallRoutes.CreateParents(destination,file.Path,createdDirectories);
                    Safe.NoLinks(target);
                    if(GeneralInstallRoutes.External(file.Path))InstalledSnapshotInstaller.ReplaceChecked(staged[i],target,file.Sha256,null);
                    else File.Move(staged[i], target);
                    createdFiles.Add(file);
                    file.Owned = true;
                }
                Safe.WriteJson(receipt, record);
                return "Instalado. " + record.Files.Count + " arquivo(s) conferido(s).";
            } catch {
                foreach(var pair in replacedFiles.Reverse())TryCleanup(()=>{var file=record.Files.Single(f=>f.Path==pair.Key);string target=Target(pair.Key);if(Matches(target,file))RestoreBackup(pair.Value,target,file.Sha256);});
                foreach (RecordFile file in createdFiles.AsEnumerable().Reverse()) {
                    TryCleanup(() => {
                        string target = Target(file.Path);
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

    public string Remove(string key, bool forgetOnly=false, Func<string,bool> activeOwner=null, Func<string,bool> preserveExternal=null) {
        string receipt = RecordPath(key);
        using (AcquireLock()) {
            Record record = ReadRecord(receipt);
            if (record == null) return "Nenhuma instalação registrada para remover.";
            var shared = new HashSet<string>(OtherFiles(receipt, activeOwner).Select(file => file.Path), StringComparer.OrdinalIgnoreCase);
            int removed = 0;
            int preserved = 0;
            foreach (RecordFile file in forgetOnly ? Enumerable.Empty<RecordFile>() : record.Files) {
                string target = Target(file.Path);
                if (!File.Exists(target)) continue;
                if (!file.Owned || shared.Contains(file.Path) || !Matches(target, file) || GeneralInstallRoutes.External(file.Path)&&preserveExternal!=null&&preserveExternal(file.Path)) {
                    preserved++;
                    continue;
                }
                if(file.Before==null)File.Delete(target);else RestoreBackup(file.Before,target,file.Sha256);
                removed++;
            }
            File.Delete(receipt);
            return removed + " arquivo(s) removido(s). " + preserved + " arquivo(s) preexistente(s), compartilhado(s) ou alterado(s) preservado(s).";
        }
    }

    string BackupPath(string hash){if(!FixedData.HashValid(hash))throw new IOException("Backup inválido.");return Safe.In(records,"backups/"+hash+".bin");}
    void SaveBackup(string target,string hash){string path=BackupPath(hash);Safe.NoLinks(path);Directory.CreateDirectory(Path.GetDirectoryName(path));if(!File.Exists(path))File.Copy(target,path,false);if(Safe.Hash(path)!=hash)throw new IOException("Backup divergente; arquivo atual preservado.");}
    void RestoreBackup(string hash,string target,string expected){
        string backup=BackupPath(hash);Safe.NoLinks(backup);if(!File.Exists(backup)||Safe.Hash(backup)!=hash||Safe.Hash(target)!=expected)throw new IOException("O backup ou o destino mudou; preservado.");
        string temp=target+".restore-"+Guid.NewGuid().ToString("N");try{File.Copy(backup,temp,false);if(Safe.Hash(temp)!=hash||Safe.Hash(target)!=expected)throw new IOException("Destino alterado durante restauração.");File.Replace(temp,target,null);}finally{TryCleanup(()=>File.Delete(temp));}
    }
    string Target(string path) { return GeneralInstallRoutes.Resolve(destination,path); }

    void ValidatePaths(string[] entries) {
        if (entries.Length == 0) throw new IOException("O pacote não contém arquivos para instalar.");
        var physical = new HashSet<string>(entries.Select(Target), StringComparer.OrdinalIgnoreCase);
        if(physical.Count!=entries.Length)throw new IOException("Dois arquivos do pacote apontam para o mesmo destino.");
        foreach (string target in physical) {
            if (Directory.Exists(target)) throw Conflict(target);
            for (string parent=Path.GetDirectoryName(target); !String.IsNullOrEmpty(parent); parent=Path.GetDirectoryName(parent))
                if (physical.Contains(parent) || File.Exists(parent)) throw Conflict(parent);
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

    Record ReadRecord(string path,bool inspection=false) {
        Safe.NoLinks(path);
        if (!File.Exists(path)) return null;
        var serializer = new JavaScriptSerializer { MaxJsonLength = Safe.Json.MaxJsonLength };
        Record record = serializer.Deserialize<Record>(inspection?GeneralProjectStorage.ReadText(path,16000000):File.ReadAllText(path));
        if (record == null || record.SourceSha256!=null&&!FixedData.HashValid(record.SourceSha256) || record.Files == null || record.Files.Count == 0 || RecordPath(record.Key) != path)
            throw new IOException("Registro de instalação inválido: " + path);
        GeneralInstallRoutes.Validate(record.InstallRoutes);
        foreach (RecordFile file in record.Files) {
            if (file == null || file.Size < 0 || !FixedData.HashValid(file.Sha256)||file.Before!=null&&!FixedData.HashValid(file.Before)) throw new IOException("Registro de arquivo inválido: " + path);
            Target(file.Path);
        }
        return record;
    }

    IEnumerable<RecordFile> OtherFiles(string receipt, Func<string,bool> activeOwner=null) {
        foreach (string path in Directory.EnumerateFiles(records, "*.json")) {
            if (String.Equals(path, receipt, StringComparison.OrdinalIgnoreCase)) continue;
            Record record = ReadRecord(path);
            if (record == null || (activeOwner != null && !activeOwner(record.Key))) continue;
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
