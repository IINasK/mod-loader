using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

public sealed class BrowserTab {
    internal readonly string Owner,Id;
    public readonly string Url;
    internal BrowserTab(string owner,string id,string url){Owner=owner;Id=id;Url=url;}
}

public sealed class OnlineBrowserSession:IDisposable {
    const string ProfileMarker="CyberpunkCoop dedicated browser profile v2";
    readonly object sync=new object();
    readonly string root,token=Guid.NewGuid().ToString("N");
    string profileRoot,profile;
    DefaultBrowserInfo selectedBrowser;
    readonly Dictionary<string,BrowserTab> owned=new Dictionary<string,BrowserTab>(StringComparer.Ordinal);
    readonly Func<string,string,string> testTransport;
    readonly Func<DefaultBrowserInfo> testBrowser;
    FileStream profileLock;
    Process browser;
    int port;
    string browserPath;
    bool disposed,mockStarted;
    public string LastCloseReason {get;private set;}
    public string BrowserName {get{return selectedBrowser==null?"navegador padrão do Windows":selectedBrowser.Name;}}
    sealed class Target {public string id{get;set;} public string type{get;set;} public string url{get;set;}}
    sealed class Version {public string webSocketDebuggerUrl{get;set;}}

    public OnlineBrowserSession(string appBrowserRoot):this(appBrowserRoot,null){}
    internal OnlineBrowserSession(string appBrowserRoot,Func<string,string,string> transport):this(appBrowserRoot,transport,null){}
    internal OnlineBrowserSession(string appBrowserRoot,Func<string,string,string> transport,Func<DefaultBrowserInfo> selection) {
        if(String.IsNullOrWhiteSpace(appBrowserRoot)||!Path.IsPathRooted(appBrowserRoot)||appBrowserRoot.Any(Char.IsControl))throw new IOException("Pasta do navegador inválida.");
        root=Path.GetFullPath(appBrowserRoot).TrimEnd('\\');
        if(root.Length<=3||root.StartsWith(@"\\",StringComparison.Ordinal))throw new IOException("Use uma pasta local própria para o navegador.");
        Safe.NoLinks(root);testTransport=transport;testBrowser=selection;
    }
    static string FileUrl(string url) {
        var link=ModImporter.ParseNexusLink(url);
        if(link.FileId<=0)throw new IOException("O navegador automático exige o link do arquivo Nexus fixado.");
        return link.Url;
    }
    static bool SameFile(string expected,string actual) {
        try{
            var fixedFile=ModImporter.ParseNexusLink(expected);int mod,file;
            return fixedFile.FileId>0&&ModPage(actual,out mod,out file)&&fixedFile.ModId==mod&&(file==0||fixedFile.FileId==file);
        }catch(IOException){return false;}
    }
    static bool ModPage(string text,out int mod,out int file) {
        mod=file=0;Uri uri;
        if(String.IsNullOrWhiteSpace(text)||text.Length>4096||text.Any(c=>Char.IsControl(c)||Char.IsWhiteSpace(c))||text.IndexOf('\\')>=0||!Uri.TryCreate(text,UriKind.Absolute,out uri)||uri.Scheme!="https"||(uri.Host!="www.nexusmods.com"&&uri.Host!="nexusmods.com")||!uri.IsDefaultPort||uri.UserInfo!="")return false;
        var raw=Regex.Match(text,@"^https://(?:www\.)?nexusmods\.com(?::443)?(/[^?#]*)(?:\?[^#]*)?(?:#[A-Za-z0-9_-]*)?$",RegexOptions.IgnoreCase);
        if(!raw.Success)return false;
        // The modern Nexus page may retain the mod ID while dropping file_id after the download.
        var path=Regex.Match(raw.Groups[1].Value,@"^(?:/[a-z]{2}(?:-[a-z]{2})?)?/(?:games/)?cyberpunk2077/mods/([0-9]+)/?$",RegexOptions.IgnoreCase);
        if(!path.Success||!Int32.TryParse(path.Groups[1].Value,out mod)||mod<=0)return false;
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(string pair in uri.Query.TrimStart('?').Split(new[]{'&'},StringSplitOptions.RemoveEmptyEntries)) {
            string[] parts=pair.Split('=');if(parts.Length!=2||!seen.Add(parts[0]))return false;
            if(parts[0]=="file_id"){if(!Regex.IsMatch(parts[1],"^[0-9]+$")||!Int32.TryParse(parts[1],out file)||file<=0)return false;}
            else if(parts[0]=="tab"){if(!new[]{"files","description","posts","images","logs","bugs","stats","videos"}.Contains(parts[1]))return false;}
            else if(parts[0]=="lang"||parts[0]=="language"||parts[0]=="locale"){if(!Regex.IsMatch(parts[1],@"^[a-z]{2}(?:-[a-z]{2})?$",RegexOptions.IgnoreCase))return false;}
            else return false;
        }
        return true;
    }
    static T Json<T>(string value) {
        try{return new JavaScriptSerializer{MaxJsonLength=1048576,RecursionLimit=16}.Deserialize<T>(value);}
        catch(ArgumentException){throw new IOException("Resposta inválida do navegador dedicado.");}
        catch(InvalidOperationException){throw new IOException("Resposta inválida do navegador dedicado.");}
    }
    void AcquireProfile() {
        if(profileLock!=null)return;
        selectedBrowser=testBrowser!=null?testBrowser():testTransport==null?DefaultBrowser.Resolve():new DefaultBrowserInfo(null,"Mock Chromium","Test",true);
        if(!selectedBrowser.SupportsControl)throw new IOException(selectedBrowser.Name+" é o padrão do Windows; esta versão não controla suas abas. O link será aberto nele, sem troca para Edge, e a detecção dos downloads continua disponível.");
        profileRoot=Safe.In(root,selectedBrowser.ProfileKey);profile=Safe.In(profileRoot,"Profile");
        Safe.NoLinks(profileRoot);Directory.CreateDirectory(profileRoot);
        string marker=Safe.In(profileRoot,"profile-owner.txt"),lockPath=Safe.In(profileRoot,"session.lock");
        try{profileLock=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
        catch(IOException){throw new IOException("Outra sessão já está usando o navegador dedicado do instalador.");}
        try{
            if(File.Exists(marker)) {
                if(new FileInfo(marker).Length>128||File.ReadAllText(marker)!=ProfileMarker)throw new IOException("A pasta escolhida não pertence ao navegador dedicado do instalador.");
            }else{
                if(Directory.Exists(profile))throw new IOException("Perfil existente sem identificação do instalador; não será utilizado.");
                using(var stream=new FileStream(marker,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var writer=new StreamWriter(stream,new UTF8Encoding(false)))writer.Write(ProfileMarker);
            }
            Safe.NoLinks(profile);Directory.CreateDirectory(profile);
        }catch{profileLock.Dispose();profileLock=null;throw;}
    }
    bool ReadPort(out int foundPort,out string foundPath) {
        foundPort=0;foundPath=null;string file=Safe.In(profile,"DevToolsActivePort");
        if(!File.Exists(file)||new FileInfo(file).Length>512)return false;
        string value;using(var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))using(var reader=new StreamReader(input,Encoding.ASCII))value=reader.ReadToEnd();
        string[] lines=value.Trim().Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries);
        if(lines.Length!=2||!Int32.TryParse(lines[0],out foundPort)||foundPort<1024||foundPort>65535||!Regex.IsMatch(lines[1],@"^/devtools/browser/[A-Za-z0-9_-]{8,128}$"))return false;
        foundPath=lines[1];return true;
    }
    bool VersionMatches(int expectedPort,string expectedPath) {
        var version=Json<Version>(Http(expectedPort,"GET","/json/version"));Uri ws;
        return version!=null&&Uri.TryCreate(version.webSocketDebuggerUrl,UriKind.Absolute,out ws)&&ws.Scheme=="ws"&&ws.Host=="127.0.0.1"&&ws.Port==expectedPort&&ws.AbsolutePath==expectedPath&&ws.Query==""&&ws.Fragment==""&&ws.UserInfo=="";
    }
    void EnsureStarted() {
        if(disposed)throw new ObjectDisposedException("OnlineBrowserSession");
        AcquireProfile();
        if(testTransport!=null){mockStarted=true;return;}
        if(browser!=null&&!browser.HasExited){CheckAttached();return;}
        int oldPort;string oldPath;
        if(ReadPort(out oldPort,out oldPath)) {
            bool running=false;try{running=VersionMatches(oldPort,oldPath);}catch(IOException){}
            if(running)throw new IOException("Feche a janela dedicada do instalador anterior antes de abrir uma nova sessão.");
        }
        string active=Safe.In(profile,"DevToolsActivePort");if(File.Exists(active))File.Delete(active);
        string sentinel="about:blank#cyberpunkcoop-"+token;
        Safe.NoLinks(selectedBrowser.Executable);
        browser=Process.Start(new ProcessStartInfo(selectedBrowser.Executable,"--user-data-dir=\""+profile+"\" --remote-debugging-address=127.0.0.1 --remote-debugging-port=0 --no-first-run --no-default-browser-check --new-window \""+sentinel+"\""){UseShellExecute=false});
        DateTime until=DateTime.UtcNow.AddSeconds(8);
        while(DateTime.UtcNow<until) {
            if(browser==null||browser.HasExited)throw new IOException("Não foi possível iniciar uma sessão própria do "+selectedBrowser.Name+". O link será aberto no navegador padrão sem controle de abas.");
            try{if(ReadPort(out port,out browserPath)&&VersionMatches(port,browserPath))return;}catch(IOException){}
            Thread.Sleep(100);
        }
        throw new IOException("O navegador dedicado não disponibilizou o controle de abas. O download pode continuar pelo navegador padrão.");
    }
    void CheckAttached() {
        if(disposed)throw new ObjectDisposedException("OnlineBrowserSession");
        if(testTransport!=null){if(!mockStarted)throw new IOException("Sessão do navegador indisponível.");return;}
        int found;string path;
        if(browser==null||browser.HasExited||!ReadPort(out found,out path)||found!=port||path!=browserPath)throw new IOException("A sessão original do navegador foi encerrada; nenhuma outra aba será controlada.");
    }
    string Request(string method,string path) {
        CheckAttached();return testTransport!=null?testTransport(method,path):Http(port,method,path);
    }
    internal static string ReadBounded(Stream stream,long size) {
        if(size>1048576)throw new IOException("Resposta do navegador maior que o limite.");
        using(var memory=new MemoryStream()) {
            byte[] buffer=new byte[8192];int n;
            while((n=stream.Read(buffer,0,buffer.Length))>0){if(memory.Length+n>1048576)throw new IOException("Resposta do navegador maior que o limite.");memory.Write(buffer,0,n);}
            try{return new UTF8Encoding(false,true).GetString(memory.ToArray());}catch(DecoderFallbackException){throw new IOException("Resposta do navegador não é texto UTF-8 válido.");}
        }
    }
    static string Http(int endpoint,string method,string path) {
        if(endpoint<1024||endpoint>65535||!path.StartsWith("/json/",StringComparison.Ordinal)||path.Any(Char.IsControl)||path.IndexOf('#')>=0)throw new IOException("Endpoint de navegador inválido.");
        var request=(HttpWebRequest)WebRequest.Create("http://127.0.0.1:"+endpoint+path);
        request.Method=method;request.AllowAutoRedirect=false;request.Proxy=null;request.Timeout=3000;request.ReadWriteTimeout=3000;request.KeepAlive=false;
        if(method=="PUT")request.ContentLength=0;
        try{using(var response=(HttpWebResponse)request.GetResponse()) {
            if(response.StatusCode!=HttpStatusCode.OK)throw new IOException("Controle local do navegador recusado.");
            using(var stream=response.GetResponseStream())return ReadBounded(stream,response.ContentLength);
        }}catch(WebException){throw new IOException("O controle local do navegador dedicado está indisponível.");}
    }
    public BrowserTab Open(string exactNexusFileUrl) {
        string url=FileUrl(exactNexusFileUrl);
        lock(sync) {
            EnsureStarted();var target=Json<Target>(Request("PUT","/json/new?"+Uri.EscapeDataString(url)));
            if(target==null||target.type!="page"||!ValidId(target.id)||!(String.IsNullOrEmpty(target.url)||target.url=="about:blank"||SameFile(url,target.url))||owned.ContainsKey(target.id))throw new IOException("Não foi possível vincular a aba ao arquivo Nexus solicitado.");
            var tab=new BrowserTab(token,target.id,url);owned.Add(target.id,tab);
            // Only the exact blank page created by this session is disposable.
            try{foreach(var blank in Targets().Where(t=>t.url=="about:blank#cyberpunkcoop-"+token))Request("GET","/json/close/"+Uri.EscapeDataString(blank.id));}catch(IOException){}
            return tab;
        }
    }
    static bool ValidId(string id){return id!=null&&Regex.IsMatch(id,@"^[A-Za-z0-9_-]{1,128}$");}
    List<Target> Targets() {
        var targets=Json<List<Target>>(Request("GET","/json/list"));
        if(targets==null||targets.Count>1024||targets.Any(t=>t==null||!ValidId(t.id))||targets.Select(t=>t.id).Distinct().Count()!=targets.Count)throw new IOException("Lista de abas inválida.");
        return targets;
    }
    public bool Close(BrowserTab tab) {
        lock(sync) {
            BrowserTab known;
            if(disposed){LastCloseReason="A sessão do navegador foi encerrada; nenhuma aba foi fechada.";return false;}
            if(tab==null||tab.Owner!=token||!owned.TryGetValue(tab.Id,out known)||!Object.ReferenceEquals(known,tab)){LastCloseReason="A aba não foi criada e vinculada por esta sessão; foi preservada.";return false;}
            try {
                var current=Targets().SingleOrDefault(t=>t.id==tab.Id);
                if(current==null){owned.Remove(tab.Id);LastCloseReason="A aba do download já foi fechada pelo usuário.";return false;}
                if(current.type!="page"||!SameFile(tab.Url,current.url)){owned.Remove(tab.Id);LastCloseReason="A aba mudou de página ou arquivo e foi preservada.";return false;}
                string response=Request("GET","/json/close/"+Uri.EscapeDataString(tab.Id));
                if(response.Trim().Trim('"')!="Target is closing")throw new IOException("O navegador não confirmou o fechamento da aba.");
                owned.Remove(tab.Id);LastCloseReason="A aba do download foi fechada.";return true;
            }catch(IOException ex){LastCloseReason=ex.Message;return false;}
        }
    }
    public void Dispose() {
        lock(sync){if(disposed)return;disposed=true;owned.Clear();if(profileLock!=null){profileLock.Dispose();profileLock=null;}if(browser!=null){browser.Dispose();browser=null;}}
    }
}
