using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

public static class OnlineTests {
    static readonly List<string> passed=new List<string>();
    static void Check(bool value,string name){if(!value)throw new Exception("TESTE FALHOU: "+name);passed.Add(name);}
    static void Refuse(Action action,string name){bool refused=false;try{action();}catch(Exception){refused=true;}Check(refused,name);}
    static string HashText(string value){using(var h=SHA256.Create())return Safe.Hex(h.ComputeHash(Encoding.UTF8.GetBytes(value)));}
    static void Text(string root,string relative,string text){string p=Safe.In(root,relative);Directory.CreateDirectory(Path.GetDirectoryName(p));File.WriteAllText(p,text,new UTF8Encoding(false));}
    static string NewDir(string root,string name){string p=Safe.In(root,name+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(p);return p;}
    internal static string Fixture(string root,string name,bool unsupported=false){string game=NewDir(root,name);string exe=Safe.In(game,"bin/x64/Cyberpunk2077.exe");Directory.CreateDirectory(Path.GetDirectoryName(exe));File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..",unsupported?"FixtureUnsupported.exe":"FixtureGame.exe"),exe);Text(game,"archive/pc/ep1/ep1.addcont_keystone","");Text(game,".fixture-only","test");return game;}
    static FixedCatalog Clone(FixedCatalog c){return Safe.Json.Deserialize<FixedCatalog>(Safe.Json.Serialize(c));}
    internal static FixedCatalog MakeCatalog(string root,out FixedAsset asset) {
        string[] basePaths={"bin/x64/winmm.dll","red4ext/RED4ext.dll","red4ext/LICENSE.txt","red4ext/THIRD_PARTY_LICENSES.txt","red4ext/plugins/NightCoop/NightCoop.dll"};
        var files=basePaths.Concat(new[]{"archive/pc/mod/test-a.archive","r6/tweaks/test-b.yaml"}).Select(p=>new FixedFile{Asset="a0",Entry=p,Path=p,Sha256=HashText("test "+p),Size=Encoding.UTF8.GetByteCount("test "+p)}).ToList();
        string archive=Safe.In(root,"fixture.zip");using(var z=ZipFile.Open(archive,ZipArchiveMode.Create))foreach(var f in files){var e=z.CreateEntry(f.Entry);using(var w=new StreamWriter(e.Open(),new UTF8Encoding(false)))w.Write("test "+f.Path);}
        asset=new FixedAsset{Id="a0",Name="fixture.zip",Format="zip",Url="https://github.com/example/project/releases/download/fixed/fixture.zip",Size=new FileInfo(archive).Length,Sha256=Safe.Hash(archive)};
        return new FixedCatalog{Schema=1,Id="fixture",GameVersion="2.5",GameFileVersion="2.5.0.0",Packages=new List<FixedMod>{new FixedMod{Id="NightCoop",Name="Base de teste",Version="test",Profile="Kit inicial",GameVersion="2.5",RequiresPL=true,Immutable=true,Dependencies=new string[0],Assets=new List<FixedAsset>{asset},Files=files.Take(5).ToList()},new FixedMod{Id="Extra",Name="Mod de teste",Version="test",Profile="Complementos",GameVersion="2.5",RequiresPL=true,Dependencies=new[]{"NightCoop"},Assets=new List<FixedAsset>{asset},Files=files.Skip(5).ToList()}}};
    }
    public static void Run(string parent) {
        var embedded=FixedData.Load();Check(embedded.Id=="cp25-fixed-20261028"&&embedded.Packages.Count==85&&embedded.Packages.Any(m=>m.Id=="face-35807-168171")&&embedded.Packages.Any(m=>m.Id=="RadioExt")&&embedded.Packages.Any(m=>m.Id=="SpotOnRadio")&&new[]{"mod-11963","mod-15606","mod-15867","mod-11964"}.All(id=>embedded.Packages.Any(m=>m.Id==id))&&FacialDownload.List().All(item=>embedded.Packages.Any(m=>m.Id==item.Id)),"Catálogo85 embutido conferido por hash preserva faciais, RadioExt e Spotify e inclui os quatro pacotes Nola sem arquivo externo");
        var additions=new[]{"mod-35772","mod-35353","mod-36378","mod-35886"}.Select(id=>embedded.Packages.Single(m=>m.Id==id)).ToList();
        Check(additions.Sum(m=>m.Files.Count)==8&&additions.All(m=>!m.Immutable&&!m.RequiresPL&&!m.RequiresRedmod&&m.Assets.Count==1),"Quatro novos mods: oito arquivos fixados sem alterar corpo ou base");
        Check(additions.Select(m=>m.Assets[0].FileId).SequenceEqual(new[]{168073,168808,170886,168484}),"Downloads oficiais dos quatro mods mantem os IDs auditados");
        Check(new HashSet<string>(additions[0].Dependencies).SetEquals(new[]{"ArchiveXL","TweakXL"})&&new HashSet<string>(additions[1].Dependencies).SetEquals(new[]{"ArchiveXL","TweakXL"})&&new HashSet<string>(additions[2].Dependencies).SetEquals(new[]{"NightCoop","redscript","ModSettings"})&&additions[3].Dependencies.SequenceEqual(new[]{"redscript"}),"Dependencias dos novos mods reutilizam somente a base existente");
        Check(GameEdition.Status(additions[0])=="unverified"&&GameEdition.NeedsPL(additions[0])&&additions.Skip(1).All(m=>!GameEdition.NeedsPL(m)),"EXtra restrito ao perfil comPL; outros tres elegiveis semPL");
        Refuse(()=>GameEdition.RequireAllowed(embedded,new[]{"mod-35772"},false),"EXtra semDLC nao e tratado como validado");
        GameEdition.RequireAllowed(embedded,additions.Skip(1).Select(m=>m.Id),false);Check(true,"Closure dos outros tres mods elegivel semDLC");
        Check(Path.IsPathRooted(Discovery.Saves())&&!Discovery.Saves().StartsWith(@"\\",StringComparison.Ordinal),"Pasta de saves descoberta e absoluta, nunca um caminho relativo silenciosamente ignorado");
        string root=NewDir(Path.GetFullPath(parent),"online-testes");FixedAsset asset;var c=MakeCatalog(root,out asset);var mod=c.Packages[1];var noop=new Action<string>(s=>{});
        FixedData.Validate(c);Check(true,"Catálogo e base de cinco arquivos válidos");
        var bad=Clone(c);bad.Packages[1].Files[0].Path="../fora.txt";Refuse(()=>FixedData.Validate(bad),"Traversal recusado");
        bad=Clone(c);bad.Packages[1].Files[0].Path=bad.Packages[0].Files[0].Path;Refuse(()=>FixedData.Validate(bad),"Colisão com a base recusada");
        bad=Clone(c);bad.Packages[0].Dependencies=new[]{"Extra"};Refuse(()=>FixedData.Validate(bad),"Ciclo de dependências recusado");
        bad=Clone(c);bad.Packages[1].Dependencies=new[]{"Desconhecido"};Refuse(()=>FixedData.Validate(bad),"Dependência desconhecida recusada");
        bad=Clone(c);bad.Packages[1].RequiresRedmod=true;Refuse(()=>FixedData.Validate(bad),"Mod que exige REDmod não entra nesta lista");
        bad=Clone(c);bad.Packages[0].Files[0].Path="bin/x64/outro.dll";Refuse(()=>FixedData.Validate(bad),"Base incompleta ou trocada recusada");
        Check(FixedData.Expand(c,new[]{"Extra"}).Select(m=>m.Id).SequenceEqual(new[]{"NightCoop","Extra"}),"Dependências entram antes do complemento");
        Check(NexusClient.Allowed(new Uri("https://files.nexus-cdn.com/file.zip"),true)&&!NexusClient.Allowed(new Uri("https://nexus-cdn.com.evil.invalid/file"),true),"Host CDN validado por domínio");
        Check(!NexusClient.Allowed(new Uri("http://github.com/file"),true)&&!NexusClient.Allowed(new Uri("https://user:pass@github.com/file"),true),"HTTP e credenciais em URL recusados");
        string game=Fixture(root,"estado");Check(FixedData.GameVersion(game)=="2.5","Versão lida do executável fixture");FixedData.ValidateGame(game,c.Packages,c.GameFileVersion);
        Refuse(()=>FixedData.ValidateGame(game,c.Packages,"2.5.0.99"),"Outro build com a mesma versão 2.5 bloqueado");
        string wrong=Fixture(root,"atualizado",true);Refuse(()=>FixedData.ValidateGame(wrong,c.Packages,c.GameFileVersion),"Jogo atualizado 2.6 bloqueado");Check(!Directory.Exists(Path.Combine(wrong,".CoopModsOnline")),"Verificar versão não escreve no jogo");
        Check(FixedData.Scan(mod,game).State=="Não instalado","Mod ausente identificado");Text(game,mod.Files[0].Path,"test "+mod.Files[0].Path);Check(FixedData.Scan(mod,game).State=="Parcial","Instalação manual parcial identificada");Text(game,mod.Files[1].Path,"test "+mod.Files[1].Path);Check(FixedData.Scan(mod,game).IsCorrect,"Instalação manual correta reconhecida sem recibo");Text(game,mod.Files[1].Path,"edição do usuário");Check(FixedData.Scan(mod,game).State=="Diferente/alterado","Edição não confundida com versão compatível");
        string cacheDir=NewDir(root,"cache");var cache=new DownloadCache(cacheDir);string archive=Path.Combine(root,"fixture.zip");cache.Import(archive,asset);Check(cache.Contains(asset),"Arquivo original importado por hash");
        string broken=Path.Combine(root,"truncado.zip");File.WriteAllText(broken,"incompleto");Refuse(()=>cache.Import(broken,asset),"Download truncado recusado");string partial=Path.Combine(root,"fixture.crdownload");File.Copy(archive,partial);Refuse(()=>cache.Import(partial,asset),"Download em andamento recusado");
        string stage=NewDir(root,"stage");cache.Stage(mod,stage,noop);Check(mod.Files.All(f=>Safe.Hash(Safe.In(stage,f.Path))==f.Sha256),"Extração segue receita e hashes");
        var tarMod=Clone(c).Packages[1];tarMod.Assets[0].Format="tar";string tarStage=NewDir(root,"stage-tar");cache.Stage(tarMod,tarStage,noop);Check(tarMod.Files.All(f=>Safe.Hash(Safe.In(tarStage,f.Path))==f.Sha256),"Leitura tar de arquivos regulares verificada por hash");
        var tampered=Clone(c).Packages[1];tampered.Files[0].Sha256=new string('0',64);Refuse(()=>cache.Stage(tampered,NewDir(root,"conteudo-invalido"),noop),"Conteúdo divergente da receita recusado");
        foreach(string kind in new[]{"traversal","duplicate","symlink"}) {
            string zipPath=Safe.In(root,kind+".zip");using(var z=ZipFile.Open(zipPath,ZipArchiveMode.Create)){var entry=z.CreateEntry(kind=="traversal"?"../escape.txt":mod.Files[0].Entry);using(var w=new StreamWriter(entry.Open()))w.Write("test");if(kind=="duplicate")z.CreateEntry(mod.Files[0].Entry);if(kind=="symlink")entry.ExternalAttributes=unchecked((int)0xA1FF0000);}
            var ca=Clone(c).Packages[1];ca.Assets[0].Sha256=Safe.Hash(zipPath);ca.Assets[0].Size=new FileInfo(zipPath).Length;cache.Import(zipPath,ca.Assets[0]);Refuse(()=>cache.Stage(ca,NewDir(root,"bad-"+kind),noop),"ZIP "+kind+" recusado");if(kind=="symlink"){ca.Assets[0].Format="tar";Refuse(()=>cache.Stage(ca,NewDir(root,"bad-tar-link"),noop),"Tar recusa metadados de link antes de extrair");}
        }
        string conflictGame=Fixture(root,"conflito");Text(conflictGame,mod.Files[0].Path,"arquivo alheio");var conflictEngine=new FixedInstaller(c,cache,conflictGame,null,true);Refuse(()=>conflictEngine.Install(new[]{"Extra"},true,noop),"Conflito bloqueia instalação");Check(File.ReadAllText(Safe.In(conflictGame,mod.Files[0].Path))=="arquivo alheio","Arquivo alheio preservado no conflito");
        string mutation=Fixture(root,"instalar");foreach(var f in c.Packages[0].Files)Text(mutation,f.Path,"test "+f.Path);Text(mutation,"archive/pc/mod/alheio.archive","alheio");string saves=NewDir(root,"saves-falsos");Text(saves,"slot/sav.dat","save de teste");var engine=new FixedInstaller(c,cache,mutation,saves,true);
        Refuse(()=>engine.Install(new[]{"Extra"},false,noop),"Plano de instalação precisa de confirmação explícita");engine.Install(new[]{"Extra"},true,noop);Check(FixedData.Scan(mod,mutation).IsCorrect,"Instalação em fixture concluída");
        Check(Directory.GetFiles(Path.Combine(mutation,".CoopModsOnline","backups"),"sav.dat",SearchOption.AllDirectories).Length==1,"Backup dos saves de teste realizado");
        int receipts=Directory.GetFiles(Path.Combine(mutation,".CoopModsOnline"),"*.json").Length;engine.Install(new[]{"Extra"},true,noop);Check(receipts==Directory.GetFiles(Path.Combine(mutation,".CoopModsOnline"),"*.json").Length,"Repetição idêntica não cria outra transação");
        Text(mutation,mod.Files[0].Path,"editado depois");engine.Undo(noop);Check(File.ReadAllText(Safe.In(mutation,mod.Files[0].Path))=="editado depois"&&!File.Exists(Safe.In(mutation,mod.Files[1].Path)),"Desfazer preserva edição posterior e retira somente arquivo próprio");Check(c.Packages[0].Files.All(f=>File.Exists(Safe.In(mutation,f.Path)))&&File.ReadAllText(Safe.In(mutation,"archive/pc/mod/alheio.archive"))=="alheio","Desfazer preserva base manual e mod alheio");Check(File.ReadAllText(Path.Combine(saves,"slot","sav.dat"))=="save de teste","Desfazer não reverte saves atuais");
        string failure=Fixture(root,"rollback");var failEngine=new FixedInstaller(c,cache,failure,null,true);Refuse(()=>failEngine.Install(new[]{"Extra"},true,noop,1),"Falha simulada na aplicação");Check(!c.Packages.SelectMany(m=>m.Files).Any(f=>File.Exists(Safe.In(failure,f.Path))),"Rollback retira só arquivos aplicados antes da falha");
        string repair=Fixture(root,"reparar");foreach(var f in c.Packages[0].Files.Concat(mod.Files.Take(1)))Text(repair,f.Path,"test "+f.Path);var repairEngine=new FixedInstaller(c,cache,repair,null,true);repairEngine.Install(new[]{"Extra"},true,noop);Check(FixedData.Scan(mod,repair).IsCorrect,"Instalação parcial completada");repairEngine.Undo(noop);Check(File.Exists(Safe.In(repair,mod.Files[0].Path))&&!File.Exists(Safe.In(repair,mod.Files[1].Path)),"Reparo desfeito preserva o arquivo manual anterior");
        string gap=Fixture(root,"interrupcao");Text(gap,mod.Files[0].Path,"test "+mod.Files[0].Path);var gapReceipt=new OnlineReceipt{Id=Guid.NewGuid().ToString("N"),CatalogId=c.Id,Status="instalando",CreatedUtc=DateTime.UtcNow.ToString("o"),Mods=new List<string>{"Extra"},Files=new List<OwnedChange>{new OwnedChange{Path=mod.Files[0].Path,After=mod.Files[0].Sha256,Applied=false}}};string gapLedger=Safe.In(gap,".CoopModsOnline/"+gapReceipt.Id+".json");Safe.WriteJson(gapLedger,new ReceiptEnvelope{Protected=Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(Safe.Json.Serialize(gapReceipt)),null,DataProtectionScope.CurrentUser))});new FixedInstaller(c,cache,gap,null,true).Undo(noop);Check(File.Exists(Safe.In(gap,mod.Files[0].Path)),"Desfazer preserva arquivo cuja autoria ficou incerta após interrupção");
        string forged=Fixture(root,"registro");var forgeryEngine=new FixedInstaller(c,cache,forged,null,true);forgeryEngine.Install(new[]{"Extra"},true,noop);string ledger=Directory.GetFiles(Path.Combine(forged,".CoopModsOnline"),"*.json").Single();File.WriteAllText(ledger,"{\"Protected\":\"alterado\"}");Refuse(()=>forgeryEngine.Undo(noop),"Registro adulterado bloqueia desfazer");Check(FixedData.Scan(mod,forged).IsCorrect,"Registro adulterado não remove arquivos");
        var noCache=new DownloadCache(NewDir(root,"vazio"));string needs=Fixture(root,"sem-cache");Refuse(()=>new FixedInstaller(c,noCache,needs,null,true).Install(new[]{"Extra"},true,noop),"Dependência sem arquivo validado bloqueia instalação");Check(!c.Packages.SelectMany(m=>m.Files).Any(f=>File.Exists(Safe.In(needs,f.Path))),"Falha no staging não aplica arquivos");
        string disabled=Fixture(root,"desativar");foreach(var f in c.Packages.SelectMany(m=>m.Files))Text(disabled,f.Path,"test "+f.Path);Text(disabled,"archive/pc/mod/terceiro.archive","terceiro");var disableEngine=new FixedInstaller(c,cache,disabled,null,true);
        Refuse(()=>disableEngine.Deactivate("NightCoop",noop),"Desativação da base imutável bloqueada");
        disableEngine.Deactivate("Extra",noop);Check(mod.Files.All(f=>!File.Exists(Safe.In(disabled,f.Path)))&&File.ReadAllText(Safe.In(disabled,"archive/pc/mod/terceiro.archive"))=="terceiro","Desativação guarda só os arquivos do mod e preserva terceiros");
        disableEngine.Undo(noop);Check(FixedData.Scan(mod,disabled).IsCorrect,"Desfazer restaura mod desativado manualmente instalado");
        Refuse(()=>disableEngine.Deactivate("Extra",noop,1),"Falha simulada ao desativar");Check(FixedData.Scan(mod,disabled).IsCorrect,"Rollback da desativação restaura arquivos movidos");
        disableEngine.Deactivate("Extra",noop);Text(disabled,mod.Files[0].Path,"novo arquivo alheio");disableEngine.Undo(noop);Check(File.ReadAllText(Safe.In(disabled,mod.Files[0].Path))=="novo arquivo alheio"&&File.Exists(Safe.In(disabled,mod.Files[1].Path)),"Reativação preserva destino ocupado e restaura os demais");
        Refuse(()=>disableEngine.Install(new[]{"Extra"},true,noop),"Restauração pendente impede operação nova");File.Delete(Safe.In(disabled,mod.Files[0].Path));disableEngine.Undo(noop);Check(FixedData.Scan(mod,disabled).IsCorrect,"Restauração pode ser retomada após resolver conflito");
        Text(disabled,mod.Files[0].Path,"edição manual");Refuse(()=>disableEngine.Deactivate("Extra",noop),"Mod alterado não é removido pela desativação");Check(File.ReadAllText(Safe.In(disabled,mod.Files[0].Path))=="edição manual","Desativação recusada preserva edição manual");
        var depsCatalog=Clone(c);var child=Clone(c).Packages[1];child.Id="Child";child.Name="Filho";child.Dependencies=new[]{"Extra"};child.Files=new List<FixedFile>{new FixedFile{Asset="a0",Entry="r6/scripts/child.reds",Path="r6/scripts/child.reds",Sha256=HashText("filho"),Size=5}};depsCatalog.Packages.Add(child);Text(disabled,child.Files[0].Path,"filho");Text(disabled,mod.Files[0].Path,"test "+mod.Files[0].Path);
        Refuse(()=>new FixedInstaller(depsCatalog,cache,disabled,null,true).Deactivate("Extra",noop),"Componente usado por outro mod instalado não é desativado");Check(FixedData.Scan(mod,disabled).IsCorrect,"Dependente bloqueia antes de mover arquivos");
        string undoDependent=Fixture(root,"desfazer-dependencia");var undoDependentEngine=new FixedInstaller(depsCatalog,cache,undoDependent,null,true);undoDependentEngine.Install(new[]{"Extra"},true,noop);Text(undoDependent,child.Files[0].Path,"filho");Refuse(()=>undoDependentEngine.Undo(noop),"Desfazer não quebra dependente instalado manualmente depois");Check(FixedData.Scan(mod,undoDependent).IsCorrect,"Desfazer bloqueado preserva o componente usado");File.Delete(Safe.In(undoDependent,child.Files[0].Path));undoDependentEngine.Undo(noop);Check(!mod.Files.Any(f=>File.Exists(Safe.In(undoDependent,f.Path))),"Desfazer prossegue após retirar o dependente externo");
        string disabledGap=Fixture(root,"desativacao-interrompida");var dr=new OnlineReceipt{Id=Guid.NewGuid().ToString("N"),CatalogId=c.Id,Status="desativando",Operation="deactivate",CreatedUtc=DateTime.UtcNow.ToString("o"),Mods=new List<string>{"Extra"},Files=new List<OwnedChange>{new OwnedChange{Path=mod.Files[0].Path,Before=mod.Files[0].Sha256,Applied=false}}};
        Text(disabledGap,".CoopModsOnline/backups/"+dr.Id+"/desativados/"+mod.Files[0].Path,"test "+mod.Files[0].Path);Safe.WriteJson(Safe.In(disabledGap,".CoopModsOnline/"+dr.Id+".json"),new ReceiptEnvelope{Protected=Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(Safe.Json.Serialize(dr)),null,DataProtectionScope.CurrentUser))});new FixedInstaller(c,cache,disabledGap,null,true).Undo(noop);Check(File.Exists(Safe.In(disabledGap,mod.Files[0].Path)),"Desativação interrompida restaura quarentena verificada sem Applied");
        var heldCatalog=Clone(c);heldCatalog.Packages[1].BlockedReason="Falha de registro reproduzida";FixedData.Validate(heldCatalog);
        string heldGame=Fixture(root,"retido");Refuse(()=>new FixedInstaller(heldCatalog,cache,heldGame,null,true).Install(new[]{"Extra"},true,noop),"Mod retido não pode ser reinstalado mesmo com cache correto");Check(!Directory.Exists(Path.Combine(heldGame,".CoopModsOnline")),"Bloqueio de compatibilidade ocorre antes de alterar jogo");
        var heldEngine=new FixedInstaller(c,cache,heldGame,null,true);heldEngine.Install(new[]{"Extra"},true,noop);heldEngine.Deactivate("Extra",noop);Refuse(()=>new FixedInstaller(heldCatalog,cache,heldGame,null,true).Undo(noop),"Desfazer não reativa mod que passou a estar retido");Check(!mod.Files.Any(f=>File.Exists(Safe.In(heldGame,f.Path))),"Tentativa de reativação bloqueada preserva quarentena");
        var conflicts=Clone(c);var guarded=conflicts.Packages[1];
        guarded.IncompatiblePaths=new[]{"r6/scripts/incompatible-fixture","bin/x64/plugins/cyber_engine_tweaks/mods/AppearanceChangeUnlocker/init.lua"};
        guarded.ExclusiveCodeDirectories=new[]{"r6/scripts/owned-fixture","bin/x64/plugins/cyber_engine_tweaks/mods/CPM-fixture"};
        guarded.NexusModId=31886;guarded.NexusFileId=168671;FixedData.Validate(conflicts);
        string cg=Fixture(root,"conflitos");var ce=new FixedInstaller(conflicts,cache,cg,null,true);
        Text(cg,"r6/scripts/incompatible-fixture/presente.reds","conflito");
        Check(FixedData.Scan(guarded,cg).IsConflict,"Scan mostra mod incompatível sem alterar arquivos");
        Refuse(()=>ce.Install(new[]{"Extra"},true,noop),"Mod incompatível bloqueia antes do staging");
        Refuse(()=>AutoInstallPlan.Build(conflicts,new[]{"Extra"},cg,cache,false),"Fila automática também bloqueia mod incompatível");
        Check(!Directory.Exists(Safe.In(cg,".CoopModsOnline"))&&File.ReadAllText(Safe.In(cg,"r6/scripts/incompatible-fixture/presente.reds"))=="conflito","Bloqueio não apaga conflito nem cria instalação");
        File.Delete(Safe.In(cg,"r6/scripts/incompatible-fixture/presente.reds"));Directory.Delete(Safe.In(cg,"r6/scripts/incompatible-fixture"));
        Text(cg,"r6/scripts/owned-fixture/old.reds","versão antiga");
        Refuse(()=>ce.Install(new[]{"Extra"},true,noop),"Código residual de versão antiga bloqueia instalação");
        Check(File.ReadAllText(Safe.In(cg,"r6/scripts/owned-fixture/old.reds"))=="versão antiga","Código residual preservado para migração manual");
        File.Delete(Safe.In(cg,"r6/scripts/owned-fixture/old.reds"));
        Text(cg,"bin/x64/plugins/cyber_engine_tweaks/mods/CPM-fixture/Data/user.lua","dados do usuário");
        Text(cg,"bin/x64/plugins/cyber_engine_tweaks/mods/CPM-fixture/Character Presets/user.preset","preset do usuário");
        Text(cg,"bin/x64/plugins/cyber_engine_tweaks/mods/AppearanceChangeUnlocker/character-presets/user.preset","preset ACU");
        ce.Install(new[]{"Extra"},true,noop);
        Check(FixedData.Scan(guarded,cg).IsCorrect,"Presets ACU sem código e Data preservados não bloqueiam instalação");
        Text(cg,"r6/scripts/incompatible-fixture/presente.reds","conflito posterior");ce.Deactivate("Extra",noop);
        Check(!mod.Files.Any(f=>File.Exists(Safe.In(cg,f.Path))),"Pode desativar o mod mesmo se conflito foi instalado depois");
        Refuse(()=>ce.Undo(noop),"Desfazer não reativa junto de mod incompatível");
        Check(File.ReadAllText(Safe.In(cg,"bin/x64/plugins/cyber_engine_tweaks/mods/CPM-fixture/Character Presets/user.preset"))=="preset do usuário","Desativar preserva presets pessoais");
        File.Delete(Safe.In(cg,"r6/scripts/incompatible-fixture/presente.reds"));Directory.Delete(Safe.In(cg,"r6/scripts/incompatible-fixture"));ce.Undo(noop);
        Check(FixedData.Scan(guarded,cg).IsCorrect,"Reativação liberada após resolver conflito");
        bad=Clone(conflicts);bad.Packages[1].IncompatiblePaths=new[]{"../fora"};Refuse(()=>FixedData.Validate(bad),"Caminho de incompatibilidade com fuga recusado");
        bad=Clone(conflicts);bad.Packages[1].NexusFileId=0;Refuse(()=>FixedData.Validate(bad),"Alias Nexus sem arquivo fixo recusado");
        var aliasImporter=new ModImporter(conflicts,cache,Safe.In(root,"aliases-pendentes.json"));
        Refuse(()=>aliasImporter.Import("https://www.nexusmods.com/cyberpunk2077/mods/31886"),"Pagina Nexus nao entra mais no importador de arquivos");
        Check(aliasImporter.Import(archive).SelectedIds.Contains("Extra"),"Arquivo e reconhecido pelo SHA independentemente da origem Nexus ou GitHub");
        Refuse(()=>aliasImporter.Import("https://www.nexusmods.com/cyberpunk2077/mods/31886?tab=files&file_id=168672"),"Outro file_id tambem exige o arquivo baixado");
        Check(new NexusClient().DownloadUrl(guarded,guarded.Assets[0])==guarded.Assets[0].Url,"Fonte GitHub oficial não exige login Nexus nem troca o hash fixado");
        OnlineDiagnosticsTests.Run(parent);OnlineRemovalTests.Run(parent);OnlineDensityTests.Run(parent);OnlineDensityWindowTests.Run(parent);OnlineDownloadTests.Run(parent);OnlineImportTests.Run(parent);OnlineAutoInstallTests.Run(parent);OnlineDownloadWatchTests.Run(parent);OnlineBrowserSessionTests.Run(parent);OnlineWindowWatchTests.Run(parent);DefaultBrowserTests.Run(parent);
        OnlineBrowserDownloadProgressTests.Run(parent);
        OnlineUnifiedQueueTests.Run(parent);
        OnlineFacialInstallTests.Run(parent);
        OnlineGameEditionTests.Run(parent);
        OnlineUserDataTests.Run(parent);
        Safe.WriteJson(Path.Combine(root,"resultado.json"),new{Passed=passed.Count,Tests=passed,FixtureOnly=true,RealGameTouched=false});File.WriteAllText(Path.Combine(parent,"ultimo-teste.txt"),root);Console.WriteLine(passed.Count+" testes aprovados em "+root);
    }
    public static void VerifySources(FixedCatalog catalog,string seedsFile,string resultFile) {
        var seeds=Safe.Json.Deserialize<List<Dictionary<string,object>>>(File.ReadAllText(seedsFile));int count=0,entries=0;
        foreach(var mod in catalog.Packages.SelectMany(m=>m.DensityProfiles==null?new List<FixedMod>{m}:m.DensityProfiles.Select(p=>NightCityDensity.Resolve(m,p.Id)).ToList()))foreach(var a in mod.Assets){var seed=seeds.First(x=>Convert.ToString(x["Sha256"])==a.Sha256);string path=Convert.ToString(seed["Path"]);if(new FileInfo(path).Length!=a.Size||Safe.Hash(path)!=a.Sha256)throw new IOException("Fonte modificada: "+a.Name);
            var files=mod.Files.Where(f=>f.Asset==a.Id).ToList();
            if(a.Format=="zip")using(var z=ZipFile.OpenRead(path))foreach(var f in files){var e=z.Entries.Single(x=>x.FullName==f.Entry);using(var s=e.Open())using(var h=SHA256.Create()){if(e.Length!=f.Size||Safe.Hex(h.ComputeHash(s))!=f.Sha256)throw new IOException("Receita divergente: "+mod.Name+" "+f.Path);entries++;}}
            else if(a.Format=="raw"){foreach(var f in files){if(f.Size!=a.Size||f.Sha256!=a.Sha256)throw new IOException("Fonte raw divergente.");entries++;}}
            else {string local=Path.GetDirectoryName(resultFile);string temp=NewDir(local,"verificacao-tar");var cache=new DownloadCache(Path.Combine(temp,"cache"));cache.Import(path,a);cache.Stage(mod,Path.Combine(temp,"stage"),s=>{});entries+=files.Count;}
            count++;
        }Safe.WriteJson(resultFile,new{Assets=count,Files=entries,HashesVerified=true,NoGameWrites=true});
    }
}
