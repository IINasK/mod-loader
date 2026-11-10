using System;
using System.Collections.Generic;
using System.Linq;

internal static class RetiredPackEntries {
    internal static readonly string[] EmptyStoreIds=new[]{"mod-12642","mod-30021","mod-8688","mod-6779"};
    internal static readonly string[] EmptyStoreHashes=new[]{"b4975e9ae3aa5afb0c73efc83119c3a4ebdc52d208d371562fbc75321325890f","1eae8079e7110b5cd28d8095fde2948fdbe1e1fcc462c0cc342f5336d4a099b8","d89d2b8f549fe9f713978b323028f0d03be93dd55bd45275c5884300a0aaedf0","be221717ff154c4c81a17b0a8e9c56333302f54c0bbfd5c9e01813e3557246a9"};
    internal const string RadioId="SpotOnRadio";
    internal const string RadioArchiveHash="1b55f0713673221f18e9c619f9e990456383be781ebe611dd0fa08927da62f13";
    internal const string HotlineId="15-NCPDHotline", HotlineHash="d493b3db9c77fe5895f7eb6b697f70a02ec3dbb0db90b038181ed825c0a16658";
    internal const string Map8KId="mod-17811", RipperPantsId="mod-18084";
    internal const string Map8KHash="95602fb43a0ae73bf0c78f102258dcdea0f2c377729d4ac95d4d3b60eaefa0c5";
    internal const string RipperPantsHash="1b777f045286f9914a7888324438709aa7e4fa890ff00dbc5ba1c514c503750e";
    internal const string DroneId="mod-31642";
    internal const string DroneArchiveHash="6f0114f760f0d37d109f006955cc435d16a97b22a92c1987ec93a018b2183467";
    static readonly string[] DroneFolders={
        "bin/x64/plugins/cyber_engine_tweaks/mods/FPVDrone",
        "red4ext/plugins/FPVDroneInput",
        "r6/scripts/FPVDrone",
        "r6/audioware/FPVDrone"
    };
    internal static bool LocalDronePath(string path) {
        return path != null && (path.Equals("archive/pc/mod/FPVDrone.archive",StringComparison.OrdinalIgnoreCase)
            || DroneFolders.Any(folder=>path.Equals(folder,StringComparison.OrdinalIgnoreCase)||path.StartsWith(folder+"/",StringComparison.OrdinalIgnoreCase)));
    }
    internal static bool LocalDroneFile(InstalledSnapshotFile file){
        return LocalDronePath(file.Path)||String.Equals(file.Path,"engine/config/platform/pc/FovFix.ini",StringComparison.OrdinalIgnoreCase)&&
            file.Sha256=="5036a02f8c9163f7035f6a6f2cb58de0d99d62fab572c8cafe6faea3faef3789";
    }

    static readonly HashSet<string> RetiredIds=new HashSet<string>(EmptyStoreIds.Concat(new[]{
        HotlineId,DroneId,RadioId,"mod-12572",Map8KId,RipperPantsId
    }),StringComparer.Ordinal);
    static readonly HashSet<string> RetiredHashes=new HashSet<string>(EmptyStoreHashes.Concat(new[]{
        HotlineHash,DroneArchiveHash,RadioArchiveHash,Map8KHash,RipperPantsHash
    }),StringComparer.Ordinal);
    static readonly HashSet<string> RetiredArchiveIds=new HashSet<string>(RetiredHashes.Select(hash=>"file-"+hash),StringComparer.Ordinal);

    internal static bool PreparedArchive(ModImportItem item){return item!=null&&item.Kind=="Arquivo"&&RetiredHashes.Contains(item.Sha256);}
    internal static HashSet<string> Remove(GeneralModList list){
        // Retire only known built-in identities; a new custom item remains the user's choice.
        var removed=new HashSet<string>(list.Mods.Where(mod=>RetiredIds.Contains(mod.Id)||RetiredArchiveIds.Contains(mod.Id)||
            mod.Variants.Any(variant=>RetiredIds.Contains(variant.CyberpunkId))).Select(mod=>mod.Id),StringComparer.Ordinal);
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
