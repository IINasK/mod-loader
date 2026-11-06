using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

public sealed class GeneralSourceInfo {
    public string Name,FileName;
    public long Bytes;
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
    internal static string Advice {get{return GeneralStyle.T("Dica: no Nexus, escolha a versão do mod e copie o link da tela com Slow download e Premium. Assim o app identifica o arquivo e o tamanho certos. Outros links continuam aceitos.","Tip: choose the mod version on Nexus, then copy the page with Slow download and Premium. This lets the app identify the right file and size. Other links are still accepted.");}}
    internal static async Task<GeneralSourceInfo> Read(string source){
        var link=GeneralNexusLink.Parse(source);if(link==null)return null;
        return await Task.Run(()=>{
            string url=link.Url;
            for(int redirect=0;redirect<4;redirect++){
                var request=(HttpWebRequest)WebRequest.Create(url);request.UserAgent="ModLoader/"+GeneralUpdateChecker.CurrentVersion;request.Headers["Application-Name"]="Mod Loader";request.Headers["Application-Version"]=GeneralUpdateChecker.CurrentVersion;
                request.AllowAutoRedirect=false;request.Timeout=request.ReadWriteTimeout=12000;request.AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate;
                using(var response=(HttpWebResponse)request.GetResponse()){
                    if((int)response.StatusCode>=300&&(int)response.StatusCode<400){
                        Uri next;var redirected=Uri.TryCreate(new Uri(url),response.Headers["Location"],out next)?GeneralNexusLink.Parse(next.AbsoluteUri):null;
                        if(redirected==null||redirected.Game!=link.Game||redirected.Mod!=link.Mod||redirected.File!=link.File)throw new IOException("Redirecionamento de metadados não autorizado.");url=next.AbsoluteUri;continue;
                    }
                    if(response.ContentLength>4*1024*1024||!(response.ContentType??"").StartsWith("text/html",StringComparison.OrdinalIgnoreCase))throw new IOException("Resposta do Nexus não contém os metadados públicos esperados.");
                    using(var input=response.GetResponseStream())using(var reader=new StreamReader(input,Encoding.UTF8)){
                        var text=new StringBuilder();var buffer=new char[8192];int count;while((count=reader.Read(buffer,0,buffer.Length))>0){if(text.Length+count>4*1024*1024)throw new IOException("Página do Nexus excessiva.");text.Append(buffer,0,count);}return Parse(text.ToString(),link);
                    }
                }
            }
            throw new IOException("Não foi possível consultar esta página do Nexus.");
        });
    }
    internal static GeneralSourceInfo Parse(string html,GeneralNexusLink link){
        var title=Regex.Match(html??"",@"<h1\b[^>]*>([\s\S]*?)</h1>",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(1));
        string name=title.Success?Plain(title.Groups[1].Value):null;
        if(String.IsNullOrWhiteSpace(name)||name.Length>300||Regex.IsMatch(name,@"just a moment|verify you are human|access denied|attention required",RegexOptions.IgnoreCase))return null;
        var info=new GeneralSourceInfo{Name=name};
        var widget=Regex.Match(html,@"<mod-file-download\b[^>]*>",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(1));
        if(widget.Success&&link.File>0){
            int id;long bytes;
            if(Int32.TryParse(Attribute(widget.Value,"file-id"),out id)&&id==link.File){
                info.FileName=Plain(Attribute(widget.Value,"filename"));if(info.FileName.Length>300)info.FileName=null;
                if(Int64.TryParse(Attribute(widget.Value,"filesize-bytes"),out bytes)&&bytes>0&&bytes<=128L*1024*1024*1024)info.Bytes=bytes;
            }
        }
        return info;
    }
    static string Attribute(string html,string name){var match=Regex.Match(html,@"\b"+Regex.Escape(name)+@"\s*=\s*([""'])([\s\S]*?)\1",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(1));return match.Success?WebUtility.HtmlDecode(match.Groups[2].Value):"";}
    static string Plain(string text){return Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(text??"",@"<[^>]*>"," ",RegexOptions.None,TimeSpan.FromSeconds(1))),@"\s+"," ").Trim();}
}

internal sealed partial class GeneralModEditor {
    void ScheduleSourceInfo(){
        if(changing||attachment)return;sourceGeneration++;sourceDelay.Stop();sourceDelay.Start();
    }
    async Task UpdateSourceInfo(){
        if(current==null||attachment||IsDisposed)return;
        int generation=sourceGeneration;var variant=current;string source=sources.Lines.Select(value=>value.Trim()).FirstOrDefault(value=>GeneralNexusLink.Parse(value)!=null||GeneralModDownload.IsArchivePath(value));
        if(source==null){sourceInfo.Text="";return;}
        var nexus=GeneralNexusLink.Parse(source);string advice=nexus!=null&&nexus.File==0?GeneralNexusInfo.Advice+"\r\n":"";
        sourceInfo.ForeColor=nexus!=null&&nexus.File==0?System.Drawing.ColorTranslator.FromHtml(GeneralStyle.Dark?"#ffbf80":"#9a4e00"):GeneralStyle.Muted;
        sourceInfo.Text=advice+T("Lendo informações do arquivo…","Reading file information…");
        try{
            GeneralSourceInfo info;if(variant.SourceInfo==null)variant.SourceInfo=new Dictionary<string,GeneralSourceInfo>();
            if(!variant.SourceInfo.TryGetValue(source,out info)){
                if(GeneralModDownload.IsArchivePath(source)&&File.Exists(source))info=new GeneralSourceInfo{Name=Path.GetFileNameWithoutExtension(source),FileName=Path.GetFileName(source),Bytes=new FileInfo(source).Length};
                else if(lookup!=null)info=await lookup(source);
            }
            if(IsDisposed||generation!=sourceGeneration||current!=variant)return;
            if(info==null){sourceInfo.Text=advice+T("Não foi possível consultar o Nexus agora. Você pode preencher o nome e continuar.","Nexus information is unavailable right now. You can enter the name and continue.");return;}
            variant.SourceInfo[source]=info;
            if(String.IsNullOrWhiteSpace(modName.Text)||modName.Text==lastAutoName){lastAutoName=info.Name;modName.Text=info.Name;}
            sourceInfo.Text=advice+(String.IsNullOrWhiteSpace(info.FileName)?"":T("Arquivo publicado: ","Published file: ")+info.FileName+"   ·   ")+(info.Bytes>0?GeneralStorageSize.Format(info.Bytes)+" ("+info.Bytes.ToString("N0")+" bytes)":T("Tamanho ainda indisponível","Size not yet available"));
        }catch(Exception){if(!IsDisposed&&generation==sourceGeneration)sourceInfo.Text=advice+T("Consulta indisponível. Preencha o nome manualmente para continuar.","Lookup unavailable. Enter the name manually to continue.");}
    }
}
