using System;
using System.Collections.Generic;
using System.IO;

public static class DefaultBrowserTests {
    public static void Run(string parent) {
        string root=Path.Combine(Path.GetFullPath(parent),"default-browser-"+Guid.NewGuid().ToString("N").Substring(0,12));Directory.CreateDirectory(root);
        var passed=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("TESTE DEFAULT BROWSER FALHOU: "+name);passed.Add(name);};
        foreach(var sample in new[]{new[]{"chrome.exe","Google Chrome","Chrome"},new[]{"chrome.exe","Chromium","Chrome"},new[]{"msedge.exe","Microsoft Edge","Edge"},new[]{"brave.exe","Brave","Brave"},new[]{"vivaldi.exe","Vivaldi","Vivaldi"},new[]{"opera.exe","Opera Internet Browser","Opera"},new[]{"launcher.exe","Opera GX Internet Browser","OperaGX"}}) {
            var browser=DefaultBrowser.Classify(Path.Combine(root,sample[0]),sample[1]);
            check(browser.SupportsControl&&browser.ProfileKey==sample[2]&&browser.Executable.EndsWith(sample[0]),"Associação reconhece "+sample[1]+" sem substituir seu executável");
        }
        foreach(var sample in new[]{new[]{"firefox.exe","Mozilla Firefox"},new[]{"other.exe","Other Browser"},new[]{"chrome.exe","Unknown Product"},new[]{"launcher.exe","Other Launcher"},new[]{"opera.exe",""}}) {
            var browser=DefaultBrowser.Classify(Path.Combine(root,sample[0]),sample[1]);
            check(!browser.SupportsControl&&browser.ProfileKey==null&&browser.Executable.EndsWith(sample[0]),"Não reconhecido usa somente shell padrão, nunca procura Edge: "+sample[0]+" / "+sample[1]);
        }
        var unknown=DefaultBrowser.Unsupported();check(!unknown.SupportsControl&&unknown.Executable==null&&unknown.ProfileKey==null,"Associação sem executável conserva fallback shell do Windows");
        var actual=DefaultBrowser.Resolve();
        Safe.WriteJson(Path.Combine(root,"resultado.json"),new{Passed=passed.Count,Tests=passed,DetectedName=actual.Name,DetectedExecutable=actual.Executable,DetectedSupportsControl=actual.SupportsControl,AssociationChanged=false,BrowserLaunched=false,NetworkUsed=false,RealGameTouched=false});File.WriteAllText(Path.Combine(parent,"ultimo-teste-default-browser.txt"),root);
    }
}
