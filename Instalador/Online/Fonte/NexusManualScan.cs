using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

internal sealed class NexusManualMatch {
    internal GeneralModVariant Variant;
    internal string Version;
    internal Dictionary<string,long?> Files;

    internal bool StillPresent(string root) {
        return Files!=null&&NexusManualScan.FilesMatch(root,Files)==true;
    }
}

internal sealed class NexusManualScan {
    static readonly StringComparer Paths=StringComparer.OrdinalIgnoreCase;
    static readonly Regex GamePath=new Regex(
        @"\A(?<wrapper>[^/]+/)?(?<path>(?:archive/pc/mod|r6/(?:scripts|tweaks|input|audioware)|bin/x64/plugins|red4ext/plugins|engine/config|mods)/.+)\z",
        RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    static readonly Regex Documentation=new Regex(@"\A(?:read[ _-]?me|licen[cs]e|changelog|credits)(?:[ ._-].*)?\z",
        RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    readonly ConcurrentDictionary<string,NexusFilePreview[]> pinned=new ConcurrentDictionary<string,NexusFilePreview[]>(StringComparer.Ordinal);

    internal static bool CanInspect(GeneralModEntry mod) {
        return !GeneralModLibrary.IsHystBody(mod.Id)&&mod.Variants.Count>0&&mod.Variants.All(variant=>
            variant.CyberpunkId==null&&variant.Sources.Count>0&&variant.Sources.All(source=>{
                var link=GeneralNexusLink.Parse(source);
                return link!=null&&(link.File>0||variant.Sources.Count==1);
            }));
    }

    internal Func<GeneralNexusLink,NexusFilePreview[]> PreviewSession(Func<GeneralNexusLink,Func<bool>,NexusFilePreview[]> fetch) {
        var elapsed=Stopwatch.StartNew();
        Func<bool> withinBudget=()=>elapsed.ElapsedMilliseconds<12000;
        var requests=new ConcurrentDictionary<string,Lazy<NexusFilePreview[]>>(StringComparer.Ordinal);
        return link=>{
            NexusFilePreview[] cached;
            if(pinned.TryGetValue(link.Url,out cached))return cached;
            return requests.GetOrAdd(link.Url,url=>new Lazy<NexusFilePreview[]>(()=>{
                var previews=withinBudget()?fetch(link,withinBudget):null;
                if(link.File>0&&previews!=null&&previews.Length==1&&previews[0].Files!=null&&pinned.Count<256)
                    pinned.TryAdd(url,previews);
                return previews;
            })).Value;
        };
    }

    internal Dictionary<string,NexusManualMatch> Inspect(string root,bool cyberpunk,IEnumerable<GeneralModEntry> mods,
        Func<GeneralNexusLink,Func<bool>,NexusFilePreview[]> fetch) {
        var read=PreviewSession(fetch);
        var found=new ConcurrentDictionary<string,NexusManualMatch>(StringComparer.Ordinal);
        Parallel.ForEach(mods.Where(CanInspect),new ParallelOptions{MaxDegreeOfParallelism=3},mod=>{
            var match=Find(root,cyberpunk,mod,read);
            if(match!=null)found.TryAdd(mod.Id,match);
        });
        return new Dictionary<string,NexusManualMatch>(found,StringComparer.Ordinal);
    }

    internal static NexusManualMatch Find(string root,bool cyberpunk,GeneralModEntry mod,Func<GeneralNexusLink,NexusFilePreview[]> read) {
        NexusManualMatch match=null;
        bool uncertain=false;
        foreach(var variant in mod.Variants){
            var sources=variant.Sources.Select(GeneralNexusLink.Parse).Select(read).ToArray();
            if(sources.Any(files=>files==null||files.Length==0)){uncertain=true;continue;}
            // An unpinned source lists alternative downloads. Pinned sources together form one variant.
            var candidates=sources.Length==1?sources[0].Select(file=>new[]{file}):new[]{sources.Select(files=>files.Single()).ToArray()};
            foreach(var candidate in candidates) {
                var paths=Destinations(variant,candidate,cyberpunk);
                var present=paths==null?(bool?)null:FilesMatch(root,paths);
                if(!present.HasValue){uncertain=true;continue;}
                if(!present.Value)continue;
                if(match!=null)return new NexusManualMatch();
                match=new NexusManualMatch{Variant=variant,Files=paths,
                    Version=String.Join(" / ",candidate.Select(file=>file.Version).Where(version=>!String.IsNullOrEmpty(version)).Distinct())};
            }
        }
        return match!=null&&uncertain?new NexusManualMatch():match;
    }

    internal static Dictionary<string,long?> Destinations(GeneralModVariant variant,NexusFilePreview[] previews,bool cyberpunk) {
        var result=new Dictionary<string,long?>(Paths);
        try {
            foreach(var preview in previews) {
                if(preview.Files==null||preview.Files.Keys.Any(path=>path.Split('/').Contains("fomod",Paths)))return null;
                string wrapper=null;
                int count=0;
                foreach(var file in preview.Files) {
                    if(Documentation.IsMatch(Path.GetFileName(file.Key)))continue;
                    string path;
                    if(cyberpunk&&variant.InstallRoutes.Count==0) {
                        var found=GamePath.Match(file.Key);
                        if(found.Success) {
                            string prefix=found.Groups["wrapper"].Value;
                            if(wrapper!=null&&!Paths.Equals(wrapper,prefix))return null;
                            wrapper=prefix;
                            path=found.Groups["path"].Value;
                        }else if(!file.Key.Contains("/")&&new[]{".archive",".xl"}.Contains(Path.GetExtension(file.Key),Paths)) {
                            path="archive/pc/mod/"+file.Key;
                        }else return null;
                    }else path=GeneralInstallRoutes.Map(file.Key,variant.InstallRoutes);
                    if(!InstalledSnapshot.SafeGenericPath(path)||result.ContainsKey(path))return null;
                    result.Add(path,file.Value);
                    count++;
                }
                if(count==0)return null;
            }
        }catch(IOException){return null;}
        catch(ArgumentException){return null;}
        return result;
    }

    internal static bool? FilesMatch(string root,Dictionary<string,long?> paths) {
        try {
            foreach(var item in paths) {
                string path=GeneralInstallRoutes.Resolve(root,item.Key);
                if((File.GetAttributes(path)&FileAttributes.Directory)!=0||
                    item.Value.HasValue&&new FileInfo(path).Length!=item.Value.Value)return false;
            }
            return true;
        }catch(FileNotFoundException){return false;}
        catch(DirectoryNotFoundException){return false;}
        catch(IOException){return null;}
        catch(UnauthorizedAccessException){return null;}
        catch(ArgumentException){return null;}
    }
}
