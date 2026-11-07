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
    readonly string home;
    readonly bool localOnly;
    string address,key;
    Process process;
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
        Directory.CreateDirectory(home);lease=new FileStream(Safe.In(home,"engine.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
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
            if(!localOnly){var relay=xml.CreateElement("listenAddress");relay.InnerText="dynamic+https://relays.syncthing.net/endpoint";options.AppendChild(relay);}
            set("globalAnnounceEnabled",localOnly?"false":"true");set("localAnnounceEnabled",localOnly?"false":"true");set("relaysEnabled",localOnly?"false":"true");set("natEnabled",localOnly?"false":"true");
            set("urAccepted","-1");set("autoUpgradeIntervalH","0");set("startBrowser","false");set("crashReportingEnabled","false");
            xml.Save(config);address="http://127.0.0.1:"+guiPort;key=Guid.NewGuid().ToString("N")+Guid.NewGuid().ToString("N");
            var start=Launch(exe,"serve --home \""+home+"\" --no-browser --no-restart --no-upgrade --log-max-size 1048576 --log-max-old-files 2 --log-level WARN");
            start.EnvironmentVariables["STGUIADDRESS"]=address;start.EnvironmentVariables["STGUIAPIKEY"]=key;
            process=Process.Start(start);var timer=Stopwatch.StartNew();
            while(timer.ElapsedMilliseconds<25000){
                if(process.HasExited)throw new IOException("O auxiliar P2P não iniciou. Verifique se outra instância está usando o compartilhamento.");
                try{Device=Convert.ToString(Get("system/status")["myID"]);if(P2PInvite.DeviceValid(Device))return;}catch(WebException){}
                Thread.Sleep(150);
            }
            throw new IOException("Tempo esgotado ao conectar o auxiliar P2P.");
        }catch{Dispose();throw;}
    }
    internal Dictionary<string,object> Get(string route){return Request("GET",route,null) as Dictionary<string,object>;}
    internal object Request(string method,string route,object body){
        var request=(HttpWebRequest)WebRequest.Create(address+"/rest/"+route);request.Proxy=null;request.AllowAutoRedirect=false;request.Timeout=5000;request.ReadWriteTimeout=5000;request.Method=method;request.Headers["X-API-Key"]=key;
        if(body!=null){byte[] bytes=Encoding.UTF8.GetBytes(Safe.Json.Serialize(body));request.ContentType="application/json";request.ContentLength=bytes.Length;using(var output=request.GetRequestStream())output.Write(bytes,0,bytes.Length);}
        using(var response=request.GetResponse())using(var input=response.GetResponseStream())using(var reader=new StreamReader(input)){
            string text=reader.ReadToEnd();if(text.Length>8000000)throw new IOException("Resposta do auxiliar muito grande.");return text.Length==0?null:Safe.Json.DeserializeObject(text);
        }
    }
    internal void ConfigurePeer(string id,string endpoint=null){
        if(!P2PInvite.DeviceValid(id)||id==Device)throw new IOException("Escolha o convite do outro computador.");
        var device=Get("config/defaults/device");device["deviceID"]=id;device["name"]="Mod Loader peer";device["introducer"]=false;device["autoAcceptFolders"]=false;
        device["addresses"]=new[]{endpoint??"dynamic"};Request("PUT","config/devices/"+id,device);
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
        if(process!=null){try{try{Request("POST","system/shutdown",null);}catch(Exception){}if(!process.HasExited&&!process.WaitForExit(4000))process.Kill();}finally{process.Dispose();process=null;}}
        if(lease!=null){lease.Dispose();lease=null;}
    }
}
