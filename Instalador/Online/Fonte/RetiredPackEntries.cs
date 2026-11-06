using System;
using System.Collections.Generic;
using System.Linq;

internal static class RetiredPackEntries {
    internal static readonly string[] EmptyStoreIds=new[]{"mod-12642","mod-30021","mod-8688","mod-6779"};
    internal static readonly string[] EmptyStoreHashes=new[]{"b4975e9ae3aa5afb0c73efc83119c3a4ebdc52d208d371562fbc75321325890f","1eae8079e7110b5cd28d8095fde2948fdbe1e1fcc462c0cc342f5336d4a099b8","d89d2b8f549fe9f713978b323028f0d03be93dd55bd45275c5884300a0aaedf0","be221717ff154c4c81a17b0a8e9c56333302f54c0bbfd5c9e01813e3557246a9"};
    internal const string RadioId="SpotOnRadio";
    internal const string RadioArchiveHash="1b55f0713673221f18e9c619f9e990456383be781ebe611dd0fa08927da62f13";
    internal const string DroneId="mod-31642";
    internal const string DroneArchiveHash="6f0114f760f0d37d109f006955cc435d16a97b22a92c1987ec93a018b2183467";
    internal static bool PreparedArchive(ModImportItem item){return item!=null&&item.Kind=="Arquivo"&&(item.Sha256==DroneArchiveHash||item.Sha256==RadioArchiveHash||EmptyStoreHashes.Contains(item.Sha256));}
    internal static HashSet<string> Remove(GeneralModList list){
        // Retire only known built-in identities; a new custom item remains the user's choice.
        var removed=new HashSet<string>(list.Mods.Where(mod=>EmptyStoreIds.Contains(mod.Id)||EmptyStoreHashes.Any(h=>mod.Id=="file-"+h)||mod.Id==RadioId||mod.Id=="file-"+RadioArchiveHash||mod.Id=="mod-12572"||mod.Id==DroneId||mod.Id=="file-"+DroneArchiveHash||mod.Variants.Any(v=>EmptyStoreIds.Contains(v.CyberpunkId)||v.CyberpunkId==RadioId||v.CyberpunkId=="mod-12572"||v.CyberpunkId==DroneId)).Select(mod=>mod.Id),StringComparer.Ordinal);
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
