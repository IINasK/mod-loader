using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    internal static void RunProjectHubTests(string parent){
        string root=Path.Combine(parent,Regex.IsMatch(Path.GetFullPath(parent),@"(?i)[\\/]online-testes-[a-f0-9]{32}([\\/]|$)")?"j":"online-testes-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var checks=new List<string>();Action<bool,string> check=(ok,text)=>{if(!ok)throw new IOException(text);checks.Add(text);};
        const string exact="https://www.nexusmods.com/cyberpunk2077/mods/34305?tab=files&file_id=169285";
        var link=GeneralNexusLink.Parse(exact);check(link!=null&&link.File==169285&&link.Url==exact,"Exact Nexus file page is recognized without credentials.");
        check(GeneralNexusLink.Parse("https://www.nexusmods.com/games/cyberpunk2077/mods/34305?file_id=169285").Url==exact,"Modern Nexus route normalizes to the same public file page.");
        check(GeneralNexusLink.Parse("https://www.nexusmods.com/cyberpunk2077/mods/34305").File==0,"A mod overview is accepted and identified as non-specific.");
        foreach(string bad in new[]{"https://www.nexusmods.com.evil.test/cyberpunk2077/mods/34305","https://user@www.nexusmods.com/cyberpunk2077/mods/34305","https://www.nexusmods.com/cyberpunk2077/mods/34305?token=secret","https://www.nexusmods.com/cyberpunk2077/mods/34305?file_id=0","https://www.nexusmods.com/cyberpunk2077/mods/34305?file_id=1&file_id=2"})check(GeneralNexusLink.Parse(bad)==null,"Unsafe or ambiguous metadata URL is rejected: "+bad.Split('?')[0]);
        const string modJson=@"{""data"":{""game_scoped_id"":""34305"",""name"":""Radio fixture"",""adult_content"":true,""version"":""2.0"",""tags"":[{""name"":""Audio""},{""name"":""audio""},{""name"":""Gameplay""}]}}";
        const string fileJson=@"{""file_id"":169285,""file_name"":""SpotOnRadio.zip"",""version"":""1.5"",""size_kb"":4216}";
        const string requirementsJson=@"{""dependency_definitions"":[{""ranges"":[{""target_mod_file"":{""mod"":{""game_scoped_id"":""4591"",""name"":""RadioExt"",""game"":{""domain_name"":""cyberpunk2077""}}},""min_version"":{""game_scoped_id"":""168536"",""version"":""2.92""},""max_version"":null},{""target_mod_file"":{""mod"":{""game_scoped_id"":""9999"",""name"":""Alternate"",""game"":{""domain_name"":""cyberpunk2077""}}},""min_version"":{""game_scoped_id"":""6000""},""max_version"":{""game_scoped_id"":""6000""}}]}],""dlc_dependency_definitions"":[]}";
        var requirements=GeneralNexusInfo.ParseRequirements(requirementsJson);
        check(requirements.Count==1&&requirements[0].Length==2&&requirements[0][0].Source.Mod==4591&&
            requirements[0][0].MinVersion=="2.92"&&requirements[0][1].MaxFile==6000,
            "Nexus requirements keep OR alternatives and exact file bounds.");
        check(GeneralNexusInfo.ParseRequirements("{}")==null&&
            GeneralNexusInfo.ParseRequirements(@"{""dependency_definitions"":[]}").Count==0&&
            GeneralNexusInfo.ParseRequirements(requirementsJson.Replace("cyberpunk2077","evil/route"))==null,
            "An unavailable, empty or invalid requirements response stays distinguishable.");
        var overviewInfo=GeneralNexusInfo.ParseMod(modJson,link);
        check(overviewInfo.Name=="Radio fixture"&&overviewInfo.ModVersion=="2.0"&&overviewInfo.Version==null&&overviewInfo.Nsfw&&overviewInfo.Tags.SequenceEqual(new[]{"Audio","Gameplay"}),"Public Nexus metadata keeps the mod version, adult flag and distinct tags.");
        var info=GeneralNexusInfo.ParseMod(modJson,link);
        GeneralNexusInfo.ApplyFile(fileJson,link,info);
        check(info.FileName=="SpotOnRadio.zip"&&info.Version=="1.5"&&info.Bytes==4317184&&info.SizeApproximate,"Selected file metadata stays distinct from the mod version and labels the reported KiB as approximate.");
        GeneralNexusInfo.ApplyFile(fileJson.Replace("169285","169286"),link,overviewInfo);
        check(overviewInfo.Version==null&&overviewInfo.Bytes==0,"A different file ID cannot supply the selected file version or size.");
        GeneralNexusInfo.ApplyFile(fileJson.Replace("4216","9999999999999"),link,overviewInfo);
        check(overviewInfo.Bytes==0,"Implausible file size is ignored.");
        overviewInfo=GeneralNexusInfo.ParseMod(modJson,link);
        check(GeneralNexusInfo.ParseMod(modJson.Replace("34305","34306"),link)==null&&GeneralNexusInfo.ParseMod("<h1>Just a moment...</h1>",link)==null,"A different mod ID and non-JSON responses cannot supply metadata.");
        var catalog=new FixedCatalog{Schema=1,Id="hub-fixture",GameVersion="2.5",GameFileVersion="2.5.0.0",Packages=new List<FixedMod>()};
        string game=Path.Combine(root,"game");Directory.CreateDirectory(game);File.WriteAllText(Safe.In(game,".fixture-only"),"fixture");
        string zip=TestZip(root,"local.zip","mods/known.txt","known");
        var store=new GeneralProjectStore(Path.Combine(root,"p"));var cache=new DownloadCache(Path.Combine(root,"cache"));
        var entry=NewEntry("Shared",zip);entry.Variants[0].SourceInfo[zip]=new GeneralSourceInfo{Name="Shared",FileName="local.zip",Bytes=new FileInfo(zip).Length};
        var first=store.Create("First",new GeneralModList{Mods=new List<GeneralModEntry>{entry}},"light","pt",game);
        var second=store.Create("Second",new GeneralModList{Mods=new List<GeneralModEntry>{entry}},"light","pt",game);
        store.Create("Empty",new GeneralModList(),"light","pt");
        foreach(var project in new[]{first,second})new GeneralModInstaller(game,Path.Combine(store.DataPath(project),"Instalacoes")).Install(zip,entry.Id+"/"+entry.DefaultVariant+"/0/"+zip,null);
        File.WriteAllText(Safe.In(game,"untracked.txt"),new string('x',10000));Directory.CreateDirectory(cache.Root);File.WriteAllText(Safe.In(cache.Root,"cache.tmp"),"cache");
        string index=Safe.Hash(Path.Combine(store.Root,"projects.json"));int files=Directory.GetFiles(store.Root,"*",SearchOption.AllDirectories).Length;
        var all=GeneralProjectStorage.Measure(store,catalog,cache);long metadata=Directory.GetFiles(store.Root,"*",SearchOption.AllDirectories).Sum(path=>new FileInfo(path).Length);
        check(all.Mods==5&&all.Projects==3&&all.WithFiles==2,"Home counts projects with payloads and deduplicates a shared installed file.");
        check(all.Total==5+metadata+new FileInfo(zip).Length,"Total includes referenced download, local metadata and cache once, without unrelated game files.");
        var one=GeneralProjectStorage.Measure(store,catalog,cache,first);check(one.Mods==5&&one.Total==5+new FileInfo(zip).Length+Directory.GetFiles(store.DataPath(first),"*",SearchOption.AllDirectories).Sum(path=>new FileInfo(path).Length),"Selected project total excludes other project data and unrelated cache.");
        check(all.ModCount==2&&all.Identified==2&&one.ModCount==1&&one.Identified==1&&one.ModBytes[entry.Id]==5,"Measured counts and per-mod bytes use installed receipt ownership.");
        check(one.Cache==Directory.GetFiles(store.DataPath(first),"*",SearchOption.AllDirectories).Sum(path=>new FileInfo(path).Length)&&all.Cache==metadata,"Project cache includes support files; global cache counts shared data once.");
        check(index==Safe.Hash(Path.Combine(store.Root,"projects.json"))&&files==Directory.GetFiles(store.Root,"*",SearchOption.AllDirectories).Length,"Storage measurement does not modify receipts, project index or game files.");
        var reading=Task.Run(()=>{for(int i=0;i<25;i++)GeneralProjectStorage.Measure(store,catalog,cache);});
        for(int i=0;i<25;i++){var state=store.Load(first);state.Language=i%2==0?"en":"pt";new GeneralModLibrary(Path.Combine(store.DataPath(first),"lista.json")).Save(state);store.SavePreferences("light","pt",root);}
        reading.GetAwaiter().GetResult();check(store.Load(first).List.Mods.Count==1,"Background size reads never block concurrent project saves.");
        check(GeneralStorageSize.Format(1536).EndsWith("KiB"),"Size labels use explicit binary units.");
        string snapshot=Path.Combine(root,"snapshot.zip");InstalledSnapshot.Create(snapshot,store.Load(first),new[]{entry.Id},catalog,store.DataPath(first),false,null);
        using(var archive=System.IO.Compression.ZipFile.OpenRead(snapshot))using(var reader=new StreamReader(archive.GetEntry("manifest.json").Open()))check(!reader.ReadToEnd().Contains(zip.Replace("\\","\\\\")),"Installed sharing does not retain local metadata source paths.");
        TestForm(catalog,new GeneralModState{List=new GeneralModList(),Destination=game,DownloadFolder=root},Path.Combine(root,"ui"),root,async form=>{
            form.AttachProject(store,first);
            foreach(string locale in new[]{"pt","en"})foreach(string theme in new[]{"light","light-red","dark"}){
                form.state.Language=locale;form.state.Theme=theme;GeneralStyle.Language=locale;GeneralStyle.UseTheme(theme);form.BuildInterface();
                foreach(var dimensions in new[]{new Size(1000,680),new Size(1240,820)}){
                    form.ClientSize=dimensions;form.PerformLayout();await Task.Delay(40);
                    int textWidth=TextRenderer.MeasureText(form.projectsButton.Text,form.projectsButton.Font,Size.Empty,TextFormatFlags.SingleLine).Width;
                    CaptureUi(form,Path.Combine(root,"projects-button-"+locale+"-"+theme+"-"+dimensions.Width+".png"));
                    check(form.projectsButton.Width-52>=textWidth,"Projects navigation reserves the full label plus icon: "+locale+"/"+theme+"/"+dimensions.Width+" (button="+form.projectsButton.Width+", text="+textWidth+")");
                    check(form.projectsButton.Right<=form.homeButton.Left&&form.homeButton.Right<=form.optionsButton.Left,"Projects button stays separate from neighboring header actions: "+locale+"/"+theme+"/"+dimensions.Width);
                }
            }
            form.state.Language="pt";form.state.Theme="light";GeneralStyle.Language="pt";GeneralStyle.UseTheme("light");form.BuildInterface();form.ClientSize=new Size(1100,760);form.PerformLayout();
            check(form.projectsButton.Right<=form.homeButton.Left&&form.homeButton.Right<=form.optionsButton.Left,"Back button sits between Projects and the hamburger.");
            form.projectsButton.PerformClick();var panel=form.menus.Current as GeneralProjectsPanel;check(panel!=null&&panel.Visible,"Projects opens a compact Home panel.");
            var choices=AllControls(panel).OfType<ListBox>().Single(box=>box.Name=="ProjectChoices");check(choices.Items.Cast<string>().SequenceEqual(new[]{"Empty","Second","First"}),"Saved custom projects appear before the built-in project.");
            check(AllControls(panel).OfType<Button>().Any(button=>button.Name=="Projectnew")&&AllControls(panel).OfType<Button>().Any(button=>button.Name=="Projectimport"),"The compact panel offers create and import project.");
            await Task.Delay(60);check(AllControls(panel).OfType<Button>().Where(button=>button.Name=="Projectnew"||button.Name=="Projectimport").All(button=>button.Height==44&&button.Bottom<=button.Parent.Height),"Compact project actions fit their layout without clipping.");check(choices.ClientSize.Width>450,"Project choices use the panel width rather than a native control default.");CaptureUi(panel,Path.Combine(root,"projects-panel.png"));form.projectsButton.PerformClick();Application.DoEvents();check(form.menus.Current==null,"Clicking Projects a second time closes the panel.");
            using(var editor=new GeneralModEditor(NewEntry("",exact),new string[0],"default",true,"",source=>Task.FromResult(GeneralNexusLink.Parse(source).File>0?info:overviewInfo))){
                editor.Show(form);await Until(()=>EditorField<TextBox>(editor,"modName").Text==info.Name);
                string fileDetails=EditorField<Label>(editor,"sourceInfo").Text;
                check(fileDetails.Contains(GeneralStyle.T("Aprox. ","About ")+GeneralStorageSize.Format(info.Bytes))&&fileDetails.Contains(GeneralStyle.T("Versão do arquivo: ","File version: ")+"1.5")&&!fileDetails.Contains("2.0"),"The exact link displays the chosen version and indicates when file size is approximate.");
                var tags=EditorField<TextBox>(editor,"tags");
                var chips=EditorField<FlowLayoutPanel>(editor,"tagChips");
                check(EditorField<CheckBox>(editor,"nsfw").Checked&&chips.Controls.OfType<Button>().Count()==2,"Nexus adult status and tags populate the mod editor.");
                tags.Text="Custom tag";
                typeof(TextBox).GetMethod("OnKeyDown",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(tags,new object[]{new KeyEventArgs(Keys.Enter)});
                check(tags.Text==""&&editor.Result==null&&chips.Controls.OfType<Button>().Any(button=>button.Text=="Custom tag ×"),"Enter adds a removable tag without saving or closing the editor.");
                EditorField<TextBox>(editor,"modName").Text="My custom name";
                EditorField<TextBox>(editor,"sources").Text="https://www.nexusmods.com/cyberpunk2077/mods/34305";
                await Task.Delay(800);check(EditorField<TextBox>(editor,"modName").Text=="My custom name","Metadata refresh preserves a user-entered name.");
                check(EditorField<Label>(editor,"sourceInfo").Text.Contains("Slow download")&&EditorField<Button>(editor,"save").Enabled&&EditorField<Label>(editor,"sourceInfo").RectangleToScreen(EditorField<Label>(editor,"sourceInfo").ClientRectangle).Bottom<=editor.RectangleToScreen(editor.ClientRectangle).Bottom,"Overview page shows simple advice without blocking save.");
                editor.ClientSize=new Size(700,710);editor.PerformLayout();Application.DoEvents();await Task.Delay(40);check(EditorField<TextBox>(editor,"sources").Height>=EditorField<TextBox>(editor,"sources").Font.Height&&EditorField<Button>(editor,"save").RectangleToScreen(EditorField<Button>(editor,"save").ClientRectangle).Bottom<=editor.RectangleToScreen(editor.ClientRectangle).Bottom,"Source editor stays usable at minimum size with fixed action buttons and a scrollable body.");CaptureUi(editor,Path.Combine(root,"source-overview-warning.png"));
                chips.Controls.OfType<Button>().Single(button=>button.Text=="Audio ×").PerformClick();
                EditorField<Button>(editor,"save").PerformClick();var library=new GeneralModLibrary(Path.Combine(root,"saved.json"));library.Save(new GeneralModState{List=new GeneralModList{Mods=new List<GeneralModEntry>{editor.Result}}});
                var loaded=library.Load().List.Mods.Single();
                check(loaded.Name=="My custom name"&&loaded.Nsfw&&loaded.Tags.SequenceEqual(new[]{"Gameplay","Custom tag"})&&loaded.Variants[0].SourceInfo.Values.Single().ModVersion=="2.0","Metadata, custom name and edited tags survive save and reopen.");
                using(var reopened=new GeneralModEditor(loaded,new string[0],"default",true,"",source=>Task.FromResult(overviewInfo))){
                    reopened.Show(form);await Task.Delay(800);
                    check(!EditorField<FlowLayoutPanel>(reopened,"tagChips").Controls.OfType<Button>().Any(button=>button.Text=="Audio ×"),"A removed Nexus tag is not reapplied when reopening the saved item.");
                }
            }
            var pending=new TaskCompletionSource<GeneralSourceInfo>();using(var editor=new GeneralModEditor(NewEntry("",exact),new string[0],"default",true,"",source=>pending.Task)){
                editor.Show(form);await Task.Delay(800);EditorField<TextBox>(editor,"sources").Text="https://example.com/other.zip";pending.SetResult(info);await Task.Delay(40);
                check(EditorField<TextBox>(editor,"modName").Text==""&&!EditorField<CheckBox>(editor,"nsfw").Checked&&EditorField<FlowLayoutPanel>(editor,"tagChips").Controls.Count==0,"A late lookup cannot apply a name, adult flag or tags after the source changed.");editor.Close();
            }
            using(var editor=new GeneralModEditor(NewEntry("",exact),new string[0],"default",true,"",source=>Task.FromException<GeneralSourceInfo>(new IOException("Unavailable")))){
                editor.Show(form);await Task.Delay(800);EditorField<TextBox>(editor,"modName").Text="Manual";check(EditorField<Button>(editor,"save").Enabled,"Unavailable Nexus metadata leaves manual editing usable.");editor.Close();
            }
            await Task.Delay(30);
        });
        Safe.WriteJson(Path.Combine(root,"resultado-project-hub.json"),new{Passed=true,Tests=checks,RealGameTouched=false,NetworkUsed=false});
    }
    static T EditorField<T>(GeneralModEditor editor,string name)where T:class{return typeof(GeneralModEditor).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(editor) as T;}
}
