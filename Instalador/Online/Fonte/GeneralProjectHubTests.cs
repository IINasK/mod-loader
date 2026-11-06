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
        string html="<h1>Spot On Radio &amp; music</h1><mod-file-download filename=\"SpotOnRadio\" filesize-bytes=\"4317184\" file-id=\"169285\">";
        var info=GeneralNexusInfo.Parse(html,link);check(info.Name=="Spot On Radio & music"&&info.FileName=="SpotOnRadio"&&info.Bytes==4317184,"Public title, published file label and exact byte count are read together.");
        check(GeneralNexusInfo.Parse(html.Replace("169285","169286"),link).Bytes==0,"A different file ID cannot supply the selected file size.");
        check(GeneralNexusInfo.Parse(html.Replace("4317184","9999999999999"),link).Bytes==0,"Implausible metadata size is ignored.");
        check(GeneralNexusInfo.Parse("<h1>Just a moment...</h1>",link)==null,"Cloudflare challenge is not treated as a mod name.");
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
            form.AttachProject(store,first);form.ClientSize=new Size(1100,760);form.PerformLayout();
            check(form.projectsButton.Right<=form.homeButton.Left&&form.homeButton.Right<=form.optionsButton.Left,"Back button sits between Projects and the hamburger.");
            form.projectsButton.PerformClick();var panel=form.menus.Current as GeneralProjectsPanel;check(panel!=null&&panel.Visible,"Projects opens a compact Home panel.");
            var choices=AllControls(panel).OfType<ListBox>().Single(box=>box.Name=="ProjectChoices");check(choices.Items.Cast<string>().SequenceEqual(new[]{"Empty","Second","First"}),"Saved custom projects appear before the built-in project.");
            check(AllControls(panel).OfType<Button>().Any(button=>button.Name=="Projectnew")&&AllControls(panel).OfType<Button>().Any(button=>button.Name=="Projectimport"),"The compact panel offers create and import project.");
            await Task.Delay(60);check(AllControls(panel).OfType<Button>().Where(button=>button.Name=="Projectnew"||button.Name=="Projectimport").All(button=>button.Height==44&&button.Bottom<=button.Parent.Height),"Compact project actions fit their layout without clipping.");check(choices.ClientSize.Width>450,"Project choices use the panel width rather than a native control default.");CaptureUi(panel,Path.Combine(root,"projects-panel.png"));form.projectsButton.PerformClick();Application.DoEvents();check(form.menus.Current==null,"Clicking Projects a second time closes the panel.");
            using(var editor=new GeneralModEditor(NewEntry("",exact),new string[0],"default",true,"",source=>Task.FromResult(info))){
                editor.Show(form);await Until(()=>EditorField<TextBox>(editor,"modName").Text==info.Name);
                check(EditorField<Label>(editor,"sourceInfo").Text.Contains(info.Bytes.ToString("N0")),"The editor displays the downloaded file byte count.");
                EditorField<TextBox>(editor,"modName").Text="My custom name";
                EditorField<TextBox>(editor,"sources").Text="https://www.nexusmods.com/cyberpunk2077/mods/34305";
                await Task.Delay(800);check(EditorField<TextBox>(editor,"modName").Text=="My custom name","Metadata refresh preserves a user-entered name.");
                check(EditorField<Label>(editor,"sourceInfo").Text.Contains("Slow download")&&EditorField<Button>(editor,"save").Enabled&&EditorField<Label>(editor,"sourceInfo").RectangleToScreen(EditorField<Label>(editor,"sourceInfo").ClientRectangle).Bottom<=editor.RectangleToScreen(editor.ClientRectangle).Bottom,"Overview page shows simple advice without blocking save.");
                editor.ClientSize=new Size(700,710);editor.PerformLayout();Application.DoEvents();await Task.Delay(40);check(EditorField<TextBox>(editor,"sources").Height>=80&&EditorField<Button>(editor,"save").RectangleToScreen(EditorField<Button>(editor,"save").ClientRectangle).Bottom<=editor.RectangleToScreen(editor.ClientRectangle).Bottom,"Source editor stays usable at minimum size with fixed action buttons and a scrollable body.");CaptureUi(editor,Path.Combine(root,"source-overview-warning.png"));
                EditorField<Button>(editor,"save").PerformClick();var library=new GeneralModLibrary(Path.Combine(root,"saved.json"));library.Save(new GeneralModState{List=new GeneralModList{Mods=new List<GeneralModEntry>{editor.Result}}});
                var loaded=library.Load().List.Mods.Single();check(loaded.Name=="My custom name"&&loaded.Variants[0].SourceInfo.Count==1,"Metadata and custom name survive save and reopen.");
            }
            var pending=new TaskCompletionSource<GeneralSourceInfo>();using(var editor=new GeneralModEditor(NewEntry("",exact),new string[0],"default",true,"",source=>pending.Task)){
                editor.Show(form);await Task.Delay(800);EditorField<TextBox>(editor,"sources").Text="https://example.com/other.zip";pending.SetResult(info);await Task.Delay(40);
                check(EditorField<TextBox>(editor,"modName").Text=="","A late lookup cannot fill a name after the source changed.");editor.Close();
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
