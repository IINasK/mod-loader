using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

public sealed class ReaderArchiveEntry {
    public string RawPath,Path;
    public long Size;
    public bool Directory;
}

public sealed class BundledArchiveReader : IDisposable {
    const long MaxEntryBytes=8L*1024*1024*1024;
    const int MaxListingBytes=16*1024*1024,MaxErrorBytes=65536;
    static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
    string folder,source,executable;
    FileStream sourceLock,exeLock,dllLock;
    Dictionary<string,ReaderArchiveEntry> entries;
    Process active;
    bool disposed;
    int reading;
    BundledArchiveReader(){}
    public static BundledArchiveReader Open(string file,long expectedSize,string expectedSha256) {
        var reader=new BundledArchiveReader();
        try{reader.Initialize(file,expectedSize,expectedSha256);return reader;}catch{reader.Dispose();throw;}
    }
    internal static ReaderArchiveEntry[] ReadEntries(string file) {
        using(var reader=new BundledArchiveReader()) {
            reader.Initialize(file,null,null);
            return reader.Files();
        }
    }
    void Initialize(string file,long? expectedSize,string expectedSha256) {
        if(expectedSize.HasValue&&(expectedSize<0||expectedSize>MaxEntryBytes||!FixedData.HashValid(expectedSha256)))
            throw new IOException("Identidade do pacote compactado inválida.");
        source=System.IO.Path.GetFullPath(file);Safe.NoLinks(source);
        sourceLock=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(sourceLock.Length>MaxEntryBytes)throw new IOException("O pacote compactado ultrapassa o limite de tamanho.");
        if(expectedSize.HasValue) {
            using(var sha=SHA256.Create())
                if(sourceLock.Length!=expectedSize||Safe.Hex(sha.ComputeHash(sourceLock))!=expectedSha256)
                    throw new IOException("O pacote compactado mudou ou não corresponde ao download aprovado.");
        }
        string temp=System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());Safe.NoLinks(temp);
        folder=System.IO.Path.Combine(temp,"CyberpunkCoop-reader-"+Guid.NewGuid().ToString("N"));
        if(System.IO.Directory.Exists(folder)||File.Exists(folder))throw new IOException("A pasta temporária do leitor já existe.");
        var security=new DirectorySecurity();security.SetAccessRuleProtection(true,false);
        security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        System.IO.Directory.CreateDirectory(folder,security);Safe.NoLinks(folder);
        executable=Safe.In(folder,"7z.exe");string dll=Safe.In(folder,"7z.dll");
        exeLock=Materialize("CyberpunkCoop.Reader.7z.exe",executable,ArchiveReaderIdentity.ExeSize,ArchiveReaderIdentity.ExeHash);
        dllLock=Materialize("CyberpunkCoop.Reader.7z.dll",dll,ArchiveReaderIdentity.DllSize,ArchiveReaderIdentity.DllHash);
        entries=ParseListing(List());
        if(expectedSize.HasValue)OnlineDiagnostics.Record("archive.reader.ready",null,"Pacote conferido e listado pelo leitor incluído no aplicativo.",new{ReaderVersion=ArchiveReaderIdentity.Version,Entries=entries.Count,SourceSha256=expectedSha256});
    }
    static FileStream Materialize(string resource,string path,long expectedSize,string expectedHash) {
        using(var input=typeof(BundledArchiveReader).Assembly.GetManifestResourceStream(resource)){
            if(input==null||input.Length!=expectedSize)throw new IOException("O leitor incluído no aplicativo está incompleto.");
            using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){input.CopyTo(output);output.Flush(true);}
        }
        var locked=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        try{using(var sha=SHA256.Create())if(locked.Length!=expectedSize||!String.Equals(Safe.Hex(sha.ComputeHash(locked)),expectedHash,StringComparison.OrdinalIgnoreCase))throw new IOException("O leitor incluído no aplicativo não passou na conferência de integridade.");return locked;}catch{locked.Dispose();throw;}
    }
    static string Quote(string value) {
        var result=new StringBuilder("\"");int slashes=0;
        foreach(char c in value){if(c=='\\'){slashes++;continue;}if(c=='\"'){result.Append('\\',slashes*2+1);result.Append(c);}else{result.Append('\\',slashes);result.Append(c);}slashes=0;}
        result.Append('\\',slashes*2);return result.Append('"').ToString();
    }
    Process Start(string arguments) {
        if(disposed)throw new ObjectDisposedException("BundledArchiveReader");
        var info=new ProcessStartInfo(executable,arguments){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=folder,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true,StandardOutputEncoding=Utf8,StandardErrorEncoding=Utf8};
        info.EnvironmentVariables["PATH"]=Environment.GetFolderPath(Environment.SpecialFolder.System);
        var process=Process.Start(info);if(process==null)throw new IOException("Não foi possível iniciar o leitor incluído.");
        active=process;process.StandardInput.Close();return process;
    }
    static void Kill(Process process){try{if(process!=null&&!process.HasExited){process.Kill();process.WaitForExit(5000);}}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}}
    static byte[] ReadBounded(Stream input,int max,Process process) {
        using(var output=new MemoryStream()){
            byte[] buffer=new byte[16384];int count;
            while((count=input.Read(buffer,0,buffer.Length))>0){if(output.Length+count>max){Kill(process);throw new IOException("A saída do leitor ultrapassou o limite de segurança.");}output.Write(buffer,0,count);}
            return output.ToArray();
        }
    }
    static byte[] Complete(Task<byte[]> task) {
        try{if(!task.Wait(5000))throw new IOException("O leitor não encerrou sua saída dentro do prazo.");return task.Result;}
        catch(AggregateException ex){throw new IOException("Falha ao receber a saída do leitor incluído.",ex.InnerException??ex);}
    }
    static string ErrorText(byte[] bytes) {
        string text=Encoding.UTF8.GetString(bytes).Trim();if(text.Length>1200)text=text.Substring(0,1200);return text.Length==0?"":"\r\nDetalhe do leitor: "+text;
    }
    string List() {
        using(var process=Start("l -slt -ba -sccUTF-8 -bd -p- -- "+Quote(source))){
            var output=Task.Factory.StartNew(()=>ReadBounded(process.StandardOutput.BaseStream,MaxListingBytes,process));
            var error=Task.Factory.StartNew(()=>ReadBounded(process.StandardError.BaseStream,MaxErrorBytes,process));
            try{
                if(!process.WaitForExit(30000)){Kill(process);process.WaitForExit(5000);throw new IOException("Tempo esgotado ao listar o pacote com o leitor incluído.");}
                byte[] listing=Complete(output),errors=Complete(error);
                if(process.ExitCode!=0)throw new IOException("O leitor incluído não conseguiu listar o pacote (código "+process.ExitCode+")."+ErrorText(errors));
                try{return Utf8.GetString(listing);}catch(DecoderFallbackException ex){throw new IOException("A lista do pacote contém nomes em uma codificação inválida.",ex);}
            }finally{Kill(process);active=null;}
        }
    }
    public static Dictionary<string,ReaderArchiveEntry> ParseListing(string raw) {
        if(raw==null||Utf8.GetByteCount(raw)>MaxListingBytes)throw new IOException("Lista do pacote ausente ou maior que o limite.");
        var result=new Dictionary<string,ReaderArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        var fields=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        using(var reader=new StringReader(raw)){
            string line;while((line=reader.ReadLine())!=null){
                if(String.IsNullOrWhiteSpace(line)){AddEntry(result,fields);fields.Clear();continue;}
                int split=line.IndexOf(" = ",StringComparison.Ordinal);
                if(split<=0)throw new IOException("A lista do pacote contém um registro não reconhecido.");
                string key=line.Substring(0,split),value=line.Substring(split+3);
                if(fields.ContainsKey(key))throw new IOException("A lista do pacote contém propriedades duplicadas.");fields.Add(key,value);
            }
        }
        AddEntry(result,fields);if(result.Count==0)throw new IOException("O pacote não contém entradas reconhecidas.");return result;
    }
    static void AddEntry(Dictionary<string,ReaderArchiveEntry> result,Dictionary<string,string> fields) {
        if(fields.Count==0)return;
        string raw,sizeValue;if(!fields.TryGetValue("Path",out raw)||!fields.TryGetValue("Size",out sizeValue))throw new IOException("Entrada incompleta na lista do pacote.");
        string path=raw.Replace('\\','/');string checkRoot=System.IO.Path.Combine(System.IO.Path.GetPathRoot(System.IO.Path.GetTempPath()),"CoopReader-check");Safe.In(checkRoot,path);
        long size;if(!Int64.TryParse(sizeValue,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out size)||size<0||size>MaxEntryBytes)throw new IOException("Tamanho de entrada inválido ou acima de "+GeneralStorageSize.Format(MaxEntryBytes)+": "+path);
        foreach(var field in fields){
            if((field.Key.IndexOf("link",StringComparison.OrdinalIgnoreCase)>=0||field.Key.IndexOf("reparse",StringComparison.OrdinalIgnoreCase)>=0)&&field.Value.Length!=0)throw new IOException("Atalhos, links e pontos de nova análise não são aceitos no pacote: "+path);
            if((field.Key.Equals("Encrypted",StringComparison.OrdinalIgnoreCase)||field.Key.Equals("Alternate Stream",StringComparison.OrdinalIgnoreCase)||field.Key.Equals("Split Before",StringComparison.OrdinalIgnoreCase)||field.Key.Equals("Split After",StringComparison.OrdinalIgnoreCase))&&field.Value!="-"&&field.Value.Length!=0)throw new IOException("Entradas criptografadas, fluxos alternativos ou arquivos divididos não são aceitos: "+path);
        }
        string attributes,folderFlag;bool hasAttributes=fields.TryGetValue("Attributes",out attributes),hasFolder=fields.TryGetValue("Folder",out folderFlag);
        if(hasFolder&&folderFlag!="+"&&folderFlag!="-")throw new IOException("Tipo de entrada não reconhecido: "+path);
        if(!hasAttributes&&!hasFolder)throw new IOException("O leitor não confirmou o tipo de arquivo: "+path);
        bool directory=hasFolder&&folderFlag=="+";
        if(hasAttributes){
            string dos=attributes.Split(' ')[0];if(dos.Any(c=>c=='L'||c=='l'))throw new IOException("Entrada com atributo de link não aceita: "+path);
            bool attrDirectory=dos.IndexOf('D')>=0;
            if(hasFolder&&attrDirectory!=directory)throw new IOException("Atributos de tipo conflitantes no pacote: "+path);directory=attrDirectory;
            var unix=Regex.Match(attributes,@"(?:^|\s)([-dlcbps])[rwxstST-]{9}(?:\s|$)");
            if(unix.Success){char kind=unix.Groups[1].Value[0];if(kind!='-'&&kind!='d')throw new IOException("Entrada não regular recusada: "+path);if((kind=='d')!=directory)throw new IOException("Tipo Unix conflitante no pacote: "+path);}
        }
        string mode;if(fields.TryGetValue("Mode",out mode)){
            var kind=Regex.Match(mode,@"^([-dlcbps])[rwxstST-]{9}$");if(kind.Success){char type=kind.Groups[1].Value[0];if(type!='-'&&type!='d'||(type=='d')!=directory)throw new IOException("Modo de arquivo não regular recusado: "+path);}
            else if(!String.IsNullOrEmpty(mode))throw new IOException("Modo de arquivo não reconhecido: "+path);
        }
        if(directory&&size!=0)throw new IOException("Diretório com conteúdo ambíguo no pacote: "+path);
        if(result.Count>=20000||result.ContainsKey(path))throw new IOException("Entradas duplicadas ou em excesso no pacote: "+path);
        result.Add(path,new ReaderArchiveEntry{RawPath=raw,Path=path,Size=size,Directory=directory});
    }
    internal ReaderArchiveEntry[] Files() {
        if(disposed)throw new ObjectDisposedException("BundledArchiveReader");
        return entries.Values.Where(entry=>!entry.Directory).ToArray();
    }
    public void CopyEntry(string entry,Action<Stream> consume) {
        if(consume==null)throw new ArgumentNullException("consume");if(disposed)throw new ObjectDisposedException("BundledArchiveReader");
        if(Interlocked.CompareExchange(ref reading,1,0)!=0)throw new IOException("O leitor já está processando uma entrada.");
        try{
            string normalized=entry.Replace('\\','/');ReaderArchiveEntry item;
            if(!entries.TryGetValue(normalized,out item)||item.Directory)throw new IOException("Arquivo regular previsto não encontrado dentro do pacote: "+entry);
            using(var process=Start("x -so -spd -bd -bb0 -y -p- -- "+Quote(source)+" "+Quote(item.RawPath))){
                int timedOut=0;
                using(var timer=new Timer(state=>{Interlocked.Exchange(ref timedOut,1);Kill(process);},null,TimeSpan.FromMinutes(10),Timeout.InfiniteTimeSpan)){
                    var error=Task.Factory.StartNew(()=>ReadBounded(process.StandardError.BaseStream,MaxErrorBytes,process));
                    FileContentMismatchException mismatch=null;
                    try {
                        using(var input=new CountedReadStream(process.StandardOutput.BaseStream)){
                            try{consume(input);}catch(FileContentMismatchException ex){if(!input.Ended)throw new IOException("A extração não terminou; não é possível autorizar uma diferença de conteúdo.",ex);mismatch=ex;}
                            if(!input.Ended||input.Count!=item.Size)throw new IOException("A extração não corresponde à entrada listada. Esperado no pacote: "+item.Size+" bytes; lidos: "+input.Count+" bytes.");
                        }
                        if(!process.WaitForExit(5000)){Kill(process);process.WaitForExit(5000);throw new IOException("O leitor não encerrou após extrair o arquivo.");}
                        byte[] errors=Complete(error);
                        if(Interlocked.CompareExchange(ref timedOut,0,0)!=0)throw new IOException("Tempo esgotado ao extrair o arquivo com o leitor incluído.");
                        if(process.ExitCode!=0)throw new IOException("O leitor não confirmou a integridade da extração (código "+process.ExitCode+")."+ErrorText(errors));
                        if(mismatch!=null)throw mismatch;
                    }finally{Kill(process);active=null;}
                }
            }
        }finally{Interlocked.Exchange(ref reading,0);}
    }
    sealed class CountedReadStream:Stream {
        readonly Stream inner;public long Count;public bool Ended;
        public CountedReadStream(Stream input){inner=input;}
        public override int Read(byte[] buffer,int offset,int count){int n=inner.Read(buffer,offset,count);if(n==0)Ended=true;Count+=n;if(Count>MaxEntryBytes)throw new IOException("A extração ultrapassou o limite de "+GeneralStorageSize.Format(MaxEntryBytes)+".");return n;}
        public override bool CanRead{get{return true;}}public override bool CanSeek{get{return false;}}public override bool CanWrite{get{return false;}}public override long Length{get{throw new NotSupportedException();}}public override long Position{get{return Count;}set{throw new NotSupportedException();}}
        public override void Flush(){}public override long Seek(long offset,SeekOrigin origin){throw new NotSupportedException();}public override void SetLength(long value){throw new NotSupportedException();}public override void Write(byte[] buffer,int offset,int count){throw new NotSupportedException();}
    }
    public void Dispose() {
        if(disposed)return;disposed=true;Kill(active);
        if(sourceLock!=null){sourceLock.Dispose();sourceLock=null;}if(exeLock!=null){exeLock.Dispose();exeLock=null;}if(dllLock!=null){dllLock.Dispose();dllLock=null;}
        if(folder==null)return;
        try{
            string full=System.IO.Path.GetFullPath(folder),parent=System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()).TrimEnd('\\')+"\\";
            if(!full.StartsWith(parent,StringComparison.OrdinalIgnoreCase)||!Regex.IsMatch(System.IO.Path.GetFileName(full),@"^CyberpunkCoop-reader-[a-f0-9]{32}$"))return;
            Safe.NoLinks(full);if(!System.IO.Directory.Exists(full))return;
            string[] children=System.IO.Directory.GetFileSystemEntries(full);
            if(children.Any(p=>System.IO.Directory.Exists(p)||(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0||!(System.IO.Path.GetFileName(p)=="7z.exe"||System.IO.Path.GetFileName(p)=="7z.dll")))return;
            foreach(string child in children){Safe.NoLinks(child);File.Delete(child);}System.IO.Directory.Delete(full,false);
        }catch(IOException){}catch(UnauthorizedAccessException){}
    }
}
