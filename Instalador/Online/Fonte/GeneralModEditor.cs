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
    readonly GeneralPicker sourcePicker = new GeneralPicker { Name = "DownloadSourcePicker", Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly List<string> legacyGroups;
    readonly TextBox installRoutes = new TextBox {
        Name = "InstallationRoutes", Dock = DockStyle.Fill, MaxLength = 4096
    };
    readonly Dictionary<string,string> routeDrafts = new Dictionary<string,string>();
    readonly string installationFolder;
    readonly Button browseDestination;
    readonly FlowLayoutPanel dependencyChips = new FlowLayoutPanel {
        Name = "DependencyChips", Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Margin = new Padding(3, 0, 3, 3)
    };
    readonly GeneralButton addDependency, groupColor;
    readonly Button quickFill;
    readonly Func<string,Task<GeneralSourceInfo>> refreshLookup;
    readonly Button copySource;
    readonly HashSet<string> originalSources;
    readonly GeneralNexusLink sourceMod;
    int sourceIndex;
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
    readonly Button alternative, unlink;
    readonly TextBox groupName = new TextBox { Name = "AlternativeGroupName", MaxLength = 18 };
    readonly Control groupFields;
    readonly GeneralModList groupList;
    string groupColorValue;
    internal GeneralAlternativeGroup GroupStyle {
        get { return LinkedAlternative == null || groupFields == null ? null : new GeneralAlternativeGroup { Name = groupName.Text.Trim(), Color = groupColorValue }; }
    }
    readonly Button save, install;
    readonly ToolTip editorTips = new ToolTip { UseAnimation = false, UseFading = false };
    internal GeneralModEntry LinkedAlternative { get; private set; }
    internal bool PickingDependency { get; private set; }
    readonly List<GeneralModVariant> model;
    readonly string editingId;
    readonly GeneralModEntry initialEntry;
    readonly bool inventoryDraft;
    readonly bool startedWithDefault;
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

    internal GeneralModEditor(GeneralModEntry entry, IEnumerable<string> categories, string selected, bool allowInstall, string installInfo,Func<string,Task<GeneralSourceInfo>> lookup=null, IEnumerable<GeneralModEntry> alternatives=null, GeneralModList groupList=null, bool projectDraft=false, string installationFolder=null, bool inventoryDraft=false, Func<string,Task<GeneralSourceInfo>> refreshLookup=null) {
        this.lookup=lookup;
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
            Id = v.Id, Name = v.Name, CompatibilityGroups = v.CompatibilityGroups.ToList(), RequiredMods = v.RequiredMods.ToList(), InstallRoutes = v.InstallRoutes.ToList(), Sources = v.Sources.ToList(), SourceInfo=v.SourceInfo==null?new Dictionary<string,GeneralSourceInfo>():v.SourceInfo.ToDictionary(pair=>pair.Key,pair=>pair.Value.SavedCopy()), CyberpunkId = v.CyberpunkId, CyberpunkVariant = v.CyberpunkVariant
        }).ToList();
        if (model.Count == 0) model.Add(new GeneralModVariant { Id = Guid.NewGuid().ToString("N"), Name = T("Principal", "Default") });
        originalSources = new HashSet<string>(model.SelectMany(variant => variant.Sources), StringComparer.Ordinal);
        sourceMod = model.Select(variant => GeneralNexusLink.Parse(variant.Sources.FirstOrDefault())).FirstOrDefault(link => link != null);
        int savedDefault = model.FindIndex(variant => variant.Id == entry.DefaultVariant);
        if (savedDefault > 0) {
            var preferred = model[savedDefault];
            model.RemoveAt(savedDefault);
            model.Insert(0, preferred);
        }
        startedWithDefault = model[0].Id == selected;
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

        sourcePicker.SelectedIndexChanged+=(s,e)=>{
            if(changing||sourcePicker.SelectedIndex<0)return;
            SaveCurrent();
            LoadSource(sourcePicker.SelectedIndex);
        };
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
        AddEditorRow(layout,sourcePicker);

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

        AddEditorRow(layout, EditorSection(T("Versões para download", "Download versions"), true));
        variants.AccessibleName = T("Versão selecionada", "Selected version");
        variants.Margin = new Padding(3, 0, 3, 8);
        variants.AddChoiceLabel = T("Adicionar", "Add");
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
        AddEditorRow(layout, variants);
        string destinationLabel=T("Destino personalizado","Custom installation folder");
        installRoutes.AccessibleName=destinationLabel;
        var destinationRow=GeneralOptionsDialog.FolderRow(destinationLabel,installRoutes,BrowseInstallFolder);
        browseDestination=(Button)destinationRow.GetControlFromPosition(1,1);
        editorTips.SetToolTip(destinationRow.GetControlFromPosition(0,0),T(
            "O Cyberpunk usa os destinos do catálogo. Use este campo em projetos de outros jogos.",
            "Cyberpunk uses the catalog destinations. Use this field for other games."));
        destinationRow.Margin=new Padding(3,4,3,3);
        AddEditorRow(layout,destinationRow);
        editorTips.SetToolTip(installRoutes,T(
            "Em branco, usa a raiz do jogo. Para dividir um pacote, separe com |. Exemplo: mods = %GAME%/Mods | config = %APPDATA%/Jogo/Mods",
            "Leave blank to use the game root. Use | to split an archive. Example: mods = %GAME%/Mods | config = %APPDATA%/Game/Mods"));

        if (alternatives != null) {
            var options = alternatives.Where(other => other.Id != entry.Id).ToList();
            AddEditorRow(layout, EditorSection(T("Vínculos entre mods", "Mod links"), true));
            var dependencyLabel = LabelFor(T("Dependências desta versão", "Dependencies for this version"));
            dependencyLabel.Margin = new Padding(3, 4, 3, 3);
            dependencyChips.AccessibleName = dependencyLabel.Text;
            addDependency = (GeneralButton)GeneralStyle.Button(T("+ Vincular dependência", "+ Link dependency"), () => BeginLink(true));
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

            var groupLabel = LabelFor(T("Grupo de alternativas", "Alternative group"));
            groupLabel.Margin = new Padding(3, 10, 3, 3);
            AddEditorRow(layout, groupLabel);
            alternative = GeneralStyle.Button("", () => BeginLink(false));
            alternative.Name = "AlternativeMod";
            alternative.Dock = DockStyle.Fill;
            alternative.Enabled = options.Count > 0;
            editorTips.SetToolTip(alternative,T(
                "Apenas um dos mods vinculados pode ficar ativo por vez.",
                "Only one linked mod can be active at a time."));
            unlink = GeneralStyle.Button(T("Desvincular", "Unlink"), () => SetLinkedAlternative(null));
            unlink.Name = "UnlinkAlternative";
            var pairing = new TableLayoutPanel {
                Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty
            };
            pairing.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pairing.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pairing.Controls.Add(alternative, 0, 0);
            pairing.Controls.Add(unlink, 1, 0);
            AddEditorRow(layout, pairing);
            groupColor = (GeneralButton)GeneralStyle.Button("", ChooseGroupColor);
            groupColor.Name = "AlternativeGroupColor";
            groupColor.AccessibleName = T("Cor do grupo", "Group color");
            groupColor.Bare = true;
            groupColor.AutoSize = false;
            groupColor.Margin = Padding.Empty;
            var groupField = EditorField(T("Nome do grupo", "Group name"), groupName);
            var groupSurface = (GeneralSurface)groupField.Controls[1];
            groupSurface.Padding = new Padding(14, 4, 14, 4);
            groupSurface.AutoSize = false;
            groupSurface.Controls.Remove(groupName);
            var groupRow = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            groupName.Dock = DockStyle.None;
            groupRow.Controls.Add(groupName);
            groupRow.Controls.Add(groupColor);
            groupRow.Layout += (s,e) => LayoutInlineAction(groupRow, groupName, groupColor, 38);
            groupSurface.Controls.Add(groupRow);
            groupFields = groupField;
            AddEditorRow(layout, groupFields);
            groupName.TextChanged += (s, e) => ValidateInputs();
            SetLinkedAlternative(options.FirstOrDefault(other => entry.AlternativeGroup != null && other.AlternativeGroup == entry.AlternativeGroup));
        }

        installNote = LabelFor(installInfo ?? "");
        installNote.Font = validation.Font = usePending.Font = GeneralStyle.Font(10);
        AddEditorRow(layout, installNote);
        AddEditorRow(layout, usePending);
        AddEditorRow(layout, validation);
        layout.SizeChanged += (s, e) => {
            ResizeFeedback(layout.ClientSize.Width-layout.Padding.Horizontal);
            sourceInfo.MaximumSize=new Size(Math.Max(1,layout.ClientSize.Width-layout.Padding.Horizontal-sourceInfo.Margin.Horizontal),0);
        };
        foreach (var field in new Control[] { modName, category, tags, description, nsfw, variants, sources, alternative, unlink, groupName, groupColor, usePending, addTagButton, addDependency }) {
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
            pickedVersion = true;
            RefreshVariants(next);
        };

        variants.TextChanged += (s, e) => { if (!variants.SettingText) ValidateInputs(); };
        foreach (Control field in new Control[] { modName, category, tags, description, sources, installRoutes })
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
        if(groupColor!=null)groupColor.MinimumSize=Size.Empty;
        if(addDependency!=null)addDependency.MinimumSize=new Size(44, TagHeight);
        nameRow.PerformLayout();
        SetAdult(adult);
        addTagButton.MinimumSize = new Size(44, TagHeight);
        FitTagChip(addTagButton);
        MergeSourceTags(entry.Tags);
        RefreshVariants(selected);
        sources.TextChanged += (s, e) => ScheduleSourceInfo();
        sourceDelay.Tick += async (s, e) => { sourceDelay.Stop(); await CurrentSourceInfo(); };
        Shown += (s, e) => ScheduleSourceInfo();
        Disposed += (s, e) => { sourceDelay.Dispose(); editorTips.Dispose(); };
    }

    protected override void OnLoad(EventArgs e) {
        var working = Screen.FromControl(Owner ?? this).WorkingArea;
        Height = Math.Min(Height, working.Height - 32);
        base.OnLoad(e);
    }

    internal void SetLinkedAlternative(GeneralModEntry entry) {
        var previous=LinkedAlternative;
        LinkedAlternative = entry;
        if (alternative == null) return;
        alternative.Text = entry == null ? T("Vincular como opção", "Link as option") : entry.Name;
        alternative.AccessibleName = entry == null ? alternative.Text : T("Trocar mod vinculado: ", "Change linked mod: ") + entry.Name;
        unlink.Visible = groupFields.Visible = entry != null;
        if(entry!=null&&(previous==null||(entry.AlternativeGroup??entry.Id)!=(previous.AlternativeGroup??previous.Id))){
            GeneralAlternativeGroup style;
            groupList.AlternativeGroups.TryGetValue(entry.AlternativeGroup??"",out style);
            groupName.Text=style!=null&&style.Name!=null?style.Name:GeneralModLibrary.GroupDefaultName(entry.AlternativeGroup);
            groupColorValue=style==null?GeneralModLibrary.DefaultGroupColor(groupList,entry.Id):style.Color;
            UpdateGroupColor();
        }
        ValidateInputs();
    }

    void SetAdult(bool value){
        adult=value;
        nsfw.BareInk=value?GeneralStyle.Danger:GeneralStyle.Line;
        nsfw.AccessibleName="Not Safe for Work"+" · "+
            (value?T("Ativo","On"):T("Inativo","Off"));
        nsfw.Invalidate();
    }

    void BeginLink(bool dependency){
        PickingDependency=dependency;
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
        field.Padding=new Padding(12,8,12,18);
        field.Controls.Add(grip);
        grip.BringToFront();
        field.Resize+=(s,e)=>grip.Location=new Point(field.ClientSize.Width-grip.Width-13,field.ClientSize.Height-grip.Height-3);
        grip.Location=new Point(field.ClientSize.Width-grip.Width-13,field.ClientSize.Height-grip.Height-3);
        grip.Paint+=(s,e)=>{
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

    void ChooseGroupColor(){
        using(var chooser=new ColorDialog { Color=ColorTranslator.FromHtml(groupColorValue),FullOpen=true }){
            if(chooser.ShowDialog(this)!=DialogResult.OK)return;
            groupColorValue="#"+(chooser.Color.ToArgb()&0xffffff).ToString("X6");
            UpdateGroupColor();
            ValidateInputs();
        }
    }

    void UpdateGroupColor(){
        groupColor.ColorSwatch = String.IsNullOrEmpty(groupColorValue)?GeneralStyle.Accent:ColorTranslator.FromHtml(groupColorValue);
        editorTips.SetToolTip(groupColor,groupColor.AccessibleName+" · "+groupColorValue);
        groupColor.Invalidate();
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
            string input;
            if(!routeDrafts.TryGetValue(version.Id,out input))continue;
            try{version.InstallRoutes=GeneralInstallRoutes.Parse(input,installationFolder);}
            catch(IOException ex){return ex.Message;}
            catch(ArgumentException ex){return ex.Message;}
            catch(NotSupportedException ex){return ex.Message;}
        }
        if(LinkedAlternative!=null&&entry.Variants.Any(version=>version.RequiredMods.Any(id=>id==LinkedAlternative.Id||
            (LinkedAlternative.AlternativeGroup!=null&&groupList.Mods.Any(mod=>mod.Id==id&&mod.AlternativeGroup==LinkedAlternative.AlternativeGroup)))))
            return T("Um mod alternativo não pode ser uma dependência desta versão.","An alternative mod cannot be a dependency of this version.");
        var style=GroupStyle;
        if(style==null)return "";
        if(String.IsNullOrWhiteSpace(style.Name)||style.Name.Length>18||style.Name.Any(Char.IsControl))
            return T("Informe um nome de até 18 caracteres para o grupo.","Enter a group name of up to 18 characters.");
        if(!GeneralModLibrary.GroupColorAvailable(groupList,LinkedAlternative.AlternativeGroup,style.Color))
            return T("Esta cor já é usada por outro grupo.","This color is already used by another group.");
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

    void BrowseInstallFolder() {
        string text=installRoutes.Text;
        int caret=installRoutes.SelectionStart;
        int start=caret==0?0:text.LastIndexOf('|',caret-1)+1;
        int end=text.IndexOf('|',caret);
        if(end<0)end=text.Length;
        string rule=text.Substring(start,end-start);
        string initial=installationFolder;
        try{
            var existing=GeneralInstallRoutes.Parse(rule,installationFolder).FirstOrDefault();
            if(existing!=null)initial=GeneralInstallRoutes.DirectoryFor(installationFolder,existing.Destination,false);
        }catch(ArgumentException){}catch(IOException){}catch(NotSupportedException){}
        try{
            string folder=WindowsFolderPicker.Choose(this,T("Destino personalizado","Custom installation folder"),initial);
            if(folder==null)return;
            int equals=rule.IndexOf('=');
            string archiveFolder=equals<0?"":rule.Substring(0,equals).Trim();
            string replacement=GeneralInstallRoutes.Format(GeneralInstallRoutes.Parse(archiveFolder+" = "+folder,installationFolder));
            string prefix=text.Substring(0,start)+(start==0?"":" ");
            string suffix=(end==text.Length?"":" ")+text.Substring(end);
            installRoutes.Text=prefix+replacement+suffix;
            installRoutes.SelectionStart=prefix.Length+replacement.Length;
        }catch(Exception ex){
            GeneralDialog.Message(this,ex.Message,T("Destino personalizado","Custom installation folder"),MessageBoxButtons.OK);
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
        browseDestination.Enabled=false;
        installRoutes.ReadOnly=true;
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

    void SaveCurrent() {
        if (current == null || attachment) return;
        current.Name = variants.Text.Trim();
        if(!installRoutes.ReadOnly)routeDrafts[current.Id]=installRoutes.Text;

        if (String.IsNullOrEmpty(current.CyberpunkId)&&sourceIndex<current.Sources.Count){
            string source=sources.Text.Trim();
            if(source.Length==0&&initialEntry.DetectedPaths.Count>0&&current.Id==initialEntry.DefaultVariant)
                source="installed://"+editingId+"/"+current.Id;
            current.Sources[sourceIndex]=source;
        }
        if(current.SourceInfo==null)current.SourceInfo=new Dictionary<string,GeneralSourceInfo>();
        foreach(string key in current.SourceInfo.Keys.Where(key=>!current.Sources.Contains(key)).ToArray())current.SourceInfo.Remove(key);
    }

    void SyncVariantPicker() {
        bool previous = changing;
        changing = true;
        variants.Items.Clear();
        variants.Items.AddRange(model.Select(variant => (object)variant.Name));
        variants.SelectedIndex = model.IndexOf(current);
        changing = previous;
    }

    void RefreshVariants(string id) {
        changing = true;
        current = model.FirstOrDefault(variant => variant.Id == id) ?? model[0];
        SyncVariantPicker();
        bool customDestination=!attachment&&String.IsNullOrEmpty(current.CyberpunkId)&&
            (String.IsNullOrEmpty(installationFolder)||!File.Exists(Path.Combine(installationFolder,"bin","x64","Cyberpunk2077.exe")));
        installRoutes.ReadOnly=!customDestination;
        browseDestination.Enabled=customDestination;
        RefreshRequiredMods();
        string savedRoute;
        installRoutes.Text=routeDrafts.TryGetValue(current.Id,out savedRoute)?savedRoute:GeneralInstallRoutes.Format(current.InstallRoutes);
        if(!customDestination&&installRoutes.Text.Length==0&&!attachment)
            installRoutes.Text=T("Destino definido pelo catálogo","Destination defined by the catalog");
        LoadSource(0);
        changing = false;
        ValidateInputs();ScheduleSourceInfo();
    }

    void LoadSource(int index){
        bool previous=changing;changing=true;
        if(current.Sources.Count==0)current.Sources.Add("");
        sourceIndex=index;
        string source=current.Sources[index];
        sources.Text=initialEntry.DetectedPaths.Count>0&&GeneralModDownload.IsInstalledSource(source)?"":source;
        sources.ReadOnly=attachment||!String.IsNullOrEmpty(current.CyberpunkId)||index>0||
            (!String.IsNullOrEmpty(sources.Text)&&GeneralNexusLink.Parse(sources.Text)==null);
        sourcePicker.Items.Clear();
        for(int i=0;i<current.Sources.Count;i++)sourcePicker.Items.Add(T("Fonte ","Source ")+(i+1));
        sourcePicker.SelectedIndex=index;
        sourcePicker.Visible=current.Sources.Count>1;
        changing=previous;
        if(!changing){ValidateInputs();ScheduleSourceInfo();}
    }

    string SourceLinkWarning(string source,GeneralModVariant version){
        if(originalSources.Contains(source)||initialEntry.DetectedPaths.Count>0&&source=="installed://"+editingId+"/"+initialEntry.DefaultVariant)return "";
        var link=GeneralNexusLink.Parse(source);
        if(link==null)return T("Cole um link do Nexus Mods.","Paste a Nexus Mods link.");
        var origin=sourceMod??model.Where(other=>other!=version)
            .Select(other=>GeneralNexusLink.Parse(other.Sources.FirstOrDefault())).FirstOrDefault(candidate=>candidate!=null);
        if(origin!=null&&(link.Mod!=origin.Mod||link.Game!=origin.Game))
            return T("Use um link do mesmo mod no Nexus.","Use a link to the same Nexus mod.");
        return "";
    }

    GeneralModEntry Draft(string id) {
        SaveCurrent();
        return new GeneralModEntry {
            Id = id, Name = modName.Text.Trim(), Category = category.Text.Trim(), Description = description.Text.Trim(),
            Tags = selectedTags.Concat(new[] { tags.Text.Trim() }).Where(tag => tag.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Nsfw = adult, NsfwOverride=adultOverride, DetectedPaths=initialEntry.DetectedPaths.ToList(), CompatibilityGroups=legacyGroups, DefaultVariant = model[0].Id, Variants = model, DependencySource=dependencySource, DependencyFile=dependencyFile
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
        bool changed=!inventoryDraft||InventoryDraftChanged(draft);
        if(inventoryDraft)save.Text=changed?T("Incluir na lista e salvar","Add to list and save"):T("Salvar","Save");
        quickFill.Enabled=!finishing&&refreshLookup!=null&&current!=null&&
            SourceLinkWarning(sources.Text.Trim(),current).Length==0;
        save.Enabled=error.Length==0&&!finishing&&changed;
        install.Enabled=save.Enabled&&current!=null&&!GeneralModLibrary.NeedsDependencyVersion(draft,current)&&
            !(draft.DetectedPaths.Count>0&&current.Sources.Any(GeneralModDownload.IsInstalledSource));
        if (current != null && variants.SelectedIndex >= 0)
            variants.Items[variants.SelectedIndex] = current.Name;
    }

    bool InventoryDraftChanged(GeneralModEntry draft) {
        return draft.Name!=initialEntry.Name||draft.Category!=(initialEntry.Category??"")||
            draft.Description!=(initialEntry.Description??"")||draft.Nsfw!=initialEntry.Nsfw||
            !draft.Tags.SequenceEqual(initialEntry.Tags)||NewCategories.Count>0||LinkedAlternative!=null||
            draft.Variants.Count!=initialEntry.Variants.Count||draft.Variants.Zip(initialEntry.Variants,(after,before)=>
                after.Name!=before.Name||!after.Sources.SequenceEqual(before.Sources)||!after.RequiredMods.SequenceEqual(before.RequiredMods)).Any(changed=>changed);
    }

    async void Finish(string id, bool requested) {
        if(finishing)return;
        GeneralModEntry draft = Draft(id);
        if(inventoryDraft&&!InventoryDraftChanged(draft))return;
        string error = CurrentValidation(draft);
        if (error.Length != 0) { validation.Text = error; return; }
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


