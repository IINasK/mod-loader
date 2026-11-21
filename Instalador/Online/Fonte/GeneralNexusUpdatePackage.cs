using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

internal delegate string GeneralNexusPackageRequest(Uri uri,string credential,out HttpStatusCode status);

internal sealed class NexusPackageException : SourcePackageException {
    internal NexusPackageException(string state,string message):base(state,message){}
}

// A filename only selects a candidate. Nexus's hash lookup must confirm the exact
// game/mod/file identity before a local SHA-256 seals the bytes for installation.
internal sealed class GeneralNexusUpdatePackage {
    const long MaximumBytes=128L*1024*1024*1024;
    const int MaximumResponse=4*1024*1024;
    readonly GeneralNexusLink link;
    readonly GeneralSourceInfo info;
    readonly string credential;
    readonly GeneralNexusPackageRequest request;
    internal GeneralNexusLink Link {get{return new GeneralNexusLink{Game=link.Game,Mod=link.Mod,File=link.File};}}
    internal GeneralSourceInfo Info {get{return info.SavedCopy();}}

    GeneralNexusUpdatePackage(GeneralNexusLink link,GeneralSourceInfo info,string credential,GeneralNexusPackageRequest request){
        this.link=link;this.info=info;this.credential=credential;this.request=request;
    }

    internal static Task<GeneralNexusUpdatePackage> Read(string targetUrl,Func<bool> active=null){
        return Read(targetUrl,NexusCredentials.Read(),GeneralNexusInfo.ReadPackageJson,active);
    }

    internal static Task<GeneralNexusUpdatePackage> Read(string targetUrl,string credential,GeneralNexusPackageRequest request,Func<bool> active=null){
        return Task.Run(()=>{
            Active(active);
            var link=GeneralNexusLink.Parse(targetUrl);
            if(link==null||link.File<=0)throw Failure("wrong-file");
            if(!NexusCredentials.Valid(credential))throw Failure("connect");
            if(request==null)throw new ArgumentNullException("request");
            var uri=new Uri("https://api.nexusmods.com/v1/games/"+link.Game+"/mods/"+link.Mod+"/files/"+link.File+".json");
            string json=ReadResponse(request,uri,credential,active);
            var file=DecodeObject(json);
            if(file==null||Number(file,"file_id")!=link.File||!Available(file))throw Failure("unavailable");
            string filename=Text(file,"file_name",300);
            if(filename==null||!ValidFilename(filename))throw Failure("unavailable");
            bool approximate=Value(file,"size_in_bytes")==null;
            long bytes=Number(file,"size_in_bytes");
            if(!approximate&&(bytes<=0||bytes>MaximumBytes))throw Failure("unavailable");
            if(approximate){
                long kib=Number(file,"size_kb");
                if(kib<=0)kib=Number(file,"size");
                bytes=kib>0&&kib<=MaximumBytes/1024?kib*1024:0;
            }
            if(bytes<0||bytes>MaximumBytes)throw Failure("unavailable");
            var info=new GeneralSourceInfo{
                Name=Text(file,"name",300)??Path.GetFileNameWithoutExtension(filename),FileName=filename,
                Version=Text(file,"version",120),ModVersion=Text(file,"mod_version",120),
                Bytes=bytes,SizeApproximate=approximate,NexusChecked=true
            };
            Active(active);
            return new GeneralNexusUpdatePackage(link,info,credential,request);
        });
    }

    internal Task<VerifiedModPackage> Verify(string path,Func<bool> active=null){return VerifyFile(path,active,false);}
    // Only our owned transport temporary can bypass the filename-extension check.
    // The same Nexus identity and both hashes are still required before commit.
    internal Task<VerifiedModPackage> VerifyDownloaded(string path,Func<bool> active=null){return VerifyFile(path,active,true);}
    Task<VerifiedModPackage> VerifyFile(string path,Func<bool> active,bool transport){
        return Task.Run(()=>{
            Active(active);
            if(!GeneralModDownload.IsArchivePath(path)&&!(transport&&Regex.IsMatch(Path.GetFileName(path),@"\AModLoader-[a-f0-9]{32}\.part\z")))throw Failure("incomplete");
            path=Path.GetFullPath(path);Safe.NoLinks(path);
            // The browser must release its write handle first. The lease remains
            // held through the lookup, so the two digests describe the same bytes.
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){
                long bytes=stream.Length;
                if(bytes<=0||bytes>MaximumBytes||!info.SizeApproximate&&info.Bytes>0&&info.Bytes!=bytes)
                    throw Failure("incomplete");
                string md5,sha256;
                using(var md5Algorithm=MD5.Create())
                using(var shaAlgorithm=SHA256.Create()){
                    byte[] buffer=new byte[128*1024];int count;long read=0;
                    while((count=stream.Read(buffer,0,buffer.Length))>0){
                        Active(active);read+=count;
                        if(read>MaximumBytes)throw Failure("incomplete");
                        md5Algorithm.TransformBlock(buffer,0,count,buffer,0);
                        shaAlgorithm.TransformBlock(buffer,0,count,buffer,0);
                    }
                    md5Algorithm.TransformFinalBlock(new byte[0],0,0);
                    shaAlgorithm.TransformFinalBlock(new byte[0],0,0);
                    if(read!=bytes||stream.Length!=bytes)throw Failure("incomplete");
                    md5=Safe.Hex(md5Algorithm.Hash);sha256=Safe.Hex(shaAlgorithm.Hash);
                }
                var uri=new Uri("https://api.nexusmods.com/v1/games/"+link.Game+"/mods/md5_search/"+md5+".json");
                string json=ReadResponse(request,uri,credential,active);
                if(!MatchesIdentity(json,link,bytes))throw Failure("wrong-file");
                Active(active);
                var verifiedInfo=info.SavedCopy();verifiedInfo.Bytes=bytes;verifiedInfo.SizeApproximate=false;
                return new VerifiedModPackage{Path=path,Sha256=sha256,Bytes=bytes,Info=verifiedInfo,SourceUrl=link.Url};
            }
        });
    }

    internal string Author {get;private set;}
    internal string ModName {get;private set;}
    internal Task<string[]> DeclaredGithubRepositories(Func<bool> active=null){
        return Task.Run(()=>{
            string json=ReadResponse(request,new Uri("https://api.nexusmods.com/v1/games/"+link.Game+"/mods/"+link.Mod+".json"),credential,active);
            var mod=DecodeObject(json);
            if(Number(mod,"mod_id")!=link.Mod)return new string[0];
            Author=Text(mod,"author",150)??Text(mod,"uploaded_by",150);
            ModName=Text(mod,"name",300);
            return GeneralNexusGithubMirror.Repositories(Value(mod,"description") as string);
        });
    }

    internal IEnumerable<string> Candidates(string configured){
        return CandidatesInFolders(GeneralModDownload.WatchFolders(configured));
    }

    internal IEnumerable<string> CandidatesInFolders(IEnumerable<string> folders){
        var found=new List<string>();
        foreach(string folder in folders.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase)){
            try{
                Safe.NoLinks(folder);
                string pattern=Path.GetFileNameWithoutExtension(info.FileName)+"*"+Path.GetExtension(info.FileName);
                foreach(string path in Directory.EnumerateFiles(folder,pattern,SearchOption.TopDirectoryOnly).Take(10000)){
                    if(!GeneralModDownload.IsArchivePath(path))continue;
                    string name=Path.GetFileName(path);
                    if(!String.Equals(name,info.FileName,StringComparison.OrdinalIgnoreCase)&&
                        !String.Equals(Regex.Replace(name,@" \([0-9]+\)(?=\.[^.]+$)",""),info.FileName,StringComparison.OrdinalIgnoreCase))continue;
                    Safe.NoLinks(path);
                    long bytes=new FileInfo(path).Length;
                    if(bytes<=0||bytes>MaximumBytes||!info.SizeApproximate&&info.Bytes>0&&info.Bytes!=bytes)continue;
                    found.Add(path);
                    if(found.Count==64)return found;
                }
            }catch(IOException){}catch(UnauthorizedAccessException){}
        }
        return found;
    }

    internal static bool MatchesIdentity(string json,GeneralNexusLink expected,long bytes){
        if(expected==null||expected.File<=0||bytes<=0||bytes>MaximumBytes||String.IsNullOrWhiteSpace(json)||json.Length>MaximumResponse)return false;
        try{
            var results=Safe.Json.DeserializeObject(json) as object[];
            if(results==null||results.Length>256)return false;
            foreach(var raw in results){
                var row=raw as Dictionary<string,object>;
                var mod=Value(row,"mod") as Dictionary<string,object>;
                var file=Value(row,"file_details") as Dictionary<string,object>;
                if(Number(mod,"mod_id")!=expected.Mod||Number(file,"file_id")!=expected.File||!Available(file)||
                    !String.Equals(Text(mod,"domain_name",100),expected.Game,StringComparison.OrdinalIgnoreCase))continue;
                long exact=Number(file,"size_in_bytes");
                if(Value(file,"size_in_bytes")!=null&&exact!=bytes)continue;
                return true;
            }
        }catch(ArgumentException){}catch(InvalidOperationException){}
        return false;
    }

    static string ReadResponse(GeneralNexusPackageRequest request,Uri uri,string credential,Func<bool> active){
        Active(active);HttpStatusCode status;string json;
        try{json=request(uri,credential,out status);}
        catch(WebException){throw Failure("unavailable");}
        catch(IOException){throw Failure("unavailable");}
        Active(active);
        if(status==HttpStatusCode.Unauthorized||status==HttpStatusCode.Forbidden)throw Failure("authentication");
        if((int)status==429)throw Failure("rate-limit");
        if(status!=HttpStatusCode.OK||String.IsNullOrWhiteSpace(json)||json.Length>MaximumResponse)throw Failure("unavailable");
        return json;
    }

    static bool Available(Dictionary<string,object> file){long category=Number(file,"category_id");return category==1||category==2||category==3;}
    internal static bool ValidFilename(string filename){return filename==Path.GetFileName(filename)&&filename.IndexOfAny(Path.GetInvalidFileNameChars())<0&&
        new[]{".zip",".rar",".7z"}.Contains(Path.GetExtension(filename),StringComparer.OrdinalIgnoreCase);}
    static void Active(Func<bool> active){if(active!=null&&!active())throw new OperationCanceledException();}
    static object Value(Dictionary<string,object> value,string key){object result;return value!=null&&value.TryGetValue(key,out result)?result:null;}
    static long Number(Dictionary<string,object> value,string key){long result;return Int64.TryParse(Convert.ToString(Value(value,key),CultureInfo.InvariantCulture),NumberStyles.None,CultureInfo.InvariantCulture,out result)?result:-1;}
    static string Text(Dictionary<string,object> value,string key,int max){var text=Value(value,key) as string;return !String.IsNullOrWhiteSpace(text)&&text.Length<=max&&!text.Any(Char.IsControl)?text:null;}
    static Dictionary<string,object> DecodeObject(string json){try{return Safe.Json.DeserializeObject(json) as Dictionary<string,object>;}catch(ArgumentException){return null;}catch(InvalidOperationException){return null;}}
    static NexusPackageException Failure(string state){
        switch(state){
            case "connect":return new NexusPackageException(state,GeneralStyle.T("Conecte ao Nexus para confirmar o arquivo da atualização.","Connect to Nexus to verify the update archive."));
            case "authentication":return new NexusPackageException(state,GeneralStyle.T("O Nexus recusou a conexão. Reconecte antes de atualizar.","Nexus rejected authentication. Reconnect before updating."));
            case "rate-limit":return new NexusPackageException(state,GeneralStyle.T("O limite da API do Nexus foi atingido. Tente novamente mais tarde.","The Nexus API limit was reached. Try again later."));
            case "wrong-file":return new NexusPackageException(state,GeneralStyle.T("O Nexus não confirmou este pacote como o arquivo exato da atualização. A instalação atual foi preservada.","Nexus did not confirm this archive as the exact update file. The current installation was preserved."));
            case "incomplete":return new NexusPackageException(state,GeneralStyle.T("O download ainda não está completo ou não corresponde ao tamanho esperado.","The download is not complete or does not match the expected size."));
            default:return new NexusPackageException(state,GeneralStyle.T("Não foi possível confirmar o pacote com o Nexus. Tente novamente; a instalação atual foi preservada.","Could not verify the archive with Nexus. Try again; the current installation was preserved."));
        }
    }
}

internal static partial class GeneralNexusInfo {
    internal static string ReadPackageJson(Uri uri,string credential,out HttpStatusCode status){
        if(uri==null||uri.Scheme!="https"||uri.Host!="api.nexusmods.com"||!uri.IsDefaultPort||!String.IsNullOrEmpty(uri.UserInfo))
            throw new IOException("API Nexus inválida.");
        return ReadJsonUrl(uri,credential,out status,8000);
    }
}
