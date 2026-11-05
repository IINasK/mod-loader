using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

public static class OnlineGameEditionTests {
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static object Get(object o,string name){return o.GetType().GetField(name,Private).GetValue(o);}
    static object Call(object o,string name,params object[] args){try{return o.GetType().GetMethod(name,Private).Invoke(o,args);}catch(TargetInvocationException ex){throw ex.InnerException??ex;}}
    static void Refuse(Action action,Action<bool,string> check,string name){bool refused=false;try{action();}catch(IOException){refused=true;}check(refused,name);}
    public static void Run(string parent){
        string root=Path.Combine(Path.GetFullPath(parent),"online-testes-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var passed=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("TESTE PERFIL DLC FALHOU: "+name);passed.Add(name);};
        FixedAsset asset;var catalog=OnlineTests.MakeCatalog(root,out asset);var basis=catalog.Packages[0];var extra=catalog.Packages[1];
        check(GameEdition.NeedsPL(extra),"Fixture antiga sem metadados mantém RequiresPL=true");
        foreach(var mod in catalog.Packages){mod.RequiresPL=false;mod.PhantomLibertyStatus="not-required";}
        FixedData.Validate(catalog);check(GameEdition.Reason(catalog,new[]{extra.Id})==null,"Fechamento sem dependência DLC permite perfil base");
        basis.PhantomLibertyStatus="required";check(GameEdition.Reason(catalog,new[]{extra.Id}).Contains(basis.Name),"Dependência DLC oculta complemento mesmo se ele não exige DLC diretamente");
        basis.PhantomLibertyStatus="unverified";check(GameEdition.Reason(catalog,new[]{extra.Id}).Contains("não foi documentado"),"Dependência sem evidência sem DLC também não entra no perfil base");
        basis.PhantomLibertyStatus="not-required";
        string game=OnlineTests.Fixture(root,"game");check(GameEdition.HasPL(game),"Reconhece marcador original da expansão");File.Delete(Safe.In(game,GameEdition.Marker));check(!GameEdition.HasPL(game),"Não confunde executável ou diretório vazio com expansão instalada");
        var cache=new DownloadCache(Path.Combine(root,"cache"));extra.PhantomLibertyStatus="unverified";
        string[] before=Directory.GetFiles(game,"*",SearchOption.AllDirectories);var engine=new FixedInstaller(catalog,cache,game,null,true);
        Refuse(()=>engine.Install(new[]{extra.Id},true,s=>{}),check,"Motor recusa componente não documentado sem DLC antes de staging, recibo ou cópia");
        Refuse(()=>AutoInstallPlan.Build(catalog,new[]{extra.Id},game,cache,false),check,"Plano da fila original recusa sem DLC antes de baixar");
        check(before.SequenceEqual(Directory.GetFiles(game,"*",SearchOption.AllDirectories))&&!Directory.Exists(Path.Combine(game,".CoopModsOnline"))&&!Directory.Exists(cache.Root),"Recusa preserva jogo e não cria cache, staging ou recibos");
        extra.PhantomLibertyStatus="not-required";FixedData.ValidateGame(game,catalog.Packages,catalog.GameFileVersion);check(true,"Receitas comprovadas sem DLC passam preflight no executável base");
        extra.PhantomLibertyStatus="required";
        int browserCalls=0;
        using(var window=new OnlineWindow(catalog,cache,true)){
            var edition=(ComboBox)Get(window,"gameEdition");var grid=(DataGridView)Get(window,"grid");var queue=(HashSet<string>)Get(window,"automaticQueue");
            window.ShowInTaskbar=false;window.StartPosition=FormStartPosition.Manual;window.Location=new System.Drawing.Point(-30000,-30000);window.Show();Application.DoEvents();
            ((TextBox)Get(window,"game")).Text=game;check(edition.SelectedIndex==0,"Preview legado permanece Com PL mesmo ao selecionar fixture sem marcador");
            typeof(OnlineWindow).GetField("launchBrowser",Private).SetValue(window,new Action<string>(url=>browserCalls++));
            Call(window,"AuthorizeAutomatic",new[]{extra.Id},game,true);int generation=(int)Get(window,"automaticGeneration");
            edition.SelectedIndex=1;Application.DoEvents();
            check(queue.Count==0&&(int)Get(window,"automaticGeneration")>generation&&Get(window,"downloadWatch")==null,"Troca de perfil cancela autorização e monitor da fila original");
            check(grid.Rows.Cast<DataGridViewRow>().Count(row=>row.Visible)==1&&!grid.Rows.Cast<DataGridViewRow>().Single(row=>Convert.ToString(row.Tag)==extra.Id).Visible,"Perfil sem DLC mostra somente fechamento elegível");
            ((CheckBox)Get(window,"selectAllHeader")).Checked=true;Call(window,"SelectAll");
            check(((List<string>)Call(window,"Chosen")).SequenceEqual(new[]{basis.Id}),"Checkbox do cabeçalho seleciona somente linhas visíveis e elegíveis");
            check(((Label)Get(window,"summary")).Text.Contains("Mods na lista: 1"),"Contadores consideram somente perfil visível");
            Refuse(()=>Call(window,"AuthorizeAutomatic",new[]{extra.Id},game,false),check,"Importação conhecida não pode autorizar fila incompatível pelo caminho automático");
            var task=(Task)Call(window,"DownloadSelection",new List<string>{extra.Id});task.GetAwaiter().GetResult();Call(window,"Install",new List<string>{extra.Id},true);
            check(browserCalls==0&&queue.Count==0&&!Directory.Exists(Path.Combine(game,".CoopModsOnline")),"Ações diretas incompatíveis não abrem navegador nem instalam");
            check(((Label)Get(window,"editionNotice")).Text.Contains("saves")&&((Label)Get(window,"editionNotice")).Text.Contains("não remove"),"Perfil informa limite dos saves e preservação dos mods instalados");
            File.WriteAllText(Safe.In(game,GameEdition.Marker),"");check(GameEdition.HasPL(game),"Marcador restaurado apenas na fixture");
            Refuse(()=>Call(window,"AuthorizeAutomatic",new[]{extra.Id},game,false),check,"Perfil Sem PL continua restrito mesmo quando a expansão existe fisicamente");
            typeof(OnlineWindow).GetField("preview",Private).SetValue(window,false);Call(window,"AutoDetectEdition");check(edition.SelectedIndex==0,"Detecção real seleciona Com PL pelo marcador original");
            File.Delete(Safe.In(game,GameEdition.Marker));Call(window,"AutoDetectEdition");check(edition.SelectedIndex==1,"Troca real de caminho/detecção seleciona Sem PL quando marcador está ausente");typeof(OnlineWindow).GetField("preview",Private).SetValue(window,true);
            basis.PhantomLibertyStatus="required";Call(window,"ApplyGameEdition");Call(window,"ImportInputs",new object[]{new[]{Path.Combine(root,"fixture.zip")}});
            var timeout=Stopwatch.StartNew();while((bool)Get(window,"busy")||(bool)Get(window,"automaticFlow")){if(timeout.ElapsedMilliseconds>10000)throw new Exception("Importação fixture excedeu limite.");Application.DoEvents();Thread.Sleep(5);}Application.DoEvents();
            check(cache.Contains(asset)&&queue.Count==0&&browserCalls==0&&!Directory.Exists(Path.Combine(game,".CoopModsOnline")),"Arquivo conhecido importado em perfil incompatível fica no cache e não autoriza nem instala automaticamente");basis.PhantomLibertyStatus="not-required";
            edition.SelectedIndex=0;Application.DoEvents();check(grid.Rows.Cast<DataGridViewRow>().All(row=>row.Visible),"Voltar ao perfil Com PL restaura a lista sem reconstruir receitas");
            extra.ManualSelection=true;
            var header=(CheckBox)Get(window,"selectAllHeader");
            header.Checked=true;Call(window,"SelectAll");
            check(((List<string>)Call(window,"Chosen")).SequenceEqual(new[]{basis.Id}),"Seleção em massa pula mod experimental");
            grid.Rows.Cast<DataGridViewRow>().Single(row=>Convert.ToString(row.Tag)==extra.Id).Cells["Select"].Value=true;
            check(((List<string>)Call(window,"Chosen")).Contains(extra.Id),"Mod experimental pode ser marcado individualmente");
            var failure=new ModPreparationException(extra,asset,extra.Files[0].Entry,new IOException("Esperado: 10 bytes; encontrado: 12 bytes."));Call(window,"ShowPreparationFailure",failure);var failed=grid.Rows.Cast<DataGridViewRow>().Single(row=>Convert.ToString(row.Tag)==extra.Id);
            check(Convert.ToString(failed.Cells["State"].Value)=="Falhou"&&failed.Cells["State"].ToolTipText.Contains(asset.Name)&&failed.Selected,"Falha na preparação destaca o mod exato e guarda mensagem completa na linha");
            check(((Label)Get(window,"summary")).Text.Contains("Falhas: 1"),"Contador de falhas inclui erro de preparação identificado");window.Close();
        }
        extra.PhantomLibertyStatus="invalido";Refuse(()=>FixedData.Validate(catalog),check,"Classificação de expansão inválida recusada no catálogo");
        Safe.WriteJson(Path.Combine(root,"resultado-perfis-dlc.json"),new{Passed=passed.Count,Tests=passed,FixtureOnly=true,RealGameTouched=false});File.WriteAllText(Path.Combine(parent,"ultimo-teste-perfis-dlc.txt"),root);
    }

    internal static void CheckCategories(string parent) {
        var passed=new List<string>();
        Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("TESTE CATEGORIAS FALHOU: "+name);passed.Add(name);};
        string root=Path.Combine(Path.GetFullPath(parent),"categorias-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var cache=new DownloadCache(Path.Combine(root,"cache"));
        var catalog=FixedData.Load();
        check(catalog.Packages.All(m=>ModCategories.CategoryOf(m.Id,m.Profile)!=ModCategory.Other),"Todos os mods da versão atual têm categoria");
        using(var window=new OnlineWindow(catalog,cache,true)) {
            var grid=(DataGridView)Get(window,"grid");
            var category=(ComboBox)Get(window,"categoryFilter");
            var clothes=(ComboBox)Get(window,"clothingFilter");
            var edition=(ComboBox)Get(window,"gameEdition");
            var header=(CheckBox)Get(window,"selectAllHeader");
            var queue=(HashSet<string>)Get(window,"automaticQueue");
            Func<HashSet<string>> visible=()=>new HashSet<string>(grid.Rows.Cast<DataGridViewRow>().Where(row=>row.Visible).Select(row=>(string)row.Tag));
            window.ShowInTaskbar=false;
            window.StartPosition=FormStartPosition.Manual;
            window.Location=new System.Drawing.Point(-30000,-30000);
            window.Show();
            Application.DoEvents();
            int all=visible().Count;
            check(all==catalog.Packages.Count(m=>String.IsNullOrWhiteSpace(m.BlockedReason))&&!clothes.Enabled,"Filtro padrão mostra o catálogo completo e desabilita o filtro de roupas");
            category.SelectedIndex=(int)ModCategory.Core;
            check(visible().Contains("CET")&&!visible().Contains("NightCoop"),"Núcleo separa frameworks do kit principal");
            category.SelectedIndex=(int)ModCategory.Stores;
            check(visible().Contains("mod-4454")&&visible().Contains("mod-13870")&&!visible().Contains("mod-16345"),"Lojas separadas dos conjuntos e dos veículos");
            category.SelectedIndex=(int)ModCategory.Clothes;
            clothes.SelectedIndex=(int)ClothingFit.Feminine;
            check(clothes.Enabled&&visible().Contains("mod-7435")&&visible().Contains("mod-16345")&&!visible().Contains("mod-7640"),"Roupas femininas incluem itens para ambas as V");
            header.Checked=true;Call(window,"SelectAll");
            check(visible().SetEquals((List<string>)Call(window,"Chosen")),"Selecionar todos usa somente o filtro atual");
            clothes.SelectedIndex=(int)ClothingFit.Masculine;
            check(visible().Contains("mod-7640")&&visible().Contains("mod-16345")&&!visible().Contains("mod-7435")&&!((List<string>)Call(window,"Chosen")).Contains("mod-7435"),"Roupas masculinas excluem e desmarcam itens femininos ocultos");
            clothes.SelectedIndex=(int)ClothingFit.Both;
            check(visible().Contains("mod-14371")&&!visible().Contains("mod-7640")&&!visible().Contains("mod-7435"),"Ambas mostra apenas os itens com suporte aos dois tipos de V");
            queue.Add("mod-35848");
            category.SelectedIndex=(int)ModCategory.Vehicles;
            check(queue.Contains("mod-35848")&&visible().Contains("mod-14162")&&!clothes.Enabled,"Troca de categoria mantém a fila ativa");
            edition.SelectedIndex=1;
            check(category.SelectedIndex==(int)ModCategory.Vehicles&&visible().Contains("mod-35848")&&!visible().Contains("mod-14162")&&!visible().Contains("mod-13870"),"Categoria e perfil Phantom Liberty são combinados");
            edition.SelectedIndex=0;
            Call(window,"SelectFacialRows");
            check(category.SelectedIndex==(int)ModCategory.Appearance&&((List<string>)Call(window,"Chosen")).Count==14,"Atalho facial muda para Aparência e marca os 14 itens");
            category.SelectedIndex=(int)ModCategory.All;
            check(visible().Count==all,"Voltar para Todas restaura as linhas");
            window.Close();
        }
        Safe.WriteJson(Path.Combine(root,"resultado-categorias.json"),new{Passed=passed.Count,Tests=passed,FixtureOnly=true,RealGameTouched=false});
    }
}
