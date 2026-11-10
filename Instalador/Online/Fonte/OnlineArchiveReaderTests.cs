using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

public static class OnlineArchiveReaderTests {
    static string Hash(byte[] bytes){using(var sha=SHA256.Create())return Safe.Hex(sha.ComputeHash(bytes));}
    static readonly string[] Codecs={"LZMA","LZMA2","PPMd"};
    static readonly string[] Fixtures={
        "N3q8ryccAAQeTgzmqgAAAAAAAAAhAAAAAAAAAOwYktQAMhpI9orpd42Img99H/DZGt2k57xylNTKVvRpu8pw7S3PqC/3lKX5/DIAAAAAgTMHrg/QxEs8nz9HQVjW/gJqJcpsbNZkmP1LN/IXsn39FzLmODhZL0RiNsg99SDEqFOkfN/SAUD4Pkuet2Nx7KOONDV407lTFkqX9iCQnsiieRTBxEomgVy0FZOkJIzVtwGavI5bh7fZNsIbnwIL2hs4PL+ewSymp2n5oBcGLAEJfgAHCwEAASMDAQEFXQAQAAAMgSIKAY6AoEIAAA==",
        "N3q8ryccAAQnqgfVqgAAAAAAAAAhAAAAAAAAAL5Ja8oBACxkaWZmZXJlbnQgd2lsZGNhcmQgdGFyZ2V0Zml4ZWQgbGl0ZXJhbCB0YXJnZXQAAACBMweuD9EP9rigkKB3XsVAGIxO2lC64U6RXTVs6V5TUqHoTxswmXTi52qm3AXFbfPB4lHzpcQ14jCQDjPcLwrl5cvYzuvwdxL7FPqfaijn8s6ktBZrleIHdIQ6mCS6VZ4cKk5L6bGSXSL+/HDHFE4yzZArFTmgABcGMQEJeQAHCwEAASMDAQEFXQAQAAAMgRIKAdbCPV4AAA==",
        "N3q8ryccAASLUiyEpQAAAAAAAAAiAAAAAAAAAIhU+04AZAdG+1DuhjM6oxUdY85xmV73DAoS9SirIPsID4AmWmEa6HAoAACBMweuD9BaW3yfP0dBXPbVIDB102uyHAaA2lDIOOAcADPmrbdphx7sKoStx/Y5++32htHQmi9nD665J8/pWSSXdH77FajcuTncm3yNpV5HuTI8DaWdtAGBoHPHDeoWt1RaCzY9W0GzCemlxY/W2+TYXV6SJuqf4fwOalvdYAAXBiUBCYCAAAcLAQABIwMBAQVdABAAAAyBIgoBXZ5ZVAAA"
    };
    public static void Run(string parent){
        string root=Path.Combine(Path.GetFullPath(parent),"online-testes-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var passed=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("TESTE LEITOR FALHOU: "+name);passed.Add(name);};
        Action<Action,string> refuse=(test,name)=>{bool failed=false;try{test();}catch(IOException){failed=true;}check(failed,name);};
        string entry="archive/pc/mod/literal[1].archive";byte[] literal=Encoding.UTF8.GetBytes("fixed literal target");
        for(int i=0;i<Codecs.Length;i++){
            byte[] bytes=Convert.FromBase64String(Fixtures[i]);string source=Path.Combine(root,Codecs[i]+".7z");File.WriteAllBytes(source,bytes);
            check(BundledArchiveReader.ReadEntries(source).Any(file=>file.Path==entry&&file.Size==literal.Length),
                Codecs[i]+" pode ser listado na prévia sem extrair o pacote");
            using(var reader=BundledArchiveReader.Open(source,bytes.Length,Hash(bytes))){
                using(var output=new MemoryStream()){reader.CopyEntry(entry,s=>s.CopyTo(output));check(output.ToArray().SequenceEqual(literal),Codecs[i]+" é lido pelo componente incorporado e o nome com colchetes é literal");}
                refuse(()=>{using(var stream=new FileStream(source,FileMode.Open,FileAccess.Write,FileShare.Read)){}},"Arquivo compactado fica protegido contra alterações durante a leitura "+Codecs[i]);
                refuse(()=>reader.CopyEntry("archive/pc/mod/ausente.archive",s=>{}),"Entrada ausente é recusada "+Codecs[i]);
                refuse(()=>reader.CopyEntry(entry,s=>{s.ReadByte();}),"Leitura incompleta não é considerada uma extração íntegra "+Codecs[i]);
                bool incompleteApproval=false;try{reader.CopyEntry(entry,s=>{throw new FileContentMismatchException(new FixedFile{Path=entry,Size=1},20,new string('a',64));});}catch(FileContentMismatchException){incompleteApproval=true;}catch(IOException){}
                check(!incompleteApproval,"Divergência só pode ser autorizada depois de ler toda a entrada "+Codecs[i]);
            }
            using(var stream=new FileStream(source,FileMode.Open,FileAccess.Write,FileShare.None))check(stream.CanWrite,"Descarte libera a trava do pacote "+Codecs[i]);
            refuse(()=>{using(var reader=BundledArchiveReader.Open(source,bytes.Length+1,Hash(bytes))){}},"Pacote com tamanho incorreto é recusado antes do leitor "+Codecs[i]);
            refuse(()=>{using(var reader=BundledArchiveReader.Open(source,bytes.Length,new string('b',64))){}},"Pacote com hash incorreto é recusado antes do leitor "+Codecs[i]);
        }
        string listing="Path = archive\\pc\\mod\\regular.archive\nSize = 20\nAttributes = A\nEncrypted = -\n\n";
        var regular=BundledArchiveReader.ParseListing(listing);check(regular.Count==1&&regular.ContainsKey("archive/pc/mod/regular.archive")&&!regular.Values.First().Directory,"Listagem normaliza separadores e confirma arquivo regular");
        foreach(string extra in new[]{"Symbolic Link = target","Hard Link = target","Copy Link = target","Reparse = target","Encrypted = +","Alternate Stream = +","Split Before = +","Mode = lrwxrwxrwx"}){
            string bad=listing.TrimEnd()+"\n"+extra+"\n\n";
            if(extra=="Encrypted = +")bad=listing.Replace("Encrypted = -","Encrypted = +");
            refuse(()=>BundledArchiveReader.ParseListing(bad),"Listagem recusa "+extra);
        }
        refuse(()=>BundledArchiveReader.ParseListing(listing+listing.ToUpperInvariant()),"Entradas duplicadas por maiúsculas são recusadas");
        refuse(()=>BundledArchiveReader.ParseListing(listing.Replace("archive\\pc\\mod\\regular.archive","..\\escape.archive")),"Caminho que sai da pasta é recusado antes de extrair");
        refuse(()=>BundledArchiveReader.ParseListing(listing.Replace("Size = 20","Size = 8589934593")),"Entrada acima de 8 GiB é recusada");
        refuse(()=>BundledArchiveReader.ParseListing(listing.Replace("Size = 20\n","")),"Entrada sem tamanho não é aceita");
        refuse(()=>BundledArchiveReader.ParseListing(listing.Replace("Attributes = A","Attributes = A_ lrwxrwxrwx")),"Link Unix em atributos ZIP também é recusado");
        refuse(()=>BundledArchiveReader.ParseListing(listing.Replace("Attributes = A","Folder = +\nAttributes = A")),"Tipo de pasta e atributos conflitantes são recusados");
        string licenses=ArchiveLicenses.Text();check(licenses.Contains("GNU LESSER GENERAL PUBLIC LICENSE")&&licenses.Contains("unRAR")&&licenses.Contains("Igor Pavlov"),"Licenças LGPL/BSD/unRAR e autoria acompanham o programa");
        string saved=Path.Combine(root,"source.tar.xz");ArchiveLicenses.SaveSource(saved);check(new FileInfo(saved).Length==ArchiveReaderIdentity.SourceSize&&Safe.Hash(saved)==ArchiveReaderIdentity.SourceHash,"Código-fonte correspondente pode ser salvo com identidade oficial intacta");
        Safe.WriteJson(Path.Combine(root,"resultado-leitor.json"),new{Passed=passed.Count,Tests=passed,ReaderVersion=ArchiveReaderIdentity.Version,FixtureOnly=true,RealGameTouched=false});File.WriteAllText(Path.Combine(parent,"ultimo-teste-leitor.txt"),root);
    }
}
