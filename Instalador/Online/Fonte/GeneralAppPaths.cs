using System;
using System.IO;

internal sealed class GeneralAppPaths {
#if MODLOADER_DEV
    internal static readonly bool IsDevelopment=true;
#else
    internal static readonly bool IsDevelopment=false;
#endif
    internal static string ProductName {get{return IsDevelopment?"Mod Loader Dev":"Mod Loader";}}
    internal static string WindowTitle {get{return ProductName+" "+Version;}}
    internal static string Version {get{
#if VELOPACK && MODLOADER_DEV
        return GeneralVelopackUpdate.InstalledVersion()??GeneralUpdateChecker.CurrentVersion;
#else
        return GeneralUpdateChecker.CurrentVersion;
#endif
    }}
    internal readonly string Data,Cache,Logs;
    internal GeneralAppPaths(string local,bool development=false){
        local=Path.GetFullPath(local);
        string modern=Path.Combine(local,development?"ModLoaderDev":"ModLoader");
        Data=Path.Combine(modern,"Config");Cache=Path.Combine(modern,"Cache");Logs=Path.Combine(modern,"Logs");
        if(!development){
            string legacy=Path.Combine(local,"ModLoaderGeral");
            if(!HasLibrary(Data)&&HasLibrary(legacy))Data=legacy;
            string oldCache=Path.Combine(local,"CyberpunkCoop","Downloads");
            if(Directory.Exists(oldCache))Cache=oldCache;
            string oldLogs=Path.Combine(local,"CyberpunkCoop","Logs");
            if(Directory.Exists(oldLogs))Logs=oldLogs;
        }
        foreach(string path in new[]{Data,Cache,Logs})Safe.NoLinks(path);
    }
    static bool HasLibrary(string path){return File.Exists(Path.Combine(path,"projects.json"))||File.Exists(Path.Combine(path,"lista.json"));}
    internal static GeneralAppPaths Current(){return new GeneralAppPaths(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),IsDevelopment);}
}
