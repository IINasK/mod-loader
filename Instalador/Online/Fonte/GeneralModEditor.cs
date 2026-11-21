using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModEditor : GeneralDialog, IGeneralModMessageContext {
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
    readonly TableLayoutPanel updateHistorySection = new TableLayoutPanel {
        Name="ModUpdateHistory", Dock=DockStyle.Top, AutoSize=true, ColumnCount=1,
        Margin=new Padding(3,0,3,0), Visible=false
    };
    readonly GeneralScrollView editorScroll;
    Func<GeneralModUpdateHistory.Entry[]> loadUpdateHistory;
    Func<string,bool> canConfirmUpdate;
    Action<string> confirmUpdate;
    Func<GeneralModUpdateHistory.Entry,string> findUpdateBackup;
    Func<string> readUpdateExecutable;
    Action chooseUpdateExecutable;
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

    readonly TextBox groupName = new TextBox { Name="VariantGroupName", MaxLength=300 };
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
    bool inventoryGroupChanged;
    internal bool InventoryLinksOnly {get;private set;}
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

    internal GeneralModEditor(GeneralModEntry entry, IEnumerable<string> categories, string selected, bool allowInstall, string installInfo,Func<string,Task<GeneralSourceInfo>> lookup=null, IEnumerable<GeneralModEntry> alternatives=null, GeneralModList groupList=null, bool projectDraft=false, string installationFolder=null, bool inventoryDraft=false, Func<string,Task<GeneralSourceInfo>> refreshLookup=null, Func<GeneralModVariant,IEnumerable<string>> catalogPaths=null, Func<GeneralModVariant,int,string> downloadedSource=null, bool fixedChoices=false, Func<GeneralNexusLink,NexusFilePreview[]> archivePreview=null, bool cyberpunkLayout=false, Func<GeneralModVariant,string> backupFile=null,IEnumerable<string> associatedFolders=null,IEnumerable<InventoryLinkMember> linkedInventory=null,
        Func<InventoryLinkMember,IEnumerable<string>> linkedLocations=null,Func<InventoryLinkMember,bool> unlinkInventory=null) {
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
            Id = v.Id, Name = v.Name, CompatibilityGroups = v.CompatibilityGroups.ToList(), RequiredMods = v.RequiredMods.ToList(), InstallRoutes = v.InstallRoutes.ToList(), OutputFolders=(v.OutputFolders??new List<string>()).ToList(), References=(v.References??new List<string>()).ToList(), Sources = v.Sources.ToList(), SourceInfo=v.SourceInfo==null?new Dictionary<string,GeneralSourceInfo>():v.SourceInfo.ToDictionary(pair=>pair.Key,pair=>pair.Value.SavedCopy()), CyberpunkId = v.CyberpunkId, CyberpunkVariant = v.CyberpunkVariant
        }).ToList();
        if (model.Count == 0) model.Add(new GeneralModVariant { Id = Guid.NewGuid().ToString("N"), Name = T("Principal", "Default") });
        originalSources = new HashSet<string>(model.SelectMany(variant => variant.Sources), StringComparer.Ordinal);

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
        var scroll=editorScroll=new GeneralScrollView(layout, panelContent: true, reserveBarSpace: true, fitPanelContent: true) { Name = "ModEditorScroll" };
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

        groupName.TextChanged+=(s,e)=>ValidateInputs();
        var groupField=FieldFor(groupName,false);
        groupField.Margin=Padding.Empty;
        groupTitle.Controls.Add(groupField,0,0);

        AddEditorRow(layout,groupTitle);
        variants.AccessibleName = T("Versão", "Version");
        variants.Margin = new Padding(3, 0, 3, 8);
        variants.AddChoiceLabel = T("Adicionar nova versão", "Add new version");
        variants.AddChoice = () => {
            if (model.Count >= 50) return;
            SaveCurrent();
            var variant = new GeneralModVariant {
                Id = Guid.NewGuid().ToString("N"), Name = T("Nova versão", "New version"),
                Category = current.Category, DisplayName = "", Description = "", Tags = new List<string>(), Nsfw = false
            };
            model.Add(variant);
            pickedVersion = true;
            RefreshVariants(variant.Id);
            variants.EditText();
        };
        if(!fixedChoices){
            variants.DeleteChoice = at => {
                SaveCurrent();
                var removed = model[at];
                model.RemoveAt(at);
                if (current == removed) RefreshVariants(model[0].Id);
                else SyncVariantPicker();
            };
        }
        variants.Visible=false;
        variants.TabStop=false;
        AddEditorRow(layout,presetMembershipRow);

        var sourceField=EditorField(T("Arquivo, pasta ou link *", "Archive, folder or link *"),sources);
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
        AddEditorRow(layout,referenceRows);
        AddEditorRow(layout,GeneralStyle.Button(T("Adicionar link de referência","Add reference link"),()=>{
            AddReferenceField("",null);ValidateInputs();
        }));
        assetChoiceRow=EditorField(T("Arquivo da release","Release file"),assetPicker);
        AddEditorRow(layout,assetChoiceRow);assetChoiceRow.Visible=false;
        assetPicker.SelectedIndexChanged+=(s,e)=>{
            int index=assetPicker.SelectedIndex;
            if(!changing&&availableAssets!=null&&index>=0&&index<availableAssets.Length)
                {
                    var references=referenceFields.Select(box=>box.Text.Trim()).Where(value=>value.Length>0).ToArray();
                    sources.Text=availableAssets[index].browser_download_url;
                    RestoreReferences(references);
                }
        };
        sourceInfo.Font = GeneralStyle.Font(10);
        sourceInfo.Dock = DockStyle.Top;
        sourceInfo.Margin = new Padding(3, 4, 3, 8);
        sourceInfo.TextChanged += (s, e) => sourceInfo.Visible = sourceInfo.Text.Length > 0;
        sourceInfo.Visible = false;
        AddEditorRow(layout,sourceInfo);


        AddEditorRow(layout, EditorSection(T("Identificação", "Identification"), true));
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
        archiveDestinations=new GeneralArchiveDestinations(installationFolder,backupFile==null?null:(Func<string>)(()=>backupFile(current)));
        archiveDestinations.Applied+=routes=>{current.InstallRoutes=routes;ValidateInputs();};
        archiveDestinations.DraftChanged+=ValidateInputs;
        archiveDestinations.ConfigureOutputs(()=>current==null?new List<string>():current.OutputFolders,
            folders=>{if(current!=null){current.OutputFolders=folders;ValidateInputs();}});
        AddEditorRow(layout,archiveDestinations);
        AddInventoryGroupLinks(layout,linkedInventory,linkedLocations,unlinkInventory);
        packageDetails.Add(archiveHeading);packageDetails.Add(archiveDestinations);

        if (alternatives != null) {
            var dependencyHeading=EditorSection(T("Vínculos entre mods", "Mod links"),true);
            AddEditorRow(layout,dependencyHeading);
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
            packageDetails.Add(dependencyHeading);packageDetails.Add(dependencyLabel);packageDetails.Add(dependencyChips);
        }

        installNote = LabelFor(installInfo ?? "");
        installNote.Font = validation.Font = usePending.Font = GeneralStyle.Font(10);
        if(!String.IsNullOrEmpty(installInfo))AddEditorRow(layout, installNote);
        AddEditorRow(layout, usePending);
        InitializePackageDrop(layout);
        AddAssociatedFolders(layout,associatedFolders,installationFolder);
        AddEditorRow(layout, validation);
        AddEditorRow(layout, updateHistorySection);
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
        var close = GeneralStyle.Button(T("Cancelar", "Cancel"), () => { Complete(DialogResult.Cancel); }, false);
        commands.Controls.Add(close);
        quickFill = GeneralStyle.Button(T("Preenchimento rápido", "Quick fill"), async () => await QuickFillFromSource());
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
            bool nexusSelected=!attachment&&GeneralModSourceMetadata.Supports(sources.Text.Trim());
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
        sources.TextChanged += (s, e) => { if(!changing){ResetPrimaryReference();ValidateInputs();} ScheduleSourceInfo(); ScheduleArchive(); };
        sourceDelay.Tick += async (s, e) => { sourceDelay.Stop(); await CurrentSourceInfo(); };
        archiveDelay.Tick += async (s,e) => { archiveDelay.Stop(); await LoadArchive(); };
        Shown += (s, e) => { ScheduleSourceInfo(); ScheduleArchive(); };
        Disposed += (s, e) => { sourceDelay.Dispose(); archiveDelay.Dispose(); editorTips.Dispose(); variants.Dispose(); };
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
            Name="DescriptionResizeGrip",Size=new Size(20,20),Cursor=Cursors.SizeNS,
            AccessibleName=T("Redimensionar descrição","Resize description"),TabStop=true
        };
        field.Padding=new Padding(12,8,12,30);
        field.Controls.Add(grip);
        grip.BringToFront();
        Action positionGrip=()=>grip.Location=new Point(field.ClientSize.Width-grip.Width-10,field.ClientSize.Height-grip.Height-10);
        field.Resize+=(s,e)=>positionGrip();
        positionGrip();
        grip.Paint+=(s,e)=>{
            e.Graphics.Clear(GeneralStyle.Surface);
            using(var pen=new Pen(grip.Focused?GeneralStyle.Accent:GeneralStyle.Muted))
                for(int offset=0;offset<9;offset+=4)e.Graphics.DrawLine(pen,5+offset,15,15,5+offset);
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
        if(entry.Variants.Any(v=>v.References.Count>32||v.References.Any(url=>!GeneralModLibrary.ValidReference(url))))
            return T("Confira os links de referência. Use endereços HTTPS.","Check the reference links. Use HTTPS addresses.");
        if(assetChoiceRow!=null&&assetChoiceRow.Visible&&assetPicker.SelectedIndex<0)
            return T("Escolha o arquivo da release.","Choose the release file.");
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
            Margin = new Padding(3, divider ? 24 : 0, 3, 3)
        };
        if (divider) {
            var line = new Panel { Dock = DockStyle.Top, Height = 1, Margin = new Padding(0, 0, 0, 16) };
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

    void AddInventoryGroupLinks(TableLayoutPanel layout,IEnumerable<InventoryLinkMember> members,
        Func<InventoryLinkMember,IEnumerable<string>> locations,Func<InventoryLinkMember,bool> unlink){
        var parts=members==null?new InventoryLinkMember[0]:members.ToArray();
        if(parts.Length==0||unlink==null)return;
        var title=EditorSection(T("Itens vinculados","Linked items"),false);
        AddEditorRow(layout,title);
        var linked=new TableLayoutPanel{Name="LinkedInventory",Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,Margin=new Padding(3,8,3,0)};
        linked.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        linked.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        foreach(var part in parts){
            var fields=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,Margin=new Padding(0,0,10,8)};
            fields.Controls.Add(new Label{Text=part.Name,Dock=DockStyle.Top,AutoSize=true,Font=GeneralStyle.Font(12,true),Margin=new Padding(0,0,0,3)});
            var path=new Label{Name="LinkedInventoryPath",Text=String.Join(Environment.NewLine,locations==null?part.Paths:locations(part)),
                Dock=DockStyle.Top,AutoSize=true,ForeColor=GeneralStyle.Muted,Margin=Padding.Empty};
            editorTips.SetToolTip(path,String.Join(Environment.NewLine,part.Paths));
            fields.SizeChanged+=(s,e)=>path.MaximumSize=new Size(Math.Max(1,fields.ClientSize.Width),0);
            fields.Controls.Add(path);
            Button detach=null;
            detach=GeneralStyle.Button(T("Desvincular","Unlink"),()=>{
                try{
                    if(!unlink(part))return;
                    inventoryGroupChanged=true;
                    fields.Dispose();detach.Dispose();
                    title.Visible=linked.Visible=linked.Controls.Count>0;
                    ValidateInputs();
                    ScheduleArchive();
                }catch(IOException ex){GeneralDialog.Message(this,ex.Message,T("Não foi possível desvincular","Could not unlink"));}
                catch(UnauthorizedAccessException ex){GeneralDialog.Message(this,ex.Message,T("Não foi possível desvincular","Could not unlink"));}
            });
            detach.Name="UnlinkInventoryMember";
            detach.AccessibleName=T("Desvincular ","Unlink ")+part.Name;
            detach.Anchor=AnchorStyles.Right;
            detach.Margin=new Padding(0,0,0,8);
            int row=linked.RowCount++;
            linked.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            linked.Controls.Add(fields,0,row);
            linked.Controls.Add(detach,1,row);
        }
        AddEditorRow(layout,linked);
    }

    void AddAssociatedFolders(TableLayoutPanel layout,IEnumerable<string> folders,string game){
        var paths=folders==null?null:folders.ToArray();
        if(paths==null||paths.Length==0)return;
        AddEditorRow(layout,EditorSection(T("Pastas associadas","Associated folders"),true));
        var chips=new FlowLayoutPanel{Name="AssociatedFolders",AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,
            Dock=DockStyle.Top,Margin=new Padding(3,0,3,0),WrapContents=true};
        foreach(string path in paths){
            string name=Path.GetFileName(path);
            var chip=CreateChip(name,path);
            chip.Text=name;
            chip.Click+=(s,e)=>{
                try{GeneralInventoryTree.RevealInExplorer(game,path);}
                catch(Exception ex){GeneralDialog.Message(this,ex.Message,T("Pasta indisponível","Folder unavailable"));}
            };
            chips.Controls.Add(chip);
        }
        chips.SizeChanged+=(s,e)=>{foreach(Control chip in chips.Controls)FitChip(chips,chip);};
        AddEditorRow(layout,chips);
    }

    internal void ConfigureUpdateHistory(Func<GeneralModUpdateHistory.Entry[]> load,Func<string,bool> canConfirm,
        Action<string> confirm,Func<GeneralModUpdateHistory.Entry,string> backup,Func<string> executable,Action chooseExecutable) {
        loadUpdateHistory=load;
        canConfirmUpdate=canConfirm;
        confirmUpdate=confirm;
        findUpdateBackup=backup;
        readUpdateExecutable=executable;
        chooseUpdateExecutable=chooseExecutable;
        RenderUpdateHistory();
    }

    internal void ScrollToUpdateHistory() {
        if (loadUpdateHistory==null||IsDisposed||!IsHandleCreated) return;
        BeginInvoke((Action)(()=>{
            if (IsDisposed||!Visible) return;
            RenderUpdateHistory();
            if(updateHistorySection.Visible)editorScroll.ScrollTo(Math.Max(0,updateHistorySection.Top-8));
        }));
    }

    void RenderUpdateHistory() {
        var entries=loadUpdateHistory();
        updateHistorySection.SuspendLayout();
        try {
            foreach(var control in updateHistorySection.Controls.Cast<Control>().ToArray())control.Dispose();
            updateHistorySection.RowStyles.Clear();
            updateHistorySection.RowCount=0;
            updateHistorySection.Visible=entries.Length>0;
            if(entries.Length==0)return;
            AddHistoryRow(updateHistorySection,EditorSection(T("Atualizações recentes","Recent updates"),true));

            var executable=readUpdateExecutable();
            var target=LabelFor(executable??T("Escolha o executável do jogo para acompanhar as atualizações.",
                "Choose the game executable to monitor updates."));
            target.ForeColor=GeneralStyle.Muted;
            target.Dock=DockStyle.Top;
            editorTips.SetToolTip(target,executable);
            var game=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,Margin=new Padding(0,0,0,12)};
            game.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            game.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var choose=GeneralStyle.Button(T("Escolher executável","Choose executable"),()=>{
                chooseUpdateExecutable();
                if(!IsDisposed)RenderUpdateHistory();
            });
            choose.Margin=new Padding(10,0,0,0);
            choose.Name="ChooseUpdateExecutable";
            game.Controls.Add(target,0,0);
            game.Controls.Add(choose,1,0);
            game.SizeChanged+=(sender,args)=>target.MaximumSize=new Size(Math.Max(80,game.ClientSize.Width-choose.Width-16),0);
            AddHistoryRow(updateHistorySection,game);

            foreach(var entry in entries)AddHistoryRow(updateHistorySection,HistoryEntry(entry));
            GeneralStyle.Apply(updateHistorySection,GeneralStyle.Dark);
        } finally {
            updateHistorySection.ResumeLayout(true);
        }
    }

    Control HistoryEntry(GeneralModUpdateHistory.Entry entry) {
        var item=new TableLayoutPanel {Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,Margin=new Padding(0,0,0,12)};
        string version=String.IsNullOrWhiteSpace(entry.Version)?T("Versão atualizada","Updated version"):entry.Version;
        string versions=String.IsNullOrWhiteSpace(entry.PreviousVersion)?version:entry.PreviousVersion+" → "+version;
        string date=new DateTime(entry.InstalledUtc,DateTimeKind.Utc).ToLocalTime().ToString("g");
        var title=LabelFor(versions+" · "+date);
        title.Font=GeneralStyle.Font(11,true);
        AddHistoryRow(item,title);
        string status;
        switch(entry.Status){
            case "loaded":
                status=T("Plugin carregado · aguardando confirmação","Plugin loaded · awaiting confirmation");
                break;
            case "crash":
                status=T("Falha do jogo após a atualização","Game crashed after the update");
                break;
            case "confirmed":
                status=T("Teste confirmado","Test confirmed");
                break;
            default:
                status=T("Aguardando confirmação","Awaiting confirmation");
                break;
        }
        string backup=null;
        try{backup=findUpdateBackup(entry);}
        catch(IOException){}
        catch(UnauthorizedAccessException){}
        var summary=LabelFor(status+" · "+(backup!=null?T("Backup disponível","Backup available"):
            String.IsNullOrEmpty(entry.Backup)?T("Sem backup","No backup"):T("Backup indisponível","Backup unavailable")));
        summary.ForeColor=GeneralStyle.Muted;
        AddHistoryRow(item,summary);

        var actions=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,WrapContents=true,Margin=Padding.Empty};
        if(canConfirmUpdate(entry.Id)){
            var confirm=GeneralStyle.Button(T("Confirmar que está funcionando","Confirm it is working"),()=>{
                try{confirmUpdate(entry.Id);RenderUpdateHistory();}
                catch(IOException ex){GeneralDialog.Message(this,ex.Message,T("Não foi possível confirmar","Could not confirm"));}
                catch(UnauthorizedAccessException ex){GeneralDialog.Message(this,ex.Message,T("Não foi possível confirmar","Could not confirm"));}
            },true);
            confirm.Name="ConfirmModUpdate";
            actions.Controls.Add(confirm);
        }
        if(backup!=null){
            var open=GeneralStyle.Button(T("Mostrar backup","Show backup"),()=>{
                try{
                    string available=findUpdateBackup(entry);
                    if(available==null)throw new IOException(T("Este backup está indisponível.","This backup is unavailable."));
                    GeneralInventoryTree.RevealInExplorer(Path.GetDirectoryName(available),Path.GetFileName(available));
                }catch(IOException ex){GeneralDialog.Message(this,ex.Message,T("Backup indisponível","Backup unavailable"));}
                catch(UnauthorizedAccessException ex){GeneralDialog.Message(this,ex.Message,T("Backup indisponível","Backup unavailable"));}
            });
            open.Name="OpenModUpdateBackup";
            actions.Controls.Add(open);
        }
        if(actions.Controls.Count>0)AddHistoryRow(item,actions);else actions.Dispose();
        return item;
    }

    static void AddHistoryRow(TableLayoutPanel layout,Control control) {
        int row=layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(control,0,row);
    }

    internal void ShowLocalFiles(IEnumerable<string> paths,IEnumerable<string> unread,Action<string> markSeen){
        archiveDestinations.SetLocalFiles(paths,unread,markSeen);
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
        sourceLinks[0]=NormalizeEditorSource(sources.Text.Trim());
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
        RefreshPackageDetails();
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
        if(saveVersionName&&CanRenameVersion(current))current.Name = variants.Text.Trim();
        current.DisplayName=modName.Text.Trim();
        current.Category=category.Text.Trim();
        current.Description=description.Text.Trim();
        current.References=referenceFields.Select(box=>box.Text.Trim()).Where(url=>url.Length>0&&url!=sources.Text.Trim()).Distinct().ToList();
        current.Tags=selectedTags.Concat(new[]{tags.Text.Trim()}).Where(tag=>tag.Length>0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        current.Nsfw=adult;
        current.NsfwOverride=adultOverride;


        if (String.IsNullOrEmpty(current.CyberpunkId)&&current.Sources.Count>0){
            string source=NormalizeEditorSource(sources.Text.Trim());
            if(source.Length==0&&initialEntry.DetectedPaths.Count>0&&current.Id==initialEntry.DefaultVariant)
                source="installed://"+editingId+"/"+current.Id;
            current.Sources[0]=source;
        }
        if(current.SourceInfo==null)current.SourceInfo=new Dictionary<string,GeneralSourceInfo>();
        foreach(string key in current.SourceInfo.Keys.Where(key=>!current.Sources.Contains(key)).ToArray())current.SourceInfo.Remove(key);
    }

    bool CanRenameVersion(GeneralModVariant version) {
        return !fixedChoices||String.IsNullOrEmpty(version.CyberpunkId);
    }

    void SyncVariantPicker() {
        bool previous = changing;
        changing = true;
        variants.Items.Clear();
        variants.Items.AddRange(model.Select(variant => (object)variant.Name));
        groupTitle.Visible=fixedChoices||model.Count>2;
        variants.DropDownStyle=CanRenameVersion(current)?ComboBoxStyle.DropDown:ComboBoxStyle.DropDownList;

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
        archiveDestinations.RefreshFolderButton();
        LoadSource();
        changing = false;
        RefreshPackageDetails();
        ValidateInputs();ScheduleSourceInfo();ScheduleArchive();
    }

    readonly TableLayoutPanel referenceRows=new TableLayoutPanel{ColumnCount=1,AutoSize=true,Dock=DockStyle.Top,Margin=Padding.Empty};
    readonly List<TextBox> referenceFields=new List<TextBox>();
    readonly GeneralPicker assetPicker=new GeneralPicker{Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList};
    Control assetChoiceRow;
    GeneralUpdateChecker.Asset[] availableAssets;
    TextBox counterpart;
    bool referenceEdited;

    void RemoveReference(TextBox box){
        var row=(Control)box.Tag;
        referenceFields.Remove(box);referenceRows.Controls.Remove(row);row.Dispose();
    }
    TextBox AddReferenceField(string url,string title){
        if(referenceFields.Count>=32)return null;
        var row=new TableLayoutPanel{ColumnCount=2,Dock=DockStyle.Top,AutoSize=true,Margin=Padding.Empty};
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var box=new TextBox{Text=url,Dock=DockStyle.Fill,MaxLength=4096,Tag=row};
        row.Controls.Add(EditorField(title??T("Link de referência","Reference link"),box),0,0);
        if(title==null)row.Controls.Add(GeneralStyle.Button("×",()=>{RemoveReference(box);ValidateInputs();}),1,0);
        referenceFields.Add(box);referenceRows.Controls.Add(row);
        GeneralStyle.Apply(row,GeneralStyle.Dark);RemoveFieldBorders(row);
        box.TextChanged+=(s,e)=>{if(box==counterpart)referenceEdited=true;ValidateInputs();};
        return box;
    }
    void ResetPrimaryReference(){
        if(counterpart!=null){RemoveReference(counterpart);counterpart=null;}
        referenceEdited=false;availableAssets=null;
        if(assetChoiceRow!=null)assetChoiceRow.Visible=false;
        bool nexus=GeneralNexusLink.Parse(sources.Text.Trim())!=null;
        if(nexus||GeneralGithubLink.Parse(sources.Text.Trim())!=null)
            counterpart=AddReferenceField("",nexus?T("Link do GitHub","GitHub link"):T("Link do Nexus Mods","Nexus Mods link"));
    }
    void RestoreReferences(IEnumerable<string> references){
        var saved=(references??Enumerable.Empty<string>()).ToArray();
        foreach(var box in referenceFields.ToArray())RemoveReference(box);
        counterpart=null;ResetPrimaryReference();
        bool nexus=GeneralNexusLink.Parse(sources.Text.Trim())!=null;
        foreach(string url in saved){
            if(counterpart!=null&&counterpart.Text.Length==0&&(nexus?GeneralGithubLink.Parse(url)!=null:GeneralNexusLink.Parse(url)!=null))counterpart.Text=url;
            else AddReferenceField(url,null);
        }
    }
    void ApplyReferenceMetadata(string source,GeneralSourceInfo fresh){
        if(counterpart!=null&&!referenceEdited&&counterpart.Text.Length==0&&fresh.Reference!=null&&
            !referenceFields.Any(box=>box.Text==fresh.Reference))counterpart.Text=fresh.Reference;
        var github=GeneralGithubLink.Parse(source);
        if(github==null||github.Asset!=null||sources.ReadOnly||current.SourceInfo.Values.Any(info=>info.GithubChecked))return;
        var choices=fresh.ReleaseChoices;
        if(choices==null||choices.Length==0)return;
        if(choices.Length==1){availableAssets=choices;return;}
        bool old=changing;changing=true;
        try{
            availableAssets=choices;assetPicker.Items.Clear();
            assetPicker.Items.AddRange(choices.Select(asset=>(object)(asset.name+" · "+fresh.ModVersion+" · "+asset.size.ToString("N0")+" B")));
            assetPicker.SelectedIndex=-1;assetChoiceRow.Visible=true;
        }finally{changing=old;}
        ValidateInputs();
    }
    void LoadSource(){
        bool previous=changing;changing=true;
        if(current.Sources.Count==0)current.Sources.Add("");
        string source=current.Sources[0];
        sources.Text=initialEntry.DetectedPaths.Count>0&&GeneralModDownload.IsInstalledSource(source)?"":source;
        sources.ReadOnly=attachment||!String.IsNullOrEmpty(current.CyberpunkId);
        RestoreReferences(current.References);
        changing=previous;
        if(!changing){ValidateInputs();ScheduleSourceInfo();ScheduleArchive();}
    }

    string SourceLinkWarning(string source,GeneralModVariant version){
        if(originalSources.Contains(source)||initialEntry.DetectedPaths.Count>0&&source=="installed://"+editingId+"/"+initialEntry.DefaultVariant)return "";
        if(GeneralModDownload.IsArchivePath(source)||GeneralModFolderSource.IsSource(source))return "";
        var link=GeneralNexusLink.Parse(source);
        if(link==null)return GeneralModDownload.IsWebSource(source)?"":T("Selecione um pacote, uma pasta ou um link HTTPS.","Choose an archive, a folder or an HTTPS link.");
        if(link.File==0)return T("No Nexus, escolha o arquivo e cole o link da tela Slow Download ou Premium Download.",
            "On Nexus, choose the file and paste the link to the Slow Download or Premium Download screen.");
        GeneralNexusLink origin;choiceLinks.TryGetValue(version.Id,out origin);
        if(origin!=null&&(link.Mod!=origin.Mod||link.Game!=origin.Game))
            return T("Use um link do mesmo mod no Nexus.","Use a link to the same Nexus mod.");
        return "";
    }

    GeneralModEntry Draft(string id) {
        SaveCurrent();
        var primary=fixedChoices?current:model[0];
        return new GeneralModEntry {
            Id=id, Name=attachment?modName.Text.Trim():(fixedChoices||model.Count>2)?groupName.Text.Trim():primary.DisplayName??initialEntry.Name,
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
            try{ModOutputIndex.NormalizeFolders(variant.OutputFolders);}
            catch(IOException){return T("Informe pastas de saída relativas e separadas por |.","Enter relative output folders separated by |.");}
            try { foreach (string source in variant.Sources) GeneralModLibrary.ValidateSource(source); }
            catch (Exception) {
                return T("Use um link HTTPS, um ZIP, RAR, 7z ou uma pasta local.", "Use an HTTPS link, a ZIP, RAR, 7z or a local folder.");
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
        save.Enabled=error.Length==0&&!finishing&&(changed||inventoryGroupChanged);
        install.Enabled=save.Enabled&&current!=null&&!GeneralModLibrary.NeedsDependencyVersion(draft,current)&&
            !(draft.DetectedPaths.Count>0&&current.Sources.Any(GeneralModDownload.IsInstalledSource));
        if (current != null && CanRenameVersion(current) && variants.SelectedIndex >= 0)
            variants.Items[variants.SelectedIndex] = current.Name;
    }

    bool InventoryDraftChanged(GeneralModEntry draft) {
        return draft.Name!=initialEntry.Name||draft.Category!=(initialEntry.Category??"")||
            draft.Description!=(initialEntry.Description??"")||draft.Nsfw!=initialEntry.Nsfw||
            !draft.Tags.SequenceEqual(initialEntry.Tags)||NewCategories.Count>0||
            draft.Variants.Any(version=>routeDrafts.ContainsKey(version.Id)&&
                !GeneralInstallRoutes.Same(version.InstallRoutes,GeneralArchiveDestinations.ReadRoutes(installationFolder,routeDrafts[version.Id])))||
            draft.Variants.Count!=initialEntry.Variants.Count||draft.Variants.Zip(initialEntry.Variants,(after,before)=>
                !after.References.SequenceEqual(before.References??new List<string>())||after.Name!=before.Name||!after.Sources.SequenceEqual(before.Sources)||!after.RequiredMods.SequenceEqual(before.RequiredMods)||
                !(after.OutputFolders??new List<string>()).SequenceEqual(before.OutputFolders??new List<string>())||
                !GeneralInstallRoutes.Same(after.InstallRoutes,before.InstallRoutes)).Any(changed=>changed);
    }

    async void Finish(string id, bool requested) {
        if(finishing)return;
        GeneralModEntry draft = Draft(id);
        string error = CurrentValidation(draft);
        if (error.Length != 0) { validation.Text = error; return; }
        if(inventoryDraft&&!InventoryDraftChanged(draft)){
            if(inventoryGroupChanged){
                InventoryLinksOnly=true;
                Complete(DialogResult.OK);
            }
            return;
        }
        if(!attachment&&lookup!=null&&GeneralModSourceMetadata.Supports(sources.Text.Trim())){
            int generation=sourceGeneration;
            sourceDelay.Stop();finishing=true;ValidateInputs();
            try{await CurrentSourceInfo();}
            finally{finishing=false;if(!IsDisposed)ValidateInputs();}
            if(IsDisposed||DialogResult==DialogResult.Cancel||generation!=sourceGeneration)return;
            draft=Draft(id);
            error=CurrentValidation(draft);
            if(error.Length!=0){validation.Text=error;return;}
        }
        if(availableAssets!=null&&availableAssets.Length==1&&!sources.ReadOnly&&GeneralGithubLink.Parse(sources.Text.Trim())!=null){
            var selected=draft.Variants.Single(v=>v.Id==current.Id);
            GeneralSourceInfo metadata;selected.SourceInfo.TryGetValue(selected.Sources[0],out metadata);
            selected.Sources[0]=availableAssets[0].browser_download_url;
            if(metadata!=null)selected.SourceInfo[selected.Sources[0]]=metadata;
            foreach(var old in selected.SourceInfo.Keys.Where(key=>!selected.Sources.Contains(key)).ToArray())selected.SourceInfo.Remove(old);
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
        Complete(DialogResult.OK);
    }
}

internal sealed partial class GeneralArchiveDestinations : TableLayoutPanel {
    readonly string game;
    readonly ListBox tree=new ListBox {
        Name="ArchiveFileTree",Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,DrawMode=DrawMode.OwnerDrawFixed,
        ItemHeight=28,Font=GeneralStyle.Font(11),DisplayMember="Text"
    };
    readonly TextBox destination=new TextBox { Name="ArchiveCurrentDestination",MaxLength=4096 };
    readonly Label feedback=GeneralModEditor.LabelFor("");
    readonly GeneralScrollView scroll;
    readonly Button reset, cancel, apply, chooseFolder;
    readonly TreeNode root=new TreeNode(T("Raiz do pacote","Archive root")){Tag=""};
    readonly TreeNode localRoot=new TreeNode(T("Arquivos locais","Local files")){Tag=""};
    readonly HashSet<TreeNode> folded=new HashSet<TreeNode>();
    readonly HashSet<string> newLocalFiles=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    Action<string> markLocalFileSeen;
    readonly Dictionary<string,TreeNode> nodes=new Dictionary<string,TreeNode>(StringComparer.OrdinalIgnoreCase);
    List<GeneralInstallRoute> accepted=new List<GeneralInstallRoute>();
    Dictionary<string,string> drafts=RouteDraft(new GeneralInstallRoute[0]);
    GeneralArchivePreview preview;
    string archiveFeedback="";
    bool updating;
    internal event Action<List<GeneralInstallRoute>> Applied;
    internal event Action DraftChanged;

    readonly Func<string> backupFile;

    internal GeneralArchiveDestinations(string game,Func<string> backupFile=null){
        this.game=game;
        this.backupFile=backupFile;
        Name="ArchiveDestinations";
        Dock=DockStyle.Fill;AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;
        ColumnCount=1;RowCount=4;Margin=new Padding(3,0,3,6);
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        tree.AccessibleName=T("Árvore de destinos dos arquivos","File destinations tree");
        scroll=new GeneralScrollView(tree);
        var frame=new GeneralSurface{Dock=DockStyle.Fill,Height=242,Padding=new Padding(9),Margin=new Padding(0,0,0,5)};
        frame.Controls.Add(scroll);
        Controls.Add(frame,0,1);
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        RowStyles.Add(new RowStyle(SizeType.Absolute,247));
        for(int row=2;row<RowCount;row++)RowStyles.Add(new RowStyle(SizeType.AutoSize));
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
        tree.MouseClick+=(s,e)=>{
            if(e.Button!=MouseButtons.Left)return;
            int index=tree.IndexFromPoint(e.Location);
            if(index>=0)MarkSeen((TreeNode)tree.Items[index]);
        };
        tree.MouseMove+=(s,e)=>{
            if(e.Button!=MouseButtons.Left)return;
            Size distance=SystemInformation.DragSize;
            var threshold=new Rectangle(dragStart.X-distance.Width/2,dragStart.Y-distance.Height/2,distance.Width,distance.Height);
            int index=tree.IndexFromPoint(dragStart);
            if(index<0||threshold.Contains(e.Location))return;
            var node=(TreeNode)tree.Items[index];
            if(!IsLocalNode(node)&&!IsVirtualNode(node)&&node!=root)tree.DoDragDrop(node,DragDropEffects.Move);
        };
        tree.KeyDown+=(s,e)=>{
            var node=Selected;
            if(node==null)return;
            if(IsLocalNode(node)&&node.Nodes.Count==0&&(e.KeyCode==Keys.Enter||e.KeyCode==Keys.Space)){
                MarkSeen(node);e.SuppressKeyPress=true;return;
            }
            if(node.Nodes.Count==0)return;
            if(e.KeyCode==Keys.Left&&!folded.Contains(node)||e.KeyCode==Keys.Right&&folded.Contains(node)){
                Toggle(node);e.Handled=true;
            }
        };

        string location=T("Localização atual","Current location");
        destination.AccessibleName=location;
        destination.AllowDrop=true;
        destination.DragEnter+=(s,e)=>{
            var node=e.Data.GetData(typeof(TreeNode)) as TreeNode;
            e.Effect=node!=null&&!IsLocalNode(node)&&!IsVirtualNode(node)?DragDropEffects.Move:DragDropEffects.None;
        };
        destination.DragDrop+=(s,e)=>{
            var node=e.Data.GetData(typeof(TreeNode)) as TreeNode;
            if(node==null||IsLocalNode(node)||IsVirtualNode(node)||!tree.Items.Contains(node))return;
            string folder=String.IsNullOrWhiteSpace(destination.Text)?"%GAME%":destination.Text;
            MovePackageNode(node,folder);
        };
        destination.TextChanged+=(s,e)=>{
            if(updating||Selected==null||IsLocalNode(Selected)||IsVirtualNode(Selected))return;
            drafts[SelectedPath]=destination.Text;
            UpdateButtons();
            NotifyDraftChanged();
        };
        var locationRow=GeneralOptionsDialog.FolderRow(location,destination,Choose);
        chooseFolder=(Button)locationRow.GetControlFromPosition(1,1);
        locationRow.Margin=new Padding(0,4,0,0);
        Controls.Add(locationRow,0,0);
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
            drafts.Remove(SelectedPath);RefreshVisualPlacement(Selected);NotifyDraftChanged();
        });
        reset.Name="ResetArchiveDestination";
        buttons.Controls.AddRange(new Control[]{apply,cancel,reset});
        Controls.Add(buttons,0,3);
        InitializeTreeActions();
        var tips=new ToolTip { UseAnimation=false,UseFading=false };
        Disposed+=(s,e)=>tips.Dispose();
        tips.SetToolTip(reset,T("Restaurar a localização herdada apenas neste item.","Restore the inherited location for this item."));
        tips.SetToolTip(cancel,T("Descartar as mudanças de destino ainda não aplicadas nesta versão.","Discard unapplied destination changes for this version."));
        tips.SetToolTip(destination,T("Escolha ou cole a pasta que deve receber este item.","Choose or paste the folder that should receive this item."));
        SizeChanged+=(s,e)=>feedback.MaximumSize=new Size(Math.Max(100,ClientSize.Width-4),0);
        RebuildTree();
    }

    static string T(string pt,string en){return GeneralStyle.T(pt,en);}
    internal void RefreshFolderButton(){
        RefreshTreeActions();
    }
    TreeNode Selected {get{return tree.SelectedItem as TreeNode;}}
    string SelectedPath {get{return Selected==null?"":(string)Selected.Tag;}}

    bool IsLocalNode(TreeNode node){
        while(node.Parent!=null)node=node.Parent;
        return ReferenceEquals(node,localRoot);
    }

    string LocalDestination(TreeNode node){
        string path=(string)node.Tag;
        return path.Length==0?game:Safe.In(game,path);
    }

    internal void SetLocalFiles(IEnumerable<string> paths,IEnumerable<string> unread,Action<string> markSeen){
        markLocalFileSeen=markSeen;
        folded.RemoveWhere(IsLocalNode);
        localRoot.Nodes.Clear();
        newLocalFiles.Clear();
        newLocalFiles.UnionWith(unread);
        BuildBranch(localRoot,new Dictionary<string,TreeNode>(StringComparer.OrdinalIgnoreCase),paths);
        folded.Add(localRoot);
        RefreshTree(root);
    }

    void MarkSeen(TreeNode node){
        string path=(string)node.Tag;
        if(!IsLocalNode(node)||!newLocalFiles.Contains(path))return;
        try{
            markLocalFileSeen(path);
            newLocalFiles.Remove(path);
            tree.AccessibleDescription=null;
            tree.Invalidate();
        }catch(IOException ex){feedback.Text=ex.Message;}
         catch(UnauthorizedAccessException ex){feedback.Text=ex.Message;}
    }

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
        archiveFeedback=value==null?T("Lendo a estrutura do pacote…","Reading the archive layout…"):
            value.NeedsFileSelection?T("Escolha um arquivo no Nexus e cole o link com file_id.","Choose a file on Nexus and paste its link with file_id."):
            value.Paths.Length==0?T("Prévia indisponível. Você ainda pode escolher a raiz.","Preview unavailable. You can still choose the root."):
            value.Origin==GeneralArchivePreviewOrigin.Local?
                T("Estrutura do arquivo local. Selecione um item para indicar seu destino.","Local archive layout. Select an item to set its destination."):
                T("Selecione uma pasta ou um arquivo para ajustar seu destino.","Select a folder or file to adjust its destination.");
        RebuildTree();
    }

    internal void ShowError(string message){
        ShowPreview(null);
        archiveFeedback=message;
        if(Selected==null||!IsLocalNode(Selected))feedback.Text=message;
    }

    void RebuildTree(){
        var selected=Selected;
        bool selectedVirtual=selected!=null&&IsVirtualNode(selected);
        root.Nodes.Clear();
        ClearVirtualFolders();
        folded.RemoveWhere(node=>!IsLocalNode(node));
        nodes.Clear();nodes.Add("",root);
        var paths=preview==null||preview.Paths.Length==0?drafts.Keys.AsEnumerable():preview.Paths;
        BuildBranch(root,nodes,paths);
        CapturePackageFolders();
        ApplyVisualPlacement();
        if(selectedVirtual)selected=root;
        else if(selected!=null&&!IsLocalNode(selected)){
            TreeNode replacement;
            nodes.TryGetValue((string)selected.Tag,out replacement);
            selected=replacement;
        }
        RefreshTree(selected??root);
    }

    static void BuildBranch(TreeNode branch,Dictionary<string,TreeNode> nodes,IEnumerable<string> paths){
        foreach(string path in paths.Where(path=>!String.IsNullOrEmpty(path)).OrderBy(path=>path,StringComparer.OrdinalIgnoreCase)){
            TreeNode parent=branch;
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
            if(localRoot.Nodes.Count>0)AddBranch(localRoot);
            AddBranch(root);
            if(virtualRoot.Nodes.Count>0)AddBranch(virtualRoot);
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
        GeneralStyle.PaintChoice(args.Graphics,args.Bounds,(args.State&DrawItemState.Selected)!=0||ReferenceEquals(node,routeDropTarget));
        int x=Indent(node),y=args.Bounds.Top+(args.Bounds.Height-16)/2;
        if(node.Parent!=null){
            int joint=x-7,mid=args.Bounds.Top+args.Bounds.Height/2;
            using(var pen=new Pen(GeneralStyle.Line)){
                args.Graphics.DrawLine(pen,joint,args.Bounds.Top,joint,node.NextNode==null?mid:args.Bounds.Bottom);
                args.Graphics.DrawLine(pen,joint,mid,x-1,mid);
            }
        }
        if(node.Nodes.Count>0)LucideIcons.Draw(args.Graphics,folded.Contains(node)?"right":"down",new Rectangle(x,y,16,16),GeneralStyle.Muted);
        var flags=TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix;
        var label=new Rectangle(x+23,args.Bounds.Top,Math.Max(0,args.Bounds.Right-x-29),args.Bounds.Height);
        if(IsLocalNode(node)&&newLocalFiles.Contains((string)node.Tag)){
            string badge=T("Novo","New");
            var badgeFlags=TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPadding;
            int width=TextRenderer.MeasureText(args.Graphics,badge,tree.Font,Size.Empty,badgeFlags).Width+4;
            if(label.Width>width+12){
                label.Width-=width+8;
                TextRenderer.DrawText(args.Graphics,badge,tree.Font,
                    new Rectangle(label.Right+8,label.Y,width,label.Height),GeneralStyle.Accent,badgeFlags);
            }
        }
        TextRenderer.DrawText(args.Graphics,node.Text,tree.Font,label,GeneralStyle.Foreground,flags);
    }

    string SuggestedFolder(TreeNode node){
        string path=(string)node.Tag;
        if(path.Length==0)return "%GAME%";
        var inherited=drafts.Where(pair=>!String.IsNullOrWhiteSpace(pair.Value)&&(pair.Key.Length==0||
            path.StartsWith(pair.Key+"/",StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(pair=>pair.Key.Length).FirstOrDefault();
        if(inherited.Value!=null){
            string remainder=path.Substring(inherited.Key.Length).TrimStart('/');
            if(!FolderNode(node))remainder=ParentPath(remainder);
            var baseFolder=ReadRoutes(game,new Dictionary<string,string>{{"",inherited.Value}})[0].Destination;
            return Under(baseFolder,remainder);
        }
        if(preview==null)return "";
        if(!FolderNode(node)){
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
        bool local=IsLocalNode(node),virtualNode=IsVirtualNode(node),refreshFolder=destination.ReadOnly!=(local||virtualNode);
        updating=true;
        try{
            destination.ReadOnly=local||virtualNode;
            feedback.Text=local?T("Localização no jogo.","Location in the game."):virtualNode?
                T("Destino escolhido. Os caminhos de origem do pacote são preservados.","Chosen destination. The archive's original paths are preserved."):archiveFeedback;
            chooseFolder.Enabled=!local&&!virtualNode;
            tree.AccessibleDescription=local&&newLocalFiles.Contains(SelectedPath)
                ?T("Novo arquivo local detectado","New local file detected"):null;
            string text;
            if(virtualNode)text=NodeFolder(node);
            else if(local)text=LocalDestination(node);
            else if(!drafts.TryGetValue(SelectedPath,out text)||String.IsNullOrWhiteSpace(text))text=SuggestedFolder(node);
            if(String.IsNullOrWhiteSpace(text))text=DefaultFolder(node);
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
        if(refreshFolder)RefreshFolderButton();
    }

    void UpdateButtons(){
        string draft;
        reset.Enabled=Selected!=null&&!IsLocalNode(Selected)&&!IsVirtualNode(Selected)&&drafts.TryGetValue(SelectedPath,out draft)&&!String.IsNullOrWhiteSpace(draft);
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
            RefreshVisualPlacement(Selected);
        }catch(IOException ex){feedback.Text=ex.Message;}
         catch(ArgumentException ex){feedback.Text=ex.Message;}
         catch(NotSupportedException ex){feedback.Text=ex.Message;}
    }

    void CancelDestinations(){
        drafts.Clear();
        foreach(var route in accepted)drafts.Add(route.Folder,route.Destination);
        RefreshVisualPlacement(Selected);
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
        var commands = GeneralTableDialogLayout.Commands();
        var save = GeneralStyle.Button(GeneralStyle.T("Salvar", "Save"), () => {
            ResultName = name.Text.Trim();
            ResultDescription = description.Text.Trim();
            Complete(DialogResult.OK);
        }, true);
        var cancel = GeneralStyle.Button(GeneralStyle.T("Cancelar", "Cancel"), () => { Complete(DialogResult.Cancel); }, false);
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
        var commands = GeneralTableDialogLayout.Commands();
        var save = GeneralStyle.Button(GeneralStyle.T("Salvar", "Save"), () => {
            Result = new GeneralModPreset {
                Id = preset == null ? Guid.NewGuid().ToString("N") : preset.Id,
                Name = name.Text.Trim(), ModIds = choices.CheckedIndices.Cast<int>().Select(index => entries[index].Id).ToList(),
                Categories = preset == null ? new List<string>() : new List<string>(preset.Categories)
            };
            Complete(DialogResult.OK);
        }, true);
        var cancel = GeneralStyle.Button(GeneralStyle.T("Cancelar", "Cancel"), () => { Complete(DialogResult.Cancel); }, false);
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
        var edit = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin=new Padding(0,8,0,0) };
        edit.Controls.Add(GeneralStyle.Button(GeneralStyle.T("Adicionar", "Add"), () => Change(false, false), false));
        edit.Controls.Add(GeneralStyle.Button(GeneralStyle.T("Renomear", "Rename"), () => Change(true, false), false));
        edit.Controls.Add(GeneralStyle.Button(GeneralStyle.T("Excluir", "Delete"), () => Change(true, true), false));
        layout.Controls.Add(edit, 0, 2);
        var commands = GeneralTableDialogLayout.Commands();
        var save = GeneralStyle.Button(GeneralStyle.T("Salvar", "Save"), () => { Complete(DialogResult.OK); }, true);
        var cancel = GeneralStyle.Button(GeneralStyle.T("Cancelar", "Cancel"), () => { Complete(DialogResult.Cancel); }, false);
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


