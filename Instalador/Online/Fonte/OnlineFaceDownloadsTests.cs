using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

public static class OnlineFaceDownloadsTests {
    public static void Run(string parent){
        var passed=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("TESTE FACIAL FALHOU: "+name);passed.Add(name);};
        string root=Path.Combine(Path.GetFullPath(parent),"face-downloads-"+Guid.NewGuid().ToString("N").Substring(0,12));Directory.CreateDirectory(root);
        var items=FacialDownload.List();string expected="16930:99414,18594:95893,2422:30004,1873:56673,1873:12061,7341:160715,7474:39563,7474:39566,7290:94454,11033:58122,6788:35570,4131:58415,1975:8720,1915:8374";
        check(items.Count==14&&String.Join(",",items.Select(i=>i.ModId+":"+i.FileId))==expected,"Quatorze arquivos correspondem a selecao autorizada, com file_id exato");
        check(items.Select(i=>i.Url).Distinct().Count()==14&&!items.Any(i=>new[]{7340,9083,1424,5063,7125,15555,7054,17331,2984}.Contains(i.ModId)),"Sem efeito removido, corpos, rigs, opcionais ou arquivos ja conferidos na lista de downloads");
        check(items.All(i=>{var p=OnlineWindow.BrowserRequest(i.Url);return p.FileName==i.Url&&p.UseShellExecute&&p.Verb=="open"&&String.IsNullOrEmpty(p.Arguments);}),"Links oficiais usam associacao normal do Windows sem perfil ou navegador fixo");
        var calls=new List<string>();
        using(var f=new FacialDownloadsWindow(Path.Combine(root,"Downloads"),url=>calls.Add(url))){
            f.ShowInTaskbar=false;f.StartPosition=FormStartPosition.Manual;f.Location=new Point(-30000,-30000);f.Show();Application.DoEvents();
            var grid=(DataGridView)f.Controls.Find("FacialDownloadList",true).Single();var next=(Button)f.Controls.Find("NextFacialDownload",true).Single();var selected=(Button)f.Controls.Find("OpenSelectedFacialDownload",true).Single();var count=(Label)f.Controls.Find("FacialDownloadCount",true).Single();
            check(grid.Rows.Count==14&&grid.ReadOnly&&calls.Count==0&&grid.Rows.Cast<DataGridViewRow>().All(r=>Convert.ToString(r.Cells["State"].Value)=="A baixar"),"Abrir grupo lista arquivos e nao abre browser ou afirma download completo");
            next.PerformClick();Application.DoEvents();check(calls.SequenceEqual(new[]{items[0].Url})&&Convert.ToString(grid.Rows[0].Cells["State"].Value)=="Página aberta","Um clique abre somente o primeiro link e marca apenas pagina aberta");
            grid.CurrentCell=grid.Rows[4].Cells[0];selected.PerformClick();Application.DoEvents();check(calls.Count==2&&calls[1]==items[4].Url&&count.Text.Contains("Páginas abertas nesta janela: 2"),"Abrir selecionado respeita a variante escolhida e contador de paginas");
            using(var bmp=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(bmp,new Rectangle(0,0,f.Width,f.Height));bmp.Save(Path.Combine(root,"previa-conjunto-facial.png"),System.Drawing.Imaging.ImageFormat.Png);}
            next.PerformClick();check(calls.Count==3&&calls[2]==items[1].Url,"Proximo escolhe primeiro link ainda nao aberto sem saltar arquivos");
            for(int i=0;i<11;i++)next.PerformClick();check(calls.Count==14&&calls.Distinct().Count()==14&&!next.Enabled&&count.Text.Contains("A abrir: 0"),"Lista termina depois de14 cliques manuais, sem abrir todas as paginas de uma vez");
            f.OpenNext();f.Open(-1);f.Open(14);check(calls.Count==14,"Fim da lista e indices invalidos nao abrem paginas extras");
            f.Open(0);check(calls.Count==15&&count.Text.Contains("Páginas abertas nesta janela: 14")&&grid.Rows.Cast<DataGridViewRow>().All(r=>Convert.ToString(r.Cells["State"].Value)=="Página aberta"),"Reabrir link nao duplica contador nem transforma pagina aberta em baixado ou instalado");
            f.Close();
        }
        bool fail=true;int attempts=0;
        using(var f=new FacialDownloadsWindow(Path.Combine(root,"Downloads"),url=>{attempts++;if(fail)throw new IOException("fixture");calls.Add(url);})){f.ShowInTaskbar=false;f.StartPosition=FormStartPosition.Manual;f.Location=new Point(-30000,-30000);f.Show();Application.DoEvents();
            var grid=(DataGridView)f.Controls.Find("FacialDownloadList",true).Single();var next=(Button)f.Controls.Find("NextFacialDownload",true).Single();var count=(Label)f.Controls.Find("FacialDownloadCount",true).Single();
            next.PerformClick();check(attempts==1&&Convert.ToString(grid.Rows[0].Cells["State"].Value)=="Não abriu"&&count.Text.Contains("Páginas abertas nesta janela: 0")&&next.Enabled,"Falha de abertura preserva item para tentar novamente sem avancar ou afirmar sucesso");
            fail=false;next.PerformClick();check(attempts==2&&calls.Last()==items[0].Url&&count.Text.Contains("Páginas abertas nesta janela: 1"),"Nova tentativa abre o mesmo primeiro arquivo com sucesso");f.Close();
        }
        check(!Directory.Exists(Path.Combine(root,"Downloads"))&&FacialMainListTests.PreparationCatalog().Packages.Count==57,"Grupo nao cria pasta, cache ou receita instalavel; catalogo principal preservado");
        Safe.WriteJson(Path.Combine(root,"resultado.json"),new{Passed=passed.Count,Tests=passed,BrowserLaunchSimulated=true,RealNetworkUsed=false,RealGameTouched=false});File.WriteAllText(Path.Combine(parent,"ultimo-teste-face-downloads.txt"),root);
    }
}
