using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
    internal List<InstalledSnapshotFile> ExportSnapshotImportedFiles(string id) {
        string folder=Safe.In(Data,"importados");
        if(!Directory.Exists(folder))throw new IOException("A instalação personalizada não tem recibo nesta pasta.");
        var receipt=Directory.GetFiles(folder,"*.json").Select(ReadReceipt)
            .Where(r=>r.Operation==LocalModPackage.ReceiptOperation && r.Status=="instalado" && r.Mods.Count==1 && r.Mods[0]==id)
            .OrderByDescending(r=>r.CreatedUtc).FirstOrDefault();
        if(receipt==null || receipt.Files.Count==0)throw new IOException("A instalação personalizada não tem recibo concluído.");
        var result=new List<InstalledSnapshotFile>();
        foreach(var file in receipt.Files) {
            string path=Safe.In(game,file.Path);
            if(!file.Applied || file.Reverted || file.UndoStarted || !File.Exists(path) || Safe.Hash(path)!=file.After)
                throw new IOException("O arquivo instalado está ausente ou alterado: "+file.Path);
            result.Add(new InstalledSnapshotFile{Path=file.Path,Sha256=file.After,Size=new FileInfo(path).Length,Kind="file"});
        }
        return result;
    }
}

public sealed partial class GeneralModInstaller {
    internal void DuplicateRecognition(string targetData){
        Safe.NoLinks(records);if(!Directory.Exists(records))return;var target=new GeneralModInstaller(destination,targetData);
        foreach(string path in Directory.GetFiles(records,"*.json")){
            var record=ReadRecord(path,true);if(record==null)continue;
            foreach(var file in record.Files)file.Owned=false;
            Safe.WriteJson(target.RecordPath(record.Key),record);
        }
    }
    internal bool HasSnapshotRecord(string key){return ReadRecord(RecordPath(key))!=null;}
    internal List<InstalledSnapshotFile> ExportSnapshotFiles(string key,List<GeneralInstallRoute> installRoutes=null) {
        Record record=ReadRecord(RecordPath(key));
        if(record==null)throw new IOException("O mod não tem recibo de instalação nesta pasta.");
        if(installRoutes!=null&&!GeneralInstallRoutes.Same(installRoutes,record.InstallRoutes))
            throw new IOException("O destino registrado é diferente do escolhido para a versão.");
        var result=new List<InstalledSnapshotFile>();
        foreach(var file in record.Files) {
            string path=Target(file.Path);
            if(!Matches(path,file))throw new IOException("O arquivo instalado está ausente ou alterado: "+file.Path);
            result.Add(new InstalledSnapshotFile{Path=file.Path,Sha256=file.Sha256,Size=file.Size,Kind="file"});
        }
        return result;
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
