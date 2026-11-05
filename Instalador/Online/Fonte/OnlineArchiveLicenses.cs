using System;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

public static class ArchiveLicenses {
    public static string Text(){using(var input=typeof(ArchiveLicenses).Assembly.GetManifestResourceStream("CyberpunkCoop.Reader.Licenses.txt")){
        if(input==null||input.Length!=ArchiveReaderIdentity.LicensesSize)throw new IOException("Licenças do leitor ausentes.");
        using(var memory=new MemoryStream()){input.CopyTo(memory);byte[] data=memory.ToArray();using(var sha=SHA256.Create())if(Safe.Hex(sha.ComputeHash(data))!=ArchiveReaderIdentity.LicensesHash)throw new IOException("Licenças do leitor divergentes.");return new UTF8Encoding(false,true).GetString(data);}
    }}
    public static void SaveSource(string destination){
        Safe.NoLinks(destination);byte[] bytes;
        using(var input=typeof(ArchiveLicenses).Assembly.GetManifestResourceStream("CyberpunkCoop.Reader.Source.tar.xz")){
            if(input==null||input.Length!=ArchiveReaderIdentity.SourceSize)throw new IOException("Código-fonte correspondente ausente.");
            using(var memory=new MemoryStream()){input.CopyTo(memory);bytes=memory.ToArray();}
        }
        using(var sha=SHA256.Create())if(Safe.Hex(sha.ComputeHash(bytes))!=ArchiveReaderIdentity.SourceHash)throw new IOException("Código-fonte do leitor divergente.");
        string temp=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){output.Write(bytes,0,bytes.Length);output.Flush(true);}Safe.NoLinks(destination);if(File.Exists(destination))File.Replace(temp,destination,null);else File.Move(temp,destination);}finally{if(File.Exists(temp)){Safe.NoLinks(temp);File.Delete(temp);}}
    }
}
public sealed class ArchiveLicensesWindow:Form {
    public ArchiveLicensesWindow(){
        Text="Sobre e licenças";Font=new Font("Segoe UI",10);ClientSize=new Size(780,560);MinimumSize=new Size(620,420);StartPosition=FormStartPosition.CenterParent;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(16),ColumnCount=1,RowCount=3};layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));Controls.Add(layout);
        layout.Controls.Add(new Label{Text="Mod Loader · leitor 7-Zip "+ArchiveReaderIdentity.Version+" incorporado",AutoSize=true,Margin=new Padding(0,0,0,10)},0,0);
        layout.Controls.Add(new TextBox{Name="ReaderLicenses",Text=ArchiveLicenses.Text(),ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,BackColor=Color.White},0,1);
        var buttons=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};
        var close=new Button{Text="Fechar",AutoSize=true,MinimumSize=new Size(100,34),DialogResult=DialogResult.Cancel};buttons.Controls.Add(close);CancelButton=close;
        var source=new Button{Name="SaveReaderSource",Text="Salvar código-fonte do leitor…",AutoSize=true,MinimumSize=new Size(220,34)};
        source.Click+=(s,e)=>{using(var save=new SaveFileDialog{FileName="7z2605-src.tar.xz",Filter="Código-fonte original (*.tar.xz)|*.tar.xz",OverwritePrompt=true})if(save.ShowDialog(this)==DialogResult.OK){try{ArchiveLicenses.SaveSource(save.FileName);}catch(Exception ex){MessageBox.Show(this,ex.Message,"Não foi possível salvar",MessageBoxButtons.OK,MessageBoxIcon.Warning);}}};buttons.Controls.Add(source);layout.Controls.Add(buttons,0,2);
    }
}
