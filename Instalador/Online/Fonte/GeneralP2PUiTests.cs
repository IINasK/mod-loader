using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

internal static class GeneralP2PUiTests {
    internal static int Run(string parent){
        string root=OnlineTests.NestedRoot(parent,"p2p-ui-");Directory.CreateDirectory(root);var checks=new List<string>();
        Action<bool,string> check=(ok,text)=>{if(!ok)throw new IOException(text);checks.Add(text);};
        bool dark=GeneralStyle.Dark,red=GeneralStyle.RedTheme,animation=GeneralStyle.AnimationsEnabled;string language=GeneralStyle.Language;
        try{
            var catalog=FixedData.Load();var state=new GeneralModState{List=GeneralModLibrary.Seed(catalog,new ModImportItem[0]),Destination=Path.Combine(root,"game")};string before=Safe.Json.Serialize(state);
            using(var service=new P2PService(Path.Combine(root,"private"),Path.Combine(root,"transport"),true)){
                foreach(string theme in new[]{"light","light-red","dark"})foreach(string locale in new[]{"pt","en"}){
                    GeneralStyle.UseTheme(theme);GeneralStyle.Language=locale;GeneralStyle.AnimationsEnabled=false;
                    using(var dialog=new GeneralP2PDialog(service,state,state.List.Mods.Select(m=>m.Id),catalog,new DownloadCache(Path.Combine(root,"cache")),false)){
                        dialog.ShowInTaskbar=false;dialog.StartPosition=FormStartPosition.Manual;dialog.Location=new Point(-30000,-30000);dialog.Show();Application.DoEvents();
                        var input=Find<TextBox>(dialog,"p2p-input");var output=Find<TextBox>(dialog,"p2p-output");var destination=Find<TextBox>(dialog,"p2p-destination");var connect=Find<Button>(dialog,"p2p-connect");var pause=Find<Button>(dialog,"p2p-pause");
                        check(output.ReadOnly&&!input.ReadOnly&&input.MaxLength==12000,"Link de envio protegido e link recebido editável/limitado: "+theme+locale);
                        check(connect.Enabled&&!pause.Visible&&!destination.Visible,"Estado inicial oferece Conectar sem lista de sessões ou pausa vazia: "+theme+locale);
                        check(!Children(dialog.Content).OfType<ListBox>().Any()&&!Children(dialog.Content).OfType<CheckBox>().Any(),"Sem lista, seleção de configuração ou ações legadas: "+theme+locale);
                        var offered=new P2PInvite{Device=String.Join("-",Enumerable.Repeat("AAAAAAA",8)),Folder="ml-"+new string('a',32),ManifestHash=new string('b',64),Kind="offer",Name="Pacote do amigo",Mode="installed-zip",Size=1024};input.Text=offered.Encode();Application.DoEvents();
                        check(destination.Visible&&destination.ReadOnly&&Find<Button>(dialog,"p2p-folder").Enabled,"Colar convite mostra destino local e escolha de pasta antes de conectar: "+theme+locale);
                        using(var image=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(root,"layout-"+theme+"-"+locale+".png"),System.Drawing.Imaging.ImageFormat.Png);}
                        Safe.WriteJson(Path.Combine(root,"layout.json"),Children(dialog.Content).Where(c=>!String.IsNullOrEmpty(c.Name)||c is TableLayoutPanel).Select(c=>new{Name=c.Name,Type=c.GetType().Name,Bounds=c.Bounds.ToString(),Parent=c.Parent.ClientRectangle.ToString()}).ToArray());
                        foreach(Control control in new Control[]{input,output,destination,connect,Find<Button>(dialog,"p2p-folder"),Find<Button>(dialog,"p2p-copy"),Find<Label>(dialog,"p2p-status")})check(Inside(control,dialog.Content),"Controle sem recorte: "+control.Name+theme+locale);
                        check(input.Parent is GeneralSurface&&output.Parent is GeneralSurface&&input.Width>300&&output.Width>300&&input.Height>=input.Font.Height*2,"Campos de links usam superfícies e largura legível: "+theme+locale);
                        input.Clear();var session=new P2PSession{Id=offered.Folder,Name=offered.Name,Hash=offered.ManifestHash,Sender=false,Mode="installed-zip",Size=1024};
                        typeof(GeneralP2PDialog).GetMethod("SelectSession",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(dialog,new object[]{session});
                        check(pause.Visible&&!pause.Enabled&&destination.Visible,"Pausa existe no recebimento e aguarda motor conectado: "+theme+locale);
                        typeof(GeneralP2PDialog).GetField("remotePaused",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(dialog,true);typeof(GeneralP2PDialog).GetMethod("UpdateActions",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(dialog,null);
                        check(pause.Text==(locale=="pt"?"Pausado pelo amigo":"Paused by friend")&&!pause.Enabled,"Pausa remota não oferece destravar o amigo: "+theme+locale);
                        session.Paused=true;typeof(GeneralP2PDialog).GetMethod("UpdateActions",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(dialog,null);check(pause.Text==(locale=="pt"?"Retomar":"Resume"),"Quem pausou recebe ação de retomar sua própria pausa: "+theme+locale);
                        using(var image=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(root,"p2p-"+theme+"-"+locale+".png"),System.Drawing.Imaging.ImageFormat.Png);}dialog.Close();
                    }
                }
                check(!service.Engine.Running&&!Directory.Exists(service.Store),"Renderização não inicia rede nem cria pacotes.");check(Safe.Json.Serialize(state)==before&&!Directory.Exists(state.Destination),"Formulário preserva estado e não toca no jogo.");
            }
            Safe.WriteJson(Path.Combine(root,"resultado-p2p-ui.json"),new{Passed=true,Tests=checks,NetworkUsed=false,RealGameTouched=false});return checks.Count;
        }finally{GeneralStyle.Dark=dark;GeneralStyle.RedTheme=red;GeneralStyle.Language=language;GeneralStyle.AnimationsEnabled=animation;}
    }
    internal static void Preview(string parent){Run(parent);}
    static T Find<T>(Control root,string name)where T:Control{return Children(root).OfType<T>().Single(c=>c.Name==name);}
    static IEnumerable<Control> Children(Control root){foreach(Control child in root.Controls){yield return child;foreach(var nested in Children(child))yield return nested;}}
    static bool Inside(Control item,Control root){for(Control current=item;current!=root;current=current.Parent){if(current.Parent==null||!current.Parent.ClientRectangle.Contains(new Rectangle(current.Location,current.Size)))return false;}return true;}
}
