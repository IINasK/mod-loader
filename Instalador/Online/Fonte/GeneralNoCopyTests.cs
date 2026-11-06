using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    internal static void RunNoCopyTests(string parent){
        var previous=StorageMaintenance.OtherLoaderProbe;StorageMaintenance.OtherLoaderProbe=()=>false;
        try{RunNoCopyCases(parent);}finally{StorageMaintenance.OtherLoaderProbe=previous;}
    }
    static void RunNoCopyCases(string parent){
        string root=System.Text.RegularExpressions.Regex.IsMatch(Path.GetFullPath(parent).Replace(Path.DirectorySeparatorChar,'/'),@"(?i)/online-testes-[a-f0-9]{32}(/|$)")?Path.Combine(parent,"n"):OnlineTests.NestedRoot(parent,"no-copy");Directory.CreateDirectory(root);
        var tests=new List<string>();Action<bool,string> check=(ok,text)=>{if(!ok)throw new IOException(text);tests.Add(text);};
        string downloads=Path.Combine(root,"downloads");Directory.CreateDirectory(downloads);
        string original=TestZip(downloads,"original.zip","mods/nested/one.txt",new string('x',1024));string hash=Safe.Hash(original);
        var asset=new FixedAsset{Id="a",Name="original.zip",Format="zip",Size=new FileInfo(original).Length,Sha256=hash};
        var store=new GeneralProjectStore(Path.Combine(root,"p"));var catalog=new FixedCatalog{Schema=1,Id="no-copy",GameVersion="2.5",GameFileVersion="2.5.0.0",Packages=new List<FixedMod>()};
        string game=Path.Combine(root,"g");Directory.CreateDirectory(game);File.WriteAllText(Path.Combine(game,".fixture-only"),"fixture");
        var mod=NewEntry("Nested mod",original);var list=new GeneralModList{Mods=new List<GeneralModEntry>{mod}};list.Presets.Add(new GeneralModPreset{Id="mine",Name="Meu preset",ModIds=new List<string>{mod.Id}});
        var project=store.Create("Sem cópias",list,"light","pt",game);string data=store.DataPath(project);
        var cache=new DownloadCache(Path.Combine(data,"Cache"),()=>downloads);cache.Import(original,asset);
        check(cache.PathFor(asset)==original&&cache.Contains(asset),"Import verifies the original file and records its path without copying it.");
        check(Directory.GetFiles(cache.Root,"*.cache").Length==0&&Directory.GetFiles(cache.Root,"*",SearchOption.AllDirectories).Sum(file=>new FileInfo(file).Length)<4096,"Project download index stays small and contains no retained archive.");
        check(new DownloadCache(cache.Root,()=>downloads).PathFor(asset)==original,"Original download reference survives reopening the cache.");
        new GeneralModInstaller(game,Path.Combine(data,"Instalacoes")).Install(original,mod.Id+"/default/0/"+original,null);
        var size=GeneralProjectStorage.Measure(store,catalog,cache,project);long support=Directory.GetFiles(data,"*",SearchOption.AllDirectories).Sum(file=>new FileInfo(file).Length);
        check(size.Mods==1024&&size.Cache==support&&size.Total==1024+support+new FileInfo(original).Length,"Project counters separate installed files, original Downloads and metadata-only cache.");
        File.Delete(original);check(!cache.Contains(asset),"User removal of the original download is detected without restoring a cache copy.");
        var state=store.Load(project);var manifest=InstalledSnapshot.Inspect(state,new[]{mod.Id},catalog,data,true);
        string output=Path.Combine(root,"manual.zip");InstalledSnapshot.CreateModpack(output,manifest,game,catalog,null);
        using(var zip=ZipFile.OpenRead(output)){
            var entry=zip.GetEntry("mods/nested/one.txt");check(entry!=null&&entry.Length==1024,"Manual modpack retains every relative subfolder down to a single 1 KiB file.");
            check(zip.Entries.All(entryItem=>!entryItem.FullName.StartsWith("files/"))&&zip.GetEntry("_modloader/project.json")!=null,"Offline ZIP contains a root-relative mod tree and small project metadata, not transport hash blobs.");
            string tree=Path.Combine(root,"other-game");Directory.CreateDirectory(tree);ZipFile.ExtractToDirectory(output,tree);check(Safe.Hash(Safe.In(tree,"mods/nested/one.txt"))==manifest.Files.Single().Sha256,"Extracting at another game's root recreates the exact mod path.");
            using(var reader=new StreamReader(zip.GetEntry("_modloader/project.json").Open())){string json=reader.ReadToEnd();check(json.Contains("Meu preset")&&json.Contains("Nested mod")&&!json.Contains(downloads.Replace("\\","\\\\")),"Export preserves names and presets without private absolute download paths.");}
        }
        string snapshot=Path.Combine(root,"p2p.zip");InstalledSnapshot.Create(snapshot,state,new[]{mod.Id},catalog,data,true,null);check(InstalledSnapshot.Read(snapshot,Safe.Hash(snapshot),catalog).Files.Count==1,"P2P remains compatible and exports installed bytes after original archive deletion.");
        var missingCar=new GeneralModEntry{Id="optional-car",Name="Optional car",Category="Cars",DefaultVariant="main",Variants=new List<GeneralModVariant>{new GeneralModVariant{Id="main",Name="Standard",Sources=new List<string>{"https://example.org/car.zip"}}}};
        state.List.Categories.Add("Cars");state.List.Mods.Add(missingCar);state.List.Presets[0].ModIds.Add(missingCar.Id);
        var partial=InstalledSnapshot.Inspect(state,new[]{mod.Id,missingCar.Id},catalog,data,true,true);
        check(partial.List.Mods.Count==1&&partial.Files.Count==1&&partial.Project.Mods.Count==2&&partial.Project.Presets[0].ModIds.Contains(missingCar.Id),"Partial sharing carries the full project/preset while the payload includes only verified installed mods.");
        string partialZip=Path.Combine(root,"partial.zip");InstalledSnapshot.Create(partialZip,state,new[]{mod.Id,missingCar.Id},catalog,data,true,null,true);var readPartial=InstalledSnapshot.Read(partialZip,Safe.Hash(partialZip),catalog);
        check(readPartial.Project.Mods.Single(m=>m.Id==missingCar.Id).Variants[0].Sources.Single()=="https://example.org/car.zip"&&readPartial.List.Mods.Count==1,"Missing mods retain complete public sources for later download, without becoming installed owners.");
        var privateProject=Safe.Json.Deserialize<InstalledSnapshotManifest>(Safe.Json.Serialize(partial));privateProject.Project.Mods.Single(m=>m.Id==missingCar.Id).Variants[0].Sources.Add(original);bool privateRejected=false;try{InstalledSnapshot.Validate(privateProject,catalog);}catch(IOException){privateRejected=true;}check(privateRejected,"Full-project metadata refuses private local download paths.");
        var wrongProject=Safe.Json.Deserialize<InstalledSnapshotManifest>(Safe.Json.Serialize(partial));wrongProject.Project.Mods.Single(m=>m.Id==mod.Id).Variants[0].Sources=new List<string>{"https://example.org/different.zip"};bool mismatchRejected=false;try{InstalledSnapshot.Validate(wrongProject,catalog);}catch(IOException){mismatchRejected=true;}check(mismatchRejected,"Full-project metadata cannot replace the source identity of a transferred installed mod.");
        string metadataZip=Path.Combine(root,"metadata.zip");InstalledSnapshot.Create(metadataZip,state,new[]{missingCar.Id},catalog,data,true,null,true);
        var metadata=InstalledSnapshot.Read(metadataZip,Safe.Hash(metadataZip),catalog);
        check(metadata.Files.Count==0&&metadata.List.Mods.Count==0&&metadata.Project.Mods.Count==2,"A 0%-available preset shares metadata without inventing installed files.");
        string metadataTarget=Path.Combine(root,"metadata-target"),metadataData=Path.Combine(root,"metadata-data");Directory.CreateDirectory(metadataTarget);
        var metadataInstaller=new InstalledSnapshotInstaller(metadataTarget,metadataData,catalog);string metadataReceipt=metadataInstaller.Install(metadataZip,Safe.Hash(metadataZip),_=>{});
        check(metadataInstaller.ReadManifest(metadataReceipt).Project.Mods.Count==2&&Directory.GetFiles(metadataTarget,"*",SearchOption.AllDirectories).Length==0,"Metadata-only receive preserves the future download list and installs no game payload.");
        var sample=Enumerable.Range(0,20).Select(i=>new GeneralModEntry{Id="m"+i,Category=i<17?"Main":i<19?"Cars":"FPV"}).ToList();var segments=PackCoverage.Build(sample,m=>Int32.Parse(m.Id.Substring(1))<17,"Uncategorized");
        check(segments.Count==3&&segments[0].Count==17&&segments[1].Label=="Cars"&&segments[1].Count==2&&segments[2].Label=="FPV"&&segments[2].Count==1,"Coverage is 85% green, 10% cars and 5% FPV by mod count.");
        check(segments[1].Color==PackCoverage.CategoryColor("Cars")&&segments[1].Color!=segments[2].Color&&PackCoverage.Build(sample,m=>true,"Uncategorized").Count==1,"Category colors are deterministic and completion leaves a single green segment.");
        File.WriteAllText(Safe.In(game,"mods/nested/one.txt"),"changed");bool changed=false;try{InstalledSnapshot.CreateModpack(Path.Combine(root,"changed.zip"),manifest,game,catalog,null);}catch(IOException){changed=true;}
        check(changed&&!File.Exists(Path.Combine(root,"changed.zip")),"Changed-during-export files stop creation and remove the incomplete output.");
        File.Delete(Safe.In(game,"mods/nested/one.txt"));bool missing=false;try{InstalledSnapshot.Inspect(state,new[]{mod.Id},catalog,data,true);}catch(InstalledSnapshotReviewException ex){missing=ex.Problems.Length==1&&ex.Message.Contains(mod.Name);}
        check(missing,"Preflight reports the incomplete mod before creating an archive or contacting a peer.");
        bool partialGuard=false;try{InstalledSnapshot.Inspect(state,new[]{mod.Id,missingCar.Id},catalog,data,true,true);}catch(InstalledSnapshotReviewException){partialGuard=true;}
        check(partialGuard,"Partial sharing still rejects a recorded mod with missing installed files; it is not silently treated as optional.");
        byte[] bytes=Encoding.UTF8.GetBytes("direct original");var direct=new FixedAsset{Id="direct",Name="direct.zip",Format="zip",Size=bytes.Length,Sha256=InstalledSnapshot.Hash(bytes)};
        string temp=cache.DownloadTemporaryPath();File.WriteAllBytes(temp,bytes);using(var control=new DownloadControl())NexusClient.CommitDownload(cache,direct,temp,control);
        string directPath=cache.PathFor(direct);check(directPath.StartsWith(downloads+"\\",StringComparison.OrdinalIgnoreCase)&&File.Exists(directPath)&&!File.Exists(temp),"Direct downloads finish in the user's Downloads folder, not in AppData.");
        check(Directory.GetFiles(cache.Root,"*.cache").Length==0,"Direct download completion never creates a permanent payload cache.");
        string sentinel=Safe.Hash(directPath);cache.ClearDownloads();check(Safe.Hash(directPath)==sentinel,"Cache cleanup never deletes indexed original downloads.");
        var child=store.Create("Outro",new GeneralModList(),"light","pt");File.WriteAllText(Path.Combine(store.DataPath(child),"child.txt"),"child");
        var one=GeneralProjectStorage.Measure(store,catalog,cache,project);var two=GeneralProjectStorage.Measure(store,catalog,cache,child);var all=GeneralProjectStorage.Measure(store,catalog,cache);
        long indexSize=new FileInfo(Path.Combine(store.Root,"projects.json")).Length;
        check(all.Cache==one.Cache+two.Cache+indexSize,"Home support total is the union of registered project data plus the one shared library index.");
        check(one.Cache==Directory.GetFiles(data,"*",SearchOption.AllDirectories).Sum(file=>new FileInfo(file).Length),"Other projects do not inflate the selected project's cache.");
        string legacyRoot=Path.Combine(root,"legacy");Directory.CreateDirectory(legacyRoot);new GeneralModLibrary(Path.Combine(legacyRoot,"lista.json")).Save(new GeneralModState{List=new GeneralModList()});
        var legacyStore=new GeneralProjectStore(legacyRoot);var legacy=legacyStore.Read().Projects.Single();var nested=legacyStore.Create("Separado",new GeneralModList(),"light","pt");File.WriteAllText(Path.Combine(legacyStore.DataPath(nested),"separate.bin"),new string('x',8192));
        var legacySize=GeneralProjectStorage.Measure(legacyStore,catalog,cache,legacy);long own=Directory.GetFiles(legacyRoot).Where(file=>Path.GetFileName(file)!="projects.json").Sum(file=>new FileInfo(file).Length);
        check(legacySize.Cache==own,"Legacy project root excludes the nested Projetos folder and shared index from its own cache.");
        var secondMod=NewEntry("Outro faltante",original);state.List.Mods.Add(secondMod);bool aggregated=false;try{InstalledSnapshot.Inspect(state,new[]{mod.Id,secondMod.Id},catalog,data,true);}catch(InstalledSnapshotReviewException ex){aggregated=ex.Problems.Length==2&&ex.Message.Contains(mod.Name)&&ex.Message.Contains(secondMod.Name);}
        check(aggregated,"Preflight lists all incomplete selected mods together before export or transfer.");
        Safe.WriteJson(Path.Combine(root,"resultado-no-copy.json"),new{Passed=true,Tests=tests,NetworkUsed=false,RealGameTouched=false,OriginalDownloadCleanupAutomatic=false});
    }
}
