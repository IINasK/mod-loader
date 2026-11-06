using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal sealed class GeneralStorageSize {
    internal long Mods,Total,Cache,Retained;
    internal int ModCount,Identified;
    internal readonly Dictionary<string,long> ModBytes=new Dictionary<string,long>(StringComparer.Ordinal);
    internal int Projects,WithFiles,Unavailable;
    internal static string Format(long bytes){double value=bytes;string[] units={"B","KiB","MiB","GiB","TiB"};int at=0;while(value>=1024&&at<units.Length-1){value/=1024;at++;}return value.ToString(at==0?"0":"0.##")+" "+units[at];}
    internal static string ProjectText(GeneralStorageSize size){return GeneralStyle.T("Mods: ","Mods: ")+Format(size.Mods)+"   ·   "+GeneralStyle.T("Total do projeto: ","Project total: ")+Format(size.Total)+"   ·   "+GeneralStyle.T("Cache do projeto: ","Project cache: ")+Format(size.Cache)+(size.Retained>0?GeneralStyle.T("   ·   Pacotes antigos: ","   ·   Old packages: ")+Format(size.Retained):"")+(size.Unavailable>0?GeneralStyle.T(" (parcial)"," (partial)"):"");}
    internal static string HomeText(GeneralStorageSize size){return GeneralStyle.T("Medidos: ","Measured: ")+size.Identified+"/"+size.ModCount+"   ·   "+GeneralStyle.T("Projetos com arquivos: ","Projects with files: ")+size.WithFiles+" / "+size.Projects+"   ·   "+GeneralStyle.T("Mods: ","Mods: ")+Format(size.Mods)+"   ·   "+GeneralStyle.T("Total: ","Total: ")+Format(size.Total)+"   ·   "+GeneralStyle.T("Cache: ","Cache: ")+Format(size.Cache)+(size.Retained>0?GeneralStyle.T("   ·   Pacotes antigos: ","   ·   Old packages: ")+Format(size.Retained):"")+(size.Unavailable>0?GeneralStyle.T(" (parcial)"," (partial)"):"");}
}
internal static class GeneralProjectStorage {
    internal static GeneralStorageSize Measure(GeneralProjectStore store,FixedCatalog catalog,DownloadCache cache,GeneralProject selected=null){
        var result=new GeneralStorageSize();var mods=Paths();var total=Paths();var overhead=Paths();var retained=Paths();var errors=Paths();
        var sizes=new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase);var seed=GeneralModLibrary.Seed(catalog,new ModImportItem[0]);
        foreach(var project in store.Read(true).Projects.Where(item=>selected==null||item.Id==selected.Id)){
            result.Projects++;var projectFiles=Paths();string data=store.DataPath(project);
            try{
                var state=store.Load(project,true);result.ModCount+=state.List.Mods.Count;
                var perMod=state.List.Mods.ToDictionary(mod=>mod.Id,mod=>Paths(),StringComparer.Ordinal);
                var aliases=new Dictionary<string,HashSet<string>>(StringComparer.Ordinal);
                foreach(var pair in state.CyberpunkImports){
                    string id=pair.Key.Split('/')[0];if(!perMod.ContainsKey(id))continue;
                    HashSet<string> names;if(!aliases.TryGetValue(pair.Value,out names))aliases[pair.Value]=names=new HashSet<string>();names.Add(id);
                }
                var paths=Paths();
                if(!String.IsNullOrWhiteSpace(state.Destination)&&Directory.Exists(state.Destination)){
                    string root=Path.GetFullPath(state.Destination).TrimEnd(Path.DirectorySeparatorChar);
                    Action<ProjectDeleteFile> record=file=>{
                        string path=Safe.In(root,file.Path);paths.Add(path);
                        foreach(string owner in file.OwnerKeys??new List<string>()){
                            string id=owner.Split('/')[0];HashSet<string> own;if(perMod.TryGetValue(id,out own))own.Add(path);
                            HashSet<string> names;if(aliases.TryGetValue(owner,out names))foreach(string name in names)perMod[name].Add(path);
                        }
                    };
                    foreach(var file in new GeneralModInstaller(root,Path.Combine(data,"Instalacoes")).ProjectDeletionFiles(true))record(file);
                    foreach(var file in new InstalledSnapshotInstaller(root,data,catalog).ProjectDeletionFiles(true))record(file);
                    foreach(var mod in state.List.Mods){
                        var original=seed.Mods.FirstOrDefault(item=>item.Id==mod.Id);if(original==null)continue;
                        foreach(var variant in mod.Variants){var trusted=original.Variants.FirstOrDefault(item=>item.Id==variant.Id&&item.Sources.SequenceEqual(variant.Sources));if(trusted==null||trusted.CyberpunkId==null)continue;
                            foreach(var recipe in FixedData.Expand(catalog,new[]{trusted.CyberpunkId})){
                                var known=recipe.Id==NightCityDensity.ModId?NightCityDensity.KnownFiles(recipe):recipe.Files;
                                foreach(var file in known){string path=Safe.In(root,file.Path);paths.Add(path);if(recipe.Id==trusted.CyberpunkId)perMod[mod.Id].Add(path);}
                            }
                        }
                    }
                    var imports=new HashSet<string>(state.CyberpunkImports.Values,StringComparer.Ordinal);
                    foreach(var mod in state.List.Mods.Where(item=>item.Id.StartsWith("file-",StringComparison.Ordinal)&&item.Id.Length==69))imports.Add(mod.Id);
                    if(File.Exists(Safe.In(root,"bin/x64/Cyberpunk2077.exe")))foreach(var file in new FixedInstaller(catalog,root).ProjectDeletionFiles(imports,true))record(file);
                }
                foreach(string path in paths)if(Add(path,projectFiles,sizes,errors)){Add(path,mods,sizes,errors);Add(path,total,sizes,errors);}
                foreach(var pair in perMod){
                    var present=Paths();foreach(string path in pair.Value)Add(path,present,sizes,errors);
                    if(present.Count>0&&!pair.Value.Any(errors.Contains)){result.Identified++;if(selected!=null)result.ModBytes[pair.Key]=Bytes(present,sizes);}
                }
                foreach(string path in state.ResolvedDownloads.Values.Concat(state.List.Mods.SelectMany(mod=>mod.Variants).SelectMany(variant=>variant.Sources)).Where(GeneralModDownload.IsArchivePath)){
                    Add(path,projectFiles,sizes,errors);Add(path,total,sizes,errors);
                }
                foreach(string path in DownloadCache.SourcePaths(Path.Combine(data,"Cache"))){Add(path,projectFiles,sizes,errors);Add(path,total,sizes,errors);}
                if(cache.RetainedRoot!=null)foreach(var mod in state.List.Mods){var original=seed.Mods.FirstOrDefault(item=>item.Id==mod.Id);if(original==null)continue;
                    var ids=mod.Variants.SelectMany(variant=>original.Variants.Where(v=>v.Id==variant.Id&&v.Sources.SequenceEqual(variant.Sources)&&v.CyberpunkId!=null).Select(v=>v.CyberpunkId)).Distinct();
                    foreach(var asset in FixedData.Expand(catalog,ids).SelectMany(recipe=>recipe.Id==NightCityDensity.ModId?NightCityDensity.Assets(recipe):recipe.Assets))Add(Safe.In(cache.RetainedRoot,asset.Sha256+".cache"),retained,sizes,errors);
                }


            }catch(IOException){result.Unavailable++;}catch(UnauthorizedAccessException){result.Unavailable++;}
            WalkProject(data,project.Legacy,overhead,sizes,errors);if(projectFiles.Count>0)result.WithFiles++;
        }
        if(selected==null){Add(Path.Combine(store.Root,"projects.json"),overhead,sizes,errors);
            if(cache.RetainedRoot!=null)try{Safe.NoLinks(cache.RetainedRoot);if(Directory.Exists(cache.RetainedRoot))foreach(string file in Directory.GetFiles(cache.RetainedRoot,"*.cache"))if(System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(file),"^[a-f0-9]{64}\\.cache$"))Add(file,retained,sizes,errors);}catch(IOException){errors.Add(cache.RetainedRoot);}catch(UnauthorizedAccessException){errors.Add(cache.RetainedRoot);}
        }
        total.UnionWith(overhead);total.UnionWith(retained);result.Retained=Bytes(retained,sizes);result.Mods=Bytes(mods,sizes);result.Cache=Bytes(overhead,sizes);result.Total=Bytes(total,sizes);result.Unavailable+=errors.Count;return result;
    }
    static HashSet<string> Paths(){return new HashSet<string>(StringComparer.OrdinalIgnoreCase);}
    // Metrics never hold a read lock over files which the installer may replace.
    internal static string ReadText(string path,int max){
        Safe.NoLinks(path);using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
            if(input.Length>max)throw new IOException("Registro excessivo.");using(var reader=new StreamReader(input)){var text=reader.ReadToEnd();if(text.Length>max)throw new IOException("Registro excessivo.");return text;}
        }
    }
    static bool Add(string path,HashSet<string> files,Dictionary<string,long> sizes,HashSet<string> errors){
        string full=Path.GetFullPath(path);
        try{if(!sizes.ContainsKey(full)){Safe.NoLinks(full);var info=new FileInfo(full);if(!info.Exists)return false;sizes[full]=info.Length;}return files.Add(full);}
        catch(IOException){errors.Add(full);return false;}catch(UnauthorizedAccessException){errors.Add(full);return false;}
    }
    static void WalkProject(string root,bool legacy,HashSet<string> files,Dictionary<string,long> sizes,HashSet<string> errors){
        if(!legacy){Walk(root,files,sizes,errors);return;}
        try{Safe.NoLinks(root);if(!Directory.Exists(root))return;
            foreach(string file in Directory.GetFiles(root))if(!Path.GetFileName(file).Equals("projects.json",StringComparison.OrdinalIgnoreCase))Add(file,files,sizes,errors);
            foreach(string folder in Directory.GetDirectories(root))if(!Path.GetFileName(folder).Equals("Projetos",StringComparison.OrdinalIgnoreCase))Walk(folder,files,sizes,errors);
        }catch(IOException){errors.Add(root);}catch(UnauthorizedAccessException){errors.Add(root);}
    }
    static void Walk(string root,HashSet<string> files,Dictionary<string,long> sizes,HashSet<string> errors){
        try{Safe.NoLinks(root);if(!Directory.Exists(root))return;foreach(string file in Directory.GetFiles(root))Add(file,files,sizes,errors);foreach(string folder in Directory.GetDirectories(root))Walk(folder,files,sizes,errors);}
        catch(IOException){errors.Add(root);}catch(UnauthorizedAccessException){errors.Add(root);}
    }
    static long Bytes(IEnumerable<string> paths,Dictionary<string,long> sizes){return paths.Sum(path=>sizes[path]);}
}
