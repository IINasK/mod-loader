using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
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
                check(menu.Width==620&&menu.Height==230&&AllControls(menu).OfType<Button>().Count(button=>button.Name!="")==4,"Menu compacto reúne apenas as quatro ações do projeto.");
                CaptureUi(menu,Path.Combine(root,"menu-unico.png"));menu.Close();
            }
            using(var packages=new GeneralActionsMenu(true)){
                check(AllControls(packages).OfType<Button>().Count(button=>button.Name=="cyberpunk")==1,"Pacotes oferece o pacote Cyberpunk separadamente.");
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
