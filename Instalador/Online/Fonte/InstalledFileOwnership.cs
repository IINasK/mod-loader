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
        new[]{".json",".jsonc",".ini",".cfg",".conf",".toml",".txt",".log",".db",".sqlite",".sqlite3",".bak",".preset",".png",".sav",".cache"},Paths);

    internal static bool Companion(string path) {
        string name=Path.GetFileName(path);
        return DataExtensions.Contains(Path.GetExtension(name))||name.Equals("settings.lua",StringComparison.OrdinalIgnoreCase)
            ||name.EndsWith("_settings.lua",StringComparison.OrdinalIgnoreCase);
    }

    internal static bool PrivateState(string path) {
        string extension=Path.GetExtension(path);
        return Paths.Equals(extension,".sqlite3")||Paths.Equals(extension,".preset")||Paths.Equals(extension,".sav")||
            path.Replace('\\','/').Split('/').Any(part=>Paths.Equals(part,"saves-archive")||Paths.Equals(part,"saves")||Paths.Equals(part,"savegames"));
    }

    internal static string GenericScope(string path) {
        int end=path.LastIndexOf('/');
        return end>0&&path.IndexOf('/')<end?path.Substring(0,end):null;
    }

    internal static Dictionary<string,string> FindAssociated(string root,IEnumerable<ModFileAnchor> registered,
        Func<string,string> scope,IEnumerable<string> candidates=null,Func<string,string> readHash=null,bool includeAll=false) {
        var anchors=registered.Where(file=>!String.IsNullOrEmpty(file.Owner)&&!GeneralInstallRoutes.External(file.Path)).ToList();
        var known=new HashSet<string>(anchors.Select(file=>file.Path),Paths);
        var scopes=anchors.GroupBy(file=>scope(file.Path),Paths).Where(group=>group.Key!=null)
            .ToDictionary(group=>group.Key,group=>group.ToList(),Paths);
        var verified=new Dictionary<string,string>(Paths);
        foreach(var group in scopes) {
            var owners=group.Value.Select(file=>file.Owner).Distinct(StringComparer.Ordinal).ToArray();
            if(owners.Length!=1)continue;
            if(group.Value.Any(file=>!Companion(file.Path)&&Matches(root,file,readHash)))verified.Add(group.Key,owners[0]);
        }
        if(candidates==null)candidates=Walk(root,verified.Keys);
        var result=new Dictionary<string,string>(Paths);
        foreach(string path in candidates) {
            if(known.Contains(path)||!includeAll&&!Companion(path))continue;
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

    internal static IEnumerable<KeyValuePair<string,string>> FindDeclared(string root,
        IEnumerable<KeyValuePair<string,string>> declarations,IEnumerable<ModFileAnchor> registered,Func<string,string> readHash){
        var claims=declarations.Distinct().ToArray();
        if(claims.Length==0)yield break;
        var anchors=registered.Where(file=>!GeneralInstallRoutes.External(file.Path)).ToArray();
        var byOwner=anchors.ToLookup(file=>file.Owner,StringComparer.Ordinal);
        var known=new HashSet<string>(anchors.Select(file=>file.Path),Paths);
        foreach(var claim in claims){
            if(claims.Any(other=>other.Value!=claim.Value&&(Within(other.Key,claim.Key)||Within(claim.Key,other.Key)))||
                anchors.Any(file=>file.Owner!=claim.Value&&Within(file.Path,claim.Key))||
                !byOwner[claim.Value].Any(file=>!Companion(file.Path)&&Matches(root,file,readHash)))continue;
            foreach(string path in Walk(root,new[]{claim.Key}).Where(path=>!known.Contains(path)))
                yield return new KeyValuePair<string,string>(path,claim.Value);
        }
    }

    static bool Within(string path,string folder){
        return Paths.Equals(path,folder)||path.StartsWith(folder+"/",StringComparison.OrdinalIgnoreCase);
    }

    static bool Matches(string root,ModFileAnchor anchor,Func<string,string> readHash) {
        if(!FixedData.HashValid(anchor.Hash))return false;
        try {
            string path=Safe.In(root,anchor.Path);Safe.NoLinks(path);
            return File.Exists(path)&&(readHash??Safe.Hash)(path)==anchor.Hash;
        }catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}
    }

    internal static IEnumerable<string> Walk(string root,IEnumerable<string> scopes) {
        var visited=new HashSet<string>(Paths);
        var pending=new Stack<string>(scopes.Select(path=>Safe.In(root,path)));
        int files=0;
        while(pending.Count>0) {
            string dir=pending.Pop();
            if(!visited.Add(dir))continue;
            Safe.NoLinks(dir);
            if(!Directory.Exists(dir))continue;
            using(var children=Directory.EnumerateFileSystemEntries(dir).GetEnumerator())while(true) {
                string path;
                try{if(!children.MoveNext())break;path=children.Current;}
                catch(DirectoryNotFoundException){break;}
                if(++files>20000)throw new IOException("Há arquivos demais nas pastas de mods para conferir.");
                Safe.NoLinks(path);
                if(Directory.Exists(path))pending.Push(path);
                else yield return path.Substring(root.TrimEnd('\\').Length+1).Replace('\\','/');
            }
        }
    }
}
