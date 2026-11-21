using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;

internal sealed partial class GeneralModWindow {
    const string UpdateFixtureOld="https://www.nexusmods.com/stardewvalley/mods/42?tab=files&file_id=10";
    const string UpdateFixtureNew="https://www.nexusmods.com/stardewvalley/mods/42?tab=files&file_id=11";

    internal static void RunSourceUpdateTests(string parent) {
        string root=OnlineTests.NestedRoot(parent,"source-updates-");Directory.CreateDirectory(root);
        var checks=new List<string>();
        Action<bool,string> check=(ok,label)=>{if(!ok)throw new IOException(label);checks.Add(label);};
        SourceUpdateRoundTrip(Safe.In(root,"normal"),check);
        SourceUpdateInterrupted(Safe.In(root,"cancel"),false,false,check);
        SourceUpdateInterrupted(Safe.In(root,"wrong"),true,false,check);
        SourceUpdateInterrupted(Safe.In(root,"commit"),false,true,check);
        SourceUpdateInterrupted(Safe.In(root,"saved"),false,true,check,true);
        SourceUpdateCyberpunkSnapshot(Safe.In(root,"snapshot"),check);
        foreach(string predecessor in new[]{"fixed","fixed-reinstall","import","received"})SourceUpdateForeign(Safe.In(root,predecessor),predecessor,check);
        foreach(string scenario in new[]{"plugin-data","plugin-code","plugin-other","plugin-folder"})
            SourceUpdateForeign(Safe.In(root,scenario),scenario,check);
        SourceUpdateRecognitionOnly(Safe.In(root,"recognition"),check);
        SourceUpdateSharedManual(Safe.In(root,"shared"),check);
        foreach(string scenario in new[]{"normal","save-retry","save-reopen","changed"})
            SourceUpdateFinalize(Safe.In(root,"finish-"+scenario),scenario,check);
        Safe.WriteJson(Safe.In(root,"result.json"),new{Passed=true,Checks=checks,RealGameTouched=false,LiveAuthenticatedNexus=false});
    }

    static void SourceUpdateFinalize(string root,string scenario,Action<bool,string> check){
        Directory.CreateDirectory(root);
        string game=Directory.CreateDirectory(Safe.In(root,"g")).FullName,data=Safe.In(root,"d");
        File.WriteAllText(Safe.In(game,".fixture-only"),"isolated fixture");
        string downloads=Directory.CreateDirectory(Safe.In(root,"downloads")).FullName;
        var files=new Dictionary<string,string>{{"engine/tools/compiler.exe","old"},{"engine/tools/compiler_lib.dll","library"},
            {"engine/config/base/scripts.ini","[Scripts]"},{"r6/config/framework/compile.toml","enabled = true"}};
        string old=SourceUpdateZip(root,"old.zip",files);files["engine/tools/compiler.exe"]="new";
        string next=SourceUpdateZip(downloads,"new.zip",files);
        var entry=NewEntry("Compiler fixture",UpdateFixtureOld);entry.Id="update-fixture";
        entry.DetectedPaths.Add("mods/temporary/observation.txt");
        var state=new GeneralModState{Destination=game,DownloadFolder=downloads,List=new GeneralModList{Mods=new List<GeneralModEntry>{entry}}};
        if(scenario=="normal"){
            var observed=Safe.Json.Deserialize<GeneralModList>(Safe.Json.Serialize(state.List));
            observed.Mods.Single().DetectedPaths=files.Keys.ToList();bool refused=false;
            try{GeneralModLibrary.ValidateList(observed,true);}catch(IOException error){
                refused=error.Message.Contains("engine/tools/compiler.exe")&&error.Message.Contains("Compiler fixture")&&error.Message.Contains("F5");
            }
            check(refused,"The inventory-capture validator still rejects framework paths and now names the refused path with a recovery instruction.");
        }
        GeneralModState saved=null;
        TestForm(new FixedCatalog{Packages=new List<FixedMod>()},state,data,root,async form=>{
            form.Persist();
            var initial=form.MakeWork(form.state.List.Mods.Single());form.Generic(game).Install(old,initial.Receipt(0),_=>{});
            form.testModUpdateSession=SourceUpdateSession();await form.Scan();await WaitForVersionCheck(form);
            var job=form.SelectedUpdates().FirstOrDefault();
            if(job==null)throw new IOException("Finalize fixture is not ready: "+form.ListStatus(initial.Entry)+" / "+form.SourceUpdateOwnership(initial).Reason);
            int lookups=0;string failure=null;
            form.testUpdatePackage=source=>{lookups++;return SourceUpdatePackage(source,next);};
            form.launch=_=>{throw new IOException("The verified fixture archive must not require a browser.");};
            form.testFailurePrompt=message=>{failure=message;return false;};
            FileStream saveLease=null;
            form.testUpdateCheckpoint=phase=>{
                if(scenario!="normal"&&phase=="files-committed")
                    saveLease=new FileStream(Safe.In(data,"lista.json"),FileMode.Open,FileAccess.Read,FileShare.Read);
            };
            try{await form.RunWork(job,game,false);}finally{if(saveLease!=null)saveLease.Dispose();}
            var work=form.MakeWork(form.state.List.Mods.Single());
            check(File.ReadAllText(Safe.In(game,"engine/tools/compiler.exe"))=="new"&&form.Generic(game).ReadUpdateRecords().Single().Files.Count==4,
                "Framework and generic application paths remain represented by the complete installation receipt: "+scenario);
            if(scenario!="normal"){
                check(failure!=null&&failure.Contains("arquivos")&&failure.Contains("cadastro")&&failure.Contains("F5"),
                    "A failure after committing files explains that finalizing the project is pending and gives a recovery action: "+scenario);
                check(form.library.Load().List.Mods.Single().Variants.Single().Sources.Single()==UpdateFixtureOld&&
                    !form.Generic(game).ReadUpdateRecords().Single().Upgrade.Reconciled,
                    "A project-save failure preserves the durable pending receipt and the last valid saved source: "+scenario);
                if(scenario=="changed"){
                    File.WriteAllText(Safe.In(game,"engine/tools/compiler.exe"),"manual change");
                    await form.Scan();await WaitForVersionCheck(form);
                    check(File.ReadAllText(Safe.In(game,"engine/tools/compiler.exe"))=="manual change"&&
                        !form.Generic(game).ReadUpdateRecords().Single().Upgrade.Reconciled,
                        "Recovery never finalizes or overwrites a package changed manually after the interrupted save.");return;
                }
                if(scenario=="save-reopen"){saved=form.library.Load();return;}
                await form.Scan();await WaitForVersionCheck(form);
            }
            work=form.MakeWork(form.state.List.Mods.Single());
            check(work.Entry.DetectedPaths.Count==0&&form.library.Load().List.Mods.Single().Variants.Single().Sources.Single()==UpdateFixtureNew&&
                form.Generic(game).ReadUpdateRecords().Single().Upgrade.Reconciled&&lookups==1,
                "Finishing the update clears provisional scan observations, persists the complete source and reuses installed files: "+scenario);
            check(form.UpdateHistoryFor(work.Entry.Id).Length==1,
                "Update recovery records its successful transaction only once: "+scenario);
        });
        if(saved!=null)TestForm(new FixedCatalog{Packages=new List<FixedMod>()},saved,data,root,async form=>{
            form.launch=_=>{throw new IOException("Reopening must reuse the committed package.");};
            await form.Scan();await WaitForVersionCheck(form);
            var work=form.MakeWork(form.state.List.Mods.Single());
            check(form.Generic(game).Status(work.Receipt(0))==GeneralModInstaller.Installed&&work.Variant.Sources.Single()==UpdateFixtureNew&&
                work.Entry.DetectedPaths.Count==0&&form.Generic(game).ReadUpdateRecords().Single().Upgrade.Reconciled,
                "Reopening a project recovers framework updates without redownloading or relaxing detection-path validation.");
        });
    }

    static string SourceUpdateZip(string root,string name,Dictionary<string,string> files) {
        Directory.CreateDirectory(root);string path=Safe.In(root,name);
        using(var zip=ZipFile.Open(path,ZipArchiveMode.Create))foreach(var file in files)
            using(var writer=new StreamWriter(zip.CreateEntry(file.Key).Open()))writer.Write(file.Value);
        return path;
    }

    static GeneralModState SourceUpdateState(string root,out string oldZip,out string newZip) {
        Directory.CreateDirectory(root);string game=Directory.CreateDirectory(Safe.In(root,"g")).FullName;
        File.WriteAllText(Safe.In(game,".fixture-only"),"test");
        string downloads=Directory.CreateDirectory(Safe.In(root,"downloads")).FullName;
        oldZip=SourceUpdateZip(root,"old.zip",new Dictionary<string,string>{{"mods/first/main.txt","old"},{"mods/first/obsolete.txt","remove me"},{"mods/first/anchor.bin","anchor"}});
        newZip=SourceUpdateZip(downloads,"fixture-"+Guid.NewGuid().ToString("N")+".zip",new Dictionary<string,string>{{"mods/first/main.txt","new"},{"mods/first/added.txt","added"},{"mods/first/anchor.bin","anchor"}});
        var entry=NewEntry("Source update fixture",UpdateFixtureOld);entry.Id="update-fixture";
        return new GeneralModState{Destination=game,DownloadFolder=downloads,Language="pt",Theme="dark",
            List=new GeneralModList{Mods=new List<GeneralModEntry>{entry}}};
    }

    static string SourceUpdateMd5(string path) {
        using(var stream=File.OpenRead(path))using(var md5=MD5.Create())return Safe.Hex(md5.ComputeHash(stream));
    }

    static Task<GeneralNexusUpdatePackage> SourceUpdatePackage(string target,string archive,string fileName=null,string version="2.0") {
        var link=GeneralNexusLink.Parse(target);long bytes=new FileInfo(archive).Length;string hash=SourceUpdateMd5(archive);
        return GeneralNexusUpdatePackage.Read(target,"fixture-key",(Uri uri,string key,out HttpStatusCode status)=>{
            status=HttpStatusCode.OK;
            if(uri.AbsolutePath.EndsWith("/files/"+link.File+".json",StringComparison.Ordinal))
                return Safe.Json.Serialize(new{file_id=link.File,category_id=1,name="Updated fixture",file_name=fileName??Path.GetFileName(archive),version=version,size_in_bytes=bytes});
            if(uri.AbsolutePath.Contains("/md5_search/"))return uri.AbsolutePath.EndsWith("/"+hash+".json",StringComparison.Ordinal)
                ?Safe.Json.Serialize(new[]{new{mod=new{mod_id=link.Mod,domain_name=link.Game},file_details=new{file_id=link.File,category_id=1,size_in_bytes=bytes}}}):"[]";
            throw new IOException("Unexpected fixture Nexus request.");
        });
    }

    static NexusUpdateSession SourceUpdateSession() {
        return new NexusUpdateSession("fixture-key",(GeneralNexusLink link,string key,out HttpStatusCode status)=>{
            status=HttpStatusCode.OK;int old=link.Mod==42?10:20;int next=old+1;
            return Safe.Json.Serialize(new{files=new[]{new{file_id=old,category_id=4,version="1.0"},new{file_id=next,category_id=1,version="2.0"}},
                file_updates=new[]{new{old_file_id=old,new_file_id=next}}});
        });
    }

    static async Task<Work> SourceUpdateReady(GeneralModWindow form,string root,string oldZip) {
        var entry=form.state.List.Mods.Single(mod=>mod.Id=="update-fixture");var work=form.MakeWork(entry);
        form.Generic(form.state.Destination).Install(oldZip,work.Receipt(0),_=>{});
        form.testModUpdateSession=SourceUpdateSession();
        await form.Scan();await WaitForVersionCheck(form);
        return form.SelectedUpdates().Single(job=>job.Entry.Id==entry.Id);
    }

    static async Task VerifyUpdateDeferral(GeneralModWindow form,Work job,string otherId,string root,Action<bool,string> check){
        form.StartSelected();
        await Until(()=>Application.OpenForms.OfType<GeneralInstallConfirmation>().Any(dialog=>dialog.Visible));
        var declined=Application.OpenForms.OfType<GeneralInstallConfirmation>().Single(dialog=>dialog.Visible);
        var no=(Button)declined.Controls.Find("CancelPendingInstall",true).Single();
        check(declined.Text=="Você deseja atualizar?"&&no.Text=="Não"&&
            declined.Controls.Find("ConfirmPendingInstall",true).Single().Text=="Sim"&&declined.CreateBackup,
            "Updating prompts for an explicit yes or no and retains the optional backup.");
        no.PerformClick();
        await Until(()=>declined.IsDisposed);
        check(!form.ManualUpdatesOnly(job.Entry.Id)&&form.SelectedUpdates().Single().Entry.Id==job.Entry.Id,
            "No cancels without suppressing the next update prompt.");

        form.marked.Add(job.Entry.Id);form.marked.Add(otherId);
        form.StartSelected();
        await Until(()=>Application.OpenForms.OfType<GeneralInstallConfirmation>().Any(dialog=>dialog.Visible));
        var defer=Application.OpenForms.OfType<GeneralInstallConfirmation>().Single(dialog=>dialog.Visible);
        var choices=(DataGridView)defer.Controls.Find("InstallChoices",true).Single();
        choices.Rows.Cast<DataGridViewRow>().Single(row=>Equals(row.Cells[1].Value,"Second source update")).Cells[0].Value=false;
        CaptureUi(defer,Safe.In(root,"confirm-update.png"));
        ((Button)defer.Controls.Find("DeferSelectedUpdates",true).Single()).PerformClick();
        await Until(()=>defer.IsDisposed&&form.ManualUpdatesOnly(job.Entry.Id));
        check(form.library.Load().ManualUpdateMods.SequenceEqual(new[]{job.Entry.Id})&&
            form.SelectedUpdates().Single().Entry.Id==otherId&&form.CanUpdateIndividualMod(job.Entry)&&
            File.ReadAllText(Safe.In(form.state.Destination,"mods/first/main.txt"))=="old",
            "Permanently declining affects only checked updates, persists locally, and leaves the files intact.");

        var rowMod=form.ModRows().Single(row=>Equals(row.Tag,job.Entry.Id));
        form.grid.CurrentCell=rowMod.Cells["Name"];
        var menu=form.grid.ContextMenuStrip;
        menu.Show(form.grid,new Point(100,100));
        var item=(ToolStripMenuItem)menu.Items.Find("UpdateIndividualMod",false).Single();
        check(item.Visible&&item.Text=="Atualizar mod"&&item==menu.Items[menu.Items.Count-1],
            "The individual update remains available as the final item of the mod's options.");
        item.PerformClick();
        menu.Close();
        await Until(()=>Application.OpenForms.OfType<GeneralInstallConfirmation>().Any(dialog=>dialog.Visible));
        var manual=Application.OpenForms.OfType<GeneralInstallConfirmation>().Single(dialog=>dialog.Visible);
        var manualList=(DataGridView)manual.Controls.Find("InstallChoices",true).Single();
        check(manualList.Rows.Count==1&&Equals(manualList.Rows[0].Cells[1].Value,job.Entry.Name)&&
            manual.Controls.Find("DeferSelectedUpdates",true).Length==0,
            "Updating from the options targets one mod even with multiple checkboxes selected.");
        ((Button)manual.Controls.Find("CancelPendingInstall",true).Single()).PerformClick();
        await Until(()=>manual.IsDisposed);
        form.marked.Clear();form.updateFocusedId=job.Entry.Id;
    }

    static void SourceUpdateRoundTrip(string root,Action<bool,string> check) {
        string oldZip,newZip;var state=SourceUpdateState(root,out oldZip,out newZip);
        var other=NewEntry("Second source update","https://www.nexusmods.com/stardewvalley/mods/43?tab=files&file_id=20");
        other.Id="update-second";state.List.Mods.Add(other);
        string secondZip=SourceUpdateZip(root,"second.zip",new Dictionary<string,string>{{"mods/second/main.txt","second"}});
        GeneralModState reopen=null;
        TestForm(FixedData.Load(),state,Safe.In(root,"d"),root,async form=>{
            var otherWork=form.MakeWork(form.state.List.Mods.Single(mod=>mod.Id==other.Id));
            form.Generic(state.Destination).Install(secondZip,otherWork.Receipt(0),_=>{});
            var job=await SourceUpdateReady(form,root,oldZip);
            check(job.UpdateTarget==UpdateFixtureNew&&form.SelectedUpdates().Count==2&&form.primaryAction.Text=="Atualizar mods",
                "The scan offers browser-assisted updates for every eligible Nexus item, including batch action.");
            form.updateFocusedId=job.Entry.Id;form.RefreshPrimaryAction();
            check(form.SelectedUpdates().Single().Entry.Id==job.Entry.Id&&form.primaryAction.Text=="Atualizar mod",
                "Focusing one update restricts the action to that item.");
            form.marked.Add(other.Id);
            check(form.SelectedUpdates().Single().Entry.Id==other.Id,"Checkbox selection overrides focused row for source updates.");
            form.marked.Add(job.Entry.Id);check(form.SelectedUpdates().Count==2,"Multiple checked source updates remain a batch.");
            form.marked.Clear();form.updateFocusedId=job.Entry.Id;
            await VerifyUpdateDeferral(form,job,other.Id,root,check);
            var deployment=new GeneralPresetDeployment(state.Destination,form.data);
            var receipts=form.PresetReceipts(state.Destination).ToArray();var disabled=form.DisabledAt(state.Destination).Read();
            foreach(var work in form.PresetWorks())deployment.RecordInstalled(form.PresetPackageFor(work,state.Destination,receipts,disabled),true);
            var originalPreset=GeneralPresetProfiles.Selected(state);
            var sharedPreset=GeneralPresetProfiles.Copy(originalPreset);
            sharedPreset.Id="shared-preset";sharedPreset.Name="Shared";
            sharedPreset.Configurations[job.Entry.Id].Description="Keep this preset description";
            var alternativePreset=GeneralPresetProfiles.Copy(originalPreset);
            alternativePreset.Id="alternative-preset";alternativePreset.Name="Alternative";
            var alternative=alternativePreset.Configurations[job.Entry.Id].Variants.Single();
            alternative.Sources[0]=UpdateFixtureOld.Replace("file_id=10","file_id=99");
            alternative.SourceInfo.Clear();
            state.List.Presets.AddRange(new[]{sharedPreset,alternativePreset});
            string previousFingerprint=form.PresetFingerprint;
            deployment.AdoptReceived(form.state.SelectedPreset,previousFingerprint);form.ReadPresetPresentation();
            check(!form.NeedsPresetLoad,"The update starts from a recorded active preset with a matching fingerprint.");
            form.testUpdatePackage=target=>SourceUpdatePackage(target,newZip);
            string failure=null;form.testFailurePrompt=message=>{failure=message;return false;};
            int opened=0;form.launch=_=>opened++;
            job.BackupUpdate=true;
            await form.RunWork(job,state.Destination,false);
            var updated=form.MakeWork(form.state.List.Mods.Single(mod=>mod.Id==job.Entry.Id));
            check(form.statuses[updated.Key]=="Instalado"&&File.ReadAllText(Safe.In(state.Destination,"mods/first/main.txt"))=="new"&&
                File.ReadAllText(Safe.In(state.Destination,"mods/first/added.txt"))=="added"&&!File.Exists(Safe.In(state.Destination,"mods/first/obsolete.txt")),
                "An exact downloaded successor replaces changed files, adds new files and removes obsolete owned files. Status="+
                form.statuses[updated.Key]+"; detail="+(form.statusDetails.ContainsKey(updated.Key)?form.statusDetails[updated.Key]:"")+"; failure="+failure);
            check(opened==0&&updated.Variant.Sources.Single()==UpdateFixtureNew&&updated.Variant.CyberpunkId==null&&
                updated.Variant.SourceInfo[UpdateFixtureNew].Version=="2.0"&&form.Generic(state.Destination).Status(updated.Receipt(0))==GeneralModInstaller.Installed,
                "A completed matching download is reused and the new source/version/receipt become authoritative.");
            var history=form.UpdateHistoryFor(updated.Entry.Id).Single();
            var historyStore=new GeneralModUpdateHistory(form.data);
            string updateBackup=historyStore.FindBackup(history);
            check(history.Version=="2.0"&&history.Status=="pending"&&history.Modules.Count==0&&
                form.HasPendingUpdateHistory(updated.Entry)&&updateBackup!=null&&File.Exists(updateBackup),
                "An update with no native module keeps its history badge and the selected backup until confirmation.");
            form.ConfirmUpdateHistory(history.Id);
            check(!form.HasPendingUpdateHistory(updated.Entry)&&File.Exists(updateBackup)&&
                historyStore.Read().Single().Status=="confirmed",
                "Manual confirmation clears the badge and preserves the saved history and backup.");
            var updatedRow=form.ModRows().Single(row=>Equals(row.Tag,updated.Entry.Id));
            check(Equals(updatedRow.Cells["Status"].Value,"Instalado · 2.0")&&form.CountVersions().Ready==1,
                "Immediately after the update, the row shows the new installed version and the ready count excludes it.");
            check(form.state.AppliedPresetFingerprint==form.PresetFingerprint&&form.state.AppliedPresetFingerprint!=previousFingerprint&&
                deployment.Read().Fingerprint==form.PresetFingerprint&&!form.NeedsPresetLoad,
                "The applied preset fingerprint is rebound in both project and deployment records without a redundant reload.");
            var saved=form.library.Load();
            check(saved.List.Mods.Single(mod=>mod.Id==updated.Entry.Id).Variants.Single().Sources.Single()==UpdateFixtureNew,
                "The project persists the new source rather than expecting the old catalog package.");
            var shared=GeneralPresetProfiles.ForPreset(saved,sharedPreset.Id).List.Mods.Single(mod=>mod.Id==job.Entry.Id);
            check(shared.Variants.Single().Sources.Single()==UpdateFixtureNew&&shared.Variants.Single().SourceInfo[UpdateFixtureNew].Version=="2.0"&&
                shared.Description=="Keep this preset description"&&alternative.Sources.Single().EndsWith("file_id=99"),
                "Switching to another saved preset uses the updated package, preserves its custom description and leaves distinct file choices alone.");
            // Simulate an already completed update written by a build that only saved the active preset.
            var legacy=sharedPreset.Configurations[job.Entry.Id].Variants.Single();
            legacy.Sources[0]=UpdateFixtureOld;legacy.SourceInfo.Clear();
            await form.RecoverSourceUpdates(state.Destination);
            var repaired=form.library.Load().List.Presets.Single(preset=>preset.Id==sharedPreset.Id).Configurations[job.Entry.Id];
            check(repaired.Variants.Single().Sources.Single()==UpdateFixtureNew&&repaired.Description==shared.Description,
                "A reconciled legacy receipt repairs its exact predecessor in inactive presets without another download.");
            await form.Scan();await WaitForVersionCheck(form);
            check(form.statuses[updated.Key]=="Instalado"&&!form.SelectedUpdates().Any(work=>work.Entry.Id==updated.Entry.Id),
                "A second scan confirms the installed update and does not offer it again.");
            check(form.state.List.Mods.Single(mod=>mod.Id==other.Id).Variants.Single().Sources.Single().Contains("file_id=20")&&
                File.ReadAllText(Safe.In(state.Destination,"mods/second/main.txt"))=="second",
                "Updating one selected item preserves every unselected mod and its source.");
            string stale=SourceUpdateZip(root,"stale.zip",new Dictionary<string,string>{{"mods/legacy/anchor.bin","legacy"}});
            form.Generic(state.Destination).Install(stale,updated.Key+"/0/"+UpdateFixtureOld,_=>{});
            File.WriteAllText(Safe.In(state.Destination,"mods/first/runtime.json"),"{}");
            File.WriteAllText(Safe.In(state.Destination,"mods/legacy/runtime.json"),"{}");
            await form.RememberModOutputBaseline(updated,state.Destination);
            var outputs=new ModOutputIndex(form.data,state.Destination).Load();
            check(outputs.Files.Any(file=>file.Owner==updated.Entry.Id&&file.Path=="mods/first/runtime.json")&&
                !outputs.Files.Any(file=>file.Owner==updated.Entry.Id&&file.Path.StartsWith("mods/legacy/",StringComparison.Ordinal)),
                "The output baseline uses the updated exact source slot and ignores stale receipts from the previous source.");
            reopen=form.library.Load();
        });
        TestForm(FixedData.Load(),reopen,Safe.In(root,"d"),root,form=>{
            check(form.state.AppliedPresetFingerprint==form.PresetFingerprint&&!form.NeedsPresetLoad,
                "Reopening the updated project keeps the applied preset identity and does not request a reload.");
            return Task.FromResult(0);
        });
    }

    static void SourceUpdateInterrupted(string root,bool wrongFile,bool committed,Action<bool,string> check,bool projectSaved=false) {
        string oldZip,newZip;var state=SourceUpdateState(root,out oldZip,out newZip);
        TestForm(FixedData.Load(),state,Safe.In(root,"d"),root,async form=>{
            var job=await SourceUpdateReady(form,root,oldZip);
            string manual=null;bool interrupted=false;
            if(wrongFile){
                manual=Safe.In(root,"wrong.zip");var bytes=File.ReadAllBytes(newZip);bytes[bytes.Length/2]^=0x01;File.WriteAllBytes(manual,bytes);
                File.Delete(newZip);
            }
            if(wrongFile||!committed) {
                string absentName="missing-"+Guid.NewGuid().ToString("N")+".zip";
                var package=await SourceUpdatePackage(UpdateFixtureNew,wrongFile?manual:newZip,absentName);
                // The wrong-file case must still model the digest of the valid
                // package, rather than having the fake server bless the bad one.
                if(wrongFile)package=await GeneralNexusUpdatePackage.Read(UpdateFixtureNew,"fixture-key",(Uri uri,string key,out HttpStatusCode status)=>{
                    status=HttpStatusCode.OK;return uri.AbsolutePath.Contains("/md5_search/")?"[]":
                        Safe.Json.Serialize(new{file_id=11,category_id=1,name="Updated fixture",file_name=absentName,version="2.0",size_in_bytes=new FileInfo(manual).Length});
                });
                form.testUpdatePackage=_=>Task.FromResult(package);
                form.launch=_=>{if(wrongFile)form.association=manual;else form.skipRequests.Add(job);};
            }else{
                var deployment=new GeneralPresetDeployment(state.Destination,form.data);
                var receipts=form.PresetReceipts(state.Destination).ToArray();
                deployment.RecordInstalled(form.PresetPackageFor(job,state.Destination,receipts,form.DisabledAt(state.Destination).Read()),true);
                deployment.AdoptReceived(form.state.SelectedPreset,form.PresetFingerprint);form.ReadPresetPresentation();
                form.testUpdatePackage=target=>SourceUpdatePackage(target,newZip);
                string checkpoint=projectSaved?"project-saved":"files-committed";
                form.testUpdateCheckpoint=phase=>{if(phase==checkpoint){interrupted=true;throw new IOException("Fixture crash at "+checkpoint+".");}};
            }
            await form.RunWork(job,state.Destination,false);
            if(committed)check(interrupted,"The interruption is injected at the requested "+(projectSaved?"project-saved":"files-committed")+" checkpoint.");
            check(form.state.List.Mods.Single().Variants.Single().Sources.Single()==(projectSaved?UpdateFixtureNew:UpdateFixtureOld),
                projectSaved?"A simulated close after project save already records the new source, while preset bookkeeping remains pending.":
                committed?"A simulated close between disk commit and project save leaves the old in-memory source for recovery.":
                wrongFile?"A manually associated package rejected by Nexus never changes the registered source.":"Cancelling while waiting for the browser preserves the registered source.");
            if(committed){
                check(File.ReadAllText(Safe.In(state.Destination,"mods/first/main.txt"))=="new","The interruption fixture occurs after the new files were durably committed.");
                form.testUpdateCheckpoint=null;
                form.ReadPresetPresentation();
                await form.RecoverSourceUpdates(state.Destination);
                var updated=form.MakeWork(form.state.List.Mods.Single());
                check(updated.Variant.Sources.Single()==UpdateFixtureNew&&form.Generic(state.Destination).Status(updated.Receipt(0))==GeneralModInstaller.Installed,
                    "Committed receipt metadata repairs the project's source after reopening, without reinstalling the package.");
                form.ReadPresetPresentation();
                check(form.state.AppliedPresetFingerprint==form.PresetFingerprint&&!form.NeedsPresetLoad,
                    projectSaved?"Recovery completes the applied preset even when the project source was already saved before the close.":
                    "Recovery completes both source metadata and the applied preset after a close at disk commit.");
                await form.Scan();await WaitForVersionCheck(form);
                check(form.statuses[updated.Key]=="Instalado"&&form.SelectedUpdates().Count==0,"The recovered source survives the next scan without a false failure or repeated update.");
            }else check(File.ReadAllText(Safe.In(state.Destination,"mods/first/main.txt"))=="old"&&
                File.Exists(Safe.In(state.Destination,"mods/first/obsolete.txt"))&&!File.Exists(Safe.In(state.Destination,"mods/first/added.txt"))&&
                !form.Generic(state.Destination).HasSnapshotRecord(job.Key+"/0/"+UpdateFixtureNew),
                wrongFile?"Wrong hash/identity preserves the old payload and never publishes a successful update receipt.":"Cancellation before download preserves every old file and never publishes an update receipt.");
        });
    }

    static void SourceUpdateCyberpunkSnapshot(string root,Action<bool,string> check) {
        Directory.CreateDirectory(root);string game=Directory.CreateDirectory(Safe.In(root,"g")).FullName;
        string data=Directory.CreateDirectory(Safe.In(root,"d")).FullName;
        string executable=Safe.In(game,"bin/x64/Cyberpunk2077.exe");Directory.CreateDirectory(Path.GetDirectoryName(executable));File.WriteAllText(executable,"fixture only");
        var catalog=new FixedCatalog{Schema=1,Id="fixture",GameVersion="2.5",GameFileVersion="2.5.0.0",Packages=new List<FixedMod>(),Defaults=new List<FixedDefault>()};
        string path="archive/pc/mod/source-update-fixture.archive";
        string zip=SourceUpdateZip(root,"new.zip",new Dictionary<string,string>{{path,"updated clothes"}});
        var entry=NewEntry("Updated clothes",UpdateFixtureNew.Replace("stardewvalley","cyberpunk2077"));entry.Id="clothing-fixture";
        var variant=entry.Variants.Single();var state=new GeneralModState{Destination=game,List=new GeneralModList{Mods=new List<GeneralModEntry>{entry}}};
        string slot=P2PPack.Slot(entry,variant,0);new GeneralModInstaller(game,Path.Combine(data,"Instalacoes")).Install(zip,slot,_=>{});
        check(!InstalledSnapshot.Uninstalled(state,entry,variant,catalog,game,data),"Cyberpunk updates with generic receipts are not omitted as never installed.");
        var manifest=InstalledSnapshot.Inspect(state,new[]{entry.Id},catalog,data,false,allowMissing:true);
        check(manifest.Files.Count==1&&manifest.Files.Single().Path==path&&manifest.Files.Single().Owners.Single()==entry.Id+"/default",
            "Cyberpunk P2P inspection reads the exact generic update receipt instead of demanding an imported-package record.");
        var reserved=Safe.Json.Deserialize<InstalledSnapshotManifest>(Safe.Json.Serialize(manifest));reserved.Schema=1;reserved.Project=null;
        catalog.Packages.Add(new FixedMod{Id="reserved-fixture",Files=new List<FixedFile>{new FixedFile{Path=path,Size=1,Sha256=new string('a',64)}}});
        bool refused=false;try{InstalledSnapshot.Validate(reserved,catalog);}catch(IOException ex){refused=ex.Message.Contains("destino reservado pelo catálogo oficial");}
        check(refused,"Updated generic packages still cannot bypass reserved framework paths in the P2P manifest.");
        File.WriteAllText(Safe.In(game,path),"manually changed");bool changed=false;
        try{InstalledSnapshot.Inspect(state,new[]{entry.Id},new FixedCatalog{Schema=1,Id="fixture",GameVersion="2.5",GameFileVersion="2.5.0.0",Packages=new List<FixedMod>(),Defaults=new List<FixedDefault>()},data,false);}
        catch(IOException){changed=true;}
        check(changed,"An altered updated file blocks export instead of falling back to stale receipt evidence.");
    }

    static FixedCatalog SourceUpdateCatalog() {
        return new FixedCatalog{Schema=1,Id="fixture",GameVersion="2.5",GameFileVersion="2.5.0.0",Packages=new List<FixedMod>(),Defaults=new List<FixedDefault>()};
    }

    static void SourceUpdateGameExecutable(string game) {
        var folder=new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);string fixture=null;
        for(int depth=0;folder!=null&&depth<6;depth++,folder=folder.Parent){
            string candidate=Path.Combine(folder.FullName,"Instalador","Online","FixtureGame.exe");
            if(File.Exists(candidate)){fixture=candidate;break;}
            candidate=Path.Combine(folder.FullName,"FixtureGame.exe");
            if(File.Exists(candidate)){fixture=candidate;break;}
        }
        if(fixture==null)throw new IOException("The compiled fixture game was not found inside the project.");
        string executable=Safe.In(game,"bin/x64/Cyberpunk2077.exe");Directory.CreateDirectory(Path.GetDirectoryName(executable));
        File.Copy(fixture,executable);File.WriteAllText(Safe.In(game,".fixture-only"),"test");
    }

    static void SourceUpdateForeign(string root,string predecessor,Action<bool,string> check) {
        bool plugin=predecessor.StartsWith("plugin-",StringComparison.Ordinal);
        bool fixedReceipt=plugin||predecessor.StartsWith("fixed",StringComparison.Ordinal);
        Directory.CreateDirectory(root);string game=Directory.CreateDirectory(Safe.In(root,"g")).FullName;
        SourceUpdateGameExecutable(game);string downloads=Directory.CreateDirectory(Safe.In(root,"downloads")).FullName;
        string first="archive/pc/mod/foreign-update.archive",obsolete="archive/pc/mod/foreign-obsolete.archive",added="archive/pc/mod/foreign-added.archive";
        var initialFiles=new Dictionary<string,string>{{first,"old foreign"},{obsolete,"obsolete foreign"}};
        var successor=new Dictionary<string,string>{{"Item codes.txt","Instructions for the player"},{first,"new foreign"},{added,"new addition"}};
        const string pluginBase="bin/x64/plugins/FixtureReporter";
        const string symbols=pluginBase+"/symbols/";
        string denied=null;
        if(plugin){
            initialFiles[pluginBase+".asi"]="old plugin";
            initialFiles[symbols+"old-id.bin"]="old module data";
            initialFiles[symbols+"old-license.txt"]="license";
            successor[pluginBase+".asi"]="new plugin";
            successor[symbols+"new-id.bin"]="new module data";
            successor[symbols+"versions.txt"]="new module versions";
            if(predecessor=="plugin-code")denied=symbols+"unexpected.dll";
            if(predecessor=="plugin-other")denied="bin/x64/plugins/Unrelated/symbols/new.bin";
            if(predecessor=="plugin-folder")denied=pluginBase+"/unknown/new.bin";
            if(denied!=null)successor[denied]="should not install";
        }
        string oldZip=SourceUpdateZip(root,"old.zip",initialFiles);
        string newZip=SourceUpdateZip(downloads,"fixture-"+Guid.NewGuid().ToString("N")+".zip",successor);
        string oldSource=UpdateFixtureOld.Replace("stardewvalley","cyberpunk2077"),newSource=UpdateFixtureNew.Replace("stardewvalley","cyberpunk2077");
        var catalog=SourceUpdateCatalog();var entry=NewEntry("Foreign receipt fixture",oldSource);entry.Id="foreign-fixture";
        var asset=new FixedAsset{Id="fixture-asset",Name="old.zip",Format="zip",Url=oldSource,ModId=42,FileId=10,Size=new FileInfo(oldZip).Length,Sha256=Safe.Hash(oldZip)};
        var recipe=new FixedMod{Id=entry.Id,Name=entry.Name,Version="1.0",Profile="Complementos",GameVersion="2.5",Dependencies=new string[0],Assets=new List<FixedAsset>{asset},
            Files=initialFiles.Select(pair=>new FixedFile{Asset=asset.Id,Entry=pair.Key,Path=pair.Key,Size=System.Text.Encoding.UTF8.GetByteCount(pair.Value),Sha256=InstalledSnapshot.Hash(System.Text.Encoding.UTF8.GetBytes(pair.Value))}).ToList()};
        if(fixedReceipt){catalog.Packages.Add(recipe);entry.Variants.Single().CyberpunkId=recipe.Id;}
        var state=new GeneralModState{Destination=game,DownloadFolder=downloads,Language="pt",Theme="dark",List=new GeneralModList{Mods=new List<GeneralModEntry>{entry}}};
        TestForm(catalog,state,Safe.In(root,"d"),root,async form=>{
            var oldWork=form.MakeWork(form.state.List.Mods.Single());string importedId=null,snapshotId=null;
            if(fixedReceipt){
                form.cache.Import(oldZip,asset);form.Cyberpunk(game).Install(new[]{recipe.Id},true,_=>{});
                check(SourceUpdateHasActiveFixedReceipt(form.Cyberpunk(game),recipe.Id),"The fixed predecessor has a real owned, committed installer receipt.");
            }else if(predecessor=="import"){
                var item=ImportItem(oldZip);form.Cyberpunk(game).InstallImported(item,_=>{});
                importedId=LocalModPackage.RoutedId(item.Sha256,oldWork.Variant.InstallRoutes);form.state.CyberpunkImports[oldWork.Receipt(0)]=importedId;
            }else{
                string sender=Directory.CreateDirectory(Safe.In(root,"sg")).FullName,senderData=Safe.In(root,"sd");
                SourceUpdateGameExecutable(sender);
                var senderState=Safe.Json.Deserialize<GeneralModState>(Safe.Json.Serialize(form.state));senderState.Destination=sender;
                new GeneralModInstaller(sender,Path.Combine(senderData,"Instalacoes")).Install(oldZip,oldWork.Receipt(0),_=>{});
                string pack=Safe.In(root,"received.zip");InstalledSnapshot.Create(pack,senderState,new[]{entry.Id},catalog,senderData,false,_=>{});
                snapshotId=form.Snapshot(game).Install(pack,Safe.Hash(pack),_=>{});form.state.SnapshotReceipts[oldWork.Key]=snapshotId;
            }
            form.testModUpdateSession=SourceUpdateSession();await form.Scan();await WaitForVersionCheck(form);
            var job=form.SelectedUpdates().Single();check(job.UpdateTarget==newSource,"A complete "+predecessor+" receipt can offer the exact Nexus successor.");
            if(fixedReceipt){
                recipe.Immutable=true;
                check(form.SourceUpdateTarget(form.MakeWork(form.state.List.Mods.Single()))==null,"An immutable catalog base never becomes an automatic source-update target.");
                recipe.Immutable=false;
            }
            form.testUpdatePackage=target=>SourceUpdatePackage(target,newZip);
            string failure=null;form.testFailurePrompt=message=>{failure=message;return false;};
            await form.RunWork(job,game,false);
            var updated=form.MakeWork(form.state.List.Mods.Single());
            if(denied!=null){
                check(failure!=null&&failure.Contains(denied)&&updated.Variant.Sources.Single()==oldSource&&
                    initialFiles.All(file=>File.ReadAllText(Safe.In(game,file.Key))==file.Value)&&!File.Exists(Safe.In(game,denied)),
                    "A new executable or unestablished plugin directory rejects the update without changing the previous installation: "+predecessor);
                return;
            }
            if(plugin){
                check(File.ReadAllText(Safe.In(game,symbols+"new-id.bin"))=="new module data"&&
                    File.ReadAllText(Safe.In(game,symbols+"versions.txt"))=="new module versions"&&
                    !File.Exists(Safe.In(game,symbols+"old-id.bin")),
                    "A verified plugin directory accepts new names for existing data types and retires the obsolete managed data.");
            }
            check(form.statuses[updated.Key]=="Instalado"&&updated.Variant.Sources.Single()==newSource&&
                File.ReadAllText(Safe.In(game,first))=="new foreign"&&!File.Exists(Safe.In(game,obsolete))&&File.Exists(Safe.In(game,added)),
                "A "+predecessor+" predecessor is replaced only after the verified successor is committed. Failure="+failure);
            check(!File.Exists(Safe.In(game,"Item codes.txt")),
                "A new root-level documentation file stays in the archive while Cyberpunk payload files update.");
            if(fixedReceipt)check(!SourceUpdateHasActiveFixedReceipt(form.Cyberpunk(game),recipe.Id),"The retired fixed receipt no longer claims the replaced files.");
            if(predecessor=="import")check(!form.Cyberpunk(game).HasSnapshotImportedRecord(importedId)&&!form.state.CyberpunkImports.Any(),"The old imported receipt is retired without deleting the new payload.");
            if(predecessor=="received")check(form.Snapshot(game).Status(snapshotId,oldWork.Key)==GeneralModInstaller.Missing&&form.state.SnapshotReceipts.Count==0,
                "The old received owner is retired without removing the updated files.");
            if(predecessor=="received") {
                var relocated=GeneralPresetProfiles.Copy(updated.Entry);
                relocated.Variants.Single().InstallRoutes=GeneralInstallRoutes.Parse(first+" = %GAME%/archive/pc/mod/relocated",game);
                bool? moved=await form.ChangeInstalledDestinations(updated.Entry,relocated);
                string newPath=GeneralInstallRoutes.Map(first,relocated.Variants.Single().InstallRoutes);
                var receipt=form.Generic(game).ReadUpdateRecord(updated.Receipt(0));
                check(moved==true&&File.Exists(Safe.In(game,newPath))&&!File.Exists(Safe.In(game,first))&&receipt!=null&&receipt.Upgrade.Reconciled,
                    "Changing an updated Cyberpunk mod's route moves its generic-owned files and preserves the update proof, including a new internal subfolder.");
                check(await form.ChangeInstalledDestinations(relocated,updated.Entry)==true&&File.Exists(Safe.In(game,first))&&!File.Exists(Safe.In(game,newPath)),
                    "The same verified route machinery can restore the package's original location.");
            }
            await form.Scan();await WaitForVersionCheck(form);
            check(form.statuses[updated.Key]=="Instalado"&&form.SelectedUpdates().Count==0,"The "+predecessor+" update survives the next scan with the new receipt.");
            if(predecessor=="fixed-reinstall"){
                var registered=form.Generic(game).ReadPackageRecord(updated.Receipt(0));
                foreach(var file in registered.Files)InstalledSnapshotInstaller.DeleteVerified(Safe.In(game,file.Path),file.Sha256);
                form.Cyberpunk(game).Install(new[]{recipe.Id},true,_=>{});
                check(SourceUpdateHasActiveFixedReceipt(form.Cyberpunk(game),recipe.Id),"The fixture independently reinstalls the old catalog after the successful update.");
                await form.Scan();await WaitForVersionCheck(form);
                check(SourceUpdateHasActiveFixedReceipt(form.Cyberpunk(game),recipe.Id),
                    "A subsequent scan never retires a different fixed receipt installed after the source update.");
                return;
            }
            string thirdZip=SourceUpdateZip(downloads,"fixture-"+Guid.NewGuid().ToString("N")+".zip",new Dictionary<string,string>{{first,"third foreign"}});
            string thirdSource=newSource.Replace("file_id=11","file_id=12");
            form.testModUpdateSession=new NexusUpdateSession("fixture-key",(GeneralNexusLink link,string key,out HttpStatusCode status)=>{
                status=HttpStatusCode.OK;return "{\"files\":[{\"file_id\":10,\"category_id\":4,\"version\":\"1.0\"},{\"file_id\":11,\"category_id\":4,\"version\":\"2.0\"},{\"file_id\":12,\"category_id\":1,\"version\":\"3.0\"}],\"file_updates\":[{\"old_file_id\":10,\"new_file_id\":11},{\"old_file_id\":11,\"new_file_id\":12}]}";
            });
            await form.Scan();await WaitForVersionCheck(form);var next=form.SelectedUpdates().Single();
            form.testUpdatePackage=target=>SourceUpdatePackage(target,thirdZip,null,"3.0");await form.RunWork(next,game,false);
            updated=form.MakeWork(form.state.List.Mods.Single());
            check(form.statuses[updated.Key]=="Instalado"&&updated.Variant.Sources.Single()==thirdSource&&File.ReadAllText(Safe.In(game,first))=="third foreign"&&!File.Exists(Safe.In(game,added)),
                "A second source update is not blocked by the retired "+predecessor+" predecessor. Failure="+failure);
            form.RemoveManagedWorks(new List<Work>{updated},game,new HashSet<string>());
            check(!File.Exists(Safe.In(game,first))&&File.Exists(Safe.In(game,"bin/x64/Cyberpunk2077.exe")),
                "Uninstall after the "+predecessor+" update removes the new owned payload and preserves the game.");
        });
    }

    static bool SourceUpdateHasActiveFixedReceipt(FixedInstaller installer,string id) {
        var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        var paths=(IEnumerable<string>)typeof(FixedInstaller).GetMethod("AllReceiptPaths",flags).Invoke(installer,new object[0]);
        var read=typeof(FixedInstaller).GetMethod("ReadReceipt",flags);
        return paths.Select(path=>(OnlineReceipt)read.Invoke(installer,new object[]{path})).Any(receipt=>
            receipt.Status=="instalado"&&receipt.Mods.Contains(id)&&receipt.Files.Any(file=>file.Applied&&!file.Reverted&&!file.UndoStarted));
    }

    static void SourceUpdateRecognitionOnly(string root,Action<bool,string> check) {
        string oldZip,newZip;var state=SourceUpdateState(root,out oldZip,out newZip);
        var entry=state.List.Mods.Single();string slot=P2PPack.Slot(entry,entry.Variants.Single(),0),data=Safe.In(root,"d");
        var owner=new GeneralModInstaller(state.Destination,Safe.In(root,"original-records"));owner.Install(oldZip,slot,_=>{});
        owner.DuplicateRecognition(Path.Combine(data,"Instalacoes"));
        TestForm(FixedData.Load(),state,data,root,async form=>{
            form.testModUpdateSession=SourceUpdateSession();await form.Scan();await WaitForVersionCheck(form);
            var job=form.MakeWork(form.state.List.Mods.Single());
            check(form.SelectedUpdates().Count==0&&form.SourceUpdateTarget(job)==null&&form.CountVersions().Review==1&&
                form.state.List.Mods.Single().Variants.Single().Sources.Single()==UpdateFixtureOld&&
                File.ReadAllText(Safe.In(state.Destination,"mods/first/main.txt"))=="old",
                "A recognition-only cloned receipt is sent to review before entering the update queue and cannot acquire ownership.");
            var composite=form.MakeWork(form.state.List.Mods.Single());composite.Variant.Sources.Add("https://example.test/extra.zip");
            check(form.SourceUpdateTarget(composite)==null,"Multi-source packages retain explicit review instead of automatic replacement.");
        });
    }

    static void SourceUpdateSharedManual(string root,Action<bool,string> check) {
        string oldZip,newZip;var state=SourceUpdateState(root,out oldZip,out newZip);
        var peer=NewEntry("Sharing fixture","https://example.test/shared.zip");peer.Id="shared-fixture";state.List.Mods.Add(peer);
        string sharedZip=SourceUpdateZip(root,"shared.zip",new Dictionary<string,string>{{"mods/first/main.txt","old"}});
        TestForm(FixedData.Load(),state,Safe.In(root,"d"),root,async form=>{
            var peerWork=form.MakeWork(form.state.List.Mods.Single(mod=>mod.Id==peer.Id));
            form.Generic(state.Destination).Install(sharedZip,peerWork.Receipt(0),_=>{});
            var job=form.MakeWork(form.state.List.Mods.Single(mod=>mod.Id=="update-fixture"));
            form.Generic(state.Destination).Install(oldZip,job.Receipt(0),_=>{});
            form.testModUpdateSession=SourceUpdateSession();await form.Scan();await WaitForVersionCheck(form);
            check(!form.SelectedUpdates().Any(work=>work.Entry.Id==job.Entry.Id)&&form.SourceUpdateTarget(job)==null&&
                File.ReadAllText(Safe.In(state.Destination,"mods/first/main.txt"))=="old"&&
                form.state.List.Mods.Single(mod=>mod.Id==job.Entry.Id).Variants.Single().Sources.Single()==UpdateFixtureOld,
                "The scan sends an update sharing paths with another mod to review before any download or overwrite.");
            File.WriteAllText(Safe.In(state.Destination,"mods/first/main.txt"),"manual edit");
            await form.Scan();await WaitForVersionCheck(form);
            check(!form.SelectedUpdates().Any(work=>work.Entry.Id==job.Entry.Id)&&File.ReadAllText(Safe.In(state.Destination,"mods/first/main.txt"))=="manual edit",
                "A manually edited installed package is not silently selected for automatic source replacement.");
        });
    }
}
