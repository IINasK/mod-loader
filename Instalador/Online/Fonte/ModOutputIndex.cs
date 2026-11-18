using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

internal sealed class ModOutputObservation {
    public string Owner,Path;
    public bool Added,Baseline;
}

internal sealed class ModOutputInstallation {
    public GeneralModEntry Entry;
    public string Variant,SnapshotId;
    public Dictionary<string,string> Imports=new Dictionary<string,string>();
}

internal sealed class ModOutputData {
    public List<ModOutputObservation> Files=new List<ModOutputObservation>();
    public List<ModOutputInstallation> Installations=new List<ModOutputInstallation>();
    public List<string> KnownOwners=new List<string>();
}

internal sealed class ModOutputIndex {
    static readonly StringComparer Paths=StringComparer.OrdinalIgnoreCase;
    const int Limit=10000,MaxBytes=8*1024*1024;
    readonly string path;

    internal ModOutputIndex(string data,string destination) {
        string key=InstalledSnapshot.Hash(Encoding.UTF8.GetBytes(Path.GetFullPath(destination).TrimEnd('\\').ToUpperInvariant()));
        path=Safe.In(data,"ModOutputs/"+key+".json");
    }

    internal ModOutputData Load(){
        Safe.NoLinks(path);
        if(!File.Exists(path))return new ModOutputData();
        if(new FileInfo(path).Length>MaxBytes)throw new IOException("Índice de arquivos locais muito grande.");
        ModOutputData data;
        try{data=Safe.Json.Deserialize<ModOutputData>(File.ReadAllText(path));}
        catch(ArgumentException ex){throw new IOException("Índice de arquivos locais inválido.",ex);}
        catch(InvalidOperationException ex){throw new IOException("Índice de arquivos locais inválido.",ex);}
        if(data==null||data.Files==null||data.Files.Count>Limit||data.Installations==null||data.Installations.Count>1000||
            data.KnownOwners==null||data.KnownOwners.Count>1000||
            data.Files.Any(file=>file==null||String.IsNullOrEmpty(file.Owner)||String.IsNullOrEmpty(file.Path))||
            data.Files.Select(file=>file.Owner+"\n"+file.Path).Distinct(Paths).Count()!=data.Files.Count||
            data.Installations.Any(item=>item==null||item.Entry==null||item.Entry.Variants==null||item.Imports==null))
            throw new IOException("Índice de arquivos locais inválido.");
        return data;
    }

    void Save(ModOutputData data){
        Safe.NoLinks(path);
        if(Encoding.UTF8.GetByteCount(Safe.Json.Serialize(data))>MaxBytes)throw new IOException("Índice de arquivos locais muito grande.");
        Safe.WriteJson(path,data);
    }

    // Metadata only. Receipt hashes are checked by the caller's inventory scan.
    internal void Reconcile(IEnumerable<P2PSyncDifference> inventory,ModOutputInstallation installation=null,IEnumerable<string> verifiedOwners=null){
        var current=Load();
        var before=current.Files.ToDictionary(item=>item.Owner+"\n"+item.Path,Paths);
        var known=new HashSet<string>(current.KnownOwners,StringComparer.Ordinal);
        var next=installation==null?new List<ModOutputObservation>():current.Files
            .Where(item=>item.Owner!=installation.Entry.Id).ToList();
        foreach(var file in inventory.Where(IsOutput).GroupBy(file=>file.Path,Paths)){
            var owners=file.Select(candidate=>candidate.ModId).Distinct(StringComparer.Ordinal).ToArray();
            if(owners.Length!=1)continue;
            var item=file.First();
            ModOutputObservation previous;
            bool existing=before.TryGetValue(item.ModId+"\n"+item.Path,out previous);
            bool knownOwner=known.Contains(item.ModId);
            bool baseline=installation!=null&&installation.Entry.Id==item.ModId&&!knownOwner;
            bool added=existing?previous.Added:!baseline&&knownOwner;
            bool preexisting=existing?previous.Baseline:baseline;
            foreach(var match in file){match.GeneratedOutput=added;match.PreexistingOutput=preexisting;}
            if(next.Count<Limit)next.Add(new ModOutputObservation{Owner=item.ModId,Path=item.Path,Added=added,Baseline=preexisting});
        }
        if(installation!=null){
            current.Installations.RemoveAll(record=>record.Entry!=null&&record.Entry.Id==installation.Entry.Id);
            if(current.Installations.Count<1000)current.Installations.Add(installation);
        }
        var ownersNow=current.KnownOwners.Concat(verifiedOwners??Enumerable.Empty<string>()).Concat(next.Select(item=>item.Owner))
            .Concat(installation==null?Enumerable.Empty<string>():new[]{installation.Entry.Id})
            .Distinct(StringComparer.Ordinal).Take(1000).ToList();
        if(installation==null&&ownersNow.Count==known.Count&&before.Count==next.Count&&next.All(item=>{
            ModOutputObservation previous;
            return before.TryGetValue(item.Owner+"\n"+item.Path,out previous)&&previous.Added==item.Added;
        }))return;
        current.Files=next;
        current.KnownOwners=ownersNow;
        Save(current);
    }

    internal void CaptureReceivedBaseline(string root,InstalledSnapshotManifest manifest){
        if(manifest.OutputScopes.Count==0)return;
        var current=Load();
        var paths=new HashSet<string>(manifest.Files.Select(file=>file.Path),Paths);
        var seen=new HashSet<string>(current.Files.Select(file=>file.Owner+"\n"+file.Path),Paths);
        foreach(var scope in manifest.OutputScopes){
            string owner=scope.Owner.Split('/')[0];
            if(!current.KnownOwners.Contains(owner)&&current.KnownOwners.Count<1000)current.KnownOwners.Add(owner);
            foreach(string file in InstalledFileOwnership.Walk(root,new[]{scope.Path})){
                if(paths.Contains(file)||!seen.Add(owner+"\n"+file))continue;
                if(current.Files.Count==Limit)break;
                current.Files.Add(new ModOutputObservation{Owner=owner,Path=file,Baseline=true});
            }
        }
        Save(current);
    }

    internal static bool IsOutput(P2PSyncDifference file){
        return file.Kind=="associated"&&file.LocalInModFolder&&file.ProvenOwner==null&&file.VerifiedZip==null&&
            !String.IsNullOrEmpty(file.ModId);
    }

    internal static List<string> NormalizeFolders(IEnumerable<string> paths){
        var result=(paths??Enumerable.Empty<string>()).Select(path=>(path??"").Trim().Replace('\\','/').TrimEnd('/'))
            .Where(path=>path.Length>0).Distinct(Paths).ToList();
        if(result.Count>32||result.Any(path=>path.Length>240||path.IndexOf('/')<=0||
            GeneralInstallRoutes.External(path)||InstalledFileOwnership.PrivateState(path)||!InstalledSnapshot.SafeGenericPath(path)))
            throw new IOException("Pastas de saída inválidas. Informe caminhos relativos dentro das pastas de mods.");
        return result;
    }

    internal static bool CanSend(string path){
        return !String.IsNullOrEmpty(path)&&Path.GetExtension(path).Equals(".json",StringComparison.OrdinalIgnoreCase)&&
            !GeneralInstallRoutes.External(path)&&!InstalledFileOwnership.PrivateState(path)&&InstalledSnapshot.SafeGenericPath(path)&&
            GeneratedFileProvenance.Category(path)=="configuration";
    }
}
