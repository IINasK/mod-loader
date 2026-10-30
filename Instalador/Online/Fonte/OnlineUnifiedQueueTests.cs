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

public static class OnlineUnifiedQueueTests {
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static object Get(object target,string name){return target.GetType().GetField(name,Private).GetValue(target);}
    static void Set(object target,string name,object value){target.GetType().GetField(name,Private).SetValue(target,value);}
    static object Call(object target,string name,params object[] args){try{return target.GetType().GetMethod(name,Private).Invoke(target,args);}catch(TargetInvocationException ex){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException??ex).Throw();throw;}}
    static void Wait(Func<bool> done){var clock=Stopwatch.StartNew();while(!done()){if(clock.Elapsed>TimeSpan.FromSeconds(18))throw new Exception("Fila unificada excedeu18segundos na fixture.");Application.DoEvents();Thread.Sleep(5);}Application.DoEvents();}
    static void Pump(Task task){Wait(()=>task.IsCompleted);task.GetAwaiter().GetResult();Application.DoEvents();}
    static void Tick(OnlineWindow window){typeof(System.Windows.Forms.Timer).GetMethod("OnTick",Private).Invoke(Get(window,"downloadWatchTimer"),new object[]{EventArgs.Empty});Application.DoEvents();}
    static bool Idle(OnlineWindow window){return !(bool)Get(window,"busy")&&!(bool)Get(window,"automaticFlow")&&!(bool)Get(window,"pollingDownload");}
    static string Hash(string content){using(var sha=System.Security.Cryptography.SHA256.Create())return Safe.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(content)));}
    static void Write(string root,string relative,string content){string path=Safe.In(root,relative);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,content,new UTF8Encoding(false));}
    static FixedMod Archive(string sources,string id,int nexusId,int count){
        string path=Path.Combine(sources,id+".zip"),entry="archive/pc/mod/unified-"+id+".archive";var bytes=new byte[count];new Random(nexusId).NextBytes(bytes);string content=Convert.ToBase64String(bytes);
        using(var zip=ZipFile.Open(path,ZipArchiveMode.Create)){var file=zip.CreateEntry(entry);using(var writer=new StreamWriter(file.Open(),new UTF8Encoding(false)))writer.Write(content);}
        var asset=new FixedAsset{Id="a0",Name=id+".zip",Size=new FileInfo(path).Length,Sha256=Safe.Hash(path),Format="zip",ModId=nexusId,FileId=nexusId+1,Url="https://www.nexusmods.com/cyberpunk2077/mods/"+nexusId+"?tab=files&file_id="+(nexusId+1)};
        return new FixedMod{Id=id,Name="Fixture "+id,Version="1.0",GameVersion="2.5",Dependencies=new[]{"NightCoop"},Assets=new List<FixedAsset>{asset},Files=new List<FixedFile>{new FixedFile{Asset=asset.Id,Entry=entry,Path=entry,Size=Encoding.UTF8.GetByteCount(content),Sha256=Hash(content)}}};
    }
    sealed class Fixture:IDisposable {
        public readonly string Root,Game,Downloads,Sources,Saves,Foreign,SaveFile;
        public readonly FixedCatalog Catalog;
        public readonly FixedMod A,B,Unselected;
        public readonly DownloadCache Cache;
        public readonly OnlineWindow Window;
        public readonly List<string> Opened=new List<string>(),States=new List<string>();
        public readonly Dictionary<string,string> BaseHashes=new Dictionary<string,string>();
        public int InstallerCalls;
        public bool ACorrectBeforeB=true;
        public Fixture(string root,bool preexisting){
            Root=root;Sources=Path.Combine(root,"sources");Downloads=Path.Combine(root,"downloads");Game=Path.Combine(root,"game");Saves=Path.Combine(root,"saves");Directory.CreateDirectory(Sources);Directory.CreateDirectory(Downloads);Directory.CreateDirectory(Path.Combine(Game,"bin","x64"));
            File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","FixtureGame.exe"),Path.Combine(Game,"bin","x64","Cyberpunk2077.exe"));Write(Game,".fixture-only","isolated");
            var baseAsset=new FixedAsset{Id="base",Name="base.zip",Size=4,Sha256=Hash("base"),Format="raw",Url="https://github.com/example/project/releases/download/fixed/base.zip"};
            var basis=new FixedMod{Id="NightCoop",Name="Fixture base",Version="fixed",GameVersion="2.5",Immutable=true,Dependencies=new string[0],Assets=new List<FixedAsset>{baseAsset},Files=new List<FixedFile>()};
            foreach(string relative in new[]{"bin/x64/winmm.dll","red4ext/RED4ext.dll","red4ext/LICENSE.txt","red4ext/THIRD_PARTY_LICENSES.txt","red4ext/plugins/NightCoop/NightCoop.dll"}){Write(Game,relative,"base");BaseHashes.Add(relative,Safe.Hash(Safe.In(Game,relative)));basis.Files.Add(new FixedFile{Asset=baseAsset.Id,Entry="",Path=relative,Size=4,Sha256=baseAsset.Sha256});}
            A=Archive(Sources,"AddonA",960001,700);B=Archive(Sources,"AddonB",960003,1700);Unselected=Archive(Sources,"UnselectedC",960005,2700);
            Catalog=new FixedCatalog{Schema=1,Id="unified-fixture",GameVersion="2.5",GameFileVersion="2.5.0.0",Packages=new List<FixedMod>{basis,A,B,Unselected}};FixedData.Validate(Catalog);Cache=new DownloadCache(Path.Combine(root,"cache"));
            Foreign=Safe.In(Game,"archive/pc/mod/foreign-preserved.archive");Write(Game,"archive/pc/mod/foreign-preserved.archive","foreign bytes");Write(Saves,"ManualSave-fixture/save.dat","fixture save preserved");SaveFile=Safe.In(Saves,"ManualSave-fixture/save.dat");
            if(preexisting){Download(A);Download(B);}
            Window=new OnlineWindow(Catalog,Cache,true);Set(Window,"downloadFolder",Downloads);
            Set(Window,"createInstaller",new Func<string,FixedInstaller>(path=>{if(!String.Equals(Path.GetFullPath(path),Game,StringComparison.OrdinalIgnoreCase))throw new IOException("Factory de teste recusou destino externo.");Interlocked.Increment(ref InstallerCalls);return new FixedInstaller(Catalog,Cache,path,Saves,true);}));
            Set(Window,"launchBrowser",new Action<string>(url=>{if(url==B.Assets[0].Url)ACorrectBeforeB=ACorrectBeforeB&&FixedData.Scan(A,Game).IsCorrect&&ReceiptFiles().Length>0;Opened.Add(url);}));((TextBox)Get(Window,"game")).Text=Game;
            var grid=(DataGridView)Get(Window,"grid");grid.CellValueChanged+=(sender,args)=>{if(args.RowIndex<0||args.ColumnIndex<0)return;var row=grid.Rows[args.RowIndex];string column=grid.Columns[args.ColumnIndex].Name;if(column=="State"||column=="Installed")States.Add(Convert.ToString(row.Tag)+"|"+column+"|"+Convert.ToString(row.Cells[column].Value));};
            Window.ShowInTaskbar=false;Window.StartPosition=FormStartPosition.Manual;Window.Location=new Point(-30000,-30000);Window.Show();Application.DoEvents();
        }
        public string Download(FixedMod mod){string path=Path.Combine(Downloads,mod.Assets[0].Name);File.Copy(Path.Combine(Sources,mod.Assets[0].Name),path);return path;}
        public DataGridViewRow Row(FixedMod mod){return ((DataGridView)Get(Window,"grid")).Rows.Cast<DataGridViewRow>().Single(row=>Convert.ToString(row.Tag)==mod.Id);}
        public string[] ReceiptFiles(){string data=Path.Combine(Game,".CoopModsOnline");return Directory.Exists(data)?Directory.GetFiles(data,"*.json"):new string[0];}
        public OnlineReceipt Receipt(string path){return (OnlineReceipt)Call(new FixedInstaller(Catalog,Cache,Game,Saves,true),"ReadReceipt",path);}
        public void Start(){Call(Window,"AuthorizeAutomatic",new[]{A.Id,B.Id},Game,true);Pump((Task)Call(Window,"ProcessAutomaticQueue",Game));}
        public void Complete(FixedMod mod,bool preexisting){
            if(preexisting)Pump(Task.Delay(2200));
            else{Download(mod);((OnlineDownloadWatch)Get(Window,"downloadWatch")).Poll(DateTime.UtcNow.AddSeconds(-3));}
            Tick(Window);Wait(()=>FixedData.Scan(mod,Game).IsCorrect&&Idle(Window));
        }
        public void Dispose(){Call(Window,"ClearAutomaticQueue");Window.Close();Window.Dispose();Application.DoEvents();}
    }
    public static int Run(string parent){
        if(Thread.CurrentThread.GetApartmentState()!=ApartmentState.STA)throw new Exception("Fila unificada UI exigeSTA.");Exception failure=null;int count=0;var previous=SynchronizationContext.Current;
        try{using(var host=new Form{ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-30000,-30000),Size=new Size(1,1)}){host.Shown+=(sender,args)=>host.BeginInvoke(new Action(()=>{try{count=RunCases(parent);}catch(Exception ex){failure=ex;}finally{host.Close();}}));Application.Run(host);}}
        finally{SynchronizationContext.SetSynchronizationContext(previous);}if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();return count;
    }
    static int RunCases(string parent){
        string root=Path.Combine(Path.GetFullPath(parent),"online-testes-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var passed=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("TESTE UNIFIED QUEUE FALHOU: "+name);passed.Add(name);};
        foreach(bool preexisting in new[]{false,true})using(var f=new Fixture(Path.Combine(root,preexisting?"preexisting":"fresh"),preexisting)){
            string saveHash=Safe.Hash(f.SaveFile),foreignHash=Safe.Hash(f.Foreign);var sourceHashes=Directory.GetFiles(f.Sources).ToDictionary(file=>file,file=>Safe.Hash(file));f.Start();
            check(((HashSet<string>)Get(f.Window,"automaticQueue")).SetEquals(new[]{f.A.Id,f.B.Id})&&f.InstallerCalls==0&&!FixedData.Scan(f.A,f.Game).IsCorrect&&!FixedData.Scan(f.B,f.Game).IsCorrect,"UI comum "+preexisting+": autorizacao fica restritaA/B e aguarda arquivo sem instalar antecipadamente");
            check(preexisting?f.Opened.Count==0:f.Opened.SequenceEqual(new[]{f.A.Assets[0].Url}),"UI comum "+preexisting+": arquivos preexistentes aguardam verificacao sem aba; fresh abre somenteA");
            if(!preexisting){
                string partial=Path.Combine(f.Downloads,f.A.Assets[0].Name+".crdownload");var bytes=File.ReadAllBytes(Path.Combine(f.Sources,f.A.Assets[0].Name));File.WriteAllBytes(partial,bytes.Take(bytes.Length/2).ToArray());File.SetCreationTimeUtc(partial,DateTime.UtcNow);File.SetLastWriteTimeUtc(partial,DateTime.UtcNow);Set(f.Window,"watchedSinceUtc",DateTime.UtcNow.AddSeconds(-1));
                Tick(f.Window);Wait(()=>Idle(f.Window));var row=f.Row(f.A);string state=Convert.ToString(row.Cells["State"].Value),progressText=Convert.ToString(row.Cells["Progress"].Value);int progress=-1;bool estimated=progressText.StartsWith("~",StringComparison.Ordinal)&&Int32.TryParse(progressText.Substring(1),out progress);
                check(estimated&&state.IndexOf("Baixando",StringComparison.OrdinalIgnoreCase)>=0&&progress>=40&&progress<=60&&!f.Cache.Contains(f.A.Assets[0])&&f.InstallerCalls==0&&f.ReceiptFiles().Length==0&&!FixedData.Scan(f.A,f.Game).IsCorrect,"ParcialA aparece Baixando com porcentagem aproximada, sem cache, recibo ou instalacao");
                File.Delete(partial);
            }
            f.Complete(f.A,preexisting);
            check(f.Cache.Contains(f.A.Assets[0])&&FixedData.Scan(f.A,f.Game).IsCorrect&&!FixedData.Scan(f.B,f.Game).IsCorrect&&f.InstallerCalls==1&&f.ReceiptFiles().Length==1,"A final passa monitor/hash/importador/motor reais e instala somenteA antes de esperarB");
            check(f.ACorrectBeforeB&&(preexisting?f.Opened.Count==0:f.Opened.SequenceEqual(new[]{f.A.Assets[0].Url,f.B.Assets[0].Url})),"PaginaB so abre aposA aplicado;B preexistente continua sem abrir aba");
            check(f.States.Any(state=>state.StartsWith(f.A.Id+"|State|Instalando",StringComparison.Ordinal))&&Convert.ToString(f.Row(f.A).Cells["Installed"].Value)=="Instalado correto","UI registra etapa InstalandoA e so mostra Instalado correto apos scan dos destinos");
            f.Complete(f.B,preexisting);
            check(f.InstallerCalls==2&&f.Cache.Contains(f.B.Assets[0])&&FixedData.Scan(f.A,f.Game).IsCorrect&&FixedData.Scan(f.B,f.Game).IsCorrect&&((HashSet<string>)Get(f.Window,"automaticQueue")).Count==0&&Get(f.Window,"downloadWatch")==null,"B final instala automaticamente e encerra a unica fila autorizada");
            check(f.States.Any(state=>state.StartsWith(f.B.Id+"|State|Instalando",StringComparison.Ordinal))&&Convert.ToString(f.Row(f.B).Cells["Installed"].Value)=="Instalado correto"&&(preexisting?f.Opened.Count==0:f.Opened.Count==2),"UI concluiB com estado de instalacao real e nenhuma pagina extra");
            var receipts=f.ReceiptFiles().Select(f.Receipt).ToList();
            check(receipts.Count==2&&receipts.All(receipt=>receipt.Status=="instalado"&&receipt.Files.Count==1&&receipt.Files.All(file=>file.Path==f.A.Files[0].Path||file.Path==f.B.Files[0].Path))&&!f.Cache.Contains(f.Unselected.Assets[0])&&!File.Exists(Safe.In(f.Game,f.Unselected.Files[0].Path)),"Recibos contem apenas arquivosA/B; itemC nao selecionado nao e baixado ou instalado");
            check(f.BaseHashes.All(pair=>Safe.Hash(Safe.In(f.Game,pair.Key))==pair.Value)&&Safe.Hash(f.Foreign)==foreignHash&&Safe.Hash(f.SaveFile)==saveHash&&sourceHashes.All(pair=>Safe.Hash(pair.Key)==pair.Value),"Base de cinco arquivos, arquivo alheio, saves e pacotes originais permanecem iguais");
            var downloadHashes=Directory.GetFiles(f.Downloads,"*.zip").ToDictionary(file=>file,file=>Safe.Hash(file));check(downloadHashes.Count==2&&downloadHashes.Values.Contains(f.A.Assets[0].Sha256)&&downloadHashes.Values.Contains(f.B.Assets[0].Sha256),"Arquivos finaisA/B continuam em Downloads com mesmos hashes apos importar e instalar");
            check(Directory.GetFiles(Path.Combine(f.Game,".CoopModsOnline","backups"),"save.dat",SearchOption.AllDirectories).Length==2&&Directory.GetFiles(Path.Combine(f.Game,".CoopModsOnline","backups"),"save.dat",SearchOption.AllDirectories).All(file=>Safe.Hash(file)==saveHash),"Cada instalacao guarda backup conferido somente do save sintetico da fixture");
            var engine=new FixedInstaller(f.Catalog,f.Cache,f.Game,f.Saves,true);engine.Undo(message=>{});engine.Undo(message=>{});
            check(!File.Exists(Safe.In(f.Game,f.A.Files[0].Path))&&!File.Exists(Safe.In(f.Game,f.B.Files[0].Path))&&Safe.Hash(f.Foreign)==foreignHash&&Safe.Hash(f.SaveFile)==saveHash&&f.BaseHashes.All(pair=>Safe.Hash(Safe.In(f.Game,pair.Key))==pair.Value)&&sourceHashes.All(pair=>Safe.Hash(pair.Key)==pair.Value)&&downloadHashes.All(pair=>Safe.Hash(pair.Key)==pair.Value),"Desfazer duas operacoes retira somenteA/B e preserva base, alheio, saves atuais e originais");
        }
        Safe.WriteJson(Path.Combine(root,"resultado-unified.json"),new{Passed=passed.Count,Tests=passed,RealMonitor=true,RealImporter=true,RealInstallerInFixture=true,RealGameTouched=false,NetworkUsed=false,BrowserSimulated=true});File.WriteAllText(Path.Combine(parent,"ultimo-teste-unified-queue.txt"),root);return passed.Count;
    }
}
