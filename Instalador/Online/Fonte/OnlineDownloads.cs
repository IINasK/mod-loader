using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public sealed class DownloadProgress {
    public string ModId,AssetId,Status;
    public long Bytes,Total;
    public int Percent { get { return Status=="Concluído"?100:Total<=0?0:(int)Math.Min(99,Bytes*100.0/Total); } }
}

public sealed class DownloadControl:IDisposable {
    readonly object sync=new object();
    readonly ManualResetEventSlim gate=new ManualResetEventSlim(true);
    readonly CancellationTokenSource cancel=new CancellationTokenSource();
    bool paused;
    public bool IsPaused { get { lock(sync)return paused; } }
    public bool IsCancelled { get { return cancel.IsCancellationRequested; } }
    internal CancellationToken Token { get { return cancel.Token; } }
    public void Pause(){lock(sync){if(IsCancelled)return;paused=true;gate.Reset();}}
    public void Resume(){lock(sync){paused=false;gate.Set();}}
    public void Cancel(){cancel.Cancel();Resume();}
    public void Checkpoint(){cancel.Token.ThrowIfCancellationRequested();gate.Wait(cancel.Token);cancel.Token.ThrowIfCancellationRequested();}
    internal void Checkpoint(Action pausedNotice){if(IsPaused&&pausedNotice!=null)pausedNotice();Checkpoint();}
    public void Dispose(){cancel.Dispose();gate.Dispose();}
}

public sealed partial class NexusClient {
    public void Download(FixedMod m,FixedAsset a,DownloadCache cache,DownloadControl control,Action<DownloadProgress> progress) {
        if(control==null)throw new ArgumentNullException("control");
        if(progress==null)throw new ArgumentNullException("progress");
        long bytes=0;
        Action<string> notify=status=>progress(new DownloadProgress{ModId=m.Id,AssetId=a.Id,Bytes=bytes,Total=a.Size,Status=status});
        string temp=null;
        try {
            control.Checkpoint(()=>notify("Pausado"));
            if(cache.Contains(a)){bytes=a.Size;notify("Concluído");return;}
            notify("Conectando");string url=DownloadUrl(m,a);
            control.Checkpoint(()=>notify("Pausado"));
            temp=cache.DownloadTemporaryPath();
            Uri uri=new Uri(url);
            for(int redirect=0;redirect<7;redirect++) {
                control.Checkpoint(()=>notify("Pausado"));
                if(!Allowed(uri,true))throw new IOException("Redirecionamento para servidor não permitido.");
                var request=(HttpWebRequest)WebRequest.Create(uri);request.AllowAutoRedirect=false;request.Timeout=30000;request.ReadWriteTimeout=30000;request.UserAgent="DouglasCyberpunkCoop-Testing/0.1.0";
                using(control.Token.Register(()=>request.Abort()))
                using(var response=(HttpWebResponse)request.GetResponse()) {
                    int status=(int)response.StatusCode;
                    if(status>=300&&status<400){uri=new Uri(uri,response.Headers["Location"]);continue;}
                    if(status!=200)throw new IOException("Download recusado pelo servidor.");
                    if(response.ContentLength>=0&&response.ContentLength!=a.Size)throw new IOException("Tamanho do download diferente do arquivo fixado.");
                    using(var input=response.GetResponseStream())
                    using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                        Transfer(input,output,a.Size,control,n=>{bytes=n;notify(control.IsPaused?"Pausado":"Baixando");});
                    }
                    control.Checkpoint(()=>notify("Pausado"));notify("Verificando");
                    CommitDownload(cache,a,temp,control);
                    bytes=a.Size;notify("Concluído");return;
                }
            }
            throw new IOException("Número excessivo de redirecionamentos.");
        }catch(OperationCanceledException){notify("Cancelado");throw;}
        catch(WebException){
            if(control.IsCancelled){notify("Cancelado");throw new OperationCanceledException("Download cancelado.");}
            notify("Falhou");throw new IOException("Download interrompido. Tente novamente; o arquivo será reiniciado e conferido antes de instalar.");
        }catch{notify("Falhou");throw;}
        finally{if(temp!=null&&File.Exists(temp)){Safe.NoLinks(temp);File.Delete(temp);}}
    }

    // shortcut: pausar conserva o stream desta execução; se o servidor encerrar a conexão, uma nova tentativa reinicia o arquivo.
    internal static void Transfer(Stream input,Stream output,long expected,DownloadControl control,Action<long> report) {
        if(expected<0)throw new IOException("Tamanho de download inválido.");
        long total=0;var buffer=new byte[65536];DateTime last=DateTime.MinValue;
        report(0);
        while(true) {
            control.Checkpoint(()=>report(total));
            int n=input.Read(buffer,0,buffer.Length);if(n==0)break;
            control.Checkpoint(()=>report(total));
            total+=n;if(total>expected)throw new IOException("Download maior que o arquivo fixado.");
            output.Write(buffer,0,n);
            if((DateTime.UtcNow-last).TotalMilliseconds>=150){last=DateTime.UtcNow;report(total);}
        }
        if(total!=expected)throw new IOException("Download incompleto. Nenhum arquivo foi aceito no cache.");
        control.Checkpoint(()=>report(total));report(total);
    }

    internal static void CommitDownload(DownloadCache cache,FixedAsset asset,string temp,DownloadControl control) {
        Safe.NoLinks(temp);
        if(new FileInfo(temp).Length!=asset.Size||Safe.Hash(temp)!=asset.Sha256)throw new IOException("Download incompleto ou hash diferente. Arquivo recusado.");
        control.Checkpoint();if(cache.SourcesOnly){cache.KeepOriginalDownload(asset,temp);return;}string dest=cache.PathFor(asset);Safe.NoLinks(dest);
        if(cache.Contains(asset))return;
        if(File.Exists(dest))File.Delete(dest);
        File.Move(temp,dest);
    }
}

public static class OnlineDownloadTests {
    public static void Run(string parent) {
        string root=Path.Combine(Path.GetFullPath(parent),"downloads-testes-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var passed=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("TESTE DOWNLOAD FALHOU: "+name);passed.Add(name);};
        byte[] data=Enumerable.Range(0,196608).Select(i=>(byte)(i%251)).ToArray();
        using(var control=new DownloadControl())using(var input=new MemoryStream(data))using(var output=new MemoryStream()) {
            control.Pause();var entered=new ManualResetEventSlim(false);
            var work=Task.Run(()=>{entered.Set();NexusClient.Transfer(input,output,data.Length,control,n=>{});});
            try{check(entered.Wait(2000)&&!work.Wait(100)&&output.Length==0,"Pausa antes do primeiro byte não transfere dados");control.Resume();check(work.Wait(2000)&&output.ToArray().SequenceEqual(data),"Retomar conclui o mesmo stream sem perder bytes");}finally{control.Resume();work.Wait(2000);entered.Dispose();}
        }
        using(var control=new DownloadControl())using(var input=new MemoryStream(data))using(var output=new MemoryStream()) {
            var paused=new ManualResetEventSlim(false);bool requested=false;
            var work=Task.Run(()=>NexusClient.Transfer(input,output,data.Length,control,n=>{if(n>0&&!requested){requested=true;control.Pause();paused.Set();}}));
            try{check(paused.Wait(2000)&&!work.Wait(100)&&output.Length>0&&output.Length<data.Length,"Pausa durante transferência conserva bytes já recebidos");control.Resume();check(work.Wait(2000)&&output.ToArray().SequenceEqual(data),"Retomar após pausa intermediária mantém conteúdo integral");}finally{control.Resume();work.Wait(2000);paused.Dispose();}
        }
        using(var control=new DownloadControl()) {
            control.Pause();var work=Task.Run(()=>{try{control.Checkpoint();return false;}catch(OperationCanceledException){return true;}});
            control.Cancel();check(work.Wait(2000)&&work.Result,"Cancelar libera uma transferência pausada");
        }
        foreach(long expected in new[]{data.Length-1L,data.Length+1L})using(var control=new DownloadControl())using(var input=new MemoryStream(data))using(var output=new MemoryStream()) {
            bool refused=false;try{NexusClient.Transfer(input,output,expected,control,n=>{});}catch(IOException){refused=true;}
            check(refused,expected<data.Length?"Bytes excedentes recusados":"Download truncado recusado");
        }
        var cache=new DownloadCache(Path.Combine(root,"cache"));Directory.CreateDirectory(cache.Root);
        string original=Path.Combine(root,"original.zip");File.WriteAllBytes(original,data);
        var asset=new FixedAsset{Id="a0",Name="fixture.zip",Size=data.Length,Sha256=Safe.Hash(original),ModId=1,FileId=1,Format="zip"};
        string wrong=Path.Combine(root,"wrong.part");File.WriteAllBytes(wrong,data.Select(b=>(byte)(b^1)).ToArray());
        using(var control=new DownloadControl()){
            bool refused=false;try{NexusClient.CommitDownload(cache,asset,wrong,control);}catch(IOException){refused=true;}
            check(refused&&!File.Exists(cache.PathFor(asset)),"Hash divergente nunca entra no cache");
            string ready=Path.Combine(root,"ready.part");File.Copy(original,ready);NexusClient.CommitDownload(cache,asset,ready,control);
            check(cache.Contains(asset),"Download completo entra no cache somente após hash e tamanho");
            var frames=new List<DownloadProgress>();new NexusClient().Download(new FixedMod{Id="Extra"},asset,cache,control,p=>frames.Add(p));
            check(frames.Count==1&&frames[0].Percent==100&&frames[0].Status=="Concluído"&&frames[0].Bytes==data.Length,"Cache válido informa conclusão individual sem acessar rede ou exigir login");
        }
        Safe.WriteJson(Path.Combine(root,"resultado.json"),new{Passed=passed.Count,Tests=passed,NetworkUsed=false,RealGameTouched=false});
        File.WriteAllText(Path.Combine(parent,"ultimo-teste-downloads.txt"),root);
    }
}
