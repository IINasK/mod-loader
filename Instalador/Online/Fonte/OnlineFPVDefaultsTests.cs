using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

public static class OnlineFPVDefaultsTests {
    const string Bindings="bin/x64/plugins/cyber_engine_tweaks/bindings.json";
    const string Profile="bin/x64/plugins/cyber_engine_tweaks/mods/FPVDrone/profiles/NASK.json";
    const string Index="bin/x64/plugins/cyber_engine_tweaks/mods/FPVDrone/FPVDrone_Profiles.json";
    static string Hash(string value){using(var sha=SHA256.Create())return Safe.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));}
    static void Put(string game,string path,string text){string p=Safe.In(game,path);Directory.CreateDirectory(Path.GetDirectoryName(p));File.WriteAllText(p,text,new UTF8Encoding(false));}
    static Dictionary<string,object> Map(object value){return (Dictionary<string,object>)value;}
    static Dictionary<string,object> Read(string game,string path){return Map(Safe.Json.DeserializeObject(File.ReadAllText(Safe.In(game,path))));}
    static FixedCatalog Clone(FixedCatalog c){return Safe.Json.Deserialize<FixedCatalog>(Safe.Json.Serialize(c));}
    public static void Run(string parent){
        string root=Path.Combine(Path.GetFullPath(parent),"online-testes-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var passed=new List<string>();
        Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("TESTE FPV DEFAULTS FALHOU: "+name);passed.Add(name);};
        Action<Action,string> refuse=(act,name)=>{bool failed=false;try{act();}catch(IOException){failed=true;}check(failed,name);};
        var production=FixedData.Load();FixedAsset asset;var c=OnlineTests.MakeCatalog(root,out asset);var mod=c.Packages[1];mod.Id="mod-31642";mod.Version=production.Packages.Single(m=>m.Id==mod.Id).Version;
        c.Defaults=production.Defaults.Where(d=>d.ModId==mod.Id).ToList();string originalIndex="{\"activePath\":\"profiles/5 INCH FS.json\",\"indexVersion\":5,\"legacyMigrationComplete\":true}";
        using(var zip=ZipFile.Open(Safe.In(root,"fixture.zip"),ZipArchiveMode.Update)){using(var w=new StreamWriter(zip.CreateEntry(Index).Open(),new UTF8Encoding(false)))w.Write(originalIndex);}
        mod.Files.Add(new FixedFile{Asset=asset.Id,Entry=Index,Path=Index,Size=Encoding.UTF8.GetByteCount(originalIndex),Sha256=Hash(originalIndex),UserData=true});asset.Size=new FileInfo(Safe.In(root,"fixture.zip")).Length;asset.Sha256=Safe.Hash(Safe.In(root,"fixture.zip"));FixedData.Validate(c);
        check(c.Defaults.Count==3,"FPV inclui somente perfil de curvas, atalhos e seleção inicial");
        var rates=Map(Map(Safe.Json.DeserializeObject(c.Defaults.Single(d=>d.Path==Profile).Content))["controls"]);rates=Map(rates["actualRates"]);
        check(Convert.ToDouble(Map(rates["roll"])["centerSensitivity"])==400&&Convert.ToDouble(Map(rates["roll"])["expo"])==0.5&&new[]{"pitch","yaw"}.All(a=>Convert.ToDouble(Map(rates[a])["centerSensitivity"])==500&&Convert.ToDouble(Map(rates[a])["expo"])==0)&&new[]{"roll","pitch","yaw"}.All(a=>Convert.ToDouble(Map(rates[a])["maxRate"])==660),"Perfil NASK usa os rates finais aprovados, sem recuperar a aproximação antiga");
        var physics=Map(Map(Safe.Json.DeserializeObject(c.Defaults.Single(d=>d.Path==Profile).Content))["physics"]);double gravity=Convert.ToDouble(physics["gravity"]),motor=Convert.ToDouble(Map(physics["powerSystem"])["referenceThrustToWeightRatio"]);
        check(Math.Abs(gravity-11.34)<0.000001&&Math.Abs(motor-8.407243)<0.000001&&Convert.ToDouble(physics["massGrams"])==400,"Gravidade, massa e referência de motor preservam o ajuste fino aprovado");
        check(Convert.ToDouble(physics["feedForward"])==120&&Convert.ToDouble(physics["tuneTightness"])==100,"Feedforward120 e Tightness100 são os padrões NASK aprovados");
        string content=String.Join("",c.Defaults.Select(d=>d.Content));check(!content.Contains("guid")&&!content.Contains("vendorId")&&!content.Contains("axisIndex")&&!content.Contains("device")&&!content.Contains("password"),"Identificação e calibração do DualSense não são exportadas");
        var desired=Map(Safe.Json.DeserializeObject(c.Defaults.Single(d=>d.Path==Bindings).Content));long binding=Convert.ToInt64(desired["fpv_drone_toggle"]);check(binding==61924494876344320L&&desired.Count==1,"Atalho de ativação salvo preserva inteiro64bits sem outros atalhos pessoais");
        string game=OnlineTests.Fixture(root,"merge");Put(game,Bindings,"{\"cet\":{\"overlay_key\":31525197391593472},\"OtherMod\":{\"other\":32088147345014784}}");
        ModDefaults.Apply(c,c.Packages.Where(m=>m.Id==mod.Id),game,Safe.In(root,"b1"),s=>{});var merged=Read(game,Bindings);
        check(Convert.ToInt64(Map(merged["FPVDrone"])["fpv_drone_toggle"])==binding&&Convert.ToInt64(Map(merged["cet"])["overlay_key"])==31525197391593472L&&Convert.ToInt64(Map(merged["OtherMod"])["other"])==32088147345014784L,"Mescla somente FPV preservando overlay e atalhos alheios com precisão64bits");
        check(Read(game,Index)["activePath"].Equals("profiles/NASK.json")&&File.Exists(Safe.In(game,Profile)),"Configuração ausente recebe perfil e índice novos");
        Put(game,Bindings,"{\"FPVDrone\":{\"fpv_drone_toggle\":0},\"cet\":{\"overlay_key\":31525197391593472}}");string personal=Safe.Hash(Safe.In(game,Bindings));Put(game,Profile,"{\"custom\":true}");Put(game,Index,originalIndex);string indexHash=Safe.Hash(Safe.In(game,Index));
        ModDefaults.Apply(c,c.Packages,game,Safe.In(root,"b2"),s=>{});
        check(Safe.Hash(Safe.In(game,Bindings))==personal&&File.ReadAllText(Safe.In(game,Profile))=="{\"custom\":true}","Atalho existente, inclusive zero, e perfil personalizado são preservados");
        check(Safe.Hash(Safe.In(game,Index))==indexHash,"Índice preexistente com aparência de fábrica também mantém seleção do usuário");
        Put(game,Bindings,"{\"OtherMod\":{\"used\":"+binding+"}}");string collision=Safe.Hash(Safe.In(game,Bindings));var messages=new List<string>();ModDefaults.Apply(c,c.Packages,game,Safe.In(root,"b3"),messages.Add);
        check(Safe.Hash(Safe.In(game,Bindings))==collision&&messages.Any(s=>s.Contains("já usado")),"Tecla em uso não é duplicada; conflito orienta configuração manual");
        Put(game,Bindings,"invalid json");string invalid=Safe.Hash(Safe.In(game,Bindings));refuse(()=>ModDefaults.Apply(c,c.Packages,game,Safe.In(root,"b4"),s=>{}),"JSON CET malformado é recusado");check(Safe.Hash(Safe.In(game,Bindings))==invalid,"Falha de leitura não substitui atalhos existentes");
        var bad=Clone(c);bad.Defaults.Single(d=>d.Format=="cet-bindings").Section="cet";refuse(()=>ModDefaults.Validate(bad),"Registro FPV não pode modificar tecla do overlay");
        bad=Clone(c);bad.Packages[1].Files.Single(f=>f.Path==Index).UserData=false;refuse(()=>ModDefaults.Validate(bad),"Exceção do índice não permite substituir código fixado");
        var cache=new DownloadCache(Safe.In(root,"cache"));cache.Import(Safe.In(root,"fixture.zip"),asset);string clean=OnlineTests.Fixture(root,"install");var engine=new FixedInstaller(c,cache,clean,null,true);engine.Install(new[]{mod.Id},true,s=>{});
        check(Read(clean,Index)["activePath"].Equals("profiles/NASK.json")&&c.Packages.All(m=>FixedData.Scan(m,clean).IsCorrect),"Instalação nova ativa NASK somente no índice recém-copiado");
        var plan=AutoInstallPlan.Build(c,new[]{mod.Id},clean,new DownloadCache(Safe.In(root,"empty-cache")),false);check(plan.Complete&&plan.ManualMod==null,"Fila conclui com perfis pessoais sem pedir ZIP novamente");
        engine.DeactivateMany(new[]{mod.Id},s=>{});check(File.Exists(Safe.In(clean,Profile))&&File.Exists(Safe.In(clean,Bindings))&&File.Exists(Safe.In(clean,Index)),"Remover o drone preserva atalhos e perfis");
        string rollback=OnlineTests.Fixture(root,"rollback");Put(rollback,Bindings,"{\"Other\":{\"key\":123}}");string before=Safe.Hash(Safe.In(rollback,Bindings));refuse(()=>ModDefaults.Apply(c,c.Packages,rollback,Safe.In(root,"b5"),s=>{},2),"Falha injetada reverte os padrões FPV");check(Safe.Hash(Safe.In(rollback,Bindings))==before&&!File.Exists(Safe.In(rollback,Profile)),"Rollback restaura atalhos alheios e remove somente perfil criado nesta tentativa");
        Safe.WriteJson(Path.Combine(root,"resultado-fpv-defaults.json"),new{Passed=passed.Count,Tests=passed,Profile="NASK",Gravity=gravity,MotorReference=motor,RealGameTouched=false,PayloadExecuted=false});
    }
}
