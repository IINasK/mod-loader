using System;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

internal static class GeneralVelopackUpdate {
    internal static void CheckAtStartup(){
        try{
            var updates=new UpdateManager(new GithubSource("https://github.com/"+GeneralUpdateChecker.Repository,null,false));
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
