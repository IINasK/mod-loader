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
        var state=new GeneralModState{Destination=root,DownloadFolder=root,SelectedPreset="kit",List=new GeneralModList{Mods=new List<GeneralModEntry>{a,b},Presets=new List<GeneralModPreset>{saved}}};
        var checks=new List<string>();Action<bool,string> check=(yes,message)=>{if(!yes)throw new IOException(message);checks.Add(message);};
        TestForm(FixedData.Load(),state,Path.Combine(root,"state"),root,async form=>{
            form.Persist();form.CapturePresetDraft();form.UpdateTableTitle();
            check(!form.presetSettings.Enabled&&form.deletePreset.Enabled,"Salvar aguarda alterações; excluir requer um preset.");
            check(((GeneralButton)form.presetSettings).IconKind=="save"&&((GeneralButton)form.deletePreset).IconKind=="trash","Preset usa os ícones oficiais de disquete e lixeira.");
            check(form.preset.PointToScreen(Point.Empty).Y==form.presetSettings.PointToScreen(Point.Empty).Y&&form.presetSettings.Right==form.deletePreset.Left&&form.tableName.Parent.Controls.Find("DiscardPreset",false).Length==0,
                "Disquete e lixeira ficam adjacentes, sem X ou coluna vazia.");
            check(form.preset.IconOnly&&form.preset.Parent==form.tableName.Parent&&form.presetSettings.Parent==form.tableName.Parent&&form.mainLayout.GetControlFromPosition(0,2)==null,"Seletor e ações integrados ao título, sem a linha Preset duplicada.");
            check(!form.grid.Columns.Contains("ResetPreset"),"A lixeira antiga não fica duplicada no cabeçalho da tabela.");
            form.tableName.Text="Rascunho";form.marked.Add(a.Id);form.DrawRows();
            check(form.ActivePreset.Name=="Original"&&form.library.Load().List.Presets.Single().Name=="Original"&&form.presetSettings.Enabled,"Nome digitado e seleção são rascunhos até salvar.");
            form.marked.Clear();form.DrawRows();
            form.tableName.Text="Renomeado";form.presetSettings.PerformClick();
            check(form.ActivePreset.Name=="Renomeado"&&form.ActivePreset.ModIds.Count==2&&form.library.Load().List.Presets.Single().Name=="Renomeado","Salvar só o nome preserva os membros do preset.");
            form.marked.Add(a.Id);form.DrawRows();form.presetSettings.PerformClick();
            check(form.ActivePreset.ModIds.SequenceEqual(new[]{a.Id})&&form.state.List.Mods.Count==2,"Disquete salva a seleção sem excluir mods da biblioteca.");
            form.marked.Add(b.Id);form.statusFilters=new HashSet<string>();form.DrawRows();form.presetSettings.PerformClick();
            check(form.ActivePreset.ModIds.Count==2,"Salvar preserva os itens marcados que um filtro está escondendo.");
            form.ResetColumnFilters();form.marked.Remove(b.Id);form.DrawRows();form.presetSettings.PerformClick();
            form.tableName.Text="   ";check(!form.presetSettings.Enabled,"Nome vazio impede salvar.");form.tableName.Text="Renomeado";
            using(var timer=new Timer{Interval=60}){
                timer.Tick+=(s,e)=>{var dialog=Application.OpenForms.OfType<GeneralDialog>().FirstOrDefault(item=>item.Text=="Excluir preset");if(dialog==null)return;timer.Stop();((Button)dialog.AcceptButton).PerformClick();};timer.Start();form.deletePreset.PerformClick();
            }
            check(form.state.List.Presets.Count==0&&form.state.List.Mods.Count==2&&!form.deletePreset.Enabled,"Lixeira exclui somente o preset e fica desativada em Todos os itens.");
            CompleteProjectName("Meu novo preset",()=>form.presetSettings.PerformClick());
            check(form.ActivePreset!=null&&form.ActivePreset.Name=="Meu novo preset"&&form.ActivePreset.ModIds.SequenceEqual(new[]{a.Id}),"Disquete em Todos os itens cria preset a partir da seleção.");
            foreach(string theme in new[]{"light","light-red","dark"}){
                state.Theme=theme;form.BuildInterface();form.PerformLayout();await Task.Delay(40);
                var remove=AllControls(form).OfType<Button>().Single(button=>button.Name=="UninstallSelected");var install=AllControls(form).OfType<Button>().Single(button=>button.Name=="DownloadInstall");
                check(Math.Abs(remove.PointToScreen(Point.Empty).Y+remove.Height/2-install.PointToScreen(Point.Empty).Y-install.Height/2)<=1,"Botões do rodapé têm o mesmo centro vertical: "+theme);
                check(((GeneralButton)remove).Primary&&((GeneralButton)remove).Destructive&&remove.Height==install.Height&&remove.Font.Size==install.Font.Size&&remove.Font.Bold==install.Font.Bold,"Desinstalar segue altura, preenchimento e tipografia de Baixar/Instalar: "+theme);
                check(form.detail.Parent==remove.Parent&&form.detail.Parent==install.Parent&&form.detail.Left>=remove.Right&&form.detail.Right<=install.Left,"Mensagem fica entre os botões, na mesma linha do rodapé: "+theme);
                var windowBounds=form.Bounds;int rows=form.grid.RowCount,height=form.grid.Height;string selectedPreset=form.state.SelectedPreset;var markedBefore=form.marked.ToArray();
                form.expandList.PerformClick();form.PerformLayout();await Task.Delay(25);
                check(form.listExpanded&&form.Bounds==windowBounds&&form.grid.Height>height+100&&form.tableName.Visible&&form.expandList.Visible,"Expandir aumenta só a lista dentro da janela: "+theme);
                check(form.preset.Visible&&form.presetSettings.Visible&&!form.optionsButton.Visible&&!remove.Visible&&!install.Visible&&!form.currentProgress.Visible,"Modo lista oculta controles externos e mantém ações do título: "+theme);
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
            check(form.ProtectedPreset&&form.tableName.ReadOnly&&!form.deletePreset.Enabled,"Presets fornecidos pelo projeto padrão não podem ser renomeados nem excluídos.");
            string presetName=form.ActivePreset.Name;int count=form.state.List.Presets.Count;
            form.optionsButton.PerformClick();var menu=(GeneralActionsMenu)form.menus.Current;
            check(AllControls(menu).OfType<Button>().Where(button=>button.Name!="").Select(button=>button.Name).OrderBy(id=>id).SequenceEqual(new[]{"clone","new","open","options","share"}),"Projeto padrão oferece novo/abrir/opções e exportação somente leitura do modpack.");
            form.optionsButton.PerformClick();await Task.Delay(30);
            string beforeExport=Safe.Hash(Path.Combine(builtInStore.DataPath(builtIn),"lista.json"));form.ProjectCommand("export");form.ProjectCommand("import");
            check(form.Visible&&form.ProjectAction==null&&beforeExport==Safe.Hash(Path.Combine(builtInStore.DataPath(builtIn),"lista.json")),"Projeto padrão também recusa importação/exportação no despacho, sem alterar dados.");
            form.marked.Add(form.state.List.Mods.First().Id);form.DrawRows();
            CompleteProjectName("Meu preset pessoal",()=>form.presetSettings.PerformClick());
            check(form.ActivePreset.Name=="Meu preset pessoal"&&!form.ProtectedPreset&&!form.tableName.ReadOnly&&form.state.List.Presets.First(item=>item.Id=="all").Name==presetName,"Salvar personalização de padrão cria um preset pessoal e preserva o original.");
            check(!Directory.Exists(Path.Combine(root,"archive")),"Testar presets não instala arquivos do jogo.");
        });
        GeneralStyle.UseTheme("light");GeneralStyle.Language="pt";
        Safe.WriteJson(Path.Combine(root,"resultado-preset-draft.json"),new{Passed=true,Tests=checks,RealGameTouched=false,NetworkUsed=false});
    }
}
