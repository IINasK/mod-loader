using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public sealed class GeneralInstallRoute {
    public string Folder, Destination;
}

internal static class GeneralInstallRoutes {
    const string Prefix = "__modloader_destination__/";
    const string FileMarker = "/__files__/";
    [ThreadStatic] internal static string FixtureAppData;
    static readonly string[] Locations = { "%GAME%", "%DOCUMENTS%", "%SAVEDGAMES%", "%LOCALAPPDATA%", "%APPDATA%", "%USERPROFILE%" };

    internal static List<GeneralInstallRoute> Parse(string text, string game) {
        var routes = new List<GeneralInstallRoute>();
        foreach (string row in text.Split(new[] { '\r', '\n', '|' }, StringSplitOptions.RemoveEmptyEntries)) {
            if (String.IsNullOrWhiteSpace(row)) continue;
            var fields = row.Split(new[] { '=' }, 2);
            string folder = fields.Length == 1 || String.IsNullOrWhiteSpace(fields[0]) ? "" : Relative(fields[0].Trim().Replace('\\', '/').TrimEnd('/'));
            string destination = fields.Last().Trim().Replace('\\', '/').TrimEnd('/');
            if (Path.IsPathRooted(destination)) {
                if (destination.StartsWith("/", StringComparison.Ordinal) || destination.Length < 3 || destination[1] != ':' || destination[2] != '/')
                    throw Invalid();
                string absolute = Path.GetFullPath(destination).TrimEnd('\\');
                var anchors = Locations.Select(token => new { Token = token, Path = Base(game, token) }).Where(item => !String.IsNullOrEmpty(item.Path))
                    .OrderByDescending(item => item.Path.Length);
                var match = anchors.FirstOrDefault(item => absolute.Equals(item.Path, StringComparison.OrdinalIgnoreCase) ||
                    absolute.StartsWith(item.Path + "\\", StringComparison.OrdinalIgnoreCase));
                if (match == null) throw Invalid();
                destination = match.Token + absolute.Substring(match.Path.Length).Replace('\\', '/');
            } else if (!destination.StartsWith("%", StringComparison.Ordinal)) {
                if (destination.Length == 0) throw Invalid();
                destination = "%GAME%/" + destination;
            } else {
                int slash = destination.IndexOf('/');
                string token = slash < 0 ? destination : destination.Substring(0, slash);
                destination = token.ToUpperInvariant() + destination.Substring(token.Length);
            }
            routes.Add(new GeneralInstallRoute { Folder = folder, Destination = destination });
        }
        Validate(routes);
        return routes;
    }

    internal static string Format(IEnumerable<GeneralInstallRoute> routes) {
        return String.Join(" | ", routes.Select(route => String.IsNullOrEmpty(route.Folder)
            ? route.Destination : route.Folder + " = " + route.Destination));
    }

    internal static void Validate(List<GeneralInstallRoute> routes) {
        if (routes == null || routes.Count > 128) throw Invalid();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var route in routes) {
            if (route == null || route.Folder == null || !used.Add(route.Folder)) throw Invalid();
            if (route.Folder.Length != 0) Relative(route.Folder);
            SplitDestination(route.Destination);
        }
    }

    static string Relative(string path) {
        if (String.IsNullOrEmpty(path) || path.Length > 300 || path.Split('/').Any(part =>
            part.Equals("__files__", StringComparison.OrdinalIgnoreCase) || part.Equals(Prefix.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)))
            throw Invalid();
        Safe.In(Path.GetTempPath(), path);
        return path;
    }

    static string[] SplitDestination(string destination) {
        if (String.IsNullOrEmpty(destination)) throw Invalid();
        var parts = destination.Split(new[] { '/' }, 2);
        if (!Locations.Contains(parts[0]) || parts.Length == 1 && parts[0] != "%GAME%") throw Invalid();
        if (parts.Length == 2) {
            Relative(parts[1]);
            if(parts[0]=="%USERPROFILE%"&&new[]{"AppData","Documents","Saved Games"}.Contains(parts[1].Split('/')[0],StringComparer.OrdinalIgnoreCase))
                throw Invalid();
            if (parts[0] != "%GAME%" && parts[1].Split('/').Any(part =>
                part.StartsWith(".", StringComparison.Ordinal) ||
                new[] { "Microsoft", "Packages", "ModLoader", "CyberpunkCoop", "Startup", "Temp", "Google", "Mozilla", "BraveSoftware" }
                    .Contains(part, StringComparer.OrdinalIgnoreCase))) throw Invalid();
        }
        return parts;
    }

    static string Base(string game, string token) {
        string path;
        switch (token) {
            case "%GAME%": path = game; break;
            case "%DOCUMENTS%": path = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); break;
            case "%SAVEDGAMES%":
                string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                path = String.IsNullOrEmpty(profile) ? null : Path.Combine(profile, "Saved Games"); break;
            case "%LOCALAPPDATA%": path = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); break;
            case "%USERPROFILE%": path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); break;
            default: path = FixtureAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); break;
        }
        return String.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path).TrimEnd('\\');
    }

    internal static string DirectoryFor(string game, string destination, bool mustExist) {
        var parts = SplitDestination(destination);
        string root = Base(game, parts[0]);
        if (root == null || root.StartsWith(@"\\", StringComparison.Ordinal)) throw new DirectoryNotFoundException(destination);
        string path = parts.Length == 1 ? root : Safe.In(root, parts[1]);
        if (parts[0] != "%GAME%" && !String.IsNullOrEmpty(game) &&
            (path + "\\").StartsWith(Path.GetFullPath(game).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
            throw Invalid();
        if (mustExist && !Directory.Exists(path)) throw new DirectoryNotFoundException(
            GeneralStyle.T("Pasta de instalação ausente: ", "Installation folder missing: ") + destination);
        return path;
    }

    internal static bool Same(List<GeneralInstallRoute> first, List<GeneralInstallRoute> second) {
        return String.Equals(Format(first), Format(second), StringComparison.OrdinalIgnoreCase);
    }

    internal static string[] ExternalFolders(string game, IEnumerable<GeneralInstallRoute> routes) {
        return routes.Where(route => !route.Destination.StartsWith("%GAME%", StringComparison.Ordinal))
            .Select(route => DirectoryFor(game,route.Destination,true)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static void CheckFolders(string game, IEnumerable<GeneralInstallRoute> routes) {
        foreach (var route in routes) DirectoryFor(game, route.Destination, true);
    }

    internal static string Map(string path, IEnumerable<GeneralInstallRoute> routes) {
        if (External(path)) throw Invalid();
        var route = routes.Where(item => item.Folder.Length == 0 || path.Equals(item.Folder, StringComparison.OrdinalIgnoreCase) || path.StartsWith(item.Folder + "/", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Folder.Length).FirstOrDefault();
        if (route == null) return path;
        string remainder = path;
        if (route.Folder.Length != 0)
            remainder = path.Length == route.Folder.Length ? Path.GetFileName(path) : path.Substring(route.Folder.Length + 1);
        if (route.Destination == "%GAME%") return remainder;
        if (route.Destination.StartsWith("%GAME%/", StringComparison.Ordinal)) return route.Destination.Substring(7) + "/" + remainder;
        return Prefix + route.Destination + FileMarker + remainder;
    }

    internal static bool External(string path) { return path != null && path.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase); }

    static string[] SplitExternal(string path) {
        string value = path.Substring(Prefix.Length);
        int marker = value.IndexOf(FileMarker, StringComparison.Ordinal);
        if (marker < 0) throw Invalid();
        string destination = value.Substring(0, marker);
        if (SplitDestination(destination)[0] == "%GAME%") throw Invalid();
        return new[] { destination, Relative(value.Substring(marker + FileMarker.Length)) };
    }

    internal static string ExternalDestination(string path) { return SplitExternal(path)[0]; }

    internal static string Resolve(string root, string path, bool mustExist = false) {
        if (!External(path)) return Safe.In(root, path);
        var parts = SplitExternal(path);
        if(!InstalledSnapshot.SafeGenericPath(parts[1]))throw Invalid();
        return Safe.In(DirectoryFor(root, parts[0], mustExist), parts[1]);
    }

    internal static void CreateParents(string root, string relative, List<string> created) {
        var pending = new Stack<string>();
        for (string path = Path.GetDirectoryName(Resolve(root, relative, true)); !Directory.Exists(path); path = Path.GetDirectoryName(path))
            pending.Push(path);
        while (pending.Count != 0) {
            string path = pending.Pop();
            Safe.NoLinks(path);
            Directory.CreateDirectory(path);
            created.Add(path);
        }
    }

    static IOException Invalid() {
        return new IOException(GeneralStyle.T("Use uma pasta do jogo ou uma subpasta local do seu usuário.",
            "Use a game folder or a local subfolder in your user profile."));
    }
}
