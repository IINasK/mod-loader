using System.Threading.Tasks;

internal static class GeneralModSourceMetadata {
    internal static bool Supports(string source){return GeneralNexusLink.Parse(source)!=null||GeneralGithubLink.Parse(source)!=null;}
    internal static Task<GeneralSourceInfo> Read(string source){
        return GeneralGithubLink.Parse(source)!=null?GeneralGithubUpdatePackage.ReadSourceInfo(source):GeneralNexusInfo.Read(source,true);
    }
}
