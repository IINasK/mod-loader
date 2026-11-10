using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;

internal sealed class P2PInvite {
    public int Schema=1;
    public string Kind, Device, Folder, ManifestHash, Name, Mode;
    public long Size;
    public bool Deferred, Automatic;
    public string SuggestedFolder, RelayDevice, Game;
    public bool PrivateFolder;

    static readonly string[] PersonalParts={
        "Users","Usuários","Usuarios","Utilizadores","Documents and Settings","AppData",
        "Desktop","Área de Trabalho","Documents","Documentos","Downloads"
    };
    static readonly char[] InvalidFolderChars=Path.GetInvalidFileNameChars();

    static bool FolderSyntax(string path){
        return path!=null&&path.Length<=240&&Regex.IsMatch(path,@"\A[A-Za-z]:\\[^\\]")&&
            path.Substring(3).Split('\\').All(part=>part.Length!=0&&part.IndexOfAny(InvalidFolderChars)<0&&
                !part.EndsWith(".")&&!part.EndsWith(" ")&&
                !part.Any(c=>Char.GetUnicodeCategory(c)==System.Globalization.UnicodeCategory.Format)&&
                !Regex.IsMatch(part,@"\A(?:CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])(?:\.|\z)",RegexOptions.IgnoreCase));
    }
    static bool HasPersonalParts(string path){
        return path.Split('\\').Any(part=>PersonalParts.Contains(part,StringComparer.OrdinalIgnoreCase)||
            part.StartsWith("OneDrive",StringComparison.OrdinalIgnoreCase));
    }
    internal void SuggestHostFolder(string path){
        if(String.IsNullOrWhiteSpace(path))return;
        path=path.TrimEnd('\\');
        if(!FolderSyntax(path))return;
        string user=Environment.UserName;
        PrivateFolder=HasPersonalParts(path)||path.Substring(3).Split('\\').Any(part=>String.Equals(part,user,StringComparison.OrdinalIgnoreCase))||
            new[]{Environment.SpecialFolder.UserProfile,Environment.SpecialFolder.MyDocuments,Environment.SpecialFolder.DesktopDirectory,Environment.SpecialFolder.LocalApplicationData}
            .Select(folder=>Environment.GetFolderPath(folder,Environment.SpecialFolderOption.DoNotVerify))
            .Where(folder=>!String.IsNullOrEmpty(folder)).Select(folder=>folder.TrimEnd('\\'))
            .Any(folder=>path.Equals(folder,StringComparison.OrdinalIgnoreCase)||path.StartsWith(folder+"\\",StringComparison.OrdinalIgnoreCase));
        SuggestedFolder=PrivateFolder?null:path;
    }
    internal const string CyberpunkGame="cyberpunk2077";
    internal static string CyberpunkFolder(string path){
        if(String.IsNullOrWhiteSpace(path))return null;
        try{
            string folder=WindowsFolderPicker.InstallationPath(path);
            return File.Exists(Safe.In(folder,"bin/x64/Cyberpunk2077.exe"))?folder:null;
        }catch(IOException){return null;}catch(UnauthorizedAccessException){return null;}
    }
    internal string LocalSuggestedFolder(){
        if(!FolderSyntax(SuggestedFolder)||HasPersonalParts(SuggestedFolder))return null;
        string current=SuggestedFolder.Substring(0,3);
        try{
            var drive=new DriveInfo(current);
            if(drive.DriveType!=DriveType.Fixed&&drive.DriveType!=DriveType.Removable)return null;
            // Check each parent before probing its children so a junction cannot redirect the lookup.
            foreach(string part in SuggestedFolder.Substring(3).Split('\\')){
                current=Path.Combine(current,part);
                if((File.GetAttributes(current)&(FileAttributes.Directory|FileAttributes.ReparsePoint))!=FileAttributes.Directory)return null;
            }
            return current;
        }catch(IOException){return null;}catch(UnauthorizedAccessException){return null;}
    }
    internal string Encode(){Validate();return "ML-P2P1:"+Convert.ToBase64String(Encoding.UTF8.GetBytes(Safe.Json.Serialize(this)));}
    internal static P2PInvite Decode(string code){
        try{
            if(code==null||code.Length>12000||!code.Trim().StartsWith("ML-P2P1:",StringComparison.Ordinal))throw new IOException("Convite P2P inválido.");
            var result=Safe.Json.Deserialize<P2PInvite>(Encoding.UTF8.GetString(Convert.FromBase64String(code.Trim().Substring(8))));
            if(result==null)throw new IOException("Convite P2P inválido.");result.Validate();return result;
        }catch(FormatException ex){throw new IOException("Convite P2P inválido.",ex);}catch(ArgumentException ex){throw new IOException("Convite P2P inválido.",ex);}
    }
    internal void Validate(){
        if(Mode!=null&&Mode!="installed-zip")throw new IOException("Formato de compartilhamento desconhecido.");
        if(Game!=null&&(Kind!="offer"||Mode!="installed-zip"||(Game!=CyberpunkGame&&Game!="generic")))
            throw new IOException("Jogo desconhecido no convite.");
        if(RelayDevice!=null&&(Kind!="offer"||Mode!="installed-zip"||!DeviceValid(RelayDevice)))
            throw new IOException("Identidade de relay inválida no convite.");
        if((PrivateFolder||SuggestedFolder!=null)&&(Kind!="offer"||Mode!="installed-zip"||
            SuggestedFolder!=null&&(PrivateFolder||!FolderSyntax(SuggestedFolder)||HasPersonalParts(SuggestedFolder))))
            throw new IOException("Sugestão de pasta inválida no convite.");
        if(Automatic&&!Deferred)throw new IOException("Aceitação automática requer convite antecipado.");
        if(Deferred&&(Mode!="installed-zip"||Size!=0))throw new IOException("Convite antecipado inválido.");
        if(Mode=="installed-zip"&&!Deferred&&(Size<=0||Size>P2PPack.MaxTotal))throw new IOException("Tamanho de pacote instalado inválido.");
        if((Schema!=1&&((Schema!=2&&Schema!=3)||!Automatic))||(Kind!="offer"&&Kind!="accept")||!DeviceValid(Device)||Folder==null||!Regex.IsMatch(Folder,"\\Aml-[a-f0-9]{32}\\z")||!P2PPack.HashValid(ManifestHash)||String.IsNullOrWhiteSpace(Name)||Name.Length>300||Name.Any(Char.IsControl))throw new IOException("Convite P2P inválido.");
    }
    internal static bool DeviceValid(string device){return device!=null&&Regex.IsMatch(device,"\\A[A-Z2-7]{7}(?:-[A-Z2-7]{7}){7}\\z");}
}

internal sealed class SyncthingEngine : IDisposable {
    internal const string Version="2.1.7";
    internal const string ZipHash="3ce7da770f546a8d02b65b2e7ee31f2a3b8b7c3f990ede045590cca4172ecfe1";
    internal const string ExeHash="cd9aea20afda2b9bdba5aff2e6ac964ed5c2e741ed4ceb48ad9e1be096ca7891";
    internal const string DownloadUrl="https://github.com/syncthing/syncthing/releases/download/v2.1.7/syncthing-windows-amd64-v2.1.7.zip";
    const string RelayPool="https://relays.syncthing.net/endpoint";
    readonly string home;
    Stopwatch inviteRouteClock;
    internal string InviteRelayDevice {get;private set;}
    readonly bool localOnly;
    string address,key;
    Process process;
    P2PProcessJob processJob;
    FileStream lease;
    internal string Device {get;private set;}
    internal string Executable {get;private set;}
    internal int ListenPort {get;private set;}
    internal bool Running {get{return process!=null&&!process.HasExited;}}
    internal SyncthingEngine(string home,bool localOnly=false){this.home=Path.GetFullPath(home);this.localOnly=localOnly;Safe.NoLinks(this.home);}
    internal static string Ensure(string root,Action<string> report){
        string folder=Safe.In(root,"engine/"+Version),exe=Safe.In(folder,"syncthing.exe");
        if(File.Exists(exe)&&Safe.Hash(exe)==ExeHash)return exe;
        Directory.CreateDirectory(folder);string zip=Safe.In(folder,"download.zip");
        report("Syncthing: download oficial (primeiro uso)…");
        try{
            ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
            var request=(HttpWebRequest)WebRequest.Create(DownloadUrl);request.Timeout=30000;request.UserAgent="ModLoader/1.1.4";
            using(var response=request.GetResponse())using(var input=response.GetResponseStream())using(var output=new FileStream(zip,FileMode.Create,FileAccess.Write,FileShare.None)){
                var buffer=new byte[65536];int count;long bytes=0;
                while((count=input.Read(buffer,0,buffer.Length))>0){bytes+=count;if(bytes>64000000)throw new IOException("O auxiliar excedeu o tamanho esperado.");output.Write(buffer,0,count);}
            }
            if(Safe.Hash(zip)!=ZipHash)throw new IOException("O Syncthing baixado difere da versão oficial fixada. Tente novamente.");
            using(var archive=ZipFile.OpenRead(zip))foreach(string name in new[]{"syncthing.exe","LICENSE.txt","AUTHORS.txt"}){
                var entry=archive.Entries.SingleOrDefault(e=>e.FullName=="syncthing-windows-amd64-v2.1.7/"+name);
                if(entry==null||entry.Length>60000000)throw new IOException("Pacote oficial do auxiliar inválido.");
                string path=Safe.In(folder,name);Safe.NoLinks(path);using(var input=entry.Open())using(var output=File.Create(path))input.CopyTo(output);
            }
            if(Safe.Hash(exe)!=ExeHash)throw new IOException("O executável do auxiliar não passou na conferência.");
            File.WriteAllText(Safe.In(folder,"SOURCE.txt"),"Unmodified Syncthing v2.1.7 (MPL-2.0). Source: https://github.com/syncthing/syncthing/tree/v2.1.7\r\n");
            return exe;
        }finally{if(File.Exists(zip))File.Delete(zip);}
    }
    static int Port(){var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return port;}
    ProcessStartInfo Launch(string exe,string args){return new ProcessStartInfo(exe,args){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Path.GetDirectoryName(exe)};}
    internal void Start(string exe){
        if(Running)return;Dispose();Safe.NoLinks(exe);if(Safe.Hash(exe)!=ExeHash)throw new IOException("Versão do Syncthing não reconhecida.");Executable=Path.GetFullPath(exe);
        InviteRelayDevice=null;inviteRouteClock=Stopwatch.StartNew();
        Directory.CreateDirectory(home);
        try{lease=new FileStream(Safe.In(home,"engine.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
        catch(IOException ex){
            int code=ex.HResult&0xffff;
            if(code!=32&&code!=33)throw;
            throw new IOException(GeneralStyle.T("O compartilhamento deste projeto já está aberto em outra janela do Mod Loader.",
                "Sharing for this project is already open in another Mod Loader window."),ex);
        }
        try{
            string config=Safe.In(home,"config.xml");
            if(!File.Exists(config))using(var generation=Process.Start(Launch(exe,"generate --home \""+home+"\" --no-port-probing"))){
                if(!generation.WaitForExit(20000)){generation.Kill();throw new IOException("O auxiliar demorou para preparar a conexão.");}
                if(generation.ExitCode!=0)throw new IOException("Não foi possível preparar o auxiliar P2P.");
            }
            var xml=new XmlDocument{XmlResolver=null};xml.Load(config);
            // No default Sync folder or personal Syncthing profile is ever adopted.
            // Session records rebuild approved roots after startup; stale/orphan folders cannot stay shared.
            foreach(XmlNode folder in xml.SelectNodes("/configuration/folder").Cast<XmlNode>().ToArray())folder.ParentNode.RemoveChild(folder);
            ListenPort=Port();int guiPort=Port();while(guiPort==ListenPort)guiPort=Port();
            var options=xml.SelectSingleNode("/configuration/options");
            foreach(XmlNode node in options.SelectNodes("listenAddress").Cast<XmlNode>().ToArray())options.RemoveChild(node);
            Action<string,string> set=(name,value)=>{var node=options.SelectSingleNode(name);if(node==null){node=xml.CreateElement(name);options.AppendChild(node);}node.InnerText=value;};
            set("listenAddress",(localOnly?"tcp://127.0.0.1:":"tcp://0.0.0.0:")+ListenPort);
            if(!localOnly){var relay=xml.CreateElement("listenAddress");relay.InnerText="dynamic+"+RelayPool;options.AppendChild(relay);}
            set("globalAnnounceEnabled",localOnly?"false":"true");set("localAnnounceEnabled",localOnly?"false":"true");set("relaysEnabled",localOnly?"false":"true");set("natEnabled",localOnly?"false":"true");
            set("urAccepted","-1");set("autoUpgradeIntervalH","0");set("startBrowser","false");set("crashReportingEnabled","false");
            xml.Save(config);address="http://127.0.0.1:"+guiPort;key=Guid.NewGuid().ToString("N")+Guid.NewGuid().ToString("N");
            var start=Launch(exe,"serve --home \""+home+"\" --no-browser --no-restart --no-upgrade --log-max-size 1048576 --log-max-old-files 2 --log-level WARN");
            start.EnvironmentVariables["STGUIADDRESS"]=address;start.EnvironmentVariables["STGUIAPIKEY"]=key;
            string log=Safe.In(home,"syncthing.log");
            long previousLogLength=File.Exists(log)?new FileInfo(log).Length:0;
            processJob=new P2PProcessJob();
            process=processJob.Start(start);
            var timer=Stopwatch.StartNew();
            while(timer.ElapsedMilliseconds<25000){
                if(process.HasExited)throw StartupFailure(log,previousLogLength,process.ExitCode);
                try{
                    var status=Get("system/status");
                    object id;
                    if(status!=null&&status.TryGetValue("myID",out id)){
                        Device=Convert.ToString(id);
                        if(P2PInvite.DeviceValid(Device))return;
                    }
                }catch(WebException){}
                Thread.Sleep(150);
            }
            throw new IOException("Tempo esgotado ao conectar o auxiliar P2P.");
        }catch{Dispose();throw;}
    }
    internal static IOException StartupFailure(string log,long previousLength,int exitCode){
        bool busy=false;
        try{
            Safe.NoLinks(log);
            using(var input=new FileStream(log,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
                long start=input.Length<previousLength?0:previousLength;
                input.Position=Math.Max(start,input.Length-8192);
                using(var reader=new StreamReader(input)){
                    var tail=new char[8192];
                    int count=reader.ReadBlock(tail,0,tail.Length);
                    busy=new string(tail,0,count).IndexOf("Failed to acquire lock: is another Syncthing instance already running?",StringComparison.Ordinal)>=0;
                }
            }
        }catch(IOException){}
        catch(UnauthorizedAccessException){}
        OnlineDiagnostics.Record("p2p.helper.start.failed",null,"O auxiliar P2P encerrou durante a inicialização.",new{ExitCode=exitCode,ProfileInUse=busy});
        return new IOException(busy?
            GeneralStyle.T("O auxiliar anterior ainda está usando este projeto. Feche as outras janelas do Mod Loader. Se já fechou todas, reinicie o Windows uma vez.",
                "The previous helper is still using this project. Close the other Mod Loader windows. If they are all closed, restart Windows once."):
            GeneralStyle.T("O auxiliar P2P encerrou ao iniciar. Feche e reabra o compartilhamento. Se persistir, confira o registro syncthing.log. Código ",
                "The P2P helper exited during startup. Reopen sharing. If it persists, check syncthing.log. Code ")+exitCode+".");
    }
    internal Dictionary<string,object> Get(string route){return Request("GET",route,null) as Dictionary<string,object>;}
    internal object Request(string method,string route,object body){
        var request=(HttpWebRequest)WebRequest.Create(address+"/rest/"+route);request.Proxy=null;request.AllowAutoRedirect=false;request.Timeout=5000;request.ReadWriteTimeout=5000;request.Method=method;request.Headers["X-API-Key"]=key;
        if(body!=null){byte[] bytes=Encoding.UTF8.GetBytes(Safe.Json.Serialize(body));request.ContentType="application/json";request.ContentLength=bytes.Length;using(var output=request.GetRequestStream())output.Write(bytes,0,bytes.Length);}
        using(var response=request.GetResponse())using(var input=response.GetResponseStream())using(var reader=new StreamReader(input)){
            string text=reader.ReadToEnd();if(text.Length>8000000)throw new IOException("Resposta do auxiliar muito grande.");return text.Length==0?null:Safe.Json.DeserializeObject(text);
        }
    }
    static string RelayIdentity(string text,out Uri uri){
        if(!Uri.TryCreate(text,UriKind.Absolute,out uri)||uri.Scheme!="relay"||uri.Port<=0||
            uri.UserInfo.Length!=0||uri.Fragment.Length!=0)return null;
        var match=Regex.Match(uri.Query,@"(?:\?|&)id=([A-Z2-7-]+)(?:&|\z)");
        string id=match.Success?match.Groups[1].Value:null;
        return P2PInvite.DeviceValid(id)?id:null;
    }
    internal void PrepareInviteRoute(){
        if(localOnly||inviteRouteClock==null)return;
        do{
            if(!Running)return;
            try{
                var status=Get("system/status");
                var services=status["connectionServiceStatus"] as Dictionary<string,object>;
                object raw;
                if(services!=null&&services.TryGetValue("dynamic+"+RelayPool,out raw)){
                    var relay=raw as Dictionary<string,object>;
                    object value;
                    var addresses=relay!=null&&relay.TryGetValue("wanAddresses",out value)?value as System.Collections.IEnumerable:null;
                    if(addresses!=null)foreach(string address in addresses.OfType<string>()){
                        Uri ignored;
                        string id=RelayIdentity(address,out ignored);
                        if(id!=null){InviteRelayDevice=id;return;}
                    }
                }
                InviteRelayDevice=null;
            }catch(WebException){return;}
            if(inviteRouteClock.ElapsedMilliseconds>=38000)return;
            Thread.Sleep(750);
        }while(true);
    }
    sealed class PublicRelayEntry { public string url {get;set;} }
    sealed class PublicRelayPool { public PublicRelayEntry[] relays {get;set;} }
    internal static string RegisteredRelay(string id,IEnumerable<string> urls){
        if(!P2PInvite.DeviceValid(id)||urls==null)return null;
        foreach(string address in urls){
            Uri uri;
            if(RelayIdentity(address,out uri)!=id)continue;
            IPAddress ip;
            if(!IPAddress.TryParse(uri.DnsSafeHost.Trim('[',']'),out ip)||IPAddress.IsLoopback(ip))continue;
            byte[] bytes=ip.GetAddressBytes();
            bool publicAddress=bytes.Length==16?(bytes[0]&0xe0)==0x20:
                bytes[0]!=0&&bytes[0]!=10&&bytes[0]!=127&&bytes[0]<224&&
                !(bytes[0]==100&&bytes[1]>=64&&bytes[1]<=127)&&
                !(bytes[0]==169&&bytes[1]==254)&&
                !(bytes[0]==172&&bytes[1]>=16&&bytes[1]<=31)&&
                !(bytes[0]==192&&(bytes[1]==168||bytes[1]==0))&&
                !(bytes[0]==198&&(bytes[1]==18||bytes[1]==19));
            if(publicAddress)return "relay://"+uri.GetComponents(UriComponents.HostAndPort,UriFormat.UriEscaped)+"/?id="+id;
        }
        return null;
    }
    string ResolveRelay(string id){
        if(localOnly||!P2PInvite.DeviceValid(id))return null;
        return ReadPublicRelay(id)??ReadPublicRelay(id);
    }
    static string ReadPublicRelay(string id){
        try{
            // The invitation supplies only a certificate ID. Network addresses come from the official registry.
            var request=(HttpWebRequest)WebRequest.Create(RelayPool+"/full");
            request.AllowAutoRedirect=false;request.Timeout=6000;request.ReadWriteTimeout=4500;
            using(var response=(HttpWebResponse)request.GetResponse())
            using(var input=response.GetResponseStream())
            using(var reader=new StreamReader(input,Encoding.UTF8)){
                if(response.StatusCode!=HttpStatusCode.OK)return null;
                var chars=new char[1024*1024+1];
                int length=reader.ReadBlock(chars,0,chars.Length);
                if(length==chars.Length)return null;
                var pool=Safe.Json.Deserialize<PublicRelayPool>(new string(chars,0,length));
                return pool==null||pool.relays==null?null:RegisteredRelay(id,pool.relays.Where(entry=>entry!=null).Select(entry=>entry.url));
            }
        }catch(WebException){return null;}
        catch(IOException){return null;}
        catch(ArgumentException){return null;}
        catch(InvalidOperationException){return null;}
    }
    internal void ConfigurePeer(string id,string endpoint=null){ConfigurePeerAddresses(id,new[]{endpoint??"dynamic"});}
    internal void EnsurePeer(string id){
        try{Get("config/devices/"+id);}
        catch(WebException ex){
            var response=ex.Response as HttpWebResponse;
            if(response==null||response.StatusCode!=HttpStatusCode.NotFound)throw;
            response.Dispose();
            ConfigurePeer(id);
        }
    }
    internal void ConfigurePeerWithRelay(string id,string relay){
        string endpoint=ResolveRelay(relay);
        ConfigurePeerAddresses(id,endpoint==null?new[]{"dynamic"}:new[]{endpoint,"dynamic"});
    }
    void ConfigurePeerAddresses(string id,string[] addresses){
        if(!P2PInvite.DeviceValid(id)||id==Device)throw new IOException("Escolha o convite do outro computador.");
        var device=Get("config/defaults/device");device["deviceID"]=id;device["name"]="Mod Loader peer";device["introducer"]=false;device["autoAcceptFolders"]=false;
        device["addresses"]=addresses;Request("PUT","config/devices/"+id,device);
    }
    internal void ConfigureFolder(string id,string path,string name,bool sender,string peer=null){
        ConfigureSharedFolder(id,path,name,sender,peer==null?new string[0]:new[]{peer},false);
    }
    internal void ConfigureSharedFolder(string id,string path,string name,bool sender,IEnumerable<string> peers,bool paused){
        if(!Regex.IsMatch(id??"","\\Aml-[a-f0-9]{32}\\z"))throw new IOException("Identificador de pacote inválido.");
        Safe.NoLinks(path);Directory.CreateDirectory(path);
        var folder=Get("config/defaults/folder");folder["id"]=id;folder["label"]=name;folder["path"]=Path.GetFullPath(path);
        folder["type"]=sender?"sendonly":"receiveonly";folder["paused"]=paused;folder["fsWatcherEnabled"]=true;folder["rescanIntervalS"]=60;
        // PUT merges folder defaults. Clear the peer secret explicitly; let Syncthing add itself.
        folder["devices"]=peers.Select(peer=>new{deviceID=peer,encryptionPassword=""}).ToArray();
        Request("PUT","config/folders/"+id,folder);
    }
    internal void Pause(string id,bool value){try{Request("PATCH","config/folders/"+id,new{paused=value});}catch(WebException ex){var response=ex.Response as HttpWebResponse;if(response==null||response.StatusCode!=HttpStatusCode.NotFound)throw;response.Dispose();}}
    internal void RemoveFolder(string id){try{Request("DELETE","config/folders/"+id,null);}catch(WebException ex){var response=ex.Response as HttpWebResponse;if(response==null||response.StatusCode!=HttpStatusCode.NotFound)throw;response.Dispose();}}
    public void Dispose(){
        try{
            if(process!=null&&!process.HasExited){
                try{Request("POST","system/shutdown",null);}catch(Exception){}
                process.WaitForExit(4000);
            }
        }finally{
            if(processJob!=null){processJob.Dispose();processJob=null;}
            if(process!=null){process.WaitForExit(4000);process.Dispose();process=null;}
            if(lease!=null){lease.Dispose();lease=null;}
        }
    }
}
