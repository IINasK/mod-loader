using System;
using System.IO;

internal sealed class GeneralAppPaths {
    internal readonly string Data,Cache,Logs;
    internal GeneralAppPaths(string local){
        local=Path.GetFullPath(local);string modern=Path.Combine(local,"ModLoader"),data=Path.Combine(modern,"Config"),legacy=Path.Combine(local,"ModLoaderGeral");
        Data=HasLibrary(data)||!HasLibrary(legacy)?data:legacy;
        string oldCache=Path.Combine(local,"CyberpunkCoop","Downloads");Cache=Directory.Exists(oldCache)?oldCache:Path.Combine(modern,"Cache");
        string oldLogs=Path.Combine(local,"CyberpunkCoop","Logs");Logs=Directory.Exists(oldLogs)?oldLogs:Path.Combine(modern,"Logs");
        foreach(string path in new[]{Data,Cache,Logs})Safe.NoLinks(path);
    }
    static bool HasLibrary(string path){return File.Exists(Path.Combine(path,"projects.json"))||File.Exists(Path.Combine(path,"lista.json"));}
    internal static GeneralAppPaths Current(){return new GeneralAppPaths(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));}
}
