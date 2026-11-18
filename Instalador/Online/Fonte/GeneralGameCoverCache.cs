using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal sealed class GeneralGameCoverCache {
    static readonly Uri GameApi=new Uri("https://api.nexusmods.com/v2/graphql");
    static readonly Regex DomainPattern=new Regex(@"\A[a-z0-9][a-z0-9_-]{0,99}\z",RegexOptions.CultureInvariant);
    const int MaxImageBytes=2*1024*1024;
    const int MaxPixelDimension=1600;
    const string GameLookup="query CoverGame($domain: String!) { game(domainName: $domain) { id domainName artworkSchema } }";
    readonly string directory;
    readonly bool allowNetwork;
    readonly Func<Uri,string,int,CancellationToken,byte[]> request;
    readonly SemaphoreSlim network=new SemaphoreSlim(3);
    readonly ConcurrentDictionary<string,SemaphoreSlim> domainGates=new ConcurrentDictionary<string,SemaphoreSlim>(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string,DateTime> retryAfter=new ConcurrentDictionary<string,DateTime>(StringComparer.Ordinal);

    internal GeneralGameCoverCache(string cacheRoot,bool allowNetwork=true,Func<Uri,string,int,CancellationToken,byte[]> request=null){
        directory=Path.Combine(Path.GetFullPath(cacheRoot),"GameCovers");
        this.allowNetwork=allowNetwork;
        this.request=request??Fetch;
    }

    internal static string Identify(GeneralProject project,GeneralModState state){
        if(project!=null&&project.TemplateId==GeneralProjectStore.CyberpunkTemplate)return "cyberpunk2077";
        if(state==null||state.List==null)return null;
        if(state.TemplateId==GeneralProjectStore.CyberpunkTemplate)return "cyberpunk2077";
        var mods=state.List.Mods.Concat(state.List.Presets.SelectMany(preset=>preset.Configurations.Values));
        var domains=mods.SelectMany(mod=>mod.Variants.SelectMany(variant=>variant.WithNaturalOption()))
            .SelectMany(variant=>variant.Sources).Select(GeneralNexusLink.Parse).Where(link=>link!=null)
            .Select(link=>link.Game).Distinct(StringComparer.Ordinal).Take(2).ToArray();
        return domains.Length==1?domains[0]:null;
    }

    internal async Task<Image> GetAsync(string gameDomain,CancellationToken cancellationToken){
        string domain=gameDomain==null?null:gameDomain.ToLowerInvariant();
        if(domain==null||!DomainPattern.IsMatch(domain))return null;
        SemaphoreSlim gate=domainGates.GetOrAdd(domain,key=>new SemaphoreSlim(1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try{
            string file=Safe.In(directory,domain+".jpg");
            var cached=await Task.Run(()=>ReadCached(file),cancellationToken).ConfigureAwait(false);
            DateTime retry;
            if(cached!=null||!allowNetwork||retryAfter.TryGetValue(domain,out retry)&&retry>DateTime.UtcNow)return cached;
            await network.WaitAsync(cancellationToken).ConfigureAwait(false);
            try{
                var downloaded=await Task.Run(()=>ReadRemote(domain,file,cancellationToken),cancellationToken).ConfigureAwait(false);
                if(downloaded==null)retryAfter[domain]=DateTime.UtcNow.AddMinutes(5);
                else retryAfter.TryRemove(domain,out retry);
                return downloaded;
            }finally{network.Release();}
        }catch(IOException){return null;}
         catch(UnauthorizedAccessException){return null;}
        finally{gate.Release();}
    }

    static Image ReadCached(string file){
        Safe.NoLinks(file);
        if(!File.Exists(file))return null;
        try{
            using(var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete)){
                if(input.Length==0||input.Length>MaxImageBytes)return null;
                using(var image=Image.FromStream(input,false,true)){
                    if(!ReasonableSize(image.Width,image.Height))return null;
                    return new Bitmap(image);
                }
            }
        }catch(ArgumentException){return null;}
         catch(ExternalException){return null;}
         catch(FileNotFoundException){return null;}
         catch(OutOfMemoryException){return null;}
    }

    Image ReadRemote(string domain,string file,CancellationToken cancellationToken){
        using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)){
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try{
                string query=Safe.Json.Serialize(new{query=GameLookup,variables=new{domain=domain}});
                var game=Safe.Json.Deserialize<GameResult>(Encoding.UTF8.GetString(request(GameApi,query,32768,timeout.Token)));
                var cover=ResolveCover(game,domain);
                if(cover==null)return null;
                var content=request(cover,null,MaxImageBytes,timeout.Token);
                if(!SupportedImage(content))return null;
                using(var input=new MemoryStream(content,false))
                using(var normalized=new MemoryStream()){
                    var decoder=BitmapDecoder.Create(input,BitmapCreateOptions.IgnoreColorProfile,BitmapCacheOption.OnDemand);
                    if(decoder.Frames.Count!=1)return null;
                    var image=decoder.Frames[0];
                    if(!ReasonableSize(image.PixelWidth,image.PixelHeight))return null;
                    var pixels=new FormatConvertedBitmap(image,PixelFormats.Bgr24,null,0);
                    var jpeg=new JpegBitmapEncoder{QualityLevel=88};
                    jpeg.Frames.Add(BitmapFrame.Create(pixels));
                    jpeg.Save(normalized);
                    if(normalized.Length>MaxImageBytes)return null;
                    timeout.Token.ThrowIfCancellationRequested();
                    byte[] bytes=normalized.ToArray();
                    try{WriteAtomic(file,bytes);}
                    catch(IOException){}
                    catch(UnauthorizedAccessException){}
                    normalized.Position=0;
                    using(var preview=Image.FromStream(normalized,false,true))return new Bitmap(preview);
                }
            }catch(Exception ex){
                if(!(ex is IOException||ex is WebException||ex is ArgumentException||
                    ex is InvalidOperationException||ex is FormatException||ex is NotSupportedException||ex is ExternalException||
                    ex is UnauthorizedAccessException||ex is OperationCanceledException))throw;
                var failure=ex as WebException;
                if(failure!=null&&failure.Response!=null)failure.Response.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
                return null;
            }
        }
    }

    static bool SupportedImage(byte[] bytes){
        if(bytes==null||bytes.Length<12||bytes.Length>MaxImageBytes)return false;
        return bytes[0]==0xff&&bytes[1]==0xd8&&bytes[2]==0xff||
            bytes[0]==0x89&&Encoding.ASCII.GetString(bytes,1,3)=="PNG"&&
                bytes[4]==0x0d&&bytes[5]==0x0a&&bytes[6]==0x1a&&bytes[7]==0x0a||
            Encoding.ASCII.GetString(bytes,0,4)=="RIFF"&&Encoding.ASCII.GetString(bytes,8,4)=="WEBP";
    }

    static bool ReasonableSize(int width,int height){
        return width>=64&&height>=64&&width<=MaxPixelDimension&&height<=MaxPixelDimension;
    }

    static Uri ResolveCover(GameResult response,string expectedDomain){
        var game=response==null||response.data==null?null:response.data.game;
        if(game==null||game.id<=0||!String.Equals(game.domainName,expectedDomain,StringComparison.Ordinal))return null;
        string id=game.id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        // Exact portrait routes from the Nexus gameArtwork schema.
        if(game.artworkSchema=="V2")return new Uri("https://images.nexusmods.com/images/games/v2/"+id+"/tile.jpg");
        if(game.artworkSchema=="V1")return new Uri("https://images.nexusmods.com/images/games/4_3/tile_"+id+".jpg");
        return null;
    }

    static byte[] Fetch(Uri uri,string body,int maxBytes,CancellationToken token){
        token.ThrowIfCancellationRequested();
        var web=(HttpWebRequest)WebRequest.Create(uri);
        web.AllowAutoRedirect=false;
        web.AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate;
        web.Timeout=5000;web.ReadWriteTimeout=5000;
        web.UserAgent=GeneralAppPaths.ProductName.Replace(' ','-')+"/"+GeneralUpdateChecker.CurrentVersion;
        web.Accept=body==null?"image/jpeg, image/png, image/webp":"application/json";
        using(token.Register(()=>web.Abort())){
            if(body!=null){
                byte[] payload=Encoding.UTF8.GetBytes(body);
                web.Method="POST";web.ContentType="application/json";web.ContentLength=payload.Length;
                using(var writer=web.GetRequestStream())writer.Write(payload,0,payload.Length);
            }
            using(var response=(HttpWebResponse)web.GetResponse()){
                if(response.StatusCode!=HttpStatusCode.OK||response.ContentLength>maxBytes)
                    throw new IOException("Resposta de capa inválida.");
                using(var stream=response.GetResponseStream())
                using(var output=new MemoryStream()){
                    byte[] buffer=new byte[8192];
                    int count;
                    while((count=stream.Read(buffer,0,buffer.Length))>0){
                        token.ThrowIfCancellationRequested();
                        if(output.Length+count>maxBytes)throw new IOException("Resposta de capa grande demais.");
                        output.Write(buffer,0,count);
                    }
                    return output.ToArray();
                }
            }
        }
    }

    static void WriteAtomic(string file,byte[] bytes){
        Safe.NoLinks(file);
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        string temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{
            using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
                output.Write(bytes,0,bytes.Length);
                output.Flush(true);
            }
            Safe.NoLinks(file);
            if(File.Exists(file))File.Replace(temporary,file,null);else File.Move(temporary,file);
        }finally{
            if(File.Exists(temporary))File.Delete(temporary);
        }
    }

    sealed class GameResult { public GameData data { get; set; } }
    sealed class GameData { public GameIdentity game { get; set; } }
    sealed class GameIdentity {
        public int id { get; set; }
        public string domainName { get; set; }
        public string artworkSchema { get; set; }
    }
}