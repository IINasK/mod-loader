using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

public sealed partial class OnlineWindow {
    const string NormalDownloadGuide="COMO INSTALAR\n1. Para os mods da lista, feche o jogo, confira as pastas, marque os itens e clique em Baixar e instalar.\n2. Quando o site abrir, entre na sua conta do Nexus, se solicitado, e clique em Manual / Slow download.\n3. Downloads concluídos são conferidos e instalados automaticamente. Para desistir do atual, clique em Pular mod atual.\n4. Para um ZIP, RAR ou 7z seu, arraste o arquivo, dê dois cliques na linha e escolha Instalar.";
    const string FacialDownloadGuide="COMO BAIXAR O CONJUNTO FACIAL\n1. Selecione os arquivos e clique em Baixar selecionados uma vez.\n2. No Nexus, entre na sua conta e clique em Manual / Slow download.\n3. Salve os ZIP/RAR/7z originais na pasta monitorada, sem extrair. O app confere, importa e abre o próximo automaticamente.\n4. Página aberta não confirma download. Esta etapa não instala. Para interromper, clique direito > Cancelar fila de downloads.";
    readonly Dictionary<string,FacialDownload> facialItems=new Dictionary<string,FacialDownload>(StringComparer.Ordinal);
    Button mainInstallButton;
    readonly Func<FacialDownload,FixedAsset> facialAssetResolver=item=>item.Asset();
    FacialDownloadQueue facialQueue;
    int facialGeneration;
    bool pollingFacial;
    FacialDownload FacialById(string id){FacialDownload item;return id!=null&&facialItems.TryGetValue(id,out item)?item:null;}
    FacialDownload CurrentFacial(){return grid.CurrentRow==null?null:FacialById((string)grid.CurrentRow.Tag);}
    bool IsSelectable(string id){return EditionEligible(id)&&(FacialById(id)!=null||catalog.Packages.Any(m=>m.Id==id&&!IsBlocked(m)));}
    void AddFacialRows(){
        // Preparation links stay separate from approved recipes and are never passed to the installer.
        if(catalog.Id!="cp25-fixed-20261028")return;
        foreach(var item in FacialDownload.List()){
            if(catalog.Packages.Any(m=>m.Id==item.Id))continue;
            facialItems.Add(item.Id,item);
            rowCategories.Add(item.Id,ModCategory.Appearance);
            int index=grid.Rows.Add(false,"Rosto · "+item.Name,item.Variant,"Somente download","Não iniciado","—","—","Fonte oficial","Conjunto facial17331 · download pelo Nexus; pacote ainda sem receita de instalação aprovada.");
            var row=grid.Rows[index];row.Tag=item.Id;row.DefaultCellStyle.BackColor=Color.FromArgb(238,247,252);
            row.Cells["Mod"].ToolTipText="Conjunto facial17331\n"+item.Url;row.Cells["Installed"].ToolTipText="O monitor confere o pacote pelo hash oficial e guarda no cache. Esta preparação não instala no jogo.";
            row.Cells["Version"].ToolTipText=item.Variant;
        }
    }
    void SelectFacialRows(){
        if (categoryFilter.SelectedIndex != (int)ModCategory.All && categoryFilter.SelectedIndex != (int)ModCategory.Appearance)
            categoryFilter.SelectedIndex = (int)ModCategory.Appearance;
        var ids=new HashSet<string>(FacialDownload.List().Select(item=>item.Id));var rows=grid.Rows.Cast<DataGridViewRow>().Where(r=>r.Visible&&IsSelectable((string)r.Tag)&&ids.Contains((string)r.Tag)).ToList();
        if(rows.Count==0){status.Text="Este catálogo não contém a preparação facial.";return;}
        foreach(DataGridViewRow row in grid.Rows)row.Cells["Select"].Value=row.Visible&&IsSelectable((string)row.Tag)&&ids.Contains((string)row.Tag);
        grid.ClearSelection();grid.CurrentCell=rows[0].Cells["Mod"];grid.FirstDisplayedScrollingRowIndex=rows[0].Index;
        UpdateSelectAll();UpdateSummary();status.Text=rows.Any(r=>FacialById((string)r.Tag)!=null)?"Os 14 arquivos Rosto estão selecionados. Os pacotes sem receita seguem somente para conferência de download.":"Os 14 mods faciais estão selecionados. Baixar e instalar confere os existentes e continua automaticamente; as dependências necessárias entram na mesma fila.";
    }
    bool FacialOpeningAllowed(){
        if(busy||automaticFlow)return false;
        if(automaticQueue.Count>0||pendingDensityId!=null||downloadWatch!=null&&downloadWatch.IsArmed){status.Text="Conclua ou cancele o download atual antes de baixar os arquivos faciais.";OnlineDiagnostics.Record("face-download.waiting-previous-operation",null,"Download facial aguarda operacao anterior, preservada.",new{QueueCount=automaticQueue.Count,Density=pendingDensityId!=null,Watching=downloadWatch!=null&&downloadWatch.IsArmed});return false;}
        return true;
    }
    bool HandleFacialSelection(List<string> ids,bool withDownload,bool reopenSingle){
        var items=ids.Select(FacialById).Where(i=>i!=null).ToList();if(items.Count==0)return false;
        OnlineDiagnostics.Record("face-download.selection",null,"Selecao com arquivos faciais encaminhada somente para download; demais mods preservados.",new{SelectedCount=ids.Count,FacialCount=items.Count,OtherCount=ids.Count-items.Count,WithDownload=withDownload,Install=false});
        if(!withDownload){status.Text="Estes arquivos estão na preparação de downloads. Baixe os originais; a instalação será liberada após conferir os pacotes.";return true;}
        if(!FacialOpeningAllowed())return true;
        StartFacialQueue(items);return true;
    }
    void OpenFacial(FacialDownload item){
        if(!FacialOpeningAllowed())return;StartFacialQueue(new[]{item});
    }
    void OpenFacialOfficial(FacialDownload item){
        try{BrowserRequest(item.Url);launchBrowser(item.Url);status.Text="Página oficial aberta. Esta ação não confirma download nem autoriza instalação.";}
        catch(Exception ex){status.Text=Friendly(ex);}
    }
    void StartFacialQueue(IEnumerable<FacialDownload> items){
        try{
            if(facialQueue!=null&&facialQueue.IsArmed){facialQueue.Resume();UpdateFacialQueueRows();return;}
            string folder=Path.GetFullPath(downloadFolder).TrimEnd('\\');
            if(!String.IsNullOrWhiteSpace(game.Text)){string root=Path.GetFullPath(game.Text).TrimEnd('\\');if(folder.Equals(root,StringComparison.OrdinalIgnoreCase)||folder.StartsWith(root+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("Escolha uma pasta de downloads fora da pasta do jogo.");}
            facialGeneration++;var queue=new FacialDownloadQueue(folder,cache,match=>{importer.RegisterPrepared(match.Asset);});facialQueue=queue;
            queue.Start(items.Select(facialAssetResolver),asset=>DispatchFacialPage(queue,asset));UpdateFacialQueueRows();
        }catch(Exception ex){FacialQueueFailure(ex);}
    }
    void DispatchFacialPage(FacialDownloadQueue queue,FixedAsset asset){
        if(InvokeRequired){Invoke(new Action(()=>DispatchFacialPage(queue,asset)));return;}
        if(facialQueue!=queue||!queue.IsArmed||queue.CurrentAsset.Sha256!=asset.Sha256)return;
        var row=Row(asset.Id);if(row==null)throw new IOException("Arquivo facial não está na lista.");
        try{
            BrowserRequest(asset.Url);launchBrowser(asset.Url);
            row.Cells["State"].Value="Aguardando download";row.Cells["Progress"].Value="—";row.Cells["State"].Style.ForeColor=Color.Black;row.Cells["State"].ToolTipText="Página enviada ao navegador; ainda não comprova recebimento do pacote.";
            if(row.Visible)grid.CurrentCell=row.Cells["Mod"];
            OnlineDiagnostics.Record("face-download.open.dispatched",asset.Id,"Página oficial aberta; monitor aguardando pacote, sem instalação.",new{ModId=asset.ModId,FileId=asset.FileId,Install=false});
        }catch{row.Cells["State"].Value="Falhou";UpdateSummary();throw;}
    }
    void FacialQueueFailure(Exception ex){
        var asset=facialQueue==null?null:facialQueue.CurrentAsset;var row=asset==null?null:Row(asset.Id);
        if(row!=null){row.Cells["State"].Value="Falhou";row.Cells["State"].Style.ForeColor=Color.Firebrick;row.Cells["State"].ToolTipText=Friendly(ex);}
        status.Text=Friendly(ex)+" Clique em Baixar selecionados para tentar novamente ou cancele a fila pelo menu.";UpdateSummary();
        OnlineDiagnostics.Record("face-queue.failed",asset==null?null:asset.Id,"Falha na fila somente de downloads; instalação não autorizada.",new{ErrorType=ex.GetType().Name});
    }
    void UpdateFacialQueueRows(){
        if(facialQueue==null)return;
        foreach(var asset in facialQueue.VerifiedAssets){var row=Row(asset.Id);if(row==null)continue;row.Cells["State"].Value="Baixado e conferido";row.Cells["Progress"].Value=100;row.Cells["Installed"].Value="Somente download";row.Cells["State"].Style.ForeColor=Color.Black;row.Cells["State"].ToolTipText="SHA-256 oficial conferido; pacote guardado no cache. Nenhuma instalação no jogo.";}
        var current=facialQueue.CurrentAsset;if(current!=null){var row=Row(current.Id);if(row!=null&&Convert.ToString(row.Cells["State"].Value)!="Aguardando download"){row.Cells["State"].Value="Conferindo Downloads";row.Cells["Progress"].Value="—";}}
        status.Text=facialQueue.IsArmed?"Arquivos conferidos: "+facialQueue.CompletedCount+". Aguardando download na pasta "+downloadFolder+". Clique em Manual / Slow download no Nexus; o próximo abre automaticamente.":"Downloads selecionados conferidos e guardados. Nenhum arquivo foi instalado no jogo.";
        LoadPending();UpdateSummary();
    }
    async Task CheckFacialDownload(){
        if(pollingFacial||busy||automaticFlow||facialQueue==null||!facialQueue.IsArmed)return;
        pollingFacial=true;var queue=facialQueue;int generation=facialGeneration;Exception failure=null;BrowserDownloadSnapshot progress=null;var activeAsset=queue.CurrentAsset;var activeItem=activeAsset==null?null:FacialById(activeAsset.Id);
        try{
            await Run(()=>{try{var found=queue.Poll((done,total)=>{var current=queue.CurrentAsset;if(current!=null)Post(()=>{if(facialQueue!=queue||generation!=facialGeneration||!queue.IsArmed||queue.CurrentAsset.Id!=current.Id)return;var row=Row(current.Id);if(row!=null){row.Cells["State"].Value="Conferindo arquivo";row.Cells["Progress"].Value=total==0?0:Math.Min(99,(int)(done*100.0/total));}});});if(found==null&&activeItem!=null)progress=BrowserDownloadProgress.Read(downloadFolder,activeAsset,queue.CurrentStartedUtc,activeItem.PublishedSizeKiB);}catch(Exception ex){failure=ex;}return generation==facialGeneration?"Conferindo downloads faciais; nenhuma instalação autorizada.":"Fila de downloads faciais cancelada; arquivos já conferidos preservados.";});
            if(facialQueue!=queue||generation!=facialGeneration||!queue.IsArmed&&queue.CompletedCount==0)return;
            UpdateFacialQueueRows();if(progress!=null&&progress.Active)ShowBrowserProgress(activeAsset.Id,progress);if(failure!=null)FacialQueueFailure(failure);
        }finally{pollingFacial=false;}
    }
    void CancelFacialQueue(){
        facialGeneration++;if(facialQueue==null)return;facialQueue.Cancel();
        Action reset=()=>{foreach(DataGridViewRow row in grid.Rows)if(FacialById((string)row.Tag)!=null&&(Convert.ToString(row.Cells["State"].Value)=="Aguardando download"||Convert.ToString(row.Cells["State"].Value)=="Conferindo arquivo"||Convert.ToString(row.Cells["State"].Value)=="Conferindo Downloads")){row.Cells["State"].Value="Cancelado";row.Cells["Progress"].Value="—";}};
        if(InvokeRequired)Post(reset);else reset();
    }
    bool CanRefreshFacialInventory(int generation,string folder){return !IsDisposed&&!busy&&!automaticFlow&&generation==facialGeneration&&folder==downloadFolder&&automaticQueue.Count==0&&pendingDensityId==null&&(downloadWatch==null||!downloadWatch.IsArmed)&&(facialQueue==null||!facialQueue.IsArmed);}
    async Task RefreshFacialInventory(){
        int generation=facialGeneration;string folder=downloadFolder;
        if(facialItems.Count==0||!CanRefreshFacialInventory(generation,folder))return;
        var available=new Dictionary<string,string>();OnlineDownloadWatch inventory=null;
        bool prepared=await Run(()=>{
            inventory=new OnlineDownloadWatch(folder);var missing=new List<FixedAsset>();
            foreach(var item in facialItems.Values){var asset=facialAssetResolver(item);string file=cache.PathFor(asset);Safe.NoLinks(file);
                if(File.Exists(file)){long size=new FileInfo(file).Length;if(asset.Size==0&&size>0&&size<=8L*1024*1024*1024)asset.Size=size;if(cache.Contains(asset)){available.Add(item.Id,file);continue;}}
                missing.Add(facialAssetResolver(item));
            }
            if(missing.Count>0){inventory.ArmByHash(missing);inventory.Poll();}
            return "Conferindo arquivos faciais já baixados; nenhum download ou instalação iniciado.";
        });
        if(!prepared||!CanRefreshFacialInventory(generation,folder))return;
        if(inventory.IsArmed){await Task.Delay(2100);if(!CanRefreshFacialInventory(generation,folder))return;
            if(!await Run(()=>{DownloadWatchMatch found;while((found=inventory.Poll())!=null)available[found.Asset.Id]=found.Path;return "Arquivos faciais disponíveis: "+available.Count+" de "+facialItems.Count+". Nada foi instalado.";}))return;
        }
        if(!CanRefreshFacialInventory(generation,folder))return;
        foreach(var item in facialItems.Values){var row=Row(item.Id);if(row==null)continue;string source;bool present=available.TryGetValue(item.Id,out source);row.Cells["State"].Value=present?"Baixado e conferido":"Não iniciado";row.Cells["Progress"].Value=present?(object)100:"—";row.Cells["Installed"].Value="Somente download";row.Cells["State"].Style.ForeColor=Color.Black;row.Cells["State"].ToolTipText=present?"SHA-256 oficial conferido: "+source+". Não instalado no jogo.":"Arquivo original ainda não localizado e conferido.";}
        status.Text="Arquivos faciais disponíveis: "+available.Count+" de "+facialItems.Count+". Nada foi instalado.";UpdateSummary();
    }
    void ShowFacialDetails(FacialDownload item){
        var row=Row(item.Id);var info=new ModDetailsInfo{Name=item.Name,PackageVersion=item.Variant,InstalledVersion="Somente download",State=row==null?"Não iniciado":Convert.ToString(row.Cells["State"].Value),GameRoot="Nenhum destino de instalação liberado",OfficialUrl=item.Url,Notes="Arquivo oficial fixado: mod "+item.ModId+" / arquivo "+item.FileId+".\r\n"+(row==null?"":row.Cells["State"].ToolTipText)+"\r\nEsta linha não é receita de instalação; não atualiza RED4ext nem altera corpo, jogo ou saves."};
        using(var details=new ModDetailsWindow(info,url=>OpenFacialOfficial(item)))details.ShowDialog(this);
    }
}
