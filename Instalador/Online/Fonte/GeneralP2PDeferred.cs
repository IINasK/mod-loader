using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

internal sealed partial class P2PService {
    // Deferred links authorize a connection first; HMAC binds its finished ZIP later.
    internal sealed class ReadyPack {
        public int Schema=1;
        public string Hash,Tag;
        public long Size;
    }
    internal P2PSession BeginDeferred(GeneralModState state,System.Collections.Generic.IEnumerable<string> ids,bool lifecycle=false){
        lock(startGate){
        if(!Engine.Running||Sessions.Count>=100)throw new IOException("Inicie a conexão ou remova compartilhamentos antigos.");
        string[] chosen=ids.Distinct(StringComparer.Ordinal).ToArray();
        if(chosen.Length==0||chosen.Any(id=>!state.List.Mods.Any(m=>m.Id==id)))throw new IOException("Escolha um preset com mods para compartilhar.");
        byte[] key=new byte[32];using(var random=RandomNumberGenerator.Create())random.GetBytes(key);
        var preset=state.List.Presets.FirstOrDefault(p=>p.Id==state.SelectedPreset);
        var session=new P2PSession{Id="ml-"+Guid.NewGuid().ToString("N"),Sender=true,Mode="installed-zip",Deferred=true,Automatic=true,Lifecycle=lifecycle,Destination=state.Destination,Hash=Safe.Hex(key),Name=preset==null?"Pacote instalado":preset.Name,SelectedMods=chosen};
        string folder=Folder(session);
        if(!String.IsNullOrEmpty(state.Destination)&&Path.GetFullPath(folder).StartsWith(Path.GetFullPath(state.Destination).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("A transferência deve ficar fora da pasta do jogo.");
        if(Directory.Exists(folder)||File.Exists(folder))throw new IOException("Destino da transferência já existe.");
        Directory.CreateDirectory(folder);
        try{File.WriteAllText(Safe.In(folder,".stignore"),"!ready.json\n!pack.zip\n*\n",new UTF8Encoding(false));Engine.ConfigureFolder(session.Id,folder,session.Name,true);AddSession(session);return session;}
        catch{Sessions.Remove(session);try{Engine.RemoveFolder(session.Id);}catch(Exception){}try{P2PPack.DeleteSnapshot(folder);}catch(Exception){}throw;}
        }
    }
    internal void PrepareDeferred(P2PSession session,GeneralModState state,FixedCatalog catalog,string appData,bool includeConfigs,Action<string> report){
        if(!SnapshotSessions().Contains(session)||!session.Sender||!session.Deferred||session.Completed)throw new IOException("Envio antecipado inválido.");
        string zip=Safe.In(Folder(session),"pack.zip"),part=zip+".part";Safe.NoLinks(zip);Safe.NoLinks(part);
        DateTime nextCheck=DateTime.MinValue;bool remotePaused=false;
        Action checkpoint=()=>{
            while(true){
                if(disposed)throw new IOException("Preparação interrompida; sessão preservada.");
                if(DateTime.UtcNow>=nextCheck){
                    nextCheck=DateTime.UtcNow.AddSeconds(1);
                    try{
                        remotePaused=session.Lifecycle
                            ?!session.Recipients.Any(p=>!Terminal(p.Phase)&&RecipientReady(session,p.Device))
                            :TransferStatus(session).RemotePaused;
                    }catch(System.Net.WebException){remotePaused=true;}
                }
                if(!session.Paused&&!remotePaused)return;
                System.Threading.Thread.Sleep(100);
            }
        };
        session.PreparationBytes=0;session.PreparationTotal=0;
        session.Stage=GeneralStyle.T("Conferindo e preparando o pacote…","Verifying and preparing the pack…");session.Error=null;Save();
        try{
            if(session.PayloadHash==null){
                if(File.Exists(zip))throw new IOException("Pacote sem registro de conclusão; preservado.");
                if(File.Exists(part))File.Delete(part);
                if(report!=null)report(session.Stage);
                InstalledSnapshot.Create(part,state,session.SelectedMods,catalog,appData,includeConfigs,report,true,checkpoint,(done,total)=>{session.PreparationBytes=done;session.PreparationTotal=total;});
                checkpoint();
                string hash=Safe.Hash(part);
                lock(Sessions){session.PayloadHash=hash;session.Size=new FileInfo(part).Length;Save();}
            }
            if(!File.Exists(zip)&&File.Exists(part)){
                if(new FileInfo(part).Length!=session.Size||Safe.Hash(part)!=session.PayloadHash)throw new IOException("Preparação interrompida divergente; preservada.");
                File.Move(part,zip);
            }
            if(!File.Exists(zip)||new FileInfo(zip).Length!=session.Size||Safe.Hash(zip)!=session.PayloadHash)throw new IOException("O pacote preparado foi alterado ou removido. Preserve os registros.");
            var ready=new ReadyPack{Hash=session.PayloadHash,Size=session.Size};ready.Tag=ReadyTag(session.Hash,session.Id,ready.Hash,ready.Size);
            Safe.WriteJson(Safe.In(Folder(session),"ready.json"),ready);
            lock(startGate)Engine.Request("POST","db/scan?folder="+session.Id,null);
            session.Error=null;session.Stage=null;Save();
        }catch(Exception ex){session.Stage=null;session.Error=ex.Message;Save();throw;}
    }
    internal static string ReadyTag(string key,string id,string hash,long size){
        if(!P2PPack.HashValid(key)||!P2PPack.HashValid(hash))throw new IOException("Assinatura de preparação inválida.");
        byte[] bytes=new byte[32];for(int i=0;i<bytes.Length;i++)bytes[i]=Byte.Parse(key.Substring(i*2,2),NumberStyles.HexNumber,CultureInfo.InvariantCulture);
        using(var mac=new HMACSHA256(bytes))return Safe.Hex(mac.ComputeHash(Encoding.UTF8.GetBytes("mod-loader-ready-1\n"+id+"\n"+hash+"\n"+size.ToString(CultureInfo.InvariantCulture))));
    }
    internal static ReadyPack ReadReady(string file,string key,string id){
        Safe.NoLinks(file);if(!File.Exists(file))return null;
        if(new FileInfo(file).Length>2048)throw new IOException("Descritor de pacote excessivo.");
        ReadyPack ready;try{ready=Safe.Json.Deserialize<ReadyPack>(File.ReadAllText(file));}catch(ArgumentException ex){throw new IOException("Descritor de pacote inválido.",ex);}
        if(ready==null||ready.Schema!=1||!P2PPack.HashValid(ready.Hash)||!P2PPack.HashValid(ready.Tag)||ready.Size<=0||ready.Size>P2PPack.MaxTotal)throw new IOException("Descritor de pacote inválido.");
        string expected=ReadyTag(key,id,ready.Hash,ready.Size);int difference=0;for(int i=0;i<expected.Length;i++)difference|=expected[i]^ready.Tag[i];
        if(difference!=0)throw new IOException("O pacote não corresponde à autorização do convite.");return ready;
    }
    bool ResolveDeferred(P2PSession session){
        var ready=ReadReady(Safe.In(Folder(session),"ready.json"),session.Hash,session.Id);if(ready==null)return false;
        if(session.PayloadHash!=null&&(session.PayloadHash!=ready.Hash||session.Size!=ready.Size))throw new IOException("O remetente mudou o pacote depois da autorização; preservado.");
        if(session.PayloadHash==null){lock(Sessions){session.PayloadHash=ready.Hash;session.Size=ready.Size;Save();}}return true;
    }
    internal bool DeferredPrepared(P2PSession session){return session.PayloadHash!=null&&File.Exists(Safe.In(Folder(session),"pack.zip"))&&File.Exists(Safe.In(Folder(session),"ready.json"));}
}