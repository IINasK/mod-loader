using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    internal static void RunProjectSearchTests(string parent){
        string root=OnlineTests.NestedRoot(parent,"search-");Directory.CreateDirectory(root);
        var checks=new List<string>();Action<bool,string> check=(yes,message)=>{if(!yes)throw new IOException(message);checks.Add(message);};
        check(GeneralModSearch.Matches(GeneralModSearch.Terms("veiculos"),"Veículos"),"Search ignores accents and case.");
        check(GeneralModSearch.Matches(GeneralModSearch.Terms("wolf"),"Wolfcut Hair"),"Name fragments match.");
        check(GeneralModSearch.Matches(GeneralModSearch.Terms("wlofcut"),"Wolfcut Hair"),"Transposed letters are accepted.");
        check(GeneralModSearch.Matches(GeneralModSearch.Terms("Cyberpnuk"),"Cyberpunk 2077"),"Long names tolerate two small edits.");
        check(!GeneralModSearch.Matches(GeneralModSearch.Terms("2078"),"Cyberpunk 2077"),"Numeric versions and identifiers are literal.");
        check(!GeneralModSearch.Matches(GeneralModSearch.Terms("abc"),"axc"),"Very short queries are not fuzzed.");
        check(!GeneralModSearch.Matches(GeneralModSearch.Terms("banana"),"Wolfcut Hair"),"Unrelated words do not match.");
        var a=NewEntry("Wolfcut Hair","https://example.com/a.zip");a.Category="Aparência";a.Variants[0].Name="Feminina 2.0";
        var b=NewEntry("Nissan GTR","https://example.com/b.zip");b.Category="Veículos";b.Variants[0].Name="Principal";
        var c=NewEntry("Radio Ext","https://example.com/c.zip");c.Category="Principal";
        var list=new GeneralModList{Categories=new List<string>{a.Category,b.Category,c.Category},Mods=new List<GeneralModEntry>{a,b,c}};
        list.Presets.Add(new GeneralModPreset{Id="kit",Name="Meu kit",ModIds=new List<string>{a.Id,b.Id}});
        var state=new GeneralModState{Destination=root,DownloadFolder=root,List=list};
        TestForm(FixedData.Load(),state,Path.Combine(root,"state"),root,async form=>{
            form.statuses[Key(a,form.Selected(a))]="Instalado";form.statuses[Key(b,form.Selected(b))]="Falhou";form.DrawRows();form.Persist();string original=Safe.Hash(Path.Combine(form.data,"lista.json"));string projectBefore=ModAuditTests.Canonical(Safe.Json.DeserializeObject(Safe.Json.Serialize(state.List)));
            form.marked.Add(b.Id);
            form.searchBox.Text="wlofcut feminina instalado";await Task.Delay(220);
            check(form.VisibleMods().Single()==a,"The native search field combines name typo, variant and visible status.");
            check(original==Safe.Hash(Path.Combine(form.data,"lista.json")),"Typing a query does not save or replace the project.");
            check(form.ModRows().Count()==1&&form.marked.Contains(b.Id),"Filtering hides unmatched rows and preserves hidden checked items.");
            form.categoryFilters=new HashSet<string>{b.Category};form.DrawRows();check(form.VisibleMods().Count==0,"Search composes with category filters.");
            check(form.grid.Rows.Cast<DataGridViewRow>().Any(row=>row.Tag is SectionRow&&((SectionRow)row.Tag).Kind=="empty"),"No results has an explicit empty-state message.");
            form.categoryFilters=null;form.searchBox.Text="falha";await Task.Delay(220);check(form.VisibleMods().Single()==b,"Search finds failed status.");
            form.searchBox.Text="pendente";await Task.Delay(220);check(form.VisibleMods().Single()==c,"Search finds pending status.");
            state.Language="en";form.BuildInterface();form.searchBox.Text="installed";await Task.Delay(220);check(form.VisibleMods().Single()==a,"Status is searchable in the active English language.");
            form.searchBox.Text="principal";await Task.Delay(220);form.statusFilters=new HashSet<string>{"Failed"};form.DrawRows();check(form.VisibleMods().Single()==b,"Search composes with status and variant values.");
            form.statusFilters=null;form.searchBox.Text="feminina";await Task.Delay(220);form.preset.SelectedIndex=1;check(form.VisibleMods().Single()==a,"Preset membership and search are combined.");
            form.searchBox.Text="radio";await Task.Delay(220);check(form.VisibleMods().Count==0,"Search never leaks items outside the current preset.");
            form.preset.SelectedIndex=0;form.searchBox.Clear();await Task.Delay(220);check(form.VisibleMods().Count==3&&form.marked.Contains(b.Id),"Clearing restores rows without changing selection.");
            foreach(string theme in new[]{"light-red","light","dark"}){
                state.Theme=theme;form.BuildInterface();form.ClientSize=new Size(1000,720);form.PerformLayout();await Task.Delay(30);
                var field=form.searchBox.Parent.Parent;
                check(form.tableName.PointToScreen(Point.Empty).Y+form.tableName.Height<=field.PointToScreen(Point.Empty).Y&&field.PointToScreen(Point.Empty).Y+field.Height<=form.grid.PointToScreen(Point.Empty).Y,"Search fits between title and column headers: "+theme);
                check(form.searchBox.BackColor==GeneralStyle.Surface&&form.searchBox.ForeColor==GeneralStyle.Foreground&&form.searchBox.BorderStyle==BorderStyle.None,"Search text uses the active palette without a square native border: "+theme);
                check(field.Height>=44&&form.searchBox.Width>350,"Search remains usable at minimum window width: "+theme);
                form.searchBox.Text="gtr";await Task.Delay(220);form.expandList.PerformClick();check(form.searchBox.Visible&&form.VisibleMods().Single()==b,"Expanded list retains live search: "+theme);
                CaptureUi(form,Path.Combine(root,"search-expanded-"+theme+".png"));form.expandList.PerformClick();CaptureUi(form,Path.Combine(root,"search-"+theme+".png"));
            }
            form.ResetColumnFilters();form.DrawRows();check(form.searchBox.Text==""&&form.VisibleMods().Count==3,"Reset clears the query and column filters together.");
            check(ModAuditTests.Canonical(Safe.Json.DeserializeObject(Safe.Json.Serialize(state.List)))==projectBefore,"Search/filter/expand preserve names, categories and preset members.");
            form.optionsButton.PerformClick();var menu=(GeneralActionsMenu)form.menus.Current;
            check(form.homeButton.Visible&&!AllControls(menu).OfType<Button>().Any(button=>button.Name=="home"),"Back to main menu is outside the project menu.");form.menus.Close();
        });
        GeneralStyle.UseTheme("light");GeneralStyle.Language="pt";
        Safe.WriteJson(Path.Combine(root,"resultado-project-search.json"),new{Passed=true,Tests=checks,RealGameTouched=false,NetworkUsed=false});
    }
    internal static void RunRedesignTests(string parent){
        string root=OnlineTests.NestedRoot(parent,"redesign-");Directory.CreateDirectory(root);
        string archive=TestZip(root,"original.zip","mods/item.txt","unchanged");
        var first=NewEntry("Alpha",archive);first.Category="Principal";
        var second=NewEntry("Beta",archive);second.Category="Veículos";second.Variants[0].Name="Alternativa";
        var third=NewEntry("Gamma",archive);third.Category="Principal";
        var list=new GeneralModList{Mods=new List<GeneralModEntry>{first,second,third},Categories=new List<string>{"Principal","Veículos"}};
        list.Presets.Add(new GeneralModPreset{Id="kit",Name="Kit original",ModIds=list.Mods.Select(mod=>mod.Id).ToList()});
        var state=new GeneralModState{List=list,SelectedPreset="kit",Destination=root,DownloadFolder=root};
        var checks=new List<string>();Action<bool,string> check=(yes,message)=>{if(!yes)throw new IOException(message);checks.Add(message);};
        TestForm(FixedData.Load(),state,Path.Combine(root,"state"),root,async form=>{
            check(form.tableName.Text=="Kit original"&&!form.tableName.ReadOnly,"Título integrado usa o nome do preset ativo.");
            form.tableName.Text="Kit renomeado";form.SaveTableTitle();check(list.Presets[0].Name=="Kit renomeado"&&form.preset.SelectedItem.ToString()=="Kit renomeado","Renomear a tabela atualiza e persiste o preset.");
            first=form.state.List.Mods.Single(mod=>mod.Id==first.Id);
            second=form.state.List.Mods.Single(mod=>mod.Id==second.Id);
            third=form.state.List.Mods.Single(mod=>mod.Id==third.Id);
            form.statuses[Key(first,form.Selected(first))]="Instalado";form.statuses[Key(second,form.Selected(second))]="Baixando";form.statuses[Key(third,form.Selected(third))]="Falhou";
            form.categoryFilters=new HashSet<string>{"Principal"};form.DrawRows();check(form.VisibleMods().Count==2,"Cabeçalho Mods filtra múltiplas categorias.");
            form.statusFilters=new HashSet<string>{"Instalado"};form.DrawRows();check(form.VisibleMods().Single().Id==first.Id,"Categoria e situação são compostas.");
            form.statusFilters=new HashSet<string>{"Download concluído"};form.DrawRows();check(form.VisibleMods().Count==0,"O filtro Download concluído é distinto de Instalado.");
            form.statusFilters=new HashSet<string>{"Instalado"};
            form.variantFilters=new HashSet<string>{"Alternativa"};form.DrawRows();check(form.VisibleMods().Count==0,"Filtro de variante se combina com os anteriores.");
            form.categoryFilters=new HashSet<string>{"Principal","Veículos"};form.variantFilters=null;form.statusFilters=new HashSet<string>{"Baixando","Falha"};form.DrawRows();check(form.VisibleMods().Count==2,"Multisseleção de situações conserva as duas situações escolhidas.");
            form.nameFilter="bet";form.DrawRows();check(form.VisibleMods().Single().Id==second.Id,"Busca do cabeçalho Mod é combinada e não distingue maiúsculas.");
            form.ResetColumnFilters();form.DrawRows();check(form.VisibleMods().Count==3,"Limpar filtros restaura todos os itens do preset.");
            form.measuredMods[first.Id]=50;
            form.measuredMods[second.Id]=900;
            using(var timer=new Timer{Interval=40}){
                timer.Tick+=(s,e)=>{
                    var filter=Application.OpenForms.OfType<GeneralColumnFilterDialog>().FirstOrDefault();
                    if(filter==null)return;
                    timer.Stop();
                    AllControls(filter).OfType<Button>().Single(button=>button.Name=="DefaultOrder").PerformClick();
                };
                timer.Start();
                form.ShowColumnFilter(form.grid.Columns["Size"].Index);
            }
            check(form.ModRows().Select(row=>(string)row.Tag).SequenceEqual(new[]{second.Id,first.Id,third.Id})&&
                !form.grid.Rows.Cast<DataGridViewRow>().Any(row=>row.Tag is SectionRow),
                "Ordenar por espaço cruza as categorias e deixa itens sem medição no fim.");
            form.orderColumn="Variant";form.DrawRows();
            check(form.ModRows().Select(row=>(string)row.Tag).SequenceEqual(new[]{first.Id,third.Id,second.Id}),
                "Ordenar por variante reúne as principais primeiro.");
            form.statuses[Key(third,form.Selected(third))]="Pendente";
            form.orderColumn="Status";form.DrawRows();
            check(form.ModRows().Select(row=>(string)row.Tag).SequenceEqual(new[]{first.Id,second.Id,third.Id}),
                "Ordenar por situação reúne instalados primeiro e pendentes por último.");
            form.statuses[Key(third,form.Selected(third))]="Falhou";
            var extra=new GeneralModVariant{Id="wave",Name="Wave",Sources=new List<string>{archive}};
            first.Variants.Add(extra);
            form.state.ChosenVariants[first.Id]=extra.Id;
            form.groupFilters=new HashSet<string>{first.Name};
            form.DrawRows();
            check(form.VisibleMods().Single().Id==first.Id&&Equals(form.ModRows().Single().Cells["Group"].Tag,"Wave"),
                "O filtro de grupo encontra a gaveta e o marcador reflete a variante ativa.");
            first.Variants.Remove(extra);
            form.state.ChosenVariants.Remove(first.Id);
            form.measuredMods.Clear();
            form.ClearListFilters();
            check(form.orderColumn==null&&form.groupFilters==null&&form.grid.Rows.Cast<DataGridViewRow>()
                .Any(row=>row.Tag is SectionRow&&((SectionRow)row.Tag).Kind=="heading"),
                "A seta de voltar recupera as categorias e limpa a ordenação e o filtro de grupo.");
            form.categoryFilters=new HashSet<string>();form.DrawRows();check(form.VisibleMods().Count==0,"Nenhuma categoria marcada é diferente de selecionar todas.");
            form.ResetColumnFilters();form.DrawRows();
            var fillers=Enumerable.Range(0,157).Select(index=>NewEntry("Componente "+index,archive)).ToList();
            foreach(var item in fillers){
                list.Mods.Add(item);
                list.Presets[0].ModIds.Add(item.Id);
                form.statuses[Key(item,form.Selected(item))]="Instalado";
            }
            form.statuses[Key(second,form.Selected(second))]="Instalado";
            form.marked.Add(second.Id);
            form.collapsedCategories.Add("Principal");
            form.statusFilters=new HashSet<string>{"Instalado"};
            form.SetAssociationSearch("Beta");
            check(form.totalProgress.Value==99&&form.totalProgress.Caption.EndsWith("159 / 160"),
                "A barra mantém a cobertura do preset mesmo com filtros na tabela.");
            var coverageBar=form.totalProgress;
            var clickSegment=typeof(GeneralProgress).GetMethod("OnMouseClick",BindingFlags.Instance|BindingFlags.NonPublic);
            clickSegment.Invoke(coverageBar,new object[]{new MouseEventArgs(MouseButtons.Left,1,coverageBar.Width-7,24,0)});
            check(form.ModRows().Single().Visible&&Equals(form.ModRows().Single().Tag,third.Id)&&
                form.totalProgress.Value==99&&form.searchBox.Text=="",
                "Um clique perto de 1% revela só o mod restante mesmo numa categoria recolhida e com filtros anteriores.");
            form.ClearListFilters();
            var coverageView=(GeneralScrollView)form.grid.Parent.Parent;
            coverageView.ScrollTo(30);
            await Task.Delay(30);
            check(!form.clearFilters.Visible,"Rolar para baixo recolhe as ferramentas antes do clique na barra.");
            clickSegment.Invoke(coverageBar,new object[]{new MouseEventArgs(MouseButtons.Left,1,coverageBar.Width-7,24,0)});
            await Task.Delay(30);
            check(form.clearFilters.Visible&&form.grid.FirstDisplayedScrollingRowIndex==0&&coverageView.Bar.Limit==0,
                "A barra volta ao topo, restaura a seta e oculta a rolagem quando o resultado cabe na tabela.");
            CaptureUi(form,Path.Combine(root,"coverage-filter.png"));
            form.categoryFilters=new HashSet<string>{"Principal"};

            form.variantFilters=new HashSet<string>{"Alternativa"};
            form.statusFilters=new HashSet<string>{"Instalado"};
            form.nameFilter="Beta";
            form.SetAssociationSearch("Beta");
            check(form.clearFilters.Enabled&&form.clearFilters.Left==form.deletePreset.Right,"A seta de limpar filtros fica ao lado da lixeira.");
            form.clearFilters.PerformClick();
            check(!form.HasListFilters&&!form.clearFilters.Enabled&&form.searchBox.Text==""&&
                form.VisibleMods().Count==160&&form.marked.SetEquals(new[]{second.Id})&&form.state.SelectedPreset=="kit",
                "Limpar filtros restaura o preset e preserva os mods marcados.");
            form.marked.Clear();
            form.collapsedCategories.Clear();
            foreach(var item in fillers){
                list.Mods.Remove(item);
                list.Presets[0].ModIds.Remove(item.Id);
                form.statuses.Remove(Key(item,form.Selected(item)));
            }
            form.statuses[Key(second,form.Selected(second))]="Baixando";
            form.DrawRows();
            var origin=form.grid.Columns["Origin"];
            check(origin.DisplayIndex==form.grid.Columns.Count-1&&
                form.ModRows().All(row=>Equals(row.Cells["Origin"].Value,"Mod Loader")&&!String.IsNullOrWhiteSpace(row.Cells["Origin"].ToolTipText)),
                "A origem fica no fim da tabela e explica o selo do Mod Loader.");
            form.gameInventory=new[]{new P2PSyncDifference{Mod="Sacred",Path="archive/pc/mod/sacred.archive",Kind="found"}};
            form.searchTerms=GeneralModSearch.Terms("sacred");form.DrawRows();
            var outside=form.grid.Rows.Cast<DataGridViewRow>().Single(row=>row.Tag is P2PSyncDifference);
            check(outside.ReadOnly&&!(outside.Cells["Mark"] is DataGridViewCheckBoxCell)&&Equals(outside.Cells["Origin"].Value,"?")&&
                !String.IsNullOrWhiteSpace(outside.Cells["Origin"].ToolTipText)&&!form.ModRows().Any(),
                "A busca encontra arquivos fora do catálogo sem incluí-los na seleção de mods.");
            form.statusFilters=new HashSet<string>{"Instalado"};form.DrawRows();
            check(!form.grid.Rows.Cast<DataGridViewRow>().Any(row=>row.Tag is P2PSyncDifference),
                "Arquivos encontrados têm situação própria e não são tratados como mods instalados do projeto.");
            form.gameInventory=new P2PSyncDifference[0];form.ResetColumnFilters();form.DrawRows();
            GeneralModLibrary.AssociateAlternative(first,second);
            GeneralModLibrary.ValidateList(list,true);
            string linked=first.AlternativeGroup;
            var style=list.AlternativeGroups[linked];
            style.Name="Corpo";
            check(style.Color.StartsWith("#"),"Um grupo novo recebe uma cor padrão.");
            form.statuses[Key(second,form.Selected(second))]="Instalado";
            form.DrawRows();
            var badges=form.ModRows().Where(row=>Equals(row.Tag,first.Id)||Equals(row.Tag,second.Id)).Select(row=>row.Cells["Group"]).ToArray();
            check(form.grid.Columns["Group"].Visible&&badges.Length==2&&badges.All(cell=>cell.Value==null),
                "O cabeçalho de grupos volta sem converter vínculos antigos em gavetas.");
            check(form.totalProgress.Caption.EndsWith("1 / 2")&&form.counts.Text.Contains("Instalados: 1"),"Duas alternativas instaladas contam como uma escolha, com outro mod pendente.");

            form.statuses[Key(first,form.Selected(first))]="Não instalado";
            form.statuses[Key(second,form.Selected(second))]="Não instalado";
            form.DrawRows();
            check(form.totalProgress.Caption.EndsWith("0 / 2"),"Duas alternativas pendentes continuam ocupando uma única vaga.");
            form.statuses[Key(second,form.Selected(second))]="Pronto para instalar";
            form.DrawRows();
            check(form.totalProgress.Caption.EndsWith("1 / 2")&&form.counts.Text.Contains("Instalados: 0"),"Um arquivo pronto completa a cobertura do grupo e ainda não conta como instalado.");
            first.CompatibilityGroups.AddRange(new[]{"Base EBB","Refit FemV"});
            first.Variants[0].CompatibilityGroups.Add("Pés padrão");
            first.Tags.Add("teste exclusivo");
            form.SetAssociationSearch("teste exclusivo");
            check(form.VisibleMods().Single().Id==first.Id&&!(form.ModRows().Single().Cells["Name"].ToolTipText??"").Contains("teste exclusivo"),
                "A busca encontra tags manuais sem mostrar as tags na tabela.");
            form.SetAssociationSearch("");
            string groupsExport=Path.Combine(root,"groups.json");
            var groupsLibrary=new GeneralModLibrary(Path.Combine(root,"groups-state.json"));
            groupsLibrary.Export(groupsExport,list);
            var roundTrip=groupsLibrary.Import(groupsExport);
            check(roundTrip.AlternativeGroups[linked].Name=="Corpo"&&roundTrip.AlternativeGroups[linked].Color==style.Color,"O nome e a cor do grupo sobrevivem à exportação.");
            var exported=roundTrip.Mods.Single(mod=>mod.Id==first.Id);
            check(exported.CompatibilityGroups.SequenceEqual(first.CompatibilityGroups)&&
                exported.Variants[0].CompatibilityGroups.SequenceEqual(first.Variants[0].CompatibilityGroups),
                "Grupos adicionais do mod e da versão sobrevivem à exportação e importação.");
            roundTrip.AlternativeGroups[linked].Name=new string('X',19);
            bool rejected=false;
            try{GeneralModLibrary.ValidateList(roundTrip,true);}catch(IOException){rejected=true;}
            check(rejected,"Uma lista importada não aceita nomes de grupo com mais de 18 caracteres.");
            CaptureUi(form,Path.Combine(root,"group-badges.png"));
            for(int i=0;i<70;i++){var item=NewEntry("Extra "+i,archive);list.Mods.Add(item);list.Presets[0].ModIds.Add(item.Id);}
            form.DrawRows();await Task.Delay(80);
            var scroll=(GeneralScrollView)form.grid.Parent.Parent;scroll.RefreshScroll();
            check(form.grid.ScrollBars==ScrollBars.Horizontal&&scroll.Bar.Visible&&scroll.Bar.Limit>0&&form.grid.Controls.OfType<VScrollBar>().All(bar=>!bar.Visible),"A barra vertical própria permanece e a horizontal fica disponível quando as colunas ocupam mais espaço.");
            scroll.Bar.MoveTo(10);check(form.grid.FirstDisplayedScrollingRowIndex==10,"Mover a barra altera a posição da tabela.");
            var tools=form.Controls.Find("TableTools",true).Single();
            await Task.Delay(60);
            check(!tools.Visible,"Rolagem para baixo recolhe busca e ferramentas após o redesenho.");
            check(scroll.Bar.AccessibilityObject.Value=="10","Acessibilidade expõe a posição da barra própria.");scroll.Bar.AccessibilityObject.Value="11";check(form.grid.FirstDisplayedScrollingRowIndex==11,"Acessibilidade pode ajustar a posição da barra.");
            int before=form.grid.FirstDisplayedScrollingRowIndex;scroll.Bar.Wheel(-120);check(form.grid.FirstDisplayedScrollingRowIndex>=before,"Roda do mouse avança a tabela.");
            scroll.Bar.MoveTo(Int32.MaxValue);check(scroll.Bar.Value==scroll.Bar.Limit,"Arraste/limite não passa do fim da tabela.");
            scroll.Bar.MoveTo(0);check(form.grid.FirstDisplayedScrollingRowIndex==0,"Início da barra retorna ao primeiro item.");
            await Task.Delay(60);
            check(tools.Visible,"Voltar ao início restaura busca e ferramentas após o redesenho.");
            CaptureUi(form,Path.Combine(root,"tabela-clara.png"));
            using(var menu=new GeneralActionsMenu()){
                menu.StartPosition=FormStartPosition.Manual;menu.Location=new Point(-30000,-30000);menu.Show(form);Application.DoEvents();
                check(menu.Width==350&&menu.Height==468&&AllControls(menu).OfType<Button>().Count(button=>button.Name!="")==7,"Menu em coluna reúne listas, compartilhar e opções.");
                CaptureUi(menu,Path.Combine(root,"menu-unico.png"));menu.Close();
            }
            using(var editor=new GeneralCategoryDetails("Principal","",false,true)){
                editor.StartPosition=FormStartPosition.Manual;editor.Location=new Point(-30000,-30000);editor.Show(form);Application.DoEvents();
                check(AllControls(editor).OfType<Button>().Any(button=>button.Text=="Excluir categoria"),"Editar categoria existente oferece excluir.");CaptureUi(editor,Path.Combine(root,"categoria.png"));editor.Close();
            }
            using(var field=new GeneralDialog("Rolagem própria") {ClientSize=new Size(620,340)}){
                var text=new TextBox{Multiline=true,Text=String.Join("\r\n",Enumerable.Range(0,100).Select(i=>"Linha "+i))};
                var view=new GeneralScrollView(text){Dock=DockStyle.Fill};field.Content.Controls.Add(view);field.StartPosition=FormStartPosition.Manual;field.Location=new Point(-30000,-30000);field.Show(form);await Task.Delay(60);view.RefreshScroll();
                check(view.Bar.Visible&&text.ScrollBars==ScrollBars.None,"Texto longo usa a barra própria sem setas nativas.");view.ScrollTo(30);check(view.Bar.Value>0,"Barra própria rola texto de edição.");field.Close();
            }
            foreach(bool dark in new[]{false,true}){
                GeneralStyle.Dark=dark;using(var bitmap=new Bitmap(300,180))using(var graphics=Graphics.FromImage(bitmap)){
                    graphics.Clear(Color.Magenta);
                    using(var shape=GeneralStyle.PopupShape(bitmap.Size))using(var clip=new Region(shape))graphics.Clip=clip;
                    GeneralStyle.PaintCard(graphics,bitmap.Size);
                    check(bitmap.GetPixel(0,0).ToArgb()==Color.Magenta.ToArgb(),"Máscara revela o fundo no canto: "+dark);
                    check(bitmap.GetPixel(150,90).ToArgb()==GeneralStyle.Surface.ToArgb(),"Pintura limpa a superfície central: "+dark);
                    check(bitmap.GetPixel(150,0).ToArgb()==GeneralStyle.Line.ToArgb(),"Contorno coincide com o recorte da janela: "+dark);
                }
            }
            GeneralStyle.Dark=false;
            check(File.ReadAllText(archive).Length>0,"Filtros/presets não removem o arquivo fonte.");
        });
        GeneralStyle.Dark=false;GeneralStyle.Language="pt";
        Safe.WriteJson(Path.Combine(root,"resultado-redesign.json"),new{Passed=true,Tests=checks,RealGameTouched=false,NetworkUsed=false});
    }
}
