using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

public sealed partial class GeneralModInstaller {
    internal sealed class CommittedUpgrade {
        public string Owner, PreviousSource, Source, Version, PackageSha256;
        public string PreviousFixed {get;set;}
        public bool PredecessorsRetired, Reconciled;
    }

    sealed class UpgradeHeader {
        public string TransactionId {get;set;}
        public CommittedUpgrade Upgrade {get;set;}
    }

    // This release can recognize committed receipts written by the Dev installer.
    // The receipt reader leaves ownership, project metadata, and files untouched.
    internal IEnumerable<Tuple<Record,CommittedUpgrade>> ReadCommittedUpgrades() {
        Safe.NoLinks(records);
        if(!Directory.Exists(records)||RecoveryPending)yield break;
        foreach(string path in Directory.EnumerateFiles(records,"*.json")){
            Tuple<Record,CommittedUpgrade> result=null;
            try{
                Safe.NoLinks(path);
                string json=GeneralProjectStorage.ReadText(path,16000000);
                var header=Safe.Json.Deserialize<UpgradeHeader>(json);
                var upgrade=header==null?null:header.Upgrade;
                Guid transactionId;
                if(upgrade==null||!upgrade.Reconciled||!upgrade.PredecessorsRetired||
                    !Guid.TryParseExact(header.TransactionId,"N",out transactionId)||
                    !Regex.IsMatch(upgrade.Owner??"",@"\A[A-Za-z0-9_-]{1,120}/[A-Za-z0-9_-]{1,120}\z")||
                    !GeneralModDownload.IsWebSource(upgrade.PreviousSource)||!GeneralModDownload.IsWebSource(upgrade.Source)||
                    upgrade.Version==null||upgrade.Version.Length>120||upgrade.Version.Any(Char.IsControl)||
                    !FixedData.HashValid(upgrade.PackageSha256))continue;
                var receipt=ReadRecord(path,true,json);
                if(receipt!=null&&receipt.Key==upgrade.Owner+"/0/"+upgrade.Source&&
                    receipt.SourceSha256==upgrade.PackageSha256)result=Tuple.Create(receipt,upgrade);
            }catch(IOException ex){OnlineDiagnostics.Record("scan.shared.receipt",null,ex.GetType().Name);}
             catch(UnauthorizedAccessException ex){OnlineDiagnostics.Record("scan.shared.receipt",null,ex.GetType().Name);}
             catch(ArgumentException ex){OnlineDiagnostics.Record("scan.shared.receipt",null,ex.GetType().Name);}
             catch(InvalidOperationException ex){OnlineDiagnostics.Record("scan.shared.receipt",null,ex.GetType().Name);}
            if(result!=null)yield return result;
        }
    }

    internal bool MatchesCommittedUpgrade(Record receipt,Func<string,string> hash,List<GeneralInstallRoute> routes){
        return !RecoveryPending&&CheckRecord(receipt,null,hash,routes)==Installed;
    }
}

internal sealed partial class GeneralModWindow {
    sealed class SharedInstalledUpdate {
        internal GeneralModInstaller Installer;
        internal GeneralModInstaller.Record Receipt;
        internal GeneralModInstaller.CommittedUpgrade Upgrade;
    }

    readonly Dictionary<string,GeneralModInstaller.CommittedUpgrade> sharedInstalledVersions=
        new Dictionary<string,GeneralModInstaller.CommittedUpgrade>(StringComparer.Ordinal);

    IEnumerable<GeneralProjectStore> SharedInstallationStores(){
        var roots=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(projectStore!=null&&roots.Add(projectStore.Root.TrimEnd('\\')))yield return projectStore;
        if(testMode)yield break;
        string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach(string relative in new[]{"ModLoaderDev/Config","ModLoader/Config","ModLoaderGeral"}){
            string path=Path.GetFullPath(Path.Combine(local,relative));
            if(!roots.Add(path.TrimEnd('\\')))continue;
            GeneralProjectStore store=null;
            try{
                Safe.NoLinks(path);
                if(File.Exists(Path.Combine(path,"projects.json"))||File.Exists(Path.Combine(path,"lista.json")))
                    store=new GeneralProjectStore(path);
            }catch(IOException ex){OnlineDiagnostics.Record("scan.shared.store",null,ex.GetType().Name);}
             catch(UnauthorizedAccessException ex){OnlineDiagnostics.Record("scan.shared.store",null,ex.GetType().Name);}
            if(store!=null)yield return store;
        }
    }

    ILookup<string,SharedInstalledUpdate> ReadSharedInstalledUpdates(string root){
        var updates=new List<SharedInstalledUpdate>();
        string currentData=Path.GetFullPath(data).TrimEnd('\\');
        string destination=Path.GetFullPath(root).TrimEnd('\\');
        foreach(var store in SharedInstallationStores()){
            GeneralProjectIndex index;
            try{index=store.Read(true);}
            catch(IOException ex){OnlineDiagnostics.Record("scan.shared.store",null,ex.GetType().Name);continue;}
            catch(UnauthorizedAccessException ex){OnlineDiagnostics.Record("scan.shared.store",null,ex.GetType().Name);continue;}
            foreach(var peer in index.Projects){
                try{
                    string peerData=store.DataPath(peer);
                    if(String.Equals(peerData.TrimEnd('\\'),currentData,StringComparison.OrdinalIgnoreCase))continue;
                    var tracked=new GeneralModLibrary(Path.Combine(peerData,"lista.json")).LoadForInspection(true);
                    if(tracked==null||String.IsNullOrWhiteSpace(tracked.Destination)||
                        !String.Equals(Path.GetFullPath(tracked.Destination).TrimEnd('\\'),destination,StringComparison.OrdinalIgnoreCase))continue;
                    var mods=tracked.List.Mods.ToDictionary(mod=>mod.Id,StringComparer.Ordinal);
                    var engine=new GeneralModInstaller(root,Path.Combine(peerData,"Instalacoes"));
                    foreach(var update in engine.ReadCommittedUpgrades()){
                        var owner=update.Item2.Owner.Split('/');
                        GeneralModEntry mod;
                        if(!mods.TryGetValue(owner[0],out mod))continue;
                        if(!mod.Variants.Any(variant=>variant.Id==owner[1]&&variant.Sources.Count==1&&
                            variant.Sources[0]==update.Item2.Source&&GeneralInstallRoutes.Same(variant.InstallRoutes,update.Item1.InstallRoutes)))continue;
                        updates.Add(new SharedInstalledUpdate{Installer=engine,Receipt=update.Item1,Upgrade=update.Item2});
                    }
                }catch(IOException ex){OnlineDiagnostics.Record("scan.shared.project",null,ex.GetType().Name);}
                 catch(UnauthorizedAccessException ex){OnlineDiagnostics.Record("scan.shared.project",null,ex.GetType().Name);}
                 catch(ArgumentException ex){OnlineDiagnostics.Record("scan.shared.project",null,ex.GetType().Name);}
            }
        }
        return updates.ToLookup(update=>update.Upgrade.Owner,StringComparer.Ordinal);
    }

    string ScanInstalledStatus(Work work,string root,ScanReads reads){
        string status;
        try{status=Status(work,root,reads,false);}
        catch(Exception ex){work.ScanError=ex.Message;status="Não verificável";}
        if(!reads.Exists||VersionInstalled(status)||status==GeneralStatusPresentation.Disabled||
            status=="Requer Phantom Liberty"||status=="Requer Cyberpunk 2077"||work.Variant.Sources.Count!=1||
            work.Variant.NaturalOption!=null||work.Variant.CyberpunkVariant!=null||
            work.Variant.CyberpunkId!=null&&Recipe(work).Immutable)return status;
        foreach(var match in reads.SharedUpdates.Value[work.Key]){
            var upgrade=match.Upgrade;
            string source=work.Variant.Sources[0];
            if(source!=upgrade.Source&&source!=upgrade.PreviousSource||
                work.Variant.CyberpunkId!=null&&work.Variant.CyberpunkId!=upgrade.PreviousFixed)continue;
            try{
                if(!match.Installer.MatchesCommittedUpgrade(match.Receipt,reads.Hash,work.Variant.InstallRoutes))continue;
                work.SharedInstallation=upgrade;
                work.ScanError=null;
                return GeneralModInstaller.Installed;
            }catch(IOException ex){OnlineDiagnostics.Record("scan.shared.verify",work.Entry.Id,ex.GetType().Name);}
             catch(UnauthorizedAccessException ex){OnlineDiagnostics.Record("scan.shared.verify",work.Entry.Id,ex.GetType().Name);}
        }
        return status;
    }
}
