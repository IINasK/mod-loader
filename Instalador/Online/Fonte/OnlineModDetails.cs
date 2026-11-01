using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

public sealed class ModDetailsInfo {
    public string Name,InstalledVersion,PackageVersion,State,GameRoot,OfficialUrl,Notes,Edition;
    public readonly List<string[]> Files=new List<string[]>();
    public static string OfficialPage(FixedMod mod){
        var asset=mod.Assets.FirstOrDefault(a=>a.ModId>0);int nexusId=asset==null?mod.NexusModId:asset.ModId;
        string url;
        if(nexusId>0)url="https://www.nexusmods.com/cyberpunk2077/mods/"+nexusId;
        else {
            var uri=new Uri(mod.Assets[0].Url);var parts=uri.AbsolutePath.Split(new[]{'/'},StringSplitOptions.RemoveEmptyEntries);
            OnlineWindow.BrowserRequest(uri.AbsoluteUri);
            if(uri.DnsSafeHost=="github.com"&&parts.Length>=5&&parts[2]=="releases"&&parts[3]=="download")url="https://github.com/"+parts[0]+"/"+parts[1]+"/releases/tag/"+parts[4];
            else if(uri.DnsSafeHost=="raw.githubusercontent.com"&&parts.Length>=2)url="https://github.com/"+parts[0]+"/"+parts[1];
            else if(uri.DnsSafeHost=="nightcoop.net")url="https://nightcoop.net/";
            else url=uri.AbsoluteUri;
        }
        OnlineWindow.BrowserRequest(url);return url;
    }
    public static ModDetailsInfo Read(FixedMod mod,string root,string notes,FixedCatalog catalog=null){
        var info=new ModDetailsInfo{Name=mod.Name,PackageVersion=mod.Version,OfficialUrl=OfficialPage(mod),Notes=notes??""};
        info.Edition=GameEdition.Category(mod);if(catalog!=null){string reason=GameEdition.Reason(catalog,new[]{mod.Id});if(reason!=null)info.Edition=reason;}
        info.Notes+=(String.IsNullOrWhiteSpace(info.Notes)?"":"\r\n")+(mod.PhantomLibertyNotes??"")+(String.IsNullOrWhiteSpace(mod.PhantomLibertySource)?"":"\r\n"+mod.PhantomLibertySource);
        bool hasRoot=!String.IsNullOrWhiteSpace(root);FixedMod filesMod=NightCityDensity.Default(mod);
        if(hasRoot){
            info.GameRoot=Path.GetFullPath(root);Safe.NoLinks(info.GameRoot);var scan=FixedData.Scan(mod,info.GameRoot);
            info.State=scan.State;info.InstalledVersion=scan.IsCorrect?mod.Version:scan.State=="Não instalado"?"Não instalado":"Não identificada: arquivos incompletos ou diferentes";
            info.Notes=scan.Detail+(String.IsNullOrWhiteSpace(info.Notes)?"":"\r\n"+info.Notes);
            if(mod.DensityProfiles!=null){
                try{filesMod=NightCityDensity.Installed(mod,info.GameRoot);var density=NightCityDensity.Read(mod,info.GameRoot);if(!String.IsNullOrEmpty(density.Version))info.InstalledVersion=density.Version+" · "+density.Name;}
                catch(IOException){filesMod=new FixedMod{Files=mod.Files.Concat(mod.DensityProfiles.SelectMany(p=>p.Files)).GroupBy(f=>f.Path,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).ToList()};}
            }
        }else{info.GameRoot="Escolha a pasta do jogo na tela principal";info.State="Pasta não informada";info.InstalledVersion="Não verificada";}
        foreach(var file in filesMod.Files){
            string path=hasRoot?Safe.In(info.GameRoot,file.Path):file.Path;bool present=false;
            if(hasRoot){Safe.NoLinks(path);present=File.Exists(path);}
            info.Files.Add(new[]{path,file.UserData?"Perfil personalizável (preservado)":hasRoot?(present?"Presente":"Ausente"):"Destino previsto"});
        }
        return info;
    }
}

public sealed class ModDetailsWindow:Form {
    public ModDetailsWindow(ModDetailsInfo info,Action<string> openWeb){
        Text="Detalhes do mod";Font=new Font("Segoe UI",10);BackColor=Color.White;ClientSize=new Size(820,620);MinimumSize=new Size(700,520);StartPosition=FormStartPosition.CenterParent;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(18),ColumnCount=1,RowCount=6};Controls.Add(layout);
        for(int i=0;i<6;i++)layout.RowStyles.Add(new RowStyle(i==3?SizeType.Percent:SizeType.AutoSize,i==3?100:0));
        layout.Controls.Add(new Label{Text=info.Name,Font=new Font(Font.FontFamily,18,FontStyle.Bold),AutoSize=true,MaximumSize=new Size(760,0),Margin=new Padding(0,0,0,12)},0,0);
        var fields=new TableLayoutPanel{ColumnCount=2,AutoSize=true,Dock=DockStyle.Top};fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,160));fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        string[][] values={new[]{"Versão instalada",info.InstalledVersion},new[]{"Versão do pacote",info.PackageVersion},new[]{"Situação",info.State},new[]{"Pasta do jogo",info.GameRoot},new[]{"Página oficial",info.OfficialUrl},new[]{"Expansão",info.Edition}};
        for(int i=0;i<values.Length;i++){
            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));fields.Controls.Add(new Label{Text=values[i][0],AutoSize=true,Anchor=AnchorStyles.Left},0,i);
            fields.Controls.Add(new TextBox{Name="Detail"+i,Text=values[i][1],ReadOnly=true,BackColor=Color.White,Dock=DockStyle.Fill,AccessibleName=values[i][0]},1,i);
        }
        layout.Controls.Add(fields,0,1);layout.Controls.Add(new Label{Text="Arquivos do mod ("+info.Files.Count+")",AutoSize=true,Margin=new Padding(0,12,0,6)},0,2);
        var files=new DataGridView{Name="ModFiles",Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,BackgroundColor=Color.White,AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells,AccessibleName="Localização dos arquivos do mod"};
        files.Columns.Add(new DataGridViewTextBoxColumn{Name="Path",HeaderText="Localização do arquivo",AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill});files.Columns.Add(new DataGridViewTextBoxColumn{Name="State",HeaderText="Arquivo",Width=125});
        foreach(var file in info.Files){int row=files.Rows.Add(file[0],file[1]);files.Rows[row].Cells[0].ToolTipText=file[0];}
        layout.Controls.Add(files,0,3);
        layout.Controls.Add(new TextBox{Text=info.Notes,ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,Height=62,BackColor=Color.White,AccessibleName="Informações adicionais",Margin=new Padding(0,10,0,7)},0,4);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,FlowDirection=FlowDirection.RightToLeft,WrapContents=false};
        var close=new Button{Text="Fechar",AutoSize=true,MinimumSize=new Size(110,34),DialogResult=DialogResult.Cancel};buttons.Controls.Add(close);CancelButton=close;
        var web=new Button{Name="OpenWeb",Text="Abrir na web",AutoSize=true,MinimumSize=new Size(140,34)};web.Click+=(s,e)=>{try{OnlineWindow.BrowserRequest(info.OfficialUrl);var page=new Uri(info.OfficialUrl);OnlineDiagnostics.Record("browser.details.request",null,"Abrir na web acionado nos detalhes.",new{Name=info.Name,Host=page.DnsSafeHost,Path=page.AbsolutePath});openWeb(info.OfficialUrl);}catch(Exception ex){MessageBox.Show(this,ex.Message,"Não foi possível abrir a página",MessageBoxButtons.OK,MessageBoxIcon.Error);}};buttons.Controls.Add(web);
        layout.Controls.Add(buttons,0,5);
    }
}
