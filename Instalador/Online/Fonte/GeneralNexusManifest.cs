using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

internal sealed class NexusFilePreview {
    internal int Id;
    internal string Version;
    internal Dictionary<string,long?> Files;
}

internal static partial class GeneralNexusInfo {
    internal static NexusFilePreview[] ReadFilePreviews(GeneralNexusLink link, Func<bool> withinBudget) {
        string key=TestKey();
        if(key==null)return null;
        return ReadFilePreviews(link,withinBudget,(uri,api)=>{
            HttpStatusCode status;
            return ReadJsonUrl(uri,api?key:null,out status,3500,!api);
        });
    }

    internal static NexusFilePreview[] ReadFilePreviews(GeneralNexusLink link,Func<bool> withinBudget,Func<Uri,bool,string> request) {
        if(!withinBudget())return null;
        string route="/v1/games/"+link.Game+"/mods/"+link.Mod+"/files";
        var response=ParseObject(request(new Uri(ApiHost+route+(link.File>0?"/"+link.File:"")+".json"),true));
        var listed=link.File>0?new object[]{response}:Value(response,"files") as object[];
        if(listed==null)return null;
        var files=listed.OfType<Dictionary<string,object>>().Where(file=>
            !String.Equals(Text(file,"category_name",32),"DELETED",StringComparison.OrdinalIgnoreCase)).ToArray();
        if(files.Length==0||files.Length>16)return null;

        var results=new List<NexusFilePreview>();
        var ids=new HashSet<int>();
        foreach(var file in files) {
            int id=ReadId(file,"file_id");
            if(id==0||link.File>0&&id!=link.File||!ids.Add(id))return null;
            var uri=PreviewUri(Text(file,"content_preview_link",2048));
            var manifest=uri==null||!withinBudget()?null:ParsePreview(request(uri,false));
            results.Add(new NexusFilePreview{Id=id,Version=Text(file,"version",120),Files=manifest});
        }
        return results.ToArray();
    }

    internal static Uri PreviewUri(string value) {
        Uri uri;
        if(!Uri.TryCreate(value,UriKind.Absolute,out uri)||uri.Scheme!="https"||!uri.IsDefaultPort||
            uri.UserInfo.Length!=0||uri.Fragment.Length!=0)return null;
        if(uri.Host=="mod-file-manifests.nexusmods.com")return uri;
        if(uri.Host!="file-metadata.nexusmods.com")return null;
        var alias=Regex.Match(uri.AbsolutePath,
            @"\A/file/nexus-files-s3-meta/[0-9]+/[0-9]+/(?<key>[a-f0-9]{2}/[a-f0-9]{2}/[a-z0-9-]+)\.json\z",
            RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        return alias.Success?new Uri("https://mod-file-manifests.nexusmods.com/"+alias.Groups["key"].Value):uri;
    }

    internal static Dictionary<string,long?> ParsePreview(string json) {
        var root=ParseObject(json);
        if(root==null)return null;
        var result=new Dictionary<string,long?>(StringComparer.OrdinalIgnoreCase);
        try {
            if(root.ContainsKey("files")){
                var files=Value(root,"files") as object[];
                if((root.ContainsKey("version")&&!MatchesId(root,"version",1))||files==null||files.Length>4096)return null;
                foreach(var file in files){
                    var node=file as Dictionary<string,object>;
                    long size;
                    if(!ReadPreviewBytes(Value(node,"file_size"),out size)||
                        !AddPreviewPath(result,Value(node,"file_path"),size))return null;
                }
            }else if(!ReadPreviewTree(root,result,0))return null;
        }catch(IOException){return null;}
        catch(ArgumentException){return null;}
        return result.Count==0?null:result;
    }

    static bool ReadPreviewTree(Dictionary<string,object> node,Dictionary<string,long?> files,int depth) {
        if(node==null||depth>32||files.Count>4096)return false;
        var kind=Value(node,"type") as string;
        var children=Value(node,"children");
        if(kind=="file"){
            if(children!=null)return false;
            var size=Value(node,"size");
            string display=size as string;
            if(display!=null){
                var exact=Regex.Match(display,@"\A([0-9]+)(?:\s*(?:B|bytes?))?\z",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
                size=exact.Success?exact.Groups[1].Value:null;
            }
            long bytes;
            return AddPreviewPath(files,Value(node,"path"),ReadPreviewBytes(size,out bytes)?(long?)bytes:null);
        }
        if(kind!="directory"&&(kind!=null||depth!=0||children==null))return false;
        if(children==null)return true;
        var items=children as object[];
        return items!=null&&items.All(item=>ReadPreviewTree(item as Dictionary<string,object>,files,depth+1));
    }

    static bool ReadPreviewBytes(object value,out long bytes) {
        return Int64.TryParse(Convert.ToString(value,CultureInfo.InvariantCulture),NumberStyles.None,CultureInfo.InvariantCulture,out bytes)
            &&bytes>=0&&bytes<=MaxFileBytes;
    }

    static bool AddPreviewPath(Dictionary<string,long?> files,object raw,long? bytes) {
        var path=raw as string;
        if(String.IsNullOrWhiteSpace(path)||path.Length>300||files.Count>=4096)return false;
        path=path.Replace('\\','/');
        Safe.In(Path.GetTempPath(),path);
        if(files.ContainsKey(path))return false;
        files.Add(path,bytes);
        return true;
    }
}
