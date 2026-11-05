using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class LocalImportWindow : Form {
    internal LocalImportWindow(ModImportItem item,string root,bool queued) {
        Text="Instalar arquivo importado";
        Font=new Font("Segoe UI",10);
        ClientSize=new Size(580,205);
        MinimumSize=new Size(550,245);
        StartPosition=FormStartPosition.CenterParent;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(16),ColumnCount=1,RowCount=4};
        Controls.Add(layout);
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label{Text=item.Name,AutoSize=true,MaximumSize=new Size(530,0),Font=new Font(Font,FontStyle.Bold)},0,0);
        layout.Controls.Add(new Label{Text="Destino: "+root,AutoSize=true,MaximumSize=new Size(530,0),Margin=new Padding(3,8,3,0)},0,1);
        layout.Controls.Add(new Label{Dock=DockStyle.Fill,Text=queued?
            "O arquivo entra antes dos próximos itens da lista assim que a operação em andamento terminar.":
            "O Mod Loader confere as pastas do pacote e faz backup dos saves antes de instalar. Use um arquivo de uma fonte em que você confia."},0,2);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,FlowDirection=FlowDirection.RightToLeft,WrapContents=false};
        var install=new Button{Name="InstallImported",Text="Instalar",DialogResult=DialogResult.OK,AutoSize=true,MinimumSize=new Size(110,34),Enabled=!String.IsNullOrWhiteSpace(root)};
        var close=new Button{Text="Cancelar",DialogResult=DialogResult.Cancel,AutoSize=true,MinimumSize=new Size(110,34)};
        buttons.Controls.Add(install);
        buttons.Controls.Add(close);
        layout.Controls.Add(buttons,0,3);
        AcceptButton=install;
        CancelButton=close;
    }
}

public sealed partial class OnlineWindow {
    sealed class LocalInstallRequest {
        internal ModImportItem Item;
        internal string Root;
    }

    readonly Queue<LocalInstallRequest> priorityImports=new Queue<LocalInstallRequest>();
    readonly List<string> deferredDrops=new List<string>();
    bool processingLocalImport;

    ModImportItem PendingAt(int index) {
        ModImportItem item;
        return index>=0 && index<grid.Rows.Count &&
            pending.TryGetValue(Convert.ToString(grid.Rows[index].Tag),out item) ? item : null;
    }

    List<ModImportItem> SelectedImports() {
        grid.EndEdit();
        return grid.Rows.Cast<DataGridViewRow>().Where(r=>r.Visible&&Convert.ToBoolean(r.Cells["Select"].Value))
            .Select(r=>PendingAt(r.Index)).Where(item=>item!=null).ToList();
    }

    void ShowImportedInstall(ModImportItem item) {
        string root=game.Text.Trim();
        using(var window=new LocalImportWindow(item,root,busy||automaticFlow||automaticQueue.Count>0)) {
            if(window.ShowDialog(this)!=DialogResult.OK)return;
        }
        try {
            QueueImportedInstall(item,root);
            var ignored=ProcessPriorityImports(false);
        } catch(Exception ex) { status.Text=Friendly(ex); }
    }

    void QueueImportedInstall(ModImportItem item,string root) {
        string target=Path.GetFullPath(root);
        if(priorityImports.Any(request=>request.Item.Id==item.Id && String.Equals(request.Root,target,StringComparison.OrdinalIgnoreCase)))return;
        priorityImports.Enqueue(new LocalInstallRequest{Item=item,Root=target});
        var row=Row("pending:"+item.Id);
        if(row!=null)row.Cells["State"].Value="Na fila prioritária";
        status.Text=item.Name+" está na fila prioritária.";
        OnlineDiagnostics.Record("import.local.queued",item.Id,"Instalação do arquivo local autorizada.");
    }

    async Task ProcessPriorityImports(bool fromAutomaticQueue) {
        if(busy||processingLocalImport||!fromAutomaticQueue&&(automaticFlow||pollingDownload||pollingFacial)||priorityImports.Count==0)return;
        processingLocalImport=true;
        try {
            while(priorityImports.Count>0) {
                var request=priorityImports.Dequeue();
                var row=Row("pending:"+request.Item.Id);
                string current=null;
                try { current=GameRoot(); } catch(IOException) {} catch(ArgumentException) {} catch(NotSupportedException) {}
                if(!String.Equals(current,request.Root,StringComparison.OrdinalIgnoreCase)) {
                    if(row!=null)row.Cells["State"].Value="Pronto para instalar";
                    status.Text="A pasta do jogo mudou. Confirme novamente a instalação de "+request.Item.Name+".";
                    continue;
                }
                if(row!=null)row.Cells["State"].Value="Instalando";
                bool installed=await Run(()=>createInstaller(request.Root).InstallImported(request.Item,Report));
                row=Row("pending:"+request.Item.Id);
                if(row==null)continue;
                row.Cells["Installed"].Value=installed?"Instalado correto":"Não registrado";
                row.Cells["State"].Value=installed?"Já instalado":"Falhou";
                row.Cells["Progress"].Value=installed?100:0;
                row.Cells["State"].ToolTipText=status.Text;
                if(installed)row.Cells["Select"].Value=false;
            }
        } finally { processingLocalImport=false; }
    }

    async void InstallSelection() {
        var local=SelectedImports();
        var fixedIds=Chosen();
        if(local.Count>0) {
            try {
                string root=GameRoot();
                if(!confirmInstallation("Instalar "+local.Count+" arquivo(s) importado(s) em:\n"+root+"\n\n"+
                    String.Join("\n",local.Select(item=>item.Name))+"\n\nSerá feito backup dos saves. Continuar?"))return;
                foreach(var item in local)QueueImportedInstall(item,root);
                await ProcessPriorityImports(false);
            } catch(Exception ex) { status.Text=Friendly(ex);return; }
        }
        if(fixedIds.Count>0||local.Count==0)Install(fixedIds,true);
    }

    async Task FlushDeferredDrops(bool fromAutomaticQueue) {
        if(busy||!fromAutomaticQueue&&(automaticFlow||pollingDownload||pollingFacial)||deferredDrops.Count==0)return;
        var next=deferredDrops.ToArray();
        deferredDrops.Clear();
        await ImportFiles(next,fromAutomaticQueue);
    }
}
