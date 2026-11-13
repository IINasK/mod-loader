using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    const string InventoryDragFormat="ModLoader.InventoryLink";
    static readonly StringComparer InventoryPaths=StringComparer.OrdinalIgnoreCase;
    string inventoryRoot,inventoryDragKey;
    InventoryLinkData inventoryLinks;
    string inventoryLoadError;
    Func<InventoryLinkSuggestion,DialogResult> testInventoryLinkChoice;

    InventoryLinkData InventoryLinks {
        get {
            if(inventoryLinks!=null&&InventoryPaths.Equals(inventoryRoot,state.Destination))return inventoryLinks;
            inventoryRoot=state.Destination;
            inventoryLoadError=null;
            try {
                inventoryLinks=String.IsNullOrWhiteSpace(inventoryRoot)?new InventoryLinkData():new InventoryLinkStore(data,inventoryRoot).Load();
            }catch(Exception ex){
                if(!(ex is IOException||ex is UnauthorizedAccessException||ex is ArgumentException||ex is InvalidOperationException))throw;
                inventoryLoadError=ex.Message;inventoryLinks=new InventoryLinkData();
            }
            return inventoryLinks;
        }
    }

    IEnumerable<P2PSyncDifference> DisplayedInventory(){
        var recognized=new HashSet<string>(nexusManual.Values.Where(match=>match.Files!=null)
            .SelectMany(match=>match.Files.Keys),InventoryPaths);
        return InventoryLinkStore.View(gameInventory,InventoryLinks,state.List.Mods)
            .Where(file=>file.Kind!="found"||file.InventoryGroupId!=null||!recognized.Contains(file.Path));
    }

    void UpdateInventoryLinks(Action<InventoryLinkData> update){
        var current=InventoryLinks;
        if(inventoryLoadError!=null)throw new IOException(inventoryLoadError);
        var copy=Safe.Json.Deserialize<InventoryLinkData>(Safe.Json.Serialize(current));
        update(copy);
        new InventoryLinkStore(data,state.Destination).Save(copy);
        inventoryLinks=copy;
        DrawRows();
    }

    void OpenInventoryTree(string selectedPath=null,IWin32Window owner=null){
        if(!CanEditInventory)return;
        var files=DisplayedInventory().Where(file=>file.Kind!="verified-variant").ToArray();
        using(var dialog=new GeneralInventoryTree(state.Destination,files,selectedPath))dialog.ShowDialog(owner??this);
    }

    P2PSyncDifference FocusedInventoryFile(){
        return grid.CurrentRow==null?null:grid.CurrentRow.Tag as P2PSyncDifference;
    }

    void ShowInventoryDetails(P2PSyncDifference item){
        if(item==null||!CanEditInventory)return;
        var files=DisplayedInventory().Where(file=>file.Kind=="found"&&
            InventoryPaths.Equals(InventoryLinkStore.Key(file),InventoryLinkStore.Key(item))).ToArray();
        using(var dialog=new GeneralSyncDifferencesDialog(files,true,
            (owner,file)=>OpenInventoryTree(file.Path,owner),
            file=>Guard(()=>GeneralInventoryTree.RevealInExplorer(state.Destination,file.Path)))){
            dialog.ShowDialog(this);
        }
    }

    P2PSyncDifference[] InventoryRow(string key){
        return DisplayedInventory().Where(file=>file.Kind=="found"&&!file.LocalInModFolder&&InventoryPaths.Equals(InventoryLinkStore.Key(file),key)).ToArray();
    }

    bool CanEditInventory {get{return !working&&!scanning&&associationChoice==null&&!String.IsNullOrWhiteSpace(state.Destination);}}
    P2PSyncDifference InventoryRowAt(int screenX,int screenY){
        var point=grid.PointToClient(new Point(screenX,screenY));
        int index=grid.HitTest(point.X,point.Y).RowIndex;
        return index<0?null:grid.Rows[index].Tag as P2PSyncDifference;
    }

    void EnableInventoryDrag(DataGridView table){
        string key=null;
        Point down=Point.Empty;
        table.MouseDown+=(s,e)=>{
            key=null;
            int index=table.HitTest(e.X,e.Y).RowIndex;
            if(e.Button!=MouseButtons.Left||!CanEditInventory||index<0)return;
            var item=table.Rows[index].Tag as P2PSyncDifference;
            if(item==null||item.Kind!="found"||item.VerifiedZip!=null||item.ProvenOwner!=null||item.LocalInModFolder)return;
            key=InventoryLinkStore.Key(item);down=e.Location;
        };
        table.MouseUp+=(s,e)=>key=null;
        table.MouseMove+=(s,e)=>{
            if(key==null||e.Button!=MouseButtons.Left||!CanEditInventory)return;
            var size=SystemInformation.DragSize;
            if(new Rectangle(down.X-size.Width/2,down.Y-size.Height/2,size.Width,size.Height).Contains(e.Location))return;
            inventoryDragKey=key;key=null;
            try{table.DoDragDrop(new DataObject(InventoryDragFormat,inventoryDragKey),DragDropEffects.Move);}
            finally{inventoryDragKey=null;}
        };
    }

    DragDropEffects InventoryDropEffect(Control target,DragEventArgs e) {
        if(target!=grid||!CanEditInventory||inventoryDragKey==null||
            !InventoryPaths.Equals(e.Data.GetData(InventoryDragFormat) as string,inventoryDragKey))return DragDropEffects.None;
        var item=InventoryRowAt(e.X,e.Y);
        return item!=null&&item.Kind=="found"&&item.VerifiedZip==null&&item.ProvenOwner==null&&!item.LocalInModFolder&&!InventoryPaths.Equals(InventoryLinkStore.Key(item),inventoryDragKey)
            ?DragDropEffects.Move:DragDropEffects.None;
    }

    void MergeInventoryRows(string from,string into) {
        if(!CanEditInventory||InventoryPaths.Equals(from,into))return;
        var source=InventoryRow(from);
        var target=InventoryRow(into);
        if(source.Length==0||target.Length==0)return;
        UpdateInventoryLinks(links=>{
            var previous=links.Groups.FirstOrDefault(group=>group.Id==from);
            var primary=links.Groups.FirstOrDefault(group=>group.Id==into);
            InventoryLinkStore.Combine(links,into,target[0].Mod??target[0].Path,null,
                source.Concat(target).Select(file=>file.Path).Concat(previous==null?Enumerable.Empty<string>():previous.Paths),
                primary==null||primary.Source==null?(previous==null?null:previous.Source):primary.Source);
        });
        detail.Text=T("Arquivos agrupados na lista.","Files grouped in the list.");
    }

    InventoryLinkGroup SelectedInventoryLink(){
        var item=grid.CurrentRow==null?null:grid.CurrentRow.Tag as P2PSyncDifference;
        var mod=FocusedEntry();
        return InventoryLinks.Groups.FirstOrDefault(group=>item!=null&&group.Id==item.InventoryGroupId||
            mod!=null&&group.CatalogId==mod.Id);
    }

    async void DropInventorySource(P2PSyncDifference item,string source){
        try{await SaveInventorySource(InventoryLinkStore.Key(item),source);}
        catch(Exception ex){if(!IsDisposed)ShowError(ex);}
    }

    async void SetInventorySource(P2PSyncDifference item) {
        if(item==null||item.Kind!="found"||!CanEditInventory)return;
        try {
            string key=InventoryLinkStore.Key(item);
            var previous=InventoryLinks.Groups.FirstOrDefault(group=>group.Id==key);
            using(var dialog=new GeneralNameDialog(T("Vincular link do Nexus","Link Nexus URL"),previous==null?null:previous.Source,
                fieldLabel:T("Link do mod ou arquivo","Mod or file URL"),maxLength:512)){
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                await SaveInventorySource(key,dialog.ResultName);
            }
        }catch(Exception ex){if(!IsDisposed)ShowError(ex);}
    }

    async Task SaveInventorySource(string key,string value) {
        if(!CanEditInventory)return;
        var link=GeneralNexusLink.Parse(value);
        if(link==null)throw new IOException(T("Cole um link de mod do Nexus.","Paste a Nexus mod link."));
        var files=InventoryRow(key);
        if(files.Length==0||files.Any(file=>file.VerifiedZip!=null||file.ProvenOwner!=null))return;
        string id=null;
        UpdateInventoryLinks(links=>{
            var group=InventoryLinkStore.Combine(links,key,files[0].Mod??files[0].Path,null,files.Select(file=>file.Path),link.Url);
            id=group.Id;
        });
        if(!NexusPreviewsAvailable){
            detail.Text=T("Link salvo. A consulta precisa da API do Nexus no Dev.","Link saved. Looking it up requires the Nexus API in Dev.");
            return;
        }
        await ReviewInventoryLinks(id);
    }

    async void VerifyInventoryZip() {
        if(!CanEditInventory||inventoryLoadError!=null)return;
        using(var dialog=new OpenFileDialog{Title=T("Escolher ZIP original do mod","Choose original mod ZIP"),
            Filter="ZIP (*.zip)|*.zip",InitialDirectory=state.DownloadFolder,CheckFileExists=true}){
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            using(var name=new GeneralNameDialog(T("Nome do pacote","Package name"),Path.GetFileNameWithoutExtension(dialog.FileName),
                fieldLabel:T("Nome do mod","Mod name"),maxLength:300)){
                if(name.ShowDialog(this)!=DialogResult.OK)return;
                try{
                    int count=await VerifyInventoryZip(dialog.FileName,name.ResultName);
                    detail.Text=count==0?T("Nenhum arquivo instalado bate com o ZIP.","No installed files match the ZIP."):
                        T("Arquivos com origem confirmada: ","Files with verified origin: ")+count;
                }catch(Exception ex){if(!IsDisposed)ShowError(ex);}
            }
        }
    }

    async Task<int> VerifyInventoryZip(string zip,string name) {
        string root=state.Destination;
        var candidates=gameInventory.Where(file=>file.Kind=="found"||file.LocalInModFolder).Select(file=>file.Path).ToArray();
        scanning=true;Busy(true);
        detail.Text=T("Conferindo conteúdo do ZIP…","Verifying ZIP contents…");
        Dictionary<string,InventoryZipLabel> matches;
        try{matches=await Task.Run(()=>InventoryLinkStore.MatchZip(root,zip,name,IsCyberpunk(root),candidates));}
        finally{scanning=false;if(!IsDisposed)Busy(false);}
        if(IsDisposed||!InventoryPaths.Equals(root,state.Destination)||matches.Count==0)return 0;
        UpdateInventoryLinks(links=>{
            foreach(var item in matches)links.ZipLabels[item.Key]=item.Value;
        });
        await Scan();
        return matches.Count;
    }

    bool NexusPreviewsAvailable {get{return testNexusPreviews!=null||!testMode&&GeneralAppPaths.IsDevelopment&&GeneralNexusInfo.HasTestKey;}}

    InventoryPreviewOwner[] InventoryPreviewOwners(string onlyGroup,P2PSyncDifference[] inventory) {
        var owners=new List<InventoryPreviewOwner>();
        foreach(var group in InventoryLinks.Groups.Where(item=>item.CatalogId==null&&item.Source!=null&&(onlyGroup==null||item.Id==onlyGroup))){
            var link=GeneralNexusLink.Parse(group.Source);
            var anchors=inventory.Where(file=>file.Kind=="found"&&file.InventoryGroupId==group.Id).Select(file=>file.Path).ToArray();
            if(link!=null&&anchors.Length>0)
                owners.Add(new InventoryPreviewOwner{Id=group.Id,Name=group.Name,RowId=group.Id,Anchors=anchors,Link=link,Variant=new GeneralModVariant()});
        }
        if(onlyGroup==null)foreach(var mod in state.List.Mods)foreach(var variant in mod.Variants){
            var sources=variant.Sources.Select(GeneralNexusLink.Parse).Where(link=>link!=null).ToList();
            var recipe=catalog.Packages.FirstOrDefault(item=>item.Id==variant.CyberpunkId);
            if(recipe!=null){
                if(recipe.NexusModId>0)sources.Add(new GeneralNexusLink{Game="cyberpunk2077",Mod=recipe.NexusModId,File=recipe.NexusFileId});
                foreach(var asset in NightCityDensity.Assets(recipe)){
                    var link=GeneralNexusLink.Parse(asset.Url);
                    if(link!=null)sources.Add(link);
                    else if(asset.ModId>0)sources.Add(new GeneralNexusLink{Game="cyberpunk2077",Mod=asset.ModId,File=asset.FileId});
                }
            }
            foreach(var link in sources.GroupBy(item=>item.Url,StringComparer.Ordinal).Select(group=>group.First()))
                owners.Add(new InventoryPreviewOwner{Id=mod.Id,Name=mod.Name,CatalogId=mod.Id,Link=link,Variant=variant});
        }
        return owners.Where(owner=>!InventoryLinks.Ignored.Contains(owner.IgnoreKey,StringComparer.Ordinal)).ToArray();
    }

    async Task ReviewInventoryLinks(string onlyGroup=null){
        if(!CanEditInventory||!NexusPreviewsAvailable)return;
        var inventory=DisplayedInventory().ToArray();
        if(!inventory.Any(file=>file.Kind=="found"))return;
        var owners=InventoryPreviewOwners(onlyGroup,inventory);
        if(owners.Length==0)return;
        string root=state.Destination;
        bool cyberpunk=IsCyberpunk(root);
        string previous=detail.Text;
        var read=nexusScan.PreviewSession(testNexusPreviews??GeneralNexusInfo.ReadFilePreviews);
        InventoryLinkSuggestion[] proposals;
        scanning=true;Busy(true);detail.Text=T("Comparando caminhos de mods com o Nexus…","Comparing mod paths with Nexus…");
        try{proposals=await Task.Run(()=>InventoryLinkSuggestions.Find(root,cyberpunk,owners,inventory,read));}
        finally{scanning=false;Busy(false);detail.Text=previous;}
        if(IsDisposed||!InventoryPaths.Equals(root,state.Destination))return;
        foreach(var proposal in proposals){
            if(InventoryLinks.Ignored.Contains(proposal.Target.IgnoreKey,StringComparer.Ordinal)||InventoryRow(proposal.RowId).Length==0||
                proposal.Target.RowId!=null&&InventoryRow(proposal.Target.RowId).Length==0)continue;
            var choice=testInventoryLinkChoice==null?AskInventoryLink(proposal):testInventoryLinkChoice(proposal);
            if(choice==DialogResult.Ignore)UpdateInventoryLinks(links=>links.Ignored.Add(proposal.Target.IgnoreKey));
            if(choice!=DialogResult.Yes)continue;
            if(proposal.Target.RowId!=null){MergeInventoryRows(proposal.RowId,proposal.Target.RowId);continue;}
            UpdateInventoryLinks(links=>InventoryLinkStore.Combine(links,null,proposal.Target.Name,
                proposal.Target.CatalogId,proposal.Paths));
        }
        if(onlyGroup!=null&&proposals.Length==0)
            detail.Text=T("Link salvo. Nenhum outro item da lista coincidiu.","Link saved. No other listed item matched.");
    }

    DialogResult AskInventoryLink(InventoryLinkSuggestion proposal){
        using(var dialog=new GeneralDialog(T("Agrupar arquivos detectados","Group detected files")){ClientSize=new Size(630,294)}){
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new Padding(20,8,20,20)};
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var message=new TextBox{Text=proposal.Target.Name+"  +  "+proposal.Name+"\r\n\r\n"+
                T("Os caminhos coincidem com a prévia do Nexus. Juntar na visualização?","The paths match the Nexus preview. Group them in the list?"),
                ReadOnly=true,Multiline=true,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,Font=GeneralStyle.Font(12)};
            layout.Controls.Add(new GeneralScrollView(message),0,0);
            var buttons=GeneralTableDialogLayout.Commands();
            var yes=GeneralStyle.Button(T("Sim","Yes"),()=>dialog.Complete(DialogResult.Yes),true);
            var no=GeneralStyle.Button(T("Não","No"),()=>dialog.Complete(DialogResult.No));
            var never=GeneralStyle.Button(T("Não, nunca mais para este mod","No, never for this mod"),()=>dialog.Complete(DialogResult.Ignore));
            buttons.Controls.Add(yes);buttons.Controls.Add(no);buttons.Controls.Add(never);
            dialog.AcceptButton=no;dialog.CancelButton=no;
            layout.Controls.Add(buttons,0,1);dialog.Content.Controls.Add(layout);
            GeneralStyle.Apply(dialog,GeneralStyle.Dark);message.BorderStyle=BorderStyle.None;
            return dialog.ShowDialog(this);
        }
    }
}
