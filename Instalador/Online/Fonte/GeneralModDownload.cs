using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

internal sealed class GeneralModDownload : IDisposable {
    readonly OnlineDownloadWatch watch;
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
        watch = new OnlineDownloadWatch(downloadFolder);
        startedUtc = DateTime.UtcNow;
        watch.Arm(new[] { this.asset });
    }

    internal bool HasPendingCandidates { get { return watch.HasPendingCandidates; } }

    internal string Poll(Action<long, long> progress = null) {
        var match = watch.Poll(progress);
        return match == null ? null : match.Path;
    }

    internal BrowserDownloadSnapshot Progress() {
        return watch.IsArmed ? BrowserDownloadProgress.Read(watch.Folder, asset, startedUtc) : new BrowserDownloadSnapshot();
    }

    public void Dispose() { watch.Cancel(); }

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
