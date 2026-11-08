using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public static partial class InstalledSnapshot {
    internal static int CountOutsidePack(InstalledSnapshotManifest manifest){
        if(manifest.Project==null)return 0;
        var included=new HashSet<string>(manifest.List.Mods.Select(ChoiceKey),StringComparer.Ordinal);
        return manifest.Project.Mods.Select(ChoiceKey).Distinct(StringComparer.Ordinal).Count(key=>!included.Contains(key));
    }
    static string ChoiceKey(GeneralModEntry mod){
        return mod.AlternativeGroup==null?"mod/"+mod.Id:"choice/"+mod.AlternativeGroup;
    }
    internal static bool Uninstalled(GeneralModState state,GeneralModEntry mod,GeneralModVariant variant,FixedCatalog catalog,string root,string appData){
        string work=Owner(mod,variant);
        if(state.SnapshotReceipts.ContainsKey(work))return false;
        if(variant.CyberpunkId!=null){
            var recipe=FixedRecipe(catalog,variant);
            var paths=recipe.Id==NightCityDensity.ModId?NightCityDensity.KnownFiles(recipe):recipe.Files;
            return paths.Where(f=>!f.UserData).All(f=>!File.Exists(Safe.In(root,f.Path))&&!Directory.Exists(Safe.In(root,f.Path)));
        }
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
        foreach(var mod in project.Mods)foreach(var variant in mod.Variants)
            if(!variant.Sources.All(s=>GeneralModDownload.IsWebSource(s)||s=="installed://"+mod.Id+"/"+variant.Id))throw new IOException("Projeto contém caminhos privados de origem.");
        GeneralModLibrary.RestoreKnownMappings(project,GeneralModLibrary.Seed(catalog,new ModImportItem[0]));
        foreach(var installed in manifest.List.Mods){
            var mod=project.Mods.SingleOrDefault(m=>m.Id==installed.Id);
            if(mod==null||mod.DefaultVariant!=installed.DefaultVariant)throw new IOException("Projeto não corresponde aos mods transferidos.");
            var expected=installed.Variants.Single();var variant=mod.Variants.Single(v=>v.Id==mod.DefaultVariant);
            if(expected.CyberpunkId==NightCityDensity.ModId&&expected.CyberpunkVariant=="legacy-low"&&variant.Id=="legacy-low"&&variant.Sources.SequenceEqual(LegacyDensityVariant(catalog).Sources)){variant.CyberpunkId=expected.CyberpunkId;variant.CyberpunkVariant=expected.CyberpunkVariant;}
            if(!variant.Sources.SequenceEqual(expected.Sources)||variant.CyberpunkId!=expected.CyberpunkId||variant.CyberpunkVariant!=expected.CyberpunkVariant)throw new IOException("Fontes do projeto divergem do pacote instalado.");
        }
    }
}
