using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal sealed partial class GeneralModWindow {
    // Explicit local diagnostic entry point; never called by startup or normal UI.
    // The named example selects the user's reported case only for this audit.
    internal static void RunOwnershipReadOnlyProbe(string parent) {
        string output=Path.GetFullPath(parent);Safe.NoLinks(output);Directory.CreateDirectory(output);
        string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string dev=new GeneralAppPaths(local,true).Data;
        var roots=new[]{dev,new GeneralAppPaths(local,false).Data,Path.Combine(local,"ModLoader","Config"),Path.Combine(local,"ModLoaderGeral")}
            .Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists).ToArray();
        var contexts=new List<GeneralUpdateOwnershipContext>();var metadata=new HashSet<string>(StringComparer.OrdinalIgnoreCase);int unreadable=0;
        foreach(string library in roots){
            var store=new GeneralProjectStore(library);string index=Path.Combine(library,"projects.json");if(File.Exists(index))metadata.Add(index);
            foreach(var project in store.Read(true).Projects){
                try{
                    string data=store.DataPath(project),path=Path.Combine(data,"lista.json");metadata.Add(path);
                    var state=store.Load(project,true);
                    contexts.Add(new GeneralUpdateOwnershipContext{Data=data,State=state,Name="Projeto "+(contexts.Count+1),
                        Proof=GeneralUpdateOwnership.ReceiptFingerprint(new{Index=store.IndexFingerprint(),Project=Safe.Hash(path)})});
                }catch(IOException){unreadable++;}catch(UnauthorizedAccessException){unreadable++;}
            }
        }
        var before=metadata.Where(File.Exists).ToDictionary(path=>path,Safe.Hash,StringComparer.OrdinalIgnoreCase);
        var results=new List<object>();var catalog=FixedData.Load();
        foreach(var current in contexts.Where(context=>context.Data.StartsWith(dev.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase)||
            String.Equals(context.Data,dev,StringComparison.OrdinalIgnoreCase)))
            foreach(var entry in current.State.List.Mods.Where(mod=>mod.Name.IndexOf("Night City Allies",StringComparison.OrdinalIgnoreCase)>=0)){
                string choice;current.State.ChosenVariants.TryGetValue(entry.Id,out choice);
                var variant=entry.Variants.FirstOrDefault(option=>option.Id==(choice??entry.DefaultVariant));if(variant==null)continue;
                int reads=0;long bytes=0;var timer=System.Diagnostics.Stopwatch.StartNew();
                var plan=GeneralUpdateOwnership.Resolve(current.State.Destination,current,entry,variant,catalog,contexts,false,path=>{
                    reads++;bytes+=new FileInfo(path).Length;return Safe.Hash(path);
                });
                timer.Stop();results.Add(new{Mod=entry.Name,plan.CanUpdate,plan.Recognized,plan.ForeignAuthority,plan.Reason,
                    Files=plan.Baseline==null?0:plan.Baseline.Files.Count,RelatedSnapshots=plan.RelatedReceipts.Count(receipt=>receipt.SnapshotId!=null),
                    RelatedGeneric=plan.RelatedReceipts.Count(receipt=>receipt.Key!=null),SharedProjects=plan.SharedProjects.Count,HashReads=reads,HashBytes=bytes,ElapsedMs=timer.ElapsedMilliseconds});
            }
        bool preserved=before.All(pair=>File.Exists(pair.Key)&&Safe.Hash(pair.Key)==pair.Value);
        Safe.WriteJson(Path.Combine(output,"readonly-ownership-probe.json"),new{Passed=preserved,ReadOnly=true,MetadataPreserved=preserved,
            RegisteredContexts=contexts.Count,UnreadableProjects=unreadable,Matches=results.Count,Results=results,NetworkUsed=false,InstalledOrUpdated=false});
        if(!preserved)throw new IOException("Project metadata changed during the read-only probe.");
    }

    internal static void RunOwnershipResolverTests(string parent) {
        string root=OnlineTests.NestedRoot(parent,"ownership-resolver-");Directory.CreateDirectory(root);
        var checks=new List<string>();Action<bool,string> check=(ok,label)=>{if(!ok)throw new IOException(label);checks.Add(label);};
        foreach(bool snapshot in new[]{false,true}) {
            string test=Safe.In(root,snapshot?"snapshot":"generic");var fixture=SharedUpdateSetup(test,snapshot);
            string currentData=Directory.CreateDirectory(Safe.In(test,"d")).FullName;
            new GeneralModLibrary(Safe.In(currentData,"lista.json")).Save(fixture.State);
            var current=SharedUpdateContext(currentData,fixture.State,"Current project");
            var unrelated=NewEntry("Unrelated mod","https://www.nexusmods.com/stardewvalley/mods/88?tab=files&file_id=90");
            unrelated.Id="unrelated";fixture.OwnerState.List.Mods.Add(unrelated);
            string unrelatedPath="mods/unrelated/payload.bin";
            string zip=SourceUpdateZip(test,"unrelated.zip",new Dictionary<string,string>{{unrelatedPath,"must not be hashed"}});
            var extra=new GeneralModInstaller(fixture.State.Destination,Path.Combine(fixture.OwnerData,"Instalacoes"));
            extra.Install(zip,P2PPack.Slot(unrelated,unrelated.Variants.Single(),0),_=>{});
            new GeneralModLibrary(Safe.In(fixture.OwnerData,"lista.json")).Save(fixture.OwnerState);
            var contexts=SharedUpdateContexts(fixture);
            var before=Directory.GetFiles(test,"*",SearchOption.AllDirectories).OrderBy(path=>path,StringComparer.OrdinalIgnoreCase)
                .Select(path=>path+"|"+new FileInfo(path).Length+"|"+new FileInfo(path).LastWriteTimeUtc.Ticks).ToArray();
            var calls=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
            Func<string,string> hash=path=>{
                if(path.Replace('\\','/').Contains("/mods/unrelated/"))throw new IOException("An unrelated payload was hashed.");
                int count;calls.TryGetValue(path,out count);calls[path]=count+1;return Safe.Hash(path);
            };
            var entry=fixture.State.List.Mods.Single();var variant=entry.Variants.Single();
            var plan=GeneralUpdateOwnership.Resolve(fixture.State.Destination,current,entry,variant,fixture.Catalog,contexts,true,hash);
            check(plan.CanUpdate&&plan.Recognized&&plan.ForeignAuthority&&plan.Baseline.Files.All(file=>file.Owned),
                "A complete foreign "+(snapshot?"snapshot":"generic")+" proof supplies authority without borrowing it from the clone. Reason="+plan.Reason);
            check(plan.RelatedReceipts.Count==2&&plan.RelatedReceipts.All(receipt=>FixedData.HashValid(receipt.Fingerprint))&&
                plan.SharedProjects.Contains("Projeto original")&&plan.SharedProjects.Contains("Projeto duplicado"),
                "Owner and clone metadata remain separate retirement targets and affected projects.");
            check(calls.Count==plan.Baseline.Files.Count&&calls.All(pair=>pair.Value==1),
                "Only this candidate's unique payload paths are hashed once; unrelated mods are not read.");
            check(plan.ProtectedPaths.Contains(unrelatedPath)&&!plan.Baseline.Files.Any(file=>plan.ProtectedPaths.Contains(file.Path)),
                "Unrelated mod paths remain protected while exact owner/clone aliases are approved together.");
            var after=Directory.GetFiles(test,"*",SearchOption.AllDirectories).OrderBy(path=>path,StringComparer.OrdinalIgnoreCase)
                .Select(path=>path+"|"+new FileInfo(path).Length+"|"+new FileInfo(path).LastWriteTimeUtc.Ticks).ToArray();
            check(before.SequenceEqual(after),"Ownership discovery does not create, rewrite or remove project/game files.");
            var cloneOnly=GeneralUpdateOwnership.Resolve(fixture.State.Destination,current.Fresh(),entry,variant,fixture.Catalog,new[]{contexts[1].Fresh()},true);
            check(!cloneOnly.CanUpdate&&cloneOnly.Recognized&&cloneOnly.Baseline==null,
                "Recognition-only copies can identify an installation without acquiring write ownership.");
            var missing=contexts.Select(context=>context.Fresh()).ToArray();missing[0].Proof=null;
            check(!GeneralUpdateOwnership.Resolve(fixture.State.Destination,current.Fresh(),entry,variant,fixture.Catalog,missing,true).CanUpdate,
                "A foreign project without its caller-supplied index/project proof cannot authorize replacement.");
            var changed=contexts.Select(context=>context.Fresh()).ToArray();changed[0].State=GeneralPresetProfiles.Copy(changed[0].State);
            changed[0].State.List.Presets.Clear();changed[0].State.List.Mods.Single(mod=>mod.Id=="original-owner").Variants.Single().Sources[0]=
                "https://www.nexusmods.com/stardewvalley/mods/99?tab=files&file_id=10";
            check(!GeneralUpdateOwnership.Resolve(fixture.State.Destination,current.Fresh(),entry,variant,fixture.Catalog,changed,true).CanUpdate,
                "An old receipt whose source no longer matches its registered project is not resurrected as authority.");
            File.WriteAllText(Safe.In(fixture.State.Destination,"mods/first/main.txt"),"manual edit");
            check(!GeneralUpdateOwnership.Resolve(fixture.State.Destination,current.Fresh(),entry,variant,fixture.Catalog,contexts.Select(context=>context.Fresh()),true).CanUpdate,
                "Fresh revalidation detects a manual edit after the first ownership audit.");
        }
        Safe.WriteJson(Safe.In(root,"result.json"),new{Passed=true,Checks=checks,RealGameTouched=false,NetworkUsed=false});
    }
}
