using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public static partial class InstalledSnapshot {
    static List<GeneralUpdateOwnershipContext> RegisteredSnapshotProjects(string root,string data){
        var directory=new DirectoryInfo(Path.GetFullPath(data));
        string store=directory.Parent!=null&&directory.Parent.Parent!=null&&directory.Parent.Name.Equals("Projetos",StringComparison.OrdinalIgnoreCase)
            ?directory.Parent.Parent.FullName:directory.FullName;
        bool systemStore=GeneralModWindow.RegisteredStores(null,true).Contains(store,StringComparer.OrdinalIgnoreCase);
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var contexts=new List<GeneralUpdateOwnershipContext>();
        GeneralModWindow.ReadRegisteredUpdateContexts(root,GeneralModWindow.RegisteredStores(store,systemStore),seen,peer=>{
            if(peer!=null&&peer.State!=null&&seen.Add(Path.GetFullPath(peer.Data).TrimEnd('\\')))contexts.Add(peer);
        });
        return contexts;
    }

    static List<InstalledSnapshotFile> SharedSnapshotFiles(IEnumerable<GeneralUpdateOwnershipContext> projects,
        string data,string root,GeneralModEntry mod,GeneralModVariant variant,FixedCatalog catalog,bool includeConfigs,Action checkpoint){
        if(variant.CyberpunkId!=null||!variant.Sources.All(GeneralModDownload.IsWebSource))return null;
        string owner=Owner(mod,variant);
        foreach(var peer in projects){
            string receipt;
            if(String.Equals(Path.GetFullPath(data),peer.Data,StringComparison.OrdinalIgnoreCase)||
                !peer.State.SnapshotReceipts.TryGetValue(owner,out receipt))continue;
            var tracked=peer.State.List.Mods.FirstOrDefault(item=>item.Id==mod.Id);
            if(tracked==null||!tracked.Variants.Any(option=>option.Id==variant.Id&&GeneralUpdateOwnership.SameSourcePayload(option,variant)))continue;
            var engine=new InstalledSnapshotInstaller(root,peer.Data,catalog);
            var files=engine.ReadMatchingInstalledFiles(receipt,mod,variant,includeConfigs,checkpoint:checkpoint);
            if(files!=null)return files;
        }
        return null;
    }

    static GeneralModWindow.SharedInstalledUpdate VerifiedSnapshotUpdate(GeneralModEntry entry,GeneralModVariant variant,
        Lazy<ILookup<string,GeneralModWindow.SharedInstalledUpdate>> index,FixedCatalog catalog,Action checkpoint){
        if(variant.Sources.Count!=1||variant.NaturalOption!=null||variant.CyberpunkVariant!=null||
            variant.CyberpunkId!=null&&FixedRecipe(catalog,variant).Immutable)return null;
        GeneralModWindow.SharedInstalledUpdate selected=null;
        foreach(var match in index.Value[Owner(entry,variant)]){
            var record=match.Receipt;
            if(!match.Matches(variant)||!match.IsIntact(path=>SnapshotVerification.Hash(path,checkpoint)))continue;
            if(selected!=null&&selected.Receipt.SourceSha256!=record.SourceSha256)
                throw new IOException("Mais de um pacote atualizado corresponde à instalação de "+entry.Name+".");
            selected=match;
        }
        return selected;
    }

    static void UseVerifiedUpdate(GeneralModVariant target,GeneralModWindow.SharedInstalledUpdate match,FixedCatalog catalog){
        var info=match.Receipt.Upgrade;
        if(target.Id!=match.Variant.Id||target.NaturalOption!=null||target.CyberpunkVariant!=null||!match.Matches(target))return;
        target.CyberpunkId=null;
        target.Sources=new List<string>{info.Source};
        GeneralSourceInfo sourceInfo;
        if(!match.Variant.SourceInfo.TryGetValue(info.Source,out sourceInfo))sourceInfo=new GeneralSourceInfo{Name=target.Name};
        sourceInfo=Clone(sourceInfo);sourceInfo.Version=info.Version;
        target.SourceInfo=new Dictionary<string,GeneralSourceInfo>{{info.Source,sourceInfo}};
        var recipe=catalog.Packages.FirstOrDefault(item=>item.Id==info.PreviousFixed);
        if(recipe!=null)
            target.RequiredMods=target.RequiredMods.Concat(recipe.Dependencies).Distinct(StringComparer.Ordinal).ToList();
    }

    // SHA-256 dos arquivos das versões 5.0.0 e dos refits fixados no catálogo Hyst.
    const string HystScript="r6/scripts/VTK_Hyst_CC_Edit/VTK_Hyst_CC_Edit.reds";
    const string HystScriptHash="659169a46016336a92ab7bdb93540e30f3d8837fc1a1347351bb8f12c61403f4";
    const string HystArchiveRoot="archive/pc/mod/";
    static readonly string[][] NaturalHystFiles={
        new[]{"EBBRB.xl","3058cacde4fda371dad232352b2ba9b401f8806bab1c4eadd069d0dff1a862a8"},
        new[]{"05_Hyst_Refit_ForcedBigBreastsBooty.archive","b05cd56a5cad772b52a12c3dcd652612753225cd006e3c85de29113da8c1932d"},
        new[]{"Y_BOOBA_BOOTYFIT_Sneaky_Hyst_VanillaClothes.archive","9d585ec4dcc4b9c7c5e2f0eb6b78bf5601aecb92639660ca369a902db66c449c"},
        new[]{"Y_Sneaky_EBBRB_Refits_PhantomLiberty_Clothes.archive","9bd6ddbe9b3170660f551a21197fcb27ec2f30caf3c2c17d8b7cf12f8f1de872"}
    };
    static readonly string[][] PushUpHystFiles={
        new[]{"EBBPRB.xl","a98f6e880f40b195445d32c28b63debfd4c44f8f6f9f97499bc3d424b289dff1"},
        new[]{"05_Hyst_Refit_ForcedBigBoobaPUSHUP_Booty.archive","7906fd6f7e956aac8d540a07841ce25ba90fdc6d6aa3a36d135ae40cc4833201"},
        new[]{"u_Vanilla_Clothes_EBB_Push_with_BOOTY.archive","05adc609451158d55a94e392768e2761c47c7a3407dafda1d48502cdc944df12"},
        new[]{"u_Phantom_Liberty_Clothes_PUSH_with_BOOTY_refits.archive","c963ef3ba9b661d9f2993ec1c3c26fd8a67c0a8c0dad680c616f992ae3478b05"}
    };
    static bool HasManualHystFiles(string id,string root) {
        if(!GeneralModLibrary.IsHystBody(id))return false;
        var paths=id=="hyst-ebb-rb"?NaturalHystFiles:PushUpHystFiles;
        return paths.Take(2).Any(file=>File.Exists(Safe.In(root,HystArchiveRoot+file[0])));
    }

    internal static GeneralModVariant DetectManualHyst(string root,GeneralModEntry mod,GeneralModEntry knownBody,List<InstalledSnapshotFile> files=null,Func<string,string> hash=null,string bodyChoice=null) {
        try {
            if(!HasManualHystFiles(mod.Id,root))return null;
            var verified=new List<InstalledSnapshotFile>();
            var variant=ReadManualHyst(root,mod,knownBody,GameEdition.HasPL(root),verified,hash,bodyChoice);
            if(files!=null)files.AddRange(verified);
            return variant;
        }catch(IOException){return null;}
        catch(UnauthorizedAccessException){return null;}
    }

    static GeneralModVariant ReadManualHyst(string root,GeneralModEntry mod,GeneralModEntry knownBody,bool includeDlc,List<InstalledSnapshotFile> files,Func<string,string> hash=null,string bodyChoice=null) {
        bool natural=mod.Id=="hyst-ebb-rb";
        if(HasManualHystFiles(natural?"hyst-ebbp-rb":"hyst-ebb-rb",root)||
            File.Exists(Safe.In(root,HystArchiveRoot+"Vanilla_ArchiveXL_FeetComponentsFV.archive")))
            throw new IOException("O corpo Hyst está junto de uma alternativa incompatível. Desative a outra opção antes de compartilhar.");
        var paths=natural?NaturalHystFiles:PushUpHystFiles;
        bool baseRefit=File.Exists(Safe.In(root,HystArchiveRoot+paths[2][0]));
        bool dlcRefit=includeDlc&&File.Exists(Safe.In(root,HystArchiveRoot+paths[3][0]));
        string id=dlcRefit?"with-refit-dlc":baseRefit?"with-refit-base":"default";
        var variant=mod.Variants.FirstOrDefault(item=>item.Id==id);
        if(variant!=null)variant=GeneralModLibrary.BodyOption(bodyChoice,mod,variant);
        var known=knownBody==null?null:knownBody.Variants.FirstOrDefault(item=>item.Id==id);
        if(variant==null||known==null||variant.CyberpunkId!=null||!variant.Sources.SequenceEqual(known.Sources,StringComparer.Ordinal)||
            !GeneralInstallRoutes.Same(variant.InstallRoutes,known.InstallRoutes))
            throw new IOException("A variante Hyst instalada manualmente não corresponde às fontes da versão no catálogo.");
        foreach(var file in paths.Take(dlcRefit?4:baseRefit?3:2))
            files.Add(VerifiedManualHystFile(root,HystArchiveRoot+file[0],file[1],hash));
        files.Add(VerifiedManualHystFile(root,HystScript,HystScriptHash,hash));
        return variant;
    }

    static InstalledSnapshotFile VerifiedManualHystFile(string root,string relativePath,string expectedHash,Func<string,string> hash) {
        string path=Safe.In(root,relativePath);Safe.NoLinks(path);
        var info=new FileInfo(path);
        if(!info.Exists||(hash??Safe.Hash)(path)!=expectedHash)
            throw new IOException("Arquivo do Hyst ausente ou diferente da versão conhecida: "+relativePath);
        return new InstalledSnapshotFile{Path=relativePath,Sha256=expectedHash,Size=info.Length,Kind="file"};
    }
}

public sealed partial class FixedInstaller {
    internal FixedInstaller(FixedCatalog sourceCatalog,string installedRoot,bool isFixture=false) {
        game=Path.GetFullPath(installedRoot).TrimEnd('\\');Safe.NoLinks(game);
        if(game.Length<=3||game.StartsWith(@"\\"))throw new IOException("Escolha uma pasta local do jogo.");
        if(isFixture&&(!File.Exists(Path.Combine(game,".fixture-only"))||!System.Text.RegularExpressions.Regex.IsMatch(game,@"(?i)[\\/]online-testes-[a-f0-9]{32}[\\/]")))
            throw new IOException("Exceção de teste permitida apenas dentro de uma fixture isolada.");
        catalog=UserFileApprovals.ResolveCatalog(sourceCatalog,game);cache=null;saves=null;fixture=isFixture;
    }
    internal bool HasSnapshotImportedRecord(string id){
        string folder=Safe.In(Data,"importados");
        return Directory.Exists(folder)&&Directory.GetFiles(folder,"*.json").Select(ReadReceipt).Any(r=>r.Operation==LocalModPackage.ReceiptOperation&&r.Status=="instalado"&&r.Mods.Contains(id));
    }
    internal List<InstalledSnapshotFile> ExportSnapshotImportedFiles(string id,Action checkpoint=null) {
        string folder=Safe.In(Data,"importados");
        if(!Directory.Exists(folder))throw new IOException("A instalação personalizada não tem recibo nesta pasta.");
        var receipt=Directory.GetFiles(folder,"*.json").Select(ReadReceipt)
            .Where(r=>r.Operation==LocalModPackage.ReceiptOperation && r.Status=="instalado" && r.Mods.Count==1 && r.Mods[0]==id)
            .OrderByDescending(r=>r.CreatedUtc).FirstOrDefault();
        if(receipt==null || receipt.Files.Count==0)throw new IOException("A instalação personalizada não tem recibo concluído.");
        return SnapshotVerification.Map(receipt.Files,(file,poll)=>{
            string path=Safe.In(game,file.Path);
            if(!file.Applied || file.Reverted || file.UndoStarted || !File.Exists(path) || SnapshotVerification.Hash(path,poll)!=file.After)
                throw new IOException("O arquivo instalado está ausente ou alterado: "+file.Path);
            return new InstalledSnapshotFile{Path=file.Path,Sha256=file.After,Size=new FileInfo(path).Length,Kind="file"};
        },checkpoint).ToList();
    }
}

public sealed partial class GeneralModInstaller {
    internal void DuplicateRecognition(string targetData){
        Safe.NoLinks(records);if(!Directory.Exists(records))return;var target=new GeneralModInstaller(destination,targetData);
        foreach(string path in Directory.GetFiles(records,"*.json")){
            var record=ReadRecord(path,true);if(record==null)continue;
            foreach(var file in record.Files)file.Owned=false;
            if(record.Upgrade!=null){
                record.Upgrade.PreviousImport=record.Upgrade.PreviousSnapshot=record.Upgrade.PreviousFixed=null;
                record.Upgrade.PreviousPresetFingerprint=record.Upgrade.ExpectedPresetFingerprint=null;record.Upgrade.RetiredKeys.Clear();
                record.Upgrade.PreviousReceiptIds.Clear();record.Upgrade.RelatedReceipts.Clear();record.Upgrade.PredecessorsRetired=record.Upgrade.Reconciled=true;
            }
            Safe.WriteJson(target.RecordPath(record.Key),record);
        }
    }
    internal bool HasSnapshotRecord(string key){return ReadRecord(RecordPath(key))!=null;}
    internal List<InstalledSnapshotFile> ExportSnapshotFiles(string key,List<GeneralInstallRoute> installRoutes=null,Action checkpoint=null) {
        Record record=ReadRecord(RecordPath(key));
        if(record==null)throw new IOException("O mod não tem recibo de instalação nesta pasta.");
        if(installRoutes!=null&&!GeneralInstallRoutes.Same(installRoutes,record.InstallRoutes))
            throw new IOException("O destino registrado é diferente do escolhido para a versão.");
        return SnapshotVerification.Map(record.Files,(file,poll)=>{
            string path=Target(file.Path);
            if(!SnapshotVerification.Matches(path,file.Size,file.Sha256,poll))throw new IOException("O arquivo instalado está ausente ou alterado: "+file.Path);
            return new InstalledSnapshotFile{Path=file.Path,Sha256=file.Sha256,Size=file.Size,Kind="file"};
        },checkpoint).ToList();
    }
}

public sealed partial class InstalledSnapshotInstaller {
    internal void CopyReceivedRecognition(string id,string targetData){
        var receipt=Read(id);
        if(receipt==null||receipt.Status!="installed")throw new IOException("Recibo de snapshot instalado ausente.");
        receipt.Recoverable=false;
        foreach(var file in receipt.Files){file.Owned=false;file.Before=null;}
        new InstalledSnapshotInstaller(destination,targetData,catalog).Save(receipt);
    }
    internal void DuplicateRecognition(string targetData){
        var target=new InstalledSnapshotInstaller(destination,targetData,catalog);
        foreach(var receipt in AllReceipts(true).Where(item=>item.Status=="installed")){
            receipt.Recoverable=false;
            foreach(var file in receipt.Files){file.Owned=false;file.Before=null;}
            target.Save(receipt);
        }
    }
}
