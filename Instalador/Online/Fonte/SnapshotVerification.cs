using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

// Bounded, operation-scoped verification. No timestamp shortcut or persistent hash cache.
internal static class SnapshotVerification {
    internal const int BufferBytes=1024*1024;
    internal static readonly int Workers=Math.Max(1,Math.Min(4,Environment.ProcessorCount-1));
    static readonly SemaphoreSlim Readers=new SemaphoreSlim(Workers,Workers);

    internal static SHA256 Algorithm(){return new SHA256Cng();}

    internal static string Hash(string path,Action checkpoint=null){
        bool entered=false;
        try{
            while(!(entered=Readers.Wait(100)))if(checkpoint!=null)checkpoint();
            if(checkpoint!=null)checkpoint();
            Safe.NoLinks(path);
            using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.SequentialScan))
            using(var sha=Algorithm()){
                byte[] buffer=new byte[(int)Math.Max(4096,Math.Min(BufferBytes,input.Length))];int count;
                while((count=input.Read(buffer,0,buffer.Length))>0){
                    if(checkpoint!=null)checkpoint();
                    sha.TransformBlock(buffer,0,count,null,0);
                }
                if(checkpoint!=null)checkpoint();
                sha.TransformFinalBlock(new byte[0],0,0);
                return Safe.Hex(sha.Hash);
            }
        }finally{if(entered)Readers.Release();}
    }

    internal static bool Matches(string path,long size,string sha256,Action checkpoint=null){
        Safe.NoLinks(path);
        return File.Exists(path)&&new FileInfo(path).Length==size&&Hash(path,checkpoint)==sha256;
    }

    // Already compressed assets should not spend seconds in deflate for almost no size saving.
    // Three bounded samples are advisory only: full SHA-256 verification still covers every byte.
    internal static CompressionLevel CompressionFor(Stream input,Action checkpoint=null){
        if(!input.CanSeek||input.Length<1024*1024)return CompressionLevel.Fastest;
        const int sampleBytes=65536;
        long original=input.Position,encoded=0;
        byte[] sample=new byte[sampleBytes];
        try{
            foreach(long position in new[]{0L,(input.Length-sampleBytes)/2,input.Length-sampleBytes}){
                if(checkpoint!=null)checkpoint();
                input.Position=position;int read=0,count;
                while(read<sample.Length&&(count=input.Read(sample,read,sample.Length-read))>0)read+=count;
                if(read!=sample.Length)throw new IOException("Arquivo mudou durante a amostragem da compressão.");
                using(var output=new MemoryStream()){
                    using(var compress=new DeflateStream(output,CompressionLevel.Fastest,true))compress.Write(sample,0,read);
                    encoded+=output.Length;
                }
            }
            if(checkpoint!=null)checkpoint();
            return encoded<sampleBytes*3L*98/100?CompressionLevel.Fastest:CompressionLevel.NoCompression;
        }finally{input.Position=original;}
    }

    // Results retain input order. The coordinator alone consumes them; callbacks are serialized.
    internal static TResult[] Map<T,TResult>(IList<T> items,Func<T,Action,TResult> read,Action checkpoint=null,int workers=0){
        var result=new TResult[items.Count];
        if(items.Count==0)return result;
        var errors=new Exception[items.Count];
        object gate=new object();int canceled=0;
        Action poll=()=>{
            lock(gate){
                if(Volatile.Read(ref canceled)!=0)throw new OperationCanceledException();
                if(checkpoint!=null)checkpoint();
            }
        };
        int degree=Math.Max(1,Math.Min(workers<=0?Workers:workers,items.Count));
        Parallel.For(0,items.Count,new ParallelOptions{MaxDegreeOfParallelism=degree},index=>{
            try{poll();result[index]=read(items[index],poll);}
            catch(OperationCanceledException ex){errors[index]=ex;Interlocked.Exchange(ref canceled,1);}
            catch(Exception ex){errors[index]=ex;}
        });
        foreach(var error in errors)if(error is OperationCanceledException)ExceptionDispatchInfo.Capture(error).Throw();
        foreach(var error in errors)if(error!=null)ExceptionDispatchInfo.Capture(error).Throw();
        return result;
    }
}
