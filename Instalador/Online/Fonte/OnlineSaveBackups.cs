using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

public sealed partial class FixedInstaller {
    string BackupRoot { get { return Safe.In(Data,"backups"); } }
    string LatestSaves { get { return Safe.In(BackupRoot,"saves-current"); } }

    // Called while the game operation lock is held. Promote only a fully verified copy.
    void BackupSaves() {
        if(String.IsNullOrEmpty(saves)||!Directory.Exists(saves))return;
        CheckBackupBoundary();
        var source=SnapshotFiles(saves);
        if(source.Count==0)return;
        string latest=LatestSaves, pending=Safe.In(BackupRoot,"saves-pending"), previous=Safe.In(BackupRoot,"saves-previous");
        if(!Directory.Exists(latest)&&Directory.Exists(previous)){
            Safe.NoLinks(previous);Safe.NoLinks(latest);
            Directory.Move(previous,latest);
        }
        DeleteBackupTree(previous);
        DeleteBackupTree(pending);
        var existing=SnapshotFiles(latest);
        if(source.Count==existing.Count&&source.All(pair=>{
            FileInfo old;
            return existing.TryGetValue(pair.Key,out old)&&pair.Value.Length==old.Length&&
                Safe.Hash(pair.Value.FullName)==Safe.Hash(old.FullName);
        }))return;

        try{
            foreach(var pair in source){
                string target=Safe.In(pending,pair.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                Safe.NoLinks(target);
                File.Copy(pair.Value.FullName,target,false);
                if(Safe.Hash(pair.Value.FullName)!=Safe.Hash(target))throw new IOException("Backup de saves não conferiu.");
            }
            if(Directory.Exists(latest))Directory.Move(latest,previous);
            try{Directory.Move(pending,latest);}
            catch{
                if(!Directory.Exists(latest)&&Directory.Exists(previous))Directory.Move(previous,latest);
                throw;
            }
            DeleteBackupTree(previous);
        }finally{DeleteBackupTree(pending);}
    }

    void CheckBackupBoundary(){
        if(String.IsNullOrWhiteSpace(saves))return;
        string source=Path.GetFullPath(saves).TrimEnd('\\')+"\\";
        string managed=Path.GetFullPath(BackupRoot).TrimEnd('\\')+"\\";
        if(source.StartsWith(managed,StringComparison.OrdinalIgnoreCase)||managed.StartsWith(source,StringComparison.OrdinalIgnoreCase))
            throw new IOException("A pasta de saves originais coincide com a área de backups.");
        Safe.NoLinks(saves);
    }

    static Dictionary<string,FileInfo> SnapshotFiles(string root){
        var files=new Dictionary<string,FileInfo>(StringComparer.OrdinalIgnoreCase);
        foreach(string path in BackupEntries(root).Where(path=>File.Exists(path)))
            files.Add(path.Substring(root.TrimEnd('\\').Length+1).Replace('\\','/'),new FileInfo(path));
        return files;
    }

    // Post-order traversal. No recursive deletion or following junctions.
    static IEnumerable<string> BackupEntries(string root){
        Safe.NoLinks(root);
        if(!Directory.Exists(root))yield break;
        foreach(string child in Directory.EnumerateDirectories(root))
            foreach(string path in BackupEntries(child))yield return path;
        foreach(string path in Directory.EnumerateFiles(root)){Safe.NoLinks(path);yield return path;}
        yield return root;
    }

    static void DeleteBackupTree(string root){
        foreach(string path in BackupEntries(root).ToArray()){
            Safe.NoLinks(path);
            if(Directory.Exists(path))Directory.Delete(path);
            else File.Delete(path);
        }
    }

    static readonly Regex LegacySaveSlot=new Regex(
        @"^(?:Auto|Manual|Quick|PointOfNoReturn|EndGame|Forced|Checkpoint)Save(?:-\d+)?$",
        RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    static readonly HashSet<string> LegacySaveRootFiles=new HashSet<string>(
        new[]{"EnabledMods.json","HDRUserSettings.json","steam_autocloud.vdf","user.gls"},StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> LegacySaveSlotFiles=new HashSet<string>(
        new[]{"sav.dat","metadata.9.json","screenshot.png"},StringComparer.OrdinalIgnoreCase);

    static bool HasLegacySaveLayout(string root,Dictionary<string,FileInfo> files){
        // Without a readable receipt, only accept the on-disk layout of complete Cyberpunk save slots.
        var slots=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(string path in BackupEntries(root).Where(path=>Directory.Exists(path)&&path!=root)){
            string slot=Path.GetFileName(path);
            if(!String.Equals(Path.GetDirectoryName(path),root,StringComparison.OrdinalIgnoreCase)||!LegacySaveSlot.IsMatch(slot)
                ||!files.ContainsKey(slot+"/sav.dat")||!files.ContainsKey(slot+"/metadata.9.json"))return false;
            slots.Add(slot);
        }
        return slots.Count>0&&files.Keys.All(relative=>{
            int slash=relative.IndexOf('/');
            return slash<0?LegacySaveRootFiles.Contains(relative)
                :slots.Contains(relative.Substring(0,slash))&&LegacySaveSlotFiles.Contains(relative.Substring(slash+1));
        });
    }

    internal sealed class SaveBackupCleanup {
        readonly FixedInstaller owner;
        readonly Dictionary<string,FileInfo> files;
        internal int Count { get; private set; }
        internal long Bytes { get; private set; }
        internal SaveBackupCleanup(FixedInstaller owner,IEnumerable<Dictionary<string,FileInfo>> snapshots){
            this.owner=owner;
            files=new Dictionary<string,FileInfo>(StringComparer.OrdinalIgnoreCase);
            foreach(var snapshot in snapshots){
                Count++;
                foreach(var file in snapshot.Values){
                    Bytes+=file.Length;
                    files.Add(file.FullName,file);
                }
            }
        }
        internal long Apply(out int skipped){
            skipped=0;
            if(Count==0)return 0;
            owner.Closed();owner.CheckBackupBoundary();
            string mutex=Safe.In(owner.Data,"operacao.lock");
            Safe.NoLinks(mutex);
            using(new FileStream(mutex,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
                var eligible=owner.PreviewOldSaveBackups().files;
                long removed=0;
                foreach(var group in files.Values.GroupBy(file=>LegacyRoot(file.FullName,owner.BackupRoot),StringComparer.OrdinalIgnoreCase)){
                    try{
                        owner.Closed();
                        var entries=BackupEntries(group.Key).ToArray();
                        if(!new HashSet<string>(entries.Where(File.Exists),StringComparer.OrdinalIgnoreCase).SetEquals(group.Select(file=>file.FullName))){
                            skipped++;continue;
                        }
                        if(group.Any(file=>{
                            if(!eligible.ContainsKey(file.FullName))return true;
                            var now=new FileInfo(file.FullName);
                            return !now.Exists||now.Length!=file.Length||now.LastWriteTimeUtc!=file.LastWriteTimeUtc;
                        })){skipped++;continue;}
                        foreach(string path in entries){
                            Safe.NoLinks(path);
                            if(Directory.Exists(path))Directory.Delete(path);
                            else {File.Delete(path);removed+=files[path].Length;}
                        }
                    }catch(IOException){skipped++;}
                     catch(UnauthorizedAccessException){skipped++;}
                }
                return removed;
            }
        }
        static string LegacyRoot(string file,string root){
            string id=file.Substring(root.Length+1,32);
            return Safe.In(root,id+"/saves");
        }
    }

    internal SaveBackupCleanup PreviewOldSaveBackups(){
        Closed();CheckBackupBoundary();
        var snapshots=new List<Dictionary<string,FileInfo>>();
        if(!Directory.Exists(BackupRoot))return new SaveBackupCleanup(this,snapshots);
        Safe.NoLinks(BackupRoot);
        var receipts=new Dictionary<string,OnlineReceipt>(StringComparer.OrdinalIgnoreCase);
        foreach(string path in AllReceiptPaths()){
            string id=Path.GetFileNameWithoutExtension(path);
            if(!Regex.IsMatch(id,"^[a-f0-9]{32}$"))continue;
            try{
                var receipt=ReadReceiptEnvelope(path,true);
                if(receipt==null||receipt.Id!=id)continue;
                OnlineReceipt previous;
                if(!receipts.TryGetValue(id,out previous)||FinishedSaveBackup(previous))receipts[id]=receipt;
            }catch(IOException){}catch(UnauthorizedAccessException){}
        }

        var candidates=new List<Tuple<DateTime,Dictionary<string,FileInfo>>>();
        foreach(string folder in Directory.EnumerateDirectories(BackupRoot)){
            string id=Path.GetFileName(folder);
            if(!Regex.IsMatch(id,"^[a-f0-9]{32}$"))continue;
            OnlineReceipt receipt;
            bool recorded=receipts.TryGetValue(id,out receipt);
            if(recorded&&!FinishedSaveBackup(receipt))continue;
            string root=Safe.In(BackupRoot,id+"/saves");
            try{
                var files=SnapshotFiles(root);
                if(files.Count==0)continue;
                if((!recorded||receipt.CatalogId!=catalog.Id)&&!HasLegacySaveLayout(root,files))continue;
                candidates.Add(Tuple.Create(Directory.GetLastWriteTimeUtc(root),files));
            }catch(IOException){}catch(UnauthorizedAccessException){}
        }
        DateTime newest=candidates.Select(item=>item.Item1).DefaultIfEmpty(DateTime.MinValue).Max();
        bool keepNewest=!SnapshotFiles(LatestSaves).Any()||Directory.GetLastWriteTimeUtc(LatestSaves)<=newest;
        snapshots.AddRange(candidates.Where(item=>!keepNewest||item.Item1<newest).Select(item=>item.Item2));
        return new SaveBackupCleanup(this,snapshots);
    }

    static bool FinishedSaveBackup(OnlineReceipt receipt){
        return receipt.Status=="instalado"||receipt.Status=="desativado"||
            receipt.Status=="desfeito"||receipt.Status=="desfeito_com_preservados";
    }
}
