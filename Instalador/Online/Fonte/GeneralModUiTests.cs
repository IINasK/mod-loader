using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    internal static void RunUiTests(string parent){
        string root=Path.Combine(parent,"ui-v2-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string target=Path.Combine(root,"game");Directory.CreateDirectory(target);
        string archive=TestZip(root,"test.zip","mods/test.txt","sample");
        var one=NewEntry("Mod sem categoria",archive);
        var two=NewEntry("Mod principal",archive);two.Category="Principal";
        var three=NewEntry("Outro mod",archive);three.Category="Principal";three.Nsfw=true;three.Description="Descrição opcional de exemplo";
        var list=new GeneralModList{Categories=new List<string>{"Principal","Veículos"},Mods=new List<GeneralModEntry>{one,two,three}};
        list.CategoryDescriptions["Principal"]="Componentes que o criador deste pack considera essenciais.";
        list.Presets.Add(new GeneralModPreset{Id="example",Name="Preset de exemplo",ModIds=new List<string>{two.Id}});
        var saved=new GeneralModState{List=list,Destination=target,DownloadFolder=root};
        var checks=new List<string>();Action<bool,string> check=(yes,message)=>{if(!yes)throw new IOException(message);checks.Add(message);};
        TestForm(FixedData.Load(),saved,Path.Combine(root,"state"),root,async form=>{
            form.ClientSize=new Size(1100,760);form.PerformLayout();Application.DoEvents();
            check(((SectionRow)form.grid.Rows[0].Tag).Kind=="heading"&&((SectionRow)form.grid.Rows[0].Tag).Category==null,"Sem categoria é a primeira seção.");
            check(form.ModRows().Select(r=>(string)r.Tag).SequenceEqual(new[]{one.Id,two.Id,three.Id}),"Linhas de mods preservam ordem por categoria.");
            check(!form.grid.Columns.Contains("Category")&&form.grid.Columns["Progress"].Visible==false,"Categoria vira seção e as duas barras ficam no rodapé.");
            check(form.grid.Rows.Cast<DataGridViewRow>().Count(r=>r.Tag is SectionRow&&((SectionRow)r.Tag).Kind=="add")==3,"Cada categoria, inclusive vazia, oferece Adicionar item.");
            check(((SectionRow)form.grid.Rows[form.grid.RowCount-1].Tag).Kind=="new","A lista termina com Criar nova categoria.");
            check(form.preset.Height==44&&form.presetSettings.PointToScreen(Point.Empty).Y==form.preset.PointToScreen(Point.Empty).Y,"Seletor e salvar preset ficam alinhados na mesma linha.");
            check(((GeneralButton)form.optionsButton).Bare&&form.optionsButton.Text==""&&((GeneralButton)form.optionsButton).IconKind=="menu","Opções usa apenas ícone de menu sem moldura.");
            check(!AllControls(form).OfType<Button>().Any(button=>button.Text=="Projetos"||button.Text=="Projects")&&form.optionsButton.Parent.Controls.OfType<Button>().Count()==1,"Cabeçalho tem só o hamburger, sem seletor Projetos.");
            check(form.destination.Parent.Height==44,"Campo da pasta mantém altura compacta alinhada ao botão.");
            check(form.Font.FontFamily.Name.IndexOf("Jost",StringComparison.OrdinalIgnoreCase)>=0,"A fonte Jost está embutida e em uso.");
            var click=new DataGridViewCellMouseEventArgs(0,-1,1,1,new MouseEventArgs(MouseButtons.Left,1,1,1,0));
            typeof(DataGridView).GetMethod("OnColumnHeaderMouseClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form.grid,new object[]{click});
            check(form.marked.Count==3&&form.Marked().Count==3,"Selecionar todos ignora títulos e divisões.");
            form.categoryFilters=new HashSet<string>{"Principal"};form.DrawRows();
            check(form.ModRows().Count()==2&&form.Marked().Count==2,"Filtro de categoria e seleção agem apenas sobre mods visíveis.");
            form.categoryFilters=null;form.DrawRows();form.preset.SelectedIndex=1;
            check(form.ModRows().Count()==1&&(string)form.ModRows().Single().Tag==two.Id,"O preset filtra itens independentemente das categorias.");
            form.preset.SelectedIndex=0;
            CaptureUi(form,Path.Combine(root,"claro-1100.png"));
            form.ClientSize=new Size(1440,900);form.PerformLayout();Application.DoEvents();CaptureUi(form,Path.Combine(root,"claro-1440.png"));
            using(var menu=new GeneralActionsMenu()){
                menu.StartPosition=FormStartPosition.Manual;menu.Location=new Point(-30000,-30000);menu.Show(form);Application.DoEvents();
                check(AllControls(menu).OfType<Button>().Where(button=>button.Name!="").Select(button=>button.Name).OrderBy(name=>name).SequenceEqual(new[]{"export","home","import","new","open","options","share"}),"Menu reúne listas, compartilhar e opções em uma coluna.");
                CaptureUi(menu,Path.Combine(root,"menu-principal.png"));menu.Close();
                check(!menu.Visible,"Menu fecha sem alterar a lista.");
            }
            using(var editor=new GeneralModEditor(NewEntry("",null),list.Categories,"default",true,"Arquivo do usuário")){
                editor.StartPosition=FormStartPosition.Manual;editor.Location=new Point(-30000,-30000);editor.Show(form);Application.DoEvents();
                var button=(Button)typeof(GeneralModEditor).GetField("install",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(editor);
                check(!button.Enabled,"Instalar começa desabilitado com campos obrigatórios vazios.");
                var name=(TextBox)typeof(GeneralModEditor).GetField("modName",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(editor);
                var sources=(TextBox)typeof(GeneralModEditor).GetField("sources",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(editor);
                name.Text="Exemplo válido";sources.Text=archive;
                check(button.Enabled,"Instalar habilita quando nome e fonte são válidos.");
                check(button.Bottom<=button.Parent.Height&&button.Right<=button.Parent.Width,"Ações do editor ficam dentro do popup.");
                CaptureUi(editor,Path.Combine(root,"editor-claro.png"));editor.Close();
            }
            string dropped=TestZip(root,"dropped.zip","mods/drop.txt","dropped");
            using(var editor=new GeneralModEditor(NewEntry("nome-do-arquivo",dropped),list.Categories,"default",true,"")){
                editor.OfferAttachment(three);
                var bind=(CheckBox)typeof(GeneralModEditor).GetField("usePending",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(editor);
                bind.Checked=true;
                var name=(TextBox)typeof(GeneralModEditor).GetField("modName",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(editor);
                var description=(TextBox)typeof(GeneralModEditor).GetField("description",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(editor);
                var adult=(CheckBox)typeof(GeneralModEditor).GetField("nsfw",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(editor);
                check(name.Text==three.Name&&description.Text==three.Description&&adult.Checked,"Vincular arquivo preserva nome, descrição e NSFW do mod de destino.");
                bind.Checked=false;
                check(name.Text=="nome-do-arquivo"&&!adult.Checked,"Voltar à importação independente restaura os metadados do arquivo.");
            }
            form.categoryFilters=new HashSet<string>{"Principal"};form.DrawRows();
            bool edited=false;
            using(var timer=new Timer{Interval=40}){
                timer.Tick+=(s,e)=>{
                    var editor=Application.OpenForms.OfType<GeneralModEditor>().FirstOrDefault();if(editor==null)return;
                    edited=true;timer.Stop();
                    var name=(TextBox)typeof(GeneralModEditor).GetField("modName",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(editor);name.Text="Arquivo categorizado";
                    ((CheckBox)typeof(GeneralModEditor).GetField("nsfw",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(editor)).Checked=true;
                    ((Button)typeof(GeneralModEditor).GetField("save",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(editor)).PerformClick();
                };
                timer.Start();form.Drop(new[]{dropped});await Until(()=>edited&&!form.scanning);
            }
            var added=form.state.List.Mods.Single(m=>m.Name=="Arquivo categorizado");
            check(added.Category=="Principal"&&added.Nsfw,"Arrastar abre edição, herda a categoria e salva NSFW.");
            check(!Directory.Exists(Path.Combine(target,"mods")),"Salvar o arquivo arrastado não instala sem autorização.");
            form.state.Theme="dark";form.state.Language="en";form.Persist();form.BuildInterface();form.ClientSize=new Size(1100,760);form.PerformLayout();Application.DoEvents();
            check(form.grid.Columns["Status"].HeaderText=="Status"&&form.preset.Items[0].ToString()=="All items","Idioma inglês alcança a tabela e os filtros.");
            check(form.BackColor==GeneralStyle.Background&&GeneralStyle.Dark,"Tema escuro atualiza toda a janela.");
            check(GeneralStyle.Status("Instalado")=="Installed"&&GeneralStyle.Status("Pulado")=="Skipped","Estados têm rótulos em inglês.");
            check(!form.next.Visible,"Pular fica oculto em repouso após trocar tema e idioma.");
            var reloaded=form.library.Load();check(reloaded.Theme=="dark"&&reloaded.Language=="en","Tema e idioma são salvos no computador.");
            CaptureUi(form,Path.Combine(root,"escuro-1100.png"));
            using(var editor=new GeneralModEditor(added,list.Categories,"default",true,"Destination: "+target)){
                editor.StartPosition=FormStartPosition.Manual;editor.Location=new Point(-30000,-30000);editor.Show(form);Application.DoEvents();
                CaptureUi(editor,Path.Combine(root,"editor-escuro.png"));editor.Close();
            }
            using(var menu=new GeneralActionsMenu())check(AllControls(menu).OfType<Button>().Single(button=>button.Name=="new").Text=="New project","Menu de listas também muda de idioma.");
        });
        GeneralStyle.Dark=false;GeneralStyle.Language="pt";
        Safe.WriteJson(Path.Combine(root,"resultado-ui-v2.json"),new{Passed=true,Tests=checks,RealGameTouched=false,NetworkUsed=false});
    }
    static void CaptureUi(Form form,string path){using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png);}}
}

