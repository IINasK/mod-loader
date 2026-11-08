using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    internal static void RunProjectHomeTests(string parent){
        string root=Path.Combine(parent,"h");Directory.CreateDirectory(root);
        var checks=new List<string>();Action<bool,string> check=(yes,message)=>{if(!yes)throw new IOException(message);checks.Add(message);};
        Exception failure=null;bool completed=false;bool animations=GeneralStyle.AnimationsEnabled;GeneralStyle.AnimationsEnabled=false;
        var thread=new System.Threading.Thread(()=>{
            try{
                var store=new GeneralProjectStore(Path.Combine(root,"p"));
                using(var home=new GeneralProjectHome(FixedData.Load(),new DownloadCache(Path.Combine(root,"cache")),store.Root,true)){
                    home.ShowInTaskbar=false;home.StartPosition=FormStartPosition.Manual;home.Location=new Point(-30000,-30000);
                    home.Shown+=async(s,e)=>{
                        try{
                            check(home.Controls.Find("NewProject",true).Length==1&&home.Controls.Find("ImportProject",true).Length==1,"Tela inicial oferece criar e importar projeto.");
                            check(home.Controls.Find("EmptyProjects",true).Length==1&&store.Read().Projects.Count==0,"Primeira abertura mostra biblioteca vazia sem criar o pacote Cyberpunk automaticamente.");
                            var homeMenu=(Button)home.Controls.Find("HomeMenu",true).Single();
                            using(var closeSharing=new Timer{Interval=60}){
                                bool openedSharing=false;
                                closeSharing.Tick+=(ss,ee)=>{
                                    var dialog=Application.OpenForms.OfType<GeneralP2PDialog>().FirstOrDefault();
                                    if(dialog==null)return;
                                    closeSharing.Stop();
                                    openedSharing=dialog.Controls.Find("p2p-input",true).Single().Visible&&
                                        !dialog.Controls.Find("p2p-output",true).Single().Visible;
                                    dialog.Close();
                                };
                                homeMenu.PerformClick();
                                var actions=home.OwnedForms.OfType<GeneralActionsMenu>().Single();
                                closeSharing.Start();
                                ((Button)actions.Controls.Find("share",true).Single()).PerformClick();
                                check(openedSharing&&store.Read().Projects.Count==0,"Home abre o mesmo compartilhamento para receber antes de existir um projeto.");
                            }
                            var create=(Button)home.Controls.Find("NewProject",true).Single();var import=(Button)home.Controls.Find("ImportProject",true).Single();
                            check(create.Top==import.Top&&create.Height==import.Height&&create.Right<=import.Left,"Criar e importar ficam lado a lado e alinhados.");
                            check(create.Height==44&&import.Height==44,"Ações principais da Home têm a altura compacta de 44px.");
                            check(store.Read().Theme=="light-red"&&GeneralStyle.RedTheme&&!GeneralStyle.Dark,"Nova biblioteca usa tema claro vermelho por padrão.");
                            var actualOpen=home.OpenRequested;GeneralProject opened=null;int openCalls=0;home.OpenRequested=project=>{opened=project;openCalls++;};
                            CompleteProjectName("Meu jogo",()=>create.PerformClick());
                            check(opened!=null&&opened.Name=="Meu jogo"&&store.Load(opened).List.Mods.Count==0&&store.Load(opened).List.Categories.Count==0,"Criar pela Home salva e abre projeto vazio sem sugestões de Cyberpunk.");
                            var blank=opened;check(home.Controls.Find("SavedProjects",true).Length==1&&home.Controls.Find("EmptyProjects",true).Length==0,"Projeto criado passa para a lista de salvos.");
                            var choices=(ListBox)home.Controls.Find("SavedProjects",true).Single();choices.SelectedIndex=0;opened=null;
                            typeof(ListBox).GetMethod("OnKeyDown",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(choices,new object[]{new KeyEventArgs(Keys.Enter)});
                            check(opened!=null&&opened.Id==blank.Id,"Projeto salvo abre pelo teclado sem recriar a lista.");
                            choices=(ListBox)home.Controls.Find("SavedProjects",true).Single();choices.SelectedIndex=0;
                            int beforeHover=openCalls;
                            typeof(ListBox).GetMethod("OnMouseMove",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(choices,new object[]{new MouseEventArgs(MouseButtons.None,0,12,20,0)});
                            check(home.HoveredSavedProject==0&&openCalls==beforeHover&&choices.SelectedIndex==0,"Hover destaca o projeto salvo sem abrir nem alterar a seleção por teclado.");
                            foreach(string theme in new[]{"light-red","light","dark"}){
                                GeneralStyle.UseTheme(theme);
                                choices.Items.Add(choices.Items[0]);choices.Items.Add(choices.Items[0]);
                                foreach(int at in new[]{0,1,choices.Items.Count-1})using(var image=new Bitmap(240,56))using(var graphics=Graphics.FromImage(image)){
                                    var bounds=new Rectangle(0,0,image.Width,image.Height);
                                    typeof(ListBox).GetMethod("OnMouseLeave",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(choices,new object[]{EventArgs.Empty});
                                    typeof(ListBox).GetMethod("OnDrawItem",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(choices,new object[]{new DrawItemEventArgs(graphics,choices.Font,bounds,at,DrawItemState.Selected)});
                                    check(image.GetPixel(3,3).ToArgb()==GeneralStyle.Surface.ToArgb()&&image.GetPixel(235,3).ToArgb()==GeneralStyle.Surface.ToArgb(),"Seleção nativa deixa cantos livres na linha "+at+"/"+theme);
                                    check(image.GetPixel(120,3).ToArgb()==GeneralStyle.Selection.ToArgb()&&image.GetPixel(120,1).ToArgb()==GeneralStyle.Surface.ToArgb(),"Seleção nativa tem recuo e preenchimento arredondado: "+at+"/"+theme);
                                    image.Save(Path.Combine(root,"home-selection-"+theme+"-"+at+".png"));
                                }
                                choices.Items.RemoveAt(choices.Items.Count-1);choices.Items.RemoveAt(choices.Items.Count-1);
                            }
                            GeneralStyle.UseTheme("light-red");
                            CaptureUi(home,Path.Combine(root,"home-hover.png"));
                            typeof(ListBox).GetMethod("OnMouseLeave",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(choices,new object[]{EventArgs.Empty});
                            check(home.HoveredSavedProject==-1,"Sair da lista remove o highlight de hover.");
                            ClickReadyWithoutRename(home,check);
                            check(opened!=null&&opened.Id!=blank.Id&&opened.TemplateId=="cyberpunk-2077"&&opened.Name=="Cyberpunk 2077"&&store.Load(opened).List.Mods.Count==158&&store.Load(blank).List.Mods.Count==0,"Pacote pronto abre com identidade fixa e preserva os projetos personalizados.");
                            check(store.Load(opened).Destination==null,"Fixture do pacote pronto não descobre nem altera o jogo real.");
                            var builtIn=opened;var configured=store.Load(builtIn);var first=configured.List.Mods.First();configured.ChosenVariants[first.Id]=first.DefaultVariant;
                            new GeneralModLibrary(Path.Combine(store.DataPath(builtIn),"lista.json")).Save(configured);
                            string configuredHash=Safe.Hash(Path.Combine(store.DataPath(builtIn),"lista.json"));
                            ClickReadyWithoutRename(home,check);
                            check(opened.Id==builtIn.Id&&store.Read().Projects.Count==2&&configuredHash==Safe.Hash(Path.Combine(store.DataPath(builtIn),"lista.json")),"Reabrir projeto pronto reutiliza estado e preferências sem duplicar ou sobrescrever.");
                            check(((ListBox)home.Controls.Find("SavedProjects",true).Single()).Items.Count==1,"Projeto padrão não é duplicado na lista de projetos personalizados.");
                            foreach(string theme in new[]{"light-red","light","dark"})foreach(string language in new[]{"pt","en"}){
                                store.SavePreferences(theme,language,root);home.Build();home.ClientSize=new Size(900,680);home.PerformLayout();await Task.Delay(40);
                                var newButton=home.Controls.Find("NewProject",true).Single();var importButton=home.Controls.Find("ImportProject",true).Single();var ready=home.Controls.Find("ReadyCyberpunk",true).Single();var saved=home.Controls.Find("SavedProjects",true).Single();
                                check(newButton.Height==importButton.Height&&newButton.Width>200&&importButton.Right<=importButton.Parent.ClientSize.Width,"Ações cabem na Home: "+theme+"/"+language);
                                check(HomeInside(newButton,home)&&HomeInside(importButton,home)&&HomeInside(home.Controls.Find("HomeMenu",true).Single(),home),"Ações e menu ficam dentro da janela após reduzir largura: "+theme+"/"+language);
                                check(ready.PointToScreen(Point.Empty).Y>=saved.PointToScreen(Point.Empty).Y+saved.Height&&ready.Bottom<=ready.Parent.ClientSize.Height,"Projetos prontos ficam abaixo dos salvos sem recorte: "+theme+"/"+language);
                                check(((GeneralButton)home.Controls.Find("HomeMenu",true).Single()).IconKind=="menu","Home usa o mesmo ícone de menu do editor: "+theme+"/"+language);
                                check(home.Controls.Find("HomeIntro",true).Single().Text==(language=="pt"?"Criado por Nask e Codex. Se achou ruim, faz você.":"Made by Nask and Codex. If you don't like it, make your own."),"Home usa o crédito combinado nos dois idiomas: "+theme+"/"+language);
                                CaptureUi(home,Path.Combine(root,"home-"+theme+"-"+language+".png"));
                            }
                            store.SavePreferences("light-red","pt",root);home.Build();
                            using(var timer=new Timer{Interval=60}){
                                timer.Tick+=(ss,ee)=>{var dialog=Application.OpenForms.OfType<GeneralOptionsDialog>().FirstOrDefault();if(dialog==null)return;timer.Stop();var theme=AllControls(dialog).OfType<GeneralPicker>().Single(p=>p.AccessibleName=="Tema");check(theme.Items.Count==4&&theme.SelectedIndex==2,"Opções lista o novo tema claro vermelho e mantém a escolha atual.");AllControls(dialog).OfType<Button>().Single(button=>button.Text=="Salvar").PerformClick();};
                                ((Button)home.Controls.Find("HomeMenu",true).Single()).PerformClick();
                                var actions=home.OwnedForms.OfType<GeneralActionsMenu>().Single();
                                timer.Start();((Button)actions.Controls.Find("options",true).Single()).PerformClick();
                            }
                            check(store.Read().Theme=="light-red","Salvar opções da Home preserva o tema vermelho.");
                            var disposable=store.Create("Projeto descartável",new GeneralModList(),"light-red","pt");
                            var neighbor=store.Create("Vizinho preservado",new GeneralModList(),"light-red","pt");home.Build();
                            string disposableFile=Path.Combine(store.DataPath(disposable),"lista.json"),disposableHash=Safe.Hash(disposableFile);
                            string blankFile=Path.Combine(store.DataPath(blank),"lista.json"),blankHash=Safe.Hash(blankFile);
                            choices=(ListBox)home.Controls.Find("SavedProjects",true).Single();
                            int target=choices.Items.Cast<object>().Select(item=>item.ToString()).ToList().IndexOf(disposable.Name);
                            var trash=GeneralProjectHome.DeleteProjectBounds(choices.GetItemRectangle(target));
                            check(target==1&&trash.Width==44&&trash.Height==44&&trash.Right==choices.GetItemRectangle(target).Right,"A lixeira reserva alvo de44px na própria linha, inclusive entre vizinhos.");
                            int beforeClick=openCalls;ClickSavedProject(choices,new Point(12,choices.GetItemRectangle(target).Top+28));
                            check(openCalls==beforeClick+1&&opened.Id==disposable.Id&&store.Read().Projects.Count==4,"Clicar no nome continua abrindo o projeto, sem excluir.");
                            choices=(ListBox)home.Controls.Find("SavedProjects",true).Single();target=choices.Items.Cast<object>().Select(item=>item.ToString()).ToList().IndexOf(disposable.Name);
                            trash=GeneralProjectHome.DeleteProjectBounds(choices.GetItemRectangle(target));var trashPoint=new Point(trash.Left+22,trash.Top+22);
                            beforeClick=openCalls;
                            int prompts=await ConfirmHomeDelete(home,new[]{DialogResult.No},()=>ClickSavedProject(choices,trashPoint),check,root);
                            check(prompts==1&&store.Read().Projects.Count==4&&Object.ReferenceEquals(choices,home.Controls.Find("SavedProjects",true).Single()),"Cancelar a primeira confirmação não exclui nem reconstrói a Home.");
                            prompts=await ConfirmHomeDelete(home,new[]{DialogResult.Yes,DialogResult.No},()=>ClickSavedProject(choices,trashPoint),check,root);
                            check(prompts==2&&store.Read().Projects.Count==4&&File.Exists(disposableFile)&&Safe.Hash(disposableFile)==disposableHash,"Cancelar a segunda confirmação preserva o projeto e seu arquivo.");
                            choices.SelectedIndex=target;
                            prompts=await ConfirmHomeDelete(home,new[]{DialogResult.Cancel},()=>typeof(ListBox).GetMethod("OnKeyDown",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(choices,new object[]{new KeyEventArgs(Keys.Delete)}),check,root);
                            check(prompts==1&&store.Read().Projects.Count==4,"Delete no teclado usa a mesma confirmação e fechar o diálogo cancela.");
                            ClickSavedProject(choices,new Point(choices.ClientSize.Width-22,choices.GetItemRectangle(choices.Items.Count-1).Bottom+12));
                            check(store.Read().Projects.Count==4&&openCalls==beforeClick,"Clicar no vazio da lista ou na lixeira não abre projeto.");
                            prompts=await ConfirmHomeDelete(home,new[]{DialogResult.Yes,DialogResult.Yes},()=>ClickSavedProject(choices,trashPoint),check,root);
                            var afterDelete=store.Read();
                            check(prompts==2&&afterDelete.Projects.Count==3&&!afterDelete.Projects.Any(item=>item.Id==disposable.Id),"Somente duas confirmações positivas removem o projeto certo do índice persistido.");
                            check(afterDelete.Projects.Any(item=>item.Id==neighbor.Id)&&afterDelete.Projects.Any(item=>item.Id==blank.Id)&&afterDelete.Projects.Any(item=>item.Id==builtIn.Id),"Excluir um projeto conserva vizinhos e o projeto padrão.");
                            check(File.Exists(disposableFile)&&Safe.Hash(disposableFile)==disposableHash&&Safe.Hash(blankFile)==blankHash,"A exclusão preserva dados, registros e arquivos dos projetos.");
                            check(openCalls==beforeClick&&((ListBox)home.Controls.Find("SavedProjects",true).Single()).Items.Count==2,"A lixeira atualiza a Home sem abrir o projeto excluído.");
                            home.Build();check(((ListBox)home.Controls.Find("SavedProjects",true).Single()).Items.Count==2,"O projeto excluído continua ausente depois de reconstruir a tela.");
                            string removeGame=Path.Combine(root,"online-testes-"+Guid.NewGuid().ToString("N"),"game");Directory.CreateDirectory(removeGame);File.WriteAllText(Safe.In(removeGame,".fixture-only"),"fixture");
                            string removeZip=TestZip(root,"home-remove.zip","mods/home.txt","home payload");
                            var removeProject=store.Create("Projeto descartável",new GeneralModList(),"light-red","pt",removeGame);
                            new GeneralModInstaller(removeGame,Path.Combine(store.DataPath(removeProject),"Instalacoes")).Install(removeZip,"home-owned",null);home.Build();
                            choices=(ListBox)home.Controls.Find("SavedProjects",true).Single();int removeAt=choices.Items.Cast<object>().Select(item=>item.ToString()).ToList().IndexOf(removeProject.Name);
                            var removeBounds=GeneralProjectHome.DeleteProjectBounds(choices.GetItemRectangle(removeAt));
                            prompts=await ConfirmHomeDelete(home,new[]{DialogResult.Yes,DialogResult.No},()=>ClickSavedProject(choices,new Point(removeBounds.Left+22,removeBounds.Top+22)),check,root,true);
                            check(prompts==2&&File.Exists(Safe.In(removeGame,"mods/home.txt"))&&store.Read().Projects.Any(item=>item.Id==removeProject.Id),"Cancelar a revisão da opção apagar mods não remove arquivo algum.");
                            prompts=await ConfirmHomeDelete(home,new[]{DialogResult.Yes,DialogResult.Yes},()=>ClickSavedProject(choices,new Point(removeBounds.Left+22,removeBounds.Top+22)),check,root,true);
                            check(prompts==2&&!File.Exists(Safe.In(removeGame,"mods/home.txt"))&&!store.Read().Projects.Any(item=>item.Id==removeProject.Id),"O fluxo real da Home apaga o arquivo e remove o projeto após as duas etapas.");
                            check(File.Exists(removeZip),"A opção apagar mods mantém o download original fora do destino.");
                            store.Delete(neighbor);home.Build();
                            store.Delete(blank);home.Build();check(store.Read().Projects.Count==1&&File.Exists(Path.Combine(store.DataPath(blank),"lista.json")),"Excluir metadados retira o projeto da Home e conserva seus dados.");
                            var remaining=store.Read().Projects.Single();string before=Safe.Hash(Path.Combine(store.DataPath(remaining),"lista.json"));home.OpenRequested=actualOpen;
                            using(var timer=new Timer{Interval=60}){
                                timer.Tick+=(ss,ee)=>{var window=Application.OpenForms.OfType<GeneralModWindow>().FirstOrDefault();if(window==null)return;timer.Stop();try{check(window.state.List.Mods.Count==158&&window.data==store.DataPath(remaining),"Abrir projeto usa a lista e a pasta próprias na janela real.");window.homeButton.PerformClick();}catch(Exception ex){failure=ex;window.Close();}};
                                timer.Start();home.OpenRequested(remaining);
                            }
                            if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
                            check(home.Visible&&home.Controls.Find("EmptyProjects",true).Length==1&&before==Safe.Hash(Path.Combine(store.DataPath(remaining),"lista.json")),"Voltar pelo menu do editor restaura a Home e preserva os dados do projeto padrão.");
                            completed=true;
                        }catch(Exception ex){failure=ex;}finally{Application.ExitThread();}
                    };Application.Run(home);
                }
            }catch(Exception ex){failure=ex;}
        });
        thread.IsBackground=true;thread.SetApartmentState(System.Threading.ApartmentState.STA);thread.Start();
        if(!thread.Join(60000))throw new IOException("Tela inicial excedeu tempo de teste.");
        GeneralStyle.UseTheme("light");GeneralStyle.Language="pt";GeneralStyle.AnimationsEnabled=animations;
        if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();if(!completed)throw new IOException("Teste de Home não terminou.");
        var state=new GeneralModState{List=new GeneralModList(),Destination=root,DownloadFolder=root};
        TestForm(FixedData.Load(),state,Path.Combine(root,"editor"),root,async form=>{
            check(((GeneralButton)form.optionsButton).IconKind=="menu","Editor mantém o menu no cabeçalho.");
            using(var menu=new GeneralActionsMenu())check(AllControls(menu).OfType<Button>().Any(button=>button.Name=="share")&&AllControls(menu).OfType<Button>().Any(button=>button.Name=="options"),"Compartilhar e Opções estão disponíveis no menu.");
            check(Object.ReferenceEquals(form.updateChecker,GeneralUpdateChecker.Session),"Home e projetos compartilham uma consulta de atualização por abertura.");
            var store=new GeneralProjectStore(Path.Combine(root,"attached"));var item=store.Create("Projeto atual",new GeneralModList(),"light-red","pt");store.SavePreferences("dark","en");form.AttachProject(store,item);
            check(form.state.Theme=="dark"&&form.state.Language=="en"&&GeneralStyle.Dark,"Tema e idioma globais acompanham a abertura de outro projeto.");
            check(form.Text.Contains("Projeto atual"),"Janela identifica o projeto aberto.");
            var oldPack=GeneralModLibrary.Seed(form.catalog,new ModImportItem[0]);
            const string eclipse=GeneralModLibrary.EclipseTattooId;
            foreach(var preset in oldPack.Presets)preset.ModIds.Remove(eclipse);
            oldPack.Presets.Add(new GeneralModPreset{Id="custom",Name="Meu preset"});
            var ready=store.Create("Cyberpunk 2077",oldPack,"dark","en",root,"cyberpunk-2077");
            form.state=store.Load(ready);
            form.AttachProject(store,ready);
            var stored=new GeneralModLibrary(Path.Combine(form.data,"lista.json")).Load().List;
            check(stored.Mods.Count(mod=>mod.Id==eclipse)==1&&
                stored.Presets.Where(preset=>preset.Id!="custom").All(preset=>preset.ModIds.Contains(eclipse))&&
                !stored.Presets.Single(preset=>preset.Id=="custom").ModIds.Contains(eclipse),
                "Atualizar o pacote oficial recupera a Eclipse nos dois presets oficiais e preserva o personalizado.");
            form.ProjectCommand("new");check(form.ProjectAction=="new","Novo projeto retorna à Home sem substituir a lista atual.");await Task.CompletedTask;
        });
        Safe.WriteJson(Path.Combine(root,"resultado-project-home.json"),new{Passed=true,Tests=checks,RealGameTouched=false,NetworkUsed=false});
    }
    static void ClickSavedProject(ListBox list,Point point){typeof(ListBox).GetMethod("OnMouseClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(list,new object[]{new MouseEventArgs(MouseButtons.Left,1,point.X,point.Y,0)});}
    static async Task<int> ConfirmHomeDelete(GeneralProjectHome home,DialogResult[] responses,Action click,Action<bool,string> check,string root,bool removeMods=false){
        int prompts=0;GeneralDialog previous=null;Exception failure=null;
        using(var timer=new Timer{Interval=50}){
            timer.Tick+=(s,e)=>{
                var dialog=Application.OpenForms.OfType<GeneralDialog>().FirstOrDefault(item=>item is GeneralProjectDeleteChoice||item is GeneralProjectDeleteReview);
                if(dialog==null||Object.ReferenceEquals(dialog,previous))return;previous=dialog;
                try{
                    check(prompts<responses.Length,"A ação não abre confirmações extras.");
                    check(((Button)dialog.AcceptButton).Name==(dialog is GeneralProjectDeleteChoice?"CancelDelete":"CancelProjectDelete"),"As duas etapas começam com Cancelar como ação padrão.");
                    check(AllControls(dialog).Any(control=>control.Text.Contains("Projeto descartável")),"Cada confirmação identifica o projeto que será excluído.");
                    CaptureUi(dialog,Path.Combine(root,"delete-confirm-"+prompts+".png"));var answer=responses[prompts++];
                    if(dialog is GeneralProjectDeleteChoice)AllControls(dialog).OfType<RadioButton>().Single(item=>item.Name==(removeMods?"DeleteMods":"DeleteAppOnly")).Checked=true;
                    if(answer==DialogResult.Cancel)dialog.Close();else AllControls(dialog).OfType<Button>().Single(button=>button.Name==(dialog is GeneralProjectDeleteChoice?(answer==DialogResult.Yes?"ContinueDelete":"CancelDelete"):(answer==DialogResult.Yes?"ConfirmProjectDelete":"CancelProjectDelete"))).PerformClick();
                }catch(Exception ex){failure=ex;timer.Stop();dialog.DialogResult=DialogResult.Cancel;}
            };
            timer.Start();click();var limit=DateTime.UtcNow.AddSeconds(20);while(home.DeletingProject&&DateTime.UtcNow<limit)await Task.Delay(25);if(home.DeletingProject)throw new IOException("Exclusão excedeu o tempo do teste.");
        }
        if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();return prompts;
    }
    static void CompleteProjectName(string value,Action show){
        using(var timer=new Timer{Interval=60}){timer.Tick+=(s,e)=>{var dialog=Application.OpenForms.OfType<GeneralNameDialog>().FirstOrDefault();if(dialog==null)return;timer.Stop();AllControls(dialog).OfType<TextBox>().Single(box=>box.Name=="NameInput").Text=value;((Button)dialog.AcceptButton).PerformClick();};timer.Start();show();}
    }
    static void ClickReadyWithoutRename(GeneralProjectHome home,Action<bool,string> check){
        int prompts=0;using(var timer=new Timer{Interval=60}){
            timer.Tick+=(s,e)=>{var dialog=Application.OpenForms.OfType<GeneralNameDialog>().FirstOrDefault();if(dialog==null)return;prompts++;dialog.DialogResult=DialogResult.Cancel;};
            timer.Start();((Button)home.Controls.Find("ReadyCyberpunk",true).Single()).PerformClick();
        }
        check(prompts==0,"Projeto pronto não abre uma janela para renomear.");
    }
    static bool HomeInside(Control control,Form home){var bounds=home.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));return bounds.Left>=0&&bounds.Top>=0&&bounds.Right<=home.ClientSize.Width&&bounds.Bottom<=home.ClientSize.Height;}
}
