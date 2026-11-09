using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

internal static class BrowserDownloadBridge {
    internal const string Host = "com.iinask.modloader.downloads";
    internal const string Origin = "chrome-extension://gocflffklneahipjecakpcjhgjlgjchn/";
    const int MaxMessageBytes = 24 * 1024;
    static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

    internal static string Channel {
        get {
            using (var user = WindowsIdentity.GetCurrent())
            using (var process = Process.GetCurrentProcess())
                return "ModLoaderBrowser-" + user.User.Value + "-" + process.SessionId + (GeneralAppPaths.IsDevelopment ? "-dev" : "");
        }
    }

    internal static string Prepare(string executable) {
        string root = Safe.In(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModLoader/BrowserIntegration");
        string extension = Safe.In(root, "Extension");
        Directory.CreateDirectory(extension);
        foreach (string file in new[] { "manifest.json", "downloads.js" }) {
            string path = Safe.In(extension, file);
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("ModLoader.Browser." + file))
            using (var reader = new StreamReader(resource, Utf8))
                File.WriteAllText(path, reader.ReadToEnd(), Utf8);
        }
        string manifest = Safe.In(root, Host + ".json");
        Safe.WriteJson(manifest, new {
            name = Host, description = "Mod Loader download notifications", path = executable,
            type = "stdio", allowed_origins = new[] { Origin }
        });
        foreach (string browser in new[] { @"Google\Chrome", @"Microsoft\Edge", @"BraveSoftware\Brave-Browser" })
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\" + browser + @"\NativeMessagingHosts\" + Host))
                key.SetValue("", manifest, RegistryValueKind.String);
        return extension;
    }

    internal static int RunHost(string origin, Stream input, Stream output, string channel = null) {
        if (!String.Equals(origin, Origin, StringComparison.Ordinal)) return 1;
        bool delivered = false;
        try {
            string path = ReadPath(input).GetAwaiter().GetResult();
            if (path != null) delivered = Forward(path, channel ?? Channel);
        } catch (IOException) { }
        WriteFrame(output, new { delivered = delivered });
        return 0;
    }

    internal static bool Forward(string path, string channel) {
        if (!GeneralModDownload.IsArchivePath(path)) return false;
        try {
            using (var client = new NamedPipeClientStream(".", channel, PipeDirection.Out,
                PipeOptions.None, TokenImpersonationLevel.Identification)) {
                client.Connect(600);
                WriteFrame(client, new { path = path });
            }
            return true;
        } catch (TimeoutException) { return false; }
          catch (IOException) { return false; }
          catch (UnauthorizedAccessException) { return false; }
    }

    internal static void WriteFrame(Stream stream, object message) {
        byte[] payload = Utf8.GetBytes(Safe.Json.Serialize(message));
        if (payload.Length > MaxMessageBytes) throw new IOException("Browser notification too large.");
        byte[] length = BitConverter.GetBytes(payload.Length);
        stream.Write(length, 0, length.Length);
        stream.Write(payload, 0, payload.Length);
        stream.Flush();
    }

    internal static async Task<string> ReadPath(Stream stream) {
        byte[] header = new byte[4];
        if (!await ReadAll(stream, header).ConfigureAwait(false)) return null;
        int length = BitConverter.ToInt32(header, 0);
        if (length < 1 || length > MaxMessageBytes) return null;
        byte[] payload = new byte[length];
        if (!await ReadAll(stream, payload).ConfigureAwait(false)) return null;
        try {
            var message = Safe.Json.DeserializeObject(Utf8.GetString(payload)) as Dictionary<string, object>;
            object value;
            string path = message != null && message.Count == 1 && message.TryGetValue("path", out value) ? value as string : null;
            return path != null && path.Length <= 4096 && GeneralModDownload.IsArchivePath(path) ? path : null;
        } catch (ArgumentException) { return null; }
          catch (InvalidOperationException) { return null; }
    }

    static async Task<bool> ReadAll(Stream stream, byte[] data) {
        int offset = 0;
        while (offset < data.Length) {
            int count = await stream.ReadAsync(data, offset, data.Length - offset).ConfigureAwait(false);
            if (count == 0) return false;
            offset += count;
        }
        return true;
    }
}

internal sealed class BrowserDownloadInbox : IDisposable {
    readonly ConcurrentQueue<string> completed = new ConcurrentQueue<string>();
    readonly NamedPipeServerStream pipe;
    volatile bool disposed;

    internal static BrowserDownloadInbox Listen(string channel) {
        if (channel == null) return null;
        try { return new BrowserDownloadInbox(channel); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    BrowserDownloadInbox(string channel) {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        using (var user = WindowsIdentity.GetCurrent())
            security.AddAccessRule(new PipeAccessRule(user.User, PipeAccessRights.FullControl, AccessControlType.Allow));
        pipe = new NamedPipeServerStream(channel, PipeDirection.In, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 4096, 4096, security);
        Task.Run((Func<Task>)Receive);
    }

    async Task Receive() {
        while (!disposed) {
            try { await pipe.WaitForConnectionAsync().ConfigureAwait(false); }
            catch (IOException) { return; }
            catch (ObjectDisposedException) { return; }

            try {
                var read = BrowserDownloadBridge.ReadPath(pipe);
                if (await Task.WhenAny(read, Task.Delay(1500)).ConfigureAwait(false) == read) {
                    string path = await read.ConfigureAwait(false);
                    if (path != null && completed.Count < 16) completed.Enqueue(path);
                } else {
                    Task ignored = read.ContinueWith(task => GC.KeepAlive(task.Exception), TaskContinuationOptions.OnlyOnFaulted);
                }
            } catch (IOException) { }
              catch (ObjectDisposedException) { return; }

            try { pipe.Disconnect(); }
            catch (IOException) { return; }
            catch (InvalidOperationException) { return; }
        }
    }

    internal string TakeMatch(string source, GeneralSourceInfo info, FixedAsset asset) {
        string path;
        while (completed.TryDequeue(out path))
            if (GeneralModDownload.MatchesCompleted(path, source, info, asset)) return path;
        return null;
    }

    public void Dispose() { disposed = true; pipe.Dispose(); }
}
