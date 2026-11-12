using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

// Presets share mod identity and payloads; only their configuration is independent.
internal static class GeneralPresetProfiles {
    internal static T Copy<T>(T value) { return Safe.Json.Deserialize<T>(Safe.Json.Serialize(value)); }
    internal static GeneralModPreset Selected(GeneralModState state) { return state.List.Presets.SingleOrDefault(p=>p.Id==state.SelectedPreset); }
    internal static void Initialize(GeneralModState state) {
        if(state==null||state.List==null||state.List.Mods==null||state.List.Mods.Any(m=>m==null))throw new IOException("Configuração de presets inválida.");
        if(state.List.Presets==null)state.List.Presets=new List<GeneralModPreset>();
        if(state.List.Presets.Any(p=>p==null||p.ModIds==null))throw new IOException("Configuração de presets inválida.");
        if(state.ChosenVariants==null)state.ChosenVariants=new Dictionary<string,string>();
        if(state.List.AlternativeGroups==null)state.List.AlternativeGroups=new Dictionary<string,GeneralAlternativeGroup>();
        foreach(var preset in state.List.Presets) {
            bool legacy=state.PresetStateVersion==0&&(preset.Configurations==null||preset.Configurations.Count==0)&&
                (preset.VariantChoices==null||preset.VariantChoices.Count==0)&&(preset.AlternativeChoices==null||preset.AlternativeChoices.Count==0)&&
                preset.BodyChoice==null&&(preset.Groups==null||preset.Groups.Count==0);
            if(preset.Configurations==null)preset.Configurations=new Dictionary<string,GeneralModEntry>();
            if(preset.VariantChoices==null)preset.VariantChoices=new Dictionary<string,string>();
            if(preset.AlternativeChoices==null)preset.AlternativeChoices=new Dictionary<string,string>();
            foreach(string id in preset.Configurations.Keys.Where(id=>!preset.ModIds.Contains(id)).ToArray())preset.Configurations.Remove(id);
            foreach(string id in preset.VariantChoices.Keys.Where(id=>!preset.ModIds.Contains(id)).ToArray())preset.VariantChoices.Remove(id);
            foreach(var mod in state.List.Mods.Where(m=>preset.ModIds.Contains(m.Id))) {
                if(!preset.Configurations.ContainsKey(mod.Id))preset.Configurations.Add(mod.Id,Copy(mod));
                string choice;
                if(legacy&&state.ChosenVariants.TryGetValue(mod.Id,out choice))preset.VariantChoices[mod.Id]=choice;
            }
            if(legacy){preset.BodyChoice=state.BodyChoice;preset.Groups=Copy(state.List.AlternativeGroups);}
            if(preset.Groups==null)preset.Groups=new Dictionary<string,GeneralAlternativeGroup>();
        }
        state.PresetStateVersion=1;
    }
    internal static void Capture(GeneralModState state) {
        Initialize(state);
        var preset=Selected(state);if(preset==null)return;
        foreach(var mod in state.List.Mods.Where(m=>preset.ModIds.Contains(m.Id)))preset.Configurations[mod.Id]=Copy(mod);
        preset.VariantChoices=state.ChosenVariants.Where(p=>preset.ModIds.Contains(p.Key)).ToDictionary(p=>p.Key,p=>p.Value);
        preset.BodyChoice=state.BodyChoice;preset.Groups=Copy(state.List.AlternativeGroups);
    }
    internal static void Switch(GeneralModState state,string selected) {
        Capture(state);state.SelectedPreset=selected;Project(state);
    }
    internal static void Project(GeneralModState state) {
        Initialize(state);var preset=Selected(state);if(preset==null)return;
        for(int i=0;i<state.List.Mods.Count;i++) {
            GeneralModEntry configured;
            if(preset.ModIds.Contains(state.List.Mods[i].Id)&&preset.Configurations.TryGetValue(state.List.Mods[i].Id,out configured))state.List.Mods[i]=Copy(configured);
        }
        state.ChosenVariants=new Dictionary<string,string>(preset.VariantChoices);state.BodyChoice=preset.BodyChoice;state.List.AlternativeGroups=Copy(preset.Groups);
    }
    internal static GeneralModState ForPreset(GeneralModState state,string id) {
        var copy=Copy(state);copy.TemplateId=state.TemplateId;copy.ProjectName=state.ProjectName;
        Initialize(copy);copy.SelectedPreset=id;Project(copy);return copy;
    }
    internal static void Edit(GeneralModState state,GeneralModEntry before,GeneralModEntry after,string variant,bool all,IEnumerable<string> memberships=null,string previousVariant=null) {
        Initialize(state);var selected=Selected(state);
        var targets=state.List.Presets.Where(p=>p.ModIds.Contains(before.Id)&&(all||p==selected)).ToArray();
        foreach(var preset in targets) {
            GeneralModEntry original;
            if(!preset.Configurations.TryGetValue(before.Id,out original))original=before;
            var updated=Copy(original);Patch(before,after,updated);
            if(updated.Variants.Count==0){updated.Variants.Add(Copy(after.Variants.First(v=>v.Id==after.DefaultVariant)));updated.DefaultVariant=after.DefaultVariant;}
            else if(!updated.Variants.Any(v=>v.Id==updated.DefaultVariant))updated.DefaultVariant=updated.Variants[0].Id;
            preset.Configurations[before.Id]=updated;
            string previous;
            if(!state.ChosenVariants.TryGetValue(before.Id,out previous))previous=before.DefaultVariant;
            if(variant!=(previousVariant??previous)&&updated.Variants.Any(v=>v.Id==variant))preset.VariantChoices[before.Id]=variant;
            string choice;
            if(preset.VariantChoices.TryGetValue(before.Id,out choice)&&!updated.Variants.Any(v=>v.Id==choice))preset.VariantChoices.Remove(before.Id);
        }
        int at=state.List.Mods.FindIndex(m=>m.Id==before.Id);
        if(at<0)state.List.Mods.Add(Copy(after));else if(all||selected==null) { var common=Copy(state.List.Mods[at]);Patch(before,after,common);state.List.Mods[at]=common; }
        if(memberships!=null) {
            var wanted=new HashSet<string>(memberships,StringComparer.Ordinal);
            foreach(var preset in state.List.Presets) {
                if(wanted.Contains(preset.Id)) {
                    if(!preset.ModIds.Contains(after.Id)){preset.ModIds.Add(after.Id);preset.Configurations[after.Id]=Copy(after);preset.VariantChoices[after.Id]=variant;}
                } else {preset.ModIds.Remove(after.Id);preset.Configurations.Remove(after.Id);preset.VariantChoices.Remove(after.Id);}
            }
        }
        if(selected!=null&&selected.ModIds.Contains(after.Id)) {
            if(!selected.Configurations.ContainsKey(after.Id))selected.Configurations[after.Id]=Copy(after);
            state.List.Mods[state.List.Mods.FindIndex(m=>m.Id==after.Id)]=Copy(selected.Configurations[after.Id]);
            state.ChosenVariants[after.Id]=variant;selected.VariantChoices[after.Id]=variant;
        } else if(at>=0)state.List.Mods[at]=Copy(after);
    }
    // Apply only fields actually changed, including individual versions, to preserve other preset customizations.
    static void Patch<T>(T before,T after,T target) {
        foreach(var field in typeof(T).GetFields(BindingFlags.Public|BindingFlags.Instance)) {
            object left=field.GetValue(before),right=field.GetValue(after);
            if(Safe.Json.Serialize(left)==Safe.Json.Serialize(right))continue;
            if(typeof(T)==typeof(GeneralModEntry)&&field.Name=="Variants") {
                var old=(List<GeneralModVariant>)left;var next=(List<GeneralModVariant>)right;var actual=(List<GeneralModVariant>)field.GetValue(target);
                actual.RemoveAll(v=>old.Any(x=>x.Id==v.Id)&&!next.Any(x=>x.Id==v.Id));
                foreach(var version in next) {
                    var previous=old.FirstOrDefault(v=>v.Id==version.Id);var existing=actual.FirstOrDefault(v=>v.Id==version.Id);
                    if(existing==null&&previous==null)actual.Add(Copy(version));else if(existing!=null&&previous!=null)Patch(previous,version,existing);
                }
            }else if(typeof(T)==typeof(GeneralModVariant)&&field.Name=="NaturalOption"&&left!=null&&right!=null&&field.GetValue(target)!=null)
                Patch((GeneralModVariant)left,(GeneralModVariant)right,(GeneralModVariant)field.GetValue(target));
            else field.SetValue(target,right==null?null:Safe.Json.Deserialize(Safe.Json.Serialize(right),field.FieldType));
        }
        var variant=(object)target as GeneralModVariant;
        if(variant!=null&&variant.SourceInfo!=null)
            foreach(var source in variant.SourceInfo.Keys.Where(s=>!variant.Sources.Contains(s)).ToArray())variant.SourceInfo.Remove(source);
    }
    internal static void Remove(GeneralModState state,string id,bool all) {
        Initialize(state);
        foreach(var preset in state.List.Presets.Where(p=>all||p.Id==state.SelectedPreset)) {preset.ModIds.Remove(id);preset.Configurations.Remove(id);preset.VariantChoices.Remove(id);}
        if(all||!state.List.Presets.Any(p=>p.ModIds.Contains(id))) {state.List.Mods.RemoveAll(m=>m.Id==id);state.ChosenVariants.Remove(id);}
    }
    internal static void Prune(GeneralModList list) {
        var ids=new HashSet<string>(list.Mods.Select(m=>m.Id),StringComparer.Ordinal);
        foreach(var p in list.Presets) {
            p.ModIds.RemoveAll(id=>!ids.Contains(id));
            if(p.Configurations!=null)foreach(var id in p.Configurations.Keys.Where(id=>!p.ModIds.Contains(id)).ToArray())p.Configurations.Remove(id);
            if(p.VariantChoices!=null)foreach(var id in p.VariantChoices.Keys.Where(id=>!p.ModIds.Contains(id)).ToArray())p.VariantChoices.Remove(id);
        }
    }
    internal static void Validate(GeneralModList list,bool trusted) {
        foreach(var preset in list.Presets) {
            if(preset.Configurations==null)preset.Configurations=new Dictionary<string,GeneralModEntry>();
            if(preset.VariantChoices==null)preset.VariantChoices=new Dictionary<string,string>();
            if(preset.AlternativeChoices==null)preset.AlternativeChoices=new Dictionary<string,string>();
            foreach(string id in preset.Configurations.Keys.Where(id=>!list.Mods.Any(m=>m.Id==id)).ToArray())preset.Configurations.Remove(id);
            if(preset.Configurations.Count>1000||preset.VariantChoices.Count>1000||preset.AlternativeChoices.Count>1000)throw new IOException("Configuração de preset excede o limite.");
            foreach(var pair in preset.Configurations) {
                if(!preset.ModIds.Contains(pair.Key)||pair.Value==null||pair.Value.Id!=pair.Key)throw new IOException("Mod configurado fora do preset.");
            }
            if(preset.Configurations.Count>0)GeneralModLibrary.ValidateList(new GeneralModList {
                Categories=list.Categories.ToList(),AlternativeGroups=list.AlternativeGroups,Mods=preset.Configurations.Values.ToList()
            },trusted);
            foreach(var pair in preset.VariantChoices) {
                GeneralModEntry mod;
                if(!preset.ModIds.Contains(pair.Key))throw new IOException("Escolha de versão fora do preset.");
                if(!preset.Configurations.TryGetValue(pair.Key,out mod))mod=list.Mods.Single(m=>m.Id==pair.Key);
                if(!mod.Variants.Any(v=>v.Id==pair.Value))throw new IOException("Versão desconhecida no preset.");
            }
        }
    }
}
