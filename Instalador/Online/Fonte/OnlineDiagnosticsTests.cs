using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

public static class OnlineDiagnosticsTests {
    public static void Run(string parent){
        string root=Path.Combine(parent,"online-testes-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var passed=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("TESTE DIAGNOSTICO FALHOU: "+name);passed.Add(name);};
        FixedAsset asset;var catalog=OnlineTests.MakeCatalog(root,out asset);string game=OnlineTests.Fixture(root,"game");var cache=new DownloadCache(Path.Combine(root,"cache"));
        foreach(var file in catalog.Packages[0].Files){string path=Safe.In(game,file.Path);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,"test "+file.Path,new System.Text.UTF8Encoding(false));}
        try{
            OnlineDiagnostics.Start();check(OnlineDiagnostics.Enabled&&OnlineDiagnostics.Snapshot().Count==1,"Sessão começa com um registro em memória");
            Parallel.For(0,16,n=>OnlineDiagnostics.Record("parallel-test",null,"Evento de teste.",new{Number=n}));var events=OnlineDiagnostics.Snapshot();
            check(events.Count(e=>Convert.ToString(e["Event"])=="parallel-test")==16&&events.Select(e=>Convert.ToInt64(e["Sequence"])).SequenceEqual(Enumerable.Range(1,events.Count).Select(n=>(long)n)),"Eventos concorrentes mantem JSON valido e sequencia crescente sem perda");
            cache.Import(Path.Combine(root,"fixture.zip"),asset);var plan=AutoInstallPlan.Build(catalog,new[]{"Extra"},game,cache,false);
            check(!plan.Complete&&plan.ReadyMods.Select(m=>m.Id).SequenceEqual(new[]{"Extra"})&&!FixedData.Scan(catalog.Packages[1],game).IsCorrect,"Arquivo completo no cache nao e considerado mod instalado");
            events=OnlineDiagnostics.Snapshot();check(events.Any(e=>Convert.ToString(e["Event"])=="cache.import.verified")&&!events.Any(e=>Convert.ToString(e["Event"])=="install.request"),"Log diferencia importacao do arquivo de acionamento do motor de instalacao");
            var importer=new ModImporter(catalog,cache,Path.Combine(root,"pending.json"));try{importer.Import("https://www.nexusmods.com/cyberpunk2077/mods/123?apikey=diagnostic-secret-test");}catch(IOException){}
            check(!OnlineDiagnostics.Report(false).Contains("diagnostic-secret-test"),"Registro nao copia URL nem parametro sensivel recebido pelo importador");
            var engine=new FixedInstaller(catalog,cache,game,null,true);engine.Install(new[]{"Extra"},true,s=>{});check(FixedData.Scan(catalog.Packages[1],game).IsCorrect,"Instalado e confirmado somente apos copia real e hashes no destino");
            events=OnlineDiagnostics.Snapshot();int imported=events.FindIndex(e=>Convert.ToString(e["Event"])=="cache.import.verified"),begin=events.FindIndex(e=>Convert.ToString(e["Event"])=="install.begin"),applied=events.FindIndex(e=>Convert.ToString(e["Event"])=="install.file.applied"),complete=events.FindIndex(e=>Convert.ToString(e["Event"])=="install.complete");
            check(imported>=0&&begin>imported&&applied>begin&&complete>applied&&events.Count(e=>Convert.ToString(e["Event"])=="install.file.applied")==2,"Linha do tempo registra pacote, inicio, arquivos aplicados, recibo e conclusao na ordem real");
            check(events.Any(e=>Convert.ToString(e["Event"])=="scan.file")&&events.Any(e=>Convert.ToString(e["Event"])=="scan.result"),"Diagnostico guarda evidencias de arquivo e resultado da conferenca por mod");
            OnlineDiagnostics.Failure("fixture.install.failed",new IOException("Acesso negado em C:\\Users\\PrivateAccount\\Downloads\\test.zip"),
                "Instalar e verificar a variante selecionada.","FixtureMod","Mod de teste","natural","2.4","Preparando");
            OnlineDiagnostics.Record("fixture.privacy",null,"ML-P2P1:secret-invitation private@example.test 192.168.1.8",
                new{Actual="Bearer secret-auth-value",UnknownField="secret-unknown-value"});
            string report=OnlineDiagnostics.Report(false);
            check(report.Contains("Falhas recentes")&&report.Contains("Mod de teste")&&report.Contains("ModVersion = 2.4")&&
                report.Contains("Expected = Instalar e verificar")&&report.Contains("Actual = Acesso negado")&&report.Contains("Stage = Preparando"),
                "Relatório inclui mod, versão, etapa, resultado esperado e causa observada");
            check(!new[]{"PrivateAccount","secret-invitation","private@example","192.168.1.8","secret-auth-value","secret-unknown-value"}
                .Any(report.Contains),"Relatório omite caminhos pessoais, convites, IP, e-mail e credenciais");
            using(var options=new GeneralOptionsDialog("dark","pt",root,installationFolder:game))
            using(var dialog=GeneralOptionsDialog.CreateSessionReport()){
                var preview=dialog.Content.Controls.Find("SessionReportPreview",true).OfType<TextBox>().Single();
                check(options.Content.Controls.Find("ReportSessionError",true).OfType<Button>().Count()==1&&
                    dialog.Content.Controls.Find("CopySessionReport",true).OfType<Button>().Count()==1&&
                    preview.ReadOnly&&preview.Text.Contains("fixture.install.failed"),"Opções tem acesso à prévia e ao botão de copiar");
            }
            for(int i=0;i<500;i++)OnlineDiagnostics.Record("scan.file",null,"Evento de arquivo.");
            check(OnlineDiagnostics.Snapshot().Count<450&&OnlineDiagnostics.Report(true).Contains("fixture.install.failed")&&
                OnlineDiagnostics.Report(true).Contains("fixture.privacy"),"Varreduras longas preservam operações e falhas com memória limitada");
            OnlineDiagnostics.Stop();OnlineDiagnostics.Record("ignored",null,"Sessão encerrada.");
            check(!OnlineDiagnostics.Enabled&&OnlineDiagnostics.Snapshot().Count==0,"Encerrar libera o histórico e ignora eventos posteriores");
            OnlineDiagnostics.Start();
            check(OnlineDiagnostics.Snapshot().Count==1&&!OnlineDiagnostics.Report(false).Contains("FixtureMod"),"Reabrir não recupera a sessão anterior");
        }finally{OnlineDiagnostics.Stop();}
        Safe.WriteJson(Path.Combine(root,"resultado-diagnostico.json"),new{Passed=passed.Count,Tests=passed,FixtureOnly=true,RealGameTouched=false});File.WriteAllText(Path.Combine(parent,"ultimo-teste-diagnostico.txt"),root);
    }
}
