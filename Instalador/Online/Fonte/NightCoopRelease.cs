using System;
using System.Linq;

internal static class NightCoopRelease {
    internal const string Version="0.2.1-beta";
    const string BaseUrl="https://nightcoop.net/dl/";
    internal const string LegacyUrl=BaseUrl+"0.1.2-beta/NightCoop-0.1.2-beta-2.5.zip";
    internal const string PriorUrl=BaseUrl+"0.2.0-beta/NightCoop-0.2.0-beta-2.5.zip";
    internal const string Url=BaseUrl+Version+"/NightCoop-"+Version+"-2.5.zip";
    internal const string DllPath="red4ext/plugins/NightCoop/NightCoop.dll";
    const string LegacyDllHash="65960e5926108e8de9dfd46476a10752f1ad7b8891152c81fd30af21dfa21a6f";
    const long LegacyDllSize=19016192;
    const string PriorDllHash="447ee81cb5200de2b372372a5c68dd36275c7b172eeee009f56e5d5d606c55f7";
    const long PriorDllSize=21416448;
    const string DllHash="03c621297ffb5e634e42ccbae542e7435d237986ca527dc40bd91cd93e68b19a";
    const long DllSize=23604224;

    // compat-b1 / mods_true_list: this verified build excludes folders without mod files.
    internal static bool IgnoresEmptyGroups(System.Collections.Generic.IEnumerable<InstalledSnapshotFile> files){
        return files.Any(file=>String.Equals(file.Path,DllPath,StringComparison.OrdinalIgnoreCase)&&file.Size==DllSize&&file.Sha256==DllHash);
    }

    internal static readonly string[] AddedPaths={
        "red4ext/plugins/NightCoop/redscript/NightCoopSdk.reds",
        "red4ext/plugins/NightCoop/redscript/NightCoopSdkNative.reds",
        "r6/scripts/NightCoopFlight/NightCoopFlight.reds",
        "r6/scripts/NightCoopFlight/LICENSE.txt",
        "r6/scripts/NightCoopFlight/THIRD_PARTY.md"
    };

    internal static bool IsUpgradeTarget(FixedFile file){
        return file.Path==DllPath&&file.Sha256==DllHash&&file.Size==DllSize;
    }
    internal static bool IsPriorSource(System.Collections.Generic.IEnumerable<string> sources){
        return sources.SequenceEqual(new[]{PriorUrl})||sources.SequenceEqual(new[]{LegacyUrl});
    }
    internal static string PriorVersion(string hash){
        return hash==PriorDllHash?"0.2.0-beta":hash==LegacyDllHash?"0.1.2-beta":null;
    }
    internal static bool IsPriorDll(long size,string hash){
        return size==PriorDllSize&&hash==PriorDllHash||size==LegacyDllSize&&hash==LegacyDllHash;
    }
    internal static bool IsPresetUpgrade(PresetPackage before,PresetPackage after){
        var prior=before.Files.SingleOrDefault(file=>file.Path==DllPath);
        return before.Immutable&&prior!=null&&IsPriorDll(prior.Size,prior.Hash)&&
            after.Files.Any(file=>file.Path==DllPath&&file.Size==DllSize&&file.Hash==DllHash)&&
            before.Files.Where(file=>file!=prior).All(file=>after.Files.Any(next=>next.Path==file.Path&&next.Size==file.Size&&next.Hash==file.Hash));
    }

    internal static bool AllowsUpgrade(InstalledSnapshotFile file,string before,FixedCatalog catalog){
        if(file.Path!=DllPath||file.Sha256!=DllHash||file.Size!=DllSize||PriorVersion(before)==null)return false;
        var basis=catalog.Packages.SingleOrDefault(mod=>mod.Id=="NightCoop"&&mod.Immutable&&mod.Version==Version);
        return basis!=null&&basis.Files.Any(IsUpgradeTarget);
    }

    // A previously installed pack's local receipt still describes its original files.
    // This exception is only applied after decoding a protected local receipt.
    internal static FixedCatalog CatalogForReceipt(FixedCatalog catalog,InstalledSnapshotManifest manifest){
        var entry=manifest==null||manifest.List==null||manifest.List.Mods==null?null:
            manifest.List.Mods.FirstOrDefault(mod=>mod!=null&&mod.Id=="NightCoop");
        var variant=entry==null||entry.Variants==null||entry.Variants.Count!=1?null:entry.Variants[0];
        if(variant==null||variant.Sources==null||!IsPriorSource(variant.Sources))return catalog;
        bool legacy=variant.Sources[0]==LegacyUrl;
        var current=catalog.Packages.SingleOrDefault(mod=>mod.Id=="NightCoop"&&mod.Version==Version);
        if(current==null)return catalog;
        var old=Safe.Json.Deserialize<FixedMod>(Safe.Json.Serialize(current));
        old.Version=legacy?"0.1.2-beta":"0.2.0-beta";
        old.Assets[0].Name="NightCoop-"+old.Version+"-2.5.zip";
        old.Assets[0].Url=variant.Sources[0];
        old.Assets[0].Sha256=legacy?"5418bc90a68c650344126a9532bec933d18c3e70cb9a97c515bb8695533c1621":"ca070aa296519fb4147dadf14a625a538df59d6a20d289fa3e335ae3da3cace5";
        old.Assets[0].Size=legacy?8895082:10117378;
        if(legacy)old.Files.RemoveAll(file=>AddedPaths.Contains(file.Path));
        var dll=old.Files.Single(file=>file.Path==DllPath);
        dll.Sha256=legacy?LegacyDllHash:PriorDllHash;dll.Size=legacy?LegacyDllSize:PriorDllSize;
        return new FixedCatalog{
            Schema=catalog.Schema,Id=catalog.Id,GameVersion=catalog.GameVersion,GameFileVersion=catalog.GameFileVersion,
            Defaults=catalog.Defaults,Excluded=catalog.Excluded,
            Packages=catalog.Packages.Select(mod=>mod==current?old:mod).ToList()
        };
    }
}
