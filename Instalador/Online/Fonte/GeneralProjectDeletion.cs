using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

internal sealed class ProjectDeleteFile {
    internal string Path,Hash,Reason;
    internal long Size;
    internal bool Owned, ReplacesPrevious;
    internal List<string> OwnerKeys;
    internal List<GeneralInstallRoute> InstallRoutes;
    internal Dictionary<string,GeneralModVariant> OwnerRecipes;
}
internal sealed class ProjectDeletePlan {
    internal GeneralProject Project;
    internal string Root,Data,Proof;
    internal readonly List<ProjectDeleteFile> Remove=new List<ProjectDeleteFile>(),Preserve=new List<ProjectDeleteFile>();
    internal readonly List<string> Peers=new List<string>();
    internal int Missing;
}
internal sealed class GeneralProjectDeletion {
    readonly GeneralProjectStore store;
    readonly FixedCatalog catalog;
    readonly bool fixture;
    ProjectDeletePlan approved;
    internal GeneralProjectDeletion(GeneralProjectStore store,FixedCatalog catalog,bool fixture=false){this.store=store;this.catalog=catalog;this.fixture=fixture;}
    internal ProjectDeletePlan Plan(GeneralProject project){approved=Inspect(project);return approved;}
    ProjectDeletePlan Inspect(GeneralProject project){
        if(project==null||project.TemplateId!=null)throw new IOException("O projeto padrão não pode ser excluído.");
        var index=store.Read();
        var proof=new List<string>{store.IndexFingerprint()};
        var result=new ProjectDeletePlan{Project=project,Data=store.DataPath(project)};
        var state=LoadForProof(project,result.Data,proof);
        if(String.IsNullOrWhiteSpace(state.Destination)){
            if(state.List.Mods.Count>0)throw new IOException("O projeto não tem destino de instalação. Você ainda pode escolher excluir somente do app.");
            result.Proof=String.Join("/",proof);return result;
        }
        result.Root=Path.GetFullPath(state.Destination).TrimEnd('\\');Safe.NoLinks(result.Root);
        if(result.Root.Length<=3||result.Root.StartsWith(@"\\"))throw new IOException("Escolha um destino local específico para remover mods.");
        if(!Directory.Exists(result.Root))throw new IOException("O destino de instalação está ausente. Use excluir somente do app se quiser manter os arquivos.");
        Closed(result.Root);

        var candidates=Files(state,result.Data,result.Root).Select(file=>Normalize(result.Root,file)).GroupBy(file=>file.Path,StringComparer.OrdinalIgnoreCase).ToList();
        bool external=candidates.Any(group=>GeneralInstallRoutes.External(group.Key));
        var shared=new HashSet<string>(StringComparer.OrdinalIgnoreCase);result.Peers.Add(result.Data);
        foreach(var peer in index.Projects.Where(item=>item.Id!=project.Id)){
            var other=LoadForProof(peer,store.DataPath(peer),proof);
            if(String.IsNullOrWhiteSpace(other.Destination))continue;
            string peerRoot=Path.GetFullPath(other.Destination).TrimEnd('\\');
            bool same=String.Equals(peerRoot,result.Root,StringComparison.OrdinalIgnoreCase);
            if(!same&&(!external||!Directory.Exists(peerRoot)))continue;
            string data=store.DataPath(peer);if(same)result.Peers.Add(data);
            foreach(var file in Files(other,data,peerRoot).Where(file=>same||GeneralInstallRoutes.External(file.Path)))
                shared.Add(GeneralInstallRoutes.External(file.Path)?GeneralInstallRoutes.Resolve(peerRoot,file.Path):Normalize(peerRoot,file).Path);
        }
        var targets=new HashSet<string>(candidates.Select(group=>group.Key),StringComparer.OrdinalIgnoreCase);
        if(File.Exists(Safe.In(result.Root,"bin/x64/Cyberpunk2077.exe")))foreach(var outside in catalog.Packages.Where(mod=>mod.Files.Any(file=>!file.UserData&&!targets.Contains(file.Path)&&File.Exists(GeneralInstallRoutes.Resolve(result.Root,file.Path)))))
            foreach(var dependency in FixedData.Expand(catalog,new[]{outside.Id}).Where(mod=>mod.Id!=outside.Id))foreach(var file in dependency.Files)shared.Add(file.Path);
        var immutable=new HashSet<string>(catalog.Packages.Where(mod=>mod.Immutable).SelectMany(mod=>mod.Files).Select(file=>file.Path),StringComparer.OrdinalIgnoreCase);
        foreach(var group in candidates){
            var file=group.First();string target=GeneralInstallRoutes.Resolve(result.Root,file.Path);Safe.NoLinks(target);
            if(Directory.Exists(target))throw new IOException("Uma pasta ocupa um arquivo de mod: "+file.Path);
            if(!File.Exists(target)){result.Missing++;continue;}
            file.Reason=shared.Contains(GeneralInstallRoutes.External(file.Path)?target:file.Path)?"Usado por outro projeto":immutable.Contains(file.Path)||!InstalledSnapshot.SafeGenericPath(file.Path)||file.Path.Equals("bin/x64/Cyberpunk2077.exe",StringComparison.OrdinalIgnoreCase)?"Base ou arquivo protegido":
                group.Any(item=>!item.Owned)?"Arquivo preexistente ou configuração preservada":group.Any(item=>item.Hash!=file.Hash||item.Size!=file.Size)?"Registros divergentes":
                !Matches(target,file)?"Arquivo alterado desde a instalação":null;
            if(file.Reason==null)result.Remove.Add(file);else result.Preserve.Add(file);
        }
        result.Remove.Sort((a,b)=>StringComparer.OrdinalIgnoreCase.Compare(a.Path,b.Path));result.Preserve.Sort((a,b)=>StringComparer.OrdinalIgnoreCase.Compare(a.Path,b.Path));
        if(proof[0]!=store.IndexFingerprint())throw new IOException("O índice de projetos mudou durante a conferência.");
        result.Proof=String.Join("/",proof);return result;
    }
    GeneralModState LoadForProof(GeneralProject project,string data,List<string> proof){
        string path=Safe.In(data,"lista.json");
        Safe.NoLinks(path);
        string before=Safe.Hash(path);
        var state=store.Load(project);
        if(before!=Safe.Hash(path))throw new IOException("O projeto mudou durante a conferência: "+project.Name);
        proof.Add(before);
        return state;
    }

    internal IEnumerable<ProjectDeleteFile> Files(GeneralModState state,string data,string root){
        foreach(var file in new GeneralModInstaller(root,Path.Combine(data,"Instalacoes")).ProjectDeletionFiles())yield return file;
        foreach(var file in new InstalledSnapshotInstaller(root,data,catalog).ProjectDeletionFiles())yield return file;
        bool cyberpunk=File.Exists(Safe.In(root,"bin/x64/Cyberpunk2077.exe"));
        var seed=GeneralModLibrary.Seed(catalog,new ModImportItem[0]);GeneralModLibrary.RestoreKnownMappings(state.List,seed);
        var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(var mod in state.List.Mods)foreach(var variant in mod.Variants){
            var trusted=seed.Mods.FirstOrDefault(item=>item.Id==mod.Id);
            var original=trusted==null?null:trusted.Variants.FirstOrDefault(item=>item.Id==variant.Id&&item.Sources.SequenceEqual(variant.Sources));
            if(original!=null&&original.CyberpunkId!=null){if(!cyberpunk)throw new IOException("Um item Cyberpunk aponta para um destino de outro jogo.");ids.Add(original.CyberpunkId);}
        }
        if(cyberpunk){
            foreach(var mod in FixedData.Expand(catalog,ids)){
                var recipe=mod.Id==NightCityDensity.ModId?NightCityDensity.Installed(mod,root):mod;
                foreach(var file in recipe.Files)yield return new ProjectDeleteFile{Path=file.Path,Hash=file.Sha256,Size=file.Size,Owned=!file.UserData&&!recipe.Immutable};
            }
            var imports=new HashSet<string>(state.CyberpunkImports.Values,StringComparer.Ordinal);
            foreach(var mod in state.List.Mods.Where(item=>item.Id.StartsWith("file-",StringComparison.Ordinal)&&item.Id.Length==69))imports.Add(mod.Id);
            foreach(var file in new FixedInstaller(catalog,root).ProjectDeletionFiles(imports))yield return file;
        }
    }
    internal void Apply(ProjectDeletePlan plan,Action<string> report=null,int failAfter=-1){
        if(!Object.ReferenceEquals(plan,approved))throw new IOException("Refaça a conferência antes de excluir.");
        var leases=new List<IDisposable>();
        try{
            if(plan.Root!=null){
                Closed(plan.Root);
                if(File.Exists(Safe.In(plan.Root,"bin/x64/Cyberpunk2077.exe"))){string folder=Safe.In(plan.Root,".CoopModsOnline");Safe.NoLinks(folder);Directory.CreateDirectory(folder);leases.Add(new FileStream(Safe.In(folder,"operacao.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None));}
                foreach(string data in plan.Peers.OrderBy(value=>value,StringComparer.OrdinalIgnoreCase))leases.Add(new GeneralModInstaller(plan.Root,Path.Combine(data,"Instalacoes")).ProjectDeletionLock());
            }
            var fresh=Inspect(plan.Project);
            if(fresh.Proof!=plan.Proof||!SameFiles(fresh,plan))throw new IOException("O projeto ou os arquivos mudaram após a conferência. Revise novamente; nada foi apagado nesta tentativa.");
            foreach(var file in plan.Remove){string path=GeneralInstallRoutes.Resolve(plan.Root,file.Path);if(!Matches(path,file))throw new IOException("Arquivo mudou antes da exclusão: "+file.Path);}
            string journal=Safe.In(plan.Data,"exclusao-permanente-"+Guid.NewGuid().ToString("N")+".json");
            var removed=new List<string>();
            Safe.WriteJson(journal,new{ProjectId=plan.Project.Id,Root=plan.Root,Status="removendo",Planned=plan.Remove.Select(file=>file.Path).ToArray(),Removed=removed});
            try{
                foreach(var file in plan.Remove){
                    Closed(plan.Root);string path=GeneralInstallRoutes.Resolve(plan.Root,file.Path);Safe.NoLinks(path);
                    if(!Matches(path,file))throw new IOException("Arquivo mudou durante a exclusão: "+file.Path);
                    File.Delete(path);removed.Add(file.Path);
                    if(report!=null)report(file.Path);
                    if(failAfter>=0&&removed.Count>=failAfter)throw new IOException("Interrupção de teste após exclusão.");
                }
                if(Inspect(plan.Project).Proof!=plan.Proof)throw new IOException("O projeto mudou durante a exclusão. Confira os arquivos restantes antes de tentar novamente.");
                Safe.WriteJson(journal,new{ProjectId=plan.Project.Id,Root=plan.Root,Status="arquivos_apagados",Removed=removed});
                store.Delete(plan.Project);approved=null;
            }catch{
                try{Safe.WriteJson(journal,new{ProjectId=plan.Project.Id,Root=plan.Root,Status="interrompido",Removed=removed});}catch(IOException){}catch(UnauthorizedAccessException){}
                throw;
            }
        }finally{foreach(var lease in leases.AsEnumerable().Reverse())lease.Dispose();}
    }
    static bool SameFiles(ProjectDeletePlan left,ProjectDeletePlan right){
        return left.Missing==right.Missing&&
            left.Remove.Select(file=>Tuple.Create(file.Path,file.Hash,file.Size)).SequenceEqual(right.Remove.Select(file=>Tuple.Create(file.Path,file.Hash,file.Size)))&&
            left.Preserve.Select(file=>Tuple.Create(file.Path,file.Reason)).SequenceEqual(right.Preserve.Select(file=>Tuple.Create(file.Path,file.Reason)));
    }
    static ProjectDeleteFile Normalize(string root,ProjectDeleteFile file){string full=GeneralInstallRoutes.Resolve(root,file.Path);if(!GeneralInstallRoutes.External(file.Path))file.Path=full.Substring(root.Length+1).Replace('\\','/');return file;}
    static bool Matches(string path,ProjectDeleteFile file){Safe.NoLinks(path);return File.Exists(path)&&new FileInfo(path).Length==file.Size&&Safe.Hash(path)==file.Hash;}
    void Closed(string root){
        if(root==null)return;
        if(fixture){if(!File.Exists(Safe.In(root,".fixture-only"))||!Regex.IsMatch(root,@"(?i)[\\/]online-testes-[a-f0-9]{32}([\\/]|$)"))throw new IOException("Exceção de teste exige uma fixture isolada.");return;}
        foreach(var process in Process.GetProcesses())using(process){
            try{string image=process.MainModule.FileName;if(image.StartsWith(root+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("Feche o jogo e os programas desta pasta antes de apagar mods.");}
            catch(Win32Exception){}catch(InvalidOperationException){}catch(NotSupportedException){}
        }
    }
}

public sealed partial class GeneralModInstaller {
    internal IDisposable ProjectDeletionLock(){return AcquireLock();}
    internal IEnumerable<ProjectDeleteFile> ProjectDeletionFiles(bool inspection=false){
        Safe.NoLinks(records);if(!Directory.Exists(records))yield break;
        foreach(string path in Directory.GetFiles(records,"*.json")){
            Safe.NoLinks(path);var record=ReadRecord(path,inspection);if(record==null)continue;
            foreach(var file in record.Files)yield return new ProjectDeleteFile{Path=file.Path,Hash=file.Sha256,Size=file.Size,Owned=file.Owned,ReplacesPrevious=file.Before!=null,OwnerKeys=new List<string>{record.Key},InstallRoutes=record.InstallRoutes};
        }
    }
}
public sealed partial class FixedInstaller {
    internal IEnumerable<ProjectDeleteFile> ProjectDeletionFiles(HashSet<string> ids,bool inspection=false){
        string folder=Safe.In(Data,"importados");Safe.NoLinks(folder);if(!Directory.Exists(folder)||ids!=null&&ids.Count==0)yield break;
        foreach(var receipt in Directory.GetFiles(folder,"*.json").Select(path=>ReadReceiptCore(path,inspection)).Where(item=>item.Operation==LocalModPackage.ReceiptOperation&&item.Status=="instalado"&&item.Mods.Count==1&&(ids==null||ids.Contains(item.Mods[0]))).GroupBy(item=>item.Mods[0]).Select(group=>group.OrderByDescending(item=>item.CreatedUtc).First()))
            foreach(var file in receipt.Files.Where(file=>file.Applied&&!file.Reverted&&!file.UndoStarted))yield return new ProjectDeleteFile{Path=file.Path,Hash=file.After,Size=File.Exists(Safe.In(game,file.Path))?new FileInfo(Safe.In(game,file.Path)).Length:0,Owned=file.Before==null,OwnerKeys=receipt.Mods.ToList()};
    }
}
public sealed partial class InstalledSnapshotInstaller {
    internal IEnumerable<ProjectDeleteFile> ProjectDeletionFiles(bool inspection=false){
        foreach(var receipt in AllReceipts(inspection)){
            if(receipt.Status!="installed")continue;
            var owners=receipt.Manifest.Files.GroupBy(payload=>payload.Path,StringComparer.OrdinalIgnoreCase).ToDictionary(group=>group.Key,group=>group.SelectMany(payload=>payload.Owners).Where(receipt.ActiveOwners.Contains).Distinct().ToList(),StringComparer.OrdinalIgnoreCase);
            var recipes=receipt.Manifest.List.Mods.ToDictionary(m=>InstalledSnapshot.Owner(m,m.Variants.Single()),m=>m.Variants.Single());
            foreach(var file in receipt.Files.Where(file=>file.Applied&&!file.Reverted)){
                List<string> keys;if(!owners.TryGetValue(file.Path,out keys)||keys.Count==0)continue;
                yield return new ProjectDeleteFile{Path=file.Path,Hash=file.After,Size=file.Size,Owned=file.Owned&&file.Before==null,ReplacesPrevious=file.Before!=null,OwnerKeys=keys,OwnerRecipes=recipes};
            }
        }
    }
}
