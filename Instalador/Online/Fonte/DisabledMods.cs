using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

internal sealed class DisabledModFile {
    public string Path, Hash;
    public long Size;
}

internal sealed class DisabledMods {
    readonly string game, cache, index;
    readonly Action checkClosed;
    internal DisabledMods(string game, string data, Action checkClosed = null) {
        this.game = Path.GetFullPath(game).TrimEnd('\\');
        this.checkClosed = checkClosed ?? (() => {});
        string destinationId = InstalledSnapshot.Hash(Encoding.UTF8.GetBytes(this.game.ToUpperInvariant())).Substring(0, 24);
        cache = Safe.In(data, "Cache/Off/" + destinationId);
        index = Safe.In(cache, "mods.json");
        string gamePrefix = this.game + "\\", cachePrefix = cache + "\\";
        if (this.game.StartsWith(@"\\") || gamePrefix.StartsWith(cachePrefix, StringComparison.OrdinalIgnoreCase)
            || cachePrefix.StartsWith(gamePrefix, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Escolha uma pasta local separada do cache.");
    }

    internal Dictionary<string, List<DisabledModFile>> Read() {
        if (!File.Exists(index)) return new Dictionary<string, List<DisabledModFile>>(StringComparer.Ordinal);
        var entries = Safe.Json.Deserialize<Dictionary<string, List<DisabledModFile>>>(GeneralProjectStorage.ReadText(index, 12000000));
        if (entries == null || entries.Count > 5000 || entries.Any(item =>
            !Regex.IsMatch(item.Key, @"\A[A-Za-z0-9_-]{1,120}/[A-Za-z0-9_-]{1,120}\z") ||
            item.Value == null || item.Value.Count == 0 || item.Value.Count > InstalledSnapshot.MaxFiles ||
            item.Value.Any(file => file == null || !FixedData.HashValid(file.Hash) || file.Size < 0 || !AllowedPath(file.Path))))
            throw new IOException("Registro de mods desativados inválido.");
        return entries;
    }

    internal static bool AllowedPath(string path) {
        return !String.IsNullOrEmpty(path) && path.Length <= 1024 && InstalledSnapshot.SafeGenericPath(path)
            && !path.Equals("bin/x64/Cyberpunk2077.exe", StringComparison.OrdinalIgnoreCase);
    }

    internal string BackupFile(string key) {
        List<DisabledModFile> files;
        if(!Read().TryGetValue(key,out files))return null;
        foreach(var file in files){
            string path=Stored(file);
            Safe.NoLinks(path);
            if(File.Exists(path))return path;
        }
        return null;
    }

    internal void Disable(Dictionary<string, List<DisabledModFile>> plan, bool compress=false) {
        using (Lock()) {
            var entries = Read();
            if (plan.Keys.Any(entries.ContainsKey)) throw new IOException("A seleção já contém mods desativados.");
            var files = Unique(plan.Values.SelectMany(filesForMod => filesForMod));
            foreach (var file in files) {
                if (!Matches(GamePath(file), file)) throw Conflict(file);
                var blob = Stored(file);
                if (File.Exists(blob) && !MatchesStored(blob,file)) throw Conflict(file);
            }
            foreach (var pair in plan) entries.Add(pair.Key, pair.Value);
            Safe.WriteJson(index, entries);
            try {
                foreach (var file in files) {
                    checkClosed();
                    string target = GamePath(file), blob = Stored(file);
                    if (!Matches(target, file)) throw Conflict(file);
                    if (!File.Exists(blob) && compress) {
                        PackVerified(target, ZipPath(file), file);
                        if (!Matches(target,file)) throw Conflict(file);
                        File.Delete(target);
                    } else if (!File.Exists(blob) && String.Equals(Path.GetPathRoot(target), Path.GetPathRoot(blob), StringComparison.OrdinalIgnoreCase)) {
                        Directory.CreateDirectory(Path.GetDirectoryName(blob));
                        Safe.NoLinks(blob);
                        File.Move(target, blob);
                    } else {
                        if (!File.Exists(blob)) CopyVerified(target, blob, file);
                        if (!Matches(target, file) || !MatchesStored(blob, file)) throw Conflict(file);
                        File.Delete(target);
                    }
                }
            } catch {
                // The index is written first so the same recovery is available after a restart.
                try { Restore(entries, plan.Keys.ToArray()); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                throw;
            }
        }
    }

    internal void Enable(IEnumerable<string> keys) {
        using (Lock()) {
            var entries = Read();
            Restore(entries, keys.Where(entries.ContainsKey).ToArray());
        }
    }

    void Restore(Dictionary<string, List<DisabledModFile>> entries, string[] keys) {
        var files = Unique(keys.SelectMany(key => entries[key]));
        foreach (var file in files) {
            string path = GamePath(file);
            if (Directory.Exists(path) || (File.Exists(path) ? !Matches(path, file) : !MatchesStored(Stored(file), file)))
                throw Conflict(file);
        }
        var restored = new List<DisabledModFile>();
        try {
            foreach (var file in files) {
                checkClosed();
                string path = GamePath(file);
                if (File.Exists(path)) {
                    if (!Matches(path, file)) throw Conflict(file);
                    continue;
                }
                CopyVerified(Stored(file), path, file);
                restored.Add(file);
            }
            Drop(entries, keys);
        } catch {
            foreach (var file in restored) {
                string path = GamePath(file);
                try { if (Matches(path, file)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            throw;
        }
    }

    internal void Discard(IEnumerable<string> keys) {
        using (Lock()) {
            var entries = Read();
            Drop(entries, keys.Where(entries.ContainsKey).ToArray());
        }
    }

    void Drop(Dictionary<string, List<DisabledModFile>> entries, string[] keys) {
        if (keys.Length == 0) return;
        var old = keys.SelectMany(key => entries[key]).ToArray();
        foreach (var key in keys) entries.Remove(key);
        Safe.WriteJson(index, entries);
        var used = new HashSet<string>(entries.Values.SelectMany(files => files).Select(file => file.Hash), StringComparer.Ordinal);
        foreach (var file in old.GroupBy(file => file.Hash).Select(group => group.First())) {
            if (used.Contains(file.Hash)) continue;
            try {
                foreach(var blob in new[]{Blob(file),ZipPath(file)}){
                    Safe.NoLinks(blob);
                    File.Delete(blob);
                }
            } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    FileStream Lock() {
        checkClosed();
        Safe.NoLinks(cache);
        Directory.CreateDirectory(cache);
        return new FileStream(Safe.In(cache, "operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    string GamePath(DisabledModFile file) {
        if (!AllowedPath(file.Path)) throw Conflict(file);
        return GeneralInstallRoutes.Resolve(game, file.Path, true);
    }
    string Blob(DisabledModFile file) { return Safe.In(cache, "files/" + file.Hash + ".bin"); }
    string ZipPath(DisabledModFile file) { return Safe.In(cache, "files/" + file.Hash + ".zip"); }
    string Stored(DisabledModFile file) {
        string zip=ZipPath(file);
        return File.Exists(zip)?zip:Blob(file);
    }

    static ZipArchiveEntry Payload(ZipArchive zip,DisabledModFile file) {
        var entry=zip.GetEntry("payload");
        if(zip.Entries.Count!=1||entry==null||entry.Length!=file.Size)throw Conflict(file);
        return entry;
    }

    static bool MatchesStored(string path,DisabledModFile file) {
        if(!path.EndsWith(".zip",StringComparison.OrdinalIgnoreCase))return Matches(path,file);
        Safe.NoLinks(path);
        if(!File.Exists(path))return false;
        using(var zip=ZipFile.OpenRead(path))
        using(var input=Payload(zip,file).Open())
        using(var sha=SHA256.Create())return Safe.Hex(sha.ComputeHash(input))==file.Hash;
    }

    static void PackVerified(string source,string destination,DisabledModFile file) {
        Safe.NoLinks(source);
        Safe.NoLinks(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        string staging=Path.Combine(Path.GetDirectoryName(destination),".ml-"+Guid.NewGuid().ToString("N").Substring(0,12)+".zip");
        try{
            using(var zip=ZipFile.Open(staging,ZipArchiveMode.Create))
            using(var output=zip.CreateEntry("payload",CompressionLevel.Fastest).Open())
            using(var input=File.OpenRead(source))input.CopyTo(output);
            if(!MatchesStored(staging,file))throw Conflict(file);
            File.Move(staging,destination);
        }finally{File.Delete(staging);}
    }

    static DisabledModFile[] Unique(IEnumerable<DisabledModFile> files) {
        return files.GroupBy(file => file.Path, StringComparer.OrdinalIgnoreCase).Select(group => {
            var file = group.First();
            if (group.Any(other => other.Hash != file.Hash || other.Size != file.Size)) throw Conflict(file);
            return file;
        }).ToArray();
    }

    static bool Matches(string path, DisabledModFile file) {
        Safe.NoLinks(path);
        return File.Exists(path) && new FileInfo(path).Length == file.Size && Safe.Hash(path) == file.Hash;
    }

    static void CopyVerified(string source, string destination, DisabledModFile file) {
        Safe.NoLinks(source);
        Safe.NoLinks(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        string staging = Path.Combine(Path.GetDirectoryName(destination), ".ml-" + Guid.NewGuid().ToString("N").Substring(0, 12) + ".tmp");
        try {
            if(source.EndsWith(".zip",StringComparison.OrdinalIgnoreCase)){
                using(var zip=ZipFile.OpenRead(source))
                using(var input=Payload(zip,file).Open())
                using(var output=new FileStream(staging,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                    InstalledSnapshotInstaller.CopyBounded(input,output,file.Size);
            }else File.Copy(source, staging, false);
            if (!Matches(staging, file)) throw Conflict(file);
            File.Move(staging, destination);
        } finally { File.Delete(staging); }
    }

    static IOException Conflict(DisabledModFile file) {
        return new IOException(GeneralStyle.T("Arquivo não pôde ser conferido: ", "File could not be verified: ") + file.Path);
    }
}
