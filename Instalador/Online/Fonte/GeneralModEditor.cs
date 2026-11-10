using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModEditor : GeneralDialog {
    readonly TextBox modName = new TextBox { Dock = DockStyle.Fill, MaxLength = 300 };
    readonly GeneralPicker category = new GeneralPicker { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown, MaxLength = 120 };
    const int MaxTags = 32, TagHeight = 32;
    readonly TextBox tags = new TextBox { Name = "TagInput", Dock = DockStyle.Fill, MaxLength = 60 };
    readonly GeneralSurface tagEditor;
    readonly GeneralButton addTagButton;
    readonly FlowLayoutPanel tagChips = new FlowLayoutPanel {
        Name = "TagChips", Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Margin = new Padding(3, 0, 3, 3)
    };
    readonly List<string> selectedTags = new List<string>();
    readonly TextBox description = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, MaxLength = 4000 };
    readonly GeneralButton nsfw;
    bool adult;
    bool? adultOverride;
    readonly CheckBox usePending = new CheckBox { AutoSize = true, Visible = false, Dock = DockStyle.Fill, UseMnemonic = false };
    readonly GeneralPicker variants = new GeneralPicker {
        Name = "DownloadVersion", Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown, MaxLength = 300
    };
    readonly TextBox sources = new TextBox { Name = "DownloadSource", Dock = DockStyle.Fill, MaxLength = 4096 };

    readonly List<string> legacyGroups;
    readonly GeneralArchiveDestinations archiveDestinations;
    readonly Control archiveHeading;
    readonly Dictionary<string,Dictionary<string,string>> routeDrafts = new Dictionary<string,Dictionary<string,string>>();
    readonly string installationFolder;
    readonly Timer archiveDelay=new Timer{Interval=400};
    readonly Func<GeneralNexusLink,NexusFilePreview[]> archivePreview;
    readonly bool cyberpunkLayout;
    int archiveGeneration;
    readonly Func<GeneralModVariant,IEnumerable<string>> catalogPaths;
    readonly Func<GeneralModVariant,int,string> downloadedSource;
    readonly FlowLayoutPanel dependencyChips = new FlowLayoutPanel {
        Name = "DependencyChips", Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Margin = new Padding(3, 0, 3, 3)
    };
    readonly GeneralButton addDependency;
    readonly Button quickFill;
    readonly Func<string,Task<GeneralSourceInfo>> refreshLookup;
    readonly Button copySource;
    readonly HashSet<string> originalSources;
    readonly GeneralNexusLink sourceMod;
    readonly TextBox groupName = new TextBox { Name="VariantGroupName", ReadOnly=true, MaxLength=300 };
    readonly Label validation = new Label { AutoSize = true, Dock = DockStyle.Fill, UseMnemonic = false };
    readonly Label installNote;
    readonly Label fileDetails=new Label{Name="DownloadFileDetails",Dock=DockStyle.Fill,AutoEllipsis=true,UseMnemonic=false,TextAlign=ContentAlignment.MiddleLeft};
    readonly Label sourceInfo=new Label{Name="SourceMetadata",AutoSize=true,Dock=DockStyle.Fill,UseMnemonic=false};
    readonly Timer sourceDelay=new Timer{Interval=650};
    readonly Func<string,Task<GeneralSourceInfo>> lookup;
    readonly string dependencySource;
    readonly int dependencyFile;
    string lastAutoName;
    int sourceGeneration, queriedGeneration = -1;
    Task sourceQuery;
    bool finishing;
    readonly GeneralModList groupList;
    readonly Button save, install;
    readonly ToolTip editorTips = new ToolTip { UseAnimation = false, UseFading = false };
    readonly List<GeneralModVariant> model;
    readonly string editingId;
    readonly GeneralModEntry initialEntry;
    readonly bool inventoryDraft;
    readonly bool startedWithDefault;
    readonly bool fixedChoices;
    readonly TableLayoutPanel groupTitle;
    readonly Dictionary<string,GeneralNexusLink> choiceLinks;
    GeneralModVariant current;
    string categoryFrom;
    bool pickedVersion, changing, attachment, returningFromList;
    protected override bool AnimateAppearance { get { return !returningFromList; } }
    internal GeneralModEntry Result { get; private set; }
    internal string SelectedVariant { get; private set; }
    internal bool InstallRequested { get; private set; }
    internal bool AttachRequested { get; private set; }
    internal string CategoryRenameFrom { get { return categoryFrom; } }
    internal readonly Dictionary<string,string> NewCategories = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);

    internal GeneralModEditor(GeneralModEntry entry, IEnumerable<string> categories, string selected, bool allowInstall, string installInfo,Func<string,Task<GeneralSourceInfo>> lookup=null, IEnumerable<GeneralModEntry> alternatives=null, GeneralModList groupList=null, bool projectDraft=false, string installationFolder=null, bool inventoryDraft=false, Func<string,Task<GeneralSourceInfo>> refreshLookup=null, Func<GeneralModVariant,IEnumerable<string>> catalogPaths=null, Func<GeneralModVariant,int,string> downloadedSource=null, bool fixedChoices=false, Func<GeneralNexusLink,NexusFilePreview[]> archivePreview=null, bool cyberpunkLayout=false) {
        this.lookup=lookup;
        this.fixedChoices=fixedChoices;
        this.archivePreview=archivePreview;
        this.cyberpunkLayout=cyberpunkLayout;
        this.catalogPaths=catalogPaths;
        this.downloadedSource=downloadedSource;
        this.refreshLookup=refreshLookup??lookup;
        editingId=entry.Id;
        initialEntry=entry;
        this.inventoryDraft=inventoryDraft;
        this.installationFolder=installationFolder;
        dependencySource=entry.DependencySource;dependencyFile=entry.DependencyFile;
        this.groupList=groupList??new GeneralModList();
        legacyGroups=entry.CompatibilityGroups.ToList();
        adultOverride=entry.NsfwOverride;
        model = entry.Variants.Select(v => new GeneralModVariant {
            DisplayName=v.DisplayName,Category=v.Category,Description=v.Description,Tags=v.Tags==null?null:v.Tags.ToList(),
            Nsfw=v.Nsfw,NsfwOverride=v.NsfwOverride,NaturalOption=v.NaturalOption,
            Id = v.Id, Name = v.Name, CompatibilityGroups = v.CompatibilityGroups.ToList(), RequiredMods = v.RequiredMods.ToList(), InstallRoutes = v.InstallRoutes.ToList(), Sources = v.Sources.ToList(), SourceInfo=v.SourceInfo==null?new Dictionary<string,GeneralSourceInfo>():v.SourceInfo.ToDictionary(pair=>pair.Key,pair=>pair.Value.SavedCopy()), CyberpunkId = v.CyberpunkId, CyberpunkVariant = v.CyberpunkVariant
        }).ToList();
        if (model.Count == 0) model.Add(new GeneralModVariant { Id = Guid.NewGuid().ToString("N"), Name = T("Principal", "Default") });
        originalSources = new HashSet<string>(model.SelectMany(variant => variant.Sources), StringComparer.Ordinal);
        sourceMod = model.Select(variant => GeneralNexusLink.Parse(variant.Sources.FirstOrDefault())).FirstOrDefault(link => link != null);
        choiceLinks=model.ToDictionary(version=>version.Id,version=>GeneralNexusLink.Parse(version.Sources.FirstOrDefault()),StringComparer.Ordinal);
        int savedDefault = model.FindIndex(variant => variant.Id == entry.DefaultVariant);
        if (savedDefault > 0&&!fixedChoices) {
            var preferred = model[savedDefault];
            model.RemoveAt(savedDefault);
            model.Insert(0, preferred);
        }
        startedWithDefault = (fixedChoices?entry.DefaultVariant:model[0].Id) == selected;
        Text = T("Detalhes do mod", "Mod details");
        Font = GeneralStyle.Font(11, false);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(780, 760);
        MinimumSize = new Size(620, 440);
        var layout = new TableLayoutPanel {
            ColumnCount = 2,
            Padding = new Padding(20, 4, 20, 14)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var scroll = new GeneralScrollView(layout, panelContent: true, reserveBarSpace: true, fitPanelContent: true) { Name = "ModEditorScroll" };
        shell.Controls.Add(scroll, 0, 0);
        Controls.Add(shell);

        modName.Text = entry.Name;
        category.Items.AddRange(categories.Cast<object>().ToArray());
        category.Text = entry.Category ?? "";
        categoryFrom = entry.Category;
        category.SelectedIndexChanged += (s, e) => { categoryFrom = category.Text; };
        category.AddChoiceLabel = T("Criar categoria", "Create category");
        category.AddChoice = CreateCategory;
        description.Text = entry.Description ?? "";
        nsfw=(GeneralButton)GeneralStyle.Button("NSFW",()=>{adultOverride=!adult;SetAdult(adultOverride.Value);});
        nsfw.Name="NsfwToggle";nsfw.Bare=true;nsfw.AutoSize=false;
        nsfw.Margin=Padding.Empty;nsfw.Dock=DockStyle.Fill;
        nsfw.Font=GeneralStyle.Font(11,true);
        editorTips.SetToolTip(nsfw,T("Conteúdo adulto","Not Safe for Work"));
        SetAdult(adultOverride??entry.Nsfw);

        groupTitle=new TableLayoutPanel { Name="VariantGroupTitle",Dock=DockStyle.Fill,AutoSize=true,Margin=new Padding(3,0,3,3),ColumnCount=1,RowCount=1 };
        groupTitle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        groupName.Text=initialEntry.Name;
        groupName.AccessibleName=T("Nome do grupo","Group name");
        editorTips.SetToolTip(groupName,T("Dê dois cliques para editar o nome do grupo.","Double-click to edit the group name."));
        groupName.DoubleClick+=(s,e)=>{groupName.ReadOnly=false;groupName.SelectAll();};
        groupName.KeyDown+=(s,e)=>{
            if(e.KeyCode==Keys.F2){groupName.ReadOnly=false;groupName.SelectAll();e.SuppressKeyPress=true;}
            if(e.KeyCode==Keys.Enter&&!groupName.ReadOnly){groupName.ReadOnly=true;e.SuppressKeyPress=true;}
        };
        groupName.Leave+=(s,e)=>groupName.ReadOnly=true;
        groupName.TextChanged+=(s,e)=>ValidateInputs();
        var groupField=FieldFor(groupName,false);
        groupField.Margin=Padding.Empty;
        groupTitle.Controls.Add(groupField,0,0);

        AddEditorRow(layout,groupTitle);
        variants.AccessibleName = fixedChoices?T("Versão do corpo","Body version"):T("Fonte · nome da versão", "Source · version name");
        variants.Margin = new Padding(3, 0, 3, 8);
        if(fixedChoices)variants.DropDownStyle=ComboBoxStyle.DropDownList;
        else{
            variants.AddChoiceLabel = T("Adicionar fonte", "Add source");
            variants.AddChoice = () => {
                if (model.Count >= 50) return;
                SaveCurrent();
                var variant = new GeneralModVariant { Id = Guid.NewGuid().ToString("N"), Name = T("Nova versão", "New version") };
                model.Add(variant);
                pickedVersion = true;
                RefreshVariants(variant.Id);
                variants.EditText();
            };
            variants.DeleteChoice = at => {
                SaveCurrent();
                var removed = model[at];
                model.RemoveAt(at);
                if (current == removed) RefreshVariants(model[0].Id);
                else SyncVariantPicker();
            };
        }
        AddEditorRow(layout, EditorField(variants.AccessibleName+" *", variants));

        var sourceField=EditorField(T("Link do mod no Nexus *", "Nexus mod link *"),sources);
        var sourceSurface=(GeneralSurface)sourceField.Controls[1];
        sourceSurface.Padding=new Padding(18,4,18,4);
        var sourceLayout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};
        sourceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        sourceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,44));
        sourceSurface.Controls.Remove(sources);
        sources.Margin=new Padding(2,9,4,0);
        sourceLayout.Controls.Add(sources,0,0);
        sourceSurface.Controls.Add(sourceLayout);
        copySource=GeneralStyle.Button("",()=>{
            if(!String.IsNullOrWhiteSpace(sources.Text))Clipboard.SetText(sources.Text.Trim());
        });
        copySource.Name="CopyDownloadSource";
        copySource.AccessibleName=T("Copiar link ou caminho","Copy link or path");
        var copyIcon=(GeneralButton)copySource;
        copyIcon.IconKind="copy";copyIcon.Bare=true;copyIcon.AutoSize=false;
        copySource.MinimumSize=copySource.Size=new Size(44,44);
        copySource.Padding=Padding.Empty;
        sourceSurface.MinimumSize=new Size(100,52);
        copySource.Margin=Padding.Empty;copySource.Dock=DockStyle.Fill;
        sourceLayout.Controls.Add(copySource,1,0);
        editorTips.SetToolTip(copySource,copySource.AccessibleName);
        AddEditorRow(layout,sourceField);
        sourceInfo.Font = GeneralStyle.Font(10);
        sourceInfo.Dock = DockStyle.Top;
        sourceInfo.Margin = new Padding(3, 4, 3, 8);
        sourceInfo.TextChanged += (s, e) => sourceInfo.Visible = sourceInfo.Text.Length > 0;
        sourceInfo.Visible = false;
        AddEditorRow(layout,sourceInfo);


        AddEditorRow(layout, EditorSection(T("Identificação", "Identification"), false));
        var nameField=EditorField(T("Nome do mod *", "Mod name *"),modName);
        var nameSurface=(GeneralSurface)nameField.Controls[1];
        var nameRow=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};
        nameSurface.Padding=new Padding(14,4,14,4);
        nameSurface.AutoSize=false;
        modName.Font=GeneralStyle.Font(11);
        modName.Dock=DockStyle.None;
        nsfw.Dock=DockStyle.None;
        nameRow.Controls.Add(modName);
        nameRow.Controls.Add(nsfw);
        nameRow.Layout+=(s,e)=>LayoutInlineAction(nameRow,modName,nsfw,
            TextRenderer.MeasureText(nsfw.Text,nsfw.Font,Size.Empty,TextFormatFlags.SingleLine).Width+24);
        nameSurface.Controls.Add(nameRow);
        AddEditorRow(layout, nameField, EditorField(T("Categoria", "Category"), category));
        var tagLabel = LabelFor(T("Tags", "Tags"));
        tagLabel.Margin = new Padding(3, 4, 3, 3);
        tags.AccessibleName = T("Nova tag", "New tag");
        tagEditor = new GeneralSurface {
            Name = "InlineTagEditor", Size = new Size(190, TagHeight), MinimumSize = new Size(80, TagHeight),
            Padding = new Padding(12, 7, 12, 6),
            Margin = new Padding(0, 0, 6, 4), Visible = false, TabIndex = MaxTags
        };
        tagEditor.Controls.Add(tags);
        addTagButton = (GeneralButton)GeneralStyle.Button(T("Adicionar", "Add"), () => {
            addTagButton.Visible = false;
            tagEditor.Visible = true;
            tagChips.PerformLayout();
            tags.Focus();
        });
        addTagButton.Name = "AddTag";
        addTagButton.AccessibleName = T("Adicionar tag", "Add tag");
        addTagButton.IconKind = "plus";
        addTagButton.Margin = new Padding(0, 0, 6, 4);
        addTagButton.TabIndex = MaxTags + 1;
        tagChips.Controls.Add(tagEditor);
        tagChips.Controls.Add(addTagButton);
        AddEditorRow(layout, tagLabel);
        AddEditorRow(layout, tagChips);
        tagChips.SizeChanged += (s, e) => {
            foreach (Control chip in tagChips.Controls) FitTagChip(chip);
        };
        tags.PreviewKeyDown += (s, e) => {
            if (e.KeyData == Keys.Enter || e.KeyData == Keys.Escape) e.IsInputKey = true;
        };
        tags.KeyDown += (s, e) => {
            if (e.KeyData != Keys.Enter && e.KeyData != Keys.Escape) return;
            e.SuppressKeyPress = true;
            string tag = tags.Text.Trim();
            if (e.KeyData == Keys.Escape || tag.Length == 0 ||
                selectedTags.Contains(tag, StringComparer.OrdinalIgnoreCase) || AddTag(tag)) {
                CloseTagInput();
                if (!addTagButton.Focus()) SelectNextControl(tagChips, true, true, true, false);
            }
        };
        var descriptionField=EditorField(T("Descrição", "Description"),description,96);
        AddDescriptionGrip((GeneralSurface)descriptionField.Controls[1]);
        AddEditorRow(layout,descriptionField);

        archiveHeading=EditorSection(T("Destinos dos arquivos","File destinations"),true);
        AddEditorRow(layout,archiveHeading);
        archiveDestinations=new GeneralArchiveDestinations(installationFolder);
        archiveDestinations.Applied+=routes=>{current.InstallRoutes=routes;ValidateInputs();};
        archiveDestinations.DraftChanged+=ValidateInputs;
        AddEditorRow(layout,archiveDestinations);

        if (alternatives != null) {
            AddEditorRow(layout, EditorSection(T("Vínculos entre mods", "Mod links"), true));
            var dependencyLabel = LabelFor(T("Dependências desta versão", "Dependencies for this version"));
            dependencyLabel.Margin = new Padding(3, 4, 3, 3);
            dependencyChips.AccessibleName = dependencyLabel.Text;
            addDependency = (GeneralButton)GeneralStyle.Button(T("+ Vincular dependência", "+ Link dependency"), BeginLink);
            addDependency.Name = "LinkDependency";
            addDependency.Margin = new Padding(0, 0, 6, 4);
            editorTips.SetToolTip(addDependency,T(
                "O mod vinculado precisa estar ativo para esta versão funcionar.",
                "The linked mod must be active for this version to work."));
            dependencyChips.SizeChanged += (s,e) => {
                foreach(Control chip in dependencyChips.Controls) FitChip(dependencyChips, chip);
            };
            AddEditorRow(layout, dependencyLabel);
            AddEditorRow(layout, dependencyChips);
            dependencyChips.Controls.Add(addDependency);
        }

        installNote = LabelFor(installInfo ?? "");
        installNote.Font = validation.Font = usePending.Font = GeneralStyle.Font(10);
        AddEditorRow(layout, installNote);
        AddEditorRow(layout, usePending);
        AddEditorRow(layout, validation);
        layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.SizeChanged += (s, e) => {
            ResizeFeedback(layout.ClientSize.Width-layout.Padding.Horizontal);
            sourceInfo.MaximumSize=new Size(Math.Max(1,layout.ClientSize.Width-layout.Padding.Horizontal-sourceInfo.Margin.Horizontal),0);
        };
        foreach (var field in new Control[] { modName, category, tags, description, nsfw, variants, sources, usePending, addTagButton, addDependency }) {
            if (field == null) continue;
            field.Enter += (s, e) => {
                if (!Visible) return;
                var area = scroll.RectangleToClient(field.RectangleToScreen(field.ClientRectangle));
                if (area.Bottom > scroll.ClientSize.Height) scroll.ScrollTo(-layout.Top + area.Bottom - scroll.ClientSize.Height + 8);
                else if (area.Top < 0) scroll.ScrollTo(Math.Max(0, -layout.Top + area.Top - 8));
            };
        }
        var commands = new FlowLayoutPanel {
            Dock=DockStyle.Fill,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=false,
            FlowDirection=FlowDirection.RightToLeft,Margin=Padding.Empty,Padding=new Padding(8,8,20,18)
        };
        install = GeneralStyle.Button(T("Instalar", "Install"), () => Finish(entry.Id, true), true);
        install.Visible = allowInstall;
        commands.Controls.Add(install);
        save = GeneralStyle.Button(inventoryDraft?T("Salvar","Save"):projectDraft?T("Aplicar","Apply"):T("Salvar", "Save"),
            () => Finish(entry.Id, false), false);
        commands.Controls.Add(save);
        var close = GeneralStyle.Button(T("Cancelar", "Cancel"), () => { DialogResult = DialogResult.Cancel; }, false);
        commands.Controls.Add(close);
        quickFill = GeneralStyle.Button(T("Preenchimento rápido", "Quick fill"), async () => await QuickFillFromNexus());
        quickFill.Name = "NexusQuickFill";
        quickFill.Visible = false;
        commands.Controls.Add(quickFill);
        var footer=new TableLayoutPanel { Dock=DockStyle.Fill,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=2,RowCount=1,Margin=Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute));
        fileDetails.Font=GeneralStyle.Font(10);
        fileDetails.Margin=new Padding(23,4,0,4);
        footer.Controls.Add(fileDetails,0,0);
        footer.Controls.Add(commands,1,0);
        shell.Controls.Add(footer, 0, 1);
        AcceptButton = save;
        CancelButton = close;
        variants.SelectedIndexChanged += (s, e) => {
            if (changing || variants.SelectedIndex < 0) return;
            string next = model[variants.SelectedIndex].Id;
            SaveCurrent(false);
            pickedVersion = true;
            RefreshVariants(next);
        };

        variants.TextChanged += (s, e) => { if (!variants.SettingText) ValidateInputs(); };
        foreach (Control field in new Control[] { modName, category, tags, description, sources })
            field.TextChanged += (s, e) => ValidateInputs();
        GeneralStyle.Apply(this, GeneralStyle.Dark);


        Action sizeActions=()=>{
            bool nexusSelected=!attachment&&GeneralNexusLink.Parse(sources.Text.Trim())!=null;
            quickFill.Visible=nexusSelected;
            footer.ColumnStyles[1].Width=commands.Padding.Horizontal+commands.Controls.Cast<Control>()
                .Where(control=>(control!=install||allowInstall||attachment)&&(control!=quickFill||nexusSelected))
                .Sum(control=>Math.Max(control.MinimumSize.Width,control.GetPreferredSize(Size.Empty).Width)+control.Margin.Horizontal);
        };
        sources.TextChanged+=(s,e)=>sizeActions();
        install.TextChanged+=(s,e)=>sizeActions();
        save.TextChanged+=(s,e)=>sizeActions();
        sizeActions();
        RemoveFieldBorders(this);
        nsfw.MinimumSize=Size.Empty;
        if(addDependency!=null)addDependency.MinimumSize=new Size(44, TagHeight);
        nameRow.PerformLayout();
        SetAdult(adult);
        addTagButton.MinimumSize = new Size(44, TagHeight);
        FitTagChip(addTagButton);

        RefreshVariants(selected);
        sources.TextChanged += (s, e) => { ScheduleSourceInfo(); ScheduleArchive(); };
        sourceDelay.Tick += async (s, e) => { sourceDelay.Stop(); await CurrentSourceInfo(); };
        archiveDelay.Tick += async (s,e) => { archiveDelay.Stop(); await LoadArchive(); };
        Shown += (s, e) => { ScheduleSourceInfo(); ScheduleArchive(); };
        Disposed += (s, e) => { sourceDelay.Dispose(); archiveDelay.Dispose(); editorTips.Dispose(); };
    }

    protected override void OnLoad(EventArgs e) {
        var working = Screen.FromControl(Owner ?? this).WorkingArea;
        Height = Math.Min(Height, working.Height - 32);
        base.OnLoad(e);
    }

    void SetAdult(bool value){
        adult=value;
        nsfw.BareInk=value?GeneralStyle.Danger:GeneralStyle.Line;
        nsfw.AccessibleName="Not Safe for Work"+" · "+
            (value?T("Ativo","On"):T("Inativo","Off"));
        nsfw.Invalidate();
    }

    void BeginLink(){
        returningFromList=true;
        Complete(DialogResult.Retry);
    }

    internal void AddRequiredMod(GeneralModEntry entry){
        if(entry.Id==editingId||current.RequiredMods.Count>=32||current.RequiredMods.Contains(entry.Id))return;
        current.RequiredMods.Add(entry.Id);
        RefreshRequiredMods();
        ValidateInputs();
    }

    void RefreshRequiredMods(){
        if(addDependency==null)return;
        dependencyChips.SuspendLayout();
        try{
            foreach(var chip in dependencyChips.Controls.OfType<GeneralButton>().Where(button=>button!=addDependency).ToArray())
                chip.Dispose();
            foreach(string id in current.RequiredMods){
                var mod=groupList.Mods.FirstOrDefault(item=>item.Id==id);
                string name=mod==null?id+" · "+T("Fora da lista","Not in list"):mod.Name;
                var chip=CreateChip(name,T("Remover dependência ","Remove dependency ")+name);
                chip.Click+=(s,e)=>{
                    current.RequiredMods.Remove(id);
                    chip.Dispose();
                    UpdateDependencyAction();
                    addDependency.Focus();
                    ValidateInputs();
                };
                FitChip(dependencyChips,chip);
                dependencyChips.Controls.Add(chip);
            }
            dependencyChips.Controls.SetChildIndex(addDependency,dependencyChips.Controls.Count-1);
            UpdateDependencyAction();
        }finally{dependencyChips.ResumeLayout(true);}
    }

    void UpdateDependencyAction(){
        addDependency.Enabled=current.RequiredMods.Count<32&&groupList.Mods.Any(mod=>mod.Id!=editingId);
        FitChip(dependencyChips,addDependency);
    }

    static void LayoutInlineAction(Panel row,TextBox input,Control action,int width){
        int buttonWidth=Math.Min(row.ClientSize.Width,width);
        int inputWidth=row.ClientSize.Width-buttonWidth;
        int height=input.PreferredHeight;
        action.SetBounds(inputWidth,0,buttonWidth,row.ClientSize.Height);
        input.SetBounds(0,Math.Max(0,(row.ClientSize.Height-height)/2),inputWidth,height);
    }


    void AddDescriptionGrip(GeneralSurface field){
        var grip=new Control {
            Name="DescriptionResizeGrip",Size=new Size(24,18),Cursor=Cursors.SizeNS,
            AccessibleName=T("Redimensionar descrição","Resize description"),TabStop=true
        };
        field.Padding=new Padding(12,8,12,30);
        field.Controls.Add(grip);
        grip.BringToFront();
        Action positionGrip=()=>grip.Location=new Point(field.ClientSize.Width-grip.Width-10,field.ClientSize.Height-grip.Height-8);
        field.Resize+=(s,e)=>positionGrip();
        positionGrip();
        grip.Paint+=(s,e)=>{
            e.Graphics.Clear(GeneralStyle.Surface);
            using(var pen=new Pen(grip.Focused?GeneralStyle.Accent:GeneralStyle.Muted))
                for(int offset=0;offset<9;offset+=4)e.Graphics.DrawLine(pen,7+offset,13,17,3+offset);
        };
        grip.GotFocus+=(s,e)=>grip.Invalidate();
        grip.LostFocus+=(s,e)=>grip.Invalidate();
        Action<int> resize=height=>{
            height=Math.Max(72,Math.Min(420,height));
            field.MinimumSize=new Size(field.MinimumSize.Width,height);
            field.Height=height;
        };
        int startY=0,startHeight=0;
        grip.MouseDown+=(s,e)=>{
            if(e.Button!=MouseButtons.Left)return;
            grip.Focus();startY=Cursor.Position.Y;startHeight=field.Height;grip.Capture=true;
        };
        grip.MouseMove+=(s,e)=>{if(grip.Capture&&e.Button==MouseButtons.Left)resize(startHeight+Cursor.Position.Y-startY);};
        grip.MouseUp+=(s,e)=>{if(e.Button==MouseButtons.Left)grip.Capture=false;};
        grip.PreviewKeyDown+=(s,e)=>{if(e.KeyCode==Keys.Up||e.KeyCode==Keys.Down)e.IsInputKey=true;};
        grip.KeyDown+=(s,e)=>{
            if(e.KeyCode==Keys.Up||e.KeyCode==Keys.Down){resize(field.Height+(e.KeyCode==Keys.Down?12:-12));e.Handled=true;}
        };
    }

    string CurrentValidation(GeneralModEntry entry){
        string error=ValidationMessage(entry);
        if(error.Length!=0)return error;
        foreach(var version in entry.Variants)
            foreach(string source in version.Sources){
                error=SourceLinkWarning(source,version);
                if(error.Length!=0)return error;
            }
        foreach(var version in entry.Variants){
            Dictionary<string,string> input;
            if(!routeDrafts.TryGetValue(version.Id,out input))continue;
            try{GeneralArchiveDestinations.ReadRoutes(installationFolder,input);}
            catch(IOException ex){return ex.Message;}
            catch(ArgumentException ex){return ex.Message;}
            catch(NotSupportedException ex){return ex.Message;}
        }
        if(initialEntry.AlternativeGroup!=null&&entry.Variants.Any(version=>version.RequiredMods.Any(id=>
            groupList.Mods.Any(mod=>mod.Id==id&&mod.AlternativeGroup==initialEntry.AlternativeGroup))))
            return T("Um mod alternativo não pode ser uma dependência desta versão.","An alternative mod cannot be a dependency of this version.");
        return "";
    }

    static void AddEditorRow(TableLayoutPanel layout, Control left, Control right = null) {
        int row = layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(left, 0, row);
        if (right == null) layout.SetColumnSpan(left, 2);
        else layout.Controls.Add(right, 1, row);
    }

    static Control EditorSection(string title, bool divider) {
        var heading = new TableLayoutPanel {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1,
            Margin = new Padding(3, divider ? 16 : 0, 3, 3)
        };
        if (divider) {
            var line = new Panel { Dock = DockStyle.Top, Height = 1, Margin = new Padding(0, 0, 0, 9) };
            line.Paint += (s, e) => e.Graphics.Clear(GeneralStyle.Line);
            heading.Controls.Add(line);
        }
        heading.Controls.Add(new Label { Text = title, AutoSize = true, Font = GeneralStyle.Font(13, true), Margin = Padding.Empty });
        return heading;
    }

    void CreateCategory() {
        using(var dialog=new GeneralCategoryDetails("","",validateName:name=>
            category.Items.Cast<string>().Concat(groupList.Categories).Any(item=>String.Equals(item,name,StringComparison.OrdinalIgnoreCase))
                ? T("Já existe uma categoria com esse nome.","A category with this name already exists.") : "")){
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            NewCategories[dialog.ResultName]=dialog.ResultDescription;
            category.Items.Add(dialog.ResultName);
            category.SelectedIndex=category.Items.Count-1;
        }
    }

    void ScheduleArchive(){
        if(changing||attachment||current==null)return;
        archiveGeneration++;
        archiveDelay.Stop();
        archiveDestinations.ShowPreview(null);
        archiveDelay.Start();
    }

    async Task LoadArchive(){
        if(attachment||current==null)return;
        var version=current;
        int generation=archiveGeneration;
        var sourceLinks=version.Sources.ToArray();
        sourceLinks[0]=sources.Text.Trim();
        try{
            var known=(catalogPaths==null?Enumerable.Empty<string>():catalogPaths(version)).ToArray();
            var local=sourceLinks.Select((link,index)=>downloadedSource==null?null:downloadedSource(version,index)).ToArray();
            var preview=await Task.Run(()=>known.Length>0
                ?GeneralArchivePreview.Load(sourceLinks[0],local[0],known,cyberpunkLayout,archivePreview)
                :GeneralArchivePreview.Combine(sourceLinks.Select((link,index)=>
                    GeneralArchivePreview.Load(link,local[index],null,cyberpunkLayout,archivePreview))));
            if(!IsDisposed&&generation==archiveGeneration)archiveDestinations.ShowPreview(preview);
        }catch(Exception ex){
            if(!(ex is IOException||ex is UnauthorizedAccessException||ex is ArgumentException||ex is NotSupportedException||ex is System.Net.WebException))throw;
            if(!IsDisposed&&generation==archiveGeneration)archiveDestinations.ShowError(ex.Message);
        }
    }

    static Control EditorField(string title, Control field, int height = 0) {
        field.AccessibleName = title;
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, Margin = Padding.Empty };
        var label = LabelFor(title);
        label.Margin = new Padding(3, 4, 3, 3);
        panel.Controls.Add(label);
        Control body = field is GeneralPicker ? field : FieldFor(field, height > 0);
        body.Margin = new Padding(3, 0, 3, 3);
        if (height > 0) body.MinimumSize = new Size(100, height);
        panel.Controls.Add(body);
        return panel;
    }

    internal void ConfigureAttachment(string file) {
        attachment = true;
        quickFill.Visible = false;
        installNote.Text = T("Arquivo recebido: ", "Received file: ") + file;
        variants.Enabled = false;
        sources.ReadOnly = true;
        archiveGeneration++;
        archiveDelay.Stop();
        archiveHeading.Visible=archiveDestinations.Visible=false;
        install.Text = T("Vincular e continuar", "Attach and continue");
        install.Visible = true;
        ValidateInputs();
    }
    internal void OfferAttachment(GeneralModEntry target) {
        var independent=Draft(null);
        usePending.Text=T("Este arquivo pertence ao item aguardando: ","This file belongs to the waiting item: ")+target.Name;
        usePending.Visible=true;
        ResizeFeedback(usePending.Parent.ClientSize.Width - usePending.Parent.Padding.Horizontal);
        usePending.CheckedChanged+=(s,e)=>{
            var metadata=usePending.Checked?target:independent;
            modName.Text=metadata.Name;category.Text=metadata.Category??"";description.Text=metadata.Description??"";
            ClearTags();
            MergeSourceTags(metadata.Tags);
            adultOverride=metadata.NsfwOverride;SetAdult(adultOverride??metadata.Nsfw);
            install.Text=usePending.Checked?T("Vincular e continuar","Attach and continue"):T("Instalar","Install");
        };
    }

    static string T(string pt, string en) { return GeneralStyle.T(pt, en); }
    internal static Label LabelFor(string text) { return new Label { Text = text, AutoSize = true, UseMnemonic = false, Margin = new Padding(3, 8, 3, 6) }; }

    internal static GeneralSurface FieldFor(Control field, bool multiline) {
        var surface = new GeneralSurface {
            Dock = DockStyle.Fill,
            AutoSize = !multiline,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Size = new Size(100, multiline ? 80 : 44),
            Padding = multiline ? new Padding(12, 10, 12, 10) : new Padding(12, 11, 12, 6),
            MinimumSize = new Size(100, multiline ? 58 : 44),
            Margin = new Padding(3, 3, 3, 3)
        };
        field.Dock = DockStyle.Fill;
        surface.Controls.Add(multiline&&(field is TextBox||field is ListBox)?(Control)new GeneralScrollView(field):field);
        return surface;
    }

    internal static void RemoveFieldBorders(Control parent) {
        foreach (Control control in parent.Controls) {
            var textbox = control as TextBox;
            if (textbox != null) textbox.BorderStyle = BorderStyle.None;
            var list = control as ListBox;
            if (list != null) list.BorderStyle = BorderStyle.None;
            RemoveFieldBorders(control);
        }
    }

    void ResizeFeedback(int width) {
        var maximum = new Size(Math.Max(120, width - 8), 0);
        installNote.MaximumSize = validation.MaximumSize = usePending.MaximumSize = sourceInfo.MaximumSize = maximum;
    }

    void ClearTags(){
        foreach(var chip in tagChips.Controls.OfType<GeneralButton>().Where(button=>button!=addTagButton).ToArray())
            chip.Dispose();
        selectedTags.Clear();
        CloseTagInput();
    }

    internal void MergeSourceTags(IEnumerable<string> incoming) {
        tagChips.SuspendLayout();
        try {
            foreach (string tag in incoming ?? Enumerable.Empty<string>()) AddTag((tag ?? "").Trim());
        } finally { tagChips.ResumeLayout(true); }
        ValidateInputs();
    }

    bool AddTag(string tag) {
        if (tag.Length == 0 || tag.Length > 60 || tag.Any(Char.IsControl) || selectedTags.Count >= MaxTags ||
            selectedTags.Contains(tag, StringComparer.OrdinalIgnoreCase)) return false;

        selectedTags.Add(tag);
        var chip = CreateChip(tag, T("Remover tag ", "Remove tag ") + tag);
        chip.Click += (s, e) => {
            selectedTags.Remove(tag);
            bool wasFocused = chip.Focused;
            chip.Dispose();
            addTagButton.Enabled = true;
            if (wasFocused && !tags.Focus()) addTagButton.Focus();
            ValidateInputs();
        };
        FitTagChip(chip);
        tagChips.Controls.Add(chip);
        tagChips.Controls.SetChildIndex(chip, selectedTags.Count - 1);
        addTagButton.Enabled = selectedTags.Count < MaxTags;
        return true;
    }

    void CloseTagInput() {
        tags.Clear();
        addTagButton.Enabled = selectedTags.Count < MaxTags;
        addTagButton.Visible = true;
        tagEditor.Visible = false;
    }

    GeneralButton CreateChip(string label,string accessibleName){
        var chip=new GeneralButton {
            Text=label+" ×", AccessibleName=accessibleName, AutoSize=true, Font=tags.Font,
            TabIndex=0, MinimumSize=new Size(44,TagHeight),
            Padding=new Padding(12,0,12,0), Margin=new Padding(0,0,6,4)
        };
        editorTips.SetToolTip(chip,accessibleName);
        return chip;
    }

    void FitTagChip(Control chip) { FitChip(tagChips, chip); }

    static void FitChip(FlowLayoutPanel row,Control chip){
        if(row.ClientSize.Width>0)
            chip.MaximumSize=new Size(Math.Max(44,row.ClientSize.Width-chip.Margin.Horizontal),chip is Button?chip.MinimumSize.Height:0);
    }

    void SaveCurrent(bool saveVersionName=true) {
        if (current == null || attachment) return;
        if(saveVersionName&&!fixedChoices)current.Name = variants.Text.Trim();
        current.DisplayName=modName.Text.Trim();
        current.Category=category.Text.Trim();
        current.Description=description.Text.Trim();
        current.Tags=selectedTags.Concat(new[]{tags.Text.Trim()}).Where(tag=>tag.Length>0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        current.Nsfw=adult;
        current.NsfwOverride=adultOverride;


        if (String.IsNullOrEmpty(current.CyberpunkId)&&current.Sources.Count>0){
            string source=sources.Text.Trim();
            if(source.Length==0&&initialEntry.DetectedPaths.Count>0&&current.Id==initialEntry.DefaultVariant)
                source="installed://"+editingId+"/"+current.Id;
            current.Sources[0]=source;
        }
        if(current.SourceInfo==null)current.SourceInfo=new Dictionary<string,GeneralSourceInfo>();
        foreach(string key in current.SourceInfo.Keys.Where(key=>!current.Sources.Contains(key)).ToArray())current.SourceInfo.Remove(key);
    }

    void SyncVariantPicker() {
        bool previous = changing;
        changing = true;
        variants.Items.Clear();
        variants.Items.AddRange(model.Select(variant => (object)variant.Name));
        groupTitle.Visible=model.Count>2;

        variants.SelectedIndex = model.IndexOf(current);
        changing = previous;
    }

    void RefreshVariants(string id) {
        changing = true;
        current = model.FirstOrDefault(variant => variant.Id == id) ?? model[0];
        modName.Text=current.DisplayName??initialEntry.Name;
        category.Text=current.Category??initialEntry.Category??"";
        categoryFrom=category.Text;
        description.Text=current.Description??initialEntry.Description??"";
        lastAutoName=null;
        ClearTags();
        MergeSourceTags(current.Tags??initialEntry.Tags);
        adultOverride=current.Nsfw.HasValue?current.NsfwOverride:initialEntry.NsfwOverride;
        SetAdult(adultOverride??current.Nsfw??initialEntry.Nsfw);
        SyncVariantPicker();
        RefreshRequiredMods();
        Dictionary<string,string> pending;
        if(!routeDrafts.TryGetValue(current.Id,out pending))
            routeDrafts[current.Id]=pending=GeneralArchiveDestinations.RouteDraft(current.InstallRoutes);
        archiveDestinations.SetRoutes(current.InstallRoutes,pending);
        LoadSource();
        changing = false;
        ValidateInputs();ScheduleSourceInfo();ScheduleArchive();
    }

    void LoadSource(){
        bool previous=changing;changing=true;
        if(current.Sources.Count==0)current.Sources.Add("");
        string source=current.Sources[0];
        sources.Text=initialEntry.DetectedPaths.Count>0&&GeneralModDownload.IsInstalledSource(source)?"":source;
        sources.ReadOnly=attachment||!String.IsNullOrEmpty(current.CyberpunkId)||
            (!String.IsNullOrEmpty(sources.Text)&&GeneralNexusLink.Parse(sources.Text)==null);
        changing=previous;
        if(!changing){ValidateInputs();ScheduleSourceInfo();ScheduleArchive();}
    }

    string SourceLinkWarning(string source,GeneralModVariant version){
        if(originalSources.Contains(source)||initialEntry.DetectedPaths.Count>0&&source=="installed://"+editingId+"/"+initialEntry.DefaultVariant)return "";
        var link=GeneralNexusLink.Parse(source);
        if(link==null)return T("Cole um link do Nexus Mods.","Paste a Nexus Mods link.");
        if(link.File==0)return T("No Nexus, escolha o arquivo e cole o link da tela Slow Download ou Premium Download.",
            "On Nexus, choose the file and paste the link to the Slow Download or Premium Download screen.");
        GeneralNexusLink choiceLink;choiceLinks.TryGetValue(version.Id,out choiceLink);
        var origin=(fixedChoices?choiceLink:sourceMod)??model.Where(other=>other!=version)
            .Select(other=>GeneralNexusLink.Parse(other.Sources.FirstOrDefault())).FirstOrDefault(candidate=>candidate!=null);
        if(origin!=null&&(link.Mod!=origin.Mod||link.Game!=origin.Game))
            return T("Use um link do mesmo mod no Nexus.","Use a link to the same Nexus mod.");
        return "";
    }

    GeneralModEntry Draft(string id) {
        SaveCurrent();
        var primary=fixedChoices?current:model[0];
        return new GeneralModEntry {
            Id=id, Name=attachment?modName.Text.Trim():model.Count>2?groupName.Text.Trim():primary.DisplayName??initialEntry.Name,
            Category=attachment?category.Text.Trim():primary.Category??initialEntry.Category??"",
            Description=attachment?description.Text.Trim():primary.Description??initialEntry.Description??"",
            Tags=attachment?selectedTags.ToList():primary.Tags??initialEntry.Tags.ToList(),
            Nsfw=attachment?adult:primary.Nsfw??initialEntry.Nsfw,
            NsfwOverride=attachment?adultOverride:primary.Nsfw.HasValue?primary.NsfwOverride:initialEntry.NsfwOverride,
            DetectedPaths=initialEntry.DetectedPaths.ToList(), CompatibilityGroups=legacyGroups,
            DefaultVariant=fixedChoices?initialEntry.DefaultVariant:primary.Id, Variants=model, DependencySource=dependencySource, DependencyFile=dependencyFile
        };
    }

    internal static string ValidationMessage(GeneralModEntry entry) {
        if (String.IsNullOrWhiteSpace(entry.Name) || entry.Name.Length > 300 || entry.Name.Any(Char.IsControl))
            return T("Informe o nome do mod.", "Enter the mod name.");
        if (entry.Category != null && (entry.Category.Length > 120 || entry.Category.Any(Char.IsControl)))
            return T("A categoria aceita até 120 caracteres.", "The category can contain up to 120 characters.");
        if (entry.Description != null && entry.Description.Length > 4000)
            return T("A descrição aceita até 4.000 caracteres.", "The description can contain up to 4,000 characters.");
        if (entry.Tags != null && (entry.Tags.Count > MaxTags || entry.Tags.Any(tag => tag.Length > 60 || tag.Any(Char.IsControl))))
            return T("Use até 32 tags de 60 caracteres cada.", "Use up to 32 tags of 60 characters each.");
        try{
            GeneralModLibrary.ValidateCompatibilityGroups(entry.CompatibilityGroups);
            foreach(var version in entry.Variants)GeneralModLibrary.ValidateCompatibilityGroups(version.CompatibilityGroups);
        }catch(IOException){
            return T("Use até 12 grupos de 18 caracteres cada.","Use up to 12 groups of 18 characters each.");
        }
        foreach (var variant in entry.Variants) {
            if(variant.DisplayName!=null&&(String.IsNullOrWhiteSpace(variant.DisplayName)||variant.DisplayName.Any(Char.IsControl)))
                return T("Informe o nome do mod em cada versão.","Enter the mod name for each version.");
            if(variant.Category!=null&&variant.Category.Any(Char.IsControl))
                return T("Use uma categoria válida em cada versão.","Use a valid category for each version.");
            if (String.IsNullOrWhiteSpace(variant.Name) || variant.Name.Length > 300 || variant.Name.Any(Char.IsControl))
                return T("Informe o nome de cada variante.", "Enter a name for each variant.");
            if (variant.Sources.Count == 0 || variant.Sources.Count > 32)
                return T("Informe de 1 a 32 links ou arquivos para cada versão.", "Enter 1 to 32 links or files for each version.");
            try { foreach (string source in variant.Sources) GeneralModLibrary.ValidateSource(source); }
            catch (Exception) {
                return T("Fonte inválida. Use um link HTTPS público ou o caminho de um ZIP, RAR ou 7z.", "Invalid source. Use a public HTTPS link or a ZIP, RAR or 7z file path.");
            }
        }
        return "";
    }

    void ValidateInputs() {
        if (changing || install == null || save == null) return;
        var draft=Draft(null);
        string error = CurrentValidation(draft);
        validation.Text = error;
        validation.Visible = error.Length != 0;
        bool changed=!inventoryDraft||error.Length==0&&InventoryDraftChanged(draft);
        if(inventoryDraft)save.Text=changed?T("Incluir na lista e salvar","Add to list and save"):T("Salvar","Save");
        quickFill.Enabled=!finishing&&refreshLookup!=null&&current!=null&&
            SourceLinkWarning(sources.Text.Trim(),current).Length==0;
        save.Enabled=error.Length==0&&!finishing&&changed;
        install.Enabled=save.Enabled&&current!=null&&!GeneralModLibrary.NeedsDependencyVersion(draft,current)&&
            !(draft.DetectedPaths.Count>0&&current.Sources.Any(GeneralModDownload.IsInstalledSource));
        if (!fixedChoices && current != null && variants.SelectedIndex >= 0)
            variants.Items[variants.SelectedIndex] = current.Name;
    }

    bool InventoryDraftChanged(GeneralModEntry draft) {
        return draft.Name!=initialEntry.Name||draft.Category!=(initialEntry.Category??"")||
            draft.Description!=(initialEntry.Description??"")||draft.Nsfw!=initialEntry.Nsfw||
            !draft.Tags.SequenceEqual(initialEntry.Tags)||NewCategories.Count>0||
            draft.Variants.Any(version=>routeDrafts.ContainsKey(version.Id)&&
                !GeneralInstallRoutes.Same(version.InstallRoutes,GeneralArchiveDestinations.ReadRoutes(installationFolder,routeDrafts[version.Id])))||
            draft.Variants.Count!=initialEntry.Variants.Count||draft.Variants.Zip(initialEntry.Variants,(after,before)=>
                after.Name!=before.Name||!after.Sources.SequenceEqual(before.Sources)||!after.RequiredMods.SequenceEqual(before.RequiredMods)||!GeneralInstallRoutes.Same(after.InstallRoutes,before.InstallRoutes)).Any(changed=>changed);
    }

    async void Finish(string id, bool requested) {
        if(finishing)return;
        GeneralModEntry draft = Draft(id);
        string error = CurrentValidation(draft);
        if (error.Length != 0) { validation.Text = error; return; }
        if(inventoryDraft&&!InventoryDraftChanged(draft))return;
        if(!attachment&&lookup!=null&&GeneralNexusLink.Parse(sources.Text.Trim())!=null){
            int generation=sourceGeneration;
            sourceDelay.Stop();finishing=true;ValidateInputs();
            try{await CurrentSourceInfo();}
            finally{finishing=false;if(!IsDisposed)ValidateInputs();}
            if(IsDisposed||DialogResult==DialogResult.Cancel||generation!=sourceGeneration)return;
            draft=Draft(id);
            error=CurrentValidation(draft);
            if(error.Length!=0){validation.Text=error;return;}
        }
        foreach(var variant in draft.Variants){
            Dictionary<string,string> pending;
            if(routeDrafts.TryGetValue(variant.Id,out pending))
                variant.InstallRoutes=GeneralArchiveDestinations.ReadRoutes(installationFolder,pending);
            GeneralModLibrary.NormalizeSources(variant);
            if(dependencySource!=null&&(variant.Name=="Indefinida"||variant.Name=="Undefined")&&
                !GeneralModLibrary.NeedsDependencyVersion(draft,variant))
                variant.Name=variant.SourceInfo.Values.Select(info=>info.Version).FirstOrDefault(value=>!String.IsNullOrEmpty(value))
                    ??T("Selecionada","Selected");
        }
        Result = draft;
        SelectedVariant = !requested && startedWithDefault && !pickedVersion ? draft.DefaultVariant : current.Id;
        AttachRequested = usePending.Checked;
        InstallRequested = requested && !AttachRequested&&!GeneralModLibrary.NeedsDependencyVersion(draft,current);
        DialogResult = DialogResult.OK;
    }
}

internal sealed class GeneralArchiveDestinations : TableLayoutPanel {
    readonly string game;
    readonly ListBox tree=new ListBox {
        Name="ArchiveFileTree",Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,DrawMode=DrawMode.OwnerDrawFixed,
        ItemHeight=28,Font=GeneralStyle.Font(11),DisplayMember="Text"
    };
    readonly TextBox destination=new TextBox { Name="ArchiveCurrentDestination",MaxLength=4096 };
    readonly Label feedback=GeneralModEditor.LabelFor("");
    readonly GeneralScrollView scroll;
    readonly Button reset, cancel, apply;
    readonly TreeNode root=new TreeNode(T("Raiz do pacote","Archive root")){Tag=""};
    readonly HashSet<TreeNode> folded=new HashSet<TreeNode>();
    readonly Dictionary<string,TreeNode> nodes=new Dictionary<string,TreeNode>(StringComparer.OrdinalIgnoreCase);
    List<GeneralInstallRoute> accepted=new List<GeneralInstallRoute>();
    Dictionary<string,string> drafts=RouteDraft(new GeneralInstallRoute[0]);
    GeneralArchivePreview preview;
    bool updating;
    internal event Action<List<GeneralInstallRoute>> Applied;
    internal event Action DraftChanged;

    internal GeneralArchiveDestinations(string game){
        this.game=game;
        Name="ArchiveDestinations";
        Dock=DockStyle.Fill;AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;
        ColumnCount=1;RowCount=4;Margin=new Padding(3,0,3,6);
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        tree.AccessibleName=T("Árvore de arquivos do pacote","Archive file tree");
        scroll=new GeneralScrollView(tree);
        var frame=new GeneralSurface{Dock=DockStyle.Fill,Height=242,Padding=new Padding(9),Margin=new Padding(0,0,0,5)};
        frame.Controls.Add(scroll);
        Controls.Add(frame,0,0);
        RowStyles.Add(new RowStyle(SizeType.Absolute,247));
        for(int row=1;row<RowCount;row++)RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tree.DrawItem+=DrawNode;
        tree.SelectedIndexChanged+=(s,e)=>{if(!updating){scroll.RefreshScroll();ShowDestination();}};
        Point dragStart=Point.Empty;
        tree.MouseDown+=(s,e)=>{
            if(e.Button!=MouseButtons.Left)return;
            dragStart=e.Location;
            int index=tree.IndexFromPoint(e.Location);
            if(index<0)return;
            var node=(TreeNode)tree.Items[index];
            if(node.Nodes.Count>0&&e.X>=Indent(node)&&e.X<Indent(node)+20)Toggle(node);
        };
        tree.MouseMove+=(s,e)=>{
            if(e.Button!=MouseButtons.Left)return;
            Size distance=SystemInformation.DragSize;
            var threshold=new Rectangle(dragStart.X-distance.Width/2,dragStart.Y-distance.Height/2,distance.Width,distance.Height);
            int index=tree.IndexFromPoint(dragStart);
            if(index>=0&&!threshold.Contains(e.Location))tree.DoDragDrop(tree.Items[index],DragDropEffects.Move);
        };
        tree.KeyDown+=(s,e)=>{
            var node=Selected;
            if(node==null||node.Nodes.Count==0)return;
            if(e.KeyCode==Keys.Left&&!folded.Contains(node)||e.KeyCode==Keys.Right&&folded.Contains(node)){
                Toggle(node);e.Handled=true;
            }
        };

        string location=T("Localização atual","Current location");
        destination.AccessibleName=location;
        destination.AllowDrop=true;
        destination.DragEnter+=(s,e)=>e.Effect=e.Data.GetDataPresent(typeof(TreeNode))?DragDropEffects.Move:DragDropEffects.None;
        destination.DragDrop+=(s,e)=>{
            var node=e.Data.GetData(typeof(TreeNode)) as TreeNode;
            if(node==null||!tree.Items.Contains(node))return;
            string folder=String.IsNullOrWhiteSpace(destination.Text)?"%GAME%":destination.Text;
            tree.SelectedItem=node;
            destination.Text=folder;
        };
        destination.TextChanged+=(s,e)=>{
            if(updating||Selected==null)return;
            drafts[SelectedPath]=destination.Text;
            UpdateButtons();
            NotifyDraftChanged();
        };
        var locationRow=GeneralOptionsDialog.FolderRow(location,destination,Choose);
        locationRow.Margin=new Padding(0,4,0,0);
        Controls.Add(locationRow,0,1);
        feedback.ForeColor=GeneralStyle.Muted;
        feedback.Margin=new Padding(0,6,0,3);
        Controls.Add(feedback,0,2);

        var buttons=new FlowLayoutPanel {
            Dock=DockStyle.Fill,AutoSize=true,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,
            Margin=new Padding(0,6,0,0)
        };
        apply=GeneralStyle.Button(T("Aplicar","Apply"),ApplyDestinations,true);
        apply.Name="ApplyArchiveDestinations";
        cancel=GeneralStyle.Button(T("Cancelar","Cancel"),CancelDestinations);
        cancel.Name="CancelArchiveDestinations";
        reset=GeneralStyle.Button(T("Resetar","Reset"),()=>{
            drafts.Remove(SelectedPath);ShowDestination();NotifyDraftChanged();
        });
        reset.Name="ResetArchiveDestination";
        buttons.Controls.AddRange(new Control[]{apply,cancel,reset});
        Controls.Add(buttons,0,3);
        var tips=new ToolTip { UseAnimation=false,UseFading=false };
        Disposed+=(s,e)=>tips.Dispose();
        tips.SetToolTip(reset,T("Restaurar a localização herdada apenas neste item.","Restore the inherited location for this item."));
        tips.SetToolTip(cancel,T("Descartar as mudanças de destino ainda não aplicadas nesta versão.","Discard unapplied destination changes for this version."));
        tips.SetToolTip(destination,T("Escolha ou cole a pasta que deve receber este item.","Choose or paste the folder that should receive this item."));
        SizeChanged+=(s,e)=>feedback.MaximumSize=new Size(Math.Max(100,ClientSize.Width-4),0);
        RebuildTree();
    }

    static string T(string pt,string en){return GeneralStyle.T(pt,en);}
    TreeNode Selected {get{return tree.SelectedItem as TreeNode;}}
    string SelectedPath {get{return Selected==null?"":(string)Selected.Tag;}}

    internal static Dictionary<string,string> RouteDraft(IEnumerable<GeneralInstallRoute> routes){
        return routes.ToDictionary(route=>route.Folder,route=>route.Destination,StringComparer.OrdinalIgnoreCase);
    }

    internal static List<GeneralInstallRoute> ReadRoutes(string game,Dictionary<string,string> drafts){
        var routes=new List<GeneralInstallRoute>();
        foreach(var item in drafts.Where(pair=>!String.IsNullOrWhiteSpace(pair.Value))){
            var parsed=GeneralInstallRoutes.Parse(" = "+item.Value,game);
            if(parsed.Count!=1)throw new IOException(T("Informe uma pasta por item.","Enter one folder per item."));
            parsed[0].Folder=item.Key;
            routes.Add(parsed[0]);
        }
        GeneralInstallRoutes.Validate(routes);
        return routes;
    }

    internal void SetRoutes(IEnumerable<GeneralInstallRoute> routes,Dictionary<string,string> pending){
        accepted=routes.ToList();
        drafts=pending;
        ShowPreview(null);
    }

    internal void ShowPreview(GeneralArchivePreview value){
        preview=value;
        feedback.Text=value==null?T("Lendo a estrutura do pacote…","Reading the archive layout…"):
            value.NeedsFileSelection?T("Escolha um arquivo no Nexus e cole o link com file_id.","Choose a file on Nexus and paste its link with file_id."):
            value.Paths.Length==0?T("Prévia indisponível. Você ainda pode escolher a raiz.","Preview unavailable. You can still choose the root."):
            value.Origin==GeneralArchivePreviewOrigin.Local?
                T("Estrutura do arquivo local. Selecione um item para indicar seu destino.","Local archive layout. Select an item to set its destination."):
                T("Selecione uma pasta ou um arquivo para ajustar seu destino.","Select a folder or file to adjust its destination.");
        RebuildTree();
    }

    internal void ShowError(string message){
        ShowPreview(null);
        feedback.Text=message;
    }

    void RebuildTree(){
        string selected=SelectedPath;
        root.Nodes.Clear();folded.Clear();nodes.Clear();nodes.Add("",root);
        var paths=preview==null||preview.Paths.Length==0?drafts.Keys.AsEnumerable():preview.Paths;
        foreach(string path in paths.Where(path=>!String.IsNullOrEmpty(path)).OrderBy(path=>path,StringComparer.OrdinalIgnoreCase)){
            TreeNode parent=root;
            string relative="";
            foreach(string part in path.Split('/')){
                relative=relative.Length==0?part:relative+"/"+part;
                TreeNode node;
                if(!nodes.TryGetValue(relative,out node)){
                    node=parent.Nodes.Add(part);node.Tag=relative;nodes.Add(relative,node);
                }
                parent=node;
            }
        }
        TreeNode next;
        RefreshTree(nodes.TryGetValue(selected,out next)?next:root);
    }

    void Toggle(TreeNode node){
        if(!folded.Add(node))folded.Remove(node);
        RefreshTree(node);
    }

    void RefreshTree(TreeNode selected){
        int top=tree.TopIndex;
        updating=true;
        tree.BeginUpdate();
        try{
            tree.Items.Clear();
            AddBranch(root);
            tree.SelectedItem=selected;
            tree.TopIndex=Math.Min(top,tree.SelectedIndex);
        }finally{tree.EndUpdate();updating=false;}
        scroll.RefreshScroll();
        ShowDestination();
    }

    void AddBranch(TreeNode node){
        tree.Items.Add(node);
        if(folded.Contains(node))return;
        foreach(TreeNode child in node.Nodes)AddBranch(child);
    }

    int Indent(TreeNode node){return 6+Math.Min(node.Level*19,Math.Max(0,tree.ClientSize.Width-110));}

    void DrawNode(object sender,DrawItemEventArgs args){
        if(args.Index<0)return;
        var node=(TreeNode)tree.Items[args.Index];
        GeneralStyle.PaintChoice(args.Graphics,args.Bounds,(args.State&DrawItemState.Selected)!=0);
        int x=Indent(node),y=args.Bounds.Top+(args.Bounds.Height-16)/2;
        if(node.Parent!=null){
            int joint=x-7,mid=args.Bounds.Top+args.Bounds.Height/2;
            using(var pen=new Pen(GeneralStyle.Line)){
                args.Graphics.DrawLine(pen,joint,args.Bounds.Top,joint,node.NextNode==null?mid:args.Bounds.Bottom);
                args.Graphics.DrawLine(pen,joint,mid,x-1,mid);
            }
        }
        if(node.Nodes.Count>0)LucideIcons.Draw(args.Graphics,folded.Contains(node)?"right":"down",new Rectangle(x,y,16,16),GeneralStyle.Muted);
        TextRenderer.DrawText(args.Graphics,node.Text,tree.Font,
            new Rectangle(x+23,args.Bounds.Top,Math.Max(0,args.Bounds.Right-x-29),args.Bounds.Height),
            GeneralStyle.Foreground,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
    }

    string SuggestedFolder(TreeNode node){
        string path=(string)node.Tag;
        if(path.Length==0)return "%GAME%";
        var inherited=drafts.Where(pair=>!String.IsNullOrWhiteSpace(pair.Value)&&(pair.Key.Length==0||
            path.StartsWith(pair.Key+"/",StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(pair=>pair.Key.Length).FirstOrDefault();
        if(inherited.Value!=null){
            string remainder=path.Substring(inherited.Key.Length).TrimStart('/');
            if(node.Nodes.Count==0)remainder=ParentPath(remainder);
            var baseFolder=ReadRoutes(game,new Dictionary<string,string>{{"",inherited.Value}})[0].Destination;
            return Under(baseFolder,remainder);
        }
        if(preview==null)return "";
        if(node.Nodes.Count==0){
            string file;
            return preview.SuggestedPaths.TryGetValue(path,out file)?Under("%GAME%",ParentPath(file)):"";
        }
        string prefix=path+"/";
        string common=null;
        foreach(var file in preview.Paths.Where(file=>file.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))){
            string mapped;
            if(!preview.SuggestedPaths.TryGetValue(file,out mapped))return "";
            string remainder=file.Substring(prefix.Length);
            if(!mapped.EndsWith(remainder,StringComparison.OrdinalIgnoreCase))return "";
            string folder=mapped.Substring(0,mapped.Length-remainder.Length);
            if(folder.Length>0&&!folder.EndsWith("/",StringComparison.Ordinal))return "";
            folder=folder.TrimEnd('/');
            if(common!=null&&!String.Equals(common,folder,StringComparison.OrdinalIgnoreCase))return "";
            common=folder;
        }
        return common==null?"":Under("%GAME%",common);
    }

    static string ParentPath(string path){
        int slash=path.LastIndexOf('/');
        return slash<0?"":path.Substring(0,slash);
    }

    static string Under(string folder,string remainder){return remainder.Length==0?folder:folder+"/"+remainder;}

    void ShowDestination(){
        var node=Selected;
        if(node==null)return;
        updating=true;
        try{
            string text;
            if(!drafts.TryGetValue(SelectedPath,out text)||String.IsNullOrWhiteSpace(text))text=SuggestedFolder(node);
            if(text.StartsWith("%",StringComparison.Ordinal)){
                try{text=GeneralInstallRoutes.DirectoryFor(game,text,false);}
                catch(IOException){}
                catch(ArgumentException){}
                catch(NotSupportedException){}
            }
            destination.Text=text;
            destination.SelectionStart=0;
        }catch(IOException){destination.Text="";}
        catch(ArgumentException){destination.Text="";}
        catch(NotSupportedException){destination.Text="";}
        finally{updating=false;}
        UpdateButtons();
    }

    void UpdateButtons(){
        string draft;
        reset.Enabled=drafts.TryGetValue(SelectedPath,out draft)&&!String.IsNullOrWhiteSpace(draft);
        bool dirty;
        try{dirty=!GeneralInstallRoutes.Same(accepted,ReadRoutes(game,drafts));}
        catch(IOException){dirty=true;}
        catch(ArgumentException){dirty=true;}
        catch(NotSupportedException){dirty=true;}
        apply.Enabled=cancel.Enabled=dirty;
    }

    void Choose(){
        try{
            string initial=Directory.Exists(destination.Text)?destination.Text:game;
            string folder=WindowsFolderPicker.Choose(this,T("Destino personalizado","Custom installation folder"),initial);
            if(folder!=null)destination.Text=folder;
        }catch(IOException ex){feedback.Text=ex.Message;}
         catch(UnauthorizedAccessException ex){feedback.Text=ex.Message;}
    }

    void ApplyDestinations(){
        try{
            accepted=ReadRoutes(game,drafts);
            if(Applied!=null)Applied(accepted.ToList());
            ShowDestination();
        }catch(IOException ex){feedback.Text=ex.Message;}
         catch(ArgumentException ex){feedback.Text=ex.Message;}
         catch(NotSupportedException ex){feedback.Text=ex.Message;}
    }

    void CancelDestinations(){
        drafts.Clear();
        foreach(var route in accepted)drafts.Add(route.Folder,route.Destination);
        ShowDestination();
        NotifyDraftChanged();
    }

    void NotifyDraftChanged(){if(DraftChanged!=null)DraftChanged();}
}

internal sealed class GeneralCategoryDetails : GeneralDialog {
    readonly TextBox name = new TextBox { Dock = DockStyle.Fill, MaxLength = 120 };
    readonly TextBox description = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, MaxLength = 4000 };
    internal string ResultName { get; private set; }
    internal string ResultDescription { get; private set; }
    internal bool DeleteRequested { get; private set; }

    internal GeneralCategoryDetails(string initialName, string initialDescription, bool isUncategorized = false, bool canDelete = false, Func<string,string> validateName = null) {
        Text = GeneralStyle.T("Categoria", "Category");
        Font = GeneralStyle.Font(11, false);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(500, 390);
        MinimumSize = new Size(440, 350);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 12, 20, 18), ColumnCount = 1, RowCount = 6 };
        for (int i = 0; i < 6; i++) layout.RowStyles.Add(new RowStyle(i == 3 ? SizeType.Percent : SizeType.AutoSize, i == 3 ? 100 : 0));
        Controls.Add(layout);
        layout.Controls.Add(GeneralModEditor.LabelFor(GeneralStyle.T("Nome *", "Name *")));
        layout.Controls.Add(GeneralModEditor.FieldFor(name, false));
        layout.Controls.Add(GeneralModEditor.LabelFor(GeneralStyle.T("Descrição", "Description")));
        layout.Controls.Add(GeneralModEditor.FieldFor(description, true));
        var feedback = new Label { AutoSize = true, Dock = DockStyle.Fill, UseMnemonic = false, Font = GeneralStyle.Font(10, false), Margin = new Padding(3, 6, 3, 6) };
        layout.Controls.Add(feedback);
        var commands = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var save = GeneralStyle.Button(GeneralStyle.T("Salvar", "Save"), () => {
            ResultName = name.Text.Trim();
            ResultDescription = description.Text.Trim();
            DialogResult = DialogResult.OK;
        }, true);
        var cancel = GeneralStyle.Button(GeneralStyle.T("Cancelar", "Cancel"), () => { DialogResult = DialogResult.Cancel; }, false);
        commands.Controls.Add(save);
        commands.Controls.Add(cancel);
        if (canDelete && !isUncategorized && !String.IsNullOrWhiteSpace(initialName)) {
            var delete = (GeneralButton)GeneralStyle.Button(GeneralStyle.T("Excluir categoria", "Delete category"), () => {
                DeleteRequested = true;
                Complete(DialogResult.OK);
            });
            delete.Destructive = true;
            commands.Controls.Add(delete);
        }
        layout.Controls.Add(commands);
        Action checkName=()=>{
            string value=name.Text.Trim();
            feedback.Text=String.IsNullOrWhiteSpace(value)||value.Any(Char.IsControl)
                ? GeneralStyle.T("Informe o nome da categoria.", "Enter the category name.")
                : validateName==null?"":validateName(value);
            save.Enabled=String.IsNullOrEmpty(feedback.Text);
        };
        name.TextChanged += (s, e) => checkName();
        name.Text = initialName ?? "";
        description.Text = initialDescription ?? "";
        checkName();
        AcceptButton = save;
        CancelButton = cancel;
        GeneralStyle.Apply(this, GeneralStyle.Dark);
        GeneralModEditor.RemoveFieldBorders(this);
    }
}

internal sealed class GeneralPresetEditor : GeneralDialog {
    readonly TextBox name = new TextBox { Dock = DockStyle.Fill, MaxLength = 120 };
    internal GeneralModPreset Result { get; private set; }

    internal GeneralPresetEditor(GeneralModPreset preset, IEnumerable<GeneralModEntry> mods, IEnumerable<string> selectedIds) {
        Text = "Preset";
        Font = GeneralStyle.Font(11, false);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(530, 510);
        MinimumSize = new Size(460, 420);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        var entries = mods.ToList();
        var selected = new HashSet<string>(selectedIds ?? (preset == null ? Enumerable.Empty<string>() : preset.ModIds), StringComparer.Ordinal);
        var choices = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
        foreach (var mod in entries) choices.Items.Add(mod.Name, selected.Contains(mod.Id));
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 12, 20, 18), ColumnCount = 1, RowCount = 5 };
        for (int i = 0; i < 5; i++) layout.RowStyles.Add(new RowStyle(i == 3 ? SizeType.Percent : SizeType.AutoSize, i == 3 ? 100 : 0));
        Controls.Add(layout);
        layout.Controls.Add(GeneralModEditor.LabelFor(GeneralStyle.T("Nome do preset *", "Preset name *")));
        layout.Controls.Add(GeneralModEditor.FieldFor(name, false));
        layout.Controls.Add(GeneralModEditor.LabelFor(GeneralStyle.T("Mods incluídos", "Included mods")));
        layout.Controls.Add(GeneralModEditor.FieldFor(choices, true));
        var commands = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var save = GeneralStyle.Button(GeneralStyle.T("Salvar", "Save"), () => {
            Result = new GeneralModPreset {
                Id = preset == null ? Guid.NewGuid().ToString("N") : preset.Id,
                Name = name.Text.Trim(), ModIds = choices.CheckedIndices.Cast<int>().Select(index => entries[index].Id).ToList(),
                Categories = preset == null ? new List<string>() : new List<string>(preset.Categories)
            };
            DialogResult = DialogResult.OK;
        }, true);
        var cancel = GeneralStyle.Button(GeneralStyle.T("Cancelar", "Cancel"), () => { DialogResult = DialogResult.Cancel; }, false);
        commands.Controls.Add(save);
        commands.Controls.Add(cancel);
        layout.Controls.Add(commands);
        name.TextChanged += (s, e) => save.Enabled = !String.IsNullOrWhiteSpace(name.Text) && !name.Text.Any(Char.IsControl);
        name.Text = preset == null ? "" : preset.Name;
        save.Enabled = !String.IsNullOrWhiteSpace(name.Text);
        AcceptButton = save;
        CancelButton = cancel;
        GeneralStyle.Apply(this, GeneralStyle.Dark);
        GeneralModEditor.RemoveFieldBorders(this);
    }
}

internal sealed class GeneralCategoryEditor : GeneralDialog {
    readonly ListBox list = new ListBox { Dock = DockStyle.Fill };
    readonly TextBox name = new TextBox { Dock = DockStyle.Fill, MaxLength = 120 };
    internal readonly List<string> Categories;
    internal readonly Dictionary<string, string> Changes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    internal GeneralCategoryEditor(IEnumerable<string> categories) {
        Categories = categories.ToList();
        foreach (string category in Categories) Changes[category] = category;
        Text = GeneralStyle.T("Categorias", "Categories");
        Font = GeneralStyle.Font(11, false);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(470, 400);
        MinimumSize = new Size(430, 360);
        StartPosition = FormStartPosition.CenterParent;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 12, 20, 18), ColumnCount = 1, RowCount = 4 };
        Controls.Add(layout);
        for (int i = 0; i < 4; i++) layout.RowStyles.Add(new RowStyle(i == 0 ? SizeType.Percent : SizeType.AutoSize, i == 0 ? 100 : 0));
        layout.Controls.Add(GeneralModEditor.FieldFor(list, true), 0, 0);
        layout.Controls.Add(GeneralModEditor.FieldFor(name, false), 0, 1);
        var edit = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        edit.Controls.Add(GeneralStyle.Button(GeneralStyle.T("Adicionar", "Add"), () => Change(false, false), false));
        edit.Controls.Add(GeneralStyle.Button(GeneralStyle.T("Renomear", "Rename"), () => Change(true, false), false));
        edit.Controls.Add(GeneralStyle.Button(GeneralStyle.T("Excluir", "Delete"), () => Change(true, true), false));
        layout.Controls.Add(edit, 0, 2);
        var commands = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var save = GeneralStyle.Button(GeneralStyle.T("Salvar", "Save"), () => { DialogResult = DialogResult.OK; }, true);
        var cancel = GeneralStyle.Button(GeneralStyle.T("Cancelar", "Cancel"), () => { DialogResult = DialogResult.Cancel; }, false);
        commands.Controls.Add(save);
        commands.Controls.Add(cancel);
        layout.Controls.Add(commands, 0, 3);
        CancelButton = cancel;
        AcceptButton = save;
        list.SelectedIndexChanged += (s, e) => { if (list.SelectedItem != null) name.Text = (string)list.SelectedItem; };
        RefreshList();
        GeneralStyle.Apply(this, GeneralStyle.Dark);
        GeneralModEditor.RemoveFieldBorders(this);
    }

    void RefreshList() { list.Items.Clear(); list.Items.AddRange(Categories.Cast<object>().ToArray()); }

    void Change(bool existing, bool remove) {
        string old = list.SelectedItem as string;
        string value = remove ? "" : name.Text.Trim();
        if (existing && old == null) return;
        if (!remove && (value.Length == 0 || value.Any(Char.IsControl) || Categories.Any(item => String.Equals(item, value, StringComparison.OrdinalIgnoreCase) && item != old))) return;
        if (existing) {
            foreach (string original in Changes.Where(pair => String.Equals(pair.Value, old, StringComparison.OrdinalIgnoreCase)).Select(pair => pair.Key).ToList()) Changes[original] = value;
            int index = Categories.IndexOf(old);
            if (remove) Categories.RemoveAt(index); else Categories[index] = value;
        } else Categories.Add(value);
        RefreshList();
        if (!remove) list.SelectedItem = value;
    }
}


