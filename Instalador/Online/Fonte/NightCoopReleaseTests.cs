using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal static class NightCoopReleaseTests {
    internal static void Run(string parent,string sources){
        string root=OnlineTests.NestedRoot(parent,"nightcoop-release-");
        Directory.CreateDirectory(root);
        var checks=new List<string>();
        var current=FixedData.Load();
        current.GameFileVersion="2.5.0.0";
        foreach(string oldUrl in new[]{NightCoopRelease.LegacyUrl,NightCoopRelease.PriorUrl})
            RunUpgrade(root,sources,current,oldUrl,(pass,message)=>{if(!pass)throw new IOException(message);checks.Add(message);});
        Safe.WriteJson(Safe.In(root,"resultado-nightcoop-021.json"),new{Passed=true,Tests=checks,RealGameTouched=false});
    }

    static void RunUpgrade(string root,string sources,FixedCatalog current,string oldUrl,Action<bool,string> check){
        Action<string> quiet=_=>{};
        var latest=current.Packages.Single(mod=>mod.Id=="NightCoop");
        var latestAsset=latest.Assets.Single();
        var oldList=GeneralModLibrary.Seed(current,new ModImportItem[0]);
        oldList.Mods.Single(mod=>mod.Id=="NightCoop").Variants.Single().Sources=new List<string>{oldUrl};
        var legacy=NightCoopRelease.CatalogForReceipt(current,new InstalledSnapshotManifest{List=oldList});
        var older=legacy.Packages.Single(mod=>mod.Id=="NightCoop");
        var oldAsset=older.Assets.Single();
        GeneralModLibrary.RestoreKnownMappings(oldList,GeneralModLibrary.Seed(current,new ModImportItem[0]));
        var migrated=oldList.Mods.Single(mod=>mod.Id=="NightCoop").Variants.Single();
        check(migrated.CyberpunkId=="NightCoop"&&migrated.Sources.SequenceEqual(new[]{NightCoopRelease.Url}),
            "An existing project migrates the exact official legacy link to the trusted current recipe.");

        var cache=new DownloadCache(Path.Combine(root,"cache"));
        cache.Import(Safe.In(sources,oldAsset.Name),oldAsset);
        cache.Import(Safe.In(sources,latestAsset.Name),latestAsset);
        string caseRoot=Safe.In(root,older.Version);
        string game=Safe.In(caseRoot,"g");
        string exe=Safe.In(game,"bin/x64/Cyberpunk2077.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(exe));
        File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","FixtureGame.exe"),exe);
        File.WriteAllText(Safe.In(game,".fixture-only"),"test");
        string data=Path.Combine(caseRoot,"snapshots"),saves=Path.Combine(caseRoot,"saves");
        Directory.CreateDirectory(saves);
        File.WriteAllText(Safe.In(saves,"fixture.txt"),"preserve");
        new FixedInstaller(legacy,cache,game,saves,true).Install(new[]{"NightCoop"},true,quiet);
        var deployed=new GeneralPresetDeployment(game,data);
        var previous=Preset(older,false);
        deployed.Apply("shared","previous",new[]{previous},new[]{previous});
        var initial=FixedData.Scan(latest,game);
        check(initial.State=="Atualização disponível"&&initial.Original==1&&initial.Missing==(oldUrl==NightCoopRelease.LegacyUrl?5:0),
            "The exact official "+older.Version+" DLL is eligible for upgrade.");
        var preserved=latest.Files.Take(4).ToDictionary(file=>file.Path,file=>File.GetLastWriteTimeUtc(Safe.In(game,file.Path)));
        var sourceState=new GeneralModState{Destination=game,List=GeneralModLibrary.Seed(legacy,new ModImportItem[0])};
        string zip=Safe.In(caseRoot,"legacy-snapshot.zip");
        InstalledSnapshot.Create(zip,sourceState,new[]{"NightCoop"},legacy,data,false,quiet);
        var snapshot=new InstalledSnapshotInstaller(game,data,legacy,true);
        string receipt=snapshot.Install(zip,Safe.Hash(zip),quiet);

        var engine=new FixedInstaller(current,cache,game,saves,true);
        try{
            engine.Install(new[]{"NightCoop"},true,quiet,1);
            throw new InvalidOperationException("A forced fixture copy failure must fail.");
        }catch(IOException){}
        check(FixedData.Scan(latest,game).State=="Atualização disponível",
            "Failure rolls back to the original official DLL.");
        engine.Install(new[]{"NightCoop"},true,quiet);
        check(FixedData.Scan(latest,game).IsCorrect&&preserved.All(file=>File.GetLastWriteTimeUtc(Safe.In(game,file.Key))==file.Value),
            "Upgrade installs all official 0.2.1 files without rewriting RED4ext 1.31.0 or its companion files.");
        var next=Preset(latest,true);
        var result=deployed.Apply("shared","current",new[]{next},new[]{next});
        check(result.Active.Single().Key==next.Key&&result.Protected.Count==0,
            "The preset recognizes the verified base upgrade without marking the installed release as conflicting.");
        check(File.ReadAllText(Safe.In(saves,"fixture.txt"))=="preserve","The fixture save stays intact.");
        check(new InstalledSnapshotInstaller(game,data,current,true).ReadManifest(receipt).List.Mods.Single().Variants.Single().Sources.Single()==oldUrl,
            "The new release still reads protected local receipts for previously installed packs.");
    }

    static PresetPackage Preset(FixedMod mod,bool current){
        string key="NightCoop/"+mod.Version.Replace('.','-');
        return new PresetPackage{Key=key,StorageKey=key,ModId=mod.Id,Name=mod.Name,Immutable=true,OfficialNightCoop=current,
            Files=mod.Files.Select(file=>new DisabledModFile{Path=file.Path,Size=file.Size,Hash=file.Sha256}).ToList()};
    }
}
