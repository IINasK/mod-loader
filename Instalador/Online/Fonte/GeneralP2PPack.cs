using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

internal sealed class P2PBlob { public string Hash, Extension; public long Size; public string File { get { return Hash+Extension; } } }
internal sealed class P2PBinding { public string Mod, Variant, File; public int Index; }
internal sealed class P2PManifest {
    public int Schema=1;
    public string Name;
    public GeneralModList List;
    public List<P2PBlob> Files=new List<P2PBlob>();
    public List<P2PBinding> Bindings=new List<P2PBinding>();
    public int Missing;
}
internal static class P2PPack {
    internal const long MaxFile=16L*1024*1024*1024, MaxTotal=128L*1024*1024*1024;
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool CreateHardLink(string path,string existing,IntPtr reserved);
    internal static bool HashValid(string hash){return hash!=null&&Regex.IsMatch(hash,"\\A[a-f0-9]{64}\\z");}
    internal static string Slot(GeneralModEntry mod,GeneralModVariant variant,int index){return mod.Id+"/"+variant.Id+"/"+index+"/"+variant.Sources[index];}
    static string Token(string file){return "shared:"+file;}
    static P2PBlob Blob(string path,string extension){
        Safe.NoLinks(path);var info=new FileInfo(path);
        if(!info.Exists||info.Length<=0||info.Length>MaxFile)throw new IOException("Arquivo ausente, vazio ou acima de "+GeneralStorageSize.Format(MaxFile)+": "+info.Name);
        return new P2PBlob{Hash=Safe.Hash(path),Size=info.Length,Extension=extension};
    }
    static string Extension(FixedAsset a){return a.Format=="raw"?".bin":a.Format=="zip"?".zip":a.Format=="7z"?".7z":a.Format=="rar"?".rar":".bin";}
    static void Add(P2PManifest manifest,string folder,string source,P2PBlob blob,bool immutableCache=false){
        if(manifest.Files.Any(f=>f.File==blob.File))return;
        if(manifest.Files.Count>=5000||manifest.Files.Sum(f=>f.Size)+blob.Size>MaxTotal)throw new IOException("Pacote excede 5000 arquivos ou "+GeneralStorageSize.Format(MaxTotal)+".");
        string dest=Safe.In(folder,blob.File);Safe.NoLinks(source);
        // The sender owns this immutable, send-only snapshot. Hard links avoid duplicating its cache.
        if(!immutableCache||!CreateHardLink(dest,source,IntPtr.Zero))Safe.CopyAtomic(source,dest,blob.Hash);
        if(new FileInfo(dest).Length!=blob.Size||Safe.Hash(dest)!=blob.Hash)throw new IOException("Arquivo alterado durante a preparação.");
        manifest.Files.Add(blob);
    }
    internal static P2PManifest Create(string folder,GeneralModState state,IEnumerable<string> ids,FixedCatalog catalog,DownloadCache cache,Action<string> report){
        Safe.NoLinks(folder);if(Directory.Exists(folder))throw new IOException("A pasta do pacote deve ser nova.");
        var seed=GeneralModLibrary.Seed(catalog,new ModImportItem[0]);
        var copy=Safe.Json.Deserialize<GeneralModList>(Safe.Json.Serialize(state.List));
        GeneralModLibrary.RestoreKnownMappings(copy,seed);
        var selected=new HashSet<string>(ids,StringComparer.Ordinal);
        if(selected.Count==0||selected.Any(id=>!copy.Mods.Any(m=>m.Id==id)))throw new IOException("Selecione um preset com mods para compartilhar.");
        foreach(var mod in copy.Mods.Where(m=>selected.Contains(m.Id)).ToList()){
            string choice;state.ChosenVariants.TryGetValue(mod.Id,out choice);
            var variant=mod.Variants.Single(v=>v.Id==(choice??mod.DefaultVariant));
            if(variant.CyberpunkId!=null)foreach(var dependency in FixedData.Expand(catalog,new[]{variant.CyberpunkId}))selected.Add(dependency.Id);
        }
        foreach(var id in selected.ToArray())if(!copy.Mods.Any(m=>m.Id==id)){var dependency=seed.Mods.Single(m=>m.Id==id);dependency.Category=null;copy.Mods.Add(dependency);}
        copy.Mods=copy.Mods.Where(m=>selected.Contains(m.Id)).ToList();
        var preset=state.List.Presets.FirstOrDefault(p=>p.Id==state.SelectedPreset);
        var manifest=new P2PManifest{Name=preset==null?"Mod pack":preset.Name,List=copy};
        copy.Presets=new List<GeneralModPreset>{new GeneralModPreset{Id="shared-pack",Name=manifest.Name,ModIds=copy.Mods.Select(m=>m.Id).ToList()}};
        Directory.CreateDirectory(folder);
        try{
        using(cache.HoldOperation())foreach(var mod in copy.Mods){
            report(mod.Name);string choice;state.ChosenVariants.TryGetValue(mod.Id,out choice);
            var variant=mod.Variants.Single(v=>v.Id==(choice??mod.DefaultVariant));
            mod.Variants=new List<GeneralModVariant>{variant};mod.DefaultVariant=variant.Id;
            if(variant.CyberpunkId!=null){
                var recipe=catalog.Packages.Single(m=>m.Id==variant.CyberpunkId);
                if(variant.CyberpunkVariant!=null)recipe=NightCityDensity.Resolve(recipe,variant.CyberpunkVariant);
                foreach(var asset in recipe.Assets){
                    if(!cache.Contains(asset)){manifest.Missing++;continue;}
                    Add(manifest,folder,cache.PathFor(asset),new P2PBlob{Hash=asset.Sha256,Size=asset.Size,Extension=Extension(asset)},true);
                }
            }else for(int index=0;index<variant.Sources.Count;index++){
                string source=variant.Sources[index],local;
                if(GeneralModDownload.IsArchivePath(source))local=source;
                else if(!state.ResolvedDownloads.TryGetValue(Slot(mod,variant,index),out local))local=null;
                if(local==null||!File.Exists(local)){
                    if(GeneralModDownload.IsArchivePath(source))throw new IOException(mod.Name+": o arquivo original não está disponível. Associe o ZIP/RAR/7z novamente.");
                    manifest.Missing++;continue;
                }
                var blob=Blob(local,Path.GetExtension(local).ToLowerInvariant());
                if(!new[]{".zip",".rar",".7z"}.Contains(blob.Extension))throw new IOException("Somente arquivos ZIP, RAR ou 7z podem ser compartilhados como mods personalizados.");
                Add(manifest,folder,local,blob);manifest.Bindings.Add(new P2PBinding{Mod=mod.Id,Variant=variant.Id,Index=index,File=blob.File});
                if(GeneralModDownload.IsArchivePath(source))variant.Sources[index]=Token(blob.File);
            }
            foreach(var v in mod.Variants){v.CyberpunkId=null;v.CyberpunkVariant=null;}
        }
        Safe.WriteJson(Safe.In(folder,"pack.json"),manifest);return manifest;
        }catch{try{DeleteSnapshot(folder);}catch(Exception){}throw;}
    }
    internal static void DeleteSnapshot(string folder){
        Safe.NoLinks(folder);if(!Directory.Exists(folder))return;
        var todo=new Stack<string>();todo.Push(folder);
        while(todo.Count>0){string path=todo.Pop();Safe.NoLinks(path);foreach(string file in Directory.GetFiles(path))Safe.NoLinks(file);foreach(string dir in Directory.GetDirectories(path))todo.Push(dir);}
        for(int attempt=0;;attempt++){
            try{Directory.Delete(folder,true);break;}
            catch(IOException){if(attempt>=30)throw;Thread.Sleep(100);Safe.NoLinks(folder);if(!Directory.Exists(folder))break;}
        }
    }
    internal static P2PManifest Read(string folder,string expectedHash){
        string path=Safe.In(folder,"pack.json");
        if(!HashValid(expectedHash)||!File.Exists(path))throw new IOException("O manifesto ainda não chegou ou difere do convite.");
        byte[] bytes;
        using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){
            if(file.Length>8000000)throw new IOException("Manifesto maior que 8 MB.");
            using(var buffer=new MemoryStream()){file.CopyTo(buffer);bytes=buffer.ToArray();}
        }
        using(var sha=SHA256.Create())if(Safe.Hex(sha.ComputeHash(bytes))!=expectedHash)throw new IOException("O manifesto difere do convite.");
        P2PManifest m;
        try{m=Safe.Json.Deserialize<P2PManifest>(Encoding.UTF8.GetString(bytes));}catch(ArgumentException ex){throw new IOException("Manifesto inválido.",ex);}
        if(m==null||m.Schema!=1||m.List==null||m.List.Mods==null||m.Files==null||m.Bindings==null||m.Files.Count>5000||m.Bindings.Count>32000||m.List.Mods.Count>1000||String.IsNullOrWhiteSpace(m.Name)||m.Name.Length>300)throw new IOException("Pacote compartilhado inválido.");
        var files=new HashSet<string>(StringComparer.Ordinal);long total=0;
        foreach(var f in m.Files){
            if(f==null||!HashValid(f.Hash)||!new[]{".zip",".rar",".7z",".bin"}.Contains(f.Extension)||f.Size<=0||f.Size>MaxFile||!files.Add(f.File)||(total+=f.Size)>MaxTotal)throw new IOException("Arquivo inválido no manifesto.");
        }
        var bindings=new HashSet<string>(StringComparer.Ordinal);
        foreach(var b in m.Bindings){
            if(b==null||!files.Contains(b.File)||b.File.EndsWith(".bin",StringComparison.Ordinal)||!bindings.Add(b.Mod+"/"+b.Variant+"/"+b.Index))throw new IOException("Associação duplicada ou inválida.");
            var mod=m.List.Mods.SingleOrDefault(x=>x!=null&&x.Id==b.Mod);
            var v=mod==null||mod.Variants==null?null:mod.Variants.SingleOrDefault(x=>x!=null&&x.Id==b.Variant);
            if(v==null||v.Sources==null||b.Index<0||b.Index>=v.Sources.Count)throw new IOException("Associação sem mod ou fonte.");
        }
        foreach(var mod in m.List.Mods){
            if(mod==null||mod.Variants==null)throw new IOException("Mod inválido no pacote.");
            foreach(var v in mod.Variants){
                if(v==null||v.Sources==null)throw new IOException("Variante inválida no pacote.");
                for(int i=0;i<v.Sources.Count;i++){
                    string source=v.Sources[i];var binding=m.Bindings.SingleOrDefault(b=>b.Mod==mod.Id&&b.Variant==v.Id&&b.Index==i);
                    if(binding!=null&&source==Token(binding.File))continue;
                    if(!GeneralModDownload.IsWebSource(source))throw new IOException("Caminhos do computador do remetente não são aceitos.");
                    GeneralModLibrary.ValidateSource(source);
                }
            }
        }
        return m;
    }
    internal static GeneralModList Receive(string folder,string expectedHash,string privateFolder,FixedCatalog catalog,DownloadCache cache,Action<string> report){
        var m=Read(folder,expectedHash);Safe.NoLinks(privateFolder);
        // Rewrite only transport tokens; recipe URLs must remain byte-for-byte unchanged.
        foreach(var b in m.Bindings){
            var v=m.List.Mods.Single(x=>x.Id==b.Mod).Variants.Single(x=>x.Id==b.Variant);
            if(v.Sources[b.Index]==Token(b.File))v.Sources[b.Index]=Safe.In(privateFolder,b.File);
        }
        string validation=Safe.In(privateFolder,"list.json");
        Safe.WriteJson(validation,m.List);
        var list=new GeneralModLibrary(Safe.In(privateFolder,"unused-state.json")).Import(validation);
        GeneralModLibrary.RestoreKnownMappings(list,GeneralModLibrary.Seed(catalog,new ModImportItem[0]));
        var allowed=list.Mods.SelectMany(mod=>mod.Variants).Where(v=>v.CyberpunkId!=null).SelectMany(v=>{
            var recipe=catalog.Packages.Single(x=>x.Id==v.CyberpunkId);
            return v.CyberpunkVariant==null?recipe.Assets:NightCityDensity.Resolve(recipe,v.CyberpunkVariant).Assets;
        }).ToList();
        foreach(var b in m.Bindings)if(list.Mods.Single(x=>x.Id==b.Mod).Variants.Single(x=>x.Id==b.Variant).CyberpunkId!=null)throw new IOException("O pacote tentou substituir uma receita conhecida por um arquivo personalizado.");
        long need=0;
        foreach(var f in m.Files){
            string source=Safe.In(folder,f.File);report(f.File);
            if(!File.Exists(source)||new FileInfo(source).Length!=f.Size||Safe.Hash(source)!=f.Hash)throw new IOException("Transferência incompleta ou arquivo diferente: "+f.File);
            if(!allowed.Any(a=>a.Sha256==f.Hash&&a.Size==f.Size)&&!m.Bindings.Any(b=>b.File==f.File))throw new IOException("Arquivo sem vínculo com a lista.");
            need+=f.Size;
        }
        Directory.CreateDirectory(privateFolder);
        if(new DriveInfo(Path.GetPathRoot(privateFolder)).AvailableFreeSpace<need+100L*1024*1024)throw new IOException("Espaço insuficiente para conferir a cópia recebida.");
        foreach(var f in m.Files){
            string source=Safe.In(folder,f.File);var asset=allowed.FirstOrDefault(a=>a.Sha256==f.Hash&&a.Size==f.Size);
            if(asset!=null)cache.Import(source,asset);
            if(m.Bindings.Any(b=>b.File==f.File)){
                string dest=Safe.In(privateFolder,f.File);
                if(!File.Exists(dest)||new FileInfo(dest).Length!=f.Size||Safe.Hash(dest)!=f.Hash)Safe.CopyAtomic(source,dest,f.Hash);
                foreach(var b in m.Bindings.Where(b=>b.File==f.File))list.Mods.Single(x=>x.Id==b.Mod).Variants.Single(x=>x.Id==b.Variant).Sources[b.Index]=dest;
            }
        }
        // Revalidate the private copy; never install or execute from the synchronized folder.
        Safe.WriteJson(validation,list);return new GeneralModLibrary(Safe.In(privateFolder,"unused-state.json")).Import(validation);
    }
}
