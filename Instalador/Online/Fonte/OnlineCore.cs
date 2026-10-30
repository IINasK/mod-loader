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

public sealed class FixedCatalog { public int Schema; public string Id,GameVersion,GameFileVersion; public List<FixedMod> Packages; public string[] Excluded; }
public sealed class FixedMod {
    public string Id,Name,Version,Profile,GameVersion,Warning,ThirdPerson,ThirdPersonNotes,ThirdPersonSource,BlockedReason;
    public string PhantomLibertyStatus,PhantomLibertyNotes,PhantomLibertySource;
    public bool RequiresPL,RequiresRedmod,Immutable;
    public string[] Dependencies;
    public string[] IncompatiblePaths,ExclusiveCodeDirectories;
    public int NexusModId,NexusFileId;
    public List<FixedAsset> Assets;
    public List<FixedFile> Files;
    public List<FixedDensityProfile> DensityProfiles;
}
public sealed class FixedAsset { public string Id,Name,Sha256,Format,Url; public long Size; public int ModId,FileId; }
public sealed class FixedFile { public string Asset,Entry,Path,Sha256; public long Size; }
public sealed class ModScan {
    public string Id,State,Detail; public int Correct,Missing,Different;
    public bool IsCorrect { get { return State=="Instalado correto"; } }
    public bool IsConflict { get { return Different>0 || State=="Não verificável"; } }
}
public sealed class OwnedChange { public string Path,Before,After; public bool Applied,UndoStarted,Reverted; }
public sealed class OnlineReceipt { public string Id,CatalogId,Status,CreatedUtc,Operation,DensityBefore,DensityAfter,BackupLayout; public List<string> Mods=new List<string>(); public List<OwnedChange> Files=new List<OwnedChange>(); }
public sealed class ReceiptEnvelope { public string Protected; }

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
            if(m==null||!Regex.IsMatch(m.Id??"","^[A-Za-z0-9-]+$")||!ids.Add(m.Id)||m.GameVersion!=c.GameVersion||m.Files==null||m.Files.Count==0||m.Files.Count>20000||m.Assets==null||m.Assets.Count==0||m.Dependencies==null) throw new IOException("Componente inválido.");
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
                Safe.In(Path.GetTempPath(),f.Path); if(f.Entry!="") Safe.In(Path.GetTempPath(),f.Entry);
                if(m.Immutable) immutable.Add(f.Path);
            }
        }
        foreach(var m in c.Packages) foreach(string d in m.Dependencies) if(!ids.Contains(d)) throw new IOException("Dependência desconhecida: "+d);
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
        visit=id=>{if(done.Contains(id))return;if(!active.Add(id))throw new IOException("Ciclo de dependências.");var m=c.Packages.SingleOrDefault(x=>x.Id==id);if(m==null)throw new IOException("Mod fora da lista fixa.");foreach(string d in m.Dependencies)visit(d);active.Remove(id);done.Add(id);result.Add(NightCityDensity.Default(m));};
        foreach(string id in selected)visit(id);return result;
    }
    public static ModScan Scan(FixedMod mod,string game,bool checkIncompatibilities=true) {
        var s=new ModScan{Id=mod.Id};
        try {
            Safe.NoLinks(game);
            string conflict=checkIncompatibilities?Incompatibility(mod,game):null;
            if(conflict!=null){s.State="Incompatível";s.Different=1;s.Detail=conflict;return s;}
            if(mod.Id==NightCityDensity.ModId)return NightCityDensity.Scan(mod,game);
            foreach(var f in mod.Files) {
                string p=Safe.In(game,f.Path);
                if(!File.Exists(p)) { if(Directory.Exists(p)) s.Different++; else s.Missing++;OnlineDiagnostics.Record("scan.file",mod.Id,"Arquivo não encontrado.",new{Game=game,Path=f.Path,ExpectedSha256=f.Sha256,ExpectedSize=f.Size,Present=false}); }
                else {var info=new FileInfo(p);string hash=info.Length==f.Size?Safe.Hash(p):null;bool correct=info.Length==f.Size&&hash==f.Sha256;if(correct)s.Correct++;else s.Different++;OnlineDiagnostics.Record("scan.file",mod.Id,correct?"Arquivo conferido.":"Arquivo divergente.",new{Game=game,Path=f.Path,ExpectedSha256=f.Sha256,ExpectedSize=f.Size,ActualSize=info.Length,ActualSha256=hash,WrittenUtc=info.LastWriteTimeUtc.ToString("o"),Present=true,Correct=correct});}
            }
            s.State=s.Different>0?"Diferente/alterado":s.Missing==0?"Instalado correto":s.Correct==0?"Não instalado":"Parcial";
            s.Detail=s.Correct+" corretos; "+s.Missing+" ausentes; "+s.Different+" diferentes";
        }catch(IOException){s.State="Não verificável";s.Detail="Não foi possível ler o caminho com segurança.";}
        catch(UnauthorizedAccessException){s.State="Não verificável";s.Detail="Sem permissão para ler os arquivos.";}
        finally{OnlineDiagnostics.Record("scan.result",mod.Id,"Conferência dos arquivos do jogo.",new{Game=game,State=s.State,Correct=s.Correct,Missing=s.Missing,Different=s.Different,Detail=s.Detail});}
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

public sealed class DownloadCache {
    public readonly string Root;
    public DownloadCache(string root) { Root=Path.GetFullPath(root); Safe.NoLinks(Root); }
    public string PathFor(FixedAsset a) { return Safe.In(Root,a.Sha256+".cache"); }
    public bool Contains(FixedAsset a) { string p=PathFor(a);return File.Exists(p)&&new FileInfo(p).Length==a.Size&&Safe.Hash(p)==a.Sha256; }
    public void Import(string source,FixedAsset a) {
        OnlineDiagnostics.Record("cache.import.begin",a.Id,"Conferindo arquivo para o cache.",new{Source=source,ExpectedSha256=a.Sha256,ExpectedSize=a.Size});
        Safe.NoLinks(source);
        if(source.EndsWith(".crdownload",StringComparison.OrdinalIgnoreCase)||source.EndsWith(".part",StringComparison.OrdinalIgnoreCase))throw new IOException("Download ainda incompleto.");
        if(!File.Exists(source)||new FileInfo(source).Length!=a.Size||Safe.Hash(source)!=a.Sha256)throw new IOException("Arquivo diferente da versão fixada: "+a.Name);
        Directory.CreateDirectory(Root); string dest=PathFor(a);if(Contains(a)){OnlineDiagnostics.Record("cache.import.verified",a.Id,"Cache já continha o pacote conferido.",new{Source=source,Cache=dest,Sha256=a.Sha256,Size=a.Size,AlreadyCached=true});return;}
        Safe.CopyAtomic(source,dest,a.Sha256);
        OnlineDiagnostics.Record("cache.import.verified",a.Id,"Arquivo conferido e copiado ao cache; não implica instalação.",new{Source=source,Cache=dest,Sha256=a.Sha256,Size=a.Size,AlreadyCached=false});
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
        foreach(var a in mod.Assets) {
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
                    foreach(var f in files){ZipArchiveEntry e;if(!entries.TryGetValue(f.Entry,out e)||e.Length!=f.Size)throw new IOException("Receita não corresponde ao arquivo.");using(var s=e.Open())WriteVerified(s,stage,f);report(mod.Name);}
                }
            } else if(a.Format=="raw") {foreach(var f in files)using(var s=File.OpenRead(source))WriteVerified(s,stage,f);}
            else {
                foreach(var f in files) {
                    // tar only streams an allowlisted entry to stdout; it never receives an extraction directory.
                    var listInfo=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"tar.exe"),"-tvf \""+source+"\" \""+f.Entry+"\""){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
                    if(!File.Exists(listInfo.FileName))throw new IOException("Este Windows não possui tar para ler RAR/7z.");
                    using(var listing=Process.Start(listInfo)) {
                        var linesTask=listing.StandardOutput.ReadToEndAsync();var errTask=listing.StandardError.ReadToEndAsync();
                        if(!listing.WaitForExit(30000)){listing.Kill();throw new IOException("Tempo esgotado na inspeção do arquivo.");}
                        string[] lines=linesTask.Result.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries);
                        if(listing.ExitCode!=0||lines.Length!=1||!lines[0].StartsWith("-",StringComparison.Ordinal))throw new IOException("Entrada RAR/7z não é um arquivo regular único; links são recusados.");errTask.Wait();
                    }
                    var psi=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"tar.exe"),"-xOf \""+source+"\" \""+f.Entry+"\""){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
                    if(!File.Exists(psi.FileName))throw new IOException("Este Windows não possui tar para ler RAR/7z. Não foi instalado nenhum programa adicional.");
                    using(var p=Process.Start(psi)) {
                        var error=p.StandardError.ReadToEndAsync();
                        try{WriteVerified(p.StandardOutput.BaseStream,stage,f);}catch{try{p.Kill();}catch{}throw;}
                        if(!p.WaitForExit(30000)){p.Kill();throw new IOException("Tempo esgotado ao ler o arquivo.");}
                        if(p.ExitCode!=0)throw new IOException("Não foi possível ler a entrada RAR/7z.");error.Wait();
                    }
                }
            }
        }
    }
    static void WriteVerified(Stream input,string stage,FixedFile f) {
        string dest=Safe.In(stage,f.Path);Directory.CreateDirectory(Path.GetDirectoryName(dest));
        using(var output=new FileStream(dest,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var sha=SHA256.Create()){
            var buffer=new byte[65536];long total=0;int n;
            while((n=input.Read(buffer,0,buffer.Length))>0){total+=n;if(total>f.Size)throw new IOException("Arquivo excede o tamanho fixado.");output.Write(buffer,0,n);sha.TransformBlock(buffer,0,n,buffer,0);}
            sha.TransformFinalBlock(new byte[0],0,0);if(total!=f.Size||Safe.Hex(sha.Hash)!=f.Sha256)throw new IOException("Conteúdo extraído diferente do catálogo: "+f.Path);
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
        catalog=c;cache=downloads;game=Path.GetFullPath(gameRoot).TrimEnd('\\');saves=saveRoot;
        if(game.Length<=3||game.StartsWith(@"\\"))throw new IOException("Escolha uma pasta local do jogo.");Safe.NoLinks(game);
        if(cache.Root.StartsWith(game+"\\",StringComparison.OrdinalIgnoreCase)||cache.Root.Equals(game,StringComparison.OrdinalIgnoreCase))throw new IOException("O cache precisa ficar fora da pasta do jogo.");
        fixture=isFixture;
        if(fixture&&(!File.Exists(Path.Combine(game,".fixture-only"))||!Regex.IsMatch(game,@"(?i)[\\/]online-testes-[a-f0-9]{32}[\\/]")))throw new IOException("Exceção de teste permitida apenas dentro de uma fixture isolada.");
    }
    string Data { get{return Safe.In(game,".CoopModsOnline");} }
    void Closed(){if(fixture)return;foreach(var p in Process.GetProcessesByName("Cyberpunk2077")){p.Dispose();throw new IOException("Feche o jogo antes de instalar ou desfazer.");}}
    public string Install(IEnumerable<string> chosen,bool acceptPlan,Action<string> report,int failAfter=-1) {
        if(!acceptPlan)throw new IOException("Confirme o plano de instalação.");
        var mods=FixedData.Expand(catalog,chosen).Select(m=>NightCityDensity.ForInstall(m,game)).ToList();if(mods.Count==0)throw new IOException("Marque pelo menos um mod.");
        OnlineDiagnostics.Record("install.request",null,"Motor de instalação acionado.",new{Game=game,Mods=mods.Select(m=>m.Id).ToArray(),Accepted=acceptPlan,Fixture=fixture});
        FixedData.RequireInstallable(mods);
        FixedData.ValidateGame(game,mods,catalog.GameFileVersion);Closed();
        foreach(var m in mods){var scan=FixedData.Scan(m,game);if(scan.IsConflict)throw new IOException(m.Name+": "+scan.State+". Resolva o conflito antes de instalar.");}
        Directory.CreateDirectory(Data);
        using(var mutex=new FileStream(Safe.In(Data,"operacao.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
            EnsureNoIncomplete();
            var pending=mods.Where(m=>!FixedData.Scan(m,game).IsCorrect).ToList();if(pending.Count==0)return "Todos os mods selecionados já estão corretos. Nada foi alterado.";
            long bytes=pending.SelectMany(x=>x.Files).Sum(x=>x.Size);
            if(new DriveInfo(Path.GetPathRoot(game)).AvailableFreeSpace<bytes*2+100L*1024*1024||new DriveInfo(Path.GetPathRoot(cache.Root)).AvailableFreeSpace<bytes+100L*1024*1024)throw new IOException("Espaço insuficiente para preparação e backup.");
            string id=Guid.NewGuid().ToString("N"),stage=Safe.In(cache.Root,"preparacao/"+id);Directory.CreateDirectory(stage);
            foreach(var m in pending){report("Preparando "+m.Name);cache.Stage(m,stage,report);}
            var receipt=new OnlineReceipt{Id=id,CatalogId=catalog.Id,Status="preparando",CreatedUtc=DateTime.UtcNow.ToString("o"),Mods=mods.Select(x=>x.Id).ToList()};
            string ledger=Safe.In(Data,id+".json");Save(ledger,receipt);
            OnlineDiagnostics.Record("install.begin",null,"Pacotes preparados; iniciando cópia para o jogo.",new{Game=game,Receipt=id,Mods=pending.Select(m=>m.Id).ToArray(),Files=pending.Sum(m=>m.Files.Count)});
            try {
                BackupSaves(Safe.In(Data,"backups/"+id+"/saves"));
                receipt.Status="instalando";Save(ledger,receipt);int written=0;
                foreach(var m in pending)foreach(var f in m.Files) {
                    Closed();string dest=Safe.In(game,f.Path);
                    if(File.Exists(dest)){if(new FileInfo(dest).Length==f.Size&&Safe.Hash(dest)==f.Sha256)continue;throw new IOException("Arquivo mudou após a conferência: "+f.Path);}
                    if(Directory.Exists(dest))throw new IOException("Uma pasta ocupa o destino: "+f.Path);
                    var change=new OwnedChange{Path=f.Path,Before=null,After=f.Sha256};receipt.Files.Add(change);Save(ledger,receipt);
                    OnlineDiagnostics.Record("install.file.begin",m.Id,"Copiando arquivo para a pasta do jogo.",new{Game=game,Receipt=id,Path=f.Path,Sha256=f.Sha256});
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));Safe.NoLinks(dest);
                    string tmp=dest+".coop-"+id+".tmp";
                    try {File.Copy(Safe.In(stage,f.Path),tmp,false);if(Safe.Hash(tmp)!=f.Sha256)throw new IOException("Cópia divergente.");Safe.NoLinks(dest);File.Move(tmp,dest);}finally{if(File.Exists(tmp)){Safe.NoLinks(tmp);File.Delete(tmp);}}
                    change.Applied=true;Save(ledger,receipt);
                    OnlineDiagnostics.Record("install.file.applied",m.Id,"Arquivo copiado e registrado.",new{Game=game,Receipt=id,Path=f.Path,Sha256=f.Sha256});
                    written++;if(failAfter>=0&&written>=failAfter)throw new IOException("Falha de teste após a cópia.");report("Instalado: "+m.Name);
                }
                receipt.Status="instalado";Save(ledger,receipt);OnlineDiagnostics.Record("install.complete",null,"Instalação concluída pelo motor.",new{Game=game,Receipt=id,Mods=pending.Select(m=>m.Id).ToArray(),Files=written});return "Instalação concluída. Mods que já estavam corretos foram preservados. Pronto para jogar.";
            }catch(Exception ex) {
                OnlineDiagnostics.Record("install.failed",null,"Falha na instalação; fluxo de desfazer será acionado.",new{Game=game,Receipt=id,ErrorType=ex.GetType().Name});
                receipt.Status="falha";Save(ledger,receipt);
                try{UndoReceipt(receipt,ledger,report);}catch{}throw;
            }
        }
    }
    void Save(string path,OnlineReceipt r){var plain=Encoding.UTF8.GetBytes(Safe.Json.Serialize(r));Safe.WriteJson(path,new ReceiptEnvelope{Protected=Convert.ToBase64String(ProtectedData.Protect(plain,null,DataProtectionScope.CurrentUser))});}
    OnlineReceipt ReadReceipt(string path) {
        Safe.NoLinks(path);if(new FileInfo(path).Length>8000000)throw new IOException("Registro excessivo.");OnlineReceipt r;
        try{var envelope=Safe.Json.Deserialize<ReceiptEnvelope>(File.ReadAllText(path));r=Safe.Json.Deserialize<OnlineReceipt>(Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(envelope.Protected),null,DataProtectionScope.CurrentUser)));}
        catch(Exception ex){if(ex is OutOfMemoryException)throw;throw new IOException("Registro alterado, corrompido ou criado por outra conta do Windows. Nenhum arquivo será removido.");}
        if(r==null||r.CatalogId!=catalog.Id||!Regex.IsMatch(r.Id??"","^[a-f0-9]{32}$")||Path.GetFileName(path)!=r.Id+".json"||r.Files==null||r.Files.Count>20000||r.Mods==null)throw new IOException("Registro inválido. Não foi alterado nenhum arquivo.");
        bool disabling=r.Operation=="deactivate";
        if(r.BackupLayout!=null&&(!disabling||r.BackupLayout!="flat-v1"))throw new IOException("Formato de backup desconhecido no registro.");
        if(r.Operation=="density"){NightCityDensity.ValidateReceipt(catalog,r);return r;}
        if(r.Operation!=null&&r.Operation!="install"&&!disabling)throw new IOException("Operação desconhecida no registro.");
        var mods=FixedData.Expand(catalog,r.Mods);
        if(disabling&&(r.Mods.Count==0||r.Mods.Count!=r.Mods.Distinct().Count()||catalog.Packages.Any(m=>r.Mods.Contains(m.Id)&&m.Immutable)))throw new IOException("Registro de desativação inválido.");
        var allowed=(disabling?catalog.Packages.Where(m=>r.Mods.Contains(m.Id)).SelectMany(m=>NightCityDensity.KnownFiles(m)):mods.SelectMany(x=>NightCityDensity.KnownFiles(x))).ToList();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var f in r.Files){if(f==null||!seen.Add(f.Path)||!allowed.Any(known=>known.Path==f.Path&&known.Sha256==(disabling?f.Before:f.After))||(disabling?f.After!=null:f.Before!=null))throw new IOException("Registro não corresponde ao catálogo.");Safe.In(game,f.Path);}return r;
    }
    public string Undo(Action<string> report) {
        Closed();Safe.NoLinks(Data);if(!Directory.Exists(Data))return "Nenhuma instalação deste programa para desfazer.";
        using(var mutex=new FileStream(Safe.In(Data,"operacao.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
            var candidates=Directory.GetFiles(Data,"*.json").Select(p=>new{Path=p,Receipt=ReadReceipt(p)}).Where(x=>x.Receipt.Status!="desfeito"&&x.Receipt.Status!="desfeito_com_preservados").OrderByDescending(x=>x.Receipt.CreatedUtc).ToList();
            if(candidates.Count==0)return "Nenhuma operação deste programa para desfazer.";var last=candidates[0];
            if(last.Receipt.Operation=="deactivate"){
                var restoring=catalog.Packages.Where(m=>last.Receipt.Mods.Contains(m.Id)).ToList();FixedData.RequireInstallable(restoring);
                foreach(var m in restoring){string issue=FixedData.Incompatibility(m,game);if(issue!=null)throw new IOException(issue);}
            }
            EnsureUndoDependencies(last.Receipt);return UndoReceipt(last.Receipt,last.Path,report);
        }
    }
    string UndoReceipt(OnlineReceipt r,string ledger,Action<string> report) {
        if(r.Operation=="density")return UndoDensity(r,ledger,report);
        if(r.Operation=="deactivate")return UndoDeactivation(r,ledger,report);
        int preserved=0;r.Status="desfazendo";Save(ledger,r);
        foreach(var f in r.Files.AsEnumerable().Reverse()){Closed();string p=Safe.In(game,f.Path);if(!File.Exists(p))continue;if(!f.Applied||Safe.Hash(p)!=f.After){preserved++;continue;}File.Delete(p);report("Retirado: "+f.Path);}
        r.Status=preserved==0?"desfeito":"desfeito_com_preservados";Save(ledger,r);
        return preserved==0?"Instalação desfeita. Arquivos anteriores e saves atuais foram preservados.":"Instalação desfeita; "+preserved+" arquivo(s) alterado(s) depois foram preservados.";
    }
    void BackupSaves(string dest) {
        if(String.IsNullOrEmpty(saves)||!Directory.Exists(saves))return;Safe.NoLinks(saves);var dirs=new Stack<string>();dirs.Push(saves);
        while(dirs.Count>0){string dir=dirs.Pop();Safe.NoLinks(dir);foreach(string sub in Directory.GetDirectories(dir)){Safe.NoLinks(sub);dirs.Push(sub);}foreach(string source in Directory.GetFiles(dir)){Safe.NoLinks(source);string relative=source.Substring(saves.TrimEnd('\\').Length+1).Replace('\\','/');string target=Safe.In(dest,relative);Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(source,target,false);if(Safe.Hash(source)!=Safe.Hash(target))throw new IOException("Backup de saves não conferiu.");}}
    }
}
