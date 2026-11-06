using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    internal static void RunProjectDeletionTests(string parent){
        string root=Path.Combine(parent,Regex.IsMatch(Path.GetFullPath(parent),@"(?i)[\\/]online-testes-[a-f0-9]{32}([\\/]|$)")?"d":"online-testes-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var checks=new List<string>();Action<bool,string> check=(yes,text)=>{if(!yes)throw new IOException(text);checks.Add(text);};
        Action<Action,string> refuse=(action,text)=>{try{action();}catch(IOException){checks.Add(text);return;}throw new IOException(text);};
        var catalog=FixedData.Load();string game=Path.Combine(root,"game");Directory.CreateDirectory(game);File.WriteAllText(Safe.In(game,".fixture-only"),"fixture");
        var store=new GeneralProjectStore(Path.Combine(root,"p"));var list=new GeneralModList();
        string owned=TestZip(root,"owned.zip","mods/owned.txt","owned"),shared=TestZip(root,"shared.zip","mods/shared.txt","shared"),modified=TestZip(root,"modified.zip","mods/modified.txt","original"),before=TestZip(root,"before.zip","mods/preexisting.txt","before");
        foreach(string zip in new[]{owned,shared,modified,before})list.Mods.Add(NewEntry(Path.GetFileName(zip),zip));
        var project=store.Create("Apagar com mods",list,"light","pt",game);string data=store.DataPath(project);
        var engine=new GeneralModInstaller(game,Path.Combine(data,"Instalacoes"));
        Directory.CreateDirectory(Safe.In(game,"mods"));File.WriteAllText(Safe.In(game,"mods/preexisting.txt"),"before");File.WriteAllText(Safe.In(game,"mods/untracked.txt"),"untracked");
        foreach(var mod in list.Mods){var variant=mod.Variants[0];engine.Install(variant.Sources[0],mod.Id+"/"+variant.Id+"/0/"+variant.Sources[0],null);}
        File.WriteAllText(Safe.In(game,"mods/modified.txt"),"edited later");
        var peer=store.Create("Outro projeto",new GeneralModList(),"light","pt",game);
        new GeneralModInstaller(game,Path.Combine(store.DataPath(peer),"Instalacoes")).Install(shared,"peer",null);
        var deletion=new GeneralProjectDeletion(store,catalog,true);var plan=deletion.Plan(project);
        check(plan.Remove.Select(file=>file.Path).SequenceEqual(new[]{"mods/owned.txt"}),"Only owned, unchanged, unshared files enter the permanent erase list.");
        check(plan.Preserve.Count==3&&plan.Preserve.Any(file=>file.Path=="mods/shared.txt")&&plan.Preserve.Any(file=>file.Path=="mods/preexisting.txt")&&plan.Preserve.Any(file=>file.Path=="mods/modified.txt"),"Preview identifies shared, preexisting and edited files as preserved.");
        check(File.Exists(Safe.In(game,"mods/owned.txt"))&&store.Read().Projects.Count==2,"Preview is read-only for game files and project index.");
        check(Directory.GetFiles(data,"exclusao-permanente-*.json").Length==0,"Preview creates no deletion journal or archive.");
        var changed=store.Load(project);changed.Theme="dark";new GeneralModLibrary(Safe.In(data,"lista.json")).Save(changed);
        refuse(()=>deletion.Apply(plan),"A changed project invalidates confirmation before erasing any file.");check(File.Exists(Safe.In(game,"mods/owned.txt")),"Rejected stale confirmation preserves files.");
        plan=deletion.Plan(project);File.WriteAllText(Safe.In(game,"mods/owned.txt"),"changed after preview");refuse(()=>deletion.Apply(plan),"A changed file invalidates the preview before deletion.");
        File.WriteAllText(Safe.In(game,"mods/owned.txt"),"owned");plan=deletion.Plan(project);deletion.Apply(plan);
        check(!File.Exists(Safe.In(game,"mods/owned.txt"))&&!store.Read().Projects.Any(item=>item.Id==project.Id),"Confirmed permanent deletion erases the owned file and removes only its project from the index.");
        check(new[]{"mods/shared.txt","mods/preexisting.txt","mods/modified.txt","mods/untracked.txt"}.All(path=>File.Exists(Safe.In(game,path)))&&store.Read().Projects.Single().Id==peer.Id,"Neighbor project, untracked, modified and preexisting files survive.");
        check(File.Exists(owned)&&!Directory.Exists(Safe.In(data,"backups"))&&Directory.GetFiles(data,"exclusao-permanente-*.json").Any(path=>File.ReadAllText(path).Contains("arquivos_apagados")),"Deletion does not erase downloads or create a backup of erased data; the journal records completion.");
        var retry=store.Create("Interrompido",new GeneralModList(),"light","pt",game);var retryInstaller=new GeneralModInstaller(game,Path.Combine(store.DataPath(retry),"Instalacoes"));
        retryInstaller.Install(TestZip(root,"retry-a.zip","mods/retry-a.txt","a"),"a",null);retryInstaller.Install(TestZip(root,"retry-b.zip","mods/retry-b.txt","b"),"b",null);
        var retryEngine=new GeneralProjectDeletion(store,catalog,true);var retryPlan=retryEngine.Plan(retry);
        refuse(()=>retryEngine.Apply(retryPlan,null,1),"Interrupted permanent deletion reports failure instead of pretending it completed.");
        check(store.Read().Projects.Any(item=>item.Id==retry.Id)&&!File.Exists(Safe.In(game,"mods/retry-a.txt"))&&File.Exists(Safe.In(game,"mods/retry-b.txt")),"Partial failure leaves the project listed and preserves pending files for review.");
        retryPlan=retryEngine.Plan(retry);check(retryPlan.Missing==1&&retryPlan.Remove.Count==1,"Retry rebuilds a fresh preview and recognizes files already erased.");retryEngine.Apply(retryPlan);
        check(!store.Read().Projects.Any(item=>item.Id==retry.Id)&&!File.Exists(Safe.In(game,"mods/retry-b.txt")),"Retry completes without restoring or duplicating already erased files.");
        string sender=Path.Combine(root,"sender");Directory.CreateDirectory(sender);File.WriteAllText(Safe.In(sender,".fixture-only"),"fixture");var senderState=new GeneralModState{Destination=sender,List=new GeneralModList()};
        var sent=NewEntry("Recebido",owned);senderState.List.Mods.Add(sent);string senderData=Path.Combine(root,"sender-data");new GeneralModInstaller(sender,Path.Combine(senderData,"Instalacoes")).Install(owned,sent.Id+"/default/0/"+owned,null);
        string pack=Path.Combine(root,"pack.zip");InstalledSnapshot.Create(pack,senderState,new[]{sent.Id},catalog,senderData,false,null);
        string received=Path.Combine(root,"receiver");Directory.CreateDirectory(received);File.WriteAllText(Safe.In(received,".fixture-only"),"fixture");var receiver=store.Create("P2P",new GeneralModList(),"light","pt",received);string receiverData=store.DataPath(receiver);
        new InstalledSnapshotInstaller(received,receiverData,catalog,true).Install(pack,Safe.Hash(pack),null);File.Delete(pack);
        var receivedEngine=new GeneralProjectDeletion(store,catalog,true);var receivedPlan=receivedEngine.Plan(receiver);
        check(receivedPlan.Remove.Count==1&&receivedPlan.Remove[0].Path=="mods/owned.txt","Snapshot receipts identify installed files without the received ZIP.");receivedEngine.Apply(receivedPlan);
        check(!File.Exists(Safe.In(received,"mods/owned.txt")),"Permanent deletion also removes owned files received over P2P.");
        var protectedProject=store.Create("Cyberpunk 2077",GeneralModLibrary.Seed(catalog,new ModImportItem[0]),"light","pt",null,"cyberpunk-2077");refuse(()=>new GeneralProjectDeletion(store,catalog,true).Plan(protectedProject),"Built-in projects remain protected.");
        string fixedMeta=Path.Combine(root,"fixed-meta");Directory.CreateDirectory(fixedMeta);FixedAsset fixedAsset;var fixedCatalog=OnlineTests.MakeCatalog(fixedMeta,out fixedAsset);
        string fixedGame=OnlineTests.Fixture(root,"fixed-game");
        foreach(var file in fixedCatalog.Packages.SelectMany(mod=>mod.Files)){string path=Safe.In(fixedGame,file.Path);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,"test "+file.Path);}
        var fixedStore=new GeneralProjectStore(Path.Combine(root,"fixed-projects"));var fixedList=GeneralModLibrary.Seed(fixedCatalog,new ModImportItem[0]);
        var fixedProject=fixedStore.Create("Fixed",fixedList,"light","pt",fixedGame);
        var outside=new FixedMod{Id="Outside",Name="Outside",Version="1",GameVersion="2.5",Dependencies=new[]{"Extra"},Assets=new List<FixedAsset>{fixedAsset},Files=new List<FixedFile>{new FixedFile{Asset="a0",Entry="r6/tweaks/outside.yaml",Path="r6/tweaks/outside.yaml",Size=7,Sha256=TestHash("outside")}}};
        fixedCatalog.Packages.Add(outside);File.WriteAllText(Safe.In(fixedGame,outside.Files[0].Path),"outside");
        var fixedDeletion=new GeneralProjectDeletion(fixedStore,fixedCatalog,true);var fixedPlan=fixedDeletion.Plan(fixedProject);
        check(fixedPlan.Remove.Count==0,"Dependencies still used by a catalog mod outside the project are preserved.");
        File.Delete(Safe.In(fixedGame,outside.Files[0].Path));fixedPlan=fixedDeletion.Plan(fixedProject);
        check(fixedPlan.Remove.Count==2&&fixedPlan.Preserve.Count==5,"Fixed recipes identify removable complement files and preserve the immutable base.");
        fixedDeletion.Apply(fixedPlan);
        check(fixedCatalog.Packages.Single(mod=>mod.Id=="NightCoop").Files.All(file=>File.Exists(Safe.In(fixedGame,file.Path)))&&File.Exists(Safe.In(fixedGame,"bin/x64/Cyberpunk2077.exe")),"Permanent deletion preserves NightCoop/RED4ext and the game executable.");
        check(fixedCatalog.Packages.Single(mod=>mod.Id=="Extra").Files.All(file=>!File.Exists(Safe.In(fixedGame,file.Path))),"Fixed complement files are erased permanently without deactivation backups.");
        TestProjectDeleteUi(root,catalog,check);
        GeneralStyle.UseTheme("light");GeneralStyle.Language="pt";
        Safe.WriteJson(Path.Combine(root,"resultado-project-deletion.json"),new{Passed=true,Tests=checks,RealGameTouched=false,RealProjectDeleted=false,NetworkUsed=false});
    }
    static void TestProjectDeleteUi(string root,FixedCatalog catalog,Action<bool,string> check){
        foreach(string theme in new[]{"light-red","light","dark"})foreach(string lang in new[]{"pt","en"}){
            GeneralStyle.UseTheme(theme);GeneralStyle.Language=lang;
            using(var choice=new GeneralProjectDeleteChoice("Projeto de teste")){
                choice.StartPosition=FormStartPosition.Manual;choice.Location=new Point(-30000,-30000);choice.Show();Application.DoEvents();
                check(!choice.RemoveMods&&((Button)choice.AcceptButton).Name=="CancelDelete","App-only is the initial choice; Enter cancels: "+theme+"/"+lang);
                AllControls(choice).OfType<RadioButton>().Single(item=>item.Name=="DeleteMods").Checked=true;check(choice.RemoveMods,"The second option selects permanent mod deletion: "+theme+"/"+lang);
                check(AllControls(choice).OfType<Button>().Where(button=>button.Name!="").All(button=>button.Bottom<=button.Parent.ClientSize.Height),"Choice buttons fit inside the dialog: "+theme+"/"+lang);CaptureUi(choice,Path.Combine(root,"delete-choice-"+theme+"-"+lang+".png"));choice.Close();
            }
            var preview=new ProjectDeletePlan{Root=@"D:\Games\Example"};preview.Remove.Add(new ProjectDeleteFile{Path="mods/example.archive"});preview.Preserve.Add(new ProjectDeleteFile{Path="mods/shared.archive",Reason="Usado por outro projeto"});
            using(var review=new GeneralProjectDeleteReview("Projeto de teste",preview)){
                review.StartPosition=FormStartPosition.Manual;review.Location=new Point(-30000,-30000);review.Show();Application.DoEvents();
                check(((Button)review.AcceptButton).Name=="CancelProjectDelete"&&AllControls(review).OfType<TextBox>().Single().Text.Contains("mods/example.archive"),"Final review lists exact paths and defaults to cancel: "+theme+"/"+lang);
                CaptureUi(review,Path.Combine(root,"delete-review-"+theme+"-"+lang+".png"));review.Close();
            }
        }
    }
}
