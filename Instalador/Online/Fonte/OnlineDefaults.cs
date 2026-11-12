using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

public sealed class FixedDefault {
    public string Id,ModId,Version,Path,Format,Section,Content,Sha256;
}

public static class ModDefaults {
    const string IniPath="red4ext/plugins/mod_settings/user.ini";
    static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
    static readonly Dictionary<string,string> Sections=new Dictionary<string,string>(StringComparer.Ordinal){
        {"Audioware","Audioware.AudiowareConfig"},{"16-Cyberpsycho","CyberpsychoEncountersSettings"},
        {"17-DarkFuture","DarkFuture.Settings.DFSettings"},{"mod-33954","ImmersiveDrivingSettings"},
        {"14-NightCityAllies","NightCityAllies.Settings.NCASettings"},{"mod-36378","UnlimitedAttributeReset.UnlimitedAttributeResetConfig"}
    };
    static readonly Dictionary<string,string> JsonPaths=new Dictionary<string,string>(StringComparer.Ordinal){
        {"mod-22340","bin/x64/plugins/cyber_engine_tweaks/mods/Shift/userConfig.json"},
        {"15-NCPDHotline","bin/x64/plugins/cyber_engine_tweaks/mods/NPCD_Hotline/mod_user_settings.json"},
        {"mod-35626","bin/x64/plugins/cyber_engine_tweaks/mods/ExpandedDualWielding/settings-v2.json"}
    };
    sealed class Change {
        public string Relative,Path,BeforeHash,AfterHash;
        public byte[] Before,After;
        public bool Written;
    }
    public static void Validate(FixedCatalog catalog){
        if(catalog==null)throw new ArgumentNullException("catalog");
        if(catalog.Defaults==null)return;
        if(catalog.Defaults.Count>64)throw new IOException("Há padrões de configuração demais no catálogo.");
        var ids=new HashSet<string>(StringComparer.Ordinal);var targets=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
        foreach(var entry in catalog.Defaults){
            if(entry==null||!Regex.IsMatch(entry.Id??"","^[A-Za-z0-9-]{1,100}$")||!ids.Add(entry.Id)||entry.Content==null||entry.Content.Length>262144)throw new IOException("Padrão de configuração inválido.");
            var mod=catalog.Packages==null?null:catalog.Packages.SingleOrDefault(m=>m.Id==entry.ModId);
            if(mod==null||mod.Immutable||entry.Version!=mod.Version)throw new IOException("A configuração não corresponde à versão fixada do mod.");
            if(!FixedData.HashValid(entry.Sha256)||Hash(Utf8.GetBytes(entry.Content))!=entry.Sha256)throw new IOException("A configuração embutida foi alterada.");
            int bytes=Utf8.GetByteCount(entry.Content);total+=bytes;if(bytes>262144||total>1048576)throw new IOException("Padrões de configuração excedem o limite.");
            if(!Safe.Allowed(entry.Path))throw new IOException("Destino de configuração inválido.");Safe.In(System.IO.Path.GetTempPath(),entry.Path);
            var collisions=catalog.Packages.SelectMany(m=>m.Files??new List<FixedFile>()).Where(f=>String.Equals(f.Path,entry.Path,StringComparison.OrdinalIgnoreCase)).ToList();
            if(collisions.Count>0)throw new IOException("Uma configuração não pode substituir arquivo fixado do mod.");
            var values=ParseObject(entry.Content);CheckData(values,0);
            string target=entry.Path;
            if(entry.Format=="ini-section"){
                string section;if(entry.Path!=IniPath||!Sections.TryGetValue(entry.ModId,out section)||entry.Section!=section)throw new IOException("Seção de configuração não autorizada.");
                if(values.Count==0||values.Any(p=>!Regex.IsMatch(p.Key,"^[A-Za-z_][A-Za-z0-9_]{0,127}$")||!(p.Value is string)||!Regex.IsMatch((string)p.Value,"^[A-Za-z0-9_.+-]{1,128}$")))throw new IOException("Valores INI de configuração inválidos.");
                target+="\n"+entry.Section;
            }else if(entry.Format=="json"){
                string path;if(!JsonPaths.TryGetValue(entry.ModId,out path)||entry.Path!=path||!String.IsNullOrEmpty(entry.Section))throw new IOException("Arquivo de configuração não autorizado.");
            }else throw new IOException("Formato de configuração não suportado.");
            if(!targets.Add(target))throw new IOException("Destino de configuração repetido.");
        }
    }
    public static bool Needs(FixedCatalog catalog,string modId,string game){
        if(catalog==null)throw new ArgumentNullException("catalog");
        if(catalog.Defaults==null||!catalog.Defaults.Any(d=>d!=null&&d.ModId==modId))return false;
        Validate(catalog);return Prepare(catalog,new HashSet<string>(new[]{modId},StringComparer.Ordinal),game).Count>0;
    }
    public static void Apply(FixedCatalog catalog,IEnumerable<FixedMod> mods,string game,string backupRoot,Action<string> report,int failAfter=-1,ISet<string> freshUserData=null){
        if(catalog==null||mods==null)throw new ArgumentNullException();
        if(catalog.Defaults==null||catalog.Defaults.Count==0)return;
        Validate(catalog);var selected=new HashSet<string>(mods.Select(m=>m.Id),StringComparer.Ordinal);
        var changes=Prepare(catalog,selected,game,report,freshUserData);if(changes.Count==0)return;
        Safe.NoLinks(backupRoot);Directory.CreateDirectory(backupRoot);
        foreach(var change in changes)if(change.Before!=null){string backup=Backup(backupRoot,change.Relative);WriteAtomic(backup,change.Before,null);if(Safe.Hash(backup)!=change.BeforeHash)throw new IOException("Backup de configuração não conferiu.");}
        SaveManifest(backupRoot,changes,"preparado");int written=0;
        try{
            foreach(var change in changes){
                WriteAtomic(change.Path,change.After,change.BeforeHash);change.Written=true;written++;SaveManifest(backupRoot,changes,"aplicando");
                if(report!=null)report("Configuração padrão aplicada: "+change.Relative);
                if(failAfter>=0&&written>=failAfter)throw new IOException("Falha de teste ao aplicar configurações.");
            }
            SaveManifest(backupRoot,changes,"concluido");
        }catch{
            foreach(var change in changes.AsEnumerable().Reverse()){
                if(!change.Written)continue;
                try{
                    Safe.NoLinks(change.Path);if(!File.Exists(change.Path)||Safe.Hash(change.Path)!=change.AfterHash)continue;
                    if(change.Before==null)File.Delete(change.Path);else WriteAtomic(change.Path,change.Before,change.AfterHash);
                }catch(IOException){}catch(UnauthorizedAccessException){}
            }
            try{SaveManifest(backupRoot,changes,"falhou-preservando-edicoes-posteriores");}catch(IOException){}catch(UnauthorizedAccessException){}
            throw;
        }
    }
    static List<Change> Prepare(FixedCatalog catalog,HashSet<string> selected,string game,Action<string> report=null,ISet<string> freshUserData=null){
        Safe.NoLinks(game);var changes=new List<Change>();
        foreach(var group in catalog.Defaults.Where(d=>selected.Contains(d.ModId)).GroupBy(d=>d.Path,StringComparer.OrdinalIgnoreCase)){
            string path=Safe.In(game,group.Key);if(Directory.Exists(path))throw new IOException("Uma pasta ocupa o destino da configuração: "+group.Key);
            if(group.First().Format=="json"){
                if(File.Exists(path))continue;byte[] content=Utf8.GetBytes(group.Single().Content);changes.Add(new Change{Relative=group.Key,Path=path,Before=null,After=content,AfterHash=Hash(content)});continue;
            }
            byte[] before=File.Exists(path)?ReadIni(path):null;string text=before==null?"":Decode(before);var sections=ReadSections(text);var append=new StringBuilder();string newline=text.Contains("\r\n")?"\r\n":before==null?"\r\n":"\n";
            foreach(var entry in group){
                if(sections.Contains(entry.Section))continue;
                if(append.Length>0||before!=null&&before.Length>0)append.Append(newline);
                append.Append('[').Append(entry.Section).Append(']').Append(newline);
                foreach(var pair in ParseObject(entry.Content).OrderBy(p=>p.Key,StringComparer.Ordinal))append.Append(pair.Key).Append(" = ").Append((string)pair.Value).Append(newline);
                sections.Add(entry.Section);
            }
            if(append.Length==0)continue;
            byte[] suffix=Utf8.GetBytes(append.ToString()),after=new byte[(before==null?0:before.Length)+suffix.Length];
            if(before!=null)Buffer.BlockCopy(before,0,after,0,before.Length);Buffer.BlockCopy(suffix,0,after,before==null?0:before.Length,suffix.Length);
            changes.Add(new Change{Relative=group.Key,Path=path,Before=before,BeforeHash=before==null?null:Hash(before),After=after,AfterHash=Hash(after)});
        }
        return changes;
    }
    static byte[] ReadIni(string path){if(new FileInfo(path).Length>1048576)throw new IOException("Arquivo de configurações existente é grande demais; foi preservado.");return File.ReadAllBytes(path);}
    static string Decode(byte[] bytes){try{string text=Utf8.GetString(bytes);if(text.IndexOf('\0')>=0)throw new IOException("Arquivo INI existente não é UTF-8; foi preservado.");return text.TrimStart('\uFEFF');}catch(DecoderFallbackException ex){throw new IOException("Arquivo INI existente não é UTF-8; foi preservado.",ex);}}
    static HashSet<string> ReadSections(string text){
        var sections=new HashSet<string>(StringComparer.OrdinalIgnoreCase);bool inSection=false;
        foreach(string raw in text.Split('\n')){
            string line=raw.Trim();if(line.Length==0||line.StartsWith(";")||line.StartsWith("#"))continue;
            if(line.StartsWith("[")){
                var match=Regex.Match(line,@"^\[([^\[\]\r\n]+)\]\s*(?:[;#].*)?$");
                if(!match.Success||!sections.Add(match.Groups[1].Value))throw new IOException("Cabeçalho INI existente é ambíguo; arquivo preservado.");inSection=true;
            }else if(!inSection||line.IndexOf('=')<=0)throw new IOException("Arquivo INI existente é inválido; foi preservado.");
        }
        return sections;
    }
    static Dictionary<string,object> ParseObject(string content){try{var value=Safe.Json.DeserializeObject(content) as Dictionary<string,object>;if(value==null)throw new IOException("Configuração deve conter um objeto de dados.");return value;}catch(ArgumentException ex){throw new IOException("Dados de configuração inválidos.",ex);}catch(InvalidOperationException ex){throw new IOException("Dados de configuração inválidos.",ex);}}
    internal static void CheckData(object value,int depth){
        if(depth>32)throw new IOException("Configuração contém dados profundos demais.");
        var map=value as Dictionary<string,object>;if(map!=null){foreach(var pair in map){if(pair.Key.Length>256||Regex.IsMatch(pair.Key,"password|passwd|secret|credential|oauth|cookie|bearer|authorization|access.?token|refresh.?token|client.?id|api.?key|^token$",RegexOptions.IgnoreCase))throw new IOException("Dados pessoais não são aceitos em padrões compartilhados.");CheckData(pair.Value,depth+1);}return;}
        var array=value as object[];if(array!=null){foreach(var item in array)CheckData(item,depth+1);return;}
        if(value==null||value is bool||value is int||value is long||value is decimal)return;
        if(value is double){if(Double.IsNaN((double)value)||Double.IsInfinity((double)value))throw new IOException("Número inválido na configuração.");return;}
        var text=value as string;if(text!=null&&text.Length<=4096&&!Regex.IsMatch(text,@"^[A-Za-z]:[\\/]|^\\\\|https?://",RegexOptions.IgnoreCase))return;
        throw new IOException("Valor não permitido na configuração.");
    }
    static void WriteAtomic(string path,byte[] content,string expected){
        Safe.NoLinks(path);if(Directory.Exists(path))throw new IOException("Uma pasta ocupa o destino da configuração.");
        string current=File.Exists(path)?Safe.Hash(path):null;if(current!=expected)throw new IOException("Configuração mudou durante a operação; alterações pessoais foram preservadas.");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));string tmp=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path),".defaults-"+Guid.NewGuid().ToString("N")+".tmp");
        try{
            using(var stream=new FileStream(tmp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(content,0,content.Length);stream.Flush(true);}
            Safe.NoLinks(path);current=File.Exists(path)?Safe.Hash(path):null;if(current!=expected)throw new IOException("Configuração mudou durante a operação; alterações pessoais foram preservadas.");
            if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);
        }finally{if(File.Exists(tmp))File.Delete(tmp);}
    }
    static string Backup(string root,string relative){return Safe.In(root,Hash(Utf8.GetBytes(relative))+".bin");}
    static void SaveManifest(string root,List<Change> changes,string status){Safe.WriteJson(Safe.In(root,"manifesto.json"),new{Status=status,Files=changes.Select(c=>new{Path=c.Relative,Before=c.BeforeHash,After=c.AfterHash,Written=c.Written,Backup=c.Before==null?null:System.IO.Path.GetFileName(Backup(root,c.Relative))}).ToArray()});}
    static string Hash(byte[] bytes){using(var sha=SHA256.Create())return Safe.Hex(sha.ComputeHash(bytes));}
}
