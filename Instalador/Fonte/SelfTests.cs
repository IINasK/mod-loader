using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

static class SelfTests {
    static int passed; static readonly List<string> results=new List<string>(); static string run;
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    [return: MarshalAs(UnmanagedType.I1)] static extern bool CreateSymbolicLink(string link,string target,int flags);
    static void Check(bool ok,string name) { if(!ok) throw new Exception("FALHOU: "+name); passed++; results.Add("OK: "+name); }
    static void Throws(Action action,string name) { bool threw=false; try { action(); } catch(Exception) { threw=true; } Check(threw,name); }
    static string Area(string name) { string p=Path.Combine(run,name+"-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(p); return p; }
    static Package Make(string root, Dictionary<string,string> data) {
        Directory.CreateDirectory(Path.Combine(root,"Dados"));
        var m=new Manifest { Schema=1, Profile="fixture", GameVersion="2.5", RequirePhantomLiberty=false, Files=new List<Item>() };
        using(var zip=ZipFile.Open(Path.Combine(root,@"Dados\conteudo.zip"),ZipArchiveMode.Create)) {
            foreach(var kv in data) {
                byte[] bytes=Encoding.UTF8.GetBytes(kv.Value);
                using(var h=System.Security.Cryptography.SHA256.Create()) m.Files.Add(new Item { Path=kv.Key, Size=bytes.Length, Sha256=Safe.Hex(h.ComputeHash(bytes)), Component="fixture" });
                using(var stream=zip.CreateEntry(kv.Key).Open()) stream.Write(bytes,0,bytes.Length);
            }
        }
        string manifest=Path.Combine(root,@"Dados\manifesto.json"); Safe.WriteJson(manifest,m);
        return Package.Load(root,Safe.Hash(manifest),Safe.Hash(Path.Combine(root,@"Dados\conteudo.zip")));
    }
    static string Game(string name) { string p=Area(name); File.WriteAllText(Path.Combine(p,".fixture-only"),"isolated"); Directory.CreateDirectory(Path.Combine(p,@"bin\x64")); File.WriteAllText(Path.Combine(p,@"bin\x64\Cyberpunk2077.exe"),"fixture, not an executable"); return p; }
    static Dictionary<string,string> Files() { return new Dictionary<string,string> { {"r6/config/amostra.ini","novo"},{"red4ext/plugins/Exemplo/plugin.dll","fixture binary"},{"archive/pc/mod/exemplo.archive","fixture archive"} }; }
    static void Quiet(string text) { }
    public static void Run(string output) {
        run=Path.Combine(Path.GetFullPath(output),"ensaio-instalador-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(run);
        try {
            foreach(string bad in new[]{"../fora","/bin/a","bin/../../fora","bin\\a","bin/a:stream","bin/a.","bin/a /b","bin/CON.txt","bin/LPT1","bin//a","bin/./a","C:/fora","bin/a\n","bin/a*"}) Throws(()=>Safe.In(run,bad),"caminho rejeitado: "+bad.Replace("\n","\\n"));
            Check(Safe.In(run,"r6/scripts/ação com espaço.reds").StartsWith(run+"\\"),"acentos e espaços preservados");
            Check(Installer.VersionSupported("2.5")&&Installer.VersionSupported("2.50")&&Installer.VersionSupported("2.5.0.0")&&!Installer.VersionSupported("2.31")&&!Installer.VersionSupported("2.51")&&!Installer.VersionSupported("2.5.1")&&!Installer.VersionSupported("2.5.0.1"),"versão 2.5 exata, sem aceitar outro patch");
            string atom=Area("copia-adulterada"); File.WriteAllText(Path.Combine(atom,"source"),"conteúdo adulterado"); File.WriteAllText(Path.Combine(atom,"target"),"preservar original");
            Throws(()=>Safe.CopyAtomic(Path.Combine(atom,"source"),Path.Combine(atom,"target"),new string('0',64)),"hash incorreto bloqueia substituição atômica"); Check(File.ReadAllText(Path.Combine(atom,"target"))=="preservar original","cópia inválida deixa destino intacto");
            var p=Make(Area("pacote"),Files()); string g=Game("instalar");
            string stub=Path.Combine(output,"FixtureGame.exe");
            if(File.Exists(stub)) {
                string realChecks=Game("pre-condicoes"); File.Copy(stub,Path.Combine(realChecks,@"bin\x64\Cyberpunk2077.exe"),true);
                p.Manifest.RequirePhantomLiberty=true; var checkedEngine=new Installer(p,realChecks,null);
                Throws(()=>checkedEngine.ValidateGame(),"ausência de Phantom Liberty bloqueia instalação");
                Directory.CreateDirectory(Path.Combine(realChecks,@"archive\pc\ep1")); File.WriteAllText(Path.Combine(realChecks,@"archive\pc\ep1\ep1.addcont_keystone"),"fixture");
                checkedEngine.ValidateGame(); Check(true,"versão de EXE e marcador da DLC aceitos no caminho de produção");
                p.Manifest.RequireRedmod=true; Throws(()=>checkedEngine.ValidateGame(),"perfil REDMOD bloqueia ausência do componente"); p.Manifest.RequireRedmod=false;
                using(var process=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(realChecks,@"bin\x64\Cyberpunk2077.exe")){UseShellExecute=false,CreateNoWindow=true,WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden})) {
                    Throws(()=>checkedEngine.ProbeWrite(),"processo de jogo aberto bloqueia operação"); process.WaitForExit(10000);
                }
                p.Manifest.RequirePhantomLiberty=false;
            } else results.Add("NÃO EXECUTADO: fixture de versão/condições de produção ausente.");
            string config=Safe.In(g,"r6/config/amostra.ini"); Directory.CreateDirectory(Path.GetDirectoryName(config)); File.WriteAllText(config,"original");
            string saves=Area("saves"); File.WriteAllText(Path.Combine(saves,"ManualSave.fixture"),"save original");
            File.WriteAllText(Path.Combine(g,"arquivo-alheio.txt"),"preservar");
            var engine=new Installer(p,g,saves,true); engine.Install(Quiet);
            Check(File.ReadAllText(config)=="novo","instalação real em pasta fictícia");
            string before=File.ReadAllText(Path.Combine(g,@".CoopMods\estado.json"));
            engine.Install(Quiet); Check(File.ReadAllText(Path.Combine(g,@".CoopMods\estado.json"))==before,"repetir instalação não perde backup");
            Check(Directory.GetFiles(Path.Combine(g,@".CoopMods\instalacoes"),"ManualSave.fixture",SearchOption.AllDirectories).Length==1,"saves copiados para backup");
            engine.Undo(Quiet);
            Check(File.ReadAllText(config)=="original","desfazer restaura original");
            Check(!File.Exists(Safe.In(g,"red4ext/plugins/Exemplo/plugin.dll")),"desfazer remove somente arquivo criado pelo pacote");
            Check(File.ReadAllText(Path.Combine(g,"arquivo-alheio.txt"))=="preservar"&&File.ReadAllText(Path.Combine(saves,"ManualSave.fixture"))=="save original","arquivos alheios e saves atuais preservados");
            engine.Install(Quiet); File.WriteAllText(config,"edição posterior"); engine.Undo(Quiet);
            Check(File.ReadAllText(config)=="edição posterior","desfazer preserva arquivo editado depois");
            Throws(()=>engine.Install(Quiet),"reinstalação não apaga edição posterior");
            string failureGame=Game("falha"); string old=Safe.In(failureGame,"r6/config/amostra.ini"); Directory.CreateDirectory(Path.GetDirectoryName(old)); File.WriteAllText(old,"antes da falha");
            var failing=new Installer(p,failureGame,null,true); Throws(()=>failing.Install(Quiet,2),"falha real durante cópia é relatada");
            Check(File.ReadAllText(old)=="antes da falha"&&!File.Exists(Safe.In(failureGame,"red4ext/plugins/Exemplo/plugin.dll")),"falha reverte novo e substituído");
            string malicious=Area("malicioso"); Throws(()=>Make(malicious,new Dictionary<string,string>{{"r6/../../fora","x"}}),"manifesto com traversal bloqueado");
            string casing=Area("duplicado"); Throws(()=>Make(casing,new Dictionary<string,string>{{"r6/teste.txt","a"},{"R6/TESTE.txt","b"}}),"destinos duplicados por maiúsculas bloqueados");
            var changed=Make(Area("hash"),Files()); File.AppendAllText(Path.Combine(changed.Root,@"Dados\conteudo.zip"),"alterado"); Throws(()=>changed.Extract(Area("hash-saida"),Quiet),"ZIP alterado bloqueado antes da extração");
            string mf=Path.Combine(p.Root,@"Dados\manifesto.json"); string mh=Safe.Hash(mf); File.AppendAllText(mf," "); Throws(()=>Package.Load(p.Root,mh,p.PayloadHash),"manifesto alterado bloqueado");
            var wrongEntry=Make(Area("entrada"),Files()); string za=Path.Combine(wrongEntry.Root,@"Dados\conteudo.zip");
            using(var zip=ZipFile.Open(za,ZipArchiveMode.Update)) { var e=zip.Entries.First(); e.Delete(); using(var s=zip.CreateEntry("../fora.txt").Open()) s.WriteByte(1); }
            wrongEntry.PayloadHash=Safe.Hash(za); Throws(()=>wrongEntry.Extract(Area("entrada-saida"),Quiet),"entrada extra com traversal bloqueada");
            var linkZip=Make(Area("linkzip"),Files()); string lz=Path.Combine(linkZip.Root,@"Dados\conteudo.zip");
            using(var zip=ZipFile.Open(lz,ZipArchiveMode.Update)) zip.Entries.First().ExternalAttributes=unchecked((int)0xA1FF0000);
            linkZip.PayloadHash=Safe.Hash(lz); Throws(()=>linkZip.Extract(Area("linkzip-saida"),Quiet),"symlink no ZIP bloqueado");
            var corruptBackupPackage=Make(Area("corrupt-backup"),Files()); string cbGame=Game("backup-corrompido"); string cbConfig=Safe.In(cbGame,"r6/config/amostra.ini"); Directory.CreateDirectory(Path.GetDirectoryName(cbConfig)); File.WriteAllText(cbConfig,"original intacto");
            var cbEngine=new Installer(corruptBackupPackage,cbGame,null,true); cbEngine.Install(Quiet);
            string cb=Directory.GetFiles(Path.Combine(cbGame,@".CoopMods\instalacoes"),"amostra.ini",SearchOption.AllDirectories).Single(); File.WriteAllText(cb,"backup corrompido");
            Throws(()=>cbEngine.Undo(Quiet),"backup corrompido impede restauração insegura"); Check(File.ReadAllText(cbConfig)=="novo","backup corrompido não substitui arquivo atual");
            var tamper=Make(Area("tamper-state"),Files()); string tg=Game("registro-adulterado"); var te=new Installer(tamper,tg,null,true); te.Install(Quiet);
            string statePath=Path.Combine(tg,@".CoopMods\estado.json"); var state=Safe.Json.Deserialize<Receipt>(File.ReadAllText(statePath)); state.Files[0].Path="../arquivo-fora.txt"; Safe.WriteJson(statePath,state);
            Throws(()=>te.Undo(Quiet),"registro de desfazer adulterado é rejeitado");
            var mismatch=Make(Area("tamanho"),Files()); mismatch.Manifest.Files[0].Size++; Throws(()=>mismatch.Extract(Area("tamanho-saida"),Quiet),"tamanho descomprimido incorreto bloqueado");
            string linkTarget=Area("link-target"), linkParent=Area("link-parent"), link=Path.Combine(linkParent,"atalho");
            if(CreateSymbolicLink(link,linkTarget,3)||CreateSymbolicLink(link,linkTarget,1)) Throws(()=>Safe.In(linkParent,"atalho/arquivo.txt"),"link de diretório no destino bloqueado");
            else results.Add("NÃO EXECUTADO: symlink real de diretório; privilégio indisponível, erro Win32 "+Marshal.GetLastWin32Error());
            string junctionGame=Path.Combine(output,"junction-game");
            if(Directory.Exists(junctionGame)) {
                Throws(()=>new Installer(Make(Area("junction-payload"),Files()),junctionGame,null,true).Install(Quiet),"junção real no destino bloqueia instalação");
                Check(File.ReadAllText(Path.Combine(output,@"junction-alvo\preservar.txt"))=="não alterar"&&!Directory.Exists(Path.Combine(output,@"junction-alvo\config")),"nenhum arquivo escrito pelo instalador através da junção");
            } else results.Add("NÃO EXECUTADO: fixture externa de junção ausente.");
            var detected=Discovery.Find(); results.Add("INFO: detecção executada; "+detected.Count+" pasta(s) encontrada(s).");
            string installedGame=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),@"Steam\steamapps\common\Cyberpunk 2077");
            if(File.Exists(Path.Combine(installedGame,@"bin\x64\Cyberpunk2077.exe"))) Check(detected.Any(d=>String.Equals(d,installedGame,StringComparison.OrdinalIgnoreCase)),"jogo local encontrado automaticamente sem alteração");
            results.Add("TOTAL: "+passed+" verificações aprovadas. Nenhum jogo real foi instalado ou removido pelos testes.");
        } finally { File.WriteAllLines(Path.Combine(run,"resultado.txt"),results,Encoding.UTF8); }
    }
}
