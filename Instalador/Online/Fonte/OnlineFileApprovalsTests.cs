using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

public static class OnlineFileApprovalsTests {
    const string Registry=".CoopModsOnline/local-approvals/identidades.dat";
    static readonly Action<string> Noop=s=>{};
    static FixedCatalog Clone(FixedCatalog value){return Safe.Json.Deserialize<FixedCatalog>(Safe.Json.Serialize(value));}
    static void Put(string game,string path,string content){string target=Safe.In(game,path);Directory.CreateDirectory(Path.GetDirectoryName(target));File.WriteAllText(target,content,new UTF8Encoding(false));}
    static FileApprovalRequest Request(Exception ex){for(var e=ex;e!=null;e=e.InnerException)if(e is FileApprovalRequest)return (FileApprovalRequest)e;return null;}
    public static void Run(string parent){
        string root=Path.Combine(Path.GetFullPath(parent),"online-testes-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var passed=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("TESTE APROVACAO FALHOU: "+name);passed.Add(name);};
        Action<Action,string> refuse=(test,name)=>{bool refused=false;try{test();}catch(IOException){refused=true;}check(refused,name);};
        FixedAsset asset;var canonical=OnlineTests.MakeCatalog(root,out asset);var mod=canonical.Packages[1];var first=mod.Files[0];first.Size--;first.Sha256=new string('a',64);FixedData.Validate(canonical);
        string originalCatalog=Safe.Json.Serialize(canonical),game=OnlineTests.Fixture(root,"game");
        foreach(var file in canonical.Packages[0].Files)Put(game,file.Path,"test "+file.Path);
        var cache=new DownloadCache(Path.Combine(root,"cache"));cache.Import(Path.Combine(root,"fixture.zip"),asset);
        FileApprovalRequest request=null;
        try{new FixedInstaller(canonical,cache,game,null,true).Install(new[]{mod.Id},true,Noop);}catch(IOException ex){request=Request(ex);}
        check(request!=null&&request.ModId==mod.Id&&request.Path==first.Path&&request.Entry==first.Entry&&request.AssetSha256==asset.Sha256&&request.ExpectedSize==first.Size&&request.ActualSize==first.Size+1,"Pacote fixado com receita divergente gera pedido completo de revisão");
        check(!File.Exists(Safe.In(game,Registry))&&mod.Files.All(f=>!File.Exists(Safe.In(game,f.Path)))&&!Directory.GetFiles(Safe.In(game,".CoopModsOnline"),"*.json").Any(),"Recusar o pedido não cria aprovação, recibo de instalação nem arquivos do complemento");
        check(!Directory.GetDirectories(Path.Combine(cache.Root,"preparacao")).Any(),"Preparação recusada é limpa sem deixar payloads incompletos no cache");
        UserFileApprovals.Approve(canonical,game,request);
        check(File.Exists(Safe.In(game,Registry))&&mod.Files.All(f=>!File.Exists(Safe.In(game,f.Path))),"Autorizar registra a identidade mas não marca nem copia o mod como instalado");
        var resolved=UserFileApprovals.Resolve(mod,game,canonical.Id);
        check(resolved!=mod&&resolved.Files[0].Sha256==request.ActualSha256&&resolved.Files[0].Size==request.ActualSize&&resolved.Files[0].CanonicalSha256==first.Sha256&&resolved.Files[0].CanonicalSize==first.Size,"Overlay conserva identidade canônica e aplica tamanho e hash aprovados");
        check(Safe.Json.Serialize(canonical)==originalCatalog,"Catálogo canônico permanece byte a byte inalterado em memória");
        var engine=new FixedInstaller(canonical,cache,game,null,true);engine.Install(new[]{mod.Id},true,Noop);
        check(FixedData.Scan(mod,game).IsCorrect&&mod.Files.All(f=>File.Exists(Safe.In(game,f.Path))),"Nova tentativa instala arquivos aprovados pelo motor existente");
        check(UserFileApprovals.GetWarning(mod,game)!=null,"Instalação aprovada mantém aviso visível e instrução de remoção");
        var reopened=Clone(canonical);check(FixedData.Scan(reopened.Packages[1],game).IsCorrect&&UserFileApprovals.ResolveCatalog(reopened,game).Packages[1].Files[0].CanonicalSize==first.Size,"Reabertura a partir do catálogo original reconhece a aprovação local");
        engine=new FixedInstaller(reopened,cache,game,null,true);engine.Deactivate(mod.Id,Noop);
        check(mod.Files.All(f=>!File.Exists(Safe.In(game,f.Path)))&&UserFileApprovals.GetWarning(mod,game)==null,"Desativação individual retira somente os arquivos registrados e elimina aviso ativo");
        engine.Undo(Noop);check(FixedData.Scan(mod,game).IsCorrect,"Desfazer desativação restaura identidade aprovada");
        engine.DeactivateMany(new[]{mod.Id},Noop);check(mod.Files.All(f=>!File.Exists(Safe.In(game,f.Path))),"Botão Remover selecionados aceita identidade aprovada");
        engine.Undo(Noop);check(FixedData.Scan(mod,game).IsCorrect,"Desfazer remoção em lote restaura bytes aprovados");
        engine.Undo(Noop);check(mod.Files.All(f=>!File.Exists(Safe.In(game,f.Path))),"Desfazer instalação original remove complemento aprovado e preserva base");
        check(canonical.Packages[0].Files.All(f=>File.Exists(Safe.In(game,f.Path))),"Base imutável preservada em instalação, remoção e reversão");
        engine=new FixedInstaller(canonical,cache,game,null,true);engine.Install(new[]{mod.Id},true,Noop);Put(game,first.Path,"alteração posterior do usuário");engine.Undo(Noop);
        check(File.ReadAllText(Safe.In(game,first.Path))=="alteração posterior do usuário"&&!File.Exists(Safe.In(game,mod.Files[1].Path)),"Reversão preserva edição desconhecida posterior e remove apenas arquivo próprio intacto");
        refuse(()=>UserFileApprovals.Approve(canonical,game,request),"Autorização não sobrescreve destino existente");
        string clean=OnlineTests.Fixture(root,"clean");
        var immutable=canonical.Packages[0];var immutableRequest=new FileApprovalRequest(immutable,immutable.Assets[0],immutable.Files[0],immutable.Files[0].Size+1,new string('b',64));
        refuse(()=>UserFileApprovals.Approve(canonical,clean,immutableRequest),"Base NightCoop e RED4ext não permitem autorização manual");
        var protectedCatalog=Clone(canonical);protectedCatalog.Packages[1].Files[0].UserData=true;refuse(()=>UserFileApprovals.Approve(protectedCatalog,clean,request),"Perfis pessoais não permitem exceção de código");
        protectedCatalog=Clone(canonical);protectedCatalog.Packages[1].Files[0].Patch=new FixedTextPatch();refuse(()=>UserFileApprovals.Approve(protectedCatalog,clean,request),"Patches FPV não permitem substituir validação determinística por aprovação genérica");
        protectedCatalog=Clone(canonical);protectedCatalog.Packages[1].DensityProfiles=new List<FixedDensityProfile>();refuse(()=>UserFileApprovals.Approve(protectedCatalog,clean,request),"Variantes de densidade não aceitam exceção de arquivo");
        protectedCatalog=Clone(canonical);protectedCatalog.Packages[1].Assets[0].Sha256=new string('c',64);refuse(()=>UserFileApprovals.Approve(protectedCatalog,clean,request),"Aprovação não pode ser reutilizada para outro pacote de origem");
        protectedCatalog=Clone(canonical);protectedCatalog.Packages[1].Files[0].Entry="outro/arquivo.archive";refuse(()=>UserFileApprovals.Approve(protectedCatalog,clean,request),"Aprovação não pode ser transferida para outra entrada");
        var cross=UserFileApprovals.ResolveCatalog(UserFileApprovals.ResolveCatalog(canonical,game),clean);check(cross.Packages[1].Files[0].Sha256==first.Sha256&&cross.Packages[1].Files[0].CanonicalSha256==null,"Overlay de outro jogo não vaza para pasta sem autorização");
        string registry=Safe.In(game,Registry),copied=Safe.In(clean,Registry);Directory.CreateDirectory(Path.GetDirectoryName(copied));File.Copy(registry,copied);
        refuse(()=>UserFileApprovals.ResolveCatalog(canonical,clean),"Registro copiado para outro jogo é recusado mesmo na mesma conta Windows");
        File.WriteAllText(copied,"corrompido");refuse(()=>UserFileApprovals.ResolveCatalog(canonical,clean),"Registro corrompido falha fechado sem aceitar bytes desconhecidos");
        File.WriteAllBytes(copied,new byte[1048577]);refuse(()=>UserFileApprovals.ResolveCatalog(canonical,clean),"Registro acima de 1 MiB recusado antes de decodificar");
        var excessive=new ApprovedFileRegistry{CatalogId=canonical.Id,GameRoot=clean};
        for(int i=0;i<257;i++)excessive.Files.Add(new ApprovedFileIdentity());
        byte[] entropy=(byte[])typeof(UserFileApprovals).GetField("Entropy",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        Safe.WriteJson(copied,new ReceiptEnvelope{Protected=Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(Safe.Json.Serialize(excessive)),entropy,DataProtectionScope.CurrentUser))});
        refuse(()=>UserFileApprovals.ResolveCatalog(canonical,clean),"Mais de 256 registros DPAPI também são recusados");
        var wrongCatalog=Clone(canonical);wrongCatalog.Id="outra-lista";refuse(()=>UserFileApprovals.ResolveCatalog(wrongCatalog,game),"Autorizações de outro catálogo não são aplicadas");
        var old=new OnlineReceipt{Id=Guid.NewGuid().ToString("N"),CatalogId=canonical.Id,Status="instalado",CreatedUtc=DateTime.UtcNow.ToString("o"),Mods=new List<string>{mod.Id},Files=new List<OwnedChange>{new OwnedChange{Path=first.Path,After=first.Sha256,Applied=true}}};
        string oldPath=Safe.In(game,".CoopModsOnline/"+old.Id+".json");Safe.WriteJson(oldPath,new ReceiptEnvelope{Protected=Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(Safe.Json.Serialize(old)),null,DataProtectionScope.CurrentUser))});
        var read=typeof(FixedInstaller).GetMethod("ReadReceipt",BindingFlags.Instance|BindingFlags.NonPublic);check(read.Invoke(new FixedInstaller(canonical,cache,game,null,true),new object[]{oldPath})!=null,"Recibo antigo com identidade canônica continua legível após aprovação");
        string historyGame=OnlineTests.Fixture(root,"historico");foreach(var file in canonical.Packages[0].Files)Put(historyGame,file.Path,"test "+file.Path);
        UserFileApprovals.Approve(canonical,historyGame,request);
        UserFileApprovals.Approve(canonical,historyGame,new FileApprovalRequest(mod,asset,first,request.ActualSize+2,new string('e',64)));
        var historicEngine=new FixedInstaller(canonical,cache,historyGame,null,true);historicEngine.Install(new[]{mod.Id},true,Noop);
        check(Safe.Hash(Safe.In(historyGame,first.Path))==request.ActualSha256&&FixedData.Scan(mod,historyGame).IsCorrect,"Extrator pode produzir identidade histórica aprovada sem comparar apenas a aprovação mais recente");
        historicEngine.Undo(Noop);check(mod.Files.All(f=>!File.Exists(Safe.In(historyGame,f.Path))),"Recibo usa os bytes realmente preparados e permite desfazer uma identidade histórica");
        Safe.WriteJson(Path.Combine(root,"resultado-aprovacoes.json"),new{Passed=passed.Count,Tests=passed,FixtureOnly=true,RealGameTouched=false});File.WriteAllText(Path.Combine(parent,"ultimo-teste-aprovacoes.txt"),root);
    }
}
