using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

public static class ModAuditTests {
    static string Hash(string text){using(var h=SHA256.Create())return Safe.Hex(h.ComputeHash(Encoding.UTF8.GetBytes(text)));}
    static void Write(string root,string path,string text){string p=Safe.In(root,path);Directory.CreateDirectory(Path.GetDirectoryName(p));File.WriteAllText(p,text,new UTF8Encoding(false));}
    static ModImportItem Zip(string root,string name,Dictionary<string,string> entries){
        string path=Path.Combine(root,name+".zip");using(var zip=ZipFile.Open(path,ZipArchiveMode.Create))foreach(var e in entries){var entry=zip.CreateEntry(e.Key);using(var writer=new StreamWriter(entry.Open(),new UTF8Encoding(false)))writer.Write(e.Value);}
        return new ModImportItem{Name=name,Kind="Arquivo",Source=path,Size=new FileInfo(path).Length,Sha256=Safe.Hash(path)};
    }
    static string Snapshot(string root){return String.Join("\n",Directory.GetFiles(root,"*",SearchOption.AllDirectories).OrderBy(p=>p).Select(p=>p+" "+Safe.Hash(p)));}
    static string Canonical(object value){var map=value as IDictionary<string,object>;if(map!=null)return "{"+String.Join(",",map.Keys.OrderBy(k=>k,StringComparer.Ordinal).Select(k=>Safe.Json.Serialize(k)+":"+Canonical(map[k])))+"}";var array=value as object[];return array==null?Safe.Json.Serialize(value):"["+String.Join(",",array.Select(Canonical))+"]";}
    static string Identity(FixedCatalog catalog){return Canonical(Safe.Json.DeserializeObject(Safe.Json.Serialize(catalog)));}
    static bool Has(ModAuditReport report,string code,string level=null){return report.Findings.Any(f=>f.Code==code&&(level==null||f.Level==level));}
    public static void Run(string parent){
        string root=Path.Combine(parent,"audit-"+Guid.NewGuid().ToString("N").Substring(0,12));Directory.CreateDirectory(root);
        var passed=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("TESTE AUDITOR FALHOU: "+name);passed.Add(name);};
        string game=Path.Combine(root,"game");Write(game,".fixture-only","test");string exe=Safe.In(game,"bin/x64/Cyberpunk2077.exe");Directory.CreateDirectory(Path.GetDirectoryName(exe));File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","FixtureGame.exe"),exe);
        var asset=new FixedAsset{Id="a0",Url="https://github.com/example/project/releases/download/test/fixture.zip",Format="zip",Sha256=Hash("fixture"),Size=7};
        string[] paths={"bin/x64/winmm.dll","red4ext/RED4ext.dll","red4ext/LICENSE.txt","red4ext/THIRD_PARTY_LICENSES.txt","red4ext/plugins/NightCoop/NightCoop.dll"};
        var basis=new FixedMod{Id="NightCoop",Name="Base de teste",Version="test",Immutable=true,GameVersion="2.5",Dependencies=new string[0],Assets=new List<FixedAsset>{asset},Files=paths.Select(p=>new FixedFile{Asset="a0",Path=p,Entry=p,Sha256=Hash("base "+p),Size=Encoding.UTF8.GetByteCount("base "+p)}).ToList()};
        var extra=new FixedMod{Id="Extra",Name="Complemento teste",Version="1.0",GameVersion="2.5",Dependencies=new[]{"NightCoop"},Warning="Somente FemV.",Assets=new List<FixedAsset>{asset},Files=new List<FixedFile>{new FixedFile{Asset="a0",Path="archive/pc/mod/a.archive",Entry="archive/pc/mod/a.archive",Sha256=Hash("extra"),Size=5}}};
        var catalog=new FixedCatalog{Schema=1,Id="audit-fixture",GameVersion="2.5",GameFileVersion="2.5.0.0",Packages=new List<FixedMod>{basis,extra}};
        var noRoot=ModAudit.Build(catalog,new[]{"Extra"},"");check(Has(noRoot,"root","Atenção")&&noRoot.Selected==1&&noRoot.Dependencies==1,"Auditor inclui dependencias e informa ausencia de pasta sem inventar leitura local");
        check(Has(noRoot,"no-redmod")&&!Has(noRoot,"redmod"),"RED4ext na base nao e confundido com exigencia REDmod");
        check(Has(noRoot,"notes","Atenção"),"Variante FemV recebe recomendacao documentada de personagem/corpo");
        string before=Snapshot(game);var missing=ModAudit.Build(catalog,new[]{"Extra"},game);check(Has(missing,"build","Informação")&&Has(missing,"dependencies","Atenção")&&Snapshot(game)==before,"Detecta dependencias ausentes com build correto sem escrever no jogo");
        foreach(string p in paths)Write(game,p,"base "+p);Write(game,extra.Files[0].Path,"extra");var correct=ModAudit.Build(catalog,new[]{"Extra"},game);
        check(!Has(correct,"dependencies","Atenção")&&correct.Findings.Count(f=>f.Code=="files"&&f.Message=="Instalado correto")==2,"Arquivos corretos sao reconhecidos por hash em componente e dependencia");
        Write(game,"red4ext/RED4ext.dll","alterado");var altered=ModAudit.Build(catalog,new[]{"Extra"},game);check(Has(altered,"files","Problema")&&File.ReadAllText(Safe.In(game,"red4ext/RED4ext.dll"))=="alterado","Base alterada gera problema informativo e permanece intacta");
        extra.IncompatiblePaths=new[]{"r6/scripts/incompatible/present.reds"};Write(game,extra.IncompatiblePaths[0],"incompatível");check(ModAudit.Build(catalog,new[]{"Extra"},game).Findings.Any(f=>f.Code=="files"&&f.Message=="Incompatível"),"Conflito conhecido e descrito sem remover arquivos");extra.IncompatiblePaths=null;
        catalog.GameFileVersion="2.5.0.99";check(Has(ModAudit.Build(catalog,new[]{"Extra"},game),"build","Problema"),"Build divergente aparece no relatorio");catalog.GameFileVersion="2.5.0.0";
        extra.RequiresPL=true;check(Has(ModAudit.Build(catalog,new[]{"Extra"},game),"pl","Problema"),"Phantom Liberty ausente e identificado");extra.RequiresPL=false;
        extra.RequiresRedmod=true;var redmod=ModAudit.Build(catalog,new[]{"Extra"},game);check(Has(redmod,"redmod","Atenção")&&Has(redmod,"redmod-missing","Problema")&&!Safe.Json.Serialize(redmod).Contains("não funciona com cópias"),"REDmod usa requisito e arquivo local, sem condenar origem da copia");extra.RequiresRedmod=false;
        var link=new ModImportItem{Kind="Link",Name="Link teste",Source="https://www.nexusmods.com/cyberpunk2077/mods/123"};check(Has(ModAudit.Build(catalog,new string[0],game,link),"link-only"),"Link desconhecido nao recebe requisitos inventados nem consulta autenticada");
        var package=Zip(root,"unknown",new Dictionary<string,string>{{"mods/Example/info.json","{}"},{"mods/Example/archives/test.archive","archive"},{"bin/x64/plugins/cyber_engine_tweaks/mods/Example/init.lua","--"},{"r6/scripts/Example/main.reds","//"},{"red4ext/plugins/Example.dll","not-a-dll"},{"red4ext/RED4ext.dll","new"},{"installer.cmd","not-executed"},{"archive/pc/mod/a.archive","overlap"}});
        string original=Safe.Hash(package.Source);before=Snapshot(game);var imported=ModAudit.Build(catalog,new string[0],game,package);
        check(Has(imported,"zip-redmod","Atenção")&&imported.Findings.Single(f=>f.Code=="zip-redmod").Evidence.StartsWith("Inferência"),"Estrutura REDmod em ZIP e marcada como inferencia, nao certeza por nome");
        check(Has(imported,"zip-cet")&&Has(imported,"zip-redscript")&&Has(imported,"zip-native"),"Destinos de CET, redscript e plugin RED4ext geram recomendacoes sem inventar versao minima");
        check(Has(imported,"zip-base","Problema")&&Has(imported,"zip-overlap")&&Has(imported,"zip-program"),"Auditor identifica caminhos da base, sobreposicao e programa no pacote");
        check(Snapshot(game)==before&&Safe.Hash(package.Source)==original&&!Directory.Exists(Safe.In(game,".CoopModsOnline")),"Inspecao preserva original e jogo sem extrair, executar, instalar ou gerar recibo");
        var bad=Zip(root,"traversal",new Dictionary<string,string>{{"../outside.txt","x"}});check(Has(ModAudit.Build(catalog,new string[0],game,bad),"archive-read","Problema")&&!File.Exists(Path.Combine(root,"outside.txt")),"Caminho inseguro no ZIP gera aviso sem extracao");
        string broken=Path.Combine(root,"broken.zip");File.WriteAllText(broken,"not-a-zip");var brokenItem=new ModImportItem{Kind="Arquivo",Name="broken",Source=broken,Size=new FileInfo(broken).Length,Sha256=Safe.Hash(broken)};check(Has(ModAudit.Build(catalog,new string[0],game,brokenItem),"archive-read","Problema"),"ZIP invalido gera relatorio informativo em vez de escapar como erro da janela");
        var symlink=Zip(root,"symlink",new Dictionary<string,string>{{"link","target"}});using(var zip=ZipFile.Open(symlink.Source,ZipArchiveMode.Update))zip.Entries[0].ExternalAttributes=unchecked((int)0xA0000000);symlink.Sha256=Safe.Hash(symlink.Source);symlink.Size=new FileInfo(symlink.Source).Length;check(Has(ModAudit.Build(catalog,new string[0],game,symlink),"archive-read","Problema"),"Entrada de link no ZIP e relatada sem extracao");
        string rar=Path.Combine(root,"source.rar");File.WriteAllText(rar,"rarfixture");check(Has(ModAudit.Build(catalog,new string[0],game,new ModImportItem{Kind="Arquivo",Name="rar",Source=rar,Size=new FileInfo(rar).Length,Sha256=Safe.Hash(rar)}),"format","Informação"),"RAR/7z nao sao executados nem recebem inspecao inventada");
        File.AppendAllText(package.Source,"modified");check(Has(ModAudit.Build(catalog,new string[0],game,package),"changed-import","Problema"),"Arquivo alterado desde a importacao nao recebe inventario de outra identidade");
        check(Has(ModAudit.Build(catalog,new[]{"Extra"},@"\\invalid-server\share"),"root","Problema"),"Auditoria recusa caminho de rede antes de consultar arquivos remotos");
        var real=FixedData.Load();var active=real.Packages.Where(m=>String.IsNullOrWhiteSpace(m.BlockedReason)&&!m.ManualSelection).Select(m=>m.Id).ToList();string identity=Identity(real);var full=ModAudit.Build(real,active,"");
        string afterIdentity=Identity(real),auditJson=Safe.Json.Serialize(full);
        if(afterIdentity!=identity)Safe.WriteJson(Path.Combine(root,"catalog-difference.json"),new{Before=identity,After=afterIdentity});
        check(Has(full,"wardrobe")&&afterIdentity==identity&&!auditJson.Contains("coop pendente")&&!auditJson.Contains("experimental"),"Receitas atuais preservam confirmacao coop e notas reais sem alterar catalogo; wardrobe="+Has(full,"wardrobe")+" identity="+(afterIdentity==identity)+" pending="+auditJson.Contains("coop pendente")+" experimental="+auditJson.Contains("experimental"));
        using(var window=new ModAuditWindow(imported)){
            window.ShowInTaskbar=false;window.StartPosition=FormStartPosition.Manual;window.Location=new Point(-30000,-30000);window.Show();Application.DoEvents();
            var controls=new Stack<Control>();controls.Push(window);var buttons=new List<Button>();bool informative=false;
            while(controls.Count>0){var c=controls.Pop();if(c is Button)buttons.Add((Button)c);if(c is Label&&c.Text.Contains("não autoriza nem impede instalações"))informative=true;foreach(Control child in c.Controls)controls.Push(child);}
            var grid=(DataGridView)window.Controls.Find("AuditFindings",true)[0];if(grid.Rows.Count>0){grid.CurrentCell=grid.Rows[grid.Rows.Count-1].Cells[0];grid.CurrentCell=grid.Rows[0].Cells[0];}Application.DoEvents();
            check(informative&&buttons.Count==1&&buttons[0].Text=="Fechar"&&((TextBox)window.Controls.Find("AuditExplanation",true)[0]).ReadOnly,"Auditor tem so leitura e Fechar, sem botao de autorizar ou instalacao");
            check(((TextBox)window.Controls.Find("AuditExplanation",true)[0]).Text.StartsWith(((ModAuditFinding)grid.CurrentRow.Tag).Message),"Explicacao corresponde exatamente a linha selecionada, inclusive primeira linha");
            using(var bmp=new Bitmap(window.Width,window.Height)){window.DrawToBitmap(bmp,new Rectangle(0,0,window.Width,window.Height));bmp.Save(Path.Combine(root,"previa-auditor.png"),System.Drawing.Imaging.ImageFormat.Png);}window.Close();
        }
        Safe.WriteJson(Path.Combine(root,"resultado-auditor.json"),new{Passed=passed.Count,Tests=passed,InformationalOnly=true,GameModified=false,ArchivesExtracted=false,NetworkUsed=false,Preview=Path.Combine(root,"previa-auditor.png")});File.WriteAllText(Path.Combine(parent,"ultimo-teste-auditor.txt"),root);
    }
}
