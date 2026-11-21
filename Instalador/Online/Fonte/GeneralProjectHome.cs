using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralProjectHome : Form {
    readonly FixedCatalog catalog;
    readonly DownloadCache cache;
    readonly GeneralProjectStore store;
    readonly bool fixture;
    readonly GeneralGameCoverCache coverCache;
    readonly Dictionary<string,P2PService> sharing=new Dictionary<string,P2PService>(StringComparer.OrdinalIgnoreCase);
    readonly GeneralMenuHost menus=new GeneralMenuHost();
    GeneralModWindow sharingWindow;
    GeneralP2PDialog sharingDialog;
    bool exitApproved,waitingForInstallation,showTemplateCopies,hasTemplateCopies;
    bool coverSizeAdjusted;
    TrackBar coverSize;
    Action reflowGallery;
    string ReceiveData {get{return Path.Combine(store.Root,"P2P-Inbox");}}
    CancellationTokenSource coverRequests;
    LinkLabel update;
    GeneralUpdateRelease available=null;
    int buildVersion;
    internal bool DeletingProject {get;private set;}
    readonly ToolTip savedTips=new ToolTip{UseAnimation=false,UseFading=false};

    internal Action<GeneralProject,string> OpenRequested;
    internal GeneralProjectHome(FixedCatalog catalog,DownloadCache cache,string root,bool fixture=false){
        this.catalog=catalog;this.cache=cache;this.fixture=fixture;store=new GeneralProjectStore(root);OpenRequested=OpenProject;
        string coverRoot=fixture?Path.Combine(store.Root,"CoverCache"):GeneralAppPaths.Current().Cache;
        coverCache=new GeneralGameCoverCache(coverRoot,allowNetwork:!fixture);
        Text=GeneralAppPaths.WindowTitle;Icon=ModLoaderBrand.AppIcon;ClientSize=new Size(1020,740);MinimumSize=new Size(900,680);
        StartPosition=FormStartPosition.CenterScreen;AutoScaleMode=AutoScaleMode.Dpi;Build();Disposed+=(s,e)=>{
            StopCoverRequests();savedTips.Dispose();menus.Dispose();
            foreach(var service in sharing.Values)try{service.Dispose();}catch(Exception){}
        };
        FormClosing+=CloseHome;
        if(!fixture)Shown+=async(s,e)=>await RestoreSharing();
        if(!fixture)Shown+=async(s,e)=>await CheckHomeModVersions();
#if !VELOPACK
        if(!fixture&&!GeneralAppPaths.IsDevelopment)Shown+=async(s,e)=>{available=await GeneralUpdateChecker.Session.CheckOnce();if(!IsDisposed)UpdateNotice();};
#endif
    }
    static string T(string pt,string en){return GeneralStyle.T(pt,en);}
    internal void Build(){
        var index=store.Read();GeneralStyle.UseTheme(index.Theme);GeneralStyle.Language=index.Language;
        menus.Close();buildVersion++;StopCoverRequests();coverRequests=new CancellationTokenSource();reflowGallery=null;
        SuspendLayout();while(Controls.Count>0){var old=Controls[0];Controls.Remove(old);old.Dispose();}
        Font=GeneralStyle.Font(12);BackColor=GeneralStyle.Background;ForeColor=GeneralStyle.Foreground;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(32,28,32,24)};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        for(int i=0;i<4;i++)layout.RowStyles.Add(i==2?new RowStyle(SizeType.Percent,100):new RowStyle(SizeType.AutoSize));Controls.Add(layout);
        var header=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=3,Margin=Padding.Empty};
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,130));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,44));
        var brand=new FlowLayoutPanel{AutoSize=true,WrapContents=false,Margin=Padding.Empty};brand.Controls.Add(new ModLoaderLogo{Size=new Size(44,44),Margin=new Padding(0,0,12,0)});
        var brandTitle=new Label{Text="MOD LOADER",AutoSize=true,Font=GeneralStyle.Font(27,true),ForeColor=GeneralStyle.Accent,Margin=Padding.Empty};brand.Controls.Add(brandTitle);header.Controls.Add(brand,0,0);
        var menu=(GeneralButton)GeneralStyle.Button("",ToggleHomeMenu);menu.Name="HomeMenu";menu.Bare=true;menu.IconKind="menu";menu.AccessibleName=T("Menu","Menu");menu.Size=new Size(44,44);menu.AutoSize=false;menu.MinimumSize=new Size(44,44);menu.Dock=DockStyle.Fill;menu.Margin=Padding.Empty;header.Controls.Add(menu,2,0);
        coverSizeAdjusted=index.GalleryCoverWidth>=142&&index.GalleryCoverWidth<=275;
        coverSize=new GalleryScaleSlider{Name="GalleryCoverSize",AccessibleName=T("Tamanho das capas","Cover size"),
            Minimum=142,Maximum=275,Value=coverSizeAdjusted?index.GalleryCoverWidth:180,SmallChange=8,LargeChange=16,
            TickStyle=TickStyle.None,AutoSize=false,Height=30,Anchor=AnchorStyles.Left|AnchorStyles.Right,Margin=new Padding(4,0,10,0)};
        coverSize.ValueChanged+=(s,e)=>{coverSizeAdjusted=true;if(reflowGallery!=null)reflowGallery();};
        coverSize.MouseUp+=(s,e)=>SaveCoverSize();
        coverSize.KeyUp+=(s,e)=>SaveCoverSize();
        savedTips.SetToolTip(coverSize,coverSize.AccessibleName);
        header.Controls.Add(coverSize,1,0);layout.Controls.Add(header,0,0);
        var intro=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=Padding.Empty};
        intro.Controls.Add(new Label{Name="HomeIntro",Text=T("Criado por Nask e Codex. Se achou ruim, faz você.","Made by Nask and Codex. If you don't like it, make your own."),AutoSize=true,ForeColor=GeneralStyle.Muted,Margin=new Padding(0,10,0,4)});
        intro.Margin=new Padding(0,0,0,22);layout.Controls.Add(intro,0,1);
        layout.Controls.Add(BuildProjectGallery(index),0,2);
        update=new LinkLabel{Name="HomeUpdate",AutoSize=true,LinkColor=GeneralStyle.Accent,ActiveLinkColor=GeneralStyle.Foreground,Margin=Padding.Empty};update.LinkClicked+=(s,e)=>{if(available!=null)Guard(()=>GeneralModDownload.OpenSource(available.Page));};layout.Controls.Add(update,0,3);UpdateNotice();
        GeneralStyle.Apply(this,GeneralStyle.Dark);
        foreach(Control choices in Controls.Find("NewProjectChoices",true))choices.BackColor=Color.Transparent;
        brandTitle.ForeColor=GeneralStyle.Accent;ResumeLayout(true);GeneralNativeTheme.ApplyTree(this);
    }
    Control BuildProjectGallery(GeneralProjectIndex index){
        var gallery=new Panel{Name="ProjectGallery"};
        var heading=Heading(T("Projetos salvos","Saved projects"));heading.Name="SavedProjectsHeading";
        var readyHeading=Heading(T("Projetos prontos","Ready projects"));readyHeading.Name="ReadyProjectsHeading";
        var divider=new Panel{Name="ProjectGalleryDivider",Height=1,BackColor=GeneralStyle.Line};
        var grid=new Panel{Name="ProjectCards"};
        grid.Controls.AddRange(new Control[]{readyHeading,divider});
        var cards=new List<HomeProjectCard>();
        var official=index.Projects.FirstOrDefault(item=>item.TemplateId==GeneralProjectStore.CyberpunkTemplate);
        var officialState=official==null?new GeneralModState{List=GeneralModLibrary.Seed(catalog,new ModImportItem[0]),Language=index.Language}:ReadCardState(official);
        hasTemplateCopies=false;
        foreach(var project in index.Projects.Where(item=>item.TemplateId==null).Reverse()){
            var saved=ReadCardState(project);
            if(official!=null&&AutomaticTemplateCopy(project,official,saved,officialState)){
                hasTemplateCopies=true;
                if(!showTemplateCopies)continue;
            }
            AddProjectCards(grid,cards,project,saved,false);
        }
        bool hasSaved=cards.Count!=0;
        if(hasSaved)AddNewProjectCard(grid,cards,false);
        AddProjectCards(grid,cards,official,officialState,true);
        if(!hasSaved)AddNewProjectCard(grid,cards,true);

        heading.Visible=hasSaved;
        gallery.Controls.AddRange(new Control[]{heading,grid});
        var scroll=new GeneralScrollView(gallery,panelContent:true,reserveBarSpace:true,fitPanelContent:true){Name="ProjectGalleryScroll"};
        Action reflow=()=>{
            gallery.SuspendLayout();
            try{
                heading.Location=new Point(0,2);
                PlaceGalleryCards(gallery,grid,cards,hasSaved?heading.Bottom+8:0,scroll.ClientSize.Height);
            }finally{gallery.ResumeLayout(false);}
        };
        reflowGallery=reflow;
        gallery.SizeChanged+=(s,e)=>reflow();
        gallery.DpiChangedAfterParent+=(s,e)=>reflow();
        scroll.SizeChanged+=(s,e)=>reflow();
        foreach(var card in cards){
            card.Enter+=(s,e)=>ShowFocusedCard(scroll,card);
            card.MouseWheel+=(s,e)=>{if(scroll.RouteWheel(e.Delta)&&e is HandledMouseEventArgs)((HandledMouseEventArgs)e).Handled=true;};
            if(card.RemoveButton!=null){
                card.RemoveButton.Enter+=(s,e)=>ShowFocusedCard(scroll,card);
                card.RemoveButton.MouseWheel+=(s,e)=>{if(scroll.RouteWheel(e.Delta)&&e is HandledMouseEventArgs)((HandledMouseEventArgs)e).Handled=true;};
            }
        }
        var token=coverRequests.Token;
        gallery.HandleCreated+=(s,e)=>LoadCoverImages(cards.ToArray(),token);
        return scroll;
    }

    static bool AutomaticTemplateCopy(GeneralProject copy,GeneralProject official,GeneralModState saved,GeneralModState template){
        if(saved==null||template==null||String.IsNullOrWhiteSpace(saved.Destination))return false;
        if(!String.Equals((saved.Destination??"").TrimEnd('\\'),(template.Destination??"").TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase))return false;
        return new[]{" · Minha versão"," · My version"}.Any(suffix=>{
            string name=official.Name+suffix;
            if(copy.Name==name)return true;
            if(!copy.Name.StartsWith(name+" (",StringComparison.Ordinal)||!copy.Name.EndsWith(")",StringComparison.Ordinal))return false;
            int number;
            return Int32.TryParse(copy.Name.Substring(name.Length+2,copy.Name.Length-name.Length-3),out number)&&number>=2;
        });
    }

    sealed class GalleryScaleSlider : TrackBar {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr handle,int message,IntPtr parameter,ref Rectangle bounds);
        internal GalleryScaleSlider(){
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|
                ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            ValueChanged+=(s,e)=>Invalidate();
            GotFocus+=(s,e)=>Invalidate();
            LostFocus+=(s,e)=>Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e){
            var channel=Rectangle.Empty;
            var thumb=Rectangle.Empty;
            SendMessage(Handle,1050,IntPtr.Zero,ref channel);
            SendMessage(Handle,1049,IntPtr.Zero,ref thumb);
            int size=12*DeviceDpi/96;
            int y=ClientSize.Height/2,center=(thumb.X+thumb.Width)/2;
            int left=channel.X,right=channel.Width;
            e.Graphics.Clear(Parent==null?GeneralStyle.Surface:Parent.BackColor);
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var rail=new Pen(GeneralStyle.Line,3))
            using(var fill=new SolidBrush(GeneralStyle.Accent)){
                rail.StartCap=rail.EndCap=LineCap.Round;
                e.Graphics.DrawLine(rail,left,y,right,y);
                rail.Color=GeneralStyle.Accent;
                e.Graphics.DrawLine(rail,left,y,Math.Max(left,center),y);
                var handle=new Rectangle(center-size/2,y-size/2,size,size);
                e.Graphics.FillEllipse(fill,handle);
                if(Focused&&ShowFocusCues){handle.Inflate(3,3);e.Graphics.DrawEllipse(rail,handle);}
            }
        }
    }

    void SaveCoverSize(){
        if(!coverSizeAdjusted)return;
        try{store.SaveGalleryCoverWidth(coverSize.Value);}
        catch(Exception ex){OnlineDiagnostics.Failure("home.gallery.size",ex,"Salvar o tamanho das capas.");}
    }

    void AddNewProjectCard(Panel grid,List<HomeProjectCard> cards,bool official){
        var card=new HomeProjectCard(null,null,T("Novo projeto","New project"),null,null,null,official,true){Name="NewProjectCard"};
        card.ConfigureNewProjectActions(
            ()=>Guard(()=>{var project=CreateProject(false);if(project!=null)OpenRequested(project,null);}),
            ()=>Guard(()=>{var project=ImportProject();if(project!=null)OpenRequested(project,null);}));
        var slot=new Panel{TabIndex=cards.Count};
        slot.Controls.Add(card);grid.Controls.Add(slot);cards.Add(card);
        card.Click+=(s,e)=>card.ToggleChoices();
    }

    GeneralModState ReadCardState(GeneralProject project){
        try { return store.Load(project,true); }
        catch(IOException) { return null; }
        catch(UnauthorizedAccessException) { return null; }
    }

    void AddProjectCards(Panel grid,List<HomeProjectCard> cards,GeneralProject project,GeneralModState state,bool official){
        string projectName=project==null?"Cyberpunk 2077":project.Name;
        string domain=GeneralGameCoverCache.Identify(project??new GeneralProject{TemplateId=GeneralProjectStore.CyberpunkTemplate},state);
        var card=new HomeProjectCard(project,null,projectName,null,ProjectCardDetails(state),domain,official);
        using(var local=ProjectCoverImage.ReadLocal(store.CoverPath(project)))if(local!=null)card.SetCover((Image)local.Clone());
        card.Name=official?"ReadyCyberpunk":"SavedProjectCard";
        card.Click+=(s,e)=>Guard(()=>OpenRequested(project??CreateProject(true),null));
        cards.Add(card);
        var slot=new Panel{TabIndex=cards.Count-1};
        card.TabIndex=0;slot.Controls.Add(card);grid.Controls.Add(slot);
        savedTips.SetToolTip(card,card.AccessibleName+"\r\n"+T("Tamanho dos pacotes de origem conhecidos. O tamanho instalado pode variar.","Known source package sizes. Installed size may vary."));
        if(official)return;
        card.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Delete){e.SuppressKeyPress=true;DeleteSavedProject(project);}};
        var remove=new HomeCardAction(true){Name="DeleteSavedProject",AccessibleName=T("Excluir projeto","Delete project")+" "+projectName,TabIndex=1};
        var edit=new HomeCardAction(false){Name="EditSavedProject",AccessibleName=T("Editar projeto","Edit project")+" "+projectName,TabIndex=2};
        remove.Click+=(s,e)=>DeleteSavedProject(project);edit.Click+=(s,e)=>Guard(()=>EditSavedProject(project,card));
        card.ConfigureActions(remove,edit);
        savedTips.SetToolTip(remove,remove.AccessibleName);savedTips.SetToolTip(edit,edit.AccessibleName);
    }

    string ProjectCardDetails(GeneralModState state){
        if(state==null)return T("Dados indisponíveis","Details unavailable");
        var members=new HashSet<string>(state.List.Mods.Select(mod=>mod.Id),StringComparer.Ordinal);
        if(members.Count==0)return "0 mods  ·  0 B";
        var sources=new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase);
        bool partial=false,estimated=false;
        Action<string,long> add=(identity,bytes)=>{
            if(String.IsNullOrEmpty(identity)||bytes<=0){partial=true;return;}
            long previous;sources.TryGetValue(identity,out previous);
            sources[identity]=Math.Max(previous,bytes);
        };
        foreach(var entry in state.List.Mods.Where(mod=>members.Contains(mod.Id))){
            var mod=entry;
            string variantId;if(!state.ChosenVariants.TryGetValue(mod.Id,out variantId))variantId=mod.DefaultVariant;
            var variant=mod.Variants.FirstOrDefault(item=>item.Id==variantId)??mod.Variants.FirstOrDefault();
            if(variant==null){partial=true;continue;}
            var fixedMod=variant.CyberpunkId==null?null:catalog.Packages.FirstOrDefault(item=>item.Id==variant.CyberpunkId);
            if(fixedMod!=null){
                var density=fixedMod.DensityProfiles==null?null:fixedMod.DensityProfiles.FirstOrDefault(item=>item.Id==variant.CyberpunkVariant);
                var assets=density==null?fixedMod.Assets:density.Assets;
                if(assets.Count==0)partial=true;
                foreach(var asset in assets)add(asset.Sha256??asset.Url,asset.Size);
                continue;
            }
            if(variant.Sources.Count==0)partial=true;
            foreach(string source in variant.Sources){
                GeneralSourceInfo info;
                if(!variant.SourceInfo.TryGetValue(source,out info)){partial=true;continue;}
                var nexus=GeneralNexusLink.Parse(source);
                add(nexus!=null?nexus.Url:source,info.Bytes);
                estimated|=info.SizeApproximate;
            }
        }
        string bytesText=sources.Count==0?T("Tamanho desconhecido","Unknown size"):
            (estimated?"≈ ":"")+GeneralStorageSize.Format(sources.Values.Sum())+(partial?T(" parcial"," partial"):"");
        return members.Count+" "+(members.Count==1?"mod":"mods")+"  ·  "+bytesText;
    }

    void PlaceGalleryCards(Panel gallery,Panel grid,List<HomeProjectCard> cards,int top,int viewportHeight){
        int dpi=gallery.DeviceDpi,gap=18*dpi/96;
        int width=Math.Max(1,gallery.Width);
        int columns=Math.Max(1,Math.Min(3,(width+gap)/(165*dpi/96+gap)));
        int captionHeight=HomeProjectCard.CaptionHeight(dpi);
        int savedCount=cards.Count(card=>!card.Official),savedRows=(savedCount+columns-1)/columns;
        int widthForPreview=savedRows==0?180*dpi/96:
            Math.Max(142*dpi/96,((viewportHeight-top-110*dpi/96)/savedRows-captionHeight-gap)*2/3);
        int preferred=coverSize.Value*dpi/96;
        if(!coverSizeAdjusted)preferred=Math.Min(preferred,widthForPreview);
        int cardWidth=Math.Min(preferred,Math.Max(1,(width-(columns-1)*gap)/columns));
        int cardHeight=cardWidth*3/2+captionHeight;
        int offset=Math.Max(0,(width-columns*cardWidth-(columns-1)*gap)/2);
        int savedHeight=savedRows==0?0:savedRows*(cardHeight+gap)-gap;
        var divider=grid.Controls.Find("ProjectGalleryDivider",false).Single();
        var readyHeading=grid.Controls.Find("ReadyProjectsHeading",false).Single();
        divider.SetBounds(0,savedHeight+20*dpi/96,width,Math.Max(1,dpi/96));
        divider.Visible=savedCount>0;
        readyHeading.Location=new Point(0,savedCount>0?divider.Bottom+14*dpi/96:0);
        int readyTop=readyHeading.Bottom+8*dpi/96;
        int readyRows=(cards.Count-savedCount+columns-1)/columns;
        grid.SetBounds(0,top,width,readyTop+Math.Max(0,readyRows*(cardHeight+gap)-gap));
        for(int i=0;i<cards.Count;i++){
            var card=cards[i];
            int index=card.Official?i-savedCount:i;
            int rowTop=(card.Official?readyTop:0)+(index/columns)*(cardHeight+gap);
            card.Parent.SetBounds(offset+(index%columns)*(cardWidth+gap),rowTop,cardWidth,cardHeight);
            card.Bounds=new Rectangle(0,0,cardWidth,cardHeight);
            card.PlaceActions();
        }
    }

    static void ShowFocusedCard(GeneralScrollView scroll,HomeProjectCard card){
        int top=card.Parent.Top+card.Parent.Parent.Top;
        int position=scroll.Bar.Value;
        if(top<position||card.Height>scroll.ClientSize.Height)scroll.ScrollTo(top);
        else if(top+card.Height>position+scroll.ClientSize.Height)
            scroll.ScrollTo(Math.Min(top,top+card.Height-scroll.ClientSize.Height));
    }

    void StopCoverRequests(){
        if(coverRequests==null)return;
        coverRequests.Cancel();coverRequests.Dispose();coverRequests=null;
    }

    async void LoadCoverImages(HomeProjectCard[] cards,CancellationToken token){
        foreach(var group in cards.Where(card=>card.Domain!=null&&!card.HasCover).GroupBy(card=>card.Domain,StringComparer.OrdinalIgnoreCase)){
            if(token.IsCancellationRequested||IsDisposed)return;
            try{
                using(var cover=await coverCache.GetAsync(group.Key,token)){
                    if(token.IsCancellationRequested||IsDisposed)return;
                    if(cover!=null)foreach(var card in group.Where(card=>!card.IsDisposed))card.SetCover((Image)cover.Clone());
                }
            }catch(OperationCanceledException){return;}
            catch(IOException){ }
            catch(UnauthorizedAccessException){ }
        }
    }

    internal sealed partial class HomeProjectCard : Button {
        const TextFormatFlags CaptionFlags=TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix;
        readonly string subtitle,details;
        Image cover;
        bool hover,pressed;
        internal readonly GeneralProject Project;
        internal readonly string PresetId,Domain;
        internal readonly bool Official;
        internal readonly bool IsNew;
        Panel choices;
        internal Button RemoveButton,EditButton;
        internal bool HasCover {get{return cover!=null;}}
        internal static int CaptionHeight(int dpi){return 100*dpi/96;}

        internal HomeProjectCard(GeneralProject project,string presetId,string title,string subtitle,string details,string domain,bool official,bool isNew=false){
            Project=project;PresetId=presetId;Domain=domain;Official=official;IsNew=isNew;Text=title;this.subtitle=subtitle;this.details=details;
            AccessibleName=String.Join(" · ",new[]{title,subtitle,details}.Where(part=>!String.IsNullOrEmpty(part)));
            AccessibleRole=AccessibleRole.PushButton;
            Cursor=Cursors.Hand;UseMnemonic=false;Font=GeneralStyle.Font(13,true);
            SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            InitializeInteraction();
        }

        internal void ConfigureNewProjectActions(Action create,Action import){
            choices=new Panel{Name="NewProjectChoices",Visible=false,BackColor=Color.Transparent};
            var first=(GeneralButton)GeneralStyle.Button(T("Criar projeto","Create project"),create,true);
            var second=(GeneralButton)GeneralStyle.Button(T("Importar projeto","Import project"),import);
            first.Name="NewProject";second.Name="ImportProject";
            foreach(var button in new[]{first,second}){
                button.AutoSize=false;button.MinimumSize=new Size(0,44);button.CompositeParentSurface=true;
                button.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Escape){CloseChoices();Focus();e.Handled=true;}};
                choices.Controls.Add(button);
            }
            choices.Resize+=(s,e)=>{first.SetBounds(0,0,choices.Width,44);second.SetBounds(0,54,choices.Width,44);};
            Controls.Add(choices);choices.BringToFront();SizeChanged+=(s,e)=>PlaceChoices();
            MouseWheel+=(s,e)=>CloseChoices();
        }
        void PlaceChoices(){
            if(choices==null)return;
            int art=Math.Min(Width*3/2,Height-CaptionHeight(DeviceDpi));
            choices.SetBounds(12,Math.Max(12,(art-98)/2),Math.Max(1,Width-24),98);
        }
        GeneralClickAway choiceDismissal;
        internal void CloseChoices(){
            if(choiceDismissal!=null){choiceDismissal.Dispose();choiceDismissal=null;}
            if(choices!=null)choices.Visible=false;
            Invalidate();
        }
        internal void ToggleChoices(){
            if(choices==null)return;
            if(choices.Visible){CloseChoices();return;}
            PlaceChoices();choices.Visible=true;
            choiceDismissal=new GeneralClickAway(this,CloseChoices);Invalidate();
        }
        protected override void OnLeave(EventArgs e){
            base.OnLeave(e);
            if(choices!=null&&IsHandleCreated)BeginInvoke((Action)(()=>{
                if(!IsDisposed&&!ContainsFocus)CloseChoices();
            }));
        }
        protected override void OnKeyDown(KeyEventArgs e){
            if(e.KeyCode==Keys.Escape&&choices!=null&&choices.Visible){CloseChoices();e.Handled=true;}
            base.OnKeyDown(e);
        }
        internal void SetCover(Image image){if(cover!=null)cover.Dispose();cover=image;Invalidate();}
        protected override void Dispose(bool disposing){if(disposing){if(choiceDismissal!=null)choiceDismissal.Dispose();hoverAnimation.Dispose();SetCover(null);}base.Dispose(disposing);}
        protected override void OnMouseEnter(EventArgs e){SetPointerOver(true);base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){pressed=false;SchedulePointerLeave();base.OnMouseLeave(e);}
        protected override void OnMouseDown(MouseEventArgs e){if(e.Button==MouseButtons.Left){pressed=true;Invalidate();}base.OnMouseDown(e);}
        protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
        protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);SyncInteraction();}
        protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);SchedulePointerLeave();}

        protected override void OnPaint(PaintEventArgs e){
            var g=e.Graphics;g.Clear(GeneralStyle.Surface);g.SmoothingMode=SmoothingMode.AntiAlias;
            int dpi=DeviceDpi, inset=3*dpi/96;
            int artHeight=Math.Min(Width*3/2,Height-CaptionHeight(dpi));
            if(artHeight<8||Width<8)return;
            var art=new Rectangle(inset,inset,Width-inset*2,artHeight-inset*2);
            bool focus=Focused&&ShowFocusCues;
            using(var shape=GeneralStyle.Round(art,12*dpi/96)){
                var clip=g.Save();g.SetClip(shape,CombineMode.Intersect);
                if(cover==null){
                    using(var fill=new SolidBrush(hover?GeneralStyle.Selection:GeneralStyle.DrawerSurface))g.FillRectangle(fill,art);
                    int icon=Math.Min(52*dpi/96,art.Width/3);
                    if(choices==null||!choices.Visible)LucideIcons.Draw(g,IsNew?"plus":"package",new Rectangle(art.X+(art.Width-icon)/2,art.Y+(art.Height-icon)/2,icon,icon),IsNew?GeneralStyle.Accent:GeneralStyle.Muted);
                }else{
                    g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                    double scale=Math.Max((double)art.Width/cover.Width,(double)art.Height/cover.Height)*(1+.035*interaction);
                    var crop=new RectangleF(0,0,(float)(art.Width/scale),(float)(art.Height/scale));
                    crop.X=(cover.Width-crop.Width)/2;crop.Y=(cover.Height-crop.Height)/2;
                    g.DrawImage(cover,art,crop,GraphicsUnit.Pixel);
                    if(pressed){using(var tint=new SolidBrush(Color.FromArgb(36,GeneralStyle.Background)))g.FillRectangle(tint,art);}
                }
                g.Restore(clip);
                if(IsNew){
                    using(var border=new Pen(hover||focus?GeneralStyle.Accent:GeneralStyle.Line,(focus?2:1)*dpi/96f)){border.DashStyle=DashStyle.Dash;g.DrawPath(border,shape);}
                }else if(interaction>0){
                    using(var border=new Pen(Color.FromArgb((int)(255*interaction),Color.White),3*dpi/96f))g.DrawPath(border,shape);
                }
            }
            int x=5*dpi/96,textWidth=Math.Max(1,Width-x*2-(EditButton==null?0:38*dpi/96)),y=artHeight+5*dpi/96;
            int line=Math.Max(Font.Height+2*dpi/96,25*dpi/96),secondary=19*dpi/96;
            bool wrap=TextRenderer.MeasureText(Text,Font).Width>textWidth;
            int heading=wrap?line*2:line;
            var titleFlags=TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(g,Text,Font,new Rectangle(x,y,textWidth,heading),GeneralStyle.Foreground,titleFlags);
            using(var font=GeneralStyle.Font(10.5f)){
                y+=heading;
                if(!String.IsNullOrEmpty(subtitle)){
                    TextRenderer.DrawText(g,subtitle,font,new Rectangle(x,y,textWidth,secondary),GeneralStyle.Muted,CaptionFlags);
                    y+=secondary;
                }
                TextRenderer.DrawText(g,details,font,new Rectangle(x,y,textWidth,secondary),GeneralStyle.Muted,CaptionFlags);
            }
        }
    }
    async void DeleteSavedProject(GeneralProject project){await DeleteSavedProjectAsync(project);}
    internal async Task DeleteSavedProjectAsync(GeneralProject project){
        if(DeletingProject||project==null||project.TemplateId!=null)return;
        DeletingProject=true;
        try{
            bool removeMods;
            using(var choice=new GeneralProjectDeleteChoice(project.Name)){if(choice.ShowDialog(this)!=DialogResult.OK)return;removeMods=choice.RemoveMods;}
            var engine=new GeneralProjectDeletion(store,catalog,fixture);ProjectDeletePlan plan=null;
            if(removeMods){UseWaitCursor=true;Enabled=false;try{plan=await Task.Run(()=>engine.Plan(project));}finally{Enabled=true;UseWaitCursor=false;}}
            using(var review=new GeneralProjectDeleteReview(project.Name,plan))if(review.ShowDialog(this)!=DialogResult.OK)return;
            UseWaitCursor=true;Enabled=false;
            try{await Task.Run(()=>{if(removeMods)engine.Apply(plan);else store.Delete(project);});}finally{Enabled=true;UseWaitCursor=false;}
            Build();
        }catch(Exception ex){GeneralDialog.Message(this,ex.Message+T("\r\n\r\nSe a remoção foi interrompida, o projeto permanece na lista para você conferir e tentar novamente. Arquivos já apagados não têm Desfazer.","\r\n\r\nIf removal was interrupted, the project stays listed so you can review and retry. Already erased files have no Undo."),T("Não foi possível concluir","Unable to complete"),MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        finally{DeletingProject=false;if(!IsDisposed){Enabled=true;UseWaitCursor=false;}}
    }
    static Label Heading(string text){return new Label{Text=text,AutoSize=true,Font=GeneralStyle.Font(15,true),Margin=new Padding(0,0,0,10)};}
    void UpdateNotice(){if(update==null||update.IsDisposed)return;update.Visible=available!=null;if(available!=null)update.Text=T("Nova versão ","New version ")+available.Version+T(" — baixar"," — download");}
    internal GeneralProject CreateProject(bool ready){
        var preferences=store.Read();
        if(ready){
            var existing=preferences.Projects.FirstOrDefault(item=>item.TemplateId=="cyberpunk-2077");if(existing!=null)return existing;
            return store.Create("Cyberpunk 2077",GeneralModLibrary.Seed(catalog,new ModImportItem[0]),preferences.Theme,preferences.Language,!fixture&&!GeneralAppPaths.IsDevelopment?Discovery.Find().FirstOrDefault():null,"cyberpunk-2077");
        }
        return ConfigureProject("",null);
    }
    GeneralProject ImportProject(){
        using(var open=new OpenFileDialog{Title=T("Importar projeto","Import project"),Filter=T("Projeto Mod Loader|*.json","Mod Loader project|*.json")}){
            if(open.ShowDialog(this)!=DialogResult.OK)return null;
            var list=new GeneralModLibrary(Path.Combine(store.Root,"lista.json")).Import(open.FileName);
            return ConfigureProject(Path.GetFileNameWithoutExtension(open.FileName),list);
        }
    }
    GeneralProject ConfigureProject(string name,GeneralModList list){
        using(var dialog=new GeneralProjectCreationDialog(name,list)){
            if(dialog.ShowDialog(this)!=DialogResult.OK)return null;
            var preferences=store.Read();
            return store.Create(dialog.ResultName,dialog.ResultList,preferences.Theme,preferences.Language,dialog.ResultDestination,
                prepare:dialog.SaveCover);
        }
    }
    void OpenProject(GeneralProject project,string preset){
        while(project!=null){
            string action=null;
            try{
                var restored=store.Load(project);
                OnlineDiagnostics.Record("project.open",null,"Projeto aberto.",new{Template=project.TemplateId??"custom",Mods=restored.List.Mods.Count});
                using(var window=new GeneralModWindow(catalog,cache,store.DataPath(project),fixture?restored:null)){
                    window.AttachProject(store,project,preset);preset=null;
                    sharingWindow=window;
                    window.AttachSharingHome(this);
                    window.Shown+=(s,e)=>{window.Refresh();Hide();};
                    window.ShowDialog();
                    action=window.ProjectAction;
                }
                if(action==null){
                    exitApproved=true;
                    Close();
                    return;
                }
                if(action=="new"||action=="import"||action=="import-project"){ShowHome();project=action=="new"?CreateProject(false):ImportProject();}
                else project=action=="ready"?CreateProject(true):action!=null&&action.StartsWith("project/",StringComparison.Ordinal)?store.Read().Projects.FirstOrDefault(item=>item.Id==action.Substring(8)):null;
            }catch{ShowHome();throw;}
        }
        ShowHome();
    }
    internal bool InstallingTransfer {get{return sharing.Values.Any(service=>service.Installing);}}

    internal bool ConfirmTransferExit(Form owner,bool localWork=false){
        if(!localWork&&!sharing.Values.Any(service=>service.HasActiveTransfers))return true;
        return AskTransferExit(owner);
    }

    internal static bool AskTransferExit(Form owner){
        return GeneralDialog.Message(owner,T(
            "Há uma transferência ou operação em andamento. Ao fechar, transferências serão interrompidas e o Mod Loader aguardará qualquer gravação em andamento terminar.",
            "A transfer or operation is in progress. Closing interrupts transfers and lets any file writes in progress finish."),
            T("Fechar o Mod Loader","Close Mod Loader"),MessageBoxButtons.YesNo,MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2,null,null,T("Fechar","Close"),T("Continuar no app","Keep working"))==DialogResult.Yes;
    }

    void CloseHome(object sender,FormClosingEventArgs e){
        if(DeletingProject){e.Cancel=true;return;}
        if(!exitApproved&&!ConfirmTransferExit(this)){e.Cancel=true;return;}
        exitApproved=true;
        if(!InstallingTransfer)return;
        e.Cancel=true;
        if(waitingForInstallation)return;
        waitingForInstallation=true;
        CloseAfterInstallation();
    }

    async void CloseAfterInstallation(){
        while(!IsDisposed&&InstallingTransfer)await Task.Delay(200);
        if(!IsDisposed)Close();
    }

    internal void DetachSharingWindow(GeneralModWindow window){
        if(sharingWindow==window)sharingWindow=null;
    }

    Control SharingPromptOwner {
        get{
            if(sharingDialog!=null&&!sharingDialog.IsDisposed&&sharingDialog.Visible)return sharingDialog;
            var window=sharingWindow;
            return window!=null&&!window.IsDisposed?window.SharingPromptOwner:this;
        }
    }

    internal P2PService SharingService(string projectData){
        P2PService service;
        if(sharing.TryGetValue(projectData,out service))return service;
        service=new P2PService(Path.Combine(projectData,"P2P"),
            fixture?Path.Combine(store.Root,"P2P-Transfers"):P2PService.TransferStore,localOnly:fixture,offerRecovery:true);
        service.EnableInstalledReceive(catalog,projectData,fixture);
        service.ConfirmConnection=session=>GeneralP2PDialog.AskConnection(this,()=>SharingPromptOwner,service,session);
        service.ApproveUserFolders=folders=>GeneralP2PDialog.AskUserFolders(this,()=>SharingPromptOwner,folders);
        service.RegisterProject=session=>{
            if(IsDisposed||!IsHandleCreated)return false;
            return (bool)Invoke((Func<bool>)(()=>{
                var window=sharingWindow;
                if(window!=null&&!window.IsDisposed&&!window.CanApplyReceivedProject)return false;
                var received=store.CreateReceived(session,projectData,catalog);
                if(window!=null&&!window.IsDisposed)window.ReloadReceivedProject(received);
                if(Visible)Build();
                return true;
            }));
        };
        service.ChooseVersion=difference=>{
            if(IsDisposed||!IsHandleCreated)return false;
            return (bool)Invoke((Func<bool>)(()=>GeneralVersionChoiceDialog.Ask(SharingPromptOwner,difference)));
        };
        sharing.Add(projectData,service);
        return service;
    }

    async Task RestoreSharing(){
        await RestoreSharing(ReceiveData,null);
        foreach(var project in store.Read().Projects)
            await RestoreSharing(store.DataPath(project),project);
    }

    async Task RestoreSharing(string projectData,GeneralProject project){
        if(IsDisposed||!File.Exists(Path.Combine(projectData,"P2P","sessions.json")))return;
        try{
            var service=SharingService(projectData);
            if(!service.NeedsDiagnosticConnection()&&!service.SnapshotSessions().Any(s=>!s.Completed&&!s.Discarded||Directory.Exists(service.Folder(s))||s.Lifecycle&&(s.Sender?s.Recipients.Any(peer=>!peer.ReceiptClosed):!s.ReceiptClosed)||!s.Sender&&s.Completed&&!s.ListImported))return;
            if(project!=null)service.SetPreparationState(store.Load(project));
            await RecoverSharing(this,service);
            await Task.Run(()=>{
                foreach(var session in service.SnapshotSessions().Where(s=>!s.Sender&&s.Completed&&!s.ListImported))
                    service.PublishProject(session,true);
            });
            if(!IsDisposed)await Task.Run(()=>service.Start(_=>{}));
        }catch(Exception ex){
            if(!IsDisposed)GeneralDialog.Message(this,ex.Message,T("Retomada P2P","P2P recovery"),MessageBoxButtons.OK,MessageBoxIcon.Warning);
        }
    }

    internal static async Task RecoverSharing(Control owner,P2PService service){
        foreach(var session in service.SnapshotSessions().Where(s=>s.RecoveryPending)){
            if(owner.IsDisposed)return;
            using(var dialog=new GeneralDialog(T("Recebimento interrompido","Interrupted download")){RequiresDecision=true}){
                dialog.ClientSize=new Size(650,284);
                var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new Padding(24,12,24,20)};
                layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                var message=new Label{Dock=DockStyle.Fill,Text=session.Name+"\r\n\r\n"+T(
                    "O download ou a instalação foi interrompido. Preserve o arquivo para retomar pelo mesmo compartilhamento ou descarte o recebimento e reverta as alterações registradas.",
                    "The download or installation was interrupted. Keep the file to resume the same transfer, or discard it and roll back recorded changes.")};
                var actions=GeneralTableDialogLayout.Commands();
                var keep=GeneralStyle.Button(T("Preservar para depois","Keep for later"),()=>dialog.Complete(DialogResult.No),true);
                var discard=GeneralStyle.Button(T("Descartar recebimento","Discard download"),()=>dialog.Complete(DialogResult.Yes));
                actions.Controls.Add(keep);actions.Controls.Add(discard);
                layout.Controls.Add(message,0,0);layout.Controls.Add(actions,0,1);dialog.Content.Controls.Add(layout);
                dialog.AcceptButton=keep;dialog.CancelButton=keep;GeneralStyle.Apply(dialog,GeneralStyle.Dark);
                if(dialog.ShowDialog(owner)==DialogResult.Yes){
                    try{await Task.Run(()=>service.DiscardIncoming(session));}
                    catch(Exception ex){if(!owner.IsDisposed)GeneralDialog.Message(owner,ex.Message,T("Recebimento preservado","Download preserved"),MessageBoxButtons.OK,MessageBoxIcon.Warning);}
                }else await Task.Run(()=>service.PreserveInterrupted(session));
            }
        }
    }

    void ShowHome(){if(IsDisposed)return;Build();Show();Activate();}
    void ToggleHomeMenu(){
        var anchor=Controls.Find("HomeMenu",true).Single();
        menus.Toggle(this,anchor,()=>new GeneralActionsMenu(home:true,hasTemplateCopies:hasTemplateCopies,showTemplateCopies:showTemplateCopies),dialog=>Guard(()=>{
            string action=((GeneralActionsMenu)dialog).ActionId;
            if(action=="template-copies"){showTemplateCopies=!showTemplateCopies;Build();}
            else if(action=="share")ShowSharing();
            else Options(anchor);
        }));
    }

    void ShowSharing(){
        var service=SharingService(ReceiveData);
        using(var dialog=new GeneralP2PDialog(service,new GeneralModState(),catalog)){
            sharingDialog=dialog;
            try{dialog.ShowDialog(this);}
            finally{sharingDialog=null;}
        }
    }

    void Options(Control anchor){
        var preferences=store.Read();using(var dialog=new GeneralOptionsDialog(preferences.Theme,preferences.Language,preferences.DownloadFolder,cache.PreviewRetainedDownloads,switchSettings:preferences.SwitchSettings)){
            dialog.AnchorTo(anchor);if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            store.SavePreferences(dialog.ThemeValue,dialog.LanguageValue,dialog.DownloadFolder,dialog.SwitchSettings);Build();
        }
    }
    void Guard(Action action){int before=buildVersion;try{action();if(Visible&&before==buildVersion)Build();}catch(Exception ex){OnlineDiagnostics.Failure("home.action.failed",ex,"Concluir a ação na tela inicial.",stage:action.Method.Name);GeneralDialog.Message(this,ex.Message,T("Não foi possível continuar","Unable to continue"),MessageBoxButtons.OK,MessageBoxIcon.Warning);}}

}

internal sealed partial class GeneralModWindow {
    GeneralProjectStore projectStore;
    GeneralProject project;
    internal string ProjectAction {get;private set;}
    Size projectWindowSize;

    protected override void OnLoad(EventArgs e){
        if(project!=null){
            float scale=DeviceDpi/96f;
            var area=Screen.FromControl(this).WorkingArea;
            bool saved=project.WindowWidth>0&&project.WindowHeight>0;
            var preferred=saved?new Size(project.WindowWidth,project.WindowHeight):new Size(1240,1240);
            Size target=FitProjectWindow(preferred,area.Size,scale,saved);
            MinimumSize=new Size(Math.Min(MinimumSize.Width,target.Width),Math.Min(MinimumSize.Height,target.Height));
            Size=projectWindowSize=target;
            Location=new Point(area.Left+(area.Width-Width)/2,area.Top+(area.Height-Height)/2);
        }
        base.OnLoad(e);
    }

    internal static Size FitProjectWindow(Size preferred,Size available,float scale,bool saved){
        double width=preferred.Width*scale,height=preferred.Height*scale;
        if(!saved){
            double fit=Math.Min(1,Math.Min(available.Width/width,available.Height/height));
            width*=fit;height*=fit;
        }
        return new Size((int)Math.Min(width,available.Width),(int)Math.Min(height,available.Height));
    }

    protected override void OnResizeEnd(EventArgs e){
        base.OnResizeEnd(e);
        if(project==null||WindowState!=FormWindowState.Normal||Size==projectWindowSize)return;
        try{
            float scale=96f/DeviceDpi;
            projectStore.SaveWindowSize(project,(int)Math.Round(Width*scale),(int)Math.Round(Height*scale));
            projectWindowSize=Size;
        }catch(IOException ex){OnlineDiagnostics.Failure("project.window-size.failed",ex,"Salvar o tamanho da janela.");}
        catch(UnauthorizedAccessException ex){OnlineDiagnostics.Failure("project.window-size.failed",ex,"Salvar o tamanho da janela.");}
    }
    internal void AttachProject(GeneralProjectStore store,GeneralProject project,string requestedPreset=null){
        projectStore=store;this.project=project;state.TemplateId=project.TemplateId;state.ProjectName=project.Name;var preferences=store.Read();
        bool refreshed=false;
        if(BuiltInProject){
            if(seed.Mods.Any(item=>GeneralModLibrary.IsFacialMod(item.Id))){
                if(!state.List.Categories.Contains(GeneralModLibrary.BodyCategory)){
                    state.List.Categories.Add(GeneralModLibrary.BodyCategory);
                    refreshed=true;
                }
                foreach(var item in state.List.Mods.Where(item=>GeneralModLibrary.IsFacialMod(item.Id)&&item.Category=="Aparência")){
                    item.Category=GeneralModLibrary.BodyCategory;
                    refreshed=true;
                }
            }
            var removed=new HashSet<string>(state.SuppressedModRecovery??Enumerable.Empty<string>(),StringComparer.Ordinal);
            var added=seed.Mods.Where(item=>!removed.Contains(item.Id)&&!state.List.Mods.Any(current=>current.Id==item.Id)).ToArray();
            if(added.Length!=0){
                state.List.Mods.AddRange(added);
                foreach(var category in seed.Categories)
                    if(!state.List.Categories.Contains(category))state.List.Categories.Add(category);
                foreach(var reference in seed.Presets){
                    var saved=state.List.Presets.FirstOrDefault(p=>p.Id==reference.Id);
                    if(saved!=null)saved.ModIds.AddRange(added.Where(item=>reference.ModIds.Contains(item.Id)).Select(item=>item.Id));
                }
                refreshed=true;
            }
            const string eclipse=GeneralModLibrary.EclipseTattooId;
            foreach(var reference in seed.Presets.Where(p=>!removed.Contains(eclipse)&&p.ModIds.Contains(eclipse))){
                var saved=state.List.Presets.FirstOrDefault(p=>p.Id==reference.Id);
                if(saved==null||saved.ModIds.Contains(eclipse))continue;
                saved.ModIds.Add(eclipse);
                refreshed=true;
            }
            var natural = seed.Mods.FirstOrDefault(mod => mod.Id == "hyst-ebb-rb");
            var savedBody = state.List.Mods.FirstOrDefault(mod => mod.Id == "hyst-ebb-rb");
            var savedBodyOnly = savedBody == null ? null : savedBody.Variants.FirstOrDefault(variant => variant.Id == "default");
            var naturalBodyOnly = natural == null ? null : natural.Variants.FirstOrDefault(variant => variant.Id == "default");
            if (savedBodyOnly != null && savedBodyOnly.Name == "5.0.0 · Somente corpo" && naturalBodyOnly != null &&
                savedBodyOnly.Sources.SequenceEqual(naturalBodyOnly.Sources, StringComparer.Ordinal)) {
                savedBodyOnly.Name = naturalBodyOnly.Name;
                refreshed = true;
            }
            if(GeneralModLibrary.GroupBodyOptions(state.List))refreshed=true;
        }
        if(requestedPreset!=null&&state.SelectedPreset!=requestedPreset&&state.List.Presets.Any(item=>item.Id==requestedPreset)){
            GeneralPresetProfiles.Switch(state,requestedPreset);
            refreshed=true;
        }
        if(refreshed||state.Theme!=preferences.Theme||state.Language!=preferences.Language){
            state.Theme=preferences.Theme;state.Language=preferences.Language;Persist();BuildInterface();
        }
        Text=GeneralAppPaths.WindowTitle+" — "+project.Name;
        UpdateTableTitle();
    }
    bool BuiltInProject {get{return project!=null&&!String.IsNullOrEmpty(project.TemplateId);}}
    void BackToProjects(){if(!working&&!scanning)CloseProjectFor("home");}

    void CloseProjectFor(string action){
        ProjectAction=action;
        Close();
    }
    void ProjectCommand(string action){
        if(working||scanning)return;
        if(BuiltInProject&&(action=="export"||action=="import"||action=="delete-project"))return;
        if(action=="clone"){
            if(projectStore==null||project==null||!ConfirmProjectNavigation())return;
            string initial=T("Cópia de ","Copy of ")+project.Name;initial=initial.Substring(0,Math.Min(120,initial.Length));
            using(var name=new GeneralNameDialog(T("Duplicar projeto","Duplicate project"),initial,T("A cópia usa a mesma pasta do jogo. Os arquivos dos mods não são duplicados.","The copy uses the same game folder. Mod files are not duplicated."))){
                if(name.ShowDialog(this)!=DialogResult.OK)return;
                var copy=projectStore.Duplicate(project,name.ResultName,catalog);ProjectCommand("project/"+copy.Id);
            }
            return;
        }
        if(action=="export"){
            using(var save=new SaveFileDialog{Filter=T("Projeto Mod Loader|*.json","Mod Loader project|*.json"),FileName=project==null?"projeto.json":String.Concat(project.Name.Where(c=>!Path.GetInvalidFileNameChars().Contains(c)))+".json"})
                if(save.ShowDialog(this)==DialogResult.OK)library.Export(save.FileName,state.List,state.SelectedPreset);
            return;
        }
        if(action=="delete-project"){
            if(!ConfirmProjectNavigation())return;
            if(projectStore==null||project==null)return;
            if(GeneralDialog.Message(this,T("Excluir este projeto da lista de projetos salvos? Mods instalados, registros e backups serão preservados.","Remove this project from saved projects? Installed mods, records and backups will be preserved.")+"\n\n"+project.Name,T("Excluir projeto","Delete project"),MessageBoxButtons.OKCancel,MessageBoxIcon.Warning)!=DialogResult.OK)return;
            projectStore.Delete(project);
        }
        CloseProjectFor(action);
    }
}
