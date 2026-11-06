using System;
using System.Collections.Generic;
using System.Linq;

internal static class RetiredPackEntries {
    internal const string DroneId="mod-31642";
    internal const string DroneArchiveHash="6f0114f760f0d37d109f006955cc435d16a97b22a92c1987ec93a018b2183467";
    internal static bool PreparedArchive(ModImportItem item){return item!=null&&item.Kind=="Arquivo"&&item.Sha256==DroneArchiveHash;}
    internal static HashSet<string> Remove(GeneralModList list){
        // Retire only known built-in identities; a new custom item remains the user's choice.
        var removed=new HashSet<string>(list.Mods.Where(mod=>mod.Id=="mod-12572"||mod.Id==DroneId||mod.Id=="file-"+DroneArchiveHash||mod.Variants.Any(v=>v.CyberpunkId=="mod-12572"||v.CyberpunkId==DroneId)).Select(mod=>mod.Id),StringComparer.Ordinal);
        list.Mods.RemoveAll(mod=>removed.Contains(mod.Id));
        foreach(var preset in list.Presets)preset.ModIds.RemoveAll(removed.Contains);
        return removed;
    }
    internal static void Remove(GeneralModState state){
        var removed=Remove(state.List);
        if(state.ChosenVariants!=null)foreach(string key in state.ChosenVariants.Keys.Where(removed.Contains).ToArray())state.ChosenVariants.Remove(key);
        ClearReferences(state.ResolvedDownloads,removed);ClearReferences(state.CyberpunkImports,removed);
    }
    static void ClearReferences(Dictionary<string,string> references,HashSet<string> removed){
        if(references==null)return;
        foreach(string key in references.Keys.Where(key=>removed.Any(id=>key.StartsWith(id+"/",StringComparison.Ordinal))).ToArray())references.Remove(key);
    }
}
