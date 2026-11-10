using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

internal sealed class InventoryPreviewOwner {
    internal string Id,Name,CatalogId,RowId;
    internal string[] Anchors;
    internal GeneralNexusLink Link;
    internal GeneralModVariant Variant;
    internal string IgnoreKey {get{return Link.Game+"/"+Link.Mod;}}
}

internal sealed class InventoryLinkSuggestion {
    internal InventoryPreviewOwner Target;
    internal string RowId,Name;
    internal string[] Paths;
}

internal static class InventoryLinkSuggestions {
    static readonly StringComparer Paths=StringComparer.OrdinalIgnoreCase;

    internal static InventoryLinkSuggestion[] Find(string root,bool cyberpunk,IEnumerable<InventoryPreviewOwner> owners,
        IEnumerable<P2PSyncDifference> inventory,Func<GeneralNexusLink,NexusFilePreview[]> preview) {
        var rows=inventory.Where(file=>file.Kind=="found").GroupBy(InventoryLinkStore.Key,Paths)
            .Select(row=>new{Id=row.Key,Name=row.First().Mod??row.First().Path,Paths=row.Select(file=>file.Path).ToArray()}).ToArray();
        var found=new ConcurrentBag<InventoryLinkSuggestion>();
        Parallel.ForEach(owners,new ParallelOptions{MaxDegreeOfParallelism=3},owner=>{
            var files=preview(owner.Link);
            if(files==null)return;
            var matches=new Dictionary<string,InventoryLinkSuggestion>(Paths);
            foreach(var file in files){
                var expected=NexusManualScan.Destinations(owner.Variant,new[]{file},cyberpunk);
                if(expected==null)continue;
                if(owner.Anchors!=null&&!owner.Anchors.Any(path=>expected.ContainsKey(path)&&Present(root,expected,new[]{path})))continue;
                foreach(var row in rows){
                    if(Paths.Equals(row.Id,owner.RowId)||matches.ContainsKey(row.Id)||!row.Paths.All(expected.ContainsKey))continue;
                    if(!Present(root,expected,row.Paths))continue;
                    matches.Add(row.Id,new InventoryLinkSuggestion{Target=owner,RowId=row.Id,Name=row.Name,Paths=row.Paths});
                }
            }
            foreach(var match in matches.Values)found.Add(match);
        });
        return found.GroupBy(item=>item.RowId,Paths)
            .Where(group=>group.Select(item=>item.Target.Id).Distinct(StringComparer.Ordinal).Count()==1)
            .Select(group=>group.OrderBy(item=>item.Target.IgnoreKey,StringComparer.Ordinal).First())
            .OrderBy(item=>item.Name,Paths).ThenBy(item=>item.RowId,Paths).ToArray();
    }

    static bool Present(string root,Dictionary<string,long?> expected,IEnumerable<string> paths) {
        return NexusManualScan.FilesMatch(root,paths.ToDictionary(path=>path,path=>expected[path],Paths))==true;
    }
}
