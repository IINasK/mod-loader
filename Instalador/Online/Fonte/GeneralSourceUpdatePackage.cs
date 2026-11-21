using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;

internal class SourcePackageException : IOException {
    internal string State {get;private set;}
    internal SourcePackageException(string state,string message):base(message){State=state;}
}
internal sealed class VerifiedModPackage {
    internal string Path,Sha256,SourceUrl,TransportRepository;
    internal long Bytes;
    internal GeneralSourceInfo Info;
}
// The provider proves remote identity; the existing installer still proves local
// ownership, protected paths, transactions and rollback independently.
internal sealed class GeneralSourceUpdatePackage {
    internal readonly string Source,Provider;
    internal readonly GeneralSourceInfo Info;
    readonly Func<string,IEnumerable<string>> candidates;
    readonly Func<string,Func<bool>,Task<VerifiedModPackage>> verify;
    readonly Func<DownloadCache,DownloadControl,Action<DownloadProgress>,Task<VerifiedModPackage>> download;
    GeneralSourceUpdatePackage(string source,string provider,GeneralSourceInfo info,Func<string,IEnumerable<string>> candidates,
        Func<string,Func<bool>,Task<VerifiedModPackage>> verify,Func<DownloadCache,DownloadControl,Action<DownloadProgress>,Task<VerifiedModPackage>> download=null){
        Source=source;Provider=provider;Info=info;this.candidates=candidates;this.verify=verify;this.download=download;
    }
    internal bool CanDownload {get{return download!=null;}}
    internal Task<VerifiedModPackage> Download(DownloadCache cache,DownloadControl control,Action<DownloadProgress> progress){return download==null?Task.FromResult<VerifiedModPackage>(null):download(cache,control,progress);}
    internal IEnumerable<string> Candidates(string folder){return candidates(folder);}
    internal Task<VerifiedModPackage> Verify(string path,Func<bool> active){return verify(path,active);}
    internal static GeneralSourceUpdatePackage From(GeneralNexusUpdatePackage package,Func<DownloadCache,DownloadControl,Action<DownloadProgress>,Task<VerifiedModPackage>> download=null){
        return new GeneralSourceUpdatePackage(package.Link.Url,"Nexus",package.Info,package.Candidates,package.Verify,download);
    }
    internal static GeneralSourceUpdatePackage From(GeneralGithubUpdatePackage package){
        return new GeneralSourceUpdatePackage(package.Source,"GitHub",package.Info,package.Candidates,package.Verify,
            package.DownloadAsset==null?null:(Func<DownloadCache,DownloadControl,Action<DownloadProgress>,Task<VerifiedModPackage>>)((cache,control,progress)=>package.Download(cache,control,progress)));
    }
}
