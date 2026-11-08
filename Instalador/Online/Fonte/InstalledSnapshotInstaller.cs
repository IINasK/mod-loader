using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

public sealed partial class InstalledSnapshotInstaller {
    public sealed class Receipt {
        public string Id, Root, Status;
        public bool Recoverable;
        public InstalledSnapshotManifest Manifest;
        public List<string> ActiveOwners = new List<string>();
        public List<Change> Files = new List<Change>();
    }
    public sealed class Change {
        public string Path, Before, After;
        public string Expected;
        public long Size;
        public bool Owned, Applied, Reverted;
    }

    readonly string destination, appData, records;
    readonly FixedCatalog catalog;
    readonly bool fixture;

    public InstalledSnapshotInstaller(string destination, string appData, FixedCatalog catalog, bool fixture = false) {
        this.destination = Path.GetFullPath(destination).TrimEnd('\\');
        this.appData = Path.GetFullPath(appData).TrimEnd('\\');
        if (this.destination.Length <= 3 || this.destination.StartsWith(@"\\") || this.appData.StartsWith(@"\\"))
            throw new IOException("Escolha pastas locais para a instalação e os dados do programa.");
        if ((this.destination + "\\").StartsWith(this.appData + "\\", StringComparison.OrdinalIgnoreCase)
            || (this.appData + "\\").StartsWith(this.destination + "\\", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Os dados do programa precisam ficar separados da pasta de instalação.");
        Safe.NoLinks(this.destination); Safe.NoLinks(this.appData);
        if (!Directory.Exists(this.destination)) throw new IOException("Escolha uma pasta de instalação existente.");
        if (fixture && (!File.Exists(Safe.In(this.destination, ".fixture-only"))
            || !Regex.IsMatch(this.destination, @"(?i)[\\/]online-testes-[a-f0-9]{32}([\\/]|$)")))
            throw new IOException("Exceção de teste permitida apenas dentro de uma fixture isolada.");
        this.fixture = fixture;
        this.catalog = catalog;
        records = Safe.In(this.appData, "Snapshots/" + Fingerprint(this.destination.ToUpperInvariant()));
    }

    public string Install(string zipPath, string expectedHash, Action<string> report, int failAfter = -1, Func<ModVersionDifference,bool> chooseVersion = null, Action<string> transaction = null, Action checkpoint = null, Action<P2PInstallProgress> progress = null, bool allowReuse = false) {
        // The archive is leased throughout verification and copying; no archive is kept in the receipt store.
        zipPath = Path.GetFullPath(zipPath); Safe.NoLinks(zipPath);
        using (var source = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read)) {
            var manifest = InstalledSnapshot.Read(zipPath, expectedHash, catalog);
            if(!allowReuse&&manifest.Files.Any(file=>file.ReuseLocal))throw new IOException("O recebimento não autorizou reutilizar arquivos locais.");
            using (var hash = SHA256.Create()) if (Safe.Hex(hash.ComputeHash(source)) != expectedHash)
                throw new IOException("O pacote foi alterado antes da instalação.");
            source.Position = 0;
            using (AcquireLock(manifest.Game)) {
                ValidateEnvironment(manifest);
                var receipts = AllReceipts().ToList();
                var pending=receipts.Where(r=>r.Status=="preparing"||r.Status=="installing").ToList();
                if(pending.Count!=0){
                    if(pending.Count!=1||!pending[0].Recoverable||!SameManifest(pending[0].Manifest,manifest))throw new IOException("Há outra instalação interrompida nesta pasta. Preserve os recibos e restaure a operação antes de continuar.");
                    if(transaction!=null)transaction(pending[0].Id);
                    return Resume(pending[0],report,checkpoint,progress);
                }
                var complete=receipts.FirstOrDefault(r=>r.Status=="installed"&&r.ActiveOwners.Count==WorkKeys(manifest).Count()&&SameManifest(r.Manifest,manifest)&&r.Files.All(Matches));
                if(complete!=null){CleanStage(Safe.In(destination,".mod-loader-snapshot-"+complete.Id));return complete.Id;}
                var approvals=ConfirmVersions(manifest,chooseVersion,receipts);
                CheckSpace(manifest);
                var others = receipts.Where(r => r.Status == "installed" && r.ActiveOwners.Count > 0).ToList();
                var receipt = new Receipt {
                    Id = Guid.NewGuid().ToString("N"), Root = destination, Status = "preparing", Manifest = manifest, Recoverable=true,
                    ActiveOwners = WorkKeys(manifest).ToList()
                };
                string stage = Safe.In(destination, ".mod-loader-snapshot-" + receipt.Id);
                var directories = new List<string>();
                var beforeHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var writtenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                bool keepStage=false;
                if(transaction!=null)transaction(receipt.Id);
                Directory.CreateDirectory(stage);
                try {
                    var groups=manifest.Files.GroupBy(f=>f.Path,StringComparer.OrdinalIgnoreCase).ToArray();
                    using (var zip = new ZipArchive(source, ZipArchiveMode.Read, true)) {
                        foreach (var group in groups) {
                            if(checkpoint!=null)checkpoint();
                            if(progress!=null)progress(new P2PInstallProgress(false,receipt.Files.Count,groups.Length,group.Key));
                            string target = Safe.In(destination, group.Key);
                            if (Directory.Exists(target)) throw Conflict(group.Key);
                            string before = HashOrNull(target);
                            beforeHashes.Add(group.Key, before);
                            string prepared = Safe.In(stage, Fingerprint(group.Key));
                            var files = group.ToList();
                            var local = files[0].ReuseLocal;
                            if (local) {
                                var file=files[0];
                                if (before!=file.Sha256 || new FileInfo(target).Length!=file.Size)
                                    throw new IOException(GeneralStyle.T("Arquivo local mudou após a comparação. Peça um novo convite: ","A local file changed after comparison. Ask for a new invitation: ")+file.Path);
                                using (var input=File.OpenRead(target)) InstalledSnapshot.ValidateCustomData(manifest,file,input);
                            } else if (files[0].Kind == "file") {
                                var file = files[0];
                                ZipArchiveEntry entry = zip.GetEntry("files/" + file.Sha256);
                                if (entry == null || entry.Length != file.Size) throw new IOException("Arquivo ausente no pacote: " + file.Path);
                                using (var input = entry.Open())
                                using (var output = new FileStream(prepared, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                                    CopyBounded(input, output, file.Size);
                                }
                                if (Safe.Hash(prepared) != file.Sha256) throw new IOException("Arquivo divergente no pacote: " + file.Path);
                                if(before!=null&&before!=file.Sha256){string approved;if(!approvals.TryGetValue(file.Path,out approved)||approved!=before)throw Conflict(file.Path);}
                            } else {
                                byte[] content = File.Exists(target) ? InstalledSnapshotConfig.Read(target) : null;
                                foreach (var file in files) content = InstalledSnapshotConfig.Apply(file, content);
                                File.WriteAllBytes(prepared, content);
                            }
                            string after = local ? before : Safe.Hash(prepared);
                            if (before != null && before != after) Backup(target, Safe.In(stage, Fingerprint(group.Key) + ".rollback"), before);
                            var change = new Change { Path = group.Key, Before = before, Expected=before, After = after, Size = local ? files[0].Size : new FileInfo(prepared).Length, Owned = before == null };
                            var previous = others.SelectMany(r => r.Files.Select(f => new { Receipt = r, File = f }))
                                .FirstOrDefault(x => x.File.Path.Equals(group.Key, StringComparison.OrdinalIgnoreCase)
                                    && !x.File.Reverted && x.File.Applied && x.File.After == before && x.File.Owned);
                            if (previous != null && before == after) {
                                change.Owned = true; change.Before = previous.File.Before;
                                if (change.Before != null) Backup(BackupPath(previous.Receipt, change.Path), BackupPath(receipt, change.Path), change.Before);
                            } else if (before != null && before != after) {
                                // Configuration changes and explicitly approved file replacements retain a verified backup.
                                change.Owned = true;
                                Backup(target, BackupPath(receipt, change.Path), before);
                            }
                            receipt.Files.Add(change);
                            Report(report, "Conferido: " + group.Key);
                        }
                    }
                    Save(receipt);
                    int written = 0;
                    try {
                        receipt.Status = "installing"; Save(receipt);
                        int processed=0;
                        foreach (var change in receipt.Files) {
                            if(checkpoint!=null)checkpoint();
                            if(progress!=null)progress(new P2PInstallProgress(true,processed++,receipt.Files.Count,change.Path));
                            Closed(manifest.Game);
                            string target = Safe.In(destination, change.Path);
                            string prepared = Safe.In(stage, Fingerprint(change.Path));
                            string current = HashOrNull(target);
                            string expectedCurrent = beforeHashes[change.Path];
                            if (current != expectedCurrent) throw new IOException("O destino mudou durante a preparação: " + change.Path);
                            if (current == change.After) { change.Applied = true; Save(receipt); continue; }
                            CreateParents(change.Path, directories);
                            ReplaceChecked(prepared, target, change.After, expectedCurrent,Safe.In(stage,"replace-"+Fingerprint(change.Path)));
                            writtenPaths.Add(change.Path);
                            change.Applied = true; Save(receipt);
                            written++;
                            if (failAfter >= 0 && written >= failAfter) throw new IOException("Falha de teste após a cópia.");
                            Report(report, "Instalado: " + change.Path);
                        }
                        receipt.Status = "installed"; Save(receipt);
                        if(progress!=null)progress(new P2PInstallProgress(true,receipt.Files.Count,receipt.Files.Count,null));
                        return receipt.Id;
                    } catch(OperationCanceledException) {
                        keepStage=true;
                        throw;
                    } catch {
                        Rollback(receipt, beforeHashes, writtenPaths, stage);
                        foreach (string directory in directories.AsEnumerable().Reverse()) TryCleanup(() => DeleteEmpty(directory));
                        throw;
                    }
                } finally { if(!keepStage)CleanStage(stage); }
            }
        }
    }

    Dictionary<string,string> ConfirmVersions(InstalledSnapshotManifest manifest,Func<ModVersionDifference,bool> choose,List<Receipt> receipts){
        var ids=new HashSet<string>(manifest.List.Mods.Select(m=>m.Id),StringComparer.Ordinal);
        var paths=new HashSet<string>(manifest.Files.Where(f=>f.Kind=="file").Select(f=>f.Path),StringComparer.OrdinalIgnoreCase);
        var obsolete=receipts.Where(r=>r.Status=="installed").SelectMany(r=>r.Manifest.Files.Where(f=>f.Kind=="file"&&f.Owners.Any(o=>r.ActiveOwners.Contains(o)&&ids.Contains(o.Split('/')[0]))&&!paths.Contains(f.Path))).Where(f=>File.Exists(Safe.In(destination,f.Path))||Directory.Exists(Safe.In(destination,f.Path))).Select(f=>f.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if(obsolete.Count>0)new ModVersionDifference{Name=manifest.Name,Current=GeneralStyle.T("Outra versão registrada","Another recorded version"),Incoming=manifest.Name,CanReplace=false,Detail=GeneralStyle.T("A versão anterior contém arquivos que não fazem parte do novo pacote. Remova a versão anterior pelo gerenciador para evitar misturar versões. Nada foi apagado.","The previous version contains files absent from the new pack. Remove the previous version through the manager to avoid mixing versions. Nothing was deleted.")+"\r\n"+String.Join("\r\n",obsolete.Take(6))}.Require(choose);
        var changed=manifest.Files.Where(f=>f.Kind=="file"&&!f.ReuseLocal).GroupBy(f=>f.Path,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).Where(f=>File.Exists(Safe.In(destination,f.Path))&&HashOrNull(Safe.In(destination,f.Path))!=f.Sha256).ToList();
        var expected=changed.ToDictionary(f=>f.Path,f=>HashOrNull(Safe.In(destination,f.Path)),StringComparer.OrdinalIgnoreCase);
        foreach(var group in changed.GroupBy(f=>String.Join(", ",manifest.List.Mods.Where(m=>f.Owners.Any(owner=>owner.StartsWith(m.Id+"/",StringComparison.Ordinal))).Select(m=>m.Name)))){
            var files=group.ToList();var known=catalog.Packages.FirstOrDefault(m=>m.Files.Any(f=>files.Any(p=>p.Path.Equals(f.Path,StringComparison.OrdinalIgnoreCase))));
            new ModVersionDifference{Name=group.Key,Current=String.Join("; ",files.Select(f=>ModVersionDifference.FileVersion(Safe.In(destination,f.Path))).Distinct()),Incoming=known==null?GeneralStyle.T("Conteúdo do pacote recebido","Received pack content"):known.Version,CanReplace=!files.Any(f=>IsImmutable(f.Path)&&!NightCoopRelease.AllowsUpgrade(f,expected[f.Path],catalog)),Detail=String.Join("\r\n",files.Take(6).Select(f=>f.Path))+"\r\n"+GeneralStyle.T("O pacote aguarda sua escolha. A troca guarda os arquivos anteriores para reversão. A base protegida só aceita atualizações oficiais reconhecidas.","The pack waits for your choice. Replacement backs up previous files. The protected base only accepts recognized official updates.")}.Require(choose);
        }
        return expected;
    }

    public List<InstalledSnapshotFile> ReadInstalledFiles(string receiptId, string workKey) {
        var receipt = Read(receiptId);
        if (receipt == null || receipt.Status != "installed" || !receipt.ActiveOwners.Contains(workKey))
            throw new IOException("O mod não tem recibo de snapshot instalado nesta pasta.");
        var files = receipt.Manifest.Files.Where(f => f.Owners.Contains(workKey)).ToList();
        var exported = new List<InstalledSnapshotFile>();
        foreach (var file in files) {
            var change = receipt.Files.Single(f => f.Path.Equals(file.Path, StringComparison.OrdinalIgnoreCase));
            if (file.Kind == "file") {
                if (!Matches(change)) throw new IOException("O arquivo instalado está ausente ou alterado: " + file.Path);
                exported.Add(file);
            } else {
                string target = Safe.In(destination, file.Path);
                if (!File.Exists(target) || new FileInfo(target).Length > 1048576) throw new IOException("Configuração instalada ausente ou excessiva: " + file.Path);
                byte[] current = InstalledSnapshotConfig.Read(target);
                string content = file.Kind == "json" ? InstalledSnapshotConfig.Json(current) : InstalledSnapshotConfig.Section(current, file.Section);
                if (content == null) throw new IOException("Seção de configuração instalada ausente: " + file.Section);
                byte[] bytes = Encoding.UTF8.GetBytes(content);
                exported.Add(new InstalledSnapshotFile { Path = file.Path, Kind = file.Kind, Section = file.Section,
                    Content = content, Size = bytes.LongLength, Sha256 = InstalledSnapshot.Hash(bytes), Owners = new List<string>(file.Owners) });
            }
        }
        return exported;
    }
    string Resume(Receipt receipt,Action<string> report,Action checkpoint,Action<P2PInstallProgress> progress){
        if(receipt.Files.Count!=receipt.Manifest.Files.Select(f=>f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count())throw new IOException("Preparação interrompida incompleta. Preserve o pacote e o recibo.");
        string stage=Safe.In(destination,".mod-loader-snapshot-"+receipt.Id);Safe.NoLinks(stage);
        foreach(var file in receipt.Files){
            string current=HashOrNull(Safe.In(destination,file.Path));
            if(current==file.After)continue;
            string prepared=Safe.In(stage,Fingerprint(file.Path));
            if(current!=file.Expected||!File.Exists(prepared)||new FileInfo(prepared).Length!=file.Size||Safe.Hash(prepared)!=file.After)throw new IOException("Não foi possível retomar: arquivo alterado ou preparação ausente em "+file.Path+". Sua alteração foi preservada.");
        }
        receipt.Status="installing";Save(receipt);
        int processed=0;
        foreach(var file in receipt.Files){
            if(checkpoint!=null)checkpoint();
            if(progress!=null)progress(new P2PInstallProgress(true,processed++,receipt.Files.Count,file.Path));
            Closed(receipt.Manifest.Game);string target=Safe.In(destination,file.Path);
            if(HashOrNull(target)!=file.After){CreateParents(file.Path,new List<string>());ReplaceChecked(Safe.In(stage,Fingerprint(file.Path)),target,file.After,file.Expected,Safe.In(stage,"replace-"+Fingerprint(file.Path)));}
            file.Applied=true;Save(receipt);Report(report,"Retomado: "+file.Path);
        }
        receipt.Status="installed";Save(receipt);CleanStage(stage);
        if(progress!=null)progress(new P2PInstallProgress(true,receipt.Files.Count,receipt.Files.Count,null));
        return receipt.Id;
    }

    internal string DiscardInterrupted(string transaction,string zip,string expectedHash) {
        Receipt receipt;
        if(transaction!=null){
            receipt=Read(transaction);
            if(receipt==null){
                CleanStage(Safe.In(destination,".mod-loader-snapshot-"+transaction));
                return null;
            }
        }else{
            if(expectedHash==null||!File.Exists(zip))return null;
            var pending=AllReceipts().Where(r=>r.Recoverable&&(r.Status=="preparing"||r.Status=="installing")).ToArray();
            if(pending.Length==0)return null;
            var manifest=InstalledSnapshot.Read(zip,expectedHash,catalog);
            receipt=pending.SingleOrDefault(r=>SameManifest(r.Manifest,manifest));
            if(receipt==null)return null;
        }
        if(receipt.Status=="installed")return receipt.Id;
        if(receipt.Status!="preparing"&&receipt.Status!="installing")return null;
        using(AcquireLock(receipt.Manifest.Game)){
            Closed(receipt.Manifest.Game);
            string stage=Safe.In(destination,".mod-loader-snapshot-"+receipt.Id);
            var before=receipt.Files.ToDictionary(f=>f.Path,f=>f.Expected,StringComparer.OrdinalIgnoreCase);
            var written=new HashSet<string>(receipt.Files.Where(f=>f.Expected!=f.After&&
                (f.Applied||HashOrNull(Safe.In(destination,f.Path))==f.After)).Select(f=>f.Path),StringComparer.OrdinalIgnoreCase);
            Rollback(receipt,before,written,stage);
            CleanStage(stage);
        }
        return null;
    }

    internal InstalledSnapshotManifest ReadManifest(string receiptId) {
        var receipt = Read(receiptId);
        if (receipt == null || receipt.Status != "installed") throw new IOException("Recibo de snapshot instalado ausente.");
        return receipt.Manifest;
    }

    public string Status(string receiptId, string workKey) {
        var receipt = Read(receiptId);
        if (receipt == null || receipt.Status != "installed" || !receipt.ActiveOwners.Contains(workKey)) return GeneralModInstaller.Missing;
        var paths = new HashSet<string>(receipt.Manifest.Files.Where(f => f.Owners.Contains(workKey)).Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
        int present = 0;
        foreach (var file in receipt.Files.Where(f => paths.Contains(f.Path))) {
            string target = Safe.In(destination, file.Path);
            if (Directory.Exists(target)) return GeneralModInstaller.Modified;
            if (!File.Exists(target)) continue;
            bool code = receipt.Manifest.Files.Any(f => f.Kind == "file" && f.Path.Equals(file.Path, StringComparison.OrdinalIgnoreCase));
            if (code && !Matches(file)) return GeneralModInstaller.Modified;
            present++;
        }
        return present == 0 ? GeneralModInstaller.Missing : present == paths.Count ? GeneralModInstaller.Installed : GeneralModInstaller.Partial;
    }

    public string Remove(string receiptId, IEnumerable<string> workKeys, Action<string> report, bool forgetOnly=false) {
        var initial = Read(receiptId);
        if (initial == null) return "Nenhum snapshot instalado para remover.";
        using (AcquireLock(initial.Manifest.Game)) {
            var receipt = Read(receiptId); Closed(receipt.Manifest.Game);
            var removing = new HashSet<string>(workKeys ?? new string[0], StringComparer.Ordinal);
            if (removing.Any(k => !WorkKeys(receipt.Manifest).Contains(k))) throw new IOException("Mod fora do recibo de instalação.");
            removing.IntersectWith(receipt.ActiveOwners);
            var others = AllReceipts().Where(r => r.Id != receipt.Id && r.Status == "installed" && r.ActiveOwners.Count > 0).ToList();
            if(!forgetOnly)EnsureDependencies(receipt, removing, others);
            var remaining = new HashSet<string>(receipt.ActiveOwners.Where(k => !removing.Contains(k)), StringComparer.Ordinal);
            int removed = 0, preserved = 0;
            foreach (var change in forgetOnly ? Enumerable.Empty<Change>() : receipt.Files.AsEnumerable().Reverse()) {
                var owners = receipt.Manifest.Files.Where(f => f.Path.Equals(change.Path, StringComparison.OrdinalIgnoreCase)).SelectMany(f => f.Owners).Distinct().ToList();
                if (!owners.Any(removing.Contains)) continue;
                if (owners.Any(remaining.Contains) || receipt.Manifest.Game == "cyberpunk2077" && IsImmutable(change.Path)
                    || others.Any(r => r.Manifest.Files.Any(f => f.Path.Equals(change.Path, StringComparison.OrdinalIgnoreCase) && f.Owners.Any(r.ActiveOwners.Contains)))) {
                    preserved++; continue;
                }
                if (!change.Owned || change.Reverted || !change.Applied || !Matches(change)) { preserved++; continue; }
                Closed(receipt.Manifest.Game);
                if (change.Before == null) DeleteChecked(Safe.In(destination, change.Path), change.After);
                else ReplaceChecked(VerifiedBackup(receipt, change), Safe.In(destination, change.Path), change.Before, change.After);
                change.Reverted = true; Save(receipt); removed++;
                Report(report, "Removido: " + change.Path);
            }
            receipt.ActiveOwners = remaining.ToList(); Save(receipt);
            return removed + " arquivo(s) removido(s). " + preserved + " arquivo(s) preexistente(s), compartilhado(s), da base ou alterado(s) preservado(s).";
        }
    }

    void ValidateEnvironment(InstalledSnapshotManifest manifest) {
        Closed(manifest.Game);
        if (manifest.Game != "cyberpunk2077") {
            if (File.Exists(Safe.In(destination, "bin/x64/Cyberpunk2077.exe"))) throw new IOException("Um snapshot genérico precisa de outro destino; esta pasta pertence ao Cyberpunk 2077.");
            return;
        }
        var ids = new HashSet<string>(manifest.List.Mods.SelectMany(m => m.Variants).Select(v => v.CyberpunkId).Where(s => !String.IsNullOrEmpty(s)), StringComparer.Ordinal);
        var mods = FixedData.Expand(catalog, ids);
        FixedData.RequireInstallable(mods);
        if (manifest.GameFileVersion != catalog.GameFileVersion) throw new IOException("O snapshot foi preparado para outra versão do jogo.");
        if (!fixture) FixedData.ValidateGame(destination, mods, manifest.GameFileVersion);
        else foreach (var mod in mods) { string conflict = FixedData.Incompatibility(mod, destination); if (conflict != null) throw new IOException(conflict); }
        foreach (var file in manifest.Files.Where(f => IsImmutable(f.Path))) {
            var known = catalog.Packages.Where(m => m.Immutable).SelectMany(m => m.Files).Single(f => f.Path.Equals(file.Path, StringComparison.OrdinalIgnoreCase));
            if (file.Kind != "file" || file.Sha256 != known.Sha256 || file.Size != known.Size) throw new IOException("A base NightCoop/RED4ext precisa corresponder ao catálogo original.");
        }
    }

    void CheckSpace(InstalledSnapshotManifest manifest) {
        long staged = 0, copies = 0, backups = 0;
        foreach (var group in manifest.Files.GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)) {
            var file = group.First();
            if(file.ReuseLocal)continue;
            string path = Safe.In(destination, file.Path);
            if (file.Kind == "file") {
                staged = checked(staged + file.Size);
                if (!File.Exists(path)) copies = checked(copies + file.Size);
                else if(HashOrNull(path)!=file.Sha256){long size=new FileInfo(path).Length;backups=checked(backups+size);staged=checked(staged+size);copies=checked(copies+file.Size);}
            } else {
                staged = checked(staged + 3L * 1048576);
                copies = checked(copies + 2L * 1048576);
                if (File.Exists(path)) {
                    long size = new FileInfo(path).Length;
                    if (size > 1048576) throw new IOException("Configuração local excessiva: " + file.Path);
                    backups = checked(backups + size);
                }
            }
        }
        string targetDrive = Path.GetPathRoot(destination), dataDrive = Path.GetPathRoot(records);
        long targetNeeded = checked(staged + copies + 100L * 1048576);
        long dataNeeded = checked(backups + 32L * 1048576);
        if (String.Equals(targetDrive, dataDrive, StringComparison.OrdinalIgnoreCase)) targetNeeded = checked(targetNeeded + dataNeeded);
        if (new DriveInfo(targetDrive).AvailableFreeSpace < targetNeeded || new DriveInfo(dataDrive).AvailableFreeSpace < dataNeeded)
            throw new IOException("Espaço insuficiente para preparar os arquivos e preservar as configurações anteriores.");
    }

    void EnsureDependencies(Receipt receipt, HashSet<string> removing, List<Receipt> others) {
        if (receipt.Manifest.Game != "cyberpunk2077") return;
        var removedIds = new HashSet<string>(receipt.Manifest.List.Mods.SelectMany(m => m.Variants.Where(v => removing.Contains(m.Id + "/" + v.Id))).Select(v => v.CyberpunkId).Where(s => !String.IsNullOrEmpty(s)), StringComparer.Ordinal);
        foreach (var r in others.Concat(new[] { receipt }))
            foreach (var mod in r.Manifest.List.Mods)
                foreach (var v in mod.Variants)
                    if (r.ActiveOwners.Contains(mod.Id + "/" + v.Id) && (r.Id != receipt.Id || !removing.Contains(mod.Id + "/" + v.Id))) removedIds.Remove(v.CyberpunkId ?? "");
        removedIds.RemoveWhere(id => catalog.Packages.Any(m => m.Id == id && m.Immutable));
        foreach (var mod in catalog.Packages.Where(m => !removedIds.Contains(m.Id))) {
            if (!FixedData.Expand(catalog, new[] { mod.Id }).Any(m => removedIds.Contains(m.Id))) continue;
            var scan = FixedData.Scan(mod, destination, false);
            if (scan.Correct + scan.Original + scan.Different > 0 || scan.State == "Não verificável")
                throw new IOException("Remova também o mod que depende desta seleção: " + mod.Name + ". Nenhum arquivo foi removido.");
        }
    }

    bool IsImmutable(string path) { return catalog != null && catalog.Packages.Any(m => m.Immutable && m.Files.Any(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase))); }
    static bool SameManifest(InstalledSnapshotManifest left,InstalledSnapshotManifest right){
        string first=Safe.Json.Serialize(left),second=Safe.Json.Serialize(right);
        if(first==second)return true;
        return SameJson(Safe.Json.DeserializeObject(first),Safe.Json.DeserializeObject(second));
    }
    static bool SameJson(object left,object right){
        var fields=left as IDictionary<string,object>;
        if(fields!=null){
            var other=right as IDictionary<string,object>;
            return other!=null&&fields.Count==other.Count&&fields.All(field=>{
                object value;
                return other.TryGetValue(field.Key,out value)&&SameJson(field.Value,value);
            });
        }
        var items=left as object[];
        if(items!=null){
            var other=right as object[];
            return other!=null&&items.Length==other.Length&&items.Zip(other,SameJson).All(equal=>equal);
        }
        return Object.Equals(left,right);
    }
    static IEnumerable<string> WorkKeys(InstalledSnapshotManifest manifest) { return manifest.List.Mods.SelectMany(m => m.Variants.Select(v => m.Id + "/" + v.Id)); }
    void Closed(string game) {
        if (fixture || game != "cyberpunk2077") return;
        foreach (var process in Process.GetProcessesByName("Cyberpunk2077")) { process.Dispose(); throw new IOException("Feche o jogo antes de instalar ou remover mods."); }
    }
    FileStream AcquireLock(string game) {
        string folder = game == "cyberpunk2077" ? Safe.In(destination, ".CoopModsOnline")
            : Safe.In(appData, "Instalacoes/" + Fingerprint((destination + "\\").ToUpperInvariant()));
        Directory.CreateDirectory(folder); Safe.NoLinks(folder);
        return new FileStream(Safe.In(folder, game == "cyberpunk2077" ? "operacao.lock" : "operations.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    string ReceiptPath(string id) {
        if (!Regex.IsMatch(id ?? "", "^[a-f0-9]{32}$")) throw new IOException("Identificador de snapshot inválido.");
        return Safe.In(records, id + ".json");
    }
    string BackupPath(Receipt receipt, string path) { return Safe.In(records, "backups/" + receipt.Id + "/" + Fingerprint(path) + ".bin"); }
    string VerifiedBackup(Receipt receipt, Change change) {
        string backup = BackupPath(receipt, change.Path);
        if (!File.Exists(backup) || Safe.Hash(backup) != change.Before) throw new IOException("O backup local não confere; o arquivo atual foi preservado.");
        return backup;
    }
    void Save(Receipt receipt) {
        byte[] content = Encoding.UTF8.GetBytes(Safe.Json.Serialize(receipt));
        Safe.WriteJson(ReceiptPath(receipt.Id), new ReceiptEnvelope { Protected = Convert.ToBase64String(ProtectedData.Protect(content, null, DataProtectionScope.CurrentUser)) });
    }
    Receipt Read(string id){return ReadCore(id,false);}
    Receipt ReadCore(string id,bool inspection) {
        string path = ReceiptPath(id);
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 24000000) throw new IOException("Recibo de snapshot excessivo.");
        Receipt receipt;
        try {
            ReceiptEnvelope envelope;
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, inspection?FileShare.ReadWrite|FileShare.Delete:FileShare.Read))
                envelope = Safe.Json.Deserialize<ReceiptEnvelope>(Encoding.UTF8.GetString(InstalledSnapshot.ReadBytes(input, 24000000)));
            receipt = Safe.Json.Deserialize<Receipt>(Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(envelope.Protected), null, DataProtectionScope.CurrentUser)));
        } catch (Exception ex) {
            if (ex is OutOfMemoryException) throw;
            throw new IOException("Recibo de snapshot alterado, corrompido ou criado por outra conta do Windows.", ex);
        }
        if (receipt == null || receipt.Id != id || !String.Equals(receipt.Root, destination, StringComparison.OrdinalIgnoreCase)
            || receipt.Files == null || receipt.Files.Count > 20000 || receipt.ActiveOwners == null
            || !new[] { "preparing", "installing", "installed", "rolledback", "rollback-preserved" }.Contains(receipt.Status)) throw new IOException("Recibo de snapshot inválido.");
        InstalledSnapshot.Validate(receipt.Manifest,NightCoopRelease.CatalogForReceipt(catalog,receipt.Manifest));
        var keys = new HashSet<string>(WorkKeys(receipt.Manifest), StringComparer.Ordinal);
        if (receipt.ActiveOwners.Count > keys.Count || receipt.ActiveOwners.Distinct().Count() != receipt.ActiveOwners.Count || receipt.ActiveOwners.Any(k => !keys.Contains(k))) throw new IOException("Proprietários inválidos no recibo.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in receipt.Files) {
            if (file == null || !paths.Add(file.Path) || !FixedData.HashValid(file.After) || file.Size < 0 || file.Size > InstalledSnapshot.MaxFileBytes
                || file.Before != null && !FixedData.HashValid(file.Before)||file.Expected!=null&&!FixedData.HashValid(file.Expected)
                || !receipt.Manifest.Files.Any(f => f.Path.Equals(file.Path, StringComparison.OrdinalIgnoreCase))) throw new IOException("Arquivo inválido no recibo de snapshot.");
            Safe.In(destination, file.Path);
            var payload = receipt.Manifest.Files.First(f => f.Path.Equals(file.Path, StringComparison.OrdinalIgnoreCase));
            if (payload.Kind == "file" && (payload.Sha256 != file.After || payload.Size != file.Size)) throw new IOException("Arquivo do recibo não corresponde ao snapshot.");
        }
        if (receipt.Status == "installed" && paths.Count != receipt.Manifest.Files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count()) throw new IOException("Recibo de snapshot incompleto.");
        return receipt;
    }
    IEnumerable<Receipt> AllReceipts(bool inspection=false) {
        Safe.NoLinks(records);
        if (!Directory.Exists(records)) yield break;
        foreach (string path in Directory.GetFiles(records, "*.json")) { var receipt = ReadCore(Path.GetFileNameWithoutExtension(path),inspection); if (receipt != null) yield return receipt; }
    }
    bool Matches(Change file) { string path = Safe.In(destination, file.Path); return File.Exists(path) && new FileInfo(path).Length == file.Size && Safe.Hash(path) == file.After; }
    void Rollback(Receipt receipt, Dictionary<string,string> beforeHashes, HashSet<string> written, string stage) {
        int preserved = 0;
        foreach (var file in receipt.Files.AsEnumerable().Reverse()) {
            if (!written.Contains(file.Path)) continue;
            try {
                string target = Safe.In(destination, file.Path);
                if (HashOrNull(target) != file.After) { preserved++; continue; }
                string before = beforeHashes[file.Path];
                if (before == null) DeleteChecked(target, file.After);
                else ReplaceChecked(Safe.In(stage, Fingerprint(file.Path) + ".rollback"), target, before, file.After);
                file.Reverted = true;
            } catch (IOException) { preserved++; } catch (UnauthorizedAccessException) { preserved++; }
        }
        receipt.ActiveOwners.Clear(); receipt.Status = preserved == 0 ? "rolledback" : "rollback-preserved"; Save(receipt);
    }
    static void ReplaceChecked(string source, string target, string desiredHash, string expectedCurrent,string preparedTemp=null) {
        Safe.NoLinks(source); Safe.NoLinks(target);
        string temp = preparedTemp??target + ".snapshot-" + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            if(preparedTemp!=null){Safe.NoLinks(temp);if(File.Exists(temp))File.Delete(temp);}
            File.Copy(source, temp, false);
            if (Safe.Hash(temp) != desiredHash) throw new IOException("O arquivo preparado mudou durante a cópia.");
            if (Directory.Exists(target) || HashOrNull(target) != expectedCurrent) throw new IOException("O destino mudou durante a cópia; sua alteração foi preservada.");
            if (expectedCurrent == null) File.Move(temp, target); else File.Replace(temp, target, null);
        } finally { TryCleanup(() => { Safe.NoLinks(temp); if (File.Exists(temp)) File.Delete(temp); }); }
    }
    static void DeleteChecked(string target, string expected) {
        Safe.NoLinks(target);
        // Keep writers out during the final comparison. Validate before requesting deletion.
        using (var stream = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
        using (var hash = SHA256.Create()) {
            if (Safe.Hex(hash.ComputeHash(stream)) != expected) throw new IOException("O destino mudou antes da remoção.");
            if (HashOrNull(target) != expected) throw new IOException("O destino mudou antes da remoção.");
            File.Delete(target);
        }
    }
    static void Backup(string source, string target, string expected) {
        Safe.NoLinks(source); Safe.NoLinks(target); Directory.CreateDirectory(Path.GetDirectoryName(target));
        File.Copy(source, target, false);
        if (Safe.Hash(target) != expected) throw new IOException("O arquivo mudou antes do backup.");
    }
    static void CopyBounded(Stream source, Stream target, long size) {
        byte[] buffer = new byte[524288]; long total = 0; int count;
        while ((count = source.Read(buffer, 0, buffer.Length)) > 0) { total += count; if (total > size) throw new IOException("Arquivo maior que o declarado."); target.Write(buffer, 0, count); }
        if (total != size) throw new IOException("Arquivo incompleto no pacote.");
    }
    void CreateParents(string relative, List<string> created) {
        for (int slash = relative.IndexOf('/'); slash >= 0; slash = relative.IndexOf('/', slash + 1)) {
            string path = Safe.In(destination, relative.Substring(0, slash));
            if (!Directory.Exists(path)) { Directory.CreateDirectory(path); created.Add(path); }
        }
    }
    static string HashOrNull(string path) { Safe.NoLinks(path); return File.Exists(path) ? Safe.Hash(path) : null; }
    static string Fingerprint(string value) { using (var sha = SHA256.Create()) return Safe.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Substring(0, 32); }
    static void Report(Action<string> report, string message) { if (report != null) report(message); }
    static IOException Conflict(string path) { return new IOException("O destino já contém outro arquivo ou uma pasta: " + path + ". O conteúdo existente foi preservado."); }
    static void TryCleanup(Action action) { try { action(); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    static void DeleteEmpty(string path) { Safe.NoLinks(path); if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path); }
    static void CleanStage(string stage) {
        TryCleanup(() => {
            Safe.NoLinks(stage);
            if (!Directory.Exists(stage)) return;
            foreach (string file in Directory.GetFiles(stage)) { Safe.NoLinks(file); File.Delete(file); }
            DeleteEmpty(stage);
        });
    }
}
