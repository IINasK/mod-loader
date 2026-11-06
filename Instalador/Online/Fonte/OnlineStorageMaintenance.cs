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
            try{if(process.Id!=current&&(process.ProcessName.StartsWith("ModLoader",StringComparison.OrdinalIgnoreCase)||process.ProcessName.StartsWith("Mod Loader",StringComparison.OrdinalIgnoreCase)||process.ProcessName.StartsWith("Cyberpunk Coop",StringComparison.OrdinalIgnoreCase)))return true;}
            catch(InvalidOperationException){}
            catch(Win32Exception){}
            catch(NotSupportedException){}
        }
        return false;
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
    FileStream ExclusiveMaintenance() {
        Safe.NoLinks(Root);string locks=Safe.In(Root,"locks");Directory.CreateDirectory(locks);
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
        try{using(ExclusiveMaintenance())foreach(string stage in Directory.GetDirectories(parent)){
            if(!Regex.IsMatch(Path.GetFileName(stage),"^[a-f0-9]{32}$")||Directory.GetLastWriteTimeUtc(stage)>DateTime.UtcNow.AddHours(-24))continue;
            try{Safe.NoLinks(stage);string file=Safe.In(stage,".active");Safe.NoLinks(file);using(var guard=new FileStream(file,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.Delete))if(CleanPreparation(stage))count++;}
            catch(IOException){}catch(UnauthorizedAccessException){}
        }}catch(IOException){}catch(UnauthorizedAccessException){}
        return count;
    }
    public long ClearDownloads() {
        if(!Directory.Exists(Root))return 0;
        if(StorageMaintenance.AnotherLoaderRunning())throw new IOException("Feche as outras janelas do Mod Loader antes de limpar os downloads.");
        long removed=0;
        using(ExclusiveMaintenance())foreach(string file in Directory.GetFiles(Root,"*.cache")){
            if(!Regex.IsMatch(Path.GetFileName(file),"^[a-f0-9]{64}\\.cache$"))continue;
            try{Safe.NoLinks(file);long size=new FileInfo(file).Length;using(var guard=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Delete))File.Delete(file);removed+=size;}
            catch(IOException){}catch(UnauthorizedAccessException){}
        }
        return removed;
    }
}
