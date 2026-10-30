using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

// Download-only source identities are separate from approved installation recipes.
public sealed class FacialDownload {
    public readonly string Name,Variant;
    public readonly int ModId,FileId;
    public FixedAsset Asset(){return FacialSourcePins.For(this);}
    public long PublishedSizeKiB {get{switch(FileId){case 99414:return 58578;case 95893:return 470;case 30004:return 2528;case 56673:return 390;case 12061:return 59830;case 160715:return 282;case 39563:return 8233;case 39566:return 4009;case 94454:return 1164;case 58122:return 231;case 35570:return 225;case 58415:return 2;case 8720:return 218;case 8374:return 51;default:throw new InvalidOperationException("Tamanho publicado não cadastrado.");}}}
    public string Id {get{return "face-"+ModId+"-"+FileId;}}
    public string Url {get{return "https://www.nexusmods.com/cyberpunk2077/mods/"+ModId+"?tab=files&file_id="+FileId;}}
    FacialDownload(string name,string variant,int mod,int file){Name=name;Variant=variant;ModId=mod;FileId=file;}
    public static List<FacialDownload> List(){return new List<FacialDownload>{
        new FacialDownload("EKT Asian Character Creator","VTK / Unique Eyes · 1.06",16930,99414),
        new FacialDownload("Unique Eyes Core","Default · V3.0",18594,95893),
        new FacialDownload("Kala's Eyes","Hanako · 1.2",2422,30004),
        new FacialDownload("Microdetails","2.0",1873,56673),
        new FacialDownload("Ava Complexions All in one","1.7 · arquivo antigo",1873,12061),
        new FacialDownload("Alternative Skin Material","1.6",7341,160715),
        new FacialDownload("Realistic Facial Details","Feminine V · principal",7474,39563),
        new FacialDownload("Realistic Facial Details — Shine","Feminine V / Vanilla HD",7474,39566),
        new FacialDownload("Vesnace Lipsticks","Classics · 1.1",7290,94454),
        new FacialDownload("Alternate Eyeliners","AIO · 2.02",11033,58122),
        new FacialDownload("CC Fix","Complete · 1.0",6788,35570),
        new FacialDownload("Darker Eyebrows","Principal · 1",4131,58415),
        new FacialDownload("Koralina's Eyebrows","No. 5 / Edit 02",1975,8720),
        new FacialDownload("Koralina's Lip Makeup","Edit 1",1915,8374)
    };}
}

public sealed class FacialDownloadsWindow:Form {
    readonly List<FacialDownload> items=FacialDownload.List();
    readonly Action<string> openWeb;
    readonly HashSet<int> opened=new HashSet<int>();
    readonly DataGridView grid=new DataGridView{Name="FacialDownloadList"};
    readonly Label status=new Label{Name="FacialDownloadStatus"};
    readonly Label count=new Label{Name="FacialDownloadCount"};
    readonly Button next=new Button{Name="NextFacialDownload",Text="Baixar próximo"};
    public FacialDownloadsWindow(string folder,Action<string> launch){
        if(launch==null)throw new ArgumentNullException("launch");openWeb=launch;
        Text="Conjunto facial · 14 downloads";Font=new Font("Segoe UI",10);BackColor=Color.White;
        ClientSize=new Size(1000,720);MinimumSize=new Size(860,640);StartPosition=FormStartPosition.CenterParent;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(18),ColumnCount=1,RowCount=7};Controls.Add(layout);
        for(int i=0;i<7;i++)layout.RowStyles.Add(new RowStyle(i==3?SizeType.Percent:SizeType.AutoSize,i==3?100:0));
        layout.Controls.Add(new Label{Text="Conjunto facial 17331",AutoSize=true,Font=new Font(Font.FontFamily,19,FontStyle.Bold),Margin=new Padding(0,0,0,7)},0,0);
        layout.Controls.Add(new Label{Text="14 arquivos para baixar · corpo padrão · sem o efeito de olhos removido\nO preset, a lista do autor, a cabeça VTK e o Avacore já foram conferidos nesta preparação.",AutoSize=true,MaximumSize=new Size(940,0),Margin=new Padding(0,0,0,8)},0,1);
        count.AutoSize=true;count.Margin=new Padding(0,0,0,7);layout.Controls.Add(count,0,2);
        grid.Dock=DockStyle.Fill;grid.ReadOnly=true;grid.AllowUserToAddRows=false;grid.AllowUserToDeleteRows=false;grid.RowHeadersVisible=false;grid.MultiSelect=false;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;grid.BackgroundColor=Color.White;grid.RowTemplate.Height=31;grid.ColumnHeadersHeight=30;
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Name",HeaderText="Arquivo",AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill});
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Variant",HeaderText="Versão / variante",Width=270});
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="State",HeaderText="Nesta janela",Width=150});
        foreach(var item in items){int row=grid.Rows.Add(item.Name,item.Variant,"A baixar");grid.Rows[row].Cells["Name"].ToolTipText=item.Url;}
        grid.CellDoubleClick+=(s,e)=>{if(e.RowIndex>=0)Open(e.RowIndex);};layout.Controls.Add(grid,0,3);
        var guide=new Label{Text="1. Clique em Baixar próximo ou selecione uma linha e use Abrir selecionado.\n2. No navegador padrão, entre no Nexus se solicitado e clique em Manual / Slow download.\n3. Depois de iniciar o download, volte aqui para abrir o próximo. Deixe os arquivos originais em:\n"+folder+"\nEsta etapa só abre as páginas oficiais: não instala, extrai ou apaga arquivos. Página aberta não significa arquivo baixado. Os pacotes serão conferidos depois.",AutoSize=true,MaximumSize=new Size(940,0),Margin=new Padding(0,12,0,7),ForeColor=Color.DimGray};layout.Controls.Add(guide,0,4);
        status.AutoSize=true;status.MaximumSize=new Size(940,0);status.Margin=new Padding(0,0,0,8);status.Text="Comece pelo primeiro arquivo. Nenhum download foi aberto por esta janela.";layout.Controls.Add(status,0,5);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,FlowDirection=FlowDirection.RightToLeft,WrapContents=false};
        var close=new Button{Text="Fechar",AutoSize=true,MinimumSize=new Size(110,35),DialogResult=DialogResult.Cancel};buttons.Controls.Add(close);CancelButton=close;
        next.AutoSize=true;next.MinimumSize=new Size(145,35);next.Click+=(s,e)=>OpenNext();buttons.Controls.Add(next);
        var selected=new Button{Name="OpenSelectedFacialDownload",Text="Abrir selecionado",AutoSize=true,MinimumSize=new Size(145,35)};selected.Click+=(s,e)=>{if(grid.CurrentRow!=null)Open(grid.CurrentRow.Index);};buttons.Controls.Add(selected);
        layout.Controls.Add(buttons,0,6);UpdateCount();
    }
    internal void OpenNext(){for(int i=0;i<items.Count;i++)if(!opened.Contains(i)){Open(i);return;}}
    internal void Open(int index){
        if(index<0||index>=items.Count)return;var item=items[index];
        try{
            OnlineWindow.BrowserRequest(item.Url);
            OnlineDiagnostics.Record("face-download.open.request","face-"+item.ModId+"-"+item.FileId,"Abrir download facial oficial; sem instalação.",new{ModId=item.ModId,FileId=item.FileId,Install=false});
            openWeb(item.Url);opened.Add(index);grid.Rows[index].Cells["State"].Value="Página aberta";grid.Rows[index].Cells["State"].Style.ForeColor=Color.Black;
            grid.CurrentCell=grid.Rows[index].Cells["Name"];status.Text=item.Name+": página enviada ao navegador padrão. Clique em Manual / Slow download; depois volte para o próximo.";
            OnlineDiagnostics.Record("face-download.open.dispatched","face-"+item.ModId+"-"+item.FileId,"Página enviada ao Windows; não confirma download ou instalação.",new{ModId=item.ModId,FileId=item.FileId,Install=false});
        }catch(Exception ex){grid.Rows[index].Cells["State"].Value="Não abriu";grid.Rows[index].Cells["State"].Style.ForeColor=Color.Firebrick;status.Text="Não foi possível abrir a página. Selecione a linha e tente novamente.";OnlineDiagnostics.Record("face-download.open.failed","face-"+item.ModId+"-"+item.FileId,"Falha ao abrir página; item continua disponível para nova tentativa.",new{ErrorType=ex.GetType().Name});}
        UpdateCount();
    }
    void UpdateCount(){count.Text="Arquivos: "+items.Count+"   |   Páginas abertas nesta janela: "+opened.Count+"   |   A abrir: "+(items.Count-opened.Count);next.Enabled=opened.Count<items.Count;}
}
