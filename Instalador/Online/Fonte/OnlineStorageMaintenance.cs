using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

internal static class StorageMaintenance {
    internal static Func<bool> OtherLoaderProbe;
    internal static bool AnotherLoaderRunning() {
        if(OtherLoaderProbe!=null)return OtherLoaderProbe();
        int current=Process.GetCurrentProcess().Id;
        foreach(var process in Process.GetProcesses())using(process){
            try{if(process.Id!=current&&SameEdition(process.ProcessName,GeneralAppPaths.IsDevelopment))return true;}
            catch(InvalidOperationException){}
            catch(Win32Exception){}
            catch(NotSupportedException){}
        }
        return false;
    }
    internal static bool SameEdition(string processName,bool development) {
        if(development)return processName.Equals("LoaderDev",StringComparison.OrdinalIgnoreCase);
        return processName.StartsWith("ModLoader",StringComparison.OrdinalIgnoreCase)||
            processName.StartsWith("Mod Loader",StringComparison.OrdinalIgnoreCase)||
            processName.StartsWith("Cyberpunk Coop",StringComparison.OrdinalIgnoreCase);
    }
    internal static void RotateLogs(string folder,int maxFiles=8,long maxBytes=64L*1024*1024) {
        if(AnotherLoaderRunning())return;
        long keptBytes=0;int kept=0;
        foreach(var file in new DirectoryInfo(folder).GetFiles("diagnostico-*.jsonl").Where(f=>Regex.IsMatch(f.Name,"^diagnostico-[0-9]{8}-[0-9]{6}-[a-f0-9]{8}\\.jsonl$")).OrderByDescending(f=>f.LastWriteTimeUtc)){
            if(kept<maxFiles&&keptBytes+file.Length<=maxBytes){kept++;keptBytes+=file.Length;continue;}
            try{Safe.NoLinks(file.FullName);using(var guard=new FileStream(file.FullName,FileMode.Open,FileAccess.Read,FileShare.Delete))File.Delete(file.FullName);}
            catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
}

public sealed partial class DownloadCache {
    public IDisposable HoldOperation() {
        Safe.NoLinks(Root);Directory.CreateDirectory(Root);string locks=Safe.In(Root,"locks");Directory.CreateDirectory(locks);
        string file=Safe.In(locks,"operations.lock");Safe.NoLinks(file);
        return new FileStream(file,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.ReadWrite);
    }
    static FileStream ExclusiveMaintenance(string root) {
        Safe.NoLinks(root);string locks=Safe.In(root,"locks");Directory.CreateDirectory(locks);
        string file=Safe.In(locks,"operations.lock");Safe.NoLinks(file);
        return new FileStream(file,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
    }
    public IDisposable HoldPreparation(string stage) {
        string full=Path.GetFullPath(stage),parent=Safe.In(Root,"preparacao");
        if(!String.Equals(Path.GetDirectoryName(full),parent,StringComparison.OrdinalIgnoreCase)||!Regex.IsMatch(Path.GetFileName(full),"^[a-f0-9]{32}$"))throw new IOException("Preparação inválida.");
        IDisposable operation=HoldOperation();
        try{Safe.NoLinks(full);Directory.CreateDirectory(full);string file=Safe.In(full,".active");Safe.NoLinks(file);return new PreparationLease(operation,new FileStream(file,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None));}
        catch{operation.Dispose();throw;}
    }
    sealed class PreparationLease : IDisposable {
        readonly IDisposable operation,stage;
        internal PreparationLease(IDisposable operation,IDisposable stage){this.operation=operation;this.stage=stage;}
        public void Dispose(){stage.Dispose();operation.Dispose();}
    }
    public int CleanOrphanPreparations() {
        string parent=Safe.In(Root,"preparacao");if(!Directory.Exists(parent)||StorageMaintenance.AnotherLoaderRunning())return 0;
        int count=0;
        try{using(ExclusiveMaintenance(Root))foreach(string stage in Directory.GetDirectories(parent)){
            if(!Regex.IsMatch(Path.GetFileName(stage),"^[a-f0-9]{32}$")||Directory.GetLastWriteTimeUtc(stage)>DateTime.UtcNow.AddHours(-24))continue;
            try{Safe.NoLinks(stage);string file=Safe.In(stage,".active");Safe.NoLinks(file);using(var guard=new FileStream(file,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.Delete))if(CleanPreparation(stage))count++;}
            catch(IOException){}catch(UnauthorizedAccessException){}
        }}catch(IOException){}catch(UnauthorizedAccessException){}
        return count;
    }
    internal static IEnumerable<string> RetainedDownloadFiles(string root) {
        if (root==null) return Enumerable.Empty<string>();
        Safe.NoLinks(root);
        if (!Directory.Exists(root)) return Enumerable.Empty<string>();
        return Directory.EnumerateFiles(root,"*.cache")
            .Where(file=>Regex.IsMatch(Path.GetFileName(file),@"\A[a-f0-9]{64}\.cache\z"));
    }

    internal sealed class CleanupPlan {
        readonly string root;
        readonly List<Copy> copies=new List<Copy>();
        internal int Count {get{return copies.Count;}}
        internal long Bytes {get;private set;}

        internal CleanupPlan(string root) {
            this.root=root;
            foreach(string path in RetainedDownloadFiles(root)){
                try{
                    Safe.NoLinks(path);
                    var file=new FileInfo(path);
                    var copy=new Copy{Path=path,Bytes=file.Length,Modified=file.LastWriteTimeUtc};
                    copies.Add(copy);
                    Bytes+=copy.Bytes;
                }catch(IOException){}catch(UnauthorizedAccessException){}
            }
        }

        internal long Apply(out int skipped) {
            skipped=Count;
            if(Count==0||!Directory.Exists(root))return 0;
            if(StorageMaintenance.AnotherLoaderRunning())throw new IOException(GeneralStyle.T(
                "Feche as outras janelas do Mod Loader antes de limpar os downloads.",
                "Close other Mod Loader windows before clearing old downloads."));

            long removed=0;
            using(ExclusiveMaintenance(root))foreach(var copy in copies){
                try{
                    Safe.NoLinks(copy.Path);
                    using(var guard=new FileStream(copy.Path,FileMode.Open,FileAccess.Read,FileShare.Delete)){
                        if(guard.Length!=copy.Bytes||File.GetLastWriteTimeUtc(copy.Path)!=copy.Modified)continue;
                        File.Delete(copy.Path);
                    }
                    removed+=copy.Bytes;
                    skipped--;
                }catch(IOException){}catch(UnauthorizedAccessException){}
            }
            return removed;
        }

        sealed class Copy {
            internal string Path;
            internal long Bytes;
            internal DateTime Modified;
        }
    }

    internal CleanupPlan PreviewRetainedDownloads(){return new CleanupPlan(RetainedRoot);}

    public long ClearDownloads(){
        int skipped;
        return new CleanupPlan(Root).Apply(out skipped);
    }
}
