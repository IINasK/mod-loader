using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out int value,int size);
    internal static void RunFloatingTests(string parent){
        string root=OnlineTests.NestedRoot(parent,"floating-");Directory.CreateDirectory(root);
        string target=Path.Combine(root,"game");Directory.CreateDirectory(target);
        var a=NewEntry("Mod instalado","https://example.com/a.zip");var b=NewEntry("Mod pendente","https://example.com/b.zip");var c=NewEntry("Mod com erro","https://example.com/c.zip");var d=NewEntry("Download em andamento","https://example.com/d.zip");
        var list=new GeneralModList{Mods=new List<GeneralModEntry>{a,b,c,d}};list.Presets.Add(new GeneralModPreset{Id="test",Name="Kit teste",ModIds=new List<string>{a.Id,b.Id,c.Id,d.Id}});
        var state=new GeneralModState{List=list,Destination=target,DownloadFolder=root};
        var checks=new List<string>();Action<bool,string> check=(yes,message)=>{if(!yes)throw new IOException(message);checks.Add(message);};
        TestForm(FixedData.Load(),state,Path.Combine(root,"state"),root,async form=>{
            foreach(string name in new[]{"menu","list","refresh","down","up","check","close","folder"}){
                using(var image=new Bitmap(30,30))using(var g=Graphics.FromImage(image)){
                    g.Clear(Color.White);LucideIcons.Draw(g,name,new Rectangle(3,3,24,24),Color.Black);int count=0;
                    for(int x=0;x<30;x++)for(int y=0;y<30;y++)if(image.GetPixel(x,y).ToArgb()!=Color.White.ToArgb())count++;
                    check(count>8,"Ícone Lucide renderiza: "+name);
                }
            }
            form.statuses[Key(a,form.Selected(a))]="Instalado";
            form.statuses[Key(b,form.Selected(b))]="Instalado: Média";
            form.statuses[Key(c,form.Selected(c))]="Falhou";
            form.statuses[Key(d,form.Selected(d))]="Baixando";
            form.DrawRows();Application.DoEvents();
            check(form.counts.Text.Contains("Instalados: 1"),"Variante diferente não entra no contador de instalados.");
            check(form.counts.Text.Contains("Falhas: 1"),"Erros aparecem na contagem.");
            int row=form.ModRows().First().Index;
            form.SetHover(row);
            check(form.grid.Rows[row].DefaultCellStyle.BackColor==GeneralStyle.Selection,"Hover destaca uma linha de mod.");
            form.SetHover(-1);
            check(form.grid.Rows[row].DefaultCellStyle.BackColor==GeneralStyle.Surface,"Sair da linha restaura o fundo.");
            CaptureUi(form,Path.Combine(root,"status-claro.png"));

            using(var dialog=new GeneralOptionsDialog("light","pt",root)){
                dialog.StartPosition=FormStartPosition.Manual;dialog.Location=new Point(-30000,-30000);dialog.Show(form);Application.DoEvents();
                var theme=(GeneralPicker)typeof(GeneralOptionsDialog).GetField("theme",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(dialog);
                var language=(GeneralPicker)typeof(GeneralOptionsDialog).GetField("language",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(dialog);
                var folder=(TextBox)typeof(GeneralOptionsDialog).GetField("folder",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(dialog);
                check(theme.PointToScreen(Point.Empty).Y==language.PointToScreen(Point.Empty).Y&&theme.Height==44&&language.Height==44,"Tema e idioma têm a mesma linha e altura.");
                var browse=AllControls(dialog).OfType<Button>().Single(x=>x.Text=="Escolher pasta");
                check(browse.Height==44&&folder.Parent.Height==44&&browse.PointToScreen(Point.Empty).Y==folder.Parent.PointToScreen(Point.Empty).Y,"Pasta e Escolher pasta compartilham altura e alinhamento.");
                var save=AllControls(dialog).OfType<Button>().Single(x=>x.Text=="Salvar");
                var cancel=AllControls(dialog).OfType<Button>().Single(x=>x.Text=="Cancelar");
                check(save.Left>cancel.Left&&save.Parent.Width-save.Right<=2,"Salvar e Cancelar terminam à direita.");
                check(dialog.Content.Height-save.Parent.Parent.Bottom<=22,"Rodapé de opções não deixa faixa vazia extra.");
                CaptureUi(dialog,Path.Combine(root,"opcoes-claro.png"));
                dialog.Close();
            }

            GeneralStyle.AnimationsEnabled=true;
            form.preset.Toggle();Application.DoEvents();
            var popup=form.preset.OpenPopup;
            check(popup!=null&&popup.Visible&&popup.Region!=null,"Seletor abre popup próprio com recorte arredondado.");
            await Task.Delay(260);
            check(popup.Visible&&popup.Region.IsVisible(popup.Width/2,popup.Height/2)&&popup.Region.IsVisible(15,popup.Height-15),"Seletor termina a expansão visível e sem recortar as opções. Visible="+popup.Visible+" Size="+popup.Size+" Client="+popup.ClientSize);
            int original=form.preset.SelectedIndex;
            var choices=AllControls(popup.Items.OfType<ToolStripControlHost>().Single().Control).OfType<ListBox>().Single();
            choices.SelectedIndex=Math.Min(choices.Items.Count-1,original+1);
            check(form.preset.SelectedIndex==original,"Navegar no seletor não altera a escolha antes de confirmar.");
            popup.Close(ToolStripDropDownCloseReason.Keyboard);
            check(popup.Visible,"Fechamento do seletor inicia a contração.");
            await Until(()=>!popup.Visible);
            check(form.preset.SelectedIndex==original,"Fechar seletor preserva o valor.");
            form.preset.Toggle();await Task.Delay(260);
            popup=form.preset.OpenPopup;
            choices=AllControls(popup.Items.OfType<ToolStripControlHost>().Single().Control).OfType<ListBox>().Single();
            choices.SelectedIndex=Math.Min(choices.Items.Count-1,original+1);
            typeof(ListBox).GetMethod("OnKeyDown",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(choices,new object[]{new KeyEventArgs(Keys.Enter)});
            await Until(()=>!popup.Visible);
            check(form.preset.SelectedIndex==Math.Min(choices.Items.Count-1,original+1),"Enter confirma a escolha no seletor.");
            form.preset.SelectedIndex=original;
            using(var menu=new GeneralActionsMenu()){
                menu.StartPosition=FormStartPosition.Manual;menu.Location=new Point(-30000,-30000);menu.Show(form);await Task.Delay(260);
                check(menu.Visible&&menu.Region.IsVisible(15,menu.Height-15),"Menu único mostra as ações por inteiro após a animação.");
                menu.Close();await Until(()=>!menu.Visible);
                check(!menu.Visible,"Menu único completa o fechamento.");
            }
            using(var dialog=new GeneralOptionsDialog("light","pt",root)){
                dialog.AnchorTo(form.optionsButton);
                check(dialog.StartPosition==FormStartPosition.Manual&&Screen.FromControl(form).WorkingArea.Contains(dialog.Bounds),"Opções usa ancoragem e permanece dentro da área visível.");
                dialog.StartPosition=FormStartPosition.Manual;dialog.Location=new Point(-30000,-30000);
                dialog.Shown+=async(s,e)=>{await Task.Delay(260);check(dialog.Region.IsVisible(15,dialog.Height-15)&&dialog.Region.IsVisible(dialog.Width/2,dialog.Height/2),"Diálogo animado termina mostrando todo o conteúdo.");dialog.Complete(DialogResult.OK);};
                var result=dialog.ShowDialog(form);
                check(result==DialogResult.OK,"Animação de fechamento preserva o resultado OK do diálogo.");
            }
            GeneralStyle.AnimationsEnabled=false;
            form.state.Theme="dark";form.state.Language="en";form.BuildInterface();form.PerformLayout();Application.DoEvents();
            int caption;
            if(DwmGetWindowAttribute(form.Handle,35,out caption,4)==0)check(caption==ColorTranslator.ToWin32(GeneralStyle.Background),"A barra de título usa a cor do tema escuro.");
            var scrollbar=((GeneralScrollView)form.grid.Parent.Parent).Bar;
            using(var image=new Bitmap(scrollbar.Width,scrollbar.Height)){
                scrollbar.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));
                check(image.GetPixel(1,image.Height/2).GetBrightness()<.35f,"A trilha da barra de rolagem fica escura.");
            }
            CaptureUi(form,Path.Combine(root,"status-escuro.png"));
            using(var dialog=new GeneralOptionsDialog("dark","en",root)){
                dialog.StartPosition=FormStartPosition.Manual;dialog.Location=new Point(-30000,-30000);dialog.Show(form);Application.DoEvents();CaptureUi(dialog,Path.Combine(root,"opcoes-escuro.png"));dialog.Close();
            }
            check(GeneralStyle.Status("Baixando")=="Downloading","Legenda traduzida acompanha a cor do estado.");
            form.state.Theme="light";form.BuildInterface();Application.DoEvents();
            scrollbar=((GeneralScrollView)form.grid.Parent.Parent).Bar;
            using(var image=new Bitmap(scrollbar.Width,scrollbar.Height)){
                scrollbar.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));
                check(image.GetPixel(1,image.Height/2).GetBrightness()>.65f,"Voltar ao tema claro restaura a barra de rolagem clara.");
            }
        });
        GeneralStyle.Dark=false;GeneralStyle.Language="pt";GeneralStyle.AnimationsEnabled=true;
        Safe.WriteJson(Path.Combine(root,"resultado-floating.json"),new{Passed=true,Tests=checks,RealGameTouched=false,NetworkUsed=false});
    }
    static IEnumerable<Control> AllControls(Control root){foreach(Control child in root.Controls){yield return child;foreach(var descendant in AllControls(child))yield return descendant;}}
}
