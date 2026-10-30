using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// A download-only continuation over the existing strict monitor/cache; it has no installer or game path.
public sealed class FacialDownloadQueue {
    readonly object gate=new object();
    readonly OnlineDownloadWatch watch;
    readonly DownloadCache cache;
    readonly Action<DownloadWatchMatch> registered;
    readonly List<FixedAsset> verified=new List<FixedAsset>();
    List<FixedAsset> assets=new List<FixedAsset>();
    Action<FixedAsset> open;
    int index,generation;
    bool needsOpen;
    public DateTime CurrentStartedUtc {get;private set;}
    public FacialDownloadQueue(string folder,DownloadCache store,Action<DownloadWatchMatch> record=null){if(store==null)throw new ArgumentNullException("store");watch=new OnlineDownloadWatch(folder);cache=store;registered=record;}
    public bool IsArmed{get{lock(gate)return index<assets.Count;}}
    public int CompletedCount{get{lock(gate)return verified.Count;}}
    public FixedAsset CurrentAsset{get{lock(gate)return index<assets.Count?Clone(assets[index]):null;}}
    public List<FixedAsset> VerifiedAssets{get{lock(gate)return verified.Select(Clone).ToList();}}
    static FixedAsset Clone(FixedAsset a){return Safe.Json.Deserialize<FixedAsset>(Safe.Json.Serialize(a));}
    public void Start(IEnumerable<FixedAsset> source,Action<FixedAsset> launch){
        if(source==null||launch==null)throw new ArgumentNullException();var list=source.Select(Clone).ToList();
        // Validate all identities before opening the first page, including missing/duplicate hashes.
        watch.ArmByHash(list);watch.Cancel();
        if(list.Count!=list.Select(a=>a.Id).Distinct().Count()||list.Count!=list.Select(a=>a.Sha256).Distinct().Count())throw new IOException("A fila contém downloads duplicados.");
        foreach(var a in list){OnlineWindow.BrowserRequest(a.Url);if(a.ModId<=0||a.FileId<=0||a.Url!="https://www.nexusmods.com/cyberpunk2077/mods/"+a.ModId+"?tab=files&file_id="+a.FileId)throw new IOException("Fonte facial precisa do arquivo oficial fixado.");}
        lock(gate){generation++;assets=list;index=0;verified.Clear();open=launch;needsOpen=true;}
        Advance(DateTime.UtcNow);
    }
    public void Cancel(){lock(gate){generation++;assets.Clear();index=0;needsOpen=false;watch.Cancel();}OnlineDiagnostics.Record("face-queue.cancelled",null,"Fila somente de downloads encerrada; nenhuma instalação autorizada.");}
    public void Resume(){lock(gate){if(index>=assets.Count||!needsOpen||watch.HasPendingCandidates)return;}Advance(DateTime.UtcNow);}
    void Advance(DateTime now){
        lock(gate){
            while(index<assets.Count){var a=assets[index];string file=cache.PathFor(a);Safe.NoLinks(file);
                if(File.Exists(file)){
                    var candidate=Clone(a);candidate.Size=new FileInfo(file).Length;
                    if(candidate.Size>0&&candidate.Size<=8L*1024*1024*1024&&(a.Size==0||candidate.Size==a.Size)&&cache.Contains(candidate)){if(registered!=null)registered(new DownloadWatchMatch{Path=file,Asset=candidate});verified.Add(candidate);index++;continue;}
                }
                watch.ArmByHash(new[]{a});watch.Poll(now,null);CurrentStartedUtc=DateTime.UtcNow;needsOpen=true;break;
            }
            if(index>=assets.Count){watch.Cancel();needsOpen=false;return;}
        }
        if(!watch.HasPendingCandidates)OpenCurrent();
    }
    void OpenCurrent(){
        FixedAsset a;int token;
        lock(gate){if(index>=assets.Count||!needsOpen)return;a=Clone(assets[index]);token=generation;}
        try{open(a);lock(gate){if(token==generation)needsOpen=false;}}
        catch{lock(gate){if(token==generation)needsOpen=true;}throw;}
    }
    public DownloadWatchMatch Poll(Action<long,long> progress){return Poll(DateTime.UtcNow,progress);}
    internal DownloadWatchMatch Poll(DateTime now,Action<long,long> progress){
        int token;lock(gate){if(index>=assets.Count)return null;token=generation;}
        var match=watch.Poll(now,progress);if(match==null){if(!watch.HasPendingCandidates)OpenCurrent();return null;}
        lock(gate){
            if(token!=generation||index>=assets.Count||assets[index].Sha256!=match.Asset.Sha256)return null;
            try{cache.Import(match.Path,match.Asset);if(registered!=null)registered(match);}
            catch{watch.ArmByHash(new[]{assets[index]});throw;}
            verified.Add(Clone(match.Asset));index++;needsOpen=true;
            OnlineDiagnostics.Record("face-queue.imported",match.Asset.Id,"Pacote conferido e importado; avançando somente downloads.",new{Source=match.Path,Sha256=match.Asset.Sha256,Size=match.Asset.Size,Install=false});
        }
        Advance(now);return match;
    }
}
