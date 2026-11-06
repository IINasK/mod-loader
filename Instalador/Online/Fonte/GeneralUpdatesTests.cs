using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

internal static class GeneralUpdatesTests {
    internal static void Run(string parent){
        string root=OnlineTests.NestedRoot(parent,"updates-");Directory.CreateDirectory(root);
        var checks=new List<string>();Action<bool,string> check=(yes,message)=>{if(!yes)throw new IOException(message);checks.Add(message);};
        Func<string,string> payload=tag=>Safe.Json.Serialize(new{tag_name=tag,draft=false,prerelease=false,assets=new[]{new{name="Mod Loader "+tag.TrimStart('v')+".exe",state="uploaded",size=12345,
            browser_download_url="https://github.com/"+GeneralUpdateChecker.Repository+"/releases/download/"+tag+"/Mod%20Loader%20"+tag.TrimStart('v')+".exe"}}});
        string valid=payload("v1.1.10");
        var newer=GeneralUpdateChecker.Parse(valid,"1.1.6");
        check(newer!=null&&newer.Version=="1.1.10"&&newer.Page.EndsWith("/releases/tag/v1.1.10"),"Compara versões numericamente: 1.1.10 é posterior a 1.1.6 e aponta para a Release oficial.");
        check(GeneralUpdateChecker.Parse(valid,"1.1.10")==null&&GeneralUpdateChecker.Parse(valid,"2.0.0")==null,"Versão igual ou antiga não gera aviso nem downgrade.");
        foreach(string bad in new[]{"v1.1.10-beta","v1.1","1.1.10.0","v9999999.1.0","1.1.10\n","../1.1.10","V1.1.10",""})
            check(GeneralUpdateChecker.VersionNumber(bad)==null,"Tag inválida/prévia é ignorada: "+bad.Replace("\n","\\n"));
        foreach(var change in new[]{new[]{"\"draft\":false","\"draft\":true"},new[]{"\"prerelease\":false","\"prerelease\":true"},
            new[]{"uploaded","new"},new[]{"\"size\":12345","\"size\":0"},new[]{"\"size\":12345","\"size\":999999999"},
            new[]{"https://github.com/","https://evil.example/"},new[]{"Mod%20Loader%201.1.10.exe","evil.exe"},new[]{"Mod Loader 1.1.10.exe","Mod Loader 1.1.9.exe"}})
            check(GeneralUpdateChecker.Parse(valid.Replace(change[0],change[1]),"1.1.6")==null,"Não anuncia release sem executável válido: "+change[0]);
        check(GeneralUpdateChecker.Parse("null","1.1.6")==null&&GeneralUpdateChecker.Parse("{\"tag_name\":\"v1.1.10\"}","1.1.6")==null,"Resposta vazia ou sem asset é ignorada.");
        string setupJson=Safe.Json.Serialize(new{tag_name="v1.1.10",draft=false,prerelease=false,assets=new[]{new{name="Mod-Loader-1.1.10.exe",state="uploaded",size=12345,digest="sha256:"+new string('a',64),browser_download_url="https://github.com/"+GeneralUpdateChecker.Repository+"/releases/download/v1.1.10/Mod-Loader-1.1.10.exe"}}});
        var automatic=GeneralUpdateChecker.Parse(setupJson,"1.1.6");
        check(automatic!=null&&automatic.Url.EndsWith("Mod-Loader-1.1.10.exe")&&automatic.Sha256==new string('a',64)&&automatic.Size==12345,"Instalador automático exige URL oficial, tamanho e digest SHA256 da Release.");
        check(newer.Url==null,"Executáveis portáteis antigos continuam só como aviso; nunca são executados como Setup.");
        foreach(string invalid in new[]{"", "sha256:bad", "md5:"+new string('a',64)}){var unsigned=GeneralUpdateChecker.Parse(setupJson.Replace("sha256:"+new string('a',64),invalid),"1.1.6");check(unsigned!=null&&unsigned.Url==null,"Sem digest válido oferece aviso manual e não atualização automática: "+invalid);}
        int calls=0;var source=new TaskCompletionSource<string>();
        var checker=new GeneralUpdateChecker(()=>{calls++;return source.Task;});
        var first=checker.CheckOnce();var second=checker.CheckOnce();
        check(Object.ReferenceEquals(first,second)&&calls==1&&!first.IsCompleted,"Duas chamadas simultâneas fazem uma só consulta sem bloquear a interface.");
        var current=new Version(GeneralUpdateChecker.CurrentVersion);string future=current.Major+"."+current.Minor+"."+(current.Build+1);
        source.SetResult(payload("v"+future));var result=first.GetAwaiter().GetResult();check(result!=null&&result.Version==future&&Object.ReferenceEquals(first,checker.CheckOnce())&&calls==1,"Reabrir menu/recriar UI não repete a consulta nesta sessão.");
        int failed=0;var offline=new GeneralUpdateChecker(()=>{failed++;throw new WebException("offline");});
        check(offline.CheckOnce().GetAwaiter().GetResult()==null&&offline.CheckOnce().Result==null&&failed==1,"Sem internet, privado, timeout ou rate limit não bloqueiam nem repetem a abertura.");
        var malformed=new GeneralUpdateChecker(()=>Task.FromResult("{invalido"));
        check(malformed.CheckOnce().GetAwaiter().GetResult()==null,"JSON malformado não interrompe o aplicativo.");
        using(var bytes=new MemoryStream(Encoding.UTF8.GetBytes(valid)))check(GeneralUpdateChecker.ReadLimited(bytes)==valid,"Leitor usa UTF-8 e preserva JSON completo.");
        bool rejected=false;using(var bytes=new MemoryStream(new byte[262145]))try{GeneralUpdateChecker.ReadLimited(bytes);}catch(IOException){rejected=true;}
        check(rejected,"Resposta sem Content-Length também tem limite de tamanho.");
        Safe.WriteJson(Path.Combine(root,"resultado-updates.json"),new{Passed=true,Tests=checks,RealGameTouched=false,NetworkUsed=false});
    }
}
