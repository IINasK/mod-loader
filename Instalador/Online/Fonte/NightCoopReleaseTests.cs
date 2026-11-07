using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal static class NightCoopReleaseTests {
    internal static void Run(string parent,string sources){
        string root=OnlineTests.NestedRoot(parent,"nightcoop-release-");
        Directory.CreateDirectory(root);
        var checks=new List<string>();
        Action<bool,string> check=(pass,message)=>{if(!pass)throw new IOException(message);checks.Add(message);};
        Action<string> quiet=_=>{};
        var current=FixedData.Load();
        current.GameFileVersion="2.5.0.0";
        var latest=current.Packages.Single(mod=>mod.Id=="NightCoop");
        var latestAsset=latest.Assets.Single();
        var oldList=GeneralModLibrary.Seed(current,new ModImportItem[0]);
        oldList.Mods.Single(mod=>mod.Id=="NightCoop").Variants.Single().Sources=new List<string>{NightCoopRelease.PriorUrl};
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
        string game=Safe.In(root,"g");
        string exe=Safe.In(game,"bin/x64/Cyberpunk2077.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(exe));
        File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","FixtureGame.exe"),exe);
        File.WriteAllText(Safe.In(game,".fixture-only"),"test");
        string data=Path.Combine(root,"snapshots"),saves=Path.Combine(root,"saves");
        Directory.CreateDirectory(saves);
        File.WriteAllText(Safe.In(saves,"fixture.txt"),"preserve");
        new FixedInstaller(legacy,cache,game,saves,true).Install(new[]{"NightCoop"},true,quiet);
        var initial=FixedData.Scan(latest,game);
        check(initial.State=="Atualização disponível"&&initial.Original==1&&initial.Missing==5,
            "The exact official 0.1.2 DLL is eligible for upgrade.");
        var preserved=latest.Files.Take(4).ToDictionary(file=>file.Path,file=>File.GetLastWriteTimeUtc(Safe.In(game,file.Path)));
        var sourceState=new GeneralModState{Destination=game,List=GeneralModLibrary.Seed(legacy,new ModImportItem[0])};
        string zip=Safe.In(root,"legacy-snapshot.zip");
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
            "Upgrade installs all official 0.2.0 files without rewriting RED4ext 1.31.0 or its companion files.");
        check(File.ReadAllText(Safe.In(saves,"fixture.txt"))=="preserve","The fixture save stays intact.");
        check(new InstalledSnapshotInstaller(game,data,current,true).ReadManifest(receipt).List.Mods.Single().Variants.Single().Sources.Single()==NightCoopRelease.PriorUrl,
            "The new release still reads protected local receipts for previously installed packs.");
        Safe.WriteJson(Safe.In(root,"resultado-nightcoop-020.json"),new{Passed=true,Tests=checks,RealGameTouched=false});
    }
}
