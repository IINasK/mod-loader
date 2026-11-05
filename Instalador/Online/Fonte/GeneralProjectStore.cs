using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

internal sealed class GeneralProject {
    public string Id, Name;
    public bool Legacy;
}

internal sealed class GeneralProjectIndex {
    public int Schema = 1;
    public string Theme = "light-red", Language = "pt";
    public string DownloadFolder = OnlineDownloadWatch.DefaultFolder();
    public List<GeneralProject> Projects = new List<GeneralProject>();
}

internal sealed class GeneralProjectStore {
    const int MaxIndexBytes = 1048576;
    readonly object gate = new object();
    internal string Root { get; private set; }
    string IndexPath { get { return Path.Combine(Root, "projects.json"); } }

    internal GeneralProjectStore(string root) {
        if (String.IsNullOrWhiteSpace(root)) throw new IOException("A pasta dos projetos está vazia.");
        Root = Path.GetFullPath(root);
        Safe.NoLinks(Root);
    }

    internal GeneralProjectIndex Read() {
        lock (gate) {
            Safe.NoLinks(IndexPath);
            if (File.Exists(IndexPath)) {
                try {
                    using (var stream = File.OpenRead(IndexPath)) {
                        if (stream.Length > MaxIndexBytes) throw new IOException("O índice de projetos excede 1 MB.");
                        using (var reader = new StreamReader(stream)) {
                            var fields = Safe.Json.DeserializeObject(reader.ReadToEnd()) as Dictionary<string, object>;
                            if (fields == null || !fields.ContainsKey("Schema") || !fields.ContainsKey("Projects")) throw new IOException("Formato do índice de projetos inválido.");
                            var index = Safe.Json.ConvertToType<GeneralProjectIndex>(fields);
                            Validate(index);
                            return index;
                        }
                    }
                } catch (ArgumentException ex) { throw new IOException("Não foi possível ler o índice de projetos. O arquivo foi preservado.", ex); }
                catch (InvalidOperationException ex) { throw new IOException("Não foi possível ler o índice de projetos. O arquivo foi preservado.", ex); }
            }
            var initial = new GeneralProjectIndex();
            string previous = Path.Combine(Root, "lista.json");
            Safe.NoLinks(previous);
            if (File.Exists(previous)) {
                var legacy = new GeneralProject { Id = "legacy", Name = "Projeto anterior", Legacy = true };
                try {
                    var state = new GeneralModLibrary(previous).Load();
                    if (state != null) {
                        var selected = state.List.Presets.FirstOrDefault(item => item.Id == state.SelectedPreset);
                        if (selected != null) legacy.Name = selected.Name.Length > 120 ? selected.Name.Substring(0, 120).Trim() : selected.Name;
                        initial.Theme = state.Theme; initial.Language = state.Language;
                        initial.DownloadFolder = state.DownloadFolder;
                    }
                } catch (IOException) { }
                initial.Projects.Add(legacy);
                Write(initial);
            }
            return initial;
        }
    }

    internal string DataPath(GeneralProject project) {
        ValidateProject(project);
        string path = project.Legacy ? Root : Safe.In(Root, "Projetos/" + project.Id);
        Safe.NoLinks(path);
        return path;
    }

    internal GeneralProject Create(string name, GeneralModList list, string theme, string language, string destination = null) {
        lock (gate) {
            name = ProjectName(name);
            ValidatePreferences(theme, language);
            list = list ?? new GeneralModList();
            GeneralModLibrary.ValidateList(list, true);
            var index = Read();
            if (index.Projects.Count >= 200) throw new IOException("Use no máximo 200 projetos nesta biblioteca.");
            var project = new GeneralProject { Id = Guid.NewGuid().ToString("N"), Name = name };
            string data = DataPath(project);
            var state = new GeneralModState {
                List = list, Theme = theme, Language = language, Destination = destination,
                DownloadFolder = index.DownloadFolder,
                SelectedPreset = list.Presets.Count == 0 ? null : list.Presets[0].Id
            };
            new GeneralModLibrary(Path.Combine(data, "lista.json")).Save(state);
            index.Projects.Add(project);
            Write(index);
            return project;
        }
    }

    internal GeneralModState Load(GeneralProject project) {
        lock (gate) {
            var registered = Registered(Read(), project);
            var state = new GeneralModLibrary(Path.Combine(DataPath(registered), "lista.json")).Load();
            if (state == null) throw new IOException("O arquivo deste projeto não foi encontrado. Nenhum dado foi substituído.");
            return state;
        }
    }

    internal void Delete(GeneralProject project) {
        lock (gate) {
            var index = Read();
            var registered = Registered(index, project);
            index.Projects.Remove(registered);
            Write(index);
        }
    }

    internal void SavePreferences(string theme, string language, string downloadFolder = null) {
        lock (gate) {
            ValidatePreferences(theme, language);
            var index = Read(); index.Theme = theme; index.Language = language;
            if (downloadFolder != null) index.DownloadFolder = DownloadPath(downloadFolder, true);
            Write(index);
        }
    }

    static GeneralProject Registered(GeneralProjectIndex index, GeneralProject project) {
        ValidateProject(project);
        var registered = index.Projects.FirstOrDefault(item => item.Id == project.Id && item.Legacy == project.Legacy);
        if (registered == null) throw new IOException("O projeto não pertence à biblioteca de projetos salvos.");
        return registered;
    }

    void Write(GeneralProjectIndex index) {
        Validate(index);
        if (Encoding.UTF8.GetByteCount(Safe.Json.Serialize(index)) > MaxIndexBytes) throw new IOException("O índice de projetos excede 1 MB.");
        Safe.NoLinks(IndexPath);
        Safe.WriteJson(IndexPath, index);
    }

    static void Validate(GeneralProjectIndex index) {
        if (index == null || index.Schema != 1 || index.Projects == null || index.Projects.Count > 200) throw new IOException("Índice de projetos inválido.");
        ValidatePreferences(index.Theme, index.Language);
        index.DownloadFolder = DownloadPath(String.IsNullOrWhiteSpace(index.DownloadFolder) ? OnlineDownloadWatch.DefaultFolder() : index.DownloadFolder, false);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var project in index.Projects) {
            ValidateProject(project);
            if (!ids.Add(project.Id)) throw new IOException("O índice contém um projeto repetido.");
        }
    }

    static void ValidateProject(GeneralProject project) {
        if (project == null || (project.Legacy ? project.Id != "legacy" : !Regex.IsMatch(project.Id ?? "", @"\A[a-f0-9]{32}\z"))) throw new IOException("Identificador de projeto inválido.");
        project.Name = ProjectName(project.Name);
    }

    static string ProjectName(string name) {
        if (String.IsNullOrWhiteSpace(name) || name.Length > 120 || name.Any(Char.IsControl)) throw new IOException("Informe um nome de projeto válido, com até 120 caracteres.");
        return name.Trim();
    }

    static void ValidatePreferences(string theme, string language) {
        if (theme != "light" && theme != "dark" && theme != "light-red") throw new IOException("Tema de projeto inválido.");
        if (language != "pt" && language != "en") throw new IOException("Idioma de projeto inválido.");
    }

    static string DownloadPath(string folder, bool requireExisting) {
        if (folder == null || folder.Any(Char.IsControl) || !Regex.IsMatch(folder, @"\A[A-Za-z]:[\\/].+")) throw new IOException("Escolha uma pasta local absoluta para os downloads.");
        string full = Path.GetFullPath(folder).TrimEnd('\\');
        if (full.Length <= 3) throw new IOException("Escolha uma pasta de downloads, não a raiz do disco.");
        Safe.NoLinks(full);
        // A missing old folder must remain editable from Home; only a new choice needs to exist now.
        return requireExisting || Directory.Exists(full) ? new OnlineDownloadWatch(full).Folder : full;
    }
}
