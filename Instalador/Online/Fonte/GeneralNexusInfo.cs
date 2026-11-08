using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

public sealed class GeneralSourceInfo {
    public string Name,FileName,Version,ModVersion;
    public bool SizeApproximate,NexusChecked;
    internal bool Nsfw;
    internal string[] Tags;
    public long Bytes;
    internal GeneralSourceInfo SavedCopy(){
        return new GeneralSourceInfo{Name=Name,FileName=FileName,Version=Version,ModVersion=ModVersion,
            Bytes=Bytes,SizeApproximate=SizeApproximate,NexusChecked=NexusChecked};
    }
}
internal sealed class GeneralNexusLink {
    internal string Game;
    internal int Mod,File;
    internal string Url {get{return "https://www.nexusmods.com/"+Game+"/mods/"+Mod+(File>0?"?tab=files&file_id="+File:"");}}
    internal static GeneralNexusLink Parse(string value){
        Uri uri;if(!GeneralModDownload.IsWebSource(value)||!Uri.TryCreate(value,UriKind.Absolute,out uri)||!(uri.Host=="www.nexusmods.com"||uri.Host=="nexusmods.com"))return null;
        var path=Regex.Match(uri.AbsolutePath,@"\A/(?:[a-z]{2}/)?(?:games/)?(?<game>[a-z0-9][a-z0-9_-]{0,99})/mods/(?<mod>[1-9][0-9]{0,9})/?\z",RegexOptions.IgnoreCase);
        int mod;if(!path.Success||!Int32.TryParse(path.Groups["mod"].Value,out mod))return null;
        var link=new GeneralNexusLink{Game=path.Groups["game"].Value.ToLowerInvariant(),Mod=mod};var seen=new HashSet<string>();
        foreach(string part in uri.Query.TrimStart('?').Split(new[]{'&'},StringSplitOptions.RemoveEmptyEntries)){
            var pair=part.Split('=');if(pair.Length!=2||!seen.Add(pair[0]))return null;
            if(pair[0]=="file_id"){if(!Int32.TryParse(pair[1],out link.File)||link.File<=0)return null;}
            else if(pair[0]!="tab"||!new[]{"files","description","posts","images","logs","bugs","stats","videos"}.Contains(pair[1]))return null;
        }
        return link;
    }
}
internal static class GeneralNexusInfo {
    const string ApiHost = "https://api.nexusmods.com";
    const string ApiKeyVariable = "MOD_LOADER_NEXUS_API_KEY";
    const int MaxResponse = 4 * 1024 * 1024;
    const long MaxFileBytes = 128L * 1024 * 1024 * 1024;

    internal static string Advice {get{return GeneralStyle.T("No Nexus, copie o link da tela Slow download da versão desejada.","On Nexus, copy the link to the Slow download page for the version you want.");}}

    internal static bool HasTestKey {get{return TestKey()!=null;}}

    static string TestKey() {
        // Personal keys are for local testing until the application is registered.
        string key = Environment.GetEnvironmentVariable(ApiKeyVariable);
        return String.IsNullOrWhiteSpace(key) || key.Length > 256 || key.Any(c => Char.IsControl(c) || Char.IsWhiteSpace(c)) ? null : key;
    }

    internal static Task<GeneralSourceInfo> Read(string source) {
        var link = GeneralNexusLink.Parse(source);
        string testKey = TestKey();
        if (link == null || (!GeneralAppPaths.IsDevelopment && testKey == null &&
            Environment.GetEnvironmentVariable("MOD_LOADER_NEXUS_API_TEST") != "1"))
            return Task.FromResult<GeneralSourceInfo>(null);
        return Task.Run(() => {
            string key = testKey;
            string resource = "/games/" + link.Game + "/mods/" + link.Mod;
            HttpStatusCode status;
            string json = ReadJson("/v3" + resource, key, out status);
            if (key != null && status == HttpStatusCode.Unauthorized) {
                key = null;
                json = ReadJson("/v3" + resource, null, out status);
            }
            if ((int)status == 451) return new GeneralSourceInfo { Nsfw = true };
            var info = ParseMod(json, link);
            if (info != null && link.File > 0 && key != null)
                ApplyFile(ReadJson("/v1" + resource + "/files/" + link.File + ".json", key, out status), link, info);
            return info;
        });
    }

    static string ReadJson(string route, string key, out HttpStatusCode status) {
        status = 0;
        var request = (HttpWebRequest)WebRequest.Create(ApiHost + route);
        request.UserAgent = "ModLoader/" + GeneralUpdateChecker.CurrentVersion;
        request.Headers["Application-Name"] = "Mod Loader";
        request.Headers["Application-Version"] = GeneralUpdateChecker.CurrentVersion;
        if (key != null) request.Headers["apikey"] = key;
        request.Accept = "application/json";
        request.AllowAutoRedirect = false;
        request.Timeout = request.ReadWriteTimeout = 8000;
        request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        try {
            using (var response = (HttpWebResponse)request.GetResponse()) {
                status = response.StatusCode;
                if (status != HttpStatusCode.OK || response.ContentLength > MaxResponse ||
                    !(response.ContentType ?? "").StartsWith("application/json", StringComparison.OrdinalIgnoreCase)) return null;
                using (var input = response.GetResponseStream())
                using (var reader = new StreamReader(input, Encoding.UTF8)) {
                    var text = new StringBuilder();
                    var buffer = new char[8192];
                    int count;
                    while ((count = reader.Read(buffer, 0, buffer.Length)) > 0) {
                        if (text.Length + count > MaxResponse) return null;
                        text.Append(buffer, 0, count);
                    }
                    return text.ToString();
                }
            }
        } catch (WebException ex) {
            using (var response = ex.Response as HttpWebResponse)
                if (response != null) status = response.StatusCode;
            return null;
        } catch (IOException) {
            return null;
        }
    }

    internal static GeneralSourceInfo ParseMod(string json, GeneralNexusLink link) {
        var data = Value(ParseObject(json), "data") as Dictionary<string, object>;
        string name = Text(data, "name", 300);
        if (link == null || name == null || !MatchesId(data, "game_scoped_id", link.Mod)) return null;
        var tags = Value(data, "tags") as object[];
        var adult = Value(data, "adult_content");
        return new GeneralSourceInfo {
            Name = name,
            NexusChecked = true,
            ModVersion = Text(data, "version", 120),
            Nsfw = adult is bool && (bool)adult,
            Tags = tags == null ? null : tags.Select(tag => Text(tag as Dictionary<string, object>, "name", 60))
                .Where(tag => tag != null).Distinct(StringComparer.OrdinalIgnoreCase).Take(32).ToArray()
        };
    }

    internal static void ApplyFile(string json, GeneralNexusLink link, GeneralSourceInfo info) {
        var data = ParseObject(json);
        if (link == null || info == null || link.File <= 0 || !MatchesId(data, "file_id", link.File)) return;
        info.FileName = Text(data, "file_name", 300);
        info.Version = Text(data, "version", 120);
        long kib;
        if (Int64.TryParse(Convert.ToString(Value(data, "size_kb"), CultureInfo.InvariantCulture),
                NumberStyles.None, CultureInfo.InvariantCulture, out kib) && kib > 0 && kib <= MaxFileBytes / 1024) {
            info.Bytes = kib * 1024;
            info.SizeApproximate = true;
        }
    }

    static Dictionary<string, object> ParseObject(string json) {
        if (String.IsNullOrWhiteSpace(json) || json.Length > MaxResponse) return null;
        try { return Safe.Json.DeserializeObject(json) as Dictionary<string, object>; }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    static object Value(Dictionary<string, object> data, string key) {
        object value;
        return data != null && data.TryGetValue(key, out value) ? value : null;
    }

    static bool MatchesId(Dictionary<string, object> data, string key, int expected) {
        int id;
        return Int32.TryParse(Convert.ToString(Value(data, key), CultureInfo.InvariantCulture),
            NumberStyles.None, CultureInfo.InvariantCulture, out id) && id == expected;
    }

    static string Text(Dictionary<string, object> data, string key, int limit) {
        string value = Value(data, key) as string;
        if (String.IsNullOrWhiteSpace(value) || value.Any(Char.IsControl)) return null;
        value = value.Trim();
        return value.Length <= limit ? value : null;
    }
}

internal sealed partial class GeneralModEditor {
    void ScheduleSourceInfo(){
        if(changing||attachment)return;sourceGeneration++;sourceDelay.Stop();sourceDelay.Start();
    }
    async Task UpdateSourceInfo(){
        if(current==null||attachment||IsDisposed)return;
        int generation=sourceGeneration;
        var variant=current;
        var candidates=sources.Lines.Select(value=>value.Trim())
            .Select(value=>new {Source=value,Nexus=GeneralNexusLink.Parse(value)})
            .Where(item=>item.Nexus!=null||GeneralModDownload.IsArchivePath(item.Source))
            .GroupBy(item=>item.Nexus==null?item.Source:item.Nexus.Url).Select(group=>group.First()).Take(32).ToList();
        if(candidates.Count==0){sourceInfo.Text="";return;}
        bool overview=candidates.Any(item=>item.Nexus!=null&&item.Nexus.File==0);
        string advice=overview?GeneralNexusInfo.Advice+"\r\n":"";
        sourceInfo.ForeColor=overview?System.Drawing.ColorTranslator.FromHtml(GeneralStyle.Dark?"#ffbf80":"#9a4e00"):GeneralStyle.Muted;
        sourceInfo.Text=advice+T("Lendo informações do arquivo…","Reading file information…");
        GeneralSourceInfo preview=null;
        GeneralNexusLink previewLink=null;
        bool restricted=false;
        foreach(var item in candidates){
            GeneralSourceInfo known;
            if(variant.SourceInfo==null)variant.SourceInfo=new Dictionary<string,GeneralSourceInfo>();
            variant.SourceInfo.TryGetValue(item.Source,out known);
            GeneralSourceInfo fresh=null;
            try{
                if(item.Nexus==null&&known==null&&File.Exists(item.Source))
                    fresh=new GeneralSourceInfo{Name=Path.GetFileNameWithoutExtension(item.Source),FileName=Path.GetFileName(item.Source),Bytes=new FileInfo(item.Source).Length};
                else if(item.Nexus!=null&&lookup!=null&&(known==null||!known.NexusChecked||
                    (item.Nexus.File>0&&String.IsNullOrEmpty(known.Version)&&GeneralNexusInfo.HasTestKey)))
                    fresh=await lookup(item.Source);
            }catch(Exception){}
            if(IsDisposed||generation!=sourceGeneration||current!=variant)return;
            if(fresh!=null){
                if(fresh.Nsfw)nsfw.Checked=true;
                if(!String.IsNullOrWhiteSpace(fresh.Name)&&(known==null||!known.NexusChecked))MergeSourceTags(fresh.Tags);
                restricted|=fresh.Nsfw&&String.IsNullOrWhiteSpace(fresh.Name);
            }

            var info=fresh!=null&&!String.IsNullOrWhiteSpace(fresh.Name)?fresh.SavedCopy():known;
            if(info==null)continue;
            if(known!=null&&fresh!=null&&!ReferenceEquals(info,known)){
                info.FileName=info.FileName??known.FileName;
                info.Version=info.Version??known.Version;
                if(info.Bytes==0||info.SizeApproximate&&!known.SizeApproximate&&known.Bytes>0){
                    info.Bytes=known.Bytes;info.SizeApproximate=known.SizeApproximate;
                }
            }
            variant.SourceInfo[item.Source]=info;
            if(preview==null){preview=info;previewLink=item.Nexus;}
        }
        if(preview==null){
            sourceInfo.Text=advice+(restricted?
                T("O Nexus restringe os dados deste conteúdo adulto.","Nexus restricts metadata for this adult content."):
                T("Dados indisponíveis. Preencha o nome para continuar.","Metadata unavailable. Enter the name to continue."));
            return;
        }
        if(String.IsNullOrWhiteSpace(modName.Text)||modName.Text==lastAutoName){lastAutoName=preview.Name;modName.Text=preview.Name;}
        sourceInfo.Text=advice+SourceDetails(preview,previewLink);
    }

    static string SourceDetails(GeneralSourceInfo info,GeneralNexusLink link){
        var details=new List<string>();
        if(!String.IsNullOrWhiteSpace(info.FileName))details.Add(T("Arquivo: ","File: ")+info.FileName);
        if(!String.IsNullOrWhiteSpace(info.Version))details.Add(T("Versão do arquivo: ","File version: ")+info.Version);
        else if(link!=null&&link.File==0&&!String.IsNullOrWhiteSpace(info.ModVersion))
            details.Add(T("Versão na página: ","Version on mod page: ")+info.ModVersion);
        if(info.Bytes>0)details.Add(info.SizeApproximate?T("Aprox. ","About ")+GeneralStorageSize.Format(info.Bytes):
            GeneralStorageSize.Format(info.Bytes)+" ("+info.Bytes.ToString("N0")+" bytes)");
        if(link!=null&&link.File>0&&String.IsNullOrWhiteSpace(info.Version)&&info.Bytes==0)
            details.Add(T("Dados do arquivo indisponíveis.","File details unavailable."));
        return String.Join("   ·   ",details);
    }
}
