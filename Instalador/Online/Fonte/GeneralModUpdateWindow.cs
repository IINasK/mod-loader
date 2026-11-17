using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    sealed class ModUpdateRow {
        internal string Key,Name,Source,Expected,Local,LocalStatus;
        internal NexusUpdateResult Online;
    }
    List<ModUpdateRow> modUpdates=new List<ModUpdateRow>();
    NexusUpdateSession modUpdateSession;
    LinkLabel modUpdateNotice;
    int modUpdateGeneration;
    bool checkingModUpdates;
    async void BeginModUpdateCheck(){
        try{await CheckModUpdates();}
        catch(Exception ex){if(!IsDisposed){checkingModUpdates=false;ShowModUpdateNotice();OnlineDiagnostics.Record("mods.update.check",null,ex.GetType().Name);}}
    }
    void AddModUpdateNotice(Control parent){
        modUpdateNotice=new LinkLabel{Name="ModUpdateNotice",AutoSize=true,Font=GeneralStyle.Font(10.5f),
            LinkColor=GeneralStyle.Accent,ActiveLinkColor=GeneralStyle.Foreground,VisitedLinkColor=GeneralStyle.Accent,
            Margin=new Padding(16,0,0,0),Text=T("Versões dos mods","Mod versions")};
        modUpdateNotice.LinkClicked+=(s,e)=>ShowModUpdates();parent.Controls.Add(modUpdateNotice);ShowModUpdateNotice();
    }
    void ShowModUpdateNotice(){
        if(modUpdateNotice==null||modUpdateNotice.IsDisposed)return;
        int available=modUpdates.Count(r=>r.Online!=null&&r.Online.State=="update"&&!LocalAtLeastOnline(r));
        int changed=modUpdates.Count(r=>LocalChanged(r.LocalStatus));
        int unknown=modUpdates.Count(r=>r.Source!=null&&r.Online!=null&&r.Online.State!="update"&&r.Online.State!="current");
        modUpdateNotice.Text=checkingModUpdates?T("Consultando versões…","Checking versions…"):
            String.Format(T("Versões: {0} atualização(ões) · {1} alterado(s)","Versions: {0} update(s) · {1} changed"),available,changed);
        if(!checkingModUpdates&&unknown>0)modUpdateNotice.Text=String.Format(T("Versões: {0} atualização(ões) · {1} sem consulta","Versions: {0} update(s) · {1} unchecked"),available,unknown);
        if(!checkingModUpdates&&modUpdates.Any(r=>r.Online!=null&&r.Online.State=="connect"))modUpdateNotice.Text=T("Conectar ao Nexus · versões","Connect to Nexus · versions");
        tips.SetToolTip(modUpdateNotice,T("Consulta de versões e alterações locais. Clique para revisar. Os arquivos permanecem protegidos pela verificação de conteúdo.",
            "Version queries and local changes. Click to review. Files remain protected by content verification."));
    }
    static bool LocalChanged(string status){return status==GeneralModInstaller.Modified||status=="Diferente/alterado"||status=="Versão diferente"||status==GeneralModInstaller.Partial||status=="Parcial";}
    static bool LocalAtLeastOnline(ModUpdateRow row){
        return LocalChanged(row.LocalStatus)&&row.Online!=null&&row.Online.State=="update"&&ModVersionNumbers.Compare(row.Local,row.Online.Version)>=0;
    }
    static string UpdateCaption(ModUpdateRow row){
        return LocalAtLeastOnline(row)?T("Local igual ou mais nova · conferir","Local same or newer · verify"):
            UpdateState(row.Online)+(row.Online==null||row.Online.Version==null?"":" · "+row.Online.Version);
    }
    async Task CheckModUpdates(){
        if(testMode||IsDisposed||exitApproved)return;
        int generation=++modUpdateGeneration;string destination=state.Destination;
        var captured=state;
        var rows=state.List.Mods.Select(MakeWork).SelectMany(work=>{
            var sources=work.Variant.Sources.Select(GeneralNexusLink.Parse).Where(link=>link!=null).ToArray();
            string localStatus;statuses.TryGetValue(work.Key,out localStatus);
            string expected=work.Variant.CyberpunkId!=null?ProfileRecipe(work).Version:
                work.Variant.SourceInfo.Values.Select(info=>info.Version).FirstOrDefault(value=>!String.IsNullOrWhiteSpace(value));
            return (sources.Length==0?new GeneralNexusLink[]{null}:sources).Select(link=>new ModUpdateRow{
                Key=work.Key,Name=work.Entry.Name,Source=link==null?null:link.Url,Expected=expected,LocalStatus=localStatus});
        }).ToList();
        var knownPaths=new Dictionary<string,List<string>>(StringComparer.Ordinal);
        foreach(var work in state.List.Mods.Select(MakeWork)){
            knownPaths[work.Key]=work.Entry.DetectedPaths.ToList();
            if(work.Variant.CyberpunkId!=null)knownPaths[work.Key].AddRange(ProfileRecipe(work).Files.Select(file=>file.Path));
        }
        checkingModUpdates=true;modUpdates=rows;ShowModUpdateNotice();
        Func<bool> active=()=>!IsDisposed&&!exitApproved&&generation==modUpdateGeneration&&Object.ReferenceEquals(captured,state)&&destination==state.Destination;
        try{
            if(modUpdateSession==null)modUpdateSession=NexusUpdateSession.Session;
            // Start network lookups independently of local disk inspection. Session deduplicates each game/mod.
            var requests=rows.Select(async row=>row.Online=await modUpdateSession.Check(GeneralNexusLink.Parse(row.Source),active)).ToArray();
            if(!String.IsNullOrEmpty(destination)&&Directory.Exists(destination)){
                var paths=await Task.Run(()=>UpdateVersionPaths(destination,knownPaths));
                if(active())foreach(var row in rows){string version;if(paths.TryGetValue(row.Key,out version))row.Local=version;}
            }
            await Task.WhenAll(requests);
        }catch(IOException){foreach(var row in rows.Where(r=>r.Online==null))row.Online=NexusUpdateResult.Unknown("unavailable");}
        catch(UnauthorizedAccessException){foreach(var row in rows.Where(r=>r.Online==null))row.Online=NexusUpdateResult.Unknown("unavailable");}
        finally{if(active()){checkingModUpdates=false;ShowModUpdateNotice();}}
    }
    Dictionary<string,string> UpdateVersionPaths(string root,Dictionary<string,List<string>> paths){
        Action<string,string> add=(key,path)=>{List<string> values;if(!paths.TryGetValue(key,out values))paths[key]=values=new List<string>();values.Add(path);};
        foreach(var file in Generic(root).ProjectDeletionFiles(true))foreach(string owner in file.OwnerKeys)add(WorkKeyFromReceipt(owner),file.Path);
        foreach(var file in Snapshot(root).ProjectDeletionFiles(true,skipUnreadable:true))foreach(string owner in file.OwnerKeys)add(owner,file.Path);
        return paths.ToDictionary(pair=>pair.Key,pair=>ModVersionNumbers.ReadLocal(root,pair.Value),StringComparer.Ordinal);
    }
    static string UpdateState(NexusUpdateResult result){
        switch(result==null?"pending":result.State){
            case "update":return T("Atualização disponível","Update available");
            case "current":return T("Sem sucessor informado","No successor reported");
            case "connect":return T("Conecte sua conta Nexus","Connect your Nexus account");
            case "authentication":return T("Autenticação recusada","Authentication rejected");
            case "rate-limit":return T("Limite da API atingido","API rate limit reached");
            case "ambiguous":return T("Versões ambíguas: revisar","Ambiguous versions: review");
            case "choose-file":return T("Informe o arquivo do Nexus","Select the Nexus file");
            case "no-source":return T("Sem identificação Nexus","No Nexus identity");
            case "pending":return T("Consultando…","Checking…");
            default:return T("Não foi possível consultar","Could not check");
        }
    }
    async void ShowModUpdates(){
        if(IsDisposed)return;
        if(modUpdates.Count==0&&!checkingModUpdates)await CheckModUpdates();
        if(IsDisposed)return;
        using(var dialog=new GeneralDialog(T("Versões dos mods","Mod versions"))){
            dialog.Name="ModVersions";dialog.ClientSize=new Size(960,560);
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20,8,20,20),ColumnCount=1,RowCount=3};
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label{AutoSize=true,MaximumSize=new Size(910,0),Margin=new Padding(0,0,0,12),Text=T(
                "A consulta segue a versão do arquivo e os sucessores indicados pelo autor no Nexus. Sem sucessor informado não garante que todas as variantes estejam atualizadas. Versões locais são lidas sem executar o mod. Nenhum arquivo é substituído aqui.",
                "Queries follow the file version and successors declared by its Nexus author. No reported successor does not guarantee every variant is current. Local versions are read without running the mod. No file is replaced here.")},0,0);
            var table=new DataGridView{Name="ModVersionResults",Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,BorderStyle=BorderStyle.None,
                EnableHeadersVisualStyles=false,BackgroundColor=GeneralStyle.Surface,GridColor=GeneralStyle.Line,ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None,CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersHeight=40,ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing};
            table.DefaultCellStyle=new DataGridViewCellStyle{BackColor=GeneralStyle.Surface,ForeColor=GeneralStyle.Foreground,SelectionBackColor=GeneralStyle.Selection,SelectionForeColor=GeneralStyle.Foreground,Font=GeneralStyle.Font(11),Padding=new Padding(8,0,8,0)};
            table.ColumnHeadersDefaultCellStyle=new DataGridViewCellStyle{BackColor=GeneralStyle.Surface,ForeColor=GeneralStyle.Muted,SelectionBackColor=GeneralStyle.Surface,SelectionForeColor=GeneralStyle.Muted,Font=GeneralStyle.Font(11,true),Padding=new Padding(8,0,8,0)};
            foreach(string title in new[]{T("Mod","Mod"),T("Esperada / local","Expected / local"),T("Arquivos locais","Local files"),"Nexus"})table.Columns.Add(new DataGridViewTextBoxColumn{HeaderText=title,SortMode=DataGridViewColumnSortMode.NotSortable});
            table.RowTemplate.Height=42;
            foreach(var result in modUpdates){
                string local=result.LocalStatus??T("Não conferido","Unchecked");
                if(LocalChanged(result.LocalStatus)){
                    int? order=ModVersionNumbers.Compare(result.Local,result.Expected);
                    local=order>0?T("Versão local mais nova · revisar","Newer local version · review"):T("Conteúdo alterado · revisar","Content changed · review");
                }
                int index=table.Rows.Add(result.Name,(result.Expected??"?")+" / "+(result.Local??"?"),local,UpdateCaption(result));
                table.Rows[index].Tag=result;foreach(DataGridViewCell cell in table.Rows[index].Cells)cell.ToolTipText=Convert.ToString(cell.Value);
            }
            layout.Controls.Add(new GeneralScrollView(table),0,1);
            var commands=GeneralTableDialogLayout.Commands();
            var close=GeneralStyle.Button(T("Fechar","Close"),()=>dialog.Close());commands.Controls.Add(close);dialog.CancelButton=close;
            commands.Controls.Add(GeneralStyle.Button(T("Abrir no Nexus","Open on Nexus"),()=>{
                var row=table.CurrentRow==null?null:table.CurrentRow.Tag as ModUpdateRow;
                if(row!=null&&row.Source!=null)Guard(()=>launch(row.Online!=null&&row.Online.Url!=null?row.Online.Url:row.Source));
            }));
            commands.Controls.Add(GeneralStyle.Button(T("Revisar alteração","Review change"),()=>{
                var row=table.CurrentRow==null?null:table.CurrentRow.Tag as ModUpdateRow;if(row==null||!LocalChanged(row.LocalStatus))return;
                // Keep is the safe existing choice. It never rewrites the expected hashes.
                GeneralVersionChoiceDialog.Ask(dialog,new ModVersionDifference{Name=row.Name,Current=row.Local??T("Versão não identificada","Version unidentified"),Incoming=row.Expected??"?",CanReplace=false,
                    Detail=T("O conteúdo local difere do registro. Manter atual preserva os arquivos. Para substituir, revise a instalação pelo projeto. Manter não transforma arquivos desconhecidos em uma instalação verificada.","Local content differs from its record. Keep current preserves the files. To replace it, review installation from the project. Keeping does not turn unknown files into a verified installation.")});
            }));
            commands.Controls.Add(GeneralStyle.Button(T("Conectar Nexus","Connect Nexus"),()=>{
                ConnectNexus();dialog.Close();
            }));
            layout.Controls.Add(commands,0,2);dialog.Content.Controls.Add(layout);GeneralStyle.Apply(dialog,GeneralStyle.Dark);dialog.ShowDialog(this);
        }
    }
    async void ConnectNexus(){
        try{
            launch("https://www.nexusmods.com/users/myaccount?tab=api%20access");
            using(var dialog=new GeneralNameDialog(T("Conectar ao Nexus","Connect to Nexus"),"",T("Cole sua chave de API. Ela fica protegida pelo Windows neste usuário. Na Dev, use sua chave pessoal de teste.","Paste your API key. Windows protects it for this user. In Dev, use your personal testing key."),T("Chave de API","API key"),256)){
                var input=dialog.Controls.Find("NameInput",true).OfType<TextBox>().First();input.UseSystemPasswordChar=true;
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                NexusCredentials.Save(GeneralAppPaths.Current().Data,dialog.ResultName);modUpdateSession=null;
            }
            await CheckModUpdates();
        }catch(Exception ex){if(!IsDisposed)ShowError(ex);}
    }
}
