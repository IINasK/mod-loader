using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    internal static void RunPresetDraftTests(string parent){
        string root=OnlineTests.NestedRoot(parent,"preset-draft-");Directory.CreateDirectory(root);
        var a=NewEntry("Primeiro","https://example.com/a.zip");var b=NewEntry("Segundo","https://example.com/b.zip");
        var saved=new GeneralModPreset{Id="kit",Name="Original",ModIds=new List<string>{a.Id,b.Id}};
        var spare=new GeneralModPreset{Id="spare",Name="Alternativo",ModIds=new List<string>{a.Id,b.Id}};
        var state=new GeneralModState{Destination=root,DownloadFolder=root,SelectedPreset="kit",List=new GeneralModList{Mods=new List<GeneralModEntry>{a,b},Presets=new List<GeneralModPreset>{saved,spare}}};
        var checks=new List<string>();Action<bool,string> check=(yes,message)=>{if(!yes)throw new IOException(message);checks.Add(message);};
        TestForm(FixedData.Load(),state,Path.Combine(root,"state"),root,async form=>{
            form.Persist();form.CapturePresetDraft();form.UpdateTableTitle();
            check(!form.presetSettings.Visible&&form.preset.DeleteChoice!=null,"Salvar aguarda edição de nome e a lixeira fica nas opções.");
            check(((GeneralButton)form.presetSettings).IconKind=="save"&&form.presetSettings.Parent==form.tableName.Parent,
                "O disquete fica no editor de nome.");
            check(form.preset.IconOnly&&form.mainLayout.GetControlFromPosition(0,2)==null,"O seletor integra o título.");
            form.preset.Toggle();
            var popup=form.preset.OpenPopup;
            var view=(GeneralScrollView)popup.Items.OfType<ToolStripControlHost>().Single().Control;
            var choices=(ListBox)view.Content;
            Application.DoEvents();
            check(choices.Items.Count==3&&popup.Padding.Left==popup.Padding.Right&&
                !view.Bar.Visible&&Math.Abs(choices.Width-view.ClientSize.Width)<=1,"Menu curto ocupa a largura com recuos iguais e agrupa as ações dos presets.");
            using(var capture=new Bitmap(popup.Width,popup.Height)){
                popup.DrawToBitmap(capture,new Rectangle(Point.Empty,capture.Size));
                capture.Save(Path.Combine(root,"preset-popup.png"));
            }
            popup.Close();
            check(!form.grid.Columns.Contains("ResetPreset"),"A lixeira antiga não fica duplicada no cabeçalho da tabela.");
            form.tableName.Text="Rascunho";form.marked.Add(a.Id);form.DrawRows();
            form.scanning=true;form.Busy(true);
            check(form.tableName.ReadOnly&&form.tableName.ForeColor==GeneralStyle.Muted&&!form.presetSettings.Enabled,
                "O título fica apagado e bloqueado durante a verificação.");
            form.scanning=false;form.Busy(false);
            check(!form.tableName.ReadOnly&&form.tableName.Text=="Rascunho"&&
                form.presetSettings.PointToScreen(new Point(form.presetSettings.Width,0)).X==form.preset.PointToScreen(Point.Empty).X,
                "O título mantém o rascunho e o disquete fica junto do seletor.");
            check(form.ActivePreset.Name=="Original"&&form.library.Load().List.Presets.Single(p=>p.Id=="kit").Name=="Original"&&form.presetSettings.Enabled,"Nome digitado e seleção são rascunhos até salvar.");
            form.marked.Clear();form.DrawRows();
            form.tableName.Text="Renomeado";form.presetSettings.PerformClick();
            check(form.ActivePreset.Name=="Renomeado"&&form.ActivePreset.ModIds.Count==2&&form.library.Load().List.Presets.Single(p=>p.Id=="kit").Name=="Renomeado","Salvar só o nome preserva os membros do preset.");
            form.marked.Add(a.Id);form.DrawRows();form.SaveCurrentPreset();
            check(form.ActivePreset.ModIds.SequenceEqual(new[]{a.Id})&&form.state.List.Mods.Count==2,"Disquete salva a seleção sem excluir mods da biblioteca.");
            form.marked.Add(b.Id);form.statusFilters=new HashSet<string>();form.DrawRows();form.SaveCurrentPreset();
            check(form.ActivePreset.ModIds.Count==2,"Salvar preserva os itens marcados que um filtro está escondendo.");
            form.ResetColumnFilters();form.marked.Remove(b.Id);form.DrawRows();form.SaveCurrentPreset();
            form.tableName.Text="   ";check(!form.presetSettings.Enabled,"Nome vazio impede salvar.");form.tableName.Text="Renomeado";
            using(var timer=new Timer{Interval=60}){
                timer.Tick+=(s,e)=>{var dialog=Application.OpenForms.OfType<GeneralDialog>().FirstOrDefault(item=>item.Text=="Excluir preset");if(dialog==null)return;timer.Stop();((Button)dialog.AcceptButton).PerformClick();};timer.Start();form.preset.DeleteChoice(0);
            }
            check(form.state.List.Presets.Single().Id==form.state.SelectedPreset&&form.state.SelectedPreset=="spare"&&
                form.state.List.Mods.Count==2&&form.preset.Items.Count==1,"Excluir o preset seleciona outro e a lixeira desativa quando resta um só.");
            check(form.projectDraft!=null&&form.library.Load().List.Presets.Count==2,"Excluir o preset prepara o rascunho antes de gravar.");
            form.SaveProjectEdits();
            CompleteProjectName("Meu novo preset",()=>form.preset.AddChoice());
            check(form.ActivePreset!=null&&form.ActivePreset.Name=="Meu novo preset"&&form.ActivePreset.ModIds.SequenceEqual(new[]{a.Id}),"A ação Novo preset cria e seleciona um preset com os itens marcados.");
            form.BeginProjectEdit();form.state.List.Mods.First().Tags.Add("tag rascunho");form.FinishProjectEdit();
            form.Persist();
            check(form.projectDraft!=null&&!form.library.Load().List.Mods.First().Tags.Contains("tag rascunho"),
                "Persistir preferências ou registros operacionais preserva a definição salva do projeto.");
            form.testProjectSaveChoice=()=>DialogResult.Cancel;
            check(!form.ConfirmProjectNavigation()&&form.state.List.Mods.First().Tags.Contains("tag rascunho"),
                "Cancelar fica no projeto com o rascunho intacto.");
            form.testProjectSaveChoice=()=>DialogResult.No;
            check(form.ConfirmProjectNavigation()&&!form.state.List.Mods.First().Tags.Contains("tag rascunho"),
                "Descartar recupera o projeto salvo.");
            form.BeginProjectEdit();form.state.List.Mods.First().Tags.Add("tag salva");form.FinishProjectEdit();
            form.testProjectSaveChoice=()=>DialogResult.Yes;
            check(form.ConfirmProjectNavigation()&&form.projectDraft==null&&form.library.Load().List.Mods.First().Tags.Contains("tag salva"),
                "Salvar ao sair grava as edições da lista.");
            form.state.AppliedPreset=form.state.SelectedPreset;form.state.AppliedPresetFingerprint=form.PresetFingerprint;
            form.marked.Clear();form.updateFocusedId=null;
            form.DrawRows();
            foreach(var action in new[]{form.footerAction,form.primaryAction}){
                check(action.Visible&&!action.Enabled&&!action.TabStop&&action.Text=="","O espaço sem ação fica inativo.");
                using(var pixels=new Bitmap(action.Width,action.Height)){
                    action.DrawToBitmap(pixels,new Rectangle(Point.Empty,action.Size));
                    int background=action.Parent.BackColor.ToArgb();
                    check(Enumerable.Range(0,pixels.Width).All(x=>Enumerable.Range(0,pixels.Height)
                        .All(y=>pixels.GetPixel(x,y).ToArgb()==background)),"A ação vazia desaparece sem moldura ou resíduo.");
                }
            }
            CaptureUi(form,Path.Combine(root,"footer-idle.png"));
            form.marked.Add(a.Id);
            form.DrawRows();
            check(form.footerAction.Visible&&!form.footerAction.Enabled&&form.footerAction.Text=="","Marcar pendente deixa a ação secundária vazia.");
            form.statuses[Key(a,form.Selected(a))]="Instalado";
            form.DrawRows();
            check(form.footerAction.Enabled&&form.footerAction.Text=="Desinstalar"&&form.UninstallSelection().SequenceEqual(new[]{a.Id}),
                "A ação Desinstalar aparece para a seleção instalada.");
            var installedRow=form.ModRows().Single(row=>Equals(row.Tag,a.Id));
            form.grid.CurrentCell=installedRow.Cells["Name"];
            form.SelectTableRow(installedRow,Keys.Control,false);
            check(form.marked.Count==0&&!form.primaryAction.Enabled&&form.primaryAction.Text==""&&!form.footerAction.Enabled,
                "Retirar a última seleção apaga as duas ações, mesmo que a linha ainda tenha foco.");
            form.SelectTableRow(installedRow,Keys.None,false);
            check(form.primaryAction.Enabled&&form.primaryAction.Text=="Desabilitar"&&form.footerAction.Enabled,
                "Selecionar o mod novamente restaura as ações disponíveis.");
            var popupMenu=form.grid.ContextMenuStrip;
            form.presetProtected.Add(a.Id);
            popupMenu.Show(form.grid,new Point(20,20));
            check(popupMenu.Items.Find("ResumePresetManagement",false).Single().Available,"A retomada fica no contexto do mod protegido.");
            popupMenu.Close();
            form.presetProtected.Remove(a.Id);
            foreach(string theme in new[]{"light","light-red","dark"}){
                state.Theme=theme;form.BuildInterface();form.PerformLayout();await Task.Delay(40);
                var remove=form.footerAction;var install=AllControls(form).OfType<Button>().Single(button=>button.Name=="DownloadInstall");
                check(Math.Abs(remove.PointToScreen(Point.Empty).Y+remove.Height/2-install.PointToScreen(Point.Empty).Y-install.Height/2)<=1,"Botões do rodapé têm o mesmo centro vertical: "+theme);
                check(((GeneralButton)remove).Primary&&((GeneralButton)remove).Destructive&&remove.Height==install.Height&&remove.Font.Size==install.Font.Size&&remove.Font.Bold==install.Font.Bold,"Desinstalar segue altura, preenchimento e tipografia de Baixar/Instalar: "+theme);
                var search=AllControls(form).Single(control=>control.Name=="ModSearchSurface");
                var clear=AllControls(search).Single(control=>control.Name=="ClearSearch");
                int searchCenter=search.PointToScreen(new Point(0,search.Height/2)).Y;
                check(Math.Abs(clear.PointToScreen(new Point(0,clear.Height/2)).Y-searchCenter)<=1&&
                    Math.Abs(form.searchBox.PointToScreen(new Point(0,form.searchBox.Height/2)).Y-searchCenter)<=1,
                    "O X e a caixa de pesquisa ficam no centro vertical do contorno: "+theme);
                var expand=form.expandList;
                check(Math.Abs(expand.Top-(expand.Parent.ClientSize.Width-expand.Right))<=1,
                    "Expandir usa recuos superior e lateral equivalentes: "+theme);
                var verify=AllControls(form).OfType<Button>().Single(button=>button.Name=="VerifyProject");
                check(verify.PointToScreen(Point.Empty).Y==install.PointToScreen(Point.Empty).Y,"Verificar e a ação principal compartilham o alinhamento: "+theme);
                var windowBounds=form.Bounds;int rows=form.grid.RowCount,height=form.grid.Height;string selectedPreset=form.state.SelectedPreset;var markedBefore=form.marked.ToArray();
                form.expandList.PerformClick();form.PerformLayout();await Task.Delay(25);
                check(form.listExpanded&&form.Bounds==windowBounds&&form.grid.Height>height+100&&form.tableName.Visible&&form.expandList.Visible,"Expandir aumenta só a lista dentro da janela: "+theme);
                check(form.preset.Visible&&!form.presetSettings.Visible&&!form.optionsButton.Visible&&!remove.Visible&&!install.Visible&&!form.currentProgress.Visible,"Modo lista oculta controles externos e mantém ações do título: "+theme);
                check(form.grid.RowCount==rows&&form.state.SelectedPreset==selectedPreset&&form.marked.SetEquals(markedBefore)&&((GeneralButton)form.expandList).IconKind=="minimize","Expandir preserva itens, preset e seleção e oferece restaurar: "+theme);
                CaptureUi(form,Path.Combine(root,"lista-expandida-"+theme+".png"));form.expandList.PerformClick();form.PerformLayout();
                check(!form.listExpanded&&form.grid.Height==height&&form.preset.Visible&&remove.Visible&&install.Visible&&form.Bounds==windowBounds,"Restaurar recupera layout e controles sem maximizar o aplicativo: "+theme);
                form.optionsButton.PerformClick();var actions=form.menus.Current as GeneralActionsMenu;check(actions!=null&&actions.Visible,"Hamburger abre menu vertical: "+theme);CaptureUi(form,Path.Combine(root,"editor-"+theme+".png"));
                form.optionsButton.PerformClick();Application.DoEvents();check(form.menus.Current==null,"Segundo clique no hamburger fecha o menu: "+theme);
            }
        });
        var builtInStore=new GeneralProjectStore(Path.Combine(root,"builtin"));var real=FixedData.Load();
        var builtIn=builtInStore.Create("Cyberpunk 2077",GeneralModLibrary.Seed(real,new ModImportItem[0]),"light-red","pt",null,"cyberpunk-2077");
        var builtInState=builtInStore.Load(builtIn);
        TestForm(real,builtInState,builtInStore.DataPath(builtIn),root,async form=>{
            form.AttachProject(builtInStore,builtIn);form.UpdateTableTitle();
            check(FitProjectWindow(new Size(1240,1240),new Size(1920,1040),1f,false)==new Size(1040,1040)&&
                FitProjectWindow(new Size(1050,760),new Size(1920,1040),1f,true)==new Size(1050,760),
                "O padrão conserva a proporção e um tamanho personalizado é restaurado.");
            form.Size=new Size(1060,780);form.OnResizeEnd(EventArgs.Empty);
            var resized=builtInStore.Read().Projects.Single();
            check(resized.WindowWidth>0&&resized.WindowHeight>0,"Redimensionar grava a preferência do projeto.");
            check(!form.tableName.ReadOnly&&form.preset.DeleteChoice!=null,"Presets oficiais podem ser editados em uma cópia.");
            string presetName=form.ActivePreset.Name;
            form.optionsButton.PerformClick();var menu=(GeneralActionsMenu)form.menus.Current;
            check(AllControls(menu).OfType<Button>().Where(button=>button.Name!="").Select(button=>button.Name).OrderBy(id=>id).SequenceEqual(new[]{"details","new","open","options","save-menu","share"}),"Projeto padrão oferece captura manual e compartilhamento.");
            form.optionsButton.PerformClick();await Task.Delay(30);
            string beforeExport=Safe.Hash(Path.Combine(builtInStore.DataPath(builtIn),"lista.json"));form.ProjectCommand("export");form.ProjectCommand("import");
            check(form.Visible&&form.ProjectAction==null&&beforeExport==Safe.Hash(Path.Combine(builtInStore.DataPath(builtIn),"lista.json")),"Projeto padrão também recusa importação/exportação no despacho, sem alterar dados.");
            form.tableName.Text="Meu preset pessoal";form.presetSettings.PerformClick();
            var personal=builtInStore.Read().Projects.Single(item=>item.TemplateId==null);
            check(form.ActivePreset.Name=="Meu preset pessoal"&&!form.BuiltInProject&&
                builtInStore.Load(personal).List.Presets.First(item=>item.Id=="all").Name=="Meu preset pessoal"&&
                builtInStore.Load(builtIn).List.Presets.First(item=>item.Id=="all").Name==presetName&&
                beforeExport==Safe.Hash(Path.Combine(builtInStore.DataPath(builtIn),"lista.json")),
                "Salvar a edição cria um projeto pessoal e preserva o original.");
            form.tableName.Text="Segundo ajuste";form.presetSettings.PerformClick();
            check(builtInStore.Read().Projects.Count==2&&builtInStore.Load(personal).List.Presets.First(item=>item.Id=="all").Name=="Segundo ajuste",
                "O próximo salvamento atualiza a mesma cópia.");
            check(!Directory.Exists(Path.Combine(root,"archive")),"Testar presets não instala arquivos do jogo.");
        });
        GeneralStyle.UseTheme("light");GeneralStyle.Language="pt";
        Safe.WriteJson(Path.Combine(root,"resultado-preset-draft.json"),new{Passed=true,Tests=checks,RealGameTouched=false,NetworkUsed=false});
    }
}
