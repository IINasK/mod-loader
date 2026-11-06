using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Win32;

internal static class GeneralAutomaticUpdate {
    internal sealed class Pending {
        public int Schema=1;
        public string Version,Sha256,AttemptUtc;
        public long Size;
        public bool Ready;
    }
    internal const long MaxBytes=268435456;
    internal static bool TryStart(string executable,GeneralAppPaths paths){
        string root=Path.Combine(paths.Cache,"Updates");
        try{
            string installed=RegisteredDirectory(executable);
            if(installed==null)return false;
            if(!Cleanup(root,GeneralUpdateChecker.CurrentVersion))ScheduleCleanup(root,GeneralUpdateChecker.CurrentVersion);
            var release=GeneralUpdateChecker.Session.CheckOnce().GetAwaiter().GetResult();
            if(release==null||release.Url==null)return false;
            return StageAndLaunch(release,installed,root,GeneralUpdateChecker.CurrentVersion,StorageMaintenance.AnotherLoaderRunning,Download,info=>{using(var process=Process.Start(info))return process!=null;});
        }catch(Exception ex){OnlineDiagnostics.Record("update.failed",null,"Atualização adiada; versão atual preservada.",new{ErrorType=ex.GetType().Name});return false;}
    }
    internal static string RegisteredDirectory(string executable){
        string full=Path.GetFullPath(executable);Safe.NoLinks(full);
        if(!Path.GetFileName(full).Equals("ModLoader.exe",StringComparison.OrdinalIgnoreCase))return null;
        foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})using(var registry=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,view))using(var key=registry.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\IINasK.ModLoader_is1")){
            string directory=key==null?null:key.GetValue("InstallLocation") as string;
            if(String.IsNullOrWhiteSpace(directory))continue;
            directory=Path.GetFullPath(directory).TrimEnd('\\');Safe.NoLinks(directory);
            if(directory.Length>3&&String.Equals(Path.Combine(directory,"ModLoader.exe"),full,StringComparison.OrdinalIgnoreCase))return directory;
        }
        return null;
    }
    internal static bool StageAndLaunch(GeneralUpdateRelease release,string installed,string root,string current,Func<bool> another,Action<GeneralUpdateRelease,Stream> download,Func<ProcessStartInfo,bool> launch){
        ValidateRelease(release,current);installed=Path.GetFullPath(installed).TrimEnd('\\');root=Path.GetFullPath(root).TrimEnd('\\');
        Safe.NoLinks(installed);Safe.NoLinks(root);
        if(installed.Length<=3||root.Length<=3||(root+"\\").StartsWith(installed+"\\",StringComparison.OrdinalIgnoreCase)||(installed+"\\").StartsWith(root+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("Cache de atualização deve ficar separado do programa.");
        if(another())return false;
        string destination=Safe.In(installed,"ModLoader.exe");if(!File.Exists(destination))throw new IOException("Programa instalado não encontrado.");
        Version existing;
        if(!Version.TryParse(FileVersionInfo.GetVersionInfo(destination).FileVersion,out existing)||existing.Build<0||new Version(existing.Major,existing.Minor,existing.Build).CompareTo(new Version(release.Version))>=0)return false;
        Directory.CreateDirectory(root);
        using(var lease=Lock(root)){
            var state=Read(root);
            if(state!=null&&state.Version==release.Version&&state.AttemptUtc!=null&&DateTime.UtcNow-Attempt(state.AttemptUtc)<TimeSpan.FromHours(24))return false;
            if(state!=null&&(state.Version!=release.Version||state.Sha256!=release.Sha256||state.Size!=release.Size)){if(!Remove(root,state))return false;state=null;}
            if(state==null){state=new Pending{Version=release.Version,Sha256=release.Sha256,Size=release.Size};string candidate=Payload(root,state);if(File.Exists(candidate)||Directory.Exists(candidate)||File.Exists(candidate+".part")||Directory.Exists(candidate+".part"))throw new IOException("Arquivo sem registro no cache de atualização; preservado.");Write(root,state);}
            string setup=Payload(root,state),part=setup+".part";Safe.NoLinks(setup);Safe.NoLinks(part);
            try{
                if(!state.Ready){
                    if(File.Exists(part))File.Delete(part);
                    using(var output=new FileStream(part,FileMode.CreateNew,FileAccess.Write,FileShare.None)){download(release,output);output.Flush(true);}
                    Verify(part,release);
                    if(File.Exists(setup)){if(!Matches(setup,state))throw new IOException("Arquivo de atualização anterior foi alterado.");File.Delete(setup);}
                    File.Move(part,setup);state.Ready=true;Write(root,state);
                }
                Verify(setup,release);
            }catch{Remove(root,state);throw;}
            if(another())return false;
            state.AttemptUtc=DateTime.UtcNow.ToString("o");Write(root,state);
            var info=new ProcessStartInfo(setup,Arguments(installed,Safe.In(root,"setup-"+state.Version+".log"))){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=root};
            bool started=launch(info);
            OnlineDiagnostics.Record(started?"update.install.start":"update.install.deferred",null,started?"Instalador verificado iniciado; o aplicativo vai encerrar antes de abrir projetos.":"Não foi possível iniciar o instalador; versão atual preservada.",new{Version=release.Version,Sha256=release.Sha256});return started;
        }
    }
    internal static string Arguments(string installed,string log){
        if(installed.Contains("\"")||log.Contains("\""))throw new IOException("Caminho de atualização inválido.");
        return "/VERYSILENT /SUPPRESSMSGBOXES /SP- /NORESTART /NOCLOSEAPPLICATIONS /NOFORCECLOSEAPPLICATIONS /NORESTARTAPPLICATIONS /MODLOADERUPDATE=1 /DIR=\""+installed+"\" /LOG=\""+log+"\"";
    }
    internal static void ValidateRelease(GeneralUpdateRelease release,string current){
        var target=release==null?null:GeneralUpdateChecker.VersionNumber(release.Version);var running=GeneralUpdateChecker.VersionNumber(current);
        if(target==null||running==null||target.CompareTo(running)<=0||target.ToString(3)!=release.Version||!FixedData.HashValid(release.Sha256)||release.Size<=0||release.Size>MaxBytes)throw new IOException("Atualização sem versão, tamanho ou hash confiável.");
        string name="Mod-Loader-"+release.Version+".exe",prefix="https://github.com/"+GeneralUpdateChecker.Repository+"/releases/download/";
        if(release.Url!=prefix+"v"+release.Version+"/"+name&&release.Url!=prefix+release.Version+"/"+name)throw new IOException("Fonte de atualização não autorizada.");
    }
    internal static void Verify(string file,GeneralUpdateRelease release){
        Safe.NoLinks(file);if(!File.Exists(file)||new FileInfo(file).Length!=release.Size||Safe.Hash(file)!=release.Sha256)throw new IOException("Instalador recebido não confere com a Release oficial.");
        var info=FileVersionInfo.GetVersionInfo(file);Version version;
        if(!Version.TryParse((info.ProductVersion??"").Trim(),out version)||version.Build<0||version.ToString(3)!=release.Version||(info.FileDescription??"").Trim()!="Mod Loader Setup")throw new IOException("O arquivo verificado não é o instalador da versão anunciada.");
    }
    internal static bool AllowedDownload(Uri uri){return uri!=null&&uri.Scheme=="https"&&uri.IsDefaultPort&&uri.UserInfo.Length==0&&(uri.Host=="github.com"||uri.Host=="release-assets.githubusercontent.com"||uri.Host=="objects.githubusercontent.com");}
    internal static void Download(GeneralUpdateRelease release,Stream output){
        ValidateRelease(release,"0.0.0");var uri=new Uri(release.Url);var clock=Stopwatch.StartNew();
        for(int redirects=0;redirects<5;redirects++){
            if(!AllowedDownload(uri))throw new IOException("Redirecionamento de atualização não autorizado.");
            var request=(HttpWebRequest)WebRequest.Create(uri);request.Method="GET";request.AllowAutoRedirect=false;request.Timeout=10000;request.ReadWriteTimeout=10000;request.UserAgent="ModLoader/"+GeneralUpdateChecker.CurrentVersion;request.Credentials=null;request.UseDefaultCredentials=false;
            using(var response=(HttpWebResponse)request.GetResponse()){
                int status=(int)response.StatusCode;
                if(status==301||status==302||status==303||status==307||status==308){uri=new Uri(uri,response.Headers["Location"]);continue;}
                if(status!=200||response.ContentLength>0&&response.ContentLength!=release.Size)throw new IOException("Resposta inesperada no download da atualização.");
                using(var input=response.GetResponseStream()){
                    byte[] buffer=new byte[65536];long bytes=0;int count;
                    while((count=input.Read(buffer,0,buffer.Length))>0){bytes+=count;if(bytes>release.Size||clock.Elapsed>TimeSpan.FromSeconds(60))throw new IOException("Download da atualização excedeu o tamanho ou tempo permitido.");output.Write(buffer,0,count);}
                    if(bytes!=release.Size)throw new IOException("Download da atualização incompleto.");return;
                }
            }
        }
        throw new IOException("Excesso de redirecionamentos na atualização.");
    }
    static string StatePath(string root){return Safe.In(root,"pending.json");}
    static string Payload(string root,Pending state){return Safe.In(root,"Mod-Loader-"+state.Version+".exe");}
    static FileStream Lock(string root){Safe.NoLinks(root);Directory.CreateDirectory(root);string file=Safe.In(root,"update.lock");Safe.NoLinks(file);return new FileStream(file,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
    static DateTime Attempt(string value){DateTime time;if(!DateTime.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out time)||time.Kind!=DateTimeKind.Utc||time>DateTime.UtcNow.AddMinutes(5))throw new IOException("Registro de atualização inválido.");return time;}
    static Pending Read(string root){
        string file=StatePath(root);Safe.NoLinks(file);if(!File.Exists(file))return null;
        if(new FileInfo(file).Length>8192)throw new IOException("Registro de atualização excessivo.");var state=Safe.Json.Deserialize<Pending>(File.ReadAllText(file));var version=state==null?null:GeneralUpdateChecker.VersionNumber(state.Version);
        if(version==null||version.ToString(3)!=state.Version||state.Schema!=1||!FixedData.HashValid(state.Sha256)||state.Size<=0||state.Size>MaxBytes)throw new IOException("Registro de atualização inválido.");if(state.AttemptUtc!=null)Attempt(state.AttemptUtc);return state;
    }
    static void Write(string root,Pending state){Safe.NoLinks(StatePath(root));Safe.WriteJson(StatePath(root),state);}
    static bool Matches(string file,Pending state){Safe.NoLinks(file);return File.Exists(file)&&new FileInfo(file).Length==state.Size&&Safe.Hash(file)==state.Sha256;}
    static bool Remove(string root,Pending state){
        try{
            string setup=Payload(root,state),part=setup+".part",log=Safe.In(root,"setup-"+state.Version+".log");Safe.NoLinks(setup);Safe.NoLinks(part);Safe.NoLinks(log);
            if(File.Exists(setup)){if(!Matches(setup,state))return false;File.Delete(setup);}
            if(File.Exists(part))File.Delete(part);if(File.Exists(log))File.Delete(log);Safe.NoLinks(StatePath(root));File.Delete(StatePath(root));return true;
        }catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}
    }
    internal static bool Cleanup(string root,string current){
        try{if(!Directory.Exists(root))return true;using(var lease=Lock(root)){var state=Read(root);return state==null||new Version(state.Version).CompareTo(new Version(current))>0||Remove(root,state);}}
        catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}catch(ArgumentException){return false;}
    }
    static void ScheduleCleanup(string root,string current){Task.Run(async()=>{for(int i=0;i<5;i++){await Task.Delay(2000);if(Cleanup(root,current))return;}});}
}
