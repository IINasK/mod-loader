using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Xml;

internal sealed class GeneralModRuntimeMonitor : IDisposable {
    static readonly TimeSpan MinimumSession = TimeSpan.FromSeconds(45);
    static readonly TimeSpan CrashLogDelay = TimeSpan.FromSeconds(5);
    static readonly TimeSpan CrashLogInterval = TimeSpan.FromSeconds(15);
    const int CrashReadLimit = 1024;
    readonly object gate = new object();
    readonly Dictionary<int,Session> running = new Dictionary<int,Session>();
    readonly List<Session> finished = new List<Session>();
    string executable;
    long lastCrashUtc, checkedFromUtc, checkedAtUtc;
    bool crashLogRead, disposed;

    sealed class Session : IDisposable {
        internal Process Process;
        internal long StartedUtc, ExitedUtc;
        internal readonly HashSet<string> LoadedUpdates = new HashSet<string>(StringComparer.Ordinal);
        public void Dispose() {
            if(Process==null)return;
            Process.Dispose();
            Process=null;
        }
    }

    internal void Observe(string gameExecutable, GeneralModUpdateHistory.Entry[] entries) {
        lock(gate) {
            if(disposed)return;
            var path=AbsolutePath(gameExecutable);
            if(!SamePath(path,executable)){
                Reset();
                executable=path;
            }
            long now=DateTime.UtcNow.Ticks;
            var pending=entries.Where(entry=>entry.Status=="pending"||entry.Status=="loaded")
                .Where(entry=>entry.InstalledUtc>0&&entry.InstalledUtc<=now&&InDestination(executable,entry.Destination)).ToArray();
            if(pending.Length==0){Reset();return;}

            ReadSessions(pending);
            long oldest=pending.Min(entry=>entry.InstalledUtc);
            bool waitFinished=finished.Any(session=>now-session.ExitedUtc>=CrashLogDelay.Ticks&&
                checkedAtUtc-session.ExitedUtc<CrashLogDelay.Ticks);
            if(checkedAtUtc==0||oldest<checkedFromUtc||now-checkedAtUtc>=CrashLogInterval.Ticks||waitFinished){
                long mostRecent;
                crashLogRead=ReadCrashLog(executable,oldest,out mostRecent);
                lastCrashUtc=Math.Max(lastCrashUtc,mostRecent);
                checkedFromUtc=oldest;
                checkedAtUtc=now;
            }
            foreach(var entry in pending.Where(entry=>lastCrashUtc>=entry.InstalledUtc))entry.Status="crash";
            finished.RemoveAll(session=>now-session.ExitedUtc>TimeSpan.FromMinutes(2).Ticks);
            if(!crashLogRead)return;
            foreach(var session in finished.Where(session=>checkedAtUtc-session.ExitedUtc>=CrashLogDelay.Ticks).ToArray()){
                foreach(var entry in pending.Where(entry=>entry.Status=="loaded"&&session.LoadedUpdates.Contains(entry.Id))){
                    if(!entry.RequiresManualConfirmation&&ReceiptModulesMatch(entry,null))entry.Status="confirmed";
                }
                finished.Remove(session);
            }
        }
    }

    void ReadSessions(GeneralModUpdateHistory.Entry[] pending) {
        foreach(var item in running.ToArray()){
            var session=item.Value;
            try {
                if(!session.Process.HasExited)continue;
                long exit=session.Process.ExitTime.ToUniversalTime().Ticks;
                if(session.LoadedUpdates.Count>0&&session.Process.ExitCode==0&&exit-session.StartedUtc>=MinimumSession.Ticks){
                    session.ExitedUtc=exit;
                    finished.Add(session);
                }
            }catch(Win32Exception){}catch(InvalidOperationException){}
            running.Remove(item.Key);
            session.Dispose();
        }
        var name=Path.GetFileNameWithoutExtension(executable);
        foreach(var process in Process.GetProcessesByName(name)){
            bool adopted=false;
            try {
                if(running.ContainsKey(process.Id)||!SamePath(AbsolutePath(process.MainModule.FileName),executable))continue;
                long started=process.StartTime.ToUniversalTime().Ticks;
                if(!pending.Any(entry=>entry.InstalledUtc<=started&&entry.Modules!=null&&entry.Modules.Count>0))continue;
                // Retain the Windows process handle so exit status remains available after the process has exited.
                process.EnableRaisingEvents=true;
                running.Add(process.Id,new Session{Process=process,StartedUtc=started});
                adopted=true;
            }catch(Win32Exception){}catch(InvalidOperationException){}
            finally {
                if(!adopted)process.Dispose();
            }
        }
        foreach(var session in running.Values){
            var waiting=pending.Where(entry=>entry.InstalledUtc<=session.StartedUtc&&entry.Modules!=null&&entry.Modules.Count>0&&
                !session.LoadedUpdates.Contains(entry.Id)).ToArray();
            if(waiting.Length==0)continue;
            try {
                session.Process.Refresh();
                var modules=new HashSet<string>(session.Process.Modules.Cast<ProcessModule>()
                    .Select(module=>AbsolutePath(module.FileName)).Where(path=>path!=null),StringComparer.OrdinalIgnoreCase);
                foreach(var entry in waiting.Where(entry=>ReceiptModulesMatch(entry,modules))){
                    session.LoadedUpdates.Add(entry.Id);
                    if(entry.Status=="pending")entry.Status="loaded";
                }
            }catch(Win32Exception){}catch(InvalidOperationException){}
        }
    }

    static bool ReceiptModulesMatch(GeneralModUpdateHistory.Entry entry, HashSet<string> loaded) {
        if(entry.Modules==null||entry.Modules.Count==0)return false;
        try {
            foreach(var file in entry.Modules){
                string extension=Path.GetExtension(file.Key);
                if(!String.Equals(extension,".dll",StringComparison.OrdinalIgnoreCase)&&
                    !String.Equals(extension,".asi",StringComparison.OrdinalIgnoreCase))return false;
                string path=Safe.In(entry.Destination,file.Key);
                if(loaded!=null&&!loaded.Contains(path))return false;
                if(!File.Exists(path)||!String.Equals(Safe.Hash(path),file.Value,StringComparison.OrdinalIgnoreCase))return false;
            }
            return true;
        }catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}catch(SecurityException){}
        return false;
    }

    static bool ReadCrashLog(string image,long since,out long latest) {
        latest=0;
        try {
            string utc=new DateTime(since,DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ss.fffZ",CultureInfo.InvariantCulture);
            string filter="*[System[((Provider[@Name='Application Error'] and EventID=1000) or "+
                "(Provider[@Name='Application Hang'] and EventID=1002)) and TimeCreated[@SystemTime >= '"+utc+"']]]";
            var query=new EventLogQuery("Application",PathType.LogName,filter){ReverseDirection=true};
            var clock=Stopwatch.StartNew();
            using(var reader=new EventLogReader(query)){
                for(int count=0;count<CrashReadLimit&&clock.ElapsedMilliseconds<2500;count++){
                    using(var record=reader.ReadEvent(TimeSpan.FromMilliseconds(500))){
                        if(record==null)return true;
                        var time=record.TimeCreated;
                        if(!time.HasValue)continue;
                        long at=time.Value.ToUniversalTime().Ticks;
                        if(at>=since&&at>latest&&SamePath(CrashImage(record.ToXml()),image))latest=at;
                    }
                }
            }
        }catch(EventLogException){}catch(UnauthorizedAccessException){}catch(SecurityException){}
         catch(InvalidOperationException){}catch(Win32Exception){}
        return false;
    }

    internal static string CrashImage(string xml) {
        if(String.IsNullOrWhiteSpace(xml)||xml.Length>65536)return null;
        try {
            var document=new XmlDocument{XmlResolver=null};
            using(var text=new StringReader(xml))
            using(var reader=XmlReader.Create(text,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,
                MaxCharactersInDocument=65536}))document.Load(reader);
            var names=new XmlNamespaceManager(document.NameTable);
            names.AddNamespace("event","http://schemas.microsoft.com/win/2004/08/events/event");
            var image=document.SelectSingleNode("/event:Event/event:EventData/event:Data[@Name='AppPath' or @Name='ExeFileName' or @Name='ApplicationPath']",names);
            return image==null?null:AbsolutePath(image.InnerText);
        }catch(XmlException){return null;}
    }

    static string AbsolutePath(string path) {
        if(String.IsNullOrWhiteSpace(path)||path.Length<3||path[1]!=':'||(path[2]!='\\'&&path[2]!='/'))return null;
        try{return Path.GetFullPath(path);}
        catch(IOException){return null;}
        catch(ArgumentException){return null;}
        catch(NotSupportedException){return null;}
    }

    static bool SamePath(string first,string second) {return String.Equals(first,second,StringComparison.OrdinalIgnoreCase);}

    static bool InDestination(string image,string destination) {
        string root=AbsolutePath(destination);
        return image!=null&&root!=null&&image.StartsWith(root.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase);
    }

    void Reset() {
        foreach(var session in running.Values)session.Dispose();
        running.Clear();
        finished.Clear();
        checkedAtUtc=checkedFromUtc=lastCrashUtc=0;
        crashLogRead=false;
    }

    public void Dispose() {
        lock(gate) {
            if(disposed)return;
            disposed=true;
            Reset();
        }
    }
}
