using System;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

internal static class GeneralVelopackUpdate {
    static UpdateManager Manager(){
        string repository="https://github.com/"+GeneralUpdateChecker.Repository;
#if MODLOADER_DEV
        return new UpdateManager(new SimpleWebSource(repository+"/releases/download/dev-preview/",timeout:5d/60));
#else
        return new UpdateManager(new GithubSource(repository,null,false));
#endif
    }
#if MODLOADER_DEV
    internal static string InstalledVersion(){
        try{var version=Manager().CurrentVersion;return version==null?null:version.ToString();}
        catch(Exception){return null;}
    }
#endif
    internal static void CheckAtStartup(){
        try{
            var updates=Manager();
            if(!updates.IsInstalled)return;
            var check=updates.CheckForUpdatesAsync();
            if(Task.WhenAny(check,Task.Delay(TimeSpan.FromSeconds(5))).GetAwaiter().GetResult()!=check){
                OnlineDiagnostics.Record("update.velopack.deferred",null,"Consulta de atualizações expirou.");
                return;
            }
            var available=check.GetAwaiter().GetResult();
            if(available==null||StorageMaintenance.AnotherLoaderRunning())return;
            using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(45)))
                updates.DownloadUpdatesAsync(available,cancelToken:timeout.Token).GetAwaiter().GetResult();
            if(StorageMaintenance.AnotherLoaderRunning())return;
            OnlineDiagnostics.Record("update.velopack.install",null,"Atualização conferida. Reiniciando o aplicativo.",new{Version=available.TargetFullRelease.Version.ToString()});
            OnlineDiagnostics.Stop();
            updates.ApplyUpdatesAndRestart(available.TargetFullRelease);
        }catch(Exception ex){
            OnlineDiagnostics.Record("update.velopack.failed",null,"Atualização adiada.",new{ErrorType=ex.GetType().Name});
        }
    }
}
