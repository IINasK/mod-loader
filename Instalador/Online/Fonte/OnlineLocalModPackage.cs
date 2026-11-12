using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

internal static class LocalModPackage {
    internal const string ReceiptOperation = "local-import";
    static readonly Regex GamePath = new Regex(@"^(?:(?:[^/]+/)?)(?<path>(?:archive/pc/mod|r6/(?:scripts|tweaks)|bin/x64/plugins/cyber_engine_tweaks/mods|red4ext/plugins)/.+)$", RegexOptions.IgnoreCase);
    static readonly string CetMods = "bin/x64/plugins/cyber_engine_tweaks/mods/";

    internal static bool Allowed(string path) {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (path.StartsWith("archive/pc/mod/",StringComparison.OrdinalIgnoreCase)) return extension==".archive" || extension==".xl";
        if (path.StartsWith("r6/scripts/",StringComparison.OrdinalIgnoreCase)) return extension==".reds";
        if (path.StartsWith("r6/tweaks/",StringComparison.OrdinalIgnoreCase)) return extension==".yaml" || extension==".yml" || extension==".tweak";
        if (path.StartsWith(CetMods,StringComparison.OrdinalIgnoreCase))
            return path.Substring(CetMods.Length).Contains("/") && new[]{".lua",".json",".ini",".txt",".md",".png",".jpg",".csv",".wav",".ogg"}.Contains(extension);
        if (path.StartsWith("red4ext/plugins/",StringComparison.OrdinalIgnoreCase))
            return new[]{".dll",".json",".ini",".toml",".yaml",".yml",".reds",".wav",".ogg",".txt",".md"}.Contains(extension);
        return false;
    }

    static bool Documentation(string path) {
        string extension=Path.GetExtension(path).ToLowerInvariant();
        return new[]{".txt",".md",".pdf",".png",".jpg",".jpeg",".webp",".url"}.Contains(extension) ||
            Path.GetFileName(path).Equals("LICENSE",StringComparison.OrdinalIgnoreCase);
    }

    internal static string RoutedId(string sha,List<GeneralInstallRoute> routes) {
        if(routes==null||routes.Count==0)return "file-"+sha;
        string mapping=GeneralInstallRoutes.Format(routes.OrderBy(route=>route.Folder,StringComparer.OrdinalIgnoreCase));
        return "file-"+InstalledSnapshot.Hash(System.Text.Encoding.UTF8.GetBytes(sha+"\n"+mapping.ToUpperInvariant()));
    }

    internal static FixedMod Read(ModImportItem item, FixedCatalog catalog, DownloadCache cache, Action<string> report,List<GeneralInstallRoute> routes=null) {
        routes=routes??new List<GeneralInstallRoute>();
        GeneralInstallRoutes.Validate(routes);
        if(routes.Any(route=>!route.Destination.StartsWith("%GAME%",StringComparison.Ordinal)))
            throw new IOException(GeneralStyle.T("No Cyberpunk, escolha um destino dentro da pasta do jogo.","For Cyberpunk, choose a destination inside the game folder."));
        if (item==null || !FixedData.HashValid(item.Sha256) || item.Id!="file-"+item.Sha256 ||
            ModImporter.ExcludedFromVanillaProfile(item)) throw new IOException("Arquivo importado inválido.");
        if (catalog.Packages.Any(m=>NightCityDensity.Assets(m).Any(a=>a.Sha256==item.Sha256)))
            throw new IOException("Este arquivo já corresponde a um mod da lista. Use a linha do catálogo.");
        var reserved=new HashSet<string>(catalog.Packages.SelectMany(NightCityDensity.KnownFiles).Select(f=>f.Path),StringComparer.OrdinalIgnoreCase);
        var asset=new FixedAsset{Id="local",Name=item.Name,Format="tar",Sha256=item.Sha256,Size=item.Size};
        cache.Import(item.Source,asset);
        var result=new FixedMod{Id=RoutedId(item.Sha256,routes),Name=item.Name,Version="Arquivo local",GameVersion=catalog.GameVersion,
            Dependencies=new string[0],Assets=new List<FixedAsset>{asset},Files=new List<FixedFile>()};
        using(var reader=BundledArchiveReader.Open(cache.PathFor(asset),asset.Size,asset.Sha256)) {
            var entries=reader.Files();
            var paths=entries.Select(e=>new { Entry=e, Match=GamePath.Match(e.Path) }).ToList();
            string cetFolder=null;
            if (!paths.Any(e=>e.Match.Success)) {
                var startup=entries.Where(e=>Regex.IsMatch(e.Path,@"^[^/]+/init\.lua$",RegexOptions.IgnoreCase)).ToList();
                if (startup.Count==1) cetFolder=startup[0].Path.Split('/')[0]+"/";
            }
            var destinationPaths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string packagePrefix=null;
            long unpacked=0;
            foreach(var candidate in paths) {
                var entry=candidate.Entry;
                string path=null;
                if (candidate.Match.Success) {
                    var match=candidate.Match.Groups["path"];
                    string prefix=entry.Path.Substring(0,match.Index);
                    if(packagePrefix!=null && !String.Equals(packagePrefix,prefix,StringComparison.OrdinalIgnoreCase))
                        throw new IOException("O pacote contém layouts alternativos. Escolha uma única variante antes de importar.");
                    packagePrefix=prefix;
                    path=match.Value;
                } else if (cetFolder!=null && entry.Path.StartsWith(cetFolder,StringComparison.OrdinalIgnoreCase)) {
                    path=CetMods+entry.Path;
                } else if (entry.Path.IndexOf('/')<0 && new[]{".archive",".xl"}.Contains(Path.GetExtension(entry.Path).ToLowerInvariant())) {
                    path="archive/pc/mod/"+entry.Path;
                }
                if(path!=null&&reserved.Contains(path))throw new IOException("Este pacote contém um arquivo da lista do Mod Loader: "+path+". Use o mod correspondente da lista.");
                path=GeneralInstallRoutes.Map(entry.Path,routes,path??entry.Path);
                if(!Allowed(path)) {
                    if (Documentation(entry.Path)) continue;
                    throw new IOException("O app não encontrou um destino seguro para "+entry.Path+". Importe uma única variante com a estrutura de pastas do jogo.");
                }
                if (reserved.Contains(path)) throw new IOException("Este pacote contém um arquivo da lista do Mod Loader: "+path+". Use o mod correspondente da lista.");
                if (!destinationPaths.Add(path)) throw new IOException("O pacote contém dois arquivos para o mesmo destino: "+path+".");
                unpacked+=entry.Size;
                if(destinationPaths.Count>1024 || unpacked>8L*1024*1024*1024) throw new IOException("O conteúdo do pacote excede o limite de importação.");
                result.Files.Add(new FixedFile{Asset=asset.Id,Entry=entry.RawPath,Path=path,Size=entry.Size});
            }
            if(result.Files.Count==0)throw new IOException("Nenhum arquivo de mod encontrado no pacote.");
            foreach(var file in result.Files) {
                report("Conferindo "+file.Path);
                using(var sha=SHA256.Create()) reader.CopyEntry(file.Entry,stream=>file.Sha256=Safe.Hex(sha.ComputeHash(stream)));
            }
        }
        return result;
    }

    internal static void ValidateReceipt(OnlineReceipt receipt) {
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(receipt.Mods.Count!=1 || !Regex.IsMatch(receipt.Mods[0]??"",@"^file-[a-f0-9]{64}$") ||
            receipt.BackupLayout!=null || receipt.Files.Count>1024 ||
            receipt.Files.Any(f=>f==null || String.IsNullOrEmpty(f.Path) || !Allowed(f.Path) || !seen.Add(f.Path) ||
                f.Before!=null || !FixedData.HashValid(f.After))) throw new IOException("Registro do arquivo importado inválido.");
    }
}

public sealed partial class FixedInstaller {
    internal string RemoveImported(string id,Action<string> report,bool forgetOnly=false) {
        Closed();Safe.NoLinks(Data);
        string folder=Safe.In(Data,"importados");
        if(!Directory.Exists(folder))return "Nenhuma instalação registrada para remover.";
        using(var mutex=new FileStream(Safe.In(Data,"operacao.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
            EnsureNoIncomplete();
            var found=Directory.GetFiles(folder,"*.json").Select(p=>new{Path=p,Receipt=ReadReceipt(p)})
                .Where(x=>x.Receipt.Status=="instalado"&&x.Receipt.Mods.Count==1&&x.Receipt.Mods[0]==id)
                .OrderByDescending(x=>x.Receipt.CreatedUtc).FirstOrDefault();
            if(found==null)return "Nenhuma instalação registrada para remover.";
            if(!forgetOnly)return UndoReceipt(found.Receipt,found.Path,report);
            found.Receipt.Status="desfeito";
            Save(found.Path,found.Receipt);
            return "Registro da instalação removido.";
        }
    }

    internal string InstallImported(ModImportItem item, Action<string> report, Func<ModVersionDifference,bool> chooseVersion = null,List<GeneralInstallRoute> routes=null) {
        Closed();
        FixedData.ValidateGame(game,new FixedMod[0],catalog.GameFileVersion);
        var recipe=LocalModPackage.Read(item,catalog,cache,report,routes);
        var owned=InstalledImports();
        if (owned.Contains(recipe.Id) && FixedData.Scan(recipe,game).IsCorrect) return recipe.Name+" já está instalado.";
        foreach(var file in recipe.Files) {
            string destination=Safe.In(game,file.Path);
            Safe.NoLinks(destination);
            if(File.Exists(destination)&&Safe.Hash(destination)!=file.Sha256)
                new ModVersionDifference{Name=recipe.Name,Current=ModVersionDifference.FileVersion(destination),Incoming=item.Name,CanReplace=false,Detail=GeneralStyle.T("Este importado conflita com outro arquivo do Cyberpunk. Remova a instalação anterior pelo gerenciador antes de escolher a nova, para não misturar versões. Nenhum arquivo foi substituído.","This import conflicts with another Cyberpunk file. Remove the previous installation through the manager before choosing the new one, to avoid mixing versions. No file was replaced.")+"\r\n"+file.Path}.Require(chooseVersion);
            if(File.Exists(destination)||Directory.Exists(destination))
                throw new IOException("Já existe um arquivo em "+file.Path+". O arquivo existente foi preservado.");
        }
        return InstallTransaction(new List<FixedMod>{recipe},report,-1,true);
    }

    internal HashSet<string> InstalledImports(Func<string,string> readHash=null) {
        var result=new HashSet<string>(StringComparer.Ordinal);
        string folder=Safe.In(Data,"importados");
        Safe.NoLinks(folder);
        if(!Directory.Exists(folder))return result;
        foreach(string path in Directory.GetFiles(folder,"*.json")) {
            try{
                var receipt=ReadReceiptCore(path,readHash!=null);
                if(receipt.Operation!=LocalModPackage.ReceiptOperation || receipt.Status!="instalado" || receipt.Files.Count==0)continue;
                if(receipt.Files.All(f=>f.Applied && File.Exists(Safe.In(game,f.Path)) && (readHash==null?Safe.Hash(Safe.In(game,f.Path)):readHash(Safe.In(game,f.Path)))==f.After))
                    result.Add(receipt.Mods[0]);
            }catch(IOException ex) {
                if(readHash==null)throw;
                OnlineDiagnostics.Failure("scan.import.failed",ex,"Verificar o recibo de importação.");
            }catch(UnauthorizedAccessException ex) {
                if(readHash==null)throw;
                OnlineDiagnostics.Failure("scan.import.failed",ex,"Ler o recibo de importação.");
            }
        }
        return result;
    }
}
