using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;

internal enum GeneralArchivePreviewOrigin { Unavailable, Nexus, Catalog, Local, Mixed }

internal sealed class GeneralArchivePreview {
    static readonly NexusManualScan NexusCache = new NexusManualScan();
    internal readonly Dictionary<string,string> SuggestedPaths;
    internal readonly string[] Paths;
    internal readonly GeneralArchivePreviewOrigin Origin;
    internal readonly bool NeedsFileSelection;

    GeneralArchivePreview(GeneralArchivePreviewOrigin origin, IEnumerable<string> paths,
        Dictionary<string,string> suggestions=null,bool needsFileSelection=false) {
        Origin=origin;
        Paths=paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        SuggestedPaths=suggestions??new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        NeedsFileSelection=needsFileSelection;
    }

    internal static GeneralArchivePreview Combine(IEnumerable<GeneralArchivePreview> previews) {
        var all=previews.Where(preview=>preview!=null).ToArray();
        var available=all.Where(preview=>preview.Paths.Length>0).ToArray();
        if(available.Length==0)return new GeneralArchivePreview(GeneralArchivePreviewOrigin.Unavailable,
            new string[0],needsFileSelection:all.Any(preview=>preview.NeedsFileSelection));
        if(available.Length==1)return available[0];

        var suggestions=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var blocked=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var preview in available)
            foreach(string path in preview.Paths) {
                if(blocked.Contains(path))continue;
                string suggested,previous;
                if(preview.SuggestedPaths.TryGetValue(path,out suggested)&&
                    (!suggestions.TryGetValue(path,out previous)||String.Equals(previous,suggested,StringComparison.OrdinalIgnoreCase)))
                    suggestions[path]=suggested;
                else {
                    suggestions.Remove(path);
                    blocked.Add(path);
                }
            }
        var origin=available.All(preview=>preview.Origin==available[0].Origin)?available[0].Origin:GeneralArchivePreviewOrigin.Mixed;
        return new GeneralArchivePreview(origin,available.SelectMany(preview=>preview.Paths),suggestions);
    }

    internal static GeneralArchivePreview Load(string source,string localArchive,IEnumerable<string> catalogPaths,
        bool cyberpunk,Func<GeneralNexusLink,NexusFilePreview[]> read=null) {
        if(GeneralModDownload.IsArchivePath(source)&&File.Exists(source))return ReadLocal(source);

        var known=catalogPaths==null?new string[0]:catalogPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(known.Length>0)return new GeneralArchivePreview(GeneralArchivePreviewOrigin.Catalog,known,
            known.ToDictionary(path=>path,StringComparer.OrdinalIgnoreCase));

        var link=GeneralNexusLink.Parse(source);
        if(link!=null&&link.File>0) {
            NexusFilePreview preview=null;
            try {
                var fetch=read??NexusCache.PreviewSession(GeneralNexusInfo.ReadFilePreviews);
                var candidates=fetch(link);
                if(candidates!=null&&candidates.Length==1&&candidates[0]!=null&&candidates[0].Id==link.File)
                    preview=candidates[0];
            } catch(WebException) {
            } catch(IOException) {
            }
            if(preview!=null&&preview.Files!=null&&preview.Files.Count>0) {
                var suggestions=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                if(NexusManualScan.Destinations(new GeneralModVariant(),new[]{preview},cyberpunk,suggestions)==null)
                    suggestions.Clear();
                else if(cyberpunk)
                    foreach(string path in suggestions.Keys.Where(path=>!LocalModPackage.Allowed(suggestions[path])).ToArray())
                        suggestions.Remove(path);
                return new GeneralArchivePreview(GeneralArchivePreviewOrigin.Nexus,preview.Files.Keys,suggestions);
            }
        }

        if(GeneralModDownload.IsArchivePath(localArchive)&&File.Exists(localArchive))return ReadLocal(localArchive);
        return new GeneralArchivePreview(GeneralArchivePreviewOrigin.Unavailable,new string[0],
            needsFileSelection:link!=null&&link.File==0);
    }

    static GeneralArchivePreview ReadLocal(string source) {
        return new GeneralArchivePreview(GeneralArchivePreviewOrigin.Local,BundledArchiveReader.ReadEntries(source).Select(file=>file.Path));
    }
}
