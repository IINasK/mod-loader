using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal sealed class ModFileAnchor {
    internal string Path, Hash, Owner;
    internal bool LocalReceipt;
}

// Companion files provide context, never ownership for export or deletion.
internal static class InstalledFileOwnership {
    static readonly StringComparer Paths=StringComparer.OrdinalIgnoreCase;
    static readonly HashSet<string> DataExtensions=new HashSet<string>(
        new[]{".json",".jsonc",".ini",".cfg",".conf",".toml",".txt",".log",".db",".sqlite",".sqlite3",".bak",".preset",".png"},Paths);

    internal static bool Companion(string path) {
        string name=Path.GetFileName(path);
        return DataExtensions.Contains(Path.GetExtension(name))||name.Equals("settings.lua",StringComparison.OrdinalIgnoreCase)
            ||name.EndsWith("_settings.lua",StringComparison.OrdinalIgnoreCase);
    }

    internal static bool PrivateState(string path) {
        string extension=Path.GetExtension(path);
        return Paths.Equals(extension,".sqlite3")||Paths.Equals(extension,".preset")||
            path.Split('/').Any(part=>Paths.Equals(part,"saves-archive"));
    }

    internal static string GenericScope(string path) {
        int end=path.LastIndexOf('/');
        return end>0&&path.IndexOf('/')<end?path.Substring(0,end):null;
    }

    internal static Dictionary<string,string> FindAssociated(string root,IEnumerable<ModFileAnchor> registered,
        Func<string,string> scope,IEnumerable<string> candidates=null) {
        var anchors=registered.Where(file=>!String.IsNullOrEmpty(file.Owner)&&!GeneralInstallRoutes.External(file.Path)).ToList();
        var known=new HashSet<string>(anchors.Select(file=>file.Path),Paths);
        var scopes=anchors.GroupBy(file=>scope(file.Path),Paths).Where(group=>group.Key!=null)
            .ToDictionary(group=>group.Key,group=>group.ToList(),Paths);
        var verified=new Dictionary<string,string>(Paths);
        foreach(var group in scopes) {
            var owners=group.Value.Select(file=>file.Owner).Distinct(StringComparer.Ordinal).ToArray();
            if(owners.Length!=1)continue;
            if(group.Value.Any(file=>!Companion(file.Path)&&Matches(root,file)))verified.Add(group.Key,owners[0]);
        }
        if(candidates==null)candidates=Walk(root,verified.Keys);
        var result=new Dictionary<string,string>(Paths);
        foreach(string path in candidates) {
            if(known.Contains(path)||!Companion(path))continue;
            var owners=new HashSet<string>(StringComparer.Ordinal);
            bool verifiedScope=false;
            for(int slash=path.LastIndexOf('/');slash>=0;slash=path.LastIndexOf('/',slash-1)) {
                string parent=path.Substring(0,slash);
                List<ModFileAnchor> parents;
                if(!scopes.TryGetValue(parent,out parents))continue;
                foreach(var anchor in parents)owners.Add(anchor.Owner);
                verifiedScope|=verified.ContainsKey(parent);
            }
            if(verifiedScope&&owners.Count==1)result[path]=owners.Single();
        }
        return result;
    }

    static bool Matches(string root,ModFileAnchor anchor) {
        if(!FixedData.HashValid(anchor.Hash))return false;
        string path=Safe.In(root,anchor.Path);Safe.NoLinks(path);
        return File.Exists(path)&&Safe.Hash(path)==anchor.Hash;
    }

    static IEnumerable<string> Walk(string root,IEnumerable<string> scopes) {
        var visited=new HashSet<string>(Paths);
        var pending=new Stack<string>(scopes.Select(path=>Safe.In(root,path)));
        int files=0;
        while(pending.Count>0) {
            string dir=pending.Pop();
            if(!visited.Add(dir))continue;
            Safe.NoLinks(dir);
            foreach(string path in Directory.EnumerateFileSystemEntries(dir)) {
                if(++files>20000)throw new IOException("Há arquivos demais nas pastas de mods para conferir.");
                Safe.NoLinks(path);
                if(Directory.Exists(path))pending.Push(path);
                else yield return path.Substring(root.TrimEnd('\\').Length+1).Replace('\\','/');
            }
        }
    }
}
