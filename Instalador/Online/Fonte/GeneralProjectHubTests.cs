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
        check(GeneralStorageSize.Format(999)=="999 B"&&GeneralStorageSize.Format(1000)=="1 KB"&&
            GeneralStorageSize.Format(1000000)=="1 MB"&&GeneralStorageSize.Format(1000000000)=="1 GB",
            "Size labels use powers of 1000 at each unit boundary.");
        check(GeneralStorageSize.Format(1536)==1.54m.ToString()+" KB","Size labels round decimal units using the current locale.");
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
                var fileLabel=EditorField<Label>(editor,"fileDetails");
                string fileDetails=fileLabel.Text;
                var install=EditorField<Button>(editor,"install");
                check(fileLabel.Parent==install.Parent.Parent&&fileLabel.Left<install.Parent.Left&&
                    !fileDetails.Contains("bytes")&&!fileDetails.Contains(".zip"),"Only version and human-readable size appear to the left of fixed actions.");
                check(fileDetails.Contains(GeneralStyle.T("Aprox. ","About ")+GeneralStorageSize.Format(info.Bytes))&&fileDetails.Contains(GeneralStyle.T("Versão do arquivo: ","File version: ")+"1.5")&&!fileDetails.Contains("2.0"),"The exact link displays the chosen version and indicates when file size is approximate.");
                var tags=EditorField<TextBox>(editor,"tags");
                var chips=EditorField<FlowLayoutPanel>(editor,"tagChips");
                var addTag=EditorField<Button>(editor,"addTagButton");
                var tagButtons=chips.Controls.OfType<Button>().ToArray();
                check(EditorField<GeneralButton>(editor,"nsfw").BareInk==GeneralStyle.Danger&&tagButtons.Length==3&&tagButtons.Last()==addTag&&!tags.Visible,
                    "Nexus tags precede the Add capsule and the editor stays hidden at rest.");
                addTag.PerformClick();
                check(tags.Visible&&!addTag.Visible,"The tag editor replaces the Add capsule.");
                tags.Text="Canceled";
                typeof(TextBox).GetMethod("OnKeyDown",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(tags,new object[]{new KeyEventArgs(Keys.Escape)});
                check(editor.Visible&&addTag.Visible&&!tags.Visible&&!chips.Controls.OfType<Button>().Any(button=>button.Text=="Canceled ×"),
                    "Escape cancels only the inline entry.");
                addTag.PerformClick();
                tags.Text="Custom tag";
                typeof(TextBox).GetMethod("OnKeyDown",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(tags,new object[]{new KeyEventArgs(Keys.Enter)});
                check(tags.Text==""&&!tags.Visible&&editor.Result==null&&chips.Controls.OfType<Button>().Reverse().Take(2).Select(button=>button.Text).SequenceEqual(new[]{addTag.Text,"Custom tag ×"}),
                    "Enter adds the tag and moves Add to the next position.");
                EditorField<TextBox>(editor,"modName").Text="My custom name";
                EditorField<TextBox>(editor,"sources").Text="https://www.nexusmods.com/cyberpunk2077/mods/34305";
                await Task.Delay(800);check(EditorField<TextBox>(editor,"modName").Text=="My custom name","Metadata refresh preserves a user-entered name.");
                check(EditorField<Label>(editor,"sourceInfo").Text.Contains("Slow download")&&EditorField<Button>(editor,"save").Enabled&&EditorField<Label>(editor,"sourceInfo").RectangleToScreen(EditorField<Label>(editor,"sourceInfo").ClientRectangle).Bottom<=editor.RectangleToScreen(editor.ClientRectangle).Bottom,"Overview page shows simple advice without blocking save.");
                editor.ClientSize=new Size(700,710);editor.PerformLayout();Application.DoEvents();await Task.Delay(40);
                CaptureUi(editor,Path.Combine(root,"source-overview-warning.png"));
                var sourceBox=EditorField<TextBox>(editor,"sources");
                var saveButton=EditorField<Button>(editor,"save");
                check(sourceBox.Height>=sourceBox.Font.Height&&
                    saveButton.RectangleToScreen(saveButton.ClientRectangle).Bottom<=editor.RectangleToScreen(editor.ClientRectangle).Bottom,
                    "Source and actions stay usable at narrow width. Source="+sourceBox.Bounds+
                    ", save="+saveButton.RectangleToScreen(saveButton.ClientRectangle)+", editor="+editor.RectangleToScreen(editor.ClientRectangle));
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
                check(EditorField<TextBox>(editor,"modName").Text==""&&EditorField<GeneralButton>(editor,"nsfw").BareInk==GeneralStyle.Line&&EditorField<FlowLayoutPanel>(editor,"tagChips").Controls.OfType<Button>().Count()==1,"A late lookup cannot apply a name, adult flag or tags after the source changed.");editor.Close();
            }
            using(var editor=new GeneralModEditor(NewEntry("",exact),new string[0],"default",true,"",source=>Task.FromException<GeneralSourceInfo>(new IOException("Unavailable")))){
                editor.Show(form);await Task.Delay(800);EditorField<TextBox>(editor,"modName").Text="Manual";check(EditorField<Button>(editor,"save").Enabled,"Unavailable Nexus metadata leaves manual editing usable.");editor.Close();
            }
            await TestCompactModEditor(form,root,check,exact);
            await TestNexusQuickFill(form,check,exact);
            await Task.Delay(30);
        });
        Safe.WriteJson(Path.Combine(root,"resultado-project-hub.json"),new{Passed=true,Tests=checks,RealGameTouched=false,NetworkUsed=false});
    }
    static async Task TestNexusQuickFill(Form owner,Action<bool,string> check,string source){
        var mod=NewEntry("Nome próprio",source);
        mod.Description="Descrição própria";
        mod.Tags.Add("Minha tag");
        mod.NsfwOverride=true;
        int requests=0;
        using(var editor=new GeneralModEditor(mod,new string[0],"default",true,"",refreshLookup:url=>{
            check(url==source,"O preenchimento rápido consulta o link da variante atual.");
            return Task.FromResult(new GeneralSourceInfo{
                Name="Nome do Nexus",Tags=new[]{"Nova tag"},NsfwKnown=true,Nsfw=false,Version=(++requests).ToString()
            });
        })){
            editor.Show(owner);
            editor.ClientSize=new Size(630,710);
            var quick=EditorField<Button>(editor,"quickFill");
            var link=EditorField<TextBox>(editor,"sources");
            var name=EditorField<TextBox>(editor,"modName");
            var chips=EditorField<FlowLayoutPanel>(editor,"tagChips");
            check(quick.Visible&&quick.Parent.Controls.OfType<Button>().Any(button=>button.Text=="Cancelar"&&
                quick.Right<button.Left&&quick.Top==button.Top),"Preenchimento rápido fica no rodapé ao lado de Cancelar.");
            link.Text="";
            check(!quick.Visible,"Sem link Nexus, o botão do rodapé fica oculto.");
            link.Text=source;
            using(var choose=new Timer{Interval=30}){
                string choice="Completar";
                choose.Tick+=(s,e)=>{
                    var dialog=Application.OpenForms.OfType<GeneralDialog>().FirstOrDefault(form=>form.Text==quick.Text);
                    if(dialog==null)return;
                    AllControls(dialog).OfType<Button>().Single(button=>button.Text==choice).PerformClick();
                };
                choose.Start();
                quick.PerformClick();
                await Until(()=>requests==1&&quick.Enabled);
                check(name.Text=="Nome próprio"&&chips.Controls.OfType<Button>().Any(button=>button.Text=="Minha tag ×")&&
                    chips.Controls.OfType<Button>().Any(button=>button.Text=="Nova tag ×")&&EditorField<GeneralButton>(editor,"nsfw").BareInk==GeneralStyle.Danger,
                    "Completar mantém nome, tags próprias e escolha manual de NSFW, acrescentando tags novas.");
                choice="Sobrescrever";
                quick.PerformClick();
                await Until(()=>requests==2&&quick.Enabled);
                check(name.Text=="Nome do Nexus"&&!chips.Controls.OfType<Button>().Any(button=>button.Text=="Minha tag ×")&&
                    EditorField<GeneralButton>(editor,"nsfw").BareInk==GeneralStyle.Line&&EditorField<TextBox>(editor,"description").Text=="Descrição própria",
                    "Sobrescrever refaz a consulta e troca apenas os dados recebidos do Nexus.");
                choose.Stop();
            }
        }
    }
    static async Task TestCompactModEditor(Form owner,string root,Action<bool,string> check,string source){
        var mod=NewEntry("Editor compacto",source);
        mod.Tags.AddRange(new[]{"Cyberpunk Modathon 2026","Gameplay","Overhaul","Quality of Life","Terrain"});
        mod.Variants[0].Name="Original";
        var preferred=new GeneralModVariant{Id="second",Name="Preferida",Sources=new List<string>{source.Replace("169285","169286")}};
        var third=new GeneralModVariant{Id="third",Name="Terceira",Sources=new List<string>{source.Replace("169285","169287")}};
        mod.Variants.AddRange(new[]{preferred,third});
        mod.DefaultVariant=preferred.Id;
        bool animations=GeneralStyle.AnimationsEnabled;
        GeneralStyle.AnimationsEnabled=true;
        GeneralStyle.UseTheme("black");
        try {
            using(var editor=new GeneralModEditor(mod,new[]{"Roupas"},preferred.Id,true,"")){
                editor.Show(owner);editor.ClientSize=new Size(630,710);editor.PerformLayout();await Task.Delay(80);
                var field=EditorField<TextBox>(editor,"sources");
                var add=EditorField<Button>(editor,"addTagButton");
                var picker=EditorField<GeneralPicker>(editor,"variants");
                var input=picker.Controls.OfType<TextBox>().Single();
                var terrain=EditorField<FlowLayoutPanel>(editor,"tagChips").Controls.OfType<Button>().Single(button=>button.Text=="Terrain ×");
                check(terrain.Top==add.Top&&terrain.Right<add.Left&&terrain.Height==add.Height&&add.Right<=add.Parent.ClientSize.Width,
                    "At narrow width Add follows the final tag with the same height and no clipping: "+terrain.Bounds+" / "+add.Bounds);
                check(input.RectangleToScreen(input.ClientRectangle).Top<field.RectangleToScreen(field.ClientRectangle).Top&&
                    !AllControls(editor).OfType<Button>().Any(button=>new[]{"Adicionar link","Remover fonte","Adicionar arquivo local"}.Contains(button.Text)),
                    "The source selector precedes the Nexus link and the legacy source actions are gone.");
                CaptureUi(editor,Path.Combine(root,"compact-editor-tags.png"));
                add.PerformClick();await Task.Delay(40);
                var inline=EditorField<TextBox>(editor,"tags");
                check(inline.Visible&&inline.Parent.Bounds.Height==terrain.Height&&inline.Parent.Top==terrain.Top&&
                    inline.Parent.Left>terrain.Right&&inline.Parent.Width>=80,"The inline input is sized and positioned after the last tag: "+inline.Parent.Bounds);
                inline.Text="Tag de exemplo";
                CaptureUi(editor,Path.Combine(root,"compact-editor-tag-input.png"));
                typeof(TextBox).GetMethod("OnKeyDown",BindingFlags.Instance|BindingFlags.NonPublic)
                    .Invoke(inline,new object[]{new KeyEventArgs(Keys.Escape)});

                check(input.Visible&&input.Text=="Preferida"&&picker.Items.Cast<string>().SequenceEqual(new[]{"Preferida","Original","Terceira"}),
                    "A legacy default moves first and remains the selected editable name.");
                var routes=EditorField<TextBox>(editor,"installRoutes");
                var browse=AllControls(editor).OfType<Button>().Single(button=>button.Text=="Escolher pasta");
                var routeBounds=routes.Parent.RectangleToScreen(routes.Parent.ClientRectangle);
                var buttonBounds=browse.RectangleToScreen(browse.ClientRectangle);
                var destination=(TableLayoutPanel)browse.Parent;
                check(!routes.Multiline&&!routes.ReadOnly&&browse.Enabled&&routeBounds.Top==buttonBounds.Top&&
                    routeBounds.Bottom==buttonBounds.Bottom&&routeBounds.Right<buttonBounds.Left&&
                                        destination.GetControlFromPosition(0,0).ForeColor==GeneralStyle.Foreground&&
                    destination.GetControlFromPosition(2,1).Top==browse.Top&&
                    destination.GetControlFromPosition(2,1).Height==browse.Height,
                    "The editable destination and Explorer button share a themed, single aligned row.");
                var name=EditorField<TextBox>(editor,"modName");
                var nsfw=EditorField<GeneralButton>(editor,"nsfw");
                check(nsfw.Parent==name.Parent&&nsfw.Left>=name.Right&&nsfw.BareInk==GeneralStyle.Line,
                    "The inactive adult marker is gray and sits in the name field.");
                check(Math.Abs((name.Top+name.Height/2)-(nsfw.Top+nsfw.Height/2))<=1&&
                    name.Bottom<=name.Parent.ClientSize.Height&&nsfw.Bottom<=nsfw.Parent.ClientSize.Height,
                    "The editable name and NSFW share a vertical center and fit their row: "+name.Bounds+" / "+nsfw.Bounds);
                nsfw.PerformClick();
                check(nsfw.BareInk==GeneralStyle.Danger,"Clicking the adult marker turns it on.");
                nsfw.PerformClick();
                check(nsfw.BareInk==GeneralStyle.Line,"Clicking again restores the gray marker.");
                var description=EditorField<TextBox>(editor,"description");
                var grip=AllControls(editor).Single(control=>control.Name=="DescriptionResizeGrip");
                var descriptionView=(GeneralScrollView)description.Parent.Parent;
                description.Text=String.Join(Environment.NewLine,Enumerable.Repeat("Long description",24));
                descriptionView.RefreshScroll();
                int height=grip.Parent.Height;
                typeof(Control).GetMethod("OnKeyDown",BindingFlags.Instance|BindingFlags.NonPublic)
                    .Invoke(grip,new object[]{new KeyEventArgs(Keys.Down)});
                check(grip.Bottom<=grip.Parent.ClientSize.Height-8&&grip.Right<=grip.Parent.ClientSize.Width-8&&
                    descriptionView.Bottom<=grip.Top&&grip.BackColor==GeneralStyle.Surface,
                    "The resize grip stays inside the themed surface with its own space below the description.");
                check(descriptionView.Bar.Limit>0&&grip.Parent.Height>height,
                    "Description keeps its own scrollbar and grows on command from its resize grip.");
                CaptureUi(editor,Path.Combine(root,"editor-themed-controls.png"));
                input.Text="Preferida editada";
                picker.Toggle();
                var popup=picker.OpenPopup;
                var view=(GeneralScrollView)popup.Items.OfType<ToolStripControlHost>().Single().Control;
                var list=(ListBox)view.Content;
                check(list.Items.Cast<string>().SequenceEqual(new[]{"Preferida editada (padrão)","Original","Terceira","Adicionar fonte"}),
                    "The version popup shows the default first and Add last.");
                check(!view.Bar.Visible||view.Bar.Left>=list.Width,"The scrollbar cannot cover the trash hit area.");
                using(var bitmap=new Bitmap(popup.Width,popup.Height)){
                    popup.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));
                    bitmap.Save(Path.Combine(root,"compact-editor-versions.png"));
                }
                Point top=new Point(65,list.GetItemRectangle(0).Top+20);
                Point bottom=new Point(65,list.GetItemRectangle(2).Top+20);
                VersionMouse(list,"OnMouseDown",bottom);
                VersionMouse(list,"OnMouseMove",top);
                VersionMouse(list,"OnMouseUp",top);
                check(picker.Items.Cast<string>().SequenceEqual(new[]{"Preferida editada","Original","Terceira"}),
                    "Dragging does not change the order of download versions.");
                Point trash=new Point(list.ClientSize.Width-20,list.GetItemRectangle(2).Top+20);
                VersionMouse(list,"OnMouseClick",trash);
                check(!popup.Visible,"Delete closes its popup before the editor refreshes.");
                await Until(()=>picker.Items.Count==2);
                check(picker.Items.Cast<string>().SequenceEqual(new[]{"Preferida editada","Original"}),
                    "The row trash removes only that version.");
                picker.Toggle();popup=picker.OpenPopup;
                list=AllControls(popup.Items.OfType<ToolStripControlHost>().Single().Control).OfType<ListBox>().Single();
                Point addVersion=new Point(65,list.GetItemRectangle(list.Items.Count-1).Top+20);
                VersionMouse(list,"OnMouseClick",addVersion);
                check(!popup.Visible&&picker.Items.Count==2,"Add closes the dropdown synchronously and defers the editor update.");
                await Until(()=>picker.Items.Count==3);
                check(input.Text=="Nova versão"&&input.SelectionLength==input.Text.Length,"Add opens a new version with its name selected for editing.");
                input.Text="Quarta";
                field.Text=source.Replace("/34305?","/4591?");
                check(!EditorField<Button>(editor,"save").Enabled,"A file from another Nexus mod is rejected.");
                field.Text="https://example.com/mod.zip";
                check(!EditorField<Button>(editor,"save").Enabled,"A new external link is rejected.");
                field.Text=source.Replace("169285","169288");
                check(EditorField<Button>(editor,"save").Enabled,"A file version of the same Nexus mod is allowed.");
                picker.SelectedIndex=0;
                check(input.Text=="Preferida editada"&&field.Text==preferred.Sources[0],
                    "Selecting another version keeps both inline names and both URLs intact.");
                EditorField<Button>(editor,"save").PerformClick();
                check(editor.Result.DefaultVariant==preferred.Id&&editor.SelectedVariant==preferred.Id&&editor.Result.NsfwOverride==false&&
                    editor.Result.Variants.Select(version=>version.Name).SequenceEqual(new[]{"Preferida editada","Original","Quarta"}),
                    "The first version is saved as default and an explicitly selected version stays selected.");
            }
            string[] archivePaths=new[]{"plugins/one.dll","plugins/data/two.txt"}
                .Concat(Enumerable.Range(0,30).Select(index=>"plugins/file"+index+".txt")).ToArray();
            using(var destinations=new GeneralArchiveDestinations(archivePaths,new GeneralInstallRoute[0],root,true)){
                destinations.Show(owner);
                var tree=(ListBox)destinations.Controls.Find("ArchiveFileTree",true).Single();
                var nodes=tree.Items.Cast<TreeNode>().ToDictionary(node=>(string)node.Tag);
                check(nodes["plugins/data"].Parent==nodes["plugins"]&&nodes["plugins/data/two.txt"].Parent==nodes["plugins/data"],
                    "The archive keeps its folder hierarchy and relative paths for routing.");
                var view=(GeneralScrollView)tree.Parent.Parent;
                view.RefreshScroll();
                check(view.Bar.Visible&&view.Bar.Limit>0&&!tree.HorizontalScrollbar,"The archive tree uses the styled vertical scrollbar.");

                var current=(TextBox)destinations.Controls.Find("ArchiveCurrentDestination",true).Single();
                var target=(TextBox)destinations.Controls.Find("ArchiveNewDestination",true).Single();
                check(current.ReadOnly&&current.BorderStyle==BorderStyle.None&&current.Text=="%GAME%"&&!target.ReadOnly,"The root shows its current location and a separate editable destination.");
                target.Text=Path.Combine(root,"relocated");
                tree.SelectedItem=nodes["plugins/data"];
                check(current.Text=="%GAME%/plugins/data"&&target.Text.Length==0,"A folder can be routed without changing its expected location.");
                target.Text="%GAME%/Data";
                tree.SelectedItem=nodes["plugins/data/two.txt"];
                check(current.Text=="%GAME%/plugins/data/two.txt","Selecting a file shows its full current location.");
                target.Text="%GAME%/Temporary";
                AllControls(destinations).OfType<Button>().Single(button=>button.Text=="Resetar").PerformClick();
                check(target.Text.Length==0,"Reset removes only the selected override.");
                var data=new DataObject();data.SetData(typeof(TreeNode),nodes["plugins/one.dll"]);
                typeof(Control).GetMethod("OnDragDrop",BindingFlags.Instance|BindingFlags.NonPublic)
                    .Invoke(target,new object[]{new DragEventArgs(data,0,0,0,DragDropEffects.Move,DragDropEffects.Move)});
                check(tree.SelectedItem==nodes["plugins/one.dll"]&&target.Text=="%GAME%","Dragging a file to the destination field assigns a portable override.");
                check(destinations.Routes.Count==0,"Route drafts do not modify the result before Apply.");
                CaptureUi(destinations,Path.Combine(root,"editor-archive-destinations.png"));
                AllControls(destinations).OfType<Button>().Single(button=>button.Text=="Aplicar").PerformClick();
                check(destinations.DialogResult==DialogResult.OK&&destinations.Routes.Count==3&&
                    destinations.Routes.Any(route=>route.Folder==""&&route.Destination=="%GAME%/relocated")&&
                    destinations.Routes.Any(route=>route.Folder=="plugins/data"&&route.Destination=="%GAME%/Data")&&
                    destinations.Routes.Any(route=>route.Folder=="plugins/one.dll"&&route.Destination=="%GAME%"),
                    "Apply keeps edits to the root, folder and file together.");
                destinations.Close();
            }
            using(var emptyTree=new GeneralArchiveDestinations(new string[0],new GeneralInstallRoute[0],root,true)){
                emptyTree.Show(owner);
                var tree=(ListBox)emptyTree.Controls.Find("ArchiveFileTree",true).Single();
                check(tree.Items.Count==1&&AllControls(emptyTree).OfType<Button>()
                    .Any(button=>button.Text=="Escolher pasta"&&button.Enabled&&button.Visible),
                    "No Nexus preview still leaves a usable destination picker for the entire archive.");
                var target=(TextBox)emptyTree.Controls.Find("ArchiveNewDestination",true).Single();
                target.Text="%GAME%/Draft";
                AllControls(emptyTree).OfType<Button>().Single(button=>button.Text=="Cancelar").PerformClick();
                check(emptyTree.DialogResult==DialogResult.Cancel&&emptyTree.Routes.Count==0,"Cancel leaves the original routes intact.");
                emptyTree.Close();
            }
            var fixedMod=NewEntry("Destino do catálogo",source);
            fixedMod.Variants[0].CyberpunkId="fixed-fixture";
            using(var fixedEditor=new GeneralModEditor(fixedMod,new string[0],"default",true,"")){
                var browse=EditorField<Button>(fixedEditor,"browseDestination");
                var routes=EditorField<TextBox>(fixedEditor,"installRoutes");
                var label=((TableLayoutPanel)browse.Parent).GetControlFromPosition(0,0);
                check(browse.Enabled&&!routes.ReadOnly&&routes.Text==""&&label.Enabled&&label.ForeColor==GeneralStyle.Foreground,
                    "A catalog destination can be edited and its dark-theme label stays legible.");
            }
            using(var editor=new GeneralModEditor(mod,new string[0],preferred.Id,true,"")){
                editor.Show(owner);
                var category=EditorField<GeneralPicker>(editor,"category");
                category.Toggle();
                var popup=category.OpenPopup;
                var list=AllControls(popup.Items.OfType<ToolStripControlHost>().Single().Control).OfType<ListBox>().Single();
                check(list.Items.Cast<string>().SequenceEqual(new[]{"Criar categoria"}),"An empty category list still offers Create category.");
                category.Toggle();
                for(int i=0;i<16;i++)category.Items.Add("Categoria "+(i+1));
                category.Toggle();
                popup=category.OpenPopup;
                var view=(GeneralScrollView)popup.Items.OfType<ToolStripControlHost>().Single().Control;
                list=(ListBox)view.Content;
                check(list.Items.Count==17&&(string)list.Items[16]=="Criar categoria","Create category follows the final category in a scrollable list.");
                view.ScrollTo(view.Bar.Limit);
                using(var timer=new Timer{Interval=30}){
                    timer.Tick+=(s,e)=>{
                        var dialog=Application.OpenForms.OfType<GeneralCategoryDetails>().FirstOrDefault();
                        if(dialog==null)return;
                        timer.Stop();
                        var fields=AllControls(dialog).OfType<TextBox>().ToArray();
                        var categoryName=fields.Single(box=>!box.Multiline);
                        categoryName.Text="categoria 1";
                        check(!((Button)dialog.AcceptButton).Enabled,"Duplicate category names are rejected case-insensitively.");
                        categoryName.Text="Nova categoria";
                        fields.Single(box=>box.Multiline).Text="Descrição da categoria";
                        ((Button)dialog.AcceptButton).PerformClick();
                    };
                    timer.Start();
                    VersionMouse(list,"OnMouseClick",new Point(65,list.GetItemRectangle(16).Top+20));
                    check(!popup.Visible,"Create category closes its menu before opening the form.");
                    await Until(()=>category.Text=="Nova categoria");
                }
                EditorField<Button>(editor,"save").PerformClick();
                check(editor.Result.Category=="Nova categoria"&&editor.NewCategories["Nova categoria"]=="Descrição da categoria",
                    "The new category and its description are staged with the mod.");
                var formOwner=(GeneralModWindow)owner;
                check(!formOwner.state.List.Categories.Contains("Nova categoria"),"Creating a category in the editor does not mutate the project before Apply.");
                formOwner.ApplyEdited(mod,editor.Result,editor.SelectedVariant,false,editor.NewCategories);
                check(formOwner.state.List.Categories.Contains("Nova categoria")&&
                    formOwner.state.List.CategoryDescriptions["Nova categoria"]=="Descrição da categoria",
                    "Applying the mod saves the newly created category and its description.");
                formOwner.state.List.Mods.Remove(editor.Result);
            }
        } finally { GeneralStyle.AnimationsEnabled=animations;GeneralStyle.UseTheme("light"); }
        var form=(GeneralModWindow)owner;
        form.state.List.Categories.AddRange(new[]{"Roupas","Lojas"});
        form.state.List.CategoryDescriptions["Roupas"]="Conjuntos";
        var first=NewEntry("Primeiro",source);form.state.List.Mods.Add(first);
        first.Category="Roupas";
        mod.Category="Roupas";form.state.List.Mods.Add(mod);
        var preset=new GeneralModPreset{Id="roupas",Name="Roupas"};
        preset.Categories.Add("Roupas");form.state.List.Presets.Add(preset);
        var edited=NewEntry("Editado",source);edited.Category="Vestuário";
        form.ApplyCategoryName("Roupas",edited);
        check(first.Category=="Vestuário"&&mod.Category=="Vestuário"&&preset.Categories.Single()=="Vestuário"&&
            form.state.List.CategoryDescriptions["Vestuário"]=="Conjuntos","Renaming a category updates every mod, preset and description.");
        edited.Category="Lojas";form.ApplyCategoryName("Vestuário",edited);
        check(first.Category=="Vestuário"&&form.state.List.Categories.Contains("Lojas"),"Choosing an existing category does not rename other items.");
    }

    static void VersionMouse(ListBox list,string method,Point point){
        list.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)
            .Invoke(list,new object[]{new MouseEventArgs(MouseButtons.Left,1,point.X,point.Y,0)});
    }

    static T EditorField<T>(GeneralModEditor editor,string name)where T:class{return typeof(GeneralModEditor).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(editor) as T;}
}
