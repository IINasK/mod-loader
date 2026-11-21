using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class GeneralModUpdateHistory {
    internal sealed class Entry {
        public string Id, ModId, Owner, Destination, Source, Version, PreviousVersion, Backup, Status;
        public long InstalledUtc;
        public bool RequiresManualConfirmation;
        public Dictionary<string,string> Modules = new Dictionary<string,string>();
    }

    internal sealed class Target {
        public string Destination, Executable;
    }

    readonly string folder;
    internal GeneralModUpdateHistory(string data) {
        folder=Safe.In(data,"UpdateHistory");
    }

    internal Entry[] Read() {
        Safe.NoLinks(folder);
        if(!Directory.Exists(folder))return new Entry[0];
        var items=new List<Entry>();
        foreach(string path in Directory.EnumerateFiles(folder,"*.json")){
            var file=new FileInfo(path);
            Guid id;
            if(!Guid.TryParseExact(Path.GetFileNameWithoutExtension(file.Name),"N",out id))continue;
            try{
                Safe.NoLinks(path);
                if(file.Length>4*1024*1024)continue;
                var entry=Safe.Json.Deserialize<Entry>(File.ReadAllText(path));
                if(entry!=null&&entry.Id==id.ToString("N")&&entry.InstalledUtc>0&&entry.InstalledUtc<=DateTime.MaxValue.Ticks&&
                    entry.Owner!=null&&entry.ModId!=null&&entry.Destination!=null&&entry.Source!=null&&entry.Modules!=null&&
                    new[]{"pending","loaded","crash","confirmed"}.Contains(entry.Status))items.Add(entry);
            }catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}catch(InvalidOperationException){}
        }
        return items.OrderByDescending(entry=>entry.InstalledUtc).ToArray();
    }

    string EntryPath(string id) {
        Guid key;
        if(!Guid.TryParseExact(id,"N",out key)||id!=key.ToString("N"))throw new IOException("Histórico de atualização inválido.");
        return Safe.In(folder,id+".json");
    }

    internal Entry Record(string root,GeneralModInstaller.Record receipt) {
        var upgrade=receipt.Upgrade;
        if(upgrade==null||upgrade.HistoryUtc==0)return null;
        string path=EntryPath(receipt.TransactionId);
        Safe.NoLinks(path);
        if(File.Exists(path))return null;
        var entry=new Entry{
            Id=receipt.TransactionId,ModId=upgrade.Owner.Split('/')[0],Owner=upgrade.Owner,Destination=Path.GetFullPath(root),
            Source=upgrade.Source,Version=upgrade.Version,PreviousVersion=upgrade.HistoryPreviousVersion,
            Backup=upgrade.HistoryBackup,InstalledUtc=upgrade.HistoryUtc,Status="pending",
            RequiresManualConfirmation=receipt.Files.Any(file=>!IsNativeModule(file.Path)&&
                !new[]{".ini",".toml",".cfg",".md",".txt"}.Contains(Path.GetExtension(file.Path),StringComparer.OrdinalIgnoreCase)),
            Modules=receipt.Files.Where(file=>IsNativeModule(file.Path))
                .ToDictionary(file=>file.Path,file=>file.Sha256,StringComparer.OrdinalIgnoreCase)
        };
        Save(entry,true);
        return entry;
    }

    internal static bool IsNativeModule(string path) {
        string extension=Path.GetExtension(path);
        return String.Equals(extension,".dll",StringComparison.OrdinalIgnoreCase)||
            String.Equals(extension,".asi",StringComparison.OrdinalIgnoreCase);
    }

    internal void Save(Entry entry,bool create=false) {
        string path=EntryPath(entry.Id);
        Safe.NoLinks(path);
        if(!create&&!File.Exists(path))return;
        Safe.WriteJson(path,entry);
    }

    internal Target ReadTarget() {
        string path=Safe.In(folder,"executable.json");
        Safe.NoLinks(path);
        if(!File.Exists(path)||new FileInfo(path).Length>16384)return null;
        return Safe.Json.Deserialize<Target>(File.ReadAllText(path));
    }

    internal void SaveTarget(Target target) {
        string path=Safe.In(folder,"executable.json");
        Safe.NoLinks(path);
        Safe.WriteJson(path,target);
    }

    internal string FindBackup(Entry entry) {
        string name=entry.Backup;
        if(String.IsNullOrWhiteSpace(name)||Path.GetFileName(name)!=name||
            !name.EndsWith(".zip",StringComparison.OrdinalIgnoreCase))return null;
        var path=Safe.In(Path.GetDirectoryName(folder),"BackupsAtualizacoes/"+name);
        Safe.NoLinks(path);
        return File.Exists(path)?path:null;
    }
}

internal sealed partial class GeneralModWindow {
    GeneralModUpdateHistory updateHistory;
    GeneralModUpdateHistory.Entry[] updateEntries = new GeneralModUpdateHistory.Entry[0];
    string updateHistoryData, updateExecutableRoot, configuredUpdateExecutable;
    readonly GeneralModRuntimeMonitor updateRuntime = new GeneralModRuntimeMonitor();
    Timer updateObservationTimer;
    bool observingUpdateRuntime;

    void StartUpdateObservation() {
        if(testMode)return;
        updateObservationTimer=new Timer{Interval=2000};
        updateObservationTimer.Tick+=(s,e)=>ObserveUpdates();
        updateObservationTimer.Start();
        Disposed+=(s,e)=>{
            updateObservationTimer.Dispose();
            if(!observingUpdateRuntime)updateRuntime.Dispose();
        };
        if(grid!=null)grid.Invalidate();
        ObserveUpdates();
    }

    GeneralModUpdateHistory History() {
        if(updateHistory!=null&&InventoryPaths.Equals(data,updateHistoryData))return updateHistory;
        updateHistoryData=data;
        updateHistory=new GeneralModUpdateHistory(data);
        updateEntries=new GeneralModUpdateHistory.Entry[0];
        updateExecutableRoot=null;
        configuredUpdateExecutable=null;
        try{updateEntries=updateHistory.Read();}
        catch(IOException ex){OnlineDiagnostics.Failure("mods.history.read",ex,"Ler as atualizações recentes.");}
        catch(UnauthorizedAccessException ex){OnlineDiagnostics.Failure("mods.history.read",ex,"Ler as atualizações recentes.");}
        return updateHistory;
    }

    internal GeneralModUpdateHistory.Entry[] UpdateHistoryFor(string modId) {
        History();
        return updateEntries.Where(entry=>entry.ModId==modId).ToArray();
    }

    GeneralModUpdateHistory.Entry CurrentUpdateHistory(GeneralModEntry mod) {
        if(mod==null||String.IsNullOrWhiteSpace(state.Destination))return null;
        History();
        if(!updateEntries.Any(entry=>entry.ModId==mod.Id))return null;
        var work=MakeWork(mod);
        if(work.Variant.Sources.Count!=1)return null;
        string root=Path.GetFullPath(state.Destination);
        var latest=updateEntries.FirstOrDefault(entry=>entry.Owner==work.Key&&InventoryPaths.Equals(entry.Destination,root));
        return latest!=null&&latest.Source==work.Variant.Sources[0]?latest:null;
    }

    bool HasPendingUpdateHistory(GeneralModEntry mod) {
        var entry=CurrentUpdateHistory(mod);
        return entry!=null&&entry.Status!="confirmed";
    }

    bool CanConfirmUpdateHistory(string id) {
        if(working||scanning||associationChoice!=null||IsDisposed)return false;
        History();
        var requested=updateEntries.FirstOrDefault(entry=>entry.Id==id&&entry.Status!="confirmed");
        if(requested==null)return false;
        var mod=state.List.Mods.FirstOrDefault(item=>item.Id==requested.ModId);
        return CurrentUpdateHistory(mod)==requested;
    }

    void ConfirmUpdateHistory(string id) {
        if(!CanConfirmUpdateHistory(id))return;
        var entry=updateEntries.Single(row=>row.Id==id);
        var next=GeneralPresetProfiles.Copy(entry);
        next.Status="confirmed";
        History().Save(next);
        entry.Status=next.Status;
        grid.Invalidate();
    }


    void RecordUpdateHistory(string root,GeneralModInstaller.Record record) {
        try{
            var entry=History().Record(root,record);
            if(entry==null)return;
            updateEntries=updateEntries.Concat(new[]{entry}).OrderByDescending(item=>item.InstalledUtc).ToArray();
            if(!IsDisposed&&grid!=null)grid.Invalidate();
        }catch(IOException ex){OnlineDiagnostics.Failure("mods.history.save",ex,"Registrar a atualização.");}
         catch(UnauthorizedAccessException ex){OnlineDiagnostics.Failure("mods.history.save",ex,"Registrar a atualização.");}
    }

    string UpdateExecutable() {
        if(String.IsNullOrWhiteSpace(state.Destination))return null;
        string root=Path.GetFullPath(state.Destination);
        var history=History();
        if(InventoryPaths.Equals(updateExecutableRoot,root))return configuredUpdateExecutable;
        updateExecutableRoot=root;
        configuredUpdateExecutable=null;
        try{
            var target=history.ReadTarget();
            if(target!=null&&InventoryPaths.Equals(target.Destination,root)){
                var path=Safe.In(root,target.Executable);
                Safe.NoLinks(path);
                if(Path.GetExtension(path).Equals(".exe",StringComparison.OrdinalIgnoreCase)&&File.Exists(path))
                    configuredUpdateExecutable=path;
            }
            if(configuredUpdateExecutable==null&&IsCyberpunk(root)){
                var path=Safe.In(root,"bin/x64/Cyberpunk2077.exe");
                Safe.NoLinks(path);
                configuredUpdateExecutable=path;
            }
        }catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}catch(InvalidOperationException){}
        return configuredUpdateExecutable;
    }

    void ChooseUpdateExecutable() {
        if(working||scanning||String.IsNullOrWhiteSpace(state.Destination)||!Directory.Exists(state.Destination))return;
        string root=Path.GetFullPath(state.Destination).TrimEnd(Path.DirectorySeparatorChar);
        using(var picker=new OpenFileDialog{Title=T("Executável do jogo","Game executable"),InitialDirectory=root,
            Filter=T("Executável do Windows (*.exe)|*.exe","Windows executable (*.exe)|*.exe"),CheckFileExists=true}){
            if(picker.ShowDialog(this)!=DialogResult.OK)return;
            try{
                string selected=Path.GetFullPath(picker.FileName);
                string prefix=root+Path.DirectorySeparatorChar;
                if(!selected.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||
                    !Path.GetExtension(selected).Equals(".exe",StringComparison.OrdinalIgnoreCase))
                    throw new IOException(T("Escolha o executável dentro da pasta de instalação do jogo.",
                        "Choose the executable inside the game installation folder."));
                Safe.NoLinks(selected);
                History().SaveTarget(new GeneralModUpdateHistory.Target{Destination=root,Executable=selected.Substring(prefix.Length)});
                updateExecutableRoot=null;
            }catch(Exception ex){ShowError(ex);}
        }
    }

    async void ObserveUpdates() {
        if(observingUpdateRuntime||working||scanning||loadingPreset||startupLoading||exitApproved||IsDisposed)return;
        var history=History();
        if(!updateEntries.Any(entry=>entry.Status!="confirmed"))return;
        string executable=UpdateExecutable();
        if(executable==null)return;
        var entries=state.List.Mods.Select(CurrentUpdateHistory).Where(entry=>entry!=null&&entry.Status!="confirmed")
            .Select(GeneralPresetProfiles.Copy).ToArray();
        if(entries.Length==0)return;
        observingUpdateRuntime=true;
        try{
            await Task.Run(()=>updateRuntime.Observe(executable,entries));
            if(IsDisposed||exitApproved||history!=History()||!InventoryPaths.Equals(executable,UpdateExecutable()))return;
            bool changed=false;
            foreach(var observation in entries){
                var live=updateEntries.FirstOrDefault(entry=>entry.Id==observation.Id);
                if(live==null||live.Status==observation.Status||live.Status=="confirmed"||live.Status=="crash")continue;
                history.Save(observation);
                live.Status=observation.Status;
                changed=true;
            }
            if(changed)grid.Invalidate();
        }catch(IOException ex){OnlineDiagnostics.Failure("mods.history.observe",ex,"Verificar as atualizações em execução.");}
         catch(UnauthorizedAccessException ex){OnlineDiagnostics.Failure("mods.history.observe",ex,"Verificar as atualizações em execução.");}
         finally{
             observingUpdateRuntime=false;
             if(IsDisposed)updateRuntime.Dispose();
         }
    }
}
