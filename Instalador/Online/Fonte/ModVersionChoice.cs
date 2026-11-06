using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

public sealed class ModVersionDifference {
    public string Name,Current,Incoming,Detail;
    public bool CanReplace=true;
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
internal sealed class GeneralVersionChoiceDialog : GeneralDialog {
    internal GeneralVersionChoiceDialog(ModVersionDifference difference):base(GeneralStyle.T("Versão diferente encontrada","Different version found")){
        Name="VersionChoice";ClientSize=new Size(680,400);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new Padding(24,8,24,24)};
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,60));
        var text=new TextBox{Name="VersionDetails",Multiline=true,ReadOnly=true,WordWrap=true,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,Text=difference.Name+"\r\n\r\n"+GeneralStyle.T("Instalado: ","Installed: ")+difference.Current+"\r\n"+GeneralStyle.T("Solicitado: ","Requested: ")+difference.Incoming+"\r\n\r\n"+difference.Detail+"\r\n\r\n"+GeneralStyle.T("Manter atual adia este item/pacote inteiro; os outros itens da fila continuam. A escolha não vale para os próximos conflitos.","Keep current defers this entire item/pack; other queue items continue. The choice does not apply to future conflicts.")};
        layout.Controls.Add(text,0,0);var actions=GeneralTableDialogLayout.Commands();
        var replace=GeneralStyle.Button(GeneralStyle.T("Usar do pacote","Use pack version"),()=>Complete(DialogResult.Yes),true);replace.Name="UseIncomingVersion";replace.Enabled=difference.CanReplace;
        var keep=GeneralStyle.Button(GeneralStyle.T("Manter atual","Keep current"),()=>Complete(DialogResult.No));keep.Name="KeepInstalledVersion";
        actions.Controls.Add(replace);actions.Controls.Add(keep);layout.Controls.Add(actions,0,1);Content.Controls.Add(layout);CancelButton=keep;AcceptButton=keep;GeneralStyle.Apply(this,GeneralStyle.Dark);text.BorderStyle=BorderStyle.None;
    }
    internal static bool Ask(Control owner,ModVersionDifference difference){
        if(owner==null||owner.IsDisposed||!owner.IsHandleCreated)return false;
        if(owner.InvokeRequired)return (bool)owner.Invoke((Func<bool>)(()=>Ask(owner,difference)));
        using(var dialog=new GeneralVersionChoiceDialog(difference))return dialog.ShowDialog(owner)==DialogResult.Yes;
    }
}