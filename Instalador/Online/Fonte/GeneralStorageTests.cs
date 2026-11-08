using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    internal static void RunStorageTests(string parent){
        string root=System.Text.RegularExpressions.Regex.IsMatch(Path.GetFullPath(parent).Replace(Path.DirectorySeparatorChar,'/'),@"(?i)/online-testes-[a-f0-9]{32}(/|$)")?Path.Combine(parent,"s"):OnlineTests.NestedRoot(parent,"sizes");Directory.CreateDirectory(root);
        var tests=new List<string>();Action<bool,string> check=(ok,message)=>{if(!ok)throw new IOException(message);tests.Add(message);};
        var catalog=new FixedCatalog{Schema=1,Id="sizes",GameVersion="2.5",GameFileVersion="2.5.0.0",Packages=new List<FixedMod>()};
        var store=new GeneralProjectStore(Path.Combine(root,"p"));var cache=new DownloadCache(Path.Combine(root,"cache"));string game=Path.Combine(root,"game");Directory.CreateDirectory(game);File.WriteAllText(Safe.In(game,".fixture-only"),"fixture");
        string source=TestZip(root,"shared.zip","mods/shared.txt","share"),zero=TestZip(root,"zero.zip","mods/empty.txt","");
        var first=NewEntry("Primeiro compartilhado",source);var second=NewEntry("Segundo compartilhado",source);var empty=NewEntry("Arquivo vazio",zero);var absent=NewEntry("Ainda não instalado","https://example.test/uninstalled.zip");
        absent.Variants[0].SourceInfo[absent.Variants[0].Sources[0]]=new GeneralSourceInfo{Name="Expected",FileName="uninstalled.zip",Bytes=99999999};
        var list=new GeneralModList{Mods=new List<GeneralModEntry>{first,second,empty,absent}};list.Presets.Add(new GeneralModPreset{Id="subset",Name="Dois itens",ModIds=new List<string>{first.Id,absent.Id}});
        var project=store.Create("Projeto de espaço",list,"light","pt",game);string data=store.DataPath(project);var engine=new GeneralModInstaller(game,Path.Combine(data,"Instalacoes"));
        foreach(var mod in new[]{first,second,empty})engine.Install(mod.Variants[0].Sources[0],mod.Id+"/default/0/"+mod.Variants[0].Sources[0],null);
        string unknown=Safe.In(game,"untracked.txt");File.WriteAllText(unknown,new string('x',9000));var size=GeneralProjectStorage.Measure(store,catalog,cache,project);
        check(size.ModCount==4&&size.Identified==3&&size.ModBytes.Count==3,"Measured ratio counts files on disk, not selected boxes or published download sizes.");
        check(size.ModBytes[first.Id]==5&&size.ModBytes[second.Id]==5&&size.ModBytes[empty.Id]==0&&!size.ModBytes.ContainsKey(absent.Id),"Shared files appear for each owner; zero bytes is identified and a missing mod stays unknown.");
        check(size.Mods==5&&size.ModBytes.Values.Sum()==10,"Project installed total deduplicates a shared path even when per-mod rows both show it.");
        long support=Directory.GetFiles(data,"*",SearchOption.AllDirectories).Sum(path=>new FileInfo(path).Length);
        check(size.Cache==support&&size.Total==size.Mods+support+new FileInfo(source).Length+new FileInfo(zero).Length,"Project cache includes support files; original Downloads archives stay a separate part of total.");
        check(size.Total>=size.Mods&&size.Total>=size.Cache,"The same metadata snapshot keeps totals consistent.");
        string received=Path.Combine(root,"received");Directory.CreateDirectory(received);File.WriteAllText(Safe.In(received,".fixture-only"),"fixture");
        string package=Path.Combine(root,"pack.zip");var manifest=InstalledSnapshot.Create(package,store.Load(project),new[]{first.Id,second.Id,empty.Id},catalog,data,false,null);
        var receivedProject=store.Create("Recebido",manifest.List,"light","pt",received);new InstalledSnapshotInstaller(received,store.DataPath(receivedProject),catalog,true).Install(package,Safe.Hash(package),null);File.Delete(package);
        var receivedSize=GeneralProjectStorage.Measure(store,catalog,cache,receivedProject);
        check(receivedSize.Identified==3&&receivedSize.Mods==5&&receivedSize.ModBytes[first.Id]==5&&receivedSize.ModBytes[second.Id]==5,"P2P receipt owners identify per-mod space after deleting the transfer ZIP.");
        File.Delete(source);File.Delete(zero);size=GeneralProjectStorage.Measure(store,catalog,cache,project);
        check(size.Identified==3&&size.Mods==5&&size.Total==size.Mods+size.Cache,"Deleting original downloads does not erase installed size knowledge or create another cache copy.");
        File.WriteAllText(Safe.In(game,"mods/shared.txt"),"larger content");size=GeneralProjectStorage.Measure(store,catalog,cache,project);
        check(size.ModBytes[first.Id]==14&&size.Mods==14,"Occupied bytes come from current file metadata rather than old receipt size.");File.WriteAllText(Safe.In(game,"mods/shared.txt"),"share");
        var visibleState=store.Load(project);visibleState.SelectedPreset=null;
        TestForm(catalog,visibleState,data,root,async form=>{
            form.AttachProject(store,project);form.RefreshStorageSize();await Until(()=>!form.storageReading);form.DrawRows();
            check(form.counts.Text.Contains("Medidos: 3/4"),"Counter reports identified sizes over all visible mod rows: "+form.counts.Text);
            check(form.grid.Columns["Size"].DisplayIndex==2&&form.grid.Columns["Variant"].Index==2&&form.grid.Columns["Status"].Index==3,"Space sits next to the name without changing the existing column-filter identities.");
            check((string)form.ModRows().Single(row=>(string)row.Tag==first.Id).Cells["Size"].Value=="5 B"&&(string)form.ModRows().Single(row=>(string)row.Tag==empty.Id).Cells["Size"].Value=="0 B","Rows show actual measured values, including zero.");
            check((string)form.ModRows().Single(row=>(string)row.Tag==absent.Id).Cells["Size"].Value=="—"&&form.ModRows().Single(row=>(string)row.Tag==absent.Id).Cells["Size"].ToolTipText.Contains("não identificado"),"Unknown sizes remain explicit and do not use download estimates.");
            form.marked.UnionWith(list.Mods.Select(mod=>mod.Id));form.DrawRows();check(form.counts.Text.Contains("Medidos: 3/4"),"Selecting all items does not change the measurement ratio.");
            form.preset.SelectedIndex=1;check(form.counts.Text.Contains("Medidos: 1/2"),"Preset filtering keeps numerator and denominator on the same visible rows.");form.preset.SelectedIndex=0;
            foreach(string theme in new[]{"light","dark","light-red"}){
                GeneralStyle.UseTheme(theme);form.state.Theme=theme;form.BuildInterface();form.RefreshStorageSize();await Until(()=>!form.storageReading);
                foreach(var dimensions in new[]{new Size(1000,680),new Size(1240,820)}){
                    form.ClientSize=dimensions;form.PerformLayout();await Task.Delay(35);
                    check(form.grid.Columns["Size"].Visible&&form.grid.GetCellDisplayRectangle(form.grid.Columns["Size"].Index,-1,true).Width>=100,"Space column remains readable at "+dimensions+" / "+theme);
                    var bounds=form.RectangleToClient(form.storageSize.RectangleToScreen(form.storageSize.ClientRectangle));
                    check(bounds.Right<=form.ClientSize.Width&&form.storageSize.Text.Contains("Cache do projeto:"),"Totals and project-cache text fit the window / "+theme+" / "+dimensions);
                    CaptureUi(form,Path.Combine(root,"sizes-"+theme+"-"+dimensions.Width+".png"));
                }
            }
            GeneralStyle.Language="en";form.state.Language="en";form.BuildInterface();form.RefreshStorageSize();await Until(()=>!form.storageReading);check(form.counts.Text.Contains("Measured: 3/4")&&form.storageSize.Text.Contains("Project cache:"),"English uses the same measured ratio and cache semantics.");
            form.ClientSize=new Size(1000,680);form.PerformLayout();await Task.Delay(35);CaptureUi(form,Path.Combine(root,"sizes-en.png"));
        });
        TestFixedStorage(root,check);
        GeneralStyle.UseTheme("light");GeneralStyle.Language="pt";
        Safe.WriteJson(Path.Combine(root,"resultado-storage-by-mod.json"),new{Passed=true,Tests=tests,GameTouched=false,NetworkUsed=false,FileContentsHashedForMeasurement=false});
    }
    static void TestFixedStorage(string root,Action<bool,string> check){
        string target=OnlineTests.Fixture(root,"g");var cache=new DownloadCache(Path.Combine(root,"fc"));var store=new GeneralProjectStore(Path.Combine(root,"fp"));var catalog=new FixedCatalog{Schema=1,Id="fixed-sizes",GameVersion="2.5",GameFileVersion="2.5.0.0",Packages=new List<FixedMod>()};
        foreach(var pair in new[]{new[]{"base","framework"},new[]{"outfit","suit"}}){
            string path="archive/pc/mod/"+pair[0]+".archive",zip=TestZip(root,pair[0]+".zip",path,pair[1]);var asset=new FixedAsset{Id="a",Name=Path.GetFileName(zip),ModId=200,FileId=pair[0]=="base"?1:2,Format="zip",Size=new FileInfo(zip).Length,Sha256=Safe.Hash(zip)};
            catalog.Packages.Add(new FixedMod{Id=pair[0],Name=pair[0],Version="1",GameVersion="2.5",Dependencies=pair[0]=="base"?new string[0]:new[]{"base"},Assets=new List<FixedAsset>{asset},Files=new List<FixedFile>{new FixedFile{Asset="a",Entry=path,Path=path,Size=pair[1].Length,Sha256=TestHash(pair[1])}}});
            string file=Safe.In(target,path);Directory.CreateDirectory(Path.GetDirectoryName(file));File.WriteAllText(file,pair[1]);Directory.CreateDirectory(cache.Root);File.Copy(zip,cache.PathFor(asset));
        }
        var seed=GeneralModLibrary.Seed(catalog,new ModImportItem[0]);seed.Mods=seed.Mods.Where(mod=>mod.Id=="outfit").ToList();seed.Presets.Clear();var project=store.Create("Roupa e dependência",seed,"light","pt",target);
        var size=GeneralProjectStorage.Measure(store,catalog,cache,project);
        check(size.Identified==1&&size.ModBytes["outfit"]==4&&size.Mods==13,"A fixed mod row counts its own files; project total also includes its installed dependency.");
        long support=Directory.GetFiles(store.DataPath(project),"*",SearchOption.AllDirectories).Sum(path=>new FileInfo(path).Length),archives=catalog.Packages.SelectMany(recipe=>recipe.Assets).Sum(asset=>new FileInfo(cache.PathFor(asset)).Length);
        check(size.Cache==support&&size.Retained==archives,"Metadata cache stays small; old dependency packages are shown separately until explicit cleanup.");
        string first=Safe.In(target,"archive/pc/mod/base.archive"),second=Safe.In(target,"archive/pc/mod/outfit.archive");File.Delete(first);File.Delete(second);size=GeneralProjectStorage.Measure(store,catalog,cache,project);
        check(size.Identified==0&&size.ModCount==1&&!size.ModBytes.ContainsKey("outfit")&&size.Mods==0&&size.Cache>0,"Cached packages are not mistaken for installed mod space or measured mods.");
        var projectCache=new DownloadCache(Path.Combine(root,"project-index"),()=>root,cache.Root);
        var oldPackage=GeneralProjectStorage.Measure(store,catalog,projectCache,project);
        check(oldPackage.Retained==archives&&projectCache.Root!=projectCache.RetainedRoot,"Project-local index does not hide the legacy cache measured by the UI.");
        using(var label=new GeneralStorageLabel(projectCache)){
            label.SetSize(oldPackage,false);
            var link=label.Links.Cast<LinkLabel.Link>().Single();
            check((string)link.LinkData==cache.Root&&label.Text.Substring(link.Start,link.Length)==GeneralStorageSize.RetainedText(oldPackage),"Only the old packages metric links to the exact directory being measured.");
            label.SetSize(oldPackage,true);
            check((string)label.Links.Cast<LinkLabel.Link>().Single().LinkData==cache.Root,"The home counter opens the same measured directory.");
            label.SetSize(new GeneralStorageSize(),true);
            check(label.LinkArea.Length==0,"No old packages link appears after the legacy cache is empty.");
        }
        string originalDownload=Path.Combine(root,"outfit.zip"),otherFile=Path.Combine(cache.Root,"unrecognized.cache");
        File.WriteAllText(otherFile,"keep");
        var previousProbe=StorageMaintenance.OtherLoaderProbe;StorageMaintenance.OtherLoaderProbe=()=>false;
        try{check(projectCache.ClearRetainedDownloads()==archives&&File.Exists(originalDownload)&&File.Exists(otherFile),"Project cleanup removes only the measured legacy copies and preserves the original downloads.");}
        finally{StorageMaintenance.OtherLoaderProbe=previousProbe;}
        var real=FixedData.Load();var density=real.Packages.Single(recipe=>recipe.Id==NightCityDensity.ModId);string densityPath=Safe.In(target,density.Files[0].Path);Directory.CreateDirectory(Path.GetDirectoryName(densityPath));File.WriteAllText(densityPath,"changed");
        var densityList=GeneralModLibrary.Seed(real,new ModImportItem[0]);densityList.Mods=densityList.Mods.Where(mod=>mod.Id==density.Id).ToList();densityList.Presets.Clear();var densityProject=store.Create("Densidade medida",densityList,"light","pt",target);
        var densitySize=GeneralProjectStorage.Measure(store,real,cache,densityProject);check(densitySize.Identified==1&&densitySize.ModBytes[density.Id]==7,"Density size uses existing known paths even when contents differ; integrity and variant checks stay separate.");

    }
}
