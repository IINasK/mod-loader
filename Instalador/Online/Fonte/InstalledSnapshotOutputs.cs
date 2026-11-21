using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public sealed class SnapshotOutputScope {
    public string Owner,Path,Anchor;
}

internal sealed class P2POutputSelection {
    public string Owner,Path,Hash;
}

public static partial class InstalledSnapshot {
    static readonly StringComparer OutputPaths=StringComparer.OrdinalIgnoreCase;

    internal static void AddOutputScopes(InstalledSnapshotManifest manifest){
        var installed=manifest.Files.Where(file=>file.Kind=="file"&&!GeneralInstallRoutes.External(file.Path)).ToArray();
        Func<string,string> scopeForPath=manifest.Game=="cyberpunk2077"?(Func<string,string>)FixedInstaller.ModFolder:InstalledFileOwnership.GenericScope;
        var groups=installed.GroupBy(file=>scopeForPath(file.Path),OutputPaths).Where(group=>group.Key!=null&&SafeGenericPath(group.Key)&&
            !InstalledFileOwnership.PrivateState(group.Key)).ToArray();
        var scopes=groups.Where(group=>group.SelectMany(file=>file.Owners).Distinct(StringComparer.Ordinal).Count()==1)
            .Select(group=>new{Folder=group.Key,Anchor=group.FirstOrDefault(file=>!InstalledFileOwnership.Companion(file.Path))})
            .Where(group=>group.Anchor!=null).Select(group=>new SnapshotOutputScope{
                Owner=group.Anchor.Owners.Single(),Path=group.Folder,Anchor=group.Anchor.Path
            }).ToDictionary(scope=>scope.Path,OutputPaths);
        var byOwner=installed.Where(file=>file.Owners.Count==1&&!InstalledFileOwnership.Companion(file.Path))
            .ToLookup(file=>file.Owners[0],StringComparer.Ordinal);
        var blocked=new HashSet<string>(OutputPaths);
        foreach(var mod in manifest.List.Mods){
            var variant=mod.Variants.Single();
            string owner=Owner(mod,variant);
            var anchor=byOwner[owner].FirstOrDefault();
            if(anchor==null)continue;
            foreach(string folder in ModOutputIndex.NormalizeFolders(variant.OutputFolders)){
                SnapshotOutputScope existing;
                if(!scopes.TryGetValue(folder,out existing))
                    scopes.Add(folder,new SnapshotOutputScope{Owner=owner,Path=folder,Anchor=anchor.Path});
                else if(existing.Owner!=owner)blocked.Add(folder);
            }
        }
        foreach(var item in scopes.Values)
            for(int slash=item.Path.LastIndexOf('/');slash>=0;slash=item.Path.LastIndexOf('/',slash-1)){
                SnapshotOutputScope parent;
                if(scopes.TryGetValue(item.Path.Substring(0,slash),out parent)&&parent.Owner!=item.Owner){
                    blocked.Add(parent.Path);blocked.Add(item.Path);
                }
            }
        foreach(string folder in blocked)scopes.Remove(folder);
        foreach(var file in installed)
            for(int slash=file.Path.LastIndexOf('/');slash>=0;slash=file.Path.LastIndexOf('/',slash-1)){
                string folder=file.Path.Substring(0,slash);
                SnapshotOutputScope candidate;
                if(scopes.TryGetValue(folder,out candidate)&&file.Owners.Any(owner=>owner!=candidate.Owner))
                    scopes.Remove(folder);
            }
        manifest.OutputScopes=scopes.Values.OrderBy(scope=>scope.Path,OutputPaths).Take(MaxFiles).ToList();
    }

    static bool UnderOutputScope(string path,string scope){return path.StartsWith(scope+"/",StringComparison.OrdinalIgnoreCase);}

    static void ValidateOutputScopes(InstalledSnapshotManifest manifest,Dictionary<string,GeneralModVariant> owners){
        if(manifest.OutputScopes==null||manifest.OutputScopes.Count>MaxFiles)throw new IOException("Mapa de saídas inválido.");
        if(manifest.OutputScopes.Count==0)return;
        var installed=manifest.Files.Where(file=>file!=null&&file.Kind=="file"&&file.Path!=null).ToLookup(file=>file.Path,OutputPaths);
        var seen=new HashSet<string>(OutputPaths);
        foreach(var scope in manifest.OutputScopes){
            GeneralModVariant variant;
            if(scope==null||scope.Path==null||scope.Anchor==null||String.IsNullOrEmpty(scope.Owner)||
                !seen.Add(scope.Path)||GeneralInstallRoutes.External(scope.Path)||!SafeGenericPath(scope.Path)||
                InstalledFileOwnership.PrivateState(scope.Path)||!owners.TryGetValue(scope.Owner,out variant)||
                !(UnderOutputScope(scope.Anchor,scope.Path)&&
                    OutputPaths.Equals(scope.Path,manifest.Game=="cyberpunk2077"?FixedInstaller.ModFolder(scope.Anchor):InstalledFileOwnership.GenericScope(scope.Anchor))||
                    variant.OutputFolders.Contains(scope.Path,OutputPaths))||
                !installed[scope.Anchor].Any(file=>file.Owners!=null&&file.Owners.Count==1&&file.Owners[0]==scope.Owner&&
                    !InstalledFileOwnership.Companion(file.Path)))
                throw new IOException("A pasta de saída não tem uma referência instalada válida.");
        }
        var indexed=manifest.OutputScopes.ToDictionary(scope=>scope.Path,OutputPaths);
        foreach(var item in manifest.OutputScopes)
            for(int slash=item.Path.LastIndexOf('/');slash>=0;slash=item.Path.LastIndexOf('/',slash-1)){
                SnapshotOutputScope parent;
                if(indexed.TryGetValue(item.Path.Substring(0,slash),out parent)&&parent.Owner!=item.Owner)
                    throw new IOException("Pastas de saída de mods diferentes se sobrepõem.");
            }
        foreach(var file in installed.SelectMany(group=>group))
            for(int slash=file.Path.LastIndexOf('/');slash>=0;slash=file.Path.LastIndexOf('/',slash-1)){
                SnapshotOutputScope scope;
                if(indexed.TryGetValue(file.Path.Substring(0,slash),out scope)&&
                    (file.Owners==null||file.Owners.Any(owner=>owner!=scope.Owner)))
                    throw new IOException("A pasta de saída é compartilhada por mais de um mod.");
            }
    }

    internal static string ReadGeneratedConfig(string root,string path){
        if(!ModOutputIndex.CanSend(path))throw new IOException("A configuração gerada não é portátil.");
        string location=Safe.In(root,path);
        Safe.NoLinks(location);
        if(!File.Exists(location)||new FileInfo(location).Length>262144)throw new IOException("Configuração gerada ausente ou muito grande.");
        if(PortableLuaSettings.IsSettingsPath(path))return PortableLuaSettings.Encode(InstalledSnapshotConfig.Read(location));
        string json=InstalledSnapshotConfig.Json(InstalledSnapshotConfig.Read(location));
        CheckPortableValues(InstalledSnapshotConfig.Object(json));
        return json;
    }

    internal static string GeneratedConfigHash(string path,string content){
        return Hash(PortableLuaSettings.IsSettingsPath(path)?PortableLuaSettings.Bytes(content):Utf8.GetBytes(content));
    }

    static void CheckPortableValues(object value){
        var map=value as Dictionary<string,object>;
        if(map!=null){
            foreach(var pair in map){
                if(!Regex.IsMatch(pair.Key,@"\A[A-Za-z0-9_.-]{1,128}\z")||
                    Regex.IsMatch(pair.Key,"password|secret|credential|cookie|token|username|userid|profile|machine|device|address|path|key",RegexOptions.IgnoreCase))
                    throw new IOException("A configuração gerada contém uma chave local ou privada.");
                CheckPortableValues(pair.Value);
            }
            return;
        }
        var array=value as System.Collections.IList;
        if(array!=null){
            foreach(object item in array)CheckPortableValues(item);
            return;
        }
        if(value is string)throw new IOException("Configurações geradas com valores de texto permanecem locais.");
    }

    internal static void AddGeneratedConfigs(InstalledSnapshotManifest manifest,string root,IEnumerable<P2POutputSelection> selected,Action checkpoint){
        var entries=new HashSet<string>(manifest.Files.Select(file=>file.Path),OutputPaths);
        var scopes=manifest.OutputScopes.ToLookup(scope=>scope.Owner.Split('/')[0],StringComparer.Ordinal);
        foreach(var item in selected){
            if(checkpoint!=null)checkpoint();
            var scope=scopes[item.Owner].FirstOrDefault(candidate=>UnderOutputScope(item.Path,candidate.Path));
            if(scope==null||entries.Contains(item.Path))continue;
            string content=ReadGeneratedConfig(root,item.Path);
            var file=Config(item.Path,PortableLuaSettings.IsSettingsPath(item.Path)?PortableLuaSettings.Kind:"json",null,content);
            if(file.Sha256!=item.Hash)throw new IOException("A configuração gerada mudou após a revisão: "+item.Path);
            file.Generated=true;
            file.Owners.Add(scope.Owner);
            manifest.Files.Add(file);
            entries.Add(file.Path);
            manifest.Schema=3;
        }
    }

    static void ValidateGeneratedConfig(InstalledSnapshotManifest manifest,InstalledSnapshotFile file){
        if(manifest.Schema!=3||file.Kind!=(PortableLuaSettings.IsSettingsPath(file.Path)?PortableLuaSettings.Kind:"json")||file.Owners.Count!=1||!ModOutputIndex.CanSend(file.Path)||
            !manifest.OutputScopes.Any(scope=>scope.Owner==file.Owners[0]&&UnderOutputScope(file.Path,scope.Path)))
            throw new IOException("Configuração gerada sem pasta de saída comprovada.");
        if(file.Kind=="json")CheckPortableValues(InstalledSnapshotConfig.Object(file.Content));
    }
}
