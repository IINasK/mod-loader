using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

internal static class GeneralPresetTests {
    internal static int Run(string parent) {
        string root=OnlineTests.NestedRoot(parent,"presets-");Directory.CreateDirectory(root);int passed=0;
        Action<bool,string> check=(ok,message)=>{if(!ok)throw new IOException(message);passed++;};
        var mod=new GeneralModEntry{Id="sample",Name="Sample",DefaultVariant="one",Tags=new List<string>{"original"},Variants=new List<GeneralModVariant>{
            new GeneralModVariant{Id="one",Name="One",Sources=new List<string>{"https://example.com/one.zip"}},new GeneralModVariant{Id="two",Name="Two",Sources=new List<string>{"https://example.com/two.zip"}}
        }};
        var state=new GeneralModState{SelectedPreset="a",List=new GeneralModList{Mods=new List<GeneralModEntry>{mod},Presets=new List<GeneralModPreset>{
            new GeneralModPreset{Id="a",Name="A",ModIds=new List<string>{mod.Id}},new GeneralModPreset{Id="b",Name="B",ModIds=new List<string>{mod.Id}}
        }}};
        state.ChosenVariants[mod.Id]="two";GeneralPresetProfiles.Initialize(state);
        check(state.List.Presets.All(p=>p.VariantChoices[mod.Id]=="two"),"Migration keeps the old selection in each existing preset.");
        var clone=GeneralPresetProfiles.Copy(state.List);
        clone.Presets[0].BodyChoice="natural";clone.Presets[0].VariantChoices[mod.Id]="one";
        var importedState=new GeneralModState{List=clone,SelectedPreset="a"};GeneralPresetProfiles.Project(importedState);
        check(importedState.BodyChoice=="natural"&&importedState.ChosenVariants[mod.Id]=="one","Imported choices survive initialization without legacy globals.");
        state.ChosenVariants[mod.Id]="one";GeneralPresetProfiles.Switch(state,"b");
        check(state.ChosenVariants[mod.Id]=="two","Changing preset restores its variant.");
        GeneralPresetProfiles.Switch(state,"a");check(state.ChosenVariants[mod.Id]=="one","Returning restores the earlier selection.");
        var original=state.List.Mods[0];var edited=GeneralPresetProfiles.Copy(original);edited.Name="Mine";edited.Tags.Add("local");
        GeneralPresetProfiles.Edit(state,original,edited,"one",false,new[]{"a","b"});GeneralPresetProfiles.Capture(state);
        GeneralPresetProfiles.Switch(state,"b");check(state.List.Mods[0].Name=="Sample"&&state.List.Mods[0].Tags.Count==1,"A local edit does not leak to another preset.");
        original=state.List.Mods[0];edited=GeneralPresetProfiles.Copy(original);edited.Description="Common description";
        GeneralPresetProfiles.Edit(state,original,edited,"two",true);GeneralPresetProfiles.Capture(state);
        GeneralPresetProfiles.Switch(state,"a");check(state.List.Mods[0].Name=="Mine"&&state.List.Mods[0].Description=="Common description"&&state.ChosenVariants[mod.Id]=="one","Editing all presets patches changed fields while keeping unrelated customizations.");
        var library=new GeneralModLibrary(Path.Combine(root,"state.json"));library.Save(state);state=library.Load();
        check(state.List.Mods[0].Name=="Mine"&&state.ChosenVariants[mod.Id]=="one","Preset choices and edits survive saving and reopening.");
        string exported=Path.Combine(root,"export.json");library.Export(exported,state.List);var imported=library.Import(exported);
        check(imported.Presets.Single(p=>p.Id=="a").Configurations[mod.Id].Name=="Mine"&&imported.Presets.Single(p=>p.Id=="b").VariantChoices[mod.Id]=="two","Export/import keeps every preset's independent configuration.");
        check(!File.ReadAllText(exported).Contains("CyberpunkId"),"Export omits internal recipe identities in preset configurations.");
        GeneralPresetProfiles.Remove(state,mod.Id,false);check(!state.List.Presets[0].ModIds.Contains(mod.Id)&&state.List.Presets[1].ModIds.Contains(mod.Id)&&state.List.Mods.Count==1,"Removing from one preset keeps the other reference and definition.");
        GeneralPresetProfiles.Remove(state,mod.Id,true);check(state.List.Mods.Count==0&&state.List.Presets.All(p=>p.Configurations.Count==0),"Removing from all presets removes metadata without touching files.");

        string game=Directory.CreateDirectory(Path.Combine(root,"game")).FullName,data=Path.Combine(root,"data");
        var first=Package(game,"first","v1","mods/a.txt","first");var second=Package(game,"second","v1","mods/b.txt","second");
        var engine=new GeneralPresetDeployment(game,data);var result=engine.Apply("a","one",new[]{first},new[]{first,second});
        check(File.Exists(Path.Combine(game,"mods/a.txt"))&&!File.Exists(Path.Combine(game,"mods/b.txt")),"Loading suspends only files absent from the desired preset.");
        check(result.Active.Count==1&&result.Issues.Count==0,"A complete load records its actual active set.");
        var stages=new List<Tuple<string,int,int>>();
        result=engine.Apply("b","two",new[]{second},new[]{first,second},
            (stage,done,total)=>stages.Add(Tuple.Create(stage,done,total)));
        int saving=stages.FindIndex(step=>step.Item1.Contains("desativados")&&step.Item3==1);
        int activating=stages.FindIndex(step=>step.Item1.Contains("second")&&step.Item3==1);
        check(saving>=0&&activating>saving,"Progress reports counted file storage before counted mod activation.");
        check(!File.Exists(Path.Combine(game,"mods/a.txt"))&&File.ReadAllText(Path.Combine(game,"mods/b.txt"))=="second","The next preset restores the stored variant without downloading.");
        long blobs=Directory.GetFiles(data,"*.zip",SearchOption.AllDirectories).Length;
        for(int i=0;i<5;i++){engine.Apply("a","one",new[]{first},new[]{first,second});engine.Apply("b","two",new[]{second},new[]{first,second});}
        check(Directory.GetFiles(data,"*.zip",SearchOption.AllDirectories).Length==blobs,"Repeated switching does not accumulate backup generations.");
        File.WriteAllText(Path.Combine(game,"mods/b.txt"),"user changed");
        result=engine.Apply("a","one",new[]{first},new[]{first,second});
        check(File.ReadAllText(Path.Combine(game,"mods/b.txt"))=="user changed"&&File.Exists(Path.Combine(game,"mods/a.txt")),"A modified mod is preserved while unrelated mods continue loading.");
        check(result.Protected.ContainsKey("second")&&result.Issues.Any(i=>i.Contains("second")),"Protection and an explanatory exception identify the affected mod.");
        engine=new GeneralPresetDeployment(game,data);check(engine.Read().Protected.ContainsKey("second"),"Protection survives restarting the manager.");
        File.WriteAllText(Path.Combine(game,"cache.db"),"unknown");engine.Apply("empty","none",new PresetPackage[0],new[]{first,second});
        check(File.ReadAllText(Path.Combine(game,"cache.db"))=="unknown","An unknown generic file is never attributed to a mod by its name.");
        var missing=new PresetPackage{Key="missing/v1",StorageKey="missing/v1",ModId="missing",Name="Missing"};
        result=engine.Apply("missing","three",new[]{missing},new[]{first,second});check(result.Issues.Any(i=>i.Contains("Missing")),"An unavailable mod becomes a pending exception instead of blocking the list.");

        string sharedGame=Directory.CreateDirectory(Path.Combine(root,"shared-game")).FullName,sharedData=Path.Combine(root,"shared-data");
        var sharedA=Package(sharedGame,"alpha","one","shared/file.txt","shared");var sharedB=GeneralPresetProfiles.Copy(sharedA);sharedB.ModId="beta";sharedB.Name="Beta";sharedB.Key=sharedB.StorageKey="beta/one";
        var sharedEngine=new GeneralPresetDeployment(sharedGame,sharedData);sharedEngine.Apply("both","a",new[]{sharedA,sharedB},new[]{sharedA,sharedB});
        sharedEngine.Apply("b","b",new[]{sharedB},new[]{sharedA,sharedB});
        check(File.ReadAllText(Path.Combine(sharedGame,"shared/file.txt"))=="shared","Suspending one owner keeps a shared file used by another active mod.");
        sharedEngine.Apply("a","a",new[]{sharedA},new[]{sharedA,sharedB});check(File.Exists(Path.Combine(sharedGame,"shared/file.txt")),"Restoring another owner preserves the shared content.");

        string crashGame=Directory.CreateDirectory(Path.Combine(root,"crash-game")).FullName,crashData=Path.Combine(root,"crash-data");
        var old=Package(crashGame,"old","one","old.bin","old");var next=Package(crashGame,"new","one","new.bin","new");
        var crash=new GeneralPresetDeployment(crashGame,crashData);crash.Apply("old","old",new[]{old},new[]{old,next});
        string journalCopy=null;
        crash.Checkpoint=phase=>{if(phase=="disabled"){journalCopy=File.ReadAllText(Directory.GetFiles(crashData,"pending.json",SearchOption.AllDirectories).Single());throw new IOException("Injected interruption");}};
        bool failed=false;try{crash.Apply("new","new",new[]{next},new[]{old,next});}catch(IOException){failed=true;}
        check(failed&&File.ReadAllText(Path.Combine(crashGame,"old.bin"))=="old"&&!File.Exists(Path.Combine(crashGame,"new.bin")),"A failure after suspension restores the earlier preset.");
        // Reproduce exactly the durable state left by a stopped process after suspension.
        var off=new DisabledMods(crashGame,crashData);off.Disable(new Dictionary<string,List<DisabledModFile>>{{old.StorageKey,old.Files}},true);
        string pending=Path.Combine(Directory.GetDirectories(Path.Combine(crashData,"Presets")).Single(),"pending.json");File.WriteAllText(pending,journalCopy);
        crash=new GeneralPresetDeployment(crashGame,crashData);crash.Recover();
        check(File.ReadAllText(Path.Combine(crashGame,"old.bin"))=="old"&&!crash.RecoveryPending,"A fresh instance recovers the journal left by an interrupted operation.");
        // Re-create an abrupt stop after restoring a file but before removing the cached recipe.
        crash.Checkpoint=phase=>{if(phase=="restoring"){journalCopy=File.ReadAllText(pending);throw new IOException("Interrupted restore");}};
        failed=false;try{crash.Apply("new","new",new[]{next},new[]{old,next});}catch(IOException){failed=true;}
        check(failed&&File.Exists(Path.Combine(crashGame,"old.bin"))&&!File.Exists(Path.Combine(crashGame,"new.bin")),"Failed restore rolls back.");
        off.Disable(new Dictionary<string,List<DisabledModFile>>{{old.StorageKey,old.Files}},true);
        File.WriteAllText(Path.Combine(crashGame,"new.bin"),"new");
        File.WriteAllText(pending,journalCopy);
        crash=new GeneralPresetDeployment(crashGame,crashData);crash.Recover();
        check(File.Exists(Path.Combine(crashGame,"old.bin"))&&!File.Exists(Path.Combine(crashGame,"new.bin"))&&off.Read().ContainsKey(next.StorageKey),"Recovery removes only partially restored bytes and retains the cached version.");
        File.WriteAllText(Path.Combine(crashGame,"new.bin"),"new");
        var collision=crash.Apply("new","new",new[]{next},new[]{old,next});
        check(File.Exists(Path.Combine(crashGame,"new.bin"))&&collision.Issues.Count>0&&!collision.Active.Any(p=>p.Key==next.Key),"Identical bytes at an unknown destination are preserved without adopting ownership.");
        string variantsGame=Directory.CreateDirectory(Path.Combine(root,"variant-game")).FullName,variantsData=Path.Combine(root,"variant-data");
        var version1=Package(variantsGame,"variant","one","mesh.txt","version-one");var version2=Package(variantsGame,"variant","two","mesh.txt","version-two");
        var variantStore=new DisabledMods(variantsGame,variantsData);variantStore.Disable(new Dictionary<string,List<DisabledModFile>>{{version2.StorageKey,version2.Files}},true);
        File.WriteAllText(Path.Combine(variantsGame,"mesh.txt"),"version-one");
        var versionEngine=new GeneralPresetDeployment(variantsGame,variantsData);versionEngine.Apply("one","one",new[]{version1},new[]{version1,version2});
        versionEngine.Apply("two","two",new[]{version2},new[]{version1,version2});
        check(File.ReadAllText(Path.Combine(variantsGame,"mesh.txt"))=="version-two","Versions using the same destination are switched without overwriting an unrecorded file.");
        versionEngine.FreeBytes=path=>0;bool noSpace=false;
        try{versionEngine.Apply("one","one",new[]{version1},new[]{version1,version2});}catch(IOException){noSpace=true;}
        check(noSpace&&File.ReadAllText(Path.Combine(variantsGame,"mesh.txt"))=="version-two"&&!versionEngine.RecoveryPending,"Insufficient space is detected before moving any files.");
        versionEngine.FreeBytes=path=>Int64.MaxValue;
        File.WriteAllText(Path.Combine(variantsGame,"mesh.txt"),"manual-tuning");
        var guarded=versionEngine.Apply("one","one",new[]{version1},new[]{version1,version2});
        check(File.ReadAllText(Path.Combine(variantsGame,"mesh.txt"))=="manual-tuning"&&guarded.Issues.Count>0,"An edited version also blocks another version of the same mod.");
        versionEngine.ReleaseProtection("variant");
        check(!versionEngine.Read().Protected.ContainsKey("variant"),"Only explicit resumption adopts the current known files as a new baseline.");
        versionEngine.Apply("one","one",new[]{version1},new[]{version1,version2});
        check(File.ReadAllText(Path.Combine(variantsGame,"mesh.txt"))=="version-one","After explicit resumption the manager can switch versions and preserve the edited copy.");
        string dependencyGame=Directory.CreateDirectory(Path.Combine(root,"dependency-game")).FullName,dependencyData=Path.Combine(root,"dependency-data");
        var dependency=Package(dependencyGame,"dependency","one","base.txt","base");var dependent=Package(dependencyGame,"dependent","one","addon.txt","addon");dependent.RequiredMods.Add("dependency");
        var dependencyEngine=new GeneralPresetDeployment(dependencyGame,dependencyData);dependencyEngine.Apply("full","full",new[]{dependency,dependent},new[]{dependency,dependent});
        var replacement=Package(dependencyGame,"dependency","two","collision.txt","incoming");
        new DisabledMods(dependencyGame,dependencyData).Disable(new Dictionary<string,List<DisabledModFile>>{{replacement.StorageKey,replacement.Files}},true);
        File.WriteAllText(Path.Combine(dependencyGame,"collision.txt"),"unknown");
        var refused=dependencyEngine.Apply("new","new",new[]{replacement,dependent},new[]{dependency,dependent,replacement});
        check(File.Exists(Path.Combine(dependencyGame,"base.txt"))&&refused.Issues.Count>0,"A failed dependency replacement retains the old dependency of a mod that stays active.");
        File.WriteAllText(Path.Combine(dependencyGame,"addon.txt"),"edited-addon");dependencyEngine.Apply("empty","empty",new PresetPackage[0],new[]{dependency,dependent});
        check(File.Exists(Path.Combine(dependencyGame,"base.txt")),"Declared dependencies of a protected mod are retained too.");
        using(var locked=new FileStream(Path.Combine(dependencyGame,"base.txt"),FileMode.Open,FileAccess.Read,FileShare.None)) {
            bool blocked=false;try{dependencyEngine.Apply("empty","again",new PresetPackage[0],new[]{dependency,dependent});}catch(IOException){blocked=true;}
            check(blocked,"Locked files stop the unsafe transition before their content can be removed.");
        }
        check(File.ReadAllText(Path.Combine(dependencyGame,"base.txt"))=="base","A locked-file failure preserves the original data.");
        string paths=Directory.CreateDirectory(Path.Combine(root,"user-files")).FullName;
        Directory.CreateDirectory(Path.Combine(paths,"ExampleGame"));GeneralInstallRoutes.FixtureAppData=paths;
        try {
            string relative=GeneralInstallRoutes.Map("addon.ini",new List<GeneralInstallRoute>{new GeneralInstallRoute{Folder="",Destination="%APPDATA%/ExampleGame"}});
            string target=GeneralInstallRoutes.Resolve(dependencyGame,relative,true);Directory.CreateDirectory(Path.GetDirectoryName(target));File.WriteAllText(target,"user-addon");
            var external=new PresetPackage{Key="external/one",StorageKey="external/one",ModId="external",Name="External",Files=new List<DisabledModFile>{new DisabledModFile{Path=relative,Hash=Safe.Hash(target),Size=new FileInfo(target).Length}}};
            dependencyEngine.Apply("external","external",new[]{external},new[]{dependency,dependent,external});
            dependencyEngine.Apply("none","none",new PresetPackage[0],new[]{dependency,dependent,external});
            check(!File.Exists(target),"A proven file in a portable user destination can be suspended separately.");
            dependencyEngine.Apply("external","external",new[]{external},new[]{dependency,dependent,external});
            check(File.ReadAllText(target)=="user-addon","A file in the user destination is restored using the receiver's resolved path.");
            Directory.CreateDirectory(Path.Combine(paths,"ExampleGame","shared"));
            string routeA=GeneralInstallRoutes.Map("shared/file.ini",new List<GeneralInstallRoute>{new GeneralInstallRoute{Folder="",Destination="%APPDATA%/ExampleGame"}});
            string routeB=GeneralInstallRoutes.Map("file.ini",new List<GeneralInstallRoute>{new GeneralInstallRoute{Folder="",Destination="%APPDATA%/ExampleGame/shared"}});
            string sharedTarget=GeneralInstallRoutes.Resolve(dependencyGame,routeA,true);File.WriteAllText(sharedTarget,"alias");
            var aliasA=new PresetPackage{Key="route-a/one",StorageKey="route-a/one",ModId="route-a",Name="Route A",Files=new List<DisabledModFile>{new DisabledModFile{Path=routeA,Hash=Safe.Hash(sharedTarget),Size=5}}};
            var aliasB=GeneralPresetProfiles.Copy(aliasA);aliasB.Key=aliasB.StorageKey="route-b/one";aliasB.ModId="route-b";aliasB.Files[0].Path=routeB;
            dependencyEngine.Apply("both","both",new[]{aliasA,aliasB},new[]{aliasA,aliasB});
            dependencyEngine.Apply("only-b","b",new[]{aliasB},new[]{aliasA,aliasB});
            check(routeA!=routeB&&File.ReadAllText(sharedTarget)=="alias","Equivalent portable routes keep their shared physical file and save a recoverable copy.");
        }finally{GeneralInstallRoutes.FixtureAppData=null;}
        Safe.WriteJson(Path.Combine(root,"result.json"),new{Passed=true,Checks=passed});return passed;
    }
    internal static string MakeZip(string root,string name,Dictionary<string,string> files) {
        string path=Path.Combine(root,name);
        using(var zip=System.IO.Compression.ZipFile.Open(path,System.IO.Compression.ZipArchiveMode.Create))foreach(var file in files)
            using(var output=new StreamWriter(zip.CreateEntry(file.Key).Open()))output.Write(file.Value);
        return path;
    }
    static PresetPackage Package(string root,string mod,string version,string path,string text) {
        string target=Safe.In(root,path);Directory.CreateDirectory(Path.GetDirectoryName(target));File.WriteAllText(target,text);
        return new PresetPackage{ModId=mod,Name=mod,Key=mod+"/"+version,StorageKey=mod+"/"+version,Files=new List<DisabledModFile>{new DisabledModFile{Path=path,Size=new FileInfo(target).Length,Hash=Safe.Hash(target)}}};
    }
}

internal sealed partial class GeneralModWindow {
    internal static void RunPresetLoadingTests(string parent) {
        string root=OnlineTests.NestedRoot(parent,"preset-ui-");Directory.CreateDirectory(root);
        string game=Directory.CreateDirectory(Path.Combine(root,"game")).FullName,data=Path.Combine(root,"state");
        File.WriteAllText(Path.Combine(game,".fixture-only"),"isolated test");
        string archiveA=GeneralPresetTests.MakeZip(root,"a.zip",new Dictionary<string,string>{{"mods/a.txt","a"}});
        string archiveB=GeneralPresetTests.MakeZip(root,"b.zip",new Dictionary<string,string>{{"mods/b.txt","b"}});
        var a=NewEntry("First",archiveA);var b=NewEntry("Second",archiveB);a.AlternativeGroup=b.AlternativeGroup="variants";
        var state=new GeneralModState{Destination=game,DownloadFolder=root,SelectedPreset="a",List=new GeneralModList{
            Mods=new List<GeneralModEntry>{a,b},AlternativeGroups=new Dictionary<string,GeneralAlternativeGroup>{{"variants",new GeneralAlternativeGroup{Name="Variants",Color="#EA9E13"}}},Presets=new List<GeneralModPreset>{
                new GeneralModPreset{Id="a",Name="Preset A",ModIds=new List<string>{a.Id}},
                new GeneralModPreset{Id="b",Name="Preset B",ModIds=new List<string>{a.Id,b.Id},AlternativeChoices=new Dictionary<string,string>{{"variants",b.Id}}}
            }
        }};
        int passed=0;Action<bool,string> check=(ok,message)=>{if(!ok)throw new IOException(message);passed++;};
        TestForm(FixedData.Load(),state,data,root,async form=>{
            form.testPresetEditScope=()=>false;
            form.Generic(game).Install(archiveA,new Work{Entry=a,Variant=a.Variants[0]}.Receipt(0),null);
            form.Generic(game).Install(archiveB,new Work{Entry=b,Variant=b.Variants[0]}.Receipt(0),null);
            await form.Scan();form.RefreshPrimaryAction();
            check(form.primaryAction.Text=="Carregar preset"&&form.NeedsPresetLoad,"Opening an unapplied preset offers Load preset instead of download.");
            check(form.preset.PopupAnchor==form.tableName,"The popup is anchored to the full preset name field.");
            var screen=Screen.PrimaryScreen.WorkingArea;
            var placements=new List<object>();
            foreach(int width in new[]{1080,1240}) {
                form.SetBounds(screen.Left+32,screen.Top+32,Math.Min(width,screen.Width-64),Math.Min(800,screen.Height-64));
                Application.DoEvents();form.preset.Toggle();Application.DoEvents();
                var field=form.tableName.RectangleToScreen(form.tableName.ClientRectangle);var popup=form.preset.OpenPopup.Bounds;
                placements.Add(new{Window=form.Bounds,Field=field,Popup=popup});
                check(popup.Top>=field.Bottom&&Math.Abs(popup.Left-field.Left)<=2&&popup.Width>=field.Width,"The popup follows the full preset field. Field: "+field+"; popup: "+popup);
                form.preset.Toggle();
            }
            Safe.WriteJson(Path.Combine(root,"preset-popup.json"),placements);
            var loading=form.LoadPreset();
            check(form.currentProgress.Caption.Contains("preset")&&form.currentProgress.Indeterminate,
                "The footer takes over immediately while the preset is being checked.");
            await loading;
            check(form.currentProgress.Caption.StartsWith("Preset carregado")&&form.currentProgress.Value==100&&!form.currentProgress.Indeterminate,
                "The footer keeps the result after the list refresh.");
            check(File.Exists(Path.Combine(game,"mods/a.txt"))&&!File.Exists(Path.Combine(game,"mods/b.txt")),"The actual window loads the selected preset using generic installation receipts.");
            check(!form.NeedsPresetLoad&&form.state.AppliedPreset=="a","After loading, normal mod actions become available.");
            form.preset.SelectedIndex=2;
            check(form.PresetWorks().Single().Entry.Id==b.Id,"The selected preset chooses exactly one alternative in a group.");check(form.NeedsPresetLoad&&File.Exists(Path.Combine(game,"mods/a.txt")),"Browsing another preset changes the button without moving files.");
            await form.LoadPreset();
            check(!File.Exists(Path.Combine(game,"mods/a.txt"))&&File.Exists(Path.Combine(game,"mods/b.txt")),"Loading another preset restores its compressed mod.");
            form.preset.SelectedIndex=1;await form.LoadPreset();
            check(File.Exists(Path.Combine(game,"mods/a.txt"))&&!File.Exists(Path.Combine(game,"mods/b.txt")),"Returning to the previous preset restores its files.");
            using(var editor=new GeneralModEditor(a,new string[0],a.DefaultVariant,false,"")) {
                editor.ConfigurePresets(form.state.List.Presets,"a",a.Id,true);
                check(editor.PresetIds.SequenceEqual(new[]{"a","b"}),"The editor shows membership in multiple presets.");
                editor.Show(form);Application.DoEvents();
                using(var timer=new Timer{Interval=60}) {
                    timer.Tick+=(s,e)=>{
                        var popup=Application.OpenForms.OfType<GeneralDialog>().FirstOrDefault(d=>d.Text=="Presets deste mod");if(popup==null)return;
                        timer.Stop();((CheckBox)popup.Controls.Find("PresetMembership-b",true).Single()).Checked=false;popup.AcceptButton.PerformClick();
                    };
                    timer.Start();((Button)editor.Controls.Find("ChooseModPresets",true).Single()).PerformClick();
                }
                check(editor.PresetIds.SequenceEqual(new[]{"a"}),"The membership menu lets the user uncheck only one preset.");editor.Close();
            }
            var manifest=InstalledSnapshot.Inspect(form.state,new[]{a.Id},FixedData.Load(),data,false);
            var other=manifest.Project.Presets.Single(p=>p.Id=="b");
            check(other.AlternativeChoices["variants"]==b.Id&&other.Configurations[b.Id].Variants[0].Sources.All(GeneralModDownload.IsInstalledSource),"P2P metadata keeps another preset's choices without exposing a local download path.");
            Safe.WriteJson(Path.Combine(root,"ui-result.json"),new{Passed=true,Checks=passed});
        });
    }
}
