using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

internal sealed class PresetPackage {
    public string Key, StorageKey, LegacyKey, ModId, Name, Group;
    public List<string> RequiredMods=new List<string>();
    public List<DisabledModFile> Files=new List<DisabledModFile>();
    public bool Immutable;
}
internal sealed class PresetDeploymentState {
    public string Preset,Fingerprint;
    public List<PresetPackage> Active=new List<PresetPackage>();
    public Dictionary<string,string> Protected=new Dictionary<string,string>();
    public List<string> Issues=new List<string>();
}
internal sealed class PresetDeploymentJournal {
    public PresetDeploymentState Before;
    public List<PresetPackage> Outgoing=new List<PresetPackage>(),Incoming=new List<PresetPackage>();
    public Dictionary<string,List<string>> Created=new Dictionary<string,List<string>>();
}
// One journal per destination; DisabledMods retains verified content and serializes its own index.
internal sealed class GeneralPresetDeployment {
    readonly string root,stateFile,journalFile,lockFile;
    readonly DisabledMods storage;
    readonly Action checkClosed;
    internal Action<string> Checkpoint;
    internal Func<string,long> FreeBytes=path=>new DriveInfo(Path.GetPathRoot(path)).AvailableFreeSpace;
    internal GeneralPresetDeployment(string game,string data,Action checkClosed=null) {
        root=Path.GetFullPath(game).TrimEnd('\\');this.checkClosed=checkClosed??(()=>{});
        string id=InstalledSnapshot.Hash(Encoding.UTF8.GetBytes(root.ToUpperInvariant())).Substring(0,24);
        var folder=Safe.In(data,"Presets/"+id);
        stateFile=Safe.In(folder,"active.json");journalFile=Safe.In(folder,"pending.json");lockFile=Safe.In(folder,"operation.lock");
        storage=new DisabledMods(root,data,this.checkClosed);
    }
    internal PresetDeploymentState Read() {
        Safe.NoLinks(stateFile);
        var state=File.Exists(stateFile)?Safe.Json.Deserialize<PresetDeploymentState>(GeneralProjectStorage.ReadText(stateFile,12000000)):new PresetDeploymentState();
        if(state==null||state.Active==null||state.Protected==null||state.Issues==null||state.Protected.Count>1000||state.Issues.Count>4000)throw new IOException("Registro de preset inválido.");
        Validate(state.Active);return state;
    }
    void Validate(IEnumerable<PresetPackage> packages) {
        var keys=new HashSet<string>();int total=0;
        foreach(var package in packages) {
            if(package==null||package.ModId==null||package.Name==null||package.RequiredMods==null||package.Files==null||!keys.Add(package.Key)||
                !System.Text.RegularExpressions.Regex.IsMatch(package.Key??"",@"\A[A-Za-z0-9_-]{1,120}/[A-Za-z0-9_-]{1,120}\z")||
                !System.Text.RegularExpressions.Regex.IsMatch(package.StorageKey??"",@"\A[A-Za-z0-9_-]{1,120}/[A-Za-z0-9_-]{1,120}\z"))throw new IOException("Receita de preset inválida.");
            foreach(var file in package.Files) {
                if(file==null||!DisabledMods.AllowedPath(file.Path)||!FixedData.HashValid(file.Hash)||file.Size<0)throw new IOException("Arquivo de preset inválido.");
                GeneralInstallRoutes.Resolve(root,file.Path,false);
                if(++total>InstalledSnapshot.MaxFiles)throw new IOException("Preset excede o limite de arquivos.");
            }
        }
    }
    string Target(DisabledModFile file) {return GeneralInstallRoutes.Resolve(root,file.Path,false);}
    bool Matches(DisabledModFile file) {string path=Target(file);Safe.NoLinks(path);return File.Exists(path)&&new FileInfo(path).Length==file.Size&&Safe.Hash(path)==file.Hash;}
    static Dictionary<string,List<DisabledModFile>> Plan(IEnumerable<PresetPackage> packages) {
        return packages.ToDictionary(p=>p.StorageKey,p=>p.Files,StringComparer.Ordinal);
    }
    bool SameTarget(DisabledModFile a,DisabledModFile b) {return String.Equals(Target(a),Target(b),StringComparison.OrdinalIgnoreCase);}
    HashSet<string> SamePaths(IEnumerable<PresetPackage> keep,IEnumerable<DisabledModFile> files) {
        var known=keep.SelectMany(p=>p.Files).ToLookup(Target,StringComparer.OrdinalIgnoreCase);
        return new HashSet<string>(files.Where(f=>known[Target(f)].Any(k=>k.Hash==f.Hash&&k.Size==f.Size)).Select(Target),StringComparer.OrdinalIgnoreCase);
    }
    FileStream Lock() {
        checkClosed();Safe.NoLinks(lockFile);Directory.CreateDirectory(Path.GetDirectoryName(lockFile));
        return new FileStream(lockFile,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
    }
    internal void ReleaseProtection(string modId) {
        using(Lock()) {
            var state=Read();var packages=state.Active.Where(p=>p.ModId==modId).ToList();
            if(packages.Count==0)throw new IOException("Nenhum registro instalado disponível para retomar o gerenciamento.");
            foreach(var file in packages.SelectMany(p=>p.Files)) {
                string path=Target(file);Safe.NoLinks(path);if(!File.Exists(path))throw new IOException("Repare os arquivos ausentes antes de retomar o gerenciamento.");
                file.Size=new FileInfo(path).Length;file.Hash=Safe.Hash(path);
            }
            state.Protected.Remove(modId);Safe.WriteJson(stateFile,state);
        }
    }
    internal void RecordInstalled(PresetPackage package) {
        using(Lock()) {
            Validate(new[]{package});if(package.Files.Count==0||!package.Files.All(Matches))return;
            var state=Read();if(state.Protected.ContainsKey(package.ModId))return;
            state.Active.RemoveAll(p=>p.ModId==package.ModId);state.Active.Add(package);Safe.WriteJson(stateFile,state);
        }
    }
    internal void RecordRemoval(IEnumerable<string> keys) {
        using(Lock()) {
            var state=Read();var removed=new HashSet<string>(keys,StringComparer.Ordinal);
            if(state.Active.RemoveAll(p=>removed.Contains(p.Key)&&!state.Protected.ContainsKey(p.ModId)&&p.Files.All(f=>{
                string path=Target(f);Safe.NoLinks(path);return !File.Exists(path)&&!Directory.Exists(path);
            }))>0)Safe.WriteJson(stateFile,state);
        }
    }
    internal bool RecoveryPending {get{Safe.NoLinks(journalFile);return File.Exists(journalFile);}}
    internal void Recover() {using(Lock())RecoverCore();}
    void RecoverCore() {
        Safe.NoLinks(journalFile);if(!File.Exists(journalFile))return;
        var journal=Safe.Json.Deserialize<PresetDeploymentJournal>(GeneralProjectStorage.ReadText(journalFile,24000000));
        if(journal==null||journal.Before==null||journal.Incoming==null||journal.Outgoing==null)throw new IOException("Recuperação de preset inválida.");
        Validate(journal.Before.Active);Validate(journal.Incoming);Validate(journal.Outgoing);
        var cached=storage.Read();
        foreach(var package in journal.Incoming.AsEnumerable().Reverse()) {
            List<string> created;
            if(journal.Created==null||!journal.Created.TryGetValue(package.StorageKey,out created))continue;
            var files=package.Files.Where(f=>created.Contains(f.Path,StringComparer.OrdinalIgnoreCase)).ToArray();
            if(cached.ContainsKey(package.StorageKey)) {
                foreach(var file in files) {
                    var target=Target(file);Safe.NoLinks(target);
                    if(!File.Exists(target))continue;
                    if(!Matches(file))throw new IOException("Recuperação preservou arquivos alterados: "+package.Name);
                    File.Delete(target);
                }
                continue;
            }
            if(!package.Files.All(Matches))throw new IOException("Recuperação preservou arquivos alterados: "+package.Name);
            var retained=new HashSet<string>(package.Files.Except(files).Select(Target),StringComparer.OrdinalIgnoreCase);
            storage.Disable(Plan(new[]{package}),true,retained);
        }
        storage.Enable(journal.Outgoing.Select(p=>p.StorageKey));
        Safe.WriteJson(stateFile,journal.Before);File.Delete(journalFile);
    }
    internal PresetDeploymentState Apply(string preset,string fingerprint,IEnumerable<PresetPackage> wanted,IEnumerable<PresetPackage> discovered,Action<string,int,int> progress=null,ISet<string> sharedPaths=null) {
        using(Lock()) {
            RecoverCore();var state=Read();var desired=wanted.ToList();Validate(desired);
            var inventory=discovered.ToList();Validate(inventory);
            var cached=storage.Read();
            state.Active.RemoveAll(p=>cached.ContainsKey(p.StorageKey));
            foreach(var p in desired.Where(p=>p.Files.Count==0)) {
                var proven=state.Active.FirstOrDefault(a=>a.Key==p.Key);
                if(proven!=null)p.Files=GeneralPresetProfiles.Copy(proven.Files);
            }
            // Never replace an existing baseline with current bytes: that would hide external changes.
            foreach(var package in inventory.Where(p=>p.Files.Count>0&&!cached.ContainsKey(p.StorageKey))) {
                var previous=state.Active.FirstOrDefault(p=>p.Key==package.Key);
                if(previous!=null){previous.Group=package.Group;previous.Name=package.Name;previous.RequiredMods=package.RequiredMods;continue;}
                if(package.Files.All(Matches)&&!state.Active.Any(p=>p.Files.Any(f=>package.Files.Any(n=>SameTarget(n,f)&&n.Hash!=f.Hash))))state.Active.Add(package);
            }
            state.Issues.Clear();
            foreach(var package in state.Active) {
                if(package.Files.Any(f=>!Matches(f)))state.Protected[package.ModId]=GeneralStyle.T("alterado fora do Mod Loader; mantido sem alterações.","changed outside Mod Loader; left unchanged.");
            }
            foreach(var package in inventory.Where(p=>p.Files.Count>0&&!state.Active.Any(a=>a.Key==p.Key)&&!cached.ContainsKey(p.StorageKey))) {
                if(package.Files.Any(f=>File.Exists(Target(f)))&&package.Files.Any(f=>!Matches(f))&&
                    !state.Active.Any(a=>a.ModId==package.ModId))state.Protected[package.ModId]=GeneralStyle.T("arquivos existentes não correspondem ao registro; preservados.","existing files differ from the record; preserved.");
            }
            var held=new HashSet<string>(state.Protected.Keys,StringComparer.Ordinal);
            foreach(var package in state.Active.Where(p=>p.Immutable||sharedPaths!=null&&p.Files.Any(f=>sharedPaths.Contains(Target(f))))) {
                held.Add(package.ModId);
                if(!desired.Any(p=>p.Key==package.Key))state.Issues.Add(package.Name+GeneralStyle.T(": arquivo compartilhado ou protegido; mantido.",": shared or protected files; retained."));
            }
            bool changed;
            do {changed=false;foreach(var p in state.Active.Where(p=>held.Contains(p.ModId)))foreach(var dep in p.RequiredMods)changed|=held.Add(dep);}while(changed);
            var eligible=desired.Where(p=>!held.Contains(p.ModId)||state.Active.Any(a=>a.Key==p.Key)).ToList();
            var unavailable=new HashSet<string>();
            foreach(var p in eligible)if(p.Files.Count==0||(!cached.ContainsKey(p.StorageKey)&&!state.Active.Any(a=>a.Key==p.Key)))unavailable.Add(p.ModId);
            do {changed=false;foreach(var p in eligible)if(p.RequiredMods.Any(dep=>unavailable.Contains(dep)||!eligible.Any(x=>x.ModId==dep)&&!state.Active.Any(x=>x.ModId==dep&&held.Contains(dep))))changed|=unavailable.Add(p.ModId);}while(changed);
            foreach(var p in desired.Where(p=>held.Contains(p.ModId)&&!state.Active.Any(a=>a.Key==p.Key)))state.Issues.Add(p.Name+GeneralStyle.T(": troca bloqueada por proteção.",": switch blocked by protection."));
            foreach(var p in eligible.Where(p=>unavailable.Contains(p.ModId)))state.Issues.Add(p.Name+GeneralStyle.T(": pendente ou dependência ausente.",": pending or missing dependency."));
            eligible.RemoveAll(p=>unavailable.Contains(p.ModId));
            foreach(var pair in state.Protected)state.Issues.Add((inventory.Concat(state.Active).FirstOrDefault(p=>p.ModId==pair.Key)??new PresetPackage{Name=pair.Key}).Name+": "+pair.Value);
            var keep=state.Active.Where(p=>held.Contains(p.ModId)||eligible.Any(x=>x.Key==p.Key)).ToList();
            var outgoing=state.Active.Where(p=>!keep.Any(k=>k.Key==p.Key)).ToList();
            var incoming=eligible.Where(p=>!state.Active.Any(a=>a.Key==p.Key)).ToList();
            // A protected or unknown destination blocks the affected incoming mod, not the whole preset.
            foreach(var p in incoming.ToArray()) {
                bool validDestination=true;
                try{foreach(var f in p.Files)GeneralInstallRoutes.Resolve(root,f.Path,true);}catch(DirectoryNotFoundException){validDestination=false;}
                if(!validDestination){incoming.Remove(p);state.Issues.Add(p.Name+GeneralStyle.T(": destino indisponível.",": destination unavailable."));continue;}
                bool conflict=p.Group!=null&&keep.Any(k=>k.Group==p.Group&&k.Key!=p.Key)||p.Files.Any(f=>Directory.Exists(Target(f))||keep.SelectMany(k=>k.Files).Any(k=>SameTarget(k,f)&&k.Hash!=f.Hash)||
                    File.Exists(Target(f))&&!keep.Concat(outgoing).Any(o=>o.Files.Any(k=>SameTarget(k,f)&&Matches(k))))||
                    incoming.Where(o=>o!=p).Any(o=>o.Files.Any(k=>p.Files.Any(f=>SameTarget(f,k)&&f.Hash!=k.Hash)));
                if(conflict){incoming.Remove(p);state.Issues.Add(p.Name+GeneralStyle.T(": destino em conflito; preservado.",": conflicting destination; preserved."));}
            }
            do {changed=false;foreach(var p in incoming.ToArray())if(p.RequiredMods.Any(dep=>!keep.Concat(incoming).Any(d=>d.ModId==dep))){incoming.Remove(p);changed=true;state.Issues.Add(p.Name+GeneralStyle.T(": dependência indisponível.",": unavailable dependency."));}}while(changed);

            // A failed replacement must not remove an active dependency that a retained mod still needs.
            do {
                changed=false;
                foreach(var dependent in keep.ToArray())foreach(var dependency in dependent.RequiredMods) {
                    if(keep.Concat(incoming).Any(p=>p.ModId==dependency))continue;
                    var old=outgoing.FirstOrDefault(p=>p.ModId==dependency);
                    if(old==null)continue;
                    outgoing.Remove(old);keep.Add(old);changed=true;
                    state.Issues.Add(old.Name+GeneralStyle.T(": mantido por dependência ativa.",": retained for an active dependency."));
                }
                foreach(var p in incoming.ToArray())if(p.RequiredMods.Any(dep=>!keep.Concat(incoming).Any(d=>d.ModId==dep))||
                    keep.Any(k=>k.Group!=null&&k.Group==p.Group&&k.Key!=p.Key||k.Files.Any(f=>p.Files.Any(n=>SameTarget(n,f)&&n.Hash!=f.Hash)))) {
                    incoming.Remove(p);changed=true;
                    state.Issues.Add(p.Name+GeneralStyle.T(": dependência ou destino em conflito.",": conflicting dependency or destination."));
                }
            }while(changed);
            var space=new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase);
            Action<string,long> reserve=(path,bytes)=>{string drive=Path.GetPathRoot(path);long current;space.TryGetValue(drive,out current);space[drive]=checked(current+bytes);};
            foreach(var f in outgoing.SelectMany(p=>p.Files).GroupBy(f=>f.Hash).Select(g=>g.First()))reserve(stateFile,checked(f.Size+65536));
            foreach(var f in incoming.SelectMany(p=>p.Files).GroupBy(f=>f.Path,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()))reserve(Target(f),checked(f.Size+65536));
            foreach(var needed in space)if(FreeBytes(needed.Key)<needed.Value)throw new IOException(GeneralStyle.T("Espaço insuficiente para carregar o preset: ","Insufficient space to load preset: ")+needed.Key);
            // Persist discoveries and protections before the first mutation, then journal the whole transition.
            Safe.WriteJson(stateFile,state);
            var journal=new PresetDeploymentJournal{Before=GeneralPresetProfiles.Copy(state),Outgoing=outgoing,Incoming=incoming};
            Safe.WriteJson(journalFile,journal);
            try {
                var desiredFiles=keep.Concat(incoming).SelectMany(p=>p.Files).ToList();
                if(Checkpoint!=null)Checkpoint("prepared");
                if(outgoing.Count>0)storage.Disable(Plan(outgoing),true,SamePaths(keep,desiredFiles));
                if(Checkpoint!=null)Checkpoint("disabled");
                int done=0;foreach(var package in incoming) {
                    checkClosed();if(progress!=null)progress(package.Name,done,incoming.Count);
                    journal.Created[package.StorageKey]=package.Files.Where(f=>!File.Exists(Target(f))).Select(f=>f.Path).ToList();
                    Safe.WriteJson(journalFile,journal);
                    storage.Enable(new[]{package.StorageKey},()=>{if(Checkpoint!=null)Checkpoint("restoring");});done++;
                    if(Checkpoint!=null)Checkpoint("enabled");
                    if(progress!=null)progress(package.Name,done,incoming.Count);
                }
                state.Active=keep.Concat(incoming).ToList();state.Preset=preset;state.Fingerprint=fingerprint;
                Safe.WriteJson(stateFile,state);File.Delete(journalFile);return state;
            }catch {
                RecoverCore();throw;
            }
        }
    }
}
