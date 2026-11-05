using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

internal static class GeneralP2PUiTests {
    internal static int Run(string parent) {
        string root=Path.Combine(Path.GetFullPath(parent),"p2p-ui-testes-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var checks=new List<string>();var failures=new List<string>();
        try {
            Exercise(root,checks,failures);
            if(failures.Count!=0)throw new IOException(String.Join("\r\n",failures));
            return checks.Count;
        } catch(Exception ex) {
            if(failures.Count==0)failures.Add(ex.GetType().Name+": "+ex.Message);
            throw;
        } finally {
            Safe.WriteJson(Path.Combine(root,"resultado-p2p-ui.json"),new{Passed=failures.Count==0,Tests=checks,Failures=failures,RealGameTouched=false,NetworkUsed=false,EngineStarted=false});
        }
    }

    internal static void Preview(string outputFolder) {
        string root=Path.GetFullPath(outputFolder);Directory.CreateDirectory(root);
        Exercise(root,new List<string>(),new List<string>());
    }

    static void Exercise(string root,List<string> checks,List<string> failures) {
        if(Thread.CurrentThread.GetApartmentState()!=ApartmentState.STA)throw new IOException("Os testes visuais P2P precisam da thread STA do aplicativo.");
        bool oldDark=GeneralStyle.Dark,oldAnimations=GeneralStyle.AnimationsEnabled;string oldLanguage=GeneralStyle.Language;
        Action<bool,string> check=(ok,message)=>{if(ok)checks.Add(message);else failures.Add(message);};
        var catalog=FixedData.Load();var list=GeneralModLibrary.Seed(catalog,new ModImportItem[0]);
        var preset=list.Presets.First();
        var state=new GeneralModState{List=list,SelectedPreset=preset.Id,Destination=Path.Combine(root,"unused-game"),DownloadFolder=Path.Combine(root,"unused-downloads")};
        string before=Safe.Json.Serialize(state);
        var cache=new DownloadCache(Path.Combine(root,"fixture-cache"));
        using(var service=new P2PService(Path.Combine(root,"fixture-private"),Path.Combine(root,"fixture-transport"),true)) {
            service.Sessions.Add(new P2PSession{Id="ml-"+new string('a',32),Name="Preset para amigos · exemplo recebido",Hash=new string('b',64),Sender=false});
            service.Sessions.Add(new P2PSession{Id="ml-"+new string('c',32),Name="Preset para amigos · exemplo enviado",Hash=new string('d',64),Sender=true});
            try {
                GeneralStyle.AnimationsEnabled=false;
                foreach(bool dark in new[]{false,true})foreach(string language in new[]{"pt","en"}) {
                    GeneralStyle.Dark=dark;GeneralStyle.Language=language;
                    string label=(dark?"escuro":"claro")+"-"+language;
                    using(var dialog=new GeneralP2PDialog(service,state,preset.ModIds,catalog,cache,false)) {
                        dialog.ShowInTaskbar=false;dialog.StartPosition=FormStartPosition.Manual;dialog.Location=new Point(-30000,-30000);
                        dialog.ClientSize=new Size(900,810);dialog.Show();Layout(dialog);
                        Populate(dialog);
                        Capture(dialog,Path.Combine(root,"p2p-"+label+"-900x810.png"));
                        CheckDialog(dialog,label+" normal",check);
                        var sessionList=(ListBox)dialog.Content.Controls.Find("p2p-sessions",true).Single();
                        sessionList.SelectedIndex=0;Application.DoEvents();
                        check(!FindButton(dialog,"p2p-load").Enabled&&!FindButton(dialog,"p2p-install").Enabled,label+": selecionar recebido sem conexão não autoriza carregar/instalar.");
                        sessionList.SelectedIndex=1;Application.DoEvents();
                        check(!FindButton(dialog,"p2p-load").Enabled&&!FindButton(dialog,"p2p-install").Enabled,label+": selecionar envio sem conexão não autoriza carregar/instalar.");
                        check(dialog.ReceivedList==null&&!dialog.InstallAfterReceive,label+": prévia não importa lista nem solicita instalação.");
                        dialog.ClientSize=new Size(900,720);Layout(dialog);
                        Capture(dialog,Path.Combine(root,"p2p-"+label+"-900x720.png"));
                        CheckDialog(dialog,label+" compacto (tela1024x768)",check);
                        dialog.Close();
                    }
                    using(var menu=new GeneralActionsMenu()) {
                        menu.ShowInTaskbar=false;menu.StartPosition=FormStartPosition.Manual;menu.Location=new Point(-30000,-30000);menu.Show();Layout(menu);
                        var buttons=Descendants(menu.Content).OfType<Button>().ToArray();
                        check(buttons.Count(b=>b.Name=="share")==1&&buttons.Count(b=>b.Name=="options")==1,label+": menu oferece Compartilhar e Opções uma vez.");
                        check(buttons.Single(b=>b.Name=="export").Text==(language=="pt"?"Exportar lista":"Export list"),label+": ação de lista traduzida no menu.");
                        Capture(menu,Path.Combine(root,"p2p-menu-"+label+".png"));menu.Close();
                    }
                }
                check(!service.Engine.Running,"Prévia e testes não iniciam o auxiliar nem usam rede.");
                check(Safe.Json.Serialize(state)==before,"Renderizar/selecionar sessões preserva lista, preset, fontes e escolhas locais.");
                check(!Directory.Exists(state.Destination)&&!Directory.Exists(state.DownloadFolder),"Testes não criam destino de jogo ou pasta de Downloads.");
                check(!Directory.Exists(service.Store)&&!Directory.Exists(Path.Combine(service.Root,"syncthing")),"Sessões ilustrativas não criam transferências nem perfil Syncthing.");
            } finally {GeneralStyle.Dark=oldDark;GeneralStyle.Language=oldLanguage;GeneralStyle.AnimationsEnabled=oldAnimations;}
        }
    }

    static void Populate(GeneralP2PDialog dialog) {
        var input=(TextBox)dialog.Content.Controls.Find("p2p-input",true).Single();
        var output=(TextBox)dialog.Content.Controls.Find("p2p-output",true).Single();
        input.Text="ML-P2P1:EXEMPLO-DE-CONVITE-SEM-CONEXAO\r\nConvite de demonstração; nenhum arquivo real.";
        output.Text="ML-P2P1:EXEMPLO-DE-RESPOSTA-SEM-CONEXAO\r\nResposta de demonstração; nenhum arquivo real.";
        Layout(dialog);
    }

    static void CheckDialog(GeneralP2PDialog dialog,string label,Action<bool,string> check) {
        check(dialog.Visible&&dialog.IsHandleCreated,label+": formulário real mostrado e layout executado.");
        check(dialog.Font.FontFamily.Name.IndexOf("Jost",StringComparison.OrdinalIgnoreCase)>=0,label+": fonte Jost compartilhada em uso.");
        var intro=(Label)dialog.Content.Controls.Find("p2p-intro",true).Single();
        int introHeight=TextRenderer.MeasureText(intro.Text,intro.Font,new Size(intro.ClientSize.Width,Int32.MaxValue),TextFormatFlags.WordBreak).Height;
        check(intro.ClientSize.Height>=introHeight,label+": instruções do pacote instalado completas.");
        var settings=(CheckBox)dialog.Content.Controls.Find("p2p-settings",true).Single();
        check(InsideAncestors(settings,dialog.Content)&&settings.Height>=settings.Font.Height+4,label+": opção de configurações sem recorte.");
        var buttons=Descendants(dialog.Content).OfType<Button>().Where(b=>b.Visible).ToArray();
        check(buttons.Length==10,label+": dez ações disponíveis no corpo do diálogo.");
        foreach(var button in buttons) {
            string name=String.IsNullOrEmpty(button.Name)?button.Text:button.Name;
            check(button.Height==44,label+": botão44px: "+name);
            check(InsideAncestors(button,dialog.Content),label+": ação sem recorte: "+name);
            check(!button.Enabled,label+": ação aguarda conexão: "+name);
            check(button is GeneralButton,label+": ação usa botão G3 compartilhado: "+name);
            int textWidth=TextRenderer.MeasureText(button.Text,button.Font,new Size(Int32.MaxValue,44),TextFormatFlags.SingleLine|TextFormatFlags.NoPadding).Width;
            check(button.Width>=textWidth+button.Padding.Horizontal,label+": texto completo no botão: "+name);
        }
        foreach(var field in Descendants(dialog.Content).OfType<TextBox>()) {
            check(InsideAncestors(field,dialog.Content),label+": campo completo: "+field.Name);
            check(field.Parent is GeneralSurface&&field.BorderStyle==BorderStyle.None,label+": campo usa superfície G3: "+field.Name);
            check(field.Width>=300&&field.Height>=field.Font.Height*2,label+": campo legível em duas linhas: "+field.Name);
            check(field.ForeColor.ToArgb()!=field.BackColor.ToArgb(),label+": contraste de texto: "+field.Name);
        }
        var output=(TextBox)dialog.Content.Controls.Find("p2p-output",true).Single();
        var input=(TextBox)dialog.Content.Controls.Find("p2p-input",true).Single();
        check(output.ReadOnly&&!input.ReadOnly&&input.MaxLength==12000,label+": resposta protegida e convite limitado a12000caracteres.");
        var status=dialog.Content.Controls.Find("p2p-status",true).Single();
        check(status.Height>=status.Font.Height&&InsideAncestors(status,dialog.Content),label+": rodapé de situação visível sem recorte.");
        check(InsideAncestors(dialog.Content.Controls.Find("p2p-sessions",true).Single(),dialog.Content),label+": lista de sessões dentro da área útil.");
    }

    static Button FindButton(GeneralP2PDialog dialog,string name){return (Button)dialog.Content.Controls.Find(name,true).Single();}
    static IEnumerable<Control> Descendants(Control root) {
        foreach(Control child in root.Controls){yield return child;foreach(var nested in Descendants(child))yield return nested;}
    }
    static bool InsideAncestors(Control control,Control root) {
        for(Control current=control;current!=root;current=current.Parent) {
            if(current.Parent==null||current.Width<=0||current.Height<=0)return false;
            var bounds=new Rectangle(current.Parent.PointToClient(current.PointToScreen(Point.Empty)),current.Size);
            if(!current.Parent.ClientRectangle.Contains(bounds))return false;
        }
        return true;
    }
    static void Layout(Form form){form.PerformLayout();Application.DoEvents();form.PerformLayout();Application.DoEvents();}
    static void Capture(Form form,string path){using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(path,System.Drawing.Imaging.ImageFormat.Png);}}
}
