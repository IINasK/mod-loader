using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    internal static void RunPresetIsolationTests(string parent){
        string root=Path.Combine(parent,"preset-clean-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var list=new GeneralModList{Categories=new List<string>{"Jogabilidade","Lojas","Veículos","Aparência"}};
        var item=NewEntry("Existente","https://example.com/mod.zip");item.Category="Lojas";list.Mods.Add(item);
        var original=new GeneralModPreset{Id="old",Name="Original",ModIds=new List<string>{item.Id}};list.Presets.Add(original);
        var state=new GeneralModState{List=list,SelectedPreset="old",Destination=root,DownloadFolder=root};
        var checks=new List<string>();Action<bool,string> check=(yes,message)=>{if(!yes)throw new IOException(message);checks.Add(message);};
        TestForm(FixedData.Load(),state,Path.Combine(root,"state"),root,async form=>{
            check(form.PresetCategories().SequenceEqual(new[]{"Lojas"}),"Preset legado conserva a categoria de seus mods e não herda grupos vazios.");
            using(var timer=new Timer{Interval=60}){
                timer.Tick+=(s,e)=>{var dialog=Application.OpenForms.OfType<GeneralNameDialog>().FirstOrDefault();if(dialog==null)return;timer.Stop();AllControls(dialog).OfType<TextBox>().Single(box=>box.Name=="NameInput").Text="Meu preset novo";AllControls(dialog).OfType<Button>().Single(button=>button.Name=="SaveName").PerformClick();};
                timer.Start();form.SavePresetSelection(true);
            }
            var active=form.ActivePreset;
            check(active!=null&&active.Name=="Meu preset novo"&&active.ModIds.Count==0&&active.Categories.Count==0,"Criar preset pelo menu começa vazio, sem categorias sugeridas.");
            check(!form.PresetCategories().Any()&&form.grid.Rows.Cast<DataGridViewRow>().Count(row=>row.Tag is SectionRow&&((SectionRow)row.Tag).Kind=="heading")==1,"Só o grupo neutro Sem categoria aparece no preset vazio.");
            var scroll=(GeneralScrollView)form.grid.Parent.Parent;scroll.RefreshScroll();check(!scroll.Bar.Visible,"Preset vazio não mantém uma barra de rolagem desnecessária após trocar a tabela.");
            CaptureUi(form,Path.Combine(root,"preset-novo-vazio.png"));
            list.Categories.Add("Minhas escolhas");active.Categories.Add("Minhas escolhas");form.Persist();form.DrawRows();
            check(form.PresetCategories().SequenceEqual(new[]{"Minhas escolhas"})&&form.grid.Rows.Cast<DataGridViewRow>().Any(row=>row.Tag is SectionRow&&((SectionRow)row.Tag).Kind=="add"&&((SectionRow)row.Tag).Category=="Minhas escolhas"),"Categoria vazia criada pelo usuário permanece com Adicionar item.");
            var added=NewEntry("Meu item","https://example.com/new.zip");added.Category="Minhas escolhas";form.ApplyEdited(added,added,"default",false);
            check(active.ModIds.SequenceEqual(new[]{added.Id})&&original.ModIds.SequenceEqual(new[]{item.Id}),"Adicionar item afeta só os membros do preset ativo.");
            string exported=Path.Combine(root,"lista.json");form.library.Export(exported,list);var imported=form.library.Import(exported);
            check(imported.Presets.Single(p=>p.Id==active.Id).Categories.SequenceEqual(new[]{"Minhas escolhas"}),"Categorias explícitas do preset acompanham exportar/importar.");
            var restored=new GeneralModLibrary(Path.Combine(root,"state","lista.json")).Load();
            check(restored.List.Presets.Single(p=>p.Id==active.Id).Categories.SequenceEqual(new[]{"Minhas escolhas"}),"Categoria escolhida persiste após reabrir.");
            state.SelectedPreset="old";form.RebuildPresets();check(form.PresetCategories().SequenceEqual(new[]{"Lojas"}),"Trocar de preset não mostra a categoria vazia do outro.");
            state.SelectedPreset=null;form.RebuildPresets();check(form.PresetCategories().Count()==5,"Todos os itens mantém acesso à organização completa.");
            GeneralModLibrary.DeleteCategory(list,"Minhas escolhas");form.DrawRows();check(active.Categories.Count==0&&added.Category==null&&active.ModIds.Contains(added.Id),"Excluir categoria limpa sua referência nos presets e preserva os mods.");
            foreach(bool dark in new[]{false,true}){
                state.Theme=dark?"dark":"light";form.BuildInterface();form.PerformLayout();await Task.Delay(60);
                check(AllControls(form).OfType<ModLoaderLogo>().Count()==1&&form.Icon!=null,"Logo vetorial e ícone aparecem no cabeçalho no tema "+state.Theme);
                var heading=AllControls(form).Single(control=>control.Name=="PresetHeading");
                using(var bitmap=new Bitmap(heading.Width,heading.Height)){
                    heading.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));int y=heading.Height-6;
                    check(bitmap.GetPixel(20,y).ToArgb()==GeneralStyle.Line.ToArgb()&&bitmap.GetPixel(heading.Width-21,y).ToArgb()==GeneralStyle.Line.ToArgb(),"Separador de título forte e contínuo no tema "+state.Theme);
                    check(bitmap.GetPixel(0,y).ToArgb()==GeneralStyle.Surface.ToArgb()&&bitmap.GetPixel(heading.Width-1,y).ToArgb()==GeneralStyle.Surface.ToArgb(),"Separador tem recuo nas duas bordas no tema "+state.Theme);
                }
                check(GeneralStyle.Line.ToArgb()!=GeneralStyle.SoftLine.ToArgb(),"Separador do título difere das divisões de categorias no tema "+state.Theme);
                form.updateAvailable=new GeneralUpdateRelease{Version="1.1.10",Page="https://github.com/"+GeneralUpdateChecker.Repository+"/releases/tag/v1.1.10"};form.ShowUpdateNotice();
                check(form.updateNotice.Visible&&form.updateNotice.Text.Contains("1.1.10"),"Aviso de atualização aparece sem janela bloqueante no tema "+state.Theme);
                string opened=null;form.launch=url=>opened=url;
                typeof(LinkLabel).GetMethod("OnLinkClicked",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(form.updateNotice,new object[]{new LinkLabelLinkClickedEventArgs(new LinkLabel.Link())});
                check(opened==form.updateAvailable.Page,"Clique do aviso abre somente a Release oficial sem executar arquivos no tema "+state.Theme);
                CaptureUi(form,Path.Combine(root,"table-"+state.Theme+".png"));
            }
            state.SelectedPreset=active.Id;form.RebuildPresets();int count=list.Categories.Count;
            CompleteCategoryName("Jogabilidade",()=>form.EditCategory(null,true));
            check(list.Categories.Count==count&&active.Categories.Contains("Jogabilidade"),"Criar categoria com nome de outro preset reutiliza o nome sem duplicação nem erro.");
            CompleteCategoryName("Meu jogo",()=>form.EditCategory("Jogabilidade",false));
            check(!active.Categories.Contains("Jogabilidade")&&active.Categories.Contains("Meu jogo")&&list.Categories.Contains("Meu jogo"),"Renomear categoria atualiza referências explícitas nos presets.");
        });
        GeneralStyle.Dark=false;GeneralStyle.Language="pt";
        var invalid=new GeneralModList{Presets=new List<GeneralModPreset>{new GeneralModPreset{Id="bad",Name="Bad",Categories=new List<string>{"Inexistente"}}}};
        bool rejected=false;try{GeneralModLibrary.ValidateList(invalid,true);}catch(IOException){rejected=true;}check(rejected,"Importação recusa referência a categoria inexistente.");
        Safe.WriteJson(Path.Combine(root,"resultado-preset-clean.json"),new{Passed=true,Tests=checks,RealGameTouched=false,NetworkUsed=false});
    }
    static void CompleteCategoryName(string name,Action show){
        using(var timer=new Timer{Interval=60}){
            timer.Tick+=(s,e)=>{var dialog=Application.OpenForms.OfType<GeneralCategoryDetails>().FirstOrDefault();if(dialog==null)return;timer.Stop();AllControls(dialog).OfType<TextBox>().Single(box=>!box.Multiline).Text=name;((Button)dialog.AcceptButton).PerformClick();};
            timer.Start();show();
        }
    }
}
