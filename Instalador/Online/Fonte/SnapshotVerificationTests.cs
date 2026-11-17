using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

internal static class SnapshotVerificationTests {
    internal static int Run(string parent){
        string root=OnlineTests.NestedRoot(parent,"hv-");Directory.CreateDirectory(root);
        var checks=new List<string>();
        Action<bool,string> check=(ok,label)=>{if(!ok)throw new IOException(label);checks.Add(label);};
        var paths=new List<string>();
        var random=new Random(417);
        byte[] block=new byte[1024*1024];random.NextBytes(block);
        for(int index=0;index<24;index++){
            string path=Path.Combine(root,"f"+index+".bin");
            using(var output=File.Create(path))for(int part=0;part<8;part++)output.Write(block,0,block.Length);
            paths.Add(path);
        }
        for(int index=0;index<128;index++){
            string path=Path.Combine(root,"s"+index+".bin");
            using(var output=File.Create(path))output.Write(block,index,4096);
            paths.Add(path);
        }
        string empty=Path.Combine(root,"empty.bin");File.WriteAllBytes(empty,new byte[0]);
        check(SnapshotVerification.Hash(empty)=="e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855","SHA-256 vazio confere com vetor conhecido.");
        var baseline=paths.Select(Safe.Hash).ToArray();
        var ordered=SnapshotVerification.Map(paths,(path,poll)=>SnapshotVerification.Hash(path,poll));
        check(ordered.SequenceEqual(baseline),"SHA-256 CNG paralelo confere byte a byte com o algoritmo anterior e mantém a ordem.");
        int active=0,maximum=0;
        var indices=Enumerable.Range(0,24).ToArray();object gate=new object();
        SnapshotVerification.Map(indices,(index,poll)=>{
            lock(gate){active++;maximum=Math.Max(maximum,active);}Thread.Sleep(5);
            lock(gate){active--;}return index;
        });
        check(maximum<=SnapshotVerification.Workers&&maximum>=1,"Verificação limita concorrência e preserva processadores para interface.");
        int calls=0;bool canceled=false;
        var cancelWatch=Stopwatch.StartNew();
        try{SnapshotVerification.Hash(paths[0],()=>{if(++calls==3)throw new OperationCanceledException();});}
        catch(OperationCanceledException){canceled=true;}
        cancelWatch.Stop();
        check(canceled&&calls==3,"Cancelamento interrompe hash por blocos sem terminar o arquivo inteiro.");
        bool parallelCanceled=false;
        try{SnapshotVerification.Map(paths,(path,poll)=>SnapshotVerification.Hash(path,poll),()=>{throw new OperationCanceledException();});}
        catch(OperationCanceledException){parallelCanceled=true;}
        check(parallelCanceled,"Cancelamento paralelo preserva OperationCanceledException sem AggregateException.");
        check(SnapshotVerification.Hash(paths[0])==baseline[0],"Cancelamento libera stream e limite de leitores para a operação seguinte.");
        string moved=paths[paths.Count-1],destination=moved+".moved";long movedSize=new FileInfo(moved).Length;
        File.Move(moved,destination);
        check(!SnapshotVerification.Matches(moved,movedSize,baseline[baseline.Length-1]),"Mover arquivo manualmente é detectado como ausente.");
        File.Move(destination,moved);
        DateTime written=File.GetLastWriteTimeUtc(moved);
        using(var update=new FileStream(moved,FileMode.Open,FileAccess.Write,FileShare.None)){update.Position=7;update.WriteByte((byte)(block[134]^255));}
        File.SetLastWriteTimeUtc(moved,written);
        check(!SnapshotVerification.Matches(moved,movedSize,baseline[baseline.Length-1]),"Alteração de mesmo tamanho e data é detectada por hash completo.");
        bool locked=false;
        using(var writer=new FileStream(paths[0],FileMode.Open,FileAccess.ReadWrite,FileShare.None)){
            try{SnapshotVerification.Hash(paths[0]);}catch(IOException){locked=true;}
        }
        check(locked,"Arquivo em escrita exclusiva não é validado por metadados.");
        bool originalException=false;
        try{SnapshotVerification.Map(indices,(index,poll)=>{if(index==0)throw new IOException("fixture");return index;});}
        catch(IOException ex){originalException=ex.Message=="fixture";}
        check(originalException,"Falha paralela mantém o tipo original para revisão por mod.");
        using(var sample=File.OpenRead(paths[0])){
            sample.Position=17;
            check(SnapshotVerification.CompressionFor(sample)==CompressionLevel.NoCompression&&sample.Position==17,
                "Amostras incompressíveis escolhem ZIP sem compressão e restauram posição do stream.");
        }
        byte[] compressible=new byte[3*1024*1024];
        using(var sample=new MemoryStream(compressible)){
            sample.Position=23;
            check(SnapshotVerification.CompressionFor(sample)==CompressionLevel.Fastest&&sample.Position==23,
                "Arquivo compressível mantém compactação e posição do stream.");
            int sampleCalls=0;bool sampleCanceled=false;
            try{SnapshotVerification.CompressionFor(sample,()=>{if(++sampleCalls==2)throw new OperationCanceledException();});}
            catch(OperationCanceledException){sampleCanceled=true;}
            check(sampleCanceled&&sample.Position==23,"Cancelar entre amostras não deixa posição alterada.");
        }
        random.NextBytes(compressible);
        Array.Clear(compressible,compressible.Length/2-32768,65536);
        using(var sample=new MemoryStream(compressible))
            check(SnapshotVerification.CompressionFor(sample)==CompressionLevel.Fastest,"Amostra intermediária compressível preserva compactação em conteúdo misto.");
        using(var sample=new MemoryStream(new byte[4096]))
            check(SnapshotVerification.CompressionFor(sample)==CompressionLevel.Fastest,"Arquivo pequeno não paga o custo de amostragem.");
        string adaptiveZip=Path.Combine(root,"adaptive.zip");
        var payloads=new[]{new byte[3*1024*1024],compressible,File.ReadAllBytes(paths[0])};
        using(var archive=ZipFile.Open(adaptiveZip,ZipArchiveMode.Create)){
            for(int index=0;index<payloads.Length;index++)using(var input=new MemoryStream(payloads[index]))
                using(var output=archive.CreateEntry("sample"+index,SnapshotVerification.CompressionFor(input)).Open())input.CopyTo(output);
        }
        using(var archive=ZipFile.OpenRead(adaptiveZip)){
            bool identical=true;
            for(int index=0;index<payloads.Length;index++)using(var input=archive.GetEntry("sample"+index).Open())
                using(var sha=SHA256.Create())identical&=Safe.Hex(sha.ComputeHash(input))==Safe.Hex(sha.ComputeHash(payloads[index]));
            check(identical,"ZIP adaptativo de conteúdo compressível, misto e incompressível preserva todos os bytes no leitor padrão.");
        }
        // The same files are measured repeatedly after warming. This is not a network/uncached disk benchmark.
        var measurements=new List<object>();
        for(int round=0;round<3;round++){
            Measure(measurements,"previous-serial",round,()=>{foreach(var path in paths)Safe.Hash(path);});
            Measure(measurements,"cng-serial",round,()=>{foreach(var path in paths)SnapshotVerification.Hash(path);});
            Measure(measurements,"cng-2",round,()=>SnapshotVerification.Map(paths,(path,poll)=>SnapshotVerification.Hash(path,poll),workers:2));
            Measure(measurements,"cng-4",round,()=>SnapshotVerification.Map(paths,(path,poll)=>SnapshotVerification.Hash(path,poll),workers:4));
        }
        var report=new{Checks=checks,Bytes=paths.Sum(path=>new FileInfo(path).Length),Files=paths.Count,LogicalProcessors=Environment.ProcessorCount,
            WorkerLimit=SnapshotVerification.Workers,BufferBytes=SnapshotVerification.BufferBytes,CancelMilliseconds=cancelWatch.Elapsed.TotalMilliseconds,
            Cache="Warm OS file cache; no persistent application hash cache",Measurements=measurements};
        Safe.WriteJson(Path.Combine(root,"verification-results.json"),report);
        Console.WriteLine("SnapshotVerification: "+checks.Count+" checks; "+root);
        return 0;
    }
    static void Measure(List<object> results,string name,int round,Action action){
        GC.Collect();GC.WaitForPendingFinalizers();
        var process=Process.GetCurrentProcess();TimeSpan before=process.TotalProcessorTime;
        var clock=Stopwatch.StartNew();action();clock.Stop();
        results.Add(new{Name=name,Round=round,Milliseconds=clock.Elapsed.TotalMilliseconds,CpuMilliseconds=(process.TotalProcessorTime-before).TotalMilliseconds});
    }
}
