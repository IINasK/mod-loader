using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Linq;
using System.Text;

internal sealed class InventoryLinkGroup {
    public string Id,Name,Source,CatalogId;
    public List<string> Paths=new List<string>();
}

internal sealed class InventoryZipLabel {
    public string Name, Hash, Archive;
}

internal sealed class InventoryLinkData {
    public Dictionary<string,InventoryZipLabel> ZipLabels=new Dictionary<string,InventoryZipLabel>(StringComparer.OrdinalIgnoreCase);
    public List<InventoryLinkGroup> Groups=new List<InventoryLinkGroup>();
    public List<string> Ignored=new List<string>();
}

internal sealed class InventoryLinkStore {
    static readonly StringComparer Paths=StringComparer.OrdinalIgnoreCase;
    readonly string path;
    internal InventoryLinkStore(string data,string root) {
        string id=InstalledSnapshot.Hash(Encoding.UTF8.GetBytes(Path.GetFullPath(root).TrimEnd('\\').ToUpperInvariant()));
        path=Safe.In(data,"InventoryLinks/"+id+".json");
    }

    internal InventoryLinkData Load() {
        if(!File.Exists(path))return new InventoryLinkData();
        if(new FileInfo(path).Length>2000000)throw new IOException("Arquivo de agrupamentos locais muito grande.");
        var links=Safe.Json.Deserialize<InventoryLinkData>(File.ReadAllText(path));
        Validate(links);
        return links;
    }

    internal void Save(InventoryLinkData links) {
        Validate(links);
        if(Encoding.UTF8.GetByteCount(Safe.Json.Serialize(links))>2000000)
            throw new IOException("Arquivo de agrupamentos locais muito grande.");
        Safe.WriteJson(path,links);
    }

    static void Validate(InventoryLinkData links) {
        if(links==null||links.Groups==null||links.Ignored==null||links.Groups.Count>1000||links.Ignored.Count>1000||
            links.Ignored.Any(key=>String.IsNullOrWhiteSpace(key)||key.Length>250))throw Invalid();
        if(links.ZipLabels==null||links.ZipLabels.Count>5000)throw Invalid();
        var labelPaths=new HashSet<string>(Paths);
        foreach(var entry in links.ZipLabels){
            var label=entry.Value;
            if(entry.Key.Length>300||!labelPaths.Add(entry.Key)||!InstalledSnapshot.SafeGenericPath(entry.Key)||
                label==null||String.IsNullOrWhiteSpace(label.Name)||label.Name.Length>300||
                label.Name.Any(Char.IsControl)||!FixedData.HashValid(label.Hash)||
                String.IsNullOrWhiteSpace(label.Archive)||label.Archive.Length>255||label.Archive.Any(Char.IsControl))throw Invalid();
        }
        var ids=new HashSet<string>(StringComparer.Ordinal);
        var owned=new HashSet<string>(Paths);
        foreach(var group in links.Groups) {
            Guid id;
            if(group==null||!Guid.TryParseExact(group.Id,"N",out id)||!ids.Add(group.Id)||
                String.IsNullOrWhiteSpace(group.Name)||group.Name.Length>300||group.Paths==null||group.Paths.Count==0||
                group.Source!=null&&GeneralNexusLink.Parse(group.Source)==null)throw Invalid();
            foreach(string file in group.Paths){
                if(String.IsNullOrWhiteSpace(file)||file.Length>300||!owned.Add(file)||owned.Count>5000)throw Invalid();
                Safe.In(Path.GetTempPath(),file);
            }
        }
    }

    internal static Dictionary<string,InventoryZipLabel> MatchZip(string root,string zipPath,string name,bool cyberpunk,
        IEnumerable<string> candidates) {
        var remaining=new HashSet<string>(candidates,Paths);
        var matches=new Dictionary<string,InventoryZipLabel>(Paths);
        string archiveName=Path.GetFileName(zipPath);
        Safe.NoLinks(zipPath);
        if(new FileInfo(zipPath).Length>InstalledSnapshot.MaxTotalBytes)throw Invalid();
        using(var archive=ZipFile.OpenRead(zipPath))
        using(var hash=SHA256.Create()){
            if(archive.Entries.Count>InstalledSnapshot.MaxFiles)throw Invalid();
            var entries=archive.Entries.Where(entry=>entry.Name.Length>0).ToDictionary(
                entry=>entry.FullName.Replace('\\','/'),Paths);
            var preview=new NexusFilePreview{Files=entries.ToDictionary(item=>item.Key,item=>(long?)item.Value.Length,Paths)};
            var destinations=new Dictionary<string,string>(Paths);
            if(NexusManualScan.Destinations(new GeneralModVariant(),new[]{preview},cyberpunk,destinations)==null)
                throw new IOException(GeneralStyle.T("O ZIP não tem uma estrutura de instalação reconhecida.",
                    "The ZIP does not have a recognized installation layout."));
            foreach(var pair in destinations){
                var entry=entries[pair.Key];
                if(!remaining.Contains(pair.Value)||entry.Length>InstalledSnapshot.MaxFileBytes)continue;
                string target=GeneralInstallRoutes.Resolve(root,pair.Value);
                Safe.NoLinks(target);
                if(!File.Exists(target)||new FileInfo(target).Length!=entry.Length)continue;
                string expected;
                using(var stream=entry.Open())expected=Safe.Hex(hash.ComputeHash(stream));
                if(Safe.Hash(target)==expected)matches.Add(pair.Value,new InventoryZipLabel{Name=name,Hash=expected,Archive=archiveName});
            }
        }
        return matches;
    }

    static IOException Invalid(){return new IOException("Agrupamentos locais inválidos.");}

    internal static string Key(P2PSyncDifference file){return file.InventoryGroupId??(file.ProvenOwner==null?"raw:"+(file.Mod??file.Path):"owner:"+file.ProvenOwner);}

    internal static IEnumerable<P2PSyncDifference> View(IEnumerable<P2PSyncDifference> inventory,InventoryLinkData links,
        IEnumerable<GeneralModEntry> entries) {
        var mods=entries.ToDictionary(mod=>mod.Id,StringComparer.Ordinal);
        var adopted=new HashSet<string>(mods.Values.SelectMany(mod=>mod.DetectedPaths),Paths);
        var byPath=links.Groups.Where(group=>group.CatalogId==null||mods.ContainsKey(group.CatalogId))
            .SelectMany(group=>group.Paths.Select(file=>new{Path=file,Group=group}))
            .ToDictionary(item=>item.Path,item=>item.Group,Paths);
        foreach(var file in inventory){
            if(file.Kind!="found"||file.VerifiedZip!=null){yield return file;continue;}
            if(adopted.Contains(file.Path))continue;
            InventoryLinkGroup group;
            if(!byPath.TryGetValue(file.Path,out group)){yield return file;continue;}
            yield return new P2PSyncDifference{Kind=group.CatalogId==null?"found":"associated",Path=file.Path,
                Mod=group.CatalogId==null?group.Name:mods[group.CatalogId].Name,ModId=group.CatalogId,InventoryGroupId=group.Id};
        }
    }

    internal static InventoryLinkGroup Combine(InventoryLinkData links,string targetId,string targetName,string catalogId,
        IEnumerable<string> paths,string source=null) {
        var files=new HashSet<string>(paths,Paths);
        var target=links.Groups.FirstOrDefault(group=>group.Id==targetId||
            catalogId!=null&&group.CatalogId==catalogId);
        if(target!=null)files.UnionWith(target.Paths);
        var groupId=target==null?Guid.NewGuid().ToString("N"):target.Id;
        string link=source??(target==null?null:target.Source);
        foreach(var previous in links.Groups.ToArray()){
            previous.Paths.RemoveAll(files.Contains);
            if(previous.Paths.Count==0)links.Groups.Remove(previous);
        }
        var merged=new InventoryLinkGroup{Id=groupId,Name=targetName,CatalogId=catalogId,Source=link,
            Paths=files.OrderBy(file=>file,Paths).ToList()};
        links.Groups.Add(merged);
        return merged;
    }
}
