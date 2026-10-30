using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

public static class OnlineImportTests {
    public static int Run(string parent) {
        string root=Path.Combine(Path.GetFullPath(parent),"import-testes-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var passed=new List<string>();
        Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("TESTE IMPORT FALHOU: "+name);passed.Add(name);};
        Action<Action,string> refuse=(action,name)=>{bool refused=false;try{action();}catch(IOException){refused=true;}check(refused,name);};
        var link=ModImporter.ParseNexusLink("https://www.nexusmods.com/cyberpunk2077/mods/123?tab=files&file_id=456#irrelevante");
        check(link.ModId==123&&link.FileId==456&&link.Url=="https://www.nexusmods.com/cyberpunk2077/mods/123?tab=files&file_id=456","Link exato e canônico sem fragmento");
        foreach(string bad in new[]{
            "http://www.nexusmods.com/cyberpunk2077/mods/123",
            "https://www.nexusmods.com.evil.invalid/cyberpunk2077/mods/123",
            "https://user:secret@www.nexusmods.com/cyberpunk2077/mods/123",
            "https://www.nexusmods.com:444/cyberpunk2077/mods/123",
            "https://www.nexusmods.com/skyrim/mods/123",
            "https://www.nexusmods.com/cyberpunk2077/mods/123?file_id=0",
            "https://www.nexusmods.com/cyberpunk2077/mods/123?file_id=-1",
            "https://www.nexusmods.com/cyberpunk2077/mods/123?file_id=9999999999999",
            "https://www.nexusmods.com/cyberpunk2077/mods/123?file_id=456&file_id=789",
            "https://www.nexusmods.com/cyberpunk2077/mods/123?apikey=secret",
            "https://www.nexusmods.com/cyberpunk2077/mods/123?tab=unknown",
            "https://www.nexusmods.com/cyberpunk2077/mods/123/../456",
            "https://www.nexusmods.com/cyberpunk2077/mods/%31%32%33",
            "https://www.nexusmods.com/cyberpunk2077/mods/123\n?file_id=456"
        }){string value=bad;refuse(()=>ModImporter.ParseNexusLink(value),"URL insegura recusada "+(passed.Count));}
        string file=Path.Combine(root,"pacote-fixo.zip");File.WriteAllText(file,"fixture de importação; não executar",new UTF8Encoding(false));
        var asset=new FixedAsset{Id="a0",ModId=123,FileId=456,Name="pacote-fixo.zip",Size=new FileInfo(file).Length,Sha256=Safe.Hash(file)};
        var catalog=new FixedCatalog{Packages=new List<FixedMod>{new FixedMod{Id="known",Assets=new List<FixedAsset>{asset}}}};
        string cacheRoot=Path.Combine(root,"cache"),queue=Path.Combine(root,"pendentes.json");var cache=new DownloadCache(cacheRoot);var importer=new ModImporter(catalog,cache,queue);
        refuse(()=>importer.Import("https://www.nexusmods.com/cyberpunk2077/mods/123"),"Pagina conhecida tambem e recusada: importacao agora e somente por arquivo");
        refuse(()=>importer.Import("https://www.nexusmods.com/cyberpunk2077/mods/123?tab=files&file_id=999"),"Pagina com file_id nao substitui o pacote baixado");
        check(!File.Exists(queue)&&!Directory.Exists(cacheRoot),"Recusar links nao cria fila, cache ou autorizacao");
        var result=importer.Import(file);
        check(result.SelectedIds.SequenceEqual(new[]{"known"})&&cache.Contains(asset)&&File.Exists(file)&&Safe.Hash(file)==asset.Sha256&&result.Message.Contains("instalação automática"),"Arquivo conhecido pronto para instalação automática, original preservado");
        string unknown=Path.Combine(root,"desconhecido.7z");File.WriteAllText(unknown,"arquivo desconhecido, não extrair",new UTF8Encoding(false));string before=Safe.Hash(unknown);
        result=importer.Import(unknown);
        check(result.Pending&&result.SelectedIds.Length==0&&importer.ReadPending().Count==1&&Safe.Hash(unknown)==before,"Arquivo desconhecido apenas registrado, sem mudar original");
        check(Directory.GetFiles(cacheRoot).Length==1&&Directory.GetFiles(root).Length==3,"Arquivo desconhecido não é copiado ou extraído");
        string sameContent=Path.Combine(root,"mesmo-conteudo.zip");File.Copy(unknown,sameContent);importer.Import(sameContent);
        check(importer.ReadPending().Count==1&&File.Exists(unknown)&&File.Exists(sameContent),"Hash desconhecido repetido não duplica fila nem remove originais");
        string privateQueue=Path.Combine(root,"fila-url.json");var privateImport=new ModImporter(catalog,cache,privateQueue);Safe.WriteJson(privateQueue,new[]{new ModImportItem{Id="nexus-789-0",Kind="Link",Name="Nexus mod789",Source="https://www.nexusmods.com/cyberpunk2077/mods/789",ModId=789,State="Pendente de auditoria"}});
        string queueBefore=Safe.Hash(privateQueue);check(privateImport.ReadPending().Count==0&&Safe.Hash(privateQueue)==queueBefore,"Links antigos sao ocultados da lista sem apagar historico");
        refuse(()=>privateImport.Import("https://www.nexusmods.com/cyberpunk2077/mods/789?apikey=secret"),"URL com credencial é recusada antes de persistir");check(Safe.Hash(privateQueue)==queueBefore,"Recusa de URL conserva a fila anterior");
        string excessive=Path.Combine(root,"fila-grande.json");File.WriteAllText(excessive,new string(' ',2000001));string excessiveHash=Safe.Hash(excessive);
        refuse(()=>new ModImporter(catalog,cache,excessive).ReadPending(),"Fila excessiva recusada antes de desserializar");check(Safe.Hash(excessive)==excessiveHash,"Fila excessiva é preservada para revisão");
        foreach(string extension in new[]{".crdownload",".exe",".part"}){string path=Path.Combine(root,"incompleto"+extension);File.WriteAllText(path,"fixture");refuse(()=>importer.Import(path),"Extensão recusada "+extension);}
        string badQueue=Path.Combine(root,"adulterado.json");Safe.WriteJson(badQueue,new[]{new ModImportItem{Id="nexus-123-0",Kind="Link",Name="mod",State="Pendente de auditoria",ModId=123,Source="https://evil.invalid/mod"}});
        refuse(()=>new ModImporter(catalog,cache,badQueue).ReadPending(),"Fila adulterada não vira link executável");
        check(File.Exists(badQueue)&&Safe.Hash(unknown)==before,"Falha de leitura da fila preserva arquivos");
        string pendingHash=Safe.Hash(queue);int cachedBefore=Directory.GetFiles(cacheRoot).Length;
        foreach(int id in new[]{10702,12669}) {
            int fileId=id==10702?61897:66441;string blockedFile=Path.Combine(root,"retido-"+id+".zip");File.WriteAllText(blockedFile,"pacote retido "+id,new UTF8Encoding(false));
            var blockedAsset=new FixedAsset{Id="a0",ModId=id,FileId=fileId,Name=Path.GetFileName(blockedFile),Sha256=Safe.Hash(blockedFile),Size=new FileInfo(blockedFile).Length};
            catalog.Packages.Add(new FixedMod{Id="retido-"+id,Name=id==10702?"BMW M4 CSL":"Yamaha R1",BlockedReason="Falha reproduzida nos registros TweakXL.",Assets=new List<FixedAsset>{blockedAsset}});
            refuse(()=>importer.Import("https://www.nexusmods.com/cyberpunk2077/mods/"+id+"?tab=files&file_id="+fileId),"Link retido continua sem autorizar fila: "+id);
            var blockedImport=importer.Import(blockedFile);
            check(blockedImport.SelectedIds.Length==0&&!blockedImport.Pending&&!cache.Contains(blockedAsset)&&Directory.GetFiles(cacheRoot).Length==cachedBefore&&Safe.Hash(queue)==pendingHash&&File.Exists(blockedFile),"Arquivo retido não seleciona, não cacheia e preserva original: "+id);
        }
        var variant=new FixedAsset{Id="a0",ModId=123,FileId=457,Name="outra-variante.zip",Size=10,Sha256=new string('a',64)};
        catalog.Packages.Add(new FixedMod{Id="variante-independente",Assets=new List<FixedAsset>{variant}});
        var variants=importer.Import(file);
        check(variants.SelectedIds.SequenceEqual(new[]{"known"}),"SHA do arquivo identifica somente a variante baixada, sem escolher outras pelo modId");
        string retiredQueue=Path.Combine(root,"fila-anterior-retidos.json");var retired=catalog.Packages.Single(m=>m.Id=="retido-10702").Assets[0];
        Safe.WriteJson(retiredQueue,new[]{new ModImportItem{Id="nexus-10702-61897",Kind="Link",Name="BMW M4 CSL",ModId=10702,FileId=61897,Source="https://www.nexusmods.com/cyberpunk2077/mods/10702?tab=files&file_id=61897",State="Pendente de auditoria"},new ModImportItem{Id="file-"+retired.Sha256,Kind="Arquivo",Name=retired.Name,Source=Path.Combine(root,retired.Name),Sha256=retired.Sha256,Size=retired.Size,State="Pendente de auditoria"}});
        check(new ModImporter(catalog,cache,retiredQueue).ReadPending().Count==0&&File.Exists(retiredQueue),"Retidos conhecidos não reaparecem como desconhecidos de fila anterior");
        Safe.WriteJson(Path.Combine(root,"resultado.json"),new{Passed=passed.Count,Tests=passed,NoGameWrites=true,NoPackageCodeExecuted=true});
        File.WriteAllText(Path.Combine(parent,"ultimo-teste-importacao.txt"),root);return passed.Count;
    }
}
