using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    sealed class ModUpdateRow {
        internal string Key,Source,LookupSource,Provider,Expected,Local,LocalStatus;
        internal NexusUpdateResult Online;
    }
    List<ModUpdateRow> modUpdates=new List<ModUpdateRow>();
    NexusUpdateSession testModUpdateSession;
    GithubModUpdateSession testGithubUpdateSession;
    ILookup<string,ModUpdateRow> versionsByWork=new ModUpdateRow[0].ToLookup(row=>row.Key,StringComparer.Ordinal);

    TableLayoutPanel versionFooter;
    LinkLabel retryVersionCheck;
    enum VersionFilter { Checked, Update, Review, Pending }
    VersionFilter? versionFilter;
    readonly LinkLabel[] footerNumbers=new LinkLabel[4];
    static string[] VersionFilterCaptions(){return new[]{T("Consultados","Checked"),T("Atualizar","Update"),T("Revisar","Review"),T("Sem resultado","No result")};}

    int modUpdateGeneration;
    bool checkingModUpdates,connectingNexus;

    void ShowVersionCheckProgress(int total,int completed,int checkedMods,bool finished=false){
        if(currentProgress==null||currentProgress.IsDisposed||working||loadingPreset||scanning&&scanCurrent!=null)return;
        if(next!=null&&scanning)next.Visible=false;
        int pending=total-checkedMods;
        currentProgress.NotApplicable=false;
        currentProgress.Indeterminate=!finished&&total==0;
        currentProgress.Value=finished?100:total==0?0:Math.Min(99,100*completed/total);
        currentProgress.Caption=(finished?(pending==0?T("Atualizações verificadas","Updates checked"):T("Consulta de versões","Version check")):
            T("Verificando atualizações","Checking for updates"))+" · "+checkedMods+"/"+total+" mods";
        if(finished&&pending>0)currentProgress.Caption+=" · "+pending+T(" sem resultado"," without result");
        currentProgress.Invalidate();
    }
    async Task RefreshModVersions(bool fresh=false,VersionLookup lookup=null,Func<string,string> readHash=null){
        try{await CheckModUpdates(fresh,lookup,readHash);}
        catch(Exception ex){if(!IsDisposed)OnlineDiagnostics.Record("mods.update.check",null,ex.GetType().Name);}
    }

    void ClearModVersions(){
        modUpdateGeneration++;
        ClearSourceUpdateOwnership();
        sharedInstalledVersions.Clear();
        versionFilter=null;
        checkingModUpdates=false;
        modUpdates.Clear();
        versionsByWork=modUpdates.ToLookup(row=>row.Key,StringComparer.Ordinal);
        ShowModUpdateNotice();
    }

    static bool VersionInstalled(string status){
        return status==UpdateAvailableStatus||InstalledVersion(status)||GeneralStatusPresentation.Kind(status)=="installed";
    }

    GeneralModEntry[] SelectedSourceReviews(){
        if(marked.Count==0&&updateFocusedId==null)return new GeneralModEntry[0];
        var selected=new HashSet<string>(TableSelectionIds()
            .Where(id=>marked.Count>0?marked.Contains(id):id==updateFocusedId),StringComparer.Ordinal);
        return state.List.Mods.Where(mod=>selected.Contains(mod.Id)&&!ManualUpdatesOnly(mod.Id)&&VersionInstalled(ListStatus(mod))&&SourceUpdateTarget(MakeWork(mod))==null)
            .Where(mod=>versionsByWork[MakeWork(mod).Key].Any(HasSourceUpdate)).ToArray();
    }

    GeneralSourceReviewDialog.Item SourceReview(string id){
        var mod=state.List.Mods.First(entry=>entry.Id==id);
        var work=MakeWork(mod);
        var rows=versionsByWork[work.Key];
        var updates=rows.Where(HasSourceUpdate).ToArray();
        var item=new GeneralSourceReviewDialog.Item{Id=id,Name=mod.Name,Variant=work.Variant.Name,
            Installed=VersionInStatus(work.Key,ListStatus(mod)),
            Published=String.Join(", ",updates.Select(row=>row.Online.Version).Where(value=>!String.IsNullOrWhiteSpace(value)).Distinct())};
        var proof=SourceUpdateOwnership(work);
        item.Target=SourceUpdateTarget(work);
        item.Archives=ReviewUpdateArchives(work,rows);
        item.CanChoose=proof!=null&&proof.CanUpdate&&item.Archives.Length>0;
        if(item.Target!=null)item.Details=T("O pacote da variante foi identificado. Você pode iniciar a atualização.",
            "The archive for this variant was identified. You can start the update.");
        else if(item.CanChoose)item.Details=T("Escolha o pacote publicado que corresponde à sua variante. O Loader confere o arquivo antes de substituir a instalação.",
            "Choose the published archive for your variant. Loader checks the archive before replacing the installation.");
        else if(updates.Length==0)item.Details=rows.Any()&&VersionChecked(rows)
            ?T("A consulta não apontou uma versão mais recente para esta instalação.","The check found no newer version for this installation.")
            :T("A consulta não confirmou uma atualização. Você pode verificar novamente ou manter a versão instalada.",
                "The check did not confirm an update. You can check again or keep the installed version.");
        else if(proof!=null&&!String.IsNullOrWhiteSpace(proof.Reason))item.Details=proof.Reason+" "+T(
            "A versão instalada pode ser mantida.","You can keep the installed version.");
        else if(!SupportsSourceReplacement(work))item.Details=T(
            "A instalação desta variante usa uma configuração que impede a troca direta do pacote. Mantenha a versão instalada.",
            "This variant uses an installation setup that prevents direct archive replacement. Keep the installed version.");
        else item.Details=T("Ainda não há um pacote confirmado para esta variante. Você pode verificar novamente ou manter a versão instalada.",
            "There is no confirmed archive for this variant yet. You can check again or keep the installed version.");
        return item;
    }

    async Task ReviewSourceUpdates(GeneralModEntry[] items,bool manualUpdate=false){
        if(GeneralDialog.FocusOpen<GeneralSourceReviewDialog>(this))return;
        string[] ids=items.Select(mod=>mod.Id).ToArray();
        var projectState=state;
        string destination=state.Destination,presetId=state.SelectedPreset;
        using(var dialog=new GeneralSourceReviewDialog(ids,SourceReview,()=>CheckModUpdates(fresh:true))){
            if(await dialog.ShowModelessAsync(this)!=DialogResult.OK||!Object.ReferenceEquals(projectState,state)||IsDisposed||
                exitApproved||working||scanning||state.SelectedPreset!=presetId||!InventoryPaths.Equals(state.Destination,destination))return;
            var entry=state.List.Mods.FirstOrDefault(mod=>mod.Id==dialog.SelectedMod);
            if(entry==null)return;
            var work=MakeWork(entry);
            var review=SourceReview(work.Entry.Id);
            work.UpdateTarget=dialog.SelectedTarget;
            work.ReviewSelectedPackage=review.CanChoose&&review.Archives.Contains(work.UpdateTarget,StringComparer.Ordinal);
            if(work.UpdateTarget!=review.Target&&!work.ReviewSelectedPackage)throw UpdateNeedsReview();
            work.UpdateRequested=true;
            work.Batch=new UpdateBatch();
            await PlanAndConfirmJobs(new List<Work>{work},Root(),ModAction.Update,manualUpdate);
        }
    }

    static bool HasSourceUpdate(ModUpdateRow row){
        return VersionInstalled(row.LocalStatus)&&row.Online!=null&&row.Online.State=="update"&&!LocalAtLeastOnline(row);
    }

    string VersionInStatus(string key,string status){
        if(!VersionInstalled(status)&&status!=UpdateAvailableStatus&&!LocalChanged(status))return null;
        GeneralModInstaller.UpgradeInfo installation;
        if(sharedInstalledVersions.TryGetValue(key,out installation)&&!String.IsNullOrEmpty(installation.Version))return installation.Version;
        var observations=versionsByWork[key].ToArray();
        string local=observations.Select(row=>row.Local).FirstOrDefault(value=>!String.IsNullOrWhiteSpace(value));
        if(!VersionInstalled(status))return local;
        string[] source=observations.Select(row=>row.Online==null?null:row.Online.SourceVersion).ToArray();
        if(source.Length>0&&source.All(version=>!String.IsNullOrWhiteSpace(version)&&version==source[0]))return source[0];
        return local??observations.Select(row=>row.Expected).FirstOrDefault(value=>!String.IsNullOrWhiteSpace(value));
    }

    void SetModStatusCell(DataGridViewRow row,GeneralModEntry entry,string key,string status,string raw){
        var cell=row.Cells["Status"];
        string version=VersionInStatus(key,status);
        var active=ActiveUpdateRow(entry.Id);
        string display=raw;
        if(active==null&&(entry.Variants.Count>1||IsBigBody(entry))){
            string local;
            statuses.TryGetValue(key,out local);
            display=VersionInstalled(local)||VersionInstalled(status)?"Instalado":
                local==null&&scanning?"Não conferido":GeneralStatusPresentation.FolderNotInstalled;
        }
        cell.Tag=display;
        cell.Value=active==null?GeneralStatusPresentation.Label(display)+(version==null?"":" · "+version):
            active.UpdatePhase+(active.UpdatePercent.HasValue?" · "+active.UpdatePercent.Value+"%":"");
        var text=TableStatusDetail(entry,key,status);
        if(version!=null)text+="\n"+T("Versão instalada ","Installed version ")+version;
        var updates=versionsByWork[key].Where(HasSourceUpdate).Select(update=>update.Online.Version)
            .Where(value=>!String.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).ToArray();
        if(updates.Length>0)text+="\n"+T("Versão mais recente na origem ","Newer upstream version ")+String.Join(", ",updates);
        if(ManualUpdatesOnly(entry.Id)&&(updates.Length>0||status==UpdateAvailableStatus))
            text+="\n"+T("Use Atualizar mod nas opções deste item.","Use Update mod in this item's options.");
        if(raw==GeneralStatusPresentation.ReviewUpdate){
            var proof=SourceUpdateOwnership(MakeWork(entry));
            text+="\n"+(proof!=null&&!String.IsNullOrWhiteSpace(proof.Reason)?proof.Reason:T(
                "Use Revisar atualização para ver os pacotes disponíveis ou conferir novamente.",
                "Use Review update to see available archives or check again."));
        }
        var pending=versionsByWork[key].Where(observation=>observation.Online==null||
            observation.Online.State!="current"&&observation.Online.State!="update")
            .Select(observation=>status==GeneralStatusPresentation.DetectedFiles&&observation.Online!=null&&observation.Online.State=="no-source"
                ?T("Para consultar atualizações, adicione o link do Nexus ou GitHub em Detalhes do mod.",
                    "To check for updates, add the Nexus or GitHub link in Mod details.")
                :UpdateState(observation.Online)).Distinct().ToArray();
        if(pending.Length>0){
            string reason=String.Join(" · ",pending);
            text+="\n"+reason;
            if(versionFilter==VersionFilter.Pending&&active==null){
                cell.Value=cell.Value+"\n"+reason;
                row.Height=grid.RowTemplate.Height+TextRenderer.MeasureText(reason,grid.Font,Size.Empty,TextFormatFlags.SingleLine).Height+2;
            }
        }
        cell.ToolTipText=text;
    }



    sealed class VersionCounts {
        internal int Total,Checked,Ready,Review,Unchecked,Unsupported;
    }

    static bool HasVersionResult(ModUpdateRow row){
        return row.Online!=null&&(row.Online.State=="current"||row.Online.State=="update");
    }

    static bool VersionChecked(IEnumerable<ModUpdateRow> rows){
        return rows.Any()&&rows.All(HasVersionResult);
    }

    VersionCounts CountVersions(bool currentPreset=false){
        var groups=modUpdates.GroupBy(row=>row.Key,StringComparer.Ordinal).ToArray();
        var ready=InstallableUpdates();
        var readyKeys=new HashSet<string>(ready.Select(work=>work.Key),StringComparer.Ordinal);
        HashSet<string> keys=null;
        if(currentPreset){
            keys=new HashSet<string>(PresetListMods().Select(mod=>MakeWork(mod).Key),StringComparer.Ordinal);
            groups=groups.Where(group=>keys.Contains(group.Key)).ToArray();
        }
        return new VersionCounts{
            Total=groups.Length,
            Checked=groups.Count(VersionChecked),
            Ready=ready.Count(work=>keys==null||keys.Contains(work.Key)),
            Review=groups.Count(group=>!ManualUpdatesOnly(group.Key.Split('/')[0])&&!readyKeys.Contains(group.Key)&&group.Any(HasSourceUpdate)),
            Unchecked=groups.Count(group=>group.Any(row=>row.Provider!=null&&!HasVersionResult(row))),
            Unsupported=groups.Count(group=>group.Any(row=>row.Provider==null)&&
                group.All(row=>row.Provider==null||HasVersionResult(row)))
        };
    }

    IEnumerable<KeyValuePair<string,string>> ModVersionSummary(){
        var count=CountVersions();
        if(count.Total==0)yield break;
        yield return VerificationLine(T("Versões consultadas","Versions checked"),String.Format(T("{0} de {1} mods","{0} of {1} mods"),count.Checked,count.Total));
        var updates=new List<string>();
        if(count.Ready>0)updates.Add(String.Format(T("{0} para atualizar","{0} ready to update"),count.Ready));
        if(count.Review>0)updates.Add(String.Format(T("{0} para revisar","{0} to review"),count.Review));
        if(updates.Count>0)yield return VerificationLine(T("Atualizações","Updates"),String.Join(" · ",updates));
        var unavailable=new List<string>();
        if(count.Unchecked>0)unavailable.Add(String.Format(T("{0} sem consulta","{0} unchecked"),count.Unchecked));
        if(count.Unsupported>0)unavailable.Add(String.Format(T("{0} sem fonte compatível","{0} without a supported source"),count.Unsupported));
        if(unavailable.Count>0)yield return VerificationLine(T("Limites da consulta","Check coverage"),String.Join(" · ",unavailable));
    }

    bool NeedsNexusConnection(){
        return modUpdates.Any(row=>row.Online!=null&&(row.Online.State=="connect"||row.Online.State=="authentication"));
    }

    bool VersionFilterAllows(GeneralModEntry mod,HashSet<string> actionable){
        if(!versionFilter.HasValue)return true;
        string key=MakeWork(mod).Key;
        var rows=versionsByWork[key];
        switch(versionFilter.Value){
            case VersionFilter.Checked:return VersionChecked(rows);
            case VersionFilter.Update:return actionable.Contains(key);
            case VersionFilter.Review:return !ManualUpdatesOnly(mod.Id)&&!actionable.Contains(key)&&rows.Any(HasSourceUpdate);
            default:return rows.Any()&&!VersionChecked(rows);
        }
    }

    Control BuildVersionFooter(){
        versionFooter=new TableLayoutPanel{Name="VersionFooter",Dock=DockStyle.Top,ColumnCount=footerNumbers.Length,
            RowCount=2,Height=60,Margin=new Padding(0,1,0,0)};
        versionFooter.RowStyles.Add(new RowStyle(SizeType.Absolute,20));
        versionFooter.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var title=new Label{Name="VersionFooterTitle",Text=T("Consulta de atualizações","Update check"),
            Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,Font=GeneralStyle.Font(10),
            ForeColor=GeneralStyle.Muted,Margin=Padding.Empty};
        versionFooter.Controls.Add(title,1,0);
        versionFooter.SetColumnSpan(title,2);
        retryVersionCheck=new LinkLabel{Name="RetryVersionCheck",Text=T("Tentar de novo","Check again"),
            Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,Font=GeneralStyle.Font(10),
            Margin=Padding.Empty,LinkBehavior=LinkBehavior.HoverUnderline,TabStop=true};
        retryVersionCheck.AccessibleDescription=T("Consulta novamente só as fontes compatíveis dos mods sem resultado.",
            "Rechecks only supported sources for mods without a result.");
        tips.SetToolTip(retryVersionCheck,retryVersionCheck.AccessibleDescription);
        retryVersionCheck.LinkClicked+=async(s,e)=>await RetryUnresolvedVersions();
        idle.Add(retryVersionCheck);
        versionFooter.Controls.Add(retryVersionCheck,3,0);
        for(int index=0;index<footerNumbers.Length;index++){
            var filter=(VersionFilter)index;
            versionFooter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/footerNumbers.Length));
            var button=new LinkLabel{Name="VersionFilter"+filter,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,
                Font=GeneralStyle.Font(10.5f,true),Margin=Padding.Empty,LinkBehavior=LinkBehavior.HoverUnderline,
                ActiveLinkColor=GeneralStyle.Foreground,TabStop=true};
            button.LinkClicked+=(s,e)=>{
                if(scanning||working||associationChoice!=null)return;
                versionFilter=versionFilter==filter?(VersionFilter?)null:filter;
                RedrawFromTop();
            };
            footerNumbers[index]=button;
            versionFooter.Controls.Add(button,index,1);
        }
        ShowModUpdateNotice();
        return versionFooter;
    }

    void ShowModUpdateNotice(){
        if(versionFooter==null||versionFooter.IsDisposed)return;
        bool show=!pickingInventoryOwner&&modUpdates.Count>0&&!checkingModUpdates;

        versionFooter.Visible=show;
        RefreshVersionFilters();

        UpdateFooterAction();
    }

    void RefreshVersionFilters(){
        if(versionFooter==null||versionFooter.IsDisposed||!versionFooter.Visible)return;
        var count=CountVersions(currentPreset:true);
        retryVersionCheck.Visible=count.Total>count.Checked&&UnresolvedVersionSources().Any();
        retryVersionCheck.LinkColor=GeneralStyle.Accent;
        retryVersionCheck.ActiveLinkColor=GeneralStyle.Foreground;
        var captions=VersionFilterCaptions();
        var values=new[]{count.Checked+"/"+count.Total,count.Ready.ToString(),count.Review.ToString(),
            (count.Total-count.Checked).ToString()};
        for(int index=0;index<values.Length;index++){
            var button=footerNumbers[index];
            bool active=versionFilter==(VersionFilter)index;
            button.Text=values[index]+"\n"+captions[index];
            button.AccessibleName=captions[index]+" "+values[index];
            button.AccessibleDescription=active?T("Filtro ativo","Filter active"):T("Filtrar lista de mods","Filter the mod list");
            if(index==(int)VersionFilter.Pending){
                string meaning=T("A consulta não confirmou se há uma versão mais recente. O selo da linha mostra se o mod está instalado.",
                    "The update check did not confirm whether a newer version exists. The row badge shows whether the mod is installed.");
                tips.SetToolTip(button,meaning);
                button.AccessibleDescription+=" "+meaning;
            }
            button.LinkColor=active?GeneralStyle.Foreground:GeneralStyle.Accent;
            button.BackColor=active?GeneralStyle.Selection:GeneralStyle.Background;
        }
    }
    static bool LocalChanged(string status){return status==GeneralModInstaller.Modified||status=="Diferente/alterado"||status=="Versão diferente"||status==GeneralModInstaller.Partial||status=="Parcial";}
    static bool LocalAtLeastOnline(ModUpdateRow row){
        return LocalChanged(row.LocalStatus)&&row.Online!=null&&row.Online.State=="update"&&ModVersionNumbers.Compare(row.Local,row.Online.Version)>=0;
    }
    static string CheckedGithubSource(GeneralModVariant variant,string source) {
        var original=GeneralGithubLink.Parse(source);
        if(original==null||original.Asset!=null||variant==null||variant.SourceInfo==null||source==null)return source;
        GeneralSourceInfo saved;
        if(!variant.SourceInfo.TryGetValue(source,out saved)||!saved.GithubChecked)return source;
        var pinned=GeneralGithubLink.Parse(saved.GithubAssetUrl);
        return GeneralGithubLink.SameRepository(original,pinned)&&pinned.Asset!=null&&pinned.Tag==saved.Version&&
            (original.Tag==null||original.Tag==pinned.Tag)?pinned.Url:source;
    }
    sealed class VersionLookup {
        readonly NexusUpdateSession nexus;
        readonly GithubModUpdateSession github;
        readonly Func<bool> active;
        readonly Dictionary<string,Task<NexusUpdateResult>> requests=new Dictionary<string,Task<NexusUpdateResult>>(StringComparer.Ordinal);

        internal VersionLookup(NexusUpdateSession nexus,GithubModUpdateSession github,Func<bool> active){
            this.nexus=nexus;this.github=github;this.active=active;
        }
        internal bool HasNexusCredential {get{return nexus.HasCredential;}}

        internal void Prefetch(IEnumerable<string> sources){
            foreach(string source in sources.Where(GeneralModDownload.IsWebSource).Distinct(StringComparer.Ordinal))
                Check(source);
        }

        internal Task<NexusUpdateResult> Check(string source){
            if(source==null)return Task.FromResult(NexusUpdateResult.Unknown("no-source"));
            Task<NexusUpdateResult> result;
            if(!requests.TryGetValue(source,out result))requests.Add(source,result=Read(source));
            return result;
        }

        async Task<NexusUpdateResult> Read(string source){
            try{
                var link=GeneralNexusLink.Parse(source);
                if(link!=null)return await nexus.Check(link,active);
                if(GithubModUpdateSession.Supports(source))return await github.Check(source,active);
                return NexusUpdateResult.Unknown("no-source");
            }catch(Exception ex){
                OnlineDiagnostics.Record("mods.update.check",null,ex.GetType().Name);
                return NexusUpdateResult.Unknown("unavailable");
            }
        }
    }

    VersionLookup CreateVersionLookup(bool fresh,Func<bool> active){
        var nexus=testModUpdateSession??(testMode?new NexusUpdateSession(null):fresh?NexusUpdateSession.Fresh():NexusUpdateSession.Session);
        var github=testGithubUpdateSession??(testMode?new GithubModUpdateSession(_=>null):fresh?new GithubModUpdateSession():GithubModUpdateSession.Session);
        return new VersionLookup(nexus,github,active);
    }

    async Task CheckModUpdates(bool fresh=false,VersionLookup lookup=null,Func<string,string> readHash=null){
        if((testMode&&testModUpdateSession==null&&testGithubUpdateSession==null)||IsDisposed||exitApproved)return;
        int generation=++modUpdateGeneration;string destination=state.Destination;
        ClearSourceUpdateOwnership();
        var captured=state;
        var rows=state.List.Mods.Select(MakeWork).SelectMany(work=>{
            var sources=work.Variant.Sources.Where(GeneralModDownload.IsWebSource).Distinct(StringComparer.Ordinal).ToArray();
            string localStatus;statuses.TryGetValue(work.Key,out localStatus);
            string expected=null;
            if(work.Variant.CyberpunkId!=null)expected=ProfileRecipe(work).Version;
            else if(work.Variant.Sources.Count==1){
                GeneralSourceInfo saved;
                if(work.Variant.SourceInfo.TryGetValue(work.Variant.Sources[0],out saved)&&(saved.NexusChecked||saved.GithubChecked))expected=saved.Version;
            }
            NexusManualMatch manual;
            if(localStatus==GeneralStatusPresentation.NexusManual&&nexusManual.TryGetValue(work.Entry.Id,out manual)&&
                Object.ReferenceEquals(manual.Variant,work.Variant))expected=manual.Version??expected;
            GeneralModInstaller.UpgradeInfo detected;
            if(sharedInstalledVersions.TryGetValue(work.Key,out detected))expected=detected.Version;
            return (sources.Length==0?new string[]{null}:sources).Select(source=>new ModUpdateRow{
                Key=work.Key,Source=source,LookupSource=detected==null?CheckedGithubSource(work.Variant,source):detected.Source,
                Provider=GeneralNexusLink.Parse(source)!=null?"nexus":GithubModUpdateSession.Supports(source)?"github":null,
                Expected=expected,LocalStatus=localStatus});
        }).ToList();
        var knownPaths=new Dictionary<string,List<string>>(StringComparer.Ordinal);
        foreach(var work in state.List.Mods.Select(MakeWork)){
            knownPaths[work.Key]=work.Entry.DetectedPaths.ToList();
            if(work.Variant.CyberpunkId!=null)knownPaths[work.Key].AddRange(ProfileRecipe(work).Files.Select(file=>file.Path));
        }
        checkingModUpdates=true;modUpdates=rows;ShowModUpdateNotice();
        Func<bool> active=()=>!IsDisposed&&!exitApproved&&generation==modUpdateGeneration&&Object.ReferenceEquals(captured,state)&&destination==state.Destination;
        var groups=rows.GroupBy(row=>row.Key,StringComparer.Ordinal).ToArray();
        int completed=0,checkedMods=0;
        ShowVersionCheckProgress(groups.Length,0,0);
        try{
            if(lookup==null)lookup=CreateVersionLookup(fresh,active);
            var requests=groups.Select(async group=>{
                await Task.WhenAll(group.Select(async row=>row.Online=await lookup.Check(row.LookupSource??row.Source)));
                completed++;if(VersionChecked(group))checkedMods++;
                if(active()&&checkingModUpdates)ShowVersionCheckProgress(groups.Length,completed,checkedMods);
            }).ToArray();
            if(!String.IsNullOrEmpty(destination)&&Directory.Exists(destination)){
                try{
                    var paths=await Task.Run(()=>UpdateVersionPaths(destination,knownPaths));
                    if(active())foreach(var row in rows){string version;if(paths.TryGetValue(row.Key,out version))row.Local=version;}
                }catch(IOException ex){OnlineDiagnostics.Record("mods.update.local-version",null,ex.GetType().Name);}
                catch(UnauthorizedAccessException ex){OnlineDiagnostics.Record("mods.update.local-version",null,ex.GetType().Name);}
            }
            await Task.WhenAll(requests);
            if(active()&&!String.IsNullOrEmpty(destination)&&Directory.Exists(destination))
                await AuditSourceUpdateOwnership(rows,destination,readHash,active);
        }catch(IOException){foreach(var row in rows.Where(r=>r.Online==null))row.Online=NexusUpdateResult.Unknown("unavailable");}
        catch(UnauthorizedAccessException){foreach(var row in rows.Where(r=>r.Online==null))row.Online=NexusUpdateResult.Unknown("unavailable");}
        finally{
            if(active()){
                checkingModUpdates=false;
                versionsByWork=rows.ToLookup(row=>row.Key,StringComparer.Ordinal);
                ShowVersionCheckProgress(groups.Length,completed,groups.Count(VersionChecked),finished:true);
                ShowModUpdateNotice();
                if(grid!=null&&!scanning&&!working)DrawRows();
            }
        }
    }
    IEnumerable<ModUpdateRow> UnresolvedVersionSources(){
        return PresetListMods().Select(MakeWork).SelectMany(work=>versionsByWork[work.Key].Where(row=>
            row.Provider!=null&&!HasVersionResult(row)&&work.Variant.Sources.Contains(row.Source)));
    }

    void ShowVersionRetryProgress(int completed,int total,int? confirmed=null){
        currentProgress.NotApplicable=false;
        currentProgress.Indeterminate=false;
        currentProgress.Value=confirmed.HasValue?100:Math.Min(99,100*completed/total);
        currentProgress.Caption=confirmed.HasValue
            ?T("Nova consulta","Recheck")+" · "+confirmed+"/"+total+T(" com resultado"," with a result")
            :T("Reconsultando atualizações","Checking for updates again")+" · "+completed+"/"+total+" mods";
        currentProgress.Invalidate();
    }

    async Task RetryUnresolvedVersions(){
        if(IsDisposed||exitApproved||checkingModUpdates||scanning||working||loadingPreset||connectingNexus||associationChoice!=null)return;
        var groups=UnresolvedVersionSources().Distinct().GroupBy(row=>row.Key,StringComparer.Ordinal).ToArray();
        if(groups.Length==0)return;
        var captured=state;
        string destination=state.Destination,presetId=state.SelectedPreset;
        int generation=++modUpdateGeneration,completed=0;
        Func<bool> active=()=>!IsDisposed&&!exitApproved&&generation==modUpdateGeneration&&ReferenceEquals(captured,state)&&
            destination==state.Destination&&presetId==state.SelectedPreset&&!working&&!scanning&&!loadingPreset;
        checkingModUpdates=true;
        ShowModUpdateNotice();
        ShowVersionRetryProgress(0,groups.Length);
        try{
            var lookup=CreateVersionLookup(true,active);
            var results=await Task.WhenAll(groups.Select(async group=>{
                var responses=await Task.WhenAll(group.Select(row=>lookup.Check(row.LookupSource??row.Source)));
                completed++;
                if(active())ShowVersionRetryProgress(completed,groups.Length);
                return group.Zip(responses,(row,online)=>new KeyValuePair<ModUpdateRow,NexusUpdateResult>(row,online));
            }));
            if(!active())return;
            foreach(var result in results.SelectMany(group=>group))result.Key.Online=result.Value;
            var affected=groups.SelectMany(group=>versionsByWork[group.Key]).ToList();
            if(!String.IsNullOrEmpty(destination)&&Directory.Exists(destination))
                await AuditSourceUpdateOwnership(affected,destination,active:active);
            if(active())ShowVersionRetryProgress(completed,groups.Length,groups.Count(group=>VersionChecked(versionsByWork[group.Key])));
        }catch(Exception ex){
            if(active()){
                OnlineDiagnostics.Record("mods.update.retry",null,ex.GetType().Name);
                currentProgress.Value=100;
                currentProgress.Caption=T("Não foi possível concluir a nova consulta","Could not finish the recheck");
                currentProgress.Invalidate();
            }
        }finally{
            if(!IsDisposed&&!exitApproved&&generation==modUpdateGeneration){
                checkingModUpdates=false;
                ShowModUpdateNotice();
                if(active())DrawRows();
            }
        }
    }

    Dictionary<string,string> UpdateVersionPaths(string root,Dictionary<string,List<string>> paths){
        Action<string,string> add=(key,path)=>{List<string> values;if(!paths.TryGetValue(key,out values))paths[key]=values=new List<string>();values.Add(path);};
        foreach(var file in Generic(root).ProjectDeletionFiles(true))foreach(string owner in file.OwnerKeys)add(WorkKeyFromReceipt(owner),file.Path);
        foreach(var file in Snapshot(root).ProjectDeletionFiles(true,skipUnreadable:true))foreach(string owner in file.OwnerKeys)add(owner,file.Path);
        return paths.ToDictionary(pair=>pair.Key,pair=>ModVersionNumbers.ReadLocal(root,pair.Value),StringComparer.Ordinal);
    }
    static string UpdateState(NexusUpdateResult result){
        switch(result==null?"pending":result.State){
            case "update":return GeneralStatusPresentation.Label(UpdateAvailableStatus);
            case "current":return T("Sem sucessor informado","No successor reported");
            case "connect":return T("Conecte sua conta Nexus","Connect your Nexus account");
            case "authentication":return T("Autenticação recusada","Authentication rejected");
            case "rate-limit":return T("Limite da API atingido","API rate limit reached");
            case "ambiguous":return T("Versões ambíguas: revisar","Ambiguous versions: review");
            case "choose-file":return T("Arquivo da versão indefinido","Version file not identified");
            case "budget":return T("Limite de consultas atingido","Check limit reached");
            case "cancelled":return T("Consulta interrompida","Check interrupted");
            case "no-source":return T("Sem origem compatível para consultar","No supported source to check");

            case "pending":return T("Consultando…","Checking…");
            default:return T("Não foi possível consultar","Could not check");
        }
    }
    async void ConnectNexus(){
        if(GeneralDialog.FocusOpen<GeneralNexusConnectionDialog>(this)||connectingNexus||working||scanning||IsDisposed)return;
        connectingNexus=true;UpdateFooterAction();
        try{
            using(var dialog=new GeneralNexusConnectionDialog(launch,GeneralNexusInfo.CheckPersonalKey)){
                if(await dialog.ShowModelessAsync(this)!=DialogResult.OK||IsDisposed||exitApproved)return;
                NexusCredentials.Save(GeneralAppPaths.Current().Data,dialog.PersonalKey);
            }
            await RefreshModVersions(true);
        }catch(Exception ex){if(!IsDisposed)ShowError(ex);}
        finally{connectingNexus=false;if(!IsDisposed)UpdateFooterAction();}
    }
}

internal sealed class GeneralSourceReviewDialog : GeneralDialog {
    internal sealed class Item {
        internal string Id,Name,Variant,Installed,Published,Details,Target;
        internal string[] Archives;
        internal bool CanChoose;
    }

    static string T(string pt,string en){return GeneralStyle.T(pt,en);}
    readonly string[] ids;
    readonly Func<string,Item> read;
    readonly Func<Task> refresh;
    readonly GeneralPicker mods=new GeneralPicker{Name="ReviewMods",Dock=DockStyle.Top,Margin=Padding.Empty};
    readonly GeneralPicker archives=new GeneralPicker{Name="ReviewArchives",Dock=DockStyle.Top,Margin=Padding.Empty};
    readonly Label name=new Label{Name="ReviewModName",AutoSize=true,Dock=DockStyle.Top};
    readonly Label versions=new Label{Name="ReviewVersions",AutoSize=true,Dock=DockStyle.Top};
    readonly Label archiveLabel=new Label{AutoSize=true,Dock=DockStyle.Top};
    readonly Label details=new Label{Name="ReviewNextStep",AutoSize=true,Dock=DockStyle.Top,Margin=Padding.Empty};
    readonly Button update,recheck;
    Item item;
    internal string SelectedMod {get;private set;}
    internal string SelectedTarget {get;private set;}

    internal GeneralSourceReviewDialog(string[] ids,Func<string,Item> read,Func<Task> refresh)
        :base(T("Revisar atualização","Review update")){
        Name="SourceUpdateReview";
        this.ids=ids;this.read=read;this.refresh=refresh;
        ClientSize=new Size(720,ids.Length>1?410:362);
        MinimumSize=new Size(660,330);
        mods.AccessibleName=T("Mod para revisar","Mod to review");
        archives.AccessibleName=archiveLabel.Text=T("Pacote publicado","Published archive");
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new Padding(20,8,20,18)};
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mods.Height=44;mods.Margin=new Padding(0,0,0,12);
        foreach(string id in ids)mods.Items.Add(read(id).Name);
        layout.Controls.Add(mods,0,0);mods.Visible=ids.Length>1;
        name.Margin=new Padding(0,0,0,4);layout.Controls.Add(name,0,1);
        versions.Margin=new Padding(0,0,0,12);layout.Controls.Add(versions,0,2);
        var explanation=new Panel();
        explanation.Controls.Add(details);
        explanation.SizeChanged+=(s,e)=>details.MaximumSize=new Size(Math.Max(1,explanation.ClientSize.Width),0);
        var body=new GeneralScrollView(explanation,panelContent:true,fitPanelContent:true);
        layout.Controls.Add(body,0,3);
        var packages=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,RowCount=2,Margin=new Padding(0,6,0,0)};
        packages.RowStyles.Add(new RowStyle(SizeType.AutoSize));packages.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        packages.Controls.Add(archiveLabel,0,0);archives.Height=44;packages.Controls.Add(archives,0,1);
        layout.Controls.Add(packages,0,4);
        var actions=GeneralTableDialogLayout.Commands();
        update=GeneralStyle.Button(T("Atualizar mod","Update mod"),()=>{
            string target=CurrentTarget();
            if(target==null)return;
            SelectedMod=item.Id;SelectedTarget=target;Complete(DialogResult.OK);
        },true);
        update.Name="ApplyReviewedUpdate";
        recheck=GeneralStyle.Button(T("Verificar novamente","Check again"),Recheck);
        recheck.Name="RecheckReviewedUpdate";
        var keep=GeneralStyle.Button(T("Manter versão instalada","Keep installed version"),()=>Complete(DialogResult.Cancel));
        keep.Name="KeepInstalledVersion";
        actions.Controls.Add(update);actions.Controls.Add(recheck);actions.Controls.Add(keep);
        layout.Controls.Add(actions,0,5);Content.Controls.Add(layout);
        CancelButton=keep;
        GeneralStyle.Apply(this,GeneralStyle.Dark);
        name.Font=GeneralStyle.Font(13,true);
        layout.SizeChanged+=(s,e)=>{
            int width=Math.Max(1,layout.ClientSize.Width-layout.Padding.Horizontal);
            name.MaximumSize=versions.MaximumSize=new Size(width,0);
        };
        mods.SelectedIndex=0;
        mods.SelectedIndexChanged+=(s,e)=>Render();
        archives.SelectedIndexChanged+=(s,e)=>update.Enabled=CurrentTarget()!=null;
        Render();
    }

    string CurrentTarget(){
        if(item==null)return null;
        if(item.Target!=null)return item.Target;
        return item.CanChoose&&archives.SelectedIndex>0?item.Archives[archives.SelectedIndex-1]:null;
    }

    void Render(){
        item=read(ids[Math.Max(0,mods.SelectedIndex)]);
        name.Text=item.Name;
        const string unavailable="—";
        versions.Text=T("Variante ","Variant ")+item.Variant+"   ·   "+
            T("Instalada ","Installed ")+(item.Installed??unavailable)+"   ·   "+
            T("Publicada ","Published ")+(String.IsNullOrWhiteSpace(item.Published)?unavailable:item.Published);
        details.Text=item.Details;
        archives.Items.Clear();
        bool choose=item.Target==null&&item.CanChoose;
        if(choose){
            archives.Items.Add(T("Selecione o pacote da variante","Select the archive for this variant"));
            foreach(string asset in item.Archives)archives.Items.Add(GeneralGithubLink.Parse(asset).Asset);
            archives.SelectedIndex=0;
        }
        archives.Parent.Visible=choose;
        update.Visible=item.Target!=null||choose;
        update.Enabled=CurrentTarget()!=null;
    }

    async void Recheck(){
        recheck.Enabled=mods.Enabled=archives.Enabled=update.Enabled=false;
        details.Text=T("Conferindo versões, pacotes e registros da instalação…","Checking versions, archives and installation records…");
        try{
            await refresh();
            if(!IsDisposed)Render();
        }catch(Exception ex){
            if(!IsDisposed)details.Text=T("A verificação falhou. ","The check failed. ")+ex.Message;
        }finally{
            if(!IsDisposed){recheck.Enabled=mods.Enabled=archives.Enabled=true;}
        }
    }
}

internal sealed class NexusKeyTrustDialog : GeneralDialog {
    static string T(string pt,string en){return GeneralStyle.T(pt,en);}

    internal NexusKeyTrustDialog():base(T("Confirmação antes de abrir o Nexus","Confirm before opening Nexus")){
        Name="NexusKeyTrust";
        ClientSize=new Size(690,420);
        var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(24,10,24,18)};
        body.RowStyles.Add(new RowStyle(SizeType.Absolute,134));
        body.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute,36));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute,68));
        var warning=new GeneralSurface{Name="NexusTrustWarning",Dock=DockStyle.Fill,Padding=new Padding(16,10,16,10),
            Margin=new Padding(0,0,0,10),OutlineColor=Color.FromArgb(224,72,82)};
        warning.Controls.Add(new Label{Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Text=T(
            "Aviso do Nexus Mods\nNunca insira sua chave pessoal de API em um aplicativo em que você não confia. Um aplicativo não registrado que pede essa chave pode ser malicioso. A chave dá ao aplicativo acesso limitado à sua conta e às suas informações pessoais no Nexus Mods.",
            "Warning from Nexus Mods\nDo not enter your personal API key into an application you do not trust. If an unregistered application asks for your personal API key it may be malicious. Your API key grants the application limited access to your Nexus Mods account and personal information.")});
        body.Controls.Add(warning,0,0);
        body.Controls.Add(new Label{Dock=DockStyle.Fill,Margin=new Padding(0,4,0,0),Text=T(
            "O Mod Loader usa a chave para consultar versões e atualizações dos seus mods. A chave fica criptografada neste PC e é enviada apenas ao Nexus Mods para autenticar essas consultas.",
            "Mod Loader uses the key to check your mods for versions and updates. The key is stored encrypted on this PC and is only sent to Nexus Mods to authenticate those requests.")},0,1);
        body.Controls.Add(new Label{Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Margin=Padding.Empty,Text=T(
            "Você confia no Mod Loader e quer abrir o Nexus Mods?",
            "Do you trust Mod Loader and want to open Nexus Mods?")},0,2);
        var actions=GeneralTableDialogLayout.Commands();
        var yes=GeneralStyle.Button(T("Sim, abrir Nexus","Yes, open Nexus"),()=>Complete(DialogResult.Yes),true);
        var no=GeneralStyle.Button(T("Não","No"),()=>Complete(DialogResult.No));
        yes.Name="TrustNexusYes";no.Name="TrustNexusNo";
        actions.Controls.Add(yes);actions.Controls.Add(no);
        body.Controls.Add(actions,0,3);Content.Controls.Add(body);
        CancelButton=no;AcceptButton=no;
        GeneralStyle.Apply(this,GeneralStyle.Dark);
        Shown+=(s,e)=>no.Select();
    }
}

internal sealed class GeneralNexusConnectionDialog : GeneralDialog {
    internal const string AccessPage="https://next.nexusmods.com/settings/api-keys#:~:text=Personal%20API%20Key";
    internal string PersonalKey {get;private set;}
    static string T(string pt,string en){return GeneralStyle.T(pt,en);}

    internal GeneralNexusConnectionDialog(Action<string> open,Func<string,Task<NexusKeyCheck>> verify)
        :base(T("Conectar ao Nexus","Connect to Nexus")){
        Name="NexusConnection";
        ClientSize=new Size(680,382);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new Padding(24,12,24,18)};
        foreach(int height in new[]{70,54,24,44})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,60));
        var instructions=new Label{Dock=DockStyle.Fill,Margin=Padding.Empty,Text=T(
            "Em Personal API Key, gere uma chave se precisar e copie o valor.\nSe aparecer a lista Integrations, role até o fim da página.\nVolte aqui, clique em Colar chave e depois em Conectar.",
            "In Personal API Key, generate a key if needed and copy its value.\nIf you see the Integrations list, scroll to the end of the page.\nReturn here, click Paste key, then Connect.")};
        layout.Controls.Add(instructions,0,0);
        var feedback=new Label{Name="NexusConnectionFeedback",Dock=DockStyle.Fill,Margin=new Padding(0,8,0,0)};
        var browse=GeneralStyle.Button(T("Abrir seção da chave pessoal","Open personal key section"),()=>{
            using(var warning=new NexusKeyTrustDialog()){
                if(warning.ShowDialog(this)!=DialogResult.Yes)return;
            }
            try{open(AccessPage);}
            catch(Exception){feedback.Text=T("Não foi possível abrir a página do Nexus.","Could not open the Nexus page.");}
        });
        browse.Name="OpenNexusApiAccess";browse.Anchor=AnchorStyles.Left;browse.Margin=Padding.Empty;
        layout.Controls.Add(browse,0,1);
        string label=T("Chave pessoal","Personal API key");
        layout.Controls.Add(new Label{Text=label,Dock=DockStyle.Fill,Margin=Padding.Empty},0,2);
        var key=new TextBox{Name="NexusPersonalKey",AccessibleName=label,MaxLength=256,UseSystemPasswordChar=true,Dock=DockStyle.Fill};
        var fieldRow=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};
        fieldRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        fieldRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fieldRow.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        fieldRow.Controls.Add(GeneralTableDialogLayout.Input(key),0,0);
        var paste=GeneralStyle.Button(T("Colar chave","Paste key"),()=>{
            try{key.SelectAll();key.Paste();key.Focus();}
            catch(System.Runtime.InteropServices.ExternalException){
                feedback.Text=T("Não foi possível colar. Tente Ctrl+V no campo.","Could not paste. Try Ctrl+V in the field.");
            }
        });
        paste.Name="PasteNexusKey";paste.Margin=new Padding(8,0,0,0);paste.Anchor=AnchorStyles.Left;
        paste.Font=GeneralStyle.Font(12);
        paste.Size=new Size(paste.GetPreferredSize(Size.Empty).Width,44);
        paste.AutoSize=false;
        fieldRow.Controls.Add(paste,1,0);
        layout.Controls.Add(fieldRow,0,3);
        layout.Controls.Add(feedback,0,4);
        var actions=GeneralTableDialogLayout.Commands();
        var cancel=GeneralStyle.Button(T("Cancelar","Cancel"),()=>Complete(DialogResult.Cancel));
        var connect=GeneralStyle.Button(T("Conectar","Connect"),()=>{},true);
        connect.Name="ValidateNexusKey";connect.Enabled=false;
        key.TextChanged+=(s,e)=>connect.Enabled=NexusCredentials.Valid(key.Text.Trim());
        connect.Click+=async(s,e)=>{
            string value=key.Text.Trim();
            if(!NexusCredentials.Valid(value))return;
            key.Enabled=connect.Enabled=paste.Enabled=false;
            feedback.Text=T("Verificando a chave…","Checking the key…");
            NexusKeyCheck result;
            try{result=await verify(value);}
            catch(Exception){result=NexusKeyCheck.Unavailable;}
            if(IsDisposed||!Visible)return;
            if(result==NexusKeyCheck.Accepted){PersonalKey=value;Complete(DialogResult.OK);return;}
            feedback.Text=result==NexusKeyCheck.Rejected?
                T("O Nexus recusou a chave. Confira se você copiou a Personal API Key.","Nexus rejected the key. Check that you copied the Personal API Key."):
                T("O Nexus não respondeu. Tente novamente.","Nexus did not respond. Try again.");
            key.Enabled=connect.Enabled=paste.Enabled=true;key.Focus();
        };
        actions.Controls.Add(connect);actions.Controls.Add(cancel);
        layout.Controls.Add(actions,0,5);Content.Controls.Add(layout);
        AcceptButton=connect;CancelButton=cancel;
        GeneralStyle.Apply(this,GeneralStyle.Dark);key.BorderStyle=BorderStyle.None;
    }
}
