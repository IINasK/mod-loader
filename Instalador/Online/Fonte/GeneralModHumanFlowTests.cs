using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;

public static class GeneralModHumanFlowTests {
    const string Relative="Data/mod/file.txt";
    static readonly List<string> checks=new List<string>();
    public static void Run(string parent) {
        string root=OnlineTests.NestedRoot(parent,"human-");Directory.CreateDirectory(root);checks.Clear();
        string local=Path.Combine(root,"local");Prepare(local);
        var installer=Installer(local);
        installer.InstallCheckpoint=step=>{if(step=="written")throw new OperationCanceledException();};
        try {installer.Install(Source(local),"fixture",null,d=>true);throw new Exception("Cancellation did not run.");}
        catch(OperationCanceledException) { }
        Check(File.ReadAllText(Target(local))=="original"&&!installer.RecoveryPending,"Cancel after write restores prior content and clears durable transaction.");
        installer=Installer(local);installer.Install(Source(local),"fixture",null,d=>true);
        string moved=Path.Combine(local,"moved.txt");File.Move(Target(local),moved);
        Check(Installer(local).Status("fixture")==GeneralModInstaller.Missing,"Moving the installed file manually is detected after reopening.");
        File.Move(moved,Target(local));var timestamp=File.GetLastWriteTimeUtc(Target(local));
        File.WriteAllText(Target(local),"changed!");File.SetLastWriteTimeUtc(Target(local),timestamp);
        Check(Installer(local).Status("fixture")==GeneralModInstaller.Modified,"A manual edit with identical length and timestamp is detected by content.");
        installer.Remove("fixture");
        Check(File.ReadAllText(Target(local))=="changed!","Uninstall preserves the user modification.");

        string concurrent=Path.Combine(root,"concurrent");Prepare(concurrent);File.Delete(Target(concurrent));
        installer=Installer(concurrent);
        installer.InstallCheckpoint=step=>{if(step=="prepared"){File.WriteAllText(Target(concurrent),"incoming");throw new OperationCanceledException();}};
        try {installer.Install(Source(concurrent),"fixture",null,d=>true);throw new Exception("Concurrent cancellation did not run.");}
        catch(OperationCanceledException) { }
        Check(File.ReadAllText(Target(concurrent))=="incoming"&&!installer.RecoveryPending&&installer.Status("fixture")==GeneralModInstaller.Missing,
            "An identical file created externally before atomic publication is preserved without claiming ownership.");

        foreach(string stage in new[]{"staged","written","committed"}) {
            string fixture=Path.Combine(root,stage);Prepare(fixture);Crash(fixture,stage);
            var reopened=Installer(fixture);
            Check(reopened.RecoveryPending&&reopened.Status("fixture")==GeneralModInstaller.Partial,"Restart detects interrupted transaction at "+stage+".");
            reopened.RecoverInterruptedInstall();
            bool committed=stage=="committed";
            Check(File.ReadAllText(Target(fixture))==(committed?"incoming":"original")&&!reopened.RecoveryPending,
                "Restart "+(committed?"preserves committed receipt":"rolls back incomplete installation")+" at "+stage+".");
            Check(!Directory.EnumerateDirectories(Game(fixture),".mod-loader-*").Any(),"Owned staging is removed on recovery at "+stage+".");
            reopened.Install(Source(fixture),"fixture",null,d=>true);
            Check(reopened.Status("fixture")==GeneralModInstaller.Installed,"Retry after "+stage+" completes installation.");
        }
        string created=Path.Combine(root,"created");Prepare(created);File.Delete(Target(created));Crash(created,"written");
        installer=Installer(created);installer.RecoverInterruptedInstall();
        Check(!File.Exists(Target(created))&&!installer.RecoveryPending,"Restart removes only files created by the interrupted installation.");
        installer.Install(Source(created),"fixture",null,d=>true);
        Check(installer.Status("fixture")==GeneralModInstaller.Installed,"A new installation remains retryable after interrupted creation.");

        string unknown=Path.Combine(root,"unknown");Prepare(unknown);Crash(unknown,"staged");
        string unknownFile=Path.Combine(Directory.GetDirectories(Game(unknown),".mod-loader-*").Single(),"user-file.txt");
        File.WriteAllText(unknownFile,"keep");Installer(unknown).RecoverInterruptedInstall();
        Check(File.ReadAllText(unknownFile)=="keep","Recovery preserves unknown content even inside its staging directory.");

        string manual=Path.Combine(root,"manual");Prepare(manual);Crash(manual,"written");
        string movedAfterCrash=Path.Combine(manual,"user-moved.txt");File.Move(Target(manual),movedAfterCrash);
        installer=Installer(manual);
        try {installer.RecoverInterruptedInstall();throw new Exception("A moved file must block replacement rollback.");}
        catch(IOException) { }
        Check(File.ReadAllText(movedAfterCrash)=="incoming"&&!File.Exists(Target(manual))&&installer.RecoveryPending,
            "A manually moved replacement after interruption is preserved without recreating it silently.");
        File.Move(movedAfterCrash,Target(manual));
        File.WriteAllText(Target(manual),"my-change");
        installer=Installer(manual);
        try {installer.RecoverInterruptedInstall();throw new Exception("Manual changes must block rollback.");}
        catch(IOException) { }
        Check(File.ReadAllText(Target(manual))=="my-change"&&installer.RecoveryPending,"Manual changes after interruption are preserved and keep recovery pending.");
        File.WriteAllText(Target(manual),"incoming");installer.RecoverInterruptedInstall();
        Check(File.ReadAllText(Target(manual))=="original"&&!installer.RecoveryPending,"Recovery remains retryable after restoring the interrupted file.");
        Safe.WriteJson(Path.Combine(root,"result.json"),new{Passed=true,Checks=checks,ProcessRestarts=6,ActualGameTouched=false,InternetTested=false});
    }
    public static void RunCrashChild(string root,string stage) {
        if(!new[]{"staged","written","committed"}.Contains(stage))throw new IOException("Invalid fixture checkpoint.");
        string marker=Path.Combine(root,"fixture.marker");
        if(!File.Exists(marker)||File.ReadAllText(marker)!="generic-interruption-test")throw new IOException("Missing isolated fixture marker.");
        var installer=Installer(root);installer.InstallCheckpoint=step=>{if(step==stage)Environment.Exit(73);};
        installer.Install(Source(root),"fixture",null,d=>true);
        throw new IOException("The interruption checkpoint was not reached.");
    }
    static void Crash(string root,string stage) {
        var start=new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--self-test-generic-crash \""+root+"\" "+stage){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
        start.EnvironmentVariables["TEMP"]=root;start.EnvironmentVariables["TMP"]=root;start.EnvironmentVariables["TMPDIR"]=root;
        using(var child=Process.Start(start)) {
            if(!child.WaitForExit(30000)){child.Kill();throw new IOException("Isolated crash fixture timed out.");}
            if(child.ExitCode!=73)throw new IOException("Isolated child did not reach checkpoint "+stage+". Exit "+child.ExitCode+".");
        }
    }
    static void Prepare(string root) {
        Directory.CreateDirectory(Path.GetDirectoryName(Target(root)));File.WriteAllText(Target(root),"original");
        File.WriteAllText(Path.Combine(root,"fixture.marker"),"generic-interruption-test");
        using(var zip=ZipFile.Open(Source(root),ZipArchiveMode.Create))using(var writer=new StreamWriter(zip.CreateEntry(Relative).Open()))writer.Write("incoming");
    }
    static GeneralModInstaller Installer(string root){return new GeneralModInstaller(Game(root),Path.Combine(root,"state"));}
    static string Game(string root){return Path.Combine(root,"game");}
    static string Target(string root){return Path.Combine(Game(root),Relative.Replace('/',Path.DirectorySeparatorChar));}
    static string Source(string root){return Path.Combine(root,"source.zip");}
    static void Check(bool valid,string message){if(!valid)throw new IOException(message);checks.Add(message);}
}
