using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

public sealed class FixedCatalog { public int Schema; public string Id,GameVersion,GameFileVersion; public List<FixedMod> Packages; public List<FixedDefault> Defaults; public string[] Excluded; }
public sealed class FixedMod {
    public string Id,Name,Version,Profile,GameVersion,Warning,Description,ThirdPerson,ThirdPersonNotes,ThirdPersonSource,BlockedReason;
    public string PhantomLibertyStatus,PhantomLibertyNotes,PhantomLibertySource;
    public bool RequiresPL,RequiresRedmod,Immutable,ManualSelection;
    public string[] Dependencies;
    public string[] IncompatiblePaths,ExclusiveCodeDirectories;
    public int NexusModId,NexusFileId;
    public List<FixedAsset> Assets;
    public List<FixedFile> Files;
    public List<FixedDensityProfile> DensityProfiles;
}
public sealed class FixedAsset { public string Id,Name,Sha256,Format,Url; public long Size; public int ModId,FileId; }
public sealed class FixedFile { public string Asset,Entry,Path,Sha256; public long Size; public bool UserData; public FixedTextPatch Patch; public string CanonicalSha256; public long CanonicalSize; public string[] PriorApprovedHashes; }
public sealed class ModScan {
    public string Id,State,Detail; public int Correct,Missing,Different,Original;
    public bool IsCorrect { get { return State=="Instalado correto"; } }
    public bool IsConflict { get { return Different>0 || State=="Não verificável"; } }
}
public sealed class OwnedChange { public string Path,Before,After; public bool Applied,UndoStarted,Reverted; }
public sealed class OnlineReceipt { public string Id,CatalogId,Status,CreatedUtc,Operation,DensityBefore,DensityAfter,BackupLayout; public List<string> Mods=new List<string>(); public List<OwnedChange> Files=new List<OwnedChange>(); }
public sealed class ReceiptEnvelope { public string Protected; }
public sealed class ModPreparationException : IOException {
    public readonly string ModId,ModName,AssetName,Entry;
    public ModPreparationException(FixedMod mod,FixedAsset asset,string entry,IOException cause)
        :base("Mod: "+mod.Name+"\r\nPacote: "+asset.Name+(String.IsNullOrEmpty(entry)?"":"\r\nArquivo: "+entry)+"\r\n\r\n"+cause.Message,cause){ModId=mod.Id;ModName=mod.Name;AssetName=asset.Name;Entry=entry;}
}
public sealed class FileContentMismatchException : IOException {
    public readonly long ActualSize; public readonly string ActualSha256;
    public FileContentMismatchException(FixedFile file,long size,string hash):base((size>file.Size?"Arquivo excede o tamanho fixado.":size<file.Size?"Arquivo menor que o tamanho fixado.":"Conteúdo diferente do catálogo.")+" Esperado: "+file.Size+" bytes; encontrado: "+size+" bytes. Arquivo: "+file.Path){ActualSize=size;ActualSha256=hash;}
}

public static class FixedData {
    public static FixedCatalog Load() {
        using(var stream=typeof(FixedData).Assembly.GetManifestResourceStream("CyberpunkCoop.catalogo.json")) {
            if(stream==null||stream.Length>16000000)throw new IOException("O catálogo está incompleto. Obtenha o programa original.");
            using(var hash=SHA256.Create())if(Safe.Hex(hash.ComputeHash(stream))!=OnlineIdentity.CatalogHash)throw new IOException("O catálogo foi alterado. Obtenha o programa original.");
            stream.Position=0;
            using(var reader=new StreamReader(stream)) { var c=Safe.Json.Deserialize<FixedCatalog>(reader.ReadToEnd());Validate(c);return c; }
        }
    }
    public static void Validate(FixedCatalog c) {
        if(c==null||c.Schema!=1||c.GameVersion!="2.5"||!Regex.IsMatch(c.GameFileVersion??"",@"^\d+(\.\d+){1,3}$")||c.Packages==null||c.Packages.Count==0||c.Packages.Count>200) throw new IOException("Catálogo inválido.");
        var ids=new HashSet<string>(StringComparer.Ordinal); var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var immutable=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var m in c.Packages) {
            if(m==null||!Regex.IsMatch(m.Id??"","^[A-Za-z0-9-]+$")||!ids.Add(m.Id)||m.GameVersion!=c.GameVersion||m.Files==null||m.Files.Count==0||m.Files.Count>20000||m.Files.All(f=>f!=null&&f.UserData)||m.Assets==null||m.Assets.Count==0||m.Dependencies==null) throw new IOException("Componente inválido.");
            if(!String.IsNullOrEmpty(m.PhantomLibertyStatus)&&!new[]{"not-required","required","unverified"}.Contains(m.PhantomLibertyStatus))throw new IOException("Classificação Phantom Liberty inválida.");
            if(m.RequiresRedmod)throw new IOException("Esta lista não aceita mods que exigem REDmod.");
            if(m.NexusModId<0||m.NexusFileId<0||(m.NexusModId==0)!=(m.NexusFileId==0))throw new IOException("Alias Nexus sem versão fixada.");
            foreach(var group in new[]{m.IncompatiblePaths,m.ExclusiveCodeDirectories})if(group!=null){
                if(group.Length>64)throw new IOException("Lista de incompatibilidades excessiva.");
                foreach(string p in group){if(String.IsNullOrWhiteSpace(p)||!Safe.Allowed(p))throw new IOException("Caminho de incompatibilidade inválido.");Safe.In(Path.GetTempPath(),p);}
            }
            var assets=new HashSet<string>();
            foreach(var a in m.Assets) {
                if(a==null||!assets.Add(a.Id)||!HashValid(a.Sha256)||a.Size<0||a.Size>8L*1024*1024*1024||!new[]{"zip","tar","raw"}.Contains(a.Format)||!NexusClient.Allowed(new Uri(a.Url),false)) throw new IOException("Fonte inválida no catálogo.");
                if(a.ModId<0||a.FileId<0||(a.ModId>0&&a.FileId==0)) throw new IOException("Identificador Nexus inválido ou não fixado.");
            }
            foreach(var f in m.Files) {
                if(f==null||!assets.Contains(f.Asset)||!HashValid(f.Sha256)||f.Size<0||f.Size>8L*1024*1024*1024||!Safe.Allowed(f.Path)||!paths.Add(f.Path)) throw new IOException("Colisão ou receita inválida no catálogo.");
                if(f.UserData&&(m.Immutable||!f.Path.StartsWith("bin/x64/plugins/cyber_engine_tweaks/mods/",StringComparison.Ordinal)||!f.Path.EndsWith(".json",StringComparison.OrdinalIgnoreCase)))throw new IOException("Perfil personalizável inválido.");
                FpvCustomization.Validate(m,f);
                Safe.In(Path.GetTempPath(),f.Path); if(f.Entry!="") Safe.In(Path.GetTempPath(),f.Entry);
                if(m.Immutable) immutable.Add(f.Path);
            }
        }
        foreach(var m in c.Packages) foreach(string d in m.Dependencies) if(!ids.Contains(d)) throw new IOException("Dependência desconhecida: "+d);
        ModDefaults.Validate(c);
        NightCityDensity.Validate(c);
        Expand(c,c.Packages.Select(x=>x.Id));
        string[] basePaths={"bin/x64/winmm.dll","red4ext/RED4ext.dll","red4ext/LICENSE.txt","red4ext/THIRD_PARTY_LICENSES.txt","red4ext/plugins/NightCoop/NightCoop.dll"};
        if(immutable.Count!=5 || !basePaths.All(immutable.Contains) || c.Packages.Any(x=>x.Immutable&&x.Id!="NightCoop")) throw new IOException("A base imutável precisa dos cinco arquivos originais.");
    }
    public static bool HashValid(string h) { return Regex.IsMatch(h??"","^[a-f0-9]{64}$"); }
    public static void RequireInstallable(IEnumerable<FixedMod> mods) {
        var blocked=mods.FirstOrDefault(m=>!String.IsNullOrEmpty(m.BlockedReason));
        if(blocked!=null)throw new IOException(blocked.Name+" está retido: "+blocked.BlockedReason);
    }
    public static List<FixedMod> Expand(FixedCatalog c,IEnumerable<string> selected) {
        var result=new List<FixedMod>(); var active=new HashSet<string>(); var done=new HashSet<string>();
        Action<string> visit=null;
        visit=id=>{if(done.Contains(id))return;if(!active.Add(id))throw new IOException("Ciclo de dependências.");var m=c.Packages.SingleOrDefault(x=>x.Id==id);if(m==null)throw new IOException("Mod fora da lista fixa: "+(id??"<sem ID>")+". A referência não pertence ao catálogo desta versão.");foreach(string d in m.Dependencies)visit(d);active.Remove(id);done.Add(id);result.Add(NightCityDensity.Default(m));};
        foreach(string id in selected)visit(id);return result;
    }
    public static ModScan Scan(FixedMod mod,string game,bool checkIncompatibilities=true) {
        var s=new ModScan{Id=mod.Id};
        try {
            mod=UserFileApprovals.Resolve(mod,game);
            Safe.NoLinks(game);
            string conflict=checkIncompatibilities?Incompatibility(mod,game):null;
            if(conflict!=null){s.State="Incompatível";s.Different=1;s.Detail=conflict;return s;}
            if(mod.Id==NightCityDensity.ModId)return NightCityDensity.Scan(mod,game);
            foreach(var f in mod.Files.Where(f=>!f.UserData)) {
                string p=Safe.In(game,f.Path);
                if(!File.Exists(p)) { if(Directory.Exists(p)) s.Different++; else s.Missing++;OnlineDiagnostics.Record("scan.file",mod.Id,"Arquivo não encontrado.",new{Game=game,Path=f.Path,ExpectedSha256=f.Sha256,ExpectedSize=f.Size,Present=false}); }
                else {var info=new FileInfo(p);string hash=(info.Length==f.Size||VehicleCompatibility.HasUpgrade(f)||f.CanonicalSha256!=null||(f.Patch!=null&&info.Length==f.Patch.SourceSize))?Safe.Hash(p):null;bool correct=FpvCustomization.MatchesFile(f,info.Length,hash)&&!FpvCustomization.IsOriginal(f,info.Length,hash);bool original=FpvCustomization.IsOriginal(f,info.Length,hash);if(correct)s.Correct++;else if(original)s.Original++;else s.Different++;OnlineDiagnostics.Record("scan.file",mod.Id,correct?"Arquivo conferido.":original?"Versão anterior reconhecida; correção disponível.":"Arquivo divergente.",new{Game=game,Path=f.Path,ExpectedSha256=f.Sha256,ExpectedSize=f.Size,ActualSize=info.Length,ActualSha256=hash,WrittenUtc=info.LastWriteTimeUtc.ToString("o"),Present=true,Correct=correct,OfficialOriginal=original});}
            }
            s.State=s.Different>0?"Diferente/alterado":s.Missing==0?(s.Original>0?"Setup NASK disponível":"Instalado correto"):s.Correct+s.Original==0?"Não instalado":"Parcial";
            s.Detail=s.Correct+" corretos; "+s.Original+" originais oficiais; "+s.Missing+" ausentes; "+s.Different+" diferentes";
            if(mod.Files.Any(f=>f.UserData))s.Detail+="; arquivos de perfil preservados, fora da verificação de versão";
        }catch(IOException){s.State="Não verificável";s.Detail="Não foi possível ler o caminho com segurança.";}
        catch(UnauthorizedAccessException){s.State="Não verificável";s.Detail="Sem permissão para ler os arquivos.";}
        finally{OnlineDiagnostics.Record("scan.result",mod.Id,"Conferência dos arquivos do jogo.",new{Game=game,State=s.State,Correct=s.Correct,Original=s.Original,Missing=s.Missing,Different=s.Different,Detail=s.Detail});}
        return s;
    }
    public static string Incompatibility(FixedMod mod,string game) {
        foreach(string relative in mod.IncompatiblePaths??new string[0]){
            string p=Safe.In(game,relative);
            if(File.Exists(p)||Directory.Exists(p))return mod.Name+": mod incompatível detectado em "+relative+". Faça backup dos presets e remova o mod incompatível antes de instalar; nenhum arquivo foi apagado.";
        }
        var known=new HashSet<string>(mod.Files.Select(f=>f.Path),StringComparer.OrdinalIgnoreCase);
        foreach(string relative in mod.ExclusiveCodeDirectories??new string[0]){
            string root=Safe.In(game,relative);if(File.Exists(root))return mod.Name+": um arquivo ocupa a pasta de código "+relative+". Preserve-o e resolva o conflito antes de instalar.";
            if(!Directory.Exists(root))continue;
            var folders=new Stack<string>();folders.Push(root);
            while(folders.Count>0){
                string dir=folders.Pop();Safe.NoLinks(dir);
                foreach(string file in Directory.GetFiles(dir)){
                    Safe.NoLinks(file);string path=file.Substring(Path.GetFullPath(game).TrimEnd('\\').Length+1).Replace('\\','/');
                    if(new[]{".lua",".reds",".dll"}.Contains(Path.GetExtension(file).ToLowerInvariant())&&!known.Contains(path))return mod.Name+": código de outra versão encontrado em "+path+". Faça backup e remova a versão anterior antes de instalar; seus presets foram preservados.";
                }
                foreach(string sub in Directory.GetDirectories(dir)){
                    Safe.NoLinks(sub);string name=Path.GetFileName(sub);
                    bool userData=dir.Equals(root,StringComparison.OrdinalIgnoreCase)&&relative.StartsWith("bin/x64/plugins/cyber_engine_tweaks/mods/",StringComparison.OrdinalIgnoreCase)&&(name.Equals("Data",StringComparison.OrdinalIgnoreCase)||name.Equals("Character Presets",StringComparison.OrdinalIgnoreCase));
                    if(!userData)folders.Push(sub);
                }
            }
        }
        return null;
    }
    public static string GameVersion(string game) {
        string exe=Safe.In(game,"bin/x64/Cyberpunk2077.exe"); if(!File.Exists(exe))throw new IOException("Escolha a pasta do jogo que contém bin, archive e r6.");
        return FileVersionInfo.GetVersionInfo(exe).ProductVersion??"não identificada";
    }
    public static string GameBuild(string game) {return (FileVersionInfo.GetVersionInfo(Safe.In(game,"bin/x64/Cyberpunk2077.exe")).FileVersion??"").Trim();}
    public static void ValidateGame(string game,IEnumerable<FixedMod> mods,string expectedBuild) {
        foreach(var m in mods){string issue=Incompatibility(m,game);if(issue!=null)throw new IOException(issue);}
        string version=GameVersion(game);
        if(!Installer.VersionSupported(version))throw new IOException("Jogo encontrado: "+version+". Esta lista foi preparada para 2.5. Uma atualização exige nova revisão; nada será instalado.");
        string build=GameBuild(game);if(build!=expectedBuild)throw new IOException("Build do jogo: "+build+". Build preparado: "+expectedBuild+". É necessária revisão antes de instalar; nenhuma versão será substituída.");
        GameEdition.ValidateGame(game,mods);
        if(mods.Any(x=>x.RequiresRedmod)&&!File.Exists(Safe.In(game,"tools/redmod/bin/redMod.exe")))throw new IOException("Baixe REDmod separadamente na loja antes de instalar este perfil.");
        if(File.Exists(Safe.In(game,"mods/NIGHT CITY ALIVE/info.json")))throw new IOException("Há uma variante REDmod antiga de Night City Alive. Resolva essa variante antes de usar o perfil sem REDmod.");
    }
}

public sealed partial class DownloadCache {
    public readonly string Root;
    public DownloadCache(string root) { Root=Path.GetFullPath(root); Safe.NoLinks(Root); CleanOrphanPreparations(); }
    public string PathFor(FixedAsset a) { return SourcesOnly?OriginalFor(a):Safe.In(Root,a.Sha256+".cache"); }
    public bool Contains(FixedAsset a) { string p=PathFor(a);return File.Exists(p)&&new FileInfo(p).Length==a.Size&&Safe.Hash(p)==a.Sha256; }
    public bool CleanPreparation(string stage) {
        try {
            string full=Path.GetFullPath(stage),parent=Safe.In(Root,"preparacao");
            if(!String.Equals(Path.GetDirectoryName(full),parent,StringComparison.OrdinalIgnoreCase)||!Regex.IsMatch(Path.GetFileName(full),"^[a-f0-9]{32}$"))throw new IOException("Pasta temporária inesperada.");
            Safe.NoLinks(full);if(!Directory.Exists(full))return true;
            var todo=new Stack<string>();todo.Push(full);
            while(todo.Count>0){string dir=todo.Pop();Safe.NoLinks(dir);foreach(string sub in Directory.GetDirectories(dir)){Safe.NoLinks(sub);todo.Push(sub);}foreach(string file in Directory.GetFiles(dir))Safe.NoLinks(file);}
            Directory.Delete(full,true);return true;
        }catch(Exception cleanup){OnlineDiagnostics.Record("stage.cleanup.failed",null,"Não foi possível limpar a preparação temporária.",new{ErrorType=cleanup.GetType().Name});return false;}
    }
    public void Import(string source,FixedAsset a) {
        using(HoldOperation()) {
        OnlineDiagnostics.Record("cache.import.begin",a.Id,"Conferindo arquivo para o cache.",new{Source=source,ExpectedSha256=a.Sha256,ExpectedSize=a.Size});
        Safe.NoLinks(source);
        if(source.EndsWith(".crdownload",StringComparison.OrdinalIgnoreCase)||source.EndsWith(".part",StringComparison.OrdinalIgnoreCase))throw new IOException("Download ainda incompleto.");
        if(!File.Exists(source)||new FileInfo(source).Length!=a.Size||Safe.Hash(source)!=a.Sha256)throw new IOException("Arquivo diferente da versão fixada: "+a.Name);
        if(SourcesOnly){Remember(a,source);return;}
        Directory.CreateDirectory(Root); string dest=PathFor(a);if(Contains(a)){OnlineDiagnostics.Record("cache.import.verified",a.Id,"Cache já continha o pacote conferido.",new{Source=source,Cache=dest,Sha256=a.Sha256,Size=a.Size,AlreadyCached=true});return;}
        Safe.CopyAtomic(source,dest,a.Sha256);
        OnlineDiagnostics.Record("cache.import.verified",a.Id,"Arquivo conferido e copiado ao cache; não implica instalação.",new{Source=source,Cache=dest,Sha256=a.Sha256,Size=a.Size,AlreadyCached=false});
        }
    }
    public int ImportFolder(string folder,IEnumerable<FixedAsset> assets,Action<string> report) {
        Safe.NoLinks(folder);if(!Directory.Exists(folder))throw new IOException("Pasta de downloads ausente.");
        var wanted=assets.GroupBy(x=>x.Sha256).Select(x=>x.First()).Where(x=>!Contains(x)).ToList();int count=0;
        foreach(string file in Directory.GetFiles(folder)) {
            if(file.EndsWith(".crdownload",StringComparison.OrdinalIgnoreCase)||file.EndsWith(".part",StringComparison.OrdinalIgnoreCase))continue;
            Safe.NoLinks(file);long size=new FileInfo(file).Length;var possible=wanted.Where(x=>x.Size==size).ToList();if(possible.Count==0)continue;
            string hash=Safe.Hash(file);var a=possible.FirstOrDefault(x=>x.Sha256==hash);if(a==null)continue;
            report("Importando "+a.Name);Import(file,a);wanted.RemoveAll(x=>x.Sha256==hash);count++;
        }return count;
    }
    public void Stage(FixedMod mod,string stage,Action<string> report) {
        using(HoldOperation()) {
        foreach(var a in mod.Assets) {
            string currentEntry=null;
            try {
            if(!Contains(a))throw new IOException("Baixe ou importe o arquivo exato: "+a.Name);
            string source=PathFor(a);var files=mod.Files.Where(x=>x.Asset==a.Id).ToList();
            if(a.Format=="zip") {
                using(var zip=ZipFile.OpenRead(source)) {
                    var entries=new Dictionary<string,ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
                    foreach(var e in zip.Entries) {
                        if(((e.ExternalAttributes>>16)&0xF000)==0xA000)throw new IOException("Link no ZIP.");
                        if(e.FullName.EndsWith("/")) { Safe.In(stage,e.FullName.TrimEnd('/'));continue; }
                        Safe.In(stage,e.FullName);
                        if(((e.ExternalAttributes>>16)&0xF000)==0xA000||entries.ContainsKey(e.FullName))throw new IOException("Link ou entrada duplicada no ZIP.");
                        entries.Add(e.FullName,e);
                    }
                    foreach(var f in files){currentEntry=f.Entry;ZipArchiveEntry e;if(!entries.TryGetValue(f.Entry,out e))throw new IOException("Arquivo previsto não encontrado dentro do pacote.");if(e.Length>8L*1024*1024*1024)throw new IOException("Arquivo ultrapassa o limite de leitura segura de 8 GiB.");using(var s=e.Open())WritePrepared(s,stage,f);report(mod.Name);}
                }
            } else if(a.Format=="raw") {foreach(var f in files){currentEntry=f.Path;using(var s=File.OpenRead(source))WriteVerified(s,stage,f);}}
            else {
                using(var reader=BundledArchiveReader.Open(source,a.Size,a.Sha256))foreach(var f in files){currentEntry=f.Entry;reader.CopyEntry(f.Entry,s=>WriteVerified(s,stage,f));report(mod.Name);}
            }
            } catch(IOException ex) {
                var mismatch=ex as FileContentMismatchException;var file=filesForFailure(mod,a,currentEntry);
                if(mismatch!=null&&file!=null&&!mod.Immutable&&!file.UserData&&file.Patch==null&&mod.DensityProfiles==null)ex=new FileApprovalRequest(mod,a,file,mismatch.ActualSize,mismatch.ActualSha256);
                var failure=new ModPreparationException(mod,a,currentEntry,ex);
                OnlineDiagnostics.Record("stage.failed",mod.Id,"Falha ao preparar arquivo do pacote.",new{ModName=mod.Name,Asset=a.Name,Entry=currentEntry,ExpectedAssetBytes=a.Size,AssetSha256=a.Sha256,Error=ex.Message});
                throw failure;
            }
        }
        }
    }
    static FixedFile filesForFailure(FixedMod mod,FixedAsset asset,string entry){return mod.Files.FirstOrDefault(f=>f.Asset==asset.Id&&(f.Entry==entry||f.Path==entry));}
    static void WritePrepared(Stream input,string stage,FixedFile file) {
        if(file.Patch==null){WriteVerified(input,stage,file);return;}
        using(var prepared=FpvCustomization.Apply(input,file))WriteVerified(prepared,stage,file);
    }
    static void WriteVerified(Stream input,string stage,FixedFile f) {
        string dest=Safe.In(stage,f.Path);Directory.CreateDirectory(Path.GetDirectoryName(dest));
        using(var output=new FileStream(dest,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var sha=SHA256.Create()){
            var buffer=new byte[65536];long total=0;int n;
            while((n=input.Read(buffer,0,buffer.Length))>0){total+=n;if(total>8L*1024*1024*1024)throw new IOException("Arquivo ultrapassa o limite de leitura segura de 8 GiB: "+f.Path);output.Write(buffer,0,n);sha.TransformBlock(buffer,0,n,buffer,0);}
            sha.TransformFinalBlock(new byte[0],0,0);string hash=Safe.Hex(sha.Hash);if(!FpvCustomization.MatchesFile(f,total,hash))throw new FileContentMismatchException(f,total,hash);
        }
    }
}

public sealed partial class NexusClient {
    string key; public bool Premium { get; private set; } public string UserName { get; private set; }
    public static bool Allowed(Uri u,bool cdn) {
        if(u.Scheme!="https"||!String.IsNullOrEmpty(u.UserInfo)||!u.IsDefaultPort)return false;
        string h=u.DnsSafeHost.ToLowerInvariant();
        return new[]{"www.nexusmods.com","api.nexusmods.com","github.com","raw.githubusercontent.com","release-assets.githubusercontent.com","objects.githubusercontent.com","nightcoop.net"}.Contains(h) || (cdn&&(h=="nexus-cdn.com"||h.EndsWith(".nexus-cdn.com")||h=="files.nexusmods.com"));
    }
    public void Connect(string personalKey) {
        if(String.IsNullOrWhiteSpace(personalKey)||personalKey.Any(c=>Char.IsControl(c)))throw new IOException("Informe sua chave pessoal de API do Nexus.");
        key=personalKey.Trim();try{var result=Json("users/validate.json");UserName=Text(result,"name");Premium=result.ContainsKey("is_premium")&&Convert.ToBoolean(result["is_premium"]);if(String.IsNullOrEmpty(UserName))throw new IOException("Conta não validada.");}catch{Disconnect();throw;}
    }
    public void Disconnect(){key=null;UserName=null;Premium=false;}
    public bool Connected { get{return key!=null;} }
    public Dictionary<string,object> Json(string relative) {
        if(!Connected)throw new IOException("Conecte sua conta Nexus primeiro.");
        var uri=new Uri("https://api.nexusmods.com/v1/"+relative);
        if(uri.DnsSafeHost!="api.nexusmods.com")throw new IOException("API inválida.");
        try{var request=(HttpWebRequest)WebRequest.Create(uri);request.Timeout=25000;request.ReadWriteTimeout=25000;request.AllowAutoRedirect=false;request.Headers["apikey"]=key;request.Headers["Application-Name"]="DouglasCyberpunkCoop-Testing";request.Headers["Application-Version"]="0.1.0";request.UserAgent="DouglasCyberpunkCoop-Testing/0.1.0";
            using(var response=(HttpWebResponse)request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream())) {string text=reader.ReadToEnd();if(text.Length>8000000)throw new IOException("Resposta Nexus excessiva.");var value=Safe.Json.DeserializeObject(text);var dict=value as Dictionary<string,object>;if(dict!=null)return dict;return new Dictionary<string,object>{{"results",value}};}
        }catch(WebException ex){var r=ex.Response as HttpWebResponse;int status=r==null?0:(int)r.StatusCode;if(r!=null)r.Close();throw new IOException(status==429?"Limite Nexus atingido. Aguarde antes de tentar novamente.":status==401?"Chave Nexus recusada. Reconecte sua conta.":status==403?"O Nexus não autorizou esta operação para sua conta.":"Nexus indisponível (HTTP "+status+"). Nenhuma versão foi substituída.");}
    }
    public string CheckVersion(FixedMod m) {
        var a=m.Assets.FirstOrDefault(x=>x.ModId>0);int modId=a==null?m.NexusModId:a.ModId;if(modId==0)return "Fonte oficial fixada";
        var data=Json("games/cyberpunk2077/mods/"+modId+".json");string version=Text(data,"version");
        return version==m.Version?"Publicada: "+version+"; versão fixada preservada":"Publicada: "+version+"; manter fixada "+m.Version;
    }
    public string DownloadUrl(FixedMod m,FixedAsset a) {
        if(a.ModId==0)return a.Url;
        if(!Connected||!Premium)throw new IOException("Conta gratuita: autorize o download do arquivo exato no site e importe a pasta Downloads.");
        int file=a.FileId;if(file<=0)throw new IOException("Arquivo Nexus não fixado; catálogo precisa de revisão.");
        var result=Json("games/cyberpunk2077/mods/"+a.ModId+"/files/"+file+"/download_link.json");var links=Objects(result["results"]).ToList();if(links.Count==0)throw new IOException("Arquivo não disponível no Nexus.");
        string url=Text(links[0],"URI");if(!Allowed(new Uri(url),true))throw new IOException("Servidor de download não permitido.");return url;
    }
    public static IEnumerable<Dictionary<string,object>> Objects(object value){var array=value as System.Collections.IEnumerable;if(array==null)yield break;foreach(object x in array){var d=x as Dictionary<string,object>;if(d!=null)yield return d;}}
    public static string Text(Dictionary<string,object> data,string key){return data.ContainsKey(key)?Convert.ToString(data[key]):"";}
    public void Download(FixedMod m,FixedAsset a,DownloadCache cache,Action<string> report) {
        using(var control=new DownloadControl())Download(m,a,cache,control,p=>report(m.Name+": "+p.Status+" · "+p.Percent+"%"));
    }
}

public sealed partial class FixedInstaller {
    readonly FixedCatalog catalog; readonly DownloadCache cache; readonly string game,saves; readonly bool fixture;
    public FixedInstaller(FixedCatalog c,DownloadCache downloads,string gameRoot,string saveRoot,bool isFixture=false) {
        cache=downloads;game=Path.GetFullPath(gameRoot).TrimEnd('\\');saves=saveRoot;
        if(game.Length<=3||game.StartsWith(@"\\"))throw new IOException("Escolha uma pasta local do jogo.");Safe.NoLinks(game);
        if(cache.Root.StartsWith(game+"\\",StringComparison.OrdinalIgnoreCase)||cache.Root.Equals(game,StringComparison.OrdinalIgnoreCase))throw new IOException("O cache precisa ficar fora da pasta do jogo.");
        fixture=isFixture;
        if(fixture&&(!File.Exists(Path.Combine(game,".fixture-only"))||!Regex.IsMatch(game,@"(?i)[\\/]online-testes-[a-f0-9]{32}[\\/]")))throw new IOException("Exceção de teste permitida apenas dentro de uma fixture isolada.");
        catalog=UserFileApprovals.ResolveCatalog(c,game);
    }
    string Data { get{return Safe.In(game,".CoopModsOnline");} }
    void Closed(){if(fixture)return;foreach(var p in Process.GetProcessesByName("Cyberpunk2077")){p.Dispose();throw new IOException("Feche o jogo antes de instalar ou desfazer.");}}
    public string Install(IEnumerable<string> chosen,bool acceptPlan,Action<string> report,int failAfter=-1) {
        if(!acceptPlan)throw new IOException("Confirme o plano de instalação.");
        var mods=FixedData.Expand(catalog,chosen).Select(m=>NightCityDensity.ForInstall(m,game)).ToList();if(mods.Count==0)throw new IOException("Marque pelo menos um mod.");
        return InstallTransaction(mods,report,failAfter,false);
    }
    string InstallTransaction(List<FixedMod> mods,Action<string> report,int failAfter,bool localImport) {
        OnlineDiagnostics.Record("install.request",null,"Motor de instalação acionado.",new{Game=game,Mods=mods.Select(m=>m.Id).ToArray(),Accepted=true,Fixture=fixture});
        FixedData.RequireInstallable(mods);
        FixedData.ValidateGame(game,mods,catalog.GameFileVersion);Closed();
        foreach(var m in mods){var scan=FixedData.Scan(m,game);if(scan.IsConflict)throw new IOException(m.Name+": "+scan.State+". Resolva o conflito antes de instalar.");}
        Directory.CreateDirectory(Data);
        using(var mutex=new FileStream(Safe.In(Data,"operacao.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
            EnsureNoIncomplete();
            if(localImport)foreach(var f in mods.SelectMany(m=>m.Files)){
                string dest=Safe.In(game,f.Path);Safe.NoLinks(dest);
                if(File.Exists(dest)||Directory.Exists(dest))throw new IOException("O destino foi ocupado: "+f.Path+". O arquivo existente foi preservado.");
            }
            var pending=mods.Where(m=>!FixedData.Scan(m,game).IsCorrect).ToList();
            if(pending.Count==0){
                if(!mods.Any(m=>ModDefaults.Needs(catalog,m.Id,game)))return "Todos os mods selecionados já estão corretos. Nada foi alterado.";
                Closed();ModDefaults.Apply(catalog,mods,game,Safe.In(Data,"defaults-backups/"+Guid.NewGuid().ToString("N")),report);
                return "Configurações iniciais aplicadas. Os ajustes existentes foram preservados.";
            }
            long bytes=pending.SelectMany(x=>x.Files).Sum(x=>x.Size);
            if(new DriveInfo(Path.GetPathRoot(game)).AvailableFreeSpace<bytes*2+100L*1024*1024||new DriveInfo(Path.GetPathRoot(cache.Root)).AvailableFreeSpace<bytes+100L*1024*1024)throw new IOException("Espaço insuficiente para preparação e backup.");
            string id=Guid.NewGuid().ToString("N"),stage=Safe.In(cache.Root,"preparacao/"+id);Directory.CreateDirectory(stage);
            var stageLease=cache.HoldPreparation(stage);
            try {
            foreach(var m in pending){report("Preparando "+m.Name);cache.Stage(m,stage,report);}
            var receipt=new OnlineReceipt{Id=id,CatalogId=catalog.Id,Status="preparando",CreatedUtc=DateTime.UtcNow.ToString("o"),Mods=mods.Select(x=>x.Id).ToList(),Operation=localImport?LocalModPackage.ReceiptOperation:null};
            string ledger=Safe.In(Data,(localImport?"importados/":"")+id+".json");Save(ledger,receipt);
            OnlineDiagnostics.Record("install.begin",null,"Pacotes preparados; iniciando cópia para o jogo.",new{Game=game,Receipt=id,Mods=pending.Select(m=>m.Id).ToArray(),Files=pending.Sum(m=>m.Files.Count)});
            try {
                BackupSaves(Safe.In(Data,"backups/"+id+"/saves"));
                receipt.Status="instalando";Save(ledger,receipt);int written=0;
                foreach(var m in pending)foreach(var f in m.Files) {
                    Closed();string dest=Safe.In(game,f.Path);Safe.NoLinks(dest);
                    if(f.UserData&&File.Exists(dest))continue;
                    if(File.Exists(dest)){
                        if(localImport)throw new IOException("O destino foi ocupado: "+f.Path+". O arquivo existente foi preservado.");
                        long size=new FileInfo(dest).Length;string hash=Safe.Hash(dest);
                        if(FpvCustomization.MatchesFile(f,size,hash)&&!FpvCustomization.IsOriginal(f,size,hash))continue;
                        if(!FpvCustomization.IsOriginal(f,size,hash))throw new IOException("Arquivo mudou após a conferência: "+f.Path);
                    }
                    if(Directory.Exists(dest))throw new IOException("Uma pasta ocupa o destino: "+f.Path);
                    string before=File.Exists(dest)?Safe.Hash(dest):null;
                    if(before!=null){
                        if(!FpvCustomization.IsOriginal(f,new FileInfo(dest).Length,before))throw new IOException("Origem mudou antes do backup.");
                        receipt.BackupLayout="fpv-flat-v1";string backup=UpgradeBackup(receipt,f.Path);
                        Directory.CreateDirectory(Path.GetDirectoryName(backup));Safe.NoLinks(backup);File.Copy(dest,backup,false);
                        if(Safe.Hash(backup)!=before)throw new IOException("Backup da versão anterior não conferiu.");
                    }
                    string prepared=Safe.In(stage,f.Path);string after=Safe.Hash(prepared);if(!FpvCustomization.MatchesFile(f,new FileInfo(prepared).Length,after))throw new IOException("Arquivo preparado mudou antes da cópia: "+f.Path);
                    var change=new OwnedChange{Path=f.Path,Before=before,After=after};receipt.Files.Add(change);Save(ledger,receipt);
                    OnlineDiagnostics.Record("install.file.begin",m.Id,"Copiando arquivo para a pasta do jogo.",new{Game=game,Receipt=id,Path=f.Path,Sha256=after});
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));Safe.NoLinks(dest);
                    string tmp=dest+".coop-"+id+".tmp";
                    try {File.Copy(prepared,tmp,false);if(Safe.Hash(tmp)!=after)throw new IOException("Cópia divergente.");Safe.NoLinks(dest);string current=File.Exists(dest)?Safe.Hash(dest):null;if(current!=before)throw new IOException("Destino mudou durante a preparação; arquivo preservado.");if(before==null)File.Move(tmp,dest);else File.Replace(tmp,dest,null);}finally{if(File.Exists(tmp)){Safe.NoLinks(tmp);File.Delete(tmp);}}
                    change.Applied=true;Save(ledger,receipt);
                    OnlineDiagnostics.Record("install.file.applied",m.Id,"Arquivo copiado e registrado.",new{Game=game,Receipt=id,Path=f.Path,Sha256=after});
                    written++;if(failAfter>=0&&written>=failAfter)throw new IOException("Falha de teste após a cópia.");report("Instalado: "+m.Name);
                }
                Closed();ModDefaults.Apply(catalog,mods,game,Safe.In(Data,"defaults-backups/"+id),report,-1,new HashSet<string>(receipt.Files.Where(f=>f.Applied).Select(f=>f.Path),StringComparer.OrdinalIgnoreCase));
                receipt.Status="instalado";Save(ledger,receipt);OnlineDiagnostics.Record("install.complete",null,"Instalação concluída pelo motor.",new{Game=game,Receipt=id,Mods=pending.Select(m=>m.Id).ToArray(),Files=written});return "Instalação concluída. Mods que já estavam corretos foram preservados. Pronto para jogar.";
            }catch(Exception ex) {
                OnlineDiagnostics.Record("install.failed",null,"Falha na instalação; fluxo de desfazer será acionado.",new{Game=game,Receipt=id,ErrorType=ex.GetType().Name});
                receipt.Status="falha";Save(ledger,receipt);
                try{UndoReceipt(receipt,ledger,report);}catch{}throw;
            }
            }finally{stageLease.Dispose();cache.CleanPreparation(stage);}
        }
    }
    void Save(string path,OnlineReceipt r){var plain=Encoding.UTF8.GetBytes(Safe.Json.Serialize(r));Safe.WriteJson(path,new ReceiptEnvelope{Protected=Convert.ToBase64String(ProtectedData.Protect(plain,null,DataProtectionScope.CurrentUser))});}
    OnlineReceipt ReadReceipt(string path){return ReadReceiptCore(path,false);}
    OnlineReceipt ReadReceiptCore(string path,bool inspection) {
        Safe.NoLinks(path);if(new FileInfo(path).Length>8000000)throw new IOException("Registro excessivo.");OnlineReceipt r;
        try{var envelope=Safe.Json.Deserialize<ReceiptEnvelope>(inspection?GeneralProjectStorage.ReadText(path,8000000):File.ReadAllText(path));r=Safe.Json.Deserialize<OnlineReceipt>(Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(envelope.Protected),null,DataProtectionScope.CurrentUser)));}
        catch(Exception ex){if(ex is OutOfMemoryException)throw;throw new IOException("Registro alterado, corrompido ou criado por outra conta do Windows. Nenhum arquivo será removido.");}
        if(r==null||r.CatalogId!=catalog.Id||!Regex.IsMatch(r.Id??"","^[a-f0-9]{32}$")||Path.GetFileName(path)!=r.Id+".json"||r.Files==null||r.Files.Count>20000||r.Mods==null)throw new IOException("Registro inválido. Não foi alterado nenhum arquivo.");
        if(r.Operation==ExtraResetOperation){ValidateExtraResetReceipt(r,path);return r;}
        if(r.Operation==LocalModPackage.ReceiptOperation){
            if(!String.Equals(Path.GetDirectoryName(path),Safe.In(Data,"importados"),StringComparison.OrdinalIgnoreCase))throw new IOException("Pasta de registro de importação inválida.");
            LocalModPackage.ValidateReceipt(r);foreach(var file in r.Files)Safe.In(game,file.Path);return r;
        }
        bool disabling=r.Operation=="deactivate";
        if(r.BackupLayout!=null&&!(disabling?r.BackupLayout=="flat-v1":r.BackupLayout=="fpv-flat-v1"))throw new IOException("Formato de backup desconhecido no registro.");
        if(r.Operation=="density"){NightCityDensity.ValidateReceipt(catalog,r);return r;}
        if(r.Operation!=null&&r.Operation!="install"&&!disabling)throw new IOException("Operação desconhecida no registro.");
        var mods=HistoricalReceipts.Expand(catalog,r.Mods);
        if(disabling&&(r.Mods.Count==0||r.Mods.Count!=r.Mods.Distinct().Count()||mods.Any(m=>r.Mods.Contains(m.Id)&&m.Immutable)))throw new IOException("Registro de desativação inválido.");
        var allowed=(disabling?mods.Where(m=>r.Mods.Contains(m.Id)).SelectMany(m=>NightCityDensity.KnownFiles(m)):mods.SelectMany(x=>NightCityDensity.KnownFiles(x))).ToList();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var f in r.Files){
            if(f==null||!seen.Add(f.Path)||!allowed.Any(known=>known.Path==f.Path&&FpvCustomization.KnownHash(known,disabling?f.Before:f.After))||(disabling?f.After!=null:f.Before!=null&&(r.BackupLayout!="fpv-flat-v1"||!allowed.Any(known=>known.Path==f.Path&&FpvCustomization.UpgradePair(known,f.Before,f.After)))))throw new IOException("Registro não corresponde ao catálogo.");
            Safe.In(game,f.Path);
        }return r;
    }
    public string Undo(Action<string> report) {
        Closed();Safe.NoLinks(Data);if(!Directory.Exists(Data))return "Nenhuma instalação deste programa para desfazer.";
        using(var mutex=new FileStream(Safe.In(Data,"operacao.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
            var candidates=AllReceiptPaths().Select(p=>new{Path=p,Receipt=ReadReceipt(p)}).Where(x=>x.Receipt.Status!="desfeito"&&x.Receipt.Status!="desfeito_com_preservados").OrderByDescending(x=>x.Receipt.CreatedUtc).ToList();
            if(candidates.Count==0)return "Nenhuma operação deste programa para desfazer.";var last=candidates[0];
            if(last.Receipt.Operation=="deactivate"){
                if(last.Receipt.Mods.Any(id=>!catalog.Packages.Any(m=>m.Id==id)))throw new IOException("Esta desativação contém mods retirados do pacote. A restauração automática foi bloqueada; os backups foram preservados.");
                var restoring=catalog.Packages.Where(m=>last.Receipt.Mods.Contains(m.Id)).ToList();FixedData.RequireInstallable(restoring);
                foreach(var m in restoring){string issue=FixedData.Incompatibility(m,game);if(issue!=null)throw new IOException(issue);}
            }
            EnsureUndoDependencies(last.Receipt);return UndoReceipt(last.Receipt,last.Path,report);
        }
    }
    string UndoReceipt(OnlineReceipt r,string ledger,Action<string> report) {
        if(r.Operation=="density")return UndoDensity(r,ledger,report);
        if(r.Operation=="deactivate"||r.Operation==ExtraResetOperation)return UndoDeactivation(r,ledger,report);
        int preserved=0;r.Status="desfazendo";Save(ledger,r);
        foreach(var f in r.Files.AsEnumerable().Reverse()){
            Closed();string p=Safe.In(game,f.Path);if(Directory.Exists(p)){preserved++;continue;}
            string current=File.Exists(p)?Safe.Hash(p):null;
            if(f.Before!=null){
                if(current==f.Before)continue;
                if(current!=null&&current!=f.After){preserved++;continue;}
                string backup=UpgradeBackup(r,f.Path);
                if(!File.Exists(backup)||Safe.Hash(backup)!=f.Before){preserved++;continue;}
                RestoreUpgrade(backup,p,f.Before,current);report("FPV original restaurado: "+f.Path);
            }else{
                if(current==null)continue;
                if(!f.Applied||current!=f.After){preserved++;continue;}
                File.Delete(p);report("Retirado: "+f.Path);
            }
        }
        r.Status=preserved==0?"desfeito":"desfeito_com_preservados";Save(ledger,r);
        return preserved==0?"Instalação desfeita. Arquivos anteriores e saves atuais foram preservados.":"Instalação desfeita; "+preserved+" arquivo(s) alterado(s) depois foram preservados.";
    }
    static void RestoreUpgrade(string source,string destination,string desired,string expectedCurrent) {
        Safe.NoLinks(source);Safe.NoLinks(destination);Directory.CreateDirectory(Path.GetDirectoryName(destination));
        string temp=destination+".fpv-"+Guid.NewGuid().ToString("N")+".tmp";
        try {
            File.Copy(source,temp,false);if(Safe.Hash(temp)!=desired)throw new IOException("Backup FPV mudou durante a restauração.");
            Safe.NoLinks(destination);string current=File.Exists(destination)?Safe.Hash(destination):null;
            if(current!=expectedCurrent)throw new IOException("Destino mudou durante a restauração do FPV; alteração preservada.");
            if(expectedCurrent==null)File.Move(temp,destination);else File.Replace(temp,destination,null);
        }finally{if(File.Exists(temp)){Safe.NoLinks(temp);File.Delete(temp);}}
    }
    string UpgradeBackup(OnlineReceipt receipt,string relative) {
        using(var sha=SHA256.Create())return Safe.In(Data,"backups/"+receipt.Id+"/fpv-originais/"+Safe.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(relative)))+".bin");
    }
    void BackupSaves(string dest) {
        if(String.IsNullOrEmpty(saves)||!Directory.Exists(saves))return;Safe.NoLinks(saves);var dirs=new Stack<string>();dirs.Push(saves);
        while(dirs.Count>0){string dir=dirs.Pop();Safe.NoLinks(dir);foreach(string sub in Directory.GetDirectories(dir)){Safe.NoLinks(sub);dirs.Push(sub);}foreach(string source in Directory.GetFiles(dir)){Safe.NoLinks(source);string relative=source.Substring(saves.TrimEnd('\\').Length+1).Replace('\\','/');string target=Safe.In(dest,relative);Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(source,target,false);if(Safe.Hash(source)!=Safe.Hash(target))throw new IOException("Backup de saves não conferiu.");}}
    }
}
