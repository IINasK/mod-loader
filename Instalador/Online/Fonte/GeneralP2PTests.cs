using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;

internal static class GeneralP2PTests {
    static readonly Action<string> Ignore = text => {};
    const string Device = "AAAAAAA-AAAAAAA-AAAAAAA-AAAAAAA-AAAAAAA-AAAAAAA-AAAAAAA-AAAAAAA";
    internal static void RunHelper(string parent){
        string root=NewRoot(parent,"p2p-helper-");
        string exe=SyncthingEngine.Ensure(root,Ignore);
        Check(new List<string>(),Safe.Hash(exe)==SyncthingEngine.ExeHash,"Auxiliar oficial conferido.");
        string again=SyncthingEngine.Ensure(root,Ignore);
        if(exe!=again||!File.Exists(Path.Combine(Path.GetDirectoryName(exe),"LICENSE.txt"))||!File.Exists(Path.Combine(Path.GetDirectoryName(exe),"SOURCE.txt")))throw new IOException("Licença ou reutilização do auxiliar ausente.");
        Safe.WriteJson(Path.Combine(root,"resultado-p2p-helper.json"),new{Passed=true,Tests=new[]{"Download oficial verificado por SHA-256 fixo.","Binário extraído confere e é reutilizado no segundo acesso.","Licença MPL e endereço de fonte preservados."},RealGameTouched=false,NetworkUsed=true,FilesShared=false});
    }

    internal static int Run(string parent) {
        string root = NewRoot(parent, "p2p-testes-");
        var checks = new List<string>();
        try {
            Action<bool,string> check = (ok,message) => Check(checks,ok,message);
            var invite = Invitation();
            var decoded = P2PInvite.Decode(invite.Encode());
            check(decoded.Device == Device && decoded.Folder == invite.Folder && decoded.ManifestHash == invite.ManifestHash && decoded.Name == invite.Name && decoded.Kind == "offer", "Convite válido preserva identidade, pacote e hash.");
            Reject(checks, () => P2PInvite.Decode("ML-P2P1:%%%"), "Base64 inválido é recusado.");
            Reject(checks, () => P2PInvite.Decode(new string('x',12001)), "Convite acima do limite é recusado.");
            Action<Action<P2PInvite>,string> rejectInvite = (mutate,message) => {
                var value = Invitation(); mutate(value);
                string code = "ML-P2P1:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(Safe.Json.Serialize(value)));
                Reject(checks, () => P2PInvite.Decode(code), message);
            };
            rejectInvite(value => value.Schema = 2, "Schema de convite desconhecido é recusado.");
            rejectInvite(value => value.Kind = "execute", "Tipo de convite desconhecido é recusado.");
            rejectInvite(value => value.Device = "invalid-device", "Device inválido é recusado.");
            rejectInvite(value => value.Device += "\n", "Device com controle final é recusado.");
            rejectInvite(value => value.Folder = "../outside", "Caminho relativo no identificador é recusado.");
            rejectInvite(value => value.Folder = @"C:\personal", "Caminho absoluto no identificador é recusado.");
            rejectInvite(value => value.ManifestHash = new string('a',63), "Hash incompleto de convite é recusado.");
            rejectInvite(value => value.ManifestHash = new string('A',64), "Hash fora do formato fixado é recusado.");
            rejectInvite(value => value.Name = "nome\ncom controle", "Nome de convite com controle é recusado.");

            var fixture = new Fixture(root);
            string transport = Path.Combine(root,"transport"), privateFolder = Path.Combine(root,"private-copy");
            var manifest = P2PPack.Create(transport,fixture.State,new[]{"custom"},fixture.Catalog,fixture.Cache,Ignore);
            string manifestPath = Path.Combine(transport,"pack.json"), hash = Safe.Hash(manifestPath);
            var read = P2PPack.Read(transport,hash);
            check(read.Name == "Preset para amigos" && read.Files.Count == 2 && read.Bindings.Count == 2 && read.Missing == 1, "Snapshot inclui arquivos locais e downloads associados, contabilizando link ausente.");
            check(read.List.Mods.Count == 1 && read.List.Mods[0].Variants.Count == 1 && read.List.Mods[0].DefaultVariant == "chosen" && read.List.Mods[0].Variants[0].Id == "chosen", "Snapshot inclui somente mods e variantes selecionados.");
            string output = File.ReadAllText(manifestPath);
            check(!new[]{root,root.Replace("\\","\\\\"),"Destination","DownloadFolder","ResolvedDownloads","ChosenVariants","SelectedPreset","unused.zip","excluded.zip"}.Any(output.Contains), "Manifesto não expõe caminhos pessoais, estado local nem arquivos não selecionados.");
            check(fixture.State.List.Mods.Count == 2 && fixture.State.List.Mods[0].Variants.Count == 2 && fixture.State.List.Mods[0].Variants[1].Sources[0] == fixture.Original, "Preparação não altera lista e referências originais.");
            Reject(checks, () => P2PPack.Create(transport,fixture.State,new[]{"custom"},fixture.Catalog,fixture.Cache,Ignore), "Snapshot exige uma pasta nova.");
            Reject(checks, () => P2PPack.Read(transport,new string('b',64)), "Manifesto diferente do hash fixado é recusado.");
            Reject(checks, () => P2PPack.Read(transport,null), "Manifesto sem hash fixado é recusado.");
            Action<Action<P2PManifest>,string> rejectManifest = (mutate,message) => {
                string folder = Path.Combine(root,"invalid-"+Guid.NewGuid().ToString("N"));
                var value = Clone(manifest); mutate(value);
                string path = Path.Combine(folder,"pack.json"); Safe.WriteJson(path,value);
                Reject(checks, () => P2PPack.Read(folder,Safe.Hash(path)),message);
            };
            rejectManifest(value => value.Schema = 2, "Schema de manifesto desconhecido é recusado.");
            rejectManifest(value => value.List.Mods[0].Variants[0].Sources[0] = @"C:\Users\Someone\private.zip", "Manifesto com caminho pessoal do remetente é recusado.");
            rejectManifest(value => value.Files[0].Extension = ".zip/../../outside", "Extensão de blob com travessia é recusada.");
            rejectManifest(value => value.Bindings.Add(value.Bindings[0]), "Associação duplicada é recusada.");
            rejectManifest(value => value.Files[0].Hash = null, "Blob sem SHA-256 é recusado.");
            rejectManifest(value => value.Files[0].Hash = new string('a',63), "Blob com SHA-256 parcial é recusado.");
            rejectManifest(value => value.Files[0].Size = P2PPack.MaxFile+1, "Blob acima de 16 GiB é recusado sem alocar o arquivo.");
            rejectManifest(value => value.Files = Enumerable.Range(1,9).Select(i => new P2PBlob{Hash=i.ToString("x64"),Extension=".zip",Size=P2PPack.MaxFile}).ToList(), "Pacote acima de 128 GiB é recusado sem alocar arquivos.");
            rejectManifest(value => value.Files.Add(value.Files[0]), "Blob duplicado é recusado.");
            rejectManifest(value => value.Files = Enumerable.Range(0,5001).Select(i => value.Files[0]).ToList(), "Manifesto acima de 5000 arquivos é recusado.");
            string oversized = Path.Combine(root,"oversized-manifest"); Directory.CreateDirectory(oversized);
            File.WriteAllText(Path.Combine(oversized,"pack.json"),new string(' ',8000001));
            Reject(checks, () => P2PPack.Read(oversized,Safe.Hash(Path.Combine(oversized,"pack.json"))), "Manifesto acima de 8 MB é recusado.");

            string incomplete = CopyTransport(root,transport,"incomplete");
            string blob = Path.Combine(incomplete,manifest.Files[0].File);
            File.Move(blob,blob+".part");
            Reject(checks, () => P2PPack.Receive(incomplete,hash,Path.Combine(root,"missing-private"),fixture.Catalog,fixture.Cache,Ignore), "Arquivo ausente com apenas parcial é recusado.");
            File.WriteAllBytes(blob,new byte[]{1});
            Reject(checks, () => P2PPack.Receive(incomplete,hash,Path.Combine(root,"partial-private"),fixture.Catalog,fixture.Cache,Ignore), "Arquivo truncado é recusado.");
            byte[] wrong = File.ReadAllBytes(Path.Combine(transport,manifest.Files[0].File)); wrong[wrong.Length-1] ^= 1; File.WriteAllBytes(blob,wrong);
            Reject(checks, () => P2PPack.Receive(incomplete,hash,Path.Combine(root,"hash-private"),fixture.Catalog,fixture.Cache,Ignore), "Mesmo tamanho com SHA-256 diferente é recusado.");
            string extraFolder = CopyTransport(root,transport,"unbound");
            string extraArchive = Zip(root,"extra.zip","unbound payload");
            var extraManifest = Clone(manifest);
            var extraBlob = new P2PBlob{Hash=Safe.Hash(extraArchive),Extension=".zip",Size=new FileInfo(extraArchive).Length};
            extraManifest.Files.Add(extraBlob); File.Copy(extraArchive,Path.Combine(extraFolder,extraBlob.File));
            Safe.WriteJson(Path.Combine(extraFolder,"pack.json"),extraManifest);
            Reject(checks, () => P2PPack.Receive(extraFolder,Safe.Hash(Path.Combine(extraFolder,"pack.json")),Path.Combine(root,"unbound-private"),fixture.Catalog,fixture.Cache,Ignore), "Blob extra íntegro sem vínculo ou receita conhecida é recusado.");

            var received = P2PPack.Receive(transport,hash,privateFolder,fixture.Catalog,fixture.Cache,Ignore);
            AssertReceived(checks,fixture,received,privateFolder);
            var copied = received.Mods.Single().Variants.Single().Sources.Take(2).ToArray();
            var copiedHashes = copied.Select(Safe.Hash).ToArray();
            for(int i=0;i<manifest.Files.Count;i++)File.WriteAllText(Path.Combine(transport,manifest.Files[i].File),"transport mutated");
            check(copied.Select(Safe.Hash).SequenceEqual(copiedHashes), "Alteração posterior da transferência não modifica cópias privadas conferidas.");
            string independent = Path.Combine(root,"independent-snapshot");
            // Use a fresh source: the previous transport mutation also detects unsafe hard links.
            var immutableFixture = new Fixture(Path.Combine(root,"immutable-fixture"));
            var immutable = P2PPack.Create(independent,immutableFixture.State,new[]{"custom"},immutableFixture.Catalog,immutableFixture.Cache,Ignore);
            string expected = immutable.Files[0].Hash;
            File.WriteAllText(immutableFixture.Original,"original mutated after snapshot");
            check(Safe.Hash(Path.Combine(independent,immutable.Files[0].File)) == expected, "Alteração posterior do original não modifica snapshot publicado.");
            KnownCatalogTests(root,checks);
            PreparationCleanup(root,checks);
            check(!Directory.Exists(fixture.State.Destination), "Testes de pacote não criam instalação de jogo.");
            Safe.WriteJson(Path.Combine(root,"resultado-p2p.json"),new{Passed=true,Tests=checks,RealGameTouched=false,NetworkUsed=false});
            return checks.Count;
        } catch(Exception ex) {
            Safe.WriteJson(Path.Combine(root,"resultado-p2p.json"),new{Passed=false,Tests=checks,Error=ex.ToString(),RealGameTouched=false,NetworkUsed=false}); throw;
        }
    }

    internal static int RunNetwork(string parent,string officialExe) {
        string root = NewRoot(parent,"p2p-network-testes-");
        var checks = new List<string>();
        P2PService sender = null, receiver = null;
        try {
            if(!File.Exists(officialExe) || Safe.Hash(officialExe) != SyncthingEngine.ExeHash)throw new IOException("Informe o executável oficial fixado para o teste local.");
            var fixture = new Fixture(root);
            sender = new P2PService(Path.Combine(root,"sender-home"),Path.Combine(root,"sender-transport"),true);
            receiver = new P2PService(Path.Combine(root,"receiver-home"),Path.Combine(root,"receiver-transport"),true);
            sender.Start(Ignore,officialExe); receiver.Start(Ignore,officialExe);
            Check(checks,sender.Engine.Running && receiver.Engine.Running && sender.Engine.Device != receiver.Engine.Device,"Dois auxiliares oficiais usam identidades e perfis isolados.");
            AssertLoopbackConfig(checks,sender); AssertLoopbackConfig(checks,receiver);
            var sent = sender.Share(fixture.State,new[]{"custom"},fixture.Catalog,fixture.Cache,Ignore);
            var accepted = receiver.Accept(sender.Invitation(sent));
            Check(checks,ReferenceEquals(accepted,receiver.Accept(sender.Invitation(sent))) && receiver.Sessions.Count == 1,"Aceitar novamente o mesmo convite não duplica a sessão.");
            Check(checks,File.ReadAllText(Path.Combine(receiver.Folder(accepted),".stignore")) == "!pack.json\n*\n","Recebimento inicial permite somente o manifesto fixado.");
            var answer = P2PInvite.Decode(receiver.Invitation(accepted));
            string correctFolder = answer.Folder, correctHash = answer.ManifestHash;
            answer.Folder = "ml-"+Guid.NewGuid().ToString("N");
            Reject(checks, () => sender.Authorize(answer.Encode()), "Resposta de outro pacote não autoriza a sessão.");
            answer.Folder = correctFolder; answer.ManifestHash = new string('b',64);
            Reject(checks, () => sender.Authorize(answer.Encode()), "Resposta com outro hash não autoriza a sessão.");
            answer.ManifestHash = correctHash;
            Check(checks,sent.Peer == null && sender.Sessions.Count == 1,"Respostas recusadas não alteram a vinculação do remetente.");
            sender.Pause(sent);
            Check(checks,ReferenceEquals(sent,sender.Authorize(answer.Encode())) && sent.Peer == receiver.Engine.Device && sent.Paused && Convert.ToBoolean(sender.Engine.Get("config/folders/"+sent.Id)["paused"]),"Resposta correta vincula somente a sessão esperada e preserva pausa anterior.");
            AssertEmptyPasswords(checks,sender,sent); AssertEmptyPasswords(checks,receiver,accepted);
            string senderDevice = sender.Engine.Device, receiverDevice = receiver.Engine.Device;
            string senderRoot = sender.Root, senderStore = sender.Store, receiverRoot = receiver.Root, receiverStore = receiver.Store;
            sender.Dispose(); receiver.Dispose();
            Check(checks,!sender.Engine.Running && !receiver.Engine.Running,"Encerramento anterior à retomada termina ambos os auxiliares.");
            sender = new P2PService(senderRoot,senderStore,true); receiver = new P2PService(receiverRoot,receiverStore,true);
            sent = sender.Sessions.Single(); accepted = receiver.Sessions.Single();
            Check(checks,sent.Sender && sent.Paused && sent.Peer == receiverDevice && !accepted.Sender && accepted.Peer == senderDevice && sent.Id == accepted.Id && sent.Hash == accepted.Hash && sent.Hash == correctHash,"Reabertura recupera papel, pausa, identidade do amigo, pacote e hash persistidos.");
            sender.Start(Ignore,officialExe); receiver.Start(Ignore,officialExe);
            Check(checks,sender.Engine.Device == senderDevice && receiver.Engine.Device == receiverDevice && Convert.ToBoolean(sender.Engine.Get("config/folders/"+sent.Id)["paused"]),"Reinício preserva identidades e reaplica pausa ao auxiliar.");
            AssertEmptyPasswords(checks,sender,sent); AssertEmptyPasswords(checks,receiver,accepted);
            sender.Engine.ConfigurePeer(receiver.Engine.Device,"tcp://127.0.0.1:"+receiver.Engine.ListenPort);
            receiver.Engine.ConfigurePeer(sender.Engine.Device,"tcp://127.0.0.1:"+sender.Engine.ListenPort);
            sender.Pause(sent);
            Check(checks,!sent.Paused && !Convert.ToBoolean(sender.Engine.Get("config/folders/"+sent.Id)["paused"]),"Retomar após reinício remove a pausa persistida.");
            sender.Engine.Request("POST","db/scan?folder="+sent.Id,null);
            Wait(() => {
                string path = Path.Combine(receiver.Folder(accepted),"pack.json");
                return File.Exists(path) && Safe.Hash(path) == sent.Hash;
            },"O manifesto não chegou pela conexão local em 30 segundos.");
            var manifest = P2PPack.Read(receiver.Folder(accepted),accepted.Hash);
            Check(checks,manifest.Files.Count == 2,"Manifesto recebido corresponde ao convite fixado.");
            Wait(() => manifest.Files.All(file => {
                string path = Path.Combine(receiver.Folder(accepted),file.File);
                return File.Exists(path) && new FileInfo(path).Length == file.Size && Safe.Hash(path) == file.Hash;
            }),"Os arquivos não chegaram completos pela conexão local em 30 segundos.");
            Check(checks,true,"Serviço em segundo plano libera e conclui transferência sem janela nem AllowFiles manual.");
            var receiverCache = new DownloadCache(Path.Combine(root,"receiver-cache"));
            var received = P2PPack.Receive(receiver.Folder(accepted),accepted.Hash,receiver.PrivateFolder(accepted),fixture.Catalog,receiverCache,Ignore);
            AssertReceived(checks,fixture,received,receiver.PrivateFolder(accepted));
            sender.Pause(sent);
            Check(checks,sent.Paused && Convert.ToBoolean(sender.Engine.Get("config/folders/"+sent.Id)["paused"]),"Pausa persiste na sessão e no auxiliar.");
            sender.Pause(sent);
            Check(checks,!sent.Paused && !Convert.ToBoolean(sender.Engine.Get("config/folders/"+sent.Id)["paused"]),"Retomar remove a pausa da sessão e do auxiliar.");
            string privateArchive = received.Mods.Single().Variants.Single().Sources[0], privateHash = Safe.Hash(privateArchive);
            receiver.Remove(accepted); sender.Remove(sent);
            Check(checks,receiver.Sessions.Count == 0 && sender.Sessions.Count == 0 && !Directory.Exists(receiver.Folder(accepted)) && !Directory.Exists(sender.Folder(sent)),"Remoção exclui somente snapshots e sessões de transporte.");
            Check(checks,File.Exists(fixture.Original) && File.Exists(privateArchive) && Safe.Hash(privateArchive) == privateHash && !Directory.Exists(fixture.State.Destination),"Remoção preserva original e cópia privada sem criar instalação de jogo.");
            Check(checks,FolderCount(sender) == 0 && FolderCount(receiver) == 0,"Auxiliares não retêm pastas compartilhadas após remoção.");
            sender.Dispose(); receiver.Dispose();
            Check(checks,!sender.Engine.Running && !receiver.Engine.Running,"Dispose encerra ambos os processos auxiliares.");
            Safe.WriteJson(Path.Combine(root,"resultado-p2p-network.json"),new{Passed=true,Tests=checks,RealGameTouched=false,NetworkUsed=true,LoopbackOnly=true});
            return checks.Count;
        } catch(Exception ex) {
            Safe.WriteJson(Path.Combine(root,"resultado-p2p-network.json"),new{Passed=false,Tests=checks,Error=ex.ToString(),RealGameTouched=false,NetworkUsed=true,LoopbackOnly=true}); throw;
        } finally { if(sender != null)sender.Dispose(); if(receiver != null)receiver.Dispose(); }
    }
    static void PreparationCleanup(string root,List<string> checks){
        using(var service=new P2PService(Path.Combine(root,"prep-private"),Path.Combine(root,"prep-transport"),true)){
            string marker=Safe.In(service.Root,"creating.json"),id="ml-"+new string('e',32),folder=Safe.In(service.Store,id);Directory.CreateDirectory(folder);File.WriteAllText(Safe.In(folder,"pack.zip"),"incomplete preparation");Safe.WriteJson(marker,new{Id=id});
            var cleanup=typeof(P2PService).GetMethod("CleanupPreparation",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);cleanup.Invoke(service,null);
            if(File.Exists(marker)||Directory.Exists(folder))throw new IOException("Preparação interrompida não foi limpa.");checks.Add("Reabertura limpa somente o ZIP de preparação interrompida sem sessão salva.");
            Directory.CreateDirectory(folder);File.WriteAllText(Safe.In(folder,"pack.zip"),"active transfer");service.Sessions.Add(new P2PSession{Id=id,Name="fixture",Hash=new string('f',64),Sender=true});Safe.WriteJson(marker,new{Id=id});cleanup.Invoke(service,null);
            if(!File.Exists(Safe.In(folder,"pack.zip"))||File.Exists(marker))throw new IOException("ZIP ativo não foi preservado.");checks.Add("Marcador após salvar sessão preserva o ZIP de transferência ativa.");
        }
    }

    static void KnownCatalogTests(string root,List<string> checks) {
        string dir = NewRoot(root,"known-");
        string archive = Zip(dir,"known.zip","trusted bytes");
        var asset = new FixedAsset{Id="asset",Name="known.zip",Format="zip",ModId=123,FileId=456,Size=new FileInfo(archive).Length,Sha256=Safe.Hash(archive)};
        var catalog = EmptyCatalog();
        catalog.Packages.Add(new FixedMod{Id="known",Name="Conhecido",Version="1",GameVersion="2.5",Dependencies=new string[0],Assets=new List<FixedAsset>{asset},Files=new List<FixedFile>()});
        var list = GeneralModLibrary.Seed(catalog,new ModImportItem[0]);
        var state = new GeneralModState{List=list,SelectedPreset="all"};
        var cache = new DownloadCache(Path.Combine(dir,"sender-cache")); cache.Import(archive,asset);
        string transport = Path.Combine(dir,"transport"), own = Path.Combine(dir,"private");
        var manifest = P2PPack.Create(transport,state,new[]{"known"},catalog,cache,Ignore);
        string hash = Safe.Hash(Path.Combine(transport,"pack.json")), url = list.Mods[0].Variants[0].Sources[0];
        var trusted = new DownloadCache(Path.Combine(dir,"receiver-cache"));
        var received = P2PPack.Receive(transport,hash,own,catalog,trusted,Ignore);
        Check(checks,manifest.Bindings.Count == 0 && trusted.Contains(asset) && received.Mods.Single().Variants.Single().Sources.Single() == url && !File.Exists(Path.Combine(own,asset.Sha256+".zip")),"Receita conhecida importa somente ao cache conferido e preserva URL oficial sem arquivo genérico.");
        string substitute = CopyTransport(dir,transport,"substitute");
        var forged = Clone(manifest);
        forged.Bindings.Add(new P2PBinding{Mod="known",Variant="default",Index=0,File=manifest.Files[0].File});
        Safe.WriteJson(Path.Combine(substitute,"pack.json"),forged);
        Reject(checks, () => P2PPack.Receive(substitute,Safe.Hash(Path.Combine(substitute,"pack.json")),Path.Combine(dir,"forged-private"),catalog,trusted,Ignore), "Associação personalizada não pode reescrever URL de receita conhecida.");
        var empty = new DownloadCache(Path.Combine(dir,"empty-cache"));
        string missing = Path.Combine(dir,"missing-transport");
        var absent = P2PPack.Create(missing,state,new[]{"known"},catalog,empty,Ignore);
        var missingList = P2PPack.Receive(missing,Safe.Hash(Path.Combine(missing,"pack.json")),Path.Combine(dir,"missing-private"),catalog,empty,Ignore);
        Check(checks,absent.Missing == 1 && absent.Files.Count == 0 && missingList.Mods[0].Variants[0].Sources.Single() == url && !empty.Contains(asset),"Arquivo conhecido ausente preserva link para download posterior.");
        catalog.Packages[0].Dependencies = new[]{"dependency"};
        catalog.Packages.Add(new FixedMod{Id="dependency",Name="Dependência",Version="1",GameVersion="2.5",Dependencies=new string[0],Assets=new List<FixedAsset>{new FixedAsset{Id="dependency-asset",Name="dependency.zip",Format="zip",ModId=123,FileId=789,Size=asset.Size,Sha256=asset.Sha256}},Files=new List<FixedFile>()});
        list.Categories.Clear(); list.CategoryDescriptions.Clear(); list.Mods[0].Category=null;
        string dependencyFolder = Path.Combine(dir,"dependency-transport");
        var dependencies = P2PPack.Create(dependencyFolder,state,new[]{"known"},catalog,empty,Ignore);
        var dependencyList = P2PPack.Receive(dependencyFolder,Safe.Hash(Path.Combine(dependencyFolder,"pack.json")),Path.Combine(dir,"dependency-private"),catalog,empty,Ignore);
        Check(checks,dependencies.Missing == 2 && dependencyList.Mods.Count == 2 && dependencyList.Mods.Single(mod => mod.Id == "dependency").Category == null && dependencyList.Presets.Single().ModIds.Contains("dependency"),"Dependência ausente da lista entra sem exigir categorias removidas e mantém link pendente no preset.");
    }

    static void AssertReceived(List<string> checks,Fixture fixture,GeneralModList list,string privateFolder) {
        var mod = list.Mods.Single(); var variant = mod.Variants.Single();
        Check(checks,mod.Name == "Meu mod" && mod.Description == "Descrição do mod" && mod.Nsfw && mod.Tags.SequenceEqual(new[]{"Favorito","Visual"}) && mod.Category == "Coleção", "Metadados, categoria, tags e NSFW sobrevivem ao recebimento.");
        Check(checks,list.Categories.Contains("Coleção") && list.CategoryDescriptions["Coleção"] == "Descrição da coleção" && list.UncategorizedName == "Avulsos" && list.UncategorizedDescription == "Organizar depois" && list.Presets.Single().Name == "Preset para amigos" && list.Presets.Single().ModIds.SequenceEqual(new[]{"custom"}),"Organização, descrições e preset preservam nomes e membros selecionados.");
        Check(checks,variant.Id == "chosen" && variant.Sources.Take(2).All(path => Path.GetDirectoryName(path) == privateFolder && File.Exists(path)) && Safe.Hash(variant.Sources[0]) == Safe.Hash(fixture.Original) && Safe.Hash(variant.Sources[1]) == Safe.Hash(fixture.Resolved),"Arquivos recebidos são cópias privadas completas, fora do transporte.");
        Check(checks,variant.Sources[2] == "https://example.com/missing.zip", "Link personalizado ausente permanece para associação posterior.");
    }

    static void AssertLoopbackConfig(List<string> checks,P2PService service) {
        var xml = new XmlDocument{XmlResolver=null}; xml.Load(Path.Combine(service.Root,"syncthing","config.xml"));
        Check(checks,new[]{"globalAnnounceEnabled","localAnnounceEnabled","relaysEnabled","natEnabled"}.All(name => xml.SelectSingleNode("/configuration/options/"+name).InnerText == "false") && xml.SelectNodes("/configuration/options/listenAddress").Count == 1 && xml.SelectSingleNode("/configuration/options/listenAddress").InnerText == "tcp://127.0.0.1:"+service.Engine.ListenPort,"Modo local desativa descoberta, NAT e relays e escuta somente loopback.");
        Check(checks,FolderCount(service) == 0 && xml.SelectNodes("/configuration/folder").Count == 0,"Perfil isolado não adota pastas pessoais ou a pasta padrão Sync.");
    }
    static int FolderCount(P2PService service) { return ((IEnumerable)service.Engine.Request("GET","config/folders",null)).Cast<object>().Count(); }
    static void AssertEmptyPasswords(List<string> checks,P2PService service,P2PSession session) {
        var folder = service.Engine.Get("config/folders/"+session.Id);
        var devices = ((IEnumerable)folder["devices"]).Cast<Dictionary<string,object>>().ToList();
        Check(checks,devices.Count == 2 && devices.All(device => device.ContainsKey("encryptionPassword") && device["encryptionPassword"] is string && ((string)device["encryptionPassword"]).Length == 0),"Configuração ativa usa senhas de pasta exatamente vazias para ambos os dispositivos.");
    }
    static void Wait(Func<bool> ready,string message) {
        var timer = Stopwatch.StartNew();
        while(!ready()) { if(timer.ElapsedMilliseconds >= 30000)throw new IOException(message); Thread.Sleep(100); }
    }
    static string NewRoot(string parent,string prefix) { string root = OnlineTests.NestedRoot(parent,prefix); Directory.CreateDirectory(root); return root; }
    static void Check(List<string> checks,bool ok,string message) { if(!ok)throw new IOException(message); checks.Add(message); }
    static void Reject(List<string> checks,Action action,string message) { bool rejected=false; try{action();}catch(IOException){rejected=true;} Check(checks,rejected,message); }
    static P2PInvite Invitation() { return new P2PInvite{Kind="offer",Device=Device,Folder="ml-"+new string('a',32),ManifestHash=new string('a',64),Name="Preset compartilhado"}; }
    static P2PManifest Clone(P2PManifest manifest) { return Safe.Json.Deserialize<P2PManifest>(Safe.Json.Serialize(manifest)); }
    static string CopyTransport(string root,string source,string name) {
        string folder = Path.Combine(root,name); Directory.CreateDirectory(folder);
        foreach(string file in Directory.GetFiles(source))File.Copy(file,Path.Combine(folder,Path.GetFileName(file)));
        return folder;
    }
    static FixedCatalog EmptyCatalog() { return new FixedCatalog{Schema=1,Id="p2p-fixture",GameVersion="2.5",GameFileVersion="2.5.0.0",Packages=new List<FixedMod>()}; }
    static string Zip(string root,string name,string text) {
        Directory.CreateDirectory(root); string path = Path.Combine(root,name);
        using(var zip = ZipFile.Open(path,ZipArchiveMode.Create))using(var writer = new StreamWriter(zip.CreateEntry("mods/example.txt").Open(),new UTF8Encoding(false)))writer.Write(text);
        return path;
    }
    sealed class Fixture {
        internal readonly string Original,Resolved;
        internal readonly GeneralModState State;
        internal readonly FixedCatalog Catalog;
        internal readonly DownloadCache Cache;
        internal Fixture(string root) {
            Directory.CreateDirectory(root); Catalog=EmptyCatalog(); Cache=new DownloadCache(Path.Combine(root,"cache"));
            Original=Zip(root,"chosen.zip","chosen contents"); Resolved=Zip(root,"resolved.zip","resolved contents");
            string unused=Zip(root,"unused.zip","unselected variant"), excluded=Zip(root,"excluded.zip","unselected mod");
            var list=new GeneralModList{UncategorizedName="Avulsos",UncategorizedDescription="Organizar depois"};
            list.Categories.Add("Coleção"); list.CategoryDescriptions.Add("Coleção","Descrição da coleção");
            list.Mods.Add(new GeneralModEntry{Id="custom",Name="Meu mod",Category="Coleção",Description="Descrição do mod",Tags=new List<string>{"Favorito","Visual"},Nsfw=true,DefaultVariant="default",Variants=new List<GeneralModVariant>{
                new GeneralModVariant{Id="default",Name="Não selecionada",Sources=new List<string>{unused}},
                new GeneralModVariant{Id="chosen",Name="Escolhida",Sources=new List<string>{Original,"https://example.com/resolved.zip","https://example.com/missing.zip"}}
            }});
            list.Mods.Add(new GeneralModEntry{Id="excluded",Name="Fora do preset",DefaultVariant="default",Variants=new List<GeneralModVariant>{new GeneralModVariant{Id="default",Name="Principal",Sources=new List<string>{excluded}}}});
            list.Presets.Add(new GeneralModPreset{Id="friends",Name="Preset para amigos",ModIds=new List<string>{"custom"}});
            State=new GeneralModState{List=list,SelectedPreset="friends",Destination=Path.Combine(root,"game-must-not-exist"),DownloadFolder=Path.Combine(root,"private-downloads")};
            State.ChosenVariants.Add("custom","chosen"); State.ResolvedDownloads.Add(P2PPack.Slot(list.Mods[0],list.Mods[0].Variants[1],1),Resolved);
        }
    }
}
