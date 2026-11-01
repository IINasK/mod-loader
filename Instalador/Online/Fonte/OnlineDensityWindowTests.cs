using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

public static class OnlineDensityWindowTests {
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    const BindingFlags StaticPrivate=BindingFlags.Static|BindingFlags.NonPublic;
    const string NcaId="22-NightCityAlive-NoREDMOD";
    sealed class Fixture:IDisposable {
        public readonly string Root,Game,Downloads;
        public readonly FixedCatalog Catalog;
        public readonly DownloadCache Cache;
        public readonly FixedMod Mod;
        public readonly OnlineWindow Window;
        public readonly List<string> Opened=new List<string>();
        public int InstallerCalls;
        public Fixture(string root,string profile) {
            Root=root;Directory.CreateDirectory(root);Downloads=Path.Combine(root,"d");Directory.CreateDirectory(Downloads);
            Cache=new DownloadCache(Path.Combine(root,"c"));
            Catalog=(FixedCatalog)typeof(OnlineDensityTests).GetMethod("Catalog",StaticPrivate).Invoke(null,new object[]{root,Cache});
            FixedData.Validate(Catalog);Mod=Catalog.Packages.Single(m=>m.Id==NcaId);
            var gameFixture=typeof(OnlineDensityTests).GetMethod("Game",StaticPrivate).Invoke(null,new object[]{root,"g",Catalog,Cache,profile});
            Game=(string)gameFixture.GetType().GetField("Game",BindingFlags.Instance|BindingFlags.Public).GetValue(gameFixture);
            Window=new OnlineWindow(Catalog,Cache,true);
            Set(Window,"downloadFolder",Downloads);
            Set(Window,"launchBrowser",new Action<string>(url=>Opened.Add(url)));
            Set(Window,"createDensityInstaller",new Func<string,FixedInstaller>(path=>{
                if(!String.Equals(Path.GetFullPath(path),Game,StringComparison.OrdinalIgnoreCase))throw new IOException("Fixture recusou destino externo.");
                Interlocked.Increment(ref InstallerCalls);
                return new FixedInstaller(Catalog,Cache,path,null,true);
            }));
            ((TextBox)Get(Window,"game")).Text=Game;
            Window.ShowInTaskbar=false;Window.StartPosition=FormStartPosition.Manual;Window.Location=new Point(-30000,-30000);Window.Show();Application.DoEvents();
        }
        public FixedMod Target(string id){return NightCityDensity.Resolve(Mod,id);}
        public FixedAsset Asset(string id){return Target(id).Assets.Single();}
        public void Forget(string id){string file=Cache.PathFor(Asset(id));if(File.Exists(file))File.Delete(file);}
        public string Download(string id){string file=Path.Combine(Downloads,"download-"+id+".zip");File.Copy(Path.Combine(Root,Asset(id).Name),file);return file;}
        public void Authorize(string id) {
            Call(Window,"ClearAutomaticQueue");
            Set(Window,"pendingDensityId",id);Set(Window,"pendingDensityRoot",Game);Set(Window,"pendingDensityGeneration",Get(Window,"automaticGeneration"));
        }
        public void PoisonNormalQueue(){((HashSet<string>)Get(Window,"automaticQueue")).Add("NotAuthorizedByDensity");}
        public void Arm(string id) {
            var target=Target(id);bool ok=(bool)Call(Window,"StartDownloadWatch",target,target.Assets.Single(),Game,false);
            if(!ok)throw new Exception("Monitor da fixture nao iniciou.");
            ((OnlineDownloadWatch)Get(Window,"downloadWatch")).Poll(DateTime.UtcNow.AddSeconds(-3));
        }
        public void Check(){var watch=(OnlineDownloadWatch)Get(Window,"downloadWatch");if(watch!=null&&watch.HasPendingCandidates)Pump(Task.Delay(2100));Pump((Task)Call(Window,"CheckWatchedDownload"));}
        public void Resume(){Pump((Task)Call(Window,"ContinueDensityProfile"));}
        public string Current {get{return NightCityDensity.Read(Mod,Game).Id;}}
        public string Status {get{return ((Label)Get(Window,"status")).Text;}}
        public DataGridView Grid {get{return (DataGridView)Get(Window,"grid");}}
        public List<OnlineReceipt> Receipts(){return (List<OnlineReceipt>)typeof(OnlineDensityTests).GetMethod("Receipts",StaticPrivate).Invoke(null,new object[]{Game});}
        public Dictionary<string,string> Snapshot(){return (Dictionary<string,string>)typeof(OnlineDensityTests).GetMethod("Snapshot",StaticPrivate).Invoke(null,new object[]{Game});}
        public bool Same(Dictionary<string,string> original){return (bool)typeof(OnlineDensityTests).GetMethod("Same",StaticPrivate).Invoke(null,new object[]{original,Game});}
        public void Dispose(){Call(Window,"ClearAutomaticQueue");Window.Close();Window.Dispose();Application.DoEvents();}
    }
    static object Get(object target,string name){var field=target.GetType().GetField(name,Private);if(field==null)throw new Exception("Campo UI ausente: "+name);return field.GetValue(target);}
    static void Set(object target,string name,object value){var field=target.GetType().GetField(name,Private);if(field==null)throw new Exception("Seam UI ausente: "+name);field.SetValue(target,value);}
    static object Call(object target,string name,params object[] args){try{return target.GetType().GetMethod(name,Private).Invoke(target,args);}catch(TargetInvocationException ex){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException??ex).Throw();throw;}}
    static void Pump(Task task){Wait(()=>task.IsCompleted,"Tarefa da UI");task.GetAwaiter().GetResult();Application.DoEvents();}
    static void Wait(Func<bool> complete,string name){var clock=Stopwatch.StartNew();while(!complete()){if(clock.Elapsed>TimeSpan.FromSeconds(10))throw new Exception(name+" excedeu dez segundos.");Application.DoEvents();Thread.Sleep(5);}Application.DoEvents();}
    static IEnumerable<Control> Descendants(Control control){foreach(Control child in control.Controls){yield return child;foreach(Control nested in Descendants(child))yield return nested;}}
    static bool IsIdle(OnlineWindow window){return !(bool)Get(window,"busy")&&!(bool)Get(window,"automaticFlow")&&!(bool)Get(window,"densityDialogOpen");}
    public static int Run(string parent) {
        if(Thread.CurrentThread.GetApartmentState()!=ApartmentState.STA)throw new Exception("Ensaios de densidade UI precisam de STA.");
        Exception failure=null;int count=0;var previous=SynchronizationContext.Current;
        try{
            using(var host=new Form{ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-30000,-30000),Size=new Size(1,1)}){
                host.Shown+=(sender,args)=>host.BeginInvoke(new Action(()=>{try{count=RunCases(parent);}catch(Exception ex){failure=ex;}finally{host.Close();}}));
                Application.Run(host);
            }
        }finally{SynchronizationContext.SetSynchronizationContext(previous);}
        if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return count;
    }
    static int RunCases(string parent) {
        string root=Path.Combine(Path.GetFullPath(parent),"online-testes-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var passed=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("TESTE DENSIDADE UI FALHOU: "+name);passed.Add(name);};
        using(var f=new Fixture(Path.Combine(root,"a"),"medium")) {
            f.Forget("high");string original=f.Download("high");var baseBefore=f.Catalog.Packages[0].Files.ToDictionary(x=>x.Path,x=>Safe.Hash(Safe.In(f.Game,x.Path)));
            f.Authorize("high");f.PoisonNormalQueue();f.Arm("high");f.Check();
            check(f.Current=="high","Monitor e importer reais aplicam HIGH solicitado, apesar de todas variantes terem o mesmo ID NCA");
            check(f.InstallerCalls==1&&f.Receipts().Count==1&&f.Receipts()[0].Operation=="density"&&f.Receipts()[0].DensityAfter=="high","Continuacao da UI invoca somente motor de perfil HIGH com recibo de densidade");
            check(((HashSet<string>)Get(f.Window,"automaticQueue")).Count==0&&Get(f.Window,"pendingDensityId")==null,"Perfil concluido limpa autorizacao propria e nao executa fila normal envenenada");
            check(baseBefore.All(x=>Safe.Hash(Safe.In(f.Game,x.Key))==x.Value),"Troca pela UI preserva cinco arquivos da base sintetica");
            check(File.Exists(original)&&Safe.Hash(original)==f.Asset("high").Sha256&&f.Opened.Count==0,"Importacao monitorada preserva original e nao abre navegador ou rede quando o arquivo ja chegou");
            check(f.Catalog.Packages.Single(m=>m.Id==NcaId).Assets.Single().FileId==164033,"Aplicar HIGH nao altera fonte Standard guardada internamente no catalogo");
        }
        using(var f=new Fixture(Path.Combine(root,"b"),"medium")) {
            f.Forget("high");f.Authorize("high");f.PoisonNormalQueue();
            Call(f.Window,"ImportInputs",new object[]{new[]{Path.Combine(f.Root,f.Asset("high").Name)}});
            Wait(()=>IsIdle(f.Window)&&Get(f.Window,"pendingDensityId")==null,"Importacao manual de perfil");
            check(f.Current=="high"&&f.InstallerCalls==1&&f.Receipts().Single().DensityAfter=="high","Arrastar/importar arquivo HIGH retoma somente a troca autorizada sem Atualizar ou confirmacao adicional");
            check(!f.Status.Contains("NotAuthorizedByDensity")&&f.Opened.Count==0,"Importacao HIGH nao encaminha NCA para fila normal ou densidade padrao");
        }
        using(var f=new Fixture(Path.Combine(root,"c"),"medium")) {
            f.Forget("high");f.Authorize("high");f.Arm("high");var before=f.Snapshot();
            Call(f.Window,"CancelDownload",f.Mod);f.Download("high");f.Check();
            check(f.Same(before)&&f.Current=="medium"&&f.InstallerCalls==0,"Cancelar antes do download impede aplicar perfil futuro");
            check(Get(f.Window,"pendingDensityId")==null&&Get(f.Window,"downloadWatch")==null&&!f.Cache.Contains(f.Asset("high")),"Cancelar invalida autorizacao de densidade e monitor sem importar arquivo posterior");
        }
        using(var f=new Fixture(Path.Combine(root,"d"),"medium")) {
            f.Forget("high");f.Authorize("high");f.Arm("high");var before=f.Snapshot();
            var path=(TextBox)Get(f.Window,"game");string other=Path.Combine(f.Root,"outro");path.Text=other;path.Text=f.Game;f.Download("high");f.Check();
            check(f.Same(before)&&f.InstallerCalls==0&&!Directory.Exists(other),"Trocar destino e voltar antes do download nao instala no destino antigo nem no novo");
            check(Get(f.Window,"pendingDensityId")==null&&Get(f.Window,"downloadWatch")==null&&!f.Cache.Contains(f.Asset("high")),"Mudanca de pasta invalida perfil pendente mesmo sem fila normal");
        }
        using(var f=new Fixture(Path.Combine(root,"e"),"medium")) {
            f.Forget("high");f.Download("high");var before=f.Snapshot();f.Arm("high");f.Check();
            check(f.Cache.Contains(f.Asset("high"))&&f.Current=="medium"&&f.Same(before)&&f.InstallerCalls==0,"Download-only de HIGH importa para cache sem autorizar troca de densidade");
        }
        using(var f=new Fixture(Path.Combine(root,"f"),null)) {
            f.Forget("low");var before=f.Snapshot();
            Pump((Task)Call(f.Window,"DownloadSelection",new List<string>{NcaId}));
            string expected="https://www.nexusmods.com/cyberpunk2077/mods/10395?tab=files&file_id=164034";
            check(f.Opened.Count==1&&f.Opened.Single()==expected&&((FixedAsset)Get(f.Window,"waitingAsset")).FileId==164034,"Primeiro download geral do NCA abre LOW fixado 164034, nao Standard ou arquivo mais recente");
            check(f.Same(before)&&f.InstallerCalls==0&&Get(f.Window,"pendingDensityId")==null,"Apenas baixar LOW ainda nao instala nem cria autorizacao de densidade");
            Call(f.Window,"ClearAutomaticQueue");f.Opened.Clear();Pump((Task)Call(f.Window,"OpenOfficial",f.Mod,(object)null));
            check(f.Opened.Single()==expected&&f.Catalog.Packages.Single(m=>m.Id==NcaId).Assets.Single().FileId==164033,"Download oficial sem escolha explicita usa Baixa sem reescrever catalogo historico");
        }
        string preview=Path.Combine(root,"dialogo-densidade-fixture.png");
        using(var f=new Fixture(Path.Combine(root,"p"),null)) {
            var before=f.Snapshot();bool captured=false;int selected=-1;string actual=null,apply=null;Exception captureFailure=null;
            using(var timer=new System.Windows.Forms.Timer{Interval=15}) {
                timer.Tick+=(sender,args)=>{
                    var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(x=>x.Text=="Densidade de NPCs · Night City Alive");if(dialog==null)return;
                    timer.Stop();
                    try{
                        dialog.StartPosition=FormStartPosition.Manual;dialog.Location=new Point(-30000,-30000);
                        selected=Descendants(dialog).OfType<ComboBox>().Single().SelectedIndex;
                        actual=Descendants(dialog).OfType<Label>().First(x=>x.Text.StartsWith("Versão:")).Text;
                        apply=Descendants(dialog).OfType<Button>().Single(x=>x.Text=="Aplicar e instalar").Text;
                        using(var bitmap=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(bitmap,new Rectangle(0,0,dialog.Width,dialog.Height));bitmap.Save(preview,System.Drawing.Imaging.ImageFormat.Png);}
                        captured=true;
                    }catch(Exception ex){captureFailure=ex;}
                    finally{dialog.DialogResult=DialogResult.Cancel;dialog.Close();}
                };
                timer.Start();Call(f.Window,"ChooseDensityProfile");Wait(()=>IsIdle(f.Window)&&(!timer.Enabled),"Previa do seletor de densidade");
            }
            if(captureFailure!=null)throw captureFailure;
            check(captured&&File.Exists(preview)&&selected==0&&actual.Contains("Não instalado")&&apply=="Aplicar e instalar","Dialogo exibe perfil atual e oferece Baixa recomendada por padrao para primeira instalacao");
            check(f.Same(before)&&f.InstallerCalls==0&&f.Opened.Count==0&&Get(f.Window,"pendingDensityId")==null,"Abrir e fechar seletor para previa nao autoriza mudanca, navegador ou instalacao");
        }
        Safe.WriteJson(Path.Combine(root,"resultado-density-window.json"),new{Passed=passed.Count,Tests=passed,UIOffscreen=true,RealImporter=true,FixtureInstaller=true,RealGameTouched=false,RealSavesRead=false,NetworkUsed=false,BrowserLaunched=false,Preview=preview});
        File.WriteAllText(Path.Combine(parent,"ultimo-teste-density-window.txt"),root);
        return passed.Count;
    }
}
