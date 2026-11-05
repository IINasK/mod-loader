using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

public sealed class FileApprovalRequest : IOException {
    public readonly string ModId,ModName,AssetName,AssetSha256,Entry,Path,ExpectedSha256,ActualSha256;
    public readonly long ExpectedSize,ActualSize;
    public FileApprovalRequest(FixedMod mod,FixedAsset asset,FixedFile file,long actualSize,string actualHash)
        :base("O arquivo extraído difere do catálogo.\r\nMod: "+mod.Name+"\r\nPacote: "+asset.Name+"\r\nArquivo: "+file.Path+"\r\nEsperado: "+(file.CanonicalSha256==null?file.Size:file.CanonicalSize)+" bytes; extraído: "+actualSize+" bytes.\r\nSHA-256 esperado: "+(file.CanonicalSha256??file.Sha256)+"\r\nSHA-256 extraído: "+actualHash+"\r\n\r\nInstalar esta diferença pode causar erros no mod ou no jogo.") {
        ModId=mod.Id;ModName=mod.Name;AssetName=asset.Name;AssetSha256=asset.Sha256;Entry=file.Entry;Path=file.Path;
        ExpectedSha256=file.CanonicalSha256??file.Sha256;ExpectedSize=file.CanonicalSha256==null?file.Size:file.CanonicalSize;ActualSha256=actualHash;ActualSize=actualSize;
    }
}

public sealed class ApprovedFileIdentity {
    public string CatalogId,ModId,AssetSha256,Entry,Path,ExpectedSha256,ActualSha256,ApprovedUtc;
    public long ExpectedSize,ActualSize;
}
public sealed class ApprovedFileRegistry {
    public int Schema=1;
    public string CatalogId,GameRoot;
    public List<ApprovedFileIdentity> Files=new List<ApprovedFileIdentity>();
}

public static class UserFileApprovals {
    const long MaxFileBytes=8L*1024*1024*1024;
    const int MaxRegistryBytes=1048576,MaxRecords=256;
    const string Relative=".CoopModsOnline/local-approvals/identidades.dat";
    static readonly byte[] Entropy=Encoding.UTF8.GetBytes("CyberpunkCoop.FileApprovals.v1");
    static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
    static string Root(string game) {
        if(String.IsNullOrWhiteSpace(game)||!System.IO.Path.IsPathRooted(game))throw new IOException("A pasta do jogo deve ser absoluta para ler autorizações locais.");
        string root=System.IO.Path.GetFullPath(game).TrimEnd('\\');
        if(root.Length<=3||root.StartsWith(@"\\",StringComparison.Ordinal))throw new IOException("A autorização exige uma pasta local do jogo.");
        Safe.NoLinks(root);return root;
    }
    static bool Identifier(string value){return !String.IsNullOrWhiteSpace(value)&&value.Length<=300&&!value.Any(c=>c<32);}
    static void ValidateRecord(ApprovedFileIdentity f,string catalogId,string root) {
        if(f==null||f.CatalogId!=catalogId||!Identifier(f.ModId)||!FixedData.HashValid(f.AssetSha256)||!FixedData.HashValid(f.ExpectedSha256)||!FixedData.HashValid(f.ActualSha256)||f.ExpectedSize<0||f.ExpectedSize>MaxFileBytes||f.ActualSize<0||f.ActualSize>MaxFileBytes||(f.ExpectedSha256==f.ActualSha256&&f.ExpectedSize==f.ActualSize)||String.IsNullOrWhiteSpace(f.Path)||!Safe.Allowed(f.Path))throw new IOException("Registro de arquivo autorizado inválido.");
        Safe.In(root,f.Path);if(!String.IsNullOrEmpty(f.Entry))Safe.In(root,f.Entry);
        DateTime time;if(!DateTime.TryParse(f.ApprovedUtc,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.RoundtripKind,out time))throw new IOException("Data da autorização local inválida.");
    }
    static ApprovedFileRegistry Read(string root,string catalogId) {
        string file=Safe.In(root,Relative);
        if(!File.Exists(file)){if(Directory.Exists(file))throw new IOException("Uma pasta ocupa o registro de autorizações.");return new ApprovedFileRegistry{CatalogId=catalogId,GameRoot=root};}
        if(new FileInfo(file).Length>MaxRegistryBytes)throw new IOException("Registro de autorizações locais maior que o limite.");
        ApprovedFileRegistry result;
        try {
            var envelope=Safe.Json.Deserialize<ReceiptEnvelope>(File.ReadAllText(file,Utf8));
            byte[] plain=ProtectedData.Unprotect(Convert.FromBase64String(envelope.Protected),Entropy,DataProtectionScope.CurrentUser);
            if(plain.Length>MaxRegistryBytes)throw new IOException("Registro de autorizações locais maior que o limite.");
            result=Safe.Json.Deserialize<ApprovedFileRegistry>(Utf8.GetString(plain));
        }catch(Exception ex){if(ex is OutOfMemoryException)throw;throw new IOException("As autorizações de arquivos estão corrompidas ou pertencem a outra conta do Windows. Nenhuma diferença será aceita.",ex);}
        if(result==null||result.Schema!=1||!Identifier(result.CatalogId)||(catalogId!=null&&result.CatalogId!=catalogId)||!String.Equals(result.GameRoot,root,StringComparison.OrdinalIgnoreCase)||result.Files==null||result.Files.Count>MaxRecords)throw new IOException("As autorizações não correspondem a este jogo ou catálogo.");
        var identities=new HashSet<string>(StringComparer.Ordinal);
        foreach(var f in result.Files){ValidateRecord(f,result.CatalogId,root);if(!identities.Add(f.ModId+"\n"+f.Path+"\n"+f.ActualSha256))throw new IOException("Autorização de arquivo duplicada.");}
        return result;
    }
    static string CanonicalHash(FixedFile file){return file.CanonicalSha256??file.Sha256;}
    static long CanonicalBytes(FixedFile file){return file.CanonicalSha256==null?file.Size:file.CanonicalSize;}
    static bool Matches(FixedMod mod,FixedFile file,ApprovedFileIdentity record) {
        return record.ModId==mod.Id&&record.Path==file.Path&&record.Entry==file.Entry&&record.ExpectedSha256==CanonicalHash(file)&&record.ExpectedSize==CanonicalBytes(file)&&mod.Assets.Any(a=>a.Id==file.Asset&&a.Sha256==record.AssetSha256);
    }
    static void ValidateBound(FixedMod mod,ApprovedFileIdentity record) {
        var file=mod.Files.SingleOrDefault(f=>f.Path==record.Path);
        if(mod.Immutable||mod.DensityProfiles!=null||file==null||file.UserData||file.Patch!=null||!Matches(mod,file,record))throw new IOException("A autorização de "+mod.Name+" pertence a outra receita ou a um arquivo protegido.");
    }
    static FixedMod Overlay(FixedMod mod,ApprovedFileRegistry registry) {
        var records=registry.Files.Where(r=>r.ModId==mod.Id).ToList();
        if(records.Count==0&&!mod.Files.Any(f=>f.CanonicalSha256!=null))return mod;
        foreach(var record in records)ValidateBound(mod,record);
        var copy=Safe.Json.Deserialize<FixedMod>(Safe.Json.Serialize(mod));
        foreach(var file in copy.Files){
            string canonicalHash=CanonicalHash(file);long canonicalSize=CanonicalBytes(file);
            file.Sha256=canonicalHash;file.Size=canonicalSize;file.CanonicalSha256=null;file.CanonicalSize=0;file.PriorApprovedHashes=null;
            var versions=records.Where(r=>r.Path==file.Path).ToList();if(versions.Count==0)continue;
            var latest=versions.Last();file.CanonicalSha256=canonicalHash;file.CanonicalSize=canonicalSize;file.Sha256=latest.ActualSha256;file.Size=latest.ActualSize;file.PriorApprovedHashes=versions.Select(r=>r.ActualSha256).Distinct().ToArray();
        }
        return copy;
    }
    public static FixedMod Resolve(FixedMod mod,string game,string catalogId=null) {
        if(mod==null)throw new ArgumentNullException("mod");
        string root=Root(game);return Overlay(mod,Read(root,catalogId));
    }
    public static FixedCatalog ResolveCatalog(FixedCatalog catalog,string game) {
        if(catalog==null)throw new ArgumentNullException("catalog");
        string root=Root(game);var registry=Read(root,catalog.Id);
        if(registry.Files.Count==0&&!catalog.Packages.Any(m=>m.Files.Any(f=>f.CanonicalSha256!=null)))return catalog;
        foreach(var record in registry.Files){var mod=catalog.Packages.SingleOrDefault(m=>m.Id==record.ModId);if(mod==null)throw new IOException("Uma autorização local aponta para um mod ausente do catálogo.");ValidateBound(mod,record);}
        var copy=Safe.Json.Deserialize<FixedCatalog>(Safe.Json.Serialize(catalog));copy.Packages=copy.Packages.Select(m=>Overlay(m,registry)).ToList();return copy;
    }
    public static void Approve(FixedCatalog catalog,string game,FileApprovalRequest request) {
        if(catalog==null||request==null)throw new ArgumentNullException("request");
        if(!Identifier(catalog.Id))throw new IOException("Catálogo inválido para autorizar uma diferença.");
        string root=Root(game);
        if(!File.Exists(Safe.In(root,"bin/x64/Cyberpunk2077.exe")))throw new IOException("Escolha a pasta do jogo antes de autorizar uma diferença.");
        foreach(var process in Process.GetProcessesByName("Cyberpunk2077")){process.Dispose();throw new IOException("Feche o jogo antes de autorizar uma diferença.");}
        var mod=catalog.Packages.SingleOrDefault(m=>m.Id==request.ModId);
        if(mod==null||mod.Immutable||mod.DensityProfiles!=null||!String.IsNullOrEmpty(mod.BlockedReason))throw new IOException("Este componente não permite exceções de arquivo.");
        var file=mod.Files.SingleOrDefault(f=>f.Path==request.Path);
        if(file==null||file.UserData||file.Patch!=null||request.ExpectedSha256!=CanonicalHash(file)||request.ExpectedSize!=CanonicalBytes(file)||request.Entry!=file.Entry||request.ModName!=mod.Name)throw new IOException("A diferença não corresponde ao arquivo fixado.");
        var asset=mod.Assets.SingleOrDefault(a=>a.Id==file.Asset&&a.Sha256==request.AssetSha256&&a.Name==request.AssetName);
        if(asset==null)throw new IOException("O pacote original não corresponde à receita autorizada.");
        var record=new ApprovedFileIdentity{CatalogId=catalog.Id,ModId=mod.Id,AssetSha256=asset.Sha256,Entry=file.Entry,Path=file.Path,ExpectedSha256=CanonicalHash(file),ExpectedSize=CanonicalBytes(file),ActualSha256=request.ActualSha256,ActualSize=request.ActualSize,ApprovedUtc=DateTime.UtcNow.ToString("o")};
        ValidateRecord(record,catalog.Id,root);
        string target=Safe.In(root,file.Path);
        if(File.Exists(target)||Directory.Exists(target))throw new IOException("O destino já existe. A exceção não pode substituir arquivos instalados; eles foram preservados.");
        string registryPath=Safe.In(root,Relative),data=System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(registryPath));Directory.CreateDirectory(data);Safe.NoLinks(data);
        using(var operation=new FileStream(Safe.In(data,"operacao.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
            var registry=Read(root,catalog.Id);
            if(!Identifier(registry.CatalogId))registry.CatalogId=catalog.Id;
            registry.Files.RemoveAll(r=>r.ModId==record.ModId&&r.Path==record.Path&&r.ActualSha256==record.ActualSha256);
            if(registry.Files.Count>=MaxRecords)throw new IOException("O limite de 256 autorizações locais foi atingido.");
            registry.Files.Add(record);
            byte[] plain=Utf8.GetBytes(Safe.Json.Serialize(registry));if(plain.Length>MaxRegistryBytes)throw new IOException("Registro de autorizações locais maior que o limite.");
            var envelope=new ReceiptEnvelope{Protected=Convert.ToBase64String(ProtectedData.Protect(plain,Entropy,DataProtectionScope.CurrentUser))};byte[] bytes=Utf8.GetBytes(Safe.Json.Serialize(envelope));if(bytes.Length>MaxRegistryBytes)throw new IOException("Registro protegido maior que o limite.");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(registryPath));Safe.NoLinks(registryPath);Safe.NoLinks(target);
            if(File.Exists(target)||Directory.Exists(target))throw new IOException("O destino mudou durante a autorização; arquivo preservado.");
            string temp=registryPath+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {
                using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){output.Write(bytes,0,bytes.Length);output.Flush(true);}
                Safe.NoLinks(registryPath);if(File.Exists(registryPath))File.Replace(temp,registryPath,null);else File.Move(temp,registryPath);
            }finally{if(File.Exists(temp)){Safe.NoLinks(temp);File.Delete(temp);}}
            OnlineDiagnostics.Record("file-approval.accepted",mod.Id,"Usuário autorizou uma identidade divergente para instalação em destino ausente.",new{Asset=asset.Name,Entry=file.Entry,Path=file.Path,ExpectedBytes=record.ExpectedSize,ActualBytes=record.ActualSize,ExpectedSha256=record.ExpectedSha256,ActualSha256=record.ActualSha256});
        }
    }
    public static string GetWarning(FixedMod mod,string game) {
        if(mod==null)throw new ArgumentNullException("mod");string root=Root(game);var registry=Read(root,null);var records=registry.Files.Where(r=>r.ModId==mod.Id).ToList();
        foreach(var record in records)ValidateBound(mod,record);
        var present=new List<string>();
        foreach(var group in records.GroupBy(r=>r.Path)){
            string path=Safe.In(root,group.Key);if(!File.Exists(path))continue;long size=new FileInfo(path).Length;var possible=group.Where(r=>r.ActualSize==size).ToList();if(possible.Count==0)continue;string hash=Safe.Hash(path);
            if(possible.Any(r=>r.ActualSha256==hash))present.Add(group.Key);
        }
        return present.Count==0?null:"Instalado com autorização manual: "+present.Count+" arquivo(s) diferem do catálogo e podem causar erros. Remover selecionados permite retirar os arquivos registrados.\r\n"+String.Join("\r\n",present);
    }
}
