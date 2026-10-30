using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

public static class FacialMainListTests {
    // Historical preparation fixtures keep their original catalog; production faces now use the unified installer.
    internal static FixedCatalog PreparationCatalog(){var c=FixedData.Load();c.Packages.RemoveAll(m=>m.Id.StartsWith("face-",StringComparison.Ordinal)||m.Id=="RadioExt"||m.Id=="SpotOnRadio"||new[]{"mod-11963","mod-15606","mod-15867","mod-11964"}.Contains(m.Id));FixedData.Validate(c);return c;}
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static object Get(object o,string name){return o.GetType().GetField(name,Private).GetValue(o);}
    static void Set(object o,string name,object value){o.GetType().GetField(name,Private).SetValue(o,value);}
    static object Call(object o,string name,params object[] args){return o.GetType().GetMethod(name,Private).Invoke(o,args);}
    public static void Run(string parent){
        string root=Path.Combine(Path.GetFullPath(parent),"face-main-"+Guid.NewGuid().ToString("N").Substring(0,12));Directory.CreateDirectory(root);
        var passed=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("TESTE FACIAL MAIN FALHOU: "+name);passed.Add(name);};
        var catalog=PreparationCatalog();var cache=new DownloadCache(Path.Combine(root,"cache"));string game=Path.Combine(root,"jogo-nao-tocar");var calls=new List<string>();bool fail=false;
        using(var f=new OnlineWindow(catalog,cache,true)){
            Directory.CreateDirectory(Path.Combine(root,"downloads"));Set(f,"downloadFolder",Path.Combine(root,"downloads"));Set(f,"launchBrowser",new Action<string>(url=>{if(fail)throw new IOException("fixture");calls.Add(url);}));
            f.ShowInTaskbar=false;f.StartPosition=FormStartPosition.Manual;f.Location=new Point(-30000,-30000);f.Show();Application.DoEvents();
            var grid=(DataGridView)Get(f,"grid");var status=(Label)Get(f,"status");var header=(CheckBox)Get(f,"selectAllHeader");var primary=(Button)Get(f,"mainInstallButton");
            var faces=grid.Rows.Cast<DataGridViewRow>().Where(r=>Convert.ToString(r.Tag).StartsWith("face-")).ToList();
            check(grid.Rows.Count==69&&faces.Count==14&&faces.All(r=>Convert.ToString(r.Cells["Mod"].Value).StartsWith("Rosto · ")&&!r.Cells["Select"].ReadOnly),"Quatorze linhas faciais selecionaveis aparecem na lista principal junto dos55mods");
            check(faces.All(r=>Convert.ToString(r.Cells["Installed"].Value)=="Somente download"&&Convert.ToString(r.Cells["Progress"].Value)=="—")&&calls.Count==0,"Preparacoes sem porcentagem, falso instalado ou browser automatico");
            Call(f,"ChooseFacialDownloads");Application.DoEvents();var chosen=(List<string>)Call(f,"Chosen");
            check(chosen.Count==14&&chosen.All(id=>id.StartsWith("face-"))&&grid.CurrentRow==faces[0]&&primary.Text=="Baixar selecionados","Botao Conjunto facial destaca e seleciona as14linhas na propria tabela");
            check(((Label)Get(f,"installGuide")).Text.Contains("COMO BAIXAR O CONJUNTO FACIAL")&&((Label)Get(f,"installGuide")).Text.Contains("Esta etapa não instala"),"Tutorial explica deteccao e proximo automaticos sem instalar");
            Call(f,"AuditMods",chosen,null);check(status.Text.Contains("preparações de download")&&calls.Count==0,"Auditar selecao facial explica etapa sem enviar IDs de preparacao ao motor de receitas");
            primary.PerformClick();Application.DoEvents();check(calls.SequenceEqual(new[]{FacialDownload.List()[0].Url})&&Convert.ToString(faces[0].Cells["State"].Value)=="Aguardando download"&&!Directory.Exists(game),"Acao principal com selecao facial abre um link sem exigir pasta ou instalar");
            primary.PerformClick();check(calls.Count==1,"Segundo clique nao pula download ainda aguardado nem abre pagina duplicada");
            using(var bmp=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(bmp,new Rectangle(0,0,f.Width,f.Height));bmp.Save(Path.Combine(root,"previa-lista-facial.png"),System.Drawing.Imaging.ImageFormat.Png);}
            for(int i=0;i<12;i++)primary.PerformClick();primary.PerformClick();check(calls.Count==1&&calls.Distinct().Count()==1&&faces.All(r=>Convert.ToString(r.Cells["Installed"].Value)=="Somente download"&&Convert.ToString(r.Cells["Progress"].Value)=="—"),"Cliques repetidos nao avancam sem arquivo nem convertem espera em baixado ou instalado");
            Call(f,"StopDownloadWatch");grid.CurrentCell=faces[4].Cells["Mod"];((Task)Call(f,"DownloadSelection",new List<string>{FacialDownload.List()[4].Id})).GetAwaiter().GetResult();check(calls.Count==2&&calls.Last()==FacialDownload.List()[4].Url,"Menu Baixar reabre o arquivo facial escolhido");
            ((Task<BrowserTab>)Call(f,"OpenOfficial",null,null)).GetAwaiter().GetResult();check(calls.Count==3&&calls.Last()==FacialDownload.List()[4].Url,"Abrir download oficial da linha facial usa mesmo link fixado");
            Call(f,"StopDownloadWatch");fail=true;((Task)Call(f,"DownloadSelection",new List<string>{FacialDownload.List()[4].Id})).GetAwaiter().GetResult();check(Convert.ToString(faces[4].Cells["State"].Value)=="Falhou"&&((Label)Get(f,"summary")).Text.Contains("Falhas: 1"),"Falha do navegador aparece na linha e no contador");fail=false;primary.PerformClick();check(calls.Count==4&&Convert.ToString(faces[4].Cells["State"].Value)=="Aguardando download","Baixar proximo tenta novamente pagina que falhou");
            header.Checked=true;Call(f,"SelectAll");chosen=(List<string>)Call(f,"Chosen");check(chosen.Count==69,"Checkbox do cabecalho inclui os14downloads e55mods");
            check(primary.Text=="Baixar selecionados"&&status.Text.Contains("outros mods ficam preservados")&&((Label)Get(f,"installGuide")).Text.Contains("CONJUNTO FACIAL"),"Marcar todos deixa clara a acao de baixar somente faciais sem instalar outros mods");
            Call(f,"StopDownloadWatch");faces[3].Cells["State"].Value="A baixar";primary.PerformClick();check(calls.Count==5&&calls.Last()==FacialDownload.List()[0].Url&&((HashSet<string>)Get(f,"automaticQueue")).Count==0,"Selecao mista abre o proximo facial em vez de retornar sem acao e nao autoriza instalacao");
            primary.PerformClick();check(calls.Count==5,"Fim da selecao mista nao instala demais mods nem abre pagina extra");
            Call(f,"Install",new List<string>{FacialDownload.List()[0].Id},false);check(status.Text.Contains("após conferir")&&calls.Count==5,"Instalar sem download nao promove arquivo facial sem receita aprovada");
            Call(f,"StopDownloadWatch");var q=(HashSet<string>)Get(f,"automaticQueue");q.Add("CET");Call(f,"OpenFacial",FacialDownload.List()[0]);check(calls.Count==5&&q.Contains("CET")&&status.Text.Contains("Conclua ou cancele"),"Download facial nao continua nem cancela fila anterior de instalacao");q.Clear();
            string original=Path.Combine(root,"Hyst-preservado.zip");File.WriteAllText(original,"fixture preservada");string originalHash=Safe.Hash(original);string queue=Path.Combine(root,"importados-pendentes.json");
            var excluded=new ModImportItem{Id="file-"+ModImporter.HystExcludedHash,Kind="Arquivo",Name="Hyst_BigBreasts PUSH UP - Big Booty.zip",Source=original,Sha256=ModImporter.HystExcludedHash,Size=185225676,State="Pendente de auditoria"};
            var other=new ModImportItem{Id="file-"+originalHash,Kind="Arquivo",Name="Outro pacote.zip",Source=original,Sha256=originalHash,Size=new FileInfo(original).Length,State="Pendente de auditoria"};Safe.WriteJson(queue,new[]{excluded,other});string queueHash=Safe.Hash(queue);
            Call(f,"LoadPending");Application.DoEvents();check(!grid.Rows.Cast<DataGridViewRow>().Any(r=>Convert.ToString(r.Cells["Mod"].Value).StartsWith("Hyst_"))&&grid.Rows.Count==70&&Safe.Hash(queue)==queueHash&&Safe.Hash(original)==originalHash,"Hyst excluido sai da lista ativa sem apagar historico, ZIP ou outro importado");
            check(ModImporter.ExcludedFromVanillaProfile(excluded)&&!ModImporter.ExcludedFromVanillaProfile(other)&&catalog.Packages.Count==57&&!catalog.Packages.Any(m=>m.Id.StartsWith("face-")),"Exclusao identifica SHA exato; links faciais nao viram receitas nem alteram catalogo");
            var importer=(ModImporter)Get(f,"importer");other.Name="Hyst outro arquivo sem SHA conhecido.zip";Safe.WriteJson(queue,new[]{excluded,other});check(importer.ReadPending().Count==1,"Nome parecido nao oculta outro pacote desconhecido");
            check(((HashSet<string>)Get(f,"automaticQueue")).Count==0&&!Directory.Exists(game)&&!Directory.Exists(cache.Root),"Fluxos faciais nao criam cache ou pasta do jogo nem autorizacao de instalacao");f.Close();
        }
        Safe.WriteJson(Path.Combine(root,"resultado.json"),new{Passed=passed.Count,Tests=passed,UIOffscreen=true,RealGameTouched=false,RealBrowserUsed=false});File.WriteAllText(Path.Combine(parent,"ultimo-teste-face-main.txt"),root);
    }
}
