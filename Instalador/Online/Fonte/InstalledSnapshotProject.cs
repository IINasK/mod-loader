using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public static partial class InstalledSnapshot {
    static bool HasInstallRoutes(InstalledSnapshotManifest manifest) {
        return manifest.List.Mods.Concat(manifest.Project==null?Enumerable.Empty<GeneralModEntry>():manifest.Project.Mods)
            .SelectMany(mod=>mod.Variants).Any(variant=>variant.InstallRoutes.Count>0);
    }

    internal static List<string> SkipMissingDestinations(InstalledSnapshotManifest manifest,string game,Func<string[],bool> approveUserFolders) {
        if(manifest.Schema!=2)return new List<string>();
        var unavailable=new HashSet<string>(StringComparer.Ordinal);
        var personal=new Dictionary<string,string[]>(StringComparer.Ordinal);
        foreach(var mod in manifest.List.Mods){
            var variant=mod.Variants.Single();
            try{
                GeneralInstallRoutes.CheckFolders(game,variant.InstallRoutes);
                string[] folders=GeneralInstallRoutes.ExternalFolders(game,variant.InstallRoutes);
                if(folders.Length>0)personal.Add(mod.Id,folders);
            }catch(DirectoryNotFoundException){unavailable.Add(mod.Id);}
        }
        if(personal.Count>0&&(approveUserFolders==null||!approveUserFolders(personal.Values.SelectMany(paths=>paths)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path=>path,StringComparer.OrdinalIgnoreCase).ToArray())))
            unavailable.UnionWith(personal.Keys);
        if(unavailable.Count==0)return new List<string>();
        bool changed;
        do{
            changed=false;
            foreach(var mod in manifest.List.Mods.Where(mod=>mod.Variants.Single().RequiredMods.Any(unavailable.Contains)))
                changed|=unavailable.Add(mod.Id);
        }while(changed);
        var skipped=manifest.List.Mods.Where(mod=>unavailable.Contains(mod.Id)).Select(mod=>mod.Id).ToList();
        var remaining=new HashSet<string>(manifest.List.Mods.Where(mod=>!unavailable.Contains(mod.Id))
            .Select(mod=>Owner(mod,mod.Variants.Single())),StringComparer.Ordinal);
        manifest.List.Mods.RemoveAll(mod=>unavailable.Contains(mod.Id));
        foreach(var preset in manifest.List.Presets)preset.ModIds.RemoveAll(unavailable.Contains);
        foreach(var file in manifest.Files)file.Owners.RemoveAll(owner=>!remaining.Contains(owner));
        manifest.Files.RemoveAll(file=>file.Owners.Count==0);
        return skipped;
    }

    internal static int CountOutsidePack(InstalledSnapshotManifest manifest){
        if(manifest.Project==null)return 0;
        var included=new HashSet<string>(manifest.List.Mods.Select(GeneralModLibrary.ChoiceKey),StringComparer.Ordinal);
        return manifest.Project.Mods.Select(GeneralModLibrary.ChoiceKey).Distinct(StringComparer.Ordinal).Count(key=>!included.Contains(key));
    }
    internal static bool Uninstalled(GeneralModState state,GeneralModEntry mod,GeneralModVariant variant,FixedCatalog catalog,string root,string appData){
        string work=Owner(mod,variant);
        if(state.SnapshotReceipts.ContainsKey(work))return false;
        if(variant.CyberpunkId!=null){
            var recipe=FixedRecipe(catalog,variant);
            var paths=recipe.Id==NightCityDensity.ModId?NightCityDensity.KnownFiles(recipe):recipe.Files;
            return paths.Where(f=>!f.UserData).All(f=>!File.Exists(Safe.In(root,f.Path))&&!Directory.Exists(Safe.In(root,f.Path)));
        }
        if(IsCyberpunk(root)&&HasManualHystFiles(mod.Id,root))return false;
        for(int i=0;i<variant.Sources.Count;i++){
            string key=work+"/"+i+"/"+variant.Sources[i];
            if(IsCyberpunk(root)){
                string id;if(state.CyberpunkImports.TryGetValue(key,out id))return false;
                if(new FixedInstaller(catalog,root).HasSnapshotImportedRecord(mod.Id))return false;
            }else if(new GeneralModInstaller(root,Path.Combine(appData,"Instalacoes")).HasSnapshotRecord(key))return false;
        }
        return true;
    }
    static GeneralModList ProjectDefinition(GeneralModState state,GeneralModList installed){
        var project=Clone(state.List);
        foreach(var mod in project.Mods){
            mod.DetectedPaths.Clear();
            mod.DefaultVariant=Selected(state,mod);
            foreach(var variant in mod.Variants){
                variant.Sources=variant.Sources.Where(GeneralModDownload.IsWebSource).ToList();
                if(variant.Sources.Count==0)variant.Sources.Add("installed://"+mod.Id+"/"+variant.Id);
                variant.SourceInfo=variant.SourceInfo.Where(pair=>variant.Sources.Contains(pair.Key)).ToDictionary(pair=>pair.Key,pair=>pair.Value);
            }
        }
        foreach(var item in installed.Mods){var mod=project.Mods.SingleOrDefault(m=>m.Id==item.Id);if(mod==null)project.Mods.Add(Clone(item));else {mod.DefaultVariant=item.DefaultVariant;foreach(var variant in item.Variants)if(!mod.Variants.Any(v=>v.Id==variant.Id))mod.Variants.Add(Clone(variant));}}
        project.Categories=project.Categories.Concat(installed.Categories).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        project.Presets=project.Presets.OrderByDescending(p=>p.Id==state.SelectedPreset).ToList();
        return project;
    }
    static void ValidateProject(InstalledSnapshotManifest manifest,FixedCatalog catalog){
        var project=manifest.Project;GeneralModLibrary.ValidateList(project,false);
        if(manifest.TemplateId!=null&&(manifest.TemplateId!=GeneralProjectStore.CyberpunkTemplate||manifest.Game!="cyberpunk2077"))
            throw new IOException("Identidade do projeto padrão inválida.");
        foreach(var mod in project.Mods)foreach(var variant in mod.Variants)
            if(!variant.Sources.All(s=>GeneralModDownload.IsWebSource(s)||s=="installed://"+mod.Id+"/"+variant.Id))throw new IOException("Projeto contém caminhos privados de origem.");
        GeneralModLibrary.RestoreKnownMappings(project,GeneralModLibrary.Seed(catalog,new ModImportItem[0]));
        foreach(var installed in manifest.List.Mods){
            var mod=project.Mods.SingleOrDefault(m=>m.Id==installed.Id);
            if(mod==null||mod.DefaultVariant!=installed.DefaultVariant)throw new IOException("Projeto não corresponde aos mods transferidos.");
            var expected=installed.Variants.Single();var variant=mod.Variants.Single(v=>v.Id==mod.DefaultVariant);
            if(expected.CyberpunkId==NightCityDensity.ModId&&expected.CyberpunkVariant=="legacy-low"&&variant.Id=="legacy-low"&&variant.Sources.SequenceEqual(LegacyDensityVariant(catalog).Sources)){variant.CyberpunkId=expected.CyberpunkId;variant.CyberpunkVariant=expected.CyberpunkVariant;}
            if(!variant.Sources.SequenceEqual(expected.Sources)||!GeneralInstallRoutes.Same(variant.InstallRoutes,expected.InstallRoutes)||!variant.RequiredMods.SequenceEqual(expected.RequiredMods)||variant.CyberpunkId!=expected.CyberpunkId||variant.CyberpunkVariant!=expected.CyberpunkVariant)throw new IOException("Fontes do projeto divergem do pacote instalado.");
        }
    }
}
