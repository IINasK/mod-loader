using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

internal static class GeneralUpdateBackup {
    internal static string NewName(){return DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")+".zip";}

    internal static string Create(string data,string root,IEnumerable<KeyValuePair<string,string>> files,Action checkpoint,string name=null){
        string folder=Safe.In(data,"BackupsAtualizacoes");
        Safe.NoLinks(folder);Directory.CreateDirectory(folder);
        if(name==null)name=NewName();
        if(Path.GetFileName(name)!=name||!name.EndsWith(".zip",StringComparison.Ordinal))
            throw new IOException("Nome de backup inválido.");
        string output=Safe.In(folder,name),partial=output+".part";
        var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var buffer=new byte[128*1024];
        try{
            using(var stream=new FileStream(partial,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None))
            using(var zip=new ZipArchive(stream,ZipArchiveMode.Create)){
                foreach(var file in files){
                    checkpoint();
                    string path=Safe.In(root,file.Key);
                    Safe.NoLinks(path);
                    if(!paths.Add(path)||!FixedData.HashValid(file.Value))
                        throw new IOException("Arquivo inválido no backup da atualização.");
                    using(var source=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
                    using(var entry=zip.CreateEntry(file.Key.Replace('\\','/'),CompressionLevel.Fastest).Open())
                    using(var sha=SHA256.Create()){
                        int count;
                        while((count=source.Read(buffer,0,buffer.Length))>0){
                            checkpoint();entry.Write(buffer,0,count);
                            sha.TransformBlock(buffer,0,count,buffer,0);
                        }
                        sha.TransformFinalBlock(new byte[0],0,0);
                        if(Safe.Hex(sha.Hash)!=file.Value)
                            throw new IOException("Arquivo alterado durante o backup: "+file.Key);
                    }
                }
            }
            checkpoint();File.Move(partial,output);return output;
        }finally{
            if(File.Exists(partial)){Safe.NoLinks(partial);File.Delete(partial);}
        }
    }
}