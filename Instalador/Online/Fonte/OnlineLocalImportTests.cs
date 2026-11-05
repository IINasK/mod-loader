using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class LocalImportTests {
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static object Get(object target,string name){return target.GetType().GetField(name,Private).GetValue(target);}
    static object Call(object target,string name,params object[] args){
        try{return target.GetType().GetMethod(name,Private).Invoke(target,args);}
        catch(TargetInvocationException ex){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException??ex).Throw();throw;}
    }
    static void Wait(Task task){
        var clock=Stopwatch.StartNew();
        while(!task.IsCompleted){if(clock.ElapsedMilliseconds>20000)throw new IOException("O teste da fila local não terminou.");Application.DoEvents();Thread.Sleep(5);}
        task.GetAwaiter().GetResult();Application.DoEvents();
    }
    static string Archive(string root,string name,string entry){
        string file=Path.Combine(root,name);
        using(var zip=ZipFile.Open(file,ZipArchiveMode.Create))
        using(var writer=new StreamWriter(zip.CreateEntry(entry).Open()))writer.Write("conteúdo de teste");
        return file;
    }
    internal static void Run(string parent) {
        Exception failure=null;
        var previous=SynchronizationContext.Current;
        try {
            using(var host=new Form{ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-30000,-30000),Size=new Size(1,1)}){
                host.Shown+=(s,e)=>host.BeginInvoke(new Action(()=>{try{RunCases(parent);}catch(Exception ex){failure=ex;}finally{host.Close();}}));
                Application.Run(host);
            }
        } finally {SynchronizationContext.SetSynchronizationContext(previous);}
        if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    static void RunCases(string parent) {
        string root=Path.Combine(parent,"online-testes-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var passed=new List<string>();
        Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("TESTE IMPORTAÇÃO LOCAL FALHOU: "+name);passed.Add(name);};
        FixedAsset asset;
        var catalog=OnlineTests.MakeCatalog(root,out asset);
        var cache=new DownloadCache(Path.Combine(root,"cache"));
        var importer=new ModImporter(catalog,cache,Path.Combine(root,"importados-pendentes.json"));
        string game=OnlineTests.Fixture(root,"game");
        string saves=Path.Combine(root,"saves");
        Directory.CreateDirectory(saves);
        string save=Path.Combine(saves,"fixture.dat");
        File.WriteAllText(save,"save preservado");
        foreach(var f in catalog.Packages[0].Files){
            string path=Safe.In(game,f.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,"test "+f.Path);
        }
        cache.Import(Path.Combine(root,"fixture.zip"),asset);
        string source=Archive(root,"Multi-Romance-fixture.zip","Multi-Romance/init.lua");
        var item=importer.Import(source).PendingItems.Single();
        string destination=Safe.In(game,"bin/x64/plugins/cyber_engine_tweaks/mods/Multi-Romance/init.lua");
        var engine=new FixedInstaller(catalog,cache,game,saves,true);
        check(!File.Exists(destination),"Arrastar sozinho ainda não instala");
        using(var dialog=new LocalImportWindow(item,game,true)){
            var button=(Button)dialog.Controls.Find("InstallImported",true).Single();
            check(button.Enabled&&button.Text=="Instalar"&&button.DialogResult==DialogResult.OK,"O diálogo disponibiliza Instalar mesmo com fila ativa");
        }

        using(var window=new OnlineWindow(catalog,cache,true)){
            window.ShowInTaskbar=false;
            window.StartPosition=FormStartPosition.Manual;
            window.Location=new Point(-30000,-30000);
            window.Show();Application.DoEvents();
            ((TextBox)Get(window,"game")).Text=game;
            bool installedBeforeCatalog=false;
            typeof(OnlineWindow).GetField("createInstaller",Private).SetValue(window,new Func<string,FixedInstaller>(path=>{
                installedBeforeCatalog|=File.Exists(destination)&&!FixedData.Scan(catalog.Packages[1],game).IsCorrect;
                return engine;
            }));
            Call(window,"LoadPending");Application.DoEvents();
            var row=((DataGridView)Get(window,"grid")).Rows.Cast<DataGridViewRow>().Single(r=>Convert.ToString(r.Tag)=="pending:"+item.Id);
            check(!row.ReadOnly&&Convert.ToString(row.Cells["State"].Value)=="Pronto para instalar","Arquivo arrastado aparece selecionável sem revisão");
            Call(window,"AuthorizeAutomatic",new[]{"Extra"},game,true);
            Call(window,"QueueImportedInstall",item,game);
            Wait((Task)Call(window,"ProcessAutomaticQueue",game));
            check(installedBeforeCatalog&&FixedData.Scan(catalog.Packages[1],game).IsCorrect,"Importado prioritário instala antes do catálogo e a fila normal continua");
            check(File.Exists(destination)&&engine.InstalledImports().Contains(item.Id)&&File.ReadAllText(save)=="save preservado","Pasta CET mapeada, recibo local conferido e save preservado");
            check(Convert.ToString(row.Cells["Installed"].Value)=="Instalado correto","Linha só indica instalado depois da escrita");
            window.Close();
        }
        engine.Undo(s=>{});
        engine.Undo(s=>{});
        check(!File.Exists(destination)&&!engine.InstalledImports().Contains(item.Id)&&File.Exists(source),"Desfazer usa os recibos da instalação e mantém o ZIP original");

        var conflict=importer.Import(Archive(root,"sobrescrita.zip","archive/pc/mod/test-a.archive")).PendingItems.Single();
        bool refused=false;
        try{engine.InstallImported(conflict,s=>{});}catch(IOException){refused=true;}
        check(refused&&!File.Exists(Safe.In(game,"archive/pc/mod/test-a.archive")),"Pacote importado não pode tomar o destino reservado de um mod do catálogo");
        var unsafeItem=importer.Import(Archive(root,"fora.zip","../fora.archive")).PendingItems.Single();
        refused=false;
        try{engine.InstallImported(unsafeItem,s=>{});}catch(IOException){refused=true;}
        check(refused&&!File.Exists(Path.Combine(game,"fora.archive")),"Entrada que foge da pasta é recusada");
        Safe.WriteJson(Path.Combine(root,"resultado-importacao-local.json"),new{Passed=passed.Count,Tests=passed,FixtureOnly=true,RealGameTouched=false});
    }
}
