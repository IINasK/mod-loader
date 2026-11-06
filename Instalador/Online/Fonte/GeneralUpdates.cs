using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class GeneralUpdateRelease {
    internal string Version, Page, Url, Sha256;
    internal long Size;
}

internal sealed class GeneralUpdateChecker {
    internal const string CurrentVersion="1.1.27";
    internal static readonly GeneralUpdateChecker Session=new GeneralUpdateChecker();
    internal const string Repository="IINasK/mod-loader";
    internal const string Endpoint="https://api.github.com/repos/"+Repository+"/releases/latest";
    const int MaxResponse=262144;
    readonly Func<Task<string>> fetch;
    readonly object gate=new object();
    Task<GeneralUpdateRelease> check;
    internal GeneralUpdateChecker(Func<Task<string>> fetch=null){this.fetch=fetch??(()=>Task.Run((Func<string>)ReadLatest));}
    internal Task<GeneralUpdateRelease> CheckOnce(){lock(gate){return check??(check=Check());}}
    async Task<GeneralUpdateRelease> Check(){
        try{return Parse(await fetch().ConfigureAwait(false),CurrentVersion);}
        catch(WebException){return null;}catch(IOException){return null;}catch(ArgumentException){return null;}catch(InvalidOperationException){return null;}
    }
    static string ReadLatest(){
        ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
        var request=(HttpWebRequest)WebRequest.Create(Endpoint);
        request.Method="GET";request.UserAgent="ModLoader/"+CurrentVersion;
        request.Accept="application/vnd.github+json";request.Headers["X-GitHub-Api-Version"]="2026-03-10";
        request.Timeout=3000;request.ReadWriteTimeout=3000;request.AllowAutoRedirect=false;
        request.Credentials=null;request.UseDefaultCredentials=false;
        using(var response=(HttpWebResponse)request.GetResponse()){
            if(response.StatusCode!=HttpStatusCode.OK||response.ContentLength>MaxResponse)return null;
            using(var stream=response.GetResponseStream())return ReadLimited(stream);
        }
    }
    internal static string ReadLimited(Stream stream){
        using(var bytes=new MemoryStream()){
            var buffer=new byte[8192];int count;
            while((count=stream.Read(buffer,0,buffer.Length))>0){if(bytes.Length+count>MaxResponse)throw new IOException("Resposta de atualização grande demais.");bytes.Write(buffer,0,count);}
            return new UTF8Encoding(false,true).GetString(bytes.ToArray());
        }
    }
    internal sealed class Release {
        public string tag_name {get;set;}
        public bool draft {get;set;}
        public bool prerelease {get;set;}
        public Asset[] assets {get;set;}
    }
    internal sealed class Asset {public string digest {get;set;}public string name {get;set;}public string state {get;set;}public string browser_download_url {get;set;}public long size {get;set;}}
    internal static Version VersionNumber(string tag){
        if(tag==null||!Regex.IsMatch(tag,@"\Av?[0-9]{1,6}\.[0-9]{1,6}\.[0-9]{1,6}\z"))return null;
        Version result;return Version.TryParse(tag.TrimStart('v'),out result)?result:null;
    }
    internal static GeneralUpdateRelease Parse(string json,string current){
        if(String.IsNullOrWhiteSpace(json)||json.Length>MaxResponse)return null;
        var release=Safe.Json.Deserialize<Release>(json);
        if(release==null||release.draft||release.prerelease||release.assets==null)return null;
        var version=VersionNumber(release.tag_name);var installed=VersionNumber(current);
        if(version==null||installed==null||version.CompareTo(installed)<=0)return null;
        string number=version.ToString(3);
        string prefix="https://github.com/"+Repository+"/releases/download/"+Uri.EscapeDataString(release.tag_name)+"/";
        bool ready=release.assets.Any(asset=>asset!=null&&asset.state=="uploaded"&&asset.size>0&&asset.size<=268435456&&
            (asset.name=="Mod Loader "+number+".exe"||asset.name=="Mod-Loader-"+number+".exe")&&
            asset.browser_download_url==prefix+Uri.EscapeDataString(asset.name));
        if(!ready)return null;
        var result=new GeneralUpdateRelease{Version=number,Page="https://github.com/"+Repository+"/releases/tag/"+Uri.EscapeDataString(release.tag_name)};
        var setup=release.assets.FirstOrDefault(asset=>asset!=null&&asset.state=="uploaded"&&asset.size>0&&asset.size<=268435456&&asset.name=="Mod-Loader-"+number+".exe"&&asset.browser_download_url==prefix+Uri.EscapeDataString(asset.name)&&Regex.IsMatch(asset.digest??"",@"\Asha256:[a-f0-9]{64}\z"));
        if(setup!=null){result.Url=setup.browser_download_url;result.Size=setup.size;result.Sha256=setup.digest.Substring(7);}return result;
    }
}

internal sealed partial class GeneralModWindow {
    readonly GeneralUpdateChecker updateChecker=GeneralUpdateChecker.Session;
    GeneralUpdateRelease updateAvailable;
    LinkLabel updateNotice;
    void AddUpdateNotice(Control parent){
        updateNotice=new LinkLabel{AutoSize=true,Font=GeneralStyle.Font(10.5f,true),LinkColor=GeneralStyle.Accent,ActiveLinkColor=GeneralStyle.Foreground,
            VisitedLinkColor=GeneralStyle.Accent,Margin=new Padding(20,0,0,0),Visible=updateAvailable!=null,Name="UpdateNotice"};
        updateNotice.LinkClicked+=(s,e)=>{if(updateAvailable!=null)Guard(()=>launch(updateAvailable.Page));};
        parent.Controls.Add(updateNotice);ShowUpdateNotice();
    }
    void ShowUpdateNotice(){
        if(updateNotice==null||updateNotice.IsDisposed)return;
        updateNotice.Visible=updateAvailable!=null;
        if(updateAvailable!=null)updateNotice.Text=T("Nova versão ","New version ")+updateAvailable.Version+T(" — baixar"," — download");
    }
    async void CheckUpdatesOnStartup(){
        if(testMode)return;
        var available=await updateChecker.CheckOnce();
        if(IsDisposed||Disposing)return;
        updateAvailable=available;ShowUpdateNotice();
    }
}
