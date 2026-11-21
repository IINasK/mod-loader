using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

// A trailing separator distinguishes an original folder from a downloaded archive,
// including when that original is temporarily unavailable after reopening a project.
internal static class GeneralModFolderSource {
    const int MaxEntries=5000,MaxDirectories=10000,MaxDepth=64;
    const long MaxFile=16L*1024*1024*1024,MaxTotal=128L*1024*1024*1024;
    internal sealed class SourceFile {
        internal string Path,Relative,Sha256;
        internal long Size,WriteTicks;
    }
    internal sealed class Package:IDisposable {
        internal string Path;
        internal string Source;
        readonly string folder;
        FileStream lease;
        internal Package(string folder,string source,FileStream lease){this.folder=folder;Source=source;this.lease=lease;Path=Safe.In(folder,"payload.zip");}
        public void Dispose(){
            if(lease==null)return;
            lease.Dispose();lease=null;
            DeleteOwned(folder);
        }
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern uint GetFinalPathNameByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle handle,StringBuilder path,uint length,uint flags);
    [StructLayout(LayoutKind.Sequential)]struct FileInformation {
        internal uint Attributes;internal System.Runtime.InteropServices.ComTypes.FILETIME Created,Accessed,Written;
        internal uint Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow;
    }
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle handle,out FileInformation info);
    internal static bool IsSource(string source){
        return !String.IsNullOrEmpty(source)&&(source.EndsWith("\\",StringComparison.Ordinal)||source.EndsWith("/",StringComparison.Ordinal))&&
            GeneralModDownload.IsLocalPath(source.TrimEnd('\\','/'));
    }
    internal static string Normalize(string source){
        if(String.IsNullOrWhiteSpace(source)||!GeneralModDownload.IsLocalPath(source.TrimEnd('\\','/')))throw Invalid();
        string path=Path.GetFullPath(source.TrimEnd('\\','/'));
        if(!GeneralModDownload.IsLocalPath(path)||path.Length<=3)throw Invalid();
        Safe.NoLinks(path);
        if(File.Exists(path))throw Invalid();
        return path.TrimEnd('\\')+"\\";
    }
    internal static string[] PreviewPaths(string source){return ReadFiles(source).Select(file=>file.Relative).ToArray();}
    internal static List<SourceFile> ReadFiles(string source){
        string root=Normalize(source);
        if(!Directory.Exists(root))throw new DirectoryNotFoundException(GeneralStyle.T("A pasta original do mod não está disponível.","The mod's original folder is unavailable."));
        var files=new List<SourceFile>();var unique=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var todo=new Stack<Tuple<string,int>>();todo.Push(Tuple.Create(root.TrimEnd('\\'),0));long total=0;int directories=0;
        while(todo.Count>0){
            var next=todo.Pop();Safe.NoLinks(next.Item1);
            if(++directories>MaxDirectories||next.Item2>MaxDepth)throw Invalid();
            foreach(string item in Directory.EnumerateFileSystemEntries(next.Item1)){
                Safe.NoLinks(item);
                string relative=Path.GetFullPath(item).Substring(root.Length).Replace('\\','/');
                if(relative.Length==0||relative.Length>300||!GeneralModDownload.IsLocalPath(item)||!String.Equals(Safe.In(root,relative),Path.GetFullPath(item),StringComparison.OrdinalIgnoreCase)||!unique.Add(relative))throw Invalid();
                var attributes=File.GetAttributes(item);
                if((attributes&FileAttributes.ReparsePoint)!=0)throw Invalid();
                if((attributes&FileAttributes.Directory)!=0){todo.Push(Tuple.Create(item,next.Item2+1));continue;}
                var info=new FileInfo(item);
                if(info.Length>MaxFile||(total+=info.Length)>MaxTotal||files.Count>=MaxEntries)throw Invalid();
                using(Open(item)){}
                files.Add(new SourceFile{Path=item,Relative=relative,Size=info.Length,WriteTicks=info.LastWriteTimeUtc.Ticks});
            }
        }
        if(files.Count==0)throw new IOException(GeneralStyle.T("A pasta não contém arquivos para instalar.","The folder contains no files to install."));
        return files.OrderBy(file=>file.Relative,StringComparer.Ordinal).ToList();
    }
    internal static Package Prepare(string source,string privateData,Action check=null,Action<string> checkpoint=null){
        string root=Normalize(source),support=Safe.In(Path.GetFullPath(privateData),"FolderImports");
        Safe.NoLinks(support);
        if(Contains(root,privateData)||Contains(privateData,root))throw Invalid();
        var files=ReadFiles(root);if(check!=null)check();
        Directory.CreateDirectory(support);Cleanup(support);
        string folder=Safe.In(support,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        File.WriteAllText(Safe.In(folder,"owner.txt"),"ModLoader.FolderImport/1\n"+Path.GetFileName(folder));
        var result=new Package(folder,root,new FileStream(Safe.In(folder,"lease"),FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None));
        var inputs=new Dictionary<string,FileStream>(StringComparer.OrdinalIgnoreCase);
        try{
            // Keep bounded read leases until the complete source snapshot is checked.
            foreach(var file in files){if(check!=null)check();EnsureUnchanged(file);inputs.Add(file.Path,Open(file.Path));}
            using(var output=new FileStream(result.Path,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None))
            using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true)){
                foreach(var file in files){
                    if(check!=null)check();EnsureUnchanged(file);
                    var entry=zip.CreateEntry(file.Relative,CompressionLevel.NoCompression);entry.LastWriteTime=new DateTimeOffset(1980,1,1,0,0,0,TimeSpan.Zero);
                    var input=inputs[file.Path];
                    using(var target=entry.Open())using(var hash=SHA256.Create()){
                        var buffer=new byte[131072];long readTotal=0;int read;
                        while((read=input.Read(buffer,0,buffer.Length))>0){
                            if(check!=null)check();if((readTotal+=read)>file.Size)throw Changed();
                            hash.TransformBlock(buffer,0,read,null,0);target.Write(buffer,0,read);
                        }
                        hash.TransformFinalBlock(new byte[0],0,0);
                        if(readTotal!=file.Size)throw Changed();file.Sha256=Safe.Hex(hash.Hash);
                    }
                    EnsureUnchanged(file);if(checkpoint!=null)checkpoint(file.Relative);
                }
            }
            var current=ReadFiles(root);
            if(current.Count!=files.Count||!current.Select(file=>file.Relative).SequenceEqual(files.Select(file=>file.Relative),StringComparer.Ordinal))throw Changed();
            foreach(var file in files){
                if(check!=null)check();EnsureUnchanged(file);
                ValidateHandle(inputs[file.Path],file.Path);
            }
            return result;
        }catch{result.Dispose();throw;}
        finally{foreach(var input in inputs.Values)input.Dispose();}
    }
    internal static bool Matches(string source,GeneralModInstaller.Record record,Func<string,string> readHash){
        if(record==null)return false;
        var files=ReadFiles(source);
        if(files.Count!=record.Files.Count)return false;
        var indexed=record.Files.ToDictionary(file=>file.ArchivePath??file.Path,StringComparer.OrdinalIgnoreCase);
        foreach(var file in files){
            GeneralModInstaller.RecordFile installed;
            if(!indexed.TryGetValue(file.Relative,out installed)||file.Size!=installed.Size||readHash(file.Path)!=installed.Sha256)return false;
        }
        return true;
    }
    internal static bool Contains(string parent,string path){return (Path.GetFullPath(path).TrimEnd('\\')+"\\").StartsWith(Path.GetFullPath(parent).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase);}
    static void EnsureUnchanged(SourceFile file){Safe.NoLinks(file.Path);var info=new FileInfo(file.Path);if(!info.Exists||info.Length!=file.Size||info.LastWriteTimeUtc.Ticks!=file.WriteTicks)throw Changed();}
    static FileStream Open(string path){
        Safe.NoLinks(path);var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        try{ValidateHandle(stream,path);return stream;}catch{stream.Dispose();throw;}
    }
    internal static void ValidateHandle(FileStream stream,string path){
        Safe.NoLinks(path);
        FileInformation info;if(!GetFileInformationByHandle(stream.SafeFileHandle,out info)||info.Links!=1)throw Invalid();
        var actual=new StringBuilder(32768);uint length=GetFinalPathNameByHandle(stream.SafeFileHandle,actual,(uint)actual.Capacity,0);
        string resolved=actual.ToString();if(resolved.StartsWith(@"\\?\",StringComparison.Ordinal))resolved=resolved.Substring(4);
        if(length==0||length>=actual.Capacity||!String.Equals(resolved,Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase))throw Changed();
    }
    static void Cleanup(string support){
        foreach(string folder in Directory.EnumerateDirectories(support).Take(1000)){
            try{
                Safe.NoLinks(folder);string lease=Safe.In(folder,"lease");
                if(!File.Exists(lease))continue;
                using(new FileStream(lease,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){}
                DeleteOwned(folder);
            }catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
    static void DeleteOwned(string folder){
        Safe.NoLinks(folder);if(!Directory.Exists(folder))return;string id=Path.GetFileName(folder);Guid parsed;
        if(!Guid.TryParseExact(id,"N",out parsed)||Directory.EnumerateDirectories(folder).Any())return;
        string[] files=Directory.GetFiles(folder);if(files.Any(path=>!new[]{"owner.txt","lease","payload.zip"}.Contains(Path.GetFileName(path),StringComparer.Ordinal)))return;
        foreach(string file in files)Safe.NoLinks(file);
        string owner=Safe.In(folder,"owner.txt");if(!File.Exists(owner)||new FileInfo(owner).Length>200||File.ReadAllText(owner)!="ModLoader.FolderImport/1\n"+id)return;
        foreach(string file in files)File.Delete(file);Directory.Delete(folder,false);
    }
    static IOException Invalid(){return new IOException(GeneralStyle.T("Escolha uma pasta local de mod, sem links, com até 5.000 arquivos (16 GB por arquivo e 128 GB no total).","Choose a local mod folder without links, up to 5,000 files (16 GB per file and 128 GB total)."));}
    static IOException Changed(){return new IOException(GeneralStyle.T("A pasta do mod mudou durante a preparação. A instalação foi preservada; tente novamente quando os arquivos estiverem prontos.","The mod folder changed during preparation. The installation was preserved; try again when its files are ready."));}
}
