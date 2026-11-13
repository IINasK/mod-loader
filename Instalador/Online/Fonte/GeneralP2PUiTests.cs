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
        bool dark=GeneralStyle.Dark,red=GeneralStyle.RedTheme,black=GeneralStyle.BlackTheme,animation=GeneralStyle.AnimationsEnabled;string language=GeneralStyle.Language;
        try{
            var catalog=FixedData.Load();var state=new GeneralModState{List=GeneralModLibrary.Seed(catalog,new ModImportItem[0]),Destination=Path.Combine(root,"game")};string before=StateContents(state);
            string localGame=Path.Combine(root,"local-game"),otherGame=Path.Combine(root,"another-game");
            foreach(string game in new[]{localGame,otherGame}){
                string bin=Path.Combine(game,"bin","x64");
                Directory.CreateDirectory(bin);
                File.WriteAllText(Path.Combine(bin,"Cyberpunk2077.exe"),"fixture");
            }
            var foundGames=new List<string>{localGame};
            using(var service=new P2PService(Path.Combine(root,"private"),Path.Combine(root,"transport"),true)){
                foreach(string theme in new[]{"light","light-red","dark","black"})foreach(string locale in new[]{"pt","en"}){
                    GeneralStyle.UseTheme(theme);GeneralStyle.Language=locale;GeneralStyle.AnimationsEnabled=false;
                    using(var dialog=new GeneralP2PDialog(service,state,catalog,false,()=>System.Threading.Tasks.Task.FromResult(foundGames))){
                        dialog.ShowInTaskbar=false;dialog.StartPosition=FormStartPosition.Manual;dialog.Location=new Point(-30000,-30000);dialog.Show();
                        // The test supplies the transfer status explicitly.
                        ((Timer)typeof(GeneralP2PDialog).GetField("timer",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(dialog)).Stop();
                        Application.DoEvents();
                        var input=Find<TextBox>(dialog,"p2p-input");var output=Find<TextBox>(dialog,"p2p-output");var destination=Find<TextBox>(dialog,"p2p-destination");var connect=Find<Button>(dialog,"p2p-connect");
                        check(output.ReadOnly&&!input.ReadOnly&&input.MaxLength==12000,"Link de envio protegido e link recebido editável/limitado: "+theme+locale);
                        check(!Find<Button>(dialog,"p2p-copy").Enabled,"Empty invitation is not copyable: "+theme+locale);
                        var generate=Find<Button>(dialog,"modpack-generate");check(generate.Enabled&&!service.Engine.Running,"Offline modpack generation is available without a running P2P engine: "+theme+locale);
                        check(!connect.Enabled&&dialog.Content.Controls.Find("p2p-pause",true).Length==0&&!destination.Visible,"Estado inicial oferece Conectar sem lista de sessões ou pausa vazia: "+theme+locale);
                        check(!Children(dialog.Content).OfType<ListBox>().Any()&&!Children(dialog.Content).OfType<CheckBox>().Any(),"Sem lista, seleção de configuração ou ações legadas: "+theme+locale);
                        var offered=new P2PInvite{Device=String.Join("-",Enumerable.Repeat("AAAAAAA",8)),Folder="ml-"+new string('a',32),ManifestHash=new string('b',64),Kind="offer",Name="Pacote do amigo",Mode="installed-zip",Size=1024};input.Text=offered.Encode();output.Text=offered.Encode();Application.DoEvents();
                        check(destination.Visible&&destination.ReadOnly&&Find<Button>(dialog,"p2p-folder").Enabled,"Colar convite mostra destino local e escolha de pasta antes de conectar: "+theme+locale);
                        var folderHint=Find<Label>(dialog,"p2p-folder-hint");
                        offered.SuggestedFolder=Directory.CreateDirectory(Path.Combine(root,"suggested")).FullName;
                        input.Text=offered.Encode();Application.DoEvents();
                        check(destination.Text==offered.SuggestedFolder&&!folderHint.Visible,
                            "Convite preenche uma pasta válida neste PC: "+theme+locale);
                        string manual=Directory.CreateDirectory(Path.Combine(root,"manual")).FullName;
                        destination.Text=manual;
                        typeof(GeneralP2PDialog).GetMethod("UpdateActions",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(dialog,null);
                        check(destination.Text==manual,"Atualizar a tela preserva a escolha manual: "+theme+locale);
                        offered.SuggestedFolder=Path.Combine(root,"missing");
                        input.Text=offered.Encode();Application.DoEvents();
                        check(destination.Text.Length==0&&folderHint.Visible&&Inside(folderHint,dialog.Content),
                            "Novo convite com pasta indisponível deixa o destino vazio: "+theme+locale);
                        offered.SuggestedFolder=null;offered.PrivateFolder=true;input.Text=offered.Encode();Application.DoEvents();
                        check(folderHint.Visible&&folderHint.Text.Contains(locale=="pt"?"pasta pessoal":"personal folder")&&destination.Text.Length==0,
                            "Pasta pessoal vira aviso e não preenche o destino: "+theme+locale);
                        offered.PrivateFolder=false;input.Text=offered.Encode();Application.DoEvents();
                        check(!folderHint.Visible&&destination.Text.Length==0,"Convite legado mantém o seletor local sem sugestão: "+theme+locale);
                        if(theme=="light"&&locale=="pt"){
                            string configured=state.Destination;
                            state.Destination=localGame;
                            state.TemplateId=GeneralProjectStore.CyberpunkTemplate;
                            offered.SuggestedFolder=otherGame;
                            input.Text=offered.Encode();Application.DoEvents();
                            check(destination.Text==localGame,"Convite antigo usa primeiro a instalação configurada no projeto Cyberpunk.");
                            offered.Game=P2PInvite.CyberpunkGame;
                            offered.SuggestedFolder=manual;
                            input.Text=offered.Encode();Application.DoEvents();
                            check(destination.Text==localGame,"Convite Cyberpunk não aceita uma pasta genérica no lugar da instalação.");
                            var validate=typeof(GeneralP2PDialog).GetMethod("CheckedDestination",BindingFlags.Instance|BindingFlags.NonPublic);
                            bool rejected=false;
                            try{validate.Invoke(dialog,new object[]{manual,offered});}
                            catch(TargetInvocationException ex){rejected=ex.InnerException is IOException;}
                            check(rejected&&(string)validate.Invoke(dialog,new object[]{localGame,offered})==localGame,
                                "A escolha manual rejeita uma pasta sem o executável e aceita a raiz correta.");
                            state.TemplateId=null;
                            state.Destination=configured;
                            offered.SuggestedFolder=null;
                            input.Text=offered.Encode();Application.DoEvents();
                            check(destination.Text==localGame,"Uma única instalação detectada no PC é preenchida sem depender da pasta do anfitrião. "+destination.Text+" / "+folderHint.Text);
                            foundGames.Add(otherGame);
                            offered.PrivateFolder=true;
                            input.Text=offered.Encode();Application.DoEvents();
                            check(destination.Text.Length==0&&folderHint.Text.Contains("executável"),
                                "Com duas instalações detectadas, o app pede para o usuário escolher.");
                            offered.Game="generic";
                            offered.PrivateFolder=false;
                            offered.SuggestedFolder=manual;
                            input.Text=offered.Encode();Application.DoEvents();
                            check(destination.Text==manual,"Pacotes genéricos continuam aceitando a pasta local sugerida.");
                            offered.Game=null;
                            offered.SuggestedFolder=null;
                            input.Text=offered.Encode();Application.DoEvents();
                        }
                        using(var image=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(root,"layout-"+theme+"-"+locale+".png"),System.Drawing.Imaging.ImageFormat.Png);}
                        Safe.WriteJson(Path.Combine(root,"layout.json"),Children(dialog.Content).Where(c=>!String.IsNullOrEmpty(c.Name)||c is TableLayoutPanel).Select(c=>new{Name=c.Name,Type=c.GetType().Name,Bounds=c.Bounds.ToString(),Parent=c.Parent.ClientRectangle.ToString()}).ToArray());
                        foreach(Control control in new Control[]{input,output,destination,connect,generate,Find<Button>(dialog,"p2p-folder"),Find<Button>(dialog,"p2p-copy"),Find<Label>(dialog,"p2p-status")})check(!control.Visible||Inside(control,dialog.Content),"Controle sem recorte: "+control.Name+theme+locale);
                        check(input.Parent is GeneralSurface&&output.Parent is GeneralSurface&&input.Width>300&&output.Width>300&&!input.Multiline&&!output.Multiline&&input.Parent.Height<=56&&output.Parent.Height<=56,"Campos de links usam superfícies e largura legível: "+theme+locale);
                        var copy=Find<Button>(dialog,"p2p-copy");check(copy.Parent==output.Parent&&copy.Left>=output.Right&&copy.Right<=copy.Parent.ClientSize.Width-16&&copy.Top>=6&&copy.Bottom<=copy.Parent.ClientSize.Height-6,"Copy action shares the send field surface without covering text: "+theme+locale);
                        check(!copy.Enabled,"Text without a matching host session cannot be copied by the invite action: "+theme+locale);
                        var coverage=PackCoverage.Build(Enumerable.Range(0,20).Select(i=>new GeneralModEntry{Id=i.ToString(),Category=i<17?"Main":i<19?"Cars":"FPV"}),m=>Int32.Parse(m.Id)<17,"Uncategorized");
                        using(var bar=new GeneralProgress{Size=new Size(640,44),Value=85,Caption="17 / 20",Segments=coverage})using(var image=new Bitmap(640,44)){bar.BackColor=GeneralStyle.Surface;bar.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(root,"coverage-"+theme+"-"+locale+".png"),System.Drawing.Imaging.ImageFormat.Png);}
                        input.Clear();
                        var incoming=new P2PSession{Id=offered.Folder,Name=offered.Name,Hash=offered.ManifestHash,Mode="installed-zip",Size=1024,Peer=offered.Device,Automatic=true,Deferred=true,PayloadHash=offered.ManifestHash};
                        var outgoing=new P2PSession{Id="ml-"+new string('c',32),Name="Project export",Hash=offered.ManifestHash,Sender=true,Lifecycle=true,Mode="installed-zip",Size=1024,
                            Recipients=new[]{new P2PRecipient{Device=offered.Device}}};
                        var views=new Dictionary<P2PSession,P2PTransferStatus>{
                            {incoming,new P2PTransferStatus{Percent=40,RemotePaused=true}},
                            {outgoing,new P2PTransferStatus{Recipients=new[]{
                                new P2PRecipientProgress{Caption="Friend 1",Percent=100,Finished=true},
                                new P2PRecipientProgress{Caption="Friend 2",Percent=55}}}}
                        };
                        service.Sessions.Add(outgoing);service.Sessions.Add(incoming);
                        var refresh=typeof(GeneralP2PDialog).GetMethod("RefreshCards",BindingFlags.Instance|BindingFlags.NonPublic);
                        Action update=()=>{Application.DoEvents();refresh.Invoke(dialog,new object[]{views});dialog.PerformLayout();};
                        update();
                        var cards=Children(dialog.Content).Where(control=>control.Name=="p2p-card").ToArray();
                        check(cards.Length==2&&cards[0] is GeneralSurface&&cards[1] is GeneralSurface,"Envio e recebimento aparecem juntos em molduras separadas: "+theme+locale);
                        var outgoingBars=Children(cards[0]).OfType<GeneralProgress>().Where(bar=>bar.Name=="p2p-progress").ToArray();
                        check(outgoingBars.Length==2&&outgoingBars[0].Value==100&&outgoingBars[1].Value==55,"Cada destinatário tem uma barra própria: "+theme+locale);
                        var pause=Find<Button>(cards[1],"p2p-pause");
                        check(pause.AccessibleName==(locale=="pt"?"Pausado pelo amigo":"Paused by friend")&&!pause.Enabled,"Pausa remota não oferece destravar o amigo: "+theme+locale);
                        incoming.Paused=true;update();
                        check(pause.AccessibleName==(locale=="pt"?"Retomar":"Resume"),"Quem pausou pode retomar sua própria pausa: "+theme+locale);
                        incoming.Paused=false;incoming.Stage="install";incoming.Phase="installing";
                        incoming.InstallProgress=new P2PInstallProgress(true,2,5,"mods/example");
                        update();
                        var prep=Find<GeneralProgress>(cards[1],"p2p-preparation");
                        var active=Find<GeneralProgress>(cards[1],"p2p-progress");
                        check(!prep.Visible&&!pause.Visible&&active.Value==40&&Find<Label>(cards[1],"p2p-transfer-status").Text=="mods/example","Instalação troca as barras pela etapa, progresso e arquivo: "+theme+locale);
                        incoming.Completed=true;incoming.Stage=null;incoming.Phase="installed";
                        update();
                        check(!pause.Visible&&active.Left==prep.Left&&active.Width==prep.Width&&active.Value==100,"Conclusão realinha a barra com o preparo: "+theme+locale);
                        incoming.ListImported=true;incoming.ModsOutsidePack=2;
                        update();
                        var status=Find<Label>(cards[1],"p2p-transfer-status");
                        check(status.Text.Contains(locale=="pt"?"Mods fora do pacote":"Mods outside the pack")&&status.Text.EndsWith("2"),
                            "An incomplete pack identifies the missing count: "+theme+locale);
                        incoming.ModsOutsidePack=0;
                        update();
                        check(status.Text==GeneralP2PDialog.InstalledMessage,"Verified install and saved project show readiness: "+theme+locale);
                        incoming.ListImported=false;incoming.ModsOutsidePack=null;
                        update();
                        var viewport=Find<GeneralScrollView>(dialog,"p2p-cards");
                        Rectangle field=Position(output.Parent,dialog.Content);
                        Rectangle received=Position(input.Parent,dialog.Content);
                        Rectangle actions=Position(connect.Parent,dialog.Content);
                        Rectangle frame=Position(cards[0],dialog.Content);
                        check(field.Left==received.Left&&field.Right==received.Right&&frame.Left==field.Left&&frame.Right==field.Right&&connect.Parent==generate.Parent&&Position(connect,dialog.Content).Right==field.Right,
                            "Fields, rightmost action and transfer cards share the visible edges: "+theme+locale);
                        check(actions.Top-received.Bottom==frame.Top-actions.Bottom&&Position(connect,dialog.Content).Top==Position(generate,dialog.Content).Top,
                            "Action row is evenly spaced and buttons share the same baseline: "+theme+locale);
                        if(!viewport.Bar.Visible)check(dialog.Content.ClientSize.Height-Position(cards[1],dialog.Content).Bottom==field.Left,
                            "Bottom inset matches the side inset with fully visible cards: "+theme+locale);
                        if(theme=="black"&&locale=="en"){
                            foreach(int i in Enumerable.Range(1,10))service.Sessions.Add(new P2PSession{Id="ml-"+i.ToString("x32"),Name="Transfer "+i,Mode="installed-zip"});
                            update();
                            var frameWithScroll=Position(cards[0],dialog.Content);
                            check(viewport.Bar.Visible&&frameWithScroll.Left==field.Left&&frameWithScroll.Right==field.Right,
                                "Overflow places the scrollbar in the gutter without moving the card edges. "+frameWithScroll+" vs "+field+" rail="+viewport.Bar.Visible+" limit="+viewport.Bar.Limit+" count="+service.Sessions.Count+" height="+viewport.Height);
                            viewport.Bar.MoveTo(viewport.Bar.Limit);Application.DoEvents();
                            var lastCard=Children(viewport.Content).Last(c=>c.Name=="p2p-card");
                            check(dialog.Content.ClientSize.Height-Position(lastCard,dialog.Content).Bottom==field.Left,
                                "The last transfer keeps the bottom inset at the scroll limit.");
                            service.Sessions.RemoveRange(2,10);update();
                            var dismiss=Find<Button>(cards[0],"p2p-dismiss");
                            check(dismiss.Visible&&dismiss.Enabled&&Inside(dismiss,cards[0]),"An active send can be canceled from an unclipped X.");
                            outgoing.Completed=true;
                            update();
                            check(dismiss.Visible&&Inside(dismiss,cards[0]),"The completed send shows an unclipped X.");
                            using(var image=new Bitmap(dialog.Width,dialog.Height)){
                                dialog.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));
                                image.Save(Path.Combine(root,"completed-send.png"),System.Drawing.Imaging.ImageFormat.Png);
                            }
                            dismiss.PerformClick();
                            DateTime deadline=DateTime.UtcNow.AddSeconds(4);
                            while(!cards[0].IsDisposed&&DateTime.UtcNow<deadline){
                                Application.DoEvents();
                                System.Threading.Thread.Sleep(10);
                            }
                            check(cards[0].IsDisposed&&!cards[1].IsDisposed,"The X removes only the completed send card.");
                            using(var reopened=new P2PService(service.Root,service.Store,true))
                                check(reopened.SnapshotSessions().Single(session=>session.Id==outgoing.Id).HistoryDismissed,
                                    "The completed send stays removed after reopening.");
                            var activeSend=new P2PSession{Id="ml-"+new string('d',32),Name="Active export",Hash=offered.ManifestHash,Sender=true,Lifecycle=true,Mode="installed-zip",Size=1024,
                                Stage="Preparing pack",Recipients=new[]{new P2PRecipient{Device=offered.Device}}};
                            service.Sessions.Add(activeSend);
                            update();
                            var activeCard=Children(viewport.Content).Single(c=>c.Name=="p2p-card"&&c.AccessibleName==activeSend.ToString());
                            activeSend.CompareBeforePack=true;activeSend.Deferred=true;activeSend.Automatic=true;activeSend.Size=0;
                            activeSend.Recipients[0].SyncChoicePhase="comparing";update();
                            check(Find<Label>(activeCard,"p2p-transfer-status").Text.Contains(locale=="pt"?"já escolheu":"friend chose"),
                                "The sender card acknowledges a quick choice while the receiver compares files.");
                            activeSend.Recipients[0].SyncChoicePhase="choice_sent";update();
                            check(Find<Label>(activeCard,"p2p-transfer-status").Text.Contains(locale=="pt"?"confirmou":"confirmed"),
                                "The sender card distinguishes submitted choice from waiting for input.");
                            activeSend.CompareBeforePack=false;activeSend.Deferred=false;activeSend.Automatic=false;activeSend.Size=1024;
                            Find<Button>(activeCard,"p2p-dismiss").PerformClick();
                            deadline=DateTime.UtcNow.AddSeconds(4);
                            while(!activeCard.IsDisposed&&DateTime.UtcNow<deadline){
                                Application.DoEvents();
                                System.Threading.Thread.Sleep(10);
                            }
                            check(activeCard.IsDisposed&&!cards[1].IsDisposed&&!incoming.Discarded,"Canceling preparation removes only the selected send.");
                            using(var reopened=new P2PService(service.Root,service.Store,true))
                                check(reopened.SnapshotSessions().Single(session=>session.Id==activeSend.Id).Discarded,
                                    "An interrupted canceled send cannot resume on reopen.");
                        }
                        incoming.Completed=false;incoming.Phase=null;incoming.PayloadHash=null;incoming.Size=0;incoming.Lifecycle=true;incoming.CompareBeforePack=true;
                        views[incoming].Connected=false;views[incoming].Accepted=false;views[incoming].RemotePaused=false;
                        update();
                        check(!Find<Button>(cards[1],"p2p-update").Enabled&&Find<Label>(cards[1],"p2p-transfer-status").Text.Contains(locale=="pt"?"Conectando":"Connecting"),"Pending connection has an explicit status.");
                        var cardMap=(System.Collections.IDictionary)typeof(GeneralP2PDialog).GetField("cards",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(dialog);
                        var receiveCard=cardMap[incoming];
                        receiveCard.GetType().GetField("waitingSince",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(receiveCard,DateTime.UtcNow.AddSeconds(-31));
                        var receiveStatus=Find<Label>(cards[1],"p2p-transfer-status");
                        update();
                        check(receiveStatus.Text.Contains(locale=="pt"?"Ainda tentando":"Still trying"),"A demora mantém a tentativa aberta: "+theme+locale);
                        views[incoming].HostDecision=P2PService.HostWaiting;update();
                        check(receiveStatus.Text.Contains(locale=="pt"?"decisão do anfitrião":"host to decide"),"O receptor vê que o pedido foi recebido: "+theme+locale);
                        views[incoming].HostDecision=P2PService.HostDeclined;update();
                        check(receiveStatus.Text.Contains(locale=="pt"?"recusou":"declined")&&!Find<Button>(cards[1],"p2p-full").Enabled,
                            "Recusa fica explícita sem liberar a transferência: "+theme+locale);
                        views[incoming].HostDecision=P2PService.HostApproved;update();
                        check(receiveStatus.Text.Contains(locale=="pt"?"Finalizando":"Finishing")&&!Find<Button>(cards[1],"p2p-full").Enabled,
                            "Aceite confirmado fica visível enquanto a conexão termina: "+theme+locale);
                        views[incoming].Connected=true;views[incoming].Accepted=true;
                        incoming.CreatedUtc=DateTime.UtcNow.AddSeconds(-5).ToString("o");
                        incoming.ConnectedUtc=DateTime.UtcNow.ToString("o");
                        update();
                        var connection=Find<Control>(cards[1],"p2p-connection");
                        var dates=Find<Label>(cards[1],"p2p-dates");
                        check(connection.AccessibleName.StartsWith(locale=="pt"?"Conectado":"Connected")&&
                            dates.Visible&&dates.Text.StartsWith(locale=="pt"?"Primeira conexão":"First connected"),
                            "Live connection and time are identified: "+theme+locale);
                        var tips=(ToolTip)typeof(GeneralP2PDialog).GetField("tips",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(dialog);
                        check(tips.GetToolTip(connection).Contains(dates.Text),"Connection hover includes the observed date: "+theme+locale);
                        views[incoming].Connected=false;incoming.DisconnectedUtc=DateTime.UtcNow.ToString("o");
                        update();
                        check(connection.AccessibleName.StartsWith(locale=="pt"?"Desconectado":"Disconnected")&&
                            tips.GetToolTip(connection).Contains(locale=="pt"?"Última desconexão":"Last disconnected"),
                            "Disconnected peer and disconnection time are identified: "+theme+locale);
                        views[incoming].Connected=true;
                        update();
                        var updateMods=Find<Button>(cards[1],"p2p-update");
                        var fullPack=Find<Button>(cards[1],"p2p-full");
                        check(updateMods.Visible&&updateMods.Enabled&&fullPack.Visible&&fullPack.Enabled,
                            "After connecting, both choices are immediately visible before the host preview arrives: "+theme+locale);
                        incoming.SyncMode=1;incoming.SyncProgress=new P2PSyncProgress{Done=2,Total=5,Item="mods/different.txt"};
                        update();
                        check(!updateMods.Enabled&&!fullPack.Enabled&&prep.Visible&&prep.Value==40&&
                            Find<Label>(cards[1],"p2p-transfer-status").Text=="mods/different.txt","The receiver shows the current local file and comparison progress: "+theme+locale);
                        service.Sessions.Clear();
                        using(var image=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(root,"p2p-"+theme+"-"+locale+".png"),System.Drawing.Imaging.ImageFormat.Png);}dialog.Close();
                    }
                }
                check(!service.Engine.Running&&!Directory.Exists(service.Store),"Renderização não inicia rede nem cria pacotes.");check(StateContents(state)==before&&!Directory.Exists(state.Destination),"Formulário preserva estado e não toca no jogo.");
            }
            Safe.WriteJson(Path.Combine(root,"resultado-p2p-ui.json"),new{Passed=true,Tests=checks,NetworkUsed=false,RealGameTouched=false});return checks.Count;
        }finally{GeneralStyle.Dark=dark;GeneralStyle.RedTheme=red;GeneralStyle.BlackTheme=black;GeneralStyle.Language=language;GeneralStyle.AnimationsEnabled=animation;}
    }
    static string StateContents(GeneralModState state){
        return ModAuditTests.Canonical(Safe.Json.DeserializeObject(Safe.Json.Serialize(state)));
    }
    internal static void Preview(string parent){Run(parent);}
    static T Find<T>(Control root,string name)where T:Control{return Children(root).OfType<T>().Single(c=>c.Name==name);}
    static IEnumerable<Control> Children(Control root){foreach(Control child in root.Controls){yield return child;foreach(var nested in Children(child))yield return nested;}}
    static Rectangle Position(Control item,Control root){return new Rectangle(root.PointToClient(item.PointToScreen(Point.Empty)),item.Size);}
    static bool Inside(Control item,Control root){for(Control current=item;current!=root;current=current.Parent){if(current.Parent==null||!current.Parent.ClientRectangle.Contains(new Rectangle(current.Location,current.Size)))return false;}return true;}
}
