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
        string root=OnlineTests.NestedRoot(parent,"ui-v2-");
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
            check(form.projectsButton.Right<=form.homeButton.Left&&form.homeButton.Right<=form.optionsButton.Left&&form.optionsButton.Parent.Controls.OfType<Button>().Count()==3,"Projetos, voltar e hamburger aparecem nesta ordem no cabeçalho.");
            check(!AllControls(form).OfType<TextBox>().Any(box=>box.AccessibleName=="Pasta de instalação"),"A pasta de instalação fica somente em Opções.");
            using(var options=new GeneralOptionsDialog("light","pt",root,null,target)){
                options.Show(form);Application.DoEvents();
                var folder=AllControls(options).OfType<TextBox>().Single(box=>box.Name=="OptionsInstallationFolder");
                check(folder.Text==target&&folder.Parent.Height==44,"Opções mostra o destino do projeto em um campo alinhado.");
            }
            check(form.currentProgress.Top==form.totalProgress.Top&&Math.Abs(form.currentProgress.Width-form.totalProgress.Width)<=1,"As barras do rodapé dividem a mesma linha em partes iguais.");
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
                check(AllControls(menu).OfType<Button>().Where(button=>button.Name!="").Select(button=>button.Name).OrderBy(name=>name).SequenceEqual(new[]{"clone","export","import","new","open","options","share"}),"Menu reúne listas, compartilhar e opções em uma coluna.");
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
            await TestAlternativePicking(form,one,two,three,root,check);
            TestSyncRemovalReview(form,root,check);
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
            form.categoryFilters=null;
            await TestPrimaryActions(form,one,two,archive,root,check);
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
    static async Task TestAlternativePicking(GeneralModWindow form,GeneralModEntry source,GeneralModEntry presetEntry,
        GeneralModEntry candidate,string root,Action<bool,string> check){
        form.preset.SelectedIndex=1;
        form.SetAssociationSearch(presetEntry.Name);
        var originalMarks=form.marked.ToArray();
        int stage=0;
        Exception failure=null;
        GeneralModEditor opened=null;
        bool previousAnimation=GeneralStyle.AnimationsEnabled;
        GeneralStyle.AnimationsEnabled=true;
        try {
            using(var timer=new Timer{Interval=280}){
                timer.Tick+=(s,e)=>{
                    try {
                        var editor=Application.OpenForms.OfType<GeneralModEditor>().FirstOrDefault(item=>item.Visible);
                        if(failure!=null){
                            if(editor!=null){timer.Stop();editor.Complete(DialogResult.Cancel);}
                            return;
                        }
                        if(stage==0&&editor!=null){
                            opened=editor;
                            var draft=AllControls(editor).OfType<TextBox>().Single(box=>box.AccessibleName=="Descrição");
                            draft.Text="Texto ainda não salvo";
                            var scroll=AllControls(editor).OfType<GeneralScrollView>().Single(control=>control.Name=="ModEditorScroll");
                            var archive=AllControls(editor).OfType<TextBox>().Single(box=>box.AccessibleName!=null&&box.AccessibleName.StartsWith("Links ou arquivos"));
                            var viewport=scroll.RectangleToScreen(scroll.ClientRectangle);
                            var field=archive.RectangleToScreen(archive.ClientRectangle);
                            CaptureUi(editor,Path.Combine(root,"editor-identification.png"));
                            if(field.Bottom>viewport.Bottom)scroll.ScrollTo(field.Bottom-viewport.Bottom+8);
                            field=archive.RectangleToScreen(archive.ClientRectangle);
                            check(Rectangle.Intersect(field,viewport).Height==field.Height&&archive.Height>=58,
                                "A rolagem revela toda a fonte de download com altura legível.");
                            scroll.ScrollTo(9999);
                            Application.DoEvents();
                            CaptureUi(editor,Path.Combine(root,"editor-download-and-link.png"));
                            stage=1;
                            AllControls(editor).OfType<Button>().Single(button=>button.Name=="AlternativeMod").PerformClick();
                        }else if(stage==1&&form.associationChoice!=null){
                            check(form.listExpanded&&!form.optionsButton.Visible&&!form.expandList.Visible&&form.grid.ReadOnly&&
                                !form.grid.Columns["Mark"].Visible&&form.grid.ContextMenuStrip==null,"A lista real expande e bloqueia as ações durante o vínculo.");
                            check(new HashSet<string>(form.ModRows().Select(row=>(string)row.Tag)).SetEquals(new[]{presetEntry.Id,candidate.Id}),
                                "A escolha ignora o preset atual e exclui o próprio mod.");
                            var confirm=AllControls(form).OfType<Button>().Single(button=>button.Name=="ConfirmAlternativeSelection");
                            check(!confirm.Enabled,"Sem um mod selecionado, OK fica desabilitado.");
                            form.SetAssociationSearch(candidate.Name);
                            var candidateRow=form.ModRows().Single();
                            form.grid.CurrentCell=candidateRow.Cells["Name"];
                            candidateRow.Selected=true;
                            check(confirm.Enabled,"A linha encontrada pela busca pode ser confirmada.");
                            CaptureUi(form,Path.Combine(root,"editor-alternative-list.png"));
                            stage=2;
                            confirm.PerformClick();
                        }else if(stage==2&&editor!=null){
                            check(Object.ReferenceEquals(opened,editor)&&editor.LinkedAlternative==candidate&&
                                AllControls(editor).OfType<TextBox>().Single(box=>box.AccessibleName=="Descrição").Text=="Texto ainda não salvo"&&
                                (editor.Region==null||editor.Region.IsVisible(editor.Width/2,editor.Height/2)),"O mesmo editor reaparece com o vínculo e o rascunho intactos.");
                            check(!form.listExpanded&&form.grid.Columns["Mark"].Visible&&!form.grid.ReadOnly&&
                                form.searchBox.Text==presetEntry.Name&&form.marked.SetEquals(originalMarks)&&form.state.SelectedPreset=="example",
                                "O modo de escolha restaura busca, preset, caixas e navegação.");
                            stage=3;
                            AllControls(editor).OfType<Button>().Single(button=>button.Name=="AlternativeMod").PerformClick();
                        }else if(stage==3&&form.associationChoice!=null){
                            stage=4;
                            AllControls(form).OfType<Button>().Single(button=>button.Name=="CancelAlternativeSelection").PerformClick();
                        }else if(stage==4&&editor!=null){
                            check(editor.LinkedAlternative==candidate&&source.AlternativeGroup==null&&candidate.AlternativeGroup==null,
                                "Cancelar a escolha preserva o vínculo em rascunho e não grava a lista.");
                            stage=5;
                            timer.Stop();
                            editor.Complete(DialogResult.Cancel);
                        }
                    }catch(Exception ex){
                        failure=new IOException("Alternative picking stage "+stage,ex);
                        if(form.associationChoice!=null)form.associationChoice.TrySetResult(null);
                        else if(opened!=null&&opened.Visible){timer.Stop();opened.Complete(DialogResult.Cancel);}
                    }
                };
                timer.Start();
                await form.EditMod(source);
            }
            if(failure!=null)throw failure;
            check(stage==5,"O fluxo conclui escolha, volta ao editor e cancela uma segunda seleção.");
        } finally {
            GeneralStyle.AnimationsEnabled=previousAnimation;
            form.SetAssociationSearch("");
            form.preset.SelectedIndex=0;
        }
    }

    static async Task TestPrimaryActions(GeneralModWindow form,GeneralModEntry first,GeneralModEntry second,
        string archive,string root,Action<bool,string> check){
        form.state.List.Mods.Add(NewEntry("Download pendente","https://example.test/missing.zip"));
        var remote=form.state.List.Mods.Last();
        var replacement=NewEntry("Alternativa local",TestZip(root,"alternative.zip","mods/test.txt","alternative"));
        GeneralModLibrary.AssociateAlternative(replacement,first);
        form.state.List.Mods.Add(replacement);
        var original=form.MakeWork(first);
        var incoming=form.MakeWork(replacement);
        string target=Path.Combine(form.state.Destination,"mods","test.txt");
        File.WriteAllText(Safe.In(form.state.Destination,".fixture-only"),"fixture");
        form.Generic(form.state.Destination).Install(archive,original.Receipt(0),null);
        await form.Scan();
        Action<string[]> select=ids=>{
            form.marked.Clear();
            form.marked.UnionWith(ids);
            form.DrawRows();
        };
        select(new[]{first.Id});
        check(form.primaryAction.Text=="Desabilitar","Instalado oferece Desabilitar.");
        select(new[]{first.Id,second.Id});
        check(form.primaryAction.Text=="Instalar","Arquivo local pendente tem prioridade sobre desabilitação.");
        select(new[]{first.Id,second.Id,remote.Id});
        List<Work> queued;
        check(form.primaryAction.Text=="Baixar"&&form.ClassifyJobs(form.SelectedJobs(),out queued)==ModAction.Download&&
            queued.Select(work=>work.Entry.Id).OrderBy(id=>id).SequenceEqual(new[]{second.Id,remote.Id}.OrderBy(id=>id)),
            "Um download pendente ganha prioridade e a fila exclui o mod já instalado.");
        select(new[]{replacement.Id});
        check(form.primaryAction.Text=="Mudar versão","A alternativa do mod ativo oferece Mudar versão.");
        incoming.SwitchAlternatives=true;
        await form.RunWork(incoming,form.state.Destination,false);
        var store=form.DisabledAt(form.state.Destination);
        check(File.ReadAllText(target)=="alternative"&&store.Read().ContainsKey(original.Key)&&form.Status(incoming,form.state.Destination)=="Instalado",
            "Troca guarda a versão anterior e instala a nova mesmo quando usam o mesmo caminho.");
        original.SwitchAlternatives=true;
        await form.RunWork(original,form.state.Destination,false);
        check(File.ReadAllText(target)=="sample"&&store.Read().ContainsKey(incoming.Key)&&!store.Read().ContainsKey(original.Key),
            "A troca de volta restaura do cache sem usar o download.");
        await form.ToggleMods(new List<Work>{original},form.state.Destination,false);
        select(new[]{first.Id});
        check(!File.Exists(target)&&form.primaryAction.Text=="Reativar"&&store.Read().ContainsKey(original.Key),
            "Desabilitar retira o arquivo do jogo e mantém o estado para reativação.");
        await form.ToggleMods(new List<Work>{original},form.state.Destination,true);
        check(File.ReadAllText(target)=="sample"&&!store.Read().ContainsKey(original.Key),"Reativar restaura o arquivo e libera sua cópia de cache.");
        var disabled=new HashSet<string>(store.Read().Keys);
        form.RemoveManagedWorks(new List<Work>{incoming},form.state.Destination,disabled);
        store.Discard(new[]{incoming.Key});
        check(File.ReadAllText(target)=="sample"&&!store.Read().ContainsKey(incoming.Key)&&File.Exists(archive),
            "Desinstalar a variante desabilitada apaga só seu cache e mantém a variante ativa e o download original.");
        form.RemoveManagedWorks(new List<Work>{original},form.state.Destination,new HashSet<string>());
        form.state.List.Mods.RemoveAll(item=>item.Id==remote.Id||item.Id==replacement.Id);
        first.AlternativeGroup=null;
        form.marked.Clear();
        form.DrawRows();
    }

    static void TestSyncRemovalReview(GeneralModWindow owner,string root,Action<bool,string> check){
        var extras=new[]{new P2PSyncExtra{Name="Extra 1",Files=new[]{new OwnedChange{Path="r6/tweaks/one.yaml"}}},
            new P2PSyncExtra{Name="Extra 2",Files=new[]{new OwnedChange{Path="r6/tweaks/two.yaml"}}}};
        using(var review=new GeneralSyncReviewDialog(new P2PSyncComparison{Extras=extras}))
        using(var timer=new Timer{Interval=40}){
            review.Show(owner);
            Application.DoEvents();
            var buttons=AllControls(review).OfType<Button>().ToArray();
            var all=buttons.Single(button=>button.Name=="SelectAllSyncExtras");
            var remove=buttons.Single(button=>button.Name=="RemoveSelectedSyncExtras");
            check(review.SelectedExtras.Length==0&&!remove.Enabled,"A revisão começa sem itens autorizados para remoção.");
            all.PerformClick();
            check(review.SelectedExtras.SequenceEqual(new[]{0,1})&&remove.Enabled,"Selecionar todos inclui cada mod extra.");
            all.PerformClick();
            check(review.SelectedExtras.Length==0&&!remove.Enabled,"Desmarcar todos retorna ao estado seguro.");
            all.PerformClick();
            bool answered=false;
            timer.Tick+=(s,e)=>{
                var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(item=>item.Name=="ConfirmSyncRemoval");
                if(dialog==null)return;
                timer.Stop();
                answered=true;
                AllControls(dialog).OfType<Button>().Single(button=>button.Name=="CancelSyncRemoval").PerformClick();
            };
            timer.Start();
            remove.PerformClick();
            check(answered&&review.Visible&&review.SelectedExtras.Length==2,"Voltar da confirmação preserva as caixas, sem concluir a remoção.");
            CaptureUi(review,Path.Combine(root,"sync-extra-review.png"));
        }
        using(var report=new GeneralSyncDifferencesDialog(new[]{
            new P2PSyncDifference{Kind="kept",Mod="Extra",Path="r6/tweaks/local.yaml"},
            new P2PSyncDifference{Kind="missing",Path="r6/scripts/host.reds"},
            new P2PSyncDifference{Kind="different",Path="r6/scripts/modified.reds"}
        })){
            report.Show(owner);
            Application.DoEvents();
            var grid=AllControls(report).OfType<DataGridView>().Single();
            check(grid.Rows.Count==3&&grid.Rows.Cast<DataGridViewRow>().Any(row=>Equals(row.Cells[0].Value,"Ausente neste PC")&&
                Equals(row.Cells[2].Value,"r6/scripts/host.reds")),"O relatório distingue ausência, alteração e extra mantido numa linha por caminho.");
            CaptureUi(report,Path.Combine(root,"sync-file-differences.png"));
        }
    }

    static void CaptureUi(Form form,string path){using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png);}}
}

