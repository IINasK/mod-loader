using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    internal static void RunProjectCloneTests(string parent){
        string root=System.Text.RegularExpressions.Regex.IsMatch(Path.GetFullPath(parent).Replace(Path.DirectorySeparatorChar,'/'),@"(?i)/online-testes-[a-f0-9]{32}(/|$)")?Path.Combine(parent,"c"):OnlineTests.NestedRoot(parent,"clones");Directory.CreateDirectory(root);
        var checks=new List<string>();Action<bool,string> check=(ok,text)=>{if(!ok)throw new IOException(text);checks.Add(text);};
        var catalog=new FixedCatalog{Schema=1,Id="clone",GameVersion="2.5",GameFileVersion="2.5.0.0",Packages=new List<FixedMod>()};
        string game=Path.Combine(root,"g");Directory.CreateDirectory(game);File.WriteAllText(Path.Combine(game,".fixture-only"),"fixture");
        string original=TestZip(root,"original.zip","mods/nested/one.txt","same installed bytes");
        var mod=NewEntry("Meu mod",original);mod.Category="Minha categoria";mod.Tags=new List<string>{"essential"};mod.Nsfw=true;mod.Description="Descrição";
        var list=new GeneralModList{Mods=new List<GeneralModEntry>{mod},Categories=new List<string>{mod.Category}};list.CategoryDescriptions[mod.Category]="Categoria personalizada";list.Presets.Add(new GeneralModPreset{Id="mine",Name="Meu preset",ModIds=new List<string>{mod.Id}});
        var store=new GeneralProjectStore(Path.Combine(root,"p"));var source=store.Create("Original",list,"dark","pt",game);string data=store.DataPath(source),key=mod.Id+"/default/0/"+original;
        var state=store.Load(source);state.SelectedPreset="mine";state.ChosenVariants[mod.Id]="default";state.ResolvedDownloads[key]=original;
        new GeneralModLibrary(Path.Combine(data,"lista.json")).Save(state);var engine=new GeneralModInstaller(game,Path.Combine(data,"Instalacoes"));engine.Install(original,key,null);
        var asset=new FixedAsset{Id="original",Sha256=Safe.Hash(original),Size=new FileInfo(original).Length,Format="zip"};new DownloadCache(Path.Combine(data,"Cache"),()=>root).Import(original,asset);
        Directory.CreateDirectory(Path.Combine(data,"P2P"));File.WriteAllBytes(Path.Combine(data,"P2P","helper.bin"),new byte[1048576]);File.WriteAllText(Path.Combine(data,"P2P","sessions.json"),"private session marker");File.WriteAllBytes(Path.Combine(data,"Cache","old.cache"),new byte[8192]);
        string sourceHash=Safe.Hash(Path.Combine(data,"lista.json")),installedHash=Safe.Hash(Safe.In(game,"mods/nested/one.txt")),downloadHash=Safe.Hash(original);
        var copy=store.Duplicate(source,"Minha versão",catalog);var cloned=store.Load(copy);string copiedData=store.DataPath(copy);
        check(copy.Id!=source.Id&&copy.TemplateId==null&&!copy.Legacy&&copy.Name=="Minha versão","Duplicate has a new identity, own data directory and editable personal-project status.");
        check(Safe.Json.Serialize(cloned.List)==Safe.Json.Serialize(state.List)&&cloned.SelectedPreset==state.SelectedPreset,"Duplicate preserves names, categories, descriptions, tags, adult flag and presets.");
        check(cloned.Destination==game&&cloned.DownloadFolder==state.DownloadFolder&&cloned.ChosenVariants[mod.Id]=="default"&&cloned.ResolvedDownloads[key]==original,"Selected variants, download references and the existing game destination are preserved.");
        check(new DownloadCache(Path.Combine(copiedData,"Cache"),()=>root).PathFor(asset)==original,"Only the small source index is copied; both projects refer to the same original download.");
        var copiedEngine=new GeneralModInstaller(game,Path.Combine(copiedData,"Instalacoes"));check(copiedEngine.Status(key)==GeneralModInstaller.Installed,"Custom installed mods remain recognized from copied metadata without reinstalling them.");
        check(!Directory.Exists(Path.Combine(copiedData,"P2P"))&&Directory.GetFiles(copiedData,"*",SearchOption.AllDirectories).All(file=>Path.GetExtension(file)==".json")&&Directory.GetFiles(copiedData,"*",SearchOption.AllDirectories).Sum(file=>new FileInfo(file).Length)<65536,"Clone contains only small project/recognition JSON, without archives, helper executables, P2P sessions or backups.");
        cloned.List.Mods[0].Name="Nome só da cópia";cloned.List.Presets[0].Name="Preset só da cópia";new GeneralModLibrary(Path.Combine(copiedData,"lista.json")).Save(cloned);
        check(Safe.Hash(Path.Combine(data,"lista.json"))==sourceHash&&store.Load(source).List.Mods[0].Name=="Meu mod","Editing the duplicate does not modify the original project definitions.");
        File.Delete(original);var manifest=InstalledSnapshot.Inspect(cloned,new[]{mod.Id},catalog,copiedData,false);check(manifest.Files.Count==1&&manifest.List.Mods.Single().Name=="Nome só da cópia","Duplicate can export installed custom files after the original package is deleted.");
        copiedEngine.Remove(key);check(Safe.Hash(Safe.In(game,"mods/nested/one.txt"))==installedHash&&engine.Status(key)==GeneralModInstaller.Installed,"Copied recognition never acquires deletion ownership of the original project's files.");
        string snapshot=Path.Combine(root,"snapshot.zip");InstalledSnapshot.Create(snapshot,state,new[]{mod.Id},catalog,data,true,null);
        string receivedGame=Path.Combine(root,"r");Directory.CreateDirectory(receivedGame);File.WriteAllText(Path.Combine(receivedGame,".fixture-only"),"fixture");
        var received=store.Create("Recebido",manifest.List,"light","pt",receivedGame);string receivedData=store.DataPath(received);var receiver=new InstalledSnapshotInstaller(receivedGame,receivedData,catalog,true);string receipt=receiver.Install(snapshot,Safe.Hash(snapshot),null);
        var receivedState=store.Load(received);string work=mod.Id+"/default";receivedState.SnapshotReceipts[work]=receipt;new GeneralModLibrary(Path.Combine(receivedData,"lista.json")).Save(receivedState);
        string receivedHash=Safe.Hash(Safe.In(receivedGame,"mods/nested/one.txt"));var receivedCopy=store.Duplicate(received,"Cópia recebida",catalog);string receivedCopyData=store.DataPath(receivedCopy);var copiedReceiver=new InstalledSnapshotInstaller(receivedGame,receivedCopyData,catalog,true);
        check(copiedReceiver.Status(receipt,work)==GeneralModInstaller.Installed&&store.Load(receivedCopy).SnapshotReceipts[work]==receipt,"P2P-installed mods remain recognized using copied protected receipt metadata.");
        copiedReceiver.Remove(receipt,new[]{work},null);check(Safe.Hash(Safe.In(receivedGame,"mods/nested/one.txt"))==receivedHash&&receiver.Status(receipt,work)==GeneralModInstaller.Installed,"Duplicate P2P recognition preserves existing files and the original receipt during removal.");
        var builtIn=store.Create("Cyberpunk 2077",GeneralModLibrary.Seed(catalog,new ModImportItem[0]),"light","pt",game,"cyberpunk-2077");string builtInHash=Safe.Hash(Path.Combine(store.DataPath(builtIn),"lista.json"));var editable=store.Duplicate(builtIn,"Meu Cyberpunk",catalog);
        check(editable.TemplateId==null&&store.Load(editable).List.Presets.Count==store.Load(builtIn).List.Presets.Count&&Safe.Hash(Path.Combine(store.DataPath(builtIn),"lista.json"))==builtInHash,"Built-in projects clone as personal editable versions while the source remains protected and unchanged.");
        var missing=store.Create("Sem jogo",new GeneralModList(),"light","pt",Path.Combine(root,"missing"));check(store.Load(store.Duplicate(missing,"Cópia sem jogo",catalog)).Destination==store.Load(missing).Destination,"A definitions-only project can be duplicated before its game folder exists.");
        int count=store.Read().Projects.Count;bool rejected=false;try{store.Duplicate(builtIn," ",catalog);}catch(IOException){rejected=true;}check(rejected&&store.Read().Projects.Count==count,"Invalid clone name does not register a partial project.");
        TestForm(catalog,store.Load(editable),Path.Combine(root,"ui-copy"),root,async form=>{form.AttachProject(store,editable);check(!form.ProtectedPreset&&!form.tableName.ReadOnly&&form.deletePreset.Enabled,"Presets in the personal duplicate are editable and deletable in the real UI.");await Task.CompletedTask;});
        TestForm(catalog,store.Load(builtIn),Path.Combine(root,"ui-base"),root,async form=>{
            form.AttachProject(store,builtIn);check(form.ProtectedPreset&&form.tableName.ReadOnly,"Source built-in preset stays protected in the real UI.");
            int before=store.Read().Projects.Count;using(var timer=new Timer{Interval=60}){timer.Tick+=(s,e)=>{var dialog=Application.OpenForms.OfType<GeneralNameDialog>().FirstOrDefault();if(dialog==null)return;timer.Stop();var field=AllControls(dialog).OfType<TextBox>().Single(box=>box.Name=="NameInput").Parent;var note=AllControls(dialog).OfType<Label>().Single(label=>label.Name=="NameDescription");var fieldBounds=dialog.RectangleToClient(field.RectangleToScreen(field.ClientRectangle));var noteBounds=dialog.RectangleToClient(note.RectangleToScreen(note.ClientRectangle));check(fieldBounds.Height>=44&&noteBounds.Top>=fieldBounds.Bottom,"Clone name field keeps its full rounded frame and the description stays outside it.");CaptureUi(dialog,Path.Combine(root,"clone-name.png"));check(AllControls(dialog).OfType<Label>().Any(label=>label.Name=="NameDescription"&&label.Text.Contains("mesma pasta")),"Clone dialog explains shared game files before creating a project.");((Button)dialog.CancelButton).PerformClick();};timer.Start();form.ProjectCommand("clone");}
            check(store.Read().Projects.Count==before&&form.ProjectAction==null&&form.Visible,"Canceling duplicate leaves the project library and current window unchanged.");
            form.optionsButton.PerformClick();var menu=(GeneralActionsMenu)form.menus.Current;var button=AllControls(menu).OfType<Button>().Single(item=>item.Name=="clone");
            CompleteProjectName("Cópia pelo menu",()=>button.PerformClick());
            var created=store.Read().Projects.Single(item=>item.Name=="Cópia pelo menu");check(created.TemplateId==null&&form.ProjectAction=="project/"+created.Id,"Duplicate menu action creates an editable project and navigates directly to it.");await Task.CompletedTask;
        });
        check(Safe.Hash(Path.Combine(store.DataPath(builtIn),"lista.json"))==builtInHash&&Safe.Hash(Safe.In(game,"mods/nested/one.txt"))==installedHash,"Clone UI preserves the built-in project file and installed mod bytes.");
        Safe.WriteJson(Path.Combine(root,"resultado-project-clone.json"),new{Passed=true,Tests=checks,RealGameTouched=false,NetworkUsed=false,ModPayloadsCopied=false});
    }
}
