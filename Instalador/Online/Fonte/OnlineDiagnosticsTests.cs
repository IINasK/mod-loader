using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

public static class OnlineDiagnosticsTests {
    public static List<Dictionary<string,object>> Read(string path){return File.ReadAllLines(path).Where(s=>!String.IsNullOrWhiteSpace(s)).Select(s=>Safe.Json.Deserialize<Dictionary<string,object>>(s)).ToList();}
    public static void Run(string parent){
        string root=Path.Combine(parent,"online-testes-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var passed=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("TESTE DIAGNOSTICO FALHOU: "+name);passed.Add(name);};
        FixedAsset asset;var catalog=OnlineTests.MakeCatalog(root,out asset);string game=OnlineTests.Fixture(root,"game");var cache=new DownloadCache(Path.Combine(root,"cache"));
        foreach(var file in catalog.Packages[0].Files){string path=Safe.In(game,file.Path);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,"test "+file.Path,new System.Text.UTF8Encoding(false));}
        string log=null;
        try{
            OnlineDiagnostics.Start(Path.Combine(root,"logs"));log=OnlineDiagnostics.FilePath;check(OnlineDiagnostics.Enabled&&File.Exists(log),"Sessao cria registro JSONL proprio fora do jogo");
            Parallel.For(0,16,n=>OnlineDiagnostics.Record("parallel-test",null,"Evento de teste.",new{Number=n}));var events=Read(log);
            check(events.Count(e=>Convert.ToString(e["Event"])=="parallel-test")==16&&events.Select(e=>Convert.ToInt64(e["Sequence"])).SequenceEqual(Enumerable.Range(1,events.Count).Select(n=>(long)n)),"Eventos concorrentes mantem JSON valido e sequencia crescente sem perda");
            cache.Import(Path.Combine(root,"fixture.zip"),asset);var plan=AutoInstallPlan.Build(catalog,new[]{"Extra"},game,cache,false);
            check(!plan.Complete&&plan.ReadyMods.Select(m=>m.Id).SequenceEqual(new[]{"Extra"})&&!FixedData.Scan(catalog.Packages[1],game).IsCorrect,"Arquivo completo no cache nao e considerado mod instalado");
            events=Read(log);check(events.Any(e=>Convert.ToString(e["Event"])=="cache.import.verified")&&!events.Any(e=>Convert.ToString(e["Event"])=="install.request"),"Log diferencia importacao do arquivo de acionamento do motor de instalacao");
            var importer=new ModImporter(catalog,cache,Path.Combine(root,"pending.json"));try{importer.Import("https://www.nexusmods.com/cyberpunk2077/mods/123?apikey=diagnostic-secret-test");}catch(IOException){}
            check(!File.ReadAllText(log).Contains("diagnostic-secret-test"),"Registro nao copia URL nem parametro sensivel recebido pelo importador");
            var engine=new FixedInstaller(catalog,cache,game,null,true);engine.Install(new[]{"Extra"},true,s=>{});check(FixedData.Scan(catalog.Packages[1],game).IsCorrect,"Instalado e confirmado somente apos copia real e hashes no destino");
            events=Read(log);int imported=events.FindIndex(e=>Convert.ToString(e["Event"])=="cache.import.verified"),begin=events.FindIndex(e=>Convert.ToString(e["Event"])=="install.begin"),applied=events.FindIndex(e=>Convert.ToString(e["Event"])=="install.file.applied"),complete=events.FindIndex(e=>Convert.ToString(e["Event"])=="install.complete");
            check(imported>=0&&begin>imported&&applied>begin&&complete>applied&&events.Count(e=>Convert.ToString(e["Event"])=="install.file.applied")==2,"Linha do tempo registra pacote, inicio, arquivos aplicados, recibo e conclusao na ordem real");
            check(events.Any(e=>Convert.ToString(e["Event"])=="scan.file")&&events.Any(e=>Convert.ToString(e["Event"])=="scan.result"),"Diagnostico guarda evidencias de arquivo e resultado da conferenca por mod");
            OnlineDiagnostics.Stop();long length=new FileInfo(log).Length;OnlineDiagnostics.Record("ignored",null,"Nao deve gravar.");check(OnlineDiagnostics.FilePath==log&&!OnlineDiagnostics.Enabled&&new FileInfo(log).Length==length,"Encerrar preserva caminho consultavel e impede gravacao posterior");
            string invalid=Path.Combine(root,"not-a-folder");File.WriteAllText(invalid,"original");OnlineDiagnostics.Start(invalid);check(!OnlineDiagnostics.Enabled&&OnlineDiagnostics.Issue!=null&&AutoInstallPlan.Build(catalog,new[]{"Extra"},game,cache,false).Complete&&File.ReadAllText(invalid)=="original","Falha do logger nao muda arquivo nem bloqueia o plano de instalacao");
        }finally{OnlineDiagnostics.Stop();}
        Safe.WriteJson(Path.Combine(root,"resultado-diagnostico.json"),new{Passed=passed.Count,Tests=passed,FixtureOnly=true,RealGameTouched=false,Log=log});File.WriteAllText(Path.Combine(parent,"ultimo-teste-diagnostico.txt"),root);
    }
}
