using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

public static class FacialDownloadQueueTests {
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static object Get(object target,string name){return target.GetType().GetField(name,Private).GetValue(target);}
    static void Set(object target,string name,object value){target.GetType().GetField(name,Private).SetValue(target,value);}
    static object Call(object target,string name,params object[] args){return target.GetType().GetMethod(name,Private).Invoke(target,args);}
    static void Pump(Task task){var clock=Stopwatch.StartNew();while(!task.IsCompleted){if(clock.Elapsed>TimeSpan.FromSeconds(12))throw new Exception("Tick facial excedeu12segundos.");Application.DoEvents();Thread.Sleep(5);}task.GetAwaiter().GetResult();Application.DoEvents();}
    static void Until(Func<bool> done){var clock=Stopwatch.StartNew();while(!done()){if(clock.Elapsed>TimeSpan.FromSeconds(12))throw new Exception("Timer facial nao completou a continuacao em12segundos.");Application.DoEvents();Thread.Sleep(5);}Application.DoEvents();}
    static void Tick(OnlineWindow window){typeof(System.Windows.Forms.Timer).GetMethod("OnTick",Private).Invoke(Get(window,"downloadWatchTimer"),new object[]{EventArgs.Empty});Application.DoEvents();}
    static FixedAsset Package(string root,string name,string content,int id){
        string path=Path.Combine(root,name);
        using(var zip=ZipFile.Open(path,ZipArchiveMode.Create)){
            var entry=zip.CreateEntry("data/"+id+".txt");
            using(var output=new StreamWriter(entry.Open(),new UTF8Encoding(false)))output.Write(content);
        }
        return new FixedAsset{Id="face-"+id+"-"+(id+1),Name=name,ModId=id,FileId=id+1,Format="zip",Size=new FileInfo(path).Length,Sha256=Safe.Hash(path),Url="https://www.nexusmods.com/cyberpunk2077/mods/"+id+"?tab=files&file_id="+(id+1)};
    }
    static string Folder(string root,string name){string path=Path.Combine(root,name);Directory.CreateDirectory(path);return path;}
    static void Copy(string source,string folder,string name){File.Copy(source,Path.Combine(folder,name));}
    public static void Run(string parent){
        string root=Path.Combine(Path.GetFullPath(parent),"face-queue-"+Guid.NewGuid().ToString("N").Substring(0,12));Directory.CreateDirectory(root);
        var passed=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("TESTE FACE QUEUE FALHOU: "+name);passed.Add(name);};
        Action<Action,string> refuse=(work,name)=>{bool rejected=false;try{work();}catch(IOException){rejected=true;}catch(ArgumentException){rejected=true;}check(rejected,name);};
        var facialList=FacialDownload.List();var facialAssets=facialList.Select(item=>item.Asset()).ToList();
        var fileIds=new[]{99414,95893,30004,56673,12061,160715,39563,39566,94454,58122,35570,58415,8720,8374};
        check(facialAssets.Count==14&&facialAssets.All(asset=>FixedData.HashValid(asset.Sha256))&&facialAssets.Select(asset=>asset.Sha256).Distinct().Count()==14,"Quatorze preparacoes faciais possuem identidadesSHA fixadas, validas e unicas");
        check(facialList.Select(item=>item.FileId).SequenceEqual(fileIds)&&facialList.Zip(facialAssets,(item,asset)=>asset.Id==item.Id&&asset.ModId==item.ModId&&asset.FileId==item.FileId&&asset.Url==item.Url&&!String.IsNullOrWhiteSpace(asset.Name)&&Path.GetFileName(asset.Name)==asset.Name).All(ok=>ok),"Quatorze fontes correspondem exatamente aos arquivos oficiais e variantes selecionados, sem corpo ou efeito removido");
        var knownSizes=new Dictionary<int,long>{{99414,59984179},{95893,480973},{30004,2588383}};
        check(facialAssets.Count(asset=>asset.Size>0)==3&&facialAssets.Where(asset=>asset.Size>0).All(asset=>knownSizes.ContainsKey(asset.FileId)&&knownSizes[asset.FileId]==asset.Size),"Tres tamanhos medidos nos originais sao preservados; outros onze continuam sem tamanho inventado");
        var a=Package(root,"primeiro.zip","Dados estaticos de rosto A, nao executar.",990001);
        var b=Package(root,"segundo.zip",new string('b',503)+" Dados estaticos de rosto B, nao executar.",990003);
        string sourceA=Path.Combine(root,a.Name),sourceB=Path.Combine(root,b.Name);string hashA=Safe.Hash(sourceA),hashB=Safe.Hash(sourceB);
        string downloads=Folder(root,"downloads");var cache=new DownloadCache(Path.Combine(root,"cache"));
        var opens=new List<FixedAsset>();var registered=new List<string>();
        var queue=new FacialDownloadQueue(downloads,cache,found=>registered.Add(found.Asset.Sha256));
        DateTime now=DateTime.UtcNow;
        queue.Start(new[]{a,b},item=>opens.Add(item));
        check(queue.IsArmed&&queue.CompletedCount==0&&opens.Count==1&&opens[0].Sha256==a.Sha256,"Iniciar fila de2pacotes abre apenas a primeira pagina e arma o monitor existente");
        string partial=Path.Combine(downloads,"primeiro.zip.crdownload");File.Copy(sourceA,partial);
        check(queue.Poll(now,null)==null&&queue.Poll(now.AddSeconds(4),null)==null&&opens.Count==1&&!cache.Contains(a),"Arquivo parcial com bytes corretos e ignorado; proxima pagina nao abre");
        string wrong=Path.Combine(downloads,a.Name);File.WriteAllBytes(wrong,new byte[(int)a.Size]);
        queue.Poll(now.AddSeconds(5),null);
        check(queue.Poll(now.AddSeconds(7),null)==null&&opens.Count==1&&queue.CompletedCount==0&&!cache.Contains(a),"Mesmo nome e tamanho com hash de outra variante nao e importado nem avanca fila");
        Copy(sourceB,downloads,"segundo-baixado-antes.zip");
        check(queue.Poll(now.AddSeconds(8),null)==null&&!cache.Contains(b)&&opens.Count==1,"Pacote futuro nao satisfaz a espera do primeiro arquivo");
        string finished=Path.Combine(downloads,"primeiro (1).zip");File.Move(partial,finished);
        check(queue.Poll(now.AddSeconds(9),null)==null&&queue.Poll(now.AddSeconds(10),null)==null,"Nome final ainda precisa dos dois segundos de estabilidade");
        using(var writer=new FileStream(finished,FileMode.Open,FileAccess.ReadWrite,FileShare.Read)){
            check(queue.Poll(now.AddSeconds(12),null)==null&&!cache.Contains(a)&&opens.Count==1,"Arquivo ainda aberto para gravacao nao e importado mesmo apos estabilidade");
        }
        var match=queue.Poll(now.AddSeconds(13),null);
        check(match!=null&&match.Asset.Sha256==a.Sha256&&cache.Contains(a)&&queue.CompletedCount==1,"Primeiro pacote liberado e conferido e importado ao cache uma unica vez");
        check(opens.Count==1&&cache.Contains(a)&&registered.SequenceEqual(new[]{a.Sha256})&&queue.CurrentAsset.Sha256==b.Sha256,"Importacao e registroA ativam esperaB preexistente sem abrir pagina desnecessaria");
        check(queue.Poll(now.AddSeconds(14),null)==null,"Segundo pacote preexistente tambem precisa estabilizar na propria espera");
        match=queue.Poll(now.AddSeconds(16),null);
        check(match!=null&&match.Asset.Sha256==b.Sha256&&cache.Contains(b)&&queue.CompletedCount==2&&!queue.IsArmed,"Segundo arquivo e importado automaticamente e encerra fila somente de download");
        check(queue.Poll(now.AddSeconds(20),null)==null&&opens.Count==1&&Directory.GetFiles(cache.Root).Length==2&&registered.SequenceEqual(new[]{a.Sha256,b.Sha256}),"Polls repetidos nao importam, registram ou abrem paginas novamente apos concluir");
        check(Safe.Hash(sourceA)==hashA&&Safe.Hash(sourceB)==hashB&&Safe.Hash(finished)==hashA&&File.Exists(wrong),"Originais, arquivo baixado e variante recusada sao preservados");
        check(!Directory.Exists(Path.Combine(root,"jogo"))&&Directory.GetDirectories(root,".CoopModsOnline",SearchOption.AllDirectories).Length==0,"Fluxo completo nao cria jogo, recibos ou instalacao");

        string cancelledFolder=Folder(root,"cancelled");var cancelledCache=new DownloadCache(Path.Combine(root,"cancelled-cache"));var cancelled=new FacialDownloadQueue(cancelledFolder,cancelledCache);var cancelledOpens=new List<string>();
        cancelled.Start(new[]{a,b},item=>cancelledOpens.Add(item.Sha256));cancelled.Cancel();Copy(sourceA,cancelledFolder,a.Name);
        check(cancelled.Poll(now.AddSeconds(25),null)==null&&!cancelled.IsArmed&&cancelledOpens.Count==1&&!cancelledCache.Contains(a),"Cancelar antes da conclusao impede importar arquivo posterior ou abrir a proxima pagina");
        cancelled.Start(new[]{a,b},item=>cancelledOpens.Add(item.Sha256));cancelled.Poll(now.AddSeconds(26),null);cancelled.Poll(now.AddSeconds(28),null);
        check(cancelledCache.Contains(a)&&cancelled.CompletedCount==1&&cancelledOpens.Count==2&&cancelledOpens.Last()==b.Sha256,"Nova autorizacao reconheceA ja concluido sem abrir paginaA e continua somente sua fila");
        cancelled.Cancel();

        string duringFolder=Folder(root,"cancel-during-hash");Copy(sourceA,duringFolder,a.Name);var duringCache=new DownloadCache(Path.Combine(root,"during-cache"));var during=new FacialDownloadQueue(duringFolder,duringCache);int duringOpens=0;bool callback=false;
        during.Start(new[]{a,b},item=>duringOpens++);during.Poll(now,null);
        match=during.Poll(now.AddSeconds(3),(done,total)=>{callback=true;during.Cancel();});
        check(callback&&match==null&&!during.IsArmed&&!duringCache.Contains(a)&&duringOpens==0,"Cancelamento durante hash invalida entrega de preexistente sem abrir paginas da autorizacao antiga");

        string oldFolder=Folder(root,"old-folder"),newFolder=Folder(root,"new-folder");var changedCache=new DownloadCache(Path.Combine(root,"changed-cache"));int changedOpens=0;
        var oldQueue=new FacialDownloadQueue(oldFolder,changedCache);oldQueue.Start(new[]{a},item=>changedOpens++);oldQueue.Cancel();
        var changed=new FacialDownloadQueue(newFolder,changedCache);changed.Start(new[]{a},item=>changedOpens++);Copy(sourceA,oldFolder,a.Name);changed.Poll(now,null);
        check(changed.Poll(now.AddSeconds(3),null)==null&&!changedCache.Contains(a)&&changedOpens==2,"Depois de cancelar por troca de pasta, arquivo da pasta antiga nao satisfaz a nova espera");
        Copy(sourceA,newFolder,a.Name);changed.Poll(now.AddSeconds(4),null);changed.Poll(now.AddSeconds(6),null);
        check(changedCache.Contains(a)&&changed.CompletedCount==1&&!changed.IsArmed&&changedOpens==2,"Nova pasta reconhece apenas seu pacote correto sem reabrir paginas nem instalar");

        string duplicateFolder=Folder(root,"duplicates");var duplicateCache=new DownloadCache(Path.Combine(root,"duplicates-cache"));var duplicates=new FacialDownloadQueue(duplicateFolder,duplicateCache);int duplicateOpens=0;
        refuse(()=>duplicates.Start(new[]{a,a},item=>duplicateOpens++),"Mesmo arquivo repetido na selecao e recusado antes de iniciar a fila");
        check(duplicateOpens==0&&!duplicates.IsArmed&&!Directory.Exists(duplicateCache.Root),"Duplicados recusados nao deixam download, cache ou autorizacao parcial");

        string preexistingFolder=Folder(root,"preexisting-cache");var preexisting=new FacialDownloadQueue(preexistingFolder,cache);int cachedOpens=0;preexisting.Start(new[]{a,b},item=>cachedOpens++);
        check(cachedOpens==0&&preexisting.CompletedCount==2&&!preexisting.IsArmed,"Cache ja conferido e reutilizado sem pedir downloads repetidos");
        string receivedFolder=Folder(root,"preexisting-downloads");Copy(sourceA,receivedFolder,a.Name);Copy(sourceB,receivedFolder,b.Name);var receivedCache=new DownloadCache(Path.Combine(root,"preexisting-downloads-cache"));var receivedRecords=new List<string>();int receivedOpens=0;
        var received=new FacialDownloadQueue(receivedFolder,receivedCache,entry=>receivedRecords.Add(entry.Asset.Sha256));received.Start(new[]{a,b},entry=>receivedOpens++);
        check(received.IsArmed&&received.CompletedCount==0&&receivedOpens==0&&!Directory.Exists(receivedCache.Root),"Todos preexistentes: iniciar observa candidatos sem abrir abas ou aceitar antes da estabilidade");
        received.Poll(now.AddSeconds(31),null);check(received.CompletedCount==1&&receivedOpens==0&&received.CurrentAsset.Sha256==b.Sha256,"Todos preexistentes: A e conferido e registrado antes de esperar estabilidadeB sem abrir abas");
        received.Poll(now.AddSeconds(34),null);check(received.CompletedCount==2&&!received.IsArmed&&receivedOpens==0&&receivedRecords.SequenceEqual(new[]{a.Sha256,b.Sha256})&&receivedCache.Contains(a)&&receivedCache.Contains(b),"Todos preexistentes: dois arquivos originais completam importacao em ordem com zero abas");
        string rejectedFolder=Folder(root,"preexisting-wrong");File.WriteAllBytes(Path.Combine(rejectedFolder,a.Name),new byte[(int)a.Size]);Copy(sourceA,rejectedFolder,a.Name+".crdownload");var rejectedCache=new DownloadCache(Path.Combine(root,"preexisting-wrong-cache"));int rejectedOpens=0;
        var rejectedQueue=new FacialDownloadQueue(rejectedFolder,rejectedCache);rejectedQueue.Start(new[]{a},entry=>rejectedOpens++);
        check(rejectedOpens==0&&rejectedQueue.CompletedCount==0,"Preexistente de tamanho esperado adia abertura enquanto verifica identidade");
        rejectedQueue.Poll(now.AddSeconds(36),null);check(rejectedOpens==1&&rejectedQueue.IsArmed&&!rejectedCache.Contains(a),"Variante errada estavel e rejeitada, parcial nao bloqueia e pagina necessaria abre uma unica vez");
        rejectedQueue.Poll(now.AddSeconds(37),null);check(rejectedOpens==1,"Variante recusada nao dispara paginas repetidas em cada tick");
        Copy(sourceA,rejectedFolder,"primeiro (1).zip");rejectedQueue.Poll(now.AddSeconds(38),null);rejectedQueue.Poll(now.AddSeconds(40),null);check(rejectedQueue.CompletedCount==1&&!rejectedQueue.IsArmed&&rejectedCache.Contains(a)&&rejectedOpens==1,"Arquivo correto posterior ao preexistente errado conclui a mesma espera sem outra pagina");

        string invalidFolder=Folder(root,"invalid");var invalidCache=new DownloadCache(Path.Combine(root,"invalid-cache"));var invalid=new FacialDownloadQueue(invalidFolder,invalidCache);int invalidOpens=0;
        refuse(()=>invalid.Start(new FixedAsset[0],item=>invalidOpens++),"Fila vazia nao inicia monitor nem abre paginas");
        refuse(()=>invalid.Start(new[]{new FixedAsset{Id="untrusted",Size=1,Sha256="invalido"}},item=>invalidOpens++),"Pacote sem SHA fixado nao e aceito para viabilizar automacao");
        check(invalidOpens==0&&!Directory.Exists(invalidCache.Root),"Recusa de identidade incompleta nao cria cache ou aciona browser");
        string byHashFolder=Folder(root,"size-from-sha");var byHashCache=new DownloadCache(Path.Combine(root,"size-from-sha-cache"));var byHash=new FacialDownloadQueue(byHashFolder,byHashCache);int byHashOpens=0;
        var noSize=Safe.Json.Deserialize<FixedAsset>(Safe.Json.Serialize(a));noSize.Size=0;byHash.Start(new[]{noSize},item=>byHashOpens++);
        var strictWatch=new OnlineDownloadWatch(byHashFolder);refuse(()=>strictWatch.Arm(new[]{noSize}),"Arm legado estrito continua rejeitando Size0; excecao limitada ao preparo facial porSHA");
        File.WriteAllBytes(Path.Combine(byHashFolder,a.Name),new byte[(int)a.Size]);Copy(sourceA,byHashFolder,"correto.zip.part");Copy(sourceA,byHashFolder,"outro-nome.zip");byHash.Poll(now,null);
        check(byHash.Poll(now.AddSeconds(3),null)==null&&!byHashCache.Contains(a)&&byHashOpens==1,"Tamanho ainda nao conhecido exige nome oficial eSHA fixado, recusando parcial, nome alheio e variante errada");
        Copy(sourceA,byHashFolder,"primeiro (1).zip");byHash.Poll(now.AddSeconds(4),null);match=byHash.Poll(now.AddSeconds(6),null);
        check(match!=null&&match.Asset.Size==a.Size&&match.Asset.Sha256==a.Sha256&&byHashCache.Contains(a)&&byHash.VerifiedAssets.Single().Size==a.Size&&!byHash.IsArmed,"IdentidadeSHA previamente fixada permite medir tamanho real e importar sem inventar tamanho do site");
        string frozenFolder=Folder(root,"frozen-identities");var frozenCache=new DownloadCache(Path.Combine(root,"frozen-cache"));var frozen=new FacialDownloadQueue(frozenFolder,frozenCache);var mutable=Safe.Json.Deserialize<FixedAsset>(Safe.Json.Serialize(a));
        frozen.Start(new[]{mutable},item=>{});mutable.Sha256=new string('0',64);mutable.Size++;Copy(sourceA,frozenFolder,a.Name);frozen.Poll(now,null);match=frozen.Poll(now.AddSeconds(3),null);
        check(match!=null&&match.Asset.Sha256==a.Sha256&&frozenCache.Contains(a),"Mudar objeto do chamador aposStart nao troca identidade ja autorizada");
        string retryFolder=Folder(root,"browser-retry");var retryCache=new DownloadCache(Path.Combine(root,"browser-retry-cache"));var retry=new FacialDownloadQueue(retryFolder,retryCache);int launchAttempts=0;
        refuse(()=>retry.Start(new[]{a},item=>{launchAttempts++;if(launchAttempts==1)throw new IOException("Navegador simulado indisponivel.");}),"Falha de abertura e reportada sem marcar download concluido");
        check(retry.IsArmed&&retry.CompletedCount==0&&!retryCache.Contains(a),"Falha do navegador preserva apenas espera autorizada ainda nao concluida");
        retry.Resume();retry.Resume();check(launchAttempts==2&&retry.IsArmed,"Retomar tenta abrir apenas uma vez e nao duplica pagina que ja esta aguardando");
        Copy(sourceA,retryFolder,a.Name);retry.Poll(now,null);retry.Poll(now.AddSeconds(3),null);
        check(retry.CompletedCount==1&&!retry.IsArmed&&retryCache.Contains(a),"Fila retomada continua usando monitor e importador verificados");
        string recordFolder=Folder(root,"record-retry");var recordCache=new DownloadCache(Path.Combine(root,"record-retry-cache"));int recordAttempts=0;var recordOpens=new List<string>();
        var recordRetry=new FacialDownloadQueue(recordFolder,recordCache,item=>{recordAttempts++;if(recordAttempts==1)throw new IOException("Registro simulado indisponivel uma vez.");});
        recordRetry.Start(new[]{a,b},item=>recordOpens.Add(item.Sha256));Copy(sourceA,recordFolder,a.Name);recordRetry.Poll(now,null);
        refuse(()=>recordRetry.Poll(now.AddSeconds(3),null),"Falha do registro e reportada depois de cache sem fingir etapa completa");
        check(recordCache.Contains(a)&&recordRetry.IsArmed&&recordRetry.CompletedCount==0&&recordRetry.CurrentAsset.Sha256==a.Sha256&&recordOpens.Count==1,"Registro falho nao abre segunda pagina nem perde o pacote atualmente aguardado");
        recordRetry.Poll(now.AddSeconds(6),null);recordRetry.Poll(now.AddSeconds(8),null);
        check(recordAttempts==2&&recordRetry.CompletedCount==1&&recordRetry.CurrentAsset.Sha256==b.Sha256&&recordOpens.Count==2,"Monitor rearmado retenta registro sem novo clique e so avanca apos sucesso");
        string restartFolder=Folder(root,"record-restart");var restartCache=new DownloadCache(Path.Combine(root,"record-restart-cache"));restartCache.Import(sourceA,a);int beforeRestartOpens=0;
        var beforeRestart=new FacialDownloadQueue(restartFolder,restartCache,entry=>{throw new IOException("Registro do cache temporariamente indisponivel.");});
        refuse(()=>beforeRestart.Start(new[]{a,b},entry=>beforeRestartOpens++),"Cache existente nao dispensa registro: falha ao registrar e reportada antes da proxima pagina");
        check(beforeRestart.IsArmed&&beforeRestart.CompletedCount==0&&beforeRestartOpens==0&&restartCache.Contains(a),"Falha de registro do cache preserva pacote e nao conclui ou abre outro item");beforeRestart.Cancel();
        var afterRestartRecords=new List<string>();var afterRestartOpens=new List<string>();bool recordedBeforePage=false;
        var afterRestart=new FacialDownloadQueue(restartFolder,restartCache,entry=>afterRestartRecords.Add(entry.Asset.Sha256));
        afterRestart.Start(new[]{a,b},entry=>{recordedBeforePage=afterRestartRecords.SequenceEqual(new[]{a.Sha256});afterRestartOpens.Add(entry.Sha256);});
        check(afterRestart.CompletedCount==1&&afterRestart.CurrentAsset.Sha256==b.Sha256&&recordedBeforePage&&afterRestartOpens.SequenceEqual(new[]{b.Sha256}),"Nova fila depois de cancelar registra cacheA antes de abrirB, sem repetir downloadA");
        Copy(sourceB,restartFolder,b.Name);afterRestart.Poll(now,null);afterRestart.Poll(now.AddSeconds(3),null);
        check(afterRestartRecords.SequenceEqual(new[]{a.Sha256,b.Sha256})&&afterRestart.CompletedCount==2&&!afterRestart.IsArmed&&restartCache.Contains(b)&&afterRestartOpens.Count==1,"Fila reiniciada recebeB posteriormente e encerra ambos registros sem abertura ou importacao duplicada");
        check(Safe.Hash(sourceA)==hashA&&Safe.Hash(sourceB)==hashB&&Directory.GetFiles(root,"*.exe",SearchOption.AllDirectories).Length==0&&Directory.GetDirectories(root,".CoopModsOnline",SearchOption.AllDirectories).Length==0,"Todos os cenarios preservam originais e nao executam ou instalam codigo de pacote");
        RunUiCases(root,sourceA,sourceB,a,b,check);
        RunFullUiCase(root,check);
        RunInventoryUiCase(root,sourceA,sourceB,a,b,check);
        Safe.WriteJson(Path.Combine(root,"resultado.json"),new{Passed=passed.Count,Tests=passed,RealMonitor=true,RealCacheImport=true,MainWindowAndTimerExercised=true,BrowserSimulated=true,RealNetworkUsed=false,RealGameTouched=false,InstallerInvoked=false});File.WriteAllText(Path.Combine(parent,"ultimo-teste-face-queue.txt"),root);
    }
    static void RunUiCases(string root,string sourceA,string sourceB,FixedAsset a,FixedAsset b,Action<bool,string> check){
        foreach(string scenario in new[]{"complete","cancel","game-change","folder-stop","cancel-after-first"}){
            string fixture=Folder(root,"ui-"+scenario),downloads=Folder(fixture,"downloads"),game=Path.Combine(fixture,"game-never-created");var cache=new DownloadCache(Path.Combine(fixture,"cache"));var calls=new List<string>();
            var faceItems=FacialDownload.List().Take(2).ToArray();var identities=new Dictionary<string,FixedAsset>();
            for(int i=0;i<2;i++){var item=faceItems[i];var asset=Safe.Json.Deserialize<FixedAsset>(Safe.Json.Serialize(i==0?a:b));asset.Id=item.Id;asset.ModId=item.ModId;asset.FileId=item.FileId;asset.Url=item.Url;identities.Add(item.Id,asset);}
            using(var window=new OnlineWindow(FacialMainListTests.PreparationCatalog(),cache,true)){
                var originalResolver=(Func<FacialDownload,FixedAsset>)Get(window,"facialAssetResolver");
                Set(window,"facialAssetResolver",new Func<FacialDownload,FixedAsset>(item=>identities.ContainsKey(item.Id)?identities[item.Id]:originalResolver(item)));
                Set(window,"launchBrowser",new Action<string>(url=>calls.Add(url)));Set(window,"downloadFolder",downloads);((TextBox)Get(window,"game")).Text=game;
                window.ShowInTaskbar=false;window.StartPosition=FormStartPosition.Manual;window.Location=new Point(-30000,-30000);window.Show();Application.DoEvents();
                var grid=(DataGridView)Get(window,"grid");foreach(DataGridViewRow row in grid.Rows)row.Cells["Select"].Value=identities.ContainsKey(Convert.ToString(row.Tag));
                var button=(Button)Get(window,"mainInstallButton");button.PerformClick();Application.DoEvents();var queue=(FacialDownloadQueue)Get(window,"facialQueue");
                check(queue!=null&&queue.IsArmed&&calls.SequenceEqual(new[]{faceItems[0].Url}),"UI "+scenario+": um clique na lista inicia fila real e abre so a primeira pagina");
                if(scenario!="complete"&&scenario!="cancel-after-first"){
                    if(scenario=="cancel")Call(window,"CancelDownload",new object[]{null});
                    else if(scenario=="game-change")((TextBox)Get(window,"game")).Text=game+"-other";
                    else Call(window,"StopDownloadWatch");
                    Application.DoEvents();Copy(sourceA,downloads,a.Name);queue.Poll(DateTime.UtcNow.AddSeconds(-3),null);Tick(window);Pump((Task)Call(window,"CheckFacialDownload"));
                    check(!queue.IsArmed&&calls.Count==1&&!cache.Contains(identities[faceItems[0].Id])&&!Directory.Exists(game),"UI "+scenario+": cancelar ou mudar destino encerra espera e nao retoma arquivo posterior");window.Close();continue;
                }
                Copy(sourceA,downloads,a.Name+".crdownload");queue.Poll(DateTime.UtcNow.AddSeconds(-3),null);Pump((Task)Call(window,"CheckFacialDownload"));
                File.WriteAllBytes(Path.Combine(downloads,a.Name),new byte[(int)a.Size]);queue.Poll(DateTime.UtcNow.AddSeconds(-3),null);Pump((Task)Call(window,"CheckFacialDownload"));
                check(calls.Count==1&&queue.CompletedCount==0&&!cache.Contains(identities[faceItems[0].Id]),"UI: tick real ignora arquivo parcial e outra variante sem abrir segunda pagina");
                Copy(sourceA,downloads,"A final (1).zip");queue.Poll(DateTime.UtcNow.AddSeconds(-3),null);Tick(window);Until(()=>calls.Count==2&&!(bool)Get(window,"busy"));
                check(queue.CompletedCount==1&&cache.Contains(identities[faceItems[0].Id])&&calls[1]==faceItems[1].Url,"UI: timer existente importa primeiro pacote e abre segundo automaticamente sem novo clique");
                if(scenario=="cancel-after-first"){
                    var firstRow=grid.Rows.Cast<DataGridViewRow>().Single(row=>Convert.ToString(row.Tag)==faceItems[0].Id);var secondRow=grid.Rows.Cast<DataGridViewRow>().Single(row=>Convert.ToString(row.Tag)==faceItems[1].Id);
                    Call(window,"CancelDownload",new object[]{null});Application.DoEvents();string cancelledStatus=((Label)Get(window,"status")).Text;
                    check(!queue.IsArmed&&queue.CompletedCount==1&&cancelledStatus.IndexOf("cancelada",StringComparison.OrdinalIgnoreCase)>=0&&Convert.ToString(secondRow.Cells["State"].Value)=="Cancelado"&&Convert.ToInt32(firstRow.Cells["Progress"].Value)==100&&Convert.ToString(firstRow.Cells["State"].Value)=="Baixado e conferido","UI: cancelar apos primeiro importado preserva primeiro100porcento e segundoCancelado");
                    Call(window,"DispatchFacialPage",queue,identities[faceItems[1].Id]);
                    check(calls.Count==2,"UI: dispatch atrasado com fila cancelada nao abre outra pagina");
                    var staleQueue=new FacialDownloadQueue(Folder(fixture,"stale-downloads"),cache);staleQueue.Start(new[]{identities[faceItems[1].Id]},entry=>{});Call(window,"DispatchFacialPage",staleQueue,identities[faceItems[1].Id]);
                    check(staleQueue.IsArmed&&calls.Count==2,"UI: dispatch de outra fila ainda armada nao usa navegador da fila atual");staleQueue.Cancel();
                    Copy(sourceB,downloads,"B apos cancelar.zip");queue.Poll(DateTime.UtcNow.AddSeconds(-3),null);Tick(window);Pump((Task)Call(window,"CheckFacialDownload"));
                    check(((Label)Get(window,"status")).Text==cancelledStatus&&Convert.ToString(secondRow.Cells["State"].Value)=="Cancelado"&&Convert.ToInt32(firstRow.Cells["Progress"].Value)==100&&queue.CompletedCount==1&&!cache.Contains(identities[faceItems[1].Id])&&calls.Count==2,"UI: Check e timer posteriores nao sobrescrevem cancelamento nem concluem falsamente o segundo arquivo");window.Close();continue;
                }
                Copy(sourceB,downloads,"B final.zip");queue.Poll(DateTime.UtcNow.AddSeconds(-3),null);Tick(window);Until(()=>queue.CompletedCount==2&&!(bool)Get(window,"busy"));
                var selectedRows=grid.Rows.Cast<DataGridViewRow>().Where(row=>identities.ContainsKey(Convert.ToString(row.Tag))).ToList();
                check(cache.Contains(identities[faceItems[1].Id])&&!queue.IsArmed&&calls.Count==2&&selectedRows.All(row=>Convert.ToString(row.Cells["Installed"].Value)=="Somente download"),"UI: dois pacotes recebidos concluem fila e nunca exibem instalado");
                string queueFile=Path.Combine(fixture,"importados-pendentes.json");var registered=Safe.Json.Deserialize<List<ModImportItem>>(File.ReadAllText(queueFile));
                check(registered.Count(item=>item.Sha256==a.Sha256||item.Sha256==b.Sha256)==2&&grid.Rows.Count==69,"UI: importador registra arquivos desconhecidos uma vez sem duplicar linhas faciais");
                check(((HashSet<string>)Get(window,"automaticQueue")).Count==0&&!Directory.Exists(game)&&Directory.GetDirectories(fixture,".CoopModsOnline",SearchOption.AllDirectories).Length==0,"UI: adquirir e registrar nao autoriza instalacao nem escreve jogo ou recibos");
                Tick(window);Pump((Task)Call(window,"CheckFacialDownload"));check(calls.Count==2&&queue.CompletedCount==2,"UI: ticks posteriores nao reabrem paginas nem repetem importacao");window.Close();
            }
        }
    }
    static void RunFullUiCase(string root,Action<bool,string> check){
        string fixture=Folder(root,"ui-all-fourteen"),sources=Folder(fixture,"sources"),downloads=Folder(fixture,"downloads"),game=Path.Combine(fixture,"game-never-created");
        var cache=new DownloadCache(Path.Combine(fixture,"cache"));var calls=new List<string>();var items=FacialDownload.List();var assets=new Dictionary<string,FixedAsset>();
        for(int i=0;i<items.Count;i++){
            var content=new byte[512+i*377];new Random(12345+i).NextBytes(content);var entry=items[i];var asset=Package(sources,"facial-"+i+".zip","Preparacao facial inerte "+i+" "+Convert.ToBase64String(content),980001+i*2);
            asset.Id=entry.Id;asset.ModId=entry.ModId;asset.FileId=entry.FileId;asset.Url=entry.Url;assets.Add(entry.Id,asset);
        }
        check(assets.Values.Select(asset=>asset.Size).Distinct().Count()==14,"UI14: fixture usa tamanhos distintos para que arquivos anteriores nao sejam candidatos das proximas esperas");
        using(var window=new OnlineWindow(FacialMainListTests.PreparationCatalog(),cache,true)){
            Set(window,"facialAssetResolver",new Func<FacialDownload,FixedAsset>(entry=>assets[entry.Id]));Set(window,"launchBrowser",new Action<string>(url=>calls.Add(url)));Set(window,"downloadFolder",downloads);((TextBox)Get(window,"game")).Text=game;
            window.ShowInTaskbar=false;window.StartPosition=FormStartPosition.Manual;window.Location=new Point(-30000,-30000);window.Show();Application.DoEvents();
            var grid=(DataGridView)Get(window,"grid");foreach(DataGridViewRow row in grid.Rows)row.Cells["Select"].Value=assets.ContainsKey(Convert.ToString(row.Tag));
            ((Button)Get(window,"mainInstallButton")).PerformClick();Application.DoEvents();var queue=(FacialDownloadQueue)Get(window,"facialQueue");
            check(queue!=null&&queue.IsArmed&&queue.CompletedCount==0&&calls.SequenceEqual(new[]{items[0].Url}),"UI14: um clique seleciona fila completa e abre somente a primeira das quatorze paginas");
            for(int i=0;i<items.Count;i++){
                int completed=i+1;var entry=items[i];var asset=assets[entry.Id];
                check(queue.CurrentAsset.Id==entry.Id&&calls.Count==completed,"UI14: arquivo "+completed+" aguarda sua propria pagina sem antecipar proximos downloads");
                Copy(Path.Combine(sources,asset.Name),downloads,asset.Name);queue.Poll(DateTime.UtcNow.AddSeconds(-3),null);Tick(window);
                Until(()=>queue.CompletedCount==completed&&!(bool)Get(window,"busy")&&!(bool)Get(window,"pollingFacial"));
                check(cache.Contains(asset)&&calls.Count==Math.Min(14,completed+1)&&(i==13||calls.Last()==items[i+1].Url),"UI14: tick "+completed+" confere e importa um pacote e abre automaticamente apenas a proxima pagina");
            }
            var registered=Safe.Json.Deserialize<List<ModImportItem>>(File.ReadAllText(Path.Combine(fixture,"importados-pendentes.json")));
            var selectedRows=grid.Rows.Cast<DataGridViewRow>().Where(row=>assets.ContainsKey(Convert.ToString(row.Tag))).ToList();
            check(queue.CompletedCount==14&&!queue.IsArmed&&calls.SequenceEqual(items.Select(entry=>entry.Url))&&assets.Values.All(asset=>cache.Contains(asset)),"UI14: fila completa quatorze arquivos e quatorze paginas na ordem, sem outro clique");
            check(registered.Count==14&&registered.Select(entry=>entry.Sha256).Distinct().Count()==14&&assets.Values.All(asset=>registered.Any(entry=>entry.Sha256==asset.Sha256))&&Directory.GetFiles(cache.Root).Length==14&&grid.Rows.Count==69,"UI14: cache e importador possuem quatorze identidades unicas e lista permanece sem duplicacoes");
            check(selectedRows.Count==14&&selectedRows.All(row=>Convert.ToString(row.Cells["Installed"].Value)=="Somente download"&&Convert.ToString(row.Cells["State"].Value)=="Baixado e conferido")&&((HashSet<string>)Get(window,"automaticQueue")).Count==0&&!Directory.Exists(game)&&Directory.GetDirectories(fixture,".CoopModsOnline",SearchOption.AllDirectories).Length==0,"UI14: nenhuma preparacao vira instalada, autoriza instalacao ou escreve jogo e recibos");
            Tick(window);Pump((Task)Call(window,"CheckFacialDownload"));check(calls.Count==14&&queue.CompletedCount==14&&Safe.Json.Deserialize<List<ModImportItem>>(File.ReadAllText(Path.Combine(fixture,"importados-pendentes.json"))).Count==14,"UI14: verificacoes posteriores preservam conclusao sem duplicar registros ou paginas");window.Close();
        }
    }
    static void RunInventoryUiCase(string root,string sourceA,string sourceB,FixedAsset a,FixedAsset b,Action<bool,string> check){
        string fixture=Folder(root,"ui-readonly-inventory"),downloads=Folder(fixture,"downloads"),game=Path.Combine(fixture,"game-never-created");var cache=new DownloadCache(Path.Combine(fixture,"cache"));var calls=new List<string>();var items=FacialDownload.List();
        var third=Package(fixture,"terceiro.zip","Terceiro arquivo inerte; somente outra variante sera colocada em Downloads.",970001);var originals=new[]{a,b,third};var identities=new Dictionary<string,FixedAsset>();
        for(int i=0;i<3;i++){var entry=items[i];var asset=Safe.Json.Deserialize<FixedAsset>(Safe.Json.Serialize(originals[i]));asset.Id=entry.Id;asset.ModId=entry.ModId;asset.FileId=entry.FileId;asset.Url=entry.Url;identities.Add(entry.Id,asset);}
        Copy(sourceA,downloads,a.Name);cache.Import(sourceB,identities[items[1].Id]);File.WriteAllBytes(Path.Combine(downloads,third.Name),new byte[(int)third.Size]);Copy(Path.Combine(fixture,third.Name),downloads,third.Name+".crdownload");
        var before=Directory.GetFiles(fixture,"*",SearchOption.AllDirectories).ToDictionary(file=>file,file=>Safe.Hash(file));
        using(var window=new OnlineWindow(FacialMainListTests.PreparationCatalog(),cache,true)){
            var originalResolver=(Func<FacialDownload,FixedAsset>)Get(window,"facialAssetResolver");Set(window,"facialAssetResolver",new Func<FacialDownload,FixedAsset>(entry=>identities.ContainsKey(entry.Id)?identities[entry.Id]:originalResolver(entry)));
            Set(window,"launchBrowser",new Action<string>(url=>calls.Add(url)));Set(window,"downloadFolder",downloads);((TextBox)Get(window,"game")).Text=game;
            window.ShowInTaskbar=false;window.StartPosition=FormStartPosition.Manual;window.Location=new Point(-30000,-30000);window.Show();Application.DoEvents();var grid=(DataGridView)Get(window,"grid");
            var faceRows=grid.Rows.Cast<DataGridViewRow>().Where(row=>Convert.ToString(row.Tag).StartsWith("face-")).ToList();
            check(faceRows.Count==14&&faceRows.All(row=>Convert.ToString(row.Cells["State"].Value)=="Não iniciado")&&calls.Count==0,"InventarioUI: preparacoes iniciam como Nao iniciado e abrir preview nao dispara browser ou fila");
            Pump((Task)Call(window,"RefreshFacialInventory"));
            var available=faceRows.Where(row=>Convert.ToString(row.Cells["State"].Value)=="Baixado e conferido").ToList();
            check(available.Count==2&&available.Any(row=>Convert.ToString(row.Tag)==items[0].Id)&&available.Any(row=>Convert.ToString(row.Tag)==items[1].Id)&&available.All(row=>Convert.ToInt32(row.Cells["Progress"].Value)==100),"InventarioUI: scan readonly reconhece originalA em Downloads eB no cache quente sem pedir novo download");
            check(faceRows.Single(row=>Convert.ToString(row.Tag)==items[2].Id).Cells["State"].Value.ToString()=="Não iniciado"&&!cache.Contains(identities[items[0].Id]),"InventarioUI: parcial e variante errada nao viram baixados, e reconhecerA nao o importa para cache");
            check(faceRows.All(row=>Convert.ToString(row.Cells["Installed"].Value)=="Somente download")&&calls.Count==0&&Get(window,"facialQueue")==null&&((HashSet<string>)Get(window,"automaticQueue")).Count==0&&!File.Exists(Path.Combine(fixture,"importados-pendentes.json"))&&!Directory.Exists(game),"InventarioUI: reconhecimento nao marca Instalado, abre navegador, registra importacao ou autoriza instalacao");
            Pump((Task)Call(window,"RefreshFacialInventory"));
            check(Directory.GetFiles(fixture,"*",SearchOption.AllDirectories).OrderBy(file=>file).SequenceEqual(before.Keys.OrderBy(file=>file))&&before.All(pair=>Safe.Hash(pair.Key)==pair.Value)&&calls.Count==0&&grid.Rows.Count==69,"InventarioUI: atualizar novamente preserva todos os bytes e arquivos, sem cache novo, recibo ou duplicacao de linhas");window.Close();
        }
    }
}
