using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal sealed class GeneralStorageSize {
    internal long Mods,Total;
    internal int Projects,WithFiles,Unavailable;
    internal static string Format(long bytes){double value=bytes;string[] units={"B","KiB","MiB","GiB","TiB"};int at=0;while(value>=1024&&at<units.Length-1){value/=1024;at++;}return value.ToString(at==0?"0":"0.##")+" "+units[at];}
    internal static string ProjectText(GeneralStorageSize size){return GeneralStyle.T("Mods: ","Mods: ")+Format(size.Mods)+"   ·   "+GeneralStyle.T("Total do projeto: ","Project total: ")+Format(size.Total)+(size.Unavailable>0?GeneralStyle.T(" (parcial)"," (partial)"):"");}
    internal static string HomeText(GeneralStorageSize size){return GeneralStyle.T("Projetos com arquivos: ","Projects with files: ")+size.WithFiles+" / "+size.Projects+"   ·   "+GeneralStyle.T("Mods: ","Mods: ")+Format(size.Mods)+"   ·   "+GeneralStyle.T("Total: ","Total: ")+Format(size.Total)+(size.Unavailable>0?GeneralStyle.T(" (parcial)"," (partial)"):"");}
}
internal static class GeneralProjectStorage {
    internal static GeneralStorageSize Measure(GeneralProjectStore store,FixedCatalog catalog,DownloadCache cache,GeneralProject selected=null){
        var result=new GeneralStorageSize();var mods=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var total=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seed=GeneralModLibrary.Seed(catalog,new ModImportItem[0]);
        foreach(var project in store.Read(true).Projects.Where(item=>selected==null||item.Id==selected.Id)){
            result.Projects++;int found=0;
            try{
                var state=store.Load(project,true);string data=store.DataPath(project);var projectFiles=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if(!String.IsNullOrWhiteSpace(state.Destination)&&Directory.Exists(state.Destination)){
                    string root=Path.GetFullPath(state.Destination).TrimEnd('\\');
                    foreach(var file in new GeneralModInstaller(root,Path.Combine(data,"Instalacoes")).ProjectDeletionFiles(true))paths.Add(Safe.In(root,file.Path));
                    foreach(var file in new InstalledSnapshotInstaller(root,data,catalog).ProjectDeletionFiles(true))paths.Add(Safe.In(root,file.Path));
                    foreach(var mod in state.List.Mods){
                        var original=seed.Mods.FirstOrDefault(item=>item.Id==mod.Id);if(original==null)continue;
                        foreach(var variant in mod.Variants){var trusted=original.Variants.FirstOrDefault(item=>item.Id==variant.Id&&item.Sources.SequenceEqual(variant.Sources));if(trusted==null)continue;
                            foreach(var recipe in FixedData.Expand(catalog,new[]{trusted.CyberpunkId})){
                                var actual=recipe.Id==NightCityDensity.ModId?NightCityDensity.Installed(recipe,root):recipe;foreach(var file in actual.Files)paths.Add(Safe.In(root,file.Path));
                            }
                        }
                    }
                    var imports=new HashSet<string>(state.CyberpunkImports.Values,StringComparer.Ordinal);foreach(var mod in state.List.Mods.Where(item=>item.Id.StartsWith("file-",StringComparison.Ordinal)&&item.Id.Length==69))imports.Add(mod.Id);
                    if(File.Exists(Safe.In(root,"bin/x64/Cyberpunk2077.exe")))foreach(var file in new FixedInstaller(catalog,root).ProjectDeletionFiles(imports,true))paths.Add(Safe.In(root,file.Path));
                }
                foreach(string path in paths)if(Add(path,projectFiles)){found++;Add(path,mods);Add(path,total);}
                foreach(string path in state.ResolvedDownloads.Values.Concat(state.List.Mods.SelectMany(mod=>mod.Variants).SelectMany(variant=>variant.Sources)).Where(GeneralModDownload.IsArchivePath)){if(Add(path,projectFiles))found++;Add(path,total);}
                foreach(var mod in state.List.Mods){var original=seed.Mods.FirstOrDefault(item=>item.Id==mod.Id);if(original==null)continue;foreach(var asset in catalog.Packages.Where(recipe=>original.Variants.Any(v=>v.CyberpunkId==recipe.Id)).SelectMany(recipe=>recipe.Assets)){if(Add(cache.PathFor(asset),projectFiles))found++;Add(cache.PathFor(asset),total);}}
                Walk(data,total);if(found>0)result.WithFiles++;
            }catch(IOException){result.Unavailable++;}catch(UnauthorizedAccessException){result.Unavailable++;}
        }
        if(selected==null){Walk(store.Root,total);Walk(cache.Root,total);}
        result.Mods=Bytes(mods);result.Total=Bytes(total);return result;
    }
    // Metrics never hold a read lock over files which the installer may replace.
    internal static string ReadText(string path,int max){
        Safe.NoLinks(path);using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
            if(input.Length>max)throw new IOException("Registro excessivo.");using(var reader=new StreamReader(input)){var text=reader.ReadToEnd();if(text.Length>max)throw new IOException("Registro excessivo.");return text;}
        }
    }
    static bool Add(string path,HashSet<string> files){try{Safe.NoLinks(path);if(!File.Exists(path))return false;return files.Add(Path.GetFullPath(path));}catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}}
    static void Walk(string root,HashSet<string> files){try{Safe.NoLinks(root);if(!Directory.Exists(root))return;foreach(string file in Directory.GetFiles(root))Add(file,files);foreach(string folder in Directory.GetDirectories(root))Walk(folder,files);}catch(IOException){}catch(UnauthorizedAccessException){}}
    static long Bytes(IEnumerable<string> paths){long sum=0;foreach(string path in paths)try{sum=checked(sum+new FileInfo(path).Length);}catch(IOException){}catch(UnauthorizedAccessException){}return sum;}
}
