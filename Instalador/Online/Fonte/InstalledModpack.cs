using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;

public sealed class InstalledSnapshotReviewException : IOException {
    public readonly string[] Problems;
    internal InstalledSnapshotReviewException(IEnumerable<string> problems):base("Não foi possível verificar todos os mods. Corrija os itens antes de exportar ou enviar:\r\n\r\n"+String.Join("\r\n\r\n",problems)){Problems=problems.ToArray();}
}
public static partial class InstalledSnapshot {
    // A manual pack uses paths relative to the game root; it never contains the sender's absolute folders.
    public static void CreateModpack(string output,InstalledSnapshotManifest manifest,string root,FixedCatalog catalog,Action<string> report){
        Validate(manifest,catalog);if(manifest.Files.Any(file=>file.Path.Equals("_modloader/project.json",StringComparison.OrdinalIgnoreCase)))throw new IOException("O mod usa o caminho reservado aos metadados do modpack.");output=Path.GetFullPath(output);root=Path.GetFullPath(root);Safe.NoLinks(output);Safe.NoLinks(root);
        if(output.StartsWith(root.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("Salve o modpack fora da pasta do jogo.");
        bool created=false;
        try{using(var stream=new FileStream(output,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None)){
            created=true;using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,true)){
                foreach(var group in manifest.Files.GroupBy(file=>file.Path,StringComparer.OrdinalIgnoreCase)){
                    var file=group.First();if(report!=null)report("Exportando "+file.Path);
                    using(var target=zip.CreateEntry(file.Path,CompressionLevel.Fastest).Open()){
                        if(file.Kind=="file"){
                            string source=GeneralInstallRoutes.Resolve(root,file.Path,true);
                            using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read))using(var sha=SHA256.Create()){
                                ValidateCustomData(manifest,file,input);input.Position=0;byte[] buffer=new byte[1048576];long length=0;int n;
                                while((n=input.Read(buffer,0,buffer.Length))>0){length+=n;if(length>file.Size)throw new IOException("Arquivo mudou: "+file.Path);sha.TransformBlock(buffer,0,n,null,0);target.Write(buffer,0,n);}
                                sha.TransformFinalBlock(new byte[0],0,0);if(length!=file.Size||Safe.Hex(sha.Hash)!=file.Sha256)throw new IOException("Arquivo mudou: "+file.Path);
                            }
                        }else{byte[] content=new byte[0];foreach(var config in group)content=InstalledSnapshotConfig.Apply(config,content);target.Write(content,0,content.Length);}
                    }
                }
                // Small organization metadata is separate from the mod payload tree.
                using(var target=zip.CreateEntry("_modloader/project.json",CompressionLevel.Optimal).Open()){byte[] bytes=Utf8.GetBytes(Safe.Json.Serialize(manifest.Project??manifest.List));target.Write(bytes,0,bytes.Length);}
            }stream.Flush(true);
        }}catch{if(created){Safe.NoLinks(output);File.Delete(output);}throw;}
    }
}
