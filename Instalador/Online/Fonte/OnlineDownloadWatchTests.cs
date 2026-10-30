using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

public static class OnlineDownloadWatchTests {
    static FixedAsset Asset(string file){return new FixedAsset{Id="a0",Name=Path.GetFileName(file),Size=new FileInfo(file).Length,Sha256=Safe.Hash(file),ModId=123,FileId=456};}
    public static int Run(string parent) {
        string root=Path.Combine(Path.GetFullPath(parent),"watch-"+Guid.NewGuid().ToString("N").Substring(0,12));Directory.CreateDirectory(root);
        string folder=Path.Combine(root,"downloads");Directory.CreateDirectory(folder);
        var passed=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("TESTE WATCH FALHOU: "+name);passed.Add(name);};
        Action<Action,string> refuse=(action,name)=>{bool failed=false;try{action();}catch(IOException){failed=true;}check(failed,name);};
        string source=Path.Combine(root,"original.zip");File.WriteAllText(source,"pacote original da fixture",new UTF8Encoding(false));var asset=Asset(source);string originalHash=Safe.Hash(source);
        var watch=new OnlineDownloadWatch(folder);DateTime now=new DateTime(2026,10,28,23,0,0,DateTimeKind.Utc);
        check(!watch.IsArmed&&watch.Poll(now)==null,"Monitor sem autorizacao nao procura downloads");
        watch.Arm(new[]{asset});check(watch.IsArmed&&watch.Poll(now)==null,"Armar arquivo exato aguarda download");
        string partial=Path.Combine(folder,"original.zip.crdownload");File.Copy(source,partial);check(watch.Poll(now.AddSeconds(4))==null,"Download parcial nao e reconhecido pelo conteudo");
        foreach(string ext in new[]{".part",".tmp",".exe"})File.Copy(source,Path.Combine(folder,"recusado"+ext));check(watch.Poll(now.AddSeconds(4))==null,"Extensoes part, tmp e executavel nunca sao importadas pelo monitor");
        string downloaded=Path.Combine(folder,"original (1).zip");File.Move(partial,downloaded);
        check(watch.Poll(now.AddSeconds(5))==null&&watch.Poll(now.AddSeconds(6))==null,"Renomeacao final precisa permanecer estavel por dois segundos");
        var match=watch.Poll(now.AddSeconds(7));check(match!=null&&match.Path==downloaded&&match.Asset.Sha256==asset.Sha256,"Arquivo concluido e reconhecido por SHA apesar do sufixo do navegador");
        check(watch.Poll(now.AddSeconds(10))==null,"Mesmo pacote so e entregue uma vez por autorizacao");
        check(File.Exists(downloaded)&&Safe.Hash(source)==originalHash&&Safe.Hash(downloaded)==originalHash,"Reconhecimento preserva pacote original e download");
        watch.Arm(new[]{asset});check(watch.Poll(now.AddSeconds(11))==null&&watch.Poll(now.AddSeconds(13))!=null,"Pacote completo preexistente exato tambem pode ser reconhecido apos estabilidade");
        watch.Cancel();check(!watch.IsArmed&&watch.Poll(now.AddSeconds(15))==null,"Cancelar desarma a espera e nao reconhece pacote posterior");
        string other=Path.Combine(folder,"outra.zip");File.WriteAllText(other,new string('x',(int)asset.Size),new UTF8Encoding(false));File.Delete(downloaded);watch.Arm(new[]{asset});watch.Poll(now.AddSeconds(16));check(watch.Poll(now.AddSeconds(18))==null,"Tamanho identico com hash divergente e ignorado");
        File.Copy(source,other,true);watch.Poll(now.AddSeconds(19));check(watch.Poll(now.AddSeconds(21))!=null,"Arquivo antes divergente pode ficar pronto depois de alterado e estabilizado");
        File.Delete(other);string child=Path.Combine(folder,"subpasta");Directory.CreateDirectory(child);File.Copy(source,Path.Combine(child,"original.zip"));watch.Arm(new[]{asset});watch.Poll(now.AddSeconds(22));check(watch.Poll(now.AddSeconds(25))==null,"Monitor nao percorre subpastas nem outras pastas");
        File.Copy(source,other);watch.Arm(new[]{asset});watch.Poll(now.AddSeconds(26));using(var locked=new FileStream(other,FileMode.Open,FileAccess.ReadWrite,FileShare.Read)){check(watch.Poll(now.AddSeconds(28))==null,"Arquivo ainda aberto para gravacao nao e aceito");}check(watch.Poll(now.AddSeconds(29))!=null,"Arquivo liberado e reconhecido na proxima verificacao");
        watch.Arm(new[]{asset});asset.Sha256=new string('0',64);watch.Poll(now.AddSeconds(30));check(watch.Poll(now.AddSeconds(32))!=null,"Alterar objeto do chamador nao altera identidade ja autorizada");
        asset=Asset(source);watch.Arm(new[]{asset});watch.Poll(now.AddSeconds(33));bool reported=false;match=watch.Poll(now.AddSeconds(35),(done,total)=>{reported=done==total&&total==asset.Size;watch.Cancel();});check(reported&&match==null&&!watch.IsArmed,"Progresso reportado permite cancelar durante hash sem entregar arquivo da autorizacao antiga");
        var empty=new OnlineDownloadWatch(folder);refuse(()=>empty.Arm(new FixedAsset[0]),"Armar lista vazia e recusado");refuse(()=>empty.Arm(new[]{new FixedAsset{Size=1,Sha256="hash-invalido"}}),"Identidade sem SHA valido e recusada");
        refuse(()=>empty.Arm(new[]{new FixedAsset{Size=8L*1024*1024*1024+1,Sha256=asset.Sha256}}),"Identidade acima do limite de 8GB e recusada sem ler arquivo");
        refuse(()=>empty.Arm(new[]{asset,new FixedAsset{Size=asset.Size+1,Sha256=asset.Sha256}}),"Mesmo SHA com tamanhos divergentes e recusado");
        refuse(()=>new OnlineDownloadWatch(@"\\server\share\downloads"),"Pasta UNC e recusada sem acessar rede");refuse(()=>new OnlineDownloadWatch(Path.GetPathRoot(root)),"Raiz de unidade nao e aceita como pasta de monitoramento");refuse(()=>new OnlineDownloadWatch(Path.Combine(root,"inexistente")),"Pasta inexistente requer escolha explicita");
        check(Directory.GetFiles(root,"*.exe",SearchOption.AllDirectories).Length==1&&Safe.Hash(Path.Combine(folder,"recusado.exe"))==originalHash&&!Directory.Exists(Path.Combine(folder,".CoopModsOnline")),"Monitor nao extrai, executa nem instala arquivos; entrada exe da fixture preservada");
        Safe.WriteJson(Path.Combine(root,"resultado.json"),new{Passed=passed.Count,Tests=passed,NetworkUsed=false,RealGameTouched=false,ReadOnlyMonitoring=true});File.WriteAllText(Path.Combine(parent,"ultimo-teste-watch.txt"),root);return passed.Count;
    }
}
