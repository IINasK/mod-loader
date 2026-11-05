using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

public static class OnlineDiagnostics {
    static readonly object gate=new object();
    static readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=2000000};
    static string file,session;
    static bool enabled;
    static FileStream active;
    static long sequence;
    const long MaxBytes=8L*1024*1024;
    public static string FilePath {get{lock(gate)return file;}}
    public static bool Enabled {get{lock(gate)return enabled&&file!=null;}}
    public static string Issue {get;private set;}
    public static void Start(string folder){
        lock(gate){
            if(active!=null){active.Dispose();active=null;}
            file=null;enabled=false;Issue=null;sequence=0;session=Guid.NewGuid().ToString("N");
            try{
                Safe.NoLinks(folder);Directory.CreateDirectory(folder);StorageMaintenance.RotateLogs(folder,7,64L*1024*1024-MaxBytes);
                string path=Safe.In(folder,"diagnostico-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+session.Substring(0,8)+".jsonl");
                using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read)){}
                active=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
                file=path;enabled=true;
            }catch(Exception ex){Issue="Não foi possível criar o registro: "+ex.GetType().Name;}
        }
        Record("session.start",null,"Registro de diagnóstico iniciado.",new{Process=Process.GetCurrentProcess().Id,MaxBytes=MaxBytes});
    }
    public static void Stop(){Record("session.stop",null,"Registro encerrado.");lock(gate){enabled=false;if(active!=null){active.Dispose();active=null;}}}
    public static void Record(string eventName,string modId,string detail,object data=null){
        lock(gate){
            if(!enabled||file==null)return;
            try{
                Safe.NoLinks(file);if(new FileInfo(file).Length>MaxBytes){Issue="O registro atingiu o limite de 8 MB; reabra o app para uma nova sessão.";enabled=false;return;}
                string line=json.Serialize(new{Utc=DateTime.UtcNow.ToString("o"),Local=DateTimeOffset.Now.ToString("o"),Sequence=++sequence,Session=session,Thread=System.Threading.Thread.CurrentThread.ManagedThreadId,Event=eventName,Mod=modId,Detail=detail,Data=data});
                using(var stream=new FileStream(file,FileMode.Append,FileAccess.Write,FileShare.Read))using(var writer=new StreamWriter(stream,new UTF8Encoding(false)))writer.WriteLine(line);
            }catch(Exception ex){Issue="Falha ao gravar o registro: "+ex.GetType().Name;enabled=false;}
        }
    }
}
