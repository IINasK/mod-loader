using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

internal sealed class GeneralGithubLink {
    internal string Repository,Tag,Asset;
    internal string Page {get{return "https://github.com/"+Repository+(Tag==null?"/releases":"/releases/tag/"+Uri.EscapeDataString(Tag));}}
    internal string Url {get{return Asset==null?Page:"https://github.com/"+Repository+"/releases/download/"+Uri.EscapeDataString(Tag)+"/"+Uri.EscapeDataString(Asset);}}
    internal static GeneralGithubLink Parse(string value) {
        Uri uri;if(!Uri.TryCreate(value,UriKind.Absolute,out uri)||uri.Scheme!="https"||uri.Host!="github.com"||
            !uri.IsDefaultPort||uri.UserInfo.Length!=0)return null;
        var match=Regex.Match(uri.AbsolutePath,@"\A/(?<repo>[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9_.-]{1,100})(?:/(?<rest>.*))?\z");
        if(!match.Success)return null;
        string rest=match.Groups["rest"].Value.TrimEnd('/');
        if(uri.Query.Length!=0&&!(rest==""&&uri.Query=="?tab=readme-ov-file"))return null;
        var link=new GeneralGithubLink{Repository=match.Groups["repo"].Value};
        if(rest==""||rest=="releases"||rest=="releases/latest")return link;
        string prefix=rest.StartsWith("releases/download/",StringComparison.Ordinal)?"releases/download/":
            rest.StartsWith("releases/tag/",StringComparison.Ordinal)?"releases/tag/":null;
        if(prefix==null)return null;
        string[] parts=rest.Substring(prefix.Length).Split('/');
        if(parts.Length!=(prefix=="releases/download/"?2:1))return null;
        try{link.Tag=Uri.UnescapeDataString(parts[0]);if(parts.Length==2)link.Asset=Uri.UnescapeDataString(parts[1]);}
        catch(UriFormatException){return null;}
        if(String.IsNullOrEmpty(link.Tag)||link.Tag.Length>120||link.Tag.Any(Char.IsControl)||link.Tag.Contains("\\")||
            link.Asset!=null&&!ValidFilename(link.Asset))return null;
        return link;
    }
    internal static bool ValidFilename(string name){return !String.IsNullOrEmpty(name)&&name.Length<=240&&name==Path.GetFileName(name)&&
        name.IndexOfAny(Path.GetInvalidFileNameChars())<0&&name!="."&&name!="..";}
    internal static bool SameRepository(GeneralGithubLink first,GeneralGithubLink second){return first!=null&&second!=null&&
        String.Equals(first.Repository,second.Repository,StringComparison.OrdinalIgnoreCase);}
}

internal sealed class GeneralGithubUpdatePackage {
    const long MaximumBytes=128L*1024*1024*1024;
    readonly GeneralGithubLink link;
    readonly string version;
    readonly GeneralUpdateChecker.Asset[] assets;
    readonly bool automatic;
    internal string Source {get{return link.Url;}}
    internal GeneralSourceInfo Info {get{return assets.Length==1?AssetInfo(assets[0]):new GeneralSourceInfo{Name=link.Repository,Version=version};}}
    GeneralGithubUpdatePackage(GeneralGithubLink link,string version,GeneralUpdateChecker.Asset[] assets,bool automatic=true){this.link=link;this.version=version;this.assets=assets;this.automatic=automatic;}
    internal static Task<GeneralGithubUpdatePackage> Read(string source,Func<bool> active=null,Func<Uri,string> fetch=null){
        return Task.Run(()=>{
            Active(active);var link=GeneralGithubLink.Parse(source);if(link==null)throw Failure("wrong-file");
            var release=ReadRelease(link,active,fetch);
            var candidates=ReleaseAssets(link.Repository,release).Where(asset=>link.Asset==null||asset.name==link.Asset).ToArray();
            if(candidates.Length==0)throw Failure("unavailable");
            var verified=candidates.Where(asset=>Digest(asset)!=null).ToArray();
            if(verified.Length==0)throw Failure("unverified");
            if(verified.Select(asset=>asset.name).Distinct(StringComparer.Ordinal).Count()!=verified.Length)throw Failure("wrong-file");
            return new GeneralGithubUpdatePackage(new GeneralGithubLink{Repository=link.Repository,Tag=release.tag_name,Asset=link.Asset},release.tag_name,verified,link.Asset!=null||candidates.Length==1);
        });
    }
    static GeneralUpdateChecker.Release ReadRelease(GeneralGithubLink link,Func<bool> active,Func<Uri,string> fetch) {
        var endpoint=new Uri("https://api.github.com/repos/"+link.Repository+"/releases/"+(link.Tag==null?"latest":"tags/"+Uri.EscapeDataString(link.Tag)));
        string json;
        try{json=(fetch??GeneralNexusGithubMirror.ReadGithub)(endpoint);}
        catch(SourcePackageException){throw;}
        catch(System.Net.WebException){throw Failure("unavailable");}catch(IOException){throw Failure("unavailable");}
        Active(active);GeneralUpdateChecker.Release release;
        try{release=String.IsNullOrWhiteSpace(json)||json.Length>262144?null:Safe.Json.Deserialize<GeneralUpdateChecker.Release>(json);}
        catch(ArgumentException){throw Failure("unavailable");}catch(InvalidOperationException){throw Failure("unavailable");}
        if(release==null||release.draft||release.prerelease&&link.Tag==null||String.IsNullOrEmpty(release.tag_name)||release.tag_name.Length>120||
            release.tag_name.Any(Char.IsControl)||link.Tag!=null&&link.Tag!=release.tag_name||release.assets==null||release.assets.Length>256)throw Failure("unavailable");
        return release;
    }
    internal static Task<GeneralSourceInfo> ReadSourceInfo(string source,Func<Uri,string> fetch=null) {
        return Task.Run(()=>{
            var link=GeneralGithubLink.Parse(source);if(link==null)return null;
            try {
                var release=ReadRelease(link,null,fetch);
                var choices=ReleaseAssets(link.Repository,release).Where(asset=>link.Asset==null||asset.name==link.Asset).ToArray();
                if(link.Asset!=null&&choices.Length!=1)return null;
                var selected=choices.Length==1?choices[0]:null;
                // Metadata is not proof that these bytes were installed. The
                // verified asset flag is set only by the package verifier.
                return new GeneralSourceInfo{Name=link.Repository.Split('/').Last(),Version=selected==null?null:release.tag_name,
                    Reference=DeclaredNexusReference(link,release.body,fetch),
                    ReleaseChoices=choices,ModVersion=release.tag_name,FileName=selected==null?null:selected.name,Bytes=selected==null?0:selected.size};
            }catch(SourcePackageException){return null;}
        });
    }
    static string DeclaredNexusReference(GeneralGithubLink link,string body,Func<Uri,string> fetch){
        string declared=GeneralNexusGithubMirror.NexusReference(body);
        if(declared!=null)return declared;
        try{
            var request=fetch??GeneralNexusGithubMirror.ReadGithub;
            var url="https://api.github.com/repos/"+link.Repository;
            var repo=Safe.Json.Deserialize<DeclaredLinks>(request(new Uri(url))??"{}");
            declared=GeneralNexusGithubMirror.NexusReference(repo==null?null:repo.homepage);
            if(declared!=null)return declared;
            var readme=Safe.Json.Deserialize<DeclaredLinks>(request(new Uri(url+"/readme"+(link.Tag==null?"":"?ref="+Uri.EscapeDataString(link.Tag))))??"{}");
            if(readme!=null&&readme.encoding=="base64"&&readme.content!=null&&readme.content.Length<=200000)
                return GeneralNexusGithubMirror.NexusReference(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(readme.content)));
        }catch(IOException){}catch(System.Net.WebException){}catch(FormatException){}catch(ArgumentException){}catch(InvalidOperationException){}
        return null;
    }
    sealed class DeclaredLinks {public string homepage {get;set;}public string encoding {get;set;}public string content {get;set;}}
    internal static IEnumerable<GeneralUpdateChecker.Asset> ReleaseAssets(string repository,GeneralUpdateChecker.Release release){
        if(release==null||release.assets==null||release.assets.Length>256)yield break;
        foreach(var asset in release.assets) {
            if(asset==null||asset.state!="uploaded"||asset.size<=0||asset.size>MaximumBytes||!GeneralGithubLink.ValidFilename(asset.name)||
                !new[]{".zip",".rar",".7z"}.Contains(Path.GetExtension(asset.name),StringComparer.OrdinalIgnoreCase))continue;
            var source=GeneralGithubLink.Parse(asset.browser_download_url);
            if(source==null||source.Asset!=asset.name||source.Tag!=release.tag_name||!String.Equals(source.Repository,repository,StringComparison.OrdinalIgnoreCase))continue;
            yield return asset;
        }
    }
    internal static string Digest(GeneralUpdateChecker.Asset asset){return Regex.IsMatch(asset==null?"":asset.digest??"",@"\Asha256:[a-f0-9]{64}\z")?asset.digest.Substring(7):null;}
    internal static bool ValidSourceInfo(string source,GeneralSourceInfo info) {
        if(!info.GithubChecked&&info.GithubAssetUrl==null)return true;
        var original=GeneralGithubLink.Parse(source);var asset=GeneralGithubLink.Parse(info.GithubAssetUrl);
        return GeneralGithubLink.SameRepository(original,asset)&&asset.Asset!=null&&
            (!info.GithubChecked||asset.Tag==info.Version)&&(original.Tag==null||original.Tag==asset.Tag)&&
            (original.Asset==null||original.Asset==asset.Asset);
    }
    internal static string Successor(GeneralGithubLink previous,GeneralUpdateChecker.Release release){
        if(previous==null||previous.Asset==null||release==null)return null;
        var names=new HashSet<string>(StringComparer.Ordinal){previous.Asset};
        if(!String.IsNullOrEmpty(previous.Tag)&&!String.IsNullOrEmpty(release.tag_name)) {
            names.Add(ReplaceVersion(previous.Asset,previous.Tag,release.tag_name));
            if(previous.Tag.StartsWith("v",StringComparison.Ordinal)&&release.tag_name.StartsWith("v",StringComparison.Ordinal))
                names.Add(ReplaceVersion(previous.Asset,previous.Tag.Substring(1),release.tag_name.Substring(1)));
        }
        var matches=ReleaseAssets(previous.Repository,release).Where(asset=>names.Contains(asset.name)&&Digest(asset)!=null).ToArray();
        return matches.Length==1?GeneralGithubLink.Parse(matches[0].browser_download_url).Url:null;
    }
    internal static bool SameAssetLine(GeneralGithubLink previous,GeneralGithubLink next){
        if(previous==null||next==null||String.IsNullOrEmpty(previous.Asset)||String.IsNullOrEmpty(next.Asset)||
            String.IsNullOrEmpty(previous.Tag)||String.IsNullOrEmpty(next.Tag))return false;
        if(previous.Asset==next.Asset||ReplaceVersion(previous.Asset,previous.Tag,next.Tag)==next.Asset)return true;
        return previous.Tag.StartsWith("v",StringComparison.Ordinal)&&next.Tag.StartsWith("v",StringComparison.Ordinal)&&
            ReplaceVersion(previous.Asset,previous.Tag.Substring(1),next.Tag.Substring(1))==next.Asset;
    }
    static string ReplaceVersion(string name,string previous,string next){return Regex.Replace(name,
        @"(?<![0-9A-Za-z.])"+Regex.Escape(previous)+@"(?![0-9A-Za-z]|\.[0-9])",_=>next);}
    internal FixedAsset DownloadAsset {get{
        if(!automatic||assets.Length!=1)return null;var asset=assets[0];
        return new FixedAsset{Id="github-release",Name=asset.name,Format=Path.GetExtension(asset.name).TrimStart('.').ToLowerInvariant(),
            Url=GeneralGithubLink.Parse(asset.browser_download_url).Url,Sha256=Digest(asset),Size=asset.size};
    }}
    internal Task<VerifiedModPackage> Download(DownloadCache cache,DownloadControl control,Action<DownloadProgress> progress,Action<string> validate=null) {
        return Task.Run(()=>{
            var asset=DownloadAsset;if(asset==null)return null;
            new NexusClient().Download(new FixedMod{Id="github-release",Name=Info.Name},asset,cache,control,progress,validate,true);
            control.Checkpoint();return new VerifiedModPackage{Path=cache.PathFor(asset),Bytes=asset.Size,Sha256=asset.Sha256,
                SourceUrl=asset.Url,Info=AssetInfo(assets[0])};
        });
    }
    internal GeneralGithubUpdatePackage MatchNexusFile(GeneralSourceInfo nexus) {
        if(nexus==null||nexus.SizeApproximate||nexus.Bytes<=0||
            !new[]{nexus.Version,nexus.ModVersion}.Any(value=>!String.IsNullOrWhiteSpace(value)&&
                (value==version||ModVersionNumbers.Compare(value,version)==0)))return null;
        var matches=assets.Where(asset=>asset.size==nexus.Bytes).ToArray();
        if(matches.Length>1)matches=matches.Where(asset=>String.Equals(asset.name,nexus.FileName,StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length==1?new GeneralGithubUpdatePackage(new GeneralGithubLink{Repository=link.Repository,Tag=version,Asset=matches[0].name},version,matches):null;
    }
    GeneralSourceInfo AssetInfo(GeneralUpdateChecker.Asset asset){return new GeneralSourceInfo{Name=String.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(asset.name))?link.Repository.Split('/').Last():Path.GetFileNameWithoutExtension(asset.name),FileName=asset.name,
        Version=version,Bytes=asset.size,GithubChecked=true,GithubAssetUrl=GeneralGithubLink.Parse(asset.browser_download_url).Url};}
    internal IEnumerable<string> Candidates(string configured){
        var found=new List<string>();
        foreach(string folder in GeneralModDownload.WatchFolders(configured).Distinct(StringComparer.OrdinalIgnoreCase)) {
            try {
                Safe.NoLinks(folder);
                foreach(string path in Directory.EnumerateFiles(folder,"*",SearchOption.TopDirectoryOnly).Take(10000)) {
                    if(!GeneralModDownload.IsArchivePath(path))continue;
                    string name=Regex.Replace(Path.GetFileName(path),@" \([0-9]+\)(?=\.[^.]+$)","");
                    if(!assets.Any(asset=>String.Equals(asset.name,name,StringComparison.OrdinalIgnoreCase)&&new FileInfo(path).Length==asset.size))continue;
                    Safe.NoLinks(path);found.Add(path);if(found.Count==64)return found;
                }
            }catch(IOException){}catch(UnauthorizedAccessException){}
        }
        return found;
    }
    internal Task<VerifiedModPackage> Verify(string path,Func<bool> active=null){
        return Task.Run(()=>{
            Active(active);if(!GeneralModDownload.IsArchivePath(path))throw Failure("incomplete");path=Path.GetFullPath(path);Safe.NoLinks(path);
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
            using(var hash=SHA256.Create()) {
                long bytes=stream.Length;if(!assets.Any(asset=>asset.size==bytes))throw Failure("incomplete");
                byte[] buffer=new byte[128*1024];int count;long read=0;
                while((count=stream.Read(buffer,0,buffer.Length))>0){Active(active);read+=count;hash.TransformBlock(buffer,0,count,buffer,0);}
                hash.TransformFinalBlock(new byte[0],0,0);string sha=Safe.Hex(hash.Hash);
                if(read!=bytes||stream.Length!=bytes)throw Failure("incomplete");
                var matching=assets.Where(asset=>asset.size==bytes&&Digest(asset)==sha).ToArray();
                if(matching.Length!=1)throw Failure("wrong-file");
                Active(active);
                var info=AssetInfo(matching[0]);return new VerifiedModPackage{Path=path,Bytes=bytes,Sha256=sha,Info=info,SourceUrl=info.GithubAssetUrl};
            }
        });
    }
    static void Active(Func<bool> active){if(active!=null&&!active())throw new OperationCanceledException();}
    internal static SourcePackageException Failure(string state){return new SourcePackageException(state,GeneralStyle.T(
        state=="unverified"?"Esta release do GitHub não informa a assinatura do arquivo. O download manual continua disponível; a troca automática precisa de revisão.":
        state=="incomplete"?"O download do GitHub ainda não está completo.":state=="wrong-file"?"O arquivo não corresponde ao pacote publicado nesta release do GitHub.":
        "Não foi possível consultar esta release do GitHub. Tente novamente mais tarde.",
        state=="unverified"?"This GitHub release does not provide an asset digest. Manual download remains available; automatic replacement needs review.":
        state=="incomplete"?"The GitHub download is not complete yet.":state=="wrong-file"?"The archive does not match the asset published in this GitHub release.":
        "Could not check this GitHub release. Try again later."));}
}
