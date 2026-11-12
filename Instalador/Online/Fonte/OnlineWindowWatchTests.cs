using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

public static class OnlineWindowWatchTests {
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    const string Url="https://www.nexusmods.com/cyberpunk2077/mods/123?tab=files&file_id=456";
    sealed class MockBrowser {
        public string Current=Url;
        public bool FailClose;
        public readonly List<string> Calls=new List<string>();
        public string Request(string method,string path) {
            Calls.Add(method+" "+path);
            if(path.StartsWith("/json/new?"))return Safe.Json.Serialize(new{id="OwnedTab",type="page",url=Uri.UnescapeDataString(path.Substring("/json/new?".Length))});
            if(path=="/json/list")return Safe.Json.Serialize(new[]{new{id="OwnedTab",type="page",url=Current},new{id="OtherTab",type="page",url="https://example.org/keep"}});
            if(path=="/json/close/OwnedTab"){if(FailClose)throw new IOException("Controle local indisponivel na fixture.");return "Target is closing";}
            throw new IOException("Mock recusou controle de outra aba: "+path);
        }
        public int Closes {get{return Calls.Count(c=>c.StartsWith("GET /json/close/"));}}
    }
    sealed class Fixture:IDisposable {
        public readonly OnlineWindow Window;
        public readonly DownloadCache Cache;
        public readonly FixedMod Mod;
        public readonly FixedAsset Asset;
        public readonly MockBrowser Browser=new MockBrowser();
        public readonly string Root,Game,Downloads,Source,Downloaded;
        readonly OnlineBrowserSession session;
        public Fixture(string root,bool copy) {
            Root=root;Directory.CreateDirectory(root);Downloads=Path.Combine(root,"downloads");Directory.CreateDirectory(Downloads);
            Game=Path.Combine(root,"jogo-nao-tocar");Source=Path.Combine(root,"original.zip");Downloaded=Path.Combine(Downloads,"original (1).zip");
            File.WriteAllText(Source,"fixture original de download UI",new UTF8Encoding(false));if(copy)File.Copy(Source,Downloaded);
            Asset=new FixedAsset{Id="a0",Name="original.zip",Format="zip",ModId=123,FileId=456,Url=Url,Size=new FileInfo(Source).Length,Sha256=Safe.Hash(Source)};
            Mod=new FixedMod{Id="Extra",Name="Fixture Nexus",Version="1.0",GameVersion="2.5",Dependencies=new string[0],Assets=new List<FixedAsset>{Asset},Files=new List<FixedFile>{new FixedFile{Asset="a0",Entry="archive/pc/mod/extra.archive",Path="archive/pc/mod/extra.archive",Size=Asset.Size,Sha256=Asset.Sha256}}};
            var catalog=new FixedCatalog{Schema=1,Id="window-watch-fixture",GameVersion="2.5",GameFileVersion="2.5.0.0",Packages=new List<FixedMod>{Mod}};
            Cache=new DownloadCache(Path.Combine(root,"cache"));Window=new OnlineWindow(catalog,Cache,true);
            Set(Window,"launchBrowser",new Action<string>(url=>Browser.Calls.Add("MOCK OPEN "+url)));
            ((OnlineBrowserSession)Get(Window,"browser")).Dispose();session=new OnlineBrowserSession(Path.Combine(root,"mock-browser"),Browser.Request);Set(Window,"browser",session);
            Set(Window,"downloadFolder",Downloads);((TextBox)Get(Window,"game")).Text=Game;
            Window.ShowInTaskbar=false;Window.StartPosition=FormStartPosition.Manual;Window.Location=new Point(-30000,-30000);Window.Show();Application.DoEvents();
        }
        public bool Start() {
            bool started=(bool)Call(Window,"StartDownloadWatch",Mod,Asset,Game,false);
            if(started){Set(Window,"waitingTab",session.Open(Url));((OnlineDownloadWatch)Get(Window,"downloadWatch")).Poll(DateTime.UtcNow.AddSeconds(-3));}
            return started;
        }
        public void Check(){var watch=(OnlineDownloadWatch)Get(Window,"downloadWatch");if(watch!=null&&watch.HasPendingCandidates)Pump(Task.Delay(2100));Pump((Task)Call(Window,"CheckWatchedDownload"));}
        public BrowserTab OpenNormal(){var task=(Task<BrowserTab>)Call(Window,"OpenOfficial",Mod,Asset);Pump(task);return task.GetAwaiter().GetResult();}
        public void Dispose(){Window.Close();Window.Dispose();session.Dispose();Application.DoEvents();}
    }
    static object Get(object target,string name){return target.GetType().GetField(name,Private).GetValue(target);}
    static void Set(object target,string name,object value){target.GetType().GetField(name,Private).SetValue(target,value);}
    static object Call(object target,string name,params object[] args){return target.GetType().GetMethod(name,Private).Invoke(target,args);}
    static void Pump(Task task) {
        var clock=Stopwatch.StartNew();
        while(!task.IsCompleted){if(clock.Elapsed>TimeSpan.FromSeconds(10))throw new Exception("Interface de teste excedeu dez segundos.");Application.DoEvents();Thread.Sleep(5);}
        task.GetAwaiter().GetResult();Application.DoEvents();
    }
    public static void Run(string parent) {
        if(Thread.CurrentThread.GetApartmentState()!=ApartmentState.STA)throw new Exception("Ensaios da interface precisam de STA.");
        Exception failure=null;
        var previousContext=SynchronizationContext.Current;
        try{
            // Keep a real message loop alive while individual fixture windows are closed.
            using(var host=new Form{ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-30000,-30000),Size=new Size(1,1)}){
                host.Shown+=(sender,args)=>host.BeginInvoke(new Action(()=>{
                    try{RunCases(parent);}catch(Exception ex){failure=ex;}
                    finally{host.Close();}
                }));
                Application.Run(host);
            }
        }finally{SynchronizationContext.SetSynchronizationContext(previousContext);}
        if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    static void RunCases(string parent) {
        OnlineFaceDownloadsTests.Run(parent);
        FacialMainListTests.Run(parent);
        FacialDownloadQueueTests.Run(parent);
        string root=Path.Combine(Path.GetFullPath(parent),"ui-watch-"+Guid.NewGuid().ToString("N").Substring(0,12));Directory.CreateDirectory(root);
        var passed=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("TESTE WINDOW WATCH FALHOU: "+name);passed.Add(name);};
        var request=OnlineWindow.BrowserRequest(Url);
        using(var isolated=new OnlineWindow(FixedData.Load(),new DownloadCache(Path.Combine(Path.GetFullPath(parent),"blocked-browser-"+Guid.NewGuid().ToString("N"))),true)){bool denied=false;try{((Action<string>)Get(isolated,"launchBrowser"))(Url);}catch(IOException){denied=true;}check(denied,"Janela preview/teste bloqueia navegador real por padrao mesmo quando fixture esquece de injetar mock");}
        check(request.UseShellExecute&&request.Verb=="open"&&request.FileName==Url&&String.IsNullOrEmpty(request.Arguments),"Navegador recebe somente link pelo shell, sem executavel, argumentos, perfil novo ou depuracao");
        bool rejected=false;try{OnlineWindow.BrowserRequest("file:///C:/Windows/System32/cmd.exe");}catch(IOException){rejected=true;}check(rejected,"Abertura normal recusa links executaveis locais");
        using(var f=new Fixture(Path.Combine(root,"normal-session"),true)) {
            var controls=new Stack<Control>();controls.Push(f.Window);bool asksForKey=false,hasDownload=false,hasHeading=false,hasCredits=false;Button refreshButton=null,installButton=null,chooseGame=null;
            while(controls.Count>0){var control=controls.Pop();string text=control.Text??"";if(text.Contains("Conectar ao Nexus")||text.Contains("Nexus desconectado")||text.Contains("chave de API")||control is TextBox&&((TextBox)control).UseSystemPasswordChar)asksForKey=true;if(control is Button&&text=="Baixar e instalar"){hasDownload=true;installButton=(Button)control;}if(control is Button&&text=="Atualizar lista")refreshButton=(Button)control;if(control is Button&&text=="Escolher jogo…")chooseGame=(Button)control;if(control is Label&&text=="Cyberpunk Coop · Mods")hasHeading=true;if(control is Label&&text=="Criado por Nask e Codex. Se achou ruim, faz você.")hasCredits=true;foreach(Control child in control.Controls)controls.Push(child);}
            check(!asksForKey&&hasDownload&&!((NexusClient)Get(f.Window,"nexus")).Connected&&typeof(OnlineWindow).GetMethod("Connect",Private)==null,"Interface oferece baixar e instalar sem botao de conexao, campo de chave ou conta Nexus no app");
            var compactGrid=(DataGridView)Get(f.Window,"grid");check(!compactGrid.Columns["TPP"].Visible&&!compactGrid.Columns["Notes"].Visible&&!compactGrid.Columns["Pause"].Visible&&compactGrid.Columns["Mod"].AutoSizeMode==DataGridViewAutoSizeColumnMode.Fill&&compactGrid.Columns["Progress"].Visible,"Lista sem colunas Pacote coop, Observacoes e botoes preserva progresso e usa espaco para o nome do mod");
            check(f.Window.Text==""&&hasHeading&&hasCredits,"Barra sem titulo, nome no cabecalho principal e credito solicitado sem emoji");
            Point refreshPoint=f.Window.PointToClient(refreshButton.PointToScreen(Point.Empty)),installPoint=f.Window.PointToClient(installButton.PointToScreen(Point.Empty));
            check(refreshButton.Parent==installButton.Parent&&refreshPoint.Y>compactGrid.Bottom&&refreshPoint.X<installPoint.X&&installPoint.X>f.Window.ClientSize.Width/2,"Atualizar lista e Baixar e instalar ficam juntos no canto inferior direito");
            var gameBox=(TextBox)Get(f.Window,"game");check(gameBox.Parent==chooseGame.Parent&&gameBox.Height==chooseGame.Height&&gameBox.Top==chooseGame.Top,"Escolher jogo e campo de caminho possuem mesma altura e alinhamento vertical: "+gameBox.Bounds+" / "+chooseGame.Bounds);
            string launched=null;Set(f.Window,"launchBrowser",new Action<string>(url=>{launched=url;}));
            check(f.OpenNormal()==null&&launched==Url&&f.Browser.Calls.Count==0,"OpenOfficial usa navegador normal sem criar sessao CDP ou aba controlada");
            Call(f.Window,"StartDownloadWatch",f.Mod,f.Asset,f.Game,false);((OnlineDownloadWatch)Get(f.Window,"downloadWatch")).Poll(DateTime.UtcNow.AddSeconds(-3));f.Check();
            check(f.Cache.Contains(f.Asset)&&f.Browser.Calls.Count==0&&!Directory.Exists(f.Game),"Sessao normal continua importacao automatica sem tocar abas ou jogo");
            var grid=(DataGridView)Get(f.Window,"grid");check(Convert.ToString(grid.Rows[0].Cells["Notes"].Value).Contains("Guia mantida"),"Interface explica que guia normal fica aberta apos importacao");
        }
        using(var f=new Fixture(Path.Combine(root,"header-selection"),false)) {
            var grid=(DataGridView)Get(f.Window,"grid");var header=(CheckBox)Get(f.Window,"selectAllHeader");var summary=(Label)Get(f.Window,"summary");
            check(header.Parent==grid&&new Rectangle(1,1,grid.Columns[0].Width,grid.ColumnHeadersHeight).Contains(header.Bounds)&&!header.Checked,"Selecionar todos e um checkbox acessivel dentro da primeira celula do cabecalho");
            int unknown=grid.Rows.Add(false,"Unknown","—","Não instalado","Revisão necessária",0,"—","","Não analisado");grid.Rows[unknown].Tag="pending:fixture";grid.Rows[unknown].ReadOnly=true;
            typeof(CheckBox).GetMethod("OnClick",Private).Invoke(header,new object[]{EventArgs.Empty});
            check(Convert.ToBoolean(grid.Rows[0].Cells["Select"].Value)&&!Convert.ToBoolean(grid.Rows[unknown].Cells["Select"].Value),"Checkbox marca mods conhecidos e preserva importado sem auditoria desmarcado");
            typeof(CheckBox).GetMethod("OnClick",Private).Invoke(header,new object[]{EventArgs.Empty});
            check(!Convert.ToBoolean(grid.Rows[0].Cells["Select"].Value)&&!header.Checked,"Segundo clique no cabecalho desmarca todos");
            grid.Rows[0].Cells["Installed"].Value="Instalado correto";grid.Rows[unknown].Cells["State"].Value="Falhou";
            check(summary.Text=="Mods na lista: 2   |   Instalados: 1   |   Falhas: 1","Contadores refletem total, instalados e falhas quando os estados mudam");
            grid.Rows.RemoveAt(unknown);check(summary.Text=="Mods na lista: 1   |   Instalados: 1   |   Falhas: 0","Remocao de linha atualiza contadores e descarta falha removida");
        }
        using(var f=new Fixture(Path.Combine(root,"mod-details"),false)) {
            var absent=ModDetailsInfo.Read(f.Mod,f.Game,"Nota preservada");
            check(absent.InstalledVersion=="Não instalado"&&absent.Files[0][1]=="Ausente"&&absent.OfficialUrl=="https://www.nexusmods.com/cyberpunk2077/mods/123","Detalhes de mod ausente mostram destino e pagina oficial sem afirmar instalacao");
            string path=Safe.In(f.Game,f.Mod.Files[0].Path);Directory.CreateDirectory(Path.GetDirectoryName(path));File.Copy(f.Source,path);
            var installed=ModDetailsInfo.Read(f.Mod,f.Game,"Nota preservada");
            check(installed.Name==f.Mod.Name&&installed.InstalledVersion==f.Mod.Version&&installed.PackageVersion==f.Mod.Version&&installed.Files[0][0]==path&&installed.Files[0][1]=="Presente","Detalhes identificam versao instalada por hashes e mostram caminho completo do arquivo");
            string opened=null;
            using(var details=new ModDetailsWindow(installed,url=>opened=url)){
                details.ShowInTaskbar=false;details.StartPosition=FormStartPosition.Manual;details.Location=new Point(-30000,-30000);details.Show();Application.DoEvents();
                check(opened==null&&((TextBox)details.Controls.Find("Detail0",true)[0]).ReadOnly&&((TextBox)details.Controls.Find("Detail3",true)[0]).Text==f.Game&&((DataGridView)details.Controls.Find("ModFiles",true)[0]).Rows.Count==1,"Janela de detalhes tem dados copiaveis e lista de arquivos sem abrir navegador automaticamente");
                using(var bmp=new Bitmap(details.Width,details.Height)){details.DrawToBitmap(bmp,new Rectangle(0,0,details.Width,details.Height));bmp.Save(Path.Combine(root,"previa-detalhes.png"),System.Drawing.Imaging.ImageFormat.Png);}
                ((Button)details.Controls.Find("OpenWeb",true)[0]).PerformClick();check(opened==installed.OfficialUrl,"Abrir na web envia somente pagina oficial ao launcher normal do aplicativo");details.Close();
            }
            File.WriteAllText(path,"alterado");var changed=ModDetailsInfo.Read(f.Mod,f.Game,"");
            check(changed.State=="Diferente/alterado"&&changed.InstalledVersion!=f.Mod.Version&&changed.InstalledVersion.StartsWith("Não identificada"),"Arquivos modificados nao recebem versao instalada presumida");
            var noRoot=ModDetailsInfo.Read(f.Mod,"","");check(noRoot.InstalledVersion=="Não verificada"&&noRoot.Files[0][0]==f.Mod.Files[0].Path&&noRoot.Files[0][1]=="Destino previsto","Sem pasta escolhida detalhes mostram somente destino previsto");
            var live=FixedData.Load();
            check(ModDetailsInfo.OfficialPage(live.Packages.Single(m=>m.Name=="CET"))=="https://github.com/maximegmd/CyberEngineTweaks/releases/tag/v1.38.1"&&ModDetailsInfo.OfficialPage(live.Packages.Single(m=>m.NexusModId==31886))=="https://www.nexusmods.com/cyberpunk2077/mods/31886","Links de detalhes usam pagina da release GitHub e alias Nexus sem disparar download direto");
            bool refused=false;try{ModDetailsInfo.OfficialPage(new FixedMod{Assets=new List<FixedAsset>{new FixedAsset{Url="file:///C:/Windows/System32/cmd.exe"}}});}catch(IOException){refused=true;}check(refused,"Detalhes recusam link local executavel em vez de abrir no sistema");
            var nca=live.Packages.Single(m=>m.Id==NightCityDensity.ModId);check(ModDetailsInfo.Read(nca,"","").Files.Count==nca.DensityProfiles.Single(p=>p.Id=="low").Files.Count,"Sem instalacao detalhes da densidade usam arquivos da Baixa padrao");
            check(File.ReadAllText(path)=="alterado"&&!Directory.Exists(Safe.In(f.Game,".CoopModsOnline"))&&f.Browser.Calls.Count==0,"Conferir e abrir detalhes preserva arquivos sem instalar ou criar recibos");
        }
        using(var f=new Fixture(Path.Combine(root,"startup-scan"),false)) {
            string exe=Safe.In(f.Game,"bin/x64/Cyberpunk2077.exe");Directory.CreateDirectory(Path.GetDirectoryName(exe));File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","FixtureGame.exe"),exe);
            Set(f.Window,"findGames",new Func<List<string>>(()=>new List<string>{f.Game}));((TextBox)Get(f.Window,"game")).Text="";
            Pump((Task)Call(f.Window,"InitializeGame"));Application.DoEvents();var grid=(DataGridView)Get(f.Window,"grid");
            check(((TextBox)Get(f.Window,"game")).Text==f.Game&&Convert.ToString(grid.Rows[0].Cells["Installed"].Value)=="Não instalado"&&Convert.ToString(grid.Rows[0].Cells["State"].Value)=="A baixar","Abertura localiza jogo e faz primeira varredura marcando mod ausente automaticamente");
            string installed=Safe.In(f.Game,f.Mod.Files[0].Path);Directory.CreateDirectory(Path.GetDirectoryName(installed));File.Copy(f.Source,installed);
            Pump((Task)Call(f.Window,"Run",new Func<string>(()=>(string)Call(f.Window,"RefreshAll"))));Application.DoEvents();
            check(Convert.ToString(grid.Rows[0].Cells["Installed"].Value)=="Instalado correto"&&Safe.Hash(installed)==f.Asset.Sha256&&!Directory.Exists(Safe.In(f.Game,".CoopModsOnline"))&&f.Browser.Calls.Count==0,"Atualizar lista reconhece mod adicionado depois sem instalar, abrir navegador ou criar recibos");
        }
        using(var f=new Fixture(Path.Combine(root,"success"),true)) {
            check(!((System.Windows.Forms.Timer)Get(f.Window,"downloadWatchTimer")).Enabled,"Preview de teste nao inicia timer ou descoberta de jogo");
            check(f.Start()&&!f.Cache.Contains(f.Asset),"Download autorizado armado fora do jogo e cache inicialmente vazio");
            f.Check();
            check(f.Cache.Contains(f.Asset),"Caminho real da interface importa download concluido sem arrastar arquivo");
            check(File.Exists(f.Downloaded)&&Safe.Hash(f.Downloaded)==f.Asset.Sha256&&Safe.Hash(f.Source)==f.Asset.Sha256,"Importacao automatica preserva os dois arquivos originais");
            check(f.Browser.Closes==1&&f.Browser.Calls.Last()=="GET /json/close/OwnedTab"&&!f.Browser.Calls.Any(c=>c.Contains("/json/close/OtherTab")),"Somente aba exata criada pelo aplicativo e fechada apos importacao");
            check(Get(f.Window,"downloadWatch")==null&&((HashSet<string>)Get(f.Window,"automaticQueue")).Count==0,"Acao apenas baixar encerra monitor sem autorizar instalacao");
            check(!Directory.Exists(f.Game)&&!Directory.Exists(Path.Combine(f.Root,".CoopModsOnline")),"Fluxo baixar/importar nao cria nem instala arquivos no jogo");
            f.Check();check(f.Browser.Closes==1,"Novo tick depois da conclusao nao importa nem fecha outra aba");
        }
        using(var f=new Fixture(Path.Combine(root,"navigated"),true)) {
            check(f.Start(),"Segundo download autorizado pode usar outra sessao isolada");f.Browser.Current="https://example.org/keep-this-tab";f.Check();
            check(f.Cache.Contains(f.Asset)&&f.Browser.Closes==0,"Download e importado mas aba navegada para outro site e preservada");
        }
        using(var f=new Fixture(Path.Combine(root,"same-mod"),true)) {
            f.Start();f.Browser.Current="https://www.nexusmods.com/games/cyberpunk2077/mods/123?tab=files";f.Check();
            check(f.Cache.Contains(f.Asset)&&f.Browser.Closes==1,"Aba propria no mesmo mod apos retirar file_id e mudar caminho ainda fecha depois da importacao");
        }
        using(var f=new Fixture(Path.Combine(root,"close-failed"),true)) {
            f.Start();f.Browser.FailClose=true;f.Check();
            check(f.Cache.Contains(f.Asset)&&Get(f.Window,"downloadWatch")==null,"Falha de controle da aba nao perde download importado");
            var grid=(DataGridView)Get(f.Window,"grid");
            check(!String.IsNullOrWhiteSpace(Convert.ToString(grid.Rows[0].Cells["Notes"].Value))&&Directory.GetFiles(Path.Combine(f.Root,"Logs"),"*.jsonl").Length==1,"Motivo de nao fechar permanece nas Observacoes e no log proprio do app");
        }
        using(var f=new Fixture(Path.Combine(root,"cancelled"),true)) {
            f.Start();Call(f.Window,"ClearAutomaticQueue");f.Check();
            check(Get(f.Window,"downloadWatch")==null&&!f.Cache.Contains(f.Asset),"Cancelar fila desarma monitor e impede importacao posterior");
            check(f.Browser.Closes==0&&File.Exists(f.Downloaded)&&!Directory.Exists(f.Game),"Cancelar preserva aba e download e nao toca jogo");
        }
        using(var f=new Fixture(Path.Combine(root,"path-change"),true)) {
            f.Start();((TextBox)Get(f.Window,"game")).Text=Path.Combine(f.Root,"outro-jogo");f.Check();
            check(Get(f.Window,"downloadWatch")==null&&!f.Cache.Contains(f.Asset)&&f.Browser.Closes==0,"Trocar pasta invalida espera mesmo sem fila de instalacao");
        }
        using(var f=new Fixture(Path.Combine(root,"wrong-file"),false)) {
            File.WriteAllText(f.Downloaded,new string('x',(int)f.Asset.Size),new UTF8Encoding(false));f.Start();f.Check();
            check(!f.Cache.Contains(f.Asset)&&f.Browser.Closes==0&&Get(f.Window,"downloadWatch")!=null,"Arquivo desconhecido de mesmo tamanho nao importa nem fecha aba aguardada");
            check(!File.Exists(Path.Combine(f.Root,"importados-pendentes.json"))&&!Directory.Exists(f.Game),"Monitor ignora desconhecido sem criar fila de auditoria ou alterar jogo");
        }
        using(var f=new Fixture(Path.Combine(root,"log-download-only"),true)) {
            OnlineDiagnostics.Start();
            try{
                string partial=Path.Combine(f.Downloads,f.Asset.Name+".crdownload");File.WriteAllText(partial,"partial");File.WriteAllText(Path.Combine(f.Downloads,"other-private-download.part"),"private");File.SetLastWriteTimeUtc(f.Downloaded,DateTime.UtcNow.AddMinutes(-5));
                ((DataGridView)Get(f.Window,"grid")).Rows[0].Cells["Select"].Value=true;Set(f.Window,"launchBrowser",new Action<string>(url=>{}));f.OpenNormal();f.Start();f.Check();Call(f.Window,"RefreshMods",new[]{f.Mod},f.Game);Application.DoEvents();
                var events=OnlineDiagnostics.Snapshot();var grid=(DataGridView)Get(f.Window,"grid");
                check(events.Any(e=>Convert.ToString(e["Event"])=="watch.file.observed"&&Convert.ToBoolean(((Dictionary<string,object>)e["Data"])["Partial"]))&&File.Exists(partial),"Log observa arquivo parcial do mod enquanto navegador ainda baixa sem ler conteudo ou apagar parcial");
                check(events.Any(e=>Convert.ToString(e["Event"])=="watch.verified"&&Convert.ToBoolean(((Dictionary<string,object>)e["Data"])["ExistedBeforeWatch"])),"Registro identifica pacote completo antigo enquanto outra copia esta sendo baixada");
                check(f.Cache.Contains(f.Asset)&&Convert.ToString(grid.Rows[0].Cells["Installed"].Value)=="Não instalado"&&!Directory.Exists(f.Game),"Concluir apenas Baixar importa cache e nunca marca como instalado sem arquivos no jogo");
                check(events.Any(e=>Convert.ToString(e["Event"])=="ui.selection.changed")&&events.Any(e=>Convert.ToString(e["Event"])=="browser.open.dispatched")&&events.Any(e=>Convert.ToString(e["Event"])=="ui.row.changed")&&!events.Any(e=>Convert.ToString(e["Event"])=="install.request")&&!OnlineDiagnostics.Report(false).Contains("other-private-download"),"Linha do tempo registra selecao, navegador e tela sem acionar instalacao nem listar downloads pessoais alheios");
            }finally{OnlineDiagnostics.Stop();}
        }
        using(var f=new Fixture(Path.Combine(root,"online-testes-"+Guid.NewGuid().ToString("N")),false)) {
            var red=((List<Control>)Get(f.Window,"actions")).OfType<Button>().Single(b=>b.Text=="Remover selecionados");check(red.BackColor==Color.Firebrick&&red.ForeColor==Color.White&&red.Parent is FlowLayoutPanel,"Remover selecionados e botao vermelho na area inferior de acoes");
            string path=Safe.In(f.Game,f.Mod.Files[0].Path);Directory.CreateDirectory(Path.GetDirectoryName(path));File.Copy(f.Source,path);File.WriteAllText(Path.Combine(f.Game,".fixture-only"),"test");
            var catalog=(FixedCatalog)Get(f.Window,"catalog");var engine=new FixedInstaller(catalog,f.Cache,f.Game,null,true);Set(f.Window,"createRemovalInstaller",new Func<string,FixedInstaller>(game=>engine));
            var grid=(DataGridView)Get(f.Window,"grid");grid.Rows[0].Cells["Select"].Value=true;Call(f.Window,"AuthorizeAutomatic",new[]{f.Mod.Id},f.Game,true);
            Set(f.Window,"confirmRemoval",new Func<string,bool>(text=>false));Pump((Task)Call(f.Window,"RemoveSelected"));check(File.Exists(path)&&((HashSet<string>)Get(f.Window,"automaticQueue")).Count==1,"Cancelar confirmacao de remover preserva arquivos e autorizacao anterior");
            Set(f.Window,"confirmRemoval",new Func<string,bool>(text=>true));Pump((Task)Call(f.Window,"RemoveSelected"));
            check(!File.Exists(path)&&((HashSet<string>)Get(f.Window,"automaticQueue")).Count==0&&Convert.ToString(grid.Rows[0].Cells["Installed"].Value)=="Não instalado"&&!Convert.ToBoolean(grid.Rows[0].Cells["Select"].Value),"Remocao pela interface cancela fila antiga e atualiza instalacao e selecao apos quarentena");
            engine.Undo(s=>{});check(File.Exists(path)&&Safe.Hash(path)==f.Asset.Sha256,"Desfazer restaura arquivo removido pelo novo botao");
        }
        using(var f=new Fixture(Path.Combine(root,"files-only-drop"),false)) {
            var buttons=((List<Control>)Get(f.Window,"actions")).OfType<Button>().ToList();check(!buttons.Any(b=>b.Text.Contains("Importar"))&&typeof(OnlineWindow).GetMethod("ImportDialog",Private)==null&&!f.Window.KeyPreview,"Importacao manual usa so arrastar arquivos, sem dialogo de link ou colar global");
            var grid=(DataGridView)Get(f.Window,"grid");var data=new DataObject(DataFormats.UnicodeText,"https://www.nexusmods.com/cyberpunk2077/mods/9083?tab=files");var enter=new DragEventArgs(data,0,0,0,DragDropEffects.Copy,DragDropEffects.None);
            typeof(Control).GetMethod("OnDragEnter",Private).Invoke(grid,new object[]{enter});check(enter.Effect==DragDropEffects.None,"Arrastar link para a lista nao sinaliza importacao aceita");
            typeof(Control).GetMethod("OnDragDrop",Private).Invoke(grid,new object[]{enter});Application.DoEvents();check(grid.Rows.Count==1&&!File.Exists(Path.Combine(f.Root,"importados-pendentes.json"))&&!Directory.Exists(f.Game),"Soltar link nao cria fila, nao abre browser e nao instala");
            var oldLink=new ModImportItem{Id="nexus-9083-0",Kind="Link",Name="Nexus mod9083",ModId=9083,Source="https://www.nexusmods.com/cyberpunk2077/mods/9083",State="Pendente de auditoria"};string queue=Path.Combine(f.Root,"importados-pendentes.json");Safe.WriteJson(queue,new[]{oldLink});string before=Safe.Hash(queue);Call(f.Window,"LoadPending");Application.DoEvents();check(grid.Rows.Count==1&&Safe.Hash(queue)==before,"Link legado sai da interface sem apagar seu registro historico");
            string package=Path.Combine(f.Root,"novo-mod.zip");using(var archive=System.IO.Compression.ZipFile.Open(package,System.IO.Compression.ZipArchiveMode.Create)){var entry=archive.CreateEntry("archive/pc/mod/new.archive");using(var writer=new StreamWriter(entry.Open()))writer.Write("fixture, not executable");}string original=Safe.Hash(package);
            var fileData=new DataObject(DataFormats.FileDrop,new[]{package});var fileEnter=new DragEventArgs(fileData,0,0,0,DragDropEffects.Copy,DragDropEffects.None);typeof(Control).GetMethod("OnDragEnter",Private).Invoke(f.Window,new object[]{fileEnter});check(fileEnter.Effect==DragDropEffects.Copy,"Arquivo arrastado e aceito na area geral da janela");
            typeof(Control).GetMethod("OnDragDrop",Private).Invoke(f.Window,new object[]{fileEnter});var timer=Stopwatch.StartNew();while((bool)Get(f.Window,"busy")||grid.Rows.Count<2){if(timer.Elapsed>TimeSpan.FromSeconds(10))throw new Exception("Drop de arquivo excedeu dez segundos");Application.DoEvents();Thread.Sleep(5);}Application.DoEvents();
            check(grid.Rows.Count==2&&Convert.ToString(grid.Rows[1].Cells["Mod"].Value)==Path.GetFileName(package)&&Convert.ToString(grid.Rows[1].Cells["Installed"].Value)=="Não registrado"&&!grid.Rows[1].ReadOnly,"ZIP escolhido pelo usuário entra selecionável sem inventar versão instalada");
            check(File.Exists(package)&&Safe.Hash(package)==original&&!Directory.Exists(f.Game)&&f.Browser.Calls.Count==0&&((HashSet<string>)Get(f.Window,"automaticQueue")).Count==0,"Arquivo novo e preservado sem extrair, instalar ou autorizar pacote desconhecido");
            var persisted=Safe.Json.Deserialize<List<ModImportItem>>(File.ReadAllText(queue));check(persisted.Count==2&&persisted.Any(p=>p.Kind=="Link")&&((ModImporter)Get(f.Window,"importer")).ReadPending().Count==1,"Registro legado permanece junto do arquivo novo enquanto so arquivos ficam visiveis");
        }
        ModAuditTests.Run(parent);
        Safe.WriteJson(Path.Combine(root,"resultado.json"),new{Passed=passed.Count,Tests=passed,UIOffscreen=true,RealImporter=true,MockBrowserOnly=true,BrowserLaunched=false,NetworkUsed=false,RealGameTouched=false,InstallerInvoked=false});File.WriteAllText(Path.Combine(parent,"ultimo-teste-window-watch.txt"),root);
    }
}
