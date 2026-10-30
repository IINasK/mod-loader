using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

public static class OnlineBrowserSessionTests {
    const string Url="https://www.nexusmods.com/cyberpunk2077/mods/100?tab=files&file_id=42";
    sealed class MockBrowser {
        public string Current=Url;
        public string NewUrl;
        public bool InvalidNew,DuplicateList;
        public readonly List<string> Calls=new List<string>();
        public string Request(string method,string path) {
            Calls.Add(method+" "+path);
            if(path.StartsWith("/json/new?"))return InvalidNew?"{oops":Safe.Json.Serialize(new{id="Tab1",type="page",url=NewUrl??Uri.UnescapeDataString(path.Substring("/json/new?".Length))});
            if(path=="/json/list")return Safe.Json.Serialize(new[]{new{id="Tab1",type="page",url=Current},new{id=DuplicateList?"Tab1":"OtherTab",type="page",url="https://example.org"}});
            if(path=="/json/close/Tab1")return "Target is closing";
            throw new IOException("Mock refuses unowned request: "+path);
        }
        public int Closes {get{return Calls.Count(c=>c.StartsWith("GET /json/close/"));}}
    }
    public static void Run(string parent) {
        string root=Path.Combine(Path.GetFullPath(parent),"browser-"+Guid.NewGuid().ToString("N").Substring(0,12));Directory.CreateDirectory(root);
        var passed=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("TESTE BROWSER FALHOU: "+name);passed.Add(name);};
        Action<Action,string> refuse=(action,name)=>{bool failed=false;try{action();}catch(IOException){failed=true;}check(failed,name);};
        string lazy=Path.Combine(root,"lazy");var mock=new MockBrowser();
        using(var session=new OnlineBrowserSession(lazy,mock.Request)) {
            check(!Directory.Exists(lazy),"Construtor é lazy e não cria perfil/cache no preview");
            refuse(()=>session.Open("https://www.nexusmods.com/cyberpunk2077/mods/100"),"Página sem file_id não cria aba automática");
            refuse(()=>session.Open("https://www.nexusmods.com.evil.test/cyberpunk2077/mods/100?file_id=42"),"Domínio falso é recusado antes de iniciar navegador");
            check(!Directory.Exists(lazy)&&mock.Calls.Count==0,"Links recusados não criam perfil nem acessam protocolo");
            BrowserTab tab=session.Open(Url);
            check(tab.Url==Url&&mock.Calls[0]=="PUT /json/new?"+Uri.EscapeDataString(Url),"Aba é criada por requisição explícita com arquivo Nexus exato");
            check(session.Close(tab)&&mock.Closes==1&&mock.Calls.Last()=="GET /json/close/Tab1"&&!String.IsNullOrWhiteSpace(session.LastCloseReason),"Fechamento usa somente targetId criado por esta sessão e registra resultado");
            check(!session.Close(tab)&&mock.Closes==1,"Aba consumida não é fechada novamente");
            check(!mock.Calls.Any(c=>c.Contains("/json/close/OtherTab")),"Outras abas são preservadas");
        }
        foreach(string navigated in new[]{"https://example.org","https://www.nexusmods.com/cyberpunk2077/mods/100?tab=files&file_id=43","https://www.nexusmods.com/cyberpunk2077/mods/200?tab=files&file_id=42","https://www.nexusmods.com/games/cyberpunk2077/mods/100?token=secret","https://www.nexusmods.com/games/othergame/mods/100","https://www.nexusmods.com/games/cyberpunk2077/mods/100?file_id=42&file_id=43","https://www.nexusmods.com/cyberpunk2077/mods/%31%30%30","https://www.nexusmods.com/games/cyberpunk2077/mods/100?redirect=https://example.org"}) {
            var changed=new MockBrowser();using(var session=new OnlineBrowserSession(Path.Combine(root,"nav-"+Guid.NewGuid().ToString("N").Substring(0,6)),changed.Request)) {
                var tab=session.Open(Url);changed.Current=navigated;
                check(!session.Close(tab)&&changed.Closes==0&&!String.IsNullOrWhiteSpace(session.LastCloseReason),"Aba alterada/ambígua é preservada com motivo: "+navigated);
            }
        }
        foreach(string sameMod in new[]{"https://www.nexusmods.com/cyberpunk2077/mods/100","https://www.nexusmods.com/games/cyberpunk2077/mods/100?tab=files","https://www.nexusmods.com/games/cyberpunk2077/mods/100?tab=files&file_id=42","https://www.nexusmods.com/pt/games/cyberpunk2077/mods/100?tab=files","https://www.nexusmods.com/pt-BR/cyberpunk2077/mods/100?locale=pt-BR","https://nexusmods.com/cyberpunk2077/mods/100?language=pt#files"}) {
            var changed=new MockBrowser();using(var session=new OnlineBrowserSession(Path.Combine(root,"same-"+Guid.NewGuid().ToString("N").Substring(0,6)),changed.Request)) {
                var tab=session.Open(Url);changed.Current=sameMod;check(session.Close(tab)&&changed.Closes==1,"Aba própria do mesmo mod pode fechar sem file_id após navegação: "+sameMod);
            }
        }
        foreach(string initial in new[]{"about:blank","","https://www.nexusmods.com/games/cyberpunk2077/mods/100"}) {
            var initialBrowser=new MockBrowser{NewUrl=initial};using(var session=new OnlineBrowserSession(Path.Combine(root,"initial-"+Guid.NewGuid().ToString("N").Substring(0,6)),initialBrowser.Request)) {
                var tab=session.Open(Url);check(session.Close(tab),"Target explicitamente criado pode iniciar transitoriamente em: "+initial);
            }
        }
        var equivalent=new MockBrowser();using(var session=new OnlineBrowserSession(Path.Combine(root,"equivalent"),equivalent.Request)) {
            var tab=session.Open(Url);equivalent.Current="https://www.nexusmods.com/cyberpunk2077/mods/100?tab=description&file_id=42";
            check(session.Close(tab),"Outra aba de conteúdo do mesmo mod e arquivo ainda corresponde ao download");
        }
        var forged=new MockBrowser();using(var session=new OnlineBrowserSession(Path.Combine(root,"forged"),forged.Request)) {
            var tab=session.Open(Url);
            check(!session.Close(new BrowserTab("wrong-session",tab.Id,Url))&&forged.Closes==0,"Token de outra sessão não autoriza fechar aba");
            check(!session.Close(new BrowserTab(tab.Owner,tab.Id,Url))&&forged.Closes==0,"Identificador copiado sem a referência original não autoriza fechamento");
        }
        var duplicates=new MockBrowser();using(var session=new OnlineBrowserSession(Path.Combine(root,"duplicate"),duplicates.Request)) {
            var tab=session.Open(Url);duplicates.DuplicateList=true;check(!session.Close(tab)&&duplicates.Closes==0,"Lista ambígua de IDs não fecha nenhuma aba");
        }
        var bad=new MockBrowser{InvalidNew=true};using(var session=new OnlineBrowserSession(Path.Combine(root,"bad-json"),bad.Request))refuse(()=>session.Open(Url),"Resposta malformada não vincula aba");
        string foreign=Path.Combine(root,"foreign");Directory.CreateDirectory(Path.Combine(foreign,"Test","Profile"));
        using(var session=new OnlineBrowserSession(foreign,new MockBrowser().Request))refuse(()=>session.Open(Url),"Perfil preexistente sem marcador do app não é adotado");
        string locked=Path.Combine(root,"locked");var firstMock=new MockBrowser();using(var first=new OnlineBrowserSession(locked,firstMock.Request)) {
            first.Open(Url);using(var second=new OnlineBrowserSession(locked,new MockBrowser().Request))refuse(()=>second.Open(Url),"Lock exclusivo impede segunda sessão de controlar o mesmo perfil");
        }
        using(var released=new OnlineBrowserSession(locked,new MockBrowser().Request))check(released.Open(Url)!=null,"Dispose libera o lock do perfil sem afinidade de thread");
        foreach(var selection in new[]{DefaultBrowser.Classify(Path.Combine(root,"firefox.exe"),"Mozilla Firefox"),DefaultBrowser.Classify(Path.Combine(root,"other.exe"),"Other Browser"),DefaultBrowser.Unsupported()}) {
            var shell=new MockBrowser();string shellRoot=Path.Combine(root,"shell-"+Guid.NewGuid().ToString("N").Substring(0,6));
            using(var session=new OnlineBrowserSession(shellRoot,shell.Request,()=>selection)) {
                refuse(()=>session.Open(Url),"Navegador sem suporte informa fallback do Windows: "+selection.Name);
                check(shell.Calls.Count==0&&!Directory.Exists(shellRoot)&&session.BrowserName==selection.Name,"Fallback não abre Edge, não cria perfil e não acessa CDP: "+selection.Name);
            }
        }
        using(var stream=new MemoryStream(new byte[1048577]))refuse(()=>OnlineBrowserSession.ReadBounded(stream,-1),"Resposta sem tamanho declarado respeita limite de 1 MiB");
        using(var stream=new MemoryStream(new byte[1]))refuse(()=>OnlineBrowserSession.ReadBounded(stream,1048577),"Content-Length excessivo é recusado antes de ler corpo");
        using(var stream=new MemoryStream(new byte[]{255,255}))refuse(()=>OnlineBrowserSession.ReadBounded(stream,2),"Resposta UTF-8 inválida não é aceita");
        using(var stream=new MemoryStream(Encoding.UTF8.GetBytes("ok")))check(OnlineBrowserSession.ReadBounded(stream,2)=="ok","Resposta curta válida é lida integralmente");
        refuse(()=>new OnlineBrowserSession(@"\\server\share\browser"),"Perfil UNC é recusado sem acessar rede");
        refuse(()=>new OnlineBrowserSession(Path.GetPathRoot(root)),"Raiz da unidade não pode ser perfil do app");
        var disposedMock=new MockBrowser();var disposedSession=new OnlineBrowserSession(Path.Combine(root,"disposed"),disposedMock.Request);var disposedTab=disposedSession.Open(Url);disposedSession.Dispose();
        check(disposedMock.Closes==0&&!disposedSession.Close(disposedTab),"Dispose não encerra processos nem outras abas do usuário");
        Safe.WriteJson(Path.Combine(root,"resultado.json"),new{Passed=passed.Count,Tests=passed,MockProtocolOnly=true,BrowserLaunched=false,NetworkUsed=false,RealGameTouched=false});
        File.WriteAllText(Path.Combine(parent,"ultimo-teste-browser.txt"),root);
    }
}
