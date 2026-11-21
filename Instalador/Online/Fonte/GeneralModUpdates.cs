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
    internal string[] ReviewArchives;
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
        var result=await Page(link).ConfigureAwait(false);
        if(!active())return NexusUpdateResult.Unknown("cancelled");
        return result.Failure==null?Evaluate(result.Files,link):NexusUpdateResult.Unknown(result.Failure);
    }
    Task<Response> Page(GeneralNexusLink link){
        Task<Response> page;
        string id=link.Game+"/"+link.Mod;
        lock(pages){
            if(!pages.TryGetValue(id,out page)){
                if(pages.Count>=256)return Task.FromResult(new Response{Failure="budget"});
                pages[id]=page=Read(link);
            }
        }
        return page;
    }

    internal sealed class DependencyFiles {
        internal FileInfoItem[] Choices=new FileInfoItem[0];
        internal FileInfoItem Automatic;
        internal string Failure;
    }
    internal async Task<DependencyFiles> DependencyChoices(GeneralNexusLink link,int pinned,
        GeneralNexusRequirement[][] required,Func<bool> active){
        if(!active())return new DependencyFiles{Failure="cancelled"};
        if(!HasCredential)return new DependencyFiles{Failure="connect"};
        var result=await Page(link).ConfigureAwait(false);
        if(!active()||result.Failure!=null)return new DependencyFiles{Failure=result.Failure??"cancelled"};
        var published=result.Files.files.Where(file=>file.category_id>=1&&file.category_id<=3&&
            !String.IsNullOrWhiteSpace(file.file_name)&&file.file_name.Length<=300&&!file.file_name.Any(Char.IsControl)&&
            GeneralNexusUpdatePackage.ValidFilename(file.file_name)&&(pinned<=0||file.file_id==pinned)).ToArray();
        var choices=new DependencyFiles{Choices=published};
        if(required.Length==0){
            // The published archive list already excludes archived and removed editions.
            // A patch or an optional archive alone cannot stand in for an unpinned main file.
            if(published.Length==1&&(pinned>0||published[0].category_id==1))choices.Automatic=published[0];
            return choices;
        }
        // Follow the publisher's replacement edges from each required minimum and stop at its maximum.
        var successors=result.Files.file_updates.ToLookup(edge=>edge.old_file_id,edge=>edge.new_file_id);
        HashSet<int> compatible=null;
        foreach(var alternatives in required){
            var union=new HashSet<int>();
            foreach(var range in alternatives){
                var visited=new HashSet<int>();
                int current=range.MinFile;
                bool reachedMaximum=range.MaxFile==0,ambiguous=false;
                while(current>0){
                    if(!visited.Add(current)){ambiguous=true;break;}
                    if(current==range.MaxFile){reachedMaximum=true;break;}
                    var next=successors[current].Distinct().Take(2).ToArray();
                    if(next.Length>1){ambiguous=true;break;}
                    current=next.Length==1?next[0]:0;
                }
                if(ambiguous||!reachedMaximum)union.Add(range.MinFile);
                else union.UnionWith(visited);
                if(range.MaxFile>0)union.Add(range.MaxFile);
            }
            if(compatible==null)compatible=union;else compatible.IntersectWith(union);
        }
        var proven=published.Where(file=>compatible.Contains(file.file_id)).ToArray();
        var availableIds=new HashSet<int>(proven.Select(file=>file.file_id));
        var newest=proven.Where(file=>!successors[file.file_id].Any(availableIds.Contains)).ToArray();
        choices.Choices=newest.Length>0?newest:proven;
        if(choices.Choices.Length==1)choices.Automatic=choices.Choices[0];
        if(choices.Choices.Length==0)choices.Failure="no-compatible-file";
        return choices;
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
    public sealed class FileInfoItem {public int file_id {get;set;}public int category_id {get;set;}public string version {get;set;}public string file_name {get;set;}}
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

    internal static bool Supports(string source){return GeneralGithubLink.Parse(source)!=null;}

    internal async Task<NexusUpdateResult> Check(string source,Func<bool> active){
        var link=GeneralGithubLink.Parse(source);
        if(link==null)return NexusUpdateResult.Unknown("no-source");
        if(!active())return NexusUpdateResult.Unknown("cancelled");
        string repository=link.Repository;
        string tag=link.Tag;
        if(tag!=null&&ModVersionNumbers.Compare(tag,tag)==null)return NexusUpdateResult.Unknown("ambiguous");
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
        if(tag==null)return new NexusUpdateResult{State="choose-file",Version=release.tag_name,Url=link.Page};
        int? newer=ModVersionNumbers.Compare(release.tag_name,tag);
        if(!newer.HasValue||newer.Value<0)return NexusUpdateResult.Unknown("ambiguous");
        string successor=GeneralGithubUpdatePackage.Successor(link,release);
        var review=newer>0&&successor==null?GeneralGithubUpdatePackage.ReleaseAssets(repository,release)
            .Where(asset=>GeneralGithubUpdatePackage.Digest(asset)!=null)
            .Select(asset=>GeneralGithubLink.Parse(asset.browser_download_url).Url).Distinct(StringComparer.Ordinal).ToArray():null;
        return new NexusUpdateResult{State=newer.Value>0?"update":"current",Version=release.tag_name,ReviewArchives=review,
            SourceVersion=tag,Url=successor??
                "https://github.com/"+repository+"/releases/tag/"+Uri.EscapeDataString(release.tag_name)};
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
