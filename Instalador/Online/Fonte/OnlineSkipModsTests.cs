using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

public static class OnlineSkipModsTests {
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static object Get(object instance,string name){return instance.GetType().GetField(name,Private).GetValue(instance);}
    static void Set(object instance,string name,object value){instance.GetType().GetField(name,Private).SetValue(instance,value);}
    static object Call(object instance,string name,params object[] args){try{return instance.GetType().GetMethod(name,Private).Invoke(instance,args);}catch(TargetInvocationException ex){throw ex.InnerException??ex;}}
    static string Hash(string text){using(var sha=SHA256.Create())return Safe.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(text)));}
    static void Put(string root,string path,string text){string target=Safe.In(root,path);Directory.CreateDirectory(Path.GetDirectoryName(target));File.WriteAllText(target,text,new UTF8Encoding(false));}
    static FixedCatalog Clone(FixedCatalog c){return Safe.Json.Deserialize<FixedCatalog>(Safe.Json.Serialize(c));}
    static void Wait(Task task){var clock=Stopwatch.StartNew();while(!task.IsCompleted){if(clock.ElapsedMilliseconds>30000)throw new Exception("Fila de teste excedeu 30 segundos.");Application.DoEvents();Thread.Sleep(5);}task.GetAwaiter().GetResult();Application.DoEvents();}
    static FixedMod AddMod(FixedCatalog catalog,DownloadCache cache,string root,string id,string name,string[] dependencies){
        string relative="archive/pc/mod/skip-"+id.ToLowerInvariant()+".archive",text="bytes de teste "+id;
        var file=new FixedFile{Asset="a0",Entry=relative,Path=relative,Size=Encoding.UTF8.GetByteCount(text),Sha256=Hash(text)};
        string source=Path.Combine(root,"source-"+id+".zip");using(var zip=ZipFile.Open(source,ZipArchiveMode.Create))using(var writer=new StreamWriter(zip.CreateEntry(relative).Open(),new UTF8Encoding(false)))writer.Write(text);
        var asset=new FixedAsset{Id="a0",Name="source-"+id+".zip",Format="zip",Url="https://github.com/example/project/releases/download/fixed/"+id+".zip",Size=new FileInfo(source).Length,Sha256=Safe.Hash(source)};
        var mod=new FixedMod{Id=id,Name=name,Version="test",Profile="Complementos",GameVersion="2.5",RequiresPL=true,Dependencies=dependencies,Assets=new List<FixedAsset>{asset},Files=new List<FixedFile>{file}};
        catalog.Packages.Add(mod);cache.Import(source,asset);return mod;
    }
    static OnlineWindow Window(FixedCatalog catalog,DownloadCache cache,string game,string saves,Func<ModPreparationException,bool> decision,Action browserCall){
        var window=new OnlineWindow(catalog,cache,true);window.ShowInTaskbar=false;window.StartPosition=FormStartPosition.Manual;window.Location=new System.Drawing.Point(-30000,-30000);window.Show();Application.DoEvents();
        ((TextBox)Get(window,"game")).Text=game;
        Set(window,"createInstaller",new Func<string,FixedInstaller>(root=>new FixedInstaller(catalog,cache,root,saves,true)));
        Set(window,"confirmSkipFailure",decision);
        Set(window,"launchBrowser",new Action<string>(url=>browserCall()));return window;
    }
    static DataGridViewRow Row(OnlineWindow window,string id){return ((DataGridView)Get(window,"grid")).Rows.Cast<DataGridViewRow>().Single(r=>Convert.ToString(r.Tag)==id);}
    static string State(OnlineWindow window,string id){return Convert.ToString(Row(window,id).Cells["State"].Value);}
    public static void Run(string parent){
        Exception failure=null;
        var previous=SynchronizationContext.Current;
        try {
            using(var host=new Form{ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new System.Drawing.Point(-30000,-30000),Size=new System.Drawing.Size(1,1)}) {
                host.Shown+=(sender,args)=>host.BeginInvoke(new Action(()=>{
                    try { RunCases(parent); }
                    catch(Exception ex) { failure=ex; }
                    finally { host.Close(); }
                }));
                Application.Run(host);
            }
        } finally { SynchronizationContext.SetSynchronizationContext(previous); }
        if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    static void RunCases(string parent){
        string root=Path.Combine(Path.GetFullPath(parent),"online-testes-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var passed=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("TESTE PULAR FALHOU: "+name);passed.Add(name);};
        FixedAsset source;var catalog=OnlineTests.MakeCatalog(root,out source);var broken=catalog.Packages[1];broken.Name="Falha A";broken.Files[0].Entry="archive/pc/mod/entrada-ausente.archive";
        var cache=new DownloadCache(Path.Combine(root,"cache"));cache.Import(Path.Combine(root,"fixture.zip"),source);
        var independent=AddMod(catalog,cache,root,"Independent","Independente B",new[]{"NightCoop"});
        var middle=AddMod(catalog,cache,root,"Middle","Intermediário",new[]{broken.Id});
        var dependent=AddMod(catalog,cache,root,"Dependent","Dependente C",new[]{middle.Id});
        var unselected=AddMod(catalog,cache,root,"Unselected","Não selecionado",new[]{"NightCoop"});
        var existing=AddMod(catalog,cache,root,"Existing","Já instalado",new[]{"NightCoop"});FixedData.Validate(catalog);
        string game=OnlineTests.Fixture(root,"game"),saves=Path.Combine(root,"saves");Put(saves,"slot/sav.dat","save de teste preservado");
        foreach(var file in catalog.Packages[0].Files)Put(game,file.Path,"test "+file.Path);
        Put(game,existing.Files[0].Path,"bytes de teste "+existing.Id);
        var protectedFiles=catalog.Packages[0].Files.Concat(existing.Files).ToDictionary(f=>f.Path,f=>Safe.Hash(Safe.In(game,f.Path)));
        string savedHash=Safe.Hash(Safe.In(saves,"slot/sav.dat"));var sources=Directory.GetFiles(root,"*.zip").ToDictionary(p=>p,Safe.Hash);
        var assets=catalog.Packages.SelectMany(m=>m.Assets).GroupBy(a=>a.Sha256).Select(g=>g.First()).ToList();var cacheHashes=assets.ToDictionary(a=>a.Sha256,a=>Safe.Hash(cache.PathFor(a)));
        int browserCalls=0,skipCalls=0;
        using(var window=Window(catalog,cache,game,saves,f=>{skipCalls++;return f.ModId==broken.Id;},()=>browserCalls++)){
            foreach(string id in new[]{broken.Id,dependent.Id,independent.Id})Row(window,id).Cells["Select"].Value=true;
            Call(window,"AuthorizeAutomatic",new[]{broken.Id,dependent.Id,independent.Id},game,true);
            Wait((Task)Call(window,"ProcessAutomaticQueue",game));
            check(skipCalls==1,"Uma falha recebe uma única decisão de pular, sem repetir o mesmo trabalho");
            check(FixedData.Scan(independent,game).IsCorrect,"Fila original continua e instala mod independente com o motor real");
            check(new[]{broken,middle,dependent}.SelectMany(m=>m.Files).All(f=>!File.Exists(Safe.In(game,f.Path))),"Mod com falha e dependentes transitivos não são instalados");
            check(new[]{broken.Id,middle.Id,dependent.Id}.All(id=>State(window,id)=="Pulado"),"Tabela mantém estado Pulado para falha e dependentes transitivos");
            check(new[]{broken.Id,dependent.Id}.All(id=>!Convert.ToBoolean(Row(window,id).Cells["Select"].Value))&&Convert.ToBoolean(Row(window,independent.Id).Cells["Select"].Value),"Somente raízes puladas são desmarcadas; seleção independente permanece");
            check(unselected.Files.All(f=>!File.Exists(Safe.In(game,f.Path))),"Mod não selecionado não entra na instalação por causa do pulo");
            string status=((Label)Get(window,"status")).Text;
            check(status.IndexOf("pulado",StringComparison.OrdinalIgnoreCase)>=0&&status.IndexOf("todos",StringComparison.OrdinalIgnoreCase)<0,"Resumo final distingue itens pulados e não declara instalação total");
            check(((Label)Get(window,"summary")).Text.IndexOf("Pulado",StringComparison.OrdinalIgnoreCase)>=0,"Contador informa os itens pulados sem os contar como instalados");
            check(((HashSet<string>)Get(window,"automaticQueue")).Count==0&&!(bool)Get(window,"automaticFlow")&&!(bool)Get(window,"busy"),"Fila termina sem autorização residual nem interface bloqueada");
            check(protectedFiles.All(p=>File.Exists(Safe.In(game,p.Key))&&Safe.Hash(Safe.In(game,p.Key))==p.Value),"Base e mod já instalado preservados");
            check(Safe.Hash(Safe.In(saves,"slot/sav.dat"))==savedHash&&sources.All(p=>Safe.Hash(p.Key)==p.Value)&&assets.All(a=>Safe.Hash(cache.PathFor(a))==cacheHashes[a.Sha256]),"Pulo preserva saves, pacotes originais e cache conferido");
            check(browserCalls==0,"Teste da fila não abriu navegador nem executou download externo");
            broken.Files[0].Entry=broken.Files[0].Path;
            Row(window,dependent.Id).Cells["Select"].Value=true;Call(window,"AuthorizeAutomatic",new[]{dependent.Id},game,true);
            check(((HashSet<string>)Get(window,"skippedThisQueue")).Count==0,"Nova autorização libera itens pulados de uma seleção anterior");
            Wait((Task)Call(window,"ProcessAutomaticQueue",game));
            check(new[]{broken,middle,dependent}.All(m=>FixedData.Scan(m,game).IsCorrect),"Nova seleção instala o item corrigido e suas dependências normalmente");
            check(State(window,broken.Id)!="Pulado"&&State(window,dependent.Id)!="Pulado"&&skipCalls==1,"Pulo anterior não bloqueia nova tentativa nem produz nova decisão indevida");
            window.Close();
        }
        var stoppedCatalog=Clone(catalog);var stoppedBroken=stoppedCatalog.Packages.Single(m=>m.Id==broken.Id);stoppedBroken.Files[0].Entry="archive/pc/mod/entrada-ausente.archive";
        string stoppedGame=OnlineTests.Fixture(root,"stopped");foreach(var file in stoppedCatalog.Packages[0].Files)Put(stoppedGame,file.Path,"test "+file.Path);
        int stopCalls=0;
        using(var window=Window(stoppedCatalog,cache,stoppedGame,saves,f=>{stopCalls++;return false;},()=>browserCalls++)){
            Call(window,"AuthorizeAutomatic",new[]{broken.Id,independent.Id},stoppedGame,true);Wait((Task)Call(window,"ProcessAutomaticQueue",stoppedGame));
            check(stopCalls==1&&stoppedCatalog.Packages.Where(m=>!m.Immutable).SelectMany(m=>m.Files).All(f=>!File.Exists(Safe.In(stoppedGame,f.Path))),"Parar interrompe a fila sem instalar o mod independente ou complementos não autorizados");
            check(((HashSet<string>)Get(window,"automaticQueue")).Count==0&&State(window,broken.Id)!="Pulado","Parar limpa a fila sem registrar uma decisão de pular");
            var failure=new ModPreparationException(stoppedBroken,stoppedBroken.Assets[0],stoppedBroken.Files[0].Entry,new IOException("Entrada ausente"));Set(window,"operationGame",stoppedGame);
            check(!(bool)Call(window,"CanSkipPreparationFailure",failure),"Sem fila autorizada o botão Pular não fica disponível");
            Call(window,"AuthorizeAutomatic",new[]{broken.Id,independent.Id},stoppedGame,true);Set(window,"operationGame",stoppedGame);Set(window,"pendingDensityId","low");
            check(!(bool)Call(window,"CanSkipPreparationFailure",failure),"Troca de densidade não oferece pulo de componentes");
            Set(window,"pendingDensityId",null);Call(window,"ClearAutomaticQueue");
            using(var dialog=new SkipModFailureWindow(failure,true,"Dependente C")){
                var skip=dialog.Controls.Find("SkipMod",true).OfType<Button>().Single();var stop=dialog.Controls.Find("StopQueue",true).OfType<Button>().Single();
                check(skip.Enabled&&skip.Text.IndexOf("Pular",StringComparison.OrdinalIgnoreCase)>=0&&stop.Text.IndexOf("Parar",StringComparison.OrdinalIgnoreCase)>=0,"Diálogo apresenta ações explícitas Pular e continuar e Parar");
            }
            using(var dialog=new SkipModFailureWindow(failure,false,""))check(!dialog.Controls.Find("SkipMod",true).OfType<Button>().Single().Enabled,"Diálogo desabilita pulo fora de uma fila elegível");
            window.Close();
        }
        check(browserCalls==0&&Safe.Hash(Safe.In(saves,"slot/sav.dat"))==savedHash,"Cenários de continuação e interrupção preservam saves e não usam navegador real");
        Safe.WriteJson(Path.Combine(root,"resultado-skip.json"),new{Passed=passed.Count,Tests=passed,FixtureOnly=true,RealGameTouched=false,RealBrowserOpened=false});File.WriteAllText(Path.Combine(parent,"ultimo-teste-skip.txt"),root);
    }
}
