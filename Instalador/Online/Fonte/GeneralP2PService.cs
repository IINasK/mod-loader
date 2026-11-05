using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

internal sealed class P2PSession {
    public string Id, Name, Hash, Peer;
    public bool Sender, Paused;
    public override string ToString(){return GeneralStyle.T(Sender?"Enviando · ":"Recebendo · ",Sender?"Sending · ":"Receiving · ")+Name;}
}
internal sealed class P2PService : IDisposable {
    internal readonly string Root, Store;
    internal readonly SyncthingEngine Engine;
    internal readonly List<P2PSession> Sessions;
    readonly string statePath;
    Timer pump;
    int pumping;
    volatile bool disposed;
    P2PSession[] receivers=new P2PSession[0];
    readonly object manifestsLock=new object();
    readonly Dictionary<string,P2PManifest> allowedManifests=new Dictionary<string,P2PManifest>();
    internal P2PService(string root,string store,bool localOnly=false){
        Root=Path.GetFullPath(root);Store=Path.GetFullPath(store);Safe.NoLinks(Root);Safe.NoLinks(Store);
        if(Root.TrimEnd('\\').Equals(Store.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))throw new IOException("Separe os arquivos privados da pasta de transferência.");
        statePath=Safe.In(Root,"sessions.json");Engine=new SyncthingEngine(Safe.In(Root,"syncthing"),localOnly);
        if(File.Exists(statePath)){
            if(new FileInfo(statePath).Length>1000000)throw new IOException("Registro de compartilhamentos inválido.");
            Sessions=Safe.Json.Deserialize<List<P2PSession>>(File.ReadAllText(statePath));
        }else Sessions=new List<P2PSession>();
        if(Sessions==null||Sessions.Count>100)throw new IOException("Registro de compartilhamentos inválido.");
        var ids=new HashSet<string>();foreach(var s in Sessions){Validate(s);if(!ids.Add(s.Id))throw new IOException("Compartilhamento repetido.");}
    }
    static void Validate(P2PSession s){
        if(s==null||!Regex.IsMatch(s.Id??"","\\Aml-[a-f0-9]{32}\\z")||!P2PPack.HashValid(s.Hash)||String.IsNullOrWhiteSpace(s.Name)||s.Name.Length>300||s.Name.Any(Char.IsControl)||(s.Peer!=null&&!P2PInvite.DeviceValid(s.Peer)))throw new IOException("Registro de compartilhamento inválido.");
    }
    internal string Folder(P2PSession s){Validate(s);return Safe.In(Store,s.Id);}
    internal string PrivateFolder(P2PSession s){Validate(s);return Safe.In(Root,"received");}
    internal void Start(Action<string> report,string testExe=null){
        if(Engine.Running)return;
        try{
            Engine.Start(testExe??SyncthingEngine.Ensure(Root,report));
            lock(manifestsLock)allowedManifests.Clear();
            foreach(var s in Sessions){if(s.Peer!=null)Engine.ConfigurePeer(s.Peer);Engine.ConfigureFolder(s.Id,Folder(s),s.Name,s.Sender,s.Peer);if(s.Paused)Engine.Pause(s.Id,true);}
            receivers=Sessions.Where(s=>!s.Sender).ToArray();
            if(pump!=null)pump.Dispose();pump=new Timer(_=>Pump(),null,2000,3000);
        }catch{Engine.Dispose();throw;}
    }
    void Save(){Safe.WriteJson(statePath,Sessions);receivers=Sessions.Where(s=>!s.Sender).ToArray();}
    void Pump(){
        if(disposed||Interlocked.Exchange(ref pumping,1)!=0)return;
        try{foreach(var s in receivers){if(disposed)break;if(s.Paused)continue;try{if(File.Exists(Safe.In(Folder(s),"pack.json")))AllowFiles(s);}catch(Exception ex){OnlineDiagnostics.Record("p2p.waiting",null,"Aguardando manifesto conferido.",new{ErrorType=ex.GetType().Name});}}}
        finally{Interlocked.Exchange(ref pumping,0);}
    }
    internal P2PSession Share(GeneralModState state,IEnumerable<string> ids,FixedCatalog catalog,DownloadCache cache,Action<string> report){
        if(!Engine.Running||Sessions.Count>=100)throw new IOException("Inicie a conexão ou remova compartilhamentos antigos.");
        var session=new P2PSession{Id="ml-"+Guid.NewGuid().ToString("N"),Sender=true};
        // Never allow exporting original archives directly from the active destination tree.
        string folder=Safe.In(Store,session.Id);
        if(!String.IsNullOrEmpty(state.Destination)&&Path.GetFullPath(folder).StartsWith(Path.GetFullPath(state.Destination).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("A pasta de compartilhamento deve ficar fora da instalação do jogo.");
        try{
            var pack=P2PPack.Create(folder,state,ids,catalog,cache,report);session.Name=pack.Name;session.Hash=Safe.Hash(Safe.In(folder,"pack.json"));
            Engine.ConfigureFolder(session.Id,folder,session.Name,true);Sessions.Add(session);Save();return session;
        }catch{
            Sessions.Remove(session);try{Engine.RemoveFolder(session.Id);}catch(Exception){}
            try{P2PPack.DeleteSnapshot(folder);}catch(Exception){}throw;
        }
    }
    internal string Invitation(P2PSession session){return new P2PInvite{Kind=session.Sender?"offer":"accept",Device=Engine.Device,Folder=session.Id,ManifestHash=session.Hash,Name=session.Name}.Encode();}
    internal P2PSession Accept(string code){
        var invite=P2PInvite.Decode(code);if(invite.Kind!="offer")throw new IOException("Cole o convite de quem está enviando o pacote.");
        if(!Engine.Running||invite.Device==Engine.Device)throw new IOException("Use o convite do outro computador.");
        var existing=Sessions.FirstOrDefault(s=>s.Id==invite.Folder);
        if(existing!=null){if(existing.Hash!=invite.ManifestHash||existing.Sender||existing.Peer!=invite.Device)throw new IOException("Este convite conflita com outro compartilhamento.");return existing;}
        if(Sessions.Count>=100)throw new IOException("Remova compartilhamentos antigos antes de receber outro.");
        var session=new P2PSession{Id=invite.Folder,Name=invite.Name,Hash=invite.ManifestHash,Peer=invite.Device};
        string folder=Folder(session);Directory.CreateDirectory(folder);
        // Receive the pinned manifest first. Other remote files are ignored until its allow-list is validated.
        File.WriteAllText(Safe.In(folder,".stignore"),"!pack.json\n*\n",new UTF8Encoding(false));
        Engine.ConfigurePeer(session.Peer);Engine.ConfigureFolder(session.Id,folder,session.Name,false,session.Peer);
        Sessions.Add(session);try{Save();}catch{Sessions.Remove(session);Engine.RemoveFolder(session.Id);throw;}return session;
    }
    internal P2PSession Authorize(string code){
        var answer=P2PInvite.Decode(code);if(answer.Kind!="accept")throw new IOException("Cole a resposta enviada por seu amigo.");
        var s=Sessions.SingleOrDefault(x=>x.Id==answer.Folder&&x.Sender);
        if(s==null||s.Hash!=answer.ManifestHash||answer.Device==Engine.Device)throw new IOException("A resposta não pertence a este pacote.");
        if(s.Peer!=null&&s.Peer!=answer.Device)throw new IOException("Este convite já está vinculado a outro amigo. Crie outro compartilhamento.");
        Engine.ConfigurePeer(answer.Device);Engine.ConfigureFolder(s.Id,Folder(s),s.Name,true,answer.Device);if(s.Paused)Engine.Pause(s.Id,true);s.Peer=answer.Device;Save();return s;
    }
    internal P2PManifest AllowFiles(P2PSession s){
        lock(manifestsLock){
        P2PManifest approved;if(allowedManifests.TryGetValue(s.Id,out approved))return approved;
        var m=P2PPack.Read(Folder(s),s.Hash);
        var ignores=new List<string>{"!pack.json"};ignores.AddRange(m.Files.Select(f=>"!"+f.File));ignores.Add("*");
        string text=String.Join("\n",ignores)+"\n",path=Safe.In(Folder(s),".stignore");
        if(!File.Exists(path)||File.ReadAllText(path)!=text){
            Engine.Request("POST","db/ignores?folder="+s.Id,new{ignore=ignores});
        }
        allowedManifests.Add(s.Id,m);return m;
        }
    }
    internal void Pause(P2PSession s){Engine.Pause(s.Id,!s.Paused);s.Paused=!s.Paused;Save();}
    internal void Remove(P2PSession s){
        Validate(s);Engine.RemoveFolder(s.Id);Sessions.Remove(s);Save();
        lock(manifestsLock)allowedManifests.Remove(s.Id);
        if(s.Peer!=null&&!Sessions.Any(other=>other.Peer==s.Peer))Engine.Request("DELETE","config/devices/"+s.Peer,null);
        // Only this session's transport snapshot is removed; private imports, originals and installations remain.
        P2PPack.DeleteSnapshot(Folder(s));
    }
    public void Dispose(){disposed=true;if(pump!=null){pump.Dispose();pump=null;}Engine.Dispose();}
}
