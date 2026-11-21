using System;
using System.IO;
using System.Collections.Generic;
using System.Net;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

// Author links are discovery hints, never proof of authorship or file identity.
// Both providers must independently identify the exact bytes before committing.
internal static class GeneralNexusGithubMirror {
    internal static string Unique(IEnumerable<string> links){
        var distinct=links.Where(link=>link!=null).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
        return distinct.Length==1?distinct[0]:null;
    }
    internal static string NexusReference(string description){
        var links=Regex.Matches(description??"", @"https://(?:www\.)?nexusmods\.com/(?:games/)?[a-zA-Z0-9_-]+/mods/[0-9]+\b");
        return Unique(links.Cast<Match>().Select(m=>GeneralNexusLink.Parse(m.Value)).Where(link=>link!=null).Select(link=>link.Url));
    }
    static readonly TimeSpan LookupLifetime=TimeSpan.FromDays(1);

    internal static void ExpireLookups(string project){
        try{
            string folder=Safe.In(project,"GithubUpdateMetadata");Safe.NoLinks(folder);
            if(!Directory.Exists(folder))return;
            var expires=DateTime.UtcNow-LookupLifetime;
            var files=new DirectoryInfo(folder).EnumerateFiles("*.json")
                .Where(file=>FixedData.HashValid(Path.GetFileNameWithoutExtension(file.Name)))
                .OrderByDescending(file=>file.LastWriteTimeUtc).ToArray();
            for(int i=0;i<files.Length;i++){
                var file=files[i];
                if(i<256&&file.LastWriteTimeUtc>expires)continue;
                Safe.NoLinks(file.FullName);File.Delete(file.FullName);
            }
            if(!Directory.EnumerateFileSystemEntries(folder).Any())Directory.Delete(folder,false);
        }catch(IOException){}catch(UnauthorizedAccessException){}
    }

    internal static string ReadCached(string project,Uri endpoint,Func<Uri,string> fetch=null){
        string file=Safe.In(project,"GithubUpdateMetadata/"+
            InstalledSnapshot.Hash(System.Text.Encoding.UTF8.GetBytes(endpoint.AbsoluteUri))+".json");
        try{
            Safe.NoLinks(file);
            var stored=new FileInfo(file);
            if(stored.Exists&&stored.LastWriteTimeUtc>DateTime.UtcNow-LookupLifetime&&stored.Length<=1024*1024){
                string saved=Safe.Json.Deserialize<string>(File.ReadAllText(file));
                if(!String.IsNullOrEmpty(saved))return saved;
            }
        }catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}catch(InvalidOperationException){}
        var response=(fetch??ReadGithub)(endpoint);
        if(!String.IsNullOrWhiteSpace(response)&&response.Length<=262144){
            try{Safe.NoLinks(file);Directory.CreateDirectory(Path.GetDirectoryName(file));Safe.WriteJson(file,response);}
            catch(IOException){}catch(UnauthorizedAccessException){}
        }
        return response;
    }

    internal static string ReadGithub(Uri endpoint){
        try{return GeneralUpdateChecker.ReadGithubRelease(endpoint);}
        catch(WebException ex){
            using(var response=ex.Response as HttpWebResponse){
                if(IsQuotaResponse(response))
                    throw GeneralGithubUpdatePackage.Failure("rate-limit");
            }
            throw;
        }
    }
    static bool IsQuotaResponse(HttpWebResponse response){
        if(response==null)return false;
        if((int)response.StatusCode==429)return true;
        if(response.StatusCode!=HttpStatusCode.Forbidden)return false;
        if(response.Headers["Retry-After"]!=null||response.Headers["X-RateLimit-Remaining"]=="0")return true;
        using(var stream=response.GetResponseStream()){
            if(stream==null)return false;
            using(var reader=new StreamReader(stream)){
                var buffer=new char[4096];int count=reader.ReadBlock(buffer,0,buffer.Length);
                return new string(buffer,0,count).IndexOf("rate limit",StringComparison.OrdinalIgnoreCase)>=0;
            }
        }
    }
    sealed class Results {public Repository[] items {get;set;}}
    sealed class Repository {public string full_name {get;set;}}
    internal static async Task<GeneralGithubUpdatePackage> Resolve(GeneralNexusUpdatePackage nexus,string name,
        IEnumerable<string> references,Func<bool> active,Func<Uri,string> fetch){
        var file=nexus.Info;
        if(file.Bytes<=0||file.SizeApproximate||String.IsNullOrWhiteSpace(file.Version)&&String.IsNullOrWhiteSpace(file.ModVersion))return null;
        fetch=fetch??ReadGithub;
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var match=await Match(nexus,references,seen,active,fetch).ConfigureAwait(false);
        if(match!=null||seen.Count>=3)return match;
        var declared=await nexus.DeclaredGithubRepositories(active).ConfigureAwait(false);
        match=await Match(nexus,declared,seen,active,fetch).ConfigureAwait(false);
        if(match!=null||seen.Count>=3||String.IsNullOrWhiteSpace(name))return match;
        if(!active())throw new OperationCanceledException();
        string query=Uri.EscapeDataString(Regex.Replace(nexus.ModName??name,@"[^\p{L}\p{N} ._-]"," ").Trim()+" in:name");
        var json=await Task.Run(()=>fetch(new Uri("https://api.github.com/search/repositories?per_page=3&q="+query))).ConfigureAwait(false);
        if(!active())throw new OperationCanceledException();
        Results results;
        try{results=Safe.Json.Deserialize<Results>(json??"{}");}
        catch(ArgumentException){return null;}catch(InvalidOperationException){return null;}
        return await Match(nexus,results==null||results.items==null?new string[0]:
            results.items.Where(item=>item!=null&&item.full_name!=null)
                .OrderByDescending(item=>String.Equals(item.full_name.Split('/')[0],nexus.Author,StringComparison.OrdinalIgnoreCase))
                .Select(item=>"https://github.com/"+item.full_name),seen,active,fetch).ConfigureAwait(false);
    }
    static async Task<GeneralGithubUpdatePackage> Match(GeneralNexusUpdatePackage nexus,IEnumerable<string> urls,
        HashSet<string> seen,Func<bool> active,Func<Uri,string> fetch){
        GeneralGithubUpdatePackage found=null;
        foreach(var url in urls){
            if(!active())throw new OperationCanceledException();
            var link=GeneralGithubLink.Parse(url);
            if(link==null||seen.Count>=3||!seen.Add(link.Repository))continue;
            var version=nexus.Info.Version??nexus.Info.ModVersion;
            var versions=new[]{link.Tag,version,"v"+version.TrimStart('v')}.Distinct();
            foreach(var tag in versions.Where(tag=>link.Tag==null||tag==link.Tag)){
                if(!active())throw new OperationCanceledException();
                try{
                    var source=new GeneralGithubLink{Repository=link.Repository,Tag=tag,Asset=link.Asset};
                    var release=await GeneralGithubUpdatePackage.Read(source.Url,active,fetch).ConfigureAwait(false);
                    var candidate=release.MatchNexusFile(nexus.Info);
                    if(candidate==null)continue;
                    if(found!=null)throw new IOException("Mais de um repositório oferece esta variante.");
                    found=candidate;break;
                }catch(SourcePackageException ex){if(ex.State=="rate-limit")throw;}
            }
        }
        return found;
    }
    internal static string[] Repositories(string description){
        if(String.IsNullOrEmpty(description)||description.Length>4*1024*1024)return new string[0];
        var matches=Regex.Matches(description,@"https://github\.com/[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9_.-]{1,100}",RegexOptions.IgnoreCase);
        return matches.Cast<Match>().Take(32).Select(match=>match.Value.TrimEnd('.'))
            .Select(GeneralGithubLink.Parse).Where(link=>link!=null)
            .Select(link=>"https://github.com/"+link.Repository).Distinct(StringComparer.OrdinalIgnoreCase).Take(9).ToArray();
    }
    internal static async Task<VerifiedModPackage> Download(GeneralNexusUpdatePackage nexus,DownloadCache cache,DownloadControl control,
        Action<DownloadProgress> progress,Func<Uri,string> fetch=null){
        Func<bool> active=()=>!control.IsCancelled;
        control.Checkpoint();
        var repositories=await nexus.DeclaredGithubRepositories(active).ConfigureAwait(false);
        // A description often lists dependencies; never pick among competing repositories.
        if(repositories.Length!=1)return null;
        var release=await GeneralGithubUpdatePackage.Read(repositories[0],active,fetch).ConfigureAwait(false);
        var mirror=release.MatchNexusFile(nexus.Info);if(mirror==null)return null;
        return await DownloadConfirmed(nexus,mirror,cache,control,progress).ConfigureAwait(false);
    }
    internal static async Task<VerifiedModPackage> DownloadConfirmed(GeneralNexusUpdatePackage nexus,GeneralGithubUpdatePackage mirror,
        DownloadCache cache,DownloadControl control,Action<DownloadProgress> progress,Action<string> retainPending=null){
        VerifiedModPackage confirmed=null;
        SourcePackageException quota=null;
        var downloaded=await mirror.Download(cache,control,progress,path=>{
            try{confirmed=nexus.VerifyDownloaded(path,()=>!control.IsCancelled).GetAwaiter().GetResult();}
            catch(SourcePackageException ex){
                if(ex.State!="rate-limit"||retainPending==null)throw;
                quota=ex;
            }
        }).ConfigureAwait(false);
        if(quota!=null&&downloaded!=null){control.Checkpoint();retainPending(downloaded.Path);throw quota;}
        if(downloaded==null||confirmed==null||downloaded.Sha256!=confirmed.Sha256||downloaded.Bytes!=confirmed.Bytes)
            throw new IOException("O arquivo do GitHub não corresponde ao arquivo confirmado pelo Nexus.");
        // Nexus remains the registered source. GitHub is only the verified transport.
        confirmed.Path=downloaded.Path;
        confirmed.TransportRepository="https://github.com/"+GeneralGithubLink.Parse(mirror.Source).Repository;
        return confirmed;
    }
}
