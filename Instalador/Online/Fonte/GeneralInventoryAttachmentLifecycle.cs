using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal sealed partial class GeneralModWindow {
    static bool MatchesInventoryAttachment(string root,InventoryAttachment attachment,Func<string,string> readHash){
        if(attachment.Hash==null)return true;
        try{
            string path=Safe.In(root,attachment.Path);
            Safe.NoLinks(path);
            return File.Exists(path)&&readHash(path)==attachment.Hash;
        }catch(IOException){return false;}
         catch(UnauthorizedAccessException){return false;}
    }

    HashSet<string> ProtectAttachedFiles(string root,IEnumerable<string> candidates,HashSet<string> excludingOwners,
        List<GeneralUpdateOwnershipContext> contexts) {
        var paths=new HashSet<string>(candidates,InventoryPaths);
        var protectedPaths=new HashSet<string>(InventoryPaths);
        if(paths.Count==0)return protectedPaths;
        if(contexts.Any(context=>context.State==null))return paths;
        bool cyberpunk=IsCyberpunk(root);
        if(cyberpunk){
            var engine=Cyberpunk(root);
            protectedPaths.UnionWith(catalog.Packages.SelectMany(NightCityDensity.KnownFiles)
                .Select(file=>file.Path).Where(paths.Contains));
            protectedPaths.UnionWith(engine.ProjectDeletionFiles(null,true).Select(file=>file.Path).Where(paths.Contains));
        }
        foreach(var context in contexts){
            bool current=InventoryPaths.Equals(context.Data,data);
            protectedPaths.UnionWith(context.State.List.Mods.Where(mod=>!current||!excludingOwners.Contains(mod.Id))
                .SelectMany(mod=>mod.DetectedPaths).Where(paths.Contains));
            if(!current)
                protectedPaths.UnionWith(new InventoryLinkStore(context.Data,root).Load().Attachments.Select(file=>file.Path).Where(paths.Contains));
            var recorded=new GeneralModInstaller(root,Path.Combine(context.Data,"Instalacoes")).ProjectDeletionFiles(true)
                .Concat(new InstalledSnapshotInstaller(root,context.Data,catalog,testMode).ProjectDeletionFiles(true));
            protectedPaths.UnionWith(recorded.Select(file=>file.Path).Where(paths.Contains));
        }
        return protectedPaths;
    }

    string RemoveInventoryAttachments(string root,HashSet<string> owners){
        var store=new InventoryLinkStore(data,root);
        if(!store.Load().Attachments.Any(file=>file.Owners.Any(owners.Contains)))return null;
        using(IsCyberpunk(root)?Cyberpunk(root).LockForRouteChange():null)
        using(Generic(root).LockUpgradeReceiptMetadata()){
            var links=store.Load();
            var selected=links.Attachments.Where(file=>file.Owners.Any(owners.Contains)).ToArray();
            if(selected.Length==0)return null;
            var removable=selected.Where(file=>file.Hash!=null&&file.Owners.All(owners.Contains)).ToArray();
            var protectedPaths=removable.Length==0?new HashSet<string>(InventoryPaths):
                ProtectAttachedFiles(root,removable.Select(file=>file.Path),owners,ReadUpdateContexts(root));
            int removed=0,preserved=0;
            foreach(var file in selected){
                string path=Safe.In(root,file.Path);
                Safe.NoLinks(path);
                if(!File.Exists(path))continue;
                if(file.Hash==null||file.Owners.Any(owner=>!owners.Contains(owner))||protectedPaths.Contains(file.Path)||
                    !InventoryLinkStore.CanManageAttachment(file.Path)||Safe.Hash(path)!=file.Hash){
                    preserved++;
                    continue;
                }
                EnsureGameClosed(root);
                File.Delete(path);
                removed++;
            }
            foreach(var file in selected)file.Owners=file.Owners.Where(owner=>!owners.Contains(owner)).ToArray();
            links.Attachments.RemoveAll(file=>file.Owners.Length==0);
            store.Save(links);
            return String.Format(T("Arquivos vinculados: {0} removidos, {1} preservados.",
                "Linked files: {0} removed, {1} preserved."),removed,preserved);
        }
    }

    List<GeneralModInstaller.RecordFile> InventoryUpdateFiles(Work work,string root,List<GeneralUpdateOwnershipContext> contexts,
        HashSet<string> shared){
        var files=new InventoryLinkStore(data,root).Load().Attachments;
        // Attachments never replace an installer receipt or another mod's file.
        var owned=files.Where(file=>file.Owners.Length==1&&file.Owners[0]==work.Entry.Id&&file.Hash!=null).ToArray();
        var protectedPaths=ProtectAttachedFiles(root,owned.Select(file=>file.Path),
            new HashSet<string>(StringComparer.Ordinal){work.Entry.Id},contexts);
        shared.UnionWith(files.Where(file=>file.Owners.Any(owner=>owner!=work.Entry.Id)).Select(file=>file.Path));
        var result=new List<GeneralModInstaller.RecordFile>();
        foreach(var file in owned.Where(file=>!protectedPaths.Contains(file.Path)&&!shared.Contains(file.Path))){
            string target=Safe.In(root,file.Path);
            Safe.NoLinks(target);
            if(!File.Exists(target))continue;
            if(!MatchesInventoryAttachment(root,file,Safe.Hash)){
                shared.Add(file.Path);
                continue;
            }
            result.Add(new GeneralModInstaller.RecordFile{Path=file.Path,ArchivePath=file.Path,
                Sha256=file.Hash,Size=new FileInfo(target).Length,Owned=true});
        }
        return result;
    }
}
