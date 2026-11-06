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
        string root=Path.Combine(parent,"search-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
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
            form.statuses[Key(a,form.Selected(a))]="Instalado";form.statuses[Key(b,form.Selected(b))]="Falhou";form.DrawRows();form.Persist();string original=Safe.Hash(Path.Combine(form.data,"lista.json"));string projectBefore=Safe.Json.Serialize(state.List);
            form.marked.Add(b.Id);
            form.searchBox.Text="wlofcut feminina concluido";await Task.Delay(220);
            check(form.VisibleMods().Single()==a,"The native search field combines name typo, variant and visible status.");
            check(original==Safe.Hash(Path.Combine(form.data,"lista.json")),"Typing a query does not save or replace the project.");
            check(form.ModRows().Count()==1&&form.marked.Contains(b.Id),"Filtering hides unmatched rows and preserves hidden checked items.");
            form.categoryFilters=new HashSet<string>{b.Category};form.DrawRows();check(form.VisibleMods().Count==0,"Search composes with category filters.");
            check(form.grid.Rows.Cast<DataGridViewRow>().Any(row=>row.Tag is SectionRow&&((SectionRow)row.Tag).Kind=="empty"),"No results has an explicit empty-state message.");
            form.categoryFilters=null;form.searchBox.Text="falha";await Task.Delay(220);check(form.VisibleMods().Single()==b,"Search finds failed status.");
            form.searchBox.Text="nao baixado";await Task.Delay(220);check(form.VisibleMods().Single()==c,"Search finds not-downloaded status.");
            state.Language="en";form.BuildInterface();form.searchBox.Text="download complete";await Task.Delay(220);check(form.VisibleMods().Single()==a,"Status is searchable in the active English language.");
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
            check(Safe.Json.Serialize(state.List)==projectBefore,"Search/filter/expand preserve names, categories and preset members.");
            form.optionsButton.PerformClick();var menu=(GeneralActionsMenu)form.menus.Current;
            check(AllControls(menu).OfType<Button>().Any(button=>button.Name=="home"),"The real project menu offers Back to main menu.");form.menus.Close();
        });
        GeneralStyle.UseTheme("light");GeneralStyle.Language="pt";
        Safe.WriteJson(Path.Combine(root,"resultado-project-search.json"),new{Passed=true,Tests=checks,RealGameTouched=false,NetworkUsed=false});
    }
    internal static void RunRedesignTests(string parent){
        string root=Path.Combine(parent,"redesign-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
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
            form.statuses[Key(first,form.Selected(first))]="Instalado";form.statuses[Key(second,form.Selected(second))]="Baixando";form.statuses[Key(third,form.Selected(third))]="Falhou";
            form.categoryFilters=new HashSet<string>{"Principal"};form.DrawRows();check(form.VisibleMods().Count==2,"Cabeçalho Mods filtra múltiplas categorias.");
            form.statusFilters=new HashSet<string>{"Download concluído"};form.DrawRows();check(form.VisibleMods().Single()==first,"Categoria e situação são compostas.");
            form.variantFilters=new HashSet<string>{"Alternativa"};form.DrawRows();check(form.VisibleMods().Count==0,"Filtro de variante se combina com os anteriores.");
            form.categoryFilters=new HashSet<string>{"Principal","Veículos"};form.variantFilters=null;form.statusFilters=new HashSet<string>{"Baixando","Falha"};form.DrawRows();check(form.VisibleMods().Count==2,"Multisseleção de situações conserva as duas situações escolhidas.");
            form.nameFilter="bet";form.DrawRows();check(form.VisibleMods().Single()==second,"Busca do cabeçalho Mod é combinada e não distingue maiúsculas.");
            form.ResetColumnFilters();form.DrawRows();check(form.VisibleMods().Count==3,"Limpar filtros restaura todos os itens do preset.");
            form.categoryFilters=new HashSet<string>();form.DrawRows();check(form.VisibleMods().Count==0,"Nenhuma categoria marcada é diferente de selecionar todas.");
            form.ResetColumnFilters();form.DrawRows();
            for(int i=0;i<70;i++){var item=NewEntry("Extra "+i,archive);list.Mods.Add(item);list.Presets[0].ModIds.Add(item.Id);}
            form.DrawRows();await Task.Delay(80);
            var scroll=(GeneralScrollView)form.grid.Parent.Parent;scroll.RefreshScroll();
            check(form.grid.ScrollBars==ScrollBars.None&&scroll.Bar.Visible&&scroll.Bar.Limit>0,"Tabela usa barra própria, sem rolagem nativa sobreposta.");
            scroll.Bar.MoveTo(10);check(form.grid.FirstDisplayedScrollingRowIndex==10,"Mover a barra altera a posição da tabela.");
            check(scroll.Bar.AccessibilityObject.Value=="10","Acessibilidade expõe a posição da barra própria.");scroll.Bar.AccessibilityObject.Value="11";check(form.grid.FirstDisplayedScrollingRowIndex==11,"Acessibilidade pode ajustar a posição da barra.");
            int before=form.grid.FirstDisplayedScrollingRowIndex;scroll.Bar.Wheel(-120);check(form.grid.FirstDisplayedScrollingRowIndex>=before,"Roda do mouse avança a tabela.");
            scroll.Bar.MoveTo(Int32.MaxValue);check(scroll.Bar.Value==scroll.Bar.Limit,"Arraste/limite não passa do fim da tabela.");
            scroll.Bar.MoveTo(0);check(form.grid.FirstDisplayedScrollingRowIndex==0,"Início da barra retorna ao primeiro item.");
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
                    graphics.Clear(Color.Magenta);GeneralStyle.PaintCard(graphics,bitmap.Size);
                    check(bitmap.GetPixel(0,0).ToArgb()==GeneralStyle.Surface.ToArgb(),"Pintura limpa o canto opaco: "+dark);
                    check(bitmap.GetPixel(150,90).ToArgb()==GeneralStyle.Surface.ToArgb(),"Pintura limpa a superfície central: "+dark);
                    check(bitmap.GetPixel(150,0).ToArgb()!=Color.Magenta.ToArgb(),"Borda não conserva pixels da pintura anterior: "+dark);
                }
            }
            GeneralStyle.Dark=false;
            check(File.ReadAllText(archive).Length>0,"Filtros/presets não removem o arquivo fonte.");
        });
        GeneralStyle.Dark=false;GeneralStyle.Language="pt";
        Safe.WriteJson(Path.Combine(root,"resultado-redesign.json"),new{Passed=true,Tests=checks,RealGameTouched=false,NetworkUsed=false});
    }
}
