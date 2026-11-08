using System;
using System.Collections.Generic;
using System.IO;

internal static class GeneralAppPathsTests {
    internal static void Run(string parent){
        string root=OnlineTests.NestedRoot(parent,"paths");Directory.CreateDirectory(root);var checks=new List<string>();Action<bool,string> check=(ok,text)=>{if(!ok)throw new IOException(text);checks.Add(text);};
        string local=Path.Combine(root,"local"),modern=Path.Combine(local,"ModLoader"),old=Path.Combine(local,"ModLoaderGeral"),oldCache=Path.Combine(local,"CyberpunkCoop","Downloads");
        var paths=new GeneralAppPaths(local);check(paths.Data==Path.Combine(modern,"Config")&&paths.Cache==Path.Combine(modern,"Cache")&&paths.Logs==Path.Combine(modern,"Logs"),"New users get separate configuration, cache and log locations.");
        check(!Directory.Exists(local),"Resolving locations creates no personal data or cache.");
        Directory.CreateDirectory(old);string index=Path.Combine(old,"projects.json");File.WriteAllText(index,"existing project fixture");Directory.CreateDirectory(oldCache);File.WriteAllText(Path.Combine(oldCache,"preserve.cache"),"cache fixture");string before=Safe.Hash(index);
        paths=new GeneralAppPaths(local);check(paths.Data==old&&paths.Cache==oldCache,"Existing project library and cache are reused without moving or duplicating them.");
        Directory.CreateDirectory(Path.Combine(modern,"Config"));Directory.CreateDirectory(Path.Combine(modern,"Cache"));paths=new GeneralAppPaths(local);check(paths.Data==old&&paths.Cache==oldCache,"Empty directories made by setup do not hide the previous library/cache.");
        string modernIndex=Path.Combine(modern,"Config","projects.json");File.WriteAllText(modernIndex,"new library fixture");paths=new GeneralAppPaths(local);check(paths.Data==Path.GetDirectoryName(modernIndex)&&paths.Cache==oldCache,"An existing modern library takes priority; historical cache remains available.");
        File.Delete(modernIndex);File.Delete(index);File.WriteAllText(Path.Combine(old,"lista.json"),"legacy list fixture");paths=new GeneralAppPaths(local);check(paths.Data==old,"The original single-list library is also recognized.");
        check(File.ReadAllText(Path.Combine(oldCache,"preserve.cache"))=="cache fixture"&&Directory.GetFiles(oldCache).Length==1,"Path resolution leaves cache content intact.");
        var dev=new GeneralAppPaths(local,true);string devRoot=Path.Combine(local,"ModLoaderDev");
        check(dev.Data==Path.Combine(devRoot,"Config")&&dev.Cache==Path.Combine(devRoot,"Cache")&&dev.Logs==Path.Combine(devRoot,"Logs"),"Dev never reuses an existing stable project, cache or log path.");
        check(StorageMaintenance.SameEdition("LoaderDev",true)&&!StorageMaintenance.SameEdition("ModLoader",true)&&!StorageMaintenance.SameEdition("LoaderDev",false)&&StorageMaintenance.SameEdition("ModLoader",false),"Running the other edition does not block updates or cleanup.");
        check(!Directory.Exists(devRoot),"The path check does not create any development data.");
        string product=GeneralAppPaths.IsDevelopment?"Mod Loader Dev":"Mod Loader";
        check(GeneralAppPaths.WindowTitle.StartsWith(product+" ",StringComparison.Ordinal)&&
            P2PService.TransferStore==Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),product,"Pacotes P2P"),
            "The edition's title and P2P transfer folder use the same identity.");
        Safe.WriteJson(Path.Combine(root,"resultado-app-paths.json"),new{Passed=true,Tests=checks,PersonalDataTouched=false,GameTouched=false});
    }
}
