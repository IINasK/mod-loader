using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using System.Windows.Forms;

internal static class InstalledSnapshotP2PTests {
    static readonly Action<string> Ignore=message=>{};
    internal static int RunNetwork(string parent,string officialExe) {
        string root=Path.Combine(Path.GetFullPath(parent),"sp-"+Guid.NewGuid().ToString("N").Substring(0,6));Directory.CreateDirectory(root);
        var checks=new List<string>();P2PService sender=null,receiver=null;
        try {
            if(!File.Exists(officialExe)||Safe.Hash(officialExe)!=SyncthingEngine.ExeHash)throw new IOException("Informe o auxiliar oficial fixado para este teste.");
            var catalog=EmptyCatalog();string sourceGame=Path.Combine(root,"sg"),sourceData=Path.Combine(root,"sd"),receiverData=Path.Combine(root,"rd");Directory.CreateDirectory(sourceGame);Directory.CreateDirectory(sourceData);Directory.CreateDirectory(receiverData);
            string original=Path.Combine(root,"original.zip");
            using(var zip=ZipFile.Open(original,ZipArchiveMode.Create)) {
                using(var writer=new StreamWriter(zip.CreateEntry("mods/fixture.txt").Open()))writer.Write("installed fixture bytes");
                using(var writer=new StreamWriter(zip.CreateEntry("mods/copy.txt").Open()))writer.Write("installed fixture bytes");
            }
            var sourceState=State(sourceGame,original);string work="fixture/default";
            new GeneralModInstaller(sourceGame,Path.Combine(sourceData,"Instalacoes")).Install(original,work+"/0/"+original,Ignore);
            File.Delete(original);Check(checks,!File.Exists(original),"ZIP original apagado depois da instalação real em fixture.");
            sender=new P2PService(Path.Combine(root,"sh"),Path.Combine(root,"st"),true);
            receiver=new P2PService(Path.Combine(root,"rh"),Path.Combine(root,"rt"),true);
            sender.Start(Ignore,officialExe);receiver.Start(Ignore,officialExe);
            Check(checks,sender.Engine.Running&&receiver.Engine.Running&&sender.Engine.Device!=receiver.Engine.Device,"Dois auxiliares oficiais com perfis e identidades independentes.");
            Loopback(checks,sender);Loopback(checks,receiver);
            string target=Target(root,"tg");receiver.EnableInstalledReceive(catalog,receiverData,true);
            var unsafeState=Safe.Json.Deserialize<GeneralModState>(Safe.Json.Serialize(sourceState));unsafeState.Destination=root;
            bool outsideGuard=false;try{sender.ShareInstalled(unsafeState,new[]{"fixture"},catalog,sourceData,true,Ignore);}catch(IOException){outsideGuard=true;}
            Check(checks,outsideGuard&&sender.Sessions.Count==0,"Impede criar transporte temporário dentro da pasta de instalação selecionada.");
            var sent=sender.ShareInstalled(sourceState,new[]{"fixture"},catalog,sourceData,true,Ignore);
            Check(checks,sent.Mode=="installed-zip"&&sent.Size>0&&sent.Hash==Safe.Hash(Safe.In(sender.Folder(sent),"pack.zip")),"Oferta aponta para um único ZIP de bytes instalados.");
            Check(checks,TransportFiles(sender.Folder(sent)).SequenceEqual(new[]{"pack.zip"}),"Transporte remetente contém só pack.zip, sem manifesto externo ou blobs de cache.");
            var accepted=receiver.Accept(sender.Invitation(sent),target,true);
            Check(checks,accepted.AutoInstall&&accepted.Destination==target&&accepted.Mode=="installed-zip","Aceitação vincula destino de fixture e instalação automática.");
            Check(checks,File.ReadAllText(Safe.In(receiver.Folder(accepted),".stignore"))=="!pack.zip\n*\n","Receptor libera somente pack.zip do convite.");
            sender.Pause(sent);sender.Authorize(receiver.Invitation(accepted));
            Check(checks,sent.Paused&&Convert.ToBoolean(sender.Engine.Get("config/folders/"+sent.Id)["paused"]),"Autorizar preserva pausa anterior da sessão.");
            sender.Engine.ConfigurePeer(receiver.Engine.Device,"tcp://127.0.0.1:"+receiver.Engine.ListenPort);
            receiver.Engine.ConfigurePeer(sender.Engine.Device,"tcp://127.0.0.1:"+sender.Engine.ListenPort);
            sender.Pause(sent);Check(checks,!sent.Paused,"Retomar libera a transferência autorizada.");
            sender.Engine.Request("POST","db/scan?folder="+sent.Id,null);
            Wait(()=>accepted.Completed||!String.IsNullOrEmpty(accepted.Error),"Instalação automática não terminou em 40 segundos.");
            Check(checks,accepted.Completed&&accepted.ReceiptId!=null&&accepted.Error==null,"Serviço recebe, verifica e instala sem janela ou chamada manual de carregar.");
            Check(checks,File.ReadAllText(Safe.In(target,"mods/fixture.txt"))=="installed fixture bytes"&&Safe.Hash(Safe.In(target,"mods/fixture.txt"))==Safe.Hash(Safe.In(target,"mods/copy.txt")),"Arquivos deduplicados chegam a ambos os destinos corretos.");
            Wait(()=>sent.Completed&&!Directory.Exists(sender.Folder(sent))&&!Directory.Exists(receiver.Folder(accepted)),"Temporários não foram removidos de ambos os lados em 40 segundos.");
            Check(checks,sent.Completed&&!Directory.Exists(sender.Folder(sent))&&!Directory.Exists(receiver.Folder(accepted)),"Ambos removem ZIP temporário ao concluir.");
            Check(checks,FolderCount(sender)==0&&FolderCount(receiver)==0,"Pastas são retiradas do auxiliar antes de excluir seus arquivos.");
            Check(checks,!Directory.Exists(receiver.PrivateFolder(accepted))&&!Directory.Exists(Path.Combine(receiverData,"Downloads"))&&!Directory.Exists(Path.Combine(receiverData,"SnapshotReadCache")),"Receptor não cria cópia privada recebida ou cache de arquivos compactados.");
            Check(checks,!Archives(receiverData).Any()&&!Archives(sourceData).Any(),"Dados persistentes mantêm recibos sem ZIP ou arquivo de mods.");
            var installer=new InstalledSnapshotInstaller(target,receiverData,catalog,true);
            Check(checks,installer.Status(accepted.ReceiptId,work)==GeneralModInstaller.Installed&&receiver.InstalledManifest(accepted).Files.Count==2,"Recibo mantém manifesto e situação depois que o transporte foi apagado.");
            var again=new GeneralModState{Destination=target,List=receiver.InstalledManifest(accepted).List};again.SnapshotReceipts[work]=accepted.ReceiptId;
            string reexport=Path.Combine(root,"again.zip");var againManifest=InstalledSnapshot.Create(reexport,again,new[]{"fixture"},catalog,receiverData,false,Ignore);
            Check(checks,againManifest.Files.Count==2&&InstalledSnapshot.Read(reexport,Safe.Hash(reexport),catalog).Files.All(f=>f.Sha256==Safe.Hash(Safe.In(target,f.Path))),"Recibo permite novo snapshot sem nenhum ZIP original ou transferido.");File.Delete(reexport);
            ConfigTransfer(root,checks,sender,receiver);
            var badBytes=Encoding.UTF8.GetBytes("corrupt fixture ZIP");string negativeTarget=Target(root,"bad"),negativeData=Path.Combine(root,"bd");Directory.CreateDirectory(negativeData);receiver.EnableInstalledReceive(catalog,negativeData,true);
            var offer=new P2PInvite{Kind="offer",Device=sender.Engine.Device,Folder="ml-"+Guid.NewGuid().ToString("N"),ManifestHash=new string('b',64),Name="Corrupt fixture",Mode="installed-zip",Size=badBytes.Length};
            var bad=receiver.Accept(offer.Encode(),negativeTarget,true);string badZip=Safe.In(receiver.Folder(bad),"pack.zip");File.WriteAllBytes(badZip,badBytes);
            Wait(()=>!String.IsNullOrEmpty(bad.Error),"ZIP corrompido não produziu erro em 40 segundos.");
            Check(checks,!bad.Completed&&bad.ReceiptId==null&&File.Exists(badZip),"Hash recebido incorreto impede instalação e preserva ZIP para revisão ou tentativa posterior.");
            Check(checks,Directory.GetFiles(negativeTarget,"*",SearchOption.AllDirectories).All(p=>Path.GetFileName(p)==".fixture-only")&&!Directory.Exists(Path.Combine(negativeData,"Snapshots")),"Pacote corrompido não modifica destino nem cria recibo de instalação.");
            receiver.Remove(bad);receiver.Remove(accepted);sender.Remove(sent);
            Check(checks,FolderCount(sender)==0&&FolderCount(receiver)==0&&File.Exists(Safe.In(target,"mods/fixture.txt")),"Encerrar sessões preserva instalações e não deixa compartilhamento ativo.");
            sender.Dispose();receiver.Dispose();Check(checks,!sender.Engine.Running&&!receiver.Engine.Running,"Finalização encerra apenas os dois auxiliares deste ensaio.");
            ResumeTransfer(root,checks,officialExe);
            DialogTransfer(root,checks,officialExe);
            Safe.WriteJson(Path.Combine(root,"resultado-snapshot-p2p.json"),new{Passed=true,Checks=checks.Count,Tests=checks,RealGameTouched=false,NetworkUsed=true,LoopbackOnly=true,AutoInstalled=true});return checks.Count;
        }catch(Exception ex){Safe.WriteJson(Path.Combine(root,"resultado-snapshot-p2p.json"),new{Passed=false,Checks=checks.Count,Tests=checks,Error=ex.ToString(),RealGameTouched=false,NetworkUsed=true,LoopbackOnly=true});throw;}
        finally{if(sender!=null)sender.Dispose();if(receiver!=null)receiver.Dispose();}
    }
    static void ConfigTransfer(string root,List<string> checks,P2PService sender,P2PService receiver) {
        string source=Path.Combine(root,"cs"),data=Path.Combine(root,"cd"),receivedData=Path.Combine(root,"crd"),target=Target(root,"cfg");Directory.CreateDirectory(source);Directory.CreateDirectory(data);Directory.CreateDirectory(receivedData);
        string code="fixture code";var mod=new FixedMod{Id="Audioware",Name="Fixture code with config",Version="1.0",GameVersion="2.5",Dependencies=new string[0],Assets=new List<FixedAsset>{new FixedAsset{Id="asset",Name="fixture.zip",Format="zip",Url="https://github.com/fixture/fixture/releases/download/v1/fixture.zip",Sha256=new string('a',64),Size=1}},Files=new List<FixedFile>{new FixedFile{Asset="asset",Entry="code.dll",Path="red4ext/plugins/Fixture/code.dll",Size=Encoding.UTF8.GetByteCount(code),Sha256=InstalledSnapshot.Hash(Encoding.UTF8.GetBytes(code))}}};
        var catalog=EmptyCatalog();catalog.Id="config-fixture";catalog.Packages.Add(mod);catalog.Defaults.Add(new FixedDefault{Id="fixture-config",ModId=mod.Id,Version=mod.Version,Path="red4ext/plugins/mod_settings/user.ini",Format="ini-section",Section="Audioware.AudiowareConfig",Content="{\"enabled\":\"true\"}",Sha256=InstalledSnapshot.Hash(Encoding.UTF8.GetBytes("{\"enabled\":\"true\"}"))});
        Write(source,"bin/x64/Cyberpunk2077.exe","fixture only");Write(source,mod.Files[0].Path,code);Write(source,"red4ext/plugins/mod_settings/user.ini","[Audioware.AudiowareConfig]\nenabled = false\n");
        Write(target,"bin/x64/Cyberpunk2077.exe","fixture only");Write(target,"red4ext/plugins/mod_settings/user.ini","[Unselected]\nlocalValue = keep\n");
        var state=new GeneralModState{Destination=source,List=GeneralModLibrary.Seed(catalog,new ModImportItem[0])};receiver.EnableInstalledReceive(catalog,receivedData,true);
        var sent=sender.ShareInstalled(state,new[]{mod.Id},catalog,data,true,Ignore);var accepted=receiver.Accept(sender.Invitation(sent),target,true);sender.Authorize(receiver.Invitation(accepted));
        sender.Engine.ConfigurePeer(receiver.Engine.Device,"tcp://127.0.0.1:"+receiver.Engine.ListenPort);receiver.Engine.ConfigurePeer(sender.Engine.Device,"tcp://127.0.0.1:"+sender.Engine.ListenPort);sender.Engine.Request("POST","db/scan?folder="+sent.Id,null);
        Wait(()=>accepted.Completed||!String.IsNullOrEmpty(accepted.Error),"Pacote de configuração não foi instalado em 40 segundos.");
        Check(checks,accepted.Completed&&accepted.Error==null&&accepted.ReceiptId!=null,"Segundo pacote instala código fixado e configuração permitida pelo mesmo serviço.");
        string ini=Safe.In(target,"red4ext/plugins/mod_settings/user.ini"),text=File.ReadAllText(ini);
        Check(checks,text.Contains("enabled = false")&&text.Contains("localValue = keep"),"Instalação automática aplica seção selecionada e preserva outras configurações locais.");
        Wait(()=>sent.Completed&&!Directory.Exists(sender.Folder(sent))&&!Directory.Exists(receiver.Folder(accepted)),"Temporários da configuração não foram removidos em 40 segundos.");
        File.WriteAllText(ini,text.Replace("enabled = false","enabled = true"),new UTF8Encoding(false));
        var received=new GeneralModState{Destination=target,List=receiver.InstalledManifest(accepted).List};received.SnapshotReceipts[mod.Id+"/default"]=accepted.ReceiptId;
        string export=Path.Combine(root,"updated.zip");var manifest=InstalledSnapshot.Create(export,received,new[]{mod.Id},catalog,receivedData,true,Ignore);
        Check(checks,manifest.Files.Single(f=>f.Kind=="ini-section").Content.Contains("true"),"Novo snapshot lê configuração instalada atualizada após excluir os ZIPs de transporte.");
        Check(checks,!Safe.Json.Serialize(manifest).Contains("localValue"),"Reexportação continua limitada às seções dos mods selecionados.");File.Delete(export);
        Check(checks,!Archives(receivedData).Any()&&!Directory.Exists(receiver.PrivateFolder(accepted)),"Mesmo com configurações nenhum pacote persistente recebido é mantido.");
        receiver.Remove(accepted);sender.Remove(sent);
    }
    static void ResumeTransfer(string root,List<string> checks,string officialExe){
        string source=Path.Combine(root,"resume-source"),data=Path.Combine(root,"resume-data"),receivedData=Path.Combine(root,"resume-received"),target=Target(root,"resume-target");Directory.CreateDirectory(source);Directory.CreateDirectory(data);Directory.CreateDirectory(receivedData);
        byte[] bytes=new byte[8*1024*1024];new Random(12345).NextBytes(bytes);string original=Path.Combine(root,"resume-original.zip");
        using(var zip=ZipFile.Open(original,ZipArchiveMode.Create))using(var output=zip.CreateEntry("mods/fixture.bin",CompressionLevel.NoCompression).Open())output.Write(bytes,0,bytes.Length);
        var state=State(source,original);var catalog=EmptyCatalog();new GeneralModInstaller(source,Path.Combine(data,"Instalacoes")).Install(original,"fixture/default/0/"+original,Ignore);File.Delete(original);
        string sh=Path.Combine(root,"resume-sh"),st=Path.Combine(root,"resume-st"),rh=Path.Combine(root,"resume-rh"),rt=Path.Combine(root,"resume-rt");P2PService sender=null,receiver=null;
        try{
            sender=new P2PService(sh,st,true);receiver=new P2PService(rh,rt,true);sender.Start(Ignore,officialExe);receiver.EnableInstalledReceive(catalog,receivedData,true);receiver.Start(Ignore,officialExe);
            sender.Engine.Request("PATCH","config/options",new{maxSendKbps=1024,limitBandwidthInLan=true});var sent=sender.ShareInstalled(state,new[]{"fixture"},catalog,data,true,Ignore);var received=receiver.Accept(sender.Invitation(sent),target,true);sender.Authorize(receiver.Invitation(received));
            Link(sender,receiver);sender.Engine.Request("POST","db/scan?folder="+sent.Id,null);
            int partial=0;Wait(()=>{partial=receiver.TransferStatus(received).Percent;return partial>0&&partial<99;},"Progresso parcial por blocos não apareceu.");Check(checks,partial>0&&partial<99&&!received.Completed,"Barra mede blocos baixados de um ZIP ainda incompleto, não tamanho pré-alocado.");
            receiver.Pause(received);Wait(()=>sender.TransferStatus(sent).RemotePaused,"Remetente não percebeu a pausa remota.");Check(checks,!sent.Paused&&received.Paused,"Pausa do receptor aparece no remetente sem alterar a pausa local dele.");
            sender.Pause(sent);receiver.Pause(received);Wait(()=>receiver.TransferStatus(received).RemotePaused,"Receptor não percebeu a pausa do remetente.");Check(checks,!received.Paused&&sent.Paused,"Retomar a própria pausa não libera uma pausa ainda mantida pelo amigo.");receiver.Pause(received);
            string senderDevice=sender.Engine.Device,receiverDevice=receiver.Engine.Device,folder=sent.Id;
            foreach(var service in new[]{sender,receiver}){
                var process=(Process)typeof(SyncthingEngine).GetField("process",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(service.Engine);
                // Syncthing's monitor has a child worker; a power-loss test must stop both.
                using(var stop=Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"taskkill.exe"),"/PID "+process.Id+" /T /F"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})){stop.WaitForExit();if(stop.ExitCode!=0)throw new IOException("Falha ao interromper apenas o auxiliar isolado de teste.");}
                process.WaitForExit();service.Dispose();
            }
            sender=new P2PService(sh,st,true);receiver=new P2PService(rh,rt,true);sent=sender.Sessions.Single(s=>s.Id==folder);received=receiver.Sessions.Single(s=>s.Id==folder);
            sender.Start(Ignore,officialExe);receiver.EnableInstalledReceive(catalog,receivedData,true);receiver.Start(Ignore,officialExe);Link(sender,receiver);
            Check(checks,sent.Paused&&received.Paused&&sender.Engine.Device==senderDevice&&receiver.Engine.Device==receiverDevice&&received.Destination==target&&!received.Completed,"Após encerrar abruptamente os auxiliares, sessões, identidades, destino e pausas são recuperados.");
            Check(checks,File.Exists(Safe.In(sender.Folder(sent),"pack.zip"))&&Directory.GetFiles(receiver.Folder(received),"*",SearchOption.AllDirectories).Any(p=>p.EndsWith(".tmp",StringComparison.OrdinalIgnoreCase)),"ZIP fonte e blocos temporários permanecem disponíveis após reiniciar.");
            receiver.Pause(received);sender.Engine.Request("PATCH","config/options",new{maxSendKbps=0});sender.Pause(sent);
            Wait(()=>received.Completed||received.Error!=null,"A retomada não concluiu a instalação.");Check(checks,received.Completed&&received.Error==null&&Safe.Hash(Safe.In(target,"mods/fixture.bin"))==InstalledSnapshot.Hash(bytes),"Transferência reiniciada conclui por hash e instala automaticamente no destino autorizado.");
            Wait(()=>sent.Completed&&!Directory.Exists(sender.Folder(sent))&&!Directory.Exists(receiver.Folder(received)),"Temporários da retomada não foram removidos.");Check(checks,!Directory.Exists(sender.Folder(sent))&&!Directory.Exists(receiver.Folder(received)),"Conclusão elimina ZIP e blocos temporários nos dois lados.");
        }finally{if(sender!=null)sender.Dispose();if(receiver!=null)receiver.Dispose();}
    }
    static void Link(P2PService sender,P2PService receiver){sender.Engine.ConfigurePeer(receiver.Engine.Device,"tcp://127.0.0.1:"+receiver.Engine.ListenPort);receiver.Engine.ConfigurePeer(sender.Engine.Device,"tcp://127.0.0.1:"+sender.Engine.ListenPort);}
    internal static void RunDialogNetwork(string parent,string officialExe){
        if(Safe.Hash(officialExe)!=SyncthingEngine.ExeHash)throw new IOException("Auxiliar de teste não reconhecido.");
        string root=Path.Combine(parent,"pd-"+Guid.NewGuid().ToString("N").Substring(0,6));Directory.CreateDirectory(root);var checks=new List<string>();
        try{DialogTransfer(root,checks,officialExe);Safe.WriteJson(Path.Combine(root,"resultado-p2p-dialog-network.json"),new{Passed=true,Tests=checks,LoopbackOnly=true,RealGameTouched=false});}
        catch(Exception ex){Safe.WriteJson(Path.Combine(root,"resultado-p2p-dialog-network.json"),new{Passed=false,Tests=checks,Error=ex.ToString()});throw;}
    }
    static void DialogTransfer(string root,List<string> checks,string officialExe){
        string game=Path.Combine(root,"ui-source"),data=Path.Combine(root,"ui-data"),receivedData=Path.Combine(root,"ui-rdata"),target=Target(root,"ui-target"),original=Path.Combine(root,"ui-original.zip");Directory.CreateDirectory(game);Directory.CreateDirectory(data);Directory.CreateDirectory(receivedData);
        using(var zip=ZipFile.Open(original,ZipArchiveMode.Create))using(var writer=new StreamWriter(zip.CreateEntry("mods/ui.txt").Open()))writer.Write("dialog transfer");
        var state=State(game,original);state.List.Mods[0].Name="Meu mod da UI";state.List.Presets.Add(new GeneralModPreset{Id="amigos",Name="Meus amigos",ModIds=new List<string>{"fixture"}});state.SelectedPreset="amigos";
        var optional=new GeneralModEntry{Id="optional",Name="Optional car",Category="Cars",DefaultVariant="main",Variants=new List<GeneralModVariant>{new GeneralModVariant{Id="main",Name="Main",Sources=new List<string>{"https://example.org/car.zip"}}}};state.List.Categories.Add("Cars");state.List.Mods.Add(optional);state.List.Presets[0].ModIds.Add("optional");
        new GeneralModInstaller(game,Path.Combine(data,"Instalacoes")).Install(original,"fixture/default/0/"+original,Ignore);File.Delete(original);var catalog=EmptyCatalog();bool animation=GeneralStyle.AnimationsEnabled;GeneralStyle.AnimationsEnabled=false;
        Exception failure=null;Action<string> phase=value=>File.WriteAllText(Path.Combine(root,"ui-phase.txt"),value);
        try{using(var sender=new P2PService(Path.Combine(root,"ui-sh"),Path.Combine(root,"ui-st"),true))using(var receiver=new P2PService(Path.Combine(root,"ui-rh"),Path.Combine(root,"ui-rt"),true)){
            sender.EnableInstalledReceive(catalog,data,true);receiver.EnableInstalledReceive(catalog,receivedData,true);sender.Start(Ignore,officialExe);receiver.Start(Ignore,officialExe);
            var receiverState=new GeneralModState{Destination=target};var cache=new DownloadCache(Path.Combine(root,"ui-cache"));
            using(var host=new Form{ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new System.Drawing.Point(-30000,-30000)})
            using(var sending=new GeneralP2PDialog(sender,state,new[]{"fixture","optional"},catalog,cache))using(var receiving=new GeneralP2PDialog(receiver,receiverState,new string[0],catalog,cache,false)){
                System.Threading.ThreadExceptionEventHandler onError=(s,e)=>{failure=e.Exception;phase(e.Exception.ToString());receiving.Dispose();sending.Dispose();host.Close();};Application.ThreadException+=onError;
                host.Shown+=async(s,e)=>{
                    try{
                        foreach(var form in new[]{sending,receiving}){form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new System.Drawing.Point(-30000,-30000);form.Show(host);}
                        phase("automatic preparation");await UIWait(()=>sending.Content.Controls.Find("p2p-output",true).Single().Text.StartsWith("ML-P2P1:",StringComparison.Ordinal)&&((Button)sending.Content.Controls.Find("p2p-copy",true).Single()).Enabled,"Conectar não gerou convite na UI.");
                        string offer=sending.Content.Controls.Find("p2p-output",true).Single().Text;Check(checks,P2PInvite.Decode(offer).Kind=="offer","Opening shows the invitation without preparing a ZIP or requiring a sender click.");
                        phase("receiver connect");receiving.Content.Controls.Find("p2p-input",true).Single().Text=offer;
                        using(var confirm=new System.Windows.Forms.Timer{Interval=25}){
                            confirm.Tick+=(a,b)=>{var question=Application.OpenForms.OfType<GeneralDialog>().FirstOrDefault(f=>f!=sending&&f!=receiving&&f.AcceptButton!=null);if(question!=null){((Button)question.AcceptButton).PerformClick();confirm.Stop();}};confirm.Start();
                            ((Button)receiving.Content.Controls.Find("p2p-connect",true).Single()).PerformClick();await UIWait(()=>receiving.Content.Controls.Find("p2p-output",true).Single().Text.Length>0,"UI de recebimento não gerou resposta.");
                        }
                        string reply=receiving.Content.Controls.Find("p2p-output",true).Single().Text;Check(checks,P2PInvite.Decode(reply).Kind=="accept"&&receiver.Sessions.Single().Destination==target,"Conectar com convite vincula o destino local e aceita automaticamente.");
                        phase("automatic sender authorization");receiver.Engine.ConfigurePeer(sender.Engine.Device,"tcp://127.0.0.1:"+sender.Engine.ListenPort);await UIWait(()=>sender.Sessions.Single().Peer!=null,"O anfitrião não detectou a aceitação automaticamente.");
                        Check(checks,sending.Content.Controls.Find("p2p-input",true).Single().Text.Length==0&&!receiving.Content.Controls.Find("p2p-output",true).Single().Visible,"Neither participant needs to copy or paste a reply code; receiver hides the unused reply field.");
                        phase("waiting install");await UIWait(()=>receiving.ReceivedList!=null,"UI não concluiu e devolveu a lista instalada.");Check(checks,receiving.ReceivedList.Mods.Single(m=>m.Id=="fixture").Name=="Meu mod da UI"&&receiving.ReceivedList.Presets.Single().Name=="Meus amigos"&&receiving.InstalledReceiptId!=null,"One-sided dialog connection installs and returns names, presets and receipt.");
                        Check(checks,receiving.ReceivedList.Mods.Count==2&&receiving.ReceivedList.Mods.Single(m=>m.Id=="optional").Variants[0].Sources.Single()=="https://example.org/car.zip"&&receiver.InstalledManifest(receiver.Sessions.Single()).List.Mods.Count==1,"Real dialog transfer imports missing optional entries and public URLs without installed receipts or warning popups.");
                        Check(checks,File.ReadAllText(Safe.In(target,"mods/ui.txt"))=="dialog transfer"&&!Directory.Exists(receiver.Folder(receiver.Sessions.Single())),"Conclusão pela UI contém o arquivo instalado e remove o temporário recebido.");phase("complete");
                    }catch(Exception ex){failure=ex;phase(ex.ToString());}finally{receiving.Dispose();sending.Dispose();host.Close();}
                };
                try{Application.Run(host);}finally{Application.ThreadException-=onError;}
            }
        }}finally{GeneralStyle.AnimationsEnabled=animation;}
        if(failure!=null)throw new IOException("Fluxo UI: "+failure.Message,failure);
    }
    static async System.Threading.Tasks.Task UIWait(Func<bool> ready,string message){var elapsed=Stopwatch.StartNew();while(!ready()){if(elapsed.ElapsedMilliseconds>40000)throw new IOException(message);await System.Threading.Tasks.Task.Delay(25);}}

    static FixedCatalog EmptyCatalog(){return new FixedCatalog{Schema=1,Id="network-fixture",GameVersion="2.5",GameFileVersion="2.5.0.0",Packages=new List<FixedMod>(),Defaults=new List<FixedDefault>()};}
    static GeneralModState State(string game,string original){var state=new GeneralModState{Destination=game};state.List.Mods.Add(new GeneralModEntry{Id="fixture",Name="Installed fixture",DefaultVariant="default",Variants=new List<GeneralModVariant>{new GeneralModVariant{Id="default",Name="Main",Sources=new List<string>{original}}}});return state;}
    static string Target(string root,string name){string path=Path.Combine(root,"online-testes-"+Guid.NewGuid().ToString("N"),name);Directory.CreateDirectory(path);File.WriteAllText(Safe.In(path,".fixture-only"),"isolated fixture");return path;}
    static void Write(string root,string path,string value){string target=Safe.In(root,path);Directory.CreateDirectory(Path.GetDirectoryName(target));File.WriteAllText(target,value,new UTF8Encoding(false));}
    static string[] TransportFiles(string folder){return Directory.GetFiles(folder).Select(Path.GetFileName).Where(name=>!name.StartsWith(".st",StringComparison.Ordinal)).OrderBy(name=>name,StringComparer.Ordinal).ToArray();}
    static IEnumerable<string> Archives(string root){return Directory.Exists(root)?Directory.GetFiles(root,"*",SearchOption.AllDirectories).Where(path=>new[]{".zip",".rar",".7z",".cache",".tar"}.Contains(Path.GetExtension(path).ToLowerInvariant())):new string[0];}
    static int FolderCount(P2PService service){return ((IEnumerable)service.Engine.Request("GET","config/folders",null)).Cast<object>().Count();}
    static void Loopback(List<string> checks,P2PService service){var xml=new XmlDocument{XmlResolver=null};xml.Load(Path.Combine(service.Root,"syncthing","config.xml"));Check(checks,new[]{"globalAnnounceEnabled","localAnnounceEnabled","relaysEnabled","natEnabled"}.All(name=>xml.SelectSingleNode("/configuration/options/"+name).InnerText=="false")&&xml.SelectSingleNode("/configuration/options/listenAddress").InnerText=="tcp://127.0.0.1:"+service.Engine.ListenPort,"Auxiliar isolado limita conexão a loopback sem descoberta, NAT ou relays.");Check(checks,FolderCount(service)==0,"Auxiliar não adota nenhuma pasta pessoal ou padrão.");}
    static void Check(List<string> checks,bool success,string label){if(!success)throw new IOException(label);checks.Add(label);}
    static void Wait(Func<bool> ready,string failure){var timer=Stopwatch.StartNew();while(!ready()){if(timer.ElapsedMilliseconds>=40000)throw new IOException(failure);Thread.Sleep(100);}}
}
