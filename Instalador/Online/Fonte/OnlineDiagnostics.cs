using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

public static class OnlineDiagnostics {
    const int TimelineLimit=240, TraceLimit=160, ProblemLimit=40;
    static readonly object gate=new object();
    static readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=2000000};
    static readonly Queue<Dictionary<string,object>> timeline=new Queue<Dictionary<string,object>>();
    static readonly Queue<Dictionary<string,object>> traces=new Queue<Dictionary<string,object>>();
    static readonly Queue<Dictionary<string,object>> problems=new Queue<Dictionary<string,object>>();
    static readonly HashSet<string> diagnosticFields=new HashSet<string>((
        "Name Mod ModVersion Variant VariantName Expected Actual Stage Phase Action State Result Status Code ErrorType Message FailureType Stack "+
        "Version AppVersion Catalog Id Side ElapsedSeconds HelperRunning LocalApiResponded PeerConnected ExitCode ProfileInUse Present "+
        "Correct Missing Different Original Cached Partial ExistedBeforeWatch Count Files Installed ActiveMods Issues "+
        "Workers DiscoveryMs ApplyMs RefreshMs TotalMs Size ExpectedSize ActualSize Sha256 ExpectedSha256 ActualSha256 "+
        "Template Preset DurationMs ElapsedMs UniqueHashes ImportsRead Mods Requested Verified Skipped InstalledCount Total Completed Pending Mode Asset Queue Generation FileId Path").Split(' '),
        StringComparer.OrdinalIgnoreCase);
    static readonly Regex privateText=new Regex(
        @"(?im)\b(?:https?|ftp|file|nxm)://[^\s""'<>]+|\bML-P2P[\w-]*:[^\s""'<>]+|\b(?:Bearer|Basic)\s+[a-z0-9+/_.=-]+|"+
        @"\b(?:api[-_ ]?key|token|password|senha|secret|authorization|credential|cookie)\b[""']?\s*[:=]\s*[^\r\n]+|"+
        @"(?<!\w)(?:[a-z]:[\\/]|\\\\|/(?:Users|home|mnt|tmp|var|private|Volumes)/)[^\r\n""'<>|]+|"+
        @"\b[\w.+-]+@[\w.-]+\.[a-z]{2,}\b|\b(?:\d{1,3}\.){3}\d{1,3}\b|(?<![\w:])(?:[0-9a-f]{0,4}:){2,}[0-9a-f]{0,4}(?![\w:])",
        RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    static string session;
    static DateTimeOffset started;
    static long sequence, dropped;
    static bool enabled;

    public static bool Enabled {get{lock(gate)return enabled;}}

    public static void Start(){
        lock(gate){
            Reset();
            started=DateTimeOffset.Now;
            session=Guid.NewGuid().ToString("N");
            enabled=true;
            Record("session.start",null,"Sessão iniciada.");
        }
    }
    public static void Stop(){lock(gate)Reset();}
    static void Reset(){
        enabled=false;session=null;sequence=0;dropped=0;
        timeline.Clear();traces.Clear();problems.Clear();
    }

    static string Redact(string value,int limit){
        if(String.IsNullOrWhiteSpace(value))return "";
        if(value.Length>4096)value=value.Substring(0,4096);
        try{value=privateText.Replace(value,"[omitido]");}
        catch(RegexMatchTimeoutException){return "[omitido]";}
        value=String.Concat(value.Where(c=>!Char.IsControl(c)||c=='\n'||c=='\r'||c=='\t')).Trim();
        return value.Length>limit?value.Substring(0,limit)+"…":value;
    }

    static Dictionary<string,object> SafeData(object value){
        var result=new Dictionary<string,object>();
        if(value==null)return result;
        var fields=json.DeserializeObject(json.Serialize(value)) as Dictionary<string,object>;
        if(fields==null)return result;
        int remaining=600;
        foreach(var field in fields){
            if(remaining<=0)break;
            if(!diagnosticFields.Contains(field.Key)||field.Value==null)continue;
            var item=field.Value;
            var list=item as IList;
            if(item is string)item=Redact((string)item,Math.Min(240,remaining));
            else if(list!=null)item=String.Join(", ",list.Cast<object>().Take(10).Select(entry=>Redact(Convert.ToString(entry,CultureInfo.InvariantCulture),35)));
            else if(!(item is ValueType))continue;
            string text=Convert.ToString(item,CultureInfo.InvariantCulture);
            if(text.Length>remaining)item=text.Substring(0,remaining)+"…";
            result[field.Key]=item;
            remaining-=field.Key.Length+Math.Min(remaining,text.Length);
        }
        return result;
    }

    static bool IsProblem(string name,Dictionary<string,object> fields){
        return name.IndexOf("failed",StringComparison.OrdinalIgnoreCase)>=0||
            name.IndexOf("exception",StringComparison.OrdinalIgnoreCase)>=0||fields.ContainsKey("ErrorType")||fields.ContainsKey("FailureType");
    }

    public static void Record(string eventName,string modId,string detail,object data=null){
        lock(gate){
            if(!enabled)return;
            try{
                var fields=SafeData(data);
                var entry=new Dictionary<string,object>{
                    {"Sequence",++sequence},{"Local",DateTimeOffset.Now.ToString("o")},{"Event",Redact(eventName,90)},
                    {"Mod",Redact(modId,100)},{"Detail",Redact(detail,400)},{"Data",fields}
                };
                bool trace=eventName=="scan.file"||eventName=="scan.result"||eventName.StartsWith("install.file.",StringComparison.Ordinal)||
                    eventName=="watch.file.observed"||eventName=="general.install.stage"||eventName=="ui.row.changed";
                var history=trace?traces:timeline;
                if(history.Count==(trace?TraceLimit:TimelineLimit)){history.Dequeue();dropped++;}
                history.Enqueue(entry);
                if(IsProblem(eventName,fields)){
                    if(problems.Count==ProblemLimit)problems.Dequeue();
                    problems.Enqueue(entry);
                }
            }catch(Exception){/* Diagnostics cannot interrupt an operation. */}
        }
    }

    internal static void Failure(string eventName,Exception error,string expected,string modId=null,string name=null,string variant=null,string version=null,string stage=null){
        if(!Enabled)return;
        var frames=new StackTrace(error,false).GetFrames()??new StackFrame[0];
        string stack=String.Join(" > ",frames.Take(6).Select(frame=>frame.GetMethod()).Where(method=>method!=null)
            .Select(method=>method.Name));
        Record(eventName,modId,"A operação falhou.",new{
            Name=name,Variant=variant,ModVersion=version,Stage=stage,Expected=expected,
            Actual=error.Message,ErrorType=error.GetType().Name,Stack=stack
        });
    }

    internal static List<Dictionary<string,object>> Snapshot(){
        lock(gate)return problems.Concat(timeline).Concat(traces).Distinct().OrderBy(entry=>(long)entry["Sequence"]).ToList();
    }

    internal static string Report(bool english){
        lock(gate){
            var text=new StringBuilder(GeneralAppPaths.WindowTitle).AppendLine();
            if(!enabled)return text.AppendLine(english?"No session report is available.":"Nenhum relatório de sessão disponível.").ToString();
            text.Append(english?"Session: ":"Sessão: ").AppendLine(session)
                .Append(english?"Started: ":"Início: ").AppendLine(started.ToString("yyyy-MM-dd HH:mm:ss zzz"))
                .Append(english?"Report generated: ":"Relatório gerado: ").AppendLine(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"))
                .Append(english?"Catalog: ":"Catálogo: ").AppendLine(OnlineIdentity.CatalogHash)
                .Append(english?"Build: ":"Compilação: ").AppendLine(typeof(OnlineDiagnostics).Module.ModuleVersionId.ToString("N"))
                .Append("Windows / .NET: ").Append(Environment.OSVersion.Version).Append(" / ").AppendLine(Environment.Version.ToString());
            if(dropped>0)text.AppendLine(english?"Earlier routine events were trimmed.":"Alguns eventos antigos foram descartados pelo limite do histórico.");
            var entries=Snapshot();
            if(problems.Count>0){
                text.AppendLine().AppendLine(english?"Recent failures":"Falhas recentes");
                foreach(var entry in problems.Reverse())AppendEntry(text,entry);
            }
            text.AppendLine().AppendLine(english?"Session timeline":"Linha do tempo da sessão");
            foreach(var entry in entries)AppendEntry(text,entry);
            return text.ToString();
        }
    }

    static void AppendEntry(StringBuilder text,Dictionary<string,object> entry){
        text.Append('#').Append(entry["Sequence"]).Append(' ').Append(entry["Local"]).Append(' ').Append(entry["Event"]);
        if(!String.IsNullOrEmpty((string)entry["Mod"]))text.Append(" · ").Append(entry["Mod"]);
        text.AppendLine().Append("  ").AppendLine((string)entry["Detail"]);
        foreach(var field in (Dictionary<string,object>)entry["Data"])
            text.Append("  ").Append(field.Key).Append(" = ").AppendLine(Convert.ToString(field.Value,CultureInfo.InvariantCulture));
    }
}
