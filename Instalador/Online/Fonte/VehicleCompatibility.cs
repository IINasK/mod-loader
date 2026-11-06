using System;
using System.Linq;

// Exact identities approved for the 2.5 vehicle repair. No arbitrary old-file acceptance.
internal static class VehicleCompatibility {
    sealed class Upgrade {internal string Path,Before,After;internal long BeforeSize;}
    static readonly Upgrade[] upgrades=new Upgrade[]{
        new Upgrade {Path="red4ext/plugins/ArchiveXL/ArchiveXL.dll",Before="ca32a4285cceb70b238b7b9f1d1785b836455bbfa359bc6c819478d7e8b69955",BeforeSize=1951744,After="97692f23b03fdb87354a3e1a4aaabba7dd604a9b8a16839afb8bcccd2bfe7674"},
        new Upgrade {Path="red4ext/plugins/ArchiveXL/Bundle/ArchiveXL.archive",Before="5b84333de16a536b36523090a6fbfcef4f866554cc93094df74ca6e37932c96f",BeforeSize=389120,After="8efa034dca39d563326eda65b63be87e66953b28f67145c81b2211e00d1c27d9"},
        new Upgrade {Path="red4ext/plugins/ArchiveXL/Bundle/PlayerCustomizationBeardFix.xl",Before="c3145023b30c0d0d2533af4c9d48c306aa4d868016e3de54ece46509ba40ff39",BeforeSize=21911,After="e666e6f1a9fafd03561c4a07df8dc773fdba2414370b04fa51b97035e1ec0036"},
        new Upgrade {Path="red4ext/plugins/ArchiveXL/Bundle/PlayerCustomizationHairFix.xl",Before="844060af19f21e97a23320df3e0b21c8299c66bc147da397b357a97d1a0ff95c",BeforeSize=1077556,After="9f008981d01a28e9b4ddeab85bd97cb952e99c8b11d72a805139334231d0b480"},
        new Upgrade {Path="red4ext/plugins/ArchiveXL/Bundle/PlayerCustomizationHairScope.xl",Before="f31eec0b6676dc64466034d11c5386ea4616e75b79da92ff72c7330a4f50ea83",BeforeSize=50466,After="888b881d88135408cb62c80796ef607ffc18f30c56125eab23ef7abdd43df4bb"},
        new Upgrade {Path="red4ext/plugins/ArchiveXL/Scripts/ArchiveXL.DynamicAppearance.reds",Before="43acc08335e541d81d92c235706fda692070c2e2fb5c60b96952e0a9e6ed7a83",BeforeSize=1926,After="bdb9ad85e5d4e956932f829ff5dae6ab66dd155244c9a09f0f6177ed48b0ee56"},
        new Upgrade {Path="red4ext/plugins/ArchiveXL/Scripts/ArchiveXL.Global.reds",Before="b76ab91e5d405cae0d65f1905d0c78df3e9a3a8d964add5d794befa7cc18c533",BeforeSize=369,After="118d95b378439f14b954e2287d667f779d005fff40fb2972e303bc255f3535a7"},
        new Upgrade {Path="red4ext/plugins/ArchiveXL/Scripts/ArchiveXL.reds",Before="650ec0ceb419b30cd23a77ab7b058725268572f87e15b5775b41cf1d89847bc8",BeforeSize=39,After="8d933d4ba814fe8bc85a065f0c449e13b3bfe61159928a10879ffe7340443e60"}
    };
    internal static bool HasUpgrade(FixedFile f){return upgrades.Any(u=>u.Path==f.Path&&u.After==f.Sha256);}
    internal static bool PriorHash(FixedFile f,string hash){return upgrades.Any(u=>u.Path==f.Path&&u.After==f.Sha256&&u.Before==hash);}
    internal static bool IsSource(FixedFile f,long size,string hash){return upgrades.Any(u=>u.Path==f.Path&&u.After==f.Sha256&&u.Before==hash&&u.BeforeSize==size);}
    internal static bool Pair(FixedFile f,string before,string after){return after==f.Sha256&&PriorHash(f,before);}
    internal static bool ApprovedPatch(FixedMod m,FixedFile f){
        return m.Id=="mod-10445"&&m.Version=="1.2"&&f.Path=="r6/tweaks/bmw_s1000rr/bmw_s1000rr.yaml"&&!f.UserData&&
            f.Patch!=null&&f.Patch.SourceSha256=="538344808d1b2afebd3a4def84481b7bd20b2d38deff20ab6e3691600099a644"&&f.Patch.SourceSize==1914&&
            f.Sha256=="558b78c394db472cfcbf1de6267a197e14ea68a3553b141fe3ca9c9c9c1e3739"&&f.Size==1374&&f.Patch.Changes!=null&&f.Patch.Changes.Count==1&&
            m.Assets.Single(a=>a.Id==f.Asset).Format=="zip";
    }
}
