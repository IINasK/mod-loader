using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

public static class OnlineUserDataTests {
    public static void Run(string parent){
        string root=Path.Combine(Path.GetFullPath(parent),"online-testes-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var passed=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("TESTE PERFIL FALHOU: "+name);passed.Add(name);};
        Action<Action,string> refuse=(action,name)=>{bool refused=false;try{action();}catch(IOException){refused=true;}check(refused,name);};
        FixedAsset asset;var c=OnlineTests.MakeCatalog(root,out asset);var mod=c.Packages[1];string relative="bin/x64/plugins/cyber_engine_tweaks/mods/fixture/profiles/default.json",content="{\"preset\":1}";
        using(var zip=ZipFile.Open(Path.Combine(root,"fixture.zip"),ZipArchiveMode.Update)){using(var writer=new StreamWriter(zip.CreateEntry(relative).Open(),new UTF8Encoding(false)))writer.Write(content);}
        asset.Size=new FileInfo(Path.Combine(root,"fixture.zip")).Length;asset.Sha256=Safe.Hash(Path.Combine(root,"fixture.zip"));
        var data=new FixedFile{Asset="a0",Entry=relative,Path=relative,Size=Encoding.UTF8.GetByteCount(content),Sha256=Hash(content),UserData=true};mod.Files.Add(data);FixedData.Validate(c);
        check(true,"Dados personalizaveis aceitos somente como JSON do CET, separados do codigo");
        var bad=Clone(c);bad.Packages[1].Files[0].UserData=true;refuse(()=>FixedData.Validate(bad),"Arquivo de codigo/archive nao pode escapar da verificacao como perfil");
        bad=Clone(c);bad.Packages[1].Files=dataList(bad.Packages[1].Files);refuse(()=>FixedData.Validate(bad),"Pacote somente de dados nao pode representar mod instalado");
        bad=Clone(c);bad.Packages[0].Files[0].UserData=true;refuse(()=>FixedData.Validate(bad),"Base imutavel nao aceita dados opcionais");
        var cache=new DownloadCache(Path.Combine(root,"cache"));cache.Import(Path.Combine(root,"fixture.zip"),asset);cache.Stage(mod,Path.Combine(root,"stage"),s=>{});
        check(Safe.Hash(Safe.In(root,"stage/"+relative))==data.Sha256,"Perfil original no ZIP continua com hash obrigatorio no staging");
        var tampered=Clone(c).Packages[1];tampered.Files.Last().Sha256=new string('0',64);refuse(()=>cache.Stage(tampered,Path.Combine(root,"bad-stage"),s=>{}),"Perfil adulterado no pacote nao passa pela protecao de dados pessoais");
        string game=Game(root,"fresh",c);Put(game,relative,"{\"preset\":99}");
        check(FixedData.Scan(mod,game).State=="Não instalado","JSON preexistente sozinho nunca marca codigo como instalado");
        var engine=new FixedInstaller(c,cache,game,null,true);engine.Install(new[]{mod.Id},true,s=>{});
        check(FixedData.Scan(mod,game).IsCorrect&&File.ReadAllText(Safe.In(game,relative))=="{\"preset\":99}","Instalacao copia codigo e preserva perfil preexistente");
        Put(game,relative,"{\"preset\":42}");check(FixedData.Scan(mod,game).IsCorrect&&FixedData.Scan(mod,game).Detail.Contains("perfil"),"Editar perfil nao altera versao verificada do codigo e fica explicado no scan");
        engine.DeactivateMany(new[]{mod.Id},s=>{});check(FixedData.Scan(mod,game).State=="Não instalado"&&File.ReadAllText(Safe.In(game,relative))=="{\"preset\":42}","Remocao em lote tira somente codigo e conserva perfil editado");
        engine.Undo(s=>{});check(FixedData.Scan(mod,game).IsCorrect&&File.ReadAllText(Safe.In(game,relative))=="{\"preset\":42}","Desfazer remocao restaura codigo sem reverter perfil");
        engine.Deactivate(mod.Id,s=>{});check(FixedData.Scan(mod,game).State=="Não instalado"&&File.ReadAllText(Safe.In(game,relative))=="{\"preset\":42}","Desativacao individual tambem preserva perfil");
        engine.Undo(s=>{});File.Delete(Safe.In(game,relative));check(FixedData.Scan(mod,game).IsCorrect,"Perfil descartado pelo usuario ou mod nao vira falha de codigo instalado");
        Put(game,mod.Files[0].Path,"code edited");check(FixedData.Scan(mod,game).IsConflict,"Alteracao em codigo continua bloqueada");
        refuse(()=>engine.DeactivateMany(new[]{mod.Id},s=>{}),"Preservacao de perfis nao permite apagar codigo alterado");
        string fresh=Game(root,"defaults",c);var freshEngine=new FixedInstaller(c,cache,fresh,null,true);freshEngine.Install(new[]{mod.Id},true,s=>{});
        check(File.ReadAllText(Safe.In(fresh,relative))==content,"Instalacao nova fornece preset oficial ausente");
        Put(fresh,relative,"{\"preset\":123}");freshEngine.Undo(s=>{});
        check(File.ReadAllText(Safe.In(fresh,relative))=="{\"preset\":123}"&&FixedData.Scan(mod,fresh).State=="Não instalado","Desfazer instalacao preserva preset editado depois e retira codigo proprio");
        Safe.WriteJson(Path.Combine(root,"resultado-user-data.json"),new{Passed=passed.Count,Tests=passed,RealGameTouched=false,PayloadExecuted=false});
    }
    static List<FixedFile> dataList(List<FixedFile> files){return files.Where(f=>f.UserData).ToList();}
    static FixedCatalog Clone(FixedCatalog c){return Safe.Json.Deserialize<FixedCatalog>(Safe.Json.Serialize(c));}
    static string Hash(string value){using(var sha=System.Security.Cryptography.SHA256.Create())return Safe.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));}
    static void Put(string root,string path,string text){string p=Safe.In(root,path);Directory.CreateDirectory(Path.GetDirectoryName(p));File.WriteAllText(p,text,new UTF8Encoding(false));}
    static string Game(string root,string name,FixedCatalog c){string game=OnlineTests.Fixture(root,name);foreach(var file in c.Packages[0].Files)Put(game,file.Path,"test "+file.Path);return game;}
}
