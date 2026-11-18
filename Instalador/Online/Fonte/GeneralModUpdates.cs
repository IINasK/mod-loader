using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

// Metadata is advisory. It never authorizes installation, ownership or a hash change.
internal sealed class NexusUpdateResult {
    internal string State,Version,Url,SourceVersion;
    internal static NexusUpdateResult Unknown(string state){return new NexusUpdateResult{State=state};}
}

internal static class NexusCredentials {
    internal static string Read(string root=null){
        string key=Environment.GetEnvironmentVariable("MOD_LOADER_NEXUS_API_KEY");
        if(Valid(key))return key;
        try{
            string path=Path.Combine(root??GeneralAppPaths.Current().Data,"nexus-key.dat");Safe.NoLinks(path);
            if(!File.Exists(path)||new FileInfo(path).Length>4096)return null;
            key=Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path),null,DataProtectionScope.CurrentUser));
            return Valid(key)?key:null;
        }catch(IOException){return null;}catch(UnauthorizedAccessException){return null;}catch(CryptographicException){return null;}
    }
    internal static bool Valid(string key){return !String.IsNullOrWhiteSpace(key)&&key.Length<=256&&!key.Any(c=>Char.IsControl(c)||Char.IsWhiteSpace(c));}
    internal static void Save(string root,string key){
        if(!Valid(key))throw new IOException("Chave Nexus inválida.");
        string path=Path.Combine(root,"nexus-key.dat");Safe.NoLinks(path);Directory.CreateDirectory(root);
        byte[] encrypted=ProtectedData.Protect(Encoding.UTF8.GetBytes(key),null,DataProtectionScope.CurrentUser);
        // Small per-user credential, never stored in a project or exported in a pack.
        using(var file=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None))file.Write(encrypted,0,encrypted.Length);
    }
}

internal enum NexusKeyCheck { Accepted, Rejected, Unavailable }

internal static partial class GeneralNexusInfo {
    internal static Task<NexusKeyCheck> CheckPersonalKey(string key){
        return Task.Run(()=>{
            HttpStatusCode status;
            string json=ReadJsonUrl(new Uri(ApiHost+"/v1/users/validate.json"),key,out status,3500);
            if(status==HttpStatusCode.Unauthorized||status==HttpStatusCode.Forbidden)return NexusKeyCheck.Rejected;
            return status==HttpStatusCode.OK&&json!=null?NexusKeyCheck.Accepted:NexusKeyCheck.Unavailable;
        });
    }
    internal static string ReadUpdateFiles(GeneralNexusLink link,string key,out HttpStatusCode status){
        return ReadJsonUrl(new Uri(ApiHost+"/v1/games/"+link.Game+"/mods/"+link.Mod+"/files.json"),key,out status,3500);
    }
}

internal sealed class NexusUpdateSession {
    static readonly object sessionGate=new object();
    static NexusUpdateSession shared;
    internal static NexusUpdateSession Session {
        get{lock(sessionGate){string key=NexusCredentials.Read();if(shared==null||shared.credential!=key)shared=new NexusUpdateSession(key);return shared;}}
    }
    internal static NexusUpdateSession Fresh(){return new NexusUpdateSession(NexusCredentials.Read());}
    internal delegate string Fetch(GeneralNexusLink link,string key,out HttpStatusCode status);
    sealed class Response {internal FilesResponse Files;internal string Failure;}
    readonly Dictionary<string,Task<Response>> pages=new Dictionary<string,Task<Response>>(StringComparer.Ordinal);
    readonly SemaphoreSlim budget=new SemaphoreSlim(2);
    readonly Fetch fetch;
    readonly string credential;
    long requestMilliseconds;
    int retainedEntries;
    string stopped;
    internal NexusUpdateSession(string key,Fetch fetch=null){credential=key;this.fetch=fetch??GeneralNexusInfo.ReadUpdateFiles;}
    internal bool HasCredential {get{return NexusCredentials.Valid(credential);}}
    internal async Task<NexusUpdateResult> Check(GeneralNexusLink link,Func<bool> active){
        if(link==null)return NexusUpdateResult.Unknown("no-source");
        if(!active())return NexusUpdateResult.Unknown("cancelled");
        if(!HasCredential)return NexusUpdateResult.Unknown("connect");
        if(link.File<=0)return NexusUpdateResult.Unknown("choose-file");
        Task<Response> page;
        string id=link.Game+"/"+link.Mod;
        lock(pages){
            if(!pages.TryGetValue(id,out page)){
                if(pages.Count>=256)return NexusUpdateResult.Unknown("budget");
                pages[id]=page=Read(link);
            }
        }
        var result=await page.ConfigureAwait(false);
        if(!active())return NexusUpdateResult.Unknown("cancelled");
        return result.Failure==null?Evaluate(result.Files,link):NexusUpdateResult.Unknown(result.Failure);
    }
    async Task<Response> Read(GeneralNexusLink link){
        await budget.WaitAsync().ConfigureAwait(false);
        try{
            if(stopped!=null)return new Response{Failure=stopped};
            if(Interlocked.Read(ref requestMilliseconds)>120000)return new Response{Failure="budget"};
            return await Task.Run(()=>{
                HttpStatusCode status;string json;
                var elapsed=Stopwatch.StartNew();
                try{json=fetch(link,credential,out status);}catch(WebException){return new Response{Failure="unavailable"};}
                catch(IOException){return new Response{Failure="unavailable"};}
                finally{Interlocked.Add(ref requestMilliseconds,elapsed.ElapsedMilliseconds);}
                if((int)status==429)stopped="rate-limit";
                if(status==HttpStatusCode.Unauthorized||status==HttpStatusCode.Forbidden)stopped="authentication";
                string failure=stopped??(status==HttpStatusCode.OK&&json!=null?null:"unavailable");
                var files=failure==null?Decode(json):null;
                if(failure==null&&files==null)failure="unavailable";
                // Retain only tiny identity/version fields, never page descriptions or response bodies.
                if(files!=null&&Interlocked.Add(ref retainedEntries,files.files.Length+files.file_updates.Length)>50000)failure="budget";
                return new Response{Files=failure==null?files:null,Failure=failure};
            }).ConfigureAwait(false);
        }finally{budget.Release();}
    }
    internal static NexusUpdateResult Parse(string json,GeneralNexusLink link){
        return Evaluate(Decode(json),link);
    }
    static FilesResponse Decode(string json){
        try{
            if(json==null||json.Length>4*1024*1024)return null;
            var response=Safe.Json.Deserialize<FilesResponse>(json);
            if(response==null||response.files==null||response.files.Length>10000||response.files.Any(f=>f==null||f.file_id<=0)||
                response.files.Select(f=>f.file_id).Distinct().Count()!=response.files.Length)return null;
            response.file_updates=response.file_updates??new FileUpdate[0];
            if(response.files.Any(file=>file.version!=null&&(file.version.Length>120||file.version.Any(Char.IsControl)))||
                response.file_updates.Length>10000||response.file_updates.Any(x=>x==null||x.old_file_id<=0||x.new_file_id<=0))return null;
            return response;
        }catch(ArgumentException){return null;}catch(InvalidOperationException){return null;}
    }
    static NexusUpdateResult Evaluate(FilesResponse response,GeneralNexusLink link){
        try{
            if(response==null||link==null)return NexusUpdateResult.Unknown("unavailable");
            if(link.File<=0)return NexusUpdateResult.Unknown("choose-file");
            var files=response.files.ToDictionary(f=>f.file_id);
            FileInfoItem source;
            files.TryGetValue(link.File,out source);
            var updates=response.file_updates??new FileUpdate[0];
            if(updates.Length>10000||updates.Any(x=>x==null||x.old_file_id<=0||x.new_file_id<=0))return NexusUpdateResult.Unknown("unavailable");
            int current=link.File;var seen=new HashSet<int>();
            while(seen.Add(current)){
                var next=updates.Where(u=>u.old_file_id==current).Select(u=>u.new_file_id).Distinct().ToArray();
                if(next.Length>1)return NexusUpdateResult.Unknown("ambiguous");
                if(next.Length==0){
                    FileInfoItem file;
                    if(!files.TryGetValue(current,out file)||!new[]{1,2,3}.Contains(file.category_id))return NexusUpdateResult.Unknown("unavailable");
                    if(file.version!=null&&(file.version.Length>120||file.version.Any(Char.IsControl)))return NexusUpdateResult.Unknown("unavailable");
                    return new NexusUpdateResult{State=current==link.File?"current":"update",Version=file.version,SourceVersion=source==null?null:source.version,
                        Url=new GeneralNexusLink{Game=link.Game,Mod=link.Mod,File=current}.Url};
                }
                current=next[0];
            }
            return NexusUpdateResult.Unknown("ambiguous");
        }catch(ArgumentException){return NexusUpdateResult.Unknown("unavailable");}catch(InvalidOperationException){return NexusUpdateResult.Unknown("unavailable");}
    }
    public sealed class FilesResponse {public FileInfoItem[] files {get;set;}public FileUpdate[] file_updates {get;set;}}
    public sealed class FileInfoItem {public int file_id {get;set;}public int category_id {get;set;}public string version {get;set;}}
    public sealed class FileUpdate {public int old_file_id {get;set;}public int new_file_id {get;set;}}
}

internal sealed class GithubModUpdateSession {
    internal static readonly GithubModUpdateSession Session=new GithubModUpdateSession();
    sealed class Lookup {internal DateTime Expires;internal Task<GeneralUpdateChecker.Release> Result;}
    readonly Dictionary<string,Lookup> releases=new Dictionary<string,Lookup>(StringComparer.OrdinalIgnoreCase);
    readonly SemaphoreSlim workers=new SemaphoreSlim(2);
    readonly Func<Uri,string> fetch;

    internal GithubModUpdateSession(Func<Uri,string> fetch=null){
        this.fetch=fetch??GeneralUpdateChecker.ReadGithubRelease;
    }

    static Match Download(string source){
        Uri link;
        if(!Uri.TryCreate(source,UriKind.Absolute,out link)||link.Scheme!="https"||link.Host!="github.com"||
            !link.IsDefaultPort||!String.IsNullOrEmpty(link.UserInfo)||link.Query.Length!=0||link.Fragment.Length!=0)return null;
        var match=Regex.Match(link.AbsolutePath,@"\A/(?<repo>[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9_.-]{1,100})/releases/download/(?<tag>[A-Za-z0-9][A-Za-z0-9_.-]{0,119})/[^/]+\z");
        return match.Success?match:null;
    }

    internal static bool Supports(string source){return Download(source)!=null;}

    internal async Task<NexusUpdateResult> Check(string source,Func<bool> active){
        var match=Download(source);
        if(match==null)return NexusUpdateResult.Unknown("no-source");
        if(!active())return NexusUpdateResult.Unknown("cancelled");
        string repository=match.Groups["repo"].Value;
        string tag=match.Groups["tag"].Value;
        if(ModVersionNumbers.Compare(tag,tag)==null)return NexusUpdateResult.Unknown("ambiguous");
        Lookup lookup;
        lock(releases){
            var now=DateTime.UtcNow;
            if(!releases.TryGetValue(repository,out lookup)||now>=lookup.Expires){
                foreach(string expired in releases.Where(pair=>now>=pair.Value.Expires).Select(pair=>pair.Key).ToArray())
                    releases.Remove(expired);
                if(releases.Count>=48)return NexusUpdateResult.Unknown("budget");
                releases[repository]=lookup=new Lookup{Expires=now.AddMinutes(10),Result=Read(repository)};
            }
        }
        var release=await lookup.Result.ConfigureAwait(false);
        if(!active())return NexusUpdateResult.Unknown("cancelled");
        if(release==null)return NexusUpdateResult.Unknown("unavailable");
        int? newer=ModVersionNumbers.Compare(release.tag_name,tag);
        if(!newer.HasValue||newer.Value<0)return NexusUpdateResult.Unknown("ambiguous");
        return new NexusUpdateResult{State=newer.Value>0?"update":"current",Version=release.tag_name,
            SourceVersion=tag,Url="https://github.com/"+repository+"/releases/tag/"+Uri.EscapeDataString(release.tag_name)};
    }

    async Task<GeneralUpdateChecker.Release> Read(string repository){
        await workers.WaitAsync().ConfigureAwait(false);
        try{
            var endpoint=new Uri("https://api.github.com/repos/"+repository+"/releases/latest");
            string json=await Task.Run(()=>fetch(endpoint)).ConfigureAwait(false);
            if(String.IsNullOrEmpty(json))return null;
            var release=Safe.Json.Deserialize<GeneralUpdateChecker.Release>(json);
            return release!=null&&!release.draft&&!release.prerelease&&release.tag_name!=null&&release.tag_name.Length<=120?release:null;
        }catch(WebException){return null;}catch(IOException){return null;}
        catch(ArgumentException){return null;}catch(InvalidOperationException){return null;}
        finally{workers.Release();}
    }
}

internal static class ModVersionNumbers {
    // Arbitrary author labels cannot be safely ordered. Never parse dates or suffixes as release versions.
    internal static int? Compare(string left,string right){
        var a=Parts(left);var b=Parts(right);if(a==null||b==null)return null;
        for(int i=0;i<Math.Max(a.Length,b.Length);i++){
            int n=(i<a.Length?a[i]:0).CompareTo(i<b.Length?b[i]:0);if(n!=0)return n;
        }
        return 0;
    }
    static int[] Parts(string value){
        if(value==null||!Regex.IsMatch(value.Trim(),@"\Av?[0-9]{1,8}(?:\.[0-9]{1,8}){1,3}\z"))return null;
        return value.Trim().TrimStart('v').Split('.').Select(Int32.Parse).ToArray();
    }
    internal static string ReadLocal(string root,IEnumerable<string> paths){
        var versions=new HashSet<string>(StringComparer.Ordinal);
        foreach(string relative in paths.Distinct(StringComparer.OrdinalIgnoreCase).Take(256)){
            if(GeneralInstallRoutes.External(relative))continue;
            string ext=Path.GetExtension(relative).ToLowerInvariant();if(ext!=".dll"&&ext!=".exe"&&ext!=".asi")continue;
            try{
                string path=Safe.In(root,relative);Safe.NoLinks(path);if(!File.Exists(path))continue;
                var info=FileVersionInfo.GetVersionInfo(path);
                string version=info.ProductVersion;
                if(Parts(version)==null)version=info.FileVersion;
                if(Parts(version)!=null)versions.Add(version.Trim());
            }catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}
        }
        return versions.Count==1?versions.Single():null;
    }
}
