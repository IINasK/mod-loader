using System;
using System.Collections;
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

public static class OnlineFacialInstallTests {
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static readonly string[] SupportIds={"face-7054-91914","face-1873-56672"};
    static object Get(object target,string name){var field=target.GetType().GetField(name,Private);if(field==null)throw new Exception("Campo de fixture ausente: "+name);return field.GetValue(target);}
    static void Set(object target,string name,object value){target.GetType().GetField(name,Private).SetValue(target,value);}
    static object Call(object target,string name,params object[] args){try{return target.GetType().GetMethod(name,Private).Invoke(target,args);}catch(TargetInvocationException ex){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException??ex).Throw();throw;}}
    static void Wait(Func<bool> done,string detail){var timer=Stopwatch.StartNew();while(!done()){if(timer.Elapsed>TimeSpan.FromSeconds(30))throw new Exception("Fixture facial excedeu30segundos: "+detail);Application.DoEvents();Thread.Sleep(5);}Application.DoEvents();}
    static void Pump(Task task){Wait(()=>task.IsCompleted,"tarefaUI");task.GetAwaiter().GetResult();Application.DoEvents();}
    static void Tick(OnlineWindow window){typeof(System.Windows.Forms.Timer).GetMethod("OnTick",Private).Invoke(Get(window,"downloadWatchTimer"),new object[]{EventArgs.Empty});Application.DoEvents();}
    static bool Idle(OnlineWindow window){return !(bool)Get(window,"busy")&&!(bool)Get(window,"automaticFlow")&&!(bool)Get(window,"pollingDownload");}
    static string Hash(string value){using(var sha=System.Security.Cryptography.SHA256.Create())return Safe.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));}
    static void Write(string root,string relative,string value){string path=Safe.In(root,relative);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,value,new UTF8Encoding(false));}
    static T Clone<T>(T value){return Safe.Json.Deserialize<T>(Safe.Json.Serialize(value));}
    static List<string> FaceIds(){return FacialDownload.List().Select(item=>item.Id).ToList();}
    static bool AllCorrect(IEnumerable<FixedMod> mods,string game){return mods.All(mod=>FixedData.Scan(mod,game).IsCorrect);}
    static void Show(OnlineWindow window){window.ShowInTaskbar=false;window.StartPosition=FormStartPosition.Manual;window.Location=new Point(-30000,-30000);window.Show();Application.DoEvents();}
    static DataGridView Grid(OnlineWindow window){return (DataGridView)Get(window,"grid");}
    static void SelectFaces(OnlineWindow window){var ids=new HashSet<string>(FaceIds());foreach(DataGridViewRow row in Grid(window).Rows)row.Cells["Select"].Value=ids.Contains(Convert.ToString(row.Tag));Application.DoEvents();}
    static FixedMod InertMod(FixedMod approved,string sources,int index,HashSet<string> allowed){
        var mod=Clone(approved);var random=new byte[700+index*397];new Random(470001+index).NextBytes(random);string content="Inert facial fixture "+index+" "+Convert.ToBase64String(random),entry="archive/pc/mod/facial-fixture-"+index+".archive",name=mod.Id+".zip",path=Path.Combine(sources,name);
        using(var zip=ZipFile.Open(path,ZipArchiveMode.Create)){var file=zip.CreateEntry(entry);using(var output=new StreamWriter(file.Open(),new UTF8Encoding(false)))output.Write(content);}
        var original=approved.Assets.First();var asset=new FixedAsset{Id="fixture-asset",Name=name,ModId=original.ModId,FileId=original.FileId,Url=original.Url,Format="zip",Size=new FileInfo(path).Length,Sha256=Safe.Hash(path)};
        mod.Immutable=false;mod.RequiresPL=false;mod.RequiresRedmod=false;mod.IncompatiblePaths=null;mod.ExclusiveCodeDirectories=null;mod.BlockedReason=null;mod.Dependencies=approved.Dependencies.Where(allowed.Contains).Concat(new[]{"NightCoop"}).Distinct().ToArray();
        mod.Assets=new List<FixedAsset>{asset};mod.Files=new List<FixedFile>{new FixedFile{Asset=asset.Id,Entry=entry,Path=entry,Size=Encoding.UTF8.GetByteCount(content),Sha256=Hash(content)}};return mod;
    }
    sealed class Fixture:IDisposable {
        public readonly string Root,Game,Downloads,Sources,Saves,Foreign,SaveFile;
        public readonly FixedCatalog Catalog;
        public readonly DownloadCache Cache;
        public readonly OnlineWindow Window;
        public readonly List<FixedMod> Ordered;
        public readonly List<string> Opened=new List<string>(),States=new List<string>();
        public readonly Dictionary<string,string> BaseHashes=new Dictionary<string,string>();
        public int InstallerCalls,ConfirmCalls;
        public bool PagesFollowInstalled=true;
        public Fixture(string root,FixedCatalog approved,string mode){
            Root=root;Game=Path.Combine(root,"game");Downloads=Path.Combine(root,"downloads");Sources=Path.Combine(root,"sources");Saves=Path.Combine(root,"saves");Directory.CreateDirectory(Sources);Directory.CreateDirectory(Downloads);Directory.CreateDirectory(Path.Combine(Game,"bin","x64"));
            File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","FixtureGame.exe"),Path.Combine(Game,"bin","x64","Cyberpunk2077.exe"));Write(Game,".fixture-only","synthetic facial fixture");
            var baseAsset=new FixedAsset{Id="base",Name="base.zip",Size=4,Sha256=Hash("base"),Format="raw",Url="https://github.com/example/project/releases/download/fixed/base.zip"};var basis=new FixedMod{Id="NightCoop",Name="Fixture base",Version="fixed",GameVersion="2.5",Immutable=true,Dependencies=new string[0],Assets=new List<FixedAsset>{baseAsset},Files=new List<FixedFile>()};
            foreach(string relative in new[]{"bin/x64/winmm.dll","red4ext/RED4ext.dll","red4ext/LICENSE.txt","red4ext/THIRD_PARTY_LICENSES.txt","red4ext/plugins/NightCoop/NightCoop.dll"}){Write(Game,relative,"base");BaseHashes.Add(relative,Safe.Hash(Safe.In(Game,relative)));basis.Files.Add(new FixedFile{Asset=baseAsset.Id,Entry="",Path=relative,Size=4,Sha256=baseAsset.Sha256});}
            var ids=new HashSet<string>(FaceIds().Concat(SupportIds));var packages=new List<FixedMod>{basis};int index=0;foreach(var mod in approved.Packages.Where(mod=>ids.Contains(mod.Id)))packages.Add(InertMod(mod,Sources,index++,ids));
            Catalog=new FixedCatalog{Schema=1,Id=approved.Id,GameVersion="2.5",GameFileVersion="2.5.0.0",Packages=packages};FixedData.Validate(Catalog);Ordered=FixedData.Expand(Catalog,FaceIds()).Where(mod=>!mod.Immutable).ToList();if(Ordered.Count!=16)throw new Exception("Dependencias faciais da fixture nao fecham16componentes.");
            Cache=new DownloadCache(Path.Combine(root,"cache"));Foreign=Safe.In(Game,"archive/pc/mod/foreign.archive");Write(Game,"archive/pc/mod/foreign.archive","foreign initial");Write(Saves,"ManualSave-fixture/save.dat","save initial");SaveFile=Safe.In(Saves,"ManualSave-fixture/save.dat");
            if(mode=="preexisting")foreach(var mod in Ordered)Download(mod);
            if(mode=="warm-cache"){var importer=new ModImporter(Catalog,Cache,Path.Combine(root,"importados-pendentes.json"));foreach(var mod in Ordered)importer.Import(Path.Combine(Sources,mod.Assets[0].Name));}
            Window=new OnlineWindow(Catalog,Cache,true);Set(Window,"downloadFolder",Downloads);((TextBox)Get(Window,"game")).Text=Game;
            Set(Window,"confirmInstallation",new Func<string,bool>(message=>{ConfirmCalls++;return true;}));Set(Window,"confirmRemoval",new Func<string,bool>(message=>true));
            Func<string,FixedInstaller> factory=path=>{if(Path.GetFullPath(path)!=Game)throw new IOException("Fixture recusou destino externo.");Interlocked.Increment(ref InstallerCalls);return new FixedInstaller(Catalog,Cache,path,Saves,true);};Set(Window,"createInstaller",factory);Set(Window,"createRemovalInstaller",new Func<string,FixedInstaller>(path=>{if(Path.GetFullPath(path)!=Game)throw new IOException("Fixture recusou remocao externa.");return new FixedInstaller(Catalog,Cache,path,Saves,true);}));
            Set(Window,"launchBrowser",new Action<string>(url=>{var next=Ordered.Single(mod=>mod.Assets[0].Url==url);int at=Ordered.IndexOf(next);PagesFollowInstalled=PagesFollowInstalled&&AllCorrect(Ordered.Take(at),Game);Opened.Add(next.Id);}));
            Grid(Window).CellValueChanged+=(sender,args)=>{if(args.RowIndex<0||args.ColumnIndex<0)return;var row=Grid(Window).Rows[args.RowIndex];string column=Grid(Window).Columns[args.ColumnIndex].Name;if(column=="State"||column=="Installed")States.Add(Convert.ToString(row.Tag)+"|"+column+"|"+Convert.ToString(row.Cells[column].Value));};Show(Window);
        }
        public DataGridViewRow Row(string id){return Grid(Window).Rows.Cast<DataGridViewRow>().Single(row=>Convert.ToString(row.Tag)==id);}
        public void Download(FixedMod mod){File.Copy(Path.Combine(Sources,mod.Assets[0].Name),Path.Combine(Downloads,mod.Assets[0].Name));}
        public void Start(){SelectFaces(Window);((Button)Get(Window,"mainInstallButton")).PerformClick();Wait(()=>Idle(Window)&&(Get(Window,"downloadWatch")!=null||AllCorrect(Ordered,Game)),"clique Baixar e instalar: "+((Label)Get(Window,"status")).Text);}
        public void Arrive(FixedMod mod,bool preexisting){
            if(!preexisting)Download(mod);var watch=(OnlineDownloadWatch)Get(Window,"downloadWatch");if(watch==null)throw new Exception("Sem monitor comum para "+mod.Id);
            if(!watch.HasPendingCandidates)watch.Poll(DateTime.UtcNow.AddSeconds(-3));
            // Advance the fixture's observation clock, never bypass identity, exclusivity or verification.
            foreach(DictionaryEntry pair in (IDictionary)Get(watch,"seen"))pair.Value.GetType().GetField("Since",BindingFlags.Instance|BindingFlags.Public).SetValue(pair.Value,DateTime.UtcNow.AddSeconds(-3));
            Tick(Window);Wait(()=>Idle(Window)&&FixedData.Scan(mod,Game).IsCorrect,"chegada "+mod.Id+": "+((Label)Get(Window,"status")).Text);
        }
        public string[] Receipts(){string data=Path.Combine(Game,".CoopModsOnline");return Directory.Exists(data)?Directory.GetFiles(data,"*.json"):new string[0];}
        public OnlineReceipt Receipt(string path){return (OnlineReceipt)Call(new FixedInstaller(Catalog,Cache,Game,Saves,true),"ReadReceipt",path);}
        public void Dispose(){Call(Window,"ClearAutomaticQueue");Window.Close();Window.Dispose();Application.DoEvents();}
    }
    public static int Run(string parent){
        if(Thread.CurrentThread.GetApartmentState()!=ApartmentState.STA)throw new Exception("Facial install tests exigeSTA.");Exception failure=null;int count=0;var previous=SynchronizationContext.Current;
        try{using(var host=new Form{ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-30000,-30000),Size=new Size(1,1)}){host.Shown+=(sender,args)=>host.BeginInvoke(new Action(()=>{try{count=RunCases(parent);}catch(Exception ex){failure=ex;}finally{host.Close();}}));Application.Run(host);}}
        finally{SynchronizationContext.SetSynchronizationContext(previous);}if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();return count;
    }
    static int RunCases(string parent){
        string root=Path.Combine(Path.GetFullPath(parent),"online-testes-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var passed=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("TESTE FACIAL INSTALL FALHOU: "+name);passed.Add(name);};var approved=FixedData.Load();var faceIds=FaceIds();var allIds=new HashSet<string>(faceIds.Concat(SupportIds));var actual=approved.Packages.Where(mod=>allIds.Contains(mod.Id)).ToList();
        check(approved.Packages.Count==139&&approved.Packages.Count(mod=>String.IsNullOrEmpty(mod.BlockedReason))==136&&actual.Count==16,"Catalogo real139registros136visiveis preserva14faciais e2suportes");
        var mecha=approved.Packages.Single(mod=>mod.Id=="face-35807-168171");
        check(mecha.NexusModId==35807&&mecha.NexusFileId==168171&&mecha.Assets.Single().Sha256=="a5f0dcf46f470b290c14b083838195b5b7df8ecaf95ca89de2eb9cdb94c1f01e"&&mecha.Assets.Single().Size==6387617&&mecha.Files.Count==3&&new HashSet<string>(mecha.Dependencies).SetEquals(new[]{"ArchiveXL","TweakXL"}),"Mecha adicional possui pacote e tres destinos fixos, usando os frameworks existentes");
        check(approved.Packages.Single(mod=>mod.Id=="ArchiveXL").Version=="v1.28.2"&&approved.Packages.Single(mod=>mod.Id=="TweakXL").Version=="v1.12.0"&&mecha.Files.All(file=>!file.Path.StartsWith("red4ext/",StringComparison.OrdinalIgnoreCase)&&!file.Path.StartsWith("bin/",StringComparison.OrdinalIgnoreCase)),"Mecha nao altera frameworks ou loader e nao adiciona DLL ou codigo executavel");
        check(actual.All(mod=>mod.Assets.Count>0&&mod.Files.Count>0&&mod.Assets.All(asset=>asset.Size>0&&FixedData.HashValid(asset.Sha256)&&asset.ModId>0&&asset.FileId>0&&asset.Url=="https://www.nexusmods.com/cyberpunk2077/mods/"+asset.ModId+"?tab=files&file_id="+asset.FileId)&&mod.Files.All(file=>file.Size>0&&FixedData.HashValid(file.Sha256)&&Safe.Allowed(file.Path)))&&actual.Sum(mod=>mod.Files.Count)==24,"Todos16componentes possuem pacotes fixados e24destinos/hash/tamanhos reais, sem receitas genericas");
        check(actual.Where(mod=>faceIds.Contains(mod.Id)).All(mod=>mod.Name.StartsWith("Rosto · ",StringComparison.Ordinal))&&actual.Where(mod=>SupportIds.Contains(mod.Id)).All(mod=>mod.Name.StartsWith("Suporte facial · ",StringComparison.Ordinal)),"Nomes dos14itens e2suportes permanecem identificaveis na lista comum");
        var realExpanded=FixedData.Expand(approved,faceIds);check(allIds.All(id=>realExpanded.Any(mod=>mod.Id==id))&&actual.All(mod=>!mod.RequiresRedmod&&!mod.Immutable)&&actual.SelectMany(mod=>mod.Files).All(file=>!file.Path.StartsWith("red4ext/",StringComparison.OrdinalIgnoreCase)&&!file.Path.StartsWith("bin/x64/",StringComparison.OrdinalIgnoreCase)),"Selecao14expandeHead/Avacore e nao escreve loader/base nem exigeREDmod");
        string metadataRoot=Path.Combine(root,"metadata");Directory.CreateDirectory(Path.Combine(metadataRoot,"downloads"));var metadataCache=new DownloadCache(Path.Combine(metadataRoot,"cache"));var calls=new List<string>();
        using(var window=new OnlineWindow(approved,metadataCache,true)){
            Set(window,"downloadFolder",Path.Combine(metadataRoot,"downloads"));Set(window,"launchBrowser",new Action<string>(url=>calls.Add(url)));Show(window);SelectFaces(window);var selected=(List<string>)Call(window,"Chosen");
            check(Grid(window).Rows.Count==136&&Grid(window).Rows.Cast<DataGridViewRow>().Count(row=>faceIds.Contains(Convert.ToString(row.Tag)))==14&&new HashSet<string>(selected).SetEquals(faceIds),"Lista real tem136linhas e selecao dos14nao cria duplicacoes ou inclui outros mods");
            bool resolved=true;foreach(string id in faceIds){Grid(window).CurrentCell=Grid(window).Rows.Cast<DataGridViewRow>().Single(row=>Convert.ToString(row.Tag)==id).Cells["Mod"];var mod=(FixedMod)Call(window,"CurrentMod");resolved=resolved&&mod!=null&&mod.Id==id;}
            check(resolved&&((Button)Get(window,"mainInstallButton")).Text=="Baixar e instalar"&&!(bool)Call(window,"HandleFacialSelection",faceIds,true,false)&&Get(window,"facialQueue")==null&&calls.Count==0,"Cada facial resolveCurrentMod e segue botao/fila comum, sem criarFacialQueue ou abrir browser na selecao");window.Close();
        }
        foreach(string mode in new[]{"fresh","preexisting","warm-cache"})using(var f=new Fixture(Path.Combine(root,mode),approved,mode)){
            check(f.Ordered.Count==16&&f.Ordered.Select(mod=>mod.Assets[0].Size).Distinct().Count()==16&&!f.Ordered.Any(mod=>FixedData.Scan(mod,f.Game).IsCorrect)&&f.Receipts().Length==0,"Fixture "+mode+":16pacotes unicos inicialmente nao instalados e base5ja correta");
            if(mode=="warm-cache")check(f.Ordered.All(mod=>f.Cache.Contains(mod.Assets[0])&&Convert.ToString(f.Row(mod.Id).Cells["Installed"].Value)!="Instalado correto"),"Cache anteriormente importado nao e apresentado como instalado antes de aplicar arquivos");
            var originalHashes=Directory.GetFiles(f.Sources).ToDictionary(file=>file,file=>Safe.Hash(file));string initialSave=Safe.Hash(f.SaveFile),initialForeign=Safe.Hash(f.Foreign);f.Start();
            check(f.ConfirmCalls==1&&Get(f.Window,"facialQueue")==null,"Fixture "+mode+": um clique emBaixar e instalar usa unica confirmacao e fila original");
            if(mode!="warm-cache"){
                check(((HashSet<string>)Get(f.Window,"automaticQueue")).SetEquals(faceIds)&&f.InstallerCalls==0&&(mode=="preexisting"?f.Opened.Count==0:f.Opened.SequenceEqual(new[]{f.Ordered[0].Id})),"Fixture "+mode+": autoriza somente14ids; dependencias16seguem plano e preexistentes nao abrem paginas");
                if(mode=="fresh"){
                    var first=f.Ordered[0];var bytes=File.ReadAllBytes(Path.Combine(f.Sources,first.Assets[0].Name));string partial=Path.Combine(f.Downloads,first.Assets[0].Name+".crdownload");File.WriteAllBytes(partial,bytes.Take(bytes.Length/2).ToArray());File.SetCreationTimeUtc(partial,DateTime.UtcNow);File.SetLastWriteTimeUtc(partial,DateTime.UtcNow);Set(f.Window,"watchedSinceUtc",DateTime.UtcNow.AddSeconds(-1));Tick(f.Window);Wait(()=>Idle(f.Window),"progresso parcial");string text=Convert.ToString(f.Row(first.Id).Cells["Progress"].Value);int percent=-1;
                    check(Convert.ToString(f.Row(first.Id).Cells["State"].Value)=="Baixando"&&text.StartsWith("~",StringComparison.Ordinal)&&Int32.TryParse(text.Substring(1),out percent)&&percent>=40&&percent<=60&&f.InstallerCalls==0&&!f.Cache.Contains(first.Assets[0])&&f.Receipts().Length==0,"Parcial facial apresenta~50porcento sem importar, instalar ou criar recibo");File.Delete(partial);Tick(f.Window);Wait(()=>Idle(f.Window),"parcial removido");
                    check(Convert.ToString(f.Row(first.Id).Cells["State"].Value)!="Baixando"&&!Convert.ToString(f.Row(first.Id).Cells["Progress"].Value).StartsWith("~",StringComparison.Ordinal)&&f.InstallerCalls==0&&!f.Cache.Contains(first.Assets[0])&&f.Opened.Count==1,"Parcial apagado limpa estimativa antiga sem instalar ou abrir pagina repetida");
                }
                for(int i=0;i<f.Ordered.Count;i++){
                    var mod=f.Ordered[i];check(((FixedMod)Get(f.Window,"waitingMod")).Id==mod.Id,"Fixture "+mode+": fila aguarda componente topologico "+(i+1));f.Arrive(mod,mode=="preexisting");
                    check(FixedData.Scan(mod,f.Game).IsCorrect&&Convert.ToString(f.Row(mod.Id).Cells["Installed"].Value)=="Instalado correto"&&f.States.Any(state=>state.StartsWith(mod.Id+"|State|Conferindo arquivo",StringComparison.Ordinal))&&f.States.Any(state=>state.StartsWith(mod.Id+"|State|Instalando",StringComparison.Ordinal)),"Fixture "+mode+": pacote "+(i+1)+" passa conferir/importar/instalar e scan antes de InstalledCorrect");
                }
            }
            check(AllCorrect(f.Ordered,f.Game)&&((HashSet<string>)Get(f.Window,"automaticQueue")).Count==0&&Get(f.Window,"downloadWatch")==null&&f.PagesFollowInstalled&&(mode=="fresh"?f.Opened.SequenceEqual(f.Ordered.Select(mod=>mod.Id)):f.Opened.Count==0),"Fixture "+mode+":16componentes instalados, fila encerrada, pagina seguinte somente apos anterior aplicado e sem abas desnecessarias");
            check(f.Ordered.All(mod=>Convert.ToString(f.Row(mod.Id).Cells["Installed"].Value)=="Instalado correto"&&Convert.ToString(f.Row(mod.Id).Cells["State"].Value)=="Já instalado"),"Fixture "+mode+": scan correto prevalece sobre cache e nao volta para Pronto para instalar");
            check(f.Ordered.All(mod=>f.Cache.Contains(mod.Assets[0]))&&f.BaseHashes.All(pair=>Safe.Hash(Safe.In(f.Game,pair.Key))==pair.Value)&&Safe.Hash(f.Foreign)==initialForeign&&Safe.Hash(f.SaveFile)==initialSave&&originalHashes.All(pair=>Safe.Hash(pair.Key)==pair.Value),"Fixture "+mode+": cache completo preserva base, alheios, saves e originais");
            var receipts=f.Receipts().Select(f.Receipt).ToList();check(receipts.All(receipt=>receipt.Status=="instalado")&&receipts.SelectMany(receipt=>receipt.Files).Select(file=>file.Path).Distinct().Count()==16&&receipts.All(receipt=>receipt.Files.All(file=>f.Ordered.Any(mod=>mod.Files[0].Path==file.Path))),"Fixture "+mode+": recibos registram somente16destinos permitidos");
            string backups=Path.Combine(f.Game,".CoopModsOnline","backups");check(Directory.GetFiles(backups,"save.dat",SearchOption.AllDirectories).Length==receipts.Count&&Directory.GetFiles(backups,"save.dat",SearchOption.AllDirectories).All(file=>Safe.Hash(file)==initialSave),"Fixture "+mode+": backup de save sintetico conferido para cada operacao");
            var engine=new FixedInstaller(f.Catalog,f.Cache,f.Game,f.Saves,true);int beforeGuard=f.Receipts().Length;string baseOnly=engine.DeactivateMany(new[]{"NightCoop"},message=>{});
            check(baseOnly.Contains("Nenhum complemento selecionado")&&baseOnly.Contains("base NightCoop/RED4ext foi preservada")&&f.Receipts().Length==beforeGuard&&f.BaseHashes.All(pair=>Safe.Hash(Safe.In(f.Game,pair.Key))==pair.Value)&&AllCorrect(f.Ordered,f.Game),"Fixture "+mode+": remover lote contendo somente base retorna sem operacao, recibo ou alteracao");
            bool blocked=false;try{engine.Deactivate("NightCoop",message=>{});}catch(IOException){blocked=true;}check(blocked&&f.Receipts().Length==beforeGuard&&f.BaseHashes.All(pair=>Safe.Hash(Safe.In(f.Game,pair.Key))==pair.Value),"Fixture "+mode+": API individual recusa desativar base imutavel sem recibo ou alteracao");
            SelectFaces(f.Window);Pump((Task)Call(f.Window,"RemoveSelected"));check(f.Catalog.Packages.Where(mod=>faceIds.Contains(mod.Id)).All(mod=>!File.Exists(Safe.In(f.Game,mod.Files[0].Path)))&&SupportIds.All(id=>FixedData.Scan(f.Catalog.Packages.Single(mod=>mod.Id==id),f.Game).IsCorrect)&&Safe.Hash(f.Foreign)==initialForeign,"Fixture "+mode+": remover14pelo botao preserva2suportes, base e arquivo alheio");
            File.WriteAllText(f.Foreign,"foreign changed after removal",new UTF8Encoding(false));File.WriteAllText(f.SaveFile,"save changed after removal",new UTF8Encoding(false));string laterForeign=Safe.Hash(f.Foreign),laterSave=Safe.Hash(f.SaveFile);engine.Undo(message=>{});
            check(AllCorrect(f.Ordered,f.Game)&&f.BaseHashes.All(pair=>Safe.Hash(Safe.In(f.Game,pair.Key))==pair.Value)&&Safe.Hash(f.Foreign)==laterForeign&&Safe.Hash(f.SaveFile)==laterSave&&originalHashes.All(pair=>Safe.Hash(pair.Key)==pair.Value),"Fixture "+mode+": desfazer restaura14e preserva modificacoes posteriores em alheios/saves e originais");
        }
        Safe.WriteJson(Path.Combine(root,"resultado-facial-install.json"),new{Passed=passed.Count,Tests=passed,ActualCatalogCount=approved.Packages.Count,RealMonitor=true,RealImporter=true,RealInstallerInFixture=true,BrowserSimulated=true,NetworkUsed=false,RealGameTouched=false});File.WriteAllText(Path.Combine(parent,"ultimo-teste-facial-install.txt"),root);return passed.Count;
    }
}
