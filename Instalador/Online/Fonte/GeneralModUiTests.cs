using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint="SendMessageW")]
    static extern IntPtr SendUiMouseMessage(IntPtr window,uint message,IntPtr keys,IntPtr position);

    static void ClickModCell(DataGridView grid,Point point,bool doubleClick=false) {
        var position=new IntPtr((point.Y<<16)|point.X);
        SendUiMouseMessage(grid.Handle,0x0200,IntPtr.Zero,position);
        SendUiMouseMessage(grid.Handle,doubleClick?0x0203U:0x0201U,new IntPtr(1),position);
        SendUiMouseMessage(grid.Handle,0x0202,IntPtr.Zero,position);
        Application.DoEvents();
    }

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
            form.state.Destination=null;
            await form.Scan();
            check(form.primaryAction.Text=="Selecionar pasta"&&form.primaryAction.Enabled
                &&form.statuses.Count==0,"Projeto sem destino oferece Selecionar pasta sem varrer os mods.");
            form.SetDestination(target);
            form.RefreshPrimaryAction();
            check(form.primaryAction.Text!="Selecionar pasta","O botão volta à ação dos mods após selecionar o destino.");
            using(var options=new GeneralOptionsDialog("light","pt",root,null,target)){
                options.Show(form);Application.DoEvents();
                var folder=AllControls(options).OfType<TextBox>().Single(box=>box.Name=="OptionsInstallationFolder");
                check(folder.Text==target&&folder.Parent.Height==44,"Opções mostra o destino do projeto em um campo alinhado.");
                check(!folder.ReadOnly&&folder.ShortcutsEnabled,"O campo da pasta aceita colagem.");
                folder.Text=archive;
                var save=(Button)options.AcceptButton;
                save.PerformClick();
                check(options.DialogResult!=DialogResult.OK,"Uma pasta inválida não é salva.");
                folder.SelectAll();
                folder.Paste("\""+target+"\"");
                save.PerformClick();
                check(options.DialogResult==DialogResult.OK&&options.Destination==target,"O caminho copiado do Explorador, entre aspas, é aceito.");
            }
            check(form.currentProgress.Top==form.totalProgress.Top&&Math.Abs(form.currentProgress.Width-form.totalProgress.Width)<=1,"As barras do rodapé dividem a mesma linha em partes iguais.");
            var verifyButton=AllControls(form).OfType<Button>().Single(button=>button.Name=="VerifyProject");
            check(verifyButton.Right<form.primaryAction.Left&&verifyButton.Top==form.primaryAction.Top,
                "Verificar fica ao lado da ação principal no rodapé.");
            check(!form.grid.Columns["Group"].Visible&&form.grid.AllowUserToResizeColumns,
                "Grupos ocupam a linha do nome e as divisórias do cabeçalho permitem ajustar a largura.");
            int initialStatusWidth=form.grid.Columns["Status"].Width;
            form.grid.Columns["Status"].Width=initialStatusWidth+28;
            check(form.grid.Columns["Status"].Width==initialStatusWidth+28,"A coluna aceita a largura ajustada pelo usuário.");
            form.grid.Columns["Status"].Width=initialStatusWidth;
            check(form.Font.FontFamily.Name.IndexOf("Jost",StringComparison.OrdinalIgnoreCase)>=0,"A fonte Jost está embutida e em uso.");
            var firstRow=form.ModRows().First();
            var statusCell=firstRow.Cells["Status"];
            var markCell=firstRow.Cells["Mark"];
            var statusBounds=form.grid.GetCellDisplayRectangle(statusCell.ColumnIndex,firstRow.Index,false);
            var markBounds=form.grid.GetCellDisplayRectangle(markCell.ColumnIndex,firstRow.Index,false);
            var statusPoint=new Point(statusBounds.Right-4,statusBounds.Top+8);
            var markMargin=new Point(markBounds.Left+2,markBounds.Top+8);
            var glyph=markCell.ContentBounds;
            var markPoint=new Point(markBounds.Left+glyph.Left+glyph.Width/2,markBounds.Top+glyph.Top+glyph.Height/2);
            ClickModCell(form.grid,statusPoint);
            check(form.marked.SetEquals(new[]{one.Id}),"Um clique na área vazia de Situação marca a linha inteira.");
            ClickModCell(form.grid,markMargin);
            check(form.marked.Count==0,"A margem da célula de marcação também responde ao clique.");
            ClickModCell(form.grid,markPoint);
            check(form.marked.SetEquals(new[]{one.Id}),"O clique sobre o símbolo marca uma única vez.");
            bool openedByDoubleClick=false;
            using(var timer=new Timer{Interval=25}){
                timer.Tick+=(s,e)=>{
                    var editor=form.OwnedForms.OfType<GeneralModEditor>().FirstOrDefault(dialog=>dialog.Visible);
                    if(editor==null)return;
                    openedByDoubleClick=true;timer.Stop();editor.Complete(DialogResult.Cancel);
                };
                timer.Start();
                ClickModCell(form.grid,statusPoint);
                ClickModCell(form.grid,statusPoint,true);
            }
            check(openedByDoubleClick&&form.marked.SetEquals(new[]{one.Id}),"Dois cliques na linha abrem os detalhes e preservam a marca.");
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
                var copySource=AllControls(editor).OfType<Button>().Single(item=>item.Name=="CopyDownloadSource");
                check(!sources.Multiline&&copySource.Parent==sources.Parent&&copySource.Right<=sources.Parent.ClientSize.Width,
                    "A fonte usa uma linha e o botão de cópia cabe dentro da caixa.");
                check(button.Bottom<=button.Parent.Height&&button.Right<=button.Parent.Width,"Ações do editor ficam dentro do popup.");
                CaptureUi(editor,Path.Combine(root,"editor-claro.png"));editor.Close();
            }
            const string firstLink="https://www.nexusmods.com/cyberpunk2077/mods/13556?tab=files&file_id=1";
            const string secondLink="https://www.nexusmods.com/cyberpunk2077/mods/13556?tab=files&file_id=2";
            var linked=NewEntry("Duas fontes",firstLink);
            linked.Variants[0].Sources.Add(secondLink);
            using(var editor=new GeneralModEditor(linked,list.Categories,"default",true,"",async source=>{
                await Task.Delay(40);
                return new GeneralSourceInfo{Name="Duas fontes",NexusChecked=true,Tags=new[]{"Compatibilidade Nexus","Veículos"}};
            })){
                editor.StartPosition=FormStartPosition.Manual;editor.Location=new Point(-30000,-30000);editor.Show(form);Application.DoEvents();
                var field=AllControls(editor).OfType<TextBox>().Single(box=>box.Name=="DownloadSource");
                var picker=AllControls(editor).OfType<GeneralPicker>().Single(control=>control.Name=="DownloadSourcePicker");
                picker.SelectedIndex=1;
                check(field.Text==secondLink&&picker.Visible,"Uma variante com vários arquivos mostra a fonte selecionada em uma única linha.");
                ((Button)editor.AcceptButton).PerformClick();
                await Until(()=>editor.Result!=null);
                check(editor.Result.Tags.Contains("Compatibilidade Nexus")&&editor.Result.Variants[0].Sources.SequenceEqual(new[]{firstLink,secondLink}),
                    "Aplicar aguarda as tags do Nexus e preserva todas as fontes da variante.");
            }
            form.marked.Clear();
            var dependency=form.CreateDependency(new GeneralNexusRequirement{Name="Dependência teste",
                Source=new GeneralNexusLink{Game="cyberpunk2077",Mod=8000},MinFile=123},null);
            form.DrawRows();
            var dependencyRow=form.ModRows().Single(row=>Equals(row.Tag,dependency.Id));
            check(Equals(dependencyRow.Cells["Status"].Value,"Escolher versão")&&Equals(dependencyRow.Cells["Size"].Value,"")&&
                !dependencyRow.Cells["Name"].Style.ForeColor.IsEmpty&&form.ModRows().Last()==dependencyRow,
                "Dependência sem arquivo aparece em laranja no fim, com status específico e espaço em branco.");
            form.statusFilters=new HashSet<string>{"Escolher versão"};
            check(form.VisibleMods().Single()==dependency,"O filtro de situação encontra dependências sem versão.");
            form.statusFilters=null;
            form.grid.CurrentCell=dependencyRow.Cells["Name"];
            form.RefreshPrimaryAction();
            check(form.primaryAction.Text=="Escolher versão","A dependência indefinida abre o editor em vez de baixar.");
            dependency.Variants[0].Sources[0]="https://www.nexusmods.com/cyberpunk2077/mods/9000?tab=files&file_id=123";
            check(GeneralModLibrary.NeedsDependencyVersion(dependency,dependency.Variants[0]),"Arquivo de outro mod não resolve a dependência.");
            dependency.Variants[0].Sources[0]="https://www.nexusmods.com/cyberpunk2077/mods/8000?tab=files&file_id=123";
            check(!GeneralModLibrary.NeedsDependencyVersion(dependency,dependency.Variants[0]),"O arquivo explícito da origem libera a ação normal.");
            form.grid.CurrentCell=null;
            form.state.List.Mods.Remove(dependency);
            form.state.List.Categories.Remove(DependencyCategory);
            form.DrawRows();
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
                            var archive=AllControls(editor).OfType<TextBox>().Single(box=>box.Name=="DownloadSource");
                            var viewport=scroll.RectangleToScreen(scroll.ClientRectangle);
                            var field=archive.RectangleToScreen(archive.ClientRectangle);
                            CaptureUi(editor,Path.Combine(root,"editor-identification.png"));
                            scroll.RefreshScroll();
                            int beforeWheel=scroll.Bar.Value;
                            scroll.Bar.Wheel(-SystemInformation.MouseWheelScrollDelta);
                            check(SystemInformation.MouseWheelScrollLines==0||scroll.Bar.Limit==beforeWheel||
                                scroll.Bar.Value-beforeWheel>=Math.Min(scroll.Bar.Limit-beforeWheel,scroll.Content.Font.Height),
                                "A roda percorre uma distância de texto no editor, em vez de poucos pixels.");
                            field=archive.RectangleToScreen(archive.ClientRectangle);
                            if(field.Bottom>viewport.Bottom)scroll.ScrollTo(scroll.Bar.Value+field.Bottom-viewport.Bottom+8);
                            field=archive.RectangleToScreen(archive.ClientRectangle);
                            Control surface=archive.Parent;
                            while(!(surface is GeneralSurface))surface=surface.Parent;
                            check(Rectangle.Intersect(field,viewport).Height==field.Height&&!archive.Multiline&&surface.Height<=52,
                                "Um único link tem campo compacto e totalmente visível: "+surface.Bounds+" / "+field+" / "+viewport);
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
                            var groupName=AllControls(editor).OfType<TextBox>().Single(box=>box.Name=="AlternativeGroupName");
                            check(groupName.Visible&&groupName.MaxLength==18&&editor.GroupStyle.Color.StartsWith("#"),"Escolher outro mod libera o nome curto e a cor do grupo.");
                            groupName.Text="Corpo leve";
                            stage=3;
                            AllControls(editor).OfType<Button>().Single(button=>button.Name=="AlternativeMod").PerformClick();
                        }else if(stage==3&&form.associationChoice!=null){
                            stage=4;
                            AllControls(form).OfType<Button>().Single(button=>button.Name=="CancelAlternativeSelection").PerformClick();
                        }else if(stage==4&&editor!=null){
                            check(editor.LinkedAlternative==candidate&&source.AlternativeGroup==null&&candidate.AlternativeGroup==null&&editor.GroupStyle.Name=="Corpo leve",
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

