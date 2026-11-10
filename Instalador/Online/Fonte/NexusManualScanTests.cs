using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal sealed partial class GeneralModWindow {
    internal static void RunNexusPreviewTests(string parent) {
        var root=OnlineTests.NestedRoot(parent,"nexus-tree-");
        Directory.CreateDirectory(root);
        var game=OnlineTests.Fixture(root,"game");
        const string modUrl="https://www.nexusmods.com/cyberpunk2077/mods/9999";
        const string archive="archive/pc/mod/manual-example.archive";
        const string script="r6/scripts/manual-example/start.reds";
        const string rawOld="{\"children\":[{\"type\":\"directory\",\"children\":[{\"type\":\"file\",\"path\":\"Release/archive/pc/mod/manual-example.archive\",\"size\":\"3 bytes\"}]},{\"type\":\"file\",\"path\":\"Release/r6/scripts/manual-example/start.reds\",\"size\":\"0.01 KB\"}]}";
        const string rawNew="{\"version\":1,\"files\":[{\"file_path\":\"Release/archive/pc/mod/manual-example.archive\",\"file_size\":5},{\"file_path\":\"Release/r6/scripts/manual-example/start.reds\",\"file_size\":7},{\"file_path\":\"README.md\",\"file_size\":160}]}";
        Action<bool,string> check=(ok,reason)=>{if(!ok)throw new IOException(reason);};

        var legacy=GeneralNexusInfo.ParsePreview(rawOld);
        var current=GeneralNexusInfo.ParsePreview(rawNew);
        check(legacy.Count==2&&legacy["Release/"+archive]==3&&legacy["Release/"+script]==null,"Legacy tree preserves only exact sizes.");
        check(current.Count==3&&current["Release/"+script]==7,"Flat manifest contains exact byte counts.");
        check(GeneralNexusInfo.ParsePreview(rawNew.Replace("Release/r6","../r6"))==null&&
            GeneralNexusInfo.ParsePreview(rawNew.Replace("\"version\":1","\"version\":2"))==null&&
            GeneralNexusInfo.ParsePreview(rawNew.Replace("README.md","release/"+archive))==null,
            "Traversal, unsupported schema, and duplicate paths are rejected.");

        const string alias="https://file-metadata.nexusmods.com/file/nexus-files-s3-meta/3333/9999/ab/cd/0123-4567.json";
        check(GeneralNexusInfo.PreviewUri(alias).AbsoluteUri=="https://mod-file-manifests.nexusmods.com/ab/cd/0123-4567"&&
            GeneralNexusInfo.PreviewUri("https://file-metadata.nexusmods.com.example.org/preview.json")==null&&
            GeneralNexusInfo.PreviewUri("https://name@file-metadata.nexusmods.com/preview.json")==null,
            "Only official HTTPS preview hosts are accepted.");

        Func<int,string,string> metadata=(id,url)=>"{\"file_id\":"+id+",\"version\":\""+id+".0\",\"content_preview_link\":\""+url+"\"}";
        int metadataRequests=0,cdnRequests=0;
        Func<Uri,bool,string> request=(uri,api)=>{
            if(api){
                System.Threading.Interlocked.Increment(ref metadataRequests);
                check(uri.Host=="api.nexusmods.com"&&uri.AbsolutePath.Contains("/cyberpunk2077/mods/9999/files"),"API query uses game and mod identity.");
                if(uri.AbsolutePath.EndsWith("/1.json"))return metadata(1,"https://file-metadata.nexusmods.com/legacy.json");
                if(uri.AbsolutePath.EndsWith("/2.json"))return metadata(2,alias);
                return "{\"files\":["+metadata(1,"https://file-metadata.nexusmods.com/legacy.json")+","+metadata(2,alias)+"]}";
            }
            System.Threading.Interlocked.Increment(ref cdnRequests);
            check(uri.Host=="mod-file-manifests.nexusmods.com"||uri.Host=="file-metadata.nexusmods.com","Public preview is fetched without forwarding API credentials.");
            return uri.Host=="file-metadata.nexusmods.com"?rawOld:rawNew;
        };
        Func<GeneralNexusLink,Func<bool>,NexusFilePreview[]> fetch=(link,budget)=>GeneralNexusInfo.ReadFilePreviews(link,budget,request);
        var mod=new GeneralModEntry{Id="manual-fixture",Name="Manual example",DefaultVariant="old",Variants=new List<GeneralModVariant>{
            new GeneralModVariant{Id="old",Name="Old",Sources=new List<string>{modUrl+"?tab=files&file_id=1"}},
            new GeneralModVariant{Id="new",Name="New",Sources=new List<string>{modUrl+"?tab=files&file_id=2"}}
        }};
        var page=new GeneralModEntry{Id="manual-page",Name="Only a Nexus page",DefaultVariant="default",Variants=new List<GeneralModVariant>{
            new GeneralModVariant{Id="default",Name="Main",Sources=new List<string>{modUrl}}
        }};
        var catalog=new FixedCatalog{Schema=1,Id="nexus-tree-fixture",GameVersion="2.5",GameFileVersion="2.5.0.0",Packages=new List<FixedMod>()};
        var state=new GeneralModState{List=new GeneralModList{Mods=new List<GeneralModEntry>{mod,page}},Language="pt"};
        string archivePath=Safe.In(game,archive),scriptPath=Safe.In(game,script);
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath));
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath));
        File.WriteAllBytes(archivePath,new byte[5]);
        File.WriteAllBytes(scriptPath,new byte[7]);
        const string unrelated="archive/pc/mod/unrelated.archive";
        File.WriteAllBytes(Safe.In(game,unrelated),new byte[12]);
        TestForm(catalog,state,Path.Combine(root,"data"),root,async form=>{
            form.testNexusPreviews=fetch;
            form.SetDestination(game);
            var entry=form.state.List.Mods.Single(item=>item.Id==mod.Id);
            var pageEntry=form.state.List.Mods.Single(item=>item.Id==page.Id);
            await form.Scan();
            check(form.Selected(entry).Id=="new"&&form.ListStatus(entry)==GeneralStatusPresentation.NexusManual&&
                !form.state.ChosenVariants.ContainsKey(entry.Id),"Opening the project recognizes the installed non-default variant without changing saved choices.");
            check(form.ListStatus(pageEntry)==GeneralStatusPresentation.NexusManual&&form.NexusVariantName(pageEntry)=="Main · 2.0",
                "The Nexus mod page can identify the installed file version without a file ID selected by the user.");
            check(form.VisibleGameInventory().Any(file=>file.Path==unrelated)&&
                !form.VisibleGameInventory().Any(file=>file.Path==archive||file.Path==script),
                "Automatically recognized files still disappear from the unidentified section.");
            var work=form.MakeWork(entry);
            var pending=new List<Work>();
            form.PlanSelected(new List<Work>{work},game,pending);
            check(pending.Count==0&&form.state.SnapshotReceipts.Count==0&&form.state.CyberpunkImports.Count==0&&
                !GeneralStatusPresentation.DownloadComplete(form.ListStatus(entry)),
                "Recognition avoids a redundant download and does not claim verified ownership.");
            check(metadataRequests==3&&cdnRequests==4,"Pinned and unpinned requests use the right endpoints.");
            File.Delete(scriptPath);
            await form.Scan();
            check(form.Selected(entry).Id=="old"&&form.ListStatus(entry)!=GeneralStatusPresentation.NexusManual&&
                form.gameInventory.Any(file=>file.Kind=="found"&&file.Path==archive),
                "A rescan drops the previous recognition if a required file is removed.");
            check(metadataRequests==4,"Pinned previews are reused, while the mod page is refreshed.");
        });

        File.WriteAllBytes(scriptPath,new byte[7]);
        var sample=new NexusFilePreview{Id=1,Version="1.0",Files=current};
        var same=NexusManualScan.Find(game,true,mod,link=>new[]{sample});
        check(same!=null&&same.Variant==null,"Two matching variants are reported as ambiguous.");
        same=NexusManualScan.Find(game,true,mod,link=>link.File==1?null:new[]{sample});
        check(same!=null&&same.Variant==null,"An unavailable sibling preview does not cause a version guess.");

        string generic=Safe.In(root,"other-game");
        Directory.CreateDirectory(generic);
        Directory.CreateDirectory(Safe.In(generic,"Addons"));
        Directory.CreateDirectory(Safe.In(generic,"Plugins"));
        File.WriteAllBytes(Safe.In(generic,"Addons/item.asset"),new byte[8]);
        File.WriteAllBytes(Safe.In(generic,"Plugins/start.lua"),new byte[3]);
        page.Variants[0].InstallRoutes=GeneralInstallRoutes.Parse("Assets = Addons | Scripts = Plugins",generic);
        var routed=new NexusFilePreview{Files=GeneralNexusInfo.ParsePreview(
            "{\"version\":1,\"files\":[{\"file_path\":\"Assets/item.asset\",\"file_size\":8},{\"file_path\":\"Scripts/start.lua\",\"file_size\":3}]}")};
        check(NexusManualScan.Find(generic,false,page,link=>new[]{routed}).Variant==page.Variants[0],
            "Recognition reuses explicit installation routes across multiple directories in another game.");
    }
}
