using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public sealed partial class DownloadCache {
    readonly object sourceGate=new object();
    readonly Func<string> downloads;
    readonly string retainedRoot;
    internal string RetainedRoot {get{return SourcesOnly?retainedRoot:Root;}}
    internal bool SourcesOnly {get{return downloads!=null;}}
    public DownloadCache(string root,Func<string> downloads,string retainedRoot=null):this(root){if(downloads==null)throw new ArgumentNullException("downloads");this.downloads=downloads;this.retainedRoot=retainedRoot==null?null:Path.GetFullPath(retainedRoot);}
    string IndexFile {get{return Safe.In(Root,"sources.json");}}
    internal static IEnumerable<string> SourcePaths(string root){string file=Safe.In(root,"sources.json");if(!File.Exists(file))return new string[0];var map=Safe.Json.Deserialize<Dictionary<string,string>>(GeneralProjectStorage.ReadText(file,2097152));if(map==null||map.Count>2048)throw new IOException("Índice de downloads inválido.");return map.Values.Where(path=>!String.IsNullOrWhiteSpace(path)&&Path.IsPathRooted(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();}
    Dictionary<string,string> ReadSources(){
        if(!File.Exists(IndexFile))return new Dictionary<string,string>(StringComparer.Ordinal);
        var map=Safe.Json.Deserialize<Dictionary<string,string>>(GeneralProjectStorage.ReadText(IndexFile,2097152));
        if(map==null||map.Count>2048||map.Any(pair=>!FixedData.HashValid(pair.Key)||String.IsNullOrWhiteSpace(pair.Value)||!Path.IsPathRooted(pair.Value)))throw new IOException("Índice de downloads inválido.");
        return map;
    }
    internal static void DuplicateSources(string source,string target){
        string file=Safe.In(source,"sources.json");if(!File.Exists(file))return;
        var map=Safe.Json.Deserialize<Dictionary<string,string>>(GeneralProjectStorage.ReadText(file,2097152));
        if(map==null||map.Count>2048||map.Any(pair=>!FixedData.HashValid(pair.Key)||String.IsNullOrWhiteSpace(pair.Value)||!Path.IsPathRooted(pair.Value)))throw new IOException("Índice de downloads inválido.");
        Safe.WriteJson(Safe.In(target,"sources.json"),map);
    }
    string OriginalFor(FixedAsset asset){lock(sourceGate){string path;return ReadSources().TryGetValue(asset.Sha256,out path)?path:Safe.In(Root,asset.Sha256+".missing");}}
    internal void Remember(FixedAsset asset,string source){
        source=Path.GetFullPath(source);Safe.NoLinks(source);
        if(source.StartsWith(Root.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("O pacote original deve ficar fora dos dados do gerenciador.");
        lock(sourceGate){var map=ReadSources();map[asset.Sha256]=source;Safe.WriteJson(IndexFile,map);}
    }
    internal string DownloadTemporaryPath(){
        string folder=SourcesOnly?Path.GetFullPath(downloads()):Root;Safe.NoLinks(folder);Directory.CreateDirectory(folder);
        return Safe.In(folder,"ModLoader-"+Guid.NewGuid().ToString("N")+".part");
    }
    internal void KeepOriginalDownload(FixedAsset asset,string temp){
        string folder=Path.GetFullPath(downloads());Safe.NoLinks(folder);Directory.CreateDirectory(folder);
        string ext=asset.Format=="raw"?".bin":asset.Format=="tar"?".tar":".zip";
        string target=Safe.In(folder,"ModLoader-"+asset.Sha256.Substring(0,16)+ext);Safe.NoLinks(target);
        if(File.Exists(target)){
            if(new FileInfo(target).Length==asset.Size&&Safe.Hash(target)==asset.Sha256){Remember(asset,target);return;}
            target=Safe.In(folder,"ModLoader-"+Guid.NewGuid().ToString("N")+ext);
        }
        File.Move(temp,target);Remember(asset,target);
    }
}
