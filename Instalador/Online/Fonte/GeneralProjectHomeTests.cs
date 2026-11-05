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
        string root=Path.Combine(parent,"home-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var checks=new List<string>();Action<bool,string> check=(yes,message)=>{if(!yes)throw new IOException(message);checks.Add(message);};
        Exception failure=null;bool completed=false;bool animations=GeneralStyle.AnimationsEnabled;GeneralStyle.AnimationsEnabled=false;
        var thread=new System.Threading.Thread(()=>{
            try{
                var store=new GeneralProjectStore(Path.Combine(root,"projects"));
                using(var home=new GeneralProjectHome(FixedData.Load(),new DownloadCache(Path.Combine(root,"cache")),store.Root,true)){
                    home.ShowInTaskbar=false;home.StartPosition=FormStartPosition.Manual;home.Location=new Point(-30000,-30000);
                    home.Shown+=async(s,e)=>{
                        try{
                            check(home.Controls.Find("NewProject",true).Length==1&&home.Controls.Find("ImportProject",true).Length==1,"Tela inicial oferece criar e importar projeto.");
                            check(home.Controls.Find("EmptyProjects",true).Length==1&&store.Read().Projects.Count==0,"Primeira abertura mostra biblioteca vazia sem criar o pacote Cyberpunk automaticamente.");
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
                            CaptureUi(home,Path.Combine(root,"home-hover.png"));
                            typeof(ListBox).GetMethod("OnMouseLeave",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(choices,new object[]{EventArgs.Empty});
                            check(home.HoveredSavedProject==-1,"Sair da lista remove o highlight de hover.");
                            ClickReadyWithoutRename(home,check);
                            check(opened!=null&&opened.Id!=blank.Id&&opened.TemplateId=="cyberpunk-2077"&&opened.Name=="Cyberpunk 2077"&&store.Load(opened).List.Mods.Count==135&&store.Load(blank).List.Mods.Count==0,"Pacote pronto abre com identidade fixa e preserva os projetos personalizados.");
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
                                check(HomeInside(newButton,home)&&HomeInside(importButton,home)&&HomeInside(home.Controls.Find("HomeOptions",true).Single(),home),"Ações e engrenagem ficam dentro da janela após reduzir largura: "+theme+"/"+language);
                                check(ready.PointToScreen(Point.Empty).Y>=saved.PointToScreen(Point.Empty).Y+saved.Height&&ready.Bottom<=ready.Parent.ClientSize.Height,"Projetos prontos ficam abaixo dos salvos sem recorte: "+theme+"/"+language);
                                check(((GeneralButton)home.Controls.Find("HomeOptions",true).Single()).IconKind=="settings","Opções usa engrenagem independente na Home: "+theme+"/"+language);
                                check(home.Controls.Find("HomeIntro",true).Single().Text==(language=="pt"?"Criado por Nask e Codex. Se achou ruim, faz você.":"Made by Nask and Codex. If you don't like it, make your own."),"Home usa o crédito combinado nos dois idiomas: "+theme+"/"+language);
                                CaptureUi(home,Path.Combine(root,"home-"+theme+"-"+language+".png"));
                            }
                            store.SavePreferences("light-red","pt",root);home.Build();
                            using(var timer=new Timer{Interval=60}){
                                timer.Tick+=(ss,ee)=>{var dialog=Application.OpenForms.OfType<GeneralOptionsDialog>().FirstOrDefault();if(dialog==null)return;timer.Stop();var theme=AllControls(dialog).OfType<GeneralPicker>().Single(p=>p.AccessibleName=="Tema");check(theme.Items.Count==3&&theme.SelectedIndex==2,"Opções lista o novo tema claro vermelho e mantém a escolha atual.");AllControls(dialog).OfType<Button>().Single(button=>button.Text=="Salvar").PerformClick();};
                                timer.Start();((Button)home.Controls.Find("HomeOptions",true).Single()).PerformClick();
                            }
                            check(store.Read().Theme=="light-red","Salvar opções da Home preserva o tema vermelho.");
                            store.Delete(blank);home.Build();check(store.Read().Projects.Count==1&&File.Exists(Path.Combine(store.DataPath(blank),"lista.json")),"Excluir metadados retira o projeto da Home e conserva seus dados.");
                            var remaining=store.Read().Projects.Single();string before=Safe.Hash(Path.Combine(store.DataPath(remaining),"lista.json"));home.OpenRequested=actualOpen;
                            using(var timer=new Timer{Interval=60}){
                                timer.Tick+=(ss,ee)=>{var window=Application.OpenForms.OfType<GeneralModWindow>().FirstOrDefault();if(window==null)return;timer.Stop();try{check(window.state.List.Mods.Count==135&&window.data==store.DataPath(remaining),"Abrir projeto usa a lista e a pasta próprias na janela real.");window.Close();}catch(Exception ex){failure=ex;window.Close();}};
                                timer.Start();home.OpenRequested(remaining);
                            }
                            if(failure!=null)throw failure;
                            check(home.Visible&&home.Controls.Find("EmptyProjects",true).Length==1&&before==Safe.Hash(Path.Combine(store.DataPath(remaining),"lista.json")),"Fechar o editor restaura a Home sem transformar o padrão em projeto personalizado.");
                            completed=true;
                        }catch(Exception ex){failure=ex;}finally{Application.ExitThread();}
                    };Application.Run(home);
                }
            }catch(Exception ex){failure=ex;}
        });
        thread.IsBackground=true;thread.SetApartmentState(System.Threading.ApartmentState.STA);thread.Start();
        if(!thread.Join(60000))throw new IOException("Tela inicial excedeu tempo de teste.");
        GeneralStyle.UseTheme("light");GeneralStyle.Language="pt";GeneralStyle.AnimationsEnabled=animations;
        if(failure!=null)throw failure;if(!completed)throw new IOException("Teste de Home não terminou.");
        var state=new GeneralModState{List=new GeneralModList(),Destination=root,DownloadFolder=root};
        TestForm(FixedData.Load(),state,Path.Combine(root,"editor"),root,async form=>{
            check(((GeneralButton)form.optionsButton).IconKind=="menu","Editor mantém o menu no cabeçalho.");
            using(var menu=new GeneralActionsMenu())check(AllControls(menu).OfType<Button>().Any(button=>button.Name=="share")&&AllControls(menu).OfType<Button>().Any(button=>button.Name=="options"),"Compartilhar e Opções estão disponíveis no menu.");
            check(Object.ReferenceEquals(form.updateChecker,GeneralUpdateChecker.Session),"Home e projetos compartilham uma consulta de atualização por abertura.");
            var store=new GeneralProjectStore(Path.Combine(root,"attached"));var item=store.Create("Projeto atual",new GeneralModList(),"light-red","pt");store.SavePreferences("dark","en");form.AttachProject(store,item);
            check(form.state.Theme=="dark"&&form.state.Language=="en"&&GeneralStyle.Dark,"Tema e idioma globais acompanham a abertura de outro projeto.");
            check(form.Text.Contains("Projeto atual"),"Janela identifica o projeto aberto.");form.ProjectCommand("new");check(form.ProjectAction=="new","Novo projeto retorna à Home sem substituir a lista atual.");await Task.CompletedTask;
        });
        Safe.WriteJson(Path.Combine(root,"resultado-project-home.json"),new{Passed=true,Tests=checks,RealGameTouched=false,NetworkUsed=false});
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
