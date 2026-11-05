using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    sealed class Work {
        internal GeneralModEntry Entry;
        internal GeneralModVariant Variant;
        internal bool Ended,Applied;
        internal double Fraction;
        internal string Key { get{return GeneralModWindow.Key(Entry,Variant);} }
        internal bool Local { get{return Variant.CyberpunkId==null&&Variant.Sources.All(GeneralModDownload.IsArchivePath);} }
        internal string Receipt(int index){return Key+"/"+index+"/"+Variant.Sources[index];}
    }
    readonly List<Work> normal=new List<Work>(),priority=new List<Work>();
    Work current, priorityCurrent;
    bool waiting,NeedUnknownFile,editingAttachment;
    readonly HashSet<Work> skipRequests=new HashSet<Work>();
    Work scanCurrent;
    int operationTotal,operationFinished;
    Action<string,string> testStage;
    string association;
    Action<string> launch=GeneralModDownload.OpenSource;
    GeneralModInstaller Generic(string root){return new GeneralModInstaller(root,Path.Combine(data,"Instalacoes"));}
    InstalledSnapshotInstaller Snapshot(string root){return new InstalledSnapshotInstaller(root,data,catalog,testMode);}
    FixedInstaller Cyberpunk(string root){return new FixedInstaller(catalog,cache,root,testMode?Path.Combine(data,"fixture-saves"):Discovery.Saves(),testMode);}
    static bool IsCyberpunk(string root){return !String.IsNullOrEmpty(root)&&File.Exists(Path.Combine(root,"bin","x64","Cyberpunk2077.exe"));}
    FixedMod Recipe(Work work){return catalog.Packages.Single(m=>m.Id==work.Variant.CyberpunkId);}
    Work MakeWork(GeneralModEntry item){return new Work{Entry=item,Variant=Selected(item)};}
    string LocalSource(Work work,int index){
        string source=work.Variant.Sources[index],resolved;
        return GeneralModDownload.IsArchivePath(source)?source:
            state.ResolvedDownloads.TryGetValue(work.Receipt(index),out resolved)?resolved:null;
    }
    static ModImportItem ImportItem(string source){
        string hash=Safe.Hash(source);
        return new ModImportItem{Id="file-"+hash,Kind="Arquivo",Name=Path.GetFileName(source),Source=source,Sha256=hash,Size=new FileInfo(source).Length};
    }
    string CyberpunkImportId(Work work,int index){
        string id;
        if(state.CyberpunkImports.TryGetValue(work.Receipt(index),out id))return id;
        if(work.Entry.Id.StartsWith("file-",StringComparison.Ordinal)&&work.Entry.Id.Length==69)return work.Entry.Id;
        string path=LocalSource(work,index);
        return path!=null&&File.Exists(path)?ImportItem(path).Id:null;
    }
    List<Work> Marked(){return ModRows().Where(r=>marked.Contains((string)r.Tag)).Select(r=>MakeWork(state.List.Mods.Single(m=>m.Id==(string)r.Tag))).ToList();}
    void Stage(Work work,string phase){if(testMode&&testStage!=null)testStage(work.Key,phase);}
    void RequestSkip(){var work=scanning?scanCurrent:priorityCurrent??current;if(work!=null)skipRequests.Add(work);}
    void CheckSkip(Work work){if(skipRequests.Contains(work))throw new OperationCanceledException();}
    void UpdateFlow(Work work,string status,int? percent=null){
        if(work!=null){
            work.Fraction=percent.HasValue?Math.Max(0,Math.Min(100,percent.Value))/100.0:work.Fraction;
            currentProgress.Value=percent.GetValueOrDefault();currentProgress.Indeterminate=!percent.HasValue;
            currentProgress.Caption=work.Entry.Name+"  ·  "+GeneralStyle.Status(status);currentProgress.Invalidate();
        }
        var active=priorityCurrent??current??scanCurrent;
        double fraction=active==null||active.Ended?0:active.Fraction;
        totalProgress.Value=operationTotal==0?0:(int)Math.Min(100,100*(operationFinished+fraction)/operationTotal);
        totalProgress.Caption=T("Itens processados: ","Items processed: ")+operationFinished+" / "+operationTotal+"  ·  "+T("restantes: ","remaining: ")+Math.Max(0,operationTotal-operationFinished);
        totalProgress.Indeterminate=false;totalProgress.Invalidate();
    }
    string Root(){
        if(p2p!=null&&p2p.Installing)throw new IOException(T("Aguarde a instalação do pacote recebido terminar.","Wait for the received pack installation to finish."));
        if(String.IsNullOrWhiteSpace(state.Destination)||!Directory.Exists(state.Destination))throw new IOException(T("Escolha a pasta de instalação primeiro.","Choose the installation folder first."));
        string root=Path.GetFullPath(state.Destination);Safe.NoLinks(root);
        return root;
    }
    void RowStatus(Work work,string status,int? pct=null,string message=null){
        string previous;statuses.TryGetValue(work.Key,out previous);
        statuses[work.Key]=status;
        if(statusFilters!=null&&GeneralStatusPresentation.Label(previous)!=GeneralStatusPresentation.Label(status))DrawRows();
        if(pct.HasValue)progress[work.Key]=pct.Value;else progress.Remove(work.Key);
        var visible=grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r=>Equals(r.Tag,work.Entry.Id));
        var listed=state.List.Mods.FirstOrDefault(m=>m.Id==work.Entry.Id);
        if(visible!=null&&listed!=null&&Object.ReferenceEquals(Selected(listed),work.Variant)){
            visible.Cells["Status"].Value=GeneralStatusPresentation.Label(status);visible.Cells["Status"].ToolTipText=message??GeneralStyle.Status(status);
            visible.Cells["Progress"].Value=pct.HasValue?(object)pct.Value:"—";UpdateCounts();
        }
        if(message!=null)detail.Text=message;
        if(working||scanning)UpdateFlow(work,status,pct);
    }
    string Status(Work work,string root){
        string snapshot;
        if(root!=null&&Directory.Exists(root)&&state.SnapshotReceipts.TryGetValue(work.Key,out snapshot)){
            string found=Snapshot(root).Status(snapshot,work.Key);
            if(found!=GeneralModInstaller.Missing||work.Variant.CyberpunkId==null)return found;
        }
        if(work.Variant.CyberpunkId!=null){
            if(!IsCyberpunk(root))return "Requer Cyberpunk 2077";
            var recipe=Recipe(work);
            if(GameEdition.NeedsPL(recipe)&&!GameEdition.HasPL(root))return "Requer Phantom Liberty";
            if(work.Variant.CyberpunkVariant!=null){
                var density=NightCityDensity.Read(recipe,root);
                if(density.Id==work.Variant.CyberpunkVariant)return "Instalado";
                if(!density.CanApply||density.Partial)return density.Name??"Instalação parcial";
                if(NightCityDensity.Supported(density.Id))return "Instalado: "+density.Name;
                return "Não instalado";
            }
            var scan=FixedData.Scan(recipe,root);
            return scan.IsCorrect?"Instalado":scan.State;
        }
        if(!String.IsNullOrEmpty(root)&&Directory.Exists(root)){
            List<string> results;
            if(IsCyberpunk(root)){
                var engine=Cyberpunk(root);
                var installed=engine.InstalledImports();
                results=Enumerable.Range(0,work.Variant.Sources.Count).Select(i=>{
                    return installed.Contains(CyberpunkImportId(work,i))?GeneralModInstaller.Installed:GeneralModInstaller.Missing;
                }).ToList();
            }else{
                var engine=Generic(root);
                results=Enumerable.Range(0,work.Variant.Sources.Count).Select(i=>engine.Status(work.Receipt(i))).ToList();
            }
            if(results.All(r=>r==GeneralModInstaller.Installed))return "Instalado";
            if(results.Contains(GeneralModInstaller.Modified))return GeneralModInstaller.Modified;
            if(results.Any(r=>r!=GeneralModInstaller.Missing))return GeneralModInstaller.Partial;
        }
        if(work.Variant.Sources.Any(s=>GeneralModDownload.IsArchivePath(s)&&!File.Exists(s)))return "Arquivo não encontrado";
        return work.Local?"Pronto para instalar":"Não iniciado";
    }
    async Task Scan(){
        if(working||scanning)return;
        scanning=true;Busy(true);
        string root=state.Destination;
        var works=state.List.Mods.Select(MakeWork).ToList();
        operationTotal=works.Count;operationFinished=0;skipRequests.Clear();
        string previous=detail.Text;detail.Text=T("Conferindo a pasta de instalação…","Checking the installation folder…");
        try{
            foreach(var work in works){
                scanCurrent=work;UpdateFlow(work,"Verificando",0);Stage(work,"scan.begin");
                string found=null;
                if(!skipRequests.Contains(work))found=await Task.Run(()=>{try{return Status(work,root);}catch(Exception){return "Não verificável";}});
                bool skipped=skipRequests.Contains(work);statuses[work.Key]=skipped?"Verificação pulada":found??"Não verificável";progress.Remove(work.Key);
                var visible=ModRows().FirstOrDefault(row=>Equals(row.Tag,work.Entry.Id));
                if(visible!=null){visible.Cells["Status"].Value=GeneralStatusPresentation.Label(statuses[work.Key]);visible.Cells["Status"].ToolTipText=GeneralStyle.Status(statuses[work.Key]);grid.InvalidateRow(visible.Index);UpdateCounts();}
                operationFinished++;work.Ended=true;UpdateFlow(work,statuses[work.Key],skipped?(int?)null:found=="Instalado"?100:0);Stage(work,"scan.end");
            }
            DrawRows();
        }finally{scanCurrent=null;detail.Text=previous;scanning=false;Busy(false);}
    }
    void StartSelected(){
        if(working||scanning)return;
        var jobs=Marked();
        if(jobs.Count>1&&IsCyberpunk(root:state.Destination)&&!GameEdition.HasPL(state.Destination)){
            jobs=jobs.Where(w=>w.Variant.CyberpunkId==null||!FixedData.Expand(catalog,new[]{w.Variant.CyberpunkId}).Any(GameEdition.NeedsPL)).ToList();
            if(jobs.Count==0){detail.Text=T("Os mods marcados precisam de Phantom Liberty.","The selected mods require Phantom Liberty.");return;}
        }
        if(jobs.Count==0){var item=FocusedEntry();if(item!=null)jobs.Add(MakeWork(item));}
        if(jobs.Count==0){detail.Text=T("Selecione um mod na lista.","Select a mod from the list.");return;}
        string root=Root();
        string description=String.Join("\n",jobs.Take(9).Select(w=>"• "+w.Entry.Name+" — "+w.Variant.Name));
        if(jobs.Count>9)description+="\n… e mais "+(jobs.Count-9)+".";
        if(GeneralDialog.Message(this,T("Baixar/Instalar ","Download/Install ")+jobs.Count+T(" mod(s) em:\n"," mod(s) in:\n")+root+"\n\n"+description,T("Confirmar instalação","Confirm installation"),MessageBoxButtons.OKCancel,MessageBoxIcon.Question)!=DialogResult.OK)return;
        Enqueue(jobs,false);
    }
    async void Enqueue(IEnumerable<Work> jobs,bool urgent){
        if(scanning)return;
        string root;try{root=Root();}catch(Exception ex){detail.Text=ex.Message;return;}
        if(!working){operationTotal=0;operationFinished=0;skipRequests.Clear();}
        foreach(var work in jobs){
            if((current!=null&&current.Entry.Id==work.Entry.Id)||(priorityCurrent!=null&&priorityCurrent.Entry.Id==work.Entry.Id)||priority.Any(w=>w.Entry.Id==work.Entry.Id))continue;
            var existing=normal.FirstOrDefault(w=>w.Entry.Id==work.Entry.Id);
            if(existing!=null){if(!urgent)continue;normal.Remove(existing);}else operationTotal++;
            (urgent?priority:normal).Add(work);RowStatus(work,"Na fila");
        }
        if(working)return;
        working=true;Busy(true);
        try{
            while(priority.Count+normal.Count>0){
                var list=priority.Count>0?priority:normal;
                current=list[0];list.RemoveAt(0);
                Busy(true);
                await RunWork(current,root,false);
                current=null;
            }
        }finally{working=false;Busy(false);SetWait(false,false);}
    }
    async Task RunWork(Work work,string root,bool interrupt){
        try{
            skipRequests.Remove(work);
            RowStatus(work,"Verificando",null,work.Entry.Name+T(": verificando arquivos…",": checking files…"));
            OnlineDiagnostics.Record("general.install.begin",work.Entry.Id,"Iniciando item da fila.",new{Variant=work.Variant.Id});
            string snapshot;
            if(state.SnapshotReceipts.TryGetValue(work.Key,out snapshot)&&Snapshot(root).Status(snapshot,work.Key)==GeneralModInstaller.Installed){RowStatus(work,"Instalado",100);return;}
            if(work.Variant.Sources.Any(GeneralModDownload.IsInstalledSource))throw new IOException(T("Este mod veio de um pacote instalado. Para reparar, receba um novo pacote do remetente.","This mod came from an installed pack. Receive a new pack from the sender to repair it."));
            if(work.Variant.CyberpunkId!=null)await InstallKnown(work,root);
            else await InstallGeneral(work,root);
            var final=await Task.Run(()=>Status(work,root));
            if(final!="Instalado")throw new IOException(T("A conferência final não confirmou esta variante: ","Final verification did not confirm this variant: ")+GeneralStyle.Status(final));
            RowStatus(work,"Instalado",100,work.Entry.Name+T(": instalado e conferido.",": installed and verified."));
            OnlineDiagnostics.Record("general.install.complete",work.Entry.Id,"Instalação conferida.",new{Variant=work.Variant.Id});
        }catch(OperationCanceledException){RowStatus(work,work.Applied?"Parcial/pulado":"Pulado",null,work.Entry.Name+T(": pulado. A fila continua.",": skipped. The queue continues."));}
        catch(Exception ex){
            string message=work.Entry.Name+": "+ex.Message;
            RowStatus(work,"Falhou",null,message);
            if(!interrupt&&!testMode)GeneralDialog.Message(this,message,T("Falha na instalação","Installation failed"),MessageBoxButtons.OK,MessageBoxIcon.Warning);
        }
        finally{work.Ended=true;operationFinished++;UpdateFlow(null,"");skipRequests.Remove(work);Stage(work,"work.end");}
    }
    async Task InstallKnown(Work work,string root){
        if(!IsCyberpunk(root))throw new IOException(T("Selecione a pasta do Cyberpunk 2077 para usar este mod do pacote.","Select the Cyberpunk 2077 folder to use this pack item."));
        EnsureGameClosed(root);
        var recipe=Recipe(work);
        var variants=FixedData.Expand(catalog,new[]{recipe.Id});
        if(work.Variant.CyberpunkVariant!=null)variants[variants.Count-1]=NightCityDensity.Resolve(recipe,work.Variant.CyberpunkVariant);
        FixedData.RequireInstallable(variants);FixedData.ValidateGame(root,variants,catalog.GameFileVersion);
        var pending=await Task.Run(()=>variants.Where(mod=>{
            if(mod.Id==recipe.Id&&work.Variant.CyberpunkVariant!=null){
                var density=NightCityDensity.Read(recipe,root);
                if(!density.CanApply)throw new IOException(density.Detail);
                return density.Id!=work.Variant.CyberpunkVariant;
            }
            var scan=FixedData.Scan(mod,root);
            if(scan.IsConflict)throw new IOException(mod.Name+": "+scan.State+". Os arquivos existentes foram preservados.");
            return !scan.IsCorrect;
        }).ToList());
        foreach(var mod in pending)foreach(var asset in mod.Assets){
            CheckSkip(work);
            if(await Task.Run(()=>cache.Contains(asset)))continue;
            if(asset.ModId==0){
                if(testMode)throw new IOException("Download de rede bloqueado no teste.");
                RowStatus(work,"Baixando",null,"Baixando "+asset.Name+"…");
                using(var control=new DownloadControl()){
                    var transfer=Task.Run(()=>new NexusClient().Download(mod,asset,cache,control,p=>{
                        if(!IsDisposed&&IsHandleCreated)BeginInvoke((Action)(()=>RowStatus(work,"Baixando",Math.Min(99,p.Percent),asset.Name)));
                    }));
                    while(!transfer.IsCompleted){if(skipRequests.Contains(work))control.Cancel();await Task.WhenAny(transfer,Task.Delay(150));}
                    await transfer;
                }
            }else await AwaitKnown(work,asset,root);
        }
        CheckSkip(work);
        RowStatus(work,"Instalando",null,work.Entry.Name+T(": preparando e instalando…",": preparing and installing…"));
        Action install=()=>{
            var engine=Cyberpunk(root);
            if(work.Variant.CyberpunkVariant==null){engine.Install(new[]{recipe.Id},true,Report);return;}
            if(recipe.Dependencies.Length>0)engine.Install(recipe.Dependencies,true,Report);
            engine.ApplyDensityProfile(work.Variant.CyberpunkVariant,Report);
        };
        var approvals=new HashSet<string>();
        while(true){
            try{Stage(work,"install.begin");await Task.Run(install);work.Applied=true;Stage(work,"install.end");break;}
            catch(ModPreparationException ex){
                var request=ex.InnerException as FileApprovalRequest;
                if(request==null||testMode||!approvals.Add(request.ModId+"/"+request.Entry))throw;
                string path=Safe.In(root,request.Path);Safe.NoLinks(path);
                if(File.Exists(path)||Directory.Exists(path)||GeneralDialog.Message(this,ex.Message+"\n\nO arquivo difere do catálogo e pode causar falhas. Instalar mesmo assim?","Arquivo diferente",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)throw;
                UserFileApprovals.Approve(catalog,root,request);
            }
        }
    }
    async Task AwaitKnown(Work work,FixedAsset asset,string root){
        using(var watch=new GeneralModDownload(state.DownloadFolder,asset)){
            bool opened=false;
            association=null;SetWait(true,true);
            try{
                while(true){
                    CheckSkip(work);
                    while(editingAttachment)await Task.Delay(100);
                    await DrainLocalPriority(root);
                    if(association!=null){
                        string selected=association;association=null;
                        try{await Task.Run(()=>cache.Import(selected,asset));return;}
                        catch(IOException ex){if(!testMode)GeneralDialog.Message(this,ex.Message,T("Arquivo diferente","Different file"),MessageBoxButtons.OK,MessageBoxIcon.Warning);}
                    }
                    var file=await Task.Run(()=>watch.Poll());
                    while(editingAttachment)await Task.Delay(100);
                    CheckSkip(work);
                    if(file!=null){await Task.Run(()=>cache.Import(file,asset));return;}
                    var snapshot=await Task.Run(()=>watch.Progress());
                    if(!opened&&!watch.HasPendingCandidates&&!snapshot.Active){
                        CheckSkip(work);
                        launch("https://www.nexusmods.com/cyberpunk2077/mods/"+asset.ModId+"?tab=files&file_id="+asset.FileId);opened=true;
                    }
                    RowStatus(work,snapshot.Active?"Baixando":watch.HasPendingCandidates?"Conferindo download":"Aguardando download",
                        snapshot.Percent.HasValue?(int?)Math.Min(snapshot.Percent.Value,99):null,
                        asset.Name+T(" — clique em Manual / Slow Download. O app confere a pasta e continua sozinho."," — click Manual / Slow Download. The app checks the folder and continues automatically."));
                    if(snapshot.Estimated&&snapshot.Active){currentProgress.Caption+=T(" · estimado"," · estimated");currentProgress.Invalidate();}
                    await Task.Delay(600);
                }
            }finally{SetWait(false,false);association=null;}
        }
    }
    async Task InstallGeneral(Work work,string root){
        EnsureGameClosed(root);
        bool cyberpunk=IsCyberpunk(root);
        var engine=cyberpunk?null:Generic(root);
        if(cyberpunk)FixedData.ValidateGame(root,new FixedMod[0],catalog.GameFileVersion);
        for(int i=0;i<work.Variant.Sources.Count;i++){
            CheckSkip(work);
            string source=work.Variant.Sources[i],receipt=work.Receipt(i);
            if(engine!=null&&await Task.Run(()=>engine.Status(receipt))==GeneralModInstaller.Installed)continue;
            if(cyberpunk){
                int index=i;
                if(await Task.Run(()=>Cyberpunk(root).InstalledImports().Contains(CyberpunkImportId(work,index))))continue;
            }
            bool web=GeneralModDownload.IsWebSource(source);
            string actual=LocalSource(work,i);
            if(web&&(actual==null||!File.Exists(actual)))actual=await AwaitAttachment(work,source,root);
            source=actual;
            if(!File.Exists(source))throw new IOException(T("Arquivo não encontrado: ","File not found: ")+source+T(". Dê dois cliques no mod para corrigir a fonte.",". Double-click the mod to update its source."));
            CheckSkip(work);
            RowStatus(work,"Instalando",(int)(i*99.0/work.Variant.Sources.Count),work.Entry.Name+": "+Path.GetFileName(source));
            string installSource=source;
            string localId=null;
            Stage(work,"install.begin");
            await Task.Run(()=>{
                EnsureGameClosed(root);
                if(cyberpunk){var item=ImportItem(installSource);Cyberpunk(root).InstallImported(item,Report);localId=item.Id;}
                else engine.Install(installSource,receipt,Report);
            });
            work.Applied=true;Stage(work,"install.end");
            if(web)state.ResolvedDownloads[receipt]=source;
            if(localId!=null)state.CyberpunkImports[receipt]=localId;
            if(web||localId!=null)Persist();
        }
    }
    async Task<string> AwaitAttachment(Work work,string source,string root){
        association=null;SetWait(true,true);
        try{
            RowStatus(work,"Aguardando arquivo",null,work.Entry.Name+T(": conclua o download e arraste o arquivo para esta janela.",": finish downloading and drop the file into this window."));
            launch(source);
            while(association==null){CheckSkip(work);if(!editingAttachment)await DrainLocalPriority(root);if(association==null)await Task.Delay(400);}
            CheckSkip(work);
            return association;
        }finally{SetWait(false,false);association=null;}
    }
    async Task DrainLocalPriority(string root){
        if(current!=null)CheckSkip(current);
        while(priority.Count>0&&priority[0].Local){
            priorityCurrent=priority[0];priority.RemoveAt(0);
            bool couldAttach=NeedUnknownFile;NeedUnknownFile=false;
            try{await RunWork(priorityCurrent,root,true);}
            finally{priorityCurrent=null;NeedUnknownFile=couldAttach;if(current!=null)UpdateFlow(current,statuses.ContainsKey(current.Key)?statuses[current.Key]:"Aguardando download");}
        }
    }
    void SetWait(bool value,bool needsFile){waiting=value;NeedUnknownFile=value&&needsFile;next.Visible=working||scanning;next.Enabled=working||scanning;}
    void Report(string message){
        if(!IsDisposed&&IsHandleCreated)BeginInvoke((Action)(()=>{
            if(state.Language=="en"){
                foreach(var prefix in new[]{new[]{"Preparando ","Preparing "},new[]{"Conferindo ","Checking "},new[]{"Instalado: ","Installed: "},new[]{"Retirado: ","Removed: "},new[]{"Importando ","Importing "}})
                    if(message.StartsWith(prefix[0],StringComparison.Ordinal)){message=prefix[1]+message.Substring(prefix[0].Length);break;}
            }
            detail.Text=message;
        }));
    }
    void EnsureGameClosed(string root){
        if(testMode||!IsCyberpunk(root))return;
        var processes=Process.GetProcessesByName("Cyberpunk2077");
        foreach(var process in processes)process.Dispose();
        if(processes.Length!=0)throw new IOException("Feche o Cyberpunk 2077 antes de instalar ou remover mods.");
    }
    async Task Uninstall(){
        if(working||scanning)return;
        try{
            var jobs=Marked();
            if(jobs.Count==0){var item=FocusedEntry();if(item!=null)jobs.Add(MakeWork(item));}
            if(jobs.Count==0)return;
            string root=Root();
            if(GeneralDialog.Message(this,"Desinstalar "+jobs.Count+" variante(s) de:\n"+root+"?","Confirmar remoção",MessageBoxButtons.OKCancel,MessageBoxIcon.Warning)!=DialogResult.OK)return;
            working=true;Busy(true);
            try{
                string result=await Task.Run(()=>{
                    EnsureGameClosed(root);
                    var ids=jobs.Where(w=>w.Variant.CyberpunkId!=null).Select(w=>w.Variant.CyberpunkId).ToList();
                    var notes=new List<string>();
                    if(ids.Count>0){if(!IsCyberpunk(root))throw new IOException("Selecione a pasta do Cyberpunk 2077.");notes.Add(Cyberpunk(root).DeactivateMany(ids,Report));}
                    bool cyberpunk=IsCyberpunk(root);var engine=cyberpunk?null:Generic(root);
                    foreach(var work in jobs.Where(w=>w.Variant.CyberpunkId==null))
                        if(state.SnapshotReceipts.ContainsKey(work.Key))notes.Add(Snapshot(root).Remove(state.SnapshotReceipts[work.Key],new[]{work.Key},Report));
                        else
                        for(int i=work.Variant.Sources.Count-1;i>=0;i--){
                            if(!cyberpunk){notes.Add(engine.Remove(work.Receipt(i)));continue;}
                            string id=CyberpunkImportId(work,i);
                            if(id!=null)notes.Add(Cyberpunk(root).RemoveImported(id,Report));
                        }
                    return String.Join(" ",notes.Distinct());
                });
                detail.Text=result;
            }finally{working=false;Busy(false);}
            await Scan();
        }catch(Exception ex){Guard(()=>{throw new IOException(ex.Message,ex);});}
    }
    async Task CyberpunkMaintenance(bool clean){
        if(working||scanning)return;
        string root=state.Destination;
        if(!IsCyberpunk(root))return;
        working=true;Busy(true);
        bool changed=false;
        try{
            var engine=Cyberpunk(root);
            if(clean){
                var plan=await Task.Run(()=>engine.PlanExtraReset());
                if(plan.Count==0){detail.Text="Nenhum arquivo fora do pacote Cyberpunk foi encontrado.";return;}
                using(var review=new GeneralDialog(T("Revisar arquivos fora do pacote Cyberpunk","Review files outside the Cyberpunk pack")){ClientSize=new Size(820,475)}){
                    var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20,8,20,20),ColumnCount=1,RowCount=2};
                    layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    var field=new GeneralSurface{Dock=DockStyle.Fill,Padding=new Padding(12),Margin=Padding.Empty};
                    var paths=new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,Dock=DockStyle.Fill,Text=String.Join("\r\n",plan.Select(p=>p.Path))};
                    field.Controls.Add(paths);layout.Controls.Add(field,0,0);
                    var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(0,14,0,0),WrapContents=false};
                    var go=GeneralStyle.Button(T("Revisado, continuar","Reviewed, continue"),()=>review.Complete(DialogResult.OK),true);
                    var cancel=GeneralStyle.Button(T("Cancelar","Cancel"),()=>review.Complete(DialogResult.Cancel));
                    buttons.Controls.Add(go);buttons.Controls.Add(cancel);layout.Controls.Add(buttons,0,1);review.Content.Controls.Add(layout);
                    review.AcceptButton=go;review.CancelButton=cancel;GeneralStyle.Apply(review,GeneralStyle.Dark);paths.BorderStyle=BorderStyle.None;
                    if(review.ShowDialog(this)!=DialogResult.OK)return;
                }
                if(GeneralDialog.Message(this,"Retirar os "+plan.Count+" arquivos listados? Eles serão guardados para desfazer a operação.","Confirmar limpeza",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
                detail.Text=await Task.Run(()=>engine.ApplyExtraReset(plan,Report));
            }else{
                if(GeneralDialog.Message(this,"Desfazer a última operação do Cyberpunk?","Confirmar reversão",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
                detail.Text=await Task.Run(()=>engine.Undo(Report));
            }
            changed=true;
        }catch(Exception ex){ShowError(ex);}
        finally{working=false;Busy(false);}
        if(changed)await Scan();
    }
}

