using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows.Forms;

public sealed class ModAuditFinding { public string Code,Level,Mod,Message,Advice,Evidence; }
public sealed class ModAuditReport {
    public readonly List<ModAuditFinding> Findings=new List<ModAuditFinding>();
    public int Selected,Dependencies;
    public void Add(string code,string level,string mod,string message,string advice,string evidence){Findings.Add(new ModAuditFinding{Code=code,Level=level,Mod=mod,Message=message,Advice=advice,Evidence=evidence});}
}

public static class ModAudit {
    public const string RedmodSource="https://www.cyberpunk.net/en/modding-support";
    static string Recipe(FixedMod mod){return "Metadados fixados: "+mod.Name+" / "+mod.Version;}
    static bool ReadError(Exception ex){return ex is IOException||ex is InvalidDataException||ex is UnauthorizedAccessException||ex is ArgumentException;}
    public static ModAuditReport Build(FixedCatalog catalog,IEnumerable<string> selected,string root,ModImportItem imported=null){
        var report=new ModAuditReport();var ids=selected.Distinct().ToList();report.Selected=ids.Count+(imported==null?0:1);
        bool hasRoot=!String.IsNullOrWhiteSpace(root);string game=null;
        if(hasRoot){try{game=Path.GetFullPath(root);if(game.Length<=3||game.StartsWith(@"\\",StringComparison.Ordinal))throw new IOException("Escolha uma pasta local do jogo.");Safe.NoLinks(game);}catch(Exception ex){if(!ReadError(ex))throw;game=null;report.Add("root","Problema","Jogo","A pasta não pôde ser conferida.","Confira o caminho e as permissões. Nenhum arquivo foi alterado.",ex.Message);}}
        else report.Add("root","Atenção","Jogo","Pasta do jogo não informada.","Escolha a pasta para conferir arquivos, dependências instaladas e versão local.","A leitura local não foi realizada.");
        List<FixedMod> mods;
        try{mods=FixedData.Expand(catalog,ids);}catch(IOException ex){report.Add("catalog","Problema","Seleção",ex.Message,"Confira os mods e as dependências da lista.","Catálogo fixado.");mods=new List<FixedMod>();}
        report.Dependencies=mods.Count(m=>!ids.Contains(m.Id));
        if(game!=null&&mods.Count>0){
            try{FixedData.ValidateGame(game,new FixedMod[0],catalog.GameFileVersion);report.Add("build","Informação","Jogo","Versão e build do jogo correspondem ao pacote.","Use as versões fixadas das dependências.","Arquivo local: "+Safe.In(game,"bin/x64/Cyberpunk2077.exe"));}
            catch(Exception ex){if(!ReadError(ex))throw;report.Add("build","Problema","Jogo",ex.Message,"Confira a pasta e a versão do jogo antes de instalar.","Conferência local de versão/build e variante antiga de Night City Alive.");}
            if(mods.Any(GameEdition.NeedsPL)&&!GameEdition.HasPL(game))report.Add("pl","Problema","Jogo","Phantom Liberty não foi encontrado.","Confira se a expansão está instalada nesta pasta.","Marcador local: archive/pc/ep1/ep1.addcont_keystone");
        }
        var scans=new Dictionary<string,ModScan>();if(game!=null)foreach(var mod in mods)scans[mod.Id]=FixedData.Scan(mod,game);
        if(mods.Count>0&&mods.All(m=>!m.RequiresRedmod))report.Add("no-redmod","Informação","Seleção","Estas variantes da lista não exigem REDmod.","RED4ext é outra ferramenta e faz parte da base do pacote.","RequiresRedmod=false no catálogo. REDmod: "+RedmodSource+"\r\nRED4ext: https://github.com/WopsS/RED4ext");
        foreach(var mod in mods){
            string plReason=GameEdition.Reason(catalog,new[]{mod.Id});
            report.Add("edition",plReason==null?"Informação":"Atenção",mod.Name,GameEdition.Category(mod),plReason??"Pode entrar no perfil sem Phantom Liberty, considerando as dependências desta receita.",(mod.PhantomLibertyNotes??Recipe(mod))+"\r\n"+(mod.PhantomLibertySource??"Metadados legados RequiresPL."));
            if(!String.IsNullOrWhiteSpace(mod.BlockedReason))report.Add("known-failure","Problema",mod.Name,mod.BlockedReason,"Consulte a correção da versão antes de tentar usar este componente.",Recipe(mod));
            if(mod.RequiresRedmod){
                report.Add("redmod","Atenção",mod.Name,"Esta variante exige o DLC gratuito REDmod.","Confira a disponibilidade oficial para sua conta/loja. A origem da cópia, sozinha, não determina compatibilidade.",Recipe(mod)+"\r\n"+RedmodSource);
                if(game!=null&&!File.Exists(Safe.In(game,"tools/redmod/bin/redMod.exe")))report.Add("redmod-missing","Problema",mod.Name,"A ferramenta REDmod não foi encontrada nesta pasta.","Confira a instalação oficial do DLC ou procure uma variante sem REDmod.","Arquivo local ausente: tools/redmod/bin/redMod.exe");
            }
            if(mod.Immutable)report.Add("immutable","Informação",mod.Name,"A base coop usa RED4ext 1.31.0 fixado.","Preserve os arquivos fixados da base; não troque o loader por exigência de outro mod.",Recipe(mod));
            if(!String.IsNullOrWhiteSpace(mod.Warning))report.Add("notes",mod.Warning.IndexOf("Somente",StringComparison.OrdinalIgnoreCase)>=0||mod.Warning.IndexOf("refit",StringComparison.OrdinalIgnoreCase)>=0||mod.Warning.Contains("HandLeft")?"Atenção":"Informação",mod.Name,mod.Warning,"Confira a variante do corpo/personagem e as instruções específicas nos detalhes do mod.",Recipe(mod));
            if(mod.Id=="mod-6945"&&mod.Version=="1.2.10")report.Add("wardrobe","Atenção",mod.Name,"O gerenciamento de roupas pode converter o guarda-roupa do save.","Preserve o backup dos saves antes de mudar este componente.","Auditoria da receita Equipment-EX 1.2.10; https://www.nexusmods.com/cyberpunk2077/mods/6945");
            if(mod.Dependencies.Length>0){
                var deps=mod.Dependencies.Select(id=>catalog.Packages.Single(m=>m.Id==id)).ToList();bool missing=game!=null&&deps.Any(d=>!scans[d.Id].IsCorrect);
                report.Add("dependencies",missing?"Atenção":"Informação",mod.Name,missing?"Há dependências ausentes, incompletas ou diferentes.":"Dependências desta receita.","Use as versões da lista; o instalador já considera as dependências nas suas operações.",String.Join("\r\n",deps.Select(d=>d.Name+" "+d.Version+(game==null?"":" — "+scans[d.Id].State))));
            }
            ModScan scan;if(scans.TryGetValue(mod.Id,out scan))report.Add("files",scan.IsConflict?"Problema":scan.State=="Parcial"?"Atenção":"Informação",mod.Name,scan.State,scan.IsCorrect?"Os arquivos correspondem à receita fixada.":scan.IsConflict?"Preserve seus arquivos e confira a divergência ou o conflito.":"Confira os arquivos ausentes na lista do instalador.",scan.Detail);
        }
        if(imported!=null)InspectImport(report,catalog,imported,game);
        return report;
    }
    static void InspectImport(ModAuditReport report,FixedCatalog catalog,ModImportItem item,string game){
        report.Add("unknown","Atenção",item.Name,"Este item não tem receita fixada no pacote.","Confira os requisitos e a versão na página do autor. A análise deste relatório não cadastra nem autoriza instalação.",item.Source);
        if(item.Kind!="Arquivo"){report.Add("link-only","Informação",item.Name,"Um link sozinho não revela o conteúdo do pacote.","Importe o arquivo original para analisar sua estrutura, ou consulte a página oficial.","Sem acesso à conta Nexus ou consulta automática à página.");return;}
        try{
            Safe.NoLinks(item.Source);
            if(!File.Exists(item.Source)||new FileInfo(item.Source).Length!=item.Size||Safe.Hash(item.Source)!=item.Sha256){report.Add("changed-import","Problema",item.Name,"O arquivo está ausente ou mudou desde a importação.","Confira o pacote original e importe o arquivo correto novamente.","Tamanho/SHA-256 diferentes do registro local.");return;}
            if(!Path.GetExtension(item.Source).Equals(".zip",StringComparison.OrdinalIgnoreCase)){report.Add("format","Informação",item.Name,"A inspeção de conteúdo desta versão lê somente ZIP.","Confira os requisitos do RAR/7z na página do autor.","Arquivo preservado, sem extração ou execução.");return;}
            var names=new List<string>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using(var zip=ZipFile.OpenRead(item.Source)){
                if(zip.Entries.Count>20000)throw new IOException("O ZIP ultrapassa o limite de 20 mil entradas desta análise.");
                foreach(var entry in zip.Entries){
                    string path=entry.FullName.TrimEnd('/');if(path.Length==0)continue;
                    if(path.Length>500||(entry.ExternalAttributes>>16&0xF000)==0xA000)throw new IOException("O ZIP contém nome muito longo ou link de arquivo/pasta.");
                    Safe.In(Path.GetTempPath(),path);
                    if(!seen.Add(path))report.Add("duplicate","Atenção",item.Name,"Há destinos repetidos dentro do ZIP.","Confira a estrutura antes de copiar os arquivos.",path);
                    if(!entry.FullName.EndsWith("/"))names.Add(path);
                }
            }
            report.Add("zip","Informação",item.Name,names.Count+" arquivos listados no ZIP.","Nenhum conteúdo foi extraído ou executado; nomes de arquivos indicam estrutura, não versão mínima nem compatibilidade garantida.","Inventário de nomes/destinos do pacote importado.");
            var infos=names.Where(n=>n.EndsWith("/info.json",StringComparison.OrdinalIgnoreCase)||n.Equals("info.json",StringComparison.OrdinalIgnoreCase));
            bool redmod=infos.Any(n=>n.IndexOf("mods/",StringComparison.OrdinalIgnoreCase)>=0||names.Any(p=>p.StartsWith(n.Substring(0,n.Length-9)+"archives/",StringComparison.OrdinalIgnoreCase)));
            if(redmod)report.Add("zip-redmod","Atenção",item.Name,"A estrutura do ZIP sugere uma variante REDmod.","Confirme com o autor se exige o DLC e se existe variante sem REDmod. Não confunda com RED4ext.","Inferência por info.json e pasta mods/archives. "+RedmodSource);
            if(names.Any(n=>n.IndexOf("cyber_engine_tweaks/mods/",StringComparison.OrdinalIgnoreCase)>=0&&n.EndsWith(".lua",StringComparison.OrdinalIgnoreCase)))report.Add("zip-cet","Atenção",item.Name,"Foram encontrados scripts na pasta do CET.","Confira a versão mínima do Cyber Engine Tweaks exigida pelo autor.","Inferência por destino dos arquivos .lua.");
            if(names.Any(n=>n.StartsWith("r6/scripts/",StringComparison.OrdinalIgnoreCase)&&n.EndsWith(".reds",StringComparison.OrdinalIgnoreCase)))report.Add("zip-redscript","Atenção",item.Name,"Foram encontrados scripts .reds em r6/scripts.","Confira os requisitos de redscript na página do autor.","Inferência pelo tipo/destino. https://github.com/jac3km4/redscript");
            if(names.Any(n=>n.StartsWith("red4ext/plugins/",StringComparison.OrdinalIgnoreCase)&&n.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)))report.Add("zip-native","Atenção",item.Name,"Foi encontrado um plugin nativo para RED4ext.","Confira a versão do jogo e a exigência de RED4ext, preservando a base 1.31.0.","Inferência pelo destino da DLL. https://github.com/WopsS/RED4ext");
            if(names.Any(n=>new[]{".exe",".bat",".cmd",".ps1",".vbs"}.Contains(Path.GetExtension(n).ToLowerInvariant())))report.Add("zip-program","Atenção",item.Name,"O pacote contém programa ou script de execução.","Confira com o autor a função desses arquivos antes de executá-los.","Extensão dos arquivos no ZIP; nenhum foi executado.");
            var basePaths=new HashSet<string>(catalog.Packages.Where(m=>m.Immutable).SelectMany(m=>m.Files).Select(f=>f.Path),StringComparer.OrdinalIgnoreCase);var baseMatches=names.Where(basePaths.Contains).ToList();
            if(baseMatches.Count>0)report.Add("zip-base","Problema",item.Name,"O ZIP inclui caminhos usados pela base coop.","Compare as versões e preserve a base original antes de substituir arquivos.",String.Join("\r\n",baseMatches));
            if(game!=null){var overlaps=names.Where(n=>Safe.Allowed(n)&&File.Exists(Safe.In(game,n))).ToList();if(overlaps.Count>0)report.Add("zip-overlap","Atenção",item.Name,overlaps.Count+" destinos do ZIP já existem na pasta do jogo.","Compare os arquivos antes de substituir. Destino igual não prova conflito de conteúdo.",String.Join("\r\n",overlaps.Take(10)));}
        }catch(Exception ex){if(!ReadError(ex))throw;report.Add("archive-read","Problema",item.Name,"Não foi possível concluir a inspeção segura do pacote.","Confira se o download terminou e se a estrutura está correta. Nenhum arquivo foi alterado.",ex.Message);}
    }
}

public sealed class ModAuditWindow:Form {
    public ModAuditWindow(ModAuditReport report){
        Text="Auditoria dos mods";Font=new Font("Segoe UI",10);BackColor=Color.White;ClientSize=new Size(960,650);MinimumSize=new Size(760,520);StartPosition=FormStartPosition.CenterParent;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(18),ColumnCount=1,RowCount=5};Controls.Add(layout);
        for(int i=0;i<5;i++)layout.RowStyles.Add(new RowStyle(i==2?SizeType.Percent:SizeType.AutoSize,i==2?100:0));
        layout.Controls.Add(new Label{Text="Auditoria informativa",Font=new Font(Font.FontFamily,18,FontStyle.Bold),AutoSize=true},0,0);
        layout.Controls.Add(new Label{Text="Selecionados: "+report.Selected+" | Dependências incluídas: "+report.Dependencies+" | Problemas: "+report.Findings.Count(f=>f.Level=="Problema")+" | Atenção: "+report.Findings.Count(f=>f.Level=="Atenção")+"\nEste relatório só orienta. Não altera arquivos, não autoriza nem impede instalações.",AutoSize=true,Margin=new Padding(0,8,0,12)},0,1);
        var grid=new DataGridView{Name="AuditFindings",Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,BackgroundColor=Color.White,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false};
        grid.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Tipo",Width=110});grid.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Mod",Width=210});grid.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="O que encontramos",AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill});
        var details=new TextBox{Name="AuditExplanation",Dock=DockStyle.Fill,ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.Vertical,Height=150,BackColor=Color.White,Margin=new Padding(0,10,0,8)};
        foreach(var finding in report.Findings.OrderBy(f=>f.Level=="Problema"?0:f.Level=="Atenção"?1:2)){int row=grid.Rows.Add(finding.Level,finding.Mod,finding.Message);grid.Rows[row].Tag=finding;grid.Rows[row].DefaultCellStyle.ForeColor=finding.Level=="Problema"?Color.Firebrick:finding.Level=="Atenção"?Color.FromArgb(140,86,0):Color.Black;}
        Action explain=()=>{var finding=grid.CurrentRow==null?null:grid.CurrentRow.Tag as ModAuditFinding;details.Text=finding==null?"":finding.Message+"\r\n\r\nRecomendação: "+finding.Advice+"\r\n\r\nEvidência: "+finding.Evidence;};grid.CurrentCellChanged+=(s,e)=>explain();
        layout.Controls.Add(grid,0,2);layout.Controls.Add(details,0,3);
        var footer=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,FlowDirection=FlowDirection.RightToLeft};var close=new Button{Text="Fechar",AutoSize=true,MinimumSize=new Size(120,34),DialogResult=DialogResult.Cancel};footer.Controls.Add(close);CancelButton=close;layout.Controls.Add(footer,0,4);
        Shown+=(s,e)=>{if(grid.Rows.Count>0)grid.CurrentCell=grid.Rows[0].Cells[0];explain();};
    }
}
