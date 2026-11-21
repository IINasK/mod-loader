using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

public sealed class ModVersionDifference {
    public string Name,Current,Incoming,Detail;
    public bool CanReplace=true;
    internal string ReplaceLabel;
    internal bool AllowSkip,SkipSelected;

    internal bool Skip(Func<ModVersionDifference,bool> choose){
        bool replace=choose!=null&&choose(this);
        if(SkipSelected&&AllowSkip)return true;
        if(!replace)throw new ModVersionKeptException(Name);
        if(!CanReplace)throw new IOException(Detail);
        return false;
    }
    internal static string ReplacementDetail(System.Collections.Generic.IEnumerable<string> paths){
        var files=paths.ToArray();
        return GeneralStyle.T("Substituir pode causar erros se a versão for incompatível. O Mod Loader guarda os arquivos substituídos para desfazer. A troca usa os caminhos dos arquivos do mod.","Replacement can cause errors if the version is incompatible. Mod Loader backs up replaced files for undo. The replacement uses the mod's file paths.")
            +"\r\n\r\n"+String.Join("\r\n",files.Take(6))+(files.Length>6?"\r\n"+GeneralStyle.T("Mais ","And ")+(files.Length-6)+GeneralStyle.T(" arquivo(s)."," more file(s)."):"");
    }
    internal static string FileVersion(string path){
        string ext=Path.GetExtension(path).ToLowerInvariant();if(ext!=".dll"&&ext!=".exe"&&ext!=".asi")return GeneralStyle.T("Conteúdo diferente; versão não identificada","Different content; version unidentified");
        var v=FileVersionInfo.GetVersionInfo(path);return String.IsNullOrWhiteSpace(v.FileVersion)?GeneralStyle.T("Versão não identificada","Version unidentified"):v.FileVersion.Trim();
    }
    internal void Require(Func<ModVersionDifference,bool> choose){
        if(choose==null)throw new IOException(Name+": "+GeneralStyle.T("versão/conteúdo diferente. Escolha qual manter antes de instalar.","different version/content. Choose which to keep before installing."));
        bool replace=choose(this);OnlineDiagnostics.Record("version.choice",null,replace?"Versão do pacote escolhida.":"Instalação atual mantida.",new{Name=Name,Current=Current,Incoming=Incoming,Replace=replace,CanReplace=CanReplace});
        if(!replace)throw new ModVersionKeptException(Name);
        if(!CanReplace)throw new IOException(Detail);
    }
}
public sealed class ModVersionKeptException : IOException {
    public ModVersionKeptException(string name):base(name+GeneralStyle.T(": instalação atual preservada; este item não foi instalado.",": existing installation preserved; this item was not installed.")){}
}
internal sealed class GeneralReceivedUpdateDialog : GeneralDialog {
    readonly CheckBox backup;
    GeneralReceivedUpdateDialog(int count):base(GeneralStyle.T("Atualizar mods recebidos","Update received mods")){
        RequiresDecision=true;Name="ReceivedUpdateChoice";ClientSize=new Size(540,250);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(24,8,24,24)};
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var text=GeneralModEditor.LabelFor(count==1?
            GeneralStyle.T("1 mod está desatualizado. Deseja atualizá-lo?","1 mod is outdated. Update it?"):
            count+GeneralStyle.T(" mods estão desatualizados. Deseja atualizá-los?"," mods are outdated. Update them?"));
        layout.Controls.Add(text,0,0);
        backup=new CheckBox{Name="BackupPreviousMods",Text=GeneralStyle.T("Criar backup","Create backup"),
            Checked=true,AutoSize=true,MinimumSize=new Size(0,32),UseMnemonic=false};
        layout.Controls.Add(backup,0,1);
        var actions=GeneralTableDialogLayout.Commands();
        var update=GeneralStyle.Button(GeneralStyle.T("Atualizar","Update"),()=>Complete(DialogResult.Yes),true);
        var keep=GeneralStyle.Button(GeneralStyle.T("Manter atual","Keep current"),()=>Complete(DialogResult.No));
        actions.Controls.Add(update);actions.Controls.Add(keep);
        layout.Controls.Add(actions,0,2);Content.Controls.Add(layout);
        AcceptButton=CancelButton=keep;
        GeneralStyle.Apply(this,GeneralStyle.Dark);
    }

    internal static bool? Ask(Control owner,int count){
        if(owner==null||owner.IsDisposed||!owner.IsHandleCreated)return null;
        if(owner.InvokeRequired)return (bool?)owner.Invoke((Func<bool?>)(()=>Ask(owner,count)));
        using(var dialog=new GeneralReceivedUpdateDialog(count))
            return dialog.ShowDialog(owner)==DialogResult.Yes?(bool?)dialog.backup.Checked:null;
    }
}

internal sealed class GeneralVersionChoiceDialog : GeneralDialog {
    internal GeneralVersionChoiceDialog(ModVersionDifference difference):base(GeneralStyle.T("Versão diferente encontrada","Different version found")){
        RequiresDecision=true;Name="VersionChoice";ClientSize=new Size(680,400);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new Padding(24,8,24,24)};
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var text=GeneralMessageText.Create(difference.Name+"\r\n\r\n"+GeneralStyle.T("Instalado: ","Installed: ")+difference.Current+"\r\n"+GeneralStyle.T("Solicitado: ","Requested: ")+difference.Incoming+"\r\n\r\n"+difference.Detail+"\r\n\r\n"+(difference.AllowSkip?GeneralStyle.T("Pular mod mantém a versão local e continua com os outros mods.","Skip mod keeps the local version and continues with the other mods."):GeneralStyle.T("Manter atual adia este item/pacote inteiro; os outros itens da fila continuam. A escolha não vale para os próximos conflitos.","Keep current defers this entire item/pack; other queue items continue. The choice does not apply to future conflicts.")),new[]{difference.Name});
        text.Name="VersionDetails";
        layout.Controls.Add(new GeneralScrollView(text),0,0);var actions=GeneralTableDialogLayout.Commands();
        var replace=GeneralStyle.Button(difference.ReplaceLabel??GeneralStyle.T("Usar do pacote","Use pack version"),()=>Complete(DialogResult.Yes),true);replace.Name="UseIncomingVersion";replace.Enabled=difference.CanReplace;
        var keep=GeneralStyle.Button(GeneralStyle.T("Manter atual","Keep current"),()=>Complete(difference.AllowSkip?DialogResult.Ignore:DialogResult.No));keep.Name="KeepInstalledVersion";
        actions.Controls.Add(replace);
        if(difference.AllowSkip){
            keep.Text=GeneralStyle.T("Pular mod","Skip mod");
            var defer=GeneralStyle.Button(GeneralStyle.T("Adiar instalação","Defer installation"),()=>Complete(DialogResult.Cancel));
            actions.Controls.Add(keep);actions.Controls.Add(defer);
            CancelButton=AcceptButton=defer;
        }else{
            actions.Controls.Add(keep);
            CancelButton=AcceptButton=keep;
        }
        layout.Controls.Add(actions,0,1);Content.Controls.Add(layout);GeneralStyle.Apply(this,GeneralStyle.Dark);text.BorderStyle=BorderStyle.None;
    }
    internal static bool Ask(Control owner,ModVersionDifference difference){
        if(owner==null||owner.IsDisposed||!owner.IsHandleCreated)return false;
        if(owner.InvokeRequired)return (bool)owner.Invoke((Func<bool>)(()=>Ask(owner,difference)));
        using(var dialog=new GeneralVersionChoiceDialog(difference)){
            var result=dialog.ShowDialog(owner);
            difference.SkipSelected=result==DialogResult.Ignore;
            return result==DialogResult.Yes;
        }
    }
}
