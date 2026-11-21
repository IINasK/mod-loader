using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public static class InstalledSnapshotConfig {
    static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
    internal static byte[] Read(string path) {
        Safe.NoLinks(path);
        using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))return InstalledSnapshot.ReadBytes(input,1048576);
    }
    internal static Dictionary<string,object> Object(string text) {
        if(text==null || Utf8.GetByteCount(text)>262144)throw new IOException("Configuração grande demais.");
        Dictionary<string,object> result;
        try{result=Safe.Json.DeserializeObject(text) as Dictionary<string,object>;}catch(Exception ex){if(!(ex is ArgumentException)&&!(ex is InvalidOperationException))throw;throw new IOException("Configuração JSON inválida.",ex);}
        if(result==null)throw new IOException("A configuração deve ser um objeto JSON.");
        ModDefaults.CheckData(result,0);return result;
    }
    internal static string Json(byte[] bytes) {return Safe.Json.Serialize(Object(Decode(bytes)));}
    internal static string Decode(byte[] bytes) {
        if(bytes==null)return "";
        if(bytes.Length>1048576)throw new IOException("Configuração grande demais.");
        try{string text=Utf8.GetString(bytes).TrimStart('\uFEFF');if(text.IndexOf('\0')>=0)throw new IOException("Configuração contém caracteres inválidos.");return text;}
        catch(DecoderFallbackException ex){throw new IOException("A configuração deve usar UTF-8.",ex);}
    }
    internal static string Section(byte[] bytes,string section) {
        var sections=ParseIni(Decode(bytes));Dictionary<string,object> values;
        return sections.TryGetValue(section,out values)?Safe.Json.Serialize(values):null;
    }
    static Dictionary<string,Dictionary<string,object>> ParseIni(string text) {
        var result=new Dictionary<string,Dictionary<string,object>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string,object> values=null;
        foreach(string raw in text.Split('\n')) {
            string line=raw.Trim();if(line.Length==0||line.StartsWith(";")||line.StartsWith("#"))continue;
            if(line.StartsWith("[")) {
                var match=Regex.Match(line,@"^\[([^\[\]\r\n]+)\]\s*(?:[;#].*)?$");
                if(!match.Success||result.ContainsKey(match.Groups[1].Value))throw new IOException("Configuração INI ambígua.");
                values=new Dictionary<string,object>(StringComparer.OrdinalIgnoreCase);result.Add(match.Groups[1].Value,values);
            }else {
                int equal=line.IndexOf('=');if(values==null||equal<=0)throw new IOException("Configuração INI inválida.");
                string key=line.Substring(0,equal).Trim(),value=line.Substring(equal+1).Trim();
                if(values.ContainsKey(key))throw new IOException("Configuração INI contém chave repetida.");values.Add(key,value);
            }
        }
        return result;
    }
    internal static void Validate(InstalledSnapshotFile file) {
        if(file.Kind==PortableLuaSettings.Kind){
            if(!file.Generated||file.Section!=null||!PortableLuaSettings.IsSettingsPath(file.Path))throw new IOException("Formato de configuração Lua inválido.");
            byte[] raw=PortableLuaSettings.Bytes(file.Content);
            if(raw.LongLength!=file.Size||InstalledSnapshot.Hash(raw)!=file.Sha256)throw new IOException("Hash da configuração Lua divergente.");
            return;
        }
        var data=Object(file.Content);
        if(file.Kind=="ini-section") {
            if(String.IsNullOrWhiteSpace(file.Section)||file.Section.Length>256||file.Section.IndexOfAny(new[]{'[',']','\r','\n'})>=0||data.Count==0||data.Any(p=>!Regex.IsMatch(p.Key,"^[A-Za-z_][A-Za-z0-9_]{0,127}$")||!(p.Value is string)||!Regex.IsMatch((string)p.Value,"^[A-Za-z0-9_.+-]{1,128}$")))
                throw new IOException("Seção INI inválida.");
        }else if(file.Kind!="json"||!String.IsNullOrEmpty(file.Section))throw new IOException("Formato de configuração inválido.");
        byte[] bytes=Utf8.GetBytes(file.Content);
        if(bytes.LongLength!=file.Size||InstalledSnapshot.Hash(bytes)!=file.Sha256)throw new IOException("Hash da configuração divergente.");
    }
    public static byte[] Apply(InstalledSnapshotFile file,byte[] currentBytes) {
        Validate(file);
        if(file.Kind==PortableLuaSettings.Kind)return Convert.FromBase64String(file.Content);
        if(file.Kind=="json")return Utf8.GetBytes(file.Content);
        string current=Decode(currentBytes);ParseIni(current);
        string newline=current.Contains("\r\n")?"\r\n":"\n";
        var output=new StringBuilder();bool skipping=false;
        foreach(string raw in current.Split(new[]{'\n'},StringSplitOptions.None)) {
            string line=raw.Trim();var header=Regex.Match(line,@"^\[([^\[\]\r\n]+)\]\s*(?:[;#].*)?$");
            if(header.Success)skipping=String.Equals(header.Groups[1].Value,file.Section,StringComparison.OrdinalIgnoreCase);
            if(!skipping)output.Append(raw.TrimEnd('\r')).Append(newline);
        }
        if(output.Length>0)output.Append(newline);
        output.Append('[').Append(file.Section).Append(']').Append(newline);
        foreach(var pair in Object(file.Content).OrderBy(p=>p.Key,StringComparer.Ordinal))output.Append(pair.Key).Append(" = ").Append((string)pair.Value).Append(newline);
        return Utf8.GetBytes(output.ToString());
    }
}

// Only a literal return-table is accepted. Nothing is evaluated by a Lua runtime.
internal sealed class PortableLuaSettings {
    internal const string Kind="lua-settings";
    const int MaxBytes=262144;
    readonly string source;
    int offset;

    static readonly Regex Identifier=new Regex(@"\G[A-Za-z_][A-Za-z0-9_]{0,127}",RegexOptions.CultureInvariant);
    static readonly Regex Number=new Regex(@"\G-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?",RegexOptions.CultureInvariant);
    static readonly Regex PrivateKey=new Regex(@"password|passwd|secret|credential|cookie|token|user|account|machine|deviceid|address|directory|path|email|hostname|license|login|apikey|privatekey|accesskey|session|steamid|discord|oauth|auth|bearer|(?:^|_)(?:key|ip|host|port|home|name|id)(?:$|_)",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    static readonly Regex Comment=new Regex(@"\A[A-Za-z0-9 \t.,:;'()_+-]{0,768}\z",RegexOptions.CultureInvariant);
    static readonly Regex PrivateComment=new Regex(@"password|passwd|secret|credential|cookie|token|api.?key|private.?key|username|user.?id|session.?id|account|(?:\d{1,3}\.){3}\d{1,3}|[A-Za-z0-9_-]{32,}|\d{9,}",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    static readonly Regex ShortValue=new Regex(@"\A(?:[A-Za-z][A-Za-z0-9_ -]{0,31})?\z",RegexOptions.CultureInvariant);
    static readonly HashSet<string> Keywords=new HashSet<string>("and break do else elseif end false for function goto if in local nil not or repeat return then true until while".Split(' '),StringComparer.Ordinal);

    PortableLuaSettings(string text){source=text;}
    internal static bool IsSettingsPath(string path){
        string name=Path.GetFileName(path);
        return name!=null&&(name.Equals("settings.lua",StringComparison.OrdinalIgnoreCase)||name.EndsWith("_settings.lua",StringComparison.OrdinalIgnoreCase));
    }
    internal static string Encode(byte[] raw){
        Validate(raw);
        return Convert.ToBase64String(raw);
    }
    internal static byte[] Bytes(string content){
        if(content==null||content.Length>((MaxBytes+2)/3)*4)throw Invalid();
        byte[] raw;
        try{raw=Convert.FromBase64String(content);}catch(FormatException){throw Invalid();}
        if(Convert.ToBase64String(raw)!=content)throw Invalid();
        Validate(raw);
        return raw;
    }
    static IOException Invalid(){return new IOException("Somente tabelas Lua de configurações portáteis podem ser compartilhadas.");}
    static void Validate(byte[] raw){
        if(raw==null||raw.Length==0||raw.Length>MaxBytes)throw Invalid();
        int start=0;
        Encoding encoding=new UTF8Encoding(false,true);
        if(raw.Length>=2&&((raw[0]==255&&raw[1]==254)||(raw[0]==254&&raw[1]==255))){
            encoding=new UnicodeEncoding(raw[0]==254,false,true);
            start=2;
        }else if(raw.Length>=3&&raw[0]==239&&raw[1]==187&&raw[2]==191)start=3;
        string text;
        try{text=encoding.GetString(raw,start,raw.Length-start);}catch(DecoderFallbackException){throw Invalid();}
        var parser=new PortableLuaSettings(text);
        parser.Space();
        if(parser.Word()!="return")throw Invalid();
        parser.Space();
        parser.Table(0);
        parser.Space();
        if(parser.Take(';'))parser.Space();
        if(parser.offset!=text.Length)throw Invalid();
    }
    bool Take(char ch){
        if(offset>=source.Length||source[offset]!=ch)return false;
        offset++;
        return true;
    }
    void Space(){
        while(offset<source.Length){
            char ch=source[offset];
            if(ch==' '||ch=='\t'||ch=='\r'||ch=='\n'){offset++;continue;}
            if(ch!='-'||offset+1>=source.Length||source[offset+1]!='-')break;
            int start=offset+2;offset=start;
            while(offset<source.Length&&source[offset]!='\r'&&source[offset]!='\n')offset++;
            if(offset-start>768)throw Invalid();
            string comment=source.Substring(start,offset-start);
            if(!Comment.IsMatch(comment)||PrivateComment.IsMatch(comment))throw Invalid();
        }
    }
    string Word(){
        var word=Identifier.Match(source,offset);
        if(!word.Success)return null;
        offset+=word.Length;
        return word.Value;
    }
    string Quoted(){
        if(offset>=source.Length||(source[offset]!='\''&&source[offset]!='"'))throw Invalid();
        char quote=source[offset++];
        int end=source.IndexOf(quote,offset);
        if(end<0||end-offset>128)throw Invalid();
        string literal=source.Substring(offset,end-offset);
        offset=end+1;
        return literal;
    }
    void Table(int depth){
        if(depth>=32||!Take('{'))throw Invalid();
        Space();
        var keys=new HashSet<string>(StringComparer.Ordinal);
        while(!Take('}')){
            int start=offset;
            string key;
            if(Take('[')){
                Space();key=Quoted();Space();
                if(!Take(']')||!Regex.IsMatch(key,@"\A[A-Za-z_][A-Za-z0-9_]{0,127}\z"))throw Invalid();
                Space();
                if(!Take('='))throw Invalid();
            }else{
                key=Word();Space();
                if(key==null||!Take('=')){key=null;offset=start;}
                else if(Keywords.Contains(key))throw Invalid();
            }
            if(key!=null&&(!keys.Add(key)||PrivateKey.IsMatch(key)))throw Invalid();
            Space();
            Value(depth);
            Space();
            if(!Take(',')&&!Take(';')){
                if(!Take('}'))throw Invalid();
                return;
            }
            Space();
        }
    }
    void Value(int depth){
        if(offset>=source.Length)throw Invalid();
        char ch=source[offset];
        if(ch=='{'){Table(depth+1);return;}
        if(ch=='\''||ch=='"'){
            string text=Quoted();
            if(!ShortValue.IsMatch(text)||PrivateKey.IsMatch(text))throw Invalid();
            return;
        }
        if(ch=='-'||(ch>='0'&&ch<='9')){
            var number=Number.Match(source,offset);
            if(!number.Success||number.Length>40)throw Invalid();
            double value;
            if(!Double.TryParse(number.Value,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out value)||Double.IsInfinity(value))throw Invalid();
            offset+=number.Length;return;
        }
        string word=Word();
        if(word!="true"&&word!="false")throw Invalid();
    }
}
