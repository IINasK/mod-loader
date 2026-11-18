using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

internal static class GeneralGameCoverCacheTests {
    internal static void Run(string parent){
        string root=OnlineTests.NestedRoot(parent,"game-covers-");
        Directory.CreateDirectory(root);
        Check(GeneralGameCoverCache.Identify(new GeneralProject{TemplateId=GeneralProjectStore.CyberpunkTemplate},null)=="cyberpunk2077",
            "The known template has the official nexus domain.");
        var state=new GeneralModState();
        AddSource(state,"https://www.nexusmods.com/games/fallout4/mods/1");
        Check(GeneralGameCoverCache.Identify(null,state)=="fallout4","A unique Nexus game identifies the project.");
        AddSource(state,"https://www.nexusmods.com/games/skyrimspecialedition/mods/2");
        Check(GeneralGameCoverCache.Identify(null,state)==null,"A mixed project does not pick an arbitrary cover.");

        byte[] png=Png(400,600);
        int api=0,image=0;
        Func<Uri,string,int,CancellationToken,byte[]> download=(uri,body,limit,token)=>{
            if(body!=null){
                Interlocked.Increment(ref api);
                Check(uri.AbsoluteUri=="https://api.nexusmods.com/v2/graphql"&&body.Contains("fallout4"),"The exact domain is resolved through GraphQL.");
                return System.Text.Encoding.UTF8.GetBytes("{\"data\":{\"game\":{\"id\":1151,\"domainName\":\"fallout4\",\"artworkSchema\":\"V2\"}}}");
            }
            Interlocked.Increment(ref image);
            Check(uri.AbsoluteUri=="https://images.nexusmods.com/images/games/v2/1151/tile.jpg","The returned identity selects the official portrait.");
            return png;
        };
        var cache=new GeneralGameCoverCache(root,true,download);
        var first=cache.GetAsync("FALLOUT4",CancellationToken.None);
        var second=cache.GetAsync("fallout4",CancellationToken.None);
        Task.WhenAll(first,second).GetAwaiter().GetResult();
        using(var a=first.Result)using(var b=second.Result)
            Check(a!=null&&b!=null&&a.Width==400&&a.Height==600&&b.Width==400&&api==1&&image==1,
                "Simultaneous cards share a single transfer and receive independent images.");

        string path=Safe.In(root,"GameCovers/fallout4.jpg");
        Check(File.ReadAllBytes(path)[0]==0xff&&Directory.GetFiles(Path.GetDirectoryName(path)).Length==1,
            "The cache commits one validated normalized JPG.");
        var offline=new GeneralGameCoverCache(root,false,(uri,body,limit,token)=>{throw new IOException("Unexpected network call.");});
        using(var result=offline.GetAsync("fallout4",CancellationToken.None).GetAwaiter().GetResult())
            Check(result!=null&&result.Height==600,"A new offline cache instance displays the saved cover.");
        Check(offline.GetAsync("missing",CancellationToken.None).GetAwaiter().GetResult()==null&&
            offline.GetAsync("../fallout4",CancellationToken.None).GetAwaiter().GetResult()==null,"Unknown or invalid domains keep the placeholder.");

        File.WriteAllText(path,"bad cache");
        using(var result=cache.GetAsync("fallout4",CancellationToken.None).GetAwaiter().GetResult())
            Check(result!=null&&api==2&&image==2,"A corrupt cache file can be replaced on the next successful lookup.");

        var unknown=new GeneralGameCoverCache(root,true,(uri,body,limit,token)=>{
            Check(body!=null,"A mismatched domain never reaches the image CDN.");
            return System.Text.Encoding.UTF8.GetBytes("{\"data\":{\"game\":{\"id\":1151,\"domainName\":\"fallout4\",\"artworkSchema\":\"V2\"}}}");
        });
        Check(unknown.GetAsync("anothergame",CancellationToken.None).GetAwaiter().GetResult()==null,
            "The API response must match the requested domain exactly.");
        using(var canceled=new CancellationTokenSource()){
            canceled.Cancel();
            bool stopped=false;
            try{cache.GetAsync("fallout4",canceled.Token).GetAwaiter().GetResult();}
            catch(OperationCanceledException){stopped=true;}
            Check(stopped,"A canceled card load exits promptly.");
        }
    }

    static void AddSource(GeneralModState state,string source){
        var variant=new GeneralModVariant();
        variant.Sources.Add(source);
        var mod=new GeneralModEntry();
        mod.Variants.Add(variant);
        state.List.Mods.Add(mod);
    }
    static byte[] Png(int width,int height){
        using(var image=new Bitmap(width,height))
        using(var stream=new MemoryStream()){
            image.Save(stream,ImageFormat.Png);
            return stream.ToArray();
        }
    }
    static void Check(bool success,string message){
        if(!success)throw new IOException(message);
    }
}