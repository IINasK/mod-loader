using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

public sealed class SkipModFailureWindow:Form {
    public SkipModFailureWindow(ModPreparationException failure,bool canSkip,string affectedNames){
        Text="Falha ao preparar "+failure.ModName;Font=new Font("Segoe UI",10);BackColor=Color.White;ClientSize=new Size(720,430);MinimumSize=new Size(580,350);StartPosition=FormStartPosition.CenterParent;ShowInTaskbar=false;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(18),ColumnCount=1,RowCount=3};layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));Controls.Add(layout);
        layout.Controls.Add(new TextBox{Name="FailureDetails",Text=failure.Message,ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.Both,WordWrap=true,Dock=DockStyle.Fill,BackColor=Color.White,AccessibleName="Mod, arquivo e motivo da falha"},0,0);
        layout.Controls.Add(new Label{Text=canSkip?"Pular deixa este mod fora desta tentativa e continua os demais. Itens que dependem dele também ficam de fora:\r\n"+affectedNames:"Esta operação não tem uma fila autorizada para continuar.",AutoSize=true,MaximumSize=new Size(660,0),Margin=new Padding(0,12,0,12)},0,1);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,FlowDirection=FlowDirection.RightToLeft,WrapContents=false};
        var stop=new Button{Name="StopQueue",Text="Parar",AutoSize=true,MinimumSize=new Size(100,36),DialogResult=DialogResult.Cancel};buttons.Controls.Add(stop);
        var skip=new Button{Name="SkipMod",Text="Pular e continuar",AccessibleName="Pular este mod e continuar a fila",AutoSize=true,MinimumSize=new Size(170,36),Enabled=canSkip,DialogResult=DialogResult.Ignore};buttons.Controls.Add(skip);
        layout.Controls.Add(buttons,0,2);CancelButton=stop;AcceptButton=stop;
    }
}

public sealed partial class OnlineWindow {
    readonly Button skipCurrentModButton=new Button{Name="SkipCurrentMod",Text="Pular mod atual",AccessibleName="Pular o mod aguardado e continuar para o próximo",AutoSize=true,MinimumSize=new Size(138,35),Enabled=false};
    readonly HashSet<string> skippedThisQueue=new HashSet<string>(StringComparer.Ordinal);
    bool preparationWasSkipped;
    Func<ModPreparationException,bool> confirmSkipFailure=null;
    bool CanSkipPreparationFailure(ModPreparationException failure){
        if(failure==null||automaticQueue.Count==0||String.IsNullOrWhiteSpace(operationGame)||String.IsNullOrWhiteSpace(automaticGame)||pendingDensityId!=null)return false;
        if(!String.Equals(Path.GetFullPath(operationGame).TrimEnd('\\'),Path.GetFullPath(automaticGame).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))return false;
        try{return automaticQueue.Any(id=>FixedData.Expand(catalog,new[]{id}).Any(m=>m.Id==failure.ModId));}catch(IOException){return false;}
    }
    List<FixedMod> SkipAffectedMods(ModPreparationException failure){
        return SkipAffectedMods(failure.ModId);
    }
    List<FixedMod> SkipAffectedMods(string id){
        return FixedData.Expand(catalog,automaticQueue).Where(m=>FixedData.Expand(catalog,new[]{m.Id}).Any(d=>d.Id==id)).ToList();
    }
    bool SkipPreparationFailure(ModPreparationException failure){
        if(!CanSkipPreparationFailure(failure))return false;
        SkipAuthorizedMod(failure.ModId,failure.ModName,failure.Message);
        preparationWasSkipped=true;return true;
    }
    void SkipAuthorizedMod(string modId,string modName,string reason){
        var affected=SkipAffectedMods(modId);var ids=new HashSet<string>(affected.Select(m=>m.Id),StringComparer.Ordinal);
        automaticQueue.RemoveWhere(id=>ids.Contains(id));StopDownloadWatch();
        foreach(var mod in affected){
            skippedThisQueue.Add(mod.Id);var row=Row(mod.Id);if(row==null)continue;
            row.Cells["Select"].Value=false;row.Cells["State"].Value="Pulado";row.Cells["State"].Style.ForeColor=Color.DarkGoldenrod;
            row.Cells["Progress"].Value="—";
            row.Cells["State"].ToolTipText=mod.Id==modId?reason+"\r\nPulado pelo usuário nesta tentativa.":"Pulado nesta tentativa porque depende de "+modName+". Nenhum arquivo instalado foi removido.";
        }
        UpdateSelectAll();UpdateSummary();status.Text=modName+" foi pulado. Continuando os demais mods da fila.";
        OnlineDiagnostics.Record("queue.mod.skipped",modId,"Usuário pulou o mod e seus dependentes nesta tentativa.",new{Game=automaticGame,Skipped=affected.Select(m=>m.Id).ToArray(),Remaining=automaticQueue.ToArray(),Reason=reason});
    }
    bool CanSkipCurrentDownload(){
        if(busy||automaticFlow||pollingDownload||densityDialogOpen||pendingDensityId!=null||!continueWatchedQueue||downloadWatch==null||!downloadWatch.IsArmed||waitingMod==null||automaticQueue.Count==0||watchedGeneration!=automaticGeneration)return false;
        if(!String.Equals(watchedGame,automaticGame,StringComparison.OrdinalIgnoreCase)||!String.Equals(game.Text.Trim(),automaticGame,StringComparison.OrdinalIgnoreCase))return false;
        return automaticQueue.Any(id=>FixedData.Expand(catalog,new[]{id}).Any(m=>m.Id==waitingMod.Id));
    }
    void UpdateDownloadSkipButton(){skipCurrentModButton.Enabled=CanSkipCurrentDownload();}
    async Task SkipCurrentDownload(){
        if(!CanSkipCurrentDownload())return;
        string root=automaticGame;var mod=waitingMod;
        SkipAuthorizedMod(mod.Id,mod.Name,"Download pulado pelo usuário. Um arquivo concluído depois não retoma esta seleção.");
        automaticGeneration++;UpdateDownloadSkipButton();
        await ProcessAutomaticQueue(root);
    }
    void OfferPreparationSkip(ModPreparationException failure){
        bool canSkip=CanSkipPreparationFailure(failure);bool skip=false;
        if(canSkip&&confirmSkipFailure!=null)skip=confirmSkipFailure(failure);
        else if(!preview){
            var affected=canSkip?SkipAffectedMods(failure):new List<FixedMod>();string names=String.Join(", ",affected.Take(5).Select(m=>m.Name))+(affected.Count>5?"; e mais "+(affected.Count-5)+" item(ns).":"");
            using(var dialog=new SkipModFailureWindow(failure,canSkip,names))skip=dialog.ShowDialog(this)==DialogResult.Ignore;
        }
        if(skip)SkipPreparationFailure(failure);
    }
    string CompleteAutomaticQueue(string message){
        int skipped=skippedThisQueue.Count;ClearAutomaticQueue();
        return skipped==0?message:"Processo concluído com "+skipped+" mod(s) pulado(s). Os demais itens autorizados foram conferidos. Os pulados continuam identificados na lista.";
    }
}
