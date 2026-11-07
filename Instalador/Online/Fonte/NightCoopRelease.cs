using System;
using System.Linq;

internal static class NightCoopRelease {
    internal const string Version="0.2.0-beta";
    internal const string PriorUrl="https://nightcoop.net/dl/0.1.2-beta/NightCoop-0.1.2-beta-2.5.zip";
    internal const string Url="https://nightcoop.net/dl/0.2.0-beta/NightCoop-0.2.0-beta-2.5.zip";
    internal const string DllPath="red4ext/plugins/NightCoop/NightCoop.dll";
    internal const string PriorDllHash="65960e5926108e8de9dfd46476a10752f1ad7b8891152c81fd30af21dfa21a6f";
    internal const long PriorDllSize=19016192;
    const string DllHash="447ee81cb5200de2b372372a5c68dd36275c7b172eeee009f56e5d5d606c55f7";
    const long DllSize=21416448;

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

    internal static bool AllowsUpgrade(InstalledSnapshotFile file,string before,FixedCatalog catalog){
        if(file.Path!=DllPath||file.Sha256!=DllHash||file.Size!=DllSize||before!=PriorDllHash)return false;
        var basis=catalog.Packages.SingleOrDefault(mod=>mod.Id=="NightCoop"&&mod.Immutable&&mod.Version==Version);
        return basis!=null&&basis.Files.Any(IsUpgradeTarget);
    }

    // A previously installed pack's local receipt still describes its original files.
    // This exception is only applied after decoding a protected local receipt.
    internal static FixedCatalog CatalogForReceipt(FixedCatalog catalog,InstalledSnapshotManifest manifest){
        if(manifest==null||manifest.List==null||manifest.List.Mods==null||
            !manifest.List.Mods.Any(mod=>mod!=null&&mod.Id=="NightCoop"&&mod.Variants!=null&&mod.Variants.Any(variant=>
                variant!=null&&variant.Sources!=null&&variant.Sources.SequenceEqual(new[]{PriorUrl}))))return catalog;
        var current=catalog.Packages.SingleOrDefault(mod=>mod.Id=="NightCoop"&&mod.Version==Version);
        if(current==null)return catalog;
        var old=Safe.Json.Deserialize<FixedMod>(Safe.Json.Serialize(current));
        old.Version="0.1.2-beta";
        old.Assets[0].Name="NightCoop-0.1.2-beta-2.5.zip";
        old.Assets[0].Url=PriorUrl;
        old.Assets[0].Sha256="5418bc90a68c650344126a9532bec933d18c3e70cb9a97c515bb8695533c1621";
        old.Assets[0].Size=8895082;
        old.Files.RemoveAll(file=>AddedPaths.Contains(file.Path));
        var dll=old.Files.Single(file=>file.Path==DllPath);
        dll.Sha256=PriorDllHash;dll.Size=PriorDllSize;
        return new FixedCatalog{
            Schema=catalog.Schema,Id=catalog.Id,GameVersion=catalog.GameVersion,GameFileVersion=catalog.GameFileVersion,
            Defaults=catalog.Defaults,Excluded=catalog.Excluded,
            Packages=catalog.Packages.Select(mod=>mod==current?old:mod).ToList()
        };
    }
}
