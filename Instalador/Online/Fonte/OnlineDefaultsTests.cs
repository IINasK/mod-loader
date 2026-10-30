using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

public static class OnlineDefaultsTests {
    const string Ini="red4ext/plugins/mod_settings/user.ini";
    const string Shift="bin/x64/plugins/cyber_engine_tweaks/mods/Shift/userConfig.json";
    const string Hotline="bin/x64/plugins/cyber_engine_tweaks/mods/NPCD_Hotline/mod_user_settings.json";
    static string Hash(string value){using(var sha=SHA256.Create())return Safe.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));}
    static FixedCatalog Clone(FixedCatalog value){return Safe.Json.Deserialize<FixedCatalog>(Safe.Json.Serialize(value));}
    static void Put(string root,string relative,string content){string path=Safe.In(root,relative);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,content,new UTF8Encoding(false));}
    static FixedDefault Entry(string id,string mod,string version,string path,string format,string section,string content){return new FixedDefault{Id=id,ModId=mod,Version=version,Path=path,Format=format,Section=section,Content=content,Sha256=Hash(content)};}
    static FixedCatalog Catalog(){return new FixedCatalog{Packages=new List<FixedMod>{
        new FixedMod{Id="mod-22340",Version="1.14.0",Files=new List<FixedFile>{new FixedFile{Path="r6/scripts/fixture-shift.reds"}}},
        new FixedMod{Id="15-NCPDHotline",Version="1.0",Files=new List<FixedFile>{new FixedFile{Path="r6/scripts/fixture-hotline.reds"}}},
        new FixedMod{Id="mod-33954",Version="1.5",Files=new List<FixedFile>{new FixedFile{Path="r6/scripts/fixture-driving.reds"}}},
        new FixedMod{Id="16-Cyberpsycho",Version="1.0",Files=new List<FixedFile>{new FixedFile{Path="r6/scripts/fixture-cyberpsycho.reds"}}}
    },Defaults=new List<FixedDefault>{
        Entry("shift-config","mod-22340","1.14.0",Shift,"json",null,"{\"format_version\":2,\"settings\":{\"shakePreset\":\"vibrations\",\"enabled\":true}}"),
        Entry("hotline-config","15-NCPDHotline","1.0",Hotline,"json",null,"{\"lastLanguageSelected\":\"pt-br\",\"enableAutoMode\":false}"),
        Entry("drive-config","mod-33954","1.5",Ini,"ini-section","ImmersiveDrivingSettings","{\"throttleNormalPct\":\"20\",\"enabled\":\"true\"}"),
        Entry("cyber-config","16-Cyberpsycho","1.0",Ini,"ini-section","CyberpsychoEncountersSettings","{\"cooldownMinutes\":\"30\"}")
    }};}
    public static void Run(string parent){
        string root=Path.Combine(Path.GetFullPath(parent),"defaults-"+Guid.NewGuid().ToString("N").Substring(0,12));Directory.CreateDirectory(root);var passed=new List<string>();
        Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("TESTE DEFAULTS FALHOU: "+name);passed.Add(name);};
        Action<Action,string> refuse=(action,name)=>{bool refused=false;try{action();}catch(IOException){refused=true;}check(refused,name);};
        var c=Catalog();ModDefaults.Validate(c);check(true,"Padrões validados por versão, hash, tipo e destino explícito");
        string game=Safe.In(root,"g");Directory.CreateDirectory(game);string backups=Safe.In(root,"backups");
        check(ModDefaults.Needs(c,"mod-22340",game)&&ModDefaults.Needs(c,"mod-33954",game),"Configurações ausentes são detectadas sem pacote de mod no cache");
        ModDefaults.Apply(c,c.Packages.Where(m=>m.Id=="mod-33954"),game,Safe.In(backups,"first"),s=>{});
        check(File.Exists(Safe.In(game,Ini))&&!File.Exists(Safe.In(game,Shift))&&!File.ReadAllText(Safe.In(game,Ini)).Contains("Cyberpsycho"),"Seleção aplica somente seção do mod escolhido, sem configurações de outros mods");
        check(!ModDefaults.Needs(c,"mod-33954",game)&&ModDefaults.Needs(c,"16-Cyberpsycho",game),"Seções já presentes não ficam pendentes");
        byte[] before=File.ReadAllBytes(Safe.In(game,Ini));ModDefaults.Apply(c,c.Packages.Where(m=>m.Id=="16-Cyberpsycho"),game,Safe.In(backups,"second"),s=>{});byte[] after=File.ReadAllBytes(Safe.In(game,Ini));
        check(after.Take(before.Length).SequenceEqual(before)&&Encoding.UTF8.GetString(after).Contains("cooldownMinutes = 30"),"Acrescentar seção preserva byte a byte o conteúdo anterior");
        check(Directory.GetFiles(Safe.In(backups,"second"),"*.bin").Length==1&&Directory.GetFiles(Safe.In(backups,"second"),"*.bin").All(path=>File.ReadAllBytes(path).SequenceEqual(before)),"Backup original é conferido e separado do manifesto");
        string noWork=Safe.In(backups,"repeat");ModDefaults.Apply(c,c.Packages.Where(m=>m.Id=="mod-33954"),game,noWork,s=>{});
        check(!Directory.Exists(noWork)&&File.ReadAllBytes(Safe.In(game,Ini)).SequenceEqual(after),"Repetição não altera arquivos nem cria outro backup");
        string caseGame=Safe.In(root,"case");Directory.CreateDirectory(caseGame);string personal="\uFEFF; user comment\r\n[immersivedrivingsettings]\r\nenabled = false\r\nthrottleNormalPct = 99\r\n[Other Mod]\r\ncustom = keep\r\n";Put(caseGame,Ini,personal);byte[] personalBytes=File.ReadAllBytes(Safe.In(caseGame,Ini));
        ModDefaults.Apply(c,c.Packages,caseGame,Safe.In(backups,"case"),s=>{});
        check(File.ReadAllBytes(Safe.In(caseGame,Ini)).Take(personalBytes.Length).SequenceEqual(personalBytes)&&File.ReadAllText(Safe.In(caseGame,Ini)).Contains("throttleNormalPct = 99"),"Seção é case-insensitive; BOM, comentários, valores pessoais e seções desconhecidas são preservados");
        check(File.Exists(Safe.In(caseGame,Shift))&&File.Exists(Safe.In(caseGame,Hotline)),"Arquivos JSON ausentes são criados com os padrões embutidos");
        Put(caseGame,Shift,"{\"settings\":{\"enabled\":false}} ");string custom=File.ReadAllText(Safe.In(caseGame,Shift));ModDefaults.Apply(c,c.Packages,caseGame,Safe.In(backups,"preserve"),s=>{});
        check(File.ReadAllText(Safe.In(caseGame,Shift))==custom&&!ModDefaults.Needs(c,"mod-22340",caseGame),"JSON preexistente ou editado nunca é sobrescrito nem marcado pendente");
        var bad=Clone(c);bad.Defaults[0].Content="{}";refuse(()=>ModDefaults.Validate(bad),"Conteúdo com hash divergente é recusado");
        bad=Clone(c);bad.Defaults[0].Version="different";refuse(()=>ModDefaults.Validate(bad),"Preset de outra versão é recusado");
        bad=Clone(c);bad.Defaults[0].Path="../escape.json";refuse(()=>ModDefaults.Validate(bad),"Fuga de caminho é recusada");
        bad=Clone(c);bad.Defaults[0].Path="bin/x64/plugins/cyber_engine_tweaks/mods/SpotOnRadio/config.json";refuse(()=>ModDefaults.Validate(bad),"Arquivo de conta não autorizado não pode virar padrão");
        bad=Clone(c);bad.Defaults[0].Format="lua";refuse(()=>ModDefaults.Validate(bad),"Código executável não é aceito como configuração embutida");
        bad=Clone(c);bad.Defaults[0].Content="{\"password\":\"forbidden\"}";bad.Defaults[0].Sha256=Hash(bad.Defaults[0].Content);refuse(()=>ModDefaults.Validate(bad),"Credenciais são recusadas mesmo com hash válido");
        bad=Clone(c);bad.Defaults[0].Content="{\"path\":\"C:\\\\private\"}";bad.Defaults[0].Sha256=Hash(bad.Defaults[0].Content);refuse(()=>ModDefaults.Validate(bad),"Caminhos pessoais absolutos são recusados");
        bad=Clone(c);bad.Defaults[2].Content="{\"enabled\":true}";bad.Defaults[2].Sha256=Hash(bad.Defaults[2].Content);refuse(()=>ModDefaults.Validate(bad),"INI exige valores escalares em formato textual previsto");
        bad=Clone(c);bad.Packages[0].Files.Add(new FixedFile{Path=Shift});refuse(()=>ModDefaults.Validate(bad),"Configuração não pode substituir arquivo fixado do pacote");
        bad=Clone(c);bad.Defaults.Add(bad.Defaults[0]);refuse(()=>ModDefaults.Validate(bad),"Preset repetido é recusado");
        bad=Clone(c);bad.Packages[0].Immutable=true;refuse(()=>ModDefaults.Validate(bad),"Padrões não alteram a base imutável");
        bad=Clone(c);bad.Defaults[0].Content="[1,true]";bad.Defaults[0].Sha256=Hash(bad.Defaults[0].Content);refuse(()=>ModDefaults.Validate(bad),"Raiz JSON que não é objeto de dados é recusada");
        bad=Clone(c);bad.Defaults[0].Content="{\"name\":\""+new string('é',140000)+"\"}";bad.Defaults[0].Sha256=Hash(bad.Defaults[0].Content);refuse(()=>ModDefaults.Validate(bad),"Limite de conteúdo considera bytes UTF-8");
        bad=Clone(c);bad.Defaults[2].Section="Other.ModSettings";refuse(()=>ModDefaults.Validate(bad),"Classe de configuração não prevista é recusada");
        bad=Clone(c);bad.Defaults[0].Content="{\"values\":[1,true,null,{\"preset\":\"safe\"}]}";bad.Defaults[0].Sha256=Hash(bad.Defaults[0].Content);ModDefaults.Validate(bad);check(true,"JSON aceita arrays e objetos aninhados de dados sem código");
        string malformed=Safe.In(root,"malformed");Directory.CreateDirectory(malformed);Put(malformed,Ini,"[broken\nkey = value");string malformedBefore=Safe.Hash(Safe.In(malformed,Ini));
        refuse(()=>ModDefaults.Apply(c,c.Packages,malformed,Safe.In(backups,"malformed"),s=>{}),"INI malformado é recusado antes de qualquer escrita");
        check(Safe.Hash(Safe.In(malformed,Ini))==malformedBefore&&!File.Exists(Safe.In(malformed,Shift)),"Planejamento completo impede JSON parcial quando o INI está inválido");
        string invalid=Safe.In(root,"encoding");Directory.CreateDirectory(Path.GetDirectoryName(Safe.In(invalid,Ini)));File.WriteAllBytes(Safe.In(invalid,Ini),new byte[]{255,254,91,0,65,0,93,0});
        refuse(()=>ModDefaults.Apply(c,c.Packages,invalid,Safe.In(backups,"encoding"),s=>{}),"INI em codificação não suportada é preservado, sem conversão silenciosa");
        File.WriteAllBytes(Safe.In(invalid,Ini),new byte[]{91,65,93,10,120,61,192,175});
        refuse(()=>ModDefaults.Apply(c,c.Packages,invalid,Safe.In(backups,"invalid-utf8"),s=>{}),"UTF-8 inválido é recusado sem caracteres substitutos");
        string rollback=Safe.In(root,"rollback");Directory.CreateDirectory(rollback);Put(rollback,Ini,"[User]\nkeep = yes\n");string original=File.ReadAllText(Safe.In(rollback,Ini));var iniFirst=Clone(c);iniFirst.Defaults=iniFirst.Defaults.OrderBy(d=>d.Format=="ini-section"?0:1).ToList();
        refuse(()=>ModDefaults.Apply(iniFirst,iniFirst.Packages,rollback,Safe.In(backups,"rollback"),s=>{},1),"Falha injetada aciona rollback");
        check(File.ReadAllText(Safe.In(rollback,Ini))==original&&!File.Exists(Safe.In(rollback,Shift)),"Rollback restaura INI anterior e não deixa configurações seguintes");
        string conflict=Safe.In(root,"conflict");Directory.CreateDirectory(conflict);int callbacks=0;
        refuse(()=>ModDefaults.Apply(c,c.Packages,conflict,Safe.In(backups,"conflict"),s=>{if(++callbacks==1)Put(conflict,Hotline,"{\"changed\":true}");}),"Mudança após planejamento bloqueia a escrita antes de sobrescrever");
        check(!File.Exists(Safe.In(conflict,Shift))&&File.ReadAllText(Safe.In(conflict,Hotline))=="{\"changed\":true}","Conflito preserva novo arquivo do usuário e reverte apenas o padrão próprio");
        string edited=Safe.In(root,"edited");Directory.CreateDirectory(edited);callbacks=0;
        refuse(()=>ModDefaults.Apply(c,c.Packages,edited,Safe.In(backups,"edited"),s=>{if(++callbacks==1){Put(edited,Shift,"{\"personal\":true}");Put(edited,Hotline,"{\"personal\":true}");}}),"Rollback com edição concorrente não toma autoria de dados pessoais");
        check(File.ReadAllText(Safe.In(edited,Shift))=="{\"personal\":true}"&&File.ReadAllText(Safe.In(edited,Hotline))=="{\"personal\":true}","Edição posterior permanece intacta mesmo na falha");
        var none=Catalog();none.Defaults=null;ModDefaults.Apply(none,none.Packages,Safe.In(root,"none"),Safe.In(backups,"none"),s=>{});check(!ModDefaults.Needs(none,"mod-22340",Safe.In(root,"none"))&&!Directory.Exists(Safe.In(root,"none")),"Catálogo antigo sem padrões permanece sem efeitos novos");
        Safe.WriteJson(Path.Combine(root,"resultado-defaults.json"),new{Passed=passed.Count,Tests=passed,RealGameTouched=false,AccountsRead=false,PayloadExecuted=false});
    }
}
