using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

public sealed class DownloadWatchMatch {
    public string Path;
    public FixedAsset Asset;
}

// Watches only the selected folder and the exact assets authorized by the caller.
public sealed class OnlineDownloadWatch {
    sealed class Observation { public long Size,Ticks;public DateTime Since;public bool Rejected; }
    readonly object gate=new object();
    readonly Dictionary<string,Observation> seen=new Dictionary<string,Observation>(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> delivered=new HashSet<string>(StringComparer.Ordinal);
    readonly Dictionary<string,string> traced=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> initialPaths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    bool initialCaptured;
    DateTime armedUtc;
    List<FixedAsset> expected=new List<FixedAsset>();
    int generation;
    const long MaxArchive=8L*1024*1024*1024;
    public string Folder {get;private set;}
    public bool IsArmed {get{lock(gate)return expected.Count>0;}}
    internal bool HasPendingCandidates {get{lock(gate)return seen.Values.Any(o=>!o.Rejected);}}
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern int SHGetKnownFolderPath(ref Guid id,uint flags,IntPtr token,out IntPtr path);
    public static string DefaultFolder() {
        Guid id=new Guid("374DE290-123F-4565-9164-39C4925E467B");IntPtr path=IntPtr.Zero;
        try{if(SHGetKnownFolderPath(ref id,0,IntPtr.Zero,out path)==0&&path!=IntPtr.Zero)return Marshal.PtrToStringUni(path);}
        finally{if(path!=IntPtr.Zero)Marshal.FreeCoTaskMem(path);}
        return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads");
    }

    public OnlineDownloadWatch(string folder) {
        if(String.IsNullOrWhiteSpace(folder)||folder.Any(Char.IsControl))throw new IOException("Escolha uma pasta local de downloads.");
        string root=System.IO.Path.GetFullPath(folder).TrimEnd('\\');
        if(root.Length<=3||root.StartsWith(@"\\",StringComparison.Ordinal))throw new IOException("Escolha uma pasta local comum para os downloads.");
        Safe.NoLinks(root);if(!Directory.Exists(root))throw new IOException("A pasta de downloads não existe. Escolha a pasta usada pelo navegador.");Folder=root;
    }
    public void Arm(IEnumerable<FixedAsset> assets) {ArmIdentities(assets,false);}
    // Download-only preparation: official SHA remains mandatory; the exact byte count is learned only after that SHA matches.
    internal void ArmByHash(IEnumerable<FixedAsset> assets) {ArmIdentities(assets,true);}
    void ArmIdentities(IEnumerable<FixedAsset> assets,bool allowUnmeasuredSize) {
        if(assets==null)throw new ArgumentNullException("assets");
        var list=assets.ToList();
        if(list.Count==0||list.Count>100||list.Any(a=>a==null||a.Size<0||a.Size==0&&!allowUnmeasuredSize||a.Size>MaxArchive||!FixedData.HashValid(a.Sha256)||a.Size==0&&(String.IsNullOrWhiteSpace(a.Name)||System.IO.Path.GetFileName(a.Name)!=a.Name||a.Name.Any(Char.IsControl))))throw new IOException("Lista de downloads esperados inválida.");
        if(list.GroupBy(a=>a.Sha256).Any(g=>g.Select(a=>a.Size).Distinct().Count()!=1))throw new IOException("O mesmo download tem tamanhos divergentes.");
        // Copy the identity so changes to a caller-owned object cannot widen an armed watch.
        list=list.GroupBy(a=>a.Sha256).Select(g=>Safe.Json.Deserialize<FixedAsset>(Safe.Json.Serialize(g.First()))).ToList();
        var initial=new List<string>();bool captured=false;
        if(OnlineDiagnostics.Enabled)try{Safe.NoLinks(Folder);initial=Directory.EnumerateFiles(Folder,"*",SearchOption.TopDirectoryOnly).Take(10001).ToList();captured=initial.Count<=10000;}catch(IOException){}catch(UnauthorizedAccessException){}
        lock(gate){expected=list;generation++;seen.Clear();delivered.Clear();traced.Clear();initialPaths.Clear();foreach(string path in initial)initialPaths.Add(path);initialCaptured=captured;armedUtc=DateTime.UtcNow;}
    }
    public void Cancel(){lock(gate){generation++;expected.Clear();seen.Clear();delivered.Clear();}}
    public DownloadWatchMatch Poll(){return Poll(DateTime.UtcNow,null);}
    public DownloadWatchMatch Poll(Action<long,long> progress){return Poll(DateTime.UtcNow,progress);}
    internal DownloadWatchMatch Poll(DateTime now){return Poll(now,null);}
    internal DownloadWatchMatch Poll(DateTime now,Action<long,long> progress) {
        List<FixedAsset> waiting;int token;
        lock(gate){if(expected.Count==0)return null;token=generation;waiting=expected.Where(a=>!delivered.Contains(a.Sha256)).ToList();}
        if(waiting.Count==0)return null;
        Safe.NoLinks(Folder);
        string[] paths=Directory.EnumerateFiles(Folder,"*",SearchOption.TopDirectoryOnly).Take(10001).ToArray();
        if(paths.Length>10000)throw new IOException("A pasta de downloads tem arquivos demais. Escolha uma pasta dedicada no navegador.");
        var present=new HashSet<string>(paths,StringComparer.OrdinalIgnoreCase);
        lock(gate){if(token!=generation)return null;foreach(string old in seen.Keys.Where(p=>!present.Contains(p)).ToList())seen.Remove(old);}
        foreach(string file in paths) {
            TraceCandidate(file,waiting);
            if(!new[]{".zip",".rar",".7z"}.Contains(System.IO.Path.GetExtension(file).ToLowerInvariant()))continue;
            try {
                string path=System.IO.Path.GetFullPath(file);
                if(!String.Equals(System.IO.Path.GetDirectoryName(path),Folder,StringComparison.OrdinalIgnoreCase))continue;
                Safe.NoLinks(path);var info=new FileInfo(path);
                if((info.Attributes&FileAttributes.ReparsePoint)!=0||info.Length<=0||info.Length>MaxArchive||!waiting.Any(a=>a.Size==info.Length||a.Size==0&&CandidateName(a,path)))continue;
                Observation observation;
                lock(gate){
                    if(token!=generation)return null;
                    if(!seen.TryGetValue(path,out observation)||observation.Size!=info.Length||observation.Ticks!=info.LastWriteTimeUtc.Ticks){seen[path]=new Observation{Size=info.Length,Ticks=info.LastWriteTimeUtc.Ticks,Since=now};continue;}
                    if(observation.Rejected||now-observation.Since<TimeSpan.FromSeconds(2))continue;
                }
                string hash;
                using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.None)) {
                    if(stream.Length!=observation.Size)continue;
                    using(var sha=SHA256.Create()) {
                        byte[] buffer=new byte[1024*1024];long total=0;int read;DateTime notified=DateTime.MinValue;
                        while((read=stream.Read(buffer,0,buffer.Length))>0){
                            lock(gate){if(token!=generation)return null;}
                            sha.TransformBlock(buffer,0,read,buffer,0);total+=read;
                            if(progress!=null&&(DateTime.UtcNow-notified>=TimeSpan.FromMilliseconds(250)||total==stream.Length)){progress(total,stream.Length);notified=DateTime.UtcNow;}
                        }
                        sha.TransformFinalBlock(new byte[0],0,0);hash=Safe.Hex(sha.Hash);
                    }
                }
                lock(gate){
                    if(token!=generation)return null;
                    var match=waiting.FirstOrDefault(a=>(a.Size==observation.Size||a.Size==0&&CandidateName(a,path))&&a.Sha256==hash);
                    if(match!=null){if(delivered.Add(match.Sha256)){var verified=Safe.Json.Deserialize<FixedAsset>(Safe.Json.Serialize(match));verified.Size=observation.Size;OnlineDiagnostics.Record("watch.verified",match.Id,"Arquivo completo, estável e conferido por SHA-256.",new{Source=path,Size=observation.Size,Sha256=hash,WrittenUtc=new DateTime(observation.Ticks,DateTimeKind.Utc).ToString("o"),CreatedUtc=info.CreationTimeUtc.ToString("o"),WatchStartedUtc=armedUtc.ToString("o"),ExistedBeforeWatch=initialCaptured?(bool?)initialPaths.Contains(path):null});return new DownloadWatchMatch{Path=path,Asset=verified};}continue;}
                    observation.Rejected=true;
                }
            }catch(IOException){}catch(UnauthorizedAccessException){}
        }
        return null;
    }
    static bool CandidateName(FixedAsset asset,string path){
        string name=System.IO.Path.GetFileName(path),extension=System.IO.Path.GetExtension(asset.Name);
        if(new[]{".zip",".rar",".7z"}.Contains(extension.ToLowerInvariant()))return Regex.IsMatch(name,"^"+Regex.Escape(System.IO.Path.GetFileNameWithoutExtension(asset.Name))+@"(?: \([0-9]+\))?"+Regex.Escape(extension)+"$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        return name.StartsWith(asset.Name,StringComparison.OrdinalIgnoreCase)&&Regex.IsMatch(name,@"(?:^|[ -])"+asset.ModId+@"(?:[ -]|$)",RegexOptions.CultureInvariant);
    }
    void TraceCandidate(string path,List<FixedAsset> waiting){
        if(!OnlineDiagnostics.Enabled)return;
        try{
            string name=System.IO.Path.GetFileName(path);
            if(!waiting.Any(a=>name.StartsWith(a.Name,StringComparison.OrdinalIgnoreCase)||a.ModId>0&&name.IndexOf("-"+a.ModId+"-",StringComparison.OrdinalIgnoreCase)>=0))return;
            Safe.NoLinks(path);var info=new FileInfo(path);string fingerprint=info.Length+"/"+info.LastWriteTimeUtc.Ticks;
            lock(gate){string old;if(traced.TryGetValue(path,out old)&&old==fingerprint)return;traced[path]=fingerprint;}
            OnlineDiagnostics.Record("watch.file.observed",null,"Arquivo do download observado sem ler conteúdo parcial.",new{Source=path,Size=info.Length,WrittenUtc=info.LastWriteTimeUtc.ToString("o"),CreatedUtc=info.CreationTimeUtc.ToString("o"),Partial=name.EndsWith(".crdownload",StringComparison.OrdinalIgnoreCase)||name.EndsWith(".part",StringComparison.OrdinalIgnoreCase)||name.EndsWith(".tmp",StringComparison.OrdinalIgnoreCase),Expected=waiting.Select(a=>a.Id).ToArray()});
        }catch(IOException){}catch(UnauthorizedAccessException){}
    }
}
