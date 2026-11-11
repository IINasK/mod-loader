using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    bool loadingPreset;
    System.Threading.CancellationTokenSource presetCancellation;

    void CancelPresetLoad() {
        if(presetCancellation==null||presetCancellation.IsCancellationRequested)return;
        presetCancellation.Cancel();
        UpdateOperationButton();
        ShowPresetProgress(T("Cancelando troca","Cancelling switch"),0,0);
    }
    string PresetFingerprint {
        get {
            if(ActivePreset==null)return null;
            return InstalledSnapshot.Hash(Encoding.UTF8.GetBytes(Path.GetFullPath(state.Destination??".").ToUpperInvariant()+"\n"+Safe.Json.Serialize(PresetWorks().OrderBy(w=>w.Entry.Id).Select(w=>{
                return new {w.Entry.Id,Recipe=PresetRecipeKey(w),Required=w.Variant.RequiredMods};
            }).ToList())));
        }
    }
    bool PresetRecoveryPending {
        get {if(String.IsNullOrWhiteSpace(state.Destination)||!Directory.Exists(state.Destination))return false;
            string id=InstalledSnapshot.Hash(Encoding.UTF8.GetBytes(Path.GetFullPath(state.Destination).TrimEnd('\\').ToUpperInvariant())).Substring(0,24);
            string path=Safe.In(data,"Presets/"+id+"/pending.json");Safe.NoLinks(path);return File.Exists(path);}
    }
    bool NeedsPresetLoad {get{return ActivePreset!=null&&(PresetRecoveryPending||state.AppliedPreset!=state.SelectedPreset||state.AppliedPresetFingerprint!=PresetFingerprint);}}
    string PresetStoredKey(Work work,string root) {
        var entries=DisabledAt(root).Read();string key=PresetRecipeKey(work);return entries.ContainsKey(key)||ActivePreset!=null?key:work.Key;
    }
    async Task TrackPresetInstall(Work work,string root) {
        if(state.AppliedPreset==null)return;
        await Task.Run(()=>{
            var receipts=PresetReceipts(root).ToArray();
            var package=PresetPackageFor(work,root,receipts,DisabledAt(root).Read());
            new GeneralPresetDeployment(root,data,()=>EnsureGameClosed(root)).RecordInstalled(package);
        });
    }
    IEnumerable<Work> PresetWorks() {
        var preset=ActivePreset;if(preset==null)yield break;
        var mods=state.List.Mods.Where(m=>preset.ModIds.Contains(m.Id));
        foreach(var group in mods.GroupBy(m=>GeneralModLibrary.ChoiceKey(m))) {
            var entries=group.ToList();var selected=entries[0];string chosen;
            if(selected.AlternativeGroup!=null) {
                if(preset.AlternativeChoices.TryGetValue(selected.AlternativeGroup,out chosen))selected=entries.FirstOrDefault(m=>m.Id==chosen)??selected;
                else if(selected.AlternativeGroup==GeneralModLibrary.BodyGroup&&state.BodyChoice!=null)selected=entries.FirstOrDefault(m=>m.Id==(state.BodyChoice==GeneralModLibrary.NaturalBody?GeneralModLibrary.EbbBody:state.BodyChoice))??selected;
                else selected=entries.FirstOrDefault(IsActiveAlternative)??selected;
            }
            yield return MakeWork(selected);
        }
    }
    bool IsPresetVersionSelected(Work choice) {
        return PresetWorks().Any(w=>SameVersion(w,choice));
    }
    static string PresetRecipeKey(Work work) {
        var identity=new {work.Variant.Id,work.Variant.Sources,work.Variant.InstallRoutes,work.Variant.CyberpunkId,work.Variant.CyberpunkVariant,work.BodyChoice};
        return work.Entry.Id+"/r-"+InstalledSnapshot.Hash(Encoding.UTF8.GetBytes(Safe.Json.Serialize(identity))).Substring(0,40);
    }
    IEnumerable<Work> PresetUniverse() {
        GeneralPresetProfiles.Capture(state);
        var seen=new HashSet<string>();
        foreach(var mod in state.List.Mods.Concat(state.List.Presets.SelectMany(p=>p.Configurations.Values)))foreach(var work in VersionWorks(mod))
            if(seen.Add(PresetRecipeKey(work)))yield return work;
    }
    IEnumerable<ProjectDeleteFile> PresetReceipts(string root) {
        var files=Generic(root).ProjectDeletionFiles().Concat(Snapshot(root).ProjectDeletionFiles());
        return IsCyberpunk(root)?files.Concat(Cyberpunk(root).ProjectDeletionFiles(null)):files;
    }
    bool PackageMatches(PresetPackage package,string root) {
        return package.Files.Count>0&&package.Files.All(f=>{
            string path=GeneralInstallRoutes.Resolve(root,f.Path,false);Safe.NoLinks(path);
            return File.Exists(path)&&new FileInfo(path).Length==f.Size&&Safe.Hash(path)==f.Hash;
        });
    }
    PresetPackage PresetPackageFor(Work work,string root,IEnumerable<ProjectDeleteFile> receipts,Dictionary<string,List<DisabledModFile>> disabled) {
        string key=PresetRecipeKey(work),storageKey=key;
        List<DisabledModFile> stored;
        disabled.TryGetValue(storageKey,out stored);
        var package=new PresetPackage{Key=key,StorageKey=storageKey,LegacyKey=work.Key,ModId=work.Entry.Id,Name=work.Entry.Name,Group=work.Entry.AlternativeGroup,RequiredMods=work.Variant.RequiredMods.ToList()};
        if(stored!=null){package.Files=GeneralPresetProfiles.Copy(stored);return package;}
        var sourceKeys=Enumerable.Range(0,work.Variant.Sources.Count).Select(work.Receipt).ToArray();
        var evidence=receipts.Where(f=>f.OwnerKeys.Any(owner=>sourceKeys.Contains(owner)&&f.InstallRoutes!=null&&GeneralInstallRoutes.Same(f.InstallRoutes,work.Variant.InstallRoutes)||
                owner==InstalledSnapshot.Owner(work.Entry,work.Variant)&&f.OwnerRecipes!=null&&f.OwnerRecipes.ContainsKey(owner)&&GeneralModLibrary.SamePayload(f.OwnerRecipes[owner],work.Variant))).ToList();
        if(IsCyberpunk(root)&&work.Variant.CyberpunkId==null) {
            var ids=new HashSet<string>(StringComparer.Ordinal);
            for(int i=0;i<work.Variant.Sources.Count;i++) {
                string id;
                if(!P2PPack.TrySlot(state.CyberpunkImports,work.Entry,work.Variant,i,out id))continue;
                if(work.Variant.InstallRoutes.Count>0) {
                    string local=LocalSource(work,i);
                    if(local==null||!File.Exists(local)||LocalModPackage.RoutedId(Safe.Hash(local),work.Variant.InstallRoutes)!=id)continue;
                }
                ids.Add(id);
            }
            if(work.Variant.InstallRoutes.Count==0&&work.Entry.Id.StartsWith("file-",StringComparison.Ordinal))ids.Add(work.Entry.Id);
            evidence.AddRange(receipts.Where(f=>f.OwnerKeys.Any(ids.Contains)));
        }
        if(work.Variant.CyberpunkId!=null) {
            var recipe=ProfileRecipe(work);package.Immutable=recipe.Immutable||evidence.Any(f=>!f.Owned||f.ReplacesPrevious);
            var routed=work.Variant.InstallRoutes.Count==0?null:GeneralInstallRoutes.FixedPaths(catalog,recipe,work.Variant.InstallRoutes);
            package.Files=recipe.Files.Where(f=>!f.UserData).Select(f=>new DisabledModFile{Path=routed==null?f.Path:routed[f.Path],Hash=f.Sha256,Size=f.Size}).ToList();
            package.RequiredMods.AddRange(recipe.Dependencies.Select(id=>state.List.Mods.FirstOrDefault(m=>m.Variants.Any(v=>v.CyberpunkId==id))).Where(m=>m!=null).Select(m=>m.Id));
        }else package.Files=evidence.GroupBy(f=>f.Path,StringComparer.OrdinalIgnoreCase).Where(g=>g.All(f=>f.Hash==g.First().Hash&&f.Size==g.First().Size))
            .Select(g=>new DisabledModFile{Path=g.Key,Hash=g.First().Hash,Size=g.First().Size}).ToList();
        if(evidence.Any(f=>!f.Owned||f.ReplacesPrevious))package.Immutable=true;
        if(package.Files.Count==0&&IsCyberpunk(root)&&IsBigBody(work.Entry)&&work.Variant.InstallRoutes.Count==0) {
            var known=seed.Mods.FirstOrDefault(m=>m.Id==work.Entry.Id);
            var verified=new List<InstalledSnapshotFile>();
            var detected=InstalledSnapshot.DetectManualHyst(root,work.Entry,known,verified,bodyChoice:work.BodyChoice);
            if(detected!=null&&detected.Id==work.Variant.Id&&GeneralModLibrary.SamePayload(detected,work.Variant))
                package.Files=verified.Select(f=>new DisabledModFile{Path=f.Path,Hash=f.Sha256,Size=f.Size}).ToList();
        }
        if(package.Files.Any(f=>!DisabledMods.AllowedPath(f.Path))) {
            package.Immutable=true;
            package.Files.RemoveAll(f=>!DisabledMods.AllowedPath(f.Path));
        }
        return package;
    }
    void EnsurePresetGameClosed(string root) {
        EnsureGameClosed(root);if(testMode||IsCyberpunk(root))return;
        string prefix=Path.GetFullPath(root).TrimEnd('\\')+"\\";
        int own;using(var current=System.Diagnostics.Process.GetCurrentProcess())own=current.Id;
        foreach(var process in System.Diagnostics.Process.GetProcesses())using(process) {
            try {if(process.Id!=own&&process.MainModule.FileName.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new IOException(T("Feche o jogo e suas ferramentas antes de carregar o preset.","Close the game and its tools before loading the preset."));}
            catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}
        }
    }
    HashSet<string> PresetSharedPaths(string root,IEnumerable<ProjectDeleteFile> receipts) {
        var paths=ExternalFilesInOtherProjects();
        foreach(var file in receipts.Where(f=>!f.Owned||f.ReplacesPrevious))paths.Add(GeneralInstallRoutes.Resolve(root,file.Path));
        if(IsCyberpunk(root))paths.UnionWith(Cyberpunk(root).ProtectedPriorFiles());
        if(projectStore==null||project==null)return paths;
        foreach(var peer in projectStore.Read().Projects.Where(p=>p.Id!=project.Id)) {
            var other=projectStore.Load(peer);
            if(String.IsNullOrWhiteSpace(other.Destination)||!Path.GetFullPath(other.Destination).TrimEnd('\\').Equals(root.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))continue;
            string peerData=projectStore.DataPath(peer);
            var files=new InstalledSnapshotInstaller(root,peerData,catalog).ProjectDeletionFiles(true)
                .Concat(new GeneralModInstaller(root,Path.Combine(peerData,"Instalacoes")).ProjectDeletionFiles(true));
            foreach(var file in files)paths.Add(GeneralInstallRoutes.Resolve(root,file.Path));
            if(IsCyberpunk(root))foreach(var id in other.CyberpunkImports.Values)
                foreach(var file in Cyberpunk(root).ProjectDeletionFiles(new HashSet<string>{id},true))paths.Add(GeneralInstallRoutes.Resolve(root,file.Path));
        }
        return paths;
    }
    SwitchPerformance localSwitchPerformance=new SwitchPerformance();
    SwitchPerformance ReadSwitchPerformance() {
        return projectStore==null?localSwitchPerformance:projectStore.Read().SwitchSettings;
    }
    int ChooseSwitchWorkers() {
        var settings=ReadSwitchPerformance();
        if(settings.AskBeforeSwitch) {
            using(var dialog=new SwitchPerformanceDialog(settings)) {
                if(dialog.ShowDialog(this)!=DialogResult.OK)return 0;
                settings=dialog.Settings;
            }
            if(projectStore==null)localSwitchPerformance=settings;
            else projectStore.SaveSwitchPerformance(settings);
        }
        return settings.Workers(Environment.ProcessorCount);
    }
    async Task LoadPreset(int workers=0) {
        if(loadingPreset||FinishingOperations||ActivePreset==null)return;
        if(!ConfirmProjectNavigation()||ActivePreset==null||!NeedsPresetLoad)return;
        string root=Root(),selected=state.SelectedPreset,fingerprint=PresetFingerprint;
        EnsurePresetGameClosed(root);GeneralPresetProfiles.Capture(state);
        if(workers<=0)workers=GeneralPresetDeployment.VerificationWorkers;
        var desiredWorks=PresetWorks().ToArray();
        foreach(var work in desiredWorks.Where(w=>w.Entry.AlternativeGroup!=null))ActivePreset.AlternativeChoices[work.Entry.AlternativeGroup]=work.Entry.Id;
        var universe=PresetUniverse().ToArray();
        var cancellation=new System.Threading.CancellationTokenSource();
        var token=cancellation.Token;
        presetCancellation=cancellation;
        loadingPreset=scanning=true;Busy(true);
        ShowPresetProgress(T("Conferindo preset","Checking preset"),0,0);
        detail.Text=ActivePreset.Name;
        var updates=new Progress<Tuple<string,int,int>>(step=>{
            if(!IsDisposed&&presetCancellation==cancellation&&!cancellation.IsCancellationRequested)
                ShowPresetProgress(step.Item1,step.Item2,step.Item3);
        });
        var progress=(IProgress<Tuple<string,int,int>>)updates;
        var elapsed=System.Diagnostics.Stopwatch.StartNew();
        long discoveryMs=0,applyMs=0;
        try {
            var result=await Task.Run(()=>{
                DateTime lastCheck=DateTime.MinValue;
                var engine=new GeneralPresetDeployment(root,data,()=>{if((DateTime.UtcNow-lastCheck).TotalSeconds>=1){EnsurePresetGameClosed(root);lastCheck=DateTime.UtcNow;}});
                var disabled=DisabledAt(root).Read();
                var receipts=PresetReceipts(root).ToArray();
                token.ThrowIfCancellationRequested();
                var desired=desiredWorks.Select(w=>PresetPackageFor(w,root,receipts,disabled)).ToList();
                var wantedKeys=new HashSet<string>(desired.Select(p=>p.Key),StringComparer.Ordinal);
                var packages=desired.Concat(universe.Where(w=>!wantedKeys.Contains(PresetRecipeKey(w)))
                    .Select(w=>PresetPackageFor(w,root,receipts,disabled))).ToList();
                var groups=packages.GroupBy(p=>p.ModId).ToArray();
                string checking=T("Conferindo mods","Checking mods");
                var discovered=GeneralPresetDeployment.CheckPackages(groups,
                    group=>group.FirstOrDefault(p=>{token.ThrowIfCancellationRequested();return PackageMatches(p,root);})??group.First(),
                    (done,total)=>progress.Report(Tuple.Create(checking,done,total)),token,workers);
                progress.Report(Tuple.Create(T("Preparando a troca","Preparing the switch"),0,0));
                var shared=PresetSharedPaths(root,receipts);
                discoveryMs=elapsed.ElapsedMilliseconds;
                using(var fixedLock=IsCyberpunk(root)?Cyberpunk(root).LockForRouteChange():null)
                using(Generic(root).ProjectDeletionLock())return engine.Apply(selected,fingerprint,desired,discovered,
                    (stage,done,total)=>progress.Report(Tuple.Create(stage,done,total)),shared,token,workers);
            });
            presetCancellation=null;
            UpdateOperationButton();
            applyMs=elapsed.ElapsedMilliseconds-discoveryMs;
            state.AppliedPreset=selected;state.AppliedPresetFingerprint=fingerprint;Persist();
            presetIssues=result.Issues;presetProtected=new HashSet<string>(result.Protected.Keys,StringComparer.Ordinal);
            detail.Text=result.Issues.Count==0?T("Preset carregado.","Preset loaded."):T("Carregado com exceções — clique para detalhes.","Loaded with exceptions — click for details.");
            UpdatePresetIssueLink();
            scanning=false;
            ShowPresetProgress(T("Atualizando a lista do preset","Refreshing the preset list"),0,0);
            await Scan(workerLimit:workers);
            RefreshStorageSize();
            RenderPresetResult();
            OnlineDiagnostics.Record("general.preset.complete",null,"Carregamento de preset concluído.",
                new{Preset=selected,Workers=workers,DiscoveryMs=discoveryMs,ApplyMs=applyMs,
                    RefreshMs=elapsed.ElapsedMilliseconds-discoveryMs-applyMs,TotalMs=elapsed.ElapsedMilliseconds,
                    ActiveMods=result.Active.Count,Issues=result.Issues.Count});
            ShowPresetProgress(result.Issues.Count==0?T("Preset carregado","Preset loaded"):
                T("Preset carregado com exceções","Preset loaded with exceptions"),1,1);
        }catch(OperationCanceledException) {
            if(!cancellation.IsCancellationRequested)throw;
            presetCancellation=null;
            detail.Text=T("Troca cancelada. A combinação anterior foi mantida.","Switch cancelled. The previous selection was kept.");
            ShowPresetProgress(T("Troca cancelada","Switch cancelled"),0,0);
            currentProgress.Indeterminate=false;
            currentProgress.NotApplicable=true;
            currentProgress.Invalidate();
        }catch {
            ShowPresetProgress(T("Carregamento interrompido","Preset load interrupted"),0,0);
            currentProgress.Indeterminate=false;
            currentProgress.NotApplicable=true;
            currentProgress.Invalidate();
            throw;
        }finally{
            presetCancellation=null;
            cancellation.Dispose();
            loadingPreset=scanning=false;
            Busy(false);RefreshPrimaryAction();
        }
    }
    void ShowPresetProgress(string stage,int done,int total) {
        currentProgress.NotApplicable=false;
        currentProgress.Indeterminate=total==0;
        currentProgress.Caption=total==0?stage+"…":done==total?stage:stage+" · "+done+" / "+total;
        currentProgress.Value=total>0?100*done/total:0;
    }
    void RenderPresetResult() {
        if(presetIssues.Count>0){detail.Text=T("Carregado com exceções — clique para detalhes.","Loaded with exceptions — click for details.");UpdatePresetIssueLink();}
    }
    List<string> presetIssues=new List<string>();
    HashSet<string> presetProtected=new HashSet<string>(StringComparer.Ordinal);
    void ReadPresetPresentation() {
        presetIssues.Clear();presetProtected.Clear();
        if(String.IsNullOrWhiteSpace(state.Destination)||!Directory.Exists(state.Destination))return;
        try {
            var deployed=new GeneralPresetDeployment(state.Destination,data).Read();
            presetIssues=deployed.Issues;presetProtected.UnionWith(deployed.Protected.Keys);state.AppliedPreset=deployed.Preset;state.AppliedPresetFingerprint=deployed.Fingerprint;UpdatePresetIssueLink();
        }catch(IOException ex){startupIssue=ex.Message;}catch(UnauthorizedAccessException ex){startupIssue=ex.Message;}
    }
    async Task RecoverPresetOnOpen() {
        if(!PresetRecoveryPending)return;
        try {
            string root=Root();var engine=new GeneralPresetDeployment(root,data,()=>EnsurePresetGameClosed(root));
            scanning=true;Busy(true);await Task.Run(()=>{
                using(var fixedLock=IsCyberpunk(root)?Cyberpunk(root).LockForRouteChange():null)
                using(Generic(root).ProjectDeletionLock())engine.Recover();
            });
            var restored=engine.Read();state.AppliedPreset=restored.Preset;state.AppliedPresetFingerprint=restored.Fingerprint;Persist();
            detail.Text=T("Troca interrompida recuperada.","Interrupted preset change recovered.");
        }catch(Exception ex){startupIssue=ex.Message;detail.Text=T("Recuperação pendente: ","Recovery pending: ")+ex.Message;}
        finally{scanning=false;Busy(false);}
    }
    void UpdatePresetIssueLink() {
        detail.Cursor=presetIssues.Count>0?Cursors.Hand:Cursors.Default;
        tips.SetToolTip(detail,presetIssues.Count>0?T("Ver exceções do carregamento","View loading exceptions"):"");
    }
    void ShowPresetIssues() {
        if(presetIssues.Count==0)return;
        using(var dialog=new GeneralDialog(T("Exceções do preset","Preset exceptions")) {ClientSize=new System.Drawing.Size(720,460)}) {
            var list=new DataGridView{Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,RowHeadersVisible=false,BackgroundColor=GeneralStyle.Surface,BorderStyle=BorderStyle.None};
            var engine=new GeneralPresetDeployment(Root(),data,()=>EnsurePresetGameClosed(Root()));var deployment=engine.Read();
            list.Columns.Add("issue",T("Mod e motivo","Mod and reason"));foreach(var issue in presetIssues) {
                int row=list.Rows.Add(issue);
                list.Rows[row].Tag=deployment.Protected.Keys.FirstOrDefault(id=>issue.StartsWith((deployment.Active.FirstOrDefault(p=>p.ModId==id)??new PresetPackage{Name=id}).Name+":",StringComparison.Ordinal));
            }
            var shell=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new Padding(16)};
            shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var resume=GeneralStyle.Button(T("Retomar gerenciamento","Resume management"),()=>{
                string id=list.CurrentRow==null?null:list.CurrentRow.Tag as string;if(id==null)return;
                if(GeneralDialog.Message(dialog,T("Adotar os arquivos atuais deste mod como referência? Os arquivos desconhecidos continuam preservados.","Adopt this mod's current files as the reference? Unknown files remain protected."),T("Retomar gerenciamento","Resume management"),MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
                Guard(()=>{engine.ReleaseProtection(id);presetProtected.Remove(id);state.AppliedPresetFingerprint=null;Persist();dialog.Complete(DialogResult.OK);RefreshPrimaryAction();});
            });
            resume.Anchor=AnchorStyles.Right;resume.Margin=new Padding(0,12,0,0);resume.Enabled=false;
            list.SelectionChanged+=(s,e)=>resume.Enabled=list.CurrentRow!=null&&list.CurrentRow.Tag is string;
            shell.Controls.Add(new GeneralScrollView(list),0,0);shell.Controls.Add(resume,0,1);
            dialog.Content.Controls.Add(shell);GeneralStyle.Apply(dialog,GeneralStyle.Dark);dialog.ShowDialog(this);
        }
    }
    // A single scope decision is made when applying an edit, never on every keystroke.
    bool? PresetEditScope(GeneralModEntry entry,bool removing=false) {
        if(state.List.Presets.Count(p=>p.ModIds.Contains(entry.Id))<2)return ActivePreset==null;
        if(ActivePreset==null&&!testMode)return GeneralDialog.Message(this,T("Aplicar a edição em todos os presets que usam este mod?","Apply the edit to every preset using this mod?"),T("Aplicar alterações","Apply changes"),MessageBoxButtons.OKCancel)==DialogResult.OK?(bool?)true:null;
        if(testMode)return testPresetEditScope==null?false:testPresetEditScope();
        var answer=GeneralDialog.Message(this,entry.Name,removing?T("Tirar da lista","Remove from list"):T("Aplicar alterações","Apply changes"),MessageBoxButtons.YesNoCancel,MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2,null,null,removing?T("Remover de todos os presets","Remove from all presets"):T("Em todos os presets","All presets"),
            removing?T("Remover deste preset","Remove from this preset"):T("Somente neste preset","This preset only"));
        return answer==DialogResult.Cancel?(bool?)null:answer==DialogResult.Yes;
    }
    Func<bool?> testPresetEditScope;

}

internal sealed partial class GeneralModEditor {
    readonly TableLayoutPanel presetMembershipRow=new TableLayoutPanel {
        Name="ModPresetMembership",Dock=DockStyle.Fill,AutoSize=true,ColumnCount=1,Visible=false,Margin=new Padding(3,8,3,10)
    };
    List<string> presetIds;
    List<string> originalPresetIds;
    internal IEnumerable<string> PresetIds {get{return presetIds;}}
    internal IEnumerable<string> ChangedPresetIds {get{return presetIds!=null&&!presetIds.SequenceEqual(originalPresetIds)?presetIds:null;}}
    internal void ConfigurePresets(IEnumerable<GeneralModPreset> presets,string selected,string modId,bool existing) {
        var available=presets.ToArray();presetMembershipRow.Controls.Clear();
        presetMembershipRow.Visible=available.Length>0;if(available.Length==0)return;
        presetIds=available.Where(p=>existing?p.ModIds.Contains(modId):p.Id==selected).Select(p=>p.Id).ToList();
        originalPresetIds=presetIds.ToList();
        var label=new Label{Text=T("Presets","Presets"),AutoSize=true,Margin=new Padding(0,0,0,6),ForeColor=GeneralStyle.Foreground};
        var choose=GeneralStyle.Button("",()=>{});choose.Name="ChooseModPresets";choose.Dock=DockStyle.Fill;choose.Margin=Padding.Empty;
        Action caption=()=>{choose.Text=presetIds.Count==1?available.Single(p=>p.Id==presetIds[0]).Name:presetIds.Count+" presets";editorTips.SetToolTip(choose,String.Join("\n",available.Where(p=>presetIds.Contains(p.Id)).Select(p=>p.Name)));};
        caption();choose.Click+=(s,e)=>{
            using(var dialog=new GeneralDialog(T("Presets deste mod","Presets for this mod")){ClientSize=new System.Drawing.Size(520,380)}) {
                var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new Padding(16),Margin=Padding.Empty};
                body.RowStyles.Add(new RowStyle(SizeType.Percent,100));body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                var checks=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=Padding.Empty};
                foreach(var p in available)checks.Controls.Add(new CheckBox{Name="PresetMembership-"+p.Id,Text=p.Name,Tag=p.Id,Checked=presetIds.Contains(p.Id),AutoSize=true,Margin=new Padding(8,10,8,10),UseMnemonic=false});
                body.Controls.Add(new GeneralScrollView(checks,panelContent:true,reserveBarSpace:true,fitPanelContent:true),0,0);
                var commands=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,FlowDirection=FlowDirection.RightToLeft,WrapContents=false};
                var apply=GeneralStyle.Button(T("Aplicar","Apply"),()=>dialog.Complete(DialogResult.OK),true);commands.Controls.Add(apply);dialog.AcceptButton=apply;
                commands.Controls.Add(GeneralStyle.Button(T("Cancelar","Cancel"),()=>dialog.Complete(DialogResult.Cancel)));
                body.Controls.Add(commands,0,1);dialog.Content.Controls.Add(body);GeneralStyle.Apply(dialog,GeneralStyle.Dark);
                dialog.AnchorTo(choose);
                if(dialog.ShowDialog(this)==DialogResult.OK){presetIds=checks.Controls.OfType<CheckBox>().Where(c=>c.Checked).Select(c=>(string)c.Tag).ToList();caption();}
            }
        };
        presetMembershipRow.Controls.Add(label,0,0);presetMembershipRow.Controls.Add(choose,0,1);presetMembershipRow.RowCount=2;
    }
}
