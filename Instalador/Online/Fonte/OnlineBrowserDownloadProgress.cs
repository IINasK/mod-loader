using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

public sealed class BrowserDownloadSnapshot {
    public bool Active,Estimated;
    public long ReceivedBytes,TotalBytes;
    public int? Percent;
    public string Path;
}

// Partial-file length is a local estimate, never proof of completed bytes or archive identity.
public static class BrowserDownloadProgress {
    const long MaxArchive=8L*1024*1024*1024;
    public static BrowserDownloadSnapshot Read(string folder,FixedAsset asset,DateTime authorizedSinceUtc,long? publishedSizeKiB=null){
        if(asset==null||!FixedData.HashValid(asset.Sha256)||asset.Size<0||asset.Size>MaxArchive||String.IsNullOrWhiteSpace(asset.Name)||Path.GetFileName(asset.Name)!=asset.Name||asset.Name.IndexOfAny(Path.GetInvalidFileNameChars())>=0||asset.Name.Any(Char.IsControl))throw new IOException("Identidade do download inválida para acompanhar progresso.");
        if(authorizedSinceUtc.Kind!=DateTimeKind.Utc)throw new ArgumentException("A autorização do download precisa estar em UTC.","authorizedSinceUtc");
        if(publishedSizeKiB.HasValue&&(publishedSizeKiB.Value<=0||publishedSizeKiB.Value>MaxArchive/1024))throw new IOException("Estimativa de tamanho inválida.");
        string root=new OnlineDownloadWatch(folder).Folder;
        long total=asset.Size>0?asset.Size:publishedSizeKiB.GetValueOrDefault()*1024;
        var inactive=new BrowserDownloadSnapshot{TotalBytes=total};
        var candidates=new List<BrowserDownloadSnapshot>();
        var paths=Directory.EnumerateFiles(root,"*",SearchOption.TopDirectoryOnly).Take(10001).ToArray();
        if(paths.Length>10000)throw new IOException("A pasta de downloads tem arquivos demais para acompanhar o progresso.");
        foreach(string file in paths){
            string name=Path.GetFileName(file),archive;
            if(name.EndsWith(".crdownload",StringComparison.OrdinalIgnoreCase))archive=name.Substring(0,name.Length-11);
            else if(name.EndsWith(".part",StringComparison.OrdinalIgnoreCase))archive=name.Substring(0,name.Length-5);
            else continue;
            if(!ExpectedName(asset,archive))continue;
            try{
                Safe.NoLinks(file);var info=new FileInfo(file);
                if((info.Attributes&FileAttributes.ReparsePoint)!=0||info.CreationTimeUtc<authorizedSinceUtc||info.LastWriteTimeUtc<authorizedSinceUtc||info.Length<0||info.Length>MaxArchive)continue;
                // Browsers can preallocate a partial file, so even a known total yields estimated progress.
                candidates.Add(new BrowserDownloadSnapshot{Active=true,Estimated=true,Path=file,ReceivedBytes=info.Length,TotalBytes=total,Percent=total>0?(int?)Math.Min(99,(int)(Math.Min(info.Length,total)*100.0/total)):null});
            }catch(IOException){}catch(UnauthorizedAccessException){}
        }
        return candidates.Count==1?candidates[0]:inactive;
    }
    static bool ExpectedName(FixedAsset asset,string name){
        string extension=Path.GetExtension(name);
        if(!new[]{".zip",".rar",".7z"}.Contains(extension.ToLowerInvariant()))return false;
        string expectedExtension=Path.GetExtension(asset.Name);
        if(new[]{".zip",".rar",".7z"}.Contains(expectedExtension.ToLowerInvariant()))return Regex.IsMatch(name,"^"+Regex.Escape(Path.GetFileNameWithoutExtension(asset.Name))+@"(?: \([0-9]+\))?"+Regex.Escape(expectedExtension)+"$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        return asset.ModId>0&&(name.StartsWith(asset.Name+" ",StringComparison.OrdinalIgnoreCase)||name.StartsWith(asset.Name+"-",StringComparison.OrdinalIgnoreCase))&&Regex.IsMatch(name,@"(?:^|[ -])"+asset.ModId+@"(?:[ -]|$)",RegexOptions.CultureInvariant);
    }
}
