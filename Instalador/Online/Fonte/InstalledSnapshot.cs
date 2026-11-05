using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

public sealed class InstalledSnapshotManifest {
    public int Schema=1;
    public string Name,Game,GameFileVersion;
    public GeneralModList List=new GeneralModList();
    public List<InstalledSnapshotFile> Files=new List<InstalledSnapshotFile>();
}
public sealed class InstalledSnapshotFile {
    public string Path,Sha256,Kind,Section,Content;
    public long Size;
    public List<string> Owners=new List<string>();
}
public static class InstalledSnapshot {
    public const int MaxFiles=10000;
    public const long MaxFileBytes=16L*1024*1024*1024,MaxTotalBytes=128L*1024*1024*1024;
    static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
    public static string Hash(byte[] bytes){using(var sha=SHA256.Create())return Safe.Hex(sha.ComputeHash(bytes));}
    public static string Entry(InstalledSnapshotFile file){return "files/"+file.Sha256;}
    public static string Owner(GeneralModEntry mod,GeneralModVariant variant){return mod.Id+"/"+variant.Id;}
    static T Clone<T>(T value){return Safe.Json.Deserialize<T>(Safe.Json.Serialize(value));}
    static string Selected(GeneralModState state,GeneralModEntry mod){string id;return state.ChosenVariants.TryGetValue(mod.Id,out id)?id:mod.DefaultVariant;}
    static bool IsCyberpunk(string root){return File.Exists(Safe.In(root,"bin/x64/Cyberpunk2077.exe"));}
    internal static bool SafeGenericPath(string path) {
        Safe.In(System.IO.Path.GetTempPath(),path);
        return !Regex.IsMatch(path,@"(^|/)(?:\.git|\.codex|\.agents|\.aws|\.CoopModsOnline|saves?|saved games|accounts?|credentials?|cookies?|logs?|cache|profiles?|userdata)(?:/|\.|$)|(?:^|/)(?:user\.settings|loginusers\.vdf|config\.vdf|steam_api\.ini|.*\.(?:dmp|log|sav|dat|db|sqlite))$",RegexOptions.IgnoreCase);
    }
    static FixedMod FixedRecipe(FixedCatalog catalog,GeneralModVariant variant) {
        var mod=catalog.Packages.SingleOrDefault(m=>m.Id==variant.CyberpunkId);
        if(mod==null||!String.IsNullOrEmpty(mod.BlockedReason))throw new IOException("Componente desconhecido ou retido.");
        if(mod.Id==NightCityDensity.ModId) {
            if(!NightCityDensity.Supported(variant.CyberpunkVariant))throw new IOException("Densidade instalada não suportada para compartilhamento.");
            return NightCityDensity.Resolve(mod,variant.CyberpunkVariant);
        }
        if(variant.CyberpunkVariant!=null)throw new IOException("Variante fixa inválida.");
        return mod;
    }
    public static InstalledSnapshotManifest Create(string zipPath,GeneralModState state,IEnumerable<string> ids,FixedCatalog catalog,string appData,bool includeConfigs,Action<string> report) {
        if(state==null||state.List==null||ids==null||String.IsNullOrWhiteSpace(state.Destination))throw new IOException("Escolha uma pasta de instalação e um preset.");
        string root=Path.GetFullPath(state.Destination);Safe.NoLinks(root);
        if(!Directory.Exists(root))throw new IOException("Pasta de instalação ausente.");
        bool cyberpunk=IsCyberpunk(root);
        var selected=new HashSet<string>(ids,StringComparer.Ordinal);
        if(selected.Count==0||selected.Any(id=>!state.List.Mods.Any(m=>m.Id==id)))throw new IOException("Selecione um preset com mods instalados.");
        var list=new GeneralModList{UncategorizedName=state.List.UncategorizedName,UncategorizedDescription=state.List.UncategorizedDescription};
        var manifest=new InstalledSnapshotManifest{Name=state.List.Presets.FirstOrDefault(p=>p.Id==state.SelectedPreset)==null?"Pacote instalado":state.List.Presets.First(p=>p.Id==state.SelectedPreset).Name,Game=cyberpunk?"cyberpunk2077":"generic",GameFileVersion=cyberpunk?catalog.GameFileVersion:null,List=list};
        var seed=GeneralModLibrary.Seed(catalog,new ModImportItem[0]);
        var entries=state.List.Mods.Where(m=>selected.Contains(m.Id)).Select(Clone).ToList();
        if(cyberpunk) {
            var fixedIds=entries.Select(m=>m.Variants.Single(v=>v.Id==Selected(state,m)).CyberpunkId).Where(id=>id!=null).ToList();
            foreach(var dependency in FixedData.Expand(catalog,fixedIds)) {
                if(entries.Any(m=>m.Variants.Single(v=>v.Id==Selected(state,m)).CyberpunkId==dependency.Id))continue;
                entries.Add(Clone(seed.Mods.Single(m=>m.Id==dependency.Id)));
            }
        }
        var fixedEngine=cyberpunk?new FixedInstaller(catalog,root):null;
        var genericEngine=cyberpunk?null:new GeneralModInstaller(root,Path.Combine(appData,"Instalacoes"));
        var receivedEngine=new InstalledSnapshotInstaller(root,appData,catalog);
        foreach(var mod in entries) {
            var variant=mod.Variants.Single(v=>v.Id==Selected(state,mod));
            if(cyberpunk && variant.CyberpunkId==NightCityDensity.ModId) {
                var installed=NightCityDensity.Installed(catalog.Packages.Single(m=>m.Id==variant.CyberpunkId),root);
                var density=NightCityDensity.Read(installed,root);
                if(!NightCityDensity.Supported(density.Id))throw new IOException(mod.Name+": densidade instalada personalizada não suportada.");
                variant=mod.Variants.Single(v=>v.CyberpunkVariant==density.Id);
            }
            string work=Owner(mod,variant);
            var originalSources=variant.Sources.ToList();
            List<InstalledSnapshotFile> files=new List<InstalledSnapshotFile>();
            string snapshotId;
            if(state.SnapshotReceipts!=null&&state.SnapshotReceipts.TryGetValue(work,out snapshotId))files=receivedEngine.ReadInstalledFiles(snapshotId,work);
            else if(variant.CyberpunkId!=null) {
                if(!cyberpunk)throw new IOException(mod.Name+": o destino não é Cyberpunk 2077.");
                var recipe=FixedRecipe(catalog,variant);
                foreach(var file in recipe.Files) {
                    string installed=Safe.In(root,file.Path);
                    if(file.UserData) {
                        if(includeConfigs&&File.Exists(installed))files.Add(Config(file.Path,"json",null,InstalledSnapshotConfig.Json(InstalledSnapshotConfig.Read(installed))));
                        continue;
                    }
                    if(!File.Exists(installed)||new FileInfo(installed).Length!=file.Size||Safe.Hash(installed)!=file.Sha256)throw new IOException(mod.Name+": arquivo instalado ausente ou alterado: "+file.Path);
                    files.Add(new InstalledSnapshotFile{Path=file.Path,Sha256=file.Sha256,Size=file.Size,Kind="file"});
                }
                if(includeConfigs&&catalog.Defaults!=null)foreach(var config in catalog.Defaults.Where(d=>d.ModId==recipe.Id)) {
                    string path=Safe.In(root,config.Path);if(!File.Exists(path))continue;
                    string content=config.Format=="json"?InstalledSnapshotConfig.Json(InstalledSnapshotConfig.Read(path)):InstalledSnapshotConfig.Section(InstalledSnapshotConfig.Read(path),config.Section);
                    if(content!=null)files.Add(Config(config.Path,config.Format,config.Section,content));
                }
            }else {
                for(int i=0;i<originalSources.Count;i++) {
                    string key=work+"/"+i+"/"+originalSources[i];
                    if(cyberpunk) {
                        string id;
                        if(state.CyberpunkImports==null||!state.CyberpunkImports.TryGetValue(key,out id)) {
                            id=mod.Id.StartsWith("file-",StringComparison.Ordinal)?mod.Id:null;
                            if(id==null)throw new IOException(mod.Name+": recibo instalado não encontrado.");
                        }
                        files.AddRange(fixedEngine.ExportSnapshotImportedFiles(id));
                    }else files.AddRange(genericEngine.ExportSnapshotFiles(key));
                }
            }
            mod.Variants=new List<GeneralModVariant>{variant};mod.DefaultVariant=variant.Id;
            if(variant.CyberpunkId==null)variant.Sources=new List<string>{"installed://"+mod.Id+"/"+variant.Id};
            list.Mods.Add(mod);
            foreach(var file in files) {
                if(!includeConfigs&&file.Kind!="file")continue;
                file.Owners=new List<string>{work};
                var same=manifest.Files.FirstOrDefault(f=>String.Equals(f.Path,file.Path,StringComparison.OrdinalIgnoreCase)&&f.Section==file.Section);
                if(same==null)manifest.Files.Add(file);
                else {if(same.Kind!=file.Kind||same.Size!=file.Size||same.Sha256!=file.Sha256)throw new IOException("Dois mods usam conteúdo diferente em "+file.Path);if(!same.Owners.Contains(work))same.Owners.Add(work);}
            }
            if(!manifest.Files.Any(f=>f.Owners.Contains(work)&&f.Kind=="file"))throw new IOException(mod.Name+": nenhum arquivo instalado foi encontrado.");
        }
        list.Categories=state.List.Categories.Where(c=>list.Mods.Any(m=>m.Category==c)).Concat(list.Mods.Where(m=>!String.IsNullOrWhiteSpace(m.Category)).Select(m=>m.Category)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach(string category in list.Categories) {string description;if(state.List.CategoryDescriptions.TryGetValue(category,out description))list.CategoryDescriptions[category]=description;}
        list.Presets.Add(new GeneralModPreset{Id="installed",Name=manifest.Name,ModIds=list.Mods.Select(m=>m.Id).ToList()});
        Validate(manifest,catalog);
        string output=Path.GetFullPath(zipPath);Safe.NoLinks(output);
        bool created=false;
        try {
            using(var stream=new FileStream(output,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None)) {
                created=true;
                using(var archive=new ZipArchive(stream,ZipArchiveMode.Create,true)) {
                    var metadata=archive.CreateEntry("manifest.json",CompressionLevel.Optimal);
                    using(var target=metadata.Open()){byte[] bytes=Utf8.GetBytes(Safe.Json.Serialize(manifest));target.Write(bytes,0,bytes.Length);}
                    foreach(var file in manifest.Files.Where(f=>f.Kind=="file").GroupBy(f=>f.Sha256).Select(g=>g.First())) {
                        if(report!=null)report("Preparando "+file.Path);
                        string source=Safe.In(root,file.Path);
                        using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read))
                        using(var sha=SHA256.Create())
                        using(var target=archive.CreateEntry(Entry(file),CompressionLevel.Fastest).Open()) {
                            ValidateCustomData(manifest,file,input);input.Position=0;
                            byte[] buffer=new byte[1024*1024];long total=0;int count;
                            while((count=input.Read(buffer,0,buffer.Length))>0){total+=count;if(total>file.Size)throw new IOException("Arquivo mudou durante a exportação: "+file.Path);sha.TransformBlock(buffer,0,count,null,0);target.Write(buffer,0,count);}
                            sha.TransformFinalBlock(new byte[0],0,0);
                            if(total!=file.Size||Safe.Hex(sha.Hash)!=file.Sha256)throw new IOException("Arquivo mudou durante a exportação: "+file.Path);
                        }
                    }
                }
                stream.Flush(true);
            }
            return manifest;
        }catch {if(created){Safe.NoLinks(output);File.Delete(output);}throw;}
    }
    static InstalledSnapshotFile Config(string path,string kind,string section,string content){byte[] bytes=Utf8.GetBytes(content);return new InstalledSnapshotFile{Path=path,Kind=kind,Section=section,Content=content,Size=bytes.Length,Sha256=Hash(bytes)};}
    public static void Validate(InstalledSnapshotManifest manifest,FixedCatalog catalog) {
        if(manifest==null||manifest.Schema!=1||manifest.List==null||manifest.List.Mods==null||manifest.Files==null||manifest.Files.Count==0||manifest.Files.Count>MaxFiles||String.IsNullOrWhiteSpace(manifest.Name)||manifest.Name.Length>300||!(manifest.Game=="cyberpunk2077"||manifest.Game=="generic")||(manifest.Game=="cyberpunk2077"&&manifest.GameFileVersion!=catalog.GameFileVersion)||(manifest.Game=="generic"&&manifest.GameFileVersion!=null))throw new IOException("Manifesto de arquivos instalados inválido.");
        GeneralModLibrary.ValidateList(manifest.List,true);
        var owners=new Dictionary<string,GeneralModVariant>(StringComparer.Ordinal);
        var recipes=new Dictionary<string,FixedMod>(StringComparer.Ordinal);
        var ids=new HashSet<string>(StringComparer.Ordinal);
        if(manifest.List.Mods.Count==0||manifest.List.Mods.Count>1000)throw new IOException("Lista de instalação inválida.");
        foreach(var mod in manifest.List.Mods) {
            if(mod==null||!Regex.IsMatch(mod.Id??"",@"\A[A-Za-z0-9_-]{1,120}\z")||!ids.Add(mod.Id)||String.IsNullOrWhiteSpace(mod.Name)||mod.Name.Length>300||mod.Variants==null||mod.Variants.Count!=1)throw new IOException("Mod instalado inválido.");
            var variant=mod.Variants[0];
            if(variant==null||!Regex.IsMatch(variant.Id??"",@"\A[A-Za-z0-9_-]{1,120}\z")||variant.Id!=mod.DefaultVariant||variant.Sources==null||variant.Sources.Count==0||variant.Sources.Count>32)throw new IOException("Variante instalada inválida.");
            string owner=Owner(mod,variant);owners.Add(owner,variant);
            if(variant.CyberpunkId!=null) {
                if(manifest.Game!="cyberpunk2077")throw new IOException("Receita Cyberpunk em destino genérico.");
                var recipe=FixedRecipe(catalog,variant);recipes.Add(owner,recipe);
                var seed=GeneralModLibrary.Seed(catalog,new ModImportItem[0]).Mods.Single(m=>m.Id==recipe.Id).Variants.Single(v=>v.CyberpunkVariant==variant.CyberpunkVariant);
                if(!seed.Sources.SequenceEqual(variant.Sources,StringComparer.Ordinal))throw new IOException("Fontes fixas alteradas no pacote.");
            }else if(variant.CyberpunkVariant!=null||variant.Sources.Count!=1||variant.Sources[0]!="installed://"+mod.Id+"/"+variant.Id)throw new IOException("O pacote contém caminhos privados de origem.");
        }
        foreach(var recipe in recipes.Values)foreach(string dependency in recipe.Dependencies)if(!recipes.Values.Any(m=>m.Id==dependency))throw new IOException("Dependência não incluída: "+dependency);
        var keys=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var regular=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0,configs=0;
        var reserved=new HashSet<string>(catalog.Packages.SelectMany(NightCityDensity.KnownFiles).Select(f=>f.Path),StringComparer.OrdinalIgnoreCase);
        foreach(var file in manifest.Files) {
            if(file==null||file.Size<0||file.Size>MaxFileBytes||!FixedData.HashValid(file.Sha256)||file.Owners==null||file.Owners.Count==0||file.Owners.Count>1000||file.Owners.Distinct(StringComparer.Ordinal).Count()!=file.Owners.Count||file.Owners.Any(o=>!owners.ContainsKey(o))||!keys.Add(file.Path+"\n"+file.Section))throw new IOException("Arquivo instalado inválido ou repetido.");
            Safe.In(Path.GetTempPath(),file.Path);total+=file.Size;if(total>MaxTotalBytes)throw new IOException("Pacote instalado grande demais.");
            if(file.Kind=="file") {
                if(file.Content!=null||file.Section!=null||!regular.Add(file.Path))throw new IOException("Arquivo binário contém metadados inválidos.");
                foreach(string owner in file.Owners) {
                    FixedMod recipe;
                    if(recipes.TryGetValue(owner,out recipe)) {
                        if(!recipe.Files.Any(f=>!f.UserData&&String.Equals(f.Path,file.Path,StringComparison.Ordinal)&&f.Size==file.Size&&f.Sha256==file.Sha256))throw new IOException("Arquivo diverge da receita fixada: "+file.Path);
                    }else if(!SafeGenericPath(file.Path)||(manifest.Game=="cyberpunk2077"&&(!LocalModPackage.Allowed(file.Path)||reserved.Contains(file.Path))))throw new IOException("Destino personalizado não autorizado: "+file.Path);
                }
            }else {
                InstalledSnapshotConfig.Validate(file);configs+=file.Size;if(configs>1048576)throw new IOException("Configurações grandes demais.");
                foreach(string owner in file.Owners) {
                    FixedMod recipe;
                    if(!recipes.TryGetValue(owner,out recipe)||!(recipe.Files.Any(f=>f.UserData&&f.Path==file.Path&&file.Kind=="json")||(catalog.Defaults!=null&&catalog.Defaults.Any(d=>d.ModId==recipe.Id&&d.Path==file.Path&&d.Format==file.Kind&&d.Section==file.Section))))throw new IOException("Configuração fora da lista autorizada: "+file.Path);
                }
            }
        }
        foreach(var file in manifest.Files) {
            if(file.Kind!="file"&&regular.Contains(file.Path))throw new IOException("Configuração colide com arquivo fixado.");
            for(int slash=file.Path.LastIndexOf('/');slash>=0;slash=file.Path.LastIndexOf('/',slash-1))if(manifest.Files.Any(f=>String.Equals(f.Path,file.Path.Substring(0,slash),StringComparison.OrdinalIgnoreCase)))throw new IOException("Arquivo ocupa diretório de outro arquivo.");
        }
        foreach(var group in manifest.Files.Where(f=>f.Kind=="file").GroupBy(f=>f.Sha256))if(group.Select(f=>f.Size).Distinct().Count()!=1)throw new IOException("Tamanhos divergentes para o mesmo conteúdo.");
        foreach(string owner in owners.Keys) {
            if(!manifest.Files.Any(f=>f.Kind=="file"&&f.Owners.Contains(owner)))throw new IOException("Mod sem arquivos instalados.");
            FixedMod recipe;if(recipes.TryGetValue(owner,out recipe))foreach(var expected in recipe.Files.Where(f=>!f.UserData))if(!manifest.Files.Any(f=>f.Kind=="file"&&f.Owners.Contains(owner)&&f.Path==expected.Path&&f.Sha256==expected.Sha256&&f.Size==expected.Size))throw new IOException("Arquivo fixado ausente: "+expected.Path);
        }
    }
    public static InstalledSnapshotManifest Read(string zipPath,string expectedHash,FixedCatalog catalog) {
        if(!FixedData.HashValid(expectedHash))throw new IOException("Hash de pacote inválido.");Safe.NoLinks(zipPath);
        using(var stream=new FileStream(zipPath,FileMode.Open,FileAccess.Read,FileShare.Read)) {
            if(stream.Length>MaxTotalBytes)throw new IOException("Pacote grande demais.");
            using(var sha=SHA256.Create())if(Safe.Hex(sha.ComputeHash(stream))!=expectedHash)throw new IOException("O pacote transferido não corresponde ao convite.");
            stream.Position=0;
            using(var zip=new ZipArchive(stream,ZipArchiveMode.Read,true)) {
                if(zip.Entries.Count>MaxFiles+1)throw new IOException("Pacote com entradas demais.");
                var entries=new Dictionary<string,ZipArchiveEntry>(StringComparer.Ordinal);
                foreach(var entry in zip.Entries)if(!entries.TryAddCompat(entry.FullName,entry))throw new IOException("Entrada repetida no pacote.");
                ZipArchiveEntry metadata;if(!entries.TryGetValue("manifest.json",out metadata)||metadata.Length>8000000)throw new IOException("Manifesto ausente ou grande demais.");
                InstalledSnapshotManifest manifest;
                using(var input=metadata.Open())manifest=Safe.Json.Deserialize<InstalledSnapshotManifest>(Utf8.GetString(ReadBytes(input,8000000)));
                Validate(manifest,catalog);
                var expected=manifest.Files.Where(f=>f.Kind=="file").GroupBy(f=>f.Sha256).Select(g=>g.First()).ToList();
                if(entries.Count!=expected.Count+1)throw new IOException("Arquivos não declarados no pacote.");
                foreach(var file in expected) {
                    ZipArchiveEntry entry;if(!entries.TryGetValue(Entry(file),out entry)||entry.Length!=file.Size)throw new IOException("Arquivo ausente ou tamanho divergente: "+file.Path);
                    using(var input=entry.Open())if(HashStream(input,file.Size)!=file.Sha256)throw new IOException("Arquivo transferido divergente: "+file.Path);
                }
                foreach(var file in manifest.Files.Where(f=>f.Kind=="file"))using(var input=entries[Entry(file)].Open())ValidateCustomData(manifest,file,input);
                return manifest;
            }
        }
    }
    static void ValidateCustomData(InstalledSnapshotManifest manifest,InstalledSnapshotFile file,Stream input) {
        bool custom=file.Owners.Any(owner=>manifest.List.Mods.Any(m=>m.Variants.Any(v=>Owner(m,v)==owner&&v.CyberpunkId==null)));
        if(!custom)return;
        string extension=System.IO.Path.GetExtension(file.Path).ToLowerInvariant();
        if(extension!=".json"&&extension!=".ini"&&extension!=".toml")return;
        if(file.Size>262144)throw new IOException("Configuração personalizada grande demais: "+file.Path);
        using(var memory=new MemoryStream()) {
            byte[] content=ReadBytes(input,262144);memory.Write(content,0,content.Length);
            string text=InstalledSnapshotConfig.Decode(memory.ToArray());
            if(extension==".json")InstalledSnapshotConfig.Object(text);
            else if(Regex.IsMatch(text,@"(?im)^\s*[^\r\n=]*(?:password|passwd|secret|credential|oauth|cookie|bearer|authorization|access.?token|refresh.?token|client.?id|api.?key|\btoken\b)[^\r\n=]*="))throw new IOException("Configuração personalizada contém dados privados: "+file.Path);
        }
    }
    internal static byte[] ReadBytes(Stream input,int maximum) {
        using(var memory=new MemoryStream()) {
            byte[] buffer=new byte[16384];int count;
            while((count=input.Read(buffer,0,buffer.Length))>0){if(memory.Length+count>maximum)throw new IOException("Entrada de dados excede o limite permitido.");memory.Write(buffer,0,count);}
            return memory.ToArray();
        }
    }
    static string HashStream(Stream input,long expected) {
        using(var sha=SHA256.Create()) {
            byte[] buffer=new byte[1024*1024];long total=0;int count;
            while((count=input.Read(buffer,0,buffer.Length))>0){total+=count;if(total>expected)throw new IOException("Entrada ZIP excede o tamanho declarado.");sha.TransformBlock(buffer,0,count,null,0);}
            if(total!=expected)throw new IOException("Entrada ZIP menor que o tamanho declarado.");sha.TransformFinalBlock(new byte[0],0,0);return Safe.Hex(sha.Hash);
        }
    }
    static bool TryAddCompat<TKey,TValue>(this Dictionary<TKey,TValue> values,TKey key,TValue value){if(values.ContainsKey(key))return false;values.Add(key,value);return true;}
}
