using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    sealed class ModUpdateRow {
        internal string Key,Source,Provider,Expected,Local,LocalStatus;
        internal NexusUpdateResult Online;
    }
    List<ModUpdateRow> modUpdates=new List<ModUpdateRow>();
    NexusUpdateSession testModUpdateSession;
    GithubModUpdateSession testGithubUpdateSession;
    ILookup<string,ModUpdateRow> versionsByWork=new ModUpdateRow[0].ToLookup(row=>row.Key,StringComparer.Ordinal);

    TableLayoutPanel versionFooter;
    enum VersionFilter { Checked, Update, Review, Pending }
    VersionFilter? versionFilter;
    readonly LinkLabel[] footerNumbers=new LinkLabel[4];
    static string[] VersionFilterCaptions(){return new[]{T("Consultados","Checked"),T("Atualizar","Update"),T("Revisar","Review"),T("Pendentes","Pending")};}

    int modUpdateGeneration;
    bool checkingModUpdates,connectingNexus;
    async Task RefreshModVersions(bool fresh=false,VersionLookup lookup=null){
        try{await CheckModUpdates(fresh,lookup);}
        catch(Exception ex){if(!IsDisposed)OnlineDiagnostics.Record("mods.update.check",null,ex.GetType().Name);}
    }

    void ClearModVersions(){
        modUpdateGeneration++;
        versionFilter=null;
        checkingModUpdates=false;
        modUpdates.Clear();
        versionsByWork=modUpdates.ToLookup(row=>row.Key,StringComparer.Ordinal);
        ShowModUpdateNotice();
    }

    static bool VersionInstalled(string status){
        return InstalledVersion(status)||GeneralStatusPresentation.Kind(status)=="installed";
    }

    string[] SelectedUpdateLinks(){
        if(marked.Count==0&&updateFocusedId==null)return new string[0];
        var selected=new HashSet<string>(TableSelectionIds()
            .Where(id=>marked.Count>0?marked.Contains(id):id==updateFocusedId),StringComparer.Ordinal);
        return state.List.Mods.Where(mod=>selected.Contains(mod.Id)&&VersionInstalled(ListStatus(mod)))
            .SelectMany(mod=>versionsByWork[MakeWork(mod).Key]).Where(HasSourceUpdate)
            .Select(row=>row.Online.Url).Where(GeneralModDownload.IsWebSource).Distinct(StringComparer.Ordinal).ToArray();
    }

    static bool HasSourceUpdate(ModUpdateRow row){
        return VersionInstalled(row.LocalStatus)&&row.Online!=null&&row.Online.State=="update"&&!LocalAtLeastOnline(row);
    }

    string VersionInStatus(string key,string status){
        if(!VersionInstalled(status)&&status!=UpdateAvailableStatus&&!LocalChanged(status))return null;
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
        cell.Tag=raw;
        cell.Value=GeneralStatusPresentation.Label(raw)+(version==null?"":" · "+version);
        var text=TableStatusDetail(entry,key,status);
        if(version!=null)text+="\n"+T("Versão instalada ","Installed version ")+version;
        var updates=versionsByWork[key].Where(HasSourceUpdate).Select(update=>update.Online.Version)
            .Where(value=>!String.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).ToArray();
        if(updates.Length>0)text+="\n"+T("Versão mais recente na origem ","Newer upstream version ")+String.Join(", ",updates);
        var pending=versionsByWork[key].Where(observation=>observation.Online==null||
            observation.Online.State!="current"&&observation.Online.State!="update")
            .Select(observation=>UpdateState(observation.Online)).Distinct().ToArray();
        if(pending.Length>0)text+="\n"+String.Join(" · ",pending);
        cell.ToolTipText=text;
    }



    sealed class VersionCounts {
        internal int Total,Checked,Ready,Review,Unchecked,Unsupported;
    }

    static bool VersionChecked(IEnumerable<ModUpdateRow> rows){
        return rows.Any()&&rows.All(row=>row.Online!=null&&(row.Online.State=="current"||row.Online.State=="update"));
    }

    VersionCounts CountVersions(bool currentPreset=false){
        var groups=modUpdates.GroupBy(row=>row.Key,StringComparer.Ordinal).ToArray();
        HashSet<string> keys=null;
        if(currentPreset){
            keys=new HashSet<string>(PresetListMods().Select(mod=>MakeWork(mod).Key),StringComparer.Ordinal);
            groups=groups.Where(group=>keys.Contains(group.Key)).ToArray();
        }
        return new VersionCounts{
            Total=groups.Length,
            Checked=groups.Count(VersionChecked),
            Ready=InstallableUpdates().Count(work=>keys==null||keys.Contains(work.Key)),
            Review=modUpdates.Where(HasSourceUpdate).Select(row=>row.Key).Distinct(StringComparer.Ordinal).Count(),
            Unchecked=groups.Count(group=>group.Any(row=>row.Provider!=null&&
                (row.Online==null||row.Online.State!="update"&&row.Online.State!="current"))),
            Unsupported=groups.Count(group=>group.Any(row=>row.Provider==null))
        };
    }

    string ModVersionSummary(){
        var count=CountVersions();
        return String.Format(T("Versões · {0}/{1} consultados · {2} para atualizar · {3} para revisar · {4} sem consulta · {5} sem origem compatível",
            "Versions · {0}/{1} checked · {2} ready to update · {3} to review · {4} unchecked · {5} without a supported source"),
            count.Checked,count.Total,count.Ready,count.Review,count.Unchecked,count.Unsupported);
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
            case VersionFilter.Review:return rows.Any(HasSourceUpdate);
            default:return rows.Any()&&!VersionChecked(rows);
        }
    }

    Control BuildVersionFooter(){
        versionFooter=new TableLayoutPanel{Name="VersionFooter",Dock=DockStyle.Top,ColumnCount=footerNumbers.Length,
            RowCount=1,Height=52,Margin=new Padding(0,1,0,0)};
        versionFooter.RowStyles.Add(new RowStyle(SizeType.Percent,100));
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
            versionFooter.Controls.Add(button,index,0);
        }
        ShowModUpdateNotice();
        return versionFooter;
    }

    void ShowModUpdateNotice(){
        if(versionFooter==null||versionFooter.IsDisposed)return;
        bool show=modUpdates.Count>0&&!checkingModUpdates;

        versionFooter.Visible=show;
        RefreshVersionFilters();

        UpdateFooterAction();
    }

    void RefreshVersionFilters(){
        if(versionFooter==null||versionFooter.IsDisposed||!versionFooter.Visible)return;
        var count=CountVersions(currentPreset:true);
        var captions=VersionFilterCaptions();
        var values=new[]{count.Checked+"/"+count.Total,count.Ready.ToString(),count.Review.ToString(),
            (count.Total-count.Checked).ToString()};
        for(int index=0;index<values.Length;index++){
            var button=footerNumbers[index];
            bool active=versionFilter==(VersionFilter)index;
            button.Text=values[index]+"\n"+captions[index];
            button.AccessibleName=captions[index]+" "+values[index];
            button.AccessibleDescription=active?T("Filtro ativo","Filter active"):T("Filtrar lista de mods","Filter the mod list");
            button.LinkColor=active?GeneralStyle.Foreground:GeneralStyle.Accent;
            button.BackColor=active?GeneralStyle.Selection:GeneralStyle.Background;
        }
    }
    static bool LocalChanged(string status){return status==GeneralModInstaller.Modified||status=="Diferente/alterado"||status=="Versão diferente"||status==GeneralModInstaller.Partial||status=="Parcial";}
    static bool LocalAtLeastOnline(ModUpdateRow row){
        return LocalChanged(row.LocalStatus)&&row.Online!=null&&row.Online.State=="update"&&ModVersionNumbers.Compare(row.Local,row.Online.Version)>=0;
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
        var github=testGithubUpdateSession??(testMode?new GithubModUpdateSession(_=>null):GithubModUpdateSession.Session);
        return new VersionLookup(nexus,github,active);
    }

    async Task CheckModUpdates(bool fresh=false,VersionLookup lookup=null){
        if((testMode&&testModUpdateSession==null)||IsDisposed||exitApproved)return;
        int generation=++modUpdateGeneration;string destination=state.Destination;
        var captured=state;
        var rows=state.List.Mods.Select(MakeWork).SelectMany(work=>{
            var sources=work.Variant.Sources.Where(GeneralModDownload.IsWebSource).Distinct(StringComparer.Ordinal).ToArray();
            string localStatus;statuses.TryGetValue(work.Key,out localStatus);
            string expected=null;
            if(work.Variant.CyberpunkId!=null)expected=ProfileRecipe(work).Version;
            else if(work.Variant.Sources.Count==1){
                GeneralSourceInfo saved;
                if(work.Variant.SourceInfo.TryGetValue(work.Variant.Sources[0],out saved)&&saved.NexusChecked)expected=saved.Version;
            }
            NexusManualMatch manual;
            if(localStatus==GeneralStatusPresentation.NexusManual&&nexusManual.TryGetValue(work.Entry.Id,out manual)&&
                Object.ReferenceEquals(manual.Variant,work.Variant))expected=manual.Version??expected;
            return (sources.Length==0?new string[]{null}:sources).Select(source=>new ModUpdateRow{
                Key=work.Key,Source=source,
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
        try{
            if(lookup==null)lookup=CreateVersionLookup(fresh,active);
            var requests=rows.Select(async row=>row.Online=await lookup.Check(row.Source)).ToArray();
            if(!String.IsNullOrEmpty(destination)&&Directory.Exists(destination)){
                var paths=await Task.Run(()=>UpdateVersionPaths(destination,knownPaths));
                if(active())foreach(var row in rows){string version;if(paths.TryGetValue(row.Key,out version))row.Local=version;}
            }
            await Task.WhenAll(requests);
        }catch(IOException){foreach(var row in rows.Where(r=>r.Online==null))row.Online=NexusUpdateResult.Unknown("unavailable");}
        catch(UnauthorizedAccessException){foreach(var row in rows.Where(r=>r.Online==null))row.Online=NexusUpdateResult.Unknown("unavailable");}
        finally{
            if(active()){
                checkingModUpdates=false;
                versionsByWork=rows.ToLookup(row=>row.Key,StringComparer.Ordinal);
                ShowModUpdateNotice();
                if(grid!=null&&!scanning&&!working)DrawRows();
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
            case "choose-file":return T("Informe o arquivo do Nexus","Select the Nexus file");
            case "no-source":return T("Sem origem compatível para consultar","No supported source to check");

            case "pending":return T("Consultando…","Checking…");
            default:return T("Não foi possível consultar","Could not check");
        }
    }
    async void ConnectNexus(){
        if(connectingNexus||working||scanning||IsDisposed)return;
        connectingNexus=true;UpdateFooterAction();
        try{
            using(var dialog=new GeneralNexusConnectionDialog(launch,GeneralNexusInfo.CheckPersonalKey)){
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                NexusCredentials.Save(GeneralAppPaths.Current().Data,dialog.PersonalKey);
            }
            await RefreshModVersions(true);
        }catch(Exception ex){if(!IsDisposed)ShowError(ex);}
        finally{connectingNexus=false;if(!IsDisposed)UpdateFooterAction();}
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
