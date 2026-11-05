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
