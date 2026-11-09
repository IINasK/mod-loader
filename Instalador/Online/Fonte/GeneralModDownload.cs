using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

internal sealed class GeneralModDownload : IDisposable {
    readonly OnlineDownloadWatch[] watches;
    readonly FixedAsset asset;
    readonly DateTime startedUtc;
    static readonly Regex WindowsDevice = new Regex(@"^(?:CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|COM[1-9¹²³]|LPT[1-9¹²³]) *(?:\.|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal GeneralModDownload(string downloadFolder, FixedAsset asset) {
        if (asset == null) throw new ArgumentNullException("asset");
        this.asset = new FixedAsset {
            Id = asset.Id, Name = asset.Name, Sha256 = asset.Sha256, Size = asset.Size,
            Format = asset.Format, Url = asset.Url, ModId = asset.ModId, FileId = asset.FileId
        };
        watches = WatchFolders(downloadFolder).Select(folder=>new OnlineDownloadWatch(folder)).ToArray();
        startedUtc = DateTime.UtcNow;
        foreach(var watch in watches)watch.Arm(new[] { this.asset });
    }

    internal bool HasPendingCandidates { get { return watches.Any(watch=>watch.HasPendingCandidates); } }

    internal string Poll(Action<long, long> progress = null) {
        foreach(var watch in watches){
            try{var match=watch.Poll(progress);if(match!=null)return match.Path;}
            catch(IOException){}catch(UnauthorizedAccessException){}
        }
        return null;
    }

    internal BrowserDownloadSnapshot Progress() {
        foreach(var watch in watches.Where(item=>item.IsArmed)){
            try{var progress=BrowserDownloadProgress.Read(watch.Folder,asset,startedUtc);if(progress.Active)return progress;}
            catch(IOException){}catch(UnauthorizedAccessException){}
        }
        return new BrowserDownloadSnapshot();
    }

    public void Dispose() { foreach(var watch in watches)watch.Cancel(); }

    static IEnumerable<string> WatchFolders(string configured) {
        foreach(var folder in new[]{configured,OnlineDownloadWatch.DefaultFolder()}.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase)){
            string verified;
            try{verified=new OnlineDownloadWatch(folder).Folder;}
            catch(IOException){continue;}catch(UnauthorizedAccessException){continue;}
            yield return verified;
        }
    }

    internal static string FindCompleted(string configured,string source,GeneralSourceInfo info) {
        var link=GeneralNexusLink.Parse(source);
        if(link==null||link.File==0||info==null||info.Bytes<=0||info.SizeApproximate)return null;
        bool hyst=link.Game=="cyberpunk2077"&&(link.Mod==9083&&link.File==170228||link.Mod==4654&&link.File==170224);
        string file=info.FileName;
        if(!hyst&&(String.IsNullOrEmpty(file)||file.IndexOfAny(Path.GetInvalidFileNameChars())>=0))return null;
        string pattern=hyst?"*.zip":Path.GetFileNameWithoutExtension(file)+"*"+Path.GetExtension(file);
        foreach(string folder in WatchFolders(configured)){
            try{
                foreach(string path in Directory.EnumerateFiles(folder,pattern,SearchOption.TopDirectoryOnly).Take(10000))
                    if(MatchesCompleted(path,source,info,null))return path;
            }catch(IOException){}catch(UnauthorizedAccessException){}
        }
        return null;
    }

    internal static bool MatchesCompleted(string path,string source,GeneralSourceInfo info,FixedAsset fixedAsset) {
        if(!IsArchivePath(path))return false;
        var link=GeneralNexusLink.Parse(source);
        bool pushUp=link!=null&&link.Game=="cyberpunk2077"&&link.Mod==9083&&link.File==170228;
        bool natural=link!=null&&link.Game=="cyberpunk2077"&&link.Mod==4654&&link.File==170224;
        long bytes=fixedAsset!=null?fixedAsset.Size:pushUp?185225676:natural?185058002:info==null||info.SizeApproximate?0:info.Bytes;
        if(bytes<=0)return false;
        string expected=info==null?null:info.FileName;
        string name=Path.GetFileName(path);
        bool publishedName=!String.IsNullOrEmpty(expected)&&Path.GetFileName(expected)==expected&&
            (String.Equals(name,expected,StringComparison.OrdinalIgnoreCase)||String.Equals(
                Regex.Replace(name,@" \([0-9]+\)(?=\.[^.]+$)",""),expected,StringComparison.OrdinalIgnoreCase));
        if(fixedAsset==null&&!pushUp&&!natural&&(!publishedName||link==null||link.File==0))return false;
        if(natural&&!publishedName&&(!name.EndsWith(".zip",StringComparison.OrdinalIgnoreCase)||!Regex.IsMatch(name,@"(?:^|[ _-])4654(?:[ _-]|$)")))return false;
        try{
            Safe.NoLinks(path);
            if(new FileInfo(path).Length!=bytes)return false;
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){
                if(stream.Length!=bytes)return false;
                if(fixedAsset!=null||pushUp){
                    string sha=fixedAsset==null?"57b106087f766589616b3f98d0851f643657abae2a778bc42a0a05b314488a82":fixedAsset.Sha256;
                    using(var hash=System.Security.Cryptography.SHA256.Create())return Safe.Hex(hash.ComputeHash(stream))==sha;
                }
                if(natural)using(var zip=new ZipArchive(stream,ZipArchiveMode.Read))
                    return zip.Entries.Any(entry=>String.Equals(entry.FullName.Replace('\\','/'),"archive/pc/mod/EBBRB.xl",StringComparison.OrdinalIgnoreCase));
                return true;
            }
        }catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}catch(InvalidDataException){return false;}
    }

    internal static bool IsArchivePath(string path) {
        if (String.IsNullOrWhiteSpace(path) || path.Length < 4 || path[1] != ':' ||
            !((path[0] >= 'A' && path[0] <= 'Z') || (path[0] >= 'a' && path[0] <= 'z')) ||
            (path[2] != '\\' && path[2] != '/') || path.Any(Char.IsControl)) return false;

        char[] invalid = Path.GetInvalidFileNameChars();
        var parts = path.Substring(3).Split('\\', '/');
        if (parts.Any(part => part.Length == 0 || part.EndsWith(".", StringComparison.Ordinal) ||
            part.EndsWith(" ", StringComparison.Ordinal) || part.IndexOfAny(invalid) >= 0 || WindowsDevice.IsMatch(part))) return false;

        string extension = Path.GetExtension(path);
        return new[] { ".zip", ".rar", ".7z" }.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }
    internal static bool IsInstalledSource(string source){return source!=null&&Regex.IsMatch(source,@"\Ainstalled://[A-Za-z0-9_-]{1,120}/[A-Za-z0-9_-]{1,120}\z");}

    static Uri ParseWebSource(string source) {
        Uri uri;
        if (String.IsNullOrWhiteSpace(source) || source.Length > 4096 || source.IndexOf('\\') >= 0 ||
            source.Any(c => Char.IsControl(c) || Char.IsWhiteSpace(c)) ||
            !source.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            !Uri.TryCreate(source, UriKind.Absolute, out uri) || !uri.IsWellFormedOriginalString() ||
            uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 || !uri.IsDefaultPort || uri.IsLoopback)
            return null;
        return uri;
    }

    internal static bool IsWebSource(string source) { return ParseWebSource(source) != null; }

    internal static ProcessStartInfo BrowserRequest(string url) {
        Uri uri = ParseWebSource(url);
        if (uri == null) throw new IOException("Use um link HTTPS válido, sem credenciais no endereço.");
        return new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true, Verb = "open" };
    }

    internal static void OpenSource(string url) {
        using (Process.Start(BrowserRequest(url))) { }
    }
}
