using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

static class OnlineExtraCleanupTests {
    static void Put(string game,string relative,string content) {
        string path=Safe.In(game,relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path,content);
    }

    public static void Run(string parent) {
        string root=Path.Combine(Path.GetFullPath(parent),"online-testes-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        FixedAsset asset;
        var catalog=OnlineTests.MakeCatalog(root,out asset);
        string game=OnlineTests.Fixture(root,"game");
        var engine=new FixedInstaller(catalog,new DownloadCache(Path.Combine(root,"cache")),game,null,true);
        var checks=new List<string>();
        Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("TESTE LIMPEZA: "+name);checks.Add(name);};
        Action<string> quiet=message=>{};
        var extras=new[]{
            "archive/pc/mod/outside.archive", "archive/pc/mod/outside.archive.xl",
            "r6/scripts/Another/another.reds", "r6/tweaks/Another/another.yaml",
            "red4ext/plugins/Other.dll", "red4ext/plugins/Other/config.ini",
            "bin/x64/plugins/cyber_engine_tweaks/mods/Other/init.lua", "mods/Legacy/info.json", "mods/Legacy/Legacy.archive"
        };
        string knownPath=catalog.Packages[1].Files[0].Path;
        var preserved=new[]{knownPath, "archive/pc/content/official.archive", "engine/config/platform/pc/personal.ini",
            "bin/x64/winmm.dll", "red4ext/plugins/NightCoop/personal.json", "mods/manual-not-redmod/notes.txt"};
        foreach(string path in extras.Concat(preserved))Put(game,path,path);
        var plan=engine.PlanExtraReset();
        check(new HashSet<string>(plan.Select(f=>f.Path)).SetEquals(extras),"Escopo identifica só os extras em locais de mod");
        check(extras.Concat(preserved).All(p=>File.Exists(Safe.In(game,p))),"Prévia não retira arquivos");
        Put(game,extras[0],"editado apos aprovacao");
        bool refused=false;
        try { engine.ApplyExtraReset(plan,quiet); } catch(IOException) { refused=true; }
        check(refused&&extras.All(p=>File.Exists(Safe.In(game,p))),"Alteração após a confirmação bloqueia toda a limpeza");
        Put(game,extras[0],extras[0]);
        engine.ApplyExtraReset(engine.PlanExtraReset(),quiet);
        check(extras.All(p=>!File.Exists(Safe.In(game,p)))&&preserved.All(p=>File.ReadAllText(Safe.In(game,p))==p),
            "Retira extras e preserva arquivos do catálogo, jogo base e ajustes fora do escopo");
        check(engine.PlanExtraReset().Count==0,"Segunda limpeza é idempotente");
        Put(game,extras[0],"destino ocupado");
        engine.Undo(quiet);
        check(File.ReadAllText(Safe.In(game,extras[0]))=="destino ocupado"&&extras.Skip(1).All(p=>File.ReadAllText(Safe.In(game,p))==p),
            "Desfazer preserva destino ocupado e restaura os demais");
        File.Delete(Safe.In(game,extras[0]));
        engine.Undo(quiet);
        check(extras.All(p=>File.ReadAllText(Safe.In(game,p))==p),"Desfazer pendente retoma quando o destino fica livre");
        bool failed=false;
        try { engine.ApplyExtraReset(engine.PlanExtraReset(),message=>{if(message.StartsWith("Guardado:",StringComparison.Ordinal))throw new IOException("Falha de teste");}); }
        catch(IOException) { failed=true; }
        check(failed&&extras.All(p=>File.ReadAllText(Safe.In(game,p))==p),"Falha durante movimentação restaura arquivos já movidos");
        Safe.WriteJson(Path.Combine(root,"resultado-limpeza.json"),new{Passed=checks.Count,Tests=checks,RealGameTouched=false});
    }
}
