using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

public sealed class DefaultBrowserInfo {
    public readonly string Executable,Name,ProfileKey;
    public readonly bool SupportsControl;
    internal DefaultBrowserInfo(string executable,string name,string profileKey,bool supported){Executable=executable;Name=name;ProfileKey=profileKey;SupportsControl=supported;}
}

public static class DefaultBrowser {
    const uint IsProtocol=0x1000,Executable=2;
    [DllImport("Shlwapi.dll",CharSet=CharSet.Unicode)]
    static extern int AssocQueryString(uint flags,uint str,string association,string verb,StringBuilder output,ref uint characters);
    static string QueryExecutable() {
        uint length=0;AssocQueryString(IsProtocol,Executable,"https","open",null,ref length);
        if(length==0||length>32768)return null;
        var value=new StringBuilder((int)length);
        return AssocQueryString(IsProtocol,Executable,"https","open",value,ref length)==0?value.ToString():null;
    }
    public static DefaultBrowserInfo Resolve() {
        try {
            string path=QueryExecutable();
            if(String.IsNullOrWhiteSpace(path)||!Path.IsPathRooted(path)||path.StartsWith(@"\\",StringComparison.Ordinal)||path.Any(Char.IsControl))return Unsupported();
            path=Path.GetFullPath(path);
            if(!String.Equals(Path.GetExtension(path),".exe",StringComparison.OrdinalIgnoreCase)||!File.Exists(path))return Unsupported();
            Safe.NoLinks(path);return Classify(path,FileVersionInfo.GetVersionInfo(path).ProductName);
        }catch(Exception ex) {
            if(ex is IOException||ex is UnauthorizedAccessException||ex is ArgumentException||ex is DllNotFoundException||ex is EntryPointNotFoundException)return Unsupported();
            throw;
        }
    }
    internal static DefaultBrowserInfo Unsupported(){return new DefaultBrowserInfo(null,"navegador padrão do Windows",null,false);}
    internal static DefaultBrowserInfo Classify(string executable,string productName) {
        string file=Path.GetFileName(executable??"").ToLowerInvariant(),product=(productName??"").Trim();
        string name=null,key=null;
        if(file=="chrome.exe"&&(product.Equals("Google Chrome",StringComparison.OrdinalIgnoreCase)||product.Equals("Chromium",StringComparison.OrdinalIgnoreCase))){name=product;key="Chrome";}
        else if(file=="msedge.exe"&&product.Equals("Microsoft Edge",StringComparison.OrdinalIgnoreCase)){name="Microsoft Edge";key="Edge";}
        else if(file=="brave.exe"&&(product.Equals("Brave",StringComparison.OrdinalIgnoreCase)||product.Equals("Brave Browser",StringComparison.OrdinalIgnoreCase))){name="Brave";key="Brave";}
        else if(file=="vivaldi.exe"&&product.Equals("Vivaldi",StringComparison.OrdinalIgnoreCase)){name="Vivaldi";key="Vivaldi";}
        else if((file=="opera.exe"||file=="launcher.exe")&&(product.Equals("Opera",StringComparison.OrdinalIgnoreCase)||product.Equals("Opera Internet Browser",StringComparison.OrdinalIgnoreCase)||product.Equals("Opera GX",StringComparison.OrdinalIgnoreCase)||product.Equals("Opera GX Browser",StringComparison.OrdinalIgnoreCase)||product.Equals("Opera GX Internet Browser",StringComparison.OrdinalIgnoreCase)||product.Equals("Opera Browser",StringComparison.OrdinalIgnoreCase))){name=product.IndexOf("GX",StringComparison.OrdinalIgnoreCase)>=0?"Opera GX":"Opera";key=name=="Opera GX"?"OperaGX":"Opera";}
        if(key!=null)return new DefaultBrowserInfo(executable,name,key,true);
        string unsupported=file=="firefox.exe"?"Firefox":"navegador padrão do Windows";
        return new DefaultBrowserInfo(executable,unsupported,null,false);
    }
}
