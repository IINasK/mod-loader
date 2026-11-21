using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

// A context is one immutable audit session. Revalidation constructs fresh
// contexts and uses a fresh hash reader; the UI never resolves ownership while painting.
internal sealed class GeneralUpdateOwnershipContext {
    internal string Data,Name,Proof;
    internal GeneralModState State;
    internal List<GeneralModInstaller.Record> Generic;
    internal List<InstalledSnapshotInstaller.Receipt> Snapshots;
    internal List<OnlineReceipt> NativeReceipts;
    internal HashSet<string> Identities;
    internal readonly Dictionary<string,string> GenericFingerprints=new Dictionary<string,string>(StringComparer.Ordinal);
    internal readonly Dictionary<string,string> SnapshotFingerprints=new Dictionary<string,string>(StringComparer.Ordinal);
    internal string StateProof;
    internal GeneralUpdateOwnershipContext Fresh(){return new GeneralUpdateOwnershipContext{Data=Data,Name=Name,Proof=Proof,State=State};}
}

internal sealed class GeneralUpdateOwnershipPlan {
    internal GeneralModInstaller.Record Baseline;
    internal string Fingerprint,Reason,NativeFixed,NativeImport;
    internal readonly List<string> NativeReceiptIds=new List<string>();
    internal readonly List<GeneralModInstaller.RelatedReceipt> RelatedReceipts=new List<GeneralModInstaller.RelatedReceipt>();
    internal readonly List<string> SharedProjects=new List<string>();
    internal readonly HashSet<string> ProtectedPaths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    internal bool CanUpdate,ForeignAuthority,Recognized;
}

internal static class GeneralUpdateOwnership {
    static readonly StringComparer Paths=StringComparer.OrdinalIgnoreCase;
    sealed class Observation {
        internal GeneralUpdateOwnershipContext Context;
        internal string Owner,Identity,Payload,Proof,NativeFixed,NativeImport;
        internal List<string> NativeReceiptIds=new List<string>();
        internal GeneralModInstaller.Record Record;
        internal GeneralModInstaller.RelatedReceipt Related;
        internal bool Owned,Present,ForeignBackup,Checked;
        internal bool Applied=true;
    }

    internal static string ReceiptFingerprint(object receipt,bool canonical=true){
        string json=canonical?ReceiptJson(receipt):Regex.Replace(Safe.Json.Serialize(receipt),"(?:\"CanonicalFingerprint\":false,|,\"CanonicalFingerprint\":false)","");
        return InstalledSnapshot.Hash(Encoding.UTF8.GetBytes(json));
    }
    // Reflection and dictionary enumeration order are not receipt identity.
    // Sort object keys recursively, retaining every value and array position.
    internal static string ReceiptJson(object receipt){return OrderedJson(Safe.Json.DeserializeObject(Safe.Json.Serialize(receipt)));}
    static string OrderedJson(object value){
        var map=value as IDictionary<string,object>;
        if(map!=null)return "{"+String.Join(",",map.Keys.OrderBy(key=>key,StringComparer.Ordinal).Select(key=>Safe.Json.Serialize(key)+":"+OrderedJson(map[key])))+"}";
        var array=value as object[];
        return array==null?Safe.Json.Serialize(value):"["+String.Join(",",array.Select(OrderedJson))+"]";
    }
    static string Canonical(string path){string full=Path.GetFullPath(path).TrimEnd('\\');Safe.NoLinks(full);return full;}
    static string Source(string source){var nexus=GeneralNexusLink.Parse(source);return nexus==null?source:nexus.Url;}
    internal static bool SameSourcePayload(GeneralModVariant left,GeneralModVariant right){
        return left!=null&&right!=null&&left.Sources.Select(Source).SequenceEqual(right.Sources.Select(Source),StringComparer.Ordinal)&&
            GeneralInstallRoutes.Same(left.InstallRoutes,right.InstallRoutes);
    }
    static string Identity(IEnumerable<string> sources,List<GeneralInstallRoute> routes){
        GeneralInstallRoutes.Validate(routes);
        return ReceiptFingerprint(new{Sources=sources.Select(Source).ToArray(),Routes=routes.OrderBy(route=>route.Folder,Paths)
            .Select(route=>new{Folder=route.Folder.ToUpperInvariant(),Destination=route.Destination.ToUpperInvariant()}).ToArray()});
    }
    static string Payload(IEnumerable<GeneralModInstaller.RecordFile> files){
        return ReceiptFingerprint(files.OrderBy(file=>file.Path,Paths).Select(file=>new{Path=file.Path.Replace('\\','/').ToUpperInvariant(),file.Size,file.Sha256}).ToArray());
    }
    static bool SourceSlot(string key,out string owner,out string source){
        owner=source=null;if(key==null)return false;var parts=key.Split(new[]{'/'},4);
        if(parts.Length!=4||parts[2]!="0"||!GeneralModDownload.IsWebSource(parts[3]))return false;
        owner=parts[0]+"/"+parts[1];source=parts[3];return true;
    }
    static List<GeneralModEntry> Definitions(GeneralModState state){
        return state.List.Mods.Concat(state.List.Presets.SelectMany(preset=>preset.Configurations.Values)).ToList();
    }
    static bool Matches(string root,IEnumerable<GeneralModInstaller.RecordFile> files,Func<string,string> hash){
        var paths=new HashSet<string>(Paths);bool any=false;
        foreach(var file in files){
            any=true;if(file==null||!FixedData.HashValid(file.Sha256)||file.Size<0||!paths.Add(file.Path))return false;
            string path=GeneralInstallRoutes.Resolve(root,file.Path,false);Safe.NoLinks(path);
            if(!File.Exists(path)||new FileInfo(path).Length!=file.Size||hash(path)!=file.Sha256)return false;
        }
        return any;
    }

    internal static GeneralUpdateOwnershipPlan Resolve(string root,GeneralUpdateOwnershipContext currentContext,GeneralModEntry entry,
        GeneralModVariant variant,FixedCatalog catalog,IEnumerable<GeneralUpdateOwnershipContext> contexts,bool fixture=false,Func<string,string> hash=null) {
        var plan=new GeneralUpdateOwnershipPlan();
        try {
            root=Canonical(root);string currentData=Canonical(currentContext.Data),owner=InstalledSnapshot.Owner(entry,variant);
            if(currentContext.State==null||!Paths.Equals(Canonical(currentContext.State.Destination),root))
                return Denied(plan,"O projeto não aponta para a instalação que está sendo conferida.");
            if(variant.Sources.Count!=1||variant.InstallRoutes.Any(route=>!route.Destination.StartsWith("%GAME%",StringComparison.Ordinal)))
                return Denied(plan,"O pacote usa mais de uma fonte ou destinos pessoais e precisa de revisão.");
            string identity=Identity(variant.Sources,variant.InstallRoutes);
            var declared=new[]{currentContext}.Concat(contexts??Enumerable.Empty<GeneralUpdateOwnershipContext>()).Where(context=>context!=null).ToArray();
            var supplied=declared.Where(context=>context.State!=null&&context.State.List!=null)
                .Where(context=>!String.IsNullOrWhiteSpace(context.State.Destination)&&Paths.Equals(Canonical(context.State.Destination),root))
                .GroupBy(context=>Canonical(context.Data),Paths).Select(group=>group.First()).Take(1001).ToArray();
            if(supplied.Length>1000)return Denied(plan,"Há projetos demais para conferir esta instalação.");
            var reads=new Dictionary<string,string>(Paths);
            Func<string,string> read=path=>{string value;path=Path.GetFullPath(path);if(!reads.TryGetValue(path,out value))reads.Add(path,value=(hash??(Func<string,string>)Safe.Hash)(path));return value;};
            var observations=new List<Observation>();var evidence=new List<string>();bool unreadable=declared.Any(context=>context.State==null||context.State.List==null);
            foreach(var context in supplied) {
                string data=Canonical(context.Data);
                lock(context) {
                    if(context.StateProof==null)context.StateProof=ReceiptFingerprint(new{context.State.Destination,context.State.List});
                    if(context.Identities==null)context.Identities=new HashSet<string>(Definitions(context.State).SelectMany(mod=>mod.Variants)
                        .Select(option=>Identity(option.Sources,option.InstallRoutes)),StringComparer.Ordinal);
                    evidence.Add(data+"\n"+context.Proof+"\n"+context.StateProof);
                    if(String.IsNullOrWhiteSpace(context.Proof)){unreadable=true;continue;}
                    try {
                        var generic=new GeneralModInstaller(root,Path.Combine(data,"Instalacoes"));
                        if(generic.RecoveryPending)unreadable=true;
                        if(context.Generic==null)context.Generic=generic.ReadUpdateOwnershipRecords();
                        if(context.Generic.Any(record=>record.Upgrade!=null&&!record.Upgrade.Reconciled))unreadable=true;
                        foreach(var record in context.Generic) {
                            string receiptOwner,source;bool slot=SourceSlot(record.Key,out receiptOwner,out source);
                            string fingerprint;if(!context.GenericFingerprints.TryGetValue(record.Key,out fingerprint))
                                {
                                    if(ReceiptFingerprint(generic.ReadPackageRecord(record.Key))!=ReceiptFingerprint(record))throw new IOException("O recibo mudou durante a conferência.");
                                    fingerprint=generic.UpgradeReceiptFingerprint(record.Key);
                                    if(ReceiptFingerprint(generic.ReadPackageRecord(record.Key))!=ReceiptFingerprint(record))throw new IOException("O recibo mudou durante a conferência.");
                                    context.GenericFingerprints[record.Key]=fingerprint;
                                }
                            string sourceIdentity=slot?Identity(new[]{source},record.InstallRoutes):null;
                            bool declaredIdentity=sourceIdentity!=null&&context.Identities.Contains(sourceIdentity);
                            var observation=new Observation{Context=context,Owner=receiptOwner,Record=record,Proof=fingerprint,
                                Identity=declaredIdentity?sourceIdentity:null,Payload=Payload(record.Files),
                                Owned=record.Files.Count>0&&record.Files.All(file=>file.Owned&&
                                    (file.Before==null||Paths.Equals(data,currentData))),ForeignBackup=!Paths.Equals(data,currentData)&&record.Files.Any(file=>file.Before!=null)};
                            if(slot)observation.Related=new GeneralModInstaller.RelatedReceipt{Data=data,Key=record.Key,Owner=receiptOwner,Fingerprint=fingerprint,CanonicalFingerprint=true};
                            observations.Add(observation);
                        }
                        var snapshots=new InstalledSnapshotInstaller(root,data,catalog,fixture);
                        if(context.Snapshots==null)context.Snapshots=snapshots.ReadUpdateOwnershipReceipts();
                        if(context.Snapshots.Any(receipt=>receipt.Status=="preparing"||receipt.Status=="installing"))unreadable=true;
                        foreach(var receipt in context.Snapshots.Where(receipt=>receipt.Status=="installed"&&receipt.ActiveOwners.Count>0)) {
                            string fingerprint;if(!context.SnapshotFingerprints.TryGetValue(receipt.Id,out fingerprint))
                                {
                                    fingerprint=snapshots.UpgradeReceiptFingerprint(receipt.Id);
                                    if(fingerprint!=ReceiptFingerprint(receipt))throw new IOException("O recibo mudou durante a conferência.");
                                    context.SnapshotFingerprints[receipt.Id]=fingerprint;
                                }
                            foreach(var mod in receipt.Manifest.List.Mods)foreach(var option in mod.Variants) {
                                string receiptOwner=InstalledSnapshot.Owner(mod,option);
                                if(!receipt.ActiveOwners.Contains(receiptOwner))continue;
                                var files=receipt.Manifest.Files.Where(file=>file.Kind=="file"&&file.Owners.Contains(receiptOwner)).Select(file=>{
                                    var change=receipt.Files.Single(item=>Paths.Equals(item.Path,file.Path));
                                    return new GeneralModInstaller.RecordFile{Path=file.Path,ArchivePath=file.Path,Size=file.Size,Sha256=file.Sha256,
                                        Owned=change.Owned&&change.Applied&&!change.Reverted&&change.Before==null};
                                }).ToList();
                                var record=new GeneralModInstaller.Record{Key=receiptOwner,InstallRoutes=option.InstallRoutes.ToList(),Files=files};
                                bool applied=receipt.Files.Where(change=>files.Any(file=>Paths.Equals(file.Path,change.Path))).All(change=>change.Applied&&!change.Reverted);
                                string sourceIdentity=Identity(option.Sources,option.InstallRoutes);
                                bool declaredIdentity=context.Identities.Contains(sourceIdentity);
                                observations.Add(new Observation{Context=context,Owner=receiptOwner,Record=record,Proof=fingerprint,
                                    Identity=declaredIdentity?sourceIdentity:null,Payload=Payload(files),Applied=applied,
                                    Owned=files.Count>0&&files.All(file=>file.Owned),
                                    ForeignBackup=receipt.Files.Where(change=>files.Any(file=>Paths.Equals(file.Path,change.Path))).Any(change=>change.Before!=null),
                                    Related=new GeneralModInstaller.RelatedReceipt{Data=data,SnapshotId=receipt.Id,Owner=receiptOwner,Fingerprint=fingerprint,CanonicalFingerprint=true}});
                            }
                        }
                    }catch(IOException){unreadable=true;}
                     catch(UnauthorizedAccessException){unreadable=true;}
                }
            }
            bool cyberpunk=File.Exists(Safe.In(root,"bin/x64/Cyberpunk2077.exe"));
            if(cyberpunk)ReadNative(root,currentContext,supplied,catalog,fixture,observations);
            Func<Observation,bool> present=observation=>{
                if(!observation.Checked){observation.Present=observation.Applied&&Matches(root,observation.Record.Files,read);observation.Checked=true;}
                return observation.Present;
            };
            // Unrelated receipt payloads provide path protection, never a reason
            // to hash the rest of the game's mod library for this candidate.
            var matching=observations.Where(observation=>observation.Identity==identity).Where(present).ToArray();
            plan.Recognized=matching.Length>0;
            var candidates=matching.Where(observation=>observation.Owned).ToArray();
            var payloads=candidates.Select(observation=>observation.Payload).Distinct(StringComparer.Ordinal).ToArray();
            if(payloads.Length!=1)return Complete(Denied(plan,payloads.Length==0?"Não foi encontrado um recibo completo com autoria para esta versão.":"Há recibos divergentes para esta versão. Revise a instalação."),root,owner,identity,
                evidence.Concat(observations.Select(observation=>observation.Proof+"\n"+observation.Present)).ToList());
            var selected=candidates.OrderBy(observation=>Paths.Equals(observation.Context.Data,currentData)?0:1)
                .ThenBy(observation=>observation.Owner==owner?0:1).First();
            plan.Baseline=Safe.Json.Deserialize<GeneralModInstaller.Record>(Safe.Json.Serialize(selected.Record));
            plan.ForeignAuthority=!Paths.Equals(Canonical(selected.Context.Data),currentData)||selected.Owner!=owner;
            plan.NativeFixed=selected.NativeFixed;plan.NativeImport=selected.NativeImport;plan.NativeReceiptIds.AddRange(selected.NativeReceiptIds);
            var affected=new HashSet<string>(selected.Record.Files.Select(file=>file.Path),Paths);
            foreach(var observation in observations.Where(observation=>observation.Identity!=identity||observation.Payload!=selected.Payload||!observation.Present||observation.ForeignBackup))
                foreach(var file in observation.Record.Files)plan.ProtectedPaths.Add(file.Path);
            foreach(var file in catalog.Packages.Where(mod=>mod.Immutable).SelectMany(mod=>mod.Files))plan.ProtectedPaths.Add(file.Path);
            foreach(var observation in observations) {
                evidence.Add(Canonical(observation.Context.Data)+"\n"+observation.Owner+"\n"+observation.Identity+"\n"+observation.Payload+"\n"+observation.Proof+"\n"+observation.Present);
                if(!observation.Record.Files.Any(file=>affected.Contains(file.Path)))continue;
                if(observation.ForeignBackup)return Complete(Denied(plan,"Um registro compartilhado restaura arquivos anteriores. Essa autoria precisa de revisão antes da atualização."),root,owner,identity,evidence);
                if(observation.Identity!=identity||observation.Payload!=selected.Payload||!observation.Present)
                    return Complete(Denied(plan,"Os arquivos também pertencem a outro mod, versão ou destino. Revise os vínculos antes de atualizar."),root,owner,identity,evidence);
                if(observation.Related!=null&&!plan.RelatedReceipts.Any(related=>Paths.Equals(related.Data,observation.Related.Data)&&related.Key==observation.Related.Key&&
                    related.SnapshotId==observation.Related.SnapshotId&&related.Owner==observation.Related.Owner))plan.RelatedReceipts.Add(observation.Related);
                if(!Paths.Equals(Canonical(observation.Context.Data),currentData))AddProject(plan,observation.Context.Name);
            }
            foreach(var context in supplied.Where(context=>!Paths.Equals(Canonical(context.Data),currentData)))
                if(context.Identities!=null&&context.Identities.Contains(identity))AddProject(plan,context.Name);
            if(selected.Record.Files.Any(file=>GeneralInstallRoutes.External(file.Path))||catalog.Packages.Where(mod=>mod.Immutable).SelectMany(mod=>mod.Files)
                .Any(file=>affected.Contains(file.Path)))return Complete(Denied(plan,"A instalação inclui arquivos de base protegidos ou destinos pessoais."),root,owner,identity,evidence);
            if(unreadable)return Complete(Denied(plan,"Não foi possível conferir todos os recibos dos projetos que usam esta instalação."),root,owner,identity,evidence);
            evidence.AddRange(reads.OrderBy(pair=>pair.Key,Paths).Select(pair=>pair.Key+"\n"+pair.Value));
            plan.CanUpdate=true;return Complete(plan,root,owner,identity,evidence);
        }catch(IOException){return Denied(plan,"Não foi possível comprovar a autoria desta instalação. Os arquivos foram preservados.");}
         catch(UnauthorizedAccessException){return Denied(plan,"Não foi possível ler os registros desta instalação. Os arquivos foram preservados.");}
    }

    static void AddProject(GeneralUpdateOwnershipPlan plan,string name){
        name=String.IsNullOrWhiteSpace(name)?"Outro projeto":name;
        if(!plan.SharedProjects.Contains(name,StringComparer.Ordinal))plan.SharedProjects.Add(name);
    }
    static GeneralUpdateOwnershipPlan Denied(GeneralUpdateOwnershipPlan plan,string reason){plan.CanUpdate=false;plan.Reason=reason;return plan;}
    static GeneralUpdateOwnershipPlan Complete(GeneralUpdateOwnershipPlan plan,string root,string owner,string identity,List<string> evidence){
        plan.Fingerprint=ReceiptFingerprint(new{Root=root.ToUpperInvariant(),Owner=owner,Identity=identity,Proof=evidence.OrderBy(value=>value,StringComparer.Ordinal).ToArray(),
            plan.CanUpdate,plan.Recognized,plan.Reason,Shared=plan.SharedProjects.OrderBy(value=>value,StringComparer.Ordinal).ToArray()});return plan;
    }

    static void ReadNative(string root,GeneralUpdateOwnershipContext current,IEnumerable<GeneralUpdateOwnershipContext> contexts,FixedCatalog catalog,
        bool fixture,List<Observation> observations) {
        List<OnlineReceipt> receipts;
        lock(current){if(current.NativeReceipts==null)current.NativeReceipts=new FixedInstaller(catalog,root,fixture).ReadUpdateOwnershipNativeReceipts();receipts=current.NativeReceipts;}
        foreach(var recipe in catalog.Packages) {
            var relevant=receipts.Where(receipt=>receipt.Operation!=LocalModPackage.ReceiptOperation&&receipt.Mods.Contains(recipe.Id)).ToArray();
            if(relevant.Length==0)continue;
            var files=recipe.Files.Where(file=>!file.UserData).Select(file=>new GeneralModInstaller.RecordFile{Path=file.Path,ArchivePath=file.Entry,Sha256=file.Sha256,Size=file.Size,
                Owned=relevant.Any(receipt=>receipt.Files.Any(change=>Paths.Equals(change.Path,file.Path)&&change.After==file.Sha256&&change.Applied&&!change.Reverted&&!change.UndoStarted&&change.Before==null))}).ToList();
            if(files.Count==0||!relevant.Any(receipt=>receipt.Files.Any(change=>change.Applied&&!change.Reverted&&!change.UndoStarted)))continue;
            var record=new GeneralModInstaller.Record{Key=recipe.Id,Files=files};
            observations.Add(new Observation{Context=current,Owner=recipe.Id,Record=record,Identity=Identity(recipe.Assets.Select(asset=>asset.Url),new List<GeneralInstallRoute>()),
                Payload=Payload(files),Owned=!recipe.Immutable&&files.All(file=>file.Owned),NativeFixed=recipe.Id,
                NativeReceiptIds=relevant.Select(receipt=>receipt.Id).ToList(),Proof=ReceiptFingerprint(relevant)});
        }
        foreach(var context in contexts)foreach(var mod in Definitions(context.State))foreach(var option in mod.Variants) {
            if(option.Sources.Count!=1)continue;string id;
            if(!P2PPack.TrySlot(context.State.CyberpunkImports,mod,option,0,out id))continue;
            var receipt=receipts.Where(item=>item.Operation==LocalModPackage.ReceiptOperation&&item.Mods.Count==1&&item.Mods[0]==id)
                .OrderByDescending(item=>item.CreatedUtc).FirstOrDefault();
            if(receipt==null)continue;
            var files=receipt.Files.Where(file=>file.Applied&&!file.Reverted&&!file.UndoStarted).Select(file=>new GeneralModInstaller.RecordFile{
                Path=file.Path,ArchivePath=file.Path,Sha256=file.After,Size=File.Exists(Safe.In(root,file.Path))?new FileInfo(Safe.In(root,file.Path)).Length:0,Owned=file.Before==null}).ToList();
            observations.Add(new Observation{Context=context,Owner=InstalledSnapshot.Owner(mod,option),Record=new GeneralModInstaller.Record{Key=id,Files=files,InstallRoutes=option.InstallRoutes.ToList()},
                Identity=Identity(option.Sources,option.InstallRoutes),Payload=Payload(files),Owned=files.Count>0&&files.All(file=>file.Owned),
                NativeImport=id,NativeReceiptIds=new List<string>{receipt.Id},Proof=ReceiptFingerprint(receipt)});
        }
    }
}

public sealed partial class GeneralModInstaller {
    internal List<Record> ReadUpdateOwnershipRecords(){
        Safe.NoLinks(records);if(!Directory.Exists(records))return new List<Record>();
        return Directory.EnumerateFiles(records,"*.json").Select(path=>ReadRecord(path,true)).Where(record=>record!=null)
            .Select(record=>Safe.Json.Deserialize<Record>(Safe.Json.Serialize(record))).ToList();
    }
}

public sealed partial class InstalledSnapshotInstaller {
    internal List<Receipt> ReadUpdateOwnershipReceipts(){
        return AllReceipts(true).Select(receipt=>Safe.Json.Deserialize<Receipt>(Safe.Json.Serialize(receipt))).ToList();
    }
}

public sealed partial class FixedInstaller {
    internal List<OnlineReceipt> ReadUpdateOwnershipNativeReceipts(){
        return AllReceiptPaths().Select(path=>ReadReceiptCore(path,true)).Where(receipt=>receipt.Status=="instalado")
            .Select(receipt=>Safe.Json.Deserialize<OnlineReceipt>(Safe.Json.Serialize(receipt))).ToList();
    }
}
