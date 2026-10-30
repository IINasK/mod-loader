using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

public static class OnlineBrowserDownloadProgressTests {
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool CreateSymbolicLink(string link,string target,int flags);
    static string Folder(string root,string name){string path=Path.Combine(root,name);Directory.CreateDirectory(path);return path;}
    static string Partial(string folder,string name,int size){string path=Path.Combine(folder,name);using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))output.SetLength(size);return path;}
    static string Snapshot(string folder){return String.Join("\n",Directory.GetFiles(folder,"*",SearchOption.AllDirectories).OrderBy(path=>path,StringComparer.OrdinalIgnoreCase).Select(path=>path+"|"+new FileInfo(path).Length+"|"+File.GetLastWriteTimeUtc(path).Ticks));}
    public static int Run(string parent){
        string root=Folder(Path.GetFullPath(parent),"browser-progress-"+Guid.NewGuid().ToString("N").Substring(0,12));
        var passed=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("TESTE BROWSER PROGRESS FALHOU: "+name);passed.Add(name);};
        Action<Action,string> refuse=(work,name)=>{bool rejected=false;try{work();}catch(IOException){rejected=true;}catch(ArgumentException){rejected=true;}check(rejected,name);};
        var asset=new FixedAsset{Id="progress-fixture",Name="pacote-fixo-123-456.zip",Size=4096,Sha256=new string('a',64),ModId=123,FileId=456};DateTime since=DateTime.UtcNow.AddMinutes(-1);
        string folder=Folder(root,"fresh");var result=BrowserDownloadProgress.Read(folder,asset,since);
        check(!result.Active&&!result.Percent.HasValue&&result.ReceivedBytes==0,"Sem parcial esperado nao apresenta download nem porcentagem ficticia");
        string partial=Partial(folder,asset.Name+".crdownload",1024);string before=Snapshot(folder);
        using(var writing=new FileStream(partial,FileMode.Open,FileAccess.Write,FileShare.None))result=BrowserDownloadProgress.Read(folder,asset,since);
        check(result.Active&&result.ReceivedBytes==1024&&result.TotalBytes==4096&&result.Percent==25&&result.Estimated&&result.Path==partial,"Parcial Chromium esperado mostra tamanho observado e25porcento estimados sem ler seu conteudo bloqueado");
        check(Snapshot(folder)==before,"Observacao do progresso nao altera arquivo parcial nem cria arquivos");
        using(var writing=new FileStream(partial,FileMode.Open,FileAccess.Write))writing.SetLength(2048);result=BrowserDownloadProgress.Read(folder,asset,since);
        check(result.Active&&result.Percent==50&&result.Estimated,"Crescimento do parcial atualiza porcentagem estimada na proxima leitura");
        using(var writing=new FileStream(partial,FileMode.Open,FileAccess.Write))writing.SetLength(4096);result=BrowserDownloadProgress.Read(folder,asset,since);
        check(result.Active&&result.Percent==99&&result.Estimated,"Tamanho total ou prealocado nunca aparece100antes de conferencia final");
        File.Move(partial,Path.Combine(folder,asset.Name));result=BrowserDownloadProgress.Read(folder,asset,since);
        check(!result.Active&&!result.Percent.HasValue,"Nome final nao e tratado como download ativo nem comprovacao de conclusao pelo helper");
        string firefox=Folder(root,"firefox");Partial(firefox,"pacote-fixo-123-456 (2).zip.part",1024);result=BrowserDownloadProgress.Read(firefox,asset,since);
        check(result.Active&&result.Percent==25,"Firefoxpart com sufixo numerico do mesmo arquivo e reconhecido");
        Partial(firefox,asset.Name+".crdownload",2048);result=BrowserDownloadProgress.Read(firefox,asset,since);
        check(!result.Active&&!result.Percent.HasValue,"Dois parciais elegiveis sao ambiguos e nao sao atribuidos a um unico download");
        string unrelated=Folder(root,"unrelated");foreach(string name in new[]{"Unconfirmed123.crdownload","outro-123-456.zip.crdownload",asset.Name+".tmp",asset.Name+".part.crdownload"})Partial(unrelated,name,1024);Partial(Folder(unrelated,"subpasta"),asset.Name+".crdownload",1024);
        check(!BrowserDownloadProgress.Read(unrelated,asset,since).Active,"Nomes alheios, Unconfirmed, temporarios genericos e subpastas sao ignorados");
        string stale=Folder(root,"stale"),staleFile=Partial(stale,asset.Name+".crdownload",1024);File.SetCreationTimeUtc(staleFile,since.AddMinutes(-5));File.SetLastWriteTimeUtc(staleFile,DateTime.UtcNow);
        check(!BrowserDownloadProgress.Read(stale,asset,since).Active,"Parcial criado antes da autorizacao nao e adotado mesmo com gravacao recente");
        File.SetCreationTimeUtc(staleFile,DateTime.UtcNow);File.SetLastWriteTimeUtc(staleFile,since.AddSeconds(-1));
        check(!BrowserDownloadProgress.Read(stale,asset,since).Active,"Parcial com ultima gravacao anterior a autorizacao e ignorado");
        string zero=Folder(root,"zero");Partial(zero,asset.Name+".part",0);result=BrowserDownloadProgress.Read(zero,asset,since);
        check(result.Active&&result.ReceivedBytes==0&&result.Percent==0,"Parcial novo vazio pode indicar inicio com zero estimado");
        var unknown=new FixedAsset{Id=asset.Id,Name=asset.Name,Size=0,Sha256=asset.Sha256,ModId=asset.ModId,FileId=asset.FileId};result=BrowserDownloadProgress.Read(firefox,unknown,since);
        check(!result.Active,"Tamanho desconhecido nao resolve ambiguidade de nomes");
        result=BrowserDownloadProgress.Read(zero,unknown,since);check(result.Active&&result.TotalBytes==0&&!result.Percent.HasValue,"Sem tamanho exato ou estimativa explicita mostra somente bytes observados");
        string estimate=Folder(root,"estimate");Partial(estimate,asset.Name+".crdownload",1024);result=BrowserDownloadProgress.Read(estimate,unknown,since,2);
        check(result.Active&&result.TotalBytes==2048&&result.Percent==50&&result.Estimated,"TamanhoKiB publicado so entra por parametro explicito e porcentagem permanece estimada");
        result=BrowserDownloadProgress.Read(estimate,asset,since,2);check(result.TotalBytes==4096&&result.Percent==25,"Tamanho exato do pacote tem prioridade sobre estimativa publicada");
        var fallback=new FixedAsset{Id="fallback",Name="Alternative Skin Material",Size=0,Sha256=asset.Sha256,ModId=7341,FileId=160715};string generic=Folder(root,"generic");Partial(generic,"Alternative Skin Material 7341 1.6 random.zip.crdownload",1024);result=BrowserDownloadProgress.Read(generic,fallback,since,282);
        check(result.Active&&result.Estimated,"Nome sem extensao exige prefixo oficial e identificador numerico isolado do mod");
        string badGeneric=Folder(root,"bad-generic");Partial(badGeneric,"Alternative Skin Material 87341 1.6.zip.crdownload",1024);Partial(badGeneric,"Alternative Skin MaterialOutro 7341 1.6.zip.crdownload",1024);
        check(!BrowserDownloadProgress.Read(badGeneric,fallback,since).Active,"ID contido em outro numero e prefixo concatenado nao identificam parcial");
        refuse(()=>BrowserDownloadProgress.Read(@"\\servidor\share\downloads",asset,since),"Pasta UNC e recusada sem consultar rede");
        refuse(()=>BrowserDownloadProgress.Read(Path.GetPathRoot(root),asset,since),"Raiz de unidade nao e aceita como pasta de observacao");
        refuse(()=>BrowserDownloadProgress.Read(folder,asset,DateTime.SpecifyKind(since,DateTimeKind.Unspecified)),"Horario de autorizacao semUTC e recusado");
        refuse(()=>BrowserDownloadProgress.Read(folder,asset,since,-1),"Estimativa negativa e recusada");
        refuse(()=>BrowserDownloadProgress.Read(folder,new FixedAsset{Name="../fora.zip",Sha256=asset.Sha256,Size=1},since),"Identidade com fuga de caminho e recusada");
        refuse(()=>BrowserDownloadProgress.Read(folder,new FixedAsset{Name=asset.Name,Sha256="nao-verificado",Size=1},since),"Identidade semSHA fixado e recusada");
        string linkFolder=Folder(root,"links"),target=Partial(root,"alvo-sem-execucao.bin",1024),link=Path.Combine(linkFolder,asset.Name+".crdownload");bool linkCreated=CreateSymbolicLink(link,target,2)||CreateSymbolicLink(link,target,0);
        if(linkCreated)check(!BrowserDownloadProgress.Read(linkFolder,asset,since).Active,"Parcial que e link para fora da pasta e ignorado");
        check(!Directory.Exists(Path.Combine(root,"jogo"))&&!Directory.GetFiles(root,"*.exe",SearchOption.AllDirectories).Any(),"Helper nao instala, extrai, executa pacote ou cria pasta de jogo");
        Safe.WriteJson(Path.Combine(root,"resultado.json"),new{Passed=passed.Count,Tests=passed,ReadOnlyPartialMetadata=true,PartialContentRead=false,BrowserOrNetworkAccess=false,RealGameTouched=false,PercentAlwaysEstimated=true,SymlinkTest=linkCreated?"Executado":"Indisponivel sem privilegio; nao contabilizado"});File.WriteAllText(Path.Combine(parent,"ultimo-teste-browser-progress.txt"),root);return passed.Count;
    }
}
